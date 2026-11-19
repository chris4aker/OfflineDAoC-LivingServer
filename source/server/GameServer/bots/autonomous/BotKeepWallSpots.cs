using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Bots as people for 1.2, item 5 (work package 5): bots waiting at a keep their realm holds sit on its walls. The leader
/// calls stick and the group "runs around a bit, climbing up wall" (ERA-RESEARCH section 9, FH Jan 2003). Owner decision
/// 11 (2026-11-17): only at a keep the realm holds, with no siege there and no enemy noticed; committed defenders and
/// siege fighters keep today's rules. A wall spot is a guard archer's position floored on the navigation mesh and
/// reached by a proven route, as AutonomousRvrDefense.Position already does: never an invented wall-top point. The spot
/// must stand above the keep's gate (on the wall, not in the courtyard) and near where the bot waits, so the group stays
/// together (AutonomousBotGroupCoordinator.CohesionRadius is 500). A bot sitting on a wall stands within 0.3-1 s of
/// noticing an enemy, and at once when it moves, casts or fights. The same for all tiers. Pure.
/// </summary>
public static class BotKeepWallSpots
{
    /// <summary>A keep this close (2D, to its centre) to where the bot waits is the keep it waits at.</summary>
    public const float KeepRadius = 1_200f;
    /// <summary>A wall spot at most this far (2D) from where the bot waits (the group leader for a member).</summary>
    public const float MaxFromAnchor = 400f;
    /// <summary>A spot at least this much above the keep's lowest gate: on the wall, not in the courtyard.</summary>
    public const float MinimumRise = 100f;
    /// <summary>Nobody else sits this close to a spot already.</summary>
    public const float PersonalSpace = 40f;
    /// <summary>An enemy player or bot within this of a bot on the wall is noticed.</summary>
    public const float WatchRadius = 2_500f;
    public const long StandMinimumMs = 300;
    public const long StandMaximumMs = 1_000;
    /// <summary>A bot that did not reach its spot in this long gives it up (and simply sits where it waits).</summary>
    public const long WalkTimeoutMs = 20_000;
    /// <summary>It has arrived this close (2D) to its spot.</summary>
    public const float ArriveTolerance = 24f;
    /// <summary>During the walk and while it sits, the bot checks the keep, its scope and its watch at most this often.</summary>
    public const long RecheckMs = 1_000;

    /// <summary>
    /// Only a party member standing in formation around its standing leader takes the walls: its follow slot walks it
    /// there and back (AutonomousGroupFormationFollow), with no order the wait sites stop. A leader, a bot at a
    /// rendezvous and a soloer sit where they wait. Pure.
    /// </summary>
    public static bool Offered(bool formationMember) => formationMember;

    /// <summary>
    /// Whether a bot walking to or sitting on a wall keeps at it: its scope still allows it and decision 11 still holds
    /// (held, no siege, no enemy noticed, not a committed defender or siege fighter). Checked each
    /// <see cref="RecheckMs"/>. Pure.
    /// </summary>
    public static bool StillAllowed(bool scopeAllows, bool heldByRealm, bool siege, bool enemyNoticed, bool committedDefender,
        bool siegeFighter) =>
        scopeAllows && Allowed(heldByRealm, siege, enemyNoticed, committedDefender, siegeFighter);

    public enum WalkStep { Walk, Arrive, Abandon, GiveUp }

    /// <summary>
    /// One turn of the walk to a wall spot <paramref name="distance"/> away (2D): abandoned when a re-check found it no
    /// longer allowed, arrived within <see cref="ArriveTolerance"/> (+16 while still settling), given up after
    /// <see cref="WalkTimeoutMs"/>, else walking on (its follow slot walks it; nothing is ordered here). Pure.
    /// </summary>
    public static WalkStep Step(float distance, bool moving, long walkedMs, bool recheckDue, bool stillAllowed)
    {
        if (recheckDue && !stillAllowed)
            return WalkStep.Abandon;
        if (distance <= ArriveTolerance + 16 && (!moving || distance <= 8))
            return WalkStep.Arrive;
        return walkedMs > WalkTimeoutMs ? WalkStep.GiveUp : WalkStep.Walk;
    }

    /// <summary>A guard's floored position at a keep.</summary>
    /// <param name="Floor">The mesh floor under the guard (TryFloor), within 96 units of its height.</param>
    /// <param name="Archer">A guard archer (the wall's own guards; other guards stand in the courtyard and at the gate).</param>
    /// <param name="Rise">How far the floor stands above the keep's lowest gate (or its centre with no gate).</param>
    public readonly record struct Candidate(Vector3 Floor, bool Archer, float Rise);

    /// <summary>
    /// Whether a waiting bot may take the walls of this keep: its realm holds it, no siege is on there, no enemy is noticed,
    /// and the bot is neither a committed defender nor a siege fighter (owner decision 11). Pure.
    /// </summary>
    public static bool Allowed(bool heldByRealm, bool siege, bool enemyNoticed, bool committedDefender, bool siegeFighter) =>
        heldByRealm && !siege && !enemyNoticed && !committedDefender && !siegeFighter;

    /// <summary>How far a floor at <paramref name="floorZ"/> stands above the keep's lowest gate, or above the keep's own
    /// height when it has no gate. Pure.</summary>
    public static float Rise(float floorZ, float? lowestGateZ, float keepZ) => floorZ - (lowestGateZ ?? keepZ);

    /// <summary>
    /// The wall spot for a bot waiting at <paramref name="anchor"/>: an archer's floor at least <see cref="MinimumRise"/>
    /// above the gate, within <see cref="MaxFromAnchor"/>, with nobody in <paramref name="taken"/> within
    /// <see cref="PersonalSpace"/>; among those the one <paramref name="roll"/> picks (in [0, 1)), so bots spread along the
    /// wall. -1 for none. Pure.
    /// </summary>
    public static int Choose(IReadOnlyList<Candidate> candidates, Vector2 anchor, IReadOnlyList<Vector2> taken, double roll)
    {
        if (candidates == null || candidates.Count == 0)
            return -1;
        Span<int> usable = candidates.Count <= 64 ? stackalloc int[candidates.Count] : new int[candidates.Count];
        int count = 0;
        for (int i = 0; i < candidates.Count; i++)
            if (Usable(candidates[i], anchor, taken))
                usable[count++] = i;
        if (count == 0)
            return -1;
        return usable[Math.Clamp((int)(Math.Clamp(roll, 0, 0.999999) * count), 0, count - 1)];
    }

    /// <summary>One candidate passes <see cref="Choose"/>'s tests. Pure.</summary>
    public static bool Usable(Candidate candidate, Vector2 anchor, IReadOnlyList<Vector2> taken)
    {
        if (!candidate.Archer || candidate.Rise < MinimumRise)
            return false;
        Vector2 spot = new(candidate.Floor.X, candidate.Floor.Y);
        if (Vector2.DistanceSquared(spot, anchor) > MaxFromAnchor * MaxFromAnchor)
            return false;
        if (taken != null)
            foreach (Vector2 other in taken)
                if (Vector2.DistanceSquared(spot, other) < PersonalSpace * PersonalSpace)
                    return false;
        return true;
    }

    /// <summary>The delay before a bot on the wall stands for an enemy it noticed; <paramref name="roll"/> in [0, 1).</summary>
    public static long StandDelay(double roll) =>
        StandMinimumMs + (long)(Math.Clamp(roll, 0, 0.999999) * (StandMaximumMs - StandMinimumMs));
}
