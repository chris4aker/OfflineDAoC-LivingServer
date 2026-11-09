using System;
using System.Linq;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Overnight run 2026-11-05/06 (build 168f55d): the siege balance sat at its defender-side limit (+0.25, share 1.0) all
    /// night while attackers took 13 of 17 keeps. The share caps the defenders a siege may draw, and that cap was rarely the
    /// limit; the attackers' cap of 128 was reached in 10 of 17 battles. Past the share's limit the balance now trims the
    /// attackers' cap (docs/NIGHT-SIEGES-AND-MARFACH.md, section 4).
    /// </summary>
    [TestFixture]
    public class UT_SiegeBalanceNov06
    {
        [SetUp]
        public void Reset() => SiegeDefenseBalance.Reset();

        [TearDown]
        public void Restore()
        {
            SiegeDefenseBalance.Reset();
            AutonomousRvrEventLayer.SiegeScheduling = true;
        }

        [TestCase(-0.25, 128, 240)]
        [TestCase(0.0, 128, 240)]
        [TestCase(0.25, 128, 240)]   // the share's limit: attackers untouched
        [TestCase(0.375, 112, 210)]  // one step past it
        [TestCase(0.5, 96, 180)]     // two steps: the most it takes
        [TestCase(1.0, 96, 180)]     // never more than that
        public void PastTheSharesLimitTheAttackersCapIsTrimmed(double shift, int keep, int relic) => Assert.Multiple(() =>
        {
            Assert.That(SiegeDefenseBalance.AttackerCapAt(RealmEventPolicy.KeepAttackers, shift), Is.EqualTo(keep));
            Assert.That(SiegeDefenseBalance.AttackerCapAt(RealmEventPolicy.RelicAttackers, shift), Is.EqualTo(relic));
        });

        [TestCase(-0.25, 0.5)]
        [TestCase(0.25, 1.0)]
        [TestCase(0.375, 1.0)]
        [TestCase(0.5, 1.0)]
        public void TheDefendersShareStillMovesAtMostAQuarter(double shift, double share) =>
            Assert.That(SiegeDefenseBalance.RatioAt(RealmEventPolicy.DefenseRatio, shift), Is.EqualTo(share));

        [Test]
        public void TheShiftGoesTwoStepsPastTheShareAndComesBackTheSameWay() => Assert.Multiple(() =>
        {
            bool[] allCaptured = [true, true, true, true, true, true];
            bool[] mostlyDefended = [true, false, false, false, false, false];
            Assert.That(SiegeDefenseBalance.NextShift(0.25, allCaptured), Is.EqualTo(0.375), "still captured at the share's limit");
            Assert.That(SiegeDefenseBalance.NextShift(0.375, allCaptured), Is.EqualTo(0.5));
            Assert.That(SiegeDefenseBalance.NextShift(0.5, allCaptured), Is.EqualTo(0.5), "the limit");
            Assert.That(SiegeDefenseBalance.NextShift(0.5, mostlyDefended), Is.EqualTo(0.375), "defenders win: the trim comes off first");
            Assert.That(SiegeDefenseBalance.NextShift(0.375, mostlyDefended), Is.EqualTo(0.25));
            Assert.That(SiegeDefenseBalance.NextShift(0.25, mostlyDefended), Is.EqualTo(0.125), "then the share falls");
            Assert.That(SiegeDefenseBalance.NextShift(0.375, [true, false, true, false, true, false]), Is.EqualTo(0.375),
                "three in six: unchanged (owner 2026-11-09: about half)");
            Assert.That(SiegeDefenseBalance.NextShift(0.375, [true, true, false, true, false, true]), Is.EqualTo(0.5),
                "four in six: more defenders");
        });

        // ---- The simulation: does the balance settle near half? (owner 2026-11-09: attackers win about half) ----

        /// <summary>
        /// A model of the attackers' chance to take a keep by the balance's shift: a logistic curve that falls as the shift adds
        /// defenders (and then trims attackers). Where it crosses one half and how steeply are unknown, so the cases move both:
        /// a strong defense that holds even with few defenders (the half point at -0.125) to one that needs the full trim (+0.375).
        /// It is a model, not a measurement.
        /// </summary>
        private static double AttackerChance(double shift, double half, double steep) =>
            1 / (1 + Math.Exp(steep * (shift - half)));

        private static (double Share, double MeanShift) SimulateSieges(double half, double steep, int sieges, int seed)
        {
            var random = new Random(seed);
            var window = new System.Collections.Generic.Queue<bool>();
            double shift = 0, shiftSum = 0;
            int counted = 0, won = 0;
            for (int siege = 0; siege < sieges; siege++)
            {
                bool captured = random.NextDouble() < AttackerChance(shift, half, steep);
                window.Enqueue(captured);
                while (window.Count > SiegeDefenseBalance.Window) window.Dequeue();
                shift = SiegeDefenseBalance.NextShift(shift, window.ToArray());
                if (siege < sieges / 10) continue; // a settling-in tenth
                counted++;
                won += captured ? 1 : 0;
                shiftSum += shift;
            }
            return (won / (double)counted, shiftSum / counted);
        }

        [TestCase(-0.125, 8)]
        [TestCase(-0.125, 16)]
        [TestCase(0.0, 8)]
        [TestCase(0.0, 16)]
        [TestCase(0.125, 8)]
        [TestCase(0.125, 16)]
        [TestCase(0.25, 8)]
        [TestCase(0.25, 16)]
        [TestCase(0.375, 8)]
        [TestCase(0.375, 16)]
        public void TheBalanceSettlesNearHalf(double half, double steep)
        {
            var (share, meanShift) = SimulateSieges(half, steep, 20_000, 1);
            TestContext.WriteLine($"half point {half:+0.###;-0.###;0}, steepness {steep}: attackers won {share:P1}, mean shift {meanShift:+0.###;-0.###}");
            Assert.That(share, Is.InRange(0.42, 0.58), "about half");
        }

        [Test]
        public void OnAverageTheBalanceSettlesAtHalf()
        {
            double[] halves = [-0.125, 0, 0.125, 0.25, 0.375];
            double[] steeps = [8, 16];
            double mean = halves.SelectMany(half => steeps.Select(steep => SimulateSieges(half, steep, 20_000, 1).Share)).Average();
            TestContext.WriteLine($"mean attacker share over ten curves: {mean:P1}");
            Assert.That(mean, Is.InRange(0.47, 0.53));
        }

        [Test]
        public void TheOwnersNightWouldHaveTrimmedTheAttackersByTheThirdCapture()
        {
            // The ledger replayed at 18:42 (window CCCCCC), then the night's results in order (SIEGE_DEFENSE_BALANCE).
            bool[] ledger = Enumerable.Repeat(true, 24).ToArray();
            bool[] night = "DCCCCCCDDCCCCCCDC".Select(result => result == 'C').ToArray();
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.Load(ledger);
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(0.5), "24 captures: share at its limit, attackers trimmed twice");
                Assert.That(RealmEventPolicy.EffectiveAttackerCap(false), Is.EqualTo(96));
                Assert.That(RealmEventPolicy.EffectiveDefenseRatio, Is.EqualTo(1.0));
            });
            foreach (bool result in night) SiegeDefenseBalance.Record(result);
            Assert.Multiple(() =>
            {
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(SiegeDefenseBalance.ShiftAfter(ledger.Concat(night))));
                Assert.That(SiegeDefenseBalance.Shift, Is.EqualTo(0.5), "attackers still took 13 of 17");
            });
        }

        [Test]
        public void OnlyTheAttackersCapIsTrimmed()
        {
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.Load(Enumerable.Repeat(true, 24).ToArray());
            Assert.Multiple(() =>
            {
                Assert.That(RealmEventPolicy.AttackerCap(false), Is.EqualTo(128), "the full cap is unchanged");
                Assert.That(RealmEventPolicy.DynamicStartThreshold(false, true), Is.EqualTo(64), "the muster still waits for 64");
                Assert.That(RealmEventPolicy.DynamicStartThreshold(true, true), Is.EqualTo(120));
                Assert.That(RealmEventPolicy.ThirdRealmCap(false), Is.EqualTo(64), "the third realm keeps its cap");
                Assert.That(RealmEventPolicy.DefenderCap(false), Is.EqualTo(128), "defenders may still match a full army");
                Assert.That(RealmEventPolicy.DefendersFor(96, 96, RealmEventPolicy.EffectiveDefenseRatio, RealmEventPolicy.DefenderCap(false)),
                    Is.EqualTo(96), "and a trimmed one, one for one");
            });
        }

        [TestCase(false, false, false, 96)]   // an automatic bot siege is trimmed
        [TestCase(true, false, false, 128)]   // a forced siege keeps its army
        [TestCase(false, true, false, 64)]    // a retake keeps its own cap
        [TestCase(false, false, true, 240)]   // a player-attack defense keeps its own cap
        public void ForcedSiegesRetakesAndPlayerDefensesKeepTheirCaps(bool forced, bool retake, bool defenseReaction, int cap)
        {
            SiegeDefenseBalance.ResetUnloadedForTests();
            SiegeDefenseBalance.Load(Enumerable.Repeat(true, 24).ToArray());
            AutonomousRvrEventLayer.SiegeScheduling = true;
            Type type = typeof(AutonomousRvrEventLayer).GetNestedType("ActiveEvent", BindingFlags.NonPublic);
            object active = Activator.CreateInstance(type, nonPublic: true);
            void Set(string field, object value) => type.GetField(field).SetValue(active, value);
            Set("TargetId", "rvr-keep-100");
            Set("RelicKeep", false);
            Set("Forced", forced);
            Set("Retake", retake);
            Set("RetakeCap", 64);
            Set("DefenseReaction", defenseReaction);
            int actual = (int)typeof(AutonomousRvrEventLayer).GetMethod("AttackerCapFor", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, [active]);
            Assert.That(actual, Is.EqualTo(cap));
        }

        [Test]
        public void TheBalanceLineShowsTheAttackersCap() => Assert.Multiple(() =>
        {
            Assert.That(SiegeDefenseBalance.Line("captured", 0.375, 0.5, [true, true, true, true, true, true]),
                Is.EqualTo("SIEGE_DEFENSE_BALANCE event=\"captured\" shift=+0.375->+0.5 share=0.75->1 attackerCap=128->96 window=\"CCCCCC\""));
            Assert.That(SiegeDefenseBalance.Line("defended", 0, -0.125, [true, false, false]),
                Is.EqualTo("SIEGE_DEFENSE_BALANCE event=\"defended\" shift=0->-0.125 share=0.75->0.625 attackerCap=128->128 window=\"CDD\""));
        });

        // ---- Who was at the keep (the logs could not say whether defenders ever arrived) ----

        [Test]
        public void ThePresenceLineGivesCommittedAndPresent() =>
            Assert.That(AutonomousRvrEventLayer.PresenceFields(128, 66, 64, 71, 3, 12),
                Is.EqualTo("committed=128/66/64 present=71/3/12"));

        [Test]
        public void TheEndLineSaysWhenTheFirstDefenderArrived() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRvrEventLayer.BattleEndFields(11 * 60_000 + 20_000, 64, 0, 5, 80, 4, 7 * 60_000 + 5_000, 128),
                Is.EqualTo("battleMinutes=11 present=64/0/5 peakPresent=80/4 defendersFirstAt=7 attackerCap=128"));
            Assert.That(AutonomousRvrEventLayer.BattleEndFields(9 * 60_000, 50, 0, 0, 59, 0, -1, 96),
                Is.EqualTo("battleMinutes=9 present=50/0/0 peakPresent=59/0 defendersFirstAt=none attackerCap=96"),
                "no defender ever reached the keep");
            Assert.That(AutonomousRvrEventLayer.BattleEndFields(-1, 0, 0, 0, 0, 0, -1, 128),
                Does.StartWith("battleMinutes=none "), "a failed rally never fought");
        });
    }
}
