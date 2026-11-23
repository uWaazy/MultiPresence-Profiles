using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MultiPresenceProfiles.Common;

namespace MultiPresenceProfiles.Games.ApproximatelyUp;

/// <summary>
/// Reads live world, real-time current planet (with planet color emoji),
/// flight vs build/creation mode, and multiplayer player count (X/Max with mod support)
/// directly from Approximately Up's IL2CPP memory (GameAssembly.dll) with disk fallback.
/// </summary>
public static class ApproximatelyUpProvider
{
	public const string CanonicalGameName = "Approximately Up";

	// Static TypeInfo RVAs in GameAssembly.dll (v1.0.313, IL2CPP v39)
	private const ulong RvaCoreTypeInfo = 0x5270120;
	private const ulong RvaUIManagerTypeInfo = 0x526E6C8;
	private const ulong RvaCustomRenderPipelineTypeInfo = 0x5271E98;

	// SteamManager.CreateLobby: mov edx, 4 (BA 04 00 00 00) at RVA 0xD511CC
	private const ulong RvaSteamCreateLobbyMaxPlayersMov = 0xD511CC;

	private const int VanillaMaxLobbyPlayers = 4;
	private const int MaxModdedLobbyPlayers = 64;

	private static int cachedProcessId;
	private static ulong cachedGameAssemblyBase;
	private static DateTime sessionStartUtc = DateTime.MinValue;
	private static DateTime nextDiskPollUtc = DateTime.MinValue;

	private static string cachedWorldName;
	private static int cachedWorldProgress = -1;
	private static string cachedMenuSelectedWorldName;
	private static int cachedMenuSelectedWorldProgress = -1;
	private static int cachedTotalValidObjectives = 80;
	private static HashSet<int> cachedValidObjectiveIds;
	private static List<PlanetOrbitEntry> cachedPlanetOrbits = new List<PlanetOrbitEntry>();
	private static bool hasPlanetAnchorCam;
	private static double anchorCamX;
	private static double anchorCamY;
	private static double anchorCamZ;
	private static bool cachedInWorld;
	private static int cachedSubmenu = -1;
	private static int cachedPlanetObjectId;
	private static bool cachedInDeepSpace;
	private static int cachedPlayerCount = 1;
	private static int cachedMaxPlayers = VanillaMaxLobbyPlayers;
	private static bool cachedInFlightMode;

	private sealed class PlanetOrbitEntry
	{
		public int Ulid;
		public int Wormhole;
		public double RealX;
		public double RealY;
		public double RealZ;
		public double MaxOrbitRadius;
	}

	private sealed class GameSaveData
	{
		public string _lastWorldFilename { get; set; }
	}

	private sealed class ObjectiveEntryData
	{
		public int _completed { get; set; }
	}

	private sealed class WorldObjectivesData
	{
		public List<int> m_Keys { get; set; }
		public List<ObjectiveEntryData> m_Values { get; set; }
	}

	private sealed class WorldSaveData
	{
		public string _name { get; set; }
		public WorldObjectivesData _objectives { get; set; }
	}

	public static bool Read(IMemoryAccessor mem, PresenceSnapshot snap)
	{
		try
		{
			if (!mem.IsAttached)
			{
				return false;
			}

			var proc = mem.AttachedProcess;
			int pid = proc?.Id ?? 0;
			if (pid <= 0)
			{
				return false;
			}

			if (pid != cachedProcessId)
			{
				cachedProcessId = pid;
				cachedGameAssemblyBase = 0;
				sessionStartUtc = DateTime.UtcNow;
				try
				{
					if (proc != null && !proc.HasExited)
					{
						sessionStartUtc = proc.StartTime.ToUniversalTime();
					}
				}
				catch { }

				nextDiskPollUtc = DateTime.MinValue;
				cachedWorldName = null;
				cachedWorldProgress = -1;
				cachedMenuSelectedWorldName = null;
				cachedMenuSelectedWorldProgress = -1;
				cachedTotalValidObjectives = 80;
				cachedValidObjectiveIds = null;
				cachedPlanetOrbits.Clear();
				hasPlanetAnchorCam = false;
				anchorCamX = 0.0;
				anchorCamY = 0.0;
				anchorCamZ = 0.0;
				cachedInWorld = false;
				cachedSubmenu = -1;
				cachedPlanetObjectId = 0;
				cachedInDeepSpace = false;
				cachedPlayerCount = 1;
				cachedMaxPlayers = VanillaMaxLobbyPlayers;
				cachedInFlightMode = false;
			}

			if (DateTime.UtcNow >= nextDiskPollUtc)
			{
				nextDiskPollUtc = DateTime.UtcNow.AddSeconds(2);
				RefreshFromDisk();
			}

			RefreshFromRam(pid);
			ApplyToSnapshot(snap);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void RefreshFromRam(int pid)
	{
		IntPtr hProcess = Wow64MemoryHelper.OpenProcess(0x0410, false, pid); // PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
		if (hProcess == IntPtr.Zero)
		{
			return;
		}

		try
		{
			bool is64 = Wow64MemoryHelper.IsProcess64Bit(hProcess);
			if (!is64)
			{
				return;
			}

			if (cachedGameAssemblyBase == 0)
			{
				if (Wow64MemoryHelper.Get64BitModuleByName(hProcess, "GameAssembly.dll", out ulong gaBase, out int gaSize, out _) && gaSize > 0)
				{
					cachedGameAssemblyBase = gaBase;
				}
			}

			if (cachedGameAssemblyBase == 0)
			{
				return;
			}

			// Detect modded lobby max players if CreateLobby's mov edx, 4 was patched in memory
			int detectedMaxPlayers = VanillaMaxLobbyPlayers;
			byte movOpcode = ReadByte(hProcess, cachedGameAssemblyBase + RvaSteamCreateLobbyMaxPlayersMov);
			if (movOpcode == 0xBA)
			{
				int immMax = ReadInt32(hProcess, cachedGameAssemblyBase + RvaSteamCreateLobbyMaxPlayersMov + 1);
				if (immMax >= VanillaMaxLobbyPlayers && immMax <= MaxModdedLobbyPlayers)
				{
					detectedMaxPlayers = immMax;
				}
			}

			ulong coreSingleton = ReadStaticSingleton(hProcess, cachedGameAssemblyBase + RvaCoreTypeInfo, 0x30);
			ulong uiSingleton = ReadStaticSingleton(hProcess, cachedGameAssemblyBase + RvaUIManagerTypeInfo, 0x00);
			ulong crpSingleton = ReadStaticSingleton(hProcess, cachedGameAssemblyBase + RvaCustomRenderPipelineTypeInfo, 0x28);

			int bestPlayerCount = 1;

			// 1. Active World Name & Multiplayer State from Core._singleton
			if (coreSingleton != 0)
			{
				UpdateValidObjectivesFromCore(hProcess, coreSingleton);

				ulong diskWorldSave = ReadPtr(hProcess, coreSingleton + 0x210); // Core._save
				if (diskWorldSave != 0)
				{
					ulong worldSave = ReadPtr(hProcess, diskWorldSave + 0x10); // DiskWorldSave._world
					if (worldSave != 0)
					{
						string ramWorldName = ReadIl2CppString(hProcess, ReadPtr(hProcess, worldSave + 0x10)); // WorldSave._name
						if (!string.IsNullOrWhiteSpace(ramWorldName))
						{
							string trimmedWorld = ramWorldName.Trim();
							if (!string.Equals(cachedWorldName, trimmedWorld, StringComparison.Ordinal))
							{
								hasPlanetAnchorCam = false;
							}
							cachedWorldName = trimmedWorld;
							cachedInWorld = true;

							int pct = ComputeWorldSaveProgressFromRam(hProcess, worldSave);
							if (pct >= 0)
							{
								cachedWorldProgress = pct;
							}
						}
					}
				}

				// SteamManager multiplayer state (Core._steam at +0x348)
				ulong steamMgr = ReadPtr(hProcess, coreSingleton + 0x348);
				if (steamMgr != 0)
				{
					ulong currentLobby = ReadUInt64(hProcess, steamMgr + 0x10);
					int lobbyState = ReadInt32(hProcess, steamMgr + 0x28); // 3=Created, 5=Connecting, 6=Connected
					int peerCount = ReadDictionaryCount(hProcess, ReadPtr(hProcess, steamMgr + 0x50)); // _connToSteam
					if (lobbyState >= 3 || currentLobby != 0 || peerCount > 0)
					{
						if (peerCount > 0)
						{
							bestPlayerCount = Math.Max(bestPlayerCount, peerCount + 1);
						}
					}
				}
			}

			// 2. UI State, Selected World, Flight vs Creation Mode, Real-Time Planet, and Lobby Players from UIManager._singleton
			if (uiSingleton != 0)
			{
				// Flight Mode vs Creation/Build Mode via UISeatBindingsManager._uiCurrentSeat (+0x48),
				// UIHandComponentsList._showCounter (+0x20), and UIManager._cursorOverlayState (+0x170)
				ulong seatBindingsMgr = ReadPtr(hProcess, uiSingleton + 0x40);
				ulong currentSeatEntity = seatBindingsMgr != 0 ? ReadUInt64(hProcess, seatBindingsMgr + 0x48) : 0;
				ulong handComponentsList = ReadPtr(hProcess, uiSingleton + 0x80);
				if (currentSeatEntity != 0)
				{
					cachedInFlightMode = true;
				}
				else if (handComponentsList != 0)
				{
					int handShowCounter = ReadInt32(hProcess, handComponentsList + 0x20);
					int cursorOverlayState = ReadInt32(hProcess, uiSingleton + 0x170);
					if (handShowCounter > 0)
					{
						cachedInFlightMode = false;
					}
					else if (cursorOverlayState <= 0)
					{
						cachedInFlightMode = true;
					}
				}

				// Active player count in UILobbyInfo (+0xD0 -> _entityToUIPlayer at +0x28)
				ulong uiLobbyInfo = ReadPtr(hProcess, uiSingleton + 0xD0);
				if (uiLobbyInfo != 0)
				{
					int lobbyEntities = ReadDictionaryCount(hProcess, ReadPtr(hProcess, uiLobbyInfo + 0x28));
					if (lobbyEntities > 0 && lobbyEntities <= MaxModdedLobbyPlayers)
					{
						bestPlayerCount = Math.Max(bestPlayerCount, lobbyEntities);
					}
				}

				// Invite menu lobby rows (UIManager._invitePlayerMenu at +0xA0)
				ulong inviteMenu = ReadPtr(hProcess, uiSingleton + 0xA0);
				if (inviteMenu != 0)
				{
					bool lobbyCreated = ReadByte(hProcess, inviteMenu + 0x48) != 0;
					ulong rowsList = ReadPtr(hProcess, inviteMenu + 0x50);
					int rowsLobby = ReadListCount(hProcess, rowsList);
					int rowsCapacity = ReadListCapacity(hProcess, rowsList);
					if (rowsCapacity > detectedMaxPlayers && rowsCapacity <= MaxModdedLobbyPlayers)
					{
						detectedMaxPlayers = rowsCapacity;
					}
					if (lobbyCreated)
					{
						if (rowsLobby > 0 && rowsLobby <= MaxModdedLobbyPlayers)
						{
							bestPlayerCount = Math.Max(bestPlayerCount, rowsLobby);
						}
					}
				}

				// Spaceship launch crew count (UIManager._spaceshipLaunch at +0x78 -> _players at +0x50)
				ulong spaceshipLaunch = ReadPtr(hProcess, uiSingleton + 0x78);
				if (spaceshipLaunch != 0)
				{
					int launchPlayers = ReadListCount(hProcess, ReadPtr(hProcess, spaceshipLaunch + 0x50));
					if (launchPlayers > 1 && launchPlayers <= MaxModdedLobbyPlayers)
					{
						bestPlayerCount = Math.Max(bestPlayerCount, launchPlayers);
					}
				}

				// Objective log selected planet fallback (UIManager._objectiveLog at +0x48 -> _currentSelectedPlanet at +0xC8)
				ulong objectiveLog = ReadPtr(hProcess, uiSingleton + 0x48);
				int objectivePlanetId = objectiveLog != 0 ? ReadInt32(hProcess, objectiveLog + 0xC8) : 0;

				// Main Menu / Solar System View (UIManager._mainMenu at +0x28)
				ulong mainMenu = ReadPtr(hProcess, uiSingleton + 0x28);
				if (mainMenu != 0)
				{
					int submenu = ReadInt32(hProcess, mainMenu + 0x280); // 0 = None (Root Menu), 1 = Play (In World / Solar System)
					cachedSubmenu = submenu;
					if (submenu == 1)
					{
						cachedInWorld = true;
						cachedMenuSelectedWorldName = null;
						cachedMenuSelectedWorldProgress = -1;
					}
					else if (submenu == 0)
					{
						cachedInWorld = false;
						cachedInFlightMode = false;
						cachedInDeepSpace = false;
						cachedWorldName = null;
						cachedWorldProgress = -1;
						hasPlanetAnchorCam = false;

						int selectedIdx = ReadInt32(hProcess, mainMenu + 0x478);
						ulong worldsList = ReadPtr(hProcess, mainMenu + 0x468);
						ulong selectedWorldSave = ReadWorldSaveFromList(hProcess, worldsList, selectedIdx, out string selectedWorld);
						if (!string.IsNullOrWhiteSpace(selectedWorld))
						{
							cachedMenuSelectedWorldName = selectedWorld.Trim();
							cachedMenuSelectedWorldProgress = ComputeWorldSaveProgressFromRam(hProcess, selectedWorldSave);
						}
						else
						{
							cachedMenuSelectedWorldName = null;
							cachedMenuSelectedWorldProgress = -1;
						}
					}

					UpdateCurrentPlanetFromRam(hProcess, mainMenu, crpSingleton, objectivePlanetId);
				}
			}

			cachedPlayerCount = Math.Max(1, Math.Min(MaxModdedLobbyPlayers, bestPlayerCount));
			cachedMaxPlayers = Math.Max(detectedMaxPlayers, cachedPlayerCount);
		}
		finally
		{
			Wow64MemoryHelper.CloseHandle(hProcess);
		}
	}

	private static void UpdateCurrentPlanetFromRam(IntPtr hProcess, ulong mainMenu, ulong crpSingleton, int objectivePlanetId)
	{
		// Read _miniatures NativeList<Miniature> at mainMenu + 0x440 (stride 0x5C = 92 bytes)
		ulong miniaturesListData = ReadPtr(hProcess, mainMenu + 0x440);
		ulong miniaturesPtr = miniaturesListData != 0 ? ReadPtr(hProcess, miniaturesListData + 0x00) : 0;
		int miniaturesLen = miniaturesListData != 0 ? ReadInt32(hProcess, miniaturesListData + 0x08) : 0;

		// Read _planetStationMiniatures NativeList<PlanetStationMiniature> at mainMenu + 0x410 (stride 0x98 = 152 bytes)
		ulong stationsListData = ReadPtr(hProcess, mainMenu + 0x410);
		ulong stationsPtr = stationsListData != 0 ? ReadPtr(hProcess, stationsListData + 0x00) : 0;
		int stationsLen = stationsListData != 0 ? ReadInt32(hProcess, stationsListData + 0x08) : 0;

		int rootMiniatureIdx = ReadInt32(hProcess, mainMenu + 0x380); // _freemoveRootMiniatureIndex
		int mouseMiniatureIdx = ReadInt32(hProcess, mainMenu + 0x3A4); // _freemoveMouseMiniatureIndex
		int selectedStationIdx = ReadInt32(hProcess, mainMenu + 0x3AC); // _freemoveSelectedPlanetStationIndex

		// Cache planet positions while _miniatures is live (UIMainMenu.OnDisable zeroes it once spawned in-game)
		if (miniaturesPtr != 0 && miniaturesLen > 0 && miniaturesLen <= 64)
		{
			byte[] miniBuf = new byte[miniaturesLen * 0x5C];
			if (Wow64MemoryHelper.ReadMemory(hProcess, true, miniaturesPtr, miniBuf, miniBuf.Length, out int miniRead) && miniRead >= miniBuf.Length)
			{
				Dictionary<int, float> stationRadiusByMiniIdx = new Dictionary<int, float>();
				if (stationsPtr != 0 && stationsLen > 0 && stationsLen <= 128)
				{
					byte[] stBuf = new byte[stationsLen * 0x98];
					if (Wow64MemoryHelper.ReadMemory(hProcess, true, stationsPtr, stBuf, stBuf.Length, out int stRead) && stRead >= stBuf.Length)
					{
						for (int s = 0; s < stationsLen; s++)
						{
							int sOff = s * 0x98;
							float srcRadius = BitConverter.ToSingle(stBuf, sOff + 0x7C);
							int linkedMini = BitConverter.ToInt32(stBuf, sOff + 0x88);
							if (linkedMini >= 0 && srcRadius > 100f && !float.IsNaN(srcRadius))
							{
								stationRadiusByMiniIdx[linkedMini] = srcRadius;
							}
						}
					}
				}

				Dictionary<int, double[]> starsByWormhole = new Dictionary<int, double[]>();
				for (int i = 0; i < miniaturesLen; i++)
				{
					int off = i * 0x5C;
					int elemWormhole = BitConverter.ToInt32(miniBuf, off + 0x54);
					int elemType = BitConverter.ToInt32(miniBuf, off + 0x58);
					if (elemType == 0)
					{
						starsByWormhole[elemWormhole] = new double[]
						{
							BitConverter.ToSingle(miniBuf, off + 0x3C),
							BitConverter.ToSingle(miniBuf, off + 0x40),
							BitConverter.ToSingle(miniBuf, off + 0x44)
						};
					}
				}

				List<PlanetOrbitEntry> newOrbits = new List<PlanetOrbitEntry>();
				for (int i = 0; i < miniaturesLen; i++)
				{
					int off = i * 0x5C;
					int ulid = BitConverter.ToInt32(miniBuf, off + 0x00);
					int elemWormhole = BitConverter.ToInt32(miniBuf, off + 0x54);
					int elemType = BitConverter.ToInt32(miniBuf, off + 0x58);

					if (elemType != 1 || !IsKnownPlanetId(ulid))
					{
						continue;
					}

					double starX = 0.0;
					double starY = 0.0;
					double starZ = 0.0;
					if (starsByWormhole.TryGetValue(elemWormhole, out double[] starPos))
					{
						starX = starPos[0];
						starY = starPos[1];
						starZ = starPos[2];
					}

					float miniX = BitConverter.ToSingle(miniBuf, off + 0x3C);
					float miniY = BitConverter.ToSingle(miniBuf, off + 0x40);
					float miniZ = BitConverter.ToSingle(miniBuf, off + 0x44);
					float distMul = BitConverter.ToSingle(miniBuf, off + 0x50);

					double invMul = (distMul > 1e-6f && !float.IsNaN(distMul)) ? (1.0 / distMul) : 1.0;
					float realRadius = stationRadiusByMiniIdx.TryGetValue(i, out float sr) && sr > 100f ? sr : 2000f;

					newOrbits.Add(new PlanetOrbitEntry
					{
						Ulid = ulid,
						Wormhole = elemWormhole,
						RealX = starX + (miniX - starX) * invMul,
						RealY = starY + (miniY - starY) * invMul,
						RealZ = starZ + (miniZ - starZ) * invMul,
						MaxOrbitRadius = Math.Max(realRadius * 2.5, realRadius + 1500.0)
					});
				}

				if (newOrbits.Count > 0)
				{
					cachedPlanetOrbits = newOrbits;
				}
			}
		}

		// 1. Check live universe coordinates in CustomRenderPipeline._singleton when flying/on surface in-game
		if (cachedSubmenu != 0 && crpSingleton != 0)
		{
			double camX = ReadDouble(hProcess, crpSingleton + 0xB8);
			double camY = ReadDouble(hProcess, crpSingleton + 0xC0);
			double camZ = ReadDouble(hProcess, crpSingleton + 0xC8);
			int wormholeIdx = ReadInt32(hProcess, crpSingleton + 0x394);

			bool hasValidPos = !double.IsNaN(camX) && !double.IsInfinity(camX) &&
							   !double.IsNaN(camY) && !double.IsInfinity(camY) &&
							   !double.IsNaN(camZ) && !double.IsInfinity(camZ) &&
							   (Math.Abs(camX) > 0.001 || Math.Abs(camY) > 0.001 || Math.Abs(camZ) > 0.001);

			if (hasValidPos)
			{
				// UIMainMenu stores wormhole index inverted: 0 -> -1, 1 -> -2
				int expectedMiniWormhole = (wormholeIdx == 0 || wormholeIdx == 1) ? (-1 - wormholeIdx) : wormholeIdx;

				bool hasMatchingOrbits = false;
				int closestPlanetUlid = 0;
				double closestDist = double.MaxValue;
				double closestMaxOrbitRadius = 2500.0;

				for (int i = 0; i < cachedPlanetOrbits.Count; i++)
				{
					PlanetOrbitEntry entry = cachedPlanetOrbits[i];
					if (entry.Wormhole != expectedMiniWormhole)
					{
						continue;
					}

					hasMatchingOrbits = true;
					double dx = camX - entry.RealX;
					double dy = camY - entry.RealY;
					double dz = camZ - entry.RealZ;
					double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

					if (dist < closestDist)
					{
						closestDist = dist;
						closestPlanetUlid = entry.Ulid;
						closestMaxOrbitRadius = entry.MaxOrbitRadius;
					}
				}

				if (hasMatchingOrbits)
				{
					hasPlanetAnchorCam = false;
					if (closestPlanetUlid != 0 && closestDist <= closestMaxOrbitRadius)
					{
						cachedPlanetObjectId = closestPlanetUlid;
						cachedInDeepSpace = false;
						return;
					}
					if (Math.Abs(camX) > 1.0 || Math.Abs(camY) > 1.0 || Math.Abs(camZ) > 1.0)
					{
						cachedInDeepSpace = true;
						return;
					}
				}
				else if (miniaturesPtr == 0 && (cachedPlanetObjectId != 0 || objectivePlanetId != 0))
				{
					if (!hasPlanetAnchorCam)
					{
						anchorCamX = camX;
						anchorCamY = camY;
						anchorCamZ = camZ;
						hasPlanetAnchorCam = true;
					}
					else
					{
						double dx = camX - anchorCamX;
						double dy = camY - anchorCamY;
						double dz = camZ - anchorCamZ;
						double distFromAnchor = Math.Sqrt(dx * dx + dy * dy + dz * dz);
						if (distFromAnchor > 5500.0)
						{
							cachedInDeepSpace = true;
							return;
						}
						cachedInDeepSpace = false;
					}
				}
			}
		}

		// 2. Check selected station in solar system / launch view
		if (stationsPtr != 0 && selectedStationIdx >= 0 && selectedStationIdx < stationsLen && stationsLen <= 128)
		{
			ulong stAddr = stationsPtr + (ulong)(selectedStationIdx * 0x98);
			int stationId = ReadInt32(hProcess, stAddr + 0x84);
			int linkedMiniIdx = ReadInt32(hProcess, stAddr + 0x88);
			int mappedPlanet = MapStationOrPlanetId(stationId);
			if (mappedPlanet != 0)
			{
				cachedPlanetObjectId = mappedPlanet;
				cachedInDeepSpace = false;
				return;
			}
			if (miniaturesPtr != 0 && linkedMiniIdx >= 0 && linkedMiniIdx < miniaturesLen)
			{
				int ulid = ReadInt32(hProcess, miniaturesPtr + (ulong)(linkedMiniIdx * 0x5C));
				if (IsKnownPlanetId(ulid))
				{
					cachedPlanetObjectId = ulid;
					cachedInDeepSpace = false;
					return;
				}
			}
		}

		// 3. Check zoomed/selected planet miniature in solar system view (_freemoveRootMiniatureIndex)
		if (miniaturesPtr != 0 && rootMiniatureIdx >= 0 && rootMiniatureIdx < miniaturesLen && miniaturesLen <= 64)
		{
			int ulid = ReadInt32(hProcess, miniaturesPtr + (ulong)(rootMiniatureIdx * 0x5C));
			int mapped = MapStationOrPlanetId(ulid);
			if (mapped != 0)
			{
				cachedPlanetObjectId = mapped;
				cachedInDeepSpace = false;
				return;
			}
		}

		// 4. Check hovered planet miniature in solar system view (_freemoveMouseMiniatureIndex)
		if (miniaturesPtr != 0 && mouseMiniatureIdx >= 0 && mouseMiniatureIdx < miniaturesLen && miniaturesLen <= 64)
		{
			int ulid = ReadInt32(hProcess, miniaturesPtr + (ulong)(mouseMiniatureIdx * 0x5C));
			int mapped = MapStationOrPlanetId(ulid);
			if (mapped != 0)
			{
				cachedPlanetObjectId = mapped;
				cachedInDeepSpace = false;
				return;
			}
		}

		// 5. Check Objective Log selected planet
		int objMapped = MapStationOrPlanetId(objectivePlanetId);
		if (objMapped != 0)
		{
			cachedPlanetObjectId = objMapped;
			cachedInDeepSpace = false;
		}
	}

	private static void RefreshFromDisk()
	{
		string saveRoot = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"AppData", "LocalLow", "ApproximatelyGames", "ApproximatelyUp");

		if (!Directory.Exists(saveRoot))
		{
			return;
		}

		string gameSavPath = Path.Combine(saveRoot, "General", "game.sav");
		string lastWorldFile = null;
		DateTime gameSavWriteUtc = DateTime.MinValue;

		if (File.Exists(gameSavPath))
		{
			gameSavWriteUtc = File.GetLastWriteTimeUtc(gameSavPath);
			var gameSav = TryReadJson<GameSaveData>(gameSavPath);
			lastWorldFile = gameSav?._lastWorldFilename;
		}

		string worldPath = ResolveWorldPath(saveRoot, lastWorldFile);
		DateTime worldWriteUtc = DateTime.MinValue;

		if (!string.IsNullOrEmpty(worldPath) && File.Exists(worldPath))
		{
			worldWriteUtc = File.GetLastWriteTimeUtc(worldPath);
			var world = TryReadJson<WorldSaveData>(worldPath);
			if (world != null)
			{
				string diskWorldName = string.IsNullOrWhiteSpace(world._name)
					? Path.GetFileNameWithoutExtension(worldPath)
					: world._name.Trim();
				int diskProgress = ComputeWorldSaveProgressFromDisk(world._objectives);

				if (cachedSubmenu == 1 || (cachedSubmenu < 0 && cachedInWorld))
				{
					if (string.IsNullOrWhiteSpace(cachedWorldName))
					{
						cachedWorldName = diskWorldName;
					}
					if (cachedWorldProgress < 0 && diskProgress >= 0)
					{
						cachedWorldProgress = diskProgress;
					}
				}
			}
		}

		if (cachedSubmenu < 0)
		{
			cachedInWorld = worldWriteUtc >= sessionStartUtc.AddSeconds(-2);
		}
	}

	private static bool IsKnownPlanetId(int id)
	{
		return MapStationOrPlanetId(id) != 0;
	}

	private static int MapStationOrPlanetId(int id)
	{
		switch (id)
		{
			case 2:
			case 2000:
			case 2999:
				return 2; // Earth
			case 3:
			case 3000:
				return 3; // Moon
			case 4:
			case 4000:
			case 4001:
				return 4; // Baobara
			case 5:
			case 5000:
				return 5; // Helirion
			case 6:
			case 6000:
			case 6001:
				return 6; // Aundara
			case 7:
			case 7000:
				return 7; // Corona Silva
			case 8:
			case 8000:
			case 8001:
				return 8; // Basalt
			case 10:
			case 10000:
				return 10; // Rimshell
			case 11:
			case 11000:
			case 11001:
				return 11; // Outcast
			case 12:
				return 12; // Kovo
			case 100:
			case 13000:
				return 100; // Ashbelt
			case 101:
			case 14000:
				return 101; // Ascensia
			case 102:
			case 15000:
				return 102; // Goldtwin
			case 103:
			case 16000:
				return 103; // Pinktwin
			case 104:
				return 104; // Tenebra
			default:
				return 0;
		}
	}

	private static string FormatPlanetWithEmoji(int planetId, bool inDeepSpace)
	{
		if (inDeepSpace)
		{
			return "🚀 In Space (Traveling)";
		}

		switch (MapStationOrPlanetId(planetId))
		{
			case 2: return "🌍 Earth";
			case 3: return "🌕 Moon";
			case 4: return "🪐 Baobara";
			case 5: return "🟠 Helirion";
			case 6: return "🔵 Aundara";
			case 7: return "🟢 Corona Silva";
			case 8: return "🟤 Basalt";
			case 10: return "🔴 Rimshell";
			case 11: return "⚪ Outcast";
			case 12: return "🟣 Kovo";
			case 100: return "⚫ Ashbelt";
			case 101: return "🟢 Ascensia";
			case 102: return "🟡 Goldtwin";
			case 103: return "🩷 Pinktwin";
			case 104: return "⚫ Tenebra";
			default: return "🌍 Earth";
		}
	}

	private static void ApplyToSnapshot(PresenceSnapshot snap)
	{
		bool inMenu = (cachedSubmenu == 0) || (!cachedInWorld && string.IsNullOrWhiteSpace(cachedWorldName));
		int players = Math.Max(1, cachedPlayerCount);
		int maxPlayers = Math.Max(cachedMaxPlayers, players);

		if (!inMenu && !string.IsNullOrWhiteSpace(cachedWorldName))
		{
			snap.State = "In Game";
			string worldDisplay = FormatWorldWithProgress(cachedWorldName, cachedWorldProgress);
			snap.Details = $"🌍 Mundo: {worldDisplay} • 👥 {players}/{maxPlayers}";

			string planetDisplay = FormatPlanetWithEmoji(cachedPlanetObjectId, cachedInDeepSpace);
			string modeDisplay = cachedInFlightMode ? "🚀 Modo Voo" : "🔧 Modo Criação";
			snap.State = $"{planetDisplay} • {modeDisplay}";

			snap.Set("world", worldDisplay);
			snap.Set("planet", planetDisplay);
			snap.Set("mode", modeDisplay);
			snap.Set("players", $"{players}/{maxPlayers}");
			if (cachedWorldProgress >= 0)
			{
				snap.Set("progress", $"{cachedWorldProgress}%");
			}
		}
		else
		{
			snap.State = "Menu";
			snap.Details = $"🚀 No Menu Principal • 👥 {players}/{maxPlayers}";
			if (!string.IsNullOrWhiteSpace(cachedMenuSelectedWorldName))
			{
				string selectedDisplay = FormatWorldWithProgress(cachedMenuSelectedWorldName, cachedMenuSelectedWorldProgress);
				snap.State = $"🌍 Mundo: {selectedDisplay}";
				snap.Set("world", selectedDisplay);
				if (cachedMenuSelectedWorldProgress >= 0)
				{
					snap.Set("progress", $"{cachedMenuSelectedWorldProgress}%");
				}
			}
			else
			{
				snap.State = "Aguardando decolagem";
			}
		}
	}

	private static string ResolveWorldPath(string saveRoot, string lastWorldFilename)
	{
		string worldsDir = Path.Combine(saveRoot, "Worlds");
		if (!Directory.Exists(worldsDir))
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(lastWorldFilename))
		{
			string direct = Path.Combine(worldsDir, lastWorldFilename);
			if (File.Exists(direct))
			{
				return direct;
			}

			if (!lastWorldFilename.EndsWith(".world", StringComparison.OrdinalIgnoreCase))
			{
				string withExt = Path.Combine(worldsDir, lastWorldFilename + ".world");
				if (File.Exists(withExt))
				{
					return withExt;
				}
			}
		}

		string newest = null;
		DateTime newestTime = DateTime.MinValue;
		foreach (string file in Directory.EnumerateFiles(worldsDir, "*.world", SearchOption.TopDirectoryOnly))
		{
			DateTime t = File.GetLastWriteTimeUtc(file);
			if (t > newestTime)
			{
				newestTime = t;
				newest = file;
			}
		}

		return newest;
	}

	private static string FormatWorldWithProgress(string worldName, int progress)
	{
		if (string.IsNullOrWhiteSpace(worldName) || progress < 0 || progress > 100)
		{
			return worldName;
		}

		return $"{worldName} ({progress}%)";
	}

	private static void UpdateValidObjectivesFromCore(IntPtr hProcess, ulong coreSingleton)
	{
		if (cachedValidObjectiveIds != null && cachedValidObjectiveIds.Count > 0)
		{
			return;
		}

		ulong objectivesArr = ReadPtr(hProcess, coreSingleton + 0x30);
		if (objectivesArr == 0)
		{
			return;
		}

		int len = ReadInt32(hProcess, objectivesArr + 0x18);
		if (len <= 0 || len > 1024)
		{
			return;
		}

		byte[] ptrBuf = new byte[len * 8];
		if (!Wow64MemoryHelper.ReadMemory(hProcess, true, objectivesArr + 0x20, ptrBuf, ptrBuf.Length, out int br) || br < ptrBuf.Length)
		{
			return;
		}

		HashSet<int> validIds = new HashSet<int>();
		for (int i = 0; i < len; i++)
		{
			ulong objPtr = BitConverter.ToUInt64(ptrBuf, i * 8);
			if (objPtr == 0)
			{
				continue;
			}

			byte isHidden = ReadByte(hProcess, objPtr + 0x20);
			if (isHidden != 0)
			{
				continue;
			}

			int objId = ReadInt32(hProcess, objPtr + 0x18);
			validIds.Add(objId);
		}

		if (validIds.Count > 0)
		{
			cachedValidObjectiveIds = validIds;
			cachedTotalValidObjectives = validIds.Count;
		}
	}

	private static int ComputeWorldSaveProgressFromRam(IntPtr hProcess, ulong worldSave)
	{
		if (worldSave == 0)
		{
			return -1;
		}

		ulong dict = ReadPtr(hProcess, worldSave + 0x48);
		if (dict == 0)
		{
			return -1;
		}

		int dictCount = ReadInt32(hProcess, dict + 0x20);
		if (dictCount <= 0)
		{
			return 0;
		}

		ulong entriesArr = ReadPtr(hProcess, dict + 0x18);
		if (entriesArr == 0)
		{
			return -1;
		}

		int entriesLen = ReadInt32(hProcess, entriesArr + 0x18);
		int scanCount = Math.Min(dictCount, entriesLen);
		if (scanCount <= 0 || scanCount > 2048)
		{
			return -1;
		}

		byte[] buf = new byte[scanCount * 16];
		if (!Wow64MemoryHelper.ReadMemory(hProcess, true, entriesArr + 0x20, buf, buf.Length, out int br) || br < buf.Length)
		{
			return -1;
		}

		int completedCount = 0;
		bool filterIds = cachedValidObjectiveIds != null && cachedValidObjectiveIds.Count > 0;
		for (int i = 0; i < scanCount; i++)
		{
			int off = i * 16;
			int hashCode = BitConverter.ToInt32(buf, off + 0x00);
			if (hashCode < 0)
			{
				continue;
			}

			int key = BitConverter.ToInt32(buf, off + 0x08);
			byte completed = buf[off + 0x0C];
			if (completed == 0)
			{
				continue;
			}

			if (!filterIds || cachedValidObjectiveIds.Contains(key))
			{
				completedCount++;
			}
		}

		return CalculateObjectivePercent(completedCount);
	}

	private static int ComputeWorldSaveProgressFromDisk(WorldObjectivesData objectives)
	{
		if (objectives == null || objectives.m_Keys == null || objectives.m_Values == null)
		{
			return -1;
		}

		int n = Math.Min(objectives.m_Keys.Count, objectives.m_Values.Count);
		if (n == 0)
		{
			return 0;
		}

		int completedCount = 0;
		bool filterIds = cachedValidObjectiveIds != null && cachedValidObjectiveIds.Count > 0;
		for (int i = 0; i < n; i++)
		{
			ObjectiveEntryData entry = objectives.m_Values[i];
			if (entry == null || entry._completed <= 0)
			{
				continue;
			}

			int key = objectives.m_Keys[i];
			if (!filterIds || cachedValidObjectiveIds.Contains(key))
			{
				completedCount++;
			}
		}

		return CalculateObjectivePercent(completedCount);
	}

	private static int CalculateObjectivePercent(int completedCount)
	{
		int total = cachedTotalValidObjectives > 0 ? cachedTotalValidObjectives : 80;
		int pct = (int)((completedCount / (float)total) * 100.0f + 0.5f);
		return Math.Max(0, Math.Min(100, pct));
	}

	private static ulong ReadWorldSaveFromList(IntPtr hProcess, ulong listAddr, int index, out string worldName)
	{
		worldName = null;
		if (listAddr == 0 || index < 0 || index >= 64)
		{
			return 0;
		}

		int size = ReadInt32(hProcess, listAddr + 0x18);
		if (index >= size)
		{
			return 0;
		}

		ulong itemsArray = ReadPtr(hProcess, listAddr + 0x10);
		if (itemsArray == 0)
		{
			return 0;
		}

		ulong uiMainMenuWorld = ReadPtr(hProcess, itemsArray + 0x20 + (ulong)(index * 8));
		if (uiMainMenuWorld == 0)
		{
			return 0;
		}

		ulong diskWorldSave = ReadPtr(hProcess, uiMainMenuWorld + 0x40); // UIMainMenuWorld._world
		if (diskWorldSave == 0)
		{
			return 0;
		}

		ulong worldSave = ReadPtr(hProcess, diskWorldSave + 0x10); // DiskWorldSave._world
		if (worldSave == 0)
		{
			return 0;
		}

		worldName = ReadIl2CppString(hProcess, ReadPtr(hProcess, worldSave + 0x10)); // WorldSave._name
		return worldSave;
	}

	private static ulong ReadStaticSingleton(IntPtr hProcess, ulong typeInfoAddr, ulong staticFieldOffset)
	{
		ulong typeInfo = ReadPtr(hProcess, typeInfoAddr);
		if (typeInfo == 0)
		{
			return 0;
		}

		ulong staticFields = ReadPtr(hProcess, typeInfo + 0xB8);
		if (staticFields == 0)
		{
			return 0;
		}

		return ReadPtr(hProcess, staticFields + staticFieldOffset);
	}

	private static int ReadDictionaryCount(IntPtr hProcess, ulong dictAddr)
	{
		if (dictAddr == 0)
		{
			return 0;
		}

		int count = ReadInt32(hProcess, dictAddr + 0x20);
		int freeCount = ReadInt32(hProcess, dictAddr + 0x24);
		int active = count - freeCount;
		return active >= 0 && active <= MaxModdedLobbyPlayers ? active : 0;
	}

	private static int ReadListCount(IntPtr hProcess, ulong listAddr)
	{
		if (listAddr == 0)
		{
			return 0;
		}

		int size = ReadInt32(hProcess, listAddr + 0x18);
		return size >= 0 && size <= MaxModdedLobbyPlayers ? size : 0;
	}

	private static int ReadListCapacity(IntPtr hProcess, ulong listAddr)
	{
		if (listAddr == 0)
		{
			return 0;
		}

		ulong itemsArray = ReadPtr(hProcess, listAddr + 0x10);
		if (itemsArray == 0)
		{
			return 0;
		}

		int len = ReadInt32(hProcess, itemsArray + 0x18);
		return len >= 0 && len <= MaxModdedLobbyPlayers ? len : 0;
	}

	private static string ReadIl2CppString(IntPtr hProcess, ulong strAddr)
	{
		if (strAddr == 0)
		{
			return null;
		}

		int charCount = ReadInt32(hProcess, strAddr + 0x10);
		if (charCount <= 0 || charCount > 256)
		{
			return null;
		}

		byte[] buf = new byte[charCount * 2];
		if (!Wow64MemoryHelper.ReadMemory(hProcess, true, strAddr + 0x14, buf, buf.Length, out int br) || br < buf.Length)
		{
			return null;
		}

		return Encoding.Unicode.GetString(buf);
	}

	private static ulong ReadPtr(IntPtr hProcess, ulong addr)
	{
		return ReadUInt64(hProcess, addr);
	}

	private static ulong ReadUInt64(IntPtr hProcess, ulong addr)
	{
		if (addr == 0) return 0;
		byte[] buf = new byte[8];
		if (Wow64MemoryHelper.ReadMemory(hProcess, true, addr, buf, 8, out int br) && br == 8)
		{
			return BitConverter.ToUInt64(buf, 0);
		}
		return 0;
	}

	private static int ReadInt32(IntPtr hProcess, ulong addr)
	{
		if (addr == 0) return 0;
		byte[] buf = new byte[4];
		if (Wow64MemoryHelper.ReadMemory(hProcess, true, addr, buf, 4, out int br) && br == 4)
		{
			return BitConverter.ToInt32(buf, 0);
		}
		return 0;
	}

	private static float ReadFloat(IntPtr hProcess, ulong addr)
	{
		if (addr == 0) return 0f;
		byte[] buf = new byte[4];
		if (Wow64MemoryHelper.ReadMemory(hProcess, true, addr, buf, 4, out int br) && br == 4)
		{
			return BitConverter.ToSingle(buf, 0);
		}
		return 0f;
	}

	private static byte ReadByte(IntPtr hProcess, ulong addr)
	{
		if (addr == 0) return 0;
		byte[] buf = new byte[1];
		if (Wow64MemoryHelper.ReadMemory(hProcess, true, addr, buf, 1, out int br) && br == 1)
		{
			return buf[0];
		}
		return 0;
	}

	private static double ReadDouble(IntPtr hProcess, ulong addr)
	{
		if (addr == 0) return 0;
		byte[] buf = new byte[8];
		if (Wow64MemoryHelper.ReadMemory(hProcess, true, addr, buf, 8, out int br) && br == 8)
		{
			return BitConverter.ToDouble(buf, 0);
		}
		return 0;
	}

	private static T TryReadJson<T>(string path) where T : class
	{
		try
		{
			string json = File.ReadAllText(path);
			if (string.IsNullOrWhiteSpace(json))
			{
				return null;
			}
			return JsonHelper.Deserialize<T>(json);
		}
		catch
		{
			return null;
		}
	}
}
