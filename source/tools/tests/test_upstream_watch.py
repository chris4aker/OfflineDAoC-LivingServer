import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import upstream_watch as uw  # noqa: E402


class Classify(unittest.TestCase):
    def test_a_gameplay_fix_is_a_fix(self):
        self.assertEqual(uw.classify("Fix NRE when target isn't a player", ["GameServer/spells/DirtyTricks.cs"]), "fix")

    def test_a_build_fix_is_not_gameplay(self):
        self.assertEqual(uw.classify("Fix build warning", [".github/workflows/ci.yml"]), "other-fix")

    def test_a_gameplay_addition_is_a_feature(self):
        self.assertEqual(uw.classify("Add live-like spell interrupt", ["GameServer/ECS-Components/CastingComponent.cs"]),
                         "feature")

    def test_anything_else_is_other(self):
        self.assertEqual(uw.classify("Update README", ["README.md"]), "other")


class Report(unittest.TestCase):
    repo = {"name": "X", "url": "https://example.org/x", "branch": "main", "reviewed": "a" * 40}

    def test_nothing_new_says_so(self):
        self.assertIn("Nothing new", uw.report(self.repo, "b" * 40, []))

    def test_a_long_report_is_cut_below_the_issue_limit(self):
        commits = [{"sha": f"{i:040d}", "date": "2026-11-24", "author": "a", "subject": "Fix spell " + "x" * 200,
                    "paths": ["GameServer/spells/S.cs"], "kind": "fix"} for i in range(2000)]
        text = uw.report(self.repo, "b" * 40, commits)
        self.assertLessEqual(len(text), uw.MAX_BODY + 200)
        self.assertIn("cut at", text)


if __name__ == "__main__":
    unittest.main()
