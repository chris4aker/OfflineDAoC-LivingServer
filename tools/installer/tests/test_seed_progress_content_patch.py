"""seed_progress accepts a shipped content patch's fixed file (game.dll's era text, #634) as kept, and nothing else."""
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import seed_progress  # noqa: E402


class ContentPatchFixes(unittest.TestCase):
    def test_fixed_hash_is_read_from_the_shipped_patches(self):
        fixes = seed_progress.content_patch_fixes()
        released = "f55ed6b068e22ce8e1106871c2fad6ee10c18390bbb8b5dc772219ad8c8b83bb"
        self.assertEqual(fixes.get(("runtime/client-opendaoc/app/game.dll", released)),
                         "3b32bfc377e06fab01048dd0a4b9f20fa7e90fcdeffd0a3fb0e8ab5dbaeef931")

    def test_only_the_released_to_fixed_pair_counts(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "content-patches.json"
            p.write_text(json.dumps([{"Path": "a.dll", "Released": "AA", "Fixed": "BB"}]), encoding="utf-8")
            fixes = seed_progress.content_patch_fixes(p)
        self.assertEqual(fixes, {("a.dll", "aa"): "bb"})
        self.assertIsNone(fixes.get(("a.dll", "cc")))

    def test_missing_file_gives_no_fixes(self):
        self.assertEqual(seed_progress.content_patch_fixes(Path("/nonexistent/content-patches.json")), {})


if __name__ == "__main__":
    unittest.main()
