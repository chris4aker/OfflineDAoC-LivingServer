"""The shipped Alloy config's privacy filter, replayed on synthetic lines only (issue #628).

    python3 -m unittest discover -s tools/grafana/tests -v

Every line here is made up. The filter is read from tools/grafana/alloy/config.alloy as it ships (alloy.Filter), so
these tests fail if the committed config stops dropping what it must.
"""
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import alloy  # noqa: E402
import kit  # noqa: E402

REPO = HERE.parents[2]
CONFIG = REPO / "tools/grafana/alloy/config.alloy"


def line(message: str, level: str = "INF", logger: str = "DOL.GS.Test") -> str:
    return f"11/25 21:04:05 | {level} | {logger} | {message}"


class FilterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.filter = alloy.load_filter(CONFIG)
        cls.kit = kit.Kit(REPO)

    def ship(self, message, **kw):
        return self.filter.apply(line(message, **kw))

    # -- what never ships -------------------------------------------------------------------------------------------
    def test_plain_server_lines_are_dropped(self):
        for message in [
            "Player Sirwhatever logged in from 192.168.1.20",
            "Sirwhatever says, \"anyone selling a ring?\"",
            "GameServer starting up",
            "Exception in pulse: System.NullReferenceException",
            "chat_line who=player",               # lower case is no marker
            "CHAT_LINEwho=player",                 # a marker must end at a space or the line's end
            "XCHAT_LINE who=player text=\"hi\"",  # nor start mid-word
        ]:
            self.assertIsNone(self.ship(message), message)

    def test_lines_outside_the_layout_are_dropped(self):
        for raw in ["   at DOL.GS.GameServer.Pulse() in GameServer.cs:line 12",
                    "System.Exception: boom",
                    "CHAT_LINE who=player ch=say realm=Albion from=\"Sirwhatever\" text=\"hi\"",  # no time, no level
                    ""]:
            self.assertIsNone(self.filter.apply(raw), raw)

    def test_account_and_network_rows_are_dropped(self):
        for message in [
            "ACCOUNT_LOGIN refused, too many wrong passwords from this address lately - someaccount:10.0.0.5",
            "HOST_PASSWORD new account refused, too many wrong host passwords from this address lately - acct:10.0.0.5",
            "CLIENT_HANDSHAKE addr=10.0.0.5 version=1.124",
            "WORLD_ENTER_REPEAT_IGNORED account=someaccount char=Sirwhatever",
            "CUSTOM_CLASS_REFUSED account=someaccount class=63",
        ]:
            self.assertIsNone(self.ship(message, level="WRN"), message)

    # -- what ships, minus names --------------------------------------------------------------------------------------
    def test_player_chat_line_keeps_counts_and_loses_names_and_text(self):
        out = self.ship('CHAT_LINE who=player ch=tell realm=Albion from="Sirwhatever" to="Aldwyn" intent=Question '
                        'zone="Camelot City" text="where do I train, Aldwyn?"')
        self.assertIsNotNone(out)
        for gone in ["Sirwhatever", "Aldwyn", "where do I train", "from=", "to=", "text="]:
            self.assertNotIn(gone, out.line)
        for kept in ["who=player", "ch=tell", "realm=Albion", "intent=Question", 'zone="Camelot City"']:
            self.assertIn(kept, out.line)
        self.assertEqual(out.labels["row"], "CHAT_LINE")
        self.assertEqual(out.labels["realm"], "Albion")

    def test_bot_chat_line_loses_its_speaker_and_text(self):
        out = self.ship('CHAT_LINE who=bot ch=say realm=Midgard from="Aldwyn" via=Reply src=reply text="hello there"')
        self.assertNotIn("Aldwyn", out.line)
        self.assertNotIn("hello", out.line)
        self.assertIn("who=bot", out.line)

    def test_names_with_escaped_quotes_are_removed_whole(self):
        out = self.ship('CHAT_LINE who=player ch=say realm=Albion from="Sir \\"Quoted\\" Name" text="a \\"b\\" c" intent=X')
        self.assertNotIn("Quoted", out.line)
        self.assertNotIn('"b', out.line)
        self.assertIn("intent=X", out.line)

    def test_exchange_sale_loses_seller_and_buyer(self):
        out = self.ship('AUTONOMOUS_EXCHANGE_SOLD realm=2 item="Ring of Might" quantity=1 price=500 seller="Sirwhatever" '
                        'sellerId=7 buyer="Aldwyn" buyerId=9 tax=5 crafted=0')
        self.assertNotIn("Sirwhatever", out.line)
        self.assertNotIn("Aldwyn", out.line)
        self.assertIn("price=500", out.line)
        self.assertEqual(out.labels["realm"], "Midgard")  # the realm's number, normalised

    def test_rvr_decide_keeps_decision_values_and_loses_the_bot(self):
        out = self.ship("RVR_DECIDE t=12 bot=Aldwyn id=4 realm=Hibernia force=f1 layer=group what=target from=keep "
                        "to=relic rule=r1 why=closer")
        self.assertNotIn("Aldwyn", out.line)
        self.assertIn("from=keep", out.line)
        self.assertIn("to=relic", out.line)

    def test_json_rows_blank_names(self):
        out = self.ship('AUTONOMOUS_GOAL_ATTEMPT {"bot":"Aldwyn","realm":1,"outcome":"done","target":"a wolf",'
                        '"members":[{"name":"Sirwhatever","role":"tank"}],"leader":"Sirwhatever"}')
        self.assertNotIn("Aldwyn", out.line)
        self.assertNotIn("Sirwhatever", out.line)
        self.assertIn('"outcome":"done"', out.line)
        self.assertIn('"bot":"-"', out.line)

    def test_ipv4_addresses_are_redacted_in_any_shipped_row(self):
        out = self.ship("SERVER_STALL worstTickMs=900 stage=npc gcPauseMs=1 gen2=0 heapMB=800 slowest=10.0.0.5")
        self.assertNotIn("10.0.0.5", out.line)

    def test_build_and_server_rows_ship_whole(self):
        msg = "FORK_BUILD name=1.2.3 commit=abc123 bot_guilds=on living=memory,grats bot_schedule=off sluaghbinder=off"
        out = self.ship(msg, level="WRN")
        self.assertEqual(out.line, msg)
        msg = "SERVER_STALL worstTickMs=900 stage=npc gcPauseMs=1 gen2=0 heapMB=800 slowAiTurns=2 bigAiTurns=0"
        self.assertEqual(self.ship(msg).line, msg)
        self.assertNotIn("realm", self.ship(msg).labels)

    def test_pending_625_rows_ship(self):
        msg = ("PLAYER_LINE_OUTCOME seq=3 t=100 ch=say realm=Albion intent=Question answered=1 latencyMs=2400 "
               "kind=answer bots=2 replies=2 window=30000")
        self.assertEqual(self.ship(msg).line, msg)
        out = self.ship("GROUP_ENDED realm=Albion reason=wipe leader=Aldwyn player=Sirwhatever size=5")
        self.assertNotIn("Aldwyn", out.line)  # set B's fields are not known yet: person keys go
        self.assertNotIn("Sirwhatever", out.line)
        self.assertIn("reason=wipe", out.line)

    def test_realm_label_is_low_cardinality(self):
        for value, label in [("Albion", "Albion"), ("1", "Albion"), ("Midgard", "Midgard"), ("3", "Hibernia"),
                             ("None", "other"), ("_FirstPlayerRealm", "other")]:
            out = self.ship(f"RVR_KEEP_TAKEN keep=Caer id=1 region=1 realm={value} battleground=False")
            self.assertEqual(out.labels["realm"], label, value)
        self.assertEqual(set(out.labels), {"job", "log", "row", "realm"})

    # -- every row in code, with every field it writes ---------------------------------------------------------------
    def test_every_shipped_row_loses_every_field_marked_dropped(self):
        checked = 0
        for marker, row in self.kit.rows.items():
            if not self.kit.ship[marker]:
                continue
            fields = sorted(row.fields)
            if not fields:
                continue
            message = marker + "".join(f' {k}="Secret{i}name"' if i % 2 else f" {k}=Secret{i}name"
                                       for i, k in enumerate(fields))
            out = self.ship(message)
            self.assertIsNotNone(out, marker)
            for i, k in enumerate(fields):
                if k in self.kit.drops[marker]:
                    self.assertNotIn(f"Secret{i}name", out.line, f"{marker} {k}")
                else:
                    self.assertIn(f"Secret{i}name", out.line, f"{marker} {k}")
            checked += 1
        self.assertGreater(checked, 600)

    def test_every_excluded_or_unshipped_row_is_dropped(self):
        for marker, ok in self.kit.ship.items():
            if not ok:
                self.assertIsNone(self.ship(f"{marker} a=1"), marker)

    def test_the_allowlist_is_the_kits(self):
        allowed = set(self.kit.allowed())
        self.assertEqual({m for m in allowed if self.filter.allow.search(m + " x=1")}, allowed)


class ReplaceSemanticsTests(unittest.TestCase):
    def test_capture_replaced_not_the_whole_match(self):
        import re
        out = alloy.Filter._replace(re.compile(r'"bot":("[^"]*")'), '{"bot":"A","x":1,"bot":"B"}', '"-"')
        self.assertEqual(out, '{"bot":"-","x":1,"bot":"-"}')


if __name__ == "__main__":
    unittest.main()
