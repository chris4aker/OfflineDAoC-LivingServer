using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Realism 2a (issue #478; docs/realism-audit-1.2.3/structure.md, channels.md findings 3 and 4, questions.md findings 2, 6 and 7,
/// lines.md finding 2): <b>no non-answer as a whole answer, in any channel.</b> A line whose act is a non-answer never goes out as
/// the whole reply to a line said to the bot or asked of its channel. The class is defined once, here, and read by every path:
/// the reply planner settles its pick with <see cref="Settle"/> (the bot answers from what it knows, asks a real question back,
/// or stays quiet), and the gate's addressee step holds whatever still comes through any exit (<see cref="WholeAnswer"/>).
/// <para>
/// The kinds (<see cref="NonAnswerKind"/>): a "dunno" ("no idea", "not sure tbh", "havent camped them"), a "try elsewhere" ("try
/// /advice", "ask around"), an echo word ("where?" to a line that is no warning, "?", "ya?"), a bare laugh to a line that doesn't
/// laugh, and stock agreement ("true", "same", "k", "gotcha") where a question wanted its answer. A line that holds a fact beside
/// its hedge ("not sure, maybe 20ish", "ask your trainer at 50, long chain") is an answer, not a non-answer. Pure.
/// </para>
/// </summary>
public static partial class BotSayGate
{
    /// <summary>What kind of non-answer a reply is, as the whole reply to the line it answers.</summary>
    public enum NonAnswerKind { None, Dunno, Elsewhere, Echo, Laugh, Agreement }

    // The words a deflection is made of: once they and the filler are gone, nothing is left of a non-answer.
    private static readonly Regex Deflection = new(
        @"\b(dunno|idunno|idk|no idea|not sure|no clue|not a clue|couldnt (tell|say)( you| ya| u)?|cant (say|tell|help|remember|recall)( you| ya| u| there| with that)?|" +
        @"dont know|dont remember|beats me|who knows|good question|hard to say|wish i knew|i forget|forgot|the name escapes me|" +
        @"never (been|done|tried|got|camped|did|seen|heard)( it| them| that| there| of it)?|havent (camped|been|seen|tried|done|heard)( it| them| that| there| of it)?|" +
        @"not my thing|depends|it depends|depends what|no comment|long story|who can say)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex Elsewhere = new(
        @"(\btry /?advice\b|/advice|\bask (around|in advice|on advice|a guard|the guards|someone|somebody|a trainer|ur trainer|your trainer|a vet|in /?advice|the realm|in faction)\b|" +
        @"\btry asking( a| the| in)?( guard| guards| trainer| merchant| someone| around| advice| /advice)?\b|\bsomeone (else )?(would|might|will|should) know\b|\bsomeone usually answers\b|\bcheck (the )?(wiki|forums|/advice)\b|\bgoogle it\b)",
        RegexOptions.CultureInvariant);

    // What a deflection is padded with ("not sure tbh", "dunno sry m8", "no idea, ask around maybe").
    private static readonly HashSet<string> Pad = new(StringComparer.Ordinal)
    {
        "tbh", "sry", "sorry", "srry", "soz", "mate", "m8", "dude", "man", "bro", "try", "there", "about", "that", "this", "one", "exactly", "atm", "rn",
        "tho", "though", "myself", "either", "really", "honestly", "maybe", "prob", "probably", "lol", "heh", "haha", "hmm", "hm", "hmmm", "uh", "um",
        "eh", "ah", "oh", "well", "so", "but", "and", "or", "i", "im", "me", "you", "u", "ya", "it", "them", "yet", "to", "the", "a", "an", "on", "of",
        "for", "with", "just", "still", "either", "sure", "ever", "here", "too", "gl", "good", "luck", "though", "now", "right", "is", "its", "be",
        "d", "p", "xd", "ok", "k", "yeah", "yea", "ye", "nope", "no", "nah", "idea", "clue", "asking", "ask",
    };

    private static readonly HashSet<string> LaughWords = new(StringComparer.Ordinal)
    {
        "lol", "lolol", "lmao", "rofl", "haha", "hahaha", "hehe", "heh", "ha", "hah", "lul", "xd", "kek", "d", "p",
    };

    // Stock agreement that agrees with nothing asked ("true", "same", "lol yeah"): never the whole answer to a question.
    private static readonly HashSet<string> AgreeWords = new(StringComparer.Ordinal)
    {
        "true", "same", "agreed", "indeed", "fair", "truth", "this", "exactly", "ikr", "word", "facts", "yup", "ya", "yeah", "yea", "yep", "so", "lol",
        "heh", "haha", "too", "enough", "here", "totally", "+1", "^",
    };

    // A plain acknowledgement ("k", "gotcha", "oh ok"): an answer to a yes-or-no ask or an order, never to what / where / how.
    private static readonly HashSet<string> AckWords = new(StringComparer.Ordinal)
    {
        "k", "kk", "ok", "okay", "oki", "gotcha", "cool", "nice", "oh", "ah", "ty", "ya", "yeah", "yea", "yep", "yup", "sure", "right", "alright",
        "aight", "mhm", "mm", "hmm", "hm", "noted", "roger", "lol", "heh",
    };

    // An echo: a word thrown back ("where?", "which way", "?", "ya?", "hm?", "what?") instead of an answer.
    private static readonly Regex EchoShape = new(
        @"^\W*(\?+|(where|where at|which way|what|wat|huh|hm+|wut|who|why|how|when|which|really|srsly|for real)\W*|(ya|yes|yeah|yep|eh|sup|yo)\W*\?+\W*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex WhAsk = new(
        @"^\W*((hi|hey|yo|so|ok|and|um|hmm|sry|sorry)\W+)*(what|whats|wat|wut|where|wheres|which|who|whos|why|how|hows|when|whens|wich)\b",
        RegexOptions.CultureInvariant);

    private static string[] WordsOf(string text) =>
        Regex.Split(AutonomousChatQuestions.Normalize(text ?? string.Empty).Replace("/advice", " advice "), @"\s+").Where(word => word.Length > 0).ToArray();

    /// <summary>Lower case with apostrophes dropped and "/advice" kept (Normalize drops the slash).</summary>
    private static string Plain(string text) =>
        Regex.Replace((text ?? string.Empty).ToLowerInvariant().Replace("'", string.Empty).Replace("’", string.Empty), @"[^a-z0-9/ ]", " ").Trim();

    /// <summary>What is left of a reply once its deflection, its "try elsewhere" and its padding are gone: empty for a non-answer.</summary>
    private static string[] PastDeflection(string reply)
    {
        string t = " " + Plain(reply) + " ";
        t = Elsewhere.Replace(t, " ");
        t = Deflection.Replace(t, " ");
        return Regex.Split(t, @"\s+").Where(word => word.Length > 0 && !Pad.Contains(word) && !LaughWords.Contains(word)).ToArray();
    }

    /// <summary>Whether a line asks what, where, how, which, who, why or when (not a yes or no). Pure.</summary>
    public static bool AsksWh(string line) => !string.IsNullOrWhiteSpace(line) && WhAsk.IsMatch(Plain(line));

    /// <summary>
    /// The kind of non-answer <paramref name="reply"/> is as the whole reply to <paramref name="answered"/> (null: the line it answers
    /// is not known, and only the kinds that are never an answer count: a dunno, a "try elsewhere", a bare "?"). Pure.
    /// </summary>
    public static NonAnswerKind KindOf(string reply, string answered = null)
    {
        if (string.IsNullOrWhiteSpace(reply))
            return NonAnswerKind.None;
        string plain = Plain(reply);
        bool deflects = Deflection.IsMatch(" " + plain + " ");
        bool elsewhere = Elsewhere.IsMatch(" " + plain + " ");
        if ((deflects || elsewhere) && PastDeflection(reply).Length == 0)
            return elsewhere && !deflects ? NonAnswerKind.Elsewhere : NonAnswerKind.Dunno;
        if (Regex.IsMatch(reply, @"^\W*\?+\W*$"))
            return NonAnswerKind.Echo;
        if (answered == null)
            return NonAnswerKind.None;
        string[] words = WordsOf(reply);
        if (words.Length == 0)
            return NonAnswerKind.None;
        // A bare laugh: fair only to a line that laughs or jokes itself.
        if (words.All(LaughWords.Contains))
            return AutonomousChatMeetup.Laughs(answered) || AutonomousChatIntentModel.Predict(answered) == eAutonomousChatIntent.Laugh
                ? NonAnswerKind.None : NonAnswerKind.Laugh;
        bool asks = AutonomousChatReplyBudget.AsksSomething(answered);
        // An echo word: "where?" is a real question back only to a warning that doesn't say where ("adds!!", "inc!").
        // A word of the question said back is no echo when it is the answer ("/anon" to "how do i go anon?"); only the bare question
        // words are.
        if (EchoShape.IsMatch(reply.Trim().ToLowerInvariant()))
        {
            if (Regex.IsMatch(plain, @"^\W*(where|where at|which way)\W*$") && AutonomousChatReplyBudget.IsAlarm(answered, AutonomousChatIntentModel.Predict(answered)) &&
                !asks)
                return NonAnswerKind.None;
            // "?" or "ya?" to "Brannoc" or "u there?": the player still has to say what they want; an echo all the same.
            return NonAnswerKind.Echo;
        }
        if (!asks)
            return NonAnswerKind.None;
        // Stock agreement to a question: "true", "same", "lol yeah" answer nothing asked; "k", "gotcha", "ya" answer a yes-or-no or
        // an order, never what / where / how.
        if (words.Length <= 4 && words.All(AgreeWords.Contains) && words.Any(word => word is "true" or "same" or "agreed" or "indeed" or "fair" or "ikr" or
                "exactly" or "word" or "facts" or "+1" or "totally"))
            return NonAnswerKind.Agreement;
        if (words.Length <= 3 && words.All(AckWords.Contains) && AsksWh(answered))
            return NonAnswerKind.Agreement;
        return NonAnswerKind.None;
    }

    private static readonly Regex OpensDunno = new(
        @"^\W*(dunno|idk|no idea|not sure|no clue|hmm dunno|never (got|camped|did) it|havent (camped|been|seen))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether a line opens on a dunno ("dunno, check lfg", "idk, not me"). It is no non-answer when something follows the hedge,
    /// but a bot answering another bot from a pool of lines doesn't open on one (1.2.2's F4, and field talk's rule before #478):
    /// <see cref="AutonomousChatFieldTalk.IsNonAnswer"/> leaves such a line unsaid too. Without it, 18 sim evenings carried 176
    /// "dunno, ..." lines in /faction instead of 4 (the evening ratchet, PR #614). Pure.
    /// </summary>
    public static bool OpensWithDunno(string line) => !string.IsNullOrWhiteSpace(line) && OpensDunno.IsMatch(line);

    /// <summary>Whether <paramref name="reply"/> is a non-answer as the whole reply to <paramref name="answered"/>. Pure.</summary>
    public static bool IsNonAnswer(string reply, string answered = null) => KindOf(reply, answered) != NonAnswerKind.None;

    /// <summary>
    /// Whether a line is owed an answer: it asks something, or it was said to the bot (a tell to it, a line naming it, its group's
    /// line). A remark said to nobody is owed none, and a reaction to it is no non-answer. Pure.
    /// </summary>
    public static bool Owed(string answered, bool saidToBot) =>
        !string.IsNullOrWhiteSpace(answered) && (saidToBot || AutonomousChatReplyBudget.AsksSomething(answered));

    /// <summary>
    /// The reply planner's last step, the gate's rule applied where the reply is chosen: <paramref name="reply"/> as it is when it
    /// is no non-answer, or the line it answers is owed none; else the answer from what the bot knows (<paramref name="known"/>:
    /// its class, level, place and plans), or null (the bot stays quiet). Never a non-answer. Pure but for <paramref name="known"/>.
    /// </summary>
    public static string Settle(string reply, string answered, bool saidToBot, Func<string> known = null)
    {
        if (reply == null || !Owed(answered, saidToBot) || !IsNonAnswer(reply, answered))
            return reply;
        string instead = known?.Invoke();
        return string.IsNullOrWhiteSpace(instead) || IsNonAnswer(instead, answered) ? null : instead;
    }

    // ---- The gate's check: what a line answers, in its room --------------------------------------------------------

    /// <summary>A line counts as the one a reply answers when it is the newest in the room, by someone else, this recent.</summary>
    public const long AnswerWindowMs = 45_000;

    private readonly record struct Said(long Seq, long At, string Speaker, string Text, string To, bool ByPlayer, string Channel);

    private static readonly Dictionary<string, Said> LastInRoom = new(StringComparer.OrdinalIgnoreCase);

    private static string RoomKey(string channel, eRealm realm, string room, string a, string b) => channel switch
    {
        "tell" => "tell:" + string.Join("|", new[] { a ?? "", b ?? "" }.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)),
        "faction" or "lfg" or "trade" or "advice" => channel + ":" + (int)realm,
        _ => channel + ":" + (room ?? ""),
    };

    private static void NoteInRoom(string key, Said said)
    {
        if (string.IsNullOrWhiteSpace(said.Text))
            return;
        lock (Recent)
            LastInRoom[key] = said;
    }

    /// <summary>
    /// The gate's rule (the addressee step): a line whose act is a non-answer, said as the whole reply to the newest line in its room
    /// when that line was said to the speaker or asked of the channel, is held. Its own lines, lines older than
    /// <see cref="AnswerWindowMs"/> and remarks said to nobody are not answered by it.
    /// </summary>
    private static Verdict WholeAnswer(Utterance line)
    {
        if (string.IsNullOrWhiteSpace(line.Text))
            return Verdict.Pass;
        string answered = line.Answers;
        bool saidToBot = line.SaidToSpeaker;
        if (answered == null)
        {
            Said last;
            string key = RoomKey(line.Channel, line.Realm, RoomOf(line), line.Speaker, line.To);
            lock (Recent)
                if (!LastInRoom.TryGetValue(key, out last))
                    return Verdict.Pass;
            if (string.Equals(last.Speaker, line.Speaker, StringComparison.OrdinalIgnoreCase) || Now() - last.At > AnswerWindowMs)
                return Verdict.Pass;
            answered = last.Text;
            saidToBot = line.Channel == "tell" || line.Channel == "group" && last.ByPlayer ||
                        string.Equals(last.To, line.Speaker, StringComparison.OrdinalIgnoreCase) || NamesSpeaker(answered, line.Speaker);
        }
        if (!Owed(answered, saidToBot))
            return Verdict.Pass;
        NonAnswerKind kind = KindOf(line.Text, answered);
        return kind == NonAnswerKind.None ? Verdict.Pass
            : new Verdict(true, "non-answer", $"{kind.ToString().ToLowerInvariant()} as the whole answer to \"{answered}\"");
    }

    private static bool NamesSpeaker(string text, string speaker)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(speaker))
            return false;
        string first = speaker.Split(' ')[0];
        return first.Length >= 3 && Regex.IsMatch(text, @"(?<![A-Za-z])" + Regex.Escape(first) + @"(?![A-Za-z])", RegexOptions.IgnoreCase);
    }
}
