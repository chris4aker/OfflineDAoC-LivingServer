#!/usr/bin/env python3
"""Runs the server tests CI leaves out because they read the installed game files (.github/ci-excluded-tests.txt,
issue #619), on a PC with a playable folder and the .NET SDK.

    python source/tools/run_installed_tests.py --list
        Each listed class, the reason it is listed and the environment variables its source reads. Needs nothing.
    python source/tools/run_installed_tests.py --game D:\\Games\\LivingServer [--only UT_A UT_B] [--out DIR] [--dry-run]
        Runs the listed classes (or --only those) with dotnet test from source/server, with:
          OFFLINE_DAOC_NAV_ROOT  <game>/runtime/server (the navmeshes are read in place, never written)
          OFFLINE_DAOC_DB_PATH   a copy of <game>/runtime/data/opendaoc.sqlite3.db in --out (never the world itself)
          OFFLINE_DAOC_DB        the same copy
          OFFLINE_DAOC_PROOF_OUT <out>/proof
        Other variables a class reads (see --list) pass through from your environment. The results are written to
        <out>/installed.trx and summed up. --dry-run prints the command and the variables and runs nothing.

Protecting the world: the database is copied with SQLite's backup API from a read-only connection, and the tests only
see the copy. Nothing runs while a server answers on port 10300 (close it first: a world being written is not copied).
This script never starts a server. --out defaults to a new folder under the system temp folder.

Entries naming one test (UT_Name.Method) are quarantined flaky tests with their own issue, not installed-file tests:
they are skipped here. Python standard library only. Tests: source/tools/tests/test_run_installed_tests.py.
"""
from __future__ import annotations

import argparse
import os
import re
import socket
import sqlite3
import subprocess
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
import ci_test_results as ci  # noqa: E402

LIST = REPO / ".github" / "ci-excluded-tests.txt"
TESTS = REPO / "source" / "server" / "Tests"
SERVER = REPO / "source" / "server"
PORT = 10300
ENV_RE = re.compile(r'GetEnvironmentVariable\(\s*"([A-Z][A-Z0-9_]*)"')
SET_HERE = ("OFFLINE_DAOC_NAV_ROOT", "OFFLINE_DAOC_DB_PATH", "OFFLINE_DAOC_DB", "OFFLINE_DAOC_PROOF_OUT")


class Stop(Exception):
    """A problem the person has to fix; nothing ran."""


def listed_classes(text: str) -> list[tuple[str, str]]:
    """(class, reason) of each whole-class entry of ci-excluded-tests.txt. Raises Stop on a malformed list."""
    entries, problems = ci.parse_excluded(text)
    if problems:
        raise Stop("ci-excluded-tests.txt: " + "; ".join(problems))
    return [(name, reason) for name, reason in entries if "." not in name]


def source_of(name: str, tests: Path = TESTS) -> Path:
    found = sorted(tests.rglob(f"{name}.cs"))
    if not found:
        raise Stop(f"{name}: no {name}.cs under {tests}")
    return found[0]


def env_vars(name: str, tests: Path = TESTS) -> list[str]:
    """The environment variables a class's source file reads, in name order."""
    return sorted(set(ENV_RE.findall(source_of(name, tests).read_text(encoding="utf-8", errors="replace"))))


def name_filter(names: list[str]) -> str:
    """The dotnet test filter selecting these classes (the leading dot, as excluded-filter uses, keeps UT_A from
    matching a class whose name ends in UT_A)."""
    return "|".join(f"FullyQualifiedName~.{n}" for n in names)


def server_running(port: int = PORT) -> bool:
    try:
        with socket.create_connection(("127.0.0.1", port), timeout=0.5):
            return True
    except OSError:
        return False


def game_paths(game: Path) -> tuple[Path, Path]:
    """(runtime/server, the world database) of a playable folder."""
    server = game / "runtime" / "server"
    world = game / "runtime" / "data" / "opendaoc.sqlite3.db"
    if not (server / "navmesh").is_dir():
        raise Stop(f"{game} is not a playable folder: no runtime/server/navmesh")
    if not world.is_file():
        raise Stop(f"{game} has no runtime/data/opendaoc.sqlite3.db")
    return server, world


def copy_world(world: Path, out: Path) -> Path:
    """A consistent copy of the world in out, read through a read-only connection. Returns the copy's path."""
    target = out / "world-copy.sqlite3.db"
    if target.exists():
        target.unlink()
    src = sqlite3.connect(f"file:{world.as_posix()}?mode=ro", uri=True)
    try:
        dst = sqlite3.connect(target)
        try:
            src.backup(dst)
        finally:
            dst.close()
    finally:
        src.close()
    return target


def plan(game: Path, names: list[str], out: Path, environ: dict[str, str]) -> tuple[list[str], dict[str, str]]:
    """The dotnet command and its environment. Copies the world into out."""
    server, world = game_paths(game)
    out.mkdir(parents=True, exist_ok=True)
    (out / "proof").mkdir(exist_ok=True)
    copy = copy_world(world, out)
    env = dict(environ)
    env.update({
        "OFFLINE_DAOC_NAV_ROOT": str(server),
        "OFFLINE_DAOC_DB_PATH": str(copy),
        "OFFLINE_DAOC_DB": str(copy),
        "OFFLINE_DAOC_PROOF_OUT": str(out / "proof"),
    })
    cmd = ["dotnet", "test", "Tests/Tests.csproj", "-c", "Release", "--filter", name_filter(names),
           "--logger", "trx;LogFileName=installed.trx", "--results-directory", str(out)]
    return cmd, env


def run(argv: list[str] | None = None, runner=subprocess.call, running=server_running, environ=None,
        list_path: Path = LIST, tests: Path = TESTS) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--list", action="store_true", help="show the classes, their reasons and variables; run nothing")
    ap.add_argument("--game", type=Path, help="the playable folder (runtime/ inside)")
    ap.add_argument("--only", nargs="+", default=[], help="only these listed classes")
    ap.add_argument("--out", type=Path, help="where the world copy, proofs and results go (default: a new temp folder)")
    ap.add_argument("--dry-run", action="store_true", help="print the command and variables; run nothing")
    a = ap.parse_args(argv)
    environ = dict(os.environ if environ is None else environ)
    try:
        entries = listed_classes(list_path.read_text(encoding="utf-8"))
        if a.list:
            for name, reason in entries:
                extra = [v for v in env_vars(name, tests) if v not in SET_HERE]
                print(f"{name}\n    {reason}\n    reads: {', '.join(env_vars(name, tests)) or '(none)'}"
                      + (f"\n    set yourself if needed: {', '.join(extra)}" if extra else ""))
            print(f"\n{len(entries)} classes. This script sets {', '.join(SET_HERE)}.")
            return 0
        if a.game is None:
            raise Stop("--game <playable folder> is needed to run them (or --list to see them)")
        known = [n for n, _ in entries]
        unknown = [n for n in a.only if n not in known]
        if unknown:
            raise Stop(f"not on ci-excluded-tests.txt: {', '.join(unknown)}")
        names = a.only or known
        if running():
            raise Stop(f"a server answers on port {PORT}: close it first (its world is not copied while it runs)")
        out = a.out or Path(tempfile.mkdtemp(prefix=f"installed-tests-{time.strftime('%Y%m%d-%H%M%S')}-"))
        cmd, env = plan(a.game, names, out, environ)
        print(f"{len(names)} classes; results in {out}")
        for key in SET_HERE:
            print(f"  {key}={env[key]}")
        print("  (in " + str(SERVER) + ") " + " ".join(f'"{c}"' if " " in c or "|" in c or ";" in c else c for c in cmd))
        if a.dry_run:
            return 0
        code = runner(cmd, cwd=str(SERVER), env=env)
        trx = out / "installed.trx"
        if trx.is_file():
            # An [Explicit] class that its own variables keep from running shows as skipped, not passed.
            summary = ci.check_trx(trx, "Installed-file tests", [], 1)
            code = code or summary
        else:
            print(f"no results file ({trx}): dotnet test did not get as far as running tests")
            code = code or 1
        return code
    except Stop as e:
        print(f"error: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(run())
