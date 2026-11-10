using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Budget = DOL.GS.AutonomousChatBudget;
using Schedule = DOL.GS.AutonomousBotSchedule;

namespace DOL.GS.Tests
{
    /// <summary>
    /// 1.1.3: a simulated day of the Living Server's population and /faction with the bot schedule (owner's PC, 2026-11-09:
    /// with bot_schedule_mode off 2,673 bots were online at 05:20 and /faction ran 323 and 291 lines an hour at 02:00-04:00,
    /// as busy as the evening). A model, not a world: the real schedule, login and logout rules and the real chat budget and
    /// gate, with a modelled roster of 2,700 gamebots (900 a realm), a third of them in bot groups whose tasks run 45-120
    /// minutes, and bots in a fight a third of the time. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_LivingServerDay
    {
        private const int Roster = 2_700;

        // A Tuesday and a Saturday (2026-11-10 and 2026-11-14).
        private static readonly DateTime Tuesday = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Local);
        private static readonly DateTime Saturday = new(2026, 11, 14, 0, 0, 0, DateTimeKind.Local);

        private static double RosterShare(DateTime local) =>
            Enumerable.Range(1, Roster).Count(id => Schedule.ClockOnline(id, AutonomousBotPersonality.TypeFor(id), local)) / (double)Roster;

        #region The clock curve

        [Test]
        public void ThePrimeTimeIsTheEveningAndTheTroughIsDeepBeforeDawn()
        {
            double[] tuesday = Enumerable.Range(0, 24).Select(h => RosterShare(Tuesday.AddHours(h).AddMinutes(30))).ToArray();
            double[] saturday = Enumerable.Range(0, 24).Select(h => RosterShare(Saturday.AddHours(h).AddMinutes(30))).ToArray();
            double prime = Enumerable.Range(19, 4).Average(h => tuesday[h]);
            double trough = Enumerable.Range(3, 4).Average(h => tuesday[h]);
            int busiest = Array.IndexOf(tuesday, tuesday.Max()), quietest = Array.IndexOf(tuesday, tuesday.Min());
            TestContext.Progress.WriteLine("hour  tuesday  saturday (bots of 2,700)");
            for (int h = 0; h < 24; h++)
                TestContext.Progress.WriteLine($"{h:00}:30  {tuesday[h] * Roster,6:0}  {saturday[h] * Roster,6:0}");
            Assert.That(busiest, Is.InRange(19, 22), "the busiest hour is in the evening");
            Assert.That(quietest, Is.InRange(3, 6), "the quietest hour is before dawn");
            Assert.That(prime, Is.GreaterThan(0.85), "the evening has nearly the whole roster");
            Assert.That(trough * Roster, Is.InRange(200, 600), "a few hundred online at 3-7 a.m.");
            Assert.That(trough / prime, Is.LessThan(0.25), "the trough is deep");
            Assert.That(Enumerable.Range(10, 8).Average(h => saturday[h]), Is.GreaterThan(Enumerable.Range(10, 8).Average(h => tuesday[h]) + 0.15),
                "a Saturday afternoon is busier than a Tuesday afternoon");
            Assert.That(Enumerable.Range(0, 24).Average(h => saturday[h]), Is.GreaterThan(Enumerable.Range(0, 24).Average(h => tuesday[h])),
                "a weekend day is busier");
        }

        [Test]
        public void TheShareNeverStepsWhereTheWeekdayAndWeekendCurvesMeet()
        {
            // Friday 18:00 and Monday 00:00 (1.1.2: a step of 0.15 of the roster, about 400 bots of 2,700, at Monday midnight).
            DateTime monday = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Local);
            double worst = 0;
            DateTime worstAt = monday;
            for (DateTime t = monday; t < monday.AddDays(7); t = t.AddMinutes(1))
            {
                double step = Math.Abs(Schedule.ClockShare(t.AddMinutes(1)) - Schedule.ClockShare(t));
                if (step > worst)
                    (worst, worstAt) = (step, t);
            }
            TestContext.Progress.WriteLine($"largest change in a minute: {worst:0.0000} of the roster at {worstAt:ddd HH:mm}");
            Assert.That(worst, Is.LessThan(0.007), "no minute changes the wanted share by more than 0.7% of the roster");
        }

        [Test]
        public void FewBotsChangeStateInAnyMinuteAllWeek()
        {
            // Includes the daily reshuffle of places (2-5 a.m.; 1.1.2 did it at midnight at once, about 450 bots of 2,700).
            DateTime monday = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Local);
            long[] ids = Enumerable.Range(1, Roster).Select(id => (long)id).ToArray();
            BotPersonalityType[] types = ids.Select(AutonomousBotPersonality.TypeFor).ToArray();
            bool[] last = ids.Select((id, i) => Schedule.ClockOnline(id, types[i], monday)).ToArray();
            int worst = 0, worstOff = 0;
            DateTime worstAt = monday;
            for (DateTime t = monday.AddMinutes(1); t <= monday.AddDays(7); t = t.AddMinutes(1))
            {
                int flips = 0, off = 0;
                for (int i = 0; i < ids.Length; i++)
                {
                    bool now = Schedule.ClockOnline(ids[i], types[i], t);
                    if (now != last[i])
                    {
                        flips++;
                        if (!now) off++;
                    }
                    last[i] = now;
                }
                if (flips > worst)
                    (worst, worstAt) = (flips, t);
                worstOff = Math.Max(worstOff, off);
            }
            TestContext.Progress.WriteLine($"most bots changing in one minute: {worst} at {worstAt:ddd HH:mm}; most sent home in one minute: {worstOff}");
            Assert.That(worst, Is.LessThanOrEqualTo(30));
            Assert.That(worstOff, Is.LessThan(Schedule.MaxLogoutsPerScan * 12), "the logout pace (3 every 5 s) keeps up");
        }

        [Test]
        public void ABotUpInTheMorningIsStillUpInTheEveningOnTheSameDay()
        {
            DateTime morning = Tuesday.AddHours(7), evening = Tuesday.AddHours(21);
            for (long id = 1; id <= Roster; id++)
                if (Schedule.ClockOnline(id, AutonomousBotPersonality.TypeFor(id), morning))
                    Assert.That(Schedule.ClockOnline(id, AutonomousBotPersonality.TypeFor(id), evening), Is.True, $"bot {id}");
        }

        [Test]
        public void DifferentBotsPlayLateOnDifferentNights()
        {
            int Late(DateTime day, long id) => Schedule.ClockOnline(id, BotPersonalityType.Leveler, day.AddHours(1)) ? 1 : 0;
            int both = 0, either = 0;
            for (long id = 1; id <= Roster; id++)
            {
                int a = Late(Tuesday, id), b = Late(Tuesday.AddDays(1), id);
                both += a & b;
                either += a | b;
            }
            Assert.That(both / (double)either, Is.InRange(0.4, 0.95), "the 1 a.m. crowd overlaps from night to night but isn't the same");
        }

        #endregion

        #region Logging out

        [Test]
        public void OnlyAFreeBotLogsOut()
        {
            Assert.That(Schedule.CanLogOut(true, false, true, true, false, false, false, false, false, false), Is.True);
            Assert.That(Schedule.CanLogOut(true, false, true, true, true, false, false, false, false, false), Is.False, "grouped (a party or a raid)");
            Assert.That(Schedule.CanLogOut(true, false, true, true, false, true, false, false, false, false), Is.False, "fighting");
            Assert.That(Schedule.CanLogOut(true, false, true, true, false, false, true, false, false, false), Is.False, "on a stable route");
            Assert.That(Schedule.CanLogOut(true, false, true, true, false, false, false, true, false, false), Is.False, "at a siege");
            Assert.That(Schedule.CanLogOut(true, false, true, true, false, false, false, false, true, false), Is.False, "carrying a relic");
            Assert.That(Schedule.CanLogOut(true, false, true, true, false, false, false, false, false, true), Is.False, "in a realm event");
            Assert.That(Schedule.CanLogOut(true, false, true, false, false, false, false, false, false, false), Is.False, "dead");
            Assert.That(Schedule.CanLogOut(false, false, true, true, false, false, false, false, false, false), Is.False, "a companion bot");
            Assert.That(Schedule.CanLogOut(true, true, true, true, false, false, false, false, false, false), Is.False, "a temporary group helper");
        }

        [Test]
        public void AnRvrBotsForceIdAloneIsNotARealmEvent()
        {
            // Every RvR gamebot carries "rvr-<id>" once it has run its RvR goal; 1.1.2 read that as "in an event", so no RvR bot
            // could ever log off on a schedule. With no event running the id commits it to nothing.
            Assert.That(AutonomousRvrEventLayer.IsForceCommitted("rvr-4242", 0), Is.False);
            Assert.That(Schedule.InRealmEvent(null), Is.False);
        }

        [TestCase(300, 0, 0, 0, 0, 300)]
        [TestCase(300, 250, 250, 0, 0, 50)]
        [TestCase(300, 250, 400, 0, 0, -25)]     // 150 still finishing: nobody logs in while the total is a quarter over
        [TestCase(300, 200, 300, 100, 0, 30)]    // overstays of the last 15 minutes count against the 10% slack
        [TestCase(300, 200, 300, 0, 20, 55)]
        [TestCase(1000, 990, 1000, 0, 0, 10)]
        public void LoginsWaitForTheBotsStillFinishing(int desired, int scheduledOnline, int total, int overstays, int pending, int expected) =>
            Assert.That(Schedule.LoginsWanted(desired, scheduledOnline, total, overstays, pending), Is.EqualTo(expected));

        #endregion

        #region A simulated day

        private sealed class Bot
        {
            public long Id;
            public BotPersonalityType Type;
            public int GroupEnds = -1;   // the minute its group task ends; -1 ungrouped
            public bool Online => Since >= 0;
            public int Since = -1;
        }

        /// <summary>One simulated day, minute by minute, per hour: the bots online at :30, logins, logouts and the worst minute.</summary>
        public sealed class Day
        {
            public readonly int[] OnlineAt30 = new int[24];
            public readonly int[] ScheduledAt30 = new int[24];
            public readonly int[] Logins = new int[24];
            public readonly int[] Logouts = new int[24];
            public int WorstLogoutMinute, WorstTenMinuteDrop, GroupedLogouts, Overstaying;
        }

        /// <summary>
        /// The population over a day from a server start at <paramref name="start"/> (the schedule on, clock mode): the startup
        /// ramp over the schedule's number, logins by AutonomousBotSchedule.LoginsWanted (16 a second at most), group formation
        /// that leaves out the bots the schedule sends home, and logout scans every 5 s of up to 3 bots that may log out.
        /// </summary>
        public static Day SimulateDay(DateTime start, int seed, int rampMinutes = 15)
        {
            var random = new Random(seed);
            var bots = Enumerable.Range(1, Roster).Select(id => new Bot { Id = id, Type = AutonomousBotPersonality.TypeFor(id) }).ToArray();
            var overstaySince = new Dictionary<long, int>();
            var day = new Day();
            var onlineByMinute = new int[24 * 60 + 1];
            for (int minute = 0; minute < 24 * 60; minute++)
            {
                DateTime local = start.AddMinutes(minute);
                bool[] want = bots.Select(bot => Schedule.ClockOnline(bot.Id, bot.Type, local)).ToArray();
                bool Wanted(Bot bot) => want[bot.Id - 1];
                Bot[] wanted = bots.Where(Wanted).ToArray();
                int hour = local.Hour;

                // Group tasks end (AutonomousBotGroupCoordinator.FinishGroupTask disbands the party).
                foreach (Bot bot in bots)
                    if (bot.GroupEnds >= 0 && minute >= bot.GroupEnds)
                        bot.GroupEnds = -1;
                // Groups form every 15 s; a third of the bots online end up in parties. Bots being sent home are left out.
                Bot[] free = bots.Where(b => b.Online && b.GroupEnds < 0 && Wanted(b)).ToArray();
                int grouped = bots.Count(b => b.Online && b.GroupEnds >= 0), online = bots.Count(b => b.Online);
                foreach (Bot bot in free.OrderBy(_ => random.Next()).Take(Math.Max(0, online / 3 - grouped) / 4))
                    bot.GroupEnds = minute + 45 + random.Next(76);

                // Logouts: a scan every 5 s, up to 3 bots each (AutonomousBotSchedule.Pulse).
                int leftThisMinute = 0;
                for (int scan = 0; scan < 12; scan++)
                {
                    int leaving = 0;
                    foreach (Bot bot in bots)
                    {
                        if (!bot.Online || Wanted(bot))
                            continue;
                        bool grouped0 = bot.GroupEnds >= 0, fighting = random.NextDouble() < 0.33;
                        if (leaving >= Schedule.MaxLogoutsPerScan ||
                            !Schedule.CanLogOut(true, false, true, true, grouped0, fighting, false, false, false, false))
                            continue;
                        if (grouped0)
                            day.GroupedLogouts++;
                        leaving++;
                        leftThisMinute++;
                        bot.Since = -1;
                    }
                }
                day.Logouts[hour] += leftThisMinute;
                day.WorstLogoutMinute = Math.Max(day.WorstLogoutMinute, leftThisMinute);

                // Overstays (scheduled off, still online) of the last 15 minutes.
                int recent = 0;
                foreach (Bot bot in bots)
                {
                    if (bot.Online && !Wanted(bot))
                    {
                        if (!overstaySince.TryGetValue(bot.Id, out int since))
                            overstaySince[bot.Id] = since = minute;
                        if (minute - since < 15)
                            recent++;
                    }
                    else
                        overstaySince.Remove(bot.Id);
                }

                // Logins: the ramp over the schedule's number, then AutonomousBotSchedule.LoginsWanted (16 a second at most).
                int desired = AutonomousPopulationRamp.DesiredActiveCount(true, wanted.Length, rampMinutes, TimeSpan.FromMinutes(minute));
                int scheduledOnline = wanted.Count(b => b.Online);
                int missing = Math.Min(60 * AutonomousPopulationController.MaximumSpawnEnqueuePerPoll,
                    Schedule.LoginsWanted(desired, scheduledOnline, bots.Count(b => b.Online), recent, 0));
                int came = 0;
                foreach (Bot bot in wanted.Where(b => !b.Online).OrderBy(_ => random.Next()).Take(Math.Max(0, missing)))
                {
                    bot.Since = minute;
                    came++;
                }
                day.Logins[hour] += came;
                onlineByMinute[minute] = bots.Count(b => b.Online);
                if (local.Minute == 30)
                {
                    day.OnlineAt30[hour] = onlineByMinute[minute];
                    day.ScheduledAt30[hour] = wanted.Length;
                    if (hour == 5)
                        day.Overstaying = bots.Count(b => b.Online && !Wanted(b));
                }
                if (minute >= 60 + 10)
                    day.WorstTenMinuteDrop = Math.Max(day.WorstTenMinuteDrop, onlineByMinute[minute - 10] - onlineByMinute[minute]);
            }
            return day;
        }

        private static Day _tuesday, _saturday;
        private static Day TuesdayRun => _tuesday ??= SimulateDay(Tuesday, 11);
        private static Day SaturdayRun => _saturday ??= SimulateDay(Saturday, 12);

        [Test]
        public void ASimulatedDayFollowsTheClockWithoutAMassLogout()
        {
            Day day = TuesdayRun, weekend = SaturdayRun;
            var report = new StringBuilder("hour  online(Tue)  scheduled  logins  logouts  online(Sat)\n");
            for (int h = 0; h < 24; h++)
                report.AppendLine($"{h:00}:30  {day.OnlineAt30[h],6}  {day.ScheduledAt30[h],9}  {day.Logins[h],6}  {day.Logouts[h],7}  {weekend.OnlineAt30[h],6}");
            report.AppendLine($"worst minute: {day.WorstLogoutMinute} logouts; worst 10-minute drop: {day.WorstTenMinuteDrop}; " +
                              $"still finishing at 05:30: {day.Overstaying}; grouped bots logged out: {day.GroupedLogouts}");
            TestContext.Progress.WriteLine(report.ToString());

            double prime = Enumerable.Range(19, 4).Average(h => day.OnlineAt30[h]);
            double trough = Enumerable.Range(3, 4).Average(h => day.OnlineAt30[h]);
            Assert.That(trough, Is.LessThan(prime * 0.3), "the night is quiet");
            Assert.That(trough, Is.InRange(150, 700), "a few hundred online before dawn");
            Assert.That(day.OnlineAt30[5], Is.LessThan(day.ScheduledAt30[5] * 1.4 + 50), "the night crowd follows the schedule (bots finishing parties included)");
            Assert.That(day.OnlineAt30[21], Is.GreaterThan(Roster * 0.85), "the evening fills up");
            Assert.That(day.GroupedLogouts, Is.Zero, "a bot in a party (or a raid) finishes before it logs out");
            Assert.That(day.WorstLogoutMinute, Is.LessThanOrEqualTo(Schedule.MaxLogoutsPerScan * 12), "logouts are paced");
            Assert.That(day.WorstTenMinuteDrop, Is.LessThan(Roster / 20), "no mass logout: under 5% of the roster in any ten minutes");
            Assert.That(Enumerable.Range(10, 8).Average(h => weekend.OnlineAt30[h]),
                Is.GreaterThan(Enumerable.Range(10, 8).Average(h => day.OnlineAt30[h]) * 1.2), "a Saturday daytime is busier");
        }

        [Test]
        public void AStartBeforeDawnRampsTheSchedulesNumberNotTheRoster()
        {
            DateTime dawn = Tuesday.AddHours(5);
            int wanted = (int)Math.Round(RosterShare(dawn) * Roster);
            // 1.1.2 ramped the whole roster and capped it at the schedule's number: 33% of 2,700 in five minutes is far more than
            // the few hundred a 5 a.m. schedule wants, so they all came in within two minutes.
            Assert.That(AutonomousPopulationRamp.DesiredActiveCount(true, wanted, 15, TimeSpan.FromMinutes(1)), Is.LessThan(wanted / 10 + 1));
            Assert.That(AutonomousPopulationRamp.DesiredActiveCount(true, wanted, 15, TimeSpan.FromMinutes(15)), Is.EqualTo(wanted));
            Day day = SimulateDay(dawn, 13);
            Assert.That(day.OnlineAt30[5], Is.InRange(wanted * 0.9, wanted * 1.1));
        }

        #endregion

        #region /faction by the hour

        /// <summary>
        /// One hour of a realm's ambient /faction with <paramref name="realmBots"/> online at <paramref name="hour"/>: each bot checks
        /// every 15-35 s as BotBrain does, is free 55% of the time and passes AutonomousBotChat.ShouldSpeak with its personality's
        /// rate, the budget's pressure and the realm's regulars; /faction is offered to a regular, to anyone 8% of the time or
        /// when it runs under 80% of its target, and starts through the real ChannelMeter gate. Each line draws 0-2 replies
        /// that wait for room under the ceiling (FactionRoom), and stories from deaths and wipes come at about one every six
        /// minutes at 1,000 bots. Lines in the hour after a ten-minute warm-up.
        /// </summary>
        public static int FactionHour(int realmBots, int hour, bool clock, double rate, int seed)
        {
            var random = new Random(seed);
            var meter = new Budget.ChannelMeter();
            Budget.Settings settings = Budget.Current;
            var band = Budget.Band(settings, Budget.Channel.Faction, realmBots, hour, clock, rate);
            long[] ids = Enumerable.Range(0, realmBots).Select(i => 50_000L + i).ToArray();
            BotPersonalityType[] types = ids.Select(AutonomousBotPersonality.TypeFor).ToArray();
            double[] rates = ids.Select(AutonomousBotPersonality.RegularFactor).ToArray();
            long[] nextCheck = ids.Select(_ => (long)random.Next(35_000)).ToArray();
            long[] lastSpoke = ids.Select(_ => -10 * 60_000L).ToArray();
            var replies = new List<long>();
            long start = 10 * 60_000, end = start + 60 * 60_000;
            int lines = 0;
            void Say(long now)
            {
                meter.Count(Budget.Channel.Faction, 1, now);
                if (now >= start && now < end)
                    lines++;
            }
            bool Room(long now) => AutonomousChatExchange.Fits(meter.PerMinute(Budget.Channel.Faction, 1, now), band.Ceiling);
            DateTime clockBase = new(2026, 11, 10, hour, 0, 0, DateTimeKind.Utc);
            for (long now = 0; now < end; now += 1_000)
            {
                for (int i = replies.Count - 1; i >= 0; i--)
                    if (replies[i] <= now)
                    {
                        if (Room(now))
                            Say(now);
                        replies.RemoveAt(i);
                    }
                if (now % 60_000 == 0 && random.NextDouble() < 0.17 * realmBots / 1_000.0)
                    for (int s = 0; s < 3; s++)
                        replies.Add(now + 2_000 + s * 6_000);
                double pressure = Budget.Pressure(meter.PerMinute(Budget.Channel.Faction, 1, now), band.Target);
                DateTime utc = clockBase.AddMilliseconds(now);
                for (int b = 0; b < ids.Length; b++)
                {
                    if (nextCheck[b] > now)
                        continue;
                    nextCheck[b] = now + 15_000 + random.Next(20_001);
                    if (random.NextDouble() >= 0.55)
                        continue;
                    bool regular = AutonomousChatRegulars.IsRegular(ids[b], types[b], now, realmBots);
                    if (!AutonomousBotChat.ShouldSpeak(utc, utc.AddMilliseconds(lastSpoke[b] - now), false, random, rates[b], pressure, regular))
                        continue;
                    if (!regular && random.NextDouble() >= AutonomousChatRegulars.OthersShare &&
                        meter.PerMinute(Budget.Channel.Faction, 1, now) >= AutonomousChatRegulars.OthersBelow * band.Target)
                        continue;
                    if (meter.TryStart(Budget.Channel.Faction, 1, now, band, random) is Budget.Verdict.HoldCeiling or Budget.Verdict.HoldGap)
                        continue;
                    Say(now);
                    lastSpoke[b] = now;
                    int count = AutonomousBotChatCoordinator.BotReplyCount("chat", realmBots, random);
                    for (int r = 0; r < count; r++)
                        replies.Add(now + 3_000 + random.Next(15_000) + r * 4_000);
                }
            }
            return lines;
        }

        [Test]
        public void FactionFollowsTheBotsOnlineSoAQuietHourIsQuiet()
        {
            Day day = TuesdayRun;
            const double rate = 2.0;   // bot_voices_chat_rate's default
            var report = new StringBuilder("/faction lines an hour, one realm, chat rate 2 (model)\nhour  off: bots lines ceiling/h   clock: bots lines ceiling/h\n");
            int[] off = new int[24], clock = new int[24];
            double[] clockCeiling = new double[24];
            for (int h = 0; h < 24; h++)
            {
                int realmBots = day.OnlineAt30[h] / 3;
                off[h] = FactionHour(Roster / 3, h, false, rate, 100 + h);
                clock[h] = FactionHour(realmBots, h, true, rate, 200 + h);
                clockCeiling[h] = Budget.Band(Budget.Current, Budget.Channel.Faction, realmBots, h, true, rate).Ceiling * 60;
                double offCeiling = Budget.Band(Budget.Current, Budget.Channel.Faction, Roster / 3, h, false, rate).Ceiling * 60;
                report.AppendLine($"{h:00}    {Roster / 3,5} {off[h],5} {offCeiling,8:0}   {realmBots,8} {clock[h],5} {clockCeiling[h],8:0}");
            }
            TestContext.Progress.WriteLine(report.ToString());

            double Avg(int[] lines, int from, int count) => Enumerable.Range(from, count).Average(h => lines[h]);
            // Schedule off (the owner's PC): the whole roster all night, /faction at 03:00-05:00 still half the evening's or more.
            Assert.That(Avg(off, 2, 3), Is.GreaterThan(Avg(off, 19, 4) * 0.4), "with the schedule off the night stays busy (as on the PC)");
            // Schedule on: the night is a fraction of the evening, and well under the schedule-off night.
            Assert.That(Avg(clock, 3, 4), Is.LessThan(Avg(clock, 19, 4) * 0.35), "with the clock schedule the night is quiet");
            Assert.That(Avg(clock, 2, 3), Is.LessThan(Avg(off, 2, 3) * 0.6), "and quieter than the schedule-off night");
            Assert.That(Avg(clock, 19, 4), Is.GreaterThan(Avg(off, 19, 4) * 0.8), "the evening is as lively as with every bot on");
            Assert.That(clock.Min(), Is.GreaterThan(0), "never dead: a channel keeps a floor");
            for (int h = 0; h < 24; h++)
                Assert.That(clock[h], Is.LessThanOrEqualTo(clockCeiling[h] * 1.05), $"{h:00}:00 stays under its ceiling");
        }

        #endregion
    }
}
