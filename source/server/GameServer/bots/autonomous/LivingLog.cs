using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Issue #625 set B: the long-running goals' rows, written to server.log. Low volume by design (hourly summaries, or
    /// one capped row per ended group), on by default, diagnostic only: they read the world and write lines, never change
    /// a decision. No bot or player names: realms, bands, group ids, region numbers and rounded positions only.
    /// <list type="bullet">
    /// <item>REALM_LEVEL_SPREAD: hourly, per realm and 5-level band (at most 3 x 11 = 33 rows an hour).</item>
    /// <item>GROUP_ENDED: per ended gamebot group (about 300-600 an hour at 3,000 bots; capped at 30 a minute).</item>
    /// <item>ECONOMY_PULSE: hourly, per realm (3 rows an hour; written by the economy ledger, EconomyPulse).</item>
    /// <item>STUCK_HOTSPOT: hourly, the top cells plus one summary row (at most 11 rows an hour).</item>
    /// </list>
    /// source/tools/living_report.py turns them into the morning summary.
    /// </summary>
    public static class LivingLogProperties
    {
        [ServerProperty("autonomous_diagnostics", "living_log_hourly_rows", "Hourly REALM_LEVEL_SPREAD and STUCK_HOTSPOT rows in server.log (issue #625; at most about 45 rows an hour). Diagnostic only; never changes a decision.", true)]
        public static bool HourlyRows = true;

        [ServerProperty("autonomous_diagnostics", "bot_group_ended_sample_every", "Log one GROUP_ENDED row for every Nth ended autonomous gamebot group (at most 30 a minute). 0 turns it off. Diagnostic only.", 1)]
        public static int GroupEndedSampleEvery = 1;
    }

    /// <summary>Shared text helpers of the #625 rows (one format: <c>TAG key=value key="text"</c>, invariant culture).</summary>
    public static class LivingLogText
    {
        public const int BandWidth = 5;

        /// <summary>The 5-level band of a level, as kill_xp_report.py's band_of: "00-04", "05-09", ..., "50-54".</summary>
        public static string Band(int level)
        {
            int low = Math.Max(0, level) / BandWidth * BandWidth;
            return string.Create(CultureInfo.InvariantCulture, $"{low:00}-{low + BandWidth - 1:00}");
        }

        public static string Realm(int realm) => realm switch
        {
            1 => "Albion",
            2 => "Midgard",
            3 => "Hibernia",
            _ => "None",
        };

        /// <summary>The UTC hour as the rows write it: 2026-11-25T10:00Z.</summary>
        public static string Hour(DateTime utc)
        {
            utc = utc.ToUniversalTime();
            return utc.ToString("yyyy-MM-dd'T'HH':00Z'", CultureInfo.InvariantCulture);
        }

        public static string Quote(string text) =>
            "\"" + (text ?? string.Empty).Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ') + "\"";

        /// <summary>"a:3,b:1" in the order given, zero counts left out; "-" for none.</summary>
        public static string Counts(IEnumerable<KeyValuePair<string, int>> counts)
        {
            string text = string.Join(",", counts.Where(pair => pair.Value > 0)
                .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}:{pair.Value}")));
            return text.Length == 0 ? "-" : text;
        }
    }

    // ---- Row 6: REALM_LEVEL_SPREAD ----------------------------------------------------------------------------------

    /// <summary>
    /// Row 6 (#544): once an hour, per realm and 5-level band, the online autonomous gamebots (not companions, not hired
    /// helpers, never the player), the levels gained since the last hour, and the bots that are new or were re-rolled
    /// under the same id (level fell, or class changed). The first hour after start is a baseline (baseline=1): every bot
    /// is "seen" then, none is new. Pure: the pulse feeds it a roster; tests feed it their own.
    /// <c>REALM_LEVEL_SPREAD hour= realm= band= bots= gained= levelled= new= rerolled= gone= baseline=</c>.
    /// </summary>
    public sealed class RealmLevelSpread
    {
        public readonly record struct Entry(long Id, int Realm, int Level, int ClassId);

        private Dictionary<long, Entry> _last;

        private sealed class Cell
        {
            public int Bots, Gained, Levelled, New, Rerolled, Gone;
        }

        public IReadOnlyList<string> Hour(DateTime hourUtc, IEnumerable<Entry> roster)
        {
            bool baseline = _last == null;
            var now = new Dictionary<long, Entry>();
            foreach (Entry entry in roster ?? [])
                now[entry.Id] = entry;
            var cells = new SortedDictionary<(int Realm, string Band), Cell>();
            Cell At(int realm, int level)
            {
                var key = (Math.Clamp(realm, 0, 3), LivingLogText.Band(level));
                if (!cells.TryGetValue(key, out Cell cell))
                    cells[key] = cell = new Cell();
                return cell;
            }

            foreach (Entry entry in now.Values)
            {
                Cell cell = At(entry.Realm, entry.Level);
                cell.Bots++;
                if (baseline)
                    continue;
                if (!_last.TryGetValue(entry.Id, out Entry before))
                {
                    cell.New++;
                    continue;
                }
                if (entry.Level < before.Level || entry.ClassId != before.ClassId || entry.Realm != before.Realm)
                {
                    cell.Rerolled++;
                    continue;
                }
                if (entry.Level > before.Level)
                {
                    cell.Gained += entry.Level - before.Level;
                    cell.Levelled++;
                }
            }
            if (!baseline)
                foreach (Entry before in _last.Values)
                    if (!now.ContainsKey(before.Id))
                        At(before.Realm, before.Level).Gone++;
            _last = now;

            string hour = LivingLogText.Hour(hourUtc);
            var lines = new List<string>(cells.Count);
            foreach (((int realm, string band), Cell cell) in cells)
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"REALM_LEVEL_SPREAD hour={hour} realm={LivingLogText.Realm(realm)} band={band} bots={cell.Bots} " +
                    $"gained={cell.Gained} levelled={cell.Levelled} new={cell.New} rerolled={cell.Rerolled} gone={cell.Gone} " +
                    $"baseline={(baseline ? 1 : 0)}"));
            return lines;
        }
    }

    // ---- Row 9: STUCK_HOTSPOT ---------------------------------------------------------------------------------------

    /// <summary>
    /// Row 9 (#555-type finds): every stuck or recovery event (the 15-minute and 45-minute watchdog recoveries, invalid
    /// position recoveries, zone seam cycles) is counted in a 500-unit cell of its region, merged across bots and kinds.
    /// Once an hour the busiest cells are written, busiest first, then one summary row (rank=0). Counting is a dictionary
    /// add under a lock; at most <see cref="MaxCells"/> cells an hour (later cells are counted in dropped=).
    /// <c>STUCK_HOTSPOT hour= rank= region= x= y= grid=500 total= bots= stuck15m= stall45m= position= seam=</c>.
    /// </summary>
    public sealed class StuckHotspots
    {
        public const int Grid = 500;
        public const int Top = 10;
        public const int MaxCells = 5_000;
        public const int MaxBotsPerCell = 64;
        public static readonly string[] Kinds = ["stuck15m", "stall45m", "position", "seam"];
        public static readonly StuckHotspots Shared = new();

        private sealed class Cell
        {
            public readonly int[] ByKind = new int[Kinds.Length];
            public readonly HashSet<long> Bots = new();
            public int Total;
        }

        private readonly object _gate = new();
        private Dictionary<(int Region, int Gx, int Gy), Cell> _cells = new();
        private int _dropped;

        /// <summary>The cell a coordinate falls in: rounded to the nearest <see cref="Grid"/>.</summary>
        public static int Round(int coordinate) => (int)Math.Round(coordinate / (double)Grid, MidpointRounding.AwayFromZero);

        public static string KindOfMarker(string logMarker) => logMarker switch
        {
            "AUTONOMOUS_STUCK_15M_RECOVERY" => "stuck15m",
            "AUTONOMOUS_GOAL_STALL_45M_RECOVERY" => "stall45m",
            _ => "position",
        };

        public void Note(string kind, long botId, int region, int x, int y)
        {
            int k = Array.IndexOf(Kinds, kind);
            if (k < 0)
                return;
            var key = (region, Round(x), Round(y));
            lock (_gate)
            {
                if (!_cells.TryGetValue(key, out Cell cell))
                {
                    if (_cells.Count >= MaxCells)
                    {
                        _dropped++;
                        return;
                    }
                    _cells[key] = cell = new Cell();
                }
                cell.ByKind[k]++;
                cell.Total++;
                if (cell.Bots.Count < MaxBotsPerCell)
                    cell.Bots.Add(botId);
            }
        }

        /// <summary>Never throws: a log helper on the watchdog's path.</summary>
        public static void NoteShared(string kind, GameBot bot)
        {
            try
            {
                if (bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false })
                    Shared.Note(kind, bot.DatabaseID, bot.CurrentRegionID, bot.X, bot.Y);
            }
            catch { }
        }

        /// <summary>The hour's rows (top cells, then rank=0), and a fresh hour.</summary>
        public IReadOnlyList<string> TakeHour(DateTime hourUtc)
        {
            Dictionary<(int Region, int Gx, int Gy), Cell> cells;
            int dropped;
            lock (_gate)
            {
                cells = _cells;
                dropped = _dropped;
                _cells = new();
                _dropped = 0;
            }
            string hour = LivingLogText.Hour(hourUtc);
            var lines = new List<string>();
            int rank = 0;
            foreach (var pair in cells.OrderByDescending(pair => pair.Value.Total).ThenByDescending(pair => pair.Value.Bots.Count)
                         .ThenBy(pair => pair.Key.Region).ThenBy(pair => pair.Key.Gx).ThenBy(pair => pair.Key.Gy).Take(Top))
            {
                var text = new StringBuilder();
                text.Append(CultureInfo.InvariantCulture,
                    $"STUCK_HOTSPOT hour={hour} rank={++rank} region={pair.Key.Region} x={pair.Key.Gx * Grid} y={pair.Key.Gy * Grid} " +
                    $"grid={Grid} total={pair.Value.Total} bots={pair.Value.Bots.Count}");
                for (int k = 0; k < Kinds.Length; k++)
                    text.Append(CultureInfo.InvariantCulture, $" {Kinds[k]}={pair.Value.ByKind[k]}");
                lines.Add(text.ToString());
            }
            int total = cells.Values.Sum(cell => cell.Total);
            int regions = cells.Keys.Select(key => key.Region).Distinct().Count();
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"STUCK_HOTSPOT hour={hour} rank=0 cells={cells.Count} regions={regions} total={total} dropped={dropped} grid={Grid}"));
            return lines;
        }

        public void Reset()
        {
            lock (_gate)
            {
                _cells = new();
                _dropped = 0;
            }
        }
    }

    // ---- Row 7: GROUP_ENDED -----------------------------------------------------------------------------------------

    /// <summary>Why a gamebot's camp attempt ended GroupChanged (AutonomousWorldBotController decides each one).</summary>
    public enum GroupChangeCause
    {
        /// <summary>The bot joined a new autonomous group (counted against the new group).</summary>
        Joined,
        /// <summary>The group's size changed and it is choosing a target again ("Choosing group target").</summary>
        Resized,
        /// <summary>The group went back to an assembly phase (a wipe or no-show resize) with a camp in hand.</summary>
        Assembling,
        /// <summary>The forming party's leader picked the meetup camp: the attempt the pick opened ends at once.</summary>
        MeetupPlan,
        /// <summary>A party casualty: the group regroups before new pulls.</summary>
        Casualty,
    }

    /// <summary>
    /// Row 7 (#544: 71-80% of group camp attempts end GroupChanged): one row per ended autonomous gamebot group
    /// (AutonomousBotGroupCoordinator sessions; companions and the player's own group are not sessions).
    /// <c>GROUP_ENDED seq= every= dropped= group= realm= objective= reason= cause= last_left= phase= task_started= camp=
    /// dur_s= size_start= size_end= alive_end= left= left_by= wipes= gc= open= hosted=</c>
    /// <list type="bullet">
    /// <item>reason/cause: the group's end, classified from the coordinator's reason (<see cref="Classify"/>); a party
    /// whose every member is dead at the end is reason=wipe.</item>
    /// <item>left/left_by/last_left: members who left during the group's life, by the bot's state as it left (logout,
    /// dead, other).</item>
    /// <item>gc: the members' camp attempts that ended GroupChanged during the group's life, by cause
    /// (<see cref="GroupChangeCause"/>); open: the attempts still open at the end, which close as "Autonomous group
    /// disbanded" (a GroupChanged too) on the members' next turn.</item>
    /// </list>
    /// Sampled: every Nth ended group (bot_group_ended_sample_every, default 1) and at most 30 a minute.
    /// </summary>
    public static class GroupEndedLog
    {
        public const string Marker = "GROUP_ENDED";
        public const int MaxRowsPerMinute = 30;
        public const int MaxTallies = 4_096;
        private static readonly Logger Log = LoggerManager.Create(typeof(GroupEndedLog));
        internal static readonly KillXpSampler Sampler = new(MaxRowsPerMinute);
        private static readonly ConcurrentDictionary<string, int[]> GroupChanged = new(StringComparer.Ordinal);
        private static readonly int CauseCount = Enum.GetValues<GroupChangeCause>().Length;

        public static string CauseName(GroupChangeCause cause) => cause switch
        {
            GroupChangeCause.Joined => "joined",
            GroupChangeCause.Resized => "resized",
            GroupChangeCause.Assembling => "assembling",
            GroupChangeCause.MeetupPlan => "meetup_plan",
            GroupChangeCause.Casualty => "casualty",
            _ => "other",
        };

        /// <summary>A member's camp attempt ended GroupChanged for <paramref name="cause"/> while in group <paramref name="groupId"/>.</summary>
        public static void CountGroupChanged(string groupId, GroupChangeCause cause)
        {
            if (string.IsNullOrEmpty(groupId))
                return;
            try
            {
                if (GroupChanged.Count >= MaxTallies && !GroupChanged.ContainsKey(groupId))
                    GroupChanged.Clear(); // a safety valve only: a tally is taken when its group ends
                int[] tally = GroupChanged.GetOrAdd(groupId, static _ => new int[CauseCount]);
                Interlocked.Increment(ref tally[Math.Clamp((int)cause, 0, CauseCount - 1)]);
            }
            catch { }
        }

        /// <summary>The group's GroupChanged tally, taken (removed).</summary>
        public static int[] TakeGroupChanged(string groupId) =>
            !string.IsNullOrEmpty(groupId) && GroupChanged.TryRemove(groupId, out int[] tally) ? tally : new int[CauseCount];

        public static int PendingTallies => GroupChanged.Count;

        public static void ResetForTests()
        {
            GroupChanged.Clear();
        }

        /// <summary>
        /// The coordinator's end reason (free text, as AUTONOMOUS_GROUP_TASK_ENDED writes it) as a stable reason and cause.
        /// A code given by the caller ("released/raid") wins. Unknown text is other/unclassified.
        /// </summary>
        public static (string Reason, string Cause) Classify(string text, string code = null)
        {
            if (!string.IsNullOrEmpty(code))
            {
                int slash = code.IndexOf('/');
                return slash < 0 ? (code, "-") : (code[..slash], code[(slash + 1)..]);
            }
            text ??= string.Empty;
            bool Has(string part) => text.Contains(part, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(text, AutonomousBotGroupCoordinator.SharedTaskExpiredReason, StringComparison.Ordinal)) return ("finished", "expired");
            if (text.StartsWith("Natural break", StringComparison.Ordinal)) return ("finished", "natural_break");
            if (text.StartsWith("Lost members to ", StringComparison.Ordinal)) return ("danger", "far_above"); // #569 step 2
            if (Has("left the locked PvE party")) return ("member_left", "no_core");
            if (text.StartsWith("A member left and fewer than two", StringComparison.Ordinal)) return ("member_left", "too_few");
            if (text.StartsWith("No replacement ", StringComparison.Ordinal)) return ("member_left", "no_replacement");
            if (Has("replacement leader has no valid") || Has("replacement meetup leader has no valid")) return ("leader_left", "no_staging");
            if (Has("leader left")) return ("leader_left", "no_rendezvous");
            if (Has("No living group leader") || Has("No living PvE leader")) return ("no_leader", "none_alive");
            if (Has("viable tank, healer") || Has("lost its viable")) return ("roster", "no_core");
            if (Has("Not enough active members") || Has("Fewer than two active members")) return ("roster", "too_few");
            if (Has("locked PvE party roster changed")) return ("roster", "changed");
            if (Has("group roster is no longer valid")) return ("roster", "invalid");
            if (Has("failed to reach")) return ("meetup", "leader_late");
            if (Has("missed the meetup deadline")) return ("meetup", "no_show");
            if (Has("regroup member has no connected route")) return ("meetup", "regroup_route");
            if (Has("expedition hub formation")) return ("meetup", "hub");
            if (Has("PvE meetup lost") || Has("replacement PvE rendezvous") || Has("no valid formation slots")) return ("meetup", "unreachable");
            if (Has("No reachable non-grey group camp")) return ("no_camp", "none_reachable");
            if (text.StartsWith("Verified individual recovery", StringComparison.Ordinal)) return ("recovery", "member_stuck");
            if (Has("Reassigned by a forced realm expedition")) return ("released", "forced_expedition");
            return ("other", "unclassified");
        }

        /// <summary>How a member was leaving, from the bot's own state (logout, dead, other).</summary>
        public static string LeftCause(GameBot bot)
        {
            if (bot == null) return "other";
            if (bot.ObjectState != GameObject.eObjectState.Active) return "logout";
            return bot.IsAlive ? "other" : "dead";
        }

        public sealed class Row
        {
            public long Sequence { get; init; }
            public int Every { get; init; }
            public long Dropped { get; init; }
            public string Group { get; init; } = string.Empty;
            public string Realm { get; init; } = "None";
            public string Objective { get; init; } = string.Empty;
            public string Reason { get; init; } = "other";
            public string Cause { get; init; } = "-";
            public string LastLeft { get; init; } = "-";
            public string Phase { get; init; } = string.Empty;
            public bool TaskStarted { get; init; }
            public bool HasCamp { get; init; }
            public long DurationSeconds { get; init; }
            public int SizeStart { get; init; }
            public int SizeEnd { get; init; }
            public int AliveEnd { get; init; }
            public IReadOnlyDictionary<string, int> LeftBy { get; init; } = new Dictionary<string, int>();
            public int Wipes { get; init; }
            public int[] GroupChanged { get; init; } = [];
            public int Open { get; init; }
            public bool Hosted { get; init; }
        }

        public static string Format(Row row)
        {
            var c = CultureInfo.InvariantCulture;
            var gc = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < CauseCount; i++)
                gc.Add(new(CauseName((GroupChangeCause)i), i < row.GroupChanged.Length ? row.GroupChanged[i] : 0));
            int left = row.LeftBy.Values.Sum();
            return string.Create(c,
                $"{Marker} seq={row.Sequence} every={row.Every} dropped={row.Dropped} group={row.Group} realm={row.Realm} " +
                $"objective={row.Objective} reason={row.Reason} cause={row.Cause} last_left={row.LastLeft} " +
                $"phase={LivingLogText.Quote(row.Phase)} task_started={(row.TaskStarted ? 1 : 0)} camp={(row.HasCamp ? 1 : 0)} " +
                $"dur_s={Math.Max(0, row.DurationSeconds)} size_start={row.SizeStart} size_end={row.SizeEnd} alive_end={row.AliveEnd} " +
                $"left={left} left_by={LivingLogText.Counts(row.LeftBy.OrderBy(pair => pair.Key, StringComparer.Ordinal))} " +
                $"wipes={row.Wipes} gc={LivingLogText.Counts(gc)} gc_total={gc.Sum(pair => pair.Value)} open={row.Open} " +
                $"hosted={(row.Hosted ? 1 : 0)}");
        }

        /// <summary>True when this ended group is sampled (and fills the row's sampling fields).</summary>
        public static bool TakeSample(out long sequence, out int every, out long dropped)
        {
            every = LivingLogProperties.GroupEndedSampleEvery;
            sequence = 0;
            dropped = 0;
            return every > 0 && Sampler.Take(every, Environment.TickCount64, out sequence, out dropped);
        }

        public static void Write(Row row)
        {
            try
            {
                if (Log.IsInfoEnabled)
                    Log.Info(Format(row));
            }
            catch { } // a log line never touches the group
        }
    }

    // ---- The hourly pulse -------------------------------------------------------------------------------------------

    /// <summary>
    /// Writes the hourly rows 6 and 9 at each UTC hour (a one-minute timer, like RvrRecordPulse). It reads the bot
    /// registry and the hotspot counts and writes lines, nothing else. Off with living_log_hourly_rows.
    /// </summary>
    public static class LivingLogPulse
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(LivingLogPulse));
        public const int PulseMilliseconds = 60_000;
        public static readonly RealmLevelSpread Spread = new();
        private static Timer _timer;
        private static int _running;
        private static DateTime _nextHourUtc;

        [GameServerStartedEvent]
        public static void Start(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            _nextHourUtc = DateTime.UtcNow; // the first pulse takes the level baseline
            _timer = new Timer(_ => Pulse(), null, PulseMilliseconds, PulseMilliseconds);
        }

        [GameServerStoppedEvent]
        public static void Stop(DOLEvent e, object sender, EventArgs args)
        {
            _timer?.Dispose();
            _timer = null;
        }

        public static IEnumerable<RealmLevelSpread.Entry> Roster() =>
            AutonomousBotRegistry.Snapshot()
                .Where(bot => bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false })
                .Select(bot => new RealmLevelSpread.Entry(bot.DatabaseID, (int)bot.Realm, bot.Level, bot.CharacterClass?.ID ?? 0));

        private static void Pulse()
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
                return;
            try
            {
                DateTime utc = DateTime.UtcNow;
                if (utc < _nextHourUtc || !LivingLogProperties.HourlyRows)
                    return;
                // The first pulse after start writes the baseline (baseline=1) and the first minutes' hotspots.
                DateTime hour = new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);
                _nextHourUtc = hour.AddHours(1);
                if (!Log.IsInfoEnabled)
                    return;
                foreach (string line in Spread.Hour(hour, Roster().ToArray()))
                    Log.Info(line);
                foreach (string line in StuckHotspots.Shared.TakeHour(hour))
                    Log.Info(line);
            }
            catch (Exception exception)
            {
                try { Log.Warn("Living log: hourly rows", exception); } catch { }
            }
            finally
            {
                Interlocked.Exchange(ref _running, 0);
            }
        }
    }
}
