"""Tests for source/tools/assemble_changelog.py (changelog fragments, issue #615).

    python3 -m unittest discover -s source/tools/tests -v
"""
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import assemble_changelog as ac  # noqa: E402

CHANGELOG = """# Living Server changelog

Intro.

## 1.2.3 (in development)

Lead paragraph.

**Bosses**
- old boss entry

**Chat** (a note after the section name)
- old chat entry

## 1.2.2 — 2026-11-23

**Bosses**
- released entry
"""


def fragment(version="1.2.3", section="Bosses", body="- new entry"):
    return f"---\nversion: {version}\nsection: {section}\n---\n{body}\n"


class Repo:
    def __init__(self, fragments, changelog=CHANGELOG):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / "changelog.d").mkdir()
        (self.root / "changelog.d/README.md").write_text("# not a fragment\n", encoding="utf-8")
        (self.root / ac.CHANGELOG).write_text(changelog, encoding="utf-8")
        for name, text in fragments.items():
            (self.root / "changelog.d" / name).write_text(text, encoding="utf-8")

    def run(self, *args):
        return ac.main([*args, "--repo", str(self.root)])

    def changelog(self):
        return (self.root / ac.CHANGELOG).read_text(encoding="utf-8")

    def close(self):
        self.tmp.cleanup()


class AssembleTests(unittest.TestCase):
    def assemble(self, fragments, changelog=CHANGELOG):
        repo = Repo(fragments, changelog)
        self.addCleanup(repo.close)
        self.assertEqual(repo.run(), 0)
        return repo

    def test_fragments_go_at_the_end_of_their_section_in_file_name_order(self):
        repo = self.assemble({"b.md": fragment(body="- second"), "a.md": fragment(body="- first"),
                              "c.md": fragment(section="Chat", body="- chat new")})
        text = repo.changelog()
        self.assertIn("**Bosses**\n- old boss entry\n- first\n- second\n\n**Chat**", text)
        self.assertIn("- old chat entry\n- chat new\n\n## 1.2.2", text)
        self.assertEqual(text.count("- released entry"), 1)
        self.assertEqual(sorted(p.name for p in (repo.root / "changelog.d").iterdir()), ["README.md"])

    def test_order_does_not_depend_on_creation_order(self):
        a = self.assemble({"615.md": fragment(body="- x"), "600.md": fragment(body="- y"), "fix-z.md": fragment(body="- z")})
        b = self.assemble({"fix-z.md": fragment(body="- z"), "600.md": fragment(body="- y"), "615.md": fragment(body="- x")})
        self.assertEqual(a.changelog(), b.changelog())
        self.assertIn("- old boss entry\n- y\n- x\n- z\n", a.changelog())

    def test_a_new_section_is_added_at_the_end_of_the_block(self):
        text = self.assemble({"2.md": fragment(section="RvR", body="- rvr"), "1.md": fragment(section="Tests", body="- t")}).changelog()
        self.assertIn("- old chat entry\n\n**Tests**\n- t\n\n**RvR**\n- rvr\n\n## 1.2.2", text)

    def test_a_missing_version_block_is_made_above_the_newest(self):
        text = self.assemble({"1.md": fragment(version="1.2.4")}).changelog()
        self.assertIn("Intro.\n\n## 1.2.4 (in development)\n\n**Bosses**\n- new entry\n\n## 1.2.3 (in development)", text)

    def test_multi_line_entries_keep_their_lines(self):
        text = self.assemble({"1.md": fragment(body="- one\n  continued\n- two")}).changelog()
        self.assertIn("- old boss entry\n- one\n  continued\n- two\n\n**Chat**", text)

    def test_a_second_run_changes_nothing(self):
        repo = self.assemble({"1.md": fragment()})
        before = repo.changelog()
        self.assertEqual(repo.run(), 0)
        self.assertEqual(repo.changelog(), before)

    def test_check_and_dry_run_change_nothing(self):
        repo = Repo({"1.md": fragment()})
        self.addCleanup(repo.close)
        self.assertEqual(repo.run("--check"), 0)
        self.assertEqual(repo.run("--dry-run"), 0)
        self.assertEqual(repo.changelog(), CHANGELOG)
        self.assertTrue((repo.root / "changelog.d/1.md").exists())


class ValidationTests(unittest.TestCase):
    def check(self, name, text):
        repo = Repo({name: text})
        self.addCleanup(repo.close)
        return repo.run("--check")

    def test_good_fragment_passes(self):
        self.assertEqual(self.check("615.md", fragment()), 0)

    def test_bad_fragments_fail(self):
        bad = {
            "no front matter": ("1.md", "- just text\n"),
            "unclosed front matter": ("1.md", "---\nversion: 1.2.3\nsection: Bosses\n- text\n"),
            "missing section": ("1.md", "---\nversion: 1.2.3\n---\n- text\n"),
            "unknown key": ("1.md", "---\nversion: 1.2.3\nsection: Bosses\nauthor: x\n---\n- text\n"),
            "bad version": ("1.md", fragment(version="next")),
            "released version": ("1.md", fragment(version="1.2.2")),
            "bold section": ("1.md", fragment(section="**Bosses**")),
            "empty body": ("1.md", fragment(body="")),
            "heading in body": ("1.md", fragment(body="## 1.2.3\n- x")),
            "bad file name": ("my fix.md", fragment()),
        }
        for why, (name, text) in bad.items():
            with self.subTest(why):
                self.assertEqual(self.check(name, text), 1)

    def test_the_repository_fragments_are_valid(self):
        frags, problems = ac.load(ac.REPO)
        changelog = (ac.REPO / ac.CHANGELOG).read_text(encoding="utf-8")
        self.assertEqual(problems + ac.validate(frags, changelog), [])


if __name__ == "__main__":
    unittest.main()
