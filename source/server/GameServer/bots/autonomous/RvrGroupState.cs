using System.Collections.Immutable;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The facts about one bot group that its open-field fight plan needs and that live elsewhere (the group coordinator's
/// session, the RvR event layer): its skill tier, its recent wipes and its rally point. Whoever owns a fact copies it
/// in when it changes (<see cref="RvrFightBoards.UpdateState"/>), so the board builder never takes the coordinator's
/// global lock. Immutable; a change makes a new record.
/// </summary>
public sealed record RvrGroupState
{
    /// <summary>How long a wipe counts as recent.</summary>
    public const long WipeMemoryMilliseconds = 10 * 60_000;
    /// <summary>At most this many wipes are remembered (the newest).</summary>
    public const int MaxWipes = 8;

    /// <summary>The group's tier; null until it is first asked for (the board builder then sets it from the leader).</summary>
    public RvrSkillTier? SkillTier { get; init; }
    /// <summary>When the group last wiped (game-loop ticks, oldest first).</summary>
    public ImmutableArray<long> RecentWipes { get; init; } = ImmutableArray<long>.Empty;
    /// <summary>Where the group meets after a retreat or a wipe; null: none set.</summary>
    public Vector3? RallyPoint { get; init; }
    public ushort RallyRegion { get; init; }

    public static RvrGroupState Empty { get; } = new();

    /// <summary>How many of the remembered wipes are recent at <paramref name="now"/>. Pure.</summary>
    public int WipesWithin(long now, long window = WipeMemoryMilliseconds)
    {
        int count = 0;
        foreach (long wipe in RecentWipes)
            if (now - wipe <= window)
                count++;
        return count;
    }

    /// <summary>This state with a wipe at <paramref name="now"/> added and the stale ones dropped. Pure.</summary>
    public RvrGroupState WithWipe(long now)
    {
        ImmutableArray<long>.Builder wipes = ImmutableArray.CreateBuilder<long>();
        foreach (long wipe in RecentWipes)
            if (now - wipe <= WipeMemoryMilliseconds)
                wipes.Add(wipe);
        wipes.Add(now);
        while (wipes.Count > MaxWipes)
            wipes.RemoveAt(0);
        return this with { RecentWipes = wipes.ToImmutable() };
    }

    /// <summary>This state with the rally point set (null clears it). Pure.</summary>
    public RvrGroupState WithRally(Vector3? point, ushort region) => this with { RallyPoint = point, RallyRegion = point.HasValue ? region : (ushort)0 };
}
