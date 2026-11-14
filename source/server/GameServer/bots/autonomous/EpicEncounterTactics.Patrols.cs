using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Shared pieces of the 1.2 raid plans for bosses that patrol or bring linked monsters (EpicEncounterTactics.CaerSidi.cs:
    /// the Crypt Lord, the Host, Lich Lord Ilron; .Tuscaran.cs: Icelord Agmundr): where a patrolling boss will walk in the
    /// next seconds (from his script's points and its walk speed), and the living bosses of a region looked up once per list. Tests only until seen on the owner's PC.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        // ---- a patrol's next seconds (pure) ----------------------------------------------------------------------

        /// <summary>
        /// Where a boss walking <paramref name="loop"/> (in order, round and round) will be over the next
        /// <paramref name="seconds"/>, one point per <paramref name="step"/> seconds, the first his spot now: he walks at
        /// <paramref name="speed"/> towards <paramref name="loop"/>[<paramref name="next"/>], then on round the loop. Pure.
        /// </summary>
        public static Vector3[] PredictPatrol(Vector3 at, IReadOnlyList<Vector3> loop, int next, float speed, float seconds, float step = 1f)
        {
            var points = new List<Vector3> { at };
            if (loop is not { Count: > 0 } || speed <= 0 || seconds <= 0 || step <= 0) return points.ToArray();
            int n = loop.Count;
            float length = 0;
            for (int k = 0; k < n; k++) length += Vector3.Distance(loop[k], loop[(k + 1) % n]);
            int i = ((next % n) + n) % n;
            Vector3 pos = at;
            for (float t = step; t <= seconds + 0.001f; t += step)
            {
                float remaining = speed * step;
                // A loop of one spot (or of spots on one spot): he stays there.
                for (int guard = 0; remaining > 0.01f && guard < 4 * n + 4; guard++)
                {
                    Vector3 goal = loop[i];
                    float d = Vector3.Distance(pos, goal);
                    if (d <= remaining)
                    {
                        pos = goal;
                        remaining -= d;
                        i = (i + 1) % n;
                        if (length < 1) break;
                    }
                    else
                    {
                        pos += (goal - pos) / d * remaining;
                        remaining = 0;
                    }
                }
                points.Add(pos);
            }
            return points.ToArray();
        }

        /// <summary>The nearest any of <paramref name="path"/> comes to <paramref name="spot"/> (3D; pure).</summary>
        public static float ClosestApproach(IReadOnlyList<Vector3> path, Vector3 spot)
        {
            float best = float.MaxValue;
            foreach (Vector3 p in path ?? []) best = Math.Min(best, Vector3.Distance(p, spot));
            return best;
        }

        /// <summary>The first loop point not yet reached by a boss whose script marks each point it reached with a flag
        /// (<paramref name="reached"/>, in the loop's order), or the loop's first once all are (pure).</summary>
        public static int NextUnreached(IReadOnlyList<bool> reached)
        {
            for (int i = 0; i < reached.Count; i++)
                if (!reached[i]) return i;
            return 0;
        }

        // ---- living bosses of a region, once per list ---------------------------------------------------------

        private static readonly object BossLookupLock = new();
        private static (object List, int Count) _bossLookupKey;
        private static Dictionary<string, GameNPC[]> _bossLookup = new(StringComparer.Ordinal);

        /// <summary>The NPC's class or one of its base classes (up to GameNPC) has this name (a test's subclass of a boss
        /// counts as the boss).</summary>
        public static bool IsOfType(GameNPC npc, string type)
        {
            for (Type t = npc?.GetType(); t != null && t != typeof(GameNPC) && t != typeof(object); t = t.BaseType)
                if (t.Name == type) return true;
            return false;
        }

        /// <summary>The living NPCs of class <paramref name="type"/> among <paramref name="live"/> (a region's NPCs), looked up
        /// once per list: the route asks this for every monster of the region on each of its turns.</summary>
        private static GameNPC[] LiveOfType(IReadOnlyList<GameNPC> live, string type)
        {
            if (live == null) return [];
            GameNPC[] found;
            lock (BossLookupLock)
            {
                if (!ReferenceEquals(_bossLookupKey.List, live) || _bossLookupKey.Count != live.Count)
                {
                    _bossLookupKey = (live, live.Count);
                    _bossLookup = new(StringComparer.Ordinal);
                }
                if (!_bossLookup.TryGetValue(type, out found))
                    _bossLookup[type] = found = live.Where(n => n != null && IsOfType(n, type)).ToArray();
            }
            return found.Where(Present).ToArray();
        }

        /// <summary>The living boss of class <paramref name="type"/> in <paramref name="near"/>'s region within
        /// <paramref name="radius"/> of it, nearest first, or null.</summary>
        private static GameNPC BossNear(GameNPC near, IReadOnlyList<GameNPC> live, string type, int radius) =>
            near == null ? null : LiveOfType(live, type)
                .Where(b => b != near && b.CurrentRegionID == near.CurrentRegionID && b.IsWithinRadius(near, radius))
                .OrderBy(b => b.GetDistanceTo(near)).FirstOrDefault();
    }
}
