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


NAMING = """# Naming
<!-- naming-allowlist:start -->
| Pattern | Where | Why it stays |
|---|---|---|
| `OfflineDaoc\\.ProgressImport\\b` | assembly | installed games run the exe |
| `xq1\\|xq2` | test | an escaped pipe is part of the pattern |
<!-- naming-allowlist:end -->
<!-- naming-legacy:start -->
| Path | Why |
|---|---|
| `docs/history/**` | records |
<!-- naming-legacy:end -->
"""


class InternalNameTests(unittest.TestCase):
    """Issue #645: the internal check fails on a planted OfflineDaoc name and passes kept names and legacy paths."""

    def run_on(self, files, naming=NAMING):
        with tempfile.TemporaryDirectory() as d:
            root = Path(d)
            (root / "NAMING.md").write_text(naming, encoding="utf-8")
            for rel, text in files.items():
                (root / rel).parent.mkdir(parents=True, exist_ok=True)
                (root / rel).write_text(text, encoding="utf-8")
            return cb.check_internal(root, cb.load_naming(root / "NAMING.md"), sorted(files))

    def test_a_planted_namespace_fails(self):
        problems, _ = self.run_on({"src/Form.cs": "namespace OfflineDaoc.Launcher;\n",
                                   ".github/workflows/x.yml": "run: dotnet build source/tools/OfflineDaoc.Launcher\n"})
        self.assertEqual(sorted((p[0], p[1]) for p in problems), [(".github/workflows/x.yml", 1), ("src/Form.cs", 1)])

    def test_the_new_names_pass(self):
        problems, allowed = self.run_on({"src/Form.cs": "namespace DAoCLivingServer.Launcher; // DAoC-LivingServer\n"})
        self.assertEqual((problems, allowed), ([], []))

    def test_a_kept_name_passes_with_its_reason(self):
        problems, allowed = self.run_on({"x.csproj": "<AssemblyName>OfflineDaoc.ProgressImport</AssemblyName>\n"})
        self.assertEqual(problems, [])
        self.assertEqual([a[3] for a in allowed], ["installed games run the exe"])

    def test_a_kept_name_does_not_cover_another_on_the_same_line(self):
        problems, _ = self.run_on({"x.py": "copy('OfflineDaoc.ProgressImport.exe', 'OfflineDaoc.Launcher')\n"})
        self.assertEqual(len(problems), 1)

    def test_legacy_paths_are_skipped(self):
        problems, allowed = self.run_on({"docs/history/OLD.md": "source/tools/OfflineDaoc.Launcher\n"})
        self.assertEqual((problems, allowed), ([], []))

    def test_an_escaped_pipe_belongs_to_the_pattern(self):
        kept, _ = self.load(NAMING)
        self.assertEqual(kept[1][0].pattern, "xq1|xq2")

    def test_a_row_without_a_reason_or_pattern_is_refused(self):
        for bad in (NAMING.replace("installed games run the exe", ""),
                    NAMING.replace("`OfflineDaoc\\.ProgressImport\\b`", "OfflineDaoc")):
            with self.assertRaises(SystemExit):
                self.load(bad)

    def test_missing_markers_are_refused(self):
        with self.assertRaises(SystemExit):
            self.load("# Naming\n")

    def test_the_repository_passes(self):
        problems, _ = cb.check_internal()
        self.assertEqual(problems, [], "\n".join(f"{p[0]}:{p[1]}: {p[2]}" for p in problems))

    def load(self, text):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "NAMING.md"
            path.write_text(text, encoding="utf-8")
            return cb.load_naming(path)


if __name__ == "__main__":
    unittest.main()
