using System;
using DOL.GS.ServerProperties;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Every setting of Crafting 2.0 (docs/CRAFTING-2.0-BUILD-PLAN.md §1.5), declared once by WP0 so the launcher (WP9)
    /// and every package read the same keys. <b>WP0 reads none of them</b>: declaring a key changes nothing until the
    /// package that reads it is merged. Owner rule (2026-11-16, replacing "every bot-side switch defaults to off"): every
    /// Living Server feature is on by default; only diagnostics, the classic crafting rules and test-only switches stay off.
    /// A mode (pace, commissions) does nothing while <see cref="BOT_CRAFTERS"/> is off. Keys taken from a fork keep
    /// the fork's name where it had one (True165's crafting_skill_rate; DividedRealms' bot_craft_orders), so a port
    /// changes only which class it reads.
    /// </summary>
    public static class CraftingEconomyProperties
    {
        // ---- Players (WP1, WP3) ----

        [ServerProperty("crafting", "crafting_era_start", "Era start: a new character joins a crafting order at a Master (1.65) instead of starting with Basic Crafting. Existing crafting rows are untouched. Read from WP3 on.", true)]
        public static bool CRAFTING_ERA_START = true;

        [ServerProperty("crafting", "crafting_secondary_caps", "Secondary crafting skills capped at 75% (the order's own secondary) and 40% (the others) of the primary, as a 2002 player reported. Read from WP3 on.", false)]
        public static bool CRAFTING_SECONDARY_CAPS;

        // From shadowofze's True165 (CraftingRate165, 00f9fbf): whole points per skill gain, 1-10 offered, never past a cap.
        [ServerProperty("crafting", "crafting_skill_rate", "Player crafting pace: skill points per gain (1 = classic). A gain still needs the normal chance, so a grey recipe gives nothing. Read from WP1 on.", 1.0)]
        public static double CRAFTING_SKILL_RATE = 1.0;

        [ServerProperty("crafting", "player_upkeep", "Player repair upkeep: classic (smith's price and durability), light (half price, no durability loss) or free. Read from WP1/WP3 on.", "light")]
        public static string PLAYER_UPKEEP = "light";

        [ServerProperty("crafting", "player_coin_by_level", "A real player's monster coin follows the XP-by-level multiplier of their band (bots' coin unchanged). Read from WP3 on.", true)]
        public static bool PLAYER_COIN_BY_LEVEL = true;

        [ServerProperty("crafting", "crafting_classic_quality", "Classic quality: 94-99% with a small masterpiece chance at every skill (off = the 1.87 rules, 96-99% and masterpieces only at 1000 or grey). Read from WP1 on.", false)]
        public static bool CRAFTING_CLASSIC_QUALITY;

        [ServerProperty("crafting", "crafting_classic_losses", "Classic losses: a failed craft costs its materials and a spellcraft explosion destroys the item. Read from WP1 on.", false)]
        public static bool CRAFTING_CLASSIC_LOSSES;

        // ---- Autonomous gamebots (WP4-WP8). Companions and players are never affected. ----

        [ServerProperty("bots", "bot_crafters", "Master switch for gamebot crafters: Crafter-type gamebots join an order, buy real materials, work at real stations and sell what they make. On by default (owner 2026-11-16). Off: no gamebot crafts. Read from WP4 on.", true)]
        public static bool BOT_CRAFTERS = true;

        // DividedRealms' second switch (AutonomousBotCraftOrders, ceac061a), on by default (owner 2026-11-16), only with bot_crafters.
        [ServerProperty("bots", "bot_craft_orders", "Gamebots pay crafter gamebots of their realm to make an upgrade, with the coin held until delivery. Needs bot_crafters. Read from WP5 on.", true)]
        public static bool BOT_CRAFT_ORDERS = true;

        [ServerProperty("bots", "bot_crafter_share_percent", "Percent of NEW gamebots that are Crafters (existing bots keep their stored type). Read from WP4 on.", 7)]
        public static int BOT_CRAFTER_SHARE_PERCENT = 7;

        [ServerProperty("bots", "bot_crafting_pace", "Gamebot crafting pace: xp (points per gain follow the bot XP multiplier of the crafter's level band) or classic (one point). Needs bot_crafters. Read from WP4 on.", "xp")]
        public static string BOT_CRAFTING_PACE = "xp";

        [ServerProperty("bots", "realm_commissions", "Realm quartermaster commissions to crafter gamebots: classic (30 an hour), doubled (60) or off. Needs bot_crafters. Read from WP5 on.", "doubled")]
        public static string REALM_COMMISSIONS = "doubled";

        // Owner 2026-11-15: built, but 0 until the economy ledger shows a need (RELEASE-TRAIN's rule); 5 is the documented option.
        [ServerProperty("bots", "exchange_tax_percent", "Realm Exchange tax in percent of a sale, taken from the seller's proceeds (0 = none; 5 is the documented option). Only a trade pays it. Read from WP5 on.", 0)]
        public static int EXCHANGE_TAX_PERCENT;

        [ServerProperty("bots", "bot_upkeep", "Gamebots' gear wears like a player's and they pay for repairs, potions and poisons. Needs bot_crafters. Read from WP7 on.", true)]
        public static bool BOT_UPKEEP = true;

        [ServerProperty("bots", "bot_luxury_spending", "Rich gamebots spend part of their surplus on real goods and services (the inflation sink). Needs bot_crafters. Read from WP7 on.", true)]
        public static bool BOT_LUXURY_SPENDING = true;

        [ServerProperty("bots", "bot_siege_supply", "Siegecrafter gamebots supply siege engines and kits below the merchant price. Needs bot_crafters. Read from WP7 on.", true)]
        public static bool BOT_SIEGE_SUPPLY = true;

        [ServerProperty("bots", "bot_crafter_chat_rate", "Crafter gamebots' trade and station chat, as a multiple of normal (0 = silent). Read from WP4 on.", 1.0)]
        public static double BOT_CRAFTER_CHAT_RATE = 1.0;

        // DividedRealms: 3 posts plus 3 for a crafter's own work (OwnWorkExtraListings, ceac061a).
        [ServerProperty("bots", "bot_crafter_exchange_listings", "Realm Exchange posts a crafter gamebot may hold for its own work (other bots hold 3). Read from WP5 on.", 6)]
        public static int BOT_CRAFTER_EXCHANGE_LISTINGS = 6;

        // ---- Commerce and social (WP8; amended into WP0's settings by WP8) ----

        [ServerProperty("bots", "bot_crafter_commerce", "Real players trade with crafter gamebots: ask by /tell or /trade what they make and sell, get a quote from their real skill and stock, haggle, commission a piece through the craft-order board, buy stock in a trade window. Needs bot_crafters. Read from WP8 on.", false)]
        public static bool BOT_CRAFTER_COMMERCE;

        [ServerProperty("bots", "bot_crafting_social", "Crafting in bot life: a price-war storyline between two real crafter gamebots of one order, guild crafters who make for guildmates at material cost (the bank pays their fee), no crafting-haste guild buff without a player crafter. Needs bot_crafters. Read from WP8 on.", false)]
        public static bool BOT_CRAFTING_SOCIAL;

        // ---- Launcher and instrumentation ----

        [ServerProperty("bots", "launcher_level50_kit", "The launcher's level-50 bot batches arrive with the quality-100 kit and 10,000 gold (off for a fresh-start world). Read by the launcher from WP9 on.", true)]
        public static bool LAUNCHER_LEVEL50_KIT = true;

        [ServerProperty("bots", "economy_ledger_enabled", "Hourly economy ledger: money supply, faucets and sinks per realm (ECON_SUPPLY, ECON_FLOWS). Logs and one table row an hour only; changes no amount. Read from WP2 on.", true)]
        public static bool ECONOMY_LEDGER_ENABLED = true;

        /// <summary>The player upkeep setting as a mode; anything unknown is <see cref="UpkeepPolicy.Mode.Light"/>.</summary>
        public static UpkeepPolicy.Mode ParseUpkeep(string value) =>
            string.Equals(value?.Trim(), "classic", StringComparison.OrdinalIgnoreCase) ? UpkeepPolicy.Mode.Classic
            : string.Equals(value?.Trim(), "free", StringComparison.OrdinalIgnoreCase) ? UpkeepPolicy.Mode.Free
            : UpkeepPolicy.Mode.Light;

        /// <summary>The tax setting kept within 0-50 percent.</summary>
        public static int SanitizeTaxPercent(int value) => Math.Clamp(value, 0, 50);
    }
}
