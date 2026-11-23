using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using MultiPresenceProfiles.Common;

namespace MultiPresenceProfiles.Games.LeMansUltimate;

public static class LeMansUltimateProvider
{
	public const string GameName = "Le Mans Ultimate";
	public const string DiscordAppId = "1463577784394973392";

	private static MemoryMappedFile mappedFile;
	private static MemoryMappedViewAccessor accessor;
	private static double lastEt = -1.0;
	private static int staleCounter = 0;

	public static bool Read(PresenceSnapshot snap)
	{
		try
		{
			if (accessor == null)
			{
				try
				{
					mappedFile = MemoryMappedFile.OpenExisting("LMU_Data", MemoryMappedFileRights.Read);
				}
				catch
				{
					try
					{
						mappedFile = MemoryMappedFile.OpenExisting(@"Local\LMU_Data", MemoryMappedFileRights.Read);
					}
					catch
					{
						return false;
					}
				}
				accessor = mappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
			}

			// LMUObjectOut layout (pack=4):
			// generic: 332 bytes
			// paths: 1300 bytes (5 * 260)
			// scoring offset: 1632 bytes
			long scoringOffset = 1632;

			// LMUScoringInfo:
			// mTrackName: char[64] at offset 0
			// mSession: int at offset 64
			// mCurrentET: double at offset 68
			// mEndET: double at offset 76
			// mMaxLaps: int at offset 84
			// mLapDist: double at offset 88
			// mResultsStreamPointer: ulong at offset 96
			// mNumVehicles: int at offset 104
			// mGamePhase: int at offset 108
			// ...
			// vehScoringInfo array begins after scoringInfo (size ~532 bytes) + scoringStreamSize (12 bytes) = offset ~2176
			byte[] trackBytes = new byte[64];
			accessor.ReadArray(scoringOffset, trackBytes, 0, 64);
			string rawTrackName = DecodeString(trackBytes);

			int session = accessor.ReadInt32(scoringOffset + 64);
			double currentEt = accessor.ReadDouble(scoringOffset + 68);
			double endEt = accessor.ReadDouble(scoringOffset + 76);
			int maxLaps = accessor.ReadInt32(scoringOffset + 84);
			int numVehicles = accessor.ReadInt32(scoringOffset + 104);
			int gamePhase = accessor.ReadInt32(scoringOffset + 108);

			if (currentEt == lastEt)
			{
				staleCounter++;
			}
			else
			{
				staleCounter = 0;
				lastEt = currentEt;
			}

			snap.DiscordAppId = DiscordAppId;
			

			// Check if in menu or game is not rendering
			if (staleCounter > 3 || numVehicles <= 0 || string.IsNullOrWhiteSpace(rawTrackName))
			{
				snap.State = "In Main Menu";
				snap.Details = snap.State;
				snap.State = null;
				snap.LargeImageKey = "lmu_logo";
				snap.LargeImageText = "Le Mans Ultimate";
				snap.SmallImageKey = null;
				snap.SmallImageText = null;
				return true;
			}

			// Walk vehScoringInfo to find player
			// VehicleScoringInfoV01 struct stride in LMU: ~232 bytes
			long vehScoringBase = scoringOffset + 532 + 12;
			int vehStride = 232;

			int playerPlace = 1;
			int playerTotalLaps = 0;
			string rawVehicleName = "";
			string rawVehicleClass = "";
			string rawVehFilename = "";
			bool playerFound = false;

			List<Tuple<string, int, bool>> vehicles = new List<Tuple<string, int, bool>>();

			int safeVehCount = Math.Min(numVehicles, 104);
			for (int i = 0; i < safeVehCount; i++)
			{
				long vehOffset = vehScoringBase + (i * vehStride);
				if (vehOffset + vehStride > accessor.Capacity)
				{
					break;
				}

				byte[] nameBytes = new byte[64];
				accessor.ReadArray(vehOffset + 36, nameBytes, 0, 64);
				string vName = DecodeString(nameBytes);

				short totalLaps = accessor.ReadInt16(vehOffset + 100);
				bool isPlayer = accessor.ReadBoolean(vehOffset + 168);
				byte place = accessor.ReadByte(vehOffset + 171);

				byte[] classBytes = new byte[32];
				accessor.ReadArray(vehOffset + 172, classBytes, 0, 32);
				string vClass = DecodeString(classBytes);

				byte[] fileBytes = new byte[32];
				accessor.ReadArray(vehOffset + 224, fileBytes, 0, 32);
				string vFile = DecodeString(fileBytes);

				vehicles.Add(Tuple.Create(vClass, (int)place, isPlayer));

				if (isPlayer)
				{
					playerFound = true;
					playerPlace = place;
					playerTotalLaps = totalLaps;
					rawVehicleName = vName;
					rawVehicleClass = vClass;
					rawVehFilename = vFile;
				}
			}

			if (!playerFound && vehicles.Count > 0)
			{
				rawVehicleName = "Le Mans Ultimate";
			}

			int classCars = 0;
			int classPos = playerPlace;
			if (!string.IsNullOrWhiteSpace(rawVehicleClass))
			{
				int countInClass = 0;
				int higherInClass = 0;
				foreach (var v in vehicles)
				{
					if (string.Equals(v.Item1, rawVehicleClass, StringComparison.OrdinalIgnoreCase))
					{
						countInClass++;
						if (v.Item2 > 0 && v.Item2 < playerPlace)
						{
							higherInClass++;
						}
					}
				}
				if (countInClass > 0)
				{
					classCars = countInClass;
					classPos = higherInClass + 1;
				}
			}

			string trackName = NormalizeTrackName(rawTrackName);
			string carName = ResolveCarName(rawVehicleName, rawVehFilename, rawVehicleClass);
			string carAsset = ResolveCarAsset(rawVehicleName, rawVehFilename, rawVehicleClass);
			string trackAsset = ResolveTrackAsset(trackName);

			string sessionName = ResolveSessionName(session);
			string posText = classCars > 0 && classPos > 0 ? $"P{classPos}/{classCars}" : $"P{playerPlace}";

			// Time or lap info
			string timeOrLap = "";
			bool hasValidTimer = endEt > 0 && endEt < 172800;
			double timeLeft = hasValidTimer ? endEt - currentEt : 0;

			if (gamePhase == 0)
			{
				timeOrLap = "Waiting for session...";
			}
			else if (gamePhase == 1 || gamePhase == 2)
			{
				timeOrLap = "On the Grid";
			}
			else if (gamePhase == 3)
			{
				timeOrLap = "Formation Lap";
			}
			else if (gamePhase == 4)
			{
				timeOrLap = "Countdown";
			}
			else if (maxLaps > 0 && maxLaps < 1000)
			{
				int currentLap = Math.Max(1, playerTotalLaps + 1);
				int lapsLeft = Math.Max(0, maxLaps - currentLap + 1);
				timeOrLap = lapsLeft <= 1 ? "Final Lap" : $"Lap {currentLap}/{maxLaps}";
			}
			else if (hasValidTimer && timeLeft > 0)
			{
				timeOrLap = FormatClock(timeLeft);
			}
			else
			{
				int currentLap = Math.Max(1, playerTotalLaps + 1);
				timeOrLap = $"Lap {currentLap}";
			}

			snap.Details = $"{sessionName} | {posText} | {timeOrLap}";
			snap.State = $"Driving {carName} at {trackName}";

			// Dynamic image switching: alternate car and track or keep car large and track small
			long secondsNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			bool showTrackLarge = (secondsNow % 15) >= 10 && !string.IsNullOrWhiteSpace(trackAsset);

			if (showTrackLarge)
			{
				snap.LargeImageKey = trackAsset;
				snap.LargeImageText = trackName;
				snap.SmallImageKey = carAsset;
				snap.SmallImageText = carName;
			}
			else
			{
				snap.LargeImageKey = !string.IsNullOrWhiteSpace(carAsset) ? carAsset : "lmu_logo_default";
				snap.LargeImageText = carName;
				snap.SmallImageKey = trackAsset;
				snap.SmallImageText = trackName;
			}

			snap.Set("car", carName);
			snap.Set("track", trackName);
			snap.Set("session", sessionName);
			snap.Set("position", playerPlace);
			snap.Set("class_position", classPos);
			snap.Set("lap", playerTotalLaps + 1);
			snap.Set("total_laps", maxLaps);
			snap.Set("time_left", formatTime(timeLeft));

			return true;
		}
		catch
		{
			Reset();
			return false;
		}
	}

	public static void Reset()
	{
		try
		{
			accessor?.Dispose();
			accessor = null;
			mappedFile?.Dispose();
			mappedFile = null;
		}
		catch { }
		lastEt = -1.0;
		staleCounter = 0;
	}

	private static string DecodeString(byte[] bytes)
	{
		if (bytes == null || bytes.Length == 0) return "";
		int len = 0;
		while (len < bytes.Length && bytes[len] != 0) len++;
		return Encoding.UTF8.GetString(bytes, 0, len).Trim();
	}

	private static string FormatClock(double seconds)
	{
		int s = Math.Max(0, (int)seconds);
		int hours = s / 3600;
		int rem = s % 3600;
		int mins = rem / 60;
		int secs = rem % 60;
		return hours > 0 ? $"{hours}:{mins:D2}:{secs:D2}" : $"{mins:D2}:{secs:D2}";
	}

	private static string formatTime(double seconds)
	{
		if (seconds <= 0) return "";
		return FormatClock(seconds);
	}

	private static string ResolveSessionName(int session)
	{
		switch (session)
		{
			case 0: return "Test Day";
			case 1:
			case 2:
			case 3:
			case 4: return "Practice";
			case 5:
			case 6:
			case 7:
			case 8: return "Qualifying";
			case 9: return "Warmup";
			case 10:
			case 11:
			case 12:
			case 13: return "Race";
			default: return "In Main Menu";
		}
	}

	private static string NormalizeTrackName(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return "Le Mans";
		string s = raw.ToLowerInvariant();
		if (s.Contains("sarthe") || s.Contains("le mans")) return "Le Mans";
		if (s.Contains("spa") || s.Contains("francorchamps")) return "Spa-Francorchamps";
		if (s.Contains("monza")) return "Monza";
		if (s.Contains("sebring")) return "Sebring";
		if (s.Contains("bahrain")) return "Bahrain";
		if (s.Contains("portimao") || s.Contains("algarve")) return "Portimão";
		if (s.Contains("fuji")) return "Fuji";
		if (s.Contains("imola") || s.Contains("enzo e dino")) return "Imola";
		if (s.Contains("cota") || s.Contains("americas")) return "COTA";
		if (s.Contains("interlagos") || s.Contains("carlos pace")) return "Interlagos";
		if (s.Contains("qatar") || s.Contains("lusail")) return "Qatar";
		if (s.Contains("silverstone")) return "Silverstone";
		if (s.Contains("ricard") || s.Contains("paul ricard")) return "Paul Ricard";
		if (s.Contains("barcelona") || s.Contains("catalunya")) return "Barcelona";
		if (s.Contains("daytona")) return "Daytona";
		if (s.Contains("laguna") || s.Contains("laguna seca")) return "Laguna Seca";
		if (s.Contains("atlanta") || s.Contains("road atlanta")) return "Road Atlanta";
		if (s.Contains("long beach")) return "Long Beach";
		return raw;
	}

	private static string ResolveTrackAsset(string trackName)
	{
		string s = (trackName ?? "").ToLowerInvariant();
		if (s.Contains("le mans")) return "lemans-sarthe";
		if (s.Contains("spa")) return "spa";
		if (s.Contains("monza")) return "monza";
		if (s.Contains("sebring")) return "sebring";
		if (s.Contains("bahrain")) return "bahrain";
		if (s.Contains("portim")) return "portimao";
		if (s.Contains("fuji")) return "fuji";
		if (s.Contains("imola")) return "imola";
		if (s.Contains("cota")) return "cota";
		if (s.Contains("interlagos")) return "interlagos";
		if (s.Contains("qatar") || s.Contains("lusail")) return "lusail";
		if (s.Contains("silverstone")) return "silverstone";
		if (s.Contains("paul ricard") || s.Contains("ricard")) return "paulricard";
		if (s.Contains("barcelona")) return "barcelona-catalunya";
		if (s.Contains("daytona")) return "daytona";
		if (s.Contains("laguna")) return "laguna_seca";
		if (s.Contains("road atlanta") || s.Contains("atlanta")) return "roadatlanta";
		if (s.Contains("long beach")) return "longbeach";
		return "lmu_logo";
	}

	private static string ResolveCarAsset(string vehName, string vehFile, string vehClass)
	{
		string s = $"{vehName} {vehFile} {vehClass}".ToLowerInvariant();
		if (s.Contains("valkyrie")) return "car_aston_martin_valkyrie";
		if (s.Contains("499p")) return "car_ferrari_499p";
		if (s.Contains("963")) return "car_porsche_963";
		if (s.Contains("gr010") || s.Contains("tr010")) return "car_toyota_gr010";
		if (s.Contains("v-series") || s.Contains("cadillac")) return "car_cadillac_v_series_r";
		if (s.Contains("bmw") && (s.Contains("hybrid") || s.Contains("v8"))) return "car_bmw_m_hybrid_v8";
		if (s.Contains("a424") || s.Contains("alpine")) return "car_alpine_a424";
		if (s.Contains("9x8")) return s.Contains("2024") ? "car_peugeot_9x8_2024" : "car_peugeot_9x8";
		if (s.Contains("sc63") || s.Contains("lamborghini sc")) return "car_lamborghini_sc63";
		if (s.Contains("glickenhaus") || s.Contains("007")) return "car_glickenhaus_007";
		if (s.Contains("isotta")) return "car_isotta_fraschini";
		if (s.Contains("vanwall") || s.Contains("680")) return "car_vanwall_680";
		if (s.Contains("genesis") || s.Contains("gmr")) return "car_genesis_gmr001";
		if (s.Contains("296")) return "car_ferrari_296_gt3";
		if (s.Contains("corvette") && s.Contains("z06")) return "car_corvette_z06_gt3";
		if (s.Contains("corvette") && s.Contains("c8")) return "car_corvette_c8r_gte";
		if (s.Contains("mustang")) return "car_ford_mustang_gt3";
		if (s.Contains("720s") || s.Contains("mclaren")) return "car_mclaren_720s_gt3";
		if (s.Contains("mercedes") || s.Contains("amg")) return "car_mercedes_amg_gt3";
		if (s.Contains("m4") || (s.Contains("bmw") && s.Contains("gt3"))) return "car_bmw_m4_gt3";
		if (s.Contains("vantage") && s.Contains("gte")) return "car_aston_martin_vantage_gte";
		if (s.Contains("vantage")) return "car_aston_martin_vantage_gt3";
		if (s.Contains("huracan")) return "car_lamborghini_huracan_gt3";
		if (s.Contains("lexus") || s.Contains("rcf")) return "car_lexus_rcf_gt3";
		if (s.Contains("911") && s.Contains("rsr")) return "car_porsche_911_rsr";
		if (s.Contains("911") || s.Contains("porsche")) return "car_porsche_911_gt3";
		if (s.Contains("ginetta")) return "car_ginetta_g61";
		if (s.Contains("ligier")) return "car_ligier_js_p325";
		if (s.Contains("duqueine")) return "car_duqueine_d09";
		if (s.Contains("adess")) return "car_adess_03";
		if (s.Contains("oreca")) return "car_oreca_07_2023";
		return "lmu_logo_default";
	}

	private static string ResolveCarName(string vehName, string vehFile, string vehClass)
	{
		string s = $"{vehName} {vehFile} {vehClass}".ToLowerInvariant();
		if (s.Contains("valkyrie")) return "Aston Martin Valkyrie";
		if (s.Contains("499p")) return "Ferrari 499P";
		if (s.Contains("963")) return "Porsche 963";
		if (s.Contains("gr010") || s.Contains("tr010")) return "Toyota GR010";
		if (s.Contains("v-series") || s.Contains("cadillac")) return "Cadillac V-Series.R";
		if (s.Contains("bmw") && (s.Contains("hybrid") || s.Contains("v8"))) return "BMW M Hybrid V8";
		if (s.Contains("a424") || s.Contains("alpine")) return "Alpine A424";
		if (s.Contains("9x8")) return s.Contains("2024") ? "Peugeot 9X8 2024" : "Peugeot 9X8";
		if (s.Contains("sc63") || s.Contains("lamborghini sc")) return "Lamborghini SC63";
		if (s.Contains("glickenhaus") || s.Contains("007")) return "Glickenhaus SCG 007";
		if (s.Contains("isotta")) return "Isotta Fraschini";
		if (s.Contains("vanwall") || s.Contains("680")) return "Vanwall Vandervell";
		if (s.Contains("genesis") || s.Contains("gmr")) return "Genesis GMR-001";
		if (s.Contains("296")) return "Ferrari 296 LMGT3";
		if (s.Contains("corvette") && s.Contains("z06")) return "Corvette Z06 GT3.R";
		if (s.Contains("corvette") && s.Contains("c8")) return "Chevrolet Corvette C8.R";
		if (s.Contains("mustang")) return "Ford Mustang LMGT3";
		if (s.Contains("720s") || s.Contains("mclaren")) return "McLaren 720S LMGT3 Evo";
		if (s.Contains("mercedes") || s.Contains("amg")) return "Mercedes-AMG LMGT3";
		if (s.Contains("m4") || (s.Contains("bmw") && s.Contains("gt3"))) return "BMW M4 LMGT3";
		if (s.Contains("vantage") && s.Contains("gte")) return "Aston Martin Vantage GTE";
		if (s.Contains("vantage")) return "Aston Martin Vantage AMR LMGT3";
		if (s.Contains("huracan")) return "Lamborghini Huracan GT3";
		if (s.Contains("lexus") || s.Contains("rcf")) return "Lexus RC F LMGT3";
		if (s.Contains("911") && s.Contains("rsr")) return "Porsche 911 RSR-19";
		if (s.Contains("911") || s.Contains("porsche")) return "Porsche 911 GT3 R";
		if (s.Contains("ginetta")) return "Ginetta G61-LT-P325-Evo";
		if (s.Contains("ligier")) return "Ligier JS P325";
		if (s.Contains("duqueine")) return "Duqueine D09 LMP3";
		if (s.Contains("adess")) return "ADESS AD25 LMP3";
		if (s.Contains("oreca")) return "Oreca 07 Gibson";
		return !string.IsNullOrWhiteSpace(vehName) ? vehName : "Le Mans Ultimate";
	}
}
