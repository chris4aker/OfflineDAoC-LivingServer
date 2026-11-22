using DOL.GS;
using DOL.GS.Keeps;
using NUnit.Framework;
using Rally = DOL.GS.AutonomousBattlegroundKeepRally;

namespace DOL.UnitTests
{
    /// <summary>
    /// #416 "Battleground keeps rarely change hands". A keep's lord takes no damage from an attacker farther than 400
    /// (GuardLord.TakeDamage: "immune to damage from this range"), but stormers struck him from up to 900, so casters,
    /// archers and healers in his fight cast and shot for nothing (owner's PC, build 43c43aa, Caer Caledon: storms reached
    /// him and he lost 1-3%). Now every stormer strikes him only from within his damage radius and walks on to him
    /// otherwise. Pure.
    /// </summary>
    [TestFixture]
    public class UT_BattlegroundLordReach
    {
        [Test]
        public void TheStrikeReachIsInsideTheLordsDamageRadius()
        {
            Assert.That(GuardLord.KeepDamageRadius, Is.EqualTo(400), "the upstream rule, unchanged");
            Assert.That(GuardLord.TowerDamageRadius, Is.EqualTo(300));
            Assert.That(Rally.LordDamageReach, Is.LessThan(GuardLord.KeepDamageRadius));
            Assert.That(Rally.LordDamageReach, Is.GreaterThan(200f), "near enough for a melee bot's own step in");
            Assert.That(Rally.LordDamageReach, Is.LessThan(AutonomousWorldBotController.BattlegroundLordStrike),
                "the 900 strike radius was what let casters stand off");
        }

        [TestCase(700f, 0f, false)]  // a caster on his floor at 700: walks on (struck for nothing before)
        [TestCase(450f, 20f, false)] // just outside his 400
        [TestCase(300f, 40f, true)]  // inside: strikes
        [TestCase(120f, 0f, true)]
        [TestCase(300f, 200f, false)] // inside on the flat but a floor below
        public void AStormerStrikesTheLordOnlyFromWhereItsHitsCount(float distance, float height, bool strikes) =>
            Assert.That(Rally.StrikesFromHere(true, distance, height), Is.EqualTo(strikes));

        [Test]
        public void FrontierKeepAndTowerLordsHaveTheirOwnReach()
        {
            Assert.That(GuardLord.DamageRadiusFor(true), Is.EqualTo(GuardLord.KeepDamageRadius));
            Assert.That(GuardLord.DamageRadiusFor(false), Is.EqualTo(GuardLord.TowerDamageRadius));
            Assert.That(RvrLordReach.For(true), Is.EqualTo(350f));
            Assert.That(RvrLordReach.For(false), Is.EqualTo(250f), "a tower's lord: 300, so 250");
            Assert.That(RvrLordReach.For(true), Is.EqualTo(Rally.LordDamageReach), "the battleground storm uses the same reach");
        }

        [TestCase(true, 1_800f, false)] // a frontier caster that saw the exposed lord from the courtyard: steps in
        [TestCase(true, 380f, false)]
        [TestCase(true, 340f, true)]
        [TestCase(false, 340f, false)]  // the same distance at a tower's lord: still too far
        [TestCase(false, 240f, true)]
        public void AFrontierAttackerStrikesALordOnlyFromWithinReach(bool keep, float distance, bool strikes) =>
            Assert.That(RvrLordReach.InReach(keep, distance), Is.EqualTo(strikes));

        [Test]
        public void OtherTargetsKeepTheirReach()
        {
            Assert.That(Rally.StrikesFromHere(false, 1_200f, 50f), Is.True, "a guard on its floor is struck from where it stands");
            Assert.That(Rally.StrikesFromHere(false, 300f, 308f), Is.False);
        }
    }
}
