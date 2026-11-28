using System;
using System.IO;
using System.Data.SQLite;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

[NonParallelizable,Explicit("Read-only captured route diagnostics, never edits meshes or game state")]
public class UT_RealmEventAuditReadOnly
{
    [Test]public void InspectCapturedHibernianExpeditionPile()
    {
        string previous=Environment.CurrentDirectory;
        string root=Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");
        var region=new Region(new RegionData{Id=200,Name="Read-only probe",Description="Read-only probe",Mobs=[]});
        try
        {
            DOL.UnitTests.InstalledDetour.Use(root);
            Environment.CurrentDirectory=root;
            using var db=new SQLiteConnection($"Data Source={Path.GetFullPath("../data/opendaoc.sqlite3.db")};Read Only=True;Pooling=False;");db.Open();
            using var command=db.CreateCommand();command.CommandText="SELECT ZoneID,Name,OffsetX,OffsetY,Width,Height FROM Zones WHERE RegionID=200";
            using(var rows=command.ExecuteReader())while(rows.Read())
            {
                ushort id=Convert.ToUInt16(rows[0]);var z=new Zone(region,id,rows.GetString(1),Convert.ToInt32(rows[2])*8192,Convert.ToInt32(rows[3])*8192,Convert.ToInt32(rows[4])*8192,Convert.ToInt32(rows[5])*8192,id,false,0,false,0,0,0,0,0){IsPathfindingEnabled=true};
                region.Zones.Add(z);LocalPathfindingMgr.LoadNavMesh(z);
            }
            var nav=PathfindingProvider.LocalPathfindingMgr;var h=DragonLairPlacement.Home(eRealm.Hibernia);var goal=new Vector3(h.X,h.Y,h.Z);
            Vector3 hub=new(334820,719979,4296);
            Assert.Multiple(()=>
            {
                foreach(var p in new[]{new Vector3(352160,680000,7224),new Vector3(352160,679872,7315),hub})
                {
                    var zone=region.GetZone((int)p.X,(int)p.Y);
                    // The hub centre is the muster's own anchor (service NPCs), not a capture: routed from as the muster does.
                    Vector3 start=p==hub?p:InstalledBotRoute.Start(nav,zone,p);
                    TestContext.WriteLine($"GROUND {p} pocket={AutonomousZonePockets.Contains(zone.ID,start.X,start.Y,start.Z)} escape={AutonomousRouteHotspotRepair.TryGetImmediateEscape(nav,region,200,start,out var esc)}:{esc} " +
                        $"wideFloor={nav.GetClosestPoint(zone,p,256,256,1024,nav.DefaultFilters)}");
                    var ground=InstalledBotRoute.Ground(200,zone,start);
                    bool direct=RealmRaidMuster.TryRoute(region,nav,eRealm.Hibernia,start,goal,out var directSteps);
                    bool via=RealmRaidMuster.TryRoute(region,nav,eRealm.Hibernia,start,goal,out var viaSteps,new Vector3(361354,750434,4944));
                    bool walk=InstalledBotRoute.Itinerary(region,nav,start,goal,out string walkTrace);
                    bool toHub=p==hub||InstalledBotRoute.Itinerary(region,nav,start,hub,out _);
                    bool fromHub=p==hub||InstalledBotRoute.Itinerary(region,nav,hub,start,out _);
                    TestContext.WriteLine($"Captured {p} zone={zone.ID} start={start} area={ground.Area} main={ground.Main} localExit={AutonomousRendezvousNavigation.HasLocalExit(nav,zone,start)} " +
                        $"direct={direct} [{string.Join(";",directSteps)}] validated-via={via} [{string.Join(";",viaSteps)}] walk={walk} toHub={toHub} fromHub={fromHub}: {walkTrace}");
                    Assert.That(direct, Is.True, $"Ordinary Sheeroe travel from {p} must find the road without an expedition-only via hint");
                }
            });
        }
        finally{foreach(var z in region.Zones)LocalPathfindingMgr.UnloadNavMesh(z);Environment.CurrentDirectory=previous;}
    }
}
