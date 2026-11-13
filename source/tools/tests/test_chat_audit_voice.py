"""Tests for chat_audit.py's voice checks (1.1.3, after RC5) on small synthetic transcripts (made-up names, no played world).

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

ROW = "11/08 20:{m:02d}:{s:02d} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch={ch} realm=Albion "


def log(rows):
    """rows: (second, speaker, text, channel, extra fields) -> a temporary log file path."""
    handle = tempfile.NamedTemporaryFile("w", suffix=".chat.log", delete=False, encoding="utf-8")
    for second, speaker, text, ch, extra in rows:
        handle.write(ROW.format(m=second // 60 % 60, s=second % 60, ch=ch).replace("20:", f"{20 + second // 3600}:", 1) +
                     f'from="{speaker}" via=Test {extra} cap=8.4 text="{text}"\n')
    handle.close()
    return handle.name


class ChatAuditVoiceTests(unittest.TestCase):
    def summary(self, rows):
        path = log(rows)
        try:
            summary = chat_audit.summarize(chat_audit.audit(path))
            summary["verdict_ok"], summary["verdict_reasons"] = chat_audit.verdict(summary)
            return summary
        finally:
            os.unlink(path)

    def test_the_rc5_notices_in_a_bots_mouth_are_system_voice(self):
        s = self.summary([
            (0, "Diesalot", "Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel; at least 93 must arrive before assault.", "faction",
             "src=event ex=1 i=0 role=herald"),
            (60, "Eveisbel", "The Krondon expedition is advancing with 200 staged level-50 adventurers.", "faction", "src=event ex=2 i=0 role=herald"),
            (120, "Huskoen", "Fighting has broken out at Dun Crimthain. There is an opportunity to intervene.", "faction", "src=event ex=3 i=0 role=herald"),
            (180, "Svella", "Warbands are gathering to assault Nottmoor Faste. Join them at the siege camp", "faction", "src=event ex=4 i=0 role=herald"),
            (240, "Silverarrow", "People said the same before the dragon lairs. One winter they were just there, all three realms, same week.",
             "faction", "src=debate ex=5 i=0 role=E"),
            (300, "Tradebot", "wts sword; pst", "trade", "src=field"),
        ])
        self.assertEqual(s["counts"]["system_voice"], 6)
        self.assertFalse(s["verdict_ok"])
        self.assertIn("lines in system voice 6", s["verdict_reasons"])

    def test_player_voice_and_the_herald_itself_pass(self):
        s = self.summary([
            (0, "Diesalot", "krondon raid forming at diogel, need 50s", "faction", "src=event ex=1 i=0 role=herald"),
            (60, "Realm herald", "Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel; at least 93 must arrive before assault.",
             "faction", "src=event ex=2 i=0 role=herald"),
            (120, "Silverarrow", "People said the same before the dragon lairs", "faction", "src=debate ex=3 i=0 role=E"),
            (125, "Silverarrow", "One winter they were just there, all three realms, same week", "faction", "src=debate ex=3 i=0 role=E"),
            (200, "Grinner", "wink ;)", "faction", "src=field ex=4 i=0 role=asker"),
            (260, "Zssddxncp", "HELLO EVERY PLAYER!!! CHEAP GOLD AT THE SITE. BUY NOW AND GET MORE GOLD FOR LESS TODAY!!! FAST!!!", "faction",
             "src=spammer ex=5 i=0 role=spammer"),
        ])
        self.assertEqual(s["counts"]["system_voice"], 0, s["examples"].get("system_voice"))

    def test_a_longer_reaction_again_within_the_hour_is_counted_but_does_not_fail(self):
        s = self.summary([
            (0, "Teller", "we lost ailinne to hibs", "faction", "src=event ex=1 i=0 role=teller"),
            (5, "Ladypancake", "ugh, that one again", "faction", "src=event ex=1 i=1 role=reactor"),
            (1800, "Teller2", "we lost bolg to hibs", "faction", "src=event ex=2 i=0 role=teller"),
            (1805, "Berorwin", "ugh that one again", "faction", "src=event ex=2 i=1 role=reactor"),
            (5400, "Teller3", "we lost crimthain to hibs", "faction", "src=event ex=3 i=0 role=teller"),
            (5405, "Rosenwen", "ugh that one again", "faction", "src=event ex=3 i=1 role=reactor"),
            (5410, "Kerry", "rip", "faction", "src=event ex=3 i=2 role=reactor"),
        ])
        self.assertEqual(s["counts"]["reaction_repeat_60min"], 1, "the third is more than an hour after the second")
        self.assertNotIn("reaction_repeat_60min", " ".join(s["verdict_reasons"]))

    def test_system_voice_reasons(self):
        self.assertEqual(chat_audit.system_voice_reason("wts sword; pst"), "semicolon")
        self.assertIsNone(chat_audit.system_voice_reason("lol ;) nice"))
        self.assertEqual(chat_audit.system_voice_reason(
            "The hibs had a pass come out of their mist overnight. The mids got told about a fortress little known and less explored."),
            "formal paragraph")
        self.assertIsNone(chat_audit.system_voice_reason("the hibs had a pass come out of their mist overnight. the mids got told about a fortress"))
        self.assertIsNotNone(chat_audit.system_voice_reason("93 adventurers present at Caer Diogel; departing together for Krondon."))


if __name__ == "__main__":
    unittest.main()
