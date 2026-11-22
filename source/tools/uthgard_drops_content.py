"""Adds Uthgard's period drop tables to a Living Server reference world, for a world update.

    python -I source/tools/uthgard_drops_content.py --pipeline-data <source/new-client/pipeline/data> --world <reference world copy>
        [--old-db <the new client's old DB: external/old_project/runtime/data/opendaoc.sqlite3.db>] [--rare round-up|skip]
        [--apply] [--report out.json]

Owner decision (2026-11-21): "import Uthgard's drop tables" into the server, so the original client (and the new client
in server mode) gets the period drops the new client's offline mode has (build_loot.py). The inputs are the new client's
pipeline data files, which are not in Git (they are made on the owner's PC):
  uthgard_drops.json    parse_uthgard_drops.py: per monster name, the item drops with the rates measured on Uthgard
  loot_item_rows.json   build_loot_items.py: ItemTemplate rows for Uthgard drops the old DB has no item for (uth_...)
  drop_item_rows.json   build_drop_items.py: ItemTemplate rows for the old DB's own loot rows that name a missing item

What it writes (only with --apply; without it, it only reports):
  - for each monster of the world (Mob, NpcTemplate; realm 0) that Uthgard has drops for and the world has NO loot for
    (no MobXLootTemplate / LootTemplate / MobDropTemplate / DropTemplateXItemTemplate row of that name, and no
    template-id loot on its spawns): one LootTemplate row per item and one MobXLootTemplate row;
  - an ItemTemplate row for each such drop the world lacks: from the old DB (--old-db) when it has the name, otherwise
    loot_item_rows.json;
  - an ItemTemplate row for each item the world's OWN loot rows (LootTemplate, LootOTD, DropTemplateXItemTemplate) name
    but lack, when drop_item_rows.json / loot_item_rows.json has that id (the "ItemTemplate: X is not found" rows).
What it never changes: any loot row the world has (real loot, owner rule), the random-item generator (ROG) and the coin
generator (LootGeneratorMoney; Uthgard's "bag of coins" rates are not imported), any existing ItemTemplate row.
Uthgard's "small chest" / "large chest" (its random-item containers) are left out, as in build_loot_items.py. Uthgard's
data has no one-time drops, so no LootOTD row is written.

Chance: the server rolls a LootTemplate row as "Chance >= d100" (LootList.GetLoot: Util.Random(1, 100)), so Chance is a
whole percent and 100 always drops. Uthgard's rate (percent of kills that dropped the item) is rounded half up and
capped at 100. A rate under 0.5% would round to 0, which never drops: --rare round-up (default) gives it 1%, --rare skip
leaves it out; the report counts both. The MobXLootTemplate row's DropCount is the monster's item count, so every item
that wins its roll drops, as each Uthgard rate was measured on its own (the new client's offline mode rolls the same way).

Names: the server keys loot by mob.Name.ToLower() (LootGeneratorTemplate), Uthgard's keys are lower case, so names match
without case. The rows use the world's own spelling of the name (LootGeneratorOneTimeDrop and the GM refresh read names
as written). A drop's item is matched by name as build_loot.py does: the monster's realm first (from its region), then
realm 0, then any.

Era: the pipeline keeps to 1.65; every ItemTemplate row is checked again here (no Trials of Atlantis or later bonus type,
as true165_prima_content.py checks), and a row naming a spell the world lacks is refused.

It refuses any world with saved progress (a row in one of progress-policy.json's progress tables; the release world's
starting guilds are world data): only a clean reference world is ever changed, never a played one.
"""
import argparse
import json
import math
import sqlite3
import sys
from collections import Counter, defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import true165_prima_content as t165  # noqa: E402  (the progress guard, column helpers and the era check)

ID_PREFIX = "uthdrop:"
# Uthgard's random-item containers (no ROG items from Uthgard; the server's own ROG stays) and coin entries
SKIP_ITEMS = {"small chest", "large chest", "some copper coins", "copper coin", "bag of coins"}
LOOT_TABLES = (("MobXLootTemplate", "MobName"), ("LootTemplate", "TemplateName"),
               ("MobDropTemplate", "MobName"), ("DropTemplateXItemTemplate", "TemplateName"))
REFERENCING_TABLES = (("LootTemplate", "TemplateName"), ("LootOTD", "MobName"), ("DropTemplateXItemTemplate", "TemplateName"))
SPELL_COLUMNS = ("SpellID", "SpellID1", "ProcSpellID", "ProcSpellID1", "PoisonSpellID")
REFERENCE_FILES = ("uthgard_drops.json", "loot_item_rows.json", "drop_item_rows.json")

Refused = t165.Refused


def chance_of(rate, rare="round-up"):
    """Uthgard's measured rate (percent) -> the server's whole-percent Chance, or None (not dropped)."""
    try:
        rate = float(rate)
    except (TypeError, ValueError):
        return None
    if not rate > 0:
        return None
    chance = min(100, int(math.floor(rate + 0.5)))
    if chance == 0:
        return 1 if rare == "round-up" else None
    return chance


def realm_of_region(region):
    """The realm a region belongs to (classic numbering; Darkness Falls and the rest: none)."""
    try:
        region = int(region or 0)
    except (TypeError, ValueError):
        return 0
    if region == 249:
        return 0
    return 1 if region < 100 else 2 if region < 200 else 3 if region < 300 else 0


def load_json(folder, name, required):
    path = Path(folder) / name
    if not path.is_file():
        if required:
            raise Refused(f"{path} is missing (made by the new client's pipeline on the owner's PC)")
        return None
    return json.loads(path.read_text(encoding="utf-8-sig"))


def rows_of(db, table):
    cur = db.execute(f'SELECT * FROM "{table}"')
    names = [d[0] for d in cur.description]
    return [dict(zip(names, r)) for r in cur]


class Run:
    def __init__(self, db, data, old_db=None, rare="round-up", apply=False):
        self.db, self.apply, self.rare = db, apply, rare
        self.uth = data["uthgard_drops"]
        self.loot_rows = {r["Id_nb"].lower(): r for r in data.get("loot_item_rows") or []}
        self.drop_rows = {r["Id_nb"].lower(): r for r in data.get("drop_item_rows") or []}
        self.loot_by_name = {}
        for r in data.get("loot_item_rows") or []:
            self.loot_by_name.setdefault((r.get("Name") or "").lower(), r)
        self.have = t165.tables(db)
        self.item_cols = t165.columns(db, "ItemTemplate")
        self.report = {"counts": Counter(), "warnings": [], "added": [], "skipped": [], "refused": [],
                       "items_missing": Counter()}
        self.noted_items = set()         # lower ids already in the report's added list
        self.pending_items = {}          # lower id -> (row, source)
        self.pending_loot = []           # (monster spelling, [(item id, chance)])
        self.refused_items = set()
        self.refused_names = set()       # lower names of refused rows (their drops are refused, not missing)
        self.world_items = {}            # lower id -> id
        self.world_by_name = defaultdict(list)
        for iid, name, realm in db.execute("SELECT Id_nb, Name, Realm FROM ItemTemplate" if "Realm" in self.item_cols
                                           else "SELECT Id_nb, Name, 0 FROM ItemTemplate"):
            self.world_items[(iid or "").lower()] = iid
            self.world_by_name[(name or "").lower()].append((iid, int(realm or 0)))
        self.spells = None
        if "Spell" in self.have and "SpellID" in t165.columns(db, "Spell"):
            self.spells = {r[0] for r in db.execute("SELECT SpellID FROM Spell")}
        self.old_by_name = defaultdict(list)
        if old_db is not None:
            for r in rows_of(old_db, "ItemTemplate"):
                self.old_by_name[(r.get("Name") or "").lower()].append(r)

    def note(self, kind, table, key, why, name=None):
        entry = {"table": table, "key": key, "why": why}
        if name:
            entry["name"] = name
        self.report[kind].append(entry)

    # --- the world -------------------------------------------------------------------------------------------------
    def monsters(self):
        """lower name -> {"name": the world's spelling, "realm": the realm most of its spawns stand in, "templates": ids}"""
        out = {}
        mob_cols = t165.columns(self.db, "Mob") if "Mob" in self.have else set()
        if mob_cols:
            realm_filter = " WHERE COALESCE(Realm, 0)=0" if "Realm" in mob_cols else ""
            tpl = "NPCTemplateID" if "NPCTemplateID" in mob_cols else "0"
            region = "Region" if "Region" in mob_cols else "0"
            spellings, realms, templates = defaultdict(Counter), defaultdict(Counter), defaultdict(set)
            for name, reg, tid in self.db.execute(f"SELECT Name, {region}, {tpl} FROM Mob{realm_filter}"):
                if not name:
                    continue
                key = name.lower()
                spellings[key][name] += 1
                realms[key][realm_of_region(reg)] += 1
                if tid not in (None, 0, -1, "0", "-1", ""):
                    templates[key].add(str(tid))
            for key in spellings:
                out[key] = {"name": spellings[key].most_common(1)[0][0], "realm": realms[key].most_common(1)[0][0],
                            "templates": templates[key]}
        if "NpcTemplate" in self.have:
            cols = t165.columns(self.db, "NpcTemplate")
            realm_filter = " WHERE COALESCE(Realm, 0)=0" if "Realm" in cols else ""
            for (name,) in self.db.execute(f"SELECT Name FROM NpcTemplate{realm_filter}"):
                if name and name.lower() not in out:
                    out[name.lower()] = {"name": name, "realm": 0, "templates": set()}
        return out

    def loot_names(self):
        names = {}
        for table, col in LOOT_TABLES:
            if table in self.have and col in t165.columns(self.db, table):
                idcol = f"{table}_ID"
                has_id = idcol in t165.columns(self.db, table)
                for name, rid in self.db.execute(f'SELECT "{col}", {idcol if has_id else "NULL"} FROM "{table}"'):
                    if name:
                        ours = bool(rid) and str(rid).startswith(ID_PREFIX)
                        prev = names.get(name.lower())
                        names[name.lower()] = ours if prev is None else (prev and ours)
        return names                     # lower name -> True when every row is this tool's own

    def check_generator(self):
        if "LootGenerator" not in self.have:
            self.report["warnings"].append("the world has no LootGenerator table: the server then uses LootGeneratorMoney "
                                           "only, and these rows would not drop")
            return
        rows = rows_of(self.db, "LootGenerator")
        tpl = [r for r in rows if str(r.get("LootGeneratorClass") or "").endswith("LootGeneratorTemplate")]
        if not tpl:
            self.report["warnings"].append("no LootGenerator row names DOL.GS.LootGeneratorTemplate: these rows would not drop")
        elif not any(not (r.get("MobName") or r.get("MobGuild") or r.get("MobFaction") or r.get("RegionID")) for r in tpl):
            self.report["warnings"].append("LootGeneratorTemplate is registered only for some monsters or regions: "
                                           "the rows drop only there")

    # --- items -------------------------------------------------------------------------------------------------------
    def era_problem(self, row):
        bad = t165.toa_bonus(row)
        if bad:
            return f"{bad[0]}={bad[1]} is a Trials of Atlantis or later bonus"
        if self.spells is not None:
            for col in SPELL_COLUMNS:
                sid = row.get(col)
                if sid not in (None, 0, "0", "") and int(sid) not in self.spells:
                    return f"{col}={sid}: the world has no such spell"
        return None

    def want_item(self, row, source):
        """Queues an ItemTemplate row the world lacks; returns its id, or None when it is refused."""
        iid = row["Id_nb"]
        key = iid.lower()
        if key in self.world_items:
            return self.world_items[key]
        if key in self.pending_items:
            return self.pending_items[key][0]["Id_nb"]
        if key in self.refused_items:
            return None
        why = self.era_problem(row)
        if why:
            self.refused_items.add(key)
            self.refused_names.add((row.get("Name") or "").lower())
            self.note("refused", "ItemTemplate", iid, why, row.get("Name"))
            return None
        self.pending_items[key] = (row, source)
        return iid

    def resolve(self, item_name, realm):
        """An Uthgard drop name -> an item id (queuing its row when the world lacks it), or None."""
        name = item_name.lower()

        def pick(rows, realm_of):
            for want in (realm, 0):
                for r in rows:
                    if realm_of(r) == want:
                        return r
            return rows[0] if rows else None
        hit = pick(self.world_by_name.get(name, []), lambda r: r[1])
        if hit:
            return hit[0]
        old = pick(self.old_by_name.get(name, []), lambda r: int(r.get("Realm") or 0))
        if old:
            return self.want_item(old, "old DB")
        if name in self.loot_by_name:
            return self.want_item(self.loot_by_name[name], "loot_item_rows.json")
        return None

    # --- the steps -----------------------------------------------------------------------------------------------
    def drops(self):
        counts = self.report["counts"]
        monsters, looted = self.monsters(), self.loot_names()
        template_loot = {n for n in looted if n.isdigit()}
        for key in sorted(self.uth):
            entry = self.uth[key]
            items = entry.get("items") or []
            if not items:
                continue
            counts["uthgard_monsters_with_items"] += 1
            mob = monsters.get(key.lower())
            if mob is None:
                counts["not_in_world"] += 1
                continue
            counts["matched"] += 1
            if key.lower() in looted:
                counts["skipped_has_loot"] += 1
                why = "already has this tool's Uthgard drops" if looted[key.lower()] else \
                    "the world already has loot for this name (kept: real loot)"
                self.note("skipped", "LootTemplate", mob["name"], why)
                continue
            if mob["templates"] & template_loot:
                counts["skipped_has_loot"] += 1
                self.note("skipped", "LootTemplate", mob["name"], "its spawns' NPC template has loot (the server uses that first)")
                continue
            chosen = {}
            for item_name, rate in items:
                if (item_name or "").lower() in SKIP_ITEMS:
                    counts["left_out_chests_and_coins"] += 1
                    continue
                chance = chance_of(rate, self.rare)
                if chance is None:
                    counts["left_out_rare"] += 1
                    continue
                iid = self.resolve(item_name, mob["realm"])
                if iid is None:
                    if (item_name or "").lower() in self.refused_names:
                        counts["drops_with_a_refused_item"] += 1
                    else:
                        self.report["items_missing"][item_name] += 1
                    continue
                if float(rate) < 0.5:
                    counts["chance_rounded_up_to_1"] += 1
                chosen[iid.lower()] = (iid, max(chance, chosen.get(iid.lower(), (iid, 0))[1]))
            if not chosen:
                counts["skipped_no_item"] += 1
                self.note("skipped", "LootTemplate", mob["name"], "none of its Uthgard drops has an item")
                continue
            self.pending_loot.append((mob["name"], sorted(chosen.values())))
            counts["monsters_added"] += 1
            counts["loot_rows_added"] += len(chosen)
            self.note("added", "LootTemplate", mob["name"],
                      f"{len(chosen)} Uthgard drops (killed {entry.get('killed', 0)} times in the bestiary)")

    def missing_references(self):
        """Items the world's own loot rows name but lack, when the pipeline built them (drop_item_rows / loot_item_rows)."""
        counts = self.report["counts"]
        seen = set()
        for table, name_col in REFERENCING_TABLES:
            if table not in self.have or "ItemTemplateID" not in t165.columns(self.db, table):
                continue
            for name, iid in self.db.execute(f'SELECT "{name_col}", ItemTemplateID FROM "{table}"'):
                key = (iid or "").lower()
                if not key or key in self.world_items or key in seen:
                    continue
                seen.add(key)
                row = self.drop_rows.get(key) or self.loot_rows.get(key)
                if row is None:
                    counts["world_references_still_missing"] += 1
                    continue
                if key in self.pending_items:
                    continue
                if self.want_item(dict(row, Id_nb=iid), "drop_item_rows.json" if key in self.drop_rows else "loot_item_rows.json"):
                    counts["world_references_fixed"] += 1
                    self.noted_items.add(key)
                    self.note("added", "ItemTemplate", iid, f"named by the world's {table} ({name}) but missing", row.get("Name"))

    def write(self):
        counts = self.report["counts"]
        for key, (row, source) in sorted(self.pending_items.items()):
            counts["items_added"] += 1
            counts[f"items_added_from_{source}"] += 1
            if key not in self.noted_items:
                self.note("added", "ItemTemplate", row["Id_nb"], f"a drop's item, from {source}", row.get("Name"))
            if self.apply:
                insert_filled(self.db, "ItemTemplate", row)
        if not self.apply:
            return
        for monster, items in self.pending_loot:
            low = monster.lower()
            for iid, chance in items:
                insert_filled(self.db, "LootTemplate", {
                    "LootTemplate_ID": f"{ID_PREFIX}{low}:{iid.lower()}", "TemplateName": monster,
                    "ItemTemplateID": iid, "Chance": chance, "Count": 1})
            insert_filled(self.db, "MobXLootTemplate", {
                "MobXLootTemplate_ID": f"{ID_PREFIX}{low}", "MobName": monster, "LootTemplateName": monster,
                "DropCount": len(items)})


def insert_filled(db, table, row):
    """Inserts the columns the table has; a NOT NULL column with no default that the row lacks gets '', 0 or (a date) 2000-01-01."""
    info = list(db.execute(f'PRAGMA table_info("{table}")'))
    full = {k: v for k, v in row.items() if k in {c[1] for c in info}}
    for _cid, name, ctype, notnull, default, pk in info:
        if name not in full and notnull and default is None and not pk:
            kind = (ctype or "").upper()
            full[name] = "2000-01-01 00:00:00" if "DATE" in kind else "" if any(t in kind for t in ("CHAR", "TEXT", "CLOB")) else 0
    t165.insert(db, table, set(full), full)


def run(pipeline_data, world, old_db=None, rare="round-up", apply=False):
    if rare not in ("round-up", "skip"):
        raise Refused(f"--rare is round-up or skip, not {rare}")
    if not Path(world).is_file():
        raise Refused(f"{world} is not a world database")
    data = {"uthgard_drops": load_json(pipeline_data, "uthgard_drops.json", True),
            "loot_item_rows": load_json(pipeline_data, "loot_item_rows.json", False),
            "drop_item_rows": load_json(pipeline_data, "drop_item_rows.json", False)}
    # read-write on an existing file only: a mistyped path never makes an empty database
    db = sqlite3.connect(f"file:{Path(world).resolve().as_posix()}?mode=rw", uri=True)
    old = None
    try:
        t165.check_clean(db)
        need = {"LootTemplate", "MobXLootTemplate", "ItemTemplate"}
        if not need <= t165.tables(db):
            raise Refused(f"the world lacks the loot tables {sorted(need - t165.tables(db))}")
        if old_db:
            if not Path(old_db).is_file():
                raise Refused(f"{old_db} is not a database")
            old = sqlite3.connect(f"file:{Path(old_db).resolve().as_posix()}?mode=ro", uri=True)
        r = Run(db, data, old, rare, apply)
        for name, value in (("loot_item_rows.json", data["loot_item_rows"]), ("drop_item_rows.json", data["drop_item_rows"])):
            if value is None:
                r.report["warnings"].append(f"{name} is missing: the drops it would give an item are left out")
        r.check_generator()
        r.drops()
        r.missing_references()
        r.write()
        if apply:
            db.commit()
        report = r.report
        report["counts"] = dict(sorted(report["counts"].items()))
        report["items_missing"] = dict(report["items_missing"].most_common())
        report["inputs"] = {name: data[name.split(".")[0]] is not None for name in REFERENCE_FILES}
        report["applied"] = bool(apply)
        return report
    finally:
        db.close()
        if old is not None:
            old.close()


def summary(report):
    c = report["counts"]
    return (("Applied" if report.get("applied") else "Would apply") +
            f": monsters matched {c.get('matched', 0)} (of {c.get('uthgard_monsters_with_items', 0)} Uthgard names with drops), "
            f"added {c.get('monsters_added', 0)} with {c.get('loot_rows_added', 0)} loot rows, "
            f"skipped {c.get('skipped_has_loot', 0)} with loot and {c.get('skipped_no_item', 0)} with no item, "
            f"items added {c.get('items_added', 0)}, item names missing {len(report['items_missing'])}, "
            f"world references fixed {c.get('world_references_fixed', 0)}, refused {len(report['refused'])}, "
            f"rare chances rounded up {c.get('chance_rounded_up_to_1', 0)}, left out as rare {c.get('left_out_rare', 0)}" +
            "".join(f"\nWarning: {w}" for w in report["warnings"]))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pipeline-data", required=True, help="the new client's pipeline/data folder (uthgard_drops.json, ...)")
    ap.add_argument("--world", required=True, help="a COPY of the release's clean reference world (sqlite)")
    ap.add_argument("--old-db", help="the new client's old DB (external/old_project/runtime/data/opendaoc.sqlite3.db), "
                                     "read only: item rows for drop names the world lacks")
    ap.add_argument("--rare", default="round-up", choices=("round-up", "skip"),
                    help="a rate under 0.5%%: give it 1%% (default) or leave it out")
    ap.add_argument("--apply", action="store_true", help="write the rows (default: report only)")
    ap.add_argument("--report", help="write the report here (JSON)")
    a = ap.parse_args()
    try:
        report = run(a.pipeline_data, a.world, a.old_db, a.rare, a.apply)
    except Refused as e:
        sys.exit(f"Refused: {e}")
    if a.report:
        Path(a.report).write_text(json.dumps(report, indent=1), encoding="utf-8")
    print(summary(report))


if __name__ == "__main__":
    main()
