using System;
using System.Collections.Generic;
using System.Text;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Keeps filler out of the bots' chat (Beta 1.1.3): near-copies of a line a channel just carried, and the kinds of line the
/// first blind evaluation marked as the bot tell. Pure rules plus a small ring of recent lines per channel moment.
/// </summary>
public static class AutonomousChatFiller
{
    /// <summary>Whether an ambient corpus line may be said in this channel now.</summary>
    public static bool AmbientAllowed(VC.Line line, bool realmWide, Random random) => line != null;

    #region Near-duplicates

    private static readonly HashSet<string> Interjections = new(StringComparer.Ordinal)
    {
        "ha", "haha", "hah", "heh", "hehe", "lol", "lmao", "rofl", "yes", "yep", "yeah", "ya", "yea", "yup", "oh", "hmm", "hm", "ah", "ok", "k", "kk",
        "so", "well", "wow", "man", "dude", "honestly", "tbh", "imo", "hey", "hi", "yo", "and", "but", "just", "really", "pretty", "very",
    };

    /// <summary>
    /// A line's skeleton for spotting near-copies: lower case, letters and digits only, numbers as "#", smileys and
    /// leading or trailing interjections dropped ("Ha yes, this exactly." and "Heh yep, this exactly." are both "this
    /// exactly"). Pure.
    /// </summary>
    public static string Skeleton(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var words = new List<string>(12);
        var word = new StringBuilder(12);
        void Flush()
        {
            if (word.Length > 0)
                words.Add(word.ToString());
            word.Clear();
        }
        foreach (char c in text.ToLowerInvariant())
        {
            if (char.IsLetter(c))
                word.Append(c);
            else if (char.IsDigit(c))
            {
                if (word.Length == 0 || word[^1] != '#')
                    word.Append('#');
            }
            else if (c != '\'')
                Flush();
        }
        Flush();
        int start = 0, end = words.Count;
        while (start < end && Interjections.Contains(words[start]))
            start++;
        while (end > start && Interjections.Contains(words[end - 1]))
            end--;
        // A line made only of interjections ("lol", "heh yeah") keeps its words: short reactions may repeat.
        return start >= end ? string.Join(' ', words) : string.Join(' ', words.GetRange(start, end - start));
    }

    /// <summary>
    /// The last lines each channel moment carried, by skeleton, for half an hour: a near-copy of one of them is not said
    /// again there. Reactions of one or two words ("lol", "gz", "omw") are exempt, as players repeat those all the time.
    /// </summary>
    public sealed class RecentTexts
    {
        public const int PerChannel = 48;
        public const long KeepMs = 30 * 60_000;

        private sealed class Ring
        {
            public readonly string[] Skeletons = new string[PerChannel];
            public readonly long[] At = new long[PerChannel];
            public int Next;
        }

        private readonly Dictionary<long, Ring> _rings = new();

        /// <summary>True (and remembered) unless a near-copy was said in this channel moment in the last half hour.</summary>
        public bool TryAdd(long channel, string text, long now)
        {
            string skeleton = Skeleton(text);
            if (skeleton.Length == 0)
                return true;
            bool reaction = skeleton.IndexOf(' ') < 0 || skeleton.Split(' ').Length <= 2 && skeleton.Length <= 10;
            lock (_rings)
            {
                if (!_rings.TryGetValue(channel, out Ring ring))
                {
                    if (_rings.Count > 4096)
                        _rings.Clear();
                    _rings[channel] = ring = new Ring();
                }
                if (!reaction)
                    for (int i = 0; i < PerChannel; i++)
                        if (ring.Skeletons[i] == skeleton && now - ring.At[i] < KeepMs && ring.At[i] <= now)
                            return false;
                ring.Skeletons[ring.Next] = skeleton;
                ring.At[ring.Next] = now;
                ring.Next = (ring.Next + 1) % PerChannel;
                return true;
            }
        }

        public void Clear()
        {
            lock (_rings)
                _rings.Clear();
        }
    }

    /// <summary>The server's recent lines.</summary>
    public static readonly RecentTexts Recent = new();

    #endregion
}
