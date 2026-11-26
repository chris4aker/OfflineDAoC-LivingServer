using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// #555: AUTONOMOUS_ZONE_SEAM_CYCLE on 11/23 (build 1.2.2 868a82d), twice at the same spot: two level 7-8 Hibernian gamebots in
/// Cruachan Gorge (zone 211) at 334880,417728,6528, travelling to the orchard nipper camp at 355817,484592,6728 (Lough Derg,
/// zone 200), each stepped 334880,417728 > 334880,417856 (into Connacht, 207) and back. Replays the zone itinerary from that
/// spot on the installed meshes and seams (navmesh-v1, byte-identical to the owner's): the trip must reach the goal zone
/// without coming back to a zone it left, and end on a complete corridor.
/// </summary>
[TestFixture, NonParallelizable, Explicit("Read-only installed Hibernian seam-cycle replay (#555)")]
public class UT_SeamCycle555
{
    [Test]
    public void CruachanToTheOrchardNipperCampNeverTurnsBack()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        Assert.That(root, Is.Not.Null.And.Not.Empty);
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        try
        {
            DOL.UnitTests.InstalledDetour.Use(root);
            Environment.CurrentDirectory = root;
            AutonomousZoneSeams.SetForTests(null);
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");
            db.Open();
            Region region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, 200, loaded);
            IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 start = new(334880, 417728, 6528);
            Vector3 goal = new(355817, 484592, 6728);
            Zone current = region.GetZone((int)start.X, (int)start.Y);
            Zone destination = region.GetZone((int)goal.X, (int)goal.Y);
            Assert.That((current?.ID, destination?.ID), Is.EqualTo(((ushort?)211, (ushort?)200)), "the logged zones");
            var left = new List<ushort>();
            for (int hop = 0; hop < 16 && current != destination; hop++)
            {
                bool routed = AutonomousZoneItinerary.TryNextStep(region, current, destination, start, goal, nav,
                    out AutonomousZoneBoundaryRouting.Step step);
                Zone next = routed ? region.GetZone((int)step.Outside.X, (int)step.Outside.Y) : null;
                TestContext.Out.WriteLine($"SEAM555 hop={hop} zone={current.ID} at={start} routed={routed} step={step} next={next?.ID}");
                Assert.That(routed, Is.True, $"hop {hop}: no step out of zone {current.ID}");
                left.Add(current.ID);
                Assert.That(left, Does.Not.Contain(next.ID), $"hop {hop}: the trip turns back into zone {next.ID} ({string.Join(">", left)})");
                start = step.Outside;
                current = next;
            }
            Assert.That(current, Is.EqualTo(destination), "reached the goal zone");
            Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, destination, start, goal), Is.True, "and walks on to the camp");
        }
        finally
        {
            AutonomousZoneSeams.SetForTests(null);
            foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
        }
    }
}
