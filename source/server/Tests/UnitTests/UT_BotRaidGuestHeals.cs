using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>dragon-fight M1: a realm raid's healers heal a real player of the realm fighting beside it.</summary>
    [TestFixture]
    public class UT_BotRaidGuestHeals
    {
        [TestCase(true, true, true, true)]     // the horn sounder in the dragon fight
        [TestCase(true, false, true, false)]   // another realm's player
        [TestCase(true, true, false, false)]   // a realm player standing by, not fighting
        [TestCase(false, true, true, false)]   // dead: the resurrection rules, not heals
        public void RaidGuestRule(bool alive, bool sameRealm, bool inCombat, bool guest) =>
            Assert.That(BotGroupSupport.IsRaidGuest(alive, sameRealm, inCombat), Is.EqualTo(guest));
    }
}
