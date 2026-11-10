using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The forced Albion Krondon raid of the owner's PC test, 2026-11-09 (RC3, build b72716e): a hub that waited 38 minutes
    /// for its last member, a member held in Avalon City for 30 minutes, slow walks between packs and to the musters, 100
    /// raiders burned in Orshom's Fire, and melee walking under Scurceol Hyrde's floating orbs. Unit tests of the rules only:
    /// no world tick, combat or pathing.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_ForcedKrondonRaidFixes
    {
        private const long Minute = 60_000L;
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        // ---- 1. the forced raid's hub and staging -------------------------------------------------------------------

        [TestCase(200, 0, true)]                 // everyone there: at once, as before
        [TestCase(199, 14, false)]               // inside the patience window it waits for all 200
        [TestCase(199, 15, true)]                // PC: 199 of 200 from 01:45 on; now it leaves
        [TestCase(180, 15, true)]                // the late-start share of a full raid (90%)
        [TestCase(179, 29, false)]
        [TestCase(100, 30, true)]                // last call: half
        [TestCase(99, 30, false)]
        [TestCase(99, 24 * 60, false)]           // never fewer than half: it is still a raid
        public void AForcedRaidStopsWaitingForItsLastMembers(int present, int minutes, bool goes) =>
            Assert.That(RealmRaidRecruitmentPolicy.ForcedGoesNow(present, 200, minutes * Minute), Is.EqualTo(goes));

        [Test]
        public void TheForcedHubLeavesByThePatienceRuleAndTheAutomaticHubIsUnchanged()
        {
            // The PC rally: 125 at 01:30 (10 minutes in), 199 from 01:45 (25 minutes in) until the owner killed the straggler.
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 125, 10 * Minute, 200, forced: true), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 199, 25 * Minute, 200, forced: true), Is.True);
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 199, 0, 200, forced: true), Is.False, "not at once");
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 200, 0, 200, forced: true), Is.True);
            // Automatic raids keep their 30-minute patience with half (the smaller of 120 of 200 and half of what they need).
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 199, 25 * Minute, 200), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 100, RealmRaidRecruitmentPolicy.HubPatienceMilliseconds, 200), Is.True);
            Assert.That(RealmRaidRecruitmentPolicy.DepartHub(false, 99, RealmRaidRecruitmentPolicy.HubPatienceMilliseconds, 200), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.ForcedLastCallMilliseconds, Is.EqualTo(RealmRaidRecruitmentPolicy.HubPatienceMilliseconds));
        }

        [Test]
        public void TheForcedStagingWaitsFromTheHubDepartureAndTheDragonMustStillLand()
        {
            Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 0, 200, true), Is.True);
            Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 14 * Minute, 199, true), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 15 * Minute, 190, true), Is.True);
            Assert.That(RealmRaidRecruitmentPolicy.Ready(true, 15 * Minute, 190, false), Is.False, "a dragon must land");
            // Automatic: the 15-minute minimum and the 75-minute late start, unchanged.
            Assert.That(RealmRaidRecruitmentPolicy.Ready(false, 14 * Minute, 200, true), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.Ready(false, 74 * Minute, 199, true), Is.False);
            Assert.That(RealmRaidRecruitmentPolicy.Ready(false, 75 * Minute, 180, true), Is.True);
        }

        [TestCase(200, 200)]    // PC: 25 posts at Caer Diogel, 25 parties of 8
        [TestCase(160, 160)]    // a hub with 20 posts would have needed 200 it can never hold
        [TestCase(296, 200)]    // never above what it was asked for
        [TestCase(8, 8)]        // one post: one party, never more than the posts hold
        public void AForcedRaidStagesWhatItsHubCanHold(int posted, int required) =>
            Assert.That(RealmRaidRecruitmentPolicy.ForcedRequiredForPosts(200, posted), Is.EqualTo(required));

        [Test]
        public void TheForcedRallyTellsThePlayerHowItsWaitEnds()
        {
            Assert.That(AutonomousRealmRaid.ForcedStartCondition(true), Does.Contain("dungeon").And.Contain("15 minutes").And.Contain("half"));
            Assert.That(AutonomousRealmRaid.ForcedStartCondition(false), Does.Contain("dragon must land"));
        }

        // ---- 2. the hold before a dungeon threat ---------------------------------------------------------------------

        [Test]
        public void TheThreatHoldIsOneHoldWhicheverMonsterIsInTheWay()
        {
            long start = 1_000_000;
            long since = AutonomousDungeonPolicy.ThreatHoldStart(0, 0, start);
            Assert.That(since, Is.EqualTo(start));
            // Scans every 0.75-1 s while the first blocker alternates between two roaming monsters: the clock keeps running.
            long last = start;
            for (long now = start + 900; now <= start + 95_000; now += 900)
            {
                since = AutonomousDungeonPolicy.ThreatHoldStart(since, last, now);
                last = now;
            }
            Assert.That(since, Is.EqualTo(start));
            Assert.That(AutonomousDungeonPolicy.HoldsForPuller(true, last - since, AutonomousWorldBotController.DungeonThreatHoldLimitMilliseconds),
                Is.False, "the 90 s limit ends it");
            Assert.That(AutonomousDungeonPolicy.HoldsForPuller(true, 89_000, AutonomousWorldBotController.DungeonThreatHoldLimitMilliseconds), Is.True);
            // After a fight or a walk (no hold scan for more than 5 s) the next hold is a new one.
            long later = last + AutonomousDungeonPolicy.ThreatHoldGapMilliseconds + 1;
            Assert.That(AutonomousDungeonPolicy.ThreatHoldStart(since, last, later), Is.EqualTo(later));
        }

        [Test]
        public void NobodyHoldsForAPullerInAnotherRegion()
        {
            // Ferken in Avalon City, his party and its puller at Caer Diogel: no hold at all.
            Assert.That(AutonomousDungeonPolicy.HoldsForPuller(false, 0, AutonomousWorldBotController.DungeonThreatHoldLimitMilliseconds), Is.False);
        }

        [TestCase(false, false, false, false, true)]  // a raid that is not holding pulls as before
        [TestCase(true, false, true, false, false)]   // holding outside its dungeon: still no pull
        [TestCase(true, true, true, false, true)]     // at a crossing, as before
        [TestCase(true, false, true, true, true)]     // its own dungeon's trash on the walk to a muster: the puller pulls
        [TestCase(true, false, false, true, false)]   // a camp pull (no corridor blocker) still waits for the order
        public void AHeldRaidClearsOrdinaryTrashOnItsWalkInItsOwnDungeon(bool hold, bool crossing, bool blocker, bool raidTrash, bool may) =>
            Assert.That(AutonomousDungeonPolicy.ExpeditionMayClearCorridor(hold, crossing, blocker, raidTrash), Is.EqualTo(may));

        // ---- 3. the raid's next pack ---------------------------------------------------------------------------------

        [Test]
        public void TheRaidClearsItsOwnFloorBeforeItChangesFloors()
        {
            // PC 02:30: the raid stood by 45043,31561,14625; the route took the atelic ooze 600 across and 1,500 down at
            // 44509,31798,13111, and nobody stood within 1,000 of it for four minutes, while a foreman 4,200 away on the
            // raid's own floor waited (45049,31280,14636 was taken next, then the raid went back down).
            var front = new Vector3(45043, 31561, 14625);
            var oozeBelow = new Vector3(44509, 31798, 13111);
            var cempaSameFloor = new Vector3(44923, 34286, 14599); // 2,700 across, same floor
            var farSameFloor = new Vector3(39186, 33970, 16733 - 2100); // 6,300 across, same floor
            Assert.That(Vector3.Distance(front, oozeBelow), Is.LessThan(Vector3.Distance(front, cempaSameFloor)), "the straight line picked the ooze");
            Assert.That(RealmRaidDungeonRoute.WalkEstimate(front, cempaSameFloor), Is.LessThan(RealmRaidDungeonRoute.WalkEstimate(front, oozeBelow)));
            Assert.That(RealmRaidDungeonRoute.WalkEstimate(front, farSameFloor), Is.LessThan(RealmRaidDungeonRoute.WalkEstimate(front, oozeBelow)));
            // Within the floor band only the distance across counts.
            Assert.That(RealmRaidDungeonRoute.WalkEstimate(front, front + new Vector3(300, 400, 250)), Is.EqualTo(500).Within(0.01));
        }

        // ---- 4 and 5. Orshom's Fire and the floating orbs -------------------------------------------------------------

        public sealed class FixTwin : GameEpicBoss
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class FixFire : GameEpicNPC
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class FixShielded : GameEpicBoss
        {
            public override bool IsAlive => true;
            public override bool InCombat => true;
        }
        public sealed class FixOrb : GameEpicNPC
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class FixRaider : GameNPC
        {
            public override bool IsAlive => true;
        }

        private Region _region;
        private ushort _nextId = 300;
        private EpicTestServerScope _server;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 61, Name = "Krondon fix test", Description = "Krondon fix test", Mobs = [] });
            EpicEncounterTactics.Register(new("FixTwin", "test/twin", [], 0, null, Hazards: ["FixFire"], HazardRadius: 300, HazardDamageType: "Cold"));
            EpicEncounterTactics.Register(new("FixShielded", "test/shielded", ["FixOrb"], 0, null));
            EpicEncounterTactics.ResetBlastForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterTactics.Unregister("FixTwin");
            EpicEncounterTactics.Unregister("FixShielded");
            EpicEncounterTactics.ResetBlastForTests();
            _server.Dispose();
        }

        private T Npc<T>(int x, int y, int z, bool flying = false) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            if (flying) typeof(GameNPC).GetField("m_flags", Hidden).SetValue(npc, GameNPC.eFlags.FLYING);
            // No brain (the route asks for one: a brain type marks some hazards).
            typeof(GameNPC).GetField("m_brains", Hidden)?.SetValue(npc, new System.Collections.ArrayList());
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

        private static List<Spell> Spells(eDamageType type) =>
            [new Spell(new DbSpell { Name = "test bolt", Damage = 200, DamageType = (int)type, Type = eSpellType.DirectDamage.ToString(),
                Target = eSpellTarget.ENEMY.ToString(), Range = 1500 }, 50)];

        [Test]
        public void TheCatalogMarksOrshomsFireAsAColdOnlyHazardOfTheTwins()
        {
            var fire = (OrshomFire)RuntimeHelpers.GetUninitializedObject(typeof(OrshomFire));
            EpicEncounterTactics.Hazard rule = EpicEncounterTactics.HazardOf(fire);
            Assert.That(rule, Is.Not.Null, "Orshom's Fire is a ground hazard");
            Assert.That(rule.Radius, Is.EqualTo(300), "its Fire Burn radius (OrylleAndOrshom.cs)");
            Assert.That(rule.VulnerableTo, Is.EqualTo(eDamageType.Cold));
            Assert.That(rule.Owners, Is.EquivalentTo(new[] { "Orshom", "Orylle" }));
            Assert.That(EpicEncounterTactics.IsHazard((Orshom)RuntimeHelpers.GetUninitializedObject(typeof(Orshom))), Is.False);
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (var t in EpicEncounterTactics.Catalog.Where(t => t.HasHazards))
                foreach (string type in t.Hazards)
                    Assert.That(gs.GetTypes().Any(x => x.Name == type && typeof(GameNPC).IsAssignableFrom(x)), Is.True, type);
        }

        [Test]
        public void OnlyColdCastersShootTheFireAndOnlyFromOutsideIt()
        {
            // The real spots: the fire at 31406,69599, Orshom Brong's spawn about 900 away.
            var twin = Npc<FixTwin>(31697, 70458, 15610);
            var fire = Npc<FixFire>(31406, 69599, 15605);
            twin.Fighting = fire.Fighting = true;
            var live = new List<GameNPC> { twin, fire };
            var meleeInIt = Npc<FixRaider>(31477, 69574, 15611);  // where Ferken burned: 75 from it
            var meleeOutside = Npc<FixRaider>(31406, 70300, 15605);
            var wizardInIt = Npc<FixRaider>(31500, 69650, 15605);
            var wizardOutside = Npc<FixRaider>(31406, 70500, 15605);
            wizardInIt.HarmfulSpells = Spells(eDamageType.Cold);
            wizardOutside.HarmfulSpells = Spells(eDamageType.Cold);
            var fireMage = Npc<FixRaider>(31406, 70500, 15605);
            fireMage.HarmfulSpells = Spells(eDamageType.Heat);

            var a = EpicEncounterTactics.Decide(meleeInIt, fire, live, 0, false);
            Assert.That(a.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(a.StandAt, Is.Not.Null);
            Assert.That(Vector2.Distance(new(a.StandAt.Value.X, a.StandAt.Value.Y), new(fire.X, fire.Y)),
                Is.GreaterThanOrEqualTo(300 + EpicEncounterTactics.HazardMargin), "out of its radius with room");
            var b = EpicEncounterTactics.Decide(meleeOutside, fire, live, 0, false);
            Assert.That((b.Action, b.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)twin)), "fight the twin, not the fire");
            Assert.That(EpicEncounterTactics.Decide(fireMage, fire, live, 0, true).Target, Is.SameAs(twin), "heat does nothing to it");
            Assert.That(EpicEncounterTactics.Decide(wizardInIt, fire, live, 0, true).Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(EpicEncounterTactics.Decide(wizardOutside, fire, live, 0, true).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "a cold caster outside it shoots it");
            // With its twins dead it is simply left.
            twin.Fighting = false;
            var alone = EpicEncounterTactics.DecideHazard(meleeOutside, fire, [fire], false);
            Assert.That((alone.Action, alone.StandAt), Is.EqualTo((EpicEncounterTactics.BotAction.StandOff, (Vector3?)null)));
        }

        [Test]
        public void ARaiderStandingInTheFireStepsOutAndNoPostGoesInIt()
        {
            var fire = Npc<FixFire>(31406, 69599, 15605);
            EpicEncounterTactics.ObserveHazards([fire]);
            Assert.That(EpicEncounterTactics.HazardAt(61, new Vector3(31477, 69574, 15611)), Is.SameAs(fire));
            Assert.That(EpicEncounterTactics.HazardAt(61, new Vector3(31406, 69599 + 449, 15605)), Is.SameAs(fire), "the margin counts");
            Assert.That(EpicEncounterTactics.HazardAt(61, new Vector3(31406, 69599 + 451, 15605)), Is.Null);
            Assert.That(EpicEncounterTactics.HazardAt(61, new Vector3(31406, 69599, 15605 + 1500)), Is.Null, "another floor");
            Assert.That(EpicEncounterTactics.HazardAt(62, new Vector3(31406, 69599, 15605)), Is.Null, "another region");
            Vector3 exit = EpicEncounterTactics.HazardExit(new(31406, 69599, 15605), 300, new(31477, 69574, 15611));
            Assert.That(Vector2.Distance(new(exit.X, exit.Y), new(31406, 69599)), Is.EqualTo(300 + EpicEncounterTactics.HazardExitDistance).Within(0.5));
            Assert.That(exit.X, Is.GreaterThan(31406), "away from it, on the raider's side");
        }

        [Test]
        public void MeleeOnAFlyingRequiredAddHoldsAtTheStandInsteadOfWalkingUnderIt()
        {
            var boss = Npc<FixShielded>(51941, 20068, 17174);
            var orb = Npc<FixOrb>(52713, 20841, 17511, flying: true);
            var live = new List<GameNPC> { boss, orb };
            var melee = Npc<FixRaider>(52656, 21354, 16990); // under the orb, on the floor below its perch
            var stand = new Vector3(50984, 21741, 17169); // the PC's 03:16:53 ranged stand
            EpicEncounterTactics.SetStand(boss, stand);
            var advice = EpicEncounterTactics.Decide(melee, orb, live, 0, canShootFlyers: false);
            Assert.That((advice.Action, advice.StandAt), Is.EqualTo((EpicEncounterTactics.BotAction.StandOff, (Vector3?)stand)));
            Assert.That(EpicEncounterTactics.Decide(melee, orb, live, 0, canShootFlyers: true).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None),
                "a shooter fights it");
            Assert.That(EpicEncounterTactics.IsRequiredAddClass(orb), Is.True);
            Assert.That(EpicEncounterTactics.IsRequiredAddClass(boss), Is.False);
        }

        [Test]
        public void TheRouteNeverTurnsOnAHazardOrARequiredAddThatFightsTheRaid()
        {
            var route = (RealmRaidDungeonRoute)Activator.CreateInstance(typeof(RealmRaidDungeonRoute), Hidden, null,
                [(ushort)61, null, Vector3.Zero, Vector3.Zero, Array.Empty<string>(), null, eRealm.Albion], null);
            MethodInfo hostile = typeof(RealmRaidDungeonRoute).GetMethod("HostileEncounter", Hidden);
            var raider = Npc<FixRaider>(31500, 69700, 15605);
            var twin = Npc<FixTwin>(31697, 70458, 15610);
            var fire = Npc<FixFire>(31406, 69599, 15605);
            var boss = Npc<FixShielded>(51941, 20068, 17174);
            var orb = Npc<FixOrb>(52713, 20841, 17511, flying: true);
            // (Field initializers don't run on these test objects: set what the fight needs.)
            orb.Fighting = true;
            fire.Fighting = true;
            twin.Fighting = true;
            fire.TargetObject = raider;
            orb.TargetObject = raider;
            GameLiving[] raiders = [raider];
            // The fire and the orb fight the raid, and neither is taken as an encounter to turn on.
            Assert.That(hostile.Invoke(route, [new GameNPC[] { fire, orb, boss }, raiders]), Is.Null);
            // A real encounter that fights the raid still is (the twin, PC 04:18:17 "it is fighting the raid").
            twin.TargetObject = raider;
            Assert.That(hostile.Invoke(route, [new GameNPC[] { fire, orb, boss, twin }, raiders]), Is.SameAs(twin));
        }
    }
}
