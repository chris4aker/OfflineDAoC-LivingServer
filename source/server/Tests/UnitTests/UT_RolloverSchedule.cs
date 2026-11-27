using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using DOL.GS.Quests;
using NUnit.Framework;
using Key = DOL.GS.RolloverSchedulerService.IntervalKey;

namespace DOL.UnitTests
{
    /// <summary>
    /// RolloverSchedulerService's schedule (#565, port of OpenDAoC-Core e688e984e, 73cd88797, 7384fed04) on a fake clock
    /// and a fake store, and PeriodicQuestService's login rule. Server-wide; the quest resets touch players only.
    /// </summary>
    [TestFixture]
    public sealed class UT_RolloverSchedule
    {
        private sealed class FakeStore : RolloverSchedule.IStore
        {
            public readonly Dictionary<Key, DateTime> Rows = new();
            public readonly List<Key> Created = new();
            public int Saves;

            public IEnumerable<(Key Key, DateTime LastRollover)> Load() => Rows.Select(r => (r.Key, r.Value)).ToList();

            public DateTime Create(Key key, DateTime now)
            {
                Created.Add(key);
                Rows[key] = now;
                return now;
            }

            public void Save(Key key, DateTime lastRollover)
            {
                Saves++;
                Rows[key] = lastRollover;
            }
        }

        private DateTime _now;
        private FakeStore _store;

        [SetUp]
        public void SetUp()
        {
            _now = new DateTime(2026, 11, 26, 15, 30, 0); // a Thursday
            _store = new FakeStore();
        }

        private RolloverSchedule Schedule(List<string> errors = null) =>
            new(_store, () => _now, (message, e) => errors?.Add(message + ": " + e.Message));

        [Test]
        public void TheNextRolloverIsTheNextHourMidnightMondayOrFirstOfTheMonth()
        {
            DateTime thursday = new(2026, 11, 26, 15, 30, 0);
            DateTime monday = new(2026, 11, 30, 0, 0, 0);
            Assert.Multiple(() =>
            {
                Assert.That(RolloverSchedule.NextRollover(Key.Hourly, thursday), Is.EqualTo(new DateTime(2026, 11, 26, 16, 0, 0)));
                Assert.That(RolloverSchedule.NextRollover(Key.Hourly, new DateTime(2026, 11, 26, 23, 59, 0)), Is.EqualTo(new DateTime(2026, 11, 27)));
                Assert.That(RolloverSchedule.NextRollover(Key.Daily, thursday), Is.EqualTo(new DateTime(2026, 11, 27)));
                Assert.That(RolloverSchedule.NextRollover(Key.Weekly, thursday), Is.EqualTo(monday));
                Assert.That(RolloverSchedule.NextRollover(Key.Weekly, monday), Is.EqualTo(monday.AddDays(7)), "a rollover on Monday waits for the next Monday");
                Assert.That(RolloverSchedule.NextRollover(Key.Monthly, thursday), Is.EqualTo(new DateTime(2026, 12, 1)));
                Assert.That(RolloverSchedule.NextRollover(Key.Monthly, new DateTime(2026, 12, 31, 23, 0, 0)), Is.EqualTo(new DateTime(2027, 1, 1)));
            });
        }

        [Test]
        public void AMissingRowIsMadeAtStartSoAServerThatStopsBeforeMidnightStillRollsOver()
        {
            RolloverSchedule schedule = Schedule();
            Assert.Multiple(() =>
            {
                Assert.That(_store.Created, Is.EquivalentTo(Enum.GetValues<Key>()));
                Assert.That(schedule.GetLastRollover(Key.Daily), Is.EqualTo(_now));
                Assert.That(schedule.Tick(), Is.Zero, "nothing is due at start");
            });

            // The server stops at 23:00 and starts again the next afternoon: the saved row makes the day roll over.
            _now = _now.Date.AddDays(1).AddHours(14);
            RolloverSchedule restarted = Schedule();
            int ran = 0;
            restarted.Subscribe(Key.Daily, () => ran++);
            Assert.That(restarted.Tick(), Is.EqualTo(2), "hourly and daily");
            Assert.That(ran, Is.EqualTo(1));
        }

        [Test]
        public void ARolloverMissedWhileTheServerWasOffRunsOnceAtTheFirstTick()
        {
            _store.Rows[Key.Hourly] = _now.AddDays(-40);
            _store.Rows[Key.Daily] = _now.AddDays(-40);
            _store.Rows[Key.Weekly] = _now.AddDays(-40);
            _store.Rows[Key.Monthly] = _now.AddDays(-40);
            RolloverSchedule schedule = Schedule();
            var runs = new Dictionary<Key, int>();
            foreach (Key key in Enum.GetValues<Key>())
            {
                Key k = key;
                schedule.Subscribe(k, () => runs[k] = runs.GetValueOrDefault(k) + 1);
            }

            Assert.That(schedule.Tick(), Is.EqualTo(4));
            Assert.That(schedule.Tick(), Is.Zero, "not again at the next tick");
            Assert.Multiple(() =>
            {
                Assert.That(runs.Values, Is.All.EqualTo(1));
                Assert.That(_store.Rows.Values, Is.All.EqualTo(_now), "saved");
                Assert.That(schedule.GetNextRollover(Key.Daily), Is.EqualTo(_now.Date.AddDays(1)));
                Assert.That(schedule.GetNextRollover(Key.Weekly), Is.EqualTo(new DateTime(2026, 11, 30)));
            });
        }

        [Test]
        public void EachIntervalRollsOverOnItsOwnBoundary()
        {
            RolloverSchedule schedule = Schedule();
            var order = new List<Key>();
            foreach (Key key in Enum.GetValues<Key>())
            {
                Key k = key;
                schedule.Subscribe(k, () => order.Add(k));
            }

            // Thursday 26 November 15:30 to Wednesday 2 December 00:30, an hour at a time.
            for (int hour = 0; hour < 5 * 24 + 9; hour++)
            {
                _now = _now.AddHours(1);
                schedule.Tick();
            }

            Assert.Multiple(() =>
            {
                Assert.That(order.Count(k => k == Key.Hourly), Is.EqualTo(5 * 24 + 9));
                Assert.That(order.Count(k => k == Key.Daily), Is.EqualTo(6), "27 November to 2 December");
                Assert.That(order.Count(k => k == Key.Weekly), Is.EqualTo(1), "Monday 30 November");
                Assert.That(order.Count(k => k == Key.Monthly), Is.EqualTo(1), "1 December");
            });
        }

        [Test]
        public void ACallbackThatThrowsIsLoggedAndTheOthersStillRun()
        {
            var errors = new List<string>();
            RolloverSchedule schedule = Schedule(errors);
            bool second = false;
            schedule.Subscribe(Key.Daily, () => throw new InvalidOperationException("boom"));
            schedule.Subscribe(Key.Daily, () => second = true);

            schedule.ForceExecute(Key.Daily);
            Assert.Multiple(() =>
            {
                Assert.That(second, Is.True);
                Assert.That(errors, Has.Count.EqualTo(1));
                Assert.That(errors[0], Does.Contain("Daily").And.Contain("boom"));
            });
        }

        [Test]
        public void TheRolloverIsSavedBeforeItsCallbacksRun()
        {
            RolloverSchedule schedule = Schedule();
            _now = _now.AddDays(1);
            DateTime savedWhenCalled = default;
            schedule.Subscribe(Key.Daily, () => savedWhenCalled = _store.Rows[Key.Daily]);
            schedule.Tick();
            Assert.That(savedWhenCalled, Is.EqualTo(_now), "a crash in a callback never makes the rollover repeat at the next start");
        }

        [Test]
        public void TheFirstStartAfterTheUpdateStartsFromTheOldTable()
        {
            var old = new List<(string, DateTime)> { ("DAILY", _now.AddHours(-10)), ("WEEKLY", _now.AddDays(-3)), ("MONTHLY", _now.AddDays(40)) };
            Assert.Multiple(() =>
            {
                Assert.That(RolloverSchedule.SeedFromOldTable(Key.Daily, old, _now), Is.EqualTo(_now.AddHours(-10)));
                Assert.That(RolloverSchedule.SeedFromOldTable(Key.Weekly, old, _now), Is.EqualTo(_now.AddDays(-3)));
                Assert.That(RolloverSchedule.SeedFromOldTable(Key.Monthly, old, _now), Is.EqualTo(_now), "a time in the future is ignored");
                Assert.That(RolloverSchedule.SeedFromOldTable(Key.Hourly, old, _now), Is.EqualTo(_now), "hourly had no old row");
                Assert.That(RolloverSchedule.SeedFromOldTable(Key.Daily, null, _now), Is.EqualTo(_now));
            });
        }

        [Test]
        public void AnUnknownIntervalIsHarmless()
        {
            RolloverSchedule schedule = Schedule();
            Assert.Multiple(() =>
            {
                Assert.DoesNotThrow(() => schedule.Subscribe((Key)99, () => { }));
                Assert.DoesNotThrow(() => schedule.ForceExecute((Key)99));
                Assert.That(schedule.GetLastRollover((Key)99), Is.EqualTo(DateTime.MinValue));
            });
        }

        // PeriodicQuestService's rules. The last rollovers: daily this midnight, weekly last Monday, monthly the 1st.
        private static DateTime LastRollover(Key key) => key switch
        {
            Key.Daily => new DateTime(2026, 11, 26),
            Key.Weekly => new DateTime(2026, 11, 23),
            Key.Monthly => new DateTime(2026, 11, 1),
            _ => DateTime.MinValue
        };

        private static uint Daily => PeriodicQuestService.MaskOf(Key.Daily);
        private static uint Weekly => PeriodicQuestService.MaskOf(Key.Weekly);
        private static uint Monthly => PeriodicQuestService.MaskOf(Key.Monthly);

        [Test]
        public void ALoginResetsTheKindsWhoseRolloverCameSinceTheLastPlay()
        {
            Assert.Multiple(() =>
            {
                Assert.That(new[] { Daily, Weekly, Monthly }, Is.Unique.And.All.Not.Zero);
                Assert.That(PeriodicQuestService.TriggeredMask(new DateTime(2026, 11, 25, 22, 0, 0), LastRollover), Is.EqualTo(Daily),
                    "played yesterday evening: the dailies only");
                Assert.That(PeriodicQuestService.TriggeredMask(new DateTime(2026, 11, 20), LastRollover), Is.EqualTo(Daily | Weekly),
                    "played last week");
                Assert.That(PeriodicQuestService.TriggeredMask(new DateTime(2026, 10, 30), LastRollover), Is.EqualTo(Daily | Weekly | Monthly),
                    "played last month");
                Assert.That(PeriodicQuestService.TriggeredMask(new DateTime(2026, 11, 26, 9, 0, 0), LastRollover), Is.Zero,
                    "played this morning");
            });
        }

        // The review's blocker: GamePlayer.AddToWorld runs at login and at every region change, and PreviousLoginDate
        // stays the same all session, so after a midnight every zone change cleared the finished dailies again.
        [Test]
        public void AZoneChangeAfterTheResetDoesNotResetAgain()
        {
            DateTime previousLogin = new(2026, 11, 25, 22, 0, 0);
            DateTime checkedAt = DateTime.MinValue;

            uint atLogin = PeriodicQuestService.CheckOnEntry(ref checkedAt, previousLogin, new DateTime(2026, 11, 26, 10, 0, 0), LastRollover);
            uint atZoneChange = PeriodicQuestService.CheckOnEntry(ref checkedAt, previousLogin, new DateTime(2026, 11, 26, 11, 0, 0), LastRollover);

            // Still online at the next midnight: the rollover resets the player (ResetPlayerQuests marks the check), and
            // the zone change after it doesn't reset again.
            DateTime nextMidnight = new(2026, 11, 27);
            Func<Key, DateTime> afterMidnight = key => key == Key.Daily ? nextMidnight : LastRollover(key);
            DateTime missedCheck = checkedAt;
            uint zoneChangeAfterMidnightUnmarked = PeriodicQuestService.CheckOnEntry(ref missedCheck, previousLogin, nextMidnight.AddHours(1), afterMidnight);
            checkedAt = nextMidnight.AddSeconds(1); // what ResetPlayerQuests sets at the rollover
            uint zoneChangeAfterMidnightMarked = PeriodicQuestService.CheckOnEntry(ref checkedAt, previousLogin, nextMidnight.AddHours(1), afterMidnight);

            Assert.Multiple(() =>
            {
                Assert.That(atLogin, Is.EqualTo(Daily), "the first entry after the midnight resets the dailies");
                Assert.That(atZoneChange, Is.Zero, "a zone change an hour later doesn't");
                Assert.That(zoneChangeAfterMidnightUnmarked, Is.EqualTo(Daily), "a midnight passed since the last check counts once");
                Assert.That(zoneChangeAfterMidnightMarked, Is.Zero, "not after the rollover itself reset the player");
                Assert.That(checkedAt, Is.EqualTo(nextMidnight.AddHours(1)));
            });
        }

        private sealed class TestDaily : DOL.GS.Quests.DailyQuest
        {
            public override string QuestPropertyKey { get; set; }
            public override void LoadQuestParameters() { }
            public override void SaveQuestParameters() { }
            public override void Notify(DOL.Events.DOLEvent e, object sender, EventArgs args) { }
        }

        private sealed class TestWeekly : DOL.GS.Quests.WeeklyQuest
        {
            public override string QuestPropertyKey { get; set; }
            public override void LoadQuestParameters() { }
            public override void SaveQuestParameters() { }
            public override void Notify(DOL.Events.DOLEvent e, object sender, EventArgs args) { }
        }

        private sealed class TestMonthly : DOL.GS.Quests.MonthlyQuest
        {
            public override string QuestPropertyKey { get; set; }
            public override void LoadQuestParameters() { }
            public override void SaveQuestParameters() { }
            public override void Notify(DOL.Events.DOLEvent e, object sender, EventArgs args) { }
        }

        [Test]
        public void TheMaskSelectsOnlyItsKindsOfFinishedQuest()
        {
            AbstractQuest daily = new TestDaily(), weekly = new TestWeekly(), monthly = new TestMonthly();
            Assert.Multiple(() =>
            {
                Assert.That(PeriodicQuestService.Matches(daily, Daily), Is.True);
                Assert.That(PeriodicQuestService.Matches(weekly, Daily), Is.False);
                Assert.That(PeriodicQuestService.Matches(monthly, Daily), Is.False);
                Assert.That(PeriodicQuestService.Matches(weekly, Daily | Weekly), Is.True);
                Assert.That(PeriodicQuestService.Matches(monthly, Daily | Weekly), Is.False);
                Assert.That(PeriodicQuestService.Matches(monthly, Monthly), Is.True);
                Assert.That(PeriodicQuestService.Matches(daily, 0), Is.False);
                Assert.That(PeriodicQuestService.Matches(null, Daily | Weekly | Monthly), Is.False, "not a periodic quest");
                Assert.DoesNotThrow(() => PeriodicQuestService.OnPlayerJoin(null));
            });
        }
    }
}
