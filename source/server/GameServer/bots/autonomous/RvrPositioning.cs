using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace DOL.GS;

/// <summary>Where a member stands in an open-field fight (work package 3).</summary>
public enum RvrStand : byte
{
    /// <summary>Not placed: stealthers, and anyone the rules do not move.</summary>
    None,
    /// <summary>Tanks: they fight at the front (not moved).</summary>
    Front,
    /// <summary>Melee damage and melee hybrids: they fight beside the tanks (not moved).</summary>
    Side,
    /// <summary>Healers, casters, archers, Shamans and Bards: behind the tanks, spread out.</summary>
    Back,
    /// <summary>A shield tank guarding a named member: beside his charge, toward the enemy.</summary>
    Escort,
}

/// <summary>A human error in a member's place (work package 3, by skill tier).</summary>
[Flags]
public enum RvrSlotError : byte
{
    None = 0,
    /// <summary>A healer stands too close to another healer (one mez can catch both).</summary>
    TooClose = 1,
    /// <summary>A healer charges with the tanks instead of holding back.</summary>
    Charge = 2,
}

/// <summary>One member's place in the layout.</summary>
public readonly record struct RvrSlot(GameLiving Member, RvrStand Stand, Vector2 Spot, bool Healer, RvrSlotError Error);

/// <summary>Why a member walks (logged with RVR_BACKLINE).</summary>
public enum RvrMoveReason : byte
{
    None,
    /// <summary>It stood in front of the tanks' line, or level with it.</summary>
    Backline,
    /// <summary>It stood within the mez spread of another backline member.</summary>
    Spread,
    /// <summary>It was far from its place (the line moved).</summary>
    Far,
    /// <summary>An enemy group is coming in and nobody is in contact yet: the group scatters.</summary>
    Incoming,
    /// <summary>A guard walks back to his charge.</summary>
    Escort,
    /// <summary>A healer runs up with the tanks (a Casual error).</summary>
    Charge,
}

/// <summary>Why a backline member of a kiting group does not step back now (logged with RVR_KITE).</summary>
public enum RvrKiteHeld : byte
{
    /// <summary>It steps back.</summary>
    None,
    /// <summary>No enemy melee fighter is free to chase: the group stands as in Engage.</summary>
    NoMelee,
    /// <summary>The nearest enemy melee fighter is at the stand-off distance or farther: it stands and casts or shoots.</summary>
    Clear,
    /// <summary>Every leg back would walk toward another enemy group.</summary>
    EnemyGroup,
    /// <summary>Every leg back would walk into the guards' range of an enemy keep or tower.</summary>
    KeepGuards,
    /// <summary>Every leg back is blocked on the mesh (a ledge, water, another floor, or no mesh).</summary>
    Blocked,
    /// <summary>A healer heals first: a group mate is low and the melee is not yet close.</summary>
    HealFirst,
}

/// <summary>A human error in one kite step (work package 3, by skill tier).</summary>
[Flags]
public enum RvrKiteError : byte
{
    None = 0,
    /// <summary>It backs off late: only once the melee fighter is nearly on it.</summary>
    Late = 1,
    /// <summary>It runs off at an angle, away from the others.</summary>
    WrongWay = 2,
    /// <summary>It runs too far.</summary>
    Overrun = 4,
}

/// <summary>What a melee member of a kiting group does (work package 3).</summary>
public enum RvrKitePeel : byte
{
    /// <summary>It fights its target (the target reached the backline, it is hit, or there is no backline).</summary>
    Fight,
    /// <summary>It walks back to its screen spot in front of the backline instead of chasing.</summary>
    Screen,
    /// <summary>It stands at its screen spot, facing the fight, and waits for the chasers.</summary>
    Stand,
}

/// <summary>One backline member's kite decision: where it steps (null: it does not), why not, its error, the step
/// length, its stand-off distance and the distance to the nearest enemy melee fighter.</summary>
public readonly record struct RvrKiteStep(Vector2? Spot, RvrKiteHeld Held, RvrKiteError Error, float Length, float StandOff,
    float MeleeDistance);

/// <summary>
/// Open-field RvR, work package 3 ("Hands", docs/RVR-FIELD-1.2.md): how a bot-only group stands like a 2003 group. Casters
/// and healers stand behind the tanks relative to the enemy (the role formation's ArcAngles/RoleSlots with "front" set
/// to the bearing of the enemy centroid); backline members keep more than 350 units apart so one mez cannot catch two
/// healers ("Keep more than 350 units apart", Uthgard guide 2011, retrospective); on "incoming" they scatter wider; a
/// healer with an enemy melee fighter on it kites ("run a bit, throw heal", FH Jan and Aug 2003, era-dated); and the
/// mistakes of each skill tier are made on purpose: healers too close together, healers charging with the tanks, a
/// ragged spread, a healer that does not kite.
///
/// Pure: positions in, places out; rolls come from fixed hashes of the member and a time window. The 350 figure is from
/// the research; every other number here (depths, tolerances, jitters, delays, error shares) is a design value.
/// </summary>
public static class RvrPositioning
{
    /// <summary>The 2003 spread: two healers closer than this invite one mez or root on both (Uthgard guide 2011).</summary>
    public const float MezSpread = 350f;
    /// <summary>The layout aims this much wider than <see cref="MezSpread"/>, so a small drift does not break it.</summary>
    public const float SpreadMargin = 30f;
    /// <summary>The spread on "incoming": wider, as players scattered before a charge.</summary>
    public const float ScatterSpread = 450f;
    /// <summary>The backline's spread depth behind the tanks' line: at least, and at most. A member with a spell reach
    /// stands deeper, by <see cref="DepthFor"/> (#531 step 2e).</summary>
    public const float MinBackDepth = 250f;
    public const float MaxBackDepth = 700f;
    /// <summary>
    /// #531 step 2e, caster depth (reference.md 6: "casters stand at 1,200-1,500 behind the tanks"; 3.2: nuke and mez
    /// 1,500). A back-line member with a spell reach stands this far inside it from the enemy, so its spells still land.
    /// </summary>
    public const float ReachMargin = 150f;
    /// <summary>The deepest a caster stands behind the line (a 1,500 nuke less the margin; a bolt reach is not used).</summary>
    public const float MaxCasterDepth = 1_350f;
    /// <summary>
    /// A healer stands about this far from the enemy (healers "stand behind the tanks and kite", reference.md 3.6; the 900
    /// keep-away of the #572 8v8 tunings, an emulator figure used as the starting value), and never farther from the line
    /// than its heal reaches.
    /// </summary>
    public const float HealerStandOff = 900f;
    /// <summary>A cover pick tries the place itself, then this many degrees round the line to each side, twice.</summary>
    public const double CoverStepDegrees = 12;
    /// <summary>A backline member closer than this behind the line, or in front of it, is out of place.</summary>
    public const float MinLineDepth = 120f;
    /// <summary>A member this far from its place walks to it even when nothing else is wrong.</summary>
    public const float FarFromSlot = 400f;
    /// <summary>A walk ends this close to its spot.</summary>
    public const float SlotTolerance = 60f;
    /// <summary>"Incoming": an enemy within this range of the group and nobody in contact yet.</summary>
    public const float IncomingRange = 2_500f;
    /// <summary>A guard's spot: this far from his charge, toward the enemy.</summary>
    public const float EscortOffset = 90f;
    /// <summary>A "too close" healer stands this far from the nearest other healer.</summary>
    public const float TooCloseMin = 150f;
    public const float TooCloseMax = 260f;
    /// <summary>Half the back arc, as RoleSlots lays it out (behind +- 50 degrees).</summary>
    public const double BackHalfSpread = Math.PI * 5 / 18;
    /// <summary>A member checks its place at most this often.</summary>
    public const long CheckMilliseconds = 1_000;
    /// <summary>A walk that has not ended by then is given up.</summary>
    public const long MoveTimeoutMilliseconds = 6_000;
    /// <summary>The errors of the layout are rolled again every this long.</summary>
    public const long ErrorWindowMilliseconds = 30_000;
    /// <summary>A walk is proven on the mesh in pieces of at most this length (each must keep to the bot's floor).</summary>
    public const float ChunkLength = 60f;
    /// <summary>The longest walk a member makes for its place.</summary>
    public const float MaxWalk = 900f;
    /// <summary>A healer kites this far (by tier) ...</summary>
    public const float KiteMinDistance = 220f;
    public const float KiteMaxDistance = 330f;
    /// <summary>... then heals at least this long before it kites again.</summary>
    public const long KiteHealWindowMinMilliseconds = 3_000;
    public const long KiteHealWindowMaxMilliseconds = 5_000;
    /// <summary>A healer walks for its place only while every member has at least this much health: heals first.</summary>
    public const int HealFirstPercent = 70;

    private const ulong TooCloseSalt = 0x52565033_544F4F43; // "RVP3TOOC"
    private const ulong ChargeSalt = 0x52565033_43485247;   // "RVP3CHRG"
    private const ulong JitterSalt = 0x52565033_4A495454;   // "RVP3JITT"
    private const ulong KiteSalt = 0x52565033_4B495445;     // "RVP3KITE"
    private const ulong DelaySalt = 0x52565033_44454C59;    // "RVP3DELY"

    // ---- who stands where ---------------------------------------------------------------------------------------------

    /// <summary>Where a class stands: tanks front, melee and melee hybrids beside them, healers, casters, archers,
    /// Shamans and Bards behind; stealthers are not placed.</summary>
    public static RvrStand StandFor(RvrRole role, eCharacterClass characterClass) => role switch
    {
        RvrRole.Tank => RvrStand.Front,
        RvrRole.Melee => RvrStand.Side,
        RvrRole.Healer or RvrRole.Caster or RvrRole.Archer => RvrStand.Back,
        RvrRole.Support => characterClass is eCharacterClass.Shaman or eCharacterClass.Bard ? RvrStand.Back : RvrStand.Side,
        _ => RvrStand.None,
    };

    /// <summary>Who counts as a healer for the spread: the main healers, Shamans and Bards.</summary>
    public static bool CountsAsHealer(RvrRole role, eCharacterClass characterClass) =>
        role == RvrRole.Healer || characterClass is eCharacterClass.Shaman or eCharacterClass.Bard;

    /// <summary>The bearing (radians, atan2 of world y and x) from one point to another.</summary>
    public static double Bearing(Vector2 from, Vector2 to) => Math.Atan2(to.Y - from.Y, to.X - from.X);

    /// <summary>The unit vector of a bearing.</summary>
    public static Vector2 Direction(double bearing) => new((float)Math.Cos(bearing), (float)Math.Sin(bearing));

    /// <summary>How far ahead of <paramref name="anchor"/> a point is, toward <paramref name="front"/> (negative: behind).</summary>
    public static float Ahead(Vector2 point, Vector2 anchor, double front) => Vector2.Dot(point - anchor, Direction(front));

    /// <summary>
    /// The tanks' line: the centre of the members who stand front or beside (tanks and melee). Null when the group has
    /// none (then nobody can stand behind them, and only the spread applies).
    /// </summary>
    public static Vector2? Line(IReadOnlyList<(Vector2 Position, RvrStand Stand)> members)
    {
        Vector2 sum = Vector2.Zero;
        int count = 0;
        foreach ((Vector2 position, RvrStand stand) in members)
            if (stand is RvrStand.Front or RvrStand.Side)
            {
                sum += position;
                count++;
            }
        return count == 0 ? null : sum / count;
    }

    // ---- the backline -----------------------------------------------------------------------------------------------

    /// <summary>
    /// The order of the backline along its arc: healers spread over it (one healer: the centre, straight behind; two or
    /// more: both ends and evenly between), the others in the gaps. The input order is kept among equals. Pure.
    /// </summary>
    public static int[] ArcOrder(IReadOnlyList<bool> healer)
    {
        int n = healer.Count;
        var order = new int[n];
        Array.Fill(order, -1);
        var healers = new List<int>();
        var others = new List<int>();
        for (int i = 0; i < n; i++)
            (healer[i] ? healers : others).Add(i);
        if (healers.Count == 1)
            order[(n - 1) / 2] = healers[0];
        else
            for (int j = 0; j < healers.Count; j++)
                order[(int)Math.Round(j * (n - 1) / (double)(healers.Count - 1))] = healers[j];
        int next = 0;
        for (int position = 0; position < n; position++)
            if (order[position] < 0)
                order[position] = others[next++];
        return order;
    }

    /// <summary>
    /// The backline's distance from the line: wide enough that neighbours on the arc stand <paramref name="spread"/>
    /// apart (healers at least, everyone where the depth allows), within <see cref="MinBackDepth"/> and
    /// <see cref="MaxBackDepth"/>. <paramref name="healerGapSteps"/> is the smallest number of arc steps between two
    /// healers (0: fewer than two). Pure.
    /// </summary>
    public static float BackRadius(int count, int healerGapSteps, float spread)
    {
        if (count <= 1)
            return MinBackDepth;
        double step = 2 * BackHalfSpread / (count - 1);
        double all = spread / (2 * Math.Sin(step / 2));
        double healers = healerGapSteps > 0 ? spread / (2 * Math.Sin(Math.Min(Math.PI, healerGapSteps * step) / 2)) : 0;
        // past MaxBackDepth the push apart (EnforceSpread) makes the rest of the spread
        return (float)Math.Clamp(all, Math.Min(Math.Max(MinBackDepth, healers), MaxBackDepth), MaxBackDepth);
    }

    /// <summary>
    /// Pushes the given points apart until every pair of the included ones is at least <paramref name="spread"/> apart
    /// (or a pass limit is reached), each pair moving half the shortfall along the line between them. Pure (changes only
    /// the array it is given).
    /// </summary>
    public static void EnforceSpread(Vector2[] points, IReadOnlyList<bool> included, float spread, double front)
    {
        Vector2 side = Direction(front + Math.PI / 2);
        for (int pass = 0; pass < 32; pass++)
        {
            bool moved = false;
            for (int i = 0; i < points.Length; i++)
            {
                if (!included[i])
                    continue;
                for (int j = i + 1; j < points.Length; j++)
                {
                    if (!included[j])
                        continue;
                    Vector2 apart = points[j] - points[i];
                    float distance = apart.Length();
                    if (distance >= spread)
                        continue;
                    Vector2 push = distance < 1f ? side : apart / distance;
                    float half = (spread - distance) / 2 + 0.5f;
                    points[i] -= push * half;
                    points[j] += push * half;
                    moved = true;
                }
            }
            if (!moved)
                return;
        }
    }

    /// <summary>The ragged spread of a tier: how far (radially, in units) and how much (degrees) a place may be off.
    /// Casual 120 / 14, Average 70 / 8, Veteran 35 / 4. Design values.</summary>
    public static (float Radial, double Degrees) Ragged(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => (120f, 14),
        RvrSkillTier.Veteran => (35f, 4),
        _ => (70f, 8),
    };

    /// <summary>The share of healers standing too close to another healer in each window: Casual 30%, Average 10%,
    /// Veteran 3%. Design values (2003 players mocked it).</summary>
    public static double TooCloseShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.30,
        RvrSkillTier.Veteran => 0.03,
        _ => 0.10,
    };

    /// <summary>The share of healers charging with the tanks in each window ("healers charging", FH Jan 2003): Casual
    /// 15%, Average 4%, Veteran 1%. Design values.</summary>
    public static double ChargeShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.15,
        RvrSkillTier.Veteran => 0.01,
        _ => 0.04,
    };

    /// <summary>The share of melee contacts a healer does not kite (it stands and is interrupted): Casual 60%, Average
    /// 30%, Veteran 10%. Design values.</summary>
    public static double KiteSkipShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.60,
        RvrSkillTier.Veteran => 0.10,
        _ => 0.30,
    };

    /// <summary>This healer's errors in the window holding <paramref name="now"/>. Pure.</summary>
    public static RvrSlotError Errors(RvrSkillTier tier, long memberId, long now, bool healer)
    {
        if (!healer)
            return RvrSlotError.None;
        long window = Math.DivRem(now, ErrorWindowMilliseconds).Quotient;
        RvrSlotError errors = RvrSlotError.None;
        if (RvrPeelPlan.Roll(memberId, window, ChargeSalt) < ChargeShare(tier))
            errors |= RvrSlotError.Charge;
        else if (RvrPeelPlan.Roll(memberId, window, TooCloseSalt) < TooCloseShare(tier))
            errors |= RvrSlotError.TooClose;
        return errors;
    }

    /// <summary>One member of the layout: its record on the board, where it stands, and the guard's charge if any.</summary>
    /// <summary>A member and where it stands. Reach (#531 step 2e): a back-line member's spell reach (a
    /// caster's longest damage or control spell, a healer's heal); 0 keeps the layout's own depth.</summary>
    public readonly record struct Placed(RvrCombatant Member, RvrStand Stand, GameLiving Charge = null, float Reach = 0);

    /// <summary>
    /// #531 step 2e: how far behind the tanks' line a back-line member stands. <paramref name="radius"/> is the layout's
    /// depth (<see cref="BackRadius"/>, which keeps the mez spread) and the least it gets; <paramref name="gap"/> is how far
    /// the enemy is ahead of the line (0 in melee contact). A caster stands its reach less <see cref="ReachMargin"/> from
    /// the enemy, at most <see cref="MaxCasterDepth"/> behind the line; a healer <see cref="HealerStandOff"/> from the
    /// enemy, no deeper than its heal reaches the line. No reach: <paramref name="radius"/>, as before. Pure.
    /// </summary>
    public static float DepthFor(bool healer, float reach, float gap, float radius)
    {
        if (reach <= 0)
            return radius;
        gap = Math.Max(0, gap);
        float want = healer ? HealerStandOff - gap : reach - ReachMargin - gap;
        float most = healer ? reach - ReachMargin : Math.Min(reach - ReachMargin, MaxCasterDepth);
        return Math.Max(radius, Math.Min(want, most));
    }

    /// <summary>
    /// #531 step 2e, the healer's cover pick: the places it tries, in order: its own, then <see cref="CoverStepDegrees"/>
    /// and twice that round <paramref name="line"/> to each side, at the same depth. Pure.
    /// </summary>
    public static Vector2[] CoverCandidates(Vector2 spot, Vector2 line)
    {
        Vector2 arm = spot - line;
        var result = new Vector2[5];
        result[0] = spot;
        int k = 1;
        foreach (double degrees in new[] { CoverStepDegrees, -CoverStepDegrees, 2 * CoverStepDegrees, -2 * CoverStepDegrees })
        {
            double a = degrees * Math.PI / 180;
            float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
            result[k++] = line + new Vector2(arm.X * cos - arm.Y * sin, arm.X * sin + arm.Y * cos);
        }
        return result;
    }

    /// <summary>
    /// #531 step 2e: whether a healer's cover place at <paramref name="at"/> keeps the spread from the other back-liners
    /// <paramref name="others"/>: at least the spread (<see cref="ScatterSpread"/> on "incoming", else <see cref="MezSpread"/>)
    /// from each, and <see cref="MezSpread"/> + 10 from another healer. That is wider than <see cref="NeedsMove"/>'s
    /// "Spread" test, so a healer that took the place is not sent off it again. Pure.
    /// </summary>
    public static bool CoverKeepsSpread(Vector2 at, bool incoming, IReadOnlyList<(Vector2 Position, bool Healer)> others)
    {
        if (others == null)
            return true;
        float spread = incoming ? ScatterSpread : MezSpread;
        foreach ((Vector2 position, bool healer) in others)
        {
            float distance = Vector2.Distance(at, position);
            if (distance < spread || healer && distance < MezSpread + 10)
                return false;
        }
        return true;
    }

    /// <summary>
    /// #531 step 2e: the first candidate that sees the tanks' line and is hidden from the nearest enemy caster (rocks and
    /// walls, reference.md 6), else 0 (its own place). Pure.
    /// </summary>
    public static int PickCover(IReadOnlyList<(bool SeesLine, bool SeenByCaster)> candidates)
    {
        if (candidates == null)
            return 0;
        for (int i = 0; i < candidates.Count; i++)
            if (candidates[i].SeesLine && !candidates[i].SeenByCaster)
                return i;
        return 0;
    }

    /// <summary>
    /// The places of a group facing an enemy at <paramref name="enemy"/>: the backline on the arc behind the tanks' line
    /// (RoleSlots' back arc with "front" toward the enemy), at a depth that spreads its healers more than the mez spread
    /// (<see cref="ScatterSpread"/> when <paramref name="incoming"/>), ragged by tier, then pushed apart; the tier's
    /// errors moved in (too close, charging); each guard beside his charge, toward the enemy. Members who stand front or
    /// beside, and the unplaced, keep their positions. With no line (no tank or melee), the backline is laid around the
    /// group's centre. With <paramref name="errors"/> false nobody makes a mistake (for tests). Pure.
    /// </summary>
    public static ImmutableArray<RvrSlot> Layout(IReadOnlyList<Placed> members, Vector2 enemy, RvrSkillTier tier,
        bool incoming, long now, bool errors = true)
    {
        if (members == null || members.Count == 0)
            return ImmutableArray<RvrSlot>.Empty;
        var positions = new List<(Vector2, RvrStand)>(members.Count);
        Vector2 centre = Vector2.Zero;
        foreach (Placed placed in members)
        {
            Vector2 at = RvrPeelPlan.At(placed.Member);
            positions.Add((at, placed.Stand));
            centre += at;
        }
        centre /= members.Count;
        Vector2 anchor = Line(positions) ?? centre;
        double front = Bearing(anchor, enemy);
        float spread = (incoming ? ScatterSpread : MezSpread) + SpreadMargin;

        // the backline, in key order, then laid along the arc
        var backs = new List<int>();
        for (int i = 0; i < members.Count; i++)
            if (members[i].Stand == RvrStand.Back)
                backs.Add(i);
        backs.Sort((a, b) => members[a].Member.Key.CompareTo(members[b].Member.Key));
        var healer = new bool[backs.Count];
        for (int j = 0; j < backs.Count; j++)
            healer[j] = CountsAsHealer(members[backs[j]].Member.Role, members[backs[j]].Member.Class);
        int[] arc = ArcOrder(healer);
        var places = new AutonomousBotGroupCoordinator.FormationPlace[backs.Count];
        Array.Fill(places, AutonomousBotGroupCoordinator.FormationPlace.Back);
        AutonomousBotGroupCoordinator.FormationSlot[] slots = AutonomousBotGroupCoordinator.RoleSlots(places, front);
        int healerGap = 0;
        for (int a = 0, last = -1; a < arc.Length; a++)
            if (healer[arc[a]])
            {
                if (last >= 0 && (healerGap == 0 || a - last < healerGap))
                    healerGap = a - last;
                last = a;
            }
        float radius = BackRadius(backs.Count, healerGap, spread);
        (float raggedRadial, double raggedDegrees) = Ragged(tier);
        // #531 step 2e: how far the enemy is ahead of the line, for each member's depth from its reach
        float gap = Math.Max(0, Ahead(enemy, anchor, front));

        var spots = new Vector2[backs.Count];
        var memberErrors = new RvrSlotError[backs.Count];
        for (int a = 0; a < arc.Length; a++)
        {
            int j = arc[a];
            RvrCombatant member = members[backs[j]].Member;
            double angle = slots[a].Angle;
            float depth = DepthFor(healer[j], members[backs[j]].Reach, gap, radius);
            if (errors)
            {
                double jitter = RvrPeelPlan.Roll(member.Key, 0, JitterSalt) * 2 - 1;
                double jitter2 = RvrPeelPlan.Roll(member.Key, 1, JitterSalt) * 2 - 1;
                angle += jitter * raggedDegrees * Math.PI / 180;
                depth = Math.Clamp(depth + (float)jitter2 * raggedRadial, MinBackDepth * 0.8f, Math.Max(depth, MaxBackDepth) + raggedRadial);
                memberErrors[j] = Errors(tier, member.Key, now, healer[j]);
            }
            spots[j] = anchor + Direction(angle) * depth;
        }
        var keepApart = new bool[backs.Count];
        for (int j = 0; j < backs.Count; j++)
            keepApart[j] = memberErrors[j] == RvrSlotError.None;
        EnforceSpread(spots, keepApart, spread, front);

        for (int j = 0; j < backs.Count; j++)
        {
            RvrCombatant member = members[backs[j]].Member;
            if ((memberErrors[j] & RvrSlotError.Charge) != 0)
                spots[j] = anchor + Direction(front) * 60f;
            else if ((memberErrors[j] & RvrSlotError.TooClose) != 0)
            {
                int nearest = -1;
                float nearestDistance = float.MaxValue;
                for (int k = 0; k < backs.Count; k++)
                    if (k != j && healer[k] && memberErrors[k] == RvrSlotError.None)
                    {
                        float distance = Vector2.Distance(spots[j], spots[k]);
                        if (distance < nearestDistance)
                        {
                            nearest = k;
                            nearestDistance = distance;
                        }
                    }
                if (nearest >= 0)
                {
                    Vector2 toward = nearestDistance < 1f ? Direction(front + Math.PI / 2) : (spots[j] - spots[nearest]) / nearestDistance;
                    float close = TooCloseMin + (TooCloseMax - TooCloseMin) * (float)RvrPeelPlan.Roll(member.Key, 2, JitterSalt);
                    spots[j] = spots[nearest] + toward * close;
                }
            }
        }

        var bySubject = new Dictionary<GameLiving, Vector2>(ReferenceEqualityComparer.Instance);
        for (int j = 0; j < backs.Count; j++)
            if (members[backs[j]].Member.Living is GameLiving living)
                bySubject[living] = spots[j];
        ImmutableArray<RvrSlot>.Builder result = ImmutableArray.CreateBuilder<RvrSlot>(members.Count);
        var backIndex = new Dictionary<int, int>();
        for (int j = 0; j < backs.Count; j++)
            backIndex[backs[j]] = j;
        for (int i = 0; i < members.Count; i++)
        {
            Placed placed = members[i];
            RvrCombatant member = placed.Member;
            bool isHealer = CountsAsHealer(member.Role, member.Class);
            if (backIndex.TryGetValue(i, out int j))
                result.Add(new RvrSlot(member.Living, RvrStand.Back, spots[j], isHealer, memberErrors[j]));
            else if (placed.Stand == RvrStand.Escort && placed.Charge != null)
            {
                Vector2 charge = bySubject.TryGetValue(placed.Charge, out Vector2 chargeSpot) ? chargeSpot
                    : RvrPeelPlan.Find(Immutable(members), placed.Charge) is { } chargeRecord ? RvrPeelPlan.At(chargeRecord)
                    : RvrPeelPlan.At(member);
                result.Add(new RvrSlot(member.Living, RvrStand.Escort, EscortSpot(charge, enemy), isHealer, RvrSlotError.None));
            }
            else
                result.Add(new RvrSlot(member.Living, placed.Stand == RvrStand.Escort ? RvrStand.Front : placed.Stand,
                    RvrPeelPlan.At(member), isHealer, RvrSlotError.None));
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<RvrCombatant> Immutable(IReadOnlyList<Placed> members)
    {
        ImmutableArray<RvrCombatant>.Builder builder = ImmutableArray.CreateBuilder<RvrCombatant>(members.Count);
        foreach (Placed placed in members)
            builder.Add(placed.Member);
        return builder.ToImmutable();
    }

    /// <summary>A guard's spot: <see cref="EscortOffset"/> from his charge toward the enemy. Pure.</summary>
    public static Vector2 EscortSpot(Vector2 charge, Vector2 enemy)
    {
        Vector2 toward = enemy - charge;
        float length = toward.Length();
        return length < 1f ? charge : charge + toward / length * EscortOffset;
    }

    /// <summary>
    /// "Incoming": enemies are in view, nobody is in contact yet, and the nearest enemy head is within
    /// <see cref="IncomingRange"/> of <paramref name="centre"/>. Pure.
    /// </summary>
    public static bool Incoming(ImmutableArray<RvrCombatant> enemies, bool inContact, Vector2 centre)
    {
        if (inContact || enemies.IsDefaultOrEmpty)
            return false;
        foreach (RvrCombatant enemy in enemies)
            if (enemy != null && !enemy.IsPet && Vector2.DistanceSquared(RvrPeelPlan.At(enemy), centre) <= IncomingRange * IncomingRange)
                return true;
        return false;
    }

    /// <summary>
    /// Whether a backline member at <paramref name="self"/> walks to its place <paramref name="slot"/>, and why: it stands
    /// in front of the line or level with it; within the spread of another backline member (healers: within
    /// <see cref="MezSpread"/> of another healer, a "too close" healer excepted); on "incoming" within the scatter spread;
    /// or far from its place. A charging healer walks to the front when it is far from it. Others ("never move"): none.
    /// <paramref name="others"/> are the other backline members (position and whether a healer). Pure.
    /// </summary>
    public static RvrMoveReason NeedsMove(Vector2 self, RvrSlot slot, Vector2? line, double front, bool incoming,
        IReadOnlyList<(Vector2 Position, bool Healer)> others)
    {
        if (slot.Stand != RvrStand.Back)
            return RvrMoveReason.None;
        float toSlot = Vector2.Distance(self, slot.Spot);
        if (toSlot <= SlotTolerance * 1.5f)
            return RvrMoveReason.None;
        if ((slot.Error & RvrSlotError.Charge) != 0)
            return toSlot > FarFromSlot / 2 ? RvrMoveReason.Charge : RvrMoveReason.None;
        if (line is Vector2 anchor && Ahead(self, anchor, front) > -MinLineDepth)
            return RvrMoveReason.Backline;
        if ((slot.Error & RvrSlotError.TooClose) == 0 && others != null)
        {
            float spread = incoming ? ScatterSpread : MezSpread;
            foreach ((Vector2 position, bool otherHealer) in others)
            {
                float distance = Vector2.Distance(self, position);
                if (slot.Healer && otherHealer && distance < MezSpread + 10 || distance < spread * 0.85f)
                    return incoming ? RvrMoveReason.Incoming : RvrMoveReason.Spread;
            }
        }
        return line != null && toSlot > FarFromSlot ? RvrMoveReason.Far : RvrMoveReason.None;
    }

    /// <summary>
    /// A caster's place moved toward its target until the target is within <paramref name="range"/> of it (a caster
    /// behind the line must still reach what it casts at). Unchanged when already in range or with no range. Pure.
    /// </summary>
    public static Vector2 WithinRange(Vector2 spot, Vector2 target, float range)
    {
        if (range <= 0)
            return spot;
        Vector2 away = spot - target;
        float distance = away.Length();
        return distance <= range || distance < 1f ? spot : target + away / distance * range;
    }

    /// <summary>The delay before a member reacts to being out of place, by tier (the design's follow delays: Casual 1-4 s,
    /// Average 0.6-2.5 s, Veteran 0.3-1.5 s). Pure.</summary>
    public static long ReactionMilliseconds(RvrSkillTier tier, long memberId, long now)
    {
        (long min, long max) = ReactionBand(tier);
        double roll = RvrPeelPlan.Roll(memberId, Math.DivRem(now, 1_000L).Quotient, DelaySalt);
        return min + (long)Math.Round((max - min) * roll);
    }

    /// <summary>The band of <see cref="ReactionMilliseconds"/> for a tier (Bots as people WP2 spreads a member inside it). Pure.</summary>
    public static (long Min, long Max) ReactionBand(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => (1_000L, 4_000L),
        RvrSkillTier.Veteran => (300L, 1_500L),
        _ => (600L, 2_500L),
    };

    /// <summary>The points a walk from <paramref name="from"/> to <paramref name="to"/> is proven through on the mesh:
    /// pieces of at most <see cref="ChunkLength"/>, the last at <paramref name="to"/>. Pure.</summary>
    public static Vector2[] Chunks(Vector2 from, Vector2 to, float chunk = ChunkLength)
    {
        float distance = Vector2.Distance(from, to);
        int pieces = Math.Max(1, (int)Math.Ceiling(distance / chunk));
        var points = new Vector2[pieces];
        for (int i = 1; i <= pieces; i++)
            points[i - 1] = Vector2.Lerp(from, to, i / (float)pieces);
        return points;
    }

    // ---- kiting -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The enemy melee fighter on a healer (at x, y): it targets the healer, swings, is not mezzed or stunned, is within
    /// melee reach, and is not a caster or healer (one standing close to cast is not kited from). Null: none. Pure.
    /// </summary>
    public static RvrCombatant KiteFrom(GameLiving self, int x, int y, ImmutableArray<RvrCombatant> enemies)
    {
        RvrCombatant attacker = RvrPeelPlan.MeleeAttackerOn(self, x, y, enemies);
        return attacker == null || attacker.Role is RvrRole.Caster or RvrRole.Healer ? null : attacker;
    }

    /// <summary>Whether a healer that has a melee fighter on it skips kiting this contact (by tier). Pure.</summary>
    public static bool KiteSkipped(RvrSkillTier tier, long memberId, long contactWindow) =>
        RvrPeelPlan.Roll(memberId, contactWindow, KiteSalt) < KiteSkipShare(tier);

    /// <summary>How far a healer runs, by tier (Casual short and late, Veteran the full run). Pure.</summary>
    public static float KiteDistance(RvrSkillTier tier, double roll)
    {
        float min = tier == RvrSkillTier.Casual ? KiteMinDistance * 0.8f : KiteMinDistance;
        return min + (KiteMaxDistance - min) * (float)Math.Clamp(roll, 0, 1);
    }

    /// <summary>How long a healer heals before it kites again. Pure.</summary>
    public static long KiteHealWindow(double roll) =>
        KiteHealWindowMinMilliseconds + (long)Math.Round((KiteHealWindowMaxMilliseconds - KiteHealWindowMinMilliseconds) * Math.Clamp(roll, 0, 1));

    /// <summary>
    /// Where a healer runs: away from the attacker, bent toward <paramref name="refuge"/> (its guard, or the tanks' line,
    /// so the tanks can peel the attacker) when that is not back toward the attacker. Pure.
    /// </summary>
    public static Vector2 KiteSpot(Vector2 self, Vector2 attacker, Vector2? refuge, float distance)
    {
        Vector2 away = self - attacker;
        away = away.LengthSquared() < 1f ? new Vector2(1, 0) : Vector2.Normalize(away);
        Vector2 direction = away;
        if (refuge is Vector2 haven && Vector2.DistanceSquared(haven, self) > 1f)
        {
            Vector2 toward = Vector2.Normalize(haven - self);
            Vector2 blended = away + toward;
            if (Vector2.Dot(toward, away) > -0.2f && blended.LengthSquared() > 0.01f)
                direction = Vector2.Normalize(blended);
        }
        return self + direction * distance;
    }

    // ---- the kiting group (stance Kite) -------------------------------------------------------------------------------
    //
    // The Kite stance (work package 4: a bigger or melee-heavy enemy) "fights while backing away": the backline keeps a
    // stand-off distance from the nearest enemy melee fighter by stepping back in short legs along the group's back
    // direction (away from the enemy centroid; the back-arc slots of the layout keep its spread), then stops and casts or
    // shoots; the melee screen the backline and fight only what reaches it (2003: healers "run a bit, throw heal",
    // ranged groups kite melee trains). Every number below is a design value.

    /// <summary>The stand-off distance a backline member keeps from the nearest enemy melee fighter (inside a caster's
    /// 1,500 range), give or take the tier's spread (<see cref="KiteStandOffSpread"/>).</summary>
    public const float KiteStandOff = 1_200f;
    /// <summary>A kite leg is at least this long and at most <see cref="KiteStepLength"/>; an overrun is longer.</summary>
    public const float KiteMinStep = 150f;
    public const float KiteStepLength = 300f;
    /// <summary>An overrunning member runs this many times its leg.</summary>
    public const float KiteOverrunFactor = 2.5f;
    /// <summary>A member that backs off late steps only once the melee fighter is this close.</summary>
    public const float KiteLateDistance = 450f;
    /// <summary>A member that runs the wrong way runs this far off the back direction (degrees).</summary>
    public const double KiteWrongWayMinDegrees = 50;
    public const double KiteWrongWayMaxDegrees = 90;
    /// <summary>The legs tried, in order, when the straight one is not safe: this many degrees to either side.</summary>
    public static readonly double[] KiteDetourDegrees = [35, 70];
    /// <summary>An enemy head this far from the enemy centroid belongs to another group ...</summary>
    public const float KiteOtherGroupGap = 1_500f;
    /// <summary>... and no leg ends closer to it than this (while getting closer).</summary>
    public const float KiteOtherGroupClearance = 2_000f;
    /// <summary>No leg ends this close to an enemy keep or tower (while getting closer): its guards' range.</summary>
    public const float KiteKeepGuardClearance = 2_500f;
    /// <summary>After a step the member casts or shoots this long (by roll) before it steps again.</summary>
    public const long KiteFightWindowMinMilliseconds = 2_000;
    public const long KiteFightWindowMaxMilliseconds = 3_500;
    /// <summary>A healer heals before it steps while a group mate is under <see cref="HealFirstPercent"/> and the melee is
    /// farther than this.</summary>
    public const float KiteHealFirstDistance = 500f;
    /// <summary>A melee member fights an enemy within this of a backline member (it reached the backline) ...</summary>
    public const float KitePeelLeash = 600f;
    /// <summary>... and otherwise stands this far in front of the backline's centre, toward the enemy ...</summary>
    public const float KiteScreenDistance = 250f;
    /// <summary>... walking back to it when farther than this.</summary>
    public const float KiteScreenTolerance = 150f;

    private const ulong StandOffSalt = 0x52564B54_53544F46;  // "RVKTSTOF"
    private const ulong LateSalt = 0x52564B54_4C415445;      // "RVKTLATE"
    private const ulong WrongWaySalt = 0x52564B54_57524F4E;  // "RVKTWRON"
    private const ulong OverrunSalt = 0x52564B54_4F564552;   // "RVKTOVER"
    private const ulong ChaseSalt = 0x52564B54_43484153;     // "RVKTCHAS"

    /// <summary>What the positioning does in a stance: the layout (Engage, Observe, Hold), the kite (Kite), or nothing
    /// (Retreat and Regroup belong to work package 4's stance code). Pure.</summary>
    public static bool PlacesIn(RvrStance stance) => stance is not (RvrStance.Retreat or RvrStance.Regroup);

    /// <summary>The tier's spread of the stand-off distance: Casual +-200, Average +-100, Veteran +-50 units.</summary>
    public static float KiteStandOffSpread(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 200f,
        RvrSkillTier.Veteran => 50f,
        _ => 100f,
    };

    /// <summary>This member's stand-off distance (fixed per member): <see cref="KiteStandOff"/> +- the tier's spread. Pure.</summary>
    public static float KiteStandOffFor(RvrSkillTier tier, long memberId) =>
        KiteStandOff + KiteStandOffSpread(tier) * (float)(RvrPeelPlan.Roll(memberId, 0, StandOffSalt) * 2 - 1);

    /// <summary>The share of kite steps begun late: Casual 35%, Average 15%, Veteran 5%. Design values.</summary>
    public static double KiteLateShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.35,
        RvrSkillTier.Veteran => 0.05,
        _ => 0.15,
    };

    /// <summary>The share of kite steps run the wrong way (50-90 degrees off): Casual 20%, Average 8%, Veteran 2%.</summary>
    public static double KiteWrongWayShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.20,
        RvrSkillTier.Veteran => 0.02,
        _ => 0.08,
    };

    /// <summary>The share of kite steps run too far (2.5 times the leg): Casual 25%, Average 10%, Veteran 3%.</summary>
    public static double KiteOverrunShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.25,
        RvrSkillTier.Veteran => 0.03,
        _ => 0.10,
    };

    /// <summary>The share of 30 s windows a melee member of a kiting group charges anyway: Casual 25%, Average 10%,
    /// Veteran 3%.</summary>
    public static double KiteChaseShare(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => 0.25,
        RvrSkillTier.Veteran => 0.03,
        _ => 0.10,
    };

    /// <summary>The errors of this member's kite step number <paramref name="step"/>. Pure.</summary>
    public static RvrKiteError KiteErrors(RvrSkillTier tier, long memberId, long step)
    {
        RvrKiteError errors = RvrKiteError.None;
        if (RvrPeelPlan.Roll(memberId, step, LateSalt) < KiteLateShare(tier))
            errors |= RvrKiteError.Late;
        if (RvrPeelPlan.Roll(memberId, step, WrongWaySalt) < KiteWrongWayShare(tier))
            errors |= RvrKiteError.WrongWay;
        if (RvrPeelPlan.Roll(memberId, step, OverrunSalt) < KiteOverrunShare(tier))
            errors |= RvrKiteError.Overrun;
        return errors;
    }

    /// <summary>Whether a melee member of a kiting group charges anyway in the window holding <paramref name="now"/>. Pure.</summary>
    public static bool KiteChases(RvrSkillTier tier, long memberId, long now) =>
        RvrPeelPlan.Roll(memberId, Math.DivRem(now, ErrorWindowMilliseconds).Quotient, ChaseSalt) < KiteChaseShare(tier);

    /// <summary>How long a member casts or shoots after a kite step. Pure.</summary>
    public static long KiteFightWindow(double roll) =>
        KiteFightWindowMinMilliseconds + (long)Math.Round((KiteFightWindowMaxMilliseconds - KiteFightWindowMinMilliseconds) * Math.Clamp(roll, 0, 1));

    /// <summary>An enemy that chases on foot to hit in melee: a tank, a melee fighter, a stealther or a melee hybrid; not a
    /// pet, and not mezzed, stunned or rooted. Pure.</summary>
    public static bool ChasingMelee(RvrCombatant enemy) =>
        enemy != null && !enemy.IsPet && !enemy.IsControlled &&
        (enemy.Role == RvrRole.Stealther || StandFor(enemy.Role, enemy.Class) is RvrStand.Front or RvrStand.Side);

    /// <summary>The nearest chasing melee enemy (<see cref="ChasingMelee"/>) to <paramref name="self"/>, or null. Pure.</summary>
    public static RvrCombatant NearestMelee(Vector2 self, ImmutableArray<RvrCombatant> enemies, out float distance)
    {
        distance = float.MaxValue;
        RvrCombatant nearest = null;
        if (enemies.IsDefaultOrEmpty)
            return null;
        foreach (RvrCombatant enemy in enemies)
        {
            if (!ChasingMelee(enemy))
                continue;
            float d = Vector2.Distance(self, RvrPeelPlan.At(enemy));
            if (d < distance)
            {
                distance = d;
                nearest = enemy;
            }
        }
        return nearest;
    }

    /// <summary>
    /// Whether a leg from <paramref name="from"/> to <paramref name="to"/> walks into danger: toward another enemy group
    /// (an enemy head more than <see cref="KiteOtherGroupGap"/> from the enemy centroid, ending within
    /// <see cref="KiteOtherGroupClearance"/> of it and closer than the start), or toward an enemy keep or tower (ending
    /// within <paramref name="keeps"/>' radius and closer than the start). Pure.
    /// </summary>
    public static RvrKiteHeld KiteHazard(Vector2 from, Vector2 to, ImmutableArray<RvrCombatant> enemies, Vector2 enemyCentroid,
        IReadOnlyList<AutonomousArmyDetour.Hazard> keeps)
    {
        if (!enemies.IsDefaultOrEmpty)
            foreach (RvrCombatant enemy in enemies)
            {
                if (enemy == null || enemy.IsPet)
                    continue;
                Vector2 at = RvrPeelPlan.At(enemy);
                if (Vector2.Distance(at, enemyCentroid) <= KiteOtherGroupGap)
                    continue;
                float end = Vector2.Distance(to, at);
                if (end < KiteOtherGroupClearance && end < Vector2.Distance(from, at))
                    return RvrKiteHeld.EnemyGroup;
            }
        if (keeps != null)
            foreach (AutonomousArmyDetour.Hazard keep in keeps)
            {
                float end = Vector2.Distance(to, keep.Center);
                if (end < keep.Radius && end < Vector2.Distance(from, keep.Center))
                    return RvrKiteHeld.KeepGuards;
            }
        return RvrKiteHeld.None;
    }

    /// <summary>
    /// The direction of a kite leg: <paramref name="back"/> (a unit vector away from the enemy), bent toward the member's
    /// back-arc slot sideways (at most half the leg) so the backline keeps its spread, then turned
    /// <paramref name="turnDegrees"/>. Pure.
    /// </summary>
    public static Vector2 KiteDirection(Vector2 self, Vector2 back, Vector2? slot, float length, double turnDegrees)
    {
        Vector2 direction = back;
        if (slot is Vector2 place)
        {
            Vector2 side = new(-back.Y, back.X);
            float lateral = Math.Clamp(Vector2.Dot(place - self, side), -length / 2, length / 2);
            Vector2 bent = back * length + side * lateral;
            if (bent.LengthSquared() > 1f)
                direction = Vector2.Normalize(bent);
        }
        if (turnDegrees != 0)
        {
            double angle = Math.Atan2(direction.Y, direction.X) + turnDegrees * Math.PI / 180;
            direction = Direction(angle);
        }
        return direction;
    }

    /// <summary>
    /// One backline member's kite step (stance Kite). It steps only while the nearest chasing melee enemy is closer than
    /// its stand-off distance (<see cref="KiteStandOffFor"/>; <see cref="KiteLateDistance"/> when it backs off late), by
    /// the shortfall (between <see cref="KiteMinStep"/> and <see cref="KiteStepLength"/>, more when it overruns), along
    /// the group's back direction (the opposite of <paramref name="front"/>, the bearing of the enemy centroid; turned
    /// 50-90 degrees when it runs the wrong way), bent toward its back-arc slot. The straight leg is tried first, then
    /// 35 and 70 degrees to either side; a leg must take it farther from the melee, walk into no hazard
    /// (<see cref="KiteHazard"/>) and be clear on the mesh (<paramref name="legClear"/>: dry ground on its own floor).
    /// With none, it holds and fights, with the reason. <paramref name="step"/> numbers its steps (for the error rolls).
    /// Pure apart from <paramref name="legClear"/>.
    /// </summary>
    public static RvrKiteStep KiteStep(Vector2 self, Vector2? slot, double front, ImmutableArray<RvrCombatant> enemies,
        Vector2 enemyCentroid, IReadOnlyList<AutonomousArmyDetour.Hazard> keeps, RvrSkillTier tier, long memberId, long step,
        Func<Vector2, Vector2, bool> legClear)
    {
        float standOff = KiteStandOffFor(tier, memberId);
        RvrCombatant melee = NearestMelee(self, enemies, out float distance);
        if (melee == null)
            return new(null, RvrKiteHeld.NoMelee, RvrKiteError.None, 0, standOff, 0);
        RvrKiteError errors = KiteErrors(tier, memberId, step);
        float trigger = (errors & RvrKiteError.Late) != 0 ? Math.Min(KiteLateDistance, standOff) : standOff;
        if (distance >= trigger)
            return new(null, RvrKiteHeld.Clear, errors & RvrKiteError.Late, 0, standOff, distance);
        float length = Math.Clamp(standOff - distance, KiteMinStep, KiteStepLength);
        if ((errors & RvrKiteError.Overrun) != 0)
            length *= KiteOverrunFactor;
        double turn = 0;
        if ((errors & RvrKiteError.WrongWay) != 0)
        {
            double roll = RvrPeelPlan.Roll(memberId, step, WrongWaySalt ^ 0xFF);
            turn = (KiteWrongWayMinDegrees + (KiteWrongWayMaxDegrees - KiteWrongWayMinDegrees) * roll) * (memberId % 2 == 0 ? 1 : -1);
        }
        Vector2 back = -Direction(front);
        Vector2 meleeAt = RvrPeelPlan.At(melee);
        Vector2 direction = KiteDirection(self, back, slot, length, turn);
        double sign = memberId % 2 == 0 ? 1 : -1;
        RvrKiteHeld first = RvrKiteHeld.None;
        var tries = new List<double>(1 + KiteDetourDegrees.Length * 2) { 0 };
        foreach (double degrees in KiteDetourDegrees)
        {
            tries.Add(degrees * sign);
            tries.Add(-degrees * sign);
        }
        foreach (double degrees in tries)
        {
            Vector2 leg = degrees == 0 ? direction : KiteDirection(Vector2.Zero, direction, null, length, degrees);
            Vector2 to = self + leg * length;
            RvrKiteHeld why = RvrKiteHeld.None;
            if (Vector2.Distance(to, meleeAt) <= distance)
                why = RvrKiteHeld.Blocked;
            else if (KiteHazard(self, to, enemies, enemyCentroid, keeps) is var hazard && hazard != RvrKiteHeld.None)
                why = hazard;
            else if (legClear != null && !legClear(self, to))
                why = RvrKiteHeld.Blocked;
            if (why == RvrKiteHeld.None)
                return new(to, RvrKiteHeld.None, errors, length, standOff, distance);
            if (first == RvrKiteHeld.None)
                first = why;
        }
        return new(null, first, errors, length, standOff, distance);
    }

    /// <summary>
    /// What a melee member of a kiting group does: it fights when it is hit in melee, when it charges anyway (the tier's
    /// error), when there is no backline, or when its target is within <see cref="KitePeelLeash"/> of a backline member
    /// (it reached the backline); otherwise it screens: it stands <see cref="KiteScreenDistance"/> in front of the
    /// backline's centre toward the enemy, walking back there when farther than <see cref="KiteScreenTolerance"/>.
    /// Pure.
    /// </summary>
    public static (RvrKitePeel Action, Vector2 Spot) KitePeel(Vector2 self, Vector2? target, IReadOnlyList<Vector2> backline,
        Vector2 enemyCentroid, bool hitInMelee, bool chases)
    {
        if (backline == null || backline.Count == 0 || hitInMelee || chases)
            return (RvrKitePeel.Fight, self);
        Vector2 centre = Vector2.Zero;
        bool reached = false;
        foreach (Vector2 member in backline)
        {
            centre += member;
            reached |= target is Vector2 t && Vector2.Distance(t, member) <= KitePeelLeash;
        }
        if (reached)
            return (RvrKitePeel.Fight, self);
        centre /= backline.Count;
        Vector2 toward = enemyCentroid - centre;
        Vector2 spot = toward.LengthSquared() < 1f ? centre : centre + Vector2.Normalize(toward) * KiteScreenDistance;
        return (Vector2.Distance(self, spot) > KiteScreenTolerance ? RvrKitePeel.Screen : RvrKitePeel.Stand, spot);
    }

    /// <summary>The rate limit of a log line: at most once per <paramref name="gap"/>. Pure.</summary>
    public static bool ShouldLog(long now, long lastLogAt, long gap) => now - lastLogAt >= gap;
}
