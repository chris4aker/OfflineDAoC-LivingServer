using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.GS.Styles;

namespace DOL.GS
{
    /// <summary>
    /// Living Server 1.2 (planned as 1.2.1, "movement that doesn't look scripted", the first slice of 2.0's "Bots as people"
    /// item 5): the pure rules of how an autonomous gamebot moves in a fight. Our own work; the fork survey of 2026-11-08 found
    /// no fork with bot strafing. Each rule is a 2003 player's habit, not noise:
    /// <list type="bullet">
    /// <item>A melee bot hitting a monster that fights someone else circles a little now and then: a short arc around the
    /// monster at the distance it already stands, to stay on its side or back, to get out of a party member it stands in,
    /// or out of the monster's face. The flank step (BotBrain.Flank) picks the spot; circling keeps it there.</item>
    /// <item>A bot the monster is hitting stays put more: a solo bot takes a small step now and then; a tank holding aggro
    /// stays put and faces the monster.</item>
    /// <item>A caster takes a short side step now and then right after a cast ends, never during one.</item>
    /// </list>
    /// Every step is split into legs of at most <see cref="LegLength"/>: the server turns an NPC toward where it walks only
    /// for a walk longer than 25 units (NpcMovementComponent.WalkToInternal), so on these short legs the bot keeps facing its
    /// target instead of turning sideways, walking and snapping back. Every leg is walked on the navigation mesh first, on dry
    /// ground and the bot's own floor (<see cref="LegReached"/>); a leg the mesh does not allow is not taken.
    /// </summary>
    public static class BotCombatMovement
    {
        // ---- Circling (melee) ---------------------------------------------------------------------------------------

        /// <summary>How often a melee bot that is not the monster's target circles: every 7-15 seconds.</summary>
        public const long CircleIntervalMinimumMs = 7_000;
        public const long CircleIntervalMaximumMs = 15_000;
        /// <summary>A bot the monster is hitting (not a tank) waits this many times longer: every 21-45 seconds.</summary>
        public const int HeldIntervalFactor = 3;
        /// <summary>The first step waits until the bot has stood in melee this long: a player swings first.</summary>
        public const long SettleMs = 2_500;
        /// <summary>A bot standing in someone (within <see cref="CrowdRadius"/>) steps aside sooner, at most this often.</summary>
        public const long CrowdedRetryMs = 2_500;
        /// <summary>Another fighter this close (2D) stands in the bot.</summary>
        public const float CrowdRadius = 28f;
        /// <summary>An arc is 28-60 units long, a held bot's at most 30; at most <see cref="MaxSweepDegrees"/> around.</summary>
        public const float ArcMinimum = 28f;
        public const float ArcMaximum = 60f;
        public const float HeldArcMaximum = 30f;
        public const float MaxSweepDegrees = 45f;
        /// <summary>A circle only from where the bot settled: at most this much farther than its stick distance.</summary>
        public const int SettledSlack = 10;

        // ---- Legs ---------------------------------------------------------------------------------------------------

        /// <summary>One leg: shorter than the 25 units past which the server turns the bot to walk.</summary>
        public const float LegLength = 22f;
        public const int MaxLegs = 3;
        /// <summary>A mesh walk that ends this close (2D) to the leg's end reaches it.</summary>
        public const float LegTolerance = 6f;
        /// <summary>A leg end this far above or below the bot is a step, a ledge or another floor: not taken.</summary>
        public const float LegMaxRise = 32f;
        /// <summary>A step that has not finished by then is given up (blocked, interrupted).</summary>
        public const long StepTimeoutMs = 2_500;

        // ---- Caster steps -------------------------------------------------------------------------------------------

        /// <summary>How often a caster steps: every 12-24 seconds, within <see cref="CasterStepWindowMs"/> of a cast's end.</summary>
        public const long CasterStepIntervalMinimumMs = 12_000;
        public const long CasterStepIntervalMaximumMs = 24_000;
        public const long CasterStepWindowMs = 1_500;
        /// <summary>A caster's step: one leg to the side, a little back.</summary>
        public const float CasterStepLength = 20f;
        public const float CasterStepBack = 6f;

        public enum CircleKind { None, Small, Normal }

        /// <summary>
        /// Who may move in a fight at all. Only an autonomous gamebot of its own (not a companion or a player's temporary
        /// helper), not in a player-led group (kept as before), not in a raid party, raid dungeon or epic encounter (their
        /// tactics, stations and blast dodges place the raiders), and only against a monster (not a player or a bot).
        /// </summary>
        public static bool Allowed(bool gamebot, bool playerLedGroup, bool raidOrEpic, bool monsterTarget) =>
            gamebot && !playerLedGroup && !raidOrEpic && monsterTarget;

        /// <summary>
        /// How a melee bot circles. None: not allowed, a ranged or stealthed bot, or a tank holding aggro (it stays put and
        /// faces the monster). Small: the monster is hitting this bot (a solo fight): a rare small step. Normal: the monster
        /// fights someone else.
        /// </summary>
        public static CircleKind Kind(bool allowed, bool meleeWeapon, bool rangedCaster, bool stealthed, bool holdsAggro,
            bool tank)
        {
            if (!allowed || !meleeWeapon || rangedCaster || stealthed)
                return CircleKind.None;
            if (holdsAggro)
                return tank ? CircleKind.None : CircleKind.Small;
            return CircleKind.Normal;
        }

        /// <summary>Milliseconds until the next circle; <paramref name="roll"/> in [0, 1).</summary>
        public static long NextCircleDelay(CircleKind kind, double roll)
        {
            roll = Math.Clamp(roll, 0, 0.999999);
            long delay = CircleIntervalMinimumMs + (long)(roll * (CircleIntervalMaximumMs - CircleIntervalMinimumMs));
            return kind == CircleKind.Small ? delay * HeldIntervalFactor : delay;
        }

        /// <summary>
        /// Whether a step is due: never before the bot stood <see cref="SettleMs"/> in melee; then when its timer is up,
        /// or sooner (at most every <see cref="CrowdedRetryMs"/>) when it stands in someone.
        /// </summary>
        public static bool CircleDue(long now, long settledSince, long nextCircle, bool crowded, long lastStep)
        {
            if (settledSince <= 0 || now - settledSince < SettleMs)
                return false;
            return now >= nextCircle || crowded && now - lastStep >= CrowdedRetryMs;
        }

        /// <summary>The degrees an arc goes around a monster at <paramref name="radius"/>; <paramref name="roll"/> in [0, 1).</summary>
        public static float SweepDegrees(CircleKind kind, float radius, double roll)
        {
            if (kind == CircleKind.None || radius <= 1)
                return 0;
            float maximum = kind == CircleKind.Small ? HeldArcMaximum : ArcMaximum;
            float arc = ArcMinimum + (float)Math.Clamp(roll, 0, 0.999999) * (maximum - ArcMinimum);
            return MathF.Min(MaxSweepDegrees, arc / radius * 180f / MathF.PI);
        }

        /// <summary>In front of the monster: StyleProcessor's front window (less than 45 degrees off its nose).</summary>
        public static bool InFront(float angle) => Normalize(angle) is < 45 or > 315;

        public static float Normalize(float angle) => (angle % 360 + 360) % 360;

        /// <summary>
        /// Which way to go around the monster: +1 clockwise (the angle from the monster's nose grows), -1 the other way, 0 no
        /// step. <paramref name="angle"/> is where the bot stands from the monster's nose (GameObject.GetAngle).
        /// In order: away from a fighter the bot stands in (<paramref name="crowdSide"/>: the side it is on, +1 or -1); out of
        /// the monster's face toward the nearer side; else either way by <paramref name="roll"/>. A bot in the window of its
        /// back or side style (<paramref name="keep"/>) never steps out of it, and a bot the monster is not hitting never
        /// steps into its face.
        /// </summary>
        public static int ChooseDirection(float angle, float sweep, int crowdSide, bool holdsAggro,
            Style.eOpeningPosition? keep, double roll)
        {
            if (sweep <= 0)
                return 0;
            angle = Normalize(angle);
            int first;
            if (crowdSide != 0)
                first = -Math.Sign(crowdSide);
            else if (!holdsAggro && InFront(angle))
                first = angle < 180 ? 1 : -1;
            else
                first = roll < 0.5 ? 1 : -1;
            bool keepWindow = keep.HasValue && DOL.AI.Brain.BotBrain.InFlankPosition(angle, keep.Value);
            foreach (int direction in new[] { first, -first })
            {
                float next = Normalize(angle + direction * sweep);
                if (keepWindow && !DOL.AI.Brain.BotBrain.InFlankPosition(next, keep.Value))
                    continue;
                if (!holdsAggro && InFront(next) && !InFront(angle))
                    continue;
                return direction;
            }
            return 0;
        }

        /// <summary>
        /// The side (+1 clockwise, -1 the other way) on which the nearest fighter within <see cref="CrowdRadius"/> of the
        /// bot stands, seen from the monster; 0 when nobody stands in it. Someone exactly on the bot's bearing counts as
        /// clockwise. Positions are 2D, headings are game headings (4096 a turn) from the monster.
        /// </summary>
        public static int CrowdSide(Vector2 monster, Vector2 self, IEnumerable<Vector2> others)
        {
            float best = CrowdRadius * CrowdRadius;
            Vector2? nearest = null;
            foreach (Vector2 other in others)
            {
                float d = Vector2.DistanceSquared(self, other);
                if (d <= best)
                {
                    best = d;
                    nearest = other;
                }
            }
            if (nearest is not Vector2 n)
                return 0;
            int difference = BotTurning.Difference(Heading(monster, self), Heading(monster, n));
            return difference >= 0 ? 1 : -1;
        }

        /// <summary>The game heading (4096 a turn, 0 = +Y, clockwise) from <paramref name="from"/> to <paramref name="to"/>,
        /// as Point2D.GetHeading.</summary>
        public static ushort Heading(Vector2 from, Vector2 to)
        {
            double heading = Math.Atan2(-(to.X - from.X), to.Y - from.Y) * (4096 / (2 * Math.PI));
            if (heading < 0)
                heading += 4096;
            return (ushort)((int)heading % 4096);
        }

        /// <summary>The point at <paramref name="heading"/> and <paramref name="distance"/> from <paramref name="center"/>,
        /// as Point2D.GetPointFromHeading (without its rounding).</summary>
        public static Vector2 PointFromHeading(Vector2 center, double heading, float distance)
        {
            double angle = heading * (2 * Math.PI / 4096);
            return new((float)(center.X - Math.Sin(angle) * distance), (float)(center.Y + Math.Cos(angle) * distance));
        }

        /// <summary>
        /// The ends of the legs of an arc around <paramref name="center"/> at <paramref name="radius"/>, starting from the
        /// bot's bearing <paramref name="bearing"/> (the monster's heading to the bot) and going <paramref name="sweep"/>
        /// degrees in <paramref name="direction"/>. Each leg is at most <see cref="LegLength"/>, at most
        /// <see cref="MaxLegs"/> legs (a longer arc is cut short). Empty for no step.
        /// </summary>
        public static Vector2[] ArcLegs(Vector2 center, float radius, ushort bearing, int direction, float sweep)
        {
            if (direction == 0 || sweep <= 0 || radius <= 1)
                return [];
            float arc = sweep * MathF.PI / 180f * radius;
            int legs = Math.Clamp((int)MathF.Ceiling(arc / LegLength), 1, MaxLegs);
            // A chord is shorter than its arc: legs of at most LegLength of arc are at most LegLength long.
            float legSweep = MathF.Min(sweep / legs, LegLength / radius * 180f / MathF.PI);
            var points = new Vector2[legs];
            for (int i = 0; i < legs; i++)
                points[i] = PointFromHeading(center, bearing + direction * legSweep * (i + 1) * 4096 / 360.0, radius);
            return points;
        }

        /// <summary>Whether a mesh walk toward <paramref name="spot"/> that ended at <paramref name="walked"/> reached it on the
        /// bot's own floor. No walk (no mesh) never reaches it.</summary>
        public static bool LegReached(Vector3 spot, Vector3? walked, float fromZ) =>
            walked is Vector3 w &&
            Vector2.DistanceSquared(new(w.X, w.Y), new(spot.X, spot.Y)) <= LegTolerance * LegTolerance &&
            MathF.Abs(w.Z - fromZ) <= LegMaxRise;

        // ---- Caster steps -------------------------------------------------------------------------------------------

        /// <summary>A caster may step: allowed (see <see cref="Allowed"/>), a ranged caster, not casting and with no cast
        /// queued, not the monster's target and not under melee pressure (those keep the existing rules).</summary>
        public static bool CasterStepAllowed(bool allowed, bool rangedCaster, bool casting, bool castQueued, bool holdsAggro,
            bool meleePressure) =>
            allowed && rangedCaster && !casting && !castQueued && !holdsAggro && !meleePressure;

        /// <summary>A caster's step is due right after a cast ended (within <see cref="CasterStepWindowMs"/>), when its timer
        /// is up. Never before its first cast in the fight.</summary>
        public static bool CasterStepDue(long now, long castEndedAt, long nextStep) =>
            castEndedAt > 0 && now - castEndedAt <= CasterStepWindowMs && now >= nextStep;

        public static long NextCasterStepDelay(double roll) =>
            CasterStepIntervalMinimumMs +
            (long)(Math.Clamp(roll, 0, 0.999999) * (CasterStepIntervalMaximumMs - CasterStepIntervalMinimumMs));

        /// <summary>A caster's step from <paramref name="self"/>: to its left or right (by <paramref name="roll"/>) as it faces
        /// <paramref name="target"/>, and a little back from it.</summary>
        public static Vector2 CasterStepSpot(Vector2 self, Vector2 target, double roll)
        {
            Vector2 toward = target - self;
            if (toward.LengthSquared() < 1)
                return self;
            toward = Vector2.Normalize(toward);
            Vector2 side = roll < 0.5 ? new(-toward.Y, toward.X) : new(toward.Y, -toward.X);
            return self + side * CasterStepLength - toward * CasterStepBack;
        }

        // ---- The stand-off ------------------------------------------------------------------------------------------

        /// <summary>
        /// "Arrive slightly short": each gamebot stops its melee approach 0-20 units short of the stick distance, the same
        /// for the same bot every fight, instead of every bot on exactly the same ring around a monster. Never under 40.
        /// Only shorter: BotBrain.IsMeleeChasing keeps a follow that is at most the stick distance.
        /// </summary>
        public static int StandOff(int stickMinimum, long botId)
        {
            int jitter = (int)((((ulong)botId * 2654435761UL) >> 7) % 21);
            return Math.Max(Math.Min(40, stickMinimum), stickMinimum - jitter);
        }

        // ---- Counters -----------------------------------------------------------------------------------------------

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(BotCombatMovement));
        private static long _circles, _held, _crowded, _legs, _blocked, _casterSteps, _nextReportTick;
        public const long ReportIntervalMs = 5 * 60_000;

        public enum Counter { Circle, HeldStep, Crowded, Leg, Blocked, CasterStep }

        /// <summary>Counts a step; every 5 minutes with any, one BOT_COMBAT_MOVEMENT line in the server log.</summary>
        public static void Count(Counter counter)
        {
            switch (counter)
            {
                case Counter.Circle: Interlocked.Increment(ref _circles); break;
                case Counter.HeldStep: Interlocked.Increment(ref _held); break;
                case Counter.Crowded: Interlocked.Increment(ref _crowded); break;
                case Counter.Leg: Interlocked.Increment(ref _legs); break;
                case Counter.Blocked: Interlocked.Increment(ref _blocked); break;
                case Counter.CasterStep: Interlocked.Increment(ref _casterSteps); break;
            }
            long now = GameLoop.GameLoopTime;
            long next = Volatile.Read(ref _nextReportTick);
            if (now < next || Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMs, next) != next)
                return;
            if (next == 0)
                return; // the first call only starts the clock
            long circles = Interlocked.Exchange(ref _circles, 0), held = Interlocked.Exchange(ref _held, 0),
                crowded = Interlocked.Exchange(ref _crowded, 0), legs = Interlocked.Exchange(ref _legs, 0),
                blocked = Interlocked.Exchange(ref _blocked, 0), caster = Interlocked.Exchange(ref _casterSteps, 0);
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_COMBAT_MOVEMENT minutes={ReportIntervalMs / 60_000} circles={circles} heldSteps={held} " +
                         $"crowded={crowded} legs={legs} blocked={blocked} casterSteps={caster}");
        }
    }

    /// <summary>
    /// Living Server 1.2: turns that don't snap. The game sends an NPC's heading as it is; the original client shows a new
    /// heading at once, so a bot that turns 150 degrees in one update spins on the spot. A cosmetic turn of an autonomous
    /// gamebot standing still (looking around, greeting, facing a banker) is sent in steps of at most 45 degrees, about a
    /// tenth of a second apart. A follower or a melee bot no longer re-faces its target on every follow check: only once it
    /// is more than 30 degrees off (as BotBrain.FaceMeleeTarget). Turns that matter in a fight (attack start, casting, the
    /// melee face) stay immediate.
    /// </summary>
    public static class BotTurning
    {
        public const int FullTurn = 4096;
        /// <summary>A bot this far off (30 degrees) re-faces; closer, it keeps its heading.</summary>
        public const int FaceTolerance = FullTurn / 12;
        /// <summary>A standing turn wider than this (60 degrees) is sent in steps.</summary>
        public const int GradualAbove = FullTurn / 6;
        /// <summary>At most 45 degrees a step.</summary>
        public const int MaxStep = FullTurn / 8;
        public const int StepMilliseconds = 110;

        /// <summary>The signed shortest turn from <paramref name="from"/> to <paramref name="to"/>, in (-2048, 2048].</summary>
        public static int Difference(ushort from, ushort to)
        {
            int d = ((to - from) % FullTurn + FullTurn) % FullTurn;
            return d > FullTurn / 2 ? d - FullTurn : d;
        }

        public static bool ShouldFace(ushort heading, ushort wanted, int tolerance = FaceTolerance) =>
            Math.Abs(Difference(heading, wanted)) > tolerance;

        /// <summary>The headings a standing turn is sent as: just <paramref name="to"/> up to <see cref="GradualAbove"/>,
        /// else even steps of at most <see cref="MaxStep"/> ending on <paramref name="to"/>.</summary>
        public static ushort[] Steps(ushort from, ushort to)
        {
            int difference = Difference(from, to);
            if (Math.Abs(difference) <= GradualAbove)
                return [(ushort)(to % FullTurn)];
            int count = (Math.Abs(difference) + MaxStep - 1) / MaxStep;
            var steps = new ushort[count];
            for (int i = 1; i <= count; i++)
                steps[i - 1] = i == count ? (ushort)(to % FullTurn)
                    : (ushort)(((from + difference * i / count) % FullTurn + FullTurn) % FullTurn);
            return steps;
        }

        /// <summary>
        /// Whether an arrived follower keeps its heading instead of re-facing what it follows (NpcMovementComponent.FollowTick):
        /// an autonomous gamebot of its own, not in a player-led group (kept as before), less than 30 degrees off.
        /// </summary>
        public static bool HoldsFollowHeading(GameNPC owner, GameObject target) =>
            owner is GameBot { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } bot && !bot.IsPlayerLedGroup &&
            target != null && target.CurrentRegion == owner.CurrentRegion &&
            !ShouldFace(owner.Heading, owner.GetHeading(target));

        private sealed class TurnState
        {
            public int Generation;
        }

        private static readonly ConditionalWeakTable<GameNPC, TurnState> Turns = new();

        /// <summary>Turns a standing bot to <paramref name="heading"/> in steps (<see cref="Steps"/>). A later turn, a walk
        /// or an attack ends it: if anything else turned the bot meanwhile, the rest is dropped.</summary>
        public static void TurnGradually(GameNPC body, ushort heading)
        {
            if (body == null)
                return;
            ushort[] steps = Steps(body.Heading, heading);
            TurnState state = Turns.GetValue(body, _ => new TurnState());
            int generation = Interlocked.Increment(ref state.Generation);
            body.TurnTo(steps[0]);
            if (steps.Length == 1)
                return;
            int index = 1;
            _ = new ECSGameTimer(body, _ =>
            {
                if (Volatile.Read(ref state.Generation) != generation || body.ObjectState != GameObject.eObjectState.Active ||
                    !body.IsAlive || body.IsMoving || body.IsAttacking || body.Heading != steps[index - 1])
                    return 0;
                body.TurnTo(steps[index++]);
                return index < steps.Length ? StepMilliseconds : 0;
            }, StepMilliseconds);
        }

        /// <summary>Turns a standing bot to face <paramref name="target"/> in steps.</summary>
        public static void TurnGradually(GameNPC body, GameObject target)
        {
            if (body == null || target == null || target.CurrentRegion != body.CurrentRegion)
                return;
            TurnGradually(body, body.GetHeading(target));
        }
    }
}
