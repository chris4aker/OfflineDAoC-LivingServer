using System;
using System.IO;
using System.Text.RegularExpressions;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// RvR work 14, step 1a (issue #532; pathing.md 3): an autonomous gamebot in a war zone makes a non-urgent move within
    /// its region only when no player could see either end. The pure rule, the row, and the four call sites. Tests only:
    /// nothing here has been seen in game.
    /// </summary>
    [TestFixture]
    public class UT_RvrUnseenMove532
    {
        private const int View = WorldMgr.VISIBILITY_DISTANCE;

        private static string GameServerRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            return Path.Combine(dir, "source", "server", "GameServer");
        }

        private static string Controller(string file) =>
            File.ReadAllText(Path.Combine(GameServerRoot(), "bots", "autonomous", file));

        [Test]
        public void A_move_waits_while_a_player_sees_either_end()
        {
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, 1_500, 800, -1), Is.True, "the player by the start");
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, 1_500, View + 2_000, 300), Is.True, "the player by the landing");
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, 4_000, View, View), Is.True, "at the edge of sight");
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, 300, 0, 0), Is.True, "a 300-unit floor step in plain view");
        }

        [Test]
        public void A_move_goes_ahead_with_nobody_watching()
        {
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, 1_500, -1, -1), Is.False, "no player in the region");
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, 1_500, View + 1, View + 500), Is.False, "the player too far");
        }

        [Test]
        public void Only_same_region_moves_of_war_bots_that_go_somewhere_wait()
        {
            Assert.That(AutonomousRvrUnseenMove.MustWait(false, true, 1_500, 100, 100), Is.False, "outside a war zone: unchanged");
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, false, 1_500, 100, 100), Is.False, "a region change: unchanged");
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, AutonomousRvrUnseenMove.MinimumDistance - 1, 100, 100),
                Is.False, "a shuffle under the minimum");
            Assert.That(AutonomousRvrUnseenMove.MustWait(true, true, AutonomousRvrUnseenMove.MinimumDistance, 100, 100), Is.True);
        }

        [Test]
        public void Seen_matches_the_teleport_rows_sight()
        {
            foreach (int from in new[] { -1, 0, 500, View, View + 1 })
                foreach (int to in new[] { -1, 0, 500, View, View + 1 })
                    Assert.That(AutonomousRvrUnseenMove.Seen(from) || AutonomousRvrUnseenMove.Seen(to),
                        Is.EqualTo(RvrTeleports.Seen(from, to) != "none"), $"{from}/{to}");
        }

        [Test]
        public void The_held_row()
        {
            Assert.That(AutonomousRvrUnseenMove.Line(7, "Some Bot", 42, eRealm.Hibernia, "keep_route_restart", 200, 1, 2, 3_001,
                    4_002, 3_200, 900, -1),
                Is.EqualTo("RVR_TELEPORT_HELD t=7 bot=\"Some Bot\" id=42 realm=" + RvrRecordText.Realm(eRealm.Hibernia) +
                           " how=keep_route_restart from=200:1,2 to=200:3001,4002 distance=3200 playerFrom=900 playerTo=-1 " +
                           "playerSaw=from holdMs=10000"));
        }

        [Test]
        public void The_non_urgent_moves_ask_the_gate_before_they_move()
        {
            string keep = Controller("AutonomousWorldBotController.KeepTravel.cs");
            string controller = Controller("AutonomousWorldBotController.cs");
            // Each gate stands before that move's MoveInRegion.
            AssertGateBefore(keep, "\"keep_route_restart\"", "bot.MoveInRegion(bot.CurrentRegionID, (int)Math.Round(floor.X)");
            AssertGateBefore(controller, "\"floor_step\"", "bot.MoveInRegion(bot.CurrentRegionID, (int)floor.X, (int)floor.Y");
            AssertGateBefore(controller, "\"listed_pocket\"", "!bot.MoveInRegion(bot.CurrentRegionID, (int)floor.X");
            AssertGateBefore(controller, "\"pocket_hop\"", "? bot.MoveInRegion(escapeRegion");
        }

        [Test]
        public void A_waiting_pocket_escape_never_climbs_to_the_capital_move()
        {
            string controller = Controller("AutonomousWorldBotController.cs");
            int gate = controller.IndexOf("\"pocket_hop\"", StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(0));
            string held = controller.Substring(gate, 600);
            Assert.That(held, Does.Contain("_terminalRouteFailuresInPocket = Math.Max(0, _terminalRouteFailuresInPocket - 1)"),
                "the failure counted for this try is taken back");
            Assert.That(held, Does.Contain("_unseenMoveHoldUntilTick = now + AutonomousRvrUnseenMove.HoldMilliseconds"));
            Assert.That(Regex.IsMatch(controller, @"if \(HoldForUnseenMove\(bot, current, now, destinationChanged\)\)\s+return true;"),
                Is.True, "a held bot stands out the hold instead of planning the failing route again");
            Assert.That(controller, Does.Contain("_unseenMoveHoldUntilTick = 0;"), "a new route order ends the hold");
        }

        private static void AssertGateBefore(string code, string how, string move)
        {
            int gate = code.IndexOf("AutonomousRvrUnseenMove.Holds(", StringComparison.Ordinal);
            int named = code.IndexOf(how, StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThanOrEqualTo(0));
            Assert.That(named, Is.GreaterThan(0), how);
            int moved = code.IndexOf(move, named, StringComparison.Ordinal);
            Assert.That(moved, Is.GreaterThan(named), $"{how} is asked before {move}");
            Assert.That(moved - named, Is.LessThan(1_500), $"{how} guards that move");
        }
    }
}
