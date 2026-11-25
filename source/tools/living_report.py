#!/usr/bin/env python3
"""One markdown morning summary of a played night, a section per Living Server goal (issue #625).

    python source/tools/living_report.py runtime/server/logs
    python source/tools/living_report.py server.log detail.log --since "11/25 22:00" --hours 10
    python source/tools/living_report.py runtime/server/logs --out morning.md

Each path is a log file or a folder. A folder is read for server.log and detail.log and their rotated copies
(server.log.2026-11-24.0 and so on, oldest first). Every module in living_report_sections/ (not starting with "_") is a
section. A section exposes:

    TITLE = "Groups"                          # its heading
    ROWS = ["GROUP_ENDED"]                    # the row tags it reads; "RVR_*" takes every tag starting with "RVR_"
    def summarize(lines) -> str: ...          # markdown

and gets the raw log lines (newline stripped, "MM/dd HH:mm:ss | LVL | logger | TAG key=value ...") whose message tag is
one of its ROWS, from every file, in time order. living_report_sections/_util.py has optional helpers (tag, fields, stamp).
A section that fails to load or raises is shown as an error in the report; the others still run.

Local logs only: nothing is sent anywhere, and the sections print no bot or player names.
"""
from __future__ import annotations

import argparse
import datetime as dt
import importlib.util
import re
import sys
import traceback
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
SECTIONS = HERE / "living_report_sections"
LOG_NAMES = ("server.log", "detail.log")
ROTATED = re.compile(r"^(server|detail)\.log\.(\d{4}-\d{2}-\d{2})\.(\d+)$")
STAMP = re.compile(r"^(\d\d)/(\d\d) (\d\d):(\d\d):(\d\d) \| ")


@dataclass
class Section:
    module: str
    title: str
    rows: list[str] = field(default_factory=list)
    summarize: object = None
    error: str | None = None
    order: float = 50.0

    def wants(self, tag: str) -> bool:
        for row in self.rows:
            if row.endswith("*") and tag.startswith(row[:-1]) or tag == row:
                return True
        return False


def log_files(paths) -> list[Path]:
    """The log files to read: files as given, folders expanded to server/detail logs, rotated copies first."""
    out: list[Path] = []
    for path in map(Path, paths):
        if path.is_file():
            out.append(path)
            continue
        if not path.is_dir():
            continue
        for name in LOG_NAMES:
            rotated = []
            for child in path.iterdir():
                m = ROTATED.match(child.name)
                if m and child.is_file() and f"{m.group(1)}.log" == name:
                    rotated.append((m.group(2), int(m.group(3)), child))
            out.extend(child for _, _, child in sorted(rotated))
            if (path / name).is_file():
                out.append(path / name)
    seen, unique = set(), []
    for path in out:
        key = path.resolve()
        if key not in seen:
            seen.add(key)
            unique.append(path)
    return unique


def message_of(line: str) -> str:
    """The message part of "MM/dd HH:mm:ss | LVL | logger | message" (the whole line when it has no header)."""
    if not STAMP.match(line):
        return line
    parts = line.split(" | ", 3)
    return parts[3] if len(parts) == 4 else parts[-1]


def tag_of(line: str) -> str:
    return message_of(line).split(" ", 1)[0]


def stamp_of(line: str, year: int) -> dt.datetime | None:
    m = STAMP.match(line)
    if not m:
        return None
    month, day, hour, minute, second = (int(x) for x in m.groups())
    try:
        return dt.datetime(year, month, day, hour, minute, second)
    except ValueError:
        return None


def load_sections(folder: Path = SECTIONS) -> list[Section]:
    """Every section module of the folder, sorted by ORDER then name; a module that fails is a Section with error."""
    sections: list[Section] = []
    if not folder.is_dir():
        return sections
    for path in sorted(folder.glob("*.py")):
        if path.name.startswith("_"):
            continue
        name = path.stem
        try:
            spec = importlib.util.spec_from_file_location(f"living_report_sections.{name}", path)
            module = importlib.util.module_from_spec(spec)
            if str(HERE) not in sys.path:
                sys.path.insert(0, str(HERE))
            spec.loader.exec_module(module)
            title = getattr(module, "TITLE", None)
            rows = getattr(module, "ROWS", None)
            summarize = getattr(module, "summarize", None)
            missing = [what for what, value in (("TITLE", title), ("ROWS", rows), ("summarize", summarize)) if value is None]
            if missing:
                sections.append(Section(name, str(title or name), error=f"missing {', '.join(missing)}"))
                continue
            if not callable(summarize):
                sections.append(Section(name, str(title), error="summarize is not callable"))
                continue
            sections.append(Section(name, str(title), [str(r) for r in rows], summarize,
                                    order=float(getattr(module, "ORDER", 50))))
        except Exception as error:  # a broken section is reported, never fatal
            sections.append(Section(name, name, error=f"could not load: {type(error).__name__}: {error}"))
    sections.sort(key=lambda s: (s.order, s.module))
    return sections


def read_lines(files, sections: list[Section], since=None, until=None, year=None):
    """Lines wanted by any section, per section, in time order across files. Returns (per-section lists, stats)."""
    year = year or dt.date.today().year
    wanted: dict[str, list[tuple[str, int, str]]] = {s.module: [] for s in sections if s.error is None}
    stats = {"files": [], "lines": 0, "first": None, "last": None}
    order = 0
    for path in files:
        count = 0
        try:
            handle = open(path, encoding="utf-8", errors="replace")
        except OSError as error:
            stats["files"].append((str(path), f"unreadable: {error}"))
            continue
        with handle:
            for raw in handle:
                count += 1
                line = raw.rstrip("\r\n")
                head = STAMP.match(line)
                if not head:
                    continue
                key = line[:14]  # "MM/dd HH:mm:ss" sorts in time within a year
                if since or until:
                    at = stamp_of(line, year)
                    if at is None or (since and at < since) or (until and at >= until):
                        continue
                tag = tag_of(line)
                takers = [s for s in sections if s.error is None and s.wants(tag)]
                if not takers:
                    continue
                stats["first"] = key if stats["first"] is None or key < stats["first"] else stats["first"]
                stats["last"] = key if stats["last"] is None or key > stats["last"] else stats["last"]
                order += 1
                for s in takers:
                    wanted[s.module].append((key, order, line))
        stats["files"].append((str(path), count))
        stats["lines"] += count
    return {name: [line for _, _, line in sorted(entries)] for name, entries in wanted.items()}, stats


def run_section(section: Section, lines: list[str]) -> tuple[str, bool]:
    if section.error:
        return f"**Section error:** {section.error}", False
    try:
        text = section.summarize(lines)
        if not isinstance(text, str):
            return f"**Section error:** summarize returned {type(text).__name__}, not text", False
        return text.strip() or "_Nothing to report._", True
    except Exception as error:
        last = traceback.extract_tb(error.__traceback__)[-1:]
        where = f" ({Path(last[0].filename).name}:{last[0].lineno})" if last else ""
        return f"**Section error:** {type(error).__name__}: {error}{where}", False


def build(paths, since=None, until=None, year=None, folder: Path = SECTIONS) -> tuple[str, int]:
    """The report and the number of sections in error."""
    sections = load_sections(folder)
    files = log_files(paths)
    per_section, stats = read_lines(files, sections, since, until, year)
    out = ["# Living Server morning summary", ""]
    if not files:
        out.append("_No log files found in " + ", ".join(str(p) for p in paths) + "._")
    else:
        span = f"{stats['first']} to {stats['last']}" if stats["first"] else "no rows of any section"
        out.append(f"Read {len(files)} file(s), {stats['lines']:,} lines; rows from {span}.")
        for name, count in stats["files"]:
            out.append(f"- `{Path(name).name}`: {count if isinstance(count, str) else f'{count:,} lines'}")
    out.append("")
    if not sections:
        out.append(f"_No sections in {folder}._")
    errors = 0
    for section in sections:
        lines = per_section.get(section.module, [])
        text, ok = run_section(section, lines)
        errors += 0 if ok else 1
        rows = f"{len(lines):,} rows of {', '.join(section.rows)}" if section.rows else "no rows"
        out += [f"## {section.title}", "", f"<sub>{section.module}: {rows}</sub>", "", text, ""]
    return "\n".join(out).rstrip() + "\n", errors


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("logs", nargs="+", help="log files or folders (server.log, detail.log and rotated copies)")
    ap.add_argument("--since", help='"MM/DD HH:MM" (the log\'s own time)')
    ap.add_argument("--hours", type=float, help="hours after --since")
    ap.add_argument("--year", type=int, help="year of the log's dates (default: this year)")
    ap.add_argument("--out", type=Path, help="write the report here instead of printing it")
    ap.add_argument("--strict", action="store_true", help="exit 1 when a section is in error")
    ap.add_argument("--sections", type=Path, default=SECTIONS, help="the sections folder (default: living_report_sections)")
    args = ap.parse_args(argv)
    year = args.year or dt.date.today().year
    since = dt.datetime.strptime(f"{year}/{args.since}", "%Y/%m/%d %H:%M") if args.since else None
    until = since + dt.timedelta(hours=args.hours) if since and args.hours else None
    report, errors = build(args.logs, since, until, year, args.sections)
    if args.out:
        args.out.write_text(report, encoding="utf-8")
    else:
        if hasattr(sys.stdout, "reconfigure"):
            sys.stdout.reconfigure(encoding="utf-8")
        print(report, end="")
    return 1 if args.strict and errors else 0


if __name__ == "__main__":
    sys.exit(main())
