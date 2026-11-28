using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using L = DOL.GS.AutonomousRvrEventLayer;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #530 step 5b: groups inside the battleground muster. The party plan, the rosters read from the event layer's force
    /// records, and the muster party's end. Unit tests only (the harness case is UT_RvrFightRehearsal scenario 4); nothing
    /// here has been seen in game. Autonomous gamebots only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BattlegroundMusterParties530
    {
        [SetUp]
        public void Reset() => L.ResetForceIntents();

        private static string Code(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [TestCase(new int[0], 0, new int[0], new int[0])]
        [TestCase(new int[0], 2, new int[0], new int[0])]
        [TestCase(new int[0], 3, new int[0], new[] { 3 })]
        [TestCase(new int[0], 8, new int[0], new[] { 8 })]
        [TestCase(new int[0], 9, new int[0], new[] { 5, 4 })]
        [TestCase(new int[0], 14, new int[0], new[] { 7, 7 })]
        [TestCase(new int[0], 17, new int[0], new[] { 6, 6, 5 })]
        [TestCase(new[] { 5 }, 2, new[] { 2 }, new int[0])]
        [TestCase(new[] { 5 }, 4, new[] { 3 }, new int[0])]
        [TestCase(new[] { 6, 8 }, 6, new[] { 2, 0 }, new[] { 4 })]
        [TestCase(new[] { 7 }, 3, new[] { 1 }, new int[0])]
        public void The_party_plan(int[] parties, int free, int[] joins, int[] newParties)
        {
            L.MusterPartyPlan plan = L.PlanMusterParties(parties, free);
            Assert.That(plan.Joins, Is.EqualTo(joins), "existing parties are filled first, to eight");
            Assert.That(plan.NewParties, Is.EqualTo(newParties), "three or more left: even parties of at most eight");
            Assert.That(plan.NewParties.All(size => size >= L.MusterPartyMinimum && size <= L.MusterPartySize), Is.True);
            Assert.That(plan.Placed, Is.LessThanOrEqualTo(free));
        }

        [Test]
        public void Every_plan_keeps_the_sizes()
        {
            for (int free = 0; free <= 70; free++)
                foreach (int[] parties in new[] { Array.Empty<int>(), new[] { 2 }, new[] { 8, 3 } })
                {
                    L.MusterPartyPlan plan = L.PlanMusterParties(parties, free);
                    for (int i = 0; i < parties.Length; i++)
                        Assert.That(parties[i] + plan.Joins[i], Is.LessThanOrEqualTo(8), $"free={free}");
                    Assert.That(plan.NewParties.All(size => size is >= 3 and <= 8), Is.True, $"free={free}");
                    Assert.That(free - plan.Placed, Is.LessThan(3), $"free={free}: at most two left solo");
                }
        }

        [Test]
        public void The_rosters_are_the_fresh_solo_musters()
        {
            L.IntentView view = new(L.Intent.Roam, false);
            L.ForceIntent Muster(string force) => L.Decide(force, view, null, L.DecideBattleground(9, false, false, true, 1).Proposal, 1);
            L.ForceIntent Wave(string force) => L.Decide(force, view, null, L.DecideBattleground(9, false, false, false, 1).Proposal, 1);
            L.WriteForceIntent("bg:rvr-11", null, Muster("bg:rvr-11"), null, 100_000);
            L.WriteForceIntent("bg:rvr-12", null, Muster("bg:rvr-12"), null, 100_000);
            L.WriteForceIntent("bg:rvr-13", null, Wave("bg:rvr-13"), null, 100_000);          // the wave went: attacking
            L.WriteForceIntent("bg:rvr-14", null, Muster("bg:rvr-14"), null, 60_000);         // stale
            L.WriteForceIntent("bg:bgmuster-alb-x-001", null, Muster("bg:bgmuster-alb-x-001"), null, 100_000); // a party
            L.WriteForceIntent("rvr-15", null, L.Decide("rvr-15", view, null,
                new(L.IntentSource.Plan, L.Intent.AssaultKeep, "rvr-keep-3", true, -1, "plan.assaultKeep"), 1), null, 100_000); // frontier
            var rosters = L.BattlegroundMusterRosters(110_000);
            Assert.That(rosters.Count, Is.EqualTo(1));
            Assert.That(rosters[0].ObjectiveId, Is.EqualTo("bg-keep-9"));
            Assert.That(rosters[0].MemberIds, Is.EqualTo(new long[] { 11, 12 }));
        }

        [Test]
        public void A_bots_own_tour_is_read_from_its_record()
        {
            DateTime now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
            var record = new OfflineWorldBotRecord { ObjectiveKind = "Battleground", ObjectiveExpiresUtc = now.AddMinutes(12).ToString("O") };
            Assert.That(AutonomousBotGroupCoordinator.TourRemaining(record, now), Is.EqualTo(TimeSpan.FromMinutes(12)));
            record.ObjectiveKind = "RvR";
            Assert.That(AutonomousBotGroupCoordinator.TourRemaining(record, now), Is.Null, "only a battleground tour");
            record.ObjectiveKind = "Battleground";
            record.ObjectiveExpiresUtc = string.Empty;
            Assert.That(AutonomousBotGroupCoordinator.TourRemaining(record, now), Is.Null, "no expiry read: never put in a party");
            Assert.That(AutonomousBotGroupCoordinator.TourRemaining(null, now), Is.Null);
        }

        [Test]
        public void A_muster_party_is_a_trip_that_hands_out_no_new_goals()
        {
            string trip = Code("bots", "autonomous", "AutonomousBotGroupCoordinator.BattlegroundTrip.cs");
            string coordinator = Code("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            string controller = Code("bots", "autonomous", "AutonomousWorldBotController.Battleground.cs");
            Assert.Multiple(() =>
            {
                Assert.That(trip, Does.Contain("newGoals: !trip.FromMuster"));
                // The call runs in the coordinator's pulse, beside the siege parties: a live statement on its own line,
                // so commenting it out (// or /* */) fails this pin.
                string live = Regex.Replace(coordinator, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
                Assert.That(Regex.IsMatch(live,
                        @"(?m)^[ \t]*FormSiegeParties\(\);[ \t]*\n[ \t]*//[^\n]*\n[ \t]*FormBattlegroundMusterParties\(\);[ \t]*$"), Is.True,
                    "the muster parties are formed in the pulse, after the siege parties");
                Assert.That(controller, Does.Contain("forceId: \"bg:\" + (party ? _groupDirective.GroupId : RvrForceId(bot))"),
                    "a battleground party is one force");
                Assert.That(Regex.IsMatch(controller, @"writer: party \? _groupDirective\.Leader == null \|\| _groupDirective\.Leader == bot"), Is.True,
                    "written by its leader");
            });
        }

        [Test]
        public void Companions_and_siege_crews_are_never_taken()
        {
            string muster = Regex.Replace(Code("bots", "autonomous", "AutonomousBotGroupCoordinator.MusterParties.cs"), @"\s+", " ");
            int start = muster.IndexOf("private static bool FreeForMusterParty(", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThan(0));
            string free = muster.Substring(start, muster.IndexOf(';', start) - start);
            Assert.Multiple(() =>
            {
                // As RvrTacticsScope: autonomous gamebots only, never a temporary helper or a hired companion.
                Assert.That(free, Does.Contain("IsAutonomousWorldBot: true"));
                Assert.That(free, Does.Contain("IsTemporaryGroupHelper: false"));
                Assert.That(free, Does.Contain("IsHiredCompanion: false"));
                Assert.That(free, Does.Contain("!BotDutyLeases.IsSpokenForByPlayer(bot)"));
                // A siege crew keeps its engine job (left out on purpose; it records the muster as any gatherer, step 5a).
                Assert.That(free, Does.Contain("!IsSiegeCrew(bot)"));
                Assert.That(muster, Does.Contain(
                    "public static bool IsSiegeCrew(GameBot bot) => BotSiegeRuntime.Assigned(bot) || AutonomousSiegeOwnership.All(bot).Length > 0;"));
            });
        }
    }
}
