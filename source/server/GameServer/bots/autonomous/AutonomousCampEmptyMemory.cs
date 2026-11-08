using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// Learns which camp spots keep coming up empty (owner 2026-11-08: 26,755 of 81,251 hunts - a third - ended "no live
    /// target remained", about 3,370 bot-hours; audited-live camps 67% empty). Each empty hunt adds one to the spot's
    /// score and each kill there takes a little off; both halve every <see cref="HalfLifeMinutes"/>, so a spot recovers
    /// on its own once bots stop finding it empty. A spot's draw weight is divided by one plus its score, and a spot at
    /// <see cref="RetireScore"/> or more is left out of planning while any other camp is left.
    /// Spots are keyed by region, monster and a <see cref="CellSize"/> cell, not by camp id (live-cluster ids change as
    /// their monsters die and respawn). Cost: one small dictionary update per empty hunt or kill; nothing per tick.
    /// Only camp-empty outcomes feed it - deaths and route trouble have their own benches.
    /// </summary>
    public static class AutonomousCampEmptyMemory
    {
        public const double HalfLifeMinutes = 45;
        public const double RetireScore = 4.0;
        /// <summary>Kills that offset one empty hunt.</summary>
        public const double KillsPerEmpty = 4.0;
        public const int CellSize = 3_000;
        private const int MaxEntries = 32_768;

        private sealed class Entry
        {
            public double Empty;
            public double Productive;
            public long Tick;
        }

        private static readonly ConcurrentDictionary<string, Entry> Entries = new(StringComparer.Ordinal);
        private static int _entryCount;

        public static string Key(ushort region, string monster, int x, int y) =>
            $"{region}:{AutonomousCampCompetition.NormalizeName(monster)}:{(long)Math.Floor(x / (double)CellSize)}:{(long)Math.Floor(y / (double)CellSize)}";

        /// <summary>Pure decay: the value after <paramref name="elapsedMilliseconds"/>.</summary>
        public static double Decay(double value, long elapsedMilliseconds) =>
            elapsedMilliseconds <= 0 ? value : value * Math.Pow(0.5, elapsedMilliseconds / (HalfLifeMinutes * 60_000));

        /// <summary>Pure score: empty hunts not offset by kills.</summary>
        public static double Score(double empty, double productive) => Math.Max(0, empty - productive);

        /// <summary>Pure draw-weight factor for a score.</summary>
        public static double WeightFactor(double score) => 1.0 / (1.0 + Math.Max(0, score));

        /// <summary>Pure rule (owner 2026-11-08): a hunt counts as empty only after the full empty-camp wait; a bot that
        /// left a crowded camp sooner found it busy, not empty.</summary>
        public static bool CountsAsEmpty(long waitedMilliseconds, long fullWaitMilliseconds) =>
            waitedMilliseconds >= fullWaitMilliseconds;

        public static void RecordEmpty(string key, long now)
        {
            if (string.IsNullOrEmpty(key))
                return;
            if (!Entries.TryGetValue(key, out Entry entry))
            {
                entry = new Entry { Tick = now };
                if (Entries.TryAdd(key, entry))
                {
                    // the size is only looked at when an entry is added (Count locks the whole dictionary)
                    if (Interlocked.Increment(ref _entryCount) > MaxEntries)
                    {
                        Entries.Clear();
                        Interlocked.Exchange(ref _entryCount, 0);
                    }
                }
                else
                    entry = Entries.GetOrAdd(key, entry);
            }
            Update(entry, now, 1, 0);
        }

        /// <summary>A kill at a spot already found empty takes a little off its score (spots never found empty keep no
        /// entry, so kills add nothing to the dictionary).</summary>
        public static void RecordKill(string key, long now)
        {
            if (!string.IsNullOrEmpty(key) && Entries.TryGetValue(key, out Entry entry))
                Update(entry, now, 0, 1 / KillsPerEmpty);
        }

        private static void Update(Entry entry, long now, double empty, double productive)
        {
            lock (entry)
            {
                long elapsed = now - entry.Tick;
                entry.Empty = Decay(entry.Empty, elapsed) + empty;
                entry.Productive = Decay(entry.Productive, elapsed) + productive;
                entry.Tick = Math.Max(entry.Tick, now);
            }
        }

        /// <summary>The spot's current score (0 for a spot never found empty).</summary>
        public static double CurrentScore(string key, long now)
        {
            if (string.IsNullOrEmpty(key) || !Entries.TryGetValue(key, out Entry entry))
                return 0;
            lock (entry)
            {
                long elapsed = now - entry.Tick;
                return Score(Decay(entry.Empty, elapsed), Decay(entry.Productive, elapsed));
            }
        }

        public static bool IsRetired(string key, long now) => CurrentScore(key, now) >= RetireScore;

        public static double Factor(string key, long now) => WeightFactor(CurrentScore(key, now));

        /// <summary>Tests only.</summary>
        public static void Clear()
        {
            Entries.Clear();
            Interlocked.Exchange(ref _entryCount, 0);
        }
    }
}
