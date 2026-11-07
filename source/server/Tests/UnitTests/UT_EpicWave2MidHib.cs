using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.EncounterDisplacementRecovery;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Epic wave 2, Midgard and Hibernia (1.2): Tuscaran Glacier, Summoner's Hall, the Midgard throws, Fomor and Marfach.
    /// The test doubles carry the real scripts' class names (nested here), so the catalog rows and the dungeon rules apply to
    /// them as they do in the game. The decisions are played in a test region; no world tick, combat, spell or pathing runs.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicWave2MidHib
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 500;
        private EpicTestServerScope _server;
        private Func<GameNPC, long> _scream;
        private Func<GameNPC, IEnumerable<GameNPC>> _guardians;

        public class Boss : GameEpicBoss
        {
            public bool Dead, Fighting = true;
            public byte Hp = 100;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
            public override byte HealthPercent => Hp;
        }
        public sealed class KingTuscar : Boss { }
        public sealed class QueenKula : Boss { }
        public sealed class Kvasir : Boss { }
        public sealed class Jailer : Boss { }
        public sealed class Fornfrusenen : Boss { }
        public sealed class Vagn : Boss { }
        public sealed class Nokkvi : Boss { }
        public sealed class SummonerLossren : Boss { }
        public sealed class Krackenschtein : Boss { }
        public sealed class Myrddraxis : Boss { }
        public sealed class BlackLady : Boss { }
        public sealed class GrandSummonerGovannon : Boss { }
        public class Add : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class FornfrusenenShard : Add { }
        public sealed class ExplodeUndead : Add { }
        public sealed class SummonedSacrifice : Add { }
        public sealed class ShadeOfAelfgar : Add { }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }

        private static readonly int Warrior = (int)eCharacterClass.Warrior, Runemaster = (int)eCharacterClass.Runemaster,
            Healer = (int)eCharacterClass.Healer, Wizard = (int)eCharacterClass.Wizard;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 64, Name = "Wave 2 test", Description = "Wave 2 test", Mobs = [] });
            EpicEncounterMechanics.ResetPerEncounter();
            _scream = T.KvasirScreamAt;
            _guardians = T.VifilGuardians;
            T.ForgetRescues();
            T.ForgetErrands();
        }

        [TearDown]
        public void TearDown()
        {
            T.KvasirScreamAt = _scream;
            T.VifilGuardians = _guardians;
            EpicEncounterMechanics.ResetPerEncounter();
            EpicEncounterMechanics.AllowTuscarTwinHeal = true;
            EpicEncounterMechanics.AllowKvasirColdHeal = true;
            _server.Dispose();
        }

        private TNpc Npc<TNpc>(int x, int y, int z) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)?.SetValue(npc, new PropertyCollection());
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            Move(npc, x, y, z);
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            if (npc is Boss boss) { boss.Hp = 100; boss.Fighting = true; } // no field initializers on an uninitialized object
            return npc;
        }

        private static void Move(GameNPC npc, float x, float y, float z)
        {
            npc.X = (int)x; npc.Y = (int)y; npc.Z = (int)z;
            npc.movementComponent.ForceUpdatePosition();
        }

        private static float Flat(GameObject a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static float Flat(Vector3 a, GameObject b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static T.Advice Decide(GameNPC raider, GameNPC target, List<GameNPC> live, int classId, int party = 0, long now = 1_000) =>
            T.Decide(raider, target, live, party, T.IsMagicDealer(classId) || classId == (int)eCharacterClass.Hunter, now, classId);

        // ---- switches, catalog, rules -------------------------------------------------------------------------------

        [Test]
        public void TheWaveTwoSwitchesShipOnAndTheGriffonPatrolStaysOff()
        {
            string[] moved = ["QueenKula", "Jailer", "Hakr", "Hurika"];
            foreach (string e in moved)
            {
                Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowDisplacement), e), Is.True, e);
                Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowBotDisplacement), e), Is.True, e);
            }
            foreach (string e in new[] { "SummonerCunovinda", "ShadeOfAelfgar", "Iarnvidiur", "Boligar", "Mortufoghus", "Ydenia",
                         "GlacierGiant", "Krackenschtein", "Anurigunda" })
                Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowBotDisplacement), e), Is.True, e);
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowPositionPuzzle), "QueenKula"), Is.True);
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowGlacierGriffonPatrol), "Torst"), Is.False);
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowGlacierGriffonPatrol), "Hurika"), Is.False);
            Assert.That(EpicEncounterMechanics.AllowDisplacement, Is.False, "the global stays off: only these encounters move raiders");
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowBotDisplacement), "Legion"), Is.True, "wave 3");
            Assert.That(EpicEncounterMechanics.AllowTuscarTwinHeal && EpicEncounterMechanics.AllowKvasirColdHeal, Is.True);
            // Govannon's 300,000 stays off by default until a played check (owner to confirm; epic_govannon_full_health).
            Assert.That(EpicEncounterMechanics.AllowGovannonFullHealth, Is.False);
            // Every shipped encounter is a real class of the server.
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (var (_, e, _) in EpicEncounterMechanics.ShippedPerEncounter)
                Assert.That(gs.GetTypes().Any(x => x.Name == e && typeof(GameNPC).IsAssignableFrom(x)), Is.True, e);
        }

        [Test]
        public void TheCatalogAndTheDungeonRulesNameRealClasses()
        {
            T.Tactic Row(string boss) => T.Catalog.Single(t => t.Boss == boss);
            Assert.That(Row("KingTuscar").After, Is.EqualTo("QueenKula"));
            Assert.That(Row("Myrddraxis").BlastThresholds, Is.EqualTo(new[] { 80, 75, 60, 50, 40, 25, 20 }));
            Assert.That(Row("Myrddraxis").BlastRadius, Is.EqualTo(2150));
            Assert.That(Row("BlackLady").SpreadRadius, Is.EqualTo(350));
            Assert.That(Row("BlackLady").FightAtHomeRadius, Is.EqualTo(1100), "fight at home kept");
            Assert.That(Row("GrandSummonerGovannon").RequiredAdds, Is.EqualTo(new[] { "ShadeOfAelfgar" }));
            Assert.That(Row("SummonerLossren").RangedAdds, Is.EqualTo(new[] { "ExplodeUndead" }));
            var encounters = T.AllDungeonRules.Select(r => r.Encounter).ToArray();
            Assert.That(encounters, Is.SupersetOf(new[] { "KingTuscar", "Kvasir", "Jailer", "Fornfrusenen", "Vagn", "SummonerLossren",
                "Krackenschtein", "Avartack" }));
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (string type in T.AllDungeonRules.SelectMany(r => r.Targets).Distinct())
                Assert.That(gs.GetTypes().Any(x => x.Name == type && typeof(GameNPC).IsAssignableFrom(x) && x.Namespace?.StartsWith("DOL.GS") == true),
                    Is.True, type);
        }

        // ---- King Tuscar and Queen Kula -------------------------------------------------------------------------------

        [Test]
        public void KulaDiesFirstAndOnlyTheKingsTargetFightsHim()
        {
            var king = Npc<KingTuscar>(34542, 57121, 11881);
            var queen = Npc<QueenKula>(34136, 57202, 12025);
            var tank = Npc<Raider>(34700, 57150, 11881);
            var other = Npc<Raider>(34600, 57000, 11881);
            king.TargetObject = tank;
            var live = new List<GameNPC> { king, queen };
            Assert.That(Decide(tank, king, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "his target holds him");
            var a = Decide(other, king, live, Warrior);
            Assert.That((a.Action, a.Target, a.Call), Is.EqualTo((T.BotAction.Retarget, (GameNPC)queen, "epic_twin_heal")));
            Assert.That(T.RouteTarget(king, live, new(34600, 57100, 11881), out string why), Is.SameAs(queen), "the route musters for her");
            Assert.That(why, Does.StartWith("after"));
            queen.Dead = true;
            Assert.That(Decide(other, king, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "she is dead: everyone on him");
        }

        [Test]
        public void CastersOnKulaKeepClearOfTheKingsHammersAndNobodyStandsInHerCircle()
        {
            var king = Npc<KingTuscar>(34542, 57121, 11881);
            var queen = Npc<QueenKula>(34136, 57202, 12025);
            var kingTank = Npc<Raider>(34600, 57150, 11881);
            king.TargetObject = kingTank;
            var live = new List<GameNPC> { king, queen };
            var caster = Npc<Raider>(34400, 57250, 12000); // 222 from the King's tank
            var a = Decide(caster, queen, live, Runemaster);
            Assert.That(a.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(a.StandAt.Value, kingTank), Is.EqualTo(T.TuscarHammerRadius + T.SpreadStepOut).Within(1));
            var melee = Npc<Raider>(34200, 57200, 12025);
            Assert.That(Decide(melee, queen, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "melee on Kula fight on");

            var inCircle = Npc<Raider>(34128, 56200, 11989); // 105 from the middle of the frost circle
            var b = Decide(inCircle, queen, live, Warrior);
            Assert.That((b.Action, b.Call), Is.EqualTo((T.BotAction.Reposition, "epic_frost_circle")));
            Assert.That(Flat(b.StandAt.Value, T.KulaFrostPoint), Is.EqualTo(T.KulaFrostRadius + T.HazardStepOut).Within(1));
            EpicEncounterMechanics.SetFor(nameof(EpicEncounterMechanics.AllowPositionPuzzle), "QueenKula", false);
            Assert.That(Decide(inCircle, queen, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "no circle with the puzzle off");
        }

        [Test]
        public void WithTheTwinHealOffTheKingsPartyStillHoldsHimAsTheCatalogSays()
        {
            EpicEncounterMechanics.AllowTuscarTwinHeal = false;
            var king = Npc<KingTuscar>(34542, 57121, 11881);
            var queen = Npc<QueenKula>(34136, 57202, 12025);
            var tank = Npc<Raider>(34700, 57150, 11881);
            king.TargetObject = tank;
            var live = new List<GameNPC> { king, queen };
            var other = Npc<Raider>(34600, 57000, 11881);
            Assert.That(Decide(other, king, live, Warrior).Target, Is.SameAs(queen), "the catalog's after rule");
            Assert.That(Decide(other, king, live, Warrior).Call, Is.Null);
        }

        // ---- Icelord Kvasir --------------------------------------------------------------------------------------------

        [Test]
        public void NoBotCastsColdAtKvasirWhileColdHealsHim()
        {
            var kvasir = Npc<Kvasir>(20240, 52101, 10924);
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            Spell Dd(eDamageType type) => new(new DbSpell { Type = eSpellType.DirectDamage.ToString(), Target = "Enemy", Damage = 100,
                DamageType = (int)type, Range = 1500 }, 50);
            Assert.That(T.ForbidsSpell(bot, kvasir, Dd(eDamageType.Cold)), Is.True);
            Assert.That(T.ForbidsSpell(bot, kvasir, Dd(eDamageType.Energy)), Is.False);
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            Assert.That(T.ForbidsSpell(player, kvasir, Dd(eDamageType.Cold)), Is.False, "a real player casts what he likes");
            var other = Npc<Vagn>(0, 0, 0);
            Assert.That(T.ForbidsSpell(bot, other, Dd(eDamageType.Cold)), Is.False, "only on Kvasir");
            var buff = new Spell(new DbSpell { Type = eSpellType.StrengthBuff.ToString(), Target = "Realm", DamageType = (int)eDamageType.Cold }, 50);
            Assert.That(T.ForbidsSpell(bot, kvasir, buff), Is.False, "harmless spells are not damage");
            EpicEncounterMechanics.AllowKvasirColdHeal = false;
            Assert.That(T.ForbidsSpell(bot, kvasir, Dd(eDamageType.Cold)), Is.False, "the switch off: cold hurts him again");
        }

        [Test]
        public void WhenKvasirScreamsEveryoneButHisTargetsPartyStandsClearOfHisTarget()
        {
            var kvasir = Npc<Kvasir>(20240, 52101, 10924);
            var tank = Npc<Raider>(20400, 52101, 10924);
            kvasir.TargetObject = tank;
            var live = new List<GameNPC> { kvasir };
            T.KvasirScreamAt = _ => 60_000;
            var caster = Npc<Raider>(20900, 52101, 10924); // 500 from the tank
            var melee = Npc<Raider>(20300, 52200, 10924);
            Assert.That(Decide(caster, kvasir, live, Runemaster, now: 50_000).Action, Is.EqualTo(T.BotAction.None), "not yet");
            foreach (long now in new long[] { 57_500, 61_000, 66_000 })
            {
                var c = Decide(caster, kvasir, live, Runemaster, now: now);
                Assert.That((c.Action, c.Call), Is.EqualTo((T.BotAction.Reposition, "epic_kvasir_scream")), now.ToString());
                Assert.That(Flat(c.StandAt.Value, tank), Is.EqualTo(T.KvasirMezRadius + T.SpreadStepOut).Within(1));
                Assert.That(Flat(Decide(melee, kvasir, live, Warrior, now: now).StandAt.Value, tank), Is.GreaterThan(T.KvasirMezRadius));
            }
            Assert.That(Decide(tank, kvasir, live, Warrior, now: 61_000).Action, Is.EqualTo(T.BotAction.None), "his target's party takes it");
            Assert.That(Decide(caster, kvasir, live, Runemaster, now: 67_000).Action, Is.EqualTo(T.BotAction.None), "over: back in");
            // Out already: a melee raider waits where it is, a caster keeps casting.
            Move(melee, 21400, 52101, 10924);
            var wait = Decide(melee, kvasir, live, Warrior, now: 61_000);
            Assert.That((wait.Action, wait.StandAt), Is.EqualTo((T.BotAction.Reposition, (Vector3?)new Vector3(21400, 52101, 10924))));
            Move(caster, 21400, 52200, 10924);
            Assert.That(Decide(caster, kvasir, live, Runemaster, now: 61_000).Action, Is.EqualTo(T.BotAction.None));
        }

        // ---- Jailer Vifil ------------------------------------------------------------------------------------------------

        [Test]
        public void OneOrTwoBotPartiesAreSentForTheJailGuardsNeverTheHoldersOrThePlayers()
        {
            Assert.That(new[] { 2, 3, 4, 7, 8, 12 }.Select(T.RescueTeamsWanted), Is.EqualTo(new[] { 0, 0, 1, 1, 2, 2 }));
            Group G() => (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));
            var claim = new T.RescueClaim();
            Group holder = G(), player = G(), a = G(), b = G(), c = G();
            static bool Idle(Group _) => false;
            static bool Busy(Group _) => true;
            Assert.That(T.ClaimRescue(claim, holder, false, holdsBoss: true, 10, 1_000, Busy), Is.False);
            Assert.That(T.ClaimRescue(claim, player, partyHasPlayer: true, false, 10, 1_000, Busy), Is.False);
            Assert.That(T.ClaimRescue(claim, a, false, false, 5, 1_000, Busy), Is.True);
            Assert.That(T.ClaimRescue(claim, b, false, false, 5, 1_000, Busy), Is.False, "five parties: one team");
            Assert.That(T.ClaimRescue(claim, a, false, false, 5, 2_000, Busy), Is.True, "a team stays a team");
            Assert.That(T.ClaimRescue(claim, b, false, false, 10, 2_000, Busy), Is.True, "ten parties: two teams");
            Assert.That(T.ClaimRescue(claim, c, false, false, 10, 2_000, Busy), Is.False);
            // Both teams done (or lost) and the guards still up a minute later: a new team goes.
            Assert.That(T.ClaimRescue(claim, c, false, false, 10, 30_000, Idle), Is.False, "too soon");
            Assert.That(T.ClaimRescue(claim, c, false, false, 10, 2_000 + T.RescueRetryMilliseconds, Idle), Is.True);
        }

        [Test]
        public void TheRescueTeamWalksDownTheTunnelClearsTheGuardsAndComesBack()
        {
            // Vifil's room to his jail: about 10,700 units. A bot walks at 180 a second, one turn a second.
            Vector3 boss = new(27074, 61280, 10351), at = boss;
            var plan = T.StartErrandPlan(T.VifilRescue, null, at, 0);
            int guards = 2;
            var orders = new List<T.ErrandOrder>();
            for (long now = 1_000; now < 600_000 && plan.Stage != T.ErrandStage.Done; now += 1_000)
            {
                bool atGoal = Vector2.Distance(new(at.X, at.Y), new(T.VifilGuardSpot.X, T.VifilGuardSpot.Y)) <= T.ErrandGoalRadius;
                var o = T.StepErrand(plan, new T.ErrandObservation(now, true, at, boss, guards, UnderAttack: atGoal && guards > 0, BossFighting: true));
                orders.Add(o);
                if (o == T.ErrandOrder.Fight && plan.Stage == T.ErrandStage.Clearing && now % 20_000 == 0) guards--; // a guard dies every 20 s
                if (o == T.ErrandOrder.Walk)
                {
                    Vector3 to = plan.Stage == T.ErrandStage.Returning ? boss : T.VifilGuardSpot;
                    Vector3 d = to - at;
                    at = d.Length() <= 180 ? to : at + Vector3.Normalize(d) * 180;
                }
            }
            Assert.That(plan.Stage, Is.EqualTo(T.ErrandStage.Done));
            Assert.That(plan.Cleared, Is.True, plan.Reason);
            Assert.That(plan.Reason, Is.EqualTo("back at the fight"));
            Assert.That(orders.Count(o => o == T.ErrandOrder.Fight), Is.GreaterThanOrEqualTo(20), "it fought the guards at the jail");
            Assert.That(guards, Is.LessThanOrEqualTo(0));
        }

        [Test]
        public void ARescueWithNoWayThereOrAfterTheFightIsDropped()
        {
            Vector3 boss = new(27074, 61280, 10351);
            var stuck = T.StartErrandPlan(T.VifilRescue, null, boss, 0);
            T.ErrandOrder last = T.ErrandOrder.Walk;
            for (long now = 1_000; now <= 30_000 && last != T.ErrandOrder.Done; now += 1_000)
                last = T.StepErrand(stuck, new T.ErrandObservation(now, true, boss, boss, 2, false, true)); // never moves
            Assert.That((last, stuck.Reason, stuck.Cleared), Is.EqualTo((T.ErrandOrder.Done, "no progress on the way", false)));
            var over = T.StartErrandPlan(T.VifilRescue, null, boss, 0);
            Assert.That(T.StepErrand(over, new T.ErrandObservation(1_000, true, boss, boss, 2, false, BossFighting: false)), Is.EqualTo(T.ErrandOrder.Done));
            var dead = T.StartErrandPlan(T.VifilRescue, null, boss, 0);
            Assert.That(T.StepErrand(dead, new T.ErrandObservation(1_000, false, boss, boss, 2, false, true)), Is.EqualTo(T.ErrandOrder.Done));
        }

        [Test]
        public void AClaimedTeamStandsOffVifilAndOthersFightHim()
        {
            var vifil = Npc<Jailer>(27074, 61280, 10351);
            var guard = Npc<Raider>(16709, 58973, 10879);
            T.VifilGuardians = _ => [guard];
            var raider = Npc<Raider>(27200, 61280, 10351);
            var live = new List<GameNPC> { vifil };
            Assert.That(Decide(raider, vifil, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "no party, no claim: fight him");
        }

        // ---- Fornfrusenen ----------------------------------------------------------------------------------------------

        [Test]
        public void ShardsDieFirstWhileHeIsAboveThirtyFivePercentAndEveryoneFightsClose()
        {
            var forn = Npc<Fornfrusenen>(49618, 32876, 10859);
            var shards = new[] { Npc<FornfrusenenShard>(49650, 32900, 10859), Npc<FornfrusenenShard>(49580, 32840, 10859),
                Npc<FornfrusenenShard>(49600, 32950, 10859) };
            var live = new List<GameNPC> { forn };
            live.AddRange(shards);
            var melee = Npc<Raider>(49700, 32876, 10859);
            var picks = Enumerable.Range(0, 3).Select(p => Decide(melee, forn, live, Warrior, party: p)).ToArray();
            Assert.That(picks.All(a => a.Action == T.BotAction.Retarget && a.Call == "epic_forn_shards"), Is.True);
            Assert.That(picks.Select(a => a.Target).Distinct().Count(), Is.EqualTo(3), "each party takes its own shard");

            var caster = Npc<Raider>(51000, 32876, 10859); // 1,382 from his spot
            var onShard = Decide(caster, shards[0], live, Runemaster);
            Assert.That(onShard.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(onShard.StandAt.Value, new Vector3(49617, 32874, 10859)), Is.LessThan(T.FornShardReach));

            forn.Hp = 30;
            var late = Decide(melee, shards[0], live, Warrior);
            Assert.That((late.Action, late.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)forn)), "shards no longer hurt him");
            var close = Decide(caster, forn, live, Runemaster);
            Assert.That((close.Action, close.Call), Is.EqualTo((T.BotAction.Reposition, "epic_close_in")));
            Assert.That(Flat(close.StandAt.Value, forn), Is.LessThan(T.FornfrusenenReach));
            Move(caster, close.StandAt.Value.X, close.StandAt.Value.Y, close.StandAt.Value.Z);
            Assert.That(Decide(caster, forn, live, Runemaster).Action, Is.EqualTo(T.BotAction.None), "close: cast");
        }

        // ---- Council Vagn and Nokkvi --------------------------------------------------------------------------------------

        [Test]
        public void CastersFightNokkviAndEveryoneElseFightsVagn()
        {
            var vagn = Npc<Vagn>(24412, 35856, 12921);
            var nokkvi = Npc<Nokkvi>(24061, 35640, 12918);
            var live = new List<GameNPC> { vagn, nokkvi };
            var r = Npc<Raider>(24200, 35700, 12920);
            Assert.That(Decide(r, vagn, live, Runemaster).Target, Is.SameAs(nokkvi));
            Assert.That(Decide(r, vagn, live, Wizard).Call, Is.EqualTo("epic_council_split"));
            Assert.That(Decide(r, nokkvi, live, Warrior).Target, Is.SameAs(vagn));
            Assert.That(Decide(r, nokkvi, live, (int)eCharacterClass.Hunter).Target, Is.SameAs(vagn), "arrows are melee damage");
            Assert.That(Decide(r, vagn, live, Warrior).Action, Is.EqualTo(T.BotAction.None));
            Assert.That(Decide(r, nokkvi, live, Runemaster).Action, Is.EqualTo(T.BotAction.None));
            nokkvi.Dead = true;
            Assert.That(Decide(r, vagn, live, Runemaster).Action, Is.EqualTo(T.BotAction.None), "the other is dead: hit what is left");
        }

        // ---- Summoner's Hall -------------------------------------------------------------------------------------------

        [Test]
        public void TheRaiderTheGhoulCrawlsTowardWalksAwayAndTheShadeDiesFirst()
        {
            var lossren = Npc<SummonerLossren>(39276, 41166, 16001);
            var ghoul = Npc<ExplodeUndead>(39000, 41000, 16001);
            var chased = Npc<Raider>(39300, 41000, 16001); // 300 from it
            var bystander = Npc<Raider>(39250, 41100, 16001);
            ghoul.TargetObject = chased;
            var live = new List<GameNPC> { lossren, ghoul };
            var kite = Decide(chased, lossren, live, Warrior);
            Assert.That((kite.Action, kite.Call), Is.EqualTo((T.BotAction.Reposition, "epic_ghoul_kite")));
            Assert.That(Flat(kite.StandAt.Value, ghoul), Is.EqualTo(T.GhoulKiteRadius + T.SpreadStepOut).Within(1));
            Assert.That(Decide(bystander, lossren, live, Warrior).Action, Is.EqualTo(T.BotAction.None));
            Assert.That(Decide(bystander, lossren, live, Runemaster).Target, Is.SameAs(ghoul), "casters kill it (ranged add)");

            var gov = Npc<GrandSummonerGovannon>(32050, 40863, 15468);
            var shade = Npc<ShadeOfAelfgar>(32128, 41667, 15491);
            var govLive = new List<GameNPC> { gov, shade };
            var melee = Npc<Raider>(32100, 40900, 15468);
            Assert.That(Decide(melee, gov, govLive, Warrior).Target, Is.SameAs(shade));
            Assert.That(T.RouteTarget(gov, govLive, new(32100, 40900, 15468), out _), Is.SameAs(shade));
            var sacrifice = Npc<SummonedSacrifice>(31018, 40889, 15491);
            govLive.Add(sacrifice);
            Assert.That(Decide(melee, gov, govLive, Runemaster).Target, Is.SameAs(sacrifice), "casters still stop the walkers first");
            shade.Dead = true;
            Assert.That(Decide(melee, gov, govLive, Warrior).Action, Is.EqualTo(T.BotAction.None));
        }

        // ---- Hibernia --------------------------------------------------------------------------------------------------

        [Test]
        public void KrackenschteinIsFoughtFromCloseByAndAvartacksTargetFacesHim()
        {
            var kr = Npc<Krackenschtein>(31773, 37385, 16000);
            var live = new List<GameNPC> { kr };
            var caster = Npc<Raider>(32773, 37385, 16000);
            var a = Decide(caster, kr, live, Runemaster);
            Assert.That(a.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(a.StandAt.Value, kr), Is.LessThan(T.KrackenschteinReach));
            var melee = Npc<Raider>(31900, 37385, 16000);
            Assert.That(Decide(melee, kr, live, Warrior).Action, Is.EqualTo(T.BotAction.None));
            Assert.That((T.TurnedAway(180), T.TurnedAway(150), T.TurnedAway(10), T.TurnedAway(350), T.TurnedAway(90)),
                Is.EqualTo((true, true, false, false, false)));
        }

        [Test]
        public void MyrddraxisBreathsAndTheBlackLadysStrikeAreAvoided()
        {
            var myr = Npc<Myrddraxis>(32302, 32221, 15635);
            var tank = Npc<Raider>(32500, 32221, 15635);
            myr.TargetObject = tank;
            var live = new List<GameNPC> { myr };
            var r = Npc<Raider>(32302, 32800, 15635);
            myr.Hp = 90;
            Assert.That(Decide(r, myr, live, Warrior, now: 1_000).Action, Is.EqualTo(T.BotAction.None));
            myr.Hp = 82;
            var w = Decide(r, myr, live, Warrior, now: 2_000);
            Assert.That(w.Action, Is.EqualTo(T.BotAction.StandOff));
            Assert.That(Flat(w.StandAt.Value, myr), Is.EqualTo(2150 + 200).Within(1));
            Assert.That(Decide(tank, myr, live, Warrior, now: 2_000).Action, Is.EqualTo(T.BotAction.None), "his target's party takes it");

            var lady = Npc<BlackLady>(30086, 37378, 15001);
            var ladyTank = Npc<Raider>(30200, 37378, 15001);
            lady.TargetObject = ladyTank;
            var healer = Npc<Raider>(30300, 37450, 15001);
            var s = Decide(healer, lady, new List<GameNPC> { lady }, Healer);
            Assert.That(s.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Flat(s.StandAt.Value, ladyTank), Is.EqualTo(350 + T.SpreadStepOut).Within(1));
        }

        // ---- the recovery waits out crowd control ----------------------------------------------------------------------

        [Test]
        public void ABotDraggedToKulasThroneWaitsOutTheMezzThenWalksBack()
        {
            Vector3 circle = T.KulaFrostPoint, home = new(34136, 57000, 12025);
            var plan = R.Plan.For(DisplacementKind.Teleport, 0, circle, 0, 0, true);
            R.Observation See(long now, Vector3 at, bool held) => new(now, true, at, home, false, false, false, (_, _) => true, held);
            Assert.That(R.Step(plan, See(0, circle, false)).Kind, Is.EqualTo(R.OrderKind.Land));
            Assert.That(R.Step(plan, See(1_000, circle, false)).Kind, Is.EqualTo(R.OrderKind.Walk));
            // Mezzed for 80 s where it stands: held, never the "no progress" fallback.
            for (long now = 2_000; now <= 82_000; now += 1_000)
                Assert.That(R.Step(plan, See(now, circle, true)).Kind, Is.EqualTo(R.OrderKind.Hold), now.ToString());
            Assert.That(R.Step(plan, See(83_000, circle, false)).Kind, Is.EqualTo(R.OrderKind.Walk), "free: it walks out");
            // Held for good: the fallback ends it at the cap.
            var chained = R.Plan.For(DisplacementKind.Teleport, 0, circle, 0, 0, true);
            R.Step(chained, See(0, circle, false));
            R.Step(chained, See(1_000, circle, false));
            R.Order last = default;
            for (long now = 2_000; now <= R.MaximumHeldWalkMilliseconds + 10_000 && chained.Stage == R.Stage.Walking; now += 1_000)
                last = R.Step(chained, See(now, circle, true));
            Assert.That(chained.Stage, Is.EqualTo(R.Stage.Recalling));
            Assert.That(last.Reason, Is.EqualTo("held too long").Or.EqualTo("walk back took too long"));
        }

        [Test]
        public void TheErrandAndCallHelpersKeepTheirGaps()
        {
            Assert.That(T.MechanicCallDue(0, 5_000), Is.True);
            Assert.That(T.MechanicCallDue(5_000, 30_000), Is.False);
            Assert.That(T.MechanicCallDue(5_000, 5_000 + T.MechanicCallGapMilliseconds), Is.True);
            Assert.That(T.CloseIn(new(1000, 0, 0), Vector3.Zero, 200), Is.EqualTo((Vector3?)new Vector3(120, 0, 0)));
            Assert.That(T.CloseIn(new(150, 0, 0), Vector3.Zero, 200), Is.Null);
            Assert.That(T.AwayFrom(new(100, 0, 0), Vector3.Zero, 800), Is.EqualTo((Vector3?)new Vector3(1050, 0, 0)));
            Assert.That(T.AwayFrom(new(900, 0, 0), Vector3.Zero, 800), Is.Null);
            Assert.That(T.IsMagicDealer(Runemaster) && T.IsMagicDealer((int)eCharacterClass.Eldritch) && !T.IsMagicDealer(Warrior) &&
                        !T.IsMagicDealer(Healer) && !T.IsMagicDealer((int)eCharacterClass.Thane), Is.True);
        }
    }
}
