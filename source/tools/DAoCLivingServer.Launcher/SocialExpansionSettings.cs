using System.Data.SQLite;
using System.Globalization;
using DOL.GS;

namespace DAoCLivingServer.Launcher;

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
    /// <summary>off, session or clock (AutonomousBotSchedule). Off by default since Living Server 1.2.1 (owner 2026-11-21: "set the
    /// server settings so the 'bot schedule' based on time or sessions is off by default"); clock was the 1.2 default.</summary>
    public string ScheduleMode { get; init; } = "off";
    public bool LivingBreaks { get; init; } = true;
    public bool LivingBodyLanguage { get; init; } = true;
    /// <summary>A solo or duo gamebot sizes up a fight on sight and opens or backs off (bot_rvr_sight_size_up, #662). No checkbox:
    /// kept as read. On by default (1.2.3).</summary>
    public bool SightSizeUp { get; init; } = true;
    /// <summary>A gamebot group's fight board lists only the enemies its members know of, and fighting bots keep looking
    /// (rvr_board_from_contacts, #529 step 3). No checkbox: kept as read. On by default (1.2.3).</summary>
    public bool BoardFromContacts { get; init; } = true;
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
    /// <summary>Which Thanksgiving the holiday chatter marks: "us" (the default), "canada" or "off" (owner 2026-11-18).</summary>
    public string Thanksgiving { get; init; } = "us";
    /// <summary>Players may hire companions with /hire (hired_companions, from True 1.65). On by default.</summary>
    public bool HiredCompanions { get; init; } = true;
    public bool LivingLeeroy { get; init; } = true;
    /// <summary>The lore's screen-centre atmosphere lines (Living Server 1.2, docs/LORE.md). On by default.</summary>
    public bool LivingAtmosphere { get; init; } = true;
    /// <summary>Dead bots fall in the original client and come back standing when raised (bot_death_animation, 1.2.1). On by
    /// default. A display fix, not a feature: ALL OFF leaves it as it is.</summary>
    public bool DeathFall { get; init; } = true;
    /// <summary>Bot and companion healers heal like a 1.65 healer outside raids and boss fights (bot_healer_period_rules,
    /// #567 B1-B5). No checkbox: kept as read. On by default (1.2.3).</summary>
    public bool HealerPeriodRules { get; init; } = true;
    /// <summary>Bots fight with the weapon set a player would (bot_weapon_set_rules, #569 step 7). No checkbox: kept as
    /// read. On by default (1.2.3).</summary>
    public bool WeaponSetRules { get; init; } = true;
    /// <summary>Solo gamebots keep a hunting ground (bot_hunting_grounds, #570 step 3). No checkbox: kept as read. On by
    /// default (1.2.3).</summary>
    public bool HuntingGrounds { get; init; } = true;
    /// <summary>The damage dealers of the player's group fight the player's target first (bot_leader_focus, #571 step 1). No
    /// checkbox: kept as read. On by default (1.2.3).</summary>
    public bool LeaderFocus { get; init; } = true;
    /// <summary>The player's group engages enemies on its own in RvR (bot_companion_rvr_engage, #664 step 1). No checkbox:
    /// kept as read. On by default (1.2.3).</summary>
    public bool CompanionRvrEngage { get; init; } = true;
    /// <summary>The player's fight orders to his group: /order and the /group words (bot_group_orders, #667 step 1). No
    /// checkbox: kept as read. On by default (1.2.3).</summary>
    public bool GroupOrders { get; init; } = true;
    /// <summary>The player's group spreads into the period backline in an RvR fight (bot_companion_rvr_formation, #665). No
    /// checkbox: kept as read. On by default (1.2.3).</summary>
    public bool CompanionRvrFormation { get; init; } = true;
    /// <summary>Gamebot parties fight one kill target, the tank's (bot_party_main_assist, #569 step 6). No checkbox: kept as
    /// read. On by default (1.2.3).</summary>
    public bool PartyMainAssist { get; init; } = true;
    /// <summary>Archer bots open with Critical Shot and a Hunter's pet waits for it (bot_critical_shot_opener, #569 step 8).
    /// No checkbox: kept as read. On by default (1.2.3).</summary>
    public bool CriticalShotOpener { get; init; } = true;
    /// <summary>Gamebot party pullers pass over too-far monsters and keep with the party (bot_party_puller_camp_turn, #569
    /// step 9). No checkbox: kept as read. On by default (1.2.3).</summary>
    public bool PartyPullerCampTurn { get; init; } = true;
    /// <summary>Gamebots show as bots: [Playerbot] in /who and "gamebot"/"buffbot" in refused invites (bot_show_as_bots, 1.2.3). Off by
    /// default (owner 2026-11-24, #485: bots look like players). A display choice, not a feature: ALL OFF leaves it as it is.</summary>
    public bool ShowBotsAsBots { get; init; }
    public string PlayerXpByLevel { get; init; } = "";
    public string BotXpByLevel { get; init; } = "";
    /// <summary>
    /// Power regeneration multiplier for players and bots alike (Living Server 1.1.2: 1.5; 1 is the classic pre-1.87 rate,
    /// which patch 1.87 doubled).
    /// </summary>
    public double ManaRegenModifier { get; init; } = DefaultManaRegenModifier;
    /// <summary>The classic rule: list casters regenerate power at half speed below 50% power. Off by default since 1.1.2.</summary>
    public bool ManaHalvedBelowHalf { get; init; }
    /// <summary>
    /// Endurance regeneration multiplier for players and bots alike (1.2.3, from a r/daoc report; 1 is the classic rate).
    /// It multiplies every tick, Tireless and endurance songs included, and the server rounds a tick down, so below 1 standing
    /// regeneration would be nothing.
    /// </summary>
    public double EnduranceRegenModifier { get; init; } = DefaultEnduranceRegenModifier;
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
    /// <summary>Crafting skill points per gain, 1 (classic) to 10 (crafting_skill_rate).</summary>
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
    /// <summary>A new gamebot takes its lifetime build from the 2003 era builds at its first load (bot_era_builds, era builds 1.2
    /// WP3). On by default.</summary>
    public bool BotEraBuilds { get; init; } = true;
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
    /// <summary>Raids learn a boss's mechanic from the raiders who have seen it (raid_learns_from_raiders; under no master
    /// switch, no checkbox). On by default (1.2); ALL OFF turns it off with the rest, and turning Skill that grows back on
    /// in the tab turns it back on.</summary>
    public bool RaidLearnsFromRaiders { get; init; } = true;
    /// <summary>A Hibernian Bard may fill a bot party's Healer slot when no Druid or Warden is in reach, ordinary groups only,
    /// never realm raids (bot_bard_fills_healer_slot, #567 B7). No checkbox: kept as read. On by default (1.2.3).</summary>
    public bool BardFillsHealerSlot { get; init; } = true;
    /// <summary>Bots in the player's group fight Morgana's two demons when the player's Albion level 50 epic event starts, and
    /// never Morgana (bot_morgana_quest_help, #611). No checkbox: kept as read. On by default (1.2.3).</summary>
    public bool MorganaQuestHelp { get; init; } = true;
    /// <summary>The newcomer stream (#630, population_newcomers): new level-1 gamebots join a realm whose levels 1-19 run low.
    /// On by default.</summary>
    public bool PopulationNewcomers { get; init; } = true;
    /// <summary>Gamebots a realm keeps in levels 1-19 at a roster of 1,000, scaled (population_newcomer_floor), 0 to 1000.</summary>
    public int NewcomerFloor { get; init; } = DefaultNewcomerFloor;
    /// <summary>New level-1 gamebots an hour per realm at a roster of 1,000, scaled (population_newcomer_per_hour), 0 to 60.</summary>
    public int NewcomerPerHour { get; init; } = DefaultNewcomerPerHour;
    /// <summary>Each newcomer pairs with a gamebot at level 20 or above that retires, kept and never deleted (population_retire). On by default.</summary>
    public bool PopulationRetire { get; init; } = true;
    public const int DefaultNewcomerFloor = 75, MaxNewcomerFloor = 1000;
    public const int DefaultNewcomerPerHour = 10, MaxNewcomerPerHour = 60;

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

    /// <summary>The Thanksgiving choices the server knows (bot_living_holidays_thanksgiving).</summary>
    public static readonly string[] ThanksgivingModes = ["us", "canada", "off"];

    public const int DefaultPlayerLootWeight = 2, MinPlayerLootWeight = 1, MaxPlayerLootWeight = 8;

    public const double DefaultManaRegenModifier = 1.5, MinManaRegenModifier = 0.25, MaxManaRegenModifier = 10;

    public const double DefaultEnduranceRegenModifier = 1, MinEnduranceRegenModifier = 1, MaxEnduranceRegenModifier = 10;

    /// <summary>
    /// The server's defaults. Since Living Server 1.2 (owner 2026-11-16: "all our living server features should be in by
    /// default if someone downloads our installer") every feature and Server life part is on, with the bot schedule off (owner 2026-11-21);
    /// off stay the classic rules (power halving, secondary caps, classic quality and losses) and both XP ranges (rates, not
    /// switches: Setup's <see cref="Recommended"/> sets them).
    /// </summary>
    public static SocialExpansionSettings Defaults { get; } = new();

    /// <summary>
    /// What Setup turns on for a first install (owner 2026-10-28: "every setting turned on and to our suggested level"):
    /// every feature and Server life part on, the suggested XP ranges, and the bot schedule off (the whole roster online). The guild share and
    /// lowest level keep their defaults. Bot crafters and their craft orders are on too (owner 2026-11-16, replacing Crafting
    /// 2.0's earlier rule that every bot-side switch defaults off). Since 1.2 this is <see cref="Defaults"/> plus the XP ranges.
    /// </summary>
    public static SocialExpansionSettings Recommended { get; } = new()
    {
        BotGuilds = true, GuildSocial = true, GuildRecruitPlayer = true, GuildClaims = true, GuildEconomy = true, GuildAlliances = true,
        PlayerGuildEconomy = true, ManaRegenModifier = DefaultManaRegenModifier, ManaHalvedBelowHalf = false,
        EnduranceRegenModifier = DefaultEnduranceRegenModifier,
        Personalities = true, Voices = true, Debates = true,
        LivingMemory = true, LivingGrats = true, LivingFirsts = true, LivingDeath = true, LivingAlarms = true, LivingNemesis = true,
        ScheduleMode = "off", LivingBreaks = true, LivingBodyLanguage = true, LivingBeggars = true, LivingBuffBots = true, LivingDeeds = true,
        LivingStorylines = true, LivingCharacters = true, LivingRumours = true, LivingHolidays = true, Thanksgiving = "us", LivingLeeroy = true,
        LivingAtmosphere = true, HiredCompanions = true, PlayerXpByLevel = SuggestedPlayerXp, BotXpByLevel = SuggestedBotXp,
        BotGroupInvites = true, PlayerLootWeight = DefaultPlayerLootWeight, JoinMode = "follow", BotRaidInvites = true,
        BotCrafters = true, BotCraftOrders = true, BotEraClassShares = true, BotEraBuilds = true,
        BotSkillProfiles = true, BotPlaceKnowledge = true, BotMovementHabits = true, LivingMilestones = true,
        RaidLearnsFromRaiders = true, BardFillsHealerSlot = true, MorganaQuestHelp = true, DeathFall = true, HealerPeriodRules = true, WeaponSetRules = true, HuntingGrounds = true, LeaderFocus = true, CompanionRvrEngage = true, CompanionRvrFormation = true, GroupOrders = true, PartyMainAssist = true, CriticalShotOpener = true, PartyPullerCampTurn = true, ShowBotsAsBots = false,
        PopulationNewcomers = true, NewcomerFloor = DefaultNewcomerFloor, NewcomerPerHour = DefaultNewcomerPerHour, PopulationRetire = true,
        SightSizeUp = true, BoardFromContacts = true,
    };

    // Key, category and description of each setting, as the server and the Python helpers write them.
    internal static readonly (string Key, string Category, string Description)[] Settings =
    [
        ("bot_guilds_enabled", "guild", "Autonomous gamebots form and join realm guilds (see docs/BOT-GUILDS.md), and players can /ginvite them. False keeps every gamebot guildless; saved memberships are kept."),
        ("bot_guild_share", "guild", "Share (0 to 1) of eligible autonomous gamebots that bot guild formation places in guilds."),
        ("bot_guild_min_level", "guild", "Lowest level at which an autonomous gamebot is placed in a bot guild."),
        ("bot_guild_social", "guild", "When bot guilds are on: guild members greet, congratulate and chat in guild chat when a real member can hear, answer your /gu, and bot guild officers recruit in /faction."),
        ("bot_guild_recruit_player", "guild", "When bot guild social life is on: bot guild officers may invite you to their guild with a yes/no dialog (when you ask for a guild, or now and then when you are nearby and unguilded)."),
        ("bot_guild_claims_keeps", "guild", "When bot guilds are on: after a realm takes a keep, a bot guild with enough members inside claims it (claim_num; guilds_claim_limit), never while a real player of that realm is inside. Its members answer first when that keep is attacked."),
        ("bot_guild_economy", "guild", "When bot guilds are on: guilded gamebots pay their guild's dues from the real coin they loot, their level-ups and realm ranks earn the guild merit points as a player's do, and bot guild leaders spend 1,000 merit points on a 24-hour guild buff."),
        ("bot_player_guild_economy", "guild", "Gamebots you invite into your guild pay its dues (the rate its leader set) from the real coin they loot, and their level-ups and realm ranks earn it merit points as a player's do. Guild realm points, keep claims and guild buffs still never count a bot. Works whether bot guilds are on or off."),
        ("bot_guild_alliances", "guild", "When bot guilds are on: each realm's RvR and raiding bot guilds form one alliance (up to four guilds); a player in one of them hears its alliance chat. Alliances with a player's guild are never touched."),
        ("bot_personalities_enabled", "bots", "Autonomous gamebots get a personality (Leveler, Socialite, Daredevil, Explorer, Crafter, No Lifer) and traits, fixed from their id, that tilt town breaks, solo versus group goals, dungeon choice, camp choice and how often they talk (see docs/BOT-PERSONALITIES.md). Hard rules never change. False: no personalities."),
        ("bot_voices_enabled", "bots", "Autonomous gamebots say pre-written, period lines (docs/bot-voices/DESIGN.md) chosen by personality, level, realm, class and situation, each in its own writing style, without repeating a line in a realm for three days (six hours once a kind of line runs short). False keeps the old lines."),
        ("bot_voices_debates", "bots", "When bot voices are on: bots of a realm play the corpus's debates and long threads in /faction while you are online, one at a time, 30-45 minutes apart, never repeating a scene until all have played."),
        ("bot_voices_chat_rate", "bots", ChatRateDescription),
        ("bot_living_memory", "bots", "Server life: autonomous gamebots of your realm remember grouping with you, your rezzes and rude words; they greet you by name, thank you for an old rez, answer you by name, mention you in /faction now and then, and stay cool after insults. Chat only. False: bots do not remember you."),
        ("bot_living_grats", "bots", "Server life: when you or a bot gains a level, groupmates say grats in /group and bots nearby in /say, and a realm's bots cheer a new level 50 in /faction."),
        ("bot_living_firsts", "bots", "Server life: the first character on the server (you or a bot) to reach level 50, the first of each class to reach 50 and the first to each realm rank from 2 up are announced to everyone and recorded for the Camelot Herald."),
        ("bot_living_death", "bots", "Server life: a bot that dies near you asks you for a rez if your class can rez, waits longer for it and thanks you after; a bot healer that is not in your group may rez you (you still accept or decline); bots talk about corpse runs and resurrection sickness."),
        ("bot_living_alarms", "bots", "Server life: a bot in the frontier that sees 3 or more enemies calls it out in /faction (rough size, classes, place), at most one call per realm every 2 minutes."),
        ("bot_living_nemesis", "bots", "Server life: a bot killed by an enemy remembers its killer, names it (by race and class) in /faction after the second kill, and celebrates when it kills that enemy back. Kills are recorded for the Camelot Herald's Most Wanted."),
        ("bot_schedule_mode", "bots", "Server life: when autonomous gamebots are online. off (the default) = the whole roster at once; session = each bot plays sessions of 1-4 hours with 20 minutes to 2 hours away, timed from server start; clock = your PC's time of day (busiest 7-11pm and weekends, quiet overnight)."),
        ("bot_living_breaks", "bots", "Server life: an idle bot now and then says brb and logs off for 5-25 minutes, and a bot grouped with you rarely goes linkdead for 30-90 seconds (the group hears about it) and comes back with an apology. Never during a fight, siege or relic march."),
        ("bot_living_body_language", "bots", "Server life: autonomous gamebots standing around in town, waiting for their group or resting look around, emote now and then (ponder, shrug, laugh, stretch their legs with a step or two in town), turn and wave at you when you walk up, and cheer when someone dings nearby. Only where a real player can see it; never in a fight. Bots do not jump; sitting comes with Moving like people (bot_movement_habits)."),
        ("bot_rvr_sight_size_up", "bots", "A solo or duo gamebot sizes up a fight when enemies come into its perception range, in the frontier and the battlegrounds (keep assaults included), and opens or backs off (#662). Boldness moves the line; no solo bot ever opens on three or more enemies. Backing off, it pulls back out of range, waits nearby, calls its close friends, then goes back to what it was doing. Not companions, not PvE."),
        ("rvr_board_from_contacts", "bots", "RvR (1.2.3, #529): a gamebot group's fight board, which its called target, crowd control, size-up and retreat read, lists only the enemies one of its members noticed, was told of by a mate or was hit by in the last 15 s, instead of every enemy within 3,500 units whether anyone saw them or not (behind a hill, behind a wall, out of view). A gamebot in a fight looks around every 2 s (enemies within 2,500, a few line-of-sight checks, the usual notice rule) so a new enemy joining the fight is seen without hitting first. Groups with you in them, and companions, are unchanged. Log: RVR_FIGHT_BOARD (from), RVR_CONTACT_SHADOW (board), RVR_SCAN_BUDGET (combatRefreshes, combatRays). False: the board searches the area as before 1.2.3, and fighting bots don't look around."),
        ("bot_living_beggars", "bots", "Server life: about 4% of the Crafter bots are beggars. They live in their capital, beg you and passing bots for coin and loot (drag coin or an item onto one to give it), sell what they are given and hoard every copper. Ask anyone for gold or items and the beggars follow you around the city insulting you and defending their corners until you leave. Beggars never group or fight."),
        ("bot_nolifer_buffbots", "bots", "Server life: some No Lifer bots (bot_nolifer_buffbot_percent of them, 30 by default) run a second character, a level-50 buffbot (Albion Cleric, Midgard Shaman, Hibernia Druid, all in buffs). It follows its main where it is safe, parks at the nearest bind when the main goes into a dungeon, the frontier or a raid, and keeps the main buffed. It never joins a group, fights or loots, is not counted in the population, and logs in and out with its main. A bot is a No Lifer by its roster id, whether bot personalities are on or off."),
        ("bot_living_deeds", "bots", "Server life: bots of your realm that see you solo a named monster, go on a kill streak in the frontier, get zerged or die to something silly tell /faction a few minutes later, and others react. Deeds build a reputation the realm talks about, and a nickname."),
        ("bot_living_storylines", "bots", "Server life: pairs of bots live out dramas in /faction over days: a kill steal, a guild split, a romance, a ninja-loot trial. The same two bots play every episode; take a side in faction and the ending follows you."),
        ("bot_living_characters", "bots", "Server life: now and then a gold spammer floods /faction until it is banned, and a few bots roleplay in thee-and-thou speech while the realm mocks them."),
        ("bot_living_rumours", "bots", "Server life: bots pass on rumours that are true: a rare named monster that just spawned, or an enemy keep whose guards are down. Go and see."),
        ("bot_living_holidays", "bots", "Server life: around Halloween, Thanksgiving, Christmas, New Year and other holidays on your PC's calendar, bots talk about them like 2003 players did and answer your holiday wishes."),
        ("bot_living_holidays_thanksgiving", "bots", "Server life: which Thanksgiving the holiday chatter marks: us (the fourth Thursday of November, the default), canada (the second Monday of October) or off."),
        ("hired_companions", "bots", "Hired companions: players hire up to 7 persistent companions (slots open at levels 5, 10, 15, 20, 30, 40 and 45; the first is free) with /hire near a healer of their realm (every capital and large town has one). They level beside their player, keep their gear and coin, and have AI Options. /spawn and /raid are unchanged."),
        ("bot_living_leeroy", "bots", "Server life: one end-game raid in twenty has a bot who charges the final boss before the raid is ready, yelling his name, and the raid has to follow him in."),
        ("living_atmosphere_lines", "bots", "Atmosphere lines: short lines in the middle of the screen when you first enter a dungeon or epic zone that day, step into a boss's lair, a boss changes phase, a dragon wakes, a realm kills a boss for the first time or a raid forms. At most one every 30 seconds, and realm news always comes first."),
        ("bot_death_animation", "bots", "Bots fall when they die, in the original game client: a companion, hired companion or gamebot that dies plays the death fall and lies there as a monster's corpse does, until the client fades it. Raised, it stands up again; released, it appears at its bind. You can still rez it with nothing selected. The new client already shows the fall and gets nothing new."),
        ("bot_leader_focus", "bots", "Bots in your group fight your fight (#571): in PvE and RvR alike, when you attack a target (or cast harmful magic at it, or your pet fights it), the damage dealers among your /spawn and /raid helpers, hired companions, /bot companions and the gamebots grouped with you fight that target before whoever is hitting them or their own top threat, as a group assists its main assist. Tanks keep guarding and peeling, healers keep healing, a target a groupmate has mesmerized or rooted is left alone, a boss rule that forbids hitting a target still holds, and /defensive keeps its range. Autonomous gamebots outside your group are unchanged. Log: COMPANION_LEADER_FOCUS."),
        ("bot_companion_rvr_engage", "bots", "Your group engages enemies on its own in RvR (#664): in the frontier, the battlegrounds and Darkness Falls, the bots in your group (your /spawn and /raid helpers, hired companions, /bot companions and the gamebots grouped with you) notice enemy bots the way gamebots do (line of sight, sooner when the enemy fights, casts or is close, one of them seeing it is all of them seeing it, a stealther only close up) and attack them without waiting for you: within 2,000 of you on /aggressive and 1,000 on /defensive, healers and casters first. They size the fight up first and don't open on an enemy force they would clearly lose to, but always defend. Keep guards and lords are left alone unless you attack them, and so is an enemy standing under its tower or keep guards or in a safe area. They never open a fight on an enemy you or a groupmate holds with a mez or root. A bot set to passive, /bot stay or /bot hold stays out, and your own target still comes first. PvE and autonomous gamebots outside your group are unchanged. Log: COMPANION_RVR_ENGAGE."),
        ("bot_group_orders", "bots", "Order your group in a fight (#667): type /order attack, /order assist, /order hold or /order regroup (a macro works too), or say the word alone in /group. Your /spawn and /raid helpers, hired companions, /bot companions and the gamebots in a group you lead obey: attack goes for your selected target now, assist fights only your fight and switches when you switch, hold only defends itself (as /hold, until the fight ends), and regroup breaks off and comes back to you. An order lasts until your next order or the end of the fight. One of them answers in /group, and warns you when an attack in RvR looks like a fight you would lose, but goes in anyway. A bot on /bot hold, /bot stay or passive keeps to that. Autonomous gamebots outside your group are unchanged. Log: GROUP_ORDER."),
        ("bot_companion_rvr_formation", "bots", "Your group spreads into an attack formation in RvR (#665): in the frontier, the battlegrounds and Darkness Falls, once your group is fighting enemy bots, the casters and healers of your group (your /spawn and /raid helpers, hired companions, /bot companions and the gamebots grouped with you) stand behind the tanks relative to the enemy, more than 350 apart so one mez can't catch two of them, wider while the enemy is still coming in, and never more than 1,000 from you. Tanks and melee fight as before. Every step is a normal walk on solid ground. A healer moves only while nobody needs a heal, and nobody walks out of a cast. PvE and autonomous gamebots outside your group are unchanged. Log: COMPANION_RVR_FORMATION."),
        ("bot_party_main_assist", "bots", "Gamebot parties fight one kill target (#569): in a party of autonomous gamebots doing group PvE, the tank's target is the party's kill target and the other members hit it before whoever is hitting them or their own top threat. A member below half health that is being hit fights back, and a member hit by an enemy player fights that player at any health. A monster a groupmate has mesmerized or rooted, an enemy pet and a siege engine are never the kill target. It doesn't apply in realm raids (the raid's own calls win), under the RvR tactics, or with an enemy player in view. Groups with a real player in them keep leader focus (bot_leader_focus), and RvR warbands and companions are unchanged. Log: AUTONOMOUS_PARTY_KILL_TARGET."),
        ("bot_healer_period_rules", "bots", "Bot and companion healers heal like a 1.65 healer (#567): gamebots, your own companion healers and /spawn helpers. A dedicated healer starts at two-thirds health (65%, not 80%), heals the tank first, uses the small heal before the big one outside an emergency, puts a heal over time on the tank as a fight wears it down, and casts a group heal only with three or more hurt and not on the pull. Realm raids, warbands and boss fights keep their own healer rules."),
        ("bot_critical_shot_opener", "bots", "Archer bots open with Critical Shot (#569): a Hunter, Scout or Ranger gamebot, hired companion or /bot companion draws a Critical Shot as its first shot at a target that isn't fighting, running or mezzed, as players pulled, when it has the endurance, and a Hunter keeps its pet at heel for that shot (at most 7 seconds) instead of sending it in first. The draw time, damage and endurance are the player's. Your own character and pet are unchanged."),
        ("bot_party_puller_camp_turn", "bots", "Gamebot party pullers (#569): a monster too far from any spot to shoot it from is passed over for a nearer one instead of the tank walking into its pack, and after 45 seconds of only such monsters the party walks in together toward one (at most twice per camp in 10 minutes); a puller standing beside its leader at the camp starts pulling, and waits with the party while the camp is empty instead of wandering off. Outdoor gamebot parties only; your own group is unchanged. Log: AUTONOMOUS_PARTY_PULL_TOO_FAR, AUTONOMOUS_GROUP_CAMP_ADVANCED, AUTONOMOUS_PARTY_PULLER_ARRIVED_WITH_LEADER, AUTONOMOUS_PARTY_PULLER_EMPTY_CAMP_HOLD."),
        ("bot_weapon_set_rules", "bots", "Bots fight with the weapon set a player would (#569): a party's only tank with Guard fights with its trained shield and guards a member within reach, a bot carrying a two-hander and a one-hander uses the line it trained higher, a hired companion keeps the melee set you readied in its window, and archers stop flipping between bow and melee weapon. Only weapons the bot already carries are used. Gamebots, hired companions and /bot companions alike."),
        ("bot_hunting_grounds", "bots", "Solo gamebots keep a hunting ground (#570), as a player levelled through one zone and its neighbours: a solo gamebot's next camp is far more often in the zone it hunts or a neighbouring one, it hunts the area around a camp whose monster is gone instead of leaving, it pulls the monster standing apart first, the stealth assassins, healers, bards and archers prefer blue targets to yellow, and once most of its kills con green it moves up to a harder spot. Groups, raids, dungeons and Darkness Falls are unchanged; companions are not touched."),
        ("bot_show_as_bots", "bots", "Mark gamebots as bots: /who adds [Playerbot] to every gamebot, and a refused group invite says \"gamebot\" or \"buffbot\". Off by default (owner, 2026-11-24): bots look like players in /who and in system lines."),
        ("bot_group_invite_player", "bots", "Bot-led groups: the leader of a gamebot party of your realm and level hunting where you can walk or ride may send you a tell and the group invite box, at most once every 45 minutes unasked (never while you fight, ride, trade, are grouped, have /spawn helpers out, are /afk or in the frontier). Say yes and you follow: the leader picks the camp (at most 6 levels above you), the meeting spot and the pulls, and waits for you. False stops unasked invites; /lfg still gets you one."),
        ("bot_group_player_loot_weight", "bots", "In a group with you and gamebots, whoever leads: you count as this many members in the coin split and the item rotation (2: with seven bots nearby you get 2 of every 9 items and 2/9 of the coin), and an item your class can use that beats what you wear comes to you first. 1 to 8."),
        ("bot_led_group_join_mode", "bots", "Bot-led groups: when you say yes to a gamebot leader's invite. follow = the leader runs the group and you follow (/lead takes over at any time); lead = you become the leader at once and the bots follow you, as bots you /invite do (/lead bot hands it back)."),
        ("bot_raid_invite_player", "bots", "Raid leaders: when a dragon or epic dungeon raid forms in your realm and you are level 50, its raid leader (an officer of a raiding guild) may send you a tell and a box, once per raid. Yes puts you in a party that needs your class (one gamebot of your role steps out); you travel to the muster town yourself. A no is final for that raid. /invites off stops these too."),
        ("crafting_era_start", "crafting", "Era start: a new character joins a crafting order at a Master (1.65) instead of starting with Basic Crafting. Existing crafting rows are untouched."),
        ("crafting_secondary_caps", "crafting", "Secondary crafting skills capped at 75% (the order's own secondary) and 40% (the others) of the primary, as a 2002 player reported."),
        ("crafting_skill_rate", "crafting", "Player crafting pace: skill points per gain (1 = classic). A gain still needs the normal chance, so a grey recipe gives nothing."),
        ("player_upkeep", "crafting", "Player repair upkeep: classic (smith's price and durability), light (half price, no durability loss) or free."),
        ("player_coin_by_level", "crafting", "A real player's monster coin follows the XP-by-level multiplier of their band (bots' coin unchanged)."),
        ("crafting_classic_quality", "crafting", "Classic quality: 94-99% with a small masterpiece chance at every skill (off = the 1.87 rules, 96-99% and masterpieces only at 1000 or grey)."),
        ("crafting_classic_losses", "crafting", "Classic losses: a failed craft costs its materials and a spellcraft explosion destroys the item."),
        ("bot_crafters", "bots", "Master switch for gamebot crafters: Crafter-type gamebots join an order, buy real materials, work at real stations and sell what they make. Off: no gamebot crafts."),
        ("bot_crafter_share_percent", "bots", "Percent of NEW gamebots that are Crafters (existing bots keep their stored type)."),
        ("bot_crafting_pace", "bots", "Gamebot crafting pace: xp (points per gain follow the bot XP multiplier of the crafter's level band) or classic (one point). Needs bot_crafters."),
        ("bot_craft_orders", "bots", "Gamebots pay crafter gamebots of their realm to make an upgrade, with the coin held until delivery. Needs bot_crafters."),
        ("realm_commissions", "bots", "Realm quartermaster commissions to crafter gamebots: classic (30 an hour), doubled (60) or off. Needs bot_crafters."),
        ("bot_crafter_exchange_listings", "bots", "Realm Exchange posts a crafter gamebot may hold for its own work (other bots hold 3)."),
        ("bot_crafter_chat_rate", "bots", "Crafter gamebots' trade and station chat, as a multiple of normal (0 = silent)."),
        ("exchange_tax_percent", "bots", "Realm Exchange tax in percent of a sale, taken from the seller's proceeds (0 = none; 5 is the documented option). Only a trade pays it."),
        ("economy_ledger_enabled", "bots", "Hourly economy ledger: money supply, faucets and sinks per realm (ECON_SUPPLY, ECON_FLOWS). Logs and one table row an hour only; changes no amount."),
        ("launcher_level50_kit", "bots", "The launcher's level-50 bot batches arrive with the quality-100 kit and 10,000 gold (off for a fresh-start world)."),
        ("bot_era_builds", "bots", "Era builds: at its first load a new autonomous gamebot draws its lifetime spec build from its class's 2002-2003 builds (estimates from period forum posts; a Mind/Body Sorcerer is likelier than a Matter one), with up to 2 points of per-bot variation, and keeps it for life. A bot with a saved build keeps it; companions, hired companions, Epic Special Forces and buffbots keep the standard builds. Off: new bots get the standard builds too. Log: ERA_BUILD_ASSIGNED, ERA_BUILD_TRAINED, ERA_BUILD_CENSUS."),
        ("bot_era_class_shares", "bots", "Era builds: the launcher's bot batches draw each new gamebot's class from its realm's 2003 class share (estimates from 2002-2003 forum posts; Cleric 12% of Albion, Reaver 3%) instead of evenly. Existing bots keep their class."),
        ("population_newcomers", "bots", "Server life: when a realm's autonomous gamebots in levels 1-19 fall below population_newcomer_floor, new level-1 gamebots join it (made like the launcher's + Lv.1 batch: the realm's 2003 class mix, never the custom class, no armour, a Classic or Shrouded Isles start), at most population_newcomer_per_hour an hour. Not during the startup ramp. Log: POPULATION_NEWCOMER. False: only the launcher adds bots."),
        ("population_newcomer_floor", "bots", "The newcomer stream's floor: the gamebots a realm keeps in levels 1-19 at a roster of 1,000 in that realm, scaled to the realm's real roster (75 at 1,000 is 8 at 100 and 225 at 3,000; at least 1). 0 turns newcomers off."),
        ("population_newcomer_per_hour", "bots", "The newcomer stream's rate: at most this many new level-1 gamebots an hour per realm at a roster of 1,000 in that realm, scaled to the realm's roster (at least 1), spaced evenly. 0 turns newcomers off."),
        ("population_retire", "bots", "Server life: each newcomer is paired with a gamebot of its realm at level 20 or above (never below 20) that retires, the least-played and least-connected first, weighted toward the longest idle (fewer bot friends, no guild role, the least recent activity), so the realm's roster stays the same size. Never a bot guild leader, a bot in your guild, on your friends list, in a group or raid, trading, with an open craft order, busy or in your sight. A retired bot keeps its character, items and coin, is never deleted and never logs in again. Log: POPULATION_RETIRED. False: newcomers add to the roster."),
        ("bot_nolifer_buffbot_percent", "bots", "Percent (0 to 100) of the No Lifer bots that run a buffbot. Which No Lifers have one is fixed from each main's roster id, so the same mains keep theirs across restarts."),
        ("bot_nolifer_buffbot_era_conc_range", "bots", "No Lifers' buffbots follow the 2003 rule for concentration buffs: no range limit, so a buffbot parked at a bind keeps its main buffed (Mythic added the 5,000-unit limit with its Classic servers in July 2005). False: their concentration buffs switch off beyond this server's buff range like everyone else's, and come back in range."),
        ("bot_skill_profiles", "bots", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): every autonomous gamebot gets a skill tier (Casual, Average or Veteran, 30/50/20, shifted by rvr_bot_awareness) and six facets: reactions, positioning, pulls, standing in fire, buffs and power use. A Casual bot pulls an add now and then, lets a buff run out and reacts late; a Veteran rarely does, and no mistake takes a named or a monster more than 2 levels above the group. Never companions, raids, battleground assaults or fights at a keep. False turns bot_skill_growth and bot_skill_spare_player_group off with it. Log: BOT_SKILL_PROFILE, BOT_SKILL_BUDGET, BOT_SKILL_MISTAKE. Mistakes: Casual pulls an add in 10% of party pulls (Average 5%, Veteran 1.5%), lets 25% of expired buffs stay off 20-90 s (12%, 4%), casts its dearest nuke in 30% of casts (15%, 5%) and rests at 25% power (35%, 45%), circles into a monster's face in 12% of steps (6%, 2%), reacts x1.3 slower (x1.0, x0.75) and in open-field RvR steps out of an area effect after 2-5 s (1-3 s, 0.5-1.5 s; an RvR group keeps its group tier) and follows its group's call after a delay inside the group tier's band, sooner with better reactions."),
        ("bot_place_knowledge", "bots", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): a gamebot knows the places it has been, and at the start what a player of its level would: its capital, its home town, its realm's zones below its level (and their towns) and, from level 20, its border keep. Dungeons, the frontier and the next zones are new ground until it goes there; it learns a place on arrival. Saved per bot; /lswhy way lists them. On its own it changes no route and no camp: asking the way and wrong roads read it. False turns bot_ask_the_way and bot_wrong_roads off with it. Log: BOT_PLACE_LEARNED, BOT_PLACE_BUDGET."),
        ("bot_movement_habits", "bots", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): gamebots move like people: melee bots circle-strafe in open-field RvR, bot groups travel on /stick in a line, bots overshoot where they stop, sit when they wait, crowd the bank and sit on the walls of keeps their realm holds. Never in raids, battleground waves, fights at a keep, stable rides or a group with you. False turns bot_move_strafe, bot_move_stick_line and bot_move_sit_and_walls off with it. Log: BOT_MOVE_HABITS."),
        ("bot_living_milestones", "bots", "Server life, Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): autonomous gamebots chase a personal goal (the ding to 50 from level 45; at 50 realm rank 5 if they lean to RvR, or a dragon trophy, an item really won from a dragon or epic boss raid's loot, if they lean to raids) and say so as they get close, in their group, in /say near you, in their guild or in /faction, within the chat budgets; the realm cheers when one gets there, the Camelot Herald lists it under Milestones of the Realm, and the goal leans the bot's plans (fewer town breaks near 50, more RvR near realm rank 5). Log: BOT_MILESTONE_ARC, BOT_MILESTONE_PROGRESS, BOT_MILESTONE_TALK, BOT_MILESTONE_REACHED, BOT_MILESTONE_SUMMARY."),
        ("raid_learns_from_raiders", "bots", "Bots as people (1.2, owner decision 1, 2026-11-17): a realm raid's familiarity with a boss mechanic follows how many of the raiders present have seen it (full from 60% of the raid, a quarter with nobody), and each raider's own reactions follow its own count; Epic Special Forces have seen everything; companions and buffbots are not counted, and a raid with a real player in it (or a player's own group) keeps the record's familiarity. Wired for Xanxicar's stomp. A played world is seeded once at its first start with the switch (each level 50 of a realm with a counted attempt, with a chance of the record's familiarity but at least 80%), so its familiarity stays the same on average. False: the record's familiarity alone, as before. Log: RAID_LEARN, RAID_MECHANIC_SEEN."),
        ("bot_bard_fills_healer_slot", "bots", "Can a Hibernian Bard fill a bot party's Healer slot when no Druid or Warden is in reach, and heal as that party's healer while it holds the slot (#567 B7)? Ordinary groups only, never realm raids. Off: only the healing classes fill it."),
        ("bot_morgana_quest_help", "bots", "Bots in your group help with Morgana's quest fight (#611): when your Albion level 50 epic event starts at the fallen tower in Lyonesse, your companions and the gamebots grouped with you fight her two demons, Bechard and Silcharde. Bots never attack Morgana, and only a player on the quest starts the event. Off: bots join only once you or the demons start the fight. Log: MORGANA_QUEST_HELP."),
        ("xp_rate_by_level", "rates", "Optional XP rate by level range for real player characters, for example 1-19:3;20-34:5;35-50:12. Inside a listed range its rate replaces xp_rate; other levels keep xp_rate. In RvR zones the result is still multiplied by rvr_zones_xp_rate. Empty (the default) or an invalid value keeps the flat xp_rate."),
        ("bot_xp_rate_by_level", "rates", "Optional XP rate by level range for autonomous gamebots, for example 1-19:1.5;20-34:2.5;35-50:6. Inside a listed range its rate replaces bot_xp_rate; other levels keep bot_xp_rate. In RvR zones the result is still multiplied by rvr_zones_xp_rate, as bot_xp_rate is. Empty (the default) or an invalid value keeps the flat bot_xp_rate. Companion bots and real players are not affected."),
        ("mana_regen_amount_modifier", "rates", "Power regeneration multiplier for real players and gamebots alike. 1 is the classic rate before patch 1.87, which doubled it; 1.5 (the Living Server default) is a middle ground."),
        ("endurance_regen_amount_modifier", "rates", "Endurance regeneration multiplier for real players and gamebots alike, Tireless and endurance songs included. 1 is the classic rate; a tick is rounded down, so below 1 a standing character regenerates nothing."),
        ("mana_regen_amount_halved_below_50_percent", "rates", "Halve power regeneration below 50% power for list casters (the classic rule). Applies to real players and gamebots alike. Off by default in the Living Server; set in the launcher's Living Server tab."),
    ];

    /// <summary>The server's description of endurance_regen_amount_modifier (ServerProperties.ENDURANCE_REGEN_AMOUNT_MODIFIER).</summary>
    public const string EnduranceRegenDescription =
        "Endurance regeneration multiplier for real players and gamebots alike, Tireless and endurance songs included. 1 is the classic rate; a tick is rounded down, so below 1 a standing character regenerates nothing.";

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
        ["bot_rvr_sight_size_up"] = Bool(SightSizeUp),
        ["rvr_board_from_contacts"] = Bool(BoardFromContacts),
        ["bot_living_beggars"] = Bool(LivingBeggars),
        ["bot_nolifer_buffbots"] = Bool(LivingBuffBots),
        ["bot_living_deeds"] = Bool(LivingDeeds),
        ["bot_living_storylines"] = Bool(LivingStorylines),
        ["bot_living_characters"] = Bool(LivingCharacters),
        ["bot_living_rumours"] = Bool(LivingRumours),
        ["bot_living_holidays"] = Bool(LivingHolidays),
        ["bot_living_holidays_thanksgiving"] = Thanksgiving,
        ["hired_companions"] = Bool(HiredCompanions),
        ["bot_living_leeroy"] = Bool(LivingLeeroy),
        ["living_atmosphere_lines"] = Bool(LivingAtmosphere),
        ["xp_rate_by_level"] = PlayerXpByLevel.Trim(),
        ["bot_xp_rate_by_level"] = BotXpByLevel.Trim(),
        ["mana_regen_amount_modifier"] = ManaRegenModifier.ToString("0.##", CultureInfo.InvariantCulture),
        ["mana_regen_amount_halved_below_50_percent"] = Bool(ManaHalvedBelowHalf),
        ["endurance_regen_amount_modifier"] = EnduranceRegenModifier.ToString("0.##", CultureInfo.InvariantCulture),
        ["bot_death_animation"] = Bool(DeathFall),
        ["bot_healer_period_rules"] = Bool(HealerPeriodRules),
        ["bot_weapon_set_rules"] = Bool(WeaponSetRules),
        ["bot_hunting_grounds"] = Bool(HuntingGrounds),
        ["bot_leader_focus"] = Bool(LeaderFocus),
        ["bot_companion_rvr_engage"] = Bool(CompanionRvrEngage),
        ["bot_group_orders"] = Bool(GroupOrders),
        ["bot_companion_rvr_formation"] = Bool(CompanionRvrFormation),
        ["bot_party_main_assist"] = Bool(PartyMainAssist),
        ["bot_critical_shot_opener"] = Bool(CriticalShotOpener),
        ["bot_party_puller_camp_turn"] = Bool(PartyPullerCampTurn),
        ["bot_show_as_bots"] = Bool(ShowBotsAsBots),
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
        ["bot_era_builds"] = Bool(BotEraBuilds),
        ["bot_nolifer_buffbot_percent"] = BuffBotPercent.ToString(CultureInfo.InvariantCulture),
        ["bot_nolifer_buffbot_era_conc_range"] = Bool(BuffBotEraConcRange),
        ["bot_skill_profiles"] = Bool(BotSkillProfiles),
        ["bot_place_knowledge"] = Bool(BotPlaceKnowledge),
        ["bot_movement_habits"] = Bool(BotMovementHabits),
        ["bot_living_milestones"] = Bool(LivingMilestones),
        ["raid_learns_from_raiders"] = Bool(RaidLearnsFromRaiders),
        ["bot_bard_fills_healer_slot"] = Bool(BardFillsHealerSlot),
        ["bot_morgana_quest_help"] = Bool(MorganaQuestHelp),
        ["population_newcomers"] = Bool(PopulationNewcomers),
        ["population_newcomer_floor"] = NewcomerFloor.ToString(CultureInfo.InvariantCulture),
        ["population_newcomer_per_hour"] = NewcomerPerHour.ToString(CultureInfo.InvariantCulture),
        ["population_retire"] = Bool(PopulationRetire),
    };

    private static string Bool(bool value) => value ? "True" : "False";

    /// <summary>
    /// Settings from stored values (missing keys keep the server default). A stored value the server can't read
    /// shows what the server then uses (from 1.2.3 its default; before, its fields stayed at zero: False, 0), and a share
    /// above 1 shows as 100%;
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
            Problem(key, $"{key} = \"{text}\" is not True or False, so the server uses its default ({Bool(fallback)}).");
            return fallback;
        }
        // 1.2.3: endurance regeneration (a r/daoc report: none at all). An unreadable value is the server's default; a value
        // below 1 is rounded down to nothing for a standing character, so the tab flags it.
        double ReadRegenModifier(string key, string what, double current, double fallback, double min, double max)
        {
            if (!stored.TryGetValue(key, out string? text) || string.IsNullOrWhiteSpace(text)) return current;
            if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate) || double.IsNaN(rate))
            {
                Problem(key, $"{key} = \"{text}\" is not a number, so the server uses its default {fallback.ToString(CultureInfo.InvariantCulture)} (before 1.2.3: 0, no {what} regeneration).");
                return fallback;
            }
            if (rate < min || rate > max)
            {
                Problem(key, $"{key} = \"{text}\" is outside {min.ToString(CultureInfo.InvariantCulture)} to {max.ToString(CultureInfo.InvariantCulture)}" +
                             (rate < 1 ? $": below 1 a standing character regenerates no {what}." : "."));
                return Math.Clamp(rate, min, max);
            }
            return Math.Round(rate, 2, MidpointRounding.AwayFromZero);
        }
        int share = d.GuildSharePercent;
        if (stored.TryGetValue("bot_guild_share", out string? shareText) && !string.IsNullOrWhiteSpace(shareText))
        {
            if (!double.TryParse(shareText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double fraction))
            {
                Problem("bot_guild_share", $"bot_guild_share = \"{shareText}\" is not a number, so the server uses its default ({d.GuildSharePercent}%).");
                share = d.GuildSharePercent;
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
                Problem("bot_guild_min_level", $"bot_guild_min_level = \"{levelText}\" is not a whole number, so the server uses its default ({d.GuildMinLevel}).");
                minLevel = d.GuildMinLevel;
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
                Problem("mana_regen_amount_modifier", $"mana_regen_amount_modifier = \"{manaText}\" is not a number, so the server uses its default {DefaultManaRegenModifier.ToString(CultureInfo.InvariantCulture)} (before 1.2.3: 0, no power regeneration).");
                manaRate = DefaultManaRegenModifier;
            }
            else if (rate < MinManaRegenModifier || rate > MaxManaRegenModifier)
            {
                Problem("mana_regen_amount_modifier", $"mana_regen_amount_modifier = \"{manaText}\" is outside {MinManaRegenModifier.ToString(CultureInfo.InvariantCulture)} to {MaxManaRegenModifier.ToString(CultureInfo.InvariantCulture)}.");
                manaRate = Math.Clamp(rate, MinManaRegenModifier, MaxManaRegenModifier);
            }
            else
                manaRate = Math.Round(rate, 2, MidpointRounding.AwayFromZero);
        }
        double enduranceRate = ReadRegenModifier("endurance_regen_amount_modifier", "endurance", d.EnduranceRegenModifier,
            DefaultEnduranceRegenModifier, MinEnduranceRegenModifier, MaxEnduranceRegenModifier);
        double chatRate = d.ChatRate;
        if (stored.TryGetValue("bot_voices_chat_rate", out string? rateText) && !string.IsNullOrWhiteSpace(rateText))
        {
            if (!double.TryParse(rateText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double rate))
            {
                Problem("bot_voices_chat_rate", $"bot_voices_chat_rate = \"{rateText}\" is not a number, so the server uses its default ({d.ChatRate.ToString(CultureInfo.InvariantCulture)}).");
                chatRate = d.ChatRate;
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
                Problem("bot_group_player_loot_weight", $"bot_group_player_loot_weight = \"{weightText}\" is not a whole number, so the server uses its default ({d.PlayerLootWeight}).");
                lootWeight = d.PlayerLootWeight;
            }
            else if (weight < MinPlayerLootWeight || weight > MaxPlayerLootWeight)
            {
                Problem("bot_group_player_loot_weight", $"bot_group_player_loot_weight = \"{weightText}\" is outside {MinPlayerLootWeight} to {MaxPlayerLootWeight}.");
                lootWeight = Math.Clamp(weight, MinPlayerLootWeight, MaxPlayerLootWeight);
            }
            else
                lootWeight = weight;
        }
        string thanksgiving = d.Thanksgiving;
        if (stored.TryGetValue("bot_living_holidays_thanksgiving", out string? thanksText) && !string.IsNullOrWhiteSpace(thanksText))
        {
            thanksgiving = thanksText.Trim().ToLowerInvariant();
            if (!ThanksgivingModes.Contains(thanksgiving))
            {
                Problem("bot_living_holidays_thanksgiving", $"bot_living_holidays_thanksgiving = \"{thanksText}\" is not us, canada or off.");
                thanksgiving = d.Thanksgiving;
            }
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
                Problem("bot_nolifer_buffbot_percent", $"bot_nolifer_buffbot_percent = \"{buffText}\" is not a whole number, so the server uses its default ({d.BuffBotPercent}%).");
                buffBotPercent = d.BuffBotPercent;
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
            SightSizeUp = ReadBool("bot_rvr_sight_size_up", d.SightSizeUp),
            BoardFromContacts = ReadBool("rvr_board_from_contacts", d.BoardFromContacts),
            LivingBeggars = ReadBool("bot_living_beggars", d.LivingBeggars),
            LivingBuffBots = ReadBool("bot_nolifer_buffbots", d.LivingBuffBots),
            LivingDeeds = ReadBool("bot_living_deeds", d.LivingDeeds),
            LivingStorylines = ReadBool("bot_living_storylines", d.LivingStorylines),
            LivingCharacters = ReadBool("bot_living_characters", d.LivingCharacters),
            LivingRumours = ReadBool("bot_living_rumours", d.LivingRumours),
            LivingHolidays = ReadBool("bot_living_holidays", d.LivingHolidays),
            Thanksgiving = thanksgiving,
            HiredCompanions = ReadBool("hired_companions", d.HiredCompanions),
            LivingLeeroy = ReadBool("bot_living_leeroy", d.LivingLeeroy),
            LivingAtmosphere = ReadBool("living_atmosphere_lines", d.LivingAtmosphere),
            // Kept as written: an invalid range setting shows its own error in the tab (the server ignores it).
            PlayerXpByLevel = stored.TryGetValue("xp_rate_by_level", out string? player) ? player?.Trim() ?? "" : "",
            BotXpByLevel = stored.TryGetValue("bot_xp_rate_by_level", out string? bots) ? bots?.Trim() ?? "" : "",
            ManaRegenModifier = manaRate,
            ManaHalvedBelowHalf = ReadBool("mana_regen_amount_halved_below_50_percent", d.ManaHalvedBelowHalf),
            EnduranceRegenModifier = enduranceRate,
            DeathFall = ReadBool("bot_death_animation", d.DeathFall),
            HealerPeriodRules = ReadBool("bot_healer_period_rules", d.HealerPeriodRules),
            WeaponSetRules = ReadBool("bot_weapon_set_rules", d.WeaponSetRules),
            HuntingGrounds = ReadBool("bot_hunting_grounds", d.HuntingGrounds),
            LeaderFocus = ReadBool("bot_leader_focus", d.LeaderFocus),
            CompanionRvrEngage = ReadBool("bot_companion_rvr_engage", d.CompanionRvrEngage),
            GroupOrders = ReadBool("bot_group_orders", d.GroupOrders),
            CompanionRvrFormation = ReadBool("bot_companion_rvr_formation", d.CompanionRvrFormation),
            PartyMainAssist = ReadBool("bot_party_main_assist", d.PartyMainAssist),
            CriticalShotOpener = ReadBool("bot_critical_shot_opener", d.CriticalShotOpener),
            PartyPullerCampTurn = ReadBool("bot_party_puller_camp_turn", d.PartyPullerCampTurn),
            ShowBotsAsBots = ReadBool("bot_show_as_bots", d.ShowBotsAsBots),
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
            BotEraBuilds = ReadCraftingBool("bot_era_builds", d.BotEraBuilds),
            BuffBotPercent = buffBotPercent,
            BuffBotEraConcRange = ReadBool("bot_nolifer_buffbot_era_conc_range", d.BuffBotEraConcRange),
            BotSkillProfiles = ReadBool("bot_skill_profiles", d.BotSkillProfiles),
            BotPlaceKnowledge = ReadBool("bot_place_knowledge", d.BotPlaceKnowledge),
            BotMovementHabits = ReadBool("bot_movement_habits", d.BotMovementHabits),
            LivingMilestones = ReadBool("bot_living_milestones", d.LivingMilestones),
            RaidLearnsFromRaiders = ReadBool("raid_learns_from_raiders", d.RaidLearnsFromRaiders),
            BardFillsHealerSlot = ReadBool("bot_bard_fills_healer_slot", d.BardFillsHealerSlot),
            MorganaQuestHelp = ReadBool("bot_morgana_quest_help", d.MorganaQuestHelp),
            PopulationNewcomers = ReadBool("population_newcomers", d.PopulationNewcomers),
            NewcomerFloor = ReadCraftingInt("population_newcomer_floor", d.NewcomerFloor, 0, MaxNewcomerFloor),
            NewcomerPerHour = ReadCraftingInt("population_newcomer_per_hour", d.NewcomerPerHour, 0, MaxNewcomerPerHour),
            PopulationRetire = ReadBool("population_retire", d.PopulationRetire),
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
    /// four "Bots as people" master switches and raids learning from their raiders (ALL OFF is the classic server), the era builds (the 2003 class mix and builds of new gamebots; bots that have
    /// theirs keep them), and both XP ranges off. The share, lowest level and each part keep their values, so turning a feature back on later restores
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
        LivingHolidays = false, LivingLeeroy = false, LivingAtmosphere = false, HiredCompanions = false, BotGroupInvites = false, BotRaidInvites = false,
        BotCrafters = false, BotCraftOrders = false,
        BotSkillProfiles = false, BotPlaceKnowledge = false, BotMovementHabits = false, LivingMilestones = false,
        RaidLearnsFromRaiders = false, BotEraClassShares = false, BotEraBuilds = false,
        PopulationNewcomers = false, PopulationRetire = false,
    };

    /// <summary>True when every feature and both XP ranges are off.</summary>
    public bool IsAllOff => !BotGuilds && !Personalities && !Voices && !AnyLiving && !HiredCompanions && !BotGroupInvites && !BotRaidInvites &&
                            !BotCrafters && !BotCraftOrders && !AnyBotsAsPeople && !RaidLearnsFromRaiders && !BotEraClassShares && !BotEraBuilds &&
                            !PopulationNewcomers && !PopulationRetire &&
                            PlayerXpByLevel.Trim() == "" && BotXpByLevel.Trim() == "";

    /// <summary>True when any "Bots as people" master switch is on.</summary>
    public bool AnyBotsAsPeople => BotSkillProfiles || BotPlaceKnowledge || BotMovementHabits || LivingMilestones;

    /// <summary>Why these settings can't be saved, or null when they can.</summary>
    public string? Validate()
    {
        if (GuildSharePercent is < 0 or > 100) return "The share of bots in guilds must be 0 to 100%.";
        if (GuildMinLevel is < 0 or > 50) return "The lowest guild level must be 0 to 50.";
        if (!ScheduleModes.Contains(ScheduleMode)) return "The bot schedule must be off, session or clock.";
        if (!JoinModes.Contains(JoinMode)) return "Joining a bot's group must be follow or lead.";
        if (!ThanksgivingModes.Contains(Thanksgiving)) return "Thanksgiving must be us, canada or off.";
        if (PlayerLootWeight is < MinPlayerLootWeight or > MaxPlayerLootWeight)
            return $"Your loot share must be {MinPlayerLootWeight} to {MaxPlayerLootWeight} members.";
        if (double.IsNaN(ManaRegenModifier) || ManaRegenModifier < MinManaRegenModifier || ManaRegenModifier > MaxManaRegenModifier)
            return $"The power regeneration rate must be {MinManaRegenModifier.ToString(CultureInfo.InvariantCulture)} to {MaxManaRegenModifier.ToString(CultureInfo.InvariantCulture)}.";
        if (double.IsNaN(EnduranceRegenModifier) || EnduranceRegenModifier < MinEnduranceRegenModifier || EnduranceRegenModifier > MaxEnduranceRegenModifier)
            return $"The endurance regeneration rate must be {MinEnduranceRegenModifier.ToString(CultureInfo.InvariantCulture)} to {MaxEnduranceRegenModifier.ToString(CultureInfo.InvariantCulture)}.";
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
