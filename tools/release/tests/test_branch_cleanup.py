"""Issue #645: the branch cleanup classifies a fake branch list, keeps what it must and archives before it deletes."""
import subprocess
import sys
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import branch_cleanup as bc  # noqa: E402

NOW = datetime(2026, 11, 26, tzinfo=timezone.utc)
OLD = NOW - timedelta(days=60)


def br(name, sha=None, days=60, in_main=False, protected=False):
    return bc.Branch(name, sha or (name.replace("/", "-") + "0" * 40)[:40], NOW - timedelta(days=days), in_main, protected)


BRANCHES = [
    br("main", in_main=True),
    br("integration/1.2"),
    br("release/0.36-se.1"),
    br("release/0.35-chris.1", in_main=True),
    br("release/1.2", in_main=True),
    br("release/1.2.1-dryrun", in_main=True),
    br("guarded", protected=True),
    br("tagged", sha="a" * 40),
    br("feature/open", days=90),
    br("feature/new", days=3),
    br("feature/new-merged", days=1, in_main=True),
    br("feature/merged", in_main=True),
    br("feature/squashed"),
    br("feature/abandoned"),
    br("feature/never"),
    br("feature/old-pr-now-open"),
]
PRS = [
    bc.PullRequest(10, "feature/open", "open"),
    bc.PullRequest(11, "feature/squashed", "closed", merged=True),
    bc.PullRequest(12, "feature/abandoned", "closed"),
    bc.PullRequest(13, "feature/merged", "closed", merged=True),
    bc.PullRequest(14, "feature/old-pr-now-open", "closed"),
    bc.PullRequest(15, "feature/old-pr-now-open", "open"),
    bc.PullRequest(16, "release/0.36-se.1", "open"),
]


def classes():
    return {v.branch: v.cls for v in bc.classify(BRANCHES, PRS, NOW, {"a" * 40})}


class ClassifyTests(unittest.TestCase):
    def test_every_branch_gets_its_class(self):
        self.assertEqual(classes(), {
            "main": "protected", "integration/1.2": "protected", "release/0.36-se.1": "protected",
            "release/0.35-chris.1": "protected", "release/1.2": "protected", "guarded": "protected",
            "tagged": "protected", "release/1.2.1-dryrun": "merged",
            "feature/open": "open-pr", "feature/old-pr-now-open": "open-pr",
            "feature/new": "recent", "feature/new-merged": "recent",
            "feature/merged": "merged", "feature/squashed": "replaced", "feature/abandoned": "replaced",
            "feature/never": "never-merged",
        })

    def test_never_touched_branches_keep(self):
        verdicts = {v.branch: v for v in bc.classify(BRANCHES, PRS, NOW, {"a" * 40})}
        for name in ("main", "integration/1.2", "release/0.36-se.1", "release/0.35-chris.1", "guarded", "tagged",
                     "feature/open", "feature/new", "feature/new-merged"):
            self.assertEqual(verdicts[name].action, "keep", name)

    def test_only_unmerged_branches_are_archived(self):
        verdicts = {v.branch: v.action for v in bc.classify(BRANCHES, PRS, NOW, {"a" * 40})}
        self.assertEqual(verdicts["feature/merged"], "delete")
        for name in ("feature/squashed", "feature/abandoned", "feature/never"):
            self.assertTrue(verdicts[name].startswith(f"tag archive/{name}"), name)

    def test_the_recent_edge_is_fourteen_days(self):
        out = {v.branch: v.cls for v in bc.classify([br("a", days=13), br("b", days=15)], [], NOW)}
        self.assertEqual(out, {"a": "recent", "b": "never-merged"})

    def test_the_summary_counts_and_lists_every_class(self):
        text = bc.summary(bc.classify(BRANCHES, PRS, NOW, {"a" * 40}), "show")
        self.assertIn("dry run: nothing was changed", text)
        self.assertIn("| protected | 7 |", text)
        self.assertIn("| never-merged | 1 |", text)
        self.assertIn("<details><summary>replaced (2)</summary>", text)
        self.assertIn("`feature/never`", text)


class FakeRemote:
    """A fake `git push` / `git ls-remote`: records what it did and refuses what it is told to refuse."""

    def __init__(self, refuse=(), lose_tags=False):
        self.calls, self.tags, self.deleted, self.refuse, self.lose_tags = [], {}, [], set(refuse), lose_tags

    def __call__(self, cmd, capture_output=False, text=False):
        self.calls.append(cmd)
        if cmd[1] == "push":
            ref = cmd[-1]
            if ref.startswith(":refs/heads/"):
                name = ref[len(":refs/heads/"):]
                if name in self.refuse:
                    return subprocess.CompletedProcess(cmd, 1)
                self.deleted.append(name)
            else:
                sha, tag = ref.split(":refs/tags/")
                if not self.lose_tags:
                    self.tags[tag] = sha
            return subprocess.CompletedProcess(cmd, 0)
        if cmd[1] == "ls-remote":
            tag = cmd[-1][len("refs/tags/"):]
            out = f"{self.tags[tag]}\trefs/tags/{tag}\n" if tag in self.tags else ""
            return subprocess.CompletedProcess(cmd, 0, stdout=out)
        raise AssertionError(cmd)


class ApplyTests(unittest.TestCase):
    def verdicts(self):
        return bc.classify(BRANCHES, PRS, NOW, {"a" * 40})

    def test_apply_tags_before_it_deletes_and_never_touches_the_kept(self):
        remote = FakeRemote()
        verdicts = self.verdicts()
        self.assertEqual(bc.apply(verdicts, "origin", {}, run=remote), 0)
        self.assertEqual(sorted(remote.deleted), ["feature/abandoned", "feature/merged", "feature/never",
                                                  "feature/squashed", "release/1.2.1-dryrun"])
        self.assertEqual(sorted(remote.tags), ["archive/feature/abandoned", "archive/feature/never",
                                               "archive/feature/squashed"])
        sha = {v.branch: v.sha for v in verdicts}
        for name in ("feature/abandoned", "feature/never", "feature/squashed"):
            self.assertEqual(remote.tags[f"archive/{name}"], sha[name])
            tag_at = next(i for i, c in enumerate(remote.calls) if c[-1].endswith(f"refs/tags/archive/{name}"))
            del_at = next(i for i, c in enumerate(remote.calls) if c[-1] == f":refs/heads/{name}")
            self.assertLess(tag_at, del_at, name)
        lease = next(c for c in remote.calls if c[-1] == ":refs/heads/feature/never")
        self.assertIn(f"--force-with-lease=refs/heads/feature/never:{sha['feature/never']}", lease)

    def test_a_tag_that_does_not_read_back_keeps_the_branch(self):
        remote = FakeRemote(lose_tags=True)
        verdicts = self.verdicts()
        self.assertEqual(bc.apply(verdicts, "origin", {}, run=remote), 3)
        self.assertEqual(remote.deleted, ["feature/merged", "release/1.2.1-dryrun"])

    def test_an_archive_tag_on_another_commit_keeps_the_branch(self):
        remote = FakeRemote()
        verdicts = self.verdicts()
        self.assertEqual(bc.apply(verdicts, "origin", {"archive/feature/never": "b" * 40}, run=remote), 1)
        self.assertNotIn("feature/never", remote.deleted)
        self.assertIn("already names", next(v for v in verdicts if v.branch == "feature/never").result)

    def test_a_refused_delete_is_a_failure(self):
        remote = FakeRemote(refuse={"feature/merged"})
        self.assertEqual(bc.apply(self.verdicts(), "origin", {}, run=remote), 1)

    def test_a_dry_run_changes_nothing(self):
        calls = []
        orig, orig_collect = bc.apply, bc.collect
        try:
            bc.apply = lambda *a, **k: calls.append(a) or 0
            bc.collect = lambda *a, **k: (BRANCHES, PRS, {"a" * 40}, {})
            import os
            os.environ.setdefault("GITHUB_TOKEN", "x")
            for mode in ("show", "apply", "Apply", ""):
                self.assertEqual(bc.main(["--mode", mode, "--repo", "o/r"]), 0)
        finally:
            bc.apply, bc.collect = orig, orig_collect
        self.assertEqual(calls, [], "only mode APPLY applies")


if __name__ == "__main__":
    unittest.main()
