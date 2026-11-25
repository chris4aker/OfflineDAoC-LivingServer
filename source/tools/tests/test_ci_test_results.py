"""Tests for source/tools/ci_test_results.py, the CI glue that fails honestly (issue #618). Each check is shown failing on a
planted defect (a missing file, a failed test, a filter that matched nothing, a vanished class) and passing without it.

    python3 -m unittest discover -s source/tools/tests -v
"""
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import ci_test_results as ci  # noqa: E402

NS = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"


def trx(results):
    """A minimal .trx with (class, method, outcome) rows, the shape dotnet test's trx logger writes."""
    defs, rows = [], []
    for i, (cls, method, outcome) in enumerate(results):
        defs.append(f'<UnitTest name="{method}" id="id{i}"><TestMethod className="DOL.UnitTests.{cls}" name="{method}"/></UnitTest>')
        error = '<Output><ErrorInfo><Message>Expected 1 but was 2\nmore</Message></ErrorInfo></Output>' if outcome == "Failed" else ""
        rows.append(f'<UnitTestResult testId="id{i}" testName="{method}" outcome="{outcome}">{error}</UnitTestResult>')
    return (f'<?xml version="1.0" encoding="utf-8"?><TestRun xmlns="{NS}"><Results>{"".join(rows)}</Results>'
            f'<TestDefinitions>{"".join(defs)}</TestDefinitions></TestRun>')


class TrxTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    def check(self, text, classes=(), min_executed=1, name="server.trx"):
        path = self.root / name
        if text is not None:
            path.write_text(text, encoding="utf-8")
        lines = []
        code = ci.check_trx(path, "Server tests", list(classes), min_executed, out=lines.append)
        return code, "\n".join(lines)

    def test_all_passed_is_green_with_the_counts(self):
        code, out = self.check(trx([("UT_A", "One", "Passed"), ("UT_A", "Two", "Passed"), ("UT_B", "Three", "NotExecuted")]))
        self.assertEqual(code, 0, out)
        self.assertIn("total 3, executed 2, passed 2, failed 0", out)

    def test_a_failed_test_is_red_and_named(self):
        code, out = self.check(trx([("UT_A", "One", "Passed"), ("UT_A", "Two", "Failed")]))
        self.assertEqual(code, 1)
        self.assertIn("::error title=Server tests: failed::DOL.UnitTests.UT_A.Two: Expected 1 but was 2", out)

    def test_a_missing_file_is_red(self):
        code, out = self.check(None)
        self.assertEqual(code, 1)
        self.assertIn("::error", out)

    def test_an_unreadable_file_is_red(self):
        code, out = self.check("<TestRun")
        self.assertEqual(code, 1)
        self.assertIn("not a readable", out)

    def test_a_filter_that_matched_nothing_is_red(self):
        code, out = self.check(trx([]))
        self.assertEqual(code, 1)
        self.assertIn("only 0 tests ran", out)
        code, out = self.check(trx([("UT_A", "One", "NotExecuted")]))
        self.assertEqual(code, 1, out)

    def test_classes_read_from_the_gate_results(self):
        rows = [("UT_AutonomousBotGuilds", "One", "Passed"), ("UT_Other", "Two", "Failed")]
        code, out = self.check(trx(rows), classes=["UT_AutonomousBotGuild"])
        self.assertEqual(code, 0, out)  # UT_Other's failure is the gate's, not this report's
        self.assertIn("total 1, executed 1, passed 1", out)
        code, out = self.check(trx(rows), classes=["UT_Other"])
        self.assertEqual(code, 1)

    def test_a_class_with_no_tests_in_the_file_is_red(self):
        code, out = self.check(trx([("UT_A", "One", "Passed")]), classes=["UT_A", "UT_Gone"])
        self.assertEqual(code, 1)
        self.assertIn("no test of UT_Gone", out)

    def test_main_reads_a_space_separated_class_list(self):
        (self.root / "r.trx").write_text(trx([("UT_A", "One", "Passed"), ("UT_B", "Two", "Passed")]), encoding="utf-8")
        self.assertEqual(ci.main(["trx", str(self.root / "r.trx"), "--title", "T", "--classes", "UT_A UT_B"]), 0)
        self.assertEqual(ci.main(["trx", str(self.root / "r.trx"), "--title", "T", "--classes", "UT_A UT_C"]), 1)


class RequireTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        sim = self.root / "source/server/Tests/bin/Release/net10.0/chat-sim"
        sim.mkdir(parents=True)
        (sim / "faction-sim-before.chat.log").write_text("x", encoding="utf-8")
        (sim / "faction-sim-after.chat.log").write_text("x", encoding="utf-8")

    def run_require(self, *patterns):
        lines = []
        return ci.require(self.root, "Chat audit", list(patterns), out=lines.append), lines

    def test_files_found_at_any_depth_are_printed(self):
        code, lines = self.run_require("chat-sim/faction-sim-*.chat.log")
        self.assertEqual(code, 0, lines)
        self.assertEqual([Path(line).name for line in lines], ["faction-sim-after.chat.log", "faction-sim-before.chat.log"])

    def test_a_missing_file_is_red(self):
        code, lines = self.run_require("chat-sim/faction-sim-*.chat.log", "chat-bench/bench-replies.json")
        self.assertEqual(code, 1)
        self.assertTrue(any(line.startswith("::error") and "bench-replies.json" in line for line in lines), lines)

    def test_build_output_folders_are_not_searched(self):
        obj = self.root / "obj/chat-bench"
        obj.mkdir(parents=True)
        (obj / "bench-replies.json").write_text("{}", encoding="utf-8")
        code, _ = self.run_require("chat-bench/bench-replies.json")
        self.assertEqual(code, 1)

    def test_find_prints_the_first_match_or_nothing(self):
        import contextlib
        import io
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(ci.main(["find", "--root", str(self.root), "chat-sim/faction-sim-*.chat.log"]), 0)
            self.assertEqual(ci.main(["find", "--root", str(self.root), "nothing-here.trx"]), 0)
        self.assertEqual([Path(line).name for line in out.getvalue().split()], ["faction-sim-after.chat.log"])


class ExcludedListTests(unittest.TestCase):
    REPO = HERE.parents[1]

    def test_an_entry_without_a_reason_or_issue_is_refused(self):
        entries, problems = ci.parse_excluded("# a note\n\nUT_A  # reads the navmesh (#619)\nUT_B\nUT_C  # no issue\nUT_A  # again (#1)\n")
        self.assertEqual([n for n, _ in entries], ["UT_A", "UT_A"])
        self.assertEqual(len(problems), 3, problems)
        self.assertIn("UT_B", problems[0])
        self.assertIn("UT_C", problems[1])
        self.assertIn("listed twice", problems[2])

    def test_a_single_test_can_be_quarantined(self):
        entries, problems = ci.parse_excluded("UT_A.Flaky  # flaky on Windows runners (#700)\n")
        self.assertEqual(problems, [])
        self.assertEqual(ci.excluded_filter(entries), "FullyQualifiedName!~.UT_A.Flaky")

    def test_main_fails_on_a_bad_list(self):
        with tempfile.TemporaryDirectory() as tmp:
            bad = Path(tmp) / "list.txt"
            bad.write_text("UT_A\n", encoding="utf-8")
            import contextlib
            import io
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(ci.main(["excluded-filter", str(bad)]), 1)

    def test_the_repository_list_is_well_formed(self):
        entries, problems = ci.parse_excluded((self.REPO / ".github/ci-excluded-tests.txt").read_text(encoding="utf-8"))
        self.assertEqual(problems, [])
        self.assertGreater(len(entries), 0)

    def test_every_listed_class_exists_and_is_explicit(self):
        """A whole class goes on the list only when all of it needs installed files, which [Explicit] on the fixture says;
        a class with synthetic tests in it must not be listed whole (issue #618: six were, and their tests never ran)."""
        import re
        tests = self.REPO / "source/server/Tests"
        entries, _ = ci.parse_excluded((self.REPO / ".github/ci-excluded-tests.txt").read_text(encoding="utf-8"))
        for name, _ in entries:
            if "." in name:
                continue  # one test, quarantined with its own issue
            files = list(tests.rglob(f"{name}.cs"))
            self.assertTrue(files, f"{name}: no {name}.cs under source/server/Tests (renamed? take it off the list)")
            src = files[0].read_text(encoding="utf-8", errors="replace")
            m = re.search(r"((?:\[[^\]]*\]\s*)*)(?:public\s+|internal\s+)?(?:sealed\s+|static\s+)*class\s+" + name + r"\b", src)
            self.assertTrue(m and "Explicit" in m.group(1), f"{name}: the fixture is not [Explicit]; list only its installed tests")


if __name__ == "__main__":
    unittest.main()
