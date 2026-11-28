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
        public void Only_active_autonomous_gamebots_in_war_that_are_not_hired_are_covered()
        {
            Assert.That(AutonomousRvrUnseenMove.Covers(true, false, true, true), Is.True, "an autonomous gamebot in a war zone");
            Assert.That(AutonomousRvrUnseenMove.Covers(false, false, true, true), Is.False, "a companion bot or a player: unchanged");
            Assert.That(AutonomousRvrUnseenMove.Covers(true, true, true, true), Is.False, "a hired companion: unchanged");
            Assert.That(AutonomousRvrUnseenMove.Covers(true, false, false, true), Is.False, "not in the world");
            Assert.That(AutonomousRvrUnseenMove.Covers(true, false, true, false), Is.False, "PvE: unchanged");
            string gate = Body(Controller("AutonomousRvrUnseenMove.cs"), "public static bool Covers(GameBot bot)");
            Assert.That(gate, Does.Contain("Covers(bot.IsAutonomousWorldBot, bot.IsHiredCompanion, " +
                                           "bot.ObjectState == GameObject.eObjectState.Active,"));
            Assert.That(gate, Does.Contain("AutonomousRvrRecordScope.InWar(bot)"));
            string holds = Body(Controller("AutonomousRvrUnseenMove.cs"), "public static bool Holds(GameBot bot");
            Assert.That(holds, Does.Match(@"if \(!Covers\(bot\)\)\s+return false;"), "Holds asks Covers first");
            Assert.That(holds, Does.Contain("RvrFights.PlayerDistance(region, x, y)"));
        }

        [Test]
        public void The_gate_measures_the_move_and_asks_each_end_for_players()
        {
            var asked = new System.Collections.Generic.List<(int, int)>();
            int Near(int x, int y)
            {
                asked.Add((x, y));
                return x < 10_000 ? 900 : -1;
            }

            Assert.That(AutonomousRvrUnseenMove.Decide(new(1_000, 2_000, 0), new(1_000, 2_030, 0), Near, out int d, out int pf,
                out int pt), Is.False, "a 30-unit shuffle never waits");
            Assert.That(asked, Is.Empty, "and never looks for players");
            Assert.That((d, pf, pt), Is.EqualTo((30, -1, -1)));

            Assert.That(AutonomousRvrUnseenMove.Decide(new(1_000, 2_000, 0), new(1_000, 2_000, 64), Near, out d, out _, out _),
                Is.True, "the distance is 3D: a 64-unit drop counts");
            Assert.That(d, Is.EqualTo(64));

            asked.Clear();
            Assert.That(AutonomousRvrUnseenMove.Decide(new(20_000, 30_000, 0), new(1_000, 2_000, 0), Near, out _, out pf, out pt),
                Is.True, "the player by the landing");
            Assert.That(asked, Is.EqualTo(new[] { (20_000, 30_000), (1_000, 2_000) }), "the start, then the landing");
            Assert.That((pf, pt), Is.EqualTo((-1, 900)));

            Assert.That(AutonomousRvrUnseenMove.Decide(new(20_000, 30_000, 0), new(20_000, 33_000, 0), Near, out _, out _, out _),
                Is.False, "nobody near either end");
        }

        [Test]
        public void The_non_urgent_moves_ask_the_gate_before_they_move()
        {
            string keep = Controller("AutonomousWorldBotController.KeepTravel.cs");
            string controller = Controller("AutonomousWorldBotController.cs");
            // Each site's own gate (with what it does when held) stands before that site's move.
            AssertGateBefore(keep, @"if \(AutonomousRvrUnseenMove\.Holds\(bot, floor, ""keep_route_restart""\)\)\s+return false;",
                "bot.MoveInRegion(bot.CurrentRegionID, (int)Math.Round(floor.X)");
            AssertGateBefore(controller,
                @"if \(found && AutonomousRvrUnseenMove\.Holds\(bot, floor, ""floor_step""\)\)\s+\{\s+" +
                @"_nextFloorStepTick = GameLoop\.GameLoopTime \+ AutonomousRvrUnseenMove\.HoldMilliseconds;\s+return false;",
                "bot.MoveInRegion(bot.CurrentRegionID, (int)floor.X, (int)floor.Y");
            AssertGateBefore(controller,
                @"ListedPocketReach, out Vector3 floor, leavePocket: true\) \|\|\s+(//[^\n]*\s+)?" +
                @"AutonomousRvrUnseenMove\.Holds\(bot, floor, ""listed_pocket""\) \|\|\s+!bot\.MoveInRegion",
                "!bot.MoveInRegion(bot.CurrentRegionID, (int)floor.X");
            AssertGateBefore(controller,
                @"if \(escapeRegion == bot\.CurrentRegionID &&\s+" +
                @"AutonomousRvrUnseenMove\.Holds\(bot, escape,\s+" +
                @"listedNudge \? ""listed_pocket_escape"" : shortHop \? ""pocket_hop"" : ""pocket_escape""\)\)\s+\{\s+" +
                @"HoldPocketEscape\(bot, now, escapeName\);\s+return true;",
                "? bot.MoveInRegion(escapeRegion");
        }

        [Test]
        public void A_waiting_pocket_escape_never_climbs_to_the_capital_move()
        {
            string controller = Controller("AutonomousWorldBotController.cs");
            string held = Body(controller, "private void HoldPocketEscape(");
            Assert.That(held, Does.Contain("_terminalRouteFailuresInPocket = Math.Max(0, _terminalRouteFailuresInPocket - 1)"),
                "the failure counted for this try is taken back");
            Assert.That(held, Does.Contain("_unseenMoveHoldUntilTick = now + AutonomousRvrUnseenMove.HoldMilliseconds"));
            Assert.That(Regex.IsMatch(controller, @"if \(HoldForUnseenMove\(bot, current, now, destinationChanged\)\)\s+return true;"),
                Is.True, "a held bot stands out the hold instead of planning the failing route again");
        }

        [Test]
        public void A_held_escape_is_not_counted_again_during_the_hold()
        {
            string escape = Body(Controller("AutonomousWorldBotController.cs"), "private bool TryEscapeTerminalRoutePocket(");
            Match early = Regex.Match(escape, @"if \(now < _unseenMoveHoldUntilTick\)\s+return true;");
            Assert.That(early.Success, Is.True, "the hold's early return");
            int counted = escape.IndexOf("_terminalRouteFailuresInPocket = repeated", StringComparison.Ordinal);
            Assert.That(counted, Is.GreaterThan(early.Index), "it comes before the failure is counted");
        }

        [Test]
        public void A_held_listed_pocket_nudge_takes_the_nudge_not_the_capital_once_the_player_has_left()
        {
            string controller = Controller("AutonomousWorldBotController.cs");
            string escape = Body(controller, "private bool TryEscapeTerminalRoutePocket(");
            // The walled branch takes the listed pocket's own floor as a move within the region; the capital is only the
            // other branch, so a bot with that floor never rides home, whether the player is still there or has left.
            Match nudge = Regex.Match(escape,
                @"if \(walledPocket && TryListedPocketFloor\(bot, current, out Vector3 pocketFloor\)\)\s+\{\s+" +
                @"escapeRegion = bot\.CurrentRegionID;\s+escape = pocketFloor;\s+escapeName = [^\n]+\s+listedNudge = true;\s+\}\s+else\s+\{");
            Assert.That(nudge.Success, Is.True, "a covered bot in a listed pocket takes the floor, in its own region");
            int capital = escape.IndexOf("AutonomousStuckWatchdog.SafeCapitalFor(bot.Realm)", StringComparison.Ordinal);
            Assert.That(capital, Is.GreaterThan(nudge.Index + nudge.Length), "the capital is looked up only in the else branch");
            int gate = escape.IndexOf("listedNudge ? \"listed_pocket_escape\"", StringComparison.Ordinal);
            Assert.That(gate, Is.GreaterThan(capital), "the shared gate asks about the nudge after the choice");
            Assert.That(escape, Does.Not.Contain("ListedPocketFloorHeld"), "no separate hold that the pocket check can outrace");

            string floor = Body(controller, "private static bool TryListedPocketFloor(");
            Assert.That(floor, Does.Contain("AutonomousRvrUnseenMove.Covers(bot) &&"), "only covered bots search for the floor");
            Assert.That(floor, Does.Contain("current, ListedPocketReach,"), "the same reach as the nudge");
            Assert.That(floor, Does.Contain("leavePocket: true"), "the same floor as the nudge");
            Assert.That(Body(controller, "private static bool TryLeaveListedPocket("),
                Does.Contain("new Vector3(bot.X, bot.Y, bot.Z), ListedPocketReach, out Vector3 floor, leavePocket: true"));

            // The same nudge, asked of the gate's rule on two turns: held while the player stands by, taken once gone.
            System.Numerics.Vector3 at = new(10_000, 20_000, 0), pocketExit = new(10_300, 20_000, 0);
            int player = 2_000;
            Func<int, int, int> lookup = (_, _) => player;
            Assert.That(AutonomousRvrUnseenMove.Decide(at, pocketExit, lookup, out _, out _, out _), Is.True, "player in sight: held");
            player = -1;
            Assert.That(AutonomousRvrUnseenMove.Decide(at, pocketExit, lookup, out _, out _, out _), Is.False,
                "player gone: the nudge goes");
            player = 2_000;
            Assert.That(AutonomousRvrUnseenMove.Decide(at, new(10_030, 20_000, 0), lookup, out _, out _, out _), Is.False,
                "a floor under 64 units away is taken at once, player or not");
        }

        [Test]
        public void Holds_waits_only_when_Decide_says_so()
        {
            string holds = Body(Controller("AutonomousRvrUnseenMove.cs"), "public static bool Holds(");
            Assert.That(holds, Does.Match(@"if \(!Covers\(bot\)\)\s+return false;"));
            Assert.That(holds, Does.Match(@"if \(!Decide\(new Vector3\(bot\.X, bot\.Y, bot\.Z\), to,[^;]*\)\)\s+return false;"),
                "a move Decide lets go is never held");
        }

        [Test]
        public void The_hold_ends_when_the_goal_moves_or_a_new_route_order_starts()
        {
            string controller = Controller("AutonomousWorldBotController.cs");
            Assert.That(Body(controller, "private bool HoldForUnseenMove("),
                Does.Match(@"if \(destinationChanged\)\s+\{\s+_unseenMoveHoldUntilTick = 0;\s+return false;"), "a moved goal");
            Assert.That(Body(controller, "private void ResetRouteOrderState("), Does.Contain("_unseenMoveHoldUntilTick = 0;"),
                "a new route order");
        }

        /// <summary>The text of the member whose declaration starts with <paramref name="signature"/>, up to its closing brace
        /// (or the end of an expression body; any line endings).</summary>
        private static string Body(string code, string signature)
        {
            int start = code.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
            Assert.That(code.IndexOf(signature, start + 1, StringComparison.Ordinal), Is.LessThan(0), $"{signature} is unique");
            int open = code.IndexOf('{', start);
            int arrow = code.IndexOf("=>", start, StringComparison.Ordinal);
            if (arrow >= 0 && arrow < open)
                return code.Substring(start, code.IndexOf(';', arrow) - start + 1);
            int depth = 0;
            for (int i = open; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}' && --depth == 0)
                    return code.Substring(start, i - start + 1);
            }
            Assert.Fail($"{signature} has no end");
            return null;
        }

        private static void AssertGateBefore(string code, string gatePattern, string move)
        {
            MatchCollection gates = Regex.Matches(code, gatePattern);
            Assert.That(gates.Count, Is.EqualTo(1), gatePattern);
            int gateEnd = gates[0].Index + gates[0].Length;
            int moved = code.IndexOf(move, gates[0].Index, StringComparison.Ordinal);
            Assert.That(moved, Is.GreaterThanOrEqualTo(gates[0].Index), $"{gatePattern} is asked before {move}");
            Assert.That(moved - gateEnd, Is.LessThan(600), $"{gatePattern} guards that move");
        }
    }
}
