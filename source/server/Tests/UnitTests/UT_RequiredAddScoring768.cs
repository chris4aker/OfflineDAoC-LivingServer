using System.Linq;
using DOL.GS;
using NUnit.Framework;
using Verdict = DOL.GS.RealmRaidDungeonRoute.AttemptVerdict;

namespace DOL.UnitTests
{
    /// <summary>
    /// #768: an attempt on a boss whose required adds die first, in order (Myrddraxis's heads), is judged on those adds too,
    /// with the same 70% / 95% rule (owner, 2026-11-27). Owner's PC, rc.2 (4f5a2aa): 14 attempts on Myrddraxis, all
    /// broke-off, 691 raider deaths, then the four-hour timeout; the boss's own health stood near full while the raid fought
    /// the heads, so no attempt was ever a try and the Special Forces were never called. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_RequiredAddScoring768
    {
        private static readonly object Head2 = new(), Head3 = new(), Head4 = new(), Head5 = new();

        private static RaidOrderedAddProgress Pulled()
        {
            var p = new RaidOrderedAddProgress();
            p.Begin(4);
            foreach (object head in new[] { Head2, Head3, Head4, Head5 }) p.Note(head, true, 100);
            return p;
        }

        /// <summary>The attempt's verdict as EndAttempt now makes it, from the boss's and the heads' state.</summary>
        private static Verdict Score(int engaged, int fallen, int bossNow, int bossLowest, RaidOrderedAddProgress adds, int[] headsNow,
            int raid, int freeResetsUsed)
        {
            int addsNow = adds.Tracking ? RaidOrderedAddProgress.AddsNowPercent(adds.BeganWith, headsNow) : 100;
            var (now, lowest) = RaidOrderedAddProgress.Judged(bossNow, bossLowest, adds.Tracking, addsNow, adds.Lowest);
            return RealmRaidDungeonRoute.Judge(engaged, fallen, now, lowest, raid, freeResetsUsed);
        }

        [Test]
        public void OnlyTheBossesWhoseAddsDieFirstInOrderAreJudgedOnTheirAdds() => Assert.Multiple(() =>
        {
            EpicEncounterTactics.Tactic Row(string boss) => EpicEncounterTactics.Catalog.First(t => t.Boss == boss);
            Assert.That(Row("Myrddraxis").AddsInOrder, Is.True);
            Assert.That(Row("Myrddraxis").RequiredAdds,
                Is.EqualTo(new[] { "MyrddraxisSecondHead", "MyrddraxisFourthHead", "MyrddraxisThirdHead", "MyrddraxisFifthHead" }));
            // Summoned adds the raid kills as they come are no progress on the boss: those bosses are judged as before.
            Assert.That(Row("Morbus").AddsInOrder, Is.False);
            Assert.That(Row("Nosdoden").AddsInOrder, Is.False);
        });

        [Test]
        public void ALostHeadFightIsATry() => Assert.Multiple(() =>
        {
            // The raid wore head 2 to 40% and lost: 30 of 80 fell (more than a quarter, less than half), the heads healed back
            // to full and the body was never touched.
            var adds = Pulled();
            adds.Note(Head2, true, 40);
            int[] back = [100, 100, 100, 100];
            Assert.That(adds.Lowest, Is.EqualTo(40));
            Assert.That(Score(80, 30, 100, 100, adds, back, 85, 0), Is.EqualTo(Verdict.Wipe));
            // Before #768 the same attempt, judged on the body alone, was no wipe (the rc.2 "broke-off").
            Assert.That(RealmRaidDungeonRoute.Judge(80, 30, 100, 100, 85, 0), Is.EqualTo(Verdict.NoWipe));
            // A head that died and came back as a new one is the encounter reset too.
            var killedAndBack = Pulled();
            killedAndBack.Note(Head2, false, 0);
            Assert.That(Score(80, 30, 100, 100, killedAndBack, back, 85, 0), Is.EqualTo(Verdict.Wipe));
            // The rows say why: the boss's own fields are unchanged, the adds' follow.
            Assert.That(RaidOrderedAddProgress.Fields(true, 100, 40), Is.EqualTo(" addsHealth=100 addsLowest=40"));
        });

        [Test]
        public void TwoTriesThenTheRaidEndsAndEachTryButTheLastCallsTheSpecialForces() => Assert.Multiple(() =>
        {
            // Few fell (10 of 80): the encounter's two free resets come first, as for any boss, then each is a try.
            var adds = Pulled();
            adds.Note(Head2, true, 55);
            int[] back = [100, 100, 100, 100];
            Assert.That(Score(80, 10, 100, 100, adds, back, 85, 0), Is.EqualTo(Verdict.FreeReset));
            Assert.That(Score(80, 10, 100, 100, adds, back, 85, 1), Is.EqualTo(Verdict.FreeReset));
            Assert.That(Score(80, 10, 100, 100, adds, back, 85, 2), Is.EqualTo(Verdict.Wipe));
            Assert.That(RealmRaidDungeonRoute.MaximumFailedAttempts, Is.EqualTo(2), "the raid's two tries");
            Assert.That(AutonomousRealmRaid.WipeCallsSpecialForces(1), Is.True, "the first try calls the Special Forces");
            Assert.That(AutonomousRealmRaid.WipeCallsSpecialForces(2), Is.False, "the second ends the raid (Defeated at Myrddraxis)");
            // Before #768 none of these was even a reset: the raid pulled again until the four-hour window ended.
            Assert.That(RealmRaidDungeonRoute.Judge(80, 10, 100, 100, 85, 2), Is.EqualTo(Verdict.NoWipe));
        });

        [Test]
        public void ABossWithoutOrderedAddsIsJudgedAsBefore() => Assert.Multiple(() =>
        {
            var none = new RaidOrderedAddProgress();
            none.Begin(0);
            none.Note(Head2, true, 10); // ignored: nothing is tracked
            Assert.That(none.Tracking, Is.False);
            Assert.That(none.Lowest, Is.EqualTo(100));
            Assert.That(RaidOrderedAddProgress.Fields(false, 100, 100), Is.Empty);
            Assert.That(RaidOrderedAddProgress.Judged(97, 64, false, 0, 0), Is.EqualTo((97, 64)));
            foreach (var (engaged, fallen, now, lowest, raid) in new[] { (80, 30, 100, 100, 85), (80, 40, 100, 100, 85), (80, 10, 100, 60, 85),
                         (80, 30, 100, 60, 85), (20, 15, 100, 100, 85), (54, 0, 100, 14, 0), (68, 2, 100, 68, 87), (80, 30, 50, 40, 85) })
            {
                bool wipe = RealmRaidDungeonRoute.IsWipe(engaged, fallen, now, lowest);
                Verdict before = !wipe ? Verdict.NoWipe
                    : RealmRaidDungeonRoute.IsResetWithRaidStanding(engaged, fallen, now, lowest) ? Verdict.FreeReset
                    : !RealmRaidDungeonRoute.IsWipe(engaged, fallen, now, lowest, raid) ? Verdict.Skirmish : Verdict.Wipe;
                Assert.That(Score(engaged, fallen, now, lowest, none, [], raid, 0), Is.EqualTo(before), $"{engaged}/{fallen} {now}/{lowest}");
            }
        });

        [Test]
        public void AWonHeadFightIsNotCountedAgainstTheRaid() => Assert.Multiple(() =>
        {
            // Head 2 killed and still dead when the attempt ends (the raid left combat to regroup): its progress stands, the
            // encounter has not reset, so it is no wipe by the reset rule (only half the raid falling would make it one).
            var adds = Pulled();
            adds.Note(Head2, false, 0);
            int[] threeLeft = [100, 100, 100];
            Assert.That(RaidOrderedAddProgress.AddsNowPercent(4, threeLeft), Is.EqualTo(0));
            Assert.That(Score(80, 30, 100, 100, adds, threeLeft, 85, 0), Is.EqualTo(Verdict.NoWipe));
            Assert.That(Score(80, 40, 100, 100, adds, threeLeft, 85, 0), Is.EqualTo(Verdict.Wipe), "half the raid fell: a wipe, as before");
            // A head still worn down when the attempt ends has not reset either.
            var worn = Pulled();
            worn.Note(Head4, true, 35);
            Assert.That(Score(80, 30, 100, 100, worn, [100, 100, 35, 100], 85, 0), Is.EqualTo(Verdict.NoWipe));
            // The next attempt starts clean: the head killed in the last one is not this one's.
            adds.Begin(3);
            foreach (object head in new[] { Head3, Head4, Head5 }) adds.Note(head, true, 100);
            Assert.That(adds.Lowest, Is.EqualTo(100));
            Assert.That(RaidOrderedAddProgress.AddsNowPercent(3, threeLeft), Is.EqualTo(100));
        });
    }
}
