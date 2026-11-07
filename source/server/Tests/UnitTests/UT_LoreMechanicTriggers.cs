using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using PhaseLine = DOL.GS.LoreBible.PhaseLine;

namespace DOL.UnitTests
{
    /// <summary>
    /// The lore engine's mechanic triggers (Living Server 1.2, feature/lore-mechanic-triggers): a boss script's throw,
    /// teleport, jail, pull or push (EncounterDisplacement.Displaced), its announced glare, stun, breath, stomp, messengers,
    /// retreat or safe spots (EncounterSignals.Raised) and its lore-only moments (EncounterSignals.Moment: berserk, scream,
    /// healed, twinHeal) fire the boss file's <c>yells.phases</c> and <c>atmosphere.phases</c> lines of that name.
    /// </summary>
    [TestFixture]
    public class UT_LoreMechanicTriggers
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 700;
        private EpicTestServerScope _server;
        private Action<GameNPC, string> _yell, _atmosphere;
        private readonly List<(GameNPC Npc, string Text)> _yells = new(), _lines = new();

        public class TestBoss : GameNPC
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
        }
        // Named as the scripts' classes, so the engine finds their real lore files (LoreBible.Current, by class).
        public sealed class Suttung : TestBoss { }
        public sealed class Krackenschtein : TestBoss { }
        public sealed class MidGjalpinulva : TestBoss { }
        public sealed class EasmarachLikeOrb : TestBoss { }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 65, Name = "Lore mechanic test", Description = "Lore mechanic test", Mobs = [] });
            EncounterSignals.ClearAll();
            LoreBossWatch.ResetForTests();
            LoreBossWatch.ListenToMechanics();
            _yell = LoreBossWatch.MechanicYell;
            _atmosphere = LoreBossWatch.MechanicAtmosphere;
            _yells.Clear();
            _lines.Clear();
            LoreBossWatch.MechanicYell = (npc, text) => _yells.Add((npc, text));
            LoreBossWatch.MechanicAtmosphere = (npc, text) => _lines.Add((npc, text));
        }

        [TearDown]
        public void TearDown()
        {
            LoreBossWatch.StopListeningToMechanics();
            LoreBossWatch.MechanicYell = _yell;
            LoreBossWatch.MechanicAtmosphere = _atmosphere;
            LoreBossWatch.ResetForTests();
            EncounterSignals.ClearAll();
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

        private static DisplacementEvent Moved(GameNPC boss, GameLiving raider, DisplacementKind kind, long at) =>
            new(raider, boss, boss.GetType().Name, kind, DisplacementRecovery.WalkBack, 65, Vector3.Zero, Vector3.One, Vector3.One, 0, true, at, true);

        private static LoreBible.Boss Lore(string scriptClass) => LoreBible.Current.BossForClass(scriptClass);

        private static string[] Texts(IEnumerable<PhaseLine> lines, string trigger) =>
            lines.Where(p => string.Equals(p.Trigger, trigger, StringComparison.OrdinalIgnoreCase)).Select(p => p.Text).ToArray();

        // ---- the triggers ---------------------------------------------------------------------------------------------

        [Test]
        public void EveryMechanicTriggerIsKnownAndFiredByAScript()
        {
            foreach (string name in LoreFightRules.MechanicTriggers)
            {
                Assert.That(LoreFightRules.TryParseTrigger(name, out var t) && t.Kind == LoreFightRules.Kind.Mechanic && t.ByScript, Is.True, name);
                Assert.That(LoreFightRules.TryParseTrigger(name.ToUpperInvariant(), out _), Is.True, name + " in any case");
            }
            Assert.That(LoreFightRules.ScriptOnlyTriggers, Is.Empty, "every trigger the files may use fires now");
            Assert.That(LoreFightRules.TryParseTrigger("enrage", out _), Is.False);
            string[] fired = Enum.GetValues<DisplacementKind>().SelectMany(LoreFightRules.DisplacementTriggers)
                .Concat(new[] { EncounterSignals.Glare, EncounterSignals.Stun, EncounterSignals.Breath, EncounterSignals.Stomp,
                    EncounterSignals.Messengers, EncounterSignals.Retreat, EncounterSignals.SafeSpots, EncounterSignals.Berserk,
                    EncounterSignals.Scream, EncounterSignals.Healed, EncounterSignals.TwinHeal }.SelectMany(LoreFightRules.SignalTriggers))
                .Distinct().ToArray();
            Assert.That(fired, Is.EquivalentTo(LoreFightRules.MechanicTriggers), "each name has an event that fires it, and no event fires an unknown name");
        }

        [Test]
        public void AMoveFiresItsKindAndTeleportIsEveryMovesFallback()
        {
            Assert.That(LoreFightRules.DisplacementTriggers(DisplacementKind.Throw), Is.EqualTo(new[] { "throw", "teleport" }));
            Assert.That(LoreFightRules.DisplacementTriggers(DisplacementKind.Teleport), Is.EqualTo(new[] { "teleport" }));
            Assert.That(LoreFightRules.DisplacementTriggers(DisplacementKind.Jail), Is.EqualTo(new[] { "jail", "teleport" }));
            Assert.That(LoreFightRules.DisplacementTriggers(DisplacementKind.Pull), Is.EqualTo(new[] { "pull", "teleport" }));
            Assert.That(LoreFightRules.DisplacementTriggers(DisplacementKind.Barrier), Is.EqualTo(new[] { "barrier", "teleport" }));
            PhaseLine[] onlyTeleport = [new("teleport", "Off you go.")];
            PhaseLine[] both = [new("teleport", "Off you go."), new("throw", "Fly!")];
            string[] throwing = LoreFightRules.DisplacementTriggers(DisplacementKind.Throw);
            Assert.That(LoreFightRules.Mechanic(new(), onlyTeleport, [], throwing, 0)?.Trigger, Is.EqualTo("teleport"), "the writers' word for any move");
            Assert.That(LoreFightRules.Mechanic(new(), both, [], throwing, 0)?.Yells, Is.EqualTo(new[] { "Fly!" }), "the kind's own line first");
            Assert.That(LoreFightRules.Mechanic(new(), [new PhaseLine("jail", "Stay.")], [], throwing, 0), Is.Null, "a jail line is not a throw's");
            Assert.That(LoreFightRules.Mechanic(new(), [new PhaseLine("health<=50", "x")], [], throwing, 0), Is.Null, "no line, nothing said");
        }

        [Test]
        public void EachSignalAndMomentFiresItsOwnName()
        {
            Assert.That(LoreFightRules.SignalTriggers(EncounterSignals.Glare), Is.EqualTo(new[] { "glare", "stare" }));
            foreach ((string mechanic, string trigger) in new[] {
                (EncounterSignals.Stun, "stun"), (EncounterSignals.Breath, "breath"), (EncounterSignals.Stomp, "stomp"),
                (EncounterSignals.Messengers, "messengers"), (EncounterSignals.Retreat, "retreat"), (EncounterSignals.SafeSpots, "safeSpots"),
                (EncounterSignals.Berserk, "berserk"), (EncounterSignals.Scream, "scream"), (EncounterSignals.Healed, "healed"),
                (EncounterSignals.TwinHeal, "twinHeal") })
            {
                Assert.That(LoreFightRules.SignalTriggers(mechanic), Is.EqualTo(new[] { trigger }), mechanic);
                PhaseLine[] lines = [new(trigger, "line " + trigger)];
                LoreFightRules.MechanicLines said = LoreFightRules.Mechanic(new(), lines, lines, LoreFightRules.SignalTriggers(mechanic), 0);
                Assert.That(said?.Trigger, Is.EqualTo(trigger), mechanic);
                Assert.That(said.Yells, Is.EqualTo(new[] { "line " + trigger }), mechanic);
                Assert.That(said.Atmosphere, Is.EqualTo("line " + trigger), mechanic);
            }
            Assert.That(LoreFightRules.Mechanic(new(), [new PhaseLine("stare", "eyes")], [], LoreFightRules.SignalTriggers(EncounterSignals.Glare), 0)?.Trigger,
                Is.EqualTo("stare"), "a glare is also a stare");
        }

        [Test]
        public void AMechanicYellsOnceAMinuteAndItsAtmosphereLineOnceAFightTwoAtMost()
        {
            var fight = new LoreFightRules.Fight();
            PhaseLine[] yells = [new("breath", "a"), new("breath", "b")];
            PhaseLine[] lines = [new("breath", "B!"), new("glare", "G!"), new("stun", "S!")];
            var first = LoreFightRules.Mechanic(fight, yells, lines, ["breath"], 0);
            Assert.That(first.Yells, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(first.Atmosphere, Is.EqualTo("B!"));
            var again = LoreFightRules.Mechanic(fight, yells, lines, ["breath"], 30_000);
            Assert.That(again.Yells, Is.Empty, "not twice in a minute");
            Assert.That(again.Atmosphere, Is.Null, "the line once a fight");
            Assert.That(LoreFightRules.Mechanic(fight, yells, lines, ["breath"], 60_000).Yells, Has.Length.EqualTo(2), "a minute later");
            Assert.That(LoreFightRules.Mechanic(fight, yells, lines, ["glare"], 61_000).Atmosphere, Is.EqualTo("G!"));
            Assert.That(LoreFightRules.Mechanic(fight, yells, lines, ["stun"], 62_000).Atmosphere, Is.Null,
                $"at most {LoreFightRules.MaxMechanicAtmospherePerFight} mechanic lines a fight");
            Assert.That(LoreFightRules.Mechanic(new LoreFightRules.Fight { AtmosphereLines = LoreFightRules.MaxAtmospherePerFight }, [], lines, ["stun"], 0).Atmosphere, Is.EqualTo("S!"),
                "the health lines' own cap does not hold a mechanic back");
        }

        // ---- the events -----------------------------------------------------------------------------------------------

        [Test]
        public void EveryMoveKindIsQueuedWithItsTriggers()
        {
            var boss = Npc<Krackenschtein>(1_000, 1_000, 0);
            var raider = Npc<Raider>(1_100, 1_000, 0);
            foreach (DisplacementKind kind in Enum.GetValues<DisplacementKind>())
                LoreBossWatch.HearDisplaced(Moved(boss, raider, kind, 0));
            Assert.That(LoreBossWatch.PendingMechanics.Select(p => p.Triggers[0]), Is.EqualTo(new[] { "throw", "teleport", "jail", "pull", "barrier" }));
            Assert.That(LoreBossWatch.PendingMechanics.All(p => p.Source == boss), Is.True);
        }

        [Test]
        public void ARaisedSignalAndANotedMomentReachTheEngineButAMomentReachesNoTactic()
        {
            var dragon = Npc<MidGjalpinulva>(1_000, 1_000, 0);
            var raider = Npc<Raider>(1_500, 1_000, 0);
            EncounterSignals.Raise(dragon, EncounterSignals.Glare, raider, 1_000, true, 6_000, 0, 0);
            EncounterSignals.Raise(dragon, EncounterSignals.Breath, null, 2_000, false, 3_000, 0, 0);
            Assert.That(EncounterSignals.Note(dragon, EncounterSignals.Berserk, 0), Is.True);
            Assert.That(LoreBossWatch.PendingMechanics.Select(p => p.Triggers[0]), Is.EqualTo(new[] { "glare", "breath", "berserk" }));
            Assert.That(EncounterSignals.Active(dragon, 0).Select(s => s.Mechanic), Is.EquivalentTo(new[] { "glare", "breath" }),
                "a lore-only moment is no signal for the raid");
        }

        [Test]
        public void AMomentIsSaidAtMostOnceInFiveSecondsPerBoss()
        {
            var kula = Npc<TestBoss>(1_000, 1_000, 0);
            var tuscar = Npc<TestBoss>(1_200, 1_000, 0);
            Assert.That(EncounterSignals.Note(kula, EncounterSignals.TwinHeal, 1_000), Is.True);
            Assert.That(EncounterSignals.Note(kula, EncounterSignals.TwinHeal, 3_000), Is.False, "every hit heals the twin: one moment");
            Assert.That(EncounterSignals.Note(tuscar, EncounterSignals.TwinHeal, 3_000), Is.True, "the other twin is its own");
            Assert.That(EncounterSignals.Note(kula, EncounterSignals.TwinHeal, 6_000), Is.True);
            LoreBossWatch.StopListeningToMechanics();
            Assert.That(EncounterSignals.Note(kula, EncounterSignals.Scream, 20_000), Is.False, "nobody listening: nothing to say");
        }

        // ---- the real lore files --------------------------------------------------------------------------------------

        [Test]
        public void SuttungsBerserkSaysHisFilesBerserkLines()
        {
            LoreBible.Boss lore = Lore(nameof(Suttung));
            Assume.That(lore, Is.Not.Null, "elder-icelord-suttung.json");
            var suttung = Npc<Suttung>(1_000, 1_000, 0);
            long now = GameLoop.GameLoopTime;
            EncounterSignals.Note(suttung, EncounterSignals.Berserk, now);
            var said = LoreBossWatch.SayMechanics(now);
            Assert.That(said.Single().Trigger, Is.EqualTo("berserk"));
            Assert.That(_yells.Single().Text, Is.AnyOf(Texts(lore.PhaseYells, "berserk")));
            Assert.That(_lines.Single().Text, Is.AnyOf(Texts(lore.AtmospherePhases, "berserk")));
            Assert.That(LoreBossWatch.PendingMechanics, Is.Empty);
        }

        [Test]
        public void KrackenschteinsThrowSaysHisTeleportLinesOnceForTheWholeThrow()
        {
            LoreBible.Boss lore = Lore(nameof(Krackenschtein));
            Assume.That(lore, Is.Not.Null, "krackenschtein.json");
            var boss = Npc<Krackenschtein>(1_000, 1_000, 0);
            long now = GameLoop.GameLoopTime;
            for (int i = 0; i < 4; i++)   // his throw hurls four raiders at once: four events
                LoreBossWatch.HearDisplaced(Moved(boss, Npc<Raider>(1_100 + i, 1_000, 0), DisplacementKind.Throw, now));
            var said = LoreBossWatch.SayMechanics(now);
            Assert.That(said.Select(s => s.Trigger).Distinct(), Is.EqualTo(new[] { "teleport" }), "his file says teleport for the throw");
            string[] expected = Texts(lore.PhaseYells, "teleport").Concat(Texts(lore.AtmospherePhases, "teleport")).ToArray();
            Assert.That(_yells.Count + _lines.Count, Is.EqualTo(Math.Min(1, Texts(lore.PhaseYells, "teleport").Length) +
                Math.Min(1, Texts(lore.AtmospherePhases, "teleport").Length)), "one yell and one line, not four");
            Assert.That(_yells.Concat(_lines).Select(x => x.Text), Is.All.AnyOf(expected));
        }

        [Test]
        public void GjalpinulvasGlareAndBreathSayHerLinesAndAStaleEventIsDropped()
        {
            LoreBible.Boss lore = Lore(nameof(MidGjalpinulva));
            Assume.That(lore, Is.Not.Null, "gjalpinulva.json");
            var dragon = Npc<MidGjalpinulva>(1_000, 1_000, 0);
            var raider = Npc<Raider>(1_500, 1_000, 0);
            long now = GameLoop.GameLoopTime;
            EncounterSignals.Raise(dragon, EncounterSignals.Glare, raider, 1_000, true, 6_000, 0, now);
            Assert.That(LoreBossWatch.SayMechanics(now).Single().Trigger, Is.EqualTo("glare"));
            EncounterSignals.Raise(dragon, EncounterSignals.Breath, null, 2_000, false, 3_000, 0, now);
            Assert.That(LoreBossWatch.SayMechanics(now + LoreBossWatch.MechanicStaleMs + 1), Is.Empty, "said late is not said");
            EncounterSignals.Raise(dragon, EncounterSignals.Breath, null, 2_000, false, 3_000, 0, now + 20_000);
            Assert.That(LoreBossWatch.SayMechanics(now + 20_000).Single().Trigger, Is.EqualTo("breath"));
            Assert.That(_lines.Select(l => l.Text), Is.All.AnyOf(Texts(lore.AtmospherePhases, "glare").Concat(Texts(lore.AtmospherePhases, "breath")).ToArray()));
            Assert.That(_yells.Select(l => l.Text), Is.All.AnyOf(Texts(lore.PhaseYells, "glare").Concat(Texts(lore.PhaseYells, "breath")).ToArray()));
        }

        [Test]
        public void AMechanicFromAnAddBelongsToTheNearestWatchedBoss()
        {
            LoreBible.Boss lore = Lore(nameof(Krackenschtein));
            Assume.That(lore, Is.Not.Null, "krackenschtein.json");
            var boss = Npc<Krackenschtein>(1_000, 1_000, 0);
            var add = Npc<EasmarachLikeOrb>(3_000, 1_000, 0);
            var far = Npc<EasmarachLikeOrb>(20_000, 1_000, 0);
            Assert.That(LoreBossWatch.OwnerOf(add), Is.EqualTo(((GameNPC)null, (LoreBible.Boss)null)), "no fight watched yet");
            LoreBossWatch.SayMechanic(boss, lore, ["health<=1"], 0);   // the boss's fight is watched now
            Assert.That(LoreBossWatch.OwnerOf(add).Npc, Is.SameAs(boss));
            Assert.That(LoreBossWatch.OwnerOf(far).Npc, Is.Null, $"farther than {LoreBossWatch.MechanicOwnerRadius}");
            boss.Dead = true;
            Assert.That(LoreBossWatch.OwnerOf(add).Npc, Is.Null, "a dead boss owns nothing");
        }
    }
}
