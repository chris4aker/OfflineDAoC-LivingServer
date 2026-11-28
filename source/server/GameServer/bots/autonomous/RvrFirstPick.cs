using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>Why the first pick took its target (the RVR_DECIDE <c>pick=</c> value).</summary>
public enum RvrFirstPickReason : byte
{
    /// <summary>Nothing to pick.</summary>
    None,
    /// <summary>The group's called target (the assist train's call) was among the candidates.</summary>
    Called,
    /// <summary>Group mates already fight it: the bot joins the opener.</summary>
    Mates,
    /// <summary>A new opener, chosen by the kill order.</summary>
    Order,
    /// <summary>#599 step 2: the battlegroup lead group's called target, taken as the group's call.</summary>
    Battlegroup,
}

/// <summary>
/// One enemy the scan proposes for the first pick, as plain values. <see cref="Enemy"/> carries the class, realm,
/// health, whether it fights the group (<see cref="RvrCombatant.TargetsGroup"/>) and the key; the rest is what the pick
/// adds: a siege engine, held by crowd control (mezzed, breakably rooted or kept for a realm-mate's control), its
/// distance from the group's centre, how many group mates fight it now, and whether it is the group's called target.
/// </summary>
public readonly record struct RvrFirstPickCandidate(RvrCombatant Enemy, bool Engine, bool Held, float DistanceFromGroup,
    int MatesOn, bool Called);

/// <summary>
/// #531 step 1 (RvR work 13, piece D; docs/rvr-audit-1.2.3/structure.md finding 3): the plan owns the first pick. Before,
/// the threat scan spread a group over its nearest enemies by database id (AutonomousRvrStaging.TargetIndex), so an
/// eight opened on two or three targets and converged only when the assist's call came 4-8 s later ("every bot on a
/// different target", reference.md 3.4's fail). Now the scan proposes its contacts and this rule picks one target for
/// the whole group: the call if there is one, else the target mates already fight, else the kill order the call itself
/// uses (<see cref="RvrTargetPriority.Score"/>), so the first pick and the later call agree.
///
/// Sourced (reference.md 3.2, 3.4): one main assist's target for everyone (FH Nov 2003); the kill order (Jan 2003) and
/// its realm variants; a mezzed enemy is left alone (any damage breaks the mez); the first death is a caster or healer
/// most of the time. Our design values: the 2 per 100 units distance term (a design value: 500 units weigh about one
/// 10-point step of the kill order, so a much nearer enemy can come before a slightly better target; checked against an
/// 8v8 kill-target weighting, see #531) and the order of the classes (people, pets, engines; free, held; within the call
/// radius, beyond it).
/// Pure: no clock, no random number, no game access.
/// </summary>
public static class RvrFirstPick
{
    /// <summary>Enemies within this of the group's centre come before those beyond it (the assist's own call radius).</summary>
    public const int CallRadius = RvrAssist.CallRadius;
    /// <summary>What every 100 units from the group's centre takes off an enemy's score.</summary>
    public const double DistancePenaltyPer100 = 2;

    private const ulong PickSalt = 0x46495253; // "FIRS"

    /// <summary>
    /// The class an enemy falls in, lowest first: people before pets before siege engines (#617), free before held by
    /// crowd control (#600), within the call radius before beyond it. Only the lowest class present is picked from. Pure.
    /// </summary>
    public static int Class(in RvrFirstPickCandidate candidate)
    {
        int kind = candidate.Engine ? 2 : candidate.Enemy?.IsPet == true ? 1 : 0;
        return kind * 4 + (candidate.Held ? 2 : 0) + (candidate.DistanceFromGroup > CallRadius ? 1 : 0);
    }

    /// <summary>
    /// The kill-order score of one enemy for a new opener: the call's score (class and realm order, wounds, fighting
    /// us), times the tier's wobble rolled from the group's seed and the enemy (so every member of the group scores it
    /// the same), less <see cref="DistancePenaltyPer100"/> per 100 units from the group's centre. Pure.
    /// </summary>
    public static double Score(in RvrFirstPickCandidate candidate, RvrSkillTier tier, ulong groupSeed)
    {
        RvrCombatant enemy = candidate.Enemy;
        if (enemy == null)
            return double.MinValue;
        double score = RvrTargetPriority.Score(enemy, default) *
                       RvrTargetPriority.Jitter(tier, RvrAssist.Roll(groupSeed, enemy.Key, 0, PickSalt));
        return score - DistancePenaltyPer100 * Math.Max(0, candidate.DistanceFromGroup) / 100.0;
    }

    /// <summary>
    /// The index of the enemy to open on, or -1 when there is none. In the lowest <see cref="Class"/> present: the
    /// called target; else the one most group mates fight (ties by score); else the best <see cref="Score"/>. Ties
    /// fall to the lower key, so the pick does not depend on the order of the list. Pure.
    /// </summary>
    public static int Pick(IReadOnlyList<RvrFirstPickCandidate> candidates, RvrSkillTier tier, ulong groupSeed,
        out RvrFirstPickReason reason)
    {
        reason = RvrFirstPickReason.None;
        if (candidates == null || candidates.Count == 0)
            return -1;
        int lowest = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
            if (candidates[i].Enemy != null)
                lowest = Math.Min(lowest, Class(candidates[i]));
        if (lowest == int.MaxValue)
            return -1;

        int called = -1, mates = -1, best = -1;
        double matesScore = double.MinValue, bestScore = double.MinValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            RvrFirstPickCandidate candidate = candidates[i];
            if (candidate.Enemy == null || Class(candidate) != lowest)
                continue;
            if (candidate.Called && (called < 0 || candidate.Enemy.Key < candidates[called].Enemy.Key))
                called = i;
            double score = Score(candidate, tier, groupSeed);
            if (candidate.MatesOn > 0 && (mates < 0 || Better(candidate.MatesOn, score, candidate.Enemy.Key,
                    candidates[mates].MatesOn, matesScore, candidates[mates].Enemy.Key)))
            {
                mates = i;
                matesScore = score;
            }
            if (best < 0 || Better(0, score, candidate.Enemy.Key, 0, bestScore, candidates[best].Enemy.Key))
            {
                best = i;
                bestScore = score;
            }
        }
        if (called >= 0)
        {
            reason = RvrFirstPickReason.Called;
            return called;
        }
        if (mates >= 0)
        {
            reason = RvrFirstPickReason.Mates;
            return mates;
        }
        reason = RvrFirstPickReason.Order;
        return best;
    }

    private static bool Better(int count, double score, long key, int bestCount, double bestScore, long bestKey) =>
        count != bestCount ? count > bestCount : score != bestScore ? score > bestScore : key < bestKey;

    /// <summary>The RVR_DECIDE value of a reason.</summary>
    public static string Label(RvrFirstPickReason reason) => reason switch
    {
        RvrFirstPickReason.Called => "called",
        RvrFirstPickReason.Mates => "mates",
        RvrFirstPickReason.Order => "order",
        RvrFirstPickReason.Battlegroup => "battlegroup",
        _ => "none",
    };
}
