using System;

namespace DOL.GS;

/// <summary>What the stealth rule needs to know about the bot looking and the stealthed enemy (plain values).</summary>
public readonly record struct RvrStealthSighting(
    double Distance,
    int ObserverLevel,
    int StealthSpec,
    bool ObserverDetectHidden = false,
    bool TargetDetectHidden = false,
    bool ObserverSeeHidden = false,
    bool TargetAssassin = false,
    bool Camouflage = false,
    bool Vanish = false,
    int ObserverStealthBonus = 0,
    bool ObserverInCombatWithOther = false,
    bool LineOfSight = true,
    bool TargetArcher = false,
    bool ObserverTrueSight = false);

/// <summary>
/// Minimal stealth detection for bots (owner decision 5, 2026-11-16; docs/RVR-FIELD-1.2.md, layer "Eyes"; work package
/// 1). Until 1.2 a bot never saw a stealthed enemy until it struck. Now a bot may notice one, by chance on each scan,
/// only close up, and never better than a human in its place: a player's client shows a stealther within the player's
/// detection range at once (GamePlayer.CanDetect: (level - stealth spec) x 20 + 125, more with Detect Hidden or See
/// Hidden, never through Vanish), so a bot notices only inside that range, and only by chance. On top of it the bot's
/// own limit: within 125 units the chance per second rises to about 50% at point blank (veteran 60%, easy 35%), it
/// falls to 0 at 350 units, a bot fighting someone else never notices, and line of sight is needed. A first cut; the
/// numbers are ours, not from a source. The same rule for every stealthed enemy, player or bot. A bot on True Sight (#531
/// step 2c-2, owner answer 71A) sees every stealther in its line of sight at once, as a player on it does. Pure.
/// </summary>
public static class RvrStealthNotice
{
    /// <summary>Inside this distance the chance per second rises towards its point-blank value.</summary>
    public const int CloseRange = 125;
    /// <summary>Beyond this a bot never notices a stealthed enemy, whatever a human could.</summary>
    public const int MaxRange = 350;
    /// <summary>The chance per second at 125 units, as a share of the point-blank chance.</summary>
    public const double EdgeShare = 0.3;
    /// <summary>The server's hard cap on a player's detection range (GamePlayer.CanDetect; StealthRules.SeeHardCap).</summary>
    public const int HumanHardCap = DOL.GS.ServerRules.StealthRules.SeeHardCap;

    /// <summary>The chance per second at point blank for an awareness level: easy 35%, normal 50%, veteran 60%.</summary>
    public static double PointBlankPerSecond(RvrBotAwareness awareness) => awareness switch
    {
        RvrBotAwareness.Easy => 0.35,
        RvrBotAwareness.Veteran => 0.60,
        _ => 0.50,
    };

    /// <summary>
    /// How far a human player in the bot's place sees this stealther, as the server rules it for a player: the one rule,
    /// StealthRules.SeeRange (#522), which GamePlayer.CanDetect and CanDetectStealthedBot use too. 0 through Vanish. Pure.
    /// </summary>
    public static int HumanRange(in RvrStealthSighting s) => DOL.GS.ServerRules.StealthRules.SeeRange(new(
        s.ObserverLevel, s.StealthSpec, s.ObserverDetectHidden, s.TargetDetectHidden, s.ObserverSeeHidden, s.TargetAssassin,
        s.Camouflage, s.Vanish, s.ObserverStealthBonus, s.TargetArcher));

    /// <summary>
    /// How far a bot on True Sight looks for stealthers (#531 step 2c-2, owner answer 71A: "as a player, for its 60 s the
    /// archer sees every stealthed enemy in its line of sight; the 350-unit bot cap doesn't apply"): past any scan's reach.
    /// </summary>
    public const int TrueSightReach = 6_000;

    /// <summary>True Sight sees this stealther: the observer has it and the stealther is not under Vanish. Pure.</summary>
    public static bool TrueSightSees(in RvrStealthSighting s) => s.ObserverTrueSight && !s.Vanish;

    /// <summary>How close a stealthed enemy must be to be a candidate for this rule at all: 350, or True Sight's reach.</summary>
    public static int CandidateRange(bool observerTrueSight) => observerTrueSight ? TrueSightReach : MaxRange;

    /// <summary>
    /// The farthest this bot may notice this stealther: the human's range, never beyond 350; on True Sight, anywhere in its
    /// sight (<see cref="TrueSightReach"/>). Pure.
    /// </summary>
    public static int Limit(in RvrStealthSighting s) => TrueSightSees(s) ? TrueSightReach : Math.Min(MaxRange, HumanRange(s));

    /// <summary>
    /// The chance per second of noticing: 0 beyond <see cref="Limit"/>, while fighting someone else or without line of
    /// sight; otherwise from the point-blank value down to 30% of it at 125 units, then down to 0 at the limit. Pure.
    /// </summary>
    public static double ChancePerSecond(in RvrStealthSighting s, RvrBotAwareness awareness)
    {
        if (!s.LineOfSight)
            return 0;
        // On True Sight every stealther in sight is seen at once, as by a player on it (owner answer 71A); Vanish still hides
        if (TrueSightSees(s))
            return s.Distance <= TrueSightReach ? 1 : 0;
        if (s.ObserverInCombatWithOther)
            return 0;
        int limit = Limit(s);
        double d = Math.Max(0, s.Distance);
        if (d > limit)
            return 0;
        double top = PointBlankPerSecond(awareness);
        if (d <= CloseRange)
            return top * (1 - (1 - EdgeShare) * d / CloseRange);
        if (limit <= CloseRange)
            return 0;
        return top * EdgeShare * (limit - d) / (limit - CloseRange);
    }

    /// <summary>The chance for one scan that comes this long after the last one (same clock as the perception rule). Pure.</summary>
    public static double ChancePerScan(in RvrStealthSighting s, RvrBotAwareness awareness, double scanIntervalMilliseconds)
    {
        double perSecond = ChancePerSecond(s, awareness);
        if (perSecond <= 0)
            return 0;
        double seconds = AutonomousRvrPerception.ClampScanInterval(scanIntervalMilliseconds) / 1000.0;
        return 1 - Math.Pow(1 - Math.Min(1, perSecond), seconds);
    }
}
