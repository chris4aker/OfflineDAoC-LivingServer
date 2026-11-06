using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Which camp monster a gamebot party's puller takes first. Prima's pulling appendix: pull one and only one, the one
    /// a bit farther from the rest; so the monster with the fewest other camp monsters within its aggro range comes
    /// first, then the highest level as before. Parties used to take the highest level, wherever it stood. Solo bots are
    /// unchanged. From Offline DAoC True 1.65 (shadowofze, 2026-11-06), where it is that edition's period rule.
    /// </summary>
    public static class AutonomousGroupPullOrder
    {
        public static IOrderedEnumerable<GameNPC> OrderForPull(this IEnumerable<GameNPC> candidates, bool party)
        {
            if (!party)
                return candidates.OrderByDescending(npc => party ? npc.EffectiveLevel : 0);
            GameNPC[] pool = candidates.ToArray();
            int[] neighbours = NeighbourCounts(pool.Select(npc => (new Vector3(npc.X, npc.Y, npc.Z),
                (npc.Brain as IOldAggressiveBrain)?.AggroRange ?? 0)).ToArray());
            var count = new Dictionary<GameNPC, int>(pool.Length);
            for (int index = 0; index < pool.Length; index++)
                count[pool[index]] = neighbours[index];
            return pool.OrderBy(npc => count[npc]).ThenByDescending(npc => npc.EffectiveLevel);
        }

        /// <summary>For each monster, how many of the others stand within its aggro range (a few dozen at most).</summary>
        public static int[] NeighbourCounts((Vector3 Position, int AggroRange)[] monsters)
        {
            int[] counts = new int[monsters.Length];
            for (int index = 0; index < monsters.Length; index++)
            {
                long range = monsters[index].AggroRange;
                if (range <= 0)
                    continue;
                float rangeSquared = range * range;
                for (int other = 0; other < monsters.Length; other++)
                    if (other != index && Vector3.DistanceSquared(monsters[index].Position, monsters[other].Position) <= rangeSquared)
                        counts[index]++;
            }
            return counts;
        }
    }
}
