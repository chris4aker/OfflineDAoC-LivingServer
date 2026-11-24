"""Tests of rvr_seen_stamps.py on a small git repository made here (#519, evidence.md F5).

    python3 -m unittest source/tools/tests/test_rvr_seen_stamps.py
"""
import contextlib
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import rvr_seen_stamps as rss  # noqa: E402

DOC = """# The gate

## Open-field RvR

| What | Must | Status |
|---|---|---|
| Sieges happen | must | **Seen** on `{a}`: 4 sieges. Contradicted later on `{b}` |
| Bots notice bots | must | Not seen yet. Open |
| Regroups no longer flap | must | **Seen** on `{a}` and on `{b}` |

## Crafting

| What | Must | Status |
|---|---|---|
| Crafters craft | | **Seen** on `{a}` |
"""


class SeenStamps(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.repo = Path(self.tmp.name)
        self.git("init", "-q")
        rvr = self.repo / "source" / "server" / "GameServer" / "bots" / "autonomous"
        rvr.mkdir(parents=True)
        (rvr / "AutonomousRvrEventLayer.cs").write_text("one\n")
        (rvr / "Crafting.cs").write_text("one\n")
        self.a = self.commit("first build", 1_000)
        (rvr / "Crafting.cs").write_text("two\n")
        self.b = self.commit("not RvR", 2_000)
        (rvr / "AutonomousRvrEventLayer.cs").write_text("two\n")
        self.c = self.commit("RvR", 3_000)

    def tearDown(self):
        self.tmp.cleanup()

    def git(self, *args, env=None):
        return subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", *args], cwd=self.repo,
                              capture_output=True, text=True, check=True, env=env).stdout.strip()

    def commit(self, message, when):
        import os
        env = dict(os.environ, GIT_AUTHOR_DATE=f"@{1_700_000_000 + when} +0000",
                   GIT_COMMITTER_DATE=f"@{1_700_000_000 + when} +0000")
        self.git("add", "-A", env=env)
        self.git("commit", "-q", "-m", message, env=env)
        return self.git("rev-parse", "--short=7", "HEAD")

    def run_tool(self, *args):
        doc = self.repo / "PLAYED.md"
        if not doc.exists():
            doc.write_text(DOC.format(a=self.a, b=self.b), encoding="utf-8")
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = rss.main([str(doc), "--repo", str(self.repo), *args])
        return code, out.getvalue(), doc

    def test_the_build_after_seen_and_whether_rvr_code_changed_since(self):
        code, out, _ = self.run_tool("--ref", self.b)
        self.assertEqual(code, 0)
        self.assertIn(f"| Sieges happen | 7 | `{self.a}` | no, to `{self.b}`", out, "only a non-RvR file changed")
        self.assertNotIn("Bots notice bots", out, "a Not seen row is not stamped")
        self.assertNotIn("Crafters", out, "not an RvR row")
        code, out, _ = self.run_tool("--ref", self.c)
        self.assertIn(f"| Sieges happen | 7 | `{self.a}` | yes, 1 file(s)", out)
        # the newest Seen build of the row: b, and only the RvR file changed since
        self.assertIn(f"| Regroups no longer flap | 9 | `{self.b}` | yes, 1 file(s)", out)
        self.assertIn("Crafters", self.run_tool("--ref", self.c, "--all")[1])

    def test_a_build_not_in_the_history_is_unknown(self):
        doc = self.repo / "PLAYED.md"
        doc.write_text(DOC.format(a="deadbee", b="deadbee"), encoding="utf-8")
        _, out, _ = self.run_tool("--ref", self.c)
        self.assertIn("unknown, the build is not in the local history", out)

    def test_write_is_idempotent_and_keeps_the_row(self):
        self.run_tool("--ref", self.c, "--write")
        _, _, doc = self.run_tool("--ref", self.c, "--write")
        text = doc.read_text(encoding="utf-8")
        self.assertEqual(text.count("_[stamp: "), 2)
        self.assertIn(f"**Seen** on `{self.a}`: 4 sieges. Contradicted later on `{self.b}` _[stamp: on build "
                      f"`{self.a}`; RvR code changed since: yes, 1 file(s), to `{self.c}`", text)
        self.assertTrue(text.splitlines()[6].endswith("]_ |"))
        # a stamp names no build of its own: the row's own builds decide
        self.assertEqual(rss.seen_builds(text.splitlines()[6]), [self.a], "Contradicted later on b is no Seen")

    def test_no_seen_row(self):
        doc = self.repo / "PLAYED.md"
        doc.write_text("# nothing\n\n| What | Status |\n|---|---|\n| x | Not seen |\n", encoding="utf-8")
        self.assertEqual(self.run_tool()[0], 2)


if __name__ == "__main__":
    unittest.main()
