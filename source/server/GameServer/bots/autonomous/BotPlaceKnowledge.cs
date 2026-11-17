using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS;

/// <summary>How a bot came to know a place (the BOT_PLACE_LEARNED line's how=).</summary>
public enum BotPlaceLearning : byte
{
    /// <summary>It was there (arrived in the zone, the town's zone, the dungeon or the capital).</summary>
    Visited,
    /// <summary>Someone told it the way (WP4): half known until it goes there.</summary>
    Told,
    /// <summary>What a player of its level would know (owner decision 8), at first sight and as it levels.</summary>
    Seeded,
}

/// <summary>What a bot knows of one place.</summary>
public enum BotPlaceKnowing : byte
{
    Unknown,
    /// <summary>Told the way, never been there.</summary>
    HalfKnown,
    Known,
}

/// <summary>
/// One place a bot can know: a zone, town, capital, dungeon, frontier spot or battleground of data/chat/places.json, by its
/// saved bit (<see cref="BotPlaceKnowledge.BitOrder"/>). <see cref="None"/> (the default) is no place. Compare and hash it
/// freely; <see cref="Name"/> is the places.json key ("lyonesse"). Pure.
/// </summary>
public readonly record struct BotPlaceKey
{
    // The bit plus one, so the default value is None.
    private readonly short _slot;

    private BotPlaceKey(int bit) => _slot = (short)(bit + 1);

    public static BotPlaceKey None => default;

    public bool IsNone => _slot == 0;

    /// <summary>The place's bit in the saved sets, or -1 for None.</summary>
    public int Bit => _slot - 1;

    /// <summary>The places.json key ("tir na nog"), or "" for None.</summary>
    public string Name => IsNone ? string.Empty : BotPlaceKnowledge.BitOrder[Bit];

    /// <summary>The place with this bit, or None when no place has it.</summary>
    public static BotPlaceKey FromBit(int bit) => bit >= 0 && bit < BotPlaceKnowledge.BitOrder.Length ? new BotPlaceKey(bit) : default;

    /// <summary>The place with this places.json key (any case), or None.</summary>
    public static BotPlaceKey Of(string name) =>
        name != null && BotPlaceKnowledge.BitsByName.TryGetValue(name.Trim(), out int bit) ? new BotPlaceKey(bit) : default;

    public override string ToString() => Name;
}

/// <summary>
/// The places one person knows in one realm: the two bit sets of an <see cref="OfflineBotPlacesRecord"/> (hex text,
/// <see cref="BotPeopleBits"/>) as a value. A place is known, half known (told the way) or unknown; a known place is never
/// half known. Pure: every change returns a new set.
/// </summary>
public readonly record struct BotPlaceSet
{
    private readonly string _known;
    private readonly string _halfKnown;

    public BotPlaceSet(string known, string halfKnown)
    {
        _known = BotPeopleBits.Normalize(known);
        _halfKnown = BotPeopleBits.Except(halfKnown, _known);
    }

    public static BotPlaceSet Empty => new(string.Empty, string.Empty);

    public static BotPlaceSet Of(OfflineBotPlacesRecord row) => row == null ? Empty : new BotPlaceSet(row.Known, row.HalfKnown);

    public static BotPlaceSet Of(IEnumerable<BotPlaceKey> known) =>
        (known ?? []).Aggregate(Empty, (set, place) => set.Learn(place, BotPlaceLearning.Seeded));

    /// <summary>The known places' bits (hex, as saved).</summary>
    public string Known => _known ?? string.Empty;

    /// <summary>The half-known places' bits (hex, as saved).</summary>
    public string HalfKnown => _halfKnown ?? string.Empty;

    public BotPlaceKnowing Knowing(BotPlaceKey place) =>
        place.IsNone ? BotPlaceKnowing.Unknown
        : BotPeopleBits.Has(Known, place.Bit) ? BotPlaceKnowing.Known
        : BotPeopleBits.Has(HalfKnown, place.Bit) ? BotPlaceKnowing.HalfKnown
        : BotPlaceKnowing.Unknown;

    public bool Knows(BotPlaceKey place) => Knowing(place) == BotPlaceKnowing.Known;

    public bool HalfKnows(BotPlaceKey place) => Knowing(place) == BotPlaceKnowing.HalfKnown;

    /// <summary>The set after learning a place: visited or seeded makes it known; told makes an unknown place half known.</summary>
    public BotPlaceSet Learn(BotPlaceKey place, BotPlaceLearning how)
    {
        if (place.IsNone)
            return this;
        return how == BotPlaceLearning.Told
            ? Knows(place) ? this : new BotPlaceSet(Known, BotPeopleBits.With(HalfKnown, place.Bit))
            : new BotPlaceSet(BotPeopleBits.With(Known, place.Bit), HalfKnown);
    }

    /// <summary>Both sets joined (a known place in either is known).</summary>
    public BotPlaceSet Union(BotPlaceSet other) =>
        new(BotPeopleBits.Union(Known, other.Known), BotPeopleBits.Union(HalfKnown, other.HalfKnown));

    public int KnownCount => BotPeopleBits.Count(Known);

    public int HalfKnownCount => BotPeopleBits.Count(HalfKnown);

    public IEnumerable<BotPlaceKey> KnownPlaces => BotPeopleBits.Indexes(Known).Select(BotPlaceKey.FromBit).Where(p => !p.IsNone);

    public IEnumerable<BotPlaceKey> HalfKnownPlaces => BotPeopleBits.Indexes(HalfKnown).Select(BotPlaceKey.FromBit).Where(p => !p.IsNone);

    /// <summary>The places in <paramref name="other"/> that this set does not know.</summary>
    public IEnumerable<BotPlaceKey> NewIn(BotPlaceSet other)
    {
        BotPlaceSet self = this;
        return other.KnownPlaces.Where(place => !self.Knows(place));
    }
}

/// <summary>
/// The places of data/chat/places.json by bit, with the lookups the rules need: what a zone or region description is
/// (<see cref="At"/>, built once, no allocation per call), what a travel destination is (<see cref="For"/>), and each
/// realm's capital, classic start town and first border keep. <see cref="Current"/> is the server's (the embedded
/// places.json); tests build their own. Pure apart from <see cref="Current"/>'s loading.
/// </summary>
public sealed class BotPlaceCatalog
{
    private static readonly Lazy<BotPlaceCatalog> Loaded = new(() => new BotPlaceCatalog(AutonomousChatPlaces.All));

    public static BotPlaceCatalog Current => Loaded.Value;

    private readonly AutonomousChatPlaces.Place[] _byBit;
    private readonly Dictionary<string, BotPlaceKey[]> _at = new(StringComparer.OrdinalIgnoreCase);

    public BotPlaceCatalog(IEnumerable<AutonomousChatPlaces.Place> places)
    {
        _byBit = new AutonomousChatPlaces.Place[BotPlaceKnowledge.BitOrder.Length];
        var at = new Dictionary<string, List<BotPlaceKey>>(StringComparer.OrdinalIgnoreCase);
        var unlisted = new List<string>();
        foreach (AutonomousChatPlaces.Place place in places ?? [])
        {
            BotPlaceKey key = BotPlaceKey.Of(place?.Key);
            if (key.IsNone)
            {
                if (place != null)
                    unlisted.Add(place.Key);
                continue;
            }
            _byBit[key.Bit] = place;
            foreach (string zone in place.Zones ?? [])
            {
                if (!at.TryGetValue(zone, out List<BotPlaceKey> list))
                    at[zone] = list = new List<BotPlaceKey>();
                if (!list.Contains(key))
                    list.Add(key);
            }
        }
        foreach (var (zone, list) in at)
            _at[zone] = list.ToArray();
        Unlisted = unlisted.ToArray();
        All = Enumerable.Range(0, _byBit.Length).Where(bit => _byBit[bit] != null).Select(BotPlaceKey.FromBit).ToArray();
    }

    /// <summary>Every place of the catalog, by bit.</summary>
    public IReadOnlyList<BotPlaceKey> All { get; }

    /// <summary>Places of the file that have no bit (a new place must be appended to <see cref="BotPlaceKnowledge.BitOrder"/>).</summary>
    public IReadOnlyList<string> Unlisted { get; }

    /// <summary>The places.json entry of a place, or null.</summary>
    public AutonomousChatPlaces.Place PlaceOf(BotPlaceKey place) => place.IsNone ? null : _byBit[place.Bit];

    /// <summary>
    /// The places a zone or region description is: the zone itself, the towns in it, a capital or dungeon of that name
    /// ("Vale of Mularn" is the vale, Mularn, Haggerfel and Fort Veldon). The array is shared: do not change it.
    /// </summary>
    public BotPlaceKey[] At(string zoneOrRegion) =>
        !string.IsNullOrWhiteSpace(zoneOrRegion) && _at.TryGetValue(zoneOrRegion.Trim(), out BotPlaceKey[] keys) ? keys : [];

    /// <summary>The places a travel destination names ("Myrkwood Forest", "keep Nottmoor", "Mularn"), as AutonomousChatPlaces.KeysFor.</summary>
    public IEnumerable<BotPlaceKey> For(string destination) =>
        AutonomousChatPlaces.KeysFor(destination, _byBit.Where(place => place != null)).Select(BotPlaceKey.Of).Where(key => !key.IsNone);

    /// <summary>"alb", "mid", "hib" (places.json's realm codes), or null.</summary>
    public static string RealmCode(eRealm realm) => realm switch
    {
        eRealm.Albion => "alb",
        eRealm.Midgard => "mid",
        eRealm.Hibernia => "hib",
        _ => null,
    };

    public bool IsOfRealm(BotPlaceKey place, eRealm realm) =>
        PlaceOf(place) is { } entry && string.Equals(entry.Realm, RealmCode(realm), StringComparison.OrdinalIgnoreCase);

    public static bool IsType(AutonomousChatPlaces.Place place, string type) =>
        place != null && string.Equals(place.Type, type, StringComparison.OrdinalIgnoreCase);

    /// <summary>The realm's capital (Camelot, Jordheim, Tir na Nog).</summary>
    public BotPlaceKey Capital(eRealm realm) =>
        All.FirstOrDefault(key => IsOfRealm(key, realm) && IsType(PlaceOf(key), "capital"));

    /// <summary>The realm's classic start town (Cotswold, Mularn, Mag Mell): a home town when the bind names none.</summary>
    public BotPlaceKey StartTown(eRealm realm) => BotPlaceKey.Of(realm switch
    {
        eRealm.Albion => "cotswold",
        eRealm.Midgard => "mularn",
        eRealm.Hibernia => "mag mell",
        _ => null,
    });

    /// <summary>The realm's first border keep (AutonomousRvrStaging: Castle Sauvage, Svasud Faste, Druim Ligen).</summary>
    public BotPlaceKey BorderKeep(eRealm realm)
    {
        IReadOnlyList<AutonomousRvrStaging.BorderKeep> keeps = AutonomousRvrStaging.BorderKeeps(realm);
        return keeps.Count == 0 ? BotPlaceKey.None : BotPlaceKey.Of(keeps[0].Name);
    }
}

/// <summary>
/// The facts the place rules need about one gamebot, as plain values (<see cref="Of"/> reads a live bot; tests build their
/// own). <see cref="HomeZone"/> is read only when the bot is seeded.
/// </summary>
public readonly record struct BotPlaceSubject(BotPersonKey Person, long CharacterId, string Name, eRealm Realm, int Level,
    string BindZone = null, GameBot Bot = null)
{
    public static BotPlaceSubject Of(GameBot bot, BotPersonKey person) =>
        new(person, bot.DatabaseID, bot.Name, bot.Realm, bot.Level, null, bot);

    /// <summary>The zone of its bind point: <see cref="BindZone"/>, else the live bot's (BotPlaceKnowledge.BindZoneOf).</summary>
    public string HomeZone => BindZone ?? (Bot != null ? BotPlaceKnowledge.BindZoneOf(Bot) : null);
}

/// <summary>
/// Bots as people, item 4, work package 3 (docs/BOTS-AS-PEOPLE-1.2.md, "Knowing only what a player would know"): each
/// autonomous gamebot's memory of the places it has been, per person and realm, saved through WP0's store
/// (offline_bot_places). A bot starts knowing what a player of its level would (owner decision 8): its capital, its home
/// town, its realm's zones whose usual monster level is below its own (and the towns in them) and, from level 20, its
/// border keep. Dungeons, the next level band's zones, the frontier and Darkness Falls are new ground until it goes there.
/// It learns a place on arriving in it; WP4 adds "told the way" (half known).
///
/// The rules are pure (<see cref="Seed"/>, <see cref="HomeTown"/>, <see cref="BotPlaceSet"/>); the live part asks
/// <see cref="BotPeopleScope"/> (the Record rule) and the bot_place_knowledge switch first. With the switch off, or for
/// anything that is not a saved gamebot, <see cref="Knows(GameBot, BotPlaceKey)"/> answers true: the whole map, as before.
/// In 1.2 nothing chooses a camp by what a bot has seen (owner decision 9). The Explorers' "zones seen lately" (camp taste)
/// is kept here too (<see cref="RememberRecentZone"/>), unchanged: in memory, 6 hours, 32 zones.
/// </summary>
public static class BotPlaceKnowledge
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>
    /// The saved bit of every place, for good: a place's bit is its index here. Append new places at the end (places.json
    /// may change order freely); never remove or reorder an entry, or saved worlds would read other places. A renamed
    /// place keeps its old entry and gets a new one. UT_BotPlaceKnowledge checks that places.json has no place missing.
    /// </summary>
    public static readonly string[] BitOrder =
    [
        "camelot", "jordheim", "tir na nog", "camelot hills", "salisbury plains", "black mountains", "dartmoor",
        "cornwall", "llyn barfog", "campacorentin", "avalon marsh", "lyonesse", "isle of glass", "avalon isle",
        "dales of devwy", "caldey", "gwyddneau", "aldland", "inishail", "forest sauvage", "snowdonia",
        "pennine mountains", "hadrian's wall", "cotswold", "prydwen", "ludlow", "humberton", "west downs",
        "adribard's retreat", "caer witrin", "caer ulfwych", "cornwall station", "swanton keep", "castle sauvage",
        "snowdonia fortress", "caer gothwaite", "wearyall", "caer diogel", "fort gwyntell", "tomb of mithra",
        "tepok's mine", "keltoi fogou", "catacombs of cardova", "stonehenge barrows", "krondon", "crystal cave",
        "avalon city", "caer sidi", "vale of mularn", "svealand", "gotar", "muspelheim", "myrkwood", "skona ravine",
        "vanern swamp", "raumarik", "malmohus", "aegir", "gripklosa", "delling crater", "faraheim", "munin sound",
        "iarnwood", "modernagrav", "uppland", "yggdra forest", "jamtland", "odin's gate", "mularn", "haggerfel",
        "fort veldon", "audliten", "vasudheim", "dvalin", "huginfell", "vindsaul faste", "fort atla", "nalliten",
        "galplen", "gna faste", "svasud faste", "aegirhamn", "bjarken", "hagall", "knarr", "nisse's lair",
        "cursed tomb", "vendo caverns", "varulvhamn", "spindelhalla", "trollheim", "iarnvidiur's lair",
        "tuscaran glacier", "nyttheim", "lough derg", "silvermine", "shannon estuary", "cliffs of moher", "lough gur",
        "bog of cullen", "bri leith", "connacht", "cursed forest", "sheeroe hills", "domnann", "caillte garran",
        "vale of balor", "cothrom gorge", "world's end", "vigilant rock", "allta fearann", "mount collory",
        "cruachan gorge", "breifine", "emain macha", "mag mell", "tir na mbeo", "ardee", "druim ligen", "ardagh",
        "howth", "connla", "caille", "innis carthaig", "culraid", "tir urphost", "alainn bin", "grove of domnann",
        "droighaid", "necht", "aalid feie", "muire tomb", "spraggon den", "koalinth caverns", "treibh caillte",
        "coruscating mine", "tur suil", "galladoria", "fomor", "darkness falls", "thidranki", "abermenai",
        "murdaigean", "caledonia",
    ];

    internal static readonly Dictionary<string, int> BitsByName = BitOrder
        .Select((name, bit) => (name, bit)).ToDictionary(entry => entry.name, entry => entry.bit, StringComparer.OrdinalIgnoreCase);

    /// <summary>Owner decision 8: the border keep is known from this level.</summary>
    public const int BorderKeepLevel = 20;

    /// <summary>How often the controller looks at which zone a bot is in (plus up to a second of spread).</summary>
    public const int VisitCheckMilliseconds = 5_000;

    /// <summary>The salt of the home-town roll (AutonomousBotPersonality.Mix; no other rule uses it).</summary>
    public const ulong HomeTownSalt = 0xB0773001UL;

    /// <summary>BOT_PLACE_LEARNED lines at most per minute, server-wide (the rest are counted in BOT_PLACE_BUDGET).</summary>
    public const int EventLinesPerMinute = 30;

    // ---- the rules (pure) -------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether a bot of this level has levelled through a realm zone: the zone's usual monster level (the middle of its
    /// 20th-80th percentile range in places.json) is below the bot's level. A level 30 knows every zone of 1-29 and
    /// Cornwall (18-31), not Avalon Isle (25-35). Pure.
    /// </summary>
    public static bool LevelledThrough(int[] levels, int level) =>
        levels is { Length: 2 } && (levels[0] + levels[1]) < 2 * level;

    /// <summary>
    /// Owner decision 8: what a bot of this level and realm knows at first sight. Its capital; its home town; its realm's
    /// zones it levelled through (<see cref="LevelledThrough"/>) and the towns in them; from level 20 its realm's first
    /// border keep. Never a dungeon, a frontier zone (only the keep), another realm's place, Darkness Falls or a
    /// battleground. Grows with the level, so a bot that dings is given the zones of its new level too. Pure.
    /// </summary>
    public static BotPlaceSet Seed(int level, eRealm realm, BotPlaceKey homeTown, BotPlaceCatalog catalog)
    {
        catalog ??= BotPlaceCatalog.Current;
        var known = new List<BotPlaceKey>();
        if (BotPlaceCatalog.RealmCode(realm) == null)
            return BotPlaceSet.Empty;
        known.Add(catalog.Capital(realm));
        if (!homeTown.IsNone)
            known.Add(homeTown);
        var zones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BotPlaceKey key in catalog.All)
        {
            AutonomousChatPlaces.Place place = catalog.PlaceOf(key);
            if (catalog.IsOfRealm(key, realm) && BotPlaceCatalog.IsType(place, "zone") && LevelledThrough(place.Levels, level))
            {
                known.Add(key);
                zones.UnionWith(place.Zones);
            }
        }
        foreach (BotPlaceKey key in catalog.All)
        {
            AutonomousChatPlaces.Place place = catalog.PlaceOf(key);
            if (catalog.IsOfRealm(key, realm) && BotPlaceCatalog.IsType(place, "town") && place.Zones.Length > 0 &&
                place.Zones.All(zones.Contains))
                known.Add(key);
        }
        if (level >= BorderKeepLevel)
            known.Add(catalog.BorderKeep(realm));
        return BotPlaceSet.Of(known.Where(key => !key.IsNone));
    }

    /// <summary>
    /// A bot's home town: a town of its realm in the zone of its bind point (where the launcher started it), picked from its
    /// id when the zone has several; else its realm's classic start town (a bot created above level 1 is bound in its
    /// capital). Pure.
    /// </summary>
    public static BotPlaceKey HomeTown(eRealm realm, string bindZone, long botId, BotPlaceCatalog catalog)
    {
        catalog ??= BotPlaceCatalog.Current;
        BotPlaceKey[] towns = catalog.At(bindZone)
            .Where(key => catalog.IsOfRealm(key, realm) && BotPlaceCatalog.IsType(catalog.PlaceOf(key), "town")).ToArray();
        if (towns.Length > 0)
            return towns[(int)(AutonomousBotPersonality.Mix(botId, HomeTownSalt) % (ulong)towns.Length)];
        return catalog.StartTown(realm);
    }

    /// <summary>The places a bot standing in this zone (and region) is at that it does not know yet. Pure.</summary>
    public static List<BotPlaceKey> NewAt(BotPlaceSet known, string zone, string region, BotPlaceCatalog catalog)
    {
        catalog ??= BotPlaceCatalog.Current;
        List<BotPlaceKey> found = null;
        foreach (BotPlaceKey[] keys in new[] { catalog.At(zone), catalog.At(region) })
            foreach (BotPlaceKey key in keys)
                if (!known.Knows(key) && (found == null || !found.Contains(key)))
                    (found ??= new List<BotPlaceKey>()).Add(key);
        return found ?? [];
    }

    // ---- the live part ----------------------------------------------------------------------------------------------

    // Per person, while it is in the world: the zone and level last looked at, and the level its seed was last applied for.
    private sealed class Watch
    {
        public int ZoneId = -1;
        public int CheckedLevel;
        public int SeededLevel;
    }

    private static readonly ConcurrentDictionary<BotPersonKey, Watch> Watches = new();

    private static bool On => BotPeopleSwitches.Current.PlaceKnowledge;

    /// <summary>
    /// What a gamebot knows of a place now. Known for everything while bot_place_knowledge is off, and for anything that
    /// is not a saved gamebot (companions, helpers, in-memory bots, players): the whole map, as before 1.2. Seeds the bot
    /// on first need. Counts nothing (WP4's hook asks the scope itself).
    /// </summary>
    public static BotPlaceKnowing Knowing(GameBot bot, BotPlaceKey place)
    {
        BotPersonKey person = BotPersonKey.Of(bot);
        if (!On || person.IsNone)
            return BotPlaceKnowing.Known;
        using var aiProfile = AiProfile.Scope(AiSystem.PlaceKnowledge); // AI_PROFILE: measures only
        return Ensure(BotPlaceSubject.Of(bot, person)).Knowing(place);
    }

    /// <summary>Whether a gamebot knows a place (see <see cref="Knowing"/>: true with the switch off or for non-gamebots).</summary>
    public static bool Knows(GameBot bot, BotPlaceKey place) => Knowing(bot, place) == BotPlaceKnowing.Known;

    /// <summary>
    /// Whether a gamebot knows a travel destination: true when any place the destination names is known, or when it names
    /// no listed place at all (an unlisted spot is not new ground we can judge: today's route).
    /// </summary>
    public static bool Knows(GameBot bot, string destination)
    {
        bool any = false;
        foreach (BotPlaceKey place in BotPlaceCatalog.Current.For(destination))
        {
            any = true;
            if (Knows(bot, place))
                return true;
        }
        return !any;
    }

    /// <summary>The places a saved gamebot knows (seeded on first need); Empty for anything else or with the switch off.</summary>
    public static BotPlaceSet KnownBy(GameBot bot)
    {
        BotPersonKey person = BotPersonKey.Of(bot);
        return !On || person.IsNone ? BotPlaceSet.Empty : Ensure(BotPlaceSubject.Of(bot, person));
    }

    /// <summary>
    /// A gamebot learns a place (WP4: <see cref="BotPlaceLearning.Told"/> when it is told the way). Asks the switch and the
    /// Record scope first. True when the bot's knowledge changed (then a BOT_PLACE_LEARNED line, rate-limited).
    /// </summary>
    public static bool Learn(GameBot bot, BotPlaceKey place, BotPlaceLearning how)
    {
        if (place.IsNone || bot == null)
            return false;
        if (BotPeopleScope.Check(bot, BotPeopleRule.Record, On, out _) != BotPeopleRefusal.None)
            return false;
        using var aiProfile = AiProfile.Scope(AiSystem.PlaceKnowledge); // AI_PROFILE: measures only
        return Learn(BotPlaceSubject.Of(bot, BotPersonKey.Of(bot)), place, how, bot.CurrentZone?.Description);
    }

    /// <summary>The rule behind <see cref="Learn(GameBot, BotPlaceKey, BotPlaceLearning)"/>, on plain facts (tests; the scope is the caller's).</summary>
    public static bool Learn(BotPlaceSubject subject, BotPlaceKey place, BotPlaceLearning how, string zone = null)
    {
        if (place.IsNone || subject.Person.IsNone)
            return false;
        BotPlaceSet before = Ensure(subject);
        if (before.Learn(place, how) == before)
            return false;
        Apply(subject, set => set.Learn(place, how));
        Counted(how, 1);
        LogLearned(subject, how, place.Name, 1, zone);
        return true;
    }

    /// <summary>
    /// The controller's hook (AutonomousWorldBotController.Tick, every <see cref="VisitCheckMilliseconds"/>): when the bot
    /// is in another zone, or has levelled, since it last looked, it learns the places it is at, and its seed is topped up
    /// for its level. Nothing else; and nothing at all, no allocation, while zone and level are unchanged.
    /// </summary>
    public static void Visit(GameBot bot)
    {
        Zone zone = bot?.CurrentZone;
        if (zone == null)
            return;
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return;
        Watch watch = Watches.GetOrAdd(person, static _ => new Watch());
        if (watch.ZoneId == zone.ID && watch.CheckedLevel == bot.Level)
            return;
        // The zone is kept only once the switch and the scope allow it, so a bot refused here learns the zone later
        // without leaving it (work package 7).
        if (BotPeopleScope.Check(bot, BotPeopleRule.Record, On, out _) != BotPeopleRefusal.None)
            return;
        watch.ZoneId = zone.ID;
        watch.CheckedLevel = bot.Level;
        using var aiProfile = AiProfile.Scope(AiSystem.PlaceKnowledge); // AI_PROFILE: measures only
        Arrive(BotPlaceSubject.Of(bot, person), zone.Description, bot.CurrentRegion?.Description);
    }

    /// <summary>
    /// The rule behind <see cref="Visit"/>, on plain facts (tests; the scope and the zone-change test are the caller's): the
    /// bot is seeded or topped up, then learns every place it is at. Returns the places learned.
    /// </summary>
    public static List<BotPlaceKey> Arrive(BotPlaceSubject subject, string zone, string region)
    {
        if (subject.Person.IsNone)
            return [];
        Interlocked.Increment(ref _checks);
        BotPlaceSet known = Ensure(subject);
        List<BotPlaceKey> found = NewAt(known, zone, region, BotPlaceCatalog.Current);
        if (found.Count == 0)
            return found;
        Apply(subject, set => found.Aggregate(set, (s, place) => s.Learn(place, BotPlaceLearning.Visited)));
        Counted(BotPlaceLearning.Visited, found.Count);
        foreach (BotPlaceKey place in found)
            LogLearned(subject, BotPlaceLearning.Visited, place.Name, 1, zone);
        return found;
    }

    /// <summary>
    /// The bot's places as a set: a bot with no row is seeded (owner decision 8), and one that has levelled since its seed
    /// was last applied is topped up with its new level's zones. Writes only when something is new.
    /// </summary>
    public static BotPlaceSet Ensure(BotPlaceSubject subject)
    {
        if (subject.Person.IsNone)
            return BotPlaceSet.Empty;
        Watch watch = Watches.GetOrAdd(subject.Person, static _ => new Watch());
        bool hasRow = BotPeopleStore.Current.Places.TryGet((subject.Person, (int)subject.Realm), out OfflineBotPlacesRecord row);
        BotPlaceSet current = hasRow ? BotPlaceSet.Of(row) : BotPlaceSet.Empty;
        if (hasRow && watch.SeededLevel >= subject.Level)
            return current;
        BotPlaceCatalog catalog = BotPlaceCatalog.Current;
        BotPlaceSet seed = Seed(subject.Level, subject.Realm, HomeTown(subject.Realm, subject.HomeZone, subject.Person.Id, catalog), catalog);
        watch.SeededLevel = subject.Level;
        int added = current.NewIn(seed).Count();
        if (hasRow && added == 0)
            return current;
        BotPlaceSet after = Apply(subject, set => set.Union(seed));
        Counted(BotPlaceLearning.Seeded, added);
        if (!hasRow)
            Interlocked.Increment(ref _botsSeeded);
        LogLearned(subject, BotPlaceLearning.Seeded, hasRow ? "level " + subject.Level : "first sight", added, null);
        return after;
    }

    /// <summary>Changes the bot's row under the table lock (creating it) and queues the save; returns the new set.</summary>
    private static BotPlaceSet Apply(BotPlaceSubject subject, Func<BotPlaceSet, BotPlaceSet> change)
    {
        BotPlaceSet result = default;
        int realm = (int)subject.Realm;
        BotPeopleStore.Current.Places.Change((subject.Person, realm),
            () => new OfflineBotPlacesRecord { PersonKey = subject.Person.ToString(), Realm = realm, CharacterId = subject.CharacterId },
            row =>
            {
                result = change(BotPlaceSet.Of(row));
                row.Known = result.Known;
                row.HalfKnown = result.HalfKnown;
                row.CharacterId = subject.CharacterId;
                row.UpdatedUtc = DateTime.UtcNow.ToString("O");
            });
        return result;
    }

    /// <summary>The zone of a gamebot's bind point (where the launcher started it), or null.</summary>
    public static string BindZoneOf(GameBot bot)
    {
        OfflineWorldBotRecord record = bot?.PersistentRecord;
        if (record == null || record.BindRegionId <= 0)
            return null;
        return WorldMgr.GetRegion((ushort)record.BindRegionId)?.GetZone(record.BindX, record.BindY)?.Description;
    }

    // ---- what /lswhy way shows ----------------------------------------------------------------------------------------

    /// <summary>
    /// The place lines of /lswhy way for one gamebot (WP4 adds its own after these: it registers a provider that calls
    /// this first). Reads, seeds a bot never seen before, changes nothing else.
    /// </summary>
    public static IEnumerable<string> WhyLines(GameBot bot)
    {
        if (!On)
            return ["Places: bot_place_knowledge is off, so it knows the whole map, as before 1.2."];
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return ["Places: not a saved gamebot; it knows the whole map."];
        BotPlaceSet set = Ensure(BotPlaceSubject.Of(bot, person));
        return DescribeLines(set, bot.Realm, bot.Level, BotPlaceCatalog.Current);
    }

    /// <summary>The /lswhy way lines for a set (by kind, places in bit order). Pure.</summary>
    public static List<string> DescribeLines(BotPlaceSet set, eRealm realm, int level, BotPlaceCatalog catalog)
    {
        catalog ??= BotPlaceCatalog.Current;
        var lines = new List<string>
        {
            $"Places ({GlobalConstants.RealmToName(realm)}): knows {set.KnownCount} of {catalog.All.Count}, half knows {set.HalfKnownCount}. " +
            $"Seeded for level {level}: capital, home town, own zones below its level{(level >= BorderKeepLevel ? ", border keep" : "")}; " +
            "learns a place on arrival.",
        };
        (string Label, Func<AutonomousChatPlaces.Place, bool> Is)[] kinds =
        [
            ("Capital and towns", p => BotPlaceCatalog.IsType(p, "capital") || BotPlaceCatalog.IsType(p, "town")),
            ("Zones", p => BotPlaceCatalog.IsType(p, "zone")),
            ("Frontier", p => BotPlaceCatalog.IsType(p, "frontier") || BotPlaceCatalog.IsType(p, "battleground")),
            ("Dungeons", p => BotPlaceCatalog.IsType(p, "dungeon")),
        ];
        foreach (var (label, isKind) in kinds)
        {
            string[] names = set.KnownPlaces.Where(key => isKind(catalog.PlaceOf(key))).Select(key => key.Name).ToArray();
            lines.Add($"{label}: {(names.Length == 0 ? "none" : string.Join(", ", names))}");
        }
        string[] half = set.HalfKnownPlaces.Select(key => key.Name).ToArray();
        if (half.Length > 0)
            lines.Add("Told the way to: " + string.Join(", ", half));
        return lines;
    }

    // ---- log lines --------------------------------------------------------------------------------------------------

    private static long _checks;
    private static long _visited;
    private static long _told;
    private static long _seeded;
    private static long _botsSeeded;
    private static long _lines;
    private static long _suppressed;
    private static long _lineWindowStart;
    private static int _linesInWindow;

    private static void Counted(BotPlaceLearning how, int places)
    {
        switch (how)
        {
            case BotPlaceLearning.Visited: Interlocked.Add(ref _visited, places); break;
            case BotPlaceLearning.Told: Interlocked.Add(ref _told, places); break;
            default: Interlocked.Add(ref _seeded, places); break;
        }
    }

    /// <summary>"BOT_PLACE_LEARNED how=visited bot=... place=..." Pure.</summary>
    public static string LearnedLine(string botName, long botId, eRealm realm, int level, BotPlaceLearning how, string what,
        int places, string zone) =>
        $"BOT_PLACE_LEARNED how={HowName(how)} bot={botName} id={botId} realm={GlobalConstants.RealmToName(realm)} level={level} " +
        (how == BotPlaceLearning.Seeded ? $"places={places} at=\"{what}\"" : $"place=\"{what}\"") +
        (string.IsNullOrEmpty(zone) ? string.Empty : $" zone=\"{zone}\"");

    public static string HowName(BotPlaceLearning how) => how switch
    {
        BotPlaceLearning.Visited => "visited",
        BotPlaceLearning.Told => "told",
        _ => "seeded",
    };

    private static void LogLearned(BotPlaceSubject subject, BotPlaceLearning how, string what, int places, string zone)
    {
        if (!Log.IsInfoEnabled || !TakeLine(DateTime.UtcNow.Ticks))
            return;
        try
        {
            Log.Info(LearnedLine(subject.Name, subject.CharacterId, subject.Realm, subject.Level, how, what, places, zone));
        }
        catch
        {
            // A log line must never stop a bot learning.
        }
    }

    /// <summary>At most <see cref="EventLinesPerMinute"/> lines a minute server-wide; the rest are counted.</summary>
    private static bool TakeLine(long nowTicks)
    {
        long start = Interlocked.Read(ref _lineWindowStart);
        if (nowTicks - start >= TimeSpan.TicksPerMinute && Interlocked.CompareExchange(ref _lineWindowStart, nowTicks, start) == start)
            Interlocked.Exchange(ref _linesInWindow, 0);
        if (Interlocked.Increment(ref _linesInWindow) > EventLinesPerMinute)
        {
            Interlocked.Increment(ref _suppressed);
            return false;
        }
        Interlocked.Increment(ref _lines);
        return true;
    }

    /// <summary>
    /// "BOT_PLACE_BUDGET seconds=300 checks=N learned=visited:N,told:N,seeded:N bots_seeded=N lines=N suppressed=N
    /// rows=N known_avg=alb:N,mid:N,hib:N" (checks: zone or level changes looked at). Pure.
    /// </summary>
    public static string BudgetLine(long seconds, long checks, long visited, long told, long seeded, long botsSeeded, long lines,
        long suppressed, int rows, IReadOnlyDictionary<eRealm, double> knownAverage) =>
        $"BOT_PLACE_BUDGET seconds={seconds} checks={checks} learned=visited:{visited},told:{told},seeded:{seeded} " +
        $"bots_seeded={botsSeeded} lines={lines} suppressed={suppressed} rows={rows} known_avg=" +
        string.Join(",", new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.Select(realm =>
            $"{BotPlaceCatalog.RealmCode(realm)}:{(knownAverage != null && knownAverage.TryGetValue(realm, out double avg) ? avg : 0).ToString("0.0", CultureInfo.InvariantCulture)}"));

    private static IEnumerable<string> TakeBudget(long seconds)
    {
        OfflineBotPlacesRecord[] rows = BotPeopleStore.Current.Places.Where(_ => true);
        var average = rows.GroupBy(row => (eRealm)row.Realm)
            .ToDictionary(group => group.Key, group => group.Average(row => (double)BotPeopleBits.Count(row.Known)));
        yield return BudgetLine(seconds, Interlocked.Exchange(ref _checks, 0), Interlocked.Exchange(ref _visited, 0),
            Interlocked.Exchange(ref _told, 0), Interlocked.Exchange(ref _seeded, 0), Interlocked.Exchange(ref _botsSeeded, 0),
            Interlocked.Exchange(ref _lines, 0), Interlocked.Exchange(ref _suppressed, 0), rows.Length, average);
    }

    // ---- start-up -----------------------------------------------------------------------------------------------------

    private static int _registered;

    /// <summary>Registers /lswhy way's place lines, BOT_PLACE_BUDGET and the logout clean-up (once).</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;
        BotPeopleWhy.Register(BotPeopleWhy.Topic.Way, WhyLines);
        BotPeopleSummary.Register("BOT_PLACE_BUDGET", TakeBudget);
        BotPeopleStore.LoggedOut += (person, _) => Watches.TryRemove(person, out Watch _);
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Register();
            if (BotPlaceCatalog.Current.Unlisted.Count > 0)
                Log.Warn("Bots as people: places without a saved bit (append them to BotPlaceKnowledge.BitOrder): " +
                         string.Join(", ", BotPlaceCatalog.Current.Unlisted));
        }
        catch (Exception exception)
        {
            Log.Warn("Bots as people: place knowledge could not register", exception);
        }
    }

    /// <summary>Tests only: forgets every per-bot watch and count.</summary>
    public static void ResetForTests()
    {
        Watches.Clear();
        _checks = _visited = _told = _seeded = _botsSeeded = _lines = _suppressed = 0;
        _linesInWindow = 0;
        _lineWindowStart = 0;
    }

    // ---- the Explorers' zones seen lately (moved here from AutonomousBotPersonality, unchanged) ------------------------

    /// <summary>The Explorers' camp taste: at most this many zones per bot, in memory (as before).</summary>
    public const int RecentZoneCapacity = 32;

    /// <summary>A zone is fresh again this long after the bot last chose a camp in it (as before).</summary>
    public static readonly TimeSpan RecentZoneFreshAfter = TimeSpan.FromHours(6);

    private static readonly ConcurrentDictionary<long, RecentZones> Recent = new();

    private sealed class RecentZones
    {
        private readonly Dictionary<string, DateTime> _visits = new(StringComparer.OrdinalIgnoreCase);

        public void Visit(string zone, DateTime now)
        {
            lock (_visits)
            {
                _visits[zone] = now;
                if (_visits.Count > RecentZoneCapacity)
                    _visits.Remove(_visits.OrderBy(pair => pair.Value).First().Key);
            }
        }

        public bool IsFresh(string zone, DateTime now)
        {
            lock (_visits)
                return !_visits.TryGetValue(zone, out DateTime last) || now - last >= RecentZoneFreshAfter;
        }
    }

    /// <summary>
    /// A bot chose a camp in this zone (AutonomousBotPersonality.RememberZone). Owner decision 9: camp choice does not read
    /// the saved places in 1.2, so this stays the in-memory "lately" list it always was (forgotten at a restart).
    /// </summary>
    public static void RememberRecentZone(long botId, string zone, DateTime when)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return;
        Recent.GetOrAdd(botId, static _ => new RecentZones()).Visit(zone, when);
    }

    /// <summary>Whether a zone is fresh for an Explorer's taste: not chosen in the last 6 hours (or never).</summary>
    public static bool IsRecentZoneFresh(long botId, string zone, DateTime now) =>
        string.IsNullOrWhiteSpace(zone) || !Recent.TryGetValue(botId, out RecentZones memory) || memory.IsFresh(zone, now);
}
