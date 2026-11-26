"""kit.py --check: the catalog, the Alloy config and the dashboards stay current and name real rows (issue #628).

Each check is planted with a defect in a temp copy and must fail there, and pass on the clean copy (docs/CI.md).
"""
import contextlib
import io
import json
import os
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))

import dashboards  # noqa: E402
import kit  # noqa: E402
import privacy  # noqa: E402
import rows  # noqa: E402

REPO = HERE.parents[2]


def run_check(repo: Path) -> tuple[int, str]:
    out = io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(out):
        code = kit.main(["--check", "--repo", str(repo)])
    return code, out.getvalue()


class CheckTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        (root / "docs").mkdir()
        shutil.copy2(REPO / "docs/LOG-ROWS.md", root / "docs/LOG-ROWS.md")
        shutil.copytree(REPO / "tools/grafana", root / "tools/grafana",
                        ignore=shutil.ignore_patterns("__pycache__", "tests"))
        (root / "source").mkdir()
        try:  # read only; a link avoids copying the server tree
            os.symlink(REPO / "source/server", root / "source/server", target_is_directory=True)
        except (OSError, NotImplementedError):
            shutil.copytree(REPO / "source/server/GameServer", root / "source/server/GameServer")
        self.root = root

    def test_the_repository_passes(self):
        code, out = run_check(REPO)
        self.assertEqual(code, 0, out)

    def test_a_clean_copy_passes_and_a_stale_catalog_fails(self):
        code, out = run_check(self.root)
        self.assertEqual(code, 0, out)
        md = self.root / "docs/LOG-ROWS.md"
        md.write_text(md.read_text(encoding="utf-8") + "\nA hand edit.\n", encoding="utf-8")
        code, out = run_check(self.root)
        self.assertEqual(code, 1)
        self.assertIn("docs/LOG-ROWS.md is stale", out)

    def test_a_hand_edited_alloy_config_fails(self):
        cfg = self.root / "tools/grafana/alloy/config.alloy"
        cfg.write_text(cfg.read_text(encoding="utf-8").replace("CHAT_LINE|", "", 1), encoding="utf-8")
        code, out = run_check(self.root)
        self.assertEqual(code, 1)
        self.assertIn("config.alloy is stale", out)

    def test_a_stray_dashboard_fails(self):
        (self.root / "tools/grafana/resources/dashboards/extra.json").write_text("{}", encoding="utf-8")
        code, out = run_check(self.root)
        self.assertEqual(code, 1)
        self.assertIn("extra.json is not generated", out)

    def test_a_policy_row_that_does_not_exist_fails(self):
        path = self.root / "tools/grafana/row-policy.json"
        policy = json.loads(path.read_text(encoding="utf-8"))
        policy["exclude_rows"]["NO_SUCH_ROW_628"] = "planted"
        path.write_text(json.dumps(policy), encoding="utf-8")
        code, out = run_check(self.root)
        self.assertEqual(code, 1)
        self.assertIn("NO_SUCH_ROW_628", out)

    def test_a_row_neither_in_code_nor_pending_fails_the_dashboards_that_read_it(self):
        # #690: BG_POPULATION is read from code now (it was a pending entry); the scan is made to miss it instead, as it
        # did before the scanner read LivingRows formatters.
        real_scan = rows.scan

        def missing_it(repo):
            found = real_scan(repo)
            found.pop("BG_POPULATION", None)
            return found
        with mock.patch.object(rows, "scan", missing_it):
            code, out = run_check(self.root)
        self.assertEqual(code, 1)
        self.assertIn("row BG_POPULATION is neither in code nor pending", out)

    def test_a_pending_entry_for_a_row_in_code_fails(self):
        # #690's acceptance: pending-rows.json has no entries for rows on main.
        path = self.root / "tools/grafana/pending-rows.json"
        pending = json.loads(path.read_text(encoding="utf-8"))
        pending["rows"]["GOAL_PASS"] = {"issue": 690, "branch": "main", "log": "server.log", "fields": ["t"]}
        path.write_text(json.dumps(pending), encoding="utf-8")
        code, out = run_check(self.root)
        self.assertEqual(code, 1, out)
        self.assertIn("GOAL_PASS", out)


class DashboardRuleTests(unittest.TestCase):
    """The panel rules, checked on the live Kit with a planted panel."""

    def setUp(self):
        self.kit = kit.Kit(REPO)
        self.saved = list(dashboards.DASHBOARDS)
        self.addCleanup(lambda: dashboards.DASHBOARDS.__setitem__(slice(None), self.saved))

    def plant(self, panel):
        dashboards.DASHBOARDS.append(dashboards.Dashboard("planted", "Planted", "", [panel]))
        return self.kit.problems()

    def test_clean(self):
        self.assertEqual(self.kit.problems(), [])

    def test_unknown_row(self):
        errors = self.plant(dashboards.Panel("x", dashboards.count_by("NOPE_ROW", "a"), ["NOPE_ROW"]))
        self.assertTrue(any("NOPE_ROW is neither in code nor pending" in e for e in errors), errors)

    def test_unknown_field(self):
        errors = self.plant(dashboards.Panel("x", dashboards.count_by("SERVER_STALL", "nope"), ["SERVER_STALL"], ["nope"]))
        self.assertTrue(any("has no field nope" in e for e in errors), errors)

    def test_dropped_field(self):
        errors = self.plant(dashboards.Panel("x", dashboards.count_by("CHAT_LINE", "from"), ["CHAT_LINE"], ["from"]))
        self.assertTrue(any("the filter drops CHAT_LINE from" in e for e in errors), errors)

    def test_unshipped_row(self):
        errors = self.plant(dashboards.Panel("x", dashboards.count_by("ACCOUNT_LOGIN", "a"), ["ACCOUNT_LOGIN"]))
        self.assertTrue(any("ACCOUNT_LOGIN is not shipped" in e for e in errors), errors)

    def test_a_field_of_a_row_whose_fields_are_not_known_fails(self):
        # Every #625 row is on main now, so plant a pending row with no known fields (only its marker and realm ship).
        marker = "PLANTED_ROW_628"
        self.kit.pending[marker] = {"issue": 628, "branch": "planted", "log": "server.log", "frequency": "-",
                                    "meaning": "-", "fields": None}
        self.kit.ship[marker], self.kit.why_not[marker] = True, ""
        self.kit.marker_only.add(marker)
        self.kit.drops[marker] = {"*": "pending row, fields not known yet: only the marker and realm ship"}
        errors = self.plant(dashboards.Panel("x", dashboards.count_by(marker, "reason"), [marker], ["reason"]))
        self.assertTrue(any(f"{marker}'s fields are not known yet" in e for e in errors), errors)


class ScanTests(unittest.TestCase):
    """The scanner finds rows in the shapes the server writes them, on planted C#."""

    def scan(self, cs: str):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        root = Path(tmp.name)
        (root / "source/server/GameServer/config").mkdir(parents=True)
        shutil.copy2(REPO / rows.LOGCONFIG, root / rows.LOGCONFIG)
        (root / "source/server/GameServer/Planted.cs").write_text(cs, encoding="utf-8")
        (root / "source/server/Tests").mkdir()
        (root / "source/server/Tests/UT_Planted.cs").write_text('Log.Info("TEST_ONLY_ROW a=1");', encoding="utf-8")
        return rows.scan(root)

    def test_shapes(self):
        found = self.scan('''
class A {
    public const string Marker = "PLANTED_CONST";
    void F(GamePlayer player, GameBot bot) {
        Log.Info($"PLANTED_LITERAL who=x realm={realm} from=\\"{player.Name}\\" n={count}");
        Log.Info("PLANTED_JSON " + JsonSerializer.Serialize(new { bot = bot.Name }));
        Emit($"{Marker} seq={seq} target={LivingRows.Kind(target)}");
        var row = new StringBuilder("PLANTED_BUILDER");
        row.Append(" bot=").Append(bot.Name).Append(" hp=").Append(hp);
        Log.Error("PLANTED_ERROR: could not read the file");
        // Log.Info("PLANTED_COMMENTED a=1");
        if (x == "PLANTED_COMPARE") return;
        Log.Info("AUTONOMOUS_GOAL_ATTEMPT_LIKE a=1");
    }
}''')
        self.assertIn("PLANTED_LITERAL", found)
        self.assertEqual(set(found["PLANTED_LITERAL"].fields), {"who", "realm", "from", "n"})
        self.assertIn("player.Name", found["PLANTED_LITERAL"].fields["from"])
        self.assertTrue(found["PLANTED_JSON"].json)
        self.assertEqual(set(found["PLANTED_CONST"].fields), {"seq", "target"})
        self.assertEqual(set(found["PLANTED_BUILDER"].fields), {"bot", "hp"})
        for absent in ["PLANTED_ERROR", "PLANTED_COMMENTED", "PLANTED_COMPARE", "TEST_ONLY_ROW"]:
            self.assertNotIn(absent, found)

    def test_livingrows_formatters(self):
        """#690: the shapes the LivingRows files use. Several row classes in one file, each with its own `const string
        Marker`; a StringBuilder with a capacity after the marker; an Append chain that ends at its ToString()."""
        found = self.scan('''
public static class FirstRows
{
    public const string Marker = "PLANTED_FIRST";
    public static string Format(long now, string caster) =>
        string.Create(CultureInfo.InvariantCulture, $"{Marker} t={now} caster={caster} note={(x ? "a{" : "b}")}");
}

/// <summary>A comment with a brace { and a string "}" in it.</summary>
public static class SecondRows
{
    public const string Marker = "PLANTED_SECOND";
    private const char Open = '{';
    public static string Format(long now, int bots)
    {
        var row = new StringBuilder(Marker, 200);
        row.Append(" t=").Append(now).Append(" bots=").Append(bots);
        return row.ToString();
    }
    public static string Other() => new StringBuilder("x").Append(" stray=").Append(1).ToString();

    public static class Inner
    {
        public const string Marker = "PLANTED_INNER";
        public static string Format(int n) => $"{Marker} n={n}";
    }

    public static string After(int k) => $"{Marker} k={k}";
}
''')
        self.assertEqual(set(found["PLANTED_FIRST"].fields), {"t", "caster", "note"})
        self.assertEqual(set(found["PLANTED_SECOND"].fields), {"t", "bots", "k"},
                         "its own const, after the nested class too; the stray Append after ToString() is not its field")
        self.assertEqual(set(found["PLANTED_INNER"].fields), {"n"})
        self.assertEqual(len(found["PLANTED_FIRST"].sites), 1)

    def test_a_new_stringbuilder_row_is_found(self):
        # #690's acceptance: a planted StringBuilder-style row, as LivingRows.Goals.cs writes GOAL_PASS.
        found = self.scan('''
public static class PlantedRows
{
    public const string PassMarker = "PLANTED_PASS";
    public static string Pass(long now, int band) {
        var row = new StringBuilder(PassMarker, 220);
        row.Append(" t=").Append(now.ToString(CultureInfo.InvariantCulture))
            .Append(" band=").Append(band.ToString(CultureInfo.InvariantCulture));
        return row.ToString();
    }
}''')
        self.assertIn("PLANTED_PASS", found)
        self.assertEqual(set(found["PLANTED_PASS"].fields), {"t", "band"})

    def test_mask_blanks_strings_chars_and_comments(self):
        text = 'a = "{"; b = \'}\'; c = \'\\\'\'; // }\nd = @"x""}"; /* { */ e = $"{X} {(q ? "}" : "{")}"; { }'
        masked = rows.mask(text)
        self.assertEqual(len(masked), len(text))
        self.assertEqual(masked.count("{"), 1)
        self.assertEqual(masked.count("}"), 1)
        self.assertEqual(masked.count("\n"), 1)

    def test_the_livingrows_rows_on_main(self):
        """The real code: the four rows of #690 with their fields, and CC_BREAK and BG_RALLY_RESPONSE with only their own
        (the scanner used to give them CAST_INTERRUPT's and BG_TARGET_PICK's, whose const shares their name)."""
        found = rows.scan(REPO)
        want = {
            "BG_POPULATION": {"t", "bg", "bracket", "realm", "bots", "bgGoal", "passing", "travelling", "assigned", "eligible",
                              "expected", "companions", "player"},
            "CAST_INTERRUPT": {"seq", "every", "t", "caster", "pet", "realm", "spell", "castMs", "leftMs", "outcome", "why",
                               "attacker", "attackerPet", "region", "bg"},
            "BG_TARGET_PICK": {"seq", "every", "t", "bg", "realm", "src", "pick", "target", "skippedControlled", "skippedEngines",
                               "rally", "list", "candidates"},
            "GOAL_PASS": {"t", "realm", "band", "members", "pool", "bgEligible", "target", "held", "open", "planned",
                          "personalities"},
            "CC_BREAK": {"t", "cc", "target", "breaker", "breakerPet", "caster", "side", "ms", "attack", "region", "bg"},
            "BG_RALLY_RESPONSE": {"t", "bg", "realm", "contact", "pet", "ms", "nth"},
        }
        for marker, keys in want.items():
            self.assertIn(marker, found)
            self.assertEqual(set(found[marker].fields), keys, marker)
        policy = privacy.load_policy(REPO / "tools/grafana/row-policy.json", REPO / "tools/grafana/pending-rows.json")
        for marker in want:
            self.assertEqual(privacy.dropped_fields(found[marker], policy), {}, f"{marker}: no field reads a name")
        self.assertEqual(dict(policy.pending), {}, "no pending entry for a row on main")

    def test_detail_log_rows_follow_logconfig(self):
        found = self.scan('class A { void F() { Log.Info($"AUTONOMOUS_GOAL_ATTEMPT_X a={1}"); Log.Info($"OTHER_ROW a={1}"); } }')
        self.assertEqual(found["AUTONOMOUS_GOAL_ATTEMPT_X"].log, "detail.log")
        self.assertEqual(found["OTHER_ROW"].log, "server.log")


class NameRuleTests(unittest.TestCase):
    def test_names(self):
        reads = privacy.expression_reads_a_name
        for expr, key in [("player.Name", "x"), ("bot.Name", "x"), ("sale.SellerName", "seller"),
                          ("RvrRecordText.Field(bot)", "bot"), ("Quote(line.Speaker)", "x"), ("op?.Name ?? \"none\"", "owner"),
                          ("target?.Name ?? \"none\"", "target"), ("x.Name", "killer"),
                          ('string.Join(",", members.Select(m => m.Name))', "dead"), ("client.Account.Name", "account")]:
            self.assertTrue(reads(expr, key), expr)
        for expr, key in [("taken.Keep.Name", "keep"), ("sale.ItemName", "item"), ("bot.Level", "level"),
                          ("RvrRecordText.Field(from ?? \"-\")", "from"), ("GlobalConstants.RealmToName(bot.Realm)", "realm"),
                          ("hub.Name", "hub"), ("bot.CharacterClass?.Name", "class")]:
            self.assertFalse(reads(expr, key), expr)

    def test_fail_closed(self):
        """Names and text the shape rules above miss are still dropped unless they are reviewed world data."""
        unsafe = privacy.unreviewed_reads
        for expr, key in [("Name", "player"), ("Name", "bot"), ("characterName", "guest"), ("gm?.Name", "gm"),
                          ("MainOf(buffBot)?.Name", "main"), ("state?.MainName", "main"), ('askedBy?.Name ?? "a bot"', "asked_by"),
                          ("corpse.Name", "corpse"), ("(living?.TargetObject as GameObject)?.Name", "target"),
                          ("e.Message", "error"), ('line.Replace("a", "b")', "line"), ("System.Environment.StackTrace", "stack"),
                          ("otherName", "other")]:
            self.assertTrue(unsafe(expr, key), expr)
        for expr, key in [("taken.Keep.Name", "keep"), ("Name", "relic"), ("nearest.Name", "keep"), ("bot.ClassName", "class"),
                          ("_camp.MonsterName", "target"), ("GlobalConstants.RealmToName(bot.Realm)", "realm"),
                          ("RvrEngagementRuntime.GroupName(bot)", "group"), ("killer?.GetType().Name", "killerType"),
                          ("nameof(OFGreenKnight)", "encounter"), ('(faction ? "faction" : "say")', "channel"),
                          ("reply.Deal?.Round ?? 0", "round"), ("bot.Level", "level"), ("session.Recipe.ProductName", "recipe")]:
            self.assertFalse(unsafe(expr, key), expr)

    def test_dropped_fields_fail_closed(self):
        policy = privacy.load_policy()
        row = rows.Row("PLANTED_ROW", fields={"player": {"Name"}, "keep": {"keep.Name"}, "level": {"Level"},
                                              "guest": {"characterName"}, "path": {"created.Path"},
                                              "file": {"file"}, "note": set(), "count": set()})
        self.assertEqual(set(privacy.dropped_fields(row, policy)), {"player", "guest", "path", "file", "note"})


if __name__ == "__main__":
    unittest.main()
