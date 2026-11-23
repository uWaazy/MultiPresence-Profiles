# eFootball PES 2021 / Football Life

- **Processes**: `PES2021.exe`, `FL24.exe`, `FL25.exe`, `FL26.exe`
- **Bridge**: Sider 7 Lua module (`PesPresence.lua`) exporting live match and career state to `%TEMP%\pes_rpc.json` (plus `Sider\pes_rpc.json`).

## Tracked States

- Main Menu
- Career Menus (`Master League` with manager name & team abbreviation; `Become a Legend` with player name & team abbreviation)
- Live Match:
  - Home vs Away team names & score (`Team A 2 x 1 Team B`)
  - Competition name (`Champions League`, `Brasileirão`, `Cup Mode`, `League Mode`, `Exhibition`, etc.) and stage (`Final`, `Semifinal`, `Quarterfinal`)
  - Live match clock (`45+3 2T`, `30 1T`, `PK`)
  - Dynamic Club vs National Team abbreviation switching in Master League / Become a Legend
