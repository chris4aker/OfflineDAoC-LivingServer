"""Build the update patches (from 0.37 on) that bring a released 0.36-family folder up to a new version.

    python make_patch.py --version 0.37 --repo <this repository>
        --game <reference game package, e.g. staging-037/OfflineDAoC-v0.37b (built + assembled, not sealed)>
        --client <reference new client package, e.g. staging-037/OfflineDAoC-v0.37c>
        --baseline-dbs <folder with the released clean world databases: world-0.36b.sqlite3.db, world-0.36.sqlite3.db>
        --out <new assets folder>   (writes v<version>b/, v<version>/, v<version>c/)

The reference packages are what a fresh install of the new version would be. A patch is the difference against the
released base (source/tools/release-baselines/<base>/):
  game patch     every changed, new and removed file of the game package (server, data, launcher, progress importer,
                 tools, docs, source, start files) - the editions\\ folder is removed: its files are only used while
                 installing the base - plus the world-data update for each edition (db-<version>b.json, db-<version>.json)
  client patch   every changed, new and removed file of the new client (new-client\\ and its start files)
Each patch carries the patcher (OfflineDaoc.Patcher, run with the game's bundled .NET), which checks, backs up,
applies and rolls back. World rows are compared ignoring volatile columns; save tables are never in an update.

Both patches are packed into independent, verified zip parts below GitHub's 2 GB limit, and the download manifests the
downloader reads are written for the three releases (v<version>b and v<version> share the game patch, v<version>c adds
the client patch; a new install downloads the base release first and then patches it).
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import shutil
import sqlite3
import subprocess
import tempfile
import zipfile
from pathlib import Path

SEVEN_ZIP = Path(r"C:\Program Files\7-Zip\7z.exe")
PART_BUDGET = 1_850_000_000
IGNORE_COLUMNS = ["LastTimeRowUpdated"]
# Columns the running game rewrites (rotating zone bonuses) never go into an update, and tables that only hold run
# state are left out entirely.
LIVE_COLUMNS = {"Zones": {"Experience", "Realmpoints", "Bountypoints", "Coin"}}
LIVE_TABLES = {"TaskRefreshIntervals", "offline_runtime_status"}
GAME_SKIP_PREFIXES = ("editions/", "patch-backups/")
GAME_SKIP_FILES = {"runtime/data/opendaoc.sqlite3.db", "PACKAGE MANIFEST.sha256"}
CLIENT_SKIP_PREFIXES = ("new-client/save/", "patch-backups/")
CLIENT_SKIP_FILES = {"new-client/PACKAGE MANIFEST.sha256"}


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def read_manifest(path: Path) -> dict[str, str]:
    out = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.strip():
            digest, name = line.split("  ", 1)
            out[name] = digest
    return out


def package_files(root: Path, skip_prefixes, skip_files) -> dict[str, Path]:
    out = {}
    for path in root.rglob("*"):
        if path.is_file():
            rel = path.relative_to(root).as_posix()
            if rel in skip_files or rel.startswith(skip_prefixes):
                continue
            out[rel] = path
    return out


# ------------------------------------------------------------------------------------------------- world data
def json_value(value):
    if isinstance(value, bytes):
        return {"$b64": base64.b64encode(value).decode("ascii")}
    return value


def table_info(con, table):
    return con.execute(f'PRAGMA table_info("{table}")').fetchall()


def world_update(base_db: Path, target_db: Path, policy: dict, version: str) -> dict:
    """The rows a released world gains, changes and loses between two clean builds, per world table."""
    skip = set(policy["ProgressTables"]) | set(policy["ClearTables"]) | {"FactionAggroLevel", "offline_patch_log"} | LIVE_TABLES
    ro = lambda p: sqlite3.connect(p.resolve().as_uri() + "?mode=ro", uri=True)
    base, target = ro(base_db), ro(target_db)
    master = lambda con: {r[0]: r[1] for r in con.execute("SELECT name, sql FROM sqlite_master WHERE type='table'")
                          if not r[0].startswith("sqlite_")}
    base_tables, target_tables = master(base), master(target)
    update = {"Version": version, "IgnoreColumns": IGNORE_COLUMNS, "NewTables": [], "NewColumns": [], "Tables": [],
              "Summary": {}}
    indexes = lambda table: [r[0] for r in target.execute(
        "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name=? AND sql IS NOT NULL ORDER BY name", (table,))]

    def schema_additions(table, info):
        """A table or columns new in this version are added as a fresh install has them; returns the base columns."""
        if table not in base_tables:
            update["NewTables"].append({"Table": table, "Create": target_tables[table], "Indexes": indexes(table)})
            return []
        base_cols = [c[1] for c in table_info(base, table)]
        for c in info:
            if c[1] not in base_cols:
                decl = (c[2] or "") + (f" DEFAULT {c[4]}" if c[4] is not None else "")
                update["NewColumns"].append({"Table": table, "Column": c[1], "Decl": decl.strip()})
        return base_cols

    for table in sorted(target_tables):
        if table in skip:
            # Save and run-state tables never get rows from an update, but a new one is created (empty), so the updated
            # world has every table a fresh install has (the progress importer needs them all).
            if table != "offline_patch_log":
                schema_additions(table, table_info(target, table))
            continue
        live = LIVE_COLUMNS.get(table, set())          # never compared, never written
        info = [c for c in table_info(target, table) if c[1] not in live]
        cols = [c[1] for c in info]
        keys = [c[1] for c in sorted((c for c in info if c[5]), key=lambda c: c[5])]
        base_cols = schema_additions(table, info)
        compare = [i for i, name in enumerate(cols) if name not in IGNORE_COLUMNS and (name in base_cols or not base_cols)]
        ignore_compare = [name for name in cols if name not in base_cols] if base_cols else []
        key_idx = [cols.index(k) for k in keys] if keys else compare

        def rows(con, available):
            sel = ", ".join(f'"{c}"' if c in available else "NULL" for c in cols)
            out, seen = {}, {}
            for row in con.execute(f'SELECT {sel} FROM "{table}"'):
                key = tuple(row[i] for i in key_idx)
                if not keys:                       # no primary key: rows are their own identity (counted duplicates)
                    seen[key] = seen.get(key, 0) + 1
                    key = key + (seen[key],)
                out[key] = row
            return out

        new = rows(target, set(cols))
        old = rows(base, set(base_cols)) if base_cols else {}
        same = lambda a, b: all(a[i] == b[i] for i in compare)
        inserts = [new[k] for k in new if k not in old]
        updates = [(old[k], new[k]) for k in new if k in old and not same(old[k], new[k])]
        deletes = [old[k] for k in old if k not in new]
        if not (inserts or updates or deletes) and table in base_tables:
            continue
        # A table with a primary key gets field updates: only the fields this version changes, applied where the
        # player's row still has the released value of those fields. The running server rewrites some fields itself
        # (monster size, level and stats are re-rolled), so a whole-row match would skip real fixes on played worlds.
        changed_fields = lambda o, n: [cols[i] for i in compare if o[i] != n[i]]
        entry = {"Table": table, "Columns": cols, "Key": keys or [cols[i] for i in compare],
                 "IgnoreCompare": ignore_compare, "ByKey": bool(keys),
                 "Insert": [[json_value(v) for v in r] for r in inserts],
                 "Update": [{"Old": [json_value(v) for v in o], "New": [json_value(v) for v in n],
                             **({"Set": changed_fields(o, n)} if keys else {})} for o, n in updates],
                 "Delete": [[json_value(v) for v in r] for r in deletes]}
        update["Tables"].append(entry)
        update["Summary"][table] = {"added": len(inserts), "changed": len(updates), "removed": len(deletes)}
    return update


def check_world_update(update: dict, base_db: Path, target_db: Path, policy: dict):
    """Applying the update to the released world must give exactly the new clean world (the test a patched fresh
    install must pass), using the same statements the patcher runs."""
    with tempfile.TemporaryDirectory() as tmp:
        work = Path(tmp) / "world.db"
        shutil.copy2(base_db, work)
        con = sqlite3.connect(work)
        for t in update["NewTables"]:
            con.execute(t["Create"])
            for index in t.get("Indexes", []):
                con.execute(index)
        for c in update["NewColumns"]:
            con.execute(f'ALTER TABLE "{c["Table"]}" ADD COLUMN "{c["Column"]}" {c["Decl"]}')
        decode = lambda v: base64.b64decode(v["$b64"]) if isinstance(v, dict) else v
        kept = 0
        for t in update["Tables"]:
            cols, table = t["Columns"], t["Table"]
            cmp = [i for i, n in enumerate(cols) if n not in update["IgnoreColumns"] and n not in t["IgnoreCompare"]]
            where = " AND ".join(f'"{cols[i]}" IS ?' for i in cmp)
            for row in t["Insert"]:
                cur = con.execute(f'INSERT OR IGNORE INTO "{table}" ({",".join(chr(34) + c + chr(34) for c in cols)}) '
                                  f'VALUES ({",".join("?" * len(cols))})', [decode(v) for v in row])
                kept += cur.rowcount == 0
            for row in t["Update"]:
                if "Set" in row:                       # field update, as the patcher does it
                    s = [cols.index(c) for c in row["Set"]]
                    k = [cols.index(c) for c in t["Key"]]
                    cur = con.execute(f'UPDATE "{table}" SET {",".join(chr(34) + cols[i] + chr(34) + "=?" for i in s)} '
                                      f'WHERE {" AND ".join(chr(34) + cols[i] + chr(34) + " IS ?" for i in k + s)}',
                                      [decode(row["New"][i]) for i in s] + [decode(row["Old"][i]) for i in k + s])
                else:
                    cur = con.execute(f'UPDATE "{table}" SET {",".join(chr(34) + c + chr(34) + "=?" for c in cols)} WHERE {where}',
                                      [decode(v) for v in row["New"]] + [decode(row["Old"][i]) for i in cmp])
                kept += cur.rowcount == 0
            for row in t["Delete"]:
                cur = con.execute(f'DELETE FROM "{table}" WHERE {where}', [decode(row[i]) for i in cmp])
                kept += cur.rowcount == 0
        con.commit()
        con.close()
        assert kept == 0, f"{kept} rows of the update did not apply to the released world"
        again = world_update(work, target_db, policy, update["Version"])
        leftover = {t["Table"]: again["Summary"][t["Table"]] for t in again["Tables"]}
        assert not leftover and not again["NewTables"] and not again["NewColumns"], f"the patched world differs: {leftover}"


# ------------------------------------------------------------------------------------------------- files
def file_diff(bases: list[dict[str, str]], target: dict[str, Path], removed_extra: dict[str, str]) -> tuple[list, list]:
    """The files to add or replace and to remove. Each base is a version the update starts from (the released base, and
    an earlier build of this same version when it is reissued); a file's From lists what it may be in any of them."""
    files, remove = [], []
    for rel, path in sorted(target.items()):
        digest = sha(path)
        if all(b.get(rel) == digest for b in bases):
            continue
        start = sorted({b[rel] for b in bases if rel in b} | ({"absent"} if any(rel not in b for b in bases) else set()))
        files.append({"Path": rel, "Sha256": digest, "Bytes": path.stat().st_size, "From": start})
    for rel in sorted({r for b in bases for r in b} - set(target)):
        remove.append({"Path": rel, "From": sorted({b[rel] for b in bases if rel in b})})
    for rel, digest in sorted(removed_extra.items()):
        remove.append({"Path": rel, "From": [digest]})
    return files, remove


def write_manifest(root: Path, files: dict[str, Path], name: str) -> Path:
    path = root / name
    path.write_text("".join(f"{sha(p)}  {rel}\n" for rel, p in sorted(files.items())), encoding="utf-8")
    return path


def stage_patch(out: Path, root_name: str, patch: dict, payload: dict[str, Path], extra: dict[str, bytes | Path],
                patcher: Path) -> Path:
    stage = out / "_stage" / root_name
    if stage.exists():
        shutil.rmtree(stage)
    (stage / "files").mkdir(parents=True)
    for rel, src in payload.items():
        dst = stage / "files" / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
    for name, content in extra.items():
        if isinstance(content, Path):
            shutil.copy2(content, stage / name)
        else:
            (stage / name).write_bytes(content)
    shutil.copytree(patcher, stage / "patcher")
    (stage / "patch.json").write_text(json.dumps(patch, indent=1) + "\n", encoding="utf-8")
    return stage


def pack(stage: Path, folder: Path, name_prefix: str) -> list[dict]:
    """Independent zip parts; part 1 holds patch.json, the patcher and the world updates."""
    root = stage.name
    files = sorted(p for p in stage.rglob("*") if p.is_file())
    first = [p for p in files if p.relative_to(stage).parts[0] != "files"]
    rest = [p for p in files if p.relative_to(stage).parts[0] == "files"]
    groups, current, size = [], list(first), sum(p.stat().st_size for p in first)
    for path in rest:
        length = path.stat().st_size
        if current and size + length > PART_BUDGET:
            groups.append(current)
            current, size = [], 0
        current.append(path)
        size += length
    groups.append(current)
    parts = []
    for number, group in enumerate(groups, start=1):
        name = f"{name_prefix}-part{number:02d}.zip" if len(groups) > 1 else f"{name_prefix}.zip"
        archive = folder / name
        with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=".txt", delete=False) as listing:
            for path in group:
                listing.write(f"{root}/{path.relative_to(stage).as_posix()}\n")
            list_name = listing.name
        subprocess.run([str(SEVEN_ZIP), "a", "-tzip", "-mx=5", "-mmt=on", "-scsUTF-8", "-bso0", "-bsp0",
                        str(archive), f"@{list_name}"], cwd=stage.parent, check=True)
        Path(list_name).unlink()
        expected = {f"{root}/{p.relative_to(stage).as_posix()}": sha(p) for p in group}
        with zipfile.ZipFile(archive) as zf:
            seen = {}
            for item in zf.infolist():
                if item.is_dir():
                    continue
                digest = hashlib.sha256()
                with zf.open(item) as stream:
                    for block in iter(lambda: stream.read(1 << 20), b""):
                        digest.update(block)
                seen[item.filename] = digest.hexdigest()
        assert seen == expected, f"{name} does not match what was packed"
        assert archive.stat().st_size < 2_000_000_000, f"{name} is too large for GitHub"
        parts.append({"Name": name, "Bytes": archive.stat().st_size, "SHA256": sha(archive).upper(),
                      "UnpackedBytes": sum(p.stat().st_size for p in group), "Files": len(group)})
    return parts


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--version", required=True)
    parser.add_argument("--base", default="0.36")
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--client", type=Path, required=True)
    parser.add_argument("--baseline-dbs", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--allow-remove", action="append", default=[],
                        help="a game file under runtime/ the patch may remove (anything else there stops the build)")
    parser.add_argument("--revision", type=int, default=1,
                        help="2 and up: a corrected reissue of this version; it runs again on installs of the earlier build")
    parser.add_argument("--also-from", type=Path,
                        help="a published version's manifests (release-baselines/<v>: <v>b-package.sha256, <v>c-new-client.sha256): "
                             "installs of it are updated too")
    args = parser.parse_args()
    v, base_v = args.version, args.base
    # the version the patcher records (backups, world log): a reissue gets its own, so it runs on the earlier build too
    patch_v = v if args.revision <= 1 else f"{v}.{args.revision}"
    earlier_game_file = next(args.also_from.glob("*b-package.sha256")) if args.also_from else None
    earlier_client_file = next(args.also_from.glob("*c-new-client.sha256")) if args.also_from else None
    earlier_game = read_manifest(earlier_game_file) if earlier_game_file else None
    earlier_client = read_manifest(earlier_client_file) if earlier_client_file else None
    repo, game, client, out = args.repo.resolve(), args.game.resolve(), args.client.resolve(), args.out.resolve()
    for folder in (f"v{v}b", f"v{v}", f"v{v}c"):
        if (out / folder).exists():
            raise SystemExit(f"{out / folder} exists; refusing to overwrite release assets")
    baseline = repo / "source/tools/release-baselines" / base_v
    info = json.loads((baseline / "baseline.json").read_text(encoding="utf-8"))
    policy = json.loads((repo / "source/tools/OfflineDaoc.ProgressImport/progress-policy.json").read_text(encoding="utf-8"))
    patcher = repo / "source/tools/OfflineDaoc.Patcher/bin/Release/net10.0"
    assert (patcher / "OfflineDaoc.Patcher.dll").is_file(), "build the patcher first (source/tools/OfflineDaoc.Patcher, Release)"
    report = {"version": v, "base": base_v}

    # ---- game patch
    base_game = read_manifest(baseline / info["game_package_manifest"])
    base_manifest_hash = sha(baseline / info["game_package_manifest"])
    editions = {k: h for k, h in base_game.items() if k.startswith("editions/")}
    base_files = {k: h for k, h in base_game.items() if k not in GAME_SKIP_FILES and not k.startswith(GAME_SKIP_PREFIXES)}
    target = package_files(game, GAME_SKIP_PREFIXES, GAME_SKIP_FILES)
    manifest = write_manifest(game, target, "PACKAGE MANIFEST.sha256")      # the new version's manifest ships too
    target["PACKAGE MANIFEST.sha256"] = manifest
    base_files["PACKAGE MANIFEST.sha256"] = base_manifest_hash
    game_bases = [base_files]
    if earlier_game is not None:
        earlier = dict(earlier_game)
        earlier["PACKAGE MANIFEST.sha256"] = sha(earlier_game_file)
        game_bases.append(earlier)
    files, remove = file_diff(game_bases, target, editions)
    # A runtime file missing from the new version is far more often a packaging mistake than a real removal.
    unexpected = [r["Path"] for r in remove if r["Path"].startswith("runtime/") and r["Path"] not in args.allow_remove]
    if unexpected:
        raise SystemExit(f"{len(unexpected)} runtime files of {base_v} are missing from the new version (check the snapshot; "
                         "pass --allow-remove for real removals):\n  " + "\n  ".join(unexpected[:40]))
    dll = "runtime/client-opendaoc/app/game.dll"
    assert all(f["Path"] != dll for f in files), "game.dll changed: the patch would need a file for each edition"
    identity = "runtime/OfflineDAoC.dll"
    ident = next(f for f in files if f["Path"] == identity)
    game_root = f"OfflineDAoC-v{v}-game-patch"
    dbs = {}
    for edition, base_db, target_db in (("b", args.baseline_dbs / f"world-{base_v}b.sqlite3.db", game / "runtime/data/opendaoc.sqlite3.db"),
                                        ("plain", args.baseline_dbs / f"world-{base_v}.sqlite3.db",
                                         game / f"editions/{v}-no-custom-class/runtime/data/opendaoc.sqlite3.db")):
        update = world_update(base_db, target_db, policy, f"{v}{'b' if edition == 'b' else ''}")
        update["Version"] = patch_v              # one version per install, whatever the edition
        check_world_update(update, base_db, target_db, policy)
        name = f"db-{v}{'b' if edition == 'b' else ''}.json"
        dbs[edition] = (name, json.dumps(update, separators=(",", ":")).encode("utf-8"))
        report[f"world_{edition}"] = update["Summary"]
    game_patch = {"Version": patch_v, "Kind": "game", "Title": f"Offline DAoC {v} update",
                  "Identity": {"Path": identity, "To": ident["Sha256"], "From": ident["From"]},
                  "Files": files, "Remove": remove, "Database": {e: n for e, (n, _) in dbs.items()},
                  "EditionText": {"plain": f"Offline DAoC {v} - no custom class (Classic + Shrouded Isles classes only)."}}
    stage = stage_patch(out, game_root, game_patch, {f["Path"]: target[f["Path"]] for f in files},
                        {n: data for n, data in dbs.values()}, patcher)
    (out / f"v{v}b").mkdir(parents=True)
    game_parts = pack(stage, out / f"v{v}b", game_root)
    report["game_patch"] = {"files": len(files), "removed": len(remove), "parts": game_parts}

    # ---- client patch
    base_client = read_manifest(baseline / info["new_client_package_manifest"])
    base_client_files = {k: h for k, h in base_client.items() if k not in CLIENT_SKIP_FILES and not k.startswith(CLIENT_SKIP_PREFIXES)}
    target_client = package_files(client, CLIENT_SKIP_PREFIXES, CLIENT_SKIP_FILES)
    cmanifest = write_manifest(client, target_client, "new-client/PACKAGE MANIFEST.sha256")   # same layout as 0.36c
    target_client["new-client/PACKAGE MANIFEST.sha256"] = cmanifest
    base_client_files["new-client/PACKAGE MANIFEST.sha256"] = sha(baseline / info["new_client_package_manifest"])
    client_bases = [base_client_files]
    if earlier_client is not None:
        earlier = {k: h for k, h in earlier_client.items() if k not in CLIENT_SKIP_FILES and not k.startswith(CLIENT_SKIP_PREFIXES)}
        earlier["new-client/PACKAGE MANIFEST.sha256"] = sha(earlier_client_file)
        client_bases.append(earlier)
    cfiles, cremove = file_diff(client_bases, target_client, {})
    cident = next(f for f in cfiles if f["Path"] == "new-client/VERSION.txt")
    client_root = f"OfflineDAoC-v{v}c-client-patch"
    client_patch = {"Version": patch_v, "Kind": "new-client", "Title": f"Offline DAoC {v}c new client update",
                    "Identity": {"Path": "new-client/VERSION.txt", "To": cident["Sha256"], "From": cident["From"]},
                    "Files": cfiles, "Remove": cremove, "Database": None, "EditionText": None}
    cstage = stage_patch(out, client_root, client_patch, {f["Path"]: target_client[f["Path"]] for f in cfiles}, {}, patcher)
    (out / f"v{v}c").mkdir(parents=True)
    client_parts = pack(cstage, out / f"v{v}c", client_root)
    report["client_patch"] = {"files": len(cfiles), "removed": len(cremove), "parts": client_parts}
    shutil.rmtree(out / "_stage")

    # ---- download manifests (the game patch lives in v<v>b, the client patch in v<v>c)
    # Identity lets the downloader see "already up to date" or "not a version this patch knows" before downloading
    game_ref = {"Release": f"{v}b", "RootFolder": game_root, "Identity": game_patch["Identity"], "Parts": game_parts}
    client_ref = {"Release": f"{v}c", "RootFolder": client_root, "Identity": client_patch["Identity"], "Parts": client_parts}
    (out / f"v{v}").mkdir(parents=True)
    for folder, title, base_release, classic in ((f"v{v}b", f"Offline DAoC {v}b (with the Sluaghbinder)", f"{base_v}b", False),
                                                (f"v{v}", f"Offline DAoC {v} (classic classes only)", base_v, True),
                                                (f"v{v}c", f"Offline DAoC {v}c (both clients)", f"{base_v}c", False)):
        manifest_out = {"Version": folder[1:], "Mode": "patch", "Title": title, "PatchVersion": v,
                        "BaseVersions": [base_v], "NewInstallBase": base_release, "GamePatch": game_ref,
                        "ClientPatch": client_ref}
        (out / folder / "download-manifest.json").write_text(json.dumps(manifest_out, indent=2) + "\n", encoding="utf-8")
    for folder in (f"v{v}b", f"v{v}", f"v{v}c"):
        lines = [f"{sha(p)}  {p.name}" for p in sorted((out / folder).iterdir()) if p.is_file() and p.name != "SHA256SUMS.txt"]
        (out / folder / "SHA256SUMS.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    (out / f"patch-report-{v}.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"game": {k: report["game_patch"][k] for k in ("files", "removed")},
                      "game_bytes": sum(p["Bytes"] for p in game_parts),
                      "client": {k: report["client_patch"][k] for k in ("files", "removed")},
                      "client_bytes": sum(p["Bytes"] for p in client_parts),
                      "world_b": report["world_b"], "world_plain": report["world_plain"]}, indent=2), flush=True)


if __name__ == "__main__":
    main()
