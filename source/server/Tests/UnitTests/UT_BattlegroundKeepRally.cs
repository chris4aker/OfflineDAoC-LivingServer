using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using Rally = DOL.GS.AutonomousBattlegroundKeepRally;

namespace DOL.UnitTests
{
    /// <summary>
    /// Battleground keep lords are taken together (playtest 2026-11-04, Thidranki: 210 level-22 bots for 45 minutes, gates
    /// down, the level-33 Renegade Lord at 100% throughout; waves of 8-9 and single stormers). Only the pure rules are
    /// tested here: the rally itself needs a live battleground (see the in-game check list in the PR).
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundKeepRally
    {
        // The Thidranki lord at level 33: MaxHealthCalculator's NPC formula (11·34 + 0.6·33² + (30-25)·1.8 = 1,036),
        // GameKeepGuard adds 2 per level (1,102) and GuardLord triples it.
        private const int ThidrankiLordLevel = 33;
        private const int ThidrankiLordHealth = 3_306;
        // The 2026-11-04 attackers: level 22, maxHealth 272-445 in the death lines.
        private const int AttackerLevel = 22;
        private const int AttackerHealth = 370;
        // A level-28 Thidranki guard: 11·29 + 0.6·28² + 9 + 56.
        private const int GuardLevel = 28;
        private const int GuardHealth = 854;

        [Test]
        public void TheThidrankiLordNeedsMoreThanTheWavesThatWentIn()
        {
            int alone = Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth, AttackerLevel, AttackerHealth);
            int withGuards = Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth, AttackerLevel, AttackerHealth,
                3, GuardLevel, GuardHealth);
            Assert.Multiple(() =>
            {
                Assert.That(alone, Is.EqualTo(12), "level 22s against the lord alone");
                Assert.That(withGuards, Is.EqualTo(24), "three level-28 guards beside him");
                // The playtest's waves went in with 8 and 9 (BG_WAVE_ASSAULT gathered=8/9) and never touched him.
                Assert.That(alone, Is.GreaterThan(9));
            });
        }

        [Test]
        public void EnoughFollowsTheRealStrengthOnBothSides()
        {
            int baseline = Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth, AttackerLevel, AttackerHealth);
            Assert.Multiple(() =>
            {
                Assert.That(Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth, 24, 400), Is.LessThan(baseline),
                    "level 24s with more health need fewer");
                Assert.That(Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth, 20, 320), Is.GreaterThan(baseline),
                    "level 20s need more");
                Assert.That(Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth / 2, AttackerLevel, AttackerHealth),
                    Is.LessThan(baseline), "a lord already at half health needs fewer");
                Assert.That(Rally.NeededAttackers(ThidrankiLordLevel + 4, ThidrankiLordHealth, AttackerLevel, AttackerHealth),
                    Is.GreaterThan(baseline), "a higher lord needs more");
                Assert.That(Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth, AttackerLevel, AttackerHealth, 1, GuardLevel, GuardHealth),
                    Is.GreaterThan(baseline), "every guard with him counts");
            });
        }

        [Test]
        public void EnoughIsBounded()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.NeededAttackers(25, 2_000, 24, 400), Is.EqualTo(Rally.MinimumRally), "never fewer than a wave");
                Assert.That(Rally.NeededAttackers(60, 30_000, 20, 300, 10, 50, 5_000), Is.EqualTo(Rally.MaximumRally));
                Assert.That(Rally.NeededAttackers(33, 0, 22, 370), Is.EqualTo(Rally.MinimumRally), "a dying lord");
                Assert.That(Rally.NeededAttackers(33, 3_306, 22, 0), Is.InRange(Rally.MinimumRally, Rally.MaximumRally), "no health read");
            });
        }

        [Test]
        public void DamageFollowsTheBaseGamesLevelRules()
        {
            Assert.Multiple(() =>
            {
                // 18% base miss and 1% weapon skill per level: level for level, the reference weapon at 82%.
                Assert.That(Rally.DamageRate(22, 22), Is.EqualTo(7.8 * 0.82).Within(1e-9));
                Assert.That(Rally.DamageRate(22, 33), Is.LessThan(Rally.DamageRate(22, 22)));
                Assert.That(Rally.DamageRate(33, 22), Is.GreaterThan(Rally.DamageRate(22, 33) * 2), "the lord outclasses one attacker");
                Assert.That(Rally.LordTakes, Is.LessThan(Rally.GuardTakes(GuardLevel)), "his 40% resists");
                Assert.That(Rally.GuardTakes(28), Is.EqualTo(0.85).Within(1e-9), "level/2+1 resists");
            });
        }

        [TestCase(14, 14, 0, true)]                 // all there
        [TestCase(13, 14, 2 * 60_000, false)]       // one short, not yet patient
        [TestCase(13, 26, 3 * 60_000, true)]        // patient after 3 minutes: half of 26 is 13
        [TestCase(12, 26, 3 * 60_000, false)]
        [TestCase(8, 14, 3 * 60_000, true)]         // half of 14 is 7, but never fewer than 8
        [TestCase(8, 30, 6 * 60_000, true)]         // last call after 6 minutes: whoever is there, at least 8
        [TestCase(8, 30, 6 * 60_000 - 1, false)]
        [TestCase(7, 30, 30 * 60_000, false)]       // never a handful
        [TestCase(9, 9, 0, true)]
        public void TheRallyGoesUpWithEnough(int rallied, int needed, long gathering, bool goes) =>
            Assert.That(Rally.ShouldStorm(rallied, needed, gathering), Is.EqualTo(goes));

        [Test]
        public void ARealPlayerAtTheRallyCountsAsTwoBots()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.PlayerWeight, Is.EqualTo(2));
                Assert.That(Rally.RallyStrength(12, 0), Is.EqualTo(12));
                Assert.That(Rally.RallyStrength(10, 1), Is.EqualTo(12), "you and ten bots go up where twelve bots would");
                Assert.That(Rally.RallyStrength(0, 1), Is.EqualTo(2));
                Assert.That(Rally.RallyStrength(-1, -1), Is.EqualTo(0));
                // With you there, a 12-bot rally starts with 10 gamebots.
                int needed = Rally.NeededAttackers(ThidrankiLordLevel, ThidrankiLordHealth, AttackerLevel, AttackerHealth);
                Assert.That(Rally.ShouldStorm(Rally.RallyStrength(needed - 2, 1), needed, 0), Is.True);
                Assert.That(Rally.ShouldStorm(Rally.RallyStrength(needed - 3, 1), needed, 0), Is.False);
            });
        }

        [Test]
        public void ARallyThatLapsedStartsOver()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.IsLapsed(0, 5_000_000), Is.False, "a new rally");
                Assert.That(Rally.IsLapsed(1_000_000, 1_000_000 + Rally.LapseMilliseconds), Is.False);
                Assert.That(Rally.IsLapsed(1_000_000, 1_000_001 + Rally.LapseMilliseconds), Is.True,
                    "the gates were rebuilt for a while: the patience clock does not carry over");
            });
        }

        [TestCase(8, 4)]
        [TestCase(14, 4)]
        [TestCase(30, 8)]
        [TestCase(60, 15)]
        public void LateArrivalsGoUpInGroups(int needed, int batch) =>
            Assert.That(Rally.ReinforcementBatch(needed), Is.EqualTo(batch));

        [Test]
        public void AnAttemptEndsLikeARaidAttempt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Rally.AttemptOver(14, 14, 100, 100, 60_000), Is.Null, "on the way up");
                Assert.That(Rally.AttemptOver(14, 8, 40, 40, 10_000), Is.Null, "fighting, lord going down");
                Assert.That(Rally.AttemptOver(14, 7, 40, 40, 10_000), Is.EqualTo("half the stormers fell"));
                Assert.That(Rally.AttemptOver(14, 0, 100, 100, 0), Is.EqualTo("half the stormers fell"), "a wipe");
                Assert.That(Rally.AttemptOver(14, 12, 96, 80, 10_000), Is.EqualTo("the lord healed back"));
                Assert.That(Rally.AttemptOver(14, 12, 90, 80, 10_000), Is.Null);
                Assert.That(Rally.AttemptOver(14, 14, 100, 100, Rally.NoProgressMilliseconds), Is.EqualTo("no progress on the lord"));
            });
        }

        [Test]
        public void TheRallyStandsJustInsideTheBreachOnTheGatesFloor()
        {
            // Thidranki Faste, 2026-11-04: gate 252000302 and the lord above it.
            Vector3 gate = new(32654, 38180, 3720);
            Vector3 lord = new(32296, 38267, 4592);
            Vector3 point = Rally.RallyAnchor(gate, lord);
            Assert.Multiple(() =>
            {
                Assert.That(point.Z, Is.EqualTo(gate.Z), "not up on the lord's floor");
                // The lord stands 368 from this gate across the floor: the rally goes halfway (184), short of the full 200.
                Assert.That(Vector2.Distance(new(point.X, point.Y), new(gate.X, gate.Y)), Is.EqualTo(184.2).Within(0.5));
                Vector3 eastGate = new(34333, 38275, 3720);
                Vector3 east = Rally.RallyAnchor(eastGate, lord);
                Assert.That(Vector3.Distance(east, eastGate), Is.EqualTo(Rally.RallyStepInside).Within(0.5), "the east gate, 2,000 away: the full 200");
                Assert.That(Vector2.Distance(new(point.X, point.Y), new(lord.X, lord.Y)),
                    Is.LessThan(Vector2.Distance(new(gate.X, gate.Y), new(lord.X, lord.Y))), "toward the lord");
                // A gate right under him: at most halfway, never past him.
                Vector3 under = Rally.RallyAnchor(new(1000, 1000, 0), new(1100, 1000, 800));
                Assert.That(under, Is.EqualTo(new Vector3(1050, 1000, 0)));
                Assert.That(Rally.RallyAnchor(gate, new(gate.X, gate.Y, 5000)), Is.EqualTo(gate));
            });
        }

        [TestCase(true, true, true, true)]
        [TestCase(true, false, true, false)]    // left the lord (died, released, chased out): through a gate again
        [TestCase(true, true, false, false)]    // another keep
        [TestCase(false, true, true, false)]
        public void AStormerComesInThroughTheGateEachTime(bool passed, bool nearLord, bool sameKeep, bool keeps) =>
            Assert.That(AutonomousWorldBotController.KeepsGatePassed(passed, nearLord, sameKeep), Is.EqualTo(keeps));

        [Test]
        public void RoamersCircleAnEnemyKeepOutsideItsWalls()
        {
            // Thidranki Faste's centre is about where the ten failed roam points of 2026-11-04 clustered; each of them lay
            // within 1,050 of it (courtyard z 3721 and wall tops z 3977).
            Vector3 keep = new(33200, 38400, 3720);
            Vector3 albionArrival = new(36393, 50586, 3730);
            Vector3 anchor = AutonomousWorldBotController.BattlegroundRoamAnchorOutside(keep, albionArrival);
            float fromKeep = Vector2.Distance(new(anchor.X, anchor.Y), new(keep.X, keep.Y));
            Assert.Multiple(() =>
            {
                Assert.That(fromKeep, Is.EqualTo(AutonomousWorldBotController.BattlegroundRoamKeepDistance).Within(0.5));
                Assert.That(fromKeep - 900, Is.GreaterThan(1_050), "the 900 random spread stays outside the walls");
                Assert.That(anchor.Y, Is.GreaterThan(keep.Y), "on Albion's side");
                Assert.That(AutonomousWorldBotController.BattlegroundRoamAnchorOutside(keep, keep),
                    Is.EqualTo(keep + new Vector3(AutonomousWorldBotController.BattlegroundRoamKeepDistance, 0, 0)));
            });
        }

        [Test]
        public void BattlegroundFightsKeepTheBattlegroundGoal() =>
            Assert.That(AutonomousWorldBotController.BattlegroundGoal(BattlegroundBrackets.ForLevel(22)),
                Is.EqualTo("Battleground: Thidranki (levels 20-24)"), "the goal the playtest's storm lines showed");
    }
}
