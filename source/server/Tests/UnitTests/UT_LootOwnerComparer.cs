using DOL.GS;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Loot owners match by their comparand's value (owner 2026-10-31: solo bots never found their bags).</summary>
    [TestFixture]
    public class UT_LootOwnerComparer
    {
        private sealed class FreshStringOwner : IGameStaticItemOwner
        {
            private readonly long _id;
            public FreshStringOwner(long id) { _id = id; }
            public string Name => "owner";
            // a new string on every call, like GameBot's "offlinebot:<id>"
            public object GameStaticItemOwnerComparand => string.Concat("offlinebot:", _id.ToString());
            public TryPickUpResult TryAutoPickUpMoney(GameMoney money) => TryPickUpResult.DoesNotWant;
            public TryPickUpResult TryAutoPickUpItem(WorldInventoryItem item) => TryPickUpResult.DoesNotWant;
            public TryPickUpResult TryPickUpMoney(GamePlayer source, GameMoney money) => TryPickUpResult.DoesNotWant;
            public TryPickUpResult TryPickUpItem(GamePlayer source, WorldInventoryItem item) => TryPickUpResult.DoesNotWant;
        }

        [Test]
        public void TheSameBotMatchesItsOwnBagEvenWithAFreshComparandString()
        {
            var comparer = new IGameStaticItemOwner.OwnerEqualityComparer();
            var owners = new System.Collections.Generic.HashSet<IGameStaticItemOwner>(comparer) { new FreshStringOwner(42) };
            Assert.That(owners.Contains(new FreshStringOwner(42)), Is.True);
            Assert.That(owners.Contains(new FreshStringOwner(43)), Is.False);
        }
    }
}
