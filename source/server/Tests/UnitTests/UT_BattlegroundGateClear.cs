using NUnit.Framework;
using Rally = DOL.GS.AutonomousBattlegroundKeepRally;

namespace DOL.UnitTests
{
    /// <summary>
    /// The battleground storm goes with the whole realm, marches on the gate as a pack, clears the gate's guards together and
    /// goes on while it kills them (owner, 2026-11-16: "make the attack work the way a real Thidranki/Murdaigean zerg did").
    /// The cases are the shapes of the owner's PC logs, builds 4d48f42 (05:39-07:56) and c3cd93c (07:59-08:25): 111 attempts,
    /// the lord never below 96%, storms of 8-17 of 11-26 inside, 105 of 106 attempts on 4d48f42 over "half the stormers fell"
    /// after a median 1.2 minutes, the fallen outside 315, gate 215, inside 174, lordFloor 14, killed by guards 471 of 718,
    /// each stormer alone against a GuardFighter at the gate. Tests only: none of this has been seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundGateClear
    {
        // BG_RALLY_STORM lines of 4d48f42 (going, inside, minutes gathering, withOtherRealm): every one of them went.
        [TestCase(11, 22, 3.8, false)] // 05:57:08 Thidranki Midgard (needed 13)
        [TestCase(9, 24, 4.1, true)]   // 05:57:17 Thidranki Hibernia (needed 14)
        [TestCase(10, 17, 2.6, true)]  // 06:01:02 Murdaigean Hibernia (needed 10)
        [TestCase(8, 21, 1.2, true)]   // 06:06:08 Thidranki Hibernia (needed 12)
        [TestCase(9, 23, 3.7, true)]   // 06:13:58 Thidranki Midgard (needed 13)
        [TestCase(8, 22, 4.8, true)]   // 06:23:13 Murdaigean Hibernia (needed 13)
        public void TheHalfRealmStormsOf4d48f42NoLongerGo(int going, int inside, double minutes, bool otherRealm)
        {
            int needed = Rally.RallyNeeded(0, inside);
            Assert.That(Rally.StormRule(going, needed, (long)(minutes * 60_000), otherRealm), Is.Null,
                $"{going} of {inside} gather on for the muster of {needed}");
        }

        [TestCase(19, 22, 0L, false, "full")]
        [TestCase(16, 22, 0L, true, "joined")]           // 80% of 19 is 16, with the other realm's storm
        [TestCase(16, 22, 4 * 60_000L, false, "patient")]
        [TestCase(12, 22, 7 * 60_000L, false, "lastcall")] // 60% of 19 is 12
        [TestCase(11, 22, 7 * 60_000L, false, null)]
        [TestCase(15, 22, 4 * 60_000L, true, null)]
        public void TheStormSaysWhichRuleSentIt(int rallied, int inside, long gathering, bool otherRealm, string rule) =>
            Assert.That(Rally.StormRule(rallied, Rally.RallyNeeded(0, inside), gathering, otherRealm), Is.EqualTo(rule));

        [Test]
        public void TheGateGuardsAreTheOnesPostedOnTheGatesFloor()
        {
            Assert.Multiple(() =>
            {
                // 4d48f42 fight samples: GuardFighters 218-798 from the gate, 15-43 above the bot.
                Assert.That(Rally.IsGateGuard(false, 485, 15), Is.True);
                Assert.That(Rally.IsGateGuard(false, Rally.GateGuardRadius, -Rally.GateGuardHeight), Is.True);
                Assert.That(Rally.IsGateGuard(false, Rally.GateGuardRadius + 1, 0), Is.False, "the courtyard's far side");
                Assert.That(Rally.IsGateGuard(false, 300, 400), Is.False, "an archer up on the wall");
                Assert.That(Rally.IsGateGuard(true, 0, 0), Is.False, "never the lord");
            });
        }

        [Test]
        public void TheWholeStormFightsOneCalledGuard()
        {
            var guards = new[]
            {
                new Rally.GateGuardFact(11, 300, false),
                new Rally.GateGuardFact(12, 700, true),
                new Rally.GateGuardFact(13, 500, true),
            };
            Assert.Multiple(() =>
            {
                Assert.That(Rally.CallGateGuard(guards, 0), Is.EqualTo(2), "the one on a stormer, nearest the gate");
                Assert.That(Rally.CallGateGuard(guards, 12), Is.EqualTo(1), "the called guard stays called while it stands");
                Assert.That(Rally.CallGateGuard(guards, 99), Is.EqualTo(2), "the called guard fell: the next");
                Assert.That(Rally.CallGateGuard(new[] { new Rally.GateGuardFact(5, 900, false), new Rally.GateGuardFact(6, 400, false) }, 0),
                    Is.EqualTo(1), "none on a stormer: the nearest the gate");
                Assert.That(Rally.CallGateGuard(new Rally.GateGuardFact[0], 0), Is.EqualTo(-1));
            });
        }

        [Test]
        public void TheClearHoldsTheStormAtTheGateUntilItsGuardsAreDownOrItStalls()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.ClearHolds(3, 0), Is.True);
                Assert.That(Rally.ClearHolds(3, Rally.ClearStallMilliseconds - 1), Is.True);
                Assert.That(Rally.ClearHolds(3, Rally.ClearStallMilliseconds), Is.False, "nothing fell for 45 s: in, as before");
                Assert.That(Rally.ClearHolds(0, 0), Is.False, "the gate is clear");
                Assert.That(Rally.InClearZone(1_514, false), Is.True, "the nearest march sample of 4d48f42's seconds 0-49");
                Assert.That(Rally.InClearZone(1_898, false), Is.False, "their median: still on the march");
                Assert.That(Rally.InClearZone(300, true), Is.False, "through the gate already");
            });
        }

        [Test]
        public void MeleeStrikeCastersStrikeFromRangeHealersStandBack()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.ClearAction(false, false, 900), Is.EqualTo(Rally.ClearStep.Strike), "melee walks to it in the fight");
                Assert.That(Rally.ClearAction(false, true, 1_000), Is.EqualTo(Rally.ClearStep.Strike), "a caster within its reach");
                Assert.That(Rally.ClearAction(false, true, 1_600), Is.EqualTo(Rally.ClearStep.Approach), "a caster out of reach closes in");
                Assert.That(Rally.ClearAction(true, false, 600), Is.EqualTo(Rally.ClearStep.StandBack), "a healer stays behind");
                Assert.That(Rally.ClearAction(true, true, 1_500), Is.EqualTo(Rally.ClearStep.Approach), "a healer too far to heal");
            });
        }

        [Test]
        public void ALeaderWaitsForItsPackOnTheMarch()
        {
            Assert.Multiple(() =>
            {
                // 4d48f42: the march at seconds 0-49 stood 1,514-2,697 from the gate (median 1,898).
                Assert.That(Rally.MarchHolds(1_700, 2_400, false, 10_000), Is.True, "700 ahead of the pack");
                Assert.That(Rally.MarchHolds(2_000, 2_400, false, 10_000), Is.False, "within the pack's spread");
                Assert.That(Rally.MarchHolds(1_700, 2_400, true, 10_000), Is.False, "never while it fights");
                Assert.That(Rally.MarchHolds(1_700, 2_400, false, Rally.MarchWindowMilliseconds), Is.False, "nothing waits for ever");
                Assert.That(Rally.MarchHolds(1_500, 2_400, false, 10_000), Is.False, "at the gate: it clears");
                Assert.That(Rally.MarchHolds(1_700, -1, false, 10_000), Is.False, "no pack counted yet");
                Assert.That(Rally.Median(new float[] { 2_697, 1_514, 1_898 }), Is.EqualTo(1_898));
                Assert.That(Rally.Median(new float[0]), Is.EqualTo(-1));
            });
        }

        [Test]
        public void AStormThatKillsGuardsGoesOn()
        {
            Assert.Multiple(() =>
            {
                // 4d48f42: 105 of 106 attempts "half the stormers fell", median 1.2 minutes, the lord at 96-100%.
                Assert.That(Rally.AttemptOver(12, 6, 100, 100, 60_000, 0, 10_000), Is.Null, "half fell, a guard fell 10 s ago");
                Assert.That(Rally.AttemptOver(12, 6, 100, 100, 60_000, 0, Rally.GuardProgressGraceMilliseconds), Is.EqualTo("half the stormers fell"));
                Assert.That(Rally.AttemptOver(12, 6, 100, 100, 60_000), Is.EqualTo("half the stormers fell"), "no guard ever fell");
                Assert.That(Rally.AttemptOver(12, 0, 100, 100, 60_000, 0, 1_000), Is.EqualTo("every stormer fell"), "a wipe is a wipe");
                Assert.That(Rally.AttemptOver(20, 20, 100, 100, Rally.NoProgressMilliseconds, 0, 60_000), Is.Null,
                    "three minutes without the lord, but the gate's guards are dying");
                Assert.That(Rally.AttemptOver(20, 20, 100, 100, Rally.NoProgressMilliseconds, 0, Rally.NoProgressMilliseconds),
                    Is.EqualTo("no progress on the lord"));
            });
        }

        [Test]
        public void TheCalledGuardIsFoughtFromAnywhereOnTheWay()
        {
            Assert.Multiple(() =>
            {
                // A guard 700 off, not on this bot: run past, unless the storm has called it.
                Assert.That(Rally.StormerMayEngage(false, false, false, 700, 0), Is.False);
                Assert.That(Rally.StormerMayEngage(false, false, false, 700, 0, calledGuard: true), Is.True);
                Assert.That(Rally.StormerMayEngage(false, false, true, 70, 15), Is.True, "hit back, as before");
            });
        }

        [Test]
        public void TheJointStormStaysButAtStrength()
        {
            Assert.Multiple(() =>
            {
                // A ready rally still holds for the other realm, now up to 90 s.
                Assert.That(Rally.HoldsForOtherRealm(60_000, true, 8, 18), Is.True);
                Assert.That(Rally.HoldsForOtherRealm(Rally.PartnerWaitMilliseconds, true, 8, 18), Is.False);
                // ...and the other realm then joins with 80% of its muster, not half.
                Assert.That(Rally.ShouldStorm(15, 18, 0, otherRealmStorming: true), Is.True);
                Assert.That(Rally.ShouldStorm(9, 18, 0, otherRealmStorming: true), Is.False);
            });
        }
    }
}
