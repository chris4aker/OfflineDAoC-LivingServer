using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// How well a reply route fits the player's line it answers (the chat framework's specificity score, C0 and C2). A route is
/// what chose the reply: "fact level", "knowledge", "shape ask_on", "intent greeting", "not sure"... (the names
/// <c>ChatAuditRoute</c> uses). The scores come from the first blind evaluation (docs/bot-voices/CHAT-EVAL.md): routes
/// whose replies a blind judge scored under 1 of 2 on "answers what was said" are weak; facts, the live world, the
/// conversation thread and the question shapes are specific. Pure.
/// </summary>
public static class AutonomousChatReplyFit
{
    /// <summary>The reply comes from what is true: the bot's own state, the world, the knowledge lookup, a join answer.</summary>
    public const int Known = 4;

    /// <summary>A reply written for this kind of question or statement, or one that follows on from the conversation.</summary>
    public const int Specific = 3;

    /// <summary>A social reply to a social line (hello, thanks, gz, gn, brb, a laugh) or class talk about the class asked.</summary>
    public const int Fits = 2;

    /// <summary>A remark on the line's topic, a stock "not sure", generic agreement: the replies the blind judge marked as ignoring the line.</summary>
    public const int Weak = 1;

    public const int None = 0;

    /// <summary>Intents whose replies answer the line itself (blind round 1: 1.5-2.0 of 2 on "answers").</summary>
    private static readonly HashSet<string> SocialIntents = new(StringComparer.Ordinal)
    {
        "greeting", "farewell", "thanks", "congrats", "ding", "back", "away", "rez", "alarm", "laugh", "abuse", "botcheck", "roleplay",
    };

    /// <summary>Question shapes whose reply sets scored under 1 of 2 in blind round 1 (a price asked back, "whichever", a random tip).</summary>
    private static readonly HashSet<string> WeakShapes = new(StringComparer.Ordinal) { "ask_price", "ask_choice", "ask_advice" };

    /// <summary>The fit of a route; <paramref name="incoming"/> (the player's line) refines a few of them.</summary>
    public static int Of(string route, string incoming = null)
    {
        if (string.IsNullOrEmpty(route))
            return None;
        if (route.StartsWith("none", StringComparison.Ordinal) || route.StartsWith("silent", StringComparison.Ordinal))
            return None;
        // Beta 1.1.2, second pass: the budget's own short replies (a tell's content, a reaction, a "dunno").
        if (route.StartsWith("short", StringComparison.Ordinal) || route.StartsWith("rescue", StringComparison.Ordinal))
            return Fits;
        if (route.StartsWith("ack", StringComparison.Ordinal))
            return Weak;
        if (route == "legacy")
            return Fits;
        if (route.StartsWith("fact", StringComparison.Ordinal) || route.StartsWith("live", StringComparison.Ordinal) ||
            route.StartsWith("knowledge", StringComparison.Ordinal) || route.StartsWith("context", StringComparison.Ordinal) ||
            route is "join" or "tell invited")
            return Known;
        // RC6 on the owner's PC: a friend's answer to "what u up to?" (what it is really doing) scored as weak, and the budget
        // swapped it for "not sure, try /advice".
        if (route.StartsWith("friend chat", StringComparison.Ordinal))
            return Known;
        if (route.StartsWith("thread", StringComparison.Ordinal))
            // "which wing?" and "...?" checks drew stock lines that didn't answer them (blind round 1: 0 of 2).
            return route is "thread followup_which" or "thread followup_confirm" ? Weak : Specific;
        if (route is "group warning" || route.StartsWith("news", StringComparison.Ordinal))
            return Specific;
        if (route.StartsWith("memory", StringComparison.Ordinal))
            return Fits;
        if (route.StartsWith("shape ", StringComparison.Ordinal))
            return WeakShapes.Contains(route[6..]) ? Weak : Specific;
        if (route.Contains("not sure", StringComparison.Ordinal) || route == "fallback")
            return Weak;
        if (route.StartsWith("intent ", StringComparison.Ordinal))
        {
            string intent = route[7..];
            if (SocialIntents.Contains(intent) || intent is "classtalk asked" or "banter laugh")
                return Fits;
            // A group forming ("lfm vendo need healer") is answered by a bot that fits it ("{level} {class} here, inv?").
            if (intent == "grouping" && FormsAGroup(incoming))
                return Fits;
            return Weak;
        }
        return Weak;
    }

    /// <summary>"lfm vendo need healer pst", "lf1m tank", "need a healer for barrows": a group looking for members. Pure.</summary>
    public static bool FormsAGroup(string incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return false;
        string[] words = AutonomousChatIntentModel.Words(incoming);
        return AutonomousChatShortTalk.HasPhrase(words, "lfm", "lf1m", "lf2m", "lf3m", "lf4m", "looking for more", "need a healer", "need healer",
            "need a tank", "need tank", "need a puller", "need puller", "need cc", "need a mezzer", "room for", "spots open", "spot open", "need 1",
            "need one more", "need 2", "lf healer", "lf tank", "lf puller", "lf cc", "lf caster", "lf more");
    }
}
