using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace DOL.GS;

/// <summary>How an interrupter keeps its caster busy, best first.</summary>
public enum RvrInterruptMeans : byte
{
    None,
    /// <summary>An archer's arrows.</summary>
    Ranged,
    /// <summary>An instant harmful spell.</summary>
    InstantSpell,
    /// <summary>The bot's pet (it goes where its owner fights).</summary>
    Pet,
    /// <summary>A harmful spell of at most <see cref="RvrInterruptPlan.FastCastMilliseconds"/>.</summary>
    FastSpell,
    /// <summary>A melee fighter already within <see cref="RvrInterruptPlan.MeleeReach"/> of the caster.</summary>
    Melee,
}

/// <summary>
/// Interrupt duty (work package 2, docs/RVR-FIELD-1.2.md "Mind" and "Hands"; ERA-RESEARCH.md section 4). Each enemy
/// healer or caster that is not the kill target gets one bot that keeps it from casting: an archer, a caster with an
/// instant or fast spell, a pet class, or a melee fighter already in reach. The interrupt itself is the server's own
/// (any hit, miss or blocked hit on the caster interrupts it; the 1.65 interrupt values are kept, owner decision 9).
///
/// THIS IS OUR DESIGN, NOT A SOURCED 2003 ROTATION: no era source describes an interrupt rotation (the research found
/// only who was valued as an interrupter: archers on casters, Theurgist pets, Minstrel "front line rupt", Champion and
/// Warden). The means order, the cap of half the group, the fast-spell limit and the 1.5-3 s look are our values.
/// Pure.
/// </summary>
public static class RvrInterruptPlan
{
    /// <summary>A spell this fast still lands inside a typical cast (our value).</summary>
    public const int FastCastMilliseconds = 2_000;
    /// <summary>A melee fighter this close to an enemy caster may take it (our value).</summary>
    public const int MeleeReach = 400;

    private const ulong LookSalt = 0x4c4f4f4b;

    /// <summary>How often an interrupter looks again (its subject's casting, its job): Veteran 1.5-2 s, Average 2-2.5 s, Casual 2.5-3 s.</summary>
    public static (long Min, long Max) LookRange(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => (1_500, 2_000),
        RvrSkillTier.Casual => (2_500, 3_000),
        _ => (2_000, 2_500),
    };

    public static long LookMilliseconds(RvrSkillTier tier, long memberKey, long now)
    {
        (long min, long max) = LookRange(tier);
        return min + (long)Math.Round(RvrAssist.Roll((ulong)memberKey, now, 4, LookSalt) * (max - min));
    }

    /// <summary>At most this many members are on interrupt duty: half the group's heads, so the train stays.</summary>
    public static int Cap(int heads) => Math.Max(1, heads / 2);

    /// <summary>The best means a member has against a caster (none for a support-only bot, a healer or the main assist). Pure.</summary>
    public static RvrInterruptMeans Means(RvrCombatant member, in RvrMemberFacts facts, RvrCombatant subject)
    {
        if (member == null || subject == null || facts.SupportOnly || member.Role == RvrRole.Healer)
            return RvrInterruptMeans.None;
        if (facts.Ranged)
            return RvrInterruptMeans.Ranged;
        if (facts.InstantSpell)
            return RvrInterruptMeans.InstantSpell;
        if (facts.HasPet)
            return RvrInterruptMeans.Pet;
        if (facts.FastSpell)
            return RvrInterruptMeans.FastSpell;
        if (member.Role is RvrRole.Tank or RvrRole.Melee or RvrRole.Stealther)
        {
            float dx = member.X - subject.X, dy = member.Y - subject.Y;
            if (dx * dx + dy * dy <= (float)MeleeReach * MeleeReach)
                return RvrInterruptMeans.Melee;
        }
        return RvrInterruptMeans.None;
    }

    /// <summary>An enemy that needs an interrupter: a noticed spellcaster, not a pet, not the kill target, not held by crowd control or a job of it.</summary>
    private static bool NeedsInterrupter(RvrCombatant enemy, RvrFightPlan plan)
    {
        if (enemy.IsPet || ReferenceEquals(enemy.Living, plan.CalledTarget) ||
            !RvrTargetPriority.IsSpellcaster(enemy.Class, enemy.Role) ||
            (enemy.Control & (RvrControlState.Mezzed | RvrControlState.Stunned)) != 0)
            return false;
        foreach (RvrAssignment job in plan.CrowdControl)
            if (ReferenceEquals(job.Subject, enemy.Living))
                return false;
        return true;
    }

    /// <summary>
    /// The plan step (RvrFightPlanner.PlanInterrupts), run after the crowd-control step. A job stands while its member
    /// and caster still qualify; free casters are then taken in kill order (healers first: "If a healer purges then keep
    /// it interrupted somehow", FH Aug 2003), each by the free member with the best means (then the nearest). One
    /// interrupter per caster, at most half the group. Pure.
    /// </summary>
    public static RvrFightPlan Plan(RvrFightPlan plan, RvrFightBoard board, RvrTacticsSwitches switches, long now,
        RvrFieldFacts facts)
    {
        if (plan == null)
            return null;
        facts ??= RvrFieldFacts.Empty;
        List<RvrCombatant> heads = RvrAssist.Heads(board);
        if (!switches.FieldTactics || !switches.Interrupts || board == null || !RvrAssist.StanceRunsPlan(plan.Stance) || heads.Count < 2)
            return plan.Interrupts.IsEmpty ? plan : plan with { Interrupts = ImmutableArray<RvrAssignment>.Empty };

        var casters = new List<RvrCombatant>();
        foreach (RvrCombatant enemy in RvrAssist.Noticed(board))
            if (NeedsInterrupter(enemy, plan))
                casters.Add(enemy);
        var crowdControl = new HashSet<GameLiving>(ReferenceEqualityComparer.Instance);
        foreach (RvrAssignment job in plan.CrowdControl)
            crowdControl.Add(job.Member);
        var members = new List<RvrCombatant>();
        foreach (RvrCombatant member in heads)
            if (!ReferenceEquals(member.Living, plan.MainAssist) && !crowdControl.Contains(member.Living) &&
                (member.Control & (RvrControlState.Mezzed | RvrControlState.Stunned)) == 0)
                members.Add(member);
        if (casters.Count == 0 || members.Count == 0)
            return plan.Interrupts.IsEmpty ? plan : plan with { Interrupts = ImmutableArray<RvrAssignment>.Empty };

        int cap = Cap(heads.Count);
        var jobs = ImmutableArray.CreateBuilder<RvrAssignment>();
        var busy = new HashSet<GameLiving>(ReferenceEqualityComparer.Instance);
        var covered = new HashSet<GameLiving>(ReferenceEqualityComparer.Instance);
        foreach (RvrAssignment job in plan.Interrupts)
        {
            if (jobs.Count >= cap)
                break;
            RvrCombatant member = members.Find(m => ReferenceEquals(m.Living, job.Member));
            RvrCombatant caster = casters.Find(c => ReferenceEquals(c.Living, job.Subject));
            if (member == null || caster == null || busy.Contains(member.Living) || covered.Contains(caster.Living) ||
                Means(member, facts.Member(member), caster) == RvrInterruptMeans.None)
                continue;
            jobs.Add(job);
            busy.Add(member.Living);
            covered.Add(caster.Living);
        }

        casters.Sort((a, b) => Order(b, facts).CompareTo(Order(a, facts)));
        foreach (RvrCombatant caster in casters)
        {
            if (jobs.Count >= cap)
                break;
            if (covered.Contains(caster.Living))
                continue;
            RvrCombatant best = null;
            RvrInterruptMeans bestMeans = RvrInterruptMeans.None;
            float bestDistance = float.MaxValue;
            foreach (RvrCombatant member in members)
            {
                if (busy.Contains(member.Living))
                    continue;
                RvrInterruptMeans means = Means(member, facts.Member(member), caster);
                if (means == RvrInterruptMeans.None)
                    continue;
                float dx = member.X - caster.X, dy = member.Y - caster.Y;
                float distance = dx * dx + dy * dy;
                if (best == null || means < bestMeans || means == bestMeans && distance < bestDistance)
                {
                    best = member;
                    bestMeans = means;
                    bestDistance = distance;
                }
            }
            if (best == null)
                continue;
            jobs.Add(new RvrAssignment(best.Living, caster.Living, now));
            busy.Add(best.Living);
            covered.Add(caster.Living);
        }
        return plan with { Interrupts = jobs.ToImmutable() };
    }

    /// <summary>Healers first, then by the kill order.</summary>
    private static double Order(RvrCombatant enemy, RvrFieldFacts facts) =>
        (enemy.Role == RvrRole.Healer ? 1_000 : 0) + RvrTargetPriority.Score(enemy, facts.Enemy(enemy));
}
