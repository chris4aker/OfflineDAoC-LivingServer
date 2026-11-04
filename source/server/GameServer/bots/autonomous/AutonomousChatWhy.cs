using System;
using System.Collections.Generic;
using System.Text;

namespace DOL.GS;

/// <summary>
/// Why a bot said a line, or why it stayed quiet (the chat framework's C0, docs/bot-voices/CHAT-BUDGETS.md). Every chat
/// decision that ends in a line, an acknowledgement or a silence for a player's line is recorded here with its rule, score
/// and reason, in a fixed ring of recent decisions; <c>/lswhy</c> shows them.
/// </summary>
public static class AutonomousChatWhy
{
    /// <summary>One decision. <see cref="Outcome"/> is "said", "ack", "silent", "pileon" or "held".</summary>
    public readonly record struct Decision(long At, long BotId, string Bot, eRealm Realm, string Channel, string Trigger, string Outcome,
        string Rule, int Score, string LineId, string Reason, string Text, string To);

    public const int Capacity = 1024;
    private static readonly Decision[] Ring = new Decision[Capacity];
    private static int _next;
    private static int _count;
    private static readonly object Sync = new();

    /// <summary>Remembers a decision (the oldest goes when the ring is full).</summary>
    public static void Record(in Decision decision)
    {
        lock (Sync)
        {
            Ring[_next] = decision;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
    }

    /// <summary>Remembers a decision about a bot's line (or its silence).</summary>
    public static void Record(GameBot bot, string channel, string trigger, string outcome, string rule, int score, string reason,
        string text = null, string lineId = null, string to = null, long? at = null) =>
        Record(new Decision(at ?? Now(), bot?.DatabaseID ?? 0, bot?.Name, bot?.Realm ?? eRealm.None, channel, trigger, outcome, rule, score,
            lineId, reason, text, to));

    /// <summary>Remembers a decision about a player's line that no bot answered (nobody to name).</summary>
    public static void RecordSilence(eRealm realm, string channel, string trigger, string rule, int score, string reason, string to, long? at = null) =>
        Record(new Decision(at ?? Now(), 0, null, realm, channel, trigger, "silent", rule, score, null, reason, null, to));

    private static long Now()
    {
        try
        {
            return GameLoop.GameLoopTime;
        }
        catch (Exception)
        {
            return Environment.TickCount64;
        }
    }

    /// <summary>The newest decisions first that match <paramref name="filter"/>, at most <paramref name="max"/>.</summary>
    public static List<Decision> Recent(int max, Func<Decision, bool> filter = null)
    {
        var found = new List<Decision>(Math.Min(Math.Max(max, 0), 64));
        lock (Sync)
        {
            for (int i = 0; i < _count && found.Count < max; i++)
            {
                Decision decision = Ring[(_next - 1 - i + Capacity) % Capacity];
                if (filter == null || filter(decision))
                    found.Add(decision);
            }
        }
        return found;
    }

    /// <summary>The newest decisions about one bot (by id, or by name when the id is unknown).</summary>
    public static List<Decision> ForBot(long botId, string name, int max) =>
        Recent(max, decision => botId > 0 ? decision.BotId == botId
            : !string.IsNullOrEmpty(name) && string.Equals(decision.Bot, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The bot that last said a line heard by this player's realm, or 0.</summary>
    public static long LastSpeaker(eRealm realm, string to = null)
    {
        List<Decision> last = Recent(1, decision => decision.BotId > 0 && decision.Realm == realm && decision.Outcome is "said" or "ack" or "pileon" &&
            (to == null || decision.To == null || string.Equals(decision.To, to, StringComparison.OrdinalIgnoreCase)));
        return last.Count > 0 ? last[0].BotId : 0;
    }

    /// <summary>A decision as one readable line: how long ago, where, what, by which rule and why.</summary>
    public static string Describe(in Decision decision, long now)
    {
        var text = new StringBuilder(160);
        long ago = Math.Max(0, now - decision.At) / 1000;
        text.Append(ago < 120 ? $"{ago}s ago" : $"{ago / 60}m ago").Append(" [").Append(decision.Channel).Append("] ");
        if (!string.IsNullOrEmpty(decision.Bot))
            text.Append(decision.Bot).Append(' ');
        text.Append(decision.Outcome);
        if (!string.IsNullOrEmpty(decision.Trigger))
            text.Append(" (").Append(decision.Trigger).Append(')');
        if (!string.IsNullOrEmpty(decision.Rule))
            text.Append(" rule ").Append(decision.Rule).Append(" score ").Append(decision.Score);
        if (!string.IsNullOrEmpty(decision.LineId))
            text.Append(" line ").Append(decision.LineId);
        if (!string.IsNullOrEmpty(decision.Reason))
            text.Append(": ").Append(decision.Reason);
        if (!string.IsNullOrEmpty(decision.Text))
            text.Append(" -> \"").Append(decision.Text).Append('"');
        return text.ToString();
    }

    #region Ambient counts (no text: they happen on bot ticks)

    /// <summary>Why an ambient opening was held back.</summary>
    public enum Held { Ceiling, Gap, NoLine, Duplicate }

    private static readonly long[] HeldCounts = new long[4 * (int)AutonomousChatBudget.Channel.Count];

    /// <summary>Counts an ambient opening held back in a channel (no allocation).</summary>
    public static void CountHeld(AutonomousChatBudget.Channel channel, Held why) =>
        System.Threading.Interlocked.Increment(ref HeldCounts[(int)why * (int)AutonomousChatBudget.Channel.Count + (int)channel]);

    /// <summary>How many ambient openings were held back in a channel for this reason since the server started.</summary>
    public static long HeldCount(AutonomousChatBudget.Channel channel, Held why) =>
        System.Threading.Interlocked.Read(ref HeldCounts[(int)why * (int)AutonomousChatBudget.Channel.Count + (int)channel]);

    #endregion

    /// <summary>Forgets every decision and count (tests).</summary>
    public static void Clear()
    {
        lock (Sync)
        {
            Array.Clear(Ring);
            _next = 0;
            _count = 0;
        }
        Array.Clear(HeldCounts);
    }
}
