-- PesPresence.lua v2.0.0
-- Universal Real-Time Rich Presence bridge for eFootball PES 2021 & SP Football Life (Sider 7)
-- Exports live match scores, clock, match state, 25+ competitions, live standings, stadium,
-- season, BAL player position, and Master League manager to pes_rpc.json

local m = { version = "2.0.0" }
local ffi = ffi
local memory = memory
local match = match

local last_write = 0
local WRITE_INTERVAL = 1.0
local g_sider_dir = ".\\"

local BUILDS = {
    { nome = "CPY",   getter = 21719648, guard = "\x48\x8b\x05\xa9\xf3\x24\x02\xc3", owner = 0x3705e10, status = 0x3704e38 },
    { nome = "Steam", getter = 21742928, guard = "\x48\x8b\x05\xe9\x7c\x25\x02\xc3", owner = 0x3714240, status = 0x3713268 },
}

local LAYOUTS = {
    normal = {
        date = 0x1642a1c, count = 0xd0bcf4, regs = 0xc12e9c, rounds = 0xd65f64, events = 0xe9ff08,
        teams = 0xadf4bc, nteams = 0xd0bcec, maxregs = 300, maxteams = 3200,
    },
    cap = {
        date = 0x19115e4, count = 0x4877328, regs = 0x1f09830, rounds = 0x447ed20, events = 0x1f7cf10,
        teams = 0x193f170, nteams = 0x4877320, maxregs = 600, maxteams = 3200,
    },
}

local COMP_META = {
    ucl         = { name = "UEFA Champions League" },
    uel         = { name = "UEFA Europa League" },
    usc         = { name = "UEFA Super Cup" },
    euro        = { name = "UEFA Euro" },
    wc          = { name = "FIFA World Cup" },
    cwc         = { name = "FIFA Club World Cup" },
    lib         = { name = "CONMEBOL Libertadores" },
    suda        = { name = "CONMEBOL Sudamericana" },
    recopa      = { name = "CONMEBOL Recopa" },
    ca          = { name = "CONMEBOL Copa América" },
    acl         = { name = "AFC Champions League" },
    br_a        = { name = "Brasileirão Série A" },
    br_b        = { name = "Brasileirão Série B" },
    br_c        = { name = "Brasileirão Série C" },
    br_d        = { name = "Brasileirão Série D" },
    cdb         = { name = "Copa do Brasil" },
    ne          = { name = "Copa do Nordeste" },
    cverde      = { name = "Copa Verde" },
    supercopa   = { name = "Supercopa do Brasil" },
    paulistao   = { name = "Paulistão" },
    est         = { name = "Campeonato Estadual" },
    epl         = { name = "Premier League" },
    efl         = { name = "EFL Championship" },
    facup       = { name = "FA Cup" },
    laliga      = { name = "LaLiga EA Sports" },
    laliga2     = { name = "LaLiga Hypermotion" },
    cdr         = { name = "Copa del Rey" },
    seriea      = { name = "Serie A" },
    serieb      = { name = "Serie BKT" },
    coppaitalia = { name = "Coppa Italia" },
    bundesliga  = { name = "Bundesliga" },
    dfb         = { name = "DFB-Pokal" },
    ligue1      = { name = "Ligue 1" },
    ligue2      = { name = "Ligue 2 BKT" },
    cdf         = { name = "Coupe de France" },
    eredivisie  = { name = "Eredivisie" },
    knvb        = { name = "KNVB Beker" },
    ligapt      = { name = "Liga Portugal" },
    tacapt      = { name = "Taça de Portugal" },
    arg_lpf     = { name = "Liga Profesional" },
    arg_cup     = { name = "Copa Argentina" },
    friendly    = { name = "Friendly" },
}

local COMP_TID_MAP = {
    [1] = "cwc", [47] = "cwc", [48] = "cwc", [145] = "cwc", [146] = "cwc",
    [2] = "ucl", [3] = "ucl", [4] = "ucl",
    [1026] = "ucl", [2050] = "ucl", [3074] = "ucl", [4098] = "ucl", [5122] = "ucl", [6146] = "ucl", [7170] = "ucl", [8194] = "ucl",
    [1027] = "ucl", [2051] = "ucl", [3075] = "ucl", [4099] = "ucl", [5123] = "ucl", [6147] = "ucl", [7171] = "ucl", [8195] = "ucl",
    [5] = "uel", [6] = "uel",
    [1029] = "uel", [2053] = "uel", [3077] = "uel", [4101] = "uel", [5125] = "uel", [6149] = "uel", [7173] = "uel", [8197] = "uel", [9221] = "uel",
    [7] = "usc",
    [8] = "lib", [9] = "lib", [10] = "lib",
    [1032] = "lib", [2056] = "lib", [3080] = "lib", [4104] = "lib", [5128] = "lib", [6152] = "lib", [7176] = "lib", [8200] = "lib",
    [1033] = "lib", [2057] = "lib", [3081] = "lib", [4105] = "lib", [5129] = "lib", [6153] = "lib", [7177] = "lib", [8201] = "lib",
    [11] = "suda", [12] = "suda",
    [1035] = "suda", [2059] = "suda", [3083] = "suda", [4107] = "suda", [5131] = "suda", [6155] = "suda", [7179] = "suda", [8203] = "suda",
    [3087] = "suda", [1039] = "suda", [2063] = "suda", [4111] = "suda", [5135] = "suda", [6159] = "suda", [7183] = "suda", [8207] = "suda",
    [13] = "recopa", [14] = "recopa",
    [15] = "acl", [16] = "acl", [1040] = "acl", [2064] = "acl", [3088] = "acl",
    [17] = "epl", [79] = "efl", [83] = "efl", [23] = "facup", [86] = "facup",
    [18] = "seriea", [82] = "serieb", [85] = "serieb", [24] = "coppaitalia", [87] = "coppaitalia",
    [19] = "laliga", [80] = "laliga2", [84] = "laliga2", [25] = "cdr", [88] = "cdr",
    [20] = "ligue1", [81] = "ligue2", [26] = "cdf", [89] = "cdf",
    [21] = "eredivisie", [27] = "knvb", [90] = "knvb",
    [22] = "ligapt", [28] = "tacapt", [91] = "tacapt",
    [50] = "bundesliga", [51] = "dfb", [52] = "dfb", [53] = "dfb",
    [29] = "br_a", [2077] = "br_a",
    [163] = "br_b", [68] = "br_b",
    [67] = "br_c", [54] = "br_c",
    [120] = "br_d", [165] = "br_d",
    [31] = "cdb", [1055] = "cdb",
    [30] = "arg_lpf", [49] = "arg_cup",
    [127] = "cverde", [128] = "ne", [129] = "supercopa", [130] = "paulistao", [131] = "est",
    [33] = "wc", [34] = "wc", [35] = "wc", [36] = "euro", [37] = "ca", [38] = "wc",
    [65535] = "friendly",
}

local GAME_MODE_MAP = {
    [0]  = "menu", [1]  = "menu", [2]  = "online", [3]  = "friendly",
    [4]  = "menu", [5]  = "menu", [6]  = "menu", [7]  = "menu",
    [8]  = "friendly", [9]  = "cup", [10] = "friendly", [11] = "friendly", [12] = "friendly",
    [13] = "edit", [14] = "edit",
    [15] = "cup", [16] = "cup", [17] = "cup", [18] = "league",
    [19] = "ml", [20] = "bal",
    [21] = "cup", [22] = "cup", [23] = "cup",
    [24] = "online", [25] = "online", [26] = "online", [27] = "online", [28] = "online", [29] = "online",
    [30] = "myclub", [31] = "myclub", [32] = "myclub", [33] = "myclub", [34] = "myclub",
    [35] = "myclub", [36] = "myclub", [37] = "myclub", [38] = "myclub", [39] = "myclub",
    [40] = "myclub", [41] = "myclub", [42] = "myclub", [43] = "myclub", [44] = "myclub",
    [45] = "menu", [46] = "menu", [47] = "menu", [48] = "menu", [49] = "menu",
    [50] = "online", [51] = "training", [52] = "training", [53] = "menu",
    [54] = "online", [55] = "online", [56] = "online", [57] = "online", [58] = "online",
    [59] = "online", [60] = "online",
}

local team_map = {
    [100] = "Manchester United", [101] = "Arsenal", [102] = "Chelsea", [103] = "Liverpool",
    [104] = "Leeds United", [105] = "West Ham", [106] = "Newcastle United", [107] = "Aston Villa",
    [108] = "Barcelona", [109] = "Real Madrid", [110] = "Valencia", [114] = "Paris Saint-Germain",
    [116] = "Ajax", [117] = "Feyenoord", [119] = "Internazionale", [121] = "AC Milan",
    [125] = "Roma", [127] = "Bayern München", [131] = "Celtic", [133] = "Olympiacos",
    [136] = "Vasco da Gama", [137] = "Palmeiras", [138] = "River Plate", [139] = "Boca Juniors",
    [172] = "Atlético de Madrid", [173] = "Manchester City", [177] = "Everton", [179] = "Tottenham",
    [204] = "Leicester City", [265] = "Sevilla", [274] = "Cruzeiro", [1245] = "Atlético Mineiro",
    [1246] = "Botafogo", [1247] = "Corinthians", [1248] = "Flamengo", [1249] = "Fluminense",
    [1250] = "Grêmio", [1252] = "Internacional", [1254] = "Santos", [1255] = "São Paulo",
    [1930] = "Athletico-PR", [1931] = "Coritiba", [1933] = "Goiás", [1936] = "Sport",
    [1937] = "Vitória", [2451] = "Atlético-GO", [2453] = "Bahia", [2459] = "Red Bull Bragantino",
    [5137] = "Juventude", [5142] = "Cuiabá", [5143] = "Fortaleza", [5145] = "Criciúma",
}

local coach_map = {}
local stadium_map = {}

local S = {
    base = 0,
    build = nil,
    L = nil,
    sys_status_addr = nil,
    sys_status_searched = false,
    global_ptr_addr = nil,
    global_ptr_searched = false,
    team_abbr_offset = nil,
    ram_teams_loaded_blk = 0,
    last_home_id = 0,
    last_away_id = 0,
    current_home = "",
    current_away = "",
    current_stadium = "",
    detected_mode_key = "menu",
    detected_player = "",
    detected_player_pos = -1,
    detected_bal_club = 0,
    detected_coach = "",
    match_state = "live",
    standings_cache_key = "",
    standings_pos = {},
    standings_round = 0,
    standings_is_league = false,
}

local function clean_str(s)
    if not s or type(s) ~= "string" then return "" end
    local clean = string.match(s, "[^%z]*")
    if not clean then return "" end
    return (clean:gsub("^%s*(.-)%s*$", "%1"))
end

local function clean_team_display_name(nm)
    if not nm or nm == "" then return "" end
    nm = nm:gsub("%s*%([%s%a][%s%a][%s%a]?%s*%)%s*", " ")
    nm = nm:gsub("^%s+", ""):gsub("%s+$", ""):gsub("%s%s+", " ")
    return nm
end

local function clean_coach_name(raw)
    if not raw or type(raw) ~= "string" then return "" end
    raw = raw:gsub('^"', ""):gsub('"$', "")
    raw = raw:gsub("^[^\\]*\\", "")
    raw = raw:gsub("^[A-Z0-9]+%s*-%s*", "")
    raw = raw:gsub("%s+[vV]%d+.*$", "")
    raw = raw:gsub("%s+TERNO.*$", "")
    raw = raw:gsub("^%s+", ""):gsub("%s+$", "")
    return raw
end

local function clean_stadium_name(raw)
    if not raw or type(raw) ~= "string" then return "" end
    raw = raw:gsub('^"', ""):gsub('"$', "")
    raw = raw:gsub("%(%([^)]*%)%)", " ")
    raw = raw:gsub("%([^)]*%)", " ")
    raw = raw:gsub("^[^\\]*\\", "")
    raw = raw:gsub("%s%-%s*Est[aá]dio.*$", "")
    raw = raw:gsub("^%s+", ""):gsub("%s+$", ""):gsub("%s%s+", " ")
    return raw
end

local function escape_json(s)
    if not s or type(s) ~= "string" then return "" end
    return s:gsub("\\", "\\\\"):gsub('"', '\\"'):gsub("\n", " "):gsub("\r", "")
end

local function read_bytes(addr, n)
    if not memory or not memory.read or not addr or addr < 0x10000 then return nil end
    local b = memory.read(addr, n)
    if type(b) ~= "string" or #b ~= n then return nil end
    return b
end

local function read_u64(addr)
    local b = read_bytes(addr, 8)
    if not b then return nil end
    return tonumber(memory.unpack("u64", b))
end

local function read_u32(addr)
    local b = read_bytes(addr, 4)
    if not b then return nil end
    return tonumber(memory.unpack("u32", b))
end

local function read_u16(addr)
    local b = read_bytes(addr, 2)
    if not b then return nil end
    return tonumber(memory.unpack("u16", b))
end

local function u16_le(s, off)
    local b1, b2 = s:byte(off, off + 1)
    if not b1 then return 0 end
    return b1 + b2 * 256
end

local function u32_le(s, off)
    local b1, b2, b3, b4 = s:byte(off, off + 3)
    if not b1 then return 0 end
    return b1 + b2 * 256 + b3 * 65536 + b4 * 16777216
end

local function team_id_from_raw(raw)
    if not raw or raw == 0 or raw == 4294967295 then return nil end
    local t = math.floor(raw / 16384)
    if t > 0 and t < 262144 then return t end
    if raw > 0 and raw < 16384 then return raw end
    return nil
end

local function detect_exe_build_and_layout()
    if S.build and S.L then return end
    if not memory or not memory.get_process_info then return end
    local info = memory.get_process_info()
    if not info or not info.base then return end
    S.base = tonumber(ffi and ffi.cast("uintptr_t", info.base) or info.base) or 0
    if S.base < 0x10000 then return end

    for _, b in ipairs(BUILDS) do
        local g = read_bytes(S.base + b.getter, #b.guard)
        if g == b.guard then
            S.build = b
            S.global_ptr_addr = S.base + b.owner
            S.sys_status_addr = S.base + b.status
            break
        end
    end

    local p1 = read_u32(S.base + 0xaede84)
    local p2 = read_u32(S.base + 0xaed7b4)
    local is_cap = (p1 == 0x19115e4) or (p2 == 0x19115e4)
    S.L = is_cap and LAYOUTS.cap or LAYOUTS.normal
end

local function resolve_sys_status_ptr()
    if S.sys_status_addr then return S.sys_status_addr end
    detect_exe_build_and_layout()
    if S.sys_status_addr then return S.sys_status_addr end
    if S.sys_status_searched then return nil end
    S.sys_status_searched = true

    if memory and memory.search_process then
        local patterns = {
            "\x8b\x88\xf0\x00\x00\x00\x83\xc1\xe8\x83\xf9\x20\x0f\x87",
            "\x8b\x88\xf0\x00\x00\x00\x83\xf9\x27\x77\x10\x48\xb8\x00\x00\x00\x00\xd4\x00\x00\x00",
        }
        for _, pat in ipairs(patterns) do
            local addr = memory.search_process(pat)
            if addr then
                local call_bytes = read_bytes(addr - 5, 5)
                if call_bytes and call_bytes:byte(1) == 0xE8 then
                    local rel = tonumber(memory.unpack("i32", call_bytes:sub(2, 5)))
                    local fn = addr + rel
                    local fn_bytes = read_bytes(fn, 8)
                    if fn_bytes and fn_bytes:sub(1, 3) == "\x48\x8b\x05" and fn_bytes:byte(8) == 0xC3 then
                        local disp = tonumber(memory.unpack("i32", fn_bytes:sub(4, 7)))
                        S.sys_status_addr = fn + 7 + disp
                        return S.sys_status_addr
                    end
                end
            end
        end
    end
    return nil
end

local function resolve_global_ptr()
    if S.global_ptr_addr then return S.global_ptr_addr end
    detect_exe_build_and_layout()
    if S.global_ptr_addr then return S.global_ptr_addr end
    if not memory or not memory.read then return nil end

    for _, cand in ipairs({ 0x143705e10, 0x143714240 }) do
        local v = read_u64(cand)
        if v and v >= 0x10000 then
            S.global_ptr_addr = cand
            return S.global_ptr_addr
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
    if not S.L then detect_exe_build_and_layout() end
    return ctx, blk
end

local function detect_game_mode_from_memory()
    local ptr_addr = resolve_sys_status_ptr()
    if not ptr_addr then return nil end
    local inst = read_u64(ptr_addr)
    if not inst or inst < 0x10000 then return nil end
    local gm = read_u32(inst + 0xF0)
    if not gm then return nil end
    return GAME_MODE_MAP[gm] or "menu"
end

local function load_static_databases(sider_dir)
    if not sider_dir then return end
    local base = sider_dir:gsub("/$", "\\")
    if base:sub(-1) ~= "\\" then base = base .. "\\" end

    local team_paths = {
        base .. "BMPES\\Servidor de Comentarios\\map_teams.txt",
        base .. "BMPES\\Servidor de Badge\\map_teams.txt",
        base .. "BMPES\\Servidor de Estadios\\map_teams.txt",
        base .. "content\\kit-server\\map.txt",
        base .. "content\\kits\\map.txt",
    }
    for _, p in ipairs(team_paths) do
        local f = io.open(p, "r")
        if f then
            for line in f:lines() do
                line = line:gsub("^\239\187\191", ""):gsub("^%s+", ""):gsub("%s+$", "")
                if line ~= "" and not line:match("^#") and not line:match("^;") then
                    local tid1, name1 = line:match('^(%d+)%s*,%s*"([^"]+)"')
                    if tid1 and name1 then
                        local id = tonumber(tid1)
                        if id and not team_map[id] then
                            local nm = name1:match("[^\\/]+$") or name1
                            nm = clean_team_display_name(nm)
                            if nm ~= "" then team_map[id] = nm end
                        end
                    else
                        local tid2, name2 = line:match("^(%d+)%s*,%s*[^,]*\\([^\\,]+)%.mp3")
                        if not name2 then
                            tid2, name2 = line:match("^(%d+)%s*,%s*[^,]*\\([^\\,]+)%.")
                        end
                        if tid2 and name2 then
                            local id = tonumber(tid2)
                            if id and not team_map[id] then
                                local nm = clean_team_display_name(name2)
                                if nm ~= "" then team_map[id] = nm end
                            end
                        end
                    end
                end
            end
            f:close()
        end
    end

    local coach_paths = {
        base .. "BMPES\\Servidor de Treinadores\\Real_Coach\\map.txt",
    }
    for _, cp in ipairs(coach_paths) do
        local cf = io.open(cp, "r")
        if cf then
            for line in cf:lines() do
                line = line:gsub("^%s+", ""):gsub("%s+$", "")
                if line ~= "" and not line:match("^#") and not line:match("^;") then
                    local tid, _, folder = line:match('^(%d+),%s*(%d+),%s*"([^"]+)"')
                    if tid and folder then
                        local id = tonumber(tid)
                        if id and not coach_map[id] then
                            local cname = clean_coach_name(folder)
                            if cname ~= "" then coach_map[id] = cname end
                        end
                    end
                end
            end
            cf:close()
        end
    end

    local stad_paths = {
        base .. "BMPES\\Servidor de Estadios\\map_teams.txt",
        base .. "content\\stadium-server\\map_teams.txt",
    }
    for _, sp in ipairs(stad_paths) do
        local sf = io.open(sp, "r")
        if sf then
            for line in sf:lines() do
                line = line:gsub("^\239\187\191", ""):gsub("^%s+", ""):gsub("%s+$", "")
                if line ~= "" and not line:match("^#") and not line:match("^;") then
                    local tid, _, sname = line:match("^%s*(%d+)%s*,%s*%d+%s*,%s*([^,\r\n]+)")
                    if tid and sname then
                        local id = tonumber(tid)
                        if id and not stadium_map[id] then
                            local clean = clean_stadium_name(sname)
                            if clean ~= "" then stadium_map[id] = clean end
                        end
                    end
                end
            end
            sf:close()
        end
    end
end

local function load_career_team_names_from_ram(blk)
    if not blk or not S.L then return end
    if S.ram_teams_loaded_blk == blk then return end
    local nt = read_u32(blk + S.L.nteams) or 0
    if nt <= 0 or nt > 10000 then return end
    S.ram_teams_loaded_blk = blk

    local max_scan = math.min(nt, S.L.maxteams or 3200)
    local TEAM_REC_SIZE = 0x690
    local CHUNK_TEAMS = 300
    for start_idx = 0, max_scan - 1, CHUNK_TEAMS do
        local n_read = math.min(CHUNK_TEAMS, max_scan - start_idx)
        local chunk = read_bytes(blk + S.L.teams + start_idx * TEAM_REC_SIZE, n_read * TEAM_REC_SIZE)
        if not chunk then break end
        local count = math.floor(#chunk / TEAM_REC_SIZE)
        for i = 0, count - 1 do
            local base_off = i * TEAM_REC_SIZE + 1
            local raw_id = u32_le(chunk, base_off + 8)
            local tid = team_id_from_raw(raw_id)
            if tid then
                local nm = clean_str(chunk:sub(base_off + 0x13c, base_off + 0x13c + 69))
                if #nm >= 2 then
                    team_map[tid] = clean_team_display_name(nm)
                end
            end
        end
    end
end

local function get_team_name(tid, fallback)
    if not tid or tid == 0 then return fallback or "" end
    if team_map[tid] then return team_map[tid] end
    return fallback or ("Team " .. tostring(tid))
end

local function detect_names_from_mis(ctx)
    if ctx and ctx.mis and ffi and memory then
        local mis_ptr = ctx.mis
        if mis_ptr then
            local p_cast = ffi.cast("char*", mis_ptr)
            if p_cast ~= nil then
                local ch = clean_team_display_name(clean_str(memory.read(p_cast + 0x13c, 0x46)))
                local ca = clean_team_display_name(clean_str(memory.read(p_cast + 0x690 + 0x13c, 0x46)))
                if ch ~= "" then S.current_home = ch end
                if ca ~= "" then S.current_away = ca end
            end
        end
    end
end

local function get_team_struct_addr(blk, team_raw)
    if not blk or not S.L or not team_raw or team_raw == 0 or team_raw == 0xffffffff then return nil end
    local idx = team_raw % 16384
    local nt = read_u32(blk + S.L.nteams)
    if nt and idx < nt then
        return blk + S.L.teams + idx * 0x690
    end
    return nil
end

local function is_national_team_struct(team_addr)
    if not team_addr then return false end
    local b = read_bytes(team_addr + 0x41c, 1)
    if b then return (b:byte(1) % 128) <= 4 end
    return false
end

local function get_team_abbr_from_struct(team_addr)
    if not team_addr then return "" end
    if S.team_abbr_offset then
        local s = clean_str(read_bytes(team_addr + S.team_abbr_offset, 8))
        if s:match("^[A-Z0-9][A-Z0-9][A-Z0-9]$") then return s end
    end
    local buf = read_bytes(team_addr + 0x180, 0xb0)
    if buf and #buf >= 8 then
        for i = 2, #buf - 4 do
            if buf:byte(i - 1) == 0 and buf:byte(i + 3) == 0 then
                local cand = buf:sub(i, i + 2)
                if cand:match("^[A-Z0-9][A-Z0-9][A-Z0-9]$") then
                    S.team_abbr_offset = 0x180 + (i - 1)
                    return cand
                end
            end
        end
    end
    return clean_team_display_name(clean_str(read_bytes(team_addr + 0x13c, 0x46)))
end

local function get_team_full_from_struct(team_addr)
    if not team_addr then return "" end
    return clean_team_display_name(clean_str(read_bytes(team_addr + 0x13c, 0x46)))
end

local CALENDAR_YEAR_COMPS = {
    br_a = true, br_b = true, br_c = true, br_d = true,
    cdb = true, ne = true, cverde = true, supercopa = true,
    paulistao = true, est = true,
    lib = true, suda = true, recopa = true, ca = true,
    arg_lpf = true, arg_cup = true,
    wc = true, cwc = true, euro = true,
}

local function read_career_season(blk, comp_key)
    if not blk or not S.L then return "" end
    local db = read_bytes(blk + S.L.date + 8, 4)
    if not db then return "" end
    local y = u16_le(db, 1)
    local mo = db:byte(3)
    if y < 1990 or y > 2150 or mo < 1 or mo > 12 then return "" end

    local effective_key = (comp_key and comp_key ~= "") and comp_key or (S.standings_root_tid and COMP_TID_MAP[S.standings_root_tid] or "")
    if CALENDAR_YEAR_COMPS[effective_key] then
        return tostring(y)
    end

    if mo >= 7 then
        return string.format("%d/%02d", y, (y + 1) % 100)
    else
        return string.format("%d/%02d", y - 1, y % 100)
    end
end

local function detect_bal_player_from_memory(blk)
    if not blk then return "", -1, 0 end
    local rec_size = 0x17c
    local chunk_recs = 500
    for start_idx = 0, 29500, chunk_recs do
        local buf = read_bytes(blk + start_idx * rec_size, chunk_recs * rec_size)
        if not buf or #buf < rec_size then break end
        local count = math.floor(#buf / rec_size)
        for r = 0, count - 1 do
            local off = r * rec_size
            local pid = u32_le(buf, off + 0x31)
            if pid == 0 and (start_idx + r) > 0 then
                return "", -1, 0
            end
            if pid >= 0x70000000 and pid ~= 0xffffffff then
                local name_str = clean_str(buf:sub(off + 0x39, off + 0x38 + 61))
                local club_raw = u32_le(buf, off + 0x165)
                local pos_byte = buf:byte(off + 8) or 0
                local pos_id = math.floor(pos_byte / 16)
                if pos_id < 0 or pos_id > 12 then pos_id = -1 end
                if name_str ~= "" then
                    return name_str, pos_id, (club_raw ~= 0xffffffff) and club_raw or 0
                end
            end
        end
    end
    return "", -1, 0
end

local function detect_ml_coach_from_memory(ctx, blk)
    if not ctx or not blk then return "" end
    local career_type = read_u32(blk + 0x183789c)
    if career_type == 2 then
        local mgr_idx = read_u16(blk + 0x182d388)
        if mgr_idx then
            local mgr = (mgr_idx > 0x514) and (blk + 0xd0ba90) or (blk + 0xc4ca0c + mgr_idx * 0x258)
            local flags = read_u32(mgr + 0x38)
            if flags and (flags % 128) == 0x63 then
                local sso = read_bytes(ctx + 0xb8, 32)
                if sso then
                    local cap = tonumber(memory.unpack("u64", sso:sub(25, 32)))
                    if cap and cap >= 16 then
                        local str_ptr = tonumber(memory.unpack("u64", sso:sub(1, 8)))
                        if str_ptr and str_ptr >= 0x10000 then
                            local name = clean_str(read_bytes(str_ptr, 64))
                            if name ~= "" then return name end
                        end
                    else
                        local name = clean_str(sso:sub(1, 16))
                        if name ~= "" then return name end
                    end
                end
            else
                local name = clean_str(read_bytes(mgr + 8, 64))
                if name ~= "" then return name end
            end
        end
    end

    local ml_raw = read_u32(blk + 0x182d398)
    local ml_tid = team_id_from_raw(ml_raw)
    if ml_tid and coach_map[ml_tid] then
        return coach_map[ml_tid]
    end
    return ""
end

local function get_user_career_club_info(ctx, blk, mode_key, in_match)
    if not blk then return "", "", 0 end
    local club_raw = 0
    if mode_key == "ml" then
        club_raw = read_u32(blk + 0x182d398) or 0
    elseif mode_key == "bal" then
        club_raw = S.detected_bal_club or 0
    else
        return "", "", 0
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
            if is_national_team_struct(h_addr) then active_raw = h
            elseif is_national_team_struct(a_addr) then active_raw = a end
        end
    end

    local t_addr = get_team_struct_addr(blk, active_raw)
    local club_tid = team_id_from_raw(active_raw) or 0
    if not t_addr then
        return "", (club_tid > 0 and get_team_name(club_tid, "") or ""), club_tid
    end
    local abbr = get_team_abbr_from_struct(t_addr)
    local full = get_team_full_from_struct(t_addr)
    if full == "" and club_tid > 0 then full = get_team_name(club_tid, "") end
    return abbr, full, club_tid
end

local function rank_table(tbl)
    local list = {}
    for tid, r in pairs(tbl) do
        list[#list + 1] = { id = tid, pts = r.pts, vit = r.vit, sg = r.sg, gp = r.gp }
    end
    table.sort(list, function(a, b)
        if a.pts ~= b.pts then return a.pts > b.pts end
        if a.vit ~= b.vit then return a.vit > b.vit end
        if a.sg ~= b.sg then return a.sg > b.sg end
        if a.gp ~= b.gp then return a.gp > b.gp end
        return a.id < b.id
    end)
    local pos_map = {}
    for idx, item in ipairs(list) do
        pos_map[item.id] = idx
    end
    return pos_map
end

local function add_result_to_table(tbl, h, a, hs, as)
    local rh = tbl[h]
    if not rh then rh = { pts = 0, vit = 0, sg = 0, gp = 0, jogos = 0 }; tbl[h] = rh end
    local ra = tbl[a]
    if not ra then ra = { pts = 0, vit = 0, sg = 0, gp = 0, jogos = 0 }; tbl[a] = ra end
    rh.gp = rh.gp + hs; rh.sg = rh.sg + (hs - as); rh.jogos = rh.jogos + 1
    ra.gp = ra.gp + as; ra.sg = ra.sg + (as - hs); ra.jogos = ra.jogos + 1
    if hs > as then
        rh.pts = rh.pts + 3; rh.vit = rh.vit + 1
    elseif as > hs then
        ra.pts = ra.pts + 3; ra.vit = ra.vit + 1
    else
        rh.pts = rh.pts + 1; ra.pts = ra.pts + 1
    end
end

-- Computes league/group standings position & round number from career RAM
local function compute_standings_snapshot(blk, target_tid, home_id, away_id, live_hs, live_as, in_match)
    if not blk or not S.L or (target_tid <= 0 and home_id <= 0) then
        return 0, 0, 0, 0
    end
    local cache_key = string.format("%x|%d|%d|%d", blk, target_tid, home_id, away_id)
    if S.standings_cache_key ~= cache_key then
        S.standings_cache_key = cache_key
        S.standings_pos = {}
        S.standings_round = 0
        S.standings_root_tid = 0
        S.standings_is_league = false

        local ev_cnt = read_u32(blk + S.L.count) or 0
        if ev_cnt > 0 and ev_cnt <= 13000 then
            local EVENT_STRIDE = 596
            local CHUNK = 400
            local target_root = 0
            local all_ev = {}

            for base_i = 0, ev_cnt - 1, CHUNK do
                local n_r = math.min(CHUNK, ev_cnt - base_i)
                local raw = read_bytes(blk + S.L.events + base_i * EVENT_STRIDE, n_r * EVENT_STRIDE)
                if not raw then break end
                for j = 0, n_r - 1 do
                    local o = j * EVENT_STRIDE + 1
                    local rh = u32_le(raw, o + 0x10)
                    local ra = u32_le(raw, o + 0x14)
                    local h = team_id_from_raw(rh)
                    local a = team_id_from_raw(ra)
                    if h and a and h ~= a then
                        local root_rid = u16_le(raw, o + 0x06)
                        local played = raw:byte(o + 0x1e) or 0
                        local hs = raw:byte(o + 0x1c) or 0
                        local as = raw:byte(o + 0x1f) or 0
                        if in_match and home_id > 0 and away_id > 0 and ((h == home_id and a == away_id) or (h == away_id and a == home_id)) then
                            if played == 0 then target_root = root_rid end
                        end
                        all_ev[#all_ev + 1] = { rid = root_rid, h = h, a = a, played = played, hs = hs, as = as }
                    end
                end
            end

            -- If in menu (not in match), find the user club's primary league (largest round-robin root_rid containing target_tid)
            if target_root == 0 and target_tid > 0 then
                local rid_counts = {}
                for _, ev in ipairs(all_ev) do
                    if ev.h == target_tid or ev.a == target_tid then
                        rid_counts[ev.rid] = (rid_counts[ev.rid] or 0) + 1
                    end
                end
                local best_cnt = 0
                for rid, cnt in pairs(rid_counts) do
                    if cnt > best_cnt then
                        best_cnt = cnt
                        target_root = rid
                    end
                end
            end

            if target_root > 0 then
                local tbl = {}
                local total_matches = 0
                for _, ev in ipairs(all_ev) do
                    if ev.rid == target_root then
                        total_matches = total_matches + 1
                        if not tbl[ev.h] then tbl[ev.h] = { pts = 0, vit = 0, sg = 0, gp = 0, jogos = 0 } end
                        if not tbl[ev.a] then tbl[ev.a] = { pts = 0, vit = 0, sg = 0, gp = 0, jogos = 0 } end
                        if ev.played == 1 then
                            add_result_to_table(tbl, ev.h, ev.a, ev.hs, ev.as)
                        end
                    end
                end
                local n_clubs = 0
                for _ in pairs(tbl) do n_clubs = n_clubs + 1 end
                local min_rr = math.floor(n_clubs * (n_clubs - 1) / 2)
                if n_clubs >= 3 and n_clubs <= 28 and total_matches >= min_rr then
                    S.standings_root_tid = target_root
                    S.standings_is_league = true
                    S.standings_base_tbl = tbl
                    S.standings_pos = rank_table(tbl)
                    local ref_t = tbl[target_tid] or tbl[home_id]
                    if ref_t then
                        S.standings_round = ref_t.jogos + (in_match and 1 or 0)
                    end
                end
            end
        end
    end

    if not S.standings_is_league or not S.standings_base_tbl then
        return 0, 0, 0, 0
    end

    local pos_map = S.standings_pos
    if in_match and home_id > 0 and away_id > 0 and S.standings_base_tbl[home_id] and S.standings_base_tbl[away_id] then
        local live_tbl = {}
        for tid, r in pairs(S.standings_base_tbl) do
            live_tbl[tid] = { pts = r.pts, vit = r.vit, sg = r.sg, gp = r.gp, jogos = r.jogos }
        end
        add_result_to_table(live_tbl, home_id, away_id, live_hs or 0, live_as or 0)
        pos_map = rank_table(live_tbl)
    end

    return pos_map[home_id] or 0, pos_map[away_id] or 0, pos_map[target_tid] or 0, S.standings_round or 0
end

local function write_presence(data)
    local tmp_dir = (os and os.getenv and (os.getenv("TEMP") or os.getenv("TMP"))) or "C:\\Windows\\Temp"
    if tmp_dir:sub(-1) == "\\" then tmp_dir = tmp_dir:sub(1, -2) end
    local sdir = g_sider_dir
    if sdir:sub(-1) ~= "\\" and sdir:sub(-1) ~= "/" then sdir = sdir .. "\\" end

    local targets = {
        sdir .. "pes_rpc.json",
        tmp_dir .. "\\pes_rpc.json",
    }

    local payload = string.format([[{
  "in_match": %s,
  "match_state": "%s",
  "home_name": "%s",
  "away_name": "%s",
  "home_score": %d,
  "away_score": %d,
  "pk_home_score": %d,
  "pk_away_score": %d,
  "user_side": "%s",
  "home_pos": %d,
  "away_pos": %d,
  "user_pos": %d,
  "round_num": %d,
  "period": %d,
  "clock_minutes": %d,
  "clock_seconds": %d,
  "added_minutes": %d,
  "comp_key": "%s",
  "tournament": "%s",
  "stage_key": "%s",
  "stage": "%s",
  "mode_key": "%s",
  "mode": "%s",
  "season": "%s",
  "stadium": "%s",
  "player": "%s",
  "player_pos_id": %d,
  "coach": "%s",
  "team_abbr": "%s",
  "team_name": "%s",
  "timestamp": %d
}]],
        data.in_match and "true" or "false",
        escape_json(data.match_state or "live"),
        escape_json(data.home_name or ""),
        escape_json(data.away_name or ""),
        data.home_score or 0,
        data.away_score or 0,
        data.pk_home_score or 0,
        data.pk_away_score or 0,
        escape_json(data.user_side or ""),
        data.home_pos or 0,
        data.away_pos or 0,
        data.user_pos or 0,
        data.round_num or 0,
        data.period or 1,
        data.clock_minutes or 0,
        data.clock_seconds or 0,
        data.added_minutes or 0,
        escape_json(data.comp_key or ""),
        escape_json(data.tournament or ""),
        escape_json(data.stage_key or ""),
        escape_json(data.stage or ""),
        escape_json(data.mode_key or "menu"),
        escape_json(data.mode or ""),
        escape_json(data.season or ""),
        escape_json(data.stadium or ""),
        escape_json(data.player or ""),
        data.player_pos_id or -1,
        escape_json(data.coach or ""),
        escape_json(data.team_abbr or ""),
        escape_json(data.team_name or ""),
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
    local stats = (match and match.stats) and match.stats() or nil
    local c_ctx, blk = get_career_ctx_and_blk()
    if blk then
        load_career_team_names_from_ram(blk)
    end

    if ctx then
        if ctx.home_team and ctx.home_team ~= 0 then
            S.last_home_id = ctx.home_team
            S.current_home = get_team_name(S.last_home_id, S.current_home)
        end
        if ctx.away_team and ctx.away_team ~= 0 then
            S.last_away_id = ctx.away_team
            S.current_away = get_team_name(S.last_away_id, S.current_away)
        end
        if ctx.stadium_name and ctx.stadium_name ~= "" then
            S.current_stadium = clean_stadium_name(ctx.stadium_name)
        elseif S.current_stadium == "" and S.last_home_id > 0 and stadium_map[S.last_home_id] then
            S.current_stadium = stadium_map[S.last_home_id]
        end
        detect_names_from_mis(ctx)
    end

    local mem_mode = detect_game_mode_from_memory()
    if mem_mode then S.detected_mode_key = mem_mode end
    local mode_key = S.detected_mode_key or "menu"

    local in_match = (stats and stats.period and stats.period > 0) and true or false
    local team_abbr, team_name, user_tid = "", "", 0

    if mode_key == "bal" then
        S.detected_coach = ""
        if S.detected_player == "" or S.detected_bal_club == 0 then
            local pn, pp, pc = detect_bal_player_from_memory(blk)
            if pn ~= "" then S.detected_player = pn end
            if pp >= 0 then S.detected_player_pos = pp end
            if pc > 0 then S.detected_bal_club = pc end
        end
        team_abbr, team_name, user_tid = get_user_career_club_info(ctx, blk, mode_key, in_match)
    elseif mode_key == "ml" then
        S.detected_player = ""
        S.detected_player_pos = -1
        if S.detected_coach == "" then
            S.detected_coach = detect_ml_coach_from_memory(c_ctx, blk)
        end
        team_abbr, team_name, user_tid = get_user_career_club_info(ctx, blk, mode_key, in_match)
    else
        S.detected_player = ""
        S.detected_player_pos = -1
        S.detected_coach = ""
    end

    if in_match then
        local tid = (ctx and ctx.tournament_id) or 65535
        local minfo = (ctx and ctx.match_info) or 0
        local stage_key, stage_txt = "", ""
        if minfo == 53 then stage_key = "final"; stage_txt = "Final: "
        elseif minfo == 52 then stage_key = "semi"; stage_txt = "Semifinal: "
        elseif minfo == 51 then stage_key = "quarter"; stage_txt = "Quartas: "
        elseif minfo == 50 then stage_key = "r16"; stage_txt = "Oitavas: " end

        local comp_key = COMP_TID_MAP[tid] or ""
        local comp_name = (comp_key ~= "" and COMP_META[comp_key]) and COMP_META[comp_key].name or ""
        if comp_name == "" then
            if mode_key == "cup" then comp_key = "cup"; comp_name = "Modo Copa"
            elseif mode_key == "league" then comp_key = "league"; comp_name = "Modo Liga"
            elseif tid ~= 65535 then comp_key = "comp"; comp_name = "Campeonato"
            else comp_key = "friendly"; comp_name = "Amistoso" end
        end

        local hs = stats.home_score or 0
        local as = stats.away_score or 0
        local pk_h = stats.pk_home_score or 0
        local pk_a = stats.pk_away_score or 0
        local mstate = S.match_state
        if (stats.period or 1) == 5 then mstate = "penalties" end

        local user_side = ""
        if user_tid > 0 then
            if user_tid == S.last_home_id then user_side = "home"
            elseif user_tid == S.last_away_id then user_side = "away" end
        end

        local hp, ap, up, rnum = compute_standings_snapshot(blk, user_tid, S.last_home_id, S.last_away_id, hs, as, true)
        local season = read_career_season(blk, comp_key)

        write_presence({
            in_match = true,
            match_state = mstate,
            home_name = (S.current_home ~= "") and S.current_home or get_team_name(S.last_home_id, "Home"),
            away_name = (S.current_away ~= "") and S.current_away or get_team_name(S.last_away_id, "Away"),
            home_score = hs,
            away_score = as,
            pk_home_score = pk_h,
            pk_away_score = pk_a,
            user_side = user_side,
            home_pos = hp,
            away_pos = ap,
            user_pos = up,
            round_num = rnum,
            period = stats.period or 1,
            clock_minutes = stats.clock_minutes or 0,
            clock_seconds = stats.clock_seconds or 0,
            added_minutes = stats.added_minutes or 0,
            comp_key = comp_key,
            tournament = comp_name,
            stage_key = stage_key,
            stage = stage_txt,
            mode_key = mode_key,
            mode = (mode_key == "ml") and "Master Liga" or ((mode_key == "bal") and "Rumo ao Estrelato" or mode_key),
            season = season,
            stadium = S.current_stadium,
            player = S.detected_player,
            player_pos_id = S.detected_player_pos,
            coach = S.detected_coach,
            team_abbr = team_abbr,
            team_name = team_name,
        })
    else
        local _, _, up, rnum = compute_standings_snapshot(blk, user_tid, 0, 0, 0, 0, false)
        local season = read_career_season(blk, "")
        write_presence({
            in_match = false,
            match_state = "menu",
            home_name = "",
            away_name = "",
            home_pos = 0,
            away_pos = 0,
            user_pos = up,
            round_num = rnum,
            comp_key = "",
            tournament = "",
            stage_key = "",
            stage = "",
            mode_key = mode_key,
            mode = (mode_key == "ml") and "Master Liga" or ((mode_key == "bal") and "Rumo ao Estrelato" or "Menu Principal"),
            season = season,
            stadium = "",
            player = S.detected_player,
            player_pos_id = S.detected_player_pos,
            coach = S.detected_coach,
            team_abbr = team_abbr,
            team_name = team_name,
        })
    end
end

function m.make_key(ctx, filename)
    if type(filename) ~= "string" then return end
    local fn = filename:gsub("/", "\\"):lower()
    if fn == "common\\script\\flow\\match\\matchhalftime.json" then
        S.match_state = "halftime"
    elseif fn == "common\\script\\flow\\match\\matchresult.json" then
        S.match_state = "fulltime"
    elseif fn == "common\\script\\flow\\match\\matchpause.json" then
        S.match_state = "paused"
    elseif fn == "common\\script\\flow\\match\\matchpostpause.json"
        or fn == "common\\script\\flow\\match\\matchposthalftime.json"
        or fn == "common\\script\\flow\\match\\matchsetup.json" then
        S.match_state = "live"
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
    S.match_state = "live"
    S.standings_cache_key = ""
    S.current_stadium = ""
    if home and home ~= 0 then
        S.last_home_id = home
        S.current_home = get_team_name(home, "")
        if stadium_map[home] then S.current_stadium = stadium_map[home] end
    end
    if away and away ~= 0 then
        S.last_away_id = away
        S.current_away = get_team_name(away, "")
    end
    detect_names_from_mis(ctx)
end

function m.after_set_conditions(ctx)
    if ctx and ctx.stadium_name and ctx.stadium_name ~= "" then
        S.current_stadium = clean_stadium_name(ctx.stadium_name)
    end
    detect_names_from_mis(ctx)
end

function m.context_reset(ctx)
    S.match_state = "live"
    S.standings_cache_key = ""
    S.current_stadium = ""
    S.detected_mode_key = "menu"
    S.detected_player = ""
    S.detected_player_pos = -1
    S.detected_coach = ""
    S.detected_bal_club = 0
    write_presence({
        in_match = false,
        match_state = "menu",
        mode_key = "menu",
        mode = "Menu Principal",
    })
end

function m.init(ctx)
    log("PesPresence: module v2.0.0 initialized (multi-patch RAM + 25+ competitions + live standings + stadium).")
    if ctx and ctx.sider_dir then
        g_sider_dir = ctx.sider_dir
    end
    detect_exe_build_and_layout()
    load_static_databases(g_sider_dir)

    ctx.register("display_frame", m.display_frame)
    ctx.register("set_teams", m.set_teams)
    ctx.register("after_set_conditions", m.after_set_conditions)
    ctx.register("livecpk_make_key", m.make_key)
    ctx.register("context_reset", m.context_reset)
end

return m
