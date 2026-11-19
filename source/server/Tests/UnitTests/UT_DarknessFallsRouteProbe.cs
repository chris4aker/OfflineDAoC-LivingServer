using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool (1.2, Princess Nahemah's route): the Darkness Falls legs a Hibernian raid walks after Prince Ba'alorien, checked
    /// on the installed zone249 mesh with the strict DF validator (complete path, under 256 nodes, certified falls and
    /// climbs). Read only. Env: OFFLINE_DAOC_NAV_ROOT (a runtime/server folder with navmesh/ and lib/), OFFLINE_DAOC_PROOF_OUT.
    /// </summary>
    [TestFixture, Explicit("Darkness Falls route probe (play test)"), NonParallelizable]
    public class UT_DarknessFallsRouteProbe
    {
        private static readonly (string What, Vector3 From, Vector3 To)[] Legs =
        [
            ("Ba'alorien room -> Nahemah goal (log)", new(36668, 15524, 16064), new(19759, 24969, 16975)),
            ("Ba'alorien room -> Nahemah muster", new(36668, 15524, 16064), new(19939, 24961, 17010)),
            ("Ba'alorien room -> Nahemah spawn", new(36668, 15524, 16064), new(18560, 24840, 17203)),
            ("Ba'alorien room -> Inquisitor Asil front", new(36668, 15524, 16064), new(30504, 13584, 15780)),
            ("Asil front -> Nahemah muster", new(30504, 13584, 15780), new(19939, 24961, 17010)),
            ("Nahemah muster -> Nahemah spawn", new(19939, 24961, 17010), new(18560, 24840, 17203)),
        ];

        [Test]
        public void Probe()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            Assume.That(root != null && output != null, "set OFFLINE_DAOC_NAV_ROOT and OFFLINE_DAOC_PROOF_OUT");
            string certPath = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "GameServer", "bots",
                "autonomous", "data", "darkness_falls_navigation_proofs.json");
            var cert = JsonSerializer.Deserialize<AutonomousDarknessFallsNavigation.Certificate>(File.ReadAllText(certPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var links = cert.TraversalLinks;
            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var zone = new Zone(null, 249, "Darkness Falls", 8192, 8192, 65536, 65536, 249, false, 0, false, 0, 0, 0, 0, 0);
            var lines = new List<string>();
            static string P(Vector3 v) => $"{(int)v.X},{(int)v.Y},{(int)v.Z}";
            bool Strict(Vector3 a, Vector3 b, out PathfindingResult result, WrappedPathfindingNode[] nodes)
            {
                result = nav.GetPathStraight(zone, a, b, nav.DefaultFilters, nodes);
                return AutonomousDarknessFallsNavigation.IsStrictSegment(result, nodes, a, b, links);
            }
            try
            {
                LocalPathfindingMgr.LoadNavMesh(zone);
                Assume.That(nav.HasNavmesh(zone), "zone249.nav not loaded");
                var nodes = new WrappedPathfindingNode[600];
                foreach (var (what, from, to) in Legs)
                {
                    Vector3? fa = nav.GetClosestPoint(zone, from, 32, 32, 64, nav.DefaultFilters);
                    Vector3? fb = nav.GetClosestPoint(zone, to, 32, 32, 64, nav.DefaultFilters);
                    bool strict = Strict(from, to, out PathfindingResult r, nodes);
                    string end = r.NodeCount > 0 ? P(nodes[Math.Min(r.NodeCount, nodes.Length) - 1].Position) : "-";
                    lines.Add($"{what}: floorFrom={(fa is { } a ? P(a) : "NONE")} floorTo={(fb is { } b ? P(b) : "NONE")} " +
                              $"status={r.Status} nodes={r.NodeCount} lastNode={end} strict={strict} " +
                              $"truncated={AutonomousDarknessFallsNavigation.IsTruncatedCorridor(r, nodes, from, to)}");
                    // Greedy strict legs along Detour's own (possibly partial) path.
                    Vector3 cur = from;
                    var legs = new List<Vector3>();
                    for (int leg = 0; leg < 40 && Vector3.Distance(cur, to) > 48; leg++)
                    {
                        if (Strict(cur, to, out _, nodes)) { legs.Add(to); cur = to; break; }
                        PathfindingResult path = nav.GetPathStraight(zone, cur, to, nav.DefaultFilters, nodes);
                        int count = Math.Min(path.NodeCount, nodes.Length);
                        if (count < 2) break;
                        var copy = nodes.Take(count).Select(n => n.Position).ToArray();
                        Vector3? next = null;
                        for (int k = count - 1; k >= 1; k--)
                            if (Vector3.Distance(copy[k], cur) > 32 && Strict(cur, copy[k], out _, new WrappedPathfindingNode[600]))
                            { next = copy[k]; break; }
                        if (next == null) break;
                        legs.Add(next.Value);
                        cur = next.Value;
                    }
                    lines.Add($"  greedy strict legs: {legs.Count} reached={Vector3.Distance(cur, to) <= 48} " +
                              $"via {string.Join(" ", legs.Select(P))}");
                }
                // The certified Hibernian chains to Nahemah's muster: can the Ba'alorien room join one?
                Vector3 muster = new(19939, 24961, 17010), room = new(36668, 15524, 16064);
                var proofs = cert.Spawns.Where(p => p.Approach is { Length: 3 } && p.Routes != null)
                    .Select(p => (p, Approach: new Vector3(p.Approach[0], p.Approach[1], p.Approach[2]),
                        Route: p.Routes.FirstOrDefault(r => r.Realm == eRealm.Hibernia && r.InWaypoints is { Length: >= 2 })))
                    .Where(x => x.Route != null && Vector3.Distance(x.Approach, muster) <= RealmRaidDungeonRoute.DarknessFallsChainReach)
                    .OrderBy(x => Vector3.Distance(x.Approach, muster)).Take(8).ToArray();
                foreach (var (p, approach, route) in proofs)
                {
                    Vector3[] chain = route.InWaypoints.Select(w => new Vector3(w[0], w[1], w[2])).ToArray();
                    int join = -1;
                    foreach (int i in Enumerable.Range(0, chain.Length).OrderBy(i => Vector3.Distance(room, chain[i])).Take(32))
                        if (Strict(room, chain[i], out _, nodes)) { join = i; break; }
                    float nearest = chain.Min(w => Vector3.Distance(room, w));
                    lines.Add($"chain {p.Id} {p.Name} approach={P(approach)} toMuster={Vector3.Distance(approach, muster):0} " +
                              $"waypoints={chain.Length} nearestToRoom={nearest:0} joinFromRoom={join}");
                }
            }
            finally
            {
                LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines));
        }
    }
}
