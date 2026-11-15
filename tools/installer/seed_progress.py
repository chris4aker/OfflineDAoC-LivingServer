"""Seeds a played-looking character and changed settings into an install's world, and later checks they were kept.

    python tools/installer/seed_progress.py seed  --game <playable folder> --record <file.json>
    python tools/installer/seed_progress.py check --game <playable folder> --record <file.json> [--db <other .db>]

For the installer's update114 test (test-installer.ps1): a Living Server 1.1.4 game gets the seed, Setup updates it to
the candidate, and check must find every seeded row and file as it was. --db checks another copy of the world instead
(the copy the smoke test's server ran on, after the candidate server's first start). CI only, on a throwaway install:
it never touches a real player's game (it refuses a world that already has characters).

What is seeded (a player's progress and choices, as a played game holds them):
- an account and a level-20 Armsman with experience, realm points, coin and a position, and an item in its backpack
  (a copy of the world's first item template);
- one gamebot per realm in the roster (make_test_world.py's own roster rows);
- Living Server settings changed away from their defaults and from the suggested ones (ServerProperty), the bot
  population target (offline_population_settings) and a launcher option (offline_local_options).
Files recorded by SHA-256 (when present): runtime/account.txt, runtime/server/bot-goals.json and the client's game.dll.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1] / "source" / "tools"))
import make_test_world as mtw  # noqa: E402  (the playtester's world builder: its roster rows and XP table)

ACCOUNT = "lsupdatecheck"
CHARACTER = "Seedkeeper"
CHARACTER_ID = "ls-update-check-seedkeeper"
ITEM_ID = "ls-update-check-item"
# Values a player could choose that are neither the server's default nor Living Server's suggested settings.
SETTINGS = {
    "bot_voices_chat_rate": "0.7",
    "bot_guild_share": "33",
    "bot_schedule_mode": "clock",
    "xp_rate_by_level": "1-50:2",
    "bot_living_firsts": "True",
    "bot_raid_invite_player": "False",
}
POPULATION = {"ActiveTarget": "123"}
LOCAL_OPTIONS = {"MakeMeGM": "False"}
FILES = ("runtime/account.txt", "runtime/server/bot-goals.json", "runtime/client-opendaoc/app/game.dll")
CHARACTER_VALUES = {
    "DOLCharacters_ID": CHARACTER_ID, "AccountName": ACCOUNT, "AccountSlot": 100, "Name": CHARACTER,
    "Realm": 1, "Class": 2, "Race": 1, "Gender": 0, "Level": 20, "Experience": mtw.XP_FOR_LEVEL[19],
    "RealmPoints": 1234, "BountyPoints": 56, "Copper": 7, "Silver": 42, "Gold": 13, "Platinum": 1, "Mithril": 0,
    "CreationModel": 32, "CurrentModel": 32, "Region": 1, "Xpos": 560200, "Ypos": 511800, "Zpos": 2280, "Direction": 1024,
    "BindRegion": 1, "BindXpos": 560200,
}
CHARACTER_KEPT = tuple(k for k in CHARACTER_VALUES if k != "DOLCharacters_ID")
ITEM_KEPT = ("OwnerID", "ITemplate_Id", "SlotPosition", "Count")


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def columns(con: sqlite3.Connection, table: str) -> list[str]:
    return [r[1] for r in con.execute(f'PRAGMA table_info("{table}")')]


def has_table(con: sqlite3.Connection, table: str) -> bool:
    return con.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone() is not None


def insert(con: sqlite3.Connection, table: str, values: dict) -> None:
    """Inserts the values the table has columns for; the server's own column defaults fill the rest."""
    have = {c.lower(): c for c in columns(con, table)}
    missing = [k for k in values if k.lower() not in have]
    if missing:
        raise SystemExit(f"{table} has no column(s) {missing}: the world's layout changed, update seed_progress.py")
    names = [have[k.lower()] for k in values]
    con.execute(f'INSERT INTO "{table}" ({",".join(f"`{n}`" for n in names)}) VALUES ({",".join("?" * len(names))})',
                list(values.values()))


def world_of(game: Path) -> Path:
    return game / "runtime" / "data" / "opendaoc.sqlite3.db"


def snapshot(con: sqlite3.Connection, game: Path | None, bots: list[str]) -> dict:
    """The seeded rows and files as they are now."""
    snap: dict = {}
    row = con.execute(f"SELECT {','.join(f'`{c}`' for c in CHARACTER_KEPT)} FROM DOLCharacters WHERE Name=?", (CHARACTER,)).fetchone()
    snap["character"] = dict(zip(CHARACTER_KEPT, row)) if row else None
    acct = con.execute("SELECT Name, PrivLevel FROM Account WHERE Name=?", (ACCOUNT,)).fetchone()
    snap["account"] = list(acct) if acct else None
    item = con.execute(f"SELECT {','.join(ITEM_KEPT)} FROM Inventory WHERE OwnerID=?", (CHARACTER_ID,)).fetchall()
    snap["items"] = [dict(zip(ITEM_KEPT, r)) for r in item]
    snap["bots"] = {}
    for name in bots:
        r = con.execute("SELECT Realm, ClassId, RaceId, Level, Experience FROM offline_world_bots WHERE Name=?", (name,)).fetchone()
        snap["bots"][name] = list(r) if r else None
    snap["settings"] = {k: (con.execute("SELECT Value FROM ServerProperty WHERE lower(`Key`)=lower(?)", (k,)).fetchone() or [None])[0]
                        for k in SETTINGS}
    if has_table(con, "offline_population_settings"):
        snap["population"] = {k: (con.execute("SELECT Value FROM offline_population_settings WHERE Key=?", (k,)).fetchone() or [None])[0]
                              for k in POPULATION}
    if has_table(con, "offline_local_options"):
        snap["local_options"] = {k: (con.execute("SELECT Value FROM offline_local_options WHERE Key=?", (k,)).fetchone() or [None])[0]
                                 for k in LOCAL_OPTIONS}
    if game is not None:
        snap["files"] = {f: sha(game / f) for f in FILES if (game / f).is_file()}
    return snap


def seed(game: Path, record: Path) -> dict:
    db = world_of(game)
    con = sqlite3.connect(db)
    try:
        for table in ("Account", "DOLCharacters", "Inventory", "ItemTemplate", "ServerProperty", "offline_world_bots", "StartupLocation"):
            if not has_table(con, table):
                raise SystemExit(f"{db} has no {table} table: not an Offline DAoC 0.37 world")
        if con.execute("SELECT COUNT(*) FROM DOLCharacters").fetchone()[0]:
            raise SystemExit(f"{db} already holds characters: seed_progress.py only seeds a new test install")
        stamp = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")
        insert(con, "Account", {"Name": ACCOUNT, "Password": ACCOUNT, "CreationDate": stamp, "Realm": 0, "PrivLevel": 1,
                                "Language": "EN", "LastTimeRowUpdated": stamp, "Account_ID": ACCOUNT})
        insert(con, "DOLCharacters", dict(CHARACTER_VALUES, CreationDate=stamp, LastPlayed=stamp, LastTimeRowUpdated=stamp))
        template = con.execute("SELECT Id_nb FROM ItemTemplate ORDER BY Id_nb LIMIT 1").fetchone()
        if template is None:
            raise SystemExit(f"{db} has no item templates")
        insert(con, "Inventory", {"Inventory_ID": ITEM_ID, "OwnerID": CHARACTER_ID, "ITemplate_Id": template[0],
                                  "SlotPosition": 40, "Count": 1, "Condition": 50000, "Durability": 50000,
                                  "LastTimeRowUpdated": stamp})
        bots = mtw.add_test_bots(con, 1)
        for key, value in SETTINGS.items():
            mtw.set_property(con, key, value, "living_server")
        if has_table(con, "offline_population_settings"):
            for key, value in POPULATION.items():
                if not con.execute("UPDATE offline_population_settings SET Value=? WHERE Key=?", (value, key)).rowcount:
                    insert(con, "offline_population_settings", {"Key": key, "Value": value})
        con.execute("CREATE TABLE IF NOT EXISTS offline_local_options (Key TEXT PRIMARY KEY,Value TEXT NOT NULL)")
        for key, value in LOCAL_OPTIONS.items():
            con.execute("INSERT INTO offline_local_options(Key,Value) VALUES (?,?) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value",
                        (key, value))
        con.commit()
        snap = snapshot(con, game, bots)
    finally:
        con.close()
    data = {"world": str(db), "bots": bots, "seeded": snap}
    record.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return data


def check(game: Path, record: Path, db: Path | None = None) -> list[str]:
    """What differs from the seed (empty: everything kept)."""
    data = json.loads(record.read_text(encoding="utf-8"))
    want = data["seeded"]
    path = db or world_of(game)
    con = sqlite3.connect(Path(path).resolve().as_uri() + "?mode=ro", uri=True)
    try:
        # Files are compared only for the game itself; another copy of the world has no game folder around it.
        now = snapshot(con, game if db is None else None, data["bots"])
    finally:
        con.close()
    problems = []
    for part, seeded in want.items():
        if part == "files" and db is not None:
            continue
        got = now.get(part)
        if isinstance(seeded, dict) and isinstance(got, dict):
            for key, value in seeded.items():
                if got.get(key) != value:
                    problems.append(f"{part} {key}: seeded {value!r}, now {got.get(key)!r}")
        elif got != seeded:
            problems.append(f"{part}: seeded {seeded!r}, now {got!r}")
    return problems


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("action", choices=("seed", "check"))
    ap.add_argument("--game", required=True, type=Path, help="the playable folder (holds runtime\\data)")
    ap.add_argument("--record", required=True, type=Path, help="the seed's record (written by seed, read by check)")
    ap.add_argument("--db", type=Path, help="check: another copy of the world (files are then not compared)")
    a = ap.parse_args(argv)
    if a.action == "seed":
        data = seed(a.game, a.record)
        s = data["seeded"]
        print(f"SEEDED {CHARACTER} level {s['character']['Level']} ({len(s['items'])} item), {len(data['bots'])} gamebots, "
              f"{len(s['settings'])} settings, files {sorted(s.get('files', {}))}")
        return 0
    problems = check(a.game, a.record, a.db)
    for p in problems:
        print(f"CHANGED {p}")
    print("KEPT everything seeded" if not problems else f"{len(problems)} seeded value(s) changed")
    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
