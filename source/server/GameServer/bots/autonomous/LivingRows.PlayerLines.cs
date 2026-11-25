using System;
using System.Collections.Generic;
using System.Globalization;

namespace DOL.GS;

/// <summary>
/// PLAYER_LINE_OUTCOME (#625 row 5): each line the real player types that bots may answer (say, group, tell, faction,
/// trade), and what came of it within <see cref="WindowMilliseconds"/>. Our answer-rate check in real play (the chat bench
/// covers only the simulation).
/// <c>PLAYER_LINE_OUTCOME seq= t= ch= realm= intent= answered= latencyMs= kind= bots= replies= window=</c>
/// <list type="bullet">
/// <item><c>seq</c>: the line's CHAT_HEAR seq in the session chat log (so the line can be found there; no name here).</item>
/// <item><c>answered</c>: 1 when any bot answered within the window; <c>latencyMs</c> the first answer's delay (-1 none).</item>
/// <item><c>kind</c>: the first answer's kind: answer, knowledge, ack, emote, or none.</item>
/// <item><c>bots</c>: distinct answering bots; <c>replies</c>: their lines (and emotes) within the window.</item>
/// </list>
/// Hooked where the player's line enters the bot reply path (BotSayGate.Hear) and where a bot's line leaves it
/// (BotSayGate's send-and-record step, with the player line it answers, re=). The kind comes from the reply path's own
/// decision (AutonomousChatWhy.Record). No timer per line: a line's outcome is settled by the next player line or the
/// rows' shared pulse (LivingRowsPulse), once its window is up. At most <see cref="PerMinute"/> rows a minute.
/// </summary>
public static class PlayerLineOutcomes
{
    public const string Marker = "PLAYER_LINE_OUTCOME";
    public const long WindowMilliseconds = 30_000;
    public const int PerMinute = 30;
    public const int Capacity = 64;

    /// <summary>A reply decision is matched to its sent line for this long.</summary>
    public const long DecisionMilliseconds = 60_000;

    private static readonly HashSet<string> Channels = new(StringComparer.Ordinal) { "say", "group", "tell", "faction", "trade" };

    private sealed class Pending
    {
        public long Seq, At;
        public string Channel, Intent;
        public eRealm Realm;
        public WeakReference<GamePlayer> Player;
        public long FirstAt = -1;
        public string FirstKind;
        public readonly HashSet<string> Bots = new(StringComparer.Ordinal);
        public int Replies;
    }

    private static readonly List<Pending> Lines = new();
    private static readonly Dictionary<string, (string Kind, long At)> LastDecision = new(StringComparer.Ordinal);

    /// <summary>Whether a channel's player lines are followed.</summary>
    public static bool Follows(string channel) => channel != null && Channels.Contains(channel);

    /// <summary>The reply kind of a reply-path decision (outcome, route and code). Pure.</summary>
    public static string KindOf(string outcome, string rule, string code)
    {
        if (outcome == "ack" || rule?.StartsWith("ack", StringComparison.Ordinal) == true)
            return "ack";
        if (rule?.StartsWith("knowledge", StringComparison.Ordinal) == true || code == AutonomousChatWhy.Codes.SelfKnowledge)
            return "knowledge";
        return "answer";
    }

    public static string Format(long seq, long now, string channel, eRealm realm, string intent, long latency, string kind, int bots, int replies) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Marker} seq={seq} t={now} ch={channel} realm={RvrRecordText.Realm(realm)} intent={intent ?? "?"} " +
            $"answered={(latency >= 0 ? 1 : 0)} latencyMs={latency} kind={kind ?? "none"} bots={bots} replies={replies} window={WindowMilliseconds}");

    /// <summary>A real player's line (BotSayGate.Hear, with its CHAT_HEAR seq). Never throws.</summary>
    public static void Heard(long seq, string channel, GamePlayer player, string intent, long now)
    {
        try
        {
            if (player == null || !Follows(channel))
                return;
            Settle(now);
            lock (Lines)
            {
                if (Lines.Count >= Capacity)
                {
                    Write(Lines[0]);
                    Lines.RemoveAt(0);
                }
                Lines.Add(new Pending
                {
                    Seq = seq, At = now, Channel = channel, Realm = player.Realm, Intent = intent,
                    Player = new WeakReference<GamePlayer>(player),
                });
            }
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>A reply-path decision that ended in a line (AutonomousChatWhy.Record): its kind, for the sent line.</summary>
    public static void Decided(in AutonomousChatWhy.Decision decision)
    {
        try
        {
            if (decision.Bot == null || decision.Outcome is not ("said" or "ack" or "pileon"))
                return;
            lock (Lines)
            {
                if (Lines.Count == 0)
                    return;
                if (LastDecision.Count >= 256)
                    LastDecision.Clear();
                LastDecision[decision.Bot] = (KindOf(decision.Outcome, decision.Rule, decision.Code), LivingRows.Now());
            }
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>A bot's line was sent; <paramref name="re"/> is the player line it answers (0: none). Never throws.</summary>
    public static void Answered(long re, string speaker, long now)
    {
        if (re <= 0 || string.IsNullOrEmpty(speaker))
            return;
        try
        {
            lock (Lines)
            {
                Pending line = Lines.Find(pending => pending.Seq == re);
                if (line == null || now - line.At > WindowMilliseconds)
                    return;
                string kind = "answer";
                if (LastDecision.Remove(speaker, out (string Kind, long At) decided) && now - decided.At <= DecisionMilliseconds)
                    kind = decided.Kind;
                Note(line, speaker, kind, now);
            }
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>
    /// A bot's emote: an answer to the newest unanswered say or group line of a player of its realm within say range (or in
    /// its group), inside the window. Never throws.
    /// </summary>
    public static void Emoted(GameBot bot, long now)
    {
        if (bot == null)
            return;
        try
        {
            lock (Lines)
            {
                for (int index = Lines.Count - 1; index >= 0; index--)
                {
                    Pending line = Lines[index];
                    if (now - line.At > WindowMilliseconds)
                        break;
                    if (line.FirstAt >= 0 || line.Realm != bot.Realm || line.Channel is not ("say" or "group") ||
                        !line.Player.TryGetTarget(out GamePlayer player))
                        continue;
                    bool near = line.Channel == "say"
                        ? player.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE)
                        : player.Group != null && ReferenceEquals(player.Group, bot.Group);
                    if (!near)
                        continue;
                    Note(line, bot.Name, "emote", now);
                    return;
                }
            }
        }
        catch
        {
            // Logging only.
        }
    }

    private static void Note(Pending line, string speaker, string kind, long now)
    {
        if (line.FirstAt < 0)
        {
            line.FirstAt = now;
            line.FirstKind = kind;
        }
        line.Bots.Add(speaker);
        line.Replies++;
    }

    /// <summary>Writes the lines whose window is up. Returns how many. Never throws.</summary>
    public static int Settle(long now)
    {
        int written = 0;
        try
        {
            lock (Lines)
            {
                while (Lines.Count > 0 && now - Lines[0].At >= WindowMilliseconds)
                {
                    Write(Lines[0]);
                    Lines.RemoveAt(0);
                    written++;
                }
            }
        }
        catch
        {
            // Logging only.
        }
        return written;
    }

    private static void Write(Pending line)
    {
        if (!LivingRows.Enabled || !LivingRows.Take(Marker, PerMinute))
            return;
        long latency = line.FirstAt >= 0 ? line.FirstAt - line.At : -1;
        LivingRows.Emit(Format(line.Seq, line.At, line.Channel, line.Realm, line.Intent, latency, line.FirstKind, line.Bots.Count,
            line.Replies));
    }

    /// <summary>How many player lines wait for their window (tests).</summary>
    public static int PendingCount
    {
        get
        {
            lock (Lines)
                return Lines.Count;
        }
    }

    /// <summary>Tests.</summary>
    public static void Reset()
    {
        lock (Lines)
        {
            Lines.Clear();
            LastDecision.Clear();
        }
    }
}
