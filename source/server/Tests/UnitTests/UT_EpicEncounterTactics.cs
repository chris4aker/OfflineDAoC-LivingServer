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
    /// Epic encounter tactics (EpicEncounterTactics, fed by docs/encounters) and the original mechanics restored behind
    /// EpicEncounterMechanics switches (EpicEncounterRestores). The scenario tests stand a boss, its adds and a small raid
    /// in a test region and play the fight through the same decisions the route and the bots make; they don't run a world
    /// tick, combat or pathing.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicEncounterTactics
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 100;
        private EpicTestServerScope _server;

        // Test doubles named apart from the real scripts; their tactics are registered like catalog rows.
        public sealed class TacticBoss : GameEpicBoss
        {
            public bool Dead, Fighting;
            public byte Hp = 100;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
            public override byte HealthPercent => Hp;
        }
        public sealed class TacticWizard : GameEpicBoss
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class TacticGiant : GameEpicBoss
        {
            public byte Hp = 100;
            public bool Fighting = true;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
            public override byte HealthPercent => Hp;
        }
        public sealed class TacticIllusion : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class TacticOrbA : GameEpicNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class TacticOrbB : GameEpicNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class TacticTree : GameEpicBoss
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 61, Name = "Tactic test", Description = "Tactic test", Mobs = [] });
            EpicEncounterTactics.Register(new("TacticBoss", "test/boss", ["TacticOrbA", "TacticOrbB"], 1500, null));
            EpicEncounterTactics.Register(new("TacticTree", "test/tree", [], 0, "TacticBoss"));
            EpicEncounterTactics.Register(new("TacticWizard", "test/wizard", ["TacticIllusion"], 0, null, KeepAlive: 1));
            EpicEncounterTactics.Register(new("TacticGiant", "test/giant", [], 0, null, 0, [80, 40], 2500));
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterTactics.Unregister("TacticBoss");
            EpicEncounterTactics.Unregister("TacticTree");
            EpicEncounterTactics.Unregister("TacticWizard");
            EpicEncounterTactics.Unregister("TacticGiant");
            _server.Dispose();
        }

        private T Npc<T>(int x, int y, int z, bool flying = false) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            if (flying) typeof(GameNPC).GetField("m_flags", Hidden).SetValue(npc, GameNPC.eFlags.FLYING);
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            Move(npc, x, y, z);
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private static void Move(GameNPC npc, int x, int y, int z)
        {
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
        }

        // ---- data from the catalog ---------------------------------------------------------------------------------

        [Test]
        public void TheCatalogFeedsTheFirstTactics()
        {
            var scurceol = EpicEncounterTactics.All.Single(t => t.Boss == "ScurceolHyrde");
            Assert.That(scurceol.RequiredAdds, Is.EqualTo(new[] { "LyftMihtOne", "LyftMihtTwo", "LyftMihtThree", "LyftMihtFour" }));
            Assert.That(scurceol.Encounter, Is.EqualTo("krondon/scurceolhyrde"));
            Assert.That(EpicEncounterTactics.All.Single(t => t.Boss == "BlackLady").FightAtHomeRadius, Is.EqualTo(1100));
            Assert.That(EpicEncounterTactics.All.Single(t => t.Boss == "AncientBlackOak").DiesWith, Is.EqualTo("BlackLady"));
            // Every class the catalog names exists in the server.
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (var t in EpicEncounterTactics.Catalog)
                foreach (string type in t.RequiredAdds.Append(t.Boss).Concat(t.DiesWith == null ? [] : [t.DiesWith]))
                    Assert.That(gs.GetTypes().Any(x => x.Name == type && typeof(GameNPC).IsAssignableFrom(x)), Is.True, type);
        }

        [Test]
        public void PartiesSpreadOverTheAddsStillAlive()
        {
            int[] spread = Enumerable.Range(0, 8).Select(i => EpicEncounterTactics.AssignedIndex(i, 4)).ToArray();
            Assert.That(spread, Is.EqualTo(new[] { 0, 1, 2, 3, 0, 1, 2, 3 }));
            Assert.That(EpicEncounterTactics.AssignedIndex(7, 1), Is.EqualTo(0));
            Assert.That(EpicEncounterTactics.AssignedIndex(-5, 3), Is.InRange(0, 2));
            Assert.That(EpicEncounterTactics.AssignedIndex(3, 0), Is.EqualTo(-1));
        }

        [Test]
        public void FightAtHomeIsADistanceFromTheSpawn()
        {
            Assert.That(EpicEncounterTactics.OutsideHome(1100, 1101), Is.True);
            Assert.That(EpicEncounterTactics.OutsideHome(1100, 1100), Is.False);
            Assert.That(EpicEncounterTactics.OutsideHome(0, 9000), Is.False);
        }

        // ---- the raid harness ---------------------------------------------------------------------------------------

        [Test]
        public void RequiredAddsComeFirstThenTheBoss()
        {
            var boss = Npc<TacticBoss>(50000, 20000, 17000);
            var orbA = Npc<TacticOrbA>(49000, 21000, 17600, flying: true);
            var orbB = Npc<TacticOrbB>(51000, 19000, 17600, flying: true);
            var tree = Npc<TacticTree>(52000, 20000, 17000);
            var live = new List<GameNPC> { boss, orbA, orbB, tree };
            var caster = Npc<Raider>(49900, 20100, 17000);
            var melee = Npc<Raider>(50050, 20050, 17000);
            Vector3 front = new(49900, 20100, 17000);

            // Before the fight is on the raid musters for the boss itself.
            Assert.That(EpicEncounterTactics.RouteTarget(boss, live, front, out string why), Is.SameAs(boss));
            boss.Fighting = true;
            // The route: the boss and the tree both send the raid to the add nearest its front.
            Assert.That(EpicEncounterTactics.RouteTarget(boss, live, front, out why), Is.SameAs(orbA));
            Assert.That(why, Does.Contain("required-adds").And.Contain("alive=2"));
            Assert.That(EpicEncounterTactics.RouteTarget(tree, live, front, out why), Is.SameAs(orbA));
            Assert.That(why, Does.StartWith("dies-with"));
            Assert.That(EpicEncounterTactics.Covers(boss, orbA, live), Is.True, "the boss attacking the raid is no reason to leave the orb");
            Assert.That(EpicEncounterTactics.IsRequiredAddOfLiveBoss(orbB, live), Is.True, "no muster of its own");

            // The bots: party 0 takes orb A, party 1 orb B; a caster shoots its orb, melee stands off under it.
            var a0 = EpicEncounterTactics.Decide(caster, boss, live, 0, canShootFlyers: true);
            Assert.That((a0.Action, a0.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)orbA)));
            var a1 = EpicEncounterTactics.Decide(caster, boss, live, 1, canShootFlyers: true);
            Assert.That(a1.Target, Is.SameAs(orbB));
            var m1 = EpicEncounterTactics.Decide(melee, boss, live, 1, canShootFlyers: false);
            Assert.That(m1.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff), "never hit the boss while an orb lives");
            Assert.That(m1.StandAt, Is.EqualTo(new Vector3(orbB.X, orbB.Y, melee.Z)));
            // With a ranged stand from the route, those who can't shoot wait there instead (not under an orb in the air).
            var standSpot = new Vector3(50500, 19500, 17000);
            EpicEncounterTactics.SetStand(boss, standSpot);
            Assert.That(EpicEncounterTactics.Decide(melee, boss, live, 1, canShootFlyers: false).StandAt, Is.EqualTo(standSpot));
            // Each orb has its own stand too: a shooter out of reach of its orb (1,667 from orb B) walks there first, then shoots.
            var standB = new Vector3(50600, 19300, 17000);
            EpicEncounterTactics.SetStand(orbB, standB);
            var walk = EpicEncounterTactics.Decide(caster, boss, live, 1, canShootFlyers: true);
            Assert.That((walk.Action, walk.Target, walk.StandAt), Is.EqualTo((EpicEncounterTactics.BotAction.StandOff, (GameNPC)orbB, (Vector3?)standB)));
            var atStand = Npc<Raider>(50650, 19350, 17000);
            var shoot = EpicEncounterTactics.Decide(atStand, boss, live, 1, canShootFlyers: true);
            Assert.That((shoot.Action, shoot.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)orbB)), "778 from it: in reach");
            Assert.That(EpicEncounterTactics.Decide(melee, boss, live, 1, canShootFlyers: false).StandAt, Is.EqualTo(standSpot), "melee wait at the route's stand");
            Assert.That(EpicEncounterTactics.Decide(caster, tree, live, 0, true).Target, Is.SameAs(boss), "the tree dies with the boss");

            // Orb A dies: everyone left goes to orb B.
            orbA.Dead = true;
            Assert.That(EpicEncounterTactics.RouteTarget(boss, live, front, out _), Is.SameAs(orbB));
            Assert.That(EpicEncounterTactics.Decide(caster, boss, live, 0, true).Target, Is.SameAs(orbB));

            // The last orb dies: now the boss.
            orbB.Dead = true;
            Assert.That(EpicEncounterTactics.RouteTarget(boss, live, front, out why), Is.SameAs(boss));
            Assert.That(why, Is.Null);
            Assert.That(EpicEncounterTactics.Decide(melee, boss, live, 1, false).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            Assert.That(EpicEncounterTactics.IsRequiredAddOfLiveBoss(orbB, live), Is.False);
        }

        [Test]
        public void ABossDrawnFromHomeIsLedBackNotFought()
        {
            var boss = Npc<TacticBoss>(50000, 20000, 17000);
            var live = new List<GameNPC> { boss };
            var chased = Npc<Raider>(51700, 20000, 17000);
            var other = Npc<Raider>(51800, 20000, 17000);
            Move(boss, 51600, 20000, 17000); // 1,600 from its spawn, past its 1,500 home radius (and inside any tether: TetherRange 0)
            boss.TargetObject = chased;
            Assert.That(EpicEncounterTactics.OutsideHome(boss), Is.True);
            Assert.That(EpicEncounterTactics.Decide(chased, boss, live, 0, false).Action, Is.EqualTo(EpicEncounterTactics.BotAction.LeadHome));
            Assert.That(EpicEncounterTactics.Decide(other, boss, live, 0, true).Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Move(boss, 51000, 20000, 17000);
            Assert.That(EpicEncounterTactics.Decide(other, boss, live, 0, true).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
        }

        [Test]
        public void EncountersWithoutATacticAreLeftAlone()
        {
            var orb = Npc<TacticOrbA>(1, 1, 1);
            Assert.That(EpicEncounterTactics.For(orb), Is.Null);
            Assert.That(EpicEncounterTactics.RouteTarget(orb, [orb], default, out string why), Is.SameAs(orb));
            Assert.That(why, Is.Null);
            Assert.That(EpicEncounterTactics.Decide(Npc<Raider>(0, 0, 0), orb, [orb], 0, true).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
        }

        [Test]
        public void TheLastIllusionIsLeftAliveAndTheWizardFought()
        {
            var wizard = Npc<TacticWizard>(31353, 37634, 14873);
            var i1 = Npc<TacticIllusion>(31649, 37316, 14866);
            var i2 = Npc<TacticIllusion>(31080, 37974, 14866);
            var i3 = Npc<TacticIllusion>(31000, 37300, 14866);
            var live = new List<GameNPC> { wizard, i1, i2, i3 };
            var raider = Npc<Raider>(31400, 37000, 14866);
            wizard.Fighting = true;
            Assert.That(EpicEncounterTactics.RouteTarget(wizard, live, new(31400, 37000, 14866), out _), Is.Not.SameAs(wizard));
            Assert.That(EpicEncounterTactics.Decide(raider, wizard, live, 2, true).Target, Is.SameAs(i3), "party 2 takes the third");
            i1.Dead = true;
            i2.Dead = true;
            Assert.That(EpicEncounterTactics.ShieldingAdds(wizard, live), Is.Empty, "one left: kept alive");
            Assert.That(EpicEncounterTactics.RouteTarget(wizard, live, default, out _), Is.SameAs(wizard));
            Assert.That(EpicEncounterTactics.RouteTarget(i3, live, default, out string why), Is.SameAs(wizard));
            Assert.That(why, Does.StartWith("keep-alive"));
            var onKept = EpicEncounterTactics.Decide(raider, i3, live, 0, true);
            Assert.That((onKept.Action, onKept.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)wizard)),
                "nobody kills the last illusion (a new set comes only when all four are dead)");
        }

        [Test]
        public void AnEarlyPullIsAnsweredByTheWholeRaid()
        {
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(31, 87, resetsWhenLeft: true), Is.EqualTo(EpicEncounterTactics.EarlyPull.Disengage),
                "a third of the raid on a boss the pull-back shakes off: pull back");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(31, 87, resetsWhenLeft: false), Is.EqualTo(EpicEncounterTactics.EarlyPull.Commit),
                "one that never resets would chase: everyone in");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(45, 83, resetsWhenLeft: true), Is.EqualTo(EpicEncounterTactics.EarlyPull.Commit),
                "half the raid is already there: everyone in");
            var aidon = EpicEncounterTactics.Leashes.Single(l => l.Boss == "AidonTheArchwizard");
            Assert.That((aidon.TetherRange, aidon.Resets), Is.EqualTo((1500, true)), "from the catalog");
        }

        [Test]
        public void APullBackInsideTheTetherIsNoPullBack()
        {
            // Fuladl, Krondon 2026-11-06 02:02: the 24 at his muster (1,399 from his spawn) fought him and all fell while 93
            // were elsewhere. He resets when left, but only past his 3,800 tether: pulled back to the muster he follows.
            var fuladl = EpicEncounterTactics.Leashes.Single(l => l.Boss == "Fuladl");
            Assert.That((fuladl.TetherRange, fuladl.Resets), Is.EqualTo((3800, true)), "from the catalog");
            bool shakes = EpicEncounterTactics.BreakOffShakesOff(fuladl.Resets, fuladl.TetherRange, 1399);
            Assert.That(shakes, Is.False);
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(24, 117, shakes), Is.EqualTo(EpicEncounterTactics.EarlyPull.Commit), "everyone in at once");
            Assert.That(RealmRaidDungeonRoute.IsWipe(24, 24, 100, 94, 117), Is.False, "and the 24 alone are a lost skirmish, not an attempt");
            Assert.That(EpicEncounterTactics.BreakOffShakesOff(true, 1500, 1400), Is.False, "Aidon's home muster is inside his 1,500 too");
            Assert.That(EpicEncounterTactics.BreakOffShakesOff(true, 1000, 1400), Is.True, "a short leash is left behind");
            Assert.That(EpicEncounterTactics.BreakOffShakesOff(false, 1000, 1400), Is.False, "one that never resets chases");
        }

        // Ranged stands for floating required adds (Scurceol's orbs): UT_ScurceolOrbStands, on the real Krondon mesh.

        [Test]
        public void ABreakOffSendsRaidersToTheMusterFromTheBossAndItsAdds()
        {
            var wizard = Npc<TacticWizard>(31353, 37634, 14873);
            var illusion = Npc<TacticIllusion>(31649, 37316, 14866);
            var other = Npc<TacticIllusion>(31080, 37974, 14866);
            var live = new List<GameNPC> { wizard, illusion, other };
            var raider = Npc<Raider>(31400, 37000, 14866);
            var rally = new Vector3(30927, 35039, 15365);
            EpicEncounterTactics.BreakOff(wizard, rally, 10_000);
            try
            {
                var onBoss = EpicEncounterTactics.Decide(raider, wizard, live, 0, true, 5_000);
                Assert.That((onBoss.Action, onBoss.StandAt), Is.EqualTo((EpicEncounterTactics.BotAction.StandOff, (Vector3?)rally)));
                Assert.That(EpicEncounterTactics.Decide(raider, illusion, live, 0, true, 5_000).StandAt, Is.EqualTo(rally));
                Assert.That(EpicEncounterTactics.BrokenOff(wizard, 10_001), Is.Null, "it ends");
            }
            finally { EpicEncounterTactics.EndBreakOff(wizard); }
        }

        [Test]
        public void AWipeIsTheRaidsOnlyWhenHalfOfItWasThereOrFell()
        {
            Assert.That(RealmRaidDungeonRoute.IsWipe(31, 31, 100, 99, 87), Is.False, "Aidon 2026-11-05: a lost skirmish");
            Assert.That(RealmRaidDungeonRoute.IsWipe(31, 31, 100, 99), Is.True, "the old rule called it a wipe");
            Assert.That(RealmRaidDungeonRoute.IsWipe(60, 31, 100, 99, 87), Is.True, "most of the raid in it, half fell");
            Assert.That(RealmRaidDungeonRoute.IsWipe(40, 40, 100, 99, 70), Is.True, "half the raid fell");
            Assert.That(RealmRaidDungeonRoute.IsWipe(60, 10, 60, 50, 87), Is.False, "most standing, boss hurt");
        }

        [Test]
        public void ALeashedBossIsMusteredForAtHome()
        {
            var home = new Vector3(31353, 37634, 14873);
            var chasing = new Vector3(31568, 35248, 15369); // where Aidon was at 22:41, 2,446 from home
            Assert.That(RealmRaidDungeonRoute.MusterReference(chasing, home, 1500, resets: true), Is.EqualTo(home));
            Assert.That(RealmRaidDungeonRoute.MusterReference(chasing, home, 1500, resets: false), Is.EqualTo(chasing));
            Assert.That(RealmRaidDungeonRoute.MusterReference(home + new Vector3(100, 0, 0), home, 1500, true), Is.EqualTo(home + new Vector3(100, 0, 0)));
        }

        [Test]
        public void AFrontLeftWhereTheChaseEndedIsResolvedAgain()
        {
            // Aidon, 2026-11-05: three musters went in to the front from the 22:38 switch while he stood at home; the
            // closest raider ended 1,979 from him and he never entered combat.
            var home = new Vector3(31353, 37634, 14873);
            var front = new Vector3(31686, 35683, 14888);
            Assert.That(RealmRaidDungeonRoute.FrontIsStale(front, home, encounterInCombat: false), Is.True);
            Assert.That(RealmRaidDungeonRoute.FrontIsStale(front, home, encounterInCombat: true), Is.False, "a fight moves with the raid");
            Assert.That(RealmRaidDungeonRoute.FrontIsStale(home + new Vector3(220, 0, 0), home, false), Is.False, "an approach beside him");
            // Walking home after a reset, the front is measured from home, as the muster is.
            var walkingBack = new Vector3(31488, 36986, 14877);
            Assert.That(RealmRaidDungeonRoute.FrontIsStale(front, RealmRaidDungeonRoute.MusterReference(walkingBack, home, 1500, true), false), Is.True);
        }

        [Test]
        public void TheRaidWithdrawsFromABlastButThePartyItFightsHoldsIt()
        {
            // 1.2 (PC 2026-11-07, 110 raiders in one stomp): at least 6% above, more when the raid takes him down faster.
            Assert.That(EpicEncounterTactics.InBlastWindow([80, 40], 86, -1), Is.True, "within 6% above 80");
            Assert.That(EpicEncounterTactics.InBlastWindow([80, 40], 87, -1), Is.False);
            Assert.That(EpicEncounterTactics.InBlastWindow([80, 40], 78, 2_000), Is.True, "just crossed");
            Assert.That(EpicEncounterTactics.InBlastWindow([80, 40], 78, 10_000), Is.False, "over");
            Assert.That(EpicEncounterTactics.NextBlastBelow([80, 40], 78), Is.EqualTo(40));

            var giant = Npc<TacticGiant>(31577, 35305, 14551);
            var tank = Npc<Raider>(31600, 35305, 14551);
            var caster = Npc<Raider>(32577, 35305, 14551);
            var far = Npc<Raider>(35000, 35305, 14551);
            giant.TargetObject = tank;
            var live = new List<GameNPC> { giant };
            EpicEncounterTactics.ResetBlastForTests();
            giant.Fighting = true;
            giant.Hp = 90;
            Assert.That(EpicEncounterTactics.Decide(caster, giant, live, 0, true, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            giant.Hp = 82;
            var w = EpicEncounterTactics.Decide(caster, giant, live, 0, true, 2_000);
            Assert.That(w.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(Vector2.Distance(new(w.StandAt.Value.X, w.StandAt.Value.Y), new(giant.X, giant.Y)), Is.EqualTo(2700).Within(1));
            Assert.That(EpicEncounterTactics.Decide(tank, giant, live, 0, false, 2_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "the party it fights pushes it over");
            Assert.That(EpicEncounterTactics.Decide(far, giant, live, 0, true, 2_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "already out of reach");
            giant.Hp = 79; // crossed at 3,000
            Assert.That(EpicEncounterTactics.Decide(caster, giant, live, 0, true, 3_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(EpicEncounterTactics.Decide(caster, giant, live, 0, true, 13_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "the stomp is over: back in");
        }

        [Test]
        public void ACalledBlastWaitsForTheRaidsCallNotForHisHealth()
        {
            // 1.2 raid discipline: the window opens as before, but nobody moves until the caller has called it, a lead
            // before it lands (Xanxicar's stomp; a test giant here).
            EpicEncounterTactics.CallBlastForTests("TacticGiant");
            RaidDiscipline.FamiliarityOverride = 1;
            try
            {
                var giant = Npc<TacticGiant>(31577, 35305, 14551);
                var tank = Npc<Raider>(31600, 35305, 14551);
                var caster = Npc<Raider>(32577, 35305, 14551);
                giant.TargetObject = tank;
                var live = new List<GameNPC> { giant };
                EpicEncounterTactics.ResetBlastForTests();
                giant.Fighting = true;
                giant.Hp = 82; // in the window, but he is not losing health: no call yet
                Assert.That(EpicEncounterTactics.Decide(caster, giant, live, 0, true, 1_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
                Assert.That(EpicEncounterTactics.Decide(caster, giant, live, 0, true, 2_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                    "the window is open but nobody has called it");
                giant.Hp = 79; // crossed at 3,000: it lands BlastLandsAfterCrossSeconds later
                var call = RaidDiscipline.CallFor(0, "TacticGiant/blast", 80, 0, 1);
                long callAt = 3_000 + (long)Math.Ceiling((EpicEncounterTactics.BlastLandsAfterCrossSeconds - call.LeadSeconds) * 1000);
                if (callAt > 3_000)
                    Assert.That(EpicEncounterTactics.Decide(caster, giant, live, 0, true, 3_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                        $"called {call.LeadSeconds:0.0} s before it lands");
                var w = EpicEncounterTactics.Decide(caster, giant, live, 0, true, Math.Max(callAt, 3_000) + 250);
                Assert.That(w.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff), "out on the call");
                Assert.That(EpicEncounterTactics.Decide(tank, giant, live, 0, false, Math.Max(callAt, 3_000) + 500).Action,
                    Is.EqualTo(EpicEncounterTactics.BotAction.None), "the party it fights stays in");
                Assert.That(EpicEncounterTactics.Decide(caster, giant, live, 0, true, 13_000).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                    "the stomp is over: back in");
                Assert.That(EpicEncounterTactics.BlastEtaSeconds(85, 80, 0.5, -1), Is.EqualTo(16.8).Within(1e-9));
                Assert.That(EpicEncounterTactics.BlastEtaSeconds(85, 80, 0, -1), Is.EqualTo(double.PositiveInfinity));
                Assert.That(EpicEncounterTactics.BlastEtaSeconds(79, 80, 0.5, 2_000), Is.EqualTo(4.8).Within(1e-9));
                Assert.That(EpicEncounterTactics.BlastEtaSeconds(79, 80, 0.5, 2_000, 4_000), Is.EqualTo(4).Within(1e-9), "the cast says when");
            }
            finally
            {
                EpicEncounterTactics.UncallBlastForTests("TacticGiant");
                RaidDiscipline.FamiliarityOverride = null;
                EpicEncounterTactics.ResetBlastForTests();
            }
        }

        [Test]
        public void NosdodensWavesAreFoughtBeforeHim()
        {
            var nosdoden = EpicEncounterTactics.All.Single(t => t.Boss == "Nosdoden");
            Assert.That(nosdoden.RequiredAdds, Is.EqualTo(new[] { "NosdodenSummonedAdds" }));
            var xanxicar = EpicEncounterTactics.All.Single(t => t.Boss == "Xanxicar");
            Assert.That((xanxicar.BlastThresholds, xanxicar.BlastRadius), Is.EqualTo((new[] { 80, 60, 40, 20 }, 2500)));
            var aidon = EpicEncounterTactics.All.Single(t => t.Boss == "AidonTheArchwizard");
            Assert.That((aidon.KeepAlive, aidon.RequiredAdds.Length), Is.EqualTo((1, 4)));
        }

        // ---- restored mechanics ------------------------------------------------------------------------------------

        /// <summary>The restore switches on by default since 1.2 (the one list; the dungeons' tests check their own). Off until
        /// the owner confirms after a played check: AllowFullFirePatches (keeps his 2026-10-19 nerf) and AllowGovannonFullHealth.</summary>
        internal static readonly string[] OnSince12 =
        [
            nameof(EpicEncounterMechanics.AllowEarthmenderClassPuzzle),
            // Epic wave 2, Galladoria (UT_EpicWaveTwo.GalladoriaRestoresOn).
            nameof(EpicEncounterMechanics.AllowAroonDamageOrder), nameof(EpicEncounterMechanics.AllowGalladoriaBridgeSpot),
            nameof(EpicEncounterMechanics.AllowEarthmendersPassive),
            nameof(EpicEncounterMechanics.AllowEarthmenderQuickReset), nameof(EpicEncounterMechanics.AllowEarthmenderMutualHeal),
            nameof(EpicEncounterMechanics.AllowBeathaHealsXaga), nameof(EpicEncounterMechanics.AllowFairyFlight),
            // Tuscaran Glacier and Summoner's Hall (UT_EpicWave2MidHib).
            nameof(EpicEncounterMechanics.AllowTuscarTwinHeal), nameof(EpicEncounterMechanics.AllowKvasirColdHeal),
            // Caer Sidi (UT_EpicWave2Albion).
            // (Funus's weapon puzzle is off by default in 1.2: the owner's call, 2026-11-08.)
            nameof(EpicEncounterMechanics.AllowHorsemenTetherHeal),
            nameof(EpicEncounterMechanics.AllowSacristanPatrol), nameof(EpicEncounterMechanics.AllowSoulReckonerFullShield),
            nameof(EpicEncounterMechanics.AllowSoulReckonerSmallRoom),
        ];

        [Test]
        public void EveryRestoredMechanicIsOffByDefaultUntilTheRaidPlaysIt()
        {
            Assert.That(EpicEncounterMechanics.RestoreSwitches, Has.Length.EqualTo(18), "1.2 play test: + AllowEasmarachEndlessFallsHeal (off)");
            foreach (string name in EpicEncounterMechanics.RestoreSwitches)
            {
                PropertyInfo p = typeof(EpicEncounterMechanics).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                Assert.That(p, Is.Not.Null, name);
                // 1.2: the Earthmenders' class rule is on; the raid assigns classes to it (UT_EpicEncounterTactics3). Epic wave 2:
                // Galladoria's (UT_EpicWaveTwo), Tuscaran's and Summoner's Hall's (UT_EpicWave2MidHib), Caer Sidi's five
                // (UT_EpicWave2Albion), each with its tactic.
                Assert.That((bool)p.GetValue(null), Is.EqualTo(OnSince12.Contains(name)), name);
            }
        }

        [Test]
        public void FunusTakesOnlyTheOriginalClassesAndWeapons()
        {
            Assert.That(EpicEncounterRestores.FunusAccepts((int)eCharacterClass.Cleric, eObjectType.GenericItem), Is.True);
            Assert.That(EpicEncounterRestores.FunusAccepts((int)eCharacterClass.Scout, eObjectType.Longbow), Is.True);
            Assert.That(EpicEncounterRestores.FunusAccepts((int)eCharacterClass.Scout, eObjectType.SlashingWeapon), Is.False);
            Assert.That(EpicEncounterRestores.FunusAccepts((int)eCharacterClass.Armsman, eObjectType.Crossbow), Is.True);
            Assert.That(EpicEncounterRestores.FunusAccepts((int)eCharacterClass.Infiltrator, eObjectType.Crossbow), Is.True);
            Assert.That(EpicEncounterRestores.FunusAccepts((int)eCharacterClass.Mercenary, eObjectType.Fired), Is.True);
            Assert.That(EpicEncounterRestores.FunusAccepts((int)eCharacterClass.Wizard, eObjectType.Staff), Is.False);
            Assert.That(EpicEncounterRestores.FunusAccepts(0, eObjectType.GenericItem), Is.False, "pets heal him");
        }

        [Test]
        public void EarthmendersTakeOnlyTheirClassGroup()
        {
            var g = EpicEncounterRestores.Earthmender.Guardian;
            Assert.That(EpicEncounterRestores.EarthmenderAccepts(g, (int)eCharacterClass.Hero), Is.True);
            Assert.That(EpicEncounterRestores.EarthmenderAccepts(g, (int)eCharacterClass.Eldritch), Is.False);
            Assert.That(EpicEncounterRestores.EarthmenderAccepts(EpicEncounterRestores.Earthmender.Magical, (int)eCharacterClass.Eldritch), Is.True);
            Assert.That(EpicEncounterRestores.EarthmenderAccepts(EpicEncounterRestores.Earthmender.Natural, (int)eCharacterClass.Druid), Is.True);
            Assert.That(EpicEncounterRestores.EarthmenderAccepts(EpicEncounterRestores.Earthmender.Shadowy, (int)eCharacterClass.Ranger), Is.True);
            foreach (EpicEncounterRestores.Earthmender kind in Enum.GetValues<EpicEncounterRestores.Earthmender>())
            {
                Assert.That(EpicEncounterRestores.EarthmenderAccepts(kind, (int)eCharacterClass.Animist), Is.True);
                Assert.That(EpicEncounterRestores.EarthmenderAccepts(kind, (int)eCharacterClass.Valewalker), Is.True);
            }
        }

        [Test]
        public void AroonOpensOneDamageTypePerScaithInOrder()
        {
            bool[] dead = new bool[9];
            Assert.That(EpicEncounterRestores.AroonAccepts(eDamageType.Slash, dead), Is.False, "immune to everything at first");
            Assert.That(EpicEncounterRestores.ScaithAccepts(0, dead), Is.True);
            Assert.That(EpicEncounterRestores.ScaithAccepts(1, dead), Is.False);
            dead[0] = true;
            Assert.That(EpicEncounterRestores.AroonAccepts(eDamageType.Slash, dead), Is.True);
            Assert.That(EpicEncounterRestores.AroonAccepts(eDamageType.Thrust, dead), Is.False);
            Assert.That(EpicEncounterRestores.ScaithAccepts(1, dead), Is.True);
            dead[2] = true; // out of order: crush dead but thrust alive opens nothing more
            Assert.That(EpicEncounterRestores.AroonAccepts(eDamageType.Crush, dead), Is.False);
            for (int i = 0; i < 9; i++) dead[i] = true;
            foreach (eDamageType t in EpicEncounterRestores.AroonOrder)
                Assert.That(EpicEncounterRestores.AroonAccepts(t, dead), Is.True, t.ToString());
        }

        [Test]
        public void KvasirIsHealedByColdOnly()
        {
            Assert.That(EpicEncounterRestores.KvasirHealedBy(eDamageType.Cold), Is.True);
            Assert.That(EpicEncounterRestores.KvasirHealedBy(eDamageType.Heat), Is.False);
        }
    }
}
