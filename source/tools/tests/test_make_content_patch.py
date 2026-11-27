"""Tests for tools/fork-build/make_content_patch.py's navmesh rule (issue #687): a release build's navmesh patches are
made from, and checked against, the published navmesh set.

    python3 -m unittest discover -s source/tools/tests -p test_make_content_patch.py -v

Runs in build-and-test's encounter-catalog job (source/tools/tests), so the repository's own content-patches.json is
checked against tools/navmesh/manifest.json on every push, not only in the (not required) navmesh job.
"""
import contextlib
import hashlib
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
REPO = TOOLS.parents[1]
sys.path.insert(0, str(REPO / "tools" / "fork-build"))
sys.path.insert(0, str(REPO / "tools" / "navmesh"))

import make_content_patch as mcp  # noqa: E402
import navmesh_set as ns  # noqa: E402

RELEASED = b"MSET" + bytes(range(256)) * 16
FIXED = b"MSET" + bytes(range(256)) * 7 + b"the fixed tile" + bytes(range(256)) * 9
OTHER = {"lib/Detour.dll": b"MZ", "navmesh/roads.json": b"{}", "navmesh/seams.json": b"{}", "navmesh/pockets.json": b"{}"}
REL = "runtime/server/navmesh/zone180.nav"


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


class FromSetTests(unittest.TestCase):
    """A fake set, unpacked and marked as navmesh_set.py fetch leaves it."""

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        files = dict(OTHER) | {"navmesh/zone180.nav": FIXED}
        self.manifest = self.root / "manifest.json"
        self.manifest.write_text(json.dumps({
            "schema": 1, "version": "v1", "tag": "navmesh-v1", "repository": ns.REPOSITORY, "source": {},
            "total_bytes": sum(len(b) for b in files.values()),
            "parts": [{"name": "navmesh-v1.part01.zip", "bytes": 10, "sha256": "0" * 64}],
            "files": [{"path": p, "bytes": len(b), "sha256": sha(b), "part": "navmesh-v1.part01.zip"}
                      for p, b in sorted(files.items())]}), encoding="utf-8")
        self.set = self.root / "set"
        for rel, data in files.items():
            (self.set / rel).parent.mkdir(parents=True, exist_ok=True)
            (self.set / rel).write_bytes(data)
        shutil.copyfile(self.manifest, self.set / ns.MARKER)
        self.released = self.root / "released.nav"
        self.released.write_bytes(RELEASED)
        self.out = self.root / "content-patches.json"
        self.out.write_text("[]\n", encoding="utf-8")

    def run_tool(self, *args) -> tuple[int, str]:
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            code = mcp.main(["make_content_patch.py", *map(str, args)])
        return code, buf.getvalue()

    def make(self, fixed="set", *extra):
        return self.run_tool("--navmesh-set", self.set, "--manifest", self.manifest, *extra, self.out, REL,
                             self.released, fixed)

    def test_a_navmesh_patch_is_made_from_the_set(self):
        code, log = self.make()
        self.assertEqual(code, 0, log)
        entry = json.loads(self.out.read_text(encoding="utf-8"))[0]
        self.assertEqual(entry["Path"], REL)
        self.assertEqual(entry["Released"], sha(RELEASED))
        self.assertEqual(entry["Fixed"], sha(FIXED), "the patch leaves the set's mesh")
        self.assertEqual(entry["Size"], len(FIXED))
        self.assertEqual(mcp.apply(RELEASED, entry["Ops"]), FIXED)
        # And navmesh_set.py check agrees with what was written.
        m = ns.load(self.manifest)
        self.assertEqual(ns.check_content_patches(m, self.out.read_text(encoding="utf-8")), [])

    def test_a_navmesh_from_a_pc_folder_is_refused(self):
        pc = self.root / "pc-zone180.nav"
        pc.write_bytes(FIXED)
        code, log = self.make(pc)
        self.assertEqual(code, 1)
        self.assertIn("made from the published navmesh set", log)
        code, log = self.run_tool(self.out, REL, self.released, pc)   # no --navmesh-set at all
        self.assertEqual(code, 1)
        self.assertIn("--navmesh-set", log)
        self.assertEqual(self.out.read_text(encoding="utf-8"), "[]\n", "nothing is written on a refusal")

    def test_a_set_fetched_against_another_manifest_is_refused(self):
        (self.set / ns.MARKER).write_text("{}", encoding="utf-8")
        code, log = self.make()
        self.assertEqual(code, 1)
        self.assertIn("another manifest", log)

    def test_an_unfetched_folder_is_refused(self):
        (self.set / ns.MARKER).unlink()
        code, log = self.make()
        self.assertEqual(code, 1)
        self.assertIn("not a set navmesh_set.py fetch unpacked", log)

    def test_a_mesh_changed_after_the_fetch_is_refused(self):
        (self.set / "navmesh/zone180.nav").write_bytes(FIXED[:-1] + b"X")
        code, log = self.make()
        self.assertEqual(code, 1)
        self.assertIn("SHA-256", log)

    def test_a_mesh_the_set_lacks_is_refused(self):
        code, log = self.run_tool("--navmesh-set", self.set, "--manifest", self.manifest, self.out,
                                  "runtime/server/navmesh/zone999.nav", self.released, "set")
        self.assertEqual(code, 1)
        self.assertIn("not in navmesh-v1", log)

    def test_only_a_navmesh_comes_from_the_set(self):
        code, log = self.run_tool("--navmesh-set", self.set, "--manifest", self.manifest, self.out,
                                  "new-client/godot/game/x.gd", self.released, "set")
        self.assertEqual(code, 1)
        self.assertIn("only a navmesh", log)

    def test_other_files_still_come_from_the_file_given(self):
        fixed = self.root / "x.gd"
        fixed.write_bytes(b"fixed script")
        released = self.root / "x-released.gd"
        released.write_bytes(b"released script")
        code, log = self.run_tool(self.out, "new-client/godot/game/x.gd", released, fixed)
        self.assertEqual(code, 0, log)
        self.assertEqual(json.loads(self.out.read_text(encoding="utf-8"))[0]["Fixed"], sha(b"fixed script"))

    def test_a_stale_size_fails_the_check(self):
        self.assertEqual(self.make()[0], 0)
        entries = json.loads(self.out.read_text(encoding="utf-8"))
        entries[0]["Size"] += 1
        problems = ns.check_content_patches(ns.load(self.manifest), json.dumps(entries))
        self.assertEqual(len(problems), 1)
        self.assertIn("fixed size", problems[0])


class RepositoryTests(unittest.TestCase):
    """What ships agrees with the published set (tools/navmesh/manifest.json, navmesh-v1 since #687)."""

    def test_every_shipped_navmesh_patch_is_the_published_sets_mesh(self):
        manifest = ns.load(ns.MANIFEST)
        self.assertIsNotNone(manifest, "tools/navmesh/manifest.json names a published set")
        patches = json.loads(ns.CONTENT_PATCHES.read_text(encoding="utf-8"))
        navmesh = [p for p in patches if p["Path"].startswith(mcp.NAVMESH_PREFIX)]
        self.assertTrue(navmesh, "the installer patches at least one navmesh (zone180, zone277)")
        self.assertEqual(ns.check_content_patches(manifest, ns.CONTENT_PATCHES.read_text(encoding="utf-8")), [])
        files = {f.path: f for f in manifest.files}
        for p in navmesh:
            entry = files[p["Path"][len("runtime/server/"):]]
            self.assertEqual((p["Fixed"], p["Size"]), (entry.sha256, entry.size), p["Path"])


if __name__ == "__main__":
    unittest.main()
