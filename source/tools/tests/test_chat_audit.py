"""Tests for source/tools/chat_audit.py on small synthetic transcripts (made-up names, no played world).

    python3 -m unittest discover -s source/tools/tests -v
"""
import os
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))

import chat_audit  # noqa: E402

PREFIX = "11/08 20:{m:02d}:{s:02d} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch=faction realm=Albion "


def log(rows):
    """rows: (second, speaker, text, extra fields) -> a temporary log file path."""
    handle = tempfile.NamedTemporaryFile("w", suffix=".chat.log", delete=False, encoding="utf-8")
    for second, speaker, text, *extra in rows:
        fields = (" " + extra[0]) if extra else ""
        handle.write(PREFIX.format(m=second // 60, s=second % 60) + f'from="{speaker}" via=Test{fields} text="{text}"\n')
    handle.close()
    return handle.name


class ChatAuditTests(unittest.TestCase):
    def counts(self, rows):
        path = log(rows)
        try:
            return chat_audit.summarize(chat_audit.audit(path))["counts"]
        finally:
            os.unlink(path)

    def test_a_presence_answer_after_the_close_is_out_of_order(self):
        c = self.counts([(0, "Aldric", "any reavers on? quick q"), (6, "Brenna", "main line first"), (9, "Aldric", "solo or grp?"),
                         (11, "Aldric", "makes sense ty"), (12, "Brenna", "sup")])
        self.assertEqual(c["answer_after_close"], 1)
        self.assertEqual(c["thanks_without_answer"], 1, "the thanks came straight after its own question")

    def test_a_level_question_after_the_invite(self):
        c = self.counts([(0, "Corwin", "need a tank for sh barrows"), (4, "Dagny", "sec, me"), (8, "Dagny", "cab, 50"), (11, "Corwin", "kk inv"),
                         (15, "Corwin", "what lvl r u?")])
        self.assertEqual(c["asks_after_invite"], 1)
        self.assertEqual(c["role_answer_false"], 1, "a cabalist is not a tank")

    def test_a_follow_up_before_any_answer(self):
        c = self.counts([(0, "Elspeth", "anyone know where to buy theurg stuff at 50"), (4, "Fenn", "brb"), (10, "Elspeth", "which part?")])
        self.assertEqual(c["followup_before_answer"], 1)

    def test_a_trade_closing_that_follows_good_luck(self):
        c = self.counts([(0, "Garr", "ring up on the exchange priced to move"), (10, "Hild", "gl selling, someone will want it"), (13, "Garr", "ill keep it then")])
        self.assertEqual(c["closing_mismatch"], 1)
        c = self.counts([(0, "Garr", "ring up on the exchange priced to move"), (10, "Hild", "ill pass, gl selling"), (13, "Garr", "ill keep it then")])
        self.assertEqual(c["closing_mismatch"], 0)

    def test_pile_ons_late_reactions_and_repeats(self):
        c = self.counts([(0, "Ivo", "got that dwarf berserker. we are even"), (8, "J1", "nice going"), (11, "J2", "well earned"), (13, "J3", "gz"),
                         (60, "Kara", "died to bwgans again"), (63, "L1", "oof"), (200, "Mags", "train of wolves, im dead"), (203, "L2", "oof")])
        self.assertEqual(c["pileon"], 1)
        self.assertEqual(c["pileon_lines_over_two"], 1)
        self.assertEqual(c["reaction_repeat_10min"], 1)
        self.assertEqual(c["reaction_no_trigger"], 0)

    def test_exact_checks_with_exchange_ids_and_classes(self):
        c = self.counts([
            (0, "Aldric", "any reavers on? quick q", 'src=field ex=7 i=0 role=asker cls="Reaver" lvl=50'),
            (6, "Brenna", "main line first", 'src=thread ex=7 i=3 role=answerer cls="Reaver" lvl=47'),
            (9, "Aldric", "solo or grp spec?", 'src=thread ex=7 i=2 role=asker cls="Reaver" lvl=50'),
            (12, "Nym", "me, sup", 'src=thread ex=7 i=1 role=answerer cls="Wizard" lvl=44'),
            (20, "Orla", "me, cab 50", 'src=thread ex=8 i=1 role=answerer cls="Cabalist" lvl=49'),
        ])
        self.assertEqual(c["exact_out_of_order"], 1)
        self.assertEqual(c["exact_role_swap"], 1, "two speakers as the answerer")
        self.assertEqual(c["level_claim_false"], 1, "a level 49 says 50")

    def test_a_clean_thread_flags_nothing(self):
        c = self.counts([
            (0, "Aldric", "any reavers on? quick q", "src=field ex=9 i=0 role=asker"), (5, "Brenna", "me, sup", "src=thread ex=9 i=1 role=answerer"),
            (10, "Aldric", "what do u put points in at 50?", "src=thread ex=9 i=2 role=asker"),
            (15, "Brenna", "main line first", "src=thread ex=9 i=3 role=answerer"), (18, "Aldric", "makes sense ty", "src=thread ex=9 i=4 role=asker"),
        ])
        flagged = {k: v for k, v in c.items() if v and k != "short_line_repeat_10min"}
        self.assertEqual(flagged, {})


if __name__ == "__main__":
    unittest.main()
