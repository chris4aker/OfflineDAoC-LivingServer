using System;
using DOL.GS.CraftingEconomy;

namespace DOL.GS;

/// <summary>
/// The Realm Exchange tax (Crafting 2.0 WP5, written new; design §4 L, decision 7). Built, and <b>0 by default</b>: the
/// owner (2026-11-15) keeps it at 0 until the economy ledger shows a need; 5% is the documented option. The tax is a
/// share of a sale's price kept from the seller's proceeds (WP0's EconomySeams.SellerProceeds, in PrepareSellerPayment,
/// for gamebot and player sellers alike); the buyer pays the listed price as before. Only a trade pays it: a player who
/// never sells on the Exchange never pays anything. The kept coin leaves the world (a sink); the sale record carries it
/// (Sale.TaxCopper), and the economy ledger (WP2) counts it from EconomySeams.SaleCommitted.
/// </summary>
public static class ExchangeTax
{
    /// <summary>The documented option, for the launcher and the docs (not the default).</summary>
    public const int DocumentedPercent = 5;

    /// <summary>exchange_tax_percent, kept within 0-50.</summary>
    public static int Percent => CraftingEconomyProperties.SanitizeTaxPercent(CraftingEconomyProperties.EXCHANGE_TAX_PERCENT);

    /// <summary>The tax on a price: round(price x percent / 100), halves up; 0 for no price or no tax. Pure.</summary>
    public static long TaxOf(long priceCopper, int percent)
    {
        percent = CraftingEconomyProperties.SanitizeTaxPercent(percent);
        if (priceCopper <= 0 || percent <= 0)
            return 0;
        // price <= long.MaxValue / 50 in any real sale; checked so an absurd one cannot wrap.
        long tax = checked((priceCopper * percent + 50) / 100);
        return Math.Clamp(tax, 0, priceCopper);
    }

    /// <summary>What the seller receives: the price less the tax. Pure.</summary>
    public static long ProceedsOf(long priceCopper, int percent) => priceCopper - TaxOf(priceCopper, percent);

    /// <summary>The seam's function (EconomySeams.SellerProceeds): (price, realm, seller, buyer) =&gt; proceeds.</summary>
    public static long SellerProceeds(long priceCopper, eRealm realm, string sellerId, string buyerId)
    {
        try { return ProceedsOf(priceCopper, Percent); }
        catch (OverflowException) { return priceCopper; }
    }
}
