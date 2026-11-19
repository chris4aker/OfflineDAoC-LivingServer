"""Tests of check_overnight_run.py on small synthetic server logs.

    python -m unittest discover -s source/tools/tests -v
"""
import contextlib
import datetime as dt
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_overnight_run as cor  # noqa: E402


def stamp(t: dt.datetime) -> str:
    return t.strftime("%m/%d %H:%M:%S")


class LogWriter:
    """Writes server.log lines the way logconfig.xml lays them out: 'MM/DD HH:MM:SS | LVL | logger | message'."""

    def __init__(self):
        self.lines = []

    def add(self, t, level, logger, message, *continuation):
        self.lines.append(f"{stamp(t)} | {level} | {logger} | {message}")
        self.lines.extend(continuation)

    def run(self, start: dt.datetime, minutes: int, commit="abc123def456", clean_stop=True, profile=True):
        """A whole server run: start lines, a SERVER_WORK (and AI_PROFILE) line a minute, then the stop lines."""
        self.add(start, "DBG", "DOL.GS.GameServer", "Current directory is: C:\\Games\\OfflineDAoC\\runtime\\server")
        self.add(start, "ERR", "DOL.GS.SkillBase", "a data-loading error while the server starts")
        self.add(start + dt.timedelta(seconds=9), "WRN", "DOL.GS.ForkBuildInfo",
                 f"FORK_BUILD name=1.2-beta commit={commit} bot_guilds=on")
        ready = start + dt.timedelta(seconds=15)
        self.add(ready, "INF", "DOL.GS.GameServer", "GameServer startup completed in 14.468 seconds")
        for m in range(1, minutes + 1):
            t = ready + dt.timedelta(minutes=m)
            self.add(t, "INF", "DOL.GS.GameLoopWorkMetrics",
                     "SERVER_WORK stage=NpcService samples=1786 avgMs=3.100 maxMs=40.000 barrierAvgMs=0.000 overBudget=3")
            if profile:
                self.add(t + dt.timedelta(seconds=3), "INF", "DOL.GS.AiProfile",
                         "AI_PROFILE tick_p50=5.03 p95=13.61 p99=37.22 max=126.69 serial_ms=0.238 parallel_ms=3.251 "
                         "locks_ms=4116.1 top=nav.path:71459/8436.6 ticks=1787 window_s=60")
        end = ready + dt.timedelta(minutes=minutes, seconds=20)
        if clean_stop:
            self.add(end, "INF", "DOL.GS.GameServer", "Stopping server...")
            self.add(end + dt.timedelta(seconds=2), "INF", "DOL.GS.GameServer", "Stopped")
        return ready

    def stall(self, t, tick_ms, gc_ms=500, profile_max=None):
        """A SERVER_STALL line, and the AI_PROFILE minute that saw the same tick a few seconds later."""
        self.add(t, "WRN", "DOL.GS.GameLoopWorkMetrics",
                 f"SERVER_STALL worstTickMs={tick_ms:.0f} stage=NpcService gcPauseMs={gc_ms:.0f} gen2=0 heapMB=2122 "
                 f"slowAiTurns=65 bigAiTurns=0 likely=\"mixed: many medium AI turns in one tick\" slowest=\"Bot BotBrain 85ms\"")
        if profile_max is not None:
            self.add(t + dt.timedelta(seconds=4), "INF", "DOL.GS.AiProfile",
                     f"AI_PROFILE tick_p50=5.03 p95=13.61 p99=37.22 max={profile_max:.2f} serial_ms=0.2 ticks=1700")

    def write(self, path: Path):
        # Lines are kept in time order, as the server writes them.
        def key(line):
            return line[:14] if cor.parse_head(line) else None
        ordered, last = [], None
        for line in self.lines:
            k = key(line)
            ordered.append((k if k is not None else last, len(ordered), line))
            last = ordered[-1][0]
        ordered.sort(key=lambda x: (x[0], x[1]))
        path.write_text("\r\n".join(line for _, _, line in ordered) + "\r\n", encoding="utf-8")
        return path


T0 = dt.datetime(2026, 11, 14, 22, 0, 0)


class Base(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def check(self, *paths, commit=None):
        files, _ = cor.collect_files([str(p) for p in paths])
        runs = cor.read_runs(files, 2026)
        run, _ = cor.pick_run(runs, commit)
        return cor.check(run)

    def main(self, *args):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = cor.main([str(a) for a in args])
        return code, out.getvalue()


class VerdictTests(Base):
    def test_a_quiet_eight_and_a_half_hours_pass_across_midnight(self):
        log = LogWriter()
        ready = log.run(T0, 510)
        log.stall(ready + dt.timedelta(hours=3, minutes=1), 1300, profile_max=1310)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "PASS", result["reasons"])
        self.assertEqual(result["run"]["commit"], "abc123def456")
        self.assertAlmostEqual(result["run"]["duration_hours"], 8.5, delta=0.02)
        self.assertEqual(result["run"]["start"], T0 + dt.timedelta(seconds=15))
        self.assertEqual(result["run"]["end"].date(), dt.date(2026, 11, 15))
        # The same tick seen by both sources is one stall, with the longer length.
        self.assertEqual(len(result["stalls"]), 1)
        self.assertEqual(result["stalls"][0]["tick_ms"], 1310)
        self.assertEqual(result["stalls"][0]["sources"], ["SERVER_STALL", "AI_PROFILE"])
        # Clock hours run from 22:00 on the 14th to 06:00 on the 15th.
        hours = [h["hour"] for h in result["clock_hours"]]
        self.assertEqual(hours[0], dt.datetime(2026, 11, 14, 22))
        self.assertEqual(hours[-1], dt.datetime(2026, 11, 15, 6))
        self.assertEqual(sum(h["stalls_1s"] for h in result["clock_hours"]), 1)
        # The data-loading error before startup is not an error that stops play.
        self.assertEqual(result["errors_while_loading"], 1)
        self.assertEqual(result["errors_that_stop_play"], [])

    def test_a_stall_of_two_seconds_fails(self):
        log = LogWriter()
        ready = log.run(T0, 500)
        log.stall(ready + dt.timedelta(hours=4, seconds=30), 2000, profile_max=2003)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(len(result["stalls_2s"]), 1)
        self.assertTrue(any("2.0 s or more" in r for r in result["reasons"]))

    def test_a_long_whole_tick_seen_only_by_ai_profile_counts(self):
        # Every stage under a second, the whole tick over two: only AI_PROFILE's max shows it.
        log = LogWriter()
        ready = log.run(T0, 500)
        t = ready + dt.timedelta(hours=2, seconds=40)
        log.add(t, "INF", "DOL.GS.AiProfile", "AI_PROFILE tick_p50=5.0 p95=13.6 p99=37.2 max=2400.00 serial_ms=0.2")
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(result["stalls"][0]["sources"], ["AI_PROFILE"])

    def test_six_stalls_in_one_sliding_hour_fail_even_split_over_two_clock_hours(self):
        log = LogWriter()
        ready = log.run(T0, 500)
        base = dt.datetime(2026, 11, 15, 1, 40, 3)
        for minutes in (0, 7, 14, 21, 28, 35):          # 01:40 .. 02:15, three in each clock hour
            log.stall(base + dt.timedelta(minutes=minutes), 1100, profile_max=1105)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(result["worst_hour"]["count"], 6)
        by_hour = {h["hour"].hour: h["stalls_1s"] for h in result["clock_hours"]}
        self.assertEqual((by_hour[1], by_hour[2]), (3, 3))
        self.assertTrue(any("6 stalls" in r for r in result["reasons"]))

    def test_five_stalls_in_an_hour_pass(self):
        log = LogWriter()
        ready = log.run(T0, 500)
        for minutes in range(5):
            log.stall(ready + dt.timedelta(hours=1, minutes=10 * minutes, seconds=30), 1500)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "PASS", result["reasons"])
        self.assertEqual(result["worst_hour"]["count"], 5)

    def test_a_gc_only_minute_is_listed_not_counted(self):
        log = LogWriter()
        ready = log.run(T0, 500)
        log.stall(ready + dt.timedelta(hours=1, seconds=30), 354, gc_ms=1020)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "PASS", result["reasons"])
        self.assertEqual(result["stalls"], [])
        self.assertEqual(len(result["gc_only_minutes"]), 1)

    def test_a_short_run_fails(self):
        log = LogWriter()
        log.run(T0, 300)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertTrue(any("less than 8 hours" in r for r in result["reasons"]))

    def test_a_short_last_run_with_no_stop_line_may_still_be_running(self):
        log = LogWriter()
        log.run(T0, 40, clean_stop=False)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(result["errors_that_stop_play"], [])
        self.assertTrue(any("still running" in n for n in result["notes"]))

    def test_a_crash_before_eight_hours_is_an_error_that_stops_play(self):
        log = LogWriter()
        log.run(T0, 200, clean_stop=False)
        log.run(T0 + dt.timedelta(hours=4), 510, commit="fffeee000111")
        result = self.check(log.write(self.dir / "server.log"), commit="abc123")
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual([e["kind"] for e in result["errors_that_stop_play"]], ["no_clean_stop"])
        # The next process's start lines are not part of the run that ended.
        self.assertLess(result["run"]["end"], T0 + dt.timedelta(hours=4))

    def test_no_stop_line_after_eight_hours_is_only_a_note(self):
        log = LogWriter()
        log.run(T0, 500, clean_stop=False)
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "PASS", result["reasons"])
        self.assertTrue(any("without a clean stop" in n for n in result["notes"]))


class ErrorTests(Base):
    def run_with(self, *extra):
        log = LogWriter()
        ready = log.run(T0, 500)
        t = ready + dt.timedelta(hours=2, seconds=10)
        for level, logger, message, continuation in extra:
            log.add(t, level, logger, message, *continuation)
        return self.check(log.write(self.dir / "server.log"))

    def test_a_fatal_line_fails(self):
        result = self.run_with(("FTL", "DOL.GS.GameLoop", "Critical error encountered in GameLoop: System.Exception: boom",
                                ["   at DOL.GS.GameLoop.Run()"]))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(result["errors_that_stop_play"][0]["kind"], "fatal")
        self.assertEqual(result["errors_that_stop_play"][0]["detail"], ["at DOL.GS.GameLoop.Run()"])

    def test_a_service_error_that_kicks_the_player_fails_and_one_that_removes_a_bot_does_not(self):
        kick = ("ERR", "DOL.GS.GameServiceUtils", "Critical error encountered in AttackService: System.NullReferenceException",
                ["   at DOL.GS.AttackService.Tick()", "Calling KickPlayerToCharScreen with (entityOwner: Chris)"])
        result = self.run_with(kick)
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(result["errors_that_stop_play"][0]["kind"], "player_kicked")

        self.tearDown()
        self.setUp()
        remove = ("ERR", "DOL.GS.GameServiceUtils", "Critical error encountered in NpcService: System.NullReferenceException",
                  ["   at DOL.GS.NpcService.Tick()", "Calling RemoveFromWorld with (entityOwner: Bot)"])
        result = self.run_with(remove)
        self.assertEqual(result["verdict"], "PASS", result["reasons"])
        self.assertEqual(len(result["other_service_errors"]), 1)
        self.assertEqual(result["other_service_errors"][0]["service"], "NpcService")

    def test_a_watchdog_line_fails(self):
        result = self.run_with(("WRN", "DOL.GS.GameLoopThreadPoolWatchdog",
                                'Watchdog: Thread "GameLoopWorker-3" is taking too long; preserving its in-flight work.', []))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(result["errors_that_stop_play"][0]["kind"], "watchdog")

    def test_other_errors_after_startup_are_counted_not_failed(self):
        result = self.run_with(("ERR", "DOL.GS.MerchantTradeItems", "Item template with ID = 'x' not found", []))
        self.assertEqual(result["verdict"], "PASS", result["reasons"])
        self.assertEqual(result["other_errors_by_logger"], {"DOL.GS.MerchantTradeItems": 1})


class LogSetTests(Base):
    def test_a_measurement_gap_fails(self):
        log = LogWriter()
        log.run(T0, 500)
        # Take out SERVER_WORK and AI_PROFILE for ten minutes in the middle, as a frozen loop would.
        cut = {stamp(T0 + dt.timedelta(seconds=15, minutes=m))[:11] for m in range(200, 210)}
        log.lines = [l for l in log.lines if not (l[:11] in cut and ("SERVER_WORK" in l or "AI_PROFILE" in l))]
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertEqual(len(result["measurement_gaps"]), 1)
        self.assertGreaterEqual(result["measurement_gaps"][0]["minutes"], 10)

    def test_rotated_files_are_read_in_order_and_the_year_turns_over(self):
        log = LogWriter()
        start = dt.datetime(2026, 12, 31, 21, 0, 0)
        log.run(start, 540, commit="0123456789ab")
        lines = log.lines
        # Split at midnight the way the server rotates: server.2026-12-31.0.log, then server.log.
        first = [l for l in lines if l.startswith("12/31")]
        second = lines[len(first):]
        (self.dir / "server.2026-12-31.0.log").write_text("\n".join(first) + "\n", encoding="utf-8")
        (self.dir / "server.log").write_text("\n".join(second) + "\n", encoding="utf-8")
        files, _ = cor.collect_files([str(self.dir / "server.log"), str(self.dir / "server.2026-12-31.0.log")])
        self.assertEqual([f.path.name for f in files], ["server.2026-12-31.0.log", "server.log"])
        runs = cor.read_runs(files)
        self.assertEqual(len(runs), 1)
        result = cor.check(runs[0])
        self.assertEqual(result["verdict"], "PASS", result["reasons"])
        self.assertEqual(result["run"]["end"].year, 2027)
        self.assertAlmostEqual(result["run"]["duration_hours"], 9.0, delta=0.02)

    def test_the_last_run_is_chosen_unless_a_commit_is_given(self):
        log = LogWriter()
        log.run(T0, 500, commit="aaaa11112222")
        log.run(T0 + dt.timedelta(hours=9), 60, commit="bbbb33334444")
        path = log.write(self.dir / "server.log")
        self.assertEqual(self.check(path)["run"]["commit"], "bbbb33334444")
        self.assertEqual(self.check(path, commit="aaaa")["run"]["commit"], "aaaa11112222")

    def test_a_run_whose_start_is_not_in_the_logs_fails(self):
        log = LogWriter()
        log.run(T0, 520)
        log.lines = log.lines[3:]          # drop the start lines and the build line
        result = self.check(log.write(self.dir / "server.log"))
        self.assertEqual(result["verdict"], "FAIL")
        self.assertTrue(any("start is not in these logs" in r for r in result["reasons"]))

    def test_warn_log_lines_without_a_level_are_read_when_no_server_log_is_given(self):
        t = T0
        lines = [f"{stamp(t)} | DOL.GS.ForkBuildInfo | FORK_BUILD name=1.2-beta commit=cccc55556666",
                 f"{stamp(t + dt.timedelta(hours=1))} | DOL.GS.GameLoopWorkMetrics | SERVER_STALL worstTickMs=2500 "
                 "stage=NpcService gcPauseMs=100 gen2=0"]
        (self.dir / "warn.log").write_text("\n".join(lines) + "\n", encoding="utf-8")
        (self.dir / "gm-action.log").write_text(f"{stamp(t)} | something\n", encoding="utf-8")
        files, notes = cor.collect_files([str(self.dir)])
        self.assertEqual([f.path.name for f in files], ["warn.log"])
        self.assertTrue(any("gm-action.log" in n for n in notes))
        result = cor.check(cor.read_runs(files, 2026)[-1])
        self.assertEqual(result["run"]["commit"], "cccc55556666")
        self.assertEqual(result["stalls"][0]["tick_ms"], 2500)
        self.assertEqual(result["verdict"], "FAIL")


class CommandLineTests(Base):
    def test_json_output_and_exit_codes(self):
        log = LogWriter()
        ready = log.run(T0, 500)
        log.stall(ready + dt.timedelta(hours=1, seconds=5), 3000, profile_max=3001)
        path = log.write(self.dir / "server.log")
        code, out = self.main(path, "--json")
        self.assertEqual(code, 1)
        data = json.loads(out)
        self.assertEqual(data["verdict"], "FAIL")
        self.assertEqual(data["run"]["commit"], "abc123def456")
        self.assertEqual(data["stalls"][0]["tick_ms"], 3001)
        self.assertEqual(len(data["runs"]), 1)

        code, out = self.main(path)
        self.assertEqual(code, 1)
        self.assertIn("Overnight gate: FAIL", out)
        self.assertIn("3.00 s", out)

        code, _ = self.main(path, "--commit", "deadbeef")
        self.assertEqual(code, 2)
        code, out = self.main(path, "--list-runs")
        self.assertEqual(code, 0)
        self.assertIn("abc123def456", out)

    def test_a_passing_run_exits_zero(self):
        log = LogWriter()
        log.run(T0, 500)
        code, out = self.main(log.write(self.dir / "server.log"))
        self.assertEqual(code, 0, out)
        self.assertIn("Overnight gate: PASS", out)


if __name__ == "__main__":
    unittest.main()
