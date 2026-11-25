"""Issue #618: each bot-voice checker CI runs is shown failing on a planted defect, and passing without it (as
tools/check.py's stale-catalog test does for the encounter catalog). The say gate's ratchet has its own in
test_session_transcript.py; the bench and evening ratchets theirs in test_chat_bench.py.

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import build_corpus  # noqa: E402
import check_runtime_names  # noqa: E402
import debates_doc  # noqa: E402


def quietly(fn, *args):
    """Runs fn, returns (exit code, output): 0 when it returns, the SystemExit code when it exits."""
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        try:
            fn(*args)
            code = 0
        except SystemExit as e:
            code = e.code if isinstance(e.code, int) else 1
    return code, out.getvalue()


class DebatesDocCheck(unittest.TestCase):
    """debates_doc.py --check: DEBATES.md must be what corpus.json's scenes render to."""

    def test_an_up_to_date_doc_passes_and_a_stale_one_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            doc = Path(tmp) / "DEBATES.md"
            with mock.patch.object(debates_doc, "OUT", doc), mock.patch.object(debates_doc, "ROOT", Path(tmp)):
                with mock.patch.object(sys, "argv", ["debates_doc.py"]):
                    self.assertEqual(quietly(debates_doc.main)[0], 0)   # writes it
                with mock.patch.object(sys, "argv", ["debates_doc.py", "--check"]):
                    code, out = quietly(debates_doc.main)
                    self.assertEqual(code, 0, out)
                    doc.write_text(doc.read_text(encoding="utf-8") + "\n- **A:** a hand edit nobody regenerated\n",
                                   encoding="utf-8")
                    code, out = quietly(debates_doc.main)
                    self.assertEqual(code, 1, out)
                    self.assertIn("out of date", out)
                    doc.unlink()
                    self.assertEqual(quietly(debates_doc.main)[0], 1, "a missing doc fails too")


class CorpusVerify(unittest.TestCase):
    """build_corpus.py --verify's comparison (the full build takes about 25 s and runs in CI's own --verify line)."""

    def test_the_committed_corpus_passes_and_a_stale_or_missing_one_fails(self):
        built = json.loads(build_corpus.OUT.read_text(encoding="utf-8"))
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "corpus.json"
            path.write_text(json.dumps(built, ensure_ascii=False), encoding="utf-8")
            self.assertEqual(quietly(build_corpus.verify, built, path)[0], 0)
            stale = dict(built, lines=built["lines"][:-1])
            path.write_text(json.dumps(stale, ensure_ascii=False), encoding="utf-8")
            code, out = quietly(build_corpus.verify, built, path)
            self.assertNotEqual(code, 0)
            path.unlink()
            self.assertNotEqual(quietly(build_corpus.verify, built, path)[0], 0)


class RuntimeNames(unittest.TestCase):
    """check_runtime_names.py: an out-of-era dungeon or guild word chosen in code fails, as do too few dungeons."""

    def run_main(self, dungeons, words):
        with mock.patch.object(check_runtime_names, "dungeon_names", return_value=dungeons), \
                mock.patch.object(check_runtime_names, "guild_words", return_value=words), \
                mock.patch.object(check_runtime_names, "check_code_lines", return_value=0), \
                mock.patch.object(sys, "argv", ["check_runtime_names.py"]):
            return quietly(check_runtime_names.main)

    def test_clean_names_pass(self):
        code, out = self.run_main([f"Dungeon{i}" for i in range(14)], ["Wolves", "Oak"])
        self.assertEqual(code, 0, out)

    def test_an_out_of_era_dungeon_fails(self):
        code, out = self.run_main([f"Dungeon{i}" for i in range(13)] + ["the Catacombs of Cardova"], ["Wolves"])
        self.assertEqual(code, 1, out)
        self.assertIn("Catacombs of Cardova", out)

    def test_an_out_of_era_guild_word_fails(self):
        code, out = self.run_main([f"Dungeon{i}" for i in range(14)], ["Azure Tower"])
        self.assertEqual(code, 1, out)

    def test_a_checker_that_finds_too_few_dungeons_fails(self):
        code, out = self.run_main(["Dungeon"], ["Wolves"])
        self.assertEqual(code, 1, out)
        self.assertIn("checker needs updating", out)

    def test_a_banned_chat_literal_in_a_strict_file_fails(self):
        """The code-literal pass itself: a planted banned word in a strict owner's file is a problem, not a warning."""
        strict = sorted(check_runtime_names.chat_rules.STRICT_OWNERS)
        self.assertTrue(strict, "no strict owners: the code check fails nothing")
        literal = ("source/server/GameServer/bots/autonomous/Planted.cs:1", strict[0], "meet me in the Catacombs of Cardova", set())
        with mock.patch.object(check_runtime_names, "chat_literals", return_value=iter([literal])):
            problems = []
            quietly(check_runtime_names.check_code_lines, problems, False)
        self.assertEqual(len(problems), 1, problems)


if __name__ == "__main__":
    unittest.main()
