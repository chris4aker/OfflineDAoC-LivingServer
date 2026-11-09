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

    def audit_rows(self, rows, ceiling=None):
        path = log(rows)
        try:
            summary = chat_audit.summarize(chat_audit.audit(path, ceiling=ceiling))
            return summary, chat_audit.verdict(summary)
        finally:
            os.unlink(path)

    def test_the_ceiling_counts_only_lines_it_holds(self):
        # 16 bot lines in 60 s is 8 a minute over two minutes against a cap of 6: above it, unless they are a tagged event.
        busy = [(k * 4, f"B{k}", f"line {k}", f"src=field ex={100 + k} i=0 role=asker cap=6.0") for k in range(16)]
        summary, (passed, reasons) = self.audit_rows(busy)
        self.assertGreater(summary["counts"]["minutes_over_ceiling"], 0)
        self.assertFalse(passed)
        event = [(k * 4, f"B{k}", f"line {k}", f"src=event ex=7 i={k} role=reactor cap=6.0") for k in range(16)]
        summary, _ = self.audit_rows(event)
        self.assertEqual(summary["counts"]["minutes_over_ceiling"], 0)

    def test_a_clean_tagged_evening_passes(self):
        rows = [
            (0, "Aldric", "any reavers on? quick q", 'src=field ex=9 i=0 role=asker cls="Reaver" lvl=50 cap=7.0'),
            (5, "Brenna", "me, sup", 'src=thread ex=9 i=1 role=answerer cls="Reaver" lvl=47 cap=7.0'),
            (10, "Aldric", "what do u put points in at 50?", 'src=thread ex=9 i=2 role=asker cls="Reaver" lvl=50 cap=7.0'),
            (15, "Brenna", "main line first", 'src=thread ex=9 i=3 role=answerer cls="Reaver" lvl=47 cap=7.0'),
            (18, "Aldric", "makes sense ty", 'src=thread ex=9 i=4 role=asker cls="Reaver" lvl=50 cap=7.0'),
            (60, "Cuthbert", "caer benowyc fell, mids have it", "src=event ex=10 i=0 role=teller cap=7.0"),
            (64, "D1", "again??", "src=event ex=10 i=1 role=reactor cap=7.0"),
            (70, "D2", "omw", "src=event ex=10 i=2 role=reactor cap=7.0"),
            (78, "D3", "need to take it back", "src=event ex=10 i=3 role=reactor cap=7.0"),
        ]
        summary, (passed, reasons) = self.audit_rows(rows)
        self.assertTrue(passed, reasons)
        self.assertEqual(summary["counts"]["pileon"], 0, "an event draws up to three")
        rows.append((82, "D4", "porting", "src=event ex=10 i=4 role=reactor cap=7.0"))
        summary, (passed, _) = self.audit_rows(rows)
        self.assertEqual(summary["counts"]["pileon"], 1, "but not four")
        self.assertFalse(passed)

    def test_an_old_log_without_ids_or_cap_cannot_pass(self):
        _, (passed, reasons) = self.audit_rows([(0, "A", "hi all"), (5, "B", "o/")])
        self.assertFalse(passed)
        self.assertTrue(any("ceiling unknown" in r for r in reasons))

    def test_replies_to_a_player_tagged_ex0_are_not_one_exchange(self):
        c = self.counts([(0, "Drazzeeeen", "check the exchange in camelot", "src=player ex=0 i=0 role=answerer cap=7.0"),
                         (180, "Speedysaracen", "later", "src=player ex=0 i=0 role=answerer cap=7.0")])
        self.assertEqual(c["exact_role_swap"], 0)
        self.assertEqual(c["exact_out_of_order"], 0)

    def feature_rows(self, rows):
        """rows: (second, raw text after the stamp) for any log row, or (second, speaker, text, fields) for a CHAT_LINE."""
        handle = tempfile.NamedTemporaryFile("w", suffix=".chat.log", delete=False, encoding="utf-8")
        for second, *rest in rows:
            stamp = f"11/08 20:{second // 60:02d}:{second % 60:02d}"
            if len(rest) == 1:
                handle.write(f"{stamp} | INF | DOL.GS.Test | {rest[0]}\n")
            else:
                speaker, text, fields = rest
                handle.write(f'{stamp} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch=faction realm=Albion from="{speaker}" via=Test {fields} text="{text}"\n')
        handle.close()
        try:
            summary = chat_audit.summarize(chat_audit.audit(handle.name, ceiling=10))
            summary["verdict"] = chat_audit.verdict(summary)
            return summary
        finally:
            os.unlink(handle.name)

    def test_a_feature_that_was_due_and_never_spoke_fails(self):
        # The RC1 live check: a spammer started (due) and said nothing; the rumour that waited spoke.
        s = self.feature_rows([(0, "LIVING_FEATURE_DUE feature=spammer realm=Albion ex=41 name=\"Xkqhoaa\""),
                               (2, "LIVING_FEATURE_DUE feature=rumour realm=Albion ex=42 topic=named_up held=ceiling"),
                               (20, "Natalie", "heard grimwold is up again in black mtns", "src=rumour ex=42 i=0 role=teller cap=10.0"),
                               (300, "Aldric", "anyone seen my horse", "src=ambient ex=43 i=0 role=opener cap=10.0")])
        self.assertEqual(s["counts"]["features_due"], 2)
        self.assertEqual(s["counts"]["features_silent"], 1, "the spammer")
        passed, reasons = s["verdict"]
        self.assertFalse(passed)
        self.assertIn("silent features 1", reasons)

    def test_a_feature_given_up_counts_but_not_one_nobody_could_hear(self):
        s = self.feature_rows([(0, "LIVING_FEATURE_DUE feature=roleplayer realm=Albion ex=7 held=ceiling"),
                               (181, "LIVING_FEATURE_DROPPED feature=roleplayer realm=Albion ex=7 waited_s=181 reason=ceiling"),
                               (190, "LIVING_FEATURE_DUE feature=spammer realm=Albion ex=8"),
                               (200, "LIVING_FEATURE_DROPPED feature=spammer realm=Albion ex=8 waited_s=10 reason=no_audience"),
                               (205, "Aldric", "anyone seen my horse", "src=ambient ex=9 i=0 role=opener cap=10.0")])
        self.assertEqual(s["counts"]["features_silent"], 1)
        self.assertEqual(s["counts"]["features_given_up"], 1)

    def test_a_feature_due_near_the_end_of_the_log_is_not_judged(self):
        s = self.feature_rows([(0, "Aldric", "anyone seen my horse", "src=ambient ex=9 i=0 role=opener cap=10.0"),
                               (60, "LIVING_FEATURE_DUE feature=spammer realm=Albion ex=8")])
        self.assertEqual(s["counts"]["features_due"], 1)
        self.assertEqual(s["counts"]["features_silent"], 0)

    def say_rows(self, rows):
        handle = tempfile.NamedTemporaryFile("w", suffix=".chat.log", delete=False, encoding="utf-8")
        for second, speaker, text, fields in rows:
            handle.write(f'11/08 21:{second // 60:02d}:{second % 60:02d} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch=say '
                         f'realm=Albion from="{speaker}" via=Test {fields} text="{text}"\n')
        handle.close()
        try:
            summary = chat_audit.summarize(chat_audit.audit(handle.name, ceiling=10))
            summary["verdict"] = chat_audit.verdict(summary)
            return summary
        finally:
            os.unlink(handle.name)

    def test_a_wall_of_say_at_the_bind_fails(self):
        # The RC1 live check at Camelot's bind: about 25 "released, running back" lines in 45 s.
        lines = ["released, running back", "at bind, omw back", "no rez, released. coming back to you"]
        s = self.say_rows([(i * 2, f"Bot{i}", lines[i % 3], "src=other cap=5.0") for i in range(25)])
        self.assertGreater(s["counts"]["say_minutes_over_ceiling"], 0)
        passed, reasons = s["verdict"]
        self.assertFalse(passed)
        self.assertTrue(any("/say" in r for r in reasons), reasons)

    def test_say_under_its_ceiling_and_answers_to_the_player_pass(self):
        rows = [(i * 30, f"Bot{i}", "anyone selling arrows", "src=other cap=5.0") for i in range(8)]
        rows += [(241 + i, f"Ans{i}", "hi there", f"src=player ex={100 + i} i=1 role=answerer cap=5.0") for i in range(6)]
        s = self.say_rows(sorted(rows))
        self.assertEqual(s["counts"]["say_minutes_over_ceiling"], 0)

    def test_an_armour_slot_is_not_a_class_claim(self):
        # RC2 live check (2026-11-09): an infiltrator 26 buying arm armour was read as an armsman claim.
        c = self.counts([(0, "Wulfrun", "wtb leather arms 4 26 inf", 'src=other cls="Infiltrator" lvl=26')])
        self.assertEqual(c["class_claim_false"], 0)
        self.assertEqual(c["two_classes_claimed"], 0)
        self.assertEqual(chat_audit.class_claims("wtb leather arms 4 26 inf"), [("infiltrator", 26)])
        self.assertEqual(chat_audit.class_claims("wts studded arms, pst"), [])
        self.assertEqual(chat_audit.class_claims("anyone selling chain arms 40?"), [])
        # A real armsman still claims one, and a false one is still caught.
        self.assertEqual(chat_audit.class_claims("arms 45 lfg"), [("armsman", 45)])
        c = self.counts([(0, "Wulfrun", "arms 45 lfg", 'src=other cls="Infiltrator" lvl=45')])
        self.assertEqual(c["class_claim_false"], 1)


if __name__ == "__main__":
    unittest.main()
