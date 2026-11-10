using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS
{
    /// <summary>
    /// A debate line as its speaker types it in /faction (1.1.3, after RC5). The scenes are written as two- and three-sentence
    /// turns, and a tidy writer's style capitalised every sentence and closed it with a full stop, so beside "lol rip" a debater
    /// read like a book: "People said the same before the dragon lairs. One winter they were just there, all three realms, same
    /// week." Every debate beat now goes through this pass:
    /// <list type="bullet">
    /// <item>a terse writer takes the shortest way the scene gives to say the line;</item>
    /// <item>a line of more than <see cref="MaxWords"/> words goes out as two or three chat lines, split between its sentences
    /// (or at a comma near the middle of one long sentence), the way players hit enter mid-thought;</item>
    /// <item>each piece is styled by the speaker's own writing style and typing habits (AutonomousChatIdiolect), but no style
    /// capitalises a second sentence inside one chat line or ends it with a full stop: a tidy writer starts each line with a
    /// capital and leaves the rest as typed.</item>
    /// </list>
    /// Only how a line is typed changes, never what it says. Pure but for the random draw.
    /// </summary>
    public static class AutonomousChatDebateSpeech
    {
        /// <summary>The most words one chat line of a debate carries when it can be split (a terse writer: <see cref="TerseMaxWords"/>).</summary>
        public const int MaxWords = 14, TerseMaxWords = 10;

        /// <summary>The most chat lines one beat becomes.</summary>
        public const int MaxPieces = 3;

        /// <summary>Whether a style is the terse one (strips punctuation, abbreviates).</summary>
        public static bool IsTerse(VC.Voice voice) => voice != null && voice.StripPunctuation;

        /// <summary>The variant a speaker says: a terse writer the shortest, anyone else one at random. Pure for a given draw.</summary>
        public static string Variant(IReadOnlyList<string> variants, VC.Voice voice, Random random)
        {
            if (variants == null || variants.Count == 0)
                return null;
            if (IsTerse(voice))
                return variants.OrderBy(v => Words(v)).ThenBy(v => v.Length).First();
            return variants[random.Next(variants.Count)];
        }

        /// <summary>The words in a text.</summary>
        public static int Words(string text) => string.IsNullOrWhiteSpace(text) ? 0 : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        /// <summary>
        /// A line in pieces of at most <paramref name="maxWords"/> words where it can be split: between sentences first, and a
        /// single sentence still too long at its comma nearest the middle. At most <see cref="MaxPieces"/> pieces. Pure.
        /// </summary>
        public static List<string> Split(string text, int maxWords = MaxWords)
        {
            var pieces = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return pieces;
            string line = text.Trim();
            if (Words(line) <= maxWords)
            {
                pieces.Add(line);
                return pieces;
            }
            var sentences = new List<string>();
            foreach (string sentence in Regex.Split(line, @"(?<=[.!?])\s+").Where(s => s.Length > 0))
                sentences.AddRange(AtComma(sentence, maxWords));
            string current = null;
            foreach (string sentence in sentences)
            {
                if (current == null)
                    current = sentence;
                else if (Words(current) + Words(sentence) <= maxWords)
                    current += " " + sentence;
                else
                {
                    pieces.Add(current);
                    current = sentence;
                }
            }
            if (current != null)
                pieces.Add(current);
            // A short tail goes back with the piece before it (no "lol" on its own line), and never more than three pieces.
            while (pieces.Count > 1 && (Words(pieces[^1]) < 3 || pieces.Count > MaxPieces))
            {
                pieces[^2] = pieces[^2] + " " + pieces[^1];
                pieces.RemoveAt(pieces.Count - 1);
            }
            return pieces;
        }

        /// <summary>A sentence over <paramref name="maxWords"/> words split at its comma nearest the middle, and again while a half is too long.</summary>
        private static IEnumerable<string> AtComma(string sentence, int maxWords)
        {
            if (Words(sentence) <= maxWords)
                return [sentence];
            int middle = sentence.Length / 2, best = -1;
            for (int i = sentence.IndexOf(", ", StringComparison.Ordinal); i >= 0; i = sentence.IndexOf(", ", i + 1, StringComparison.Ordinal))
            {
                bool fits = Words(sentence[..i]) >= 4 && Words(sentence[(i + 2)..]) >= 4;
                if (fits && (best < 0 || Math.Abs(i - middle) < Math.Abs(best - middle)))
                    best = i;
            }
            return best < 0 ? [sentence] : AtComma(sentence[..best], maxWords).Concat(AtComma(sentence[(best + 2)..], maxWords));
        }

        /// <summary>
        /// A debate line as the speaker types it: its pieces (one chat line each), split and styled as described above. With no
        /// style (voices off) the line is split only. Pure for a given draw.
        /// </summary>
        public static List<string> Typed(string line, VC.Voice voice, long botId, Random random)
        {
            random ??= Random.Shared;
            bool terse = IsTerse(voice);
            var typed = new List<string>();
            foreach (string piece in Split(line, terse ? TerseMaxWords : MaxWords))
            {
                string text = piece;
                if (voice != null)
                {
                    // A tidy writer's capital starts the chat line; no second sentence is capitalised and no full stop ends it.
                    text = VC.Style(text, voice with { CapitalizeSentences = false, EndPeriod = false }, random);
                    text = AutonomousChatIdiolect.Apply(text, botId, voice.Capitalize, random);
                }
                text = Chat(text, terse);
                if (text.Length > 0)
                    typed.Add(text);
            }
            return typed;
        }

        /// <summary>
        /// A chat line's punctuation as typed: no full stop at its end, and a terse writer's sentences run together. The scenes
        /// are written in lowercase, so with no style capitalising sentences a capital inside a line is a name, and it stays. Pure.
        /// </summary>
        public static string Chat(string text, bool terse)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;
            string result = text.Trim();
            if (terse)
                result = Regex.Replace(result, @"(?<!\.)\.(?!\.) ", " ");
            if (result.EndsWith('.') && !result.EndsWith("..", StringComparison.Ordinal))
                result = result[..^1];
            return result;
        }
    }
}
