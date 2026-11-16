using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using Upgrade = DOL.GS.ServerProperties.LivingServerSettingsUpgrade;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Owner 2026-11-16: "They should be on by default - all our living server features should be in by default if someone
    /// downloads our installer". Every player-facing Living Server switch defaults on; diagnostics, the classic rules, the epic
    /// encounter options (owner decisions), destructive gates and the edition switch keep their values. Worlds that ran an
    /// earlier build move once where the row still holds the untouched old default. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_LivingServerDefaultsOn
    {
        /// <summary>The switches 1.2 turned on (their old default in brackets is what an earlier build stored).</summary>
        public static readonly string[] TurnedOn =
        [
            "bot_guilds_enabled", "bot_personalities_enabled", "bot_living_firsts", "bot_living_death", "bot_living_alarms",
            "bot_living_nemesis", "bot_living_breaks", "bot_living_body_language",
            "bot_crafters", "bot_craft_orders", "bot_upkeep", "bot_luxury_spending", "bot_siege_supply",
            "bot_crafter_commerce", "bot_crafting_social",
        ];

        /// <summary>Kept off, each for its reason (docs/CRAFTING.md, AGENTS.md).</summary>
        public static readonly string[] KeptOff =
        [
            // diagnostics
            "craft_made_lines", "econ_trade_lines",
            // classic rules for real players (a choice of rule, not a feature)
            "crafting_secondary_caps", "crafting_classic_quality", "crafting_classic_losses", "mana_regen_amount_halved_below_50_percent",
            // epic encounter options, the owner's decisions until a played check
            "epic_funus_weapon_puzzle", "epic_full_fire_patches", "epic_govannon_full_health", "epic_govannon_full_heal",
            "epic_easmarach_endless_falls_heal",
            // owner-only destructive gate
            "allow_character_deletion",
        ];

        private static Dictionary<string, object> Defaults() =>
            typeof(Properties).Assembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Select(field => field.GetCustomAttribute<ServerPropertyAttribute>())
                .Where(attribute => attribute != null)
                .GroupBy(attribute => attribute.Key)
                .ToDictionary(group => group.Key, group => group.First().DefaultValue);

        [Test]
        public void EveryLivingServerFeatureDefaultsOn()
        {
            var defaults = Defaults();
            foreach (string key in TurnedOn)
                Assert.That(defaults[key], Is.EqualTo(true), key);
            Assert.That(defaults["bot_schedule_mode"], Is.EqualTo("clock"), "the real-clock schedule (RELEASE-TRAIN phase C)");
            foreach (string key in KeptOff)
                Assert.That(defaults[key], Is.EqualTo(false), key + " stays off");
            Assert.That(defaults["exchange_tax_percent"], Is.EqualTo(0), "the tax stays 0 (owner 2026-11-15)");
            Assert.That(defaults["enable_sluaghbinder"], Is.EqualTo(true), "the edition switch is set by the edition, unchanged");
        }

        [Test]
        public void TheCraftingFieldsStartOnBeforeTheDatabaseIsRead()
        {
            // CraftingEconomyProperties gives each field its default as a start value (WP0's convention), so code that runs
            // before InitProperties (tests, tools) sees the same defaults as a fresh world.
            Assert.That(new[]
            {
                CraftingEconomyProperties.BOT_CRAFTERS, CraftingEconomyProperties.BOT_CRAFT_ORDERS, CraftingEconomyProperties.BOT_UPKEEP,
                CraftingEconomyProperties.BOT_LUXURY_SPENDING, CraftingEconomyProperties.BOT_SIEGE_SUPPLY,
                CraftingEconomyProperties.BOT_CRAFTER_COMMERCE, CraftingEconomyProperties.BOT_CRAFTING_SOCIAL,
            }.All(on => on), Is.True);
        }

        [Test]
        public void EachChangedDefaultIsTheCodeDefault()
        {
            var defaults = Defaults();
            foreach (Upgrade.Change change in Upgrade.Changes.Where(change => change.Since == "1.2.0.1"))
                Assert.That(Upgrade.SameStoredValue(Convert.ToString(defaults[change.Key], System.Globalization.CultureInfo.InvariantCulture),
                    change.NewDefault), Is.True, change.Key);
            Assert.That(Upgrade.Changes.Where(change => change.Since == "1.2.0.1").Select(change => change.Key),
                Is.EquivalentTo(TurnedOn.Append("bot_schedule_mode")));
            Assert.That(Upgrade.Version, Is.EqualTo("1.2.0.1"));
        }

        [Test]
        public void AnUntouchedWorldMovesOnceAndAChoiceIsKept()
        {
            // A 1.1.4 world: the server wrote each old default and nobody changed it.
            var untouched = TurnedOn.ToDictionary(key => key, _ => ("False", "False"));
            untouched["bot_schedule_mode"] = ("off", "off");
            Assert.That(Upgrade.Plan("1.1.2.1", untouched), Is.EquivalentTo(untouched.Keys));
            Assert.That(Upgrade.Plan("1.2", untouched), Is.EquivalentTo(untouched.Keys), "a world that ran an earlier 1.2 build");
            Assert.That(Upgrade.Plan("1.2.0.1", untouched), Is.Empty, "once only");

            var chosen = new Dictionary<string, (string Value, string DefaultValue)>
            {
                ["bot_guilds_enabled"] = ("False", ""),          // written by the launcher: the player's choice
                ["bot_crafters"] = ("False", ""),                // likewise (every launcher save from 1.2 clears DefaultValue)
                ["bot_personalities_enabled"] = ("True", "False"), // already on
                ["bot_schedule_mode"] = ("session", "off"),      // another mode
                ["bot_upkeep"] = ("True", "True"),               // a fresh 1.2 world
            };
            Assert.That(Upgrade.Plan("1.2", chosen), Is.Empty);
            Assert.That(Upgrade.PlanChanges("1.2", untouched).Single(change => change.Key == "bot_schedule_mode").NewDefault, Is.EqualTo("clock"));
        }
    }
}
