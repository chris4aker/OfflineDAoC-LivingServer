import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import check_versions as cv  # noqa: E402

REL = "https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/tag/"
README = f"""### Alive. **Beta 1.1.1 is out.**
> **[Download Beta 1.1.1]({REL}v1.1.1-beta)**
1. Download `LivingServer-Setup-1.1.1-beta.exe` from the [release]({REL}v1.1.1-beta).
2. Or `OfflineDAoC-LivingServer-1.1.1-beta.zip`. The log says `FORK_BUILD name=1.1.1-beta`.
Install [Offline DAoC](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.37b) first.
"""
CHANGELOG = "# Living Server changelog\n\n## Beta 1.1.1 — 2026-10-31\n\nFixes.\n\n## Beta 1.1 — 2026-10-29\n"
FORK = 'public const string BuildName = "1.1.1-beta";\npublic const string BaseRelease = "0.37";\n'
FORM = 'internal const string DisplayVersion = "0.37";\n'


class CheckVersions(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.write(readme=README, changelog=CHANGELOG, fork=FORK, form=FORM)

    def tearDown(self):
        shutil.rmtree(self.root)

    def write(self, readme=None, changelog=None, fork=None, form=None):
        for rel, text in ((cv.README, readme), (cv.CHANGELOG, changelog), (cv.FORK_BUILD_INFO, fork), (cv.MAIN_FORM, form)):
            if text is not None:
                path = self.root / rel
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8")

    def problems(self):
        return cv.check(self.root)

    def test_agreeing_files_pass(self):
        self.assertEqual(self.problems(), [])

    def test_readme_on_the_previous_release_fails(self):
        self.write(readme=README.replace("1.1.1", "1.1"))
        found = "\n".join(self.problems())
        for what in ("is out", "links release 1.1-beta", "LivingServer-Setup-1.1-beta.exe",
                     "OfflineDAoC-LivingServer-1.1-beta.zip", "FORK_BUILD name=1.1-beta"):
            self.assertIn(what, found)

    def test_in_development_heading_is_not_released(self):
        self.write(changelog=CHANGELOG.replace("## Beta 1.1.1", "## Beta 1.1.2 (in development)\n\n- a line\n\n## Beta 1.1.1"))
        self.assertEqual(self.problems(), [], "README stays on the released 1.1.1")
        self.write(fork=FORK.replace("1.1.1-beta", "1.1.2-beta"))
        self.assertEqual(self.problems(), [], "the build may already be named for the release in development")

    def test_build_name_ahead_without_a_heading_fails(self):
        self.write(fork=FORK.replace("1.1.1-beta", "1.1.2-beta"))
        self.assertTrue(any("BuildName is 1.1.2-beta" in p for p in self.problems()))

    def test_readme_ahead_of_the_changelog_fails(self):
        self.write(changelog=CHANGELOG.replace("## Beta 1.1.1 — 2026-10-31", "## Beta 1.1.1 (in development)"))
        self.assertTrue(self.problems())

    def test_display_version_must_be_the_base_release(self):
        self.write(form=FORM.replace("0.37", "0.36"))
        self.assertTrue(any("DisplayVersion is 0.36" in p for p in self.problems()))
        self.write(fork=FORK.replace('"0.37"', '"0.36,0.37"'))
        self.assertEqual(self.problems(), [])

    def test_display_version_holding_a_living_server_version_follows_the_build(self):
        self.write(form=FORM.replace("0.37", "1.1.1"))
        self.assertEqual(self.problems(), [])
        self.write(form=FORM.replace("0.37", "1.1"))
        self.assertTrue(any("DisplayVersion is 1.1" in p for p in self.problems()))


if __name__ == "__main__":
    unittest.main()
