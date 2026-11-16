using System;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP5: the Realm Exchange tax (written new). Built, 0 by default (owner 2026-11-15); 5% is the documented
    /// option. Only a sale pays it, from the seller's proceeds; a player who never trades pays nothing. Plus the formatter
    /// fix (realm=Albion, not realm=_FirstPlayerRealm). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_ExchangeTax
    {
        private int _tax;

        [SetUp]
        public void SetUp() => _tax = CraftingEconomyProperties.EXCHANGE_TAX_PERCENT;

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.EXCHANGE_TAX_PERCENT = _tax;
            MarketSeams.ResetForTests();
            EconomySeams.ResetForTests();
            BotServiceSeams.ResetForTests();
            EconomyLifecycle.ResetForTests();
        }

        [Test]
        public void TheDefaultIsNoTax()
        {
            var attribute = (ServerPropertyAttribute)Attribute.GetCustomAttribute(
                typeof(CraftingEconomyProperties).GetField(nameof(CraftingEconomyProperties.EXCHANGE_TAX_PERCENT)), typeof(ServerPropertyAttribute));
            Assert.That(attribute.DefaultValue, Is.EqualTo(0));
            Assert.That(ExchangeTax.DocumentedPercent, Is.EqualTo(5));
            CraftingEconomyProperties.EXCHANGE_TAX_PERCENT = 0;
            MarketSeams.RegisterForTests();
            Assert.That(EconomySeams.ProceedsOf(12_345, eRealm.Albion, "offlinebot:1", "offlinebot:2"), Is.EqualTo(12_345),
                "registered at 0: the seller gets the whole price, as before");
        }

        [TestCase(0, 1_000, 0)]
        [TestCase(5, 1_000, 50)]
        [TestCase(5, 10, 1)]   // 0.5 rounds up
        [TestCase(5, 9, 0)]    // 0.45 rounds down
        [TestCase(5, 1, 0)]
        [TestCase(5, 0, 0)]
        [TestCase(5, -10, 0)]
        [TestCase(100, 1_000, 500)] // the setting is kept within 0-50
        [TestCase(-5, 1_000, 0)]
        public void TheTaxIsAShareOfThePriceRounded(int percent, long price, long tax)
        {
            Assert.That(ExchangeTax.TaxOf(price, percent), Is.EqualTo(tax));
            if (price > 0)
                Assert.That(ExchangeTax.ProceedsOf(price, percent) + ExchangeTax.TaxOf(price, percent), Is.EqualTo(price));
        }

        [Test]
        public void AtFivePercentTheSellerReceivesTheRest()
        {
            CraftingEconomyProperties.EXCHANGE_TAX_PERCENT = 5;
            MarketSeams.RegisterForTests();
            Assert.That(EconomySeams.ProceedsOf(10_000, eRealm.Midgard, "player-char-id", "offlinebot:7"), Is.EqualTo(9_500),
                "a player seller pays it too");
            Assert.That(EconomySeams.ProceedsOf(10_000, eRealm.Midgard, "offlinebot:7", "player-char-id"), Is.EqualTo(9_500));
            Assert.That(ExchangeTax.SellerProceeds(long.MaxValue, eRealm.Albion, "a", "b"), Is.EqualTo(long.MaxValue),
                "an absurd price cannot wrap: no tax rather than a wrong one");
        }

        [Test]
        public void APlayerWhoNeverTradesPaysNothing()
        {
            // The tax lives only in the seller's proceeds of a sale (PrepareSellerPayment); nothing else calls the seam.
            CraftingEconomyProperties.EXCHANGE_TAX_PERCENT = 5;
            MarketSeams.RegisterForTests();
            int calls = 0;
            Func<long, eRealm, string, string, long> taxed = EconomySeams.SellerProceeds;
            EconomySeams.SellerProceeds = (price, realm, seller, buyer) => { calls++; return taxed(price, realm, seller, buyer); };
            Assert.That(calls, Is.Zero, "no sale, no call: a player's coin is never touched by the tax");
            string[] callers = System.IO.Directory.GetFiles(SourceRoot(), "*.cs", System.IO.SearchOption.AllDirectories);
            int sites = 0;
            foreach (string path in callers)
                foreach (string line in System.IO.File.ReadLines(path))
                    if (line.Contains("EconomySeams.ProceedsOf(") && !line.TrimStart().StartsWith("//") && !line.TrimStart().StartsWith("///"))
                        sites++;
            Assert.That(sites, Is.EqualTo(1), "one caller: the Exchange's seller payment (RealmExchangeTransactions.PrepareSellerPayment)");
        }

        [Test]
        public void TheLogFormatterNamesTheRealms()
        {
            Assert.That(EconomyLog.Line("T", ("realm", eRealm.Albion)), Is.EqualTo("T realm=Albion"), "not realm=_FirstPlayerRealm");
            Assert.That(EconomyLog.Format(eRealm.Midgard), Is.EqualTo("Midgard"));
            Assert.That(EconomyLog.Format(eRealm.Hibernia), Is.EqualTo("Hibernia"), "not _LastPlayerRealm or _Last");
            Assert.That(EconomyLog.Format(eRealm.None), Is.EqualTo("None"), "not _First");
            Assert.That(EconomyLog.Format((eRealm)9), Is.EqualTo("9"));
            Assert.That(EconomyLog.Format(eCraftingSkill.ArmorCrafting), Is.EqualTo("ArmorCrafting"), "other enums as before");
        }

        private static string SourceRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir, "source", "server", "GameServer")))
                dir = System.IO.Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return System.IO.Path.Combine(dir, "source", "server", "GameServer");
        }
    }
}
