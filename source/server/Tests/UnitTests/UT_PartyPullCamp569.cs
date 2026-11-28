using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3 (#569 step 9): the rest of the party puller's camp turn.
    /// A too-far target is passed over instead of the tank walking into the pack, and
    /// after 45 s the camp point moves in (at most twice per camp in 10 minutes); a puller beside its arrived leader is at
    /// the camp; and it waits with the party while the camp is empty.
    /// </summary>
    public class UT_PartyPullCamp569
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [TestCase(PartyPullStart.TooFar, false, PartyPullFallback.SkipTooFar)]
        [TestCase(PartyPullStart.ContactFight, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.NoRangedPull, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.Benched, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.NotApplicable, false, PartyPullFallback.OrdinaryPull)]
        [TestCase(PartyPullStart.Started, true, PartyPullFallback.Handled)]
        [TestCase(PartyPullStart.Waiting, true, PartyPullFallback.Handled)]
        [TestCase(PartyPullStart.Unreachable, true, PartyPullFallback.Handled)]
        public void TooFarIsNeverTheOrdinaryPull(PartyPullStart why, bool handled, PartyPullFallback expected) =>
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(handled, why, campTurn: true), Is.EqualTo(expected));

        [Test]
        public void SwitchedOffTheOldFallThroughStays() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(false, PartyPullStart.TooFar, false),
                Is.EqualTo(PartyPullFallback.OrdinaryPull));
            Assert.That(AutonomousPartyPullCamp.AfterPartyPull(true, PartyPullStart.Waiting, false),
                Is.EqualTo(PartyPullFallback.Handled));
        });

        [Test]
        public void TooFarIsBeyondTheShortApproachToAFiringPoint() => Assert.Multiple(() =>
        {
            // range 1,500: the approach is 450 past 1,400
            Assert.That(AutonomousDefensivePull.IsTooFarForFiringPoint(1_850, 1_500), Is.False);
            Assert.That(AutonomousDefensivePull.IsTooFarForFiringPoint(1_851, 1_500), Is.True);
        });

        [Test]
        public void APullerBesideAnArrivedLeaderHasArrived() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPartyPullCamp.PartyPullerHasArrived(1_000, 2_000, 2_000, 1_250, 500), Is.True, "inside the ring");
            Assert.That(AutonomousPartyPullCamp.PartyPullerHasArrived(1_400, 1_200, 300, 1_250, 500), Is.True, "beside the leader");
            Assert.That(AutonomousPartyPullCamp.PartyPullerHasArrived(1_400, 1_300, 300, 1_250, 500), Is.False, "the leader is outside");
            Assert.That(AutonomousPartyPullCamp.PartyPullerHasArrived(1_800, 1_200, 600, 1_250, 500), Is.False, "too far from the leader");
        });

        [Test]
        public void OnlyConsecutiveTooFarTurnsCountTowardTheAdvance() => Assert.Multiple(() =>
        {
            const long now = 1_000_000;
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(now - 30_000, true, now - 10_000, now - 20_000, now), Is.True);
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(0, true, now - 10_000, 0, now), Is.False, "no episode yet");
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(now - 30_000, false, now - 10_000, 0, now), Is.False, "new camp");
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(now - 30_000, true, now - 10_000, now - 5_000, now), Is.False,
                "the party fought since the last skip");
            Assert.That(AutonomousPartyPullCamp.TooFarEpisodeContinues(now - 100_000, true,
                now - AutonomousPartyPullCamp.TooFarEpisodeGapMilliseconds - 1, 0, now), Is.False, "a long gap");
            Assert.That(AutonomousPartyPullCamp.ShouldAdvanceAfterTooFar(44_999), Is.False);
            Assert.That(AutonomousPartyPullCamp.ShouldAdvanceAfterTooFar(45_000), Is.True);
            Assert.That(AutonomousPartyPullCamp.EmptyTurnEndsTooFarEpisode(now + 1, now), Is.False, "a skip is still pending");
            Assert.That(AutonomousPartyPullCamp.EmptyTurnEndsTooFarEpisode(now, now), Is.True);
        });

        [Test]
        public void AtMostTwoAdvancesPerCampInTenMinutes()
        {
            const long now = 10_000_000;
            var ticks = new List<long>();
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now), Is.True);
            ticks.Add(now - 60_000);
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now), Is.True);
            ticks.Add(now - 30_000);
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now), Is.False, "two in the window");
            Assert.That(AutonomousPartyPullCamp.CanAdvanceCamp(ticks, now - 60_000 + AutonomousPartyPullCamp.AdvanceWindowMilliseconds),
                Is.True, "the first one ages out");
            Assert.That(ticks, Has.Count.EqualTo(1));
        }

        [Test]
        public void TheEmptyCampClockKeepsRunningThroughTooFarSkips() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousPartyPullCamp.EmptyClockAfterTooFarSkip(0, 5_000), Is.EqualTo(5_000));
            Assert.That(AutonomousPartyPullCamp.EmptyClockAfterTooFarSkip(1_000, 5_000), Is.EqualTo(1_000));
            Assert.That(AutonomousPartyPullCamp.ShouldLogTooFar(0, 5_000), Is.True);
            Assert.That(AutonomousPartyPullCamp.ShouldLogTooFar(5_000, 5_000 + AutonomousPartyPullCamp.TooFarLogMilliseconds - 1), Is.False);
            Assert.That(AutonomousPartyPullCamp.ShouldLogTooFar(5_000, 5_000 + AutonomousPartyPullCamp.TooFarLogMilliseconds), Is.True);
        });

        [Test]
        public void OnlyOutdoorBotOnlyPvePartiesMoveTheirCampPoint() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(true, true, false, false, false, false, false), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(false, true, false, false, false, false, false), Is.False, "not group PvE");
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(true, false, false, false, false, false, false), Is.False, "another camp");
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(true, true, true, false, false, false, false), Is.False, "dungeon");
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(true, true, false, true, false, false, false), Is.False, "regrouping");
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(true, true, false, false, true, false, false), Is.False, "expedition");
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(true, true, false, false, false, true, false), Is.False, "hosts a player");
            Assert.That(AutonomousBotGroupCoordinator.MayAdvancePartyCamp(true, true, false, false, false, false, true), Is.False, "a player's group");
        });

        [Test]
        public void TheSwitchDefaultsOnAndShipsOn()
        {
            Assert.That(AutonomousPartyPullCamp.CampTurn, Is.True);
            string settings = Source("..", "..", "tools", "release-server-settings.json");
            Assert.That(settings, Does.Contain("\"bot_party_puller_camp_turn\": \"True\""));
        }

        [Test]
        public void TheCampTurnAsksWhyAndSkipsTooFarBeforeTheOrdinaryPull()
        {
            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            int start = controller.IndexOf("private bool WorkCamp(BotBrain brain, GameBot bot)");
            int ask = controller.IndexOf("AutonomousDefensivePull.TryBegin(bot, target, out partyPullWhy);", start);
            int skip = controller.IndexOf("return SkipTooFarPartyTarget(bot, target, emptySinceBefore, reportedEmptyBefore);", start);
            int ordinary = controller.IndexOf("BeginSavageMeleePull(bot, target);", start);
            int hold = controller.IndexOf("if (HoldPartyPullerAtEmptyCamp(bot))", start);
            int patrol = controller.IndexOf("PatrolCamp(bot);", start);
            Assert.That(ask, Is.GreaterThan(start));
            Assert.That(skip, Is.GreaterThan(ask));
            Assert.That(ordinary, Is.GreaterThan(skip), "the ordinary pull comes only after the too-far skip");
            Assert.That(hold, Is.GreaterThan(ordinary));
            Assert.That(patrol, Is.GreaterThan(hold), "the puller's hold comes before the patrol");
            Assert.That(controller, Does.Contain("!certifiedDarknessFallsCamp && !pullerArrivedWithLeader &&"),
                "a puller beside its arrived leader skips the walk to the camp");
        }

        [Test]
        public void TheBoolTryBeginIsTheReasonOverloadsBool()
        {
            string pull = Source("bots", "autonomous", "AutonomousDefensivePull.cs");
            Assert.That(pull, Does.Contain("public static bool TryBegin(GameBot shooter, GameLiving target)\n" +
                                           "            => TryBeginCore(shooter, target, flyingHandoff: false, out _);"));
            Assert.That(pull, Does.Contain("public static bool TryBegin(GameBot shooter, GameLiving target, out PartyPullStart why)\n" +
                                           "            => TryBeginCore(shooter, target, flyingHandoff: false, out why);"));
        }
    }
}
