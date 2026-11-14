"""Adds Offline DAoC True 1.65's Prima content to a Living Server reference world, for a world update.

    python -I source/tools/true165_prima_content.py --true165 <True 1.65 runtime/true165> --world <reference world copy>
        [--sets spawns,encounters,...] [--with-corrections] [--apply] [--report out.json]

Living Server packages never change world rows. A world change reaches players only through a release's world update:
the reference world of the release (built by build_release_037.py from a clean install) is diffed against the released
one by make_patch.py, and the patcher applies the difference once. This script prepares that reference world: it adds
the rows of shadowofze's True 1.65 Prima batch (v0.4-v0.7, GPL-3.0) that the world lacks. Without --apply it only
reports what it would add.

It refuses any world with saved progress (a row in one of the progress tables of
OfflineDaoc.ProgressImport/progress-policy.json): only a clean reference world is ever changed, never a played one.

What it adds, set by set (all by default except corrections):
  templates   NpcTemplate rows of the Prima monsters and encounter adds (not when the TemplateId is taken)
  items       ItemTemplate rows of the Prima drops and quest rewards (period item pages; no ToA-or-later bonus)
  paths       the Unseelie Envoy's convoy route (Path, PathPoints)
  spawns      Mob rows of the Prima monsters the world lacks (not when the same monster stands within 500 in the region)
  encounters  the encounter spawns, controllers and the Celain Eirich row, and the 12 world updates that name an
              encounter class, brain or template on an existing spawn (Talvor, Cuinn, Brungar, Zargin, the Unseelie Envoy
              and his stallions). Some of these also set the encounter's own level, speed, model and place (the Envoy
              at level 55, speed 180; his stallions at 48), as True 1.65's encounters have them. Needs the templates
              set (the updates name its TemplateIds).
  quests      DataQuest rows of the Prima quests (not when the quest id or name is taken)
  drops       drops165.json loot for the monsters this run added, when the world has no loot for that name
  corrections (only with --with-corrections) True 1.65's Prima changes to existing spawns' levels, aggression and speed.
              These change monster difficulty in an existing world, so they are the owner's call.

Every row keeps True 1.65's values; only columns the world's table has are written. A row whose key is already in the
world is left alone. The report lists every added, skipped and updated row with the reason.
"""
import argparse
import json
import math
import re
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
POLICY = HERE / "OfflineDaoc.ProgressImport" / "progress-policy.json"

SETS = ("templates", "items", "paths", "spawns", "encounters", "quests", "drops")
FILES = {
    "templates": ["add-primatemplates165.json", "add-primaenctemplates165.json"],
    "items": ["add-primaitems165.json", "add-primaquestitems165.json"],
    "paths": ["add-primapaths165.json", "add-primapathpoints165.json"],
    "spawns": ["add-primaspawns165.json", "add-cornwallprima165.json"],
    "encounters": ["add-primaencounters165.json"],
    "quests": ["add-primaquests165.json"],
}
# A monster of the same name this close in the same region is the world's own copy of a spawn True 1.65 restored.
SAME_SPAWN_RADIUS = 500
# eProperty types that are Trials of Atlantis or later, as True 1.65 defines them
# (tools/claude-version/true165/audit_fix_2026-10-31/stats/fix_stats.py): 99-119 (Catacombs class lines, unified
# Archery, ...) and 145-256 (cap bonuses, PowerPool %, Acuity, All-skills, speed and ToA bonuses, mythical, ...).
# True 1.65 keeps 156 and 163-165 only on items whose own period page lists them; its Prima files are those items.
LATER_BONUS_TYPES = set(range(99, 120)) | set(range(145, 257))
PERIOD_ATTESTED_BONUS_TYPES = {156, 163, 164, 165}


class Refused(Exception):
    pass


def progress_tables():
    try:
        return json.loads(POLICY.read_text(encoding="utf-8-sig"))["ProgressTables"]
    except (OSError, KeyError, ValueError):
        # the policy is the source of truth; these are the ones that always hold a played world's saves
        return ["Account", "DOLCharacters"]


def tables(db):
    return {r[0] for r in db.execute("SELECT name FROM sqlite_master WHERE type='table'")}


def columns(db, table):
    return {r[1] for r in db.execute(f'PRAGMA table_info("{table}")')}


def check_clean(db):
    have = tables(db)
    for table in progress_tables():
        if table in have and db.execute(f'SELECT 1 FROM "{table}" LIMIT 1').fetchone():
            raise Refused(f"the world has saved progress ({table} has rows): only a clean reference world may be changed")


def load(folder, name):
    path = Path(folder) / name
    return json.loads(path.read_text(encoding="utf-8-sig"))


def toa_bonus(item):
    for key, value in item.items():
        if re.fullmatch(r"(Bonus\d*|Extra)?BonusType|Bonus\d+Type", key) and isinstance(value, int) and \
                value in LATER_BONUS_TYPES and value not in PERIOD_ATTESTED_BONUS_TYPES:
            return key, value
    return None


def is_encounter_row(row):
    text = f"{row.get('ClassType') or ''} {row.get('Brain') or ''}"
    return bool(re.search(r"\b\w+165\b", text)) or "CelainEirich" in text


def insert(db, table, cols, row):
    names = [k for k in row if k in cols]
    values = [(1 if v is True else 0 if v is False else v) for v in (row[k] for k in names)]
    db.execute(f'INSERT INTO "{table}" ({",".join(chr(34) + n + chr(34) for n in names)}) '
               f'VALUES ({",".join("?" * len(names))})', values)


class Run:
    def __init__(self, db, true165, apply):
        self.db, self.true165, self.apply = db, Path(true165), apply
        self.report = {"added": [], "skipped": [], "updated": [], "refused": []}
        self.added_monsters = set()

    def note(self, kind, table, key, why, name=None):
        entry = {"table": table, "key": key, "why": why}
        if name:
            entry["name"] = name
        self.report[kind].append(entry)

    def has_key(self, table, key_col, key):
        return self.db.execute(f'SELECT 1 FROM "{table}" WHERE "{key_col}"=?', (key,)).fetchone() is not None

    def same_spawn_near(self, row):
        name, region = row.get("Name") or "", row.get("Region")
        for x, y in self.db.execute('SELECT X, Y FROM Mob WHERE lower(Name)=lower(?) AND Region=?', (name, region)):
            if math.hypot((x or 0) - (row.get("X") or 0), (y or 0) - (row.get("Y") or 0)) <= SAME_SPAWN_RADIUS:
                return True
        return False

    def add_file(self, name, row_filter=None):
        doc = load(self.true165, name)
        table, key_col = doc["table"], doc["key"]
        if table not in tables(self.db):
            self.note("refused", table, name, "the world has no such table")
            return
        cols = columns(self.db, table)
        for row in doc["rows"]:
            if row_filter and not row_filter(row):
                continue
            key = str(row.get(key_col))
            label = row.get("Name") or row.get("Id_nb")
            if self.has_key(table, key_col, key):
                self.note("skipped", table, key, "key already in the world", label)
                continue
            if table == "NpcTemplate" and "TemplateId" in cols and \
                    self.db.execute("SELECT 1 FROM NpcTemplate WHERE TemplateId=?", (row.get("TemplateId"),)).fetchone():
                self.note("skipped", table, key, f"TemplateId {row.get('TemplateId')} is taken", label)
                continue
            if table == "ItemTemplate" and (bad := toa_bonus(row)):
                self.note("refused", table, key, f"{bad[0]}={bad[1]} is a Trials of Atlantis or later bonus", label)
                continue
            if table == "Mob" and self.same_spawn_near(row):
                self.note("skipped", table, key, f"the same monster stands within {SAME_SPAWN_RADIUS} in region {row.get('Region')}", label)
                continue
            if table == "DataQuest" and "Name" in cols and \
                    self.db.execute("SELECT 1 FROM DataQuest WHERE lower(Name)=lower(?)", (row.get("Name") or "",)).fetchone():
                self.note("skipped", table, key, "a quest of that name is in the world", label)
                continue
            if self.apply:
                insert(self.db, table, cols, row)
            if table == "Mob":
                self.added_monsters.add((row.get("Name") or "").lower())
            self.note("added", table, key, doc["reason"][:120], label)

    def rule_updates(self, predicate):
        rules = load(self.true165, "true165-rules.json")
        for update in rules.get("updates", []):
            sql, reason = update.get("sql", ""), update.get("reason", "")
            if not predicate(sql, reason):
                continue
            m = re.match(r"\s*UPDATE\s+(\w+)\s+SET\s+(.*?)\s+WHERE\s+(\w+)\s*(=|IN)\s*(.+)$", sql, re.S | re.I)
            if not m or m.group(1) != "Mob" or m.group(3) != "Mob_ID":
                self.note("refused", "Mob", sql[:80], "not a plain update of Mob rows by Mob_ID")
                continue
            ids = re.findall(r"'([^']*)'", m.group(5))
            present = [i for i in ids if self.has_key("Mob", "Mob_ID", i)]
            if not present:
                self.note("skipped", "Mob", ",".join(ids)[:80], "none of its spawns are in the world")
                continue
            cols = columns(self.db, "Mob")
            names = re.findall(r"(\w+)\s*=", m.group(2))
            if any(n not in cols for n in names):
                self.note("refused", "Mob", ",".join(ids)[:80], f"sets a column the world lacks ({names})")
                continue
            if self.apply:
                marks = ",".join("?" * len(present))
                self.db.execute(f"UPDATE Mob SET {m.group(2)} WHERE Mob_ID IN ({marks})", present)
            self.note("updated", "Mob", ",".join(present)[:80], reason[:120])

    def drops(self):
        doc = load(self.true165, "drops165.json")
        if not {"LootTemplate", "MobXLootTemplate"} <= tables(self.db):
            self.note("refused", "LootTemplate", "drops165.json", "the world has no loot tables")
            return
        for monster, entry in doc["monsters"].items():
            if monster.lower() not in self.added_monsters:
                continue
            if self.db.execute("SELECT 1 FROM MobXLootTemplate WHERE lower(MobName)=lower(?)", (monster,)).fetchone() or \
                    self.db.execute("SELECT 1 FROM LootTemplate WHERE lower(TemplateName)=lower(?)", (monster,)).fetchone():
                self.note("skipped", "LootTemplate", monster, "the world already has loot for this name")
                continue
            items = [(i, c) for i, c in entry["items"]
                     if self.db.execute("SELECT 1 FROM ItemTemplate WHERE Id_nb=?", (i,)).fetchone()]
            if not items:
                self.note("skipped", "LootTemplate", monster, "none of its drop items are in the world")
                continue
            if self.apply:
                for item, chance in items:
                    self.db.execute("INSERT INTO LootTemplate (TemplateName, ItemTemplateID, Chance, Count, LootTemplate_ID) "
                                    "VALUES (?, ?, ?, 1, ?)", (monster, item, chance, f"p165drop:{monster}:{item}"))
                self.db.execute("INSERT INTO MobXLootTemplate (MobName, LootTemplateName, DropCount, MobXLootTemplate_ID) "
                                "VALUES (?, ?, ?, ?)", (monster, monster, entry["drop_count"], f"p165drop:{monster}"))
            self.note("added", "LootTemplate", monster, f"{len(items)} period drops ({doc['source'][:60]})")


def run(true165, world, sets=SETS, corrections=False, apply=False):
    if "encounters" in sets and "templates" not in sets:
        raise Refused("the encounters set names the templates set's TemplateIds: run them together")
    if not Path(world).is_file():
        raise Refused(f"{world} is not a world database")
    # read-write on an existing file only: a mistyped path never makes an empty database
    db = sqlite3.connect(f"file:{Path(world).resolve().as_posix()}?mode=rw", uri=True)
    try:
        check_clean(db)
        r = Run(db, true165, apply)
        for name in sets:
            if name in FILES:
                for f in FILES[name]:
                    r.add_file(f)
            if name == "encounters":
                r.add_file("add-restoredspawns165.json", is_encounter_row)
                r.rule_updates(lambda sql, reason: "Prima encounter" in reason and sql.lstrip().upper().startswith("UPDATE MOB"))
            if name == "drops":
                r.drops()
        if corrections:
            r.rule_updates(lambda sql, reason: reason.startswith("True 1.65 Prima monster:"))
        if apply:
            db.commit()
        return r.report
    finally:
        db.close()


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--true165", required=True, help="True 1.65's runtime/true165 folder")
    ap.add_argument("--world", required=True, help="a COPY of the release's clean reference world (sqlite)")
    ap.add_argument("--sets", default=",".join(SETS))
    ap.add_argument("--with-corrections", action="store_true")
    ap.add_argument("--apply", action="store_true", help="write the rows (default: report only)")
    ap.add_argument("--report", help="write the report here (JSON)")
    a = ap.parse_args()
    sets = [s.strip() for s in a.sets.split(",") if s.strip()]
    unknown = [s for s in sets if s not in SETS]
    if unknown:
        ap.error(f"unknown sets: {unknown}")
    try:
        report = run(a.true165, a.world, sets, a.with_corrections, a.apply)
    except Refused as e:
        sys.exit(f"Refused: {e}")
    if a.report:
        Path(a.report).write_text(json.dumps(report, indent=1), encoding="utf-8")
    print(("Applied" if a.apply else "Would apply") + ": " +
          ", ".join(f"{k} {len(v)}" for k, v in report.items()))


if __name__ == "__main__":
    main()
