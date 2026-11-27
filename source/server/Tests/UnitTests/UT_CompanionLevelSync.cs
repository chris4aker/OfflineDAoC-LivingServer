using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #571 step 6 (DetShaft 56972bb08): /spawn helpers level with their owner.
    [TestFixture]
    public class UT_CompanionLevelSync
    {
        [TestCase(30, 20, 50, 30)]   // behind: catches up
        [TestCase(20, 20, 50, 20)]   // level: no change
        [TestCase(15, 20, 50, 20)]   // the owner is lower: never levels down
        [TestCase(60, 20, 50, 50)]   // clamped to the max level
        [TestCase(0, 1, 50, 1)]      // never below 1
        public void TargetLevelIsTheOwnersButNeverLower(int owner, int bot, int max, int expected)
        {
            Assert.That(CompanionLevelSync.TargetLevel(owner, bot, max), Is.EqualTo((byte)expected));
        }

        [Test]
        public void OnlyAHelperInItsOwnersGroupFollows()
        {
            object group = new object(), other = new object();
            Assert.That(CompanionLevelSync.InOwnersGroup(group, group), Is.True);
            Assert.That(CompanionLevelSync.InOwnersGroup(group, other), Is.False);
            Assert.That(CompanionLevelSync.InOwnersGroup(null, null), Is.False);
            Assert.That(CompanionLevelSync.InOwnersGroup(group, null), Is.False);
        }

        [Test]
        public void KitRollsInTheSpawnBandAndNeverBelowTheOldKit()
        {
            for (double roll = 0; roll < 1; roll += 0.05)
            {
                int gear = CompanionLevelSync.GearLevel(30, 0, roll);
                Assert.That(gear, Is.InRange(20, 30));
                // An old kit at 25 is never replaced by a lower roll.
                Assert.That(CompanionLevelSync.GearLevel(30, 25, roll), Is.InRange(25, 30));
            }
            Assert.That(CompanionLevelSync.GearLevel(30, 0, 0), Is.EqualTo(20));
            Assert.That(CompanionLevelSync.GearLevel(30, 0, 0.999), Is.EqualTo(30));
            // An old kit above the new band's top (cannot happen upward-only) is clamped to the level.
            Assert.That(CompanionLevelSync.GearLevel(30, 40, 0.5), Is.EqualTo(30));
            // Low levels stay inside 1..level.
            Assert.That(CompanionLevelSync.GearLevel(3, 0, 0), Is.EqualTo(1));
            Assert.That(CompanionLevelSync.GearLevel(3, 0, 0.999), Is.EqualTo(3));
        }

        [Test]
        public void AtFiftyTheEndgameKitIsUnchanged()
        {
            Assert.That(CompanionLevelSync.GearLevel(50, 0, 0), Is.EqualTo(50));
            Assert.That(CompanionLevelSync.GearLevel(50, 45, 0.3), Is.EqualTo(50));
        }

        [Test]
        public void OnlySpawnHelpersFollowTheOwnersLevel()
        {
            Assert.That(CompanionLevelSync.Applies(temporaryHelper: true, autonomous: false, specialForce: false), Is.True);
            // Hired and /bot companions are not temporary helpers.
            Assert.That(CompanionLevelSync.Applies(false, false, false), Is.False);
            // Autonomous gamebots, including one grouped with the player, keep their own progression.
            Assert.That(CompanionLevelSync.Applies(false, true, false), Is.False);
            Assert.That(CompanionLevelSync.Applies(true, true, false), Is.False);
            // The Epic Special Forces too.
            Assert.That(CompanionLevelSync.Applies(true, false, true), Is.False);
            Assert.That(CompanionLevelSync.Applies(false, false, true), Is.False);
        }
    }
}
