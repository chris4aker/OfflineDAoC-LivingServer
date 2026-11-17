using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidDungeonRoute;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's PC, build 453e01a, 2026-11-17 (sweep 4), Tuscaran Glacier, Icelord Agmundr (aggro 600, spawn 15037,50114):
    /// <list type="bullet">
    /// <item>Wipe 2: 159 stranded raiders were moved to the route's front, 15037,49914, 200 from his spawn, while the raid
    /// regrouped at its muster 15184,48849; he fought them at home (12:25, 12:30) and the second fight ended the raid.</item>
    /// <item>The 82 who went in at the way in at 12:24 joined a two-raider return wave at its step 35 of 49, which no corridor
    /// reached from there (atStep=0); after three failed routes each was moved to that front.</item>
    /// <item>Wipe 1: the muster for the hrimthursa icespine (15333,49257) was 907 from his spawn, but the formation post of the
    /// party in slot 6 around it was 15022,49568 (Bjagira's route destination at 12:09-12:11), 546 from his spawn; he came at
    /// 12:20:11 while the muster stood at 66 of 67.</item>
    /// </list>
    /// Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidRegroupTeleport1117
    {
        private static readonly Vector3 AgmundrSpawn = new(15037, 50114, 12408);
        private static readonly Vector3 Front = new(15037, 49914, 12424);
        private static readonly Vector3 Muster = new(15184, 48849, 12516);
        private static readonly Vector3 IcespineMuster = new(15333, 49257, 12484);
        private const int AgmundrAggro = 600;

        private static R.BossDanger[] Agmundr =>
            [new("Icelord Agmundr", AgmundrSpawn, AgmundrAggro + R.UntargetedBossMargin)];

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        // ---- the stranded regroup point ---------------------------------------------------------------------------------------

        [Test]
        public void TheFrontWasInsideHisAggroAndTheMusterIsNot()
        {
            Assert.That(Flat(Front, AgmundrSpawn), Is.EqualTo(200).Within(1), "the front: 200 from his spawn");
            Assert.That(R.InBossDanger(Front, Agmundr), Is.True);
            Assert.That(Flat(Muster, AgmundrSpawn), Is.GreaterThan(AgmundrAggro + R.UntargetedBossMargin), "the muster: 1,273 out");
            Assert.That(R.InBossDanger(Muster, Agmundr), Is.False);
        }

        [Test]
        public void ARaidThatHoldsSendsItsStrandedRaidersToItsMuster()
        {
            // While the raid regroups, the route holds at its muster (Destination) and its front stays the approach to him.
            Assert.That(R.StrandedRegroupPoint(true, Front, Muster, Muster, Agmundr), Is.EqualTo(Muster));
            // Even with no danger known, a holding raid is never sent to the front.
            Assert.That(R.StrandedRegroupPoint(true, Front, Muster, Muster, []), Is.EqualTo(Muster));
        }

        [Test]
        public void ARaidGoingInSendsThemToTheFrontUnlessARestingBossIsThere()
        {
            Vector3 clearFront = new(15184, 47800, 12516);
            Assert.That(R.StrandedRegroupPoint(false, clearFront, clearFront, Muster, Agmundr), Is.EqualTo(clearFront), "today's front");
            Assert.That(R.StrandedRegroupPoint(false, Front, Front, Muster, Agmundr), Is.EqualTo(Muster), "the front by him: the muster");
            Assert.That(R.StrandedRegroupPoint(false, Front, Front, null, Agmundr), Is.Null, "no muster: nobody is moved");
        }

        [Test]
        public void NoSafePointMeansNoTeleport()
        {
            // A hold point inside his aggro and no clear muster: the stranded raider stays where it is (today's fallback).
            Vector3 unsafeHold = new(15037, 49600, 12450);
            Assert.That(R.InBossDanger(unsafeHold, Agmundr), Is.True);
            Assert.That(R.StrandedRegroupPoint(true, Front, unsafeHold, unsafeHold, Agmundr), Is.Null);
            Assert.That(R.StrandedRegroupPoint(true, Front, unsafeHold, null, Agmundr), Is.Null);
            // A second resting danger over the muster too.
            R.BossDanger[] both = [.. Agmundr, new("hrimthursa icespine", new(15200, 48900, 12500), 500 + R.UntargetedBossMargin)];
            Assert.That(R.StrandedRegroupPoint(true, Front, Muster, Muster, both), Is.Null);
        }

        // ---- wipe 1: a formation post inside his aggro ----------------------------------------------------------------------

        [Test]
        public void TheSlotSixPostAroundTheIcespineMusterStoodInHisAggro()
        {
            Assert.That(Flat(IcespineMuster, AgmundrSpawn), Is.EqualTo(907).Within(1), "the muster itself was guarded (907 > 900)");
            Assert.That(R.InBossDanger(IcespineMuster, Agmundr), Is.False);
            Vector3 post = RealmRaidFormation.RawPost(IcespineMuster, 6, 1);
            Assert.That(post.X, Is.EqualTo(15022).Within(1), "Bjagira's route destination, 12:09-12:11");
            Assert.That(post.Y, Is.EqualTo(49568).Within(1));
            Assert.That(Flat(post, AgmundrSpawn), Is.LessThan(AgmundrAggro), "546 from his spawn: inside his aggro");
            Assert.That(R.InBossDangerStrict(post, Agmundr), Is.True, "now refused while the raid holds (RealmRaidDungeonRoute.PostDangers)");
        }

        [Test]
        public void SomeSpreadOrCompactPostAroundThatMusterIsClear()
        {
            // The guard never leaves a party without a candidate: tries away from him stay outside his aggro + 300.
            for (int slot = 0; slot < RealmRaidRecruitmentPolicy.FormationSlots; slot++)
            {
                bool any = false;
                for (int attempt = 0; attempt < 22 && !any; attempt++)
                    any = !R.InBossDangerStrict(RealmRaidFormation.RawPost(IcespineMuster, slot, attempt), Agmundr);
                Assert.That(any, Is.True, $"slot {slot}");
            }
        }

        [Test]
        public void RawPostKeepsTheFormationsGeometry()
        {
            Vector3 c = new(1000, 2000, 300);
            Assert.That(RealmRaidFormation.RawPost(c, 0, 0), Is.EqualTo(c), "slot 0 first try: the centre");
            Assert.That(Vector3.Distance(RealmRaidFormation.RawPost(c, 3, 0), c), Is.EqualTo(280).Within(0.5), "140 x root 4");
            Assert.That(Vector3.Distance(RealmRaidFormation.RawPost(c, 3, 1), c), Is.EqualTo(350).Within(0.5), "70 more on odd tries");
            Assert.That(Vector3.Distance(RealmRaidFormation.RawPost(c, 0, 6), c), Is.EqualTo(100).Within(0.5), "compact");
        }

        // ---- return-wave joiners get a step they can reach --------------------------------------------------------------------

        private static Vector3[] TuscaranSteps() =>
            W.WalkSteps([new(29505, 27351, 17087), new(22000, 38000, 14500), Muster], Muster);

        [Test]
        public void JoinersAtTheWayInStartFromTheEntranceNotTheWavesStep()
        {
            Vector3[] steps = TuscaranSteps();
            int waveStep = steps.Length - 3;
            Assert.That(waveStep, Is.GreaterThan(10), "the wave is far in");
            Assert.That(W.JoinStep(steps, default, false, waveStep), Is.EqualTo(0), "still outside: the first step, where they come in");
            Assert.That(W.JoinStep(steps, steps[0] + new Vector3(80, -40, 0), true, waveStep), Is.EqualTo(0), "inside at the way in");
            Assert.That(W.JoinStep(steps, steps[waveStep] + new Vector3(600, 0, 0), true, waveStep), Is.EqualTo(waveStep),
                "near the wave: with it");
            Assert.That(W.JoinStep(steps, steps[5] + new Vector3(300, 0, 0), true, waveStep), Is.EqualTo(5), "part way: its own step");
        }

        [Test]
        public void ARaiderCatchingUpWalksOneStepAtATimeUntilTheWave()
        {
            const int wave = 34;
            Assert.That(W.CatchUp(0, -1, wave), Is.EqualTo(0), "not at its step yet: still walking to it");
            Assert.That(W.CatchUp(0, 0, wave), Is.EqualTo(1), "at its step: the next, at most 1,200 on");
            Assert.That(W.CatchUp(5, 2, wave), Is.EqualTo(5), "an earlier step is not headway");
            Assert.That(W.CatchUp(5, 9, wave), Is.EqualTo(10), "at a later step: on from there");
            Assert.That(W.CatchUp(33, 33, wave), Is.EqualTo(-1), "the next is the wave's: it walks with the wave");
            Assert.That(W.CatchUp(3, 48, wave), Is.EqualTo(-1), "moved to the raid: caught up");
        }

        [Test]
        public void EveryCatchUpLegIsOneStepLong()
        {
            Vector3[] steps = TuscaranSteps();
            for (int i = 1; i < steps.Length; i++)
                Assert.That(Vector3.Distance(steps[i - 1], steps[i]), Is.LessThanOrEqualTo(W.StepUnits + 1), $"leg {i}");
        }
    }
}
