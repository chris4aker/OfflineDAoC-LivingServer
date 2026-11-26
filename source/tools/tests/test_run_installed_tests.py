"""Tests of run_installed_tests.py (issue #619): the installed-file tests' runner, without dotnet or a real game."""
import contextlib
import io
import sqlite3
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import run_installed_tests as rit  # noqa: E402

LIST = "# notes\nUT_A  # reads the navmesh (#619)\nUT_B  # reads the world (#619)\nUT_C.Flaky  # flaky (#700)\n"
SRC_A = 'class UT_A { string r = Environment.GetEnvironmentVariable("OFFLINE_DAOC_NAV_ROOT");\n' \
        '  string z = Environment.GetEnvironmentVariable( "OFFLINE_DAOC_ZONES"); }'
SRC_B = 'class UT_B { string d = Environment.GetEnvironmentVariable("OFFLINE_DAOC_DB_PATH"); }'


class RunnerTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)
        self.list = self.root / "ci-excluded-tests.txt"
        self.list.write_text(LIST, encoding="utf-8")
        self.tests = self.root / "Tests"
        (self.tests / "UnitTests").mkdir(parents=True)
        (self.tests / "UnitTests" / "UT_A.cs").write_text(SRC_A, encoding="utf-8")
        (self.tests / "UnitTests" / "UT_B.cs").write_text(SRC_B, encoding="utf-8")
        self.game = self.root / "game"
        (self.game / "runtime" / "server" / "navmesh").mkdir(parents=True)
        (self.game / "runtime" / "data").mkdir(parents=True)
        self.world = self.game / "runtime" / "data" / "opendaoc.sqlite3.db"
        con = sqlite3.connect(self.world)
        con.execute("CREATE TABLE DOLCharacters (Name TEXT)")
        con.execute("INSERT INTO DOLCharacters VALUES ('Player')")
        con.commit()
        con.close()
        self.calls = []

    def tearDown(self):
        self._tmp.cleanup()

    def runner(self, cmd, cwd, env):
        self.calls.append((cmd, cwd, env))
        return 0

    def go(self, *argv, running=False):
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = rit.run(list(argv), runner=self.runner, running=lambda: running, environ={"KEEP": "1"},
                           list_path=self.list, tests=self.tests)
        return code, out.getvalue(), err.getvalue()

    def test_list_shows_reasons_and_variables_and_skips_quarantines(self):
        code, out, _ = self.go("--list")
        self.assertEqual(code, 0)
        self.assertIn("UT_A\n    reads the navmesh (#619)\n    reads: OFFLINE_DAOC_NAV_ROOT, OFFLINE_DAOC_ZONES", out)
        self.assertIn("set yourself if needed: OFFLINE_DAOC_ZONES", out)
        self.assertNotIn("UT_C", out)
        self.assertIn("2 classes", out)
        self.assertEqual(self.calls, [])

    def test_the_tests_get_a_copy_of_the_world_never_the_world(self):
        out_dir = self.root / "out"
        code, out, err = self.go("--game", str(self.game), "--out", str(out_dir))
        self.assertEqual(code, 1, out + err)  # dotnet "ran" but wrote no results: not a pass
        self.assertEqual(len(self.calls), 1)
        cmd, cwd, env = self.calls[0]
        copy = Path(env["OFFLINE_DAOC_DB_PATH"])
        self.assertEqual(copy, out_dir / "world-copy.sqlite3.db")
        self.assertEqual(env["OFFLINE_DAOC_DB"], str(copy))
        self.assertNotEqual(copy.resolve(), self.world.resolve())
        con = sqlite3.connect(copy)
        self.assertEqual(con.execute("SELECT Name FROM DOLCharacters").fetchall(), [("Player",)])
        con.close()
        self.assertEqual(env["OFFLINE_DAOC_NAV_ROOT"], str(self.game / "runtime" / "server"))
        self.assertEqual(env["OFFLINE_DAOC_PROOF_OUT"], str(out_dir / "proof"))
        self.assertEqual(env["KEEP"], "1")
        self.assertIn("FullyQualifiedName~.UT_A|FullyQualifiedName~.UT_B", cmd)
        self.assertEqual(cmd[:3], ["dotnet", "test", "Tests/Tests.csproj"])
        self.assertEqual(Path(cwd), rit.SERVER)

    def test_results_are_summed_up_from_the_trx(self):
        out_dir = self.root / "out"
        out_dir.mkdir()

        def runner(cmd, cwd, env):
            (out_dir / "installed.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestDefinitions>'
                '<UnitTest id="1" name="Routes"><TestMethod className="DOL.UnitTests.UT_A" name="Routes"/></UnitTest>'
                '</TestDefinitions><Results><UnitTestResult testId="1" testName="Routes" outcome="Passed"/>'
                '</Results></TestRun>', encoding="utf-8")
            return 0
        with contextlib.redirect_stdout(io.StringIO()):
            code = rit.run(["--game", str(self.game), "--out", str(out_dir), "--only", "UT_A"], runner=runner,
                           running=lambda: False, environ={}, list_path=self.list, tests=self.tests)
        self.assertEqual(code, 0)

    def test_dry_run_runs_nothing(self):
        code, out, _ = self.go("--game", str(self.game), "--out", str(self.root / "o"), "--dry-run")
        self.assertEqual(code, 0)
        self.assertEqual(self.calls, [])
        self.assertIn("OFFLINE_DAOC_DB_PATH=", out)

    def test_nothing_runs_while_a_server_answers(self):
        code, _, err = self.go("--game", str(self.game), "--out", str(self.root / "o"), running=True)
        self.assertEqual(code, 2)
        self.assertIn("port 10300", err)
        self.assertEqual(self.calls, [])
        self.assertFalse((self.root / "o").exists())

    def test_only_listed_classes_can_be_picked(self):
        code, _, err = self.go("--game", str(self.game), "--only", "UT_Z")
        self.assertEqual(code, 2)
        self.assertIn("not on ci-excluded-tests.txt: UT_Z", err)
        self.assertEqual(self.calls, [])

    def test_a_folder_that_is_not_a_game_is_refused(self):
        code, _, err = self.go("--game", str(self.root), "--out", str(self.root / "o"))
        self.assertEqual(code, 2)
        self.assertIn("not a playable folder", err)

    def test_running_needs_a_game(self):
        code, _, err = self.go()
        self.assertEqual(code, 2)
        self.assertIn("--game", err)

    def test_the_repository_list_and_sources_are_readable(self):
        entries = rit.listed_classes(rit.LIST.read_text(encoding="utf-8"))
        self.assertGreater(len(entries), 0)
        for name, _ in entries:
            rit.env_vars(name)  # each class has a source file (Stop otherwise)


if __name__ == "__main__":
    unittest.main()
