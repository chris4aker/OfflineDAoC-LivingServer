"""Dullahan's Bulwark update for the Sluaghbinder (0.34b).

  preview : python -B install_bulwark_update.py            (default, changes nothing)
  install : python -B install_bulwark_update.py install
  rollback: python -B install_bulwark_update.py rollback <backup-folder>

1. Chat text: the Cairn armor buffs say "unholy aura" instead of "holy aura",
   and the Cairn strength buffs say "filled with the power of the cairn"
   instead of the strength of Thor / the gods.
2. The Dullahan's Bulwark taunts cost power (2/6/10/14/18 by rank) and share
   one 15-second recast instead of a free 4-second one.
3. Cairn Ward (59026, Bulwark 20), an armor buff the core Cairn Oath line
   already outclasses, becomes Barrow Deflection: +4% parry, with new ranks
   Barrow Riposte (59110, Bulwark 32, +6%) and Barrow Wardblade (59111,
   Bulwark 44, +8%). Custom icon in an unused icon cell (498), shown with the
   stock rank borders, cast with the Sluagh blood-shield visual.

Client: two copies of icon sheet 400 get one 32x32 cell (no other byte
changes); gamedata.mpk gets three icons.csv and three spells.csv rows (no
stock row changes). Run with the launcher, client and server CLOSED.
"""
from __future__ import annotations

import json
import shutil
import sqlite3
import struct
import subprocess
import sys
from datetime import datetime
from pathlib import Path

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import install_blood_shield as bs  # noqa: E402  (shared guarded helpers)
from daoc_catalog import archive  # noqa: E402
from PIL import Image  # noqa: E402

CLIENT, DB = bs.CLIENT, bs.DB
ART = HERE / "work/barrow-deflection/barrow_deflection_32.png"
SHEETS = (CLIENT / "icons/spl_400.bmp", CLIENT / "icons/classic/SPL_400.bmp")
ICON_CELL = 498                     # unused by every icons.csv row (499 is Siege Ward)
VISUAL_TEMPLATE = "4569"            # Sluagh Blood Shield client row
LINE = "Dullahan's Bulwark"

HOLY = ("You are surrounded by a holy aura.", "{0} is surrounded by a holy aura.",
        "Your holy aura wears off.", "{0}'s holy aura wears off.")
UNHOLY = ("You are surrounded by an unholy aura.", "{0} is surrounded by an unholy aura.",
          "Your unholy aura wears off.", "{0}'s unholy aura wears off.")
ARMOR_BUFFS = (59024, 59025, 59105, 59106, 59107, 59108, 59109)

THOR = ("You are blessed with the strength of Thor!", "{0} is blessed with the strength of the gods!")
CAIRN = ("You are filled with the power of the cairn!", "{0} is filled with the power of the cairn!")
STRENGTH_BUFFS = (59027, 59028, 59029)

TAUNT_POWER = {59040: 2, 59041: 6, 59042: 10, 59043: 14, 59044: 18}
TAUNT_RECAST = 15
TAUNT_TIMER_GROUP = 59040

PARRY_GROUP = 59026
PARRY_MESSAGES = ("Barrow-cold reflexes guide your weapon.", "{0} moves with barrow-cold reflexes.",
                  "Your barrow-cold reflexes fade.", "{0}'s barrow-cold reflexes fade.")
# (spell id, Bulwark level, parry %, name, client rank border)
PARRY_RANKS = ((59026, 20, 4.0, "Barrow Deflection", -1),
               (59110, 32, 6.0, "Barrow Riposte", 0),
               (59111, 44, 8.0, "Barrow Wardblade", 1))


def parry_description(value):
    return (f"Barrow-cold reflexes guide the Sluaghbinder's weapon, increasing its chance to "
            f"parry by {value:g}% for 20 minutes.")


def server_stopped():
    bs.stopped()
    out = subprocess.run(["powershell", "-NoProfile", "-Command",
                          "(Get-Process CoreServer,OfflineDAoC -ErrorAction SilentlyContinue | Measure-Object).Count"],
                         capture_output=True, text=True, check=True).stdout.strip()
    bs.require(out in ("", "0"), "CoreServer or the OfflineDAoC launcher is running; stop them first")


# ---------------------------------------------------------------- icon sheets
def bmp_layout(blob):
    bs.require(blob[:2] == b"BM", "Not a BMP")
    offset, = struct.unpack_from("<I", blob, 10)
    width, height = struct.unpack_from("<ii", blob, 18)
    bpp, compression = struct.unpack_from("<HI", blob, 28)
    bs.require((width, abs(height), bpp, compression) == (320, 320, 24, 0), "Unexpected icon sheet format")
    return offset, width, height, ((width * 3 + 3) // 4) * 4


def paint_cell(blob, icon, cell):
    offset, width, height, stride = bmp_layout(blob)
    data = bytearray(blob)
    x0, y0 = (cell % 10) * 32, ((cell % 100) // 10) * 32
    for y in range(32):
        row = (height - 1 - (y0 + y)) if height > 0 else (y0 + y)
        base = offset + row * stride + x0 * 3
        for x in range(32):
            r, g, b = icon.getpixel((x, y))[:3]
            data[base + x * 3:base + x * 3 + 3] = bytes((b, g, r))
    return bytes(data)


def changed_bytes(old, new):
    return sum(1 for a, b in zip(old, new) if a != b)


# ------------------------------------------------------------- client catalog
def plan_client():
    name, entries, files, tables = bs.catalogs()
    icons = {r[0]: r for r in bs.rows_of(files["icons.csv"]) if r and r[0].strip().isdigit()}
    bs.require(all(not r[2].strip().lstrip("-").isdigit() or int(r[2]) != ICON_CELL for r in icons.values()),
               f"Icon cell {ICON_CELL} is referenced by icons.csv; choose another")
    spells = tables["spells.csv"]
    bs.require(VISUAL_TEMPLATE in spells, "Sluagh Blood Shield client row 4569 is missing; install it first")
    first_icon = max(int(k) for k in icons) + 1
    icon_ids = list(range(first_icon, first_icon + len(PARRY_RANKS)))
    spell_ids = []
    ident = 4570
    while len(spell_ids) < len(PARRY_RANKS):
        if str(ident) not in spells:
            spell_ids.append(ident)
        ident += 1
    width = len(next(iter(icons.values())))
    icon_rows, spell_rows = [], []
    for (spell_id, _, _, title, border), icon_id, client_id in zip(PARRY_RANKS, icon_ids, spell_ids):
        row = [str(icon_id), title, str(ICON_CELL), str(border)] + ["-1"] * (width - 4)
        icon_rows.append(row)
        srow = list(spells[VISUAL_TEMPLATE])
        srow[0], srow[1], srow[2] = str(client_id), "Sluagh " + title, str(icon_id)
        spell_rows.append(srow)
    icon_data, spell_data = files["icons.csv"], files["spells.csv"]
    for row in icon_rows:
        icon_data = bs.add_row(icon_data, row)
    for row in spell_rows:
        spell_data = bs.add_row(spell_data, row)
    replaced = {"icons.csv": icon_data, "spells.csv": spell_data}
    new_entries = [archive.Entry(e.name, replaced.get(e.name.lower(), e.data), e.timestamp, e.flags) for e in entries]
    blob = archive.write(name, new_entries)
    archive.verify_memory_image(blob)
    _, decoded = archive.read(blob)
    bs.require([e.name for e in decoded] == [e.name for e in entries], "Archive entry order changed")
    for old, new in zip(entries, decoded):
        bs.require((old.timestamp, old.flags) == (new.timestamp, new.flags), "Archive metadata changed")
        added = icon_rows if old.name.lower() == "icons.csv" else spell_rows if old.name.lower() == "spells.csv" else None
        if added is None:
            bs.require(old.data == new.data, f"Unrelated archive entry changed: {old.name}")
        else:
            rows = bs.rows_of(new.data)
            bs.require(all(r in rows for r in added), f"New rows missing from {old.name}")
            bs.require([r for r in rows if r not in added] == bs.rows_of(old.data), f"Stock row changed in {old.name}")
    return blob, dict(zip((r[0] for r in PARRY_RANKS), spell_ids)), icon_rows, spell_rows


# ------------------------------------------------------------------- database
def plan_database(con, client_ids):
    spells = bs.spell_snapshot(con)
    for i in ARMOR_BUFFS:
        bs.require(tuple(spells[i][f"Message{n}"] for n in range(1, 5)) == HOLY, f"{i} messages are not the holy-aura text")
    for i in STRENGTH_BUFFS:
        bs.require((spells[i]["Message1"], spells[i]["Message2"]) == THOR, f"{i} messages are not the Thor text")
    for i in TAUNT_POWER:
        bs.require(spells[i]["Type"] == "Taunt" and spells[i]["Power"] == 0 and spells[i]["RecastDelay"] == 4,
                   f"{i} is not the free 4 s taunt; re-review")
    bs.require(spells[59026]["Name"] == "Cairn Ward", "59026 is no longer Cairn Ward; re-review")
    bs.require(59110 not in spells and 59111 not in spells, "59110/59111 already exist")
    updates = {i: dict(zip(("Message1", "Message2", "Message3", "Message4"), UNHOLY)) for i in ARMOR_BUFFS}
    updates.update({i: {"Message1": CAIRN[0], "Message2": CAIRN[1]} for i in STRENGTH_BUFFS})
    updates.update({i: {"Power": p, "RecastDelay": TAUNT_RECAST, "SharedTimerGroup": TAUNT_TIMER_GROUP}
                    for i, p in TAUNT_POWER.items()})
    spell_id, _, value, title, _ = PARRY_RANKS[0]
    parry = {"Name": title, "Description": parry_description(value), "Type": "ParryBuff", "Value": value,
             "Target": "Self", "Range": 0, "Radius": 0, "Duration": 1200, "Power": -10, "CastTime": 3.0,
             "Concentration": 0, "Icon": client_ids[spell_id], "ClientEffect": client_ids[spell_id],
             "SpellGroup": PARRY_GROUP, "EffectGroup": PARRY_GROUP, "PackageID": "Sluaghbinder_Bulwark",
             "Message1": PARRY_MESSAGES[0], "Message2": PARRY_MESSAGES[1],
             "Message3": PARRY_MESSAGES[2], "Message4": PARRY_MESSAGES[3]}
    updates[spell_id] = parry
    inserts = []
    template = dict(spells[59026])
    template.update(parry)
    for new_id, level, value, title, _ in PARRY_RANKS[1:]:
        row = dict(template)
        row.update({"SpellID": new_id, "Name": title, "Description": parry_description(value), "Value": value,
                    "Icon": client_ids[new_id], "ClientEffect": client_ids[new_id],
                    "TooltipId": 29000 + (new_id - 59000), "Spell_ID": f"Sluaghbinder_{new_id}"})
        inserts.append((row, level))
    tooltips = {r["TooltipId"] for r in spells.values()}
    bs.require(all(row["TooltipId"] not in tooltips for row, _ in inserts), "Tooltip identity already used")
    return spells, updates, inserts


def apply_database(con, updates, inserts):
    for ident, values in updates.items():
        sets = ", ".join(f"{column}=?" for column in values)
        con.execute(f"UPDATE Spell SET {sets} WHERE SpellID=?", (*values.values(), ident))
    for row, level in inserts:
        columns = list(row)
        con.execute(f"INSERT INTO Spell ({','.join(columns)}) VALUES ({','.join('?' for _ in columns)})",
                    [row[c] for c in columns])
        con.execute("INSERT INTO LineXSpell (LineName, SpellID, Level, LastTimeRowUpdated, LineXSpell_ID) "
                    "VALUES (?, ?, ?, ?, ?)",
                    (LINE, row["SpellID"], level, "2000-01-01 00:00:00", f"{LINE}_{row['SpellID']}"))


def check_database(before, after, updates, inserts):
    inserted = {row["SpellID"]: row for row, _ in inserts}
    bs.require(set(after) == set(before) | set(inserted), "Unexpected spell rows added/removed")
    for ident, row in before.items():
        expected = dict(row)
        expected.update(updates.get(ident, {}))
        bs.require(after[ident] == expected, f"Unexpected change to spell {ident}")
    for ident, row in inserted.items():
        bs.require(after[ident] == row, f"Inserted spell {ident} differs")


# -------------------------------------------------------------------- actions
def load_icon():
    """The Barrow Deflection icon. The PNG is not in git (it sits on a stock icon frame); when it is missing it is
    drawn from the player's own client by work/barrow-deflection/draw_icon.py."""
    if ART.exists():
        return Image.open(ART).convert("RGB")
    sys.path.insert(0, str(ART.parent))
    import draw_icon  # noqa: E402  (reads the stock spell sheets from runtime/client-opendaoc/app/icons)
    return draw_icon.draw().convert("RGB")


def build():
    icon = load_icon()
    bs.require(icon.size == (32, 32), "Icon art must be 32x32")
    sheets = {}
    for path in SHEETS:
        old = path.read_bytes()
        new = paint_cell(old, icon, ICON_CELL)
        bs.require(changed_bytes(old, new) <= 32 * 32 * 3 and len(old) == len(new), f"{path.name}: unexpected byte changes")
        sheets[path] = (old, new)
    blob, client_ids, icon_rows, spell_rows = plan_client()
    return sheets, blob, client_ids, icon_rows, spell_rows


def preview():
    sheets, blob, client_ids, icon_rows, spell_rows = build()
    with bs.read_db() as con:
        spells, updates, inserts = plan_database(con, client_ids)
    print("Icon sheets:", ", ".join(f"{p.relative_to(CLIENT)} ({changed_bytes(o, n)} bytes)" for p, (o, n) in sheets.items()))
    print("icons.csv rows:", icon_rows)
    print("spells.csv rows:", [r[:3] for r in spell_rows])
    for ident, values in updates.items():
        print(f"  {ident} {spells[ident]['Name']}:")
        for column, value in values.items():
            print(f"     {column}: {spells[ident][column]!r} -> {value!r}")
    for row, level in inserts:
        print(f"  + {row['SpellID']} {row['Name']} ({LINE} {level}) parry {row['Value']}% icon {row['Icon']}")
    print("Preview only. Run with 'install' to apply.")


def install():
    server_stopped()
    sheets, blob, client_ids, icon_rows, spell_rows = build()
    game = CLIENT / "gamedata.mpk"
    with bs.read_db() as con:
        before, updates, inserts = plan_database(con, client_ids)
    backup = HERE / "install-backups" / ("bulwark-update-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
    backup.mkdir(parents=True)
    shutil.copy2(game, backup / "gamedata.mpk")
    for n, path in enumerate(SHEETS):
        shutil.copy2(path, backup / f"sheet{n}.bmp")
    with bs.db_session(DB) as con, bs.db_session(backup / "opendaoc.sqlite3.db") as destination:
        con.backup(destination)
        bs.require(destination.execute("PRAGMA quick_check").fetchone()[0] == "ok", "DB backup invalid")
    manifest = {"created": datetime.now().astimezone().isoformat(),
                "gamedata": {"before": bs.file_sha(game), "after": bs.sha(blob)},
                "sheets": [{"path": str(p), "before": bs.sha(o), "after": bs.sha(n)} for p, (o, n) in sheets.items()],
                "client_rows": {str(k): v for k, v in client_ids.items()},
                "updated": {str(i): {c: before[i][c] for c in values} for i, values in updates.items()},
                "inserted": [row["SpellID"] for row, _ in inserts]}
    (backup / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    bs.atomic_write(game, blob)
    for path, (_, new) in sheets.items():
        bs.atomic_write(path, new)
    with bs.db_session(DB) as con:
        con.row_factory = sqlite3.Row
        apply_database(con, updates, inserts)
        check_database(before, bs.spell_snapshot(con), updates, inserts)
        bs.require(con.execute("PRAGMA quick_check").fetchone()[0] == "ok", "DB quick_check failed")
    bs.require(bs.file_sha(game) == manifest["gamedata"]["after"], "Installed catalog differs")
    print("Client rows:", client_ids)
    print(f'Installed. Rollback with:\n  python -B "{Path(__file__).resolve()}" rollback "{backup}"')


def rollback(folder: Path):
    server_stopped()
    m = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    game = CLIENT / "gamedata.mpk"
    bs.require(bs.file_sha(game) in (m["gamedata"]["before"], m["gamedata"]["after"]),
               "Later catalog changes detected; refuse to overwrite them")
    bs.atomic_write(game, (folder / "gamedata.mpk").read_bytes())
    for n, sheet in enumerate(m["sheets"]):
        bs.atomic_write(Path(sheet["path"]), (folder / f"sheet{n}.bmp").read_bytes())
    with bs.db_session(DB) as con:
        for ident, values in m["updated"].items():
            sets = ", ".join(f"{column}=?" for column in values)
            con.execute(f"UPDATE Spell SET {sets} WHERE SpellID=?", (*values.values(), int(ident)))
        for ident in m["inserted"]:
            con.execute("DELETE FROM Spell WHERE SpellID=?", (ident,))
            con.execute("DELETE FROM LineXSpell WHERE SpellID=?", (ident,))
    print("Rolled back.")


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else "preview"
    if command == "install":
        install()
    elif command == "rollback" and len(sys.argv) > 2:
        rollback(Path(sys.argv[2]))
    else:
        preview()
