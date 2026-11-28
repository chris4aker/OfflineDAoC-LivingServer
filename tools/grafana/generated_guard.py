#!/usr/bin/env python3
"""Fails when a pull request commits a file main generates (issue #817, owner decision 59A).

docs/LOG-ROWS.md and tools/grafana/alloy/config.alloy are written on main after each merge (kit.py --write --generated,
.github/workflows/generated-files.yml). A pull request that commits them conflicts with the next one that adds a row.

    python3 tools/grafana/generated_guard.py --base HEAD^1      # CI, on the pull request's merge commit
    python3 tools/grafana/generated_guard.py --base origin/main # tools/check.py: what this branch changes since main

It compares the base's merge base with HEAD (git diff BASE...HEAD), so main's own changes to the files never count.
A base that git doesn't know (no origin/main in a fresh clone) is a note, not a failure.
Fix: git checkout origin/main -- docs/LOG-ROWS.md tools/grafana/alloy/config.alloy
"""
from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
GENERATED = ("docs/LOG-ROWS.md", "tools/grafana/alloy/config.alloy")


def changed(repo: Path, base: str) -> list[str] | None:
    """The generated files the range base...HEAD changes, or None when git can't read the range."""
    run = subprocess.run(["git", "diff", "--name-only", f"{base}...HEAD", "--", *GENERATED], cwd=repo,
                         capture_output=True, text=True)
    if run.returncode != 0:
        return None
    return [line for line in run.stdout.splitlines() if line.strip()]


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--base", required=True, help="the branch the pull request merges into, or HEAD^1 on its merge commit")
    ap.add_argument("--repo", type=Path, default=REPO)
    args = ap.parse_args(argv)
    files = changed(args.repo, args.base)
    if files is None:
        print(f"note: git can't compare with {args.base}; the generated-file guard is skipped here (CI runs it)")
        return 0
    if files:
        for f in files:
            print(f"error: this branch changes {f}, which main generates after each merge (#817). Restore main's copy: "
                  f"git checkout origin/main -- {f}", file=sys.stderr)
        return 1
    print(f"Generated files untouched since {args.base}: {', '.join(GENERATED)}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
