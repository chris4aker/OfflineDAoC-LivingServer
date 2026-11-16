using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// GitHub #49: the same hired companion showed twice in the group window. A copy of the companion that is no longer
    /// in the world must not stay in the group when the companion is summoned again.
    /// </summary>
    [TestFixture]
    public class UT_CompanionStaleCopy
    {
        [Test]
        public void OnlyALeftoverObjectOfTheSameCompanionIsStale() => Assert.Multiple(() =>
        {
            Assert.That(HiredCompanions165.IsStaleCopy(memberBotId: 77, memberIsLiveObject: false, summonedBotId: 77), Is.True);
            Assert.That(HiredCompanions165.IsStaleCopy(77, true, 77), Is.False, "the companion that is in the world");
            Assert.That(HiredCompanions165.IsStaleCopy(78, false, 77), Is.False, "another companion or gamebot in the group");
            Assert.That(HiredCompanions165.IsStaleCopy(0, false, 0), Is.False, "no id: never");
        });
    }
}
