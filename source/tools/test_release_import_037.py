"""Test the packaged progress importer against real saves, in throwaway copies only.

    python test_release_import_037.py --package <staging>/OfflineDAoC-v0.37b --work <new empty folder>
        --case NAME=EDITION:EXPECT:SOURCE_FOLDER [--case ...] [--server-check NAME]

EDITION is "b" (0.37b database) or "plain" (the 0.37 no-custom-class database). EXPECT is one of
  ok        the import must succeed with every saved table copied row for row,
  refuse    the import must stop without changing the new folder,
  leave     like "refuse" first, then succeed with --leave-sluaghbinder-bots (the 0.37 edition
            leaves the autonomous Sluaghbinder bots and the items they carry behind).
SOURCE_FOLDER is only read. --server-check NAME then starts the packaged server (bundled .NET only)
on that case's imported save and logs in with its account.txt through connect.exe, as ENTER REALM
does, to show the save loads.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import socket
import sqlite3
import subprocess
import sys
import time
from pathlib import Path

SLUAGHBINDER = 63
# A play test sweep's guest record (server: EpicPlaytestSweepGuestRecord.Param) is never carried: it would give GM back.
SWEEP_GUEST_KEY = "LivingServer.EpicSweepGuest"


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def tree_hash(folder: Path) -> str:
    # SQLite's -shm file is a shared-memory index that any reader of a WAL database may rewrite;
    # it holds no data, so only the database files themselves are compared.
    digest = hashlib.sha256()
    for path in sorted(p for p in folder.rglob("*") if p.is_file() and not p.name.endswith("-shm")):
        digest.update(path.relative_to(folder).as_posix().encode())
        digest.update(sha(path).encode())
    return digest.hexdigest()


def runtime_of(folder: Path) -> Path:
    return folder / "runtime" if (folder / "runtime/data/opendaoc.sqlite3.db").exists() else folder


def ro(path: Path) -> sqlite3.Connection:
    return sqlite3.connect(path.as_uri() + "?mode=ro", uri=True)


def counts(db: Path, tables) -> dict[str, int]:
    with ro(db) as con:
        present = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        return {t: con.execute(f'SELECT count(*) FROM "{t}"').fetchone()[0] for t in tables if t in present}


def run_import(package: Path, env, old: Path, new: Path, report: Path, leave: bool) -> int:
    exe = package / "tools/ProgressImporter/OfflineDaoc.ProgressImport.exe"
    args = [str(exe), "--import", str(old), str(new), "--replace-progress", str(report)]
    if leave:
        args.append("--leave-sluaghbinder-bots")
    return subprocess.run(args, env=env, timeout=3600).returncode


def make_destination(package: Path, edition: str, target: Path):
    data = target / "runtime/data"
    data.mkdir(parents=True)
    db = (package / "runtime/data/opendaoc.sqlite3.db" if edition == "b"
          else package / "editions/0.37-no-custom-class/runtime/data/opendaoc.sqlite3.db")
    shutil.copy2(db, data / "opendaoc.sqlite3.db")


def check_case(package: Path, env, policy, name: str, source: Path, edition: str, expect: str, work: Path) -> dict:
    case = work / name
    new = case / "new"
    make_destination(package, edition, new)
    old_runtime = runtime_of(source)
    old_db = old_runtime / "data/opendaoc.sqlite3.db"
    source_before = tree_hash(old_runtime / "data")
    progress = policy["ProgressTables"]
    old_counts = counts(old_db, progress)
    with ro(old_db) as con:
        sluagh_bots = con.execute(f"SELECT count(*) FROM offline_world_bots WHERE ClassId={SLUAGHBINDER}").fetchone()[0]
        sluagh_items = con.execute(f"SELECT count(*) FROM Inventory WHERE OwnerID IN (SELECT 'offlinebot:'||BotId "
                                   f"FROM offline_world_bots WHERE ClassId={SLUAGHBINDER})").fetchone()[0]
        present = {r[0] for r in con.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        sweep_records = (con.execute("SELECT count(*) FROM AccountXCustomParam WHERE KeyName=?", (SWEEP_GUEST_KEY,)).fetchone()[0]
                         if "AccountXCustomParam" in present else 0)
    result = {"source": str(source), "edition": edition, "expect": expect, "accounts": old_counts.get("Account"),
              "characters": old_counts.get("DOLCharacters"), "bots": old_counts.get("offline_world_bots"),
              "inventory": old_counts.get("Inventory"), "sluaghbinder_bots": sluagh_bots}

    new_db = new / "runtime/data/opendaoc.sqlite3.db"
    if expect in ("refuse", "leave"):
        before = sha(new_db)
        code = run_import(package, env, source, new, case / "refused.txt", False)
        assert code != 0, f"{name}: the import should have been refused"
        assert sha(new_db) == before and not (new / "runtime/account.txt").exists(), f"{name}: a refused import changed the new folder"
        result["refused"] = (case / "refused.txt").read_text(encoding="utf-8", errors="replace").splitlines()[0][:220]
        if expect == "refuse":
            assert tree_hash(old_runtime / "data") == source_before, f"{name}: the old folder changed"
            return result

    code = run_import(package, env, source, new, case / "report.txt", expect == "leave")
    report = (case / "report.txt").read_text(encoding="utf-8", errors="replace")
    assert code == 0, f"{name}: import failed:\n{report}"
    assert tree_hash(old_runtime / "data") == source_before, f"{name}: the old folder changed"
    new_counts = counts(new_db, progress)
    for table, count in old_counts.items():
        expected = count
        if expect == "leave" and table == "offline_world_bots":
            expected = count - sluagh_bots
        if expect == "leave" and table == "Inventory":
            expected = count - sluagh_items
        if expect == "leave" and table == "ItemUnique":
            continue        # unique items carried only by the bots left behind are removed with them
        if table == "AccountXCustomParam":
            expected = count - sweep_records
        assert new_counts.get(table) == expected, f"{name}: {table} has {new_counts.get(table)} rows, expected {expected}"
    with ro(new_db) as con:
        assert con.execute("PRAGMA quick_check").fetchone()[0] == "ok"
        assert con.execute("SELECT count(*) FROM Account WHERE PrivLevel<>1").fetchone()[0] == 0
        assert con.execute("SELECT count(*) FROM offline_world_bots WHERE IsOnline<>0").fetchone()[0] == 0
        assert con.execute("SELECT Value FROM offline_local_options WHERE Key='MakeMeGM'").fetchone()[0] == "false"
        assert con.execute("SELECT count(*) FROM AccountXCustomParam WHERE KeyName=?", (SWEEP_GUEST_KEY,)).fetchone()[0] == 0, \
            f"{name}: a sweep guest record was carried (it would raise the account back to GM)"
        rates = dict(con.execute("SELECT `Key`,Value FROM ServerProperty WHERE lower(`Key`) IN ('xp_rate','bot_xp_rate')"))
        assert set(rates.values()) <= {"1"}, rates
        switch = con.execute("SELECT Value FROM ServerProperty WHERE `Key`='enable_sluaghbinder'").fetchone()[0]
        assert switch == ("True" if edition == "b" else "False"), "the new folder must keep its own edition"
        if edition == "plain":
            assert con.execute(f"SELECT count(*) FROM offline_world_bots WHERE ClassId={SLUAGHBINDER}").fetchone()[0] == 0
        target = con.execute("SELECT Value FROM offline_population_settings WHERE Key='ActiveTarget'").fetchone()[0]
        result["active_target"] = target
        result["imported_bots"] = con.execute("SELECT count(*) FROM offline_world_bots").fetchone()[0]
    old_account = old_runtime / "account.txt"
    new_account = new / "runtime/account.txt"
    if old_counts.get("Account", 0) == 0:
        result["account"] = "none in the old save; the new folder keeps its own"
        assert not new_account.exists()
    elif old_account.exists():
        def login(path):
            pairs = (line.split(":", 1) for line in path.read_text(encoding="utf-8-sig").splitlines() if ":" in line)
            values = {key.strip(): value.strip() for key, value in pairs}
            return values.get("Account"), values.get("Password")
        assert login(new_account) == login(old_account), f"{name}: the old login was not carried over"
        result["account"] = "old login carried over"
    else:
        assert new_account.exists(), f"{name}: no account.txt was written for the kept account"
        result["account"] = "old account kept with a new password"
    result["report"] = [line for line in report.splitlines() if line.strip()][:12]
    return result


def server_check(package: Path, env, case: Path) -> dict:
    """Start the packaged server on an imported save and log in with its account.txt."""
    if subprocess.run(["tasklist", "/FI", "IMAGENAME eq CoreServer.exe"], capture_output=True, text=True).stdout.lower().count("coreserver.exe"):
        raise SystemExit("Another server is running")
    with socket.socket() as probe:
        if probe.connect_ex(("127.0.0.1", 10300)) == 0:
            raise SystemExit("Port 10300 is in use")
    live = case / "server-run"
    shutil.copytree(package / "runtime/server", live / "server")
    shutil.copytree(case / "new/runtime/data", live / "data")
    creds = dict(line.split(":", 1) for line in (case / "new/runtime/account.txt").read_text().splitlines() if ":" in line)
    account, password = creds["Account"].strip(), creds["Password"].strip()
    log = live / "console.log"
    started = time.monotonic()
    with log.open("w", encoding="utf-8") as output:
        server = subprocess.Popen([str(live / "server/CoreServer.exe")], cwd=live / "server", env=env, stdin=subprocess.PIPE,
                                  stdout=output, stderr=subprocess.STDOUT, text=True, creationflags=subprocess.CREATE_NO_WINDOW)
        client = None
        try:
            while "GameServer startup completed" not in log.read_text(encoding="utf-8", errors="replace"):
                if server.poll() is not None:
                    raise RuntimeError("Server exited during startup; see " + str(log))
                if time.monotonic() - started > 1200:
                    raise RuntimeError("Startup timeout")
                time.sleep(2)
            startup = round(time.monotonic() - started)
            with ro(live / "data/opendaoc.sqlite3.db") as db:
                before = db.execute("SELECT LastLogin FROM Account WHERE Name=?", (account,)).fetchone()[0]
            app = package / "runtime/client-opendaoc/app"
            client = subprocess.Popen([str(app / "connect.exe"), "game.dll", "127.0.0.1", account, password], cwd=app, env=env,
                                      creationflags=subprocess.CREATE_NO_WINDOW)
            deadline = time.monotonic() + 240
            logged_in = False
            while time.monotonic() < deadline and not logged_in:
                time.sleep(3)
                with ro(live / "data/opendaoc.sqlite3.db") as db:
                    logged_in = db.execute("SELECT LastLogin FROM Account WHERE Name=?", (account,)).fetchone()[0] != before
            if not logged_in:
                text = log.read_text(encoding="utf-8", errors="replace")
                logged_in = f"({account})" in text or f"Account {account}" in text or f"'{account}'" in text
            assert logged_in, "the imported account did not log in"
            return {"startup_seconds": startup, "login": f"'{account}' logged in with the imported account.txt"}
        finally:
            subprocess.run(["taskkill", "/IM", "game.dll", "/T", "/F"], capture_output=True)
            for name in ("game.dll", "camelot.exe"):
                subprocess.run(["taskkill", "/FI", f"IMAGENAME eq {name}", "/T", "/F"], capture_output=True)
            if server.poll() is None:
                server.stdin.write("exit\n")
                server.stdin.flush()
                try:
                    server.wait(timeout=600)
                except subprocess.TimeoutExpired:
                    server.terminate()
                    raise RuntimeError("Server needed a forced stop")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--package", type=Path, required=True)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--case", action="append", default=[])
    parser.add_argument("--server-check")
    args = parser.parse_args()
    package, work = args.package.resolve(), args.work.resolve()
    work.mkdir(parents=True, exist_ok=False)
    policy = json.loads((package / "tools/ProgressImporter/progress-policy.json").read_text())
    env = os.environ.copy()
    env["DOTNET_ROOT"] = str(package / "tools/dotnet")
    env["DOTNET_ROOT_X64"] = env["DOTNET_ROOT"]
    env["DOTNET_MULTILEVEL_LOOKUP"] = "0"
    results = {}
    for spec in args.case:
        name, rest = spec.split("=", 1)
        edition, expect, source = rest.split(":", 2)
        assert edition in ("b", "plain") and expect in ("ok", "refuse", "leave"), spec
        print(f"== {name}", flush=True)
        results[name] = check_case(package, env, policy, name, Path(source).resolve(), edition, expect, work)
        print(json.dumps(results[name], indent=2), flush=True)
    if args.server_check:
        print(f"== server check on {args.server_check}", flush=True)
        results["server_check"] = server_check(package, env, work / args.server_check)
        print(json.dumps(results["server_check"], indent=2), flush=True)
    (work / "results.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    print("ALL IMPORT TESTS PASSED", flush=True)


if __name__ == "__main__":
    sys.exit(main())
