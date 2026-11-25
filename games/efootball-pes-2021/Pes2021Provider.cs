using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using MultiPresenceProfiles.Common;

namespace MultiPresenceProfiles.Games.Pes2021;

public sealed class PesMatchData
{
	public bool in_match { get; set; }
	public string match_state { get; set; }
	public string home_name { get; set; }
	public string away_name { get; set; }
	public int home_score { get; set; }
	public int away_score { get; set; }
	public int pk_home_score { get; set; }
	public int pk_away_score { get; set; }
	public string user_side { get; set; }
	public int home_pos { get; set; }
	public int away_pos { get; set; }
	public int user_pos { get; set; }
	public int round_num { get; set; }
	public int period { get; set; }
	public int clock_minutes { get; set; }
	public int clock_seconds { get; set; }
	public int added_minutes { get; set; }
	public string comp_key { get; set; }
	public string tournament { get; set; }
	public string stage_key { get; set; }
	public string stage { get; set; }
	public string mode_key { get; set; }
	public string mode { get; set; }
	public string season { get; set; }
	public string stadium { get; set; }
	public string player { get; set; }
	public int player_pos_id { get; set; } = -1;
	public string coach { get; set; }
	public string team_abbr { get; set; }
	public string team_name { get; set; }
	public long timestamp { get; set; }
}

public static class Pes2021Provider
{
	public const string CanonicalGameName = "eFootball PES 2021";

	private static readonly string[] PosAbbrEn = { "GK", "CB", "LB", "RB", "DMF", "CMF", "LMF", "RMF", "AMF", "LWF", "RWF", "SS", "CF" };
	private static readonly string[] PosAbbrPt = { "GOL", "ZAG", "LE", "LD", "VOL", "MLG", "MLE", "MLD", "MAT", "PTE", "PTD", "SA", "CA" };
	private static readonly string[] PosAbbrEs = { "PT", "DFC", "LI", "LD", "MCD", "MC", "MI", "MD", "MO", "EXI", "EXD", "SD", "CD" };

	public static bool Read(IMemoryAccessor mem, PresenceSnapshot snap)
	{
		try
		{
			

			if (TryReadJsonBridge(mem, snap))
			{
				return true;
			}

			if (TryReadProcessMemory(mem, snap))
			{
				return true;
			}

			snap.Details = null;
			snap.LargeImageText = null;
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

	private static List<string> BuildCandidateJsonPaths(IMemoryAccessor mem)
	{
		var paths = new List<string>(5);
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void AddPath(string p)
		{
			if (!string.IsNullOrWhiteSpace(p) && seen.Add(p))
			{
				paths.Add(p);
			}
		}

		try
		{
			AddPath(Path.Combine(Path.GetTempPath(), "pes_rpc.json"));
		}
		catch { }

		AddPath(@"C:\Windows\Temp\pes_rpc.json");

		try
		{
			Process proc = mem.AttachedProcess;
			if (proc != null && !proc.HasExited)
			{
				string exePath = proc.MainModule?.FileName;
				if (!string.IsNullOrWhiteSpace(exePath))
				{
					string dir = Path.GetDirectoryName(exePath);
					if (!string.IsNullOrWhiteSpace(dir))
					{
						AddPath(Path.Combine(dir, "pes_rpc.json"));
						AddPath(Path.Combine(dir, "SiderAddons", "pes_rpc.json"));
					}
				}
			}
		}
		catch { }

		return paths;
	}

	private static bool TryReadJsonBridge(IMemoryAccessor mem, PresenceSnapshot snap)
	{
		string bestPath = null;
		DateTime newestWriteUtc = DateTime.MinValue;

		foreach (var path in BuildCandidateJsonPaths(mem))
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

	private static bool IsPortuguese(string lang)
	{
		return !string.IsNullOrWhiteSpace(lang) && lang.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsSpanish(string lang)
	{
		return !string.IsNullOrWhiteSpace(lang) && lang.StartsWith("es", StringComparison.OrdinalIgnoreCase);
	}

	private static string FormatOrdinalPos(int pos, string lang)
	{
		if (pos <= 0) return "";
		if (IsPortuguese(lang) || IsSpanish(lang))
		{
			return $"{pos}\u00BA";
		}
		return $"#{pos}";
	}

	private static string GetPositionAbbr(int posId, string lang)
	{
		if (posId < 0 || posId >= PosAbbrEn.Length) return "";
		if (IsPortuguese(lang)) return PosAbbrPt[posId];
		if (IsSpanish(lang)) return PosAbbrEs[posId];
		return PosAbbrEn[posId];
	}

	private static string LocalizeCompetition(string compKey, string rawTournament, string lang)
	{
		string ck = (compKey ?? "").Trim().ToLowerInvariant();
		if (ck == "wc")
		{
			if (IsPortuguese(lang)) return "Copa do Mundo FIFA";
			if (IsSpanish(lang)) return "Copa Mundial FIFA";
			return "FIFA World Cup";
		}
		if (ck == "cwc")
		{
			if (IsPortuguese(lang) || IsSpanish(lang)) return "Mundial de Clubes FIFA";
			return "FIFA Club World Cup";
		}
		if (ck == "suda")
		{
			if (IsPortuguese(lang)) return "CONMEBOL Sul-Americana";
			return "CONMEBOL Sudamericana";
		}
		if (ck == "usc")
		{
			if (IsPortuguese(lang)) return "Supercopa da UEFA";
			if (IsSpanish(lang)) return "Supercopa de la UEFA";
			return "UEFA Super Cup";
		}
		if (ck == "friendly")
		{
			return "Friendly";
		}
		if (ck == "cup")
		{
			return "Cup Mode";
		}
		if (ck == "league")
		{
			return "League Mode";
		}
		if (ck == "comp")
		{
			return "Competition";
		}

		string t = !string.IsNullOrWhiteSpace(rawTournament) ? rawTournament.Trim() : "";
		if (string.Equals(t, "Modo Copa", StringComparison.OrdinalIgnoreCase))
		{
			return "Cup Mode";
		}
		if (string.Equals(t, "Modo Liga", StringComparison.OrdinalIgnoreCase))
		{
			return "League Mode";
		}
		if (string.Equals(t, "Amistoso", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(t, "Friendly", StringComparison.OrdinalIgnoreCase))
		{
			return "Friendly";
		}
		if (string.Equals(t, "Campeonato", StringComparison.OrdinalIgnoreCase))
		{
			return "Competition";
		}

		return !string.IsNullOrWhiteSpace(t)
			? t
			: "Friendly";
	}

	private static string LocalizeStagePrefix(string stageKey, string rawStage)
	{
		string sk = (stageKey ?? "").Trim().ToLowerInvariant();
		if (sk == "final" || (!string.IsNullOrWhiteSpace(rawStage) && rawStage.IndexOf("Final", StringComparison.OrdinalIgnoreCase) >= 0 && rawStage.IndexOf("Semi", StringComparison.OrdinalIgnoreCase) < 0))
		{
			return "\uD83C\uDFC6 " + "Final" + ": ";
		}
		if (sk == "semi" || (!string.IsNullOrWhiteSpace(rawStage) && rawStage.IndexOf("Semi", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			return "Semi-Final" + ": ";
		}
		if (sk == "quarter" || (!string.IsNullOrWhiteSpace(rawStage) && rawStage.IndexOf("Quarta", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			return "Quarter-Finals" + ": ";
		}
		if (sk == "r16" || (!string.IsNullOrWhiteSpace(rawStage) && rawStage.IndexOf("Oitava", StringComparison.OrdinalIgnoreCase) >= 0))
		{
			return "Round of 16" + ": ";
		}
		return !string.IsNullOrWhiteSpace(rawStage) ? rawStage.Trim() + " " : "";
	}

	private static string ResolveModeKey(PesMatchData data)
	{
		if (!string.IsNullOrWhiteSpace(data.mode_key))
		{
			return data.mode_key.Trim().ToLowerInvariant();
		}
		string m = (data.mode ?? "").Trim();
		if (m.IndexOf("Master", StringComparison.OrdinalIgnoreCase) >= 0) return "ml";
		if (m.IndexOf("Estrelato", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("Legend", StringComparison.OrdinalIgnoreCase) >= 0) return "bal";
		if (m.IndexOf("Treino", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("Train", StringComparison.OrdinalIgnoreCase) >= 0) return "training";
		if (m.IndexOf("Edit", StringComparison.OrdinalIgnoreCase) >= 0) return "edit";
		if (m.IndexOf("Copa", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("Cup", StringComparison.OrdinalIgnoreCase) >= 0) return "cup";
		if (m.IndexOf("Liga", StringComparison.OrdinalIgnoreCase) >= 0 || m.IndexOf("League", StringComparison.OrdinalIgnoreCase) >= 0) return "league";
		return "menu";
	}

	private static void ApplyDataToSnapshot(PresenceSnapshot snap, PesMatchData data)
	{
		string modeKey = ResolveModeKey(data);
		string season = !string.IsNullOrWhiteSpace(data.season) ? data.season.Trim() : "";
		string teamAbbr = !string.IsNullOrWhiteSpace(data.team_abbr) ? data.team_abbr.Trim() : "";
		string teamName = !string.IsNullOrWhiteSpace(data.team_name) ? data.team_name.Trim() : "";
		string userPosStr = FormatOrdinalPos(data.user_pos, "en");
		string posAbbr = GetPositionAbbr(data.player_pos_id, "en");

		if (!data.in_match)
		{
			snap.Details = null;
			snap.LargeImageText = null;
			string seasonSuffix = !string.IsNullOrEmpty(season) ? $" \u2022 {season}" : "";
			string clubDisplay = !string.IsNullOrEmpty(teamName) ? teamName : teamAbbr;
			string clubWithPos = !string.IsNullOrEmpty(clubDisplay)
				? (!string.IsNullOrEmpty(userPosStr) ? $"{clubDisplay} ({userPosStr})" : clubDisplay)
				: "";

			if (modeKey == "ml")
			{
				string mlMenu = "In Master League";
				snap.State = mlMenu;
				snap.Details = $"{mlMenu}{seasonSuffix}";
				if (!string.IsNullOrWhiteSpace(data.coach))
				{
					string coachLabel = "Manager";
					snap.State = !string.IsNullOrEmpty(clubWithPos)
						? $"{coachLabel}: {data.coach.Trim()} | {clubWithPos}"
						: $"{coachLabel}: {data.coach.Trim()}";
				}
				else
				{
					snap.State = !string.IsNullOrEmpty(clubWithPos) ? clubWithPos : null;
				}
			}
			else if (modeKey == "bal")
			{
				string blMenu = "In Become a Legend";
				snap.State = blMenu;
				snap.Details = $"{blMenu}{seasonSuffix}";
				if (!string.IsNullOrWhiteSpace(data.player))
				{
					string playerLabel = "Player";
					string pDisplay = !string.IsNullOrEmpty(posAbbr)
						? $"{data.player.Trim()} ({posAbbr})"
						: data.player.Trim();
					snap.State = !string.IsNullOrEmpty(clubWithPos)
						? $"{playerLabel}: {pDisplay} | {clubWithPos}"
						: $"{playerLabel}: {pDisplay}";
				}
				else
				{
					snap.State = !string.IsNullOrEmpty(clubWithPos) ? clubWithPos : null;
				}
			}
			else if (modeKey == "edit")
			{
				snap.State = "In Edit Mode";
				snap.Details = snap.State;
				snap.State = null;
			}
			else if (modeKey == "training")
			{
				snap.State = "In Training Mode";
				snap.Details = snap.State;
				snap.State = null;
			}
			else
			{
				snap.State = "In Main Menu";
				snap.Details = snap.State;
				snap.State = null;
			}

			snap.Set("in_match", false);
			snap.Set("mode", modeKey);
			snap.Set("season", season);
			return;
		}

		// In Match
		string homeDefault = "Home";
		string awayDefault = "Away";
		string home = !string.IsNullOrWhiteSpace(data.home_name) ? data.home_name.Trim() : homeDefault;
		string away = !string.IsNullOrWhiteSpace(data.away_name) ? data.away_name.Trim() : awayDefault;
		string scoreSep = "-";
		string score = $"{data.home_score} {scoreSep} {data.away_score}";

		snap.Details = $"{home} {score} {away}";

		string mstate = (data.match_state ?? "live").Trim().ToLowerInvariant();
		string timeStr;
		if (mstate == "halftime")
		{
			timeStr = "Halftime";
		}
		else if (mstate == "fulltime")
		{
			timeStr = "Full Time";
		}
		else if (mstate == "penalties" || data.period == 5)
		{
			string penLbl = "Penalties";
			timeStr = (data.pk_home_score > 0 || data.pk_away_score > 0)
				? $"{penLbl} ({data.pk_home_score} {scoreSep} {data.pk_away_score})"
				: penLbl;
		}
		else
		{
			bool usePtEs = false;
			string periodTag = usePtEs ? "1T" : "1H";
			if (data.period == 2) periodTag = usePtEs ? "2T" : "2H";
			else if (data.period == 3) periodTag = usePtEs ? "1P" : "ET1";
			else if (data.period == 4) periodTag = usePtEs ? "2P" : "ET2";

			timeStr = data.added_minutes > 0
				? $"{data.clock_minutes}+{data.added_minutes}' {periodTag}"
				: $"{data.clock_minutes}' {periodTag}";

			if (mstate == "paused")
			{
				string pausedLabel = "Paused";
				timeStr = $"{timeStr} ({pausedLabel})";
			}
		}

		string competition = LocalizeCompetition(data.comp_key, data.tournament, "en");
		string stagePrefix = LocalizeStagePrefix(data.stage_key, data.stage);
		string compDisplay = $"{stagePrefix}{competition}";

		bool isRout = Math.Abs(data.home_score - data.away_score) >= 3 && (data.home_score + data.away_score) >= 3;
		string routSuffix = isRout
			? " \u2022 " + "Rout"
			: "";

		snap.Details = $"{compDisplay} \u2022 {timeStr}{routSuffix}";

		string player = !string.IsNullOrWhiteSpace(data.player) ? data.player.Trim() : "";
		string coach = !string.IsNullOrWhiteSpace(data.coach) ? data.coach.Trim() : "";
		string stadium = !string.IsNullOrWhiteSpace(data.stadium) ? data.stadium.Trim() : "";

		snap.LargeImageText = !string.IsNullOrEmpty(stadium)
			? $"\uD83C\uDFDF\uFE0F {stadium} \u2014 {CanonicalGameName}"
			: null;

		string teamBadge = "";
		if (!string.IsNullOrEmpty(teamAbbr) && !string.IsNullOrEmpty(userPosStr))
		{
			teamBadge = $"{teamAbbr} ({userPosStr})";
		}
		else if (!string.IsNullOrEmpty(teamAbbr))
		{
			teamBadge = teamAbbr;
		}
		else if (!string.IsNullOrEmpty(userPosStr))
		{
			teamBadge = userPosStr;
		}

		if (modeKey == "bal")
		{
			string modeLabel = "Become a Legend";
			string playerLabel = "Player";
			string balClub = !string.IsNullOrEmpty(teamAbbr) && !string.IsNullOrEmpty(userPosStr)
				? $"{teamAbbr} {userPosStr}"
				: (!string.IsNullOrEmpty(teamAbbr) ? teamAbbr : userPosStr);
			string tagParts = !string.IsNullOrEmpty(posAbbr) && !string.IsNullOrEmpty(balClub)
				? $" ({posAbbr} \u2022 {balClub})"
				: (!string.IsNullOrEmpty(posAbbr) ? $" ({posAbbr})" : (!string.IsNullOrEmpty(balClub) ? $" ({balClub})" : ""));
			snap.State = !string.IsNullOrWhiteSpace(player)
				? $"{modeLabel} | {playerLabel}: {player}{tagParts}"
				: (!string.IsNullOrEmpty(teamBadge) ? $"{modeLabel} \u2022 {teamBadge}" : modeLabel);
		}
		else if (modeKey == "ml")
		{
			string modeLabel = "Master League";
			string coachLabel = "Manager";
			string badgeSuffix = !string.IsNullOrEmpty(teamBadge) ? $" \u2022 {teamBadge}" : "";
			snap.State = !string.IsNullOrWhiteSpace(coach)
				? $"{modeLabel} | {coachLabel}: {coach}{badgeSuffix}"
				: $"{modeLabel}{badgeSuffix}";
		}
		else if (!string.IsNullOrEmpty(stadium))
		{
			snap.State = $"\uD83C\uDFDF\uFE0F {stadium}";
		}
		else
		{
			snap.State = null;
		}

		snap.Set("team_home", home);
		snap.Set("team_away", away);
		snap.Set("score_home", data.home_score);
		snap.Set("score_away", data.away_score);
		snap.Set("score", score);
		snap.Set("competition", competition);
		snap.Set("stage", stagePrefix.Trim());
		snap.Set("mode", modeKey);
		snap.Set("match_time", timeStr);
		snap.Set("stadium", stadium);
		snap.Set("season", season);
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
			ulong statusRva = 0x3704E38;

			// CPY owner pointer
			ownerPtr = mem.Read<ulong>(baseAddr + 0x3705e10, true);
			if (ownerPtr >= 0x10000)
			{
				blkPtr = mem.Read<ulong>(ownerPtr + 0x48, true);
			}

			// Steam / Football Life owner pointer fallback
			if (blkPtr < 0x10000)
			{
				ownerPtr = mem.Read<ulong>(baseAddr + 0x3714240, true);
				if (ownerPtr >= 0x10000)
				{
					blkPtr = mem.Read<ulong>(ownerPtr + 0x48, true);
					statusRva = 0x3713268;
				}
			}

			// Detect normal vs cap layout
			uint p1 = mem.Read<uint>(baseAddr + 0xAEDE84, true);
			uint p2 = mem.Read<uint>(baseAddr + 0xAED7B4, true);
			bool isCap = (p1 == 0x19115E4u) || (p2 == 0x19115E4u);
			ulong dateOff = isCap ? 0x19115E4ul : 0x1642A1Cul;

			int gameMode = -1;
			ulong statusInst = mem.Read<ulong>(baseAddr + statusRva, true);
			if (statusInst >= 0x10000)
			{
				gameMode = mem.Read<int>(statusInst + 0xF0, true);
			}

			string season = ReadCareerSeason(mem, blkPtr, dateOff);
			string seasonSuffix = !string.IsNullOrEmpty(season) ? $" \u2022 {season}" : "";

			if (gameMode == 19)
			{
				string mlMenu = "In Master League";
				string coach = ReadMlCoachName(mem, ownerPtr, blkPtr);
				snap.Details = null;
				snap.LargeImageText = null;
				snap.State = mlMenu;
				snap.Details = $"{mlMenu}{seasonSuffix}";
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
				string balPlayer = ReadBalPlayerName(mem, blkPtr, out int posId);
				string posAbbr = GetPositionAbbr(posId, "en");
				snap.Details = null;
				snap.LargeImageText = null;
				snap.State = blMenu;
				snap.Details = $"{blMenu}{seasonSuffix}";
				if (!string.IsNullOrWhiteSpace(balPlayer))
				{
					string playerLabel = "Player";
					string pDisplay = !string.IsNullOrEmpty(posAbbr) ? $"{balPlayer} ({posAbbr})" : balPlayer;
					snap.State = $"{playerLabel}: {pDisplay}";
				}
				else
				{
					snap.State = null;
				}
				return true;
			}

			if (gameMode == 13 || gameMode == 14)
			{
				snap.Details = null;
				snap.LargeImageText = null;
				snap.State = "In Edit Mode";
				snap.Details = snap.State;
				snap.State = null;
				return true;
			}

			if (gameMode == 51 || gameMode == 52)
			{
				snap.Details = null;
				snap.LargeImageText = null;
				snap.State = "In Training Mode";
				snap.Details = snap.State;
				snap.State = null;
				return true;
			}

			snap.Details = null;
			snap.LargeImageText = null;
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

	private static string ReadCareerSeason(IMemoryAccessor mem, ulong blkPtr, ulong dateOff)
	{
		if (blkPtr < 0x10000) return "";
		ushort year = mem.Read<ushort>(blkPtr + dateOff + 8, true);
		byte month = mem.Read<byte>(blkPtr + dateOff + 10, true);
		if (year < 1990 || year > 2150 || month < 1 || month > 12) return "";
		return month >= 7
			? $"{year}/{(year + 1) % 100:00}"
			: $"{year - 1}/{year % 100:00}";
	}

	private static string ReadMlCoachName(IMemoryAccessor mem, ulong ctxPtr, ulong blkPtr)
	{
		if (ctxPtr < 0x10000 || blkPtr < 0x10000)
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

	private static string ReadBalPlayerName(IMemoryAccessor mem, ulong blkPtr, out int posId)
	{
		posId = -1;
		if (blkPtr < 0x10000)
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
				byte posByte = mem.Read<byte>(rec + 0x07, true);
				int p = posByte >> 4;
				if (p >= 0 && p <= 12) posId = p;
				if (!string.IsNullOrWhiteSpace(name))
				{
					return name.Trim();
				}
			}
		}

		return null;
	}
}
