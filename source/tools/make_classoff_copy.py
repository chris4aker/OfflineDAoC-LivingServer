"""The class-off (0.37 edition) checks' helper: reads a game's edition, and makes a class-off copy of a test game.

    python make_classoff_copy.py status --game <playable folder>
    python make_classoff_copy.py make --from <test game> --to <new folder beside it> [--classic-game-dll <game.dll>]

status prints the edition as the server and the client see it, one line a test can match, then the custom-class saves:
    EDITION enable_sluaghbinder=False game.dll=classic   (the 0.37 edition: class off)
    EDITION enable_sluaghbinder=True game.dll=sluaghbinder (0.37b)
    CUSTOM-CLASS characters=0 gamebots=0                 (Hibernian classes 16, 62 and 63; a 0.37 world has none)

make copies a test game (on the owner's PC: C:\\Games\\OfflineDAoC\\playable-v0.37b-fresh) to a new folder in the same
parent and turns the COPY into the 0.37 edition, as build_release_037.py makes the 0.37 world and client:
- classes / enable_sluaghbinder False in the copy's world (build_release_037.set_edition), the Sluaghbinder trainer and
  wisp and the class's specialization rows removed (build_release_037.no_custom_class_database's statements);
- the copy's client game.dll replaced by the 0.37 one (the 0.37b client without its two Sluaghbinder patches). Both
  files are hash-guarded: the copy's must be the 0.37b client (or already the 0.37 one), the replacement the 0.37 one.
  A new install from Living Server Setup has it (runtime\\client-opendaoc\\app\\game.dll);
- the world copied with SQLite's backup (opened read-only, so the source is never written).
It refuses: the main install (a folder named playable-v0.37b), a target that exists or is outside the source's parent,
and a running server (port 10300 answering). Characters and gamebots of the custom class in the copy are listed, not
changed (a 0.37 world never has them; play a classic class). Nothing in the source folder is changed.
"""
from __future__ import annotations

import argparse
import hashlib
import shutil
import socket
import sqlite3
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_release_037 as release  # noqa: E402  (the release builder's edition switch, mobs and client hashes)

CLASSIC_GAME_DLL = release.EDITION_GAME_DLL_SHA256      # 0.37: without the Sluaghbinder patches
SLUAGH_GAME_DLL = release.SLUAGH_GAME_DLL_SHA256        # 0.37b
import patch_era_creation_text as era_text  # noqa: E402
# The 0.37 client with the installer's "Not available in this era" description patch (#634) is still the classic client.
CLASSIC_PATCHED_GAME_DLL = era_text.known_fixed()
MAIN_INSTALL = "playable-v0.37b"
GAME_DLL = Path("runtime/client-opendaoc/app/game.dll")
WORLD = Path("runtime/data/opendaoc.sqlite3.db")
CUSTOM_CLASSES = (16, 62, 63)   # Acolyte (the Sluaghbinder novice in Hibernia), the client's Mauler slot, Sluaghbinder


def sha(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def client_kind(path: Path) -> str:
    if not path.is_file():
        return "missing"
    digest = sha(path)
    if digest in CLASSIC_PATCHED_GAME_DLL:
        return "classic"
    return {CLASSIC_GAME_DLL: "classic", SLUAGH_GAME_DLL: "sluaghbinder"}.get(digest, "unknown")


def switch_value(db: Path) -> str:
    con = sqlite3.connect(db.resolve().as_uri() + "?mode=ro", uri=True)
    try:
        if not con.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='ServerProperty'").fetchone():
            return "missing"
        row = con.execute("SELECT Value FROM ServerProperty WHERE lower(`Key`)='enable_sluaghbinder'").fetchone()
        return str(row[0]) if row else "missing"
    finally:
        con.close()


def custom_class_saves(db: Path) -> dict:
    """Hibernian characters and gamebots of the custom class (a 0.37 world has none)."""
    con = sqlite3.connect(db.resolve().as_uri() + "?mode=ro", uri=True)
    try:
        tables = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        marks = ",".join("?" * len(CUSTOM_CLASSES))
        chars = con.execute(f"SELECT Name, AccountName, Class FROM DOLCharacters WHERE Realm=3 AND Class IN ({marks}) ORDER BY Name",
                            CUSTOM_CLASSES).fetchall() if "DOLCharacters" in tables else []
        bots = con.execute(f"SELECT Name, ClassId FROM offline_world_bots WHERE Realm=3 AND ClassId IN ({marks}) ORDER BY Name",
                           CUSTOM_CLASSES).fetchall() if "offline_world_bots" in tables else []
        return {"characters": chars, "gamebots": bots}
    finally:
        con.close()


def status(game: Path) -> str:
    db = game / WORLD
    value = switch_value(db) if db.is_file() else "no-world"
    return f"EDITION enable_sluaghbinder={value} game.dll={client_kind(game / GAME_DLL)}"


def port_in_use(port: int = 10300) -> bool:
    with socket.socket() as probe:
        probe.settimeout(1)
        return probe.connect_ex(("127.0.0.1", port)) == 0


def make(source: Path, target: Path, classic_dll: Path | None, check_port: bool = True) -> dict:
    source, target = source.resolve(), target.resolve()
    if source.name.lower() == MAIN_INSTALL:
        raise SystemExit(f"{source} is the main install ({MAIN_INSTALL}): it is never copied or touched. Use the test copy.")
    if not (source / WORLD).is_file():
        raise SystemExit(f"{source} has no {WORLD}: not a playable folder")
    if target.exists():
        raise SystemExit(f"{target} exists: give a new folder (this tool never writes over a game)")
    if target.parent != source.parent:
        raise SystemExit(f"{target} must be beside {source}, in {source.parent}")
    if target.name.lower() == MAIN_INSTALL:
        raise SystemExit(f"{target.name} is the main install's name")
    if check_port and port_in_use():
        raise SystemExit("A server answers on port 10300: stop it first (the world must not change while it is copied)")
    kind = client_kind(source / GAME_DLL)
    if kind == "sluaghbinder":
        if classic_dll is None:
            raise SystemExit("The test game has the 0.37b client: give --classic-game-dll (the 0.37 game.dll, from a new install)")
        if sha(classic_dll) != CLASSIC_GAME_DLL:
            raise SystemExit(f"{classic_dll} is not the 0.37 client (sha256 must be {CLASSIC_GAME_DLL})")
    elif kind != "classic":
        raise SystemExit(f"{source / GAME_DLL} is {kind}: not a client this tool knows (hash guard). Nothing was copied.")

    world_dir = (source / WORLD).parent

    def skip(folder: str, names: list[str]) -> list[str]:
        if Path(folder).resolve() == world_dir:
            return [n for n in names if n.startswith(WORLD.name)]   # the world goes through SQLite's backup below
        return []

    shutil.copytree(source, target, ignore=skip, symlinks=True)
    try:
        src = sqlite3.connect((source / WORLD).as_uri() + "?mode=ro", uri=True)
        dst = sqlite3.connect(target / WORLD)
        src.backup(dst)
        src.close()
        release.set_edition(dst, False)
        marks = ",".join("?" * len(release.SLUAGH_MOBS))
        mobs = dst.execute(f"DELETE FROM Mob WHERE Mob_ID IN ({marks})", release.SLUAGH_MOBS).rowcount
        specs = dst.execute("DELETE FROM ClassXSpecialization WHERE ClassID=63").rowcount
        dst.commit()
        assert dst.execute("PRAGMA quick_check").fetchone()[0] == "ok"
        dst.close()
        if kind == "sluaghbinder":
            shutil.copy2(classic_dll, target / GAME_DLL)
        after = status(target)
        if after != "EDITION enable_sluaghbinder=False game.dll=classic":
            raise SystemExit(f"the copy did not become the 0.37 edition: {after}")
    except BaseException:
        print(f"Stopped part-way; {target} is an unfinished copy (delete it before trying again).", file=sys.stderr)
        raise
    return {"copy": str(target), "status": after, "removed_mobs": mobs, "removed_class_specializations": specs,
            "client_replaced": kind == "sluaghbinder", "source_status": status(source), **custom_class_saves(target / WORLD)}


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="action", required=True)
    st = sub.add_parser("status", help="the game's edition, as the server and the client see it")
    st.add_argument("--game", required=True, type=Path)
    mk = sub.add_parser("make", help="a class-off copy of a test game, beside it")
    mk.add_argument("--from", dest="source", required=True, type=Path)
    mk.add_argument("--to", dest="target", required=True, type=Path)
    mk.add_argument("--classic-game-dll", type=Path, help="the 0.37 game.dll (a new install's runtime\\client-opendaoc\\app\\game.dll)")
    a = ap.parse_args(argv)
    if a.action == "status":
        print(status(a.game))
        if (a.game / WORLD).is_file():
            saves = custom_class_saves(a.game / WORLD)
            print(f"CUSTOM-CLASS characters={len(saves['characters'])} gamebots={len(saves['gamebots'])}")
            for name, account, cls in saves["characters"]:
                print(f"  character {name} (account {account}, class {cls})")
        return 0
    report = make(a.source, a.target, a.classic_game_dll)
    print(f"COPY {report['copy']}")
    print(report["status"])
    print(f"removed: {report['removed_mobs']} Sluaghbinder trainer/wisp, {report['removed_class_specializations']} class-63 "
          f"specialization rows; client replaced: {report['client_replaced']}")
    print(f"source unchanged: {report['source_status']}")
    for name, account, cls in report["characters"]:
        print(f"NOTE custom-class character in the copy: {name} (account {account}, class {cls}); play a classic class")
    if report["gamebots"]:
        print(f"NOTE {len(report['gamebots'])} custom-class gamebot(s) in the copy's roster (a 0.37 world has none): "
              + ", ".join(n for n, _ in report["gamebots"][:10]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
