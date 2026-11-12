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
    /// Raid tactics round 3 (1.2): Grand Summoner Govannon's demon-form spread and walking adds, Orshom Brong's fire and
    /// Orylle after him, Galladoria's Earthmenders by class (with the original class rule switched on), Spindler
    /// Broodmother's brood, and the quieter early-pull log. The tactic rows are the catalog's (renamed onto test doubles
    /// with <c>with</c>, so the numbers stay the catalog's); the harness tests stand a boss, its adds and a raid in a test
    /// region and play the decisions the route and the bots make. No world tick, combat, spell or pathing runs.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicEncounterTactics3
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 300;
        private EpicTestServerScope _server;
        private bool _classPuzzle;

        public class Boss3 : GameEpicBoss
        {
            public bool Dead, Fighting;
            public ushort Look = 61;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
            public override ushort Model { get => Look; set => Look = value; }
        }
        public sealed class Govannon3 : Boss3 { }
        public sealed class Orshom3 : Boss3 { }
        public sealed class Orylle3 : Boss3 { }
        public sealed class Primal3 : Boss3 { }
        public sealed class Brood3 : Boss3 { }
        public class Add3 : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class Sacrifice3 : Add3 { }
        public sealed class Demon3 : Add3 { }
        public sealed class Fire3 : GameEpicNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        public sealed class GuardianMender3 : Add3 { }
        public sealed class MagicalMender3 : Add3 { }
        public sealed class NaturalMender3 : Add3 { }
        public sealed class ShadowyMender3 : Add3 { }
        public sealed class Newborn3 : Add3 { }
        public sealed class Underdeveloped3 : Add3 { }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }

        private static readonly Dictionary<string, string> MenderNames = new()
        {
            ["GuardianEarthmender"] = nameof(GuardianMender3), ["MagicalEarthmender"] = nameof(MagicalMender3),
            ["NaturalEarthmender"] = nameof(NaturalMender3), ["ShadowyEarthmender"] = nameof(ShadowyMender3),
        };

        private static EpicEncounterTactics.Tactic Row(string boss) => EpicEncounterTactics.Catalog.Single(t => t.Boss == boss);

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            EncounterMeleeStations.ResetForTests();
            _region = new Region(new RegionData { Id = 63, Name = "Tactic test 3", Description = "Tactic test 3", Mobs = [] });
            _classPuzzle = EpicEncounterMechanics.AllowEarthmenderClassPuzzle;
            // The catalog's rows, put on the test doubles.
            EpicEncounterTactics.Register(Row("GrandSummonerGovannon") with
                { Boss = nameof(Govannon3), RangedAdds = [nameof(Sacrifice3), nameof(Demon3)] });
            EpicEncounterTactics.Register(Row("Orshom") with { Boss = nameof(Orshom3), Hazards = [nameof(Fire3)] });
            EpicEncounterTactics.Register(Row("Orylle") with { Boss = nameof(Orylle3), Hazards = [nameof(Fire3)], After = nameof(Orshom3) });
            var primal = Row("EarthPrimal");
            EpicEncounterTactics.Register(primal with
            {
                Boss = nameof(Primal3), RequiredAdds = primal.RequiredAdds.Select(a => MenderNames[a]).ToArray(),
                ClassRules = primal.ClassRules.Select(r => r with { Add = MenderNames[r.Add] }).ToArray()
            });
            EpicEncounterTactics.Register(Row("SpindlerBroodmother") with
                { Boss = nameof(Brood3), RangedAdds = [nameof(Newborn3)], DeathAdds = [nameof(Underdeveloped3)] });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string boss in new[] { nameof(Govannon3), nameof(Orshom3), nameof(Orylle3), nameof(Primal3), nameof(Brood3) })
                EpicEncounterTactics.Unregister(boss);
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = _classPuzzle;
            EncounterMeleeStations.ResetForTests();
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

        private static float Flat(GameObject a, GameObject b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));
        private static float Flat(Vector3 a, GameObject b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        // ---- the catalog --------------------------------------------------------------------------------------------

        [Test]
        public void TheCatalogCarriesTheRoundThreeTactics()
        {
            var govannon = Row("GrandSummonerGovannon");
            Assert.That((govannon.SpreadRadius, govannon.SpreadWhileModel), Is.EqualTo((300, 636)), "Shroud of Agony, demon model");
            Assert.That(govannon.RangedAdds, Is.EqualTo(new[] { "SummonedSacrifice", "SummonedDemon" }));
            // Epic wave 2: the Shade of Aelfgar (only if both walkers reach his altar) is killed first; he is never immune.
            Assert.That(govannon.RequiredAdds, Is.EqualTo(new[] { "ShadeOfAelfgar" }));
            var orshom = Row("Orshom");
            Assert.That((orshom.Hazards.Single(), orshom.HazardRadius), Is.EqualTo(("OrshomFire", 300)), "Fire Burn's radius");
            Assert.That(Row("Orylle").After, Is.EqualTo("Orshom"));
            var spindler = Row("SpindlerBroodmother");
            Assert.That((spindler.RangedAdds.Single(), spindler.DeathAdds.Single()), Is.EqualTo(("SBAdds", "SBDeadAdds")));
            Assert.That(Row("EarthPrimal").ClassRuleSwitch, Is.EqualTo(nameof(EpicEncounterMechanics.AllowEarthmenderClassPuzzle)));
            // Every class name a round-3 row uses exists in the server.
            Assembly gs = typeof(GameNPC).Assembly;
            foreach (var t in EpicEncounterTactics.Catalog)
                foreach (string type in t.RangedAdds.Concat(t.Hazards).Concat(t.DeathAdds).Concat(t.ClassRules.Select(r => r.Add))
                             .Concat(t.After == null ? [] : [t.After]))
                    Assert.That(gs.GetTypes().Any(x => x.Name == type && typeof(GameNPC).IsAssignableFrom(x)), Is.True, type);
            Assert.That(EpicEncounterTactics.SwitchOn(nameof(EpicEncounterMechanics.AllowEarthmenderClassPuzzle)), Is.True);
            Assert.That(EpicEncounterTactics.SwitchOn(nameof(EpicEncounterMechanics.AllowGovannonFullHealth)), Is.False,
                "off by default until a played check (epic_govannon_full_health)");
            Assert.That(EpicEncounterTactics.SwitchOn("NoSuchSwitch"), Is.False);
        }

        [Test]
        public void TheEarthmenderClassTableIsTheGamesOwnRule()
        {
            // The raid's table (catalog) and the Earthmenders' TakeDamage rule (EpicEncounterRestores) agree for every class.
            var rules = Row("EarthPrimal").ClassRules.ToDictionary(r => r.Add, r => r.Classes);
            var kinds = new Dictionary<string, EpicEncounterRestores.Earthmender>
            {
                ["GuardianEarthmender"] = EpicEncounterRestores.Earthmender.Guardian, ["MagicalEarthmender"] = EpicEncounterRestores.Earthmender.Magical,
                ["NaturalEarthmender"] = EpicEncounterRestores.Earthmender.Natural, ["ShadowyEarthmender"] = EpicEncounterRestores.Earthmender.Shadowy,
            };
            Assert.That(rules.Keys, Is.EquivalentTo(kinds.Keys));
            foreach (eCharacterClass c in Enum.GetValues<eCharacterClass>())
                foreach (var (add, kind) in kinds)
                    Assert.That(Array.IndexOf(rules[add], c) >= 0, Is.EqualTo(EpicEncounterRestores.EarthmenderAccepts(kind, (int)c)), $"{add} {c}");
        }

        // ---- Grand Summoner Govannon ----------------------------------------------------------------------------------

        [Test]
        public void InDemonFormTheRaidSpreadsOffHisTargetAndThePartyHeFightsHoldsHim()
        {
            // 2026-11-06 11:15: the raid stood in one pile on his target (27 deaths inside one 100-unit square) and his
            // Shroud of Agony (radius 300 around his target) killed 61 with nobody hitting them.
            var gov = Npc<Govannon3>(32124, 40754, 15466);
            var tank = Npc<Raider>(32300, 40754, 15466); // 176 from him
            gov.TargetObject = tank;
            gov.Fighting = true;
            var live = new List<GameNPC> { gov };
            var casters = Enumerable.Range(0, 10).Select(i => Npc<Raider>(32280 + i * 8, 40700 + i * 10, 15466)).ToList();
            var melee = Enumerable.Range(0, 10).Select(i => Npc<Raider>(32250 + i * 6, 40800 - i * 9, 15466)).ToList();
            var healers = Enumerable.Range(0, 4).Select(i => Npc<Raider>(32330 + i * 10, 40790, 15466)).ToList();
            int healer = (int)eCharacterClass.Healer, warrior = (int)eCharacterClass.Warrior, runemaster = (int)eCharacterClass.Runemaster;

            // His first form: no Shroud, nobody moves.
            Assert.That(casters.Concat(melee).All(r => EpicEncounterTactics.Decide(r, gov, live, 0, true, 1_000, runemaster).Action ==
                                                       EpicEncounterTactics.BotAction.None), Is.True);

            gov.Look = 636; // the demon
            var moved = new List<(Raider R, EpicEncounterTactics.Advice A)>();
            moved.AddRange(casters.Select(r => (r, EpicEncounterTactics.Decide(r, gov, live, 0, true, 2_000, runemaster))));
            Assert.That(moved.All(d => d.A.Action == EpicEncounterTactics.BotAction.Reposition && d.A.StandAt != null), Is.True,
                "casters step clear and keep him as their target");
            // Owner 2026-11-08: the melee fight him from three ring spots, each party at its own (2026-11-07: one spot behind him).
            EncounterMeleeStations.ResetRingForTests();
            var behind = melee.Select(r => (r, A: EpicEncounterTactics.Decide(r, gov, live, 0, false, 2_000, warrior)))
                .Where(d => d.A.Action != EpicEncounterTactics.BotAction.None).ToList();
            Assert.That(behind.All(d => d.A.Action == EpicEncounterTactics.BotAction.Reposition && d.A.Target == gov), Is.True,
                "melee step to their ring spot and keep him as their target");
            Assert.That(behind.All(d => Math.Abs(Flat(d.A.StandAt.Value, gov) - EpicEncounterTactics.RingRadius) < 1), Is.True,
                "the ring: within melee reach of him");
            Vector3 spot0 = EncounterMeleeStations.RingSpot(new(gov.X, gov.Y, gov.Z), 0, 0, 3, EpicEncounterTactics.RingRadius, gov.Z);
            Assert.That(behind.All(d => Flat(d.A.StandAt.Value, tank) > 300 || Vector3.Distance(d.A.StandAt.Value, spot0) < 1), Is.True,
                "only the spot towards his target is inside 300 around it");
            // Healers step clear like the casters (a hold for healers was tried and dropped: duo7).
            Assert.That(EpicEncounterTactics.Decide(tank, gov, live, 0, false, 2_000, warrior).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "the one he fights holds him");

            var everyone = casters.Concat(melee).Concat(healers).ToList();
            Assert.That(everyone.Count(r => Flat(r, tank) <= 300), Is.EqualTo(24), "before: the whole pile is in the Shroud");
            foreach (var (r, a) in moved.Concat(behind)) Move(r, a.StandAt.Value.X, a.StandAt.Value.Y, a.StandAt.Value.Z);
            var healerMoves = healers.Select(r => (r, A: EpicEncounterTactics.Decide(r, gov, live, 0, false, 2_500, healer))).ToList();
            Assert.That(healerMoves.All(d => d.A.Action == EpicEncounterTactics.BotAction.Reposition), Is.True,
                "the healers step clear");
            foreach (var (r, a) in healerMoves) Move(r, a.StandAt.Value.X, a.StandAt.Value.Y, a.StandAt.Value.Z);
            moved.AddRange(healerMoves);
            Assert.That(casters.Concat(healers).Count(r => Flat(r, tank) <= 300), Is.EqualTo(0), "after: no caster or healer is in it");
            Assert.That(melee.Count(r => Flat(r, tank) <= 300), Is.LessThan(melee.Count),
                "after: only the melee of the spot towards his target are in it");
            Assert.That(melee.All(r => EpicEncounterTactics.Decide(r, gov, live, 0, false, 3_000, warrior).Action ==
                                       EpicEncounterTactics.BotAction.None), Is.True, "at their spots they fight on");
            Assert.That(casters.Concat(healers).All(r => Flat(r, tank) >= 540 && Flat(r, gov) <= 1500), Is.True, "at range, in spell range");
            // Out of it, they fight on from there.
            Assert.That(casters.All(r => EpicEncounterTactics.Decide(r, gov, live, 0, true, 3_000, runemaster).Action ==
                                         EpicEncounterTactics.BotAction.None), Is.True);
            Assert.That(healers.All(r => EpicEncounterTactics.Decide(r, gov, live, 0, false, 3_000, healer).Action ==
                                         EpicEncounterTactics.BotAction.None), Is.True);
        }

        [Test]
        public void TheSpreadSpotIsAwayFromHisTargetAndOnlyForThoseWhoFightAtRange()
        {
            Vector3 boss = new(0, 0, 0), tank = new(100, 0, 0);
            Vector3? spot = EpicEncounterTactics.SpreadSpot(new(150, 100, 0), boss, tank, 300, atRange: true);
            Assert.That(Vector2.Distance(new(spot.Value.X, spot.Value.Y), new(tank.X, tank.Y)), Is.EqualTo(550).Within(1));
            Assert.That(spot.Value.Y, Is.GreaterThan(0), "on the raider's own side");
            Assert.That(EpicEncounterTactics.SpreadSpot(new(100, 0, 0), boss, tank, 300, true), Is.Not.Null, "standing on the target: away from the boss");
            Assert.That(EpicEncounterTactics.SpreadSpot(new(150, 100, 0), boss, tank, 300, atRange: false), Is.Null, "melee");
            Assert.That(EpicEncounterTactics.SpreadSpot(new(900, 0, 0), boss, tank, 300, atRange: true), Is.Null, "already clear");
        }

        [Test]
        public void CastersKillTheWalkersBeforeTheyReachHisAltarAndMeleeStayOnHim()
        {
            var gov = Npc<Govannon3>(32050, 40863, 15468);
            var sacrifice = Npc<Sacrifice3>(31018, 40889, 15491);
            var demon = Npc<Demon3>(33215, 40883, 15491);
            var live = new List<GameNPC> { gov, sacrifice, demon };
            var caster = Npc<Raider>(32300, 40700, 15466);
            var warrior = Npc<Raider>(32150, 40800, 15466);
            var c0 = EpicEncounterTactics.Decide(caster, gov, live, 0, true, 1_000, (int)eCharacterClass.Wizard);
            var c1 = EpicEncounterTactics.Decide(caster, gov, live, 1, true, 1_000, (int)eCharacterClass.Wizard);
            Assert.That((c0.Action, c0.Target, c1.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)sacrifice, (GameNPC)demon)));
            Assert.That(EpicEncounterTactics.Decide(warrior, gov, live, 0, false, 1_000, (int)eCharacterClass.Armsman).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None));
            Assert.That(EpicEncounterTactics.RouteTarget(gov, live, new(32300, 40700, 15466), out _), Is.SameAs(gov), "the route stays on him");
            Assert.That(EpicEncounterTactics.IsTacticPart(sacrifice), Is.True, "a raider aiming at one asks the tactic");
            sacrifice.Dead = true;
            demon.Dead = true;
            Assert.That(EpicEncounterTactics.Decide(caster, gov, live, 0, true, 1_000, (int)eCharacterClass.Wizard).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "both dead: back on him");
        }

        // ---- Orshom Brong and Orylle ----------------------------------------------------------------------------------

        [Test]
        public void NobodyFightsOrshomsFireOrStandsInIt()
        {
            var orshom = Npc<Orshom3>(31697, 70458, 15610);
            var orylle = Npc<Orylle3>(31413, 70773, 15765);
            var fire = Npc<Fire3>(31406, 69599, 15605);
            var live = new List<GameNPC> { orshom, orylle, fire };
            var burning = Npc<Raider>(31500, 69650, 15605); // 107 from the fire
            var clear = Npc<Raider>(31650, 70300, 15610);

            Assert.That(EpicEncounterTactics.IsHazard(fire), Is.True);
            var onFire = EpicEncounterTactics.Decide(burning, fire, live, 0, false, 1_000, (int)eCharacterClass.Armsman);
            Assert.That(onFire.Action, Is.EqualTo(EpicEncounterTactics.BotAction.StandOff), "in the fire: out first");
            Assert.That(Vector2.Distance(new(onFire.StandAt.Value.X, onFire.StandAt.Value.Y), new(fire.X, fire.Y)), Is.EqualTo(600).Within(1));
            var fromAfar = EpicEncounterTactics.Decide(clear, fire, live, 0, true, 1_000, (int)eCharacterClass.Wizard);
            Assert.That((fromAfar.Action, fromAfar.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)orshom)),
                "it burned you: fight Orshom, not the fire");
            var onOrshom = EpicEncounterTactics.Decide(burning, orshom, live, 0, false, 1_000, (int)eCharacterClass.Armsman);
            Assert.That(onOrshom.Action, Is.EqualTo(EpicEncounterTactics.BotAction.Reposition), "fighting Orshom from inside the fire: step out");
            Move(burning, onOrshom.StandAt.Value.X, onOrshom.StandAt.Value.Y, onOrshom.StandAt.Value.Z);
            Assert.That(EpicEncounterTactics.Decide(burning, orshom, live, 0, false, 1_000, (int)eCharacterClass.Armsman).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "out of it: fight on");
            Assert.That(EpicEncounterTactics.RouteTarget(fire, live, default, out string why), Is.SameAs(orshom));
            Assert.That(why, Does.StartWith("hazard of"));

            // Orshom dead: the fire still burns; Orylle is the one to fight.
            orshom.Dead = true;
            Assert.That(EpicEncounterTactics.Decide(clear, fire, live, 0, true, 1_000, 0).Target, Is.SameAs(orylle));
            orylle.Dead = true;
            var alone = EpicEncounterTactics.Decide(clear, fire, live, 0, true, 1_000, 0);
            Assert.That((alone.Action, alone.StandAt), Is.EqualTo((EpicEncounterTactics.BotAction.StandOff, (Vector3?)null)), "never hit it");
        }

        [Test]
        public void OryllesPartyHoldsHerWhileEveryoneElseKillsOrshomFirst()
        {
            var orshom = Npc<Orshom3>(31697, 70458, 15610);
            var orylle = Npc<Orylle3>(31413, 70773, 15765);
            var live = new List<GameNPC> { orshom, orylle };
            var held = Npc<Raider>(31450, 70800, 15765);
            var other = Npc<Raider>(31500, 70700, 15700);
            orylle.TargetObject = held;
            Assert.That(EpicEncounterTactics.Decide(held, orylle, live, 0, false, 1_000, 0).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            var a = EpicEncounterTactics.Decide(other, orylle, live, 0, true, 1_000, 0);
            Assert.That((a.Action, a.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)orshom)));
            Assert.That(EpicEncounterTactics.RouteTarget(orylle, live, default, out string why), Is.SameAs(orshom), "the route musters for him");
            Assert.That(why, Does.StartWith("after"));
            orshom.Dead = true;
            Assert.That(EpicEncounterTactics.Decide(other, orylle, live, 0, true, 1_000, 0).Action, Is.EqualTo(EpicEncounterTactics.BotAction.None));
            Assert.That(EpicEncounterTactics.RouteTarget(orylle, live, default, out _), Is.SameAs(orylle));
        }

        // ---- Galladoria: the Earthmenders by class -------------------------------------------------------------------

        private (Primal3 Primal, Add3[] Menders, List<GameNPC> Live) EarthRoom()
        {
            var primal = Npc<Primal3>(39727, 62620, 11684);
            Add3[] menders =
            [
                Npc<GuardianMender3>(40020, 62401, 11676), Npc<MagicalMender3>(39459, 62412, 11688),
                Npc<NaturalMender3>(39552, 62929, 11690), Npc<ShadowyMender3>(39965, 62921, 11662),
            ];
            primal.Fighting = true;
            return (primal, menders, new List<GameNPC> { primal }.Concat(menders).ToList());
        }

        private static EpicEncounterRestores.Earthmender Kind(GameNPC mender) => mender switch
        {
            GuardianMender3 => EpicEncounterRestores.Earthmender.Guardian,
            MagicalMender3 => EpicEncounterRestores.Earthmender.Magical,
            NaturalMender3 => EpicEncounterRestores.Earthmender.Natural,
            _ => EpicEncounterRestores.Earthmender.Shadowy,
        };

        [Test]
        public void EachRaiderTakesAnEarthmenderItsClassHurts()
        {
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = true;
            var (primal, menders, live) = EarthRoom();
            var raider = Npc<Raider>(39800, 62500, 11684);
            var hero = EpicEncounterTactics.Decide(raider, menders[1], live, 3, false, 1_000, (int)eCharacterClass.Hero);
            Assert.That((hero.Action, hero.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)menders[0])),
                "a hero on the Magical Earthmender (no damage) goes to the Guardian");
            Assert.That(EpicEncounterTactics.Decide(raider, menders[1], live, 3, true, 1_000, (int)eCharacterClass.Eldritch).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "an eldritch on the Magical one fights on");
            Assert.That(EpicEncounterTactics.Decide(raider, primal, live, 0, false, 1_000, (int)eCharacterClass.Ranger).Target,
                Is.SameAs(menders[3]), "a ranger aiming at the primal takes the Shadowy one");
            var animists = Enumerable.Range(0, 4).Select(i => EpicEncounterTactics.Decide(raider, primal, live, i, true, 1_000,
                (int)eCharacterClass.Animist).Target).ToArray();
            Assert.That(animists, Is.EquivalentTo(menders), "animists hurt all four: spread by party");
            var bainshee = EpicEncounterTactics.Decide(raider, menders[2], live, 0, true, 1_000, (int)eCharacterClass.Bainshee);
            Assert.That((bainshee.Action, bainshee.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)primal)),
                "a class none of them takes damage from keeps the primal busy");
            Assert.That(EpicEncounterTactics.Decide(raider, primal, live, 0, true, 1_000, (int)eCharacterClass.Vampiir).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None));

            // Switch off: everyone hurts every Earthmender, parties spread over them as before.
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = false;
            Assert.That(EpicEncounterTactics.Decide(raider, menders[1], live, 3, false, 1_000, (int)eCharacterClass.Hero).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None));
            Assert.That(EpicEncounterTactics.Decide(raider, primal, live, 2, false, 1_000, (int)eCharacterClass.Bainshee).Target,
                Is.SameAs(menders[2]));
        }

        [Test]
        public void AHibernianRaidCoversAllFourEarthmendersUnderTheClassRule()
        {
            // The raid harness: a Galladoria raid of 48 in six parties of every Hibernian class, each raider aiming where the
            // route sent it (the primal, then the Earthmender nearest the front). With the original rule on, no raider is left
            // on an Earthmender its class can't hurt, every Earthmender has raiders who can, and as each dies its raiders move on.
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = true;
            var (primal, menders, live) = EarthRoom();
            eCharacterClass[] hib =
            [
                eCharacterClass.Hero, eCharacterClass.Blademaster, eCharacterClass.Champion, eCharacterClass.Druid,
                eCharacterClass.Eldritch, eCharacterClass.Enchanter, eCharacterClass.Mentalist, eCharacterClass.Bard,
                eCharacterClass.Warden, eCharacterClass.Nightshade, eCharacterClass.Ranger, eCharacterClass.Animist,
                eCharacterClass.Valewalker, eCharacterClass.Bainshee, eCharacterClass.Vampiir, eCharacterClass.Druid,
            ];
            var raid = Enumerable.Range(0, 48).Select(i => (R: Npc<Raider>(39800 + i % 7 * 20, 62500 + i / 7 * 20, 11684),
                Class: hib[i % hib.Length], Party: i / 8)).ToList();
            static bool Ranged(eCharacterClass c) => c is eCharacterClass.Eldritch or eCharacterClass.Enchanter or eCharacterClass.Mentalist or
                eCharacterClass.Animist or eCharacterClass.Bainshee or eCharacterClass.Ranger;

            for (int round = 0; round < 4; round++)
            {
                GameNPC routeTarget = EpicEncounterTactics.RouteTarget(primal, live, new(39800, 62500, 11684), out _);
                var on = new Dictionary<Raider, GameNPC>();
                foreach (var (r, c, party) in raid)
                {
                    GameNPC aim = routeTarget;
                    var advice = EpicEncounterTactics.Decide(r, aim, live, party, Ranged(c), 1_000, (int)c);
                    if (advice.Action == EpicEncounterTactics.BotAction.Retarget)
                    {
                        aim = advice.Target;
                        // The retarget is final: asked again on the new target, the tactic keeps it there.
                        Assert.That(EpicEncounterTactics.Decide(r, aim, live, party, Ranged(c), 1_000, (int)c).Action,
                            Is.EqualTo(EpicEncounterTactics.BotAction.None), $"{c} settles on {aim.GetType().Name}");
                    }
                    on[r] = aim;
                }
                Add3[] alive = menders.Where(m => !m.Dead).ToArray();
                foreach (var (r, c, _) in raid)
                    if (on[r] is Add3 m)
                        Assert.That(EpicEncounterRestores.EarthmenderAccepts(Kind(m), (int)c), Is.True, $"{c} sent to {m.GetType().Name}");
                foreach (Add3 m in alive)
                    Assert.That(raid.Count(x => on[x.R] == m), Is.GreaterThan(0), $"round {round}: nobody on {m.GetType().Name}");
                Assert.That(raid.Where(x => on[x.R] == primal).All(x => alive.All(m => !EpicEncounterRestores.EarthmenderAccepts(Kind(m), (int)x.Class))),
                    Is.True, "only a class none of the living ones takes damage from stays on the primal");
                alive[0].Dead = true; // the next Earthmender dies
            }
            Assert.That(EpicEncounterTactics.RouteTarget(primal, live, default, out _), Is.SameAs(primal), "all four dead: the primal");
        }

        // ---- Spindler Broodmother -------------------------------------------------------------------------------------

        [Test]
        public void OnlyAddPartiesAndAreaCastersLeaveHerAndTheBroodIsClearedAtHerBody()
        {
            var brood = Npc<Brood3>(21283, 51707, 10876);
            var newborn = Npc<Newborn3>(21300, 51750, 10876);
            var live = new List<GameNPC> { brood, newborn };
            var caster = Npc<Raider>(21600, 51700, 10876);
            var tank = Npc<Raider>(21400, 51700, 10876);
            // Owner 2026-11-08: newborn-only parties and area spells on her spot; a caster outside the add parties and with no
            // area spell stays on her (before: every caster and archer took the newborns).
            Assert.That(EpicEncounterTactics.Decide(caster, brood, live, 0, true, 1_000, (int)eCharacterClass.Eldritch).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "not in an add party: stay on her");
            Assert.That(EpicEncounterTactics.Decide(tank, brood, live, 0, false, 1_000, (int)eCharacterClass.Hero).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "melee stay on her");

            brood.Dead = true;
            var spawn = Npc<Underdeveloped3>(21290, 51720, 10876);
            spawn.LoadedFromScript = true;
            Assert.That(EpicEncounterTactics.IsDeathAdd(spawn), Is.True);
            Assert.That(RealmRaidDungeonRoute.IsEncounterAdd(spawn, new(21400, 51700, 10876), []), Is.True,
                "she is dead, but her brood is still the fight's");
            var stray = Npc<Add3>(21290, 51720, 10876);
            stray.LoadedFromScript = true;
            Assert.That(RealmRaidDungeonRoute.IsEncounterAdd(stray, new(21400, 51700, 10876), []), Is.False, "an add of nobody living");
        }

        // ---- the early-pull log ---------------------------------------------------------------------------------------

        [Test]
        public void EarlyPullsAreLoggedForBossesFinalsAndPullBacksOnly()
        {
            var commit = EpicEncounterTactics.EarlyPull.Commit;
            Assert.That(EpicEncounterTactics.EarlyPullWorthLogging(EpicEncounterTactics.EarlyPull.Disengage, false, false, 5_000), Is.True,
                "every pull-back");
            Assert.That(EpicEncounterTactics.EarlyPullWorthLogging(commit, false, false, -1), Is.False, "a Darkness Falls named simply fought");
            Assert.That(EpicEncounterTactics.EarlyPullWorthLogging(commit, true, false, -1), Is.True, "a final");
            Assert.That(EpicEncounterTactics.EarlyPullWorthLogging(commit, false, true, -1), Is.True, "a catalogued boss");
            Assert.That(EpicEncounterTactics.EarlyPullWorthLogging(commit, false, true, 60_000), Is.False, "not again within ten minutes");
            Assert.That(EpicEncounterTactics.EarlyPullWorthLogging(commit, false, true, EpicEncounterTactics.EarlyPullLogRepeatMilliseconds), Is.True);

            // 2026-11-06 09:09-13:10: 53 lines. The DF named, trash and Galladoria packs that were simply fought have no
            // catalog row; the scripted princes and lords and Summoner's Hall's two pull-backs still log.
            Assert.That(EpicEncounterTactics.Leashes.Any(l => l.Boss == "SummonerRoesia"), Is.True);
            Assert.That(EpicEncounterTactics.Leashes.Any(l => l.Boss == "Fuladl"), Is.True, "Fuladl's early pull (Krondon) still logs");
        }

        // ---- the restored switch -------------------------------------------------------------------------------------

        [Test]
        public void TheEarthmenderClassRuleIsTheOnlyRestoredMechanicOn()
        {
            var on = EpicEncounterMechanics.RestoreSwitches.Where(name =>
                (bool)typeof(EpicEncounterMechanics).GetProperty(name, BindingFlags.Public | BindingFlags.Static).GetValue(null)).ToArray();
            // Epic wave 2 turned on the ones the raid now plays (Galladoria, Tuscaran Glacier, Summoner's Hall, Caer Sidi): the
            // one list is UT_EpicEncounterTactics.OnSince12.
            Assert.That(on, Is.EquivalentTo(UT_EpicEncounterTactics.OnSince12), "the class rule and wave 2's switches");
        }
    }
}
