"""Tests of check_shots.py (issue #623): a failing screenshot run fails the installer job.

    python -m unittest discover -s tools/installer/tests -v
"""
import contextlib
import io
import re
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import check_shots as cs  # noqa: E402

REPO = HERE.parents[1]
PROGRAM = HERE / "src" / "Program.cs"
WORKFLOW = REPO / ".github" / "workflows" / "installer.yml"


def good_scale(root, scale, code="0", pages=cs.PAGES + cs.OPTIONAL):
    folder = root / f"scale-{scale}"
    folder.mkdir(parents=True, exist_ok=True)
    for name in pages:
        (folder / f"{name}.png").write_bytes(b"\x89PNG fake")
    (root / f"scale-{scale}.exit").write_text(code + "\r\n", encoding="utf-8")


def quiet(argv):
    with contextlib.redirect_stdout(io.StringIO()) as out:
        code = cs.run(argv)
    return code, out.getvalue()


class CheckShotsTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.root = Path(self._tmp.name)

    def tearDown(self):
        self._tmp.cleanup()

    def test_two_good_runs_pass(self):
        good_scale(self.root, "1")
        good_scale(self.root, "1.5")
        code, out = quiet(["--root", str(self.root), "--scales", "1", "1.5"])
        self.assertEqual(code, 0, out)
        self.assertNotIn("::error", out)

    def test_a_planted_failing_run_fails(self):
        good_scale(self.root, "1")
        good_scale(self.root, "1.5", code="-532462766")  # an unhandled .NET exception's exit code
        code, out = quiet(["--root", str(self.root), "--scales", "1", "1.5"])
        self.assertEqual(code, 1)
        self.assertIn("scale 1.5: the screenshot run exited -532462766", out)

    def test_a_run_that_never_finished_fails(self):
        good_scale(self.root, "1")
        code, out = quiet(["--root", str(self.root), "--scales", "1", "1.5"])
        self.assertEqual(code, 1)
        self.assertIn("scale 1.5: no exit code", out)

    def test_a_missing_or_empty_page_fails(self):
        good_scale(self.root, "1", pages=cs.PAGES[:-1])
        good_scale(self.root, "1.5")
        (self.root / "scale-1.5" / "02-choose.png").write_bytes(b"")
        code, out = quiet(["--root", str(self.root), "--scales", "1", "1.5"])
        self.assertEqual(code, 1)
        self.assertIn("scale 1: 08-failed.png is missing", out)
        self.assertIn("scale 1.5: 02-choose.png is empty", out)

    def test_a_missing_dialog_picture_only_warns(self):
        good_scale(self.root, "1", pages=cs.PAGES)
        code, out = quiet(["--root", str(self.root), "--scales", "1"])
        self.assertEqual(code, 0, out)
        self.assertIn("::warning title=Installer screenshots::scale 1: 09-dialog.png was not taken", out)

    def test_a_garbled_exit_code_fails(self):
        good_scale(self.root, "1", code="")
        code, out = quiet(["--root", str(self.root), "--scales", "1"])
        self.assertEqual(code, 1)
        self.assertIn("is not a number", out)


class InStepTests(unittest.TestCase):
    def test_pages_match_program_cs(self):
        text = PROGRAM.read_text(encoding="utf-8")
        body = text[text.index("static int Screenshots("):text.index("// ---", text.index("static int Screenshots("))]
        pages = tuple(re.findall(r'\("(\d\d-[a-z-]+)", \(\) =>', body))
        self.assertEqual(pages, cs.PAGES)
        self.assertIn('"09-dialog.png"', body)

    def test_the_workflow_records_exit_codes_and_runs_the_check(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        step = text[text.index("- name: Pictures of every page"):text.index("- uses: actions/upload-artifact", text.index("- name: Pictures of every page"))]
        self.assertIn("scale-$s.exit", step)
        self.assertIn("python tools/installer/check_shots.py --root tools/installer/shots --scales 1 1.5", step)
        self.assertNotIn("continue-on-error", step)
        # The pictures are still uploaded when the check fails, so a failure can be looked at.
        shots = text[text.index("name: screenshots") - 200:text.index("name: screenshots")]
        self.assertIn("if: always()", shots)


if __name__ == "__main__":
    unittest.main()
