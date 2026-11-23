# MultiPresence-Profiles

Custom game profiles and memory providers adapted for integration into [MultiPresence](https://github.com/Dekirai/MultiPresence).

## Structure

- `Common/ProfileTypes.cs` — Minimal interfaces (`IMemoryAccessor`, `PresenceSnapshot`) used to keep each game provider independent from external frameworks.
- `games/` — One folder per game with its provider source, offsets/signatures notes, and any required bridge module.

## Games

| Game | Process | Method | Folder |
|---|---|---|---|
| **Approximately Up** | `ApproximatelyUp.exe` | IL2CPP memory (`GameAssembly.dll`) + save JSON fallback | [approximately-up](games/approximately-up/) |
| **eFootball PES 2021** / **Football Life** | `PES2021.exe`, `FL24.exe`, `FL25.exe`, `FL26.exe` | Sider 7 Lua bridge (`pes_rpc.json`) + memory offsets | [efootball-pes-2021](games/efootball-pes-2021/) |
| **Le Mans Ultimate** | `Le Mans Ultimate.exe`, `LeMansUltimate.exe` | Shared memory (`LMU_Data`) | [le-mans-ultimate](games/le-mans-ultimate/) |
