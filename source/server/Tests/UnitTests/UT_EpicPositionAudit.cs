using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Tool (1.2 play test): the fixed positions the epic encounters move raiders to, or send them to, checked against the
    /// installed navmeshes in BOTH directions. <see cref="UT_DisplacementLandingAudit"/> asks "is there floor and a way to the
    /// boss"; this also asks "is there a way back" (a ledge or a fall is one way: Detour has a path down, not up), and adds the
    /// tactic positions: Easmarach's six waterfall stands, the Sacristan's eleven waiting points, the Vifil tunnel, Kula's
    /// frost circle, and the Apocalypse room (how far the floor reaches from its middle in each direction, against the
    /// tactic's 800 and the door at 1,157).
    /// Read only. Env: OFFLINE_DAOC_NAV_ROOT (a runtime/server folder with navmesh/ and the Detour library under lib/),
    /// OFFLINE_DAOC_DB (the world database), OFFLINE_DAOC_PROOF_OUT (report file, tab separated).
    /// </summary>
    [TestFixture, Explicit("Navmesh position audit tool (play test)"), NonParallelizable]
    public class UT_EpicPositionAudit
    {
        public readonly record struct Probe(string Encounter, string What, ushort Region, Vector3 Spot, string Anchor, Vector3? AnchorAt = null);

        private static IEnumerable<Probe> Probes()
        {
            foreach (var s in UT_DisplacementLandingAudit.Spots)
                yield return new(s.Encounter, s.Kind, s.Region, new(s.X, s.Y, s.Z), s.Boss);
            int i = 0;
            foreach (Vector3 stand in EpicEncounterTactics.EasmarachFallsStands)
                yield return new("Easmarach", $"FallsStand{++i}", 191, stand, "Easmarach", EpicEncounterTactics.EasmarachSpawn);
            yield return new("Easmarach", "FallsTop", 191, EpicEncounterTactics.EasmarachFallsTop, "Easmarach", EpicEncounterTactics.EasmarachSpawn);
            i = 0;
            foreach (Vector3 station in EpicEncounterTactics.SacristanStations)
                yield return new("SkeletalSacristan", $"Station{++i}", 60, station, "Skeletal Sacristan", EpicEncounterTactics.SacristanPatrol[0]);
            yield return new("Jailer", "RescueSpot", 160, EpicEncounterTactics.VifilGuardSpot, "Jailer Vifil");
            yield return new("Jailer", "JailToRescueSpot", 160, EpicEncounterTactics.VifilJail, "rescue spot", EpicEncounterTactics.VifilGuardSpot);
            yield return new("QueenKula", "FrostCircle", 160, EpicEncounterTactics.KulaFrostPoint, "Queen Kula");
            var room = EpicEncounterTactics.HorsemenRoom;
            yield return new("Horsemen", "Door", 60, room.Door, "room middle", room.Middle);
            // Caer Sidi's way up (play test 2026-11-07: a raid on the lower floor at z 15,100 never reached the horsemen's room).
            yield return new("Horsemen", "FromEntrance", 60, new(31666, 35982, 18639), "room middle", room.Middle);
            yield return new("Horsemen", "FromLowerFloor", 60, new(25762, 31265, 15119), "room middle", room.Middle);
            yield return new("Horsemen", "FromReckonerRoom", 60, EpicEncounterTactics.SoulReckonerRoom, "room middle", room.Middle);
        }

        [Test]
        public void Audit()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string dbPath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB");
            string output = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROOF_OUT");
            Assume.That(root != null && dbPath != null && output != null, "set OFFLINE_DAOC_NAV_ROOT, OFFLINE_DAOC_DB, OFFLINE_DAOC_PROOF_OUT");
            using var db = new SQLiteConnection($"Data Source={dbPath};Read Only=True;Pooling=False");
            db.Open();
            var zones = new List<(ushort Id, string Name, ushort Region, int X0, int Y0, int W, int H)>();
            using (var cmd = new SQLiteCommand("select ZoneID,Name,RegionID,OffsetX,OffsetY,Width,Height from Zones", db))
            using (var r = cmd.ExecuteReader())
                while (r.Read())
                    zones.Add(((ushort)r.GetInt32(0), r.GetString(1), (ushort)r.GetInt32(2), r.GetInt32(3) * 8192, r.GetInt32(4) * 8192,
                        r.GetInt32(5) * 8192, r.GetInt32(6) * 8192));
            Vector3? Spawn(string name, ushort region)
            {
                using var cmd = new SQLiteCommand("select X,Y,Z from Mob where Name=@n and Region=@r limit 1", db);
                cmd.Parameters.AddWithValue("@n", name);
                cmd.Parameters.AddWithValue("@r", region);
                using var r = cmd.ExecuteReader();
                return r.Read() ? new Vector3(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2)) : null;
            }

            string previous = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var loaded = new Dictionary<ushort, Zone>();
            var lines = new List<string> { "encounter\twhat\tregion\tspot\tfloor\tto_anchor\tback\tdetail" };
            Zone ZoneOf(ushort region, Vector3 p, out string name)
            {
                var z = zones.FirstOrDefault(q => q.Region == region && p.X >= q.X0 && p.X < q.X0 + q.W && p.Y >= q.Y0 && p.Y < q.Y0 + q.H);
                name = z.Name;
                if (z.Name == null) return null;
                if (!loaded.TryGetValue(z.Id, out Zone zone))
                {
                    zone = new Zone(null, z.Id, z.Name, z.X0, z.Y0, z.W, z.H, z.Id, false, 0, false, 0, 0, 0, 0, 0);
                    LocalPathfindingMgr.LoadNavMesh(zone);
                    loaded[z.Id] = zone;
                }
                return zone;
            }
            static string P(Vector3 v) => $"{(int)v.X},{(int)v.Y},{(int)v.Z}";
            try
            {
                foreach (Probe p in Probes())
                {
                    string head = $"{p.Encounter}\t{p.What}\t{p.Region}\t{P(p.Spot)}";
                    Zone zone = ZoneOf(p.Region, p.Spot, out string zoneName);
                    if (zone == null) { lines.Add($"{head}\tNO_ZONE\t\t\t"); continue; }
                    if (!nav.HasNavmesh(zone)) { lines.Add($"{head}\tNO_NAVMESH\t\t\tzone {zone.ID} {zoneName}"); continue; }
                    if (!EncounterDisplacement.TryLanding(nav, zone, p.Spot, out Vector3 landing))
                    { lines.Add($"{head}\tOFF_MESH\t\t\tno floor under it or within 600"); continue; }
                    Vector3? anchor = p.AnchorAt ?? Spawn(p.Anchor, p.Region);
                    if (anchor == null) { lines.Add($"{head}\tFLOOR {P(landing)}\t\t\tanchor \"{p.Anchor}\" not in the world database"); continue; }
                    Vector3? anchorFloor = nav.GetClosestPoint(zone, anchor.Value, 96, 96, 256, nav.DefaultFilters);
                    if (anchorFloor == null) { lines.Add($"{head}\tFLOOR {P(landing)}\t\t\tanchor {p.Anchor} {P(anchor.Value)} has no floor"); continue; }
                    bool to = AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, landing, anchorFloor.Value);
                    bool back = AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, anchorFloor.Value, landing);
                    float off = Vector2.Distance(new(landing.X, landing.Y), new(p.Spot.X, p.Spot.Y));
                    lines.Add($"{head}\tFLOOR {P(landing)}\t{(to ? "PATH" : "NO_PATH")}\t{(back ? "PATH" : "NO_PATH")}\t" +
                              $"anchor {p.Anchor} {P(anchorFloor.Value)}; floor {off:0} flat and {(int)(p.Spot.Z - landing.Z)} below the spot; " +
                              $"{Vector3.Distance(landing, anchorFloor.Value):0} from the anchor");
                }

                // The Apocalypse room: from its middle, how far the floor reaches in 24 directions (stepping 50 units, a step
                // counts when the floor under it is within 40 flat and 200 down, and a corridor from the middle reaches it).
                var room = EpicEncounterTactics.HorsemenRoom;
                Zone caerSidi = ZoneOf(60, room.Middle, out _);
                if (caerSidi != null && nav.HasNavmesh(caerSidi))
                {
                    Vector3 middle = nav.GetClosestPoint(caerSidi, room.Middle, 96, 96, 256, nav.DefaultFilters) ?? room.Middle;
                    var reach = new List<string>();
                    int inside800 = 0;
                    for (int d = 0; d < 24; d++)
                    {
                        double a = d * Math.PI * 2 / 24;
                        int last = 0;
                        for (int r = 50; r <= 2000; r += 50)
                        {
                            var probe = new Vector3(middle.X + (float)(Math.Cos(a) * r), middle.Y + (float)(Math.Sin(a) * r), middle.Z + 100);
                            Vector3? floor = nav.GetClosestPoint(caerSidi, probe, 40, 40, 300, nav.DefaultFilters);
                            if (floor == null || Vector2.Distance(new(floor.Value.X, floor.Value.Y), new(probe.X, probe.Y)) > 40 ||
                                Math.Abs(floor.Value.Z - middle.Z) > 300 ||
                                !AutonomousZoneItinerary.HasCompleteCorridor(nav, caerSidi, middle, floor.Value)) break;
                            last = r;
                        }
                        if (last >= room.InsideRadius) inside800++;
                        reach.Add($"{d * 15}deg={last}");
                    }
                    lines.Add($"Horsemen\tRoomReach\t60\t{P(middle)}\t{inside800}/24 directions reach {room.InsideRadius}\t\t\t{string.Join(' ', reach)}");
                }
            }
            finally
            {
                foreach (Zone zone in loaded.Values) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            File.WriteAllLines(output, lines);
            TestContext.Out.WriteLine(string.Join(Environment.NewLine, lines));
        }
    }
}
