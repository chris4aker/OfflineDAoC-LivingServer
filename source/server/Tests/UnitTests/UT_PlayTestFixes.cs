using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Fixes from the 2026-10-27 evening classic-client play test.</summary>
    [TestFixture]
    public class UT_PlayTestFixes
    {
        [Test]
        public void AHorseTicketQuestionWithoutADestinationIsAnswered()
        {
            Assert.That(AutonomousChatKnowledge.TryAnswerTicketsInGeneral("where can i buy a horse ticket?", out string answer), Is.True);
            Assert.That(answer, Does.Contain("stable"));
            Assert.That(AutonomousChatKnowledge.TryAnswerTicketsInGeneral("how do i get a ticket", out _), Is.True);
            Assert.That(AutonomousChatKnowledge.TryAnswerTicketsInGeneral("where is the stable master", out _), Is.True);
            Assert.That(AutonomousChatKnowledge.TryAnswerTicketsInGeneral("selling tickets cheap", out _), Is.False, "no question");
            Assert.That(AutonomousChatKnowledge.TryAnswerTicketsInGeneral("where do i level at 20?", out _), Is.False, "not about tickets");
        }

        [Test]
        public void AGroupCallNoBotFitsGetsAnHonestReply()
        {
            // "lvl 2 viking lfg pst" with no bot under level 10 drew silence.
            for (int seed = 0; seed < 20; seed++)
            {
                string low = AutonomousBotChatCoordinator.NoneNearLevelReply(2, null, new System.Random(seed));
                string mid = AutonomousBotChatCoordinator.NoneNearLevelReply(25, null, new System.Random(seed));
                string healer = AutonomousBotChatCoordinator.NoneNearLevelReply(25, "healer", new System.Random(seed));
                Assert.That(low, Is.Not.Empty);
                Assert.That(mid, Does.Not.Contain("lowbie"));
                Assert.That(healer, Does.Contain("healer"));
                Assert.That(low + mid + healer, Does.Not.Contain("send me an invite"), "a bot that can't join never offers to");
            }
            Assert.That(AutonomousBotChatCoordinator.NoneNearLevelReplyChance, Is.InRange(0.5, 1.0));
        }

        [Test]
        public void OnlyAGroupmateNearThePlayerCallsTheFight()
        {
            // A groupmate across East Svealand said "assisting you" while the player fought alone near Mularn.
            Assert.That(AutonomousBotCombatCalls.SawTheFight(false, 800), Is.True);
            Assert.That(AutonomousBotCombatCalls.SawTheFight(false, AutonomousBotCombatCalls.FightHearingRadius), Is.True);
            Assert.That(AutonomousBotCombatCalls.SawTheFight(false, 25_000), Is.False);
            Assert.That(AutonomousBotCombatCalls.SawTheFight(false, null), Is.False, "another region");
            Assert.That(AutonomousBotCombatCalls.SawTheFight(true, null), Is.True, "its own fight");
        }

        [Test]
        public void AGamebotWalkingBackTalksLikeAPlayer()
        {
            foreach (string stage in new[] { "release", "stable", "mount", "dismount", "arrive", "walk" })
                for (int seed = 0; seed < 6; seed++)
                {
                    string line = GameBot.GamebotReturnLine(stage, new System.Random(seed), "Mularn", "East Svealand");
                    Assert.That(line, Is.Not.Empty, stage);
                    Assert.That(line, Does.Not.Contain("formation").And.Not.Contain("I am"), stage);
                    Assert.That(line, Is.EqualTo(line.ToLowerInvariant().Replace("mularn", "Mularn").Replace("east svealand", "East Svealand")), stage);
                }
            Assert.That(GameBot.GamebotReturnLine("mount", new System.Random(1), null, null), Does.Not.Contain("{"));
        }

        [Test]
        public void ABotNamedMidFightAnswersAfterIt()
        {
            for (int seed = 0; seed < 6; seed++)
            {
                string line = AutonomousBotChatCoordinator.AfterAFight("sure, send me an invite", new System.Random(seed));
                Assert.That(line, Does.EndWith("sure, send me an invite"));
                Assert.That(line.Length, Is.GreaterThan("sure, send me an invite".Length));
            }
            Assert.That(AutonomousBotChatCoordinator.AfterAFight("ok", new System.Random(1), riding: true), Does.Contain("horse"));
            Assert.That(AutonomousBotChatCoordinator.NamedWhileFightingWaitMs, Is.InRange(30_000, 180_000));
        }

        [Test]
        public void NoGoodJobAfterAFightThePlayerDiedIn()
        {
            // "Gj." in /group just after the player died to a myrkcat.
            var lowRoll = new FixedRandom(0.01);
            var died = new AutonomousBotCombatCalls.GroupState();
            AutonomousBotCombatCalls.GroupEvent(died, new(true, 1, 2, 2, false), 1_000, lowRoll);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(died, new(false, 0, 2, 1, false, PlayerDown: true), 60_000, lowRoll), Is.Null);
            var won = new AutonomousBotCombatCalls.GroupState();
            AutonomousBotCombatCalls.GroupEvent(won, new(true, 1, 2, 2, false), 1_000, lowRoll);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(won, new(false, 0, 2, 2, false), 60_000, lowRoll), Is.EqualTo("kill"));
        }

        private sealed class FixedRandom : System.Random
        {
            private readonly double _value;
            public FixedRandom(double value) => _value = value;
            public override double NextDouble() => _value;
            public override int Next(int maxValue) => 0;
            public override int Next(int minValue, int maxValue) => minValue;
        }

        [Test]
        public void AGroupmateAskedToComeAnswersHonestly()
        {
            Assert.That(AutonomousBotGroupChatter.AsksToCome("can u come to mularn?"), Is.True);
            Assert.That(AutonomousBotGroupChatter.AsksToCome("meet me at the bridge"), Is.True);
            Assert.That(AutonomousBotGroupChatter.AsksToCome("u coming?"), Is.True);
            Assert.That(AutonomousBotGroupChatter.AsksToCome("going to connacht badgers, come with me?"), Is.True);
            Assert.That(AutonomousBotGroupChatter.AsksToCome("where should we hunt?"), Is.False);
            Assert.That(AutonomousBotGroupChatter.AsksToCome("welcome to the group"), Is.False);
            for (int seed = 0; seed < 6; seed++)
            {
                Assert.That(AutonomousBotGroupChatter.ComeReply(false, int.MaxValue, "Aegir's Landing", new System.Random(seed)),
                    Does.Contain("Aegir's Landing"), "another region: it says where it is");
                Assert.That(AutonomousBotGroupChatter.ComeReply(false, int.MaxValue, null, new System.Random(seed)), Does.Not.Contain("{"));
                Assert.That(AutonomousBotGroupChatter.ComeReply(true, 500, "Vale of Mularn", new System.Random(seed)), Does.Not.Contain("cant"));
            }
        }

        [Test]
        public void AGamebotGroupmateBesideThePlayerGoesThroughThePortalToo()
        {
            int r = TemporaryGroupStableTravel.GroupmateTravelRadius;
            Assert.That(TemporaryGroupStableTravel.WasBesideDeparture(100, 1_000, 1_000, 100, 1_200, 1_100, true), Is.True);
            Assert.That(TemporaryGroupStableTravel.WasBesideDeparture(100, 0, 0, 100, r, 0, true), Is.True);
            Assert.That(TemporaryGroupStableTravel.WasBesideDeparture(100, 0, 0, 100, r + 1, 0, true), Is.False, "too far to have gone along");
            Assert.That(TemporaryGroupStableTravel.WasBesideDeparture(151, 1_000, 1_000, 100, 1_000, 1_000, true), Is.False, "another region");
            Assert.That(TemporaryGroupStableTravel.WasBesideDeparture(100, 1_000, 1_000, 100, 1_000, 1_000, false), Is.False, "fighting or on a duty");
        }

        [Test]
        public void ADragonQuestionNamesTheRealmsDragon()
        {
            Assert.That(AutonomousChatKnowledge.TryAnswerDragon(eRealm.Midgard, "is it true theres a dragon in midgard?", out string mid), Is.True);
            Assert.That(mid, Does.Contain("Gjalpinulva").And.Contain("Malmohus"));
            Assert.That(AutonomousChatKnowledge.TryAnswerDragon(eRealm.Midgard, "where is the hib dragon", out string hib), Is.True);
            Assert.That(hib, Does.Contain("Sheeroe Hills"));
            Assert.That(AutonomousChatKnowledge.TryAnswerDragon(eRealm.Albion, "any dragons around here?", out string alb), Is.True);
            Assert.That(alb, Does.Contain("Golestandt").And.Contain("Dartmoor"));
            Assert.That(AutonomousChatKnowledge.TryAnswerDragon(eRealm.Albion, "killed a dragonfly lol", out _), Is.False);
            Assert.That(AutonomousChatKnowledge.TryAnswerDragon(eRealm.Albion, "the dragon raid was fun", out _), Is.False, "no question");
        }

        [Test]
        public void AnRvrStatusQuestionIsAnEnemyQuestion()
        {
            Assert.That(AutonomousChatShortTalk.QuestionShape("whats going on in rvr tonight?"), Is.EqualTo("ask_enemy"));
            Assert.That(AutonomousChatShortTalk.QuestionShape("anything happening in rvr?"), Is.EqualTo("ask_enemy"));
            Assert.That(AutonomousChatShortTalk.QuestionShape("whats going on in emain"), Is.EqualTo("ask_enemy"));
        }

        [Test]
        public void ALowLevelAskingWhereToLevelHearsTheStarterZones()
        {
            var random = new System.Random(3);
            Assert.That(AutonomousChatLiveAnswers.StarterZones(eRealm.Hibernia, 1, random), Does.Contain("Connacht"));
            Assert.That(AutonomousChatLiveAnswers.StarterZones(eRealm.Midgard, 3, random), Does.Contain("Mularn"));
            Assert.That(AutonomousChatLiveAnswers.StarterZones(eRealm.Albion, 2, random), Does.Contain("Camelot Hills"));
            Assert.That(AutonomousChatLiveAnswers.StarterZones(eRealm.Albion, 12, random), Is.Null, "past the starter zones");
        }

        [Test]
        public void HuntingTalkWaitsForAFight()
        {
            Assert.That(AutonomousBotCombatCalls.IsHuntTalk("you have the patience of a saint with these pulls"), Is.True);
            Assert.That(AutonomousBotCombatCalls.IsHuntTalk("Loot is yours, I don't need it."), Is.True);
            Assert.That(AutonomousBotCombatCalls.IsHuntTalk("good spot, this camp suits us"), Is.True);
            Assert.That(AutonomousBotCombatCalls.IsHuntTalk("glad i grouped with you, honestly"), Is.False);
            Assert.That(AutonomousBotCombatCalls.IsHuntTalk("brb, door"), Is.False);
            Assert.That(AutonomousBotCombatCalls.FoughtRecently(null, 0), Is.False);
        }

        [Test]
        public void TeleMobPrefersTheCampNearThePlayersLevel()
        {
            Assert.That(DOL.GS.Commands.PlayerMobNavigator.CampLevelGap(3, 4, 3), Is.EqualTo(0));
            Assert.That(DOL.GS.Commands.PlayerMobNavigator.CampLevelGap(27, 28, 3), Is.EqualTo(24), "the level 27 myrkcats");
            Assert.That(DOL.GS.Commands.PlayerMobNavigator.CampLevelGap(1, 2, 5), Is.EqualTo(3));
            Assert.That(DOL.GS.Commands.PlayerMobNavigator.CampLevelGap(5, 5, 1), Is.LessThan(DOL.GS.Commands.PlayerMobNavigator.CampLevelGap(27, 27, 1)));
        }

        [Test]
        public void HowLongToFiftyIsAnswered()
        {
            Assert.That(AutonomousChatKnowledge.TryAnswerTimeToCap("how long does it take to hit 50?", out string answer), Is.True);
            Assert.That(answer, Does.Contain("played"));
            Assert.That(AutonomousChatKnowledge.TryAnswerTimeToCap("how long to max level", out _), Is.True);
            Assert.That(AutonomousChatKnowledge.TryAnswerTimeToCap("how long is the boat ride", out _), Is.False);
            Assert.That(AutonomousChatKnowledge.TryAnswerTimeToCap("50 scout lfg", out _), Is.False);
        }

        [Test]
        public void ABuyPostIsNotAnsweredAsASale()
        {
            const string wtb = "wtb a better staff for a lvl 1 mage";
            Assert.That(AutonomousChatShortTalk.TradeReplyFits(wtb, "how much are you asking?"), Is.False);
            Assert.That(AutonomousChatShortTalk.TradeReplyFits(wtb, "pst me the stats"), Is.False);
            Assert.That(AutonomousChatShortTalk.TradeReplyFits(wtb, "check the exchange"), Is.True);
            Assert.That(AutonomousChatShortTalk.TradeReplyFits("wts nice sword", "how much are you asking?"), Is.True);
            Assert.That(AutonomousChatShortTalk.TradeReplyFits("wts nice sword", "wts or wtb?"), Is.False);
            Assert.That(AutonomousChatShortTalk.TradeReplyFits("anyone craft?", "pst me what you need made"), Is.False, "gamebots don't craft");
        }

        [Test]
        public void ADeclinedRezIsNotOfferedAgainForAMinute()
        {
            Assert.That(BotGroupSupport.RecentlyDeclined(0, 500_000), Is.False, "never declined");
            Assert.That(BotGroupSupport.RecentlyDeclined(100_000, 130_000), Is.True);
            Assert.That(BotGroupSupport.RecentlyDeclined(100_000, 100_000 + BotGroupSupport.DeclinedRezPauseMs), Is.False);
        }

        [Test]
        public void ARaidQuestionIsAnsweredFromTheLiveRaids()
        {
            Assert.That(AutonomousChatKnowledge.AsksAboutRealmRaid("any hibs doing a dungeon run tonight?"), Is.True);
            Assert.That(AutonomousChatKnowledge.AsksAboutRealmRaid("is there a raid going"), Is.True);
            Assert.That(AutonomousChatKnowledge.AsksAboutRealmRaid("that raid was fun"), Is.False);
            var raids = new[]
            {
                new AutonomousRealmRaid.Summary("epic-hibernia", "Galladoria", "Hibernia", "RAID", 174, 116, 168, 0, "Battle"),
                new AutonomousRealmRaid.Summary("dragon-albion", "Golestandt", "Albion", "Respawning", 0, 0, 0, 0),
            };
            Assert.That(AutonomousChatKnowledge.TryAnswerRealmRaid("Hibernia", 1, raids, out string hib), Is.True);
            Assert.That(hib, Does.Contain("Galladoria").And.Contain("116").And.Contain("50 thing"));
            Assert.That(AutonomousChatKnowledge.TryAnswerRealmRaid("Albion", 50, raids, out string alb), Is.True);
            Assert.That(alb, Does.Contain("nothing running"));
        }

        [Test]
        public void InsultingABotsGuildIsAbuse()
        {
            Assert.That(AutonomousChatIntentModel.Predict("ur guild sucks lol"), Is.EqualTo(eAutonomousChatIntent.Abuse));
            Assert.That(AutonomousChatIntentModel.Predict("your guild is a joke"), Is.EqualTo(eAutonomousChatIntent.Abuse));
            Assert.That(AutonomousChatIntentModel.Predict("any guilds recruiting newbies?"), Is.Not.EqualTo(eAutonomousChatIntent.Abuse));
        }

        [Test]
        public void AMerchantInteractionSpotIsSearchedOnlyNearTheMerchant()
        {
            // Issue #25: distant warbands ran 18 multi-zone route probes every 15 seconds and stalled NpcService.
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(150), Is.True);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(AutonomousSupplyMerchantSpace.SpotSearchRange), Is.True);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(AutonomousSupplyMerchantSpace.SpotSearchRange + 1), Is.False);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(110_000), Is.False);
            Assert.That(AutonomousSupplyMerchantSpace.ShouldResolveSpot(int.MaxValue), Is.False, "another region");
            Assert.That(AutonomousSupplyMerchantSpace.RetryAfterFailureMs, Is.GreaterThanOrEqualTo(15_000));
        }

        [Test]
        public void APlayerIsRaisedOnlyWhereNothingWillHitThem()
        {
            // Play test 2026-10-28: raised twice into a level 25 camp, killed again within two seconds each time.
            Assert.That(BotGroupSupport.SafeToRaisePlayer(0, 0), Is.True);
            Assert.That(BotGroupSupport.SafeToRaisePlayer(1, 0), Is.False, "a hostile still fighting beside the caster");
            Assert.That(BotGroupSupport.SafeToRaisePlayer(0, 1), Is.False, "an aggressive hostile in aggro range of the caster");
            Assert.That(BotGroupSupport.RaiseDangerRadius, Is.InRange(800, 2_000));
        }

        [Test]
        public void ARaiseSpotIsClearOfFightsAndAggroRange()
        {
            // In-game check 2026-10-28: in a camp that respawns, the cleric rezzes from its edge instead of clearing forever.
            var camp = new[]
            {
                new BotGroupSupport.RaiseHostile(new System.Numerics.Vector3(0, 0, 0), false, 800),
                new BotGroupSupport.RaiseHostile(new System.Numerics.Vector3(300, 0, 0), false, 800),
            };
            Assert.That(BotGroupSupport.PointIsSafeToRaise(new System.Numerics.Vector3(100, 0, 0), camp), Is.False, "inside the camp");
            Assert.That(BotGroupSupport.PointIsSafeToRaise(new System.Numerics.Vector3(-900, 0, 0), camp), Is.True, "past every aggro reach");
            var fight = new[] { new BotGroupSupport.RaiseHostile(new System.Numerics.Vector3(0, 0, 0), true, 0) };
            Assert.That(BotGroupSupport.PointIsSafeToRaise(new System.Numerics.Vector3(BotGroupSupport.RaiseDangerRadius - 10, 0, 0), fight), Is.False);
            Assert.That(BotGroupSupport.PointIsSafeToRaise(new System.Numerics.Vector3(BotGroupSupport.RaiseDangerRadius + 10, 0, 0), fight), Is.True);
            Assert.That(BotGroupSupport.PointIsSafeToRaise(new System.Numerics.Vector3(0, 0, 0), new BotGroupSupport.RaiseHostile[0]), Is.True);
        }
    }
}
