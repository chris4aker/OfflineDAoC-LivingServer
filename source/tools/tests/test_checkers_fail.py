"""Issue #618: the source/tools checkers CI runs, each shown failing on a planted defect and passing without it. The
epic encounter catalog's is in test_check.py (tools/check.py, issue #615), the CI glue's in test_ci_test_results.py.

    python3 -m unittest discover -s source/tools/tests -v
"""
import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import make_command_reference as reference  # noqa: E402


class CommandReferenceCheck(unittest.TestCase):
    """make_command_reference.py --check: ALL SERVER COMMANDS.txt must be what the registrations render to."""

    def test_an_up_to_date_file_passes_and_a_stale_or_missing_one_fails(self):
        text = reference.render(reference.commands())
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp) / "ALL SERVER COMMANDS.txt"
            with mock.patch.object(reference, "OUT", out), mock.patch.object(reference, "commands", return_value=reference.commands()), \
                    mock.patch.object(sys, "argv", ["make_command_reference.py", "--check"]), \
                    contextlib.redirect_stderr(io.StringIO()):
                out.write_bytes(text.replace("\n", "\r\n").encode("utf-8"))
                self.assertEqual(reference.main(), 0)
                out.write_text(text.replace("/hire", "/hyre", 1), encoding="utf-8")
                self.assertEqual(reference.main(), 1)
                out.unlink()
                self.assertEqual(reference.main(), 1)


if __name__ == "__main__":
    unittest.main()
