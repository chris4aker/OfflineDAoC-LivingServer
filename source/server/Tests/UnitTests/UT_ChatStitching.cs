using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Chat audit 1.2.2, phase 2 (docs/bot-voices/CHAT-AUDIT-1.2.2.md, fixes F8-F10 and the stitching pass): replies put together
    /// from pieces at run time (a rescue's "dunno", a reaction, a request's answer, "sorry, was fighting." before a reply, the
    /// asker's name before an answer, a friend's small talk, a frontier death, a lore answer) are run, every piece with every
    /// other, through every writing style, a style that always laughs and the typing habits, and then through the scorer's
    /// CLUNKY checks (ChatClunky, a port of tools/bot-voices/eval/chat_scorers.py). Tests only; none of it is seen in game.
    /// </summary>
    [TestFixture]
    public class UT_ChatStitching
    {
        private static VC _corpus;
        private static VC Corpus => _corpus ??= VC.Parse(Resource("chat.corpus.json"), Resource("chat.voices.json"));

        private static string Resource(string suffix)
        {
            var assembly = typeof(VC).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string RepoFile(params string[] parts)
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        /// <summary>Every writing style, and the chatty one laughing every time.</summary>
        private static IEnumerable<VC.Voice> Voices()
        {
            foreach (VC.Voice voice in Corpus.Voices)
                yield return voice with { TypoChance = 0 };
            yield return Corpus.Voices.Single(v => v.Name == "chatty") with { EmoteChance = 1.0, TypoChance = 0 };
        }

        /// <summary>Bot ids with no habit and with each typing habit (the "lol" tail among them).</summary>
        private static long[] _botIds;
        private static long[] BotIds() => _botIds ??= FindBotIds();

        private static long[] FindBotIds()
        {
            var ids = new List<long> { 0 };
            foreach (AutonomousChatIdiolect.Habit habit in new[] { AutonomousChatIdiolect.Habit.ShortWords, AutonomousChatIdiolect.Habit.DoubleQuestion,
                         AutonomousChatIdiolect.Habit.LolTail, AutonomousChatIdiolect.Habit.Leet })
            {
                long id = Enumerable.Range(1, 20_000).Select(i => (long)i).FirstOrDefault(i => AutonomousChatIdiolect.HabitsOf(i).HasFlag(habit));
                if (id > 0)
                    ids.Add(id);
            }
            return ids.ToArray();
        }

        /// <summary>A text in every style, by every habit, over a few draws.</summary>
        private static IEnumerable<string> Styled(string text, int draws = 4)
        {
            foreach (VC.Voice voice in Voices())
                foreach (long id in BotIds())
                    for (int seed = 0; seed < draws; seed++)
                        yield return AutonomousBotVoices.StyleText(voice, id, text, new Random(seed));
        }

        private static void Clean(IEnumerable<string> texts, string what, params string[] only)
        {
            var bad = new List<string>();
            foreach (string text in texts.Distinct())
            {
                string[] codes = ChatClunky.Codes(text).Where(code => only.Length == 0 || only.Contains(code)).ToArray();
                if (codes.Length > 0)
                    bad.Add($"{string.Join(",", codes)}: {text}");
            }
            Assert.That(bad, Is.Empty, what + ":\n" + string.Join("\n", bad.Take(30)));
        }

        // ---- the scorer port ------------------------------------------------------------------------------------------

        [Test]
        public void TheClunkyPortMatchesThePythonScorer()
        {
            string file = RepoFile("tools", "bot-voices", "tests", "clunky_cases.json");
            if (file == null)
                Assert.Ignore("tools/bot-voices is not beside the tests");
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            int count = 0;
            foreach (var item in json.RootElement.EnumerateArray())
            {
                string text = item[0].GetString();
                string[] expected = item[1].EnumerateArray().Select(e => e.GetString()).ToArray();
                Assert.That(ChatClunky.Codes(text), Is.EqualTo(expected), text);
                count++;
            }
            Assert.That(count, Is.GreaterThan(40));
        }

        // ---- F9: no laugh on a dunno, a warning, a death or a lore line --------------------------------------------------

        [TestCase("no clue, sry")]
        [TestCase("not sure tbh")]
        [TestCase("hmm dunno")]
        [TestCase("thats a full raid, dont go alone")]
        [TestCase("careful, the adds wipe groups there")]
        [TestCase("died to the guards again")]
        [TestCase("prydwen is in albion, we cant go there")]
        [TestCase("bog of cullen? take a horse from a stable master")]
        [TestCase("i dont craft, ask around")]
        public void ASeriousLineTakesNoLaugh(string line)
        {
            Assert.That(VC.IsSerious(line), Is.True);
            Assert.That(VC.TakesEmote(null, line), Is.False);
            foreach (string styled in Styled(line, 8))
                Assert.That(ChatClunky.Codes(styled), Has.No.Member("tacked_laugh"), styled);
        }

        [Test]
        public void AnOrdinaryLineStillLaughsAndNoStopComesBeforeTheLaugh()
        {
            VC.Voice laughing = Corpus.Voices.Single(v => v.Name == "chatty") with { EmoteChance = 1.0 };
            Assert.That(VC.TakesEmote(null, "long walk back to the camp"), Is.True);
            Assert.That(VC.HasLaugh(VC.Style("long walk back to the camp", laughing, new Random(1))), Is.True);
            string stopped = VC.Style("good luck out there.", laughing, new Random(1));
            Assert.That(stopped, Does.Not.Contain(". "), stopped);
            Assert.That(ChatClunky.Codes(stopped), Is.Empty, stopped);
        }

        [Test]
        public void ALoreLineGivenOutTakesNoLaugh()
        {
            const string tale = "the old wood keeps its own counsel and the trees remember every fire";
            Assert.That(VC.TakesEmote(null, tale), Is.True, "an ordinary line on its face");
            LoreChat.Said(tale);
            Assert.That(LoreChat.IsLoreLine(tale), Is.True);
            Assert.That(LoreChat.IsLoreLine("sorry, was fighting. " + tale), Is.True, "with a piece before it");
            VC.Voice laughing = Corpus.Voices.Single(v => v.Name == "chatty") with { EmoteChance = 1.0 };
            foreach (long id in BotIds())
                for (int seed = 0; seed < 10; seed++)
                {
                    string styled = AutonomousBotVoices.StyleText(laughing, id, tale, new Random(seed));
                    Assert.That(VC.HasLaugh(styled), Is.False, styled);
                }
            Assert.That(VC.TakesEmote("lore", "anything at all"), Is.False);
        }

        [Test]
        public void AFrontierDeathIsToldWithoutATackedLaugh()
        {
            var random = new Random(4);
            var told = new HashSet<string>();
            for (int i = 0; i < 300; i++)
                told.UnionWith(AutonomousChatFieldTalk.RvrDeathOpeners("emain", random));
            Assert.That(told.Where(line => System.Text.RegularExpressions.Regex.IsMatch(line, @"(lol|heh)\s*$")), Is.Empty,
                "no laugh tacked on the end; \"1 v 5 lol, dead\" keeps the one it was told with");
            Clean(told, "frontier deaths", "tacked_laugh", "doubled_word", "punctuation", "dangling_connector");
        }

        // ---- F10: names keep their capitals --------------------------------------------------------------------------

        [TestCase("the armsman trainer in camelot", "The armsman trainer in Camelot.")]
        [TestCase("three princes sit under legion", "Three princes sit under Legion.")]
        [TestCase("take the bog of cullen road to tir na nog", "Take the Bog of Cullen road to Tir na Nog.")]
        [TestCase("krondon? ogre hall under avalon", "Krondon? Ogre hall under Avalon.")]
        public void ATidyWriterCapitalizesTheNames(string text, string expected)
        {
            VC.Voice proper = Corpus.Voices.Single(v => v.Name == "proper");
            Assert.That(AutonomousBotVoices.StyleText(proper, 0, text, new Random(1)), Is.EqualTo(expected));
        }

        [Test]
        public void ALowerCaseWriterKeepsItsLowerCaseAndCommonWordsStaySmall()
        {
            VC.Voice plain = Corpus.Voices.Single(v => v.Name == "plain");
            Assert.That(AutonomousBotVoices.StyleText(plain, 0, "the armsman trainer in camelot", new Random(1)), Is.EqualTo("the armsman trainer in camelot"));
            Assert.That(AutonomousChatNames.Capitalize("the host of the crypt lord and the glacier giant"), Is.EqualTo("the host of the crypt lord and the glacier giant"));
            Assert.That(AutonomousChatNames.Capitalize("The barrows by camelot's gate"), Is.EqualTo("The barrows by Camelot's gate"));
            Assert.That(AutonomousChatNames.Names.ContainsKey("cursed forest"), Is.True);
            Assert.That(AutonomousChatNames.Names.ContainsKey("df"), Is.False, "too short to tell from a chat word");
        }

        [Test]
        public void TheAskersNameBeforeATidyAnswerKeepsTheSentence()
        {
            Assert.That(AutonomousBotChatCoordinator.NameTheAsker("The trainer is in Camelot.", "Wynn", 2), Is.EqualTo("Wynn, the trainer is in Camelot."));
            Assert.That(AutonomousBotChatCoordinator.NameTheAsker("I think so.", "Wynn", 2), Is.EqualTo("wynn I think so."));
            Assert.That(AutonomousBotChatCoordinator.NameTheAsker("Camelot, by the trainers.", "Wynn", 2), Is.EqualTo("Wynn, Camelot, by the trainers."));
        }

        // ---- F8: a lore line that asks and answers is drawn only for its own question -------------------------------

        [TestCase("what lvl is spraggon den?", "who is eraliwyr? the alchemist for viraniels plague. deep in the den, near spriggit", null)]
        [TestCase("is vendo caverns ok at 24?", "where is grjotgard? in the bear cave", null)]
        [TestCase("what lvl is iarnvidiurs lair?", "is the lair hard? the trash is fine with cures, iarnvidiur needs a raid",
            "the lair? the trash is fine with cures, iarnvidiur needs a raid")]
        [TestCase("whats in the cursed forest?", "whats in the cursed forest? juggernaut, the blight, cailean", "the cursed forest? juggernaut, the blight, cailean")]
        [TestCase("whats in the isle of glass?", "whats there? caer gothwaite, gothwaite harbor", "caer gothwaite, gothwaite harbor")]
        [TestCase("who holds abermenai?", "who holds abermenai? renegades, under the chieftain abermenai", "abermenai? renegades, under the chieftain abermenai")]
        [TestCase("what lvl for abermenai?", "who holds abermenai? renegades, under the chieftain abermenai", null)]
        [TestCase("spindelhalla what lvl?", "spindelhalla? spider dungeon. good hunting, watch the webs", "spindelhalla? spider dungeon. good hunting, watch the webs")]
        [TestCase("where is spindelhalla?", "spindelhalla? spider dungeon. good hunting, watch the webs", null)]
        [TestCase("what lvl is vendo?", "vendo is 20 to 28, bears and the bear cave", "vendo is 20 to 28, bears and the bear cave")]
        public void ALoreLineThatAsksIsSaidOnlyForItsOwnQuestion(string question, string line, string expected)
        {
            string[] names = { "spraggon den", "vendo caverns", "vendo", "iarnvidiur's lair", "iarnvidiurs lair", "the lair", "cursed forest",
                "isle of glass", "abermenai", "spindelhalla" };
            Assert.That(LoreChat.FitAnswer(question, line, names), Is.EqualTo(expected));
        }

        [Test]
        public void EveryLoreAnswerFitsItsQuestionInEveryStyle()
        {
            LoreBible bible = LoreBible.Current;
            var said = new List<string>();
            var raw = new HashSet<string>();
            var styled = new List<string>();
            foreach (LoreBible.Location location in bible.Locations)
                foreach (string question in new[] { $"what lvl is {location.Name}?", $"whats in {location.Name}?", $"is {location.Name} hard?",
                             $"where is {location.Name}?", $"can a 40 solo in {location.Name}?" })
                    foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                        for (int seed = 0; seed < 3; seed++)
                            if (LoreChat.TryAnswer(bible, Corpus.Lines, question.ToLowerInvariant(), new Random(seed), out string answer, realm))
                            {
                                if (ChatClunky.Codes(answer).Intersect(new[] { "own_question", "tacked_laugh", "case_mix" }).Any())
                                    raw.Add(answer);
                                else if (said.Count % 7 == 0)
                                    // Styled as the server styles it, right after the answer is given out.
                                    styled.AddRange(Voices().Select(voice => AutonomousBotVoices.StyleText(voice, BotIds()[^1], answer, new Random(seed))));
                                said.Add(answer);
                            }
            Assert.That(said, Is.Not.Empty);
            Assert.That(raw.Where(answer => ChatClunky.Codes(answer).Contains("own_question")), Is.Empty, "the answer never asks the question back");
            Assert.That(styled, Is.Not.Empty);
            Clean(styled, "lore answers", "own_question", "tacked_laugh", "case_mix");
        }

        // ---- the stitching pass -----------------------------------------------------------------------------------

        private static AutonomousChatReplyBudget.Moment Moment(string message, string channel = "group", string cls = "Armsman",
            bool heal = false, bool rez = false) =>
            new(message, channel, false, eAutonomousChatIntent.Unknown, cls, heal, rez, false);

        [Test]
        public void EveryRescueAndReactionReadsClean()
        {
            var said = new HashSet<string>();
            string[] lines = { "where do i buy dyes?", "anyone know where the smith is?", "lol that pull", "ugh died again", "need heal!!",
                "rez pls", "any buffs?", "dinged 30!" };
            foreach (string line in lines)
                foreach (string cls in new[] { "Armsman", "Cleric", "Eldritch", "Enchanter", "Infiltrator", "Healer", "Animist", "Shaman" })
                    for (int seed = 0; seed < 60; seed++)
                    {
                        bool healer = cls is "Cleric" or "Healer" or "Shaman";
                        string rescue = AutonomousChatReplyBudget.Rescue(Moment(line, "group", cls, healer, healer), "none unknown", new Random(seed));
                        if (rescue != null)
                            said.Add(rescue);
                    }
            foreach (string kind in new[] { "sympathy", "laugh", "agree" })
                for (int seed = 0; seed < 40; seed++)
                    said.Add(AutonomousChatReplyBudget.Reaction(kind, new Random(seed)));
            Assert.That(said.Count, Is.GreaterThan(20));
            Clean(said.SelectMany(text => Styled(text)), "rescues and reactions");
        }

        [Test]
        public void EveryPieceBeforeAReplyReadsClean()
        {
            string[] replies = { "sure, where?", "on my way", "cant, mid fight", "the trainer is in camelot", "try vendo at 24", "dunno" };
            var said = new HashSet<string>();
            foreach (string reply in replies)
                for (int seed = 0; seed < 12; seed++)
                {
                    said.Add(AutonomousBotChatCoordinator.AfterAFight(reply, new Random(seed)));
                    said.Add(AutonomousBotChatCoordinator.AfterAFight(reply, new Random(seed), riding: true));
                }
            foreach (string role in new[] { null, "healer", "enchanter", "tank", "archer" })
                foreach (int level in new[] { 5, 24, 48 })
                    for (int seed = 0; seed < 8; seed++)
                        said.Add(AutonomousBotChatCoordinator.NoneNearLevelReply(level, role, new Random(seed)));
            Clean(said.SelectMany(text => Styled(text, 2)), "pieces before a reply");
            var named = said.SelectMany(text => Styled(text, 1)).Select(text => AutonomousBotChatCoordinator.NameTheAsker(text, "Wynn", 2));
            Clean(named, "the asker's name before an answer");
        }

        [Test]
        public void EveryFriendsSmallTalkReadsClean()
        {
            var said = new HashSet<string>();
            var doings = new[]
            {
                new AutonomousChatEngagement.Doing("Emain Macha", null, null, true, false, true, false),
                new AutonomousChatEngagement.Doing(null, null, "Spindelhalla", false, false, false, false),
                new AutonomousChatEngagement.Doing("Emain Macha", null, null, false, false, true, false),
                new AutonomousChatEngagement.Doing("Camelot", null, null, false, true, false, false),
                new AutonomousChatEngagement.Doing("Salisbury Plains", "large skeleton", null, false, false, false, false),
                new AutonomousChatEngagement.Doing("Salisbury Plains", null, null, false, false, false, false),
                new AutonomousChatEngagement.Doing(null, null, null, false, false, false, false),
            };
            foreach (var doing in doings)
                foreach (bool offer in new[] { true, false })
                    foreach (string last in new[] { null, "Vendo Caverns", "Salisbury Plains" })
                        for (int seed = 0; seed < 16; seed++)
                            said.Add(AutonomousChatEngagement.FriendChat("Wynn", doing, last, offer, new Random(seed)));
            Clean(said.SelectMany(text => Styled(text, 1)), "a friend's small talk");
        }

        [Test]
        public void EveryWhereToLevelAndSpecAnswerReadsClean()
        {
            var said = new HashSet<string>();
            string[] dungeons = { null, "Spindelhalla", "Tomb of Mithra", "Spraggon Den", "Vendo Caverns", "Keltoi Fogou" };
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                foreach (int level in new[] { 3, 8, 12, 18, 24, 33, 41, 48 })
                    foreach (string dungeon in dungeons)
                        foreach (bool busy in new[] { true, false })
                            for (int seed = 0; seed < 3; seed++)
                            {
                                string zone = AutonomousChatLiveAnswers.CatalogZone(realm, level);
                                foreach (string z in new[] { zone, null })
                                    if (AutonomousChatLiveAnswers.HuntText(realm, level, z, dungeon, busy, new Random(seed)) is { } text)
                                        said.Add(text);
                            }
            foreach (string cls in new[] { "armsman", "cleric", "wizard", "warrior", "healer", "runemaster", "hero", "druid", "enchanter", "ranger" })
                for (int seed = 0; seed < 12; seed++)
                    if (AutonomousChatReplyPath.SpecAnswer($"what spec for a {cls}?", "Cleric", new Random(seed)) is { } spec)
                        said.Add(spec);
            Assert.That(said.Count, Is.GreaterThan(50));
            Clean(said.SelectMany(text => Styled(text, 1)), "where-to-level and spec answers");
        }

        [Test]
        public void EveryTrainerAnswerReadsClean()
        {
            var said = new HashSet<string>();
            foreach ((eRealm realm, string[] classes) in new[]
                     {
                         (eRealm.Albion, new[] { "Armsman", "Cleric", "Cabalist", "Infiltrator", "Minstrel", "Wizard" }),
                         (eRealm.Midgard, new[] { "Warrior", "Healer", "Runemaster", "Shadowblade", "Skald" }),
                         (eRealm.Hibernia, new[] { "Hero", "Druid", "Eldritch", "Enchanter", "Animist", "Blademaster" }),
                     })
                foreach (string cls in classes)
                    for (int seed = 0; seed < 12; seed++)
                        said.Add(AutonomousChatKnowledge.GenericTrainerAnswer(realm, cls, new Random(seed)));
            Clean(said.SelectMany(text => Styled(text, 1)), "trainer answers");
        }
    }
}
