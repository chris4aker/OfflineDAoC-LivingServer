using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// What a bot group's stance machine remembers between fight boards (work package 4 of docs/RVR-FIELD-1.2.md). Written
/// by the group's members through <see cref="RvrFightBoards.UpdateState"/> (RvrEngagementRuntime), read by the pure
/// planner. Immutable.
/// </summary>
public sealed record RvrEngagementFacts
{
    /// <summary>A stable number for the group (its leader's database id): the seed of its rolls.</summary>
    public long GroupKey { get; init; }
    /// <summary>How many members the group has, dead or alive. Below 2 the bot is solo and has no stance.</summary>
    public int GroupSize { get; init; }
    /// <summary>The leader's Patience trait, 0 to 9 (5 with personalities off): impatient groups stop watching early.</summary>
    public int Patience { get; init; } = 5;
    /// <summary>The leader's boldness, 0 to 1 (AutonomousRvrPerception.Boldness; 0.5 with personalities off).</summary>
    public double Boldness { get; init; } = 0.5;
    /// <summary>When a member last noticed an enemy (RvR perception let it through) or the group touched one.</summary>
    public long LastNoticedAt { get; init; } = long.MinValue / 2;
    /// <summary>When the current encounter began: the first notice after a quiet spell.</summary>
    public long EncounterSince { get; init; } = long.MinValue / 2;
    /// <summary>When the group was last in contact (an enemy on it, or it on an enemy).</summary>
    public long LastContactAt { get; init; } = long.MinValue / 2;
    /// <summary>Where the group stood when it was last in contact.</summary>
    public Vector3 LastFightPoint { get; init; }
    /// <summary>Where the enemy stood when the group was last in contact.</summary>
    public Vector3? LastEnemyPoint { get; init; }
    /// <summary>When the group last called a retreat.</summary>
    public long LastRetreatAt { get; init; } = long.MinValue / 2;
    /// <summary>A retreat or regroup another group of the warband passed on; null: none.</summary>
    public RvrStanceRelay? Relay { get; init; }

    public static RvrEngagementFacts Empty { get; } = new();
}

/// <summary>
/// A retreat or regroup passed on from another group of the warband: it counts from <see cref="AppliesAt"/> until
/// <see cref="ExpiresAt"/>. <see cref="From"/> is the sending group's log name (RvrEngagementRuntime.GroupName), logged
/// as <c>from=</c> on the relayed stance's RVR_REGROUP / RVR_RETREAT line; null when not known.
/// </summary>
public readonly record struct RvrStanceRelay(RvrStance Stance, long AppliesAt, long ExpiresAt, string From = null);

/// <summary>Which stances the settings allow (rvr_bot_size_up, rvr_bot_disengage with bot_veteran_retreat, rvr_bot_regroup).</summary>
public readonly record struct RvrEngagementOptions(bool SizeUp, bool Kite, bool Retreat, bool Regroup)
{
    public bool Any => SizeUp || Kite || Retreat || Regroup;

    public static RvrEngagementOptions AllOn => new(true, true, true, true);

    /// <summary>
    /// From the switches. Retreat needs rvr_bot_disengage AND bot_veteran_retreat (<paramref name="groupRetreatEnabled"/>,
    /// BotGroupRetreat.Enabled): the retreat runs through the group retreat. Kite needs rvr_bot_disengage only.
    /// </summary>
    public static RvrEngagementOptions From(RvrTacticsSwitches switches, bool groupRetreatEnabled) => new(
        switches.FieldTactics && switches.SizeUp,
        switches.FieldTactics && switches.Disengage,
        switches.FieldTactics && switches.Disengage && groupRetreatEnabled,
        switches.FieldTactics && switches.Regroup);
}

/// <summary>What one size-up saw: both sides, their effective power, the odds and the shape of the fight.</summary>
public readonly record struct RvrSizeUp
{
    public RvrStrength Friendly { get; init; }
    /// <summary>The side the group would fight: every enemy in view, or the stronger realm of a fight between two others.</summary>
    public RvrStrength Enemy { get; init; }
    public double FriendlyPower { get; init; }
    public double EnemyPower { get; init; }
    /// <summary>The group's share of the power on the field, 0 to 1 (0.5 even).</summary>
    public double Odds { get; init; }
    /// <summary>The odds as the group reads them: off by up to its tier's misjudgement.</summary>
    public double PerceivedOdds { get; init; }
    public bool EnemyInView { get; init; }
    public bool InContact { get; init; }
    public bool ThirdParty { get; init; }
    public bool EnemyBigger { get; init; }
    /// <summary>A melee-heavy enemy against a group that fights from range: kite it.</summary>
    public bool KiteMelee { get; init; }
    /// <summary>The share of the members within <see cref="RvrEngagement.HoldRadius"/> of their centre.</summary>
    public double Cohesion { get; init; }
    public bool StrungOut { get; init; }
    /// <summary>From the group's centre to the enemy's (flat); infinity with no enemy in view.</summary>
    public double EnemyDistance { get; init; }
    public Vector3 FriendlyCentre { get; init; }
    public Vector3? EnemyCentre { get; init; }
}

/// <summary>
/// A stance decision: the stance, why, the rally point for a regroup (null: keep the stored one), and whether starting
/// it is a real reason to pass it on to the warband (a retreat from a fight, a regroup after a retreat or a hurting fight).
/// </summary>
public readonly record struct RvrStanceDecision(RvrStance Stance, string Reason, Vector3? Rally = null, bool Relays = false);

/// <summary>One living member of a group as the main-body rule sees it: its region, where it stands, whether it leads.</summary>
public readonly record struct RvrMemberSpot(ushort Region, float X, float Y, bool Leader);

/// <summary>A group's main body: the region holding most of its living members, their centre there, and how many they are.</summary>
public readonly record struct RvrGroupBody(ushort Region, Vector2 Centre, int Members);

/// <summary>
/// What the group's runtime last acted on: the build time, stance, start and region of the group's own main record. Only
/// the main body's boards move it (<see cref="RvrEngagement.Track"/>), so a member in another region cannot end or restart
/// the group's stance. Immutable.
/// </summary>
public sealed record RvrStanceLedger
{
    public long LastBuild { get; init; } = long.MinValue;
    public RvrStance Stance { get; init; } = RvrStance.Engage;
    public long Since { get; init; } = long.MinValue;
    public ushort Region { get; init; }

    public static RvrStanceLedger Empty { get; } = new();
}

/// <summary>One step of the ledger: the next ledger, whether this board was acted on, whether the stance changed, and the stance before.</summary>
public readonly record struct RvrStanceStep(RvrStanceLedger Next, bool Acted, bool Changed, RvrStance Previous, long PreviousSince);

/// <summary>
/// Work package 4 of docs/RVR-FIELD-1.2.md ("Mind": stances, size-up, watching, retreat and regroup). The group's stance
/// machine and every rule around it, pure: no clock, no random number, no game access. Rolls come from the group's and
/// the member's keys (<see cref="AutonomousBotPersonality.Mix"/>), so a decision is the same on every board of an episode.
///
/// <para>Every number below is a DESIGN VALUE, first cut, to be tuned against the owner's PC logs. None is sourced.</para>
///
/// The stance (one per bot-only group; a solo bot has none):
/// <list type="bullet">
/// <item>Observe: an enemy group (2+ heads), or a fight between the other two realms, has been noticed and is not on the
/// group yet. Watch for <see cref="WatchMilliseconds"/> (tier base times the leader's patience), then decide.</item>
/// <item>Hold: members are strung out when the enemy is noticed: the front waits for the rest.</item>
/// <item>Engage: the odds are fair or better (<see cref="EngageOdds"/>).</item>
/// <item>Kite: the odds are below fair but not bad, or a melee-heavy enemy meets a group that fights from range. Only the
/// stance is published; work package 3 moves the bots.</item>
/// <item>Retreat: bad odds (<see cref="RetreatOdds"/>), or a lost fight. Runs through BotGroupRetreat.</item>
/// <item>Regroup: after a retreat, after a fight when the group needs rest, or when another group passes it on. It lasts
/// at least <see cref="MinRegroupMilliseconds"/> (by tier) unless the group is in contact.</item>
/// </list>
/// Only the board of the group's main body (<see cref="MainBody"/>) has a stance; a member in another region gets the
/// neutral Engage and cannot start or end the group's stance (<see cref="Track"/>). A group passes a retreat or regroup on
/// only for a real reason, once per <see cref="RelayWindowMilliseconds"/>, to groups near it in its region (<see cref="MayRelay"/>,
/// <see cref="RelayReaches"/>).
/// A stance lasts at least <see cref="MinStanceMilliseconds"/>, except that contact ends a watch, a hold or a regroup at
/// once (the enemy came to the group). A bot that is hit always fights back, whatever the stance (<see cref="MayTakeTarget"/>).
/// </summary>
public static class RvrEngagement
{
    // ---- hysteresis ---------------------------------------------------------------------------------------------------
    /// <summary>A stance lasts at least this long (docs/RVR-FIELD-1.2.md).</summary>
    public const long MinStanceMilliseconds = 5_000;
    /// <summary>A notice counts for this long (as long as RvR perception remembers a noticed enemy).</summary>
    public const long AwareMilliseconds = AutonomousRvrPerception.ForgetAfterMilliseconds;

    // ---- size-up: effective power (design values) ---------------------------------------------------------------------
    /// <summary>Level weight: (level / 50) to this power. A level 40 counts 0.64 of a level 50.</summary>
    public const double LevelExponent = 2.0;
    /// <summary>Health weight: a head at 0% health still counts this much (it may be healed).</summary>
    public const double HealthFloor = 0.25;
    /// <summary>Power weight: a power user at 0% power counts this much.</summary>
    public const double PowerFloor = 0.4;
    /// <summary>Each main healer multiplies the side by this much more, up to <see cref="MaxCountedHealers"/>.</summary>
    public const double HealerBonus = 0.15;
    public const int MaxCountedHealers = 3;
    /// <summary>A mezzed or stunned head counts this much of a head.</summary>
    public const double ControlledValue = 0.25;
    /// <summary>A pet counts this much of a head.</summary>
    public const double PetValue = 0.3;

    // ---- size-up: thresholds (design values) --------------------------------------------------------------------------
    /// <summary>Engage at these odds or better (0.45: a power ratio of about 0.82 to 1).</summary>
    public const double EngageOdds = 0.45;
    /// <summary>Retreat below these odds (0.30: a power ratio of about 0.43 to 1, such as 8 against 19 equals).</summary>
    public const double RetreatOdds = 0.30;
    /// <summary>In contact with every healer dead and the enemy's alive, the fight is lost below these odds.</summary>
    public const double LostOdds = 0.38;
    /// <summary>An enemy of at least 3 heads with this share in melee is melee-heavy ...</summary>
    public const double MeleeHeavyShare = 0.6;
    public const int MeleeHeavyMinHeads = 3;
    /// <summary>... and a group with this share at range kites it ...</summary>
    public const double KiteRangedShare = 0.4;
    /// <summary>... unless the odds are at least this good (a clearly weaker melee group is simply fought).</summary>
    public const double KiteMeleeMaxOdds = 0.70;
    /// <summary>How far a group misreads the odds, by tier: Casual, Average, Veteran.</summary>
    public const double CasualMisread = 0.10, AverageMisread = 0.06, VeteranMisread = 0.03;
    /// <summary>Each recent wipe (up to <see cref="MaxCautionWipes"/>) raises both thresholds by this much.</summary>
    public const double WipeCaution = 0.04;
    public const int MaxCautionWipes = 3;
    /// <summary>The boldest leader lowers the engage threshold by this much, the most timid raises it as much.</summary>
    public const double BoldEngageSwing = 0.06;
    public const double BoldRetreatSwing = 0.05;

    // ---- watching (design values) --------------------------------------------------------------------------------------
    /// <summary>Observe needs an enemy group of this many heads (or a fight between the other two realms).</summary>
    public const int EnemyGroupHeads = 2;
    /// <summary>Base watch, by tier: casual groups jump the gun, veterans size up.</summary>
    public const long CasualWatchMilliseconds = 6_000, AverageWatchMilliseconds = 9_000, VeteranWatchMilliseconds = 12_000;
    public const long MaxWatchMilliseconds = 20_000;

    // ---- hold (design values) ------------------------------------------------------------------------------------------
    public const int HoldRadius = 1_000;
    /// <summary>A group of at least <see cref="HoldMinMembers"/> with fewer than this share within <see cref="HoldRadius"/> is strung out.</summary>
    public const double HoldCohesion = 0.7;
    public const int HoldMinMembers = 3;
    public const long MaxHoldMilliseconds = 12_000;

    // ---- retreat (design values) ---------------------------------------------------------------------------------------
    /// <summary>A retreat ends after this long at most (the group retreat order's own length).</summary>
    public const long MaxRetreatMilliseconds = BotGroupRetreat.RetreatMilliseconds;
    /// <summary>... or once the enemy's centre is this far away.</summary>
    public const int RetreatClearDistance = 3_000;
    /// <summary>A group that ran does not run again for this long (it fights: "kill and then run like hell").</summary>
    public const long RetreatCooldownMilliseconds = BotGroupRetreat.CooldownMilliseconds;
    /// <summary>Ragged retreats: how long a member takes to react to the call, by tier (min, max).</summary>
    public const int CasualReactMin = 800, CasualReactMax = 2_000;
    public const int AverageReactMin = 500, AverageReactMax = 1_600;
    public const int VeteranReactMin = 300, VeteranReactMax = 1_000;
    /// <summary>Ragged retreats: the share of members that never run, by tier.</summary>
    public const double CasualHoldouts = 0.20, AverageHoldouts = 0.10, VeteranHoldouts = 0.04;
    /// <summary>A member hit in the last this-long fights back instead of running.</summary>
    public const long HitMemoryMilliseconds = 2_500;

    // ---- regroup and after the fight (design values) -------------------------------------------------------------------
    public const long MaxRegroupMilliseconds = 75_000;
    /// <summary>A regroup lasts at least this long, by tier, unless the group is in contact (attacked or attacking):
    /// casual groups head out again soonest, veterans take their time to rest and re-form. Design values.</summary>
    public const long CasualMinRegroupMilliseconds = 10_000, AverageMinRegroupMilliseconds = 15_000, VeteranMinRegroupMilliseconds = 20_000;
    /// <summary>A regroup is over when the members are together and at this health and power (average).</summary>
    public const int RegroupHealth = 85, RegroupPower = 70;
    /// <summary>A member this close to the rally point is there.</summary>
    public const int RallyArriveRadius = 400;
    /// <summary>A fight that ended this recently, with the group under <see cref="AfterFightHealth"/> or <see cref="AfterFightPower"/>, is rested after.</summary>
    public const long AfterFightMilliseconds = 20_000;
    public const int AfterFightHealth = 80, AfterFightPower = 60;
    /// <summary>A group that moves on rests this far from the fight, away from where the enemy came from.</summary>
    public const int AfterFightMoveDistance = 1_800;
    /// <summary>The chance a group leaves the fight's spot to rest ("the place you just fought will be the first place the
    /// enemy come looking"), by tier: casual groups rest where they fought, veterans move.</summary>
    public const double CasualMovesOn = 0.10, AverageMovesOn = 0.50, VeteranMovesOn = 0.90;

    // ---- warband relay -------------------------------------------------------------------------------------------------
    /// <summary>The perception calls' relay: half the time ...</summary>
    public const double RelayChance = AutonomousRvrPerception.WarbandRelayChance;
    /// <summary>... 4 s late.</summary>
    public const long RelayDelayMilliseconds = AutonomousRvrPerception.WarbandRelayDelayMilliseconds;
    /// <summary>A relayed stance is acted on within this long after it arrives, or not at all.</summary>
    public const long RelayValidMilliseconds = 10_000;
    /// <summary>A group passes a stance on at most once in this long (fix of the 1.2 test build 19e7037 regroup flap).</summary>
    public const long RelayWindowMilliseconds = 60_000;
    /// <summary>Only groups whose main body is in the sender's region and within this of the sender's centre hear it: the
    /// reach of a spotter's call (<see cref="AutonomousRvrPerception.SpotterRadius"/>).</summary>
    public const int RelayRadius = AutonomousRvrPerception.SpotterRadius;

    // ---- chasing (design values) ---------------------------------------------------------------------------------------
    /// <summary>A member stops chasing a fleeing enemy this far from its group's centre, by tier.</summary>
    public const int CasualLeash = 2_500, AverageLeash = 2_000, VeteranLeash = 1_500;
    /// <summary>A bold bot (boldness at least <see cref="BoldChaser"/>) chases this many times as far: into the enemy's guards.</summary>
    public const double BoldLeashFactor = 2.5;
    public const double BoldChaser = 0.75;
    /// <summary>An enemy this close to the group is on it, whatever the stance.</summary>
    public const int UponGroupRadius = 350;

    // ---- the wipe rule (lifted from AutonomousRvrDirector.Choose) ------------------------------------------------------
    /// <summary>A force with this many recent wipes regroups before it goes hunting again.</summary>
    public const int RepeatedWipes = 3;

    private const ulong WatchSalt = 0x57415443, MisreadSalt = 0x4D495352, ReactSalt = 0x52454143, HoldoutSalt = 0x484F4C44,
        MoveOnSalt = 0x4D4F5645;

    /// <summary>A roll from 0 to 1 (exclusive) fixed by two numbers. Pure.</summary>
    public static double Roll(long a, long b, ulong salt) =>
        AutonomousBotPersonality.Mix(a ^ unchecked((long)AutonomousBotPersonality.Mix(b, salt)), salt) % 1_000_000 / 1_000_000.0;

    // ---- the odds ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A side's effective power: (heads, mezzed and stunned ones at <see cref="ControlledValue"/>, plus pets at
    /// <see cref="PetValue"/>) x level weight x health weight x power weight x healer bonus. Never decreases when a head,
    /// a level, health, power or a healer is added. Pure.
    /// </summary>
    public static double Power(RvrStrength side)
    {
        if (side == null || side.Count + side.Pets <= 0)
            return 0;
        double heads = side.Count - (1 - ControlledValue) * Math.Clamp(side.Controlled, 0, side.Count) + PetValue * side.Pets;
        double level = Math.Pow(Math.Clamp(side.Count == 0 ? 1 : (double)side.TotalLevels / side.Count, 1, 50) / 50.0, LevelExponent);
        double health = HealthFloor + (1 - HealthFloor) * Math.Clamp(side.HealthPercent, 0, 100) / 100.0;
        double powerShare = side.Count == 0 ? 0 : Math.Clamp((double)side.PowerUsers / side.Count, 0, 1);
        double power = 1 - powerShare * (1 - PowerFloor) * (1 - Math.Clamp(side.PowerPercent, 0, 100) / 100.0);
        double healers = 1 + HealerBonus * Math.Clamp(side.Healers, 0, MaxCountedHealers);
        return heads * level * health * power * healers;
    }

    /// <summary>The friendly share of the power on the field, 0 to 1: 0.5 for even sides, 1 with no enemy. Pure.</summary>
    public static double Odds(double friendlyPower, double enemyPower)
    {
        friendlyPower = Math.Max(0, friendlyPower);
        enemyPower = Math.Max(0, enemyPower);
        if (enemyPower <= 0)
            return 1;
        return friendlyPower / (friendlyPower + enemyPower);
    }

    public static double Odds(RvrStrength friendly, RvrStrength enemy) => Odds(Power(friendly), Power(enemy));

    /// <summary>How far a tier misreads the odds (the amplitude). Pure.</summary>
    public static double Misread(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => CasualMisread,
        RvrSkillTier.Veteran => VeteranMisread,
        _ => AverageMisread,
    };

    /// <summary>The engage threshold for a group: wipes make it careful, a bold leader brave. Pure.</summary>
    public static double EngageThreshold(int recentWipes, double boldness) =>
        EngageOdds + WipeCaution * Math.Clamp(recentWipes, 0, MaxCautionWipes) - BoldEngageSwing * (2 * Math.Clamp(boldness, 0, 1) - 1);

    /// <summary>The retreat threshold for a group (see <see cref="EngageThreshold"/>). Pure.</summary>
    public static double RetreatThreshold(int recentWipes, double boldness) =>
        RetreatOdds + WipeCaution * Math.Clamp(recentWipes, 0, MaxCautionWipes) - BoldRetreatSwing * (2 * Math.Clamp(boldness, 0, 1) - 1);

    /// <summary>How long the group watches before it decides: its tier's base x (0.5 + patience / 9) x 0.8-1.2. Pure.</summary>
    public static long WatchMilliseconds(RvrSkillTier tier, int patience, double roll)
    {
        long baseWatch = tier switch
        {
            RvrSkillTier.Casual => CasualWatchMilliseconds,
            RvrSkillTier.Veteran => VeteranWatchMilliseconds,
            _ => AverageWatchMilliseconds,
        };
        double factor = (0.5 + Math.Clamp(patience, 0, 9) / 9.0) * (0.8 + 0.4 * Math.Clamp(roll, 0, 1));
        return Math.Min(MaxWatchMilliseconds, (long)Math.Round(baseWatch * factor));
    }

    /// <summary>How long this group watches an encounter whose watch began at <paramref name="since"/>. Pure.</summary>
    public static long WatchMilliseconds(RvrEngagementFacts facts, RvrSkillTier tier, long since)
    {
        facts ??= RvrEngagementFacts.Empty;
        return WatchMilliseconds(tier, facts.Patience, Roll(facts.GroupKey, since, WatchSalt));
    }

    /// <summary>
    /// Sizes up a board: both strengths and powers, the odds as they are and as the group reads them, the fight's shape
    /// and the group's spread. With a fight between the other two realms in view, the side the group would face is the
    /// stronger of the two. Pure.
    /// </summary>
    public static RvrSizeUp SizeUp(RvrFightBoard board, RvrEngagementFacts facts, RvrSkillTier tier)
    {
        facts ??= RvrEngagementFacts.Empty;
        RvrStrength friendly = board?.Friendly ?? RvrStrength.None;
        RvrStrength enemy = board?.Enemy ?? RvrStrength.None;
        bool thirdParty = board?.ThirdPartyFight == true;
        double enemyPower = Power(enemy);
        if (thirdParty)
        {
            RvrStrength strongest = null;
            double best = -1;
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                if (realm == board.Realm)
                    continue;
                RvrStrength side = RvrStrength.Of(board.Enemies, realm);
                double power = Power(side);
                if (power > best)
                {
                    best = power;
                    strongest = side;
                }
            }
            if (strongest != null && strongest.Count + strongest.Pets > 0)
            {
                enemy = strongest;
                enemyPower = best;
            }
        }
        double friendlyPower = Power(friendly);
        double odds = Odds(friendlyPower, enemyPower);
        double misread = (2 * Roll(facts.GroupKey, facts.EncounterSince, MisreadSalt) - 1) * Misread(tier);

        int heads = 0, close = 0;
        Vector3 centre = board?.FriendlyCentroid ?? default;
        if (board != null)
            foreach (RvrCombatant member in board.Members)
            {
                if (member == null || member.IsPet)
                    continue;
                heads++;
                if (Flat(member.X - centre.X, member.Y - centre.Y) <= HoldRadius)
                    close++;
            }
        double cohesion = heads == 0 ? 1 : (double)close / heads;
        Vector3? enemyCentre = board?.EnemyCentroid;
        return new RvrSizeUp
        {
            Friendly = friendly,
            Enemy = enemy,
            FriendlyPower = friendlyPower,
            EnemyPower = enemyPower,
            Odds = odds,
            PerceivedOdds = Math.Clamp(odds + misread, 0, 1),
            EnemyInView = enemy.Count + enemy.Pets > 0,
            InContact = board?.InContact == true,
            ThirdParty = thirdParty,
            EnemyBigger = enemy.Count > friendly.Count,
            KiteMelee = enemy.Count >= MeleeHeavyMinHeads && enemy.MeleeShare >= MeleeHeavyShare && friendly.RangedShare >= KiteRangedShare,
            Cohesion = cohesion,
            StrungOut = heads >= HoldMinMembers && cohesion < HoldCohesion,
            EnemyDistance = enemyCentre is { } e ? Flat(e.X - centre.X, e.Y - centre.Y) : double.PositiveInfinity,
            FriendlyCentre = centre,
            EnemyCentre = enemyCentre,
        };
    }

    private static double Flat(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    // ---- the stance machine --------------------------------------------------------------------------------------------

    /// <summary>
    /// The group's next stance from its current one (taken at <paramref name="since"/>), a size-up and its stored facts.
    /// Hysteresis: a stance taken less than <see cref="MinStanceMilliseconds"/> ago stays, unless contact ends a watch,
    /// a hold or a regroup, or the stance is an Engage the group never chose (the neutral start: it has not decided on
    /// this encounter yet). Pure.
    /// </summary>
    public static RvrStanceDecision Decide(RvrStance current, long since, in RvrSizeUp s, RvrEngagementFacts facts,
        RvrGroupState state, RvrEngagementOptions options, RvrSkillTier tier, long now)
    {
        facts ??= RvrEngagementFacts.Empty;
        state ??= RvrGroupState.Empty;
        RvrStanceDecision want = Want(current, since, s, facts, state, options, tier, now);
        if (want.Stance == current)
            return want;
        bool undecided = current == RvrStance.Engage && since <= facts.EncounterSince;
        bool forced = s.InContact && current is RvrStance.Observe or RvrStance.Hold or RvrStance.Regroup;
        if (now - since < MinStanceMilliseconds && !undecided && !forced)
            return new RvrStanceDecision(current, "held", current == RvrStance.Regroup ? RegroupRally(facts, state, tier, s) : null);
        return want;
    }

    private static RvrStanceDecision Want(RvrStance current, long since, in RvrSizeUp s, RvrEngagementFacts f,
        RvrGroupState state, RvrEngagementOptions o, RvrSkillTier tier, long now)
    {
        bool aware = s.InContact || s.EnemyInView && now - f.LastNoticedAt <= AwareMilliseconds;
        bool coolingDown = now - f.LastRetreatAt < RetreatCooldownMilliseconds;
        int wipes = state.WipesWithin(now);

        // A retreat or regroup another group of the warband passed on (newer than the current stance).
        if (f.Relay is { } relay && relay.AppliesAt <= now && now < relay.ExpiresAt && relay.AppliesAt > since &&
            current is not (RvrStance.Retreat or RvrStance.Regroup))
        {
            if (relay.Stance == RvrStance.Retreat && aware && o.Retreat && !coolingDown)
                return new(RvrStance.Retreat, "relayed retreat");
            // A group in contact fights its own fight (below): a relayed regroup in contact is ended by Decide's forced
            // rule on the next board, so it would only be a 1-second start (owner's PC log on 5378dbe, 2026-11-17/18:
            // 123 of 347 relay-started regroups began in contact, 121 lasted 1 s). The relay stays valid until it
            // expires, so a group whose contact ends in time still regroups.
            if (o.Regroup && !s.InContact)
                return new(RvrStance.Regroup, "relayed " + (relay.Stance == RvrStance.Retreat ? "retreat" : "regroup"), s.FriendlyCentre);
        }

        if (current == RvrStance.Retreat)
        {
            if (!o.Retreat)
                return new(RvrStance.Engage, "retreat switched off");
            if (now - since >= MaxRetreatMilliseconds || !aware || s.EnemyDistance >= RetreatClearDistance)
                return o.Regroup
                    ? new(RvrStance.Regroup, "retreat over", RegroupRally(f, state, tier, s), Relays: true)
                    : new(RvrStance.Engage, "retreat over");
            return new(RvrStance.Retreat, "retreating");
        }

        if (current == RvrStance.Regroup)
        {
            if (s.InContact)
                return Fight(s, f, wipes, o, coolingDown, "contact while regrouping");
            if (!o.Regroup)
                return new(RvrStance.Engage, "regroup switched off");
            if (now - since < MinRegroupMilliseconds(tier))
                return new(RvrStance.Regroup, "regrouping", RegroupRally(f, state, tier, s));
            if (now - since >= MaxRegroupMilliseconds)
                return new(RvrStance.Engage, "regroup timed out");
            if (Rested(s.Friendly) && !s.StrungOut)
                return new(RvrStance.Engage, "regrouped");
            return new(RvrStance.Regroup, "regrouping", RegroupRally(f, state, tier, s));
        }

        // Nothing in view: Regroup is only for after a retreat (above) or a fight that hurt (health under
        // AfterFightHealth or power under AfterFightPower). A group at or above both goes back to Engage, the neutral
        // stance under which it travels and roams on (the travel hold lets go of it).
        if (!aware)
        {
            if (current is RvrStance.Engage or RvrStance.Kite && o.Regroup && f.LastContactAt >= since &&
                now - f.LastContactAt <= AfterFightMilliseconds && NeedsRest(s.Friendly))
                return new(RvrStance.Regroup, "after the fight", AfterFightRally(f, tier), Relays: true);
            return new(RvrStance.Engage, "nothing in view");
        }

        // An enemy is known. Before the group has chosen to fight this encounter, it holds and watches.
        bool undecided = current is RvrStance.Observe or RvrStance.Hold ||
                         current == RvrStance.Engage && since <= f.EncounterSince;
        if (!s.InContact && undecided)
        {
            if (o.Regroup && s.StrungOut && current != RvrStance.Observe &&
                !(current == RvrStance.Hold && now - since >= MaxHoldMilliseconds))
                return new(RvrStance.Hold, "strung out");
            if (o.SizeUp && (s.Enemy.Count >= EnemyGroupHeads || s.ThirdParty))
            {
                if (current != RvrStance.Observe)
                    return new(RvrStance.Observe, s.ThirdParty ? "third-party fight in view" : "enemy group in view");
                if (now - since < WatchMilliseconds(f, tier, since))
                    return new(RvrStance.Observe, "watching");
                return Fight(s, f, wipes, o, coolingDown, "watched");
            }
        }
        return Fight(s, f, wipes, o, coolingDown, s.InContact ? "in contact" : "sized up");
    }

    /// <summary>Engage, Kite or Retreat on a size-up. Pure.</summary>
    public static RvrStanceDecision Fight(in RvrSizeUp s, RvrEngagementFacts f, int recentWipes, RvrEngagementOptions o,
        bool retreatCoolingDown, string why)
    {
        f ??= RvrEngagementFacts.Empty;
        double odds = s.PerceivedOdds;
        bool lost = s.InContact && s.Friendly.Healers == 0 && s.Enemy.Healers > 0 && odds < LostOdds;
        return FightStance(odds, lost, s.KiteMelee, o, retreatCoolingDown, recentWipes, f.Boldness, why);
    }

    /// <summary>
    /// The fight stance for odds as the group reads them: Retreat below the retreat threshold (or a lost fight), Kite
    /// below the engage threshold or against a melee-heavy enemy, else Engage. A group that just ran fights instead of
    /// running again. Pure.
    /// </summary>
    public static RvrStanceDecision FightStance(double perceivedOdds, bool lost, bool kiteMelee, RvrEngagementOptions o,
        bool retreatCoolingDown, int recentWipes, double boldness, string why)
    {
        if (o.Retreat && !retreatCoolingDown && (perceivedOdds < RetreatThreshold(recentWipes, boldness) || lost))
            return new(RvrStance.Retreat, lost ? why + ": fight lost" : why + ": bad odds", Relays: true);
        if (o.Kite && (perceivedOdds < EngageThreshold(recentWipes, boldness) || kiteMelee && perceivedOdds < KiteMeleeMaxOdds))
            return new(RvrStance.Kite, kiteMelee ? why + ": melee-heavy enemy" : why + ": odds below fair");
        return new(RvrStance.Engage, why + ": fair odds");
    }

    /// <summary>The regroup's rally point: the stored one (a retreat's end), else the after-fight rest spot, else here. Pure.</summary>
    public static Vector3 RegroupRally(RvrEngagementFacts f, RvrGroupState state, RvrSkillTier tier, in RvrSizeUp s)
    {
        if (state?.RallyPoint is { } rally)
            return rally;
        if (f != null && f.LastContactAt > long.MinValue / 2)
            return AfterFightRally(f, tier);
        return s.FriendlyCentre;
    }

    /// <summary>Where a group rests after its fight: by tier, the spot itself or a spot away from the enemy. Pure.</summary>
    public static Vector3 AfterFightRally(RvrEngagementFacts f, RvrSkillTier tier) =>
        AfterFightRestPoint(f.LastFightPoint, f.LastEnemyPoint, MovesOnAfterFight(tier, Roll(f.GroupKey, f.LastContactAt, MoveOnSalt)));

    /// <summary>The chance a group of this tier leaves the fight's spot to rest. Pure.</summary>
    public static double MoveOnShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => CasualMovesOn,
        RvrSkillTier.Veteran => VeteranMovesOn,
        _ => AverageMovesOn,
    };

    public static bool MovesOnAfterFight(RvrSkillTier tier, double roll) => roll < MoveOnShare(tier);

    /// <summary>
    /// The rest spot after a fight: the fight's spot for a group that stays, else <paramref name="distance"/> on, away
    /// from where the enemy stood (any way when that is unknown). Pure.
    /// </summary>
    public static Vector3 AfterFightRestPoint(Vector3 fight, Vector3? enemy, bool movesOn, int distance = AfterFightMoveDistance)
    {
        if (!movesOn)
            return fight;
        Vector2 away = enemy is { } e ? new Vector2(fight.X - e.X, fight.Y - e.Y) : new Vector2(1, 0);
        if (away.LengthSquared() < 1)
            away = new Vector2(1, 0);
        Vector2 point = new Vector2(fight.X, fight.Y) + Vector2.Normalize(away) * distance;
        return new Vector3(point.X, point.Y, fight.Z);
    }

    /// <summary>The group is rested enough to go back out. Pure.</summary>
    public static bool Rested(RvrStrength friendly) =>
        friendly == null || friendly.Count == 0 ||
        friendly.HealthPercent >= RegroupHealth && (friendly.PowerUsers == 0 || friendly.PowerPercent >= RegroupPower);

    /// <summary>The group needs a rest after its fight. Pure.</summary>
    public static bool NeedsRest(RvrStrength friendly) =>
        friendly != null && friendly.Count > 0 &&
        (friendly.HealthPercent < AfterFightHealth || friendly.PowerUsers > 0 && friendly.PowerPercent < AfterFightPower);

    /// <summary>
    /// Work package 4's step of the fight plan (RvrFightPlanner.PlanStance): the stance and, for a regroup, the rally
    /// point. A solo bot (fewer than two members known) and switched-off settings keep the neutral Engage. Pure.
    /// </summary>
    public static RvrFightPlan PlanStance(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now, bool groupRetreatEnabled)
    {
        if (plan == null || board == null)
            return plan;
        state ??= RvrGroupState.Empty;
        RvrEngagementFacts facts = state.Engagement ?? RvrEngagementFacts.Empty;
        RvrEngagementOptions options = RvrEngagementOptions.From(switches, groupRetreatEnabled);
        if (!options.Any || facts.GroupSize < 2)
            return Neutral(plan, now, "no stance");
        // One stance per group: a board of members away from the group's main body (another region) has none.
        if (!board.MainBody)
            return Neutral(plan, now, "away from the main body");
        RvrSizeUp sizeUp = SizeUp(board, facts, plan.SkillTier);
        RvrStanceDecision decision = Decide(plan.Stance, plan.StanceSince, sizeUp, facts, state, options, plan.SkillTier, now);
        if (decision.Stance != plan.Stance)
            plan = plan with
            {
                Stance = decision.Stance, StanceSince = now, StanceReason = decision.Reason,
                StanceRelays = decision.Relays && !EndsRelayedRetreat(plan, decision),
            };
        if (decision.Stance == RvrStance.Regroup && decision.Rally is { } rally)
            plan = plan with { RallyPoint = rally, RallyRegion = board.RegionId };
        return plan;
    }

    /// <summary>
    /// The regroup that ends a relayed retreat is part of that relayed stance and is not passed on: the group that fought
    /// already told the warband. Without this a relayed retreat's "retreat over" relayed again, so one fight reached a
    /// second ring of groups and echoed back to the group that started it (owner's PC log on 1.2 test build 453e01a,
    /// 11:33-14:00: 42 of the 75 "retreat over" relays ended a relayed retreat). Pure.
    /// </summary>
    public static bool EndsRelayedRetreat(RvrFightPlan before, RvrStanceDecision next) =>
        before != null && before.Stance == RvrStance.Retreat && next.Stance == RvrStance.Regroup &&
        (before.StanceReason ?? string.Empty).StartsWith("relayed", StringComparison.Ordinal);

    private static RvrFightPlan Neutral(RvrFightPlan plan, long now, string reason) =>
        plan.Stance == RvrStance.Engage ? plan
            : plan with { Stance = RvrStance.Engage, StanceSince = now, StanceReason = reason, StanceRelays = false };

    /// <summary>How long a regroup lasts at least, by tier, unless contact ends it. Pure.</summary>
    public static long MinRegroupMilliseconds(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => CasualMinRegroupMilliseconds,
        RvrSkillTier.Veteran => VeteranMinRegroupMilliseconds,
        _ => AverageMinRegroupMilliseconds,
    };

    // ---- one stance per group ------------------------------------------------------------------------------------------

    /// <summary>
    /// A group's main body: the region holding most of its living members; on a tie the leader's region, and when the
    /// leader is in none of the tied regions (or dead), the lowest region id. Region 0 and no members when none is alive. Pure.
    /// </summary>
    public static RvrGroupBody MainBody(IEnumerable<RvrMemberSpot> alive)
    {
        var counts = new Dictionary<ushort, (int Count, double X, double Y, bool Leader)>();
        if (alive != null)
            foreach (RvrMemberSpot spot in alive)
            {
                counts.TryGetValue(spot.Region, out var c);
                counts[spot.Region] = (c.Count + 1, c.X + spot.X, c.Y + spot.Y, c.Leader || spot.Leader);
            }
        ushort best = 0;
        (int Count, double X, double Y, bool Leader) top = default;
        bool any = false;
        foreach (KeyValuePair<ushort, (int Count, double X, double Y, bool Leader)> pair in counts)
        {
            var c = pair.Value;
            bool better = !any || c.Count > top.Count ||
                          c.Count == top.Count && (c.Leader && !top.Leader || c.Leader == top.Leader && pair.Key < best);
            if (!better)
                continue;
            any = true;
            best = pair.Key;
            top = c;
        }
        return any ? new RvrGroupBody(best, new Vector2((float)(top.X / top.Count), (float)(top.Y / top.Count)), top.Count) : default;
    }

    /// <summary>
    /// The group's runtime acting on a board: only a board of the group's main body that is newer than the last one acted
    /// on counts; any other board (a member in another region, an older build) changes nothing. Pure.
    /// </summary>
    public static RvrStanceStep Track(RvrStanceLedger ledger, RvrFightBoard board)
    {
        ledger ??= RvrStanceLedger.Empty;
        if (board?.Plan == null || !board.MainBody || board.BuiltAt <= ledger.LastBuild)
            return new RvrStanceStep(ledger, false, false, ledger.Stance, ledger.Since);
        RvrFightPlan plan = board.Plan;
        bool changed = plan.Stance != ledger.Stance ||
                       plan.Stance == RvrStance.Retreat && plan.StanceSince != ledger.Since && ledger.Since != long.MinValue;
        RvrStanceLedger next = ledger with { LastBuild = board.BuiltAt, Stance = plan.Stance, Since = plan.StanceSince, Region = board.RegionId };
        return new RvrStanceStep(next, true, changed, ledger.Stance, ledger.Since);
    }

    /// <summary>
    /// Whether a group that just started a stance passes it on: only for a real reason (<see cref="RvrStanceDecision.Relays"/>:
    /// never a relayed stance, never "nothing in view"), and at most once per <see cref="RelayWindowMilliseconds"/>. Pure.
    /// </summary>
    public static bool MayRelay(bool startRelays, long lastRelayAt, long now) =>
        startRelays && now - lastRelayAt >= RelayWindowMilliseconds;

    /// <summary>Whether a relay from a group's main body (region, centre) reaches another group: same region, within <see cref="RelayRadius"/>. Pure.</summary>
    public static bool RelayReaches(ushort fromRegion, Vector2 from, RvrGroupBody to) =>
        to.Members > 0 && to.Region == fromRegion && Vector2.Distance(from, to.Centre) <= RelayRadius;

    /// <summary>The step as the planner calls it: bot_veteran_retreat read from <see cref="BotGroupRetreat.Enabled"/>.</summary>
    public static RvrFightPlan PlanStance(RvrFightPlan plan, RvrFightBoard board, RvrGroupState state,
        RvrTacticsSwitches switches, long now) =>
        PlanStance(plan, board, state, switches, now, BotGroupRetreat.Enabled);

    // ---- what the stance lets a member do ------------------------------------------------------------------------------

    /// <summary>
    /// Whether a member may take this enemy as a new target under the group's stance. A bot that is hit always may (it
    /// fights back, whatever the stance). Engage and Kite take anything; a watching, holding or regrouping group takes
    /// only an enemy that is on it; a retreating group takes nothing. Pure.
    /// </summary>
    public static bool MayTakeTarget(RvrStance stance, bool hit, bool enemyUponGroup) =>
        hit || stance is RvrStance.Engage or RvrStance.Kite ||
        enemyUponGroup && stance is RvrStance.Observe or RvrStance.Hold or RvrStance.Regroup;

    /// <summary>A bot that is hit fights back, whatever the stance. Pure.</summary>
    public static bool FightsBack(RvrStance stance, bool hit) => hit || MayTakeTarget(stance, false, false);

    /// <summary>The bot was hit by an enemy player or bot in the last <see cref="HitMemoryMilliseconds"/>. Pure.</summary>
    public static bool WasHit(long now, long lastHitPvp) => lastHitPvp > 0 && now - lastHitPvp <= HitMemoryMilliseconds;

    // ---- ragged retreats -----------------------------------------------------------------------------------------------

    /// <summary>The share of a tier's members that never run. Pure.</summary>
    public static double HoldoutShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => CasualHoldouts,
        RvrSkillTier.Veteran => VeteranHoldouts,
        _ => AverageHoldouts,
    };

    /// <summary>A tier's reaction to a retreat call (min, max ms). Pure.</summary>
    public static (int Min, int Max) ReactionRange(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => (CasualReactMin, CasualReactMax),
        RvrSkillTier.Veteran => (VeteranReactMin, VeteranReactMax),
        _ => (AverageReactMin, AverageReactMax),
    };

    /// <summary>How one member reacts to one retreat call: after how long, or never. Fixed by the member and the call. Pure.</summary>
    public static (int DelayMilliseconds, bool HoldsOut) RetreatReaction(RvrSkillTier tier, long member, long call)
    {
        (int min, int max) = ReactionRange(tier);
        int delay = min + (int)Math.Round((max - min) * Roll(member, call, ReactSalt));
        return (delay, Roll(member, call, HoldoutSalt) < HoldoutShare(tier));
    }

    /// <summary>
    /// Whether a member runs on a retreat call published at <paramref name="publishedAt"/>: once its reaction delay is
    /// over, unless it is one that never runs, and not while it is being hit (it fights back). Pure.
    /// </summary>
    public static bool HeedsRetreat(long now, long publishedAt, (int DelayMilliseconds, bool HoldsOut) reaction, bool hit) =>
        !hit && !reaction.HoldsOut && now - publishedAt >= reaction.DelayMilliseconds;

    // ---- the warband relay ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A retreat or regroup passed on to another group of the warband: half the time (a roll from 0 to 1 below
    /// <see cref="RelayChance"/>), arriving <see cref="RelayDelayMilliseconds"/> later. Other stances are not passed on.
    /// <paramref name="from"/> is the sending group's log name. Pure.
    /// </summary>
    public static RvrStanceRelay? RelayTo(RvrStance stance, long now, double roll, string from = null)
    {
        if (stance is not (RvrStance.Retreat or RvrStance.Regroup) || roll >= RelayChance)
            return null;
        long at = now + RelayDelayMilliseconds;
        return new RvrStanceRelay(stance, at, at + RelayValidMilliseconds, from);
    }

    // ---- chasing -------------------------------------------------------------------------------------------------------

    /// <summary>How far from its group's centre a member chases a fleeing enemy: by tier, much further for a bold bot. Pure.</summary>
    public static int ChaseLeash(RvrSkillTier tier, double boldness)
    {
        int leash = tier switch
        {
            RvrSkillTier.Casual => CasualLeash,
            RvrSkillTier.Veteran => VeteranLeash,
            _ => AverageLeash,
        };
        return boldness >= BoldChaser ? (int)Math.Round(leash * BoldLeashFactor) : leash;
    }

    /// <summary>
    /// Whether a chasing member gives up: it is beyond its leash from its group's centre and the enemy is further out
    /// still (running away). A bot that is hit never stops: it fights back. Pure.
    /// </summary>
    public static bool StopsChasing(double botFromGroup, double enemyFromGroup, int leash, bool hit) =>
        !hit && botFromGroup > leash && enemyFromGroup >= botFromGroup;

    // ---- wipes ---------------------------------------------------------------------------------------------------------

    /// <summary>The wipe rule of AutonomousRvrDirector.Choose: a force that wiped this often lately regroups first. Pure.</summary>
    public static bool WipedRepeatedly(int recentWipes) => recentWipes >= RepeatedWipes;

    /// <summary>Records a wipe of a bot group (the Group object) in its stored facts. Lock-free; safe under the coordinator's lock.</summary>
    public static void RecordWipe(object group, long now)
    {
        if (group != null && Properties.RVR_BOT_FIELD_TACTICS)
            RvrFightBoards.UpdateState(group, state => state.WithWipe(now));
    }

    /// <summary>A group's recent wipes (the last ten minutes) for the event layer's Force.</summary>
    public static int RecentWipes(object group, long now) =>
        group == null ? 0 : RvrFightBoards.StateOf(group).WipesWithin(now);
}
