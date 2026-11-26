"""Tests for tools/check.py, the local mirror of CI's Python checks (issue #615).

    python3 -m unittest discover -s source/tools/tests -v
"""
import contextlib
import io
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPO / "tools"))

import check  # noqa: E402


def workflow_python_commands():
    """Every python command line of the mirrored workflows (build-and-test.yml, navmesh.yml), without the interpreter
    and unittest's -v."""
    out = []
    lines = [raw for wf in check.WORKFLOWS for raw in (REPO / wf).read_text(encoding="utf-8").split("\n")]
    for raw in lines:
        line = raw.strip()
        if line.startswith("run:"):
            line = line[4:].strip()
        if line.startswith("#"):
            continue
        for interp in ("python3 ", "python "):
            if line.startswith(interp):
                parts = [p for p in line[len(interp):].split() if p != "-v"]
                out.append(" ".join(parts))
    return out


class MirrorTests(unittest.TestCase):
    def test_every_workflow_python_check_runs_here_or_says_why_not(self):
        commands = workflow_python_commands()
        self.assertGreater(len(commands), 15)
        local = {c for _, c in check.CHECKS}
        missing = [c for c in commands if c not in local and c.split()[0] not in check.NOT_LOCAL
                   and " ".join(c.split()[:2]) not in check.NOT_LOCAL]
        self.assertEqual(missing, [], "add these to tools/check.py CHECKS (or NOT_LOCAL with the reason)")

    def test_the_issue_615_checks_are_in(self):
        local = {c for _, c in check.CHECKS}
        for c in ["source/tools/epic_encounter_catalog.py --check", "source/tools/make_command_reference.py --check",
                  "tools/bot-voices/say_gate_ratchet.py", "tools/lore/lint_lore.py", "-m unittest discover -s source/tools/tests",
                  "tools/release/check_versions.py", "source/tools/assemble_changelog.py --check"]:
            self.assertIn(c, local)

    def test_the_navmesh_workflow_is_mirrored(self):
        commands = workflow_python_commands()
        self.assertIn("tools/navmesh/navmesh_set.py check", commands, "navmesh.yml is read too (issue #684)")
        self.assertIn("-m unittest discover -s tools/navmesh/tests", {c for _, c in check.CHECKS})

    def test_every_check_names_an_existing_script_or_folder(self):
        for _, command in check.CHECKS:
            parts = command.split()
            target = parts[parts.index("-s") + 1] if parts[0] == "-m" else parts[0]
            self.assertTrue((REPO / target).exists(), target)

    def test_unittest_folders_split_into_one_job_per_file(self):
        jobs = [j for j in check.jobs_for(REPO) if j.name.startswith("tools/lore/tests/")]
        self.assertEqual(len(jobs), len(list((REPO / "tools/lore/tests").glob("test*.py"))))
        self.assertTrue(all("-p" in j.argv for j in jobs))


class StaleCatalogTests(unittest.TestCase):
    """tools/check.py fails on a stale epic encounter catalog (the issue's acceptance), in a temp copy of the inputs."""

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        (root / "source/tools").mkdir(parents=True)
        shutil.copy2(REPO / "source/tools/epic_encounter_catalog.py", root / "source/tools")
        shutil.copytree(REPO / "docs/encounters", root / "docs/encounters")
        try:  # read only by --check; a link avoids copying the 90 MB server tree
            os.symlink(REPO / "source/server", root / "source/server", target_is_directory=True)
        except (OSError, NotImplementedError):
            shutil.copytree(REPO / "source/server/GameServer", root / "source/server/GameServer")
        self.root = root

    def run_check(self):
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
            code = check.main(["--repo", str(self.root), "--only", "epic_encounter_catalog"])
        return code, out.getvalue()

    def test_an_up_to_date_copy_passes_and_a_stale_one_fails(self):
        code, out = self.run_check()
        self.assertEqual(code, 0, out)
        md = self.root / "docs/encounters/EPIC-ENCOUNTERS.md"
        md.write_text(md.read_text(encoding="utf-8") + "\nA hand edit nobody regenerated.\n", encoding="utf-8")
        code, out = self.run_check()
        self.assertEqual(code, 1, out)
        self.assertIn("FAIL", out)
        self.assertIn("epic_encounter_catalog.py --check", out)


if __name__ == "__main__":
    unittest.main()
