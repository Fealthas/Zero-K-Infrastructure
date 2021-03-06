using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using ZkData;

namespace ZkLobbyServer
{
    public class SteamWebApi
    {
        string webApiKey;
        int steamAppID;

        public SteamWebApi(int steamAppId, string webApiKey)
        {
            steamAppID = steamAppId;
            this.webApiKey = webApiKey;
        }


        public class PlayerSummariesResposne
        {
            public Response response { get; set; }

            public class Response
            {
                public List<PlayerInfo> players { get; set; }
            }
        }

        public class AuthenticateUserTicketResponse
        {
            public Response response { get; set; }
            public class Response
            {
                public Params @params { get; set; }
                public class Params
                {
                    public string result { get; set; }
                    public ulong steamid { get; set; }
                    public ulong ownersteamid { get; set; }
                }
            }
        }

        public SteamWebApi.PlayerInfo WebGetPlayerInfo(ulong steamID)
        {
            var wc = new WebClient();
            var ret =
                wc.DownloadString(
                    string.Format("https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={0}&steamids={1}", webApiKey, steamID));

            var response = JsonConvert.DeserializeObject<PlayerSummariesResposne>(ret);
            return response.response.players.FirstOrDefault();
        }

        public class PlayerInfo
        {
            public ulong steamid { get; set; }
            public string personaname { get; set; }
            public long lastlogoff { get; set; }
            public string profileurl { get; set; }
            public string avatar { get; set; }
            public string avatarmedium { get; set; }
            public string avatarfull { get; set; }
            public long primaryclanid { get; set; }
        }

        public ulong WebValidateAuthToken(string hexToken)
        {
            var wc = new WebClient();
            var ret =
                wc.DownloadString(string.Format(
                    "https://api.steampowered.com/ISteamUserAuth/AuthenticateUserTicket/V0001?key={0}&appid={1}&ticket={2}",
                    webApiKey,
                    steamAppID,
                    hexToken));
            var response = JsonConvert.DeserializeObject<AuthenticateUserTicketResponse>(ret);

            return response.response.@params.steamid;
        }


        public async Task<PlayerInfo> VerifyAndGetAccountInformation(string token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                try
                {
                    var steamID = WebValidateAuthToken(token);
                    var info = WebGetPlayerInfo(steamID);
                    info.steamid = steamID;
                    return info;
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Error linking steam account: {0}", ex);
                }
            }
            return null;
        }
    }
}


/*
 * ISteamUserStats
 * 
name: "SetUserStatsForGame",
version: 1,
httpmethod: "POST",
parameters: [
{
name: "key",
type: "string",
optional: false,
description: "access key"
},
{
name: "steamid",
type: "uint64",
optional: false,
description: "SteamID of user"
},
{
name: "appid",
type: "uint32",
optional: false,
description: "appid of game"
},
{
name: "count",
type: "uint32",
optional: false,
description: "Number of stats and achievements to set a value for (name/value param pairs)"
},
{
name: "name[0]",
type: "string",
optional: false,
description: "Name of stat or achievement to set"
},
{
name: "value[0]",
type: "uint32",
optional: false,
description: "Value to set"
}
]*/