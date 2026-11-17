using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The rows of the "Bots as people" tables (<see cref="BotPeopleTables"/>), kept in memory and saved through the Living
/// Server pattern (<see cref="LivingServerTable{TKey,TRow}"/>: copies written in batches off the game loop under the
/// shared database write lock). Loaded at server start; saved every 5 minutes, soon after a bot logs out, and at server
/// stop. The packages change rows only through the tables here (Change, TryGet, Remove), never by writing the database.
/// <see cref="Current"/> is the server's store; tests make their own with their own database.
/// </summary>
public sealed class BotPeopleStore
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>How often the store is saved and BOT_PEOPLE_SUMMARY is logged (design: "every 5 minutes").</summary>
    public const int SaveIntervalMilliseconds = 5 * 60_000;
    /// <summary>A logout saves this soon after, once for all the bots that left meanwhile.</summary>
    public const int LogoutSaveDelayMilliseconds = 5_000;

    /// <summary>One row per person: item 2's skill (WP1).</summary>
    public LivingServerTable<BotPersonKey, OfflineBotSkillRecord> Skill { get; }
    /// <summary>One row per person and realm: item 4's places (WP3).</summary>
    public LivingServerTable<(BotPersonKey Person, int Realm), OfflineBotPlacesRecord> Places { get; }
    /// <summary>One row per person, character and arc: item 6's milestones (WP6).</summary>
    public LivingServerTable<(BotPersonKey Person, long CharacterId, string Arc), OfflineBotMilestoneRecord> Milestones { get; }
    /// <summary>One row per person and mechanic: WP2R's raid mechanics seen.</summary>
    public LivingServerTable<(BotPersonKey Person, string Mechanic), OfflineBotMechanicSeenRecord> MechanicsSeen { get; }

    /// <summary>Rows that were not loaded because their PersonKey could not be read (logged at load).</summary>
    public int Unreadable { get; private set; }

    /// <param name="database">The database to write to (tests pass their own; the server's by default).</param>
    public BotPeopleStore(Func<IObjectDatabase> database = null)
    {
        Skill = new("bot skill", row => KeyOf(row.PersonKey), null, database);
        Places = new("bot places", row => (KeyOf(row.PersonKey), row.Realm), null, database);
        Milestones = new("bot milestones", row => (KeyOf(row.PersonKey), row.CharacterId, row.Arc ?? string.Empty), null, database);
        MechanicsSeen = new("bot mechanics seen", row => (KeyOf(row.PersonKey), row.Mechanic ?? string.Empty), null, database);
    }

    /// <summary>The server's store (replaced only by tests through <see cref="UseForTests"/>).</summary>
    public static BotPeopleStore Current { get; private set; } = new();

    /// <summary>Swaps the server's store (tests); returns the one it replaced.</summary>
    public static BotPeopleStore UseForTests(BotPeopleStore store)
    {
        BotPeopleStore previous = Current;
        Current = store ?? new BotPeopleStore();
        return previous;
    }

    private static BotPersonKey KeyOf(string text) => BotPersonKey.TryParse(text, out BotPersonKey key) ? key : default;

    /// <summary>Loads the rows (tests call it with their own). A row whose key can't be read is skipped and counted.</summary>
    public void Load(IEnumerable<OfflineBotSkillRecord> skill, IEnumerable<OfflineBotPlacesRecord> places,
        IEnumerable<OfflineBotMilestoneRecord> milestones, IEnumerable<OfflineBotMechanicSeenRecord> mechanicsSeen)
    {
        int unreadable = 0;
        IEnumerable<T> Readable<T>(IEnumerable<T> rows, Func<T, string> key)
        {
            foreach (T row in rows ?? [])
            {
                if (row != null && BotPersonKey.TryParse(key(row), out _))
                    yield return row;
                else
                    unreadable++;
            }
        }
        Skill.Load(Readable(skill, row => row.PersonKey).ToArray());
        Places.Load(Readable(places, row => row.PersonKey).ToArray());
        Milestones.Load(Readable(milestones, row => row.PersonKey).ToArray());
        MechanicsSeen.Load(Readable(mechanicsSeen, row => row.PersonKey).ToArray());
        Unreadable = unreadable;
    }

    /// <summary>Loads every table from the database.</summary>
    public void LoadFrom(IObjectDatabase database) => Load(
        database.SelectAllObjects<OfflineBotSkillRecord>(), database.SelectAllObjects<OfflineBotPlacesRecord>(),
        database.SelectAllObjects<OfflineBotMilestoneRecord>(), database.SelectAllObjects<OfflineBotMechanicSeenRecord>());

    /// <summary>Writes every queued row (never on the game loop). With <paramref name="wait"/> it waits for a running save.</summary>
    public void Flush(bool wait = false)
    {
        Skill.Flush(wait);
        Places.Flush(wait);
        Milestones.Flush(wait);
        MechanicsSeen.Flush(wait);
    }

    /// <summary>Rows per table, in <see cref="BotPeopleTables.All"/>'s order.</summary>
    public int[] Counts() => [Skill.Count, Places.Count, Milestones.Count, MechanicsSeen.Count];

    /// <summary>Every row of one person (the merge and /lswhy read it).</summary>
    public (OfflineBotSkillRecord Skill, OfflineBotPlacesRecord[] Places, OfflineBotMilestoneRecord[] Milestones,
        OfflineBotMechanicSeenRecord[] MechanicsSeen) RowsOf(BotPersonKey person)
    {
        string text = person.ToString();
        return (Skill.TryGet(person, out OfflineBotSkillRecord skill) ? skill : null,
            Places.Where(row => row.PersonKey == text), Milestones.Where(row => row.PersonKey == text),
            MechanicsSeen.Where(row => row.PersonKey == text));
    }

    /// <summary>Removes every row of a person (a deleted bot). Returns how many rows went.</summary>
    public int Forget(BotPersonKey person)
    {
        if (person.IsNone)
            return 0;
        string text = person.ToString();
        return (Skill.Remove(person) ? 1 : 0) + Places.RemoveWhere(row => row.PersonKey == text) +
               Milestones.RemoveWhere(row => row.PersonKey == text) + MechanicsSeen.RemoveWhere(row => row.PersonKey == text);
    }

    /// <summary>
    /// Joins the rows of <paramref name="from"/> (characters, usually) into <paramref name="person"/> by the merge rules
    /// (<see cref="BotPeopleMerge"/>): the person's own rows are part of the merge, the old rows are removed and the
    /// merged ones queued for saving. For the bot-player model of 2.0; nothing calls it in 1.2. Returns the rows written.
    /// </summary>
    public int MergeInto(BotPersonKey person, IEnumerable<BotPersonKey> from)
    {
        if (!person.IsPlayer && !person.IsCharacter)
            throw new ArgumentException("Merge into a person", nameof(person));
        BotPersonKey[] sources = (from ?? []).Where(key => !key.IsNone).Append(person).Distinct().ToArray();
        var rows = sources.Select(RowsOf).ToArray();

        OfflineBotSkillRecord skill = BotPeopleMerge.Skill(person, rows.Select(r => r.Skill));
        OfflineBotPlacesRecord[] places = BotPeopleMerge.Places(person, rows.SelectMany(r => r.Places));
        OfflineBotMilestoneRecord[] milestones = BotPeopleMerge.Milestones(person, rows.SelectMany(r => r.Milestones));
        OfflineBotMechanicSeenRecord[] seen = BotPeopleMerge.MechanicsSeen(person, rows.SelectMany(r => r.MechanicsSeen));

        foreach (BotPersonKey source in sources)
            Forget(source);
        int written = 0;
        if (skill != null)
        {
            Skill.Change(person, () => skill, _ => { });
            written++;
        }
        foreach (OfflineBotPlacesRecord row in places)
            Places.Change((person, row.Realm), () => row, _ => { });
        foreach (OfflineBotMilestoneRecord row in milestones)
            Milestones.Change((person, row.CharacterId, row.Arc ?? string.Empty), () => row, _ => { });
        foreach (OfflineBotMechanicSeenRecord row in seen)
            MechanicsSeen.Change((person, row.Mechanic ?? string.Empty), () => row, _ => { });
        return written + places.Length + milestones.Length + seen.Length;
    }

    // ---- The server's life cycle: load at start, a save and a summary line every 5 minutes, a save at stop. ----

    private static Timer _saveTimer;
    private static int _logoutSaveQueued;
    private static long _lastSummaryTicks;
    private static long _logouts;

    /// <summary>Raised (on the caller's thread) when a gamebot with a person leaves the world, before its rows are saved.
    /// A package that caches per-bot state (a skill profile) drops it here. Keep handlers short; never block.</summary>
    public static event Action<BotPersonKey, GameBot> LoggedOut;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Current.LoadFrom(GameServer.Database);
            if (Current.Unreadable > 0)
                Log.Warn($"Bots as people: {Current.Unreadable} row(s) with an unreadable PersonKey were not loaded");
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: could not read the bot people tables", exception);
        }
        Interlocked.Exchange(ref _lastSummaryTicks, DateTime.UtcNow.Ticks);
        _saveTimer = new Timer(_ => SaveAndSummarize(), null, SaveIntervalMilliseconds, SaveIntervalMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _saveTimer?.Dispose();
        _saveTimer = null;
        Current.Flush(wait: true);
    }

    /// <summary>
    /// A gamebot left the world (AutonomousBotRegistry.Unregister): the packages hear it, and the store is saved a few
    /// seconds later on a pool thread, once for every bot that left meanwhile. Nothing is written when nothing changed.
    /// </summary>
    public static void OnLoggedOut(GameBot bot)
    {
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return;
        Interlocked.Increment(ref _logouts);
        try
        {
            LoggedOut?.Invoke(person, bot);
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: a logout handler failed", exception);
        }
        if (Interlocked.Exchange(ref _logoutSaveQueued, 1) != 0)
            return;
        LogoutTimer.Change(LogoutSaveDelayMilliseconds, Timeout.Infinite);
    }

    private static Timer _logoutTimer;

    // One timer, armed by the first logout and fired once for every bot that left within the delay (a pool thread).
    private static Timer LogoutTimer => LazyInitializer.EnsureInitialized(ref _logoutTimer, () => new Timer(_ =>
    {
        Volatile.Write(ref _logoutSaveQueued, 0);
        try
        {
            Current.Flush();
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: the save after a logout failed", exception);
        }
    }, null, Timeout.Infinite, Timeout.Infinite));

    private static void SaveAndSummarize()
    {
        try
        {
            Current.Flush();
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: the 5-minute save failed", exception);
        }
        try
        {
            long now = DateTime.UtcNow.Ticks;
            long seconds = Math.Max(1, (now - Interlocked.Exchange(ref _lastSummaryTicks, now)) / TimeSpan.TicksPerSecond);
            Log.Info(SummaryLine(seconds, BotPeopleSwitches.Current, Current.Counts(), BotPeopleScope.TakeCounts(),
                Interlocked.Exchange(ref _logouts, 0)));
            foreach (string line in BotPeopleSummary.TakeLines(seconds))
                Log.Info(line);
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: the summary line failed", exception);
        }
    }

    /// <summary>
    /// BOT_PEOPLE_SUMMARY: the switches, the rows per table, the scope's answers per rule (allowed/refused) and the
    /// refusals by reason, and the logouts, over the last <paramref name="seconds"/>. Pure.
    /// </summary>
    public static string SummaryLine(long seconds, BotPeopleSwitches switches, int[] rows, long[,] scopeCounts, long logouts) =>
        $"BOT_PEOPLE_SUMMARY seconds={seconds} {switches.Describe()} rows={string.Join(",", BotPeopleTables.All.Zip(rows, (t, n) => $"{t[(t.IndexOf("bot_") + 4)..]}:{n}"))} " +
        $"scope: {BotPeopleScope.Describe(scopeCounts)} logouts={logouts}";
}

/// <summary>
/// The packages' own 5-minute summary lines (BOT_SKILL_BUDGET, BOT_SKILL_MISTAKES, BOT_PLACE_BUDGET, BOT_WAY_SUMMARY,
/// BOT_MOVE_HABITS; docs/BOTS-AS-PEOPLE-1.2.md, "Log tags"). A package registers a function once (at server start or in a
/// static constructor) that returns its lines for the window and resets its counts; the store's timer logs them right
/// after BOT_PEOPLE_SUMMARY, off the game loop. A function that throws is skipped for that window.
/// </summary>
public static class BotPeopleSummary
{
    private static readonly ConcurrentDictionary<string, Func<long, IEnumerable<string>>> Sources = new(StringComparer.Ordinal);

    /// <summary>Registers (or replaces) the source with this tag. <paramref name="lines"/> gets the window in seconds.</summary>
    public static void Register(string tag, Func<long, IEnumerable<string>> lines)
    {
        if (string.IsNullOrWhiteSpace(tag) || lines == null)
            throw new ArgumentException("A summary needs a tag and a source");
        Sources[tag] = lines;
    }

    public static bool Unregister(string tag) => Sources.TryRemove(tag, out _);

    public static string[] Tags => Sources.Keys.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();

    /// <summary>Every source's lines for the window, by tag order; a failing source gives none.</summary>
    public static List<string> TakeLines(long seconds)
    {
        var lines = new List<string>();
        foreach (var (_, source) in Sources.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            try
            {
                lines.AddRange((source(seconds) ?? []).Where(line => !string.IsNullOrEmpty(line)));
            }
            catch
            {
                // A summary must never stop the save or the other summaries.
            }
        }
        return lines;
    }
}
