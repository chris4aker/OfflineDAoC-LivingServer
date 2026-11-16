using System;
using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP5: the price index (written new). The property the design asks for: no buy-and-sell loop through
    /// the index makes coin (a bid is below every ask by at least the vendor spread, and never below what a merchant
    /// pays); flooding a band lowers it, demand raises it, within bounds; the material ceiling holds. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_PriceIndex
    {
        [TearDown]
        public void TearDown()
        {
            MarketPriceIndex.Instance.ResetForTests();
            PriceIndexes.Current = null;
        }

        [Test]
        public void NoBuySellLoopMakesCoin()
        {
            var random = new Random(20261115);
            for (int i = 0; i < 20_000; i++)
            {
                long floor = random.Next(0, 50_000);
                long merchant = floor + random.Next(0, 50_000);
                long basis = random.Next(1, 2_000_000);
                long materials = random.Next(0, 3) == 0 ? 0 : random.Next(1, 500_000);
                var stats = new PriceIndexMath.BandStats(random.Next(0, 500), random.Next(0, 500), random.Next(0, 20), random.Next(0, 3_000_000));
                double seller = -0.6 + random.NextDouble() * 1.4; // beyond the range on purpose: clamped
                double buyer = -0.6 + random.NextDouble() * 1.4;
                PriceIndexMath.Quote quote = PriceIndexMath.QuoteOf(basis, floor, merchant, materials, stats, seller, buyer);
                PriceIndexMath.Quote cheapest = PriceIndexMath.QuoteOf(basis, floor, merchant, materials, stats, PriceIndexMath.MinDisposition, buyer);
                PriceIndexMath.Quote dearestBid = PriceIndexMath.QuoteOf(basis, floor, merchant, materials, stats, seller, PriceIndexMath.MaxDisposition);

                Assert.That(quote.Bid, Is.GreaterThanOrEqualTo(quote.Floor), "a bot never bids below what a merchant pays");
                Assert.That(dearestBid.Bid + quote.Spread, Is.LessThanOrEqualTo(cheapest.Ask),
                    "the highest bid is below the lowest ask by the spread: buying at an ask and selling at a bid loses");
                Assert.That(quote.Ask, Is.GreaterThanOrEqualTo(quote.Floor + quote.Spread),
                    "buying at an ask and selling to a merchant loses");
                Assert.That(quote.Fair, Is.GreaterThanOrEqualTo(floor));
                if (materials > 0)
                    Assert.That(quote.Fair, Is.LessThanOrEqualTo(Math.Max(floor, materials * PriceIndexMath.CeilingMaterialMultiple)),
                        "the fair price never passes the material ceiling");
            }
        }

        [Test]
        public void FloodingABandLowersItAndDemandRaisesItWithinBounds()
        {
            Assert.That(PriceIndexMath.Pressure(0, 0), Is.EqualTo(1.0));
            Assert.That(PriceIndexMath.Pressure(50, 0), Is.LessThan(1.0));
            Assert.That(PriceIndexMath.Pressure(10_000, 0), Is.EqualTo(PriceIndexMath.MinFactor));
            Assert.That(PriceIndexMath.Pressure(0, 50), Is.GreaterThan(1.0));
            Assert.That(PriceIndexMath.Pressure(0, 10_000), Is.EqualTo(PriceIndexMath.MaxFactor));
            Assert.That(PriceIndexMath.Pressure(1, 0), Is.GreaterThan(0.95), "one listing barely moves a band");
            long balanced = PriceIndexMath.Fair(1_000, 10, 0, new PriceIndexMath.BandStats(5, 5, 0, 0));
            long flooded = PriceIndexMath.Fair(1_000, 10, 0, new PriceIndexMath.BandStats(200, 5, 0, 0));
            Assert.That(flooded, Is.LessThan(balanced));
            Assert.That(PriceIndexMath.Fair(1_000, 900, 0, new PriceIndexMath.BandStats(200, 0, 0, 0)), Is.EqualTo(900), "never below the floor");
        }

        [Test]
        public void SalesMoveTheBandOnlyOnceItHasSome()
        {
            var two = new PriceIndexMath.BandStats(5, 5, 2, 4_000);
            var three = two with { Sales = 3 };
            Assert.That(PriceIndexMath.Fair(1_000, 0, 0, two), Is.EqualTo(1_000));
            Assert.That(PriceIndexMath.Fair(1_000, 0, 0, three), Is.EqualTo(1_500), "halfway to the sales, at most twice the basis");
            Assert.That(PriceIndexMath.NextEma(0, 800), Is.EqualTo(800));
            Assert.That(PriceIndexMath.NextEma(800, 1_200), Is.EqualTo(900));
        }

        [Test]
        public void TheIndexHearsListingsSalesAndDemandPerBand()
        {
            MarketPriceIndex index = MarketPriceIndex.Instance;
            index.ResetForTests();
            index.FloorOf = _ => 50;
            index.BaseOf = _ => 1_000;
            index.MerchantPriceOf = _ => 100;
            DbInventoryItem sword = Sword("Bronze sword", 22);
            sword.OwnerLot = RealmExchangeBroker.AlbionOwnerLot;
            sword.SellPrice = 1_000;
            MarketBand band = MarketPriceIndex.BandOf(sword);
            Assert.That(band.Realm, Is.EqualTo(eRealm.Albion), "a listing's realm is its lot's");

            long before = index.Fair(sword);
            for (int i = 0; i < 40; i++)
                index.NoteListed(sword);
            Assert.That(index.StatsOf(band).Stock, Is.EqualTo(40));
            Assert.That(index.StatsOf(band with { Realm = eRealm.None }).Stock, Is.EqualTo(40), "the realms together too");
            Assert.That(index.Fair(sword), Is.LessThan(before), "a flooded band falls");

            index.NoteSale(new RealmExchangeSaleLedger.Sale(1, "Bronze sword", 1, 2_000, "offlinebot:1", "A", "offlinebot:2", "B", DateTime.UtcNow));
            Assert.That(index.StatsOf(band).Sales, Is.EqualTo(1));
            Assert.That(index.StatsOf(band).Stock, Is.EqualTo(39), "a sale takes one off the shelf");
            index.NoteSale(new RealmExchangeSaleLedger.Sale(2, "Bronze sword", 1, 2_000, "offlinebot:1", "A", "offlinebot:2", "B", DateTime.UtcNow));
            Assert.That(index.StatsOf(band).Sales, Is.EqualTo(1), "a name seen only in Albion is not Midgard's");

            index.ObserveDemand(band);
            Assert.That(index.StatsOf(band).Demand, Is.EqualTo(1));
        }

        [Test]
        public void TheHourlyRecountReadsTheWholeExchangeAndFadesDemand()
        {
            MarketPriceIndex index = MarketPriceIndex.Instance;
            index.ResetForTests();
            DbInventoryItem a = Sword("A", 22), b = Sword("B", 23), old = Sword("Old", 22);
            foreach (DbInventoryItem listing in new[] { a, b, old })
            {
                listing.OwnerLot = RealmExchangeBroker.MidgardOwnerLot;
                listing.SellPrice = 500;
                listing.OwnerID = "offlinebot:5";
                listing.RealmExchangeListedUtc = DateTime.UtcNow.ToString("O");
            }
            old.RealmExchangeListedUtc = DateTime.UtcNow.AddHours(-30).ToString("O");
            index.Market = () => [a, b, old];
            MarketBand band = MarketPriceIndex.BandOf(a);
            for (int i = 0; i < 8; i++)
                index.ObserveDemand(band);

            var lines = index.Recount(DateTime.UtcNow);

            Assert.That(index.StatsOf(band).Stock, Is.EqualTo(2), "two live listings in the band; the expired one is not stock");
            Assert.That(index.StatsOf(band).Demand, Is.EqualTo(4), "demand halves each hour");
            Assert.That(lines.Single(), Does.StartWith("ECON_INDEX realm=Midgard bands=1 stock=2 demand=4 sales=0"));
        }

        [Test]
        public void TheIndexIsWP0sOnceRegistered()
        {
            MarketSeams.RegisterForTests();
            try
            {
                Assert.That(PriceIndexes.Current, Is.SameAs(MarketPriceIndex.Instance));
                Assert.That(MarketPriceIndex.TryParseKey(new MarketBand(eRealm.Hibernia, 3, 12, 4, 1).Key, out MarketBand back), Is.True);
                Assert.That(back, Is.EqualTo(new MarketBand(eRealm.Hibernia, 3, 12, 4, 1)));
            }
            finally
            {
                MarketSeams.ResetForTests();
                EconomySeams.ResetForTests();
                BotServiceSeams.ResetForTests();
                EconomyLifecycle.ResetForTests();
            }
        }

        private static DbInventoryItem Sword(string name, int level) => new()
        {
            Template = new DbItemTemplate
            {
                Id_nb = name, Name = name, Object_Type = (int)eObjectType.SlashingWeapon, Item_Type = (int)eInventorySlot.RightHandWeapon,
                Level = level, Quality = 95, Price = 100, IsTradable = true, IsDropable = true,
            },
        };
    }
}
