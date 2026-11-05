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
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterTactics.Unregister("TacticBoss");
            EpicEncounterTactics.Unregister("TacticTree");
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

            // The route: the boss and the tree both send the raid to the add nearest its front.
            Assert.That(EpicEncounterTactics.RouteTarget(boss, live, front, out string why), Is.SameAs(orbA));
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

        // ---- restored mechanics ------------------------------------------------------------------------------------

        [Test]
        public void EveryRestoredMechanicIsOffByDefault()
        {
            Assert.That(EpicEncounterMechanics.RestoreSwitches, Has.Length.EqualTo(17));
            foreach (string name in EpicEncounterMechanics.RestoreSwitches)
            {
                PropertyInfo p = typeof(EpicEncounterMechanics).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
                Assert.That(p, Is.Not.Null, name);
                Assert.That((bool)p.GetValue(null), Is.False, name);
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
