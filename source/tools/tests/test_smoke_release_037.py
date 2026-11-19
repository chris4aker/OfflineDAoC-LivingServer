"""Tests of smoke_release_037.py's startup wait: a server that exits or never finishes starting fails with the tail of
its console log in the message, so the CI output (tools/installer/test-installer.ps1, Smoke-Game) shows why.

    python -m unittest discover -s source/tools/tests -v
"""
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import smoke_release_037 as smoke  # noqa: E402


class FakeServer:
    def __init__(self, returncode=None):
        self.returncode = returncode

    def poll(self):
        return self.returncode


class StartupWaitTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.log = Path(self.tmp.name) / "console.log"

    def tearDown(self):
        self.tmp.cleanup()

    def test_started_server_returns(self):
        self.log.write_text("loading\nGameServer startup completed\n", encoding="utf-8")
        smoke.wait_for_startup(FakeServer(), self.log, timeout=5, poll=0)

    def test_exited_server_shows_exit_code_and_log_tail(self):
        lines = [f"line {i}" for i in range(100)] + ["Unhandled exception. System.InvalidOperationException: boom"]
        self.log.write_text("\n".join(lines) + "\n", encoding="utf-8")
        with self.assertRaises(RuntimeError) as caught:
            smoke.wait_for_startup(FakeServer(returncode=134), self.log, timeout=5, poll=0)
        message = str(caught.exception)
        self.assertIn("Server exited during startup (exit code 134)", message)
        self.assertIn("System.InvalidOperationException: boom", message)
        self.assertNotIn("line 10\n", message)   # only the tail, not the whole log

    def test_timeout_shows_log_tail(self):
        self.log.write_text("Loading recipes\n", encoding="utf-8")
        with self.assertRaises(RuntimeError) as caught:
            smoke.wait_for_startup(FakeServer(), self.log, timeout=0, poll=0)
        self.assertIn("Startup timeout", str(caught.exception))
        self.assertIn("Loading recipes", str(caught.exception))

    def test_missing_or_empty_log(self):
        self.assertIn("no server log", smoke.log_tail(self.log))
        self.log.write_text("", encoding="utf-8")
        self.assertEqual(smoke.log_tail(self.log), "(the server log is empty)")


if __name__ == "__main__":
    unittest.main()
