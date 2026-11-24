using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Realism 4a (#487): the movement audit's small, visual-only fixes (BotIdleLife and its call sites).</summary>
    [TestFixture]
    public class UT_IdleLife
    {
        // ---- Tell 2: a standing follower's look is no longer snapped back --------------------------------------------

        [Test]
        public void AnArrivedFollowerOfAStandingLeaderKeepsEveryLookOnceItHasFacedTheLeader()
        {
            // Before the fix 119 of the 120 look angles (30-150 degrees either way) were past the 30-degree tolerance and
            // re-faced within 400 ms. Now none are, once the follower has faced its leader on arrival.
            int undone = 0, looks = 0;
            for (int degrees = 30; degrees < 150; degrees++)
                foreach (int sign in new[] { -1, 1 })
                {
                    looks++;
                    ushort heading = AutonomousBotBodyLanguage.Turned(0, sign * degrees * 4096 / 360);
                    bool pastTolerance = BotTurning.ShouldFace(heading, 0);
                    if (pastTolerance && !BotTurning.KeepsArrivedFollowHeading(true, standingFormation: true, facedOnArrival: true))
                        undone++;
                }
            Assert.That(looks, Is.EqualTo(240));
            Assert.That(undone, Is.Zero);
        }

        [TestCase(true, true, false, false, TestName = "first arrival faces the leader once")]
        [TestCase(true, false, true, false, TestName = "a walking leader is still faced (moving case unchanged)")]
        [TestCase(false, true, true, false, TestName = "a player's group is unchanged")]
        [TestCase(true, true, true, true, TestName = "settled follower of a standing leader keeps its heading")]
        public void FollowHeadingRule(bool ownParty, bool standing, bool faced, bool keeps) =>
            Assert.That(BotTurning.KeepsArrivedFollowHeading(ownParty, standing, faced), Is.EqualTo(keeps));

        [Test]
        public void ARealPlayerOrAPlainNpcNeverKeepsAFollowHeadingByThisRule() =>
            Assert.That(BotTurning.KeepsArrivedFollowHeading((GameNPC)null, true, true), Is.False);

        // ---- Tell 4: the rest seat is kept until the next action ------------------------------------------------------

        [Test]
        public void AFullRestKeepsTheSeatOnlyForAnIdleAutonomousGamebot()
        {
            Assert.That(BotIdleLife.KeepsSeatAfterRest(true, true, true, false, false, false, false), Is.True);
            Assert.That(BotIdleLife.KeepsSeatAfterRest(false, true, true, false, false, false, false), Is.False, "companions and a player's group: as before");
            Assert.That(BotIdleLife.KeepsSeatAfterRest(true, false, true, false, false, false, false), Is.False, "dead");
            Assert.That(BotIdleLife.KeepsSeatAfterRest(true, true, false, false, false, false, false), Is.False, "not seated");
            Assert.That(BotIdleLife.KeepsSeatAfterRest(true, true, true, true, false, false, false), Is.False, "a fight wakes it standing");
            Assert.That(BotIdleLife.KeepsSeatAfterRest(true, true, true, false, true, false, false), Is.False, "moving");
            Assert.That(BotIdleLife.KeepsSeatAfterRest(true, true, true, false, false, true, false), Is.False, "casting");
            Assert.That(BotIdleLife.KeepsSeatAfterRest(true, true, true, false, false, false, true), Is.False, "attacking");
        }

        [Test]
        public void StandingIsSpreadFromFourTenthsToOneAndAHalfSecondsAndOnlyWhenWatchedAndCalm()
        {
            var random = new Random(487);
            var values = Enumerable.Range(0, 2_000).Select(_ => BotIdleLife.StandSpreadMs(random.NextDouble())).ToArray();
            Assert.That(values.Min(), Is.GreaterThanOrEqualTo(400));
            Assert.That(values.Max(), Is.LessThanOrEqualTo(1_500));
            Assert.That(values.Distinct().Count(), Is.GreaterThan(500), "not one machine delay");
            Assert.That(BotIdleLife.StandSpreadMs(0), Is.EqualTo(400));
            Assert.That(BotIdleLife.StandSpreadMs(1), Is.EqualTo(1_500));
            Assert.That(BotIdleLife.SpreadsStanding(watched: true, combatBlocked: false), Is.True);
            Assert.That(BotIdleLife.SpreadsStanding(watched: false, combatBlocked: false), Is.False, "unwatched: today's pace");
            Assert.That(BotIdleLife.SpreadsStanding(watched: true, combatBlocked: true), Is.False, "a fight stands it at once");
        }

        [Test]
        public void TheSeatAnimationIsOneSwitchWithTheOriginalIds()
        {
            // The open PC check (#487): is 0xF0 drawn as a seat on the original client? Drop or swap it in BotIdleLife.
            Assert.That((int)BotIdleLife.SitAnimation, Is.EqualTo(0xF0));
            Assert.That((int)BotIdleLife.StandAnimation, Is.EqualTo(0xF1));
            Assert.That(BotIdleLife.ShowSeat, Is.True);
        }

        // ---- Tell 6: a standing party is not an exact ring --------------------------------------------------------------

        [Test]
        public void ASpreadRingKeepsThirtyTwoUnitsApartWithinItsBoundsForTwoToEightMembers()
        {
            var random = new Random(4870);
            int spread = 0, total = 0;
            for (int trial = 0; trial < 10_000; trial++)
            {
                int count = 2 + trial % 7;
                int radius = 64 + count * 3;
                long[] keys = Enumerable.Range(0, count).Select(_ => (long)random.Next(1, int.MaxValue)).ToArray();
                double rotation = random.Next(32) * Math.PI / 180d;
                Vector2[] slots = BotIdleLife.RingOffsets(keys, radius, rotation);
                Assert.That(slots, Has.Length.EqualTo(count));
                for (int i = 0; i < count; i++)
                {
                    float r = slots[i].Length();
                    Assert.That(r, Is.InRange(radius * 0.75f - 0.01f, radius * 1.35f + 0.01f));
                    double even = rotation + Math.PI * 2d * i / count;
                    double off = Math.Atan2(slots[i].Y, slots[i].X) - even;
                    off = Math.Atan2(Math.Sin(off), Math.Cos(off)) * 180d / Math.PI;
                    Assert.That(Math.Abs(off), Is.LessThanOrEqualTo(14.001));
                    for (int j = 0; j < i; j++)
                        Assert.That(Vector2.Distance(slots[i], slots[j]), Is.GreaterThanOrEqualTo(32f),
                            $"members {j} and {i} of {count} too close");
                    total++;
                    if (Math.Abs(r - radius) > 2 || Math.Abs(off) > 1)
                        spread++;
                }
            }
            Assert.That(spread, Is.GreaterThan(total * 8 / 10), "most members are off the exact ring");
        }

        [Test]
        public void TheSameMembersGetTheSameSpreadSlotsEveryTurn()
        {
            long[] keys = [11, 2_048, 77_001, 5, 900_000];
            Vector2[] first = BotIdleLife.RingOffsets(keys, 79, 0.3);
            Vector2[] again = BotIdleLife.RingOffsets(keys, 79, 0.3);
            Assert.That(again, Is.EqualTo(first));
            var radii = first.Select(slot => Math.Round(slot.Length())).Distinct().Count();
            Assert.That(radii, Is.GreaterThan(1), "not one radius for everybody");
        }

        // ---- Tell 7: emotes send their text ------------------------------------------------------------------------------

        [Test]
        public void ABotEmotePrintsThePlayerEmoteLineForEachViewer()
        {
            string[] wave = EmoteCommandHandler.MessagesFor(eEmote.Wave);
            Assert.That(wave, Is.Not.Null);
            Assert.That(BotIdleLife.EmoteLine(wave, "Brannoc", null, false), Is.EqualTo(string.Format(wave[1], "Brannoc")));
            Assert.That(BotIdleLife.EmoteLine(wave, "Brannoc", "Maeve", true), Is.EqualTo(string.Format(wave[3], "Brannoc", "you")));
            Assert.That(BotIdleLife.EmoteLine(wave, "Brannoc", "Maeve", false), Is.EqualTo(string.Format(wave[3], "Brannoc", "Maeve")));
            Assert.That(BotIdleLife.EmoteLine(wave, "Brannoc", "Maeve", true), Does.Contain("Brannoc").And.Contain("you"));
            Assert.That(BotIdleLife.EmoteLine(null, "Brannoc", null, false), Is.Null, "an animation with no player emote");
            Assert.That(EmoteCommandHandler.MessagesFor(BotIdleLife.SitAnimation), Is.Null, "the seat prints nothing");
        }

        [Test]
        public void EveryEmoteInTheBodyLanguagePoolsHasItsText()
        {
            IEnumerable<eEmote> pools = AutonomousBotBodyLanguage.TownEmotes.Concat(AutonomousBotBodyLanguage.WaitEmotes)
                .Concat(AutonomousBotBodyLanguage.GreetEmotes).Concat(AutonomousBotBodyLanguage.CheerEmotes).Distinct();
            foreach (eEmote emote in pools)
            {
                string[] lines = EmoteCommandHandler.MessagesFor(emote);
                Assert.That(lines, Has.Length.EqualTo(4), emote.ToString());
                Assert.That(BotIdleLife.EmoteLine(lines, "Brannoc", "Maeve", false), Does.Contain("Brannoc"), emote.ToString());
            }
        }

        // ---- Tell 8: a breather after a kill, only when watched --------------------------------------------------------

        [Test]
        public void ABreatherIsOneAndAHalfToSevenSecondsSkewedShort()
        {
            var random = new Random(8);
            int[] values = Enumerable.Range(0, 5_000).Select(_ => BotIdleLife.BreatherMs(random.NextDouble())).OrderBy(v => v).ToArray();
            Assert.That(values[0], Is.GreaterThanOrEqualTo(1_500));
            Assert.That(values[^1], Is.LessThanOrEqualTo(7_000));
            Assert.That(values[values.Length / 2], Is.LessThan(4_250), "skewed short");
            Assert.That(values.Distinct().Count(), Is.GreaterThan(1_000));
            Assert.That(values.Min(), Is.GreaterThan(900 + 499), "longer than today's 0.9-1.4 s");
        }

        [Test]
        public void ABreatherOnlyWhenAPlayerSeesTheBotAndNothingFights()
        {
            Assert.That(BotIdleLife.BreathesAfterKill(true, false, false, false, false, false), Is.True);
            Assert.That(BotIdleLife.BreathesAfterKill(false, false, false, false, false, false), Is.False, "unwatched: today's pace");
            Assert.That(BotIdleLife.BreathesAfterKill(true, true, false, false, false, false), Is.False, "an add");
            Assert.That(BotIdleLife.BreathesAfterKill(true, false, true, false, false, false), Is.False, "a party member fighting");
            Assert.That(BotIdleLife.BreathesAfterKill(true, false, false, true, false, false), Is.False, "a player's group");
            Assert.That(BotIdleLife.BreathesAfterKill(true, false, false, false, true, false), Is.False, "RvR");
            Assert.That(BotIdleLife.BreathesAfterKill(true, false, false, false, false, true), Is.False, "a raid");
        }
    }
}
