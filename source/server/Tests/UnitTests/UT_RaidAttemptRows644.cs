using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>
    /// #644 part 1: the raid attempt rows RAID_ATTEMPT, RAID_ATTEMPT_END and RAID_DEATH. Their formatters are pure, the attempt
    /// id counts per event, the died/left split tells real deaths from raiders who left, RAID_DEATH has no cap during a live
    /// attempt (owner decision a) and no row carries a name. Tests only: not yet seen in a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidAttemptRows644 : DutyLeaseFixture
    {
        private readonly List<string> _rows = [];
        private long _oldTime;

        [SetUp]
        public void Capture()
        {
            _oldTime = GameLoop.GameLoopTime;
            LivingRowsTestKit.SetTime(5_000_000);
            RvrRecordBudget.Reset();
            RaidAttemptRows.ResetForTests();
            _rows.Clear();
            LivingRows.TestSink = row => { lock (_rows) _rows.Add(row); };
        }

        [TearDown]
        public void Release()
        {
            LivingRows.TestSink = null;
            RaidAttemptRows.ResetForTests();
            RvrRecordBudget.Reset();
            LivingRowsTestKit.SetTime(_oldTime);
        }

        private List<string> Rows(string tag)
        {
            lock (_rows)
                return _rows.Where(row => row.StartsWith(tag + " ", StringComparison.Ordinal)).ToList();
        }

        private static string F(string row, string name) => LivingRowsTestKit.Field(row, name);

        /// <summary>A real player standing alive in region 1 (DutyLeaseTestKit's player has no life).</summary>
        private sealed class LivePlayer : GamePlayer
        {
            private LivePlayer() : base(null, null) { }
            public override bool IsAlive => true;
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
        }

        private static GamePlayer RealPlayer() => (LivePlayer)RuntimeHelpers.GetUninitializedObject(typeof(LivePlayer));

        // ---- the pure formatters -----------------------------------------------------------------------------------

        [Test]
        public void TheAttemptRowHasItsFieldsInOrderAndTheRecoveryFieldsOnlyAfterAFailedAttempt()
        {
            string first = RaidAttemptRows.FormatAttempt(new RaidAttemptRows.AttemptFacts("dungeon-albion-krondon.1", "dungeon-albion-krondon",
                "dungeon", 61, "Orylle", 1, 96, 64, 0, "12:30:8:46", "Cleric:20,Armsman:12", 1, "abc1234", GatherS: 312));
            Assert.That(first, Is.EqualTo("RAID_ATTEMPT attempt=dungeon-albion-krondon.1 event=dungeon-albion-krondon kind=dungeon region=61 " +
                "boss=\"Orylle\" n=1 size=96 period=64 sizePct=150 sf=0 roles=12:30:8:46 classes=Cleric:20,Armsman:12 players=1 build=abc1234 " +
                "gatherS=312"));
            string retry = RaidAttemptRows.FormatAttempt(new RaidAttemptRows.AttemptFacts("e.2", "e", "dragon", 1, "Golestandt", 2, 120, 0,
                24, "1:2:3:4", "-", 0, "local", "e.1", 431, 300));
            Assert.That(LivingRowsTestKit.Keys(retry), Is.EqualTo(new[]
                { "attempt", "event", "kind", "region", "boss", "n", "size", "period", "sizePct", "sf", "roles", "classes", "players", "build",
                  "prev", "wipeToPullS", "gatherS" }));
            Assert.Multiple(() =>
            {
                Assert.That(F(retry, "period"), Is.EqualTo("-"), "no period size: unknown, not 0");
                Assert.That(F(retry, "sizePct"), Is.EqualTo("-"));
                Assert.That(F(retry, "prev"), Is.EqualTo("e.1"));
                Assert.That(F(retry, "wipeToPullS"), Is.EqualTo("431"));
                Assert.That(F(retry, "sf"), Is.EqualTo("24"));
            });
        }

        [Test]
        public void TheEndRowHasItsFieldsAndUnknownCountsAreADash()
        {
            string end = RaidAttemptRows.FormatEnd(new RaidAttemptRows.EndFacts("e.3", "wipe", 412, 96, 70, 61, 9, 88, 100, 31));
            Assert.That(end, Is.EqualTo("RAID_ATTEMPT_END attempt=e.3 outcome=wipe secs=412 engaged=96 fallen=70 died=61 left=9 deaths=88 " +
                "health=100 lowest=31"));
            string stopped = RaidAttemptRows.FormatEnd(new RaidAttemptRows.EndFacts("e.4", "stopped", 5, 40, -1, -1, -1, 0, -1, -1));
            Assert.Multiple(() =>
            {
                Assert.That(F(stopped, "fallen"), Is.EqualTo("-"));
                Assert.That(F(stopped, "left"), Is.EqualTo("-"));
                Assert.That(F(stopped, "health"), Is.EqualTo("-"));
                Assert.That(F(stopped, "deaths"), Is.EqualTo("0"));
            });
        }

        [Test]
        public void TheDeathRowHasItsFieldsAndNoName()
        {
            string death = RaidAttemptRows.FormatDeath(new RaidAttemptRows.DeathFacts("e.1", 73, 14, "player", true, "Cleric", "healer", 50,
                "boss", "Icelord Kvasir", "aoe", 4321, "breath", 812, 3, 2));
            Assert.That(death, Is.EqualTo("RAID_DEATH attempt=e.1 t=73 idx=14 kind=player player=1 class=Cleric role=healer level=50 " +
                "killerKind=boss killer=\"Icelord Kvasir\" source=aoe spellId=4321 mechanic=breath dist=812 healersIn=3 attackers=2"));
            Assert.That(LivingRowsTestKit.Keys(death), Does.Not.Contain("name").And.Not.Contain("bot").And.Not.Contain("account"));
            string nobody = RaidAttemptRows.FormatDeath(new RaidAttemptRows.DeathFacts("e.1", 1, 2, "gamebot", false, null, "-", 50, "none", null,
                "other", 0, null, -1, 0, 0));
            Assert.Multiple(() =>
            {
                Assert.That(F(nobody, "player"), Is.EqualTo("0"));
                Assert.That(F(nobody, "class"), Is.EqualTo("-"));
                Assert.That(F(nobody, "killer"), Is.EqualTo("-"));
                Assert.That(F(nobody, "mechanic"), Is.EqualTo("-"));
                Assert.That(F(nobody, "dist"), Is.EqualTo("-"));
            });
        }

        [Test]
        public void AFieldNeverBreaksTheKeyValueLayout()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidAttemptRows.Token(" two words "), Is.EqualTo("two_words"));
                Assert.That(RaidAttemptRows.Token("a\"b"), Is.EqualTo("a'b"));
                Assert.That(RaidAttemptRows.Token(""), Is.EqualTo("-"));
                Assert.That(RaidAttemptRows.Quoted("Lord \"X\""), Is.EqualTo("Lord 'X'"));
            });
        }

        // ---- the attempt id --------------------------------------------------------------------------------------------

        [Test]
        public void TheAttemptIdCountsPerEvent()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidAttemptRows.NextId("dungeon-albion-krondon"), Is.EqualTo("dungeon-albion-krondon.1"));
                Assert.That(RaidAttemptRows.NextId("dungeon-albion-krondon"), Is.EqualTo("dungeon-albion-krondon.2"));
                Assert.That(RaidAttemptRows.NextId("dragon-albion"), Is.EqualTo("dragon-albion.1"), "each event counts on its own");
                Assert.That(RaidAttemptRows.NextId("dungeon-albion-krondon"), Is.EqualTo("dungeon-albion-krondon.3"));
                Assert.That(RaidAttemptRows.NextId(null), Is.EqualTo("-.1"));
            });
        }

        [Test]
        public void TwoTrackersOfOneEventShareItsSequence()
        {
            // A raid's route gate (another dungeon's boss on the walk in) and its own route write ids of the same event.
            var gate = new RaidAttemptTracker("epic-albion", "dungeon", 0);
            var main = new RaidAttemptTracker("epic-albion", "dungeon", 100);
            Assert.That(gate.Begin(null, 277, [], [], 0, -1).Id, Is.EqualTo("epic-albion.1"));
            gate.End("killed", 0, 0, 0, 0, 0, 0, 1);
            Assert.That(main.Begin(null, 248, [], [], 2, -1).Id, Is.EqualTo("epic-albion.2"));
            Assert.That(gate.CurrentId, Is.EqualTo("epic-albion.1"), "after its end a tracker's rows carry its last id");
            Assert.That(new RaidAttemptTracker("x", "dungeon", 0).CurrentId, Is.EqualTo("-"));
        }

        // ---- the died/left split ---------------------------------------------------------------------------------------

        [Test]
        public void FallenIsSplitIntoDiedAndLeft()
        {
            // a: died (and fallen), b: left without dying (fallen), c: died and rezzed (not fallen now), d: stood throughout.
            string[] engaged = ["a", "b", "c", "d"];
            var fallen = new HashSet<string> { "a", "b" };
            var died = new HashSet<string> { "a", "c", "x" };   // x: not in the attempt
            var (d, l) = RaidAttemptRows.SplitFallen(engaged, fallen.Contains, died.Contains);
            Assert.Multiple(() =>
            {
                Assert.That(d, Is.EqualTo(2), "a and c died in it; x was not in it");
                Assert.That(l, Is.EqualTo(1), "b fell (left the region) without dying");
                Assert.That(RaidAttemptRows.SplitFallen<string>(null, null, null), Is.EqualTo((0, 0)));
            });
        }

        // ---- outcomes, roles, sources --------------------------------------------------------------------------------

        [TestCase(true, false, false, "left combat", "wipe")]
        [TestCase(false, true, false, "reset to full", "reset")]
        [TestCase(false, false, true, "left combat", "skirmish")]
        [TestCase(false, false, false, "killed; it came back since", "killed")]
        [TestCase(false, false, false, "reset to full", "reset")]
        [TestCase(false, false, false, "left combat", "broke-off")]
        [TestCase(false, false, false, "never entered combat", "broke-off")]
        [TestCase(false, false, false, "no damage for five minutes", "broke-off")]
        public void TheDungeonOutcomeComesFromEndAttemptsDecision(bool wipe, bool freeReset, bool skirmish, string how, string outcome) =>
            Assert.That(RaidAttemptRows.DungeonOutcome(wipe, freeReset, skirmish, how), Is.EqualTo(outcome));

        [TestCase("Route blocked: no remaining encounter was reachable", false, "route-blocked")]
        [TestCase("Stalled: no named or boss kill", false, "stalled")]
        [TestCase("The raid's four-hour window ran out.", false, "timeout")]
        [TestCase("Stopped from the launcher.", false, "stopped")]
        [TestCase(null, false, "stopped")]
        [TestCase("The dragon encounter has ended; checking actual respawn", true, "killed")]
        public void AnAttemptOpenAtTheRaidsEndTakesTheRaidsOutcome(string reason, bool bossDead, string outcome) =>
            Assert.That(RaidAttemptRows.RaidEndOutcome(reason, bossDead), Is.EqualTo(outcome));

        [Test]
        public void TheDragonOutcomeIsItsVerdict()
        {
            Assert.That(RaidAttemptRows.DragonOutcome(true), Is.EqualTo("wipe"));
            Assert.That(RaidAttemptRows.DragonOutcome(false), Is.EqualTo("broke-off"));
        }

        [TestCase(eCharacterClass.Armsman, "tank")]
        [TestCase(eCharacterClass.Paladin, "tank")]
        [TestCase(eCharacterClass.Cleric, "healer")]
        [TestCase(eCharacterClass.Friar, "healer")]
        [TestCase(eCharacterClass.Bard, "healer")]
        [TestCase(eCharacterClass.Minstrel, "buffer")]
        [TestCase(eCharacterClass.Skald, "buffer")]
        [TestCase(eCharacterClass.Wizard, "attacker")]
        [TestCase(eCharacterClass.Unknown, "-")]
        public void EachClassHasOneRole(eCharacterClass characterClass, string role) =>
            Assert.That(RaidAttemptRows.Role(characterClass), Is.EqualTo(role));

        [Test]
        public void RolesAndClassesAreCompactCounts()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidAttemptRows.RoleCounts(["tank", "healer", "healer", "attacker", "-", "buffer", "attacker", "attacker"]),
                    Is.EqualTo("1:2:1:3"));
                Assert.That(RaidAttemptRows.ClassCounts(["Wizard", "Cleric", "Cleric", "-", null, "Armsman", "Wizard", "Cleric"]),
                    Is.EqualTo("Cleric:3,Wizard:2,Armsman:1"));
                Assert.That(RaidAttemptRows.ClassCounts([]), Is.EqualTo("-"));
                Assert.That(RaidAttemptRows.SizePercent(96, 64), Is.EqualTo(150));
                Assert.That(RaidAttemptRows.SizePercent(10, 0), Is.EqualTo(-1));
                Assert.That(RaidAttemptRows.Build("abc1234def56"), Is.EqualTo("abc1234"));
                Assert.That(RaidAttemptRows.Build("local"), Is.EqualTo("local"));
                Assert.That(RaidAttemptRows.Build(null), Is.EqualTo("local"));
            });
        }

        [Test]
        public void TheDeathsSourceIsItsRecentLastHit()
        {
            const long now = 100_000;
            RaidAttemptRows.LastHit Hit(AttackData.eAttackType type, eSpellType spell = eSpellType.None, int radius = 0, bool fall = false,
                long ago = 100) => new(now - ago, fall, type, 77, spell, radius);
            Assert.Multiple(() =>
            {
                Assert.That(RaidAttemptRows.Source(null, now), Is.EqualTo("other"));
                Assert.That(RaidAttemptRows.Source(Hit(AttackData.eAttackType.MeleeTwoHand), now), Is.EqualTo("melee"));
                Assert.That(RaidAttemptRows.Source(Hit(AttackData.eAttackType.Spell, eSpellType.DirectDamage), now), Is.EqualTo("spell"));
                Assert.That(RaidAttemptRows.Source(Hit(AttackData.eAttackType.Spell, eSpellType.DirectDamage, 350), now), Is.EqualTo("aoe"));
                Assert.That(RaidAttemptRows.Source(Hit(AttackData.eAttackType.Spell, eSpellType.DamageOverTime, 350), now), Is.EqualTo("dot"));
                Assert.That(RaidAttemptRows.Source(Hit(AttackData.eAttackType.Unknown, fall: true), now), Is.EqualTo("fall"));
                Assert.That(RaidAttemptRows.Source(Hit(AttackData.eAttackType.Ranged), now), Is.EqualTo("other"));
                Assert.That(RaidAttemptRows.Source(Hit(AttackData.eAttackType.MeleeOneHand, ago: RaidAttemptRows.LastHitMilliseconds + 1), now),
                    Is.EqualTo("other"), "a hit long before the death is not its source");
            });
        }

        [Test]
        public void TheKillersKind()
        {
            object victim = new(), boss = new(), add = new();
            Assert.Multiple(() =>
            {
                Assert.That(RaidAttemptRows.KillerKind(null, victim, boss, false, false), Is.EqualTo("none"));
                Assert.That(RaidAttemptRows.KillerKind(null, victim, boss, true, false), Is.EqualTo("env"));
                Assert.That(RaidAttemptRows.KillerKind(victim, victim, boss, false, false), Is.EqualTo("env"));
                Assert.That(RaidAttemptRows.KillerKind(boss, victim, boss, false, false), Is.EqualTo("boss"));
                Assert.That(RaidAttemptRows.KillerKind(add, victim, boss, false, true), Is.EqualTo("pet"));
                Assert.That(RaidAttemptRows.KillerKind(add, victim, boss, false, false), Is.EqualTo("add"));
                Assert.That(RaidAttemptRows.KillerKind(Gamebot(), victim, boss, false, false), Is.EqualTo("raider"));
            });
        }

        // ---- the tracker and the death hook ----------------------------------------------------------------------------

        [Test]
        public void APullWritesTheAttemptRowWithTheRaidInTheRegion()
        {
            Bot[] raid = Enumerable.Range(0, 10).Select(_ => Gamebot()).ToArray();
            raid[9].Region = 2;                     // walking back elsewhere: not in the size
            raid[8].Alive = false;                  // dead: not in the size
            Bot sf = SpecialForce();
            GamePlayer player = RealPlayer();           // the player, a raid guest in the region (RealPlayer stands in region 1)
            var tracker = new RaidAttemptTracker("dungeon-albion-krondon", "dungeon", 4, () => [player]);
            RaidAttempt attempt = tracker.Begin(null, 1, raid.Take(5), raid.Append(sf), 0, 90_000);
            string row = Rows(RaidAttemptRows.AttemptMarker).Single();
            Assert.Multiple(() =>
            {
                Assert.That(F(row, "attempt"), Is.EqualTo("dungeon-albion-krondon.1"));
                Assert.That(F(row, "size"), Is.EqualTo("10"), "eight living gamebots in the region, the Special Force and the player");
                Assert.That(F(row, "sizePct"), Is.EqualTo("250"));
                Assert.That(F(row, "sf"), Is.EqualTo("1"));
                Assert.That(F(row, "players"), Is.EqualTo("1"));
                Assert.That(F(row, "n"), Is.EqualTo("1"));
                Assert.That(F(row, "gatherS"), Is.EqualTo("90"));
                Assert.That(F(row, "prev"), Is.Null, "the first attempt has no recovery fields");
                Assert.That(attempt.Members, Is.EqualTo(6), "the five sent in and the player");
                Assert.That(attempt.IsMember(player), Is.True);
            });
        }

        [Test]
        public void EveryRaiderDeathInALiveAttemptIsWrittenWithNoCap()
        {
            // Owner decision a (#644): no per-minute cap during a live attempt. 300 deaths in the same game second.
            Bot[] raid = Enumerable.Range(0, 299).Select(_ => Gamebot()).ToArray();
            Bot companion = Companion();
            GamePlayer player = RealPlayer();
            var tracker = new RaidAttemptTracker("dungeon-hibernia-galladoria", "dungeon", 100, () => [player]);
            tracker.Begin(null, 1, raid.Append(companion), raid, 5_000_000, -1);
            LivingRowsTestKit.SetTime(5_030_000);
            foreach (Bot bot in raid.Append(companion)) RaidAttemptRows.NoteDeath(bot, null);
            RaidAttemptRows.NoteDeath(player, null);
            List<string> deaths = Rows(RaidAttemptRows.DeathMarker);
            Assert.Multiple(() =>
            {
                Assert.That(deaths, Has.Count.EqualTo(301), "every death, none dropped by a budget");
                Assert.That(deaths.Select(d => F(d, "idx")).Distinct().Count(), Is.EqualTo(301), "each raider has its own index");
                Assert.That(deaths.Count(d => F(d, "player") == "1"), Is.EqualTo(1));
                Assert.That(F(deaths.Single(d => F(d, "player") == "1"), "kind"), Is.EqualTo("player"));
                Assert.That(F(deaths[^2], "kind"), Is.EqualTo("companion"));
                Assert.That(F(deaths[0], "kind"), Is.EqualTo("gamebot"));
                Assert.That(F(deaths[0], "attempt"), Is.EqualTo("dungeon-hibernia-galladoria.1"));
                Assert.That(F(deaths[0], "t"), Is.EqualTo("30"), "seconds into the pull");
                Assert.That(F(deaths[0], "killerKind"), Is.EqualTo("none"));
                Assert.That(F(deaths[0], "source"), Is.EqualTo("other"));
            });
        }

        [Test]
        public void AFallIsTheDeathsSourceAndOnlyRaidersInALiveAttemptAreWritten()
        {
            Bot raider = Gamebot(), stranger = Gamebot();
            var tracker = new RaidAttemptTracker("e", "dragon", 0);
            RaidAttemptRows.NoteDeath(raider, null);
            Assert.That(Rows(RaidAttemptRows.DeathMarker), Is.Empty, "no attempt is live");
            tracker.Begin(null, 1, [raider], [raider], 0, -1);
            RaidAttemptRows.NoteFall(raider);
            RaidAttemptRows.NoteDeath(stranger, null);
            RaidAttemptRows.NoteDeath(raider, null);
            string death = Rows(RaidAttemptRows.DeathMarker).Single();
            Assert.Multiple(() =>
            {
                Assert.That(F(death, "source"), Is.EqualTo("fall"));
                Assert.That(F(death, "killerKind"), Is.EqualTo("env"));
            });
            tracker.End("wipe", 1, 1, 1, 0, 100, 40, 1_000);
            Assert.That(RaidAttemptRows.AnyLive, Is.False, "an ended attempt leaves the death hook");
            RaidAttemptRows.NoteDeath(raider, null);
            Assert.That(Rows(RaidAttemptRows.DeathMarker), Has.Count.EqualTo(1), "after the end, no more deaths are written for it");
        }

        [Test]
        public void TheEndRowCountsTheDeathsAndTheNextPullAfterAWipeCarriesTheRecoveryFields()
        {
            Bot a = Gamebot(), b = Gamebot();
            var tracker = new RaidAttemptTracker("dragon-albion", "dragon", 120);
            tracker.Begin(null, 1, [a, b], [a, b], 0, -1);
            RaidAttemptRows.NoteDeath(a, null);
            RaidAttemptRows.NoteDeath(a, null);  // died, rezzed and died again: two deaths, one raider
            Assert.That(tracker.Current.HasDied(a), Is.True);
            Assert.That(tracker.Current.HasDied(b), Is.False);
            string id = tracker.End("wipe", 2, 2, 1, 1, 100, 55, 300_000);
            string end = Rows(RaidAttemptRows.EndMarker).Single();
            Assert.Multiple(() =>
            {
                Assert.That(id, Is.EqualTo("dragon-albion.1"));
                Assert.That(F(end, "secs"), Is.EqualTo("300"));
                Assert.That(F(end, "deaths"), Is.EqualTo("2"));
                Assert.That(F(end, "died"), Is.EqualTo("1"));
                Assert.That(F(end, "left"), Is.EqualTo("1"));
            });
            tracker.Begin(null, 1, [a, b], [a, b], 900_000, 420_000);
            string retry = Rows(RaidAttemptRows.AttemptMarker)[^1];
            Assert.Multiple(() =>
            {
                Assert.That(F(retry, "attempt"), Is.EqualTo("dragon-albion.2"));
                Assert.That(F(retry, "n"), Is.EqualTo("2"), "the second pull of the same boss");
                Assert.That(F(retry, "prev"), Is.EqualTo("dragon-albion.1"));
                Assert.That(F(retry, "wipeToPullS"), Is.EqualTo("600"));
                Assert.That(F(retry, "gatherS"), Is.EqualTo("420"));
            });
            tracker.End("killed", 2, 0, 0, 0, 0, 0, 1_000_000);
            tracker.Begin(null, 1, [a], [a], 1_100_000, -1);
            Assert.That(F(Rows(RaidAttemptRows.AttemptMarker)[^1], "prev"), Is.Null, "no recovery fields after a kill");
        }

        [Test]
        public void APullWhileAnAttemptIsOpenEndsItAsStopped()
        {
            Bot a = Gamebot();
            var tracker = new RaidAttemptTracker("e", "dungeon", 0);
            tracker.Begin(null, 1, [a], [a], 0, -1);
            tracker.Begin(null, 1, [a], [a], 10_000, -1);
            Assert.That(F(Rows(RaidAttemptRows.EndMarker).Single(), "outcome"), Is.EqualTo("stopped"));
            Assert.That(tracker.Current.Id, Is.EqualTo("e.2"));
        }

        [Test]
        public void NoNewRowCarriesAName()
        {
            Bot a = Gamebot();
            GamePlayer player = RealPlayer();
            var tracker = new RaidAttemptTracker("e", "dungeon", 10, () => [player]);
            tracker.Begin(null, 1, [a], [a], 0, -1);
            RaidAttemptRows.NoteDeath(a, Gamebot());
            RaidAttemptRows.NoteDeath(player, null);
            tracker.End("wipe", 2, 2, 2, 0, 100, 50, 1_000);
            List<string> rows;
            lock (_rows) rows = _rows.ToList();
            Assert.That(rows, Has.Count.EqualTo(4));
            foreach (string row in rows)
            {
                Assert.That(LivingRowsTestKit.Keys(row), Does.Not.Contain("name").And.Not.Contain("bot").And.Not.Contain("account"), row);
                if (row.StartsWith(RaidAttemptRows.DeathMarker + " ", StringComparison.Ordinal))
                    Assert.That(F(row, "killer"), Is.EqualTo("-"), "a raider killer is no monster: its name is not written");
            }
        }

        // ---- the dragon fight's verdicts ----------------------------------------------------------------------------------

        [Test]
        public void TheDragonFightTellsItsAttemptsAndTheirVerdicts()
        {
            var fight = new RealmRaidDragonFight("Golestandt");
            var idle = new RealmRaidDragonFight.Dragon(false, true, false, 100, 100_000);
            long[] roster = Enumerable.Range(1, 20).Select(i => (long)i).ToArray();
            fight.Engage(roster, idle, 0);
            Assert.That((fight.AttemptsBegun, fight.AttemptsEnded, fight.AttemptOpen), Is.EqualTo((1, 0, true)));
            Assert.That(fight.EngagedIds, Has.Count.EqualTo(20));
            // half fell at some pulse, then the dragon is out of combat after the approach: a wipe
            fight.Advance(roster.Select(id => new RealmRaidDragonFight.Raider(id, id > 10, false, false)).ToList(),
                new RealmRaidDragonFight.Dragon(true, true, false, 60, 60_000), 60_000);
            fight.Advance(roster.Select(id => new RealmRaidDragonFight.Raider(id, true, false, false)).ToList(), idle, 4 * 60_000);
            Assert.That(fight.AttemptsEnded, Is.EqualTo(1));
            RealmRaidDragonFight.AttemptEnd end = fight.LastEnd!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(end.Wipe, Is.True);
                Assert.That(end.Engaged, Has.Length.EqualTo(20));
                Assert.That(end.Fallen, Has.Count.EqualTo(10));
                Assert.That(end.LowestPercent, Is.EqualTo(60));
                Assert.That(fight.AttemptOpen, Is.False);
            });
        }
    }
}
