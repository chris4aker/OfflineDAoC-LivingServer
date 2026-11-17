using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using DOL.GS.Styles;

namespace DOL.GS;

/// <summary>
/// Open-field RvR, work package 3 ("Hands", docs/RVR-FIELD-1.2.md): who guards whom, when a group mate counts as hit in
/// melee, and whom a guard pulls off his charge. The 2003 drill: "Shield tanks - Guard + intercept + /stick on your
/// casters, slam meleers that get too close" (FH Jan 2003, era-dated). Pure: it reads only the fight board's snapshot
/// (<see cref="RvrFightBoard"/>) and the plan; no clock, no random number, no game access. The brain carries it out
/// (BotBrain.TankPeel.cs, BotBrain.RvrPositioning.cs).
///
/// Every number here that the research does not give (the reach slack, the leash, the windows, the error shares) is a
/// design value, not a 2003 measurement.
/// </summary>
public static class RvrPeelPlan
{
    /// <summary>
    /// An enemy this close (2D) to a member, targeting it and fighting, hits it in melee. Melee reach is about 200 with
    /// the attacker's own size; the board's positions are up to a second old, hence the slack. Design value.
    /// </summary>
    public const int MeleeReach = 250;

    /// <summary>"Slam meleers that get too close": an enemy melee fighter this close to a guarded member is peeled even
    /// before it swings at it. Design value.</summary>
    public const int CloseRadius = 300;

    /// <summary>A guard engages enemies this close to his charge; farther ones he leaves to the others (he stays with
    /// his charge). Guard itself works within GuardAbilityHandler.GUARD_DISTANCE (256). Design value.</summary>
    public const int LeashRadius = 450;

    /// <summary>A sticking guard walks back to his charge when farther than this from his escort spot. Design value.</summary>
    public const int StickRadius = 180;

    /// <summary>An over-extension (the tank forgets his charge) lasts this long once rolled. Design value.</summary>
    public const long OverExtendWindowMilliseconds = 20_000;

    private const ulong OverExtendSalt = 0x52565033_4F564558; // "RVP3OVEX"

    /// <summary>Whom a tank protects first: the main healers, then casters, then hybrid support. -1: not protected.</summary>
    public static int ProtectRank(RvrRole role) => role switch
    {
        RvrRole.Healer => 0,
        RvrRole.Caster => 1,
        RvrRole.Support => 2,
        _ => -1,
    };

    /// <summary>A member that guards: a tank (shield or not; the brain guards only with a shield in hand).</summary>
    public static bool IsProtector(RvrCombatant member) =>
        member?.Living != null && !member.IsPet && member.Role == RvrRole.Tank;

    /// <summary>A member a tank guards: a healer, a caster or a hybrid support class.</summary>
    public static bool IsProtectee(RvrCombatant member) =>
        member?.Living != null && !member.IsPet && ProtectRank(member.Role) >= 0;

    /// <summary>
    /// Work package 3's step of the plan (RvrFightPlanner.PlanProtect): the protect assignments for this board. None with
    /// rvr_bot_peel off or no enemy in view (the plan then stays neutral for the peel). Pure.
    /// </summary>
    public static RvrFightPlan Protect(RvrFightPlan plan, RvrFightBoard board, RvrTacticsSwitches switches, long now)
    {
        if (plan == null)
            return null;
        ImmutableArray<RvrAssignment> protect = !switches.Peel || board == null || board.Enemies.IsDefaultOrEmpty
            ? ImmutableArray<RvrAssignment>.Empty
            : Assign(board.Members, plan.Protect, now);
        return protect.IsEmpty && plan.Protect.IsEmpty ? plan : plan with { Protect = protect };
    }

    /// <summary>
    /// One protector per protected member where possible, highest rank first (healers, then casters, then support), each
    /// tank guarding one member. Assignments that are still valid are kept (a tank does not swap charges every second);
    /// a free tank goes to the nearest unguarded member in rank order; when no tank is free, a tank guarding a caster
    /// or support member moves to an unguarded healer. Pure.
    /// </summary>
    public static ImmutableArray<RvrAssignment> Assign(ImmutableArray<RvrCombatant> members,
        ImmutableArray<RvrAssignment> previous, long now)
    {
        if (members.IsDefaultOrEmpty)
            return ImmutableArray<RvrAssignment>.Empty;
        var protectors = new List<RvrCombatant>();
        var protectees = new List<RvrCombatant>();
        foreach (RvrCombatant member in members)
        {
            if (IsProtector(member))
                protectors.Add(member);
            else if (IsProtectee(member))
                protectees.Add(member);
        }
        if (protectors.Count == 0 || protectees.Count == 0)
            return ImmutableArray<RvrAssignment>.Empty;
        protectors.Sort((a, b) => a.Key.CompareTo(b.Key));
        protectees.Sort((a, b) =>
        {
            int rank = ProtectRank(a.Role).CompareTo(ProtectRank(b.Role));
            return rank != 0 ? rank : a.Key.CompareTo(b.Key);
        });

        // subject -> its assignment; protector -> its subject
        var bySubject = new Dictionary<GameLiving, RvrAssignment>(ReferenceEqualityComparer.Instance);
        var byProtector = new Dictionary<GameLiving, GameLiving>(ReferenceEqualityComparer.Instance);
        bool IsProtectorLiving(GameLiving living) => protectors.Exists(p => ReferenceEquals(p.Living, living));
        bool IsProtecteeLiving(GameLiving living) => protectees.Exists(p => ReferenceEquals(p.Living, living));

        if (!previous.IsDefaultOrEmpty)
            foreach (RvrAssignment job in previous)
                if (job.Member != null && job.Subject != null && IsProtectorLiving(job.Member) && IsProtecteeLiving(job.Subject) &&
                    !byProtector.ContainsKey(job.Member) && !bySubject.ContainsKey(job.Subject))
                {
                    bySubject[job.Subject] = job;
                    byProtector[job.Member] = job.Subject;
                }

        // free tanks to the unguarded, in rank order, nearest first
        foreach (RvrCombatant subject in protectees)
        {
            if (bySubject.ContainsKey(subject.Living))
                continue;
            RvrCombatant nearest = null;
            long nearestDistance = long.MaxValue;
            foreach (RvrCombatant protector in protectors)
            {
                if (byProtector.ContainsKey(protector.Living))
                    continue;
                long distance = DistanceSquared(protector, subject);
                if (distance < nearestDistance)
                {
                    nearest = protector;
                    nearestDistance = distance;
                }
            }
            if (nearest == null)
                break;
            bySubject[subject.Living] = new RvrAssignment(nearest.Living, subject.Living, now);
            byProtector[nearest.Living] = subject.Living;
        }

        // no tank free: an unguarded healer takes the tank of the lowest-ranked guarded member below it
        foreach (RvrCombatant subject in protectees)
        {
            if (bySubject.ContainsKey(subject.Living))
                continue;
            int rank = ProtectRank(subject.Role);
            RvrCombatant donor = null;
            for (int i = protectees.Count - 1; i >= 0; i--)
            {
                RvrCombatant other = protectees[i];
                if (ProtectRank(other.Role) > rank && bySubject.ContainsKey(other.Living))
                {
                    donor = other;
                    break;
                }
            }
            if (donor == null)
                continue;
            GameLiving tank = bySubject[donor.Living].Member;
            bySubject.Remove(donor.Living);
            bySubject[subject.Living] = new RvrAssignment(tank, subject.Living, now);
            byProtector[tank] = subject.Living;
        }

        ImmutableArray<RvrAssignment>.Builder result = ImmutableArray.CreateBuilder<RvrAssignment>(bySubject.Count);
        foreach (RvrCombatant subject in protectees)
            if (bySubject.TryGetValue(subject.Living, out RvrAssignment job))
                result.Add(job);
        return result.ToImmutable();
    }

    /// <summary>The member a protector guards in this plan, or null. Pure.</summary>
    public static GameLiving ChargeOf(RvrFightPlan plan, GameLiving protector) =>
        plan == null ? null : RvrFightPlan.SubjectFor(plan.Protect, protector);

    /// <summary>The protector assigned to <paramref name="member"/>, or null. Pure.</summary>
    public static GameLiving GuardOf(RvrFightPlan plan, GameLiving member)
    {
        if (plan == null || member == null)
            return null;
        foreach (RvrAssignment job in plan.Protect)
            if (ReferenceEquals(job.Subject, member))
                return job.Member;
        return null;
    }

    /// <summary>The board's record of a member, or null.</summary>
    public static RvrCombatant Find(ImmutableArray<RvrCombatant> side, GameLiving living)
    {
        if (living != null && !side.IsDefaultOrEmpty)
            foreach (RvrCombatant combatant in side)
                if (ReferenceEquals(combatant?.Living, living))
                    return combatant;
        return null;
    }

    /// <summary>
    /// An enemy that is hitting <paramref name="member"/> (standing at x, y) in melee: it targets the member, it is
    /// fighting, it is not mezzed or stunned, and it is within <see cref="MeleeReach"/>. Players, bots and their pets
    /// all count (the fight board lists them). Something attacking someone else never does. Pure.
    /// </summary>
    public static bool HitInMelee(GameLiving member, int x, int y, ImmutableArray<RvrCombatant> enemies) =>
        MeleeAttackerOn(member, x, y, enemies) != null;

    /// <summary>The nearest enemy hitting <paramref name="member"/> in melee (see <see cref="HitInMelee"/>), or null. Pure.</summary>
    public static RvrCombatant MeleeAttackerOn(GameLiving member, int x, int y, ImmutableArray<RvrCombatant> enemies)
    {
        if (member == null || enemies.IsDefaultOrEmpty)
            return null;
        RvrCombatant nearest = null;
        long nearestDistance = long.MaxValue;
        foreach (RvrCombatant enemy in enemies)
        {
            if (enemy == null || !ReferenceEquals(enemy.Target, member) || !enemy.Fighting || CannotSwing(enemy))
                continue;
            long distance = DistanceSquared(enemy.X, enemy.Y, x, y);
            if (distance <= (long)MeleeReach * MeleeReach && distance < nearestDistance)
            {
                nearest = enemy;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    /// <summary>
    /// A member counts as hit in melee (for Guard and Intercept) when the RvR board shows an enemy hitting it
    /// (<see cref="HitInMelee"/>), or when the group's fight board lists it in HitInMelee (from the attacker lists:
    /// players, enemy bots and monsters that target it and swing in reach). Pure.
    /// </summary>
    public static bool Threatened(GameLiving member, int x, int y, ImmutableArray<RvrCombatant> enemies,
        IReadOnlySet<GameLiving> fightBoardHitInMelee) =>
        member != null && (HitInMelee(member, x, y, enemies) || fightBoardHitInMelee?.Contains(member) == true);

    /// <summary>
    /// Whom a guard pulls off <paramref name="charge"/>: first an enemy hitting it in melee, then any enemy that targets
    /// it, then an enemy melee fighter (tank, melee, stealther or pet) within <see cref="CloseRadius"/> of it; nearest
    /// to the charge first. A mezzed enemy is never peeled (a hit would break the group's own mez). Null: nobody. Pure.
    /// </summary>
    public static RvrCombatant AttackerOf(RvrCombatant charge, ImmutableArray<RvrCombatant> enemies)
    {
        if (charge?.Living == null || enemies.IsDefaultOrEmpty)
            return null;
        RvrCombatant best = null;
        int bestTier = int.MaxValue;
        long bestDistance = long.MaxValue;
        foreach (RvrCombatant enemy in enemies)
        {
            if (enemy?.Living == null || (enemy.Control & RvrControlState.Mezzed) != 0)
                continue;
            long distance = DistanceSquared(enemy, charge);
            bool targets = ReferenceEquals(enemy.Target, charge.Living);
            int tier;
            if (targets && enemy.Fighting && !CannotSwing(enemy) && distance <= (long)MeleeReach * MeleeReach)
                tier = 0;
            else if (targets)
                tier = 1;
            else if (IsMeleeKind(enemy.Role) && distance <= (long)CloseRadius * CloseRadius)
                tier = 2;
            else
                continue;
            if (tier < bestTier || tier == bestTier && distance < bestDistance)
            {
                best = enemy;
                bestTier = tier;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// In an open-field fight a tank's protection target is limited to its own assignment (so all tanks do not chase the
    /// same attacker): the enemy to pull off its charge (<see cref="AttackerOf"/>), or null when it has no charge or
    /// nobody is on it. Pure.
    /// </summary>
    public static GameLiving ProtectionTargetFor(RvrFightPlan plan, RvrFightBoard board, GameLiving protector)
    {
        GameLiving charge = ChargeOf(plan, protector);
        if (charge == null || board == null)
            return null;
        return AttackerOf(Find(board.Members, charge), board.Enemies)?.Living;
    }

    /// <summary>A style with a stun proc (Slam and the like): what a slam queues. Pure.</summary>
    public static bool IsStunStyle(Style style)
    {
        if (style?.Procs == null)
            return false;
        foreach (StyleProcInfo proc in style.Procs)
            if (proc?.Spell?.SpellType == eSpellType.StyleStun)
                return true;
        return false;
    }

    /// <summary>Melee kinds a guard slams on sight near his charge.</summary>
    public static bool IsMeleeKind(RvrRole role) => role is RvrRole.Tank or RvrRole.Melee or RvrRole.Stealther or RvrRole.Pet;

    private static bool CannotSwing(RvrCombatant enemy) =>
        (enemy.Control & (RvrControlState.Mezzed | RvrControlState.Stunned)) != 0;

    // ---- human errors ----------------------------------------------------------------------------------------------

    /// <summary>
    /// The share of time a shield tank over-extends and leaves his charge ("tanks dont pan camera and overextend easily",
    /// Uthgard 2011; FH Aug 2003): Casual 35%, Average 15%, Veteran 5%, plus up to 10 points for a bold personality
    /// (boldness 0.5 is neutral). Design values.
    /// </summary>
    public static double OverExtendShare(RvrSkillTier tier, double boldness)
    {
        double share = tier switch
        {
            RvrSkillTier.Casual => 0.35,
            RvrSkillTier.Veteran => 0.05,
            _ => 0.15,
        };
        return Math.Clamp(share + (Math.Clamp(boldness, 0, 1) - 0.5) * 0.2, 0, 0.6);
    }

    /// <summary>Whether this tank over-extends in the <see cref="OverExtendWindowMilliseconds"/> window that holds
    /// <paramref name="now"/>. Pure: the same tank, window and tier give the same answer.</summary>
    public static bool OverExtends(RvrSkillTier tier, double boldness, long memberId, long now) =>
        Roll(memberId, Math.DivRem(now, OverExtendWindowMilliseconds).Quotient, OverExtendSalt) < OverExtendShare(tier, boldness);

    /// <summary>A fixed roll in [0, 1) for a member, a time window and a salt. Pure.</summary>
    public static double Roll(long memberId, long window, ulong salt) =>
        AutonomousBotPersonality.Mix(unchecked(memberId * 1_000_003L + window), salt) % 10_000 / 10_000.0;

    private static long DistanceSquared(RvrCombatant a, RvrCombatant b) => DistanceSquared(a.X, a.Y, b.X, b.Y);

    private static long DistanceSquared(int ax, int ay, int bx, int by)
    {
        long dx = ax - bx, dy = ay - by;
        return dx * dx + dy * dy;
    }

    /// <summary>2D position of a board record.</summary>
    public static Vector2 At(RvrCombatant combatant) => new(combatant.X, combatant.Y);
}
