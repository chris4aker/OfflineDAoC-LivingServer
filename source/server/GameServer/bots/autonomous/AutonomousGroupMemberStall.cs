using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// A group leader waits for members who trail too far behind. A member that makes no
    /// progress toward the leader for 45 seconds no longer holds the whole group: the leader
    /// moves on (the member keeps following and gets a fresh formation point as the leader
    /// moves). Seen in Iarnwood on 2026-10-05: six groups waited forever for one member each,
    /// all standing at the same spot. The first time a member is released this way its full
    /// movement state is logged (AUTONOMOUS_GROUP_MEMBER_STALLED) so the cause can be found.
    /// Owner 2026-10-29: two members stood 24 and 9 minutes in a pocket of the navigation mesh (Skona Ravine, Silvermine
    /// Mts.) 1,300 units from their leader, every path out failing, and nothing brought them back. The log now also names
    /// their zone, the leader's, and where the member was in the last minutes (how it got there), and a member still
    /// stalled after <see cref="RecoverMilliseconds"/> is moved to its leader (AUTONOMOUS_GROUP_MEMBER_RECOVERED).
    /// </summary>
    public static class AutonomousGroupMemberStall
    {
        public const long StallMilliseconds = 45_000;
        public const float ProgressUnits = 100f;
        public const long RecoverMilliseconds = 180_000;
        public const long RecoverCooldownMilliseconds = 300_000;
        private const long SampleEveryMilliseconds = 30_000;
        private const int SampleCount = 6;

        /// <summary>
        /// A PvE party's member that has not come <see cref="CloserUnits"/> nearer its leader for this long (in the leader's
        /// region or out of it) no longer holds the party either, even while it keeps walking: the leader goes on to the camp
        /// and the member keeps travelling there (<see cref="HoldClock"/>). The same 10 minutes the party gives a hosted
        /// player who is not getting closer (BotLedGroups.GoOnWithoutMs). Owner's PC 2026-11-15 (build 1dc66b6): party
        /// albion-f86e88-1370 stood in Caer Diogel from 00:55 to past 01:19 on its way to griffins in Gwyddneau; two of its
        /// members were in Camelot at 01:05 (AUTONOMOUS_CAPITAL_EGRESS_RECOVERY), and a member in another region never let
        /// the leader go: the 45-second rule above only looks at members in the leader's region that stand still.
        /// </summary>
        public const long LongHoldMilliseconds = 10 * 60_000L;
        public const float CloserUnits = 200f;
        /// <summary>A clock not stepped for this long (the member was close, or nobody waited for it) starts again.</summary>
        public const long HoldClockStaleMilliseconds = 30_000L;

        /// <summary>
        /// How long a trailing member has held its leader without getting closer. Pure: fed one observation at a time.
        /// <c>sameRegion</c> false (the member is in another region) is never "closer"; coming back into the leader's region is.
        /// </summary>
        public sealed class HoldClock
        {
            /// <summary>The member is out of formation and holds the leader.</summary>
            public bool Holding { get; private set; }
            /// <summary>When the member last got <see cref="CloserUnits"/> nearer (or started holding).</summary>
            public long Since { get; private set; }
            public float Closest { get; private set; } = float.MaxValue;
            public long LastStep { get; private set; }

            /// <summary>True when the member has held the leader <see cref="LongHoldMilliseconds"/> without getting closer.</summary>
            public bool Step(bool sameRegion, float distance, int cohesionRadius, long now)
            {
                bool stale = LastStep > 0 && now - LastStep > HoldClockStaleMilliseconds;
                LastStep = now;
                if (sameRegion && distance <= cohesionRadius)
                {
                    Holding = false;
                    Since = 0;
                    Closest = float.MaxValue;
                    return false;
                }
                if (!Holding || stale)
                {
                    Holding = true;
                    Since = now;
                    Closest = sameRegion ? distance : float.MaxValue;
                    return false;
                }
                if (sameRegion && (Closest == float.MaxValue || distance <= Closest - CloserUnits))
                {
                    Closest = distance;
                    Since = now;
                    return false;
                }
                return now - Since >= LongHoldMilliseconds;
            }
        }

        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousGroupMemberStall));

        private sealed class Trail
        {
            public float AnchorX, AnchorY;
            public long Since;
            public bool Logged;
            public bool RecoverRequested;
            public long LastRecovered;
            public long NextSample;
            public readonly (long Tick, int X, int Y, int Z, ushort Zone)[] Samples = new (long, int, int, int, ushort)[SampleCount];
            public int SampleTotal;
            public readonly HoldClock Hold = new();
            public bool LongHoldLogged;
        }

        private static readonly ConditionalWeakTable<GameBot, Trail> Trails = new();

        /// <summary>Pure rule: the member itself moved less than 100 units in 45 seconds.</summary>
        public static bool IsStalled(float movedSinceAnchor, long sinceTick, long now) =>
            sinceTick > 0 && movedSinceAnchor < ProgressUnits && now - sinceTick >= StallMilliseconds;

        /// <summary>A member still stalled this long is moved to its leader (the pure rule).</summary>
        public static bool ShouldRecover(long sinceTick, long now, long lastRecovered) =>
            sinceTick > 0 && now - sinceTick >= RecoverMilliseconds &&
            (lastRecovered == 0 || now - lastRecovered >= RecoverCooldownMilliseconds);

        private static string DescribeTrail(Trail trail, long now)
        {
            int count = System.Math.Min(trail.SampleTotal, SampleCount);
            var parts = new System.Collections.Generic.List<string>();
            for (int i = count; i >= 1; i--)
            {
                var sample = trail.Samples[(trail.SampleTotal - i) % SampleCount];
                parts.Add($"-{(now - sample.Tick) / 1000}s:{sample.X},{sample.Y},{sample.Z}(z{sample.Zone})");
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// A PvE party's member in another region than its leader: true once it has been away (and not come back) for
        /// <see cref="LongHoldMilliseconds"/>, so it no longer holds the party (logged once). The member is never moved.
        /// </summary>
        public static bool ReleaseFromOtherRegion(GameBot member, GameBot leader, long now)
        {
            if (member == null || leader == null || member == leader || member.CurrentRegionID == leader.CurrentRegionID)
                return false;
            Trail trail = Trails.GetOrCreateValue(member);
            lock (trail)
                return StepLongHold(trail, member, leader, false, float.MaxValue, 0, now);
        }

        private static bool StepLongHold(Trail trail, GameBot member, GameBot leader, bool sameRegion, float distance, int cohesionRadius, long now)
        {
            if (!trail.Hold.Step(sameRegion, distance, cohesionRadius, now))
            {
                if (!trail.Hold.Holding)
                    trail.LongHoldLogged = false;
                return false;
            }
            if (!trail.LongHoldLogged)
            {
                trail.LongHoldLogged = true;
                Log.Warn($"AUTONOMOUS_GROUP_MEMBER_LONG_HOLD leader={leader.Name} member={member.Name} id={member.DatabaseID} " +
                         $"class=\"{member.ClassName}\" region={member.CurrentRegionID} leaderRegion={leader.CurrentRegionID} " +
                         $"distance={(sameRegion ? distance.ToString("0") : "-1")} position={member.X},{member.Y},{member.Z} " +
                         $"zone=\"{member.CurrentZone?.Description}\"({member.CurrentZone?.ID}) " +
                         $"leaderZone=\"{leader.CurrentZone?.Description}\"({leader.CurrentZone?.ID}) leaderAt={leader.X},{leader.Y},{leader.Z} " +
                         $"heldSeconds={(now - trail.Hold.Since) / 1000} alive={member.IsAlive} returning={member.IsReturningAfterRelease} " +
                         $"riding={member.IsOnStableMasterRoute} activity=\"{member.PersistentRecord?.Activity}\" " +
                         $"progress=\"{member.PersistentRecord?.ObjectiveProgress}\"; " +
                         "not closer for 10 minutes, the leader goes on and the member keeps travelling to the camp");
            }
            return true;
        }

        /// <summary>
        /// True when this trailing member should no longer hold the group. With <paramref name="boundedHold"/> (a PvE party)
        /// a member that keeps moving but has not come closer for <see cref="LongHoldMilliseconds"/> is let go too.
        /// </summary>
        public static bool Release(GameBot member, GameBot leader, float distance, int cohesionRadius, long now, bool boundedHold = false)
        {
            if (member == null || leader == null || member == leader)
                return false;
            Trail trail = Trails.GetOrCreateValue(member);
            lock (trail)
            {
                if (now >= trail.NextSample)
                {
                    trail.NextSample = now + SampleEveryMilliseconds;
                    trail.Samples[trail.SampleTotal % SampleCount] =
                        (now, member.X, member.Y, member.Z, (ushort)(member.CurrentZone?.ID ?? 0));
                    trail.SampleTotal++;
                }
                bool heldTooLong = boundedHold && StepLongHold(trail, member, leader, true, distance, cohesionRadius, now);
                float moved = System.MathF.Sqrt((member.X - trail.AnchorX) * (member.X - trail.AnchorX) +
                    (member.Y - trail.AnchorY) * (member.Y - trail.AnchorY));
                if (distance <= cohesionRadius || trail.Since == 0 || moved >= ProgressUnits)
                {
                    trail.AnchorX = member.X;
                    trail.AnchorY = member.Y;
                    trail.Since = now;
                    if (distance <= cohesionRadius)
                        trail.Logged = false;
                    return heldTooLong;
                }
                if (!IsStalled(moved, trail.Since, now))
                    return heldTooLong;
                if (ShouldRecover(trail.Since, now, trail.LastRecovered))
                    trail.RecoverRequested = true;
                if (!trail.Logged)
                {
                    trail.Logged = true;
                    var brain = member.Brain as BotBrain;
                    Log.Warn($"AUTONOMOUS_GROUP_MEMBER_STALLED leader={leader.Name} " +
                             $"member={member.Name} id={member.DatabaseID} class=\"{member.ClassName}\" distance={distance:0} " +
                             $"region={member.CurrentRegionID} position={member.X},{member.Y},{member.Z} " +
                             $"zone=\"{member.CurrentZone?.Description}\"({member.CurrentZone?.ID}) " +
                             $"leaderZone=\"{leader.CurrentZone?.Description}\"({leader.CurrentZone?.ID}) " +
                             $"leaderAt={leader.X},{leader.Y},{leader.Z} trail=\"{DescribeTrail(trail, now)}\" " +
                             $"moving={member.IsMoving} speed={member.movementComponent?.CurrentSpeed} maxSpeed={member.MaxSpeed} " +
                             $"casting={member.IsCasting} spell=\"{member.castingComponent?.SpellHandler?.Spell?.Name}\" " +
                             $"inCombat={member.InCombat} aggro={brain?.HasAggro} resting={member.IsRecoveryResting} " +
                             $"crowdControlled={member.IsCrowdControlled} fsm={brain?.FSM?.GetCurrentState()?.StateType} " +
                             $"activity=\"{member.PersistentRecord?.Activity}\"; the leader moves on without waiting");
                }
                return true;
            }
        }

        /// <summary>
        /// Called on the stalled member's own AI turn. A gamebot of an autonomous party that stayed stalled for
        /// <see cref="RecoverMilliseconds"/>, alive and idle, is moved to its leader's side (same region) and the
        /// move is logged with where it came from. Companions and members of a player's group are never moved.
        /// </summary>
        public static bool TryRecoverToLeader(GameBot member, GameBot leader)
        {
            if (member == null || leader == null || member == leader || !member.IsAutonomousWorldBot ||
                member.IsTemporaryGroupHelper || member.IsPlayerLedGroup || !Trails.TryGetValue(member, out Trail trail))
                return false;
            long now = GameLoop.GameLoopTime;
            string trailText;
            long stalledSeconds;
            lock (trail)
            {
                if (!trail.RecoverRequested)
                    return false;
                if (!member.IsAlive || !leader.IsAlive || member.CurrentRegionID != leader.CurrentRegionID ||
                    member.IsOnStableMasterRoute || member.InCombat || member.IsCasting ||
                    member.GetDistanceTo(leader) <= 600)
                {
                    trail.RecoverRequested = false;
                    return false;
                }
                trailText = DescribeTrail(trail, now);
                stalledSeconds = (now - trail.Since) / 1000;
            }
            string from = $"{member.X},{member.Y},{member.Z} zone=\"{member.CurrentZone?.Description}\"({member.CurrentZone?.ID})";
            float distance = member.GetDistanceTo(leader);
            int slot = member.ObjectID % 5 - 2;
            member.StopMovingOnPath();
            member.StopMoving();
            if (!member.MoveTo(leader.CurrentRegionID, leader.X + slot * 60, leader.Y + 60, leader.Z, leader.Heading))
                return false;
            lock (trail)
            {
                trail.RecoverRequested = false;
                trail.LastRecovered = now;
                trail.AnchorX = member.X;
                trail.AnchorY = member.Y;
                trail.Since = now;
                trail.Logged = false;
            }
            Log.Warn($"AUTONOMOUS_GROUP_MEMBER_RECOVERED leader={leader.Name} member={member.Name} id={member.DatabaseID} " +
                     $"class=\"{member.ClassName}\" stalledSeconds={stalledSeconds} distance={distance:0} from={from} " +
                     $"to={member.X},{member.Y},{member.Z} leaderZone=\"{leader.CurrentZone?.Description}\"({leader.CurrentZone?.ID}) trail=\"{trailText}\"");
            return true;
        }
    }
}
