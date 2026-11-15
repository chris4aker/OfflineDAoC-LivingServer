using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// The 1.2 raid plans for Albion's big bosses that had only the generic dungeon route (owner 2026-11-14: every big boss
    /// is in Living Server 1.2): Caer Sidi's Apocalypse, Warlord Dorinakka, the Silencer, Lord Sanguis, the Bane of Hope,
    /// Fate's Chosen and the Spectral Provisioner; Krondon's Drihten Elreden, Deben se Gecynde, Beran, Fuladl, Fulafeallan,
    /// Lurfos, Ulor and Jehrog and Scurceol's orbs; Summoner's Hall's tether; Lord Gildas. The tests stand bosses, adds and
    /// raiders in a test region and play the rules; no world tick, combat, spell or pathing runs. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_AlbionBossPlans12
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 3000;
        private EpicTestServerScope _server;
        private Func<bool> _flying, _resisting;
        private Func<GameObject, Vector3, Vector3?> _snap;

        public class Mob : GameNPC
        {
            public bool Dead, Fighting;
            public byte Hurt;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
            public override byte HealthPercent => (byte)(100 - Hurt);
        }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }
        public sealed class TestApocalypse : Apocalypse
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestHarbinger : HarbringerOfFate
        {
            public bool Dead;
            public byte Hurt;
            public override bool IsAlive => !Dead;
            public override bool InCombat => true;
            public override byte HealthPercent => (byte)(100 - Hurt);
        }
        public sealed class TestDorinakka : DOL.GS.Scripts.WarlordDorinakka
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestSilencer : Silencer
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestBane : DOL.GS.Scripts.BaneOfHope
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestFate : FateChosen
        {
            public override bool IsAlive => true;
        }
        public sealed class TestProvisioner : DOL.GS.Scripts.SpectralProvisioner
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestLossren : SummonerLossren
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestSoul : TorturedSouls
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        /// <summary>Lossren and her soul under their own class names (the catalog matches class names).</summary>
        public static class Named
        {
            public sealed class SummonerLossren : DOL.GS.SummonerLossren
            {
                public override bool IsAlive => true;
                public override bool InCombat => true;
            }
            public sealed class TorturedSouls : DOL.GS.TorturedSouls
            {
                public override bool IsAlive => true;
                public override bool InCombat => true;
            }
        }
        /// <summary>An autonomous raid bot with the spells the test gives it (no brain, so it casts nothing).</summary>
        public sealed class HoldBot : GameBot
        {
            private HoldBot() : base((OfflineWorldBotRecord)null) { }
            public List<Spell> Known;
            public override bool IsAlive => true;
            public override List<Spell> Spells => Known ?? [];
        }
        public sealed class TestPet : GameNPC
        {
            public override bool IsAlive => true;
        }
        public sealed class TestGhoul : ExplodeUndead
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestDeben : DebenSeGecynde
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestMage : DebenMage
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestFighter : DebenFighter
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestBeran : BeranSupplyMaster
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestDrihten : DrihtenElreden
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class TestGildas : LordGildas
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }

        private static readonly int Warrior = (int)eCharacterClass.Warrior, Wizard = (int)eCharacterClass.Wizard,
            Cleric = (int)eCharacterClass.Cleric, Scout = (int)eCharacterClass.Scout;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            UseRegion(60);
            EpicEncounterMechanics.ResetPerEncounter();
            _flying = T.ApocalypseFlying;
            _resisting = T.SilencerResisting;
            _snap = T.AlbionFloorSnap;
            T.ApocalypseFlying = () => false;
            T.SilencerResisting = () => false;
            T.AlbionFloorSnap = (_, _) => null;
            T.ResetAttackerSlotsForTests();
        }

        [TearDown]
        public void TearDown()
        {
            T.ApocalypseFlying = _flying;
            T.SilencerResisting = _resisting;
            T.AlbionFloorSnap = _snap;
            T.ResetAttackerSlotsForTests();
            T.ResetKeptSoulsForTests();
            EpicEncounterMechanics.ResetPerEncounter();
            _server.Dispose();
        }

        private void UseRegion(ushort id)
        {
            _region = new Region(new RegionData { Id = id, Name = "Albion plans test", Description = "Albion plans test", Mobs = [] });
            _region.Zones.Add(new Zone(_region, (ushort)(id * 10), "Albion plans test", 0, 0, 65536, 65536, (ushort)(id * 10 + 1),
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

        private static T.Advice? Rule(GameNPC raider, GameNPC target, List<GameNPC> live, bool ranged, int classId, int party = 0) =>
            T.DungeonAdvice(new(raider, target, live, party, ranged, 1_000, classId));

        // ---- the shared pieces ----------------------------------------------------------------------------------------

        [Test]
        public void TheFrontArcAndTheRingSpotsArePure()
        {
            Vector3 boss = new(0, 0, 0), facing = new(100, 0, 0);
            Assert.That(T.InFrontArc(boss, facing, new(100, 30, 0), T.FrontArcDegrees), Is.True, "beside his target");
            Assert.That(T.InFrontArc(boss, facing, new(-100, 10, 0), T.FrontArcDegrees), Is.False, "behind him");
            Assert.That(T.InFrontArc(boss, facing, new(0, 100, 0), T.FrontArcDegrees), Is.False, "at his side");
            Vector3 a = T.PartyRingSpot(boss, 0, 8, 1_000, 0), b = T.PartyRingSpot(boss, 1, 8, 1_000, 0);
            Assert.That(a.X, Is.EqualTo(1_000).Within(0.5));
            Assert.That(Flat(a, b), Is.GreaterThan(700), "eight spots 1,000 out lie 765 apart: a 600 area hit takes one");
            Assert.That(T.PartyRingSpot(boss, 9, 8, 1_000, 0), Is.EqualTo(T.PartyRingSpot(boss, 1, 8, 1_000, 0)));
        }

        [Test]
        public void MeleeInFrontOfAParryingBossStepBehindHim()
        {
            var lord = Npc<TestDorinakka>(35625, 41676, 15631);
            var tank = Npc<Raider>(35725, 41676, 15631);
            var melee = Npc<Raider>(35700, 41700, 15631);
            var back = Npc<Raider>(35520, 41680, 15631);
            lord.TargetObject = tank;
            var live = new List<GameNPC> { lord, tank, melee, back };
            var step = Rule(melee, lord, live, false, Warrior);
            Assert.That(step?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(step.Value.StandAt.Value.X, Is.LessThan(lord.X), "behind him, on the line from his target");
            Assert.That(Flat(step.Value.StandAt.Value, At(lord)), Is.EqualTo(T.BehindStepDistance).Within(1));
            Assert.That(Rule(back, lord, live, false, Warrior), Is.Null, "already behind: fight");
            Assert.That(Rule(tank, lord, live, false, Warrior), Is.Null, "his target faces him");
            Assert.That(Rule(melee, lord, live, true, Wizard), Is.Null, "casters are not moved");
        }

        // ---- Apocalypse -----------------------------------------------------------------------------------------------

        [Test]
        public void WhileApocalypseFliesTheRaidLeavesTheRainButNotThroughTheDoor()
        {
            var apoc = Npc<TestApocalypse>(29467, 25235, 19490);
            var raider = Npc<Raider>(29500, 25400, 19490);
            var live = new List<GameNPC> { apoc, raider };
            T.ApocalypseFlying = () => true;
            var a = Rule(raider, apoc, live, false, Warrior);
            Assert.That(a?.Action, Is.EqualTo(T.BotAction.Reposition));
            Vector3 spot = a.Value.StandAt.Value;
            Assert.That(Flat(spot, T.ApocRainSpot), Is.EqualTo(T.ApocFlyRing).Within(1), "out of the 800 under him");
            Assert.That(Flat(spot, T.HorsemenRoom.Door), Is.GreaterThan(T.ApocDoorClear), "the barrier would move it back under the rain");
            // A raider straight on the line to the door is turned aside.
            Vector3? doorward = T.ApocFlyStep(new(29462, 26000, 19490));
            Assert.That(Flat(doorward.Value, T.HorsemenRoom.Door), Is.GreaterThan(T.ApocDoorClear));
            Assert.That(T.ApocFlyStep(new(29467, 26600, 19490)), Is.Null, "already out of the rain");
            var clear = Npc<Raider>(30600, 25335, 19490);
            live.Add(clear);
            Assert.That(Rule(clear, apoc, live, false, Warrior)?.Action, Is.EqualTo(T.BotAction.StandOff), "nobody keeps him while he flies");
            if (T.BarrierMovesBots(T.HorsemenRoom))
            {
                Assert.That(T.KeepInsideFor(apoc, spot), Is.EqualTo(spot), "the room keeps the fly ring while he flies");
                T.ApocalypseFlying = () => false;
                Assert.That(Flat(T.KeepInsideFor(apoc, spot), T.HorsemenRoom.Middle), Is.LessThanOrEqualTo(T.HorsemenRoom.InsideRadius + 1),
                    "on the ground the room's 800 again");
            }
        }

        [Test]
        public void OnTheGroundEveryoneButHisPartyKillsTheHarbingersFirst()
        {
            var apoc = Npc<TestApocalypse>(29467, 25235, 19490);
            var tank = Npc<Raider>(29467, 25335, 19490);
            var melee = Npc<Raider>(29400, 25300, 19490);
            var h1 = Npc<TestHarbinger>(29700, 25235, 19490);
            var h2 = Npc<TestHarbinger>(29300, 25200, 19490);
            h2.Hurt = 40;
            apoc.TargetObject = tank;
            var live = new List<GameNPC> { apoc, tank, melee, h1, h2 };
            Assert.That(Rule(tank, apoc, live, false, Warrior), Is.Null, "the party he fights holds him");
            var a = Rule(melee, apoc, live, false, Warrior);
            Assert.That((a?.Action, a?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)h2)), "the most hurt Harbinger first");
            h1.Dead = h2.Dead = true;
            Assert.That(Rule(melee, apoc, live, false, Warrior), Is.Null, "none left: melee on him");
            var wizard = Npc<Raider>(29467, 25235, 19490);
            live.Add(wizard);
            var ring = Rule(wizard, apoc, live, true, Wizard);
            Assert.That(ring?.Action, Is.EqualTo(T.BotAction.Reposition), "the back line to its party's spot");
            Assert.That(Flat(ring.Value.StandAt.Value, T.HorsemenRoom.Middle), Is.EqualTo(T.ApocPartyRing).Within(2));
        }

        // ---- Warlord Dorinakka ----------------------------------------------------------------------------------------

        /// <summary>The world database's twelve DorinakkaBaf crypt knights (two columns of six up the hall to him).</summary>
        private static readonly (int X, int Y)[] DorinakkaKnightSpawns =
        [
            (35303, 40668), (35300, 40861), (35303, 41054), (35304, 41240), (35306, 41439), (35308, 41627),
            (35978, 40673), (35977, 40869), (35981, 41062), (35983, 41251), (35980, 41444), (35983, 41632),
        ];

        private Mob Knight(int x, int y)
        {
            var knight = Npc<Mob>(x, y, 15631);
            knight.PackageID = T.DorinakkaKnights;
            knight.Name = "crypt knight";
            return knight;
        }

        [Test]
        public void NoDorinakkaKnightIsPulledOnItsOwnWhileHeLives()
        {
            // Owner's PC, build 1dc66b6, 2026-11-15: the second knight cleared "first" brought him and the ten left.
            var lord = Npc<TestDorinakka>(35625, 41676, 15631);
            var live = new List<GameNPC> { lord };
            Mob[] knights = DorinakkaKnightSpawns.Select(s => Knight(s.X, s.Y)).ToArray();
            live.AddRange(knights);
            foreach (Mob knight in knights)
            {
                Assert.That(T.ClearFirstOwner(knight, live), Is.Null, $"{knight.X},{knight.Y}: no knight is a clear-first of its own");
                Assert.That(T.DungeonRoute(knight, live, out string why), Is.SameAs(lord), $"{knight.X},{knight.Y}: the route musters for him");
                Assert.That(why, Does.StartWith("dorinakka-pack"));
                Assert.That(T.RouteTarget(knight, live, At(lord), out _), Is.SameAs(lord));
            }
            Assert.That(T.ClearFirstOwner(lord, live), Is.Null);
            Assert.That(T.DungeonRoute(lord, live, out _), Is.Null, "he himself is fought");
            knights[6].Fighting = true;
            Assert.That(T.DungeonRoute(knights[6], live, out _), Is.Null, "a knight already fighting the raid is answered");
            // The thirteenth crypt knight (no PackageID, a plain monster beside the hall) is not his.
            var stray = Npc<Mob>(35073, 41287, 15631);
            stray.Name = "crypt knight";
            live.Add(stray);
            Assert.That(T.DungeonRoute(stray, live, out _), Is.Null);
            // Without him the knights are ordinary targets again.
            var alone = new List<GameNPC>(knights);
            Assert.That(T.DungeonRoute(knights[0], alone, out _), Is.Null);
        }

        [Test]
        public void DorinakkasMusterLiesInsideHisTether()
        {
            // The knights' own musters lay 2,250 from him (02:44:03, 35,274,39,462): he chased the raid out of his 2,000
            // tether, immune, nine times. His own muster is 1,400 from his spawn.
            T.Leash leash = T.Leashes.Single(l => l.Boss == nameof(DOL.GS.Scripts.WarlordDorinakka));
            Assert.That(leash.Resets, Is.True);
            Assert.That(RealmRaidDungeonRoute.FinalMusterStandOff, Is.LessThan(leash.TetherRange));
            Assert.That(RealmRaidDungeonRoute.MusterReference(new(35000, 39600, 15631), new(35625, 41676, 15631), leash.TetherRange, leash.Resets),
                Is.EqualTo(new Vector3(35625, 41676, 15631)), "out chasing raiders, the raid musters for him at home");
        }

        [Test]
        public void InDorinakkasFightEveryoneButHisPartyAndTheHealersKillsTheKnightsOneAtATime()
        {
            var lord = Npc<TestDorinakka>(35625, 41676, 15631);
            var tank = Npc<Raider>(35725, 41676, 15631);
            var wizard = Npc<Raider>(35300, 42200, 15631);
            var warrior = Npc<Raider>(35640, 41300, 15631);
            var scout = Npc<Raider>(35500, 41000, 15631);
            var cleric = Npc<Raider>(35600, 41100, 15631);
            lord.TargetObject = tank;
            Mob a = Knight(35308, 41627), b = Knight(35983, 41632), sleeping = Knight(35303, 40668), mezzed = Knight(35306, 41439);
            a.Fighting = b.Fighting = mezzed.Fighting = true;
            b.Hurt = 30;
            mezzed.Hurt = 60;
            mezzed.IsMezzed = true;
            var live = new List<GameNPC> { lord, tank, wizard, warrior, scout, cleric, a, b, sleeping, mezzed };
            Assert.That(Rule(tank, lord, live, false, Warrior), Is.Null, "the party he fights holds him");
            foreach (var (raider, ranged, cls) in new[] { (wizard, true, Wizard), (warrior, false, Warrior), (scout, true, Scout) })
            {
                var advice = Rule(raider, lord, live, ranged, cls);
                Assert.That((advice?.Action, advice?.Target), Is.EqualTo(((T.BotAction?)T.BotAction.Retarget, (GameNPC)b)),
                    $"class {cls}: the most hurt knight awake, so the raid's damage lands on one; the mesmerized one waits");
                Assert.That(advice.Value.Reason, Does.StartWith("dorinakka-knights"));
            }
            Assert.That(Rule(cleric, lord, live, true, Cleric), Is.Null, "healers heal");
            b.Dead = a.Dead = true;
            Assert.That(Rule(warrior, lord, live, false, Warrior)?.Target, Is.SameAs(mezzed), "the mesmerized knight once it is the last");
            mezzed.Dead = true;
            Assert.That(Rule(wizard, lord, live, true, Wizard), Is.Null, "a knight out of combat is not his fight's: everyone on him");
            var front = Npc<Raider>(35700, 41700, 15631);
            live.Add(front);
            Assert.That(Rule(front, lord, live, false, Warrior)?.Action, Is.EqualTo(T.BotAction.Reposition), "his melee from behind him");
        }

        // ---- the Silencer --------------------------------------------------------------------------------------------

        [Test]
        public void AtMostNineRaidersHitTheSilencer()
        {
            var silencer = Npc<TestSilencer>(31035, 36303, 15621);
            var tank = Npc<Raider>(31100, 36303, 15621);
            silencer.TargetObject = tank;
            var live = new List<GameNPC> { silencer, tank };
            var raiders = Enumerable.Range(0, T.SilencerAttackerCap + 2).Select(i => Npc<Raider>(31200 + i * 10, 36400, 15621)).ToList();
            live.AddRange(raiders);
            for (int i = 0; i < T.SilencerAttackerCap; i++)
                Assert.That(Rule(raiders[i], silencer, live, false, Warrior), Is.Null, $"attacker {i + 1} fights");
            var tenth = Rule(raiders[T.SilencerAttackerCap], silencer, live, false, Warrior);
            Assert.That(tenth?.Action, Is.EqualTo(T.BotAction.StandOff), "a tenth makes his chance 1% a hit");
            Assert.That(Flat(tenth.Value.StandAt.Value, At(silencer)), Is.EqualTo(T.SilencerStandOff).Within(1));
            Assert.That(Rule(raiders[0], silencer, live, false, Warrior), Is.Null, "a raider keeps its place while it fights");
            Assert.That(Rule(tank, silencer, live, false, Warrior), Is.Null, "the party he fights always does");
            Assert.That(T.TakeAttackerSlot(silencer, raiders[0], T.SilencerAttackerCap, 1_000 + T.SilencerSlotMilliseconds + 1, ask: false),
                Is.False, "a place nobody asked for in 8 s is freed");
            T.SilencerResisting = () => true;
            Assert.That(Rule(raiders[1], silencer, live, false, Warrior)?.Action, Is.EqualTo(T.BotAction.StandOff), "his 99%: hold");
            Assert.That(Rule(tank, silencer, live, false, Warrior), Is.Null, "his target's party keeps him");
            Assert.That(T.AllDungeonRules.Single(r => r.Encounter == nameof(Silencer)).NoPets(silencer), Is.True, "no pets on him");
        }

        // ---- the Bane of Hope, Fate's Chosen, Lord Sanguis ------------------------------------------------------------------

        [Test]
        public void FatesChosenDiesBeforeTheBaneAndTheBanesBackLineSpreads()
        {
            var bane = Npc<TestBane>(31265, 30833, 13954);
            var fate = Npc<TestFate>(37500, 30162, 13969);
            var live = new List<GameNPC> { bane, fate };
            Assert.That(T.ClearFirstOwner(fate, live), Is.SameAs(bane), "it stands 123 from a spot the Bane throws raiders to");
            var tank = Npc<Raider>(31300, 30833, 13954);
            var healer = Npc<Raider>(31265, 30900, 13954);
            bane.TargetObject = tank;
            live.AddRange([tank, healer]);
            var a = Rule(healer, bane, live, false, Cleric);
            Assert.That(a?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(a.Value.StandAt.Value, At(bane)), Is.EqualTo(T.BaneRing).Within(2));
        }

        [Test]
        public void SanguisBloodMagesAreTheCastersAndTheNewRowsAreInTheCatalog()
        {
            Assert.That(T.For(nameof(LordSanguis))?.RangedAdds, Does.Contain(nameof(BloodMage)));
            Assert.That(T.For(nameof(LichLordSanguis))?.RangedAdds, Does.Contain(nameof(BloodMage)));
            Assert.That(T.For(nameof(Fuladl))?.RequiredAdds, Is.EqualTo(new[] { nameof(FuladlAdd) }), "95% while a part lives");
            Assert.That(T.For(nameof(Fulafeallan))?.RequiredAdds, Is.EqualTo(new[] { nameof(FulafeallanAdd) }));
            Assert.That(T.For(nameof(JehrogBysen))?.After, Is.EqualTo(nameof(UlorBysen)), "Ulor first (period)");
            Assert.That(T.For(nameof(LurfosHerald)) is { SpreadRadius: 300, MeleeBehind: true, TanksTaunt: true }, Is.True);
            Assert.That(T.For(nameof(ScurceolHyrde))?.AddsInOrder, Is.True, "one orb at a time");
            Assert.That(T.For(nameof(BeranSupplyMaster)) is { HasHazards: true, HazardRadius: 1000 }, Is.True);
            foreach (string summoner in new[] { nameof(SummonerRoesia), nameof(SummonerCunovinda), nameof(SummonerLossren), nameof(GrandSummonerGovannon) })
                Assert.That(T.For(summoner)?.FightAtHomeRadius, Is.EqualTo(1000), summoner);
            Assert.That(T.For(nameof(SummonerLossren))?.RangedAdds, Is.EqualTo(new[] { nameof(ExplodeUndead), nameof(TorturedSouls) }));
        }

        // ---- the Spectral Provisioner ---------------------------------------------------------------------------------------

        [Test]
        public void TheProvisionerIsAmbushedOnHisLoopAndNoBotWastesHeatSpiritOrColdOnHim()
        {
            Assert.That(T.ProvisionerStations.Length, Is.EqualTo(7), "six points (his cross's middle twice, 171 apart; point2 and point8 89 apart) and one long leg");
            for (int i = 0; i < T.ProvisionerStations.Length; i++)
                for (int j = 0; j < i; j++)
                    Assert.That(Flat(T.ProvisionerStations[i], T.ProvisionerStations[j]), Is.GreaterThan(300));
            var provisioner = Npc<TestProvisioner>(30940, 39418, 17004);
            var raider = Npc<Raider>(30050, 39425, 17004);
            var live = new List<GameNPC> { provisioner, raider };
            int party = Array.FindIndex(T.ProvisionerStations, s => Flat(s, new(30050, 39425, 17004)) < 1);
            Assert.That(Rule(raider, provisioner, live, false, Warrior, party)?.Action, Is.EqualTo(T.BotAction.Reposition),
                "890 off: the melee wait at the station");
            Assert.That(Rule(raider, provisioner, live, true, Wizard, party)?.Action, Is.EqualTo(T.BotAction.None),
                "within 1,400 of the station: the casters fire");
            var rule = T.AllDungeonRules.Single(r => r.Encounter == nameof(DOL.GS.Scripts.SpectralProvisioner));
            Assert.That(rule.Forbids, Is.EquivalentTo(new[] { eDamageType.Heat, eDamageType.Spirit, eDamageType.Cold }));
            var manifestation = Npc<Mob>(29979, 39429, 17006); manifestation.PackageID = T.ProvisionerLevel;
            live.Add(manifestation);
            Assert.That(T.ClearFirstOwner(manifestation, live), Is.SameAs(provisioner), "his level dies first");
        }

        // ---- Summoner's Hall -----------------------------------------------------------------------------------------------

        [Test]
        public void TheRaidFightsASummonerInsideHerTether()
        {
            UseRegion(248);
            var lossren = Npc<TestLossren>(39276, 41166, 16001);
            // The Hibernia raid's muster, PC log e3b8238: 1,388 from her spawn.
            var caster = Npc<Raider>(37889, 41358, 16001);
            var chased = Npc<Raider>(38200, 41300, 16001);
            lossren.TargetObject = chased;
            var live = new List<GameNPC> { lossren, caster, chased };
            var a = Rule(caster, lossren, live, true, Wizard);
            Assert.That(a?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(a.Value.StandAt.Value, At(lossren)), Is.LessThan(T.SummonerInnerRadius), "in to fight within 900 of her home");
            Assert.That(Rule(chased, lossren, live, true, Wizard), Is.Null, "the one she chases is the catalog's: it leads her home");
            var near = Npc<Raider>(39000, 41166, 16001);
            live.Add(near);
            Assert.That(Rule(near, lossren, live, false, Warrior), Is.Null, "already inside");
            // On one of her souls the soul's summoner counts.
            var soul = Npc<TestSoul>(38505, 41211, 16001);
            live.Add(soul);
            Assert.That(Rule(caster, soul, live, false, Warrior)?.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(T.SummonerStepIn(new(39276, 42500, 16001), new(39276, 41166, 16001)).Value.Y,
                Is.EqualTo(41166 + T.SummonerInnerRadius - T.CloseInInset).Within(1));
        }

        [Test]
        public void LossrensGhoulIsEveryCastersFirst()
        {
            UseRegion(248);
            var lossren = Npc<TestLossren>(39276, 41166, 16001);
            var wizard = Npc<Raider>(39000, 41166, 16001);
            var warrior = Npc<Raider>(39200, 41166, 16001);
            var soul = Npc<TestSoul>(39189, 41500, 16000);
            var live = new List<GameNPC> { lossren, wizard, warrior, soul };
            var ghoul = Npc<TestGhoul>(39745, 41176, 16001);
            live.Add(ghoul);
            Assert.That(Rule(wizard, lossren, live, true, Wizard)?.Target, Is.SameAs(ghoul));
            Assert.That(Rule(wizard, soul, live, true, Wizard)?.Target, Is.SameAs(ghoul), "from a soul too");
            Assert.That(Rule(warrior, lossren, live, false, Warrior), Is.Null, "melee stay on her");
        }

        [Test]
        public void OneOfLossrensSoulsIsLeftAliveSoNoNewWaveComes()
        {
            // Owner's PC, build 1dc66b6, 2026-11-15: she calls a new wave only when none of hers lives; the raid cleared 14 and
            // 18 waves, and the souls killed 98 of the 170 who fell. One soul of hers is left alive.
            UseRegion(248);
            var lossren = Npc<Named.SummonerLossren>(39276, 41166, 16001);
            var warrior = Npc<Raider>(39200, 41166, 16001);
            var wizard = Npc<Raider>(39000, 41166, 16001);
            var first = Npc<Named.TorturedSouls>(39189, 41500, 16000);
            var second = Npc<Named.TorturedSouls>(38900, 41211, 16001);
            var live = new List<GameNPC> { lossren, warrior, wizard, first, second };
            Assert.That(Rule(warrior, lossren, live, false, Warrior), Is.Null, "on her: the melee fight her");
            Assert.That(T.LeftAlive(first), Is.True, "her first soul (whole, lowest id) is kept");
            Assert.That(T.LeftAlive(second), Is.False, "the rest are killed");
            var a = Rule(warrior, first, live, false, Warrior);
            Assert.That((a?.Action, a?.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)lossren)), "a raider on it goes back to her");
            Assert.That(a.Value.Reason, Does.StartWith("lossren-kept-soul"));
            Assert.That(Rule(warrior, second, live, false, Warrior), Is.Null, "any other soul is fought");
            Assert.That(T.LiveRangedAdds(lossren, live), Is.EqualTo(new GameNPC[] { second }), "the casters' list skips it");
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            Assert.That(T.MustNotHit(bot, first), Is.True, "no bot hits it, area spells included");
            Assert.That(T.MustNotHit(bot, second), Is.False);
            Assert.That(T.MustNotHit(bot, lossren), Is.False);

            // While the ghoul lives a caster on the kept soul takes the ghoul.
            var ghoul = Npc<TestGhoul>(39745, 41176, 16001);
            live.Add(ghoul);
            Assert.That(Rule(wizard, first, live, true, Wizard)?.Target, Is.SameAs(ghoul));
            Assert.That(Rule(warrior, first, live, false, Warrior)?.Target, Is.SameAs(lossren));

            // The kept soul died anyway (a real player's hit): one of the souls still alive is kept instead.
            first.ObjectState = GameObject.eObjectState.Inactive;
            Assert.That(T.LeftAlive(second), Is.False, "chosen on a raider's turn, not by the check");
            Assert.That(Rule(warrior, lossren, live, false, Warrior), Is.Null);
            Assert.That(T.LeftAlive(second), Is.True);
            // Her reset or death ends it.
            lossren.ObjectState = GameObject.eObjectState.Inactive;
            Assert.That(T.LeftAlive(second), Is.False);
        }

        [Test]
        public void TheKeptSoulIsTheHealthiestThenTheLowestId()
        {
            UseRegion(248);
            var hurt = Npc<Mob>(0, 0, 0); hurt.Hurt = 40;
            var whole = Npc<Mob>(0, 0, 0);
            var wholeLater = Npc<Mob>(0, 0, 0);
            Assert.That(T.ChooseKeptSoul(new GameNPC[] { wholeLater, hurt, whole }), Is.SameAs(whole));
            Assert.That(T.ChooseKeptSoul(new GameNPC[] { hurt }), Is.SameAs(hurt));
            Assert.That(T.ChooseKeptSoul(new GameNPC[0]), Is.Null);
            Assert.That(T.ChooseKeptSoul(null), Is.Null);
        }

        [Test]
        public void OnlyLossrensSoulsAreLeftAlive()
        {
            var rules = T.AllDungeonRules.Where(r => r.LeaveAlive != null).ToArray();
            Assert.That(rules.Select(r => r.Encounter), Is.EqualTo(new[] { "SummonerLossrenKeptSoul" }));
            Assert.That(rules[0].Targets, Does.Contain(nameof(TorturedSouls)));
            // Her ghoul and every other monster are fought as before.
            UseRegion(248);
            var lossren = Npc<TestLossren>(39276, 41166, 16001);
            var ghoul = Npc<TestGhoul>(39745, 41176, 16001);
            var warrior = Npc<Raider>(39200, 41166, 16001);
            var live = new List<GameNPC> { lossren, ghoul, warrior };
            Assert.That(Rule(warrior, ghoul, live, false, Warrior), Is.Null, "no soul: nothing kept");
            Assert.That(T.LeftAlive(ghoul), Is.False);
            Assert.That(T.LeftAlive(lossren), Is.False);
        }

        // ---- Lossren's kept soul held mesmerized (owner approved 2026-11-15) -------------------------------------------

        private static Spell Hold(eSpellType type, string target = "Enemy", int radius = 0, double damage = 0, double value = 0,
            int duration = 60, int level = 40, int sub = 0) => new(new DbSpell
        {
            SpellID = 9_300 + level, Type = type.ToString(), Target = target, Range = 1500, Radius = radius, Damage = damage,
            Value = value, Duration = duration, SubSpellID = sub, CastTime = 3
        }, level);

        [Test]
        public void TheKeptSoulsHoldIsAMesmerizeThenAStunThenARootDamageDoesNotBreak()
        {
            Spell mez = Hold(eSpellType.Mesmerize), stun = Hold(eSpellType.Stun, duration: 9),
                root = Hold(eSpellType.UnbreakableSpeedDecrease, value: 99);
            Assert.That(T.HoldRank(mez), Is.EqualTo(0), "the period's way: mesmerize her adds");
            Assert.That(T.HoldRank(Hold(eSpellType.Mez)), Is.EqualTo(0));
            Assert.That(T.HoldRank(stun), Is.EqualTo(1));
            Assert.That(T.HoldRank(root), Is.EqualTo(2));
            Assert.That(T.HoldRank(Hold(eSpellType.Mesmerize, radius: 350)), Is.EqualTo(-1), "an area mesmerize reaches her other souls");
            Assert.That(T.HoldRank(Hold(eSpellType.Stun, damage: 50)), Is.EqualTo(-1), "no damage on it");
            Assert.That(T.HoldRank(Hold(eSpellType.Stun, sub: 1234)), Is.EqualTo(-1), "nor a sub spell's");
            Assert.That(T.HoldRank(Hold(eSpellType.SpeedDecrease, value: 99)), Is.EqualTo(-1), "a root damage breaks is no hold");
            Assert.That(T.HoldRank(Hold(eSpellType.UnbreakableSpeedDecrease, value: 50)), Is.EqualTo(-1), "a snare is no hold");
            Assert.That(T.HoldRank(Hold(eSpellType.DirectDamage, damage: 100)), Is.EqualTo(-1));
            Assert.That(T.HoldRank(null), Is.EqualTo(-1));

            Assert.That(T.BestHoldSpell(new[] { root, stun, mez }, 50), Is.SameAs(mez));
            Assert.That(T.BestHoldSpell(new[] { root, stun }, 50), Is.SameAs(stun));
            Assert.That(T.BestHoldSpell(new[] { root, stun, mez }, 30), Is.Null, "above the bot's level");
            Spell longer = Hold(eSpellType.Mesmerize, duration: 80, level: 45);
            Assert.That(T.BestHoldSpell(new[] { mez, longer }, 50), Is.SameAs(longer));
            Assert.That(T.BestHoldSpell(null, 50), Is.Null);

            // Cast again before it wakes: with at most its cast time and the lead left, never earlier.
            Assert.That(T.HoldDue(0, 3_000), Is.True, "not held");
            Assert.That(T.HoldDue(3_000 + T.KeptSoulRecastLeadMilliseconds, 3_000), Is.True, "lands before it ends");
            Assert.That(T.HoldDue(3_001 + T.KeptSoulRecastLeadMilliseconds, 3_000), Is.False, "too early (an early recast is shorter)");
        }

        [Test]
        public void OneCrowdControlBotHoldsTheKeptSoulAMesmerizerFirst()
        {
            UseRegion(248);
            var lossren = Npc<Named.SummonerLossren>(39276, 41166, 16001);
            var stunner = Npc<Raider>(39000, 41166, 16001);
            var mezzer = Npc<Raider>(39100, 41166, 16001);
            var otherMezzer = Npc<Raider>(39150, 41166, 16001);
            Assert.That(T.HoldsKeptSoul(lossren, stunner, 1, 1_000), Is.True, "no mesmerizer yet: a stun holds it");
            Assert.That(T.HoldsKeptSoul(lossren, stunner, 1, 2_000), Is.True, "its own turn keeps its place");
            Assert.That(T.HoldsKeptSoul(lossren, mezzer, 0, 3_000), Is.True, "a mesmerizer takes over");
            Assert.That(T.HoldsKeptSoul(lossren, stunner, 1, 3_500), Is.False);
            Assert.That(T.HoldsKeptSoul(lossren, otherMezzer, 0, 4_000), Is.False, "one holder: the second mesmerizer fights");
            Assert.That(T.HoldsKeptSoul(lossren, mezzer, 0, 5_000), Is.True);
            Assert.That(T.HoldsKeptSoul(lossren, stunner, -1, 6_000), Is.False, "no hold: never the holder");

            mezzer.IsMezzed = true;
            Assert.That(T.HoldsKeptSoul(lossren, otherMezzer, 0, 6_000), Is.True, "a holder held itself gives up its place");
            mezzer.IsMezzed = false;
            Assert.That(T.HoldsKeptSoul(lossren, mezzer, 0, 6_500), Is.False);
            Assert.That(T.HoldsKeptSoul(lossren, mezzer, 0, 6_000 + T.KeptSoulHolderStaleMilliseconds + 1), Is.True,
                "a holder whose turn stops coming is replaced");
            mezzer.ObjectState = GameObject.eObjectState.Inactive;
            Assert.That(T.HoldsKeptSoul(lossren, otherMezzer, 0, 17_500), Is.True, "a holder gone is replaced");
        }

        [Test]
        public void WithNoCrowdControlBotTheKeptSoulIsOnlyLeftAlone()
        {
            var rules = T.AllDungeonRules.Where(r => r.Control != null).ToArray();
            Assert.That(rules.Select(r => r.Encounter), Is.EqualTo(new[] { "SummonerLossrenKeptSoul" }), "only her kept soul is held");
            UseRegion(248);
            var lossren = Npc<Named.SummonerLossren>(39276, 41166, 16001);
            var soul = Npc<Named.TorturedSouls>(39189, 41500, 16000);
            var warrior = Npc<HoldBot>(39200, 41166, 16001);
            warrior.Known = [Hold(eSpellType.DirectDamage, damage: 100, level: 1), Hold(eSpellType.SpeedDecrease, value: 99, level: 1)];
            var live = new List<GameNPC> { lossren, soul, warrior };
            Assert.That(rules[0].Control(warrior, lossren, live), Is.False, "no hold: it fights on");
            Assert.That(T.LeftAlive(soul), Is.True, "the soul is kept all the same");
            var stunner = Npc<Raider>(39000, 41166, 16001);
            Assert.That(T.HoldsKeptSoul(lossren, stunner, 1, 1_000), Is.True, "nobody took the holder's place");
        }

        [Test]
        public void NoAreaSpellPetOrDamageOverTimeBreaksTheKeptSoulsMesmerize()
        {
            UseRegion(248);
            var lossren = Npc<Named.SummonerLossren>(39276, 41166, 16001);
            var kept = Npc<Named.TorturedSouls>(39189, 41500, 16000);
            var other = Npc<Named.TorturedSouls>(38900, 41211, 16001);
            var warrior = Npc<Raider>(39200, 41166, 16001);
            var live = new List<GameNPC> { lossren, kept, other, warrior };
            Assert.That(Rule(warrior, lossren, live, false, Warrior), Is.Null);
            Assert.That(T.LeftAlive(kept), Is.True);
            kept.IsMezzed = true;

            // Area spells: AddBlastCandidate marks a mesmerized monster and one a bot must not hit; either vetoes the blast.
            var bot = Npc<HoldBot>(39000, 41166, 16001);
            Assert.That(BotBrain.IsBreakablyControlled(kept), Is.True);
            Assert.That(T.MustNotHit(bot, kept), Is.True, "no spell, damage over time included (CanCastOffensiveSpell and " +
                "CheckInstantOffensiveSpells ask it), no swing (GameBot.StartAttack), no area spell");
            Assert.That(T.MustNotHit(bot, other), Is.False, "her other souls are fought");
            var blast = BotAoePolicy.Evaluate(new[] { new BotAoePolicy.Candidate(100, true, false),
                new BotAoePolicy.Candidate(150, true, BotBrain.IsBreakablyControlled(kept) || T.MustNotHit(bot, kept)) }, 300, 350, false);
            Assert.That(blast.Vetoed, Is.True, "an area spell on a soul beside it is not cast");

            // Pets: a bot's pet never takes it.
            var pet = Npc<TestPet>(39050, 41166, 16001);
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(pet, new ControlledMobBrain(bot) { Body = pet });
            Assert.That(T.PetMustNotHit(pet, kept), Is.True);
            Assert.That(T.PetMustNotHit(pet, other), Is.False);
            Assert.That(T.PetMustNotHit(pet, lossren), Is.False);
        }

        // ---- Krondon -------------------------------------------------------------------------------------------------------

        [Test]
        public void DebensMagesAreTheCastersFirstAndHisFightersTheMelees()
        {
            UseRegion(61);
            var deben = Npc<TestDeben>(21615, 54280, 14076);
            var tank = Npc<Raider>(21700, 54280, 14076);
            var wizard = Npc<Raider>(21400, 54300, 14076);
            var hero = Npc<Raider>(21600, 54200, 14076);
            var mage = Npc<TestMage>(21650, 54350, 14076);
            var fighter = Npc<TestFighter>(21600, 54250, 14076);
            deben.TargetObject = tank;
            var live = new List<GameNPC> { deben, tank, wizard, hero, mage, fighter };
            Assert.That(Rule(wizard, deben, live, true, Wizard)?.Target, Is.SameAs(mage));
            Assert.That(Rule(hero, deben, live, false, Warrior)?.Target, Is.SameAs(fighter));
            Assert.That(Rule(tank, deben, live, false, Warrior), Is.Null, "the party he fights holds him");
        }

        [Test]
        public void BeransGuardsAndDrihtensStudents()
        {
            UseRegion(61);
            var beran = Npc<TestBeran>(48907, 26990, 17163);
            var tank = Npc<Raider>(48950, 26990, 17163);
            var hero = Npc<Raider>(48800, 26900, 17163);
            var guard = Npc<Mob>(48900, 27050, 17163); guard.Name = T.BeranGuards; guard.Fighting = true;
            beran.TargetObject = tank;
            var live = new List<GameNPC> { beran, tank, hero, guard };
            Assert.That(Rule(hero, beran, live, false, Warrior)?.Target, Is.SameAs(guard));
            // His room (37,323,68,217) lies past the test zone's edge: the same spots 10,000 south.
            var drihten = Npc<TestDrihten>(37323, 58217, 15102);
            var student = Npc<Mob>(36587, 58029, 15116); student.PackageID = T.DrihtenStudents;
            var stranger = Npc<Mob>(31000, 50000, 15116); stranger.PackageID = T.DrihtenStudents;
            live = [drihten, student, stranger];
            Assert.That(T.ClearFirstOwner(student, live), Is.SameAs(drihten), "760 from him: dies first");
            Assert.That(T.ClearFirstOwner(stranger, live), Is.Null, "out of his call");
        }

        // ---- Lord Gildas ---------------------------------------------------------------------------------------------------

        [Test]
        public void ATankOnItsFeetTakesGildasOffHisStunnedTarget()
        {
            var stunned = Npc<Raider>(0, 0, 0);
            var near = Npc<Raider>(100, 0, 0);
            var far = Npc<Raider>(300, 0, 0);
            var alsoStunned = Npc<Raider>(50, 0, 0);
            var tanks = new (GameLiving, float, bool)[] { (stunned, 10f, true), (near, 100f, false), (far, 300f, false), (alsoStunned, 50f, true) };
            Assert.That(T.TakeOverFromStunned(stunned, true, tanks), Is.SameAs(near));
            Assert.That(T.TakeOverFromStunned(stunned, false, tanks), Is.Null, "his target is on its feet: no taunt");
            var gildas = Npc<TestGildas>(29015, 41910, 12933);
            var tank = Npc<Raider>(29115, 41910, 12933);
            var melee = Npc<Raider>(29100, 41930, 12933);
            gildas.TargetObject = tank;
            var live = new List<GameNPC> { gildas, tank, melee };
            Assert.That(Rule(melee, gildas, live, false, Warrior)?.Action, Is.EqualTo(T.BotAction.Reposition), "behind him (he blocks in front)");
        }
    }
}
