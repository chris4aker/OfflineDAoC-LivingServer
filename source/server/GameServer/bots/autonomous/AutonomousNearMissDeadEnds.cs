using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// #570 step 2b: walk targets that Detour's direct search reaches only "nearly" though they lie on other ground (a
    /// cliff edge above the target: Black Mtns. South's crossing into Camelot Hills at 548800,495328 lies on the low
    /// hillside, and from the Camelot side the plateau ends 51 units above it at 548762,495343). Detour returns the edge as
    /// a partial path, and the mover keeps a partial path that ends within <see cref="NearMissDistance"/> of its target
    /// without asking the zone road graph, so bots stood there until their recovery gave up. A gamebot's stall there teaches
    /// the spot: from then on an autonomous gamebot's walk to that target asks the zone roads even after a near miss. No
    /// navmesh changes; every other target keeps today's shortcut. Tests only until a PC log.
    /// </summary>
    public static class AutonomousNearMissDeadEnds
    {
        /// <summary>The mover's near-miss shortcut (<see cref="Pathfinder.TryRoadPath"/>): a partial end this close is kept.</summary>
        public const float NearMissDistance = 256f;
        /// <summary>Two targets this close count as one (the same listed crossing, snapped).</summary>
        public const float SameTargetDistance = 64f;
        public const long MemoryMilliseconds = 6 * 60 * 60_000L;
        public const int Capacity = 256;

        public readonly record struct Spot(Vector3 Target, long Until);

        // per zone, replaced (never changed) on a write, so the mover reads it without a lock
        private static readonly ConcurrentDictionary<ushort, Spot[]> Spots = new();
        private static readonly object Sync = new();
        private static int _count;

        /// <summary>Pure rule: a stall whose path stopped short of a target in the bot's own zone, within the near-miss distance.</summary>
        public static bool IsNearMissStall(PathfindingStatus status, Vector3 current, Vector3 target, bool sameZone) =>
            status == PathfindingStatus.PartialPathFound && sameZone &&
            Vector3.DistanceSquared(current, target) <= NearMissDistance * NearMissDistance;

        /// <summary>
        /// Pure rule for the mover: a partial path ending this near its target is kept as it is (no road search), unless
        /// the walker reads the memory (autonomous gamebots) and the target is a known dead end.
        /// </summary>
        public static bool KeepsNearMiss(bool nearMiss, bool readsMemory, ushort zone, Vector3 target, long now) =>
            nearMiss && !(readsMemory && IsKnown(zone, target, now));

        /// <summary>Remembers (or renews) a near-miss dead end; true when it is new.</summary>
        public static bool Remember(ushort zone, Vector3 target, long now)
        {
            lock (Sync)
            {
                Spot[] old = Spots.TryGetValue(zone, out Spot[] spots) ? spots : [];
                var next = new List<Spot>(old.Length + 1);
                bool known = false;
                foreach (Spot spot in old)
                {
                    if (spot.Until <= now)
                        continue;
                    if (!known && Vector3.DistanceSquared(spot.Target, target) <= SameTargetDistance * SameTargetDistance)
                    {
                        known = true;
                        next.Add(spot with { Until = now + MemoryMilliseconds });
                    }
                    else
                        next.Add(spot);
                }
                _count += next.Count - old.Length;
                Spots[zone] = next.ToArray();
                if (known)
                    return false;
                if (_count >= Capacity)
                    PruneExpired(now);
                if (_count >= Capacity)
                    return false;
                next.Add(new Spot(target, now + MemoryMilliseconds));
                _count++;
                Spots[zone] = next.ToArray();
                return true;
            }
        }

        // under Sync
        private static void PruneExpired(long now)
        {
            foreach (var pair in Spots)
            {
                Spot[] kept = Array.FindAll(pair.Value, spot => spot.Until > now);
                if (kept.Length == pair.Value.Length)
                    continue;
                _count -= pair.Value.Length - kept.Length;
                Spots[pair.Key] = kept;
            }
        }

        /// <summary>True when this target is a remembered near-miss dead end of the zone.</summary>
        public static bool IsKnown(ushort zone, Vector3 target, long now)
        {
            if (!Spots.TryGetValue(zone, out Spot[] spots))
                return false;
            foreach (Spot spot in spots)
                if (spot.Until > now && Vector3.DistanceSquared(spot.Target, target) <= SameTargetDistance * SameTargetDistance)
                    return true;
            return false;
        }

        /// <summary>Tests only.</summary>
        public static void Clear()
        {
            lock (Sync)
            {
                Spots.Clear();
                _count = 0;
            }
        }
    }
}
