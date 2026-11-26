"""Tests of the raid_attempts report section and raid_postmortem.py (#644 part 6) on synthetic rows written in the layout
of LivingRows.Raid.cs's Format* functions (no real log, no name from the owner's PC).

    python3 -m unittest source/tools/tests/test_raid_attempts.py
"""
import contextlib
import io
import re
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(HERE))
import raid_postmortem as pm  # noqa: E402
from living_report_sections import _raid, raid_attempts  # noqa: E402
from living_report_sections._util import fields  # noqa: E402

REPO = HERE.parents[1]
PINS = REPO / "source/server/Tests/UnitTests/UT_RaidAttemptRows644.cs"


def at(clock):
    return f"11/25 {clock} | INF | DOL.GS.LivingRows | "


def pull(clock, aid, boss="Orylle", n=1, size=96, period=64, pct=150, sf=0, roles="12:30:8:46", prev=None, wait=None,
         gather=None, event=None, kind="dungeon"):
    event = event or aid.rsplit(".", 1)[0]
    row = (f"RAID_ATTEMPT attempt={aid} event={event} kind={kind} region=61 boss=\"{boss}\" n={n} size={size} "
           f"period={period} sizePct={pct} sf={sf} roles={roles} classes=Cleric:20,Armsman:12 players=1 build=abc1234")
    if prev:
        row += f" prev={prev} wipeToPullS={wait}"
    if gather is not None:
        row += f" gatherS={gather}"
    return at(clock) + row


def end(clock, aid, outcome, secs, engaged=96, fallen=70, died=61, left=9, deaths=3, health=100, lowest=31):
    return at(clock) + (f"RAID_ATTEMPT_END attempt={aid} outcome={outcome} secs={secs} engaged={engaged} fallen={fallen} "
                        f"died={died} left={left} deaths={deaths} health={health} lowest={lowest}")


def death(clock, aid, t, idx, role="healer", kind="gamebot", killer_kind="boss", killer="Orylle", source="aoe",
          mechanic="-", healers=3, cls="Cleric", player=0, spell=4321):
    return at(clock) + (f"RAID_DEATH attempt={aid} t={t} idx={idx} kind={kind} player={player} class={cls} role={role} "
                        f"level=50 killerKind={killer_kind} killer=\"{killer}\" source={source} spellId={spell} "
                        f"mechanic={mechanic} dist=812 healersIn={healers} attackers=2")


K = "dungeon-albion-krondon"
NIGHT = [
    pull("22:00:00", f"{K}.1", gather=312),
    death("22:01:13", f"{K}.1", 73, 14, mechanic="breath"),
    death("22:01:20", f"{K}.1", 80, 3, role="tank", cls="Armsman", source="melee", mechanic="breath+adds", spell=0),
    death("22:01:22", f"{K}.1", 82, 15, kind="player", player=1, killer_kind="add", killer="Krondon Guard", healers=0),
    at("22:06:52") + f"REALM_EXPEDITION_WIPE region=61 target=\"Orylle\" level=70 failures=1 engaged=96 attempt={K}.1",
    end("22:06:52", f"{K}.1", "wipe", 412),
    pull("22:14:03", f"{K}.2", n=2, prev=f"{K}.1", wait=431),
    death("22:15:00", f"{K}.2", 57, 3, role="tank", cls="Armsman", source="melee", spell=0),
    end("22:20:03", f"{K}.2", "killed", 360, died=1, left=0, deaths=1, health=0, lowest=0),
    pull("23:00:00", "dragon-albion.1", boss="Golestandt", kind="dragon", size=120, period="-", pct="-", sf=24),
]


def quiet(fn, *args):
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = fn(*args)
    return code, out.getvalue(), err.getvalue()


class ParserTests(unittest.TestCase):
    def test_rows_join_on_the_attempt_id(self):
        found = _raid.attempts(NIGHT)
        self.assertEqual([a.key for a in found], [f"{K}.1", f"{K}.2", "dragon-albion.1"])
        first = found[0]
        self.assertEqual((first.boss, first.outcome, first.secs, len(first.deaths)), ("Orylle", "wipe", 412, 3))
        self.assertEqual(found[2].outcome, "open")
        self.assertIsNone(_raid.count(found[2].pull["period"]), "a dash is unknown, not 0")

    def test_an_id_pulled_again_after_a_restart_is_a_new_attempt(self):
        again = NIGHT[:6] + [pull("23:30:00", f"{K}.1"), death("23:30:40", f"{K}.1", 40, 1)]
        found = _raid.attempts(again)
        self.assertEqual([a.key for a in found], [f"{K}.1", f"{K}.1 (run 2)"])
        self.assertEqual([len(a.deaths) for a in found], [3, 1], "the later death goes to the newest pull")

    def test_a_death_before_its_pull_row_still_counts(self):
        found = _raid.attempts([death("22:01:13", "e.1", 73, 1), end("22:02:00", "e.1", "wipe", 120)])
        self.assertEqual((found[0].pull, found[0].outcome, len(found[0].deaths)), ({}, "wipe", 1))

    def test_the_rows_pinned_in_the_csharp_tests_parse(self):
        """The literal rows UT_RaidAttemptRows644 pins for FormatAttempt, FormatEnd and FormatDeath parse into the fields
        these reports read: if the C# rows change, this fails instead of the report silently misreading them."""
        text = PINS.read_text(encoding="utf-8")
        pinned = {}
        for m in re.finditer(r'Is\.EqualTo\(("RAID_[A-Z_]+ [^;]+?)\)\);', text):
            row = "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', m.group(1))).replace('\\"', '"')
            pinned[row.split(" ", 1)[0]] = row
        self.assertEqual(set(pinned), {"RAID_ATTEMPT", "RAID_ATTEMPT_END", "RAID_DEATH"})
        a = _raid.attempts([at("22:00:00") + pinned["RAID_ATTEMPT"]])[0]
        self.assertEqual((a.id, a.boss, a.pull["size"], a.pull["sizePct"], a.pull["roles"], a.pull["gatherS"]),
                         ("dungeon-albion-krondon.1", "Orylle", "96", "150", "12:30:8:46", "312"))
        e = fields(pinned["RAID_ATTEMPT_END"])
        self.assertEqual((e["outcome"], e["secs"], e["died"], e["left"], e["lowest"]), ("wipe", "412", "61", "9", "31"))
        d = fields(pinned["RAID_DEATH"])
        self.assertEqual((_raid.killer_of(d), d["role"], d["source"], _raid.mechanics_of(d), d["healersIn"]),
                         ("boss: Icelord Kvasir", "healer", "aoe", ["breath"], "3"))


class SectionTests(unittest.TestCase):
    def setUp(self):
        self.text = raid_attempts.summarize(NIGHT)

    def test_has_the_section_interface(self):
        self.assertEqual(raid_attempts.ROWS, ["RAID_ATTEMPT", "RAID_ATTEMPT_END", "RAID_DEATH"])
        self.assertTrue(raid_attempts.TITLE)

    def test_attempts_per_boss_outcomes_and_time_to_kill(self):
        self.assertIn("3 attempts (2 ended, 1 kill), 4 raider deaths.", self.text)
        row = next(line for line in self.text.splitlines() if line.startswith(f"| {K} | Orylle |"))
        cells = [c.strip() for c in row.strip("|").split("|")]
        # Event, Boss, Pulls, Outcomes, Kills, Median size, Size vs period, Time to kill, Time to fail, Lowest, Deaths
        self.assertEqual(cells, [K, "Orylle", "2", "wipe 1, killed 1", "50%", "96", "150%", "6:00", "6:52", "31%", "2"])
        dragon = next(line for line in self.text.splitlines() if line.startswith("| dragon-albion | Golestandt |"))
        self.assertIn("| open 1 |", dragon)
        self.assertIn("| - |", dragon, "no period size: a dash")
        self.assertIn("No end row (still live when the log stops, or the server stopped): dragon-albion.1.", self.text)

    def test_deaths_by_role_kind_killer_and_mechanic(self):
        self.assertIn("| healer | 2 | 50% |", self.text)
        self.assertIn("| tank | 2 | 50% |", self.text)
        self.assertIn("| gamebot | 3 | 75% |", self.text)
        self.assertIn("| player | 1 | 25% |", self.text)
        self.assertIn("| boss: Orylle | 3 | 75% |", self.text)
        self.assertIn("| add: Krondon Guard | 1 | 25% |", self.text)
        self.assertIn("| breath | 2 |", self.text)
        self.assertIn("| adds | 1 |", self.text)
        self.assertIn("Deaths with no living healer within range: 1 (25%).", self.text)
        self.assertIn("By the last hit: aoe 2 (50%), melee 2 (50%).", self.text)

    def test_died_against_left_and_wipe_to_next_pull(self):
        self.assertIn("died 62, left 9.", self.text)
        self.assertIn("Pulls after a failed attempt: 1. From the failed end to the next pull: median 7:11, longest 7:11.",
                      self.text)
        self.assertIn("Muster or regroup before a pull (1 pull): median 5:12", self.text)

    def test_no_rows_and_no_names(self):
        self.assertIn("No RAID_ATTEMPT", raid_attempts.summarize([]))
        self.assertIn("No RAID_ATTEMPT", raid_attempts.summarize([at("22:00:00") + "RAID_ATTEMPT attempt=- event=x"]))
        self.assertNotRegex(self.text, r"\bidx=|\bname\b")

    def test_the_report_loader_picks_the_section_up(self):
        import living_report as lr
        sections = {s.module: s for s in lr.load_sections()}
        self.assertIn("raid_attempts", sections)
        self.assertIsNone(sections["raid_attempts"].error)
        self.assertNotIn("_raid", sections, "the shared parser is not a section")


class PostmortemTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        self.logs = Path(self._tmp.name)
        noise = at("22:03:00") + "AUTONOMOUS_KILL_XP bot=1 xp=10"
        other_run = at("21:00:00") + f"REALM_EXPEDITION_ATTEMPT_ENDED region=61 target=\"Orylle\" attempt={K}.1"
        lines = NIGHT[:3] + [noise] + NIGHT[3:]
        (self.logs / "server.log").write_text("\n".join([other_run] + lines) + "\n", encoding="utf-8")

    def tearDown(self):
        self._tmp.cleanup()

    def test_one_attempts_timeline(self):
        code, out, err = quiet(pm.main, [f"{K}.1", str(self.logs)])
        self.assertEqual(code, 0, err)
        self.assertIn(f"# Raid attempt {K}.1", out)
        self.assertIn("Size 96 against a period size of 64 (150%), Special Forces 0", out)
        self.assertIn("Roles: tank 12, healer 30, buffer 8, attacker 46.", out)
        self.assertIn("Muster or regroup before the pull: 5:12.", out)
        self.assertIn("**wipe** after 6:52", out)
        self.assertIn("died 61, left 9", out)
        self.assertIn("## Deaths (3)", out)
        self.assertIn("Raider deaths: first at 1:13.", out, "three deaths of 96: no quarter mark")
        self.assertIn("Deaths per 15 s: 1:00 1, 1:15 2.", out)
        self.assertIn("| 1:22 | 15 | player (you) | Cleric | healer | 50 | add: Krondon Guard | aoe | 4321 | - | 812 | 0 | 2 |", out)
        self.assertIn("| 1:20 | 3 | gamebot | Armsman | tank | 50 | boss: Orylle | melee | - | breath+adds |", out)
        self.assertIn("## Other rows of this attempt (1)", out)
        self.assertIn("- 6:52 `REALM_EXPEDITION_WIPE", out)
        self.assertNotIn("21:00", out, "a row of the same id from another run of the server is left out")
        self.assertNotIn("AUTONOMOUS_KILL_XP", out)

    def test_a_retry_names_the_failed_attempt_and_the_wait(self):
        code, out, _ = quiet(pm.main, [f"{K}.2", str(self.logs / "server.log")])
        self.assertEqual(code, 0)
        self.assertIn(f"After failed attempt `{K}.1`: 7:11 from its end to this pull.", out)
        self.assertIn("**killed** after 6:00", out)

    def test_quarter_and_half_marks(self):
        rows = [pull("22:00:00", "e.1", size=4)] + [death(f"22:00:{10 * i:02d}", "e.1", 10 * i, i) for i in range(1, 4)]
        (self.logs / "small.log").write_text("\n".join(rows) + "\n", encoding="utf-8")
        _, out, _ = quiet(pm.main, ["e.1", str(self.logs / "small.log")])
        self.assertIn("Raider deaths: first at 0:10, a quarter of the pull's 4 at 0:10, half of the pull's 4 at 0:20.", out)

    def test_an_open_attempt_and_a_missing_one(self):
        code, out, _ = quiet(pm.main, ["dragon-albion.1", str(self.logs)])
        self.assertEqual(code, 0)
        self.assertIn("**No end row**", out)
        self.assertIn("No raider died in this attempt.", out)
        code, _, err = quiet(pm.main, ["nope.9", str(self.logs)])
        self.assertEqual(code, 1)
        self.assertIn("not in these logs", err)

    def test_list_and_runs(self):
        code, out, _ = quiet(pm.main, ["--list", str(self.logs)])
        self.assertEqual(code, 0)
        self.assertIn(f"| {K}.1 | 11/25 22:00:00 | Orylle | 96 | wipe | 6:52 | 3 |", out)
        again = self.logs / "restart.log"
        again.write_text("\n".join(NIGHT[:6] + [pull("23:30:00", f"{K}.1"), end("23:31:00", f"{K}.1", "killed", 60)]) + "\n",
                         encoding="utf-8")
        code, out, _ = quiet(pm.main, [f"{K}.1", str(again)])
        self.assertIn("was pulled 2 times", out)
        self.assertIn("**killed** after 1:00", out)
        code, out, _ = quiet(pm.main, [f"{K}.1", str(again), "--run", "1"])
        self.assertIn("**wipe** after 6:52", out)
        self.assertEqual(quiet(pm.main, [f"{K}.1", str(again), "--run", "3"])[0], 2)

    def test_bad_arguments(self):
        self.assertEqual(quiet(pm.main, [f"{K}.1"])[0], 2)
        self.assertEqual(quiet(pm.main, [f"{K}.1", str(self.logs / "none")])[0], 2)

    def test_out_writes_the_file(self):
        target = self.logs / "pm.md"
        self.assertEqual(quiet(pm.main, [f"{K}.1", str(self.logs), "--out", str(target)])[0], 0)
        self.assertIn("## Deaths (3)", target.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
