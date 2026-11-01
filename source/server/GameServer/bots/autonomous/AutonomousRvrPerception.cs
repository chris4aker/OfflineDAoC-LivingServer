using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>The rvr_bot_awareness setting: how sharp autonomous gamebots are at spotting enemy players in RvR.</summary>
public enum RvrBotAwareness
{
    Easy,
    Normal,
    Veteran,
}

/// <summary>What a frontier scan found, for the perception rule. Only players and what they bring are perceived.</summary>
public enum RvrPerceivedKind
{
    /// <summary>Everything else (enemy gamebots and their pets, siege, guards, doors, monsters): acquired as before.</summary>
    Other,
    Player,
    /// <summary>A player's /spawn companion or a temporary helper bot.</summary>
    PlayerCompanion,
    /// <summary>A pet owned by a player or by a player's companion.</summary>
    PlayerPet,
}

/// <summary>Why a target was noticed (logged with RVR_NOTICE).</summary>
[Flags]
public enum RvrNoticeFactors
{
    None = 0,
    Close = 1,
    Combat = 2,
    Casting = 4,
    Group = 8,
    Facing = 16,
    Still = 32,
    Alerted = 64,
}

/// <summary>The numbers one rvr_bot_awareness level uses.</summary>
public readonly record struct RvrAwarenessProfile(int AlwaysNoticeRadius, int OuterRange, double NearChance, double FarChance,
    double ReactionScale, int SpotterRadius);

/// <summary>One enemy as one bot sees it on one scan.</summary>
public readonly record struct RvrSighting(double Distance, bool InCombat, bool Casting, int GroupSize, bool InFacingCone, bool StandingStill);

/// <summary>A bot that could hear a spotter's call (pure input for SpotterRecipients).</summary>
public readonly record struct RvrSpotterMate(long Id, int X, int Y, int Z, bool Alive, bool SameRegion);

/// <summary>
/// RvR perception (docs/BOT-BUILDS-AND-RVR.md, "RvR perception"; owner Chris, 2026-11-01). A player on r/daoc reported
/// that gamebots in the frontier saw him from 5,000 units, instantly and all at once, whenever he was in line of sight:
/// radar, not players. Now an autonomous gamebot notices an enemy player (or that player's companion or pet) by chance on
/// each scan, the chance falling with distance and rising when the enemy is fighting, casting, in a group or in front of
/// the bot; it then takes a moment to react, and it tells its group and warband, who react after their own moment. Close
/// up it always notices. Enemy gamebots, siege, keep guards, doors and monsters are acquired as before, and a bot or
/// group under attack fights back at once. The rules here are pure (unit-tested); the frontier scan reads the game.
/// </summary>
public static class AutonomousRvrPerception
{
    // ---- the numbers (normal is the 2003 player; easy and veteran scale it)

    /// <summary>Within this distance a scan always notices (normal).</summary>
    public const int AlwaysNoticeRadius = 1_000;
    /// <summary>Beyond this a bot never notices on its own (normal); the old scan went to 5,000.</summary>
    public const int OuterRange = 3_500;
    /// <summary>The per-scan chance just outside the always radius (normal). Scans come every 1.0-1.25 s.</summary>
    public const double NearChance = 0.50;
    /// <summary>The per-scan chance at the outer range (normal).</summary>
    public const double FarChance = 0.03;
    /// <summary>No boost makes a scan beyond the always radius certain.</summary>
    public const double MaxChance = 0.95;

    public const double CombatFactor = 2.0;
    public const double CastingFactor = 1.5;
    public const double GroupFactor = 1.5;
    public const int GroupMinimum = 3;
    public const double FacingFactor = 1.75;
    public const double FacingConeDegrees = 120;
    public const double StillFactor = 0.5;

    public const int ReactionMinMilliseconds = 500;
    public const int ReactionMaxMilliseconds = 2_000;

    /// <summary>A spotter's group and warband mates within this hear the call (normal).</summary>
    public const int SpotterRadius = 2_500;
    /// <summary>A noticed enemy is forgotten after this long out of the bot's sight.</summary>
    public const int ForgetAfterMilliseconds = 15_000;
    /// <summary>At most one spoken spot call in a group this often.</summary>
    public const int SpotCallGapMilliseconds = 12_000;

    public static RvrBotAwareness ParseAwareness(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "easy" => RvrBotAwareness.Easy,
        "veteran" => RvrBotAwareness.Veteran,
        _ => RvrBotAwareness.Normal,
    };

    public static RvrAwarenessProfile Profile(RvrBotAwareness awareness) => awareness switch
    {
        RvrBotAwareness.Easy => new(700, 2_500, 0.35, 0.02, 1.3, 2_000),
        RvrBotAwareness.Veteran => new(1_300, 4_500, 0.70, 0.08, 0.7, 3_000),
        _ => new(AlwaysNoticeRadius, OuterRange, NearChance, FarChance, 1.0, SpotterRadius),
    };

    /// <summary>Only enemy players and what they bring are perceived; everything else is acquired as before.</summary>
    public static bool IsSubject(RvrPerceivedKind kind) => kind != RvrPerceivedKind.Other;

    /// <summary>
    /// Whether the perception rule applies to this pick: the setting is on, the target is a player-kind, and the target is
    /// not firing on the bot or its realm-mates near it, nor attacking its group (retaliation stays immediate).
    /// </summary>
    public static bool Applies(bool enabled, RvrPerceivedKind kind, bool firingOnRealm, bool attackingGroup) =>
        enabled && IsSubject(kind) && !firingOnRealm && !attackingGroup;

    /// <summary>The chance that one scan notices this enemy, and the factors that moved it. Pure.</summary>
    public static double NoticeChance(RvrSighting sighting, RvrAwarenessProfile profile, out RvrNoticeFactors factors)
    {
        factors = RvrNoticeFactors.None;
        if (sighting.Distance <= profile.AlwaysNoticeRadius)
        {
            factors = RvrNoticeFactors.Close;
            return 1.0;
        }
        if (sighting.Distance > profile.OuterRange)
            return 0.0;
        // Falls fast past the always radius and flattens near the outer range: (1 - t)^2.
        double t = (sighting.Distance - profile.AlwaysNoticeRadius) / Math.Max(1, profile.OuterRange - profile.AlwaysNoticeRadius);
        double chance = profile.FarChance + (profile.NearChance - profile.FarChance) * (1 - t) * (1 - t);
        if (sighting.InCombat) { chance *= CombatFactor; factors |= RvrNoticeFactors.Combat; }
        if (sighting.Casting) { chance *= CastingFactor; factors |= RvrNoticeFactors.Casting; }
        if (sighting.GroupSize >= GroupMinimum) { chance *= GroupFactor; factors |= RvrNoticeFactors.Group; }
        if (sighting.InFacingCone) { chance *= FacingFactor; factors |= RvrNoticeFactors.Facing; }
        if (sighting.StandingStill && !sighting.InCombat) { chance *= StillFactor; factors |= RvrNoticeFactors.Still; }
        return Math.Min(MaxChance, chance);
    }

    public static double NoticeChance(RvrSighting sighting, RvrAwarenessProfile profile) => NoticeChance(sighting, profile, out _);

    /// <summary>Whether a scan with this roll (0 to 1) notices. Pure.</summary>
    public static bool Notices(RvrSighting sighting, RvrAwarenessProfile profile, double roll, out RvrNoticeFactors factors) =>
        roll < NoticeChance(sighting, profile, out factors);

    /// <summary>Whether a bearing (degrees clockwise from where the bot faces, 0-360) is inside the facing cone. Pure.</summary>
    public static bool InFacingCone(double bearingDegrees, double coneDegrees = FacingConeDegrees)
    {
        double bearing = (bearingDegrees % 360 + 360) % 360;
        return bearing <= coneDegrees / 2 || bearing >= 360 - coneDegrees / 2;
    }

    /// <summary>
    /// How bold the bot is, 0 to 1: Daredevils 1, others from their Risk trait; 0.5 with personalities off (null). Pure.
    /// </summary>
    public static double Boldness(BotPersonality? personality) => personality switch
    {
        null => 0.5,
        { Type: BotPersonalityType.Daredevil } => 1.0,
        { } p => Math.Clamp(p.Traits.Risk, 0, 9) / 9.0 * 0.8,
    };

    /// <summary>
    /// The delay between noticing and engaging, 0.5-2.0 s at normal: higher level and bolder personalities are faster, and a
    /// roll (0 to 1) adds the rest. Easy is slower and veteran faster (the profile's reaction scale). Pure.
    /// </summary>
    public static int ReactionDelayMilliseconds(int level, BotPersonality? personality, double roll, RvrAwarenessProfile profile)
    {
        double levelFactor = (Math.Clamp(level, 1, 50) - 1) / 49.0;
        double speed = 0.45 * levelFactor + 0.35 * Boldness(personality) + 0.20 * Math.Clamp(roll, 0, 1);
        double delay = ReactionMaxMilliseconds - (ReactionMaxMilliseconds - ReactionMinMilliseconds) * speed;
        return (int)Math.Round(delay * profile.ReactionScale);
    }

    /// <summary>The spotter's mates that hear its call: alive, in its region, within the spotter radius, not itself. Pure.</summary>
    public static long[] SpotterRecipients(long spotterId, int x, int y, int z, IEnumerable<RvrSpotterMate> mates, int radius)
    {
        long squared = (long)radius * radius;
        return mates.Where(mate => mate.Id != spotterId && mate.Alive && mate.SameRegion &&
                (long)(mate.X - x) * (mate.X - x) + (long)(mate.Y - y) * (mate.Y - y) + (long)(mate.Z - z) * (mate.Z - z) <= squared)
            .Select(mate => mate.Id).Distinct().ToArray();
    }

    /// <summary>The compass word from the bot towards the enemy (DAoC: x grows east, y grows south). Pure.</summary>
    public static string Direction(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
            return "here";
        double degrees = Math.Atan2(dx, -dy) * 180 / Math.PI;
        int sector = (int)Math.Round(((degrees % 360) + 360) % 360 / 45) % 8;
        return sector switch
        {
            0 => "north", 1 => "ne", 2 => "east", 3 => "se", 4 => "south", 5 => "sw", 6 => "west", _ => "nw",
        };
    }

    /// <summary>The spoken call, "inc 3 north". Pure.</summary>
    public static string SpotCall(int count, int dx, int dy) => $"inc {Math.Max(1, count)} {Direction(dx, dy)}";

    public static string Describe(RvrNoticeFactors factors) =>
        factors == RvrNoticeFactors.None ? "distance" : factors.ToString().Replace(", ", "+");

    /// <summary>
    /// What each bot has noticed: (bot id, target key) to when it may engage. Entries are forgotten
    /// ForgetAfterMilliseconds after the bot last saw the target, and swept now and then. Thread-safe.
    /// </summary>
    public sealed class Memory
    {
        private sealed class Entry
        {
            public long ReadyAt;
            public long LastSeen;
        }

        public enum State
        {
            Unnoticed,
            Reacting,
            Ready,
        }

        private readonly ConcurrentDictionary<(long Bot, long Target), Entry> _entries = new();
        private long _nextSweep;

        public int Count => _entries.Count;

        /// <summary>The bot sees the target on this scan: its state, refreshing the entry.</summary>
        public State Seen(long bot, long target, long now)
        {
            Sweep(now);
            if (!_entries.TryGetValue((bot, target), out Entry entry))
                return State.Unnoticed;
            lock (entry)
            {
                if (now - entry.LastSeen > ForgetAfterMilliseconds)
                {
                    _entries.TryRemove((bot, target), out _);
                    return State.Unnoticed;
                }
                entry.LastSeen = now;
                return now >= entry.ReadyAt ? State.Ready : State.Reacting;
            }
        }

        /// <summary>The bot noticed the target on its own: it may engage after the delay.</summary>
        public void Notice(long bot, long target, long now, int delayMilliseconds) => Set(bot, target, now, now + delayMilliseconds);

        /// <summary>
        /// A mate called the target: the bot treats it as noticed after its own delay. An earlier entry stays. Returns true
        /// when the call was news to this bot.
        /// </summary>
        public bool Alert(long bot, long target, long now, int delayMilliseconds)
        {
            Sweep(now);
            if (_entries.TryGetValue((bot, target), out Entry existing) && now - existing.LastSeen <= ForgetAfterMilliseconds)
                return false;
            Set(bot, target, now, now + delayMilliseconds);
            return true;
        }

        private void Set(long bot, long target, long now, long readyAt) =>
            _entries[(bot, target)] = new Entry { ReadyAt = readyAt, LastSeen = now };

        private void Sweep(long now)
        {
            if (now < Volatile.Read(ref _nextSweep))
                return;
            Volatile.Write(ref _nextSweep, now + ForgetAfterMilliseconds);
            foreach (var pair in _entries)
                if (now - pair.Value.LastSeen > ForgetAfterMilliseconds)
                    _entries.TryRemove(pair.Key, out _);
        }

        public void Clear() => _entries.Clear();
    }
}
