using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace DOL.GS;

// ---- The RvR record (issue #505, audit piece A; docs/rvr-audit-1.2.3/structure.md §5 A, evidence.md §4) -----------------
//
// Logging only. Nothing in these files changes what a bot does: no game state is written, no target, path or aggro is
// set, and no random number is drawn (UT_RvrRecord.RecordFilesDrawNoRandomness pins that). The rows are read by
// source/tools/rvr_hour.py (the "RvR hour" report, also `played_check_log.py --rvr-hour`) and
// source/tools/rvr_fight_timeline.py (per-fight and per-siege timelines). Logs stay out of git (AGENTS.md).
//
// Volume: decision rows are change-only (a row per decision change, never per tick); rows that could repeat per tick
// pass a per-tag minute budget whose dropped lines are counted and reported in the next RVR_HOUR (dropped=), so a budget
// never hides that lines were dropped.

/// <summary>Formatting helpers shared by the record's rows. Pure.</summary>
public static class RvrRecordText
{
    /// <summary>A value for a key=value field: quoted when it has a space or is empty, quotes removed.</summary>
    public static string Field(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";
        value = value.Replace('"', '\'');
        return value.IndexOf(' ') >= 0 || value.IndexOf('=') >= 0 ? $"\"{value}\"" : value;
    }

    public static string Realm(eRealm realm) => realm switch
    {
        eRealm.Albion => "Albion",
        eRealm.Midgard => "Midgard",
        eRealm.Hibernia => "Hibernia",
        _ => "None",
    };

    /// <summary>
    /// A living as it appears in a row: a bot by name; the human as "player" (his name goes in a player= field, which the
    /// tools mask, as played_check_log.py does).
    /// </summary>
    public static string Who(GameLiving living) => living switch
    {
        null => "-",
        GamePlayer => "player",
        _ => Field(living.Name),
    };

    /// <summary>The kind of a living for the rows: Player, Bot, Pet, Guard, Siege, Door or Npc.</summary>
    public static string Kind(GameLiving living) => living switch
    {
        null => "-",
        GamePlayer => "Player",
        GameBot => "Bot",
        GameSiegeWeapon => "Siege",
        Keeps.GameKeepDoor => "Door",
        Keeps.GameKeepGuard => "Guard",
        GameNPC { Brain: AI.Brain.IControlledBrain } => "Pet",
        _ => "Npc",
    };
}

/// <summary>
/// A per-tag minute budget that counts what it drops (unlike RvrLogBudget, whose drops are silent). The dropped total is
/// taken by the hourly RVR_HOUR line. Thread-safe.
/// </summary>
public static class RvrRecordBudget
{
    private sealed class Window
    {
        public long Minute;
        public int Count;
    }

    private static readonly ConcurrentDictionary<string, Window> Windows = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> Dropped = new(StringComparer.Ordinal);

    /// <summary>True when a line of <paramref name="tag"/> may be written now; a refused line is counted.</summary>
    public static bool Take(string tag, long now, int perMinute)
    {
        Window window = Windows.GetOrAdd(tag, static _ => new Window());
        lock (window)
        {
            long minute = now / 60_000;
            if (window.Minute != minute)
            {
                window.Minute = minute;
                window.Count = 0;
            }
            if (window.Count < perMinute)
            {
                window.Count++;
                return true;
            }
        }
        Dropped.AddOrUpdate(tag, 1, static (_, n) => n + 1);
        return false;
    }

    /// <summary>"TAG:n,TAG:n" of the lines dropped since the last call (and resets them); "0" when none.</summary>
    public static string TakeDropped()
    {
        var parts = new List<string>();
        foreach (string tag in Dropped.Keys)
            if (Dropped.TryRemove(tag, out long n) && n > 0)
                parts.Add($"{tag}:{n}");
        parts.Sort(StringComparer.Ordinal);
        return parts.Count == 0 ? "0" : string.Join(",", parts);
    }

    /// <summary>Forgets a tag's minute window (a per-attempt tag whose attempt ended, #644). Its counted drops stay for RVR_HOUR.</summary>
    public static void Forget(string tag)
    {
        if (tag != null) Windows.TryRemove(tag, out _);
    }

    /// <summary>Tests.</summary>
    public static void Reset()
    {
        Windows.Clear();
        Dropped.Clear();
    }
}

/// <summary>
/// Change-only memory of the last value of each (bot, what) decision. Thread-safe; any brain may write its own bot's rows.
/// </summary>
public sealed class RvrDecisionMemory
{
    private readonly ConcurrentDictionary<(long Bot, string What), string> _last = new();

    /// <summary>
    /// Records <paramref name="to"/> as the bot's current <paramref name="what"/>. True (with the previous value in
    /// <paramref name="from"/>, "-" for none) when it changed; false when it is what was recorded last.
    /// </summary>
    public bool Change(long bot, string what, string to, out string from)
    {
        to ??= "-";
        var key = (bot, what);
        while (true)
        {
            if (!_last.TryGetValue(key, out string previous))
            {
                if (_last.TryAdd(key, to))
                {
                    from = "-";
                    return true;
                }
                continue;
            }
            if (string.Equals(previous, to, StringComparison.Ordinal))
            {
                from = previous;
                return false;
            }
            if (_last.TryUpdate(key, to, previous))
            {
                from = previous;
                return true;
            }
        }
    }

    /// <summary>The last recorded value, or null.</summary>
    public string Last(long bot, string what) => _last.TryGetValue((bot, what), out string value) ? value : null;

    public int Count => _last.Count;

    public void Clear() => _last.Clear();
}

/// <summary>
/// Piece A: RVR_DECIDE, one row per change of a decision, written where it is made.
/// <c>RVR_DECIDE t= bot= id= realm= force= layer= what= from= to= rule= why=</c>.
/// <list type="bullet">
/// <item><c>what</c>: destination, intent, target, engage, flee or legs.</item>
/// <item><c>layer</c>: who decided (controller, event, scan, brain, plan, flee, roam, chat, companion, coordinator).</item>
/// <item><c>rule</c>: a short code per site (<c>exec.committed</c>, <c>roam.override</c>, <c>scan.distributed</c>, ...;
/// each site names its own; rvr_hour.py lists the top rules per hour).</item>
/// </list>
/// Change-only: the same <c>to</c> twice in a row for the same bot and <c>what</c> writes nothing, so the cost follows
/// decisions, not ticks. Engage and flee rows are events (each one is a decision) and are written each time, under a
/// counted budget.
/// </summary>
public static class RvrDecide
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrDecide));

    /// <summary>The safety budget for all RVR_DECIDE rows together, a minute; drops are counted in RVR_HOUR.</summary>
    public const int LinesPerMinute = 1_500;

    public static readonly RvrDecisionMemory Memory = new();

    /// <summary>The rule codes seen since start (for the CI ratchet's runtime twin and the hour report). Thread-safe.</summary>
    private static readonly ConcurrentDictionary<string, long> RuleCounts = new(StringComparer.Ordinal);

    /// <summary>The row. Pure.</summary>
    public static string Format(long t, string bot, long id, eRealm realm, string force, string layer, string what,
        string from, string to, string rule, string why, string player = null)
    {
        var text = new StringBuilder(160);
        text.Append("RVR_DECIDE t=").Append(t).Append(" bot=").Append(RvrRecordText.Field(bot)).Append(" id=").Append(id)
            .Append(" realm=").Append(RvrRecordText.Realm(realm)).Append(" force=").Append(RvrRecordText.Field(force ?? "-"))
            .Append(" layer=").Append(layer).Append(" what=").Append(what)
            .Append(" from=").Append(RvrRecordText.Field(from ?? "-")).Append(" to=").Append(RvrRecordText.Field(to ?? "-"))
            .Append(" rule=").Append(rule ?? "none");
        if (!string.IsNullOrEmpty(why))
            text.Append(" why=").Append(RvrRecordText.Field(why));
        if (!string.IsNullOrEmpty(player))
            text.Append(" player=").Append(RvrRecordText.Field(player));
        return text.ToString();
    }

    /// <summary>Whether <paramref name="rule"/> is a well-formed rule code: area.name, lower-case area. Pure.</summary>
    public static bool IsRuleCode(string rule)
    {
        if (string.IsNullOrEmpty(rule))
            return false;
        int dot = rule.IndexOf('.');
        if (dot <= 0 || dot == rule.Length - 1)
            return false;
        for (int i = 0; i < rule.Length; i++)
        {
            char c = rule[i];
            if (i < dot ? !(c is >= 'a' and <= 'z') : !(char.IsLetterOrDigit(c) || c == '.'))
                return false;
        }
        return true;
    }

    /// <summary>
    /// A change of a decision: written when <paramref name="to"/> differs from the bot's last recorded
    /// <paramref name="what"/>. Only autonomous gamebots are traced.
    /// </summary>
    public static void Change(GameBot bot, string layer, string what, string to, string rule, string why = null, string player = null)
    {
        if (bot == null || !bot.IsAutonomousWorldBot || !Log.IsInfoEnabled)
            return;
        if (!Memory.Change(bot.DatabaseID, what, to, out string from))
            return;
        Write(bot, layer, what, from, to, rule, why, player);
    }

    /// <summary>An event decision (engage, flee): written every time, under the shared budget.</summary>
    public static void Event(GameBot bot, string layer, string what, string to, string rule, string why = null, string player = null)
    {
        if (bot == null || !bot.IsAutonomousWorldBot || !Log.IsInfoEnabled)
            return;
        Memory.Change(bot.DatabaseID, what, to, out string from);
        Write(bot, layer, what, from, to, rule, why, player);
    }

    /// <summary>A target decision: the target's name, or "player" with the player= field when it is the human.</summary>
    public static void Target(GameBot bot, string layer, GameLiving target, string rule, string why = null) =>
        Change(bot, layer, "target", RvrRecordText.Who(target), rule, why, target is GamePlayer player ? player.Name : null);

    private static void Write(GameBot bot, string layer, string what, string from, string to, string rule, string why, string player)
    {
        long now = GameLoop.GameLoopTime;
        RuleCounts.AddOrUpdate(rule ?? "none", 1, static (_, n) => n + 1);
        if (!RvrRecordBudget.Take("RVR_DECIDE", now, LinesPerMinute))
            return;
        Log.Info(Format(now, bot.Name, bot.DatabaseID, bot.Realm, bot.TempProperties.GetProperty<string>("RvrEventForce"),
            layer, what, from, to, rule, why, player));
    }

    /// <summary>How many decision rows each rule code has produced since start (the hour report's top rules).</summary>
    public static IReadOnlyDictionary<string, long> Rules() => new Dictionary<string, long>(RuleCounts, StringComparer.Ordinal);

    /// <summary>Tests.</summary>
    public static void Reset()
    {
        Memory.Clear();
        RuleCounts.Clear();
    }
}
