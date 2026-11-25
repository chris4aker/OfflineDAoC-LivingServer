#!/usr/bin/env python3
"""Folds the changelog fragments (changelog.d/*.md) into CHANGELOG-LIVING-SERVER.md (issue #615).

Each pull request adds its own fragment instead of editing the changelog, so pull requests no longer conflict on it.
The format is in changelog.d/README.md. At release (or whenever the owner wants the changelog current):

    python3 source/tools/assemble_changelog.py --check    # validate every fragment, change nothing (CI)
    python3 source/tools/assemble_changelog.py --dry-run  # print the changelog as it would be written
    python3 source/tools/assemble_changelog.py            # write the changelog and delete the folded fragments

Order is stable: within a section the fragments go in by file name (plain code-point order), after what the section
already holds; a section the version's block doesn't have yet is added at the end of the block, in the order of its
first fragment's file name. Running it twice changes nothing the second time (the fragments are gone).
"""
from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
FRAGMENTS = Path("changelog.d")
CHANGELOG = Path("CHANGELOG-LIVING-SERVER.md")
SKIP = {"README.md"}
KEYS = {"version", "section"}
VERSION = re.compile(r"^\d+\.\d+(\.\d+)?$")
NAME = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*\.md$")
HEADING = re.compile(r"^## (?:Beta )?(\d+\.\d+(?:\.\d+)?)(.*)$")


@dataclass
class Fragment:
    path: Path
    version: str
    section: str
    body: str


class FragmentError(Exception):
    pass


def parse(path: Path) -> Fragment:
    text = path.read_text(encoding="utf-8").replace("\r\n", "\n")
    if not NAME.match(path.name):
        raise FragmentError(f"{path.name}: name it <issue-or-branch>.md (letters, digits, '.', '_', '-')")
    lines = text.split("\n")
    if not lines or lines[0].strip() != "---":
        raise FragmentError(f"{path.name}: must start with a '---' front matter line")
    try:
        end = next(i for i in range(1, len(lines)) if lines[i].strip() == "---")
    except StopIteration:
        raise FragmentError(f"{path.name}: the front matter has no closing '---'") from None
    meta: dict[str, str] = {}
    for line in lines[1:end]:
        if not line.strip():
            continue
        key, sep, value = line.partition(":")
        key = key.strip()
        if not sep or key not in KEYS:
            raise FragmentError(f"{path.name}: front matter line {line!r}: only 'version:' and 'section:' are allowed")
        if key in meta:
            raise FragmentError(f"{path.name}: '{key}' is given twice")
        meta[key] = value.strip().strip('"').strip("'")
    for key in sorted(KEYS):
        if not meta.get(key):
            raise FragmentError(f"{path.name}: front matter needs '{key}:'")
    if not VERSION.match(meta["version"]):
        raise FragmentError(f"{path.name}: version {meta['version']!r} is not like 1.2.3")
    if "*" in meta["section"] or "\n" in meta["section"]:
        raise FragmentError(f"{path.name}: write the section without '**' (e.g. 'section: Bosses')")
    body = "\n".join(lines[end + 1:]).strip("\n")
    if not body.strip():
        raise FragmentError(f"{path.name}: the fragment has no text below its front matter")
    if any(line.startswith("#") for line in body.split("\n")):
        raise FragmentError(f"{path.name}: no headings in a fragment; the section comes from 'section:'")
    return Fragment(path, meta["version"], meta["section"], body)


def load(repo: Path) -> tuple[list[Fragment], list[str]]:
    frags, problems = [], []
    folder = repo / FRAGMENTS
    if not folder.is_dir():
        return frags, problems
    for path in sorted(folder.iterdir(), key=lambda p: p.name):
        if path.name in SKIP or path.name.startswith(".") or path.is_dir():
            continue
        try:
            frags.append(parse(path))
        except (FragmentError, UnicodeDecodeError) as e:
            problems.append(str(e))
    return frags, problems


def released(changelog: str) -> set[str]:
    """Versions whose heading is dated (released), not '(in development)'."""
    out = set()
    for line in changelog.split("\n"):
        m = HEADING.match(line)
        if m and "(in development)" not in m.group(2):
            out.add(m.group(1))
    return out


def validate(frags: list[Fragment], changelog: str) -> list[str]:
    done = released(changelog)
    return [f"{f.path.name}: version {f.version} is already released; use the version in development"
            for f in frags if f.version in done]


def block_bounds(lines: list[str], version: str) -> tuple[int, int] | None:
    for i, line in enumerate(lines):
        m = HEADING.match(line)
        if m and m.group(1) == version and "(in development)" in m.group(2):
            end = next((j for j in range(i + 1, len(lines)) if lines[j].startswith("## ")), len(lines))
            return i, end
    return None


def is_section_line(line: str) -> bool:
    return line.startswith("**") and not line.startswith("***")


def trim_end(lines: list[str], start: int, end: int) -> int:
    """Index after the last non-blank line in lines[start:end]."""
    while end > start and not lines[end - 1].strip():
        end -= 1
    return end


def assemble(changelog: str, frags: list[Fragment]) -> str:
    lines = changelog.split("\n")
    for version in sorted({f.version for f in frags}, key=lambda v: [int(x) for x in v.split(".")]):
        mine = sorted((f for f in frags if f.version == version), key=lambda f: f.path.name)
        bounds = block_bounds(lines, version)
        if bounds is None:
            first = next((i for i, line in enumerate(lines) if line.startswith("## ")), len(lines))
            lines[first:first] = [f"## {version} (in development)", ""]
            bounds = (first, first + 2)
        sections: dict[str, list[Fragment]] = {}
        for f in mine:
            sections.setdefault(f.section, []).append(f)
        for section, group in sections.items():
            head, end = bounds
            text = [line for f in group for line in f.body.split("\n")]
            marker = f"**{section}**"
            at = next((i for i in range(head + 1, end) if lines[i].startswith(marker)), None)
            if at is not None:
                stop = next((j for j in range(at + 1, end) if is_section_line(lines[j])), end)
                insert = trim_end(lines, at, stop)
                lines[insert:insert] = text
            else:
                insert = trim_end(lines, head, end)
                lines[insert:insert] = ["", marker] + text
            bounds = block_bounds(lines, version)
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--repo", type=Path, default=REPO)
    mode = ap.add_mutually_exclusive_group()
    mode.add_argument("--check", action="store_true", help="validate the fragments only (CI)")
    mode.add_argument("--dry-run", action="store_true", help="print the assembled changelog, change nothing")
    args = ap.parse_args(argv)
    repo = args.repo.resolve()
    changelog = (repo / CHANGELOG).read_text(encoding="utf-8")
    frags, problems = load(repo)
    problems += validate(frags, changelog)
    if problems:
        for p in problems:
            print(f"changelog.d/{p}", file=sys.stderr)
        print(f"{len(problems)} fragment problem(s); the format is in changelog.d/README.md", file=sys.stderr)
        return 1
    if args.check:
        print(f"changelog fragments: {len(frags)} ok")
        return 0
    result = assemble(changelog, frags)
    if args.dry_run:
        sys.stdout.write(result)
        return 0
    (repo / CHANGELOG).write_text(result, encoding="utf-8", newline="\n")
    for f in frags:
        f.path.unlink()
    print(f"folded {len(frags)} fragment(s) into {CHANGELOG}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
