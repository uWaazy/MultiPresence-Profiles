# Le Mans Ultimate

- **Processes**: `Le Mans Ultimate.exe`, `LeMansUltimate.exe`
- **Data source**: Shared memory (`LMU_Data` / `Local\LMU_Data` memory-mapped file)

## Tracked States

- Main Menu (when `mNumVehicles <= 0` or `mCurrentET` is stale)
- On Track:
  - Track name, car name, vehicle class
  - Session type (`Practice`, `Qualifying`, `Race`, etc.)
  - Current position (`P1 / 38`), current lap, remaining session time or lap count
