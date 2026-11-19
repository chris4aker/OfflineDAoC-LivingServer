using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Darkness Falls raid AI (1.2, DF raid wave; Legion's own rules are elsewhere): High Lord Oro and his copies, High
    /// Lord Saeor's death heal, High Lord Baln's wing, the pet-eating princes, the raid's trail for late members and the
    /// route's objective rules. The tests stand bosses and bots in a test region and play the pure rules; no world tick,
    /// combat, spell or pathing runs. Tests only: nothing here was seen in game.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_DarknessFallsRaid
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 2_600;
        private EpicTestServerScope _server;

        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool Dead;
            public ICharacterClass Class = new ClassArmsman();
            public override bool IsAlive => !Dead;
            public override eRealm Realm { get => eRealm.Hibernia; set { } }
            public override ICharacterClass CharacterClass => Class;
            public override void StopMoving() { }
            public override bool IsCasting => false;
        }

        public sealed class TestOro : HighLordOro { public override bool IsAlive => true; }
        public sealed class TestOroCopy : HighLordOroClone
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class TestBaln : HighLordBaln
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }
        public sealed class TestDuke : GameEpicNPC { public override bool IsAlive => true; }
        public sealed class Trash : GameNPC { public override bool IsAlive => true; }
        public sealed class TestSaeor : HighLordSaeor { public override bool IsAlive => true; }
        public sealed class TestLegion : DOL.GS.Scripts.Legion
        {
            public bool Fighting;
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            UseRegion(249, (0, 0));
            AutonomousDarknessFallsRaidTrail.ForgetAll();
        }

        [TearDown]
        public void TearDown()
        {
            AutonomousDarknessFallsRaidTrail.ForgetAll();
            _server.Dispose();
        }

        private void UseRegion(ushort id, params (int X, int Y)[] zones)
        {
            _region = new Region(new RegionData { Id = id, Name = "DF raid test", Description = "DF raid test", Mobs = [] });
            ushort zoneId = (ushort)(id * 10);
            foreach (var (x, y) in zones)
                _region.Zones.Add(new Zone(_region, zoneId++, "DF raid test", x, y, 65536, 65536, zoneId, false, 0, false, 0, 0, 0, 0, 0));
        }

        private T Npc<T>(int x, int y, int z) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
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
            npc.SubZoneObject = new SubZoneObject(npc);
            Zone zone = _region.Zones.First(z => x >= z.XOffset && x < z.XOffset + z.Width && y >= z.YOffset && y < z.YOffset + z.Height);
            Assert.That(zone.AddObject(npc), Is.True);
            SubZoneObject sub = npc.SubZoneObject;
            sub.DestinationSubZone.AddObject(sub.Node);
            zone.OnObjectAddedToZone();
            sub.OnSubZoneTransition();
            return npc;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static Vector3 At(GameObject o) => new(o.X, o.Y, o.Z);

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray()));
        }

        // ---- High Lord Oro and his copies ------------------------------------------------------------------------------

        [Test]
        public void TheRouteFightsTheRealOroNeverACopy()
        {
            var oro = Npc<TestOro>(45048, 25215, 15924);
            var copy = Npc<TestOroCopy>(44848, 25414, 15860);
            GameNPC[] live = [oro, copy];
            Assert.That(EpicEncounterTactics.DungeonRoute(copy, live, out string why), Is.SameAs(oro));
            Assert.That(why, Does.Contain("copy"));
            copy.Fighting = true;
            Assert.That(EpicEncounterTactics.DungeonRoute(copy, live, out _), Is.SameAs(oro), "a copy that fights the raid too");
            Assert.That(EpicEncounterTactics.DungeonRoute(oro, live, out _), Is.Null, "Oro himself is fought");
            var far = Npc<TestOroCopy>(10_000, 10_000, 15860);
            Assert.That(EpicEncounterTactics.DungeonRoute(far, [far, oro], out _), Is.Null, "a copy 35,000 away is not his");
        }

        [Test]
        public void RaidersOnACopyTheyDontHoldTurnToOroAndCastersKeepOutOfHisDrain()
        {
            var oro = Npc<TestOro>(45048, 25215, 15924);
            var copy = Npc<TestOroCopy>(44848, 25414, 15860);
            Vector3[] oros = [At(oro), At(copy)];
            Vector3 beside = new(44900, 25414, 15860);
            var a = EpicEncounterTactics.OroAdvice(beside, true, false, false, oro, oros, copy);
            Assert.That(a?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Retarget));
            Assert.That(a.Value.Target, Is.SameAs(oro));
            Assert.That(EpicEncounterTactics.OroAdvice(beside, true, true, false, oro, oros, copy), Is.Null,
                "the party the copy fights holds it");
            var drain = EpicEncounterTactics.OroAdvice(beside, false, false, true, oro, oros, oro);
            Assert.That(drain?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition), "a caster 52 from a copy steps out");
            Assert.That(Flat(drain.Value.StandAt.Value, At(copy)), Is.EqualTo(EpicEncounterTactics.OroDrainStandOff).Within(1));
            Assert.That(drain.Value.Target, Is.SameAs(oro), "it keeps its target and fights from there");
            Assert.That(EpicEncounterTactics.OroAdvice(new(46_000, 25215, 15924), false, false, true, oro, oros, oro), Is.Null,
                "a caster 952 off is clear of every drain");
            Assert.That(EpicEncounterTactics.OroAdvice(beside, false, false, false, oro, oros, oro), Is.Null,
                "melee fight in the drain");
            Assert.That(EpicEncounterTactics.OroDrainStepOut, Is.GreaterThan(EpicEncounterTactics.OroDrainRadius));
        }

        [Test]
        public void ABotOnACopyIsSentToOroByTheDecision()
        {
            var oro = Npc<TestOro>(45048, 25215, 15924);
            var copy = Npc<TestOroCopy>(44848, 25414, 15860);
            var bot = Npc<Bot>(44700, 25414, 15860);
            var advice = EpicEncounterTactics.Decide(bot, copy, [oro, copy], 0, false, 1_000);
            Assert.That(advice.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Retarget));
            Assert.That(advice.Target, Is.SameAs(oro));
        }

        // ---- High Lord Saeor --------------------------------------------------------------------------------------------

        [Test]
        public void AWoundedRaiderStepsOutOfSaeorsDeathHealUntilHealed()
        {
            Assert.That(EpicEncounterTactics.KillHealStepsOut(false, 39, false), Is.True);
            Assert.That(EpicEncounterTactics.KillHealStepsOut(false, 60, false), Is.False);
            Assert.That(EpicEncounterTactics.KillHealStepsOut(true, 60, false), Is.True, "once out it waits for 75%");
            Assert.That(EpicEncounterTactics.KillHealStepsOut(true, 75, false), Is.False);
            Assert.That(EpicEncounterTactics.KillHealStepsOut(false, 10, true), Is.False, "his target stays: running drags him");

            var saeor = Npc<TestSaeor>(49796, 38134, 15925);
            var near = EpicEncounterTactics.SaeorWoundedAdvice(new(50_200, 38134, 15925), false, saeor);
            Assert.That(near?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(Flat(near.Value.StandAt.Value, At(saeor)), Is.EqualTo(EpicEncounterTactics.SaeorWoundedStandOff).Within(1));
            Assert.That(EpicEncounterTactics.SaeorWoundedStandOff, Is.GreaterThan(EpicEncounterTactics.SaeorDeathHealRadius));
            Vector3 out1 = new(49796 + 1_400, 38134, 15925);
            Assert.That(EpicEncounterTactics.SaeorWoundedAdvice(out1, true, saeor), Is.Null, "a caster out there fights on");
            var melee = EpicEncounterTactics.SaeorWoundedAdvice(out1, false, saeor);
            Assert.That(melee?.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff));
            Assert.That(melee.Value.StandAt, Is.Null, "already out: it waits there");
        }

        // ---- High Lord Baln ---------------------------------------------------------------------------------------------

        [Test]
        public void BalnsNamedWingIsClearedBeforeHim()
        {
            var baln = Npc<TestBaln>(35351, 39179, 14958);
            var duke = Npc<TestDuke>(35351 + 1_955, 39179, 14958);
            var farDuke = Npc<TestDuke>(35351 + 3_500, 39179, 14958);
            var trash = Npc<Trash>(35351 + 500, 39179, 14958);
            GameNPC[] live = [baln, duke, farDuke, trash];
            Assert.That(EpicEncounterTactics.ClearFirstOwner(duke, live), Is.SameAs(baln));
            Assert.That(EpicEncounterTactics.ClearFirstOwner(farDuke, live), Is.Null, "outside his 3,000 call");
            Assert.That(EpicEncounterTactics.ClearFirstOwner(trash, live), Is.Null, "the wing's trash comes with his fight");
            baln.Fighting = true;
            Assert.That(EpicEncounterTactics.ClearFirstOwner(duke, live), Is.Null, "his fight is on: no more clearing first");
            Assert.That(EpicEncounterTactics.BalnCalls(false, true, 3_000), Is.True);
            Assert.That(EpicEncounterTactics.BalnCalls(false, false, 100), Is.False, "not his kind");
            Assert.That(EpicEncounterTactics.For("HighLordBaln")?.RangedAdds, Does.Contain("BalnMinion"));
            Assert.That(EpicEncounterTactics.For("HighLordBaln")?.AreaOnClump, Is.False, "area-on-clump stays Spindler's (shared caster AI)");
        }

        // ---- the princes ------------------------------------------------------------------------------------------------

        [Test]
        public void OnlyNecromancersAndAnimistsStandOffTheOtherPetClassesReleaseTheirPet()
        {
            Assert.That(EpicEncounterTactics.PetIsTheFight((int)eCharacterClass.Necromancer), Is.True);
            Assert.That(EpicEncounterTactics.PetIsTheFight((int)eCharacterClass.Animist), Is.True);
            foreach (var c in new[] { eCharacterClass.Druid, eCharacterClass.Hunter, eCharacterClass.Theurgist, eCharacterClass.Bonedancer,
                         eCharacterClass.Spiritmaster, eCharacterClass.Enchanter, eCharacterClass.Cabalist, eCharacterClass.Minstrel })
                Assert.That(EpicEncounterTactics.PetIsTheFight((int)c), Is.False, c.ToString());
            Assert.That(EpicEncounterTactics.AllDungeonRules.Any(r => r.Targets.Contains(nameof(PrinceBaalorien)) && r.Act != null), Is.True,
                "a rule on the princes releases the pet");
        }

        [Test]
        public void NoSummonNearAFightingPrince()
        {
            var turret = new Spell(new DbSpell { Type = eSpellType.SummonAnimistPet.ToString(), Name = "Grove's Executioner", Target = "Self" }, 50);
            var nuke = new Spell(new DbSpell { Type = eSpellType.DirectDamage.ToString(), Name = "Nuke", Target = "Enemy" }, 50);
            Assert.That(EpicEncounterTactics.IsPetSummon(turret), Is.True);
            Assert.That(EpicEncounterTactics.IsPetSummon(nuke), Is.False);
            Vector3 prince = new(24868, 32144, 16086);
            (Vector3, bool)[] fighting = [(prince, true)];
            Assert.That(EpicEncounterTactics.SummonFeeds(true, fighting, new(26_000, 32144, 16086), null), Is.True);
            Assert.That(EpicEncounterTactics.SummonFeeds(true, fighting, new(30_000, 32144, 16086), prince), Is.True,
                "the caster is far but its target is the prince");
            Assert.That(EpicEncounterTactics.SummonFeeds(true, fighting, new(30_000, 32144, 16086), null), Is.False);
            Assert.That(EpicEncounterTactics.SummonFeeds(true, [(prince, false)], new(25_000, 32144, 16086), null), Is.False,
                "out of combat he eats nothing");
            Assert.That(EpicEncounterTactics.SummonFeeds(false, fighting, new(25_000, 32144, 16086), null), Is.False);
            var bot = Npc<Bot>(25_000, 32144, 16086);
            Assert.That(EpicEncounterTactics.SummonFeedsPrince(bot, turret), Is.False,
                "a bot that isn't an autonomous gamebot (a companion) summons as its player decides");
            string cast = Source("bots", "GameBot.cs");
            Assert.That(cast, Does.Contain("EpicEncounterTactics.SummonFeedsPrince(this, spell)"), "GameBot.CastSpell's gate");
        }

        // ---- the raid's trail -------------------------------------------------------------------------------------------

        [Test]
        public void TheTrailKeepsTheRaidsWayWithoutItsLoops()
        {
            var trail = new AutonomousDarknessFallsRaidTrail();
            Assert.That(trail.Record(new(0, 0, 0)), Is.True);
            Assert.That(trail.Record(new(100, 0, 0)), Is.False, "closer than a step");
            trail.Record(new(500, 0, 0));
            trail.Record(new(1_000, 0, 0));
            trail.Record(new(1_000, 500, 0));
            trail.Record(new(1_000, 1_000, 0));
            Assert.That(trail.Points.Count, Is.EqualTo(5));
            Assert.That(trail.Record(new(550, 50, 0)), Is.True, "back by 500,0: the loop is cut");
            Assert.That(trail.Points, Is.EqualTo(new Vector3[] { new(0, 0, 0), new(500, 0, 0) }));
            trail.Record(new(500, 0, 600));
            Assert.That(trail.Points.Count, Is.EqualTo(3), "the same spot on another floor is no loop");
            Assert.That(trail.ChainTo(new(9, 9, 9)).Last(), Is.EqualTo(new Vector3(9, 9, 9)), "the post comes last");
            Assert.That(new AutonomousDarknessFallsRaidTrail().ChainTo(new(1, 1, 1)), Is.Null, "too short a trail");
            Assert.That(trail.Record(new(float.NaN, 0, 0)), Is.False);
        }

        [Test]
        public void TheLeadRaiderIsTheOneNearestTheFront()
        {
            Vector3 front = new(10_000, 10_000, 0);
            Assert.That(AutonomousDarknessFallsRaidTrail.Leader([new(10_900, 10_000, 0), new(10_100, 10_000, 0)], front),
                Is.EqualTo(new Vector3(10_100, 10_000, 0)));
            Assert.That(AutonomousDarknessFallsRaidTrail.Leader([new(20_000, 10_000, 0)], front), Is.Null, "nobody at the front");
            Assert.That(AutonomousDarknessFallsRaidTrail.Leader(null, front), Is.Null);
        }

        [Test]
        public void EachRealmsRaidRecordsItsOwnTrailAndANewRaidStartsAgain()
        {
            var route = new object();
            var lead = Npc<Bot>(10_000, 10_000, 0);
            for (int i = 0; i < 4; i++)
            {
                lead.X = 10_000 + i * 500;
                lead.movementComponent.ForceUpdatePosition();
                AutonomousDarknessFallsRaidTrail.Observe(route, eRealm.Hibernia, [lead], new(lead.X, lead.Y, lead.Z), i * 3_000);
            }
            Assert.That(AutonomousDarknessFallsRaidTrail.TryChainTo(eRealm.Hibernia, new(13_000, 10_000, 0), out Vector3[] chain), Is.True);
            Assert.That(chain.Length, Is.EqualTo(5));
            Assert.That(AutonomousDarknessFallsRaidTrail.TryChainTo(eRealm.Midgard, new(13_000, 10_000, 0), out _), Is.False);
            AutonomousDarknessFallsRaidTrail.Observe(new object(), eRealm.Hibernia, [lead], new(lead.X, lead.Y, lead.Z), 20_000);
            Assert.That(AutonomousDarknessFallsRaidTrail.TryChainTo(eRealm.Hibernia, new(13_000, 10_000, 0), out _), Is.False,
                "a new raid's trail starts empty");
            lead.Dead = true;
            AutonomousDarknessFallsRaidTrail.Observe(route, eRealm.Albion, [lead], new(lead.X, lead.Y, lead.Z), 0);
            Assert.That(AutonomousDarknessFallsRaidTrail.TryChainTo(eRealm.Albion, new(1, 1, 1), out _), Is.False, "the dead lead nobody");
        }

        [Test]
        public void ARaidMembersChainGoesRoundALegItCantWalkButASoloRouteDoesnt()
        {
            Vector3[] chain = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0), new(4, 0, 0), new(5, 0, 0)];
            Assert.That(AutonomousWorldBotController.NextWalkableWaypoint(chain, 1, 3, p => p.X == 3), Is.EqualTo(3));
            Assert.That(AutonomousWorldBotController.NextWalkableWaypoint(chain, 1, 3, p => p.X == 5), Is.Null, "only three ahead");
            Assert.That(AutonomousWorldBotController.NextWalkableWaypoint(chain, 4, 3, p => p.X == 5), Is.EqualTo(5));
            Assert.That(AutonomousWorldBotController.IsRaidChainKey("raid:Hibernia:1:2:3"), Is.True);
            Assert.That(AutonomousWorldBotController.IsRaidChainKey("in:Hibernia:123"), Is.False, "a solo grind's route");
            Assert.That(AutonomousWorldBotController.IsRaidChainKey(null), Is.False);
            string routes = Source("bots", "autonomous", "AutonomousWorldBotDarknessFallsRoutes.cs");
            Assert.That(routes, Does.Contain("AutonomousDarknessFallsRaidTrail.TryChainTo(bot.Realm, post, out Vector3[] trail)"),
                "a late member with no certified chain walks the raid's trail");
        }

        // ---- the route's objectives -------------------------------------------------------------------------------------

        [Test]
        public void OnceTheRaidMustersForLegionAFarObjectiveThatCameUpIsLeft()
        {
            Assert.That(AutonomousDarknessFallsRaidRoute.LeavesForFinal(true, false, 20_000), Is.True);
            Assert.That(AutonomousDarknessFallsRaidRoute.LeavesForFinal(false, false, 20_000), Is.False, "before the final's muster");
            Assert.That(AutonomousDarknessFallsRaidRoute.LeavesForFinal(true, true, 20_000), Is.False, "one fighting the raid is answered");
            Assert.That(AutonomousDarknessFallsRaidRoute.LeavesForFinal(true, false, 2_000), Is.False, "one near the front is taken");
        }

        [Test]
        public void TheRouteWiresTheDarknessFallsRules()
        {
            // CI checks the source out with Windows line endings; compare it with Unix ones.
            string route = Source("bots", "autonomous", "RealmRaidDungeonRoute.cs").Replace("\r\n", "\n");
            Assert.That(route, Does.Contain("AutonomousDarknessFallsRaidTrail.Observe(this, _realm, participants, Front, now)"));
            Assert.That(route, Does.Contain("_objectives != null && RealmRaidNeutralEvents.IsObjective(_objectives, n.Name)) &&\n" +
                                            "                  _clearedNames.Contains(n.Name)"),
                "a cleared objective's name is done whatever its class (Oro's copies)");
            Assert.That(route, Does.Contain("RealmRaidNeutralEvents.IsObjective(_objectives, n.Name) || clearFirst.ContainsKey(n)"),
                "an objective raid kills a boss's clear-first monsters too");
            Assert.That(route, Does.Contain(".OrderBy(item => WalkEstimate(encounter, item.Approach))"), "floors first in the chain search");
            Assert.That(route, Does.Contain("!LeftForTheFinal(n, now)"));
        }

        [Test]
        public void TheRaidRulesAndCatalogRowsAreThere()
        {
            var rules = EpicEncounterTactics.AllDungeonRules;
            Assert.That(rules.Single(r => r.Encounter == nameof(HighLordOro)).Route, Is.Not.Null);
            Assert.That(rules.Single(r => r.Encounter == nameof(HighLordOro)).RoutesInCombat, Is.True);
            Assert.That(rules.Single(r => r.Encounter == nameof(HighLordSaeor)).Decide, Is.Not.Null);
            Assert.That(rules.Single(r => r.Encounter == nameof(HighLordBaln)).ClearFirstOf, Is.Not.Null);
            Assert.That(EpicEncounterTactics.For("Beliathan")?.RangedAdds, Does.Contain("BeliathanMinion"));
            Assert.That(EpicEncounterTactics.For("PrincessNahemah")?.RangedAdds, Does.Contain("NahemahMinion"));
            Assert.That(EpicEncounterTactics.For("HighLordBaelerdoth")?.SpreadRadius, Is.EqualTo(350));
            Assert.That(EpicEncounterTactics.KillHealBosses, Does.Contain(nameof(HighLordSaeor)));
        }

        // ---- Legion's antechamber (route order only; Legion's own rules are another file's) ------------------------------

        [Test]
        public void LegionsAntechamberIsClearedBeforeHisPullAndAgainIfItRespawns()
        {
            var legion = Npc<TestLegion>(45027, 51899, 15468);
            var chamberlain = Npc<TestDuke>(45546, 51300, 15469);
            var marquis = Npc<TestDuke>(46913, 49904, 15457);
            var adremal = Npc<TestDuke>(45057, 45808, 15470);
            var peaceful = Npc<TestDuke>(45300, 51600, 15469);
            typeof(GameNPC).GetField("m_flags", Hidden)!.SetValue(peaceful, GameNPC.eFlags.PEACE);
            GameNPC[] live = [legion, chamberlain, marquis, adremal, peaceful];
            Assert.That(EpicEncounterTactics.ClearFirstOwner(chamberlain, live), Is.SameAs(legion));
            Assert.That(EpicEncounterTactics.ClearFirstOwner(marquis, live), Is.SameAs(legion), "2,745 from his home");
            Assert.That(EpicEncounterTactics.ClearFirstOwner(adremal, live), Is.Null, "the Grand Chancellor is 6,091 away: fought on the way");
            Assert.That(EpicEncounterTactics.ClearFirstOwner(peaceful, live), Is.Null, "a peaceful NPC never holds his pull");
            legion.Fighting = true;
            Assert.That(EpicEncounterTactics.ClearFirstOwner(chamberlain, live), Is.Null, "his fight is on");
            string route = Source("bots", "autonomous", "RealmRaidDungeonRoute.cs");
            Assert.That(route, Does.Contain("is up again: cleared before the pull"), "the muster clears a respawned one before it goes in");
        }
    }
}
