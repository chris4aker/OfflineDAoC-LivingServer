using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Keep hasteners (#565 step 4a, port of OpenDAoC-Core 43924def4): on PvE and PvP servers the server rules decide who
    /// is served; on the normal RvR server only the hastener's own realm is (a PEACE NPC counts as everyone's realm in
    /// NormalServerRules). Players only: hasteners serve on interact, and gamebots and companions don't interact.
    /// </summary>
    [TestFixture]
    public sealed class UT_HastenerRealm
    {
        [TestCase(true, ExpectedResult = true)]
        [TestCase(false, ExpectedResult = false)]
        public bool OnTheRvrServerOnlyTheHastenersRealmIsServed(bool sameRealm)
        {
            bool rulesAsked = false;
            bool served = FrontierHastener.Serves(EGameServerType.GST_Normal, sameRealm, () => rulesAsked = true);
            Assert.That(rulesAsked, Is.False, "the PEACE flag must not open an enemy keep's hastener");
            return served;
        }

        [TestCase(EGameServerType.GST_PvE)]
        [TestCase(EGameServerType.GST_PvP)]
        public void OnPveAndPvpServersTheServerRulesDecide(EGameServerType type)
        {
            Assert.Multiple(() =>
            {
                Assert.That(FrontierHastener.Serves(type, false, () => true), Is.True, "another realm served where the rules allow it");
                Assert.That(FrontierHastener.Serves(type, true, () => false), Is.False);
            });
        }

        [TestCase(EGameServerType.GST_Casual)]
        [TestCase(EGameServerType.GST_Roleplay)]
        public void OtherServerTypesKeepTheRealmCheck(EGameServerType type)
        {
            Assert.Multiple(() =>
            {
                Assert.That(FrontierHastener.Serves(type, true, () => false), Is.True);
                Assert.That(FrontierHastener.Serves(type, false, () => true), Is.False);
            });
        }
    }
}
