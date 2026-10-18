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
        // Review 2026-10-18
        [TestCase("rez pls ty", Intent.Rez)]
        [TestCase("can someone rez me? ty in advance", Intent.Rez)]
        [TestCase("fuck you", Intent.Abuse)]
        public void ShortChatAndCommonRequestsAreRead(string message, Intent expected) =>
            Assert.That(Model.Predict(message), Is.EqualTo(expected), message);

        [TestCase("is this a dumb question")]
        [TestCase("this camp is garbage")]
        [TestCase("are you a bot?")]
        [TestCase("are scouts op")]
        [TestCase("yellow cons are for warming up")]
        [TestCase("ready when you are")]
        [TestCase("where do u sell trash loot")]
        [TestCase("terrible lag for everyone tonight")]
        [TestCase("awful lot of people at the camp")]
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

        [TestCase("is the keep door down?", Intent.Away, false)]
        [TestCase("my phone died", Intent.Away, false)]
        [TestCase("anyone have heat res gear?", Intent.Rez, false)]
        [TestCase("what res should i cap first", Intent.Rez, false)]
        [TestCase("back to camp", Intent.Back, false)]
        [TestCase("got killed by a bear", Intent.RvR, false)]
        [TestCase("finally got 50 plat", Intent.Ding, false)]
        [TestCase("i mean yeah", Intent.Help, false)]
        [TestCase("brb phone", Intent.Away, true)]
        public void WordsThatOnlySoundLikeShortChat(string message, Intent intent, bool expected) =>
            Assert.That(Model.Predict(message) == intent, Is.EqualTo(expected), $"{message}: {Model.Predict(message)}");

        [TestCase("ty for the group", false)]
        [TestCase("ty, inv pls", true)]
        [TestCase("thanks! can i join?", true)]
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
        [TestCase("goblin shaman", "goblin shamans")]
        [TestCase("human", "humans")]
        [TestCase("roof", "roofs")]
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
            Assert.That(lines.Where(line => line.Needs.Contains("will_rez")), Is.Not.Empty);
            Assert.That(lines.Where(line => line.Text.Contains("omw") || line.Text.Contains("coming") || line.Text.Contains("rezzing"))
                .All(line => line.Needs.Contains("will_rez")), Is.True, "Set only when the bot will really come.");
            Assert.That(Corpus.Lines.Where(line => line.Text is "rez pls" or "ty for the rez" && line.Kind == "group"), Is.Empty,
                "Group talk runs while the bot is alive and nobody has rezzed it.");
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

        [Test]
        public void SocialQuestionsGetSocialAnswers()
        {
            // Re-run 2026-10-18: "hey all, how is everyone?" got "couldnt tell you", "anyone on?" only refusals.
            Assert.That(Corpus.Lines.Where(line => line.Kind == "reply" && line.Answers.Contains("ask_howareyou")).Count(), Is.GreaterThanOrEqualTo(10));
            Assert.That(Corpus.Lines.Where(line => line.Kind == "reply" && line.Answers.Contains("ask_choice")).Count(), Is.GreaterThanOrEqualTo(8));
            Assert.That(Corpus.Lines.Where(line => line.Kind == "reply" && line.Answers.Contains("ask_anyone") && !line.Text.Contains("sorry") &&
                !line.Text.StartsWith("not") && !line.Text.StartsWith("cant")).Count(), Is.GreaterThanOrEqualTo(8), "Not every answer is a no.");
            Assert.That(Corpus.Lines.Where(line => line.Invites == "ask_howareyou").Select(line => line.Text),
                Has.Some.Contains("how is everyone"));
        }

        [Test]
        public void ClassTalkCanBeAboutTheClassAsked()
        {
            Assert.That(AutonomousChatShortTalk.ClassWordsIn("is friar good?"), Is.EqualTo(new[] { "friar" }));
            Assert.That(AutonomousChatShortTalk.SameClassWord("friars", "friar"), Is.True);
            Assert.That(AutonomousChatShortTalk.NamesAClass("depends on your spec"), Is.False);
            string[] aboutFriars = Corpus.Lines.Where(line => line.Kind == "reply" && line.Intent == "classtalk" &&
                AutonomousChatShortTalk.ClassWordsIn(line.Text).Any(w => AutonomousChatShortTalk.SameClassWord(w, "friar"))).Select(l => l.Text).ToArray();
            Assert.That(aboutFriars, Is.Not.Empty);
            Assert.That(Corpus.Lines.Count(line => line.Kind == "reply" && line.Intent == "classtalk" && !AutonomousChatShortTalk.NamesAClass(line.Text)),
                Is.GreaterThanOrEqualTo(10), "General answers for a class nobody wrote about.");
        }

        [Test]
        public void TheWindowIsThreeDaysWithASixHourFallback()
        {
            Assert.That(VC.RecentLines.Window, Is.EqualTo(TimeSpan.FromDays(3)));
            Assert.That(VC.RecentLines.Relaxed, Is.EqualTo(TimeSpan.FromHours(6)));
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

        #region Live test 2026-10-18

        [TestCase("how is everyone?", true)]
        [TestCase("hey all, hows it going", true)]
        [TestCase("how's everyone doing", true)]
        [TestCase("sup all", true)]
        [TestCase("how are you?", true)]
        [TestCase("hi all", false)]
        [TestCase("hello", false)]
        [TestCase("how much xp to 6?", false)]
        [TestCase("how do i get to camelot", false)]
        public void AHowAreYouIsToldFromAPlainHello(string text, bool asks) =>
            Assert.That(AutonomousChatShortTalk.AsksHowAreYou(text), Is.EqualTo(asks));

        [Test]
        public void HowAreYouGreetingsHaveAnswersOfTheirOwn()
        {
            // "hi all" drew "not bad, and you?": those lines answer "how are you" and are kept out of plain hellos.
            var answers = Corpus.Lines.Where(l => l.Kind == "reply" && l.Answers.Contains("ask_howareyou")).ToList();
            Assert.That(answers, Has.Count.GreaterThanOrEqualTo(5));
            Assert.That(Corpus.Lines.Count(l => l.Kind == "reply" && l.Intent == "greeting" && !l.Answers.Contains("ask_howareyou")),
                Is.GreaterThanOrEqualTo(10), "Plain hellos are left for a plain hello.");
            Assert.That(Model.Predict("how is everyone?"), Is.EqualTo(Intent.Greeting));
        }

        [TestCase((ushort)10)]
        [TestCase((ushort)101)]
        [TestCase((ushort)201)]
        public void TheCapitalsAreCapitals(ushort region) =>
            Assert.That(AutonomousBotVoices.IsCapitalRegion(region), Is.True,
                "The capitals are one zone at the dungeon offset; chat must not call them dungeons (\"lfg City of Camelot\").");

        [Test]
        public void ARoleThatSaysItIsNot50IsNotPlayedByA50()
        {
            VC.Scene scene = Corpus.Scenes.Single(s => s.Id == "rvr_guild_recruit");
            Assert.That(scene.Roles["D"].LevelHigh, Is.EqualTo(49), "D says \"i am not 50 yet, can i join\".");
            var random = new Random(4);
            var fifties = Enumerable.Range(1, 8).Select(i => new AutonomousBotDebates.Candidate(i, BotPersonalityType.Socialite, "Cleric", 50)).ToList();
            Assert.That(AutonomousBotDebates.Cast(scene, fifties, random), Is.Null, "Nobody below 50 to play D: the scene isn't cast.");
            var mixed = fifties.Append(new AutonomousBotDebates.Candidate(99, BotPersonalityType.Socialite, "Cleric", 42)).ToList();
            for (int i = 0; i < 20; i++)
                Assert.That(AutonomousBotDebates.Cast(scene, mixed, random)?["D"], Is.EqualTo(99));
            Assert.That(Corpus.Scenes.Where(s => s.Roles.Values.Any(r => r.LevelLow != 1 || r.LevelHigh != 50)).Select(s => s.Id),
                Is.SupersetOf(new[] { "rvr_guild_recruit", "rvr_relic_retake", "hib_gen_relic_night" }));
        }

        [TestCase("anyone on?", "ask_on")]
        [TestCase("whos around", "ask_on")]
        [TestCase("anybody here?", "ask_on")]
        [TestCase("how is everyone?", "ask_howareyou")]
        [TestCase("anyone on for a group at the pikemen camp near the bridge?", null)]
        [TestCase("lf guild", "ask_guild")]
        [TestCase("mind if i hunt near cotswold?", "ask_permission")]
        [TestCase("should i roll a cleric or a friar?", "ask_choice")]
        [TestCase("lfg or something?", null)]
        [TestCase("where to next?", "ask_next")]
        [TestCase("so where are we heading?", "ask_next")]
        [TestCase("lol", null)]
        public void APlayersQuestionTypeIsRead(string text, string shape) =>
            Assert.That(AutonomousChatShortTalk.QuestionShape(text), Is.EqualTo(shape));

        [Test]
        public void QuestionsAboutTheWorldAndWhoIsOnHaveFittingAnswers()
        {
            // /gu "anyone on?" drew "good question, someone else might know"; "anyone know if Caer Sursbrooke is ours?" drew
            // "ask again in a bit".
            Assert.That(Corpus.Lines.Count(l => l.Kind == "reply" && l.Answers.Contains("ask_on")), Is.GreaterThanOrEqualTo(10));
            Assert.That(Corpus.Lines.Count(l => l.Kind == "reply" && l.Answers.Contains("ask_know")), Is.GreaterThanOrEqualTo(10));
            Assert.That(Corpus.Lines.Single(l => l.Text.StartsWith("anyone know who camps")).Invites, Is.EqualTo("ask_know"));
            Assert.That(Corpus.Lines.Single(l => l.Text == "anyone else up at this hour? i never sleep").Invites, Is.EqualTo("chat"),
                "\"anyone else...?\" is company, not a request: \"same here\", not \"sorry, busy\".");
            Assert.That(Corpus.Lines.Single(l => l.Text == "murd tonight? short a couple for a group").Invites, Is.EqualTo("lfg"));
            Assert.That(Corpus.Lines.Where(l => l.Text.Contains('?') && l.Invites is "joke" or "boast").Select(l => l.Text),
                Is.All.Not.StartWith("who is the best"));
        }

        [TestCase("what does lfg mean?", "looking for group")]
        [TestCase("whats pbae", "point blank")]
        [TestCase("noob question, what is a con?", "tough")]
        [TestCase("what does inc mean", "incoming")]
        public void ChatWordsAreExplained(string text, string part)
        {
            Assert.That(Knowledge.TryExplainTerm(text, out string answer), Is.True);
            Assert.That(answer, Does.Contain(part));
        }

        [TestCase("what is the best class?")]
        [TestCase("what does a friar do")]
        [TestCase("lfg")]
        public void OtherQuestionsAreNotGlossary(string text) => Assert.That(Knowledge.TryExplainTerm(text, out _), Is.False);

        [Test]
        public void RepliesDontNameAChannel()
        {
            // A reply can be sent in /faction, /say, /group or /gu: "ask in /faction, someone will know" was said in /faction.
            Assert.That(Corpus.Lines.Where(l => l.Kind == "reply").Select(l => l.Text),
                Has.None.Matches(@"\b(try|ask in|post in|someone in) /?faction\b"),
                "Advice that teaches the channel (\"say lfg in faction with your level\") is fine.");
        }

        [Test]
        public void AGroupInACapitalDoesntTalkAboutPulls()
        {
            var inCamelot = new VC.Speaker(1, "Theaenorelle", eRealm.Albion, "Cleric", 8, BotPersonalityType.Socialite,
                new HashSet<string> { "in_group", "in_player_group", "in_capital", "evening" });
            var lines = Corpus.Candidates("group", inCamelot).Select(l => l.Text).ToList();
            Assert.That(lines, Is.Not.Empty);
            Assert.That(lines, Has.None.Contains("pull").And.None.Contains("oom").And.None.Contains("camp"));
            Assert.That(Corpus.Lines.Single(l => l.Text == "where to next?").Invites, Is.EqualTo("ask_next"));
            Assert.That(Corpus.Lines.Count(l => l.Kind == "reply" && l.Answers.Contains("ask_next")), Is.GreaterThanOrEqualTo(8));
        }

        [Test]
        public void UnpromptedFactionLinesWaitAfterAPlayerSpeaks()
        {
            Assert.That(Chat.QuietAfterPlayer(10_000, 10_000 + Chat.PlayerQuietMs - 1), Is.True);
            Assert.That(Chat.QuietAfterPlayer(10_000, 10_000 + Chat.PlayerQuietMs), Is.False);
            Assert.That(Chat.QuietAfterPlayer(0, 5_000), Is.False, "No player has spoken yet.");
        }

        [Test]
        public void ARunningSceneWaitsWhenAPlayerSpeaks()
        {
            Assert.That(AutonomousBotDebates.AfterPlayerSpoke(1_000, 5_000), Is.EqualTo(5_000 + AutonomousBotDebates.PlayerPauseMs));
            Assert.That(AutonomousBotDebates.AfterPlayerSpoke(90_000, 5_000), Is.EqualTo(90_000), "A longer pause is kept.");
        }

        #endregion
    }
}
