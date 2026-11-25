"""The publish-release workflow's pre-release wiring (issue #637), checked without running GitHub Actions: the inputs,
the dry run that publishes nothing, and the tag step's shell run for real on final and test-build tags."""
import os
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

WORKFLOW = Path(__file__).resolve().parents[3] / ".github/workflows/publish-release.yml"


def tag_step_script(text):
    """The run: block of the "The tag's builds" step, dedented."""
    m = re.search(r"- name: The tag's builds\n(?:.*\n)*?\s+run: \|\n((?:\s{10}.*\n|\n)+)", text)
    assert m, "no tag step"
    return "\n".join(line[10:] for line in m.group(1).splitlines())


class WorkflowTests(unittest.TestCase):
    def setUp(self):
        self.text = WORKFLOW.read_text(encoding="utf-8")

    def test_prerelease_input_is_off_by_default_and_dry_run_on(self):
        self.assertRegex(self.text, r"prerelease:\n\s+description: [^\n]+\n\s+type: boolean\n\s+default: false")
        self.assertRegex(self.text, r"dry_run:\n\s+description: [^\n]+\n\s+type: boolean\n\s+default: true")

    def test_the_dry_run_publishes_nothing(self):
        dry = self.text[self.text.index("  dry-run:"):self.text.index("  publish:")]
        self.assertIn("if: inputs.dry_run", dry)
        self.assertNotIn("contents: write", dry)
        self.assertRegex(dry, r"publish_release\.py publish .*--dry-run \$FLAG")
        publish = self.text[self.text.index("  publish:"):]
        self.assertIn("if: ${{ !inputs.dry_run }}", publish)
        self.assertNotIn("--dry-run", publish)
        self.assertIn("$FLAG", publish)

    def run_tag_step(self, tag, prerelease):
        with tempfile.TemporaryDirectory() as d:
            out = Path(d) / "out"
            out.write_text("")
            env = dict(os.environ, TAG=tag, PRERELEASE="true" if prerelease else "false", GITHUB_OUTPUT=str(out))
            r = subprocess.run(["bash", "-c", tag_step_script(self.text)], env=env, capture_output=True, text=True)
            values = dict(line.split("=", 1) for line in out.read_text().splitlines() if "=" in line)
            return r.returncode, values

    @unittest.skipUnless(shutil.which("bash"), "needs bash (CI's versions job has it)")
    def test_the_tag_step_names_the_package_build(self):
        self.assertEqual(self.run_tag_step("v1.2.3-rc.1", True), (0, {"build": "1.2.3", "flag": "--prerelease"}))
        self.assertEqual(self.run_tag_step("v1.2.3", False), (0, {"build": "1.2.3", "flag": ""}))
        self.assertEqual(self.run_tag_step("v1.1.4-beta", False), (0, {"build": "1.1.4-beta", "flag": ""}))

    @unittest.skipUnless(shutil.which("bash"), "needs bash (CI's versions job has it)")
    def test_the_tag_step_refuses_a_mismatch(self):
        for tag, prerelease in (("v1.2.3", True), ("v1.2.3-rc.1", False), ("v1.2.3-rc.0", True), ("v1.2.3-beta", True)):
            with self.subTest(tag=tag, prerelease=prerelease):
                self.assertNotEqual(self.run_tag_step(tag, prerelease)[0], 0)


if __name__ == "__main__":
    unittest.main()
