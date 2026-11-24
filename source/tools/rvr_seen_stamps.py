#!/usr/bin/env python3
"""Stamps each RvR "Seen" row of docs/PLAYED-CHECK-1.2.md with the build it was seen on and whether the RvR code
changed since that build (a `git diff` of the RvR paths from the build to a ref, main by default). RvR audit 3
(docs/rvr-audit-1.2.3/evidence.md F5, #519): every RvR "Seen" was from a build older than the RvR code that shipped,
and nothing in the doc said so.

    python source/tools/rvr_seen_stamps.py                      # print the stamped table (markdown)
    python source/tools/rvr_seen_stamps.py --ref origin/main    # against another ref
    python source/tools/rvr_seen_stamps.py --write              # put the stamp in each row's status cell
    python source/tools/rvr_seen_stamps.py --all                # every Seen row, not only the RvR ones

What it does and does not say:
* A row is "Seen" when a cell says Seen (not "Not seen"). Its build is the newest commit named in backticks in the clause
  of a Seen in that row (up to the next sentence, ";" or "Contradicted"), or, failing that, anywhere in the row. The stamp names that build only: an older build's Seen is
  never given as a newer build's.
* "RvR code changed since: yes" means `git diff <build> <ref>` touches RVR_PATHS. It says nothing on whether the
  change matters to that row: the row is to be seen again on a build without changes, or read with that in mind.
* A build not in the local history (a shallow clone, or a PC build never pushed) is "unknown"; fetch more history
  (`git fetch --deepen=500 origin main`) and run again.
* Only RvR rows by default: rows in a section whose heading names RvR, battlegrounds, sieges or keeps, and rows whose
  first cell does.

Exit code 0; 2 when the doc has no Seen row to stamp.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DOC = ROOT / "docs" / "PLAYED-CHECK-1.2.md"
# The RvR code: every bot file about RvR, battlegrounds, sieges, the frontier, realm events and keeps.
RVR_PATHS = tuple(f":(glob)source/server/GameServer/bots/**/{pattern}" for pattern in
                  ("*Rvr*", "rvr*", "*Battleground*", "*Siege*", "*Frontier*", "*RealmEvent*", "*Keep*"))
RVR_WORDS = re.compile(r"\b(RvR|battlegrounds?|sieges?|keeps?|frontier)\b", re.IGNORECASE)
SEEN = re.compile(r"(?<![Nn]ot )(?<![Nn]ot yet )\bSeen\b")
COMMIT = re.compile(r"`([0-9a-f]{7,12})`")
STAMP = re.compile(r" _\[stamp: [^\]]*\]_")
NEAR_SEEN = 120   # characters after a "Seen" in which its build is named
SEEN_ENDS = re.compile(r"\.\s|;|\bContradicted\b|\b[Nn]ot seen\b|\bOpen\b")   # ... up to the end of its clause


def git(*args: str, cwd: Path = ROOT) -> subprocess.CompletedProcess:
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True)


def known(commit: str, cwd: Path = ROOT) -> bool:
    return git("cat-file", "-e", f"{commit}^{{commit}}", cwd=cwd).returncode == 0


def commit_time(commit: str, cwd: Path = ROOT) -> int:
    out = git("show", "-s", "--format=%ct", commit, cwd=cwd)
    return int(out.stdout.strip() or 0) if out.returncode == 0 else 0


def changed_files(build: str, ref: str, cwd: Path = ROOT) -> list[str] | None:
    """The RvR files changed from build to ref, or None when either is not in the history."""
    if not known(build, cwd) or not known(ref, cwd):
        return None
    out = git("diff", "--name-only", build, ref, "--", *RVR_PATHS, cwd=cwd)
    return [line for line in out.stdout.splitlines() if line.strip()] if out.returncode == 0 else None


def seen_builds(row: str) -> list[str]:
    """Commits named right after a Seen in the row; else every commit in the row. Pure."""
    clean = STAMP.sub("", row)
    near = []
    for m in SEEN.finditer(clean):
        window = clean[m.end():m.end() + NEAR_SEEN]
        window = SEEN_ENDS.split(window, 1)[0]
        near += COMMIT.findall(window)
    return list(dict.fromkeys(near or COMMIT.findall(clean)))


def seen_rows(text: str, everything: bool = False) -> list[tuple[int, str, str]]:
    """(line number, section heading, line) of each table row that says Seen. Pure."""
    heading, rows = "", []
    for number, line in enumerate(text.splitlines(), 1):
        if line.startswith("#"):
            heading = line.lstrip("#").strip()
            continue
        if not line.startswith("|") or line.startswith("|---") or not SEEN.search(STAMP.sub("", line)):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if cells and cells[0] in ("What", "Row", "Check"):
            continue
        if everything or RVR_WORDS.search(heading) or (cells and RVR_WORDS.search(cells[0])):
            rows.append((number, heading, line))
    return rows


def stamp_for(builds: list[str], ref: str, cwd: Path = ROOT) -> tuple[str, str, list[str] | None]:
    """(build, the stamp text, changed files): the newest build named that the history knows, else the first named."""
    if not builds:
        return "-", "no build named in the row", None
    present = [b for b in builds if known(b, cwd)]
    build = max(present, key=lambda b: commit_time(b, cwd)) if present else builds[0]
    files = changed_files(build, ref, cwd)
    short = git("rev-parse", "--short", ref, cwd=cwd).stdout.strip() or ref
    if files is None:
        since = "unknown, the build is not in the local history"
    else:
        since = f"yes, {len(files)} file(s)" if files else "no"
    return build, f"on build `{build}`; RvR code changed since: {since}, to `{ref}` `{short}`", files


def first_cell(line: str, width: int = 70) -> str:
    cell = line.strip().strip("|").split("|")[0].strip()
    return cell if len(cell) <= width else cell[:width - 3] + "..."


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("doc", nargs="?", default=str(DOC))
    parser.add_argument("--ref", default="HEAD", help="the code the Seen is held against (default HEAD)")
    parser.add_argument("--write", action="store_true", help="write the stamps into the doc's status cells")
    parser.add_argument("--all", action="store_true", help="every Seen row, not only the RvR ones")
    parser.add_argument("--repo", default=str(ROOT), help="the git repository (default: this one)")
    args = parser.parse_args(argv)
    path, repo = Path(args.doc), Path(args.repo)
    text = path.read_text(encoding="utf-8")
    rows = seen_rows(text, args.all)
    if not rows:
        print("no Seen row to stamp", file=sys.stderr)
        return 2
    lines = text.splitlines(keepends=True)
    print("| Row | Line | Seen on | RvR code changed since |")
    print("|---|---|---|---|")
    for number, _, line in rows:
        build, stamp, files = stamp_for(seen_builds(line), args.ref, repo)
        since = stamp.split("RvR code changed since: ", 1)[-1] if build != "-" else stamp
        print(f"| {first_cell(line)} | {number} | `{build}` | {since} |" if build != "-" else
              f"| {first_cell(line)} | {number} | - | {since} |")
        if args.write and build != "-":
            body = lines[number - 1].rstrip("\r\n")
            ending = lines[number - 1][len(body):]
            body = STAMP.sub("", body).rstrip()
            if body.endswith("|"):
                body = body[:-1].rstrip() + f" _[stamp: {stamp}]_ |"
            else:
                body += f" _[stamp: {stamp}]_"
            lines[number - 1] = body + ending
    if args.write:
        path.write_text("".join(lines), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
