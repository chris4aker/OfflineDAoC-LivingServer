// DIAGNOSTIC ONLY (#704, Account #2): not for merge. Prints every eligible but unaudited spawn of the starter dungeons.
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Numerics;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, Explicit("diagnostic #704"), NonParallelizable]
    public class UT_Diag704StarterSpawns
    {
        [Test]
        public void PrintUnauditedStarterSpawns()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string previous = Environment.CurrentDirectory;
            var loaded = new List<Zone>();
            var report = new System.Text.StringBuilder();
            try
            {
                Environment.CurrentDirectory = root;
                string databasePath = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB_PATH") ??
                    Path.GetFullPath(Path.Combine(root, "..", "data", "opendaoc.sqlite3.db"));
                using var db = new SQLiteConnection($"Data Source={databasePath};Read Only=True;Pooling=False;");
                db.Open();
                var build = typeof(UT_StarterDungeonInstalledMesh).GetMethod("BuildRegion", BindingFlags.Static | BindingFlags.NonPublic);
                foreach ((int regionId, Vector3 entrance) in new[]
                {
                    (21, new Vector3(33150, 32732, 16480)), (129, new Vector3(34693, 33173, 16447)), (221, new Vector3(31120, 29939, 16239)),
                })
                {
                    var region = (Region)build.Invoke(null, new object[] { db, regionId, loaded });
                    Zone zone = region.GetZone((int)entrance.X, (int)entrance.Y);
                    IPathfindingMgr nav = PathfindingProvider.LocalPathfindingMgr;
                    using var command = db.CreateCommand();
                    command.CommandText = "select Mob_ID,Name,Level,ClassType,Realm,X,Y,Z from Mob where Region=@region";
                    command.Parameters.AddWithValue("@region", regionId);
                    using var reader = command.ExecuteReader();
                    int eligible = 0, missing = 0;
                    while (reader.Read())
                    {
                        string name = reader.GetString(1);
                        bool ok = !string.IsNullOrEmpty(name) && char.IsLower(name[0]) && reader.GetInt32(2) > 0 &&
                                  reader.GetString(3) == "DOL.GS.GameNPC" && reader.GetInt32(4) == 0;
                        if (!ok) continue;
                        eligible++;
                        string id = reader.GetString(0);
                        if (AutonomousDungeonGoalCatalog.HasVerifiedSpawn(id)) continue;
                        missing++;
                        Vector3 spawn = new(reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7));
                        Vector3? floor = nav.GetClosestPoint(zone, spawn, 64, 64, 256, nav.DefaultFilters);
                        bool there = floor.HasValue && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, entrance, floor.Value);
                        bool back = floor.HasValue && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor.Value, entrance);
                        report.AppendLine($"DIAG704 UNAUDITED region={regionId} id={id} name=\"{name}\" level={reader.GetInt32(2)} " +
                            $"spawn={spawn.X:0},{spawn.Y:0},{spawn.Z:0} floor={(floor.HasValue ? $"{floor.Value.X:0.0},{floor.Value.Y:0.0},{floor.Value.Z:0.0}" : "none")} " +
                            $"there={there} back={back} restored={AutonomousClassic165RestoredSpawnCatalog.Contains(id)}");
                    }
                    report.AppendLine($"DIAG704 REGION region={regionId} eligible={eligible} unaudited={missing}");
                }
            }
            finally
            {
                foreach (Zone zone in loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
            Assert.Fail("DIAGNOSTIC REPORT\n" + report);
        }
    }
}
