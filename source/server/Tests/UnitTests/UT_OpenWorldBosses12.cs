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
using OW = DOL.GS.RealmRaidOpenWorld;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2, every big boss in (owner 2026-11-14): the open-world big bosses. Each has a raid plan (a catalog
    /// row from the hand notes, and a code rule where a field can't say it: EpicEncounterTactics.OpenWorld.cs) and, but for
    /// Morgana (a quest NPC in the period) and Fomor City's two (no path in on its navmesh), a realm raid of its own on the
    /// dragon-fight engine (RealmRaidOpenWorld), with its share of the schedule and its place in the GM sweep. Bosses, adds and
    /// raiders stand in a test region; no world tick, combat, spell or pathing runs. Nothing here was played: tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_OpenWorldBosses12
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 5000;
        // The test zone spans 65,536 units: each test's world coordinates are moved so its first NPC (the boss) stands in
        // the middle of it. The rules use distances only.
        private int _ox, _oy;
        private bool _origin;
        private EpicTestServerScope _server;

        // Test doubles carry the boss's own class name: the catalog's rows and the code rules go by it. They are made without
        // a constructor (field initializers don't run): a boss fights and stands at full health unless told otherwise.
        public class Boss : GameEpicBoss
        {
            public bool Dead, Calm;
            public byte Hp;
            public override bool IsAlive => !Dead;
            public override bool InCombat => !Calm;
            public override byte HealthPercent => Hp == 0 ? (byte)100 : Hp;
        }
        public sealed class GlacierGiant : Boss { }
        public sealed class Curengkur : Boss { }
        public sealed class SpiritOfLordEmthoro : Boss { }
        public sealed class Evern : Boss { }
        public sealed class OFGreenKnight : Boss { }
        public sealed class Myrddraxis : Boss { }
        public sealed class Mortufoghus : Boss { }
        public sealed class Marfoirwort : Boss { }
        public sealed class Mollunero : Boss { }
        public sealed class Colialt : Boss { }
        public sealed class EpicJuggernaut : Boss { }
        public class Add : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
            public override byte HealthPercent => 100;
        }
        public sealed class EvernFairy : Add { }
        public sealed class GKTrees : Add { }
        public sealed class CurengkurNest : Add { }
        public sealed class MolluneroAdd : Add { }
        public sealed class ColialtAdds : Add { }
        public sealed class MyrddraxisSecondHead : Add { }
        public sealed class MyrddraxisThirdHead : Add { }
        public sealed class MyrddraxisFourthHead : Add { }
        public sealed class MyrddraxisFifthHead : Add { }
        public sealed class Raider : GameNPC { public override bool IsAlive => true; }

        private static readonly int Warrior = (int)eCharacterClass.Warrior, Wizard = (int)eCharacterClass.Wizard,
            Healer = (int)eCharacterClass.Healer, Druid = (int)eCharacterClass.Druid, Cleric = (int)eCharacterClass.Cleric;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            UseRegion(100);
            EpicEncounterMechanics.ResetPerEncounter();
            EncounterSignals.ClearAll();
            T.ResetBlastForTests();
        }

        [TearDown]
        public void TearDown()
        {
            T.ResetBlastForTests();
            EncounterSignals.ClearAll();
            EpicEncounterMechanics.ResetPerEncounter();
            _server.Dispose();
        }

        private void UseRegion(ushort id)
        {
            _origin = false;
            _region = new Region(new RegionData { Id = id, Name = "Open-world bosses test", Description = "Open-world bosses test", Mobs = [] });
            _region.Zones.Add(new Zone(_region, (ushort)(id * 10), "Open-world bosses test", 0, 0, 1_048_576, 1_048_576,
                (ushort)(id * 10 + 1), false, 0, false, 0, 0, 0, 0, 0));
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
            if (!_origin)
            {
                (_ox, _oy, _origin) = (x - 32_768, y - 32_768, true);
            }
            x -= _ox;
            y -= _oy;
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

        private static T.Advice Decide(GameLiving raider, GameNPC target, List<GameNPC> live, int classId, bool shoots = false, long now = 1_000) =>
            T.Decide(raider, target, live, 0, shoots, now, classId);

        private static readonly string[] Bosses =
        [
            "OFGreenKnight", "Colialt", "SpiritOfLordEmthoro", "GlacierGiant", "WrethOfWinterJotun", "WrethOfWinterArachite",
            "WrethOfWinterSpider", "WrethOfWinterCrab", "WrethOfWinterScorp", "RaumarikRevenant", "Mortufoghus", "Evern",
            "EpicJuggernaut", "Curengkur", "Marfoirwort", "Mollunero", "Myrddraxis", "Anurigunda",
        ];

        // ---- the plans and the raids -----------------------------------------------------------------------------------

        [Test]
        public void EveryOpenWorldBigBossHasARaidPlan()
        {
            foreach (string boss in Bosses)
                Assert.That(T.For(boss), Is.Not.Null, $"{boss}: a catalog row");
            Assert.That(T.For("Evern") is { FightAtHomeRadius: 1400, FocusRangedAdds: true } e && e.RangedAdds.SequenceEqual(new[] { "EvernFairy" }), Is.True,
                "his fairies first, inside his 1,700 tether");
            Assert.That(T.For("OFGreenKnight") is { SpreadRadius: 500, TanksTaunt: true, AreaOnClump: true } gk && gk.RangedAdds.SequenceEqual(new[] { "GKTrees" }), Is.True,
                "Might of the Forest's 500, the healer pick taunted back, the trees");
            Assert.That(T.For("Mortufoghus").SpreadRadius, Is.EqualTo(1000), "Dark Packt, 1,000 around his target");
            Assert.That(T.For("Marfoirwort").SpreadRadius, Is.EqualTo(350), "his Life-leech, 350 around his target");
            Assert.That(T.For("Anurigunda").SpreadRadius, Is.EqualTo(350), "Touch of Flames, 350 around his target");
            Assert.That(T.For("Curengkur") is { HasHazards: true, HazardRadius: 800 } c && c.Hazards.SequenceEqual(new[] { "CurengkurNest" }), Is.True);
            Assert.That(T.For("Mollunero").RangedAdds, Is.EqualTo(new[] { "MolluneroAdd" }));
            Assert.That(T.For("Colialt").RangedAdds, Is.EqualTo(new[] { "ColialtAdds" }));
            Assert.That(T.For("EpicJuggernaut").FightAtHomeRadius, Is.EqualTo(3000), "inside his 3,600 tether");
            Assert.That(T.For("Morgana"), Is.Null, "a quest NPC in the period: no plan was invented");
            string[] coded = ["GlacierGiant", "Curengkur", "SpiritOfLordEmthoro"];
            T.DungeonRule[] rules = T.AllDungeonRules.Where(r => r.Targets.Any(coded.Contains)).ToArray();
            Assert.That(rules.Select(r => r.Encounter), Is.EquivalentTo(coded));
            Assert.That(rules.All(r => !string.IsNullOrEmpty(r.Summary)), Is.True);
        }

        [Test]
        public void MyrddraxissHeadsAreRequiredInThePeriodsOrderAndHisBreathsStay()
        {
            T.Tactic m = T.For("Myrddraxis");
            Assert.That(m.RequiredAdds, Is.EqualTo(new[] { "MyrddraxisSecondHead", "MyrddraxisFourthHead", "MyrddraxisThirdHead", "MyrddraxisFifthHead" }),
                "head 2 (lifetap), then 4, then 3 (Thredh, 2003-08-17); head 5 last");
            Assert.That(m.AddsInOrder, Is.True);
            Assert.That(m.BlastThresholds, Is.EqualTo(new[] { 80, 75, 60, 50, 40, 25, 20 }));
            Assert.That(m.BlastRadius, Is.EqualTo(2150));
        }

        [Test]
        public void EveryOpenWorldRaidIsADragonStyleRaidWithAHubInTheBosssRegion()
        {
            Assert.That(OW.Raids.Select(r => r.Id), Is.Unique);
            Assert.That(OW.Raids.Length, Is.EqualTo(16));
            Assert.Multiple(() =>
            {
                foreach (OW.OpenWorldRaid raid in OW.Raids)
                {
                    var d = AutonomousRealmRaid.Definitions.Single(x => x.Id == raid.Id);
                    Assert.That(d.IsOpenWorld && !d.IsDungeon && !d.IsNeutral, Is.True, raid.Id);
                    Assert.That((d.Realm, d.Region, d.BossType, d.SuggestedBots), Is.EqualTo((raid.Realm, raid.Region, raid.BossType, raid.SuggestedBots)));
                    Assert.That(d.FinalTypes, Is.EqualTo(new[] { raid.BossType }), raid.Id);
                    Assert.That(raid.SuggestedBots, Is.InRange(RealmRaidRecruitmentPolicy.MinimumScaledBots, 64), raid.Id + ": the period size, never under 40");
                    Assert.That(RealmRaidMuster.Hubs.Any(h => h.Event == raid.Id && h.Region == raid.Region), Is.True, raid.Id + ": a hub in its region");
                    Assert.That(T.For(raid.BossType), Is.Not.Null, raid.Id + ": its plan");
                    Assert.That(raid.Evidence, Is.Not.Empty, raid.Id);
                    Assert.That(DOL.GS.LoreChannels.LoreChronicle.RaidFinals[raid.Id], Is.EqualTo(new[] { raid.BossType }));
                }
            });
            Assert.That(AutonomousRealmRaid.Definitions.Where(d => d.IsOpenWorld).Select(d => d.Realm).Distinct(),
                Is.EquivalentTo(new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }));
            Assert.That(AutonomousRealmRaid.Definitions.Any(d => d.BossType is "Morgana" or "Myrddraxis" or "Anurigunda"), Is.False,
                "Morgana is no raid boss of the period; Fomor City has no path in on its navmesh");
            Assert.That(OW.Of("openworld-midgard-glaciergiant").SuggestedBots, Is.EqualTo(48), "5-6 groups (FreddysHouse, Dec 2003)");
            Assert.That(OW.Of("openworld-albion-coliath").SuggestedBots, Is.EqualTo(64), "Legion's size: about Legion difficulty");
        }

        // ---- the schedule ------------------------------------------------------------------------------------------------

        private static readonly string[] Dungeons = ["dragon-midgard", "epic-midgard", "dungeon-midgard-trollheim"];
        private static readonly string[] MidgardOpen = OW.Raids.Where(r => r.Realm == eRealm.Midgard).Select(r => r.Id).ToArray();

        [Test]
        public void TheOpenWorldRaidsTakeTheirShareOfTheScheduleAndTheWretchesCountAsOne()
        {
            string[] all = [.. Dungeons, .. MidgardOpen];
            Assert.That(OW.Choose(all, 25, 0.20, 0, 0), Does.StartWith(OW.IdPrefix), "a roll under the share: an open-world boss");
            Assert.That(OW.Choose(all, 25, 0.30, 0, 0), Is.EqualTo(Dungeons[0]), "over it: the other raids");
            Assert.That(OW.Choose(MidgardOpen, 25, 0.99, 0, 0), Does.StartWith(OW.IdPrefix), "nothing else up: an open-world boss");
            Assert.That(OW.Choose(Dungeons, 100, 0.0, 0, 0), Is.EqualTo(Dungeons[0]), "no open-world boss up: the others");
            Assert.That(OW.Choose(all, 0, 0.0, 0, 0), Is.EqualTo(Dungeons[0]), "share 0: never on the schedule");
            Assert.That(OW.Choose(MidgardOpen, 0, 0.0, 0, 0), Is.Null);
            Assert.That(OW.Choose([], 25, 0, 0, 0), Is.Null);

            int open = 0, wretches = 0, n = 1_000;
            var picks = new Dictionary<string, int>();
            for (int i = 0; i < n; i++)
            {
                double roll = (i + 0.5) / n;
                // Equidistributed over any run of i (golden ratio and square root of two steps): independent of the pool roll.
                string id = OW.Choose(all, 25, roll, i * 0.6180339887 % 1, i * 0.4142135624 % 1);
                picks[id] = picks.GetValueOrDefault(id) + 1;
                if (OW.Is(id)) open++;
                if (OW.ChoiceOf(id) == "wreths-of-winter") wretches++;
            }
            Assert.That(open, Is.EqualTo(250), "a quarter of the rolls");
            Assert.That(picks.Where(p => !OW.Is(p.Key)).Sum(p => p.Value), Is.EqualTo(750), "the dragon and the dungeons keep three quarters");
            // Midgard's open-world choices: the Glacier Giant, the Wretches (one), the Revenant, Mortufoghus.
            Assert.That(wretches, Is.EqualTo(open / 4).Within(15), "the five Wretches are one choice in four, not five in eight");
            Assert.That(MidgardOpen.Where(id => OW.ChoiceOf(id) == "wreths-of-winter").Count(), Is.EqualTo(5));
        }

        [Test]
        public void TheSweepRunsTheOpenWorldRaidsAloneOrWithEveryRaid()
        {
            string[] known = AutonomousRealmRaid.Definitions.Select(d => d.Id).ToArray();
            string[] only = EpicPlaytestSweep.Parse("openworld", known, out string[] unknown);
            Assert.That(unknown, Is.Empty);
            Assert.That(only, Is.EqualTo(OW.Raids.Select(r => r.Id).ToArray()), "the open-world raids in their own order");
            Assert.That(EpicPlaytestSweep.Parse("OpenWorld", known, out _), Is.EqualTo(only));
            string[] all = EpicPlaytestSweep.Parse("all", known, out _);
            Assert.That(all, Is.SupersetOf(only));
            Assert.That(all.TakeLast(3), Is.All.StartsWith("darkness-"), "Darkness Falls still last");
            Assert.That(EpicPlaytestSweep.Parse("openworld-hibernia-evern,nope", known, out unknown), Is.EqualTo(new[] { "openworld-hibernia-evern" }));
            Assert.That(unknown, Is.EqualTo(new[] { "nope" }));
        }

        [Test]
        public void AnOpenWorldRaidThatCannotStartWaitsBeforeTheScheduleTriesItAgain()
        {
            Assert.That(OW.FailedStartCooldownMilliseconds, Is.GreaterThanOrEqualTo(RealmRaidRecruitmentPolicy.ScheduleMinutes * 60_000L / 2),
                "longer than half a schedule slot: the next roll picks another raid");
            Assert.That(OW.SharePercentSetting, Is.EqualTo(25));
            Assert.That(OW.Kind, Is.EqualTo("Open-world boss"));
            var gg = Npc<GlacierGiant>(651972, 626960, 5468);
            (int spawnX, int spawnY) = (gg.X, gg.Y);
            gg.X += 1_000;
            Assert.That((OW.Home(gg).X, OW.Home(gg).Y), Is.EqualTo((spawnX, spawnY)), "the staging ring is around his spawn, not where he walked");
        }

        // ---- the Glacier Giant ----------------------------------------------------------------------------------------

        [Test]
        public void TheGlacierGiantsHealersStandOutOfHisThrowAndInReachOfHisTank()
        {
            var gg = Npc<GlacierGiant>(651972, 626960, 5468);
            var tank = Npc<Raider>(652200, 626960, 5468);
            gg.TargetObject = tank;
            var healer = Npc<Raider>(651300, 626960, 5468);
            var warrior = Npc<Raider>(651800, 626960, 5468);
            var live = new List<GameNPC> { gg, tank, healer, warrior };

            var a = T.DungeonAdvice(new(healer, gg, live, 0, false, 1_000, Healer));
            Assert.That(a?.Action, Is.EqualTo(T.BotAction.Reposition));
            Vector3 spot = a.Value.StandAt.Value;
            Assert.That(Flat(spot, At(gg)), Is.EqualTo(T.GlacierHealerStand).Within(1), "2,060: past the kick's 2,000");
            Assert.That(Flat(spot, At(tank)), Is.LessThanOrEqualTo(T.GlacierHealerReach), "and within reach of his tank");
            Assert.That(T.DungeonAdvice(new(warrior, gg, live, 0, false, 1_000, Warrior)), Is.Null, "melee fight him");
            Assert.That(T.DungeonAdvice(new(tank, gg, live, 0, false, 1_000, Druid)), Is.Null, "his own target holds him");
            gg.Calm = true;
            Assert.That(T.DungeonAdvice(new(healer, gg, live, 0, false, 1_000, Healer)), Is.Null, "not before the pull");
        }

        [Test]
        public void TheGlacierHealerSpotsFanOutAndAHealerThereStays()
        {
            Vector3 gg = new(0, 0, 0), tank = new(300, 0, 0);
            Vector3[] spots = Enumerable.Range(0, 3).Select(i => T.GlacierHealerSpot(new(100, 0, 0), gg, tank, i).Value).ToArray();
            Assert.That(spots.Select(s => (int)Math.Round(s.Y)).Distinct().Count(), Is.EqualTo(3), "three spots, not one");
            foreach (Vector3 s in spots)
            {
                Assert.That(Flat(s, gg), Is.EqualTo(T.GlacierHealerStand).Within(1));
                Assert.That(Flat(s, tank), Is.LessThanOrEqualTo(T.GlacierHealerReach));
                Assert.That(T.GlacierHealerSpot(s, gg, tank, 0), Is.Null, "there: it stays and heals");
            }
            Assert.That(T.GlacierHealerSpot(new(2_200, 0, 0), gg, tank, 0), Is.Null, "already out and in reach");
            Assert.That(T.GlacierHealerSpot(new(-2_400, 0, 0), gg, tank, 0), Is.Not.Null, "out of the kick but too far from the tank");
        }

        // ---- Curengkur --------------------------------------------------------------------------------------------------

        [Test]
        public void CurengkursTankLeadsHimOutOfHisNestAndNobodyStandsInIt()
        {
            UseRegion(181);
            var cur = Npc<Curengkur>(376086, 172803, 3452);
            var nest = Npc<CurengkurNest>(376086, 172803, 3452);
            var tank = Npc<Raider>(376300, 172803, 3452);
            var melee = Npc<Raider>(375900, 172803, 3452);
            cur.TargetObject = tank;
            var live = new List<GameNPC> { cur, nest, tank, melee };
            var lead = T.DungeonAdvice(new(tank, cur, live, 0, false, 1_000, Warrior));
            Assert.That(lead?.Action, Is.EqualTo(T.BotAction.Reposition), "his tank keeps him and walks");
            Assert.That(Flat(lead.Value.StandAt.Value, At(cur)), Is.EqualTo(T.CurengkurLeadOut).Within(1), "1,500 out of his spawn");
            Assert.That(T.DungeonAdvice(new(melee, cur, live, 0, false, 1_000, Warrior)), Is.Null, "the lead is his target's");
            var stepped = Decide(melee, cur, live, Warrior);
            Assert.That(stepped.Action, Is.EqualTo(T.BotAction.Reposition), "out of the nest's 800");
            Assert.That(Flat(stepped.StandAt.Value, At(nest)), Is.GreaterThan(T.CurengkurNestRadius));
        }

        [Test]
        public void CurengkursLeadEndsOnceHeIsOut()
        {
            Vector3 spawn = new(0, 0, 0);
            Assert.That(T.CurengkurLeadSpot(new(200, 0, 0), new(100, 0, 0), spawn), Is.EqualTo(new Vector3(T.CurengkurLeadOut, 0, 0)));
            Assert.That(T.CurengkurLeadSpot(new(1_480, 0, 0), new(900, 0, 0), spawn), Is.Null, "his tank is there: he follows");
            Assert.That(T.CurengkurLeadSpot(new(600, 0, 0), new(1_200, 0, 0), spawn), Is.Null, "he is out of his 800");
        }

        // ---- the Spirit of Lord Emthoro --------------------------------------------------------------------------------

        [Test]
        public void EmthoroIsFoughtFromInsideHisPit()
        {
            UseRegion(51);
            var em = Npc<SpiritOfLordEmthoro>(238081, 434761, 4521);
            var caster = Npc<Raider>(239281, 434761, 4521);
            var inside = Npc<Raider>(238381, 434761, 4521);
            var live = new List<GameNPC> { em, caster, inside };
            var a = T.DungeonAdvice(new(caster, em, live, 0, true, 1_000, Wizard));
            Assert.That(a?.Action, Is.EqualTo(T.BotAction.Reposition), "a caster 1,200 out does no damage");
            Assert.That(Flat(a.Value.StandAt.Value, At(em)), Is.LessThan(T.EmthoroReach), "his spawn: where he stands");
            Assert.That(T.DungeonAdvice(new(inside, em, live, 0, true, 1_000, Wizard)), Is.Null, "300 from his spawn: in");
        }

        // ---- Evern, the Green Knight, Mollunero and Coliath: adds -------------------------------------------------------

        [Test]
        public void EvernsFairiesDieBeforeTheyReachAStone()
        {
            UseRegion(200);
            var evern = Npc<Evern>(429883, 380409, 2328);
            var tank = Npc<Raider>(430000, 380409, 2328);
            evern.TargetObject = tank;
            var fairy = Npc<EvernFairy>(429764, 380398, 2726);
            var second = Npc<EvernFairy>(429800, 380450, 2726);
            var wizard = Npc<Raider>(429000, 380409, 2328);
            var warrior = Npc<Raider>(429950, 380300, 2328);
            var live = new List<GameNPC> { evern, tank, fairy, second, wizard, warrior };
            var a = Decide(wizard, evern, live, Wizard, shoots: true);
            Assert.That(a.Action, Is.EqualTo(T.BotAction.Retarget));
            Assert.That(a.Target, Is.SameAs(fairy), "the whole raid's casters on one fairy (the lowest id on a tie)");
            Assert.That(Decide(warrior, evern, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "the melee stay on him");
        }

        [Test]
        public void TheGreenKnightsTreesAreShotAndHisAreaHitIsSteppedOutOf()
        {
            UseRegion(1);
            var gk = Npc<OFGreenKnight>(592359, 418245, 4988);
            var tank = Npc<Raider>(592500, 418245, 4988);
            gk.TargetObject = tank;
            var tree = Npc<GKTrees>(591000, 418245, 4988);
            var healer = Npc<Raider>(592700, 418245, 4988);
            var wizard = Npc<Raider>(591300, 418245, 4988);
            var live = new List<GameNPC> { gk, tank, tree, healer, wizard };
            var step = Decide(healer, gk, live, Cleric);
            Assert.That(step.Action, Is.EqualTo(T.BotAction.Reposition), "Might of the Forest: 500 around his target");
            Assert.That(Flat(step.StandAt.Value, At(tank)), Is.EqualTo(500 + T.SpreadStepOut).Within(1));
            var shoot = Decide(wizard, gk, live, Wizard, shoots: true);
            Assert.That((shoot.Action, shoot.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)tree)), "the casters kill his trees");
        }

        [Test]
        public void MolluneroAndColiathsAddsAreTheCastersTargets()
        {
            UseRegion(181);
            var mol = Npc<Mollunero>(381822, 268541, 3403);
            var octo = Npc<MolluneroAdd>(381900, 268600, 3403);
            var wizard = Npc<Raider>(381000, 268541, 3403);
            var live = new List<GameNPC> { mol, octo, wizard };
            Assert.That(Decide(wizard, mol, live, Wizard, shoots: true).Target, Is.SameAs(octo));
            UseRegion(51);
            var col = Npc<Colialt>(327855, 443917, 7514);
            var zombie = Npc<ColialtAdds>(328000, 444000, 7514);
            var mage = Npc<Raider>(327000, 443917, 7514);
            var melee = Npc<Raider>(327950, 443917, 7514);
            live = [col, zombie, mage, melee];
            Assert.That(Decide(mage, col, live, Wizard, shoots: true).Target, Is.SameAs(zombie));
            Assert.That(Decide(melee, col, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "the melee hold him");
        }

        // ---- the spreads ------------------------------------------------------------------------------------------------

        [Test]
        public void MortufoghussAndMarfoirwortsAreaHitsAreSteppedOutOf()
        {
            UseRegion(151);
            var mort = Npc<Mortufoghus>(451102, 350788, 7022);
            var tank = Npc<Raider>(451300, 350788, 7022);
            mort.TargetObject = tank;
            var healer = Npc<Raider>(451900, 350788, 7022);
            var live = new List<GameNPC> { mort, tank, healer };
            var a = Decide(healer, mort, live, Healer);
            Assert.That(a.Action, Is.EqualTo(T.BotAction.Reposition), "Dark Packt: 1,000 around his target");
            Assert.That(Flat(a.StandAt.Value, At(tank)), Is.EqualTo(1000 + T.SpreadStepOut).Within(1));

            UseRegion(181);
            var marf = Npc<Marfoirwort>(293831, 283911, 2695);
            var marfTank = Npc<Raider>(294000, 283911, 2695);
            marf.TargetObject = marfTank;
            var druid = Npc<Raider>(294200, 283911, 2695);
            var b = Decide(druid, marf, new List<GameNPC> { marf, marfTank, druid }, Druid);
            Assert.That(b.Action, Is.EqualTo(T.BotAction.Reposition), "every leech heals him: out of 350 around his target");
            Assert.That(Flat(b.StandAt.Value, At(marfTank)), Is.EqualTo(350 + T.SpreadStepOut).Within(1));
        }

        // ---- Myrddraxis's heads -----------------------------------------------------------------------------------------

        [Test]
        public void MyrddraxisLosesHisHeadsTwoFourThreeFiveThenTheBody()
        {
            UseRegion(180);
            var myr = Npc<Myrddraxis>(32302, 32221, 15635);
            var tank = Npc<Raider>(32500, 32221, 15635);
            myr.TargetObject = tank;
            var second = Npc<MyrddraxisSecondHead>(32384, 31942, 15731);
            var third = Npc<MyrddraxisThirdHead>(32187, 32205, 15731);
            var fourth = Npc<MyrddraxisFourthHead>(32371, 32351, 15731);
            var fifth = Npc<MyrddraxisFifthHead>(32576, 32133, 15731);
            var raider = Npc<Raider>(32302, 31800, 15635);
            var live = new List<GameNPC> { myr, tank, second, third, fourth, fifth, raider };

            GameNPC Next() => Decide(raider, myr, live, Warrior) is { Action: T.BotAction.Retarget } a ? a.Target : null;
            Assert.That(Next(), Is.SameAs(second), "head 2, the lifetap head, first");
            var onThird = Decide(raider, third, live, Warrior);
            Assert.That((onThird.Action, onThird.Target), Is.EqualTo((T.BotAction.Retarget, (GameNPC)second)), "not head 3 before head 2");
            second.Dead = true;
            Assert.That(Next(), Is.SameAs(fourth), "then head 4");
            fourth.Dead = true;
            Assert.That(Next(), Is.SameAs(third), "then head 3");
            third.Dead = true;
            Assert.That(Next(), Is.SameAs(fifth), "then head 5 (in the script it doesn't die by itself)");
            fifth.Dead = true;
            Assert.That(Decide(raider, myr, live, Warrior).Action, Is.EqualTo(T.BotAction.None), "then the body");
        }
    }
}
