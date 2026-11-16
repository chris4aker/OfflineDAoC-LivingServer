using System;
using System.Globalization;
using System.Text;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The CRAFT_* and ECON_* log tags of Crafting 2.0 and their one line format, matching the AUTONOMOUS_* lines:
    /// <c>TAG key=value key="text" ...</c>, invariant culture, copper as whole numbers, one line per outcome. Anything
    /// frequent is summed hourly; per-craft lines are gated by <see cref="EconomyDiagnostics"/>. WP0 writes no line.
    /// Ported lines keep their fork's tag as an alias where a tool already reads it (DividedRealms'
    /// AUTONOMOUS_CRAFT_CATALOG and AUTONOMOUS_EXCHANGE_SOLD, True165's BOT_MONEY_LEDGER_165).
    /// </summary>
    public static class EconomyLog
    {
        // Crafting (WP1, WP3, WP4, WP8)
        public const string CraftRecipeAudit = "CRAFT_RECIPE_AUDIT";
        public const string CraftRecipeBroken = "CRAFT_RECIPE_BROKEN";
        public const string CraftWorldAudit = "CRAFT_WORLD_AUDIT";
        public const string CraftEraJoin = "CRAFT_ERA_JOIN";
        public const string CraftPlayerTitle = "CRAFT_PLAYER_TITLE";
        public const string CraftEnroll = "CRAFT_ENROLL";
        public const string CraftSessionStart = "CRAFT_SESSION_START";
        public const string CraftMade = "CRAFT_MADE";
        public const string CraftSessionEnd = "CRAFT_SESSION_END";
        public const string CraftTitle = "CRAFT_TITLE";
        public const string CraftFirst = "CRAFT_FIRST";
        public const string CraftExplosion = "CRAFT_EXPLOSION";
        public const string CraftStory = "CRAFT_STORY";

        // Economy (WP2, WP5-WP8, WP10)
        public const string EconSupply = "ECON_SUPPLY";
        public const string EconFlows = "ECON_FLOWS";
        public const string EconOrder = "ECON_ORDER";
        public const string EconCommission = "ECON_COMMISSION";
        public const string EconList = "ECON_LIST";
        public const string EconSale = "ECON_SALE";
        public const string EconReclaim = "ECON_RECLAIM";
        public const string EconIndex = "ECON_INDEX";
        public const string EconGearCheck = "ECON_GEAR_CHECK";
        public const string EconTemplateBuy = "ECON_TEMPLATE_BUY";
        public const string EconUndressGuard = "ECON_UNDRESS_GUARD";
        public const string EconWear = "ECON_WEAR";
        public const string EconRepair = "ECON_REPAIR";
        public const string EconConsumable = "ECON_CONSUMABLE";
        public const string EconLuxury = "ECON_LUXURY";
        public const string EconSiegeSupply = "ECON_SIEGE_SUPPLY";
        public const string EconGovernor = "ECON_GOVERNOR";
        public const string EconPlayerOrder = "ECON_PLAYER_ORDER";
        public const string EconPlayerDelivery = "ECON_PLAYER_DELIVERY";
        public const string EconPlayerSell = "ECON_PLAYER_SELL";
        public const string EconScJob = "ECON_SC_JOB";
        public const string EconHaggle = "ECON_HAGGLE";
        public const string EconGuildSpend = "ECON_GUILD_SPEND";
        public const string EconRehearsal = "ECON_REHEARSAL";

        // Fork tags kept as aliases by the ports
        public const string DividedRealmsCatalog = "AUTONOMOUS_CRAFT_CATALOG";
        public const string DividedRealmsExchangeSold = "AUTONOMOUS_EXCHANGE_SOLD";
        public const string True165MoneyLedger = "BOT_MONEY_LEDGER_165";

        public static readonly string[] AllTags =
        [
            CraftRecipeAudit, CraftRecipeBroken, CraftWorldAudit, CraftEraJoin, CraftPlayerTitle, CraftEnroll,
            CraftSessionStart, CraftMade, CraftSessionEnd, CraftTitle, CraftFirst, CraftExplosion, CraftStory,
            EconSupply, EconFlows, EconOrder, EconCommission, EconList, EconSale, EconReclaim, EconIndex, EconGearCheck,
            EconTemplateBuy, EconUndressGuard, EconWear, EconRepair, EconConsumable, EconLuxury, EconSiegeSupply,
            EconGovernor, EconPlayerOrder, EconPlayerDelivery, EconPlayerSell, EconScJob, EconHaggle, EconGuildSpend,
            EconRehearsal,
        ];

        /// <summary>
        /// One line: the tag, then each pair as key=value. Numbers are written invariantly (copper whole), booleans as
        /// 1/0, and text in double quotes with quotes and line breaks replaced, so a line always parses.
        /// </summary>
        public static string Line(string tag, params (string Key, object Value)[] pairs)
        {
            var line = new StringBuilder(tag ?? string.Empty);
            if (pairs != null)
                foreach ((string key, object value) in pairs)
                {
                    if (string.IsNullOrEmpty(key))
                        continue;
                    line.Append(' ').Append(key).Append('=').Append(Format(value));
                }
            return line.ToString();
        }

        /// <summary>A value as the line format writes it.</summary>
        public static string Format(object value) => value switch
        {
            null => "\"\"",
            string text => Quote(text),
            bool flag => flag ? "1" : "0",
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            float number => number.ToString("0.###", CultureInfo.InvariantCulture),
            decimal number => number.ToString("0.###", CultureInfo.InvariantCulture),
            DateTime time => time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            Enum named => named.ToString(),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Quote(value.ToString()),
        };

        private static string Quote(string text) =>
            "\"" + (text ?? string.Empty).Replace('"', '\'').Replace('\r', ' ').Replace('\n', ' ') + "\"";
    }

    /// <summary>
    /// Switches for the verbose economy lines (the pattern of AutonomousDiagnosticsProperties): off by default, never
    /// shipped on, and they only decide whether a line is written, never what a bot does.
    /// </summary>
    public static class EconomyDiagnostics
    {
        [DOL.GS.ServerProperties.ServerProperty("economy_diagnostics", "craft_made_lines", "One CRAFT_MADE line per gamebot craft. Diagnostic only.", false)]
        public static bool CraftMadeLines;

        [DOL.GS.ServerProperties.ServerProperty("economy_diagnostics", "econ_trade_lines", "One ECON_LIST / ECON_SALE line per gamebot listing and sale (otherwise hourly sums). Diagnostic only.", false)]
        public static bool TradeLines;
    }
}
