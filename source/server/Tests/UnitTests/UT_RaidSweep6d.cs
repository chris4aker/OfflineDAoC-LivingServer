using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Sweep 6d (owner's PC log, build b1b6846, 2026-11-18, 12:00-13:31): a Galladoria return wave stuck with no help left
    /// during the Earth fight and never released (G-5), the 6-hour sweep cap recorded as "Stopped by player" (L-1), raiders
    /// left on the Earthmenders after the raid ended and killed by the Earth primal (P-1), Krondon's held epics back on the
    /// raiders walking out (P-2), and a required-adds phase that made no progress for 46 minutes with no line saying so.
    /// Bots and monsters stand in a test region; no world tick, combat, spell or pathing runs. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidSweep6d
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 6600;
        private EpicTestServerScope _server;

        public sealed class EarthPrimal : GameEpicBoss { public override bool IsAlive => true; }
        public sealed class NaturalEarthmender : GameNPC { public override bool IsAlive => true; }
        public sealed class Trash : GameNPC { public override bool IsAlive => true; }

        public sealed class Raider : GameBot
        {
            private Raider() : base((OfflineWorldBotRecord)null) { }
            public int Stops;
            public override bool IsAlive => true;
            public override bool IsCasting => false;
            public override void StopAttack() => Stops++;
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = EpicEncounterTactics.GalladoriaRegion, Name = "Sweep 6d test",
                Description = "Sweep 6d test", Mobs = [] });
            EpicFightLedger.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EpicFightLedger.ResetForTests();
            _server.Dispose();
        }

        private T Living<T>(int x, int y, int z) where T : GameNPC
        {
            T npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)?.SetValue(npc, new ArrayList());
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

        // ---- G-5: a wave stuck with its help used up during a fight -----------------------------------------------------

        [Test]
        public void AStuckWaveWithNoHelpLeftIsReleasedDuringAFightToo()
        {
            // Galladoria wave 10 at 11:37:40: helps=2, "no help left", the route in the Earth primal's EARLY_PULL Commit
            // (fighting, not mustering): the body can't come, nobody waits at the way in.
            var help = RealmRaidReturnWave.ChooseHelp(0, false, 2);
            Assert.That(help, Is.EqualTo(RealmRaidReturnWave.Help.None));
            Assert.That(RealmRaidReturnWave.ReleasesStuckWave(help), Is.True,
                "build b1b6846: 45 of 117 raiders stood in the wave at 38492,62016,12372 until the raid ended");
            Assert.That(RealmRaidReturnWave.ReleasesStuckWave(RealmRaidReturnWave.Help.BodyMeets), Is.False,
                "help left: it is helped first");
        }

        // ---- L-1: the sweep cap is its own outcome ---------------------------------------------------------------------

        [Test]
        public void ASweepCapIsItsOwnOutcomeWithNoCooldownAndNoResult()
        {
            string reason = AutonomousRealmRaid.SweepCapReason(360);
            Assert.Multiple(() =>
            {
                Assert.That(reason, Is.EqualTo("Sweep cap: the play-test sweep's 360-minute limit."));
                Assert.That(AutonomousRealmRaid.StopOutcome(reason), Is.EqualTo("Sweep cap"), "not \"Stopped by player\"");
                Assert.That(AutonomousRealmRaid.SetsCooldown(reason), Is.False, "no cooldown, as before");
                Assert.That(AutonomousEpicSpecialForces.IsWin("Sweep cap") || AutonomousEpicSpecialForces.IsLoss("Sweep cap"), Is.False);
                Assert.That(RealmEventRecordStore.RecordOutcome("Sweep cap", 0), Is.EqualTo("Sweep cap"));
                var rules = RealmRaidRecruitmentPolicy.SizingRules.Default;
                Assert.That(RealmRaidRecruitmentPolicy.SizePercent(["Defeated", "Sweep cap", "Sweep cap"], rules),
                    Is.EqualTo(RealmRaidRecruitmentPolicy.SizePercent(["Defeated"], rules)), "no win or loss for the record");
                // The realm hears it is called off, not that it gave up or was beaten.
                Assert.That(RealmEventBanter.RaidOutcome("Galladoria", "Sweep cap"),
                    Is.EqualTo(RealmEventBanter.RaidOutcome("Galladoria", "Stopped by player")));
                // The launcher's own stop is still the player's.
                Assert.That(AutonomousRealmRaid.StopOutcome("Stopped by the player from the launcher."), Is.EqualTo("Stopped by player"));
            });
        }

        // ---- P-1: raiders leave the encounter when the raid ends --------------------------------------------------------

        [Test]
        public void AtTheEndARaiderDropsAnEncounterTargetThatIsNotHittingIt()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.DropsEncounterTargetAtEnd(true, true, false), Is.True);
                Assert.That(AutonomousRealmRaid.DropsEncounterTargetAtEnd(true, true, true), Is.False, "it defends itself");
                Assert.That(AutonomousRealmRaid.DropsEncounterTargetAtEnd(true, false, false), Is.False, "trash: its own fight");
                Assert.That(AutonomousRealmRaid.DropsEncounterTargetAtEnd(false, true, false), Is.False, "outside the dungeon");
            });
        }

        [Test]
        public void RaidersOnThePassiveEarthmendersWalkOutAndOneTheBossHitsFightsBack()
        {
            var earth = Living<EarthPrimal>(39727, 62620, 11684);
            var mender = Living<NaturalEarthmender>(39552, 62929, 11684);
            var trash = Living<Trash>(39000, 62000, 11684);
            Raider onMender = Living<Raider>(39600, 62900, 11684), hitByBoss = Living<Raider>(39700, 62600, 11684),
                onTrash = Living<Raider>(39010, 62010, 11684);
            Assert.That(AutonomousRealmRaid.IsEncounterNpc(earth) && AutonomousRealmRaid.IsEncounterNpc(mender), Is.True);
            Assert.That(AutonomousRealmRaid.IsEncounterNpc(trash), Is.False);
            onMender.TargetObject = mender;      // the Earthmenders are passive: it is not hitting him
            hitByBoss.TargetObject = earth;
            earth.TargetObject = hitByBoss;      // the Earth primal is hitting this one
            onTrash.TargetObject = trash;
            int dropped = AutonomousRealmRaid.DropEncounterTargets([onMender, hitByBoss, onTrash], [EpicEncounterTactics.GalladoriaRegion]);
            Assert.Multiple(() =>
            {
                Assert.That(dropped, Is.EqualTo(1));
                Assert.That(onMender.TargetObject, Is.Null, "build b1b6846: 59 killed by the Earth primal after the end");
                Assert.That(onMender.Stops, Is.EqualTo(1));
                Assert.That(hitByBoss.TargetObject, Is.SameAs(earth), "the boss is hitting it: it keeps defending itself");
                Assert.That(onTrash.TargetObject, Is.SameAs(trash));
            });
            onMender.TargetObject = mender;
            Assert.That(AutonomousRealmRaid.DropEncounterTargets([onMender], [61]), Is.Zero, "not in the raid's dungeon");
        }

        // ---- P-2: the epic respawn hold outlives the raid for a while ---------------------------------------------------

        [Test]
        public void AnEndedRaidsDungeonStaysHeldUpToTenMinutesWhileItsRaidersAreInIt()
        {
            const long ended = 1_000_000;
            const ushort krondon = 61;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.EndedHoldMilliseconds, Is.EqualTo(10 * 60_000));
                // Krondon, won at 13:01:08: Lurfos and Drihten came back from 13:01:47 on the raiders leaving.
                Assert.That(AutonomousRealmRaid.EndedHoldRegions(krondon, null, ended, ended + 2 * 60_000, 80), Is.EqualTo(new[] { krondon }));
                Assert.That(AutonomousRealmRaid.EndedHoldRegions(krondon, null, ended, ended + 11 * 60_000, 80), Is.Empty,
                    "after ten minutes the epics respawn as always");
                Assert.That(AutonomousRealmRaid.EndedHoldRegions(krondon, null, ended, ended + 2 * 60_000, 0), Is.Empty,
                    "nobody of the raid left inside: no reason to wait");
                // A raid whose way in leads through another dungeon holds both, as while it ran.
                Assert.That(AutonomousRealmRaid.EndedHoldRegions(248, 249, ended, ended + 60_000, 3), Is.EqualTo(new ushort[] { 248, 249 }));
                Assert.That(AutonomousRealmRaid.KeepsEndedHold(ended, ended + 10 * 60_000 - 1, 1), Is.True);
                Assert.That(AutonomousRealmRaid.KeepsEndedHold(ended, ended + 10 * 60_000, 1), Is.False);
            });
        }

        // ---- D-1 diagnostics: a required-adds phase with no progress ------------------------------------------------------

        [Test]
        public void ARequiredAddsPhaseThatOutTakesItsHealthWithNoDeathIsLoggedOnceAfterTwentyMinutes()
        {
            // The four Earthmenders: 60,000 each; the raid dealt them about 30,000 a minute (1.44M in 48 minutes), none died.
            var watch = new RealmRaidNoProgress();
            const long health = 240_000;
            int due = 0, dueAt = -1;
            for (int minute = 0; minute <= 48; minute++)
                if (watch.Observe("Corrupt Primal of Earth", 4, health, 30_000, minute * 60_000L)) { due++; dueAt = minute; }
            Assert.That(due, Is.EqualTo(1), "once per phase");
            Assert.That(dueAt, Is.EqualTo(RealmRaidNoProgress.Minutes));
            Assert.That(watch.Dealt, Is.EqualTo(49 * 30_000L));
            string line = RealmRaidNoProgress.Line(191, "Corrupt Primal of Earth", "Magical Earthmender", 4, health, 630_000, 20);
            Assert.That(line, Does.StartWith("REALM_EXPEDITION_NO_PROGRESS region=191 boss=\"Corrupt Primal of Earth\""));
            Assert.That(line, Does.Contain("adds=4 health=240000 dealt=630000 minutes=20"));
        }

        [Test]
        public void AnAddDeathOrTooLittleDamageIsProgress()
        {
            var watch = new RealmRaidNoProgress();
            bool any = false;
            for (int minute = 0; minute <= 34; minute++)
                any |= watch.Observe("Corrupt Primal of Earth", minute < 15 ? 4 : 3, 240_000, 30_000, minute * 60_000L);
            Assert.That(any, Is.False, "an Earthmender died at 15 minutes: the phase starts again there");
            Assert.That(watch.Since, Is.EqualTo(15 * 60_000L));
            var slow = new RealmRaidNoProgress();
            for (int minute = 0; minute <= 40; minute++)
                any |= slow.Observe("Corrupt Primal of Earth", 4, 240_000, 5_000, minute * 60_000L);
            Assert.That(any, Is.False, "205,000 in 41 minutes is less than their health");
            Assert.That(RealmRaidNoProgress.Stalled(20 * 60_000, 240_001, 240_000), Is.True);
            Assert.That(RealmRaidNoProgress.Stalled(20 * 60_000 - 1, 900_000, 240_000), Is.False);
        }

        [Test]
        public void TheLedgerTellsTheMinutesDamageOnTheAddsWithoutFlushingIt()
        {
            EpicFightLedger.Add(191, false, "Natural Earthmender", 1, 6_828);
            EpicFightLedger.Add(191, false, "Guardian Earthmender", 2, 15_378);
            EpicFightLedger.Add(191, false, "Corrupt Primal of Earth", 3, 1_026);
            EpicFightLedger.Add(191, true, "Corrupt Primal of Earth", 3, 17_561);
            EpicFightLedger.Add(160, false, "Natural Earthmender", 4, 999);
            long dealt = EpicFightLedger.DealtTo(191, new[] { "Natural Earthmender", "Guardian Earthmender" }.ToHashSet());
            Assert.That(dealt, Is.EqualTo(6_828 + 15_378));
            Assert.That(EpicFightLedger.Flush(191).Dealt, Does.Contain("Guardian Earthmender:15378"), "still there for the ledger line");
        }
    }
}
