using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// Living Server 1.2 (planned as 1.2.1): town idling at the bank and the usual crowd spots. A gamebot on a town break
    /// (AutonomousTownIdleRouting) no longer stands on one random spot for the whole 15-30 minutes. After it walks in, and
    /// then every 2-6 minutes, it wanders over to where people stood around on a 2003 server: the vault keeper (the bank),
    /// a merchant, a trainer, or a crowd that is already standing somewhere (other bots or players), and faces what it came
    /// to. Our own, built on our town idle code; True165's town idle matches ours but for 7 lines, so nothing was ported.
    /// These are the pure rules; the controller walks only to a spot the navigation mesh has a complete path to, on dry
    /// ground, on the floor of what it came to, inside the town, and outside every live monster's aggro range.
    /// </summary>
    public static class AutonomousTownHangouts
    {
        public enum Kind { Bank, Merchant, Trainer, Crowd }

        /// <summary>The first wander comes 20-90 seconds after the bot walked in; then one every 2-6 minutes.</summary>
        public const long FirstHopMinimumMs = 20_000;
        public const long FirstHopMaximumMs = 90_000;
        public const long DwellMinimumMs = 120_000;
        public const long DwellMaximumMs = 360_000;
        /// <summary>No wander with less than two minutes of the break left; a failed search tries again in a minute.</summary>
        public const long MinimumRemainingMs = 120_000;
        public const long RetryMs = 60_000;

        /// <summary>How far around the bot it looks for a hangout: a town, or a capital.</summary>
        public const ushort SearchRadius = 1_400;
        public const ushort CapitalSearchRadius = 2_000;

        /// <summary>Where it stands near an NPC: in front of it (within 70 degrees of where it faces), 70-170 away; a bank
        /// queue stands closer, at most 140.</summary>
        public const float SpotMinimum = 70f;
        public const float SpotMaximum = 170f;
        public const float BankSpotMaximum = 140f;
        public const float FrontSpreadDegrees = 70f;

        /// <summary>A crowd: at least <see cref="CrowdMinimum"/> people standing within <see cref="CrowdRadius"/> of one of
        /// them, at most <see cref="CrowdMaximum"/> (a full crowd is left alone), and not the bot's own crowd (anyone within
        /// <see cref="OwnCrowdRadius"/> of the bot is not counted).</summary>
        public const float CrowdRadius = 160f;
        public const int CrowdMinimum = 2;
        public const int CrowdMaximum = 12;
        public const float OwnCrowdRadius = 200f;
        /// <summary>It joins a crowd on its edge, 55-110 from its middle, on the side it comes from.</summary>
        public const float CrowdRingMinimum = 55f;
        public const float CrowdRingMaximum = 110f;
        /// <summary>Nobody already stands this close to its spot.</summary>
        public const float PersonalSpace = 32f;

        /// <summary>A spot this far above or below what it came to is another floor: not taken.</summary>
        public const float MaxRise = 64f;
        /// <summary>The walk over is at most this long, and at most three times the straight line (or 300 more).</summary>
        public const double MaxWalk = 2_200;
        /// <summary>It has arrived this close to its spot.</summary>
        public const float ArriveTolerance = 28f;

        /// <summary>
        /// What the bot wanders over to, from a roll in [0, 1): the bank 35%, a crowd 30%, a merchant 20%, a trainer 15%,
        /// among the ones this town has. Null when it has none.
        /// </summary>
        public static Kind? Choose(bool bank, bool merchant, bool trainer, bool crowd, double roll)
        {
            (Kind Kind, double Weight, bool Present)[] options =
            [
                (Kind.Bank, 0.35, bank), (Kind.Crowd, 0.30, crowd), (Kind.Merchant, 0.20, merchant), (Kind.Trainer, 0.15, trainer),
            ];
            double total = 0;
            foreach (var option in options)
                if (option.Present)
                    total += option.Weight;
            if (total <= 0)
                return null;
            double pick = Math.Clamp(roll, 0, 0.999999) * total;
            Kind? last = null;
            foreach (var option in options)
            {
                if (!option.Present)
                    continue;
                last = option.Kind;
                if (pick < option.Weight)
                    return option.Kind;
                pick -= option.Weight;
            }
            return last;
        }

        public static long FirstHopDelay(double roll) =>
            FirstHopMinimumMs + (long)(Math.Clamp(roll, 0, 0.999999) * (FirstHopMaximumMs - FirstHopMinimumMs));

        public static long NextDwell(double roll) =>
            DwellMinimumMs + (long)(Math.Clamp(roll, 0, 0.999999) * (DwellMaximumMs - DwellMinimumMs));

        /// <summary>A wander is due once its time came, with at least <see cref="MinimumRemainingMs"/> of the break left.</summary>
        public static bool HopDue(DateTime now, DateTime? hopAt, DateTime breakEnds) =>
            hopAt.HasValue && now >= hopAt.Value && (breakEnds - now).TotalMilliseconds >= MinimumRemainingMs;

        /// <summary>A spot in front of an NPC standing at <paramref name="npc"/> facing <paramref name="npcHeading"/> (game
        /// heading); rolls in [0, 1).</summary>
        public static Vector2 SpotNear(Vector2 npc, ushort npcHeading, Kind kind, double r1, double r2)
        {
            double spread = (Math.Clamp(r1, 0, 0.999999) * 2 - 1) * FrontSpreadDegrees;
            float maximum = kind == Kind.Bank ? BankSpotMaximum : SpotMaximum;
            float distance = SpotMinimum + (float)Math.Clamp(r2, 0, 0.999999) * (maximum - SpotMinimum);
            return BotCombatMovement.PointFromHeading(npc, npcHeading + spread * 4096 / 360, distance);
        }

        /// <summary>
        /// The middle of the biggest crowd among <paramref name="standers"/> (people standing still near the bot), not
        /// counting anyone within <see cref="OwnCrowdRadius"/> of the bot itself; ties go to the nearer one. False when no
        /// crowd of <see cref="CrowdMinimum"/> to <see cref="CrowdMaximum"/> people stands anywhere.
        /// </summary>
        public static bool TryFindCrowd(IReadOnlyList<Vector2> standers, Vector2 self, out Vector2 center, out int count)
        {
            center = default;
            count = 0;
            var others = new List<Vector2>(standers.Count);
            foreach (Vector2 stander in standers)
                if (Vector2.DistanceSquared(stander, self) > OwnCrowdRadius * OwnCrowdRadius)
                    others.Add(stander);
            float bestDistance = float.MaxValue;
            foreach (Vector2 seed in others)
            {
                Vector2 sum = Vector2.Zero;
                int members = 0;
                foreach (Vector2 other in others)
                    if (Vector2.DistanceSquared(seed, other) <= CrowdRadius * CrowdRadius)
                    {
                        sum += other;
                        members++;
                    }
                if (members < CrowdMinimum || members > CrowdMaximum)
                    continue;
                Vector2 middle = sum / members;
                float distance = Vector2.DistanceSquared(middle, self);
                if (members > count || members == count && distance < bestDistance)
                {
                    count = members;
                    center = middle;
                    bestDistance = distance;
                }
            }
            return count > 0;
        }

        /// <summary>A spot on the edge of a crowd at <paramref name="center"/>, on the side the bot comes from (within 60
        /// degrees); rolls in [0, 1).</summary>
        public static Vector2 SpotInCrowd(Vector2 center, Vector2 self, double r1, double r2)
        {
            ushort toward = BotCombatMovement.Heading(center, self);
            double spread = (Math.Clamp(r1, 0, 0.999999) * 2 - 1) * 60;
            float distance = CrowdRingMinimum + (float)Math.Clamp(r2, 0, 0.999999) * (CrowdRingMaximum - CrowdRingMinimum);
            return BotCombatMovement.PointFromHeading(center, toward + spread * 4096 / 360, distance);
        }

        /// <summary>Nobody stands within <see cref="PersonalSpace"/> of <paramref name="spot"/>.</summary>
        public static bool HasRoom(Vector2 spot, IEnumerable<Vector2> standers)
        {
            foreach (Vector2 stander in standers)
                if (Vector2.DistanceSquared(spot, stander) < PersonalSpace * PersonalSpace)
                    return false;
            return true;
        }

        /// <summary>A walk of <paramref name="pathLength"/> to a spot <paramref name="straight"/> away is a stroll across town,
        /// not a trek around it.</summary>
        public static bool WalkAcceptable(double pathLength, double straight) =>
            pathLength > 0 && pathLength <= MaxWalk && pathLength <= Math.Max(straight * 3, straight + 300);

        public static string Word(Kind kind) => kind switch
        {
            Kind.Bank => "bank",
            Kind.Merchant => "merchants",
            Kind.Trainer => "trainers",
            _ => "crowd",
        };

        // ---- Counters -----------------------------------------------------------------------------------------------

        private static readonly DOL.Logging.Logger Log = DOL.Logging.LoggerManager.Create(typeof(AutonomousTownHangouts));
        private static long _bank, _merchant, _trainer, _crowd, _none, _arrived, _nextReportTick;
        public const long ReportIntervalMs = 5 * 60_000;

        /// <summary>Counts a wander (null: none found); every 5 minutes with any, one AUTONOMOUS_TOWN_HANGOUT log line.</summary>
        public static void Count(Kind? kind, bool arrived = false)
        {
            if (arrived)
                Interlocked.Increment(ref _arrived);
            else
                switch (kind)
                {
                    case Kind.Bank: Interlocked.Increment(ref _bank); break;
                    case Kind.Merchant: Interlocked.Increment(ref _merchant); break;
                    case Kind.Trainer: Interlocked.Increment(ref _trainer); break;
                    case Kind.Crowd: Interlocked.Increment(ref _crowd); break;
                    default: Interlocked.Increment(ref _none); break;
                }
            long now = GameLoop.GameLoopTime;
            long next = Volatile.Read(ref _nextReportTick);
            if (now < next || Interlocked.CompareExchange(ref _nextReportTick, now + ReportIntervalMs, next) != next)
                return;
            if (next == 0)
                return; // the first call only starts the clock
            long bank = Interlocked.Exchange(ref _bank, 0), merchant = Interlocked.Exchange(ref _merchant, 0),
                trainer = Interlocked.Exchange(ref _trainer, 0), crowd = Interlocked.Exchange(ref _crowd, 0),
                none = Interlocked.Exchange(ref _none, 0), done = Interlocked.Exchange(ref _arrived, 0);
            if (Log.IsInfoEnabled)
                Log.Info($"AUTONOMOUS_TOWN_HANGOUT minutes={ReportIntervalMs / 60_000} bank={bank} merchants={merchant} " +
                         $"trainers={trainer} crowd={crowd} noSpot={none} arrived={done}");
        }
    }
}
