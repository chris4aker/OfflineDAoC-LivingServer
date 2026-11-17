using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Bots as people for 1.2, item 5 (docs/BOTS-AS-PEOPLE-1.2.md, "Movement that doesn't look scripted"; work package 5): the
/// pure rules of the movement habits that are not the stick line (<see cref="BotStickLine"/>) or the keep walls
/// (<see cref="BotKeepWallSpots"/>). Each is a 2003 habit (ERA-RESEARCH sections 3, 5, 6 and 9; Prima), and each has a
/// delay, a spread and a share by skill tier (principle 1):
/// <list type="bullet">
/// <item>Circle-strafe: a melee gamebot in an open-field RvR fight chains short side legs around its target every 3-8 s,
/// out of the target's front and inside its own positional window. Casual 20% of fights, Average 50%, Veteran 85%.</item>
/// <item>Face the enemy: a healer with an enemy melee fighter on it runs its kite facing it, in short legs along an arc
/// ("run a bit, throw heal"), instead of turning its back; a caster backs off 80 units facing it. The kite's distance and
/// speed are kept. Casual 30% of contacts, Average 60%, Veteran 90%.</item>
/// <item>Overshoot: arriving on foot at the bank, a merchant, a trainer or a crowd in town, it runs 30-60 units past and
/// steps back. Casual 25% of arrivals, Average 15%, Veteran 8%.</item>
/// <item>Sit while waiting: waiting more than 30 s for its group, at a rendezvous or at the bank, it sits (emote 0xF0) and
/// stands at once to move, cast or fight. The same for all tiers: 60% of waits, after 30-50 s.</item>
/// <item>Crowd the bank: with 4 or more bots already at a vault keeper, a bot that would join a crowd joins the bank's.</item>
/// </list>
/// Every walk is legs of at most <see cref="LegLength"/> (BotCombatMovement.LegLength) proven on dry ground of the
/// bot's own floor; at most <see cref="LegChecksPerSecond"/> such checks a second server-wide, none while the game loop is
/// busy. In RvR the tier is the group's (owner decision 5); elsewhere the bot's own, from its id with this package's salt.
/// No game world here: the runtime is BotMovementHabitsRuntime and BotBrain.MovementHabits.
/// </summary>
public static class BotMovementHabits
{
    /// <summary>One leg: shorter than the 25 units past which the server turns an NPC to walk (BotCombatMovement).</summary>
    public const float LegLength = BotCombatMovement.LegLength;

    // ---- the bot's own tier (outside RvR) ---------------------------------------------------------------------------

    private const ulong TierSalt = 0x4D4F5645; // "MOVE"

    /// <summary>
    /// The movement tier of a bot outside an RvR fight: RVR-FIELD-1.2's shares (30/50/20, moved by rvr_bot_awareness)
    /// rolled from the bot's id with this package's own salt, so it does not follow its group's tier. Pure.
    /// </summary>
    public static RvrSkillTier TierOf(long botId, RvrBotAwareness awareness) =>
        RvrGroupSkill.TierForRoll(AutonomousBotPersonality.Mix(botId, TierSalt) % 10_000 / 100.0, 0,
            RvrGroupSkill.Shares(awareness));

    /// <summary>The tier the habits use for a bot: its skill profile's when it has one (work package 1), else
    /// <see cref="TierOf(long, RvrBotAwareness)"/>. Pure; BotMovementHabitsRuntime.TierOf is its one caller.</summary>
    public static RvrSkillTier TierFor(RvrSkillTier? profileTier, long botId, RvrBotAwareness awareness) =>
        profileTier ?? TierOf(botId, awareness);

    /// <summary>A fixed roll in [0, 1) for a bot, an occasion and a salt (no random number, no clock).</summary>
    public static double Roll(long botId, long occasion, ulong salt) =>
        AutonomousBotPersonality.Mix(unchecked(botId * 31 + occasion * 0x2545F491), salt) % 1_000_000 / 1_000_000.0;

    private static double Share(RvrSkillTier tier, double casual, double average, double veteran) => tier switch
    {
        RvrSkillTier.Casual => casual,
        RvrSkillTier.Veteran => veteran,
        _ => average,
    };

    // ---- circle-strafe (melee, open-field RvR) ----------------------------------------------------------------------

    public const double StrafeCasual = 0.20, StrafeAverage = 0.50, StrafeVeteran = 0.85;
    /// <summary>Chained side legs every 3-8 s.</summary>
    public const long StrafeIntervalMinimumMs = 3_000;
    public const long StrafeIntervalMaximumMs = 8_000;
    /// <summary>The first strafe of a fight waits until the bot stood in melee this long.</summary>
    public const long StrafeSettleMs = 1_500;
    /// <summary>A strafe is 30-66 units of arc (one to three legs), at most <see cref="StrafeMaxSweepDegrees"/> around.</summary>
    public const float StrafeArcMinimum = 30f;
    public const float StrafeArcMaximum = 66f;
    public const float StrafeMaxSweepDegrees = 60f;
    /// <summary>Only from melee range: at most this far past the bot's melee reach.</summary>
    public const int StrafeReachSlack = 40;
    private const ulong StrafeSalt = 0x53545246; // "STRF"

    public static double StrafeShare(RvrSkillTier tier) => Share(tier, StrafeCasual, StrafeAverage, StrafeVeteran);

    /// <summary>Whether this bot strafes in its fight against this target (one roll a fight, by tier). Pure.</summary>
    public static bool StrafesThisFight(RvrSkillTier tier, long botId, long targetId) =>
        Roll(botId, targetId, StrafeSalt) < StrafeShare(tier);

    /// <summary>Milliseconds to the next strafe; <paramref name="roll"/> in [0, 1).</summary>
    public static long NextStrafeDelay(double roll) =>
        StrafeIntervalMinimumMs + (long)(Math.Clamp(roll, 0, 0.999999) * (StrafeIntervalMaximumMs - StrafeIntervalMinimumMs));

    /// <summary>
    /// Who strafes: a melee fighter (not a ranged caster, no bow in hand), hitting an enemy player or bot it stands in melee
    /// range of, neither of them running, and the bot not casting, stealthed, crowd-controlled or turning-disabled. Pure.
    /// </summary>
    public static bool StrafeAllowed(bool meleeWeapon, bool rangedCaster, bool attacking, bool enemyPlayerOrBot,
        bool inMeleeRange, bool botMoving, bool targetMoving, bool casting, bool stealthed, bool held) =>
        meleeWeapon && !rangedCaster && attacking && enemyPlayerOrBot && inMeleeRange && !botMoving && !targetMoving &&
        !casting && !stealthed && !held;

    /// <summary>The degrees a strafe goes around a target at <paramref name="radius"/>; <paramref name="roll"/> in [0, 1).</summary>
    public static float StrafeSweep(float radius, double roll)
    {
        if (radius <= 1)
            return 0;
        float arc = StrafeArcMinimum + (float)Math.Clamp(roll, 0, 0.999999) * (StrafeArcMaximum - StrafeArcMinimum);
        return MathF.Min(StrafeMaxSweepDegrees, arc / radius * 180f / MathF.PI);
    }

    // ---- face the enemy (casters and healers under melee, open-field RvR) -------------------------------------------

    public const double FaceCasual = 0.30, FaceAverage = 0.60, FaceVeteran = 0.90;
    /// <summary>A back-off is up to four legs of 20 units (80 units), each bent a little: an arc, never a straight run.</summary>
    public const int FaceLegs = 4;
    public const float FaceLegLength = 20f;
    /// <summary>Each leg turns at most this many degrees from the one before (toward the refuge, else by the roll).</summary>
    public const float FaceBendDegrees = 14f;
    /// <summary>A caster backs off again at most this often (a healer uses its kite's heal window).</summary>
    public const long FaceRetryMinimumMs = 3_000;
    public const long FaceRetryMaximumMs = 5_000;
    private const ulong FaceSalt = 0x46414345; // "FACE"

    public static double FaceShare(RvrSkillTier tier) => Share(tier, FaceCasual, FaceAverage, FaceVeteran);

    /// <summary>Whether a caster or healer faces the melee fighter on it this time (one roll a contact, by tier). Pure.</summary>
    public static bool FacesThisContact(RvrSkillTier tier, long botId, long contact) =>
        Roll(botId, contact, FaceSalt) < FaceShare(tier);

    public static long NextFaceDelay(double roll) =>
        FaceRetryMinimumMs + (long)(Math.Clamp(roll, 0, 0.999999) * (FaceRetryMaximumMs - FaceRetryMinimumMs));

    /// <summary>
    /// The ends of a back-off from <paramref name="attacker"/>: <see cref="FaceLegs"/> legs of <see cref="FaceLegLength"/>,
    /// the first straight away from the attacker, each next one bent up to <see cref="FaceBendDegrees"/> toward
    /// <paramref name="refuge"/> (a guard or the tanks' line) when there is one, else to the side of <paramref name="roll"/>.
    /// Every leg is shorter than <see cref="LegLength"/>, so the bot keeps facing the attacker as it backs. Pure.
    /// </summary>
    public static Vector2[] BackOffLegs(Vector2 self, Vector2 attacker, Vector2? refuge, double roll, int legs = FaceLegs)
    {
        Vector2 away = self - attacker;
        if (away.LengthSquared() < 1 || legs <= 0)
            return [];
        away = Vector2.Normalize(away);
        float bend = FaceBendDegrees * MathF.PI / 180f;
        int side = roll < 0.5 ? 1 : -1;
        var points = new Vector2[legs];
        Vector2 at = self, direction = away;
        for (int i = 0; i < legs; i++)
        {
            if (i > 0)
            {
                int turn = side;
                if (refuge is Vector2 r && (r - at).LengthSquared() > 1)
                {
                    Vector2 toward = Vector2.Normalize(r - at);
                    float cross = direction.X * toward.Y - direction.Y * toward.X;
                    turn = cross >= 0 ? 1 : -1;
                }
                direction = Rotate(direction, turn * bend);
                // Never back toward the attacker: the arc stays on the far side of it.
                if (Vector2.Dot(direction, away) < 0.25f)
                    direction = Rotate(direction, -turn * bend);
            }
            at += direction * FaceLegLength;
            points[i] = at;
        }
        return points;
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return new(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    // ---- overshoot (arrivals in town) -------------------------------------------------------------------------------

    public const double OvershootCasual = 0.25, OvershootAverage = 0.15, OvershootVeteran = 0.08;
    /// <summary>At least 30 past: farther than the hangout's 28-unit arrival (AutonomousTownHangouts.ArriveTolerance), so
    /// the end of an overshoot never counts as arrived.</summary>
    public const float OvershootMinimum = 30f;
    public const float OvershootMaximum = 60f;
    /// <summary>A walk shorter than this is a shuffle, not an arrival: no overshoot.</summary>
    public const float OvershootMinimumWalk = 150f;
    /// <summary>It notices after 150-600 ms and steps back.</summary>
    public const long StepBackMinimumMs = 150;
    public const long StepBackMaximumMs = 600;
    private const ulong OvershootSalt = 0x4F565253; // "OVRS"

    public static double OvershootShare(RvrSkillTier tier) => Share(tier, OvershootCasual, OvershootAverage, OvershootVeteran);

    /// <summary>Whether this arrival overshoots (one roll an arrival, by the bot's tier). Pure.</summary>
    public static bool Overshoots(RvrSkillTier tier, long botId, long arrival, float walk) =>
        walk >= OvershootMinimumWalk && Roll(botId, arrival, OvershootSalt) < OvershootShare(tier);

    public static float OvershootDistance(double roll) =>
        OvershootMinimum + (float)Math.Clamp(roll, 0, 0.999999) * (OvershootMaximum - OvershootMinimum);

    public enum HangoutStep { WalkToSpot, WalkPast, StepBack, SteppingBack, Arrived }

    /// <summary>
    /// One turn of the walk to a town hangout with an overshoot (AutonomousTownIdleRouting): while it steps back, the
    /// step-back owns the walk (no new route over it) until it stands at the spot; with an overshoot pending it walks on
    /// past the spot (passing through it does not count) and steps back once at the spot past it; with none it walks
    /// to the spot as before. <paramref name="overshootPending"/>: an overshoot not yet stepped back. Pure.
    /// </summary>
    public static HangoutStep Hangout(bool atSpot, bool overshootPending, bool atPast, bool moving, bool steppingBack)
    {
        if (steppingBack)
            return atSpot && !moving ? HangoutStep.Arrived : HangoutStep.SteppingBack;
        if (overshootPending)
        {
            if (atPast)
                return HangoutStep.StepBack;
            if (moving || !atSpot)
                return HangoutStep.WalkPast;
        }
        return atSpot ? HangoutStep.Arrived : HangoutStep.WalkToSpot;
    }

    /// <summary>
    /// The face-and-back-off of a healer that would kite: the same run as its kite (from <paramref name="self"/> to the
    /// kite's spot, 220-330 units), walked in legs of at most <see cref="FaceLegLength"/> facing the attacker, along a
    /// slight arc (bulging up to <see cref="FaceArcBulge"/> of the run to the side of <paramref name="roll"/>). The habit
    /// changes facing and the path's shape, never the safety distance. At most <see cref="FaceMaxLegs"/> legs. Pure.
    /// </summary>
    public static Vector2[] ArcLegsTo(Vector2 self, Vector2 spot, double roll)
    {
        float run = Vector2.Distance(self, spot);
        if (run < 1)
            return [];
        Vector2 along = (spot - self) / run;
        Vector2 side = new(-along.Y, along.X);
        float bulge = run * FaceArcBulge * (roll < 0.5 ? 1 : -1);
        // The arc is a little longer than the run; legs of the arc stay under FaceLegLength.
        int legs = Math.Clamp((int)MathF.Ceiling(run * 1.05f / FaceLegLength), 1, FaceMaxLegs);
        var points = new Vector2[legs];
        for (int i = 1; i <= legs; i++)
        {
            float t = i / (float)legs;
            points[i - 1] = self + (spot - self) * t + side * (bulge * 4 * t * (1 - t));
        }
        return points;
    }

    /// <summary>
    /// Whether a proven face walk is taken: a healer's face-back only when the mesh allows at least
    /// <see cref="FaceMinimumShare"/> of its kite's run (else it kites as today, part way allowed); a caster's back-off
    /// with at least two legs (backed into a wall after one is no back-off). Pure.
    /// </summary>
    public static bool FaceWalkTaken(int provenLegs, int plannedLegs, float covered, float planned, bool kite) =>
        kite ? provenLegs > 0 && planned > 0 && covered >= planned * FaceMinimumShare : provenLegs >= 2 && plannedLegs > 0;

    /// <summary>A healer's face-back arc bulges at most this share of its run to the side.</summary>
    public const float FaceArcBulge = 0.12f;
    /// <summary>A face-back is at most this many legs (the longest kite, 330 units, takes 18 of 20).</summary>
    public const int FaceMaxLegs = 20;
    /// <summary>A face-back proven for less than this share of the kite's run is not taken: the bot kites as today.</summary>
    public const float FaceMinimumShare = 0.8f;

    public static long StepBackDelay(double roll) =>
        StepBackMinimumMs + (long)(Math.Clamp(roll, 0, 0.999999) * (StepBackMaximumMs - StepBackMinimumMs));

    /// <summary>The spot <paramref name="distance"/> past <paramref name="spot"/> on the line from <paramref name="from"/>
    /// (where the bot set off). <paramref name="spot"/> itself when the two are the same point. Pure.</summary>
    public static Vector2 OvershootSpot(Vector2 from, Vector2 spot, float distance)
    {
        Vector2 way = spot - from;
        return way.LengthSquared() < 1 ? spot : spot + Vector2.Normalize(way) * distance;
    }

    /// <summary>The ends of legs of at most <see cref="LegLength"/> from <paramref name="from"/> to <paramref name="to"/>
    /// (at most <paramref name="maximumLegs"/>; a longer way is cut short). Empty for no way. Pure.</summary>
    public static Vector2[] Legs(Vector2 from, Vector2 to, int maximumLegs = 3)
    {
        float length = Vector2.Distance(from, to);
        if (length < 1 || maximumLegs <= 0)
            return [];
        int count = Math.Clamp((int)MathF.Ceiling(length / LegLength), 1, maximumLegs);
        float step = MathF.Min(LegLength, length / count);
        Vector2 direction = (to - from) / length;
        var points = new Vector2[count];
        for (int i = 0; i < count; i++)
            points[i] = from + direction * step * (i + 1);
        return points;
    }

    // ---- sitting while waiting --------------------------------------------------------------------------------------

    /// <summary>A wait of 30 s plus 0-20 s, then 60% of waits sit; the same for all tiers.</summary>
    public const long SitAfterMs = 30_000;
    public const long SitSpreadMs = 20_000;
    public const double SitChance = 0.60;
    /// <summary>A wait that is not refreshed for this long (the bot moved on) has ended.</summary>
    public const long WaitGapMs = 6_000;
    /// <summary>A bot that moved this far from where it began waiting has begun another wait.</summary>
    public const float WaitDrift = 48f;
    private const ulong SitSalt = 0x53495420; // "SIT "

    public static long SitDelay(long botId, long wait) =>
        SitAfterMs + (long)(Roll(botId, wait, SitSalt ^ 0xD1) * SitSpreadMs);

    /// <summary>Whether this wait ends seated (one roll a wait). Pure.</summary>
    public static bool SitsThisWait(long botId, long wait) => Roll(botId, wait, SitSalt) < SitChance;

    /// <summary>A sit is due once the wait has lasted its delay. Pure.</summary>
    public static bool SitDue(long waitedMs, long delayMs) => waitedMs >= delayMs;

    /// <summary>
    /// Whether a waiting bot may sit now: alive, standing still, not casting, stealthed, in a fight, attacking,
    /// holding aggro, crowd-controlled, resting (resting sits on its own) or riding. Pure.
    /// </summary>
    public static bool MaySit(bool alive, bool moving, bool casting, bool stealthed, bool inCombat, bool attacking,
        bool holdsAggro, bool held, bool resting, bool stableRide) =>
        alive && !moving && !casting && !stealthed && !inCombat && !attacking && !holdsAggro && !held && !resting && !stableRide;

    // ---- crowding the bank ------------------------------------------------------------------------------------------

    /// <summary>With this many bots already standing at a vault keeper, a crowd choice leans to the bank.</summary>
    public const int BankCrowdBots = 4;
    /// <summary>Bots within this of a vault keeper stand at the bank.</summary>
    public const float BankCrowdRadius = 220f;

    /// <summary>The hangout a bot wanders to with <paramref name="botsAtBank"/> bots at the bank: a crowd choice becomes the
    /// bank when the bank already has a crowd of <see cref="BankCrowdBots"/> or more; every other choice is kept. Pure.</summary>
    public static AutonomousTownHangouts.Kind LeanToBank(AutonomousTownHangouts.Kind chosen, int botsAtBank, bool bankHere) =>
        chosen == AutonomousTownHangouts.Kind.Crowd && bankHere && botsAtBank >= BankCrowdBots
            ? AutonomousTownHangouts.Kind.Bank : chosen;

    // ---- the budget -------------------------------------------------------------------------------------------------

    /// <summary>At most this many new mesh leg checks a second server-wide for the habits.</summary>
    public const int LegChecksPerSecond = 40;
    /// <summary>A game-loop tick this long (AiProfile.LastTickMs) means the loop is busy: no new habit leg.</summary>
    public const double BusyTickMilliseconds = 50;

    public static bool LoopBusy(double lastTickMilliseconds) => lastTickMilliseconds >= BusyTickMilliseconds;

    /// <summary>
    /// The per-second leg budget as plain values: <paramref name="second"/> is the second of the last take and
    /// <paramref name="used"/> the checks taken in it. True (and the state moved on) when <paramref name="checks"/> more
    /// fit in the second of <paramref name="nowMs"/>. Pure; the runtime holds the state under a lock.
    /// </summary>
    public static bool TakeBudget(ref long second, ref int used, long nowMs, int checks, int perSecond = LegChecksPerSecond)
    {
        long now = nowMs / 1000;
        if (now != second)
        {
            second = now;
            used = 0;
        }
        if (checks <= 0 || used + checks > perSecond)
            return false;
        used += checks;
        return true;
    }

    // ---- the log ----------------------------------------------------------------------------------------------------

    /// <summary>Each per-event tag (BOT_MOVE_STRAFE, _STICK_LOST, _OVERSHOOT, _SIT, _WALL) at most this often server-wide.</summary>
    public const long EventLogGapMs = 10_000;

    public static bool ShouldLog(long now, long lastAt) => now - lastAt >= EventLogGapMs;

    /// <summary>The BOT_MOVE_HABITS line for a window (counts are named; zero counts are written). Pure.</summary>
    public static string SummaryLine(long seconds, IReadOnlyList<(string Name, long Count)> counts)
    {
        var parts = new List<string>(counts.Count + 1) { $"BOT_MOVE_HABITS seconds={seconds}" };
        foreach (var (name, count) in counts)
            parts.Add($"{name}={count}");
        return string.Join(" ", parts);
    }
}
