"""List everything that has changed since a released base (0.36 / 0.36b / 0.36c), as input for the next patch.

    python find_changes_since_release.py --baseline 0.36 --repo <this repository>
        --dev <the development install, e.g. "new class test - CLAUDE VERSION">
        --client <the new client's development folder, e.g. "OFFLINE DAOC MMOENGINE GODOT">
        [--world-db <a clean world database to compare, default: the dev install's live database>]
        [--out <report.json>]

Reads only; changes nothing. It reports:
  1. server source: commits and uncommitted files in the dev install's server repository since the release's commit;
  2. runtime files the game package ships (same rules as build_release_036.py) that differ from the released package -
     changed, added and removed (the server and launcher builds are rebuilt from source, so they are listed apart);
  3. the new client: files that differ from the released 0.36c package (same rules as build_newclient_036.py), and
     whether C# changed (the network client must then be rebuilt);
  4. this repository since the release tag (launcher, importer, tools, docs);
  5. world data: per database table, rows added / changed / removed against the released clean world database.
     Save tables and live state (accounts, characters, bots, keeps, doors, relics, run state) are left out. A changed
     table means a database fix to ship as an update script that touches only those world rows.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import sqlite3
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import build_newclient_036 as newclient  # noqa: E402  (keep_godot: what the 0.36c package ships)
import build_release_036 as release      # noqa: E402  (include: which runtime files the game package ships)

REBUILT = {"server/GameServer.dll", "server/GameServer.pdb", "server/lib/GameServer.dll", "server/lib/GameServer.pdb",
           "server/win-x64/GameServer.dll", "server/win-x64/GameServer.pdb", "OfflineDAoC.dll", "OfflineDAoC.exe",
           "OfflineDAoC.pdb", "OfflineDAoC.deps.json", "OfflineDAoC.runtimeconfig.json"}
LIVE_STATE_TABLES = {"Keep", "Door", "Relic", "offline_runtime_status", "sqlite_sequence", "sqlite_stat1", "sqlite_stat4",
                     "offline_population_settings", "offline_local_options"}   # (the release sets these to its defaults)
# Columns the running game rewrites by itself: every save time, zone bonuses, and the stats boss scripts re-roll.
LIVE_COLUMNS = {"LastTimeRowUpdated"}
LIVE_COLUMNS_BY_TABLE = {"Zones": {"Experience", "Realmpoints", "Bountypoints", "Coin"}}
# Rows the release build writes itself (edition switch, XP, account creation).
RELEASE_ROWS = {"ServerProperty": {"enable_sluaghbinder", "xp_rate", "bot_xp_rate", "allow_auto_account_creation"}}


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def manifest(path: Path) -> dict[str, str]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            digest, name = line.split("  ", 1)
            out[name] = digest
    return out


def git(repo: Path, *args) -> str:
    return subprocess.run(["git", *args], cwd=repo, capture_output=True, text=True).stdout.strip()


def compare_tree(files: dict[str, Path], expected: dict[str, str], skip=frozenset()) -> dict:
    changed, added = [], []
    for rel, path in sorted(files.items()):
        if rel in skip:
            continue
        if rel not in expected:
            added.append(rel)
        elif sha(path) != expected[rel]:
            changed.append(rel)
    removed = sorted(rel for rel in expected if rel not in files and rel not in skip)
    return {"changed": changed, "added": added, "removed": removed}


def runtime_files(runtime: Path) -> dict[str, Path]:
    out = {}
    for dirpath, _, names in os.walk(runtime):
        for name in names:
            path = Path(dirpath) / name
            rel = path.relative_to(runtime)
            if release.include(rel):
                out[rel.as_posix()] = path
    return out


def client_files(godot: Path) -> dict[str, Path]:
    out = {}
    for dirpath, _, names in os.walk(godot):
        for name in names:
            path = Path(dirpath) / name
            rel = path.relative_to(godot)
            if newclient.keep_godot(rel):
                out[rel.as_posix()] = path
    return out


def table_rows(con: sqlite3.Connection, table: str) -> tuple[list[str], dict]:
    cols = con.execute(f'PRAGMA table_info("{table}")').fetchall()
    keys = [c[1] for c in sorted(cols, key=lambda c: c[5]) if c[5]] or ["rowid"]
    skip = LIVE_COLUMNS | LIVE_COLUMNS_BY_TABLE.get(table, set())
    names = [c[1] for c in cols if c[1] not in skip]
    key_sql = ", ".join(f'"{k}"' if k != "rowid" else "rowid" for k in keys)
    out = {}
    for row in con.execute(f'SELECT {key_sql}, ' + ", ".join(f'"{n}"' for n in names) + f' FROM "{table}"'):
        key = tuple(row[:len(keys)])
        if len(key) == 1 and str(key[0]).lower() in RELEASE_ROWS.get(table, set()):
            continue
        out[key] = row[len(keys):]
    return names, out


def compare_world(baseline_db: Path, current_db: Path, policy: dict) -> dict:
    skip = (set(policy["ProgressTables"]) | set(policy.get("OptionalProgressTables", [])) | set(policy["ClearTables"]) |
            {"FactionAggroLevel"} | LIVE_STATE_TABLES)
    ro = lambda p: sqlite3.connect(p.resolve().as_uri() + "?mode=ro", uri=True)
    with ro(baseline_db) as base, ro(current_db) as cur:
        tables = {r[0] for r in base.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        current = {r[0] for r in cur.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        report = {"tables_added": sorted(current - tables), "tables_removed": sorted(tables - current), "tables": {}}
        for table in sorted(tables & current):
            if table in skip:
                continue
            names, a = table_rows(base, table)
            names_b, b = table_rows(cur, table)
            if names != names_b:
                report["tables"][table] = {"columns_changed": sorted(set(names) ^ set(names_b))}
                continue
            added = sum(1 for k in b if k not in a)
            removed = sum(1 for k in a if k not in b)
            changed_keys = [k for k in a.keys() & b.keys() if a[k] != b[k]]
            if added or removed or changed_keys:
                columns = {}
                for k in changed_keys:
                    for i, name in enumerate(names):
                        if a[k][i] != b[k][i]:
                            columns[name] = columns.get(name, 0) + 1
                entry = {"added": added, "changed": len(changed_keys), "removed": removed,
                         "changed_columns": dict(sorted(columns.items(), key=lambda kv: -kv[1])[:12])}
                if table == "Mob" and changed_keys and "ClassType" in names:
                    by_class = {}
                    for k in changed_keys:
                        cls = b[k][names.index("ClassType")]
                        by_class[cls] = by_class.get(cls, 0) + 1
                    # boss scripts that re-roll their adds' stats while the game runs show up here; review the rest
                    entry["changed_by_class"] = dict(sorted(by_class.items(), key=lambda kv: -kv[1])[:15])
                report["tables"][table] = entry
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--baseline", default="0.36")
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--dev", type=Path, required=True)
    parser.add_argument("--client", type=Path, required=True)
    parser.add_argument("--world-db", type=Path)
    parser.add_argument("--baseline-db", type=Path, help="default: <repo>/../release-baselines/<baseline>/world-<baseline>b.sqlite3.db")
    parser.add_argument("--out", type=Path)
    parser.add_argument("--no-files", action="store_true", help="skip hashing the runtime and new client folders (quick)")
    args = parser.parse_args()
    repo = args.repo.resolve()
    base_dir = repo / "source/tools/release-baselines" / args.baseline
    info = json.loads((base_dir / "baseline.json").read_text(encoding="utf-8"))
    report = {"baseline": info["release"]}

    server_repo = args.dev / "development-source/server"
    since = info["server_source_commit"]
    report["server_commits"] = git(server_repo, "log", "--format=%h %ci %s", f"{since}..HEAD").splitlines()
    report["server_uncommitted"] = [l for l in git(server_repo, "status", "--short").splitlines()
                                    if not l.startswith("??") and l.strip().endswith(".cs")]

    empty = {"changed": [], "added": [], "removed": [], "skipped": True}
    game = manifest(base_dir / info["game_package_manifest"])
    shipped_runtime = {k[len("runtime/"):]: v for k, v in game.items() if k.startswith("runtime/")}
    files = {} if args.no_files else runtime_files(args.dev / "runtime")
    runtime = dict(empty) if args.no_files else compare_tree(files, shipped_runtime, skip=REBUILT | {"data/opendaoc.sqlite3.db"})
    # files only the release build adds (tools/dotnet lives outside runtime; paths.dat and serverconfig are rewritten)
    runtime["changed"] = [r for r in runtime["changed"] if r not in ("client-opendaoc/app/paths.dat", "server/config/serverconfig.xml")]
    report["runtime"] = runtime

    client = manifest(base_dir / info["new_client_package_manifest"])
    shipped_godot = {k[len("new-client/godot/"):]: v for k, v in client.items() if k.startswith("new-client/godot/")}
    godot = dict(empty) if args.no_files else compare_tree(client_files(args.client / "godot"), shipped_godot)
    godot["csharp_changed"] = any(r.endswith(".cs") for r in godot["changed"] + godot["added"])
    report["new_client"] = godot

    # database update scripts the server chats added or changed after the release (how world fixes are made)
    published = info["published_utc"].replace("T", " ").rstrip("Z")
    from datetime import datetime, timezone
    cutoff = datetime.fromisoformat(published).replace(tzinfo=timezone.utc).timestamp()
    scripts = args.dev / "tools/claude-version"
    report["db_scripts_since_release"] = sorted(
        p.relative_to(scripts).as_posix() for p in scripts.rglob("*")
        if p.is_file() and p.suffix in (".py", ".json", ".sql") and "__pycache__" not in p.parts
        and "runs" not in p.relative_to(scripts).parts[:1] and p.stat().st_mtime > cutoff)

    tag = info["repo_commit"]
    report["repo_commits"] = git(repo, "log", "--format=%h %ci %s", f"{tag}..origin/main").splitlines()
    report["repo_files"] = git(repo, "diff", "--name-only", tag, "origin/main").splitlines()

    baseline_db = args.baseline_db or repo.parent / "release-baselines" / args.baseline / f"world-{args.baseline}b.sqlite3.db"
    world_db = args.world_db or args.dev / "runtime/data/opendaoc.sqlite3.db"
    policy = json.loads((repo / "source/tools/DAoCLivingServer.ProgressImport/progress-policy.json").read_text(encoding="utf-8"))
    report["world_data"] = compare_world(baseline_db, world_db, policy)

    text = json.dumps(report, indent=2)
    if args.out:
        args.out.write_text(text + "\n", encoding="utf-8")
    print(f"server: {len(report['server_commits'])} commits, {len(report['server_uncommitted'])} uncommitted .cs files")
    for name in ("runtime", "new_client"):
        part = report[name]
        print(f"{name}: {len(part['changed'])} changed, {len(part['added'])} added, {len(part['removed'])} removed")
    print(f"new client C# changed: {godot['csharp_changed']}")
    print(f"repo: {len(report['repo_commits'])} commits since the release tag")
    print(f"database update scripts since the release: {len(report['db_scripts_since_release'])}")
    print(f"world data: {len(report['world_data']['tables'])} tables differ from the released clean world")
    if not args.out:
        print(text)


if __name__ == "__main__":
    main()
