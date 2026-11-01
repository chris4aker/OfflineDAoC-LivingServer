using DOL.AI.Brain;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Bring A Friend for monsters with no faction: their own kind comes (owner 2026-10-28).</summary>
    [TestFixture]
    public sealed class UT_BafFriends
    {
        [Test]
        public void AMonsterWithAFactionKeepsTheFactionRule() => Assert.Multiple(() =>
        {
            Assert.That(BafFriends.IsFriend(true, true, true, false, true), Is.True, "an allied faction, another name");
            Assert.That(BafFriends.IsFriend(true, false, false, true, true), Is.False, "same name but no faction friendship");
        });

        [Test]
        public void AMonsterWithNoFactionBringsItsOwnKind() => Assert.Multiple(() =>
        {
            Assert.That(BafFriends.IsFriend(false, false, false, true, true), Is.True, "a windswept wraith brings a windswept wraith");
            Assert.That(BafFriends.IsFriend(false, false, false, false, true), Is.False, "not a frore lich");
            Assert.That(BafFriends.IsFriend(false, false, true, true, true), Is.False, "a same-named monster with a faction answers to its faction");
            Assert.That(BafFriends.IsFriend(false, false, false, true, false), Is.False, "the setting off: OpenDAoC as it was");
        });
    }
}
