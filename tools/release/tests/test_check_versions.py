import contextlib
import io
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
FORM = 'internal static string DisplayVersion => LivingServerVersion(LauncherBuild.Name);\n'
PINNED_FORM = 'internal const string DisplayVersion = "0.37";\n'
# 1.2 released as final (owner, 2026-11-19): tag v1.2, build 1.2, no "Beta".
FINAL_README = f"""### Alive. **1.2 is out.**
> **[Download Living Server 1.2]({REL}v1.2)**
1. Download `LivingServer-Setup-1.2.exe` from the [release]({REL}v1.2).
2. Or `OfflineDAoC-LivingServer-1.2.zip`. The log says `FORK_BUILD name=1.2`.
Install [Offline DAoC](https://github.com/chris4aker/OfflineDAoC-base-files/releases/tag/v0.37) first.
"""
FINAL_CHANGELOG = ("# Living Server changelog\n\n## 1.2 — 2026-11-20\n\nWhere Legends Wake.\n\n"
                   "## Beta 1.1.4 — 2026-11-12\n\nFixes.\n\n## 0.37-se.1 — 2026-10-18\n")


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

    def test_the_1_2_lines_headings_without_beta_are_in_development(self):
        # integration/1.2: "## 1.2.1 (in development)" and "## 1.2 (in development)" above a 1.1.2 still in development.
        self.write(changelog=CHANGELOG.replace("## Beta 1.1.1", "## 1.2.1 (in development)\n\n- a line\n\n"
                                               "## 1.2 (in development)\n\n- a line\n\n"
                                               "## Beta 1.1.2 (in development)\n\n- a line\n\n## Beta 1.1.1"))
        for build in ("1.2-beta", "1.2.1-beta", "1.1.2-beta", "1.1.1-beta"):
            self.write(fork=FORK.replace("1.1.1-beta", build))
            self.assertEqual(self.problems(), [], build)
        self.write(fork=FORK.replace("1.1.1-beta", "1.3-beta"))
        self.assertTrue(any("BuildName is 1.3-beta" in p for p in self.problems()))

    def test_a_dated_heading_without_beta_is_a_final_release(self):
        # From 1.2 on the releases are final (owner, 2026-11-19): "## 1.2 — <date>" is released, build 1.2, tag v1.2.
        self.write(changelog=CHANGELOG.replace("## Beta 1.1.1", "## 1.2 — 2026-11-20\n\n- a line\n\n## Beta 1.1.1"))
        found = "\n".join(self.problems())
        self.assertIn("the newest release in CHANGELOG-LIVING-SERVER.md is 1.2", found)
        self.assertIn("BuildName is 1.1.1-beta, expected 1.2", found)
        self.write(readme=FINAL_README, fork=FORK.replace("1.1.1-beta", "1.2"))
        self.assertEqual(self.problems(), [])

    def test_old_0x_headings_are_never_releases(self):
        # "## 0.37-se.1 — <date>" (and any 0.x) above the betas isn't read as released.
        for heading in ("## 0.37-se.1 — 2026-11-09", "## 0.38 — 2026-11-09", "## Developed as 0.37-se.2 — 2026-10-20"):
            self.write(changelog=CHANGELOG.replace("## Beta 1.1.1", f"{heading}\n\n- a line\n\n## Beta 1.1.1"))
            self.assertEqual(self.problems(), [], heading)

    def test_a_final_release_readme_must_drop_beta(self):
        self.write(changelog=FINAL_CHANGELOG, fork=FORK.replace("1.1.1-beta", "1.2"), readme=FINAL_README)
        self.assertEqual(self.problems(), [])
        for sentence in ("**Living Server 1.2 is out.**", "**1.2 is out.**"):
            self.write(readme=FINAL_README.replace("**1.2 is out.**", sentence))
            self.assertEqual(self.problems(), [], sentence)
        # The beta names of 1.2 are wrong once 1.2 is released as final.
        self.write(readme=FINAL_README.replace("**1.2 is out.**", "**Beta 1.2 is out.**").replace("v1.2)", "v1.2-beta)")
                   .replace("1.2.exe", "1.2-beta.exe").replace("1.2.zip", "1.2-beta.zip").replace("name=1.2`", "name=1.2-beta`"))
        found = "\n".join(self.problems())
        for what in ("says \"Beta 1.2 is out\"", "links release 1.2-beta", "LivingServer-Setup-1.2-beta.exe",
                     "OfflineDAoC-LivingServer-1.2-beta.zip", "FORK_BUILD name=1.2-beta"):
            self.assertIn(what, found)
        # The README still on Beta 1.1.4 after 1.2 is out.
        self.write(readme=README.replace("1.1.1", "1.1.4"))
        found = "\n".join(self.problems())
        for what in ("says \"Beta 1.1.4 is out\"", "links release 1.1.4-beta", "not the newest release 1.2"):
            self.assertIn(what, found)

    def test_a_final_release_needs_the_final_build_name(self):
        self.write(changelog=FINAL_CHANGELOG, readme=FINAL_README, fork=FORK.replace("1.1.1-beta", "1.2-beta"))
        self.assertTrue(any("BuildName is 1.2-beta, expected 1.2" in p for p in self.problems()))

    def test_links_to_other_tags_and_longer_versions_are_read_whole(self):
        self.write(changelog=FINAL_CHANGELOG, fork=FORK.replace("1.1.1-beta", "1.2"),
                   readme=FINAL_README + f"Old: [{REL}v1.2.1]({REL}v1.2.1).\n")
        self.assertTrue(any("links release 1.2.1 " in p for p in self.problems()), "v1.2.1 is not v1.2")

    def test_the_tag_must_be_the_newest_releases(self):
        self.write(changelog=FINAL_CHANGELOG, fork=FORK.replace("1.1.1-beta", "1.2"), readme=FINAL_README)
        self.assertEqual(cv.check(self.root, "v1.2"), [])
        for bad in ("v1.2-beta", "v1.1.4-beta", "v1.2.1", "1.2"):
            self.assertTrue(any(f"the tag {bad} is not the newest release" in p for p in cv.check(self.root, bad)), bad)
        # A beta release still takes its beta tag (Beta 1.1.1 is v1.1.1-beta).
        self.write(changelog=CHANGELOG, fork=FORK, readme=README)
        self.assertEqual(cv.check(self.root, "v1.1.1-beta"), [])
        self.assertTrue(any("the tag v1.1.1 is not" in p for p in cv.check(self.root, "v1.1.1")))

    def test_the_tag_needs_the_released_build_name_not_an_in_development_one(self):
        # While 1.2.1 is in development BuildName may be 1.2.1, but publishing v1.2 needs BuildName 1.2.
        self.write(changelog=FINAL_CHANGELOG.replace("## 1.2 —", "## 1.2.1 (in development)\n\n- a line\n\n## 1.2 —"),
                   readme=FINAL_README, fork=FORK.replace("1.1.1-beta", "1.2.1"))
        self.assertEqual(self.problems(), [])
        self.assertTrue(any("BuildName is 1.2.1, but the release v1.2 needs 1.2" in p for p in cv.check(self.root, "v1.2")))

    def test_a_test_build_tag_is_of_the_version_in_development(self):
        # Issue #637: v1.2.1-rc.1 is published while 1.2.1 is in development, from a package built as 1.2.1.
        changelog = FINAL_CHANGELOG.replace("## 1.2 —", "## 1.2.1 (in development)\n\n- a line\n\n## 1.2 —")
        self.write(changelog=changelog, readme=FINAL_README, fork=FORK.replace("1.1.1-beta", "1.2.1"))
        self.assertEqual(cv.check(self.root, "v1.2.1-rc.1"), [])
        self.assertEqual(cv.check(self.root, "v1.2.1-rc.7"), [])
        self.assertTrue(any("not of a version in development" in p for p in cv.check(self.root, "v1.2.2-rc.1")))
        self.assertTrue(any("not of a version in development" in p for p in cv.check(self.root, "v1.2-rc.1")))
        self.write(fork=FORK.replace("1.1.1-beta", "1.2"))
        self.assertTrue(any("BuildName is 1.2, but the test build v1.2.1-rc.1 needs 1.2.1" in p
                            for p in cv.check(self.root, "v1.2.1-rc.1")))

    def test_main_names_the_release_and_its_tag(self):
        self.write(changelog=FINAL_CHANGELOG, fork=FORK.replace("1.1.1-beta", "1.2"), readme=FINAL_README)
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            self.assertEqual(cv.main(["--root", str(self.root), "--tag", "v1.2"]), 0)
        self.assertIn("1.2 released (build 1.2, tag v1.2)", out.getvalue())
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(cv.main(["--root", str(self.root), "--tag", "v1.2-beta"]), 1)

    def test_build_name_ahead_without_a_heading_fails(self):
        self.write(fork=FORK.replace("1.1.1-beta", "1.1.2-beta"))
        self.assertTrue(any("BuildName is 1.1.2-beta" in p for p in self.problems()))

    def test_readme_ahead_of_the_changelog_fails(self):
        self.write(changelog=CHANGELOG.replace("## Beta 1.1.1 — 2026-10-31", "## Beta 1.1.1 (in development)"))
        self.assertTrue(self.problems())

    def test_a_pinned_display_version_fails(self):
        # Up to 1.2.1 the launcher showed "VERSION 0.37"; from 1.2.2 it shows the Living Server build.
        self.write(form=PINNED_FORM)
        self.assertTrue(any('DisplayVersion is pinned to "0.37"' in p for p in self.problems()))
        self.write(form=PINNED_FORM.replace("0.37", "1.1.1"))
        self.assertTrue(any('DisplayVersion is pinned to "1.1.1"' in p for p in self.problems()))

    def test_display_version_must_read_the_launcher_build(self):
        self.write(form='internal static string DisplayVersion => "Living Server";\n')
        self.assertTrue(any("doesn't read LauncherBuild.Name" in p for p in self.problems()))
        self.write(form=FORM)
        self.assertEqual(self.problems(), [])

    def test_base_release_is_still_required(self):
        self.write(fork=FORK.replace('public const string BaseRelease = "0.37";\n', ""))
        self.assertTrue(any("no BaseRelease" in p for p in self.problems()))

if __name__ == "__main__":
    unittest.main()
