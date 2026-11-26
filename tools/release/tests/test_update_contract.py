"""Tests for tools/release/update_contract.py and the frozen update contract (#685, docs/RELEASE-TRAIN.md).

The candidate release must be what a released launcher reads (asset names, URL prefix, digest form); a planted change
to any of them must show. The contract's values are also pinned against the launcher and Setup sources, so a change to
one is a red test here, not only in the Windows job.
"""
import hashlib
import json
import re
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import publish_release as pr  # noqa: E402
import update_contract as uc  # noqa: E402

ROOT = Path(__file__).resolve().parents[3]
UPDATES = ROOT / "source/tools/DAoCLivingServer.Launcher/Updates"

# What every released launcher since 1.2 relies on (1.2.2's own copy of the code has the same values).
FROZEN_REPO = "chris4aker/OfflineDAoC-LivingServer"
FROZEN_PREFIX = "https://github.com/" + FROZEN_REPO + "/releases/download/"
FROZEN_LEGACY_ASSET = "OfflineDAoC-LivingServer-{}.zip"
FROZEN_DIGEST = re.compile(r"^sha256:[0-9a-f]{64}$")


def make_package(root, build="1.2.3"):
    folder = Path(root) / f"LivingServer-{build}"
    folder.mkdir(parents=True)
    dll = b"server " + build.encode()
    (folder / "GameServer.dll").write_bytes(dll)
    for name in ("GameServer.pdb", "INSTALL.cmd", "ROLLBACK.cmd", "install-server-build.ps1",
                 "rollback-server-build.ps1", "fork-build-common.ps1"):
        (folder / name).write_text(name)
    (folder / "BUILD-INFO.txt").write_text(
        f"BuildName={build}\nBaseRelease=0.37\nCommit={'a' * 40}\nBranch=685/merge\n"
        f"GameServer.dll={hashlib.sha256(dll).hexdigest()}\nGameServer.pdb=x\n", encoding="utf-8")
    return folder


def released_launcher_accepts(feed_dir, build="1.2.3"):
    """What 1.2.2's launcher checks, in Python: the asset it looks for, its digest's shape, its URL prefix. None when it
    accepts, else why not. The Windows job runs the real code; this is the fast tripwire."""
    latest = json.loads((Path(feed_dir) / "latest.json").read_text(encoding="utf-8"))
    asset = next((a for a in latest["assets"] if a["name"].lower() == FROZEN_LEGACY_ASSET.format(build).lower()), None)
    if asset is None:
        return "no package asset"
    if not FROZEN_DIGEST.match(asset.get("digest") or ""):
        return "no SHA-256 digest"
    if not asset["browser_download_url"].startswith(FROZEN_PREFIX):
        return "the address isn't one of this project's release files"
    return None


class CandidateTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        make_package(self.root / "pkg")

    def tearDown(self):
        self.tmp.cleanup()

    def candidate(self, name="feed", **kw):
        return uc.candidate(self.root / "pkg", self.root / name, **kw)

    def test_the_clean_candidate_is_what_a_released_launcher_takes(self):
        feed = self.candidate()
        self.assertEqual(feed["tag"], "v1.2.3")
        self.assertEqual(feed["repository"], FROZEN_REPO)
        self.assertEqual(feed["assets"], ["DAoC-LivingServer-1.2.3.zip", "OfflineDAoC-LivingServer-1.2.3.zip"])
        self.assertIsNone(released_launcher_accepts(self.root / "feed"))
        latest = json.loads((self.root / "feed/latest.json").read_text())
        self.assertFalse(latest["prerelease"] or latest["draft"])
        for a in latest["assets"]:
            self.assertEqual(a["browser_download_url"], FROZEN_PREFIX + "v1.2.3/" + a["name"])
            self.assertEqual(a["digest"], "sha256:" + pr.sha256_file(self.root / "feed" / a["name"]))
            self.assertIn(a["browser_download_url"], feed["files"])
        self.assertEqual(json.loads((self.root / "feed/releases.json").read_text()), [latest])

    def test_the_zip_is_publish_releases(self):
        self.candidate()
        ref = self.root / "ref.zip"
        pr.write_zip(self.root / "pkg/LivingServer-1.2.3", ref)
        self.assertEqual(pr.sha256_file(ref), pr.sha256_file(self.root / "feed/DAoC-LivingServer-1.2.3.zip"))

    def test_planted_repo_path_breaks_it(self):
        self.candidate(repository="chris4aker/DAoC-LivingServer")
        self.assertIn("address", released_launcher_accepts(self.root / "feed"))

    def test_planted_repo_path_case_breaks_it(self):
        self.candidate(repository="chris4aker/offlinedaoc-livingserver")
        self.assertIn("address", released_launcher_accepts(self.root / "feed"))

    def test_planted_asset_rename_breaks_it(self):
        with mock.patch.object(pr, "legacy_zip_name", lambda build: None):
            self.candidate()
        self.assertEqual(released_launcher_accepts(self.root / "feed"), "no package asset")

    def test_planted_digest_format_breaks_it(self):
        with mock.patch.object(pr, "github_digest", lambda sha: "sha-256:" + sha):
            self.candidate()
        self.assertEqual(released_launcher_accepts(self.root / "feed"), "no SHA-256 digest")

    def test_a_candidate_tag_is_a_prerelease(self):
        feed = self.candidate(tag="v1.2.3-rc.2")
        latest = json.loads((self.root / "feed/latest.json").read_text())
        self.assertTrue(latest["prerelease"])
        self.assertEqual(feed["assets"], ["DAoC-LivingServer-1.2.3-rc.2.zip", "OfflineDAoC-LivingServer-1.2.3-rc.2.zip"])

    def test_a_tag_for_another_build_is_refused(self):
        with self.assertRaises(uc.Stop):
            self.candidate(tag="v1.2.4")


class ReleasedTests(unittest.TestCase):
    RELEASES = [
        {"tag_name": "v1.2.3-rc.1", "prerelease": True, "draft": False},
        {"tag_name": "v1.2.2", "prerelease": False, "draft": False},
        {"tag_name": "v1.2.1", "prerelease": False, "draft": False},
        {"tag_name": "navmesh-v1", "prerelease": False, "draft": False},
        {"tag_name": "v1.2.4", "prerelease": False, "draft": True},
        {"tag_name": "v0.37", "prerelease": False, "draft": False},
        {"tag_name": "v1.1.4-beta", "prerelease": False, "draft": False},
    ]

    def test_the_newest_full_release_below_the_candidate(self):
        self.assertEqual(uc.pick_released(self.RELEASES, "1.2.3")["tag_name"], "v1.2.2")

    def test_right_after_a_release_the_one_before_it(self):
        self.assertEqual(uc.pick_released(self.RELEASES, "1.2.2")["tag_name"], "v1.2.1")

    def test_betas_order_before_their_final(self):
        self.assertLess(uc.version_key("1.1.4-beta"), uc.version_key("1.1.4"))
        self.assertLess(uc.version_key("1.1.4"), uc.version_key("1.2"))
        self.assertEqual(uc.pick_released(self.RELEASES, "1.2")["tag_name"], "v1.1.4-beta")

    def test_nothing_older_is_refused(self):
        with self.assertRaises(uc.Stop):
            uc.pick_released(self.RELEASES, "1.0")


class RepositoryTests(unittest.TestCase):
    def test_the_live_repository_must_match_exactly(self):
        uc.check_repository(FROZEN_REPO)
        for other in ("chris4aker/DAoC-LivingServer", "chris4aker/offlinedaoc-livingserver"):
            with self.assertRaises(uc.Stop):
                uc.check_repository(other)


class FrozenContractTests(unittest.TestCase):
    """The contract's values in the code that ships (docs/RELEASE-TRAIN.md, "The frozen update contract")."""

    def src(self, name):
        return (UPDATES / name).read_text(encoding="utf-8")

    def test_repository_api_and_download_prefix(self):
        feed = self.src("ReleaseFeed.cs")
        self.assertEqual(uc.repository_from_source(), FROZEN_REPO)
        self.assertIn('LatestUrl = "https://api.github.com/repos/" + Repository + "/releases/latest"', feed)
        self.assertIn('DownloadPrefix = "https://github.com/" + Repository + "/releases/download/"', feed)
        self.assertIn("url.StartsWith(DownloadPrefix, StringComparison.Ordinal)", feed)
        self.assertIn(f'ProjectUrl = "https://github.com/{FROZEN_REPO}"',
                      (ROOT / "tools/installer/src/Program.cs").read_text(encoding="utf-8"))
        self.assertIn(f'"GITHUB_REPOSITORY", "{FROZEN_REPO}"', (ROOT / "tools/release/publish_release.py").read_text())

    def test_asset_names(self):
        info = self.src("ReleaseInfo.cs")
        self.assertIn('LegacyPackagePrefix = "OfflineDAoC-LivingServer-"', info)
        self.assertIn('PackagePrefix = "DAoC-LivingServer-"', info)
        for build in ("1.2.3", "1.2.3-rc.1", "1.3"):
            self.assertEqual(pr.legacy_zip_name(build), FROZEN_LEGACY_ASSET.format(build))
        self.assertEqual(pr.zip_name("1.2.2"), FROZEN_LEGACY_ASSET.format("1.2.2"))

    def test_digest_format(self):
        self.assertIn(r'@"^sha256:([0-9a-fA-F]{64})$"', self.src("PackageDigest.cs"))
        self.assertTrue(FROZEN_DIGEST.match(pr.github_digest("0" * 64)))

    def test_markers_and_handoff(self):
        upd = self.src("LauncherUpdater.cs")
        self.assertIn('TestBuildMarker = "installed-test-build.txt"', upd)
        self.assertIn('ResultFile = "last-update.txt"', upd)
        script = self.src("apply-update.ps1")
        for p in ("$PackageFolder", "$GameFolder", "$Build", "$Log", "$Result", "$Restart", "$Release", "$NoPause"):
            self.assertIn(p, script)
        self.assertIn("-GameFolder", script)
        self.assertIn('install-server-build.ps1" %*', (ROOT / "tools/fork-build/INSTALL.cmd").read_text())

    def test_base_files(self):
        self.assertIn('Upstream = "https://github.com/chris4aker/OfflineDAoC-base-files/releases/download/v"',
                      (ROOT / "tools/installer/src/Model.cs").read_text(encoding="utf-8"))

    def test_prereleases_never_latest(self):
        self.assertIn('"make_latest": "false" if prerelease else "true"', (ROOT / "tools/release/publish_release.py").read_text())


if __name__ == "__main__":
    unittest.main()
