"""Tests for tools/navmesh/navmesh_set.py (issue #684): the manifest, the fetch and verify steps (a planted mismatch must
fail), the test list, the installer's navmesh patches and the results summary.

    python3 -m unittest discover -s tools/navmesh/tests -v
"""
from __future__ import annotations

import contextlib
import copy
import hashlib
import io
import json
import os
import sys
import tempfile
import unittest
import unittest.mock
import urllib.error
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
sys.path.insert(0, str(REPO / "tools" / "navmesh"))

import navmesh_set as ns  # noqa: E402

FILES = {
    "lib/Detour.dll": b"MZ not really a dll",
    "navmesh/pockets.json": b"{}",
    "navmesh/roads.json": b"{\"roads\": []}",
    "navmesh/seams.json": b"{\"seams\": []}",
    "navmesh/zone021.nav": b"MSET" + b"\x01\x00\x00\x00" + bytes(range(256)) * 8,
    "navmesh/zone181.nav": b"MSET" + b"\x01\x00\x00\x00" + bytes(range(255, -1, -1)) * 8,
}


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def make_part(folder: Path, name: str, files: dict[str, bytes]) -> Path:
    path = folder / name
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        for rel in sorted(files):
            z.writestr(zipfile.ZipInfo(rel, (2000, 1, 1, 0, 0, 0)), files[rel])
    return path


def make_manifest(parts: dict[str, Path], layout: dict[str, str], files=FILES) -> dict:
    return {
        "schema": 1, "version": "v1", "tag": "navmesh-v1", "repository": ns.REPOSITORY,
        "captured": "2026-11-26", "source": {"build": "1.2.3", "commit": "820944dc"},
        "total_bytes": sum(len(b) for b in files.values()),
        "parts": [{"name": n, "bytes": p.stat().st_size, "sha256": ns.sha256_of(p)} for n, p in parts.items()],
        "files": [{"path": rel, "bytes": len(files[rel]), "sha256": sha(files[rel]), "part": layout[rel]} for rel in sorted(files)],
    }


class SetFixture(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.parts = self.root / "upload"
        self.parts.mkdir()
        first = {k: v for k, v in FILES.items() if not k.endswith("181.nav")}
        second = {"navmesh/zone181.nav": FILES["navmesh/zone181.nav"]}
        self.p1 = make_part(self.parts, "navmesh-v1.part01.zip", first)
        self.p2 = make_part(self.parts, "navmesh-v1.part02.zip", second)
        layout = {k: "navmesh-v1.part01.zip" for k in first} | {k: "navmesh-v1.part02.zip" for k in second}
        self.data = make_manifest({"navmesh-v1.part01.zip": self.p1, "navmesh-v1.part02.zip": self.p2}, layout)
        self.manifest_path = self.write_manifest(self.data)
        self.dest = self.root / "set"

    def write_manifest(self, data: dict, name: str = "manifest.json") -> Path:
        path = self.root / name
        path.write_text(json.dumps(data, indent=2), encoding="utf-8")
        return path

    def fetch(self, data=None) -> tuple[int, str]:
        path = self.manifest_path if data is None else self.write_manifest(data, "other.json")
        lines: list[str] = []
        code = ns.fetch(ns.parse(json.loads(path.read_text())), path, self.dest, self.parts, out=lines.append)
        return code, "\n".join(lines)


class ManifestTests(SetFixture):
    def test_a_good_manifest_parses(self):
        m = ns.parse(self.data)
        self.assertEqual(m.tag, "navmesh-v1")
        self.assertEqual(len(m.files), len(FILES))
        self.assertEqual(m.url(m.parts[0]),
                         "https://github.com/chris4aker/OfflineDAoC-LivingServer/releases/download/navmesh-v1/navmesh-v1.part01.zip")

    def test_the_placeholder_is_no_set(self):
        self.assertIsNone(ns.load(REPO / "tools/navmesh/manifest.json") if ns.is_placeholder(
            ns.read_json(REPO / "tools/navmesh/manifest.json")) else None)
        placeholder = self.write_manifest({"schema": 1, "placeholder": True}, "p.json")
        self.assertIsNone(ns.load(placeholder))
        with self.assertRaises(ns.SetError):
            ns.parse({"schema": 1, "placeholder": True})

    def test_damaged_manifests_are_refused(self):
        cases = {
            "tag": lambda d: d.update(tag="v1.2.3"),
            "version": lambda d: d.update(version="1"),
            "repository": lambda d: d.update(repository="someone/else"),
            "no parts": lambda d: d.update(parts=[]),
            "part name": lambda d: d["parts"][0].update(name="navmesh-v1.zip"),
            "part too big": lambda d: d["parts"][0].update(bytes=ns.PART_LIMIT),
            "bad sha": lambda d: d["files"][0].update(sha256="ABC"),
            "unsafe path": lambda d: d["files"][0].update(path="../../evil.dll"),
            "absolute path": lambda d: d["files"][0].update(path="C:/Windows/evil.dll"),
            "unknown part": lambda d: d["files"][0].update(part="navmesh-v1.part09.zip"),
            "twice": lambda d: d["files"].append(dict(d["files"][0])),
            "no detour": lambda d: d.update(files=[f for f in d["files"] if f["path"] != "lib/Detour.dll"]),
            "no zone": lambda d: d.update(files=[f for f in d["files"] if not f["path"].endswith(".nav")]),
            "total": lambda d: d.update(total_bytes=1),
        }
        for name, change in cases.items():
            with self.subTest(name):
                data = copy.deepcopy(self.data)
                change(data)
                with self.assertRaises(ns.SetError):
                    ns.parse(data)

    def test_status_says_published_or_not(self):
        out = self.root / "gh_output"
        for path, expected in ((self.manifest_path, "true"), (self.write_manifest({"schema": 1, "placeholder": True}, "p.json"), "false")):
            out.write_text("")
            with unittest.mock.patch.dict(os.environ, {"GITHUB_OUTPUT": str(out)}), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(ns.main(["status", str(path)]), 0)
            self.assertIn(f"published={expected}", out.read_text())
            self.assertIn("key=" + ns.sha256_of(path), out.read_text())

    def test_status_fails_on_a_damaged_manifest(self):
        bad = self.write_manifest({"schema": 1, "version": "v1"}, "bad.json")
        with contextlib.redirect_stdout(io.StringIO()) as printed:
            self.assertEqual(ns.main(["status", str(bad)]), 1)
        self.assertIn("::error", printed.getvalue())


WORLD_BYTES = b"SQLite format 3\x00" + bytes(64)


class WorldTests(SetFixture):
    """Issue #687: the clean world database in the set, named by the manifest's "world" block."""

    def with_world(self) -> dict:
        files = dict(FILES, **{ns.WORLD: WORLD_BYTES})
        part = make_part(self.parts, "navmesh-v1.part01.zip", files)
        data = make_manifest({"navmesh-v1.part01.zip": part}, {k: "navmesh-v1.part01.zip" for k in files}, files)
        data["world"] = {"name": "opendaoc.sqlite3.db", "path": ns.WORLD, "bytes": len(WORLD_BYTES), "sha256": sha(WORLD_BYTES),
                         "clean": True, "checked_by": "tools/navmesh/clean_world.py verify (build_release_037.verify_clean)"}
        return data

    def test_a_set_with_a_clean_world_parses_fetches_and_verifies(self):
        data = self.with_world()
        m = ns.parse(data)
        self.assertEqual(m.world.path, ns.WORLD)
        self.assertEqual(m.world.size, len(WORLD_BYTES))
        code, log = self.fetch(data)
        self.assertEqual(code, 0, log)
        self.assertEqual((self.dest / ns.WORLD).read_bytes(), WORLD_BYTES)
        (self.dest / "world" / "extra.db").write_bytes(b"x")
        lines: list[str] = []
        self.assertEqual(ns.verify(m, self.dest, lines.append), 1, "an extra file beside the world is found")
        self.assertIn("world/extra.db", "\n".join(lines))

    def test_a_navmesh_only_set_has_no_world(self):
        self.assertIsNone(ns.parse(self.data).world)

    def test_the_world_block_must_name_the_listed_checked_database(self):
        cases = {
            "no block": lambda d: d.pop("world"),
            "no file": lambda d: d.update(files=[f for f in d["files"] if f["path"] != ns.WORLD],
                                          total_bytes=sum(len(b) for b in FILES.values())),
            "other size": lambda d: d["world"].update(bytes=1),
            "other hash": lambda d: d["world"].update(sha256="0" * 64),
            "not clean": lambda d: d["world"].update(clean=False),
            "unchecked": lambda d: d["world"].pop("checked_by"),
            "other name": lambda d: d["world"].update(name="played.db"),
            "other path": lambda d: d["files"].append({"path": "world/played.db", "bytes": 1, "sha256": "0" * 64,
                                                       "part": "navmesh-v1.part01.zip"}),
        }
        for name, change in cases.items():
            with self.subTest(name):
                data = self.with_world()
                change(data)
                with self.assertRaises(ns.SetError):
                    ns.parse(data)

    def test_status_says_whether_the_set_has_a_world(self):
        out = self.root / "gh_output"
        for data, expected in ((self.with_world(), "true"), (self.data, "false")):
            out.write_text("")
            path = self.write_manifest(data, "w.json")
            with unittest.mock.patch.dict(os.environ, {"GITHUB_OUTPUT": str(out)}), contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(ns.main(["status", str(path)]), 0)
            self.assertIn(f"world={expected}", out.read_text())


class FakeGitHub(ns.GitHub):
    """Records the calls; GitHub's digest of an upload is the uploaded bytes' SHA-256 unless told otherwise."""

    def __init__(self, tags=(), releases=(), bad_digest=None, latest=""):
        super().__init__("token")
        self.tags, self.releases, self.bad_digest, self.latest = set(tags), list(releases), bad_digest, latest
        self.assets, self.calls = [], []

    def tag_exists(self, tag):
        return tag in self.tags

    def release_for(self, tag):
        return next((r for r in self.releases if r["tag_name"] == tag), None)

    def latest_tag(self):
        return self.latest

    def create_draft(self, tag, commit, title, body):
        self.calls.append(("draft", tag, commit))
        return {"url": "u", "upload_url": "up{?name}", "html_url": "h", "draft": True}

    def upload(self, release, path, name):
        digest = "0" * 64 if name == self.bad_digest else ns.sha256_of(path)
        self.assets.append({"name": name, "digest": f"sha256:{digest}"})

    def get(self, release):
        return dict(release, assets=self.assets)

    def publish(self, release):
        self.calls.append(("publish",))
        return {"draft": False, "prerelease": True, "html_url": "h"}


class PublishTests(SetFixture):
    with_world = WorldTests.with_world
    COMMIT = "a" * 40

    def setUp(self):
        super().setUp()
        self.data = self.with_world()
        self.manifest_path = self.write_manifest(self.data, "navmesh-manifest.json")
        self.manifest = ns.parse(self.data)

    def run_publish(self, gh, dry_run=False):
        lines: list[str] = []
        code = ns.publish(gh, self.manifest, self.manifest_path, self.parts, self.COMMIT, "notes", dry_run, lines.append)
        return code, "\n".join(lines)

    def test_a_dry_run_publishes_nothing(self):
        gh = FakeGitHub()
        code, log = self.run_publish(gh, dry_run=True)
        self.assertEqual(code, 0)
        self.assertEqual(gh.calls, [])
        self.assertIn("never Latest (make_latest false)", log)

    def test_it_publishes_a_pre_release_with_every_file(self):
        gh = FakeGitHub()
        code, log = self.run_publish(gh)
        self.assertEqual(code, 0, log)
        self.assertEqual(gh.calls, [("draft", "navmesh-v1", self.COMMIT), ("publish",)])
        self.assertEqual({a["name"] for a in gh.assets}, {"navmesh-v1.part01.zip", "navmesh-manifest.json"})

    def test_an_existing_tag_or_release_is_never_replaced(self):
        for gh in (FakeGitHub(tags={"navmesh-v1"}), FakeGitHub(releases=[{"tag_name": "navmesh-v1", "draft": True, "html_url": "d"}])):
            with self.subTest(gh=gh.tags or "draft"), self.assertRaisesRegex(ns.SetError, "never replaced|exists already"):
                self.run_publish(gh, dry_run=True)
            self.assertEqual(gh.calls, [])

    def test_a_wrong_digest_leaves_the_draft_unpublished(self):
        gh = FakeGitHub(bad_digest="navmesh-v1.part01.zip")
        with self.assertRaisesRegex(ns.SetError, "left unpublished"):
            self.run_publish(gh)
        self.assertNotIn(("publish",), gh.calls)

    def test_a_changed_part_or_a_set_without_a_world_is_refused(self):
        (self.parts / "navmesh-v1.part01.zip").write_bytes(b"changed")
        with self.assertRaises(ns.SetError):
            self.run_publish(FakeGitHub(), dry_run=True)
        self.manifest = ns.parse(self.navmesh_only())
        with self.assertRaisesRegex(ns.SetError, "no clean world"):
            self.run_publish(FakeGitHub(), dry_run=True)

    def navmesh_only(self):
        data = copy.deepcopy(self.data)
        data.pop("world")
        data["files"] = [f for f in data["files"] if f["path"] != ns.WORLD]
        data["total_bytes"] = sum(f["bytes"] for f in data["files"])
        return data

    def test_becoming_latest_is_an_error(self):
        with self.assertRaisesRegex(ns.SetError, "Latest"):
            self.run_publish(FakeGitHub(latest="navmesh-v1"))

    def test_the_commit_must_be_a_full_sha(self):
        self.COMMIT = "main"
        with self.assertRaisesRegex(ns.SetError, "full commit"):
            self.run_publish(FakeGitHub(), dry_run=True)


class FetchVerifyTests(SetFixture):
    def test_a_clean_set_is_fetched_and_verified(self):
        code, log = self.fetch()
        self.assertEqual(code, 0, log)
        for rel, data in FILES.items():
            self.assertEqual((self.dest / rel).read_bytes(), data)
        self.assertTrue((self.dest / ns.MARKER).is_file())
        self.assertFalse((self.root / "set-parts").exists(), "the downloaded parts are removed after unpacking")
        lines: list[str] = []
        self.assertEqual(ns.verify(ns.parse(self.data), self.dest, lines.append), 0, lines)

    def test_a_planted_part_mismatch_fails(self):
        data = copy.deepcopy(self.data)
        data["parts"][1]["sha256"] = "0" * 64
        code, log = self.fetch(data)
        self.assertEqual(code, 1)
        self.assertIn("navmesh-v1.part02.zip has SHA-256", log)
        self.assertFalse(self.dest.exists(), "nothing half-checked is left to cache")

    def test_a_planted_file_mismatch_inside_a_good_part_fails(self):
        # The part's own hash is right (re-zipped and re-hashed), but one file inside is not the manifest's.
        planted = dict(FILES)
        planted["navmesh/zone021.nav"] = FILES["navmesh/zone021.nav"][:-1] + b"X"
        p1 = make_part(self.parts, "navmesh-v1.part01.zip", {k: v for k, v in planted.items() if not k.endswith("181.nav")})
        data = copy.deepcopy(self.data)
        data["parts"][0].update(bytes=p1.stat().st_size, sha256=ns.sha256_of(p1))
        code, log = self.fetch(data)
        self.assertEqual(code, 1)
        self.assertIn("navmesh/zone021.nav in navmesh-v1.part01.zip", log)

    def test_a_part_with_an_unlisted_or_escaping_file_fails(self):
        for name in ("navmesh/extra.nav", "../escape.nav"):
            with self.subTest(name):
                files = {k: v for k, v in FILES.items() if not k.endswith("181.nav")} | {name: b"x"}
                p1 = make_part(self.parts, "navmesh-v1.part01.zip", files)
                data = copy.deepcopy(self.data)
                data["parts"][0].update(bytes=p1.stat().st_size, sha256=ns.sha256_of(p1))
                code, log = self.fetch(data)
                self.assertEqual(code, 1)
                self.assertIn("which the manifest doesn't list", log)
                self.assertFalse((self.root / "escape.nav").exists())

    def test_a_missing_part_fails(self):
        self.p2.unlink()
        code, log = self.fetch()
        self.assertEqual(code, 1)

    def test_verify_fails_on_a_changed_missing_or_extra_file_after_a_cache_restore(self):
        self.assertEqual(self.fetch()[0], 0)
        m = ns.parse(self.data)
        for plant, expect in (
                (lambda: (self.dest / "navmesh/zone181.nav").write_bytes(b"MSET damaged"), "navmesh/zone181.nav is"),
                (lambda: (self.dest / "navmesh/roads.json").unlink(), "navmesh/roads.json is missing"),
                (lambda: (self.dest / "navmesh/zone999.nav").write_bytes(b"x"), "zone999.nav is in")):
            with self.subTest(expect):
                self.assertEqual(self.fetch()[0], 0)
                plant()
                lines: list[str] = []
                self.assertEqual(ns.verify(m, self.dest, lines.append), 1)
                self.assertIn(expect, "\n".join(lines))
        # Same bytes, one changed: the size check alone would miss it.
        self.assertEqual(self.fetch()[0], 0)
        nav = self.dest / "navmesh/zone021.nav"
        data = nav.read_bytes()
        nav.write_bytes(data[:-1] + bytes([data[-1] ^ 1]))
        lines = []
        self.assertEqual(ns.verify(m, self.dest, lines.append), 1)

    def test_the_cli_verify_fails_on_a_planted_mismatch(self):
        self.assertEqual(self.fetch()[0], 0)
        (self.dest / "lib/Detour.dll").write_bytes(b"MZ planted")
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(ns.main(["verify", str(self.manifest_path), "--root", str(self.dest)]), 1)
            self.assertEqual(ns.main(["verify", str(REPO / "tools/navmesh/manifest.json"), "--root", str(self.dest)]), 1,
                             "the placeholder has no set to verify")

    def test_download_does_not_retry_a_404_and_cleans_up(self):
        calls = []

        def opener(request, timeout):
            calls.append(request.full_url)
            raise urllib.error.HTTPError(request.full_url, 404, "Not Found", None, None)
        target = self.root / "x.zip"
        with self.assertRaises(ns.SetError):
            ns.download("https://github.com/x/y/releases/download/navmesh-v1/x.zip", target, opener=opener)
        self.assertEqual(len(calls), 1)
        self.assertFalse(target.exists())
        self.assertFalse(target.with_name("x.zip.partial").exists())

    def test_download_writes_through_a_partial_file(self):
        def opener(request, timeout):
            self.assertEqual(request.get_header("User-agent"), "navmesh-set-ci")
            return io.BytesIO(b"payload")
        target = self.root / "y.zip"
        ns.download("https://example.invalid/y.zip", target, opener=opener)
        self.assertEqual(target.read_bytes(), b"payload")


class ListTests(unittest.TestCase):
    def test_the_repository_list_is_sound(self):
        entries = ns.read_list(ns.TEST_LIST)
        self.assertGreaterEqual(len(entries), 3)
        self.assertEqual(ns.check_list(entries, ns.EXCLUDED_LIST.read_text(encoding="utf-8")), [])

    def test_the_filter_names_each_entry(self):
        f = ns.test_filter([("UT_A", "r (#1)"), ("UT_B.M", "r (#1)")])
        self.assertEqual(f, "FullyQualifiedName~.UT_A|FullyQualifiedName~.UT_B.M")

    def test_a_bad_entry_is_found(self):
        excluded = ns.EXCLUDED_LIST.read_text(encoding="utf-8")
        problems = ns.check_list([("UT_NoSuchClass", "r (#684)"), ("UT_DragonLairProbe.NoSuchMethod", "r (#684)"),
                                  ("UT_PassportLock", "r (#684)")], excluded)
        text = "\n".join(problems)
        self.assertIn("no UT_NoSuchClass.cs", text)
        self.assertIn("has no test method NoSuchMethod", text)
        self.assertIn("UT_PassportLock: UT_PassportLock must stay on", text)

    def test_a_list_without_reasons_is_refused(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "list.txt"
            path.write_text("UT_DragonLairProbe\n", encoding="utf-8")
            with self.assertRaises(ns.SetError):
                ns.read_list(path)
            path.write_text("# only notes\n", encoding="utf-8")
            with self.assertRaises(ns.SetError):
                ns.read_list(path)


class ContentPatchTests(SetFixture):
    def test_the_installers_navmesh_patch_must_match_the_set(self):
        m = ns.parse(self.data)
        fixed = sha(FILES["navmesh/zone181.nav"])
        good = json.dumps([{"Path": "runtime/server/navmesh/zone181.nav", "Released": "1" * 64, "Fixed": fixed},
                           {"Path": "runtime/client-opendaoc/app/x.dat", "Fixed": "2" * 64}])
        self.assertEqual(ns.check_content_patches(m, good), [])
        stale = json.dumps([{"Path": "runtime/server/navmesh/zone181.nav", "Fixed": "3" * 64}])
        self.assertIn("is not the installer's fixed", ns.check_content_patches(m, stale)[0])
        absent = json.dumps([{"Path": "runtime/server/navmesh/zone277.nav", "Fixed": "3" * 64}])
        self.assertIn("not in the set", ns.check_content_patches(m, absent)[0])

    def test_the_repository_check_passes(self):
        lines: list[str] = []
        self.assertEqual(ns.check(out=lines.append), 0, lines)

    def test_the_check_fails_on_a_damaged_manifest(self):
        bad = self.write_manifest({"schema": 1, "version": "v1"}, "bad.json")
        lines: list[str] = []
        self.assertEqual(ns.check(manifest_path=bad, out=lines.append), 1)


TRX = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>{results}</Results>
  <TestDefinitions>{defs}</TestDefinitions>
</TestRun>"""


def trx(rows: list[tuple[str, str, str]]) -> str:
    results, defs = [], []
    for i, (cls, method, outcome) in enumerate(rows):
        results.append(f'<UnitTestResult testId="{i}" testName="{method}" outcome="{outcome}" />')
        defs.append(f'<UnitTest id="{i}" name="{method}"><TestMethod className="DOL.UnitTests.{cls}" name="{method}" /></UnitTest>')
    return TRX.format(results="".join(results), defs="".join(defs))


class ResultsTests(unittest.TestCase):
    ENTRIES = [("UT_A", "r (#684)"), ("UT_B.Inspect", "r (#684)")]

    def run_results(self, rows) -> tuple[int, str, str]:
        with tempfile.TemporaryDirectory() as tmp:
            path, summary = Path(tmp) / "n.trx", Path(tmp) / "summary.md"
            path.write_text(trx(rows), encoding="utf-8")
            lines: list[str] = []
            code = ns.results(path, self.ENTRIES, "true", lines.append, summary)
            return code, "\n".join(lines), summary.read_text(encoding="utf-8")

    def test_every_result_is_listed_and_all_passing_passes(self):
        code, log, summary = self.run_results([("UT_A", "One", "Passed"), ("UT_B", "Inspect", "Passed")])
        self.assertEqual(code, 0, log)
        self.assertIn("DOL.UnitTests.UT_A.One", summary)
        self.assertIn("Cache hit: **true**", summary)

    def test_a_failed_inconclusive_or_missing_test_fails(self):
        for rows, expect in (([("UT_A", "One", "Failed"), ("UT_B", "Inspect", "Passed")], "UT_A.One: Failed"),
                             ([("UT_A", "One", "Inconclusive"), ("UT_B", "Inspect", "Passed")], "Inconclusive"),
                             ([("UT_A", "One", "Passed"), ("UT_B", "Other", "Passed")], "UT_B.Inspect: no result")):
            with self.subTest(expect):
                code, log, _ = self.run_results(rows)
                self.assertEqual(code, 1)
                self.assertIn(expect, log)

    def test_no_results_file_fails(self):
        self.assertEqual(ns.results(Path("/nonexistent/n.trx"), self.ENTRIES, out=lambda _: None), 1)


if __name__ == "__main__":
    unittest.main()
