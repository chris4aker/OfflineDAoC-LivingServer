import hashlib
import json
import re
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
        self.draft["assets"] = self.draft["assets"] + [{"name": name, "digest": f"sha256:{digest}"}]

    def get(self, release):
        return self.draft

    def publish(self, release, prerelease=False):
        self.calls.append(("publish", "prerelease", "not latest") if prerelease else ("publish",))
        return {"draft": False, "prerelease": prerelease, "html_url": "h"}


class RenameTests(unittest.TestCase):
    """Issue #641: from 1.2.3 the files are DAoC-LivingServer-<build>.zip and DAoC-LivingServer-Setup-<build>.exe, and a
    release also carries the zip under its old name for 1.2.0-1.2.2 launchers. Earlier builds keep their names."""

    def test_names_by_build(self):
        self.assertEqual(pr.zip_name("1.2.2"), "OfflineDAoC-LivingServer-1.2.2.zip")
        self.assertEqual(pr.setup_name("1.2.2"), "LivingServer-Setup-1.2.2.exe")
        self.assertIsNone(pr.legacy_zip_name("1.2.2"))
        self.assertEqual(pr.zip_name("1.1.4-beta"), "OfflineDAoC-LivingServer-1.1.4-beta.zip")
        for build in ("1.2.3", "1.2.3-rc.1", "1.2.10", "1.3", "2.0"):
            with self.subTest(build=build):
                self.assertEqual(pr.zip_name(build), f"DAoC-LivingServer-{build}.zip")
                self.assertEqual(pr.setup_name(build), f"DAoC-LivingServer-Setup-{build}.exe")
                self.assertEqual(pr.legacy_zip_name(build), f"OfflineDAoC-LivingServer-{build}.zip")

    def test_a_renamed_release_carries_the_zip_under_both_names_and_checks_both(self):
        body = ("| `DAoC-LivingServer-Setup-1.2.3.exe` | (filled in when the exe is built) |\n"
                "| `DAoC-LivingServer-1.2.3.zip` | `" + sha(b"zip") + "` |\n")
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "DAoC-LivingServer-1.2.3.zip"
            path.write_bytes(b"zip")
            gh = FakeGitHub()
            pr.publish(gh, "v1.2.3", COMMIT, "T", body, path, False)
            self.assertEqual(gh.calls, [("create", "v1.2.3", COMMIT, "T"), ("upload", "DAoC-LivingServer-1.2.3.zip"),
                                        ("upload", "OfflineDAoC-LivingServer-1.2.3.zip"), ("publish",)])
            bad = FakeGitHub(digest_ok=False)
            with self.assertRaises(pr.Stop):
                pr.publish(bad, "v1.2.3", COMMIT, "T", body, path, False)
            self.assertNotIn(("publish",), bad.calls)

    def test_the_old_name_is_refused_for_a_renamed_build(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "OfflineDAoC-LivingServer-1.2.3.zip"
            path.write_bytes(b"zip")
            with self.assertRaisesRegex(pr.Stop, "needs DAoC-LivingServer-1.2.3.zip"):
                pr.publish(FakeGitHub(), "v1.2.3", COMMIT, "T", "`" + sha(b"zip") + "`", path, True)


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
            self.assertEqual(target.name, "DAoC-LivingServer-1.2.3-rc.1.zip")
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
        self.assertIn("| `DAoC-LivingServer-1.2.3-rc.1.zip` | `" + "a" * 64 + "` |", body)
        self.assertNotIn("tests only", body.lower())
        self.assertNotIn("owner", body.lower())

    def _zip(self, d):
        path = Path(d) / "DAoC-LivingServer-1.2.3-rc.1.zip"
        path.write_bytes(b"zip")
        return path

    def test_publishes_as_a_prerelease_never_latest(self):
        with tempfile.TemporaryDirectory() as d:
            path = self._zip(d)
            gh = FakeGitHub()
            pr.publish(gh, self.TAG, COMMIT, "T", pr.prerelease_body("1.2.3-rc.1", sha(b"zip")), path, False, prerelease=True)
            # The same file under the pre-1.2.3 name too, for 1.2.x launchers with test builds on (#641).
            self.assertEqual(gh.calls, [("create", self.TAG, COMMIT, "T", True), ("upload", path.name),
                                        ("upload", "OfflineDAoC-LivingServer-1.2.3-rc.1.zip"),
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


class CheckJobsTests(unittest.TestCase):
    """Issue #623: publish-release refuses a run where any required job did not succeed."""

    @staticmethod
    def jobs(**overrides):
        names = list(pr.REQUIRED_JOBS) + ["candidate-install", "report: chat audit"]
        rows = [{"name": n, "status": "completed", "conclusion": overrides.get(n, "success")} for n in names]
        return {"total_count": len(rows), "jobs": [r for r in rows if r["conclusion"] != "absent"]}

    def test_all_required_jobs_green_passes(self):
        checked = pr.check_jobs(self.jobs(**{"report: chat audit": "failure"}))
        self.assertEqual([n for n, _ in checked], list(pr.REQUIRED_JOBS))

    def test_a_failed_required_job_is_refused(self):
        with self.assertRaisesRegex(pr.Stop, r"launcher: failure"):
            pr.check_jobs(self.jobs(launcher="failure"))

    def test_a_skipped_or_cancelled_required_job_is_refused(self):
        with self.assertRaisesRegex(pr.Stop, r"new-client: skipped.*versions: cancelled"):
            pr.check_jobs(self.jobs(**{"new-client": "skipped", "versions": "cancelled"}))

    def test_a_missing_required_job_is_refused(self):
        data = self.jobs(server="absent")
        data["total_count"] = len(data["jobs"])
        with self.assertRaisesRegex(pr.Stop, r"server: not in the run"):
            pr.check_jobs(data)

    def test_an_unfinished_job_is_refused(self):
        data = self.jobs()
        data["jobs"][0].update(status="in_progress", conclusion=None)
        with self.assertRaisesRegex(pr.Stop, r"server: in_progress"):
            pr.check_jobs(data)

    def test_a_truncated_job_list_is_refused(self):
        data = self.jobs()
        data["total_count"] = 150
        with self.assertRaisesRegex(pr.Stop, r"only 7 were listed"):
            pr.check_jobs(data)

    def test_the_command_line_exits_1_and_says_nothing_was_published(self):
        import contextlib
        import io
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "jobs.json"
            path.write_text(json.dumps(self.jobs(**{"encounter-catalog": "failure"})))
            with contextlib.redirect_stdout(io.StringIO()) as out:
                code = pr.run(["check-jobs", "--jobs", str(path)])
        self.assertEqual(code, 1)
        self.assertIn("encounter-catalog: failure", out.getvalue())

    def test_required_jobs_are_build_and_test_jobs_with_their_own_names(self):
        text = (Path(__file__).resolve().parents[3] / ".github/workflows/build-and-test.yml").read_text(encoding="utf-8")
        for name in pr.REQUIRED_JOBS:
            m = re.search(rf"(?m)^  {re.escape(name)}:\n((?:    .*\n|\n)*)", text)
            self.assertIsNotNone(m, f"{name} is not a build-and-test job")
            # A `name:` on the job would change the name the jobs API reports.
            self.assertNotRegex(m.group(1), r"(?m)^    name:", f"{name} has its own display name")

    def test_the_workflow_checks_the_jobs_before_downloading_the_package(self):
        text = (Path(__file__).resolve().parents[3] / ".github/workflows/publish-release.yml").read_text(encoding="utf-8")
        check = text.index("publish_release.py check-jobs")
        self.assertLess(text.index("- name: The run to publish from"), check)
        self.assertLess(check, text.index("- name: Download the run's package"))
        self.assertIn("jobs?filter=latest&per_page=100", text)
