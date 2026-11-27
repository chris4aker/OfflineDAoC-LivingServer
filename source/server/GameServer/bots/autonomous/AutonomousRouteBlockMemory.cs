using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// #570 step 2b: route spots where several solo gamebots gave a camp up ("Route blocked by X ... with no way around",
    /// <see cref="AutonomousWorldBotController"/>'s route threat Retarget). Each bot used to learn the same blocker on its
    /// own and pay for the same trip again. Once <see cref="DistinctBots"/> different bots give up within
    /// <see cref="Radius"/> of one another in <see cref="WindowMilliseconds"/>, the spot is a block for
    /// <see cref="ActiveMilliseconds"/> (renewed by each new give-up there). Solo gamebots up to the highest level that gave
    /// up there then plan no camp at it or straight past it, while another camp is left (<see cref="KeepUnblocked{T}"/>).
    /// Kept per region; the blocks are published as snapshots read without a lock. Autonomous gamebots only; monsters are
    /// not touched. Tests only until a PC log.
    /// </summary>
    public static class AutonomousRouteBlockMemory
    {
        public const int DistinctBots = 3;
        public const int Radius = 800;
        public const int RouteRadius = 900;
        public const int DestinationRadius = 1_200;
        public const long WindowMilliseconds = 30 * 60_000;
        public const long ActiveMilliseconds = 30 * 60_000;
        public const int MaxBlocksPerRegion = 32;
        public const int MaxRecords = 4_000;

        private readonly record struct GiveUp(int X, int Y, long Bot, int Level, long Tick);

        /// <summary>A block; replaced, never changed, so readers need no lock.</summary>
        public sealed record Block(ushort Region, int X, int Y, int MaxBotLevel, long Until);

        private static readonly object Sync = new();
        private static readonly Dictionary<ushort, List<GiveUp>> Records = new();
        private static int _recordCount;
        // blocks per region, a new dictionary published on every change
        private static volatile Dictionary<ushort, Block[]> _blocks = new();
        private static long _latestUntil;

        /// <summary>True while any block may still be live; a cheap first test before the per-region reads.</summary>
        public static bool AnyActive(long now) => Volatile.Read(ref _latestUntil) > now;

        /// <summary>Records a solo bot's route give-up; returns the block it made or renewed, if any.</summary>
        public static Block RecordGiveUp(ushort region, int x, int y, long bot, int botLevel, long now, out bool created,
            out int bots)
        {
            created = false;
            bots = 0;
            lock (Sync)
            {
                if (!Records.TryGetValue(region, out List<GiveUp> records))
                    Records[region] = records = new();
                _recordCount -= records.RemoveAll(r => now - r.Tick > WindowMilliseconds);
                if (_recordCount >= MaxRecords)
                {
                    foreach (List<GiveUp> other in Records.Values)
                        _recordCount -= other.RemoveAll(r => now - r.Tick > WindowMilliseconds);
                    if (_recordCount >= MaxRecords && records.Count > 0)
                    {
                        records.RemoveAt(0);
                        _recordCount--;
                    }
                }
                records.Add(new GiveUp(x, y, bot, botLevel, now));
                _recordCount++;
                HashSet<long> distinct = new();
                int maxLevel = 0;
                foreach (GiveUp r in records)
                    if (Near(r.X, r.Y, x, y, Radius))
                    {
                        distinct.Add(r.Bot);
                        maxLevel = Math.Max(maxLevel, r.Level);
                    }
                bots = distinct.Count;
                if (distinct.Count < DistinctBots)
                    return null;

                Block[] current = _blocks.TryGetValue(region, out Block[] existing) ? existing : Array.Empty<Block>();
                List<Block> next = new(current.Length + 1);
                Block old = null;
                foreach (Block b in current)
                {
                    if (b.Until <= now)
                        continue;
                    if (old == null && Near(b.X, b.Y, x, y, Radius))
                        old = b;
                    else
                        next.Add(b);
                }
                created = old == null;
                Block block = new(region, old?.X ?? x, old?.Y ?? y, Math.Max(old?.MaxBotLevel ?? 0, maxLevel),
                    now + ActiveMilliseconds);
                next.Add(block);
                if (next.Count > MaxBlocksPerRegion)
                {
                    // drop the ones that run out soonest
                    next.Sort((a, b) => b.Until.CompareTo(a.Until));
                    next.RemoveRange(MaxBlocksPerRegion, next.Count - MaxBlocksPerRegion);
                }
                Dictionary<ushort, Block[]> published = new(_blocks) { [region] = next.ToArray() };
                _blocks = published;
                if (block.Until > Volatile.Read(ref _latestUntil))
                    Volatile.Write(ref _latestUntil, block.Until);
                return block;
            }
        }

        /// <summary>The live blocks in one region for a bot of this level (an empty array when none).</summary>
        public static Block[] ActiveFor(int botLevel, ushort region, long now)
        {
            if (!_blocks.TryGetValue(region, out Block[] all) || all.Length == 0)
                return [];
            List<Block> result = null;
            foreach (Block b in all)
                if (b.Until > now && botLevel <= b.MaxBotLevel)
                    (result ??= new()).Add(b);
            return result?.ToArray() ?? [];
        }

        public static bool Blocks(Block[] blocks, ushort fromRegion, int fromX, int fromY, ushort toRegion, int toX, int toY)
        {
            foreach (Block b in blocks)
                if (Blocks(b, fromRegion, fromX, fromY, toRegion, toX, toY))
                    return true;
            return false;
        }

        /// <summary>
        /// Pure rule: the destination is within <see cref="DestinationRadius"/> of the block, or (same region, the bot not
        /// already within <see cref="Radius"/> of it) the straight way there passes within <see cref="RouteRadius"/>.
        /// </summary>
        public static bool Blocks(Block b, ushort fromRegion, int fromX, int fromY, ushort toRegion, int toX, int toY)
        {
            if (b.Region != toRegion)
                return false;
            if (Near(b.X, b.Y, toX, toY, DestinationRadius))
                return true;
            if (fromRegion != toRegion || Near(b.X, b.Y, fromX, fromY, Radius))
                return false;
            if (b.X < Math.Min(fromX, toX) - RouteRadius || b.X > Math.Max(fromX, toX) + RouteRadius ||
                b.Y < Math.Min(fromY, toY) - RouteRadius || b.Y > Math.Max(fromY, toY) + RouteRadius)
                return false;
            return AutonomousPveBossDanger.DistanceToSegment(b.X, b.Y, fromX, fromY, toX, toY) <= RouteRadius;
        }

        /// <summary>
        /// Pure rule for the camp picker: the candidates that are not blocked, unless that leaves none, then all of them
        /// (a bot is never left without a camp by a block). <paramref name="skipped"/> counts the ones dropped.
        /// </summary>
        public static List<T> KeepUnblocked<T>(List<T> candidates, Func<T, bool> blocked, out int skipped)
        {
            skipped = 0;
            if (candidates.Count <= 1)
                return candidates;
            List<T> kept = new(candidates.Count);
            foreach (T candidate in candidates)
                if (!blocked(candidate))
                    kept.Add(candidate);
            if (kept.Count == 0 || kept.Count == candidates.Count)
                return candidates;
            skipped = candidates.Count - kept.Count;
            return kept;
        }

        /// <summary>Tests only.</summary>
        public static void Clear()
        {
            lock (Sync)
            {
                Records.Clear();
                _recordCount = 0;
                _blocks = new();
                Volatile.Write(ref _latestUntil, 0);
            }
        }

        private static bool Near(int ax, int ay, int bx, int by, int radius)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy <= (double)radius * radius;
        }
    }
}
