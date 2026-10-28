using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The talking update (Living Server Beta 1.1): chat that only says what is true (places, news, xp, roles), conversations
    /// that follow on (threads, follow-ups, closings), combat calls in the player's group, bot-started tells, busier realm
    /// chat and /lfg, /trade channels. These are the pure parts; what needs a running world is listed in
    /// docs/bot-voices/TALKING-UPDATE.md as in-game checks.
    /// </summary>
    [TestFixture]
    public class UT_TalkingUpdate
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

        /// <summary>A Random whose draws are fixed, so the chance-based calls are deterministic.</summary>
        private sealed class FixedRandom(double value) : Random
        {
            public override double NextDouble() => value;
            public override int Next(int maxValue) => 0;
            public override int Next(int minValue, int maxValue) => minValue;
        }

        /// <summary>Draws the top of every range.</summary>
        private sealed class LongestRandom : Random
        {
            public override int Next(int maxValue) => maxValue - 1;
        }

        private static readonly Random Always = new FixedRandom(0.0);
        private static readonly Random Never = new FixedRandom(0.999);

        #region Places

        private const string PlacesJson = """
        {"places": [
          {"key": "vale of mularn", "realm": "mid", "type": "zone", "aliases": ["vale of mularn"], "zones": ["Vale of Mularn"]},
          {"key": "mularn", "realm": "mid", "type": "town", "aliases": ["mularn"], "zones": ["Vale of Mularn"]},
          {"key": "myrkwood", "realm": "mid", "type": "zone", "aliases": ["myrkwood", "myrkwood forest"], "zones": ["Myrkwood Forest"]},
          {"key": "spindelhalla", "realm": "mid", "type": "dungeon", "aliases": ["spindelhalla", "spindel"], "zones": ["Spindelhalla"]}
        ]}
        """;

        [Test]
        public void ABotInAZoneIsAtTheZoneAndItsTowns()
        {
            var places = AutonomousChatPlaces.Parse(PlacesJson);
            Assert.That(AutonomousChatPlaces.KeysAt("Vale of Mularn", null, places), Is.EquivalentTo(new[] { "vale of mularn", "mularn" }));
            Assert.That(AutonomousChatPlaces.KeysAt("Myrkwood Forest", null, places), Is.EquivalentTo(new[] { "myrkwood" }));
            Assert.That(AutonomousChatPlaces.KeysAt("Spindelhalla", null, places), Is.EquivalentTo(new[] { "spindelhalla" }));
            Assert.That(AutonomousChatPlaces.KeysAt("Camelot Hills", null, places), Is.Empty);
        }

        [Test]
        public void ADestinationIsThePlaceNamedNotEveryTownInItsZone()
        {
            var places = AutonomousChatPlaces.Parse(PlacesJson);
            Assert.That(AutonomousChatPlaces.KeysFor("Myrkwood Forest", places), Is.EquivalentTo(new[] { "myrkwood" }));
            Assert.That(AutonomousChatPlaces.KeysFor("Spindelhalla", places), Is.EquivalentTo(new[] { "spindelhalla" }));
            Assert.That(AutonomousChatPlaces.KeysFor("Mularn", places), Is.EquivalentTo(new[] { "mularn" }));
            Assert.That(AutonomousChatPlaces.KeysFor("", places), Is.Empty);
        }

        [Test]
        public void PlaceFlagsSayAtAndGoing()
        {
            var places = AutonomousChatPlaces.Parse(PlacesJson);
            Assert.That(AutonomousChatPlaces.Flags("Vale of Mularn", null, "Spindelhalla", places),
                Is.EquivalentTo(new[] { "at:vale of mularn", "at:mularn", "going:spindelhalla" }));
        }

        [Test]
        public void TheEmbeddedPlacesLoadAndHaveRealms()
        {
            Assert.That(AutonomousChatPlaces.All.Length, Is.GreaterThan(100));
            Assert.That(AutonomousChatPlaces.All.Select(p => p.Realm), Is.All.AnyOf("alb", "mid", "hib", "*"));
            Assert.That(AutonomousChatPlaces.All.Select(p => p.Key), Is.Unique);
        }

        [Test]
        public void NearIsAtOrGoing()
        {
            var at = new HashSet<string>(["at:myrkwood"], StringComparer.Ordinal);
            var going = new HashSet<string>(["going:myrkwood"], StringComparer.Ordinal);
            var neither = new HashSet<string>(["at:spindelhalla"], StringComparer.Ordinal);
            Assert.That(VC.NeedsMet(["near:myrkwood"], at), Is.True);
            Assert.That(VC.NeedsMet(["near:myrkwood"], going), Is.True);
            Assert.That(VC.NeedsMet(["near:myrkwood"], neither), Is.False);
            Assert.That(VC.NeedsMet(["!near:myrkwood"], neither), Is.True);
            Assert.That(VC.NeedsMet(["!near:myrkwood"], at), Is.False);
        }

        [Test]
        public void ALineThatNamesAPlaceClaimsBeingThereOrSaysItIsAbout()
        {
            // The build checks this; here the shipped corpus is checked again: no claim kind names a place without a place need.
            string[] claimKinds = ["ambient", "lfg", "rvr", "camp", "group", "ding", "death", "news"];
            var keysByAlias = AutonomousChatPlaces.All.SelectMany(p => p.Aliases.Select(a => (Alias: a, p.Key)))
                .Where(x => x.Alias.Length >= 5).ToArray();
            var offenders = Corpus.Lines.Where(line => claimKinds.Contains(line.Kind) && !line.About)
                .Where(line => keysByAlias.Any(x => System.Text.RegularExpressions.Regex.IsMatch(line.Text.ToLowerInvariant(),
                    $@"(?<![a-z']){System.Text.RegularExpressions.Regex.Escape(x.Alias)}(?![a-z])")))
                .Where(line => !line.Needs.Any(n => n.TrimStart('!').StartsWith("at:") || n.TrimStart('!').StartsWith("going:") ||
                                                    n.TrimStart('!').StartsWith("near:")))
                .Select(line => line.Text).ToArray();
            // Lines marked "@about place" carry "about" in corpus.json (2026-10-26; before, the tag was dropped and this allowed a
            // tenth of the lines through). Any other line that names a place must say where the speaker is.
            Assert.That(offenders, Is.Empty, string.Join("\n", offenders.Take(20)));
        }

        #endregion

        #region News

        [Test]
        public void AKeepTakenIsWonLostOrOtherForEachRealm()
        {
            DateTime now = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
            var items = AutonomousChatNews.KeepChange("Caer Benowyc", "Pennine Mountains", eRealm.Albion, eRealm.Midgard, now);
            Assert.That(items.Single(i => i.Realm == eRealm.Midgard).Topic, Is.EqualTo("keep_won"));
            Assert.That(items.Single(i => i.Realm == eRealm.Albion).Topic, Is.EqualTo("keep_lost"));
            Assert.That(items.Single(i => i.Realm == eRealm.Hibernia).Topic, Is.EqualTo("keep_other"));
            Assert.That(items.Single(i => i.Realm == eRealm.Albion).Enemy, Is.EqualTo(eRealm.Midgard));
            Assert.That(AutonomousChatNews.KeepChange("Caer Benowyc", null, eRealm.Albion, eRealm.Albion, now), Is.Empty);
            Assert.That(AutonomousChatNews.KeepChange("Caer Benowyc", null, eRealm.Albion, eRealm.None, now), Is.Empty);
        }

        [Test]
        public void ARelicIsLostWonOrBackHome()
        {
            DateTime now = DateTime.UtcNow;
            var stolen = AutonomousChatNews.RelicChange("Scabbard of Excalibur", true, eRealm.Albion, eRealm.Hibernia, eRealm.Albion, now);
            Assert.That(stolen.Single().Topic, Is.EqualTo("relic_lost"));
            Assert.That(stolen.Single().Realm, Is.EqualTo(eRealm.Albion));
            Assert.That(stolen.Single().Enemy, Is.EqualTo(eRealm.Hibernia));
            var won = AutonomousChatNews.RelicChange("Scabbard of Excalibur", false, eRealm.Hibernia, eRealm.Hibernia, eRealm.Albion, now);
            Assert.That(won.Single().Topic, Is.EqualTo("relic_won"));
            Assert.That(won.Single().Realm, Is.EqualTo(eRealm.Hibernia));
            var back = AutonomousChatNews.RelicChange("Scabbard of Excalibur", false, eRealm.Albion, eRealm.Albion, eRealm.Albion, now);
            Assert.That(back.Single().Topic, Is.EqualTo("relic_back"));
        }

        [Test]
        public void NewsFlagsLastTheirWindow()
        {
            var board = new ChatNewsBoard();
            DateTime then = new(2026, 10, 26, 20, 0, 0, DateTimeKind.Utc);
            foreach (ChatNewsItem item in AutonomousChatNews.KeepChange("Caer Benowyc", "Pennine Mountains", eRealm.Albion, eRealm.Midgard, then))
                board.Post(item);
            var albion = board.FlagSlots(eRealm.Albion, then.AddMinutes(30));
            Assert.That(albion.Keys, Is.SupersetOf(new[] { "keep_news", "keep_lost_recent" }));
            Assert.That(albion.ContainsKey("keep_won_recent"), Is.False);
            Assert.That(albion["keep_lost_recent"]["keep"], Is.EqualTo("Caer Benowyc"));
            Assert.That(board.FlagSlots(eRealm.Midgard, then.AddMinutes(30)).ContainsKey("keep_won_recent"), Is.True);
            Assert.That(board.FlagSlots(eRealm.Albion, then.AddMinutes(61)), Is.Empty);
        }

        [Test]
        public void SightingsNeverPushKeepNewsOffTheBoard()
        {
            var board = new ChatNewsBoard();
            DateTime now = DateTime.UtcNow;
            foreach (ChatNewsItem item in AutonomousChatNews.KeepChange("Caer Benowyc", null, eRealm.Albion, eRealm.Midgard, now))
                board.Post(item);
            for (int i = 0; i < ChatNewsBoard.Capacity * 2; i++)
                board.Post(new ChatNewsItem("enemy_seen", eRealm.Albion, now) { Enemy = eRealm.Midgard, Zone = "Emain Macha", Size = 4 });
            Assert.That(board.FlagSlots(eRealm.Albion, now).ContainsKey("keep_lost_recent"), Is.True);
            Assert.That(board.EnemySightings(eRealm.Albion, now).Length, Is.EqualTo(ChatNewsBoard.SightingCapacity));
            Assert.That(board.EnemySightings(eRealm.Albion, now.AddMinutes(16)), Is.Empty);
            Assert.That(board.EnemySightings(eRealm.Midgard, now), Is.Empty);
        }

        [Test]
        public void AFlagsSlotsFillTheLineThatNeedsIt()
        {
            VC.Line line = Corpus.Lines.First(l => l.Needs.Contains("keep_lost_recent") && l.Text.Contains("{keep}"));
            var needSlots = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                ["keep_lost_recent"] = new Dictionary<string, string> { ["keep"] = "Caer Benowyc", ["winner"] = "Midgard" },
                ["keep_won_recent"] = new Dictionary<string, string> { ["keep"] = "Bledmeer Faste" },
            };
            var slots = VC.SlotsForLine(line, new Dictionary<string, string> { ["level"] = "30" }, needSlots);
            Assert.That(slots["keep"], Is.EqualTo("Caer Benowyc"));
            Assert.That(slots["level"], Is.EqualTo("30"));
        }

        [Test]
        public void DefendersKnowOfASiegeOnlyOnceItIsSeen()
        {
            var mustering = new AutonomousRvrEventLayer.SiegeView("k1", "keep Caer Benowyc", eRealm.Midgard, eRealm.Albion, 1, 0, 0,
                BattleStarted: false, Marching: true, RelicKeep: false);
            Assert.That(AutonomousChatNews.SiegeFlags(eRealm.Albion, [mustering], null).ContainsKey("defending"), Is.False);
            Assert.That(AutonomousChatNews.SiegeFlags(eRealm.Midgard, [mustering], null)["attacking"]["keep"], Is.EqualTo("Caer Benowyc"));
            var hit = mustering with { BattleStarted = true };
            var flags = AutonomousChatNews.SiegeFlags(eRealm.Albion, [hit], (_, _, _) => "Pennine Mountains");
            Assert.That(flags["defending"]["enemy"], Is.EqualTo("Midgard"));
            Assert.That(flags["defending"]["frontier"], Is.EqualTo("Pennine Mountains"));
            Assert.That(AutonomousChatNews.SiegeFlags(eRealm.Hibernia, [hit], null), Is.Empty);
        }

        [Test]
        public void ASiegeEndsAsHeldFailedOrTaken()
        {
            Assert.That(AutonomousRvrEventLayer.SiegeOutcomeNews("keep Caer Benowyc", eRealm.Midgard, eRealm.Albion, eRealm.Albion,
                eRealm.Albion, false).Topic, Is.EqualTo("siege_held"));
            Assert.That(AutonomousRvrEventLayer.SiegeOutcomeNews("keep Caer Benowyc", eRealm.Midgard, eRealm.Albion, eRealm.Midgard,
                eRealm.Albion, false).Topic, Is.EqualTo("siege_failed"));
            var taken = AutonomousRvrEventLayer.SiegeOutcomeNews("keep Caer Benowyc", eRealm.Midgard, eRealm.Albion, eRealm.Albion,
                eRealm.Midgard, true);
            Assert.That(taken.Topic, Is.EqualTo("keep_lost"));
            Assert.That(taken.Slots["keep"], Is.EqualTo("Caer Benowyc"));
            Assert.That(AutonomousRvrEventLayer.SiegeOutcomeNews("keep Caer Benowyc", eRealm.Midgard, eRealm.Albion, eRealm.Hibernia,
                eRealm.Midgard, true).Topic, Is.EqualTo("keep_other"));
            Assert.That(AutonomousRvrEventLayer.SiegeOutcomeNews("keep Caer Benowyc", eRealm.Midgard, eRealm.Albion, eRealm.Hibernia,
                eRealm.Albion, false).Topic, Is.Null);
        }

        [Test]
        public void ASightingReadsLikeAPlayerCallingIt()
        {
            DateTime now = DateTime.UtcNow;
            var item = new ChatNewsItem("enemy_seen", eRealm.Albion, now.AddMinutes(-3)) { Enemy = eRealm.Midgard, Zone = "Emain Macha", Size = 5 };
            string text = AutonomousChatLiveAnswers.SightingText(item, now, Always);
            Assert.That(text, Is.EqualTo("5 mids in Emain Macha, couple min ago"));
            var big = item with { Size = 12, When = now };
            Assert.That(AutonomousChatLiveAnswers.SightingText(big, now, Always), Does.StartWith("big group of mids"));
        }

        #endregion

        #region Experience and roles

        [TestCase(0, 0, 1000, 0, "xp_low")]
        [TestCase(150, 0, 1000, 1, "xp_low")]
        [TestCase(250, 0, 1000, 2, "xp_mid")]
        [TestCase(799, 0, 1000, 7, "xp_mid")]
        [TestCase(800, 0, 1000, 8, "xp_high")]
        [TestCase(5000, 0, 1000, 9, "xp_high")]
        public void BubblesAreTenthsOfTheLevel(long xp, long low, long high, int bubbles, string flag)
        {
            Assert.That(AutonomousBotVoices.BubblesFor(xp, low, high), Is.EqualTo(bubbles));
            Assert.That(AutonomousBotVoices.XpFlag(bubbles, 20), Is.EqualTo(flag));
        }

        [Test]
        public void ALevelFiftyHasNoXpFlag()
        {
            Assert.That(AutonomousBotVoices.XpFlag(5, 50), Is.Null);
            Assert.That(AutonomousBotVoices.BubblesFor(10, 100, 100), Is.Null);
        }

        #endregion

        #region Conversations

        [TestCase("why?", "why")]
        [TestCase("how come", "why")]
        [TestCase("rly?", "really")]
        [TestCase("for real?", "really")]
        [TestCase("how so", "why")]
        [TestCase("wdym", "what")]
        [TestCase("huh?", "what")]
        [TestCase("who was it", "who")]
        [TestCase("where at?", "where")]
        [TestCase("how do you mean", "how")]
        public void AShortFollowUpIsRead(string message, string word) =>
            Assert.That(AutonomousChatThreads.FollowUpWord(message), Is.EqualTo(word));

        [TestCase("why is everyone in emain tonight anyway")]
        [TestCase("where is the cleric trainer in camelot")]
        [TestCase("hi")]
        [TestCase("")]
        public void ALongerQuestionIsNotAFollowUp(string message) => Assert.That(AutonomousChatThreads.FollowUpWord(message), Is.Null);

        [Test]
        public void AnAnswerHasWordsAndIsNotAQuestionOrGreeting()
        {
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("try spindelhalla", eAutonomousChatIntent.Dungeon), Is.True);
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("no idea sorry", eAutonomousChatIntent.Unknown), Is.True);
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("where?", eAutonomousChatIntent.Unknown), Is.False);
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("hi all", eAutonomousChatIntent.Greeting), Is.False);
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("lol", eAutonomousChatIntent.Laugh), Is.False);
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("why", eAutonomousChatIntent.Unknown), Is.False);
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("lfg healer for tom", eAutonomousChatIntent.Unknown), Is.False,
                "a player's own lfg is not an answer");
            Assert.That(AutonomousChatThreads.ReadsAsAnswer("wts nice sword", eAutonomousChatIntent.Trading), Is.False);
        }

        [Test]
        public void ClosingsFitTheQuestionMostSpecificFirst()
        {
            Assert.That(AutonomousChatThreads.ClosingAnswers("ask_where"), Is.EqualTo(new[] { "ask_where", "ask" }));
            Assert.That(AutonomousChatThreads.ClosingAnswers("ask"), Is.EqualTo(new[] { "ask" }));
            Assert.That(AutonomousChatThreads.ClosingAnswers(""), Is.EqualTo(new[] { "ask" }));
        }

        [TestCase("sure", true)]
        [TestCase("yeah inv me", true)]
        [TestCase("ok", true)]
        [TestCase("sounds good!", true)]
        [TestCase("nah im good", false)]
        [TestCase("maybe later", false)]
        [TestCase("no thanks", false)]
        [TestCase("sorry, already grouped", false)]
        [TestCase("what level are you", null)]
        [TestCase("no problem, inv me", true)]
        public void AYesOrNoToAnOffer(string message, bool? yes) => Assert.That(AutonomousChatThreads.SaysYes(message), Is.EqualTo(yes));

        [Test]
        public void TheThreadLogKeepsTheLastLinesAndOpenQuestions()
        {
            var log = new ChatThreadLog();
            log.Record("faction:Albion", 7, "Aldwin", "anyone know where to hunt at 25?", "ask_where", 1_000);
            ChatThreadLog.Said question = log.OpenQuestion("faction:Albion", 30_000, AutonomousChatThreads.AnswerWindowMs);
            Assert.That(question, Is.Not.Null);
            Assert.That(question.Family, Is.EqualTo("ask"));
            log.Close(question);
            Assert.That(log.OpenQuestion("faction:Albion", 30_000, AutonomousChatThreads.AnswerWindowMs), Is.Null);
            Assert.That(log.Last("faction:Albion", 200_000, AutonomousChatThreads.FollowUpWindowMs), Is.Null, "too old");
            Assert.That(log.LastBy(7, 60_000, AutonomousChatThreads.TellMemoryMs)?.Text, Does.StartWith("anyone know"));
            Assert.That(log.Last("faction:Midgard", 2_000, 90_000), Is.Null);
            for (int i = 0; i < ChatThreadLog.PerChannel + 3; i++)
                log.Record("say:1", i + 10, "Bot" + i, "line " + i, "chat", 5_000 + i);
            Assert.That(log.Last("say:1", 6_000, 90_000).Text, Is.EqualTo("line " + (ChatThreadLog.PerChannel + 2)));
        }

        [Test]
        public void AStatementIsNotAnOpenQuestion()
        {
            var log = new ChatThreadLog();
            log.Record("faction:Albion", 7, "Aldwin", "finally dinged 30", "boast", 1_000);
            Assert.That(log.OpenQuestion("faction:Albion", 2_000, 75_000), Is.Null);
        }

        [Test]
        public void EveryFollowUpWordHasGeneralReplies()
        {
            foreach (string word in new[] { "why", "really", "how", "what", "who" })
                Assert.That(Corpus.Lines.Count(l => l.Kind == "reply" && l.Answers.Contains("followup_" + word) && l.Topic == "general"),
                    Is.GreaterThanOrEqualTo(5), word);
        }

        #endregion

        #region Combat calls

        private static AutonomousBotCombatCalls.GroupView View(bool fighting, int enemies, int members = 4, int alive = 4, bool worn = false) =>
            new(fighting, enemies, members, alive, worn);

        [Test]
        public void AFightStartsWithAPullCallAndAnAddIsCalled()
        {
            var state = new AutonomousBotCombatCalls.GroupState();
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 1), 1_000, Always), Is.EqualTo("pull"));
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 1), 2_000, Always), Is.Null, "same fight, nothing new");
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 2), 3_000, Always), Is.EqualTo("add"));
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 3), 5_000, Always), Is.Null, "an add a moment later is not called again");
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 4), 25_000, Always), Is.EqualTo("add"));
        }

        [Test]
        public void AWipeIsCalledOnceWhenHalfTheGroupDies()
        {
            var state = new AutonomousBotCombatCalls.GroupState();
            AutonomousBotCombatCalls.GroupEvent(state, View(true, 2, 6, 6), 1_000, Never);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 2, 6, 4), 2_000, Always), Is.Null);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 2, 6, 3), 3_000, Always), Is.EqualTo("wipe"));
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 2, 6, 2), 4_000, Always), Is.Null);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(false, 0, 6, 2), 9_000, Always), Is.Null, "no gj after a wipe");
        }

        [Test]
        public void AGroupThatStartsAFightWithItsDeadIsNotWiping()
        {
            var state = new AutonomousBotCombatCalls.GroupState();
            AutonomousBotCombatCalls.GroupEvent(state, View(true, 1, 4, 2), 1_000, Never);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(true, 1, 4, 2), 2_000, Always), Is.Null);
        }

        [Test]
        public void AFightEndsWithGjOrARest()
        {
            var state = new AutonomousBotCombatCalls.GroupState();
            AutonomousBotCombatCalls.GroupEvent(state, View(true, 1), 1_000, Never);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(false, 0), 20_000, Always), Is.EqualTo("kill"));
            AutonomousBotCombatCalls.GroupEvent(state, View(true, 1), 30_000, Never);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(false, 0, worn: true), 50_000, Always), Is.EqualTo("rest"));
            AutonomousBotCombatCalls.GroupEvent(state, View(true, 1), 60_000, Never);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(state, View(false, 0), 80_000, Never), Is.Null, "most fights end quietly");
        }

        [Test]
        public void OomIsCalledInAFightOnly()
        {
            var state = new AutonomousBotCombatCalls.BotState();
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, false, 90, 5, true, 1_000, Always), Is.Null, "rezzed with no power, no fight");
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 90, 5, true, 2_000, Always), Is.EqualTo("oom"));
        }

        [TestCase("incoming", 1, true)]
        [TestCase("pulling, get set", 1, false)]
        [TestCase("ready? pulling now", 1, false)]
        [TestCase("bringing it in", 1, false)]
        [TestCase("inc 1", 1, true)]
        [TestCase("inc 1", 2, false)]
        [TestCase("inc, two of them", 2, true)]
        [TestCase("inc, two of them", 3, false)]
        [TestCase("another one, add!", 2, true)]
        [TestCase("one coming, on me", 1, true)]
        public void APullOrAddLineFitsTheFight(string text, int enemies, bool fits) =>
            Assert.That(AutonomousBotCombatCalls.PullLineFits(text, enemies), Is.EqualTo(fits));

        [Test]
        public void MezRootAndStunAreCalledOnceTheyLand()
        {
            Assert.That(AutonomousBotCombatCalls.NeedsLanding("mez"), Is.True);
            Assert.That(AutonomousBotCombatCalls.NeedsLanding("root"), Is.True);
            Assert.That(AutonomousBotCombatCalls.NeedsLanding("stun"), Is.True);
            Assert.That(AutonomousBotCombatCalls.NeedsLanding("heal_big"), Is.False, "a group heal is called as it is cast");
            Assert.That(AutonomousBotCombatCalls.NeedsLanding("rez"), Is.False);
        }

        [Test]
        public void TheNewDefaultsReachAWorldOnce()
        {
            var oldWorld = new Dictionary<string, (string Value, string DefaultValue)>
            {
                ["bot_voices_enabled"] = ("False", "False"),   // the server wrote the old default
                ["bot_living_memory"] = ("False", ""),          // the launcher wrote it: the player's choice
                ["bot_living_grats"] = ("True", "False"),
            };
            Assert.That(DOL.GS.ServerProperties.LivingServerSettingsUpgrade.Plan("", oldWorld), Is.EqualTo(new[] { "bot_voices_enabled" }));
            Assert.That(DOL.GS.ServerProperties.LivingServerSettingsUpgrade.Plan("1.1", oldWorld), Is.Empty, "once only");
            Assert.That(DOL.GS.ServerProperties.LivingServerSettingsUpgrade.Plan("", new Dictionary<string, (string, string)>()), Is.Empty);
        }

        [Test]
        public void LowHealthAndOomAreCalledOnTheWayDownOnly()
        {
            var state = new AutonomousBotCombatCalls.BotState();
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 25, 80, true, 100_000, Always), Is.EqualTo("low_hp"));
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 20, 80, true, 101_000, Always), Is.Null, "still low");
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 60, 80, true, 102_000, Always), Is.Null);
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 25, 80, true, 120_000, Always), Is.Null, "too soon after the last call");
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 60, 80, true, 150_000, Always), Is.Null);
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 25, 80, true, 151_000, Always), Is.EqualTo("low_hp"));
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 90, 10, true, 152_000, Always), Is.EqualTo("oom"));
            Assert.That(AutonomousBotCombatCalls.SelfEvent(state, true, 90, 5, true, 153_000, Always), Is.Null);
            Assert.That(AutonomousBotCombatCalls.SelfEvent(new AutonomousBotCombatCalls.BotState(), true, 90, 5, false, 1_000, Always), Is.Null,
                "a tank's power is not called");
            Assert.That(AutonomousBotCombatCalls.SelfEvent(new AutonomousBotCombatCalls.BotState(), false, 20, 90, true, 1_000, Always), Is.Null,
                "low health out of a fight is not a call");
        }

        [Test]
        public void TheSpellBeingCastNamesTheCall()
        {
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.Mesmerize, eSpellTarget.ENEMY, 0, 2), Is.EqualTo("mez"));
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.Mesmerize, eSpellTarget.ENEMY, 0, 1), Is.Null, "the mez lines say add");
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.SpeedDecrease, eSpellTarget.ENEMY, 99, 1), Is.EqualTo("root"));
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.SpeedDecrease, eSpellTarget.ENEMY, 40, 1), Is.Null, "a snare is not a root");
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.Heal, eSpellTarget.GROUP, 0, 1), Is.EqualTo("heal_big"));
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.Heal, eSpellTarget.REALM, 0, 1), Is.Null);
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.Resurrect, eSpellTarget.CORPSE, 0, 0), Is.EqualTo("rez"));
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.Stun, eSpellTarget.ENEMY, 0, 1), Is.EqualTo("stun"));
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.Taunt, eSpellTarget.ENEMY, 0, 1), Is.EqualTo("taunt"));
            Assert.That(AutonomousBotCombatCalls.CastTopic(eSpellType.DirectDamage, eSpellTarget.ENEMY, 0, 1), Is.Null);
        }

        [Test]
        public void CallsAreSpacedInTheGroupAndPerBot()
        {
            var group = new AutonomousBotCombatCalls.GroupState();
            var a = new AutonomousBotCombatCalls.BotState();
            var b = new AutonomousBotCombatCalls.BotState();
            Assert.That(AutonomousBotCombatCalls.Gate(group, a, "pull", 10_000), Is.True);
            Assert.That(AutonomousBotCombatCalls.Gate(group, b, "add", 11_000), Is.False, "the group just heard a call");
            Assert.That(AutonomousBotCombatCalls.Gate(group, b, "wipe", 11_000), Is.True, "a wipe call is never held back by the group gap");
            Assert.That(AutonomousBotCombatCalls.Gate(group, a, "low_hp", 16_000), Is.False, "this bot spoke 6 s ago");
            Assert.That(AutonomousBotCombatCalls.Gate(group, a, "low_hp", 19_000), Is.True);
        }

        [Test]
        public void EveryCombatCallHasLines()
        {
            foreach (string topic in new[] { "pull", "add", "low_hp", "oom", "wipe", "kill", "rest", "mez", "root", "stun", "heal_big", "rez", "taunt" })
                Assert.That(Corpus.Lines.Count(l => l.Kind == "combat" && l.Topic == topic), Is.GreaterThanOrEqualTo(6), topic);
            Assert.That(Corpus.Lines.Where(l => l.Kind == "combat").Select(l => l.Needs), Has.All.Contains("in_player_group"));
            Assert.That(Corpus.Lines.Where(l => l.Kind == "combat" && l.Topic == "rez").Select(l => l.Needs), Has.All.Contains("can_rez"));
        }

        #endregion

        #region Tells, channels and volume

        [Test]
        public void BotTellsAreRare()
        {
            for (int i = 0; i < 50; i++)
            {
                long first = AutonomousLivingServer.NextTellGap(true, Random.Shared);
                long later = AutonomousLivingServer.NextTellGap(false, Random.Shared);
                Assert.That(first, Is.InRange(4 * 60_000, 10 * 60_000));
                Assert.That(later, Is.InRange(25 * 60_000, 45 * 60_000));
            }
        }

        [Test]
        public void ADuoOfferIsForAPlayerOfAboutTheSameLevelAloneNearby()
        {
            Assert.That(AutonomousLivingServer.FitsGroupOffer(20, 21, false, true, 2_000), Is.True);
            Assert.That(AutonomousLivingServer.FitsGroupOffer(20, 24, false, true, 2_000), Is.False);
            Assert.That(AutonomousLivingServer.FitsGroupOffer(20, 20, true, true, 2_000), Is.False, "the player has a group");
            Assert.That(AutonomousLivingServer.FitsGroupOffer(20, 20, false, false, 2_000), Is.False, "another zone");
            Assert.That(AutonomousLivingServer.FitsGroupOffer(3, 3, false, true, 100), Is.False, "too new to be asked");
            Assert.That(AutonomousLivingServer.FitsHelpOffer(30, 10, true, 1_000), Is.True);
            Assert.That(AutonomousLivingServer.FitsHelpOffer(15, 10, true, 1_000), Is.False);
            Assert.That(AutonomousLivingServer.FitsHelpOffer(40, 25, true, 1_000), Is.False);
        }

        [Test]
        public void AnOfferThatSaysHereIsOnlyMadeInTheSameZone()
        {
            Assert.That(AutonomousLivingServer.OfferFits("hey, want to duo here? im {level} {class}", false), Is.False);
            Assert.That(AutonomousLivingServer.OfferFits("hi, im {level} {class}, want to duo in {zone}?", false), Is.False);
            Assert.That(AutonomousLivingServer.OfferFits("want to group? xp is better with two", false), Is.True);
            Assert.That(AutonomousLivingServer.OfferFits("hey, want to duo here? im {level} {class}", true), Is.True);
        }

        [Test]
        public void EveryTellTopicHasLinesAndOffersNeverPromiseAnInvite()
        {
            foreach (string topic in new[] { "hello_friend", "after_group", "group_offer", "help_offer", "news_tell", "guildmate" })
                Assert.That(Corpus.Lines.Count(l => l.Kind == "tell" && l.Topic == topic), Is.GreaterThanOrEqualTo(5), topic);
            Assert.That(Corpus.Lines.Where(l => l.Kind == "tell").Select(l => l.Text),
                Has.None.Contains("sending an invite").And.None.Contains("ill invite").And.None.Contains("i'll invite"));
        }

        [Test]
        public void InviteLinesAreDormantBecauseABotCannotInviteAPlayer()
        {
            var invites = Corpus.Lines.Where(l => l.Kind == "lfg" && l.Topic == "invite").ToArray();
            Assert.That(invites, Is.Not.Empty);
            Assert.That(invites.Select(l => l.Needs), Has.All.Contains("recruiting"));
        }

        [Test]
        public void AGroupHelloExistsForJoiningThePlayersGroup()
        {
            Assert.That(Corpus.Lines.Count(l => l.Kind == "lfg" && l.Topic == "join" && l.Needs.Contains("in_player_group")),
                Is.GreaterThanOrEqualTo(10));
        }

        [Test]
        public void FactionChatIsBusierInTheEveningAndWithMoreBots()
        {
            var fixedDraw = new FixedRandom(0.5);
            long normal = AutonomousBotChatCoordinator.FactionGapMs(120, 14, 1.0, fixedDraw);
            Assert.That(normal, Is.EqualTo(120_000));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(120, 21, 1.0, fixedDraw), Is.LessThan(normal));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(120, 4, 1.0, fixedDraw), Is.GreaterThan(normal));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(400, 14, 1.0, fixedDraw), Is.LessThan(normal));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(20, 14, 1.0, fixedDraw), Is.GreaterThan(normal));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(120, 14, 2.0, fixedDraw), Is.LessThan(normal));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(10_000, 21, 3.0, fixedDraw), Is.GreaterThanOrEqualTo(40_000), "never a flood");
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(120, 14, 0, fixedDraw), Is.GreaterThan(TimeSpan.FromDays(365).TotalMilliseconds),
                "rate 0: bots only answer");
            Assert.That(AutonomousBotChatCoordinator.LocalGapMs(14, 1.0, fixedDraw), Is.EqualTo(60_000));
            // The longest draw too (the fixed draw above is the shortest).
            var longest = new LongestRandom();
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(120, 14, 1.0, longest), Is.EqualTo(240_000));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(120, 4, 1.0, longest), Is.LessThanOrEqualTo(20 * 60_000));
            Assert.That(AutonomousBotChatCoordinator.FactionGapMs(10, 4, 0.1, longest), Is.EqualTo(20 * 60_000), "never silent for hours");
            Assert.That(AutonomousBotChatCoordinator.LocalGapMs(14, 0, fixedDraw), Is.GreaterThan(TimeSpan.FromDays(365).TotalMilliseconds));
        }

        #endregion
    }
}
