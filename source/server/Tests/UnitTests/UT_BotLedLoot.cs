using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.BotLedGroups;

namespace DOL.UnitTests
{
    /// <summary>
    /// Loot in a group with the real player and gamebots (owner's decision 2, 2026-11-04; docs/BOT-LED-GROUPS.md): the
    /// player counts as two members (a setting) in the coin split and the item rotation, whoever leads, and an item the
    /// player's class can use that beats what they wear comes to them first.
    /// </summary>
    [TestFixture]
    public class UT_BotLedLoot
    {
        private static int PlayerItemsOf(int drops, int weight, int bots)
        {
            var ledger = new LootLedger();
            for (int i = 0; i < drops; i++)
                ledger.Record(NextItemGoesToPlayer(ledger, weight, bots, upgradeForPlayer: false));
            return ledger.PlayerItems;
        }

        [Test]
        public void WithSevenBotsThePlayerGetsTwoOfEveryNineItems()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PlayerItemsOf(9, 2, 7), Is.EqualTo(2));
                Assert.That(PlayerItemsOf(90, 2, 7), Is.EqualTo(20));
                Assert.That(PlayerItemsOf(8, 1, 7), Is.EqualTo(1), "weight 1: one member like the rest");
            });
        }

        [Test]
        public void TheFirstItemIsThePlayers()
        {
            var ledger = new LootLedger();
            Assert.That(NextItemGoesToPlayer(ledger, 2, 7, false), Is.True);
            ledger.Record(true);
            Assert.That(NextItemGoesToPlayer(ledger, 2, 7, false), Is.False);
        }

        [Test]
        public void AnUpgradeComesToThePlayerFirstAndCountsAsTheirs()
        {
            var ledger = new LootLedger();
            ledger.Record(true);
            Assert.That(NextItemGoesToPlayer(ledger, 2, 7, upgradeForPlayer: true), Is.True);
            ledger.Record(true);
            // Two items already: the rotation waits until the bots have had theirs (2 of 9).
            for (int i = 0; i < 7; i++)
            {
                Assert.That(NextItemGoesToPlayer(ledger, 2, 7, false), Is.False, $"item {i + 3}");
                ledger.Record(false);
            }
            Assert.That(NextItemGoesToPlayer(ledger, 2, 7, false), Is.True, "item 10 starts the next rotation");
        }

        [Test]
        public void WithNoBotInRangeEveryItemIsThePlayers() =>
            Assert.That(PlayerItemsOf(5, 2, 0), Is.EqualTo(5));

        [Test]
        public void TheWeightIsKeptBetweenOneAndEight()
        {
            Assert.Multiple(() =>
            {
                Assert.That(ClampWeight(0), Is.EqualTo(1));
                Assert.That(ClampWeight(2), Is.EqualTo(2));
                Assert.That(ClampWeight(99), Is.EqualTo(MaximumPlayerLootWeight));
            });
        }

        [Test]
        public void CoinIsSplitEvenlyWithThePlayerCountingTwice()
        {
            (long[] players, long[] bots) = CoinShares(900, 2, 1, 7);
            Assert.Multiple(() =>
            {
                Assert.That(players, Is.EqualTo(new long[] { 200 }));
                Assert.That(bots, Is.All.EqualTo(100));
                Assert.That(bots.Length, Is.EqualTo(7));
            });
        }

        [TestCase(1, 2, 1, 7)]
        [TestCase(1001, 2, 1, 7)]
        [TestCase(7, 2, 1, 3)]
        [TestCase(123457, 3, 1, 5)]
        [TestCase(50, 2, 2, 3)]
        public void TheSharesAddUpToTheDropExactly(long copper, int weight, int players, int bots)
        {
            (long[] toPlayers, long[] toBots) = CoinShares(copper, weight, players, bots);
            Assert.That(toPlayers.Sum() + toBots.Sum(), Is.EqualTo(copper));
            Assert.That(toPlayers.Concat(toBots), Is.All.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void WithOnlyBotsOrOnlyThePlayerOneSideTakesAll()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CoinShares(300, 2, 0, 3).Bots.Sum(), Is.EqualTo(300));
                Assert.That(CoinShares(300, 2, 1, 0).Players.Single(), Is.EqualTo(300));
                Assert.That(CoinShares(0, 2, 1, 3).Players.Single(), Is.Zero);
            });
        }

        [Test]
        public void AnUpgradeIsUsableHighEnoughAndBeatsTheWornItem()
        {
            Assert.Multiple(() =>
            {
                Assert.That(IsUpgradeForPlayer(true, 10, 20, true, true, true, 300, 200, 8), Is.True);
                Assert.That(IsUpgradeForPlayer(false, 10, 20, true, true, true, 300, 200, 8), Is.False, "the class can't use it");
                Assert.That(IsUpgradeForPlayer(true, 25, 20, true, true, true, 300, 200, 8), Is.False, "too high a level");
                Assert.That(IsUpgradeForPlayer(true, 10, 20, true, true, true, 205, 200, 8), Is.False, "not enough better");
                Assert.That(IsUpgradeForPlayer(true, 10, 20, true, false, true, 50, 0, 8), Is.True, "an empty armour slot");
                Assert.That(IsUpgradeForPlayer(true, 10, 20, true, false, false, 50, 0, 8), Is.False, "an empty weapon hand");
                Assert.That(IsUpgradeForPlayer(true, 10, 20, false, false, true, 50, 0, 8), Is.False, "not something worn");
            });
        }
    }
}
