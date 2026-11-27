using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>One enemy a fight board listed, as the shadow checks it against the members' contacts.</summary>
public readonly record struct RvrShadowListing(long Target, bool Known, bool Stale, bool Player, int Distance);

/// <summary>The shadow's counts for one realm (one minute, or since the last Clear).</summary>
public readonly record struct RvrShadowTotals(long Listed, long Known, long Stale, long Unknown, long UnknownPlayers,
    int Pairs, int PairsUnknown, int PairsUnknownPlayers)
{
    /// <summary>The share of board-listed enemy-seconds no member knew of (0 with nothing listed).</summary>
    public double UnknownShare => Listed == 0 ? 0 : (double)Unknown / Listed;

    /// <summary>The share of (group, enemy) pairs listed that no member ever knew of while listed: the #529 acceptance number.</summary>
    public double PairsUnknownShare => Pairs == 0 ? 0 : (double)PairsUnknown / Pairs;
}

/// <summary>
/// #529 step 2, the shadow of the fight board: every board build checks each enemy its area search listed against the
/// contacts of the group's members (RvrContacts) and counts it known, stale (known, but last seen more than the
/// perception memory's 15 s ago) or unknown ("listed but unknown": the omniscient group mind of structure.md finding 1).
/// Nothing changes on the board. Counted per realm in one-minute windows and logged as RVR_CONTACT_SHADOW: board entries
/// (enemy-seconds, a board is rebuilt at most once a second) and distinct (group, enemy) pairs, a pair being unknown when
/// no listing of it in the window was known. A pair seen across a window's edge counts in both windows. The first
/// unknown listing of a pair in a window may be sampled as RVR_CONTACT_UNKNOWN, at most SamplesPerWindow per realm.
/// Pure apart from its own state; thread-safe.
/// </summary>
public sealed class RvrContactShadow
{
    public const long WindowMilliseconds = 60_000;
    public const int SamplesPerWindow = 20;
    /// <summary>A known contact last seen longer ago than this is "stale" (the perception memory forgets after 15 s).</summary>
    public const long StaleAfterMilliseconds = AutonomousRvrPerception.ForgetAfterMilliseconds;

    private sealed class Window
    {
        public long Start;
        public long Listed, Known, Stale, Unknown, UnknownPlayers;
        // (group, enemy) to (ever known in this window, is a player)
        public readonly Dictionary<(long Group, long Target), (bool Known, bool Player)> Pairs = new();
        public int Samples;
    }

    private sealed class Realm
    {
        public Window Current;
        public RvrShadowTotals Total;
        public readonly HashSet<(long Group, long Target)> AllPairs = new();
        public readonly HashSet<(long Group, long Target)> AllKnown = new();
        public readonly HashSet<(long Group, long Target)> AllPlayers = new();
    }

    private readonly Dictionary<int, Realm> _realms = new();

    /// <summary>
    /// Whether <see cref="Totals(int)"/> keeps distinct pairs over the whole time (the rehearsal and tests). Off in the
    /// game, where the minute windows are the record and an hours-long set of pairs would only grow.
    /// </summary>
    public bool TrackPairs { get; set; }

    /// <summary>
    /// One board build of <paramref name="group"/> (its party key) of <paramref name="realm"/>. Returns the listings this
    /// call may sample as RVR_CONTACT_UNKNOWN (the first unknown listing of a pair in the window, within the cap), and in
    /// <paramref name="lines"/> the RVR_CONTACT_SHADOW lines of windows that closed (null when none).
    /// </summary>
    public List<RvrShadowListing> Board(int realm, long group, long now, IReadOnlyList<RvrShadowListing> listed, out List<string> lines)
    {
        lines = null;
        List<RvrShadowListing> samples = null;
        lock (_realms)
        {
            if (!_realms.TryGetValue(realm, out Realm state))
                _realms[realm] = state = new Realm();
            lines = Roll(now, null);
            state.Current ??= new Window { Start = now };
            Window window = state.Current;
            foreach (RvrShadowListing listing in listed)
            {
                var pair = (group, listing.Target);
                window.Listed++;
                if (TrackPairs)
                {
                    state.AllPairs.Add(pair);
                    if (listing.Player)
                        state.AllPlayers.Add(pair);
                }
                if (listing.Known)
                {
                    window.Known++;
                    if (listing.Stale)
                        window.Stale++;
                    if (TrackPairs)
                        state.AllKnown.Add(pair);
                }
                else
                {
                    window.Unknown++;
                    if (listing.Player)
                        window.UnknownPlayers++;
                }
                bool firstUnknown = !listing.Known && !window.Pairs.ContainsKey(pair);
                window.Pairs[pair] = window.Pairs.TryGetValue(pair, out var seen)
                    ? (seen.Known || listing.Known, listing.Player)
                    : (listing.Known, listing.Player);
                if (firstUnknown && window.Samples < SamplesPerWindow)
                {
                    window.Samples++;
                    (samples ??= new List<RvrShadowListing>()).Add(listing);
                }
            }
        }
        return samples;
    }

    /// <summary>Closes the windows that are over at <paramref name="now"/> and returns their lines (null when none).</summary>
    public List<string> TakeLines(long now)
    {
        lock (_realms)
            return Roll(now, null);
    }

    private List<string> Roll(long now, List<string> lines)
    {
        foreach (var pair in _realms)
        {
            Window window = pair.Value.Current;
            if (window == null || now - window.Start < WindowMilliseconds)
                continue;
            RvrShadowTotals totals = Totals(window);
            Realm state = pair.Value;
            state.Total = new RvrShadowTotals(state.Total.Listed + totals.Listed, state.Total.Known + totals.Known,
                state.Total.Stale + totals.Stale, state.Total.Unknown + totals.Unknown, state.Total.UnknownPlayers + totals.UnknownPlayers,
                0, 0, 0);
            state.Current = null;
            if (totals.Listed > 0)
                (lines ??= new List<string>()).Add(Describe(pair.Key, totals, window.Start, now));
        }
        return lines;
    }

    private static RvrShadowTotals Totals(Window window)
    {
        int unknown = 0, unknownPlayers = 0;
        foreach (var entry in window.Pairs.Values)
            if (!entry.Known)
            {
                unknown++;
                if (entry.Player)
                    unknownPlayers++;
            }
        return new RvrShadowTotals(window.Listed, window.Known, window.Stale, window.Unknown, window.UnknownPlayers,
            window.Pairs.Count, unknown, unknownPlayers);
    }

    /// <summary>
    /// The counts of <paramref name="realm"/> since the last Clear, the open window included. Pairs here are distinct over
    /// the whole time (a pair is unknown when no listing of it was ever known), not summed per window; 0 unless
    /// <see cref="TrackPairs"/> is on.
    /// </summary>
    public RvrShadowTotals Totals(int realm)
    {
        lock (_realms)
        {
            if (!_realms.TryGetValue(realm, out Realm state))
                return default;
            RvrShadowTotals closed = state.Total;
            RvrShadowTotals open = state.Current != null ? Totals(state.Current) : default;
            int unknownPlayers = 0;
            foreach (var pair in state.AllPlayers)
                if (!state.AllKnown.Contains(pair))
                    unknownPlayers++;
            return new RvrShadowTotals(closed.Listed + open.Listed, closed.Known + open.Known, closed.Stale + open.Stale,
                closed.Unknown + open.Unknown, closed.UnknownPlayers + open.UnknownPlayers, state.AllPairs.Count,
                state.AllPairs.Count - state.AllKnown.Count, unknownPlayers);
        }
    }

    /// <summary>The RVR_CONTACT_SHADOW line of one closed window. Pure.</summary>
    public static string Describe(int realm, in RvrShadowTotals totals, long start, long end)
    {
        // One minute of one realm's fight-board shadow, counting board-listed enemies and group-enemy pairs no member knew of.
        return $"RVR_CONTACT_SHADOW realm={GlobalConstants.RealmToName((eRealm)realm)} windowS={(end - start) / 1000} " +
        $"listed={totals.Listed} known={totals.Known} stale={totals.Stale} unknown={totals.Unknown} " +
        $"unknownPlayers={totals.UnknownPlayers} unknownShare={totals.UnknownShare:0.###} pairs={totals.Pairs} " +
        $"pairsUnknown={totals.PairsUnknown} pairsUnknownPlayers={totals.PairsUnknownPlayers} " +
        $"pairsUnknownShare={totals.PairsUnknownShare:0.###}";
    }

    public void Clear()
    {
        lock (_realms)
            _realms.Clear();
    }
}
