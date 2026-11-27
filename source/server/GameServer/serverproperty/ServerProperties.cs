using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DOL.Database;

namespace DOL.GS.ServerProperties
{
	/// <summary>
	/// The abstract ServerProperty class that also defines the
	/// static Init and Load methods for other properties that inherit
	/// </summary>
	public abstract class Properties
	{
		/// <summary>
		/// Defines a logger for this class.
		/// </summary>
		private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// Init the properties
		/// </summary>
		public static void InitProperties()
		{
			var propDict = AllDomainProperties;
			foreach (var prop in propDict)
			{
				Load(prop.Value.Item1, prop.Value.Item2, prop.Value.Item3);
			}

			// Living Server: new defaults reach worlds that ran an earlier build (once).
			LivingServerSettingsUpgrade.Apply(propDict);
			
			// Refresh static dict values for display
			AllCurrentProperties = propDict.ToDictionary(k => k.Key, v => v.Value.Item2.GetValue(null));
		}

		// Properties: feel free to improve the SPs in the
		// categories below, or extend the category list.

		#region SYSTEM / DEBUG
		/// <summary>
		/// TempProperties to register
		/// </summary>
		[ServerProperty("system", "tempproperties_to_register", "Serialized list of tempprop string, separated by semi-colon for be registered when a player disconnect", "LastPotionItemUsedTick;SpellAvailableTime;ItemUseDelay;LastChargedItemUsedTick")]
		public static string TEMPPROPERTIES_TO_REGISTER;
		/// <summary>
		/// Do we activate TempProperties manager Checkup on log in
		/// </summary>
		[ServerProperty("system", "activate_temp_properties_manager_checkup", "Do we activate TempProperties manager Checkup on log in?", true)]
		public static bool ACTIVATE_TEMP_PROPERTIES_MANAGER_CHECKUP;
		/// <summary>
		/// Do we activate TempProperties manager Checkup debug
		/// </summary>
		[ServerProperty("log", "activate_temp_properties_manager_checkup_debug", "Do we activate TempProperties manager Checkup debug?", false)]
		public static bool ACTIVATE_TEMP_PROPERTIES_MANAGER_CHECKUP_DEBUG;
		/// <summary>
		/// Enable Debug mode - used to alter some features during server startup to make debugging easier
		/// Can be changed while server is running but may require restart to enable all debug features
		/// </summary>
		[ServerProperty("system", "enable_debug", "Enable Debug mode? Used to alter some features during server startup to make debugging easier", false)]
		public static bool ENABLE_DEBUG;

		/// <summary>
		/// Use raw RNG instead of Deck of Cards
		/// </summary>
		[ServerProperty("system", "override_deck_rng", "Should we use raw RNG instead of Deck-Of-Cards normalization?", false)]
		public static bool OVERRIDE_DECK_RNG;

		/// <summary>
		/// Maximum length for reward quest description text to prevent client crashes
		/// </summary>
		[ServerProperty("system", "max_rewardquest_description_length", "Maximum length for reward quest description text to prevent client crashes.", 1000)]
		public static int MAX_REWARDQUEST_DESCRIPTION_LENGTH;

		/// <summary>
		/// If the server should only accept connections from staff
		/// </summary>
		[ServerProperty("system", "staff_login", "Staff Login Only - Edit this to set weather you wish staff to be the only ones allowed to Log in values True,False", false)]
		public static bool STAFF_LOGIN;

		/// <summary>
		/// The minimum client version required to connect
		/// </summary>
		[ServerProperty("system", "client_version_min", "Minimum Client Version - Edit this to change which client version at the least have to be used: -1 = any, 1.80 = 180", -1)]
		public static int CLIENT_VERSION_MIN;

		/// <summary>
		/// What is the maximum client type allowed to connect
		/// </summary>
		[ServerProperty("system", "client_type_max", "What is the maximum client type allowed to connect", -1)]
		public static int CLIENT_TYPE_MAX;

		/// <summary>
		/// The maximum client version required to connect
		/// </summary>
		[ServerProperty("system", "client_version_max", "Maximum Client Version - Edit this to change which client version at the most have to be used: -1 = any, 1.80 = 180", -1)]
		public static int CLIENT_VERSION_MAX;

		/// <summary>
		/// Enable RC4 encryption for communication between the client and the server
		/// </summary>
		[ServerProperty("system", "client_enable_encryption_rc4", "Enable client RC4 encryption - Advanced user only, you need a special launcher to enable encryption", false)]
		public static bool CLIENT_ENABLE_ENCRYPTION_RC4;

		/// <summary>
		/// Should the server load quests
		/// </summary>
		[ServerProperty("system", "load_quests", "Should the server load quests, values True,False", true)]
		public static bool LOAD_QUESTS;

		/// <summary>
		/// Should the server load Buff Tokens
		/// </summary>
		[ServerProperty("system", "load_buff_tokens", "Should the server load buff tokens (npc and items), values True,False", true)]
		public static bool LOAD_BUFF_TOKENS;

		/// <summary>
		/// Should the server load Arrow Summoning items
		/// </summary>
		[ServerProperty("system", "load_arrow_summoning", "Should the server load Arrow Summoning items, values True,False", true)]
		public static bool LOAD_ARROW_SUMMONING;

		/// <summary>
		/// Should the server load Housing items
		/// </summary>
		[ServerProperty("system", "load_housing_items", "Should the server load Housing items, values True,False", true)]
		public static bool LOAD_HOUSING_ITEMS;

		/// <summary>
		/// Should the server load Housing NPC
		/// </summary>
		[ServerProperty("system", "load_housing_npc", "Should the server load Housing npc, values True,False", true)]
		public static bool LOAD_HOUSING_NPC;

		/// <summary>
		/// Disable Bug Reports
		/// </summary>
		[ServerProperty("system", "disable_bug_reports", "Set to true to disable bug reporting, and false to enable bug reporting", true)]
		public static bool DISABLE_BUG_REPORTS;
		/// <summary>
		/// Max bug repots in Queue
		/// </summary>
		[ServerProperty("system", "max_bugreport_queue", "Maximum number of bug reports allowed in queue.  0 to disabled max check", 100)]
		public static int MAX_BUGREPORT_QUEUE;

		/// <summary>
		/// The max number of players on the server
		/// </summary>
		[ServerProperty("system", "max_players", "Max Players - Edit this to set the maximum players allowed to connect at the same time", 4000)]
		public static int MAX_PLAYERS;

		/// <summary>
		/// What class should the server use for players
		/// </summary>
		[ServerProperty("system", "player_class", "What class should the server use for players", "DOL.GS.GamePlayer")]
		public static string PLAYER_CLASS;

		/// <summary>
		/// A serialised list of RegionIDs that will load objects
		/// </summary>
		[ServerProperty("system", "debug_load_regions", "Serialized list of region IDs that will load objects, separated by semi-colon (leave this blank to load all regions normally)", "")]
		public static string DEBUG_LOAD_REGIONS;

		/// <summary>
		/// A serialised list of disabled expansion IDs
		/// </summary>
		[ServerProperty("system", "disabled_expansions", "Serialized list of disabled expansions IDs, expansion IDs are client type seperated by ;", "")]
		public static string DISABLED_EXPANSIONS = string.Empty;

		/// <summary>
		/// Server Language
		/// </summary>
		[ServerProperty("system", "server_language", "Language of your server. It can be EN, FR or DE.", "EN")]
		public static string SERV_LANGUAGE;

		/// <summary>
		/// allow_change_language
		/// </summary>
		[ServerProperty("system", "allow_change_language", "Should we allow clients to change their language ?", false)]
		public static bool ALLOW_CHANGE_LANGUAGE;

		/// <summary>
		/// Allow player to change their character face after creation through customizing ?
		/// </summary>
		[ServerProperty("system", "allow_customize_face_after_creation", "Allow player to change their character face after creation through customizing ?", true)]
		public static bool ALLOW_CUSTOMIZE_FACE_AFTER_CREATION;

		/// <summary>
		/// Allow player to change their character starting stats after creation through customizing ?
		/// </summary>
		[ServerProperty("system", "allow_customize_stats_after_creation", "Allow player to change their character starting stats after creation through customizing ?", true)]
		public static bool ALLOW_CUSTOMIZE_STATS_AFTER_CREATION;

		/// <summary>
		/// StatSave Interval
		/// </summary>
		[ServerProperty("system", "statsave_interval", "Interval (in minutes) at which performance statistics are saved to the database. 0 or less to deactivate.", 0)]
		public static int STATSAVE_INTERVAL;

		/// <summary>
		/// Bug Report Email Addresses
		/// </summary>
		[ServerProperty("system", "bug_report_email_addresses", "set to the email addresses you want bug reports sent to (bug reports will only send if the user has set an email address for his account, multiple addresses seperate with ;", "")]
		public static string BUG_REPORT_EMAIL_ADDRESSES;

		/// <summary>
		/// Ban Hackers
		/// </summary>
		[ServerProperty("system", "ban_hackers", "Should we ban hackers, if set to true, bans will be done, if set to false, kicks will be done", false)]
		public static bool BAN_HACKERS;

		/// <summary>
		/// Is the database translated
		/// </summary>
		[ServerProperty("system", "db_language", "What language is the DB", "EN")]
		public static string DB_LANGUAGE;

		[ServerProperty("system", "statprint_frequency", "Interval (in milliseconds) at which performance statistics are gathered and print to the server console. 0 or less to deactivate.", 30000)]
		public static int STATPRINT_FREQUENCY;

		/// <summary>
		/// Should users be able to create characters in all realms using the same account
		/// </summary>
		[ServerProperty("system", "allow_all_realms", "should we allow characters to be created on all realms using a single account", false)]
		public static bool ALLOW_ALL_REALMS;

		/// <summary>
		/// This will Allow/Disallow dual loggins
		/// </summary>
		[ServerProperty("system", "allow_dual_logins", "Disable to disallow players to connect with more than 1 account at a time.", true)]
		public static bool ALLOW_DUAL_LOGINS;

		/// <summary>
		/// The emote delay
		/// </summary>
		[ServerProperty("system", "emote_delay", "The emote delay, default 3000ms before another emote.", 3000)]
		public static int EMOTE_DELAY;

		/// <summary>
		/// Command spam protection delay
		/// </summary>
		[ServerProperty("system", "command_spam_delay", "The spam delay, default 1500ms before the same command can be issued again.", 1500)]
		public static int COMMAND_SPAM_DELAY;

		/// <summary>
		/// This specifies the max number of inventory items to send in an update packet
		/// </summary>
		[ServerProperty("system", "max_items_per_packet", "Max number of inventory items sent per packet.", 30)]
		public static int MAX_ITEMS_PER_PACKET;

		/// <summary>
		/// Display centered screen messages if a player enters an area.
		/// </summary>
		[ServerProperty("system", "display_area_enter_screen_desc", "Display centered screen messages if a player enters an area.", false)]
		public static bool DISPLAY_AREA_ENTER_SCREEN_DESC;

		/// <summary>
		/// Whether or not to enable the audit log
		/// </summary>
		[ServerProperty("system", "enable_audit_log", "Whether or not to enable the audit log", false)]
		public static bool ENABLE_AUDIT_LOG;
		
		/// <summary>
		/// Enable a periodic server shutdown. If you run your server into a batch loop, this performs a restart.
		/// </summary>
		[ServerProperty("system", "hours_uptime_between_shutdown", "Hours between a scheduled server shutdown (-1 = no scheduled restart)", -1)]
		public static int HOURS_UPTIME_BETWEEN_SHUTDOWN;

		/// <summary>
		/// Use the NPC Guild Scripts
		/// </summary>
		[ServerProperty("system", "use_npcguildscripts", "Use the NPC Guild Scripts", true)]
		public static bool USE_NPCGUILDSCRIPTS;

		[ServerProperty("system", "game_loop_tick_rate", "How many ticks per second the game loop tries to run at. If it can't keep up, the logic will effectively run slower than intended.", 30)]
		public static int GAME_LOOP_TICK_RATE;

		#endregion

		#region LOGGING

		/// <summary>
		/// Log All GM commands
		/// </summary>
		[ServerProperty("system", "log_all_gm_commands", "Log all GM commands on the server", false)]
		public static bool LOG_ALL_GM_COMMANDS;

		/// <summary>
		/// Should the server Log trades
		/// </summary>
		[ServerProperty("system", "log_trades", "Should the server Log all trades a player makes, values True,False", false)]
		public static bool LOG_TRADES;

		/// <summary>
		/// Log Email Addresses
		/// </summary>
		[ServerProperty("system", "log_email_addresses", "set to the email addresses you want logs automatically emailed to, multiple addresses seperate with ;", "")]
		public static string LOG_EMAIL_ADDRESSES;

		/// <summary>
		/// Enable inventory logging (trade, loot, buy, sell, quests,...)
		/// </summary>
		[ServerProperty("log", "log_inventory", "Enable inventory logging (trade, loot, buy, sell, quests,...)", false)]
		public static bool LOG_INVENTORY;

		/// <summary>
		/// Enable trade logging in inventory log (log_inventory must be enabled)
		/// </summary>
		[ServerProperty("log", "log_inventory_trade", "Enable trade logging in inventory log (log_inventory must be enabled)", true)]
		public static bool LOG_INVENTORY_TRADE;

		/// <summary>
		/// Enable craft logging in inventory log (log_inventory must be enabled)
		/// </summary>
		[ServerProperty("log", "log_inventory_craft", "Enable craft logging in inventory log (log_inventory must be enabled)", true)]
		public static bool LOG_INVENTORY_CRAFT;

		/// <summary>
		/// Enable loot logging in inventory log (log_inventory must be enabled)
		/// </summary>
		[ServerProperty("log", "log_inventory_loot", "Enable loot logging in inventory log (log_inventory must be enabled)", true)]
		public static bool LOG_INVENTORY_LOOT;

		/// <summary>
		/// Enable quest logging in inventory log (log_inventory must be enabled)
		/// </summary>
		[ServerProperty("log", "log_inventory_quest", "Enable quest logging in inventory log (log_inventory must be enabled)", true)]
		public static bool LOG_INVENTORY_QUEST;

		/// <summary>
		/// Enable merchant logging in inventory log (log_inventory must be enabled)
		/// </summary>
		[ServerProperty("log", "log_inventory_merchant", "Enable merchant logging in inventory log (log_inventory must be enabled)", true)]
		public static bool LOG_INVENTORY_MERCHANT;

		/// <summary>
		/// Enable other logging in inventory log (log_inventory must be enabled)
		/// </summary>
		[ServerProperty("log", "log_inventory_other", "Enable other logging in inventory log (log_inventory must be enabled)", true)]
		public static bool LOG_INVENTORY_OTHER;
		#endregion

		#region SERVER

		/// <summary>
		/// Enable/Disable Autokick Timer
		/// </summary>
		[ServerProperty("server", "player idle kick", "Enable auto kick for inactive players", false)]
		public static bool KICK_IDLE_PLAYER_STATUS;

		/// <summary>
		/// How long before kicking inactive player
		/// </summary>
		[ServerProperty("server", "minutes to kick", "How many minutes before kicking inactive player to char screen <Default 1hr> ", 60)]
		public static int KICK_IDLE_PLAYER_TIME;

		/// <summary>
		/// Disable quit timers for players?
		/// </summary>
		[ServerProperty("server", "disable_quit_timer", "Allow players to log out without waiting?", false)]
		public static bool DISABLE_QUIT_TIMER;

		/// <summary>
		/// Queue Service Host
		/// </summary>
		[ServerProperty("server", "queue_api_url", "Provide the URL for the queue service endpoint - blank to disable", "")]
		public static string QUEUE_API_URI;

		/// <summary>
		/// Enable Discord Webhook?
		/// </summary>
		[ServerProperty("server", "Discord_Webhook_Active", "Enable Discord webhook?", false)]
		public static bool DISCORD_ACTIVE;

		/// <summary>
		/// Webhook ID
		/// </summary>
		[ServerProperty("server", "Discord_Webhook_ID", "The id of the webhook", "")]
		public static string DISCORD_WEBHOOK_ID;
		
		/// <summary>
		/// RvRWebhook ID
		/// </summary>
		[ServerProperty("atlas", "Discord_RVR_Webhook_ID", "The id of the webhook for RvR updates", "")]
		public static string DISCORD_RVR_WEBHOOK_ID;

		/// <summary>
		/// RvRWebhook ID
		/// </summary>
		[ServerProperty("atlas", "Discord_AlbChat_Webhook_ID", "The id of the webhook for all Albion chat", "")]
		public static string DISCORD_ALBCHAT_WEBHOOK_ID;

		/// <summary>
		/// RvRWebhook ID
		/// </summary>
		[ServerProperty("atlas", "Discord_HibChat_Webhook_ID", "The id of the webhook for Hibernia chat", "")]
		public static string DISCORD_HIBCHAT_WEBHOOK_ID;

		/// <summary>
		/// RvRWebhook ID
		/// </summary>
		[ServerProperty("atlas", "Discord_MidChat_Webhook_ID", "The id of the webhook for Midgard chat", "")]
		public static string DISCORD_MIDCHAT_WEBHOOK_ID;

		/// <summary>
		/// Tester Role
		/// </summary>
		[ServerProperty("atlas", "tester_login", "Allow only testers and staff to login", false)]
		public static bool TESTER_LOGIN;

		/// <summary>
		/// The slowmode duration for /advice in seconds
		/// </summary>
		[ServerProperty("atlas", "advice_slowmode_length", "The slowmode duration for /advice in seconds", 60)]
		public static int ADVICE_SLOWMODE_LENGTH;

		/// <summary>
		/// The slowmode duration for /trade in seconds
		/// </summary>
		[ServerProperty("atlas", "trade_slowmode_length", "The slowmode duration for /trade in seconds", 60)]
		public static int TRADE_SLOWMODE_LENGTH;

		/// <summary>
		/// The slowmode duration for /lfg in seconds
		/// </summary>
		[ServerProperty("atlas", "lfg_slowmode_length", "The slowmode duration for /lfg in seconds", 60)]
		public static int LFG_SLOWMODE_LENGTH;

		/// <summary>
		/// The first factor in the PVE mob damage equation. Lower hits harder.
		/// </summary>
		[ServerProperty("atlas", "pve_mob_damage_f1", "The first factor in the PVE mob damage equation. Lower hits harder.", 3.2)]
		public static double PVE_MOB_DAMAGE_F1;

		/// <summary>
		/// The second factor in the PVE mob damage equation. Lower hits harder.
		/// </summary>
		[ServerProperty("atlas", "pve_mob_damage_f2", "The second factor in the PVE mob damage equation. Lower hits harder.", 150.0)]
		public static double PVE_MOB_DAMAGE_F2;

		/// <summary>
		/// Enable integrated serverlistupdate script?
		/// </summary>
		[ServerProperty("server", "serverlistupdate_enabled", "Enable in-built serverlistupdate script?", false)]
		public static bool SERVERLISTUPDATE_ENABLED;

		/// <summary>
		/// The username for server list update.
		/// </summary>
		[ServerProperty("server", "serverlistupdate_user", "Username for serverlistupdate.", "")]
		public static string SERVER_LIST_UPDATE_USER;

		/// <summary>
		/// The password for server list update.
		/// </summary>
		[ServerProperty("server", "serverlistupdate_password", "Password for serverlistupdate.", "")]
		public static string SERVER_LIST_UPDATE_PASS;

		/// <summary>
		/// Post 1.108 Passive RA 9-Tiers
		/// </summary>
		[ServerProperty("server", "use_new_passives_ras_scaling", "Use new passives realmabilities scaling (1.108+) ?", false)]
		public static bool USE_NEW_PASSIVES_RAS_SCALING;

		/// <summary>
		/// Post 1.108 Active RA 5-Tiers
		/// </summary>
		[ServerProperty("server", "use_new_actives_ras_scaling", "Use new actives realmabilities (5-Tiers) scaling (1.108+) ?", false)]
		public static bool USE_NEW_ACTIVES_RAS_SCALING;

		/// <summary>
		/// Use pre 1.105 train or livelike
		/// </summary>
		[ServerProperty("server", "custom_train", "Train is custom pre-1.105 one ? (false set it to livelike 1.105+)", true)]
		public static bool CUSTOM_TRAIN;

		/// <summary>
		/// Record news in database
		/// </summary>
		[ServerProperty("server", "record_news", "Record News in database?", true)]
		public static bool RECORD_NEWS;

		/// <summary>
		/// The Server Message of the Day
		/// </summary>
		[ServerProperty("server", "motd", "The Server Message of the Day - Edit this to set what is displayed when a level 2+ character enters the game for the first time, set to \"\" for nothing", "Welcome back to DAoC - Living Server. Adventure alone or alongside autonomous player bots across the three realms.")]
		public static string MOTD;

		/// <summary>
		/// The message players get when they enter the game past level 1
		/// </summary>
		[ServerProperty("server", "starting_msg", "The Starting Mesage - Edit this to set what is displayed when a level 1 character enters the game for the first time, set to \"\" for nothing", "Welcome to DAoC - Living Server, a private Classic and Shrouded Isles world built to play alone or with player bot companions.")]
		public static string STARTING_MSG;

		/// <summary>
		/// The broadcast type
		/// </summary>
		[ServerProperty("server", "broadcast_type", "Broadcast Type - Edit this to change what /b does, values 0 = disabled, 1 = area, 2 = visibility distance, 3 = zone, 4 = region, 5 = realm, 6 = server", 1)]
		public static int BROADCAST_TYPE;

		/// <summary>
		/// Anon Modifier
		/// </summary>
		[ServerProperty("server", "anon_modifier", "Various modifying options for anon, 0 = default, 1 = /who shows player but as ANON, -1 = disabled", 0)]
		public static int ANON_MODIFIER;

		/// <summary>
		/// Death Messages All Realms
		/// </summary>
		[ServerProperty("server", "death_messages_all_realms", "Set to true if you want all realms to see other realms death and kill messages", false)]
		public static bool DEATH_MESSAGES_ALL_REALMS;

		/// <summary>
		/// Disable Instances
		/// </summary>
		[ServerProperty("server", "disable_instances", "Enable or disable instances on the server", false)]
		public static bool DISABLE_INSTANCES;

		/// <summary>
		/// Save QuestItems into Database
		/// </summary>
		[ServerProperty("server", "save_quest_mobs_into_database", "set false if you don't want this", true)]
		public static bool SAVE_QUEST_MOBS_INTO_DATABASE;

		/// <summary>
		/// This specifies the max amount of people in one battlegroup.
		/// </summary>
		[ServerProperty("server", "battlegroup_max_member", "Max number of members allowed in a battlegroup.", 64)]
		public static int BATTLEGROUP_MAX_MEMBER;

		/// <summary>
		///  This specifies the max amount of people in one group.
		/// </summary>
		[ServerProperty("server", "group_max_member", "Max number of members allowed in a group.", 8)]
		public static int GROUP_MAX_MEMBER;

		/// <summary>
		/// Sets the disabled commands for the server split by ;
		/// </summary>
		[ServerProperty("server", "disabled_commands", "Serialized list of disabled commands separated by semi-colon, example /realm;/toon;/quit", "")]
		public static string DISABLED_COMMANDS;

		/// <summary>
		/// Disable Appeal System
		/// </summary>
		[ServerProperty("server", "disable_appeal_system", "Disable the /Appeal System", false)]
		public static bool DISABLE_APPEALSYSTEM;

		/// <summary>
		/// Use Database Language datas instead of files (if empty = build the table from files)
		/// </summary>
		[ServerProperty("server", "use_dblanguage", "Use Database Language datas instead of files (if empty = build the table from files)", false)]
		public static bool USE_DBLANGUAGE;

		/// <summary>
		/// Update existing rows within the LanguageSystem table from language files.
		/// </summary>
		[ServerProperty("server", "update_existing_db_system_sentences_from_files", "Update existing rows within the LanguageSystem table from language files.", false)]
		public static bool UPDATE_EXISTING_DB_SYSTEM_SENTENCES_FROM_FILES;

		/// <summary>
		/// Set the maximum number of objects allowed in a region.  Smaller numbers offer better performance.  This is used to allocate arrays for both Regions and GamePlayers
		/// </summary>
		[ServerProperty("server", "region_max_objects", "Set the maximum number of objects allowed in a region.  Smaller numbers offer better performance.  This can't be changed while the server is running. (256 - 65535)", (ushort)30000)]
		public static ushort REGION_MAX_OBJECTS;

		/// <summary>
		/// Show logins
		/// </summary>
		[ServerProperty("server", "show_logins", "Show login messages when players log in and out of game?", true)]
		public static bool SHOW_LOGINS;

		/// <summary>
		/// Show logins channel
		/// </summary>
		[ServerProperty("server", "show_logins_channel", "What channel should be used for login messages? See eChatType, default is System.", (byte)0)]
		public static byte SHOW_LOGINS_CHANNEL;

		/// <summary>
		/// Enable PvE Speed
		/// </summary>
		[ServerProperty("server", "enable_pve_speed", "Set to true if you wish to enable the extra 25% increase to speed when not in combat or an RvR zone", true)]
		public static bool ENABLE_PVE_SPEED;

		/// <summary>
		/// Enable Encumberance Speed loss
		/// </summary>
		[ServerProperty("server", "enable_encumberance_speed_loss", "Set to true if you wish to enable the encumberance speed loss", true)]
		public static bool ENABLE_ENCUMBERANCE_SPEED_LOSS;

		/// <summary>
		/// Property to enable "forced" Tooltip send when Update are made to player skills, or player effects.
		/// </summary>
		[ServerProperty("server", "use_new_tooltip_forcedupdate", "Set to true if you wish to enable the new 1.110+ Tooltip Forced update each time the server send a skill to a new client.", true)]
		public static bool USE_NEW_TOOLTIP_FORCEDUPDATE;

		/// <summary>
		/// Property to enable crush/slash/thrust determining damage variance for polearms and 2H weapons
		/// </summary>
		[ServerProperty("server", "enable_albion_advanced_weapon_spec", "Set to true to determine damage variance for polearms and 2H weapons on 1H crush/slash/thrust spec.", true)]
		public static bool ENABLE_ALBION_ADVANCED_WEAPON_SPEC;
		
		/// <summary>
		/// Property to enable free respecs
		/// </summary>
		[ServerProperty("server", "free_respec", "Set to true to always allow respecs", false)]
		public static bool FREE_RESPEC;
		#endregion

		#region WORLD
		/// <summary>
		/// Epic encounters strength: 100 is 100% base strength
		/// </summary>
		[ServerProperty("world", "set_difficulty_on_epic_encounters", "Tune encounters taggued <Epic Encounter>. 0 means auto adaptative, others values are % of the initial difficulty (100%=initial difficulty)", 100)]
		public static int SET_DIFFICULTY_ON_EPIC_ENCOUNTERS;

		/// <summary>
		/// A serialised list of disabled RegionIDs
		/// </summary>
		[ServerProperty("world", "disabled_regions", "Serialized list of disabled region IDs, separated by semi-colon or a range with a dash (ie 1-5;7;9)", "")]
		public static string DISABLED_REGIONS = string.Empty;

		/// <summary>
		/// Should the server disable the tutorial zone
		/// </summary>
		[ServerProperty("world", "disable_tutorial", "should the server disable the tutorial zone", false)]
		public static bool DISABLE_TUTORIAL;

		[ServerProperty("world", "world_item_decay_time", "How long (milliseconds) will an item dropped on the ground stay in the world.", (uint) 180000)]
		public static uint WORLD_ITEM_DECAY_TIME;

		[ServerProperty("world", "world_pickup_distance", "How far before you can no longer pick up an object (loot for example).", 256)]
		public static int WORLD_PICKUP_DISTANCE;

		[ServerProperty("world", "world_day_increment", "Larger increments make shorter days. Because night time is 25% faster, it should ideally be a multiple of 4.", (uint) 24)]
		public static uint WORLD_DAY_INCREMENT;

		[ServerProperty("world", "world_npc_update_interval", "How often (milliseconds) will npc's broadcast updates to the clients.", (uint) 5000)]
		public static uint WORLD_NPC_UPDATE_INTERVAL;

		[ServerProperty("world", "world_object_update_interval", "How often (milliseconds) will objects (static, housing, doors) broadcast updates to the clients.", (uint) 30000)]
		public static uint WORLD_OBJECT_UPDATE_INTERVAL;

		[ServerProperty("world", "world_player_update_interval", "How often (milliseconds) will players be checked for updates.", (uint) 1000)]
		public static uint WORLD_PLAYER_UPDATE_INTERVAL;

		[ServerProperty("world", "weather_check_interval", "How often (milliseconds) will weather be checked for a chance to start a storm.", 5 * 60 * 1000)]
		public static int WEATHER_CHECK_INTERVAL;

		[ServerProperty("world", "weather_chance", "What is the chance of starting a storm.", 5)]
		public static int WEATHER_CHANCE;

		[ServerProperty("world", "weather_log_events", "Should weather events be shown in the Log (and on the console).", true)]
		public static bool WEATHER_LOG_EVENTS;

		[ServerProperty("world", "check_los_before_aggro", "Should we perform LoS checks before allowing standard NPCs to aggro from proximity.", true)]
		public static bool CHECK_LOS_BEFORE_AGGRO;

		[ServerProperty("world", "check_los_before_aggro_fnf", "Should we perform LoS checks before allowing FnF turrets to aggro from proximity. If false, they will attempt to cast behind walls.", true)]
		public static bool CHECK_LOS_BEFORE_AGGRO_FNF;

		[ServerProperty("world", "check_los_before_npc_ranged_attack", "Should we perform LoS checks before allowing archer NPCs to attack.", true)]
		public static bool CHECK_LOS_BEFORE_NPC_RANGED_ATTACK;

		[ServerProperty("world", "check_los_during_ranged_attack_minimum_interval", "The minimum interval (milliseconds) between two LoS checks performed during a ranged attack.", 200)]
		public static int CHECK_LOS_DURING_RANGED_ATTACK_MINIMUM_INTERVAL;

		[ServerProperty("world", "check_los_during_cast", "Should we perform LoS checks during spell casts (players and gamebots; see check_los_during_npc_cast for other NPCs).", true)]
		public static bool CHECK_LOS_DURING_CAST;

		// Port of OpenDAoC-Core cbe604ce1 and 978b991fc (live): a monster, guard or pet lands what it started unless crowd
		// controlled. Gamebots keep the player's checks (NpcFinishRules).
		[ServerProperty("world", "check_los_during_npc_cast", "Should NPCs other than gamebots perform LoS checks during spell casts.", false)]
		public static bool CHECK_LOS_DURING_NPC_CAST;

		[ServerProperty("world", "check_range_at_npc_cast_end", "Should NPCs other than gamebots check range at the end of a spell cast.", false)]
		public static bool CHECK_RANGE_AT_NPC_CAST_END;

		[ServerProperty("world", "check_los_during_npc_ranged_attack", "Should NPCs other than gamebots perform LoS checks during a bow draw.", false)]
		public static bool CHECK_LOS_DURING_NPC_RANGED_ATTACK;

		[ServerProperty("world", "check_range_at_npc_ranged_attack_end", "Should NPCs other than gamebots check range at the end of a bow draw.", false)]
		public static bool CHECK_RANGE_AT_NPC_RANGED_ATTACK_END;

		[ServerProperty("world", "check_los_during_cast_minimum_interval", "The minimum interval (milliseconds) between two LoS checks performed during a spell cast.", 200)]
		public static int CHECK_LOS_DURING_CAST_MINIMUM_INTERVAL;

		[ServerProperty("world", "los_check_timeout", "After how long (milliseconds) should a los check timeout. If less than 0, the default ECS timer interval will be used.", 1500)]
		public static int LOS_CHECK_TIMEOUT;

		/// <summary>
		/// HPs gained per champion's level
		/// </summary>
		[ServerProperty("world", "hps_per_championlevel", "The amount of extra HPs gained each time you reach a new Champion's Level", 40)]
		public static int HPS_PER_CHAMPIONLEVEL;

		/// <summary>
		/// Time player must wait after failed task
		/// </summary>
		[ServerProperty("world", "task_pause_ticks", "Time player must wait after failed task check to get new chance for a task, in milliseconds", 5 * 60 * 1000)]
		public static int TASK_PAUSE_TICKS;

		/// <summary>
		/// Should we handle tasks with items
		/// </summary>
		[ServerProperty("world", "task_give_random_item", "Task is also rewarded with ROG ?", false)]
		public static bool TASK_GIVE_RANDOM_ITEM;

		/// <summary>
		/// Should we enable Zone Bonuses?
		/// </summary>
		[ServerProperty("world", "enable_zone_bonuses", "Are Zone Bonuses Enabled?", false)]
		public static bool ENABLE_ZONE_BONUSES;

		/// <summary>
		/// List of ZoneId where personnal mount is allowed
		/// </summary>
		[ServerProperty("world", "allow_personnal_mount_in_regions", "CSV Regions where player mount is allowed", "")]
		public static string ALLOW_PERSONNAL_MOUNT_IN_REGIONS;

		/// <summary>
		/// Display the zonepoint with a choosen model
		/// </summary>
		[ServerProperty("world", "zonepoint_npctemplate", "Display the zonepoint with the following npctemplate. 0 for no display", 0)]
		public static int ZONEPOINT_NPCTEMPLATE;
		#endregion

		#region RATES
		/// <summary>
		/// Xp Cap for a player.  Given in percent of level.  Default is 125%
		/// </summary>
		[ServerProperty("rates", "XP_Cap_Percent", "Maximum XP a player can earn given in percent of their level. Default is 125%", 125)]
		public static int XP_CAP_PERCENT;

		/// <summary>
		/// Xp Cap for a player vs player kill.  Given in percent of level.  Default is 125%
		/// </summary>
		[ServerProperty("rates", "XP_PVP_Cap_Percent", "Maximum XP a player can earn killing another player, given in percent of their level. Default is 125%", 125)]
		public static int XP_PVP_CAP_PERCENT;

		/// <summary>
		/// The Experience Rate
		/// </summary>
		[ServerProperty("rates", "xp_rate", "The Experience Points Rate Modifier - Edit this to change the rate at which you gain experience points e.g 1.5 is 50% more 2.0 is twice the amount (100%) 0.5 is half the amount (50%)", 1.0)]
		public static double XP_RATE;

		/// <summary>
		/// The autonomous player-bot experience rate. This is intentionally
		/// independent from XP_RATE so the real player's progression can be
		/// tuned without changing the persistent world population.
		/// </summary>
		[ServerProperty("rates", "bot_xp_rate", "The autonomous player-bot Experience Points Rate Modifier. This does not affect real player characters or temporary companion helpers.", 1.0)]
		public static double BOT_XP_RATE;

		/// <summary>
		/// Optional XP rates by level range for real players, replacing XP_RATE inside each listed
		/// range (see LevelRangeXpRates). Empty keeps the flat rate.
		/// </summary>
		[ServerProperty("rates", "xp_rate_by_level", "Optional XP rate by level range for real player characters, for example 1-19:3;20-34:5;35-50:12. Inside a listed range its rate replaces xp_rate; other levels keep xp_rate. In RvR zones the result is still multiplied by rvr_zones_xp_rate. Empty (the default) or an invalid value keeps the flat xp_rate.", "")]
		public static string XP_RATE_BY_LEVEL;

		/// <summary>
		/// Optional XP rates by level range for autonomous gamebots, replacing BOT_XP_RATE inside each
		/// listed range (see LevelRangeXpRates). Empty keeps the flat rate.
		/// </summary>
		[ServerProperty("rates", "bot_xp_rate_by_level", "Optional XP rate by level range for autonomous gamebots, for example 1-19:1.5;20-34:2.5;35-50:6. Inside a listed range its rate replaces bot_xp_rate; other levels keep bot_xp_rate. In RvR zones the result is still multiplied by rvr_zones_xp_rate, as bot_xp_rate is. Empty (the default) or an invalid value keeps the flat bot_xp_rate. Companion bots and real players are not affected.", "")]
		public static string BOT_XP_RATE_BY_LEVEL;

		/// <summary>
		/// Quest experience follows xp_rate_by_level (owner 2026-10-30, see QuestExperience). On, and a range covers the
		/// player's level: the quest's XP is multiplied by that level's rate. Off, no range or no setting: quest XP as before.
		/// </summary>
		[ServerProperty("rates", "quest_xp_by_level", "Quest experience follows xp_rate_by_level. When true (the default) and one of its ranges covers the player's level, the XP of data quests, reward quests, bounties and the daily, weekly and battleground quests is multiplied by that level's rate (a bounty's xp_rate is replaced by it). While true, one quest payment never pays more than one level. False is a kill switch (2026-10-31): no rate and no cap, quest XP exactly as before 1.1.1. No range for the level or no xp_rate_by_level leaves the amount as it was.", true)]
		public static bool QUEST_XP_BY_LEVEL;

		/// <summary>
		/// The epic chains, linked by quest ID as the server reads the quest rows (Living Server 1.2, EpicChainLinks):
		/// epic_chains.py's changes, made in memory, never written to the world.
		/// </summary>
		[ServerProperty("quest", "epic_chains_linked", "The epic chains are linked by quest ID as the server reads the quest rows (Living Server 1.2): the Guild of Shadows steps 7 to 48 in order with their period dialogue, and every other guild's epic steps pinned in order (tools/claude-version/epic_chains.py's changes). They are made in memory each time the rows are read and never written to the world database, so removing Living Server leaves the released quests as they were. False is a kill switch: the rows are read as they are.", true)]
		public static bool EPIC_CHAINS_LINKED = true;

		/// <summary>
		/// The CL Experience Rate
		/// </summary>
		[ServerProperty("rates", "cl_xp_rate", "The Champion Level Experience Points Rate Modifier - Edit this to change the rate at which you gain CL experience points e.g 1.5 is 50% more 2.0 is twice the amount (100%) 0.5 is half the amount (50%)", 1.0)]
		public static double CL_XP_RATE;

		/// <summary>
		/// RvR Zones XP Rate
		/// </summary>
		[ServerProperty("rates", "rvr_zones_xp_rate", "An extra Experience Points multiplier in RvR zones (battlegrounds, frontiers, Darkness Falls), applied on top of xp_rate and bot_xp_rate. 1.0 gives RvR zones the same rate as everywhere else.", 1.0)]
		public static double RvR_XP_RATE;

		/// <summary>
		/// The Realm Points Rate
		/// </summary>
		[ServerProperty("rates", "rp_rate", "The Realm Points Rate Modifier - Edit this to change the rate at which you gain realm points e.g 1.5 is 50% more 2.0 is twice the amount (100%) 0.5 is half the amount (50%)", 1.0)]
		public static double RP_RATE;

		/// <summary>
		/// The Bounty Points Rate
		/// </summary>
		[ServerProperty("rates", "bp_rate", "The Bounty Points Rate Modifier - Edit this to change the rate at which you gain bounty points e.g 1.5 is 50% more 2.0 is twice the amount (100%) 0.5 is half the amount (50%)", 1.0)]
		public static double BP_RATE;

		/// <summary>
		/// The damage players do against monsters with melee
		/// </summary>
		[ServerProperty("rates", "pve_melee_damage", "The PvE Melee Damage Modifier - Edit this to change the amount of melee damage done when fighting mobs e.g 1.5 is 50% more damage 2.0 is twice the damage (100%) 0.5 is half the damage (50%)", 1.0)]
		public static double PVE_MELEE_DAMAGE;

		/// <summary>
		/// The damage players do against monsters with spells
		/// </summary>
		[ServerProperty("rates", "pve_spell_damage", "The PvE Spell Damage Modifier - Edit this to change the amount of spell damage done when fighting mobs e.g 1.5 is 50% more damage 2.0 is twice the damage (100%) 0.5 is half the damage (50%)", 1.0)]
		public static double PVE_SPELL_DAMAGE = 1.0;

		/// <summary>
		/// The damage players do against players with melee
		/// </summary>
		[ServerProperty("rates", "pvp_melee_damage", "The PvP Melee Damage Modifier - Edit this to change the amount of melee damage done when fighting players e.g 1.5 is 50% more damage 2.0 is twice the damage (100%) 0.5 is half the damage (50%)", 1.0)]
		public static double PVP_MELEE_DAMAGE;

		/// <summary>
		/// The damage players do against players with spells
		/// </summary>
		[ServerProperty("rates", "pvp_spell_damage", "The PvP Spell Damage Modifier - Edit this to change the amount of spell damage done when fighting players e.g 1.5 is 50% more damage 2.0 is twice the damage (100%) 0.5 is half the damage (50%)", 1.0)]
		public static double PVP_SPELL_DAMAGE;

		/// <summary>
		/// The % value of gainrps when heal a players recently damaged in rvr.
		/// </summary>
		[ServerProperty("rates", "heal_pvp_damage_value_rp", "How many % of heal final value is obtained in rps?", 8)]
		public static int HEAL_PVP_DAMAGE_VALUE_RP;

		/// <summary>
		/// The highest possible Block Rate against an Enemy (Hard Cap)
		/// </summary>
		[ServerProperty("rates", "block_cap", "Block Rate Cap Modifier - Edit this to change the highest possible block rate against an enemy (Hard Cap) in game e.g .60 = 60%", 1.00)]
		public static double BLOCK_CAP;

		///<summary>
		/// The highest possible Evade Rate against an Enemy (Hard Cap)
		/// </summary>
		[ServerProperty("rates", "evade_cap", "Evade Rate Cap Modifier - Edit this to change the highest possible evade rate against an enemy (Hard Cap) in game e.g .50 = 50%", 0.50)]
		public static double EVADE_CAP;

		///<summary>
		///The highest possible Parry Rate against an Enemy (Hard Cap)
		/// </summary>
		[ServerProperty("rates", "parry_cap", "Parry Rate Cap Modifier - Edit this to change the highest possible parry rate against an enemy (Hard Cap) in game e.g .50 = 50%", 0.50)]
		public static double PARRY_CAP;

		/// <summary>
		/// The money drop modifier
		/// </summary>
		[ServerProperty("rates", "money_drop", "Money Drop Modifier - Edit this to change the amount of money which is dropped e.g 1.5 is 50% more 2.0 is twice the amount (100%) 0.5 is half the amount (50%)", 1.0)]
		public static double MONEY_DROP;
		
		/// <summary>
		/// The small chest drop chance
		/// </summary>
		[ServerProperty("rates", "base_smallchest_chance", "Percentage chance that a mob will drop a small chest of money", 10)]
		public static int BASE_SMALLCHEST_CHANCE;
		
		/// <summary>
		/// The math multiplier for small chest value
		/// </summary>
		[ServerProperty("rates", "smallchest_multiplier", "The math multiplier for small chest value. Increase for more gold", 10)]
		public static int SMALLCHEST_MULTIPLIER;
		
		/// <summary>
		/// The large chest drop chance
		/// </summary>
		[ServerProperty("rates", "base_largechest_chance", "Percentage chance that a mob will drop a large chest of money", 5)]
		public static int BASE_LARGECHEST_CHANCE;
		
		/// <summary>
		/// The math multiplier for small chest value
		/// </summary>
		[ServerProperty("rates", "largechest_multiplier", "The math multiplier for large chest value. Increase for more gold", 17)]
		public static int LARGECHEST_MULTIPLIER;
		
		/// <summary>
		/// The time until a player is worth rps again after death
		/// </summary>
		[ServerProperty("rates", "rp_worth_seconds", "Realm Points Worth Seconds - Edit this to change how many seconds until a player is worth RPs again after being killed ", 300)]
		public static int RP_WORTH_SECONDS;

		/// <summary>
		/// Health Regen Rate
		/// </summary>
		[ServerProperty("rates", "health_regen_amount_modifier", "Health regen amount modifier", 1.0)]
		public static double HEALTH_REGEN_AMOUNT_MODIFIER = 1.0;

		/// <summary>
		/// Endurance regeneration multiplier (1.2.3: set in the launcher's Living Server tab, 1 to 10; from a r/daoc report of no
		/// endurance regeneration at all). EnduranceRegenerationAmountCalculator multiplies the whole tick by it and rounds down.
		/// </summary>
		[ServerProperty("rates", "endurance_regen_amount_modifier", "Endurance regeneration multiplier for real players and gamebots alike, Tireless and endurance songs included. 1 is the classic rate; a tick is rounded down, so below 1 a standing character regenerates nothing.", 1.0)]
		public static double ENDURANCE_REGEN_AMOUNT_MODIFIER = 1.0;

		/// <summary>
		/// Health Regen Rate
		/// </summary>
		/// <summary>
		/// Power regeneration multiplier. Living Server 1.1.2 (owner 2026-11-03, from a r/daoc report): 1.5 by default. The base
		/// rate is the pre-1.87 one (patch 1.87 doubled it, about 23 per tick at level 50 instead of 12), so 1.5 is a middle ground.
		/// Worlds that still had the old untouched default (1) get 1.5 once (LivingServerSettingsUpgrade).
		/// </summary>
		[ServerProperty("rates", "mana_regen_amount_modifier", "Power regeneration multiplier for real players and gamebots alike. 1 is the classic rate before patch 1.87, which doubled it; 1.5 (the Living Server default) is a middle ground.", 1.5)]
		public static double MANA_REGEN_AMOUNT_MODIFIER = 1.5;

		/// <summary>
		/// The classic list-caster rule: half power regeneration below 50% power. Living Server 1.1.2: off by default, and it applies
		/// to real players and GameBots alike when on (bots were exempt before).
		/// </summary>
		[ServerProperty("rates", "mana_regen_amount_halved_below_50_percent", "Halve power regeneration below 50% power for list casters (the classic rule). Applies to real players and gamebots alike. Off by default in the Living Server; set in the launcher's Living Server tab.", false)]
		public static bool MANA_REGEN_AMOUNT_HALVED_BELOW_50_PERCENT;

		/// <summary>
		/// Items sell ratio
		/// </summary>
		[ServerProperty("rates", "item_sell_ratio", "Merchants are buying items at the % of initial value", 50)]
		public static int ITEM_SELL_RATIO;

		/// <summary>
		/// Chance for condition loss on weapons and armor
		/// </summary>
		[ServerProperty("rates", "item_condition_loss_chance", "What chance does armor or weapon have to lose condition?", 5)]
		public static int ITEM_CONDITION_LOSS_CHANCE;

		/// <summary>
		/// Under level 35 mount speed, live like = 135
		/// </summary>
		[ServerProperty("rates", "mount_under_level_35_speed", "What is the speed of player controlled mounts under level 35?", (short)135)]
		public static short MOUNT_UNDER_LEVEL_35_SPEED;

		/// <summary>
		/// Over level 35 mount speed, live like = 145
		/// </summary>
		[ServerProperty("rates", "mount_over_level_35_speed", "What is the speed of player controlled mounts over level 35?", (short)145)]
		public static short MOUNT_OVER_LEVEL_35_SPEED;

		/// <summary>
		/// Relic Bonus Modifier
		/// </summary>
		[ServerProperty("rates", "relic_owning_bonus", "Relic Owning Bonus in percent per relic (default 10%) in effect when owning enemy relic", (short)10)]
		public static short RELIC_OWNING_BONUS;

		#endregion

		#region NPCs
		/// <summary>
		/// Doppelganger realm point value
		/// </summary>
		[ServerProperty("npc", "doppelganger_realm_points", "Realm point value of doppelgangers.", 400)]
		public static int DOPPELGANGER_REALM_POINTS;

		/// <summary>
		/// Doppelganger bounty point value
		/// </summary>
		[ServerProperty("npc", "doppelganger_bounty_points", "Bounty point value of doppelgangers.", 250)]
		public static int DOPPELGANGER_BOUNTY_POINTS;

		[ServerProperty("npc", "force_mob_autoset_stats", "Should standard NPCs have their stat automatically set using server properties (discarding database values).", true)]
		public static bool FORCE_MOB_AUTOSET_STATS;

		/// <summary>
		/// Base Value to use when auto-setting STR stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_str_base", "Base Value to use when auto-setting STR stat.", (short)30)]
		public static short MOB_AUTOSET_STR_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting STR stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_str_multiplier", "Multiplier to use when auto-setting STR stat. Multiplied by 10 when applied.", 1.0)]
		public static double MOB_AUTOSET_STR_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting CON stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_con_base", "Base Value to use when auto-setting CON stat.", (short)30)]
		public static short MOB_AUTOSET_CON_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting CON stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_con_multiplier", "Multiplier to use when auto-setting CON stat.", 1.0)]
		public static double MOB_AUTOSET_CON_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting DEX stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_dex_base", "Base Value to use when auto-setting DEX stat.", (short)30)]
		public static short MOB_AUTOSET_DEX_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting DEX stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_dex_multiplier", "Multiplier to use when auto-setting DEX stat.", 1.0)]
		public static double MOB_AUTOSET_DEX_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting QUI stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_qui_base", "Base Value to use when auto-setting qui stat.", (short)30)]
		public static short MOB_AUTOSET_QUI_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting QUI stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_qui_multiplier", "Multiplier to use when auto-setting QUI stat.", 1.0)]
		public static double MOB_AUTOSET_QUI_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting INT stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_int_base", "Base Value to use when auto-setting INT stat.", (short)30)]
		public static short MOB_AUTOSET_INT_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting INT stat.
		/// </summary>
		[ServerProperty("npc", "mob_autoset_int_multiplier", "Multiplier to use when auto-setting INT stat.", 1.0)]
		public static double MOB_AUTOSET_INT_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting pet STR stat.
		/// </summary>
		[ServerProperty("npc", "pet_autoset_str_base", "Base Value to use when auto-setting Pet STR stat.", (short)30)]
		public static short PET_AUTOSET_STR_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting pet STR stat.
		/// </summary> 
		[ServerProperty("npc", "pet_autoset_str_multiplier", "Multiplier to use when auto-setting Pet STR stat. Multiplied by 10 when applied.", 1.0)]
		public static double PET_AUTOSET_STR_MULTIPLIER;
		
		/// Base Value to use when auto-setting pet CON stat.
		/// </summary>
		[ServerProperty("npc", "pet_autoset_con_base", "Base Value to use when auto-setting Pet CON stat.", (short)30)]
		public static short PET_AUTOSET_CON_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting pet CON stat.
		/// </summary>
		[ServerProperty("npc", "pet_autoset_con_multiplier", "Multiplier to use when auto-setting Pet CON stat.", 1.0)]
		public static double PET_AUTOSET_CON_MULTIPLIER;

		/// Base Value to use when auto-setting Pet DEX stat.
		/// </summary>
		[ServerProperty("npc", "pet_autoset_dex_base", "Base Value to use when auto-setting Pet DEX stat.", (short)30)]
		public static short PET_AUTOSET_DEX_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting pet DEX stat.
		/// </summary>
		[ServerProperty("npc", "pet_autoset_dex_multiplier", "Multiplier to use when auto-setting Pet DEX stat.", 1.0)]
		public static double PET_AUTOSET_DEX_MULTIPLIER;

		/// Base Value to use when auto-setting Pet QUI stat.
		/// </summary>
		[ServerProperty("npc", "pet_autoset_qui_base", "Base Value to use when auto-setting Pet QUI stat.", (short)30)]
		public static short PET_AUTOSET_QUI_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting pet QUI stat.
		/// </summary>
		[ServerProperty("npc", "pet_autoset_qui_multiplier", "Multiplier to use when auto-setting Pet QUI stat. ", 1.0)]
		public static double PET_AUTOSET_QUI_MULTIPLIER;

		/// <summary>
		/// Multiplier to use when auto-setting pet INT stat.
		/// INT is the stat used for spell damage for mobs/pets
		/// </summary>
		[ServerProperty("npc", "pet_autoset_int_base", "Multiplier to use when auto-setting Pet INT stat.", (short)30)]
		public static short PET_AUTOSET_INT_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting pet INT stat.
		/// INT is the stat used for spell damage for mobs/pets
		/// </summary>
		[ServerProperty("npc", "pet_autoset_int_multiplier", "Multiplier to use when auto-setting Pet INT stat.", 1.0)]
		public static double PET_AUTOSET_INT_MULTIPLIER;

		// Necro pet stat properties

		/// <summary>
		/// Base value to use when setting strength for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_str_base", "Base value to use when setting strength for most necromancer pets.", (short)60)]
		public static short NECRO_PET_STR_BASE;

		/// <summary>
		/// Multiplier to use when setting strength for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_str_multiplier", "Multiplier to use when setting strength for most necromancer pets.", 1.0)]
		public static double NECRO_PET_STR_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting constitution for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_con_base", "Base value to use when setting constitution for most necromancer pets.", (short)60)]
		public static short NECRO_PET_CON_BASE;

		/// <summary>
		/// Multiplier to use when setting constitution for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_con_multiplier", "Multiplier to use when setting constitution for most necromancer pets.", 0.5)]
		public static double NECRO_PET_CON_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting dexterity for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_dex_base", "Base value to use when setting dexterity for most necromancer pets.", (short)60)]
		public static short NECRO_PET_DEX_BASE;

		/// <summary>
		/// Multiplier to use when setting dexterity for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_dex_multiplier", "Multiplier to use when setting dexterity for most necromancer pets.", 0.0)]
		public static double NECRO_PET_DEX_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting quickness for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_qui_base", "Base value to use when setting quickness for most necromancer pets.", (short)60)]
		public static short NECRO_PET_QUI_BASE;

		/// <summary>
		/// Multiplier to use when setting quickness for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_qui_multiplier", "Multiplier to use when setting quickness for most necromancer pets.", 0.3333)]
		public static double NECRO_PET_QUI_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting intelligence for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_int_base", "Base value to use when setting intelligence for most necromancer pets.", (short)60)]
		public static short NECRO_PET_INT_BASE;

		/// <summary>
		/// Multiplier to use when setting intelligence for most necromancer pets.
		/// </summary>
		[ServerProperty("npc", "necro_pet_int_multiplier", "Multiplier to use when setting intelligence for most necromancer pets.", 0.3333)]
		public static double NECRO_PET_INT_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting strength for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_str_base", "Base value to use when setting strength for greater necroservant pets.", (short)60)]
		public static short NECRO_GREATER_PET_STR_BASE;

		/// <summary>
		/// Multiplier to use when setting strength for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_str_multiplier", "Multiplier to use when setting strength for greater necroservant pets.", 0.0)]
		public static double NECRO_GREATER_PET_STR_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting constitution forgreater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_con_base", "Base value to use when setting constitution for greater necroservant pets.", (short)60)]
		public static short NECRO_GREATER_PET_CON_BASE;

		/// <summary>
		/// Multiplier to use when setting constitution for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_con_multiplier", "Multiplier to use when setting constitution for greater necroservant pets.", 0.3333)]
		public static double NECRO_GREATER_PET_CON_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting dexterity for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_dex_base", "Base value to use when setting dexterity for greater necroservant pets.", (short)60)]
		public static short NECRO_GREATER_PET_DEX_BASE;

		/// <summary>
		/// Multiplier to use when setting dexterity for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_dex_multiplier", "Multiplier to use when setting dexterity for greater necroservant pets.", 0.5)]
		public static double NECRO_GREATER_PET_DEX_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting quickness for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_qui_base", "Base value to use when setting quickness for greater necroservant pets.", (short)60)]
		public static short NECRO_GREATER_PET_QUI_BASE;

		/// <summary>
		/// Multiplier to use when setting quickness for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_qui_multiplier", "Multiplier to use when setting quickness for greater necroservant pets.", 1.0)]
		public static double NECRO_GREATER_PET_QUI_MULTIPLIER;

		/// <summary>
		/// Base value to use when setting intelligence for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_int_base", "Base value to use when setting intelligence for greater necroservant pets.", (short)60)]
		public static short NECRO_GREATER_PET_INT_BASE;

		/// <summary>
		/// Multiplier to use when setting intelligence for greater necroservant pets.
		/// </summary>
		[ServerProperty("npc", "necro_greater_pet_int_multiplier", "Multiplier to use when setting intelligence for greater necroservant pets.", 0.3333)]
		public static double NECRO_GREATER_PET_INT_MULTIPLIER;

		/// <summary>
		/// How often should pets think?
		/// </summary>
		[ServerProperty("npc", "pet_think_interval", "How often should pets think?", 600)]
		public static int PET_THINK_INTERVAL;

		/// <summary>
		/// Scale pet spell values according to their level?
		/// </summary>
		[ServerProperty("npc", "pet_bd_commander_taunt_multiplier", "Percentage of damage that BD commanders get as extra aggro when taunting, e.g. a taunting BD commander gets 150% normal aggro at 50, 200% at 100, 250% at 150 etc. ", 150)]
		public static int PET_BD_COMMANDER_TAUNT_VALUE;

		/// <summary>
		/// Minimum respawn time for npc's without a set respawninterval
		/// </summary>
		[ServerProperty("npc", "npc_min_respawn_interval", "Minimum respawn time, in minutes, for npc's without a set respawninterval", 5)]
		public static int NPC_MIN_RESPAWN_INTERVAL;

		/// <summary>
		/// Respawn Interval for Shrouded Isles Epic Encounter
		/// </summary>
		[ServerProperty("world", "set_si_epic_encounter_respawninterval", "Respawn Time, in minutes, for Epic Encounters in Shrouded Isles", 60)]
		public static int SET_SI_EPIC_ENCOUNTER_RESPAWNINTERVAL;

		/// <summary>
		/// Respawn Interval for Normal Epic Game Boss Encounter
		/// </summary>
		[ServerProperty("world", "set_epic_game_encounter_respawninterval", "Respawn Time, in minutes, for Normal Epic Game Encounters", 60)]
		public static int SET_EPIC_GAME_ENCOUNTER_RESPAWNINTERVAL;

		/// <summary>
		/// Respawn Interval for Epic Quest Mobs
		/// </summary>
		[ServerProperty("world", "set_epic_quest_encounter_respawninterval", "Respawn Time, in minutes, for Epic Quest Encounters", 30)]
		public static int SET_EPIC_QUEST_ENCOUNTER_RESPAWNINTERVAL;

		/// <summary>
		/// Weapon damage cap for epic encounters that use melee weapons
		/// </summary>
		[ServerProperty("npc", "set_epic_encounter_weapon_damage_cap", "Maximum damage cap multipler for epic encounters that use melee weapons", 1.5)]
		public static double SET_EPIC_ENCOUNTER_WEAPON_DAMAGE_CAP;

		/// <summary>
		/// Epic encounters (1.2): a boss's throw, teleport or jail does not take your companions and pet along
		/// (EncounterDisplacement.MovePlayer). Owner default 2026-11-06, to confirm.
		/// </summary>
		[ServerProperty("npc", "epic_companions_stay_when_thrown", "Epic encounters: when a boss throws, teleports or jails you, your /spawn companions and your pet stay at the fight and keep fighting (True, the 1.2 default) or are moved along with you as by a portal (False, as most throws did before 1.2; Legion's throw kept them at the fight before too). Bots the boss throws themselves find their way back either way.", true)]
		public static bool EPIC_COMPANIONS_STAY_WHEN_THROWN = true;

		/// <summary>
		/// The dragons' wing-gust throw (1.2, DragonEncounterThrow): how high above the thrown raider each dragon may hurl.
		/// 0 keeps the original lair spots (OpenDAoC-Core 3513bad: up to about 1,400 units, about 60% of a raider's health
		/// without Safe Fall). Owner default 2026-11-06, to confirm.
		/// </summary>
		[ServerProperty("npc", "epic_dragon_throw_height_golestandt", "Golestandt's wing-gust throw: the highest a raider is hurled above where they stood, in units. 0 = the original lair spots (up to about 1,400 units: about 60% of a raider's health from the fall). 712 is the fork's old half throw (about 34%); lower is gentler.", 0)]
		public static int EPIC_DRAGON_THROW_HEIGHT_GOLESTANDT = 0;

		[ServerProperty("npc", "epic_dragon_throw_height_gjalpinulva", "Gjalpinulva's wing-gust throw: the highest a raider is hurled above where they stood, in units. 0 = the original lair spots (about 60% of a raider's health from the fall). 712 is about 34%; lower is gentler.", 0)]
		public static int EPIC_DRAGON_THROW_HEIGHT_GJALPINULVA = 0;

		[ServerProperty("npc", "epic_dragon_throw_height_cuuldurach", "Cuuldurach's wing-gust throw: the highest a raider is hurled above where they stood, in units. 0 = the original lair spots (about 60% of a raider's health from the fall). 712 is about 34%; lower is gentler.", 0)]
		public static int EPIC_DRAGON_THROW_HEIGHT_CUULDURACH = 0;

		/// <summary>
		/// Epic encounters (1.2, epic wave 2): the restored original mechanics, each its own setting so the owner can turn one
		/// off or on without a rebuild (EpicEncounterMechanics reads them live; a boss's health and a spell take the value
		/// when they are next read or built). Owner defaults 2026-11-07, to confirm.
		/// </summary>
		[ServerProperty("npc", "epic_horsemen_tether_heal", "Caer Sidi: Fames, Morbus and Funus heal to full when drawn past their tether, as in the original (True, 1.2). The raid fights each horseman near his room and leads him back.", true)]
		public static bool EPIC_HORSEMEN_TETHER_HEAL = true;

		[ServerProperty("npc", "epic_funus_weapon_puzzle", "Caer Sidi: Funus takes damage only from clerics, and from mercenaries, armsmen, infiltrators and scouts with their listed weapon; any other hit (pets too) heals him to full (True). Off by default in 1.2. Do not turn it on yet: the tactic is not finished (owner 2026-11-08: off until the bots clear it with no heals). False (default): everyone hurts him, as in 1.1.", false)]
		public static bool EPIC_FUNUS_WEAPON_PUZZLE = false;

		[ServerProperty("npc", "epic_funus_bot_shield_guard", "Caer Sidi: while Funus's weapon puzzle is on, the sting of a damage shield on a bot he hits neither heals nor hurts him (True, 1.2): a bot can't take a shield off, and its sting would heal him to full. False: as the original, the sting of a bot of a class he doesn't take damage from heals him to full. Real players keep the original either way.", true)]
		public static bool EPIC_FUNUS_BOT_SHIELD_GUARD = true;

		[ServerProperty("npc", "epic_sacristan_patrol", "Caer Sidi: the Skeletal Sacristan walks his loop at full speed and never stops, with 70% magic resists (True, 1.2). The raid waits along his loop and hits him as he passes.", true)]
		public static bool EPIC_SACRISTAN_PATROL = true;

		[ServerProperty("npc", "epic_soul_reckoner_full_shield", "Caer Sidi: Soul Reckoner takes no damage at all while a reckoned soul lives (True, 1.2; False: a quarter).", true)]
		public static bool EPIC_SOUL_RECKONER_FULL_SHIELD = true;

		[ServerProperty("npc", "epic_soul_reckoner_small_room", "Caer Sidi: Soul Reckoner takes damage only within 900 of his room's middle (True, 1.2; False: 1,500, the 1.1 change).", true)]
		public static bool EPIC_SOUL_RECKONER_SMALL_ROOM = true;

		[ServerProperty("npc", "epic_aroon_damage_order", "Galladoria: Aroon's Scaiths die in order, each immune until the one before is dead, and Aroon takes only the damage types whose Scaith is dead (True, 1.2).", true)]
		public static bool EPIC_AROON_DAMAGE_ORDER = true;

		[ServerProperty("npc", "epic_galladoria_bridge_spot", "Galladoria: the primals come when a raider (bots too) stands on the waterfall bridge spot (True, 1.2); a bot raid that waited on the bridge three minutes starts it anyway.", true)]
		public static bool EPIC_GALLADORIA_BRIDGE_SPOT = true;

		[ServerProperty("npc", "epic_full_fire_patches", "Galladoria: the Fire primal's trail of fire hits 220 in a 450 radius, as in the original (True), or 120 in 300 (False: the owner's 2026-10-19 nerf, kept as the 1.2 default until a played check).", false)]
		public static bool EPIC_FULL_FIRE_PATCHES = false;

		[ServerProperty("npc", "epic_earthmender_class_puzzle", "Galladoria: each Earthmender takes damage only from its own class group (True, 1.2). The raid sends each raider to one its class hurts.", true)]
		public static bool EPIC_EARTHMENDER_CLASS_PUZZLE = true;

		[ServerProperty("npc", "epic_earthmenders_passive", "Galladoria: Earthmenders never attack, they only heal (True, 1.2, as in the original).", true)]
		public static bool EPIC_EARTHMENDERS_PASSIVE = true;

		[ServerProperty("npc", "epic_earthmender_quick_reset", "Galladoria: an Earthmender heals to full 30 s after its last fight even with raiders in the room (True, 1.2, as in the original).", true)]
		public static bool EPIC_EARTHMENDER_QUICK_RESET = true;

		[ServerProperty("npc", "epic_earthmender_mutual_heal", "Galladoria: Earthmenders heal each other as well as the Earth primal (as in the original). False by default (owner 2026-11-18): with it on the bot raid can't kill them.", false)]
		public static bool EPIC_EARTHMENDER_MUTUAL_HEAL = false;

		[ServerProperty("npc", "epic_beatha_heals_xaga", "Galladoria: damage to Beatha heals Xaga by twice the amount (True, 1.2). The raid kills Beatha, then Tine, then Xaga.", true)]
		public static bool EPIC_BEATHA_HEALS_XAGA = true;

		[ServerProperty("npc", "epic_fairy_flight", "Galladoria: Beatha and Tine circle the room even while they are fought (True, 1.2, as in the original).", true)]
		public static bool EPIC_FAIRY_FLIGHT = true;

		[ServerProperty("npc", "epic_govannon_full_health", "Summoner's Hall: Grand Summoner Govannon has 300,000 health, as in the original (True), or 200,000 (False, the 1.2 default until a played check: a 60-bot raid took him about 1% a minute).", false)]
		public static bool EPIC_GOVANNON_FULL_HEALTH = false;

		[ServerProperty("npc", "epic_govannon_full_heal", "Summoner's Hall: when Grand Summoner Govannon turns into his demon at 50% health he heals to full, as in the original (True), or to 75% (False, the 1.2 default: owner 2026-11-11, after bot raids that stepped out of his Shroud still lost with the demon at 29% to 42%).", false)]
		public static bool EPIC_GOVANNON_FULL_HEAL = false;

		[ServerProperty("npc", "epic_easmarach_endless_falls_heal", "Galladoria: Easmarach heals an eighth on the waterfall top every time he falls to 30% and stays up there, as in the original (True), or heals there once a fight and then comes back down to fight on (False, the 1.2 default: a bot raid can only shoot him up there, about 1% a minute, and never finished him; play test 2026-11-07).", false)]
		public static bool EPIC_EASMARACH_ENDLESS_FALLS_HEAL = false;

		[ServerProperty("npc", "epic_kvasir_cold_heal", "Tuscaran Glacier: cold damage heals Icelord Kvasir by a fifth of his health and does no damage (True, 1.2). No bot casts cold at him.", true)]
		public static bool EPIC_KVASIR_COLD_HEAL = true;

		[ServerProperty("npc", "epic_tuscar_twin_heal", "Tuscaran Glacier: damage to King Tuscar heals Queen Kula and damage to her heals him while the other is hurt (True, 1.2). The raid kills Kula first; only the King's own tank fights him. Test first.", true)]
		public static bool EPIC_TUSCAR_TWIN_HEAL = true;

		[ServerProperty("npc", "epic_dragon_messengers", "The dragons: from half health every 80-90 s four to six messengers run out of the lair and each calls four to six adds (True, 1.2). The raid's casters and archers run them down. False: no messengers at any dragon.", true)]
		public static bool EPIC_DRAGON_MESSENGERS = true;

		/// <summary>
		/// Epic encounters (1.2): per-encounter switch values that replace the shipped ones (EpicEncounterMechanics.
		/// ShippedPerEncounter), for any boss and any of its switches, without a rebuild.
		/// </summary>
		[ServerProperty("npc", "epic_encounter_switches", "Epic encounters: per-boss switch values that replace what ships, as Switch@Boss=True or False, separated by commas or semicolons. Example: AllowBotDisplacement@Orylle=False; AllowPositionPuzzle@Olcasgean=False. Switches: AllowDisplacement, AllowPositionPuzzle, AllowInvulnerableHazards, AllowAddShield, AllowGlacierGriffonPatrol, AllowBotDisplacement, AllowDragonThrow, AllowDragonMessengers. Boss = the script's class name (the catalog docs/encounters/EPIC-ENCOUNTERS.md lists them). Empty: what ships.", "")]
		public static string EPIC_ENCOUNTER_SWITCHES = "";

		/// <summary>
		/// Allow Roam
		/// </summary>
		[ServerProperty("npc", "allow_roam", "Allow mobs to roam on the server", true)]
		public static bool ALLOW_ROAM;

		/// <summary>
		/// Chance for NPC to roam.
		/// </summary>
		[ServerProperty("npc", "gamenpc_roam_cooldown_min", "Minimum duration in seconds between two roams.", 5)]
		public static int GAMENPC_ROAM_COOLDOWN_MIN;

		/// <summary>
		/// Chance for NPC to roam.
		/// </summary>
		[ServerProperty("npc", "gamenpc_roam_cooldown_max", "Maximum duration in seconds between two roams.", 40)]
		public static int GAMENPC_ROAM_COOLDOWN_MAX;

		/// <summary>
		/// How often, in milliseconds, to check follow distance.  Lower numbers make NPC follow closer but increase load on server.
		/// </summary>
		[ServerProperty("npc", "gamenpc_followcheck_time", "How often, in milliseconds, to check follow distance. Lower numbers make NPC follow closer but increase load on server.", 100)]
		public static int GAMENPC_FOLLOWCHECK_TIME;

		/// <summary>
		/// Override the classtype of any npc with a classtype of DOL.GS.GameNPC
		/// </summary>
		[ServerProperty("npc", "gamenpc_default_classtype", "Change the classtype of any npc of classtype DOL.GS.GameNPC to this.", "DOL.GS.GameNPC")]
		public static string GAMENPC_DEFAULT_CLASSTYPE;

		/// <summary>
		/// Chances for npc (including pet) to style (chance is calculated randomly according to this value + the number of style the NPC own)
		/// </summary>
		[ServerProperty("npc", "gamenpc_chances_to_style", "Change the chance to fire a style for a mob or a pet", 20)]
		public static int GAMENPC_CHANCES_TO_STYLE;

		/// <summary>
		/// NPCs heal when a target is below what percentage of their health?
		/// </summary>
		[ServerProperty("npc", "npc_heal_threshold", "NPCs heal targets whose health falls below this percentage.", 75)]
		public static int NPC_HEAL_THRESHOLD;

		/// <summary>
		/// Pets heal when a target is below what percentage of their health?
		/// </summary>
		[ServerProperty("npc", "pet_heal_threshold", "Pets (including charmed NPCs, excluding Bonedancer pets) heal targets whose health falls below this percentage.", 50)]
		public static int PET_HEAL_THRESHOLD;

		/// <summary>
		/// Bonedancer healer pets heal when a target is below what percentage of their health?
		/// </summary>
		[ServerProperty("npc", "bonedancer_healer_pets_heal_threshold", "Bonedancer healer pets heal targets whose health falls below this percentage.", 90)]
		public static int BONEDANCER_HEALER_PET_HEAL_THRESHOLD;

		/// <summary>
		/// Expand the Wild Minion RA to also improve crit chance for ranged and spell attacks?
		/// </summary>
		[ServerProperty("npc", "expand_wild_minion", "Expand the Wild Minion RA to also improve crit chance for ranged and spell attacks?", false)]
		public static bool EXPAND_WILD_MINION;

		#endregion

		#region PVP / RVR

		/// <summary>
		/// PvP Immunity Timer - Killed by Mobs
		/// </summary>
		[ServerProperty("pvp", "Timer_Killed_By_Mob", "Immunity Timer When player killed in PvP, in seconds", 10)]
		public static int TIMER_KILLED_BY_MOB;

		/// <summary>
		/// PvP Immunity Timer - Killed by Player
		/// </summary>
		[ServerProperty("pvp", "Timer_Killed_By_Player", "Immunity Timer When player killed in PvP, in seconds", 10)]
		public static int TIMER_KILLED_BY_PLAYER;

		/// <summary>
		/// PvP Immunity Timer - Region Changed
		/// </summary>
		[ServerProperty("pvp", "Timer_Region_Changed", "Immunity Timer when player changes regions, in seconds", 10)]
		public static int TIMER_REGION_CHANGED;

		/// <summary>
		/// PvP Immunity Timer - Game Entered
		/// </summary>
		[ServerProperty("pvp", "Timer_Game_Entered", "Immunity Timer when player enters the game, in seconds", 10)]
		public static int TIMER_GAME_ENTERED;

		/// <summary>
		/// PvP Immunity Timer - Teleport
		/// </summary>
		[ServerProperty("pvp", "Timer_PvP_Teleport", "Immunity Timer when player teleports within the same region, in seconds", 10)]
		public static int TIMER_PVP_TELEPORT;

		/// <summary>
		/// Time after a relic lost in nature is returning to his ReturnRelicPad pad
		/// </summary>
		[ServerProperty("pvp", "Relic_Return_Time", "A lost relic will automatically returns to its defined point, in seconds", 20 * 60)]
		public static int RELIC_RETURN_TIME;

		/// <summary>
		/// Allow all realms access to DF
		/// </summary>
		[ServerProperty("pvp", "allow_all_realms_df", "Should we allow all realms access to DF", false)]
		public static bool ALLOW_ALL_REALMS_DF;

		/// <summary>
		/// Allow Bounty Points to be gained in Battlegrounds
		/// </summary>
		[ServerProperty("pvp", "allow_bps_in_bgs", "Allow bounty points to be gained in battlegrounds", false)]
		public static bool ALLOW_BPS_IN_BGS;

		/// <summary>
		/// This if the server battleground zones are open to players
		/// </summary>
		[ServerProperty("pvp", "bg_zones_open", "Can the players teleport to battleground", true)]
		public static bool BG_ZONES_OPENED;

		/// <summary>
		/// Message to display to player if BG zones are closed
		/// </summary>
		[ServerProperty("pvp", "bg_zones_closed_message", "Message to display to player if BG zones are closed", "The battlegrounds are not open on this server.")]
		public static string BG_ZONES_CLOSED_MESSAGE;

		/// <summary>
		/// How many players are required on the relic pad to trigger the pillar?
		/// </summary>
		// 8 for the offline game (was 16; owner 2026-10-15). Players, gamebots and a player's companion bots count.
		[ServerProperty("pvp", "relic_players_required_on_pad", "How many players are required on the relic pad to trigger the pillar?", 8)]
		public static int RELIC_PLAYERS_REQUIRED_ON_PAD;

		/// <summary>
		/// Ignore too long outcoming packet or not
		/// </summary>
		[ServerProperty("pvp", "enable_minotaur_relics", "Shall we enable Minotaur Relics ?", false)]
		public static bool ENABLE_MINOTAUR_RELICS;

		/// <summary>
		/// Enable WarMap manager
		/// </summary>
		[ServerProperty("pvp", "enable_warmapmgr", "Shall we enable the WarMap manager ?", false)]
		public static bool ENABLE_WARMAPMGR;
		
		/// <summary>
		/// Toggle con loss on PvP server type
		/// </summary>
		[ServerProperty("pvp", "pvp_death_con_loss", "Loose con on pvp death on PvP servertype", true)]
		public static bool PVP_DEATH_CON_LOSS;

		/// <summary>
		/// PvP Realm Timer. # of minutes an account must wait to change realms after pvp combat. 0 disables the timer
		/// </summary>
		[ServerProperty("pvp", "pvp_realm_timer_minutes", "# of minutes an account must wait to change realms after PvP combat. 0 disables the timer", 0)]
		public static int PVP_REALM_TIMER_MINUTES;

		/// <summary>
		/// Whether releasing in a battleground should teleport the player to the portal keep
		/// </summary>
		[ServerProperty("pvp", "bg_release_to_portal_keep", "Whether releasing in a battleground should teleport the player to the portal keep", false)]
		public static bool BG_RELEASE_TO_PORTAL_KEEP;

		#endregion

		#region Daily / Weekly / Monthly / Beetle Quest
		/// <summary>
		/// The value of Daily Quest realmpoints reward
		/// </summary>
		[ServerProperty("quest", "daily_rvr_reward", "Daily Quest realmpoints reward", 0)]
		public static int DAILY_RVR_REWARD;
		
		/// <summary>
		/// The value of Weekly Quest realmpoints reward
		/// </summary>
		[ServerProperty("quest", "weekly_rvr_reward", "Weekly Quest realmpoints reward", 0)]
		public static int WEEKLY_RVR_REWARD;
		
		/// <summary>
		/// The value of Monthly Quest realmpoints reward
		/// </summary>
		[ServerProperty("quest", "monthly_rvr_reward", "Monthly Quest realmpoints reward", 0)]
		public static int MONTHLY_RVR_REWARD;
		
		/// <summary>
		/// The value of Hardcore RvR Quest realmpoints reward
		/// </summary>
		[ServerProperty("quest", "hardcore_rvr_reward", "Hardcore Quest realmpoints reward", 0)]
		public static int HARDCORE_RVR_REWARD;
		
		/// <summary>
		/// The value of Beetle RvR Quest realmpoints reward
		/// </summary>
		[ServerProperty("quest", "beetle_rvr_reward", "Beetle Quest realmpoints reward", 0)]
		public static int BEETLE_RVR_REWARD;
		#endregion

		#region KEEPS
		/// <summary>
		/// The number of players needed for claiming
		/// </summary>
		[ServerProperty("keeps", "claim_num", "Players Needed For Claim - Edit this to change the amount of players required to claim a keep, towers are half this amount", 8)]
		public static int CLAIM_NUM;

		/// <summary>
		/// Use Keep Balancing
		/// </summary>
		[ServerProperty("keeps", "use_keep_balancing", "Set to true if you want keeps to be higher level in NF the less you have, and lower level the more you have", false)]
		public static bool USE_KEEP_BALANCING;

		/// <summary>
		/// Use Live Keep Bonuses
		/// </summary>
		[ServerProperty("keeps", "use_live_keep_bonuses", "Set to true if you want to use the live keeps bonuses, for example 3% extra xp", false)]
		public static bool USE_LIVE_KEEP_BONUSES;

		/// <summary>
		/// Use Supply Chain
		/// </summary>
		[ServerProperty("keeps", "use_supply_chain", "Set to true if you want to use the live supply chain for keep teleporting, set to false to allow teleporting to any keep that your realm controls (and towers)", false)]
		public static bool USE_SUPPLY_CHAIN;

		/// <summary>
		/// Load Hookpoints
		/// </summary>
		[ServerProperty("keeps", "load_hookpoints", "Load keep hookpoints", true)]
		public static bool LOAD_HOOKPOINTS;

		/// <summary>
		/// Load Keeps
		/// </summary>
		[ServerProperty("keeps", "load_keeps", "Load keeps", true)]
		public static bool LOAD_KEEPS = true;

		/// <summary>
		/// The level keeps start at when not claimed - please note only levels 4 and 5 are supported correctly at this time
		/// </summary>
		[ServerProperty("keeps", "starting_keep_level", "The level an unclaimed keep starts at.", 4)]
		public static int STARTING_KEEP_LEVEL;

		/// <summary>
		/// The level keeps start at when claimed - please note only levels 4 and 5 are supported correctly at this time
		/// </summary>
		[ServerProperty("keeps", "starting_keep_claim_level", "The level a claimed keep starts at.", 5)]
		public static int STARTING_KEEP_CLAIM_LEVEL;

		/// <summary>
		/// The maximum keep level - please note only levels 4 and 5 are supported correctly at this time
		/// </summary>
		[ServerProperty("keeps", "max_keep_level", "The maximum keep level.", 5)]
		public static int MAX_KEEP_LEVEL;

		/// <summary>
		/// Enable the keep upgrade timer to slowly raise keep levels
		/// </summary>
		[ServerProperty("keeps", "enable_keep_upgrade_timer", "Enable the keep upgrade timer to slowly raise keep levels?", false)]
		public static bool ENABLE_KEEP_UPGRADE_TIMER;

		/// <summary>
		/// Define toughness for keep and tower walls: 100 is 100% player's damages inflicted.
		/// </summary>
		[ServerProperty("keeps", "set_structures_toughness", "This value is % of total damages inflicted to walls. (100=full damages)", 100)]
		public static int SET_STRUCTURES_TOUGHNESS;

		/// <summary>
		/// Define toughness for keep doors: 100 is 100% player's damages inflicted.
		/// </summary>
		[ServerProperty("keeps", "set_keep_door_toughness", "This value is % of total damages inflicted to level 1 door. (100=full damages)", 100)]
		public static int SET_KEEP_DOOR_TOUGHNESS;

		/// <summary>
		/// Define toughness for tower doors: 100 is 100% player's damages inflicted.
		/// </summary>
		[ServerProperty("keeps", "set_tower_door_toughness", "This value is % of total damages inflicted to level 1 door. (100=full damages)", 100)]
		public static int SET_TOWER_DOOR_TOUGHNESS;

		/// <summary>
		/// Allow player pets to attack keep walls
		/// </summary>
		[ServerProperty("keeps", "structures_allowpetattack", "Allow player pets to attack keep and tower walls?", true)]
		public static bool STRUCTURES_ALLOWPETATTACK;

		/// <summary>
		/// Allow player pets to attack keep and tower doors
		/// </summary>
		[ServerProperty("keeps", "doors_allowpetattack", "Allow player pets to attack keep and tower doors?", true)]
		public static bool DOORS_ALLOWPETATTACK;

		/// <summary>
		/// Multiplier used in determining RP reward for claiming towers.
		/// </summary>
		[ServerProperty("keeps", "tower_rp_claim_multiplier", "Integer multiplier used in determining RP reward for claiming towers.", 100)]
		public static int TOWER_RP_CLAIM_MULTIPLIER;

		/// <summary>
		/// Multiplier used in determining RP reward for keeps.
		/// </summary>
		[ServerProperty("keeps", "keep_rp_claim_multiplier", "Integer multiplier used in determining RP reward for claiming keeps.", 1000)]
		public static int KEEP_RP_CLAIM_MULTIPLIER;

		/// <summary>
		/// Turn on logging of keep captures
		/// </summary>
		[ServerProperty("keeps", "log_keep_captures", "Turn on logging of keep captures?", false)]
		public static bool LOG_KEEP_CAPTURES;

		/// <summary>
		/// Base RP value of a keep
		/// </summary>
		[ServerProperty("keeps", "keep_rp_base", "Base RP value of a keep", 0)] // Previously 4500.
		public static int KEEP_RP_BASE;

		/// <summary>
		/// Base RP value of a tower
		/// </summary>
		[ServerProperty("keeps", "tower_rp_base", "Base RP value of a tower", 0)] // Previously 500.
		public static int TOWER_RP_BASE;

		/// <summary>
		/// The number of seconds from last kill the this lord is worth no RP
		/// </summary>
		[ServerProperty("keeps", "lord_rp_worth_seconds", "The number of seconds from last kill the this lord is worth no RP.", 300)]
		public static int LORD_RP_WORTH_SECONDS;

		/// <summary>
		/// Multiplier used to add or subtract RP worth based on keep level difference from 50.
		/// </summary>
		[ServerProperty("keeps", "keep_rp_multiplier", "Integer multiplier used to increase/decrease RP worth based on keep level difference from 50.", 0)] // Previously 50.
		public static int KEEP_RP_MULTIPLIER;

		/// <summary>
		/// Multiplier used to add or subtract RP worth based on tower level difference from 50.
		/// </summary>
		[ServerProperty("keeps", "tower_rp_multiplier", "Integer multiplier used to increase/decrease RP worth based on tower level difference from 50.", 0)] // Previously 50.
		public static int TOWER_RP_MULTIPLIER;

		/// <summary>
		/// Multiplier used to add or subtract RP worth based on upgrade level > 1
		/// </summary>
		[ServerProperty("keeps", "upgrade_multiplier", "Integer multiplier used to increase/decrease RP worth based on upgrade level (0..10)", 100)]
		public static int UPGRADE_MULTIPLIER;

		/// <summary>
		/// Multiplier used to determine keep level changes when balancing.  For each keep above or below normal multiply by this.
		/// </summary>
		[ServerProperty("keeps", "keep_balance_multiplier", "Multiplier used to determine keep level changes when balancing.  For each keep above or below normal multiply by this.", 1.0)]
		public static double KEEP_BALANCE_MULTIPLIER;

		/// <summary>
		/// Multiplier used to determine keep level changes when balancing.  For each keep above or below normal multiply by this.
		/// </summary>
		[ServerProperty("keeps", "tower_balance_multiplier", "Multiplier used to determine tower level changes when balancing.  For each keep above or below normal multiply by this.", 0.15)]
		public static double TOWER_BALANCE_MULTIPLIER;

		/// <summary>
		/// Balance Towers and Keeps separately
		/// </summary>
		[ServerProperty("keeps", "balance_towers_separate", "Balance Towers and Keeps separately?", true)]
		public static bool BALANCE_TOWERS_SEPARATE;

		/// <summary>
		/// Multiplier used to determine keep guard levels.  This is applied to the bonus level (usually 4) and added after balance adjustments.
		/// </summary>
		[ServerProperty("keeps", "keep_guard_level_multiplier", "Multiplier used to determine keep guard levels.  This is applied to the bonus level (usually 4) and added after balance adjustments.", 1.6)]
		public static double KEEP_GUARD_LEVEL_MULTIPLIER;

		/// <summary>
		/// Modifier used to adjust damage for pets on keep components
		/// </summary>
		[ServerProperty("keeps", "pet_damage_multiplier", "Modifier used to adjust damage for pets classes.", 1.0)]
		public static double PET_DAMAGE_MULTIPLIER;

		/// <summary>
		/// Modifier used to adjust damage for pet spam classes (currently animist and theurgist) on keep components
		/// </summary>
		[ServerProperty("keeps", "pet_spam_damage_multiplier", "Modifier used to adjust damage for pet spam classes (currently animist and theurgist).", 1.0)]
		public static double PET_SPAM_DAMAGE_MULTIPLIER;

		/// <summary>
		/// Multiplier used to determine keep guard levels.  This is applied to the bonus level (usually 4) and added after balance adjustments.
		/// </summary>
		[ServerProperty("keeps", "tower_guard_level_multiplier", "Multiplier used to determine tower guard levels.  This is applied to the bonus level (usually 4) and added after balance adjustments.", 1.0)]
		public static double TOWER_GUARD_LEVEL_MULTIPLIER;

		/// <summary>
		/// Keeps to load. 0 for Old Keeps, 1 for new keeps, 2 for both.
		/// </summary>
		[ServerProperty("keeps", "use_new_keeps", "Appearance Keeps Components to load. 0 for Old Appearance Keeps Components, 1 for New Appearance Keeps Components. 2 is no longer used but load 0 for compatibility.", 0)]
		public static int USE_NEW_KEEPS;

		/// <summary>
		/// Should guards loaded from db be equipped by Keepsystem? (false=load equipment from db)
		/// </summary>
		[ServerProperty("keeps", "autoequip_guards_loaded_from_db", "Should guards loaded from db be equipped by Keepsystem? (false=load equipment from db)", true)]
		public static bool AUTOEQUIP_GUARDS_LOADED_FROM_DB;

		/// <summary>
		/// Should guards loaded from db be modeled by Keepsystem? (false=load from db)
		/// </summary>
		[ServerProperty("keeps", "automodel_guards_loaded_from_db", "Should guards loaded from db be modeled by Keepsystem? (false=load from db)", true)]
		public static bool AUTOMODEL_GUARDS_LOADED_FROM_DB;

		/// <summary>
		/// Are unclaimed keeps considered the enemy in PvP mode?
		/// </summary>
		[ServerProperty("keeps", "pvp_unclaimed_keeps_enemy", "Are unclaimed keeps considered the enemy in PvP mode?", false)]
		public static bool PVP_UNCLAIMED_KEEPS_ENEMY;

		/// <summary>
		/// Grace period in minutes to allow relog near enemy structure after link death
		/// </summary>
		[ServerProperty("keeps", "NearKeepRelogGracePeriod", "The grace period in minutes, to allow to relog near an enemy structure.", 3)]
		public static int NEAR_KEEP_RELOG_GRACE_PERIOD;

		/// <summary>
		/// Should players that exceed BG level cap be moved out of BG when logging in?
		/// </summary>
		[ServerProperty("keeps", "teleport_login_bg_level_exceeded", "Should players that exceed BG level cap be moved out of BG when logging in?", true)]
		public static bool TELEPORT_LOGIN_BG_LEVEL_EXCEEDED;

		/// <summary>
		/// Do you allowed player to climb towers?
		/// </summary>
		[ServerProperty("keeps", "allow_tower_climb", "Do you allowed player to climb towers? Set True for yes, False for not.", false)]
		public static bool ALLOW_TOWER_CLIMB;

		/// <summary>
		/// Keep guard heal when a target is below what percentage of their health?
		/// </summary>
		[ServerProperty("keeps", "keep_heal_threshold", "Keep guards heal targets whose health falls below this value.", 60)]
		public static int KEEP_HEAL_THRESHOLD;
		
		/// <summary>
		/// Base Value to use when auto-setting STR stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_str_base", "Base Value to use when auto-setting STR stat. ", (short)20)]
		public static short GUARD_AUTOSET_STR_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting STR stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_str_multiplier", "Multiplier to use when auto-setting STR stat.  Multiplied by 10 when used.", 0.7)]
		public static double GUARD_AUTOSET_STR_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting CON stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_con_base", "Base Value to use when auto-setting CON stat. ", (short)30)]
		public static short GUARD_AUTOSET_CON_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting CON stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_con_multiplier", "Multiplier to use when auto-setting CON stat. ", 0.0)]
		public static double GUARD_AUTOSET_CON_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting QUI stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_qui_base", "Base Value to use when auto-setting qui stat. ", (short)40)]
		public static short GUARD_AUTOSET_QUI_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting QUI stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_qui_multiplier", "Multiplier to use when auto-setting QUI stat. ", 0.0)]
		public static double GUARD_AUTOSET_QUI_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting DEX stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_dex_base", "Base Value to use when auto-setting DEX stat. ", (short)1)]
		public static short GUARD_AUTOSET_DEX_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting DEX stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_dex_multiplier", "Multiplier to use when auto-setting DEX stat. ", 1.0)]
		public static double GUARD_AUTOSET_DEX_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting INT stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_int_base", "Base Value to use when auto-setting INT stat. ", (short)30)]
		public static short GUARD_AUTOSET_INT_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting INT stat.
		/// </summary>
		[ServerProperty("keeps", "guard_autoset_int_multiplier", "Multiplier to use when auto-setting INT stat. ", 1.0)]
		public static double GUARD_AUTOSET_INT_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting STR stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_str_base", "Base Value to use when auto-setting STR stat. ", (short)20)]
		public static short LORD_AUTOSET_STR_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting STR stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_str_multiplier", "Multiplier to use when auto-setting STR stat.  Multiplied by 10 when used.", 0.8)]
		public static double LORD_AUTOSET_STR_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting CON stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_con_base", "Base Value to use when auto-setting CON stat. ", (short)30)]
		public static short LORD_AUTOSET_CON_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting CON stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_con_multiplier", "Multiplier to use when auto-setting CON stat. ", 0)]
		public static double LORD_AUTOSET_CON_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting QUI stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_qui_base", "Base Value to use when auto-setting qui stat. ", (short)60)]
		public static short LORD_AUTOSET_QUI_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting QUI stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_qui_multiplier", "Multiplier to use when auto-setting QUI stat. ", 0)]
		public static double LORD_AUTOSET_QUI_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting DEX stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_dex_base", "Base Value to use when auto-setting DEX stat. ", (short)2)]
		public static short LORD_AUTOSET_DEX_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting DEX stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_dex_multiplier", "Multiplier to use when auto-setting DEX stat. ", 2.0)]
		public static double LORD_AUTOSET_DEX_MULTIPLIER;

		/// <summary>
		/// Base Value to use when auto-setting INT stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_int_base", "Base Value to use when auto-setting INT stat. ", (short)30)]
		public static short LORD_AUTOSET_INT_BASE;

		/// <summary>
		/// Multiplier to use when auto-setting INT stat.
		/// </summary>
		[ServerProperty("keeps", "lord_autoset_int_multiplier", "Multiplier to use when auto-setting INT stat. ", 1.0)]
		public static double LORD_AUTOSET_INT_MULTIPLIER;

		/// <summary>
		/// Respawn time for keep guards in minutes.
		/// </summary>
		[ServerProperty("keeps", "guard_respawn", "Respawn time for keep guards in minutes.", 15)]
		public static int GUARD_RESPAWN;

		/// <summary>
		/// Respawn variance for keep guards in minutes.
		/// </summary>
		[ServerProperty("keeps", "guard_respawn_variance", "Respawn variance for keep guards in minutes.", 10)]
		public static int GUARD_RESPAWN_VARIANCE;
		
		/// <summary>
		/// Doors base health value.
		/// </summary>
		[ServerProperty("keeps", "keep_doors_base_health", "Keep doors base health. Will be multiplied by the keep's base level.", 200)]
		public static int KEEP_DOORS_BASE_HEALTH;

		/// <summary>
		/// Doors health upgrade modifier.
		/// </summary>
		[ServerProperty("keeps", "keep_doors_health_upgrade_modifier", "The modifier used to calculate the extra amount of door health per upgrade.", 1.0)]
		public static double KEEP_DOORS_HEALTH_UPGRADE_MODIFIER;

		/// <summary>
		/// Components base health value.
		/// </summary>
		[ServerProperty("keeps", "keep_components_base_health", "Keep components base health. Will be multiplied by the keep's base level.", 200)]
		public static int KEEP_COMPONENTS_BASE_HEALTH;

		/// <summary>
		/// Components health upgrade modifier.
		/// </summary>
		[ServerProperty("keeps", "keep_components_health_upgrade_modifier", "The modifier used to calculate the extra amount of component health per upgrade.", 1.0)]
		public static double KEEP_COMPONENTS_HEALTH_UPGRADE_MODIFIER;

		/// <summary>
		/// Relic gates health value.
		/// </summary>
		[ServerProperty("keeps", "relic_doors_health", "Relic gates health value", 180000)]
		public static int RELIC_DOORS_HEALTH;

		#endregion

		#region PVE / TOA
		/// <summary>
		/// Allow currency exchange?
		/// </summary>
		[ServerProperty("pve", "currency_exchange_allow", "Allow players to exchange aurulite/blood seals/glass/scales by giving them to a merchant who takes the desired currency, i.e. you can give glass to dragon merchants to get scales?", true)]
		public static bool CURRENCY_EXCHANGE_ALLOW;

		/// <summary>
		/// Currency exchange values, i.e. you need 10 aurulite to get 1 dragon scale.
		/// </summary>
		[ServerProperty("pve", "currency_exchange_values", "Value of special currencies for currency conversion.", "atlanteanglass|1;aurulite|4;dragonscales|40;BloodSeal|1000")]
		public static string CURRENCY_EXCHANGE_VALUES;

		/// <summary>
		/// Allow currencies to be exchanged for BPs?
		/// </summary>
		[ServerProperty("pve", "bp_exchange_allow", "Allow players to exchange special currencies for BPs by giving them to BP merchants?", true)]
		public static bool BP_EXCHANGE_ALLOW;

		/// <summary>
		/// BP exchange values.  Each item grants x BPs.
		/// </summary>
		[ServerProperty("pve", "bp_exchange_values", "Value of special currencies in BPs.", "atlanteanglass|1;aurulite|4;dragonscales|40;BloodSeal|1000")]
		public static string BP_EXCHANGE_VALUES;

		/// <summary>
		/// Initial percent chance of a mob BAFing for a single attacker
		/// </summary>
		[ServerProperty("pve", "baf_initial_chance", "Percent chance for a mob to bring a friend when attacked by a single attacker.  Each multiples of 100 guarantee an add, so a cumulative chance of 250% guarantees two adds with a 50% chance of a third.", 0)]
		public static int BAF_INITIAL_CHANCE;

		/// <summary>
		/// Added percent chance of a mob BAFing for each attacker past the first
		/// </summary>
		[ServerProperty("pve", "baf_additional_chance", "Percent chance for a mob to bring a friend for each additional attacker.  Each multiples of 100 guarantee an add, so a cumulative chance of 250% guarantees two adds with a 50% chance of a third.", 50)]
		public static int BAF_ADDITIONAL_CHANCE;

		/// <summary>
		/// Do BAF mobs attack the player who pulled?
		/// </summary>
		[ServerProperty("pve", "baf_mobs_attack_puller", "Do mobs brought by friends only attack the character who pulled them?  If false, mobs attack random players near the puller.", false)]
		public static bool BAF_MOBS_ATTACK_PULLER;

		/// <summary>
		/// Do BAF mobs attack characters in the same BG as the pulling character?
		/// </summary>
		[ServerProperty("pve", "baf_mobs_attack_bg_members", "Do mobs brought by friends attack random nearby players in the puller's battlegroup?  If false, mobs only attack characters in the pulling player's group.", false)]
		public static bool BAF_MOBS_ATTACK_BG_MEMBERS;

		/// <summary>
		/// Is the number of mobs added by BAF based on the number of nearby players in the puller's BG?
		/// </summary>
		[ServerProperty("pve", "baf_mobs_count_bg_members", "Is the number of mobs brought by a friend based on the number of nearby players in the pulling player's battlegroup?  If false, the number of mobs brought is determined by the number of players in the pulling player's group.", false)]
		public static bool BAF_MOBS_COUNT_BG_MEMBERS;			

		/// <summary>
		/// Adjustment to missrate per number of attackers
		/// </summary>
		[ServerProperty("pve", "missrate_reduction_per_attackers", "Adjustment to missrate per number of attackers", 0)]
		public static int MISSRATE_REDUCTION_PER_ATTACKERS;

		/// <summary>
		/// Spell damage reduction multiplier based on hitchance below 55%.
		/// Default is 4.3, which will produce the minimum 1 damage at a 33% chance to hit
		/// Lower numbers reduce damage reduction
		/// </summary>
		[ServerProperty("pve", "spell_hitchance_damage_reduction_multiplier", "Spell damage reduction multiplier based on hitchance if < 55%. Lower numbers reduce damage reduction.", 4.3)]
		public static double SPELL_HITCHANCE_DAMAGE_REDUCTION_MULTIPLIER;

		/// <summary>
		/// TOA Artifact XP rate
		/// </summary>
		[ServerProperty("pve", "artifact_xp_rate", "Adjust the rate at which all artifacts gain xp.  Higher numbers mean slower XP gain. XP / this = result", 350)]
		public static int ARTIFACT_XP_RATE;

		/// <summary>
		/// TOA Scroll drop rate
		/// </summary>
		[ServerProperty("pve", "scroll_drop_rate", "Adjust the drop rate (percent chance) for scrolls.", 25)]
		public static int SCROLL_DROP_RATE;

		/// <summary>
		/// Max camp bonus
		/// </summary>
		[ServerProperty("pve", "max_camp_bonus", "Max camp bonus, 0.55 = 55%", 0.55)]
		public static double MAX_CAMP_BONUS;

		/// <summary>
		/// Max camp bonus
		/// </summary>
		[ServerProperty("pve", "max_dungeon_camp_bonus", "Max camp bonus, 0.55 = 55%", 0.66)]
		public static double MAX_DUNGEON_CAMP_BONUS;

		/// <summary>
		/// Minimum privilege level to be able to enter Atlantis through teleporters.
		/// </summary>
		[ServerProperty("pve", "atlantis_teleport_plvl", "Set the minimum privilege level required to enter Atlantis zones.", 2)]
		public static int ATLANTIS_TELEPORT_PLVL;
		
		/// <summary>
		/// Time Before Adventure Wings Instances Destroy when Empty
		/// </summary>
		[ServerProperty("pve", "adventurewing_time_to_destroy", "Set the time before Instanced Adventure Wings (Catacombs) are destroy when empty (in minutes).", 5)]
		public static int ADVENTUREWING_TIME_TO_DESTROY;

		/// <summary>
		/// Aurulite Loot Generator Drop Base Chance
		/// </summary>
		[ServerProperty("pve", "lootgenerator_aurulite_base_chance", "Base chance for dropping Aurulite using Loot Generator.", 10)]
		public static int LOOTGENERATOR_AURULITE_BASE_CHANCE;

		/// <summary>
		/// Aurulite Loot Generator Amount Ratio
		/// </summary>
		[ServerProperty("pve", "lootgenerator_aurulite_amount_ratio", "Modify the final count of Aurulite Loot Generator drop. (TotalCount * lootgenerator_aurulite_amount_ratio)", 0.5)]
		public static double LOOTGENERATOR_AURULITE_AMOUNT_RATIO;
		
		/// <summary>
		/// Aurulite Loot Generator Named Boost Count
		/// </summary>
		[ServerProperty("pve", "lootgenerator_aurulite_named_count", "Increase count of Aurulite Loot Generator drop for Named mobs. (count * lootgenerator_aurulite_named_count)", 1.5)]
		public static double LOOTGENERATOR_AURULITE_NAMED_COUNT;
				
		/// <summary>
		/// Atlantean Glass Loot Generator Drop Base Chance
		/// </summary>
		[ServerProperty("pve", "lootgenerator_atlanteanglass_base_chance", "Base chance for dropping Atlantean Glass using Loot Generator.", 20)]
		public static int LOOTGENERATOR_ATLANTEANGLASS_BASE_CHANCE;
		
		/// <summary>
		/// Atlantean Glass Loot Generator Named Boost Count
		/// </summary>
		[ServerProperty("pve", "lootgenerator_atlanteanglass_named_count", "Increase count of Atlantean Glass Loot Generator drop for Named mobs. (count * lootgenerator_atlanteanglass_named_count)", 1.5)]
		public static double LOOTGENERATOR_ATLANTEANGLASS_NAMED_COUNT;
		
		/// <summary>
		/// Dragon Scales Loot Generator Drop Base Chance
		/// </summary>
		[ServerProperty("pve", "lootgenerator_dragonscales_base_chance", "Base chance for dropping Dragon Scales using Loot Generator.", 15)]
		public static int LOOTGENERATOR_DRAGONSCALES_BASE_CHANCE;
		
		/// <summary>
		/// Dragon Scales Loot Generator Named Boost Count
		/// </summary>
		[ServerProperty("pve", "lootgenerator_dragonscales_named_count", "Multiplier for number of scales dropped from named mobs, including named dragons.  Must be an integer.", 2)]
		public static int LOOTGENERATOR_DRAGONSCALES_NAMED_COUNT;	
		
		/// <summary>
		/// Dreaded Seals Loot Generator Starting Level
		/// </summary>
		[ServerProperty("pve", "lootgenerator_dreadedseals_starting_level", "Mob level to start dropping Dreaded Glowing Seals", 25)]
		public static int LOOTGENERATOR_DREADEDSEALS_STARTING_LEVEL;

		/// <summary>
		/// Dreaded Seals Loot Generator Drop Chance Per Level
		/// </summary>
		[ServerProperty("pve", "lootgenerator_dreadedseals_drop_chance_per_level", "Increase in Dreaded Glowing Seal drop chance per level, in hundredths of a percent.", 25)]
		public static int LOOTGENERATOR_DREADEDSEALS_DROP_CHANCE_PER_LEVEL;

		/// <summary>
		/// Dreaded Seals Loot Generator Base Chance
		/// </summary>
		[ServerProperty("pve", "lootgenerator_dreadedseals_base_chance", "Base chance to drop a Dreaded Seal, in hundredths of a percent", 25)]
		public static int LOOTGENERATOR_DREADEDSEALS_BASE_CHANCE;

		/// <summary>
		/// Dreaded Seals Loot Generator Named Boost Chance
		/// </summary>
		[ServerProperty("pve", "lootgenerator_dreadedseals_named_chance", "Increase chance of Dreaded Seals Loot Generator drop for Named mobs. (count * lootgenerator_dreadedseals_named_chance)", 1.5)]
		public static double LOOTGENERATOR_DREADEDSEALS_NAMED_CHANCE;

		/// <summary>
		/// Dreaded Seal multipliers by level
		/// </summary>
		[ServerProperty("pve", "dreadedseals_level_multiplier", "Level based multipliers for RPs and BPs awarded when turning in dreaded seals.", "21|2;26|3;31|5;36|30;41|70;46|150;50|300")]
		public static string DREADEDSEALS_LEVEL_MULTIPLIER;

		/// <summary>
		/// Dreaded Seal RP values before level multiplier and BP rate
		/// </summary>
		[ServerProperty("pve", "dreadedseals_bp_values", "BP values of dreaded seal types before level multiplier and BP rate is applied.", "glowing_dreaded_seal|3.334;sanguine_dreaded_seal|3.334;lambent_dreaded_seal|33.334;lambent_dreaded_seal2|33.334;fulgent_dreaded_seal|166.667;effulgent_dreaded_seal|833.334")]
		public static string DREADEDSEALS_BP_VALUES;

		/// <summary>
		/// Dreaded Seal BP values before level multiplier and RP rate
		/// </summary>
		[ServerProperty("pve", "dreadedseals_rp_values", "RP values of dreaded seal types before level multiplier and RP rate is applied.", "glowing_dreaded_seal|10;sanguine_dreaded_seal|10;lambent_dreaded_seal|100;lambent_dreaded_seal2|100;fulgent_dreaded_seal|500;effulgent_dreaded_seal|2500")]
		public static string DREADEDSEALS_RP_VALUES;

		/// <summary>
		/// PvE Experience Loss Start Level
		/// </summary>
		[ServerProperty("pve", "pve_exp_loss_level", "Which level should players killed in PvE start losing experience?", (byte)6)]
		public static byte PVE_EXP_LOSS_LEVEL;

		// After OpenDAoC-Core e10348f31: a saved gravestone (it survives a restart) is removed this many days after it was made.
		[ServerProperty("world", "gravestone_decay_time", "Minimum time (days) before a gravestone is removed from the world. 0 means no gravestones will be removed.", 7)]
		public static int GRAVESTONE_DECAY_TIME;

		/// <summary>
		/// PvE Conn Loss Start Level
		/// </summary>
		[ServerProperty("pve", "pve_con_loss_level", "Which level should players killed in PvE start losing constitution?", (byte)6)]
		public static byte PVE_CON_LOSS_LEVEL;

		#endregion

		#region HOUSING
		/// <summary>
		/// Maximum number of houses supported on this server.  Limits the size of the housing array used for updates
		/// </summary>
		[ServerProperty("housing", "max_num_houses", "Max number of houses supported on this server.", 5000)]
		public static int MAX_NUM_HOUSES;

		/// <summary>
		/// The starting NPCTemplate ID to use for housing NPC's
		/// </summary>
		[ServerProperty("housing", "housing_starting_npctemplate_id", "The starting NPCTemplate ID to use for housing NPC's", 500)]
		public static int HOUSING_STARTING_NPCTEMPLATE_ID;

		/// <summary>
		/// Sets the max allowed items inside a house.
		/// </summary>
		[ServerProperty("housing", "max_indoor_house_items", "Max number of items allowed inside a players house.", 40)]
		public static int MAX_INDOOR_HOUSE_ITEMS;

		/// <summary>
		/// Max outdoor items.  If Outdoor is increased past 30 they vanish. It seems to be hardcoded in client
		/// </summary>
		[ServerProperty("housing", "max_outdoor_house_items", "Max number of items allowed in a players garden.", 30)]
		public static int MAX_OUTDOOR_HOUSE_ITEMS;

		[ServerProperty("housing", "indoor_items_depend_on_size", "If true the max number of allowed House indoor items are set like live (40, 60, 80, 100)", true)]
		public static bool INDOOR_ITEMS_DEPEND_ON_SIZE;

		[ServerProperty("housing", "housing_rent_cottage", "Rent price for a cottage.", 20L * 100L * 100L)] // 20g
		public static long HOUSING_RENT_COTTAGE;

		[ServerProperty("housing", "housing_rent_house", "Rent price for a house.", 35L * 100L * 100L)] // 35g
		public static long HOUSING_RENT_HOUSE;

		[ServerProperty("housing", "housing_rent_villa", "Rent price for a villa.", 60L * 100L * 100L)] // 60g
		public static long HOUSING_RENT_VILLA;

		[ServerProperty("housing", "housing_rent_mansion", "Rent price for a mansion.", 100L * 100L * 100L)] // 100g
		public static long HOUSING_RENT_MANSION;

		[ServerProperty("housing", "housing_lot_price_start", "Starting lot price before per hour reductions", 95L * 1000L * 100L * 100L)] // 95p
		public static long HOUSING_LOT_PRICE_START;

		[ServerProperty("housing", "housing_lot_price_per_hour", "Lot price reduction per hour.", (long)(1.2 * 1000 * 100 * 100))] // 1.2p
		public static long HOUSING_LOT_PRICE_PER_HOUR;

		[ServerProperty("housing", "housing_lot_price_minimum", "Minimum lot price.", 300L * 100L * 100L)] // 300g
		public static long HOUSING_LOT_PRICE_MINIMUM;

		/// <summary>
		/// How often, in days, is rent due?  0 for never, negative for testing repossession
		/// </summary>
		[ServerProperty("housing", "rent_due_days", "How often, in days, is rent due?  0 for never, negative for testing repossession.", 7)]
		public static int RENT_DUE_DAYS;

		/// <summary>
		/// How often, in minutes, do we check for rent?
		/// </summary>
		[ServerProperty("housing", "rent_check_interval", "How often, in minutes, do we check for rent?", 120)]
		public static int RENT_CHECK_INTERVAL;

		/// <summary>
		/// How many rent payments can be stored in the lockbox?
		/// </summary>
		[ServerProperty("housing", "rent_lockbox_payments", "How many rent payments can be stored in the lockbox?", 4)]
		public static int RENT_LOCKBOX_PAYMENTS;

		/// <summary>
		/// The worth of 1 (one) bounty point in gold (e.g. 1 bp = 1g -> 10000, 1bp = 10g -> 100000)
		/// </summary>
		[ServerProperty("housing", "rent_bounty_point_to_gold", "The worth of 1 (one) bounty point in gold (e.g. 1 bp = 1g -> 10000, 1bp = 10g -> 100000)", 10000)]
		public static long RENT_BOUNTY_POINT_TO_GOLD;

		/// <summary>
		/// Do housing consignment merchants use BP instead of money?
		/// </summary>
		[ServerProperty("housing", "consignment_use_bp", "If true the housing consignment merchants use BP instead of money.", false)]
		public static bool CONSIGNMENT_USE_BP;

		/// <summary>
		/// Enable consignment merchants and market cache
		/// </summary>
		[ServerProperty("housing", "market_enabled", "If true the market explorers are enabled and the cache is initialized on server start.", true)]
		public static bool MARKET_ENABLED;

		/// <summary>
		/// Enable consignment merchants and market cache
		/// </summary>
		[ServerProperty("housing", "market_enable", "If true the market explorers are enabled and the cache is initialized on server start.", true)]
		public static bool MARKET_ENABLE;

		/// <summary>
		/// Enable logging of all market activity
		/// </summary>
		[ServerProperty("housing", "market_enable_log", "Enable debug logging of all market activity", false)]
		public static bool MARKET_ENABLE_LOG;

		/// <summary>
		/// What is the additional fee (%) charged to players using the market explorer?
		/// </summary>
		[ServerProperty("housing", "market_fee_percent", "What is the additional fee (%) charged to players using the market explorer?", 20)]
		public static int MARKET_FEE_PERCENT;

		/// <summary>
		/// How many items can the market search return?
		/// </summary>
		[ServerProperty("housing", "market_search_limit", "How many items can the market search return?", 300)]
		public static int MARKET_SEARCH_LIMIT;

		#endregion

		#region CLASSES
		/// <summary>
		/// Allow players to /train without having a trainer present
		/// </summary>
		[ServerProperty("classes", "allow_train_anywhere", "Allow players to use the /train command to open a trainer window anywhere in the world?", true)]
		public static bool ALLOW_TRAIN_ANYWHERE;

		/// <summary>
		/// Allow players to /train without having a trainer present
		/// </summary>
		[ServerProperty("classes", "allow_vault_command", "Allow players to use the /vault command to open the player's vault anywhere in the world?", false)]
		public static bool ALLOW_VAULT_COMMAND;

		/// <summary>
		/// Disable some classes from being created
		/// </summary>
		[ServerProperty("classes", "disabled_classes", "Serialized list of disabled classes, separated by semi-colon or a range with a dash (ie 1-5;7;9)", "")]
		public static string DISABLED_CLASSES;

		/// <summary>
		/// Offline DAoC 0.33 editions: the custom Hibernian Sluaghbinder class (0.33b keeps it on,
		/// 0.33 "no custom class" turns it off). Off means no player creation through the Mauler slot.
		/// Bots never play the class in either edition (owner 2026-11-17, BotCustomClassRule): no gamebot, companion,
		/// helper or hired companion is made as one, and a gamebot that already is one is kept but never logged in.
		/// </summary>
		[ServerProperty("classes", "enable_sluaghbinder", "Allow the custom Hibernian Sluaghbinder class (player creation, autonomous bots and helpers). False leaves only the Classic + SI classes.", true)]
		public static bool ENABLE_SLUAGHBINDER = true;

		/// <summary>
		/// Living Server 1.2 era builds (docs/ERA-BUILDS-1.2.md, WP2): the launcher's + Lv.1, + Lv.20 and + Lv.50 batches (and
		/// AutonomousBotIdentityGenerator.Generate) draw each new gamebot's class from its realm's 2003 share
		/// (DAoCLivingServer.Shared.EraBuildCatalog: Cleric 12% of Albion, Reaver 3%) instead of evenly. Existing bots keep their class.
		/// The launcher reads the row (a missing row is on); bots never play the custom class either way (owner 2026-11-17).
		/// </summary>
		[ServerProperty("bots", "bot_era_class_shares", "Era builds: the launcher's bot batches draw each new gamebot's class from its realm's 2003 class share (estimates from 2002-2003 forum posts; Cleric 12% of Albion, Reaver 3%) instead of evenly. Existing bots keep their class.", true)]
		public static bool BOT_ERA_CLASS_SHARES = true;

		/// <summary>
		/// The newcomer stream (issue #630; AutonomousNewcomerStream): new level-1 gamebots join a realm whose levels 1-19 run
		/// low, and as many gamebots at level 20 or above retire (kept, never deleted). Autonomous gamebots only. New keys in 1.2.3, on
		/// from the start, so no LivingServerSettingsUpgrade line (a world from an earlier build has no row).
		/// </summary>
		[ServerProperty("bots", "population_newcomers", "Server life: when a realm's autonomous gamebots in levels 1-19 fall below population_newcomer_floor, new level-1 gamebots join it (made like the launcher's + Lv.1 batch: the realm's 2003 class mix, never the custom class, no armour, a Classic or Shrouded Isles start), at most population_newcomer_per_hour an hour. Not during the startup ramp. Log: POPULATION_NEWCOMER. False: only the launcher adds bots.", true)]
		public static bool POPULATION_NEWCOMERS = true;

		[ServerProperty("bots", "population_newcomer_floor", "The newcomer stream's floor: the gamebots a realm keeps in levels 1-19 at a roster of 1,000 in that realm, scaled to the realm's real roster (75 at 1,000 is 8 at 100 and 225 at 3,000; at least 1). 0 turns newcomers off.", 75)]
		public static int POPULATION_NEWCOMER_FLOOR = 75;

		[ServerProperty("bots", "population_newcomer_per_hour", "The newcomer stream's rate: at most this many new level-1 gamebots an hour per realm at a roster of 1,000 in that realm, scaled to the realm's roster (at least 1), spaced evenly. 0 turns newcomers off.", 10)]
		public static int POPULATION_NEWCOMER_PER_HOUR = 10;

		[ServerProperty("bots", "population_retire", "Server life: each newcomer is paired with a gamebot of its realm at level 20 or above (never below 20) that retires, the least-played and least-connected first, weighted toward the longest idle (fewer bot friends, no guild role, the least recent activity), so the realm's roster stays the same size. Never a bot guild leader, a bot in your guild, on your friends list, in a group or raid, trading, with an open craft order, busy or in your sight. A retired bot keeps its character, items and coin, is never deleted and never logs in again. Log: POPULATION_RETIRED. False: newcomers add to the roster.", true)]
		public static bool POPULATION_RETIRE = true;

		/// <summary>
		/// Era builds 1.2 (docs/ERA-BUILDS-1.2.md, WP3): a new gamebot takes its lifetime build from the era data at its first
		/// load. Saved builds are never re-drawn; companions, hired companions, Epic Special Forces and buffbots are untouched.
		/// </summary>
		[ServerProperty("bots", "bot_era_builds", "Era builds: at its first load a new autonomous gamebot draws its lifetime spec build from its class's 2002-2003 builds (estimates from period forum posts; a Mind/Body Sorcerer is likelier than a Matter one), with up to 2 points of per-bot variation, and keeps it for life. A bot with a saved build keeps it; companions, hired companions, Epic Special Forces and buffbots keep the standard builds. Off: new bots get the standard builds too. Log: ERA_BUILD_ASSIGNED, ERA_BUILD_TRAINED, ERA_BUILD_CENSUS.", true)]
		public static bool BOT_ERA_BUILDS = true;

		/// <summary>Era builds 1.2 (WP3): percent of new gamebots that take one of today's class-file builds instead (the odd spec).</summary>
		[ServerProperty("bots", "bot_era_offmeta_percent", "Era builds: percent (0 to 100) of new gamebots that take one of today's class-file builds instead of an era build, the odd spec every server had. 0: era builds only. Which bots are off-meta is fixed by each bot's roster id. Needs bot_era_builds.", 10)]
		public static int BOT_ERA_OFFMETA_PERCENT = 10;

		/// <summary>
		/// Character passport (docs/CHARACTER-PASSPORT.md): while this world is hosted, friends may bring their own characters
		/// through the Passport Desk. False refuses new visitors; visitors already here can still go home.
		/// </summary>
		[ServerProperty("passport", "passport_visitors", "While hosting, let friends bring their own characters to this world (Character passport). False refuses new visitors; visitors already here can still go home.", true)]
		public static bool PASSPORT_VISITORS = true;

		/// <summary>
		/// Disable some races from being created
		/// </summary>
		[ServerProperty("classes", "disabled_races", "Serialized list of disabled races, separated by semi-colon or a range with a dash (ie 1-5;7;9)", "")]
		public static string DISABLED_RACES;

		/// <summary>
		/// Days before your elligable for a free level in Albion
		/// </summary>
		[ServerProperty("classes", "freelevel_days_albion", "days before your elligable for a free level in Albion, use -1 to deactivate", 7)]
		public static int FREELEVEL_DAYS_ALBION;
		
		/// <summary>
		/// Days before your elligable for a free level in Midgard
		/// </summary>
		[ServerProperty("classes", "freelevel_days_midgard", "days before your elligable for a free level in Midgard, use -1 to deactivate", 7)]
		public static int FREELEVEL_DAYS_MIDGARD;

		/// <summary>
		/// Days before your elligable for a free level in Hibernia
		/// </summary>
		[ServerProperty("classes", "freelevel_days_hibernia", "days before your elligable for a free level in Hibernia, use -1 to deactivate", 7)]
		public static int FREELEVEL_DAYS_HIBERNIA;

		/// <summary>
		/// Buff Range, 0 for unlimited
		/// </summary>
		[ServerProperty("classes", "buff_range", "The range that concentration buffs can last from the owner before it expires.  0 for unlimited.", 0)]
		public static int BUFF_RANGE;

		/// <summary>
		/// Allow Cata Slash Level
		/// </summary>
		[ServerProperty("classes", "allow_cata_slash_level", "Allow catacombs classes to use /level command", false)]
		public static bool ALLOW_CATA_SLASH_LEVEL;

		/// <summary>
		/// Sets the Cap for Player Turrets
		/// </summary>
		[ServerProperty("classes", "turret_player_cap_count", "Sets the cap of turrets for a Player", 12)]
		public static int TURRET_PLAYER_CAP_COUNT;

		/// <summary>
		/// Sets the Area Cap for Turrets
		/// </summary>
		[ServerProperty("classes", "turret_area_cap_count", "Sets the cap of the Area for turrets", 10)]
		public static int TURRET_AREA_CAP_COUNT;

		/// <summary>
		/// Sets the Circle of the Area to check for Turrets
		/// </summary>
		[ServerProperty("classes", "turret_area_cap_radius", "Sets the Radius which is checked for the turret area cap", 1000)]
		public static int TURRET_AREA_CAP_RADIUS;

		[ServerProperty("classes", "theurgist_pet_cap", "Maximum Theurgist pets; 0 preserves uncapped 1.65 behavior limited by cast time, power and duration", 0)]
		public static int THEURGIST_PET_CAP;

		/// <summary>
		/// Do we want to allow items to be equipped regardless of realm?
		/// </summary>
		[ServerProperty("classes", "allow_cross_realm_items", "Do we want to allow items to be equipped regardless of realm?", false)]
		public static bool ALLOW_CROSS_REALM_ITEMS;

		/// <summary>
		/// What level should /level bring you to? 0 to disable
		/// </summary>
		[ServerProperty("classes", "slash_level_target", "What level should /level bring you to? 0 is disabled.", 0)]
		public static int SLASH_LEVEL_TARGET;

		/// <summary>
		/// What level should you have on your account to be able to use /level?
		/// </summary>
		[ServerProperty("classes", "slash_level_requirement", "What level should you have on your account be able to use /level?", 50)]
		public static int SLASH_LEVEL_REQUIREMENT;

		/// <summary>
		/// Should we allow archers to be able to use arrows from their quiver?
		/// </summary>
		[ServerProperty("classes", "allow_old_archery", "Should we allow archers to be able to use arrows from their quiver?", true)]
		public static bool ALLOW_OLD_ARCHERY;

		/// <summary>
		/// Level at which res sickness starts to apply
		/// </summary>
		[ServerProperty("classes", "ress_sickness_level", "What level should ress sickness start to apply?", (byte)6)]
		public static byte RESS_SICKNESS_LEVEL;

		[ServerProperty("classes", "volley_roof_check", "Enables roof obstruction checks for Volley", false)]
		public static bool VOLLEY_ROOF_CHECK;

		[ServerProperty("classes", "ground_target_snap_max_distance", "Max snap distance for ground-targets onto a walkable surface. Failed checks invalidate the ground target. (0 = disabled)", 32f)]
		public static float GROUND_TARGET_SNAP_MAX_DISTANCE;

		#endregion

		#region SPELLS

		/// <summary>
		/// Spells-related properties
		/// </summary>
		[ServerProperty("spells", "spell_interrupt_duration", "", 3000)]
		public static int SPELL_INTERRUPT_DURATION;

		/// <summary>The self interrupt a cast starts when it reaches its half-cast point while an attacker's interrupt timer is still
		/// running (port of OpenDAoC-Core 21eda0f90; the live-like interrupt has no switch back).</summary>
		[ServerProperty("spells", "spell_self_interrupt_duration", "How long the self interrupt lasts when a cast reaches its half-cast point while interrupted", 2200)]
		public static int SPELL_SELF_INTERRUPT_DURATION;

		[ServerProperty("spells", "spell_charm_named_check", "Prevents charm spell to work on Named Mobs, 0 = disable, 1 = enable", 1)]
		public static int SPELL_CHARM_NAMED_CHECK;

		#endregion

		#region GUILDS / ALLIANCES
		/// <summary>
		/// The max number of guilds in an alliance
		/// </summary>
		[ServerProperty("guild", "alliance_max", "Max Guilds In Alliance - Edit this to change the maximum number of guilds in an alliance -1 = unlimited, 0=disable alliances", -1)]
		public static int ALLIANCE_MAX;

		/// <summary>
		/// The number of players needed to form a guild
		/// </summary>
		[ServerProperty("guild", "guild_num", "Players Needed For Guild Form - Edit this to change the amount of players required to form a guild", 8)]
		public static int GUILD_NUM;

		/// <summary>
		/// This enables or disables new guild dues.
		/// </summary>
		[ServerProperty("guild", "new_guild_dues", "Guild dues can be set from 1-100% if enabled, or standard 2% if not", true)]
		public static bool NEW_GUILD_DUES;

		/// <summary>
		/// This sets the guild dues max value to 0~100%.
		/// </summary>
		[ServerProperty("guild", "guild_dues_max_value", "Guild dues can be set from 1-100%", 25)]
		public static int GUILD_DUES_MAX_VALUE;

		/// <summary>
		/// Do we allow guild members from other realms
		/// </summary>
		[ServerProperty("guild", "allow_cross_realm_guilds", "Do we allow guild members from other realms?", false)]
		public static bool ALLOW_CROSS_REALM_GUILDS;

		/// <summary>
		/// How many things do we allow guilds to claim?
		/// </summary>
		[ServerProperty("guild", "guilds_claim_limit", "How many things do we allow guilds to claim?", 1)]
		public static int GUILDS_CLAIM_LIMIT;

		/// <summary>
		/// Guild Crafting Buff bonus amount
		/// </summary>
		[ServerProperty("guild", "guild_buff_crafting", "Percent speed gain for the guild crafting buff?", (ushort)5)]
		public static ushort GUILD_BUFF_CRAFTING;

		/// <summary>
		/// Guild XP Buff bonus amount
		/// </summary>
		[ServerProperty("guild", "guild_buff_xp", "Extra XP gain percent for the guild PvE XP buff?", (ushort)5)]
		public static ushort GUILD_BUFF_XP;

		/// <summary>
		/// Guild RP Buff bonus amount
		/// </summary>
		[ServerProperty("guild", "guild_buff_rp", "Extra RP gain percent for the guild RP buff?", (ushort)2)]
		public static ushort GUILD_BUFF_RP;

		/// <summary>
		/// Guild BP Buff bonus amount -  this is not available on live, disabled by default
		/// </summary>
		[ServerProperty("guild", "guild_buff_bp", "Extra BP gain percent for the guild BP buff?", (ushort)0)]
		public static ushort GUILD_BUFF_BP;

		/// <summary>
		/// Guild artifact XP Buff bonus amount
		/// </summary>
		[ServerProperty("guild", "guild_buff_artifact_xp", "Extra artifact XP gain percent for the guild artifact XP buff?", (ushort)5)]
		public static ushort GUILD_BUFF_ARTIFACT_XP;

		/// <summary>
		/// Guild masterlevel XP Buff bonus amount
		/// </summary>
		[ServerProperty("guild", "guild_buff_masterlevel_xp", "Extra masterlevel XP gain percent for the guild masterlevel XP buff?", (ushort)20)]
		public static ushort GUILD_BUFF_MASTERLEVEL_XP;

		/// <summary>
		/// How much merit to reward guild when dragon is killed, if any.
		/// </summary>
		[ServerProperty("guild", "guild_merit_on_dragon_kill", "How much merit to reward guild when dragon is killed, if any.", (ushort)0)]
		public static ushort GUILD_MERIT_ON_DRAGON_KILL;
		
		/// <summary>
		/// How much merit to reward guild when legion is killed, if any.
		/// </summary>
		[ServerProperty("guild", "guild_merit_on_legion_kill", "How much merit to reward guild when legion is killed, if any.", (ushort)0)]
		public static ushort GUILD_MERIT_ON_LEGION_KILL;

		/// <summary>
		/// When a banner is lost to the enemy how long is the wait before purchase is allowed?  In Minutes.
		/// </summary>
		[ServerProperty("guild", "guild_banner_lost_time", "When a banner is lost to the enemy how many minutes is the wait before purchase is allowed?", (ushort)1440)]
		public static ushort GUILD_BANNER_LOST_TIME;

		/// <summary>
		/// Autonomous gamebots form realm guilds, carry guild names and can be invited into a player's guild.
		/// </summary>
		[ServerProperty("guild", "bot_guilds_enabled", "Autonomous gamebots form and join realm guilds (see docs/BOT-GUILDS.md), and players can /ginvite them. False keeps every gamebot guildless; saved memberships are kept.", true)]
		public static bool BOT_GUILDS_ENABLED;

		/// <summary>
		/// Share of eligible autonomous gamebots placed in bot guilds.
		/// </summary>
		[ServerProperty("guild", "bot_guild_share", "Share (0 to 1) of eligible autonomous gamebots that bot guild formation places in guilds.", 0.70)]
		public static double BOT_GUILD_SHARE;

		/// <summary>
		/// Lowest level at which an autonomous gamebot joins a bot guild.
		/// </summary>
		[ServerProperty("guild", "bot_guild_min_level", "Lowest level at which an autonomous gamebot is placed in a bot guild.", 5)]
		public static int BOT_GUILD_MIN_LEVEL;

		/// <summary>
		/// Bot guild members talk in guild chat and recruit in /faction (when bot guilds are on).
		/// </summary>
		[ServerProperty("guild", "bot_guild_social", "When bot guilds are on: guild members greet, congratulate and chat in guild chat when a real member can hear, answer your /gu, and bot guild officers recruit in /faction.", true)]
		public static bool BOT_GUILD_SOCIAL;

		/// <summary>
		/// Bot guild officers may invite a player into their guild with a yes/no dialog.
		/// </summary>
		[ServerProperty("guild", "bot_guild_recruit_player", "When bot guild social life is on: bot guild officers may invite you to their guild with a yes/no dialog (when you ask for a guild, or now and then when you are nearby and unguilded).", true)]
		public static bool BOT_GUILD_RECRUIT_PLAYER;

		/// <summary>
		/// Bot guilds claim keeps their realm takes, under the usual claim rules.
		/// </summary>
		[ServerProperty("guild", "bot_guild_claims_keeps", "When bot guilds are on: after a realm takes a keep, a bot guild with enough members inside claims it (claim_num; guilds_claim_limit), never while a real player of that realm is inside. Its members answer first when that keep is attacked.", true)]
		public static bool BOT_GUILD_CLAIMS_KEEPS;

		/// <summary>
		/// Guilded gamebots pay dues from real looted coin, earn merit points, and bot guild leaders buy guild buffs.
		/// </summary>
		[ServerProperty("guild", "bot_guild_economy", "When bot guilds are on: guilded gamebots pay their guild's dues from the real coin they loot, their level-ups and realm ranks earn the guild merit points as a player's do, and bot guild leaders spend 1,000 merit points on a 24-hour guild buff.", true)]
		public static bool BOT_GUILD_ECONOMY;

		/// <summary>
		/// Gamebots a player invited into his guild (AutonomousBotGuilds) pay its dues and earn it merit points, as players do
		/// (owner 2026-11-03, from a r/daoc report). Guild realm points, keep claims and guild buffs still never count a bot.
		/// </summary>
		[ServerProperty("guild", "bot_player_guild_economy", "Gamebots you invite into your guild pay its dues (the rate its leader set) from the real coin they loot, and their level-ups and realm ranks earn it merit points as a player's do. Guild realm points, keep claims and guild buffs still never count a bot. Works whether bot guilds are on or off.", true)]
		public static bool BOT_PLAYER_GUILD_ECONOMY;

		/// <summary>
		/// Each realm's RvR and raiding bot guilds form one alliance.
		/// </summary>
		[ServerProperty("guild", "bot_guild_alliances", "When bot guilds are on: each realm's RvR and raiding bot guilds form one alliance (up to four guilds); a player in one of them hears its alliance chat. Alliances with a player's guild are never touched.", true)]
		public static bool BOT_GUILD_ALLIANCES;

		/// <summary>
		/// Autonomous gamebots have personality types and traits that tilt how they spend their time.
		/// </summary>
		[ServerProperty("bots", "bot_personalities_enabled", "Autonomous gamebots get a personality (Leveler, Socialite, Daredevil, Explorer, Crafter, No Lifer) and traits, fixed from their id, that tilt town breaks, solo versus group goals, dungeon choice, camp choice and how often they talk (see docs/BOT-PERSONALITIES.md). Hard rules never change. False: no personalities.", true)]
		public static bool BOT_PERSONALITIES_ENABLED;

		/// <summary>
		/// Autonomous gamebots react to a player-shaped attacker like a player (stefanrows/OfflineDAoC 0.271).
		/// </summary>
		[ServerProperty("bots", "bot_incoming_fire_reaction", "Autonomous gamebots hit by a player, bot or pet react like a player: a 0.3-2.5 s reaction delay (healers heal meanwhile; shorter for tanks and bold personalities), an area spell from a distance doesn't make its caster the bot's target, and healers cure a dotted group with the group cure. Companions and bots in your group are not affected. False keeps the immediate reaction.", true)]
		public static bool BOT_INCOMING_FIRE_REACTION;

		/// <summary>
		/// RvR perception (owner 2026-11-01): autonomous gamebots notice enemy players in RvR by chance, react after a moment and call it to their group.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_perception", "In the frontier, battlegrounds and Darkness Falls, an autonomous gamebot notices an enemy player (or a player's companion or pet) by chance: always within 1,000 units, never beyond 3,500; one bot looking on its own takes about 1.5 s at 1,500 units, 6 s at 2,500 and 20 s at 3,400 (the median), less when the enemy is fighting, casting, in a group of 3 or more or in front of the bot, more when it stands still. At most 3 bots of a realm look for the same enemy group each second; the others learn of it from their call. It engages 0.5-2 s later (faster at higher level and for bold personalities). Enemy gamebots are noticed the same way when rvr_bot_perception_bots is on; siege, guards, doors and monsters are unchanged, and a bot or group under attack fights back at once. False keeps the old instant sighting up to 5,000 units.", true)]
		public static bool RVR_BOT_PERCEPTION;

		/// <summary>
		/// How sharp autonomous gamebots are at spotting enemy players in RvR: easy, normal or veteran.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_awareness", "When rvr_bot_perception is on: easy (sure sighting within 700, never beyond 2,500, twice the normal time to notice, slower reactions, 2 lookers per enemy each second), normal (like players in 2003: 1,000 / 3,500, 0.5-2 s, 3 lookers) or veteran (1,300 / 4,500, half the normal time to notice, faster reactions, a wider call radius, 5 lookers).", "normal")]
		public static string RVR_BOT_AWARENESS;

		/// <summary>
		/// A gamebot that notices an enemy player tells its group and warband, who react after their own moment.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_spotters", "When rvr_bot_perception is on: the gamebot that notices an enemy player tells its group mates within 2,500 units, who engage after their own reaction delay; each other group of its warband hears it relayed half the time, 4 s later. A bot on a realm raid, in a battleground assault or in a player's group doesn't leave it to answer. With bot voices on it calls it in /group (\"inc 3 north\") when you are in that group. False: every bot has to notice for itself.", true)]
		public static bool RVR_BOT_SPOTTERS;

		/// <summary>
		/// Open-field RvR overhaul for 1.2 (owner 2026-11-16; docs/RVR-FIELD-1.2.md): the master switch of the bot groups' open-field tactics.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_field_tactics", "The master switch of the open-field RvR tactics for autonomous gamebots (docs/RVR-FIELD-1.2.md): a bot-only group, or a solo gamebot, away from keeps shares one fight plan. It never applies to realm raids, battleground keep assaults, keep defenders and siege fighters, siege engine operators, relic carriers, companions, or any group you lead or are in. False turns every rvr_bot_*, rvr_march_* and rvr_roam_* tactics switch below off at once. In this build it changes nothing by itself: the tactics arrive with 1.2 work packages 1 to 6, each behind its own switch.", true)]
		public static bool RVR_BOT_FIELD_TACTICS;

		/// <summary>
		/// 1.2 work package 1 (eyes): bots notice enemy bots by the player rule, scan only as far as noticing reaches, and may notice a stealther close up.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_perception_bots", "Open-field RvR (1.2): away from keeps, autonomous gamebots notice enemy gamebots and their pets the way they notice you (rvr_bot_perception: by chance, after a reaction delay, called to the group), instead of at once at 5,000 units; noticing one enemy notices its group mates within 1,500 units of it, and a bot looks only as far as noticing reaches (3,500 on normal, never less than 2,600). A bot may also notice a stealthed enemy, player or bot, close up by chance (about 50% a second at point blank, none beyond 350 units or beyond what a player in its place could see, never while fighting someone else). Bots at a keep they hold or besiege, raids, battleground assaults and groups with a player are unchanged, and a bot or group under attack fights back at once. Log: RVR_NOTICE (kind, party), RVR_SPOT, RVR_SCAN_BUDGET. Needs rvr_bot_perception and rvr_bot_field_tactics.", true)]
		public static bool RVR_BOT_PERCEPTION_BOTS;

		/// <summary>
		/// 1.2 work package 1 (eyes): two lookers per group.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_group_lookers", "Open-field RvR (1.2): away from keeps, two members of each gamebot group take turns (every 10 s) looking for enemies, you included; soloers look for themselves. The others look only once a mate calls an enemy, a group mate is fighting or the group's plan has a target. Bots at a keep they hold or besiege, raids, battleground assaults and groups with a player are unchanged. Log: RVR_SCAN_BUDGET (lookers, woken, idle). Needs rvr_bot_perception and rvr_bot_field_tactics.", true)]
		public static bool RVR_BOT_GROUP_LOOKERS;

		/// <summary>
		/// 1.2 work package 2 (assist): a main assist and a called target.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_assist", "Open-field RvR (1.2): in a bot-only group outside raids, battleground assaults and keep fights, one fighter (a tank first) is the main assist and the group's target is called by the 2003 kill order (casters, healers, bladeturn casters, crowd controllers, tanks last; versus Midgard the Shaman first, versus Hibernia the Bard, the Eldritch, then the Druids). A call is held 4-8 s; an enemy using Mastery of Concentration or just resurrected becomes the call; one the group cannot reach is never called. The first target is the plan's too (1.2.3, #531): the group opens on one enemy (the call, else the one mates already fight, else the kill order), people before pets and siege engines, free enemies before crowd-controlled ones, instead of spreading over two or three. Members follow after their own delay by the group's skill tier, and some tunnel. Log: RVR_ASSIST_CALL, RVR_ASSIST_SWITCH, RVR_DECIDE (pick). Needs rvr_bot_field_tactics.", true)]
		public static bool RVR_BOT_ASSIST;

		/// <summary>
		/// 1.2 work package 2 (assist): the group's crowd-control plan.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_cc_plan", "Open-field RvR (1.2): a bot group's mezzers, stunners and rooters each hold one enemy that is not the kill target, from the charge on; the kill target is never mezzed, a resisted mez is cast again and the server's crowd-control immunity is respected. Human errors by skill tier: area damage sometimes breaks a mez, and a mezzed crowd controller is not covered for 2-4 s. Log: RVR_CC_ASSIGN. Needs rvr_bot_field_tactics.", true)]
		public static bool RVR_BOT_CC_PLAN;

		/// <summary>
		/// 1.2 work package 2 (assist): interrupt duty.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_interrupts", "Open-field RvR (1.2): each enemy healer or caster that is not the kill target gets one bot of the group (an archer, a caster with an instant or fast spell, a pet class, or a melee fighter in reach; at most half the group) that keeps hitting it; it looks again every 1.5-3 s by skill tier. Our design, not a 2003 rotation; the server's 1.65 interrupt rules are unchanged. Log: RVR_INTERRUPT. Needs rvr_bot_field_tactics.", true)]
		public static bool RVR_BOT_INTERRUPTS;

		/// <summary>
		/// 1.2 work package 3 (protect): tanks guard their healers against players and bots.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_peel", "Open-field RvR (1.2): in a bot-only group outside raids, battleground assaults and keep fights, the plan names one tank per healer (then casters) as its guard; peeling now triggers when a player or a bot, not only a monster, hits a healer or caster. A guard keeps Protect and, with a shield, Guard on his charge, Intercepts for it below half health, pulls off only the enemies on his own charge (so the tanks do not all chase one attacker) and slams it with a stun style when his class has one usable (the server's 1.65 stun immunity applies). Human errors by skill tier: tanks over-extend and leave the healer (Casual 35%, Average 15%, Veteran 5% of the time; design values). Log: RVR_PEEL. Needs rvr_bot_field_tactics.", true)]
		public static bool RVR_BOT_PEEL;

		/// <summary>
		/// 1.2 work package 3 (protect): backline, spread and kiting.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_positioning", "Open-field RvR (1.2): in a fight against players or bots, a bot-only group's casters and healers stand behind the tanks relative to the enemy and keep more than 350 units apart, so one mez cannot catch two healers (450 on incoming); a guard stays beside his charge; a healer with an enemy melee fighter on it kites (runs a bit, then heals). In the Kite stance (a bigger or melee-heavy enemy) the group fights while backing away: casters, healers and archers keep about 1,200 units from the nearest enemy melee fighter by stepping back in short legs (150-300 units) away from the enemy, then cast or shoot for 2-3.5 s; they never step toward another enemy group, into an enemy keep's guards (2,500 units) or onto a blocked leg, and hold and fight when no leg is safe; melee members screen the backline and fight only what reaches it. Every walk is proven on dry ground on the bot's own floor first. Human errors by skill tier (design values): healers too close together (Casual 30%, Average 10%, Veteran 3%), healers charging with the tanks (15% / 4% / 1%), a healer that does not kite (60% / 30% / 10%), a ragged spread; kiting late (35% / 15% / 5%), the wrong way (20% / 8% / 2%) or too far (25% / 10% / 3%), melee charging anyway (25% / 10% / 3%). Log: RVR_BACKLINE, RVR_KITE. Needs rvr_bot_field_tactics.", true)]
		public static bool RVR_BOT_POSITIONING;

		/// <summary>
		/// 1.2 work package 4 (stances, RvrEngagement): watch and size up before engaging.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_size_up", "Open-field RvR (1.2): size-up. A bot-only group in the open field that notices an enemy group, or a fight between the other two realms, stops and watches first (about 6 s casual, 9 s average, 12 s veteran, shorter for an impatient leader) and counts heads, healers, levels, health and power; then it engages at fair odds, kites or retreats. A bot that is hit always fights back. Raids, battleground assaults, keep fights and groups with a player keep today's rules. Needs rvr_bot_field_tactics. Log: RVR_SIZE_UP, RVR_WATCH, RVR_STANCE.", true)]
		public static bool RVR_BOT_SIZE_UP;
		[ServerProperty("bots", "bot_rvr_sight_size_up", "A solo or duo gamebot sizes up a fight when enemies come into its perception range, in the frontier and the battlegrounds (keep assaults included), and opens or backs off (#662). Boldness moves the line; no solo bot ever opens on three or more enemies. Backing off, it pulls back out of range, waits nearby, calls its close friends, then goes back to what it was doing. Not companions, not PvE.", true)]
		public static bool BOT_RVR_SIGHT_SIZE_UP;

		/// <summary>
		/// 1.2 work package 4 (stances, RvrEngagement): kite and retreat from bad odds (needs bot_veteran_retreat for the retreat).
		/// </summary>
		[ServerProperty("bots", "rvr_bot_disengage", "Open-field RvR (1.2): disengage. A bot-only group facing bad odds or losing its fight kites (fights while backing away) or retreats: drops its targets, sprints and runs, raggedly (members react 0.3-2 s apart, some never run, and one caught in melee fights back). Half of the warband's other groups hear of a retreat 4 s later. The retreat uses the group retreat, so bot_veteran_retreat off also turns the RvR retreat off (kiting stays). Needs rvr_bot_field_tactics. Log: RVR_RETREAT, RVR_STANCE.", true)]
		public static bool RVR_BOT_DISENGAGE;

		/// <summary>
		/// 1.2 work package 4 (stances, RvrEngagement): hold, regroup and rest, the chase leash, wipe memory.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_regroup", "Open-field RvR (1.2): regroup. After a retreat, or after a fight that left it hurt, a bot-only group meets at a rally point, rests and goes back out; veteran groups leave the fight's spot to rest, casual ones rest where they fought. Members strung out when an enemy is noticed wait for the rest. A member gives up a chase past its leash (bold bots chase much further). A group remembers its wipes; after three in ten minutes it roams before it hunts again. Needs rvr_bot_field_tactics. Log: RVR_REGROUP, RVR_CHASE, RVR_STANCE.", true)]
		public static bool RVR_BOT_REGROUP;

		/// <summary>
		/// 1.2 work package 5 (lanes): a siege march uses up to three proven lanes.
		/// </summary>
		[ServerProperty("bots", "rvr_march_lanes", "Open-field RvR (1.2): a siege army leaves on up to three proven lanes instead of one line: from a portal keep everyone takes the one road to its milegate, then the groups split 50/25/25 across the main lane and up to two alternates the server re-walked at start (67/33 with one). A lane that passes near a keep the enemy holds now, or a march hazard, is left out; with no lane left everyone takes today's route. The march leader keeps the pace on the main lane, the other groups keep level with him on theirs, and the army gathers again at the siege camp as today. Groups you lead or are in march as today. Log: RVR_LANE_LOADED, RVR_LANE_REJECTED, RVR_LANE_ASSIGN, RVR_LANE_FALLBACK. Needs rvr_bot_field_tactics.", true)]
		public static bool RVR_MARCH_LANES;

		/// <summary>
		/// 1.2 work package 5 (lanes): roaming groups may travel on the proven lanes between catalog objectives.
		/// </summary>
		[ServerProperty("bots", "rvr_roam_lanes", "Open-field RvR (1.2): roaming bot groups that travel between two catalog objectives (keeps, milegates, border keeps) may take one of the proven lanes instead of the one main route, by group. Used by the roaming groups of work package 6; groups you lead or are in are never changed. Log: RVR_LANE_ASSIGN, RVR_LANE_FALLBACK (by work package 6's callers). Needs rvr_bot_field_tactics.", true)]
		public static bool RVR_ROAM_LANES;

		/// <summary>
		/// 1.2 work package 6 (roamers): the population split and role-built eight-man groups.
		/// </summary>
		[ServerProperty("bots", "rvr_roaming_parties", "Open-field RvR (1.2): Autonomous gamebots split their RvR population (siege parties form first and are not counted) into warbands, role-built eight-man groups and soloers, duos and stealthers, by rvr_roam_population_split. An eight is built to its realm's 2003 skeleton: two main healers, a crowd controller, a shield tank, a speed class, two who can cure mez, a Shaman (Midgard) or Theurgist (Albion), the rest damage; a missing role goes to the next best class and the group still forms, and rvr_group_pet_cap holds. Eights roam; they do not open sieges. Groups you lead or join are never changed. Needs rvr_bot_field_tactics. Log: RVR_PARTY_FORMED.", true)]
		public static bool RVR_ROAMING_PARTIES;

		/// <summary>Era builds 1.2 (WP5, decision D9): the RvR eight's crowd-control slot prefers a crowd-control build.</summary>
		[ServerProperty("bots", "rvr_roster_build_roles", "Era builds in RvR (1.2): when a role-built eight fills its crowd-control slot it first takes a member or bot whose era build is a crowd-control build (a Pacification Healer, a Mind Sorcerer, a Music Bard; roles healer-cc and cc-mezzer), then the classes as before. Bots without an era build rank as before. Groups you lead or join are never changed. Needs rvr_roaming_parties. Log: RVR_PARTY_FORMED cc=.", true)]
		public static bool RVR_ROSTER_BUILD_ROLES = true;

		/// <summary>
		/// 1.2 work package 6 (roamers): circuits and check spots.
		/// </summary>
		[ServerProperty("bots", "rvr_roam_checks", "Open-field RvR (1.2): A roaming eight walks one of its realm's patrol circuits in order (the check-spot catalog, re-walked on the navmesh at server start) and checks each milegate, levelling camp, dungeon door or named monster for 30 to 60 seconds, then moves on. Circuits are weighted toward where enemy levelling bots are by a live count every 3 minutes; Forest Sauvage, Snowdonia, Mount Collory and Cruachan Gorge are walked only when nothing else has prey. Enemy levelling bots at a spot are hunted, and after a kill the group moves on (rvr_roam_move_on_minutes). Soloers and duos work chokepoints and camps; stealthers camp milegates and crossings in loose packs. Needs rvr_bot_field_tactics. Log: RVR_ROAM_CHECK, RVR_ROAM_HUNT, RVR_ROAM_MOVE_ON, RVR_CHECKSPOT_LOADED.", true)]
		public static bool RVR_ROAM_CHECKS;

		/// <summary>
		/// 1.2 work package 6 (roamers): stealth openers and the soloer's odds.
		/// </summary>
		[ServerProperty("bots", "rvr_bot_stealth_openers", "Open-field RvR (1.2): A gamebot Infiltrator, Nightshade or Shadowblade still in stealth opens a fight with its own stealth style (Backstab, Perforate Artery and the like), once per target, and a soloer, duo or stealther opens only when the odds allow it: the target alone or weaker, no enemy group near it, its own health and power up (casual bots misjudge more often). A bot that is hit always fights back. Needs rvr_bot_field_tactics. Log: RVR_STEALTH_OPEN.", true)]
		public static bool RVR_BOT_STEALTH_OPENERS;

		/// <summary>
		/// 1.2 work package 6 (roamers): the population split of RvR gamebots not in a siege (owner decision 2, 2026-11-16).
		/// </summary>
		[ServerProperty("bots", "rvr_roam_population_split", "Of the RvR gamebots not in a siege, the percent in warbands / role-built eight-man groups / soloers, duos and stealthers (owner decision 2026-11-16: 55/30/15; an assumption, no source gives the 2003 proportions). Three numbers; anything else reads as 55/30/15. Needs rvr_roaming_parties.", "55/30/15")]
		public static string RVR_ROAM_POPULATION_SPLIT;

		/// <summary>
		/// 1.2 work package 6 (roamers): the move-on rule (owner decision 4, 2026-11-16).
		/// </summary>
		[ServerProperty("bots", "rvr_roam_move_on_minutes", "The move-on rule: after a kill at a check spot a roaming bot group, soloer or stealther leaves within this many minutes (veteran groups sooner, casual ones later); no corpse camping. 0 leaves right after the kill. Needs rvr_roam_checks.", 3)]
		public static int RVR_ROAM_MOVE_ON_MINUTES;

		/// <summary>
		/// Pet caps (North Star audit 2026-11-03): how many pet classes may join one RvR group that gamebots form.
		/// </summary>
		[ServerProperty("bots", "rvr_group_pet_cap", "When gamebots form an RvR group (a roaming warband or a siege party), at most this many pet classes (Cabalist, Theurgist, Necromancer, Spiritmaster, Bonedancer, Enchanter, Animist, Sluaghbinder) join a group of five or more, and half as many (at least 1) a group of four or fewer. The others wait for the next group. Groups you lead or join are never changed. 0 turns the cap off.", 2)]
		public static int RVR_GROUP_PET_CAP;

		// ---- Bots as people (docs/BOTS-AS-PEOPLE-1.2.md; 1.2 work package 0 declares every switch, each package fills its own).
		// All on by default (owner 2026-11-16): new keys, so a world from an earlier build has no row and takes the code default
		// (no LivingServerSettingsUpgrade line). Each starts at its default so code that runs before InitProperties sees it.

		/// <summary>
		/// Bots as people, item 2's master switch (work packages 1 and 2).
		/// </summary>
		[ServerProperty("bots", "bot_skill_profiles", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): every autonomous gamebot gets a skill tier (Casual, Average or Veteran, 30/50/20, shifted by rvr_bot_awareness) and six facets: reactions, positioning, pulls, standing in fire, buffs and power use. A Casual bot pulls an add now and then, lets a buff run out and reacts late; a Veteran rarely does, and no mistake takes a named or a monster more than 2 levels above the group. Never companions, raids, battleground assaults or fights at a keep. False turns bot_skill_growth and bot_skill_spare_player_group off with it. Log: BOT_SKILL_PROFILE, BOT_SKILL_BUDGET, BOT_SKILL_MISTAKE. Mistakes: Casual pulls an add in 10% of party pulls (Average 5%, Veteran 1.5%), lets 25% of expired buffs stay off 20-90 s (12%, 4%), casts its dearest nuke in 30% of casts (15%, 5%) and rests at 25% power (35%, 45%), circles into a monster's face in 12% of steps (6%, 2%), reacts x1.3 slower (x1.0, x0.75) and in open-field RvR steps out of an area effect after 2-5 s (1-3 s, 0.5-1.5 s; an RvR group keeps its group tier) and follows its group's call after a delay inside the group tier's band, sooner with better reactions.", true)]
		public static bool BOT_SKILL_PROFILES = true;

		/// <summary>
		/// Bots as people, work package 1.
		/// </summary>
		[ServerProperty("bots", "bot_skill_growth", "Bots as people (1.2): a gamebot's skill grows with play: every fight it or a groupmate near it wins teaches all six facets (the time since its last fight, at most 3 minutes), and a death teaches positioning, standing in fire and reactions. One tier at most in 1.2 (about 40 hours of fighting from the bottom of Casual to Average), and it never falls. Saved in offline_bot_skill every 5 minutes and soon after a logout. Needs bot_skill_profiles. Log: BOT_SKILL_GROWTH, BOT_SKILL_BUDGET.", true)]
		public static bool BOT_SKILL_GROWTH = true;

		/// <summary>
		/// Bots as people, work package 2.
		/// </summary>
		[ServerProperty("bots", "bot_skill_spare_player_group", "Bots as people (1.2): in a group with you, a gamebot makes its mistakes at a quarter of its own rate (owner decision 4, 2026-11-17); its reactions are not quickened, so it still plays like a person, a careful one. Needs bot_skill_profiles. Log: BOT_SKILL_MISTAKE spared=true, BOT_SKILL_MISTAKES (rate_in against rate_out).", true)]
		public static bool BOT_SKILL_SPARE_PLAYER_GROUP = true;

		/// <summary>
		/// Bots as people, work package 2R.
		/// </summary>
		[ServerProperty("bots", "raid_learns_from_raiders", "Bots as people (1.2, owner decision 1, 2026-11-17): a realm raid's familiarity with a boss mechanic follows how many of the raiders present have seen it (full from 60% of the raid, a quarter with nobody), and each raider's own reactions follow its own count; Epic Special Forces have seen everything; companions and buffbots are not counted, and a raid with a real player in it (or a player's own group) keeps the record's familiarity. Wired for Xanxicar's stomp. A played world is seeded once at its first start with the switch (each level 50 of a realm with a counted attempt, with a chance of the record's familiarity but at least 80%), so its familiarity stays the same on average. False: the record's familiarity alone, as before. Log: RAID_LEARN, RAID_MECHANIC_SEEN.", true)]
		public static bool RAID_LEARNS_FROM_RAIDERS = true;
		[ServerProperty("bots", "bot_bard_fills_healer_slot", "Can a Hibernian Bard fill a bot party's Healer slot when no Druid or Warden is in reach, and heal as that party's healer while it holds the slot (#567 B7)? Ordinary groups only, never realm raids. Off: only the healing classes fill it.", true)]
		public static bool BOT_BARD_FILLS_HEALER_SLOT;
		[ServerProperty("bots", "bot_morgana_quest_help", "Bots in your group help with Morgana's quest fight (#611): when your Albion level 50 epic event starts at the fallen tower in Lyonesse, your companions and the gamebots grouped with you fight her two demons, Bechard and Silcharde. Bots never attack Morgana, and only a player on the quest starts the event. Off: bots join only once you or the demons start the fight. Log: MORGANA_QUEST_HELP.", true)]
		public static bool BOT_MORGANA_QUEST_HELP = true;

		/// <summary>
		/// Bots as people, item 4's master switch (work packages 3 and 4).
		/// </summary>
		[ServerProperty("bots", "bot_place_knowledge", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): a gamebot knows the places it has been, and at the start what a player of its level would: its capital, its home town, its realm's zones below its level (and their towns) and, from level 20, its border keep. Dungeons, the frontier and the next zones are new ground until it goes there; it learns a place on arrival. Saved per bot; /lswhy way lists them. On its own it changes no route and no camp: asking the way and wrong roads read it. False turns bot_ask_the_way and bot_wrong_roads off with it. Log: BOT_PLACE_LEARNED, BOT_PLACE_BUDGET.", true)]
		public static bool BOT_PLACE_KNOWLEDGE = true;

		/// <summary>
		/// Bots as people, work package 4.
		/// </summary>
		[ServerProperty("bots", "bot_ask_the_way", "Bots as people (1.2): a gamebot going somewhere it does not know may ask the way in /say near you or in /faction (at most one ask in /faction per realm every 10 minutes); only a bot of its realm that knows the place answers, truthfully, and the asker then half knows it. Or it tags along behind a realm-mate of about its level (2 levels at most) going there, and leaves it at the zone line, when the leader stops, or when a monster yellow or worse to it is on the way. Needs bot_place_knowledge. Log: BOT_WAY_ASK, BOT_WAY_TOLD, BOT_WAY_FOLLOW, BOT_WAY_SUMMARY.", true)]
		public static bool BOT_ASK_THE_WAY = true;

		/// <summary>
		/// Bots as people, work package 4.
		/// </summary>
		[ServerProperty("bots", "bot_wrong_roads", "Bots as people (1.2): a gamebot going somewhere it does not know may take a wrong road for 600 to 1,500 units, look around and come back to its route. A wrong road follows the proven walking legs of the route network, which may cross open ground rather than a painted road. At most once a trip and 3 minutes; never on a stable ride, in a group, in a frontier, in a dungeon or near monsters more than 2 levels above it, and it turns back when a monster yellow or worse to it is on the way. Needs bot_place_knowledge. Log: BOT_WAY_WRONG_ROAD, BOT_WAY_SUMMARY.", true)]
		public static bool BOT_WRONG_ROADS = true;

		/// <summary>
		/// Bots as people, item 5's master switch (work package 5).
		/// </summary>
		[ServerProperty("bots", "bot_movement_habits", "Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): gamebots move like people: melee bots circle-strafe in open-field RvR, bot groups travel on /stick in a line, bots overshoot where they stop, sit when they wait, crowd the bank and sit on the walls of keeps their realm holds. Never in raids, battleground waves, fights at a keep, stable rides or a group with you. False turns bot_move_strafe, bot_move_stick_line and bot_move_sit_and_walls off with it. Log: BOT_MOVE_HABITS.", true)]
		public static bool BOT_MOVEMENT_HABITS = true;

		/// <summary>
		/// Bots as people, work package 5.
		/// </summary>
		[ServerProperty("bots", "bot_move_strafe", "Bots as people (1.2): in an open-field RvR fight, melee gamebots circle-strafe their target and casters and healers face the enemy and back off in an arc, more often the better the bot. Needs bot_movement_habits and rvr_bot_field_tactics. Log: BOT_MOVE_STRAFE.", true)]
		public static bool BOT_MOVE_STRAFE = true;

		/// <summary>
		/// Bots as people, work package 5.
		/// </summary>
		[ServerProperty("bots", "bot_move_stick_line", "Bots as people (1.2): bot-only groups travelling in the open world run on /stick, each member following the one above it, so the group runs as a strung-out line; now and then one loses stick and catches up. Not on stable rides, raids, battleground waves or sieges. Needs bot_movement_habits. Log: BOT_MOVE_STICK_LOST.", true)]
		public static bool BOT_MOVE_STICK_LINE = true;

		/// <summary>
		/// Bots as people, work package 5.
		/// </summary>
		[ServerProperty("bots", "bot_move_sit_and_walls", "Bots as people (1.2): gamebots sit while they wait for their group, at a rendezvous or at the bank, and bots waiting at a keep their realm holds, with no siege there and no enemy noticed, sit on its walls and stand when an enemy comes (owner decision 11, 2026-11-17). Sitting is only how it looks: resting is unchanged. Needs bot_movement_habits. Log: BOT_MOVE_SIT, BOT_MOVE_WALL.", true)]
		public static bool BOT_MOVE_SIT_AND_WALLS = true;

		[ServerProperty("bots", "bot_weapon_set_rules", "Bots fight with the weapon set a player would (#569): a party's only tank with Guard fights with its trained shield and guards a member within reach, a bot carrying a two-hander and a one-hander uses the line it trained higher, a hired companion keeps the melee set you readied in its window, and archers stop flipping between bow and melee weapon. Only weapons the bot already carries are used. Gamebots, hired companions and /bot companions alike.", true)]
		public static bool BOT_WEAPON_SET_RULES;

		/// <summary>
		/// Bots as people, item 6 (work package 6).
		/// </summary>
		[ServerProperty("bots", "bot_living_milestones", "Server life, Bots as people (1.2, docs/BOTS-AS-PEOPLE-1.2.md): autonomous gamebots chase a personal goal (the ding to 50 from level 45; at 50 realm rank 5 if they lean to RvR, or a dragon trophy, an item really won from a dragon or epic boss raid's loot, if they lean to raids) and say so as they get close, in their group, in /say near you, in their guild or in /faction, within the chat budgets; the realm cheers when one gets there, the Camelot Herald lists it under Milestones of the Realm, and the goal leans the bot's plans (fewer town breaks near 50, more RvR near realm rank 5). Log: BOT_MILESTONE_ARC, BOT_MILESTONE_PROGRESS, BOT_MILESTONE_TALK, BOT_MILESTONE_REACHED, BOT_MILESTONE_SUMMARY.", true)]
		public static bool BOT_LIVING_MILESTONES = true;

		/// <summary>
		/// Autonomous gamebots speak lines from the bot-voices corpus in their own writing style.
		/// </summary>
		[ServerProperty("bots", "bot_voices_enabled", "Autonomous gamebots say pre-written, period lines (docs/bot-voices/DESIGN.md) chosen by personality, level, realm, class and situation, each in its own writing style, without repeating a line in a realm for three days (six hours once a kind of line runs short). False keeps the old lines.", true)]
		public static bool BOT_VOICES_ENABLED;

		/// <summary>
		/// Bots of a realm hold debates and long threads in /faction.
		/// </summary>
		[ServerProperty("bots", "bot_voices_debates", "When bot voices are on: bots of a realm play the corpus's debates and long threads in /faction while you are online, one at a time, 30-45 minutes apart, never repeating a scene until all have played.", true)]
		public static bool BOT_VOICES_DEBATES;

		/// <summary>
		/// Bots now and then start a private tell with the player (the talking update).
		/// </summary>
		[ServerProperty("bots", "bot_voices_tells", "When bot voices and bot memory are on: now and then (at most every 25-45 minutes) a bot sends you a tell: a friend saying hello or passing on news, a guildmate, or a solo bot of your level nearby asking to duo.", true)]
		public static bool BOT_VOICES_TELLS;

		/// <summary>
		/// How busy realm chat is (the talking update; from Beta 1.1.2 the default is 2, a busy server; 1 is calmer, 3 very busy): a multiplier on every
		/// channel's target in chat-budget.json. The launcher's Living Server tab sets it.
		/// </summary>
		[ServerProperty("bots", "bot_voices_chat_rate", "When bot voices are on: how much bots talk on their own, from 0 (they only answer) to 3. 2 (the default) sounds like a busy server; 1 is calmer, 3 very busy. Chat already rises and falls with the hour and with how many bots are on (chat-budget.json).", 2.0)]
		public static double BOT_VOICES_CHAT_RATE;

		public const string LIVING_SERVER_SETTINGS_VERSION_KEY = "living_server_settings_version";

		/// <summary>The Living Server build whose default changes were last applied to this world (LivingServerSettingsUpgrade).</summary>
		[ServerProperty("bots", LIVING_SERVER_SETTINGS_VERSION_KEY, "Living Server: the build whose new defaults were last applied to this world's saved settings (set by the server; leave it alone).", "")]
		public static string LIVING_SERVER_SETTINGS_VERSION;

		public const string LIVING_SERVER_WORLD_REPAIRS_KEY = "living_server_world_repairs";

		/// <summary>The one-time world repairs that already ran on this world, comma-separated (BuffBotCopyRetirement: "buffbot-copies").</summary>
		[ServerProperty("bots", LIVING_SERVER_WORLD_REPAIRS_KEY, "Living Server: the one-time repairs of saved world data that already ran on this world (set by the server; leave it alone).", "")]
		public static string LIVING_SERVER_WORLD_REPAIRS;

		// ---- Server life (docs/LIVING-SERVER.md). Every part is on by default since Living Server 1.2 (owner 2026-11-16: "all our
		// living server features should be in by default if someone downloads our installer"); worlds that ran an earlier build
		// move once where the row still holds the untouched old default (LivingServerSettingsUpgrade). ----

		/// <summary>Bots remember the player: greetings by name, thanks for a rez or a group, gossip, grudges.</summary>
		[ServerProperty("bots", "bot_living_memory", "Server life: autonomous gamebots of your realm remember grouping with you, your rezzes and rude words; they greet you by name, thank you for an old rez, answer you by name, mention you in /faction now and then, and stay cool after insults. Chat only. False: bots do not remember you.", true)]
		public static bool BOT_LIVING_MEMORY;

		/// <summary>Grats from nearby, grouped and guilded bots when a character dings; /faction grats for a 50.</summary>
		[ServerProperty("bots", "bot_living_grats", "Server life: when you or a bot gains a level, groupmates say grats in /group and bots nearby in /say, and a realm's bots cheer a new level 50 in /faction.", true)]
		public static bool BOT_LIVING_GRATS;

		/// <summary>Realm-wide announcements of server firsts, recorded for the Herald.</summary>
		[ServerProperty("bots", "bot_living_firsts", "Server life: the first character on the server (you or a bot) to reach level 50, the first of each class to reach 50 and the first to each realm rank from 2 up are announced to everyone and recorded for the Camelot Herald.", true)]
		public static bool BOT_LIVING_FIRSTS;

		/// <summary>Rez requests and thanks, bots rezzing you, corpse runs and resurrection sickness chatter.</summary>
		[ServerProperty("bots", "bot_living_death", "Server life: a bot that dies near you asks you for a rez if your class can rez, waits longer for it and thanks you after; a bot healer that is not in your group may rez you (you still accept or decline); bots talk about corpse runs and resurrection sickness.", true)]
		public static bool BOT_LIVING_DEATH;

		/// <summary>Player-style enemy sightings in /faction from bots in the frontier.</summary>
		[ServerProperty("bots", "bot_living_alarms", "Server life: a bot in the frontier that sees 3 or more enemies calls it out in /faction (rough size, classes, place), at most one call per realm every 2 minutes.", true)]
		public static bool BOT_LIVING_ALARMS;

		/// <summary>Bots remember who killed them in RvR, call them out and celebrate revenge.</summary>
		[ServerProperty("bots", "bot_living_nemesis", "Server life: a bot killed by an enemy remembers its killer, names it (by race and class) in /faction after the second kill, and celebrates when it kills that enemy back. Kills are recorded for the Camelot Herald's Most Wanted.", true)]
		public static bool BOT_LIVING_NEMESIS;

		/// <summary>When bots are online: the whole roster (off), shaped around your session, or by the real clock.</summary>
		[ServerProperty("bots", "bot_schedule_mode", "Server life: when autonomous gamebots are online. off (the default) = the whole roster at once; session = each bot plays sessions of 1-4 hours with 20 minutes to 2 hours away, timed from server start; clock = your PC's time of day (busiest 7-11pm and weekends, quiet overnight).", "off")]
		public static string BOT_SCHEDULE_MODE;

		/// <summary>Bots take short breaks (brb) and now and then go linkdead and come back.</summary>
		[ServerProperty("bots", "bot_living_breaks", "Server life: an idle bot now and then says brb and logs off for 5-25 minutes, and a bot grouped with you rarely goes linkdead for 30-90 seconds (the group hears about it) and comes back with an apology. Never during a fight, siege or relic march.", true)]
		public static bool BOT_LIVING_BREAKS;

		/// <summary>Idle bots look around, emote, shift their feet, wave at you and cheer a ding nearby.</summary>
		[ServerProperty("bots", "bot_living_body_language", "Server life: autonomous gamebots standing around in town, waiting for their group or resting look around, emote now and then (ponder, shrug, laugh, stretch their legs with a step or two in town), turn and wave at you when you walk up, and cheer when someone dings nearby. Only where a real player can see it; never in a fight. Bots do not jump; sitting comes with Moving like people (bot_movement_habits).", true)]
		public static bool BOT_LIVING_BODY_LANGUAGE;

		[ServerProperty("bots", "bot_healer_period_rules", "Bot and companion healers heal like a 1.65 healer (#567): gamebots, your own companion healers and /spawn helpers. A dedicated healer starts at two-thirds health (65%, not 80%), heals the tank first, uses the small heal before the big one outside an emergency, puts a heal over time on the tank as a fight wears it down, and casts a group heal only with three or more hurt and not on the pull. Realm raids, warbands and boss fights keep their own healer rules.", true)]
		public static bool BOT_HEALER_PERIOD_RULES;

		/// <summary>#571 step 1: the damage dealers of a player's group fight the player's target first (CompanionLeaderFocus).</summary>
		[ServerProperty("bots", "bot_leader_focus", "Bots in your group fight your fight (#571): in PvE and RvR alike, when you attack a target (or cast harmful magic at it, or your pet fights it), the damage dealers among your /spawn and /raid helpers, hired companions, /bot companions and the gamebots grouped with you fight that target before whoever is hitting them or their own top threat, as a group assists its main assist. Tanks keep guarding and peeling, healers keep healing, a target a groupmate has mesmerized or rooted is left alone, a boss rule that forbids hitting a target still holds, and /defensive keeps its range. Autonomous gamebots outside your group are unchanged. Log: COMPANION_LEADER_FOCUS.", true)]
		public static bool BOT_LEADER_FOCUS;

		/// <summary>About 4% of Crafter bots are beggars who live in their capital (AutonomousBeggars).</summary>
		[ServerProperty("bots", "bot_living_beggars", "Server life: about 4% of the Crafter bots are beggars. They live in their capital, beg you and passing bots for coin and loot (drag coin or an item onto one to give it), sell what they are given and hoard every copper. Ask anyone for gold or items and the beggars follow you around the city insulting you and defending their corners until you leave. Beggars never group or fight.", true)]
		public static bool BOT_LIVING_BEGGARS;

		/// <summary>Living Server 1.2: a share of the No Lifer bots run a level-50 buffbot (NoLiferBuffBots, docs/BOT-PERSONALITIES.md).</summary>
		[ServerProperty("bots", "bot_nolifer_buffbots", "Server life: some No Lifer bots (bot_nolifer_buffbot_percent of them, 30 by default) run a second character, a level-50 buffbot (Albion Cleric, Midgard Shaman, Hibernia Druid, all in buffs). It follows its main where it is safe, parks at the nearest bind when the main goes into a dungeon, the frontier or a raid, and keeps the main buffed. It never joins a group, fights or loots, is not counted in the population, and logs in and out with its main. A bot is a No Lifer by its roster id, whether bot personalities are on or off.", true)]
		public static bool BOT_NOLIFER_BUFFBOTS;

		/// <summary>Percent (0-100) of the No Lifer bots that have a buffbot, fixed per main from its roster id.</summary>
		[ServerProperty("bots", "bot_nolifer_buffbot_percent", "Percent (0 to 100) of the No Lifer bots that run a buffbot. Which No Lifers have one is fixed from each main's roster id, so the same mains keep theirs across restarts.", 30)]
		public static int BOT_NOLIFER_BUFFBOT_PERCENT;

		/// <summary>The 2003 rule for buffbots' concentration buffs: no range limit (the 5,000-unit limit came with the July 2005 Classic servers).</summary>
		[ServerProperty("bots", "bot_nolifer_buffbot_era_conc_range", "No Lifers' buffbots follow the 2003 rule for concentration buffs: no range limit, so a buffbot parked at a bind keeps its main buffed (Mythic added the 5,000-unit limit with its Classic servers in July 2005). False: their concentration buffs switch off beyond this server's buff range like everyone else's, and come back in range.", true)]
		public static bool BOT_NOLIFER_BUFFBOT_ERA_CONC_RANGE;

		/// <summary>Bots that saw the player do something tell the realm; reputation and nicknames (AutonomousPlayerDeeds).</summary>
		[ServerProperty("bots", "bot_living_deeds", "Server life: bots of your realm that see you solo a named monster, go on a kill streak in the frontier, get zerged or die to something silly tell /faction a few minutes later, and others react. Deeds build a reputation the realm talks about, and a nickname.", true)]
		public static bool BOT_LIVING_DEEDS;

		/// <summary>Gamebots remember the bots they grouped with; friends group again, grudges keep apart (AutonomousBotBonds).</summary>
		[ServerProperty("bots", "bot_group_bonds", "Server life: gamebots remember the bots they grouped with. Good runs make friends who look for each other when forming a party; bots who never showed up or wiped the group too often are kept apart until a long wait makes them take anyone.", true)]
		public static bool BOT_GROUP_BONDS;

		/// <summary>Bot-led groups (docs/BOT-LED-GROUPS.md): the leader of a gamebot party may invite you, and you can just follow.</summary>
		[ServerProperty("bots", "bot_group_invite_player", "Bot-led groups: the leader of a gamebot party of your realm and level hunting where you can walk or ride may send you a tell and the group invite box, at most once every 45 minutes unasked (never while you fight, ride, trade, are grouped, have /spawn helpers out, are /afk or in the frontier). Say yes and you follow: the leader picks the camp (at most 6 levels above you), the meeting spot and the pulls, and waits for you. False stops unasked invites; /lfg still gets you one.", true)]
		public static bool BOT_GROUP_INVITE_PLAYER;

		/// <summary>How many members the real player counts as in the loot of a group with gamebots (owner 2026-11-04).</summary>
		[ServerProperty("bots", "bot_group_player_loot_weight", "In a group with you and gamebots, whoever leads: you count as this many members in the coin split and the item rotation (2: with seven bots nearby you get 2 of every 9 items and 2/9 of the coin), and an item your class can use that beats what you wear comes to you first. 1 to 8.", 2)]
		public static int BOT_GROUP_PLAYER_LOOT_WEIGHT;

		/// <summary>Bot cosmetics (owner 2026-11-12): gamebots spend coin on dyes and weapon lusters (AutonomousBotCosmetics).</summary>
		[ServerProperty("bots", "bot_cosmetics", "Gamebots spend their coin on their looks as well as their gear: at a merchant between tasks, a bot with coin above its reserve buys the game's own dyes, enamels and weapon lusters, at their own prices, in its own colours (each bot has its own, leaning on its realm's; guild members often wear the guild's). Glowing weapons come only from epic bosses, dragons and Darkness Falls seal merchants, as in 1.65. New gear comes in its plain colour, so they pay again. Some bots never bother. False: no bot buys dyes.", true)]
		public static bool BOT_COSMETICS;

		/// <summary>Bot cosmetics: what dyes and lusters cost bots, in percent (AutonomousBotCosmetics).</summary>
		[ServerProperty("bots", "bot_cosmetics_price_percent", "What dyes and weapon lusters cost gamebots, in percent of the game's own price (100: as the dye items cost, from 50 copper for a light shade to over 16 gold for royal and black ones and about 196 gold for a weapon luster). 1 to 1000.", 100)]
		public static int BOT_COSMETICS_PRICE_PERCENT;

		/// <summary>Bot-led groups, B2: what saying yes to a gamebot leader's invite does (follow or lead).</summary>
		[ServerProperty("bots", "bot_led_group_join_mode", "Bot-led groups: when you say yes to a gamebot leader's invite. follow = the leader runs the group and you follow (/lead takes over at any time); lead = you become the leader at once and the bots follow you, as bots you /invite do (/lead bot hands it back).", "follow")]
		public static string BOT_LED_GROUP_JOIN_MODE;

		/// <summary>Play with friends (1.2, CompanionBudget): /spawn and /raid companions on the whole server while more than one real player is online.</summary>
		[ServerProperty("bots", "companion_budget_total", "Play with friends: while more than one real player is online, the most /spawn and /raid companions the whole server may have out together (each player also has companion_budget_per_player). With one player online neither applies, so a /raid 80 (79 companions) works as always. 0 turns the server total off.", 160)]
		public static int COMPANION_BUDGET_TOTAL;

		/// <summary>Play with friends (1.2, CompanionBudget): one player's share of the companions while more than one real player is online.</summary>
		[ServerProperty("bots", "companion_budget_per_player", "Play with friends: while more than one real player is online, the most /spawn and /raid companions one player may have out (79 = a full /raid 80). The server total, companion_budget_total, still applies. With one player online this doesn't apply. 0 turns the per-player share off.", 79)]
		public static int COMPANION_BUDGET_PER_PLAYER;

		/// <summary>Bot-led groups, B3: a realm raid's leader may invite a level-50 player.</summary>
		[ServerProperty("bots", "bot_raid_invite_player", "Raid leaders: when a dragon or epic dungeon raid forms in your realm and you are level 50, its raid leader (an officer of a raiding guild) may send you a tell and a box, once per raid. Yes puts you in a party that needs your class (one gamebot of your role steps out); you travel to the muster town yourself. A no is final for that raid. /invites off stops these too.", true)]
		public static bool BOT_RAID_INVITE_PLAYER;

		/// <summary>Multi-day dramas between pairs of bots, played as /faction episodes (AutonomousBotStorylines).</summary>
		[ServerProperty("bots", "bot_living_storylines", "Server life: pairs of bots live out dramas in /faction over days: a kill steal, a guild split, a romance, a ninja-loot trial. The same two bots play every episode; take a side in faction and the ending follows you.", true)]
		public static bool BOT_LIVING_STORYLINES;

		/// <summary>Gold spammers and roleplayers (AutonomousGoldSpammers, AutonomousRoleplayers).</summary>
		[ServerProperty("bots", "bot_living_characters", "Server life: now and then a gold spammer floods /faction until it is banned, and a few bots roleplay in thee-and-thou speech while the realm mocks them.", true)]
		public static bool BOT_LIVING_CHARACTERS;

		/// <summary>Tales: the realm's storyteller in /faction, fireside tales in a group (AutonomousTales).</summary>
		[ServerProperty("bots", "bot_living_tales", "Server life: now and then a bot asks the realm's storyteller for the tale of a dragon or a boss in /faction, others hush the channel, and it is told in a dozen lines; you can ask for one too (\"tell the one about legion\"). Resting in a boss's place with a gamebot in your group, a groupmate tells the short version. Needs bot voices and debates.", true)]
		public static bool BOT_LIVING_TALES;

		/// <summary>The rumour after a tale may be true: the realm's raid on that boss forms a few minutes later.</summary>
		[ServerProperty("bots", "bot_living_tale_raids", "Server life: about half the time, the rumour that follows a tale is true: the realm's own automatic raid on that boss forms a few minutes later (only when one could form anyway). Off: the rumour is always just lore.", true)]
		public static bool BOT_LIVING_TALE_RAIDS;

		/// <summary>True rumours: real rare spawns and undefended enemy keeps (AutonomousRumours).</summary>
		[ServerProperty("bots", "bot_living_rumours", "Server life: bots pass on rumours that are true: a rare named monster that just spawned, or an enemy keep whose guards are down. Go and see.", true)]
		public static bool BOT_LIVING_RUMOURS;

		/// <summary>Holiday chatter around real holidays on the PC's calendar (AutonomousHolidays).</summary>
		[ServerProperty("bots", "bot_living_holidays", "Server life: around Halloween, Thanksgiving, Christmas, New Year and other holidays on your PC's calendar, bots talk about them like 2003 players did and answer your holiday wishes.", true)]
		public static bool BOT_LIVING_HOLIDAYS;

		/// <summary>Which Thanksgiving holiday chatter marks (AutonomousHolidays; owner 2026-11-18: a non-US option).</summary>
		[ServerProperty("bots", "bot_living_holidays_thanksgiving", "Server life: which Thanksgiving the holiday chatter marks: us (the fourth Thursday of November, the default), canada (the second Monday of October) or off.", "us")]
		public static string BOT_LIVING_HOLIDAYS_THANKSGIVING;

		/// <summary>Now and then a raider charges the final boss early (AutonomousRaidLeeroy).</summary>
		[ServerProperty("bots", "bot_living_leeroy", "Server life: one end-game raid in twenty has a bot who charges the final boss before the raid is ready, yelling his name, and the raid has to follow him in.", true)]
		public static bool BOT_LIVING_LEEROY;

		/// <summary>The lore's screen-centre atmosphere lines (Living Server 1.2, LoreAtmosphere, docs/LORE.md).</summary>
		[ServerProperty("bots", "living_atmosphere_lines", "Atmosphere lines: short lines in the middle of the screen when you first enter a dungeon or epic zone that day, step into a boss's lair, a boss changes phase, a dragon wakes, a realm kills a boss for the first time or a raid forms. At most one every 30 seconds, and realm news always comes first.", true)]
		public static bool LIVING_ATMOSPHERE_LINES;

		/// <summary>A dead bot falls in the original client (1.2.1, BotDeathAnimation): its removal leaves a corpse, and it comes
		/// back when raised or released. The new client is not affected; it shows the fall already.</summary>
		[ServerProperty("bots", "bot_death_animation", "Bots fall when they die, in the original game client: a companion, hired companion or gamebot that dies plays the death fall and lies there as a monster's corpse does, until the client fades it. Raised, it stands up again; released, it appears at its bind. You can still rez it with nothing selected. The new client already shows the fall and gets nothing new.", true)]
		public static bool BOT_DEATH_ANIMATION;

		/// <summary>Realism 3a (#485, owner decision 2026-11-24): bots look like players unless this is switched on.</summary>
		[ServerProperty("bots", "bot_show_as_bots", "Mark gamebots as bots: /who adds [Playerbot] to every gamebot, and a refused group invite says \"gamebot\" or \"buffbot\". Off by default (owner, 2026-11-24): bots look like players in /who and in system lines.", false)]
		public static bool BOT_SHOW_AS_BOTS = false;

		[ServerProperty("bots", "epic_armor_counts_bots", "Gamebots (autonomous and companions) count when an epic boss's or epic NPC's armor drops with the number of attackers, at epic_armor_bot_strength_percent of a player: by default each bot takes off half a player's step (0.02 instead of 0.04), and bots alone take off at most half of the full range (an epic boss from 1.6 to 1.0, never to the 0.4 floor). Real players and pets keep their own rule on top, down to 0.4. False is the stock rule: only players and pets count.", true)]
		public static bool EPIC_ARMOR_COUNTS_BOTS = true;

		[ServerProperty("bots", "epic_armor_bot_strength_percent", "When epic_armor_counts_bots is on: a gamebot's share of a real player's epic armor drop, in percent (0 to 100), which sets both its step (a player's is 0.04) and how much of the armor range bots alone can take off. 50 (the default): 0.02 a bot, an epic boss down to 1.0 at most from bots. 100 counts a bot as a player.", 50)]
		public static int EPIC_ARMOR_BOT_STRENGTH_PERCENT = 50;



		#endregion

		#region CRAFT / SALVAGE

		/// <summary>
		/// The crafting sellback price control at each craft (calculate a percent of raw materials used)
		/// </summary>
		[ServerProperty("craft", "crafting_adjust_product_price", "Change price to recommended value in database for product itemtemplate", false)]
		public static bool CRAFTING_ADJUST_PRODUCT_PRICE;
		/// <summary>
		/// The crafting price control for secondary craft (trinketing) modifier
		/// </summary>
		[ServerProperty("craft", "crafting_secondary_sellback_percent", "How many percent of raw materials cost is SellBack values", 98.572)]
		public static double CRAFTING_SECONDARYCRAFT_SELLBACK_PERCENT;
		/// <summary>
		/// The crafting price control for craft modifier
		/// </summary>
		[ServerProperty("craft", "crafting_sellback_percent", "How many percent of raw materials cost is SellBack values", 95)]
		public static int CRAFTING_SELLBACK_PERCENT;
		/// <summary>
		/// The crafting speed modifier
		/// </summary>
		[ServerProperty("craft", "crafting_speed", "Crafting Speed Modifier - Edit this to change the speed at which you craft e.g 1.5 is 50% faster 2.0 is twice as fast (100%) 0.5 is half the speed (50%)", 1.0)]
		public static double CRAFTING_SPEED;

		/// <summary>
		/// Crafting skill gain bonus in capital cities
		/// </summary>
		[ServerProperty("craft", "capital_city_crafting_skill_gain_bonus", "Crafting skill gain bonus % in capital cities; 5 = 5%", 5)]
		public static int CAPITAL_CITY_CRAFTING_SKILL_GAIN_BONUS;

		/// <summary>
		/// Crafting speed bonus in capital cities
		/// </summary>
		[ServerProperty("craft", "capital_city_crafting_speed_bonus", "Crafting speed bonus in capital cities; 2 = 2x, 3 = 3x, ..., 1 = standard", 1.0)]
		public static double CAPITAL_CITY_CRAFTING_SPEED_BONUS;
		
		/// <summary>
		/// Crafting speed bonus in capital cities
		/// </summary>
		[ServerProperty("craft", "keep_crafting_speed_bonus", "Crafting speed bonus in the keeps; 2 = 2x, 3 = 3x, ..., 1 = standard", 1.0)]
		public static double KEEP_CRAFTING_SPEED_BONUS;

		/// <summary>
		/// Allow any realm to craft items with a realm of 0 (no realm)
		/// </summary>
		[ServerProperty("craft", "allow_craft_norealm_items", "Allow any realm to craft items with 0 (no) realm.", false)]
		public static bool ALLOW_CRAFT_NOREALM_ITEMS;

		/// <summary>
		/// Max character crafting skill?
		/// </summary>
		[ServerProperty("craft", "crafting_max_skills", "Set character crafting skills to max level.", false)]
		public static bool CRAFTING_MAX_SKILLS;
		
		/// <summary>
		/// Max character crafting skill?
		/// </summary>
		[ServerProperty("craft", "crafting_max_skills_amount", "The amount to which set the crafting skills when using crafting_max_skills", 1)]
		public static int CRAFTING_MAX_SKILLS_AMOUNT;

		/// <summary>
		/// Use salvage per realm and get back material to use in chars realm
		/// </summary>
		[ServerProperty("salvage", "use_salvage_per_realm", "Enable to get back material to use in chars realm. Disable to get back the same material in all realms.", false)]
		public static bool USE_SALVAGE_PER_REALM;

		/// <summary>
		/// Use salvage per realm and get back material to use in chars realm
		/// </summary>
		[ServerProperty("salvage", "use_new_salvage", "Enable to use a new system calcul of salvage count based on object_type.", false)]
		public static bool USE_NEW_SALVAGE;

		#endregion

		#region ACCOUNT
		/// <summary>
		/// Allow auto-account creation  This is also set in serverconfig.xml and must be enabled for this property to work.
		/// </summary>
		[ServerProperty("account", "allow_auto_account_creation", "Allow auto-account creation  This is also set in serverconfig.xml and must be enabled for this property to work.", true)]
		public static bool ALLOW_AUTO_ACCOUNT_CREATION;

		/// <summary>
		/// Account bombing prevention
		/// </summary>
		[ServerProperty("account", "time_between_account_creation", "The time in minutes between 2 accounts creation. This avoid account bombing with dynamic ip. 0 to disable", 0)]
		public static int TIME_BETWEEN_ACCOUNT_CREATION;

		/// <summary>
		/// Account IP bombing prevention
		/// </summary>
		[ServerProperty("account", "time_between_account_creation_sameip", "The time in minutes between accounts creation from the same ip.", 15)]
		public static int TIME_BETWEEN_ACCOUNT_CREATION_SAMEIP;

		/// <summary>
		/// Total number of account allowed for the same IP
		/// </summary>
		[ServerProperty("account", "total_accounts_allowed_sameip", "Total number of account allowed for the same IP", 20)]
		public static int TOTAL_ACCOUNTS_ALLOWED_SAMEIP;

		#endregion

		#region ATLAS
		/// <summary>
		/// Allow claiming of BG keeps
		/// </summary>
		[ServerProperty("atlas", "allow_bg_claim", "Allow claiming of BG keeps", false)]
		public static bool ALLOW_BG_CLAIM;

		/// <summary>
		/// Enables the API endpoints on the port :5000
		/// </summary>
		[ServerProperty("atlas", "atlas_api", "Enables the API endpoints on the port :5000", false)]
		public static bool ATLAS_API;
		
		/// <summary>
		/// Maximum number of charges allowed
		/// </summary>
		[ServerProperty("atlas", "max_charge_items", "Maximum number of charges allowed", 2)]
		public static int MAX_CHARGE_ITEMS;
		
		/// <summary>
		/// Maximum numbers of entities allowed
		/// </summary>
		[ServerProperty("server", "max_entities", "Maximum numbers of entities allowed", 150000)]
		public static int MAX_ENTITIES;

		/// <summary>
		/// Enforces the check on the link between game account and Discord
		/// </summary>
		[ServerProperty("atlas", "force_discord_link", "Enforces the check on the link between game account and Discord", false)]
		public static bool FORCE_DISCORD_LINK;

		/// <summary>
		/// Set the password to access certain API commands as shutdown
		/// </summary>
		[ServerProperty("atlas", "api_password", "Set the password to access certain API commands as shutdown", "")]
		public static string API_PASSWORD;

		[ServerProperty("atlas", "enable_corpsesummoner", "Whether or not to enable the corpse summoner command", true)]
		public static bool ENABLE_CORPSESUMONNER;

		[ServerProperty("atlas", "salvage_yield_multiplier", "The salvage yield multiplier", 0.5)]
		public static double SALVAGE_YIELD_MULTIPLIER;

		[ServerProperty("atlas", "max_craft_time", "The maximum craft time allowed in seconds. All timers above this value will be normalised to the input value", 0)]
		public static int MAX_CRAFT_TIME;
		
		[ServerProperty("atlas", "of_teleport_interval", "The seconds between OF porting ceremonies", 120)]
		public static int OF_REPORT_INTERVAL;

		[ServerProperty("atlas", "patch_notes_url", "The URL of the remote patch notes .txt to display with /sn", "")]
		public static string PATCH_NOTES_URL;

		[ServerProperty("atlas", "immunity_timer_use_adaptive", "toggle adaptive vs. flat immunity timers", false)]
		public static bool IMMUNITY_TIMER_USE_ADAPTIVE;

		[ServerProperty("atlas", "immunity_timer_flat_length", "if non-adapative timers, the duration (in seconds) for immunity", 60)]
		public static int IMMUNITY_TIMER_FLAT_LENGTH;

		[ServerProperty("atlas", "immunity_timer_adaptive_length", "if adapative timers, the modifer to apply to length (i.e. stun length * 6)", 6)]
		public static int IMMUNITY_TIMER_ADAPTIVE_LENGTH;

		#endregion

		#region RANDOM OBJECT GENERATION

		[ServerProperty("atlas_rog", "rog_toa_item_chance", "chance of generating an object with TOA stats (in %)", 0)]
		public static int ROG_TOA_ITEM_CHANCE;
		
		[ServerProperty("atlas_rog", "rog_armor_weight", "chance of armor being included in the roll (in %)", 50)]
		public static int ROG_ARMOR_WEIGHT;

		[ServerProperty("atlas_rog", "rog_magical_weight", "chance of jewelry/magical being included in the roll (in %)", 40)]
		public static int ROG_MAGICAL_WEIGHT;

		[ServerProperty("atlas_rog", "rog_weapon_weight", "chance of weapons being included in the roll (in %)", 40)]
		public static int ROG_WEAPON_WEIGHT;

		[ServerProperty("atlas_rog", "rog_starting_qual", "base item quality (in %)", 95)]
		public static int ROG_STARTING_QUAL;

		[ServerProperty("atlas_rog", "rog_cap_qual", "max item quality (in %)", 99)]
		public static int ROG_CAP_QUAL;

		//don't put this above 35-45 or you will get way too many
		[ServerProperty("atlas_rog", "rog_toa_stat_weight", "toa specific stat weight (in %)", 0)]
		public static int ROG_TOA_STAT_WEIGHT;

		[ServerProperty("atlas_rog", "rog_item_stat_weight", "stat bonus (str, con, dex, etc) weight (in %)", 45)]
		public static int ROG_ITEM_STAT_WEIGHT;

		[ServerProperty("atlas_rog", "rog_item_resist_weight", "resist bonus (body, matter, etc) weight (in %)", 48)]
		public static int ROG_ITEM_RESIST_WEIGHT;

		[ServerProperty("atlas_rog", "rog_item_skill_weight", "skill bonus (Celtic Spear, Dual Wield, etc) weight (in %)", 25)]
		public static int ROG_ITEM_SKILL_WEIGHT;

		[ServerProperty("atlas_rog", "rog_item_allskill_weight", "all skills bonus weight (in %)", 0)]
		public static int ROG_STAT_ALLSKILL_WEIGHT;

		[ServerProperty("atlas_rog", "rog_magical_item_offset", "base chance to get a magical RoG item (in %) [Level*2 is added to get final value]", 60)]
		public static int ROG_MAGICAL_ITEM_OFFSET;

		[ServerProperty("atlas_rog", "rog_use_weighted_generation", "toggle weighted rolls vs. simple generation", false)]
		public static bool ROG_USE_WEIGHTED_GENERATION;

		#endregion

		#region CONTROLS_AUTOMATION

		[ServerProperty("controls_automation", "auto_select_opening_style", "Automatically perform the opening style of the currently selected style if conditions are not met (recursive).", false)]
		public static bool AUTO_SELECT_OPENING_STYLE;

		[ServerProperty("controls_automation", "allow_auto_backup_styles", "Allow players to set a style to be used automatically with /backupstyle.", false)]
		public static bool ALLOW_AUTO_BACKUP_STYLES;

		[ServerProperty("controls_automation", "allow_non_anytime_backup_styles", "If /backupstyle is enabled, can players set a non-anytime style as their backup?", false)] 
		public static bool ALLOW_NON_ANYTIME_BACKUP_STYLES;

		#endregion

		public static IDictionary<string, object> AllCurrentProperties { get; private set; }

		/// <summary>
		/// Get a Dictionary that tracks all Properties by Key String
		/// Returns the ServerPropertyAttribute, the Static Field with current Value, and the according DataObject
		/// Create a default dataObject if value wasn't found in Database
		/// </summary>
		public static IDictionary<string, Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty>> AllDomainProperties
		{
			get
			{
				var result = new Dictionary<string, Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty>>();
				var allProperties = GameServer.Database.SelectAllObjects<DbServerProperty>();
				
				foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
				{
					foreach (Type type in asm.GetTypes())
					{
						foreach (FieldInfo fld in type.GetFields())
						{
							// Properties are Static
							if (!fld.IsStatic)
								continue;
							
							// Properties shoud contain a property attribute
							object[] attribs = fld.GetCustomAttributes(typeof(ServerPropertyAttribute), false);
							if (attribs.Length == 0)
								continue;
							
							ServerPropertyAttribute att = (ServerPropertyAttribute)attribs[0];
							
							DbServerProperty serverProp = allProperties.Where(p => p.Key.Equals(att.Key, StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
							
							if (serverProp == null)
							{
								// Init DB Object
								serverProp = new DbServerProperty();
								serverProp.Category = att.Category;
								serverProp.Key = att.Key;
								serverProp.Description = att.Description;
								if (att.DefaultValue is double)
								{
									CultureInfo myCIintl = new CultureInfo("en-US", false);
									IFormatProvider provider = myCIintl.NumberFormat;
									serverProp.DefaultValue = ((double)att.DefaultValue).ToString(provider);
								}
								else
								{
									serverProp.DefaultValue = att.DefaultValue.ToString();
								}
								serverProp.Value = serverProp.DefaultValue;
							}
							
							result[att.Key] = new Tuple<ServerPropertyAttribute, FieldInfo, DbServerProperty>(att, fld, serverProp);
						}
					}
				}

				return result;
			}
		}
		
		/// <summary>
		/// This method loads the property from the database and returns
		/// the value of the property as strongly typed object based on the
		/// type of the default value
		/// </summary>
		/// <param name="attrib">The attribute</param>
		/// <returns>The real property value</returns>
		public static void Load(ServerPropertyAttribute attrib, FieldInfo field, DbServerProperty prop)
		{
			string key = attrib.Key;
			
			// Not Added to database...
			if (!prop.IsPersisted)
			{
				GameServer.Database.AddObject(prop);

				if (log.IsDebugEnabled)
					log.DebugFormat("Cannot find server property {0} creating it", key);
			}
			
			if (log.IsDebugEnabled)
				log.DebugFormat("Loading {0} Value is {1}", key, prop.Value);
			
			try
			{
				if (field.IsInitOnly && log.IsWarnEnabled)
					log.WarnFormat("Property {0} is ReadOnly, Value won't be changed - {1} !", key, field.GetValue(null));
				
				// 1.2.3: en-US numbers as before, a comma decimal ("1,0") read as that number, and a value that can't be read
				// at all (blank, "1.0x") uses the attribute's default instead of leaving the field at 0 / false
				// (a r/daoc report: endurance_regen_amount_modifier unreadable gave no endurance regeneration at all).
				object value = ServerPropertyValue.ReadOrDefault(prop.Value, attrib.DefaultValue, out bool fellBack);
				if (fellBack && log.IsWarnEnabled)
					log.Warn($"SERVER_PROPERTY_UNREADABLE key={key} value=\"{prop.Value}\": can't be read as {attrib.DefaultValue.GetType().Name}, " +
					         $"using the default {Convert.ToString(attrib.DefaultValue, CultureInfo.InvariantCulture)}. Set it in the launcher or fix the ServerProperty row.");
				field.SetValue(null, value);
			}
			catch (Exception e)
			{
				if (log.IsErrorEnabled)
				{
					log.ErrorFormat("Exception in ServerProperties Load: {0}", e);
					log.ErrorFormat("Trying to load {0} value is {1}", key, prop.Value);
				}
			}
		}

		/// <summary>
		/// Refreshes the server properties from the DB
		/// </summary>
		public static void Refresh()
		{
			if (log.IsInfoEnabled)
				log.Info("Refreshing server properties...");

			InitProperties();
		}

		/// <summary>
		/// Irreversibly removes rogue properties from the Database.
		/// </summary>
		public static void CleanUpDatabase()
		{
			IList<DbServerProperty> dbProperties = GameServer.Database.SelectAllObjects<DbServerProperty>();
			List<string> properties = [];

			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				foreach (Type type in assembly.GetTypes())
				{
					foreach (FieldInfo field in type.GetFields())
					{
						// Properties are static.
						if (!field.IsStatic)
							continue;

						// Properties should contain a property attribute.
						object[] attributes = field.GetCustomAttributes(typeof(ServerPropertyAttribute), false);

						if (attributes.Length == 0)
							continue;

						properties.Add(((ServerPropertyAttribute) attributes[0]).Key);
					}
				}
			}

			foreach (DbServerProperty dbProperty in dbProperties)
			{
				if (properties.Contains(dbProperty.Key))
					continue;

				try
				{
					GameServer.Database.DeleteObject(dbProperty);

					if (log.IsInfoEnabled)
						log.Info($"Removed rogue property: {dbProperty.Key} ({dbProperty.Value}).");
				}
				catch
				{
					if (log.IsErrorEnabled)
						log.Error($"Couldn't remove rogue property: {dbProperty.Key} ({dbProperty.Value}).");
				}
			}
		}
	}
}
