using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS.Tests
{
    /// <summary>
    /// A port of the chat audit's CLUNKY scorer (tools/bot-voices/eval/chat_scorers.py: clunky_issues, is_non_answer), for the
    /// stitching property test (UT_ChatStitching). tools/bot-voices/tests/clunky_cases.json holds cases both run, so the two stay
    /// the same: when the Python scorer changes, change this file the same way.
    /// </summary>
    public static class ChatClunky
    {
        private static readonly Regex Apos = new("[’'`]");

        private static readonly string[] NonAnswerPatterns =
        {
            @"\bnot sure\b", @"\bno idea\b", @"\bno clue\b", @"\bdunno\b", @"\bidk\b", @"\bidunno\b", @"\bcouldnt (tell|say)\b",
            @"\bcant (say|remember|recall)\b", @"\bdont know\b", @"\bdont remember\b", @"\bnever (been|done|tried)\b",
            @"\b(it )?depends\b", @"\bask (around|in|on|a guard|the guards|someone|your trainer|ur trainer|a vet|in advice|/advice)\b",
            @"\btry /advice\b", @"/advice", @"\bgood question\b", @"\bwho knows\b", @"\bbeats me\b", @"\bhard to say\b",
            @"\bsomeone else (would|might|will) know\b", @"\bi forget\b", @"\bforgot\b", @"\bhmm+\b\W*$", @"^\W*(ya|yes|yeah|yep|hm+|huh|what|wat)\W*\?+\W*$",
            @"^\W*\?+\W*$", @"\bwhat do you mean\b", @"\bwhy do you ask\b", @"\bnot my thing\b", @"\bdepends what\b",
        };
        private static readonly Regex[] NonAnswer = NonAnswerPatterns.Select(p => new Regex(p)).ToArray();

        /// <summary>chat_scorers.is_non_answer.</summary>
        public static bool IsNonAnswer(string text)
        {
            string t = Apos.Replace(text ?? string.Empty, string.Empty).ToLowerInvariant().Trim();
            return t.Length > 0 && NonAnswer.Any(p => p.IsMatch(t));
        }

        private static readonly string[] VowelExceptions = { "one", "once", "uni", "use", "usu", "uti", "ure", "uri", "eu", "ewe", "ubi" };
        private static readonly string[] HSilent = { "hour", "honest", "honor", "honour", "heir" };
        private static readonly HashSet<string> NotNoun = new()
        {
            "a", "an", "and", "or", "of", "on", "in", "at", "as", "is", "it", "if", "its", "i", "im", "are", "am", "also", "all", "any",
            "up", "us", "off", "out", "over", "into", "onto", "else", "ever", "each", "either", "even", "oh", "ok", "okay", "until",
        };
        private static readonly HashSet<string> DoubleOk = new()
        {
            "ha", "he", "lol", "no", "very", "so", "go", "ty", "gz", "bye", "yes", "yeah", "yep", "hey", "hi", "that", "had", "k",
            "omg", "wow", "woo", "more", "run", "now", "far", "many", "again", "pull", "kill", "nom", "boom", "knock", "tick", "rr", "pew", "bang",
        };
        private static readonly HashSet<string> Connectors = new() { "and", "but", "or", "the", "a", "an", "because", "if", "than", "my", "your", "their", "our", "very" };
        private static readonly Regex PluralAfterOne = new(@"\b(1|one|a single) (levels|mobs|players|bots|groups|hours|minutes|mins|days|weeks|rps|points|kills|" +
                                                           @"times|pieces|items|camps|keeps|relics|spots|zones)\b");
        private static readonly Regex SingularAfterMany = new(@"\b([2-9]|[1-9]\d+|two|three|four|five|six|several|many|few|couple of|lots of) " +
            @"(level|mob|player|bot|group|hour|minute|day|week|kill|time|piece|item|camp|keep|relic|spot|zone)\b(?=\s*($|[.,!?;]|(ago|left|more|and|so|in|at|for|to go)\b))");
        private static readonly Regex Slot = new(@"\{[a-z_]+\}|\[[a-z_ :]+\]|<[a-z_]+>|\$\{|%s|\bnull\b|\bundefined\b|\bNaN\b");
        private static readonly Regex Punct = new(@"\s[,.;](?![\w.)(])|,,|\.\.,|,\.(?!\.)|\?\s*,|![,.](?!\.)|\s{2,}|^\s*[,;]");

        /// <summary>chat_scorers.PROPER.</summary>
        public static readonly string[] Proper =
        {
            "camelot", "jordheim", "tir na nog", "albion", "midgard", "hibernia", "cotswold", "mularn", "mag mell", "avalon", "emain macha",
            "darkness falls", "legion", "myrkwood", "lyonesse",
        };
        private static readonly Regex LaughTail = new(@"\s(lol|haha+|heh|hehe|lmao|rofl|:p|:d|xd|:\)|;\))\W*$", RegexOptions.IgnoreCase);
        private static readonly Regex Serious = new(@"\b(careful|dont go alone|wipe|wiped|die|dead|died|raid|cant go|careless|trust nothing)\b");
        private static readonly Regex OwnQuestion = new(
            @"^\W*((whats|what s|what is|who is|whos|who holds|where is|wheres|where s|is the|is it|how do|how is|why is)\b[^?]{0,48})\?\s+\S");

        /// <summary>chat_scorers.takes_an.</summary>
        public static bool TakesAn(string word)
        {
            string w = (word ?? string.Empty).ToLowerInvariant().TrimStart('\'', '"', '(');
            if (w.Length == 0)
                return false;
            if (char.IsDigit(w[0]))
            {
                string digits = Regex.Match(w, @"\d+").Value;
                return digits[0] == '8' || digits is "11" or "18";
            }
            if (HSilent.Any(h => w.StartsWith(h, StringComparison.Ordinal)))
                return true;
            if ("aeiou".Contains(w[0]))
                return !VowelExceptions.Any(e => w.StartsWith(e, StringComparison.Ordinal)) && !(w[0] == 'u' && w.Length == 1);
            return false;
        }

        /// <summary>chat_scorers.clunky_issues: (code, detail) for each thing that reads stitched together.</summary>
        public static List<(string Code, string Detail)> Issues(string text)
        {
            var issues = new List<(string, string)>();
            if (string.IsNullOrEmpty(text))
                return issues;
            string raw = text;
            string low = Apos.Replace(raw, string.Empty).ToLowerInvariant();
            string[] words = Regex.Matches(low, @"[a-z0-9%+]+").Select(m => m.Value).ToArray();
            foreach (Match m in Regex.Matches(low, @"(?<![\w-])([a-z]+) \1(?![\w-])"))
                if (!DoubleOk.Contains(m.Groups[1].Value))
                    issues.Add(("doubled_word", m.Value));
            foreach (Match m in Regex.Matches(low, @"\b(a|an) ([a-z0-9][\w%]*)"))
            {
                string art = m.Groups[1].Value, next = m.Groups[2].Value;
                string tail = next.Length > 1 ? next[1..] : "x";
                if (NotNoun.Contains(next) || next.Length <= 3 && !Regex.IsMatch(tail, "[aeiou]") || next.Length == 1)
                    continue;
                if (art == "a" && TakesAn(next))
                    issues.Add(("a_an", "a " + next));
                else if (art == "an" && !TakesAn(next) && !char.IsDigit(next[0]))
                    issues.Add(("a_an", "an " + next));
            }
            foreach (Match m in PluralAfterOne.Matches(low))
                issues.Add(("number_agreement", m.Value));
            foreach (Match m in SingularAfterMany.Matches(low))
                issues.Add(("number_agreement", m.Value));
            foreach (Match m in Slot.Matches(raw))
                issues.Add(("unfilled_slot", m.Value));
            string last = words.Length > 0 ? words[^1] : string.Empty;
            string trimmed = raw.TrimEnd();
            if (Connectors.Contains(last) && !trimmed.EndsWith('?') && !trimmed.EndsWith(')') && words.Length > 2)
                issues.Add(("dangling_connector", last));
            if (Regex.IsMatch(trimmed, @"[,;:]\s*$") || Regex.IsMatch(low, @"\b(and|but|or),"))
                issues.Add(("dangling_connector", trimmed.Length > 12 ? trimmed[^12..] : trimmed));
            foreach (Match m in Punct.Matches(raw))
                issues.Add(("punctuation", m.Value));
            var seen = new HashSet<string>();
            for (int i = 0; i + 4 < words.Length; i++)
            {
                string gram = string.Join(' ', words.Skip(i).Take(5));
                if (seen.Contains(gram) && gram.Split(' ').Distinct().Count() >= 4)
                {
                    issues.Add(("repeated_clause", gram));
                    break;
                }
                seen.Add(gram);
            }
            if (Regex.IsMatch(low, @"^\W*(yes|yep|yeah)\b.*[.,]\s*(no|nope)\s*[,.]") || Regex.IsMatch(low, @"^\W*(no|nope)\b.*[.,]\s*(yes|yep|yeah)\s*[,.]"))
                issues.Add(("contradicts_itself", "yes ... no"));
            Match tense = Regex.Match(low, @"\b(did|didnt|does|doesnt|will|wont|cant|can)( (you|u|i|he|she|they|we|it))? (went|was|came|saw|took|gave|ate)\b");
            if (tense.Success)
                issues.Add(("tense", tense.Value));
            Match laugh = LaughTail.Match(raw);
            if (laugh.Success)
            {
                string body = raw[..laugh.Index].Trim();
                if (IsNonAnswer(body) || body.EndsWith('.') || Regex.IsMatch(body, @"\? \w") || Serious.IsMatch(body.ToLowerInvariant()))
                    issues.Add(("tacked_laugh", laugh.Groups[1].Value));
            }
            Match own = OwnQuestion.Match(low);
            if (own.Success)
                issues.Add(("own_question", own.Groups[1].Value.Trim()));
            if (char.IsUpper(raw[0]))
                foreach (string name in Proper)
                    if (Regex.IsMatch(raw, @"(?<![A-Za-z])" + Regex.Escape(name) + @"(?![a-z])"))
                    {
                        issues.Add(("case_mix", name));
                        break;
                    }
            return issues;
        }

        /// <summary>The issue codes of a text, sorted and once each.</summary>
        public static string[] Codes(string text) => Issues(text).Select(i => i.Code).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
    }
}
