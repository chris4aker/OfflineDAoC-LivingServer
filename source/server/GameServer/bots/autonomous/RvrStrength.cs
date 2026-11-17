using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// One side of an open-field fight in numbers (docs/RVR-FIELD-1.2.md, the Observe stance: "heads, healers, levels,
/// health, power"). Work package 0 counts heads, healers, levels, health and power; work package 4 adds the make-up
/// (melee, ranged, crowd-controlled heads) and one realm's share of a side (<see cref="Of(IEnumerable{RvrCombatant}, eRealm)"/>,
/// for a fight between the other two realms), and compares two strengths (<see cref="RvrEngagement.Power"/>,
/// <see cref="RvrEngagement.Odds"/>). Immutable.
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
    /// <summary>Heads that fight in melee: tanks, melee damage and stealthers.</summary>
    public int MeleeHeads { get; init; }
    /// <summary>Heads that fight from range or from behind: casters, archers, healers and hybrid support.</summary>
    public int RangedHeads { get; init; }
    /// <summary>Heads mezzed or stunned when the board was built (out of the fight for now). A breakable root is not counted.</summary>
    public int Controlled { get; init; }

    public int AverageLevel => Count == 0 ? 0 : (int)Math.Round((double)TotalLevels / Count, MidpointRounding.AwayFromZero);
    /// <summary>The share of the heads that fight in melee, 0 to 1.</summary>
    public double MeleeShare => Count == 0 ? 0 : (double)MeleeHeads / Count;
    /// <summary>The share of the heads that fight from range or from behind, 0 to 1.</summary>
    public double RangedShare => Count == 0 ? 0 : (double)RangedHeads / Count;

    /// <summary>Nobody.</summary>
    public static RvrStrength None { get; } = new();

    /// <summary>Counts one side. Pure.</summary>
    public static RvrStrength Of(IEnumerable<RvrCombatant> side) => Of(side, eRealm.None);

    /// <summary>
    /// Counts the part of a side that belongs to <paramref name="realm"/> (all of it for <see cref="eRealm.None"/>): the
    /// enemy in view is two realms when they fight each other. Pure.
    /// </summary>
    public static RvrStrength Of(IEnumerable<RvrCombatant> side, eRealm realm)
    {
        int count = 0, healers = 0, pets = 0, levels = 0, highest = 0, health = 0, powerUsers = 0, power = 0;
        int melee = 0, ranged = 0, controlled = 0;
        if (side != null)
            foreach (RvrCombatant combatant in side)
            {
                if (combatant == null || realm != eRealm.None && combatant.Realm != realm)
                    continue;
                if (combatant.IsPet)
                {
                    pets++;
                    continue;
                }
                count++;
                if (combatant.Role == RvrRole.Healer)
                    healers++;
                if (combatant.Role is RvrRole.Tank or RvrRole.Melee or RvrRole.Stealther)
                    melee++;
                else if (combatant.Role is RvrRole.Caster or RvrRole.Archer or RvrRole.Healer or RvrRole.Support)
                    ranged++;
                if ((combatant.Control & (RvrControlState.Mezzed | RvrControlState.Stunned)) != 0)
                    controlled++;
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
            MeleeHeads = melee,
            RangedHeads = ranged,
            Controlled = controlled,
        };
    }

    /// <summary>For the log: "5 heads (2 healers, 1 pets) L50 hp 87 pow 64".</summary>
    public override string ToString() =>
        $"{Count} heads ({Healers} healers, {Pets} pets) L{AverageLevel} hp {HealthPercent} pow {PowerPercent}";
}
