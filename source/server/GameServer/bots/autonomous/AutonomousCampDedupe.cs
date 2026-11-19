using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Merges catalog camps that are really one camp (owner 2026-11-08). Live-cluster camps are cut per monster level and
    /// per 1,500-unit cell, so one patch of Silvermine hill toads became up to 24 overlapping camp ids sharing about 13
    /// toads; every id was drawn as its own camp, so hill toad took 31% of Hibernia's level 8-11 hunts and 87% of them
    /// ended "no live hill toad". Same-name outdoor camps in one region whose anchors are within <see cref="MergeRadius"/>
    /// of a seed become that seed: the seed keeps its id and anchor (authored sources first, then the most monsters), and
    /// takes the members' spawn levels and monsters. No monster is added, moved or changed.
    /// </summary>
    public static class AutonomousCampDedupe
    {
        public const int MergeRadius = 2_500;

        public readonly record struct Candidate(ushort Region, string Name, int X, int Y, int Priority, int LiveMobs, bool Mergeable, string Id);

        /// <summary>Seed priority by camp source: authored and verified anchors first.</summary>
        public static int SourcePriority(string campId)
        {
            if (string.IsNullOrEmpty(campId)) return 0;
            if (campId.StartsWith("classic165-restored:", StringComparison.OrdinalIgnoreCase) ||
                campId.StartsWith("capnbry:", StringComparison.OrdinalIgnoreCase)) return 3;
            if (campId.StartsWith("audited-live:", StringComparison.OrdinalIgnoreCase)) return 2;
            if (campId.StartsWith("live-uncovered:", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        /// <summary>Only ordinary outdoor PvE camp sources are merged (not starter, battleground or dungeon camps), and never
        /// a camp with its own hard-coded rule (the Salisbury spirit camp is for parties of four only).</summary>
        public static bool IsMergeableSource(string campId) =>
            SourcePriority(campId) > 0 && AutonomousAuditedCampPolicy.CanAssignToParty(campId, 1);

        /// <summary>
        /// For each candidate, the index of the camp it merges into (its own index when it is a seed or not mergeable).
        /// Greedy: by priority, then monsters, then id; each seed absorbs every unassigned same-name camp within the radius.
        /// </summary>
        public static int[] SeedOf(IReadOnlyList<Candidate> camps, int radius = MergeRadius)
        {
            int[] seed = new int[camps.Count];
            for (int i = 0; i < seed.Length; i++)
                seed[i] = i;
            long reach = (long)radius * radius;
            foreach (var group in Enumerable.Range(0, camps.Count)
                         .Where(i => camps[i].Mergeable && !string.IsNullOrEmpty(camps[i].Name))
                         .GroupBy(i => (camps[i].Region, Name: camps[i].Name.Trim().ToLowerInvariant())))
            {
                int[] order = group
                    .OrderByDescending(i => camps[i].Priority)
                    .ThenByDescending(i => camps[i].LiveMobs)
                    .ThenBy(i => camps[i].Id ?? string.Empty, StringComparer.Ordinal)
                    .ToArray();
                if (order.Length < 2)
                    continue;
                var assigned = new bool[camps.Count];
                foreach (int s in order)
                {
                    if (assigned[s])
                        continue;
                    assigned[s] = true;
                    foreach (int other in order)
                    {
                        if (assigned[other])
                            continue;
                        long dx = (long)camps[s].X - camps[other].X, dy = (long)camps[s].Y - camps[other].Y;
                        if (dx * dx + dy * dy > reach)
                            continue;
                        assigned[other] = true;
                        seed[other] = s;
                    }
                }
            }
            return seed;
        }
    }

    /// <summary>
    /// Camps sitting among much stronger aggressive monsters (owner 2026-11-08). Route-threat scanning ignores monsters
    /// within <see cref="AutonomousRouteThreatPolicy.CampSkipRadius"/> of the camp (they are "the camp"), so a camp of
    /// level-6 spore seeds beside level 33-36 cniogcrag cutters (Cothrom Gorge) kept sending level-8 bots to their deaths.
    /// A camp whose radius holds an aggressive monster <see cref="LevelsAboveCamp"/>+ levels above the camp's highest
    /// spawn is not handed to a bot that monster would out-level by <see cref="LevelsAboveSoloBot"/> (solo) or
    /// <see cref="LevelsAboveParty"/> (a party of four or more). The camp stays for everyone it is safe for.
    /// </summary>
    public static class AutonomousCampThreatPolicy
    {
        public const int LevelsAboveCamp = 10;
        public const int LevelsAboveSoloBot = 3;
        public const int LevelsAboveParty = 6;

        public static bool IsDangerousFor(int threatLevel, int campMaxLevel, int botLevel, int groupSize) =>
            threatLevel > 0 && threatLevel >= campMaxLevel + LevelsAboveCamp &&
            threatLevel >= botLevel + (groupSize >= 4 ? LevelsAboveParty : LevelsAboveSoloBot);
    }

    /// <summary>
    /// The camp draw weight (owner 2026-11-08): capped live spawns x nearness x 40, divided by one plus the bots already
    /// there, by the square root of how many legal camps the same monster has (so one monster with dozens of camps no
    /// longer takes most draws), and scaled by the empty-camp memory factor (1 for a spot never found empty).
    /// </summary>
    public static class AutonomousCampSpread
    {
        public static int Weight(int spawnWeight, int nearness, int botsThere, int campsOfThisMonster, double emptyFactor) =>
            Math.Max(1, (int)Math.Round(Math.Max(1, spawnWeight) * Math.Max(1, nearness) * 40.0 /
                                        (1 + Math.Max(0, botsThere)) / Math.Sqrt(Math.Max(1, campsOfThisMonster)) *
                                        Math.Clamp(emptyFactor, 0, 1)));
    }
}
