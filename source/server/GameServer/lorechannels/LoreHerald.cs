#nullable disable
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DOL.GS.LoreChannels;

// Living Server 1.2: the Herald's lore. Shared with the launcher (linked): the chronicle sentence of a raid record, the
// bestiary's unlock rules, and the realms' first kills, kept in the realm event ledger beside the raid records.

/// <summary>The chronicle sentence of a raid record, from its final boss's lore (chronicle.win / chronicle.loss).</summary>
public static class LoreChronicle
{
    /// <summary>
    /// Each raid's final encounters by script class, in the order the raid fights them: the same as
    /// AutonomousRealmRaid.Definitions' FinalTypes (UT_LoreChannelHerald checks it). Here because the launcher links
    /// this file and not the raid code.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RaidFinals = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["dragon-albion"] = ["AlbGolestandt"],
        ["dragon-midgard"] = ["MidGjalpinulva"],
        ["dragon-hibernia"] = ["HibCuuldurach"],
        ["epic-albion"] = ["Apocalypse"],
        ["epic-midgard"] = ["KingTuscar", "QueenKula"],
        ["epic-hibernia"] = ["Olcasgean"],
        ["summoners-albion"] = ["GrandSummonerGovannon"],
        ["summoners-midgard"] = ["GrandSummonerGovannon"],
        ["summoners-hibernia"] = ["GrandSummonerGovannon"],
        ["darkness-albion"] = ["Legion"],
        ["darkness-midgard"] = ["Legion"],
        ["darkness-hibernia"] = ["Legion"],
        ["dungeon-albion-krondon"] = ["Orylle"],
        ["dungeon-albion-avalon"] = ["DuraekEmpowered"],
        ["dungeon-albion-crystal"] = ["Xanxicar"],
        ["dungeon-albion-corrupt"] = ["AidonTheArchwizard", "LordGildas"],
        ["dungeon-midgard-trollheim"] = ["Nosdoden"],
        ["dungeon-midgard-iarnvidiur"] = ["Iarnvidiur"],
        ["dungeon-midgard-dodens"] = ["SpiritmasterAros"],
        ["dungeon-hibernia-tursuil"] = ["Balor"],
        ["dungeon-hibernia-marfach"] = ["BlackLady", "ChieftainCaimheul", "Avartack"],
    };

    /// <summary>The end of a raid's details: the raid leader's name (Living Server 1.2). Rows without it name none.</summary>
    public const string RaidLeaderMarker = " Raid leader: ";

    public static string WithRaidLeader(string details, string leader) =>
        string.IsNullOrWhiteSpace(leader) ? details : $"{details}{RaidLeaderMarker}{leader.Replace(".", "").Trim()}.";

    public static string RaidLeaderIn(string details)
    {
        int at = details?.LastIndexOf(RaidLeaderMarker, StringComparison.Ordinal) ?? -1;
        if (at < 0) return null;
        int start = at + RaidLeaderMarker.Length, end = details.IndexOf('.', start);
        string name = (end < 0 ? details[start..] : details[start..end]).Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>"win", "loss" or null: a win is "Boss defeated"; a loss is a defeat, or a time-out after a wipe.</summary>
    public static string Kind(string outcome, string details)
    {
        string record = RealmEventRecordStore.RecordOutcome(outcome, details);
        return record == "Boss defeated" ? "win" : record is "Defeated" or "Timed out" ? "loss" : null;
    }

    /// <summary>
    /// Fills a template: {realm} (Albion, Midgard, Hibernia), {raidLeader} (the leader's name, or "its raid leader"
    /// when the record names none) and {minutes} (the raid's length, at least 1).
    /// </summary>
    public static string Render(string template, string realm, string raidLeader, int minutes)
    {
        if (string.IsNullOrWhiteSpace(template)) return null;
        string text = template.Replace("{realm}", string.IsNullOrWhiteSpace(realm) ? "the realm" : realm)
            .Replace("{raidLeader}", string.IsNullOrWhiteSpace(raidLeader) ? "its raid leader" : raidLeader)
            .Replace("{minutes}", Math.Max(1, minutes).ToString(CultureInfo.InvariantCulture));
        return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text[1..] : text;
    }

    /// <summary>The boss whose chronicle a raid's record tells: its first final with lore.</summary>
    public static LoreBoss FinalBoss(ILoreSource source, string eventId) =>
        RaidFinals.TryGetValue(eventId ?? "", out string[] finals)
            ? finals.Select(source.FindBossByClass).FirstOrDefault(b => b?.Chronicle != null)
            : null;

    public static int Minutes(string startedUtc, string endedUtc) =>
        DateTimeOffset.TryParse(startedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var start) &&
        DateTimeOffset.TryParse(endedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end) && end >= start
            ? (int)Math.Round((end - start).TotalMinutes) : 0;

    /// <summary>The Herald's sentence for a finished raid record, or null (not a raid, no result, no lore yet).</summary>
    public static string For(ILoreSource source, RealmEventRecord record)
    {
        if (source == null || record == null || string.IsNullOrEmpty(record.EndedUtc)) return null;
        string kind = Kind(record.Outcome, record.Details);
        LoreBoss boss = kind == null ? null : FinalBoss(source, record.EventId);
        if (boss == null) return null;
        return Render(kind == "win" ? boss.Chronicle.Win : boss.Chronicle.Loss, record.Realm, RaidLeaderIn(record.Details),
            Minutes(record.StartedUtc, record.EndedUtc));
    }
}

/// <summary>A realm's first kill of a boss.</summary>
public sealed record LoreFirstKill(int Realm, string BossId, string KilledUtc, string Killer, string EventId);

/// <summary>
/// The realms' first kills, in the realm event ledger (realm-event-records.sqlite3, never the game database), table
/// LoreFirstKills: one row per realm and boss, written once. The launcher reads it for the bestiary; its DELETE ALL
/// RECORDS leaves it alone.
/// </summary>
public static class LoreFirstKillStore
{
    private const string Schema = "CREATE TABLE IF NOT EXISTS LoreFirstKills(Realm INTEGER NOT NULL, BossId TEXT NOT NULL, KilledUtc TEXT NOT NULL, Killer TEXT NOT NULL, EventId TEXT NOT NULL, PRIMARY KEY(Realm, BossId))";

    private static SQLiteConnection Open(string path, bool readOnly)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionStringBuilder { DataSource = path, ReadOnly = readOnly,
            FailIfMissing = readOnly, Pooling = false, DefaultTimeout = 5 }.ConnectionString);
        connection.Open();
        return connection;
    }

    /// <summary>Adds first kills; one already there for that realm and boss stays as it is. Returns how many were added.</summary>
    public static int Add(string path, IEnumerable<LoreFirstKill> kills)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        using var connection = Open(path, false);
        using (var schema = connection.CreateCommand()) { schema.CommandText = Schema; schema.ExecuteNonQuery(); }
        using var tx = connection.BeginTransaction();
        int added = 0;
        foreach (LoreFirstKill kill in kills ?? [])
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = "INSERT OR IGNORE INTO LoreFirstKills VALUES(@realm,@boss,@utc,@killer,@event)";
            command.Parameters.AddWithValue("@realm", kill.Realm);
            command.Parameters.AddWithValue("@boss", kill.BossId ?? "");
            command.Parameters.AddWithValue("@utc", kill.KilledUtc ?? "");
            command.Parameters.AddWithValue("@killer", kill.Killer ?? "");
            command.Parameters.AddWithValue("@event", kill.EventId ?? "");
            added += command.ExecuteNonQuery();
        }
        tx.Commit();
        return added;
    }

    /// <summary>The ledger's raid records that ended "Boss defeated" (the first-kill seed for raid finals).</summary>
    public static List<RealmEventRecord> RaidWins(string path) => RaidRecords(path, true);

    /// <summary>The ledger's finished raid records (raid ids only; <paramref name="winsOnly"/>: "Boss defeated" only).</summary>
    public static List<RealmEventRecord> RaidRecords(string path, bool winsOnly = false)
    {
        var rows = new List<RealmEventRecord>();
        if (!File.Exists(path)) return rows;
        using var connection = Open(path, true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='Events'";
        if ((long)command.ExecuteScalar() == 0) return rows;
        command.CommandText = "SELECT * FROM Events WHERE EndedUtc<>''" + (winsOnly ? " AND Outcome='Boss defeated'" : "") + " ORDER BY EndedUtc";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (LoreChronicle.RaidFinals.ContainsKey(reader.GetString(1)))
                rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.GetString(7), reader.GetString(8), reader.GetString(9), reader.GetInt32(10), reader.GetInt32(11)));
        return rows;
    }

    public static List<LoreFirstKill> Read(string path)
    {
        var kills = new List<LoreFirstKill>();
        if (!File.Exists(path)) return kills;
        using var connection = Open(path, true);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='LoreFirstKills'";
        if ((long)command.ExecuteScalar() == 0) return kills;
        command.CommandText = "SELECT Realm, BossId, KilledUtc, Killer, EventId FROM LoreFirstKills ORDER BY KilledUtc";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            kills.Add(new LoreFirstKill(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return kills;
    }
}

/// <summary>One boss in a realm's bestiary.</summary>
public sealed record LoreBestiaryEntry(LoreBoss Boss, LoreLocation Location, bool Unlocked, string FirstKillUtc, string FirstKiller,
    int Wins, int Losses)
{
    /// <summary>What the story says until the realm has brought the boss down.</summary>
    public const string LockedStory = "Unknown. No raid of this realm has brought it down yet; until one does, only the songs below are told.";
    public string StoryShown => Unlocked ? Boss.Story : LockedStory;
}

/// <summary>
/// The bestiary's rules (owner to confirm): every boss of the realm's own and the neutral places is listed with its name,
/// place and legend (the raid-leader advice everyone already repeats, so it never hides how to fight). Its story, first
/// kill and raid record open when the realm first kills it: a first kill in the ledger, or, for raid finals, the
/// realm's earliest "Boss defeated" raid record (so wins from before 1.2 count).
/// </summary>
public static class LoreBestiary
{
    public static List<LoreBestiaryEntry> For(ILoreSource source, int realm, IEnumerable<LoreFirstKill> firstKills,
        IEnumerable<RealmEventRecord> raidRecords)
    {
        var entries = new List<LoreBestiaryEntry>();
        if (source == null) return entries;
        source = LoreRealmView.For(source, realm);   // the reader's realm's take on neutral bosses (realmTakes)
        string realmName = LoreRealm.Name(realm);
        var kills = (firstKills ?? []).Where(k => k.Realm == realm)
            .GroupBy(k => k.BossId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.OrderBy(k => k.KilledUtc, StringComparer.Ordinal).First(), StringComparer.OrdinalIgnoreCase);
        var records = (raidRecords ?? []).Where(r => string.Equals(r.Realm, realmName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(r.EndedUtc)).ToList();
        foreach (LoreLocation location in source.AllLocations.Where(l => LoreRealm.Sees(realm, l.RealmId)))
        foreach (LoreBoss boss in source.BossesOf(location))
        {
            var mine = records.Where(r => LoreChronicle.RaidFinals.TryGetValue(r.EventId, out string[] finals) && finals.Any(boss.IsClass)).ToList();
            int wins = mine.Count(r => LoreChronicle.Kind(r.Outcome, r.Details) == "win");
            int losses = mine.Count(r => LoreChronicle.Kind(r.Outcome, r.Details) == "loss");
            string first = kills.TryGetValue(boss.Id, out LoreFirstKill kill) ? kill.KilledUtc : null;
            string killer = kill?.Killer;
            RealmEventRecord firstWin = mine.Where(r => LoreChronicle.Kind(r.Outcome, r.Details) == "win")
                .OrderBy(r => r.EndedUtc, StringComparer.Ordinal).FirstOrDefault();
            if (firstWin != null && (first == null || string.CompareOrdinal(firstWin.EndedUtc, first) < 0))
            {
                first = firstWin.EndedUtc;
                killer = LoreChronicle.RaidLeaderIn(firstWin.Details) is { } leader ? $"{realmName} raid led by {leader}" : $"{realmName} raid";
            }
            entries.Add(new LoreBestiaryEntry(boss, location, first != null, first, killer, wins, losses));
        }
        return entries;
    }

    /// <summary>The bestiary page of one entry, as plain lines (the launcher's page and the Herald).</summary>
    public static List<string> Page(LoreBestiaryEntry entry, int realm)
    {
        var lines = new List<string> { entry.Boss.Name, $"{entry.Location.Name} ({LoreRealm.Name(entry.Location.RealmId)})", "" };
        if (entry.Unlocked)
        {
            lines.Add($"First slain by {LoreRealm.Name(realm)}: {When(entry.FirstKillUtc)}" + (string.IsNullOrEmpty(entry.FirstKiller) ? "" : $" ({entry.FirstKiller})"));
            if (entry.Wins + entry.Losses > 0) lines.Add($"Raid record: {entry.Wins} won, {entry.Losses} lost");
            lines.Add("");
        }
        else
        {
            lines.Add($"Not yet slain by {LoreRealm.Name(realm)}.");
            lines.Add("");
        }
        lines.Add("Story");
        lines.Add(entry.StoryShown ?? "");
        lines.Add("");
        lines.Add("What the raid leaders say");
        lines.Add(string.IsNullOrWhiteSpace(entry.Boss.Legend) ? "Nothing yet." : entry.Boss.Legend);
        return lines;
    }

    public static string When(string utc) =>
        DateTimeOffset.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)
            ? t.UtcDateTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : "unknown";
}
