using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Chat = DOL.GS.AutonomousBotChatCoordinator;
using Intent = DOL.GS.eAutonomousChatIntent;
using Knowledge = DOL.GS.AutonomousChatKnowledge;
using Model = DOL.GS.AutonomousChatIntentModel;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>Bot chat 0.36-se.4: reading short chat, replies that fit what was said, group and guild talk, and the corpus rules.</summary>
    [TestFixture]
    public class UT_VoicesSe4
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

        #region Reading what a player typed

        // The audit's 90 messages (2026-10-18): these read as real-world talk, which /say ignored.
        [TestCase("lol", Intent.Laugh)]
        [TestCase("haha", Intent.Laugh)]
        [TestCase("rofl", Intent.Laugh)]
        [TestCase(":)", Intent.Laugh)]
        [TestCase("ty", Intent.Thanks)]
        [TestCase("thanks!", Intent.Thanks)]
        [TestCase("thx for the rez", Intent.Thanks)]
        [TestCase("cheers mate", Intent.Thanks)]
        [TestCase("gz", Intent.Congrats)]
        [TestCase("grats", Intent.Congrats)]
        [TestCase("nice ding", Intent.Congrats)]
        [TestCase("ding!", Intent.Ding)]
        [TestCase("ding 20", Intent.Ding)]
        [TestCase("finally 35", Intent.Ding)]
        [TestCase("brb", Intent.Away)]
        [TestCase("afk", Intent.Away)]
        [TestCase("brb dinner", Intent.Away)]
        [TestCase("back", Intent.Back)]
        [TestCase("re", Intent.Back)]
        [TestCase("ok back, what did i miss", Intent.Back)]
        [TestCase("wb", Intent.Greeting)]
        [TestCase("hi", Intent.Greeting)]
        [TestCase("hey all", Intent.Greeting)]
        [TestCase("hail Haralhelm", Intent.Greeting)]
        [TestCase("o/", Intent.Greeting)]
        [TestCase("gn all", Intent.Farewell)]
        [TestCase("good night everyone", Intent.Farewell)]
        [TestCase("logging off, cya", Intent.Farewell)]
        [TestCase("inc", Intent.Alarm)]
        [TestCase("INC 10 mids at benowyc", Intent.Alarm)]
        [TestCase("keep under attack!", Intent.Alarm)]
        [TestCase("rez pls", Intent.Rez)]
        [TestCase("anyone rez?", Intent.Rez)]
        [TestCase("need a rez in black mountains", Intent.Rez)]
        [TestCase("wts lvl 20 sword", Intent.Trading)]
        [TestCase("wtb mp chain", Intent.Trading)]
        [TestCase("pc on bloodied sword?", Intent.Trading)]
        [TestCase("anyone craft armor? need 30 chain", Intent.Trading)]
        [TestCase("lfg 12 cleric", Intent.Grouping)]
        [TestCase("lf1m healer spindel", Intent.Grouping)]
        [TestCase("inv pls", Intent.Grouping)]
        [TestCase("need puller for treibh caillte", Intent.Grouping)]
        [TestCase("is friar good?", Intent.ClassTalk)]
        [TestCase("best spec for a cleric?", Intent.ClassTalk)]
        [TestCase("are scouts op", Intent.ClassTalk)]
        [TestCase("should i roll a bard or druid", Intent.ClassTalk)]
        [TestCase("is shaman worth it at 50", Intent.ClassTalk)]
        [TestCase("are you a bot?", Intent.BotCheck)]
        [TestCase("lol are you even real", Intent.BotCheck)]
        [TestCase("ur all bots lol", Intent.BotCheck)]
        [TestCase("how do i get to emain", Intent.Travel)]
        [TestCase("where is the tomb of mithra?", Intent.Travel)]
        [TestCase("im lost lol", Intent.Travel)]
        [TestCase("where should i hunt at 30", Intent.Grinding)]
        [TestCase("anyone know a good xp spot for 25", Intent.Grinding)]
        [TestCase("what does con mean", Intent.Help)]
        [TestCase("got ganked by a nightshade again", Intent.RvR)]
        [TestCase("same", Intent.Banter)]
        [TestCase("stfu", Intent.Abuse)]
        [TestCase("youre useless", Intent.Abuse)]
        [TestCase("learn to play", Intent.Abuse)]
        [TestCase("who won the presidential election", Intent.IgnoreOutOfWorld)]
        public void ShortChatAndCommonRequestsAreRead(string message, Intent expected) =>
            Assert.That(Model.Predict(message), Is.EqualTo(expected), message);

        [TestCase("is this a dumb question")]
        [TestCase("this camp is garbage")]
        [TestCase("are you a bot?")]
        [TestCase("are scouts op")]
        [TestCase("yellow cons are for warming up")]
        [TestCase("ready when you are")]
        public void InnocentLinesAreNotInsults(string message) =>
            // Insults are remembered by bots (Living Server memory), so only an insult aimed at someone counts.
            Assert.That(Model.Predict(message), Is.Not.EqualTo(Intent.Abuse), message);

        [TestCase("lol are you even real")]
        [TestCase("my real job is boring")]
        [TestCase("k")]
        [TestCase("hmm")]
        [TestCase("lag?")]
        [TestCase("the realm is quiet")]
        public void OnlyTheHardWordListMakesRealWorldTalk(string message) =>
            // /say ignores real-world talk entirely; a classifier guess must never silence a player.
            Assert.That(Model.Predict(message), Is.Not.EqualTo(Intent.IgnoreOutOfWorld), message);

        [TestCase("ty for the group", false)]
        [TestCase("thanks for the invite", false)]
        [TestCase("anyone know a good xp spot for 25", false)]
        [TestCase("inv pls", true)]
        [TestCase("room for a cleric?", true)]
        public void AThankYouIsNotAGroupRequest(string message, bool expected) =>
            Assert.That(Chat.AsksToJoinGroup(message), Is.EqualTo(expected), message);

        #endregion

        #region How many answer

        [Test]
        public void ANoneOpenerIsNeverAnsweredAndQuestionsAlmostAlways()
        {
            var random = new Random(3);
            int asked = 0;
            for (int i = 0; i < 1_000; i++)
            {
                Assert.That(Chat.BotReplyCount("none", 5, random), Is.Zero);
                Assert.That(Chat.BotReplyCount("", 5, random), Is.Zero);
                if (Chat.BotReplyCount("ask_where", 5, random) > 0)
                    asked++;
                Assert.That(Chat.BotReplyCount("chat", 5, random), Is.InRange(0, 1));
                Assert.That(Chat.BotReplyCount("ask", 0, random), Is.Zero, "Nobody to answer.");
            }
            Assert.That(asked, Is.GreaterThan(850), "A question is answered about 9 times in 10.");
        }

        [Test]
        public void APlayerIsAnsweredButALaughInFactionUsuallyIsNot()
        {
            var random = new Random(4);
            int laughs = 0, greetings = 0;
            for (int i = 0; i < 1_000; i++)
            {
                laughs += Chat.PlayerReplyCount(Intent.Laugh, true, false, 5, random);
                greetings += Chat.PlayerReplyCount(Intent.Greeting, true, false, 5, random);
                Assert.That(Chat.PlayerReplyCount(Intent.Unknown, false, false, 5, random), Is.InRange(1, 2), "Anything else gets an answer.");
                Assert.That(Chat.PlayerReplyCount(Intent.Laugh, true, true, 5, random), Is.EqualTo(1), "A named bot always answers.");
                Assert.That(Chat.PlayerReplyCount(Intent.Greeting, true, false, 1, random), Is.LessThanOrEqualTo(1));
            }
            Assert.That(laughs, Is.LessThan(350));
            Assert.That(greetings, Is.GreaterThan(1_800), "A hello in /faction draws a couple of answers.");
        }

        [Test]
        public void RepliesArriveInOrderAndLongerOnesTakeLonger()
        {
            var random = new Random(5);
            int first = Chat.ReplyDelayMs("k", 0, random);
            int second = Chat.ReplyDelayMs("yeah, try the barrows, lots of groups go there", first, random);
            Assert.That(second, Is.GreaterThan(first));
            Assert.That(first, Is.InRange(1_200, 3_000));
            int longer = 0;
            for (int i = 0; i < 200; i++)
                if (Chat.ReplyDelayMs(new string('x', 80), 0, random) > Chat.ReplyDelayMs("k", 0, random))
                    longer++;
            Assert.That(longer, Is.GreaterThan(190));
        }

        [Test]
        public void AboutOneBotInSevenIsARegular()
        {
            int regulars = Enumerable.Range(1, 10_000).Count(id => AutonomousBotPersonality.RegularFactor(id) > 1);
            int quiet = Enumerable.Range(1, 10_000).Count(id => AutonomousBotPersonality.RegularFactor(id) < 1);
            Assert.That(regulars, Is.InRange(1_100, 1_700));
            Assert.That(quiet, Is.InRange(2_900, 3_700));
            Assert.That(AutonomousBotPersonality.RegularFactor(42), Is.EqualTo(AutonomousBotPersonality.RegularFactor(42)), "Fixed per bot.");
        }

        #endregion

        #region Corpus rules

        [Test]
        public void NotFlagsAreMetOnlyWithoutTheFlag()
        {
            var grouped = new HashSet<string> { "in_group", "outdoors" };
            var solo = new HashSet<string> { "solo", "outdoors" };
            Assert.That(VC.NeedsMet(["!in_group"], solo), Is.True);
            Assert.That(VC.NeedsMet(["!in_group"], grouped), Is.False);
            Assert.That(VC.NeedsMet(["outdoors", "!in_dungeon"], grouped), Is.True);
            Assert.That(VC.NeedsMet([], grouped), Is.True);
        }

        [Test]
        public void ASpecificQuestionFallsBackToGeneralAnswers()
        {
            Assert.That(VC.AnswerChain("ask_where"), Is.EqualTo(new[] { "ask_where", "ask" }));
            Assert.That(VC.AnswerChain("ask_bind"), Is.EqualTo(new[] { "ask_bind", "ask" }));
            Assert.That(VC.AnswerChain("boast"), Is.EqualTo(new[] { "boast" }));
            Assert.That(VC.AnswerChain("none"), Is.Empty);
            Assert.That(VC.AnswerChain(""), Is.Empty);
            Assert.That(VC.InviteFamily("ask_opinion"), Is.EqualTo("ask"));
        }

        [TestCase("hill cat", "hill cats")]
        [TestCase("orc clanshacker", "orc clanshackers")]
        [TestCase("giant wolf", "giant wolves")]
        [TestCase("lich", "liches")]
        [TestCase("bwca of the hills", "bwcas of the hills")]
        [TestCase("forest fairy", "forest fairies")]
        [TestCase("skeletal footman", "skeletal footmen")]
        [TestCase("wild boar", "wild boars")]
        [TestCase("spiders", "spiders")]
        public void MonsterPlurals(string one, string many) => Assert.That(VC.Plural(one), Is.EqualTo(many));

        [Test]
        public void TonightReadsTodayByDay()
        {
            Assert.That(VC.TimeFlag(9), Is.EqualTo("day"));
            Assert.That(VC.TimeFlag(19), Is.EqualTo("evening"));
            Assert.That(VC.TimeFlag(2), Is.EqualTo("night"));
            Assert.That(VC.ForTimeOfDay("anyone running spindel tonight?", 12), Is.EqualTo("anyone running spindel today?"));
            Assert.That(VC.ForTimeOfDay("camped there all night", 12), Is.EqualTo("camped there all day"));
            Assert.That(VC.ForTimeOfDay("anyone running spindel tonight?", 21), Is.EqualTo("anyone running spindel tonight?"));
        }

        [Test]
        public void ProperWritersTypeChatWordsAsChatWords()
        {
            VC.Voice proper = Corpus.Voices.Single(voice => voice.Name == "proper");
            var random = new Random(1);
            Assert.That(VC.Style("lol", proper, random), Is.EqualTo("lol"));
            Assert.That(VC.Style("ty ty", proper, random), Is.EqualTo("ty ty"));
            Assert.That(VC.Style("gz", proper, random), Is.EqualTo("gz"));
            Assert.That(VC.Style("good luck out there", proper, random), Is.EqualTo("Good luck out there."));
        }

        [Test]
        public void ATypoCanBeCorrectedInASecondLine()
        {
            VC.Voice typo = Corpus.Voices.Single(voice => voice.Name == "typo_prone");
            int corrections = 0;
            for (int seed = 0; seed < 2_000; seed++)
            {
                string text = VC.Style("anyone heading towards spindelhalla", typo, new Random(seed), out string correction);
                if (correction == null)
                    continue;
                corrections++;
                Assert.That(correction, Does.StartWith("*"));
                Assert.That("anyone heading towards spindelhalla", Does.Contain(correction[1..]), "The correction is the word as it should be.");
                Assert.That(text, Does.Not.Contain(correction[1..]), "It fixes a word the line got wrong.");
            }
            Assert.That(corrections, Is.GreaterThan(50));
        }

        [Test]
        public void EveryOpenerSaysWhatItInvitesAndEveryInvitationHasReplies()
        {
            string[] openers = ["ambient", "lfg", "trade", "rvr", "camp", "group", "guild"];
            var linesByAnswer = Corpus.Lines.Where(line => line.Kind == "reply").SelectMany(line => line.Answers.Select(a => (a, line)))
                .GroupBy(pair => pair.a).ToDictionary(group => group.Key, group => group.Count());
            Assert.Multiple(() =>
            {
                foreach (VC.Line line in Corpus.Lines.Where(line => openers.Contains(line.Kind)))
                {
                    Assert.That(line.Invites, Is.Not.Empty, line.Text);
                    if (line.Invites is "none" or "chat")
                        continue;
                    int answers = VC.AnswerChain(line.Invites).Sum(key => linesByAnswer.GetValueOrDefault(key));
                    Assert.That(answers, Is.GreaterThanOrEqualTo(5), $"{line.Invites}: {line.Text}");
                }
                foreach (string family in new[] { "ask", "lfg", "trade", "chat", "joke", "boast", "complaint", "news" })
                    Assert.That(Corpus.Lines.Count(line => line.Kind == "closing" && line.Answers.Contains(family)), Is.GreaterThanOrEqualTo(5), family);
                Assert.That(Corpus.Lines.Where(line => line.Kind == "closing").Select(line => line.Text), Has.None.EndsWith("?"),
                    "A closing never asks something new.");
            });
        }

        [Test]
        public void GroupTalkIsOnlyForGroupsAndGuildTalkCoversEveryCharter()
        {
            Assert.Multiple(() =>
            {
                foreach (VC.Line line in Corpus.Lines.Where(line => line.Kind == "group"))
                    Assert.That(line.Needs.Contains("in_group") || line.Needs.Contains("in_player_group"), Is.True, line.Text);
                foreach (string topic in new[] { "leveling", "social", "rvr", "raiding", "crafting", "general" })
                    Assert.That(Corpus.Lines.Count(line => line.Kind == "guild" && line.Topic == topic), Is.GreaterThanOrEqualTo(15), topic);
                Assert.That(Corpus.Lines.Count(line => line.Kind == "ambient" && line.Needs.Contains("in_dungeon")), Is.GreaterThanOrEqualTo(30),
                    "Bots in dungeons have something to say (the audit found none).");
            });
        }

        [Test]
        public void OnlyABotThatCanRezSaysItIsComing()
        {
            var lines = Corpus.Lines.Where(line => line.Kind == "reply" && line.Intent == "rez").ToArray();
            Assert.That(lines.Where(line => line.Needs.Contains("can_rez")), Is.Not.Empty);
            Assert.That(lines.Where(line => line.Text.Contains("omw") || line.Text.Contains("coming") || line.Text.Contains("rezzing"))
                .All(line => line.Needs.Contains("can_rez")), Is.True);
        }

        [Test]
        public void ARelaxedPickReusesAShortReplyHeardLongEnoughAgo()
        {
            var speaker = new VC.Speaker(7, "Test", eRealm.Albion, "Cleric", 20, BotPersonalityType.Leveler, new HashSet<string> { "solo", "outdoors" });
            var recent = new VC.RecentLines();
            DateTime now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            var slots = new Dictionary<string, string> { ["player"] = "Wrenfield" };
            foreach (VC.Line line in Corpus.Candidates("reply", speaker, "thanks"))
                recent.Use(eRealm.Albion, line.Id, now.AddHours(-1));
            var strict = new VC.Request("reply", speaker, slots, recent, now, new Random(1)) { Intent = "thanks" };
            Assert.That(Corpus.TryPick(strict, out _), Is.False, "Every thanks reply was heard in the last six hours.");
            Assert.That(Corpus.TryPick(strict with { RelaxTo = TimeSpan.FromMinutes(40) }, out VC.Picked picked), Is.True);
            Assert.That(picked.Line.Intent, Is.EqualTo("thanks"));
            Assert.That(Corpus.TryPick(strict with { RelaxTo = TimeSpan.FromMinutes(40), Now = now.AddHours(-1).AddMinutes(10) }, out _), Is.False,
                "Heard ten minutes ago: still resting.");
        }

        [Test]
        public void TheNoRepeatWindowCanBeSavedAndRestored()
        {
            var recent = new VC.RecentLines();
            DateTime then = new(2026, 10, 18, 10, 0, 0, DateTimeKind.Utc);
            recent.Use(eRealm.Midgard, "abc", then);
            recent.Use(eRealm.Hibernia, "def", then.AddMinutes(5));
            var saved = recent.UsedSince(then.AddMinutes(1));
            Assert.That(saved.Select(s => s.Id), Is.EquivalentTo(new[] { "def" }));
            var restored = new VC.RecentLines();
            foreach (var use in recent.UsedSince(DateTime.MinValue))
                restored.Restore(use.Realm, use.Id, use.When);
            Assert.That(restored.WasUsed(eRealm.Midgard, "abc", then.AddHours(1)), Is.True);
            restored.Restore(eRealm.Midgard, "abc", then.AddHours(-3));
            Assert.That(restored.UsedWithin(eRealm.Midgard, "abc", then.AddMinutes(10), TimeSpan.FromMinutes(15)), Is.True, "An older save doesn't win.");
            Assert.That(AutonomousBotLinesHeard.KeyOf(eRealm.Midgard, "abc"), Is.EqualTo("2:abc"));
        }

        [Test]
        public void ScenesThatNeedTheNightPlayOnlyAtNight()
        {
            VC.Scene[] night = Corpus.Scenes.Where(scene => scene.Needs.Contains("night")).ToArray();
            Assert.That(night, Is.Not.Empty);
            var heard = new HashSet<string>(Corpus.Scenes.Where(scene => !night.Contains(scene)).Select(scene => scene.Id));
            for (int seed = 0; seed < 50; seed++)
            {
                VC.Scene noon = AutonomousBotDebates.ChooseScene(Corpus.Scenes, eRealm.Albion, heard, [20, 25, 30, 40, 50], new Random(seed),
                    out _, "day");
                Assert.That(noon == null || !noon.Needs.Contains("night"), Is.True, noon?.Id);
            }
        }

        #endregion

        #region Knowledge and group turns

        [TestCase("where is the cleric trainer", "Raine", false)]
        [TestCase("how do i get to camelot", "Mel", false)]
        [TestCase("how do i get to camelot", "Camelot", true)]
        [TestCase("where is tomb of mithra?", "Tomb of Mithra", true)]
        public void PlacesMatchAsWholeWords(string text, string name, bool expected) =>
            Assert.That(Knowledge.ContainsPhrase(text, name), Is.EqualTo(expected));

        [Test]
        public void OnlyRealLocationQuestionsGetALocationAnswer()
        {
            Assert.That(Knowledge.LooksLikeLocationQuestion("ranger, recurve bow, pathfinding, nature friend"), Is.False);
            Assert.That(Knowledge.LooksLikeLocationQuestion("lfm, healer and tank found, need dps"), Is.False);
            Assert.That(Knowledge.LooksLikeLocationQuestion("wheres spindelhalla?"), Is.True);
            Assert.That(Knowledge.LooksLikeTrainerQuestion("where is the scout trainer"), Is.True);
            Assert.That(Knowledge.LooksLikeTrainerQuestion("where do i train?"), Is.True);
            Assert.That(Knowledge.IsRoleName("Healer") && Knowledge.IsRoleName("Armsman") && Knowledge.IsRoleName("Ranger"), Is.True);
            Assert.That(Knowledge.IsRoleName("Lt. Rydderac"), Is.False);
        }

        [Test]
        public void AGroupHearsItsFirstLineSoonThenEveryFewMinutes()
        {
            var random = new Random(2);
            object sync = new();
            long next = 0;
            Assert.That(AutonomousBotGroupChatter.TakeTurn(sync, () => next, v => next = v, 1_000, random, known: false), Is.False,
                "A new group books its first line.");
            Assert.That(next - 1_000, Is.InRange(AutonomousBotGroupChatter.FirstLineMinMs, AutonomousBotGroupChatter.FirstLineMaxMs));
            Assert.That(AutonomousBotGroupChatter.TakeTurn(sync, () => next, v => next = v, next - 1, random, known: true), Is.False);
            long due = next;
            Assert.That(AutonomousBotGroupChatter.TakeTurn(sync, () => next, v => next = v, due, random, known: true), Is.True);
            Assert.That(next - due, Is.InRange(AutonomousBotGroupChatter.MinGapMs, AutonomousBotGroupChatter.MaxGapMs));
        }

        #endregion
    }
}
