using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Movement;

namespace DOL.GS;

/// <summary>
/// Read-only Classic/SI knowledge used by bot chat. World locations are indexed
/// once, on the first actual player question; spell questions read the player's
/// own class lines. Nothing runs on an AI tick and no external service is used.
/// </summary>
public static class AutonomousChatKnowledge
{
    private sealed record Place(string Name, string Area, ushort RegionId, bool IsDungeon, bool IsMonster, byte Level);

    private static readonly object IndexSync = new();
    private static IReadOnlyDictionary<string, Place[]> _places;

    public static bool TryAnswer(GamePlayer player, string message, out string answer)
    {
        answer = string.Empty;
        if (player == null || string.IsNullOrWhiteSpace(message))
            return false;

        string query = message.Trim();
        if (TryExplainTerm(query, out answer))
            return true;
        if (LooksLikeStableQuestion(query) && TryAnswerStable(player.Realm, query, out answer))
            return true;
        if (LooksLikeSpellQuestion(query) && TryAnswerSpell(player, query, out answer))
            return true;
        if (LooksLikeTrainerQuestion(query) && TryAnswerTrainer(player.Realm, query, player.CharacterClass?.ID ?? 0, player, out answer))
            return true;
        return LooksLikeLocationQuestion(query) && TryAnswerLocation(player.Realm, query, out answer);
    }

    public static bool TryAnswer(eRealm realm, string message, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string query = message.Trim();
        // A bot's own line is answered only when it really asks something (in game "ranger, recurve bow, pathfinding"
        // drew a location answer about an NPC named Ranger; audit 2026-10-18).
        if (AutonomousBotVoices.Enabled && !query.Contains('?'))
            return false;
        if (LooksLikeStableQuestion(query) && TryAnswerStable(realm, query, out answer))
            return true;
        if (LooksLikeTrainerQuestion(query) && TryAnswerTrainer(realm, query, 0, null, out answer))
            return true;
        return LooksLikeLocationQuestion(query) && TryAnswerLocation(realm, query, out answer);
    }

    #region Chat words (0.36-se.4)

    // What period chat words mean. Live test 2026-10-18: "what does lfg mean?" drew "what level range?".
    private static readonly Dictionary<string, string> Terms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lfg"] = "looking for group", ["lfm"] = "looking for more, a group wants people", ["lf1m"] = "looking for one more",
        ["gz"] = "congrats", ["grats"] = "congrats", ["inc"] = "incoming, enemies or adds coming", ["oom"] = "out of mana",
        ["oop"] = "out of power", ["brb"] = "be right back", ["afk"] = "away from keyboard", ["bio"] = "bathroom break lol",
        ["ks"] = "kill steal, taking someone elses mob", ["pbae"] = "point blank area effect, the caster blows up everything around him",
        ["pbaoe"] = "point blank area effect, the caster blows up everything around him", ["rp"] = "realm points, you get them in rvr",
        ["rps"] = "realm points, you get them in rvr", ["rr"] = "realm rank, from realm points", ["rvr"] = "realm vs realm, the frontier",
        ["wts"] = "want to sell", ["wtb"] = "want to buy", ["wtt"] = "want to trade", ["pst"] = "please send tell",
        ["pc"] = "price check", ["con"] = "how tough a mob looks, by its color", ["add"] = "another mob joining the fight",
        ["adds"] = "more mobs joining the fight", ["ding"] = "level up", ["rez"] = "resurrect", ["aggro"] = "a mob that attacks on sight",
        ["df"] = "darkness falls, the big dungeon", ["ml"] = "master level", ["ra"] = "realm ability", ["np"] = "no problem",
        ["ty"] = "thank you", ["lol"] = "laughing out loud", ["imo"] = "in my opinion", ["mez"] = "mesmerize, it holds a mob still",
        ["stun"] = "they cant move or act for a bit", ["zerg"] = "a huge mob of players running together", ["gank"] = "killing someone who cant fight back",
    };

    /// <summary>"what does lfg mean?", "whats pbae", "what is a con": a short meaning.</summary>
    public static bool TryExplainTerm(string text, out string answer)
    {
        answer = string.Empty;
        string t = (text ?? string.Empty).Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
        var m = System.Text.RegularExpressions.Regex.Match(t,
            @"^(?:so |um |hey |sorry |noob question,? |dumb question,? )?(?:what does|what do|wat does|what is|whats|what's|wat is|wats) (?:an? )?([a-z0-9]+)(?: mean| stand for| means)?$");
        if (!m.Success || !Terms.TryGetValue(m.Groups[1].Value, out string meaning))
            return false;
        string word = m.Groups[1].Value;
        answer = Pick($"{word} = {meaning}", $"{meaning}", $"it means {meaning}", $"{meaning} :)");
        return true;
    }

    #endregion

    #region Trainers (0.36-se.4)

    /// <summary>"where is the scout trainer", "where do i train", "who trains clerics" (playtest 2026-10-18: a new Scout
    /// spent 25 minutes finding Lt. Rydderac, and a bot by /send named the wrong zone).</summary>
    public static bool LooksLikeTrainerQuestion(string text) =>
        HasWord(text, "trainer") || HasWord(text, "trainers") || HasPhrase(text, "where do i train") ||
        HasPhrase(text, "where to train") || HasPhrase(text, "who trains") || HasPhrase(text, "where can i train") ||
        HasPhrase(text, "where do you train");

    private static bool TryAnswerTrainer(eRealm realm, string query, int askerClassId, GamePlayer asker, out string answer)
    {
        answer = string.Empty;
        // The class named in the question, else the asker's own class.
        int classId = 0;
        foreach (ICharacterClass cls in AllClasses())
        {
            if (cls != null && !string.IsNullOrEmpty(cls.Name) && (HasWord(query, cls.Name) || HasWord(query, cls.Name + "s")))
            {
                classId = cls.ID;
                break;
            }
        }
        if (classId == 0)
            classId = askerClassId;
        if (classId == 0)
            return false;
        var trainers = TrainersOf(realm, classId);
        if (trainers.Count == 0)
            return false;
        // The nearest one in the asker's own region, then the capital.
        var best = trainers.OrderByDescending(t => asker != null && t.Region == asker.CurrentRegionID)
            .ThenBy(t => asker != null && t.Region == asker.CurrentRegionID ? Math.Pow(t.X - asker.X, 2) + Math.Pow(t.Y - asker.Y, 2) : 0)
            .ThenByDescending(t => t.Capital).First();
        string className = AllClasses().FirstOrDefault(c => c?.ID == classId)?.Name?.ToLowerInvariant() ?? "your class";
        var other = trainers.FirstOrDefault(t => t.Zone != best.Zone);
        // Worded to read right when a voice drops the commas (live test 2026-10-18: "Lieutenant Mhoudi in Campacorentin Forest they
        // train scouts").
        answer = other.Name != null && Random.Shared.Next(3) == 0
            ? $"{best.Name} in {best.Zone} trains {className}s, theres one in {other.Zone} too"
            : Pick($"{className} trainer is {best.Name} in {best.Zone}", $"{best.Name} trains {className}s in {best.Zone}",
                $"go see {best.Name} in {best.Zone}");
        return true;
    }

    private static readonly object TrainerSync = new();
    private static Dictionary<(eRealm Realm, int ClassId), List<(string Name, string Zone, ushort Region, bool Capital, int X, int Y)>> _trainers;

    /// <summary>The trainers of a class a realm can reach, indexed once on the first trainer question.</summary>
    private static List<(string Name, string Zone, ushort Region, bool Capital, int X, int Y)> TrainersOf(eRealm realm, int classId)
    {
        if (_trainers == null)
        {
            lock (TrainerSync)
            {
                if (_trainers == null)
                {
                    var index = new Dictionary<(eRealm, int), List<(string, string, ushort, bool, int, int)>>();
                    foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                                 AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion)))
                    {
                        foreach (GameTrainer trainer in region.Objects.OfType<GameTrainer>())
                        {
                            if (trainer?.ObjectState is not GameObject.eObjectState.Active || trainer.TrainedClass == eCharacterClass.Unknown)
                                continue;
                            foreach (eRealm reach in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                            {
                                if (!RegionIsAccessible(reach, region.ID) || trainer.Realm != reach && trainer.Realm != eRealm.None)
                                    continue;
                                var key = (reach, (int)trainer.TrainedClass);
                                if (!index.TryGetValue(key, out var list))
                                    index[key] = list = new();
                                list.Add((trainer.Name, trainer.CurrentZone?.Description ?? region.Description, region.ID,
                                    AutonomousBotVoices.IsCapitalRegion(region.ID), trainer.X, trainer.Y));
                            }
                        }
                    }
                    _trainers = index;
                }
            }
        }
        return _trainers.TryGetValue((realm, classId), out var found) ? found : new();
    }

    private static ICharacterClass[] _classes;

    private static ICharacterClass[] AllClasses() => _classes ??= LoadClasses().ToArray();

    private static IEnumerable<ICharacterClass> LoadClasses()
    {
        foreach (eCharacterClass value in Enum.GetValues<eCharacterClass>())
        {
            ICharacterClass cls = null;
            try
            {
                cls = ScriptMgr.FindCharacterClass((int)value);
            }
            catch
            {
                // a class the server doesn't script
            }
            if (cls != null)
                yield return cls;
        }
    }

    #endregion

    private static bool TryAnswerStable(eRealm realm, string query, out string answer)
    {
        answer = string.Empty;
        var routes = new List<(string Master, string Area, string Destination)>();
        foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                     AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion) &&
                     RegionIsAccessible(realm, region.ID)))
        {
            foreach (GameStableMaster master in region.Objects.OfType<GameStableMaster>()
                         .Where(master => master.ObjectState is GameObject.eObjectState.Active &&
                                          (master.Realm == realm || master.Realm == eRealm.None) && master.TradeItems != null))
            {
                foreach (DictionaryEntry entry in master.TradeItems.GetAllItems())
                {
                    if (entry.Value is not DbItemTemplate ticket || ticket.Item_Type != 40)
                        continue;
                    string destination = TicketDestination(ticket.Name);
                    PathPoint path = MovementMgr.LoadPath(ticket.Id_nb);
                    PathPoint endpoint = path;
                    int guard = 0;
                    while (endpoint?.Next != null && guard++ < 5000)
                        endpoint = endpoint.Next;
                    string endpointArea = endpoint == null ? string.Empty : region.GetZone(endpoint.X, endpoint.Y)?.Description ?? string.Empty;
                    if (!ContainsPhrase(query, destination) && !ContainsPhrase(query, endpointArea))
                        continue;
                    routes.Add((master.Name, master.CurrentZone?.Description ?? region.Description,
                        string.IsNullOrWhiteSpace(endpointArea) ? destination : endpointArea));
                }
            }
        }

        (string Master, string Area, string Destination)[] matches = routes.Distinct().Take(3).ToArray();
        if (matches.Length == 0)
            return false;
        string masters = string.Join(" or ", matches.Take(2).Select(route => $"{route.Master} in {route.Area}"));
        answer = Pick($"for {matches[0].Destination} get a ticket from {masters}", $"{masters} sells tickets to {matches[0].Destination}",
            $"horse to {matches[0].Destination}: {masters}");
        return true;
    }

    private static bool TryAnswerSpell(GamePlayer player, string query, out string answer)
    {
        answer = string.Empty;
        var candidates = new List<(Spell Spell, SpellLine Line)>();
        foreach (Specialization specialization in player.GetSpecList())
        {
            foreach (SpellLine line in specialization.PretendSpellLinesForLiving(player, 50))
            {
                foreach (Spell spell in SkillBase.GetSpellList(line.KeyName))
                {
                    if (spell?.Level is < 1 or > 50 || string.IsNullOrWhiteSpace(spell.Name) ||
                        !ContainsPhrase(query, spell.Name))
                        continue;
                    candidates.Add((spell, line));
                }
            }
        }

        (Spell Spell, SpellLine Line) match = candidates
            .DistinctBy(entry => (entry.Spell.Name.ToLowerInvariant(), entry.Spell.Level, entry.Line.KeyName))
            .OrderByDescending(entry => entry.Spell.Name.Length)
            .ThenBy(entry => entry.Spell.Level)
            .FirstOrDefault();
        if (match.Spell == null)
            return false;

        answer = match.Line.IsBaseLine
            ? Pick($"{match.Spell.Name} comes at level {match.Spell.Level}", $"you get {match.Spell.Name} at {match.Spell.Level}, from {match.Line.Name}")
            : Pick($"{match.Spell.Name} needs {match.Spell.Level} in {match.Line.Name}", $"{match.Spell.Level} {match.Line.Name} for {match.Spell.Name}");
        return true;
    }

    private static bool TryAnswerLocation(eRealm realm, string query, out string answer)
    {
        answer = string.Empty;
        IReadOnlyDictionary<string, Place[]> index = GetPlaces();
        KeyValuePair<string, Place[]> match = index
            .Where(entry => entry.Key.Length >= 3 && ContainsPhrase(query, entry.Key))
            .OrderByDescending(entry => entry.Key.Length)
            .FirstOrDefault();
        if (match.Value == null)
            return false;

        Place[] reachable = match.Value.Where(place => RegionIsAccessible(realm, place.RegionId)).ToArray();
        if (reachable.Length == 0)
            return false;
        Place place = reachable
            .GroupBy(entry => (entry.Area, entry.RegionId, entry.IsDungeon, entry.IsMonster))
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .First();

        if (place.IsMonster)
        {
            answer = place.IsDungeon
                ? Pick($"{place.Name}? inside {place.Area}, bring a group", $"{place.Name} is in {place.Area}, deep in", $"inside {place.Area}, careful going in")
                : Pick($"{place.Name}? around {place.Area}", $"{place.Name} are out in {place.Area}", $"try {place.Area}, seen them there");
            return true;
        }

        if (place.IsDungeon)
        {
            DbZonePoint entrance = DOLDB<DbZonePoint>.SelectObjects(DB.Column("TargetRegion").IsEqualTo(place.RegionId))
                .Where(point => point.SourceRegion != 0 && (point.Realm == 0 || point.Realm == (ushort)realm))
                .FirstOrDefault(point => RegionIsAccessible(realm, point.SourceRegion));
            if (entrance != null)
            {
                Region source = WorldMgr.GetRegion(entrance.SourceRegion);
                string sourceArea = source?.GetZone(entrance.SourceX, entrance.SourceY)?.Description ?? source?.Description;
                if (!string.IsNullOrWhiteSpace(sourceArea))
                {
                    answer = Pick($"{place.Name} entrance is in {sourceArea}", $"{place.Name}? the way in is in {sourceArea}",
                        $"head to {sourceArea}, {place.Name} entrance is there");
                    return true;
                }
            }
            answer = Pick($"{place.Name} is on our side, look for the entrance", $"{place.Name}? its a dungeon on our side, bring a group");
            return true;
        }

        if (AutonomousBotVoices.IsCapitalRegion(place.RegionId) && place.Area.Equals(place.Name, StringComparison.OrdinalIgnoreCase))
        {
            answer = Pick($"{place.Name}? any stable master runs a horse there", $"take a horse to {place.Name}, the roads all lead there",
                $"{place.Name}, the capital, a stable master gets you there");
            return true;
        }

        answer = place.Area.Equals(place.Name, StringComparison.OrdinalIgnoreCase)
            ? Pick($"{place.Name} is its own zone, a stable master can get you close", $"{place.Name}? take a horse from a stable master")
            : Pick($"{place.Name} is in {place.Area}", $"{place.Name}? thats in {place.Area}", $"in {place.Area}, follow the road",
                $"{place.Area}, a horse gets you most of the way");
        return true;
    }

    private static IReadOnlyDictionary<string, Place[]> GetPlaces()
    {
        if (_places != null)
            return _places;
        lock (IndexSync)
        {
            if (_places != null)
                return _places;

            var building = new Dictionary<string, List<Place>>(StringComparer.OrdinalIgnoreCase);
            foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                         AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion)))
            {
                bool capital = AutonomousBotVoices.IsCapitalRegion(region.ID); // the capitals look like dungeons to the server
                Add(building, region.Description, new(region.Description, region.Description, region.ID, region.IsDungeon && !capital, false, 0));
                foreach (Zone zone in region.Zones.Where(zone => zone != null))
                    Add(building, zone.Description, new(zone.Description, region.Description, region.ID, zone.IsDungeon && !capital, false, 0));
                // "where is camelot?": nobody types "camelot city" or "city of camelot" (live test 2026-10-18).
                if (capital && ShortCityName(region.Description) is string shortName)
                    Add(building, shortName, new(region.Description, region.Description, region.ID, false, false, 0));

                foreach (GameNPC npc in region.Objects.OfType<GameNPC>())
                {
                    if (npc?.ObjectState is not GameObject.eObjectState.Active || string.IsNullOrWhiteSpace(npc.Name) || npc.CurrentZone == null)
                        continue;
                    bool monster = npc.Realm == eRealm.None && npc.Level > 0 && npc.Brain != null &&
                                   (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 &&
                                   npc is not GameMerchant and not GameGuard and not GameTaxi and not GameSummonedPet and
                                   not GameTrainer and not GameTeleporter and not GameHealer and not CraftNPC;
                    // 0.36-se.4: guards, trainers and NPCs named after a role ("Healer", "Armsman", "Ranger") are not
                    // places; a short name is matched only as a whole word (see ContainsPhrase).
                    if (!monster && (npc is GameGuard or GameSummonedPet || IsRoleName(npc.Name) || npc.Name.Trim().Length < 5))
                        continue;
                    if (monster && npc.Name.Trim().Length < 4)
                        continue;
                    Add(building, npc.Name, new(npc.Name, npc.CurrentZone.Description, region.ID,
                        npc.CurrentZone.IsDungeon || region.IsDungeon, monster, npc.Level));
                }
            }
            _places = building.ToDictionary(entry => entry.Key, entry => entry.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
            return _places;
        }
    }

    /// <summary>"Camelot City" or "City of Camelot" -> "Camelot"; null for other names.</summary>
    public static string ShortCityName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (name.StartsWith("City of ", StringComparison.OrdinalIgnoreCase))
            return name["City of ".Length..].Trim();
        if (name.EndsWith(" City", StringComparison.OrdinalIgnoreCase))
            return name[..^" City".Length].Trim();
        return null;
    }

    private static void Add(Dictionary<string, List<Place>> index, string key, Place place)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (!index.TryGetValue(key.Trim(), out List<Place> places))
            index[key.Trim()] = places = new();
        places.Add(place);
    }

    public static bool LooksLikeLocationQuestion(string text) =>
        HasWord(text, "where") || HasWord(text, "wheres") || HasWord(text, "where's") || HasWord(text, "find") ||
        HasWord(text, "located") || HasWord(text, "location") || HasPhrase(text, "how do i get") || HasPhrase(text, "how do you get") ||
        HasPhrase(text, "how to get");

    private static bool LooksLikeStableQuestion(string text) =>
        text.Contains("stable master", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("stablemaster", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("horse to", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ticket to", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSpellQuestion(string text) =>
        text.Contains("what level", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("which level", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("when do i learn", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("when can i cast", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("learn to cast", StringComparison.OrdinalIgnoreCase);

    /// <summary>The phrase as whole words (audit 2026-10-18: "cleric trainer" matched the NPC "Raine" in "tRAINEr",
    /// and "camelot" the monster "Mel").</summary>
    public static bool ContainsPhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase) || string.IsNullOrEmpty(text))
            return false;
        string needle = phrase.Trim();
        int start = 0;
        while ((start = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = start + needle.Length;
            if ((start == 0 || !char.IsLetterOrDigit(text[start - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end])))
                return true;
            start = end;
        }
        return false;
    }

    private static bool HasWord(string text, string word) => ContainsPhrase(text, word);

    private static bool HasPhrase(string text, string phrase) => ContainsPhrase(text, phrase);

    private static readonly HashSet<string> RoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "healer", "guard", "merchant", "trainer", "master", "stable master", "stablemaster", "smith", "blacksmith", "armorer",
        "armourer", "weaponsmith", "tailor", "fletcher", "enchanter", "vault keeper", "banker", "guildmaster", "guild registrar",
        "innkeeper", "bartender", "hastener", "townsman", "townswoman", "villager", "peasant", "farmer", "messenger", "herald",
        "captain", "sergeant", "lieutenant", "commander", "soldier", "watchman", "sentry", "archer", "scout", "ranger", "hunter",
        "mage", "wizard", "cleric", "priest", "monk", "friar", "druid", "bard", "warrior", "armsman", "champion", "hero", "warden",
    };

    /// <summary>An NPC name that is a role or a class, not someone players name ("Healer", "Armsman", "Ranger").</summary>
    public static bool IsRoleName(string name) => string.IsNullOrWhiteSpace(name) || RoleNames.Contains(name.Trim()) ||
        AllClassNames.Contains(name.Trim());

    private static readonly HashSet<string> AllClassNames = new(Enum.GetNames<eCharacterClass>(), StringComparer.OrdinalIgnoreCase);

    private static string Pick(params string[] options) => options[Random.Shared.Next(options.Length)];

    private static string TicketDestination(string name)
    {
        string value = name?.Trim() ?? string.Empty;
        int marker = value.IndexOf("ticket to", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 ? value[(marker + "ticket to".Length)..].Trim() : value;
    }

    // Matches the authoritative Classic/SI homeland ownership used by /mobs and player travel.
    private static bool RegionIsAccessible(eRealm realm, ushort regionId)
    {
        if (regionId is 1 or 10 or 20 or 21 or 22 or 23 or 24 or 50 or 51 or 60 or 61 or 62)
            return realm == eRealm.Albion;
        if (regionId is 100 or 101 or 125 or 126 or 127 or 128 or 129 or 150 or 151 or 160 or 161)
            return realm == eRealm.Midgard;
        if (regionId is 180 or 181 or 190 or 191 or 192 or 193 or 194 or 200 or 201 or 220 or 221 or 222 or 223 or 224)
            return realm == eRealm.Hibernia;
        return true;
    }
}
