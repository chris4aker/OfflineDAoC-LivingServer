#!/usr/bin/env python3
"""The navmesh set's clean world database (issue #687, owner decision 2026-11-26: option 1).

The database tests of .github/navmesh-tests.txt read the world beside the navmeshes. A played database is never
published (AGENTS.md, "Protect player data"), so the set's world is made from a FRESH install (navmesh-publish.yml: a
real Setup install on a CI runner, whose server has never started) and cleaned by the release builder's own code, then
checked by the release builder's own verify step. Nothing here repeats those rules: they live in
source/tools/build_release_037.py (clean_world, verify_clean, verify_tables_empty) and in
source/tools/DAoCLivingServer.ProgressImport/progress-policy.json.

    python tools/navmesh/clean_world.py clean SOURCE.db --out NEW.db [--report NEW.db.clean.json] [--require-fresh]
        Copies SOURCE with SQLite's online backup, opened read-only (SOURCE is never written; a copy of a live database
        is consistent), runs build_release_037.clean_world() on the copy (every saved-progress table emptied, the default
        keeps, doors and relics, no account), keeps the source's edition switch, then runs the check below. Refuses an
        existing --out, and deletes a half-made one. --require-fresh also refuses a source holding any account, character
        or gamebot (a fresh install's world has none). The report (--report) names the file's SHA-256; the packer
        (Publish-NavmeshSet.ps1 -WorldDatabase) takes only a file whose report says clean with that hash.
    python tools/navmesh/clean_world.py verify DB.db
        Fails closed (exit 1, with an ::error) unless the world is clean: build_release_037.verify_clean() (every table of
        progress-policy.json that the release builder empties holds no row; no account, character or gamebot; only the
        starting guilds; the default keeps, doors and relics) and verify_tables_empty() for every saved-progress table the
        server declares (Crafting 2.0's EconomyTables.All, the hired companions, BotPeopleTables.All), which must all be in
        the policy too. A missing file, a damaged database or a missing table fails it as well. Opened read-only.

Run in navmesh-publish.yml before the set is packed and again on the packed copy before it is published, and in
navmesh.yml on every run before the tests read the world. Tests: tools/navmesh/tests/test_clean_world.py (a planted row in
any saved-progress table fails the check). Python standard library only.
"""
from __future__ import annotations

import argparse
import gc
import hashlib
import json
import sqlite3
import sys
import traceback
from contextlib import closing
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
TOOLS = REPO / "source" / "tools"
sys.path.insert(0, str(TOOLS))
import build_release_037 as release  # noqa: E402  (the release builder's own cleaning and checks)

POLICY = TOOLS / "DAoCLivingServer.ProgressImport" / "progress-policy.json"
DEFAULTS = TOOLS / "release-world-defaults.json"
TITLE = "Clean world"


class NotClean(Exception):
    """The world is not clean (or can't be read): it must not be published or used."""


def annotate(kind: str, text: str) -> str:
    return f"::{kind} title={TITLE}::{' '.join(text.split())[:400]}"


def load_rules(repo: Path = REPO) -> tuple[dict, dict, list[str]]:
    """progress-policy.json, release-world-defaults.json and every saved-progress table the server declares (checked to be
    in the policy, as build_release_037.main() does before it builds)."""
    tools = repo / "source" / "tools"
    policy = json.loads((tools / "DAoCLivingServer.ProgressImport/progress-policy.json").read_text(encoding="utf-8"))
    defaults = json.loads((tools / "release-world-defaults.json").read_text(encoding="utf-8"))
    declared = release.economy_tables(repo) + release.hired_companion_tables(repo) + release.bot_people_tables(repo)
    release.check_policy_covers(policy, declared)
    return policy, defaults, declared


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def edition(con: sqlite3.Connection) -> str | None:
    """classes / enable_sluaghbinder as stored ("True"/"False"), or None without a row."""
    row = con.execute("SELECT Value FROM ServerProperty WHERE lower(`Key`)='enable_sluaghbinder'").fetchone()
    return None if row is None else str(row[0])


def verify(database: Path, repo: Path = REPO) -> dict:
    """The release builder's verify step on database, read-only. Raises NotClean."""
    if not database.is_file():
        raise NotClean(f"no world database at {database}")
    try:
        policy, defaults, declared = load_rules(repo)
        with closing(sqlite3.connect(database.resolve().as_uri() + "?mode=ro", uri=True)) as con:
            report = release.verify_clean(con, policy, defaults)
            report["declared_tables"] = release.verify_tables_empty(con, declared)
            # Every table the release builder empties, with its row count (0, or "absent"), for the run page.
            emptied = policy["ProgressTables"] + policy.get("OptionalProgressTables", []) + policy["ClearTables"]
            present = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            counts = {t: (con.execute(f'SELECT COUNT(*) FROM "{t}"').fetchone()[0] if t in present else "absent")
                      for t in emptied}
    except AssertionError as e:
        raise NotClean(f"{database.name} is not a clean world: {e or 'a check of build_release_037.verify_clean failed'}")
    except (sqlite3.Error, KeyError, TypeError) as e:
        raise NotClean(f"{database.name} could not be checked ({type(e).__name__}: {e}): treated as not clean")
    report["policy_tables"] = counts
    report["clean"] = True
    report["sha256"] = sha256_of(database)
    report["bytes"] = database.stat().st_size
    return report


def clean(source: Path, out: Path, repo: Path = REPO, require_fresh: bool = False) -> dict:
    """A clean copy of source at out (source is only read). Raises NotClean; never leaves a half-made out behind.
    require_fresh: refuse a source that holds any account, character or gamebot (a fresh install's world has none: its
    server never started), so a played world can't be fed in by mistake even though it would be cleaned."""
    if not source.is_file():
        raise NotClean(f"no world database at {source}")
    if out.exists():
        raise NotClean(f"{out} exists: the clean world is always a new file")
    if out.resolve() == source.resolve():
        raise NotClean("--out is the source")
    policy, defaults, _ = load_rules(repo)
    out.parent.mkdir(parents=True, exist_ok=True)
    try:
        with closing(sqlite3.connect(source.resolve().as_uri() + "?mode=ro", uri=True)) as src, \
                closing(sqlite3.connect(out)) as dst:
            src.backup(dst)
        with closing(sqlite3.connect(out)) as con:
            saves = {t: con.execute(f'SELECT COUNT(*) FROM "{t}"').fetchone()[0] for t in ("Account", "DOLCharacters", "offline_world_bots")
                     if con.execute("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", (t,)).fetchone()[0]}
            source_edition = edition(con)
        if require_fresh and any(saves.values()):
            raise NotClean(f"{source.name} is not a fresh install's world (it holds {saves}): refused with --require-fresh")
        cleaned = release.clean_world(out, policy, defaults)   # asserts as a release build does
        # clean_world sets the 0.37b package's switch (on); the set's world keeps the edition the install has.
        if source_edition is not None and source_edition.strip().lower() == "false":
            with closing(sqlite3.connect(out)) as con:
                release.set_edition(con, False)
                con.commit()
        report = verify(out, repo)
    except BaseException as e:
        # clean_world() leaves its connection open when one of its asserts fails, and the traceback keeps it alive:
        # Windows would refuse to delete the half-made file. Clearing the finished frames closes it.
        traceback.clear_frames(e.__traceback__)
        gc.collect()
        for suffix in ("", "-wal", "-shm", "-journal"):
            Path(str(out) + suffix).unlink(missing_ok=True)
        if isinstance(e, NotClean):
            raise
        if isinstance(e, AssertionError):
            raise NotClean(f"the release builder's cleaning refused {source.name}: {e}")
        if isinstance(e, sqlite3.Error):
            raise NotClean(f"{source.name} could not be cleaned ({e})")
        raise
    report.update({"source_saves_before_cleaning": saves, "source_edition": source_edition,
                   "clean_world_report": cleaned})
    return report


def summary_lines(report: dict) -> list[str]:
    tables = report.get("policy_tables", {})
    empty = sum(1 for n in tables.values() if n == 0)
    absent = sum(1 for n in tables.values() if n == "absent")
    return [f"quick_check {report.get('quick_check')}; {report.get('bytes', 0):,} bytes",
            f"progress-policy.json tables: {empty} empty, {absent} absent; with rows (world tables verify_clean checks "
            f"by content: the starting guilds, local options, runtime status): {[t for t, n in tables.items() if n not in (0, 'absent')]}",
            f"server-declared saved-progress tables empty: {len(report.get('declared_tables', {}))}",
            f"keeps {report.get('keeps')}, doors {report.get('doors')}, edition switch {report.get('enable_sluaghbinder')}"]


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("clean", help="a clean copy of a fresh install's world")
    c.add_argument("source", type=Path)
    c.add_argument("--out", type=Path, required=True)
    c.add_argument("--report", type=Path, help="also write the report here (JSON)")
    c.add_argument("--require-fresh", action="store_true",
                   help="refuse a source with any account, character or gamebot (a fresh install has none)")
    v = sub.add_parser("verify", help="fail closed unless the world is clean")
    v.add_argument("database", type=Path)
    v.add_argument("--report", type=Path, help="also write the report here (JSON)")
    args = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(newline="\n")
    try:
        report = clean(args.source, args.out, require_fresh=args.require_fresh) if args.cmd == "clean" else verify(args.database)
    except NotClean as e:
        print(annotate("error", str(e)))
        print("NOT CLEAN: this world must not be published or used.")
        return 1
    if args.report:
        args.report.write_text(json.dumps(report, indent=1, default=str), encoding="utf-8")
    print(json.dumps({k: v for k, v in report.items() if k not in ("policy_tables", "declared_tables", "clean_world_report")},
                     indent=1, default=str))
    for line in summary_lines(report):
        print(line)
    print(annotate("notice", f"CLEAN: {(args.out if args.cmd == 'clean' else args.database).name} passed "
                             f"build_release_037's verify step ({len(report['policy_tables'])} policy tables, "
                             f"{len(report['declared_tables'])} declared tables)"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
