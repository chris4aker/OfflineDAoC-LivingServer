using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Epic wave 2 (1.2): the dragons, Xanxicar, Legion and Galladoria with their mechanics on and the raid's answers
    /// (EncounterSignals, EpicEncounterTactics.Signals.cs, the catalog rows). The test doubles carry the scripts' class names
    /// where a rule looks a boss up by name (nested here, so they hide the real ones), else the catalog's row renamed onto
    /// them. No world tick, combat, spell or pathing runs: these prove the decisions, not a played fight.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicWaveTwo
    {
        /// <summary>The restore switches on since wave 2 (with the class rule, on since round 3).</summary>
        public static readonly string[] GalladoriaRestoresOn =
        [
            nameof(EpicEncounterMechanics.AllowEarthmenderClassPuzzle), nameof(EpicEncounterMechanics.AllowAroonDamageOrder),
            // AllowFullFirePatches is off by default (the owner's 2026-10-19 nerf kept until he confirms); the raid plays both.
            nameof(EpicEncounterMechanics.AllowGalladoriaBridgeSpot),
            nameof(EpicEncounterMechanics.AllowEarthmendersPassive), nameof(EpicEncounterMechanics.AllowEarthmenderQuickReset),
            nameof(EpicEncounterMechanics.AllowEarthmenderMutualHeal), nameof(EpicEncounterMechanics.AllowBeathaHealsXaga),
            nameof(EpicEncounterMechanics.AllowFairyFlight),
        ];

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 500;
        private EpicTestServerScope _server;
        private Func<GameNPC, IReadOnlyList<GameNPC>, IEnumerable<GameLiving>> _raidersNear;

        public class Boss2 : GameEpicBoss
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }
        public class Add2 : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class Dragon2 : Boss2 { }
        public sealed class Messenger2 : Add2 { }
        public sealed class Conservator2 : Boss2 { }
        public sealed class Aroon2 : Boss2 { }
        public sealed class ScaithA2 : Add2 { }
        public sealed class ScaithB2 : Add2 { }
        public sealed class ScaithC2 : Add2 { }
        public sealed class Olcasgean2Copy : Boss2 { }
        public sealed class OlcasgeanMain : Boss2 { }
        // The scripts' own names (the Fire primal rule looks them up by name).
        public sealed class FirePrimal : Boss2 { }
        public sealed class WaterPrimal : Boss2 { }
        public sealed class TrailOfFire : Add2 { }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }

        private static EpicEncounterTactics.Tactic Row(string boss) => EpicEncounterTactics.Catalog.Single(t => t.Boss == boss);

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 64, Name = "Wave 2 test", Description = "Wave 2 test", Mobs = [] });
            EncounterSignals.ClearAll();
            _raidersNear = EpicEncounterTactics.RaidersNear;
            EpicEncounterTactics.Register(Row("AlbGolestandt") with { Boss = nameof(Dragon2), RangedAdds = [nameof(Messenger2)] });
            EpicEncounterTactics.Register(Row("Conservator") with { Boss = nameof(Conservator2) });
            EpicEncounterTactics.Register(Row("Aroon") with
                { Boss = nameof(Aroon2), RequiredAdds = [nameof(ScaithA2), nameof(ScaithB2), nameof(ScaithC2)] });
            EpicEncounterTactics.AttackRules.Add(new(nameof(Conservator2), 800, true, null));
            EpicEncounterTactics.SafeSpotRules.Add(new("OlcasgeanTest", [nameof(Olcasgean2Copy), nameof(OlcasgeanMain)],
                [new(40170, 62600, 11681), new(39237, 62644, 11685)], 200, null));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string boss in new[] { nameof(Dragon2), nameof(Conservator2), nameof(Aroon2) })
                EpicEncounterTactics.Unregister(boss);
            EpicEncounterTactics.AttackRules.RemoveAll(r => r.Boss == nameof(Conservator2));
            EpicEncounterTactics.SafeSpotRules.RemoveAll(r => r.Boss == "OlcasgeanTest");
            EpicEncounterTactics.RaidersNear = _raidersNear;
            EncounterSignals.ClearAll();
            EpicEncounterMechanics.ResetPerEncounter();
            _server.Dispose();
        }

        private T Npc<T>(int x, int y, int z) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            Move(npc, x, y, z);
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private static void Move(GameNPC npc, float x, float y, float z)
        {
            npc.X = (int)x; npc.Y = (int)y; npc.Z = (int)z;
            npc.movementComponent.ForceUpdatePosition();
        }

        private static float Flat(GameObject a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static float Flat(GameObject a, GameObject b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static float Flat(Vector3 a, GameObject b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        private const int Wizard = (int)eCharacterClass.Wizard, Druid = (int)eCharacterClass.Druid, Hero = (int)eCharacterClass.Hero;
        private static readonly EpicEncounterTactics.BotAction None = EpicEncounterTactics.BotAction.None,
            Reposition = EpicEncounterTactics.BotAction.Reposition, Retarget = EpicEncounterTactics.BotAction.Retarget;

        // ---- switches and catalog -------------------------------------------------------------------------------------

        [Test]
        public void GalladoriasRestoredMechanicsAreOnAndTheMessengersOlcasgeanAndTheTeleportsShip()
        {
            // Galladoria's are on; every restore switch matches the one list of switches on since 1.2 (the other dungeons' too).
            foreach (string name in GalladoriaRestoresOn)
                Assert.That(EpicEncounterMechanics.Global(name), Is.True, name);
            foreach (string name in EpicEncounterMechanics.RestoreSwitches)
                Assert.That(EpicEncounterMechanics.Global(name), Is.EqualTo(UT_EpicEncounterTactics.OnSince12.Contains(name)), name);
            foreach (string dragon in new[] { "AlbGolestandt", "MidGjalpinulva", "HibCuuldurach" })
                Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowDragonMessengers), dragon), Is.True, dragon);
            foreach (string boss in new[] { "Olcasgean", "WaterfallAntipass", "WaterPrimal", "SpindlerBroodmother" })
            {
                Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowDisplacement), boss), Is.True, boss);
                Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowBotDisplacement), boss), Is.True, boss);
            }
            foreach (string boss in new[] { "Olcasgean", "Conservator", "Easmarach" })
                Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowPositionPuzzle), boss), Is.True, boss);
            // On since wave 3 (UT_EpicWave3): Easmarach's push behind the falls (the route kills him before the last room and
            // shoots him on the waterfall top) and Legion's throw for bots (recalled down from the platform).
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowDisplacement), "Easmarach"), Is.True);
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowBotDisplacement), "Legion"), Is.True);
            Assert.That(EpicEncounterMechanics.AllowDisplacement, Is.False, "the global switch is unchanged");
        }

        [Test]
        public void TheCatalogCarriesTheWaveTwoTactics()
        {
            Assert.That(Row("AlbGolestandt").RangedAdds, Is.EqualTo(new[] { "GolestandtMessenger" }));
            Assert.That(Row("MidGjalpinulva").RangedAdds, Is.EqualTo(new[] { "GjalpinulvaMessenger" }));
            Assert.That(Row("HibCuuldurach").RangedAdds, Is.EqualTo(new[] { "CuuldurachMessenger" }));
            Assert.That(Row("Legion").RangedAdds, Is.EqualTo(new[] { "LegionAdd" }));
            Assert.That(Row("Conservator").FightAtHomeRadius, Is.EqualTo(600), "led home before his 800 rule bites");
            var aroon = Row("Aroon");
            Assert.That(aroon.AddsInOrder, Is.True);
            Assert.That(aroon.RequiredAdds, Is.EqualTo(new[] { "CorpScaith", "SpioradScaith", "RopadhScaith", "DamhnaScaith",
                "FuinneamgScaith", "BruScaith", "FuarScaith", "TaesScaith", "ScorScaith" }), "the script's order (EpicEncounterRestores.AroonOrder)");
            Assert.That((Row("Xaga").RequiredAdds, Row("Xaga").AddsInOrder), Is.EqualTo((new[] { "Beatha", "Tine" }, true)), "Beatha first");
            Assert.That((Row("FirePrimal").Hazards.Single(), Row("FirePrimal").HazardRadius), Is.EqualTo(("TrailOfFire", 450)));
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (string type in aroon.RequiredAdds.Concat(Row("Xaga").RequiredAdds)
                         .Concat(EpicEncounterTactics.AttackRules.Where(r => r.Boss != nameof(Conservator2)).Select(r => r.Boss))
                         .Concat(EpicEncounterTactics.SafeSpotRules.Where(r => r.Boss != "OlcasgeanTest").SelectMany(r => r.Bodies)))
                Assert.That(gs.GetTypes().Any(x => x.Name == type && typeof(GameNPC).IsAssignableFrom(x)), Is.True, type);
            Assert.That(EpicEncounterTactics.AttackRules.Select(r => (r.Boss, r.Radius, r.FromSpawn)),
                Is.SupersetOf(new[] { ("Conservator", 800, true), ("Easmarach", 2_500, true), ("Legion", 1_400, false) }));
        }

        // ---- signals ----------------------------------------------------------------------------------------------------

        [Test]
        public void ASignalLastsUntilItHasLandedAndIsSeenByListeners()
        {
            var dragon = Npc<Dragon2>(10_000, 10_000, 1_000);
            var raider = Npc<Raider>(10_500, 10_000, 1_000);
            EncounterSignal heard = null;
            void Listen(EncounterSignal s) => heard = s;
            EncounterSignals.Raised += Listen;
            try { EncounterSignals.Raise(dragon, EncounterSignals.Glare, raider, 1000, true, 6000, 0, 1_000); }
            finally { EncounterSignals.Raised -= Listen; }
            Assert.That(heard?.Target, Is.SameAs(raider));
            Assert.That(EncounterSignals.Active(dragon, EncounterSignals.Glare, 6_000), Is.Not.Null);
            Assert.That(EncounterSignals.Active(dragon, EncounterSignals.Glare, 7_000)?.Landed(7_000), Is.True);
            Assert.That(EncounterSignals.Active(dragon, EncounterSignals.Glare, 8_600), Is.Null, "6 s warning + 1.5 s aftermath");
            EncounterSignals.Raise(dragon, EncounterSignals.Stun, null, 2000, false, 6000, 0, 10_000);
            EncounterSignals.Clear(dragon);
            Assert.That(EncounterSignals.Active(dragon, 11_000), Is.Empty, "a reset clears them");
            Assert.That(EncounterSignals.LoreTrigger(EncounterSignals.Breath), Is.EqualTo("breath"), "the lore engine's script-only trigger");
            Assert.That(EncounterSignals.LoreTrigger(EncounterSignals.Glare), Is.EqualTo("glare"));
        }

        [Test]
        public void OnADragonsStareEveryoneButTheStaredRaiderAndTheTankStepsAwayFromHim()
        {
            var dragon = Npc<Dragon2>(10_000, 10_000, 1_000);
            dragon.Fighting = true;
            var tank = Npc<Raider>(10_300, 10_000, 1_000);
            var stared = Npc<Raider>(10_500, 10_200, 1_000);
            dragon.TargetObject = tank;
            var near = Enumerable.Range(0, 8).Select(i => Npc<Raider>(10_400 + i * 60, 10_100 + i * 40, 1_000)).ToList();
            var far = Npc<Raider>(12_000, 10_000, 1_000);
            var live = new List<GameNPC> { dragon };
            EncounterSignals.Raise(dragon, EncounterSignals.Glare, stared, 1000, true, 6000, 0, 1_000);

            Assert.That(EpicEncounterTactics.Decide(stared, dragon, live, 0, false, 2_000, Hero).Action, Is.EqualTo(None), "he stays");
            Assert.That(EpicEncounterTactics.Decide(tank, dragon, live, 0, false, 2_000, Hero).Action, Is.EqualTo(None), "the tank holds it");
            Assert.That(EpicEncounterTactics.Decide(far, dragon, live, 0, true, 2_000, Wizard).Action, Is.EqualTo(None));
            foreach (Raider r in near)
            {
                var a = EpicEncounterTactics.Decide(r, dragon, live, 0, false, 2_000, Hero);
                Assert.That((a.Action, a.Target), Is.EqualTo((Reposition, (GameNPC)dragon)), "melee too: out of his circle, same target");
                Assert.That(Flat(a.StandAt.Value, stared), Is.EqualTo(1_250).Within(1));
                Move(r, a.StandAt.Value.X, a.StandAt.Value.Y, a.StandAt.Value.Z);
            }
            Assert.That(near.Count(r => Flat(r, stared) <= 1000), Is.Zero, "nobody but him and the tank under the glare");
            Assert.That(near.All(r => EpicEncounterTactics.Decide(r, dragon, live, 0, false, 3_000, Hero).Action == None), Is.True,
                "outside: fight on from there");
            Assert.That(EpicEncounterTactics.Decide(near[0], dragon, live, 0, false, 9_000, Hero).Action, Is.EqualTo(None), "it has gone");
        }

        [Test]
        public void ForTheLookAroundStunAndTheBreathCastersArchersAndHealersStepOutAndMeleeTakeIt()
        {
            var dragon = Npc<Dragon2>(10_000, 10_000, 1_000);
            dragon.Fighting = true;
            var caster = Npc<Raider>(11_200, 10_000, 1_000);
            var healer = Npc<Raider>(10_000, 11_400, 1_000);
            var melee = Npc<Raider>(10_250, 10_000, 1_000);
            var live = new List<GameNPC> { dragon };
            foreach (string mechanic in new[] { EncounterSignals.Stun, EncounterSignals.Breath })
            {
                EncounterSignals.ClearAll();
                EncounterSignals.Raise(dragon, mechanic, null, 2000, false, 6000, 0, 1_000);
                var c = EpicEncounterTactics.Decide(caster, dragon, live, 0, true, 2_000, Wizard);
                var h = EpicEncounterTactics.Decide(healer, dragon, live, 0, false, 2_000, Druid);
                Assert.That((c.Action, h.Action), Is.EqualTo((Reposition, Reposition)), mechanic);
                Assert.That(Flat(c.StandAt.Value, dragon), Is.EqualTo(2_250).Within(1), mechanic);
                Assert.That(Flat(h.StandAt.Value, dragon), Is.EqualTo(2_250).Within(1), mechanic);
                Assert.That(Vector2.Distance(new(h.StandAt.Value.X, h.StandAt.Value.Y), new(melee.X, melee.Y)), Is.LessThan(2_300),
                    "the healer outside still reaches the melee on its side (heal range 2,000 from the edge)");
                Assert.That(EpicEncounterTactics.Decide(melee, dragon, live, 0, false, 2_000, Hero).Action, Is.EqualTo(None),
                    mechanic + ": melee can't make 2,000 in 6 s");
                Assert.That(EpicEncounterTactics.Decide(caster, dragon, live, 0, true, 9_000, Wizard).Action, Is.EqualTo(None),
                    mechanic + ": landed, back in");
            }
            // Xanxicar's stomp moves nobody here: his blast rule has the rest of the raid out already, and his party takes it.
            EncounterSignals.ClearAll();
            EncounterSignals.Raise(dragon, EncounterSignals.Stomp, null, 2500, false, 6000, 0, 1_000);
            Assert.That(EpicEncounterTactics.Decide(caster, dragon, live, 0, true, 2_000, Wizard).Action, Is.EqualTo(None));
        }

        [Test]
        public void TheSignalStepIsPureAndAwayFromTheMiddle()
        {
            var dragon = Npc<Dragon2>(0, 0, 0);
            var stared = Npc<Raider>(500, 0, 0);
            var other = Npc<Raider>(600, 100, 0);
            var glare = new EncounterSignal(dragon, EncounterSignals.Glare, stared, 1000, true, 0, 6000, 7500);
            Vector3? to = EpicEncounterTactics.SignalStep(other, new(600, 100, 0), glare, false, Hero);
            Assert.That(to.Value.Y, Is.GreaterThan(100), "on its own side of him");
            Assert.That(EpicEncounterTactics.SignalStep(other, new(2000, 0, 0), glare, false, Hero), Is.Null, "already clear");
            Assert.That(EpicEncounterTactics.SignalStep(stared, new(500, 0, 0), glare, false, Hero), Is.Null);
            var unknown = new EncounterSignal(dragon, "somethingElse", null, 1000, false, 0, 6000, 7500);
            Assert.That(EpicEncounterTactics.SignalStep(other, new(600, 100, 0), unknown, true, Wizard), Is.Null,
                "only the listed mechanics around the boss move anyone");
        }

        [Test]
        public void HealersTakeAMarkedRaiderFirstUnlessALowerEmergencyWaits()
        {
            Assert.That(EpicEncounterTactics.PreferMarked(90, 100, null), Is.True);
            Assert.That(EpicEncounterTactics.PreferMarked(90, 100, 70), Is.True, "the stared raider before a 70% one");
            Assert.That(EpicEncounterTactics.PreferMarked(90, 100, 30), Is.False, "a 30% emergency first");
            Assert.That(EpicEncounterTactics.PreferMarked(100, 100, null), Is.False, "full: nothing to heal");
            Assert.That(EpicEncounterTactics.PreferMarked(60, EpicEncounterTactics.KillHealPatientPercent, 80), Is.False,
                "beside Legion only those under half are marked");
            Assert.That(EpicEncounterTactics.KillHealBosses, Does.Contain("Legion"));
        }

        [Test]
        public void ThePartyCallsAMechanicOnceInEightSeconds()
        {
            Assert.That(EncounterCalls.Topic(EncounterSignals.Messengers), Is.EqualTo("epic_messengers"));
            Assert.That(EncounterCalls.Due(-1, 0), Is.True);
            Assert.That(EncounterCalls.Due(1_000, 8_999), Is.False);
            Assert.That(EncounterCalls.Due(1_000, 9_000), Is.True);
        }

        // ---- dragon messengers ------------------------------------------------------------------------------------------

        [Test]
        public void CastersAndArchersRunTheMessengersDownWhileMeleeStayOnTheDragon()
        {
            var dragon = Npc<Dragon2>(391_345, 755_661, 410);
            dragon.Fighting = true;
            var runners = Enumerable.Range(0, 5).Select(i => Npc<Messenger2>(391_345 + i * 300, 754_000 - i * 200, 300)).ToList();
            var live = new List<GameNPC> { dragon };
            live.AddRange(runners);
            var caster = Npc<Raider>(391_900, 755_300, 410);
            var melee = Npc<Raider>(391_500, 755_600, 410);
            var picked = Enumerable.Range(0, 10).Select(p => EpicEncounterTactics.Decide(caster, dragon, live, p, true, 1_000, Wizard)).ToList();
            Assert.That(picked.All(a => a.Action == Retarget && runners.Contains(a.Target)), Is.True);
            Assert.That(picked.Select(a => a.Target).Distinct().Count(), Is.EqualTo(5), "the parties spread over the runners");
            Assert.That(EpicEncounterTactics.Decide(melee, dragon, live, 0, false, 1_000, Hero).Action, Is.EqualTo(None), "melee stay");
            Assert.That(EpicEncounterTactics.IsTacticPart(runners[0]), Is.True);
            Assert.That(EpicEncounterTactics.Decide(caster, runners[0], live, 0, true, 1_000, Wizard).Action, Is.EqualTo(None),
                "a caster on a runner keeps it");
            runners.ForEach(r => r.Dead = true);
            Assert.That(EpicEncounterTactics.Decide(caster, dragon, live, 0, true, 1_000, Wizard).Action, Is.EqualTo(None), "all dead: the dragon");
        }

        // ---- close range ------------------------------------------------------------------------------------------------

        [Test]
        public void ARaiderOutsideConservatorsReachStepsInAndOneInsideFightsOn()
        {
            var co = Npc<Conservator2>(31_246, 40_938, 13_433);
            co.Fighting = true;
            var live = new List<GameNPC> { co };
            var farCaster = Npc<Raider>(31_246 + 1_200, 40_938, 13_433);
            var nearCaster = Npc<Raider>(31_246 + 500, 40_938, 13_433);
            var a = EpicEncounterTactics.Decide(farCaster, co, live, 0, true, 1_000, Wizard);
            Assert.That(a.Action, Is.EqualTo(Reposition));
            Assert.That(Flat(a.StandAt.Value, co), Is.EqualTo(500).Within(1), "800 less 300");
            Assert.That(EpicEncounterTactics.Decide(nearCaster, co, live, 0, true, 1_000, Wizard).Action, Is.EqualTo(None));
            Assert.That(EpicEncounterTactics.StepIn(new(0, 0, 0), new(0, 0, 0), 800), Is.Null);
            Assert.That(EpicEncounterTactics.StepIn(new(700, 0, 0), new(0, 0, 0), 800), Is.Not.Null, "inside the margin steps in too");
            Assert.That(EpicEncounterTactics.StepIn(new(1200, 0, 0), new(0, 0, 0), 1400), Is.Null, "Legion: 1,200 is close enough");
        }

        // ---- Olcasgean's safe spots ---------------------------------------------------------------------------------------

        [Test]
        public void TheTwoLowestPartiesHoldOneSafeSpotEachAndFightTheBodyThere()
        {
            Assert.That(EpicEncounterTactics.SpotOf(5, 0, new[] { 9, 7, 5 }), Is.EqualTo(0));
            Assert.That(EpicEncounterTactics.SpotOf(7, 0, new[] { 9, 7, 5 }), Is.EqualTo(1));
            Assert.That(EpicEncounterTactics.SpotOf(9, 0, new[] { 9, 7, 5 }), Is.EqualTo(-1), "the rest fight where they like");
            Assert.That((EpicEncounterTactics.SpotOf(5, 10, new[] { 5 }), EpicEncounterTactics.SpotOf(5, 11, new[] { 5 })), Is.EqualTo((0, 1)),
                "one party: its members split");

            var copy = Npc<Olcasgean2Copy>(40_170, 62_600, 11_681);
            var main = Npc<OlcasgeanMain>(39_237, 62_644, 11_685);
            var live = new List<GameNPC> { copy, main };
            var first = Npc<Raider>(39_700, 61_900, 11_690);   // lowest object id: party 0 of the test
            var second = Npc<Raider>(39_650, 61_950, 11_690);
            var third = Npc<Raider>(39_600, 61_900, 11_690);
            EpicEncounterTactics.RaidersNear = (_, _) => new GameLiving[] { first, second, third };
            Assert.That(EpicEncounterTactics.Decide(first, main, live, first.ObjectID, false, 1_000, Hero).Action, Is.EqualTo(None),
                "asleep: the spots mean nothing yet");

            main.Fighting = true;
            var a1 = EpicEncounterTactics.Decide(first, main, live, first.ObjectID, false, 1_000, Hero);
            var a2 = EpicEncounterTactics.Decide(second, main, live, second.ObjectID, false, 1_000, Hero);
            Assert.That((a1.Action, a1.StandAt), Is.EqualTo((Reposition, (Vector3?)new Vector3(40_170, 62_600, 11_681))));
            Assert.That((a2.Action, a2.StandAt), Is.EqualTo((Reposition, (Vector3?)new Vector3(39_237, 62_644, 11_685))));
            Assert.That(EpicEncounterTactics.Decide(third, main, live, third.ObjectID, false, 1_000, Hero).Action, Is.EqualTo(None));
            Move(first, 40_170, 62_600, 11_681);
            Move(second, 39_237, 62_644, 11_685);
            var at1 = EpicEncounterTactics.Decide(first, main, live, first.ObjectID, false, 2_000, Hero);
            Assert.That((at1.Action, at1.Target), Is.EqualTo((Retarget, (GameNPC)copy)), "on spot 1: fight the copy that stands there");
            Assert.That(EpicEncounterTactics.Decide(second, main, live, second.ObjectID, false, 2_000, Hero).Action, Is.EqualTo(None),
                "on spot 2 beside him: fight on");
            Assert.That(EpicEncounterTactics.Decide(first, copy, live, first.ObjectID, false, 2_000, Hero).Action, Is.EqualTo(None));
        }

        // ---- Aroon's Scaiths in order -----------------------------------------------------------------------------------

        [Test]
        public void TheRaidKillsAroonsScaithsOneAtATimeInOrder()
        {
            var aroon = Npc<Aroon2>(51_478, 43_359, 10_369);
            var a = Npc<ScaithA2>(51_500, 43_400, 10_369);
            var b = Npc<ScaithB2>(51_300, 43_300, 10_369);
            var c = Npc<ScaithC2>(51_700, 43_500, 10_369);
            var live = new List<GameNPC> { aroon, c, b, a };
            aroon.Fighting = true;
            var raider = Npc<Raider>(51_400, 43_350, 10_369);
            for (int party = 0; party < 6; party++)
                Assert.That(EpicEncounterTactics.Decide(raider, aroon, live, party, false, 1_000, Hero).Target, Is.SameAs(a),
                    "every party on the first one (the others are immune)");
            var onB = EpicEncounterTactics.Decide(raider, b, live, 0, false, 1_000, Hero);
            Assert.That((onB.Action, onB.Target), Is.EqualTo((Retarget, (GameNPC)a)), "not next: turn to the next one");
            b.TargetObject = raider;
            Assert.That(EpicEncounterTactics.Decide(raider, b, live, 0, false, 1_000, Hero).Action, Is.EqualTo(None),
                "the party it fights holds it");
            Assert.That(EpicEncounterTactics.RouteTarget(aroon, live, new(51_300, 43_300, 10_369), out _), Is.SameAs(a),
                "the route goes to the next one, not the nearest");
            a.Dead = true;
            Assert.That(EpicEncounterTactics.Decide(raider, aroon, live, 3, false, 1_000, Hero).Target, Is.SameAs(b));
            b.Dead = true; c.Dead = true;
            Assert.That(EpicEncounterTactics.Decide(raider, aroon, live, 3, false, 1_000, Hero).Action, Is.EqualTo(None), "then Aroon");
        }

        // ---- the Fire primal ------------------------------------------------------------------------------------------------

        [Test]
        public void MeleeLeaveTheFirePrimalToRangedWhileAnotherPrimalLivesAndRangedStepOutOfItsTrail()
        {
            var fire = Npc<FirePrimal>(40_142, 63_014, 11_670);
            var water = Npc<WaterPrimal>(39_652, 61_200, 11_700);
            var patch = Npc<TrailOfFire>(40_142, 63_014, 11_670);
            fire.Fighting = true; water.Fighting = true;
            var live = new List<GameNPC> { fire, water, patch };
            var melee = Npc<Raider>(40_250, 63_014, 11_670);
            var caster = Npc<Raider>(40_300, 63_200, 11_670); // 245 from the patch
            var m = EpicEncounterTactics.Decide(melee, fire, live, 0, false, 1_000, Hero);
            Assert.That((m.Action, m.Target), Is.EqualTo((Retarget, (GameNPC)water)));
            var c = EpicEncounterTactics.Decide(caster, fire, live, 0, true, 1_000, Wizard);
            Assert.That(c.Action, Is.EqualTo(Reposition), "out of the patch first");
            Assert.That(Flat(c.StandAt.Value, patch), Is.EqualTo(750).Within(1), "450 + 300");
            Assert.That(EpicEncounterTactics.IsHazard(patch), Is.True, "never a target");
            water.Dead = true;
            Assert.That(EpicEncounterTactics.Decide(melee, fire, live, 0, false, 1_000, Hero).Action, Is.EqualTo(None),
                "the last primal: melee fight it in the fire, no step-out loop");
        }

        // ---- Galladoria's bridge -------------------------------------------------------------------------------------------

        [Test]
        public void ARaidBotOnTheBridgeStartsTheLastRoomAfterThreeMinutesAtMost()
        {
            Assert.That(EpicEncounterRestores.BridgeFallbackDue(0, 1_000_000), Is.False, "nobody on the bridge");
            Assert.That(EpicEncounterRestores.BridgeFallbackDue(10_000, 10_000 + 179_999), Is.False);
            Assert.That(EpicEncounterRestores.BridgeFallbackDue(10_000, 10_000 + 180_000), Is.True);
        }
    }
}
