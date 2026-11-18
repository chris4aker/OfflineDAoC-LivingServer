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
    /// Galladoria, owner's PC, build 5378dbe, 2026-11-17 (epic-hibernia, lost at Xaga). Neither Aroon the Urlamhai nor Xaga's
    /// room was ever pulled as an attempt: an add in combat during the muster sent the route straight to it, and all five
    /// wipes were scored "raid-wide deaths outside an attempt". These stand a boss with two required adds (Xaga with Beatha and
    /// Tine, as test doubles) in a test region and play the route's decisions; they don't run a world tick. Tests only: not
    /// yet seen on the owner's PC.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidAttempts5378
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 500;
        private EpicTestServerScope _server;

        public sealed class FairyQueen : GameEpicBoss
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }
        public sealed class FairyVoid : GameEpicBoss
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }
        public sealed class FairyFire : GameEpicBoss
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 62, Name = "Attempt test", Description = "Attempt test", Mobs = [] });
            // As the catalog's galladoria/xaga row: two required adds, killed in order.
            EpicEncounterTactics.Register(new("FairyQueen", "test/fairyqueen", ["FairyVoid", "FairyFire"], 0, null) { AddsInOrder = true });
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterTactics.Unregister("FairyQueen");
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
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        // Xaga's spawn and her two fairies about 100-200 from her (27482,54821,12935 in Galladoria).
        private (FairyQueen Xaga, FairyVoid Beatha, FairyFire Tine, List<GameNPC> Live) Room()
        {
            var xaga = Npc<FairyQueen>(27482, 54821, 12935);
            var beatha = Npc<FairyVoid>(27580, 54740, 12935);
            var tine = Npc<FairyFire>(27400, 54950, 12935);
            return (xaga, beatha, tine, new List<GameNPC> { xaga, beatha, tine });
        }

        // ---- A: a boss's adds are fought only inside an attempt on it ---------------------------------------------------

        [Test]
        public void RequiredAddsAreFoughtOnlyInsideACommittedAttempt()
        {
            Assert.Multiple(() =>
            {
                // A boss with a muster: no attempt open (the muster at 2 of 86), the adds wait for the pull.
                Assert.That(RealmRaidDungeonRoute.FightsRequiredAddsNow(ownerMustered: true, attemptOnOwner: false, skirmishHeld: false), Is.False);
                // The raid holds at the muster for a skirmish (EarlyPull.Hold): still not sent in.
                Assert.That(RealmRaidDungeonRoute.FightsRequiredAddsNow(true, true, true), Is.False);
                // FINAL_ENGAGE or an early-pull Commit opened the attempt: the adds now, as before.
                Assert.That(RealmRaidDungeonRoute.FightsRequiredAddsNow(true, true, false), Is.True);
                // A boss the route fights with no muster of its own is as before.
                Assert.That(RealmRaidDungeonRoute.FightsRequiredAddsNow(false, false, false), Is.True);
            });
        }

        [Test]
        public void AnAddFightingDuringTheMusterIsAnEarlyPullOfItsBoss()
        {
            var (xaga, beatha, tine, live) = Room();
            Vector3 front = new(27719, 53444, 12814);
            // 00:09:36-00:09:48: Beatha caught the two raiders teleported beside her; Xaga herself out of combat.
            beatha.Fighting = true;
            Assert.Multiple(() =>
            {
                Assert.That(EpicEncounterTactics.FightOn(xaga, live), Is.True, "the fight is on: the muster's early-pull rule answers it");
                // What the route switched to on 5378dbe with no attempt open (REALM_EXPEDITION_TACTIC from="Xaga" to="Beatha").
                Assert.That(EpicEncounterTactics.RouteTarget(xaga, live, front, out string why), Is.SameAs(beatha));
                Assert.That(why, Does.Contain("required-adds"));
                // Now it is no switch: no attempt is open on her.
                Assert.That(RealmRaidDungeonRoute.FightsRequiredAddsNow(true, false, false), Is.False);
                // The muster's answer with 2 at the fight and a raid of 124 of which only a handful are free to come: Hold.
                int needed = RealmRaidDungeonRoute.FinalMusterNeeded(124, 0);
                Assert.That(EpicEncounterTactics.ChooseEarlyPull(2, 14, 124, false, false, needed), Is.EqualTo(EpicEncounterTactics.EarlyPull.Hold));
                // A muster's worth inside: Commit, and then Beatha (her adds die in order: Beatha first).
                Assert.That(EpicEncounterTactics.ChooseEarlyPull(2, needed, 124, false, false, needed), Is.EqualTo(EpicEncounterTactics.EarlyPull.Commit));
            });
            // Inside the attempt the adds come in order; with Beatha dead, Tine.
            Assert.That(RealmRaidDungeonRoute.FightsRequiredAddsNow(true, true, false), Is.True);
            beatha.Dead = true;
            tine.Fighting = true;
            Assert.That(EpicEncounterTactics.RouteTarget(xaga, live, front, out _), Is.SameAs(tine));
        }

        [Test]
        public void AFightThatStoppedSendsTheRouteBackToTheBossToJudgeTheAttempt()
        {
            var (xaga, beatha, tine, live) = Room();
            // A wipe in the attempt: every raider at Beatha fell and the room went quiet (out of combat).
            Assert.Multiple(() =>
            {
                Assert.That(EpicEncounterTactics.FightOn(xaga, live), Is.False);
                // The route's condition for going back to the boss (whose muster ends the attempt as a wipe and regroups).
                Assert.That(EpicEncounterTactics.RequiredAddOwner(beatha, live), Is.SameAs(xaga));
                Assert.That(EpicEncounterTactics.RouteTarget(xaga, live, default, out _), Is.SameAs(xaga), "the boss, not an add");
                // A boss with required adds is fought as an encounter with a muster, never as one of her adds.
                Assert.That(EpicEncounterTactics.IsRequiredAddOfLiveBoss(xaga, live), Is.False);
                Assert.That(EpicEncounterTactics.IsRequiredAddOfLiveBoss(tine, live), Is.True);
            });
        }

        [Test]
        public void EveryBossWithRequiredAddsTheOwnerNamedHasAMuster()
        {
            // The gate applies to a boss the route musters for (an epic encounter); these are the ones the fix must not break.
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (var (boss, add) in new[] { ("Xaga", "Beatha"), ("Xaga", "Tine"), ("Aroon", "CorpScaith"), ("Aroon", "ScorScaith"),
                         ("ScurceolHyrde", "LyftMihtOne"), ("SoulReckoner", "ReckonedSoul") })
            {
                var tactic = EpicEncounterTactics.For(boss);
                Assert.That(tactic?.RequiredAdds, Does.Contain(add), $"{boss}: {add} is a required add");
                Type type = gs.GetTypes().Single(t => t.Name == boss && typeof(GameNPC).IsAssignableFrom(t));
                Assert.That(typeof(IGameEpicNpc).IsAssignableFrom(type), Is.True, $"{boss} is an epic encounter (mustered)");
            }
        }
    }
}
