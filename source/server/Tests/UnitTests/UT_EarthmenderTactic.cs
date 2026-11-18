using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;
using Kind = DOL.GS.EpicEncounterRestores.Earthmender;

namespace DOL.UnitTests
{
    /// <summary>
    /// Galladoria's Earthmenders (owner 2026-11-18, "Tactic first, then decide"): the raid's heal-watching tactic, and a
    /// deterministic simulation of the fight built on the boss's own rules (the heal spell read from the brain, the brains'
    /// heal-target rule EpicEncounterRestores.EarthmenderHealsTarget, the class puzzle EarthmenderAccepts) and the tactic's
    /// own pure functions, with the raid of build b1b6846's Galladoria run on the owner's PC (118 raiders at the stop).
    /// Tests only: nothing here has been seen in a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EarthmenderTactic
    {
        // ---- the boss's numbers, read from the boss ----------------------------------------------------------------

        private static Spell HealSpell()
        {
            var brain = (GuardianEarthmenderBrain)RuntimeHelpers.GetUninitializedObject(typeof(GuardianEarthmenderBrain));
            return (Spell)typeof(GuardianEarthmenderBrain).GetProperty("EarthmenderHeal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(brain);
        }

        private static int MaxHealthOf<T>() where T : GameNPC => ((T)RuntimeHelpers.GetUninitializedObject(typeof(T))).MaxHealth;

        [Test]
        public void TheSimulationUsesTheBosssOwnHealAndHealth()
        {
            Spell heal = HealSpell();
            Assert.That((heal.Value, heal.CastTime, heal.RecastDelay, heal.Uninterruptible, heal.ID),
                Is.EqualTo((2000.0, 3000, 0, true, EpicEncounterTactics.EarthmenderHealSpellId)), "the menders' heal");
            Assert.That(new[] { MaxHealthOf<GuardianEarthmender>(), MaxHealthOf<MagicalEarthmender>(), MaxHealthOf<NaturalEarthmender>(),
                MaxHealthOf<ShadowyEarthmender>() }, Is.All.EqualTo(60_000));
            Assert.That(MaxHealthOf<EarthPrimal>(), Is.EqualTo(125_000));
            // Level 73, auto-set dexterity 30 + 72 (GameNPC.SetStats): 3,000 x (1 - 42/600) = 2,790, sent as 27 tenths.
            Assert.That(Fight.CastMs(heal), Is.EqualTo(2_700));
        }

        [Test]
        public void TheBrainsHealTheEarthPrimalAndTheOtherHurtMendersNeverThemselves()
        {
            Assert.That(EpicEncounterRestores.EarthmenderHealsTarget(true, false, false, true, 99, false), Is.True, "the primal, always");
            Assert.That(EpicEncounterRestores.EarthmenderHealsTarget(false, true, false, true, 99, true), Is.True, "another mender");
            Assert.That(EpicEncounterRestores.EarthmenderHealsTarget(false, true, false, true, 99, false), Is.False, "mutual heal off");
            Assert.That(EpicEncounterRestores.EarthmenderHealsTarget(false, true, true, true, 50, true), Is.False, "never itself");
            Assert.That(EpicEncounterRestores.EarthmenderHealsTarget(false, true, false, true, 100, true), Is.False, "full");
            Assert.That(EpicEncounterRestores.EarthmenderHealsTarget(false, true, false, false, 0, true), Is.False, "dead");
            Assert.That(EpicEncounterRestores.EarthmenderHealsTarget(false, false, false, true, 10, true), Is.False, "any other NPC");
        }

        // ---- the tactic's pure parts ---------------------------------------------------------------------------------

        private static EpicEncounterTactics.MenderView[] Views(params (Kind K, bool Alive, int Hp, int Heals)[] m) =>
            m.Select(x => new EpicEncounterTactics.MenderView(x.K, x.Alive, x.Hp, x.Heals)).ToArray();

        [Test]
        public void TheMenderToKillIsTheOneTheFewestHealsLandOn()
        {
            var v = Views((Kind.Guardian, true, 99, 9), (Kind.Magical, true, 99, 3), (Kind.Natural, true, 80, 0), (Kind.Shadowy, true, 99, 6));
            Assert.That(EpicEncounterTactics.EarthmenderToKill(v, null), Is.EqualTo(Kind.Natural));
            // Kept while no other has two fewer heals.
            var w = Views((Kind.Guardian, true, 99, 9), (Kind.Magical, true, 99, 0), (Kind.Natural, true, 80, 1), (Kind.Shadowy, true, 99, 6));
            Assert.That(EpicEncounterTactics.EarthmenderToKill(w, Kind.Natural), Is.EqualTo(Kind.Natural));
            Assert.That(EpicEncounterTactics.EarthmenderToKill(w, Kind.Guardian), Is.EqualTo(Kind.Magical));
            // Ties: the lowest health; a dead one never.
            var t = Views((Kind.Guardian, false, 0, 0), (Kind.Magical, true, 70, 2), (Kind.Natural, true, 40, 2), (Kind.Shadowy, true, 99, 5));
            Assert.That(EpicEncounterTactics.EarthmenderToKill(t, Kind.Guardian), Is.EqualTo(Kind.Natural));
            Assert.That(EpicEncounterTactics.EarthmenderToKill(Views((Kind.Guardian, false, 0, 0)), null), Is.Null);
        }

        [Test]
        public void EachClassGoesWhereItsDamageCounts()
        {
            var v = Views((Kind.Guardian, true, 99, 9), (Kind.Magical, true, 99, 3), (Kind.Natural, true, 80, 0), (Kind.Shadowy, false, 0, 0));
            Kind kill = Kind.Natural;
            Kind? Job(eCharacterClass c, int id, bool hurtable = true) => EpicEncounterTactics.EarthmenderJob((int)c, id, v, kill, hurtable);
            Assert.That(Enumerable.Range(0, 20).Select(i => Job(eCharacterClass.Animist, i)), Is.All.EqualTo(Kind.Natural), "animists on the kill");
            Assert.That(Job(eCharacterClass.Valewalker, 7), Is.EqualTo(Kind.Natural));
            Assert.That(Enumerable.Range(0, 20).Select(i => Job(eCharacterClass.Warden, i)), Is.All.EqualTo(Kind.Natural), "its own group stays");
            // A decoy's group: half on it, half on the primal (null), and every raider whose class the puzzle lets hurt it.
            var heroes = Enumerable.Range(0, 200).Select(i => Job(eCharacterClass.Hero, i)).ToArray();
            Assert.That(heroes.Count(j => j == null), Is.InRange(80, 120), "about half keep the primal hurt");
            Assert.That(heroes.Where(j => j != null), Is.All.EqualTo(Kind.Guardian));
            Assert.That(Enumerable.Range(0, 50).Select(i => Job(eCharacterClass.Hero, i, false)), Is.All.EqualTo(Kind.Guardian),
                "the primal out of his tether takes no damage: everyone stays on a mender");
            Assert.That(Enumerable.Range(0, 20).Select(i => Job(eCharacterClass.Ranger, i)), Is.All.Null, "the Shadowy one is dead: the primal");
            Assert.That(Job(eCharacterClass.Bainshee, 3), Is.Null, "a class that hurts none: the primal");
            // The split is fixed by the raider: the same raider gets the same job each turn.
            Assert.That(Job(eCharacterClass.Hero, 12345), Is.EqualTo(Job(eCharacterClass.Hero, 12345)));
            // Never a mender the class can't hurt.
            foreach (eCharacterClass c in Enum.GetValues<eCharacterClass>())
                for (int id = 0; id < 8; id++)
                    if (Job(c, id) is Kind k)
                        Assert.That(EpicEncounterRestores.EarthmenderAccepts(k, (int)c), Is.True, $"{c} sent to {k}");
        }

        [Test]
        public void ThePlanIsLoggedAsATacticLine()
        {
            var v = Views((Kind.Guardian, true, 99, 9), (Kind.Magical, true, 99, 3), (Kind.Natural, true, 80, 0), (Kind.Shadowy, false, 0, 0));
            string line = EpicEncounterTactics.EarthmenderPlanLine(191, "Corrupt Primal of Earth", "Natural Earthmender", v, true);
            Assert.That(line, Does.StartWith("REALM_EXPEDITION_TACTIC region=191 encounter=\"Corrupt Primal of Earth\" tactic=galladoria/earthmenders"));
            Assert.That(line, Does.Contain("kill=\"Natural Earthmender\" heals=\"Guardian:9,Magical:3,Natural:0,Shadowy:dead\""));
            Assert.That(line, Does.Contain("health=\"Guardian:99,Magical:99,Natural:80\""));
            Assert.That(line, Does.Contain("half on the primal"));
            Assert.That(line, Does.Not.Contain("from=").And.Not.Contain("required-adds of"), "the checker's required-adds row ignores it");
            Assert.That(EpicEncounterTactics.EarthmenderPlanLine(191, "p", null, v, false), Does.Contain("kill=\"none\"").And.Contain("out of his tether"));
        }

        [Test]
        public void TheHealWatchCountsEachCastOnceOnItsTarget()
        {
            var watch = new EpicEncounterTactics.EarthmenderHealWatch();
            object a = new(), b = new();
            watch.Note(1, a, 20, 1_000);
            watch.Note(1, a, 20, 1_250); // the same cast, sampled again
            watch.Note(2, b, 20, 1_500);
            watch.Note(1, null, 0, 4_000);
            watch.Note(1, new object(), 30, 4_250);
            Assert.That((watch.Seen(20, 5_000), watch.Seen(30, 5_000)), Is.EqualTo((2, 1)));
            Assert.That(watch.Seen(20, 1_000 + EpicEncounterTactics.EarthmenderHealWindowMs + 600), Is.EqualTo(0), "older than the window");
        }

        // ---- the rule in Decide, with the room's real classes -----------------------------------------------------------

        public sealed class TestPrimal : EarthPrimal
        {
            public bool Fighting; // GetUninitializedObject: set by the test
            public override bool IsAlive => true;
            public override bool InCombat => Fighting;
            public override byte HealthPercent => 99;
        }
        public sealed class TestGuardian : GuardianEarthmender { public bool Dead; public override bool IsAlive => !Dead; public override byte HealthPercent => 99; }
        public sealed class TestMagical : MagicalEarthmender { public bool Dead; public override bool IsAlive => !Dead; public override byte HealthPercent => 99; }
        public sealed class TestNatural : NaturalEarthmender { public bool Dead; public override bool IsAlive => !Dead; public override byte HealthPercent => 99; }
        public sealed class TestShadowy : ShadowyEarthmender { public bool Dead; public override bool IsAlive => !Dead; public override byte HealthPercent => 99; }
        public sealed class Raider : GameNPC { public override bool IsAlive => true; }

        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private EpicTestServerScope _server;
        private Region _region;
        private ushort _nextId = 500;
        private bool _puzzle, _mutual;
        private Func<GameNPC, (object, GameNPC)> _cast;
        private Func<GameNPC, bool> _tether;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 64, Name = "Earthmender test", Description = "Earthmender test", Mobs = [] });
            _puzzle = EpicEncounterMechanics.AllowEarthmenderClassPuzzle;
            _mutual = EpicEncounterMechanics.AllowEarthmenderMutualHeal;
            _cast = EpicEncounterTactics.EarthmenderCast;
            _tether = EpicEncounterTactics.EarthPrimalOutOfTether;
            EpicEncounterTactics.EarthPrimalOutOfTether = _ => false;
            EpicEncounterTactics.ResetEarthmenderTacticForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = _puzzle;
            EpicEncounterMechanics.AllowEarthmenderMutualHeal = _mutual;
            EpicEncounterTactics.EarthmenderCast = _cast;
            EpicEncounterTactics.EarthPrimalOutOfTether = _tether;
            EpicEncounterTactics.ResetEarthmenderTacticForTests();
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

        [Test]
        public void RaidersAreSentByTheHealsTheyWatch()
        {
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = true;
            EpicEncounterMechanics.AllowEarthmenderMutualHeal = true;
            var primal = Npc<TestPrimal>(39727, 62620, 11684);
            primal.Fighting = true;
            var g = Npc<TestGuardian>(40020, 62401, 11676);
            var m = Npc<TestMagical>(39459, 62412, 11688);
            var n = Npc<TestNatural>(39552, 62929, 11690);
            var s = Npc<TestShadowy>(39965, 62921, 11662);
            var live = new List<GameNPC> { primal, g, m, n, s };
            // Order G, M, N, S then the primal: G heals M first, the other three heal G first; their queued casts go to the primal.
            var casts = new Dictionary<GameNPC, (object, GameNPC)> { [g] = (new object(), m), [m] = (new object(), g), [n] = (new object(), g), [s] = (new object(), g) };
            EpicEncounterTactics.EarthmenderCast = h => casts.TryGetValue(h, out var x) ? x : (null, null);
            var raider = Npc<Raider>(39800, 62500, 11684);

            var animist = EpicEncounterTactics.Decide(raider, primal, live, 0, true, 10_000, (int)eCharacterClass.Animist);
            Assert.That((animist.Action, animist.Target), Is.EqualTo((EpicEncounterTactics.BotAction.Retarget, (GameNPC)n)),
                "Natural and Shadowy get no heal; Natural is first in the catalog's order");
            Assert.That(animist.Reason, Does.Contain("kill"));
            Assert.That(EpicEncounterTactics.Decide(raider, n, live, 0, true, 10_100, (int)eCharacterClass.Animist).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "settled: asked again on the kill, it fights on");
            var ranger = EpicEncounterTactics.Decide(raider, primal, live, 0, true, 10_200, (int)eCharacterClass.Ranger);
            Assert.That(ranger.Target, Is.AnyOf(s, primal), "a ranger's mender is a decoy: on it or on the primal");
            var heroes = Enumerable.Range(0, 40).Select(i =>
            {
                raider.ObjectID = (ushort)(600 + i);
                return EpicEncounterTactics.Decide(raider, m, live, 0, false, 10_300, (int)eCharacterClass.Hero).Target;
            }).ToArray();
            Assert.That(heroes, Is.All.AnyOf(g, primal), "a hero never stays on the Magical one");
            Assert.That(heroes.Count(t => t == primal), Is.InRange(8, 32), "half the Guardian's group keeps the primal hurt");

            // The Natural one dies: its wardens, bards and druids go to the primal.
            n.Dead = true;
            live.Remove(n);
            raider.ObjectID = 700;
            Assert.That(EpicEncounterTactics.Decide(raider, primal, live, 0, false, 12_000, (int)eCharacterClass.Warden).Action,
                Is.EqualTo(EpicEncounterTactics.BotAction.None), "a warden stays on the primal");
            Assert.That(EpicEncounterTactics.Decide(raider, primal, live, 0, true, 12_100, (int)eCharacterClass.Animist).Target, Is.SameAs(s),
                "the next one with no heal");

            // Mutual heal off (or the puzzle off): no opinion, the catalog's rules as before.
            EpicEncounterMechanics.AllowEarthmenderMutualHeal = false;
            Assert.That(EpicEncounterTactics.Decide(raider, primal, live, 0, true, 13_000, (int)eCharacterClass.Animist).Reason ?? "",
                Does.Not.Contain("earthmenders:"));
        }

        // ---- the simulation -------------------------------------------------------------------------------------------

        /// <summary>Build b1b6846's Galladoria raid at the 12:47:28 stop (AUTONOMOUS_GROUP_OUTCOME, 16 parties, 118 raiders).</summary>
        public static readonly (eCharacterClass Class, int Count)[] B1b6Roster =
        [
            (eCharacterClass.Druid, 19), (eCharacterClass.Warden, 15), (eCharacterClass.Champion, 15), (eCharacterClass.Bard, 14),
            (eCharacterClass.Hero, 10), (eCharacterClass.Animist, 9), (eCharacterClass.Enchanter, 7), (eCharacterClass.Blademaster, 6),
            (eCharacterClass.Valewalker, 6), (eCharacterClass.Nightshade, 5), (eCharacterClass.Mentalist, 5), (eCharacterClass.Eldritch, 4),
            (eCharacterClass.Ranger, 3),
        ];

        /// <summary>Damage one raider dealt each mender a minute, b1b6846's 47 EPIC_FIGHT_LEDGER lines 11:59:43-12:47:28 (damage
        /// over attacker-minutes): Guardian 699,959 / 1,612, Natural 328,857 / 1,053, Magical 244,091 / 1,020, Shadowy 199,366
        /// / 428, the Earth primal 46,894 / 376. Animists and valewalkers: 400 (assumed; the ledger mixes them in).</summary>
        public static readonly Dictionary<Kind, int> PerRaiderPerMinute = new()
            { [Kind.Guardian] = 434, [Kind.Magical] = 239, [Kind.Natural] = 312, [Kind.Shadowy] = 467 };
        public const int UniversalPerMinute = 400, PrimalPerMinute = 124;

        /// <summary>b1b6846's attackers a minute on each (the same ledger): what the old split put on them.</summary>
        public static readonly Dictionary<int, int> B1b6Attackers = new() { [0] = 34, [1] = 22, [2] = 22, [3] = 9, [Fight.Primal] = 8 };

        public sealed class Fight
        {
            public const int Primal = 4;
            public const int TickMs = 50, ThinkMs = 500;
            private readonly int[] _order;                // GetNPCsInRadius's order: 0-3 the menders, 4 the primal
            private readonly int[] _hp = new int[5], _max = new int[5];
            private readonly int[] _cur = { -1, -1, -1, -1 }, _queued = { -1, -1, -1, -1 };
            private readonly long[] _end = new long[4], _nextThink = new long[4];
            private readonly Random _rng;
            private readonly double _healValue;
            private readonly int _castMs;
            public readonly double?[] DiedAtMinute = new double?[4];
            public readonly EpicEncounterTactics.EarthmenderHealWatch Watch = new();
            public long Now { get; private set; }
            public sealed class R
            {
                public int Id, Class; public Kind? Group; public bool Universal, Heals; public int SwingMs; public long NextHit; public int On = -1;
                public double PerMinute;
            }
            public readonly List<R> Raid = new();

            public static int CastMs(Spell heal)
            {
                const int dexterity = 30 + 72; // GameNPC.SetStats at level 73 (MOB_AUTOSET_DEX_BASE 30, multiplier 1)
                double ms = Math.Max(heal.CastTime * (1 - (dexterity - 60) / 600.0), heal.CastTime * 0.4); // GameLiving.CalculateCastingTime
                return (int)ms / 100 * 100; // SpellHandler.SendCastAnimation sends tenths and ends the cast there
            }

            public Fight(int[] order, int seed, double damageScale, bool druidsFight, IEnumerable<(eCharacterClass, int)> roster)
            {
                _order = order;
                _rng = new Random(seed);
                Spell heal = HealSpell();
                _healValue = heal.Value;
                _castMs = CastMs(heal);
                for (int i = 0; i < 4; i++) _max[i] = _hp[i] = 60_000;
                _max[Primal] = _hp[Primal] = MaxHealthOf<EarthPrimal>();
                for (int i = 0; i < 4; i++) _nextThink[i] = i * 137 % ThinkMs;
                int id = 0;
                foreach (var (c, count) in roster)
                    for (int k = 0; k < count; k++, id++)
                    {
                        Kind[] hurts = EpicEncounterTactics.MendersHurtBy((int)c);
                        var r = new R
                        {
                            Id = 1_000 + id, Class = (int)c, Universal = hurts.Length > 1, Group = hurts.Length == 1 ? hurts[0] : null,
                            Heals = c == eCharacterClass.Druid && !druidsFight, SwingMs = 3_000 + id * 97 % 1_000,
                        };
                        r.NextHit = id * 311 % r.SwingMs;
                        r.PerMinute = damageScale * (r.Universal ? UniversalPerMinute : r.Group is Kind g ? PerRaiderPerMinute[g] : 0);
                        Raid.Add(r);
                    }
            }

            public bool Alive(int i) => _hp[i] > 0;
            public int HealthPercent(int i) => _max[i] <= 0 ? 0 : Math.Clamp(_hp[i] * 100 / _max[i], 0, 100); // GameObject.HealthPercent
            public bool AllDead => Enumerable.Range(0, 4).All(i => !Alive(i));

            public void Step()
            {
                // The raid's hits (the puzzle: EarthmenderAccepts; pets are not modelled apart, the ledger counted them in).
                foreach (R r in Raid)
                {
                    if (r.On < 0 || Now < r.NextHit) continue;
                    r.NextHit += r.SwingMs;
                    if (!Alive(r.On) && r.On != Primal) continue;
                    double perMinute = r.On == Primal ? PrimalPerMinute : r.PerMinute;
                    if (r.On != Primal && !EpicEncounterRestores.EarthmenderAccepts((Kind)r.On, r.Class)) continue;
                    int dmg = (int)Math.Round(perMinute * r.SwingMs / 60_000.0);
                    _hp[r.On] = r.On == Primal ? Math.Max(1, _hp[r.On] - dmg) : Math.Max(0, _hp[r.On] - dmg);
                    if (r.On != Primal && _hp[r.On] == 0 && DiedAtMinute[r.On] == null) DiedAtMinute[r.On] = Now / 60_000.0;
                }
                for (int h = 0; h < 4; h++)
                {
                    if (!Alive(h)) { _cur[h] = _queued[h] = -1; continue; }
                    if (_cur[h] >= 0 && Now >= _end[h])
                    {
                        // HealSpellHandler: Value x the Mob_Spells variance 0.6-1.0, capped by the missing health.
                        int t = _cur[h];
                        if (Alive(t)) _hp[t] = Math.Min(_max[t], _hp[t] + (int)(_healValue * (0.6 + 0.4 * _rng.NextDouble())));
                        _cur[h] = -1;
                        if (_queued[h] >= 0 && Alive(_queued[h])) Start(h, _queued[h]); // CastingComponent.PromoteQueuedSpellHandler
                        _queued[h] = -1;
                    }
                    if (Now >= _nextThink[h])
                    {
                        _nextThink[h] += ThinkMs;
                        if (_cur[h] >= 0) continue; // if (!Body.IsCasting)
                        // The brain's loop: a cast request for each NPC that passes the rule, in the radius list's order.
                        int[] asked = _order.Where(i => EpicEncounterRestores.EarthmenderHealsTarget(i == Primal, i != Primal, i == h, Alive(i),
                            HealthPercent(i), EpicEncounterMechanics.AllowEarthmenderMutualHeal)).ToArray();
                        if (asked.Length == 0) continue;
                        Start(h, asked[0]);                         // the first request starts
                        if (asked.Length > 1) _queued[h] = asked[^1]; // each later one replaces the queued spell: the last stays
                    }
                }
                Now += TickMs;
            }

            private object[] _castToken = new object[4];
            private void Start(int healer, int target)
            {
                _cur[healer] = target;
                _end[healer] = Now + TickMs + _castMs;
                _castToken[healer] = new object();
                Watch.Note(healer, _castToken[healer], 100 + target, Now);
            }

            public EpicEncounterTactics.MenderView[] Views() => Enumerable.Range(0, 4)
                .Select(i => new EpicEncounterTactics.MenderView((Kind)i, Alive(i), HealthPercent(i), Watch.Seen(100 + i, Now))).ToArray();
        }

        /// <summary>The old split: b1b6846's attackers on each, as its ledger shows them.</summary>
        private static void OldSplit(Fight f)
        {
            var want = new Dictionary<int, int>(B1b6Attackers);
            foreach (Fight.R r in f.Raid) r.On = -1;
            foreach (Fight.R r in f.Raid.Where(r => !r.Heals))
            {
                if (r.Group is Kind g && want[(int)g] > 0) { r.On = (int)g; want[(int)g]--; continue; }
                if (r.Universal)
                {
                    int pick = Enumerable.Range(0, 4).Where(i => f.Alive(i) && want[i] > 0).DefaultIfEmpty(-1).First();
                    if (pick >= 0) { r.On = pick; want[pick]--; continue; }
                }
                if (want[Fight.Primal] > 0) { r.On = Fight.Primal; want[Fight.Primal]--; }
            }
            // A dead mender's raiders stay idle in the old split: the log never got that far.
        }

        /// <summary>The old rule with every raider of the roster in it (EpicEncounterTactics.Decide before this change): each on
        /// its own group's mender, animists and valewalkers spread by party (AssignedIndex), a dead group's on the primal.</summary>
        private static void OldRuleWholeRaid(Fight f)
        {
            int[] alive = Enumerable.Range(0, 4).Where(f.Alive).ToArray();
            int n = 0;
            foreach (Fight.R r in f.Raid)
            {
                int party = n++ / 8;
                if (r.Heals) { r.On = -1; continue; }
                if (r.Universal) r.On = alive.Length > 0 ? alive[EpicEncounterTactics.AssignedIndex(party, alive.Length)] : Fight.Primal;
                else r.On = r.Group is Kind g && f.Alive((int)g) ? (int)g : Fight.Primal;
            }
        }

        private Kind? _kill;
        /// <summary>The new tactic: EpicEncounterTactics.EarthmenderToKill and EarthmenderJob, on the heals the watch saw.</summary>
        private void NewTactic(Fight f)
        {
            var views = f.Views();
            _kill = EpicEncounterTactics.EarthmenderToKill(views, _kill);
            foreach (Fight.R r in f.Raid)
                r.On = r.Heals ? -1 : EpicEncounterTactics.EarthmenderJob(r.Class, r.Id, views, _kill, true) is Kind k ? (int)k : Fight.Primal;
        }

        private (int Killed, double?[] At) Run(int[] order, Action<Fight> plan, double scale = 1, bool druidsFight = false, int minutes = 120,
            bool mutualHeal = true)
        {
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = true;
            EpicEncounterMechanics.AllowEarthmenderMutualHeal = mutualHeal;
            _kill = null;
            var f = new Fight(order, 7, scale, druidsFight, B1b6Roster);
            while (f.Now < minutes * 60_000L && !f.AllDead)
            {
                if (f.Now % 2_000 == 0) plan(f); // bots re-decide every couple of seconds
                f.Step();
            }
            return (f.DiedAtMinute.Count(d => d != null), f.DiedAtMinute);
        }

        private static readonly int[] PrimalLast = [0, 1, 2, 3, Fight.Primal], PrimalFirst = [Fight.Primal, 0, 1, 2, 3];

        private static string Show(double?[] at) =>
            string.Join(" ", Enumerable.Range(0, 4).Select(i => $"{(Kind)i}={(at[i] is double d ? $"{d:0.0}min" : "alive")}"));

        [Test]
        public void TheOldSplitKillsNoneLikeThePcLog()
        {
            // The radius list's order is the subzone's insertion order (Zone.GetObjectsInRadius, SubZone AddLast): the menders
            // spawn Guardian, Magical, Natural, Shadowy; the primal spawns before them but goes to the end of the list whenever
            // he leaves the subzone and comes back. With b1b6846's ledger split, the primal first in the list would have let
            // the Guardian die in about 5 minutes (nobody heals it: every first cast goes to the hurt primal). The log shows 46
            // minutes at 99-100%, so on the PC he was after the menders (Inference). Both orders are measured below.
            var (killed, at) = Run(PrimalLast, OldSplit, minutes: 48);
            TestContext.WriteLine($"old split (b1b6846 ledger), primal last: {killed} of 4 in 48 min; {Show(at)}");
            Assert.That(killed, Is.EqualTo(0), "the log: 46 minutes, every Earthmender at 99-100%");
            var first = Run(PrimalFirst, OldSplit, minutes: 48);
            TestContext.WriteLine($"old split (b1b6846 ledger), primal first: {first.Killed} of 4 in 48 min; {Show(first.At)}");
            Assert.That(first.Killed, Is.EqualTo(1), "the order the log rules out");
        }

        [Test]
        public void TheNewTacticKillsOneMenderAndThenStalls()
        {
            foreach (int[] order in new[] { PrimalLast, PrimalFirst })
            {
                var old = Run(order, OldRuleWholeRaid);
                var neu = Run(order, NewTactic);
                TestContext.WriteLine($"order {string.Join(",", order)}: old rule, whole raid: {old.Killed} of 4 in 120 min ({Show(old.At)}); " +
                                      $"new tactic: {neu.Killed} of 4 ({Show(neu.At)})");
                Assert.That(old.Killed, Is.EqualTo(0));
                Assert.That(neu.Killed, Is.GreaterThanOrEqualTo(1), "the heal-starved mender dies");
                Assert.That(neu.At.Where(d => d != null).Min(), Is.LessThan(15.0));
                // Honest result: with b1b6846's damage the other three hold (no win). The next test measures the gap.
                Assert.That(neu.Killed, Is.LessThan(4), "if this fails the raid can win: update the docs and the report");
            }
        }

        /// <summary>What the raid does with mutual heal off (the default since the owner's 2026-11-18 decision): the heal-watching
        /// rule has no opinion, and the catalog's class rule sends each raider to a mender its class hurts.</summary>
        private void RaidWithMutualHealOff(Fight f)
        {
            int[] alive = Enumerable.Range(0, 4).Where(f.Alive).ToArray();
            var views = f.Views();
            int n = 0;
            foreach (Fight.R r in f.Raid)
            {
                int party = n++ / 8;
                // The tactic stays gated: off with mutual heal off.
                Assert.That(EpicEncounterMechanics.AllowEarthmenderMutualHeal, Is.False);
                if (r.Heals) { r.On = -1; continue; }
                if (r.Universal) r.On = alive.Length > 0 ? alive[EpicEncounterTactics.AssignedIndex(party, alive.Length)] : Fight.Primal;
                else r.On = r.Group is Kind g && f.Alive((int)g) ? (int)g : Fight.Primal;
                if (r.On != Fight.Primal)
                    Assert.That(EpicEncounterRestores.EarthmenderAccepts((Kind)r.On, r.Class), Is.True, $"{(eCharacterClass)r.Class} on {(Kind)r.On}");
            }
        }

        [Test]
        public void WithMutualHealOffTheRaidKillsAllFour()
        {
            // Owner 2026-11-18, "Mutual heal off": the menders heal only the primal, so nothing heals a mender.
            foreach (int[] order in new[] { PrimalLast, PrimalFirst })
            {
                var split = Run(order, OldSplit, minutes: 120, mutualHeal: false);
                var raid = Run(order, RaidWithMutualHealOff, minutes: 120, mutualHeal: false);
                var druids = Run(order, RaidWithMutualHealOff, druidsFight: true, minutes: 120, mutualHeal: false);
                TestContext.WriteLine($"mutual heal off, order {string.Join(",", order)}: b1b6846's ledger split: {split.Killed} of 4 " +
                                      $"({Show(split.At)}); class rule, whole raid: {raid.Killed} of 4 ({Show(raid.At)}); " +
                                      $"with the druids fighting: {druids.Killed} of 4 ({Show(druids.At)})");
                Assert.That(raid.Killed, Is.EqualTo(4), "all four die");
                Assert.That(raid.At.Max(), Is.LessThan(20.0));
                Assert.That(split.Killed, Is.EqualTo(4), "even b1b6846's own split kills all four");
            }
        }

        [Test]
        public void WithMutualHealOffEachClassStillTakesAMenderItHurts()
        {
            EpicEncounterMechanics.AllowEarthmenderClassPuzzle = true;
            EpicEncounterMechanics.AllowEarthmenderMutualHeal = false;
            var primal = Npc<TestPrimal>(39727, 62620, 11684);
            primal.Fighting = true;
            GameNPC[] menders = [Npc<TestGuardian>(40020, 62401, 11676), Npc<TestMagical>(39459, 62412, 11688),
                Npc<TestNatural>(39552, 62929, 11690), Npc<TestShadowy>(39965, 62921, 11662)];
            var live = new List<GameNPC> { primal }.Concat(menders).ToList();
            var raider = Npc<Raider>(39800, 62500, 11684);
            // The catalog's row, put on the test doubles (as UT_EpicEncounterTactics3 does).
            var names = new Dictionary<string, string>
            {
                ["GuardianEarthmender"] = nameof(TestGuardian), ["MagicalEarthmender"] = nameof(TestMagical),
                ["NaturalEarthmender"] = nameof(TestNatural), ["ShadowyEarthmender"] = nameof(TestShadowy),
            };
            var row = EpicEncounterTactics.Catalog.Single(t => t.Boss == "EarthPrimal");
            EpicEncounterTactics.Register(row with
            {
                Boss = nameof(TestPrimal), RequiredAdds = row.RequiredAdds.Select(a => names[a]).ToArray(),
                ClassRules = row.ClassRules.Select(r => r with { Add = names[r.Add] }).ToArray()
            });
            try
            {
            foreach (var (c, _) in B1b6Roster)
                for (int party = 0; party < 4; party++)
                foreach (GameNPC aim in live)
                {
                    var advice = EpicEncounterTactics.Decide(raider, aim, live, party, true, 20_000, (int)c);
                    Assert.That(advice.Reason ?? "", Does.Not.Contain("earthmenders:"), "the heal-watching rule is off");
                    GameNPC on = advice.Action == EpicEncounterTactics.BotAction.Retarget ? advice.Target : aim;
                    Assert.That(on, Is.Not.SameAs(primal), $"{c}: every class of the roster hurts a mender");
                    Assert.That(EpicEncounterRestores.EarthmenderAccepts((Kind)Array.IndexOf(menders, on), (int)c), Is.True,
                        $"{c} aiming at {aim.GetType().Name}, party {party}: sent to {on.GetType().Name}");
                }
            }
            finally { EpicEncounterTactics.Unregister(nameof(TestPrimal)); }
        }

        [Test, Explicit("Measurement: a few minutes of simulation. Its numbers are in docs/encounters/EPIC-ENCOUNTERS.md.")]
        public void TheDamageTheRaidWouldNeedToWin()
        {
            foreach (int[] order in new[] { PrimalLast, PrimalFirst })
            foreach (bool druids in new[] { false, true })
            foreach (var (name, plan) in new (string, Action<Fight>)[] { ("old rule", OldRuleWholeRaid), ("new tactic", NewTactic) })
            {
                string found = "no win up to x6";
                for (double scale = 1; scale <= 6.0001; scale += 0.25)
                {
                    var r = Run(order, plan, scale, druids);
                    if (r.Killed == 4) { found = $"wins at x{scale:0.00} ({Show(r.At)})"; break; }
                }
                TestContext.WriteLine($"order {string.Join(",", order)}, druids {(druids ? "fight" : "heal")}, {name}: {found}");
            }
        }
    }
}
