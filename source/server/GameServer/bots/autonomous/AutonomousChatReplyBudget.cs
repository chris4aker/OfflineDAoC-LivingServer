using System;

namespace DOL.GS;

/// <summary>
/// The reply budget (Beta 1.1.3, docs/bot-voices/CHAT-BUDGETS.md): a bot answers a player only with a reply that fits the
/// line (AutonomousChatReplyFit at least chat-budget.json's "min_fit"). Below it the bot says nothing, unless the line was
/// a tell to it or named it, which gets a short human acknowledgement ("?", "ya?", "sec"), never a stock brush-off. The
/// first blind evaluation found silence read more human than the weak replies (3.99 against 3.30 of 6). This budget is
/// only for replies to the player: the world's own chatter has the ambient budget. Pure.
/// </summary>
public static class AutonomousChatReplyBudget
{
    public enum Outcome { Answer, Ack, Silent }

    /// <summary>What a reply of this fit becomes: said, an acknowledgement (to a tell or a line naming the bot), or silence.</summary>
    public static Outcome Decide(int fit, bool addressed, int minFit) =>
        fit >= minFit ? Outcome.Answer : addressed ? Outcome.Ack : Outcome.Silent;

    private static readonly string[] Acks = ["?", "ya?", "hm?", "yes?", "ya", "whats up?", "yo", "hmm?", "yeah?", "sup?"];
    private static readonly string[] BusyAcks = ["sec", "1 sec", "sec, fighting", "brt", "one sec"];

    /// <summary>
    /// A short acknowledgement. A bot leans on two or three of its own (fixed from its id), as people do; a busy bot says
    /// "sec". Pure.
    /// </summary>
    public static string Ack(long botId, bool busy, Random random)
    {
        if (busy)
            return BusyAcks[random.Next(BusyAcks.Length)];
        int own = (int)(AutonomousBotPersonality.Mix(botId, 53) % (ulong)Acks.Length);
        return random.NextDouble() < 0.7 ? Acks[(own + random.Next(3)) % Acks.Length] : Acks[random.Next(Acks.Length)];
    }

    /// <summary>
    /// A stock brush-off ("ask around maybe", "beats me", "i think so?", "good question"): the blind judge's most common
    /// complaint when said to a question anyone could answer. Pure.
    /// </summary>
    public static bool IsBrushOff(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = AutonomousChatQuestions.Normalize(text);
        // A style's emote at the end (":D", "heh") is no part of what is said.
        foreach (string tail in new[] { " d", " p", " lol", " heh", " haha" })
            if (t.EndsWith(tail, StringComparison.Ordinal))
                t = t[..^tail.Length];
        return t is "beats me" or "no idea" or "no idea sorry" or "not sure" or "dunno" or "no clue" or "good question" or "i think so" or
                   "hmm not sure" or "wish i knew" or "couldnt tell you" or "not a clue honestly" or "pretty sure yes but dont quote me" ||
               t.StartsWith("ask around", StringComparison.Ordinal) || t.Contains("someone said something about that") ||
               t.Contains("i was wondering that too") || t.StartsWith("no idea i just got here", StringComparison.Ordinal) ||
               t.StartsWith("cant help there", StringComparison.Ordinal) || t.StartsWith("maybe ask a", StringComparison.Ordinal) ||
               t.StartsWith("try asking a", StringComparison.Ordinal) || t.StartsWith("someone usually answers", StringComparison.Ordinal);
    }
}
