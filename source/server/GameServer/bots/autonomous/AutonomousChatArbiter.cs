using System;

namespace DOL.GS;

/// <summary>
/// Who answers a player's line, and when (Beta 1.1.2, the chat framework's arbitration, docs/bot-voices/CHAT-BUDGETS.md).
/// One primary responder per player line per channel; then, where a crowd would, a few pile-ons: more hellos for a hello
/// or a ding, a "lol" or "+1" for a joke or a remark, never a second answer to a question. A reply arrives after the bot
/// has read the line, thought for a moment and typed it at its own speed, so a long answer takes longer than "?". Pure.
/// </summary>
public static class AutonomousChatArbiter
{
    /// <summary>The plan for one player line: the primary answer, social pile-ons (their own short reply of the line's
    /// kind: "hey", "gz") and reaction pile-ons ("lol", "+1", "this", "^").</summary>
    public readonly record struct Plan(int Primary, int Social, int Reactions)
    {
        public int Total => Primary + Social + Reactions;
    }

    /// <summary>Intents a crowd answers with its own short reply each (several guildmates say hi).</summary>
    public static bool IsSocial(eAutonomousChatIntent intent) => intent is eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Ding or
        eAutonomousChatIntent.Back or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Alarm;

    /// <summary>Routes a bystander may react to with "lol", "+1" or "this": a joke, a mishap, a boast, banter, an opinion, news.</summary>
    public static string ReactionKind(string route, eAutonomousChatIntent intent)
    {
        if (route == null)
            return null;
        if (route is "shape player_mishap" or "intent laugh" or "intent banter laugh" or "shape player_goodnews" || intent == eAutonomousChatIntent.Laugh)
            return route == "shape player_goodnews" ? "cheer" : "laugh";
        // A call ("zerg at mmg", "free rps at amg") draws its own short answers, never "+1" or "same" (Beta 1.1.2, round 3).
        if (route.StartsWith("context inc", StringComparison.Ordinal) || route.StartsWith("context rvr", StringComparison.Ordinal) ||
            intent is eAutonomousChatIntent.Alarm or eAutonomousChatIntent.RvR or eAutonomousChatIntent.Rez)
            return null;
        if (route is "shape ask_opinion" or "shape complaint" || route.StartsWith("shape player_rl", StringComparison.Ordinal))
            return "agree";
        return null;
    }

    /// <summary>
    /// The plan for a player's line: <paramref name="wanted"/> is how many bots would answer it at all (the line's kind and
    /// channel, AutonomousBotChatCoordinator.PlayerReplyCount); the first is the primary answer, the rest become pile-ons of
    /// the kind that fits, at most <paramref name="cap"/>. A line said to one bot by name, or a question, has one answer.
    /// </summary>
    public static Plan Arrange(int wanted, eAutonomousChatIntent intent, string route, bool directed, bool question, int available, int cap, Random random)
    {
        if (wanted <= 0 || available <= 0)
            return new Plan(0, 0, 0);
        if (directed || available == 1)
            return new Plan(1, 0, 0);
        int extra = Math.Min(Math.Min(wanted - 1, cap), available - 1);
        if (IsSocial(intent) && !question)
            return new Plan(1, Math.Max(0, extra), 0);
        // A reaction now and then even when only one bot would answer (a crowd laughs at a good one).
        string kind = question ? null : ReactionKind(route, intent);
        if (kind == null)
            return new Plan(1, 0, 0);
        int reactions = extra > 0 ? extra : random.NextDouble() < 0.25 ? 1 : 0;
        return new Plan(1, 0, Math.Min(reactions, Math.Min(cap, available - 1)));
    }

    private static readonly string[] Laughs = ["lol", "haha", "heh", "lol", "rofl", "hehe", "lol!"];
    private static readonly string[] Cheers = ["gz", "grats", "nice!", "gz!", "woot", "grats :)"];
    private static readonly string[] Agrees = ["+1", "this", "^", "^ this", "yep", "agreed", "true", "same"];

    /// <summary>A pile-on reaction of a kind ("laugh", "cheer", "agree"). Pure.</summary>
    public static string Reaction(string kind, Random random) => kind switch
    {
        "laugh" => Laughs[random.Next(Laughs.Length)],
        "cheer" => Cheers[random.Next(Cheers.Length)],
        _ => Agrees[random.Next(Agrees.Length)],
    };

    /// <summary>
    /// How long a reply takes to arrive: reading the line (about 20 ms a character, at most 4 s), a moment's thought
    /// (0.3-1.5 s) and typing it at <paramref name="wpm"/> words a minute (five characters a word), so "?" comes in about
    /// two seconds and a sentence in eight. <paramref name="after"/> is when the previous reply in this exchange was due:
    /// replies stay in order and never land in the same second. Pure.
    /// </summary>
    public static int DelayMs(string incoming, string reply, int wpm, int after, Random random)
    {
        int reading = Math.Min(4_000, 400 + (incoming?.Length ?? 20) * (15 + random.Next(11)));
        int thinking = 300 + random.Next(1_201);
        double charsPerMs = Math.Clamp(wpm, 5, 200) * 5 / 60_000.0;
        int typing = (int)((reply?.Length ?? 0) / charsPerMs);
        // Round 8: never faster than a person can read and answer: about 2 s plus 0.3 s a word, with a little jitter (the stream
        // judge saw /faction answers 1-2 s after the question).
        int words = string.IsNullOrWhiteSpace(reply) ? 1 : reply.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int due = Math.Max(reading + thinking + typing, 2_000 + 300 * words + random.Next(601));
        return after > 0 ? Math.Max(due, after + 800 + random.Next(1_700)) : due;
    }
}
