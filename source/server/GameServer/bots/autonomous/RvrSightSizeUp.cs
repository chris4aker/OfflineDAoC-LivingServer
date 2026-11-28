using System;
using System.Numerics;

namespace DOL.GS;

/// <summary>What a solo or duo gamebot knows when enemies come into its perception range (#662).</summary>
public readonly record struct RvrSightFacts
{
    public int OwnLevel { get; init; }
    /// <summary>The bot and the realm-mates alive within <see cref="RvrSightSizeUp.AllyRadius"/>: everyone near, for the odds.</summary>
    public int Allies { get; init; }
    /// <summary>Of those, the bot and the ones who would really fight with it: its group, its warband, its bond friends and
    /// real players. The hard rule counts only these (a stranger in range improves the odds, never makes a bot "not alone").
    /// 0: the same as <see cref="Allies"/>.</summary>
    public int Fighters { get; init; }
    /// <summary>The average level of those allies (the bot's own level when it is alone).</summary>
    public int AllyAverageLevel { get; init; }
    /// <summary>Enemy people (players and gamebots) the bot has noticed: perceived, never off-screen.</summary>
    public int Enemies { get; init; }
    /// <summary>Of those, the main healers (Cleric, Druid, Healer).</summary>
    public int EnemyHealers { get; init; }
    public int EnemyAverageLevel { get; init; }
    public int EnemyTopLevel { get; init; }
    /// <summary>An enemy is attacking the bot or a friend near it: fighting back is always allowed.</summary>
    public bool Attacked { get; init; }
}

public enum RvrSightCall { Engage, BackOff }

/// <summary>The call and why, for the log row.</summary>
public readonly record struct RvrSightDecision(RvrSightCall Call, double Odds, double Line, bool Mistake, string Reason);

/// <summary>
/// #662 (owner decision 2026-11-26): a solo or duo autonomous gamebot sizes up a fight when the enemies come into its
/// perception range, in the frontier and the battlegrounds (keep assaults included), and either opens or backs off. The
/// odds weigh both sides by head count (a main healer counts twice) and level. Boldness (personality) moves the line: a
/// bolder bot takes worse odds; nobody needs better than even odds, so every bot takes a fair fight. A casual bot sometimes
/// makes a mild bad call: a misread opens on odds a little short of its line, never far short, and never makes it pass up a
/// fight its line takes. No solo bot ever opens on <see cref="SoloNeverOpensOn"/> or more enemies, whatever the roll. A bot
/// that is attacked always fights back. Period reading: docs/RVR-FIELD-1.2.md, "Soloers and stealthers". Pure.
/// </summary>
public static class RvrSightSizeUp
{
    public const string PropertyKey = "bot_rvr_sight_size_up";

    /// <summary>Realm-mates this close count as the bot's side (as <see cref="RvrSoloEngage.GroupRadius"/>).</summary>
    public const int AllyRadius = 1_500;
    /// <summary>Owner 2026-11-26: "no solo bot ever opens on 3 or more enemies".</summary>
    public const int SoloNeverOpensOn = 3;
    /// <summary>Each level of difference is worth this much strength (about one level in twelve: 8%).</summary>
    public const double LevelStep = 1.08;
    /// <summary>The line at <see cref="BoldnessAnchor"/>: even odds.</summary>
    public const double EvenLine = 1.0;
    /// <summary>The boldness that takes even odds: a middling Risk trait (AutonomousRvrPerception.Boldness gives an ordinary
    /// bot Risk/9 x 0.8, so Risk 4-5 is about 0.4; a bot with no personality is 0.5).</summary>
    public const double BoldnessAnchor = 0.4;
    /// <summary>How far the line falls per unit of boldness above <see cref="BoldnessAnchor"/>: a Daredevil (1.0) takes 0.64.
    /// Below the anchor the line stays at even odds.</summary>
    public const double BoldnessSlope = 0.6;
    /// <summary>A misread flips only a call this close to the line ("a mild bad call").</summary>
    public const double MistakeBand = 0.2;

    /// <summary>How far the bot backs off from the nearest enemy, past its notice range.</summary>
    public const int BackOffDistance = 2_000;
    /// <summary>How long a bot waits nearby before it goes back to what it was doing.</summary>
    public const long WaitMilliseconds = 30_000;
    /// <summary>A bot logs the same call again at most this often.</summary>
    public const long RelogMilliseconds = 30_000;

    /// <summary>
    /// Whether the rule covers a party of this size: a lone bot always; a duo only where RvrEngagement's stance machine has no
    /// opinion (field tactics off, or a battleground keep assault, where RvrTacticsScope turns them off), so a duo is never
    /// sized up twice or held by two movers. A group of three or more keeps its own stance logic (owner 2026-11-26). Pure.
    /// </summary>
    public static bool CoversParty(int members, bool stanceHasOpinion) => members <= 1 || members == 2 && !stanceHasOpinion;

    /// <summary>
    /// One step of a back-off hold: null to keep holding, or why it ends (the bot then goes back to what it was doing):
    /// "none" when no hold runs, then "dead", "attacked" (the bot or a group member is in a fight: fight back), "waited"
    /// and "out_of_scope". Pure.
    /// </summary>
    public static string HoldStep(long now, long holdUntil, bool alive, bool attacked, bool inScope) =>
        holdUntil == 0 ? "none" : !alive ? "dead" : attacked ? "attacked" : now >= holdUntil ? "waited" :
        !inScope ? "out_of_scope" : null;

    /// <summary>A back-off decision while holding or not: the new end of the hold, and whether it is a fresh back-off (only a
    /// fresh one walks away and calls friends; a repeat while waiting just extends the wait). Pure.</summary>
    public static (long Until, bool Fresh) Rearm(long now, long holdUntil) => (now + WaitMilliseconds, holdUntil <= now);

    /// <summary>#766: a failed order whose destination lies this close to the back-off point is the back-off's own (as
    /// AutonomousForeignRouteFailure.RouteRadius: the mover may move the point onto its floor).</summary>
    public const float BackOffOrderRadius = 96f;

    /// <summary>
    /// #766: whether a pending mover failure belongs to the hold's back-off walk, not to the route the hold paused. The
    /// back-off is a plain PathTo outside the route's own orders, so its NoPathFound used to be charged to the travel goal
    /// when the route resumed (three of them dropped the goal: AUTONOMOUS_ROUTE_RECOVERY). Null: no back-off walk was
    /// ordered, so a failure is the route's. Pure.
    /// </summary>
    public static bool FailureIsBackOffs(Vector3? backOffOrder, Vector3 failed) =>
        backOffOrder is Vector3 order && Vector3.DistanceSquared(order, failed) <= BackOffOrderRadius * BackOffOrderRadius;

    /// <summary>The line the odds must reach, by boldness (0 to 1; 0.5 when the bot has no personality). Bolder: lower;
    /// never above even odds.</summary>
    public static double LineFor(double boldness) =>
        EvenLine - Math.Max(0, Math.Clamp(boldness, 0, 1) - BoldnessAnchor) * BoldnessSlope;

    /// <summary>Strength of one side: heads (a main healer twice) weighted by level against the other side's level.</summary>
    public static double Strength(int heads, int healers, int level, int otherLevel) =>
        Math.Max(0, heads + Math.Max(0, healers)) * Math.Pow(LevelStep, Math.Clamp(level - otherLevel, -20, 20));

    /// <summary>Our strength over theirs, both sides weighed against each other's average level.</summary>
    public static double Odds(in RvrSightFacts facts)
    {
        int ours = Math.Max(1, facts.Allies);
        int ourLevel = facts.AllyAverageLevel > 0 ? facts.AllyAverageLevel : facts.OwnLevel;
        // The strongest enemy counts for the whole group's level when he towers over it (a grey can't be outnumbered away).
        int theirLevel = Math.Max(facts.EnemyAverageLevel, facts.EnemyTopLevel - 3);
        double theirs = Strength(Math.Max(1, facts.Enemies), facts.EnemyHealers, theirLevel, ourLevel);
        return ours / Math.Max(0.01, theirs);
    }

    /// <summary>
    /// The decision. <paramref name="roll"/> is 0 to 1, held per bot and contact; below the tier's
    /// <see cref="RvrSoloEngage.MistakeChance"/> it flips a close call. Pure.
    /// </summary>
    public static RvrSightDecision Decide(in RvrSightFacts facts, double boldness, RvrSkillTier tier, double roll)
    {
        double line = LineFor(boldness);
        double odds = Odds(facts);
        if (facts.Attacked)
            return new(RvrSightCall.Engage, odds, line, false, "attacked");
        if (facts.Enemies <= 0)
            return new(RvrSightCall.Engage, odds, line, false, "no_enemy");
        int fighters = facts.Fighters > 0 ? facts.Fighters : facts.Allies;
        if (fighters <= 1 && facts.Enemies >= SoloNeverOpensOn)
            return new(RvrSightCall.BackOff, odds, line, false, "solo_outnumbered");
        bool read = odds >= line;
        // The bad call is the one in the report: opening on odds a little short of the line. Never far short.
        bool mistake = !read && line - odds <= MistakeBand && roll < RvrSoloEngage.MistakeChance(tier);
        bool engage = read || mistake;
        return new(engage ? RvrSightCall.Engage : RvrSightCall.BackOff, odds, line, mistake,
            mistake ? "misread" : read ? "odds" : "outmatched");
    }
}
