#!/usr/bin/env python3
"""Runs every Python check of .github/workflows/build-and-test.yml locally, in parallel (issue #615).

Required before every push (AGENTS.md). The checks need no dotnet, no game files and no network. They take about 175 CPU
seconds: about 90 s on a 2-core box, under a minute with 4 cores or more. Each unittest folder runs one process per test file, so the slow lore and bot-voice suites spread
over the cores.

    python3 tools/check.py              # every check (tools/check.sh calls this)
    python3 tools/check.py --list       # the checks, with the CI job each one mirrors
    python3 tools/check.py --only lore  # only the checks whose name contains "lore"
    python3 tools/check.py --jobs 2     # at most two at a time (default: the CPU count)

The list below is the workflow's, step for step: source/tools/tests/test_check.py reads the workflow and fails when a
Python command there is neither in CHECKS nor in NOT_LOCAL (with the reason). So a check added to CI must be added here.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
WORKFLOW = Path(".github/workflows/build-and-test.yml")

# (CI job, command) as the workflow writes it, without "python"/"python3" and without unittest's "-v".
CHECKS: list[tuple[str, str]] = [
    ("versions", "tools/release/check_versions.py"),
    ("versions", "tools/release/check_brand.py"),
    ("versions", "tools/release/check_brand.py --internal"),
    ("versions", "-m unittest discover -s tools/release/tests"),
    ("versions", "-m unittest discover -s tools/installer/tests"),
    ("encounter-catalog", "source/tools/epic_encounter_catalog.py --check"),
    ("encounter-catalog", "source/tools/assemble_changelog.py --check"),
    ("encounter-catalog", "tools/grafana/kit.py --check"),
    ("encounter-catalog", "-m unittest discover -s tools/grafana/tests"),
    ("encounter-catalog", "-m unittest discover -s source/tools/tests"),
    ("server", "tools/bot-voices/build_corpus.py --verify"),
    ("server", "tools/bot-voices/check_runtime_names.py"),
    ("server", "tools/bot-voices/debates_doc.py --check"),
    ("server", "tools/bot-voices/say_gate_ratchet.py"),
    ("server", "-m unittest discover -s tools/bot-voices/tests"),
    ("server", "-m unittest discover -s tools/lore/tests"),
    ("server", "tools/lore/lint_lore.py"),
    ("server", "tools/lore/repetition.py --check"),
    ("server", "tools/lore/reachability.py --check"),
    ("server", "tools/lore/continuity.py --check"),
    ("server", "tools/lore/depth_report.py --check"),
    ("server", "-m unittest discover -s tools/claude-version/tests"),
    ("server", "-m unittest discover -s tools/perf/tests"),
    # Not a workflow line of its own (source/tools/tests/test_command_reference.py runs it in CI); kept as its own line
    # so a stale ALL SERVER COMMANDS.txt says what to run.
    ("encounter-catalog", "source/tools/make_command_reference.py --check"),
]

# Workflow Python commands that do not run here, by script, with the reason.
NOT_LOCAL: dict[str, str] = {
    "tools/newclient-compat/installed_check.py": "needs Godot and the 0.37 release commit (new-client job)",
    "source/tools/chat_audit.py": "reads the transcripts the server tests write (dotnet; the report: chat audit job)",
    # Its own tests (source/tools/tests/test_ci_test_results.py) run here; the CI lines read what dotnet test wrote.
    "source/tools/ci_test_results.py": "reads the .trx results and the files the dotnet tests write (server, launcher jobs)",
    "tools/bot-voices/eval/evening_read.py": "reads the evenings the server tests write (dotnet)",
    "tools/bot-voices/eval/score_bench.py": "reads the bench replies the server tests write (dotnet)",
}


@dataclass
class Job:
    name: str
    ci: str
    argv: list[str]


def unittest_files(folder: Path) -> list[str]:
    return sorted(p.name for p in folder.glob("test*.py"))


def jobs_for(repo: Path, checks=CHECKS) -> list[Job]:
    out: list[Job] = []
    for ci, command in checks:
        parts = command.split()
        if parts[:3] == ["-m", "unittest", "discover"]:
            folder = parts[parts.index("-s") + 1]
            files = unittest_files(repo / folder)
            if not files:
                out.append(Job(f"{folder} (no test files)", ci, [sys.executable, "-c", "pass"]))
            for name in files:
                out.append(Job(f"{folder}/{name}", ci, [sys.executable, "-m", "unittest", "discover", "-s", folder, "-p", name]))
        else:
            out.append(Job(command, ci, [sys.executable, *parts]))
    return out


def run(job: Job, repo: Path) -> tuple[Job, int, float, str]:
    start = time.monotonic()
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE="1", PYTHONIOENCODING="utf-8")
    proc = subprocess.run(job.argv, cwd=repo, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                          text=True, encoding="utf-8", errors="replace")
    return job, proc.returncode, time.monotonic() - start, proc.stdout


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--repo", type=Path, default=REPO, help="repository to check (default: this one)")
    ap.add_argument("--only", action="append", default=[], help="run only checks whose name contains this (repeatable)")
    ap.add_argument("--jobs", type=int, default=max(os.cpu_count() or 1, 1))
    ap.add_argument("--list", action="store_true", help="list the checks and exit")
    args = ap.parse_args(argv)
    repo = args.repo.resolve()

    jobs = jobs_for(repo)
    if args.only:
        jobs = [j for j in jobs if any(o in j.name for o in args.only)]
        if not jobs:
            print(f"no check matches {args.only}", file=sys.stderr)
            return 2
    if args.list:
        for j in jobs:
            print(f"[{j.ci}] {j.name}")
        for script, why in NOT_LOCAL.items():
            print(f"[not local] {script}: {why}")
        return 0

    start = time.monotonic()
    failed: list[tuple[Job, str]] = []
    # Longest first is unknown ahead of time; the lore and bot-voice suites go first because they are the slow ones.
    jobs.sort(key=lambda j: (not ("lore" in j.name or "bot-voices" in j.name), j.name))
    with ThreadPoolExecutor(max_workers=max(args.jobs, 1)) as pool:
        for job, code, secs, output in pool.map(lambda j: run(j, repo), jobs):
            print(f"{'ok  ' if code == 0 else 'FAIL'} {secs:5.1f}s [{job.ci}] {job.name}", flush=True)
            if code != 0:
                failed.append((job, output))
    for job, output in failed:
        print(f"\n===== FAIL [{job.ci}] {job.name}\n{output.rstrip()}")
    total = time.monotonic() - start
    if failed:
        print(f"\n{len(failed)} of {len(jobs)} checks failed in {total:.0f} s: fix them before you push.")
        return 1
    print(f"\nAll {len(jobs)} checks passed in {total:.0f} s. Not run here: {', '.join(NOT_LOCAL)} (see --list).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
