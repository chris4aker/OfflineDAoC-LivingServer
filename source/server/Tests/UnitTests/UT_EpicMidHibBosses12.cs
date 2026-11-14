using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2, every big boss in (owner 2026-11-14): the raid plans of the Midgard and Hibernia big bosses that had
    /// only the generic dungeon route. Tuscaran Glacier (Hakr, Hurika, Torst, Steinvor and Skuf, Hjalmar, Suttung, Issorden,
    /// the council Ozur, Otrygg and Hord, the elders Birghir and Guthlac, and the chain to King Tuscar and Queen Kula),
    /// Galladoria (the primals in the period's order, the Air and Water primals, Olcasgean's second body, Hurionthex, the
    /// Organic-Energy Mechanism, the Giant Sporite Cluster, Olcasar Geomancer), Tur Suil (Balor's eye), Iarnvidiur and
    /// Trollheim (Nosdoden). Bosses, adds and raiders stand in a test region; no world tick, combat, spell or pathing runs.
    /// Nothing here was played in game: tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicMidHibBosses12
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 3000;
        private EpicTestServerScope _server;
        private Func<GameNPC, bool> _waterRetreating, _shield;
        private Func<GameLiving> _eyeVictim;

        // Test doubles carry the boss's own class name: the catalog's rows go by it.
        public sealed class Hakr : DOL.GS.Hakr { public bool Fighting; public override bool IsAlive => true; public override bool InCombat => Fighting; }
        public sealed class HakrAdd : DOL.GS.HakrAdd { public override bool IsAlive => true; }
        public sealed class Hurika : DOL.GS.Hurika { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class Issorden : DOL.GS.Issorden { public override bool IsAlive => true; }
        public sealed class Hord : DOL.GS.Scripts.Hord { public bool Fighting; public override bool IsAlive => true; public override bool InCombat => Fighting; }
        public sealed class Otrygg : DOL.GS.Otrygg { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class OtryggAdd : DOL.GS.OtryggAdd { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class Guthlac : DOL.GS.Guthlac { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class FrozenBomb : DOL.GS.FrozenBomb { public override bool IsAlive => true; }
        public sealed class Steinvor : DOL.GS.Steinvor { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class Skuf : DOL.GS.Skuf { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class EffectMob : DOL.GS.EffectMob { public override bool IsAlive => true; }
        public sealed class KingTuscar : DOL.GS.KingTuscar { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class QueenKula : DOL.GS.QueenKula { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class Agmundr : DOL.GS.Agmundr { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class FirePrimal : DOL.GS.FirePrimal { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class EarthPrimal : DOL.GS.EarthPrimal { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class WaterPrimal : DOL.GS.WaterPrimal { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class AirPrimal : DOL.GS.AirPrimal { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class GiantSporiteCluster : DOL.GS.GiantSporiteCluster { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class GSCAdds : DOL.GS.GSCAdds { public override bool IsAlive => true; public override bool InCombat => false; }
        public sealed class OlcasarGeomancer : DOL.GS.OlcasarGeomancer { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class OGAdds : DOL.GS.OGAdds { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class Balor : DOL.GS.Balor { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class BalorEye : DOL.GS.BalorEye { public override bool IsAlive => true; }
        public sealed class Nosdoden : DOL.GS.Nosdoden { public override bool IsAlive => true; public override bool InCombat => true; }
        public sealed class NosdodenSummonedAdds : DOL.GS.NosdodenSummonedAdds { public override bool IsAlive => true; }
        public class Mob : GameNPC
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class Raider : GameNPC { public override bool IsAlive => true; }

        private static readonly int Warrior = (int)eCharacterClass.Warrior, Wizard = (int)eCharacterClass.Wizard;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            UseRegion(160);
            EpicEncounterMechanics.ResetPerEncounter();
            EncounterSignals.ClearAll();
            _waterRetreating = T.WaterPrimalRetreating;
            _shield = T.HasDamageShield;
            _eyeVictim = T.BalorEyeVictim;
        }

        [TearDown]
        public void TearDown()
        {
            T.WaterPrimalRetreating = _waterRetreating;
            T.HasDamageShield = _shield;
            T.BalorEyeVictim = _eyeVictim;
            EncounterSignals.ClearAll();
            EpicEncounterMechanics.ResetPerEncounter();
            _server.Dispose();
        }

        private void UseRegion(ushort id)
        {
            _region = new Region(new RegionData { Id = id, Name = "Mid/Hib bosses test", Description = "Mid/Hib bosses test", Mobs = [] });
            _region.Zones.Add(new Zone(_region, (ushort)(id * 10), "Mid/Hib bosses test", 0, 0, 65536, 65536, (ushort)(id * 10 + 1),
                false, 0, false, 0, 0, 0, 0, 0));
        }

        private TNpc Npc<TNpc>(int x, int y, int z) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            var caches = typeof(GameObject).GetField("_objectsInRadiusCaches", Hidden)!;
            caches.SetValue(npc, Activator.CreateInstance(caches.FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(npc, new ArrayList());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)!.SetValue(npc, new PropertyCollection());
            npc.effectListComponent = EffectListComponent.Create(npc);
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            npc.SubZoneObject = new SubZoneObject(npc);
            Zone zone = _region.Zones[0];
            Assert.That(zone.AddObject(npc), Is.True);
            SubZoneObject sub = npc.SubZoneObject;
            sub.DestinationSubZone.AddObject(sub.Node);
            zone.OnObjectAddedToZone();
            sub.OnSubZoneTransition();
            return npc;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        // ---- the catalog ---------------------------------------------------------------------------------------------

        [Test]
        public void EveryBigBossOfTheseDungeonsHasARaidPlan()
        {
            string[] bosses =
            [
                "Hakr", "Hurika", "Torst", "Steinvor", "Skuf", "Hjalmar", "Suttung", "Issorden", "Ozur", "Otrygg", "Hord",
                "Birghir", "Guthlac", "AirPrimal", "WaterPrimal", "Olcasgean2", "UaimhLairmaster", "Hurionthex",
                "OrganicEnergyMechanism", "GiantSporiteCluster", "OlcasarGeomancer", "Balor", "Iarnvidiur", "Nosdoden",
            ];
            foreach (string boss in bosses)
                Assert.That(T.For(boss), Is.Not.Null, $"{boss}: a catalog row");
            Assert.That(T.AllDungeonRules.Where(r => r.Targets.Any(bosses.Contains)).All(r => !string.IsNullOrEmpty(r.Summary)), Is.True);
            Assert.That(T.For("Ozur").SpreadRadius, Is.EqualTo(400), "Ozur's Disease, 400 around his target");
            Assert.That(T.For("Iarnvidiur").SpreadRadius, Is.EqualTo(850), "Black Plague, 850 around his target");
            Assert.That(T.For("Hurionthex").SpreadWhileModel, Is.EqualTo(844), "Black Plague only in his monkey form");
            Assert.That(T.For("Torst") is { SpreadRadius: 400, MeleeBehind: true, TanksTaunt: true }, Is.True);
            Assert.That(T.For("Skuf").After, Is.EqualTo("Steinvor"));
            Assert.That(T.For("Birghir").After, Is.EqualTo("Guthlac"));
            Assert.That(T.For("Hakr").RequiredAdds, Is.EqualTo(new[] { "HakrAdd" }));
            Assert.That(T.For("Nosdoden").RequiredAdds, Is.EqualTo(new[] { "NosdodenSummonedAdds" }), "the waves stay required adds");
        }

        // ---- Tuscaran Glacier ----------------------------------------------------------------------------------------

        [Test]
        public void HakrsFarIceweaversDieFirstAndTheNearOnesInHisFight()
        {
            var hakr = Npc<Hakr>(25415, 57215, 11357);
            var near = Npc<HakrAdd>(25450, 57250, 11357); near.PackageID = T.HakrLinkedPackage;
            var far = Npc<HakrAdd>(30008, 56329, 11894);
            var live = new List<GameNPC> { hakr, near, far };
            Assert.That(T.ClearFirstOwner(far, live), Is.SameAs(hakr), "the far pair before the pull");
            Assert.That(T.ClearFirstOwner(near, live), Is.Null, "the linked pair is his fight");
            Assert.That(T.LiveRequiredAdds(hakr, live), Does.Contain(near));
            hakr.Fighting = true;
            Assert.That(T.ClearFirstOwner(far, live), Is.Null, "once he fights it is his fight's add");
        }

        [Test]
        public void TheRingsKeepCastersOutOfTheBossesOwnReach()
        {
            var hurika = Npc<Hurika>(50839, 35892, 16154);
            Vector3 b = At(hurika);
            T.BossRing ring = T.GlacierRings.Single(r => r.Boss == "Hurika");
            var step = T.RingAdvice(new(51839, 35892, 16154), b, hurika, ring, true, false);
            Assert.That(step?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(step.Value.StandAt.Value, b), Is.EqualTo(1_100 + T.SpreadStepOut).Within(1), "1,350: in spell range");
            Assert.That(T.RingAdvice(new(52300, 35892, 16154), b, hurika, ring, true, false), Is.Null, "already out");
            Assert.That(T.RingAdvice(new(50900, 35892, 16154), b, hurika, ring, false, false), Is.Null, "melee fight her");
            Assert.That(T.RingAdvice(new(50900, 35892, 16154), b, hurika, ring, true, true), Is.Null, "her target's party fights her");
            var caster = Npc<Raider>(51300, 35892, 16154);
            var live = new List<GameNPC> { hurika, caster };
            Assert.That(T.DungeonAdvice(new(caster, hurika, live, 0, true, 1_000, Wizard))?.Action, Is.EqualTo(T.BotAction.Reposition),
                "her patrol is off as shipped: the griffon rule has no opinion and the ring speaks");
            Assert.That(T.GlacierRings.Select(r => r.Boss), Is.EquivalentTo(new[] { "Hurika", "Torst", "Suttung" }));
        }

        [Test]
        public void IssordensSkeletonsAndHordsNeighboursComeAfterHim()
        {
            var issorden = Npc<Issorden>(54453, 37638, 11435);
            var skeleton = Npc<Mob>(40000, 30000, 11000); skeleton.PackageID = T.IssordenLinkedPackage;
            var hord = Npc<Hord>(21916, 40173, 13422);
            var ozur = Npc<Mob>(21892, 39430, 13421);
            var otrygg = Npc<Mob>(23938, 41960, 13412);
            var live = new List<GameNPC> { issorden, skeleton, hord, ozur, otrygg };
            Assert.That(T.ClearFirstOwner(skeleton, live), Is.SameAs(issorden), "they join him from anywhere in the glacier");
            Assert.That(T.PullWait(ozur, live, 1_000), Does.Contain("Hord"), "Ozur stands 743 from Hord (aggro 1,500)");
            Assert.That(T.PullWait(otrygg, live, 1_000), Is.Null, "2,700 from him");
            Assert.That(T.PullWait(hord, live, 1_000), Is.Null);
            hord.Fighting = true;
            Assert.That(T.PullWait(ozur, live, 1_000), Is.Null, "his fight is on");
        }

        [Test]
        public void OtryggsPetsAreNeverTheRaidsTarget()
        {
            var otrygg = Npc<Otrygg>(23938, 41960, 13412);
            var pet = Npc<OtryggAdd>(23990, 42000, 13412);
            var raider = Npc<Raider>(24100, 42000, 13412);
            var live = new List<GameNPC> { otrygg, pet, raider };
            var a = T.DungeonAdvice(new(raider, pet, live, 0, false, 1_000, Warrior));
            Assert.That((a?.Action, a?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)otrygg)));
            Assert.That(T.DungeonRoute(pet, live, out string why), Is.SameAs(otrygg), "a pet fighting the raid is still mapped to him");
            Assert.That(why, Does.Contain("Otrygg"));
        }

        [Test]
        public void GuthlacsIceSpikeIsEveryShootersTargetAndBirghirWaitsForHim()
        {
            var guthlac = Npc<Guthlac>(33580, 55666, 11887);
            var spike = Npc<FrozenBomb>(33900, 55900, 11887);
            var wizard = Npc<Raider>(33300, 55500, 11887);
            var live = new List<GameNPC> { guthlac, spike, wizard };
            var a = T.Decide(wizard, guthlac, live, 0, true, 1_000, Wizard);
            Assert.That((a.Action, a.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)spike)), "it erupts 25 s after it comes, 2,500 within 3,000");
        }

        [Test]
        public void SteinvorDiesFirstAndHisPillarIsSteppedOutOf()
        {
            var steinvor = Npc<Steinvor>(30416, 49307, 11384);
            var skuf = Npc<Skuf>(30504, 49838, 11383);
            var pillar = Npc<EffectMob>(30600, 49900, 11383);
            var raider = Npc<Raider>(30650, 49950, 11383);
            var live = new List<GameNPC> { steinvor, skuf, pillar, raider };
            var stepped = T.Decide(raider, skuf, live, 0, false, 1_000, Warrior);
            Assert.That(stepped.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(stepped.StandAt.Value, At(pillar)), Is.GreaterThan(350), "out of the Pillar of Frost");
            pillar.ObjectState = GameObject.eObjectState.Deleted;
            var a = T.Decide(raider, skuf, live, 0, false, 1_000, Warrior);
            Assert.That((a.Action, a.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)steinvor)), "Skuf's own party holds him");
            Assert.That(T.RouteTarget(skuf, live, At(raider), out _), Is.SameAs(steinvor));
        }

        [Test]
        public void TheGlacierChainsToKingTuscarAndQueenKula()
        {
            var king = Npc<KingTuscar>(34542, 57121, 11881);
            var kula = Npc<QueenKula>(34136, 57202, 12025);
            var agmundr = Npc<Agmundr>(15037, 50114, 12408);
            var hord = Npc<Hord>(21916, 40173, 13422);
            var live = new List<GameNPC> { king, kula, agmundr, hord };
            Assert.That(T.RouteTarget(king, live, At(king), out string why), Is.SameAs(kula), "Kula first (twin heal)");
            Assert.That(why, Does.Contain("after"));
            Assert.That(T.PullWait(king, live, 1_000), Is.Null, "no other boss's rule holds the final");
            Assert.That(T.PullWait(kula, live, 1_000), Is.Null);
            Assert.That(T.ClearFirstOwner(king, live), Is.Null);
            Assert.That(T.ClearFirstOwner(kula, live), Is.Null);
        }

        // ---- Galladoria ----------------------------------------------------------------------------------------------

        [Test]
        public void ThePrimalsFallInThePeriodsOrder()
        {
            UseRegion(T.GalladoriaRegion);
            var fire = Npc<FirePrimal>(39481, 63240, 11699);
            var earth = Npc<EarthPrimal>(39727, 62620, 11684);
            var water = Npc<WaterPrimal>(39547, 62071, 11688);
            var air = Npc<AirPrimal>(39713, 61264, 12372);
            var live = new List<GameNPC> { fire, earth, water, air };
            Assert.That(T.PrimalKillOrder, Is.EqualTo(new[] { "WaterPrimal", "FirePrimal", "EarthPrimal", "AirPrimal" }));
            Assert.That(T.DungeonRoute(air, live, out _), Is.SameAs(water));
            Assert.That(T.DungeonRoute(fire, live, out _), Is.SameAs(water));
            Assert.That(T.DungeonRoute(water, live, out _), Is.Null, "it is the first");
            water.ObjectState = GameObject.eObjectState.Deleted;
            Assert.That(T.DungeonRoute(earth, live, out string why), Is.SameAs(fire));
            Assert.That(why, Does.Contain("order"));
            fire.ObjectState = GameObject.eObjectState.Deleted;
            Assert.That(T.DungeonRoute(air, live, out _), Is.SameAs(earth));
            earth.ObjectState = GameObject.eObjectState.Deleted;
            Assert.That(T.DungeonRoute(air, live, out _), Is.Null, "the Air primal last");
        }

        [Test]
        public void TheAirPrimalIsShotFromTheMiddleAndTheWaterPrimalLeftAloneInItsPool()
        {
            UseRegion(T.GalladoriaRegion);
            var air = Npc<AirPrimal>(40355, 61543, 12372);
            var water = Npc<WaterPrimal>(39652, 60831, 11893);
            Vector3 airAt = At(air);
            Assert.That(T.AirPrimalAdvice(new(39900, 62000, 11685), airAt, air, true, water)?.Action, Is.EqualTo(T.BotAction.None),
                "in reach: shoot");
            var walk = T.AirPrimalAdvice(new(37000, 62000, 11685), airAt, air, true, water);
            Assert.That((walk?.Action, walk?.StandAt), Is.EqualTo(((T.BotAction?)T.BotAction.Reposition, (Vector3?)T.AirPrimalRingCentre)));
            Vector3[] ring = [new(39120, 61387, 12372), new(38531, 61871, 12372), new(38361, 62497, 12372), new(38525, 63092, 12372),
                new(38936, 63471, 12372), new(39462, 63707, 12372), new(40028, 63647, 12372), new(40633, 63236, 12372),
                new(40817, 62737, 12372), new(40760, 62068, 12372), new(40355, 61543, 12372)];
            Assert.That(ring.Max(p => Vector3.Distance(T.AirPrimalRingCentre, p)), Is.LessThan(T.AirPrimalShootReach),
                "the middle reaches its whole ring");
            var melee = T.AirPrimalAdvice(new(39900, 62000, 11685), airAt, air, false, water);
            Assert.That((melee?.Action, melee?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)water)));
            Assert.That(T.AirPrimalAdvice(new(39600, 62600, 11685), airAt, air, false, null)?.Action, Is.EqualTo(T.BotAction.StandOff));
            Assert.That(T.WaterPrimalAdvice(water, true)?.Action, Is.EqualTo(T.BotAction.StandOff));
            Assert.That(T.WaterPrimalAdvice(water, false), Is.Null);
            var raider = Npc<Raider>(39700, 60900, 11893);
            T.WaterPrimalRetreating = _ => true;
            Assert.That(T.DungeonAdvice(new(raider, water, new List<GameNPC> { water, raider }, 0, false, 1_000, Warrior))?.Action,
                Is.EqualTo(T.BotAction.StandOff));
            Assert.That(T.AllDungeonRules.Single(r => r.Encounter == "PrimalOrder").NoPets(air), Is.True, "pets do 5 to it");
        }

        [Test]
        public void OneBodyOfOlcasgeanAndHurionthexsShieldAndTheMechanismFromRange()
        {
            UseRegion(T.GalladoriaRegion);
            var main = Npc<Mob>(39237, 62644, 11685);
            Assert.That(T.OneBodyAdvice(main) is { Action: T.BotAction.Retarget } a && a.Target == main, Is.True);
            Assert.That(T.DungeonRulesFor(Npc<Olcasgean2Double>(40170, 62600, 11681)).Any(r => r.Encounter == nameof(Olcasgean2)), Is.True);

            var hurion = Npc<Mob>(55672, 43536, 12417);
            Vector3 h = At(hurion);
            var back = T.HurionthexAdvice(new(55800, 43536, 12417), h, hurion, true, false, false);
            Assert.That(back?.Action, Is.EqualTo(T.BotAction.StandOff));
            Assert.That(Flat(back.Value.StandAt.Value, h), Is.GreaterThan(T.GranidonStepBack));
            Assert.That(T.HurionthexAdvice(new(55800, 43536, 12417), h, hurion, false, false, false), Is.Null, "no shield: fight");
            Assert.That(T.HurionthexAdvice(new(55800, 43536, 12417), h, hurion, true, true, false), Is.Null, "casters fight on");
            Assert.That(T.HurionthexAdvice(new(55800, 43536, 12417), h, hurion, true, false, true), Is.Null, "his target's party holds him");

            var mech = Npc<Mob>(49406, 31276, 14392);
            var onTank = Npc<Mob>(49500, 31300, 14392);
            var onHealer = Npc<Mob>(49600, 31300, 14392);
            Vector3 m = At(mech);
            var feeder = T.MechanismAdvice(new(49450, 31276, 14392), m, mech, false, [onTank, onHealer], f => f == onHealer);
            Assert.That((feeder?.Action, feeder?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)onHealer)));
            var none = T.MechanismAdvice(new(49450, 31276, 14392), m, mech, false, [], _ => false);
            Assert.That(none?.Action, Is.EqualTo(T.BotAction.StandOff), "never a swing at its shield");
            Assert.That(Flat(none.Value.StandAt.Value, m), Is.GreaterThan(T.MechanismStandOff));
            Assert.That(T.MechanismAdvice(new(48600, 31276, 14392), m, mech, true, [onTank], _ => true), Is.Null, "casters and archers kill it");
            Assert.That(T.AllDungeonRules.Single(r => r.Encounter == nameof(OrganicEnergyMechanism)).NoPets(mech), Is.True);
        }

        public sealed class Olcasgean2Double : Olcasgean2 { public override bool IsAlive => true; }

        [Test]
        public void TheSporiteClusterIsFoughtInOneCircleAndOlcasarsMinionsAreHeld()
        {
            UseRegion(T.GalladoriaRegion);
            var cluster = Npc<GiantSporiteCluster>(37527, 48247, 13781);
            var copy = Npc<GSCAdds>(37560, 48280, 13781);
            Vector3 c = At(cluster);
            var stepIn = T.SporiteCircleAdvice(new(38327, 48247, 13781), c, cluster, true);
            Assert.That(stepIn?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(stepIn.Value.StandAt.Value, c), Is.EqualTo(T.SporiteCircleStepIn).Within(1));
            Assert.That(T.SporiteCircleAdvice(new(37827, 48247, 13781), c, cluster, true), Is.Null, "inside the circle");
            Assert.That(T.SporiteCircleAdvice(new(38327, 48247, 13781), c, cluster, false), Is.Null, "melee are on a body");
            var live = new List<GameNPC> { cluster, copy };
            Assert.That(T.DungeonRoute(copy, live, out _), Is.SameAs(cluster), "one health pool, one fight");
            Assert.That(T.For("GiantSporiteCluster") is { AreaOnClump: true }, Is.True, "the period: \"pbae\" the clump");

            var olcasar = Npc<OlcasarGeomancer>(39115, 36863, 14977);
            var minion = Npc<OGAdds>(39150, 36900, 14977);
            Assert.That(T.OlcasarMinionAdvice(minion, olcasar, true).Action, Is.EqualTo(T.BotAction.None), "its party holds it");
            var other = T.OlcasarMinionAdvice(minion, olcasar, false);
            Assert.That((other.Action, other.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)olcasar)));
            Assert.That(T.DungeonRoute(minion, new List<GameNPC> { olcasar, minion }, out _), Is.SameAs(olcasar),
                "a minion fighting the raid is still mapped to him: killed, it rises again");
        }

        // ---- Tur Suil, Trollheim -------------------------------------------------------------------------------------

        [Test]
        public void BalorsEyeIsOutRunAndHisTargetHoldsHim()
        {
            UseRegion(190);
            var balor = Npc<Balor>(32032, 31048, 15984);
            var eye = Npc<BalorEye>(32032, 31128, 16331);
            var victim = Npc<Raider>(32300, 31128, 15984);
            Vector3 e = At(eye);
            var run = T.BalorEyeAdvice(At(victim), e, balor, false);
            Assert.That(run?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(run.Value.StandAt.Value, e), Is.EqualTo(T.BalorEyeOutrun).Within(1));
            Assert.That(T.BalorEyeAdvice(new(34000, 31128, 15984), e, balor, false)?.Action, Is.EqualTo(T.BotAction.StandOff), "out: wait");
            Assert.That(T.BalorEyeAdvice(At(victim), e, balor, true), Is.Null, "his tank holds him");
            var live = new List<GameNPC> { balor, eye, victim };
            T.BalorEyeVictim = () => victim;
            Assert.That(T.DungeonAdvice(new(victim, balor, live, 0, false, 1_000, Warrior))?.Action, Is.EqualTo(T.BotAction.Reposition));
            T.BalorEyeVictim = () => null;
            Assert.That(T.DungeonAdvice(new(victim, balor, live, 0, false, 1_000, Warrior)), Is.Null);
        }

        [Test]
        public void NosdodensVoidIsSteppedOutOfAndHisCastersStandOff()
        {
            UseRegion(150);
            var nosdoden = Npc<Nosdoden>(37538, 12262, 13377);
            var victim = Npc<Raider>(37800, 12262, 13377);
            var neighbour = Npc<Raider>(37900, 12262, 13377);
            var caster = Npc<Raider>(38000, 12262, 13377);
            var live = new List<GameNPC> { nosdoden, victim, neighbour, caster };
            EncounterSignal s = EncounterSignals.Raise(nosdoden, EncounterSignals.Glare, victim, 550, true, 5_000, 0, 1_000);
            Vector3? step = T.SignalStep(neighbour, At(neighbour), s, false, Warrior);
            Assert.That(step, Is.Not.Null, "the raider beside the void's target steps out");
            Assert.That(Flat(step.Value, At(victim)), Is.GreaterThan(550));
            Assert.That(T.SignalStep(victim, At(victim), s, false, Warrior), Is.Null, "the target itself is healed first");
            EncounterSignals.ClearAll();
            var ring = T.DungeonAdvice(new(caster, nosdoden, live, 0, true, 1_000, Wizard));
            Assert.That(ring?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(ring.Value.StandAt.Value, At(nosdoden)), Is.GreaterThan(950));
            var essence = Npc<NosdodenSummonedAdds>(37600, 12300, 13377);
            live.Add(essence);
            Assert.That(T.DungeonAdvice(new(caster, nosdoden, live, 0, true, 1_000, Wizard)), Is.Null,
                "while a wave lives the raid is on the wave (required adds)");
        }
    }
}
