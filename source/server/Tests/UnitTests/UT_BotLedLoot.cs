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
                ledger.Record(NextItemGoesToPlayer(ledger, weight, bots));
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
            Assert.That(NextItemGoesToPlayer(ledger, 2, 7), Is.True);
            ledger.Record(true);
            Assert.That(NextItemGoesToPlayer(ledger, 2, 7), Is.False);
        }

        // Owner 2026-11-04: the player gets the primary spoils, and gamebots, who start without armour and earn it, still
        // gear up from drops. (a) the player's upgrade, (b) else a gamebot's upgrade, (c) else the rotation.
        [Test]
        public void AnUpgradeForThePlayerGoesToThePlayerBeforeAnything()
        {
            var ledger = new LootLedger();
            ledger.Record(true);   // the player just had their rotation turn
            Assert.Multiple(() =>
            {
                Assert.That(Route(true, true, ledger, 2, 7, playerInRange: true), Is.EqualTo(LootRoute.UpgradePlayer), "before a bot's upgrade");
                Assert.That(Route(true, false, ledger, 2, 7, playerInRange: true), Is.EqualTo(LootRoute.UpgradePlayer), "whatever the rotation says");
                Assert.That(ReasonOf(LootRoute.UpgradePlayer), Is.EqualTo("upgrade-player"));
            });
        }

        [Test]
        public void OtherwiseAnUpgradeForAGamebotGoesToThatBotEvenOnThePlayersTurn()
        {
            var ledger = new LootLedger();   // the first item of the rotation would be the player's
            Assert.Multiple(() =>
            {
                Assert.That(NextItemGoesToPlayer(ledger, 2, 7), Is.True);
                Assert.That(Route(false, true, ledger, 2, 7, playerInRange: true), Is.EqualTo(LootRoute.UpgradeBot));
                Assert.That(Route(true, true, ledger, 2, 7, playerInRange: false), Is.EqualTo(LootRoute.UpgradeBot), "the player isn't in range");
                Assert.That(Route(false, true, ledger, 2, 0, playerInRange: true), Is.EqualTo(LootRoute.RotationPlayer), "no gamebot in range");
                Assert.That(ReasonOf(LootRoute.UpgradeBot), Is.EqualTo("upgrade-bot"));
            });
        }

        [Test]
        public void EverythingElseGoesThroughTheRotation()
        {
            var ledger = new LootLedger();
            var routes = new System.Collections.Generic.List<LootRoute>();
            for (int i = 0; i < 9; i++)
            {
                LootRoute route = Route(false, false, ledger, 2, 7, playerInRange: true);
                routes.Add(route);
                ledger.Record(route == LootRoute.RotationPlayer);
            }
            Assert.Multiple(() =>
            {
                Assert.That(routes.Count(route => route == LootRoute.RotationPlayer), Is.EqualTo(2), "2 of every 9");
                Assert.That(routes[0], Is.EqualTo(LootRoute.RotationPlayer));
                Assert.That(Route(false, false, new LootLedger(), 2, 7, playerInRange: false), Is.EqualTo(LootRoute.RotationBot));
                Assert.That(ReasonOf(LootRoute.RotationPlayer), Is.EqualTo("rotation"));
                Assert.That(ReasonOf(LootRoute.RotationBot), Is.EqualTo("rotation"));
            });
        }

        [Test]
        public void UpgradesAreNotCountedInTheRotation()
        {
            // Upgrades are given outside the rotation (the ledger records rotation items only), so a run of upgrades for
            // the gamebots does not take the player's rotation turns, and the player's upgrades don't spend them.
            var ledger = new LootLedger();
            Assert.That(Route(false, true, ledger, 2, 7, true), Is.EqualTo(LootRoute.UpgradeBot));
            Assert.That(Route(true, false, ledger, 2, 7, true), Is.EqualTo(LootRoute.UpgradePlayer));
            Assert.That((ledger.Items, ledger.PlayerItems), Is.EqualTo((0, 0)));
            Assert.That(Route(false, false, ledger, 2, 7, true), Is.EqualTo(LootRoute.RotationPlayer), "the first rotation item is still the player's");
        }

        [Test]
        public void TheBotItImprovesMostGetsIt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PickBotForUpgrade([null, 40, 120, 60]), Is.EqualTo(2));
                Assert.That(PickBotForUpgrade([90, null, 90]), Is.EqualTo(0), "the first of equals");
                Assert.That(PickBotForUpgrade([null, null]), Is.EqualTo(-1), "an upgrade for none");
                Assert.That(PickBotForUpgrade([]), Is.EqualTo(-1));
                Assert.That(PickBotForUpgrade([-5, null]), Is.EqualTo(0), "an upgrade the appraisal accepts with a low value gain (a Savage weapon) still counts");
            });
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
