using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// The assist train (work package 2, docs/RVR-FIELD-1.2.md "Mind" and "Hands"; ERA-RESEARCH.md section 2): one fighter
/// is the main assist and his target is the group's; the others switch to it after their own delay, and some tunnel.
/// Pure: the plan step works on the board, its facts (<see cref="RvrFieldFacts"/>) and the plan before it; the member
/// rules on numbers. Rolls are hashes of the group, the enemy and the time, so a run is the same on every replay and
/// the shares can be tested over many seeds.
///
/// Sourced: "Nominate a tank from your group as the main assist" (FH Nov 2003); the kill order; switching to a caster
/// seen using Mastery of Concentration and to an enemy just resurrected (FH Aug 2003). Our design values (no source
/// measured them): the 4-8 s hold, the follow delays and tunnel shares per tier, the 20% wobble, the 5-15% tank calls,
/// the 1,500 noticing radius around an enemy in contact (layer 1's group rule) and the per-second notice chance of a
/// rising enemy.
/// </summary>
public static class RvrAssist
{
    /// <summary>Noticing one enemy means noticing its group mates within this radius (docs, layer 1).</summary>
    public const int NoticeRadius = 1_500;
    /// <summary>No call on an enemy farther than this from the group's centre: chasing is for the bold, not the plan.</summary>
    public const int CallRadius = 2_500;
    public const long MinHoldMilliseconds = 4_000;
    public const long MaxHoldMilliseconds = 8_000;
    /// <summary>A call after this long without one starts a new fight (a new roll for who tunnels).</summary>
    public const long NewFightGapMilliseconds = 20_000;
    /// <summary>The aggro weight of the called target (a protection target gets 10,000: it stays above the call).</summary>
    public const long CalledAggro = 8_000;

    private const ulong CallSalt = 0x41535349; // "ASSI"
    private const ulong DelaySalt = 0x44454c41;
    private const ulong TunnelSalt = 0x54554e4e;
    private const ulong WrongSalt = 0x57524f4e;
    private const ulong HoldSalt = 0x484f4c44;
    private const ulong RiseSalt = 0x52495345;

    /// <summary>Whether a stance runs the fight plan: Engage and Kite do; Observe, Hold, Retreat and Regroup call nothing.</summary>
    public static bool StanceRunsPlan(RvrStance stance) => stance is RvrStance.Engage or RvrStance.Kite;

    /// <summary>A 0-1 roll from a seed and two numbers. Pure.</summary>
    public static double Roll(ulong seed, long a, long b, ulong salt)
    {
        unchecked
        {
            ulong mixed = AutonomousBotPersonality.Mix(a, seed ^ salt);
            mixed = AutonomousBotPersonality.Mix(b, mixed);
            return (mixed >> 11) * (1.0 / (1UL << 53));
        }
    }

    /// <summary>The group's seed: the key of its first member with a live object (0 when none).</summary>
    public static ulong GroupSeed(RvrFightBoard board)
    {
        if (board != null)
            foreach (RvrCombatant member in board.Members)
                if (member?.Living != null && !member.IsPet)
                    return (ulong)member.Key;
        return 0;
    }

    /// <summary>Living members who are not pets.</summary>
    public static List<RvrCombatant> Heads(RvrFightBoard board)
    {
        var heads = new List<RvrCombatant>(8);
        if (board != null)
            foreach (RvrCombatant member in board.Members)
                if (member?.Living != null && !member.IsPet)
                    heads.Add(member);
        return heads;
    }

    private static float DistanceSquared(RvrCombatant a, RvrCombatant b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// The enemies the group knows about: those in contact (targeting the group or targeted by it) and their mates within
    /// <see cref="NoticeRadius"/>. The board lists everything in 3,500 units whether noticed or not, so nothing outside
    /// this set may be called, controlled or interrupted (principle 2: one perception rule). Pure.
    /// </summary>
    public static List<RvrCombatant> Noticed(RvrFightBoard board)
    {
        var noticed = new List<RvrCombatant>();
        if (board == null)
            return noticed;
        var contact = new List<RvrCombatant>();
        foreach (RvrCombatant enemy in board.Enemies)
            if (enemy != null && (enemy.TargetsGroup || enemy.TargetedByGroup))
                contact.Add(enemy);
        if (contact.Count == 0)
            return noticed;
        const float radius = (float)NoticeRadius * NoticeRadius;
        foreach (RvrCombatant enemy in board.Enemies)
        {
            if (enemy?.Living == null)
                continue;
            bool known = enemy.TargetsGroup || enemy.TargetedByGroup;
            for (int i = 0; !known && i < contact.Count; i++)
                known = DistanceSquared(enemy, contact[i]) <= radius;
            if (known)
                noticed.Add(enemy);
        }
        return noticed;
    }

    /// <summary>
    /// The enemies that may be called: noticed, within <see cref="CallRadius"/> of the group's centre, not unreachable or
    /// on a wall, not mesmerized (unless every one left is), and heads before pets (a pet only when no head is left). Pure.
    /// </summary>
    public static List<RvrCombatant> Candidates(RvrFightBoard board, RvrFieldFacts facts)
    {
        facts ??= RvrFieldFacts.Empty;
        var heads = new List<RvrCombatant>();
        var mezzed = new List<RvrCombatant>();
        var pets = new List<RvrCombatant>();
        if (board == null)
            return heads;
        const float radius = (float)CallRadius * CallRadius;
        foreach (RvrCombatant enemy in Noticed(board))
        {
            if (facts.Enemy(enemy).CannotBeCalled)
                continue;
            float dx = enemy.X - board.FriendlyCentroid.X, dy = enemy.Y - board.FriendlyCentroid.Y;
            if (dx * dx + dy * dy > radius)
                continue;
            if ((enemy.Control & RvrControlState.Mezzed) != 0)
                mezzed.Add(enemy);
            else if (enemy.IsPet)
                pets.Add(enemy);
            else
                heads.Add(enemy);
        }
        // Owner 2026-10-27 (BotPvpCrowdControl): a mesmerized enemy is left alone while another is free to fight.
        if (heads.Count > 0)
            return heads;
        return pets.Count > 0 ? pets : mezzed;
    }

    /// <summary>
    /// The main assist: the one before while he is still in the group, else a tank (the group's leader first among
    /// tanks, then the highest level), else the leader if he fights in melee, else a melee fighter, else an archer or
    /// stealther, else the leader, else the first member. "Nominate a tank from your group as the main assist" (FH Nov 2003). Pure.
    /// </summary>
    public static RvrCombatant ChooseMainAssist(RvrFightBoard board, RvrFieldFacts facts, GameLiving previous)
    {
        facts ??= RvrFieldFacts.Empty;
        List<RvrCombatant> heads = Heads(board);
        if (previous != null)
            foreach (RvrCombatant member in heads)
                if (ReferenceEquals(member.Living, previous))
                    return member;
        RvrCombatant best = null;
        int bestRank = int.MaxValue;
        foreach (RvrCombatant member in heads)
        {
            bool leader = facts.Member(member).IsLeader;
            int rank = member.Role switch
            {
                RvrRole.Tank => leader ? 0 : 1,
                RvrRole.Melee => leader ? 2 : 3,
                RvrRole.Archer or RvrRole.Stealther => 4,
                _ => leader ? 5 : 6,
            };
            if (rank < bestRank || rank == bestRank && best != null && member.Level > best.Level)
            {
                best = member;
                bestRank = rank;
            }
        }
        return best;
    }

    /// <summary>How long a call made at <paramref name="calledAt"/> is held: 4 to 8 s. Pure.</summary>
    public static long HoldMilliseconds(ulong seed, long calledAt) =>
        MinHoldMilliseconds + (long)Math.Round(Roll(seed, calledAt, 0, HoldSalt) * (MaxHoldMilliseconds - MinHoldMilliseconds));

    /// <summary>The caller's score of an enemy for the call made at <paramref name="calledAt"/>: the kill order with the tier's wobble. Pure.</summary>
    public static double CallScore(RvrCombatant enemy, in RvrEnemyFacts facts, RvrSkillTier tier, ulong seed, long calledAt) =>
        RvrTargetPriority.Score(enemy, facts) * RvrTargetPriority.Jitter(tier, Roll(seed, enemy?.Key ?? 0, calledAt, CallSalt));

    /// <summary>
    /// The chance per board (about once a second) that the caller notices a rising enemy: Veteran 90%, Average 75%,
    /// Casual 60%. A rising enemy is noticed within a second or two in most groups, and later in the casual ones.
    /// </summary>
    public static double RiseNoticeChance(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => 0.90,
        RvrSkillTier.Casual => 0.60,
        _ => 0.75,
    };

    /// <summary>The plan with no assist train.</summary>
    public static RvrFightPlan Cleared(RvrFightPlan plan) =>
        plan.MainAssist == null && plan.CalledTarget == null ? plan : plan with { MainAssist = null, CalledTarget = null, CalledAt = 0 };

    /// <summary>
    /// The plan step (RvrFightPlanner.PlanAssist): the main assist and the called target. The call is held 4-8 s and
    /// dropped early when the target dies or leaves the board, becomes unreachable or goes up a wall, or is mezzed by
    /// someone else; a rising enemy (Mastery of Concentration, just resurrected) takes the call once the caller notices
    /// it. A new call goes to the best score by the kill order with the tier's wobble, or, in 5-15% of calls, to a tank.
    /// Pure.
    /// </summary>
    public static RvrFightPlan Plan(RvrFightPlan plan, RvrFightBoard board, RvrTacticsSwitches switches, long now,
        RvrFieldFacts facts)
    {
        if (plan == null)
            return null;
        facts ??= RvrFieldFacts.Empty;
        if (!switches.FieldTactics || !switches.Assist || board == null || !StanceRunsPlan(plan.Stance))
            return Cleared(plan);
        if (Heads(board).Count < 2)
            return Cleared(plan);
        List<RvrCombatant> candidates = Candidates(board, facts);
        if (candidates.Count == 0)
            return Cleared(plan);
        RvrCombatant mainAssist = ChooseMainAssist(board, facts, plan.MainAssist);
        RvrSkillTier tier = plan.SkillTier;
        ulong seed = GroupSeed(board);

        // A rising enemy the caller noticed on this board.
        RvrCombatant rising = null;
        double risingScore = double.MinValue;
        foreach (RvrCombatant enemy in candidates)
        {
            RvrEnemyFacts enemyFacts = facts.Enemy(enemy);
            if (!enemyFacts.Rising || Roll(seed, enemy.Key, now / 1_000, RiseSalt) >= RiseNoticeChance(tier))
                continue;
            double score = RvrTargetPriority.Score(enemy, enemyFacts);
            if (score > risingScore)
            {
                rising = enemy;
                risingScore = score;
            }
        }

        RvrCombatant held = null;
        if (plan.CalledTarget != null)
            foreach (RvrCombatant enemy in candidates)
                if (ReferenceEquals(enemy.Living, plan.CalledTarget))
                {
                    held = enemy;
                    break;
                }
        bool holding = held != null && (held.Control & RvrControlState.Mezzed) == 0 &&
                       now - plan.CalledAt < HoldMilliseconds(seed, plan.CalledAt);
        if (holding && (rising == null || ReferenceEquals(rising, held)))
            return plan with { MainAssist = mainAssist.Living };
        if (rising != null)
            return plan with { MainAssist = mainAssist.Living, CalledTarget = rising.Living, CalledAt = now };

        RvrCombatant pick = null;
        if (WrongCall(tier, seed, now))
            pick = Best(candidates, facts, tier, seed, now, tanksOnly: true);
        pick ??= Best(candidates, facts, tier, seed, now, tanksOnly: false);
        return plan with { MainAssist = mainAssist.Living, CalledTarget = pick.Living, CalledAt = now };
    }

    /// <summary>Whether the call a group makes at <paramref name="now"/> is one of the tier's calls on a tank. Pure.</summary>
    public static bool WrongCall(RvrSkillTier tier, ulong seed, long now) =>
        Roll(seed, now, 0, WrongSalt) < RvrTargetPriority.WrongCallShare(tier);

    private static RvrCombatant Best(List<RvrCombatant> candidates, RvrFieldFacts facts, RvrSkillTier tier, ulong seed,
        long now, bool tanksOnly)
    {
        RvrCombatant best = null;
        double bestScore = double.MinValue;
        foreach (RvrCombatant enemy in candidates)
        {
            if (tanksOnly && RvrTargetPriority.KillRole(enemy) != RvrKillRole.Tank)
                continue;
            double score = CallScore(enemy, facts.Enemy(enemy), tier, seed, now);
            if (score > bestScore)
            {
                best = enemy;
                bestScore = score;
            }
        }
        return best;
    }

    // ---- the members: delay and tunnel --------------------------------------------------------------------------------

    /// <summary>The delay to follow a call, by tier (the design's table): Casual 1-4 s, Average 0.6-2.5 s, Veteran 0.3-1.5 s.</summary>
    public static (long Min, long Max) FollowDelayRange(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => (1_000, 4_000),
        RvrSkillTier.Veteran => (300, 1_500),
        _ => (600, 2_500),
    };

    /// <summary>The share of members who ignore the call for a whole fight, by tier: Casual 40%, Average 25%, Veteran 10%.</summary>
    public static double IgnoreShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.40,
        RvrSkillTier.Veteran => 0.10,
        _ => 0.25,
    };

    /// <summary>
    /// The tunnel share for one member: the tier's share scaled by boldness, 0.6x for the most careful (0) to 1.4x for a
    /// Daredevil (1); the 0.5 of a bot without a personality gives the tier's share exactly. Pure.
    /// </summary>
    public static double TunnelShare(RvrSkillTier tier, double boldness) =>
        Math.Clamp(IgnoreShare(tier) * (0.6 + 0.8 * Math.Clamp(boldness, 0, 1)), 0, 1);

    /// <summary>The delay of member <paramref name="memberId"/> to follow the call made at <paramref name="calledAt"/>. Pure.</summary>
    public static long FollowDelay(RvrSkillTier tier, long memberId, long calledAt)
    {
        (long min, long max) = FollowDelayRange(tier);
        return min + (long)Math.Round(Roll((ulong)memberId, calledAt, 1, DelaySalt) * (max - min));
    }

    /// <summary>Whether member <paramref name="memberId"/> ignores the calls of the fight that began at <paramref name="fightId"/>. Pure.</summary>
    public static bool Tunnels(RvrSkillTier tier, long memberId, long fightId, double boldness) =>
        Roll((ulong)memberId, fightId, 2, TunnelSalt) < TunnelShare(tier, boldness);

    /// <summary>
    /// Pure: the fight a call belongs to. A call within <see cref="NewFightGapMilliseconds"/> of the last call this member
    /// saw belongs to the same fight; otherwise it starts a new one, keyed by its time.
    /// </summary>
    public static long FightId(long previousFightId, long lastCallSeenAt, long calledAt) =>
        previousFightId != 0 && calledAt - lastCallSeenAt <= NewFightGapMilliseconds ? previousFightId : Math.Max(1, calledAt);

    // ---- the log lines ------------------------------------------------------------------------------------------------

    /// <summary>Pure: a group logs a call when it is a new target and its last call line is at least 2 s old.</summary>
    public static bool ShouldLogCall(GameLiving logged, GameLiving called, long now, long lastLogAt) =>
        called != null && !ReferenceEquals(logged, called) && now - lastLogAt >= 2_000;
}
