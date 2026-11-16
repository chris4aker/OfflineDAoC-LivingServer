using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Living Server body language (bot_living_body_language) and the flyer pull fix from the 2026-10-18 play logs.</summary>
    [TestFixture]
    public class UT_BodyLanguage
    {
        [TestCase(BodySpot.Town, 0.0, 8_000)]
        [TestCase(BodySpot.Town, 0.999, 24_983)]
        [TestCase(BodySpot.Waiting, 0.0, 10_000)]
        [TestCase(BodySpot.Resting, 0.5, 21_000)]
        [TestCase(BodySpot.Town, 7.0, 24_999)]     // a bad roll is clamped, never a burst
        public void IdleActionsAreSecondsApart(BodySpot spot, double roll, long expected) =>
            Assert.That(AutonomousBotBodyLanguage.NextDelayMs(spot, roll), Is.EqualTo(expected).Within(1));

        [Test]
        public void ADueGreetingUsuallyWinsWithAWaveBowOrSalute()
        {
            BodyChoice greet = AutonomousBotBodyLanguage.Choose(BodySpot.Town, greetDue: true, resting: false, 0.1, 0.0, 0.5);
            Assert.That(greet.Move, Is.EqualTo(BodyMove.Greet));
            Assert.That(greet.Emote, Is.EqualTo(eEmote.Wave));
            Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Town, true, false, 0.7, 0.0, 0.5).Move,
                Is.Not.EqualTo(BodyMove.Greet), "40% of the time the bot just carries on");
            Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Town, false, false, 0.1, 0.0, 0.5).Move, Is.EqualTo(BodyMove.Look));
        }

        [Test]
        public void TownHasLooksEmotesAndStepsButARestingBotNeverSteps()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Town, false, false, 0.2, 0.5, 0.5).Move, Is.EqualTo(BodyMove.Look));
                Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Town, false, false, 0.7, 0.5, 0.5).Move, Is.EqualTo(BodyMove.Emote));
                BodyChoice step = AutonomousBotBodyLanguage.Choose(BodySpot.Town, false, false, 0.9, 0.5, 0.5);
                Assert.That(step.Move, Is.EqualTo(BodyMove.Shuffle));
                Assert.That(step.StepLength, Is.InRange(AutonomousBotBodyLanguage.ShuffleMinimumStep, 60));
                for (double r1 = 0; r1 < 1; r1 += 0.05)
                    Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Town, false, true, r1, 0.5, 0.5).Move,
                        Is.Not.EqualTo(BodyMove.Shuffle), $"resting, roll {r1}");
            });
        }

        [Test]
        public void EmotesAreTheMinorityOfIdleActions()
        {
            int emotes = 0, total = 0;
            foreach (BodySpot spot in new[] { BodySpot.Town, BodySpot.Waiting })
                for (double r1 = 0.005; r1 < 1; r1 += 0.01, total++)
                    if (AutonomousBotBodyLanguage.Choose(spot, false, false, r1, 0.5, 0.5, allowStep: false).Move == BodyMove.Emote)
                        emotes++;
            Assert.That(emotes, Is.InRange(total / 5, total / 4), "about one action in four or five is an emote");
        }

        [Test]
        public void ABotJustStandingAroundLooksAndEmotesButNeverSteps()
        {
            for (double r1 = 0; r1 < 1; r1 += 0.05)
                Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Town, false, false, r1, 0.5, 0.5, allowStep: false).Move,
                    Is.AnyOf(BodyMove.Look, BodyMove.Emote), $"roll {r1}");
            Assert.That(AutonomousBotBodyLanguage.StandingStillMs, Is.InRange(3_000, 10_000));
        }

        [Test]
        public void WaitingAndRestingNeverStep()
        {
            for (double r1 = 0; r1 < 1; r1 += 0.05)
            {
                Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Waiting, false, false, r1, 0.3, 0.3).Move,
                    Is.AnyOf(BodyMove.Look, BodyMove.Emote));
                Assert.That(AutonomousBotBodyLanguage.Choose(BodySpot.Resting, false, false, r1, 0.3, 0.3).Move,
                    Is.AnyOf(BodyMove.Look, BodyMove.None));
            }
        }

        [Test]
        public void EveryEmoteIsOneTheNewClientAnimates()
        {
            // The new client builds animations for the classic emotes, ids 1-46 (pipeline/build_races.py).
            var all = AutonomousBotBodyLanguage.TownEmotes.Concat(AutonomousBotBodyLanguage.WaitEmotes)
                .Concat(AutonomousBotBodyLanguage.GreetEmotes).Concat(AutonomousBotBodyLanguage.CheerEmotes);
            Assert.That(all.Select(e => (int)e), Has.All.InRange(1, 46));
            Assert.That(AutonomousBotBodyLanguage.WaitEmotes, Does.Not.Contain(eEmote.Dance), "nobody dances while the group waits");
        }

        [TestCase(0.0, 0.2, -341)]    // 30 degrees left
        [TestCase(0.0, 0.8, 341)]     // 30 degrees right
        [TestCase(0.999, 0.8, 1695)]  // ~149 degrees
        public void LooksTurn30To150Degrees(double r2, double r3, int expected) =>
            Assert.That(AutonomousBotBodyLanguage.LookOffset(r2, r3), Is.EqualTo(expected));

        [TestCase((ushort)4000, 341, (ushort)245)]
        [TestCase((ushort)100, -341, (ushort)3855)]
        [TestCase((ushort)2048, 0, (ushort)2048)]
        public void TurnsWrapAroundTheCircle(ushort heading, int offset, ushort expected) =>
            Assert.That(AutonomousBotBodyLanguage.Turned(heading, offset), Is.EqualTo(expected));

        [Test]
        public void TownStepsStayNearTheIdleSpot()
        {
            var spot = new Vector3(30000, 30000, 2000);
            for (int i = 0; i < 64; i++)
            {
                double angle = i * 0.4;
                var from = spot + new Vector3((i % 9 - 4) * 15, (i % 7 - 3) * 15, 0);
                Vector3 to = AutonomousBotBodyLanguage.StepTarget(spot, from, angle, 60);
                Assert.That(Vector2.Distance(new(to.X, to.Y), new(spot.X, spot.Y)),
                    Is.LessThanOrEqualTo(AutonomousBotBodyLanguage.ShuffleRadius + 0.01), $"step {i}");
            }
            Assert.That(AutonomousBotBodyLanguage.ShuffleRadius, Is.LessThan(96), "the town break walks a bot back from 96 units");
        }

        [Test]
        public void TheSwitchIsOnByDefault() =>   // owner 2026-11-16: every Living Server feature on by default
            Assert.That(typeof(DOL.GS.ServerProperties.Properties).GetField("BOT_LIVING_BODY_LANGUAGE")
                .GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false)
                .Cast<DOL.GS.ServerProperties.ServerPropertyAttribute>().Single().DefaultValue, Is.EqualTo(true));

        [Test]
        public void ABotWaitingForAPartyOftenStandsBetweenPulls()
        {
            long until = 0, next = 0;
            Assert.That(AutonomousGroupMatchmakingPause.ShouldHold(true, 50, 1_000, ref until, ref next, 0.5, 0.0), Is.True,
                "a level 50 holds on a roll under 60%");
            Assert.That(until, Is.EqualTo(1_000 + AutonomousGroupMatchmakingPause.MinimumPauseMs));
            Assert.That(AutonomousGroupMatchmakingPause.ShouldHold(true, 50, 30_000, ref until, ref next, 0.99, 0.0), Is.True,
                "the pause lasts its full length");
            Assert.That(AutonomousGroupMatchmakingPause.ShouldHold(true, 50, until + 1, ref until, ref next, 0.0, 0.0), Is.False,
                "then the bot grinds at least a minute before the next roll");
            Assert.That(AutonomousGroupMatchmakingPause.ShouldHold(true, 50, next, ref until, ref next, 0.0, 1.0), Is.True);
            Assert.That(until - next + AutonomousGroupMatchmakingPause.GrindBetweenRollsMs, Is.EqualTo(0));
        }

        [Test]
        public void LevellingBotsWaitLessAndBotsNotWaitingNeverHold()
        {
            long until = 0, next = 0;
            Assert.That(AutonomousGroupMatchmakingPause.ShouldHold(true, 20, 0, ref until, ref next, 0.5, 0.5), Is.False,
                "a levelling bot holds only on a roll under 35%");
            Assert.That(next, Is.EqualTo(AutonomousGroupMatchmakingPause.GrindBetweenRollsMs));
            until = 999_999;
            Assert.That(AutonomousGroupMatchmakingPause.ShouldHold(false, 50, 0, ref until, ref next, 0.0, 0.0), Is.False,
                "a bot in a group (or not waiting) never holds, and its pause is cleared");
            Assert.That(until, Is.Zero);
            Assert.That(AutonomousGroupMatchmakingPause.MaximumPauseMs, Is.LessThan(15 * 60_000), "far inside the 15-minute stuck watchdog");
        }

        [TestCase("Offline level 50 Cleric 41/33", false)]
        [TestCase("offline level 50 Armsman 41/33", false)]
        [TestCase("Shadowwalker Leather Helm", true)]
        [TestCase("Offline Wanderer's Cloak", true)]
        [TestCase("  ", false)]
        public void StarterKitTemplatesAreNotAdvertisedInFaction(string name, bool advertised) =>
            Assert.That(AutonomousBotChatCoordinator.IsAdvertisableItemName(name), Is.EqualTo(advertised));

        // A melee bot must prove a ground approach to a flyer; casters and archers shoot it as before.
        [TestCase(GameNPC.eFlags.FLYING, false, true)]
        [TestCase(GameNPC.eFlags.FLYING, true, false)]
        [TestCase((GameNPC.eFlags)0, false, false)]
        public void MeleeBotsProveAFlyerCanBeReached(GameNPC.eFlags flags, bool ranged, bool mustProve) =>
            Assert.That(AutonomousPveTargetPolicy.MustProveFlyerApproach(flags, ranged), Is.EqualTo(mustProve));
    }
}
