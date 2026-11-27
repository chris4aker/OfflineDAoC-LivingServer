"""Compares the original client's crafting recipes with a Living Server world's (#580). Read only.

The client builds its crafting window from its own recipe file (tdl.crf, inside ifd.mpk) and sends the server only
a recipe id. A recipe the window shows that the world lacks, or hides, "does nothing" (now: "That recipe cannot be
made on this server.", UT_CraftRequestReplies). This script lists those recipes, and the ones whose name, craft,
level or realm differ, so a later world update can fix the real gaps (docs/research/CLIENT-RECIPE-CHECK-580.md).

    python -I source/tools/client_recipe_check.py --client crafts.json --world <copy>/opendaoc.sqlite3.db
        [--crf <copy of tdl.crf> | --mpk <copy of ifd.mpk>] [--client-build "1.130 (game.dll sha256 ...)"]
        [--level-field skill|level|material_level] [--report out.json] [--markdown out.md] [--limit 50]

--client is the output of atom0s/craft_extract (AGPL-3.0), run by hand as a separate program on a copy of the
client's tdl.crf: `craft_extract --file tdl.crf --out crafts.json --mode 2` (mode 1 CSV and mode 3 SQLite read too).
None of its code is in this repository and this script does not run it; it only reads the file it wrote, which is
client data and stays out of Git. JSON or SQLite is the safer choice: craft_extract writes CSV fields unquoted, so a
name with a comma is read back here by position (see read_client_csv).

Nothing is written to any input: the world (and a craft_extract SQLite file) are opened with mode=ro, --crf and --mpk
are only hashed and their version read, and the full --report (client data) is refused inside this repository. The --markdown
report holds recipe ids, names, crafts, levels and counts only, for docs/research/.

Matching follows the server: a client recipe id is a CraftedItem.CraftedItemID (MakeProductHandler reads a ushort and
RecipeDB.FindBy looks it up), names are compared without case, and a world recipe the server hides is counted as
hidden by the M0 rule of export_recipe_fixture.py (one rule, shared).
"""
import argparse
import collections
import hashlib
import json
import re
import sqlite3
import struct
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
import export_recipe_fixture as erf  # noqa: E402

REALMS = ("Albion", "Midgard", "Hibernia")  # craft_extract's realm index 0-2; ItemTemplate.Realm is 1-3 (0: any)
CRF_VERSIONS = {0x66: "v66", 0x67: "v67"}  # the tdl.crf header versions craft_extract reads
LEVEL_FIELDS = ("skill", "level", "material_level")

# Client profession names -> eCraftingSkill (CraftingMgr.cs). Keys are lower-case letters only; a name not here is
# reported as unknown rather than guessed.
PROFESSIONS = {
    "weaponcraft": 1, "weaponcrafting": 1, "weaponsmith": 1, "weaponsmithing": 1,
    "armorcraft": 2, "armorcrafting": 2, "armoursmith": 2, "armorsmith": 2, "armorsmithing": 2,
    "siegecraft": 3, "siegecrafting": 3,
    "alchemy": 4,
    "metalworking": 6, "metalwork": 6, "smithing": 6,
    "leatherworking": 7, "leathercraft": 7, "leathercrafting": 7, "leatherwork": 7,
    "clothworking": 8, "clothwork": 8,
    "gemcutting": 9, "gemcraft": 9,
    "herbalcraft": 10, "herbalcrafting": 10, "herbcraft": 10, "herbcrafting": 10, "herbalism": 10,
    "tailoring": 11,
    "fletching": 12, "bowcraft": 12, "bowcrafting": 12,
    "spellcraft": 13, "spellcrafting": 13,
    "woodworking": 14, "woodwork": 14,
    "basiccrafting": 15, "basiccraft": 15,
}


def norm(text):
    return " ".join((text or "").lower().split())


def profession_skill(name):
    return PROFESSIONS.get(re.sub(r"[^a-z]", "", (name or "").lower()))


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def crf_info(path):
    """The tdl.crf's sha256, size and header version (its first little-endian uint32). Read only."""
    with open(path, "rb") as f:
        head = f.read(4)
    version = struct.unpack("<I", head)[0] if len(head) == 4 else None
    return {"file": Path(path).name, "sha256": sha256(path), "bytes": Path(path).stat().st_size,
            "version": CRF_VERSIONS.get(version, f"unknown ({version})")}


def mpk_crf_info(path, entry="tdl.crf"):
    """The same for the tdl.crf inside a copy of ifd.mpk, read in memory with our own MPAK reader
    (source/new-client/pipeline/daoc_mpk.py), plus the archive's own sha256. Nothing is written."""
    sys.path.insert(0, str(REPO / "source" / "new-client" / "pipeline"))
    from daoc_mpk import MpakArchive
    data = MpakArchive(str(path)).read(entry)
    version = struct.unpack("<I", data[:4])[0] if len(data) >= 4 else None
    return {"file": f"{Path(path).name}:{entry}", "sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data),
            "version": CRF_VERSIONS.get(version, f"unknown ({version})"), "archive_sha256": sha256(path)}


def as_int(value):
    try:
        return int(str(value).strip())
    except (TypeError, ValueError):
        return None


def full_name(base_name, name):
    """The name the crafting window shows: the base material, then the recipe's own name ("bronze" + "short sword")."""
    return norm(f"{base_name or ''} {name or ''}")


def recipe(realm, rid, profession, category, name, base_name, icon, level, material_level, skill, materials):
    return {"realm": realm, "id": rid, "profession": (profession or "").strip(), "category": (category or "").strip(),
            "name": (name or "").strip(), "base_material_name": (base_name or "").strip(), "icon": icon,
            "level": level, "material_level": material_level, "skill": skill, "materials": materials}


# --- craft_extract output readers (formats as its save_csv, save_json and save_sqlite write them) ---

def _is_int(token):
    return as_int(token) is not None


def read_client_csv(text):
    """Mode 1. One row per recipe, unquoted: id, realm, realm_name, profession, category, name, base_material,
    base_material_name, icon, level, material_level, skill, then (base_material, base_material_name, count, name) per
    material. A name may hold a comma, so the recipe name runs to the first position followed by int, text, then four
    ints; a material's name runs to the next int, text, int that leaves room for a whole material. Rows that still
    do not fit are returned as unparsed."""
    out, unparsed = [], []
    for number, line in enumerate(text.splitlines(), 1):
        if not line.strip():
            continue
        t = line.split(",")
        if number == 1 and t[0].strip().lower() == "id":
            continue
        if len(t) < 12 or not (_is_int(t[0]) and _is_int(t[1])):
            unparsed.append(number)
            continue
        k = next((k for k in range(6, len(t) - 5)
                  if _is_int(t[k]) and all(_is_int(x) for x in t[k + 2:k + 6])), None)
        if k is None:
            unparsed.append(number)
            continue
        rest, materials, ok = t[k + 6:], [], True
        while rest:
            if len(rest) < 4 or not (_is_int(rest[0]) and _is_int(rest[2])):
                ok = False
                break
            end = 4
            while end < len(rest) and not (len(rest) - end >= 4 and _is_int(rest[end]) and _is_int(rest[end + 2])):
                end += 1
            materials.append((full_name(rest[1], ",".join(rest[3:end])), as_int(rest[2])))
            rest = rest[end:]
        if not ok:
            unparsed.append(number)
            continue
        out.append(recipe(as_int(t[1]), as_int(t[0]), t[3], t[4], ",".join(t[5:k]), t[k + 1], as_int(t[k + 2]),
                          as_int(t[k + 3]), as_int(t[k + 4]), as_int(t[k + 5]), materials))
    return out, unparsed


def read_client_json(data):
    """Mode 2: {"Albion": [recipe, ...], "Midgard": [...], "Hibernia": [...]}."""
    out = []
    for realm_name, items in (data or {}).items():
        if realm_name not in REALMS:
            continue
        for r in items or []:
            materials = [(full_name(m.get("base_material_name"), m.get("name")), as_int(m.get("count")))
                         for m in r.get("materials") or []]
            out.append(recipe(REALMS.index(realm_name), as_int(r.get("id")), r.get("profession"), r.get("category"),
                              r.get("name"), r.get("base_material_name"), as_int(r.get("icon")), as_int(r.get("level")),
                              as_int(r.get("material_level")), as_int(r.get("skill")), materials))
    return out


def read_client_sqlite(path):
    """Mode 3: tables recipes, recipes_materials and base_materials. recipes_materials has no realm, so a recipe id
    used in two realms gets no materials (unknown) rather than both lists. A recipe listed in two categories of one
    realm is written twice with its materials twice; it reads as one list."""
    db = sqlite3.connect(f"{Path(path).resolve().as_uri()}?mode=ro", uri=True)
    try:
        bases = {r[0]: r[1] for r in db.execute("SELECT id, name FROM base_materials")}
        listed = collections.Counter(r[0] for r in db.execute("SELECT id FROM recipes"))
        realms_of = collections.Counter(r[0] for r in db.execute("SELECT DISTINCT id, realm_id FROM recipes"))
        mats = collections.defaultdict(list)
        for rid, base, count, name in db.execute("SELECT recipe_id, base_material, count, name FROM recipes_materials"):
            mats[rid].append((full_name(bases.get(base, "") if base else "", name), as_int(count)))
        for rid, items in mats.items():
            n = listed[rid]
            if n > 1 and len(items) % n == 0 and items == items[:len(items) // n] * n:
                mats[rid] = items[:len(items) // n]
        out = []
        for row in db.execute("SELECT id, realm_id, profession, category, name, base_material, icon, level, "
                              "material_level, skill FROM recipes"):
            rid, realm, prof, cat, name, base, icon, level, mlevel, skill = row
            out.append(recipe(as_int(realm), as_int(rid), prof, cat, name, bases.get(base, "") if base else "",
                              as_int(icon), as_int(level), as_int(mlevel), as_int(skill),
                              mats.get(rid, []) if realms_of[rid] == 1 else None))
        return out
    finally:
        db.close()


def read_client(path):
    """craft_extract's output, by content: SQLite, JSON or CSV. Returns (recipes, unparsed CSV line numbers, format)."""
    path = Path(path)
    with open(path, "rb") as f:
        head = f.read(16)
    if head.startswith(b"SQLite format 3\x00"):
        return read_client_sqlite(path), [], "sqlite"
    text = path.read_text(encoding="utf-8-sig", errors="replace")
    if text.lstrip().startswith("{"):
        return read_client_json(json.loads(text)), [], "json"
    recipes, unparsed = read_client_csv(text)
    return recipes, unparsed, "csv"


# --- the world ---

def read_world(path):
    """The world's recipes as export_recipe_fixture builds them (same tables, same hidden rule), by recipe id."""
    erf.check_tables()
    db = erf.open_read_only(path)
    try:
        fixture = erf.build_fixture(db)
    finally:
        db.close()
    names = {key: m["name"] for key, m in fixture["materials"].items()}
    world = {}
    for r in fixture["recipes"]:
        r = dict(r)
        r["material_names"] = sorted((norm(names.get(key, key)), count) for key, count in r["materials"])
        world[r["id"]] = r
    return world


# --- the diff ---

def craft_name(skill):
    return erf.SKILLS.get(skill, f"skill {skill}") if skill is not None else "unknown"


def diff(client, world, level_field="skill"):
    """Every client recipe against the world row of the same id. Differences per recipe (a recipe may have several):
    missing (no row: the server says it cannot be made), hidden (a row the server hides, with the M0 reason), and for a
    usable row name, craft, level and realm; materials is counted apart (names can differ in wording only)."""
    seen, duplicates, unknown_prof = {}, 0, collections.Counter()
    for r in client:
        if r["id"] is None:
            continue
        if (r["realm"], r["id"]) in seen:
            duplicates += 1
            continue
        seen[(r["realm"], r["id"])] = r
        if profession_skill(r["profession"]) is None:
            unknown_prof[r["profession"]] += 1

    by_name = collections.defaultdict(list)
    for w in world.values():
        by_name[(norm(w.get("name")), w["skill"])].append(w["id"])

    rows = collections.defaultdict(list)
    counts = collections.Counter()
    by_craft = collections.defaultdict(collections.Counter)
    calibration = collections.Counter()
    materials_differ = 0
    client_ids = set()
    for (realm, rid), r in sorted(seen.items()):
        client_ids.add(rid)
        skill = profession_skill(r["profession"])
        craft = craft_name(skill) if skill else r["profession"] or "unknown"
        names = {norm(r["name"]), full_name(r["base_material_name"], r["name"])}
        line = {"id": rid, "realm": REALMS[realm] if realm in range(3) else realm, "craft": craft,
                "client_name": full_name(r["base_material_name"], r["name"]), "client_level": r.get(level_field)}
        w = world.get(rid)
        if w is None:
            line["same_name_elsewhere"] = sorted(i for n in names for i in by_name.get((n, skill), []))
            found = ["missing"]
        elif w.get("hidden"):
            line.update(world_name=w.get("name", ""), hidden=w["hidden"])
            found = ["hidden"]
        else:
            found = []
            line.update(world_name=w.get("name", ""), world_craft=craft_name(w["skill"]), world_level=w["level"])
            for field in LEVEL_FIELDS:
                if r.get(field) is not None and r[field] == w["level"]:
                    calibration[field] += 1
            if norm(w.get("name")) not in names:
                found.append("name")
            if skill is not None and skill != w["skill"]:
                found.append("craft")
            if r.get(level_field) is not None and r[level_field] != w["level"]:
                found.append("level")
            if realm in range(3) and w.get("realm") not in (0, None, realm + 1):
                line["world_realm"] = w.get("realm")
                found.append("realm")
            if r["materials"] is not None and sorted(r["materials"]) != w["material_names"]:
                materials_differ += 1
        for kind in found or ["same"]:
            counts[kind] += 1
            by_craft[craft][kind] += 1
            if kind != "same":
                rows[kind].append(line)

    world_only = [{"id": w["id"], "craft": craft_name(w["skill"]), "world_name": w.get("name", ""),
                   "world_level": w["level"], **({"hidden": w["hidden"]} if w.get("hidden") else {})}
                  for i, w in sorted(world.items()) if i not in client_ids]
    world_only_by_craft = collections.Counter(x["craft"] for x in world_only)
    matched = sum(1 for (_, rid) in seen if rid in world and not world[rid].get("hidden"))
    return {
        "client": {"recipes": len(seen), "duplicates": duplicates,
                   "by_realm": dict(collections.Counter(REALMS[r] if r in range(3) else str(r) for r, _ in seen)),
                   "unknown_professions": dict(unknown_prof)},
        "world": {"recipes": len(world), "hidden": sum(1 for w in world.values() if w.get("hidden"))},
        "level_field": level_field,
        "level_calibration": {"usable_matches": matched, **{f: calibration[f] for f in LEVEL_FIELDS}},
        "counts": dict(counts),
        "by_craft": {k: dict(v) for k, v in sorted(by_craft.items())},
        "materials_differ": materials_differ,
        "world_only": {"recipes": len(world_only), "by_craft": dict(sorted(world_only_by_craft.items()))},
        "rows": {k: v for k, v in sorted(rows.items())},
        "world_only_rows": world_only,
    }


# --- reports ---

KINDS = ("same", "missing", "hidden", "name", "craft", "level", "realm")


def summary(report):
    c = report["counts"]
    cal = report["level_calibration"]
    lines = [f"client recipes {report['client']['recipes']} ({report['client']['duplicates']} duplicate rows), "
             f"world recipes {report['world']['recipes']} ({report['world']['hidden']} hidden)",
             "client recipes: " + ", ".join(f"{k} {c.get(k, 0)}" for k in KINDS),
             f"materials differ on {report['materials_differ']} usable matches",
             f"world only: {report['world_only']['recipes']}",
             f"level field {report['level_field']}; of {cal['usable_matches']} usable id matches the world level equals "
             + ", ".join(f"client {f} {cal[f]}" for f in LEVEL_FIELDS)]
    if report["client"]["unknown_professions"]:
        lines.append("unknown client professions: " + ", ".join(sorted(report["client"]["unknown_professions"])))
    if report.get("unparsed_csv_lines"):
        lines.append(f"CSV lines not read: {len(report['unparsed_csv_lines'])}")
    return "\n".join(lines)


def markdown(report, limit=50):
    """Names and counts for docs/research/ (no other client data)."""
    src = report.get("source", {})
    out = ["# Client recipe check (#580)", "",
           f"- Client build: {src.get('client_build') or 'not given'}",
           f"- Recipe file: {src.get('crf') or 'not given'}",
           f"- craft_extract output: {src.get('client_format', '?')}, sha256 `{src.get('client_sha256', '?')}`",
           f"- World: sha256 `{src.get('world_sha256', '?')}`",
           f"- Level compared: client `{report['level_field']}` against `CraftedItem.CraftingLevel`", "",
           "```", summary(report), "```", "", "## By craft", "",
           "| Craft | " + " | ".join(KINDS) + " | world only |", "|---" * (len(KINDS) + 2) + "|"]
    for craft, kinds in report["by_craft"].items():
        out.append(f"| {craft} | " + " | ".join(str(kinds.get(k, 0)) for k in KINDS)
                   + f" | {report['world_only']['by_craft'].get(craft, 0)} |")
    for kind, rows in report["rows"].items():
        out += ["", f"## {kind} ({len(rows)})", ""]
        for r in rows[:limit]:
            extra = ""
            if kind == "missing" and r.get("same_name_elsewhere"):
                extra = f"; same name and craft in the world as {', '.join(map(str, r['same_name_elsewhere']))}"
            elif kind == "hidden":
                extra = f"; hidden: {r['hidden']}"
            elif kind == "name":
                extra = f"; world: {r['world_name']}"
            elif kind == "craft":
                extra = f"; world: {r['world_craft']}"
            elif kind == "level":
                extra = f"; client {r['client_level']}, world {r['world_level']}"
            elif kind == "realm":
                extra = f"; world realm {r['world_realm']}"
            out.append(f"- {r['id']} {r['realm']} {r['craft']}: {r['client_name']}{extra}")
        if len(rows) > limit:
            out.append(f"- ... and {len(rows) - limit} more (full list in the --report JSON, kept out of Git)")
    return "\n".join(out) + "\n"


def inside_repo(path):
    try:
        Path(path).resolve().relative_to(REPO)
        return True
    except ValueError:
        return False


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--client", required=True, help="craft_extract's output (CSV, JSON or SQLite)")
    parser.add_argument("--world", required=True, help="a copy of a world database (opened read-only)")
    parser.add_argument("--crf", help="the tdl.crf the output came from (hashed and its version read only)")
    parser.add_argument("--mpk", help="or a copy of ifd.mpk: its tdl.crf is read in memory, hashed and its version read")
    parser.add_argument("--client-build", help="the client build, as it should read in the report")
    parser.add_argument("--level-field", choices=LEVEL_FIELDS, default="skill",
                        help="the client field compared with CraftingLevel (the calibration line shows which fits)")
    parser.add_argument("--report", help="the full report as JSON (client data: refused inside this repository)")
    parser.add_argument("--markdown", help="the names-and-counts report for docs/research/")
    parser.add_argument("--limit", type=int, default=50, help="rows listed per difference in --markdown")
    args = parser.parse_args(argv)
    if args.report and inside_repo(args.report):
        parser.error("--report holds client data; write it outside the repository")

    client, unparsed, fmt = read_client(args.client)
    report = diff(client, read_world(args.world), args.level_field)
    report["unparsed_csv_lines"] = unparsed
    report["source"] = {"client_build": args.client_build, "client_format": fmt,
                        "client_sha256": sha256(args.client), "world_sha256": sha256(args.world)}
    if args.crf or args.mpk:
        info = crf_info(args.crf) if args.crf else mpk_crf_info(args.mpk)
        report["source"]["crf"] = f"{info['file']} {info['version']}, {info['bytes']} bytes, sha256 `{info['sha256']}`"
        if info.get("archive_sha256"):
            report["source"]["crf"] += f" (archive sha256 `{info['archive_sha256']}`)"
    if args.report:
        Path(args.report).write_text(json.dumps(report, indent=1), encoding="utf-8")
    if args.markdown:
        Path(args.markdown).write_text(markdown(report, args.limit), encoding="utf-8")
    print(summary(report))
    return 0


if __name__ == "__main__":
    sys.exit(main())
