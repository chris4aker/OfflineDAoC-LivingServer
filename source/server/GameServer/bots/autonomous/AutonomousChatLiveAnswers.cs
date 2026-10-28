using System;
using DOL.Database;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS.ServerRules;

namespace DOL.GS;

/// <summary>
/// Answers from the live world for questions the reply corpus can only guess at (chat audit 2026-10-21): where people of a
/// level are hunting right now, where the enemy has been seen, whether a bot is in the place a player asked about, and where
/// the bot is. Every answer names what is really true on this server at that moment, or nothing.
/// </summary>
public static class AutonomousChatLiveAnswers
{
    /// <summary>The level asked about ("good camp for 24?"), else the asker's, else the bot's.</summary>
    public static int LevelAsked(string text, int fallback)
    {
        foreach (Match match in Regex.Matches(text ?? string.Empty, @"\b(\d{1,2})s?\b"))
            if (int.TryParse(match.Groups[1].Value, out int level) && level is >= 1 and <= 50)
                return level;
        return fallback;
    }

    /// <summary>"where is a good place to level at 24?": the zone where bots of that level are camping now, and a dungeon for it.</summary>
    public static string WhereToHunt(GameBot bot, GamePlayer asker, string incoming, Random random)
    {
        if (bot == null)
            return null;
        int level = LevelAsked(incoming, asker?.Level ?? bot.Level);
        string zone = AutonomousBotRegistry.Snapshot()
            .Where(other => other.Realm == bot.Realm && other.IsAlive && Math.Abs(other.Level - level) <= 2 &&
                            !string.IsNullOrEmpty(other.PersistentRecord?.CurrentCampId) && other.CurrentZone != null &&
                            !other.CurrentZone.IsRvR && other.CurrentRegion?.IsRvR != true &&
                            !AutonomousBotVoices.IsCapitalRegion(other.CurrentRegionID) && !string.IsNullOrWhiteSpace(other.CurrentZone.Description))
            .GroupBy(other => other.CurrentZone.Description)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
        string dungeon = level >= 7 ? AutonomousBotVoices.DungeonFor(bot.Realm, level) : null;
        if (zone != null && dungeon != null && !zone.Contains(dungeon, StringComparison.OrdinalIgnoreCase))
            return random.Next(3) switch
            {
                0 => $"for {level} try {zone}, lots of people camping there right now. or {dungeon} if you get a group",
                1 => $"{zone} is busy with {level}s at the moment, {dungeon} is good too with a group",
                _ => $"{dungeon} with a group, or {zone} solo, thats where the {level}s are",
            };
        if (zone != null)
            return random.Next(2) == 0 ? $"{zone}, saw a bunch of {level}s camping there" : $"try {zone}, thats where the {level}s are right now";
        if (dungeon != null)
            return random.Next(2) == 0 ? $"{dungeon} is good around {level} if you can get a group" : $"{dungeon} for {level}, bring a group";
        return StarterZones(bot.Realm, level, random);
    }

    /// <summary>
    /// Play test 2026-10-27: "where should i level?" from a level 1 got no answer, because no bot hunts that low. A new player's
    /// realm has its starter zones (the zones with the most level 1-5 monsters in the world data: Camelot Hills, Vale of Mularn,
    /// Connacht and Lough Derg). Below level 10 only. Pure.
    /// </summary>
    public static string StarterZones(eRealm realm, int level, Random random)
    {
        if (level >= 10)
            return null;
        string zones = realm switch
        {
            eRealm.Albion => "Camelot Hills, right outside Cotswold",
            eRealm.Midgard => "the Vale of Mularn, all around town",
            eRealm.Hibernia => "Connacht or Lough Derg around Mag Mell",
            _ => null,
        };
        if (zones == null)
            return null;
        return random.Next(2) == 0 ? $"at {level}? {zones}, everything there is easy" : $"{zones}. stay there till 5 or so";
    }

    /// <summary>
    /// "where are the mids?": what the realm has seen. The talking update: first the realm's latest sightings on its news board
    /// (frontier bots report what they see), with how long ago; else enemies that a bot or player of the realm can see now;
    /// else that it is quiet. Before, the answer came from where every enemy gamebot really was, seen or not.
    /// </summary>
    public static string EnemySighting(GameBot bot, Random random)
    {
        if (bot == null)
            return null;
        ChatNewsItem latest = AutonomousChatNews.Board.EnemySightings(bot.Realm, DateTime.UtcNow).FirstOrDefault();
        if (latest != null)
            return SightingText(latest, DateTime.UtcNow, random);
        // Only watchers in the frontier can see enemies there (review 2026-10-26: all bots against all enemies was millions of
        // distance checks with a 9,000-bot roster).
        ILookup<ushort, GameLiving> watchers = AutonomousBotRegistry.Snapshot()
            .Where(other => other.Realm == bot.Realm && other.IsAlive && (other.CurrentZone?.IsRvR == true || other.CurrentRegion?.IsRvR == true))
            .Concat<GameLiving>(ClientService.Instance.GetPlayersOfRealm(bot.Realm))
            .ToLookup(watcher => watcher.CurrentRegionID);
        var seen = AutonomousBotRegistry.Snapshot()
            .Where(other => other.Realm != bot.Realm && other.Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia && other.IsAlive &&
                            other.CurrentZone != null && (other.CurrentZone.IsRvR || other.CurrentRegion?.IsRvR == true) &&
                            !string.IsNullOrWhiteSpace(other.CurrentZone.Description) &&
                            watchers[other.CurrentRegionID].Any(watcher => watcher.IsWithinRadius(other, SightRadius)))
            .GroupBy(other => (other.Realm, Zone: other.CurrentZone.Description, ZoneRealm: FrontierHome(other.CurrentZone.Description)))
            // Enemies on our side of the frontier first, then on neutral ground, then in their own homeland (live check
            // 2026-10-21: "where are the mids?" drew "big group of mids in Uppland", Midgard's own frontier).
            .OrderByDescending(group => group.Key.ZoneRealm == bot.Realm ? 2 : group.Key.ZoneRealm == group.Key.Realm ? 0 : 1)
            .ThenByDescending(group => group.Count())
            .FirstOrDefault();
        if (seen == null)
            return random.Next(2) == 0 ? "havent seen any, quiet out there right now" : "quiet atm, no enemies that i know of";
        string enemy = seen.Key.Realm switch { eRealm.Albion => "albs", eRealm.Midgard => "mids", _ => "hibs" };
        // All of them home in their own frontier: say so, rather than "zerg in Uppland" as if they were coming.
        if (seen.Key.ZoneRealm == seen.Key.Realm)
            return random.Next(2) == 0 ? $"{enemy} are sitting in their own frontier, around {seen.Key.Zone}"
                : $"none on our side, {enemy} are back home in {seen.Key.Zone}";
        return seen.Count() >= 8
            ? random.Next(2) == 0 ? $"big group of {enemy} in {seen.Key.Zone}" : $"{enemy} zerg in {seen.Key.Zone} last i heard"
            : random.Next(2) == 0 ? $"saw some {enemy} around {seen.Key.Zone}" : $"a few {enemy} in {seen.Key.Zone}";
    }

    /// <summary>How far a bot or player of the realm sees an enemy for "where are the mids?".</summary>
    public const int SightRadius = 3_500;

    /// <summary>A sighting off the news board in chat words: "6 mids in Emain Macha, couple min ago". Pure.</summary>
    public static string SightingText(ChatNewsItem item, DateTime now, Random random)
    {
        string enemy = item.Enemy switch { eRealm.Albion => "albs", eRealm.Midgard => "mids", _ => "hibs" };
        double minutes = Math.Max(0, (now - item.When).TotalMinutes);
        string when = minutes < 2 ? "just now" : minutes < 6 ? "couple min ago" : $"{(int)minutes} min ago";
        string size = item.Size >= 8 ? "big group of" : item.Size >= 3 ? $"{item.Size}" : "a couple";
        return random.Next(2) == 0 ? $"{size} {enemy} in {item.Zone}, {when}" : $"saw {size} {enemy} at {item.Zone} {when}";
    }

    /// <summary>
    /// Whose side of the frontier a zone is (the world data leaves every zone's realm at 0): Albion's Forest Sauvage, Snowdonia,
    /// Pennine Mountains and Hadrian's Wall; Midgard's Uppland, Yggdra Forest, Jamtland Mountains and Odin's Gate; Hibernia's
    /// Emain Macha, Breifine, Cruachan Gorge and Mount Collory. None for battlegrounds and the rest.
    /// </summary>
    public static eRealm FrontierHome(string zone) => zone switch
    {
        "Forest Sauvage" or "Snowdonia" or "Pennine Mountains" or "Hadrian's Wall" => eRealm.Albion,
        "Uppland" or "Yggdra Forest" or "Jamtland Mountains" or "Odin's Gate" => eRealm.Midgard,
        "Emain Macha" or "Breifine" or "Cruachan Gorge" or "Mount Collory" => eRealm.Hibernia,
        _ => eRealm.None,
    };

    /// <summary>True when the bot is in the place a player named ("camelot", "black mountains", "the frontier").</summary>
    public static bool IsIn(GameBot bot, string place)
    {
        if (bot == null || string.IsNullOrWhiteSpace(place))
            return false;
        if (place is "frontier" or "rvr" or "the frontier")
            return bot.CurrentZone?.IsRvR == true || bot.CurrentRegion?.IsRvR == true;
        // Where the bot is, not where it is going (in-game check 2026-10-21: a bot in Camelot Hills on its way to Camelot
        // answered "anyone in camelot?").
        return PlaceMatches(place, [bot.CurrentZone?.Description, bot.CurrentRegion?.Description],
            KnownPlaces());
    }

    /// <summary>
    /// A place named in chat matches a name that is that place: "camelot" is the City of Camelot, not Camelot Hills (live check
    /// 2026-10-21: "anyone in camelot?" drew "im in Camelot Hills"). When some zone or region is the place itself ("camelot",
    /// "city of camelot", "camelot city"), only those count; otherwise any name containing the words does.
    /// </summary>
    public static bool PlaceMatches(string place, IEnumerable<string> names, IReadOnlyCollection<string> known)
    {
        string wanted = Normal(place);
        if (wanted.Length == 0)
            return false;
        bool exactExists = known.Any(name => IsExactly(Normal(name), wanted));
        foreach (string name in names)
        {
            string n = Normal(name);
            if (n.Length == 0)
                continue;
            if (exactExists ? IsExactly(n, wanted) : (" " + n + " ").Contains(" " + wanted + " ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsExactly(string name, string wanted) => name == wanted || name == "city of " + wanted || name == wanted + " city";

    private static string Normal(string text)
    {
        string t = Regex.Replace((text ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9' ]", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        return t.StartsWith("the ") ? t[4..] : t;
    }

    private static string[] _knownPlaces;

    /// <summary>Every zone and region name of the world, read once.</summary>
    private static IReadOnlyCollection<string> KnownPlaces()
    {
        if (_knownPlaces != null)
            return _knownPlaces;
        try
        {
            _knownPlaces = WorldMgr.GetAllRegions()
                .SelectMany(region => new[] { region.Description }.Concat(region.Zones.Select(zone => zone.Description)))
                .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().ToArray();
        }
        catch
        {
            return [];
        }
        return _knownPlaces;
    }

    /// <summary>"anyone in camelot?" answered by a bot that is there; null from one that isn't.</summary>
    public static string InPlace(GameBot bot, string place, Random random)
    {
        if (!IsIn(bot, place))
            return null;
        string zone = bot.CurrentZone?.Description ?? place;
        return random.Next(3) switch
        {
            0 => $"here, in {zone}",
            1 => $"im in {zone}, whats up?",
            _ => $"yep, {zone}",
        };
    }

    /// <summary>"where are you?": the bot's zone, level and class.</summary>
    public static string WhereIAm(GameBot bot, GamePlayer asker, Random random) =>
        bot == null ? null : AutonomousBotChatCoordinator.WhereText(string.IsNullOrWhiteSpace(bot.CurrentZone?.Description) ? "the frontier" : bot.CurrentZone.Description.Trim(),
            bot.CurrentZone != null && asker != null && bot.CurrentZone == asker.CurrentZone, bot.Level, bot.CharacterClass?.Name ?? "adventurer", random);

    #region Questions with real answers (the talking update audit)

    /// <summary>What a bot knows about itself, for "r u 50 yet", "are u a healer", "do u have speed", "u got pots?".</summary>
    public static AutonomousChatQuestions.BotFacts FactsOf(GameBot bot)
    {
        int potions = 0, equipped = 0, crafted = 0;
        try
        {
            potions = bot.Inventory?.AllItems.Count(item => item != null && item.SlotPosition >= (int)eInventorySlot.FirstBackpack &&
                                                          item.SlotPosition <= (int)eInventorySlot.LastBackpack &&
                                                          item.Name?.Contains("potion", StringComparison.OrdinalIgnoreCase) == true) ?? 0;
            // "whered u get that armor": worn pieces, and how many a crafter made (IsCrafted; Creator also holds the mob a drop came
            // from, review 2026-10-26).
            foreach (DbInventoryItem item in bot.Inventory?.EquippedItems ?? [])
            {
                if (item == null || item.SlotPosition is < (int)eInventorySlot.MinEquipable or > (int)eInventorySlot.MaxEquipable)
                    continue;
                equipped++;
                if (item.IsCrafted)
                    crafted++;
            }
        }
        catch (InvalidOperationException)
        {
        }
        bool speed = bot.Spells?.Any(spell => spell?.SpellType == eSpellType.SpeedEnhancement && spell.Level <= bot.Level) == true;
        return new AutonomousChatQuestions.BotFacts(bot.DatabaseID, bot.Level, bot.CharacterClass?.Name ?? bot.ClassName, bot.Realm,
            bot.RealmLevel, AutonomousBotPersonality.For(bot).Type, speed, potions, bot.Guild?.Name, equipped, crafted,
            bot.InCombat || BotGroupInvite.DutyOf(bot) != BotGroupInvite.Duty.None);
    }

    private static readonly Dictionary<eRealm, (long At, AutonomousChatQuestions.WorldFacts Facts)> Worlds = new();

    /// <summary>What a realm knows of the world (who holds Darkness Falls, the relics, the keeps, the places), cached 10 s.</summary>
    public static AutonomousChatQuestions.WorldFacts WorldFor(eRealm realm)
    {
        long now = GameLoop.GameLoopTime;
        lock (Worlds)
            if (Worlds.TryGetValue(realm, out var cached) && now - cached.At < 10_000)
                return cached.Facts;
        var relics = new List<(string, eRealm, eRealm, bool)>();
        var keeps = new List<(string, eRealm)>();
        try
        {
            foreach (GameRelic relic in RelicMgr.GetRelics() ?? [])
                if (relic != null)
                    relics.Add((relic.Name, relic.Realm, relic.OriginalRealm, relic.IsMounted));
            foreach (var keep in GameServer.KeepManager?.GetAllKeeps() ?? [])
                if (keep != null && !keep.IsPortalKeep && !BattlegroundBrackets.IsBattlegroundRegion(keep.Region))
                    keeps.Add((keep.Name, keep.Realm));
        }
        catch (Exception)
        {
            // The world isn't up (tests): answer from places alone.
        }
        eRealm df = eRealm.None;
        try { df = DFEnterJumpPoint.DarknessFallOwner; } catch (Exception) { }
        var facts = new AutonomousChatQuestions.WorldFacts(df, relics, keeps, AutonomousChatPlaces.All);
        lock (Worlds)
            Worlds[realm] = (now, facts);
        return facts;
    }

    /// <summary>
    /// A bot's answer to a question it can answer truly: about itself when the question is said to it (<paramref name="addressed"/>:
    /// named, by tell, in its group), or about the world (place levels, Darkness Falls, relics, keeps). Null when it can't.
    /// </summary>
    public static string AnswerQuestion(GameBot bot, string text, bool addressed, Random random)
    {
        if (bot == null || string.IsNullOrWhiteSpace(text))
            return null;
        // "torvald u still a thane?": the bot's own name is not part of the question (the holdout check).
        string unnamed = AutonomousChatThreads.WithoutName(text, bot.Name);
        if (!string.IsNullOrWhiteSpace(unnamed))
            text = unnamed;
        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(text);
        if (intent is eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Abuse)
            return null;
        string self = addressed ? AutonomousChatQuestions.SelfQuestion(text) : null;
        string answer = self != null
            ? AutonomousChatQuestions.AnswerSelf(self, text, FactsOf(bot), random)
            : AutonomousChatQuestions.AnswerWorld(text, bot.Realm, WorldFor(bot.Realm), random);
        return answer == null ? null : AutonomousBotVoices.StyleFor(bot, answer);
    }

    #endregion
}
