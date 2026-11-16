using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS.Crafting;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// Crafting in bot life (Crafting 2.0 WP8b, design doc §7 and facet P), behind bot_crafting_social (off) and bot_crafters
    /// (off):
    /// <list type="bullet">
    /// <item>The crafting storyline ("pricewar", storylines in craft_drama.lines): two real crafter gamebots of the same
    /// order and realm undercut each other in /faction over three episodes. Its pair is cast only from bots that really
    /// share an order, and its scenes name their real craft ({recipe}) and the first one's real title ({title}).</item>
    /// <item>Guild crafters: a guild's crafter gamebots (a bot guild's, or bots a player invited into his guild) make for
    /// guildmates at material cost; a Crafting-charter bot guild's bank pays them the fee they waive.</item>
    /// </list>
    /// Use what's real: every name, craft and title comes from the bots' saved crafting skills.
    /// </summary>
    public static class CraftingDrama
    {
        /// <summary>bot_crafters and bot_crafting_social.</summary>
        public static bool On => CraftingEconomyProperties.BOT_CRAFTERS && CraftingEconomyProperties.BOT_CRAFTING_SOCIAL;

        /// <summary>The storyline arcs about crafting (scene ids story_&lt;arc&gt;_n).</summary>
        public static readonly IReadOnlyCollection<string> CraftArcs = new HashSet<string>(StringComparer.Ordinal) { "pricewar" };

        public static bool IsCraftArc(string arc) => arc != null && CraftArcs.Contains(arc);

        /// <summary>A crafter's real craft facts, from BotCraftingSkills' saved text.</summary>
        public readonly record struct CraftFacts(eCraftingSkill Primary, int Skill)
        {
            public bool Trained => Primary != eCraftingSkill.NoCrafting && Skill > 0;
            public string Title => BotCraftingRules.Title(Primary, Skill);
        }

        public static CraftFacts FactsOf(string skills)
        {
            if (string.IsNullOrWhiteSpace(skills))
                return new CraftFacts(eCraftingSkill.NoCrafting, 0);
            eCraftingSkill primary = BotCraftingSkills.Primary(skills);
            return new CraftFacts(primary, BotCraftingSkills.Value(skills, primary));
        }

        /// <summary>A price-war pair: both trained in the same order (a gear order: weapons, armor, tailoring, fletching). Pure.</summary>
        public static bool FitsPriceWar(CraftFacts a, CraftFacts b) =>
            a.Trained && b.Trained && a.Primary == b.Primary && Array.IndexOf(AutonomousBotCraftOrders.GearCrafts, a.Primary) >= 0;

        /// <summary>The slots a craft arc's scenes use: the pair's craft and the first one's title. Pure.</summary>
        public static void AddSlots(IDictionary<string, string> slots, CraftFacts a)
        {
            slots["recipe"] = CraftWord(a.Primary);
            slots["title"] = a.Title;
        }

        public static string CraftWord(eCraftingSkill craft) => craft switch
        {
            eCraftingSkill.WeaponCrafting => "weaponcraft",
            eCraftingSkill.ArmorCrafting => "armorcraft",
            eCraftingSkill.Tailoring => "tailoring",
            eCraftingSkill.Fletching => "fletching",
            eCraftingSkill.SpellCrafting => "spellcraft",
            eCraftingSkill.Alchemy => "alchemy",
            _ => "crafting",
        };

        private static readonly Regex AsksForCrafter = new(
            @"\b(any(one|body|1)? (craft|crafts|crafting|make|makes|can make)|who (crafts|can make|makes|does)|(need|lf|looking for) (a )?(crafter|smith|tailor|fletcher|armorcrafter|weaponcrafter|spellcrafter|alchemist)|guild crafters?)\b",
            RegexOptions.Compiled);

        /// <summary>A guild line that asks who crafts ("anyone craft?", "who can make studded?", "lf armorcrafter"). Pure.</summary>
        public static bool AsksWhoCrafts(string message) =>
            !string.IsNullOrWhiteSpace(message) && AsksForCrafter.IsMatch(message.ToLowerInvariant());

        /// <summary>
        /// A guild crafter's answer in guild chat: its real title and craft, what it makes surely (the highest level), and
        /// the guild price. Pure.
        /// </summary>
        public static string GuildCrafterAnswer(CraftFacts facts, int highestLevel, bool bankPaysFee, Random random)
        {
            string level = highestLevel > 0 ? $", up to lvl {highestLevel}" : string.Empty;
            string price = bankPaysFee ? "guild price is just materials, the bank covers my fee" : "materials only for guild";
            return (random ?? Random.Shared).Next(3) switch
            {
                0 => $"me, {facts.Title.ToLowerInvariant()}, {facts.Skill} {CraftWord(facts.Primary)}{level}. {price}, send me a tell",
                1 => $"i do {CraftWord(facts.Primary)} ({facts.Skill}){level}. tell me what you need, {price}",
                _ => $"{facts.Skill} {CraftWord(facts.Primary)} here{level}. {price}, pst",
            };
        }

        /// <summary>
        /// The fee a Crafting-charter bot guild's bank pays its crafter for a guildmate's piece: the fee the crafter waived
        /// (the board's fee on the materials), only while the bank holds twice that. 0 for any other guild (a player's guild
        /// bank is the player's). Pure.
        /// </summary>
        public static long BankFee(long materials, double bank, bool craftingBotGuild)
        {
            if (!craftingBotGuild || materials <= 0)
                return 0;
            long fee = AutonomousBotCraftOrders.Quote(materials) - materials;
            return bank >= fee * 2.0 ? fee : 0;
        }
    }
}
