using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    // AI_PROFILE (Living Server 1.2): the percentile maths, the merging of per-thread counters, self time, the measured
    // locks, the output formats and the cost of one scope. The slot design is lometur's (HearthDAoC bot CPU meter) and
    // the scope and measured lock are stefanrows's (BotThinkProfiler); both GPL-3.0.
    [TestFixture, NonParallelizable]
    public class UT_AiProfile
    {
        private bool _wasEnabled;

        [SetUp]
        public void TurnOn()
        {
            _wasEnabled = AiProfile.Enabled;
            AiProfile.Enabled = true;
        }

        [TearDown]
        public void Restore() => AiProfile.Enabled = _wasEnabled;

        // A system of the test's own, so other code that runs scopes cannot change its numbers.
        private static int Slot(string name) => AiSystemNames.Resolve("test." + name);

        private static AiProfileSystemStat Find(AiProfileSystemStat[] stats, int slot) =>
            stats.FirstOrDefault(stat => stat.System == slot);

        private static void Spin(TimeSpan time)
        {
            long until = Stopwatch.GetTimestamp() + (long)(time.TotalSeconds * Stopwatch.Frequency);
            while (Stopwatch.GetTimestamp() < until) { }
        }

        // ---- Percentiles -------------------------------------------------------------------------------------------

        [Test]
        public void NearestRankPercentilesOfOneToAHundred()
        {
            double[] sorted = Enumerable.Range(1, 100).Select(i => (double)i).ToArray();
            Assert.That(AiProfileTicks.Percentile(sorted, 100, 50), Is.EqualTo(50));
            Assert.That(AiProfileTicks.Percentile(sorted, 100, 95), Is.EqualTo(95));
            Assert.That(AiProfileTicks.Percentile(sorted, 100, 99), Is.EqualTo(99));
            Assert.That(AiProfileTicks.Percentile(sorted, 100, 100), Is.EqualTo(100));
            Assert.That(AiProfileTicks.Percentile(sorted, 100, 0), Is.EqualTo(1), "p0 is the smallest value");
        }

        [Test]
        public void PercentilesOfSmallAndEmptyWindows()
        {
            Assert.That(AiProfileTicks.Percentile([], 0, 99), Is.Zero);
            Assert.That(AiProfileTicks.Percentile([7], 1, 50), Is.EqualTo(7));
            Assert.That(AiProfileTicks.Percentile([7], 1, 99), Is.EqualTo(7));
            // Ten values: p50 is the 5th, p95 and p99 the 10th.
            double[] ten = Enumerable.Range(1, 10).Select(i => i * 10.0).ToArray();
            Assert.That(AiProfileTicks.Percentile(ten, 10, 50), Is.EqualTo(50));
            Assert.That(AiProfileTicks.Percentile(ten, 10, 95), Is.EqualTo(100));
            Assert.That(AiProfileTicks.Percentile(ten, 10, 99), Is.EqualTo(100));
        }

        [Test]
        public void TickSummarySortsItsSamplesAndSplitsSerialFromParallel()
        {
            var ticks = new AiProfileTicks(1000);
            // 1..100 ms in a shuffled order; serial is a tenth of each tick, parallel half.
            foreach (int ms in Enumerable.Range(1, 100).OrderBy(i => (i * 37) % 101))
                ticks.Add(ms, ms / 10.0, ms / 2.0);

            AiProfileTickSummary summary = ticks.Summarize();
            Assert.That(summary.Ticks, Is.EqualTo(100));
            Assert.That(summary.Dropped, Is.Zero);
            Assert.That(summary.TickP50, Is.EqualTo(50));
            Assert.That(summary.TickP95, Is.EqualTo(95));
            Assert.That(summary.TickP99, Is.EqualTo(99));
            Assert.That(summary.TickMax, Is.EqualTo(100));
            Assert.That(summary.TickAverage, Is.EqualTo(50.5).Within(1e-9));
            Assert.That(summary.SerialAverage, Is.EqualTo(5.05).Within(1e-9));
            Assert.That(summary.SerialP99, Is.EqualTo(9.9).Within(1e-9));
            Assert.That(summary.ParallelAverage, Is.EqualTo(25.25).Within(1e-9));
            Assert.That(summary.OtherAverage, Is.EqualTo(50.5 - 5.05 - 25.25).Within(1e-9));
        }

        [Test]
        public void TicksPastCapacityStillCountInTheAveragesAndTheMaximum()
        {
            var ticks = new AiProfileTicks(4);
            foreach (double ms in new[] { 1.0, 2, 3, 4, 100 })
                ticks.Add(ms, 0, 0);
            AiProfileTickSummary summary = ticks.Summarize();
            Assert.That(summary.Ticks, Is.EqualTo(5));
            Assert.That(summary.Dropped, Is.EqualTo(1));
            Assert.That(summary.TickMax, Is.EqualTo(100), "the maximum is exact");
            Assert.That(summary.TickAverage, Is.EqualTo(22));
            Assert.That(summary.TickP99, Is.EqualTo(4), "percentiles use the recorded ticks only");

            ticks.Clear();
            Assert.That(ticks.Summarize(), Is.EqualTo(default(AiProfileTickSummary)));
        }

        // ---- Merging -----------------------------------------------------------------------------------------------

        [Test]
        public void WindowsAddEveryThreadsCountersAndStartAgainAfterEachMinute()
        {
            int slot = Slot("merge");
            var merger = new AiProfileMerger();
            merger.Baseline();

            const int threads = 4, scopesEach = 1000;
            using var start = new Barrier(threads);
            Thread[] workers = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
            {
                start.SignalAndWait();
                for (int i = 0; i < scopesEach; i++)
                    using (AiProfile.Scope("test.merge")) { }
            })).ToArray();
            foreach (Thread worker in workers) worker.Start();
            foreach (Thread worker in workers) worker.Join();

            AiProfileSystemStat first = Find(merger.TakeWindow(), slot);
            Assert.That(first.Calls, Is.EqualTo(threads * scopesEach));
            Assert.That(first.TotalMs, Is.GreaterThanOrEqualTo(0));
            Assert.That(first.Name, Is.EqualTo("test.merge"));

            // Nothing new ran: the next window has no row for it (the totals are not counted twice).
            Assert.That(Find(merger.TakeWindow(), slot).Calls, Is.Zero);

            // The threads ended: their counters are folded in once and their slots let go.
            using (AiProfile.Scope("test.merge")) { }
            Assert.That(Find(merger.TakeWindow(), slot).Calls, Is.EqualTo(1));
        }

        [Test]
        public void EndedThreadsAreFoldedSoTheSlotListDoesNotGrow()
        {
            var merger = new AiProfileMerger();
            merger.Baseline();
            int before = AiProfile.ThreadSlotCount;
            for (int round = 0; round < 5; round++)
            {
                var worker = new Thread(() => { using (AiProfile.Scope("test.fold")) { } });
                worker.Start();
                worker.Join();
            }
            AiProfileSystemStat stat = Find(merger.TakeWindow(), Slot("fold"));
            Assert.That(stat.Calls, Is.EqualTo(5));
            Assert.That(AiProfile.ThreadSlotCount, Is.LessThanOrEqualTo(before));
        }

        [Test]
        public void TheLongestCallIsPerWindow()
        {
            int slot = Slot("max");
            var merger = new AiProfileMerger();
            merger.Baseline();
            using (AiProfile.Scope("test.max")) Spin(TimeSpan.FromMilliseconds(20));
            using (AiProfile.Scope("test.max")) { }
            AiProfileSystemStat first = Find(merger.TakeWindow(), slot);
            Assert.That(first.MaxMs, Is.GreaterThanOrEqualTo(19));

            using (AiProfile.Scope("test.max")) { }
            AiProfileSystemStat second = Find(merger.TakeWindow(), slot);
            Assert.That(second.Calls, Is.EqualTo(1));
            Assert.That(second.MaxMs, Is.LessThan(19), "last minute's long call is not this minute's maximum");
        }

        [Test]
        public void DifferenceSkipsSystemsThatDidNotRunAndSortsBySelfTime()
        {
            var before = new long[AiProfile.SlotLength];
            var after = new long[AiProfile.SlotLength];
            void Set(long[] slot, AiSystem system, long calls, long totalMs, long selfMs)
            {
                int at = AiProfile.Padding + (int)system * AiProfile.Fields;
                slot[at] = calls;
                slot[at + 1] = totalMs * Stopwatch.Frequency / 1000;
                slot[at + 2] = selfMs * Stopwatch.Frequency / 1000;
            }
            Set(before, AiSystem.CombatHeals, 10, 5, 5);
            Set(after, AiSystem.CombatHeals, 30, 25, 25);
            Set(before, AiSystem.ControllerTick, 5, 100, 10);
            Set(after, AiSystem.ControllerTick, 5, 100, 10);
            Set(after, AiSystem.RouteZoneStep, 2, 80, 80);

            AiProfileSystemStat[] stats = AiProfileMerger.Difference(before, after);
            Assert.That(stats.Select(stat => stat.Name), Is.EqualTo(new[] { "route.zone_step", "combat.heals" }));
            Assert.That(stats[1].Calls, Is.EqualTo(20));
            Assert.That(stats[1].TotalMs, Is.EqualTo(20).Within(0.001));
        }

        // ---- Self time -----------------------------------------------------------------------------------------------

        [Test]
        public void ANestedScopeIsTakenOutOfItsParentsSelfTime()
        {
            int parent = Slot("parent"), child = Slot("child");
            var merger = new AiProfileMerger();
            merger.Baseline();
            using (AiProfile.Scope("test.parent"))
            {
                Spin(TimeSpan.FromMilliseconds(5));
                using (AiProfile.Scope("test.child"))
                    Spin(TimeSpan.FromMilliseconds(20));
            }
            AiProfileSystemStat[] stats = merger.TakeWindow();
            AiProfileSystemStat p = Find(stats, parent), c = Find(stats, child);
            Assert.That(c.SelfMs, Is.EqualTo(c.TotalMs), "a scope with nothing inside: self is total");
            Assert.That(p.TotalMs, Is.GreaterThanOrEqualTo(c.TotalMs + 4.5));
            Assert.That(p.SelfMs, Is.EqualTo(p.TotalMs - c.TotalMs).Within(0.5));
            Assert.That(p.SelfMs, Is.LessThan(c.SelfMs));
        }

        [Test]
        public void ALeafIsTakenOutOfTheOpenScope()
        {
            int parent = Slot("leafparent");
            var merger = new AiProfileMerger();
            merger.Baseline();
            long tenMs = Stopwatch.Frequency / 100;
            using (AiProfile.Scope("test.leafparent"))
            {
                Spin(TimeSpan.FromMilliseconds(30));
                AiProfile.AddLeaf(AiSystem.NavPath, tenMs);
            }
            AiProfileSystemStat p = Find(merger.TakeWindow(), parent);
            Assert.That(p.SelfMs, Is.EqualTo(p.TotalMs - 10).Within(0.01));
        }

        [Test]
        public void NothingIsRecordedWhileOff()
        {
            int slot = Slot("off");
            var merger = new AiProfileMerger();
            merger.Baseline();
            AiProfile.Enabled = false;
            AiProfile.ProfileScope scope = AiProfile.Scope("test.off");
            Assert.That(scope.IsActive, Is.False);
            scope.Dispose();
            using (AiProfile.Scope(AiSystem.CombatHeals)) { }
            AiProfile.AddLeaf(AiSystem.NavPath, 1000);
            Assert.That(AiProfile.Timestamp(), Is.Zero);
            AiProfile.Enabled = true;
            Assert.That(Find(merger.TakeWindow(), slot).Calls, Is.Zero);
        }

        [Test]
        public void ScopeIfTimesOnlyWhenAsked()
        {
            Assert.That(AiProfile.ScopeIf(false, AiSystem.RouteZoneStep).IsActive, Is.False);
            AiProfile.ProfileScope scope = AiProfile.ScopeIf(true, AiSystem.RouteZoneStep);
            Assert.That(scope.IsActive, Is.True);
            scope.Dispose();
        }

        // ---- Names -------------------------------------------------------------------------------------------------

        [Test]
        public void EveryBuiltInSystemHasAUniqueDottedName()
        {
            string[] names = Enumerable.Range(0, (int)AiSystem.BuiltInCount).Select(AiSystemNames.Of).ToArray();
            Assert.That(names, Has.All.Match(@"^[a-z]+\.[a-z0-9_]+$"));
            Assert.That(names.Distinct().Count(), Is.EqualTo(names.Length));
            Assert.That(AiSystemNames.Resolve("route.zone_step"), Is.EqualTo((int)AiSystem.RouteZoneStep));
            Assert.That(AiSystemNames.Resolve("test.same"), Is.EqualTo(AiSystemNames.Resolve("test.same")));
            Assert.That((int)AiSystem.BuiltInCount, Is.LessThan(AiProfile.MaxSystems));
            Assert.That(AiSystemNames.IsLockWait((int)AiSystem.LockCoordinator), Is.True);
            Assert.That(AiSystemNames.IsLockWait((int)AiSystem.CoordinatorPulse), Is.False);
        }

        // ---- Measured locks ------------------------------------------------------------------------------------------

        [Test]
        public void AContendedLockRecordsItsWaitAndAnUncontendedOneNothing()
        {
            var gate = new object();
            var merger = new AiProfileMerger();
            merger.Baseline();

            using (AiProfile.Lock(gate, AiSystem.LockRaid))
                Assert.That(Monitor.IsEntered(gate), Is.True);
            Assert.That(Monitor.IsEntered(gate), Is.False, "disposing releases the lock");
            Assert.That(Find(merger.TakeWindow(), (int)AiSystem.LockRaid).Calls, Is.Zero);

            using var held = new ManualResetEventSlim();
            var holder = new Thread(() =>
            {
                lock (gate)
                {
                    held.Set();
                    Spin(TimeSpan.FromMilliseconds(30));
                }
            });
            holder.Start();
            held.Wait();
            using (AiProfile.Lock(gate, AiSystem.LockRaid))
            {
                // Same monitor, same reentrancy as lock (gate).
                using (AiProfile.Lock(gate, AiSystem.LockRaid)) { }
                Assert.That(Monitor.IsEntered(gate), Is.True);
            }
            holder.Join();

            AiProfileSystemStat wait = Find(merger.TakeWindow(), (int)AiSystem.LockRaid);
            Assert.That(wait.Calls, Is.EqualTo(1));
            Assert.That(wait.TotalMs, Is.GreaterThan(5));
        }

        // Review of PR #67: the measured lock must behave exactly like lock (gate) in every way the 137 sites rely on.

        [TestCase(true)]
        [TestCase(false)]
        public void TheLockIsReleasedWhenTheBlockThrows(bool profilerOn)
        {
            AiProfile.Enabled = profilerOn;
            var gate = new object();
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (AiProfile.Lock(gate, AiSystem.LockCoordinator))
                    throw new InvalidOperationException("thrown inside the block");
            });
            Assert.That(Monitor.IsEntered(gate), Is.False);
            bool taken = false;
            var other = new Thread(() =>
            {
                taken = Monitor.TryEnter(gate);
                if (taken) Monitor.Exit(gate);
            });
            other.Start();
            other.Join();
            Assert.That(taken, Is.True, "another thread can take it at once");
        }

        [Test]
        public void AThrowInTheInnerOfAReentrantPairLeavesTheOuterHeld()
        {
            var gate = new object();
            using (AiProfile.Lock(gate, AiSystem.LockRaid))
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using (AiProfile.Lock(gate, AiSystem.LockRaid))
                        throw new InvalidOperationException("thrown in the inner block");
                });
                Assert.That(Monitor.IsEntered(gate), Is.True, "the outer hold is still there");
                bool taken = true;
                var other = new Thread(() => taken = Monitor.TryEnter(gate));
                other.Start();
                other.Join();
                Assert.That(taken, Is.False, "and still excludes other threads");
            }
            Assert.That(Monitor.IsEntered(gate), Is.False);
        }

        [Test]
        public void WhileTheWrapperHoldsAGateAPlainLockAndTryEnterElsewhereAreExcluded()
        {
            var gate = new object();
            bool tryEnterGot = true;
            int plainLockEntered = 0;
            Thread plain;
            using (AiProfile.Lock(gate, AiSystem.LockRvrEvents))
            {
                var probe = new Thread(() =>
                {
                    tryEnterGot = Monitor.TryEnter(gate, 20);
                    if (tryEnterGot) Monitor.Exit(gate);
                });
                probe.Start();
                probe.Join();
                plain = new Thread(() => { lock (gate) Interlocked.Exchange(ref plainLockEntered, 1); });
                plain.Start();
                Thread.Sleep(50);
                Assert.That(Volatile.Read(ref plainLockEntered), Is.Zero, "a plain lock (gate) waits");
            }
            Assert.That(tryEnterGot, Is.False, "Monitor.TryEnter on another thread fails");
            Assert.That(plain.Join(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(plainLockEntered, Is.EqualTo(1), "and gets in once the wrapper releases");
        }

        [Test]
        public void UncontendedReentrancyRecordsNoWait()
        {
            var gate = new object();
            var merger = new AiProfileMerger();
            merger.Baseline();
            using (AiProfile.Lock(gate, AiSystem.LockCoordinator))
            {
                using (AiProfile.Lock(gate, AiSystem.LockCoordinator))
                {
                    using (AiProfile.Lock(gate, AiSystem.LockCoordinator))
                        Assert.That(Monitor.IsEntered(gate), Is.True);
                    Assert.That(Monitor.IsEntered(gate), Is.True);
                }
                Assert.That(Monitor.IsEntered(gate), Is.True);
            }
            Assert.That(Monitor.IsEntered(gate), Is.False);
            Assert.That(Find(merger.TakeWindow(), (int)AiSystem.LockCoordinator).Calls, Is.Zero);
        }

        [Test]
        public void WaitAndPulseWorkThroughTheWrapper()
        {
            var gate = new object();
            bool ready = false, woke = false;
            var waiter = new Thread(() =>
            {
                using (AiProfile.Lock(gate, AiSystem.LockRaid))
                {
                    while (!ready)
                        Monitor.Wait(gate); // gives the monitor up while waiting, as under lock (gate)
                    woke = Monitor.IsEntered(gate);
                }
            });
            waiter.Start();
            Thread.Sleep(20);
            using (AiProfile.Lock(gate, AiSystem.LockRaid))
            {
                ready = true;
                Monitor.Pulse(gate);
            }
            Assert.That(waiter.Join(TimeSpan.FromSeconds(5)), Is.True, "the waiter woke up");
            Assert.That(woke, Is.True, "and held the monitor again");
            Assert.That(Monitor.IsEntered(gate), Is.False);
        }

        [Test]
        public void ADefaultHeldLockReleasesNothing()
        {
            var gate = new object();
            using (AiProfile.Lock(gate, AiSystem.LockRaid))
            {
                default(AiProfile.HeldLock).Dispose();
                Assert.That(Monitor.IsEntered(gate), Is.True);
            }
        }

        [Test]
        public void ASystemThreadingLockIsRefusedAtBuildTime()
        {
            var method = typeof(AiProfile).GetMethod(nameof(AiProfile.Lock), [typeof(Lock), typeof(AiSystem)]);
            Assert.That(method, Is.Not.Null);
            var obsolete = (ObsoleteAttribute)Attribute.GetCustomAttribute(method, typeof(ObsoleteAttribute));
            Assert.That(obsolete?.IsError, Is.True, "passing a System.Threading.Lock is a compile error");
            Assert.That(typeof(AiProfile.HeldLock).IsByRefLike, Is.True, "a ref struct: no await across it, no boxing");
        }

        [Test]
        public void TheLockStillWorksWhileTheProfilerIsOff()
        {
            AiProfile.Enabled = false;
            var gate = new object();
            using (AiProfile.Lock(gate, AiSystem.LockCoordinator))
                Assert.That(Monitor.IsEntered(gate), Is.True);
            Assert.That(Monitor.IsEntered(gate), Is.False);
        }

        // ---- Output --------------------------------------------------------------------------------------------------

        private static AiProfileMinute SampleMinute()
        {
            var ticks = new AiProfileTicks(100);
            for (int i = 1; i <= 100; i++)
                ticks.Add(i, i / 10.0, i / 2.0);
            AiProfileSystemStat[] systems =
            [
                new("think.npc", (int)AiSystem.ThinkNpc, 12000, 900, 850.25, 12.5),
                new("pulse.realm_raid", (int)AiSystem.PulseRealmRaid, 1800, 40, 40, 3.5),
                new("lock.coordinator", (int)AiSystem.LockCoordinator, 30, 1.5, 1.5, 0.4),
            ];
            return new AiProfileMinute(new DateTime(2026, 11, 3, 12, 1, 0, DateTimeKind.Utc), 60, ticks.Summarize(), systems);
        }

        [Test]
        public void TheSummaryLineStartsWithTheAuditsFieldsInItsOrder()
        {
            string line = SampleMinute().SummaryLine();
            Assert.That(line, Does.StartWith(
                "AI_PROFILE tick_p50=50.00 p95=95.00 p99=99.00 max=100.00 serial_ms=5.050 parallel_ms=25.250 locks_ms=1.5 " +
                "top=think.npc:12000/850.3,pulse.realm_raid:1800/40.0,lock.coordinator:30/1.5 "));
            Assert.That(line, Does.Contain(" ticks=100 "));
            Assert.That(line, Does.Contain(" serial_p99=9.90 "));
            Assert.That(line, Does.Contain(" over_budget=serial_p99:9.9,pulse.realm_raid:3.5 "));
            Assert.That(line, Does.EndWith(" utc=2026-11-03T12:01:00Z"));
            Assert.That(line, Does.Not.Contain("\n"));
        }

        [Test]
        public void AQuietMinuteSaysSo()
        {
            var minute = new AiProfileMinute(DateTime.UtcNow, 60, default, []);
            Assert.That(minute.TopList(10), Is.EqualTo("none"));
            Assert.That(minute.OverBudget(), Is.Empty);
            Assert.That(minute.SummaryLine(), Does.Contain(" top=none "));
        }

        [Test]
        public void CsvRowsHaveOneLinePerSystemAndTheSharesAddUp()
        {
            AiProfileMinute minute = SampleMinute();
            string[] rows = minute.CsvRows().ToArray();
            Assert.That(rows, Has.Length.EqualTo(3));
            Assert.That(rows[0], Does.StartWith("2026-11-03T12:01:00Z,think.npc,12000,900,850.25,12.5,75,"));
            Assert.That(rows.All(row => row.Split(',').Length == AiProfileMinute.CsvHeader.Split(',').Length));
            Assert.That(minute.Systems.Sum(minute.SharePercent), Is.EqualTo(100).Within(1e-9));
        }

        [Test]
        public void TheCsvFileGetsItsHeaderOnce()
        {
            string dir = Path.Combine(Path.GetTempPath(), "ai-profile-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, AiProfileCsv.FileName);
            try
            {
                AiProfileCsv.Write(SampleMinute(), path);
                AiProfileCsv.Write(SampleMinute(), path);
                string[] lines = File.ReadAllLines(path);
                Assert.That(lines[0], Is.EqualTo(AiProfileMinute.CsvHeader));
                Assert.That(lines.Count(line => line == AiProfileMinute.CsvHeader), Is.EqualTo(1));
                Assert.That(lines, Has.Length.EqualTo(1 + 2 * 3));
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Test]
        public void TheGmCommandLinesNameTheTopSystems()
        {
            string[] lines = SampleMinute().ChatLines(2).ToArray();
            Assert.That(lines.Any(line => line.StartsWith("think.npc: 12000")), Is.True);
            Assert.That(lines.Any(line => line.StartsWith("lock.coordinator")), Is.False, "only the top two");
            Assert.That(lines.Last(), Does.Contain("1 more"));
        }

        // ---- The game-loop hooks ------------------------------------------------------------------------------------

        [Test]
        public void TheTickHooksFollowTheServerPropertyAndRecordTheTick()
        {
            bool property = AutonomousPopulationProperties.AI_PROFILE;
            try
            {
                AiProfile.Enabled = false;
                AutonomousPopulationProperties.AI_PROFILE = true;
                AiProfile.BeginTick();
                AiProfile.EndTick();
                Assert.That(AiProfile.Enabled, Is.True, "the end of a tick copies ai_profile into the switch");

                AiProfile.BeginTick();
                long serial = AiProfile.Timestamp();
                Spin(TimeSpan.FromMilliseconds(3));
                AiProfile.EndSerial(serial);
                long parallel = AiProfile.Timestamp();
                Spin(TimeSpan.FromMilliseconds(2));
                AiProfile.EndParallel(parallel);
                Assert.That(AiProfile.ElapsedThisTickMs(), Is.GreaterThanOrEqualTo(5));
                AiProfile.EndTick();
                Assert.That(AiProfile.LastTickMs, Is.GreaterThanOrEqualTo(5));
                Assert.That(AiProfile.LastSerialMs, Is.InRange(2.9, AiProfile.LastTickMs));

                AiProfileMinute minute = AiProfile.TakeMinute(Stopwatch.GetTimestamp(), DateTime.UtcNow);
                Assert.That(minute.Ticks.Ticks, Is.EqualTo(1));
                Assert.That(minute.Ticks.SerialAverage, Is.GreaterThanOrEqualTo(2.9));
                Assert.That(minute.Ticks.ParallelAverage, Is.GreaterThanOrEqualTo(1.9));
                Assert.That(AiProfile.LastMinute, Is.SameAs(minute), "what /lsprofile shows");

                AutonomousPopulationProperties.AI_PROFILE = false;
                AiProfile.BeginTick();
                AiProfile.EndTick();
                Assert.That(AiProfile.Enabled, Is.False);
                Assert.That(AiProfile.LastTickMs, Is.Zero);
            }
            finally
            {
                AutonomousPopulationProperties.AI_PROFILE = property;
            }
        }

        [Test]
        public void SingleNavmeshQueriesAreLeavesOfTheOpenScope()
        {
            var merger = new AiProfileMerger();
            merger.Baseline();
            long started = NavQueryProfile.Start();
            Spin(TimeSpan.FromMilliseconds(2));
            NavQueryProfile.Stop(NavQueryProfile.Kind.ClosestPoint, started);
            started = NavQueryProfile.Start();
            NavQueryProfile.Stop(NavQueryProfile.Kind.ZoneStep, started);
            AiProfileSystemStat[] stats = merger.TakeWindow();
            Assert.That(Find(stats, (int)AiSystem.NavClosestPoint).Calls, Is.EqualTo(1));
            Assert.That(Find(stats, (int)AiSystem.NavClosestPoint).TotalMs, Is.GreaterThanOrEqualTo(1.9));
            Assert.That(Find(stats, (int)AiSystem.RouteZoneStep).Calls, Is.Zero, "zone steps have their own scope");
        }

        // ---- Cost ----------------------------------------------------------------------------------------------------

        // The cost of one scope (two timestamps, a few thread-local adds), measured as the best of several batches so
        // a busy CI machine does not fail it by chance. The audit asked for "about 50 ns"; the gate is 100 ns. A scope
        // that started to lock, allocate or look anything up would cost several times that.
        public const double ScopeBudgetNanoseconds = 100;

        private static double BestNanosecondsPerCall(Action<int> batch, int calls, int batches = 9)
        {
            batch(calls); // warm up: JIT, tiering and the thread's slot
            double best = double.MaxValue;
            for (int round = 0; round < batches; round++)
            {
                long started = Stopwatch.GetTimestamp();
                batch(calls);
                double ns = Stopwatch.GetElapsedTime(started).TotalMilliseconds * 1e6 / calls;
                best = Math.Min(best, ns);
            }
            return best;
        }

        private static void Scopes(int calls)
        {
            for (int i = 0; i < calls; i++)
                using (AiProfile.Scope(AiSystem.CombatStyle)) { }
        }

        private static void Timestamps(int calls)
        {
            long sink = 0;
            for (int i = 0; i < calls; i++)
                sink += Stopwatch.GetTimestamp();
            GC.KeepAlive(sink);
        }

        [Test]
        public void OneScopeCostsAboutAHundredNanosecondsOrLess()
        {
            const int calls = 200_000;
            double scope = BestNanosecondsPerCall(Scopes, calls);
            double timestamp = BestNanosecondsPerCall(Timestamps, calls);
            TestContext.Out.WriteLine(FormattableString.Invariant(
                $"AI_PROFILE scope cost: {scope:0.0} ns per scope; one Stopwatch.GetTimestamp: {timestamp:0.0} ns"));
            // The fixed part of a scope beyond its two timestamps is small on any machine...
            Assert.That(scope - 2 * timestamp, Is.LessThan(40), "a scope's own work beyond its two timestamps");
            // ...and the whole scope stays within the budget wherever the clock itself is not slow.
            Assert.That(scope, Is.LessThan(Math.Max(ScopeBudgetNanoseconds, 2 * timestamp + 40)));
        }

        [Test]
        public void AScopeCostsAlmostNothingWhileOff()
        {
#if DEBUG
            Assert.Ignore("Timing bounds hold for Release builds only.");
#endif
            AiProfile.Enabled = false;
            double off = BestNanosecondsPerCall(Scopes, 1_000_000);
            TestContext.Out.WriteLine(FormattableString.Invariant($"AI_PROFILE scope cost while off: {off:0.00} ns"));
            // About 1 ns measured (one bool read and a loop); the bound is loose so a busy shared CI runner cannot flake it.
            Assert.That(off, Is.LessThan(20));
        }

        [Test]
        public void AScopeAllocatesNothing()
        {
            Scopes(10); // the thread's slot is made on its first scope
            long before = GC.GetAllocatedBytesForCurrentThread();
            Scopes(100_000);
            using (AiProfile.Lock(new object(), AiSystem.LockRaid)) { }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.LessThanOrEqualTo(64), "only the test's own lock object");
        }
    }
}
