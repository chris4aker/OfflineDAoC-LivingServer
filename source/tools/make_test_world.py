"""Build a world database for the automated playtester (source/tools/OfflineDaoc.Playtester).

Two kinds of test world, both written to a NEW file; the source is only ever read:

    python make_test_world.py from-release --world <clean release world .db> --out <new test world .db>
                                           [--bots-per-realm 10]
    python make_test_world.py minimal --out <new test world .db>

from-release (the real test world, on the owner's PC)
    Copies a clean release world (for example the opendaoc.sqlite3.db of a fresh 0.37b install) through SQLite's
    online backup, opened read-only, so the source file is never written. On the copy it runs the release builder's
    own clean_world() (build_release_037.py): every saved-progress table empty, default keeps, doors and relics, no
    account, character or bot. Then it adds the playtester's fixtures (below) and a small roster of level-1 gamebots
    so the chat scenarios have someone to talk to.

minimal (no game files needed: CI and this repository's own checks)
    A world with only what a server needs to start and let a character in: one region (Albion) with two zones and a
    startup location. The server creates every other table itself on its first start. No navmeshes, no NPCs, no
    gamebots: enough for login, save, restart and the chat and group checks between two scripted players.

Fixtures in both:
    * the marker table offline_playtester_world. The playtester refuses to start a server on a database without it,
      so it can never run on a played world.
    * from-release only: each realm's first test bot is level 12 (--helper-level), the kill quest's and guild's group mate.
    * three accounts, made at their first login: playtester and playtester2 (players) and playtestergm (GM, plvl 3).
      Their password is their name; the server stores them hashed at the first login.
    * account creation without the waiting time between new accounts.

It never opens the source for writing, never writes over an existing file, and refuses an --out that is the source.
"""
from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
MARKER_TABLE = "offline_playtester_world"
FORMAT = "1"
ACCOUNTS = (("playtester", 1), ("playtester2", 1), ("playtestergm", 3))

# The server's own table layouts (SqliteObjectDatabase, as it creates them) for the tables the minimal world fills.
# The server adds any column a later build needs (AlterTable) and creates every other table at its first start.
MINIMAL_SCHEMA = {
    "Regions": """CREATE TABLE `Regions` (`RegionID` UNSIGNED SMALLINT(5) NOT NULL DEFAULT 0,
`Name` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `Description` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
`IP` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `Port` UNSIGNED SMALLINT(5) NOT NULL DEFAULT 0,
`Expansion` INT(11) NOT NULL DEFAULT 0, `HousingEnabled` TINYINT(1) NOT NULL DEFAULT 0,
`DivingEnabled` TINYINT(1) NOT NULL DEFAULT 0, `WaterLevel` INT(11) NOT NULL DEFAULT 0,
`ClassType` VARCHAR(200) NOT NULL DEFAULT '' COLLATE NOCASE, `IsFrontier` TINYINT(1) NOT NULL DEFAULT 0,
`LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00', `Regions_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE,
PRIMARY KEY (`RegionID`))""",
    "Zones": """CREATE TABLE `Zones` (`ZoneID` INT(11) NOT NULL DEFAULT 0, `RegionID` UNSIGNED SMALLINT(5) NOT NULL DEFAULT 0,
`Name` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `IsLava` TINYINT(1) NOT NULL DEFAULT 0,
`DivingFlag` UNSIGNED TINYINT(3) NOT NULL DEFAULT 0, `WaterLevel` INT(11) NOT NULL DEFAULT 0,
`OffsetY` INT(11) NOT NULL DEFAULT 0, `OffsetX` INT(11) NOT NULL DEFAULT 0, `Width` INT(11) NOT NULL DEFAULT 0,
`Height` INT(11) NOT NULL DEFAULT 0, `Experience` INT(11) NOT NULL DEFAULT 0, `Realmpoints` INT(11) NOT NULL DEFAULT 0,
`Bountypoints` INT(11) NOT NULL DEFAULT 0, `Coin` INT(11) NOT NULL DEFAULT 0, `Realm` UNSIGNED TINYINT(3) NOT NULL DEFAULT 0,
`LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00', `Zones_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE,
PRIMARY KEY (`ZoneID`))""",
    "StartupLocation": """CREATE TABLE `StartupLocation` (`StartupLoc_ID` INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
`XPos` INT(11) NOT NULL DEFAULT 0, `YPos` INT(11) NOT NULL DEFAULT 0, `ZPos` INT(11) NOT NULL DEFAULT 0,
`Heading` INT(11) NOT NULL DEFAULT 0, `Region` INT(11) NOT NULL DEFAULT 0, `MinVersion` INT(11) NOT NULL DEFAULT 0,
`RealmID` INT(11) NOT NULL DEFAULT 0, `RaceID` INT(11) NOT NULL DEFAULT 0, `ClassID` INT(11) NOT NULL DEFAULT 0,
`ClientRegionID` INT(11) NOT NULL DEFAULT 0, `LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00')""",
    "Account": """CREATE TABLE `Account` (`Name` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE,
`Password` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `CreationDate` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',
`LastLogin` DATETIME DEFAULT NULL, `Realm` INT(11) NOT NULL DEFAULT 0, `PrivLevel` UNSIGNED INT(10) NOT NULL DEFAULT 0,
`Status` INT(11) NOT NULL DEFAULT 0, `Mail` TEXT DEFAULT NULL COLLATE NOCASE, `LastLoginIP` VARCHAR(255) DEFAULT NULL COLLATE NOCASE,
`LastClientVersion` TEXT DEFAULT NULL COLLATE NOCASE, `Language` TEXT DEFAULT NULL COLLATE NOCASE,
`IsMuted` TINYINT(1) NOT NULL DEFAULT 0, `IsWarned` TINYINT(1) NOT NULL DEFAULT 0, `Notes` TEXT DEFAULT NULL COLLATE NOCASE,
`IsTester` TINYINT(1) NOT NULL DEFAULT 0, `CharactersTraded` INT(11) NOT NULL DEFAULT 0,
`SoloCharactersTraded` INT(11) NOT NULL DEFAULT 0, `DiscordID` TEXT DEFAULT NULL COLLATE NOCASE,
`Realm_Timer_Realm` INT(11) NOT NULL DEFAULT 0, `Realm_Timer_Last_Combat` DATETIME DEFAULT NULL,
`LastDisconnected` DATETIME DEFAULT NULL, `LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00',
`Account_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE, PRIMARY KEY (`Name`))""",
    "ServerProperty": """CREATE TABLE `ServerProperty` (`Category` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
`Key` VARCHAR(255) NOT NULL DEFAULT '' COLLATE NOCASE, `Description` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
`DefaultValue` TEXT NOT NULL DEFAULT '' COLLATE NOCASE, `Value` TEXT NOT NULL DEFAULT '' COLLATE NOCASE,
`LastTimeRowUpdated` DATETIME NOT NULL DEFAULT '2000-01-01 00:00:00', `ServerProperty_ID` VARCHAR(255) DEFAULT NULL COLLATE NOCASE,
PRIMARY KEY (`Key`))""",
}

# Albion (region 1) with Camelot Hills and Salisbury Plains, as the release world has them; the startup location is
# the release world's Cotswold start (StartupLocation rows 1-4), for every Albion race and class.
MINIMAL_REGIONS = [(1, "Region001", "Albion", "127.0.0.1", 10400, 0, 0, 0, 0, "", 1)]
MINIMAL_ZONES = [(0, 1, "Camelot Hills", 59, 67, 8, 8), (1, 1, "Salisbury Plains", 67, 67, 8, 8)]
MINIMAL_STARTS = [(574123, 530520, 2933, 3077, 1, 0, 1, 0, 0, 0)]

# A few plain names per realm for the test roster (test worlds only; the launcher's generator makes real rosters).
BOT_NAMES = {
    1: ["Aldric", "Brennan", "Cedrik", "Dunstan", "Edwina", "Fenwick", "Gareth", "Hollis", "Isolde", "Jorran"],
    2: ["Arnulf", "Bjornar", "Dagrun", "Eirik", "Frode", "Gudrun", "Halvard", "Ingvild", "Jorund", "Ketil"],
    3: ["Aodhan", "Brannoc", "Caoimhe", "Deaglan", "Eithne", "Fionnan", "Grainne", "Labhras", "Meadhbh", "Niamhan"],
}
# GamePlayer.XPForLevel: the experience a character has on reaching each level (index = level - 1), to level 20
XP_FOR_LEVEL = [0, 50, 250, 850, 2300, 6350, 15950, 37950, 88950, 203950, 459950, 839950, 1399950, 2199950, 3399950,
                5199950, 7899950, 11949950, 17999950, 27099950]

# The helper bots' specializations, as a level-10 tank of the class trains them (GameBot.SerializedSpecs "Spec|level;...").
# 20 is these classes' specialization multiplier: points 2+3+4+5 at levels 2-5 and 2 x level from 6 on, 94 by level 10;
# a spec level n costs n points: main weapon 9 (44), Shields 7 (27), Parry 5 (14) spend 85, and 9 are left over.
HELPER_SPECS = {
    2: "Slash|9;Shields|7;Parry|5;Thrust|1;Crush|1;Polearm|1;Two Handed|1",
    22: "Sword|9;Shields|7;Parry|5;Axe|1;Hammer|1;Thrown Weapons|1",
    43: "Blades|9;Shields|7;Parry|5;Blunt|1;Piercing|1;Celtic Dual|1",
}


def spec_points(level: int) -> int:
    """GameBot.GetSpecPointsForLevel summed to a level, for a multiplier-20 class."""
    return sum(lvl if lvl <= 5 else 2 * lvl for lvl in range(2, level + 1))


def helper_specs(class_id: int, level: int) -> tuple[str, int]:
    """Scaled from the level-10 plan: the trained specs and the points left over at this level."""
    plan = [(name, int(n)) for name, n in (part.split("|") for part in HELPER_SPECS[class_id].split(";"))]
    total = spec_points(level)
    specs, spent = [], 0
    for name, at10 in plan:
        want = max(1, min(level, round(at10 * level / 10)))
        cost = sum(range(2, want + 1))
        if spent + cost > total:
            want, cost = 1, 0
        specs.append(f"{name}|{want}")
        spent += cost
    return ";".join(specs), total - spent


# (class id, class name, race id, race name) as the launcher's BotCharacterGenerator picks them
BOT_STARTERS = {
    1: [(2, "Armsman", 1, "Briton"), (5, "Theurgist", 1, "Briton"), (3, "Scout", 3, "Highlander"), (6, "Cleric", 1, "Briton")],
    2: [(22, "Warrior", 5, "Norseman"), (26, "Healer", 7, "Dwarf"), (25, "Hunter", 8, "Kobold"), (27, "Spiritmaster", 5, "Norseman")],
    3: [(43, "Blademaster", 9, "Celt"), (47, "Druid", 10, "Firbolg"), (50, "Ranger", 11, "Elf"), (42, "Mentalist", 12, "Lurikeen")],
}


def now_text() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")


def refuse_paths(out: Path, world: Path | None = None) -> None:
    """Never write over a file, and never let the output be the source."""
    if out.exists():
        raise SystemExit(f"{out} already exists: give a new file name (this tool never writes over a world)")
    for suffix in ("-wal", "-shm", "-journal"):
        if Path(str(out) + suffix).exists():
            raise SystemExit(f"{out}{suffix} exists: remove it or pick another name")
    if world is not None:
        if not world.is_file():
            raise SystemExit(f"{world} is not a file")
        if out.resolve() == world.resolve():
            raise SystemExit("--out is the source world: the test world is always a new file")
    if not out.parent.is_dir():
        raise SystemExit(f"{out.parent} does not exist")


def read_only_copy(world: Path, out: Path) -> None:
    """Copy the world with SQLite's online backup, the source opened read-only (mode=ro): it is never written."""
    source = sqlite3.connect(world.resolve().as_uri() + "?mode=ro", uri=True)
    try:
        target = sqlite3.connect(out)
        try:
            source.backup(target)
        finally:
            target.close()
    finally:
        source.close()


def set_property(con: sqlite3.Connection, key: str, value: str, category: str = "playtester", description: str = "") -> None:
    if con.execute("SELECT COUNT(*) FROM ServerProperty WHERE lower(`Key`)=lower(?)", (key,)).fetchone()[0]:
        con.execute("UPDATE ServerProperty SET Value=? WHERE lower(`Key`)=lower(?)", (value, key))
    else:
        con.execute("INSERT INTO ServerProperty (Category,`Key`,Description,DefaultValue,Value,LastTimeRowUpdated,ServerProperty_ID) "
                    "VALUES (?,?,?,?,?,?,?)", (category, key, description or key, value, value, now_text(), key))


def add_fixtures(con: sqlite3.Connection, mode: str, source: str) -> dict:
    """The marker, the playtester's accounts and quick account creation (shared by both kinds of world)."""
    con.execute(f"CREATE TABLE IF NOT EXISTS {MARKER_TABLE} (Key TEXT PRIMARY KEY, Value TEXT NOT NULL)")
    marker = {"format": FORMAT, "mode": mode, "source": source, "made_utc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
              "accounts": ",".join(name for name, _ in ACCOUNTS)}
    con.executemany(f"INSERT OR REPLACE INTO {MARKER_TABLE} (Key,Value) VALUES (?,?)", marker.items())
    for name, plvl in ACCOUNTS:
        # A plain password is hashed by the server at the first login (LoginRequestHandler: no "##" prefix).
        con.execute("INSERT INTO Account (Name,Password,CreationDate,Realm,PrivLevel,Language,LastTimeRowUpdated,Account_ID) "
                    "VALUES (?,?,?,0,?,'EN',?,?)", (name, name, now_text(), plvl, now_text(), name))
    set_property(con, "time_between_account_creation", "0", "account")
    set_property(con, "allow_auto_account_creation", "True", "account")
    return marker


def build_minimal(out: Path) -> dict:
    refuse_paths(out)
    con = sqlite3.connect(out)
    try:
        for ddl in MINIMAL_SCHEMA.values():
            con.execute(ddl)
        stamp = now_text()
        con.executemany("INSERT INTO Regions (RegionID,Name,Description,IP,Port,Expansion,HousingEnabled,DivingEnabled,WaterLevel,"
                        "ClassType,IsFrontier,LastTimeRowUpdated,Regions_ID) VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?)",
                        [r + (stamp, f"region-{r[0]}") for r in MINIMAL_REGIONS])
        con.executemany("INSERT INTO Zones (ZoneID,RegionID,Name,OffsetY,OffsetX,Width,Height,LastTimeRowUpdated,Zones_ID) "
                        "VALUES (?,?,?,?,?,?,?,?,?)", [z + (stamp, str(z[0])) for z in MINIMAL_ZONES])
        con.executemany("INSERT INTO StartupLocation (XPos,YPos,ZPos,Heading,Region,MinVersion,RealmID,RaceID,ClassID,ClientRegionID,"
                        "LastTimeRowUpdated) VALUES (?,?,?,?,?,?,?,?,?,?,?)", [s + (stamp,) for s in MINIMAL_STARTS])
        marker = add_fixtures(con, "minimal", "built from nothing (make_test_world.py minimal)")
        con.commit()
        report = verify_test_world(con)
    finally:
        con.close()
    report["marker"] = marker
    return report


def add_test_bots(con: sqlite3.Connection, per_realm: int, helper_level: int = 0) -> list[str]:
    """Level-1 gamebots at their realm's startup locations, queued as the launcher queues them (MainForm.GenerateBotCharacters).

    With helper_level, each realm's first bot (a tank: Armsman, Warrior, Blademaster) is that level instead, with the
    experience of that level and trained for it: its specializations in its record and "trained-level|N" (a gamebot
    trains only at a natural break between tasks, which a fresh bot reaches late: the first in-container run waited 5
    minutes for it in vain). It is the bot a kill-quest scenario invites: a few levels above the quest, as a player
    would meet one. It starts without armour, as every gamebot does."""
    names = []
    stamp = datetime.now(timezone.utc).isoformat()
    for realm in (1, 2, 3):
        for i in range(per_realm):
            name = BOT_NAMES[realm][i]
            class_id, class_name, race_id, race_name = BOT_STARTERS[realm][i % len(BOT_STARTERS[realm])]
            start = con.execute("SELECT Region,XPos,YPos,ZPos FROM StartupLocation WHERE MinVersion<=168 AND (RealmID=0 OR RealmID=?) "
                                "AND (RaceID=0 OR RaceID=?) AND (ClassID=0 OR ClassID=?) AND (XPos<>0 OR YPos<>0) "
                                "AND Region NOT IN (27) ORDER BY ClassID DESC, RaceID DESC, StartupLoc_ID LIMIT 1",
                                (realm, race_id, class_id)).fetchone()
            if start is None:
                raise SystemExit(f"no startup location for realm {realm} {race_name} {class_name}")
            region, x, y, z = start
            level = helper_level if helper_level > 1 and i == 0 else 1
            con.execute("""INSERT INTO offline_world_bots
                (Name, Realm, ClassId, ClassName, RaceId, RaceName, Gender, Level, Experience, RealmPoints,
                 ZoneName, Activity, IsOnline, IsAlive, LastUpdateUtc, MoneyCopper, InventoryRevision,
                 ZoneId, X, Y, Z, RegionId, Health, Mana, Endurance, BindRegionId, BindX, BindY, BindZ,
                 CurrentGoal, ObjectiveProgress, LastMeaningfulProgressUtc, IsRetired)
                VALUES (?,?,?,?,?,?,?,?,?,0,'Classic starting area','Queued at randomized starting location',0,1,?,0,0,
                        0,?,?,?,?,1,0,0,?,?,?,?,'Awaiting staggered login queue','Playtester test roster',?,0)""",
                        (name, realm, class_id, class_name, race_id, race_name, i % 2, level, XP_FOR_LEVEL[level - 1], stamp,
                         x, y, z, region, region, x, y, z, stamp))
            if level > 1:
                bot_id = con.execute("SELECT last_insert_rowid()").fetchone()[0]
                specs, left = helper_specs(class_id, level)
                con.execute("UPDATE offline_world_bots SET SerializedAbilities=?, SerializedSpecs=?, UnspentSpecPoints=? WHERE BotId=?",
                            (f"trained-level|{level}|realm-points|0", specs, left, bot_id))
            names.append(name)
    return names


def set_startup_ramp(con: sqlite3.Connection, minutes: int) -> bool:
    """The server's own startup ramp (ServerProperty startup_ramp_minutes, default 15) set short, so the whole test roster
    is online a minute after a start, not fifteen. offline_population_settings only mirrors it for the launcher.
    False when the world has no such row (the server then uses its default)."""
    if not con.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='ServerProperty'").fetchone():
        return False
    return con.execute("UPDATE ServerProperty SET Value=? WHERE lower(`Key`)='startup_ramp_minutes'", (str(minutes),)).rowcount > 0


def build_from_release(world: Path, out: Path, bots_per_realm: int, helper_level: int = 12) -> dict:
    refuse_paths(out, world)
    sys.path.insert(0, str(HERE))
    import build_release_037 as release  # the release builder's own cleaning and checks

    policy = json.loads((HERE / "OfflineDaoc.ProgressImport/progress-policy.json").read_text())
    defaults = json.loads((HERE / "release-world-defaults.json").read_text(encoding="utf-8"))
    read_only_copy(world, out)
    try:
        before = sqlite3.connect(out)
        saves = {t: before.execute(f'SELECT COUNT(*) FROM "{t}"').fetchone()[0] for t in ("Account", "DOLCharacters", "offline_world_bots")
                 if before.execute("SELECT COUNT(*) FROM sqlite_master WHERE name=?", (t,)).fetchone()[0]}
        before.close()
        cleaned = release.clean_world(out, policy, defaults)   # empty saves, default world (asserts as a release does)
        con = sqlite3.connect(out)
        try:
            marker = add_fixtures(con, "from-release", str(world))
            bots = add_test_bots(con, bots_per_realm, helper_level) if bots_per_realm else []
            con.execute("UPDATE offline_population_settings SET Value=? WHERE Key='ActiveTarget'", (str(len(bots)),))
            con.execute("UPDATE offline_population_settings SET Value='1' WHERE Key='StartupRampMinutes'")
            set_startup_ramp(con, 1)
            con.commit()
            report = verify_test_world(con)
        finally:
            con.close()
    except BaseException:
        for suffix in ("", "-wal", "-shm", "-journal"):
            Path(str(out) + suffix).unlink(missing_ok=True)   # never leave a half-made world behind
        raise
    report.update({"marker": marker, "saves_in_source": saves, "release_clean": cleaned, "bots": bots})
    return report


def verify_test_world(con: sqlite3.Connection) -> dict:
    """What the playtester relies on: the marker, the accounts, a startup location and no character."""
    assert con.execute("PRAGMA quick_check").fetchone()[0] == "ok"
    marker = dict(con.execute(f"SELECT Key,Value FROM {MARKER_TABLE}"))
    assert marker.get("format") == FORMAT, marker
    accounts = {name: plvl for name, plvl in con.execute("SELECT Name,PrivLevel FROM Account")}
    assert accounts == dict(ACCOUNTS), f"accounts other than the playtester's: {accounts}"
    tables = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    if "DOLCharacters" in tables:
        assert con.execute("SELECT COUNT(*) FROM DOLCharacters").fetchone()[0] == 0, "a character is in the test world"
    starts = con.execute("SELECT COUNT(*) FROM StartupLocation").fetchone()[0]
    assert starts > 0, "no startup location"
    return {"quick_check": "ok", "accounts": accounts, "startup_locations": starts,
            "regions": con.execute("SELECT COUNT(*) FROM Regions").fetchone()[0]}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="mode", required=True)
    rel = sub.add_parser("from-release", help="the real test world, from a clean release world (read only)")
    rel.add_argument("--world", required=True, type=Path, help="a clean release world database (never written)")
    rel.add_argument("--out", required=True, type=Path, help="the new test world file (must not exist)")
    rel.add_argument("--bots-per-realm", type=int, default=10,
                     help="gamebots per realm (default 10, the most; 0 for none). faction-rate's prediction scales with them")
    rel.add_argument("--helper-level", type=int, default=12,
                     help="the level of each realm's first bot, the kill quest's group mate (default 12, 1 for none)")
    mini = sub.add_parser("minimal", help="a minimal world that needs no game files")
    mini.add_argument("--out", required=True, type=Path, help="the new test world file (must not exist)")
    args = parser.parse_args(argv)
    if args.mode == "minimal":
        report = build_minimal(args.out)
    else:
        if not 0 <= args.bots_per_realm <= len(BOT_NAMES[1]):
            raise SystemExit(f"--bots-per-realm is 0 to {len(BOT_NAMES[1])}")
        if not 1 <= args.helper_level <= len(XP_FOR_LEVEL):
            raise SystemExit(f"--helper-level is 1 to {len(XP_FOR_LEVEL)}")
        report = build_from_release(args.world, args.out, args.bots_per_realm, args.helper_level)
    print(json.dumps(report, indent=2, default=str))
    print(f"Test world written to {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
