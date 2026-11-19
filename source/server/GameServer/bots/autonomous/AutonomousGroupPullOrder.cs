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
            Dictionary<GameNPC, int> count = CountsOf(pool);
            return pool.OrderBy(npc => count[npc]).ThenByDescending(npc => npc.EffectiveLevel);
        }

        /// <summary>
        /// The same order for a party's puller, with its skill (Bots as people WP2, docs/BOTS-AS-PEOPLE-1.2.md, item 2):
        /// now and then (Casual 10% of pulls, Average 5%, Veteran 1.5%; a quarter of that in a group with a real player)
        /// the puller takes a monster with one neighbour inside its aggro range instead of the clean one, so one add comes
        /// along. Never a named, never more than 2 levels above the group, the add never a harder con, only in a party of
        /// three or more with everyone alive and outside dungeons, at most once in 3 minutes (BotSkillMistakes.ChooseAddPull).
        /// Out of scope (BotPeopleScope, rule Mistakes) or switched off: exactly today's order.
        /// </summary>
        public static IOrderedEnumerable<GameNPC> OrderForPull(this IEnumerable<GameNPC> candidates, bool party, GameBot puller)
        {
            if (!party || puller == null)
                return candidates.OrderForPull(party);
            GameNPC[] pool = candidates.ToArray();
            int careless = BotSkillHands.PullMistake(puller, pool);
            if (careless < 0)
                return pool.OrderForPull(true);
            GameNPC first = pool[careless];
            Dictionary<GameNPC, int> count = CountsOf(pool);
            // The careless pick first; the rest exactly as today (the caller's distance order follows).
            return pool.OrderBy(npc => npc == first ? 0 : 1).ThenBy(npc => count[npc]).ThenByDescending(npc => npc.EffectiveLevel);
        }

        private static Dictionary<GameNPC, int> CountsOf(GameNPC[] pool)
        {
            int[] neighbours = NeighbourCounts(pool.Select(npc => (new Vector3(npc.X, npc.Y, npc.Z),
                (npc.Brain as IOldAggressiveBrain)?.AggroRange ?? 0)).ToArray());
            var count = new Dictionary<GameNPC, int>(pool.Length);
            for (int index = 0; index < pool.Length; index++)
                count[pool[index]] = neighbours[index];
            return count;
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
