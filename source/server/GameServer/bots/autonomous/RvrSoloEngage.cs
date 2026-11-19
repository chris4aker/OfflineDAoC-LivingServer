using System;

namespace DOL.GS;

/// <summary>
/// What a soloer (or a duo, or a stealther) knows about one enemy it could open on: its own state, the target, and
/// what it can see around the target (only what it perceived: the threat scan's visible list).
/// </summary>
public readonly record struct RvrSoloFacts
{
    public int OwnLevel { get; init; }
    public int OwnHealthPercent { get; init; }
    public bool UsesPower { get; init; }
    public int OwnPowerPercent { get; init; }
    /// <summary>The soloer and its duo partner, if the partner is alive and near.</summary>
    public int Allies { get; init; }
    public bool Stealthed { get; init; }
    public int TargetLevel { get; init; }
    public int TargetHealthPercent { get; init; }
    /// <summary>Enemies the soloer sees within <see cref="RvrSoloEngage.GroupRadius"/> of the target, the target included.</summary>
    public int EnemiesNearTarget { get; init; }
    /// <summary>Of those, the main healers (Cleric, Druid, Healer).</summary>
    public int HealersNearTarget { get; init; }
    /// <summary>The target is busy with someone else (a monster, another realm): a third party's fight.</summary>
    public bool TargetBusy { get; init; }
    /// <summary>The target is attacking the soloer or a friend near it: fighting back is always allowed.</summary>
    public bool TargetAttackingUs { get; init; }
}

/// <summary>
/// When a soloer, duo or stealther opens a fight (work package 6; docs/RVR-FIELD-1.2.md, "Soloers and stealthers": in
/// 2003 visible soloing was close to impossible, "invisible or join a zerg"). It opens only when the odds allow it:
/// the target is alone or weaker, there is no enemy group around it (more enemies than the soloer has friends; a
/// healer counts twice), and the soloer is healthy and has power. From stealth it may take a target a few levels above
/// it. It always fights back. Like a person it misjudges sometimes, more in a casual hand. Pure.
/// </summary>
public static class RvrSoloEngage
{
    public const int GroupRadius = 1_500;
    public const int MinimumHealthPercent = 70;
    public const int MinimumPowerPercent = 40;
    /// <summary>Below this a soloer never opens, mistake or not.</summary>
    public const int NeverBelowHealthPercent = 35;
    public const int WeakTargetHealthPercent = 50;

    /// <summary>How often the read of the odds is wrong (both ways): casual 20%, average 10%, veteran 4%.</summary>
    public static double MistakeChance(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Veteran => 0.04,
        RvrSkillTier.Average => 0.10,
        _ => 0.20,
    };

    /// <summary>The odds, read right.</summary>
    public static bool OddsAllow(in RvrSoloFacts facts)
    {
        if (facts.TargetAttackingUs)
            return true;
        if (facts.OwnHealthPercent < MinimumHealthPercent || facts.UsesPower && facts.OwnPowerPercent < MinimumPowerPercent)
            return false;
        int allies = Math.Max(1, facts.Allies);
        int enemies = Math.Max(1, facts.EnemiesNearTarget) + Math.Max(0, facts.HealersNearTarget);
        if (enemies > allies)
            return false; // an enemy group
        bool alone = facts.EnemiesNearTarget <= 1;
        int edge = (facts.Stealthed ? 3 : 1) + (allies >= 2 ? 1 : 0);
        bool weaker = facts.TargetLevel <= facts.OwnLevel - 2 || facts.TargetHealthPercent <= WeakTargetHealthPercent || facts.TargetBusy;
        return alone && facts.TargetLevel <= facts.OwnLevel + edge || weaker && facts.TargetLevel <= facts.OwnLevel + edge + 2;
    }

    /// <summary>
    /// The decision, with the tier's mistakes: a roll below <see cref="MistakeChance"/> flips the read (opening on bad
    /// odds, or letting a good target pass). Fighting back is never flipped, and nobody opens hurt below
    /// <see cref="NeverBelowHealthPercent"/>.
    /// </summary>
    public static bool MayOpen(in RvrSoloFacts facts, RvrSkillTier tier, double roll)
    {
        if (facts.TargetAttackingUs)
            return true;
        if (facts.OwnHealthPercent < NeverBelowHealthPercent)
            return false;
        bool read = OddsAllow(facts);
        return roll < MistakeChance(tier) ? !read : read;
    }
}
