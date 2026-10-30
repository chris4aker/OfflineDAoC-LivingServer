using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// A dragon raid's attempts, wipes and regroups (2026-10-30). The dungeon route (RealmRaidDungeonRoute) has treated a
/// wipe on a named encounter as a failed attempt since 0.37-se.2: the raid regroups, the Epic Special Forces answer,
/// and the raid goes in once more together. Dragon raids had no such rule. After a wipe at the dragon the fallen
/// released to the nearest bind and walked back by twos and threes to a dragon at full health, until it died or the
/// four hours ran out, and nobody called the Special Forces ("Dragon raids don't call them yet").
///
/// Now a dragon raid keeps the dungeon raid's rules. An attempt begins when the raid goes in from its staging ring (or
/// when the dragon is found in a fight with raiders: a Leeroy, an early pull). It fails when half the raiders in it
/// fell, or the dragon healed back to 95% after being taken to 85% or lower (<see cref="IsWipe"/>, the dungeon rule).
/// Then the raid regroups at its staging ring, outside the dragon's aggro: the fallen walk back, the living rest and
/// rebuff for at least five minutes, and the raid goes in again once 70% of its roster stands there (40% after 20
/// minutes) and the dragon has landed. After two failed attempts the raid is defeated and goes home. The lair radius,
/// the 600 damage cap and the landing rule are untouched.
///
/// The raid feeds it plain facts once a pulse (<see cref="Raider"/>, <see cref="Dragon"/>), so the rules run the same
/// in a unit test as in the game.
/// </summary>
public sealed class RealmRaidDragonFight
{
    /// <summary>One raider as the raid sees it this pulse: standing (alive, in the dragon's region, not walking back from
    /// a release), at the dragon (standing within <see cref="RealmRaidDungeonRoute.TargetPresenceRadius"/> of it) and at
    /// the staging ring (standing on a staging post, outside the dragon's clearance).</summary>
    public readonly record struct Raider(long Id, bool Standing, bool AtDragon, bool AtStaging);

    /// <summary>The dragon this pulse. OutOfLair: drawn out of its lair, where it takes no damage and the raid leads it home.</summary>
    public readonly record struct Dragon(bool InCombat, bool Landed, bool OutOfLair, int HealthPercent, int Health);

    public const int MaximumFailedAttempts = RealmRaidDungeonRoute.MaximumFailedAttempts;
    public const long RegroupMinimumMilliseconds = RealmRaidDungeonRoute.RegroupMinimumMilliseconds;
    /// <summary>
    /// The raid needs this long to cross from its staging ring (3,600 to 6,100 units from the lair) to the dragon;
    /// before it has passed, a dragon out of combat is the approach, not a wipe. Longer than the dungeon's two minutes
    /// from a muster 1,400 units out.
    /// </summary>
    public const long EngageGraceMilliseconds = 3 * 60_000;
    /// <summary>A dragon that took no damage for this long while in the fight ends the attempt (the raid around it fell
    /// and only a trickle keeps it busy), as a dungeon encounter does.</summary>
    public const long NoProgressMilliseconds = RealmRaidDungeonRoute.TargetNoProgressMilliseconds;
    public const long RegroupLogMilliseconds = 2 * 60_000;

    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RealmRaidDragonFight));

    private readonly string _dragon;
    // Everyone who stood in the current attempt (raiders arriving during the fight join it).
    private readonly HashSet<long> _engaged = new();
    private bool _attempt;
    private long _attemptSince;
    private int _attemptLowestPercent = 100;
    // Lowest health points seen in the attempt: damage is progress, a percent can hide a few raiders' work.
    private int _lowestHealth;
    private long _progressTick;
    private long _regroupSince, _nextRegroupLog;
    private readonly Queue<int> _wipes = new();

    public RealmRaidDragonFight(string dragon)
    {
        _dragon = dragon;
    }

    /// <summary>The raid is gathering at its staging ring after a wipe.</summary>
    public bool Regrouping { get; private set; }
    public int FailedAttempts { get; private set; }
    /// <summary>The dragon's name once it has beaten the raid twice; null while the raid can still win.</summary>
    public string DefeatedAt { get; private set; }
    public bool Defeated => DefeatedAt != null;
    /// <summary>Raiders in the current attempt (for logs and tests).</summary>
    public int Engaged => _engaged.Count;
    /// <summary>What the launcher shows while the raid regroups or is defeated; null while it fights as before.</summary>
    public string Status { get; private set; }

    /// <summary>The dungeon rule: at least eight in the attempt and half of them fallen, or the dragon reset after real damage.</summary>
    public static bool IsWipe(int engaged, int fallen, int healthPercent, int lowestPercent) =>
        RealmRaidDungeonRoute.IsWipe(engaged, fallen, healthPercent, lowestPercent);

    /// <summary>Raiders a regroup waits for: 70% of the roster, 40% after 20 minutes, 8 to 150 (the dungeon rule).</summary>
    public static int RegroupNeeded(int roster, long regroupMilliseconds) =>
        RealmRaidDungeonRoute.RegroupNeeded(roster, regroupMilliseconds);

    /// <summary>
    /// The attempt is over when the dragon has been out of the fight after the approach grace, or took no damage for
    /// five minutes while still in it. Leading a dragon home from outside its lair counts as progress, not a stall.
    /// </summary>
    public static bool AttemptOver(bool dragonInCombat, long attemptMilliseconds, long sinceProgressMilliseconds) =>
        dragonInCombat ? sinceProgressMilliseconds >= NoProgressMilliseconds : attemptMilliseconds >= EngageGraceMilliseconds;

    /// <summary>The regroup goes in again: rested at least five minutes, enough of the roster at the ring, the dragon landed.</summary>
    public static bool GoesInAgain(int staged, int roster, long regroupMilliseconds, bool landed) =>
        landed && regroupMilliseconds >= RegroupMinimumMilliseconds && staged >= RegroupNeeded(roster, regroupMilliseconds);

    /// <summary>The raid goes in from its staging ring (the start): everyone staged is in the attempt.</summary>
    public void Engage(IEnumerable<long> staged, Dragon dragon, long now) => BeginAttempt(staged, dragon, now);

    /// <summary>The next wipe the raid has not reacted to yet (the realm's notice, the Special Forces).</summary>
    public bool TryTakeWipe(out int failures) => _wipes.TryDequeue(out failures);

    /// <summary>One raid pulse (every ten seconds) while the raid has started and the dragon lives.</summary>
    public void Advance(IReadOnlyList<Raider> raiders, Dragon dragon, long now)
    {
        if (Defeated) return;
        if (Regrouping)
        {
            Regroup(raiders, dragon, now);
            return;
        }
        if (dragon.InCombat)
        {
            long[] fighting = raiders.Where(r => r.AtDragon).Select(r => r.Id).ToArray();
            // A fight the raid walked into without going in together (a Leeroy, an early pull) is an attempt too, from
            // the raiders around the dragon; a dragon fighting somebody else is not the raid's business.
            if (_attempt) _engaged.UnionWith(fighting);
            else if (fighting.Length > 0) BeginAttempt(fighting, dragon, now);
            else return;
            _attemptLowestPercent = Math.Min(_attemptLowestPercent, dragon.HealthPercent);
            if (dragon.Health < _lowestHealth || dragon.OutOfLair)
            {
                _lowestHealth = Math.Min(_lowestHealth, dragon.Health);
                _progressTick = now;
            }
        }
        if (!_attempt || !AttemptOver(dragon.InCombat, now - _attemptSince, now - _progressTick)) return;
        EndAttempt(raiders, dragon, now, dragon.InCombat ? "no damage for five minutes" : "left combat");
    }

    private void BeginAttempt(IEnumerable<long> raiders, Dragon dragon, long now)
    {
        _attempt = true;
        _attemptSince = _progressTick = now;
        _engaged.Clear();
        _engaged.UnionWith(raiders);
        _attemptLowestPercent = dragon.HealthPercent;
        _lowestHealth = dragon.Health;
        Regrouping = false;
        Status = null;
    }

    /// <summary>Ends the attempt; true when it was a wipe (the raid now regroups or is defeated).</summary>
    private bool EndAttempt(IReadOnlyList<Raider> raiders, Dragon dragon, long now, string how)
    {
        _attempt = false;
        int engaged = _engaged.Count;
        var standing = new HashSet<long>(raiders.Where(r => r.Standing).Select(r => r.Id));
        // Dead, released and walking back, or no longer in the raid at all.
        int fallen = _engaged.Count(id => !standing.Contains(id));
        _engaged.Clear();
        if (!IsWipe(engaged, fallen, dragon.HealthPercent, _attemptLowestPercent))
        {
            if (Log.IsInfoEnabled)
                Log.Info($"REALM_RAID_DRAGON_BROKE_OFF dragon=\"{_dragon}\" engaged={engaged} fallen={fallen} health={dragon.HealthPercent} " +
                         $"lowest={_attemptLowestPercent} how=\"{how}\"");
            return false;
        }
        FailedAttempts++;
        _wipes.Enqueue(FailedAttempts);
        // Guarded like the Info lines: the tests run this class with no logging configured.
        if (Log.IsWarnEnabled)
            Log.Warn($"REALM_RAID_DRAGON_WIPE dragon=\"{_dragon}\" failures={FailedAttempts} of={MaximumFailedAttempts} engaged={engaged} " +
                     $"fallen={fallen} health={dragon.HealthPercent} lowest={_attemptLowestPercent} how=\"{how}\"");
        if (FailedAttempts >= MaximumFailedAttempts)
        {
            DefeatedAt = _dragon;
            Status = $"defeated at {_dragon}";
            return true;
        }
        Regrouping = true;
        _regroupSince = now;
        _nextRegroupLog = 0;
        Status = RegroupStatus(0, RegroupNeeded(raiders.Count, 0), dragon.Landed);
        return true;
    }

    private void Regroup(IReadOnlyList<Raider> raiders, Dragon dragon, long now)
    {
        long regroupMilliseconds = now - _regroupSince;
        int roster = raiders.Count;
        int staged = raiders.Count(r => r.AtStaging);
        int needed = RegroupNeeded(roster, regroupMilliseconds);
        if (GoesInAgain(staged, roster, regroupMilliseconds, dragon.Landed))
        {
            BeginAttempt(raiders.Where(r => r.AtStaging).Select(r => r.Id), dragon, now);
            if (Log.IsInfoEnabled)
                Log.Info($"REALM_RAID_DRAGON_ENGAGE dragon=\"{_dragon}\" staged={staged} needed={needed} roster={roster} " +
                         $"minutes={regroupMilliseconds / 60_000.0:0.0} attempt={FailedAttempts + 1} retry=true");
            return;
        }
        Status = RegroupStatus(staged, needed, dragon.Landed);
        if (now < _nextRegroupLog) return;
        _nextRegroupLog = now + RegroupLogMilliseconds;
        if (Log.IsInfoEnabled)
            Log.Info($"REALM_RAID_DRAGON_REGROUP dragon=\"{_dragon}\" staged={staged} needed={needed} roster={roster} " +
                     $"standing={raiders.Count(r => r.Standing)} landed={dragon.Landed} minutes={regroupMilliseconds / 60_000.0:0.0}");
    }

    private string RegroupStatus(int staged, int needed, bool landed) =>
        $"regrouping at the staging ring after a wipe ({staged}/{needed}, attempt {FailedAttempts + 1} of {MaximumFailedAttempts})" +
        (landed ? "" : ", dragon airborne");
}
