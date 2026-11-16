using System;
using DOL.Database;
using DOL.GS.CraftingEconomy;
using DOL.Logging;

namespace DOL.GS.Crafting
{
    /// <summary>
    /// The real player's side of Crafting 2.0 (WP3, milestone M1; docs/CRAFTING.md for players,
    /// docs/CRAFTING-2.0-BUILD-PLAN.md for developers): era start, secondary caps, coin by level band and the smith's
    /// upkeep. Pure rules plus a few thin helpers that read the live settings, called from GamePlayer's crafting code, the
    /// crafting masters, the loot generators and the smith. Gamebots are GameNPCs and never reach any of it; companions
    /// (/bot) only matter here as the killer of a monster whose coin a real player shares.
    /// <para>
    /// Era start never takes anything away: an account-realm crafting row that exists keeps its skills and its order. Only a
    /// row created from now on (the first character an account logs in with in a realm) starts without an order.
    /// </para>
    /// </summary>
    public static class PlayerCraftingRules
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(PlayerCraftingRules));

        // ---- Era start (crafting_era_start) ----

        /// <summary>
        /// The orders a Master admits while era start is on: the six crafting orders of 1.65. Basic Crafting (1.76b, 2005)
        /// and Siegecraft as a primary order (a full tradeskill from 1.90; no siegecraft recipe exists in this world) are
        /// not era orders. Every order still trains siegecraft as a secondary skill.
        /// </summary>
        public static readonly eCraftingSkill[] EraOrders =
        [
            eCraftingSkill.WeaponCrafting, eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring,
            eCraftingSkill.Fletching, eCraftingSkill.Alchemy, eCraftingSkill.SpellCrafting,
        ];

        /// <summary>Whether <paramref name="skill"/> is one of the six orders of 1.65.</summary>
        public static bool IsEraOrder(eCraftingSkill skill) => Array.IndexOf(EraOrders, skill) >= 0;

        /// <summary>Whether a Master of <paramref name="order"/> admits new members: every order with era start off, the six
        /// era orders with it on. Members already in an order are never turned away (they only change their primary).</summary>
        public static bool IsOpenOrder(eCraftingSkill order, bool eraStart) => !eraStart || IsEraOrder(order);

        /// <summary>The skills of a crafting row created before era start (and with it off): every skill at 1, as
        /// <see cref="DbAccountXCrafting"/>'s own default.</summary>
        public static string AllSkillsAtOne => new DbAccountXCrafting().SerializedCraftingSkills;

        /// <summary>
        /// The order and skills a NEW account-realm crafting row starts with: none at all with era start on (the character
        /// joins an order at a Master); Basic Crafting with every skill at 1 with it off, as before 1.2.
        /// </summary>
        public static (eCraftingSkill Primary, string Skills) NewRowValues(bool eraStart) =>
            eraStart ? (eCraftingSkill.NoCrafting, string.Empty) : (eCraftingSkill.BasicCrafting, AllSkillsAtOne);

        /// <summary>A new account-realm crafting row by <see cref="NewRowValues"/>.</summary>
        public static DbAccountXCrafting NewAccountRealmRow(string account, int realm, bool eraStart)
        {
            (eCraftingSkill primary, string skills) = NewRowValues(eraStart);
            return new DbAccountXCrafting
            {
                AccountId = account,
                Realm = realm,
                CraftingPrimarySkill = (int) primary,
                SerializedCraftingSkills = skills,
            };
        }

        /// <summary>
        /// The primary a loaded row plays with. A row that holds skills but no primary (written by a tool or an old build)
        /// gets Basic Crafting, because CraftingProgressMgr saves no skills for a character without a primary: this way no
        /// load and save can drop a skill. Anything else is kept exactly.
        /// </summary>
        public static eCraftingSkill LoadedPrimary(eCraftingSkill stored, int skillCount) =>
            stored == eCraftingSkill.NoCrafting && skillCount > 0 ? eCraftingSkill.BasicCrafting : stored;

        /// <summary>
        /// The primary after a skill is added to a character (a Master's training or a GM's /crafting add): unchanged when
        /// the character has one; else the added skill when it is an era order and era start is on; else Basic Crafting (the
        /// pre-1.2 rule, which also keeps the row's skills from being dropped on save).
        /// </summary>
        public static eCraftingSkill PrimaryAfterAdding(eCraftingSkill current, eCraftingSkill added, bool eraStart)
        {
            if (current != eCraftingSkill.NoCrafting)
                return current;
            return eraStart && IsEraOrder(added) ? added : eCraftingSkill.BasicCrafting;
        }

        /// <summary>What a character's crafting attempt needs before it starts: the recipe's skill trained (any value of 0 or
        /// more). A character without it hears <see cref="NotTrainedMessage"/>.</summary>
        public static bool IsTrained(int skillValue) => skillValue >= 0;

        public static string NotTrainedMessage(string skillName) =>
            $"You have not been trained in {skillName}. Speak to a crafting master in your capital to join an order.";

        // ---- Secondary caps (crafting_secondary_caps) ----

        /// <summary>
        /// How far another order's craft may rise, in percent of the primary, while secondary caps are on. A 2002 player
        /// report: "If a tailor, fletching can only be raised to 75% of your tailoring skill, and all other trade skills
        /// 40%". The Masters' own words (Crafting.txt, InitialEntersentence) name each order's "good skills" (75%) and its
        /// "great difficulty" (40%), and a "great bit" (75%) or "small bit" (40%) of siegecraft. Spellcraft and alchemy are
        /// each other's 75%. The material skills (metal, leather, cloth, wood, gems, herbs) are never capped by this rule
        /// (recipes need them at the recipe's level); 100 means no cap. A character whose primary is not one of the six
        /// orders (Basic Crafting from before 1.2, or none) has no caps.
        /// </summary>
        public static int SecondaryCapPercent(eCraftingSkill primary, eCraftingSkill skill)
        {
            if (skill == primary || !IsEraOrder(primary) || !IsCappedCraft(skill))
                return 100;
            return (primary, skill) switch
            {
                (eCraftingSkill.WeaponCrafting, eCraftingSkill.ArmorCrafting) => 75,
                (eCraftingSkill.WeaponCrafting, eCraftingSkill.SiegeCrafting) => 75,
                (eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring) => 75,
                (eCraftingSkill.ArmorCrafting, eCraftingSkill.WeaponCrafting) => 75,
                (eCraftingSkill.Tailoring, eCraftingSkill.Fletching) => 75,
                (eCraftingSkill.Fletching, eCraftingSkill.WeaponCrafting) => 75,
                (eCraftingSkill.Fletching, eCraftingSkill.SiegeCrafting) => 75,
                (eCraftingSkill.SpellCrafting, eCraftingSkill.Alchemy) => 75,
                (eCraftingSkill.Alchemy, eCraftingSkill.SpellCrafting) => 75,
                _ => 40,
            };
        }

        /// <summary>The crafts the secondary caps apply to: the six orders' crafts and siegecraft.</summary>
        public static bool IsCappedCraft(eCraftingSkill skill) => IsEraOrder(skill) || skill == eCraftingSkill.SiegeCrafting;

        /// <summary>The highest value <paramref name="skill"/> may reach by crafting, or <see cref="int.MaxValue"/> when
        /// no cap applies (caps off, the primary itself, a material skill, or no era order as primary).</summary>
        public static int SkillCap(eCraftingSkill primary, int primaryValue, eCraftingSkill skill, bool capsOn)
        {
            if (!capsOn)
                return int.MaxValue;
            int percent = SecondaryCapPercent(primary, skill);
            if (percent >= 100)
                return int.MaxValue;
            return Math.Max(1, Math.Max(0, primaryValue) * percent / 100);
        }

        /// <summary>
        /// How many of <paramref name="count"/> points a skill at <paramref name="current"/> may take under
        /// <paramref name="cap"/>: all of them below the cap, the part up to it, none at or above it. A skill already above
        /// its cap (trained before the caps were switched on, or before a change of primary) keeps its value.
        /// </summary>
        public static int PointsAllowed(int current, int count, int cap)
        {
            if (count <= 0 || current >= cap)
                return 0;
            return (int) Math.Min(count, (long) cap - current);
        }

        // ---- Coin by level band (player_coin_by_level) ----

        /// <summary>
        /// The multiplier on a monster's coin for a real player whose XP runs at <paramref name="xpRate"/>: the rate
        /// itself while the setting is on and the rate is above 1, so a level is worth the coin it was at the classic rate;
        /// 1 otherwise (the setting never lowers coin). Capped at the XP parser's own maximum.
        /// </summary>
        public static double CoinMultiplier(bool settingOn, double xpRate)
        {
            if (!settingOn || double.IsNaN(xpRate) || double.IsInfinity(xpRate) || xpRate <= 1.0)
                return 1.0;
            return Math.Min(xpRate, LevelRangeXpRates.MaximumRate);
        }

        /// <summary><paramref name="copper"/> times <paramref name="multiplier"/>, rounded down; unchanged at 1 or less.</summary>
        public static long ScaleCoin(long copper, double multiplier)
        {
            if (copper <= 0)
                return Math.Max(0, copper);
            if (double.IsNaN(multiplier) || multiplier <= 1.0)
                return copper;
            double scaled = Math.Floor(copper * multiplier);
            return scaled >= long.MaxValue ? long.MaxValue : (long) scaled;
        }

        /// <summary>
        /// The real player a monster's coin is scaled for: the killer when it is a player, the owner of a player's pet, or,
        /// for a /bot companion in a player's group, the group's highest-level player. Null for gamebots and their pets,
        /// monsters and anything else, so a gamebot's coin never changes.
        /// </summary>
        public static GamePlayer CoinBeneficiary(GameObject killer)
        {
            if (killer is not GameLiving living)
                return null;
            GameLiving root = AutonomousBotRealmPointRewards.ResolveRootRewardOwner(living) ?? living;
            if (root is GamePlayer player)
                return player;
            if (root is GameBot { IsTemporaryGroupHelper: true, IsAutonomousWorldBot: false } companion && companion.Group != null)
            {
                GamePlayer best = null;
                foreach (GamePlayer member in companion.Group.GetPlayersInTheGroup())
                    if (best == null || member.Level > best.Level)
                        best = member;
                return best;
            }
            return null;
        }

        /// <summary>A monster's coin as the live settings pay it: scaled by the beneficiary's XP rate for its level
        /// (xp_rate_by_level inside a range, xp_rate elsewhere) while player_coin_by_level is on.</summary>
        public static long CoinForKiller(long copper, GameObject killer)
        {
            if (!CraftingEconomyProperties.PLAYER_COIN_BY_LEVEL)
                return copper;
            GamePlayer player = CoinBeneficiary(killer);
            if (player == null)
                return copper;
            return ScaleCoin(copper, CoinMultiplier(true, LevelRangeXpRates.PlayerRate(player.Level)));
        }

        // ---- Upkeep (player_upkeep) ----

        /// <summary>What the smith charges for a repair whose classic price is <paramref name="classicCost"/> under the
        /// live setting (classic, light = half rounded up, free = nothing). Whether the repair spends durability is
        /// Repair.ModifyConditionAndDurability's rule (WP1), which the smith calls.</summary>
        public static long SmithCost(long classicCost) => UpkeepPolicy.RepairCost(classicCost, UpkeepPolicy.Current);

        // ---- Logs ----

        /// <summary>CRAFT_ERA_JOIN: a new crafting row started without an order (event=start) or a character joined one
        /// (event=join). One line each; never per craft.</summary>
        public static void LogEraJoin(GamePlayer player, string eventName, eCraftingSkill order)
        {
            if (player == null || !Log.IsInfoEnabled)
                return;
            Log.Info(EconomyLog.Line(EconomyLog.CraftEraJoin,
                ("event", eventName), ("player", player.Name), ("realm", GlobalConstants.RealmToName(player.Realm)),
                ("order", order), ("era", CraftingEconomyProperties.CRAFTING_ERA_START)));
        }

        /// <summary>CRAFT_PLAYER_TITLE: a real player's crafting skill reached a new title (every hundred up to 1000).</summary>
        public static void LogTitle(GamePlayer player, eCraftingSkill skill, int value, bool primary)
        {
            if (player == null || !Log.IsInfoEnabled)
                return;
            Log.Info(EconomyLog.Line(EconomyLog.CraftPlayerTitle,
                ("player", player.Name), ("realm", GlobalConstants.RealmToName(player.Realm)), ("skill", skill),
                ("value", value), ("primary", primary)));
        }
    }
}
