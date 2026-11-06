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
    public static LoreBible.Location LocationAt(LoreBible bible, ushort region, ushort zone)
    {
        LoreBible.Location byRegion = null;
        foreach (LoreBible.Location location in bible.LocationsInRegion(region))
        {
            if (location.Zones.Length > 0)
            {
                if (location.Zones.Contains(zone))
                    return location;
            }
            else
                byRegion ??= location;
        }
        return byRegion;
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
/// of them dies), "formChange" (its model changes); "death" and "wake" come from events (it dies, a dragon wakes).
/// </summary>
public static class LoreFightRules
{
    public enum Kind { Health, AddSpawn, OrbDeath, AddsDead, FormChange, Death, Wake }

    public sealed record Trigger(Kind Kind, int Percent)
    {
        public bool NeedsAdds => Kind is Kind.AddSpawn or Kind.OrbDeath or Kind.AddsDead;
        /// <summary>Said by an event, not by looking at the fight: the boss dies, a dragon takes to the sky.</summary>
        public bool ByEvent => Kind is Kind.Death or Kind.Wake;
    }

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
        public readonly HashSet<string> Said = new(StringComparer.Ordinal);
        public int AtmosphereLines;
    }

    /// <summary>At most this many atmosphere (screen-centre) phase lines in one fight; yells are not capped beyond their own gap.</summary>
    public const int MaxAtmospherePerFight = 2;

    /// <summary>
    /// The triggers that fire on this observation (health in %, living adds or -1 when the boss has none to count), in
    /// order. Updates the fight. A health trigger fires once a fight; addSpawn when the count rises, orbDeath when it falls,
    /// addsDead when it reaches 0 after there were some.
    /// </summary>
    public static List<string> Observe(Fight fight, IEnumerable<string> triggers, int healthPercent, int adds, int model = -1)
    {
        var fired = new List<string>();
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

    public static string FirstKillKey(eRealm realm, string bossId) => $"firstkill:{(int)realm}:{bossId}";

    /// <summary>Whether a rate-limited realm line may go out again (dragon wakes hourly, Darkness Falls every 15 minutes).</summary>
    public static bool GapOver(long? last, long now, long gap) => last == null || now - last.Value >= gap;

    /// <summary>Who killed it, for the realm: a player or bot, or the owner of a pet; None for a monster.</summary>
    public static eRealm KillerRealm(GameObject killer) => killer switch
    {
        GamePlayer player => player.Realm,
        GameBot bot => bot.Realm,
        GameNPC { Brain: AI.Brain.IControlledBrain controlled } => controlled.GetPlayerOwner()?.Realm ?? (controlled.Owner as GameLiving)?.Realm ?? eRealm.None,
        _ => eRealm.None,
    };
}
