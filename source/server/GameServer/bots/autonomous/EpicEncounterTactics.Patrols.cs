using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Shared pieces of the 1.2 raid plans for bosses that patrol or bring linked monsters (EpicEncounterTactics.CaerSidi.cs:
    /// the Crypt Lord, the Host, Lich Lord Ilron; .Tuscaran.cs: Icelord Agmundr): where a patrolling boss will walk in the
    /// next seconds (from his script's points and its walk speed), the living bosses of a region looked up once per list,
    /// and the generic rule that defers a monster perched out of reach above its floor (Tuscaran Glacier's griffon
    /// icewings). Tests only until seen on the owner's PC.
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

        // ---- a scripted loop path (MovementMgr.CreatePath, OpenDAoC-Core's patrol cleanup) -------------------------

        /// <summary>A boss's scripted loop (his script's points, the first his spawn) as a patrol for <see cref="PredictPatrol"/>,
        /// starting at its second point: the order the old WalkTo chains (point1 ... his spawn last) were read in (pure).</summary>
        public static Vector3[] LoopFromSecondPoint(IReadOnlyList<(int X, int Y, int Z)> points) =>
            points is not { Count: > 0 } ? []
                : points.Skip(1).Append(points[0]).Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray();

        /// <summary>The index in his script's points of the point <paramref name="npc"/> walks to on his scripted loop (the
        /// first is 0), or -1 without one.</summary>
        public static int ScriptedPathIndex(GameNPC npc)
        {
            DOL.GS.Movement.PathPoint at = npc?.CurrentPathPoint, first = npc?.ScriptedPath;
            if (at == null || first == null) return -1;
            int i = 0;
            for (DOL.GS.Movement.PathPoint p = first; p != null && i < 10_000; p = p.Next, i++)
                if (ReferenceEquals(p, at)) return i;
            return -1;
        }

        /// <summary>The patrol point (an index of <see cref="LoopFromSecondPoint"/>'s loop of <paramref name="count"/>) that
        /// <paramref name="npc"/> walks to now; the loop's first (his script's second point) when he has no path yet.</summary>
        public static int NextOnScriptedLoop(GameNPC npc, int count)
        {
            int at = ScriptedPathIndex(npc);
            return at < 0 || count <= 0 ? 0 : (at - 1 + count) % count;
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

        // ---- a monster perched out of reach (Tuscaran Glacier's griffon icewings) -------------------------------

        /// <summary>A flying monster further than this above the floor under it is deferred (spells and bows reach 1,500
        /// from a floor spot beside it at best; the raid's muster stands 1,400 off).</summary>
        public const int PerchedFlyerHeight = 900;
        /// <summary>The floor is looked for this far below it.</summary>
        public const int PerchedFloorSearch = 2_500;

        /// <summary>The monster stands this far above its floor that the raid neither reaches it nor shoots it from where it
        /// musters (pure): a flyer, more than <see cref="PerchedFlyerHeight"/> above the floor found under it, and not
        /// fighting.</summary>
        public static bool PerchedOutOfReach(bool flying, bool inCombat, int monsterZ, int? floorZ) =>
            flying && !inCombat && floorZ is int floor && monsterZ - floor > PerchedFlyerHeight;

        private static readonly Dictionary<GameNPC, (long Tick, int? Floor)> PerchFloors = new();

        /// <summary>The floor's height under <paramref name="npc"/> (navmesh), looked up once a minute per monster; null
        /// without a navmesh. Settable for tests.</summary>
        public static Func<GameNPC, int?> FloorUnder = npc =>
        {
            var nav = PathfindingProvider.Instance;
            Zone zone = npc?.CurrentZone;
            if (zone == null || nav is not { IsAvailable: true } || !nav.HasNavmesh(zone)) return null;
            return nav.GetClosestPoint(zone, new Vector3(npc.X, npc.Y, npc.Z), 128, 128, PerchedFloorSearch, nav.DefaultFilters) is Vector3 f
                ? (int)f.Z : null;
        };

        private static int? CachedFloorUnder(GameNPC npc, long now)
        {
            lock (PerchFloors)
                if (PerchFloors.TryGetValue(npc, out var hit) && now - hit.Tick < 60_000) return hit.Floor;
            int? floor = FloorUnder(npc);
            lock (PerchFloors)
            {
                if (PerchFloors.Count > 512) PerchFloors.Clear();
                PerchFloors[npc] = (now, floor);
            }
            return floor;
        }

        public static void ForgetPerchFloors()
        {
            lock (PerchFloors) PerchFloors.Clear();
        }

        /// <summary>
        /// PC log e3b8238 (Tuscaran Glacier, 06:17-06:33): a griffon icewing perched 1,129 above its floor was the raid's
        /// target; its muster sat 0/68 for 15.5 minutes while the raid walked recovery loops under it, and once there it was
        /// killed in 30 s. Its 24 kin sit 1,100-2,000 above the floor too. Such epic trash (a plain GameEpicNPC that flies)
        /// is deferred while it sits up there: the route takes another target, and fights it when it comes down or attacks.
        /// Bosses that fly (Torst, Hurika) have their own rules and are never deferred here.
        /// </summary>
        private static string PerchedFlyerWait(GameNPC target, IReadOnlyList<GameNPC> live, long now)
        {
            if (target?.GetType() != typeof(GameEpicNPC) || (target.Flags & GameNPC.eFlags.FLYING) == 0 || target.InCombat) return null;
            int? floor = CachedFloorUnder(target, now);
            return PerchedOutOfReach(true, false, target.Z, floor)
                ? $"perched {target.Z - floor} above its floor: out of reach until it comes down"
                : null;
        }

        [DungeonRules]
        private static IEnumerable<DungeonRule> PatrolRules() =>
        [
            new("perched-flyer", [], null)
            {
                Summary = "Epic trash that flies and perches more than 900 above its floor (the glacier's griffon icewings) is " +
                          "deferred while it sits there: the route takes another target and fights it when it comes down.",
                PullWait = PerchedFlyerWait,
            },
        ];
    }
}
