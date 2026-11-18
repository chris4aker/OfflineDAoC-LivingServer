using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The Darkness Falls knight loop (owner's PC log, build b1b6846, 2026-11-18): after Prince Abdin died at 16:40:16 the
    /// Albion raid walked its chain towards Prince Asmoien, and each time Chthonic Knight Ukobat (level 66, back about 3
    /// minutes after a death, under the ten-minute epic hold) came back on its trail behind it, the trail gate turned the
    /// whole raid back to him: 12 REALM_EXPEDITION_TRAIL_GATE lines "from=Ukobat", 14 kills of him in a row from 16:42:30
    /// to 18:14:46 and no other named. The knight, the route, raiders and trail stand in a test region; no world tick, combat or pathing
    /// runs. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_DfKnightLoop
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 7_400;
        private EpicTestServerScope _server;
        private ushort[] _savedHoldRegions;

        public sealed class Knight : GameEpicNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
            public override bool InCombat => false;
        }
        public sealed class Prince : GameEpicNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
            public override bool InCombat => false;
        }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }

        // Ukobat's spawn and the front the raid's switches took (REALM_EXPEDITION_TRAIL_GATE / TARGET_SWITCH, b1b6846).
        private const int UkobatX = 16129, UkobatY = 21495, UkobatZ = 18284;

        private static FieldInfo HoldRegions => typeof(AutonomousRealmRaid).GetField("_epicHoldRegions", HiddenStatic)!;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = AutonomousDarknessFallsPolicy.RegionId, Name = "DF knight test",
                Description = "DF knight test", Mobs = [] });
            _region.Zones.Add(new Zone(_region, 2490, "DF knight test", 0, 0, 65536, 65536, 2490, false, 0, false, 0, 0, 0, 0, 0));
            AutonomousDarknessFallsRaidTrail.ForgetAll();
            _savedHoldRegions = (ushort[])HoldRegions.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            HoldRegions.SetValue(null, _savedHoldRegions);
            AutonomousDarknessFallsRaidTrail.ForgetAll();
            _server.Dispose();
        }

        private T Npc<T>(int x, int y, int z, string name) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameObject).GetField("m_name", Hidden)!.SetValue(npc, name);
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            var caches = typeof(GameObject).GetField("_objectsInRadiusCaches", Hidden)!;
            caches.SetValue(npc, Activator.CreateInstance(caches.FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(npc, new ArrayList());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)!.SetValue(npc, new PropertyCollection());
            typeof(GameLiving).GetField("_abilitiesLock", Hidden)?.SetValue(npc, new Lock());
            typeof(GameLiving).GetField("m_abilities", Hidden)?.SetValue(npc, new Dictionary<string, Ability>());
            npc.effectListComponent = EffectListComponent.Create(npc);
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private Knight Ukobat()
        {
            var knight = Npc<Knight>(UkobatX, UkobatY, UkobatZ, "Chthonic Knight Ukobat");
            var brain = new StandardMobBrain { AggroRange = 500 };
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(knight, brain);
            brain.Body = knight;
            typeof(GameNPC).GetField("m_respawnInterval", Hidden)!.SetValue(knight, 180_000);
            return knight;
        }

        private static RealmRaidDungeonRoute DarknessFallsRoute()
        {
            ConstructorInfo ctor = typeof(RealmRaidDungeonRoute).GetConstructors(Hidden).Single(c => c.GetParameters().Length == 7);
            Vector3 muster = new(16260, 22862, 18556);
            return (RealmRaidDungeonRoute)ctor.Invoke([AutonomousDarknessFallsPolicy.RegionId, null, muster, muster,
                new[] { "Legion" }, new[] { "Prince Asmoien", "Chthonic Knight Ukobat" }, eRealm.Albion]);
        }

        private static void Set(RealmRaidDungeonRoute route, string field, object value) =>
            typeof(RealmRaidDungeonRoute).GetField(field, Hidden)!.SetValue(route, value);
        private static object Get(RealmRaidDungeonRoute route, string field) =>
            typeof(RealmRaidDungeonRoute).GetField(field, Hidden)!.GetValue(route);

        private static bool TrailGate(RealmRaidDungeonRoute route, GameNPC[] live, GameLiving[] raiders, long now) =>
            (bool)typeof(RealmRaidDungeonRoute).GetMethod("TrailGate", Hidden)!.Invoke(route, [live, raiders, now]);
        private static void Defeated(RealmRaidDungeonRoute route, long now) =>
            typeof(RealmRaidDungeonRoute).GetMethod("TargetDefeated", Hidden)!.Invoke(route, [now]);

        /// <summary>The raid's trail: its lead raider walked from below Ukobat's spot, past him, on towards Prince Asmoien.</summary>
        private Raider WalkTrail(RealmRaidDungeonRoute route)
        {
            var lead = Npc<Raider>(UkobatX, UkobatY - 1600, UkobatZ, "Lead");
            for (int i = 0; i < 6; i++)
            {
                lead.Y = UkobatY - 1600 + i * 800;
                lead.movementComponent.ForceUpdatePosition();
                AutonomousDarknessFallsRaidTrail.Observe(route, eRealm.Albion, [lead], new(lead.X, lead.Y, lead.Z), i * 3_000);
            }
            Assert.That(AutonomousDarknessFallsRaidTrail.TryPoints(eRealm.Albion, out Vector3[] trail), Is.True);
            Assert.That(RealmRaidRouteGate.Blocks(trail, new(UkobatX, UkobatY, UkobatZ), 500), Is.True, "Ukobat stands on the trail");
            return lead;
        }

        // ---- the respawn hold ----------------------------------------------------------------------------------------------

        [Test]
        public void AKnightTheRaidKilledOnItsWayIsHeldWhateverItsRespawn()
        {
            const int knight = 180_000; // about 3 minutes (Ukobat: killed 16:52:44, back by 16:55:50)
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, knight, true), Is.False,
                    "build b1b6846: under ten minutes, its own timer");
                Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, knight, true, killedOnRaidsWay: true), Is.True,
                    "the raid killed it on its way: held while the raid runs");
                Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, knight, false, killedOnRaidsWay: true), Is.False,
                    "no raid in Darkness Falls (it ended): its own timer");
                Assert.That(RealmRaidRouteGate.HoldsRespawn(false, true, true, knight, true, killedOnRaidsWay: true), Is.False,
                    "the hold switched off");
                Assert.That(RealmRaidRouteGate.HoldsRespawn(true, false, true, knight, true, killedOnRaidsWay: true), Is.False,
                    "ordinary monsters keep the trash hold's rules");
                Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, 3_600_000, true), Is.True, "the long hold is unchanged");
            });
        }

        [Test]
        public void UkobatKilledAsTheRaidsTargetStaysDownUntilTheRaidEnds()
        {
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute), nameof(AutonomousRealmRaid));
            var ukobat = Ukobat();
            HoldRegions.SetValue(null, new[] { AutonomousDarknessFallsPolicy.RegionId });
            ukobat.Dead = true;
            Assert.That(AutonomousRealmRaid.HoldsEpicRespawn(ukobat), Is.False, "build b1b6846: back in 3 minutes during the raid");

            RealmRaidDungeonRoute route = DarknessFallsRoute();
            ukobat.Dead = false;
            Set(route, "_target", ukobat);
            ukobat.Dead = true;
            Defeated(route, 60_000);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.IsKilledOnTheWay(ukobat), Is.True);
                Assert.That(AutonomousRealmRaid.HoldsEpicRespawn(ukobat), Is.True, "held while the raid is in Darkness Falls");
                Assert.That(logs.Lines.Any(l => l.StartsWith("REALM_RAID_EPIC_RESPAWN_HELD ") && l.Contains("killedOnWay=True")),
                    Is.True, string.Join(" | ", logs.Lines));
                Assert.That(ukobat.RespawnInterval, Is.EqualTo(180_000), "his own timer is unchanged");
            });
            // The raid ended (its hold released): he comes back on his own timer and is forgotten.
            HoldRegions.SetValue(null, Array.Empty<ushort>());
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.HoldsEpicRespawn(ukobat), Is.False);
                Assert.That(AutonomousRealmRaid.IsKilledOnTheWay(ukobat), Is.False);
            });
            // A solo bot's kill during a raid is not the raid's: his own timer.
            HoldRegions.SetValue(null, new[] { AutonomousDarknessFallsPolicy.RegionId });
            Assert.That(AutonomousRealmRaid.HoldsEpicRespawn(ukobat), Is.False);
        }

        [Test]
        public void OnlyADarknessFallsRouteMarksItsKills()
        {
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute), nameof(AutonomousRealmRaid));
            var ukobat = Ukobat();
            ConstructorInfo ctor = typeof(RealmRaidDungeonRoute).GetConstructors(Hidden).Single(c => c.GetParameters().Length == 7);
            Vector3 muster = new(0, 0, 0);
            var caerSidi = (RealmRaidDungeonRoute)ctor.Invoke([(ushort)60, null, muster, muster, new[] { "Apocalypse" }, null, eRealm.Albion]);
            Set(caerSidi, "_target", ukobat);
            ukobat.Dead = true;
            Defeated(caerSidi, 1_000);
            Assert.That(AutonomousRealmRaid.IsKilledOnTheWay(ukobat), Is.False, "other dungeons keep their epics' own timers");
        }

        // ---- the trail gate ------------------------------------------------------------------------------------------------

        [Test]
        public void TheTrailGatePassesAKnightKilledOnTheTrailThreeTimesAndTheRouteGoesOn()
        {
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute), nameof(AutonomousRealmRaid));
            bool hold = RealmRaidRouteGate.HoldEpicRespawn;
            RealmRaidRouteGate.HoldEpicRespawn = false; // the cap is for a knight that comes back anyway
            try
            {
                RealmRaidDungeonRoute route = DarknessFallsRoute();
                var ukobat = Ukobat();
                var asmoien = Npc<Prince>(UkobatX, UkobatY + 6000, UkobatZ, "Prince Asmoien");
                var lead = WalkTrail(route);
                ((HashSet<string>)Get(route, "_clearedNames")).Add(ukobat.Name);
                // The raid walks its chain to Prince Asmoien.
                Set(route, "_chain", new[] { new Vector3(UkobatX, UkobatY + 1000, UkobatZ), new Vector3(UkobatX, UkobatY + 5800, UkobatZ) });
                Set(route, "_chainTarget", asmoien);
                typeof(RealmRaidDungeonRoute).GetProperty(nameof(RealmRaidDungeonRoute.TargetName))!.SetValue(route, asmoien.Name);
                GameNPC[] live = [ukobat, asmoien];
                long now = 10 * 60_000;
                for (int kill = 1; kill <= RealmRaidDungeonRoute.TrailGateRekillCap; kill++)
                {
                    now += RealmRaidDungeonRoute.TrailGateCheckMilliseconds;
                    ukobat.Dead = false;
                    Assert.That(TrailGate(route, live, [lead], now), Is.True, $"respawn {kill}: the gate turns the raid on him");
                    Assert.That(route.TargetName, Is.EqualTo(ukobat.Name));
                    ukobat.Dead = true;
                    Defeated(route, now + 120_000);
                    Assert.That(route.TrailKills(ukobat.Name), Is.EqualTo(kill));
                    Assert.That(route.TargetName, Is.EqualTo("Prince Asmoien"),
                        "build b1b6846 kept \"Ukobat\": the goal read \"Advancing to Chthonic Knight Ukobat (waypoint 6/10)\"");
                }
                // Back a fourth time: the gate passes him, once logged.
                now += RealmRaidDungeonRoute.TrailGateCheckMilliseconds;
                ukobat.Dead = false;
                Assert.That(TrailGate(route, live, [lead], now), Is.False, "build b1b6846 turned on him 12 times in a row");
                now += RealmRaidDungeonRoute.TrailGateCheckMilliseconds;
                Assert.That(TrailGate(route, live, [lead], now), Is.False);
                string[] passed = logs.Lines.Where(l => l.StartsWith("REALM_EXPEDITION_TRAIL_GATE_PASSED ")).ToArray();
                Assert.Multiple(() =>
                {
                    Assert.That(passed, Has.Length.EqualTo(1), string.Join(" | ", logs.Lines));
                    Assert.That(passed[0], Does.Contain("target=\"Chthonic Knight Ukobat\"").And.Contain("kills=3 cap=3")
                        .And.Contain("from=\"Prince Asmoien\""));
                    Assert.That(Get(route, "_target"), Is.Null, "the route goes on to Asmoien");
                });
                // Another named killed: the count starts again.
                Set(route, "_target", asmoien);
                asmoien.Dead = true;
                Defeated(route, now + 60_000);
                Assert.That(route.TrailKills(ukobat.Name), Is.EqualTo(0));
                now += RealmRaidDungeonRoute.TrailGateCheckMilliseconds;
                Assert.That(TrailGate(route, live, [lead], now + 60_000), Is.True, "after real progress he is cleared again");
            }
            finally { RealmRaidRouteGate.HoldEpicRespawn = hold; }
        }

        [Test]
        public void AKnightKilledWhenItAttacksTheRaidNeitherCountsNorResetsItsTrailKills()
        {
            using var logs = new TestLogCapture(nameof(RealmRaidDungeonRoute), nameof(AutonomousRealmRaid));
            RealmRaidDungeonRoute route = DarknessFallsRoute();
            var ukobat = Ukobat();
            var lead = WalkTrail(route);
            ((HashSet<string>)Get(route, "_clearedNames")).Add(ukobat.Name);
            long now = 60_000;
            Assert.That(TrailGate(route, [ukobat], [lead], now), Is.True);
            ukobat.Dead = true;
            Defeated(route, now);
            // He comes back and attacks the raid: fought as an attacker (another switch), not by the trail gate.
            ukobat.Dead = false;
            Set(route, "_target", ukobat);
            ukobat.Dead = true;
            Defeated(route, now + 60_000);
            Assert.That(route.TrailKills(ukobat.Name), Is.EqualTo(1));
        }

        [Test]
        public void TheCapIsThreeTrailKills()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidDungeonRoute.TrailGateRekillCap, Is.EqualTo(3));
                Assert.That(RealmRaidDungeonRoute.TrailGatePasses(2), Is.False);
                Assert.That(RealmRaidDungeonRoute.TrailGatePasses(3), Is.True);
                Assert.That(RealmRaidDungeonRoute.TrailGatePasses(0), Is.False);
            });
        }

        // ---- #358 W-1 -------------------------------------------------------------------------------------------------------

        [Test]
        public void TheEpicTrashRouteGateNeverAppliesToDarknessFalls()
        {
            // A Darkness Falls raid enters region 249 straight from its realm (Camelot Hills for Albion): no dungeon on the
            // way in, so #358's W-1 epic-trash gate (RealmRaidDungeonRoute.TryCreateGate) is never built for it.
            ConstructorInfo ctor = typeof(RealmRaidDungeonRoute).GetConstructors(Hidden).Single(c => c.GetParameters().Length == 7);
            var entrance = new DbZonePoint { TargetRegion = AutonomousDarknessFallsPolicy.RegionId, SourceRegion = 1 };
            Vector3 muster = new(16260, 22862, 18556);
            var route = (RealmRaidDungeonRoute)ctor.Invoke([AutonomousDarknessFallsPolicy.RegionId, entrance, muster, muster,
                new[] { "Legion" }, new[] { "Prince Asmoien" }, eRealm.Albion]);
            Assert.That(RealmRaidDungeonRoute.TryCreateGate(route, out var gate, out string detail), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(gate, Is.Null);
                Assert.That(detail, Is.EqualTo("no dungeon on the way in"));
            });
        }
    }
}
