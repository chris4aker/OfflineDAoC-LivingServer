using DOL.GS;
using NUnit.Framework;

namespace DOL.Tests
{
    /// <summary>Pure rules behind the boss script fixes ported from Offline DAoC True 1.65 (shadowofze, 2026-11-12).</summary>
    [TestFixture]
    public class UT_BossScriptRules
    {
        [TestCase(true, false, false, true, false, true, TestName = "A monster of the boss's realm answers his call")]
        [TestCase(true, true, false, true, false, false, TestName = "A gamebot in the room is not ordered to attack")]
        [TestCase(true, false, true, true, false, false, TestName = "A pet in the room is not ordered to attack")]
        [TestCase(false, false, false, true, false, false, TestName = "An NPC of another realm is not ordered to attack")]
        [TestCase(true, false, false, false, false, false, TestName = "A corpse is not ordered to attack")]
        [TestCase(true, false, false, true, true, false, TestName = "A peaceful NPC is not ordered to attack")]
        public void BossCallReachesItsOwnKindOnly(bool sameRealm, bool isGameBot, bool controlled, bool alive, bool peaceful, bool expected)
        {
            Assert.That(EpicEncounterMechanics.IsOwnKind(sameRealm, isGameBot, controlled, alive, peaceful), Is.EqualTo(expected));
        }
    }
}
