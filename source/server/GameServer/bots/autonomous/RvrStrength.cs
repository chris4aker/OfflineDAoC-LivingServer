using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// One side of an open-field fight in numbers (docs/RVR-FIELD-1.2.md, the Observe stance: "heads, healers, levels,
/// health, power"). A record only: work package 0 counts, work package 4 (size-up) compares two of them. Immutable.
/// </summary>
public sealed record RvrStrength
{
    /// <summary>Heads: players and bots, pets not counted.</summary>
    public int Count { get; init; }
    /// <summary>Main healers among them (<see cref="RvrRole.Healer"/>: Cleric, Druid, Healer).</summary>
    public int Healers { get; init; }
    /// <summary>Pets (not in <see cref="Count"/>).</summary>
    public int Pets { get; init; }
    /// <summary>The sum of the heads' levels.</summary>
    public int TotalLevels { get; init; }
    public int HighestLevel { get; init; }
    /// <summary>The heads' average health, 0-100.</summary>
    public int HealthPercent { get; init; }
    /// <summary>How many of the heads use power.</summary>
    public int PowerUsers { get; init; }
    /// <summary>The average power of those who use it, 0-100 (0 when nobody does).</summary>
    public int PowerPercent { get; init; }

    public int AverageLevel => Count == 0 ? 0 : (int)Math.Round((double)TotalLevels / Count, MidpointRounding.AwayFromZero);

    /// <summary>Nobody.</summary>
    public static RvrStrength None { get; } = new();

    /// <summary>Counts one side. Pure.</summary>
    public static RvrStrength Of(IEnumerable<RvrCombatant> side)
    {
        int count = 0, healers = 0, pets = 0, levels = 0, highest = 0, health = 0, powerUsers = 0, power = 0;
        if (side != null)
            foreach (RvrCombatant combatant in side)
            {
                if (combatant == null)
                    continue;
                if (combatant.IsPet)
                {
                    pets++;
                    continue;
                }
                count++;
                if (combatant.Role == RvrRole.Healer)
                    healers++;
                levels += combatant.Level;
                highest = Math.Max(highest, combatant.Level);
                health += Math.Clamp(combatant.HealthPercent, 0, 100);
                if (combatant.UsesPower)
                {
                    powerUsers++;
                    power += Math.Clamp(combatant.PowerPercent, 0, 100);
                }
            }
        if (count == 0 && pets == 0)
            return None;
        return new RvrStrength
        {
            Count = count,
            Healers = healers,
            Pets = pets,
            TotalLevels = levels,
            HighestLevel = highest,
            HealthPercent = count == 0 ? 0 : (int)Math.Round((double)health / count, MidpointRounding.AwayFromZero),
            PowerUsers = powerUsers,
            PowerPercent = powerUsers == 0 ? 0 : (int)Math.Round((double)power / powerUsers, MidpointRounding.AwayFromZero),
        };
    }

    /// <summary>For the log: "5 heads (2 healers, 1 pets) L50 hp 87 pow 64".</summary>
    public override string ToString() =>
        $"{Count} heads ({Healers} healers, {Pets} pets) L{AverageLevel} hp {HealthPercent} pow {PowerPercent}";
}
