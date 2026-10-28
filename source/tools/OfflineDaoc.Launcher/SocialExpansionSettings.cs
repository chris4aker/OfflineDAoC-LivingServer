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

    public bool BotGuilds { get; init; }
    public int GuildSharePercent { get; init; } = 70;
    public int GuildMinLevel { get; init; } = 5;
    public bool GuildSocial { get; init; } = true;
    public bool GuildRecruitPlayer { get; init; } = true;
    public bool GuildClaims { get; init; } = true;
    public bool GuildEconomy { get; init; } = true;
    public bool GuildAlliances { get; init; } = true;
    public bool Personalities { get; init; }
    public bool Voices { get; init; } = true;
    public bool Debates { get; init; } = true;
    public bool LivingMemory { get; init; } = true;
    public bool LivingGrats { get; init; } = true;
    public bool LivingFirsts { get; init; }
    public bool LivingDeath { get; init; }
    public bool LivingAlarms { get; init; }
    public bool LivingNemesis { get; init; }
    /// <summary>off, session or clock (AutonomousBotSchedule).</summary>
    public string ScheduleMode { get; init; } = "off";
    public bool LivingBreaks { get; init; }
    public bool LivingBodyLanguage { get; init; }
    /// <summary>About 4% of Crafter bots are beggars in their capital (AutonomousBeggars). On by default.</summary>
    public bool LivingBeggars { get; init; } = true;
    /// <summary>Deeds and reputation, storylines, gold spammers and roleplayers, true rumours (the living-world parts). On by default.</summary>
    public bool LivingDeeds { get; init; } = true;
    public bool LivingStorylines { get; init; } = true;
    public bool LivingCharacters { get; init; } = true;
    public bool LivingRumours { get; init; } = true;
    /// <summary>Holiday chatter and Leeroy in raids (Beta 1.1). On by default.</summary>
    public bool LivingHolidays { get; init; } = true;
    public bool LivingLeeroy { get; init; } = true;
    public string PlayerXpByLevel { get; init; } = "";
    public string BotXpByLevel { get; init; } = "";

    /// <summary>The server's defaults: since Beta 1.1 (the talking update) bot voices, bot memory and grats on, the rest off.</summary>
    public static SocialExpansionSettings Defaults { get; } = new();

    /// <summary>
    /// What Setup turns on for a first install (owner 2026-10-28: "every setting turned on and to our suggested level"):
    /// every feature and Server life part on, the suggested XP ranges, and the real-clock bot schedule. The guild share and
    /// lowest level keep their defaults.
    /// </summary>
    public static SocialExpansionSettings Recommended { get; } = new()
    {
        BotGuilds = true, GuildSocial = true, GuildRecruitPlayer = true, GuildClaims = true, GuildEconomy = true, GuildAlliances = true,
        Personalities = true, Voices = true, Debates = true,
        LivingMemory = true, LivingGrats = true, LivingFirsts = true, LivingDeath = true, LivingAlarms = true, LivingNemesis = true,
        ScheduleMode = "clock", LivingBreaks = true, LivingBodyLanguage = true, LivingBeggars = true, LivingDeeds = true,
        LivingStorylines = true, LivingCharacters = true, LivingRumours = true, LivingHolidays = true, LivingLeeroy = true,
        PlayerXpByLevel = SuggestedPlayerXp, BotXpByLevel = SuggestedBotXp,
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
        ("bot_guild_economy", "guild", "When bot guilds are on: guilded gamebots pay dues from real loot, level-ups earn merit points, and leaders buy guild buffs."),
        ("bot_guild_alliances", "guild", "When bot guilds are on: each realm's RvR and raiding bot guilds form one alliance."),
        ("bot_personalities_enabled", "bots", "Autonomous gamebots get a personality and traits that tilt town breaks, goals, dungeons, camps and chattiness (see docs/BOT-PERSONALITIES.md)."),
        ("bot_voices_enabled", "bots", "Autonomous gamebots say pre-written, period lines in their own writing style (docs/bot-voices/DESIGN.md)."),
        ("bot_voices_debates", "bots", "When bot voices are on: bots of a realm play debates and long threads in /faction while you are online."),
        ("bot_living_memory", "bots", "Server life: autonomous gamebots of your realm remember grouping with you, your rezzes and rude words; they greet you by name, thank you for an old rez, answer you by name, mention you in /faction now and then, and stay cool after insults. Chat only. False keeps today's bots."),
        ("bot_living_grats", "bots", "Server life: when you or a bot gains a level, groupmates say grats in /group and bots nearby in /say, and a realm's bots cheer a new level 50 in /faction."),
        ("bot_living_firsts", "bots", "Server life: the first character on the server (you or a bot) to reach level 50, the first of each class to reach 50 and the first to each realm rank from 2 up are announced to everyone and recorded for the Camelot Herald."),
        ("bot_living_death", "bots", "Server life: a bot that dies near you asks you for a rez if your class can rez, waits longer for it and thanks you after; a bot healer that is not in your group may rez you (you still accept or decline); bots talk about corpse runs and resurrection sickness."),
        ("bot_living_alarms", "bots", "Server life: a bot in the frontier that sees 3 or more enemies calls it out in /faction (rough size, classes, place), at most one call per realm every 2 minutes."),
        ("bot_living_nemesis", "bots", "Server life: a bot killed by an enemy remembers its killer, names it (by race and class) in /faction after the second kill, and celebrates when it kills that enemy back. Kills are recorded for the Camelot Herald's Most Wanted."),
        ("bot_schedule_mode", "bots", "Server life: when autonomous gamebots are online. off = the whole roster (today's behavior); session = each bot plays sessions of 1-4 hours with 20 minutes to 2 hours away, timed from server start; clock = your PC's time of day (busiest 7-11pm and weekends, quiet overnight)."),
        ("bot_living_breaks", "bots", "Server life: an idle bot now and then says brb and logs off for 5-25 minutes, and a bot grouped with you rarely goes linkdead for 30-90 seconds (the group hears about it) and comes back with an apology. Never during a fight, siege or relic march."),
        ("bot_living_body_language", "bots", "Server life: autonomous gamebots standing around in town, waiting for their group or resting look around, emote now and then (ponder, shrug, laugh, stretch their legs with a step or two in town), turn and wave at you when you walk up, and cheer when someone dings nearby. Only where a real player can see it; never in a fight. Bots cannot jump or sit (the game sends no such state for them)."),
        ("bot_living_beggars", "bots", "Server life: about 4% of the Crafter bots are beggars. They live in their capital, beg you and passing bots for coin and loot (drag coin or an item onto one to give it), sell what they are given and hoard every copper. Ask anyone for gold or items and the beggars follow you around the city insulting you and defending their corners until you leave. Beggars never group or fight."),
        ("bot_living_deeds", "bots", "Server life: bots of your realm that see you solo a named monster, go on a kill streak in the frontier, get zerged or die to something silly tell /faction a few minutes later, and others react. Deeds build a reputation the realm talks about, and a nickname."),
        ("bot_living_storylines", "bots", "Server life: pairs of bots live out dramas in /faction over days: a kill steal, a guild split, a romance, a ninja-loot trial. The same two bots play every episode; take a side in faction and the ending follows you."),
        ("bot_living_characters", "bots", "Server life: now and then a gold spammer floods /faction until it is banned, and a few bots roleplay in thee-and-thou speech while the realm mocks them."),
        ("bot_living_rumours", "bots", "Server life: bots pass on rumours that are true: a rare named monster that just spawned, or an enemy keep whose guards are down. Go and see."),
        ("bot_living_holidays", "bots", "Server life: around Halloween, Thanksgiving, Christmas, New Year and other holidays on your PC's calendar, bots talk about them like 2003 players did and answer your holiday wishes."),
        ("bot_living_leeroy", "bots", "Server life: one end-game raid in twenty has a bot who charges the final boss before the raid is ready, yelling his name, and the raid has to follow him in."),
        ("xp_rate_by_level", "rates", "Optional XP rate by level range for real player characters (set in the launcher's Living Server tab)."),
        ("bot_xp_rate_by_level", "rates", "Optional XP rate by level range for autonomous gamebots (set in the launcher's Living Server tab)."),
    ];

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
        ["bot_personalities_enabled"] = Bool(Personalities),
        ["bot_voices_enabled"] = Bool(Voices),
        ["bot_voices_debates"] = Bool(Debates),
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
        ["bot_living_deeds"] = Bool(LivingDeeds),
        ["bot_living_storylines"] = Bool(LivingStorylines),
        ["bot_living_characters"] = Bool(LivingCharacters),
        ["bot_living_rumours"] = Bool(LivingRumours),
        ["bot_living_holidays"] = Bool(LivingHolidays),
        ["bot_living_leeroy"] = Bool(LivingLeeroy),
        ["xp_rate_by_level"] = PlayerXpByLevel.Trim(),
        ["bot_xp_rate_by_level"] = BotXpByLevel.Trim(),
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
            Personalities = ReadBool("bot_personalities_enabled", d.Personalities),
            Voices = ReadBool("bot_voices_enabled", d.Voices),
            Debates = ReadBool("bot_voices_debates", d.Debates),
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
            LivingDeeds = ReadBool("bot_living_deeds", d.LivingDeeds),
            LivingStorylines = ReadBool("bot_living_storylines", d.LivingStorylines),
            LivingCharacters = ReadBool("bot_living_characters", d.LivingCharacters),
            LivingRumours = ReadBool("bot_living_rumours", d.LivingRumours),
            LivingHolidays = ReadBool("bot_living_holidays", d.LivingHolidays),
            LivingLeeroy = ReadBool("bot_living_leeroy", d.LivingLeeroy),
            // Kept as written: an invalid range setting shows its own error in the tab (the server ignores it).
            PlayerXpByLevel = stored.TryGetValue("xp_rate_by_level", out string? player) ? player?.Trim() ?? "" : "",
            BotXpByLevel = stored.TryGetValue("bot_xp_rate_by_level", out string? bots) ? bots?.Trim() ?? "" : "",
        };
    }

    /// <summary>The schedule modes the server knows (AutonomousBotSchedule.Parse).</summary>
    public static readonly string[] ScheduleModes = ["off", "session", "clock"];

    /// <summary>True when any Living Server part is on.</summary>
    public bool AnyLiving => LivingMemory || LivingGrats || LivingFirsts || LivingDeath || LivingAlarms || LivingNemesis ||
                             ScheduleMode != "off" || LivingBreaks || LivingBodyLanguage || LivingBeggars ||
                             LivingDeeds || LivingStorylines || LivingCharacters || LivingRumours || LivingHolidays || LivingLeeroy;

    /// <summary>
    /// "All off": the three features, every Living Server part and both XP ranges off. The share, lowest level and each part keep their
    /// values, so turning a feature back on later restores the player's own choices.
    /// </summary>
    public SocialExpansionSettings AllOff() => this with
    {
        BotGuilds = false, Personalities = false, Voices = false, PlayerXpByLevel = "", BotXpByLevel = "",
        LivingMemory = false, LivingGrats = false, LivingFirsts = false, LivingDeath = false, LivingAlarms = false,
        LivingNemesis = false, ScheduleMode = "off", LivingBreaks = false, LivingBodyLanguage = false, LivingBeggars = false,
        LivingDeeds = false, LivingStorylines = false, LivingCharacters = false, LivingRumours = false,
        LivingHolidays = false, LivingLeeroy = false,
    };

    /// <summary>True when every feature and both XP ranges are off.</summary>
    public bool IsAllOff => !BotGuilds && !Personalities && !Voices && !AnyLiving && PlayerXpByLevel.Trim() == "" && BotXpByLevel.Trim() == "";

    /// <summary>Why these settings can't be saved, or null when they can.</summary>
    public string? Validate()
    {
        if (GuildSharePercent is < 0 or > 100) return "The share of bots in guilds must be 0 to 100%.";
        if (GuildMinLevel is < 0 or > 50) return "The lowest guild level must be 0 to 50.";
        if (!ScheduleModes.Contains(ScheduleMode)) return "The bot schedule must be off, session or clock.";
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
                // ServerProperty_ID is unique, so new rows use the key, as the server's own rows do.
                command.CommandText = """
                    INSERT INTO ServerProperty (Category, `Key`, Description, DefaultValue, Value, LastTimeRowUpdated, ServerProperty_ID)
                    VALUES (@category, @key, @description, '', @value, @updated, @key)
                    ON CONFLICT(`Key`) DO UPDATE SET Value=excluded.Value, LastTimeRowUpdated=excluded.LastTimeRowUpdated
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
