#!/usr/bin/env python3
"""Fails on a new player-visible use of the old project names (issue #641).

The owner, 2026-11-25: the product is "DAoC - Living Server"; no player-visible "Offline DAoC", "OfflineDAoC" or
"shadowofze" outside the credits, the legacy records and the compatibility shims.

    python3 tools/release/check_brand.py           # check (CI: the versions job; tools/check.py)
    python3 tools/release/check_brand.py --list    # every hit it allows, with the reason

What it reads (SURFACES): the text players see. That is the string literals of the launcher, Setup and the game server,
the messages of the install scripts, and the player docs shipped in the package or linked from the README. In C# and
PowerShell it reads string literals only, never comments; in docs it reads every line.

What it allows:
- COMPATIBILITY: names that existing installs, releases and repositories depend on (file names such as OfflineDAoC.exe,
  the pre-1.2.3 package name, the repository and base-files URLs, the upstream forks' repository names). Each pattern
  has its reason.
- brand-allowlist.json: per file, a hit is allowed when its line contains one of the listed texts (each with its reason),
  and whole files (credits, the release notes of releases made under the old name).
Anything else fails, with the file and line. Fix the text, or add an allowlist entry that says why it must stay.
"""
from __future__ import annotations

import argparse
import fnmatch
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
ALLOWLIST = Path(__file__).resolve().parent / "brand-allowlist.json"
OLD = re.compile(r"Offline DAoC|OfflineDAoC|shadowofze")

# (kind, glob relative to the repository). kind: "cs" (string literals), "ps" (PowerShell and cmd: non-comment lines),
# "text" (every line).
SURFACES: list[tuple[str, str]] = [
    ("cs", "source/tools/OfflineDaoc.Launcher/**/*.cs"),
    ("cs", "tools/installer/src/**/*.cs"),
    ("cs", "source/server/GameServer/**/*.cs"),
    ("ps", "tools/fork-build/install-server-build.ps1"),
    ("ps", "tools/fork-build/rollback-server-build.ps1"),
    ("ps", "tools/fork-build/fork-build-common.ps1"),
    ("ps", "tools/fork-build/*.cmd"),
    ("ps", "source/tools/OfflineDaoc.Launcher/Updates/apply-update.ps1"),
    ("text", "README.md"),
    ("text", "QUICK COMMANDS.txt"),
    ("text", "docs/PLAY.md"),
    ("text", "docs/KNOWN-ISSUES.md"),
    ("text", "docs/PLAY-WITH-FRIENDS.md"),
    ("text", "docs/TRANSFER-PROGRESS.md"),
    ("text", "docs/QUICK-COMMANDS.md"),
    ("text", "docs/LIVING-SERVER.md"),
    ("text", "docs/CHARACTER-PASSPORT.md"),
    ("text", "docs/XP-BY-LEVEL-RANGE.md"),
    ("text", "docs/BOT-PERSONALITIES.md"),
    ("text", "docs/BOT-GUILDS.md"),
    ("text", "docs/RELEASE-NOTES-*.md"),
    ("text", "tools/fork-build/README.txt"),
    ("text", "linux/README.md"),
    ("text", "linux/*.desktop"),
    ("text", "CREDITS.md"),
]

# Names that must keep their old spelling, and why. They are removed from a line before it is checked.
COMPATIBILITY: list[tuple[str, str]] = [
    (r"chris4aker/OfflineDAoC-(?:LivingServer|base-files)\b",
     "the GitHub repositories keep their names (renaming them is an owner choice for later)"),
    (r"\bOfflineDAoC-LivingServer-",
     "the package's name up to 1.2.2, still attached to every release for 1.2.x launchers"),
    (r"\bOfflineDAoC\.(?:exe|dll|pdb|deps\.json|runtimeconfig\.json)\b",
     "the launcher's file names in existing installs; OfflineDAoC.exe stays as a copy that opens DAoCLivingServer.exe"),
    (r"\bGet-OfflineDAoC\.ps1\b", "the base game's own downloader, pinned by SHA-256"),
    (r"\bOfflineDAoC-v[\w.*-]*", "the base game's release file names"),
    (r"\bOfflineDAoC036\b", "the client display profile name stored in players' settings"),
    (r"(?:zomboden|stefanrows|FelixGagnonB|DetShaft)/OfflineDAoC[\w-]*", "upstream forks' repository names (credits)"),
    (r"\bOfflineDAoC-True165\b", "the upstream True 1.65 repository's name (credits)"),
    (r"github\.com/shadowofze\b[\w/.-]*", "the original author's profile (credits)"),
    (r"Offline DAoC friends ", "firewall rules a 1.2.x launcher made keep their names and are still removed by them"),
    (r"[\"']OfflineDAoC[\"']", "the old launcher's process name (an update refuses while it runs), and its saved-data folder name"),
    (r"'OfflineDAoC\*'", "game folders found by their old name"),
    (r"%LOCALAPPDATA%\\OfflineDAoC\b", "where existing saved friend profiles and passports live"),
    (r"~/OfflineDAoC/", "the Linux scripts' default game folder, where existing Linux installs are"),
    (r"\"OfflineDAoC\.[A-Z]\w+\"", "internal temporary property keys, never shown"),
    (r"\bOfflineDAoC Join Friend v1\b|\"Offline DAoC Join Friend\"",
     "the DPAPI entropy and description that encrypt saved friend passwords: changing them loses those passwords"),
]

# A C# string literal (regular, verbatim or interpolated), read on one line.
STRING = re.compile(r'@?\$?"(?:[^"\\]|\\.)*"')


def strip_compat(line: str) -> str:
    for pattern, _ in COMPATIBILITY:
        line = re.sub(pattern, "", line)
    return line


def visible_text(kind: str, line: str) -> str:
    """The part of a line a player can see: string literals of code, non-comment lines of scripts, whole doc lines."""
    stripped = line.strip()
    if kind == "cs":
        if stripped.startswith(("//", "/*", "*", "///")):
            return ""
        return " ".join(m.group(0) for m in STRING.finditer(line))
    if kind == "ps":
        low = stripped.lower()
        if stripped.startswith("#") or low.startswith("rem ") or low == "rem" or stripped.startswith("::"):
            return ""
        return line.split(" #")[0]
    return line


def load_allowlist(path: Path = ALLOWLIST) -> dict:
    data = json.loads(path.read_text(encoding="utf-8"))
    for name, entry in data.get("files", {}).items():
        if not entry.get("why"):
            raise SystemExit(f"{path}: the whole-file entry {name} has no reason")
    for name, entries in data.get("lines", {}).items():
        for entry in entries:
            if not entry.get("text") or not entry.get("why"):
                raise SystemExit(f"{path}: an entry for {name} needs both text and why")
    return data


def surface_files(repo: Path, surfaces=SURFACES):
    seen = set()
    for kind, pattern in surfaces:
        for path in sorted(repo.glob(pattern)):
            rel = path.relative_to(repo).as_posix()
            if path.is_file() and rel not in seen and "/bin/" not in rel and "/obj/" not in rel:
                seen.add(rel)
                yield kind, rel, path


def check(repo: Path = REPO, allowlist: dict | None = None, surfaces=SURFACES):
    """(problems, allowed): each a list of (file, line number, line, reason)."""
    allowlist = load_allowlist() if allowlist is None else allowlist
    whole = allowlist.get("files", {})
    lines_ok = allowlist.get("lines", {})
    problems, allowed = [], []
    for kind, rel, path in surface_files(repo, surfaces):
        try:
            text = path.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            continue
        file_entry = next((e for name, e in whole.items() if fnmatch.fnmatch(rel, name)), None)
        for number, line in enumerate(text.splitlines(), 1):
            seen = visible_text(kind, line)
            if not OLD.search(seen):
                continue
            if file_entry:
                allowed.append((rel, number, line.strip(), file_entry["why"]))
                continue
            if not OLD.search(strip_compat(seen)):
                allowed.append((rel, number, line.strip(), "compatibility name"))
                continue
            entry = next((e for e in lines_ok.get(rel, []) if e["text"] in line), None)
            if entry:
                allowed.append((rel, number, line.strip(), entry["why"]))
            else:
                problems.append((rel, number, line.strip(), ""))
    return problems, allowed


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="print every allowed hit with its reason")
    a = ap.parse_args(argv)
    problems, allowed = check()
    if a.list:
        for rel, number, line, why in allowed:
            print(f"allowed  {rel}:{number}: {line[:120]}  ({why})")
    for rel, number, line, _ in problems:
        print(f"{rel}:{number}: player-visible old name: {line[:160]}")
    if problems:
        print(f"\n{len(problems)} player-visible use(s) of Offline DAoC / OfflineDAoC / shadowofze (issue #641). The product is "
              "\"DAoC - Living Server\" (\"Living Server\" for short). Fix the text, or, if it must stay (credits, a legacy "
              f"record, a compatibility name), add it to {ALLOWLIST.relative_to(REPO)} with the reason.")
        return 1
    print(f"Brand check: no new player-visible old name ({len(allowed)} allowed: credits, records and compatibility names).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
