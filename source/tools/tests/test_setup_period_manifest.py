"""#725: DAoCLivingServer.Setup restores only the period manifest's archive section.

classic165_period_restored_spawn_ids.txt holds the 1,290 archived period-map rows Setup's migration
restores, then '#'-headed sections of new camp IDs that only the game server's catalog reads. Setup must
stop at the first '#' line, or its count check (1,290) and its archive completeness check both fail.
"""
import re
import unittest
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
MANIFEST = ROOT / "source/server/GameServer/bots/autonomous/data/classic165_period_restored_spawn_ids.txt"
SETUP = ROOT / "source/tools/DAoCLivingServer.Setup/Program.cs"


def archive_section(lines):
    ids = []
    for line in lines:
        line = line.strip()
        if not line:
            continue
        if line.startswith("#"):
            break
        ids.append(line)
    return ids


def valid_id(text):
    try:
        uuid.UUID(text)
        return True
    except ValueError:
        return text.isdigit() and int(text) > 0


class SetupPeriodManifestTests(unittest.TestCase):
    def setUp(self):
        self.lines = MANIFEST.read_text(encoding="ascii").splitlines()
        self.setup = SETUP.read_text(encoding="utf-8")

    def test_the_archive_section_holds_the_count_setup_expects(self):
        expected = int(re.search(r"ExpectedPeriodMapRestoredSpawnCount\s*=\s*(\d+);", self.setup).group(1))
        ids = archive_section(self.lines)
        self.assertEqual(len(ids), expected)
        self.assertEqual(len(set(i.lower() for i in ids)), expected, "no duplicates")
        self.assertTrue(all(valid_id(i) for i in ids), "every archive line is a Mob_ID")

    def test_the_later_sections_start_with_a_comment_line(self):
        index = next(i for i, line in enumerate(self.lines) if line.strip().startswith("#"))
        self.assertGreater(len(self.lines), index + 1, "the camp sections follow")

    def test_setup_stops_at_the_first_comment_line_for_the_period_manifest(self):
        self.assertIn('LoadRestoredSpawnIds("classic165_period_restored_spawn_ids.txt", archiveSectionOnly: true)', self.setup)
        body = self.setup[self.setup.index("private static string[] LoadRestoredSpawnIds("):]
        body = body[:body.index("return result")]
        self.assertRegex(body, r"if \(line\.StartsWith\('#'\)\)\s*\{\s*if \(archiveSectionOnly\)\s*break;\s*continue;\s*\}")
        self.assertLess(body.index("StartsWith('#')"), body.index("Invalid Mob_ID"), "comments are handled before the ID check")


if __name__ == "__main__":
    unittest.main()
