# eFootball PES 2021 / Football Life

- **Processes**: `PES2021.exe`, `FL24.exe`, `FL25.exe`, `FL26.exe`
- **Bridge**: Sider 7 Lua module (`PesPresence.lua` v2.0.0) exporting live match and career state to `%TEMP%\pes_rpc.json` (plus `C:\Windows\Temp\pes_rpc.json` and game/Sider directory).

## Tracked States

- Main Menu, Edit Mode, Training Mode
- Career Menus (`Master League` with season, manager name & club/national abbreviation; `Become a Legend` with season, player name, position & team abbreviation)
- Live Match:
  - Home vs Away team names, score, and penalty shootout score
  - Live match state (`live`, `halftime`, `fulltime`, `paused`) and clock (`45+3 2T`, `30 1T`, `PK`)
  - 40+ standardized competitions (`comp_key`) and knockout stages
  - Live league standings position (`home_pos`, `away_pos`, `user_pos`, `round_num`)
  - Stadium name, season, and dynamic Club vs National Team switching in Master League / Become a Legend
- Multi-build & multi-patch RAM support: `CPY` and `Steam` binaries, `normal` and expanded `cap` memory layouts
