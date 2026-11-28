using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.RealmAbilities;
using NUnit.Framework;
using static DOL.GS.AutonomousBotRealmAbilities;
using static DOL.GS.RvrRealmActives;

namespace DOL.UnitTests
{
    /// <summary>
    /// #531 step 2c-1: Speed of Sound on the engage, Mastery of Concentration when interrupted twice, Bunker of Faith after
    /// a death, for autonomous gamebots in the open field. The rules and the builds; the wiring is shown by the #520
    /// rehearsal row. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_RvrRealmActives531
    {
        // ---- the triggers ----

        [TestCase(RvrStance.Engage, true, 1500.0, 3, ExpectedResult = true, TestName = "SoS: engaging, a target picked, 1,500 to go")]
        [TestCase(RvrStance.Engage, true, (double)ChargeMinDistance, 1, ExpectedResult = true, TestName = "SoS: at the near edge")]
        [TestCase(RvrStance.Engage, true, (double)ChargeMaxDistance, 1, ExpectedResult = true, TestName = "SoS: at the far edge")]
        [TestCase(RvrStance.Engage, true, 500.0, 3, ExpectedResult = false, TestName = "SoS: already there")]
        [TestCase(RvrStance.Engage, true, 3000.0, 3, ExpectedResult = false, TestName = "SoS: too far, the fight has not begun")]
        [TestCase(RvrStance.Engage, false, 1500.0, 3, ExpectedResult = false, TestName = "SoS: no target picked yet")]
        [TestCase(RvrStance.Engage, true, 1500.0, 0, ExpectedResult = false, TestName = "SoS: no mate in reach")]
        [TestCase(RvrStance.Observe, true, 1500.0, 3, ExpectedResult = false, TestName = "SoS: still sizing up")]
        [TestCase(RvrStance.Hold, true, 1500.0, 3, ExpectedResult = false, TestName = "SoS: holding")]
        [TestCase(RvrStance.Retreat, true, 1500.0, 3, ExpectedResult = false, TestName = "SoS: not on a retreat (the plan's engage only)")]
        public bool SpeedOfSoundFiresOnTheEngage(RvrStance stance, bool targeted, double distance, int mates) =>
            SpeedOfSoundWanted(stance, targeted, distance, mates);

        [TestCase(true, 2, true, true, ExpectedResult = true, TestName = "MoC: second interrupt, still interrupted")]
        [TestCase(true, 3, true, true, ExpectedResult = true, TestName = "MoC: third interrupt")]
        [TestCase(true, 1, true, true, ExpectedResult = false, TestName = "MoC: first interrupt only")]
        [TestCase(true, 2, false, true, ExpectedResult = false, TestName = "MoC: not interrupted now")]
        [TestCase(true, 2, true, false, ExpectedResult = false, TestName = "MoC: only instants to cast")]
        [TestCase(false, 2, true, true, ExpectedResult = false, TestName = "MoC: no open-field fight")]
        public bool MasteryOfConcentrationFiresWhenInterruptedTwice(bool fight, int interrupts, bool now, bool castTime) =>
            MasteryOfConcentrationWanted(fight, interrupts, now, castTime);

        [TestCase(true, 1, 2, ExpectedResult = true, TestName = "BoF: a mate dead, two standing")]
        [TestCase(true, 2, 6, ExpectedResult = true, TestName = "BoF: two dead, six standing")]
        [TestCase(true, 0, 8, ExpectedResult = false, TestName = "BoF: nobody dead yet")]
        [TestCase(true, 1, 1, ExpectedResult = false, TestName = "BoF: only the Cleric standing")]
        [TestCase(false, 1, 6, ExpectedResult = false, TestName = "BoF: not in contact")]
        public bool BunkerOfFaithFiresAfterADeath(bool contact, int dead, int living) => BunkerOfFaithWanted(contact, dead, living);

        [Test]
        public void AnInterruptCountsOnceAndTheCountStartsAgainAfterTheFight()
        {
            int count = 0; bool was = false; long last = long.MinValue / 2;
            void Look(bool interrupted, bool combat, long at) =>
                (count, was, last) = CountInterrupts(count, was, last, interrupted, combat, at);
            Look(false, true, 0);
            Look(true, true, 1_000);
            Look(true, true, 2_000);   // the same interrupt, extended by more hits
            Assert.That(count, Is.EqualTo(1), "one interrupt, however many hits keep it running");
            Look(false, true, 3_000);
            Look(true, true, 4_000);
            Assert.That(count, Is.EqualTo(2), "a second interrupt");
            Look(false, false, 10_000);
            Assert.That(count, Is.EqualTo(2), "out of combat for less than the gap: same fight");
            Look(false, false, 4_000 + FightGapMilliseconds);
            Assert.That(count, Is.Zero, "a new fight counts from zero");
            Look(true, true, 30_000);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void TheExecutorHandlesExactlyTheThreeAndBotsMayUseThem()
        {
            Assert.That(Keys, Is.EquivalentTo(new[] { SpeedOfSound, MasteryOfConcentration, BunkerOfFaith }));
            foreach (string key in Keys)
            {
                Assert.That(Handles(key), Is.True, key);
                Assert.That(UsableActives, Does.Contain(key), key);
            }
            Assert.That(Handles(Purge), Is.False, "Purge keeps its own handler");
        }

        [Test]
        public void TheRealTimersAreThirtyMinutes()
        {
            var dba = new DOL.Database.DbAbility { KeyName = SpeedOfSound, Name = "Speed of Sound" };
            Assert.That(new AtlasOF_SpeedOfSound(dba, 1).GetReUseDelay(1), Is.EqualTo(1800));
            dba = new DOL.Database.DbAbility { KeyName = MasteryOfConcentration, Name = "Mastery of Concentration" };
            Assert.That(new AtlasOF_MasteryofConcentration(dba, 1).GetReUseDelay(1), Is.EqualTo(1800));
            dba = new DOL.Database.DbAbility { KeyName = BunkerOfFaith, Name = "Bunker of Faith" };
            Assert.That(new AtlasOF_BunkerOfFaith(dba, 1).GetReUseDelay(1), Is.EqualTo(1800));
        }

        // ---- the builds ----

        private static Dictionary<string, int> Catalog(params string[] keys) =>
            keys.Select(k => k.StartsWith("AtlasOF_") ? k : "AtlasOF_" + k).ToDictionary(k => k, k => k switch
            {
                Purge or PurgeReduced or SecondWind or IgnorePain or IgnorePainTank or SpeedOfSound or MasteryOfConcentration or BunkerOfFaith => 1,
                FirstAid => 3,
                _ => 5,
            });

        private static int Cost(string key, int level) => key switch
        {
            Purge or SecondWind or SpeedOfSound or BunkerOfFaith => 10,
            PurgeReduced => 4,
            IgnorePain or MasteryOfConcentration => 14,
            IgnorePainTank => 8,
            FirstAid => AtlasRAHelpers.GetCommonUpgradeCostFor3LevelsRA(level),
            _ => AtlasRAHelpers.GetCommonUpgradeCostFor5LevelsRA(level),
        };

        private static int FirstPointsBuying(Dictionary<string, int> available, Archetype archetype, int classId, string key)
        {
            for (int points = 0; points <= 200; points++)
                if (Plan(available, archetype, classId, points, Cost).ContainsKey(key))
                    return points;
            return -1;
        }

        [Test]
        public void AMinstrelBuysSpeedOfSoundAfterPurge()
        {
            var minstrel = Catalog("AugStr", "AugCon", "AugDex", "AugQui", "AugAcuity", "MasteryOfPain", "MasteryOfBlocking", "MasteryOfArms",
                "Toughness", "Serenity", Purge, SecondWind, FirstAid, IgnorePain, SpeedOfSound);
            Assert.That(ArchetypeOf(minstrel.Keys), Is.EqualTo(Archetype.Melee));
            int purge = FirstPointsBuying(minstrel, Archetype.Melee, (int)eCharacterClass.Minstrel, Purge);
            int sos = FirstPointsBuying(minstrel, Archetype.Melee, (int)eCharacterClass.Minstrel, SpeedOfSound);
            Assert.That(sos, Is.GreaterThan(purge).And.GreaterThan(0), $"purge at {purge}, Speed of Sound at {sos}");
            Assert.That(sos, Is.LessThanOrEqualTo(60), "early in the build");
        }

        [Test]
        public void ACasterBuysMasteryOfConcentrationRightAfterItsPrerequisite()
        {
            var wizard = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfMagery", "WildPower", "Serenity", "Toughness", "EtherealBond",
                PurgeReduced, FirstAid, "MasteryOfTheArcane", MasteryOfConcentration);
            for (int points = 0; points <= 200; points++)
            {
                var plan = Plan(wizard, Archetype.Caster, (int)eCharacterClass.Wizard, points, Cost);
                if (plan.ContainsKey(MasteryOfConcentration))
                    Assert.That(plan.GetValueOrDefault(AugAcuity), Is.GreaterThanOrEqualTo(3), $"points={points}");
            }
            Assert.That(FirstPointsBuying(wizard, Archetype.Caster, (int)eCharacterClass.Wizard, MasteryOfConcentration), Is.InRange(1, 80));
        }

        [Test]
        public void AClericBuysBunkerOfFaithAndMasteryOfConcentration()
        {
            var cleric = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfHealing", "WildHealing", "Serenity", "Toughness", PurgeReduced,
                FirstAid, MasteryOfConcentration, BunkerOfFaith);
            Assert.That(ArchetypeOf(cleric.Keys), Is.EqualTo(Archetype.Healer));
            int bunker = FirstPointsBuying(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, BunkerOfFaith);
            int moc = FirstPointsBuying(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, MasteryOfConcentration);
            Assert.That(bunker, Is.InRange(1, 60));
            Assert.That(moc, Is.GreaterThan(bunker).And.LessThanOrEqualTo(100));
        }

        [Test]
        public void AClassWithoutTheAbilityNeverBuysIt()
        {
            var armsman = Catalog("AugStr", "AugCon", "AugDex", "AugQui", "MasteryOfPain", "MasteryOfParrying", "MasteryOfBlocking", "Toughness",
                Purge, SecondWind, FirstAid, IgnorePainTank);
            var plan = Plan(armsman, Archetype.Melee, (int)eCharacterClass.Armsman, 300, Cost);
            foreach (string key in Keys)
                Assert.That(plan.ContainsKey(key), Is.False, key);
        }
    }
}
