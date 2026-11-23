-- PesPresence.lua
-- Real-time Rich Presence bridge for eFootball PES 2021 (Sider 7)
-- Automatically exports live match scores, clock, tournament, stage, and game mode to pes_rpc.json

local m = { version = "1.5.0" }
local ffi = ffi

local last_write = 0
local WRITE_INTERVAL = 1.0 -- seconds
local g_sider_dir = ".\\"

local COMP_MAP = {
    [2] = "Champions League", [3] = "Champions League", [4] = "Champions League",
    [1026] = "Champions League", [2050] = "Champions League", [3074] = "Champions League",
    [4098] = "Champions League", [5122] = "Champions League", [6146] = "Champions League",
    [7170] = "Champions League", [8194] = "Champions League",
    [1027] = "Champions League", [2051] = "Champions League", [3075] = "Champions League",
    [4099] = "Champions League", [5123] = "Champions League", [6147] = "Champions League",
    [7171] = "Champions League", [8195] = "Champions League",
    [5] = "Europa League", [1029] = "Europa League", [2053] = "Europa League",
    [3077] = "Europa League", [4101] = "Europa League", [5125] = "Europa League",
    [6149] = "Europa League", [7173] = "Europa League", [8197] = "Europa League",
    [9221] = "Europa League",
    [19] = "La Liga", [80] = "LaLiga Hypermotion", [84] = "LaLiga Hypermotion",
    [22] = "Primeira Liga",
    [29] = "Brasileirao Serie A", [2077] = "Brasileirao Serie A",
    [163] = "Brasileirao Serie B", [68] = "Brasileirao Serie B",
    [67] = "Brasileirao Serie C", [54] = "Brasileirao Serie C",
    [120] = "Brasileirao Serie D", [165] = "Brasileirao Serie D",
    [31] = "Copa do Brasil", [1055] = "Copa do Brasil",
    [127] = "Copa Verde",
    [30] = "Liga Profesional",
    [50] = "Bundesliga",
    [65535] = "Amistoso"
}

-- Baseline clubs & national teams
local team_map = {
    [100] = "Manchester United", [101] = "Arsenal", [102] = "Chelsea", [103] = "Liverpool",
    [104] = "Leeds United", [105] = "West Ham", [106] = "Newcastle United", [107] = "Aston Villa",
    [108] = "Barcelona", [109] = "Real Madrid", [110] = "Valencia", [114] = "Paris Saint-Germain",
    [116] = "Ajax", [117] = "Feyenoord", [119] = "Inter de Milao", [121] = "AC Milan",
    [125] = "Roma", [127] = "Bayern de Munique", [131] = "Celtic", [133] = "Olympiacos",
    [136] = "Vasco da Gama", [137] = "Palmeiras", [138] = "River Plate", [139] = "Boca Juniors",
    [172] = "Atletico de Madrid", [173] = "Manchester City", [177] = "Everton", [179] = "Tottenham",
    [204] = "Leicester City", [265] = "Sevilla", [274] = "Cruzeiro", [1245] = "Atletico Mineiro",
    [1246] = "Botafogo", [1247] = "Corinthians", [1248] = "Flamengo", [1249] = "Fluminense",
    [1250] = "Gremio", [1252] = "Internacional", [1255] = "Sao Paulo", [1930] = "Athletico-PR",
    [1937] = "Vitoria", [2451] = "Atletico-GO", [2453] = "Bahia", [2459] = "Red Bull Bragantino",
    [5137] = "Juventude", [5142] = "Cuiaba", [5143] = "Fortaleza", [5145] = "Criciuma"
}

local coach_map = {
    [108] = "Hansi Flick",
    [109] = "Carlo Ancelotti",
    [100] = "Rúben Amorim",
    [101] = "Mikel Arteta",
    [103] = "Arne Slot",
    [173] = "Pep Guardiola",
    [137] = "Abel Ferreira",
    [1248] = "Filipe Luís",
    [1255] = "Dorival Jr",
    [1245] = "Eduardo Domínguez",
    [1247] = "Ramón Díaz",
    [2453] = "Rogério Ceni",
    [1250] = "Renato Gaúcho",
    [1252] = "Roger Machado",
    [1246] = "Artur Jorge",
    [1249] = "Mano Menezes",
    [136] = "Rafael Paiva",
    [274] = "Fernando Diniz",
    [1930] = "Lucho González"
}

local current_home = ""
local current_away = ""
local detected_mode = "Menu Principal"
local detected_player = ""
local detected_coach = ""
local last_home_id = 0
local last_away_id = 0

local sys_status_ptr_addr = nil
local sys_status_searched = false

-- Exact sys::Status::GetGameMode() enum (PES 2021 0x00..0x3C)
local GAME_MODE_MAP = {
    [0]  = "Menu Principal",    -- UNKNOWN
    [1]  = "Menu Principal",    -- TEST_MENU
    [2]  = "Modo Online",       -- TEST_ONLINE
    [3]  = "Modo Amistoso",     -- TEST_1ON1
    [4]  = "Menu Principal",    -- BOOT_STEP
    [5]  = "Menu Principal",    -- TITLE_DEMO_LOOP
    [6]  = "Menu Principal",    -- FIRST_SETTINGS
    [7]  = "Menu Principal",    -- TOP_MENU
    [8]  = "Modo Amistoso",     -- EXHIBITION
    [9]  = "Modo Copa",         -- COMPETITION (Cup / Tournament)
    [10] = "Modo Amistoso",     -- PD_VERSUS
    [11] = "Modo Amistoso",     -- RS_MATCH
    [12] = "Modo Amistoso",     -- COOP_MATCH
    [13] = "Menu Principal",    -- EDIT (Ajuste Personalizado)
    [14] = "Menu Principal",    -- EDIT_LIVEUPDATE
    [15] = "Modo Copa",         -- UEFA_CL
    [16] = "Modo Copa",         -- UEFA_EL
    [17] = "Modo Copa",         -- UEFA_EURO
    [18] = "Modo Liga",         -- UEFA_LEAGUE (Campeonato / Liga)
    [19] = "Master Liga",       -- UEFA_ML
    [20] = "Rumo ao Estrelato", -- UEFA_BL
    [21] = "Modo Copa",         -- CONMEBOL_LB
    [22] = "Modo Copa",         -- CONMEBOL_SD
    [23] = "Modo Copa",         -- AFC_ACL
    [24] = "Modo Online",       -- ONLINE_RANK
    [25] = "Modo Online",       -- ONLINE_QUICK
    [26] = "Modo Online",       -- ONLINE_LOBBY
    [27] = "Modo Online",       -- ONLINE_MOM_LOBBY
    [28] = "Modo Online",       -- ONLINE_COOP_MATCH
    [29] = "Modo Online",       -- ONLINE_WATCHMATCH
    [30] = "myClub",            -- MYCLUB
    [31] = "myClub",            -- MYCLUB_RANK
    [32] = "myClub",            -- MYCLUB_COM_MATCH
    [33] = "myClub",            -- MYCLUB_COM_COACH
    [34] = "myClub",            -- MYCLUB_COM_COMPE
    [35] = "myClub",            -- MYCLUB_LOBBY
    [36] = "myClub",            -- MYCLUB_EVENT_COMPE
    [37] = "myClub",            -- MYCLUB_COOP_MATCH
    [38] = "myClub",            -- MYCLUB_EVENT_COMPE_COOP
    [39] = "myClub",            -- MYCLUB_COM_COMPE_COOP
    [40] = "myClub",            -- MYCLUB_PES_LEAGUE_MATCHING
    [41] = "myClub",            -- MYCLUB_PES_LEAGUE_CLAN_COOP_MATCHING
    [42] = "myClub",            -- MYCLUB_PES_LEAGUE_LOBBY
    [43] = "myClub",            -- MYCLUB_MATCHDAY
    [44] = "myClub",            -- MYCLUB_MATCHDAY_COOP
    [45] = "Menu Principal",    -- STATS_AND_INFO (Replay / Conquista / Info)
    [46] = "Menu Principal",    -- STATS_AND_INFO_INFO
    [47] = "Menu Principal",    -- STATS_AND_INFO_DP
    [48] = "Menu Principal",    -- STATS_AND_INFO_LIVE
    [49] = "Menu Principal",    -- ONLINE_SETTINGS
    [50] = "Modo Online",       -- ONLINE_EVENT
    [51] = "Modo Treino",       -- STEPUP_TUTORIAL (Treino de Habilidades)
    [52] = "Modo Treino",       -- FREE_TRAINING (Treino Livre)
    [53] = "Menu Principal",    -- SYSTEM_SETTINGS
    [54] = "PES League",        -- PES_LEAGUE
    [55] = "PES League",        -- PES_LEAGUE_MATCHING
    [56] = "PES League",        -- PES_LEAGUE_CLAN_COOP_MATCHING
    [57] = "PES League",        -- PES_LEAGUE_RANDOM_MATCHING
    [58] = "PES League",        -- PES_LEAGUE_LOBBY
    [59] = "Matchday",          -- MATCHDAY
    [60] = "Matchday",          -- MATCHDAY_COOP
}

local function resolve_sys_status_ptr()
    if sys_status_ptr_addr then return sys_status_ptr_addr end
    if sys_status_searched then return nil end
    sys_status_searched = true

    if not memory or not memory.read then return nil end

    -- Fast CPY signature check
    local sig = memory.read(0x140C553A0, 8)
    if sig == "\x48\x8d\x05\x01\x04\xaa\x02\xc3" then
        sys_status_ptr_addr = 0x143704E38
        log("PesPresence: sys::Status resolved (CPY)")
        return sys_status_ptr_addr
    end

    -- Generic AOB scan (works on Steam + CPY)
    if memory.search_process then
        local patterns = {
            "\x8b\x88\xf0\x00\x00\x00\x83\xc1\xe8\x83\xf9\x20\x0f\x87",
            "\x8b\x88\xf0\x00\x00\x00\x83\xf9\x27\x77\x10\x48\xb8\x00\x00\x00\x00\xd4\x00\x00\x00"
        }
        for _, pat in ipairs(patterns) do
            local addr = memory.search_process(pat)
            if addr then
                local call_bytes = memory.read(addr - 5, 5)
                if type(call_bytes) == "string" and #call_bytes == 5 and call_bytes:byte(1) == 0xE8 then
                    local rel = tonumber(memory.unpack("i32", call_bytes:sub(2, 5)))
                    local fn = addr + rel
                    local fn_bytes = memory.read(fn, 8)
                    if type(fn_bytes) == "string" and #fn_bytes == 8
                        and fn_bytes:sub(1, 3) == "\x48\x8b\x05"
                        and fn_bytes:byte(8) == 0xC3 then
                        local disp = tonumber(memory.unpack("i32", fn_bytes:sub(4, 7)))
                        sys_status_ptr_addr = fn + 7 + disp
                        log(string.format("PesPresence: sys::Status resolved via AOB at 0x%X", sys_status_ptr_addr))
                        return sys_status_ptr_addr
                    end
                end
            end
        end
    end

    log("PesPresence: sys::Status AOB not found, falling back to Menu Principal")
    return nil
end

local function detect_game_mode_from_memory()
    local ptr_addr = resolve_sys_status_ptr()
    if not ptr_addr then return nil end

    local raw_inst = memory.read(ptr_addr, 8)
    if type(raw_inst) ~= "string" or #raw_inst ~= 8 then return nil end

    local inst = tonumber(memory.unpack("u64", raw_inst))
    if not inst or inst < 0x10000 then return nil end

    local raw_gm = memory.read(inst + 0xF0, 4)
    if type(raw_gm) ~= "string" or #raw_gm ~= 4 then return nil end

    local gm = tonumber(memory.unpack("u32", raw_gm))
    if not gm then return nil end

    return GAME_MODE_MAP[gm] or "Menu Principal"
end

local function clean_str(s)
    if not s or type(s) ~= "string" then return "" end
    local clean = string.match(s, '[^%z]*')
    if not clean then return "" end
    return (clean:gsub('^%s*(.-)%s*$', '%1'))
end

local function escape_json(s)
    if not s or type(s) ~= "string" then return "" end
    return s:gsub('\\', '\\\\'):gsub('"', '\\"'):gsub('\n', '\\r')
end

local function clean_coach_name(raw)
    if not raw or type(raw) ~= "string" then return "" end
    raw = raw:gsub('^"', ''):gsub('"$', '')
    raw = raw:gsub("^[^\\]*\\", "") -- remove folder prefix e.g. "BRASIL\"
    raw = raw:gsub("^[A-Z0-9]+%s*-%s*", "") -- remove club prefix e.g. "SAO - "
    raw = raw:gsub("%s+[vV]%d+.*$", "") -- remove " v2", " v3"
    raw = raw:gsub("%s+TERNO.*$", "")
    raw = raw:gsub("^%s+", ""):gsub("%s+$", "")
    return raw
end

local function load_team_and_coach_databases(sider_dir)
    if not sider_dir then return end

    -- Load team names
    local paths = {
        sider_dir .. "\\BMPES\\Servidor de Comentarios\\map_teams.txt",
        sider_dir .. "\\BMPES\\Servidor de Badge\\map_teams.txt",
        sider_dir .. "\\BMPES\\Servidor de Estadios\\map_teams.txt"
    }

    for _, p in ipairs(paths) do
        local f = io.open(p, "r")
        if f then
            for line in f:lines() do
                line = line:gsub("^%s+", ""):gsub("%s+$", "")
                if not line:match("^#") and not line:match("^;") and line ~= "" then
                    local tid1, name1 = line:match("^(%d+),%s*\"([^\"]+)\"")
                    if tid1 and name1 then
                        local id = tonumber(tid1)
                        if id and not team_map[id] then
                            team_map[id] = name1
                        end
                    else
                        local tid2, name2 = line:match("^(%d+),%s*[^,]*\\([^\\,]+)%.mp3")
                        if not name2 then
                            tid2, name2 = line:match("^(%d+),%s*[^,]*\\([^\\,]+)%.")
                        end
                        if tid2 and name2 then
                            local id = tonumber(tid2)
                            if id and not team_map[id] then
                                team_map[id] = name2
                            end
                        end
                    end
                end
            end
            f:close()
        end
    end

    -- Load coach database
    local coach_map_path = sider_dir .. "\\BMPES\\Servidor de Treinadores\\Real_Coach\\map.txt"
    local cf = io.open(coach_map_path, "r")
    if cf then
        for line in cf:lines() do
            line = line:gsub("^%s+", ""):gsub("%s+$", "")
            if not line:match("^#") and not line:match("^;") and line ~= "" then
                -- Format: <team_id>, <weather>, <folder>
                local tid, weather, folder = line:match("^(%d+),%s*(%d+),%s*\"([^\"]+)\"")
                if tid and folder then
                    local id = tonumber(tid)
                    if id and not coach_map[id] then
                        local cname = clean_coach_name(folder)
                        if cname ~= "" then
                            coach_map[id] = cname
                        end
                    end
                end
            end
        end
        cf:close()
    end
end

local function get_team_name(tid, fallback)
    if not tid or tid == 0 then return fallback or "" end
    if team_map[tid] then return team_map[tid] end
    return fallback or ("Time " .. tostring(tid))
end

local function get_coach_name(tid, fallback)
    if not tid or tid == 0 then return fallback or "" end
    if coach_map[tid] then return coach_map[tid] end
    return fallback or ""
end

local function detect_names_from_mis(ctx)
    if ctx and ctx.mis and ffi and memory then
        local mis_ptr = ctx.mis
        if mis_ptr then
            local p_cast = ffi.cast("char*", mis_ptr)
            if p_cast ~= nil then
                local h = memory.read(p_cast + 0x13c, 0x46)
                local a = memory.read(p_cast + 0x690 + 0x13c, 0x46)
                local ch = clean_str(h)
                local ca = clean_str(a)
                if ch ~= "" then current_home = ch end
                if ca ~= "" then current_away = ca end
            end
        end
    end
end

local global_ptr_addr = nil
local global_ptr_searched = false

local function read_u64(addr)
    if not addr or addr < 0x10000 then return nil end
    local b = memory.read(addr, 8)
    if type(b) ~= "string" or #b ~= 8 then return nil end
    return tonumber(memory.unpack("u64", b))
end

local function read_u32(addr)
    if not addr or addr < 0x10000 then return nil end
    local b = memory.read(addr, 4)
    if type(b) ~= "string" or #b ~= 4 then return nil end
    return tonumber(memory.unpack("u32", b))
end

local function read_u16(addr)
    if not addr or addr < 0x10000 then return nil end
    local b = memory.read(addr, 2)
    if type(b) ~= "string" or #b ~= 2 then return nil end
    return tonumber(memory.unpack("u16", b))
end

local function resolve_global_ptr()
    if global_ptr_addr then return global_ptr_addr end
    if not memory or not memory.read then return nil end

    for _, cand in ipairs({ 0x143705e10, 0x143714240 }) do
        local v = read_u64(cand)
        if v and v >= 0x10000 then
            global_ptr_addr = cand
            log(string.format("PesPresence: global_ptr resolved at 0x%X", cand))
            return global_ptr_addr
        end
    end

    if not global_ptr_searched and memory.search_process then
        global_ptr_searched = true
        local loc = memory.search_process("\x48\x8b\x05\xe9\x7c\x25\x02\xc3")
        if loc then
            local disp_bytes = memory.read(loc + 3, 4)
            if type(disp_bytes) == "string" and #disp_bytes == 4 then
                local disp = tonumber(memory.unpack("i32", disp_bytes))
                global_ptr_addr = loc + 7 + disp
                return global_ptr_addr
            end
        end
    end

    return nil
end

local function get_career_ctx_and_blk()
    local gp = resolve_global_ptr()
    if not gp then return nil, nil end
    local ctx = read_u64(gp)
    if not ctx or ctx < 0x10000 then return nil, nil end
    local blk = read_u64(ctx + 0x48)
    if not blk or blk < 0x10000 then return ctx, nil end
    return ctx, blk
end

-- Try reading BAL created player name and club from memory
local detected_bal_club = 0
local team_abbr_offset = nil
local team_abbr_logged = false

local function get_team_struct_addr(blk, team_raw)
    if not blk or not team_raw or team_raw == 0 or team_raw == 0xffffffff then return nil end
    local idx = team_raw % 16384
    local nt = read_u32(blk + 0xd0bcec)
    if nt and idx < nt then
        return blk + 0xadf4bc + idx * 0x690
    end
    return nil
end

local function is_national_team_struct(team_addr)
    if not team_addr then return false end
    local b = memory.read(team_addr + 0x41c, 1)
    if type(b) == "string" and #b == 1 then
        return (b:byte(1) % 128) <= 4
    end
    return false
end

local function get_team_abbr_from_struct(team_addr)
    if not team_addr or not memory or not memory.read then return "" end
    if team_abbr_offset then
        local raw = memory.read(team_addr + team_abbr_offset, 8)
        local s = clean_str(raw)
        if s:match("^[A-Z0-9][A-Z0-9][A-Z0-9]$") then
            return s
        end
    end

    local buf = memory.read(team_addr + 0x180, 0xb0)
    if type(buf) == "string" and #buf >= 8 then
        for i = 2, #buf - 4 do
            if buf:byte(i - 1) == 0 and buf:byte(i + 3) == 0 then
                local cand = buf:sub(i, i + 2)
                if cand:match("^[A-Z0-9][A-Z0-9][A-Z0-9]$") then
                    team_abbr_offset = 0x180 + (i - 1)
                    if not team_abbr_logged then
                        team_abbr_logged = true
                        log(string.format("PesPresence: team_abbr offset discovered at +0x%X (%s)", team_abbr_offset, cand))
                    end
                    return cand
                end
            end
        end
    end

    local full = clean_str(memory.read(team_addr + 0x13c, 0x46))
    return full
end

local function detect_bal_player_from_memory()
    if not memory or not memory.read then return "" end
    local _, blk = get_career_ctx_and_blk()
    if not blk then return "" end

    local rec_size = 0x17c
    local chunk_recs = 500
    for start_idx = 0, 24500, chunk_recs do
        local buf = memory.read(blk + start_idx * rec_size, chunk_recs * rec_size)
        if type(buf) ~= "string" or #buf < rec_size then
            break
        end
        local count = math.floor(#buf / rec_size)
        for r = 0, count - 1 do
            local off = r * rec_size
            local pid = tonumber(memory.unpack("u32", buf:sub(off + 0x31, off + 0x34)))
            if pid and pid >= 0x70000000 and pid ~= 0xffffffff then
                local name_str = clean_str(buf:sub(off + 0x39, off + 0x38 + 61))
                local club_raw = tonumber(memory.unpack("u32", buf:sub(off + 0x165, off + 0x168)))
                if club_raw and club_raw ~= 0xffffffff then
                    detected_bal_club = club_raw
                end
                if name_str ~= "" then
                    return name_str
                end
            end
        end
    end
    return ""
end

local function detect_career_team_abbr(ctx, mode, in_match)
    if not memory or not memory.read then return "" end
    local _, blk = get_career_ctx_and_blk()
    if not blk then return "" end

    local club_raw = 0
    if mode == "Master Liga" then
        club_raw = read_u32(blk + 0x182d398) or 0
    elseif mode == "Rumo ao Estrelato" then
        club_raw = detected_bal_club or 0
    else
        return ""
    end

    local active_raw = club_raw
    if in_match and ctx then
        local h = ctx.home_team or 0
        local a = ctx.away_team or 0
        local club_idx = (club_raw ~= 0) and (club_raw % 16384) or -1
        local h_is_club = (h ~= 0) and ((h % 16384) == club_idx)
        local a_is_club = (a ~= 0) and ((a % 16384) == club_idx)
        if not h_is_club and not a_is_club then
            local h_addr = get_team_struct_addr(blk, h)
            local a_addr = get_team_struct_addr(blk, a)
            if is_national_team_struct(h_addr) then
                active_raw = h
            elseif is_national_team_struct(a_addr) then
                active_raw = a
            end
        end
    end

    local t_addr = get_team_struct_addr(blk, active_raw)
    if not t_addr then return "" end
    return get_team_abbr_from_struct(t_addr)
end

-- Read active Master League coach/manager name from memory
local function detect_ml_coach_from_memory()
    if not memory or not memory.read then return "" end
    local ctx, blk = get_career_ctx_and_blk()
    if not ctx or not blk then return "" end

    local career_type = read_u32(blk + 0x183789c)
    if career_type == 2 then
        local mgr_idx = read_u16(blk + 0x182d388)
        if mgr_idx then
            local mgr = (mgr_idx > 0x514) and (blk + 0xd0ba90) or (blk + 0xc4ca0c + mgr_idx * 0x258)
            local flags = read_u32(mgr + 0x38)
            if flags and (flags % 128) == 0x63 then
                local sso = memory.read(ctx + 0xb8, 32)
                if type(sso) == "string" and #sso == 32 then
                    local cap = tonumber(memory.unpack("u64", sso:sub(25, 32)))
                    if cap and cap >= 16 then
                        local str_ptr = tonumber(memory.unpack("u64", sso:sub(1, 8)))
                        if str_ptr and str_ptr >= 0x10000 then
                            local name = clean_str(memory.read(str_ptr, 64))
                            if name ~= "" then return name end
                        end
                    else
                        local name = clean_str(sso:sub(1, 16))
                        if name ~= "" then return name end
                    end
                end
            else
                local name = clean_str(memory.read(mgr + 8, 64))
                if name ~= "" then return name end
            end
        end
    end

    local ml_team_id = read_u32(blk + 0x182d398)
    if ml_team_id and ml_team_id ~= 0 then
        return get_coach_name(ml_team_id, "")
    end
    return ""
end

local function write_presence(data)
    local targets = {
        (os.getenv("TEMP") or "C:\\Windows\\Temp") .. "\\pes_rpc.json",
        "C:\\Windows\\Temp\\pes_rpc.json",
        g_sider_dir .. "pes_rpc.json"
    }

    local payload = string.format([[{
  "in_match": %s,
  "home_name": "%s",
  "away_name": "%s",
  "home_score": %d,
  "away_score": %d,
  "period": %d,
  "clock_minutes": %d,
  "clock_seconds": %d,
  "added_minutes": %d,
  "tournament": "%s",
  "stage": "%s",
  "mode": "%s",
  "player": "%s",
  "coach": "%s",
  "team_abbr": "%s",
  "timestamp": %d
}]],
        data.in_match and "true" or "false",
        escape_json(data.home_name or ""),
        escape_json(data.away_name or ""),
        data.home_score or 0,
        data.away_score or 0,
        data.period or 1,
        data.clock_minutes or 0,
        data.clock_seconds or 0,
        data.added_minutes or 0,
        escape_json(data.tournament or ""),
        escape_json(data.stage or ""),
        escape_json(data.mode or ""),
        escape_json(data.player or ""),
        escape_json(data.coach or ""),
        escape_json(data.team_abbr or ""),
        os.time()
    )

    for _, path in ipairs(targets) do
        local f = io.open(path, "w")
        if f then
            f:write(payload)
            f:close()
        end
    end
end

local function update_presence_state(ctx)
    local stats = nil
    if match and match.stats then
        stats = match.stats()
    end

    if ctx then
        if ctx.home_team and ctx.home_team ~= 0 then
            last_home_id = ctx.home_team
            current_home = get_team_name(last_home_id, current_home)
        end
        if ctx.away_team and ctx.away_team ~= 0 then
            last_away_id = ctx.away_team
            current_away = get_team_name(last_away_id, current_away)
        end
        detect_names_from_mis(ctx)
    end

    if stats and stats.period and stats.period > 0 then
        local tid = (ctx and ctx.tournament_id) or 65535

        local stage = ""
        local minfo = (ctx and ctx.match_info) or 0
        if minfo == 53 then
            stage = "Final: "
        elseif minfo == 52 then
            stage = "Semifinal: "
        elseif minfo == 51 then
            stage = "Quartas: "
        end

        local mem_mode = detect_game_mode_from_memory()
        if mem_mode then
            detected_mode = mem_mode
        end

        local mode = detected_mode
        if not mode or mode == "" or mode == "Menu" or mode == "Menu Principal" then
            mode = (tid == 65535) and "Amistoso" or "Campeonato"
        end

        local comp = COMP_MAP[tid]
        if not comp then
            if mode == "Modo Copa" then
                comp = "Modo Copa"
            elseif mode == "Modo Liga" then
                comp = "Modo Liga"
            elseif tid ~= 65535 then
                comp = "Campeonato"
            else
                comp = "Amistoso"
            end
        end

        -- Resolve player name for BAL or coach name for ML
        local team_abbr = ""
        if mode == "Rumo ao Estrelato" then
            detected_coach = ""
            if detected_player == "" or detected_bal_club == 0 then
                detected_player = detect_bal_player_from_memory()
            end
            team_abbr = detect_career_team_abbr(ctx, mode, true)
        elseif mode == "Master Liga" then
            detected_player = ""
            if detected_coach == "" then
                detected_coach = detect_ml_coach_from_memory()
            end
            team_abbr = detect_career_team_abbr(ctx, mode, true)
        else
            detected_player = ""
            detected_coach = ""
        end

        write_presence({
            in_match = true,
            home_name = (current_home ~= "") and current_home or get_team_name(last_home_id, "Mandante"),
            away_name = (current_away ~= "") and current_away or get_team_name(last_away_id, "Visitante"),
            home_score = stats.home_score or 0,
            away_score = stats.away_score or 0,
            period = stats.period or 1,
            clock_minutes = stats.clock_minutes or 0,
            clock_seconds = stats.clock_seconds or 0,
            added_minutes = stats.added_minutes or 0,
            tournament = comp,
            stage = stage,
            mode = mode,
            player = detected_player,
            coach = detected_coach,
            team_abbr = team_abbr
        })
    else
        local mem_mode = detect_game_mode_from_memory()
        if mem_mode then
            detected_mode = mem_mode
        end
        local menu_mode = (detected_mode ~= "" and detected_mode ~= "Menu") and detected_mode or "Menu Principal"
        local team_abbr = ""
        if menu_mode == "Rumo ao Estrelato" then
            detected_coach = ""
            if detected_player == "" or detected_bal_club == 0 then
                detected_player = detect_bal_player_from_memory()
            end
            team_abbr = detect_career_team_abbr(ctx, menu_mode, false)
        elseif menu_mode == "Master Liga" then
            detected_player = ""
            if detected_coach == "" then
                detected_coach = detect_ml_coach_from_memory()
            end
            team_abbr = detect_career_team_abbr(ctx, menu_mode, false)
        else
            detected_player = ""
            detected_coach = ""
        end
        write_presence({
            in_match = false,
            home_name = "",
            away_name = "",
            mode = menu_mode,
            tournament = "",
            stage = "",
            player = detected_player,
            coach = detected_coach,
            team_abbr = team_abbr
        })
    end
end

function m.display_frame(ctx)
    local now = os.clock()
    if now - last_write >= WRITE_INTERVAL then
        last_write = now
        update_presence_state(ctx)
    end
end

function m.set_teams(ctx, home, away)
    if home and home ~= 0 then
        last_home_id = home
        current_home = get_team_name(home, current_home)
    end
    if away and away ~= 0 then
        last_away_id = away
        current_away = get_team_name(away, current_away)
    end
    detect_names_from_mis(ctx)
end

function m.after_set_conditions(ctx)
    detect_names_from_mis(ctx)
end

function m.context_reset(ctx)
    detected_mode = "Menu Principal"
    detected_player = ""
    detected_coach = ""
    detected_bal_club = 0
    write_presence({
        in_match = false,
        home_name = "",
        away_name = "",
        mode = "Menu Principal",
        tournament = "",
        stage = "",
        player = "",
        coach = "",
        team_abbr = ""
    })
end

function m.init(ctx)
    log("PesPresence: module v1.6.0 initialized successfully for eFootball PES 2021.")
    if ctx and ctx.sider_dir then
        g_sider_dir = ctx.sider_dir
    end

    load_team_and_coach_databases(g_sider_dir)

    -- Register continuous background hooks
    ctx.register("display_frame", m.display_frame)
    ctx.register("set_teams", m.set_teams)
    ctx.register("after_set_conditions", m.after_set_conditions)
    ctx.register("context_reset", m.context_reset)
end

return m
