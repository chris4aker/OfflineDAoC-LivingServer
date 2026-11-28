"""generated_guard.py: a pull request that commits a file main generates fails; main's own changes don't (issue #817).

Each case is a throwaway git repository: main, a branch off it, and the branch's merge commit as CI checks it out.
"""
import contextlib
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import generated_guard  # noqa: E402


def git(repo: Path, *args: str) -> str:
    return subprocess.run(["git", *args], cwd=repo, check=True, capture_output=True, text=True).stdout


def commit(repo: Path, files: dict[str, str], message: str):
    for rel, text in files.items():
        (repo / rel).parent.mkdir(parents=True, exist_ok=True)
        (repo / rel).write_text(text, encoding="utf-8")
    git(repo, "add", "-A")
    git(repo, "commit", "-q", "-m", message)


def guard(repo: Path, base: str, *extra: str) -> tuple[int, str]:
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        code = generated_guard.main(["--base", base, "--repo", str(repo), *extra])
    return code, out.getvalue()


WORKFLOW = HERE.parents[2] / ".github" / "workflows" / "build-and-test.yml"


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = Path(self.tmp.name)
        git(self.repo, "init", "-q", "-b", "main")
        git(self.repo, "config", "user.email", "t@example.invalid")
        git(self.repo, "config", "user.name", "t")
        commit(self.repo, {"docs/LOG-ROWS.md": "rows v1\n", "tools/grafana/alloy/config.alloy": "cfg v1\n",
                           "src/a.cs": "a\n"}, "base")

    def branch(self, files: dict[str, str]):
        git(self.repo, "checkout", "-q", "-b", "feature", "main")
        commit(self.repo, files, "feature")

    def test_a_branch_that_only_changes_code_passes(self):
        self.branch({"src/a.cs": "a with a new row\n"})
        code, out = guard(self.repo, "main")
        self.assertEqual(code, 0, out)

    def test_a_branch_that_commits_a_generated_file_fails(self):
        for rel in generated_guard.GENERATED:
            with self.subTest(rel=rel):
                git(self.repo, "checkout", "-q", "main")
                subprocess.run(["git", "branch", "-q", "-D", "feature"], cwd=self.repo, capture_output=True)
                self.branch({"src/a.cs": "a2\n", rel: "regenerated on the branch\n"})
                code, out = guard(self.repo, "main")
                self.assertEqual(code, 1)
                self.assertIn(f"changes {rel}", out)
                self.assertIn(f"git checkout origin/main -- {rel}", out)

    def test_main_regenerating_after_the_branch_forked_does_not_count(self):
        self.branch({"src/a.cs": "a2\n"})
        git(self.repo, "checkout", "-q", "main")
        commit(self.repo, {"docs/LOG-ROWS.md": "rows v2\n"}, "bot: regenerate")
        git(self.repo, "checkout", "-q", "feature")
        code, out = guard(self.repo, "main")
        self.assertEqual(code, 0, out)
        # And after the branch merges main in (the merge train's habit), still clean.
        git(self.repo, "merge", "-q", "--no-edit", "main")
        code, out = guard(self.repo, "main")
        self.assertEqual(code, 0, out)

    def test_the_merge_commit_ci_checks_out(self):
        # pull_request runs check out refs/pull/N/merge: first parent main, second the branch. --base HEAD^1.
        self.branch({"src/a.cs": "a2\n"})
        git(self.repo, "checkout", "-q", "main")
        commit(self.repo, {"tools/grafana/alloy/config.alloy": "cfg v2\n"}, "bot: regenerate")
        git(self.repo, "merge", "-q", "--no-ff", "--no-edit", "feature")
        self.assertEqual(guard(self.repo, "HEAD^1")[0], 0)
        git(self.repo, "checkout", "-q", "feature")
        commit(self.repo, {"docs/LOG-ROWS.md": "rows from the branch\n"}, "commits the catalog")
        git(self.repo, "checkout", "-q", "-B", "pr-merge", "main")
        git(self.repo, "reset", "-q", "--hard", "HEAD^1")  # main before the merge above
        git(self.repo, "merge", "-q", "--no-ff", "--no-edit", "feature")
        code, out = guard(self.repo, "HEAD^1")
        self.assertEqual(code, 1, out)
        self.assertIn("changes docs/LOG-ROWS.md", out)

    def test_an_unknown_base_is_a_note(self):
        code, out = guard(self.repo, "origin/main")
        self.assertEqual(code, 0)
        self.assertIn("note:", out)


if __name__ == "__main__":
    unittest.main()


class StrictTests(unittest.TestCase):
    """--strict (CI): a range git can't read fails instead of skipping the guard."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = Path(self.tmp.name)
        git(self.repo, "init", "-q", "-b", "main")
        git(self.repo, "config", "user.email", "t@example.invalid")
        git(self.repo, "config", "user.name", "t")
        commit(self.repo, {"src/a.cs": "a\n"}, "base")

    def test_an_unknown_base_is_a_note_without_strict(self):
        code, out = guard(self.repo, "origin/main")
        self.assertEqual(code, 0, out)
        self.assertIn("note:", out)

    def test_an_unknown_base_fails_with_strict(self):
        code, out = guard(self.repo, "origin/main", "--strict")
        self.assertEqual(code, 1, out)
        self.assertIn("error:", out)

    def test_a_shallow_merge_commit_without_its_parent_fails_with_strict(self):
        # A depth-1 checkout: HEAD^1 isn't there.
        code, out = guard(self.repo, "HEAD^1", "--strict")
        self.assertEqual(code, 1, out)

    def test_strict_still_passes_a_clean_range(self):
        commit(self.repo, {"src/a.cs": "a2\n"}, "next")
        code, out = guard(self.repo, "HEAD^1", "--strict")
        self.assertEqual(code, 0, out)


class WorkflowTests(unittest.TestCase):
    """build-and-test.yml keeps the guard on both runs of a branch, strict (review of #818)."""

    def setUp(self):
        self.text = WORKFLOW.read_text(encoding="utf-8")
        job = self.text.split("\n  encounter-catalog:", 1)[1]
        self.job = job.split("\n  new-client:", 1)[0]

    def test_the_pull_request_run_guards_its_merge_commit_strictly(self):
        self.assertIn("run: python3 tools/grafana/generated_guard.py --base HEAD^1 --strict", self.job)
        self.assertIn("github.event_name == 'pull_request'", self.job)
        # The bot branch is exempt only from this repository, not from a fork's branch of the same name.
        self.assertIn("github.event.pull_request.head.repo.full_name == github.repository", self.job)

    def test_the_push_run_guards_the_branch_against_main_strictly(self):
        self.assertIn("run: python3 tools/grafana/generated_guard.py --base origin/main --strict", self.job)
        self.assertIn("github.event_name == 'push' && github.ref_name != 'main' && github.ref_name != 'bot/generated-files'",
                      self.job)

    def test_the_checkout_has_the_history_the_guard_reads(self):
        self.assertIn("fetch-depth: ${{ github.event_name == 'push' && '0' || '2' }}", self.job)
