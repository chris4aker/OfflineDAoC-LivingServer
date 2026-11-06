using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>The other raid dungeons (2026-10-20): every one has a hub, a final room and its scripted final bosses.</summary>
    [TestFixture]
    public class UT_RaidDungeons
    {
        [Test]
        public void EveryRaidDungeonIsScheduledWithAHubAndAFinalRoom() => Assert.Multiple(() =>
        {
            foreach (var dungeon in AutonomousRealmRaid.RaidDungeons)
            {
                var definition = AutonomousRealmRaid.Definitions.Single(d => d.Id == dungeon.Id);
                Assert.That(definition.IsDungeon, Is.True, dungeon.Name);
                Assert.That(definition.IsNeutral, Is.False, dungeon.Name);
                Assert.That(definition.Trigger, Is.EqualTo(dungeon.Final), dungeon.Name);
                Assert.That(definition.FinalTypes, Does.Contain(dungeon.BossType), dungeon.Name);
                Assert.That(RealmRaidMuster.Hubs.Where(h => h.Event == dungeon.Id), Is.Not.Empty, $"{dungeon.Name} has a muster hub");
                string realm = dungeon.Realm switch { eRealm.Albion => "albion", eRealm.Midgard => "midgard", _ => "hibernia" };
                Assert.That(dungeon.Id, Does.StartWith($"dungeon-{realm}-"));
                Assert.That(AutonomousRealmRaid.SlowRespawnRegions, Does.Contain(dungeon.Region), dungeon.Name);
                foreach (string type in dungeon.Finals)
                    Assert.That(typeof(GameServer).Assembly.GetTypes().Any(t => t.Name == type && typeof(GameNPC).IsAssignableFrom(t)),
                        Is.True, $"{dungeon.Name}: boss script {type}");
            }
            Assert.That(AutonomousRealmRaid.Definitions.Select(d => d.Id), Is.Unique);
            Assert.That(AutonomousRealmRaid.RaidDungeons.Select(d => d.Region), Is.EquivalentTo(new ushort[] { 61, 50, 62, 277, 150, 161, 246, 190, 276 }));
            var crystal = AutonomousRealmRaid.Definitions.Single(d => d.Id == "dungeon-albion-crystal");
            var hub = RealmRaidMuster.Hubs.First(h => h.Event == crystal.Id);
            Assert.That(crystal.Through, Is.EqualTo(50), "Crystal Cave is entered through Avalon City");
            Assert.That(crystal.EntranceHome(hub), Is.EqualTo(51), "it stages outside Avalon City, in the Shrouded Isles");
            var krondon = AutonomousRealmRaid.Definitions.Single(d => d.Id == "dungeon-albion-krondon");
            Assert.That(krondon.EntranceHome(RealmRaidMuster.Hubs.First(h => h.Event == krondon.Id)), Is.EqualTo(0), "Krondon is entered from open ground");
        });

        [Test]
        public void TheEpicDungeonsKeepTheirOwnFinals() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousRealmRaid.Definitions.Single(d => d.Id == "epic-midgard").FinalTypes, Is.EqualTo(new[] { "KingTuscar", "QueenKula" }));
            Assert.That(AutonomousRealmRaid.Definitions.Single(d => d.Id == "darkness-albion").Trigger, Is.EqualTo(RealmRaidNeutralEvents.FinalApproach(249)));
            Assert.That(AutonomousRealmRaid.Definitions.Single(d => d.Id == "dragon-albion").IsDungeon, Is.False);
        });
    }

    /// <summary>
    /// A raid dungeon's route must prove a path from its entrance to its final room (RealmRaidDungeonRoute.TryCreate
    /// does the same at raid start). Needs OFFLINE_DAOC_TEST_DETOUR and OFFLINE_DAOC_NAV_ROOT (with navmesh/zoneNNN.nav).
    /// </summary>
    [TestFixture, NonParallelizable, Explicit("Needs the native Detour library and installed navmeshes")]
    public class UT_RaidDungeonRoutesProbe
    {
        private string _prior;
        private readonly List<Zone> _loaded = new();

        [SetUp]
        public void Load()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            string native = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TEST_DETOUR");
            Assert.That(root, Is.Not.Null.And.Not.Empty);
            Assert.That(native, Is.Not.Null.And.Not.Empty);
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
            }
            catch (InvalidOperationException) { }
            _prior = Environment.CurrentDirectory;
            Environment.CurrentDirectory = root;
        }

        [TearDown]
        public void Unload()
        {
            foreach (Zone zone in _loaded) LocalPathfindingMgr.UnloadNavMesh(zone);
            _loaded.Clear();
            Environment.CurrentDirectory = _prior;
        }

        // region, the entrance's arrival point inside (its zone point's target)
        [TestCase(61, 33013, 31379, 15698)]     // Krondon
        [TestCase(50, 32366, 21177, 8071)]      // Avalon City (Albion-only entrance)
        [TestCase(50, 31159, 47380, 8307)]      // Avalon City
        [TestCase(62, 28621, 31142, 16418)]     // Crystal Cave, from its entrance out of Avalon City
        [TestCase(277, 32395, 31847, 16158)]    // Hall of the Corrupt
        [TestCase(150, 28284, 47911, 16000)]    // Trollheim
        [TestCase(161, 34991, 37707, 17231)]    // Iarnvidiur's Lair
        [TestCase(246, 32259, 30624, 16045)]    // Dodens Gruva
        [TestCase(190, 38038, 27675, 13247)]    // Tur Suil
        [TestCase(276, 32915, 29238, 16268)]    // Marfach Caverns
        [TestCase(191, 32072, 29524, 17051)]    // control: Galladoria
        [TestCase(160, 35612, 17899, 19050)]    // control: Tuscaran Glacier
        public void TheFinalRoomIsReachableFromTheEntrance(int regionId, int x, int y, int z) =>
            Reach(regionId, new(x, y, z), AutonomousRealmRaid.Definitions.First(d => d.Region == regionId).Trigger);

        // The Crystal Cave raid walks through Avalon City: from each entrance to the Crystal Cave portal.
        [TestCase(32366, 21177, 8071)]
        [TestCase(31159, 47380, 8307)]
        public void AvalonCityLeadsToTheCrystalCavePortal(int x, int y, int z) => Reach(50, new(x, y, z), new(21094, 42184, 7909));

        // Krondon, 2026-10-21: by its portal in Aldland only 7 slots in a row had a staging post, and the rally failed.
        [Test]
        public void KrondonsPortalHasRoomForItsRaid()
        {
            var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Region).GetField("m_regionData", flags).SetValue(region, new RegionData { Id = 51 });
            var zones = new List<Zone>();
            typeof(Region).GetField("m_zones", flags).SetValue(region, zones);
            var zone = new Zone(region, 56, "Aldland", 44 * 8192, 58 * 8192, 65536, 65536, 56, false, 0, false, 0, 0, 0, 0, 0);
            zones.Add(zone);
            _loaded.Add(zone);
            LocalPathfindingMgr.LoadNavMesh(zone);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Assert.That(nav.HasNavmesh(zone), Is.True, "zone056.nav loaded");
            var posts = new List<Vector3>();
            for (int slot = 0; slot < RealmRaidRecruitmentPolicy.MaximumParties; slot++)
                if (RealmRaidStaging.TryDungeonPost(nav, zone, new(386927, 479721, 3664), slot, posts, out Vector3 post))
                    posts.Add(post);
            TestContext.Out.WriteLine($"Krondon staging posts: {posts.Count}");
            // Krondon raids up to 160: twenty eight-bot parties.
            Assert.That(posts.Count, Is.GreaterThanOrEqualTo(20));
        }

        // Trollheim, 2026-10-22: every staging post by its portal in Gripklosa Mountains lay on a shelf beside it that joins
        // neither the portal nor Bjarken, so the raid was never scheduled ("no corridor inside zone 152 to the staging post").
        [Test]
        public void TrollheimsPostsAreOnTheGroundFromBjarken()
        {
            Zone zone = LoadZone(151, 152, 34 * 8192, 30 * 8192);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 bjarken = nav.GetClosestPoint(zone, new(290227, 301820, 4160), 64, 64, 64, nav.DefaultFilters).Value;
            var posts = new List<Vector3>();
            for (int slot = 0; slot < RealmRaidRecruitmentPolicy.MaximumParties; slot++)
                if (RealmRaidStaging.TryDungeonPost(nav, zone, new(304016, 277289, 6990), slot, posts, out Vector3 post))
                    posts.Add(post);
            TestContext.Out.WriteLine($"Trollheim staging posts: {posts.Count}");
            Assert.That(posts.Count, Is.GreaterThanOrEqualTo(20), "Trollheim raids up to 160: twenty parties");
            foreach (Vector3 post in posts)
                Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, bjarken, post), Is.True, $"Bjarken to the post at {post}");
        }

        // Hall of the Corrupt has two final bosses. Live run 2026-10-22: the raid killed Aidon, then stood above Lord Gildas's
        // room for 37 minutes and ended "Route blocked": the ramp up from his room meets the main floor at one 32-unit step,
        // higher than the mesh's walkable climb. Walk links over that step (tools/navmesh_splice/add_offmesh.py).
        [TestCase(32395, 31847, 16158)]   // the entrance
        [TestCase(31671, 35027, 15368)]   // where the raid stood when it gave up
        [TestCase(31353, 37634, 14873)]   // Aidon's room
        public void LordGildasIsReachable(int x, int y, int z)
        {
            Zone zone = LoadZone(277);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 from = nav.GetClosestPoint(zone, new(x, y, z), 64, 64, 64, nav.DefaultFilters).Value;
            Vector3 gildas = nav.GetClosestPoint(zone, new(29015, 41910, 12933), 64, 64, 64, nav.DefaultFilters).Value;
            Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, from, gildas), Is.True, "to Lord Gildas");
            Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, gildas, from), Is.True, "and back");
        }

        // Marfach Caverns, 2026-11-05: the raid turned on the Ancient Black Oak (spawned by Black Lady's script at
        // 30091,37620,15049) and used that point as its front. For 3.5 hours every party's camp stayed at the muster, 1,600-2,200
        // units away: no formation post resolved around the Oak (RealmRaidFormation needs sight and a corridor from the
        // centre). The switch now fights from an approach proven from the raid (TryEncounterApproach). This probe shows both
        // on the installed zone276.nav; it was not run when the fix was written (no navmeshes in the build container).
        [Test]
        public void TheBlackOakIsFoughtFromAnApproach()
        {
            Zone zone = LoadZone(276);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            var oak = new Vector3(30091, 37620, 15049);
            Vector3 muster = nav.GetClosestPoint(zone, new(31012, 35916, 14994), 64, 64, 64, nav.DefaultFilters).Value;
            int Posts(Vector3 centre)
            {
                var posts = new List<Vector3>();
                for (int slot = 0; slot < 10; slot++)
                    if (RealmRaidFormation.TryResolve(nav, zone, centre, slot, posts, out Vector3 post)) posts.Add(post);
                return posts.Count;
            }
            TestContext.Out.WriteLine($"floor at the Oak: {nav.GetClosestPoint(zone, oak, 64, 64, 128, nav.DefaultFilters)?.ToString() ?? "none"}");
            TestContext.Out.WriteLine($"posts around the Oak's own point: {Posts(oak)} of 10");
            Assert.That(AutonomousZonePointApproach.TryResolve(nav, zone, muster, oak, 220, out Vector3 approach), Is.True, "an approach from the muster");
            int around = Posts(approach);
            TestContext.Out.WriteLine($"approach {approach}: posts {around} of 10");
            Assert.That(around, Is.GreaterThanOrEqualTo(8), "a 64-80 raid's parties get posts at the Oak");
        }

        /// <summary>A reading aid: what the navmesh offers between two points (closest floor, straight path, where it stops).</summary>
        [TestCase(277, 32395, 31847, 16158, 29015, 41910, 12933)]   // Hall of the Corrupt: entrance to Lord Gildas
        [TestCase(277, 31671, 35027, 15368, 29015, 41910, 12933)]   // the raid's front at the end (2026-10-22) to Lord Gildas
        [TestCase(277, 32395, 31847, 16158, 31353, 37634, 14873)]   // entrance to Aidon (control)
        [TestCase(277, 29015, 41910, 12933, 28742, 40250, 13692)]   // Lord Gildas back toward where the raid's path stops
        public void ProbeBetween(int regionId, int x1, int y1, int z1, int x2, int y2, int z2)
        {
            Zone zone = LoadZone(regionId);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            foreach (Vector3 p in new Vector3[] { new(x1, y1, z1), new(x2, y2, z2) })
                foreach (float range in new[] { 64f, 256f, 1024f })
                    TestContext.Out.WriteLine($"closest to {p} within {range}: {nav.GetClosestPoint(zone, p, range, range, range, nav.DefaultFilters)?.ToString() ?? "none"}");
            Vector3? a = nav.GetClosestPoint(zone, new(x1, y1, z1), 256, 256, 256, nav.DefaultFilters);
            Vector3? b = nav.GetClosestPoint(zone, new(x2, y2, z2), 1024, 1024, 1024, nav.DefaultFilters);
            if (a == null || b == null) { TestContext.Out.WriteLine("no floor"); return; }
            var nodes = new WrappedPathfindingNode[4096];
            var result = nav.GetPathStraight(zone, a.Value, b.Value, nav.DefaultFilters, nodes);
            Vector3 end = result.NodeCount > 0 ? nodes[result.NodeCount - 1].Position : a.Value;
            TestContext.Out.WriteLine($"path {a} -> {b}: {result.Status} nodes={result.NodeCount} ends at {end} ({Vector3.Distance(end, b.Value):0} short)");
            for (int i = 0; i < result.NodeCount; i += Math.Max(1, result.NodeCount / 30))
                TestContext.Out.WriteLine($"  {i}: {nodes[i].Position} {nodes[i].Flags}");
            if (Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROBE_GRID") != $"{regionId}:{x1}")
                return;
            // Floors on a grid between the two points: every level the mesh has there.
            float minX = Math.Min(x1, x2) - 1500, maxX = Math.Max(x1, x2) + 1500, minY = Math.Min(y1, y2) - 1500, maxY = Math.Max(y1, y2) + 1500;
            for (float gy = minY; gy <= maxY; gy += 400)
            {
                var row = new System.Text.StringBuilder($"y={gy,6:0}:");
                for (float gx = minX; gx <= maxX; gx += 400)
                {
                    var levels = new SortedSet<int>();
                    for (float gz = Math.Min(z1, z2) - 800; gz <= Math.Max(z1, z2) + 800; gz += 150)
                        if (nav.GetClosestPoint(zone, new(gx, gy, gz), 150, 150, 80, nav.DefaultFilters) is Vector3 f &&
                            Math.Abs(f.X - gx) < 160 && Math.Abs(f.Y - gy) < 160)
                            levels.Add((int)Math.Round(f.Z / 100));
                    row.Append(levels.Count == 0 ? "      ." : $" {string.Join('/', levels),6}");
                }
                TestContext.Out.WriteLine(row.ToString());
            }
        }

        /// <summary>
        /// Which of two points each floor on a grid connects to (G: the first, E: the second, B: both, -: neither), and its
        /// height in hundreds. Set OFFLINE_DAOC_PROBE_MAP="region:x0,y0,x1,y1,step:z0,z1:ax,ay,az:bx,by,bz".
        /// </summary>
        [Test]
        public void ProbeConnectivityMap()
        {
            string spec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_PROBE_MAP");
            Assume.That(spec, Is.Not.Null.And.Not.Empty);
            string[] parts = spec.Split(':');
            int[] where = parts[0].Split('/').Select(int.Parse).ToArray();
            int regionId = where[0];
            float[] box = parts[1].Split(',').Select(float.Parse).ToArray();
            float[] zs = parts[2].Split(',').Select(float.Parse).ToArray();
            float[] a = parts[3].Split(',').Select(float.Parse).ToArray();
            float[] b = parts[4].Split(',').Select(float.Parse).ToArray();
            Zone zone = where.Length == 4 ? LoadZone(regionId, where[1], where[2], where[3]) : LoadZone(regionId);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 pa = nav.GetClosestPoint(zone, new(a[0], a[1], a[2]), 256, 256, 256, nav.DefaultFilters).Value;
            Vector3 pb = nav.GetClosestPoint(zone, new(b[0], b[1], b[2]), 256, 256, 256, nav.DefaultFilters).Value;
            var nodes = new WrappedPathfindingNode[4096];
            bool Joins(Vector3 from, Vector3 to)
            {
                var r = nav.GetPathStraight(zone, from, to, nav.DefaultFilters, nodes);
                return r.Status == PathfindingStatus.PathFound && r.NodeCount > 0 && Vector3.Distance(nodes[r.NodeCount - 1].Position, to) < 32;
            }
            TestContext.Out.WriteLine($"A={pa} B={pb} A-B joined={Joins(pa, pb)}");
            for (float gy = box[1]; gy <= box[3]; gy += box[4])
            {
                var row = new System.Text.StringBuilder($"y={gy,6:0}:");
                for (float gx = box[0]; gx <= box[2]; gx += box[4])
                {
                    var cells = new SortedDictionary<int, string>();
                    for (float gz = zs[0]; gz <= zs[1]; gz += 60)
                        if (nav.GetClosestPoint(zone, new(gx, gy, gz), box[4] / 2, box[4] / 2, 40, nav.DefaultFilters) is Vector3 f &&
                            Math.Abs(f.X - gx) <= box[4] / 2 && Math.Abs(f.Y - gy) <= box[4] / 2)
                        {
                            int level = (int)Math.Round(f.Z / 100);
                            if (cells.ContainsKey(level)) continue;
                            bool ja = Joins(f, pa), jb = Joins(f, pb);
                            cells[level] = (ja && jb ? "B" : ja ? "G" : jb ? "E" : "-") + level;
                        }
                    row.Append(cells.Count == 0 ? "         ." : $" {string.Join('/', cells.Values),9}");
                }
                TestContext.Out.WriteLine(row.ToString());
            }
        }

        /// <summary>
        /// The expedition router (RealmRaidMuster.TryRoute) on real meshes, with why it failed.
        /// OFFLINE_DAOC_MUSTER="realm:region:zone/offsetX/offsetY,...:x,y,z:x,y,z" (start, goal; zones 8 x 8192 wide).
        /// </summary>
        [Test]
        public void ProbeMusterRoute()
        {
            string spec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_MUSTER");
            Assume.That(spec, Is.Not.Null.And.Not.Empty);
            string[] parts = spec.Split(':');
            var realm = (eRealm)int.Parse(parts[0]);
            ushort regionId = ushort.Parse(parts[1]);
            var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Region).GetField("m_regionData", flags).SetValue(region, new RegionData { Id = regionId });
            var zones = new List<Zone>();
            typeof(Region).GetField("m_zones", flags).SetValue(region, zones);
            foreach (string item in parts[2].Split(','))
            {
                int[] v = item.Split('/').Select(int.Parse).ToArray();
                var zone = new Zone(region, (ushort)v[0], $"zone {v[0]}", v[1], v[2], 65536, 65536, (ushort)v[0], false, 0, false, 0, 0, 0, 0, 0);
                zones.Add(zone);
                _loaded.Add(zone);
                LocalPathfindingMgr.LoadNavMesh(zone);
            }
            Vector3 P(string s) { float[] f = s.Split(',').Select(float.Parse).ToArray(); return new(f[0], f[1], f[2]); }
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 start = nav.GetClosestPoint(region.GetZone((int)P(parts[3]).X, (int)P(parts[3]).Y), P(parts[3]), 64, 64, 64, nav.DefaultFilters) ?? P(parts[3]);
            Vector3 goal = nav.GetClosestPoint(region.GetZone((int)P(parts[4]).X, (int)P(parts[4]).Y), P(parts[4]), 128, 128, 128, nav.DefaultFilters) ?? P(parts[4]);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ok = RealmRaidMuster.TryRoute(region, nav, realm, start, goal, out Vector3[] seams);
            TestContext.Out.WriteLine($"{start} -> {goal}: {(ok ? "OK" : "FAILED " + RealmRaidMuster.LastRouteFailure)} in {watch.ElapsedMilliseconds} ms; seams {string.Join(" ", seams)}");
        }

        /// <summary>
        /// A dungeon raid's staging posts by its portal and whether the hub's route reaches each (the raid start check).
        /// OFFLINE_DAOC_POSTS="realm:region:zone/offsetX/offsetY:portal x,y,z:hub x,y,z:slots".
        /// </summary>
        [Test]
        public void ProbeDungeonPosts()
        {
            string spec = Environment.GetEnvironmentVariable("OFFLINE_DAOC_POSTS");
            Assume.That(spec, Is.Not.Null.And.Not.Empty);
            string[] parts = spec.Split(':');
            var realm = (eRealm)int.Parse(parts[0]);
            int[] z = parts[2].Split('/').Select(int.Parse).ToArray();
            var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Region).GetField("m_regionData", flags).SetValue(region, new RegionData { Id = ushort.Parse(parts[1]) });
            var zones = new List<Zone>();
            typeof(Region).GetField("m_zones", flags).SetValue(region, zones);
            var zone = new Zone(region, (ushort)z[0], $"zone {z[0]}", z[1], z[2], 65536, 65536, (ushort)z[0], false, 0, false, 0, 0, 0, 0, 0);
            zones.Add(zone);
            _loaded.Add(zone);
            LocalPathfindingMgr.LoadNavMesh(zone);
            Vector3 P(string s) { float[] f = s.Split(',').Select(float.Parse).ToArray(); return new(f[0], f[1], f[2]); }
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Vector3 portal = P(parts[3]);
            Vector3 hub = nav.GetClosestPoint(zone, P(parts[4]), 64, 64, 64, nav.DefaultFilters) ?? P(parts[4]);
            var posts = new List<Vector3>();
            for (int slot = 0; slot < int.Parse(parts[5]); slot++)
            {
                if (!RealmRaidStaging.TryDungeonPost(nav, zone, portal, slot, posts, out Vector3 post))
                { TestContext.Out.WriteLine($"slot {slot}: no post"); continue; }
                posts.Add(post);
                bool routed = RealmRaidMuster.TryRoute(region, nav, realm, hub, post, out _);
                bool direct = AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, hub, post);
                TestContext.Out.WriteLine($"slot {slot}: {post} dist {Vector3.Distance(post, portal):0} route={routed} {(routed ? "" : RealmRaidMuster.LastRouteFailure)} direct={direct}");
            }
        }

        private Zone LoadZone(int regionId) => LoadZone(regionId, regionId, 8192, 8192);

        private Zone LoadZone(int regionId, int zoneId, int offsetX, int offsetY)
        {
            var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Region).GetField("m_regionData", flags).SetValue(region, new RegionData { Id = (ushort)regionId });
            var zones = new List<Zone>();
            typeof(Region).GetField("m_zones", flags).SetValue(region, zones);
            var zone = new Zone(region, (ushort)zoneId, $"zone {zoneId}", offsetX, offsetY, 65536, 65536, (ushort)zoneId, false, 0, false, 0, 0, 0, 0, 0);
            zones.Add(zone);
            _loaded.Add(zone);
            LocalPathfindingMgr.LoadNavMesh(zone);
            Assert.That(PathfindingProvider.LocalPathfindingMgr.HasNavmesh(zone), Is.True, "navmesh loaded");
            return zone;
        }

        private void Reach(int regionId, Vector3 entrance, Vector3 trigger)
        {
            int x = (int)entrance.X, y = (int)entrance.Y, z = (int)entrance.Z;
            var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            typeof(Region).GetField("m_regionData", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(region, new RegionData { Id = (ushort)regionId });
            typeof(Region).GetField("m_zones", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(region, new List<Zone>());
            var zone = new Zone(region, (ushort)regionId, $"zone {regionId}", 8192, 8192, 65536, 65536, (ushort)regionId, false, 0, false, 0, 0, 0, 0, 0);
            ((List<Zone>)typeof(Region).GetField("m_zones", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(region)).Add(zone);
            _loaded.Add(zone);
            LocalPathfindingMgr.LoadNavMesh(zone);
            var nav = PathfindingProvider.LocalPathfindingMgr;
            Assert.That(nav.HasNavmesh(zone), Is.True, "navmesh loaded");
            Vector3? floor = nav.GetClosestPoint(zone, new(x, y, z), 48, 48, 64, nav.DefaultFilters);
            Assert.That(floor.HasValue, Is.True, "navmesh floor at the entrance arrival");
            Assert.That(AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value), Is.True, "the arrival has a way out");
            bool reached = AutonomousZonePointApproach.TryResolve(nav, zone, floor.Value, trigger, 220, out Vector3 approach);
            TestContext.Out.WriteLine($"region {regionId}: entrance {floor.Value} -> final {trigger}: {(reached ? $"reached at {approach}" : "no path")}");
            Assert.That(reached, Is.True, "a proven path from the entrance to the final room");
        }
    }
}
