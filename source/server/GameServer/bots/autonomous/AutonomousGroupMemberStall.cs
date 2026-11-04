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

        /// <summary>True when this trailing member should no longer hold the group.</summary>
        public static bool Release(GameBot member, GameBot leader, float distance, int cohesionRadius, long now)
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
                float moved = System.MathF.Sqrt((member.X - trail.AnchorX) * (member.X - trail.AnchorX) +
                    (member.Y - trail.AnchorY) * (member.Y - trail.AnchorY));
                if (distance <= cohesionRadius || trail.Since == 0 || moved >= ProgressUnits)
                {
                    trail.AnchorX = member.X;
                    trail.AnchorY = member.Y;
                    trail.Since = now;
                    if (distance <= cohesionRadius)
                        trail.Logged = false;
                    return false;
                }
                if (!IsStalled(moved, trail.Since, now))
                    return false;
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
