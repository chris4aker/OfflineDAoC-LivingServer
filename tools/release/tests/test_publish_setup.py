import copy
import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import publish_setup as ps  # noqa: E402

TAG = "v1.1.1-beta"
EXE = "LivingServer-Setup-1.1.1-beta.exe"
ZIP = "OfflineDAoC-LivingServer-1.1.1-beta.zip"
# The v1.1.1-beta notes' file table as the owner wrote it before the exe was built (CRLF, as GitHub stores it).
BODY = ("### Install\r\n\r\n- run `LivingServer-Setup-1.1.1-beta.exe`.\r\n\r\n### Files\r\n\r\n| File | SHA-256 |\r\n|---|---|\r\n"
        "| `LivingServer-Setup-1.1.1-beta.exe` | (filled in when the exe is built) |\r\n"
        "| `OfflineDAoC-LivingServer-1.1.1-beta.zip` | `c640533245d815aa0aa5c5c4385591ba8217fe6505b5329f1de102599cee8c93` |\r\n"
        "\r\nFull details: CHANGELOG.\r\n")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def release(body=BODY, exe_sha=None, zip_sha="a" * 64, draft=False):
    assets = [{"name": ZIP, "digest": f"sha256:{zip_sha}"}]
    if exe_sha:
        assets.append({"name": EXE, "digest": f"sha256:{exe_sha}"})
    return {"tag_name": TAG, "draft": draft, "body": body, "assets": assets, "id": 1,
            "url": "https://api.github.com/repos/x/y/releases/1",
            "upload_url": "https://uploads.github.com/repos/x/y/releases/1/assets{?name,label}"}


class FakeGitHub:
    def __init__(self, rel):
        self.rel, self.token, self.uploads, self.edits = rel, "token", [], []

    def release(self, tag):
        return copy.deepcopy(self.rel)

    def upload(self, rel, path, name):
        data = Path(path).read_bytes()
        self.uploads.append(name)
        self.rel["assets"].append({"name": name, "digest": f"sha256:{sha(data)}"})

    def edit_body(self, rel, body):
        self.edits.append(body)
        self.rel["body"] = body


class FillNotes(unittest.TestCase):
    def test_replaces_only_the_placeholder_and_keeps_crlf(self):
        body, _ = ps.fill_notes(BODY, EXE, "b" * 64)
        self.assertEqual(body, BODY.replace("(filled in when the exe is built)", f"`{'b' * 64}`"))
        self.assertEqual(body.count("\r\n"), BODY.count("\r\n"))
        self.assertNotIn("\n |", body)

    def test_notes_that_already_show_the_hash_are_left(self):
        filled, _ = ps.fill_notes(BODY, EXE, "b" * 64)
        self.assertIsNone(ps.fill_notes(filled, EXE, "b" * 64)[0])

    def test_notes_with_another_hash_stop(self):
        filled, _ = ps.fill_notes(BODY, EXE, "b" * 64)
        with self.assertRaises(ps.Stop):
            ps.fill_notes(filled, EXE, "c" * 64)

    def test_notes_without_a_setup_row_are_left(self):
        body, message = ps.fill_notes("no table here\n", EXE, "b" * 64)
        self.assertIsNone(body)
        self.assertIn("no file-table row", message)

    def test_the_install_line_naming_the_exe_is_not_the_row(self):
        i, line = ps.setup_row(BODY, EXE)
        self.assertTrue(line.startswith("| `LivingServer-Setup"))


class Decide(unittest.TestCase):
    def test_tag_must_be_a_living_server_beta(self):
        for bad in ("1.1.1-beta", "v1.1.1", "v0.37b"):
            with self.assertRaises(ps.Stop):
                ps.names(bad)
        self.assertEqual(ps.names("v1.1.2-beta"), ("1.1.2-beta", "LivingServer-Setup-1.1.2-beta.exe",
                                                   "OfflineDAoC-LivingServer-1.1.2-beta.zip"))

    def test_plan_without_the_exe_has_work(self):
        self.assertFalse(ps.decide(release(), TAG)["done"])

    def test_plan_with_exe_and_hash_in_notes_is_done(self):
        filled, _ = ps.fill_notes(BODY, EXE, "b" * 64)
        self.assertTrue(ps.decide(release(filled, exe_sha="b" * 64), TAG)["done"])

    def test_plan_with_exe_but_placeholder_still_has_work(self):
        self.assertFalse(ps.decide(release(exe_sha="b" * 64), TAG)["done"])

    def test_missing_package_or_draft_stops(self):
        rel = release()
        rel["assets"] = []
        with self.assertRaises(ps.Stop):
            ps.decide(rel, TAG)
        with self.assertRaises(ps.Stop):
            ps.decide(release(draft=True), TAG)

    def test_embedded_package_must_be_the_release_package(self):
        with self.assertRaises(ps.Stop):
            ps.decide(release(zip_sha="a" * 64), TAG, "b" * 64, "d" * 64)

    def test_different_attached_exe_is_never_replaced(self):
        with self.assertRaises(ps.Stop):
            ps.decide(release(exe_sha="c" * 64), TAG, "b" * 64, "a" * 64)

    def test_same_attached_exe_is_not_uploaded_again(self):
        result = ps.decide(release(exe_sha="b" * 64), TAG, "b" * 64, "a" * 64)
        self.assertFalse(result["upload"])
        self.assertIn("`" + "b" * 64 + "`", result["body"])


class Run(unittest.TestCase):
    def setUp(self):
        self.dir = tempfile.TemporaryDirectory()
        d = Path(self.dir.name)
        self.exe, self.zip = d / EXE, d / "LivingServer.zip"
        self.exe.write_bytes(b"setup exe")
        self.zip.write_bytes(b"package zip")

    def tearDown(self):
        self.dir.cleanup()

    def publish(self, gh, *extra):
        return ps.run(["publish", "--tag", TAG, "--exe", str(self.exe), "--package", str(self.zip), *extra], gh)

    def test_publish_uploads_fills_notes_and_a_second_run_does_nothing(self):
        gh = FakeGitHub(release(zip_sha=sha(b"package zip")))
        self.assertEqual(self.publish(gh), 0)
        self.assertEqual(gh.uploads, [EXE])
        self.assertEqual(len(gh.edits), 1)
        self.assertIn(f"| `{EXE}` | `{sha(b'setup exe')}` |\r\n", gh.edits[0])
        self.assertEqual(self.publish(gh), 0)
        self.assertEqual((len(gh.uploads), len(gh.edits)), (1, 1), "a re-run changes nothing")
        self.assertEqual(ps.run(["plan", "--tag", TAG], gh), 0)

    def test_dry_run_changes_nothing(self):
        gh = FakeGitHub(release(zip_sha=sha(b"package zip")))
        self.assertEqual(self.publish(gh, "--dry-run"), 0)
        self.assertEqual((gh.uploads, gh.edits), ([], []))

    def test_dry_run_reports_where_a_real_run_stops_without_failing(self):
        gh = FakeGitHub(release(zip_sha=sha(b"package zip"), exe_sha="c" * 64))
        self.assertEqual(self.publish(gh, "--dry-run"), 0)
        self.assertEqual(self.publish(gh), 1)
        self.assertEqual((gh.uploads, gh.edits), ([], []))

    def test_wrong_exe_name_stops(self):
        other = self.exe.with_name("LivingServer-Setup-1.1-beta.exe")
        other.write_bytes(b"x")
        gh = FakeGitHub(release(zip_sha=sha(b"package zip")))
        self.assertEqual(ps.run(["publish", "--tag", TAG, "--exe", str(other), "--package", str(self.zip)], gh), 1)
        self.assertEqual(gh.uploads, [])

    def test_release_json_file(self):
        path = Path(self.dir.name) / "release.json"
        path.write_text(json.dumps(release(zip_sha=sha(b"package zip"))), encoding="utf-8")
        gh = FakeGitHub(None)
        self.assertEqual(self.publish(gh, "--dry-run", "--release-json", str(path)), 0)


if __name__ == "__main__":
    unittest.main()
