# Approximately Up

- **Process**: `ApproximatelyUp.exe`
- **Engine**: Unity IL2CPP v39 (`GameAssembly.dll`, v1.0.313)
- **Data source**: Direct IL2CPP memory reading with local save file (`AppData\LocalLow`) fallback for world names and objective progress.

## Tracked States

- Main Menu (including currently selected world name & completion percentage)
- In World:
  - Current planet/celestial body (orbit distance check + color emoji) or deep space (`In Space (Traveling)`)
  - Creation / Build mode vs Flight mode
  - World name & completion percentage
  - Lobby player count (`X/4` vanilla or up to `X/64` when modded `SteamManager.CreateLobby` is patched)
