"""Tests for the informational metrics of source/tools/chat_audit.py (1.1.3): lines aimed at the player, the top speakers'
share of /faction, and follow-ups that refer back to a real earlier line. Small synthetic transcripts, made-up names.

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


def stamp(second):
    return f"11/08 20:{second // 60 % 60:02d}:{second % 60:02d}" if second < 3600 else f"11/08 21:{(second - 3600) // 60:02d}:{second % 60:02d}"


def log(rows):
    """rows: (second, who, channel, speaker, text, extra fields) -> a temporary log file path."""
    handle = tempfile.NamedTemporaryFile("w", suffix=".chat.log", delete=False, encoding="utf-8")
    for second, who, channel, speaker, text, extra in rows:
        handle.write(f"{stamp(second)} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who={who} ch={channel} realm=Albion "
                     f'from="{speaker}" via=Test {extra} text="{text}"\n')
    handle.close()
    return handle.name


class EngagementMetricTests(unittest.TestCase):
    def info(self, rows):
        path = log(rows)
        try:
            return chat_audit.engagement(path)
        finally:
            os.unlink(path)

    def test_lines_aimed_at_the_player_are_counted_per_hour(self):
        info = self.info([
            (0, "player", "tell", "Questor", "hey", 'to="Aldric"'),
            (60, "bot", "tell", "Aldric", "hey Questor! out in lyonesse", 'to="Questor" src=player'),
            (120, "bot", "faction", "Brenna", "anyone seen Questor tonight?", "src=player"),
            (180, "bot", "say", "Cadoc", "ouch, u ok?", "src=player"),
            (240, "bot", "faction", "Brenna", "lf healer for keltoi", "src=field"),
            (3599, "bot", "faction", "Brenna", "still lf healer", "src=field"),
        ])["player_directed"]
        self.assertEqual(info["players"], ["Questor"])
        self.assertEqual(info["lines"], 3)
        self.assertAlmostEqual(info["per_hour"], 3.0, delta=0.1)

    def test_the_top_ten_share_of_faction(self):
        rows = [(i, "bot", "faction", "Loud", "lf grp", "src=field") for i in range(0, 60, 2)]
        rows += [(100 + i, "bot", "faction", f"Quiet{i}", "lf grp", "src=field") for i in range(30)]
        info = self.info(sorted(rows))["faction_concentration"]["Albion"]
        self.assertEqual(info["speakers"], 31)
        self.assertEqual(info["busiest"], ("Loud", 30))
        self.assertAlmostEqual(info["top10_share"], (30 + 9) / 60, places=3)

    def test_a_follow_up_counts_only_when_its_subject_really_spoke_earlier(self):
        info = self.info([
            (0, "bot", "faction", "Aldric", "lf grp for keltoi", "src=field ex=1 i=0 role=asker"),
            (900, "bot", "faction", "Brenna", "aldric u ever find a grp earlier?", "src=followup ex=2 i=0 role=asker"),
            (906, "bot", "faction", "Aldric", "nah, ended up soloing", "src=followup ex=2 i=1 role=answerer"),
            (950, "bot", "faction", "Brenna", "cadoc did u ever find a camp?", "src=followup ex=3 i=0 role=asker"),
            (956, "bot", "faction", "Cadoc", "not yet, still looking", "src=followup ex=3 i=1 role=answerer"),
            (1000, "bot", "faction", "Brenna", "if anyone new needs directions just /send me", "src=bit ex=4 i=0 role=opener"),
        ])["follow_ups"]
        self.assertEqual(info["exchanges"], 2)
        self.assertEqual(info["anchored"], 1)
        self.assertEqual(info["unanchored"], 1, "Cadoc never spoke before he was asked after")
        self.assertEqual(info["bits"], 1)

    def test_the_metrics_never_change_the_verdict(self):
        path = log([(0, "bot", "faction", "Aldric", "lf grp", "src=field ex=1 i=0 role=asker cap=8.0")])
        try:
            summary = chat_audit.summarize(chat_audit.audit(path))
            self.assertTrue(chat_audit.verdict(summary)[0])
        finally:
            os.unlink(path)


if __name__ == "__main__":
    unittest.main()
