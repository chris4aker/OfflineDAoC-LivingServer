"""Issue #475: the session chat log into transcripts (session_transcript.py) and the say gate's ratchet (say_gate_ratchet.py).
Synthetic rows only; no played log.

  python -m unittest discover -s tools/bot-voices/tests -v
"""
import json
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import say_gate_ratchet  # noqa: E402
import session_transcript as st  # noqa: E402

LOG = """\
11/24 20:00:00 | CHAT_SESSION build=1.2.3 commit=abc1234 rows=say,hear,held,burst,flag gate=pass-through
11/24 20:00:05 | CHAT_SAY seq=2 t=5000 ch=faction room="Albion" realm=Albion from="Marelle" id=7 kind=gamebot src=ambient via=X.Y act=? line=? contract=none styled=? rule=pass heard="Chris" text="anyone for Mularn?"
11/24 20:00:09 | CHAT_HEAR seq=3 t=9000 ch=tell room="Marelle" realm=Albion from="Chris" to="Marelle" intent=Location zone="Camelot" grouped="" heard="Chris" text="I'm by camp station"
11/24 20:00:11 | CHAT_SAY seq=4 t=11000 ch=tell room="Chris" realm=Albion from="Marelle" to="Chris" re=3 src=player_reply via=X.Y act=? line=? contract=none styled=? rule=pass heard="Chris" text="lol Chris"
11/24 20:00:12 | CHAT_SAY seq=5 t=12000 ch=group room="group:Sven" realm=Albion from="Sven" src=other via=X.Y act=? line=? contract=none styled=? rule=pass heard="Other" text="not for Chris"
11/24 20:00:20 | CHAT_HELD seq=6 t=20000 ch=faction realm=Albion to="Chris" outcome=silent code=no-fit rule="none" score=0 trigger="player Chris" reason="nothing fit"
11/24 20:00:30 | SCREEN_BURST seq=7 t=30000 to="Chris" realm=Albion type=25 n=10 window=900ms key="*'s weapon returns to normal." text="XxEvilxX's weapon returns to normal."
11/24 20:30:00 | CHAT_FLAG seq=8 t=40000 to="Chris" realm=Albion at=4 ch=tell lines="2,4" zone="Camelot" note="a laugh to Chris's place"
11/24 21:00:00 | CHAT_SAY seq=9 t=50000 ch=faction room="Albion" realm=Albion from="Marelle" src=ambient via=X.Y act=? line=? contract=none styled=? rule=pass heard="Chris" text="gn all"
11/24 21:05:00 | CHAT_SESSION build=1.2.4 commit=def5678 rows=say,hear,held,burst,flag gate=pass-through
11/24 21:05:10 | CHAT_SAY seq=1 t=1000 ch=say room="Camelot" realm=Albion from="Marelle" src=other via=X.Y act=? line=? contract=none styled=? rule=pass heard="Chris" text="hi"
11/24 21:05:11 | DOL.GS.Something | not a gate row
"""


class TranscriptTests(unittest.TestCase):
    def setUp(self):
        self.dir = Path(tempfile.mkdtemp())
        self.log = self.dir / "session-chat.log"
        self.log.write_text(LOG, encoding="utf-8")

    def test_rows_are_split_by_build_and_never_mixed(self):
        parts = st.sections(st.read_rows([self.log], 2026))
        self.assertEqual([p["build"] for p in parts], ["1.2.3", "1.2.4"])
        self.assertEqual(len(parts[0]["rows"]), 8)
        self.assertEqual(len(parts[1]["rows"]), 1)

    def test_the_transcript_is_the_players_screen_with_names_masked(self):
        out = self.dir / "out"
        self.assertEqual(st.main([str(self.log), "--out", str(out), "--year", "2026"]), 0)
        md = (out / "session-11-24-1.2.3-Player1.md").read_text(encoding="utf-8")
        self.assertIn("(Marelle) <Faction> anyone for Mularn?", md)
        self.assertIn('You send, "I\'m by camp station" to Marelle', md)
        self.assertIn('Marelle sends, "lol Player1"', md)
        self.assertIn("-- held (faction, nobody): no-fit", md)
        self.assertIn("[System x10 in 900ms] XxEvilxX's weapon returns to normal.", md)
        self.assertIn(">>> /odd #8 marks 2,4: a laugh to Player1's place", md)
        self.assertNotIn("not for", md, "a line the player didn't get isn't on their screen")
        self.assertNotIn("Chris", md)
        lines = [json.loads(line) for line in (out / "session-11-24-1.2.3.jsonl").read_text(encoding="utf-8").splitlines()]
        self.assertNotIn("Chris", json.dumps(lines))
        self.assertEqual(lines[2]["re"], "3")
        self.assertEqual(lines[0]["seen_by"], ["Player1"])
        # Screen order: the rows' order in the file.
        order = [line.split("#", 1)[1].split()[0] for line in md.splitlines() if line[:2].isdigit()]
        self.assertEqual(order, ["2", "3", "4", "6", "7", "8", "9"])

    def test_defects_are_counted_per_hour_per_channel_per_build(self):
        summary = self.dir / "summary.json"
        st.main([str(self.log), "--json", str(summary), "--year", "2026"])
        first, second = json.loads(summary.read_text(encoding="utf-8"))
        self.assertEqual(first["build"], "1.2.3")
        self.assertEqual(first["hours"], 1.0)
        self.assertEqual(first["flags"], 1)
        self.assertEqual(first["per_hour"], 1.0)
        self.assertEqual(first["per_channel"], {"tell": {"flags": 1, "per_hour": 1.0}})
        self.assertEqual(first["bursts"], 1)
        self.assertEqual(first["held"], 1)
        self.assertEqual(second["flags"], 0)

    def test_a_log_without_rows_says_so(self):
        empty = self.dir / "empty.log"
        empty.write_text("11/24 20:00:00 | INF | DOL | nothing\n", encoding="utf-8")
        self.assertEqual(st.main([str(empty)]), 2)


class RatchetTests(unittest.TestCase):
    def test_sends_inside_a_gate_call_are_not_raw(self):
        lines = [
            '        BotSayGate.Say(new BotSayGate.Utterance("group", bot.Name, bot.Realm, text), said =>',
            "        {",
            '            bot.Group.SendMessageToGroupMembers(bot, said, eChatType.CT_Group, eChatLoc.CL_ChatWindow); // (a ")" here',
            "            return said;",
            "        });",
            "        player.Out.SendMessage(\"(raw) text\", eChatType.CT_System, eChatLoc.CL_SystemWindow);",
        ]
        self.assertEqual(say_gate_ratchet.gated_lines(lines), {0, 1, 2, 3, 4})

    def test_a_rise_fails_and_a_fall_is_reported(self):
        rises, falls = say_gate_ratchet.check({"a.cs": 3, "new.cs": 1}, {"a.cs": 2, "b.cs": 4})
        self.assertEqual(len(rises), 2)
        self.assertEqual(len(falls), 1)

    def test_the_repository_passes_its_own_ratchet(self):
        counts, outside, gate_calls = say_gate_ratchet.scan()
        self.assertEqual(outside, [], "only BotSayGate.cs records a bot line")
        baseline = json.loads(say_gate_ratchet.BASELINE.read_text(encoding="utf-8"))
        rises, _ = say_gate_ratchet.check(counts, baseline["raw_sends"])
        self.assertEqual(rises, [])
        self.assertGreaterEqual(gate_calls, 17, "the 17 wire exits go through the gate")


if __name__ == "__main__":
    unittest.main()
