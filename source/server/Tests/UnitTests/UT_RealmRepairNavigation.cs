using System;
using System.Collections.Generic;
using System.IO;
using System.Data.SQLite;
using System.Numerics;
using System.Runtime.InteropServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;
[NonParallelizable, Explicit("Installed navigation read-only repair probes")]
public class UT_RealmRepairNavigation
{
    [Test]
    public void CapturedGlacierApproaches()
    {
        string previous=Environment.CurrentDirectory;
        string root=Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        var regions=new Dictionary<ushort,Region>();
        try
        {
            DOL.UnitTests.InstalledDetour.Use(root);
            Environment.CurrentDirectory=root;
            using var db=new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");db.Open();
            foreach(ushort rid in new ushort[]{151,160,200})
            {
                var region=new Region(new RegionData{Id=rid,Name="Probe",Description="Probe",Mobs=[]});regions[rid]=region;
                using var cmd=db.CreateCommand();cmd.CommandText="SELECT ZoneID,Name,OffsetX,OffsetY,Width,Height FROM Zones WHERE RegionID="+rid;
                using var rows=cmd.ExecuteReader();
                while(rows.Read())
                {
                    ushort id=Convert.ToUInt16(rows[0]);
                    var z=new Zone(region,id,rows.GetString(1),Convert.ToInt32(rows[2])*8192,Convert.ToInt32(rows[3])*8192,Convert.ToInt32(rows[4])*8192,Convert.ToInt32(rows[5])*8192,id,false,0,false,0,0,0,0,0){IsPathfindingEnabled=true};
                    region.Zones.Add(z);LocalPathfindingMgr.LoadNavMesh(z);
                }
            }
            var nav=PathfindingProvider.LocalPathfindingMgr;
            // #704: every check runs and a red run lists them all.
            Assert.Multiple(()=>
            {
            foreach(var (rid,p) in new (ushort,Vector3)[]{
                (151,new(382872,333782,6389)),(151,new(377328,286268,7593)),
                (151,new(348195,302918,5526)),(151,new(376591,288264,7446)),
                (151,new(376439,288389,7439)),(151,new(375726,288975,7405)),
                (151,new(370425,282504,8025)),(151,new(385318,329452,5962)),
                (151,new(380478,280008,8784)),(151,new(331156,298157,8789)),
                (151,new(331682,297516,8370)),(160,new(30522,22478,17467))})
            {
                var region=regions[rid];var zone=region.GetZone((int)p.X,(int)p.Y);
                var small=nav.GetClosestPoint(zone,p,64,64,128,nav.DefaultFilters);
                var wide=nav.GetClosestPoint(zone,p,768,768,768,nav.DefaultFilters);
                var tall=nav.GetClosestPoint(zone,p,64,64,4096,nav.DefaultFilters);
                TestContext.WriteLine($"CAPTURE region={rid} zone={zone?.Description} position={p} small={small} wide={wide} vertical={tall}");
            }
            var start=new Vector3(379260,385996,7752);var goal=new Vector3(398621,271246,8575);
            bool success=RealmRaidMuster.TryRoute(regions[151],nav,eRealm.Midgard,start,goal,out var seams);
            TestContext.WriteLine($"HAGALL ENTRY {success} [{string.Join(";",seams)}]");
            Assert.That(success, Is.True);
            var dragonHome = DragonLairPlacement.Home(eRealm.Hibernia);
            var dragonGoal = new Vector3(dragonHome.X,dragonHome.Y,dragonHome.Z);
            Vector3 glacier=new(352160,680000,7224);
            Zone glacierZone=regions[200].GetZone((int)glacier.X,(int)glacier.Y);
            Vector3 glacierStart=InstalledBotRoute.Start(nav,glacierZone,glacier);
            var glacierGround=InstalledBotRoute.Ground(200,glacierZone,glacierStart);
            bool hib=RealmRaidMuster.TryRoute(regions[200],nav,eRealm.Hibernia,glacierStart,dragonGoal,out var hibSeams,new(361354,750434,4944));
            TestContext.WriteLine($"CUULDURACH CAPTURE CORRIDOR {hib} start={glacierStart} area={glacierGround.Area} main={glacierGround.Main} [{string.Join(";",hibSeams)}] {RealmRaidMuster.LastRouteFailure}");
            Assert.That(hib, Is.True, "Cuuldurach route from the glacier capture");
            Vector3? surfaceFloor = nav.GetClosestPoint(regions[151].GetZone(382872,333782),
                new(382872,333782,7709),64,64,128,nav.DefaultFilters);
            Assert.That(surfaceFloor.HasValue, Is.True, "Hagall glacier surface floor");
            if (!surfaceFloor.HasValue) return;
            Vector3 surface = surfaceFloor.Value;
            Zone surfaceZone = regions[151].GetZone((int)surface.X,(int)surface.Y);
            foreach (var delta in new[]{new Vector3(150,0,0),new Vector3(0,150,0),new Vector3(-150,0,0)})
            {
                var grounded = nav.GetMoveAlongSurfaceGrounded(surfaceZone,surface,surface+delta,nav.DefaultFilters);
                Assert.That(grounded.HasValue, Is.True);
                if (!grounded.HasValue) continue;
                var floor = nav.GetClosestPoint(surfaceZone,grounded.Value,2,2,4,nav.DefaultFilters);
                Assert.That(floor.HasValue, Is.True);
                if (floor.HasValue) Assert.That(Math.Abs(floor.Value.Z-grounded.Value.Z), Is.LessThan(2));
                TestContext.WriteLine($"GLACIER APPROACH GROUND start={surface} legacy={nav.GetMoveAlongSurface(surfaceZone,surface,surface+delta,nav.DefaultFilters)} grounded={grounded}");
            }
            });
        }
        finally
        {
            foreach(var r in regions.Values)foreach(var z in r.Zones)LocalPathfindingMgr.UnloadNavMesh(z);
            Environment.CurrentDirectory=previous;
        }
    }
}
