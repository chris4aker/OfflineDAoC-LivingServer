using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Issue #625 row 8: ECONOMY_PULSE, one row per realm at each UTC hour, written by <see cref="EconomyLedger"/> from
    /// the same snapshot and closed flow window as ECON_SUPPLY / ECON_FLOWS (3 rows an hour). hour= is the end of the
    /// window. Instrumentation only.
    /// <list type="bullet">
    /// <item>The Exchange: active listings (and the gamebots' share) and their median asking price by slot band; the
    /// hour's sales, their copper and tax, and the count and median price by slot band.</item>
    /// <item>Gamebot coin in and out by source: loot (coin and loot shares), vendor (sales, gear), exchange (proceeds of
    /// sales, purchases; from the committed sales, so a bot seller's credit is not counted twice), repair (repairs, kits),
    /// upkeep (healer, travel, tickets, medallions, consumables, potions, ammo, poison, siege, dye, luxury), craft
    /// (materials, sales, orders, enchanting, commissions) and other. Training costs a gamebot no coin, so it has no
    /// field.</item>
    /// </list>
    /// </summary>
    public static class EconomyPulse
    {
        public const string Marker = "ECONOMY_PULSE";
        public static readonly string[] SlotBands = ["other", "weapon", "armor", "jewelry"];
        public const int SlotBandCount = 4;
        public const int MaxSalePrices = 4_096;

        /// <summary>The slot band of an item type (DbInventoryItem.Item_Type, an eInventorySlot value).</summary>
        public static int SlotBand(int itemType) => (eInventorySlot)itemType switch
        {
            eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or eInventorySlot.TwoHandWeapon
                or eInventorySlot.DistanceWeapon => 1,
            eInventorySlot.HeadArmor or eInventorySlot.HandsArmor or eInventorySlot.FeetArmor or eInventorySlot.TorsoArmor
                or eInventorySlot.LegsArmor or eInventorySlot.ArmsArmor => 2,
            eInventorySlot.Jewelry or eInventorySlot.Cloak or eInventorySlot.Neck or eInventorySlot.Waist
                or eInventorySlot.LeftBracer or eInventorySlot.RightBracer or eInventorySlot.LeftRing
                or eInventorySlot.RightRing or eInventorySlot.Mythical => 3,
            _ => 0,
        };

        /// <summary>The coin sources, each with its flow kinds (exchange is read from the committed sales instead).</summary>
        public static readonly (string Name, EconomyFlowKind[] Kinds)[] Sources =
        [
            ("loot", [EconomyFlowKind.LootCoin, EconomyFlowKind.LootShare]),
            ("vendor", [EconomyFlowKind.VendorSale, EconomyFlowKind.VendorGear]),
            ("repair", [EconomyFlowKind.Repair, EconomyFlowKind.RepairKit]),
            ("upkeep", [EconomyFlowKind.Healer, EconomyFlowKind.Travel, EconomyFlowKind.StableTicket, EconomyFlowKind.Medallion,
                EconomyFlowKind.Consumable, EconomyFlowKind.Potion, EconomyFlowKind.Ammo, EconomyFlowKind.Poison,
                EconomyFlowKind.Siege, EconomyFlowKind.Dye, EconomyFlowKind.Luxury]),
            ("craft", [EconomyFlowKind.CraftMaterials, EconomyFlowKind.CraftSale, EconomyFlowKind.CraftOrder,
                EconomyFlowKind.Enchant, EconomyFlowKind.Commission]),
        ];

        /// <summary>Kinds never counted under "other": the named sources, and ExchangeSale (the sale seam counts it).</summary>
        private static bool Named(EconomyFlowKind kind)
        {
            if (kind == EconomyFlowKind.ExchangeSale)
                return true;
            foreach ((_, EconomyFlowKind[] kinds) in Sources)
                if (Array.IndexOf(kinds, kind) >= 0)
                    return true;
            return false;
        }

        public static IReadOnlyList<string> Lines(DateTime endUtc, SupplySnapshot now, EconomyFlowLedger.Window window)
        {
            var lines = new List<string>(3);
            if (window == null)
                return lines;
            var c = CultureInfo.InvariantCulture;
            string hour = endUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH':00Z'", c);
            for (int realm = 1; realm <= 3; realm++)
            {
                RealmSupply supply = now?.Realms[realm];
                var text = new StringBuilder();
                text.Append(c, $"{Marker} hour={hour} realm={RealmName(realm)}");
                text.Append(c, $" listings={supply?.Listings ?? 0} bot_listings={supply?.BotListings ?? 0}");
                for (int band = 0; band < SlotBandCount; band++)
                    text.Append(c, $" list_{SlotBands[band]}={supply?.ListingBandCount[band] ?? 0} list_{SlotBands[band]}_med={supply?.ListingBandMedian[band] ?? 0}");
                text.Append(c, $" sold={window.Sales(realm)} sold_copper={window.SalesCopper(realm)} tax={window.SalesTax(realm)}");
                for (int band = 0; band < SlotBandCount; band++)
                    text.Append(c, $" sold_{SlotBands[band]}={window.SaleBandCount(realm, band)} sold_{SlotBands[band]}_med={window.SaleBandMedian(realm, band)}");
                long totalIn = 0, totalOut = 0;
                foreach ((string name, EconomyFlowKind[] kinds) in Sources)
                {
                    long @in = 0, @out = 0;
                    foreach (EconomyFlowKind kind in kinds)
                    {
                        @in += window.In(realm, kind);
                        @out += window.Out(realm, kind);
                    }
                    totalIn += @in;
                    totalOut += @out;
                    text.Append(c, $" {name}_in={@in} {name}_out={@out}");
                    if (name == "vendor")
                    {
                        // The Exchange sits after vendor in the row: gamebots' sale proceeds in, purchases out.
                        text.Append(c, $" exchange_in={window.BotReceived(realm)} exchange_out={window.BotPaid(realm)}");
                        totalIn += window.BotReceived(realm);
                        totalOut += window.BotPaid(realm);
                    }
                }
                long otherIn = 0, otherOut = 0;
                for (int k = 0; k < EconomyFlowClassifier.KindCount; k++)
                {
                    var kind = (EconomyFlowKind)k;
                    if (Named(kind))
                        continue;
                    otherIn += window.In(realm, kind);
                    otherOut += window.Out(realm, kind);
                }
                totalIn += otherIn;
                totalOut += otherOut;
                text.Append(c, $" other_in={otherIn} other_out={otherOut} coin_in={totalIn} coin_out={totalOut}");
                lines.Add(text.ToString());
            }
            return lines;
        }

        private static string RealmName(int realm) => realm switch { 1 => "Albion", 2 => "Midgard", 3 => "Hibernia", _ => "None" };
    }
}
