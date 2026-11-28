using System.Collections.Immutable;
using System.Numerics;

namespace DOL.GS;

/// <summary>The group's mood in an open-field fight (docs/RVR-FIELD-1.2.md, the stance table).</summary>
public enum RvrStance : byte
{
    /// <summary>An enemy group, or a fight between the other two realms, is in view: watch and size up.</summary>
    Observe,
    /// <summary>The odds look fair or better: run the fight plan. Today's behaviour, and the neutral plan's stance.</summary>
    Engage,
    /// <summary>Members are strung out or still arriving: stay together and wait.</summary>
    Hold,
    /// <summary>A bigger or melee-heavy enemy: fight while backing away.</summary>
    Kite,
    /// <summary>Bad odds or a lost fight: drop targets, sprint, use escapes.</summary>
    Retreat,
    /// <summary>After a retreat or a wipe: meet at the rally point, rest, re-form.</summary>
    Regroup,
}

/// <summary>One job in the plan: <paramref name="Member"/> works on <paramref name="Subject"/> since tick <paramref name="Since"/>.</summary>
public readonly record struct RvrAssignment(GameLiving Member, GameLiving Subject, long Since);

/// <summary>
/// One bot group's open-field fight plan (docs/RVR-FIELD-1.2.md layer 2): who is the main assist and what he called,
/// who controls, interrupts and protects whom, the stance and since when, the rally point and the group's skill tier.
/// Immutable: the builder makes the next plan from the one before it and the new board
/// (<see cref="RvrFightPlanner.Next"/>) and publishes it with the board, so a member reads one consistent plan.
/// Work package 0 makes only the neutral plan (no assist, no assignments, stance Engage); packages 2, 3 and 4 fill it.
/// </summary>
public sealed record RvrFightPlan
{
    public long BuiltAt { get; init; }
    /// <summary>The member whose target is the group's target; null: no assist train.</summary>
    public GameLiving MainAssist { get; init; }
    /// <summary>The called target; null: none.</summary>
    public GameLiving CalledTarget { get; init; }
    /// <summary>When <see cref="CalledTarget"/> was called (game-loop tick).</summary>
    public long CalledAt { get; init; }
    /// <summary>
    /// #599 step 2: <see cref="CalledTarget"/> is the battlegroup lead group's call, taken by this follower group's main
    /// assist (RvrAssist.Plan); false for the group's own call.
    /// </summary>
    public bool CallFromBattlegroup { get; init; }
    /// <summary>Member: the crowd controller; Subject: the enemy it holds.</summary>
    public ImmutableArray<RvrAssignment> CrowdControl { get; init; } = ImmutableArray<RvrAssignment>.Empty;
    /// <summary>Member: the interrupter; Subject: the enemy caster or healer it keeps from casting.</summary>
    public ImmutableArray<RvrAssignment> Interrupts { get; init; } = ImmutableArray<RvrAssignment>.Empty;
    /// <summary>Member: the guard; Subject: the group mate it protects.</summary>
    public ImmutableArray<RvrAssignment> Protect { get; init; } = ImmutableArray<RvrAssignment>.Empty;
    public RvrStance Stance { get; init; } = RvrStance.Engage;
    /// <summary>When the group took <see cref="Stance"/> (game-loop tick).</summary>
    public long StanceSince { get; init; }
    /// <summary>Why the group took <see cref="Stance"/> (the RVR_STANCE and RVR_REGROUP reason); empty for the neutral start.</summary>
    public string StanceReason { get; init; } = string.Empty;
    /// <summary>Starting <see cref="Stance"/> was a real reason to pass it on to the warband (RvrStanceDecision.Relays).</summary>
    public bool StanceRelays { get; init; }
    public Vector3? RallyPoint { get; init; }
    public ushort RallyRegion { get; init; }
    public RvrSkillTier SkillTier { get; init; } = RvrSkillTier.Average;
    /// <summary>#531 step 2a: when the group's present fight began (its first board in contact after a 20 s gap); 0: none yet.</summary>
    public long FightSince { get; init; }
    /// <summary>#531 step 2a: the last board in contact (game-loop tick); 0: none yet.</summary>
    public long LastContactAt { get; init; }
    /// <summary>#531 step 2a: when the opener window opened (the fight's first board the plan ran on); 0: not yet.</summary>
    public long OpenerSince { get; init; }
    /// <summary>#531 step 2a: the opener of this fight is spent (an area mez landed, or the window ran out).</summary>
    public bool OpenerDone { get; init; }
    /// <summary>#531 step 2a: the area mezzer who opens the fight (<see cref="RvrOpener"/>); null: no opener job.</summary>
    public GameLiving OpenerMember { get; init; }
    /// <summary>#531 step 2a: the enemy the opener's area mez is cast on.</summary>
    public GameLiving OpenerCentre { get; init; }

    /// <summary>No plan at all: what a board has before its first plan.</summary>
    public static RvrFightPlan None { get; } = new();

    /// <summary>The plan holds no decision: no assist, no call, no assignment, stance Engage. Bots under it fight as before.</summary>
    public bool IsNeutral => MainAssist == null && CalledTarget == null && CrowdControl.IsEmpty && Interrupts.IsEmpty &&
                             Protect.IsEmpty && Stance == RvrStance.Engage &&
                             OpenerMember == null;

    /// <summary>The neutral plan for a group with these stored facts. Pure.</summary>
    public static RvrFightPlan Neutral(long now, RvrGroupState state) => new()
    {
        BuiltAt = now,
        StanceSince = now,
        RallyPoint = state?.RallyPoint,
        RallyRegion = state?.RallyRegion ?? 0,
        SkillTier = state?.SkillTier ?? RvrSkillTier.Average,
    };

    /// <summary>What <paramref name="member"/> is assigned in <paramref name="jobs"/>, or null. Pure.</summary>
    public static GameLiving SubjectFor(ImmutableArray<RvrAssignment> jobs, GameLiving member)
    {
        if (member != null)
            foreach (RvrAssignment job in jobs)
                if (ReferenceEquals(job.Member, member))
                    return job.Subject;
        return null;
    }
}

/// <summary>
/// Makes a group's next fight plan from the plan before it, the new board and the group's stored facts. Pure: no
/// clock, no random number, no game access (the board is a snapshot). Each part of the plan has its own step, owned by
/// one work package, so the packages do not edit each other's code:
/// <list type="bullet">
/// <item><see cref="PlanStance"/>: work package 4 (rvr_bot_size_up, rvr_bot_disengage, rvr_bot_regroup)</item>
/// <item><see cref="PlanAssist"/>, <see cref="PlanCrowdControl"/>, <see cref="PlanInterrupts"/>: work package 2
/// (rvr_bot_assist, rvr_bot_cc_plan, rvr_bot_interrupts). Their rules are pure; the steps hand them the board's
/// <see cref="RvrFieldFacts"/>, which <see cref="RvrFieldReader"/> reads once per board from its live objects
/// (reachability reports, Mastery of Concentration, resurrection, crowd-control immunity, members' spells).</item>
/// <item><see cref="PlanOpener"/>: #531 step 2a, the AE mez opener (rvr_bot_cc_plan)</item>
/// <item><see cref="PlanProtect"/>: work package 3 (rvr_bot_peel)</item>
/// </list>
/// In work package 0 every step returns the plan unchanged, so the result is the neutral plan.
/// </summary>
public static class RvrFightPlanner
{
    public static RvrFightPlan Next(RvrFightPlan previous, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now)
    {
        state ??= RvrGroupState.Empty;
        if (!switches.FieldTactics || board == null)
            return RvrFightPlan.Neutral(now, state);
        RvrFightPlan plan = Carry(previous, state, now);
        plan = PlanStance(plan, board, state, switches, now);
        plan = PlanAssist(plan, board, state, switches, now);
        plan = PlanOpener(plan, board, state, switches, now);
        plan = PlanCrowdControl(plan, board, state, switches, now);
        plan = PlanInterrupts(plan, board, state, switches, now);
        plan = PlanProtect(plan, board, state, switches, now);
        return plan;
    }

    /// <summary>
    /// The plan the steps start from: the one before it (its call, assignments and stance carry over, so a step can hold
    /// a call for seconds and give the stance hysteresis) with the group's current facts; the neutral plan when there
    /// was none.
    /// </summary>
    public static RvrFightPlan Carry(RvrFightPlan previous, RvrGroupState state, long now)
    {
        if (previous == null || ReferenceEquals(previous, RvrFightPlan.None))
            return RvrFightPlan.Neutral(now, state);
        return previous with
        {
            BuiltAt = now,
            RallyPoint = state.RallyPoint,
            RallyRegion = state.RallyRegion,
            SkillTier = state.SkillTier ?? previous.SkillTier,
        };
    }

    /// <summary>Work package 4: the stance and a regroup's rally point (<see cref="RvrEngagement.PlanStance(RvrFightPlan, RvrFightBoard, RvrGroupState, RvrTacticsSwitches, long)"/>).</summary>
    public static RvrFightPlan PlanStance(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now) => RvrEngagement.PlanStance(plan, board, state, switches, now);

    /// <summary>Work package 2: the main assist and the called target (<see cref="RvrAssist.Plan"/>, on the board's facts).</summary>
    public static RvrFightPlan PlanAssist(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now) => RvrAssist.Plan(plan, board, switches, now, RvrFieldReader.Of(board, plan?.CalledTarget));

    /// <summary>#531 step 2a: the area mez that opens the fight (<see cref="RvrOpener.Plan"/>).</summary>
    public static RvrFightPlan PlanOpener(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now) => RvrOpener.Plan(plan, board, switches, now, RvrFieldReader.Of(board));

    /// <summary>Work package 2: who holds which enemy (<see cref="RvrCrowdControlPlan.Plan"/>).</summary>
    public static RvrFightPlan PlanCrowdControl(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now) => RvrCrowdControlPlan.Plan(plan, board, switches, now, RvrFieldReader.Of(board));

    /// <summary>Work package 2: who keeps which enemy caster or healer from casting (<see cref="RvrInterruptPlan.Plan"/>).</summary>
    public static RvrFightPlan PlanInterrupts(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now) => RvrInterruptPlan.Plan(plan, board, switches, now, RvrFieldReader.Of(board));

    /// <summary>Work package 3: who guards whom (<see cref="RvrPeelPlan.Protect"/>: one tank per healer, then casters).</summary>
    public static RvrFightPlan PlanProtect(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now) => RvrPeelPlan.Protect(plan, board, switches, now);
}
