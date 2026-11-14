using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>dragon-fight L3: a healer held by a long crowd control in a PvE fight purges it.</summary>
    [TestFixture]
    public class UT_BotPurgeOnLongStun
    {
        // inCombat, health, battleground, healer, controlRemainingMs
        [TestCase(true, 100, false, true, 39_000, true)]    // a healer in the dragon's 40 s stun
        [TestCase(true, 100, false, true, 3_000, false)]    // a short monster stun: not worth Purge
        [TestCase(true, 100, false, false, 39_000, false)]  // a fighter at full health in PvE waits it out, as before
        [TestCase(false, 100, false, true, 39_000, false)]  // out of a fight: no
        [TestCase(true, 100, true, false, 3_000, true)]     // against other realms: as before
        [TestCase(true, 50, false, false, 3_000, true)]     // going badly: as before
        public void PurgeRule(bool inCombat, int health, bool battleground, bool healer, long remaining, bool expected) =>
            Assert.That(AutonomousBotRealmAbilities.PurgeWanted(inCombat, health, battleground, healer, remaining),
                Is.EqualTo(expected));
    }
}
