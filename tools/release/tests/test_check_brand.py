import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_brand as cb  # noqa: E402

SURFACES = [("cs", "src/**/*.cs"), ("ps", "scripts/*.ps1"), ("text", "docs/*.md")]


class BrandCheckTests(unittest.TestCase):
    """Issue #641: the brand check fails on a planted player-visible old name and passes the clean and allowed ones."""

    def run_on(self, files, allowlist=None):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            for rel, text in files.items():
                (root / rel).parent.mkdir(parents=True, exist_ok=True)
                (root / rel).write_text(text, encoding="utf-8")
            return cb.check(root, allowlist or {"files": {}, "lines": {}}, SURFACES)

    def test_a_planted_launcher_string_fails(self):
        problems, _ = self.run_on({"src/Form.cs": 'Text = "Welcome to Offline DAoC";\n'})
        self.assertEqual([(p[0], p[1]) for p in problems], [("src/Form.cs", 1)])

    def test_a_planted_doc_line_and_script_message_fail(self):
        problems, _ = self.run_on({"docs/PLAY.md": "Install OfflineDAoC first.\n",
                                   "scripts/install.ps1": "Write-Host 'Made by shadowofze'\n"})
        self.assertEqual(sorted(p[0] for p in problems), ["docs/PLAY.md", "scripts/install.ps1"])

    def test_comments_are_not_player_visible(self):
        problems, _ = self.run_on({"src/Form.cs": "// Offline DAoC did this first\n/// shadowofze's code\nint x = 1; \n",
                                   "scripts/install.ps1": "# Offline DAoC\nrem OfflineDAoC\n"})
        self.assertEqual(problems, [])

    def test_compatibility_names_pass(self):
        problems, allowed = self.run_on({
            "src/Updater.cs": 'string a = "OfflineDAoC.exe"; string b = "OfflineDAoC-LivingServer-" + v; '
                              'string c = "https://github.com/chris4aker/OfflineDAoC-LivingServer";\n',
            "docs/README.md": "Files: `OfflineDAoC-v0.36b.zip.001`, `Get-OfflineDAoC.ps1`.\n",
        })
        self.assertEqual(problems, [])
        self.assertEqual(len(allowed), 2)

    def test_an_allowlisted_line_and_file_pass_with_their_reason(self):
        allow = {"files": {"docs/CREDITS*.md": {"why": "credits"}},
                 "lines": {"docs/README.md": [{"text": "started from Offline DAoC", "why": "former name"}]}}
        problems, allowed = self.run_on({"docs/CREDITS.md": "By shadowofze.\n",
                                         "docs/README.md": "It started from Offline DAoC.\nOffline DAoC rocks.\n"}, allow)
        self.assertEqual([(p[0], p[1]) for p in problems], [("docs/README.md", 2)], "only the listed line is allowed")
        self.assertEqual(sorted(a[3] for a in allowed), ["credits", "former name"])

    def test_an_entry_without_a_reason_is_refused(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "allow.json"
            path.write_text('{"files": {}, "lines": {"README.md": [{"text": "Offline DAoC"}]}}', encoding="utf-8")
            with self.assertRaises(SystemExit):
                cb.load_allowlist(path)

    def test_the_repository_passes(self):
        problems, _ = cb.check()
        self.assertEqual(problems, [], "\n".join(f"{p[0]}:{p[1]}: {p[2]}" for p in problems))


if __name__ == "__main__":
    unittest.main()
