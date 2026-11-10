using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Ex = DOL.GS.AutonomousChatExchange;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Speech = DOL.GS.AutonomousChatDebateSpeech;
using Spoken = DOL.GS.RealmEventSpoken;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// 1.1.3, after RC5 (the owner's PC, 2026-11-09): chat that reads like players typed it. A realm notice in a bot's mouth said as a
    /// player would ("krondon raid forming at diogel, need 50s", not "Krondon: recruiting up to 120 level-50 adventurers..."), event
    /// reactions that don't come back over an evening, debate turns typed in the speaker's own style (no narrator's paragraph), a
    /// frontier death told many ways, and a deed that names where it happened. These are tests on the code and the corpus, with a
    /// simulated evening written for source/tools/chat_audit.py; none of it is an in-game check.
    /// </summary>
    [TestFixture]
    public class UT_ChatVoiceAfterRC5
    {
        private static VC _corpus;
        private static VC Corpus => _corpus ??= Load();

        private static VC Load()
        {
            var assembly = typeof(VC).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        }

        // ---- 1. realm notices in a bot's mouth ------------------------------------------------------------------------------

        /// <summary>The source folder of the game server, or null when the tests run without the sources.</summary>
        private static string AutonomousFolder()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string folder = Path.Combine(dir.FullName, "GameServer", "bots", "autonomous");
                if (File.Exists(Path.Combine(folder, "RealmEventNotices.cs")))
                    return folder;
            }
            return null;
        }

        /// <summary>The interpolated strings ($"...") in a piece of C#, with each {expression}.</summary>
        private static IEnumerable<List<(bool Hole, string Text)>> Interpolated(string code)
        {
            for (int i = code.IndexOf("$\"", StringComparison.Ordinal); i >= 0; i = code.IndexOf("$\"", i + 1, StringComparison.Ordinal))
            {
                var parts = new List<(bool, string)>();
                var text = new StringBuilder();
                int depth = 0, k = i + 2;
                for (; k < code.Length; k++)
                {
                    char c = code[k];
                    if (depth == 0 && c == '"')
                        break;
                    if (depth == 0 && c == '{')
                    {
                        parts.Add((false, text.ToString()));
                        text.Clear();
                        depth = 1;
                        continue;
                    }
                    if (depth > 0)
                    {
                        if (c == '"')
                        {
                            int close = code.IndexOf('"', k + 1);
                            text.Append(code, k, close - k + 1);
                            k = close;
                            continue;
                        }
                        if (c == '{')
                            depth++;
                        if (c == '}' && --depth == 0)
                        {
                            parts.Add((true, text.ToString()));
                            text.Clear();
                            continue;
                        }
                    }
                    text.Append(c);
                }
                parts.Add((false, text.ToString()));
                yield return parts;
                i = k;
            }
        }

        /// <summary>A sample value for a notice's {expression}: a number, a raid, a hub, a keep, a realm, a relic.</summary>
        private static string Sample(string expression) => expression switch
        {
            _ when expression.Contains("RallyLine") => "A defense force has been rallied!",
            _ when expression.Contains("startCondition") => "At least 200 must arrive before the dungeon assault.",
            _ when expression.Contains("GuildName") => "Epic Special Forces",
            _ when Regex.IsMatch(expression, "present|joined|Sum|RecruitCap|Required") => "93",
            _ when expression.Contains("Hub") => "Caer Diogel",
            _ when expression.Contains("RaidName") || expression.Contains("Definition.Name") || expression.Contains("definition.Name") => "Krondon",
            _ when expression.Contains("RealmToName") || expression == "loser" => "Midgard",
            _ when expression.Contains("CarrierTargets") || expression == "name" => "Scabbard of Excalibur",
            _ when Regex.IsMatch(expression, "present|joined|Sum|RecruitCap|Required") => "93",
            _ when expression.Contains("boss") => "Orylle",
            _ => "Caer Benowyc",
        };

        /// <summary>
        /// Every written notice the server queues: the literal ones read from the source (each RealmEventNotices.Queue call),
        /// filled with sample names, and every RealmEventBanter text.
        /// </summary>
        private static List<string> AllNotices()
        {
            var notices = new List<string>();
            string folder = AutonomousFolder();
            if (folder != null)
                foreach (string file in Directory.GetFiles(folder, "*.cs"))
                {
                    // Windows checkouts have CRLF line ends.
                    string code = File.ReadAllText(file).Replace("\r\n", "\n");
                    for (int at = code.IndexOf("RealmEventNotices.Queue(", StringComparison.Ordinal); at >= 0;
                         at = code.IndexOf("RealmEventNotices.Queue(", at + 1, StringComparison.Ordinal))
                    {
                        int end = code.IndexOf(";\n", at, StringComparison.Ordinal);
                        if (end < 0)
                            end = code.Length;
                        foreach (var parts in Interpolated(code[at..end]))
                            notices.Add(string.Concat(parts.Select(p => p.Hole ? Sample(p.Text) : p.Text)));
                    }
                }
            foreach (string raid in new[] { "Krondon", "Summoner's Hall", "Golestandt" })
            {
                for (int i = 0; i < 8; i++)
                {
                    notices.Add(RealmEventBanter.RaidReminder(raid, i % 2 == 0));
                    foreach (string outcome in new[] { "Boss defeated", "Timed out", "Beaten to it", "Defeated", "Failed rally", "Stopped by player", "?" })
                        notices.Add(RealmEventBanter.RaidOutcome(raid, outcome));
                    notices.Add(RealmEventBanter.RaidWipe(raid, raid));
                    notices.Add(RealmEventBanter.RaidWipe(raid, "Orylle"));
                }
            }
            foreach (bool defender in new[] { true, false })
            {
                notices.Add(RealmEventBanter.SiegeReminder("Caer Benowyc", defender));
                foreach (eRealm winner in new[] { eRealm.None, eRealm.Albion, eRealm.Midgard })
                    foreach ((bool timedOut, bool failed) in new[] { (false, false), (true, false), (false, true) })
                        notices.Add(RealmEventBanter.SiegeOutcome("Caer Benowyc", defender, timedOut, failed, winner, eRealm.Albion));
            }
            return notices.Distinct().ToList();
        }

        /// <summary>The PC lines (RC3-RC5) read as what they are.</summary>
        [TestCase("Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel; at least 93 must arrive before assault.", "raid_recruit", "Krondon")]
        [TestCase("The Krondon expedition is advancing with 200 staged level-50 adventurers.", "raid_advance", "Krondon")]
        [TestCase("Fighting has broken out at Dun Crimthain. There is an opportunity to intervene.", "keep_others_fighting", null)]
        [TestCase("Warbands are gathering to assault Nottmoor Faste. Join them at the siege camp", "siege_gather", null)]
        [TestCase("93 adventurers present at Caer Diogel; departing together for Krondon. Late arrivals will join the expedition directly.", "raid_depart", "Krondon")]
        public void ThePcNoticesAreReadForWhatTheyAre(string notice, string kind, string raid)
        {
            Spoken.Reading reading = Spoken.Read(notice);
            Assert.That(reading, Is.Not.Null);
            Assert.That(reading.Kind, Is.EqualTo(kind));
            if (raid != null)
                Assert.That(reading.Raid, Is.EqualTo(raid));
        }

        /// <summary>
        /// Every notice the server can queue is known, and what a bot says for it is a player's line: no herald phrase, no
        /// semicolon, lowercase, ten words at most, no head count ("120 ... at least 93"), and the place named short.
        /// </summary>
        [Test]
        public void EveryNoticeABotSaysReadsLikeAPlayer()
        {
            List<string> notices = AllNotices();
            if (AutonomousFolder() != null)
                Assert.That(notices.Count, Is.GreaterThan(40), "the notices were read from the source");
            var random = new Random(11);
            // Most written notices are caught by the checker's patterns (a sanity check of the patterns, not a rule for notices).
            Assert.That(notices.Count(Spoken.SystemVoice), Is.GreaterThan(notices.Count / 2));
            foreach (string notice in notices)
            {
                Assert.That(Spoken.Read(notice), Is.Not.Null, "a notice no rule knows: " + notice);
                var said = new HashSet<string>();
                for (int i = 0; i < 40; i++)
                {
                    string line = Spoken.Say(notice, RealmEventNotices.KeepSlots("Caer Benowyc", eRealm.Midgard), random);
                    said.Add(line);
                    Assert.That(Spoken.SystemVoice(line), Is.False, $"{notice} -> {line}");
                    Assert.That(line, Is.EqualTo(line.ToLowerInvariant()), line);
                    Assert.That(line, Does.Not.Contain(";").And.Not.Contain("level-50").And.Not.Contain("adventurers"));
                    Assert.That(Regex.IsMatch(line, @"\b(93|120|200)\b"), Is.False, "no head count: " + line);
                    Assert.That(Speech.Words(line), Is.LessThanOrEqualTo(10), line);
                    Assert.That(line, Does.Not.Contain("caer benowyc").And.Not.Contain("caer diogel"), "names said short: " + line);
                }
                Assert.That(said.Count, Is.GreaterThanOrEqualTo(3), "several ways to say: " + notice);
            }
        }

        [Test]
        public void TheRc5RaidNoticeIsSaidTheWayChatSaidIt()
        {
            var random = new Random(3);
            string notice = "Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel; at least 93 must arrive before assault.";
            for (int i = 0; i < 30; i++)
            {
                string line = Spoken.Say(notice, AutonomousRealmRaid.RaidSlots("Krondon", null), random);
                Assert.That(line, Does.Contain("krondon"));
                Assert.That(line, Does.Contain("diogel"));
            }
            Assert.That(Spoken.ShortHub("Snowdonia Station", random), Is.EqualTo("snowdonia"));
            Assert.That(Spoken.ShortHub("Dalniver's service settlement (World's End)", random), Is.EqualTo("worlds end"));
            Assert.That(Spoken.ShortHub("Camelot Hills camp by the Darkness Falls entrance", random), Is.EqualTo("df entrance"));
            Assert.That(Spoken.ShortRaid("Summoner's Hall", random), Is.AnyOf("sh", "summoners hall", "summoners"));
        }

        [Test]
        public void ANoticeNoRuleKnowsIsStillTypedChat()
        {
            string line = Spoken.Plain("The ancient gates of Somewhere are opening; 40 heroes are needed!");
            Assert.That(line, Is.EqualTo("the ancient gates of somewhere are opening"));
            Assert.That(Spoken.SystemVoice(line), Is.False);
        }

        /// <summary>A news line's names as players typed them: "we lost Dun Ailinne to Hibernia" -> "we lost ailinne to hibs".</summary>
        [Test]
        public void NewsSlotsAreTheNamesChatUsed()
        {
            var random = new Random(5);
            var slots = new Dictionary<string, string>
            {
                ["keep"] = "Dun Ailinne", ["enemy"] = "Hibernia", ["winner"] = "Midgard", ["loser"] = "Albion", ["raid"] = "Summoner's Hall",
                ["boss"] = "Grand Summoner Govannon", ["zone"] = "The Black Mountains", ["relic"] = "The Scabbard of Excalibur", ["size"] = "12",
            };
            for (int i = 0; i < 20; i++)
            {
                Dictionary<string, string> chat = Spoken.ChatSlots(slots, random);
                Assert.That(chat["keep"], Is.AnyOf("ailinne", "aili", "ailin"));
                Assert.That(chat["enemy"], Is.AnyOf("hib", "hibs"));
                Assert.That(chat["winner"], Is.AnyOf("mid", "mids"));
                Assert.That(chat["loser"], Is.AnyOf("alb", "albs"));
                Assert.That(chat["raid"], Is.AnyOf("sh", "summoners hall", "summoners"));
                Assert.That(chat["boss"], Is.EqualTo("govannon"));
                Assert.That(chat["zone"], Does.StartWith("black m"));
                Assert.That(chat["relic"], Is.EqualTo("scabbard of excalibur"));
                Assert.That(chat["size"], Is.EqualTo("12"));
            }
        }

        // ---- 2. reactions over an evening ---------------------------------------------------------------------------------

        [Test]
        public void ALongerReactionRestsForTheEveningAndAShortOneKeepsTheTenMinuteRule()
        {
            var evening = new AutonomousChatBursts.EveningMemory();
            const string lane = "test:evening";
            long t = 10 * 60_000;
            Assert.That(evening.TryUse(lane, "ugh, that one again", t), Is.True);
            Assert.That(evening.Fresh(lane, "ugh that one again", t + 2 * 3_600_000L), Is.False, "the RC3 and RC5 line, two hours later");
            Assert.That(evening.TryUse(lane, "oh well not my keep", t), Is.True);
            Assert.That(evening.TryUse(lane, "oh well, not my keep", t + 90 * 60_000L), Is.False);
            Assert.That(evening.Fresh(lane, "ugh that one again", t + AutonomousChatBursts.EveningMemory.WindowMs), Is.True, "the next evening");
            Assert.That(evening.TryUse(lane, "again??", t), Is.True);
            Assert.That(evening.TryUse(lane, "again??", t + 1_000), Is.True, "two words: the ten-minute window's business, not the evening's");
            Assert.That(evening.Fresh("test:other", "ugh that one again", t + 1_000), Is.True, "per channel");
        }

        /// <summary>One burst of reactions to a piece of news, as AutonomousChatBursts.React picks them: capped, each from the
        /// topic's replies then any news, none heard in the burst, the ten-minute window, and (after) the evening's memory.</summary>
        private static List<string> Burst(string topic, int size, long now, DateTime clock, Ex.ShortWindow shorts, AutonomousChatBursts.EveningMemory evening,
            VC.RecentLines recent, string lane, Random random, int seed)
        {
            var heard = new HashSet<string>();
            var said = new List<string>();
            for (int i = 0; i < Math.Min(size, 3); i++)
            {
                var speaker = new VC.Speaker(5_000 + seed * 7 + i, "Simbot" + i, eRealm.Albion, "Cleric", 50, BotPersonalityType.Leveler,
                    new HashSet<string>(StringComparer.Ordinal));
                string text = null;
                foreach (string answers in new[] { "news_" + topic, "news" })
                    if (Corpus.TryPick(new VC.Request("reply", speaker, new Dictionary<string, string>(), recent, clock, random)
                        {
                            Answers = answers, RelaxTo = AutonomousBotVoices.ShortReplyRest, LocalHour = 20,
                            Filter = line => !heard.Contains(AutonomousChatFiller.Skeleton(line.Text)) && shorts.Fresh(lane, line.Text, now) &&
                                             (evening == null || evening.Fresh(lane, line.Text, now)),
                        }, out VC.Picked picked))
                    {
                        if (heard.Add(AutonomousChatFiller.Skeleton(picked.Text)) && shorts.TryUse(lane, picked.Text, now) &&
                            (evening == null || evening.TryUse(lane, picked.Text, now)))
                            text = picked.Text;
                        break;
                    }
                if (text == null)
                    break;
                said.Add(text);
            }
            return said;
        }

        /// <summary>
        /// An evening of keep news (a keep lost or won, a siege held or failed, about every eight minutes, for three hours) and
        /// its bursts: with the evening's memory no reaction of three words or more is heard twice, and the bursts stay full.
        /// </summary>
        [Test]
        public void ReactionsToTheSameKindOfEventVaryOverAnEvening()
        {
            string[] topics = ["keep_lost", "keep_won", "keep_lost", "siege_held", "keep_lost", "keep_other", "siege_failed", "keep_lost"];
            (int Repeats, int Lines, int Bursts, int Thin) Play(bool withEvening)
            {
                var random = new Random(9);
                var shorts = new Ex.ShortWindow();
                var evening = withEvening ? new AutonomousChatBursts.EveningMemory() : null;
                var recent = new VC.RecentLines();
                var last = new Dictionary<string, long>();
                int repeats = 0, lines = 0, bursts = 0, thin = 0;
                DateTime start = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
                for (long now = 0, k = 0; now < 3 * 3_600_000L; now += 8 * 60_000, k++)
                {
                    List<string> said = Burst(topics[k % topics.Length], 3, now, start.AddMilliseconds(now), shorts, evening, recent, "sim", random, (int)k);
                    bursts++;
                    thin += said.Count < 2 ? 1 : 0;
                    foreach (string text in said)
                    {
                        lines++;
                        string key = AutonomousChatFiller.Skeleton(text);
                        if (AutonomousChatBursts.EveningMemory.Covers(text) && last.TryGetValue(key, out long at) && now - at < 3 * 3_600_000L)
                            repeats++;
                        last[key] = now;
                    }
                }
                return (repeats, lines, bursts, thin);
            }
            var before = Play(false);
            var after = Play(true);
            TestContext.Progress.WriteLine($"CHAT_VOICE reactions before: {before}; after: {after}");
            Assert.That(after.Repeats, Is.Zero, "no 3+ word reaction twice in the evening");
            Assert.That(after.Thin, Is.LessThanOrEqualTo(after.Bursts / 10), "the bursts don't run dry");
            Assert.That(after.Lines, Is.GreaterThanOrEqualTo(before.Lines * 9 / 10));
        }

        [Test]
        public void EachBurstTopicHasAWidePool()
        {
            foreach (string topic in new[] { "keep_won", "keep_lost", "keep_other", "siege_held", "siege_failed", "relic_lost", "relic_won", "relic_back",
                         "raid_won", "raid_wiped", "df_won", "df_lost" })
                Assert.That(Corpus.Lines.Count(line => line.Kind == "reply" && line.Answers.Contains("news_" + topic)), Is.GreaterThanOrEqualTo(15), topic);
        }

        // ---- 3. debates typed in the speaker's own style -----------------------------------------------------------------

        [Test]
        public void TheRc5DebateLinesAreTypedNotNarrated()
        {
            VC.Voice proper = Corpus.Voices.Single(v => v.Name == "proper");
            VC.Voice terse = Corpus.Voices.Single(v => v.Name == "terse");
            var random = new Random(1);
            List<string> pieces = Speech.Typed("people said the same before the dragon lairs. one winter they were just there, all three realms, same week",
                proper, 0, random);
            Assert.That(pieces, Is.EqualTo(new[] { "People said the same before the dragon lairs", "One winter they were just there, all three realms, same week" }));
            pieces = Speech.Typed("then tunnels under every frontier the next summer, new lands across the water that autumn, three islands last autumn",
                proper, 0, random);
            Assert.That(pieces.Count, Is.EqualTo(2), string.Join(" / ", pieces));
            Assert.That(pieces.All(p => !p.EndsWith('.')));
            Assert.That(Speech.Variant(["you say that. i remember the first winter, when three dragons turned up in one week and nobody had a map for any of them",
                "people said the same before the dragon lairs. one winter they were just there, all three realms, same week"], terse, random),
                Does.StartWith("people said"), "a terse writer says it the shorter way");
            pieces = Speech.Typed("people said the same before the dragon lairs. one winter they were just there, all three realms, same week", terse, 0, random);
            Assert.That(pieces.All(p => !p.Contains(',') && !p.Contains(". ") && Speech.Words(p) <= Speech.TerseMaxWords), string.Join(" / ", pieces));
        }

        [TestCase("a short line stays one line", 1)]
        [TestCase("maybe. or maybe it opens for someone who isnt pushing", 1)]
        [TestCase("one two three four five six seven eight. nine ten eleven twelve thirteen fourteen fifteen sixteen", 2)]
        [TestCase("one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen", 1)]
        [TestCase("one two three four five six seven eight nine, ten eleven twelve thirteen fourteen fifteen sixteen", 2)]
        [TestCase("one two three four five six seven eight nine ten eleven twelve. ok", 1)]
        public void ALongTurnIsSplitWherePlayersHitEnter(string text, int pieces) =>
            Assert.That(Speech.Split(text).Count, Is.EqualTo(pieces), string.Join(" / ", Speech.Split(text)));

        private static readonly Dictionary<string, string> SceneSlots = new(StringComparer.Ordinal)
        {
            ["q"] = "", ["name_a"] = "Brannoc", ["name_b"] = "Ysolde", ["player"] = "Manawatch", ["keep"] = "Caer Benowyc", ["enemy"] = "mids",
            ["capital"] = "Camelot", ["camp"] = "the bandit camp", ["level"] = "24", ["zone"] = "Black Mountains South", ["frontier"] = "Hadrian's Wall",
            ["class"] = "cleric", ["dungeon"] = "Tomb of Mithra", ["item"] = "Bloodied Sword", ["realm"] = "Albion", ["monster"] = "bandit",
            ["monsters"] = "bandits",
        };

        /// <summary>
        /// The rule for every scene line, in every writing style: no chat line of a debate is longer than 14 words (10 for a terse
        /// writer) unless it is one sentence with no comma to split it at; none ends with a full stop; none capitalises a second
        /// sentence the scene didn't; a terse writer's line has no comma and no full stop inside; and none reads as a herald's
        /// (chat_audit.py "lines in system voice": no formally capitalised paragraph of more than 15 words).
        /// </summary>
        [Test]
        public void EveryDebateLineIsTypedInTheSpeakersStyle()
        {
            var random = new Random(4);
            int lines = 0, split = 0, unsplittable = 0;
            foreach (VC.Scene scene in Corpus.Scenes)
                foreach (VC.Beat beat in scene.Beats.Where(b => !b.IsPause))
                    foreach (string variant in beat.Variants)
                    {
                        if (!VC.TryFill(variant, SceneSlots, out string filled))
                            continue;
                        foreach (VC.Voice voice in Corpus.Voices)
                        {
                            List<string> pieces = Speech.Typed(filled, voice, 1_000 + lines % 97, random);
                            lines++;
                            split += pieces.Count > 1 ? 1 : 0;
                            int max = Speech.IsTerse(voice) ? Speech.TerseMaxWords : Speech.MaxWords;
                            // Only a beat with no words ("...") can vanish, for a writer who drops punctuation.
                            if (pieces.Count == 0)
                            {
                                Assert.That(filled.Any(char.IsLetterOrDigit), Is.False, filled);
                                continue;
                            }
                            Assert.That(pieces.Count, Is.LessThanOrEqualTo(Speech.MaxPieces));
                            foreach (string piece in pieces)
                            {
                                string where = $"{scene.Id} ({voice.Name}): {filled} -> {piece}";
                                if (Speech.Words(piece) > max + 2)
                                {
                                    // Only one long sentence with nowhere to split it, or the last of three pieces; still within the
                                    // corpus's line length.
                                    unsplittable++;
                                    // (a writer's "lol" or smiley at the end is not part of the sentence)
                                    string sentence = Regex.Replace(piece, @" (lol|heh|haha|:\)|:P|:D)$", string.Empty);
                                    Assert.That(Speech.Split(sentence, max).Count == 1 || pieces.Count == Speech.MaxPieces && piece == pieces[^1], Is.True, where);
                                    Assert.That(piece.Length, Is.LessThanOrEqualTo(VC.MaxLength), where);
                                }
                                Assert.That(piece.EndsWith('.') && !piece.EndsWith("..", StringComparison.Ordinal), Is.False, where);
                                foreach (Match m in Regex.Matches(piece, @"[.!?] ([A-Z][a-z']*)"))
                                    if (m.Groups[1].Value != "I" && !m.Groups[1].Value.StartsWith("I'", StringComparison.Ordinal))
                                        Assert.That(filled, Does.Contain(m.Groups[1].Value), "a capital the scene didn't write: " + where);
                                if (Speech.IsTerse(voice))
                                    Assert.That(piece.Contains(',') || Regex.IsMatch(piece, @"(?<!\.)\. "), Is.False, where);
                                Assert.That(Spoken.SystemVoice(piece), Is.False, where);
                            }
                        }
                    }
            TestContext.Progress.WriteLine($"CHAT_VOICE debates: {lines} lines typed, {split} split, {unsplittable} long single sentences");
            Assert.That(lines, Is.GreaterThan(10_000));
            Assert.That(unsplittable, Is.LessThan(lines / 20), "long unsplittable sentences are rare");
        }

        // ---- 4. frontier death stories --------------------------------------------------------------------------------

        private static readonly Field.WorldView Frontier = new(["Caer Benowyc", "Caer Berkstead"], [], ["Caer Renaris"], ["Midgard"], 0, 1, "Forest Sauvage");

        private static Field.Facts Facts(long id, string zone, bool rvr) =>
            new(id, eRealm.Albion, "Scout", 50, zone, null, rvr, false, false, false, 1, false, false, false, false, null, null, 0, false, null, 3, 20, false,
                Frontier);

        [Test]
        public void AFrontierDeathHasWidePools()
        {
            var random = new Random(2);
            var openers = new HashSet<string>();
            for (int i = 0; i < 200; i++)
                openers.UnionWith(Field.RvrDeathOpeners("emain", random));
            Assert.That(openers.Count, Is.GreaterThan(300), "the opener has hundreds of ways");
            Assert.That(Field.RvrDeathPlaces("emain", random).Distinct().Count(), Is.GreaterThanOrEqualTo(10));
            Assert.That(Field.RvrDeathWho(random).Distinct().Count(), Is.GreaterThanOrEqualTo(10));
            Assert.That(Field.RvrDeathAfter("emain", random).Distinct().Count(), Is.GreaterThanOrEqualTo(15));
            foreach (string line in openers.Concat(Field.RvrDeathAfter("emain", random)).Concat(Field.RvrDeathPlaces("emain", random)))
                Assert.That(line, Does.Not.Contain("camp").And.Not.Contain("pulled").And.Not.Contain("tower"), line);
        }

        /// <summary>
        /// Forty frontier deaths in an evening (one every four and a half minutes, RC5's rate and more): the teller never says a
        /// line twice in the hour (RC5: "got ganked lol" twice in 13 minutes), and a third speaker's "same happened to me in ..."
        /// names a frontier zone (RC5: "ganked in jamtland mtns" drew "same happened to me in dales of devwy").
        /// </summary>
        [Test]
        public void AnEveningOfFrontierDeathsDoesNotRepeat()
        {
            var random = new Random(8);
            var memory = new Field.Memory();
            var shorts = new Ex.ShortWindow();
            var told = new Dictionary<string, long>();
            int stories = 0;
            string[] frontier = ["Emain Macha", "Odin's Gate", "Hadrian's Wall"];
            string[] hunting = ["Dales of Devwy", "Lyonesse", "Cornwall"];
            for (long now = 0; now < 3 * 3_600_000L; now += 270_000)
            {
                Field.Facts teller = Facts(stories, frontier[stories % 3], true);
                bool thirdOut = stories % 2 == 0;
                Field.Facts third = Facts(stories + 2, thirdOut ? frontier[(stories + 1) % 3] : hunting[stories % 3], thirdOut);
                long at = now;
                List<Field.Step> story = Field.Story("death", teller, Facts(stories + 1, "Lyonesse", false), third, random, memory,
                    text => shorts.Fresh("sim", text, at));
                story = AutonomousBotChatCoordinator.KeepFreshShorts(story, text => shorts.TryUse("sim", text, at));
                stories++;
                if (story.Count == 0)
                    continue;
                foreach (Field.Step step in story.Where(s => s.Who == 0))
                {
                    string key = Ex.Normalize(step.Text);
                    Assert.That(told.TryGetValue(key, out long when) && now - when < 60 * 60_000L, Is.False, "told again within the hour: " + step.Text);
                    told[key] = now;
                }
                foreach (Field.Step step in story.Where(s => s.Who == 2 && s.Text.StartsWith("same happened to me in ", StringComparison.Ordinal)))
                {
                    string zone = step.Text["same happened to me in ".Length..].TrimEnd('.', '!', ' ');
                    Assert.That(hunting.Any(h => h.ToLowerInvariant().StartsWith(zone, StringComparison.Ordinal)), Is.False, "a hunting zone: " + step.Text);
                }
            }
            Assert.That(told.Count, Is.GreaterThan(60), "most stories were told");
        }

        // ---- 5. deeds name the place ------------------------------------------------------------------------------------

        [Test]
        public void AWitnessedDeedNamesItsPlaceButNowAndThen()
        {
            Assert.That(AutonomousPlayerDeeds.NamesPlace("named_solo", true, 0.5), Is.True);
            Assert.That(AutonomousPlayerDeeds.NamesPlace("named_solo", true, AutonomousPlayerDeeds.NoPlaceShare - 0.01), Is.False, "now and then");
            Assert.That(AutonomousPlayerDeeds.NamesPlace("named_solo", false, 0.5), Is.False, "no place known");
            Assert.That(AutonomousPlayerDeeds.NamesPlace("rep_named", true, 0.5), Is.False, "a reputation repeated, not seen");
            Assert.That(AutonomousPlayerDeeds.NamesPlace("nickname", true, 0.5), Is.False);
            Dictionary<string, string> said = AutonomousPlayerDeeds.ChatSlots(new Dictionary<string, string>
                { ["place"] = "The Black Mountains", ["target"] = "Grimwold", ["player"] = "Manawatch" }, new Random(1));
            Assert.That(said["place"], Does.StartWith("black m"));
            Assert.That(said["target"], Is.EqualTo("Grimwold"), "the real monster, by name");

            // The RC5 line's family now names the place; a deed with its place known says it in most lines.
            var speaker = new VC.Speaker(77, "Simwitness", eRealm.Albion, "Cleric", 50, BotPersonalityType.Leveler, new HashSet<string>(StringComparer.Ordinal));
            var slots = new Dictionary<string, string>(said) { ["player_class"] = "wizard" };
            var random = new Random(6);
            int named = 0, total = 0;
            for (int i = 0; i < 200; i++)
            {
                bool wantPlace = AutonomousPlayerDeeds.NamesPlace("named_solo", true, random.NextDouble());
                VC.Picked picked = null;
                bool ok = wantPlace && Corpus.TryPick(new VC.Request("deed", speaker, slots, null, DateTime.UtcNow, random)
                    { Topic = "named_solo", Filter = line => line.Text.Contains("{place}", StringComparison.Ordinal) }, out picked) ||
                    Corpus.TryPick(new VC.Request("deed", speaker, slots, null, DateTime.UtcNow, random) { Topic = "named_solo" }, out picked);
                Assert.That(ok, Is.True);
                Assert.That(picked.Text, Does.Contain("Grimwold"));
                Assert.That(picked.Text, Does.Not.Contain("a named monster"));
                total++;
                named += picked.Text.Contains(said["place"]) ? 1 : 0;
            }
            Assert.That(named, Is.GreaterThan(total * 3 / 4), $"{named} of {total} named the place");
            Assert.That(Corpus.Lines.Where(l => l.Kind == "deed" && l.Topic == "named_solo" && l.Text.StartsWith("not lying, i just saw", StringComparison.Ordinal))
                .All(l => l.Text.Contains("{place}") || l.Text.EndsWith("alone", StringComparison.Ordinal)), Is.True);
        }

        // ---- 6. an evening, simulated, for chat_audit.py ------------------------------------------------------------------

        /// <summary>
        /// A simulated evening of /faction (three hours, synthetic bots): realm notices said by bots, keep news with its bursts,
        /// debate scenes from the corpus and frontier death stories, the old way (the notice word for word in the bot's style, a
        /// beat in one line in the writer's style, reactions without the evening's memory) and the new. Written as
        /// faction-sim-voice-before/after.chat.log next to UT_FactionCoherence's, so CI audits them with chat_audit.py.
        /// </summary>
        private static List<string> SimulateEvening(bool fixedCode, out int systemVoice, out int longerRepeats)
        {
            var random = new Random(21);
            var log = new List<string>();
            var shorts = new Ex.ShortWindow();
            var evening = new AutonomousChatBursts.EveningMemory();
            var recent = new VC.RecentLines();
            var memory = new Field.Memory();
            List<string> notices = AllNotices().Where(n => !n.StartsWith("At least", StringComparison.Ordinal)).ToList();
            DateTime start = new(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc);
            long ex = 0, lastLine = -10_000;
            int sys = 0, repeats = 0;
            var lastReaction = new Dictionary<string, long>();
            double ceiling = AutonomousChatBudget.Band(AutonomousChatBudget.Current, AutonomousChatBudget.Channel.Faction, 594, 20, false, 1.0).Ceiling;
            var held = new List<long>();
            // Lines come at most one every three seconds (the simulated channel's spacing), and a line the ceiling holds (all but
            // an event's) waits for room, as a debate beat does.
            void Line(long at, string speaker, long id, string src, long exchange, int index, string role, string text)
            {
                long t = Math.Max(at, lastLine + 3_000);
                if (src != "event")
                {
                    while (!Ex.Fits(held.Count(h => h > t - 120_000 && h <= t) / 2.0, ceiling))
                        t += 3_000;
                    held.Add(t);
                }
                lastLine = t;
                long clock = 20 * 3_600_000L + t;
                if (Spoken.SystemVoice(text))
                    sys++;
                if (role is "reactor" && AutonomousChatBursts.EveningMemory.Covers(text))
                {
                    string key = AutonomousChatFiller.Skeleton(text);
                    if (lastReaction.TryGetValue(key, out long before) && t - before < 3_600_000L)
                        repeats++;
                    lastReaction[key] = t;
                }
                log.Add($"11/09 {clock / 3_600_000 % 24:00}:{clock / 60_000 % 60:00}:{clock / 1_000 % 60:00} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot " +
                        $"ch=faction realm=Albion from=\"{speaker}\" via=Sim src={src} ex={exchange} i={index} role={role} cap={ceiling.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} text=\"{text.Replace('"', '\'')}\"");
            }
            string Name(long id) => "Simvoice" + (char)('a' + id % 26) + (char)('a' + id / 26 % 26);
            VC.Voice VoiceOf(long id) => Corpus.VoiceFor(id);
            string Styled(long id, string text) => AutonomousChatIdiolect.Apply(VC.Style(text, VoiceOf(id), random), id, VoiceOf(id).Capitalize, random);
            string[] newsTopics = ["keep_lost", "keep_won", "siege_held", "keep_lost", "keep_other", "siege_failed"];
            int k = 0;
            for (long now = 0; now < 3 * 3_600_000L; now += 60_000, k++)
            {
                if (now < lastLine)
                    continue;
                long bot = 7_000 + random.Next(160);
                if (k % 9 == 0)
                {
                    // A realm notice no news line told: the herald's text, said by a bot.
                    string notice = notices[random.Next(notices.Count)];
                    string said = fixedCode ? Styled(bot, Spoken.Say(notice, RealmEventNotices.KeepSlots("Caer Benowyc", eRealm.Midgard), random)) : Styled(bot, notice);
                    Line(now, Name(bot), bot, "event", ++ex, 0, "herald", said);
                }
                else if (k % 9 == 4)
                {
                    // Keep news and its burst (at most three reactions).
                    string topic = newsTopics[k / 9 % newsTopics.Length];
                    var slots = new Dictionary<string, string> { ["keep"] = "Caer Benowyc", ["enemy"] = "Midgard", ["winner"] = "Midgard", ["loser"] = "Hibernia" };
                    var speaker = new VC.Speaker(bot, Name(bot), eRealm.Albion, "Cleric", 50, BotPersonalityType.Leveler, new HashSet<string>(StringComparer.Ordinal));
                    if (!Corpus.TryPick(new VC.Request("news", speaker, fixedCode ? Spoken.ChatSlots(slots, random) : slots, recent, start.AddMilliseconds(now), random)
                            { Topic = topic, LocalHour = 20 }, out VC.Picked news))
                        continue;
                    long id = ++ex;
                    Line(now, Name(bot), bot, "event", id, 0, "teller", Styled(bot, news.Text));
                    List<string> burst = Burst(topic, 3, now, start.AddMilliseconds(now), shorts, fixedCode ? evening : null, recent, "sim", random, k);
                    for (int i = 0; i < burst.Count; i++)
                        Line(lastLine + 2_000, Name(bot + 1 + i), bot + 1 + i, "event", id, i + 1, "reactor", burst[i]);
                }
                else if (k % 30 == 15)
                {
                    // A debate scene, six to eight beats.
                    VC.Scene scene = Corpus.Scenes[random.Next(Corpus.Scenes.Length)];
                    long id = ++ex;
                    var cast = new Dictionary<string, long>();
                    int beatIndex = 0;
                    foreach (VC.Beat beat in scene.Beats.Where(b => !b.IsPause).Take(6))
                    {
                        if (!cast.TryGetValue(beat.Role, out long who))
                            cast[beat.Role] = who = 7_200 + random.Next(160);
                        string[] filled = beat.Variants.Select(v => VC.TryFill(v, SceneSlots, out string f) ? f : null).Where(f => f != null).ToArray();
                        if (filled.Length == 0)
                            continue;
                        VC.Voice voice = VoiceOf(who);
                        List<string> pieces = fixedCode
                            ? Speech.Typed(Speech.Variant(filled, voice, random), voice, who, random)
                            : [VC.Style(filled[random.Next(filled.Length)], voice, random)];
                        foreach (string piece in pieces)
                            Line(lastLine + (piece == pieces[0] ? 8_000 : 4_000), Name(who), who, "debate", id, beatIndex, beat.Role, piece);
                        beatIndex++;
                    }
                }
                else if (k % 5 == 2)
                {
                    // A frontier death story.
                    long at = now;
                    List<Field.Step> story = Field.Story("death", Facts(bot, "Emain Macha", true), Facts(bot + 1, "Lyonesse", false),
                        Facts(bot + 2, "Cornwall", false), random, memory, text => shorts.Fresh("sim", text, at));
                    story = AutonomousBotChatCoordinator.KeepFreshShorts(story, text => shorts.TryUse("sim", text, at));
                    long id = ++ex;
                    int index = 0;
                    foreach (Field.Step step in story)
                        Line(lastLine + 2_500, Name(bot + step.Who), bot + step.Who, "story", id, index++, step.Who == 0 ? "teller" : "riff", step.Text);
                }
            }
            systemVoice = sys;
            longerRepeats = repeats;
            return log;
        }

        [Test]
        public void AnEveningReadsLikePlayersTyped()
        {
            List<string> before = SimulateEvening(false, out int beforeSystem, out int beforeRepeats);
            List<string> after = SimulateEvening(true, out int afterSystem, out int afterRepeats);
            string folder = Environment.GetEnvironmentVariable("CHAT_SIM_DIR") is { Length: > 0 } dir ? dir : Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-sim");
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder, "faction-sim-voice-before.chat.log"), before);
            File.WriteAllLines(Path.Combine(folder, "faction-sim-voice-after.chat.log"), after);
            TestContext.Progress.WriteLine($"CHAT_VOICE evening before: {before.Count} lines, {beforeSystem} in system voice, {beforeRepeats} longer reactions again within the hour");
            TestContext.Progress.WriteLine($"CHAT_VOICE evening after: {after.Count} lines, {afterSystem} in system voice, {afterRepeats} longer reactions again within the hour");
            Assert.That(beforeSystem, Is.GreaterThan(0), "the simulation reproduces the RC5 lines");
            Assert.That(afterSystem, Is.Zero);
            Assert.That(afterRepeats, Is.Zero);
            Assert.That(after.Count, Is.GreaterThan(200));
        }
    }
}
