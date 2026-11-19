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
    /// Tool (1.2, the epic "wow" pass): checks every fixed landing spot of the encounter throws, teleports and jails against
    /// the installed navmeshes. For each spot: is there walkable floor under it (or within 600, EncounterDisplacement.TryLanding),
    /// and a complete navmesh corridor from that floor to the boss's spawn (the walk back a bot makes)? A spot that fails gets
    /// the bots' fallback at run time (EncounterDisplacementRecovery: the short recall), so nothing is stuck; this lists them.
    /// Read only. Env: OFFLINE_DAOC_NAV_ROOT (a runtime/server folder with navmesh/ and lib/Detour.dll), OFFLINE_DAOC_DB (the
    /// world database), OFFLINE_DAOC_PROOF_OUT (report file). Random spots (Glacier Giant, Mortufoghus) and spots relative to the
    /// boss (Krevo, Hakr, Hurika, Dra'argus) are checked at run time only (EPIC_DISPLACEMENT_OFF_MESH in the server log).
    /// </summary>
    [TestFixture, Explicit("Navmesh landing-spot audit tool"), NonParallelizable]
    public class UT_DisplacementLandingAudit
    {
        public readonly record struct LandingSpot(string Encounter, string Boss, ushort Region, int X, int Y, int Z, string Kind);

        /// <summary>Every fixed spot in scripts/namedmobs (EncounterDisplacement.Move) and the dragons' throw.</summary>
        public static readonly LandingSpot[] Spots =
        [
            new("LegendaryAfanc", "Legendary Afanc", 1, 451486, 393503, 2754, "Teleport"),
            new("Olcasgean", "Olcasgean", 191, 38399, 60893, 12242, "Throw"),
            new("Olcasgean", "Olcasgean", 191, 38564, 64161, 12242, "Throw"),
            new("Olcasgean", "Olcasgean", 191, 41580, 62325, 12242, "Throw"),
            new("WaterPrimal", "Olcasgean", 191, 38626, 60891, 11771, "Teleport"),
            new("WaterPrimal", "Olcasgean", 191, 40606, 60868, 11721, "Teleport"),
            new("WaterfallAntipass", "Olcasgean", 191, 39664, 60792, 11542, "Barrier"),
            new("Easmarach", "Easmarach", 191, 40067, 50494, 11708, "Barrier"),
            new("SpindlerBroodmother", "Spindler Broodmother", 191, 21115, 53483, 11286, "Teleport"),
            new("MoranTheMighty", "Moran the Mighty", 1, 401943, 753091, 222, "Throw"),
            new("MoranTheMighty", "Moran the Mighty", 1, 406787, 749150, 213, "Throw"),
            new("MoranTheMighty", "Moran the Mighty", 1, 401061, 755882, 469, "Throw"),
            new("Iarnvidiur", "Iarnvidiur", 161, 12563, 13450, 18537, "Teleport"),
            new("Iarnvidiur", "Iarnvidiur", 161, 15212, 13331, 18537, "Teleport"),
            new("Iarnvidiur", "Iarnvidiur", 161, 16958, 13269, 18537, "Teleport"),
            new("Iarnvidiur", "Iarnvidiur", 161, 11829, 13530, 18537, "Teleport"),
            new("Orylle", "Orylle", 61, 31406, 69599, 15605, "Throw"),
            new("LyftMiht", "Scurceol Hyrde", 61, 50986, 20031, 16964, "Throw"),
            new("LyftMiht", "Scurceol Hyrde", 61, 51936, 21012, 16964, "Throw"),
            new("LyftMiht", "Scurceol Hyrde", 61, 52784, 20019, 16964, "Throw"),
            new("LyftMiht", "Scurceol Hyrde", 61, 51940, 18968, 16964, "Throw"),
            new("Horsemen", "Apocalypse", 60, 29469, 25244, 19490, "Barrier"),
            new("BaneOfHope", "Bane of Hope", 60, 34496, 30879, 14551, "Teleport"),
            new("BaneOfHope", "Bane of Hope", 60, 37377, 30154, 13973, "Teleport"),
            new("BaneOfHope", "Bane of Hope", 60, 38292, 31794, 13940, "Teleport"),
            new("Boligar", "Boligar", 151, 409989, 378960, 7752, "Teleport"),
            new("Boligar", "Boligar", 151, 411003, 377902, 7704, "Teleport"),
            new("Boligar", "Boligar", 151, 409983, 376894, 7376, "Teleport"),
            new("Boligar", "Boligar", 151, 408968, 377920, 7440, "Teleport"),
            new("Ydenia", "Ydenia of the Seithkona", 100, 664713, 896689, 1553, "Throw"),
            new("Ydenia", "Ydenia of the Seithkona", 100, 667220, 894261, 1543, "Throw"),
            new("Ydenia", "Ydenia of the Seithkona", 100, 665968, 892792, 1561, "Throw"),
            new("Ydenia", "Ydenia of the Seithkona", 100, 663895, 893446, 1554, "Throw"),
            new("SummonerCunovinda", "Summoner Cunovinda", 248, 24874, 36116, 17060, "Teleport"),
            new("GrandSummonerGovannon", "Grand Summoner Govannon", 248, 32091, 39684, 16302, "Teleport"),
            new("Krackenschtein", "Krackenschtein", 180, 32956, 37669, 16465, "Throw"),
            new("Krackenschtein", "Krackenschtein", 180, 31879, 38149, 16465, "Throw"),
            new("Krackenschtein", "Krackenschtein", 180, 31727, 37401, 16465, "Throw"),
            new("Krackenschtein", "Krackenschtein", 180, 32159, 36387, 16465, "Throw"),
            new("Anurigunda", "Anurigunda", 180, 30652, 33089, 16124, "Barrier"),
            new("SyssroTheRuthless", "Sys'sro the Ruthless", 50, 41489, 40699, 8145, "Throw"),
            new("Xanxicar", "Xanxicar", 62, 32338, 32387, 16539, "Throw"),
            new("Jailer", "Jailer Vifil", 160, 16631, 58683, 10858, "Jail"),
            new("QueenKula", "Queen Kula", 160, 34128, 56095, 11898, "Teleport"),
            new("Legion", "Legion", 249, 48200, 49566, 20833, "Teleport"),
            .. DragonSpots("AlbGolestandt", "Golestandt", 1),
            .. DragonSpots("MidGjalpinulva", "Gjalpinulva", 100),
            .. DragonSpots("HibCuuldurach", "Cuuldurach the Glimmer King", 200),
        ];

        private static IEnumerable<LandingSpot> DragonSpots(string type, string boss, ushort region) =>
            DragonEncounterThrow.Spots[type].Select(s => new LandingSpot(type, boss, region, s.X, s.Y, s.Z, "Throw"));

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
            var lines = new List<string> { "encounter\tkind\tregion\tspot\tresult\tlanding\tdetail" };
            try
            {
                foreach (LandingSpot s in Spots)
                {
                    var z = zones.FirstOrDefault(q => q.Region == s.Region && s.X >= q.X0 && s.X < q.X0 + q.W && s.Y >= q.Y0 && s.Y < q.Y0 + q.H);
                    string spot = $"{s.X},{s.Y},{s.Z}";
                    if (z.Name == null) { lines.Add($"{s.Encounter}\t{s.Kind}\t{s.Region}\t{spot}\tNO_ZONE\t\t"); continue; }
                    if (!loaded.TryGetValue(z.Id, out Zone zone))
                    {
                        zone = new Zone(null, z.Id, z.Name, z.X0, z.Y0, z.W, z.H, z.Id, false, 0, false, 0, 0, 0, 0, 0);
                        LocalPathfindingMgr.LoadNavMesh(zone);
                        loaded[z.Id] = zone;
                    }
                    if (!nav.HasNavmesh(zone)) { lines.Add($"{s.Encounter}\t{s.Kind}\t{s.Region}\t{spot}\tNO_NAVMESH\t\tzone {z.Id}"); continue; }
                    if (!EncounterDisplacement.TryLanding(nav, zone, new Vector3(s.X, s.Y, s.Z), out Vector3 landing))
                    {
                        lines.Add($"{s.Encounter}\t{s.Kind}\t{s.Region}\t{spot}\tOFF_MESH\t\tno floor under it or within 600: the bot is recalled");
                        continue;
                    }
                    string land = $"{(int)landing.X},{(int)landing.Y},{(int)landing.Z}";
                    int fall = (int)(s.Z - landing.Z);
                    Vector3? boss = Spawn(s.Boss, s.Region);
                    if (boss == null) { lines.Add($"{s.Encounter}\t{s.Kind}\t{s.Region}\t{spot}\tFLOOR\t{land}\tfall {fall}; boss \"{s.Boss}\" not in the world database"); continue; }
                    Vector3? bossFloor = nav.GetClosestPoint(zone, boss.Value, 96, 96, 256, nav.DefaultFilters);
                    bool path = bossFloor.HasValue && AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, landing, bossFloor.Value);
                    lines.Add($"{s.Encounter}\t{s.Kind}\t{s.Region}\t{spot}\t{(path ? "REACHABLE" : "NO_PATH")}\t{land}\t" +
                              $"fall {fall} ({EncounterDisplacement.FallDamagePercent(Math.Max(0, fall), 0)}%); " +
                              (path ? "walks back to the boss" : "no corridor to the boss: the bot is recalled"));
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
