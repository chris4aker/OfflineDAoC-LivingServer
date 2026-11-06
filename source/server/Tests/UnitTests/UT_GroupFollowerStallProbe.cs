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

[Explicit("Read-only installed navmesh probe: two group members that stood still 'Traveling in group formation' 1,300 units from their leader on 2026-10-29 (Yriarborg in region 100, Oisorvan in region 200)"), NonParallelizable]
public class UT_GroupFollowerStallProbe
{
    [TestCase((ushort)100, 715535f, 902535f, 5794f, 806197f, 701339f, 4963f)]
    [TestCase((ushort)200, 321186f, 603853f, 10243f, 297155f, 642943f, 4851f)]
    public void TheStandingSpotToTheRendezvous(ushort regionId, float x, float y, float z, float rx, float ry, float rz)
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        Assert.That(root, Is.Not.Null.And.Not.Empty, "Set OFFLINE_DAOC_NAV_ROOT to an install's runtime/server folder");
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                (name, asm, search) => name == "lib/Detour" ? NativeLibrary.Load(Path.Combine(root, "lib", "Detour.dll")) : IntPtr.Zero);
            Environment.CurrentDirectory = root;
            using var db = new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");
            db.Open();
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Region region = (Region)typeof(UT_AuditedDungeonInstalledMesh)
                .GetMethod("BuildRegion", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { db, (int)regionId, loaded });

            Vector3 from = new(x, y, z), rendezvous = new(rx, ry, rz);
            Zone zone = region.GetZone((int)from.X, (int)from.Y);
            Zone goalZone = region.GetZone((int)rendezvous.X, (int)rendezvous.Y);
            TestContext.WriteLine($"region {regionId}: stand zone={zone?.ID} {zone?.Description}  rendezvous zone={goalZone?.ID} {goalZone?.Description}");
            var snap = nav.GetClosestPoint(zone, from, 48, 48, 4096, nav.DefaultFilters);
            var snapSmall = nav.GetClosestPoint(zone, from, 48, 48, 96, nav.DefaultFilters);
            TestContext.WriteLine($"stand {from}: closest(tall)={snap} closest(96)={snapSmall}");

            Span<WrappedPathfindingNode> nodes = stackalloc WrappedPathfindingNode[512];
            foreach (var (label, target) in new (string, Vector3)[]
                     {
                         ("rendezvous", rendezvous),
                         ("1,300 toward the rendezvous", from + Vector3.Normalize(rendezvous - from) * 1300),
                         ("600 toward the rendezvous", from + Vector3.Normalize(rendezvous - from) * 600),
                         ("300 east", from + new Vector3(300, 0, 0)),
                         ("300 west", from + new Vector3(-300, 0, 0)),
                         ("300 north", from + new Vector3(0, 300, 0)),
                         ("300 south", from + new Vector3(0, -300, 0)),
                     })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var path = nav.GetPathStraight(zone, from, target, nav.DefaultFilters, nodes);
                watch.Stop();
                Vector3 last = path.NodeCount > 0 ? nodes[Math.Min(path.NodeCount, nodes.Length) - 1].Position : from;
                TestContext.WriteLine($"  -> {label}: status={path.Status} nodes={path.NodeCount} lastToGoal={Vector3.Distance(last, target):0} took={watch.Elapsed.TotalMilliseconds:0.0}ms");
            }
        }
        finally { foreach (var zoneLoaded in loaded) LocalPathfindingMgr.UnloadNavMesh(zoneLoaded); Environment.CurrentDirectory = previous; }
    }
}
