using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// Read-only probe of the six relic shrines on the installed navmesh (relic audit 2026-10-31, B1/L9): can an attacker
/// walk from outside the main gate onto the shrine floor once the gates are open, which doors lie on that walk, how far
/// the floor is from the relic (pickup needs 192, the eight on the pad 500), and how much floor there is within 500.
/// Run with OFFLINE_DAOC_NAV_ROOT=&lt;runtime\server&gt; (and OFFLINE_DAOC_DB_PATH for another database).
/// </summary>
[TestFixture, Explicit("Read-only installed relic shrine navmesh probe"), NonParallelizable]
public class UT_RelicShrineReach
{
    private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
    private sealed class ProbeDoor : GameDoorBase
    {
        public override int X { get; set; }
        public override int Y { get; set; }
        public override int Z { get; set; }
        public override Region CurrentRegion { get; set; }
        public override eDoorState State { get; set; } = eDoorState.Closed;
        public int Id;
        public bool Postern;
        public bool Registered;
    }

    [Test]
    public void EveryRelicShrineFloorIsReachedOnceItsGatesAreOpen()
    {
        string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        Assert.That(root, Is.Not.Null.And.Not.Empty, "set OFFLINE_DAOC_NAV_ROOT to runtime\\server");
        string previous = Environment.CurrentDirectory;
        var loaded = new List<Zone>();
        var previousServer = GameServer.Instance;
        var previousNav = PathfindingProvider.Instance;
        GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        var failures = new List<string>();
        try
        {
            NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(Path.Combine(root, "lib", "Detour.dll")) : IntPtr.Zero);
            Environment.CurrentDirectory = root;
            string databasePath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB_PATH");
            if (string.IsNullOrEmpty(databasePath))
            {
                databasePath = Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"));
            }
            using var db = new SQLiteConnection($"Data Source={databasePath};Read Only=True;Pooling=False;");
            db.Open();
            var nav = PathfindingProvider.LocalPathfindingMgr;
            PathfindingProvider.SetPathfindingMgr(nav);
            var regions = new Dictionary<int, Region>();
            var keeps = new List<(int Id, string Name, int Region, Vector3 Center)>();
            using (var q = db.CreateCommand())
            {
                q.CommandText = "select KeepID,Name,Region,X,Y,Z from Keep where SkinType=99 order by KeepID";
                using var r = q.ExecuteReader();
                while (r.Read()) keeps.Add((r.GetInt32(0), r.GetString(1), r.GetInt32(2), new(r.GetInt32(3), r.GetInt32(4), r.GetInt32(5))));
            }
            Assert.That(keeps, Has.Count.EqualTo(6));
            foreach (var keep in keeps)
            {
                if (!regions.TryGetValue(keep.Region, out Region region))
                    regions[keep.Region] = region = UT_AuditedDungeonInstalledMesh.BuildRegion(db, keep.Region, loaded);
                Vector3 pad = default;
                using (var q = db.CreateCommand())
                {
                    q.CommandText = "select X,Y,Z from WorldObject where ClassType like '%GameRelicPad' and Region=@r and abs(X-@x)<3000 and abs(Y-@y)<3000";
                    q.Parameters.AddWithValue("@r", keep.Region); q.Parameters.AddWithValue("@x", keep.Center.X); q.Parameters.AddWithValue("@y", keep.Center.Y);
                    using var r = q.ExecuteReader();
                    if (r.Read()) pad = new(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2));
                }
                var doors = new List<ProbeDoor>();
                using (var q = db.CreateCommand())
                {
                    q.CommandText = "select InternalID,X,Y,Z,IsPostern from Door where abs(X-@x)<2500 and abs(Y-@y)<2500";
                    q.Parameters.AddWithValue("@x", keep.Center.X); q.Parameters.AddWithValue("@y", keep.Center.Y);
                    using var r = q.ExecuteReader();
                    while (r.Read())
                    {
                        var door = (ProbeDoor)RuntimeHelpers.GetUninitializedObject(typeof(ProbeDoor));
                        door.CurrentRegion = region; door.Id = r.GetInt32(0);
                        door.X = r.GetInt32(1); door.Y = r.GetInt32(2); door.Z = r.GetInt32(3); door.Postern = r.GetInt32(4) != 0;
                        door.State = eDoorState.Open;
                        door.Registered = nav.RegisterDoor(door);
                        doors.Add(door);
                    }
                }
                Zone zone = region.GetZone((int)keep.Center.X, (int)keep.Center.Y);
                ProbeDoor gate = doors.Where(d => !d.Postern).OrderByDescending(d => Vector2.DistanceSquared(new(d.X, d.Y), new(keep.Center.X, keep.Center.Y))).First();
                Vector2 outward = Vector2.Normalize(new Vector2(gate.X - keep.Center.X, gate.Y - keep.Center.Y));
                Vector3 rawOutside = new(gate.X + outward.X * 900, gate.Y + outward.Y * 900, gate.Z);
                Vector3? outside = nav.GetClosestPoint(region.GetZone((int)rawOutside.X, (int)rawOutside.Y), rawOutside, 128, 128, 1024, nav.DefaultFilters);
                Vector3? padFloor = AutonomousRelicShrine.ShrineFloor(nav, zone, pad);
                TestContext.WriteLine($"{keep.Name} (rvr-keep-{keep.Id}) pad={pad} outside={outside} padFloor={padFloor} " +
                    $"pickup3d={(padFloor.HasValue ? Vector3.Distance(padFloor.Value, pad) : -1):0} dz={(padFloor.HasValue ? pad.Z - padFloor.Value.Z : 0):0}");
                TestContext.WriteLine("  doors: " + string.Join(" ", doors.OrderBy(d => d.Id).Select(d => $"{d.Id % 100:00}{(d.Postern ? "p" : "")}{(d.Registered ? "" : "!UNREGISTERED")}")));
                if (!outside.HasValue || !padFloor.HasValue)
                {
                    failures.Add($"{keep.Name}: no floor (outside={outside} pad={padFloor})");
                    continue;
                }
                foreach (var d in doors) { d.State = eDoorState.Open; nav.UpdateDoorFlags(d); }
                var attacker = new AutonomousKeepApproachNavigation(nav, []);
                bool open = AutonomousZoneItinerary.HasCompleteCorridor(attacker, zone, outside.Value, padFloor.Value);
                TestContext.WriteLine($"  all doors open: outside->shrine={(open ? "PASS" : "FAIL")}");
                if (!open) failures.Add($"{keep.Name}: shrine floor not reached with every door open");
                var needed = new List<string>();
                foreach (var door in doors.Where(d => d.Registered))
                {
                    door.State = eDoorState.Closed; nav.UpdateDoorFlags(door);
                    if (!AutonomousZoneItinerary.HasCompleteCorridor(attacker, zone, outside.Value, padFloor.Value)) needed.Add($"{door.Id % 100:00}");
                    door.State = eDoorState.Open; nav.UpdateDoorFlags(door);
                }
                TestContext.WriteLine($"  registered doors on the walk (closing one alone blocks it): {string.Join(",", needed)}");
                var nodes = new WrappedPathfindingNode[512];
                var path = nav.GetPathStraight(zone, outside.Value, padFloor.Value, nav.DefaultFilters, nodes);
                var walk = new List<Vector3> { outside.Value };
                for (int i = 0; i < path.NodeCount; i++) walk.Add(nodes[i].Position);
                foreach (var door in doors.Where(d => !d.Registered))
                    TestContext.WriteLine($"  unregistered door {door.Id % 100:00} at {door.X},{door.Y},{door.Z}: on the open walk={AutonomousRelicShrine.WalkPassesDoor(walk, new(door.X, door.Y, door.Z))}");
                // The planner checks the unregistered relic gate itself (AutonomousKeepApproachNavigation, DoorMgr.UnregisteredInNavmesh).
                var gateAware = new AutonomousKeepApproachNavigation(nav, [], doors.Where(d => !d.Registered && !d.Postern).Select(d => new Vector3(d.X, d.Y, d.Z)).ToArray());
                bool throughShutRelicGate = AutonomousZoneItinerary.HasCompleteCorridor(gateAware, zone, outside.Value, padFloor.Value);
                TestContext.WriteLine($"  every registered door open, the relic gate shut: outside->shrine={(throughShutRelicGate ? "OPEN (gate ignored)" : "blocked")}");
                if (throughShutRelicGate) failures.Add($"{keep.Name}: the shut relic gate does not block the planned walk");
                foreach (var d in doors) { d.State = eDoorState.Closed; nav.UpdateDoorFlags(d); }
                bool closed = AutonomousZoneItinerary.HasCompleteCorridor(attacker, zone, outside.Value, padFloor.Value);
                TestContext.WriteLine($"  all doors closed: outside->shrine={(closed ? "OPEN (doors do not block)" : "blocked")}");
                int standing = 0;
                for (int dx = -400; dx <= 400; dx += 100)
                for (int dy = -400; dy <= 400; dy += 100)
                {
                    Vector3? spot = nav.GetClosestPoint(zone, new(pad.X + dx, pad.Y + dy, padFloor.Value.Z), 32, 32, 96, nav.DefaultFilters);
                    if (spot.HasValue && Vector3.Distance(spot.Value, pad) <= 480 &&
                        AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, padFloor.Value, spot.Value)) standing++;
                }
                TestContext.WriteLine($"  connected floor spots within 480 of the relic (100-unit grid): {standing}");
                if (standing < 8) failures.Add($"{keep.Name}: only {standing} floor spots within 480 of the relic");
                if (Vector3.Distance(padFloor.Value, pad) > WorldMgr.INTERACT_DISTANCE)
                    failures.Add($"{keep.Name}: shrine floor {Vector3.Distance(padFloor.Value, pad):0} from the relic (pickup needs {WorldMgr.INTERACT_DISTANCE})");
            }
            Assert.That(failures, Is.Empty, string.Join(Environment.NewLine, failures));
        }
        finally
        {
            foreach (var zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            Environment.CurrentDirectory = previous;
            GameServer.LoadTestDouble(previousServer);
            PathfindingProvider.SetPathfindingMgr(previousNav);
        }
    }
}
