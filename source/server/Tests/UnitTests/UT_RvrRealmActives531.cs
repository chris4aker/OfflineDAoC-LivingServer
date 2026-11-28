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
    /// a death, for autonomous gamebots in the open field. Step 2c-2: Perfect Recovery after a death and Vanish on a run
    /// from a lost fight, and the archers' True Sight once an enemy assassin strikes the group (owner answer 71A). The rules
    /// and the builds; the wiring is shown by the #520 rehearsal rows. Tests only.
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

        // ---- 2c-2: Perfect Recovery ----

        private static Fallen Dead(RvrRole role, double distance, bool sight = true, bool raised = false) => new(role, distance, sight, raised);

        [Test]
        public void PerfectRecoveryRaisesAHealerFirstThenACasterThenTheNearest()
        {
            var fallen = new List<Fallen> { Dead(RvrRole.Tank, 200), Dead(RvrRole.Caster, 900), Dead(RvrRole.Healer, 1_400), Dead(RvrRole.Melee, 100) };
            Assert.That(PerfectRecoveryPick(fallen), Is.EqualTo(2), "the healer, though farthest");
            fallen.RemoveAt(2);
            Assert.That(PerfectRecoveryPick(fallen), Is.EqualTo(1), "then the caster");
            fallen.RemoveAt(1);
            Assert.That(PerfectRecoveryPick(fallen), Is.EqualTo(1), "then the nearest");
            Assert.That(PerfectRecoveryPick(new[] { Dead(RvrRole.Healer, 1_200), Dead(RvrRole.Healer, 300) }), Is.EqualTo(1), "the nearer of two healers");
        }

        [Test]
        public void PerfectRecoverySkipsACorpseOutOfReachOutOfSightOrAlreadyBeingRaised()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PerfectRecoveryPick(new[] { Dead(RvrRole.Healer, PerfectRecoveryRange + 1) }), Is.EqualTo(-1), "beyond 1,500");
                Assert.That(PerfectRecoveryPick(new[] { Dead(RvrRole.Healer, PerfectRecoveryRange) }), Is.Zero, "at 1,500");
                Assert.That(PerfectRecoveryPick(new[] { Dead(RvrRole.Healer, 500, sight: false) }), Is.EqualTo(-1), "out of sight");
                Assert.That(PerfectRecoveryPick(new[] { Dead(RvrRole.Healer, 500, raised: true) }), Is.EqualTo(-1), "a rez already on the way");
                Assert.That(PerfectRecoveryPick(new[] { Dead(RvrRole.Healer, 500, raised: true), Dead(RvrRole.Tank, 800) }), Is.EqualTo(1),
                    "the next corpse instead");
                Assert.That(PerfectRecoveryPick(new List<Fallen>()), Is.EqualTo(-1), "nobody dead");
                Assert.That(PerfectRecoveryPick(null), Is.EqualTo(-1));
            });
        }

        [TestCase(true, false, true, ExpectedResult = true, TestName = "PR: in the fight, a mate down")]
        [TestCase(false, false, true, ExpectedResult = false, TestName = "PR: the fight is over (the ordinary rez does it)")]
        [TestCase(true, true, true, ExpectedResult = false, TestName = "PR: casting something else")]
        [TestCase(true, false, false, ExpectedResult = false, TestName = "PR: nobody to raise")]
        public bool PerfectRecoveryFiresAfterADeathInTheFight(bool inFight, bool casting, bool someoneDown) =>
            PerfectRecoveryWanted(inFight, casting, someoneDown ? new[] { Dead(RvrRole.Tank, 600) } : new Fallen[0]);

        [Test]
        public void ThePerfectRecoveryRezIsTheHandlers()
        {
            Spell rez = PerfectRecoverySpell(50, 1800);
            Assert.Multiple(() =>
            {
                Assert.That(rez.SpellType, Is.EqualTo(eSpellType.Resurrect));
                Assert.That(rez.Target, Is.EqualTo(eSpellTarget.CORPSE));
                Assert.That(rez.CastTime, Is.Zero, "instant");
                Assert.That(rez.Range, Is.EqualTo(PerfectRecoveryRange));
                Assert.That(rez.ResurrectHealth, Is.EqualTo(100));
                Assert.That(rez.ResurrectMana, Is.EqualTo(100));
                Assert.That(rez.Power, Is.Zero, "free");
                Assert.That(rez.RecastDelay, Is.EqualTo(1_800_000), "the spell code disables Perfect Recovery for 30 minutes");
            });
        }

        // ---- 2c-2: Vanish ----

        [TestCase(true, 1_000L, false, ExpectedResult = true, TestName = "Vanish: running, an enemy realm's hit a second ago")]
        [TestCase(true, VanishHitWindowMilliseconds, false, ExpectedResult = true, TestName = "Vanish: the hit at the window's edge")]
        [TestCase(true, VanishHitWindowMilliseconds + 1, false, ExpectedResult = false, TestName = "Vanish: no enemy realm's hit lately (monsters)")]
        [TestCase(true, -1L, false, ExpectedResult = false, TestName = "Vanish: never hit by an enemy realm")]
        [TestCase(false, 1_000L, false, ExpectedResult = false, TestName = "Vanish: not running")]
        [TestCase(true, 1_000L, true, ExpectedResult = false, TestName = "Vanish: already hidden")]
        public bool VanishFiresOnARunFromALostFight(bool retreat, long since, bool stealthed) => VanishWanted(retreat, since, stealthed);

        // ---- 2c-2: True Sight ----

        [TestCase(1, false, ExpectedResult = true, TestName = "TS: an enemy assassin struck the group")]
        [TestCase(2, false, ExpectedResult = true, TestName = "TS: two assassins")]
        [TestCase(0, false, ExpectedResult = false, TestName = "TS: no assassin (other enemies only)")]
        [TestCase(1, true, ExpectedResult = false, TestName = "TS: already running")]
        public bool TrueSightFiresOnceAnAssassinStrikes(int assassins, bool running) => TrueSightWanted(assassins, running);

        private static RvrStealthSighting Sight(double distance, bool trueSight, bool vanish = false, bool los = true, bool fighting = false) =>
            new(distance, 50, 40, Vanish: vanish, LineOfSight: los, ObserverInCombatWithOther: fighting, ObserverTrueSight: trueSight);

        [Test]
        public void OnTrueSightABotSeesEveryStealtherInItsSightAtOncePastTheBotCap()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrStealthNotice.ChancePerSecond(Sight(1_500, trueSight: false), RvrBotAwareness.Normal), Is.Zero,
                    "without True Sight: never beyond 350");
                Assert.That(RvrStealthNotice.ChancePerSecond(Sight(1_500, trueSight: true), RvrBotAwareness.Normal), Is.EqualTo(1),
                    "on True Sight: at once at 1,500 (owner answer 71A)");
                Assert.That(RvrStealthNotice.ChancePerSecond(Sight(3_500, trueSight: true), RvrBotAwareness.Easy), Is.EqualTo(1),
                    "at any range in sight, whatever the awareness");
                Assert.That(RvrStealthNotice.ChancePerSecond(Sight(800, trueSight: true, fighting: true), RvrBotAwareness.Normal), Is.EqualTo(1),
                    "even while fighting someone else, as a player on True Sight");
                Assert.That(RvrStealthNotice.ChancePerSecond(Sight(800, trueSight: true, los: false), RvrBotAwareness.Normal), Is.Zero,
                    "line of sight is still needed");
                Assert.That(RvrStealthNotice.ChancePerSecond(Sight(100, trueSight: true, vanish: true), RvrBotAwareness.Normal), Is.Zero,
                    "Vanish still hides");
                Assert.That(RvrStealthNotice.Limit(Sight(1_500, trueSight: true)), Is.EqualTo(RvrStealthNotice.TrueSightReach));
                Assert.That(RvrStealthNotice.Limit(Sight(1_500, trueSight: false)), Is.LessThanOrEqualTo(RvrStealthNotice.MaxRange));
                Assert.That(RvrStealthNotice.CandidateRange(true), Is.EqualTo(RvrStealthNotice.TrueSightReach));
                Assert.That(RvrStealthNotice.CandidateRange(false), Is.EqualTo(RvrStealthNotice.MaxRange));
            });
        }

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
        public void TheExecutorHandlesExactlyItsSixAndBotsMayUseThem()
        {
            Assert.That(Keys, Is.EquivalentTo(new[] { SpeedOfSound, MasteryOfConcentration, BunkerOfFaith, PerfectRecovery, Vanish, TrueSight }));
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
            dba = new DOL.Database.DbAbility { KeyName = PerfectRecovery, Name = "Perfect Recovery" };
            Assert.That(new AtlasOF_PerfectRecovery(dba, 1).GetReUseDelay(1), Is.EqualTo(1800));
            dba = new DOL.Database.DbAbility { KeyName = Vanish, Name = "Vanish" };
            Assert.That(new AtlasOF_Vanish(dba, 1).GetReUseDelay(1), Is.EqualTo(1800));
            dba = new DOL.Database.DbAbility { KeyName = TrueSight, Name = "True Sight" };
            Assert.That(new AtlasOF_TrueSight(dba, 1).GetReUseDelay(1), Is.EqualTo(1800));
        }

        // ---- the builds ----

        private static Dictionary<string, int> Catalog(params string[] keys) =>
            keys.Select(k => k.StartsWith("AtlasOF_") ? k : "AtlasOF_" + k).ToDictionary(k => k, k => k switch
            {
                Purge or PurgeReduced or SecondWind or IgnorePain or IgnorePainTank or SpeedOfSound or MasteryOfConcentration or BunkerOfFaith or
                    PerfectRecovery or Vanish or TrueSight => 1,
                FirstAid => 3,
                _ => 5,
            });

        private static int Cost(string key, int level) => key switch
        {
            Purge or SecondWind or SpeedOfSound or BunkerOfFaith or Vanish or TrueSight => 10,
            PurgeReduced => 4,
            IgnorePain or MasteryOfConcentration or PerfectRecovery => 14,
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
        public void AHealerBuysPerfectRecoveryAfterPurge()
        {
            var healer = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfHealing", "WildHealing", "Serenity", "Toughness", PurgeReduced,
                FirstAid, MasteryOfConcentration, PerfectRecovery);
            Assert.That(ArchetypeOf(healer.Keys), Is.EqualTo(Archetype.Healer));
            int purge = FirstPointsBuying(healer, Archetype.Healer, (int)eCharacterClass.Healer, PurgeReduced);
            int pr = FirstPointsBuying(healer, Archetype.Healer, (int)eCharacterClass.Healer, PerfectRecovery);
            Assert.That(pr, Is.GreaterThan(purge).And.LessThanOrEqualTo(70), $"purge at {purge}, Perfect Recovery at {pr}");
        }

        [Test]
        public void AnInfiltratorBuysVanishAfterPurge()
        {
            var infiltrator = Catalog("AugStr", "AugCon", "AugDex", "AugQui", "MasteryOfPain", "Dodger", "Toughness", Purge, SecondWind,
                FirstAid, IgnorePain, Vanish, "SeeHidden");
            Assert.That(ArchetypeOf(infiltrator.Keys), Is.EqualTo(Archetype.Melee));
            int purge = FirstPointsBuying(infiltrator, Archetype.Melee, (int)eCharacterClass.Infiltrator, Purge);
            int vanish = FirstPointsBuying(infiltrator, Archetype.Melee, (int)eCharacterClass.Infiltrator, Vanish);
            Assert.That(vanish, Is.GreaterThan(purge).And.LessThanOrEqualTo(60), $"purge at {purge}, Vanish at {vanish}");
        }

        [TestCase(eCharacterClass.Scout)]
        [TestCase(eCharacterClass.Hunter)]
        [TestCase(eCharacterClass.Ranger)]
        public void AnArcherBuysTrueSightAfterPurge(eCharacterClass archer)
        {
            var catalog = Catalog("AugStr", "AugCon", "AugDex", "AugQui", "MasteryOfArchery", "FalconsEye", "Dodger", "Toughness",
                PurgeReduced, SecondWind, FirstAid, TrueSight, "MasteryOfPain", "Determination");
            Assert.That(ArchetypeOf(catalog.Keys), Is.EqualTo(Archetype.Archer));
            int purge = FirstPointsBuying(catalog, Archetype.Archer, (int)archer, PurgeReduced);
            int ts = FirstPointsBuying(catalog, Archetype.Archer, (int)archer, TrueSight);
            Assert.That(ts, Is.GreaterThan(purge).And.LessThanOrEqualTo(70), $"purge at {purge}, True Sight at {ts}");
        }

        [Test]
        public void TheClericAndTheMinstrelBuyNeitherPerfectRecoveryNorVanish()
        {
            var cleric = Catalog("AugAcuity", "AugCon", "AugDex", "MasteryOfHealing", "WildHealing", "Serenity", "Toughness", PurgeReduced,
                FirstAid, MasteryOfConcentration, BunkerOfFaith);
            var minstrel = Catalog("AugStr", "AugCon", "AugDex", "AugQui", "AugAcuity", "MasteryOfPain", "MasteryOfBlocking", "MasteryOfArms",
                "Toughness", "Serenity", Purge, SecondWind, FirstAid, IgnorePain, SpeedOfSound);
            var clericPlan = Plan(cleric, Archetype.Healer, (int)eCharacterClass.Cleric, 300, Cost);
            var minstrelPlan = Plan(minstrel, Archetype.Melee, (int)eCharacterClass.Minstrel, 300, Cost);
            Assert.That(clericPlan.ContainsKey(PerfectRecovery) || clericPlan.ContainsKey(Vanish) || clericPlan.ContainsKey(TrueSight), Is.False);
            Assert.That(minstrelPlan.ContainsKey(PerfectRecovery) || minstrelPlan.ContainsKey(Vanish) || minstrelPlan.ContainsKey(TrueSight), Is.False);
            Assert.That(clericPlan.ContainsKey(BunkerOfFaith), Is.True, "the Cleric keeps its Bunker");
            Assert.That(minstrelPlan.ContainsKey(SpeedOfSound), Is.True, "the Minstrel keeps its Speed of Sound");
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
