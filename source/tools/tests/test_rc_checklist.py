"""Tests of rc_checklist.py and rc3-checklist.json on small synthetic server logs (no real log or name from a PC).

    python3 -m pytest -q source/tools/tests/test_rc_checklist.py
"""
import contextlib
import datetime as dt
import io
import json
import re
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(TOOLS))
import rc_checklist as rc  # noqa: E402

REPO = TOOLS.parents[1]
SERVER = REPO / "source" / "server" / "GameServer"
T0 = dt.datetime(2026, 11, 28, 6, 0, 0)


class Log:
    """server.log lines as logconfig.xml lays them out: 'MM/DD HH:MM:SS | LVL | logger | message'."""

    def __init__(self):
        self.lines = []

    def add(self, minutes, message, level="INF", logger="DOL.GS.Test"):
        t = T0 + dt.timedelta(minutes=minutes)
        self.lines.append(f"{t.strftime('%m/%d %H:%M:%S')} | {level} | {logger} | {message}")
        return self

    def start(self, minutes=0, name="1.2.3", commit="abc1234def56"):
        self.add(minutes, "Current directory is: C:\\Games\\LivingServer\\runtime\\server", "DBG", "DOL.GS.GameServer")
        return self.add(minutes, f"FORK_BUILD name={name} commit={commit} bot_guilds=on", "WRN", "DOL.GS.ForkBuildInfo")

    def text(self):
        return "\n".join(self.lines) + "\n"


def run(*logs, args=(), checklist=None):
    with tempfile.TemporaryDirectory() as tmp:
        paths = []
        for i, log in enumerate(logs):
            path = Path(tmp) / f"server{i}.log"
            path.write_text(log.text(), encoding="utf-8")
            paths.append(str(path))
        extra = []
        if checklist is not None:
            cpath = Path(tmp) / "checklist.json"
            cpath.write_text(json.dumps(checklist), encoding="utf-8")
            extra = ["--checklist", str(cpath)]
        out = io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(io.StringIO()):
            code = rc.main([*paths, "--json", "--year", "2026", *extra, *args])
        return code, json.loads(out.getvalue())


def check(result, check_id, build=0):
    for issue in result["builds"][build]["issues"]:
        for c in issue["checks"]:
            if c["id"] == check_id:
                return c
    raise KeyError(check_id)


def issue(result, number, build=0):
    return next(i for i in result["builds"][build]["issues"] if i["issue"] == number)


class Statuses(unittest.TestCase):
    def test_pass_when_every_row_is_seen_and_first_line_quoted(self):
        log = Log().start()
        log.add(5, "RVR_FORCE_ZERG what=formed id=3 realm=Albion")
        log.add(9, "RVR_FORCE_ZERG what=joined id=3 realm=Albion")
        log.add(10, "RVR_FORCE_ZERG what=joined id=3 realm=Albion")
        code, result = run(log)
        self.assertEqual(code, 0)
        c = check(result, "599-zerg")
        self.assertEqual(c["status"], "PASS")
        self.assertEqual([r["count"] for r in c["rows"]], [1, 2])
        self.assertEqual(c["rows"][0]["first"], "11/28 06:05:00")
        self.assertIn("what=formed", c["rows"][0]["line"])

    def test_not_seen_lists_the_missing_row(self):
        log = Log().start().add(5, "RVR_FORCE_ZERG what=formed id=3 realm=Albion")
        c = check(run(log)[1], "599-zerg")
        self.assertEqual(c["status"], "NOT SEEN")
        self.assertIn("1/2 rows seen", c["why"][0])
        self.assertIn("joined|left|moved|ended", c["why"][0])

    def test_never_row_fails_even_when_rows_are_seen(self):
        log = Log().start()
        log.add(1, "BOT_REALM_ABILITY_USED bot=Aelf ability=BunkerOfFaith health=40")
        log.add(2, 'BOT_REALM_ABILITY_TRAINED bot="Aelf" class=Cleric realmLevel=12 points=3 archetype=Healer '
                   'gained="AvoidanceOfMagic 1"')
        c = check(run(log)[1], "531-realm-abilities")
        self.assertEqual(c["status"], "FAIL")
        self.assertEqual(c["never"][0]["count"], 1)
        self.assertEqual(issue(run(log)[1], 531)["status"], "FAIL")

    def test_in_game_only_line(self):
        c = check(run(Log().start())[1], "564-client")
        self.assertEqual(c["status"], "IN GAME")
        self.assertEqual(len(c["in_game"]), 6)
        self.assertEqual(issue(run(Log().start())[1], 564)["status"], "IN GAME")

    def test_pass_with_in_game_parts_keeps_them_for_the_reader(self):
        log = Log().start().add(3, "KEEP_LEVEL_DEFAULT_RAISED keeps=4 level=5")
        c = check(run(log)[1], "526-keep-level")
        self.assertEqual(c["status"], "PASS")
        self.assertTrue(c["in_game"])
        self.assertTrue(c["also"])


class Conditions(unittest.TestCase):
    def test_numeric_regex_and_range_conditions(self):
        self.assertTrue(rc.condition(">8")("9"))
        self.assertFalse(rc.condition(">8")("8"))
        self.assertFalse(rc.condition(">8")(None))
        self.assertTrue(rc.condition("16..24")("24"))
        self.assertFalse(rc.condition("16..24")("25"))
        self.assertTrue(rc.condition("re:^(own|server)$")("own"))
        self.assertFalse(rc.condition("re:^(own|server)$")("ownx"))
        self.assertTrue(rc.condition("True")("True"))
        self.assertFalse(rc.condition("True")("False"))

    def test_field_conditions_pick_lines(self):
        log = Log().start()
        log.add(1, "RVR_FORCE_CLUSTER t=1 realm=Albion parties=2 bots=8 clusters=1 biggest=8 over24=0")
        log.add(6, "RVR_FORCE_CLUSTER t=2 realm=Albion parties=3 bots=20 clusters=1 biggest=20 over24=0")
        c = check(run(log)[1], "599-cluster")
        self.assertEqual(c["status"], "PASS")
        self.assertEqual(c["rows"][0]["count"], 1)
        self.assertEqual(c["count"][0]["count"], 1)   # 16..24

    def test_any_of_rows(self):
        log = Log().start()
        log.add(1, 'BOT_GUILD_CLAIM_SKIPPED keep="Dun Crauchon" reason=no-members')
        log.add(2, "RVR_CLAIM_HOLD target=12 realm=Albion")
        self.assertEqual(check(run(log)[1], "523-claim")["status"], "PASS")

    def test_rate_bar_and_minimum_run(self):
        log = Log().start()
        for minute in range(0, 7 * 60, 10):
            log.add(minute, f"SERVER_HANDLE_CENSUS total=900 byType=\"File=10\" grew=\"\" ")
            log.add(minute, "SERVER_MEMORY heapMB=1 memoryLoadPct=40 highLoadPct=90 lastBlockingGen2=none "
                            'topAllocations="System.String 3MB/min 40%"')
            log.add(minute, "SERVER_GC_PAUSE ms=30 gc=1 gen=2 reason=x kind=Blocking last=none")
        c = check(run(log)[1], "540-memory")
        self.assertEqual(c["status"], "PASS", c["why"])
        for minute in range(0, 7 * 60, 5):
            log.add(minute, "SERVER_STALL worstTickMs=1500 stage=none")
        c = check(run(log)[1], "540-memory")
        self.assertEqual(c["status"], "FAIL")
        self.assertGreaterEqual(c["rate"][0]["per_hour"], 9.8)

    def test_short_run_is_not_seen(self):
        log = Log().start()
        log.add(1, 'SERVER_HANDLE_CENSUS total=900 byType="File=10"')
        log.add(1, "SERVER_MEMORY memoryLoadPct=40 highLoadPct=90 lastBlockingGen2=none topAllocations=\"x 1MB/min\"")
        log.add(60, "SERVER_GC_PAUSE ms=30 last=none")
        c = check(run(log)[1], "540-memory")
        self.assertEqual(c["status"], "NOT SEEN")
        self.assertIn("needs 6 h", " ".join(c["why"]))

    def test_one_stall_in_a_short_run_is_not_seen_not_fail(self):
        log = Log().start()
        log.add(1, 'SERVER_HANDLE_CENSUS total=900 byType="File=10"')
        log.add(1, "SERVER_MEMORY memoryLoadPct=40 highLoadPct=90 lastBlockingGen2=none topAllocations=\"x 1MB/min\"")
        log.add(2, "SERVER_GC_PAUSE ms=30 last=none")
        log.add(3, "SERVER_STALL worstTickMs=1500 stage=none")
        log.add(5, "SERVER_GC_PAUSE ms=30 last=none")
        c = check(run(log)[1], "540-memory")
        self.assertEqual(c["status"], "NOT SEEN", c["why"])
        self.assertIn("needs 6 h", " ".join(c["why"]))
        self.assertGreater(c["rate"][0]["per_hour"], 9.8)   # the projected rate is still shown

    def test_never_row_fails_a_short_run(self):
        log = Log().start().add(1, "SERVER_MEMORY memoryLoadPct=40 topAllocations="
                                   '"System.ValueTuple`2[System.Int32,System.Int32][] 90MB/min 60%"')
        self.assertEqual(check(run(log)[1], "540-memory")["status"], "FAIL")

    def test_value_tuple_at_top_of_allocations_fails(self):
        log = Log().start().add(1, "SERVER_MEMORY memoryLoadPct=40 topAllocations="
                                   '"System.ValueTuple`2[System.Int32,System.Int32][] 90MB/min 60%; System.String 1MB/min"')
        self.assertEqual(check(run(log)[1], "540-memory")["status"], "FAIL")
        log = Log().start().add(1, "SERVER_MEMORY memoryLoadPct=40 topAllocations="
                                   '"System.String 1MB/min; System.ValueTuple`2[System.Int32,System.Int32][] 2MB/min"')
        self.assertEqual(check(run(log)[1], "540-memory")["never"][0]["count"], 0)


class BackOffJoin(unittest.TestCase):
    def test_route_recovery_aimed_at_a_back_off_point_fails(self):
        log = Log().start()
        log.add(1, "RVR_SIGHT_BACK_OFF bot=Aelf id=7 realm=Albion region=200 enemies=3 nearest=900 to=1000,2000,30 "
                   "waitSeconds=8 called=False")
        log.add(3, "AUTONOMOUS_ROUTE_RECOVERY bot=Other id=8 region=200 failedDestination=1010,2010,30 x=1")
        c = check(run(log)[1], "766-backoff")
        self.assertEqual(c["status"], "PASS")     # another bot: not joined
        log.add(4, "AUTONOMOUS_ROUTE_RECOVERY bot=Aelf id=7 region=200 failedDestination=1050,2050,30 x=1")
        c = check(run(log)[1], "766-backoff")
        self.assertEqual(c["status"], "FAIL")
        self.assertEqual(c["never"][0]["count"], 1)
        self.assertEqual(c["count"][0]["count"], 2)

    def test_far_recovery_passes(self):
        log = Log().start()
        log.add(1, "RVR_SIGHT_BACK_OFF bot=Aelf id=7 region=200 to=1000,2000,30")
        log.add(3, "AUTONOMOUS_ROUTE_RECOVERY bot=Aelf id=7 region=200 failedDestination=1200,2000,30")
        self.assertEqual(check(run(log)[1], "766-backoff")["status"], "PASS")


class Builds(unittest.TestCase):
    def test_builds_are_judged_apart_and_other_builds_are_not_judged(self):
        log = Log().start(0, commit="aaaa1111")
        log.add(1, "RVR_FORCE_ZERG what=formed id=1")
        log.start(30, commit="bbbb2222")
        log.add(31, "RVR_FORCE_ZERG what=joined id=1")
        log.start(60, name="1.2.2", commit="cccc3333")
        log.add(61, "RVR_FORCE_ZERG what=formed id=1")
        code, result = run(log)
        self.assertEqual(code, 0)
        self.assertEqual([b["commit"] for b in result["builds"]], ["aaaa1111", "bbbb2222"])
        self.assertEqual(check(result, "599-zerg", 0)["status"], "NOT SEEN")
        self.assertEqual(check(result, "599-zerg", 1)["status"], "NOT SEEN")
        self.assertEqual([s["commit"] for s in result["not_judged"]], ["cccc3333"])
        _, picked = run(log, args=("--build", "bbbb"))
        self.assertEqual([b["commit"] for b in picked["builds"]], ["bbbb2222"])

    def test_no_build_is_exit_2(self):
        log = Log().add(1, "RVR_FORCE_ZERG what=formed id=1")
        code, result = run(log)
        self.assertEqual(code, 2)
        self.assertEqual(result["builds"], [])

    def test_two_files_each_split_on_its_own(self):
        server = Log().start().add(1, "RVR_ASSIST_CALL group=g1 realm=Albion region=250")
        detail = Log().add(2, "AUTONOMOUS_AREA_HUNT bot=Aelf id=7 level=12 camp=c", "DBG")
        code, result = run(server, detail)
        self.assertEqual(len(result["builds"]), 1)
        self.assertEqual(len(result["not_judged"]), 1)   # a detail.log with no FORK_BUILD of its own

    def test_chat_names_are_masked(self):
        log = Log().start().add(1, 'CHAT_LINE who=player ch=say realm=Albion from="Somebody" text="hi"')
        line = check(run(log)[1], "480-followups")["rows"][0]["line"]
        self.assertNotIn("Somebody", line)
        self.assertIn("from=<name>", line)

    def test_companion_leader_and_client_address_are_masked(self):
        log = Log().start()
        log.add(1, 'COMPANION_RVR_ENGAGE leader="Somebody" call=assist target="Foe" kind=player why=leader')
        log.add(2, 'COMPANION_RVR_ENGAGE leader="Somebody" call=drop target="Foe" kind=player why=leash')
        log.add(3, 'COMPANION_LEADER_FOCUS bot="Helper" class="Cleric" kind=companion leader="Somebody" why=attacker')
        log.add(4, "CLIENT_HANDSHAKE addr=192.168.1.20 version=1127 type=2 client=original")
        result = run(log)[1]
        lines = [r["line"] for r in check(result, "664-engage")["rows"]]
        lines.append(check(result, "571-focus")["rows"][0]["line"])
        lines.append(check(result, "425-handshake")["rows"][0]["line"])
        for line in lines:
            self.assertIsNotNone(line)
            self.assertNotIn("Somebody", line)
            self.assertNotIn("192.168", line)
        self.assertIn("leader=<name>", lines[0])
        self.assertIn("leader=<name>", lines[2])
        self.assertIn("addr=<addr>", lines[3])
        self.assertIn('bot="Helper"', lines[2])   # a bot's name is not the player's: kept

    def test_text_report_has_summary_first(self):
        log = Log().start().add(1, "RVR_FORCE_ZERG what=formed id=1")
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "server.log"
            path.write_text(log.text(), encoding="utf-8")
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                self.assertEqual(rc.main([str(path), "--year", "2026"]), 0)
        text = out.getvalue()
        self.assertLess(text.index("Summary (issues):"), text.index("\n#766 ("))
        self.assertIn("first 11/28 06:01:00: RVR_FORCE_ZERG what=formed", text)
        out = io.StringIO()
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / "server.log"
            path.write_text(log.text(), encoding="utf-8")
            with contextlib.redirect_stdout(out):
                rc.main([str(path), "--year", "2026", "--brief"])
        brief = out.getvalue()
        self.assertIn("[NOT SEEN] 599-zerg", brief)
        self.assertNotIn("in game:", brief)


class DataFile(unittest.TestCase):
    data = json.loads(rc.CHECKLIST.read_text(encoding="utf-8"))

    def matchers(self):
        for issue in self.data["issues"]:
            for c in issue["checks"]:
                for r in c.get("rows", []):
                    yield from (r["any"] if "any" in r else [r])
                for key in ("count", "never", "rate"):
                    yield from c.get(key, [])
                for n in c.get("near", []):
                    yield n["a"]
                    yield n["b"]

    def test_both_checklists_are_encoded(self):
        on_503 = {766, 664, 599, 572, 531, 530, 529, 528, 526, 523, 520}
        on_476 = {768, 540, 544, 569, 570, 571, 565, 564, 562, 580, 479, 480, 425}
        self.assertEqual({i["issue"] for i in self.data["issues"] if i["list"] == 503}, on_503)
        self.assertEqual({i["issue"] for i in self.data["issues"] if i["list"] == 476}, on_476)
        ids = [c["id"] for i in self.data["issues"] for c in i["checks"]]
        self.assertEqual(len(ids), len(set(ids)))

    def test_every_row_and_field_is_written_by_the_server(self):
        """A typo or a renamed row would leave a line NOT SEEN for ever: every tag is in the server code, and every
        field a line tests is written in a file that writes that tag."""
        writers = {}
        files = [p for p in SERVER.rglob("*.cs") if "Tests" not in p.parts]
        texts = {p: p.read_text(encoding="utf-8", errors="replace") for p in files}
        missing = []
        for m in self.matchers():
            tag = m["tag"]
            if tag not in writers:
                writers[tag] = [t for t in texts.values() if re.search(rf"\b{re.escape(tag)}\b", t)]
            if not writers[tag]:
                missing.append(tag)
                continue
            for field in m.get("where", {}):
                if not any(f"{field}=" in t for t in writers[tag]):
                    missing.append(f"{tag} {field}=")
        self.assertEqual(missing, [])

    def test_every_condition_parses(self):
        for m in self.matchers():
            for value in (m.get("where") or {}).values():
                rc.condition(value)("1")


if __name__ == "__main__":
    unittest.main()
