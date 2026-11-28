using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The AE mez opener (#531 step 2a; docs/rvr-audit-1.2.3/mechanics.md "the mez that opens a fight is not planned as an
/// opener", reference.md 3.2). At a fight's first contact one of the group's area mezzers casts its area mez on the
/// enemy pile before anything else: "The opener is AE mez on the charge"; "the side that sees first and mezzes first
/// usually wins". The pile is mezzed whole, the kill target included: the train's first blow wakes only the one it
/// hits (any damage breaks a mez, the server's rule), and a mezzed call is dropped for a free one (RvrAssist), so the
/// group kills the one left standing while the rest sleep. After the opener (a mez landed on the pile, or the window
/// ran out) the crowd-control plan runs as before, and it never mezzes the kill target.
///
/// Pure: the plan step reads the board, its facts and the plan before it. Sourced: the opener itself, the 350 area mez
/// radius and the 1,500 range (reference.md 3.2). Our design values (no source measured them): the 8 s window, at least
/// two enemies caught, a healer caught counting half an enemy more (the 8v8 mez score in #572's evidence: +30 per enemy caught,
/// +15 on a healer), and two mezzed enemies meaning the opener landed.
/// </summary>
public static class RvrOpener
{
    /// <summary>The area mez radius the centre is chosen with (reference.md 3.2: "AE mez radius about 350").</summary>
    public const int Radius = 350;
    /// <summary>How long after the group's first planned board in a fight the opener may still be cast.</summary>
    public const long WindowMilliseconds = 8_000;
    /// <summary>The fewest free enemies a centre must catch (as BotBrain.PvpAreaMezWorthwhile).</summary>
    public const int MinimumCaught = 2;
    /// <summary>A caught healer counts this much more than another enemy.</summary>
    public const double HealerBonus = 0.5;
    /// <summary>This many mezzed enemy heads on the board: an area mez landed, the opener is spent.</summary>
    public const int LandedCount = 2;

    /// <summary>The plan with no opener job (the fight's timing is kept).</summary>
    public static RvrFightPlan Cleared(RvrFightPlan plan) =>
        plan.OpenerMember == null && plan.OpenerCentre == null ? plan : plan with { OpenerMember = null, OpenerCentre = null };

    /// <summary>
    /// The plan step (RvrFightPlanner.PlanOpener), after the assist step. A fight begins at the first board in contact
    /// after <see cref="RvrAssist.NewFightGapMilliseconds"/> without one; its opener window opens at the first board on
    /// which the plan runs (stance Engage or Kite, two members or more) and lasts <see cref="WindowMilliseconds"/>. While
    /// it is open and no area mez has landed, the opener job goes to an area mezzer who is not the main assist and not
    /// controlled (the one before while it still can), aimed at the free enemy whose radius catches the most free enemies
    /// (at least <see cref="MinimumCaught"/>). Pure.
    /// </summary>
    public static RvrFightPlan Plan(RvrFightPlan plan, RvrFightBoard board, RvrTacticsSwitches switches, long now,
        RvrFieldFacts facts)
    {
        if (plan == null)
            return null;
        facts ??= RvrFieldFacts.Empty;
        if (!switches.FieldTactics || !switches.CrowdControlPlan || board == null)
            return Cleared(plan);
        List<RvrCombatant> noticed = RvrAssist.Noticed(board);
        if (noticed.Count == 0)
            return Cleared(plan);

        bool newFight = plan.LastContactAt == 0 || now - plan.LastContactAt > RvrAssist.NewFightGapMilliseconds;
        plan = newFight
            ? plan with { FightSince = now, LastContactAt = now, OpenerSince = 0, OpenerDone = false }
            : plan with { LastContactAt = now };
        if (plan.OpenerDone)
            return Cleared(plan);
        if (!RvrAssist.StanceRunsPlan(plan.Stance) || RvrAssist.Heads(board).Count < 2)
            return Cleared(plan);
        if (plan.OpenerSince == 0)
            plan = plan with { OpenerSince = now };

        var free = new List<RvrCombatant>();
        int mezzed = 0;
        foreach (RvrCombatant enemy in noticed)
        {
            if (enemy.IsPet)
                continue;
            if ((enemy.Control & RvrControlState.Mezzed) != 0)
                mezzed++;
            else if (!enemy.IsControlled && !facts.Enemy(enemy).MezImmune && !facts.Enemy(enemy).CannotBeCalled)
                free.Add(enemy);
        }
        if (mezzed >= LandedCount || now - plan.OpenerSince > WindowMilliseconds)
            return Cleared(plan with { OpenerDone = true });

        RvrCombatant centre = Centre(free, board.FriendlyCentroid);
        if (centre == null)
            return Cleared(plan);
        RvrCombatant member = Mezzer(board, facts, plan, centre);
        if (member == null)
            return Cleared(plan);
        return plan with { OpenerMember = member.Living, OpenerCentre = centre.Living };
    }

    /// <summary>How many of <paramref name="free"/> an area mez on <paramref name="centre"/> catches, and its score. Pure.</summary>
    public static (int Caught, double Score) Catch(RvrCombatant centre, IReadOnlyList<RvrCombatant> free)
    {
        const float radius = (float)Radius * Radius;
        int caught = 0;
        double score = 0;
        foreach (RvrCombatant enemy in free)
        {
            float dx = enemy.X - centre.X, dy = enemy.Y - centre.Y;
            if (dx * dx + dy * dy > radius)
                continue;
            caught++;
            score += 1 + (RvrTargetPriority.KillRole(enemy) == RvrKillRole.Healer ? HealerBonus : 0);
        }
        return (caught, score);
    }

    /// <summary>
    /// The free enemy whose radius catches the most (by score; then nearer the group's centre, then the lower key), or
    /// null when none catches <see cref="MinimumCaught"/>. Pure.
    /// </summary>
    public static RvrCombatant Centre(IReadOnlyList<RvrCombatant> free, Vector3 friendlyCentroid)
    {
        RvrCombatant best = null;
        double bestScore = double.MinValue;
        float bestDistance = float.MaxValue;
        foreach (RvrCombatant enemy in free)
        {
            (int caught, double score) = Catch(enemy, free);
            if (caught < MinimumCaught)
                continue;
            float dx = enemy.X - friendlyCentroid.X, dy = enemy.Y - friendlyCentroid.Y;
            float distance = dx * dx + dy * dy;
            if (score > bestScore || score == bestScore && (distance < bestDistance ||
                                                           distance == bestDistance && enemy.Key < best.Key))
            {
                best = enemy;
                bestScore = score;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// The opener's caster: the one before while it is still an uncontrolled area mezzer and not the main assist, else
    /// the eligible one nearest the centre (then the lower key). Null when the group has no area mezzer free. Pure.
    /// </summary>
    public static RvrCombatant Mezzer(RvrFightBoard board, RvrFieldFacts facts, RvrFightPlan plan, RvrCombatant centre)
    {
        RvrCombatant best = null;
        float bestDistance = float.MaxValue;
        foreach (RvrCombatant member in RvrAssist.Heads(board))
        {
            RvrMemberFacts memberFacts = facts.Member(member);
            if (!memberFacts.AoeMez || memberFacts.ControlledSince != 0 || member.IsControlled ||
                ReferenceEquals(member.Living, plan.MainAssist))
                continue;
            if (plan.OpenerMember != null && ReferenceEquals(member.Living, plan.OpenerMember))
                return member;
            float dx = member.X - centre.X, dy = member.Y - centre.Y;
            float distance = dx * dx + dy * dy;
            if (distance < bestDistance || distance == bestDistance && best != null && member.Key < best.Key)
            {
                best = member;
                bestDistance = distance;
            }
        }
        return best;
    }
}
