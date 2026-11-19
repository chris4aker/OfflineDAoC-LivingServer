using System.Linq;
using DOL.Database;
using DOL.GS.Commands;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>/lscraft (Crafting 2.0 WP10): the answers from what is registered now, and the hooks later packages use.</summary>
    [TestFixture, NonParallelizable]
    public class UT_CraftEconomyTestCommand
    {
        [TearDown]
        public void Reset() => CraftEconomyTestHooks.ResetForTests();

        [Test]
        public void EachPartSaysWhatIsNotBuiltYet()
        {
            // Chat audit TEXT-042: every package is built now; an empty part is one not registered on this server.
            const string NotRegistered = "not registered on this server";
            Assert.That(CraftEconomyTestCommandHandler.CraftLines(), Has.Some.Contains("Recipe catalog: " + NotRegistered));
            Assert.That(CraftEconomyTestCommandHandler.CraftLines(), Has.Some.Contains("Crafter roster: " + NotRegistered));
            Assert.That(CraftEconomyTestCommandHandler.WtbLines()[0], Does.Contain(NotRegistered));
            Assert.That(CraftEconomyTestCommandHandler.CommissionLines(), Has.Some.Contains(NotRegistered));
            Assert.That(CraftEconomyTestCommandHandler.LedgerLines([], null)[0], Does.Contain(NotRegistered));
            Assert.That(CraftEconomyTestCommandHandler.CrafterLines("Bob", eRealm.Albion, 30, BotPersonalityType.Crafter, "", 12_345, null, false),
                Has.Some.Contains(NotRegistered));
            string[] every = CraftEconomyTestCommandHandler.CraftLines().Concat(CraftEconomyTestCommandHandler.WtbLines())
                .Concat(CraftEconomyTestCommandHandler.CommissionLines()).Concat(CraftEconomyTestCommandHandler.LedgerLines([], null))
                .Concat(CraftEconomyTestCommandHandler.CrafterLines("Bob", eRealm.Albion, 30, BotPersonalityType.Crafter, "", 12_345, null, false))
                .ToArray();
            Assert.That(every.Where(line => line.Contains("not built yet") || System.Text.RegularExpressions.Regex.IsMatch(line, @"\bWP\d")),
                Is.Empty, "no work-package notes in what a GM reads");
        }

        [Test]
        public void CoinIsWrittenWithoutTheLanguageTables()
        {
            Assert.That(CraftEconomyTestCommandHandler.Coin(0), Is.EqualTo("0c"));
            Assert.That(CraftEconomyTestCommandHandler.Coin(12_345_678), Is.EqualTo("1,234g 56s 78c"));
            Assert.That(CraftEconomyTestCommandHandler.Coin(10_000), Is.EqualTo("1g"));
            Assert.That(CraftEconomyTestCommandHandler.Coin(-205), Is.EqualTo("-2s 5c"));
        }

        [Test]
        public void TheDefaultsAreTheHoldersOwn()
        {
            Assert.That(CraftEconomyTestCommandHandler.IsBuilt(BotCrafters.Roster, typeof(BotCrafters)), Is.False);
            Assert.That(CraftEconomyTestCommandHandler.IsBuilt(CraftOrders.Board, typeof(CraftOrders)), Is.False);
            Assert.That(CraftEconomyTestCommandHandler.IsBuilt(PriceIndexes.Current, typeof(PriceIndexes)), Is.False);
            Assert.That(CraftEconomyTestCommandHandler.IsBuilt(DOL.GS.Crafting.RecipeCatalogs.Current, typeof(DOL.GS.Crafting.RecipeCatalogs)), Is.False);
            Assert.That(CraftEconomyTestCommandHandler.IsBuilt(new object(), typeof(BotCrafters)), Is.True);
        }

        [Test]
        public void TheLedgerSumsOnlineGamebotsByRealm()
        {
            string[] lines = CraftEconomyTestCommandHandler.LedgerLines(
                [(eRealm.Albion, 10_000, false), (eRealm.Albion, 30_000, true), (eRealm.Hibernia, 5, false)], null);
            Assert.That(lines.Count(l => l.StartsWith("  ")), Is.EqualTo(2));
            Assert.That(lines.Single(l => l.Contains("Albion")), Does.Contain("2 bots").And.Contain(CraftEconomyTestCommandHandler.Coin(40_000)).And.Contain(CraftEconomyTestCommandHandler.Coin(30_000)));
        }

        [Test]
        public void TheIndexPricesAnItemWithTodaysNumbers()
        {
            var template = new DbItemTemplate { Id_nb = "t", Name = "test helm", Level = 30, Quality = 95, Object_Type = 34, Item_Type = 21,
                DPS_AF = 60, MaxCondition = 50_000, Condition = 50_000, Price = 10_000, IsDropable = true, IsTradable = true, PackSize = 1 };
            DbInventoryItem item = GameInventoryItem.Create(template);
            string[] lines = CraftEconomyTestCommandHandler.IndexLines(item, eRealm.Midgard, PriceIndexes.Current);
            Assert.That(lines[0], Does.Contain("test helm").And.Contain("the merchant appraisal and the listing valuation").And.Contain(MarketBand.Of(eRealm.Midgard, item).Key));
            Assert.That(lines[1], Does.Contain("floor " + CraftEconomyTestCommandHandler.Coin(AutonomousBotEconomy.CalculateStandardVendorSaleCopper(item))));
            Assert.That(CraftEconomyTestCommandHandler.IndexLines(null, eRealm.Midgard, null)[0], Does.StartWith("Put an item"));
        }

        [Test]
        public void APackageRegistersItsOwnPart()
        {
            Assert.That(CraftEconomyTestHooks.TryGet("wtb", out _), Is.False);
            CraftEconomyTestHooks.Register("wtb", (_, words) => [$"wtb by WP6 ({words.Length})"]);
            Assert.That(CraftEconomyTestHooks.TryGet("WTB", out CraftEconomyTestHooks.Handler handler), Is.True);
            Assert.That(handler(null, ["wtb", "x"]).Single(), Is.EqualTo("wtb by WP6 (2)"));
        }
    }
}
