using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// #644 parts 3-5: RAID_MECHANIC_FIRE (every signal of a live attempt and its aftermath, on by default), the measure-only
    /// signals of the raid bosses that had none (decision d: the fight is unchanged), and RAID_THREAT and RAID_ROLE_TOTALS
    /// behind the diagnostics switch (decision c). Tests only: not yet seen in a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidMechanicRows644 : DutyLeaseFixture
    {
        private readonly List<string> _rows = [];
        private readonly List<EncounterSignal> _measured = [];
        private readonly List<EncounterSignal> _raised = [];
        private long _oldTime;

        [SetUp]
        public void Capture()
        {
            _oldTime = GameLoop.GameLoopTime;
            LivingRowsTestKit.SetTime(5_000_000);
            RvrRecordBudget.Reset();
            RaidAttemptRows.ResetForTests();
            RaidBossMechanics.ResetForTests();
            EncounterSignals.ClearAll();
            RaidDiagnosticRows.ForTests = null;
            _rows.Clear();
            _measured.Clear();
            _raised.Clear();
            LivingRows.TestSink = row => { lock (_rows) _rows.Add(row); };
            EncounterSignals.Measured += OnMeasured;
            EncounterSignals.Raised += OnRaised;
        }

        [TearDown]
        public void Release()
        {
            EncounterSignals.Measured -= OnMeasured;
            EncounterSignals.Raised -= OnRaised;
            LivingRows.TestSink = null;
            RaidDiagnosticRows.ForTests = null;
            RaidAttemptRows.ResetForTests();
            RaidBossMechanics.ResetForTests();
            EncounterSignals.ClearAll();
            RvrRecordBudget.Reset();
            LivingRowsTestKit.SetTime(_oldTime);
        }

        private void OnMeasured(EncounterSignal s) { lock (_measured) _measured.Add(s); }
        private void OnRaised(EncounterSignal s) { lock (_raised) _raised.Add(s); }

        private List<string> Rows(string tag)
        {
            lock (_rows)
                return _rows.Where(row => row.StartsWith(tag + " ", StringComparison.Ordinal)).ToList();
        }

        private static string F(string row, string name) => LivingRowsTestKit.Field(row, name);

        /// <summary>A boss standing alive at 0,0,0 in region 1 with a settable target.</summary>
        private sealed class Boss : GameNPC
        {
            public string TestName = "Legion";
            public bool Alive = true;
            public GameObject Target;
            public override string Name { get => TestName; set { } }
            public override bool IsAlive => Alive;
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
            public override GameObject TargetObject { get => Target; set => Target = value; }
        }

        private static Boss NewBoss(string name = "Legion")
        {
            var boss = (Boss)RuntimeHelpers.GetUninitializedObject(typeof(Boss));
            boss.TestName = name;
            boss.Alive = true;
            return boss;
        }

        private static Bot At(int x)
        {
            Bot bot = Gamebot();
            bot.TestX = x;
            return bot;
        }

        private static Spell TestSpell(int radius, int range, double castSeconds) =>
            new(new DbSpell { SpellID = 99_001, Name = "test", Radius = radius, Range = range, CastTime = castSeconds, Damage = 900,
                Type = eSpellType.DirectDamage.ToString(), Target = eSpellTarget.ENEMY.ToString() }, 60);

        // ---- the pure formatters and helpers ---------------------------------------------------------------------------

        [Test]
        public void TheMechanicRowHasItsFieldsInOrderAndDashesForTheUnknown()
        {
            string row = RaidAttemptRows.FormatMechanic(new RaidAttemptRows.MechanicFacts("darkness-albion.2", "Legion", "soulBlast", 84, 1000,
                22, 14, 12, 5, 8, false, 0, 450, 41_200));
            Assert.That(row, Is.EqualTo("RAID_MECHANIC_FIRE attempt=darkness-albion.2 boss=\"Legion\" mechanic=soulBlast t=84 radius=1000 " +
                "exposedAtCall=22 within=14 hit=12 died=5 avoided=8 called=0 leadMs=0 reactMedianMs=450 damage=41200"));
            Assert.That(LivingRowsTestKit.Keys(row), Is.EqualTo(new[]
                { "attempt", "boss", "mechanic", "t", "radius", "exposedAtCall", "within", "hit", "died", "avoided", "called", "leadMs",
                  "reactMedianMs", "damage" }));
            string adds = RaidAttemptRows.FormatMechanic(new RaidAttemptRows.MechanicFacts("e.1", "Spindler Broodmother", "adds", 30, 0,
                -1, -1, 3, 1, -1, false, 0, -1, 900));
            Assert.Multiple(() =>
            {
                Assert.That(F(adds, "exposedAtCall"), Is.EqualTo("-"), "no area: no exposure");
                Assert.That(F(adds, "within"), Is.EqualTo("-"));
                Assert.That(F(adds, "avoided"), Is.EqualTo("-"));
                Assert.That(F(adds, "reactMedianMs"), Is.EqualTo("-"));
                Assert.That(F(adds, "hit"), Is.EqualTo("3"));
            });
        }

        [Test]
        public void DiedAndAvoidedFromTheExposedTheWithinAndTheHit()
        {
            object a = new(), b = new(), c = new(), d = new(), e = new();
            var dead = new HashSet<object> { a, d };
            var (died, avoided) = RaidAttemptRows.MechanicOutcome<object>([a, b, c, e], [a], [d], o => dead.Contains(o));
            Assert.That((died, avoided), Is.EqualTo((2, 3)), "a and d struck and dead; b, c, e exposed, alive and missed");
            Assert.That(RaidAttemptRows.MedianOf([300L, 100L, 200L, 900L]), Is.EqualTo(200));
            Assert.That(RaidAttemptRows.MedianOf([]), Is.EqualTo(-1));
        }

        [Test]
        public void TheThreatRowAndWhy()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidAttemptRows.ThreatWhy(true, true, true), Is.EqualTo("death"));
                Assert.That(RaidAttemptRows.ThreatWhy(false, true, true), Is.EqualTo("taunt"));
                Assert.That(RaidAttemptRows.ThreatWhy(false, false, true), Is.EqualTo("mechanic"));
                Assert.That(RaidAttemptRows.ThreatWhy(false, false, false), Is.EqualTo("threat"));
            });
            string row = RaidAttemptRows.FormatThreat(new RaidAttemptRows.ThreatFacts("darkness-albion.2", "Legion", 31, "tank", "healer",
                "gamebot", "threat", 12_400));
            Assert.That(row, Is.EqualTo("RAID_THREAT attempt=darkness-albion.2 boss=\"Legion\" t=31 from=tank to=healer toKind=gamebot " +
                "why=threat heldMs=12400"));
        }

        [Test]
        public void RoleTotalsSumEachRoleAndListItsClasses()
        {
            var cleric1 = new RaiderTotals { Healed = 9_000, Overheal = 2_000, DealtAdds = 100, PowerOut = true };
            var cleric2 = new RaiderTotals { Healed = 7_000, Overheal = 500 };
            var arms = new RaiderTotals { DealtBoss = 40_000, DealtAdds = 5_000, Took = 60_000, Cc = 0, Interrupts = 2 };
            var sorc = new RaiderTotals { DealtBoss = 30_000, Cc = 4 };
            RaidAttemptRows.RoleTotalsFacts[] rows = RaidAttemptRows.RoleTotals("e.1",
            [
                new("healer", "Cleric", cleric1, 1), new("healer", "Cleric", cleric2, 0), new("healer", "Friar", null, 2),
                new("tank", "Armsman", arms, 1), new("attacker", "Sorcerer", sorc, 0), new("-", "-", new RaiderTotals { DealtBoss = 5 }, 0),
            ]);
            Assert.That(rows.Select(r => r.Role), Is.EqualTo(new[] { "tank", "healer", "buffer", "attacker" }));
            RaidAttemptRows.RoleTotalsFacts healer = rows[1];
            Assert.Multiple(() =>
            {
                Assert.That((healer.Raiders, healer.Healed, healer.Overheal, healer.Deaths, healer.PowerOuts, healer.DealtAdds),
                    Is.EqualTo((3, 16_000L, 2_500L, 3, 1, 100L)), "a raider with no sums still counts as a raider");
                Assert.That(healer.Classes, Is.EqualTo("Cleric:2/100/16000,Friar:1/0/0"));
                Assert.That(rows[2].Classes, Is.EqualTo("-"));
                Assert.That(rows[2].Raiders, Is.EqualTo(0));
                Assert.That(rows.Sum(r => r.DealtBoss), Is.EqualTo(70_000), "a raider with no class is in no role");
            });
            Assert.That(RaidAttemptRows.FormatRoleTotals(rows[0]), Is.EqualTo("RAID_ROLE_TOTALS attempt=e.1 role=tank raiders=1 " +
                "dealtBoss=40000 dealtAdds=5000 took=60000 healed=0 overheal=0 deaths=1 cc=0 interrupts=2 powerOuts=0 classes=Armsman:1/45000/0"));
        }

        // ---- decision d: measure-only signals change nothing in the fight -----------------------------------------------

        [Test]
        public void AMeasureOnlySignalReachesNoTacticNoCallAndNoLore()
        {
            Boss boss = NewBoss();
            EncounterSignal s = EncounterSignals.Measure(boss, "soulBlast", null, 1000, false, 0, 0, 5_000_000);
            Assert.Multiple(() =>
            {
                Assert.That(_measured, Has.Count.EqualTo(1));
                Assert.That(_raised, Is.Empty, "not raised: no tactic, /group call or lore line hears it");
                Assert.That(EncounterSignals.Active(boss, 5_000_000), Is.Empty, "not live for the raid AI");
                Assert.That(EncounterSignals.AllActive(5_000_000), Is.Empty);
                Assert.That((s.Mechanic, s.Radius, s.Lands, s.Until), Is.EqualTo(("soulBlast", 1000, 5_000_000L, 5_001_500L)));
            });
        }

        /// <summary>The bosses decision d covers, their script and each mechanic's measure call (a Cast wraps the script's own cast).</summary>
        private static readonly (string Boss, string Script, string[] Casts, string[] Spawns)[] Bosses =
        [
            ("Legion", "DarknessFalls/Legion.cs", ["soulBlast"], ["adds"]),
            ("Grand Summoner Govannon", "SummonersHall/GrandSummonerGovannon.cs", ["dot"], ["sacrifice"]),
            ("Green Knight", "OFGreenKnight.cs", ["heat", "heal"], ["trees"]),
            ("Spindler Broodmother", "Galladoria/SpindlerBroodmother.cs", ["mezz"], ["adds"]),
            ("Queen Kula", "TuscarianGlacier/KingTuscarAndQueenKula.cs", ["cold", "frostMezz", "frostRoot"], []),
            ("King Tuscar", "TuscarianGlacier/KingTuscarAndQueenKula.cs", ["hammers", "thunder", "bleed"], []),
            // The rest of #644's list (Account #2): the other raid bosses and the open-world bosses with scripted casts.
            ("Apocalypse", "CaerSidi/ApocalypseAnd4Horsemans .cs", ["plague", "gtaoe", "rainOfFire"], []),
            ("Orylle", "Krondon/OrylleAndOrshom.cs", ["fire"], []),
            ("Dura'ek the Empowered", "AvalonCity/DuraekEmpowered.cs", ["heatProc"], []),
            ("Aidon the Archwizard", "HallOfTheCorrupt/AidonTheArchwizard.cs",
                ["fireShield", "dd", "copyFire", "copyIce", "copyAir", "copyEarth"], []),
            ("Iarnvidiur", "IarnvidiurLair/Iarnvidiur.cs", ["bolt", "dot", "dd", "disease"], []),
            ("Game Epic Aros", "Dodens/GameEpicAros.cs", ["bomb", "bigBomb", "debuff", "summon"], []),
            ("Aros the Spiritmaster", "Dodens/SpiritmasterAros.cs", ["bomb", "debuff", "dd", "stun"], []),
            ("Black Lady", "MarfachCaverns/BlackLady.cs", ["dd"], []),
            ("Myrddraxis", "Fomor/Myrddraxis.cs",
                ["disease", "hasteDebuff", "dot", "dd", "stun", "pbaoe", "dd2", "head2", "head3", "head4", "head5"], []),
            ("Evern", "Evern.cs", ["fairyHeal"], []),
            ("Maldahar the Glimmer Prince", "MaldaharTheGlimmerPrince.cs", ["lifeTap", "pbaoe"], []),
        ];

        private static IEnumerable<TestCaseData> BossCases() => Bosses.Select(b => new TestCaseData(b.Boss, b.Script, b.Casts, b.Spawns).SetName("Boss " + b.Boss));

        private static string Script(string relative)
        {
            DirectoryInfo dir = new(TestContext.CurrentContext.TestDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "GameServer", "scripts", "namedmobs"))) dir = dir.Parent;
            Assert.That(dir, Is.Not.Null, "the source tree");
            return File.ReadAllText(Path.Combine(dir!.FullName, "GameServer", "scripts", "namedmobs", relative));
        }

        [TestCaseSource(nameof(BossCases))]
        public void EachBossMechanicFiresAMeasureOnlySignalAndTheFightIsUnchanged(string bossName, string script, string[] casts, string[] spawns)
        {
            string text = Script(script);
            foreach (string mechanic in casts)
            {
                // The script's own cast is the first argument, unchanged: the same caster, the same spell, the same line, and
                // its result decides nothing but the measurement; the spell named after it is the one cast.
                var calls = Regex.Matches(text, @"RaidBossMechanics\.Cast\(((?:Body\.)?CastSpell\((\w+), SkillBase\.GetSpellLine\(GlobalSpellsLines\.Mob_Spells\)(?:, false)?\)), (Body|this), """ +
                    mechanic + @""", (\w+)\);");
                Assert.That(calls, Is.Not.Empty, $"{bossName}: {mechanic}");
                foreach (Match m in calls)
                    Assert.That(m.Groups[4].Value, Is.EqualTo(m.Groups[2].Value), $"{bossName}: {mechanic} measures the spell it casts");
            }
            foreach (string mechanic in spawns)
                Assert.That(text, Does.Contain($"RaidBossMechanics.Spawn(Body, \"{mechanic}\");"), $"{bossName}: {mechanic}");

            // The measurement itself: a signal for each mechanic, and nothing of the boss, its target or its spell changes.
            Boss boss = NewBoss(bossName);
            Bot target = At(400);
            boss.Target = target;
            Spell spell = TestSpell(500, 1500, 2.5);
            var before = (spell.ID, spell.Radius, spell.Range, spell.CastTime, spell.Damage, spell.RecastDelay);
            foreach (string mechanic in casts)
            {
                RaidBossMechanics.Cast(false, boss, mechanic, spell);
                RaidBossMechanics.CastBegan(boss, spell, target, spell.CastTime);
                Assert.That(_measured.Any(s => s.Mechanic == mechanic), Is.False, "not asked: no signal");
                RaidBossMechanics.Cast(true, boss, mechanic, spell);
                Assert.That(_measured.Any(s => s.Mechanic == mechanic), Is.False, "asked, not begun: no signal yet");
                RaidBossMechanics.CastBegan(boss, spell, target, spell.CastTime);
                EncounterSignal s = _measured.Last();
                Assert.That((s.Mechanic, s.Radius, s.AroundTarget, s.Target, s.Lands - s.At), Is.EqualTo((mechanic, 500, true, (GameLiving)target, 2_500L)));
            }
            foreach (string mechanic in spawns)
            {
                RaidBossMechanics.Spawn(boss, mechanic);
                Assert.That((_measured.Last().Mechanic, _measured.Last().Radius), Is.EqualTo((mechanic, 0)));
            }
            Assert.Multiple(() =>
            {
                Assert.That((spell.ID, spell.Radius, spell.Range, spell.CastTime, spell.Damage, spell.RecastDelay), Is.EqualTo(before), "the spell");
                Assert.That(boss.Target, Is.SameAs(target), "the target");
                Assert.That(_raised, Is.Empty, "no tactic, call or lore line");
                Assert.That(EncounterSignals.Active(boss, LivingRows.Now()), Is.Empty, "nothing live for the raid AI");
                Assert.That(_measured, Has.Count.EqualTo(casts.Length + spawns.Length));
            });
        }

        // ---- RAID_MECHANIC_FIRE end to end --------------------------------------------------------------------------

        [Test]
        public void AFireCountsTheExposedTheSteppedOutTheWithinTheHitAndTheDead()
        {
            Boss boss = NewBoss();
            Bot a = At(100), b = At(300), c = At(2_000);
            var tracker = new RaidAttemptTracker("darkness-albion", "dungeon", 64);
            tracker.Begin(boss, 1, [a, b, c], [a, b, c], 5_000_000, -1);
            LivingRowsTestKit.SetTime(5_010_000);
            EncounterSignals.Measure(boss, "soulBlast", null, 500, false, 2_000, 0, 5_010_000);
            b.TestX = 900;                                     // steps out before the landing
            LivingRowsTestKit.SetTime(5_010_700);
            RaidAttemptRows.PulseLive(5_010_700);
            LivingRowsTestKit.SetTime(5_012_000);
            RaidAttemptRows.PulseLive(5_012_000);              // the landing: a is within
            RaidAttemptRows.NoteHit(a, new AttackData { Attacker = boss, Target = a, AttackType = AttackData.eAttackType.Spell, Damage = 700 });
            RaidAttemptRows.NoteHit(c, new AttackData { Attacker = boss, Target = c, AttackType = AttackData.eAttackType.MeleeOneHand, Damage = 90 });
            a.Alive = false;
            LivingRowsTestKit.SetTime(5_017_000);
            RaidAttemptRows.PulseLive(5_017_000);
            Assert.That(Rows(RaidAttemptRows.MechanicMarker), Is.Empty, "counted until 6 s after the landing");
            LivingRowsTestKit.SetTime(5_018_000);
            RaidAttemptRows.PulseLive(5_018_000);
            string row = Rows(RaidAttemptRows.MechanicMarker).Single();
            Assert.Multiple(() =>
            {
                Assert.That(F(row, "attempt"), Is.EqualTo("darkness-albion.1"));
                Assert.That(F(row, "mechanic"), Is.EqualTo("soulBlast"));
                Assert.That(F(row, "t"), Is.EqualTo("10"));
                Assert.That(F(row, "exposedAtCall"), Is.EqualTo("2"));
                Assert.That(F(row, "within"), Is.EqualTo("1"));
                Assert.That(F(row, "hit"), Is.EqualTo("1"), "the boss's spell hit, not its melee");
                Assert.That(F(row, "damage"), Is.EqualTo("700"));
                Assert.That(F(row, "died"), Is.EqualTo("1"));
                Assert.That(F(row, "avoided"), Is.EqualTo("1"));
                Assert.That(F(row, "called"), Is.EqualTo("0"), "a measure-only signal");
                Assert.That(F(row, "leadMs"), Is.EqualTo("2000"));
                Assert.That(F(row, "reactMedianMs"), Is.EqualTo("700"));
                Assert.That(LivingRowsTestKit.Keys(row), Does.Not.Contain("name").And.Not.Contain("bot").And.Not.Contain("account"));
            });
            tracker.End("wipe", 3, 1, 1, 0, 50, 40, 5_020_000);
        }

        [Test]
        public void ARaisedSignalIsCalledAndAFireStillOpenAtTheEndIsWritten()
        {
            Boss boss = NewBoss("Xanxicar");
            Bot a = At(100);
            var tracker = new RaidAttemptTracker("dungeon-albion-crystal", "dungeon", 120);
            tracker.Begin(boss, 1, [a], [a], 5_000_000, -1);
            EncounterSignals.Raise(boss, EncounterSignals.Stomp, null, 800, false, 5_000, 0, 5_000_000);
            tracker.End("wipe", 1, 1, 1, 0, 30, 30, 5_001_000);
            string row = Rows(RaidAttemptRows.MechanicMarker).Single();
            Assert.That((F(row, "called"), F(row, "exposedAtCall"), F(row, "within"), F(row, "avoided")), Is.EqualTo(("1", "1", "-", "-")),
                "it never landed: within and avoided unknown");
            EncounterSignals.Raise(boss, EncounterSignals.Stomp, null, 800, false, 0, 0, 5_002_000);
            Assert.That(Rows(RaidAttemptRows.MechanicMarker), Has.Count.EqualTo(1), "no attempt live: no row");
        }

        [Test]
        public void MechanicFiresAreBudgetedAt20AMinute()
        {
            Boss boss = NewBoss();
            Bot a = At(100);
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            tracker.Begin(boss, 1, [a], [a], 5_000_000, -1);
            for (int i = 0; i < 25; i++) RaidBossMechanics.Spawn(boss, "adds");
            LivingRowsTestKit.SetTime(5_007_000);
            RaidAttemptRows.PulseLive(5_007_000);
            Assert.That(Rows(RaidAttemptRows.MechanicMarker), Has.Count.EqualTo(20));
            tracker.End("wipe", 1, 1, 0, 0, 50, 50, 5_008_000);
        }

        // ---- decision c: the diagnostics rows ----------------------------------------------------------------------

        [Test]
        public void ThreatAndRoleTotalsAreOffByDefault()
        {
            Assert.That(RaidDiagnosticRows.RAID_DIAGNOSTIC_ROWS, Is.False, "off in player installs");
            Boss boss = NewBoss();
            Bot a = At(100);
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            tracker.Begin(boss, 1, [a], [a], 5_000_000, -1);
            boss.Target = a;
            RaidAttemptRows.PulseLive(5_001_000);
            RaidAttemptRows.NoteHit(boss, new AttackData { Attacker = a, Target = boss, Damage = 500 });
            tracker.End("wipe", 1, 1, 0, 0, 50, 50, 5_008_000);
            Assert.That(Rows(RaidAttemptRows.ThreatMarker).Concat(Rows(RaidAttemptRows.RoleTotalsMarker)), Is.Empty);
        }

        [Test]
        public void WithTheSwitchEachTargetChangeIsOneThreatRow()
        {
            RaidDiagnosticRows.ForTests = true;
            Boss boss = NewBoss();
            Bot tank = At(100), other = At(200);
            var tracker = new RaidAttemptTracker("darkness-albion", "dungeon", 64);
            tracker.Begin(boss, 1, [tank, other], [tank, other], 5_000_000, -1);
            boss.Target = tank;
            RaidAttemptRows.PulseLive(5_002_000);
            RaidAttemptRows.PulseLive(5_003_000);                  // the same target: no row
            LivingRowsTestKit.SetTime(5_003_500);
            RaidAttemptRows.NoteHit(boss, new AttackData { Attacker = other, Target = boss, Damage = 50,
                SpellHandler = (DOL.GS.Spells.ISpellHandler)RuntimeHelpers.GetUninitializedObject(typeof(DOL.GS.Spells.TauntSpellHandler)) });
            boss.Target = other;
            RaidAttemptRows.PulseLive(5_004_000);
            other.Alive = false;
            boss.Target = tank;
            RaidAttemptRows.PulseLive(5_009_000);
            boss.Target = null;
            RaidAttemptRows.PulseLive(5_010_000);
            List<string> rows = Rows(RaidAttemptRows.ThreatMarker);
            Assert.Multiple(() =>
            {
                Assert.That(rows.Select(r => F(r, "why")), Is.EqualTo(new[] { "threat", "taunt", "death", "threat" }));
                Assert.That(F(rows[0], "from"), Is.EqualTo("none"));
                Assert.That(F(rows[0], "heldMs"), Is.EqualTo("2000"), "the boss was untargeted from the pull");
                Assert.That(F(rows[1], "heldMs"), Is.EqualTo("2000"));
                Assert.That(F(rows[1], "toKind"), Is.EqualTo("gamebot"));
                Assert.That(F(rows[2], "heldMs"), Is.EqualTo("5000"));
                Assert.That(F(rows[3], "to"), Is.EqualTo("none"));
                Assert.That(F(rows[0], "attempt"), Is.EqualTo("darkness-albion.1"));
            });
            tracker.End("wipe", 2, 2, 1, 0, 50, 50, 5_011_000);
        }

        [Test]
        public void WithTheSwitchTheAttemptEndWritesFourRoleTotalsAndTheSumsAreKept()
        {
            RaidDiagnosticRows.ForTests = true;
            Boss boss = NewBoss();
            Bot a = At(100);
            Boss add = NewBoss("Legion's Add");
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            RaidAttempt attempt = tracker.Begin(boss, 1, [a], [a], 5_000_000, -1);
            RaidAttemptRows.NoteHit(boss, new AttackData { Attacker = a, Target = boss, Damage = 500, CriticalDamage = 100 });
            RaidAttemptRows.NoteHit(add, new AttackData { Attacker = a, Target = add, Damage = 70 });
            RaidAttemptRows.NoteHit(a, new AttackData { Attacker = boss, Target = a, AttackType = AttackData.eAttackType.MeleeOneHand, Damage = 333 });
            RaidAttemptRows.NoteHeal(a, 1_000, 400);
            RaidAttemptRows.NoteCc(a, add);
            RaidAttemptRows.NoteInterrupt(a, add);
            RaidAttemptRows.NoteInterrupt(add, a);                   // a monster interrupting a raider is not the raider's
            attempt.NotePowerOut(a);
            RaiderTotals t = attempt.TotalsFor(a);
            Assert.That((t.DealtBoss, t.DealtAdds, t.Took, t.Healed, t.Overheal, t.Cc, t.Interrupts, t.PowerOut),
                Is.EqualTo((600L, 70L, 333L, 400L, 600L, 1, 1, true)));
            tracker.End("wipe", 1, 1, 0, 0, 50, 50, 5_008_000);
            List<string> rows = Rows(RaidAttemptRows.RoleTotalsMarker);
            Assert.That(rows.Select(r => F(r, "role")), Is.EqualTo(new[] { "tank", "healer", "buffer", "attacker" }));
            Assert.That(LivingRowsTestKit.Keys(rows[0]), Is.EqualTo(new[]
                { "attempt", "role", "raiders", "dealtBoss", "dealtAdds", "took", "healed", "overheal", "deaths", "cc", "interrupts",
                  "powerOuts", "classes" }));
        }

        [Test]
        public void WithTheSwitchOffTheHitPathKeepsNoSums()
        {
            Boss boss = NewBoss();
            Bot a = At(100);
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            RaidAttempt attempt = tracker.Begin(boss, 1, [a], [a], 5_000_000, -1);
            RaidAttemptRows.NoteHit(boss, new AttackData { Attacker = a, Target = boss, Damage = 500 });
            RaidAttemptRows.NoteHeal(a, 1_000, 400);
            Assert.That(attempt.TotalsFor(a), Is.Null);
            tracker.End("wipe", 1, 1, 0, 0, 50, 50, 5_008_000);
        }
    }
}
