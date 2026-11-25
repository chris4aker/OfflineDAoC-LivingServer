import hashlib
import json
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import publish_release as pr  # noqa: E402

TAG = "v1.1.2-beta"
BUILD = "1.1.2-beta"
COMMIT = "c" * 40
ZIP = "OfflineDAoC-LivingServer-1.1.2-beta.zip"
NOTES = ("# Living Server Beta 1.1.2: release notes\n\nHeader for maintainers.\n\n---\n\n### Title\n\nText.\n\n"
         "### Files\n\n| File | SHA-256 |\n|---|---|\n"
         "| `LivingServer-Setup-1.1.2-beta.exe` | (filled in when the exe is built) |\n"
         "| `OfflineDAoC-LivingServer-1.1.2-beta.zip` | (zip SHA-256: filled in by hand at publish) |\n\nEnd.\n")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def make_artifact(root, branch="main", commit=COMMIT, build=BUILD, dll=b"dll", tamper=False):
    folder = Path(root) / f"LivingServer-{build}"
    (folder / "docs").mkdir(parents=True)
    (folder / "launcher").mkdir()
    files = {"GameServer.dll": dll, "GameServer.pdb": b"pdb", "INSTALL.cmd": b"i", "ROLLBACK.cmd": b"r",
             "install-server-build.ps1": b"a", "rollback-server-build.ps1": b"b", "fork-build-common.ps1": b"c",
             "docs/KNOWN-ISSUES.md": b"k", "launcher/OfflineDAoC.exe": b"exe"}
    for name, data in files.items():
        (folder / name).write_bytes(data)
    info = [f"BuildName={build}", "BaseRelease=0.37", f"Commit={commit}", f"Branch={branch}",
            f"GameServer.dll={sha(dll)}", f"GameServer.pdb={sha(b'pdb')}",
            f"launcher/OfflineDAoC.exe={sha(b'other' if tamper else b'exe')}"]
    (folder / "BUILD-INFO.txt").write_text("\n".join(info) + "\n")
    return folder


class FakeGitHub:
    def __init__(self, tag=None, release=None, digest_ok=True):
        self.token, self.tag, self.rel, self.digest_ok, self.calls = "t", tag, release, digest_ok, []

    def tag_exists(self, tag):
        return self.tag

    def release_for(self, tag):
        return self.rel

    def create_draft(self, tag, commit, title, body, prerelease=False):
        self.calls.append(("create", tag, commit, title, prerelease) if prerelease else ("create", tag, commit, title))
        self.draft = {"url": "u", "upload_url": "x{?name}", "html_url": "h", "draft": True, "assets": []}
        return self.draft

    def upload(self, release, path, name):
        self.calls.append(("upload", name))
        digest = sha(Path(path).read_bytes()) if self.digest_ok else "0" * 64
        self.draft["assets"] = [{"name": name, "digest": f"sha256:{digest}"}]

    def get(self, release):
        return self.draft

    def publish(self, release, prerelease=False):
        self.calls.append(("publish", "prerelease", "not latest") if prerelease else ("publish",))
        return {"draft": False, "prerelease": prerelease, "html_url": "h"}


class PackageTests(unittest.TestCase):
    def test_zips_the_one_folder_in_the_earlier_releases_layout(self):
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a")
            target, digest = pr.package(Path(d) / "a", TAG, COMMIT, Path(d) / "out")
            self.assertEqual(target.name, ZIP)
            self.assertEqual(digest, sha(target.read_bytes()))
            names = zipfile.ZipFile(target).namelist()
            top = f"LivingServer-{BUILD}/"
            self.assertEqual(names[0], top)
            self.assertTrue(all(n.startswith(top) for n in names))
            # each folder: its entry, its files in name order, then its subfolders (as in v1.1.1-beta)
            self.assertLess(names.index(top + "fork-build-common.ps1"), names.index(top + "docs/"))
            self.assertEqual(names[1], top + "BUILD-INFO.txt")
            self.assertIn(top + "launcher/OfflineDAoC.exe", names)

    def test_refuses_a_pull_request_runs_package(self):
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", branch="111/merge")
            with self.assertRaisesRegex(pr.Stop, "Branch=111/merge"):
                pr.package(Path(d) / "a", TAG, COMMIT, Path(d) / "out")

    def test_refuses_another_commit_or_build(self):
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", commit="d" * 40)
            with self.assertRaisesRegex(pr.Stop, "Commit="):
                pr.package(Path(d) / "a", TAG, COMMIT, Path(d) / "out")
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", build="1.1.1-beta")
            with self.assertRaisesRegex(pr.Stop, "LivingServer-1.1.2-beta"):
                pr.package(Path(d) / "a", TAG, COMMIT, Path(d) / "out")

    def test_refuses_a_file_that_does_not_match_build_info(self):
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", tamper=True)
            with self.assertRaisesRegex(pr.Stop, "launcher/OfflineDAoC.exe"):
                pr.package(Path(d) / "a", TAG, COMMIT, Path(d) / "out")


class NotesTests(unittest.TestCase):
    def test_takes_the_text_below_the_line_and_fills_only_the_zip_row(self):
        body = pr.release_body(NOTES, BUILD, "a" * 64)
        self.assertTrue(body.startswith("### Title"))
        self.assertNotIn("Header for maintainers", body)
        self.assertIn(f"| `{ZIP}` | `{'a' * 64}` |", body)
        self.assertIn("| `LivingServer-Setup-1.1.2-beta.exe` | (filled in when the exe is built) |", body)

    def test_needs_the_setup_placeholder(self):
        with self.assertRaisesRegex(pr.Stop, "Setup row"):
            pr.release_body(NOTES.replace("(filled in when the exe is built)", "`x`"), BUILD, "a" * 64)

    def test_needs_the_zip_placeholder(self):
        with self.assertRaisesRegex(pr.Stop, "zip row"):
            pr.release_body(NOTES.replace("(zip SHA-256: filled in by hand at publish)", "tbd"), BUILD, "a" * 64)

    def test_refuses_leftover_owner_placeholders(self):
        with self.assertRaisesRegex(pr.Stop, "placeholders"):
            pr.release_body(NOTES.replace("Text.", "(owner: one line here)"), BUILD, "a" * 64)

    def test_the_repository_notes_are_ready(self):
        notes = Path(__file__).resolve().parents[3] / "docs" / "RELEASE-NOTES-1.1.2.md"
        body = pr.release_body(notes.read_text(encoding="utf-8"), BUILD, "a" * 64)
        self.assertNotIn("@@", body)


class PublishTests(unittest.TestCase):
    def zip(self, d):
        path = Path(d) / ZIP
        path.write_bytes(b"zip")
        return path, f"| `{ZIP}` | `{sha(b'zip')}` |"

    def test_creates_attaches_checks_and_publishes_in_that_order(self):
        with tempfile.TemporaryDirectory() as d:
            path, body = self.zip(d)
            gh = FakeGitHub()
            pr.publish(gh, TAG, COMMIT, "T", body, path, dry_run=False)
            self.assertEqual([c[0] for c in gh.calls], ["create", "upload", "publish"])

    def test_dry_run_changes_nothing(self):
        with tempfile.TemporaryDirectory() as d:
            path, body = self.zip(d)
            gh = FakeGitHub()
            pr.publish(gh, TAG, COMMIT, "T", body, path, dry_run=True)
            self.assertEqual(gh.calls, [])

    def test_never_moves_a_tag_or_reuses_a_release(self):
        with tempfile.TemporaryDirectory() as d:
            path, body = self.zip(d)
            with self.assertRaisesRegex(pr.Stop, "tag"):
                pr.publish(FakeGitHub(tag={"ref": "x"}), TAG, COMMIT, "T", body, path, dry_run=True)
            with self.assertRaisesRegex(pr.Stop, "exists already"):
                pr.publish(FakeGitHub(release={"draft": True}), TAG, COMMIT, "T", body, path, dry_run=True)

    def test_leaves_the_draft_unpublished_when_github_has_another_digest(self):
        with tempfile.TemporaryDirectory() as d:
            path, body = self.zip(d)
            gh = FakeGitHub(digest_ok=False)
            with self.assertRaisesRegex(pr.Stop, "unpublished"):
                pr.publish(gh, TAG, COMMIT, "T", body, path, dry_run=False)
            self.assertNotIn(("publish",), gh.calls)

    def test_the_notes_must_show_this_zip(self):
        with tempfile.TemporaryDirectory() as d:
            path, _ = self.zip(d)
            with self.assertRaisesRegex(pr.Stop, "don't show"):
                pr.publish(FakeGitHub(), TAG, COMMIT, "T", "no hash", path, dry_run=True)


class FinalReleaseTests(unittest.TestCase):
    """1.2 is a final release (owner, 2026-11-19): tag v1.2, build 1.2, no "-beta" anywhere."""
    TAG, BUILD, ZIP = "v1.2", "1.2", "OfflineDAoC-LivingServer-1.2.zip"

    def notes(self):
        return NOTES.replace("1.1.2-beta", self.BUILD).replace("Beta 1.1.2", "1.2")

    def test_tags_name_their_build(self):
        for tag, build in (("v1.2", "1.2"), ("v1.2.1", "1.2.1"), ("v2.0", "2.0"), ("v1.1.4-beta", "1.1.4-beta"),
                           ("v1.1-beta", "1.1-beta"), ("v1.2-beta", "1.2-beta")):
            self.assertEqual(pr.build_of(tag), build, tag)

    def test_refuses_what_is_not_a_living_server_tag(self):
        # v0.37 / v0.37b are Offline DAoC's own tags in this repository; the rest aren't release tags at all.
        for bad in ("1.2", "v1", "v0.37", "v0.37b", "v0.36-se.1", "v1.2-rc1", "v1.2-beta.1", "v1.2 ", "latest", ""):
            with self.assertRaises(pr.Stop, msg=bad):
                pr.build_of(bad)

    def test_zips_the_1_2_package_under_its_final_names(self):
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", build=self.BUILD)
            target, _ = pr.package(Path(d) / "a", self.TAG, COMMIT, Path(d) / "out")
            self.assertEqual(target.name, self.ZIP)
            self.assertEqual(zipfile.ZipFile(target).namelist()[0], "LivingServer-1.2/")

    def test_refuses_a_candidate_still_built_as_1_2_beta(self):
        # The launchers on 1.1.x would refuse it: they look for LivingServer-1.2 and BUILD-INFO BuildName=1.2.
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", build="1.2-beta")
            with self.assertRaisesRegex(pr.Stop, "LivingServer-1.2"):
                pr.package(Path(d) / "a", self.TAG, COMMIT, Path(d) / "out")
        with tempfile.TemporaryDirectory() as d:
            folder = make_artifact(Path(d) / "a", build=self.BUILD)
            info = folder / "BUILD-INFO.txt"
            info.write_text(info.read_text().replace("BuildName=1.2", "BuildName=1.2-beta"))
            with self.assertRaisesRegex(pr.Stop, "BuildName=1.2-beta, expected 1.2"):
                pr.package(Path(d) / "a", self.TAG, COMMIT, Path(d) / "out")

    def test_notes_rows_use_the_final_names(self):
        body = pr.release_body(self.notes(), self.BUILD, "a" * 64)
        self.assertIn(f"| `{self.ZIP}` | `{'a' * 64}` |", body)
        self.assertIn("| `LivingServer-Setup-1.2.exe` | (filled in when the exe is built) |", body)
        with self.assertRaisesRegex(pr.Stop, "LivingServer-Setup-1.2.exe"):
            pr.release_body(NOTES.replace("1.1.2", "1.2"), self.BUILD, "a" * 64)   # rows still named 1.2-beta

    def test_the_1_2_notes_file_table_names_the_final_files(self):
        notes = (Path(__file__).resolve().parents[3] / "docs" / "RELEASE-NOTES-1.2.md").read_text(encoding="utf-8")
        rows = [l for l in notes.splitlines() if l.lstrip().startswith("|")]
        self.assertEqual([l for l in rows if "`LivingServer-Setup-1.2.exe`" in l],
                         ["| `LivingServer-Setup-1.2.exe` | (filled in when the exe is built) |"])
        self.assertEqual(len([l for l in rows if f"`{self.ZIP}`" in l]), 1)
        self.assertNotIn("1.2-beta", notes)

    def test_publishes_v1_2_as_latest(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / self.ZIP
            path.write_bytes(b"zip")
            gh = FakeGitHub()
            pr.publish(gh, self.TAG, COMMIT, "Living Server 1.2 — Where Legends Wake", f"`{sha(b'zip')}`", path,
                       dry_run=False)
            self.assertEqual(gh.calls[0], ("create", "v1.2", COMMIT, "Living Server 1.2 — Where Legends Wake"))
            self.assertEqual(gh.calls[1], ("upload", self.ZIP))
            beta = Path(d) / "OfflineDAoC-LivingServer-1.2-beta.zip"
            beta.write_bytes(b"zip")
            with self.assertRaisesRegex(pr.Stop, "needs OfflineDAoC-LivingServer-1.2.zip"):
                pr.publish(FakeGitHub(), self.TAG, COMMIT, "T", f"`{sha(b'zip')}`", beta, dry_run=True)


if __name__ == "__main__":
    unittest.main()


class PrereleaseTests(unittest.TestCase):
    """Test builds (issue #637): v1.2.3-rc.1 carries the push run's 1.2.3 package and is never Latest."""
    TAG = "v1.2.3-rc.1"

    def test_candidate_tags_name_the_final_build_of_their_package(self):
        self.assertEqual(pr.builds_of("v1.2.3-rc.1", True), ("1.2.3", "1.2.3-rc.1"))
        self.assertEqual(pr.builds_of("v1.2.3-rc.12", True), ("1.2.3", "1.2.3-rc.12"))
        self.assertEqual(pr.builds_of("v1.2.3", False), ("1.2.3", "1.2.3"))

    def test_a_candidate_must_be_a_prerelease_and_a_prerelease_a_candidate(self):
        for tag, prerelease in (("v1.2.3-rc.1", False), ("v1.2.3", True), ("v1.2.3-rc.0", True), ("v1.2.3-beta", True),
                                ("v0.37-rc.1", True), ("v1.2.3-rc1", True)):
            with self.subTest(tag=tag, prerelease=prerelease), self.assertRaises(pr.Stop):
                pr.builds_of(tag, prerelease)

    def test_zips_the_final_builds_package_under_the_candidates_name(self):
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", build="1.2.3")
            target, digest = pr.package(Path(d) / "a", self.TAG, COMMIT, Path(d) / "out", prerelease=True)
            self.assertEqual(target.name, "OfflineDAoC-LivingServer-1.2.3-rc.1.zip")
            with zipfile.ZipFile(target) as z:
                self.assertTrue(all(n.startswith("LivingServer-1.2.3/") for n in z.namelist()))
            self.assertEqual(digest, sha(target.read_bytes()))

    def test_refuses_a_package_of_another_build(self):
        with tempfile.TemporaryDirectory() as d:
            make_artifact(Path(d) / "a", build="1.2.2")
            with self.assertRaises(pr.Stop):
                pr.package(Path(d) / "a", self.TAG, COMMIT, Path(d) / "out", prerelease=True)

    def test_notes_are_short_and_show_the_zip(self):
        body = pr.prerelease_body("1.2.3-rc.1", "a" * 64)
        self.assertIn("test build 1", body)
        self.assertIn("| `OfflineDAoC-LivingServer-1.2.3-rc.1.zip` | `" + "a" * 64 + "` |", body)
        self.assertNotIn("tests only", body.lower())
        self.assertNotIn("owner", body.lower())

    def _zip(self, d):
        path = Path(d) / "OfflineDAoC-LivingServer-1.2.3-rc.1.zip"
        path.write_bytes(b"zip")
        return path

    def test_publishes_as_a_prerelease_never_latest(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._zip(d)
            gh = FakeGitHub()
            pr.publish(gh, self.TAG, COMMIT, "T", pr.prerelease_body("1.2.3-rc.1", sha(b"zip")), path, False, prerelease=True)
            self.assertEqual(gh.calls, [("create", self.TAG, COMMIT, "T", True), ("upload", path.name),
                                        ("publish", "prerelease", "not latest")])

    def test_dry_run_publishes_nothing(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._zip(d)
            gh = FakeGitHub()
            pr.publish(gh, self.TAG, COMMIT, "T", pr.prerelease_body("1.2.3-rc.1", sha(b"zip")), path, True, prerelease=True)
            self.assertEqual(gh.calls, [])

    def test_the_real_publish_call_asks_for_a_prerelease_that_is_not_latest(self):
        sent = []

        class Recording(pr.GitHub):
            def request(self, method, url, data=None, content_type="application/json", missing_ok=False):
                sent.append((method, json.loads(data) if data else None))
                return {"draft": False, "prerelease": True}

        gh = Recording(token="t")
        gh.create_draft(self.TAG, COMMIT, "T", "b", True)
        gh.publish({"url": "u"}, True)
        self.assertEqual(sent[0][1]["prerelease"], True)
        self.assertEqual(sent[1][1], {"draft": False, "prerelease": True, "make_latest": "false"})
        sent.clear()
        gh.publish({"url": "u"})
        self.assertEqual(sent[0][1], {"draft": False, "prerelease": False, "make_latest": "true"}, "finals unchanged")

    def test_the_command_line_notes_for_a_prerelease_need_no_notes_file(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._zip(d)
            out = Path(d) / "notes.md"
            self.assertEqual(pr.run(["notes", "--prerelease", "--tag", self.TAG, "--zip", str(path), "--out", str(out)]), 0)
            self.assertIn(sha(b"zip"), out.read_text(encoding="utf-8"))
