function gadget:GetInfo()  return {    name      = "Terraform Map",    desc      = "Terraforms the map",    author    = "Google Frog",    date      = "14 May 2013",    license   = "GNU GPL, v2 or later",    layer     = 0,    enabled   = true  --  loaded by default?  }end--------------------------------------------------------------------------------------------------------------------------------------------------------------------------if (not gadgetHandler:IsSyncedCode()) then    returnend--------------------------------------------------------------------------------------------------------------------------------------------------------------------------include("LuaRules/Configs/customcmds.h.lua")local MAP_SIZE_X = Game.mapSizeXlocal MAP_SIZE_Z = Game.mapSizeZlocal SPACING = 1600local HALF_SPACE = SPACING/2local nanoDefID = UnitDefNames["armnanotc"].idlocal units = {}	function gadget:GameStart() -- GameStart	Spring.Echo("Map Terraformer Active")endlocal nx = HALF_SPACElocal nz = HALF_SPACElocal function setupTerraform(raise)	local nanoCreated = 0	while nx < MAP_SIZE_X do		while nz < MAP_SIZE_Z do			nanoCreated = nanoCreated + 1			if nanoCreated > 7 then				return false			end						local h = Spring.GetGroundHeight(nx,nz)			local unitID = Spring.CreateUnit(nanoDefID, nx, h, nz, 1, 1)						local params = {}			params[1] = 2 -- 1 = level, 2 = raise, 3 = smooth, 4 = ramp, 5 = restore			params[2] = 1 -- teamID of the team doing the terraform			params[3] = 1 -- true or false			params[4] = raise -- raise			params[5] = 5 -- how many points there are in the lasso (2 for ramp)			params[6] = 1 -- how many constructors are working on it			params[7] = 0 -- 0 = none, 1 = only raise, 2 = only lower			params[8] = nx - HALF_SPACE			params[9] = nz - HALF_SPACE			params[10] = nx + HALF_SPACE			params[11] = nz - HALF_SPACE			params[12] = nx + HALF_SPACE			params[13] = nz + HALF_SPACE			params[14] = nx - HALF_SPACE			params[15] = nz + HALF_SPACE			params[16] = nx - HALF_SPACE			params[17] = nz - HALF_SPACE			params[18] = unitID			params[19] = unitID			params[20] = unitID						units[#units + 1] = unitID						if unitID then				Spring.GiveOrderToUnit(unitID, CMD_TERRAFORM_INTERNAL, params, {})			else				Spring.MarkerAddPoint(nx,h,nz,"failure to make nano")			end			nz = nz + SPACING		end		nz = HALF_SPACE		nx = nx + SPACING	end	return trueendlocal unitsIndex = 1local function useTerraform()	local nanoCreated = 0	while nx < MAP_SIZE_X do		while nz < MAP_SIZE_Z do			nanoCreated = nanoCreated + 1			if nanoCreated > 7 then				return false			end						local unitID = units[unitsIndex]			unitsIndex = unitsIndex + 1						local params = {}			params[1] = 5 -- 1 = level, 2 = raise, 3 = smooth, 4 = ramp, 5 = restore			params[2] = 1 -- teamID of the team doing the terraform			params[3] = 1 -- true or false			params[4] = 0 -- raise			params[5] = 5 -- how many points there are in the lasso (2 for ramp)			params[6] = 1 -- how many constructors are working on it			params[7] = 0 -- 0 = none, 1 = only raise, 2 = only lower			params[8] = nx - HALF_SPACE			params[9] = nz - HALF_SPACE			params[10] = nx + HALF_SPACE			params[11] = nz - HALF_SPACE			params[12] = nx + HALF_SPACE			params[13] = nz + HALF_SPACE			params[14] = nx - HALF_SPACE			params[15] = nz + HALF_SPACE			params[16] = nx - HALF_SPACE			params[17] = nz - HALF_SPACE			params[18] = unitID			params[19] = unitID			params[20] = unitID						if unitID then				Spring.GiveOrderToUnit(unitID, CMD_TERRAFORM_INTERNAL, params, {})				Spring.MarkerAddPoint(nx,0,nz,"nano order Given")			else				Spring.MarkerAddPoint(nx,0,nz,"failure to give order")			end			nz = nz + SPACING		end		nz = HALF_SPACE		nx = nx + SPACING	end	return trueendlocal function spawnScout(unitName, x, z, face)	local h = Spring.GetGroundHeight(x,z)	local unitID = Spring.CreateUnit(unitName, x, h, z, face, 0)	if unitID then		Spring.GiveOrderToUnit(unitID, CMD.MOVE, {MAP_SIZE_X-x, Spring.GetGroundHeight(MAP_SIZE_X-x,z), z}, {})	else		Spring.MarkerAddPoint(x,h,z,"failure to make scout")	end	return unitIDendlocal SCOUT_SPACE = 800local function spawnUnitGrid(unitName)	for x = SCOUT_SPACE/2, MAP_SIZE_X, SCOUT_SPACE do		for z = SCOUT_SPACE/2, MAP_SIZE_Z, SCOUT_SPACE do			local h = Spring.GetGroundHeight(x,z)			local unitID = Spring.CreateUnit(unitName, x, h, z, 1, 0)		end	endendlocal done = falselocal done2 = truelocal destroyUnits = {}function gadget:GameFrame(f)		if f%30 == 5 and not done then		done = setupTerraform(10)	end		if f == 300 then		destroyUnits[1] = spawnScout("corawac", 10, MAP_SIZE_Z*0.65, 1)		destroyUnits[2] = spawnScout("corawac", MAP_SIZE_X - 10, MAP_SIZE_Z*0.88, 3)	end		if f == 1000 then		if destroyUnits[1] then			Spring.DestroyUnit(destroyUnits[1], false, true)		end		if destroyUnits[2] then			Spring.DestroyUnit(destroyUnits[2], false, true)		end		spawnUnitGrid("armmstor")		nx = HALF_SPACE		nz = HALF_SPACE		done2 = false		--spawnScout("armflea", MAP_SIZE_Z*0.1)		--spawnScout("armflea", MAP_SIZE_Z*0.25)		--spawnScout("armflea", MAP_SIZE_Z*0.4)	end		if f%30 == 5 and not done2 then		done2 = useTerraform()	endend