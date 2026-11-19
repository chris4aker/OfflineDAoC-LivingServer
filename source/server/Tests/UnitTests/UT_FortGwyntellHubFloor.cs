using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The Fort Gwyntell floor (region 51, zone 53): a separate flat floor of 27 polygons at 5,715 that joins nothing.
    /// Lordmuffin logged in on it on the owner's PC (2026-11-17, 453e01a) and failed the epic-albion hub route three
    /// times before release. The terminal-pocket entry moves only a member that is really on that floor, and only to
    /// the hub centre when the hub reaches the member's formation slot.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_FortGwyntellHubFloor
    {
        private static readonly Vector3 Lordmuffin = new(426321, 416865, 5715);
        private static readonly Vector3 HubCentre = new(426905, 416817, 5712);
        private static readonly Vector3 BindPoint = new(426604, 416383, 5715);

        /// <summary>Two components: the trapped floor (its bounding box at 5,715) and the fort's connected ground.</summary>
        private sealed class TwoFloorMesh : PathfindingMgrBase
        {
            public static bool OnTrappedFloor(Vector3 p) =>
                p.X is >= 425856 and <= 426675 && p.Y is >= 416704 and <= 417613 && MathF.Abs(p.Z - 5715) <= 32;

            public override bool IsAvailable => true;
            public override bool HasNavmesh(Zone zone) => true;
            public override Vector3? GetClosestPoint(Zone zone, Vector3 position, float xRange, float yRange,
                float zRange, EDtPolyFlags[] filters) => position;

            public override PathfindingResult GetPathStraight(Zone zone, Vector3 from, Vector3 to,
                EDtPolyFlags[] filters, Span<WrappedPathfindingNode> nodes)
            {
                nodes[0] = new(from, EDtPolyFlags.Walk);
                if (OnTrappedFloor(from) != OnTrappedFloor(to))
                    return new(PathfindingStatus.NoPathFound, 1);
                nodes[1] = new(to, EDtPolyFlags.Walk);
                return new(PathfindingStatus.PathFound, 2);
            }
        }

        private static Region Region(ushort id, ushort zoneId, int zoneX, int zoneY, out Zone zone)
        {
            var region = (Region)RuntimeHelpers.GetUninitializedObject(typeof(Region));
            typeof(Region).GetField("m_regionData", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(region, new RegionData { Id = id });
            zone = new(region, zoneId, "Dales of Devwy", zoneX, zoneY, 65536, 65536, zoneId, false, 0, false, 0, 0, 0, 0, 0);
            typeof(Region).GetField("m_zones", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(region, new List<Zone> { zone });
            return region;
        }

        [Test]
        public void OnlyAMemberOnTheTrappedFloorIsMovedToTheHub()
        {
            var mesh = new TwoFloorMesh();
            Region region = Region(51, 53, 376832, 376832, out _);
            Vector3 slot = HubCentre + new Vector3(88, 0, 0);

            Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(mesh, region, 51, Lordmuffin,
                out Vector3 escape), Is.True, "Lordmuffin's logged position is on the trapped floor");
            Assert.That(escape, Is.EqualTo(HubCentre));
            Assert.That(AutonomousRouteHotspotRepair.TryGetHubRouteEscape(mesh, region, 51, Lordmuffin, slot,
                out Vector3 hubEscape), Is.True);
            Assert.That(hubEscape, Is.EqualTo(HubCentre));
            Assert.Multiple(() =>
            {
                // Connected fort ground at the same height inside the 500-unit circle (west of the floor's box).
                Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(mesh, region, 51,
                    new(425777, 417100, 5715), out _), Is.False, "a bot on the real network is never moved");
                Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(mesh, region, 51,
                    Lordmuffin with { Z = 5782 }, out _), Is.False, "the ground above the floor is outside the height band");
                Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(mesh, region, 51,
                    new(425100, 417158, 5715), out _), Is.False, "outside the measured circle");
                Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(mesh, Region(1, 53, 376832, 376832, out _),
                    1, Lordmuffin, out _), Is.False, "same coordinates in another region");
                Assert.That(TwoFloorMesh.OnTrappedFloor(BindPoint), Is.False, "the witness is on the connected ground");
                Assert.That(AutonomousRouteHotspotRepair.TryGetHubRouteEscape(mesh, region, 51, Lordmuffin,
                    new(426321, 417000, 5715), out _), Is.False, "the escape must reach the member's slot");
                Assert.That(AutonomousRouteHotspotRepair.TryGetHubRouteEscape(mesh, region, 51, HubCentre, slot,
                    out _), Is.False, "a member already at the hub is reported, not moved again");
            });
        }

        /// <summary>
        /// The same entry against the real mesh. OFFLINE_DAOC_NAV_ROOT holds navmesh/zone053.nav (the owner's PC copy);
        /// OFFLINE_DAOC_TEST_DETOUR names the native Detour library.
        /// </summary>
        [Test, Explicit("Read-only installed-navmesh probe")]
        public void InstalledMeshTrapsLordmuffinAndTheHubEscapeReachesTheBindPoint()
        {
            string root = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
            Assert.That(root, Is.Not.Null.And.Not.Empty);
            string native = Environment.GetEnvironmentVariable("OFFLINE_DAOC_TEST_DETOUR");
            if (!string.IsNullOrEmpty(native))
                NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                    (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(native) : IntPtr.Zero);
            string previous = Environment.CurrentDirectory;
            Region region = Region(51, 53, 376832, 376832, out Zone zone);
            try
            {
                Environment.CurrentDirectory = root;
                LocalPathfindingMgr.LoadNavMesh(zone);
                var nav = PathfindingProvider.LocalPathfindingMgr;
                Assert.That(nav.HasNavmesh(zone), Is.True);
                Vector3 hub = nav.GetClosestPoint(zone, HubCentre, 96, 96, 512, nav.DefaultFilters).Value;
                Vector3 bind = nav.GetClosestPoint(zone, BindPoint, 96, 96, 512, nav.DefaultFilters).Value;
                Vector3 slot = nav.GetClosestPoint(zone, HubCentre + new Vector3(88, 0, 0), 96, 96, 512,
                    nav.DefaultFilters).Value;
                TestContext.WriteLine($"HUB {hub} BIND {bind} SLOT {slot}");
                Assert.Multiple(() =>
                {
                    Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, Lordmuffin, hub), Is.False,
                        "Lordmuffin's floor has no corridor to the hub");
                    Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, hub, bind), Is.True,
                        "the hub and the bind point share the connected ground");
                    Assert.That(AutonomousRouteHotspotRepair.TryGetHubRouteEscape(nav, region, 51, Lordmuffin, slot,
                        out Vector3 escape), Is.True);
                    Assert.That(Vector3.Distance(escape, HubCentre), Is.LessThan(16));
                    // Connected fort ground at 5,717 inside the 500-unit circle, west and east of the floor.
                    Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(nav, region, 51,
                        new(425777, 417100, 5715), out _), Is.False, "connected ground inside the circle stays put");
                    Assert.That(AutonomousRouteHotspotRepair.TryGetImmediateEscape(nav, region, 51,
                        new(426700, 417158, 5715), out _), Is.False, "connected ground inside the circle stays put");
                    Assert.That(AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, Lordmuffin, bind), Is.False,
                        "the bind point is not on the trapped floor");
                });
            }
            finally
            {
                LocalPathfindingMgr.UnloadNavMesh(zone);
                Environment.CurrentDirectory = previous;
            }
        }
    }
}
