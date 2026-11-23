using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using MultiPresenceProfiles.Common;

namespace MultiPresenceProfiles.Games.Pes2021;

public sealed class PesMatchData
{
	public bool in_match { get; set; }
	public string home_name { get; set; }
	public string away_name { get; set; }
	public int home_score { get; set; }
	public int away_score { get; set; }
	public int period { get; set; }
	public int clock_minutes { get; set; }
	public int clock_seconds { get; set; }
	public int added_minutes { get; set; }
	public string tournament { get; set; }
	public string stage { get; set; }
	public string mode { get; set; }
	public string player { get; set; }
	public string coach { get; set; }
	public string team_abbr { get; set; }
	public long timestamp { get; set; }
}

public static class Pes2021Provider
{
	public const string CanonicalGameName = "eFootball PES 2021";

	private static readonly string[] CandidateJsonPaths = new[]
	{
		Path.Combine(Path.GetTempPath(), "pes_rpc.json"),
		@"C:\Windows\Temp\pes_rpc.json"
	};

	public static bool Read(IMemoryAccessor mem, PresenceSnapshot snap)
	{
		try
		{
			

			// Priority 1: Check real-time JSON bridge exported by Sider Lua module
			if (TryReadJsonBridge(snap))
			{
				return true;
			}

			// Priority 2: Direct memory reading from PES2021.exe
			if (TryReadProcessMemory(mem, snap))
			{
				return true;
			}

			// Default menu fallback when attached to process
			snap.Details = null; // Keeps default "eFootball PES 2021"
			snap.State = "In Main Menu";
			snap.Details = snap.State;
			snap.State = null;
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static void Reset()
	{
	}

	private static bool TryReadJsonBridge(PresenceSnapshot snap)
	{
		string bestPath = null;
		DateTime newestWriteUtc = DateTime.MinValue;

		foreach (var path in CandidateJsonPaths)
		{
			try
			{
				if (File.Exists(path))
				{
					DateTime writeTime = File.GetLastWriteTimeUtc(path);
					if (writeTime > newestWriteUtc)
					{
						newestWriteUtc = writeTime;
						bestPath = path;
					}
				}
			}
			catch { }
		}

		if (string.IsNullOrWhiteSpace(bestPath))
		{
			return false;
		}

		try
		{
			// Stale check (older than 30 seconds means game or match bridge stopped)
			if (DateTime.UtcNow - newestWriteUtc > TimeSpan.FromSeconds(30))
			{
				return false;
			}

			string rawJson = "";
			using (var fs = new FileStream(bestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
			using (var reader = new StreamReader(fs, Encoding.UTF8))
			{
				rawJson = reader.ReadToEnd();
			}

			if (string.IsNullOrWhiteSpace(rawJson))
			{
				return false;
			}

			PesMatchData data = JsonHelper.Deserialize<PesMatchData>(rawJson);
			if (data == null)
			{
				return false;
			}

			ApplyDataToSnapshot(snap, data);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void ApplyDataToSnapshot(PresenceSnapshot snap, PesMatchData data)
	{
		if (!data.in_match)
		{
			snap.Details = null;
			string menuMode = !string.IsNullOrWhiteSpace(data.mode) ? data.mode.Trim() : "";
			string menuTeamAbbr = !string.IsNullOrWhiteSpace(data.team_abbr) ? data.team_abbr.Trim() : "";
			if (menuMode.IndexOf("Master", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				string mlMenu = "In Master League";
				snap.State = mlMenu;
				snap.Details = mlMenu;
				if (!string.IsNullOrWhiteSpace(data.coach))
				{
					string coachLabel = "Manager";
					snap.State = !string.IsNullOrEmpty(menuTeamAbbr)
						? $"{coachLabel}: {data.coach.Trim()} | {menuTeamAbbr}"
						: $"{coachLabel}: {data.coach.Trim()}";
				}
				else
				{
					snap.State = !string.IsNullOrEmpty(menuTeamAbbr) ? menuTeamAbbr : null;
				}
			}
			else if (menuMode.IndexOf("Estrelato", StringComparison.OrdinalIgnoreCase) >= 0
				|| menuMode.IndexOf("Legend", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				string blMenu = "In Become a Legend";
				snap.State = blMenu;
				snap.Details = blMenu;
				if (!string.IsNullOrWhiteSpace(data.player))
				{
					string playerLabel = "Player";
					snap.State = !string.IsNullOrEmpty(menuTeamAbbr)
						? $"{playerLabel}: {data.player.Trim()} | {menuTeamAbbr}"
						: $"{playerLabel}: {data.player.Trim()}";
				}
				else
				{
					snap.State = !string.IsNullOrEmpty(menuTeamAbbr) ? menuTeamAbbr : null;
				}
			}
			else
			{
				snap.State = "In Main Menu";
				snap.Details = snap.State;
				snap.State = null;
			}
			snap.Set("in_match", false);
			snap.Set("mode", menuMode);
			return;
		}

		// In Match
		string home = !string.IsNullOrWhiteSpace(data.home_name) ? data.home_name.Trim() : "Mandante";
		string away = !string.IsNullOrWhiteSpace(data.away_name) ? data.away_name.Trim() : "Visitante";
		string score = $"{data.home_score} x {data.away_score}";

		// Discord Activity Title: "Barcelona 2 x 1 Real Madrid"
		snap.Details = $"{home} {score} {away}";

		// Format Match Time: e.g. "45+3 2T" or "30 1T" or "Pênaltis"
		string periodTag = "1T";
		if (data.period == 2) periodTag = "2T";
		else if (data.period == 3) periodTag = "1P";
		else if (data.period == 4) periodTag = "2P";
		else if (data.period == 5) periodTag = "PK";

		string timeStr;
		if (data.added_minutes > 0)
		{
			timeStr = $"{data.clock_minutes}+{data.added_minutes} {periodTag}";
		}
		else
		{
			timeStr = $"{data.clock_minutes} {periodTag}";
		}

		// Competition line
		string competition = !string.IsNullOrWhiteSpace(data.tournament) ? data.tournament.Trim() : "Amistoso";
		if (string.Equals(competition, "Modo Copa", StringComparison.OrdinalIgnoreCase))
		{
			competition = "Cup Mode";
		}
		else if (string.Equals(competition, "Modo Liga", StringComparison.OrdinalIgnoreCase))
		{
			competition = "League Mode";
		}
		string stage = !string.IsNullOrWhiteSpace(data.stage) ? data.stage.Trim() : "";

		bool isFriendly = competition.IndexOf("Amistoso", StringComparison.OrdinalIgnoreCase) >= 0
			|| competition.IndexOf("Exhibition", StringComparison.OrdinalIgnoreCase) >= 0;

		if (isFriendly)
		{
			snap.Details = "Amistoso";
		}
		else
		{
			string compDisplay = !string.IsNullOrWhiteSpace(stage) ? $"{stage}{competition}" : competition;
			snap.Details = $"{compDisplay} | {timeStr}";
		}

		// Mode and Coach / Player line
		string mode = !string.IsNullOrWhiteSpace(data.mode) ? data.mode.Trim() : "";
		string player = !string.IsNullOrWhiteSpace(data.player) ? data.player.Trim() : "";
		string coach = !string.IsNullOrWhiteSpace(data.coach) ? data.coach.Trim() : "";
		string teamAbbr = !string.IsNullOrWhiteSpace(data.team_abbr) ? data.team_abbr.Trim() : "";
		string abbrSuffix = !string.IsNullOrEmpty(teamAbbr) ? $" | {teamAbbr}" : "";

		if (mode.IndexOf("Estrelato", StringComparison.OrdinalIgnoreCase) >= 0 || mode.IndexOf("Legend", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			string modeLabel = "Become a Legend";
			string playerLabel = "Player";
			snap.State = !string.IsNullOrWhiteSpace(player)
				? $"{modeLabel} | {playerLabel}: {player}{abbrSuffix}"
				: $"{modeLabel}{abbrSuffix}";
		}
		else if (mode.IndexOf("Master", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			string modeLabel = "Master League";
			string coachLabel = "Manager";
			snap.State = !string.IsNullOrWhiteSpace(coach)
				? $"{modeLabel} | {coachLabel}: {coach}{abbrSuffix}"
				: $"{modeLabel}{abbrSuffix}";
		}
		else
		{
			snap.State = null;
		}

		// Populate tokens
		snap.Set("team_home", home);
		snap.Set("team_away", away);
		snap.Set("score_home", data.home_score);
		snap.Set("score_away", data.away_score);
		snap.Set("score", score);
		snap.Set("competition", competition);
		snap.Set("stage", stage);
		snap.Set("mode", mode);
		snap.Set("match_time", timeStr);
		snap.Set("player", player);
		snap.Set("coach", coach);
		snap.Set("team_abbr", teamAbbr);
	}

	private static bool TryReadProcessMemory(IMemoryAccessor mem, PresenceSnapshot snap)
	{
		if (!mem.IsAttached)
		{
			return false;
		}

		try
		{
			ulong baseAddr = mem.GetModuleBaseAddress(mem.AttachedProcess?.ProcessName + ".exe");
			if (baseAddr == 0) return false;

			ulong ownerPtr = 0;
			ulong blkPtr = 0;

			// Check CPY owner pointer
			ownerPtr = mem.Read<ulong>(baseAddr + 0x3705e10, true);
			if (ownerPtr != 0)
			{
				blkPtr = mem.Read<ulong>(ownerPtr + 0x48, true);
			}

			// If null, try Steam owner pointer
			if (blkPtr == 0)
			{
				ownerPtr = mem.Read<ulong>(baseAddr + 0x3714240, true);
				if (ownerPtr != 0)
				{
					blkPtr = mem.Read<ulong>(ownerPtr + 0x48, true);
				}
			}

			// Check sys::Status (CPY: 0x3704E38) for live menu mode (19 = Master League, 20 = Become a Legend)
			int gameMode = -1;
			ulong statusInst = mem.Read<ulong>(baseAddr + 0x3704E38, true);
			if (statusInst >= 0x10000)
			{
				gameMode = mem.Read<int>(statusInst + 0xF0, true);
			}

			if (gameMode == 19)
			{
				string mlMenu = "In Master League";
				string coach = ReadMlCoachName(mem, ownerPtr, blkPtr);
				snap.Details = null;
				snap.State = mlMenu;
				snap.Details = mlMenu;
				if (!string.IsNullOrWhiteSpace(coach))
				{
					string coachLabel = "Manager";
					snap.State = $"{coachLabel}: {coach}";
				}
				else
				{
					snap.State = null;
				}
				return true;
			}

			if (gameMode == 20)
			{
				string blMenu = "In Become a Legend";
				string balPlayer = ReadBalPlayerName(mem, blkPtr);
				snap.Details = null;
				snap.State = blMenu;
				snap.Details = blMenu;
				if (!string.IsNullOrWhiteSpace(balPlayer))
				{
					string playerLabel = "Player";
					snap.State = $"{playerLabel}: {balPlayer}";
				}
				else
				{
					snap.State = null;
				}
				return true;
			}

			// Fallback menu status
			snap.Details = null;
			snap.State = "In Main Menu";
			snap.Details = snap.State;
			snap.State = null;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string ReadMlCoachName(IMemoryAccessor mem, ulong ctxPtr, ulong blkPtr)
	{
		if (ctxPtr == 0 || blkPtr == 0)
		{
			return null;
		}

		int careerType = mem.Read<int>(blkPtr + 0x183789C, true);
		if (careerType != 2)
		{
			return null;
		}

		ushort mgrIdx = mem.Read<ushort>(blkPtr + 0x182D388, true);
		ulong mgrAddr = mgrIdx > 0x514 ? blkPtr + 0xD0BA90 : blkPtr + 0xC4CA0C + (ulong)mgrIdx * 0x258;
		uint flags = mem.Read<uint>(mgrAddr + 0x38, true);

		if ((flags & 0x7F) == 0x63)
		{
			ulong cap = mem.Read<ulong>(ctxPtr + 0xB8 + 0x18, true);
			ulong strAddr = cap >= 16 ? mem.Read<ulong>(ctxPtr + 0xB8, true) : ctxPtr + 0xB8;
			if (strAddr >= 0x10000)
			{
				string customName = mem.ReadString(strAddr, 64, true);
				if (!string.IsNullOrWhiteSpace(customName))
				{
					return customName.Trim();
				}
			}
		}
		else
		{
			string presetName = mem.ReadString(mgrAddr + 8, 64, true);
			if (!string.IsNullOrWhiteSpace(presetName))
			{
				return presetName.Trim();
			}
		}

		return null;
	}

	private static string ReadBalPlayerName(IMemoryAccessor mem, ulong blkPtr)
	{
		if (blkPtr == 0)
		{
			return null;
		}

		const int recSize = 0x17C;
		for (int i = 0; i < 25000; i++)
		{
			ulong rec = blkPtr + (ulong)(i * recSize);
			uint pid = mem.Read<uint>(rec + 0x30, true);
			if (pid == 0 && i > 0)
			{
				break;
			}
			if (pid >= 0x70000000 && pid != 0xFFFFFFFF)
			{
				string name = mem.ReadString(rec + 0x38, 61, true);
				if (!string.IsNullOrWhiteSpace(name))
				{
					return name.Trim();
				}
			}
		}

		return null;
	}
}
