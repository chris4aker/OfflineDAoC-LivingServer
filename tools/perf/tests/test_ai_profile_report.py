"""ai_profile_report.py on small made-up logs. No real install is read or written.

    python -m unittest discover -s tools/perf/tests -v
"""
import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import ai_profile_report as tool  # noqa: E402

HEADER = "utc,system,calls,total_ms,self_ms,max_ms,avg_us,share_pct"


def line(minute, p99, maximum=None, serial=1.5, over=""):
    """One AI_PROFILE line as the server writes it, with the NLog prefix."""
    utc = f"2026-11-03T12:{minute:02d}:00Z"
    maximum = p99 * 2 if maximum is None else maximum
    extra = f" over_budget={over}" if over else ""
    return (f"11/03 12:{minute:02d}:00 | INF | DOL.GS.AiProfile | AI_PROFILE tick_p50=10.00 p95=20.00 p99={p99:.2f} "
            f"max={maximum:.2f} serial_ms={serial:.3f} parallel_ms=6.000 locks_ms=2.5 "
            f"top=think.npc:1000/300.0,route.zone_step:20/50.0 ticks=1800 window_s=60 tick_avg=11.00 serial_p99=3.00 "
            f"serial_max=4.00 parallel_p99=9.00 parallel_max=12.00 other_ms=3.500{extra} utc={utc}\n")


def csv_rows(minute):
    utc = f"2026-11-03T12:{minute:02d}:00Z"
    return [f"{utc},think.npc,1000,900,300,5,900,60", f"{utc},route.zone_step,20,50,50,30,2500,10",
            f"{utc},chat.ambient,5,1,1,0.5,200,0.2"]


class ParseTests(unittest.TestCase):
    def test_reads_the_fields_and_the_top_list(self):
        minute = tool.parse_line(line(1, 45.5))
        self.assertEqual(minute.when, "2026-11-03T12:01:00Z")
        self.assertEqual(minute.get("p99"), 45.5)
        self.assertEqual(minute.get("serial_ms"), 1.5)
        self.assertEqual(minute.top, [("think.npc", 1000, 300.0), ("route.zone_step", 20, 50.0)])

    def test_ignores_other_lines_and_the_budget_warning(self):
        self.assertIsNone(tool.parse_line("11/03 12:00:00 | INF | X | SERVER_WORK stage=NpcService\n"))
        self.assertIsNone(tool.parse_line("11/03 12:00:00 | X | AI_PROFILE_BUDGET over=serial_p99:6.0\n"))

    def test_top_none_and_bad_items(self):
        self.assertEqual(tool.parse_top("none"), [])
        self.assertEqual(tool.parse_top("a.b:3/1.5,broken"), [("a.b", 3, 1.5)])


class ReportTests(unittest.TestCase):
    def run_tool(self, log_lines, csv_lines=None, *args):
        with tempfile.TemporaryDirectory() as folder:
            log = Path(folder) / "server.log"
            log.write_text("".join(log_lines), encoding="utf-8")
            if csv_lines is not None:
                (Path(folder) / "ai-profile.csv").write_text("\n".join([HEADER] + csv_lines) + "\n", encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
                code = tool.main([str(log), *args])
            return code, out.getvalue()

    def test_top_ten_from_the_csv_is_exact(self):
        rows = csv_rows(1) + [HEADER] + csv_rows(2)  # a repeated header line is skipped
        code, text = self.run_tool([line(1, 40), line(2, 60)], rows)
        self.assertEqual(code, 0)
        self.assertIn("| 1 | think.npc | 1,000 | 300.0 | 900.0 | 5.00 |", text)
        self.assertIn("| 2 | route.zone_step | 20 | 50.0 | 50.0 | 30.00 |", text)
        self.assertNotIn("under-counted", text)
        self.assertIn("worst 60.0 ms", text)

    def test_without_the_csv_it_falls_back_to_the_top_lists(self):
        code, text = self.run_tool([line(1, 40), line(2, 60)])
        self.assertEqual(code, 0)
        self.assertIn("under-counted", text)
        self.assertIn("| 1 | think.npc | 1,000 | 300.0 | - | - |", text)

    def test_gate_passes_and_fails_on_the_worst_minute(self):
        code, text = self.run_tool([line(1, 40), line(2, 60)], None, "--gate", "L300")
        self.assertEqual(code, 0)
        self.assertIn("**Result: PASS**", text)
        code, text = self.run_tool([line(1, 40), line(2, 120)], None, "--gate", "L300")
        self.assertEqual(code, 1)
        self.assertIn("FAIL: p99 tick < 100 ms in every minute (worst minute p99 120.0 ms; 1 of 2 minutes over)", text)

    def test_max_and_stalls_count_against_the_gate(self):
        stall = "11/03 12:02:30 | WRN | DOL.GS.GameLoopWorkMetrics | SERVER_STALL worstTickMs=1300 stage=NpcService\n"
        code, text = self.run_tool([line(1, 40, maximum=600), line(2, 40), stall], None, "--gate", "L300")
        self.assertEqual(code, 1)
        self.assertIn("FAIL: max tick < 500 ms", text)
        self.assertIn("FAIL: no SERVER_STALL over 1000 ms (1 stall minute(s) over, worst 1300 ms)", text)

    def test_skip_minutes_leaves_out_the_startup_ramp_and_its_stalls(self):
        # A stall line comes before the AI_PROFILE line of the minute it happened in.
        stall = "11/03 12:00:30 | WRN | X | SERVER_STALL worstTickMs=2500 stage=NpcService\n"
        code, text = self.run_tool([stall, line(1, 900, maximum=2500), line(2, 40), line(3, 50)],
                                   csv_rows(1) + csv_rows(2) + csv_rows(3), "--skip-minutes", "1", "--gate", "L300")
        self.assertEqual(code, 0, text)
        self.assertIn("Minutes: 2 (2026-11-03T12:02:00Z", text)
        self.assertIn("| 1 | think.npc | 1,000 |", text)

    def test_timeline_buckets_keep_the_worst_p99(self):
        lines = [line(m, 10 + m) for m in range(1, 11)]
        code, text = self.run_tool(lines, None, "--every", "5")
        self.assertEqual(code, 0)
        self.assertIn("| 2026-11-03T12:01:00Z | 10.0 | 20.0 | 15.0 |", text)
        self.assertIn("| 2026-11-03T12:06:00Z | 10.0 | 20.0 | 20.0 |", text)

    def test_over_budget_minutes_are_marked(self):
        code, text = self.run_tool([line(1, 40, serial=6, over="serial_p99:7.0")])
        self.assertIn("Minutes over the serial budget (5 ms a tick, 2 ms a pulse): 1", text)
        self.assertIn("6.00 * |", text)

    def test_no_lines_is_exit_code_two(self):
        code, _ = self.run_tool(["nothing here\n"])
        self.assertEqual(code, 2)

    def test_timeline_csv(self):
        with tempfile.TemporaryDirectory() as folder:
            log = Path(folder) / "server.log"
            log.write_text(line(1, 40) + line(2, 50), encoding="utf-8")
            timeline = Path(folder) / "timeline.csv"
            with contextlib.redirect_stdout(io.StringIO()):
                tool.main([str(log), "--timeline-csv", str(timeline)])
            rows = timeline.read_text(encoding="utf-8").splitlines()
            self.assertEqual(rows[0].split(",")[:5], ["utc", "ticks", "tick_p50", "p95", "p99"])
            self.assertEqual(rows[2].split(",")[4], "50.00")


if __name__ == "__main__":
    unittest.main()
