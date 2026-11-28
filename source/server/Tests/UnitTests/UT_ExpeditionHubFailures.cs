using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[NonParallelizable, Explicit("Read-only reproduction of captured expedition routes")]
public class UT_ExpeditionHubFailures
{
    /// <summary>Where Tir na Nog's exit 19 puts a bot in Lough Derg (ZonePoint 19's target).</summary>
    private static readonly Vector3 LoughDergArrival = new(332733, 485215, 5215);

    [Test]
    public void CapturedSourceApproaches()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        try
        {
            DOL.UnitTests.InstalledDetour.Use(root);
            Environment.CurrentDirectory = root;
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");
            db.Open();
            // #704: every capture is checked and a red run lists them all.
            Assert.Multiple(() =>
            {
            foreach (var s in new[] {
                (10, 9, new Vector3(32543,26691,7831)),
                (10, 9, new Vector3(32323,26409,8363)),
                (10, 9, new Vector3(32537,26927,7834)),
                (10, 5, new Vector3(32543,26691,7831)),
                (10, 5, new Vector3(32323,26409,8363)),
                (10, 5, new Vector3(32537,26927,7834)),
                (100,165,new Vector3(768012,737271,5558)),
                (100,165,new Vector3(768592,736737,5861)),
                (200,167,new Vector3(330492,483420,7493)),
                (200,167,new Vector3(330845,483364,7356)),
                (200,167,new Vector3(330807,483361,7381)),
                (200,167,new Vector3(330511,483418,7467)) })
            {
                var region = (Region)typeof(UT_AuditedDungeonInstalledMesh).GetMethod("BuildRegion", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] {db,s.Item1,loaded});
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"SELECT SourceX,SourceY,SourceZ FROM ZonePoint WHERE Id={s.Item2}";
                using var r = cmd.ExecuteReader();
                bool edgeRow = r.Read();
                Assert.That(edgeRow, Is.True, $"ZonePoint {s.Item2} exists");
                if (!edgeRow) continue;
                Vector3 target = new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2));
                var zone=region.GetZone((int)s.Item3.X,(int)s.Item3.Y);
                var nav=PathfindingProvider.LocalPathfindingMgr;
                bool shelf = s.Item1 == 200;
                if (shelf)
                {
                    // #704: the four Lough Derg captures (September) stand about 2,100 above the Tir na Nog exit 19 arrival,
                    // on side ground (area 69468, not the zone's main ground), with no route from them to edge 167's source.
                    // What is checked: no route from the exit 19 arrival reaches them. That ground is a listed seam network
                    // with Connacht and zone 201 (seams.json), so other ways on are not ruled out: #845. Pinned as a cut.
                    Vector3? near=nav.GetClosestPoint(zone,s.Item3,64,64,128,nav.DefaultFilters);
                    Assert.That(near.HasValue, Is.True, $"Shelf {s}: floor under the capture");
                    Vector3 floorStart=near ?? s.Item3;
                    var ground=InstalledBotRoute.Ground(200,zone,floorStart);
                    bool walk=InstalledBotRoute.Itinerary(region,nav,floorStart,target,out string trace);
                    bool fromExit=InstalledBotRoute.Itinerary(region,nav,LoughDergArrival,floorStart,out string exitTrace);
                    TestContext.WriteLine($"SHELF {s} floor={near} area={ground.Area} main={ground.Main} walk={walk}: {trace}; fromArrival={fromExit}: {exitTrace}");
                    Assert.That(ground.Area.HasValue && ground.Main.HasValue && ground.Area != ground.Main, Is.True,
                        $"Shelf {s}: the cut, side ground (area {ground.Area}, main {ground.Main})");
                    Assert.That(walk, Is.False, $"Shelf {s}: the cut, no route from the shelf to the source ({trace})");
                    Assert.That(fromExit, Is.False, $"Shelf {s}: no route from the exit 19 arrival onto it ({exitTrace})");
                }
                bool success=AutonomousZonePointApproach.TryResolve(nav,zone,s.Item3,target,112,out var approach);
                var edge = new DOL.Database.DbZonePoint { Id=(ushort)s.Item2, SourceRegion=(ushort)s.Item1, SourceX=(int)target.X, SourceY=(int)target.Y, SourceZ=(int)target.Z };
                bool repaired=AutonomousRouteHotspotRepair.TryResolveAuditedCrossingSource(nav,region,edge,s.Item3,out var corrected);
                TestContext.WriteLine($"REPAIR {s} repaired={repaired} corrected={corrected}");
                if(shelf)
                {
                    Assert.That(success || repaired,Is.False,$"Shelf {s}: the cut, neither an approach nor the audited crossing repair");
                }
                else if(s.Item1 != 10)
                {
                    Assert.That(success || repaired,Is.True,$"Captured expedition source {s}");
                    if(repaired)
                    {
                        Assert.That(Vector3.Distance(corrected,s.Item3),Is.LessThan(96));
                        Assert.That(AutonomousZonePointApproach.TryResolve(nav,zone,corrected,target,112,out _),Is.True);
                    }
                }
                TestContext.WriteLine($"SOURCE {s} target={target} approach={success}:{approach} floor={nav.GetClosestPoint(zone,s.Item3,96,96,1024,nav.DefaultFilters)}");
                foreach (int radius in new[] {0,32,64,128,256,512})
                for(int a=0;a<(radius==0?1:8);a++)
                {
                    Vector3 raw=s.Item3+new Vector3(MathF.Cos(a*MathF.PI/4)*radius,MathF.Sin(a*MathF.PI/4)*radius,0);
                    var floor=nav.GetClosestPoint(zone,raw,16,16,1024,nav.DefaultFilters);
                    if(floor.HasValue && AutonomousZonePointApproach.TryResolve(nav,zone,floor.Value,target,112,out _))
                        TestContext.WriteLine($"CONNECTED radius={radius} point={floor} delta={floor.Value-s.Item3}");
                }
            }
            {
                // The main-ground route to edge 167's source: from the Tir na Nog exit 19 landing, the zone point arrival nearest
                // the captures (the only ZonePoint target within 3,000 of them).
                var region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, 200, loaded);
                var nav = PathfindingProvider.LocalPathfindingMgr;
                Vector3 target = new(348198, 492819, 5240);
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = "SELECT SourceX,SourceY,SourceZ FROM ZonePoint WHERE Id=167";
                    using var r = cmd.ExecuteReader();
                    bool edgeRow = r.Read();
                    Assert.That(edgeRow, Is.True, "ZonePoint 167 exists");
                    if (edgeRow) target = new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
                }
                Zone zone = region.GetZone((int)LoughDergArrival.X, (int)LoughDergArrival.Y);
                using (var arrivals = db.CreateCommand())
                {
                    arrivals.CommandText = "SELECT count(*) FROM ZonePoint WHERE Id=19 AND TargetRegion=200 AND TargetX=332733 AND TargetY=485215";
                    Assert.That(Convert.ToInt32(arrivals.ExecuteScalar()), Is.EqualTo(1), "exit 19 lands at the Lough Derg arrival");
                }
                Assert.That(AutonomousZonePointApproach.TryResolve(nav, zone, LoughDergArrival, target, 112, out Vector3 approach), Is.True,
                    "From the exit 19 arrival the bot has an approach to edge 167's source");
                Assert.That(InstalledBotRoute.Itinerary(region, nav, LoughDergArrival, approach, out string arrivalTrace), Is.True,
                    $"From the exit 19 arrival the bot's route reaches that approach: {arrivalTrace}");
            }
            });
        }
        finally { foreach(var z in loaded) LocalPathfindingMgr.UnloadNavMesh(z); Environment.CurrentDirectory=previous; }
    }
}
