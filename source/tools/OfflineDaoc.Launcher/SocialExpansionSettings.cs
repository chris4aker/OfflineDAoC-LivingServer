using System.Data.SQLite;
using System.Globalization;
using DOL.GS;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The Living Server's server settings (docs/BOT-GUILDS.md, docs/BOT-PERSONALITIES.md,
/// docs/XP-BY-LEVEL-RANGE.md), read from and written to the world database's ServerProperty table,
/// as the launcher's Options tab does for the flat XP rates. The server reads them at startup.
/// A setting without a row uses the server's default, which is the default here too.
/// </summary>
public sealed record SocialExpansionSettings
{
    public const string SuggestedPlayerXp = "1-19:3;20-34:5;35-50:12";
    public const string SuggestedBotXp = "1-19:1.5;20-34:2.5;35-50:6";

    public bool BotGuilds { get; init; } = true;
    public int GuildSharePercent { get; init; } = 70;
    public int GuildMinLevel { get; init; } = 5;
    public bool GuildSocial { get; init; } = true;
    public bool GuildRecruitPlayer { get; init; } = true;
    public bool GuildClaims { get; init; } = true;
    public bool GuildEconomy { get; init; } = true;
    public bool GuildAlliances { get; init; } = true;
    /// <summary>Gamebots you invite into your guild pay its dues and earn it merit (Living Server 1.1.2). On by default; works with bot guilds off.</summary>
    public bool PlayerGuildEconomy { get; init; } = true;
    public bool Personalities { get; init; } = true;
    public bool Voices { get; init; } = true;
    public bool Debates { get; init; } = true;
    /// <summary>How much bots talk unprompted, 0 to 3 (bot_voices_chat_rate). 2 by default from Living Server 1.1.2 (owner
    /// 2026-11-04, after the playtest measured 6.4 lines a minute in Albion at 900 bots a realm with 1).</summary>
    public double ChatRate { get; init; } = 2.0;
    public bool LivingMemory { get; init; } = true;
    public bool LivingGrats { get; init; } = true;
    public bool LivingFirsts { get; init; } = true;
    public bool LivingDeath { get; init; } = true;
    public bool LivingAlarms { get; init; } = true;
    public bool LivingNemesis { get; init; } = true;
    /// <summary>off, session or clock (AutonomousBotSchedule); clock by default since Living Server 1.2.</summary>
    public string ScheduleMode { get; init; } = "clock";
    public bool LivingBreaks { get; init; } = true;
    public bool LivingBodyLanguage { get; init; } = true;
    /// <summary>About 4% of Crafter bots are beggars in their capital (AutonomousBeggars). On by default.</summary>
    public bool LivingBeggars { get; init; } = true;
    /// <summary>Some No Lifer bots run a level-50 buffbot that keeps them buffed (NoLiferBuffBots, Living Server 1.2). On by default.</summary>
    public bool LivingBuffBots { get; init; } = true;
    /// <summary>Deeds and reputation, storylines, gold spammers and roleplayers, true rumours (the living-world parts). On by default.</summary>
    public bool LivingDeeds { get; init; } = true;
    public bool LivingStorylines { get; init; } = true;
    public bool LivingCharacters { get; init; } = true;
    public bool LivingRumours { get; init; } = true;
    /// <summary>Holiday chatter and Leeroy in raids (Beta 1.1). On by default.</summary>
    public bool LivingHolidays { get; init; } = true;
    public bool LivingLeeroy { get; init; } = true;
    /// <summary>The lore's screen-centre atmosphere lines (Living Server 1.2, docs/LORE.md). On by default.</summary>
    public bool LivingAtmosphere { get; init; } = true;
    public string PlayerXpByLevel { get; init; } = "";
    public string BotXpByLevel { get; init; } = "";
    /// <summary>
    /// Power regeneration multiplier for players and bots alike (Living Server 1.1.2: 1.5; 1 is the classic pre-1.87 rate,
    /// which patch 1.87 doubled).
    /// </summary>
    public double ManaRegenModifier { get; init; } = DefaultManaRegenModifier;
    /// <summary>The classic rule: list casters regenerate power at half speed below 50% power. Off by default since 1.1.2.</summary>
    public bool ManaHalvedBelowHalf { get; init; }
    /// <summary>Bot-led groups (Living Server 1.2): the leader of a gamebot party may invite you, and you follow (bot_group_invite_player). On by default.</summary>
    public bool BotGroupInvites { get; init; } = true;
    /// <summary>How many members you count as in the loot of a group with gamebots, whoever leads (bot_group_player_loot_weight, 1 to 8).</summary>
    public int PlayerLootWeight { get; init; } = DefaultPlayerLootWeight;
    /// <summary>When you say yes to a bot leader's invite: "follow" (the default) or "lead" (bot_led_group_join_mode, Living Server 1.2).</summary>
    public string JoinMode { get; init; } = "follow";
    /// <summary>A forming raid's leader may invite you at level 50 (bot_raid_invite_player, Living Server 1.2). On by default.</summary>
    public bool BotRaidInvites { get; init; } = true;

    // ---- Crafting 2.0, the player's side (docs/CRAFTING.md; CraftingEconomyProperties on the server) ----
    /// <summary>A new character joins a crafting order at a Master instead of starting with Basic Crafting (crafting_era_start). On by default.</summary>
    public bool CraftingEraStart { get; init; } = true;
    /// <summary>Another order's craft stops at 75% or 40% of the primary (crafting_secondary_caps). Off by default.</summary>
    public bool CraftingSecondaryCaps { get; init; }
    /// <summary>Crafting skill points per gain, 1 (classic) to 10 (crafting_skill_rate, True165's key).</summary>
    public double CraftingPace { get; init; } = DefaultCraftingPace;
    /// <summary>The smith's repairs: classic, light (the default) or free (player_upkeep).</summary>
    public string PlayerUpkeep { get; init; } = "light";
    /// <summary>Monster coin follows your XP rate for your level (player_coin_by_level). On by default.</summary>
    public bool PlayerCoinByLevel { get; init; } = true;
    /// <summary>94-99% quality with a masterpiece chance at every skill (crafting_classic_quality). Off by default.</summary>
    public bool CraftingClassicQuality { get; init; }
    /// <summary>Failed crafts cost materials and explosions destroy the item (crafting_classic_losses). Off by default.</summary>
    public bool CraftingClassicLosses { get; init; }

    // ---- Crafting 2.0, the gamebots' side and the economy (CraftingEconomyProperties; WP9). Defaults are the server's. ----
    /// <summary>Crafter-type gamebots join an order, buy materials, craft at stations and sell what they make (bot_crafters). On by default since 1.2 (owner 2026-11-16).</summary>
    public bool BotCrafters { get; init; } = true;
    /// <summary>Percent of NEW gamebots that are Crafters, 0-100 (bot_crafter_share_percent); existing bots keep their stored type.</summary>
    public int BotCrafterSharePercent { get; init; } = DefaultBotCrafterSharePercent;
    /// <summary>xp (the bot XP rate of the crafter's level) or classic (bot_crafting_pace).</summary>
    public string BotCraftingPace { get; init; } = "xp";
    /// <summary>Gamebots pay crafter gamebots of their realm for an upgrade, coin held until delivery (bot_craft_orders). On by default since 1.2; needs bot crafters.</summary>
    public bool BotCraftOrders { get; init; } = true;
    /// <summary>The realm quartermaster's commissions to crafter gamebots: classic, doubled (the default) or off (realm_commissions).</summary>
    public string RealmCommissions { get; init; } = "doubled";
    /// <summary>Realm Exchange posts a crafter gamebot may hold for its own work (bot_crafter_exchange_listings), 3 to 100.</summary>
    public int BotCrafterExchangeListings { get; init; } = DefaultBotCrafterExchangeListings;
    /// <summary>Crafter gamebots' trade and station chat, 0 to 3 times normal (bot_crafter_chat_rate).</summary>
    public double BotCrafterChatRate { get; init; } = DefaultBotCrafterChatRate;
    /// <summary>The Realm Exchange tax in percent of a sale, 0 to 50, from the seller's proceeds (exchange_tax_percent). 0 by default.</summary>
    public int ExchangeTaxPercent { get; init; }
    /// <summary>The hourly economy ledger (economy_ledger_enabled): logs and one row an hour, no amount changes. On by default.</summary>
    public bool EconomyLedger { get; init; } = true;
    /// <summary>The launcher's "+ Lv.50" bots arrive with the quality-100 kit and 10,000 gold (launcher_level50_kit). On by default.</summary>
    public bool Level50Kit { get; init; } = true;
    /// <summary>The launcher's bot batches draw each new gamebot's class from its realm's 2003 share (bot_era_class_shares,
    /// era builds 1.2; EraClassShares). On by default.</summary>
    public bool BotEraClassShares { get; init; } = true;
    /// <summary>Percent of the No Lifer bots that run a buffbot, 0 to 100 (bot_nolifer_buffbot_percent).</summary>
    public int BuffBotPercent { get; init; } = DefaultBuffBotPercent;
    /// <summary>Buffbots' concentration buffs have no range limit, the 2003 rule (bot_nolifer_buffbot_era_conc_range). On by default.</summary>
    public bool BuffBotEraConcRange { get; init; } = true;

    // ---- Bots as people (docs/BOTS-AS-PEOPLE-1.2.md): the four master switches. The parts are server keys only. ----
    /// <summary>Skill tiers and facets that vary and improve with play (bot_skill_profiles). On by default (1.2).</summary>
    public bool BotSkillProfiles { get; init; } = true;
    /// <summary>Bots know only the places they have been, ask the way and now and then take a wrong road (bot_place_knowledge). On by default (1.2).</summary>
    public bool BotPlaceKnowledge { get; init; } = true;
    /// <summary>Strafing, /stick lines, overshooting, sitting and keep walls (bot_movement_habits). On by default (1.2).</summary>
    public bool BotMovementHabits { get; init; } = true;
    /// <summary>Personal milestones bots chase and talk about, in the Herald (bot_living_milestones). On by default (1.2).</summary>
    public bool LivingMilestones { get; init; } = true;

    public static readonly string[] UpkeepModes = ["classic", "light", "free"];

    /// <summary>The bot crafting paces the server knows (BotCraftingRules.PaceRate: anything but classic is xp).</summary>
    public static readonly string[] BotCraftingPaces = ["xp", "classic"];

    /// <summary>The commission modes (QuartermasterCommissions.ParseMode: off/none/0 is off, anything unknown doubled).</summary>
    public static readonly string[] CommissionModes = ["classic", "doubled", "off"];

    public const int DefaultBotCrafterSharePercent = 7, DefaultBotCrafterExchangeListings = 6, DefaultBuffBotPercent = 30;
    /// <summary>MarketSeams.ListingPolicy.CrafterCap: never below the 3 every bot has, never above the 100 consignment slots.</summary>
    public const int MinBotCrafterExchangeListings = 3, MaxBotCrafterExchangeListings = 100;
    /// <summary>CraftingEconomyProperties.SanitizeTaxPercent.</summary>
    public const int MaxExchangeTaxPercent = 50;
    public const double DefaultBotCrafterChatRate = 1, MaxBotCrafterChatRate = 3;

    public const double DefaultCraftingPace = 1, MinCraftingPace = 1, MaxCraftingPace = 10;

    public static readonly string[] JoinModes = ["follow", "lead"];

    public const int DefaultPlayerLootWeight = 2, MinPlayerLootWeight = 1, MaxPlayerLootWeight = 8;

    public const double DefaultManaRegenModifier = 1.5, MinManaRegenModifier = 0.25, MaxManaRegenModifier = 10;

    /// <summary>
    /// The server's defaults. Since Living Server 1.2 (owner 2026-11-16: "all our living server features should be in by
    /// default if someone downloads our installer") every feature and Server life part is on, with the real-clock schedule;
    /// off stay the classic rules (power halving, secondary caps, classic quality and losses) and both XP ranges (rates, not
    /// switches: Setup's <see cref="Recommended"/> sets them).
    /// </summary>
    public static SocialExpansionSettings Defaults { get; } = new();

    /// <summary>
    /// What Setup turns on for a first install (owner 2026-10-28: "every setting turned on and to our suggested level"):
    /// every feature and Server life part on, the suggested XP ranges, and the real-clock bot schedule. The guild share and
    /// lowest level keep their defaults. Bot crafters and their craft orders are on too (owner 2026-11-16, replacing Crafting
    /// 2.0's earlier rule that every bot-side switch defaults off). Since 1.2 this is <see cref="Defaults"/> plus the XP ranges.
    /// </summary>
    public static SocialExpansionSettings Recommended { get; } = new()
    {
        BotGuilds = true, GuildSocial = true, GuildRecruitPlayer = true, GuildClaims = true, GuildEconomy = true, GuildAlliances = true,
        PlayerGuildEconomy = true, ManaRegenModifier = DefaultManaRegenModifier, ManaHalvedBelowHalf = false,
        Personalities = true, Voices = true, Debates = true,
        LivingMemory = true, LivingGrats = true, LivingFirsts = true, LivingDeath = true, LivingAlarms = true, LivingNemesis = true,
        ScheduleMode = "clock", LivingBreaks = true, LivingBodyLanguage = true, LivingBeggars = true, LivingBuffBots = true, LivingDeeds = true,
        LivingStorylines = true, LivingCharacters = true, LivingRumours = true, LivingHolidays = true, LivingLeeroy = true,
        LivingAtmosphere = true, PlayerXpByLevel = SuggestedPlayerXp, BotXpByLevel = SuggestedBotXp,
        BotGroupInvites = true, PlayerLootWeight = DefaultPlayerLootWeight, JoinMode = "follow", BotRaidInvites = true,
        BotCrafters = true, BotCraftOrders = true, BotEraClassShares = true,
        BotSkillProfiles = true, BotPlaceKnowledge = true, BotMovementHabits = true, LivingMilestones = true,
    };

    // Key, category and description of each setting, as the server and the Python helpers write them.
    internal static readonly (string Key, string Category, string Description)[] Settings =
    [
        ("bot_guilds_enabled", "guild", "Autonomous gamebots form and join realm guilds (see docs/BOT-GUILDS.md), and players can /ginvite them. False keeps every gamebot guildless; saved memberships are kept."),
        ("bot_guild_share", "guild", "Share (0 to 1) of eligible autonomous gamebots that bot guild formation places in guilds."),
        ("bot_guild_min_level", "guild", "Lowest level at which an autonomous gamebot is placed in a bot guild."),
        ("bot_guild_social", "guild", "When bot guilds are on: guild members greet, congratulate and chat in guild chat when a real member can hear, answer your /gu, and bot guild officers recruit in /faction."),
        ("bot_guild_recruit_player", "guild", "When bot guild social life is on: bot guild officers may invite you to their guild with a yes/no dialog."),
        ("bot_guild_claims_keeps", "guild", "When bot guilds are on: bot guilds claim keeps and towers their realm takes, and defend them first."),
        ("bot_guild_economy", "guild", "When bot guilds are on: guilded gamebots pay dues from real loot, level-ups and realm ranks earn merit points, and leaders buy guild buffs."),
        ("bot_player_guild_economy", "guild", "Gamebots you invite into your guild pay its dues (the rate its leader set) from the real coin they loot, and their level-ups and realm ranks earn it merit points as a player's do. Guild realm points, keep claims and guild buffs still never count a bot. Works whether bot guilds are on or off."),
        ("bot_guild_alliances", "guild", "When bot guilds are on: each realm's RvR and raiding bot guilds form one alliance."),
        ("bot_personalities_enabled", "bots", "Autonomous gamebots get a personality and traits that tilt town breaks, goals, dungeons, camps and chattiness (see docs/BOT-PERSONALITIES.md)."),
        ("bot_voices_enabled", "bots", "Autonomous gamebots say pre-written, period lines in their own writing style (docs/bot-voices/DESIGN.md)."),
        ("bot_voices_debates", "bots", "When bot voices are on: bots of a realm play debates and long threads in /faction while you are online."),
        ("bot_voices_chat_rate", "bots", ChatRateDescription),
        ("bot_living_memory", "bots", "Server life: autonomous gamebots of your realm remember grouping with you, your rezzes and rude words; they greet you by name, thank you for an old rez, answer you by name, mention you in /faction now and then, and stay cool after insults. Chat only. False keeps today's bots."),
        ("bot_living_grats", "bots", "Server life: when you or a bot gains a level, groupmates say grats in /group and bots nearby in /say, and a realm's bots cheer a new level 50 in /faction."),
        ("bot_living_firsts", "bots", "Server life: the first character on the server (you or a bot) to reach level 50, the first of each class to reach 50 and the first to each realm rank from 2 up are announced to everyone and recorded for the Camelot Herald."),
        ("bot_living_death", "bots", "Server life: a bot that dies near you asks you for a rez if your class can rez, waits longer for it and thanks you after; a bot healer that is not in your group may rez you (you still accept or decline); bots talk about corpse runs and resurrection sickness."),
        ("bot_living_alarms", "bots", "Server life: a bot in the frontier that sees 3 or more enemies calls it out in /faction (rough size, classes, place), at most one call per realm every 2 minutes."),
        ("bot_living_nemesis", "bots", "Server life: a bot killed by an enemy remembers its killer, names it (by race and class) in /faction after the second kill, and celebrates when it kills that enemy back. Kills are recorded for the Camelot Herald's Most Wanted."),
        ("bot_schedule_mode", "bots", "Server life: when autonomous gamebots are online. off = the whole roster at once; session = each bot plays sessions of 1-4 hours with 20 minutes to 2 hours away, timed from server start; clock (the default since Living Server 1.2) = your PC's time of day (busiest 7-11pm and weekends, quiet overnight)."),
        ("bot_living_breaks", "bots", "Server life: an idle bot now and then says brb and logs off for 5-25 minutes, and a bot grouped with you rarely goes linkdead for 30-90 seconds (the group hears about it) and comes back with an apology. Never during a fight, siege or relic march."),
        ("bot_living_body_language", "bots", "Server life: autonomous gamebots standing around in town, waiting for their group or resting look around, emote now and then (ponder, shrug, laugh, stretch their legs with a step or two in town), turn and wave at you when you walk up, and cheer when someone dings nearby. Only where a real player can see it; never in a fight. Bots cannot jump or sit (the game sends no such state for them)."),
        ("bot_living_beggars", "bots", "Server life: about 4% of the Crafter bots are beggars. They live in their capital, beg you and passing bots for coin and loot (drag coin or an item onto one to give it), sell what they are given and hoard every copper. Ask anyone for gold or items and the beggars follow you around the city insulting you and defending their corners until you leave. Beggars never group or fight."),
        ("bot_nolifer_buffbots", "bots", "Server life: some No Lifer bots (bot_nolifer_buffbot_percent of them, 30 by default) run a second character, a level-50 buffbot (Albion Cleric, Midgard Shaman, Hibernia Druid, all in buffs). It follows its main where it is safe, parks at the nearest bind when the main goes into a dungeon, the frontier or a raid, and keeps the main buffed. It never joins a group, fights or loots, is not counted in the population, and logs in and out with its main. A bot is a No Lifer by its roster id, whether bot personalities are on or off."),
        ("bot_living_deeds", "bots", "Server life: bots of your realm that see you solo a named monster, go on a kill streak in the frontier, get zerged or die to something silly tell /faction a few minutes later, and others react. Deeds build a reputation the realm talks about, and a nickname."),
        ("bot_living_storylines", "bots", "Server life: pairs of bots live out dramas in /faction over days: a kill steal, a guild split, a romance, a ninja-loot trial. The same two bots play every episode; take a side in faction and the ending follows you."),
        ("bot_living_characters", "bots", "Server life: now and then a gold spammer floods /faction until it is banned, and a few bots roleplay in thee-and-thou speech while the realm mocks them."),
        ("bot_living_rumours", "bots", "Server life: bots pass on rumours that are true: a rare named monster that just spawned, or an enemy keep whose guards are down. Go and see."),
        ("bot_living_holidays", "bots", "Server life: around Halloween, Thanksgiving, Christmas, New Year and other holidays on your PC's calendar, bots talk about them like 2003 players did and answer your holiday wishes."),
        ("bot_living_leeroy", "bots", "Server life: one end-game raid in twenty has a bot who charges the final boss before the raid is ready, yelling his name, and the raid has to follow him in."),
        ("living_atmosphere_lines", "bots", "Atmosphere lines: short lines in the middle of the screen when you first enter a dungeon or epic zone that day, step into a boss's lair, a boss changes phase, a dragon wakes, a realm kills a boss for the first time or a raid forms. At most one every 30 seconds, and realm news always comes first."),
        ("bot_group_invite_player", "bots", "Bot-led groups: the leader of a gamebot party of your realm and level hunting where you can walk or ride may send you a tell and the group invite box, at most once every 45 minutes unasked (never while you fight, ride, trade, are grouped, have /spawn companions out, are /afk or in the frontier). Say yes and you follow: the leader picks the camp (at most 6 levels above you), the meeting spot and the pulls, and waits for you. False stops unasked invites; /lfg still gets you one."),
        ("bot_group_player_loot_weight", "bots", "In a group with you and gamebots, whoever leads: you count as this many members in the coin split and the item rotation (2: with seven bots nearby you get 2 of every 9 items and 2/9 of the coin), and an item your class can use that beats what you wear comes to you first. 1 to 8."),
        ("bot_led_group_join_mode", "bots", "Bot-led groups: when you say yes to a gamebot leader's invite. follow = the leader runs the group and you follow (/lead takes over at any time); lead = you become the leader at once and the bots follow you, as bots you /invite do (/lead bot hands it back)."),
        ("bot_raid_invite_player", "bots", "Raid leaders: when a dragon or epic dungeon raid forms in your realm and you are level 50, its raid leader (an officer of a raiding guild) may send you a tell and a box, once per raid. Yes puts you in a party that needs your class (one gamebot of your role steps out); you travel to the muster town yourself. A no is final for that raid. /invites off stops these too."),
        ("crafting_era_start", "crafting", "Era start: a new character joins a crafting order at a Master (1.65) instead of starting with Basic Crafting. Existing crafting rows are untouched. Set in the launcher's Living Server tab."),
        ("crafting_secondary_caps", "crafting", "Secondary crafting skills capped at 75% (the order's own secondary) and 40% (the others) of the primary, as a 2002 player reported. Set in the launcher's Living Server tab."),
        ("crafting_skill_rate", "crafting", "Player crafting pace: skill points per gain (1 = classic). A gain still needs the normal chance, so a grey recipe gives nothing. Set in the launcher's Living Server tab."),
        ("player_upkeep", "crafting", "Player repair upkeep: classic (smith's price and durability), light (half price, no durability loss) or free. Set in the launcher's Living Server tab."),
        ("player_coin_by_level", "crafting", "A real player's monster coin follows the XP-by-level multiplier of their band (bots' coin unchanged). Set in the launcher's Living Server tab."),
        ("crafting_classic_quality", "crafting", "Classic quality: 94-99% with a small masterpiece chance at every skill (off = the 1.87 rules, 96-99% and masterpieces only at 1000 or grey). Set in the launcher's Living Server tab."),
        ("crafting_classic_losses", "crafting", "Classic losses: a failed craft costs its materials and a spellcraft explosion destroys the item. Set in the launcher's Living Server tab."),
        ("bot_crafters", "bots", "Master switch for gamebot crafters: Crafter-type gamebots join an order, buy real materials, work at real stations and sell what they make. Off: no gamebot crafts. Set in the launcher's Living Server tab."),
        ("bot_crafter_share_percent", "bots", "Percent of NEW gamebots that are Crafters (existing bots keep their stored type). Set in the launcher's Living Server tab."),
        ("bot_crafting_pace", "bots", "Gamebot crafting pace: xp (points per gain follow the bot XP multiplier of the crafter's level band) or classic (one point). Needs bot_crafters. Set in the launcher's Living Server tab."),
        ("bot_craft_orders", "bots", "Gamebots pay crafter gamebots of their realm to make an upgrade, with the coin held until delivery. Needs bot_crafters. Set in the launcher's Living Server tab."),
        ("realm_commissions", "bots", "Realm quartermaster commissions to crafter gamebots: classic (30 an hour), doubled (60) or off. Needs bot_crafters. Set in the launcher's Living Server tab."),
        ("bot_crafter_exchange_listings", "bots", "Realm Exchange posts a crafter gamebot may hold for its own work (other bots hold 3). Set in the launcher's Living Server tab."),
        ("bot_crafter_chat_rate", "bots", "Crafter gamebots' trade and station chat, as a multiple of normal (0 = silent). Set in the launcher's Living Server tab."),
        ("exchange_tax_percent", "bots", "Realm Exchange tax in percent of a sale, taken from the seller's proceeds (0 = none; 5 is the documented option). Only a trade pays it. Set in the launcher's Living Server tab."),
        ("economy_ledger_enabled", "bots", "Hourly economy ledger: money supply, faucets and sinks per realm (ECON_SUPPLY, ECON_FLOWS). Logs and one table row an hour only; changes no amount. Set in the launcher's Living Server tab."),
        ("launcher_level50_kit", "bots", "The launcher's level-50 bot batches arrive with the quality-100 kit and 10,000 gold (off for a fresh-start world). Set in the launcher's Living Server tab."),
        ("bot_era_class_shares", "bots", "Era builds: the launcher's bot batches draw each new gamebot's class from its realm's 2003 class share (estimates from 2002-2003 forum posts; Cleric 12% of Albion, Reaver 3%) instead of evenly. Existing bots keep their class. Set in the launcher's Living Server tab."),
        ("bot_nolifer_buffbot_percent", "bots", "Percent (0 to 100) of the No Lifer bots that run a buffbot. Which No Lifers have one is fixed from each main's roster id, so the same mains keep theirs across restarts."),
        ("bot_nolifer_buffbot_era_conc_range", "bots", "No Lifers' buffbots follow the 2003 rule for concentration buffs: no range limit, so a buffbot parked at a bind keeps its main buffed (Mythic added the 5,000-unit limit with its Classic servers in July 2005). False: their concentration buffs switch off beyond this server's buff range like everyone else's, and come back in range."),
        ("bot_skill_profiles", "bots", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): every autonomous gamebot gets a skill tier (Casual, Average or Veteran, 30/50/20, shifted by rvr_bot_awareness) and six facets: reactions, positioning, pulls, standing in fire, buffs and power use. A Casual bot pulls an add now and then, lets a buff run out and reacts late; a Veteran rarely does, and no mistake takes a named or a monster more than 2 levels above the group. Never companions, raids, battleground assaults or fights at a keep. False turns bot_skill_growth and bot_skill_spare_player_group off with it. Log: BOT_SKILL_PROFILE, BOT_SKILL_BUDGET, BOT_SKILL_MISTAKE. In this build it changes nothing yet (work package 2): the profiles are made, logged and grow, but no bot plays differently until the mistakes come."),
        ("bot_place_knowledge", "bots", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): a gamebot knows the places it has been, and at the start what a player of its level would: its capital, its home town, its realm's zones below its level (and their towns) and, from level 20, its border keep. Dungeons, the frontier and the next zones are new ground until it goes there; it learns a place on arrival. Saved per bot; /lswhy way lists them. On its own it changes no route and no camp: asking the way and wrong roads read it. False turns bot_ask_the_way and bot_wrong_roads off with it. Log: BOT_PLACE_LEARNED, BOT_PLACE_BUDGET."),
        ("bot_movement_habits", "bots", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): gamebots move like people: melee bots circle-strafe in open-field RvR, bot groups travel on /stick in a line, bots overshoot where they stop, sit when they wait, crowd the bank and sit on the walls of keeps their realm holds. Never in raids, battleground waves, fights at a keep, stable rides or a group with you. False turns bot_move_strafe, bot_move_stick_line and bot_move_sit_and_walls off with it. Log: BOT_MOVE_HABITS."),
        ("bot_living_milestones", "bots", "Server life, Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): gamebots chase a personal goal (the ding to 50, realm rank 5, a dragon trophy really won in a raid) and talk about it as they get close, in /faction, their guild, their group or /say; the realm cheers when one gets there, the Camelot Herald lists it under Milestones of the Realm, and the goal leans the bot's next plans. Log: BOT_MILESTONE_ARC, BOT_MILESTONE_TALK, BOT_MILESTONE_REACHED. In this build it changes nothing yet (work package 6)."),
        ("xp_rate_by_level", "rates", "Optional XP rate by level range for real player characters (set in the launcher's Living Server tab)."),
        ("bot_xp_rate_by_level", "rates", "Optional XP rate by level range for autonomous gamebots (set in the launcher's Living Server tab)."),
        ("mana_regen_amount_modifier", "rates", "Power regeneration multiplier for real players and gamebots alike. 1 is the classic rate before patch 1.87, which doubled it; 1.5 (the Living Server default) is a middle ground. Set in the launcher's Living Server tab."),
        ("mana_regen_amount_halved_below_50_percent", "rates", "Halve power regeneration below 50% power for list casters (the classic rule). Applies to real players and gamebots alike. Off by default in the Living Server; set in the launcher's Living Server tab."),
    ];

    /// <summary>The server's description of bot_voices_chat_rate (ServerProperties.BOT_VOICES_CHAT_RATE).</summary>
    public const string ChatRateDescription =
        "When bot voices are on: how much bots talk on their own, from 0 (they only answer) to 3. 2 (the default) sounds like a busy server; 1 is calmer, 3 very busy. Chat already rises and falls with the hour and with how many bots are on (chat-budget.json).";

    /// <summary>The value the server would read for each key.</summary>
    public IReadOnlyDictionary<string, string> ToStoredValues() => new Dictionary<string, string>
    {
        ["bot_guilds_enabled"] = Bool(BotGuilds),
        ["bot_guild_share"] = (GuildSharePercent / 100.0).ToString("0.##", CultureInfo.InvariantCulture),
        ["bot_guild_min_level"] = GuildMinLevel.ToString(CultureInfo.InvariantCulture),
        ["bot_guild_social"] = Bool(GuildSocial),
        ["bot_guild_recruit_player"] = Bool(GuildRecruitPlayer),
        ["bot_guild_claims_keeps"] = Bool(GuildClaims),
        ["bot_guild_economy"] = Bool(GuildEconomy),
        ["bot_guild_alliances"] = Bool(GuildAlliances),
        ["bot_player_guild_economy"] = Bool(PlayerGuildEconomy),
        ["bot_personalities_enabled"] = Bool(Personalities),
        ["bot_voices_enabled"] = Bool(Voices),
        ["bot_voices_debates"] = Bool(Debates),
        ["bot_voices_chat_rate"] = ChatRate.ToString("0.##", CultureInfo.InvariantCulture),
        ["bot_living_memory"] = Bool(LivingMemory),
        ["bot_living_grats"] = Bool(LivingGrats),
        ["bot_living_firsts"] = Bool(LivingFirsts),
        ["bot_living_death"] = Bool(LivingDeath),
        ["bot_living_alarms"] = Bool(LivingAlarms),
        ["bot_living_nemesis"] = Bool(LivingNemesis),
        ["bot_schedule_mode"] = ScheduleMode,
        ["bot_living_breaks"] = Bool(LivingBreaks),
        ["bot_living_body_language"] = Bool(LivingBodyLanguage),
        ["bot_living_beggars"] = Bool(LivingBeggars),
        ["bot_nolifer_buffbots"] = Bool(LivingBuffBots),
        ["bot_living_deeds"] = Bool(LivingDeeds),
        ["bot_living_storylines"] = Bool(LivingStorylines),
        ["bot_living_characters"] = Bool(LivingCharacters),
        ["bot_living_rumours"] = Bool(LivingRumours),
        ["bot_living_holidays"] = Bool(LivingHolidays),
        ["bot_living_leeroy"] = Bool(LivingLeeroy),
        ["living_atmosphere_lines"] = Bool(LivingAtmosphere),
        ["xp_rate_by_level"] = PlayerXpByLevel.Trim(),
        ["bot_xp_rate_by_level"] = BotXpByLevel.Trim(),
        ["mana_regen_amount_modifier"] = ManaRegenModifier.ToString("0.##", CultureInfo.InvariantCulture),
        ["mana_regen_amount_halved_below_50_percent"] = Bool(ManaHalvedBelowHalf),
        ["bot_group_invite_player"] = Bool(BotGroupInvites),
        ["bot_group_player_loot_weight"] = PlayerLootWeight.ToString(CultureInfo.InvariantCulture),
        ["bot_led_group_join_mode"] = JoinMode,
        ["bot_raid_invite_player"] = Bool(BotRaidInvites),
        ["crafting_era_start"] = Bool(CraftingEraStart),
        ["crafting_secondary_caps"] = Bool(CraftingSecondaryCaps),
        ["crafting_skill_rate"] = CraftingPace.ToString("0.##", CultureInfo.InvariantCulture),
        ["player_upkeep"] = PlayerUpkeep,
        ["player_coin_by_level"] = Bool(PlayerCoinByLevel),
        ["crafting_classic_quality"] = Bool(CraftingClassicQuality),
        ["crafting_classic_losses"] = Bool(CraftingClassicLosses),
        ["bot_crafters"] = Bool(BotCrafters),
        ["bot_crafter_share_percent"] = BotCrafterSharePercent.ToString(CultureInfo.InvariantCulture),
        ["bot_crafting_pace"] = BotCraftingPace,
        ["bot_craft_orders"] = Bool(BotCraftOrders),
        ["realm_commissions"] = RealmCommissions,
        ["bot_crafter_exchange_listings"] = BotCrafterExchangeListings.ToString(CultureInfo.InvariantCulture),
        ["bot_crafter_chat_rate"] = BotCrafterChatRate.ToString("0.##", CultureInfo.InvariantCulture),
        ["exchange_tax_percent"] = ExchangeTaxPercent.ToString(CultureInfo.InvariantCulture),
        ["economy_ledger_enabled"] = Bool(EconomyLedger),
        ["launcher_level50_kit"] = Bool(Level50Kit),
        ["bot_era_class_shares"] = Bool(BotEraClassShares),
        ["bot_nolifer_buffbot_percent"] = BuffBotPercent.ToString(CultureInfo.InvariantCulture),
        ["bot_nolifer_buffbot_era_conc_range"] = Bool(BuffBotEraConcRange),
        ["bot_skill_profiles"] = Bool(BotSkillProfiles),
        ["bot_place_knowledge"] = Bool(BotPlaceKnowledge),
        ["bot_movement_habits"] = Bool(BotMovementHabits),
        ["bot_living_milestones"] = Bool(LivingMilestones),
    };

    private static string Bool(bool value) => value ? "True" : "False";

    /// <summary>
    /// Settings from stored values (missing keys keep the server default). A stored value the server can't read
    /// shows what the server then uses (its fields stay at zero: False, 0), and a share above 1 shows as 100%;
    /// each is reported in <paramref name="problems"/> and its key added to <paramref name="problemKeys"/>, so a
    /// save can repair exactly those rows.
    /// </summary>
    public static SocialExpansionSettings FromStoredValues(IReadOnlyDictionary<string, string?> stored, List<string> problems,
        ISet<string>? problemKeys = null)
    {
        var d = Defaults;
        void Problem(string key, string message)
        {
            problems.Add(message);
            problemKeys?.Add(key);
        }
        bool ReadBool(string key, bool fallback)
        {
            if (!stored.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text)) return fallback;
            if (bool.TryParse(text.Trim(), out bool value)) return value;
            Problem(key, $"{key} = \"{text}\" is not True or False, so the server reads it as False.");
            return false;
        }
        int share = d.GuildSharePercent;
        if (stored.TryGetValue("bot_guild_share", out string? shareText) && !string.IsNullOrWhiteSpace(shareText))
        {
            if (!double.TryParse(shareText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double fraction))
            {
                Problem("bot_guild_share", $"bot_guild_share = \"{shareText}\" is not a number, so the server reads it as 0.");
                share = 0;
            }
            else if (fraction < 0 || fraction > 1)
            {
                Problem("bot_guild_share", $"bot_guild_share = \"{shareText}\" is outside 0 to 1.");
                share = fraction < 0 ? 0 : 100;
            }
            else
                share = (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
        }
        int minLevel = d.GuildMinLevel;
        if (stored.TryGetValue("bot_guild_min_level", out string? levelText) && !string.IsNullOrWhiteSpace(levelText))
        {
            if (!int.TryParse(levelText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
            {
                Problem("bot_guild_min_level", $"bot_guild_min_level = \"{levelText}\" is not a whole number, so the server reads it as 0.");
                minLevel = 0;
            }
            else if (level < 0 || level > 50)
            {
                Problem("bot_guild_min_level", $"bot_guild_min_level = \"{levelText}\" is outside 0 to 50.");
                minLevel = Math.Clamp(level, 0, 50);
            }
            else
                minLevel = level;
        }
        double manaRate = d.ManaRegenModifier;
        if (stored.TryGetValue("mana_regen_amount_modifier", out string? manaText) && !string.IsNullOrWhiteSpace(manaText))
        {
            if (!double.TryParse(manaText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || double.IsNaN(rate))
            {
                Problem("mana_regen_amount_modifier", $"mana_regen_amount_modifier = \"{manaText}\" is not a number, so the server reads it as 0 (the slowest regeneration).");
                manaRate = MinManaRegenModifier;
            }
            else if (rate < MinManaRegenModifier || rate > MaxManaRegenModifier)
            {
                Problem("mana_regen_amount_modifier", $"mana_regen_amount_modifier = \"{manaText}\" is outside {MinManaRegenModifier.ToString(CultureInfo.InvariantCulture)} to {MaxManaRegenModifier.ToString(CultureInfo.InvariantCulture)}.");
                manaRate = Math.Clamp(rate, MinManaRegenModifier, MaxManaRegenModifier);
            }
            else
                manaRate = Math.Round(rate, 2, MidpointRounding.AwayFromZero);
        }
        double chatRate = d.ChatRate;
        if (stored.TryGetValue("bot_voices_chat_rate", out string? rateText) && !string.IsNullOrWhiteSpace(rateText))
        {
            if (!double.TryParse(rateText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
            {
                Problem("bot_voices_chat_rate", $"bot_voices_chat_rate = \"{rateText}\" is not a number, so the server reads it as 0 (bots only answer).");
                chatRate = 0;
            }
            else if (rate < 0 || rate > 3)
            {
                Problem("bot_voices_chat_rate", $"bot_voices_chat_rate = \"{rateText}\" is outside 0 to 3.");
                chatRate = Math.Clamp(rate, 0, 3);
            }
            else
                chatRate = rate;
        }
        int lootWeight = d.PlayerLootWeight;
        if (stored.TryGetValue("bot_group_player_loot_weight", out string? weightText) && !string.IsNullOrWhiteSpace(weightText))
        {
            if (!int.TryParse(weightText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
            {
                Problem("bot_group_player_loot_weight", $"bot_group_player_loot_weight = \"{weightText}\" is not a whole number, so the server reads it as 0 (counted as 1).");
                lootWeight = MinPlayerLootWeight;
            }
            else if (weight < MinPlayerLootWeight || weight > MaxPlayerLootWeight)
            {
                Problem("bot_group_player_loot_weight", $"bot_group_player_loot_weight = \"{weightText}\" is outside {MinPlayerLootWeight} to {MaxPlayerLootWeight}.");
                lootWeight = Math.Clamp(weight, MinPlayerLootWeight, MaxPlayerLootWeight);
            }
            else
                lootWeight = weight;
        }
        string joinMode = d.JoinMode;
        if (stored.TryGetValue("bot_led_group_join_mode", out string? joinText) && !string.IsNullOrWhiteSpace(joinText))
        {
            joinMode = joinText.Trim().ToLowerInvariant();
            if (!JoinModes.Contains(joinMode))
            {
                Problem("bot_led_group_join_mode", $"bot_led_group_join_mode = \"{joinText}\" is not follow or lead, so the server reads it as follow.");
                joinMode = "follow";
            }
        }
        // The crafting rows (Crafting 2.0): an unreadable value leaves the server on its default (the field keeps it), so
        // the tab shows that default, unlike the older rows whose server fields read as False or 0.
        bool ReadCraftingBool(string key, bool fallback)
        {
            if (!stored.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text)) return fallback;
            if (bool.TryParse(text.Trim(), out bool value)) return value;
            Problem(key, $"{key} = \"{text}\" is not True or False, so the server keeps its default ({Bool(fallback)}).");
            return fallback;
        }
        double pace = d.CraftingPace;
        if (stored.TryGetValue("crafting_skill_rate", out string? paceText) && !string.IsNullOrWhiteSpace(paceText))
        {
            if (!double.TryParse(paceText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || double.IsNaN(rate))
            {
                Problem("crafting_skill_rate", $"crafting_skill_rate = \"{paceText}\" is not a number, so the server keeps its default (1).");
                pace = DefaultCraftingPace;
            }
            else if (rate < MinCraftingPace || rate > MaxCraftingPace || rate != Math.Floor(rate))
            {
                Problem("crafting_skill_rate", $"crafting_skill_rate = \"{paceText}\" is not a whole number from {MinCraftingPace} to {MaxCraftingPace}.");
                pace = Math.Clamp(Math.Round(rate, MidpointRounding.AwayFromZero), MinCraftingPace, MaxCraftingPace);
            }
            else
                pace = rate;
        }
        string upkeep = d.PlayerUpkeep;
        if (stored.TryGetValue("player_upkeep", out string? upkeepText) && !string.IsNullOrWhiteSpace(upkeepText))
        {
            upkeep = upkeepText.Trim().ToLowerInvariant();
            if (!UpkeepModes.Contains(upkeep))
            {
                // CraftingEconomyProperties.ParseUpkeep: anything unknown is light.
                Problem("player_upkeep", $"player_upkeep = \"{upkeepText}\" is not classic, light or free, so the server reads it as light.");
                upkeep = "light";
            }
        }
        // The gamebots' crafting and the economy (CraftingEconomyProperties): an unreadable number leaves the server on its
        // default (the field keeps it), and one out of range is clamped by the code that reads it, as shown here.
        int ReadCraftingInt(string key, int fallback, int min, int max)
        {
            if (!stored.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text)) return fallback;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                Problem(key, $"{key} = \"{text}\" is not a whole number, so the server keeps its default ({fallback}).");
                return fallback;
            }
            if (value < min || value > max)
            {
                Problem(key, $"{key} = \"{text}\" is outside {min} to {max}, so the server uses {Math.Clamp(value, min, max)}.");
                return Math.Clamp(value, min, max);
            }
            return value;
        }
        double crafterChat = d.BotCrafterChatRate;
        if (stored.TryGetValue("bot_crafter_chat_rate", out string? crafterChatText) && !string.IsNullOrWhiteSpace(crafterChatText))
        {
            if (!double.TryParse(crafterChatText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || double.IsNaN(rate))
                Problem("bot_crafter_chat_rate", $"bot_crafter_chat_rate = \"{crafterChatText}\" is not a number, so the server keeps its default (1).");
            else if (rate < 0 || rate > MaxBotCrafterChatRate)
            {
                Problem("bot_crafter_chat_rate", $"bot_crafter_chat_rate = \"{crafterChatText}\" is outside 0 to 3.");
                crafterChat = Math.Clamp(rate, 0, MaxBotCrafterChatRate);
            }
            else
                crafterChat = Math.Round(rate, 1, MidpointRounding.AwayFromZero);
        }
        string botPace = d.BotCraftingPace;
        if (stored.TryGetValue("bot_crafting_pace", out string? botPaceText) && !string.IsNullOrWhiteSpace(botPaceText))
        {
            botPace = botPaceText.Trim().ToLowerInvariant();
            if (!BotCraftingPaces.Contains(botPace))
            {
                Problem("bot_crafting_pace", $"bot_crafting_pace = \"{botPaceText}\" is not xp or classic, so the server reads it as xp.");
                botPace = "xp";
            }
        }
        string commissions = d.RealmCommissions;
        if (stored.TryGetValue("realm_commissions", out string? commissionText) && !string.IsNullOrWhiteSpace(commissionText))
        {
            commissions = commissionText.Trim().ToLowerInvariant();
            if (commissions is "none" or "0")
                commissions = "off";   // QuartermasterCommissions.ParseMode reads these as off too
            else if (!CommissionModes.Contains(commissions))
            {
                Problem("realm_commissions", $"realm_commissions = \"{commissionText}\" is not classic, doubled or off, so the server reads it as doubled.");
                commissions = "doubled";
            }
        }
        // bot_nolifer_buffbot_percent is a ServerProperties field without a start value: unreadable, the server reads 0.
        int buffBotPercent = d.BuffBotPercent;
        if (stored.TryGetValue("bot_nolifer_buffbot_percent", out string? buffText) && !string.IsNullOrWhiteSpace(buffText))
        {
            if (!int.TryParse(buffText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent))
            {
                Problem("bot_nolifer_buffbot_percent", $"bot_nolifer_buffbot_percent = \"{buffText}\" is not a whole number, so the server reads it as 0 (no buffbots).");
                buffBotPercent = 0;
            }
            else if (percent is < 0 or > 100)
            {
                Problem("bot_nolifer_buffbot_percent", $"bot_nolifer_buffbot_percent = \"{buffText}\" is outside 0 to 100.");
                buffBotPercent = Math.Clamp(percent, 0, 100);
            }
            else
                buffBotPercent = percent;
        }
        string schedule = d.ScheduleMode;
        if (stored.TryGetValue("bot_schedule_mode", out string? scheduleText) && !string.IsNullOrWhiteSpace(scheduleText))
        {
            schedule = scheduleText.Trim().ToLowerInvariant();
            if (!ScheduleModes.Contains(schedule))
            {
                Problem("bot_schedule_mode", $"bot_schedule_mode = \"{scheduleText}\" is not off, session or clock, so the server reads it as off.");
                schedule = "off";
            }
        }
        return new SocialExpansionSettings
        {
            BotGuilds = ReadBool("bot_guilds_enabled", d.BotGuilds),
            GuildSharePercent = share,
            GuildMinLevel = minLevel,
            GuildSocial = ReadBool("bot_guild_social", d.GuildSocial),
            GuildRecruitPlayer = ReadBool("bot_guild_recruit_player", d.GuildRecruitPlayer),
            GuildClaims = ReadBool("bot_guild_claims_keeps", d.GuildClaims),
            GuildEconomy = ReadBool("bot_guild_economy", d.GuildEconomy),
            GuildAlliances = ReadBool("bot_guild_alliances", d.GuildAlliances),
            PlayerGuildEconomy = ReadBool("bot_player_guild_economy", d.PlayerGuildEconomy),
            Personalities = ReadBool("bot_personalities_enabled", d.Personalities),
            Voices = ReadBool("bot_voices_enabled", d.Voices),
            Debates = ReadBool("bot_voices_debates", d.Debates),
            ChatRate = chatRate,
            LivingMemory = ReadBool("bot_living_memory", d.LivingMemory),
            LivingGrats = ReadBool("bot_living_grats", d.LivingGrats),
            LivingFirsts = ReadBool("bot_living_firsts", d.LivingFirsts),
            LivingDeath = ReadBool("bot_living_death", d.LivingDeath),
            LivingAlarms = ReadBool("bot_living_alarms", d.LivingAlarms),
            LivingNemesis = ReadBool("bot_living_nemesis", d.LivingNemesis),
            ScheduleMode = schedule,
            LivingBreaks = ReadBool("bot_living_breaks", d.LivingBreaks),
            LivingBodyLanguage = ReadBool("bot_living_body_language", d.LivingBodyLanguage),
            LivingBeggars = ReadBool("bot_living_beggars", d.LivingBeggars),
            LivingBuffBots = ReadBool("bot_nolifer_buffbots", d.LivingBuffBots),
            LivingDeeds = ReadBool("bot_living_deeds", d.LivingDeeds),
            LivingStorylines = ReadBool("bot_living_storylines", d.LivingStorylines),
            LivingCharacters = ReadBool("bot_living_characters", d.LivingCharacters),
            LivingRumours = ReadBool("bot_living_rumours", d.LivingRumours),
            LivingHolidays = ReadBool("bot_living_holidays", d.LivingHolidays),
            LivingLeeroy = ReadBool("bot_living_leeroy", d.LivingLeeroy),
            LivingAtmosphere = ReadBool("living_atmosphere_lines", d.LivingAtmosphere),
            // Kept as written: an invalid range setting shows its own error in the tab (the server ignores it).
            PlayerXpByLevel = stored.TryGetValue("xp_rate_by_level", out string? player) ? player?.Trim() ?? "" : "",
            BotXpByLevel = stored.TryGetValue("bot_xp_rate_by_level", out string? bots) ? bots?.Trim() ?? "" : "",
            ManaRegenModifier = manaRate,
            ManaHalvedBelowHalf = ReadBool("mana_regen_amount_halved_below_50_percent", d.ManaHalvedBelowHalf),
            BotGroupInvites = ReadBool("bot_group_invite_player", d.BotGroupInvites),
            PlayerLootWeight = lootWeight,
            JoinMode = joinMode,
            BotRaidInvites = ReadBool("bot_raid_invite_player", d.BotRaidInvites),
            CraftingEraStart = ReadCraftingBool("crafting_era_start", d.CraftingEraStart),
            CraftingSecondaryCaps = ReadCraftingBool("crafting_secondary_caps", d.CraftingSecondaryCaps),
            CraftingPace = pace,
            PlayerUpkeep = upkeep,
            PlayerCoinByLevel = ReadCraftingBool("player_coin_by_level", d.PlayerCoinByLevel),
            CraftingClassicQuality = ReadCraftingBool("crafting_classic_quality", d.CraftingClassicQuality),
            CraftingClassicLosses = ReadCraftingBool("crafting_classic_losses", d.CraftingClassicLosses),
            BotCrafters = ReadCraftingBool("bot_crafters", d.BotCrafters),
            BotCrafterSharePercent = ReadCraftingInt("bot_crafter_share_percent", d.BotCrafterSharePercent, 0, 100),
            BotCraftingPace = botPace,
            BotCraftOrders = ReadCraftingBool("bot_craft_orders", d.BotCraftOrders),
            RealmCommissions = commissions,
            BotCrafterExchangeListings = ReadCraftingInt("bot_crafter_exchange_listings", d.BotCrafterExchangeListings,
                MinBotCrafterExchangeListings, MaxBotCrafterExchangeListings),
            BotCrafterChatRate = crafterChat,
            ExchangeTaxPercent = ReadCraftingInt("exchange_tax_percent", d.ExchangeTaxPercent, 0, MaxExchangeTaxPercent),
            EconomyLedger = ReadCraftingBool("economy_ledger_enabled", d.EconomyLedger),
            Level50Kit = ReadCraftingBool("launcher_level50_kit", d.Level50Kit),
            BotEraClassShares = ReadCraftingBool("bot_era_class_shares", d.BotEraClassShares),
            BuffBotPercent = buffBotPercent,
            BuffBotEraConcRange = ReadBool("bot_nolifer_buffbot_era_conc_range", d.BuffBotEraConcRange),
            BotSkillProfiles = ReadBool("bot_skill_profiles", d.BotSkillProfiles),
            BotPlaceKnowledge = ReadBool("bot_place_knowledge", d.BotPlaceKnowledge),
            BotMovementHabits = ReadBool("bot_movement_habits", d.BotMovementHabits),
            LivingMilestones = ReadBool("bot_living_milestones", d.LivingMilestones),
        };
    }

    /// <summary>The schedule modes the server knows (AutonomousBotSchedule.Parse).</summary>
    public static readonly string[] ScheduleModes = ["off", "session", "clock"];

    /// <summary>True when any Living Server part is on.</summary>
    public bool AnyLiving => LivingMemory || LivingGrats || LivingFirsts || LivingDeath || LivingAlarms || LivingNemesis ||
                             ScheduleMode != "off" || LivingBreaks || LivingBodyLanguage || LivingBeggars || LivingBuffBots ||
                             LivingDeeds || LivingStorylines || LivingCharacters || LivingRumours || LivingHolidays || LivingLeeroy ||
                             LivingAtmosphere;

    /// <summary>
    /// "All off": the three features, every Living Server part, bot leaders' invites, bot crafters and their craft orders, the
    /// four "Bots as people" master switches, and both XP ranges off. The share, lowest level and each part keep their values, so turning a feature back on later restores
    /// the player's own choices. Power regeneration and the loot share are rates, not features, and keep their values too, as
    /// do the crafting rules (they are the game's rules for your characters, not Living Server features), the Exchange tax, the
    /// economy ledger (it only counts) and the level-50 kit (a launcher option).
    /// </summary>
    public SocialExpansionSettings AllOff() => this with
    {
        BotGuilds = false, Personalities = false, Voices = false, PlayerXpByLevel = "", BotXpByLevel = "",
        LivingMemory = false, LivingGrats = false, LivingFirsts = false, LivingDeath = false, LivingAlarms = false,
        LivingNemesis = false, ScheduleMode = "off", LivingBreaks = false, LivingBodyLanguage = false, LivingBeggars = false, LivingBuffBots = false,
        LivingDeeds = false, LivingStorylines = false, LivingCharacters = false, LivingRumours = false,
        LivingHolidays = false, LivingLeeroy = false, LivingAtmosphere = false, BotGroupInvites = false, BotRaidInvites = false,
        BotCrafters = false, BotCraftOrders = false,
        BotSkillProfiles = false, BotPlaceKnowledge = false, BotMovementHabits = false, LivingMilestones = false,
    };

    /// <summary>True when every feature and both XP ranges are off.</summary>
    public bool IsAllOff => !BotGuilds && !Personalities && !Voices && !AnyLiving && !BotGroupInvites && !BotRaidInvites &&
                            !BotCrafters && !BotCraftOrders && !AnyBotsAsPeople && PlayerXpByLevel.Trim() == "" && BotXpByLevel.Trim() == "";

    /// <summary>True when any "Bots as people" master switch is on.</summary>
    public bool AnyBotsAsPeople => BotSkillProfiles || BotPlaceKnowledge || BotMovementHabits || LivingMilestones;

    /// <summary>Why these settings can't be saved, or null when they can.</summary>
    public string? Validate()
    {
        if (GuildSharePercent is < 0 or > 100) return "The share of bots in guilds must be 0 to 100%.";
        if (GuildMinLevel is < 0 or > 50) return "The lowest guild level must be 0 to 50.";
        if (!ScheduleModes.Contains(ScheduleMode)) return "The bot schedule must be off, session or clock.";
        if (!JoinModes.Contains(JoinMode)) return "Joining a bot's group must be follow or lead.";
        if (PlayerLootWeight is < MinPlayerLootWeight or > MaxPlayerLootWeight)
            return $"Your loot share must be {MinPlayerLootWeight} to {MaxPlayerLootWeight} members.";
        if (double.IsNaN(ManaRegenModifier) || ManaRegenModifier < MinManaRegenModifier || ManaRegenModifier > MaxManaRegenModifier)
            return $"The power regeneration rate must be {MinManaRegenModifier.ToString(CultureInfo.InvariantCulture)} to {MaxManaRegenModifier.ToString(CultureInfo.InvariantCulture)}.";
        if (double.IsNaN(CraftingPace) || CraftingPace < MinCraftingPace || CraftingPace > MaxCraftingPace || CraftingPace != Math.Floor(CraftingPace))
            return $"The crafting pace must be a whole number of points from {MinCraftingPace} to {MaxCraftingPace}.";
        if (!UpkeepModes.Contains(PlayerUpkeep)) return "Repair upkeep must be classic, light or free.";
        if (BotCrafterSharePercent is < 0 or > 100) return "The share of crafters among new bots must be 0 to 100%.";
        if (!BotCraftingPaces.Contains(BotCraftingPace)) return "The bot crafting pace must be xp or classic.";
        if (!CommissionModes.Contains(RealmCommissions)) return "Realm commissions must be classic, doubled or off.";
        if (BotCrafterExchangeListings is < MinBotCrafterExchangeListings or > MaxBotCrafterExchangeListings)
            return $"A crafter's Exchange posts must be {MinBotCrafterExchangeListings} to {MaxBotCrafterExchangeListings}.";
        if (double.IsNaN(BotCrafterChatRate) || BotCrafterChatRate < 0 || BotCrafterChatRate > MaxBotCrafterChatRate)
            return "The crafters' chat rate must be 0 to 3.";
        if (ExchangeTaxPercent is < 0 or > MaxExchangeTaxPercent) return $"The Exchange tax must be 0 to {MaxExchangeTaxPercent}%.";
        if (BuffBotPercent is < 0 or > 100) return "The share of No Lifers with a buffbot must be 0 to 100%.";
        return XpError("Your XP by level", PlayerXpByLevel) ?? XpError("Bot XP by level", BotXpByLevel);
    }

    /// <summary>Why an XP-by-level setting is invalid (the server's own rules), or null when it is empty or valid.</summary>
    public static string? XpError(string label, string spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        return LevelRangeXpRates.TryParse(spec, out _, out string error) ? null : $"{label}: {error}.";
    }

    public static SocialExpansionSettings Load(string database, List<string> problems, ISet<string>? problemKeys = null)
    {
        if (!File.Exists(database)) throw new InvalidOperationException("The world database is missing: " + database);
        using var connection = Open(database, readOnly: true);
        return FromStoredValues(ReadStored(connection, null), problems, problemKeys);
    }

    /// <summary>
    /// Writes the settings that differ from <paramref name="previous"/> (all of them when it is null) and the keys in
    /// <paramref name="alsoWrite"/> (rows being repaired), in one transaction, then reads them back. Only while the
    /// server is stopped.
    /// </summary>
    public void Save(string database, SocialExpansionSettings? previous, Func<bool> serverStopped, IEnumerable<string>? alsoWrite = null)
    {
        if (Validate() is { } invalid) throw new InvalidOperationException(invalid);
        if (!serverStopped()) throw new InvalidOperationException("Stop the server first. Settings can only change while it is stopped.");
        if (!File.Exists(database)) throw new InvalidOperationException("The world database is missing: " + database);
        var values = ToStoredValues();
        var old = previous?.ToStoredValues();
        var repair = new HashSet<string>(alsoWrite ?? Array.Empty<string>(), StringComparer.Ordinal);
        var changed = values.Where(pair => old is null || old[pair.Key] != pair.Value || repair.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        if (changed.Count == 0) return;

        using var connection = Open(database, readOnly: false);
        using (var transaction = connection.BeginTransaction())
        {
            string updated = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            foreach (var (key, value) in changed)
            {
                var setting = Settings.Single(s => s.Key == key);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                // ServerProperty_ID is unique, so new rows use the key, as the server's own rows do. DefaultValue '' marks the
                // row as the player's choice, on an update too (from 1.2), so the server's settings upgrade
                // (LivingServerSettingsUpgrade) never moves it to a later changed default.
                command.CommandText = """
                    INSERT INTO ServerProperty (Category, `Key`, Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID)
                    VALUES (@category, @key, @description, '', @value, @updated, @key)
                    ON CONFLICT(`Key`) DO UPDATE SET Value=excluded.Value, DefaultValue='', LastTimeRowUpdated=excluded.LastTimeRowUpdated
                    """;
                command.Parameters.AddWithValue("@category", setting.Category);
                command.Parameters.AddWithValue("@key", key);
                command.Parameters.AddWithValue("@description", setting.Description);
                command.Parameters.AddWithValue("@value", value);
                command.Parameters.AddWithValue("@updated", updated);
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        var after = ReadStored(connection, changed.Keys);
        foreach (var (key, value) in changed)
        {
            if ((after.GetValueOrDefault(key) ?? "") != value)
                throw new InvalidOperationException($"{key} reads back as \"{after.GetValueOrDefault(key)}\", not \"{value}\".");
        }
    }

    private static SQLiteConnection Open(string database, bool readOnly)
    {
        var builder = new SQLiteConnectionStringBuilder { DataSource = database, Pooling = false, DefaultTimeout = 10, ReadOnly = readOnly };
        var connection = new SQLiteConnection(builder.ConnectionString);
        connection.Open();
        return connection;
    }

    private static Dictionary<string, string?> ReadStored(SQLiteConnection connection, IEnumerable<string>? keys)
    {
        var wanted = (keys ?? Settings.Select(s => s.Key)).ToArray();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT `Key`, Value FROM ServerProperty WHERE `Key` IN ({string.Join(",", wanted.Select((_, i) => "@k" + i))})";
        for (int i = 0; i < wanted.Length; i++) command.Parameters.AddWithValue("@k" + i, wanted[i]);
        var stored = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) stored[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return stored;
    }
}
