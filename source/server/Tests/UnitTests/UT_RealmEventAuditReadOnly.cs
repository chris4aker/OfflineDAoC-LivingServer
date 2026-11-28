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
                // The muster's own route (RealmRaidMuster.TryRoute from the Innis Carthaig hub, the dragon-hibernia hub's
                // anchor): the road to Cuuldurach with and without the expedition-only via hint, every leg complete.
                Zone hubZone=region.GetZone((int)hub.X,(int)hub.Y);
                bool direct=RealmRaidMuster.TryRoute(region,nav,eRealm.Hibernia,hub,goal,out var directSteps);
                bool via=RealmRaidMuster.TryRoute(region,nav,eRealm.Hibernia,hub,goal,out var viaSteps,new Vector3(361354,750434,4944));
                bool walk=InstalledBotRoute.Itinerary(region,nav,hub,goal,out string walkTrace);
                TestContext.WriteLine($"HUB {hub} zone={hubZone.ID} direct={direct} [{string.Join(";",directSteps)}] via={via} [{string.Join(";",viaSteps)}] walk={walk}: {walkTrace}");
                Assert.That(direct, Is.True, "Ordinary Sheeroe travel from the hub must find the road without an expedition-only via hint");
                Assert.That(via, Is.True, "The hub's route with its via hint");
                Assert.That(walk, Is.True, $"The bot's route from the hub to Cuuldurach, every leg complete: {walkTrace}");
                // #704: the September captures A (zone 204) and B (zone 202) stand on side ground at the 202/204 seam (not
                // the zones' main ground). What is checked: no muster or bot route from them (the itinerary only steps back
                // and forth across that seam), and no route from the hub onto them. That ground is a listed seam network
                // with zones 216, 205 and 201 (seams.json), so other ways on are not ruled out: #845. Pinned as a cut.
                foreach(var p in new[]{new Vector3(352160,680000,7224),new Vector3(352160,679872,7315)})
                {
                    var zone=region.GetZone((int)p.X,(int)p.Y);
                    Vector3 start=InstalledBotRoute.Start(nav,zone,p);
                    Zone startZone=region.GetZone((int)start.X,(int)start.Y);
                    var ground=InstalledBotRoute.Ground(200,startZone,start);
                    bool captured=RealmRaidMuster.TryRoute(region,nav,eRealm.Hibernia,start,goal,out _);
                    bool fromHub=InstalledBotRoute.Itinerary(region,nav,hub,start,out string hubTrace);
                    bool onward=InstalledBotRoute.Itinerary(region,nav,start,goal,out string onwardTrace);
                    TestContext.WriteLine($"CAPTURE {p} start={start} zone={startZone.ID} area={ground.Area} main={ground.Main} exit={AutonomousRendezvousNavigation.HasLocalExit(nav,startZone,start)} " +
                        $"route={captured} fromHub={fromHub}: {hubTrace}; onward={onward}: {onwardTrace}");
                    Assert.That(ground.Area.HasValue && ground.Main.HasValue && ground.Area != ground.Main, Is.True,
                        $"Sheeroe capture {p}: the cut, side ground (area {ground.Area}, main {ground.Main})");
                    Assert.That(captured, Is.False, $"Sheeroe capture {p}: the cut, no muster route from the side ground");
                    Assert.That(onward, Is.False, $"Sheeroe capture {p}: the cut, no bot route onward ({onwardTrace})");
                    Assert.That(fromHub, Is.False, $"Sheeroe capture {p}: no route from the hub onto the side ground ({hubTrace})");
                }
            });
        }
        finally{foreach(var z in region.Zones)LocalPathfindingMgr.UnloadNavMesh(z);Environment.CurrentDirectory=previous;}
    }
}
