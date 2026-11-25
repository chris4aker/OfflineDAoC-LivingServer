using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Issue #625 set B: the long-running goals' rows (REALM_LEVEL_SPREAD, GROUP_ENDED, ECONOMY_PULSE, STUCK_HOTSPOT):
    /// each is written with its fields, carries no names, and stays low-volume at about 3,000 gamebots. Tests only.
    /// </summary>
    [TestFixture]
    public sealed class UT_LivingLog625
    {
        private static readonly DateTime H10 = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

        // ---- REALM_LEVEL_SPREAD ----

        [Test]
        public void LevelSpreadTakesABaselineThenCountsGainsNewRerolledAndGone()
        {
            var spread = new RealmLevelSpread();
            var first = new[]
            {
                new RealmLevelSpread.Entry(1, 1, 20, 2), new RealmLevelSpread.Entry(2, 1, 22, 3),
                new RealmLevelSpread.Entry(3, 2, 49, 4), new RealmLevelSpread.Entry(4, 3, 30, 5),
            };
            IReadOnlyList<string> baseline = spread.Hour(H10, first);
            Assert.That(baseline, Has.All.Contains(" baseline=1"));
            Assert.That(baseline, Has.Some.EqualTo(
                "REALM_LEVEL_SPREAD hour=2026-11-25T10:00Z realm=Albion band=20-24 bots=2 gained=0 levelled=0 new=0 rerolled=0 gone=0 baseline=1"));

            var second = new[]
            {
                new RealmLevelSpread.Entry(1, 1, 23, 2),   // +3
                new RealmLevelSpread.Entry(2, 1, 5, 3),    // re-rolled: level fell
                new RealmLevelSpread.Entry(3, 2, 50, 9),   // re-rolled: class changed
                new RealmLevelSpread.Entry(5, 3, 1, 5),    // new; bot 4 is gone
            };
            IReadOnlyList<string> hour = spread.Hour(H10.AddHours(1), second);
            Assert.That(hour, Has.Some.EqualTo(
                "REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Albion band=20-24 bots=1 gained=3 levelled=1 new=0 rerolled=0 gone=0 baseline=0"));
            Assert.That(hour, Has.Some.StartWith("REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Albion band=05-09 bots=1 gained=0 levelled=0 new=0 rerolled=1 "));
            Assert.That(hour, Has.Some.StartWith("REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Midgard band=50-54 bots=1 gained=0 levelled=0 new=0 rerolled=1 "));
            Assert.That(hour, Has.Some.StartWith("REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Hibernia band=00-04 bots=1 gained=0 levelled=0 new=1 "));
            Assert.That(hour, Has.Some.StartWith("REALM_LEVEL_SPREAD hour=2026-11-25T11:00Z realm=Hibernia band=30-34 bots=0 ").And.Contain(" gone=1 "));
        }

        [Test]
        public void LevelSpreadIsAtMostThirtyThreeRowsAnHourForThreeThousandBots()
        {
            var spread = new RealmLevelSpread();
            var random = new Random(625);
            RealmLevelSpread.Entry[] Roster() => Enumerable.Range(1, 3_000)
                .Select(id => new RealmLevelSpread.Entry(id, 1 + id % 3, 1 + random.Next(50), id % 11)).ToArray();
            spread.Hour(H10, Roster());
            IReadOnlyList<string> rows = spread.Hour(H10.AddHours(1), Roster());
            Assert.That(rows.Count, Is.LessThanOrEqualTo(33), "3 realms x 11 bands");
            Assert.That(rows.Sum(row => int.Parse(Regex.Match(row, @" bots=(\d+)").Groups[1].Value)), Is.EqualTo(3_000));
        }

        // ---- STUCK_HOTSPOT ----

        [Test]
        public void HotspotsMergeKindsAndBotsInFiveHundredUnitCells()
        {
            var hotspots = new StuckHotspots();
            hotspots.Note("stuck15m", 1, 51, 23_740, 41_100);   // cell 47,82 -> 23500,41000
            hotspots.Note("stall45m", 2, 51, 23_300, 40_800);   // the same cell
            hotspots.Note("seam", 2, 51, 23_499, 41_249);       // the same cell, the same bot again
            hotspots.Note("position", 3, 100, 1_000, 1_000);
            hotspots.Note("not-a-kind", 4, 1, 0, 0);
            IReadOnlyList<string> rows = hotspots.TakeHour(H10);
            Assert.That(rows[0], Is.EqualTo(
                "STUCK_HOTSPOT hour=2026-11-25T10:00Z rank=1 region=51 x=23500 y=41000 grid=500 total=3 bots=2 stuck15m=1 stall45m=1 position=0 seam=1"));
            Assert.That(rows[1], Does.StartWith("STUCK_HOTSPOT hour=2026-11-25T10:00Z rank=2 region=100 x=1000 y=1000 "));
            Assert.That(rows[^1], Is.EqualTo("STUCK_HOTSPOT hour=2026-11-25T10:00Z rank=0 cells=2 regions=2 total=4 dropped=0 grid=500"));
            Assert.That(hotspots.TakeHour(H10.AddHours(1)).Single(), Does.Contain(" rank=0 cells=0 "), "a fresh hour");
            Assert.That(StuckHotspots.KindOfMarker("AUTONOMOUS_STUCK_15M_RECOVERY"), Is.EqualTo("stuck15m"));
            Assert.That(StuckHotspots.KindOfMarker("AUTONOMOUS_GOAL_STALL_45M_RECOVERY"), Is.EqualTo("stall45m"));
            Assert.That(StuckHotspots.KindOfMarker("AUTONOMOUS_POSITION_RECOVERY"), Is.EqualTo("position"));
        }

        [Test]
        public void HotspotsStayAtElevenRowsAnHourHoweverManyBotsAreStuck()
        {
            var hotspots = new StuckHotspots();
            var random = new Random(9);
            for (int i = 0; i < 30_000; i++)
                hotspots.Note(StuckHotspots.Kinds[i % 4], i % 3_000, random.Next(1, 300), random.Next(0, 800_000), random.Next(0, 800_000));
            IReadOnlyList<string> rows = hotspots.TakeHour(H10);
            Assert.That(rows.Count, Is.EqualTo(StuckHotspots.Top + 1));
            Assert.That(rows[^1], Does.Contain($" cells={StuckHotspots.MaxCells} ").And.Not.Contain(" dropped=0 "), "the cell cap holds");
        }

        // ---- GROUP_ENDED ----

        [Test]
        public void GroupEndedRowCarriesItsFieldsAndNoNames()
        {
            string line = GroupEndedLog.Format(new GroupEndedLog.Row
            {
                Sequence = 7, Every = 1, Dropped = 0, Group = "albion-ab12-007", Realm = "Albion", Objective = "GroupPve",
                Reason = "member_left", Cause = "too_few", LastLeft = "logout", Phase = "Grinding \"camp\"", TaskStarted = true,
                HasCamp = true, DurationSeconds = 2710, SizeStart = 6, SizeEnd = 1, AliveEnd = 1,
                LeftBy = new Dictionary<string, int> { ["other"] = 2, ["logout"] = 3, ["dead"] = 0 },
                Wipes = 1, GroupChanged = [0, 0, 6, 1, 4], Open = 1, Hosted = false,
            });
            Assert.That(line, Is.EqualTo(
                "GROUP_ENDED seq=7 every=1 dropped=0 group=albion-ab12-007 realm=Albion objective=GroupPve reason=member_left " +
                "cause=too_few last_left=logout phase=\"Grinding 'camp'\" task_started=1 camp=1 dur_s=2710 size_start=6 size_end=1 " +
                "alive_end=1 left=5 left_by=logout:3,other:2 wipes=1 gc=assembling:6,meetup_plan:1,casualty:4 gc_total=11 open=1 hosted=0"));
        }

        [Test]
        public void EveryCoordinatorEndReasonHasAStableClass()
        {
            Assert.That(GroupEndedLog.Classify(AutonomousBotGroupCoordinator.SharedTaskExpiredReason), Is.EqualTo(("finished", "expired")));
            Assert.That(GroupEndedLog.Classify("anything", "released/raid"), Is.EqualTo(("released", "raid")));
            Assert.That(GroupEndedLog.Classify("Party disbanded", "disbanded/logout"), Is.EqualTo(("disbanded", "logout")));
            Assert.That(GroupEndedLog.Classify("something new"), Is.EqualTo(("other", "unclassified")));

            // Every literal reason FinishGroupTask is given in the coordinator classifies (a new one must be added).
            string folder = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "GameServer", "bots", "autonomous");
            var reasons = new List<string>();
            foreach (string file in Directory.GetFiles(folder, "AutonomousBotGroupCoordinator*.cs"))
            {
                string source = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(source, @"FinishGroupTask\(\w+,\s*\$?""([^""{]+)"))
                    reasons.Add(match.Groups[1].Value);
                foreach (Match match in Regex.Matches(source, @"\?\s*""(The PvE party lost[^""]*|Fewer than two active[^""]*|The group roster[^""]*)"""))
                    reasons.Add(match.Groups[1].Value);
            }
            Assert.That(reasons.Count, Is.GreaterThanOrEqualTo(20), "the scan found the coordinator's reasons");
            // A capture cut short by an interpolation ("Leader {name} failed ...") is checked whole below.
            foreach (string reason in reasons.Where(reason => reason.Trim().Length >= 12))
                Assert.That(GroupEndedLog.Classify(reason).Reason, Is.Not.EqualTo("other"), reason);
            // Interpolated reasons whose text starts with a name ("{bot.Name} left the locked ...").
            Assert.That(GroupEndedLog.Classify("Probe left the locked PvE party and no viable core roster remained"),
                Is.EqualTo(("member_left", "no_core")));
            Assert.That(GroupEndedLog.Classify("Verified individual recovery for Probe: stuck"), Is.EqualTo(("recovery", "member_stuck")));
            Assert.That(GroupEndedLog.Classify("Leader Probe failed to reach Castle Sauvage within the 20-minute staging window"),
                Is.EqualTo(("meetup", "leader_late")));
        }

        [Test]
        public void GroupChangedIsTalliedPerGroupAndTakenOnce()
        {
            GroupEndedLog.ResetForTests();
            GroupEndedLog.CountGroupChanged("g1", GroupChangeCause.Assembling);
            GroupEndedLog.CountGroupChanged("g1", GroupChangeCause.Assembling);
            GroupEndedLog.CountGroupChanged("g1", GroupChangeCause.Joined);
            GroupEndedLog.CountGroupChanged("", GroupChangeCause.Casualty);
            int[] tally = GroupEndedLog.TakeGroupChanged("g1");
            Assert.That(tally[(int)GroupChangeCause.Assembling], Is.EqualTo(2));
            Assert.That(tally[(int)GroupChangeCause.Joined], Is.EqualTo(1));
            Assert.That(GroupEndedLog.TakeGroupChanged("g1").Sum(), Is.Zero, "taken once");
            Assert.That(GroupEndedLog.PendingTallies, Is.Zero);
        }

        [Test]
        public void GroupEndedIsCappedAtThirtyAMinute()
        {
            // About 300-600 groups end an hour at 3,000 gamebots (5-8 bot parties, 30-90 minute tasks); the cap is 1,800.
            var sampler = new KillXpSampler(GroupEndedLog.MaxRowsPerMinute);
            int taken = 0;
            for (int i = 0; i < 1_000; i++)
                if (sampler.Take(1, 5_000 + i, out _, out _))
                    taken++;
            Assert.That(taken, Is.EqualTo(30), "a burst of 1,000 ends in one minute writes 30 rows");
            Assert.That(sampler.Take(1, 70_000, out _, out long dropped), Is.True);
            Assert.That(dropped, Is.EqualTo(970), "the next row says how many the cap skipped");
            Assert.That(LivingLogProperties.GroupEndedSampleEvery, Is.EqualTo(1), "on by default");
            Assert.That(LivingLogProperties.HourlyRows, Is.True, "on by default");
        }

        // ---- ECONOMY_PULSE ----

        [Test]
        public void SlotBandsFollowTheInventorySlots()
        {
            Assert.That(EconomyPulse.SlotBand((int)eInventorySlot.TwoHandWeapon), Is.EqualTo(1));
            Assert.That(EconomyPulse.SlotBand((int)eInventorySlot.TorsoArmor), Is.EqualTo(2));
            Assert.That(EconomyPulse.SlotBand((int)eInventorySlot.RightRing), Is.EqualTo(3));
            Assert.That(EconomyPulse.SlotBand(0), Is.EqualTo(0));
            Assert.That(EconomyPulse.SlotBand(-1), Is.EqualTo(0));
        }

        [Test]
        public void EconomyPulseHasListingsSalesAndCoinBySource()
        {
            var flows = new EconomyFlowLedger { BotInfo = id => (1, 30) };
            flows.Record(1, 500, EconomyFlowKind.LootCoin, "GameBot.cs", "x");
            flows.Record(1, 200, EconomyFlowKind.VendorSale, "x.cs", "x");
            flows.Record(1, -80, EconomyFlowKind.Repair, "x.cs", "x");
            flows.Record(1, -30, EconomyFlowKind.Healer, "x.cs", "x");
            flows.Record(1, 40, EconomyFlowKind.BeggarGift, "x.cs", "x");
            flows.Record(1, 999, EconomyFlowKind.ExchangeSale, "x.cs", "x"); // counted from the sale seam instead
            flows.OnSaleCommitted(new RealmExchangeSaleLedger.Sale(1, "sword", 1, 1_000, "offlinebot:1", "S", "player", "B",
                H10, 0, false, (int)eInventorySlot.RightHandWeapon));
            flows.OnSaleCommitted(new RealmExchangeSaleLedger.Sale(1, "sword", 1, 3_000, "player", "S", "offlinebot:2", "B",
                H10, 0, false, (int)eInventorySlot.RightHandWeapon));
            flows.OnSaleCommitted(new RealmExchangeSaleLedger.Sale(1, "ring", 1, 700, "player", "S", "player2", "B", H10));
            var supply = new SupplyBuilder()
                .Listing(1, 100, true, (int)eInventorySlot.HeadArmor)
                .Listing(1, 300, false, (int)eInventorySlot.HeadArmor)
                .Listing(1, 900, true, (int)eInventorySlot.HeadArmor)
                .Listing(2, 50, true)
                .Build(H10);
            IReadOnlyList<string> lines = EconomyPulse.Lines(H10, supply, flows.TakeWindow());
            Assert.That(lines.Count, Is.EqualTo(3), "one row per realm an hour");
            string albion = lines[0];
            Assert.That(albion, Does.StartWith("ECONOMY_PULSE hour=2026-11-25T10:00Z realm=Albion listings=3 bot_listings=2 "));
            Assert.That(albion, Does.Contain(" list_armor=3 list_armor_med=300 "));
            Assert.That(albion, Does.Contain(" sold=3 sold_copper=4700 tax=0 "));
            Assert.That(albion, Does.Contain(" sold_weapon=2 sold_weapon_med=1000 "), "lower middle of an even count");
            Assert.That(albion, Does.Contain(" sold_other=1 sold_other_med=700 "));
            Assert.That(albion, Does.Contain(" loot_in=500 loot_out=0 vendor_in=200 vendor_out=0 exchange_in=1000 exchange_out=3000 "));
            Assert.That(albion, Does.Contain(" repair_in=0 repair_out=80 upkeep_in=0 upkeep_out=30 "));
            Assert.That(albion, Does.EndWith(" other_in=40 other_out=0 coin_in=1740 coin_out=3110"));
            Assert.That(lines[1], Does.Contain("realm=Midgard listings=1 bot_listings=1 list_other=1 list_other_med=50 "));
            Assert.That(lines, Has.None.Contains("\"S\"").And.None.Contains("sword"), "no names or items");
        }

        [Test]
        public void TheLedgerWritesThePulseAtEachHourButNotAtTheBaseline()
        {
            var pulses = new List<string>();
            var source = new Source();
            EconomyLedger ledger = EconomyLedger.CreateForTests();
            DateTime now = H10.AddMinutes(30);
            ledger.Clock = () => now;
            ledger.Write = _ => { };
            ledger.PulseWrite = pulses.Add;
            ledger.Enabled = () => true;
            ledger.Source = source;
            try
            {
                ledger.Load();
                ledger.WaitForSnapshots();
                Assert.That(pulses, Is.Empty, "the baseline writes no pulse");
                now = H10.AddHours(1).AddSeconds(1);
                ledger.Pulse(1_000);
                ledger.WaitForSnapshots();
                Assert.That(pulses.Count, Is.EqualTo(3));
                Assert.That(pulses[0], Does.StartWith("ECONOMY_PULSE hour=2026-11-25T11:00Z realm=Albion listings=1 "));
            }
            finally
            {
                ledger.ResetForTests();
            }
        }

        private sealed class Source : IEconomySupplySource
        {
            public void Read(SupplyBuilder builder, Action closeWindow)
            {
                closeWindow();
                builder.Bot(1, 1, 30, 1_000).Listing(1, 400, true, (int)eInventorySlot.Cloak);
            }

            public void WriteRows(IReadOnlyList<OfflineEconomyLedgerRecord> rows) { }
        }
    }
}
