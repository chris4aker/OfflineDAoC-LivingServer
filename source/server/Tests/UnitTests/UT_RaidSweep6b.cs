using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidDungeonRoute;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's PC log, build b1b6846, 2026-11-18 (sweep 6b): Fornfrusenen's retry with no shards (Tuscaran Glacier), Aroon's
    /// three attempts cut off between his Scaiths and Easmarach's stale muster record (Galladoria). Tests only: not yet seen
    /// on the owner's PC.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidSweep6b
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 700;
        private EpicTestServerScope _server;

        public sealed class ScaithBoss : GameEpicBoss
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }
        public sealed class ScaithOne : GameEpicBoss
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }
        public sealed class ScaithTwo : GameEpicBoss
        {
            public bool Dead, Fighting;
            public override bool IsAlive => !Dead;
            public override bool InCombat => Fighting;
        }

        private sealed class ShardsBrain : FornfrusenenBrain
        {
            public bool ShardAlive;
            protected override bool AnyShardAlive() => ShardAlive;
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 63, Name = "Sweep 6b test", Description = "Sweep 6b test", Mobs = [] });
            // As the catalog's galladoria/aroon row: required adds killed in order.
            EpicEncounterTactics.Register(new("ScaithBoss", "test/scaithboss", ["ScaithOne", "ScaithTwo"], 0, null) { AddsInOrder = true });
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterTactics.Unregister("ScaithBoss");
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

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }

        private static long S(int seconds) => seconds * 1_000L;

        // ---- 1. Fornfrusenen: a heal to full with no shard alive re-arms him -------------------------------------------

        [Test]
        public void AHealToFullWithNoShardAliveRearmsTheShards()
        {
            // 09:13:31 TARGET_HEALED from=71 to=100 aggro=5, the last shard dead since 09:10: the 09:15 retry got no shards.
            var brain = new ShardsBrain { FornInCombat = true, ShardAlive = false };
            brain.RearmAfterHeal();
            Assert.That(brain.FornInCombat, Is.False, "re-armed: the next pull with aggro raises fresh shards (SpawnShards)");
        }

        [Test]
        public void AHealWhileAShardLivesLeavesTheShardFightAlone()
        {
            // 09:04:19 TARGET_HEALED from=49 to=100 with the last shard still fought (until 09:09): nothing changes.
            var brain = new ShardsBrain { FornInCombat = true, ShardAlive = true };
            brain.RearmAfterHeal();
            Assert.That(brain.FornInCombat, Is.True, "a live shard is never replaced mid-fight");
            var idle = new ShardsBrain { FornInCombat = false, ShardAlive = false };
            idle.RearmAfterHeal();
            Assert.That(idle.FornInCombat, Is.False, "a boss never pulled stays as he is");
        }

        [Test]
        public void BothHealBranchesRearmAndHeIsNotWeakened()
        {
            string source = File.ReadAllText(Path.Combine(RepoRoot(), "source", "server", "GameServer", "scripts", "namedmobs",
                "TuscarianGlacier", "Fornfrusonen.cs"));
            Match think = Regex.Match(source, @"public override void Think\(\)\s*\{(?<body>.*?)base\.Think\(\);", RegexOptions.Singleline);
            Assert.That(think.Success, Is.True);
            string body = think.Groups["body"].Value;
            Assert.Multiple(() =>
            {
                Assert.That(Regex.IsMatch(body, @"IsOutOfTetherRange\)\s*\{[^}]*Body\.Health = Body\.MaxHealth;[^}]*RearmAfterHeal\(\);", RegexOptions.Singleline),
                    Is.True, "the tether heal re-arms");
                Assert.That(Regex.IsMatch(body, @"InCombatInLast\(35 \* 1000\)\)\s*\{[^}]*Body\.Health = Body\.MaxHealth;[^}]*RearmAfterHeal\(\);", RegexOptions.Singleline),
                    Is.True, "the 30-35 s quiet heal re-arms");
                // Unchanged: his health, the shards' health, their number and the quarter each takes.
                Assert.That(((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(Fornfrusenen))).MaxHealth, Is.EqualTo(100000));
                Assert.That(((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(FornfrusenenShard))).MaxHealth, Is.EqualTo(50000));
                Assert.That(source, Does.Contain("i < Util.Random(2, 3)"));
                Assert.That(source, Does.Contain("boss.Health -= boss.MaxHealth / 4"));
            });
        }

        // ---- 2. Aroon: a fight with an add of the attempt's boss is contact, and restarts the grace --------------------

        [Test]
        public void AnAddOfTheAttemptsBossInCombatIsTheAttemptsFight()
        {
            var boss = Npc<ScaithBoss>(30000, 30000, 15000);
            var one = Npc<ScaithOne>(30200, 30000, 15000);
            var two = Npc<ScaithTwo>(30000, 30300, 15000);
            var live = new List<GameNPC> { boss, one, two };
            Assert.That(R.AttemptAddInFight(boss, live), Is.False, "nothing in combat");
            one.Fighting = true;
            Assert.That(R.AttemptAddInFight(boss, live), Is.True);
            one.Fighting = false; one.Dead = true;
            Assert.That(R.AttemptAddInFight(boss, live), Is.False, "the gap: one dead, two not pulled yet");
            two.Fighting = true;
            Assert.That(R.AttemptAddInFight(boss, live), Is.True);
            Assert.That(R.AttemptAddInFight(null, live), Is.False, "no attempt boss");
        }

        [Test]
        public void TheGraceRunsFromTheLastAddFightNotOnlyFromThePull()
        {
            // The diagnosis's case: FINAL_ENGAGE at 0 s; the boss never enters combat; add 1 is fought and dies at 200 s; add 2
            // is first in combat at 230 s. b1b6846 ended the attempt "never entered combat" as soon as add 1 died.
            long engaged = S(0), lastAddFight = S(200);
            Assert.Multiple(() =>
            {
                Assert.That(R.InFinalEngageGrace(engaged, 0, S(210)), Is.False, "as on b1b6846: the grace from the pull ran out");
                for (int t = 200; t <= 230; t += 2)
                    Assert.That(R.InFinalEngageGrace(engaged, lastAddFight, S(t)), Is.True, $"no ATTEMPT_ENDED at {t} s");
                Assert.That(R.InFinalEngageGrace(engaged, lastAddFight, S(319)), Is.True);
                Assert.That(R.InFinalEngageGrace(engaged, lastAddFight, S(320)), Is.False, "120 s after the last add fight it is judged");
                Assert.That(R.InFinalEngageGrace(engaged, 0, S(119)), Is.True, "the pull's own grace is as before");
                Assert.That(R.InFinalEngageGrace(S(1000), S(200), S(1100)), Is.True, "an older attempt's add fight never shortens it");
                Assert.That(R.InFinalEngageGrace(S(1000), S(200), S(1120)), Is.False);
            });
        }

        [Test]
        public void PullsWhoseAddsWereFoughtAreNeverUnreached()
        {
            // Aroon attempts 1-3 (09:04:49, 09:12:19, 09:21:59): each ended "never entered combat" and the third set him aside
            // ("never entered combat after 3 pulls"), with 14, 35 and 19 fallen at his Scaiths.
            var before = new R.PullContact();
            bool asideBefore = false;
            for (int pull = 0; pull < 3; pull++) { before.Begin(); asideBefore = before.Ended(false); }
            Assert.That(asideBefore, Is.True, "b1b6846: the add fights never reached SawCombat");
            var now = new R.PullContact();
            bool aside = false;
            for (int pull = 0; pull < 3; pull++) { now.Begin(); now.SawCombat(); /* a Scaith in combat */ aside |= now.Ended(false); }
            Assert.That(aside, Is.False, "a pull that fought his adds reached the encounter");
            Assert.That(now.Contact, Is.True, "ends \"left combat\", not \"never entered combat\"");
        }

        [Test]
        public void TheKillSinceTheAttemptStillKeysOnThePull()
        {
            // The new field is separate: a death seen after the pull but before the last add fight is still a kill since it.
            Assert.That(R.KilledSinceAttempt(S(10), S(150), S(400)), Is.True);
            Assert.That(R.KilledSinceAttempt(S(300), S(150), S(400)), Is.False);
        }

        // ---- 3. Easmarach: an old muster record is not reused -----------------------------------------------------------

        private static long At(string hhmmss) => (long)TimeSpan.Parse(hhmmss).TotalMilliseconds;

        [Test]
        public void AMusterLeftForAWholeStallWindowStartsAFreshCount()
        {
            // 07:29:32-07:33:35 FINAL_MUSTER for Easmarach (best 43 of 86); 07:34:26 TARGET_SWITCH away; back at 09:22.
            var old = new R.MusterProgress();
            old.Observe(36, 86, At("07:29:32"));
            old.Observe(43, 86, At("07:33:35"));
            Assert.Multiple(() =>
            {
                Assert.That(old.Observe(43, 86, At("07:34:06")), Is.False);
                Assert.That(old.StaleAt(At("09:22:09")), Is.True, "108 minutes away: the record is an old muster's");
                // What b1b6846 did with it: stalled 9 s after Easmarach was the target again.
                var reused = new R.MusterProgress();
                reused.Observe(43, 86, At("07:33:35"));
                Assert.That(reused.Observe(0, 86, At("09:22:09")), Is.True, "the stale record stalls at once");
                // A fresh record: no stall in the first 20 minutes.
                var fresh = new R.MusterProgress();
                for (long t = At("09:22:09"); t < At("09:22:09") + R.MusterStalledMilliseconds; t += 10_000)
                    Assert.That(fresh.Observe(0, 86, t), Is.False);
            });
        }

        [Test]
        public void KrondonsShortInterruptionsStillKeepTheMusterClock()
        {
            // Build 8d7baa3: the raid turned on Scurceol Hyrde 14:42:32-14:48:07 between two Beran musters; the clock follows.
            var beran = new R.MusterProgress();
            beran.Observe(5, 50, At("14:42:02"));
            Assert.That(beran.StaleAt(At("14:48:07")), Is.False, "six minutes away is the same muster");
            Assert.That(new R.MusterProgress().StaleAt(At("14:48:07")), Is.False, "a new record is never stale");
            Assert.That(beran.StaleAt(At("14:42:02") + R.MusterStalledMilliseconds), Is.True);
        }
    }
}
