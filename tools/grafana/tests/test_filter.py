"""The shipped Alloy config's privacy filter, replayed on synthetic lines only (issue #628).

    python3 -m unittest discover -s tools/grafana/tests -v

Every line here is made up. The filter is the Alloy config the kit builds from this checkout's code (kit.Kit.alloy_config,
read with alloy.Filter), so these tests fail if the config main ships from this code stops dropping what it must. The
committed tools/grafana/alloy/config.alloy can lag one merge behind (main regenerates it, #817).
"""
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import alloy  # noqa: E402
import kit  # noqa: E402

REPO = HERE.parents[2]
# #817: the committed copy lags a pull request's code (main regenerates it after the merge), so the tests read the
# config the kit builds from this checkout's code, written to a temp file.
_CONFIG_DIR = None


def current_config() -> Path:
    global _CONFIG_DIR
    if _CONFIG_DIR is None:
        import kit  # noqa: E402
        _CONFIG_DIR = tempfile.TemporaryDirectory()
        (Path(_CONFIG_DIR.name) / "config.alloy").write_text(kit.Kit(REPO).alloy_config(), encoding="utf-8", newline="\n")
    return Path(_CONFIG_DIR.name) / "config.alloy"


def line(message: str, level: str = "INF", logger: str = "DOL.GS.Test") -> str:
    return f"11/25 21:04:05 | {level} | {logger} | {message}"


class FilterTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.filter = alloy.load_filter(current_config())
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

    def test_625_rows_ship(self):
        # #625's rows are on main now (set A and set B), so their real lines ship whole: counts and kinds, no names.
        msg = ("PLAYER_LINE_OUTCOME seq=3 t=100 ch=say realm=Albion intent=Question answered=1 latencyMs=2400 "
               "kind=answer bots=2 replies=2 window=30000")
        self.assertEqual(self.ship(msg).line, msg)
        msg = ("GROUP_ENDED seq=1 every=1 dropped=0 group=albion-21308c-052 realm=Albion objective=GroupPve "
               "reason=recovery cause=member_stuck last_left=- phase=\"Meeting up\" task_started=0 camp=0 dur_s=156 "
               "size_start=6 size_end=6 alive_end=6 left=0 left_by=- wipes=0")
        out = self.ship(msg)
        self.assertEqual(out.line, msg)
        self.assertEqual(out.labels["realm"], "Albion")

    def test_names_the_first_filter_missed_are_dropped(self):
        # Review of #633: these shipped a player's name before the filter failed closed.
        for message, kept in [
            ('QUEST_XP player="Sirwhatever" level=20 source="quest"', "level=20"),
            ('GM_JUMP_BOT gm="Sirwhatever" bot="Aldwyn" kind=gamebot', "kind=gamebot"),
            ('BUFFBOT_DIED buffbot="Aldwyn" main="Sirwhatever" killer="a wolf"', "BUFFBOT_DIED"),
            ('EPIC_SWEEP_END why="done" results=1 guest="Sirwhatever" guestRealm=Albion', "results=1"),
            ('EPIC_ENCOUNTER_STARTED encounter=OFGreenKnight by="Sirwhatever" bot=no how=x', "encounter=OFGreenKnight"),
            ('TALE_START realm=Albion scene=1 teller="Aldwyn" asked_by="Sirwhatever"', "scene=1"),
            ('LIVING_SERVER_REZ_ASK bot="Aldwyn" rezzer="Sirwhatever"', "LIVING_SERVER_REZ_ASK"),
            ('RVR_ROAM_HUNT group=12 realm=Albion prey="Sirwhatever"', "group=12"),
            ('AUTONOMOUS_XP_PROGRESS bot="Aldwyn" level=20', "level=20"),
            ('DB_BACKUP result=failed error="C:\\Users\\Sirwhatever\\db"', "result=failed"),
        ]:
            out = self.ship(message)
            self.assertIsNotNone(out, message)
            self.assertNotIn("Sirwhatever", out.line, message)
            self.assertNotIn("Aldwyn", out.line, message)
            self.assertIn(kept, out.line, message)

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
