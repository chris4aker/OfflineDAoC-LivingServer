using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace DOL.GS;

/// <summary>What a crowd controller puts on its subject.</summary>
public enum RvrControlKind : byte
{
    None,
    Mez,
    Stun,
    Root,
}

/// <summary>
/// The group's crowd-control plan (work package 2, docs/RVR-FIELD-1.2.md "Mind" and "Hands"; ERA-RESEARCH.md section 3).
/// The group's mezzers, stunners and rooters each get one enemy that is not the kill target: the kill target is never
/// mezzed; the mezzer opens on the charge (its job exists from first contact, and it casts as soon as it is in range);
/// a resisted mez is cast again (the job stands while the subject is free and not immune); the 1.65 RvR immunity is
/// the server's own (an immunity effect after a mez, stun or root ends; for bots the NPC diminishing immunity): a
/// subject the server would refuse is not assigned for that kind. Pure, apart from the immunity readers at the end,
/// which ask the same effects as the server's spell handlers.
///
/// Human errors (our design values): a crowd-controlled crowd controller keeps its job, uncovered, for 2-4 s by tier
/// before another may take its subject; and a bot's area damage sometimes breaks a mez
/// (<see cref="AreaDamageBreaksMez"/>): "average ones use AEO to break mezz" (FH Jan 2003, satire).
/// </summary>
public static class RvrCrowdControlPlan
{
    private const ulong ReplaceSalt = 0x5245504c;
    private const ulong BreakSalt = 0x42524b4d;

    /// <summary>
    /// How long a mezzed or stunned crowd controller keeps its job before another member may take its subject:
    /// Veteran 2-2.8 s, Average 2.5-3.5 s, Casual 3.2-4 s ("a mezzed mezzer is not covered for 2-4 s").
    /// </summary>
    public static (long Min, long Max) ReplaceDelayRange(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => (2_000, 2_800),
        RvrSkillTier.Casual => (3_200, 4_000),
        _ => (2_500, 3_500),
    };

    public static long ReplaceDelay(RvrSkillTier tier, long memberKey, long controlledSince)
    {
        (long min, long max) = ReplaceDelayRange(tier);
        return min + (long)Math.Round(RvrAssist.Roll((ulong)memberKey, controlledSince, 3, ReplaceSalt) * (max - min));
    }

    /// <summary>The chance that a bot's area damage spell ignores a mez it would break: Casual 25%, Average 12%, Veteran 4%.</summary>
    public static double AreaBreakShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.25,
        RvrSkillTier.Veteran => 0.04,
        _ => 0.12,
    };

    /// <summary>
    /// Pure: whether bot <paramref name="casterKey"/> lets its area damage reach the mezzed <paramref name="subjectKey"/>
    /// in the 10-second window of <paramref name="now"/> (one roll per caster, subject and window, so a bot does not
    /// flip its mind every think).
    /// </summary>
    public static bool AreaDamageBreaksMez(RvrSkillTier tier, long casterKey, long subjectKey, long now) =>
        RvrAssist.Roll((ulong)casterKey, subjectKey, now / 10_000, BreakSalt) < AreaBreakShare(tier);

    /// <summary>Pure: an area mez centred on <paramref name="centre"/> with <paramref name="radius"/> leaves the kill target out.</summary>
    public static bool AreaSpareKillTarget(Vector3 centre, int radius, Vector3? killTarget) =>
        killTarget is not { } kill || Vector3.DistanceSquared(new Vector3(centre.X, centre.Y, 0), new Vector3(kill.X, kill.Y, 0)) >
        (float)(radius + 50) * (radius + 50);

    /// <summary>The kinds a member can apply to an enemy with these facts, best first: mez, stun, root. Pure.</summary>
    public static RvrControlKind BestKind(in RvrMemberFacts member, in RvrEnemyFacts enemy)
    {
        if (member.CanMez && !enemy.MezImmune)
            return RvrControlKind.Mez;
        if (member.CanStun && !enemy.StunImmune)
            return RvrControlKind.Stun;
        if (member.CanRoot && !enemy.RootImmune)
            return RvrControlKind.Root;
        return RvrControlKind.None;
    }

    private static bool HeldBy(RvrCombatant enemy) => (enemy.Control & (RvrControlState.Mezzed | RvrControlState.Stunned)) != 0;

    /// <summary>
    /// The plan step (RvrFightPlanner.PlanCrowdControl). Jobs that still stand are kept (and keep their start); a free
    /// crowd controller then takes the best free subject for its best kind. A subject already held by someone outside
    /// the plan is left alone. Pure.
    /// </summary>
    public static RvrFightPlan Plan(RvrFightPlan plan, RvrFightBoard board, RvrTacticsSwitches switches, long now,
        RvrFieldFacts facts)
    {
        if (plan == null)
            return null;
        facts ??= RvrFieldFacts.Empty;
        if (!switches.FieldTactics || !switches.CrowdControlPlan || board == null || !RvrAssist.StanceRunsPlan(plan.Stance) ||
            RvrAssist.Heads(board).Count < 2)
            return plan.CrowdControl.IsEmpty ? plan : plan with { CrowdControl = ImmutableArray<RvrAssignment>.Empty };

        List<RvrCombatant> noticed = RvrAssist.Noticed(board);
        var subjects = new List<RvrCombatant>();
        foreach (RvrCombatant enemy in noticed)
            if (!enemy.IsPet && !ReferenceEquals(enemy.Living, plan.CalledTarget))
                subjects.Add(enemy);
        var controllers = new List<RvrCombatant>();
        foreach (RvrCombatant member in RvrAssist.Heads(board))
            if (facts.Member(member).CanControl && !ReferenceEquals(member.Living, plan.MainAssist))
                controllers.Add(member);
        if (subjects.Count == 0 || controllers.Count == 0)
            return plan.CrowdControl.IsEmpty ? plan : plan with { CrowdControl = ImmutableArray<RvrAssignment>.Empty };

        var jobs = ImmutableArray.CreateBuilder<RvrAssignment>();
        var busyMembers = new HashSet<GameLiving>(ReferenceEqualityComparer.Instance);
        var takenSubjects = new HashSet<GameLiving>(ReferenceEqualityComparer.Instance);
        RvrSkillTier tier = plan.SkillTier;

        // 1. Jobs that still stand.
        foreach (RvrAssignment job in plan.CrowdControl)
        {
            RvrCombatant member = controllers.Find(m => ReferenceEquals(m.Living, job.Member));
            RvrCombatant subject = subjects.Find(e => ReferenceEquals(e.Living, job.Subject));
            if (member == null || subject == null || takenSubjects.Contains(subject.Living))
                continue;
            RvrMemberFacts memberFacts = facts.Member(member);
            // A controlled controller keeps its job, uncovered, until the tier's delay has passed.
            if (memberFacts.ControlledSince != 0 && now - memberFacts.ControlledSince >= ReplaceDelay(tier, member.Key, memberFacts.ControlledSince))
                continue;
            // Held (by this job): kept. Free: kept while the member can still apply something (a resist is cast again).
            if (!HeldBy(subject) && BestKind(memberFacts, facts.Enemy(subject)) == RvrControlKind.None)
                continue;
            jobs.Add(job);
            busyMembers.Add(member.Living);
            takenSubjects.Add(subject.Living);
        }

        // 2. Free controllers, mezzers first (area mezzers before single), each takes the best free subject.
        controllers.Sort((a, b) => Rank(facts.Member(a)).CompareTo(Rank(facts.Member(b))));
        foreach (RvrCombatant member in controllers)
        {
            RvrMemberFacts memberFacts = facts.Member(member);
            if (busyMembers.Contains(member.Living) || memberFacts.ControlledSince != 0)
                continue;
            RvrCombatant best = null;
            double bestScore = double.MinValue;
            foreach (RvrCombatant subject in subjects)
            {
                // Held by someone outside the plan (another group's mezzer): left alone.
                if (takenSubjects.Contains(subject.Living) || HeldBy(subject))
                    continue;
                RvrControlKind kind = BestKind(memberFacts, facts.Enemy(subject));
                if (kind == RvrControlKind.None)
                    continue;
                double score = kind == RvrControlKind.Root ? RvrTargetPriority.RootScore(subject) : RvrTargetPriority.HoldScore(subject);
                if (score > bestScore)
                {
                    best = subject;
                    bestScore = score;
                }
            }
            if (best == null)
                continue;
            jobs.Add(new RvrAssignment(member.Living, best.Living, now));
            busyMembers.Add(member.Living);
            takenSubjects.Add(best.Living);
        }
        return plan with { CrowdControl = jobs.ToImmutable() };
    }

    private static int Rank(in RvrMemberFacts facts) => facts.AoeMez ? 0 : facts.CanMez ? 1 : facts.CanStun ? 2 : 3;

    // ---- the server's immunity rules, read as its spell handlers read them --------------------------------------------

    /// <summary>
    /// The server would refuse a mez of <paramref name="duration"/> ms (MesmerizeSpellHandler.CheckSpellResist): a mez
    /// immunity effect or ability, a bot's NPC immunity that refuses a new mez, or a bot below
    /// 75% health ("enraged"). Not pure (reads effects).
    /// </summary>
    public static bool MezImmune(GameLiving target, int duration)
    {
        if (target?.effectListComponent == null)
            return false;
        if (target.effectListComponent.ContainsEffectForEffectType(eEffect.MezImmunity) || target.HasAbility(Abilities.MezzImmunity))
            return true;
        // A player already mezzed is refused too, but that one is held, not free: the plan does not ask.
        if (target is GamePlayer)
            return false;
        if (EffectListService.GetEffectOnTarget(target, eEffect.NPCMezImmunity) is NpcMezImmunityEffect immunity &&
            !immunity.CanApplyNewEffect(Math.Max(1, duration)))
            return true;
        return target is GameNPC && target.HealthPercent < 75;
    }

    /// <summary>The server would refuse a stun of <paramref name="duration"/> ms (StunSpellHandler.CheckSpellResist). Not pure.</summary>
    public static bool StunImmune(GameLiving target, int duration)
    {
        if (target?.effectListComponent == null)
            return false;
        if (target.effectListComponent.ContainsEffectForEffectType(eEffect.StunImmunity) || target.HasAbility(Abilities.StunImmunity))
            return true;
        if (target is GamePlayer)
            return false;
        return EffectListService.GetEffectOnTarget(target, eEffect.NPCStunImmunity) is NpcStunImmunityEffect immunity &&
            !immunity.CanApplyNewEffect(Math.Max(1, duration));
    }

    /// <summary>The server would refuse a root (SpeedDecreaseSpellHandler.ApplyEffectOnTarget). Not pure.</summary>
    public static bool RootImmune(GameLiving target)
    {
        if (target?.effectListComponent == null)
            return false;
        return target.HasAbility(Abilities.CCImmunity) || target.HasAbility(Abilities.RootImmunity) ||
            target.effectListComponent.ContainsEffectForEffectType(eEffect.SnareImmunity) ||
            target.effectListComponent.ContainsEffectForEffectType(eEffect.SpeedOfSound);
    }
}
