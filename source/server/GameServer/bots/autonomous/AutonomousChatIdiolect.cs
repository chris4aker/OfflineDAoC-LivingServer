using System;
using System.Text;

namespace DOL.GS;

/// <summary>
/// The style layer (Beta 1.1.2): small typing habits fixed from a bot's id, applied to every corpus line it says after its
/// writing style (AutonomousBotVoiceCorpus.Style), so two bots with the same line still type it differently. A bot may write
/// "u" and "r", double its question marks, trail off with "...", tag a "lol" on, or, rarely, type 2003 leetspeak ("teh",
/// "ne1", "l8r"). Only lowercase chat words change: names, places and capitalised words are left as they are, and a tidy
/// writer keeps its tidy text. Pure: the same bot and draw give the same result.
/// </summary>
public static class AutonomousChatIdiolect
{
    [Flags]
    public enum Habit { None = 0, ShortWords = 1, DoubleQuestion = 2, TrailOff = 4, LolTail = 8, Leet = 16 }

    /// <summary>A bot's habits, from its id (about a third of bots have one or more).</summary>
    public static Habit HabitsOf(long botId)
    {
        ulong roll = AutonomousBotPersonality.Mix(botId, 61);
        Habit habits = Habit.None;
        if (roll % 100 < 14) habits |= Habit.ShortWords;
        if (roll / 100 % 100 < 8) habits |= Habit.DoubleQuestion;
        if (roll / 10_000 % 100 < 7) habits |= Habit.TrailOff;
        if (roll / 1_000_000 % 100 < 6) habits |= Habit.LolTail;
        if (roll / 100_000_000 % 100 < 3) habits |= Habit.Leet | Habit.ShortWords;
        return habits;
    }

    /// <summary>Applies a bot's habits to a line it is about to say. <paramref name="tidy"/>: a careful writer, left alone.</summary>
    /// <paramref name="terse"/>: a short functional line ("lfg 38 sm"), which takes no "lol" tail and no trailing "...".
    public static string Apply(string text, long botId, bool tidy, Random random, bool terse = false)
    {
        if (string.IsNullOrEmpty(text) || tidy || botId <= 0)
            return text;
        Habit habits = HabitsOf(botId);
        if (habits == Habit.None)
            return text;
        string result = text;
        if (habits.HasFlag(Habit.ShortWords) || habits.HasFlag(Habit.Leet))
            result = Words(result, habits.HasFlag(Habit.Leet), random);
        string trimmed = result.TrimEnd();
        if (habits.HasFlag(Habit.DoubleQuestion) && trimmed.EndsWith('?') && !trimmed.EndsWith("??") && random.NextDouble() < 0.5)
            result = trimmed + "?";
        // Beta 1.1.2, round 4: no trailing "..." any more (the blind judge's tell); the TrailOff habit is kept for its id draw.
        if (!terse && habits.HasFlag(Habit.LolTail) && random.NextDouble() < 0.15 && !Contains(result, "lol") && !Contains(result, "heh") &&
            !Contains(result, "haha") && result.Split(' ').Length >= 3)
            result = result.TrimEnd('.') + " lol";
        return result.Length <= AutonomousBotVoiceCorpus.MaxLength ? result : text;
    }

    private static bool Contains(string text, string word) =>
        (" " + text.ToLowerInvariant() + " ").Contains(" " + word + " ", StringComparison.Ordinal) ||
        text.EndsWith(word, StringComparison.OrdinalIgnoreCase);

    private static string Words(string text, bool leet, Random random)
    {
        var output = new StringBuilder(text.Length);
        int start = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && (char.IsLetter(text[i]) || text[i] == '\''))
                continue;
            if (i > start)
                output.Append(Swap(text[start..i], leet, random));
            if (i < text.Length)
                output.Append(text[i]);
            start = i + 1;
        }
        return output.ToString();
    }

    /// <summary>One lowercase word in a habit's spelling ("you" -> "u"; with leetspeak "the" -> "teh"), else as written.</summary>
    private static string Swap(string word, bool leet, Random random)
    {
        if (word.Length == 0 || char.IsUpper(word[0]))
            return word;
        string swapped = word switch
        {
            "you" => "u",
            "your" => "ur",
            "you're" or "youre" => "ur",
            "are" => random.NextDouble() < 0.6 ? "r" : word,
            "please" => "pls",
            "thanks" => "thx",
            "because" => "cuz",
            "people" => "ppl",
            _ => word,
        };
        if (!leet || swapped != word)
            return swapped;
        return word switch
        {
            "the" when random.NextDouble() < 0.4 => "teh",
            "anyone" => "ne1",
            "later" => "l8r",
            "to" when random.NextDouble() < 0.3 => "2",
            "for" when random.NextDouble() < 0.3 => "4",
            "woot" => "w00t",
            "great" => "gr8",
            _ => word,
        };
    }
}
