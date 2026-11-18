using System.Collections.Generic;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidDungeonRoute;
using W = DOL.GS.RealmRaidReturnWave;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's PC, build 5378dbe, 2026-11-17/18 (sweep 5):
    /// <list type="bullet">
    /// <item>34 stranded raiders were moved to 31246,40738, 200 from the resting Conservator, and 2 to 27655,54721, 209 from
    /// the resting Xaga, in the route turn between picking them and setting their muster.</item>
    /// <item>Galladoria: the muster for an olcasar warrior was placed against the bosses only and the stranded-regroup guard
    /// (named, finals, linked packs) refused it for ~18 minutes; 123 raiders stood on the landing at the way in.</item>
    /// <item>Avalon: a return wave of 2-3 called the main body back 11 times (helps=9, cap 2) while the raid mustered.</item>
    /// </list>
    /// Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidRegroup5378
    {
        // ---- 1. no teleport to the front of a resting target before its muster ---------------------------------------------

        private static readonly Vector3 Conservator = new(31246, 40938, 15373);
        private static readonly Vector3 ConservatorFront = new(31246, 40738, 15373);

        [Test]
        public void NobodyIsMovedToTheFrontOfARestingTargetBeforeItsMuster()
        {
            // The guard leaves the raid's own target out (OfTheTarget), so the front 200 from him is "clear": that was the gap.
            List<R.BossDanger> dangers = R.RestingDangersOf(
                [new("Conservator", Conservator, Conservator, 500, false, false, true, false, OfTheTarget: true)]);
            Assert.That(R.StrandedRegroupPoint(false, ConservatorFront, ConservatorFront, null, dangers), Is.EqualTo(ConservatorFront),
                "the point the 34 were sent to");
            // Now that turn moves nobody: not holding, the target rests, it is a mustered encounter and its muster isn't set.
            Assert.That(R.AwaitsMuster(holding: false, targetResting: true, musteredEncounter: true, musterSet: false), Is.True);
            // Once the muster is set the raid holds there and they go to it; in his fight, to the front; trash is not touched.
            Assert.That(R.AwaitsMuster(holding: true, targetResting: true, musteredEncounter: true, musterSet: true), Is.False);
            Assert.That(R.AwaitsMuster(holding: false, targetResting: true, musteredEncounter: true, musterSet: true), Is.False, "the pull walks in");
            Assert.That(R.AwaitsMuster(holding: false, targetResting: false, musteredEncounter: true, musterSet: false), Is.False, "his fight is on");
            Assert.That(R.AwaitsMuster(holding: false, targetResting: true, musteredEncounter: false, musterSet: false), Is.False, "\"Clearing\" trash");
        }

        // ---- 2. a muster lies where the stranded-regroup guard sends raiders ------------------------------------------------

        // A straight walk from the encounter (0,0) towards the raid; a named (a GameEpicNPC, not a GameEpicBoss) beside it.
        private static readonly Vector3 Encounter = new(0, 0, 0);
        private static readonly Vector3[] Walk = [Encounter, new(-5000, 0, 0)];
        private static readonly Vector3 Scryer = new(-1400, 600, 0);

        private static List<R.BossDanger> Resting() => R.RestingDangersOf(
            [new("olcasar scryer", Scryer, Scryer, 500, false, false, BossOrNamed: true, LinkedPack: false, OfTheTarget: false)]);

        [Test]
        public void AMusterIsPlacedAgainstWhatTheRegroupGuardRefuses()
        {
            const float standOff = 1400;
            List<R.BossDanger> bosses = [];   // the old muster dangers: GameEpicBoss only, and the scryer is none
            Vector3? old = R.SafeMusterAlong(Walk, Encounter, standOff, bosses, null);
            Assert.That(old, Is.Not.Null);
            Assert.That(old.Value.X, Is.EqualTo(-1470.6f).Within(1), "the first walk point past the stand-off");
            Assert.That(R.StrandedRegroupPoint(true, Encounter, old.Value, old.Value, Resting()), Is.Null,
                "5378dbe: the guard refuses the hold point, and nobody stranded is ever moved to it");
            Assert.That(R.RefusalText(old.Value, Resting()), Does.StartWith("olcasar scryer@-1400,600,0/distance=").And.EndWith("/edge=736"),
                "REGROUP_REFUSED names the danger, the distance and the strict edge");

            List<R.BossDanger> both = R.MusterDangersOf(bosses, Resting());
            Vector3? now = R.SafeMusterAlong(Walk, Encounter, standOff, both, null);
            Assert.That(now, Is.Not.Null);
            Assert.That(Vector3.Distance(now.Value, Scryer), Is.GreaterThanOrEqualTo(800), "out of the scryer's aggro + 300");
            Assert.That(R.StrandedRegroupPoint(true, Encounter, now.Value, now.Value, Resting()), Is.EqualTo(now.Value),
                "the guard accepts the muster: stranded raiders are sent to it");
            Assert.That(R.RefusalText(now.Value, Resting()), Is.EqualTo("none"));
        }

        [Test]
        public void MusterDangersKeepEveryBossDangerToo()
        {
            R.BossDanger boss = new("Olcasgean", new(100, 100, 0), 900);
            List<R.BossDanger> both = R.MusterDangersOf([boss], Resting());
            Assert.That(both, Does.Contain(boss), "a boss in combat (left out by the guard) still keeps the muster off");
            Assert.That(both.Count, Is.EqualTo(1 + Resting().Count));
            Assert.That(R.MusterDangersOf(null, null), Is.Empty);
        }

        // ---- 3. a return wave calls for help at most twice, and a mustering raid sends none ---------------------------------

        [Test]
        public void AWaveThatHasCalledTwiceFightsInstead()
        {
            const int few = 3;   // Avalon: 2-3 living, a vyr'em couranteer in the way
            Assert.That(W.Walk(few, few, 0, true, false, true, false, 0), Is.EqualTo(W.WaveMove.CallHelp));
            Assert.That(W.Walk(few, few, 0, true, false, true, false, 1), Is.EqualTo(W.WaveMove.CallHelp));
            Assert.That(W.Walk(few, few, 0, true, false, true, false, W.MaximumHelps), Is.EqualTo(W.WaveMove.Fight),
                "5378dbe: helps=9 against a cap of 2");
            Assert.That(W.Walk(few, few, 0, true, false, true, false, 9), Is.EqualTo(W.WaveMove.Fight));
            Assert.That(W.Walk(few, few, 0, true, false, false, true, W.MaximumHelps), Is.EqualTo(W.WaveMove.CallHelp),
                "helpers already on their way: it waits for them");
            Assert.That(W.Walk(W.MinimumWave, W.MinimumWave, 0, true, false, true, false, 0), Is.EqualTo(W.WaveMove.Fight), "a party's worth fights");
        }

        [Test]
        public void AMusteringRaidSendsNoHelpers()
        {
            Assert.That(W.BodyCanCome(13, false, mustering: true), Is.False, "Avalon: 37 of 44 at the muster for Dra'argus");
            Assert.That(W.BodyCanCome(13, false, mustering: false), Is.True);
            Assert.That(W.BodyCanCome(13, true, mustering: false), Is.False);
            Assert.That(W.ChooseHelp(0, W.BodyCanCome(13, false, true), 0), Is.EqualTo(W.Help.None), "nothing to send from a muster");
        }
    }
}
