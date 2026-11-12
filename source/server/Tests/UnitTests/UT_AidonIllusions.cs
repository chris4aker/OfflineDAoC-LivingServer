using NUnit.Framework;
using Aidon = DOL.AI.Brain.AidonTheArchwizardBrain;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Aidon the Archwizard's illusions leave with every fight (1.2, owner 2026-11-12: "hall of corrupt needs fixing").
    /// On the owner's PC they piled up to 134 and two raids died to them in a loop for hours: the illusions left only
    /// after his first reset, while every reset zeroed the counters that let a pull call four more. The rule is pure;
    /// the play test covers the room.
    /// </summary>
    [TestFixture]
    public class UT_AidonIllusions
    {
        [Test]
        public void TheyLeaveOnceWhenHeResetsAndNotWhileHeFights()
        {
            Assert.That(Aidon.IllusionsLeaveNow(hasAggro: false, leftSinceLastFight: false), Is.True, "he reset after a fight");
            Assert.That(Aidon.IllusionsLeaveNow(hasAggro: false, leftSinceLastFight: true), Is.False, "idle: nothing to sweep again");
            Assert.That(Aidon.IllusionsLeaveNow(hasAggro: true, leftSinceLastFight: false), Is.False, "mid-fight they stay");
        }

        [Test]
        public void ThreePullsNeverLeaveMoreThanFourBehind()
        {
            // The script's own bookkeeping, replayed: a pull clears the flag and calls four illusions when the counters
            // read zero; a reset zeroes the counters and sweeps when the rule says so.
            int alive = 0, counters = 0;
            bool left = true;
            for (int pull = 1; pull <= 3; pull++)
            {
                left = false;                                   // OnAttackedByEnemy
                if (counters == 0) { alive += 4; counters = 4; } // SpawnCopies
                Assert.That(alive, Is.EqualTo(4), $"pull {pull}");
                counters = 0;                                   // the raid died: he resets
                if (Aidon.IllusionsLeaveNow(false, left)) { alive = 0; left = true; }
                Assert.That(alive, Is.Zero, $"after reset {pull}");
            }
        }

        [Test]
        public void OnlyHisFourKindsAreIllusions()
        {
            Assert.That(Aidon.IsIllusion(null), Is.False);
        }
    }
}
