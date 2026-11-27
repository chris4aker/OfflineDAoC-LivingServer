using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Live spawns the period catalog never recorded become camps (GitHub #544; ported from True 1.65's
    /// AddUncoveredLiveCamps, upstream 8d0dfdda). In a zone the CapnBry sightings cover, a spawn had a camp only if a
    /// sighting of its name was within 5,000 units; the source-empty zones (most of Hibernia: 13 of its outdoor zones,
    /// against 7 of Albion's and none of Midgard's) make a camp of every live cluster. So Hibernia's catalog held about
    /// twice Albion's camps per level band, and its bots levelled about twice as fast. True 1.65's audit of the same world
    /// found no camp for 1,334 of Campacorentin's 1,682 spawns and 862 of Avalon Marsh's 1,132.
    /// <para>The rule is the same in every realm. A cluster of at least <see cref="MinimumSpawns"/> spawns of one name and
    /// level in a <see cref="LevelCellSize"/>-unit cell, with no camp of that name within the cover radius, becomes a camp
    /// at the spawn nearest its centre. A second pass takes the spawns still uncovered in clusters of mixed levels: at least
    /// three of one name in an ordinary <see cref="MixedCellSize"/>-unit camp cell. No monster is added, moved or changed.</para>
    /// <para>Pure, so it runs on the background planner and in tests.</para>
    /// </summary>
    public static class AutonomousUncoveredLiveCamps
    {
        public const int MinimumSpawns = 3;
        public const int LevelCellSize = 1_500;
        public const int MixedCellSize = 4_200;
        public const string Prefix = "live-uncovered:";

        /// <summary>One live outdoor spawn that may become part of a camp (the caller filters dungeons, battlegrounds and
        /// zones outside Classic and SI). <paramref name="Id"/> only breaks ties when two spawns are as near the centre.</summary>
        public readonly record struct Spawn(ushort Zone, ushort Region, int X, int Y, string Name, int Level, string Id);

        /// <summary>A new camp: its spawns (indexes into the input), the one it is anchored at and its id.</summary>
        public sealed record Cluster(int[] Members, int Representative, bool MixedLevels, string CampId);

        public static string NormalizeName(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// The camps to add. <paramref name="covered"/> holds the anchors of the camps already in the catalog by region and
        /// normalized monster name; a spawn within <paramref name="coverRadius"/> of one of its own name is covered.
        /// </summary>
        public static List<Cluster> Find(IReadOnlyList<Spawn> spawns,
            IReadOnlyDictionary<(ushort Region, string Name), (int X, int Y)[]> covered, int coverRadius)
        {
            var result = new List<Cluster>();
            if (spawns == null || spawns.Count == 0)
                return result;
            long reach = (long)coverRadius * coverRadius;
            var anchors = new Dictionary<(ushort Region, string Name), List<(int X, int Y)>>();
            if (covered != null)
                foreach (var pair in covered)
                    anchors[(pair.Key.Region, NormalizeName(pair.Key.Name))] = new List<(int X, int Y)>(pair.Value ?? []);
            string[] names = spawns.Select(spawn => NormalizeName(spawn.Name)).ToArray();

            bool Covered(int index)
            {
                Spawn spawn = spawns[index];
                if (!anchors.TryGetValue((spawn.Region, names[index]), out var spots))
                    return false;
                foreach (var spot in spots)
                {
                    long dx = spawn.X - spot.X, dy = spawn.Y - spot.Y;
                    if (dx * dx + dy * dy <= reach)
                        return true;
                }
                return false;
            }

            // first pass: one name and level per 1,500-unit cell
            var byLevel = new Dictionary<(ushort, ushort, int, int, string, int), List<int>>();
            for (int i = 0; i < spawns.Count; i++)
            {
                if (string.IsNullOrEmpty(names[i]) || Covered(i))
                    continue;
                Spawn spawn = spawns[i];
                var key = (spawn.Zone, spawn.Region, FloorDiv(spawn.X, LevelCellSize), FloorDiv(spawn.Y, LevelCellSize), names[i], spawn.Level);
                if (!byLevel.TryGetValue(key, out var members))
                    byLevel[key] = members = new List<int>();
                members.Add(i);
            }
            foreach (var pair in byLevel.OrderBy(pair => pair.Value[0]))
            {
                if (pair.Value.Count < MinimumSpawns)
                    continue;
                var (zone, region, cellX, cellY, name, level) = pair.Key;
                result.Add(new Cluster(pair.Value.ToArray(), Representative(spawns, pair.Value), false,
                    $"{Prefix}{zone}:{region}:{cellX}:{cellY}:{name}:{level}"));
            }

            // the first pass's camps cover their surroundings too
            foreach (Cluster cluster in result)
            {
                int index = cluster.Representative;
                var key = (spawns[index].Region, names[index]);
                if (!anchors.TryGetValue(key, out var spots))
                    anchors[key] = spots = new List<(int X, int Y)>();
                spots.Add((spawns[index].X, spawns[index].Y));
            }

            // second pass: what is still uncovered, any level, one name per ordinary camp cell
            var mixed = new Dictionary<(ushort, ushort, int, int, string), List<int>>();
            for (int i = 0; i < spawns.Count; i++)
            {
                if (string.IsNullOrEmpty(names[i]) || Covered(i))
                    continue;
                Spawn spawn = spawns[i];
                var key = (spawn.Zone, spawn.Region, FloorDiv(spawn.X, MixedCellSize), FloorDiv(spawn.Y, MixedCellSize), names[i]);
                if (!mixed.TryGetValue(key, out var members))
                    mixed[key] = members = new List<int>();
                members.Add(i);
            }
            foreach (var pair in mixed.OrderBy(pair => pair.Value[0]))
            {
                if (pair.Value.Count < MinimumSpawns)
                    continue;
                var (zone, region, cellX, cellY, name) = pair.Key;
                result.Add(new Cluster(pair.Value.ToArray(), Representative(spawns, pair.Value), true,
                    $"{Prefix}{zone}:{region}:{cellX}:{cellY}:{name}:mixed"));
            }
            return result;
        }

        /// <summary>The spawn nearest the cluster's centre (never the arithmetic average, which can fall between two
        /// walkable surfaces).</summary>
        private static int Representative(IReadOnlyList<Spawn> spawns, List<int> members)
        {
            double averageX = members.Average(index => (double)spawns[index].X);
            double averageY = members.Average(index => (double)spawns[index].Y);
            return members
                .OrderBy(index => Math.Abs(spawns[index].X - averageX) + Math.Abs(spawns[index].Y - averageY))
                .ThenBy(index => spawns[index].Id ?? string.Empty, StringComparer.Ordinal)
                .First();
        }

        private static int FloorDiv(int value, int size) => (int)Math.Floor(value / (double)size);
    }
}
