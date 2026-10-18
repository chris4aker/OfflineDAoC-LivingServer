"""Build the Offline DAoC 0.36b playable package (plus the 0.36 edition files).

    python build_release_036.py --snapshot <1:1 copy of the CLAUDE VERSION folder>
                                --repo <this repository> --edition-game-dll <0.36 client without the Sluaghbinder patches>
                                --out <new staging folder>

The package is the CLAUDE VERSION 1:1 except for these, per the owner:
  * the player's own local account (no account.txt; the launcher makes one per install and the
    server creates it on the first ENTER REALM). No account, character or bot of the developer's ships.
  * the player's own bots (empty saved world; bots are created from the launcher),
  * default launcher settings (bot goals, GM off, 1x XP, zero-bot start, fresh client profile),
  * a DEFAULT world: every keep, battleground keep and relic with its original owner and level and every keep door
    whole (release-world-defaults.json), whatever the developer's test runs captured or broke,
  * no developer diagnostics or graphics experiments: no DXVK (d3d9.dll / dxvk.conf), no diagnostics switch files
    (*.on / *.off), no heap dumps, logs, backups or "before-" copies.
Builds that differ from the snapshot on purpose: GameServer.dll (edition switch) and the
launcher (portable account, per-install client profile, the 0.36c new-client mode). Everything else is copied byte for
byte and hash-verified. The snapshot is only read, never changed.

The 0.36 "no custom class" edition is two swap-in files under editions/: a database with
classes/enable_sluaghbinder = False (plus no Sluaghbinder trainer, wisp or class skill rows)
and a game.dll that is the 0.36b client without its two Sluaghbinder patches (class label, race links), so the
Hibernian Mauler slot is disabled as in v0.32 while the classic war map, red quest markers and the QUEST GUIDE
button work the same. It is the v0.32 client with patch_bounty_map_client.py, patch_quest_marker_range_client.py,
patch_classic_warmap_client.py and patch_quest_journal_button_client.py applied, in that order (the same file as 0.35).

World data checks (the release fails if one is off): the Darkness Falls raid spawns match
darkness_falls_raid_event_mobs.json exactly (Princess Nahemah, High Lord Saeor and High Lord Oro moved off their daises),
every quest kill target in set_quest_target_respawns.ledger.json respawns within 300 seconds, the relic pad needs 8,
and the navmesh road, seam and pocket files are present.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sqlite3
from datetime import datetime, timezone
from pathlib import Path

EDITION_GAME_DLL_SHA256 = "f55ed6b068e22ce8e1106871c2fad6ee10c18390bbb8b5dc772219ad8c8b83bb"
SLUAGH_GAME_DLL_SHA256 = "e1d471bb19108610ab9c8ca77dd41af40afe716685b03f4cdeda88c05678463b"
DOTNET_VERSION = "10.0.11"
CLIENT_PROFILE = "OfflineDAoC036"
SLUAGH_MOBS = ("sluaghbinder_trainer_tir_na_nog", "sluaghbinder_bound_wisp_tir_na_nog")
RELIC_PAD_REQUIREMENT = "8"
QUEST_RESPAWN_SECONDS = 300

# Personal or run-state files that are never part of a public world.
EXCLUDED_DIRS = {"logs", "backups", "dxvk-staging", "deployment-backups", "keep-relic-reset-backups", "progress-backups",
                 "screenshots", ".git", "testresults", "dumps"}
EXCLUDED_NAMES = {"account.txt", "rvr-world.json", "bot-goals.json", "bot-world.json", "bot-world.request", "bot-ai-delay.json",
                  "realm-events.request.json", "realm-events.result.json", "realm-event-records.sqlite3",
                  "realm-event-records.sqlite3-wal", "realm-event-records.sqlite3-shm", "errorlog.txt",
                  "debug.log", "chat.log", "user.dat", "unins000.exe", "unins000.dat", "uninstdaoc.exe",
                  # DXVK stays in the developer's own install only (owner 2026-10-14)
                  "d3d9.dll", "dxvk.conf", "game.dll_d3d9.log",
                  # diagnostics switches (their code paths stay off without these files)
                  "memory-census.on", "gc-snapshots.on", "gc-diagnostics.off"}
EXCLUDED_SUFFIXES = (".log", ".dmp", ".gcdump", ".bak", ".sqlite3.db", ".sqlite3.db-wal", ".sqlite3.db-shm",
                     ".sqlite3.db-journal")
# Must never be anywhere in the runtime that ships (checked after the copy).
FORBIDDEN_SUFFIXES = (".on", ".off", ".dmp", ".gcdump", ".log")
FORBIDDEN_NAMES = {"d3d9.dll", "dxvk.conf", "account.txt", "claude_test_account.txt", "dev_mode.txt"}
REQUIRED_FILES = ("server/navmesh/roads.json", "server/navmesh/seams.json", "server/navmesh/pockets.json",
                  "server/GameServer.dll", "server/CoreServer.exe", "client-opendaoc/app/game.dll", "OfflineDAoC.exe")


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def include(relative: Path) -> bool:
    parts = [part.lower() for part in relative.parts]
    if any(part in EXCLUDED_DIRS or "before-" in part or "pre-route-" in part for part in parts[:-1]):
        return False
    name = parts[-1]
    if parts[0] == "data":
        return False                      # the world database is rebuilt below
    return name not in EXCLUDED_NAMES and "before-" not in name and not name.endswith(EXCLUDED_SUFFIXES)


def copy_tree(source: Path, target: Path, keep=lambda rel: True) -> dict[str, str]:
    copied = {}
    for path in sorted(source.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if not keep(relative):
            continue
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
        copied[relative.as_posix()] = sha(path)
    return copied


def check_runtime_files(runtime: Path):
    """Nothing personal, diagnostic or experimental ships; the navigation data does."""
    bad = [p.relative_to(runtime).as_posix() for p in runtime.rglob("*") if p.is_file() and (
        p.name.lower() in FORBIDDEN_NAMES or p.name.lower().endswith(FORBIDDEN_SUFFIXES) or "before-" in p.name.lower())]
    assert not bad, f"files that must not ship: {bad[:20]}"
    missing = [name for name in REQUIRED_FILES if not (runtime / name).is_file()]
    assert not missing, f"required files missing: {missing}"


def clean_world(database: Path, policy: dict, defaults: dict) -> dict:
    """Empty every saved/progress table and restore the default world and launcher settings."""
    con = sqlite3.connect(database)
    con.execute("PRAGMA foreign_keys=OFF")
    tables = {row[0] for row in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
    cleared = []
    extra = ["FactionAggroLevel"]   # per-character standings (clean_slate.py clears them too)
    for table in policy["ProgressTables"] + policy["ClearTables"] + extra:
        if table in tables and table not in {"Guild", "GuildRank"}:
            con.execute(f'DELETE FROM "{table}"')
            cleared.append(table)
    # Starting guild definitions and ranks are world data; their earned state is not.
    con.execute("UPDATE Guild SET RealmPoints=0,BountyPoints=0,Bank=0,MeritPoints=0,Webpage='',Email='',"
                "Motd='',oMotd='',HaveGuildHouse=0,GuildHouseNumber=0")
    con.execute("UPDATE DBHouse SET OwnerID='',GuildName='',GuildHouse=0,HasConsignment=0,KeptMoney=0,Model=0,Name='' "
                "WHERE COALESCE(OwnerID,'')<>''")
    # The default frontier: every keep (frontier, battleground, portal) with its original owner and level and no claim,
    # every keep door whole, every relic home - whatever the developer's runs captured or broke.
    for keep_id, keep in defaults["Keep"].items():
        changed = con.execute("UPDATE Keep SET Realm=?,Level=?,ClaimedGuildName='' WHERE KeepID=?",
                              (keep["Realm"], keep["Level"], int(keep_id))).rowcount
        assert changed == 1, f"keep {keep_id} {keep['Name']}"
    for door_id, door in defaults["Door"].items():
        changed = con.execute("UPDATE Door SET Health=?,State=? WHERE Door_ID=?",
                              (door["Health"], door["State"], door_id)).rowcount
        assert changed == 1, f"door {door_id}"
    con.execute("UPDATE Relic SET Realm=OriginalRealm,LastRealm=OriginalRealm WHERE OriginalRealm IN (1,2,3)")
    con.execute("UPDATE ServerProperty SET Value='1' WHERE lower(`Key`) IN ('xp_rate','bot_xp_rate')")
    con.execute("UPDATE ServerProperty SET Value='' WHERE lower(`Key`) IN ('xp_rate_by_level','bot_xp_rate_by_level')")
    con.execute("UPDATE ServerProperty SET Value='True' WHERE lower(`Key`)='allow_auto_account_creation'")
    con.execute("INSERT OR REPLACE INTO offline_local_options(Key,Value) VALUES('MakeMeGM','false')")
    # Public zero-bot start: the player creates bots from the launcher buttons.
    con.execute("UPDATE offline_population_settings SET Value='0' WHERE Key='ActiveTarget'")
    con.execute("UPDATE offline_population_settings SET Value='true' WHERE Key='PopulationEnabled'")
    con.execute("UPDATE offline_population_settings SET Value='15',Description='First third within five minutes; "
                "full requested roster by fifteen minutes.' WHERE Key='StartupRampMinutes'")
    con.execute("UPDATE offline_runtime_status SET ServerState='Stopped',ServerPid=0,ActiveBots=0,WarmBots=0,"
                "ColdBots=0,ServerMemoryMb=0,TickP95Ms=0,AiWorkQueue=0") if "offline_runtime_status" in tables else None
    set_edition(con, True)
    for table in cleared:
        con.execute("DELETE FROM sqlite_sequence WHERE name=?", (table,)) if "sqlite_sequence" in tables else None
    for stat in ("sqlite_stat1", "sqlite_stat4"):
        if stat in tables:
            con.execute(f"DELETE FROM {stat}")
    con.commit()
    con.execute("PRAGMA journal_mode=DELETE")
    con.execute("VACUUM")
    con.execute("ANALYZE")
    con.commit()
    report = verify_clean(con, policy, defaults)
    con.close()
    return report


def set_edition(con: sqlite3.Connection, enabled: bool):
    value = "True" if enabled else "False"
    columns = [row[1] for row in con.execute("PRAGMA table_info(ServerProperty)")]
    exists = con.execute("SELECT COUNT(*) FROM ServerProperty WHERE `Key`='enable_sluaghbinder'").fetchone()[0]
    if exists:
        con.execute("UPDATE ServerProperty SET Value=? WHERE `Key`='enable_sluaghbinder'", (value,))
        return
    row = {"Category": "classes", "Key": "enable_sluaghbinder",
           "Description": "Allow the custom Hibernian Sluaghbinder class (player creation, autonomous bots and helpers). "
                          "False leaves only the Classic + SI classes.",
           "DefaultValue": "True", "Value": value,
           "LastTimeRowUpdated": datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M:%S"),
           "ServerProperty_ID": "enable_sluaghbinder"}
    names = [name for name in columns if name in row]
    con.execute(f"INSERT INTO ServerProperty ({','.join('`'+n+'`' for n in names)}) VALUES ({','.join('?'*len(names))})",
                [row[name] for name in names])


def verify_clean(con: sqlite3.Connection, policy: dict, defaults: dict) -> dict:
    assert con.execute("PRAGMA quick_check").fetchone()[0] == "ok"
    for table in policy["ProgressTables"] + policy["ClearTables"] + ["FactionAggroLevel"]:
        if table in {"Guild", "GuildRank", "offline_local_options", "offline_runtime_status"}:
            continue
        exists = con.execute("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone()[0]
        if exists:
            assert con.execute(f'SELECT COUNT(*) FROM "{table}"').fetchone()[0] == 0, table
    assert con.execute("SELECT COUNT(*) FROM Account").fetchone()[0] == 0, "an account would ship"
    assert con.execute("SELECT COUNT(*) FROM DOLCharacters").fetchone()[0] == 0, "a character would ship"
    assert con.execute("SELECT COUNT(*) FROM offline_world_bots").fetchone()[0] == 0, "a bot would ship"
    settings = dict(con.execute("SELECT Key,Value FROM offline_population_settings"))
    assert settings["ActiveTarget"] == "0" and settings["PopulationEnabled"] == "true", settings
    assert settings["StartupRampMinutes"] == "15", settings
    assert con.execute("SELECT Value FROM offline_local_options WHERE Key='MakeMeGM'").fetchone()[0] == "false"
    assert con.execute("SELECT Value FROM ServerProperty WHERE lower(`Key`)='allow_auto_account_creation'").fetchone()[0] == "True"
    assert con.execute("SELECT COUNT(*) FROM ServerProperty WHERE lower(`Key`) IN ('xp_rate_by_level','bot_xp_rate_by_level') "
                       "AND Value<>''").fetchone()[0] == 0, "XP rates by level must be off in a public package"
    keeps = {str(k): (realm, level, claim) for k, realm, level, claim in
             con.execute("SELECT KeepID,Realm,Level,COALESCE(ClaimedGuildName,'') FROM Keep")}
    assert set(keeps) == set(defaults["Keep"]), "the keep list differs from release-world-defaults.json"
    for keep_id, keep in defaults["Keep"].items():
        assert keeps[keep_id] == (keep["Realm"], keep["Level"], ""), f"keep {keep_id} {keep['Name']}: {keeps[keep_id]}"
    doors = {d: (h, s) for d, h, s in con.execute("SELECT Door_ID,Health,State FROM Door")}
    assert set(doors) == set(defaults["Door"]), "the door list differs from release-world-defaults.json"
    for door_id, door in defaults["Door"].items():
        assert doors[door_id] == (door["Health"], door["State"]), f"door {door_id}"
    assert con.execute("SELECT COUNT(*) FROM Relic WHERE Realm<>OriginalRealm OR LastRealm<>OriginalRealm").fetchone()[0] == 0
    return {"quick_check": "ok", "population": settings, "keeps": len(keeps), "doors": len(doors),
            "enable_sluaghbinder": con.execute("SELECT Value FROM ServerProperty WHERE `Key`='enable_sluaghbinder'").fetchone()[0]}


def check_world_data(database: Path, repo: Path) -> dict:
    """The raid and quest data fixes made outside the C# code ship with the world."""
    con = sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)
    manifest = json.loads((repo / "source/server/GameServer/bots/autonomous/data/darkness_falls_raid_event_mobs.json")
                          .read_text(encoding="utf-8"))
    for row in manifest["rows"]:
        spawn = con.execute("SELECT X,Y,Z FROM Mob WHERE Mob_ID=?", (row["id"],)).fetchone()
        assert spawn is not None and list(spawn) == row["spawn"], \
            f"Darkness Falls raid spawn {row['name']} ({row['id']}) does not match the raid manifest: {spawn}"
    ledger = json.loads((repo / "tools/claude-version/set_quest_target_respawns.ledger.json").read_text(encoding="utf-8"))
    slow = [mob for mob in ledger["rows"]
            if (con.execute("SELECT RespawnInterval FROM Mob WHERE Mob_ID=?", (mob,)).fetchone() or [10**9])[0] > QUEST_RESPAWN_SECONDS]
    assert not slow, f"quest kill targets respawning slower than {QUEST_RESPAWN_SECONDS} s: {slow[:10]}"
    pad = con.execute("SELECT Value FROM ServerProperty WHERE `Key`='relic_players_required_on_pad'").fetchone()
    assert pad is not None and pad[0] == RELIC_PAD_REQUIREMENT, f"relic pad requirement {pad}"
    con.close()
    return {"darkness_falls_raid_spawns": len(manifest["rows"]), "quest_respawn_rows": len(ledger["rows"]),
            "relic_players_required_on_pad": pad[0]}


def no_custom_class_database(source: Path, target: Path) -> dict:
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)
    con = sqlite3.connect(target)
    set_edition(con, False)
    removed_mobs = con.execute(f"DELETE FROM Mob WHERE Mob_ID IN ({','.join('?'*len(SLUAGH_MOBS))})", SLUAGH_MOBS).rowcount
    removed_specs = con.execute("DELETE FROM ClassXSpecialization WHERE ClassID=63").rowcount
    con.commit()
    con.execute("VACUUM")
    con.commit()
    assert con.execute("PRAGMA quick_check").fetchone()[0] == "ok"
    assert con.execute("SELECT Value FROM ServerProperty WHERE `Key`='enable_sluaghbinder'").fetchone()[0] == "False"
    assert removed_mobs == len(SLUAGH_MOBS), removed_mobs
    con.close()
    return {"removed_mobs": removed_mobs, "removed_class_specializations": removed_specs}


def set_auto_account_creation(config: Path):
    text = config.read_text(encoding="utf-8-sig")
    old = "<AutoAccountCreation>False</AutoAccountCreation>"
    if old in text:
        config.write_text(text.replace(old, "<AutoAccountCreation>True</AutoAccountCreation>"), encoding="utf-8")
    assert "<AutoAccountCreation>True</AutoAccountCreation>" in config.read_text(encoding="utf-8-sig")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--snapshot", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--edition-game-dll", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    snapshot, repo, out = args.snapshot.resolve(), args.repo.resolve(), args.out.resolve()
    if out.exists():
        raise SystemExit(f"{out} exists; refusing to overwrite a staging folder")
    if sha(args.edition_game_dll) != EDITION_GAME_DLL_SHA256:
        raise SystemExit("The edition game.dll is not the patched client without the Sluaghbinder patches")
    live_runtime = snapshot / "runtime"
    if sha(live_runtime / "client-opendaoc/app/game.dll") != SLUAGH_GAME_DLL_SHA256:
        raise SystemExit("The snapshot client is not the Sluaghbinder client this release expects")
    policy = json.loads((repo / "source/tools/OfflineDaoc.ProgressImport/progress-policy.json").read_text())
    defaults = json.loads((repo / "source/tools/release-world-defaults.json").read_text(encoding="utf-8"))

    print("Copying the runtime (personal, diagnostic and run-state files excluded)...", flush=True)
    runtime = out / "runtime"
    copied = copy_tree(live_runtime, runtime, include)

    print("Building the clean world database...", flush=True)
    database = runtime / "data/opendaoc.sqlite3.db"
    database.parent.mkdir(parents=True, exist_ok=True)
    with sqlite3.connect((live_runtime / "data/opendaoc.sqlite3.db").as_uri() + "?mode=ro", uri=True) as src, \
            sqlite3.connect(database) as dst:
        src.backup(dst)
    world = clean_world(database, policy, defaults)
    world_data = check_world_data(database, repo)

    set_auto_account_creation(runtime / "server/config/serverconfig.xml")
    (runtime / "client-opendaoc/app/paths.dat").write_bytes(f"[paths]\r\nsettings={CLIENT_PROFILE}".encode("ascii"))

    print("Installing the release server and launcher builds...", flush=True)
    server_build = repo / "source/server/Release/lib"
    replaced = {}
    for folder in ("server", "server/lib", "server/win-x64"):
        for name in ("GameServer.dll", "GameServer.pdb"):
            target = runtime / folder / name
            if target.exists():
                shutil.copy2(server_build / name, target)
                replaced[f"{folder}/{name}"] = sha(target)
    launcher_build = repo / "source/tools/OfflineDaoc.Launcher/bin/Release/net10.0-windows"
    for name in ("OfflineDAoC.dll", "OfflineDAoC.exe", "OfflineDAoC.pdb", "OfflineDAoC.deps.json",
                 "OfflineDAoC.runtimeconfig.json"):
        shutil.copy2(launcher_build / name, runtime / name)
        replaced[name] = sha(runtime / name)
    check_runtime_files(runtime)

    print("Bundling the .NET runtime...", flush=True)
    dotnet = Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "dotnet"
    bundled = out / "tools/dotnet"
    for sub in (f"host/fxr/{DOTNET_VERSION}", f"shared/Microsoft.NETCore.App/{DOTNET_VERSION}",
                f"shared/Microsoft.WindowsDesktop.App/{DOTNET_VERSION}", f"shared/Microsoft.AspNetCore.App/{DOTNET_VERSION}"):
        copy_tree(dotnet / sub, bundled / sub)
    for name in ("dotnet.exe", "LICENSE.txt", "ThirdPartyNotices.txt"):
        if (dotnet / name).exists():
            shutil.copy2(dotnet / name, bundled / name)

    # The progress importer is added by assemble_release_036.py, with the docs and source.

    print("Building the 0.36 no-custom-class edition files...", flush=True)
    edition = out / "editions/0.36-no-custom-class"
    no_class = no_custom_class_database(database, edition / "runtime/data/opendaoc.sqlite3.db")
    (edition / "runtime/client-opendaoc/app").mkdir(parents=True, exist_ok=True)
    shutil.copy2(args.edition_game_dll, edition / "runtime/client-opendaoc/app/game.dll")

    report = {
        "built_utc": datetime.now(timezone.utc).isoformat(),
        "snapshot": str(snapshot),
        "runtime_files_copied": len(copied),
        "world": world,
        "world_data": world_data,
        "no_custom_class_edition": no_class,
        "replaced_on_purpose": replaced,
        "copied_hashes": copied,
    }
    (out.parent / f"{out.name}-build-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k != "copied_hashes"}, indent=2), flush=True)


if __name__ == "__main__":
    main()
