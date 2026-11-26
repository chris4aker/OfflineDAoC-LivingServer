// DIAGNOSTIC ONLY (#704, Account #2): not for merge. Is each failing September capture standable ground on the 1.2 meshes?
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Numerics;
using System.Text;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture, NonParallelizable, Explicit("diagnostic #704")]
public class UT_Diag704Captures
{
    [Test]
    public void PrintCaptures()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        var report = new StringBuilder();
        try
        {
            DOL.UnitTests.InstalledDetour.Use(root);
            Environment.CurrentDirectory = root;
            AutonomousZoneSeams.SetForTests(null);
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");
            db.Open();
            var regions = new Dictionary<int, Region>();
            foreach (int r in new[] { 100, 200 }) regions[r] = UT_AuditedDungeonInstalledMesh.BuildRegion(db, r, loaded);
            IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
            var h = DragonLairPlacement.Home(eRealm.Hibernia);
            Vector3 dragon = new(h.X, h.Y, h.Z);
            var cases = new (string Name, int Region, Vector3 At, Vector3 Goal)[]
            {
                ("glacier/sheeroe A", 200, new(352160,680000,7224), dragon),
                ("sheeroe B", 200, new(352160,679872,7315), dragon),
                ("sheeroe C", 200, new(334820,719979,4296), dragon),
                ("expedition 1", 200, new(330492,483420,7493), new(348198,492819,5240)),
                ("expedition 2", 200, new(330845,483364,7356), new(348198,492819,5240)),
                ("expedition 3", 200, new(330807,483361,7381), new(348198,492819,5240)),
                ("expedition 4", 200, new(330511,483418,7467), new(348198,492819,5240)),
                ("muire north pocket", 200, new(330801,483353,7690), new(348198,492819,5240)),
                ("ranred", 200, new(449741,337716,5787), new(426569,319120,4729)),
                ("beroorwin (passes)", 200, new(449741,338748,3613), new(426569,319120,4729)),
                ("blodfelag", 100, new(707427,773530,4624), new(708401,772052,4553)),
                ("portal 165 start", 100, new(808662,724912,4883), new(808830,725039,4882)),
                ("mularn stable (passes)", 100, new(803743,722129,4684), new(808830,725039,4882)),
            };
            foreach (var c in cases)
            {
                Region region = regions[c.Region];
                Zone zone = region.GetZone((int)c.At.X, (int)c.At.Y);
                Zone goalZone = region.GetZone((int)c.Goal.X, (int)c.Goal.Y);
                Vector3? small = nav.GetClosestPoint(zone, c.At, 64, 64, 128, nav.DefaultFilters);
                Vector3? tall = nav.GetClosestPoint(zone, c.At, 64, 64, 4096, nav.DefaultFilters);
                Vector3 floor = small ?? tall ?? c.At;
                bool pocket = small.HasValue && AutonomousZonePockets.Contains(zone.ID, floor.X, floor.Y, floor.Z);
                bool exit = small.HasValue && AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor);
                int? area = AutonomousZoneRoads.AreaAt(zone, floor);
                int? main = AutonomousZoneSeams.MainArea((ushort)c.Region, zone.ID);
                bool corridor = small.HasValue && zone == goalZone && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor, c.Goal);
                bool dragonRoute = c.Goal == dragon && small.HasValue &&
                    RealmRaidMuster.TryRoute(region, nav, eRealm.Hibernia, floor, dragon, out var seams);
                report.AppendLine($"DIAG704C {c.Name}: region={c.Region} zone={zone?.ID} goalZone={goalZone?.ID} raw={c.At} " +
                    $"small={(small.HasValue ? $"{small.Value} dz={small.Value.Z - c.At.Z:0}" : "none")} tall={tall} pocket={pocket} " +
                    $"localExit={exit} roadArea={area} mainArea={main} sameZoneCorridor={corridor} dragonRoute={dragonRoute}");
            }
            report.AppendLine($"DIAG704C dragon home={dragon} zone={regions[200].GetZone((int)dragon.X, (int)dragon.Y)?.ID}");
        }
        finally
        {
            AutonomousZoneSeams.SetForTests(null);
            foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
        }
        Assert.Fail("DIAGNOSTIC REPORT\n" + report);
    }
}
