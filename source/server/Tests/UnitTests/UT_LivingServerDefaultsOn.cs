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

        /// <summary>Bots as people (docs/BOTS-AS-PEOPLE-1.2.md, "Settings"): new 1.2 keys, on from the start, so no upgrade line.</summary>
        public static readonly string[] BotsAsPeople =
        [
            "bot_skill_profiles", "bot_skill_growth", "bot_skill_spare_player_group", "raid_learns_from_raiders",
            "bot_place_knowledge", "bot_ask_the_way", "bot_wrong_roads", "bot_movement_habits", "bot_move_strafe",
            "bot_move_stick_line", "bot_move_sit_and_walls", "bot_living_milestones",
        ];

        /// <summary>Era builds (docs/ERA-BUILDS-1.2.md, "Settings"): new 1.2 keys, on from the start, so no upgrade line.</summary>
        public static readonly string[] EraBuilds = ["bot_era_class_shares", "bot_era_builds", "rvr_roster_build_roles"];

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
            // Owner 2026-11-21: "set the server settings so the 'bot schedule' based on time or sessions is off by default".
            Assert.That(defaults["bot_schedule_mode"], Is.EqualTo("off"), "the whole roster online (clock in 1.2)");
            Assert.That(defaults["bot_living_breaks"], Is.EqualTo(true), "breaks are a separate feature and stay on");
            foreach (string key in KeptOff)
                Assert.That(defaults[key], Is.EqualTo(false), key + " stays off");
            foreach (string key in BotsAsPeople)
                Assert.That(defaults[key], Is.EqualTo(true), key + " (bots as people)");
            foreach (string key in EraBuilds)
                Assert.That(defaults[key], Is.EqualTo(true), key + " (era builds)");
            Assert.That(defaults["bot_era_offmeta_percent"], Is.EqualTo(10), "one new gamebot in ten on an odd spec (D5)");
            Assert.That(Upgrade.Changes.Select(change => change.Key).Intersect(EraBuilds.Append("bot_era_offmeta_percent")), Is.Empty,
                "era builds: new keys, no upgrade line");
            Assert.That(Upgrade.Changes.Select(change => change.Key).Intersect(BotsAsPeople), Is.Empty,
                "new keys: a world from an earlier build has no row, so it takes the code default without an upgrade line");
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
            // A key a later build changed again (bot_schedule_mode: clock in 1.2.0.1, off in 1.2.1) ends on that build's default.
            foreach (Upgrade.Change change in Upgrade.Changes.Where(change => change.Since == "1.2.0.1"))
            {
                Upgrade.Change last = Upgrade.Changes.Last(c => c.Key == change.Key);
                Assert.That(Upgrade.SameStoredValue(Convert.ToString(defaults[change.Key], System.Globalization.CultureInfo.InvariantCulture),
                    last.NewDefault), Is.True, change.Key);
            }
            foreach (Upgrade.Change change in Upgrade.Changes.Where(change => change.Since == "1.2.1"))
                Assert.That(Upgrade.SameStoredValue(Convert.ToString(defaults[change.Key], System.Globalization.CultureInfo.InvariantCulture),
                    change.NewDefault), Is.True, change.Key);
            Assert.That(Upgrade.Changes.Where(change => change.Since == "1.2.0.1").Select(change => change.Key),
                Is.EquivalentTo(TurnedOn.Append("bot_schedule_mode")));
            Assert.That(Upgrade.Changes.Where(change => change.Since == "1.2.1").Select(change => (change.Key, change.OldDefault, change.NewDefault)),
                Is.EqualTo(new[] { ("bot_schedule_mode", "clock", "off") }));
            Assert.That(Upgrade.Version, Is.EqualTo("1.2.1"));
            Assert.That(Upgrade.Version, Is.EqualTo(Upgrade.Changes.Last().Since), "the newest build with a change");
        }

        [Test]
        public void TheEarthmendersMutualHealMovesOffOnlyWhereItWasTheUntouchedOldDefault()
        {
            // Owner 2026-11-18, "Mutual heal off": the code default is now False.
            var change = Upgrade.Changes.Single(c => c.Since == "1.2.0.2");
            Assert.That((change.Key, change.OldDefault, change.NewDefault), Is.EqualTo(("epic_earthmender_mutual_heal", "True", "False")));
            Assert.That(Upgrade.SameStoredValue(Convert.ToString(Defaults()[change.Key], System.Globalization.CultureInfo.InvariantCulture),
                change.NewDefault), Is.True, "the code default");
            var untouched = new Dictionary<string, (string Value, string DefaultValue)> { ["epic_earthmender_mutual_heal"] = ("True", "True") };
            Assert.That(Upgrade.Plan("1.2.0.1", untouched), Is.EqualTo(new[] { "epic_earthmender_mutual_heal" }), "a world that ran an earlier 1.2 build");
            Assert.That(Upgrade.Plan("1.2", untouched), Is.EqualTo(new[] { "epic_earthmender_mutual_heal" }));
            Assert.That(Upgrade.Plan("1.2.0.2", untouched), Is.Empty, "once only");
            foreach (var row in new[] { ("True", ""), ("False", "True"), ("False", "False") }) // the launcher's choice; already off
                Assert.That(Upgrade.Plan("1.2.0.1", new Dictionary<string, (string, string)> { ["epic_earthmender_mutual_heal"] = row }), Is.Empty,
                    row.ToString());
        }

        [Test]
        public void AnUntouchedWorldMovesOnceAndAChoiceIsKept()
        {
            // A 1.1.4 world: the server wrote each old default and nobody changed it.
            var untouched = TurnedOn.ToDictionary(key => key, _ => ("False", "False"));
            untouched["bot_schedule_mode"] = ("off", "off");
            Assert.That(Upgrade.Plan("1.1.2.1", untouched), Is.EquivalentTo(untouched.Keys), "each key once");
            Assert.That(Upgrade.Plan("1.2", untouched), Is.EquivalentTo(untouched.Keys), "a world that ran an earlier 1.2 build");
            Assert.That(Upgrade.Plan("1.2.0.1", untouched), Is.Empty, "once only");
            // The schedule goes off -> clock (1.2.0.1) -> off (1.2.1): applied in order, so the world ends on off.
            Assert.That(Upgrade.PlanChanges("1.2", untouched).Where(change => change.Key == "bot_schedule_mode")
                    .Select(change => (change.Since, change.NewDefault)),
                Is.EqualTo(new[] { ("1.2.0.1", "clock"), ("1.2.1", "off") }));
            Assert.That(Upgrade.PlanChanges("1.1.2.1", untouched).Last(change => change.Key == "bot_schedule_mode").NewDefault, Is.EqualTo("off"));

            var chosen = new Dictionary<string, (string Value, string DefaultValue)>
            {
                ["bot_guilds_enabled"] = ("False", ""),          // written by the launcher: the player's choice
                ["bot_crafters"] = ("False", ""),                // likewise (every launcher save from 1.2 clears DefaultValue)
                ["bot_personalities_enabled"] = ("True", "False"), // already on
                ["bot_schedule_mode"] = ("session", "off"),      // another mode
                ["bot_upkeep"] = ("True", "True"),               // a fresh 1.2 world
            };
            Assert.That(Upgrade.Plan("1.2", chosen), Is.Empty);
        }

        [Test]
        public void TheBotScheduleMovesOffOnceWhereTheServerWroteClock()
        {
            // Owner 2026-11-21: "set the server settings so the 'bot schedule' based on time or sessions is off by default".
            // A 1.2 world holds ("clock", "clock") from 1.2.0.1's change or from a fresh 1.2 start.
            var clock = new Dictionary<string, (string Value, string DefaultValue)> { ["bot_schedule_mode"] = ("clock", "clock") };
            foreach (string done in new[] { "1.2.0.1", "1.2.0.2" })
            {
                Assert.That(Upgrade.Plan(done, clock), Is.EqualTo(new[] { "bot_schedule_mode" }), done);
                Assert.That(Upgrade.PlanChanges(done, clock).Single().NewDefault, Is.EqualTo("off"), done);
            }
            Assert.That(Upgrade.Plan("1.2.1", clock), Is.Empty, "once only");
            Assert.That(Upgrade.Plan("1.2", new Dictionary<string, (string, string)> { ["bot_schedule_mode"] = ("CLOCK", "clock") }),
                Is.EqualTo(new[] { "bot_schedule_mode" }), "the server's match ignores case");
            foreach (var row in new[]
                     {
                         ("clock", ""),        // chosen in the launcher (it clears DefaultValue on every save)
                         ("session", "clock"), // another mode
                         ("session", "off"),
                         ("session", ""),
                         ("off", "clock"),     // already off
                         ("clock", "off"),     // a player's clock on a row an earlier build wrote as off
                     })
                Assert.That(Upgrade.PlanChanges("1.2.0.2", new Dictionary<string, (string, string)> { ["bot_schedule_mode"] = row }), Is.Empty,
                    row.ToString());
            // ("clock", "off") from an earlier build too: 1.2.0.1 needs both off, 1.2.1 both clock.
            Assert.That(Upgrade.PlanChanges("1.1.4", new Dictionary<string, (string, string)> { ["bot_schedule_mode"] = ("clock", "off") }), Is.Empty);
        }

        [Test]
        public void AKeyWithTwoOldDefaultsInOneBuildMovesOnce()
        {
            // The chained plan must not change realm_raid_largest_size_percent's two 1.1.2.1 lines (100 or 150 -> 200).
            foreach (string old in new[] { "100", "150" })
            {
                var rows = new Dictionary<string, (string Value, string DefaultValue)> { ["realm_raid_largest_size_percent"] = (old, old) };
                Assert.That(Upgrade.PlanChanges("1.1.2", rows).Select(change => (change.OldDefault, change.NewDefault)),
                    Is.EqualTo(new[] { (old, "200") }), old);
            }
        }
    }
}
