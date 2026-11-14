using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Realm raid flow, from the owner's overnight PC log of 2026-11-14 (build e3b8238, which already had the return waves,
    /// the 70% pull and the "a reset is not a wipe" rule of PR #195, and the raid-size rule of PR #193).
    /// </summary>
    [TestFixture]
    public class UT_RaidFlow
    {
        private const long Minute = 60_000;
        private static readonly EpicEncounterTactics.EarlyPull Commit = EpicEncounterTactics.EarlyPull.Commit,
            Disengage = EpicEncounterTactics.EarlyPull.Disengage, Hold = EpicEncounterTactics.EarlyPull.Hold;

        // ---- 1. an early pull is judged against the living raid --------------------------------------------------------

        [Test]
        public void AHandfulInsideAfterAWipeIsNotTheRaid()
        {
            // The PC log's three early pulls that each lost a fight: near / inside / living raid. None of them shakes off
            // (no tether, or the muster inside it), and before, near*2 >= inside committed all three.
            int Needed(int raid) => RealmRaidDungeonRoute.FinalMusterNeeded(raid, 0);
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(16, 18, 118, false, false, Needed(118)), Is.EqualTo(Hold), "ater skeleton");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(26, 35, 82, false, false, Needed(82)), Is.EqualTo(Hold), "Council Nokkvi");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(24, 32, 103, false, false, Needed(103)), Is.EqualTo(Hold), "the Host");
            // The old rule on the same numbers, for the record.
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(24, 32, false), Is.EqualTo(Commit));
        }

        [Test]
        public void TheWholeRaidInsideStillCommits()
        {
            // Galladoria's trash and Darkness Falls' princes: most of the raid is inside, so a pull is answered by all of it.
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(62, 212, 222, false, false, RealmRaidDungeonRoute.FinalMusterNeeded(222, 0)),
                Is.EqualTo(Commit), "olcasar stonemelder: 212 of 222 inside");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(29, 93, 100, false, false, RealmRaidDungeonRoute.FinalMusterNeeded(100, 0)),
                Is.EqualTo(Commit), "Prince Abdin: 93 of 100 inside");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(52, 60, 100, false, false, 70), Is.EqualTo(Commit),
                "half the raid is already in it: it is the raid's fight");
        }

        [Test]
        public void ABossThePullBackShakesOffIsLeft()
        {
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(10, 90, 100, true, false, 70), Is.EqualTo(Disengage));
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(10, 90, 100, true, true, 70), Is.EqualTo(Commit),
                "pulled back from a moment ago and still on the raid: the raid inside goes in");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(10, 20, 100, true, true, 70), Is.EqualTo(Hold),
                "...but a handful inside is never sent in");
        }

        [Test]
        public void TheOldThreeArgumentRuleIsUnchanged()
        {
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(31, 87, true), Is.EqualTo(Disengage));
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(31, 87, false), Is.EqualTo(Commit));
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(45, 83, true), Is.EqualTo(Commit));
        }

        [Test]
        public void AHeldSkirmishOfTheFewIsNotTheRaidsAttempt()
        {
            // The Host's 24 all fall while 103 live: a lost skirmish (PR #193's rule), no attempt used.
            Assert.That(RealmRaidDungeonRoute.IsWipe(24, 24, 100, 90), Is.True);
            Assert.That(RealmRaidDungeonRoute.IsWipe(24, 24, 100, 90, 103), Is.False);
        }
    }
}
