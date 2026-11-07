using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The screen-centre line throttle for one player (Living Server 1.2, docs/LORE.md "Atmosphere lines"). Pure: times in,
/// lines out, unit-tested in UT_LoreAtmosphere.
/// <list type="bullet">
/// <item>Realm news (keeps, relics, raid ends, siege starts, Darkness Falls, a realm's first kill, a dragon waking) always
/// wins: it is never held back by the atmosphere gap, and an atmosphere line waits 10 s after any of it;</item>
/// <item>an atmosphere line (first entry, a named area, a lair, a boss phase) at most once every 30 s per player; at most one
/// waits, the newest, and it is dropped when it goes stale (a phase line after 8 s);</item>
/// <item>never more than 4 centre lines in 5 s (the new client stacks 4 and shows each for 5 s): the rest wait.</item>
/// </list>
/// </summary>
public sealed class AtmosphereThrottle
{
    public const long AtmosphereGapMs = 30_000;
    public const long NewsQuietMs = 10_000;
    public const long StackWindowMs = 5_000;
    public const int StackMax = 4;
    public const int RealmQueueMax = 8;

    /// <summary>A centre line. Realm lines are news; Large uses the gold CT_ScreenCenter, else the smaller cream one.</summary>
    public sealed record Line(string Text, bool Realm, bool Large, string ChatCopy, long Expires)
    {
        /// <summary>What to remember once it is shown (first entry today, an area this session), or null.</summary>
        public string OnceKey { get; init; }
        public bool OncePerDay { get; init; }
    }

    private readonly List<long> _shown = new();
    private readonly List<Line> _realm = new();
    private Line _local;
    private long _lastAtmosphere = long.MinValue / 2;
    private long _lastNews = long.MinValue / 2;

    public Line Waiting => _local;
    public int RealmWaiting => _realm.Count;

    public void Offer(Line line, long now)
    {
        if (line == null || string.IsNullOrWhiteSpace(line.Text) || line.Expires <= now)
            return;
        if (line.Realm)
        {
            if (_realm.Count >= RealmQueueMax)
                _realm.RemoveAt(0);
            if (!_realm.Any(l => l.Text == line.Text))
                _realm.Add(line);
        }
        else
            _local = line;   // the newest atmosphere line is the one that still fits where the player is
    }

    /// <summary>Realm news sent outside this service (a keep taken, a relic, a raid's end) reached the player.</summary>
    public void NoteNews(long now)
    {
        _lastNews = Math.Max(_lastNews, now);
        _shown.Add(now);
    }

    private bool StackRoom(long now)
    {
        _shown.RemoveAll(t => now - t >= StackWindowMs);
        return _shown.Count < StackMax;
    }

    /// <summary>The lines to send now (realm lines first), in order. Each one sent is counted.</summary>
    public List<Line> Due(long now)
    {
        var due = new List<Line>();
        _realm.RemoveAll(l => l.Expires <= now);
        if (_local != null && _local.Expires <= now)
            _local = null;
        while (_realm.Count > 0 && StackRoom(now))
        {
            Line line = _realm[0];
            _realm.RemoveAt(0);
            due.Add(line);
            _shown.Add(now);
            _lastNews = now;
        }
        if (_local != null && _realm.Count == 0 && now - _lastNews >= NewsQuietMs && now - _lastAtmosphere >= AtmosphereGapMs && StackRoom(now))
        {
            due.Add(_local);
            _shown.Add(now);
            _lastAtmosphere = now;
            _local = null;
        }
        return due;
    }
}

/// <summary>When a location's first-entry and area lines are due. Pure.</summary>
public static class LoreEntryRules
{
    /// <summary>The location the player stands in: by zone when the location lists zones (a lair in an open-world region), else by region.</summary>
    /// <remarks>
    /// Several files may claim a region (an umbrella beside its zone files): a file listing the zone wins, then the
    /// narrowest (fewest regions), then one with areas at coordinates, then the first by id.
    /// </remarks>
    public static LoreBible.Location LocationAt(LoreBible bible, ushort region, ushort zone)
    {
        LoreBible.Location[] here = bible.LocationsInRegion(region);
        LoreBible.Location byZone = here.Where(l => l.Zones.Contains(zone)).OrderBy(l => l.Zones.Length).ThenBy(l => l.Id, StringComparer.Ordinal).FirstOrDefault();
        if (byZone != null)
            return byZone;
        return here.Where(l => l.Zones.Length == 0)
            .OrderBy(l => l.Regions.Length)
            .ThenByDescending(l => l.Areas.Any(a => a.X != 0 || a.Y != 0))
            .ThenBy(l => l.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>The day key of a first-entry line: once per character per day (the PC's calendar day).</summary>
    public static string Day(DateTime local) => local.ToString("yyyy-MM-dd");

    public static string EntryKey(string locationId) => "entry:" + locationId;
    public static string AreaKey(string locationId, string area) => $"area:{locationId}:{area}";
    public static string LairKey(string bossId) => "lair:" + bossId;

    /// <summary>A first-entry line is due when the player has just walked in and has not seen one for this place today.</summary>
    public static bool EntryDue(string previousLocationId, string currentLocationId, string lastShownDay, string today) =>
        currentLocationId != null && !string.Equals(previousLocationId, currentLocationId, StringComparison.OrdinalIgnoreCase) &&
        lastShownDay != today;

    /// <summary>Inside an area's circle: 2D within the radius, and, when the area gives a height, within 800 units of it (floors of a dungeon).</summary>
    public static bool InArea(LoreBible.Area area, ushort region, int x, int y, int z)
    {
        if (area.Region != region)
            return false;
        long dx = x - area.X, dy = y - area.Y;
        if (dx * dx + dy * dy > (long)area.Radius * area.Radius)
            return false;
        return area.Z == 0 || Math.Abs(z - area.Z) <= 800;
    }
}

/// <summary>
/// A boss fight's triggers as pure rules (UT_LoreAtmosphere): the boss's health and its adds are watched every couple of
/// seconds while it fights, and these rules say which data lines are due. Triggers: "health&lt;=N" (once a fight, when its
/// health first falls to N% or less), "addsSpawn" (its adds appear), "orbDeath" (one of its adds dies), "addsDead" (the last
/// of them dies), "formChange" (its model changes); "death" and "wake" come from events (it dies, a dragon wakes). The
/// mechanic triggers (<see cref="MechanicTriggers"/>: a throw or teleport, a glare, a breath, a berserk...) come from the
/// boss scripts' own announcements (EncounterDisplacement.Displaced, EncounterSignals.Raised and EncounterSignals.Moment).
/// </summary>
public static class LoreFightRules
{
    public enum Kind { Health, AddSpawn, OrbDeath, AddsDead, FormChange, PartnerDead, Death, Wake, PlayerDeath, Mechanic }

    public sealed record Trigger(Kind Kind, int Percent)
    {
        public bool NeedsAdds => Kind is Kind.AddSpawn or Kind.OrbDeath or Kind.AddsDead;
        public bool NeedsPartners => Kind is Kind.PartnerDead;
        /// <summary>Said by an event, not by looking at the fight: the boss dies, a dragon takes to the sky, it kills a player.</summary>
        public bool ByEvent => Kind is Kind.Death or Kind.Wake or Kind.PlayerDeath;
        /// <summary>Said when the boss's script announces the mechanic (1.2, the lore-mechanic triggers).</summary>
        public bool ByScript => Kind is Kind.Mechanic;
    }

    /// <summary>
    /// The triggers a boss script announces (1.2). A move (EncounterDisplacement.Displaced) fires its kind: "throw",
    /// "teleport", "jail", "pull" or "barrier", and "teleport" stands for any move in a file that has no line for the kind
    /// itself (the writers used it for every throw, push and port). A signal (EncounterSignals.Raised) fires "glare" (or
    /// "stare"), "stun", "breath", "stomp", "messengers", "retreat" or "safeSpots"; a lore-only moment
    /// (EncounterSignals.Moment) "berserk" (Suttung), "scream" (Kvasir), "healed" (Funus) or "twinHeal" (Tuscar, Kula).
    /// </summary>
    public static readonly string[] MechanicTriggers =
    [
        "teleport", "throw", "jail", "pull", "barrier",
        "glare", "stare", "stun", "stomp", "breath", "messengers", "retreat", "safeSpots",
        "berserk", "scream", "healed", "twinHeal",
    ];

    /// <summary>Triggers the files may use that nothing fires (none since 1.2's mechanic triggers; kept for the schema's message).</summary>
    public static readonly string[] ScriptOnlyTriggers = [];

    /// <summary>The triggers a move fires, the kind's own first; "teleport" is every move's fallback.</summary>
    public static string[] DisplacementTriggers(DisplacementKind kind) => kind switch
    {
        DisplacementKind.Throw => ["throw", "teleport"],
        DisplacementKind.Teleport => ["teleport"],
        DisplacementKind.Jail => ["jail", "teleport"],
        DisplacementKind.Pull => ["pull", "teleport"],
        DisplacementKind.Barrier => ["barrier", "teleport"],
        _ => ["teleport"],
    };

    /// <summary>The triggers a script's signal or moment fires (EncounterSignals' names; a glare is also a "stare").</summary>
    public static string[] SignalTriggers(string mechanic) =>
        string.IsNullOrEmpty(mechanic) ? [] :
        mechanic == EncounterSignals.Glare ? ["glare", "stare"] : [EncounterSignals.LoreTrigger(mechanic)];

    public static bool TryParseTrigger(string text, out Trigger trigger)
    {
        trigger = null;
        string t = (text ?? string.Empty).Replace(" ", string.Empty);
        if (t.StartsWith("health<=", StringComparison.OrdinalIgnoreCase) && int.TryParse(t[8..], out int percent) && percent is >= 1 and <= 99)
            trigger = new Trigger(Kind.Health, percent);
        else if (string.Equals(t, "addSpawn", StringComparison.OrdinalIgnoreCase) || string.Equals(t, "addsSpawn", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.AddSpawn, 0);
        else if (string.Equals(t, "formChange", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.FormChange, 0);
        else if (string.Equals(t, "death", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.Death, 0);
        else if (string.Equals(t, "wake", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.Wake, 0);
        else if (string.Equals(t, "partnerDead", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.PartnerDead, 0);
        else if (string.Equals(t, "playerDeath", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.PlayerDeath, 0);
        else if (MechanicTriggers.Contains(t, StringComparer.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.Mechanic, 0);
        else if (string.Equals(t, "orbDeath", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.OrbDeath, 0);
        else if (string.Equals(t, "addsDead", StringComparison.OrdinalIgnoreCase))
            trigger = new Trigger(Kind.AddsDead, 0);
        return trigger != null;
    }

    /// <summary>One fight's memory: what it has seen and which lines it has said.</summary>
    public sealed class Fight
    {
        public int LowestHealth = 100;
        public int Adds = -1;
        public bool HadAdds;
        public int Model = -1;
        public int Partners = -1;
        public readonly HashSet<string> Said = new(StringComparer.Ordinal);
        public int AtmosphereLines;
        /// <summary>When each mechanic trigger last yelled (game time).</summary>
        public readonly Dictionary<string, long> MechanicYelled = new(StringComparer.OrdinalIgnoreCase);
        public int MechanicAtmosphereLines;
    }

    /// <summary>At most this many atmosphere (screen-centre) phase lines in one fight; yells are not capped beyond their own gap.</summary>
    public const int MaxAtmospherePerFight = 2;

    /// <summary>
    /// At most this many mechanic atmosphere lines in one fight, each trigger once, counted apart from the phase lines so a
    /// boss whose health lines used theirs still shows its throw. The player's own throttle (one line every 30 s, realm news
    /// first) still decides when, or whether, each one shows.
    /// </summary>
    public const int MaxMechanicAtmospherePerFight = 2;

    /// <summary>A mechanic trigger yells again only after this long (a dragon's breath comes every half minute or so).</summary>
    public const long MechanicYellGapMs = 60_000;

    /// <summary>What a mechanic says: the trigger it matched, the yells to pick from (empty: none due) and the atmosphere line (or null).</summary>
    public sealed record MechanicLines(string Trigger, string[] Yells, string Atmosphere);

    /// <summary>
    /// A boss script announced a mechanic (<paramref name="candidates"/>, best first: <see cref="DisplacementTriggers"/>,
    /// <see cref="SignalTriggers"/>). The first candidate the boss's file has a line for is the one that fires, or null when
    /// it has none. Its yells are due once per <see cref="MechanicYellGapMs"/>; its atmosphere line once a fight, at most
    /// <see cref="MaxMechanicAtmospherePerFight"/> a fight. Updates the fight.
    /// </summary>
    public static MechanicLines Mechanic(Fight fight, IReadOnlyCollection<LoreBible.PhaseLine> yells,
        IReadOnlyCollection<LoreBible.PhaseLine> atmosphere, IEnumerable<string> candidates, long now)
    {
        yells ??= [];
        atmosphere ??= [];
        static bool Has(IEnumerable<LoreBible.PhaseLine> lines, string trigger) =>
            lines.Any(p => string.Equals(p.Trigger?.Replace(" ", string.Empty), trigger, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Text));
        string matched = (candidates ?? []).FirstOrDefault(c => !string.IsNullOrEmpty(c) && (Has(yells, c) || Has(atmosphere, c)));
        if (matched == null)
            return null;
        string[] texts = [];
        if (Has(yells, matched) && (!fight.MechanicYelled.TryGetValue(matched, out long last) || now - last >= MechanicYellGapMs))
        {
            fight.MechanicYelled[matched] = now;
            texts = yells.Where(p => string.Equals(p.Trigger?.Replace(" ", string.Empty), matched, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Text))
                .Select(p => p.Text).ToArray();
        }
        string line = null;
        if (Has(atmosphere, matched) && fight.MechanicAtmosphereLines < MaxMechanicAtmospherePerFight && fight.Said.Add("mechanic:" + matched))
        {
            fight.MechanicAtmosphereLines++;
            line = atmosphere.First(p => string.Equals(p.Trigger?.Replace(" ", string.Empty), matched, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Text)).Text;
        }
        return new MechanicLines(matched, texts, line);
    }

    /// <summary>
    /// The triggers that fire on this observation (health in %, living adds or -1 when the boss has none to count), in
    /// order. Updates the fight. A health trigger fires once a fight; addSpawn when the count rises, orbDeath when it falls,
    /// addsDead when it reaches 0 after there were some.
    /// </summary>
    public static List<string> Observe(Fight fight, IEnumerable<string> triggers, int healthPercent, int adds, int model = -1, int partners = -1)
    {
        var fired = new List<string>();
        bool partnerDied = partners >= 0 && fight.Partners > partners;
        if (partners >= 0)
            fight.Partners = partners;
        bool newForm = model >= 0 && fight.Model >= 0 && model != fight.Model;
        if (model >= 0)
            fight.Model = model;
        int before = fight.LowestHealth;
        fight.LowestHealth = Math.Min(fight.LowestHealth, healthPercent);
        int previousAdds = fight.Adds;
        bool spawned = adds > 0 && adds > Math.Max(previousAdds, 0);
        bool died = previousAdds > 0 && adds >= 0 && adds < previousAdds;
        bool allDead = previousAdds > 0 && adds == 0;
        if (adds >= 0)
        {
            fight.Adds = adds;
            fight.HadAdds |= adds > 0;
        }
        foreach (string text in triggers.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseTrigger(text, out Trigger trigger))
                continue;
            bool fire = trigger.Kind switch
            {
                Kind.Health => before > trigger.Percent && fight.LowestHealth <= trigger.Percent && !fight.Said.Contains(text),
                Kind.AddSpawn => spawned,
                Kind.OrbDeath => died && !allDead,
                Kind.AddsDead => allDead && !fight.Said.Contains(text),
                Kind.FormChange => newForm,
                Kind.PartnerDead => partnerDied,
                _ => false,
            };
            if (!fire)
                continue;
            fight.Said.Add(text);
            fired.Add(text);
        }
        return fired;
    }

    /// <summary>A fight is over (the boss reset) when it has been out of combat for a while and is back near full health.</summary>
    public static bool Reset(bool inCombat, long outOfCombatMs, int healthPercent) => !inCombat && outOfCombatMs >= 10_000 && healthPercent >= 95;
}

/// <summary>Once-only rules for realm-scale lore lines. Pure.</summary>
public static class LoreOnceRules
{
    public const long DragonWakeGapMs = 60 * 60_000L;
    public const long DarknessFallsGapMs = 15 * 60_000L;

    /// <summary>Whether a rate-limited realm line may go out again (dragon wakes hourly, Darkness Falls every 15 minutes).</summary>
    public static bool GapOver(long? last, long now, long gap) => last == null || now - last.Value >= gap;
}
