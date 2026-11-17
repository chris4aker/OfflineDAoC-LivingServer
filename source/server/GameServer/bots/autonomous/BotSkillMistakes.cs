using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS;

/// <summary>The kinds of mistake a gamebot's hands make (docs/BOTS-AS-PEOPLE-1.2.md, item 2; work package 2). One per facet.</summary>
public enum BotSkillMistakeKind : byte
{
    /// <summary>It reacted late to being hit (BotIncomingFireReaction's hold, slower than an average person's slowest).</summary>
    Reaction,
    /// <summary>It circled into the monster's face instead of keeping its side or back.</summary>
    Position,
    /// <summary>It pulled a monster with a neighbour inside its aggro range instead of the clean one.</summary>
    Pull,
    /// <summary>It stood in an enemy's area effect for 2 s or more before stepping out (open-field RvR).</summary>
    Fire,
    /// <summary>It let a buff run out and renewed it late.</summary>
    Buff,
    /// <summary>It cast its most expensive damage spell instead of the one it would have picked.</summary>
    Power,
}

/// <summary>The facts of one camp monster a gamebot party's puller may take (pure input of <see cref="BotSkillMistakes.ChooseAddPull"/>).</summary>
/// <param name="Con">Its con to the puller (ConColor: -3 grey .. 3 purple).</param>
public readonly record struct BotPullView(Vector3 Position, int AggroRange, int Level, int Con, bool Named);

/// <summary>The puller's group when it pulls (pure input of <see cref="BotSkillMistakes.ChooseAddPull"/>).</summary>
public readonly record struct BotPullGroup(int Size, int TopLevel, bool AllAlive, bool InDungeon);

/// <summary>
/// How skill shows in a gamebot's hands (docs/BOTS-AS-PEOPLE-1.2.md, item 2, "What the bot does"; work package 2): the
/// rate and the delay of each mistake by the bot's skill profile (<see cref="BotSkillProfile.Blend(BotSkillFacet, double, double, double)"/>:
/// the tier's design value in the middle of its band, moving toward the neighbouring tier's at the band's edges), and the
/// caps that keep a mistake recoverable ("A mistake is never a death sentence"). In a group with a real player a chance is
/// a quarter (owner decision 4, bot_skill_spare_player_group); a delay is never shortened. In an RvR group the group's tier
/// stays (owner decision 5): a member's facet only moves its delay inside that tier's band. Pure: no game world, no clock.
/// </summary>
public static class BotSkillMistakes
{
    /// <summary>A mistake's chance in a group with a real player, of its own (owner decision 4).</summary>
    public const double SpareFactor = 0.25;

    // ---- the design table (item 2), per tier: Casual, Average, Veteran ----------------------------------------------

    /// <summary>Reactions: the factor on the delay before reacting to a hit.</summary>
    public static readonly (double Casual, double Average, double Veteran) ReactionFactors = (1.3, 1.0, 0.75);
    /// <summary>Positioning: the share of repositions that end badly placed (in the monster's face).</summary>
    public static readonly (double Casual, double Average, double Veteran) PositionChances = (0.12, 0.06, 0.02);
    /// <summary>Pulls: the share of party pulls that take a monster with a neighbour inside its aggro range.</summary>
    public static readonly (double Casual, double Average, double Veteran) PullChances = (0.10, 0.05, 0.015);
    /// <summary>Standing in fire: the delay (ms) before stepping out of an area effect it is taking damage from.</summary>
    public static readonly (long Min, long Max) FireCasual = (2_000, 5_000), FireAverage = (1_000, 3_000), FireVeteran = (500, 1_500);
    /// <summary>Buffs: the share of buff expiries not renewed at once.</summary>
    public static readonly (double Casual, double Average, double Veteran) BuffChances = (0.25, 0.12, 0.04);
    /// <summary>Buffs: how long a lapsed buff stays off (seconds).</summary>
    public static readonly (double Min, double Max) BuffLapseCasual = (20, 90), BuffLapseAverage = (10, 45), BuffLapseVeteran = (5, 20);
    /// <summary>Power: the share of a caster's damage casts that take its most expensive damage spell.</summary>
    public static readonly (double Casual, double Average, double Veteran) PowerChances = (0.30, 0.15, 0.05);
    /// <summary>
    /// Power: the power (percent) below which the bot sits to rest. Today's floor (AutonomousRestPolicy.PowerRestThreshold,
    /// 45) is the Veteran's; the design's 10-point steps go down from it (design 15/25/35 would have every bot, Veterans
    /// too, pull on less power than today: worse, not more human).
    /// </summary>
    public static readonly (double Casual, double Average, double Veteran) PowerRestFloors = (25, 35, 45);

    /// <summary>Today's power rest floor (AutonomousRestPolicy.PowerRestThreshold).</summary>
    public const int TodayPowerRestFloor = 45;
    /// <summary>A solo offensive caster never rests below this: running dry alone is a death, not a mistake.</summary>
    public const int SoloCasterPowerRestFloor = 35;
    /// <summary>A stand-in caster keeps this much power (percent) after an expensive cast by mistake, in a group or alone.</summary>
    public const int GroupPowerReservePercent = 10, SoloPowerReservePercent = 40;
    /// <summary>A pull mistake takes no monster more than this many levels above the group's highest member.</summary>
    public const int MaxLevelsAboveGroup = 2;
    /// <summary>A party smaller than this never pulls an add by mistake (a duo could not take it).</summary>
    public const int MinPartyForAddPull = 3;
    /// <summary>After a pull mistake the same puller makes none for this long (no two adds in a row).</summary>
    public const long PullMistakeCooldownMilliseconds = 180_000;
    /// <summary>A puller decides once per this window whether its next pull is careless (its searches repeat meanwhile).</summary>
    public const long PullDecisionWindowMilliseconds = 20_000;
    /// <summary>A step out of fire ends this far outside the area's radius.</summary>
    public const int FireStepMargin = 75;
    /// <summary>A delay of this much or more in an area effect counts as standing in it (BOT_SKILL_MISTAKE kind=fire).</summary>
    public const long FireMistakeMilliseconds = 2_000;

    public static BotSkillFacet FacetOf(BotSkillMistakeKind kind) => kind switch
    {
        BotSkillMistakeKind.Reaction => BotSkillFacet.Reactions,
        BotSkillMistakeKind.Position => BotSkillFacet.Positioning,
        BotSkillMistakeKind.Pull => BotSkillFacet.Pulls,
        BotSkillMistakeKind.Fire => BotSkillFacet.Fire,
        BotSkillMistakeKind.Buff => BotSkillFacet.Buffs,
        _ => BotSkillFacet.Power,
    };

    public static string KindName(BotSkillMistakeKind kind) => kind switch
    {
        BotSkillMistakeKind.Reaction => "reaction",
        BotSkillMistakeKind.Position => "position",
        BotSkillMistakeKind.Pull => "pull",
        BotSkillMistakeKind.Fire => "fire",
        BotSkillMistakeKind.Buff => "buff",
        _ => "power",
    };

    /// <summary>The kinds that are a chance (spared in a player's group); reactions and fire are delays (never shortened).</summary>
    public static bool IsChance(BotSkillMistakeKind kind) =>
        kind is BotSkillMistakeKind.Position or BotSkillMistakeKind.Pull or BotSkillMistakeKind.Buff or BotSkillMistakeKind.Power;

    // ---- chances ------------------------------------------------------------------------------------------------------

    /// <summary>A chance kind's own rate for this profile (no sparing); 0 for a delay kind or no profile. Pure.</summary>
    public static double BaseChance(BotSkillMistakeKind kind, in BotSkillProfile profile)
    {
        if (profile.IsNone)
            return 0;
        var (c, a, v) = kind switch
        {
            BotSkillMistakeKind.Position => PositionChances,
            BotSkillMistakeKind.Pull => PullChances,
            BotSkillMistakeKind.Buff => BuffChances,
            BotSkillMistakeKind.Power => PowerChances,
            _ => (0d, 0d, 0d),
        };
        return Math.Clamp(profile.Blend(FacetOf(kind), c, a, v), 0, 1);
    }

    /// <summary>
    /// The chance of a mistake of this kind: the profile's own rate, a quarter of it in a group with a real player while
    /// <paramref name="spareSwitch"/> (bot_skill_spare_player_group) is on. 0 for no profile and for the delay kinds. Pure.
    /// </summary>
    public static double Chance(BotSkillMistakeKind kind, in BotSkillProfile profile, bool inPlayerGroup, bool spareSwitch) =>
        BaseChance(kind, profile) * (Spared(inPlayerGroup, spareSwitch) ? SpareFactor : 1);

    /// <summary>Whether the quarter rate applies. Pure.</summary>
    public static bool Spared(bool inPlayerGroup, bool spareSwitch) => inPlayerGroup && spareSwitch;

    /// <summary>A roll in [0, 1) under a chance makes the mistake. Pure.</summary>
    public static bool Rolls(double chance, double roll) => roll < chance;

    // ---- delays -------------------------------------------------------------------------------------------------------

    /// <summary>The factor on a reaction delay from the bot's own tier (x1.3 / x1.0 / x0.75, spread inside the band). 1 for no profile. Pure.</summary>
    public static double ReactionFactor(in BotSkillProfile profile) =>
        profile.IsNone ? 1 : profile.Blend(BotSkillFacet.Reactions, ReactionFactors.Casual, ReactionFactors.Average, ReactionFactors.Veteran);

    /// <summary>The same in an RvR group: the group's tier, the member's place in its band (owner decision 5). Pure.</summary>
    public static double ReactionFactor(RvrSkillTier groupTier, double memberPosition) =>
        BotSkillProfile.Blend(groupTier, memberPosition, ReactionFactors.Casual, ReactionFactors.Average, ReactionFactors.Veteran);

    /// <summary>
    /// A delay inside a band [<paramref name="min"/>, <paramref name="max"/>]: a member's place in its band (1 the best)
    /// moves the middle of its draws, the roll spreads them around it; never outside the band, and over members spread
    /// evenly the mean stays the band's middle (owner decision 5: the RvR field's measures stay readable). Pure.
    /// </summary>
    public static long SpreadInBand(long min, long max, double roll, double position)
    {
        if (max <= min)
            return Math.Max(0, min);
        double r = Math.Clamp(double.IsFinite(roll) ? roll : 0.5, 0, 1);
        double p = Math.Clamp(double.IsFinite(position) ? position : 0.5, 0, 1);
        double fraction = 0.5 * r + 0.5 * (1 - p);
        return min + (long)Math.Round((max - min) * fraction);
    }

    /// <summary>The design's fire band (ms) of a tier: Casual 2-5 s, Average 1-3 s, Veteran 0.5-1.5 s. Pure.</summary>
    public static (long Min, long Max) FireBand(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => FireCasual,
        RvrSkillTier.Veteran => FireVeteran,
        _ => FireAverage,
    };

    /// <summary>The delay (ms) before stepping out of an area effect, from the bot's own tier. Pure.</summary>
    public static long FireDelayMilliseconds(in BotSkillProfile profile, double roll)
    {
        if (profile.IsNone)
            return FireAverage.Min + (long)Math.Round((FireAverage.Max - FireAverage.Min) * Math.Clamp(roll, 0, 1));
        double min = profile.Blend(BotSkillFacet.Fire, FireCasual.Min, FireAverage.Min, FireVeteran.Min);
        double max = profile.Blend(BotSkillFacet.Fire, FireCasual.Max, FireAverage.Max, FireVeteran.Max);
        return (long)Math.Round(min + (max - min) * Math.Clamp(roll, 0, 1));
    }

    /// <summary>The same in an RvR group: inside the group tier's band, moved by the member's fire facet (decision 5). Pure.</summary>
    public static long FireDelayMilliseconds(RvrSkillTier groupTier, double memberPosition, double roll)
    {
        var (min, max) = FireBand(groupTier);
        return SpreadInBand(min, max, roll, memberPosition);
    }

    /// <summary>How long (ms) a lapsed buff stays off: Casual 20-90 s, Average 10-45 s, Veteran 5-20 s, spread in the band. Pure.</summary>
    public static long BuffLapseMilliseconds(in BotSkillProfile profile, double roll)
    {
        if (profile.IsNone)
            return 0;
        double min = profile.Blend(BotSkillFacet.Buffs, BuffLapseCasual.Min, BuffLapseAverage.Min, BuffLapseVeteran.Min);
        double max = profile.Blend(BotSkillFacet.Buffs, BuffLapseCasual.Max, BuffLapseAverage.Max, BuffLapseVeteran.Max);
        return (long)Math.Round((min + (max - min) * Math.Clamp(roll, 0, 1)) * 1000);
    }

    // ---- power --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The power (percent) below which a bot sits to rest: Casual 25, Average 35, Veteran 45 (today's), spread in the band.
    /// In a player's group it moves three quarters of the way back to today's (the spared rate). A solo offensive caster
    /// never goes below <see cref="SoloCasterPowerRestFloor"/>. Today's 45 for no profile. Pure.
    /// </summary>
    public static int PowerRestFloor(in BotSkillProfile profile, bool inPlayerGroup, bool spareSwitch, bool soloCaster)
    {
        if (profile.IsNone)
            return TodayPowerRestFloor;
        double floor = profile.Blend(BotSkillFacet.Power, PowerRestFloors.Casual, PowerRestFloors.Average, PowerRestFloors.Veteran);
        if (Spared(inPlayerGroup, spareSwitch))
            floor = TodayPowerRestFloor - (TodayPowerRestFloor - floor) * SpareFactor;
        if (soloCaster)
            floor = Math.Max(floor, SoloCasterPowerRestFloor);
        return (int)Math.Clamp(Math.Round(floor), 0, TodayPowerRestFloor);
    }

    /// <summary>
    /// Whether an expensive cast by mistake is allowed: it leaves the caster at least 10% of its power in a group, 40% alone
    /// (a Casual caster runs low, then rests longer; it does not cast itself dry alone). Pure.
    /// </summary>
    public static bool ExpensiveCastAllowed(int mana, int maxMana, int cost, bool solo) =>
        maxMana > 0 && cost > 0 && (long)(mana - cost) * 100 >= (long)maxMana * (solo ? SoloPowerReservePercent : GroupPowerReservePercent);

    /// <summary>The index of the most expensive cost (the first of equals), or -1 when none is dearer than <paramref name="pickedCost"/>. Pure.</summary>
    public static int MostExpensive(IReadOnlyList<int> costs, int pickedCost)
    {
        int best = -1, bestCost = pickedCost;
        for (int i = 0; i < costs.Count; i++)
            if (costs[i] > bestCost)
            {
                best = i;
                bestCost = costs[i];
            }
        return best;
    }

    // ---- pulls --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The monster a careless puller takes instead of the clean one: a monster with exactly one other camp monster within
    /// its aggro range, so the pull brings one add. The caps ("never a death sentence"): a party of three or more, all alive,
    /// outside dungeons; neither the monster nor its add named; neither more than <see cref="MaxLevelsAboveGroup"/> levels
    /// above the group's highest member; the add of the same con as the monster or easier. The nearest such monster to the
    /// puller (a careless puller takes what is in front of him). -1: no safe add pull here, the clean pull stands. Pure.
    /// </summary>
    public static int ChooseAddPull(IReadOnlyList<BotPullView> pool, Vector3 puller, in BotPullGroup group)
    {
        if (pool == null || pool.Count < 2 || group.Size < MinPartyForAddPull || !group.AllAlive || group.InDungeon)
            return -1;
        int top = group.TopLevel + MaxLevelsAboveGroup;
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < pool.Count; i++)
        {
            BotPullView monster = pool[i];
            if (monster.Named || monster.Level > top || monster.AggroRange <= 0)
                continue;
            float range = (float)monster.AggroRange * monster.AggroRange;
            int add = -1, neighbours = 0;
            for (int j = 0; j < pool.Count && neighbours < 2; j++)
            {
                if (j == i || Vector3.DistanceSquared(monster.Position, pool[j].Position) > range)
                    continue;
                neighbours++;
                add = j;
            }
            if (neighbours != 1)
                continue;
            BotPullView other = pool[add];
            if (other.Named || other.Level > top || other.Con > monster.Con)
                continue;
            float distance = Vector3.DistanceSquared(puller, monster.Position);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    /// <summary>
    /// The second look a careless pick gets before it is allowed (review 2026-11-17): every monster that really stands
    /// within the pick's aggro range (<paramref name="around"/>, from a radius scan of the world, not only the camp's own
    /// monster) is counted. Exactly one, not named, at most <see cref="MaxLevelsAboveGroup"/> above the group's top and no
    /// harder con than the pick: else the clean pull stands. Pure.
    /// </summary>
    public static bool AddScanAllows(in BotPullView pick, IReadOnlyList<BotPullView> around, int groupTopLevel)
    {
        if (around == null || pick.AggroRange <= 0)
            return false;
        float range = (float)pick.AggroRange * pick.AggroRange;
        int count = 0;
        BotPullView add = default;
        foreach (BotPullView other in around)
        {
            if (Vector3.DistanceSquared(pick.Position, other.Position) > range)
                continue;
            if (++count > 1)
                return false;
            add = other;
        }
        return count == 1 && !add.Named && add.Level <= groupTopLevel + MaxLevelsAboveGroup && add.Con <= pick.Con;
    }

    /// <summary>A careless pick held from its decision (the window) still fires only while the scope allows it and the
    /// party still qualifies (three or more, all alive, outside dungeons). Pure.</summary>
    public static bool HeldPullStands(bool scopeAllows, in BotPullGroup group) =>
        scopeAllows && group.Size >= MinPartyForAddPull && group.AllAlive && !group.InDungeon;

    /// <summary>A lapse already running holds only while the scope still allows mistakes and no boss is near (dungeon, a
    /// named target, pull or aggro of the bot or its group); else the buff is renewed now. Pure.</summary>
    public static bool LapseHolds(bool scopeAllows, bool inDungeon, bool namedInPlay) =>
        scopeAllows && BuffLapseAllowed(inDungeon, namedInPlay);

    /// <summary>
    /// Where a bot standing in an area effect walks: out of the circle, straight away from its centre, unless that heads
    /// toward the enemy (an area centred on a groupmate, the enemy beyond the bot): then out on the circle's far side from
    /// the enemy. The spot is never nearer the enemy than the bot is. <paramref name="from"/>: the caster, for a bot at the
    /// very centre. Pure.
    /// </summary>
    public static Vector2 FireStepSpot(Vector2 self, Vector2 center, int radius, Vector2? enemy, Vector2 from)
    {
        float distance = Vector2.Distance(self, center);
        Vector2 away = distance >= 1 ? (self - center) / distance
            : Vector2.Distance(self, from) >= 1 ? Vector2.Normalize(self - from) : Vector2.UnitX;
        if (enemy is Vector2 foe && Vector2.Distance(self, foe) >= 1 && Vector2.Dot(away, Vector2.Normalize(foe - self)) > 0)
        {
            Vector2 back = center - foe;
            away = back.Length() >= 1 ? Vector2.Normalize(back) : -Vector2.Normalize(foe - self);
        }
        return center + away * (Math.Max(0, radius) + FireStepMargin);
    }

    /// <summary>Whether a pull mistake may be decided now: the switch's scope allowed it and the last one is far enough back. Pure.</summary>
    public static bool PullCooledDown(long now, long lastMistakeAt) =>
        lastMistakeAt <= 0 || now - lastMistakeAt >= PullMistakeCooldownMilliseconds;

    // ---- positioning --------------------------------------------------------------------------------------------------

    /// <summary>How far (degrees) an angle around a monster is from its front (0: straight in its face). Pure.</summary>
    public static float FrontGap(float angle)
    {
        float a = BotCombatMovement.Normalize(angle);
        return Math.Min(a, 360 - a);
    }

    /// <summary>
    /// The circling direction a badly placed bot takes: the one that ends nearer the monster's face (+1 clockwise, -1 the
    /// other way; 0 for no sweep). The arc is the same length as a good one, so the bot circles back out on its next step. Pure.
    /// </summary>
    public static int TowardFront(float angle, float sweep)
    {
        if (sweep <= 0)
            return 0;
        return FrontGap(angle + sweep) <= FrontGap(angle - sweep) ? 1 : -1;
    }

    /// <summary>Positioning mistakes are made only where they cost a style, never a life: not by the bot holding aggro (it
    /// stands in the face anyway), never against a named monster. Pure.</summary>
    public static bool PositionMistakeAllowed(bool holdsAggro, bool namedMonster) => !holdsAggro && !namedMonster;

    // ---- buffs --------------------------------------------------------------------------------------------------------

    /// <summary>A buff may lapse only where no boss fight is near: not in a dungeon (where the bosses are camped) and not with
    /// a named monster as the bot's target or committed pull. Pure.</summary>
    public static bool BuffLapseAllowed(bool inDungeon, bool namedTarget) => !inDungeon && !namedTarget;

    // ---- the log and the summary (pure) -------------------------------------------------------------------------------

    public const string MistakeTag = "BOT_SKILL_MISTAKE";
    public const string SummaryTag = "BOT_SKILL_MISTAKES";

    /// <summary>BOT_SKILL_MISTAKE kind= spared= name= person= realm= tier= chance= detail. Pure.</summary>
    public static string MistakeLine(BotSkillMistakeKind kind, bool spared, string name, BotPersonKey person, eRealm realm,
        RvrSkillTier tier, double chance, string detail) =>
        $"{MistakeTag} kind={KindName(kind)} spared={(spared ? "true" : "false")} name={name} person={person} " +
        $"realm={BotSkill.RealmName(realm)} tier={BotSkillProfile.TierName(tier)} " +
        $"chance={chance.ToString("0.000", CultureInfo.InvariantCulture)}" + (string.IsNullOrEmpty(detail) ? "" : " " + detail);

    public static readonly int Kinds = Enum.GetValues<BotSkillMistakeKind>().Length;

    /// <summary>The index of a count: kind, tier (0-2), spared (0 strangers, 1 the player's group).</summary>
    public static int CountIndex(BotSkillMistakeKind kind, RvrSkillTier tier, bool spared) =>
        ((int)kind * 3 + Math.Clamp((int)tier, 0, 2)) * 2 + (spared ? 1 : 0);

    public static int CountLength => Kinds * 3 * 2;

    /// <summary>
    /// BOT_SKILL_MISTAKES seconds= then per kind out.kind=c M/D,a M/D,v M/D (strangers: mistakes/decisions by tier) and
    /// in.kind=... (the player's group), then rate_out= rate_in= over the chance kinds (pull, buff, power, position: the
    /// played check's quarter), lines= suppressed=. Pure.
    /// </summary>
    public static string SummaryLine(long seconds, long[] mistakes, long[] decisions, long lines, long suppressed)
    {
        var text = new StringBuilder(512);
        text.Append(SummaryTag).Append(" seconds=").Append(seconds);
        long chanceMistakes0 = 0, chanceDecisions0 = 0, chanceMistakes1 = 0, chanceDecisions1 = 0;
        for (int k = 0; k < Kinds; k++)
        {
            var kind = (BotSkillMistakeKind)k;
            for (int s = 0; s < 2; s++)
            {
                text.Append(' ').Append(s == 0 ? "out." : "in.").Append(KindName(kind)).Append('=');
                for (int t = 0; t < 3; t++)
                {
                    int index = CountIndex(kind, (RvrSkillTier)t, s == 1);
                    long m = Get(mistakes, index), d = Get(decisions, index);
                    if (t > 0)
                        text.Append(',');
                    text.Append("cav"[t]).Append(m).Append('/').Append(d);
                    if (IsChance(kind))
                    {
                        if (s == 0) { chanceMistakes0 += m; chanceDecisions0 += d; }
                        else { chanceMistakes1 += m; chanceDecisions1 += d; }
                    }
                }
            }
        }
        text.Append(" rate_out=").Append(Rate(chanceMistakes0, chanceDecisions0))
            .Append(" rate_in=").Append(Rate(chanceMistakes1, chanceDecisions1))
            .Append(" lines=").Append(lines).Append(" suppressed=").Append(suppressed);
        return text.ToString();

        static long Get(long[] values, int index) => values != null && index < values.Length ? values[index] : 0;
        static string Rate(long m, long d) => d == 0 ? "-" : ((double)m / d).ToString("0.000", CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// The live side of work package 2: every hook asks here, and here asks <see cref="BotPeopleScope"/> first (rule Mistakes,
/// switch bot_skill_profiles): out of scope or switched off, the hook keeps today's rule and logs nothing. Reads the bot's
/// profile (<see cref="BotSkill.TryProfile"/>), rolls, counts each decision and mistake for BOT_SKILL_MISTAKES, logs
/// BOT_SKILL_MISTAKE (at most <see cref="LinesPerMinute"/> a minute) and credits WP1's learning events (pulls, buffs,
/// rests). Autonomous gamebots only; never companions, real players, raids or the duties the scope fences off.
/// </summary>
public static class BotSkillHands
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>BOT_SKILL_MISTAKE lines a minute at most; the rest are counted (suppressed=).</summary>
    public const int LinesPerMinute = 30;
    /// <summary>A per-tick reader (the rest floor, the spell choice) asks the scope again after this long, or at once in a new region.</summary>
    public const long ScopeCacheMilliseconds = 2_000;

    private sealed class Hands
    {
        public long ScopeUntil;
        public ushort Region;
        public bool Allowed;
        public bool InPlayerGroup;
        public long PullDecidedUntil;
        public bool PullCareless;
        public int PullMistakeObject;
        public long PullMistakeAt;
        public long LastBuffCredit, LastRestCredit, LastPullCredit;
    }

    private static readonly ConcurrentDictionary<BotPersonKey, Hands> Bots = new();
    private static readonly long[] Mistakes = new long[BotSkillMistakes.CountLength];
    private static readonly long[] Decisions = new long[BotSkillMistakes.CountLength];
    private static long _lines, _suppressed, _minute, _linesThisMinute;

    // ---- the gate -------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The hook's gate at an event: the scope (rule Mistakes, bot_skill_profiles) first, then the profile. False: keep
    /// today's rule. <paramref name="spared"/>: the quarter rate applies (a real player in the group, the switch on).
    /// </summary>
    public static bool Allows(GameBot bot, out BotSkillProfile profile, out bool spared)
    {
        profile = default;
        spared = false;
        BotPeopleSwitches switches = BotPeopleSwitches.Current;
        if (BotPeopleScope.Check(bot, BotPeopleRule.Mistakes, switches.SkillProfiles, out BotPeopleFacts facts) != BotPeopleRefusal.None ||
            !BotSkill.TryProfile(bot, out profile))
            return false;
        spared = BotSkillMistakes.Spared(facts.InPlayerGroup, switches.SparePlayerGroup);
        if (TryHands(bot, out Hands hands))
            Remember(hands, bot, true, facts.InPlayerGroup);
        return true;
    }

    /// <summary>
    /// The same for a reader that runs every tick (the rest floor, the spell choice): the scope's answer is kept for
    /// <see cref="ScopeCacheMilliseconds"/> (and asked again at once in another region), so the scope is asked at most
    /// every 2 s per bot.
    /// </summary>
    public static bool AllowsCached(GameBot bot, out BotSkillProfile profile, out bool spared)
    {
        profile = default;
        spared = false;
        if (!BotPeopleSwitches.Current.SkillProfiles || !TryHands(bot, out Hands hands))
            return false;
        long now = GameLoop.GameLoopTime;
        if (now >= Volatile.Read(ref hands.ScopeUntil) || hands.Region != bot.CurrentRegionID)
        {
            BotPeopleSwitches switches = BotPeopleSwitches.Current;
            bool allowed = BotPeopleScope.Check(bot, BotPeopleRule.Mistakes, switches.SkillProfiles, out BotPeopleFacts facts) ==
                           BotPeopleRefusal.None;
            Remember(hands, bot, allowed, facts.InPlayerGroup);
        }
        if (!hands.Allowed || !BotSkill.TryProfile(bot, out profile))
            return false;
        spared = BotSkillMistakes.Spared(hands.InPlayerGroup, BotPeopleSwitches.Current.SparePlayerGroup);
        return true;
    }

    private static void Remember(Hands hands, GameBot bot, bool allowed, bool inPlayerGroup)
    {
        hands.Allowed = allowed;
        hands.InPlayerGroup = inPlayerGroup;
        hands.Region = bot.CurrentRegionID;
        Volatile.Write(ref hands.ScopeUntil, GameLoop.GameLoopTime + ScopeCacheMilliseconds);
    }

    private static bool TryHands(GameBot bot, out Hands hands)
    {
        hands = null;
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return false;
        hands = Bots.GetOrAdd(person, static _ => new Hands());
        return true;
    }

    // ---- counting and the log ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Counts one decision (and the mistake, when made) for BOT_SKILL_MISTAKES. True when a BOT_SKILL_MISTAKE line may be
    /// written for it now (a mistake, inside the per-minute budget): only then does the caller build its detail and call
    /// <see cref="Write"/>, so nothing is formatted for a decision that logs nothing.
    /// </summary>
    private static bool Note(BotSkillMistakeKind kind, in BotSkillProfile profile, bool spared, bool mistake)
    {
        int index = BotSkillMistakes.CountIndex(kind, profile.Tier, spared);
        Interlocked.Increment(ref Decisions[index]);
        if (!mistake)
            return false;
        Interlocked.Increment(ref Mistakes[index]);
        return Log.IsInfoEnabled && TakeLine();
    }

    private static void Write(BotSkillMistakeKind kind, GameBot bot, in BotSkillProfile profile, bool spared, double chance, string detail) =>
        Log.Info(BotSkillMistakes.MistakeLine(kind, spared, bot.Name, BotPersonKey.Of(bot), bot.Realm, profile.Tier, chance, detail));

    private static bool TakeLine()
    {
        long minute = Environment.TickCount64 / 60_000;
        if (Interlocked.Exchange(ref _minute, minute) != minute)
            Interlocked.Exchange(ref _linesThisMinute, 0);
        if (Interlocked.Increment(ref _linesThisMinute) > LinesPerMinute)
        {
            Interlocked.Increment(ref _suppressed);
            return false;
        }
        Interlocked.Increment(ref _lines);
        return true;
    }

    private static IEnumerable<string> TakeSummary(long seconds)
    {
        var mistakes = new long[Mistakes.Length];
        var decisions = new long[Decisions.Length];
        for (int i = 0; i < mistakes.Length; i++)
        {
            mistakes[i] = Interlocked.Exchange(ref Mistakes[i], 0);
            decisions[i] = Interlocked.Exchange(ref Decisions[i], 0);
        }
        return [BotSkillMistakes.SummaryLine(seconds, mistakes, decisions, Interlocked.Exchange(ref _lines, 0), Interlocked.Exchange(ref _suppressed, 0))];
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // ---- reactions (BotIncomingFireReaction.Draw) ---------------------------------------------------------------------------

    /// <summary>
    /// The skill factor on a reaction hold: the bot's own tier, or in an RvR group with a fight board the group's tier with
    /// the member's reactions facet (owner decision 5). 1 (today's hold) when out of scope. Delays are never shortened for
    /// the player's group.
    /// </summary>
    public static double ReactionFactor(GameBot bot, out BotSkillProfile profile, out bool spared)
    {
        if (!Allows(bot, out profile, out spared))
            return 1;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        if (bot.Group is { MemberCount: >= 2 } && RvrFightBoards.PeekPlan(bot) is { } plan)
            return BotSkillMistakes.ReactionFactor(plan.SkillTier, profile.Position(BotSkillFacet.Reactions));
        return BotSkillMistakes.ReactionFactor(profile);
    }

    /// <summary>Counts a drawn reaction hold; a hold past the slowest an average person takes (<paramref name="late"/>) is a mistake.</summary>
    public static void NoteReaction(GameBot bot, in BotSkillProfile profile, bool spared, bool late, int holdMilliseconds, double factor)
    {
        if (Note(BotSkillMistakeKind.Reaction, profile, spared, late))
            Write(BotSkillMistakeKind.Reaction, bot, profile, spared, 0, $"hold_ms={holdMilliseconds} factor={F(factor)}");
    }

    // ---- positioning (BotBrain.CombatMovement) -------------------------------------------------------------------------------

    /// <summary>Whether this circle step ends badly placed (in the monster's face). Rolled at the step; counted.</summary>
    public static bool PositionMistake(GameBot bot, GameNPC monster, bool holdsAggro)
    {
        if (!BotSkillMistakes.PositionMistakeAllowed(holdsAggro, IsNamed(monster)) || !Allows(bot, out BotSkillProfile profile, out bool spared))
            return false;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        double chance = BotSkillMistakes.Chance(BotSkillMistakeKind.Position, profile, spared, true);
        bool mistake = BotSkillMistakes.Rolls(chance, Util.RandomDouble());
        if (Note(BotSkillMistakeKind.Position, profile, spared, mistake))
            Write(BotSkillMistakeKind.Position, bot, profile, spared, chance, $"monster=\"{monster?.Name}\"");
        return mistake;
    }

    /// <summary>A named monster: no realm, a name with a capital (AutonomousPlayerDeeds.IsNamed).</summary>
    public static bool IsNamed(GameLiving living) =>
        living is GameNPC npc and not GameBot && AutonomousPlayerDeeds.IsNamed(npc.Name, npc.Realm == eRealm.None, false);

    // ---- pulls (AutonomousGroupPullOrder) -----------------------------------------------------------------------------------

    /// <summary>
    /// The camp monster a party's puller takes by mistake (an index into <paramref name="pool"/>), or -1 for today's clean
    /// order. Decided once per <see cref="BotSkillMistakes.PullDecisionWindowMilliseconds"/> (the puller searches again
    /// every second or so until it pulls), at most once in 3 minutes, only with a safe add (<see cref="BotSkillMistakes.ChooseAddPull"/>).
    /// Credits WP1's PullMade at most once a minute.
    /// </summary>
    public static int PullMistake(GameBot puller, IReadOnlyList<GameNPC> pool)
    {
        if (puller == null || pool == null || pool.Count == 0 || !TryHands(puller, out Hands hands))
            return -1;
        long now = GameLoop.GameLoopTime;
        if (now < hands.PullDecidedUntil)
        {
            if (!hands.PullCareless)
                return -1;
            // A held careless pick is asked again before it fires: the scope (a raid muster, the player's lead...) and the
            // party (everyone still alive, three or more, outside dungeons).
            if (!BotSkillMistakes.HeldPullStands(Allows(puller, out _, out _), GroupOf(puller)))
            {
                hands.PullCareless = false;
                return -1;
            }
            return Find(pool, hands.PullMistakeObject);
        }
        if (!Allows(puller, out BotSkillProfile profile, out bool spared))
        {
            hands.PullDecidedUntil = now + BotSkillMistakes.PullDecisionWindowMilliseconds;
            hands.PullCareless = false;
            return -1;
        }
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        hands.PullDecidedUntil = now + BotSkillMistakes.PullDecisionWindowMilliseconds;
        hands.PullCareless = false;
        if (now - hands.LastPullCredit >= BotSkillGrowth.PullGapMilliseconds && BotSkill.Grow(puller, BotSkillEvent.PullMade))
            hands.LastPullCredit = now;
        double chance = BotSkillMistakes.Chance(BotSkillMistakeKind.Pull, profile, spared, true);
        bool careless = BotSkillMistakes.PullCooledDown(now, hands.PullMistakeAt) && BotSkillMistakes.Rolls(chance, Util.RandomDouble());
        BotPullGroup group = GroupOf(puller);
        int index = -1;
        if (careless && BotSkillMistakes.HeldPullStands(true, group))
        {
            BotPullView[] views = Views(puller, pool);
            index = BotSkillMistakes.ChooseAddPull(views, new Vector3(puller.X, puller.Y, puller.Z), group);
            // The pool holds only the camp's own monster: look at what really stands around the pick before taking it.
            if (index >= 0 && !ScanAllowsAdd(puller, pool[index], views[index], group.TopLevel))
                index = -1;
        }
        if (Note(BotSkillMistakeKind.Pull, profile, spared, index >= 0))
            Write(BotSkillMistakeKind.Pull, puller, profile, spared, chance,
                $"monster=\"{pool[index].Name}\" level={pool[index].Level} group={puller.Group?.MemberCount ?? 1}");
        if (index < 0)
            return -1;
        hands.PullCareless = true;
        hands.PullMistakeObject = pool[index].ObjectID;
        hands.PullMistakeAt = now;
        return index;
    }

    /// <summary>World scans for a careless pick's neighbours, server-wide, per second at most (a pick is decided once per
    /// 20 s per puller, so this is rarely reached; past it the clean pull stands).</summary>
    public const int AddScansPerSecond = 8;

    private static long _scanSecond, _scansThisSecond;

    private static bool TakeScan()
    {
        long second = Environment.TickCount64 / 1_000;
        if (Interlocked.Exchange(ref _scanSecond, second) != second)
            Interlocked.Exchange(ref _scansThisSecond, 0);
        return Interlocked.Increment(ref _scansThisSecond) <= AddScansPerSecond;
    }

    /// <summary>
    /// One radius scan around the careless pick (its aggro range), at the decision only and rate-limited: every live
    /// monster there (no realm, not a pet, not a bot, not peaceful), whatever its name, goes to
    /// <see cref="BotSkillMistakes.AddScanAllows"/>.
    /// </summary>
    private static bool ScanAllowsAdd(GameBot puller, GameNPC pick, in BotPullView view, int groupTopLevel)
    {
        if (view.AggroRange <= 0 || !TakeScan())
            return false;
        var around = new List<BotPullView>(4);
        foreach (GameNPC npc in pick.GetNPCsInRadius((ushort)Math.Min(view.AggroRange, ushort.MaxValue)))
        {
            if (npc == null || npc == pick || !npc.IsAlive || npc is GameBot || npc.Realm != eRealm.None ||
                npc.Brain is DOL.AI.Brain.IControlledBrain || (npc.Flags & GameNPC.eFlags.PEACE) != 0)
                continue;
            around.Add(new BotPullView(new Vector3(npc.X, npc.Y, npc.Z), (npc.Brain as DOL.AI.Brain.IOldAggressiveBrain)?.AggroRange ?? 0,
                npc.EffectiveLevel, (int)ConLevels.GetConColor(puller.GetConLevel(npc)), IsNamed(npc)));
            if (around.Count > 1)
                return false;
        }
        return BotSkillMistakes.AddScanAllows(view, around, groupTopLevel);
    }

    private static int Find(IReadOnlyList<GameNPC> pool, int objectId)
    {
        for (int i = 0; i < pool.Count; i++)
            if (pool[i]?.ObjectID == objectId)
                return i;
        return -1;
    }

    private static BotPullView[] Views(GameBot puller, IReadOnlyList<GameNPC> pool)
    {
        var views = new BotPullView[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            GameNPC npc = pool[i];
            views[i] = new BotPullView(new Vector3(npc.X, npc.Y, npc.Z), (npc.Brain as DOL.AI.Brain.IOldAggressiveBrain)?.AggroRange ?? 0,
                npc.EffectiveLevel, (int)ConLevels.GetConColor(puller.GetConLevel(npc)), IsNamed(npc));
        }
        return views;
    }

    private static BotPullGroup GroupOf(GameBot bot)
    {
        Group group = bot.Group;
        if (group == null)
            return new BotPullGroup(1, bot.Level, bot.IsAlive, bot.CurrentZone?.IsDungeon == true);
        int top = 0;
        bool alive = true;
        foreach (GameLiving member in group.GetMembersInTheGroup())
        {
            top = Math.Max(top, member.Level);
            alive &= member.IsAlive;
        }
        return new BotPullGroup(group.MemberCount, top, alive, bot.CurrentZone?.IsDungeon == true);
    }

    // ---- buffs (BotBuffLapses, the maintenance pass) -------------------------------------------------------------------------

    /// <summary>
    /// How long (ms) the bot lets this expired buff stay off: 0 renews it at once (today). Decided once per expiry (the
    /// caller remembers it, <see cref="BotBuffLapses{T}"/>); never in a dungeon or with a named in play (the brain's
    /// <paramref name="namedInPlay"/>: its ordered or committed pull and aggro list; here its own and its groupmates' targets).
    /// </summary>
    public static long BuffLapse(GameBot bot, GameLiving target, Spell spell, bool namedInPlay)
    {
        if (bot == null || !BotSkillMistakes.BuffLapseAllowed(bot.CurrentZone?.IsDungeon == true,
                namedInPlay || IsNamed(bot.TargetObject as GameLiving) || GroupTargetsNamed(bot)) ||
            !Allows(bot, out BotSkillProfile profile, out bool spared))
            return 0;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        double chance = BotSkillMistakes.Chance(BotSkillMistakeKind.Buff, profile, spared, true);
        bool mistake = BotSkillMistakes.Rolls(chance, Util.RandomDouble());
        long lapse = mistake ? BotSkillMistakes.BuffLapseMilliseconds(profile, Util.RandomDouble()) : 0;
        if (Note(BotSkillMistakeKind.Buff, profile, spared, mistake))
            Write(BotSkillMistakeKind.Buff, bot, profile, spared, chance, $"spell=\"{spell?.Name}\" target=\"{target?.Name}\" lapse_s={lapse / 1000}");
        return lapse;
    }

    /// <summary>
    /// Asked at every maintenance pass while a lapse runs (BotBuffLapses): it holds only while the scope still allows
    /// mistakes (no raid, assault, keep fight, ride...), the bot is not in a dungeon and no named is in play for it or its
    /// group (<paramref name="namedInPlay"/>: targets, the ordered or committed pull and the aggro list, read by the brain).
    /// False: the buff is renewed now.
    /// </summary>
    public static bool LapseStillAllowed(GameBot bot, bool namedInPlay) =>
        bot != null && BotSkillMistakes.LapseHolds(Allows(bot, out _, out _), bot.CurrentZone?.IsDungeon == true,
            namedInPlay || IsNamed(bot.TargetObject as GameLiving) || GroupTargetsNamed(bot));

    /// <summary>Whether a groupmate's target is a named monster (a boss pull may be under way).</summary>
    public static bool GroupTargetsNamed(GameBot bot)
    {
        if (bot?.Group == null)
            return false;
        foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
            if (member != bot && IsNamed(member.TargetObject as GameLiving))
                return true;
        return false;
    }

    /// <summary>A buff was cast: WP1's BuffCast lesson, at most once a minute.</summary>
    public static void NoteBuffCast(GameBot bot)
    {
        if (bot == null || !TryHands(bot, out Hands hands))
            return;
        long now = GameLoop.GameLoopTime;
        if (now - hands.LastBuffCredit < BotSkillGrowth.BuffGapMilliseconds)
            return;
        hands.LastBuffCredit = now;
        BotSkill.Grow(bot, BotSkillEvent.BuffCast);
    }

    // ---- power (BotSpellPower, BotRestRecovery) ------------------------------------------------------------------------------

    /// <summary>
    /// The damage spell a caster casts: <paramref name="picked"/> (today's choice), or by mistake the most expensive damage
    /// spell among <paramref name="spells"/> when that leaves it its reserve. Counted only where a dearer damage spell exists.
    /// </summary>
    public static Spell ChooseSpell(GameBot bot, IReadOnlyList<Spell> spells, Spell picked)
    {
        if (bot == null || picked == null || spells == null || spells.Count < 2 || !BotCasterPriority.IsDamage(picked) ||
            !AllowsCached(bot, out BotSkillProfile profile, out bool spared))
            return picked;
        int pickedCost = bot.PowerCost(picked);
        Spell dearest = null;
        int dearestCost = pickedCost;
        foreach (Spell spell in spells)
        {
            if (spell == null || !BotCasterPriority.IsDamage(spell))
                continue;
            int cost = bot.PowerCost(spell);
            if (cost > dearestCost)
            {
                dearest = spell;
                dearestCost = cost;
            }
        }
        if (dearest == null)
            return picked;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        double chance = BotSkillMistakes.Chance(BotSkillMistakeKind.Power, profile, spared, true);
        bool solo = bot.Group is not { MemberCount: >= 2 };
        bool mistake = BotSkillMistakes.Rolls(chance, Util.RandomDouble()) &&
                       BotSkillMistakes.ExpensiveCastAllowed(bot.Mana, bot.MaxMana, dearestCost, solo);
        if (Note(BotSkillMistakeKind.Power, profile, spared, mistake))
            Write(BotSkillMistakeKind.Power, bot, profile, spared, chance,
                $"spell=\"{dearest.Name}\" cost={dearestCost} instead=\"{picked.Name}\" cost_instead={pickedCost} power={bot.ManaPercent}");
        return mistake ? dearest : picked;
    }

    /// <summary>
    /// The power (percent) below which this bot sits to rest (<see cref="BotSkillMistakes.PowerRestFloor"/>); today's 45 out
    /// of scope. Read every tick: the scope is cached (<see cref="AllowsCached"/>). A bot resting for power now and then
    /// credits WP1's RestedForPower (at most every 5 minutes).
    /// </summary>
    public static byte PowerRestFloor(GameBot bot)
    {
        if (bot == null || !AllowsCached(bot, out BotSkillProfile profile, out bool spared) || bot.MaxMana <= 0)
            return BotSkillMistakes.TodayPowerRestFloor;
        bool soloCaster = bot.Group is not { MemberCount: >= 2 } && BotSpellPower.IsOffensiveCaster(bot);
        int floor = BotSkillMistakes.PowerRestFloor(profile, spared, true, soloCaster);
        if (bot.IsRecoveryResting && bot.ManaPercent < 100 && TryHands(bot, out Hands hands))
        {
            long now = GameLoop.GameLoopTime;
            if (now - hands.LastRestCredit >= BotSkillGrowth.RestGapMilliseconds)
            {
                hands.LastRestCredit = now;
                BotSkill.Grow(bot, BotSkillEvent.RestedForPower);
            }
        }
        return (byte)floor;
    }

    // ---- standing in fire (BotBrain.RvrPositioning) -------------------------------------------------------------------------

    /// <summary>
    /// The delay (ms) before a member of an RvR group steps out of an area effect: inside the group tier's band (Casual 2-5 s,
    /// Average 1-3 s, Veteran 0.5-1.5 s), moved by the member's fire facet (owner decision 5). -1: out of scope, today's
    /// rule (it does not step out).
    /// </summary>
    public static long FireDelay(GameBot bot, RvrSkillTier groupTier)
    {
        if (!Allows(bot, out BotSkillProfile profile, out bool spared))
            return -1;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        long delay = BotSkillMistakes.FireDelayMilliseconds(groupTier, profile.Position(BotSkillFacet.Fire), Util.RandomDouble());
        bool stood = delay >= BotSkillMistakes.FireMistakeMilliseconds;
        if (Note(BotSkillMistakeKind.Fire, profile, spared, stood))
            Write(BotSkillMistakeKind.Fire, bot, profile, spared, 0, $"delay_ms={delay} group_tier={BotSkillProfile.TierName(groupTier)}");
        return delay;
    }

    /// <summary>
    /// The delay (ms) before a member of an RvR group walks to its place once out of it: RvrPositioning's band for the group's
    /// tier, moved by the member's positioning facet (owner decision 5). <paramref name="today"/> out of scope.
    /// </summary>
    public static long RvrPlaceDelay(GameBot bot, RvrSkillTier groupTier, long today)
    {
        if (!AllowsCached(bot, out BotSkillProfile profile, out _))
            return today;
        var (min, max) = RvrPositioning.ReactionBand(groupTier);
        return BotSkillMistakes.SpreadInBand(min, max, Util.RandomDouble(), profile.Position(BotSkillFacet.Positioning));
    }

    // ---- life cycle ---------------------------------------------------------------------------------------------------------

    private static int _wired;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args) => Wire();

    /// <summary>Hooks the logout event and the 5-minute summary (once).</summary>
    public static void Wire()
    {
        if (Interlocked.Exchange(ref _wired, 1) != 0)
            return;
        BotPeopleStore.LoggedOut += (person, _) => Bots.TryRemove(person, out Hands _);
        BotPeopleSummary.Register(BotSkillMistakes.SummaryTag, TakeSummary);
    }

    /// <summary>Forgets every bot's hands and the counts (tests).</summary>
    public static void ResetForTests()
    {
        Bots.Clear();
        Array.Clear(Mistakes);
        Array.Clear(Decisions);
    }

    /// <summary>The counts since the last take (tests): mistakes and decisions by <see cref="BotSkillMistakes.CountIndex"/>.</summary>
    public static (long[] Mistakes, long[] Decisions) PeekCounts() => ((long[])Mistakes.Clone(), (long[])Decisions.Clone());
}
