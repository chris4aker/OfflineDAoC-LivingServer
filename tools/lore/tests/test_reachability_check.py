"""Issue #618: reachability.py --check fails when an unreachable count passes its cap (KNOWN_UNREACHABLE), shown on a
planted cap one below today's count, and passes at the caps.

    python3 -m unittest discover -s tools/lore/tests -v
"""
import contextlib
import io
import sys
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import reachability  # noqa: E402


class CheckFails(unittest.TestCase):
    def run_check(self):
        with contextlib.redirect_stdout(io.StringIO()) as out:
            code = reachability.main(["--check"])
        return code, out.getvalue()

    def test_passes_at_the_caps_and_fails_one_below(self):
        code, out = self.run_check()
        self.assertEqual(code, 0, out)
        rows = reachability.build()[0]
        bad = reachability.unreachable(rows)
        self.assertTrue(bad, "nothing unreachable today: plant a lower cap some other way")
        key, n = sorted(bad.items())[0]
        with mock.patch.dict(reachability.KNOWN_UNREACHABLE, {key: n - 1}):
            code, out = self.run_check()
        self.assertEqual(code, 1, out)
        self.assertIn("REACH over cap", out)


if __name__ == "__main__":
    unittest.main()
