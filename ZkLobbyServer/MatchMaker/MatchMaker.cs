﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using LobbyClient;
using PlasmaShared;
using ZkData;

namespace ZkLobbyServer
{
    public partial class MatchMaker
    {
        private const int TimerSeconds = 30;

        private const int BanSeconds = 30;

        private ConcurrentDictionary<string, DateTime> bannedPlayers = new ConcurrentDictionary<string, DateTime>();
        private Dictionary<string, int> ingameCounts = new Dictionary<string, int>();

        private List<ProposedBattle> invitationBattles = new List<ProposedBattle>();
        private ConcurrentDictionary<string, PlayerEntry> players = new ConcurrentDictionary<string, PlayerEntry>();
        private List<MatchMakerSetup.Queue> possibleQueues = new List<MatchMakerSetup.Queue>();

        private Dictionary<string, int> queuesCounts = new Dictionary<string, int>();

        private ZkLobbyServer server;


        private object tickLock = new object();
        private Timer timer;
        private int totalQueued;

        public MatchMaker(ZkLobbyServer server)
        {
            this.server = server;
            using (var db = new ZkDataContext())
            {
                possibleQueues.Add(new MatchMakerSetup.Queue()
                {
                    Name = "4v4",
                    Description = "Play 4v4 with players of similar skill.",
                    MaxPartySize = 4,
                    MinSize = 8,
                    MaxSize = 8,
                    EloCutOffExponent = 0.96,
                    Game = server.Game,
                    Mode = AutohostMode.Teams,
                    Maps =
                        db.Resources.Where(
                                x => (x.MapSupportLevel >= MapSupportLevel.MatchMaker) && (x.MapIsTeams != false) && (x.TypeID == ResourceType.Map) && x.MapIsSpecial != true)
                            .Select(x => x.InternalName)
                            .ToList()
                });

                possibleQueues.Add(new MatchMakerSetup.Queue()
                {
                    Name = "3v3",
                    Description = "Play 3v3 with players of similar skill.",
                    MaxPartySize = 3,
                    MinSize = 6,
                    MaxSize = 6,
                    EloCutOffExponent = 0.965,
                    Game = server.Game,
                    Mode = AutohostMode.Teams,
                    Maps =
                        db.Resources.Where(
                                x => (x.MapSupportLevel >= MapSupportLevel.MatchMaker) && (x.MapIsTeams != false) && (x.TypeID == ResourceType.Map) && x.MapIsSpecial != true)
                            .Select(x => x.InternalName)
                            .ToList()
                });

                possibleQueues.Add(new MatchMakerSetup.Queue()
                {
                    Name = "2v2",
                    Description = "Play 2v2 with players of similar skill.",
                    MaxPartySize = 2,
                    MinSize = 4,
                    MaxSize = 4,
                    EloCutOffExponent = 0.97,
                    Game = server.Game,
                    Mode = AutohostMode.Teams,
                    Maps =
                        db.Resources.Where(
                                x => (x.MapSupportLevel >= MapSupportLevel.MatchMaker) && (x.MapIsTeams != false) && (x.TypeID == ResourceType.Map) && x.MapIsSpecial != true)
                            .Select(x => x.InternalName)
                            .ToList()
                });

                possibleQueues.Add(new MatchMakerSetup.Queue()
                {
                    Name = "1v1",
                    Description = "Duel an opponent of similar skill in a 1v1 match.",
                    MaxPartySize = 1,
                    MinSize = 2,
                    MaxSize = 2,
                    EloCutOffExponent = 0.98,
                    Game = server.Game,
                    Maps =
                        db.Resources.Where(
                                x => (x.MapSupportLevel >= MapSupportLevel.MatchMaker) && (x.MapIs1v1 == true) && (x.TypeID == ResourceType.Map) && x.MapIsSpecial != true)
                            .Select(x => x.InternalName)
                            .ToList(),
                    Mode = AutohostMode.Game1v1,
                });
            }
            timer = new Timer(TimerSeconds * 1000);
            timer.AutoReset = true;
            timer.Elapsed += TimerTick;
            timer.Start();

            queuesCounts = CountQueuedPeople(players.Values);
            ingameCounts = CountIngamePeople();
        }


        public async Task AreYouReadyResponse(ConnectedUser user, AreYouReadyResponse response)
        {
            PlayerEntry entry;
            if (players.TryGetValue(user.Name, out entry))
                if (entry.InvitedToPlay)
                {
                    if (response.Ready) entry.LastReadyResponse = true;
                    else
                    {
                        entry.LastReadyResponse = false;
                        await RemoveUser(user.Name, true);
                    }

                    var invitedPeople = players.Values.Where(x => x?.InvitedToPlay == true).ToList();

                    if (invitedPeople.Count <= 1)
                    {
                        foreach (var p in invitedPeople) p.LastReadyResponse = true;
                        // if we are doing tick because too few people, make sure we count remaining people as readied to not ban them 
                        OnTick();
                    }
                    else if (invitedPeople.All(x => x.LastReadyResponse)) OnTick();
                    else
                    {
                        var readyCounts = CountQueuedPeople(invitedPeople.Where(x => x.LastReadyResponse));

                        var proposedBattles = ProposeBattles(invitedPeople.Where(x => x.LastReadyResponse));

                        await Task.WhenAll(invitedPeople.Select(async (p) =>
                        {
                            var invitedBattle = invitationBattles?.FirstOrDefault(x => x.Players.Contains(p));
                            await
                                server.SendToUser(p.Name,
                                    new AreYouReadyUpdate()
                                    {
                                        QueueReadyCounts = readyCounts,
                                        ReadyAccepted = p.LastReadyResponse == true,
                                        LikelyToPlay = proposedBattles.Any(y => y.Players.Contains(p)),
                                        YourBattleSize = invitedBattle?.Size,
                                        YourBattleReady =
                                            invitedPeople.Count(x => x.LastReadyResponse && (invitedBattle?.Players.Contains(x) == true))
                                    });
                        }));
                    }
                }
        }

        public int GetTotalWaiting() => totalQueued;


        public async Task OnLoginAccepted(ConnectedUser conus)
        {
            await conus.SendCommand(new MatchMakerSetup() { PossibleQueues = possibleQueues });
            await UpdatePlayerStatus(conus.Name);
        }

        public async Task OnServerGameChanged(string game)
        {
            foreach (var pq in possibleQueues) pq.Game = game;
            await server.Broadcast(new MatchMakerSetup() { PossibleQueues = possibleQueues });
        }

        public async Task QueueRequest(ConnectedUser user, MatchMakerQueueRequest cmd)
        {
            var banTime = BannedSeconds(user.Name);
            if (banTime != null)
            {
                await UpdatePlayerStatus(user.Name);
                await user.Respond($"Please rest and wait for {banTime}s because you refused previous match");
                return;
            }

            // already invited ignore requests
            PlayerEntry entry;
            if (players.TryGetValue(user.Name, out entry) && entry.InvitedToPlay)
            {
                await UpdatePlayerStatus(user.Name);
                return;
            }

            var wantedQueueNames = cmd.Queues?.ToList() ?? new List<string>();
            var wantedQueues = possibleQueues.Where(x => wantedQueueNames.Contains(x.Name)).ToList();

            var party = server.PartyManager.GetParty(user.Name);
            if (party != null) wantedQueues = wantedQueues.Where(x => x.MaxSize/2 >= party.UserNames.Count).ToList(); // if is in party keep only queues where party fits

            if (wantedQueues.Count == 0) // delete
            {
                await RemoveUser(user.Name, true);
                return;
            }

            await AddOrUpdateUser(user, wantedQueues);
        }


        /// <summary>
        /// Removes user (and his party) from MM queues
        /// </summary>
        /// <param name="name"></param>
        /// <param name="broadcastChanges">should change be broadcasted/statuses updated</param>
        /// <returns></returns>
        public async Task RemoveUser(string name, bool broadcastChanges)
        {
            var party = server.PartyManager.GetParty(name);
            var anyRemoved = false;

            if (party != null)
            {
                foreach (var n in party.UserNames) if (await RemoveSingleUser(n)) anyRemoved = true;
            }
            else
            {
                anyRemoved = await RemoveSingleUser(name);
            }
            if (broadcastChanges && anyRemoved) await UpdateAllPlayerStatuses();
        }

        public async Task UpdateAllPlayerStatuses()
        {
            ingameCounts = CountIngamePeople();
            queuesCounts = CountQueuedPeople(players.Values);

            await Task.WhenAll(server.ConnectedUsers.Keys.Where(x => x != null).Select(UpdatePlayerStatus));
        }

        private async Task AddOrUpdateUser(ConnectedUser user, List<MatchMakerSetup.Queue> wantedQueues)
        {
            var party = server.PartyManager.GetParty(user.Name);
            if (party != null)
                foreach (var p in party.UserNames)
                {
                    var conUs = server.ConnectedUsers.Get(p);
                    if (conUs != null)
                        players.AddOrUpdate(p,
                            (str) => new PlayerEntry(conUs.User, wantedQueues, party),
                            (str, usr) =>
                            {
                                usr.UpdateTypes(wantedQueues);
                                usr.Party = party;
                                return usr;
                            });
                }
            else
                players.AddOrUpdate(user.Name,
                    (str) => new PlayerEntry(user.User, wantedQueues, null),
                    (str, usr) =>
                    {
                        usr.UpdateTypes(wantedQueues);
                        usr.Party = null;
                        return usr;
                    });


            // if nobody is invited, we can do tick now to speed up things
            if (invitationBattles?.Any() != true) OnTick();
            else await UpdateAllPlayerStatuses(); // else we just send statuses
        }


        private int? BannedSeconds(string name)
        {
            DateTime banEntry;
            if (bannedPlayers.TryGetValue(name, out banEntry) && (DateTime.UtcNow.Subtract(banEntry).TotalSeconds < BanSeconds)) return (int)(BanSeconds - DateTime.UtcNow.Subtract(banEntry).TotalSeconds);
            else bannedPlayers.TryRemove(name, out banEntry);
            return null;
        }

        private Dictionary<string, int> CountIngamePeople()
        {
            var ncounts = possibleQueues.ToDictionary(x => x.Name, x => 0);
            foreach (var bat in server.Battles.Values.OfType<MatchMakerBattle>().Where(x => (x != null) && x.IsMatchMakerBattle && x.IsInGame))
            {
                var plrs = bat.spring?.Context?.LobbyStartContext?.Players.Count(x => !x.IsSpectator) ?? 0;
                if (plrs > 0)
                {
                    var type = bat.Prototype?.QueueType;
                    if (type != null) ncounts[type.Name] += plrs;
                }
            }
            return ncounts;
        }

        private Dictionary<string, int> CountQueuedPeople(IEnumerable<PlayerEntry> sumPlayers)
        {
            int total = 0;
            var ncounts = possibleQueues.ToDictionary(x => x.Name, x => 0);
            foreach (var plr in sumPlayers.Where(x => x != null))
            {
                total++;
                foreach (var jq in plr.QueueTypes) ncounts[jq.Name]++;
            }
            totalQueued = total; // ugly to both return and set class property, refactor for nicer
            return ncounts;
        }

        public Dictionary<string, int> GetQueueCounts() => queuesCounts;

        private void OnTick()
        {
            lock (tickLock)
            {
                try
                {
                    timer.Stop();
                    var realBattles = ResolveToRealBattles();

                    UpdateAllPlayerStatuses();

                    foreach (var bat in realBattles) StartBattle(bat);

                    ResetAndSendMmInvitations();
                }
                catch (Exception ex)
                {
                    Trace.TraceError("MatchMaker tick error: {0}", ex);
                }
                finally
                {
                    timer.Start();
                }
            }
        }

        private static List<ProposedBattle> ProposeBattles(IEnumerable<PlayerEntry> users)
        {
            var proposedBattles = new List<ProposedBattle>();

            var usersByWaitTime = users.OrderBy(x => x.JoinedTime).ToList();
            var remainingPlayers = usersByWaitTime.ToList();

            foreach (var user in usersByWaitTime)
                if (remainingPlayers.Contains(user)) // consider only those not yet assigned
                {
                    var battle = TryToMakeBattle(user, remainingPlayers);
                    if (battle != null)
                    {
                        proposedBattles.Add(battle);
                        remainingPlayers.RemoveAll(x => battle.Players.Contains(x));
                    }
                }

            return proposedBattles;
        }


        private async Task<bool> RemoveSingleUser(string name)
        {
            PlayerEntry entry;
            if (players.TryRemove(name, out entry))
            {
                if (entry.InvitedToPlay) bannedPlayers[entry.Name] = DateTime.UtcNow; // was invited but he is gone now (whatever reason), ban!

                ConnectedUser conUser;
                if (server.ConnectedUsers.TryGetValue(name, out conUser) && (conUser != null)) if (entry?.InvitedToPlay == true) await conUser.SendCommand(new AreYouReadyResult() { AreYouBanned = true, IsBattleStarting = false, });
                return true;
            }
            return false;
        }

        private void ResetAndSendMmInvitations()
        {
            // generate next battles and send inviatation
            invitationBattles = ProposeBattles(players.Values.Where(x => x != null));
            var toInvite = invitationBattles.SelectMany(x => x.Players).ToList();
            foreach (var usr in players.Values.Where(x => x != null))
                if (toInvite.Contains(usr))
                {
                    usr.InvitedToPlay = true;
                    usr.LastReadyResponse = false;
                }
                else
                {
                    usr.InvitedToPlay = false;
                    usr.LastReadyResponse = false;
                }

            server.Broadcast(toInvite.Select(x => x.Name), new AreYouReady() { SecondsRemaining = TimerSeconds });
        }

        private List<ProposedBattle> ResolveToRealBattles()
        {
            var lastMatchedUsers = players.Values.Where(x => x?.InvitedToPlay == true).ToList();

            // force leave those not ready
            foreach (var pl in lastMatchedUsers.Where(x => !x.LastReadyResponse)) RemoveUser(pl.Name, false);

            var readyUsers = lastMatchedUsers.Where(x => x.LastReadyResponse).ToList();
            var realBattles = ProposeBattles(readyUsers);

            var readyAndStarting = readyUsers.Where(x => realBattles.Any(y => y.Players.Contains(x))).ToList();
            var readyAndFailed = readyUsers.Where(x => !realBattles.Any(y => y.Players.Contains(x))).Select(x => x.Name);

            server.Broadcast(readyAndFailed, new AreYouReadyResult() { IsBattleStarting = false });

            server.Broadcast(readyAndStarting.Select(x => x.Name), new AreYouReadyResult() { IsBattleStarting = true });

            foreach (var usr in readyAndStarting)
            {
                PlayerEntry entry;
                players.TryRemove(usr.Name, out entry);
            }

            return realBattles;
        }

        private async Task StartBattle(ProposedBattle bat)
        {
            var battle = new MatchMakerBattle(server, bat);
            server.Battles[battle.BattleID] = battle;

            // also join in lobby
            await server.Broadcast(server.ConnectedUsers.Keys, new BattleAdded() { Header = battle.GetHeader() });
            foreach (var usr in bat.Players) await server.ForceJoinBattle(usr.Name, battle);

            await battle.StartGame();
        }


        private void TimerTick(object sender, ElapsedEventArgs elapsedEventArgs)
        {
            OnTick();
        }


        private static ProposedBattle TryToMakeBattle(PlayerEntry player, IList<PlayerEntry> otherPlayers)
        {
            var allPlayers = new List<PlayerEntry>();
            allPlayers.AddRange(otherPlayers);
            allPlayers.Add(player);

            var playersByElo =
                otherPlayers.Where(x => x != player)
                    .OrderBy(x => Math.Abs(x.LobbyUser.EffectiveMmElo - player.LobbyUser.EffectiveMmElo))
                    .ThenBy(x => x.JoinedTime)
                    .ToList();

            var testedBattles = player.GenerateWantedBattles(allPlayers);

            foreach (var other in playersByElo)
                foreach (var bat in testedBattles)
                {
                    if (bat.CanBeAdded(other, allPlayers)) bat.AddPlayer(other, allPlayers);
                    if (bat.Players.Count == bat.Size) return bat;
                }
            return null;
        }


        private async Task UpdatePlayerStatus(string name)
        {
            ConnectedUser conus;
            if (server.ConnectedUsers.TryGetValue(name, out conus))
            {
                PlayerEntry entry;
                players.TryGetValue(name, out entry);
                var ret = new MatchMakerStatus()
                {
                    QueueCounts = queuesCounts,
                    IngameCounts = ingameCounts,
                    JoinedQueues = entry?.QueueTypes.Select(x => x.Name).ToList(),
                    CurrentEloWidth = entry?.EloWidth,
                    JoinedTime = entry?.JoinedTime,
                    BannedSeconds = BannedSeconds(name),
                    UserCount = server.ConnectedUsers.Count
                };


                // check for instant battle start - only non partied people
                if ((invitationBattles?.Any() != true) && (players.Count > 0) && (server.PartyManager.GetParty(name) == null))
                // nobody invited atm and some in queue
                {
                    // get all currently queued players except for self
                    var testPlayers = players.Values.Where(x => (x != null) && (x.Name != name)).ToList();
                    var testSelf = new PlayerEntry(conus.User, possibleQueues.ToList(), null); // readd self but with all queues
                    testPlayers.Add(testSelf);
                    var testBattles = ProposeBattles(testPlayers);
                    ret.InstantStartQueues = testBattles.Where(x => x.Players.Contains(testSelf)).Select(x => x.QueueType.Name).Distinct().ToList();
                }

                await conus.SendCommand(ret);
            }
        }
    }
}