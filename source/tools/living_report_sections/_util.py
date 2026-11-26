"""Optional helpers for living_report sections (issue #625). Not a section itself (the loader skips "_" modules).

    from living_report_sections._util import tag, fields, stamp, median, table
"""
from __future__ import annotations

import re
import statistics

FIELD = re.compile(r'(\w+)=("([^"]*)"|\S+)')
STAMP = re.compile(r"^(\d\d/\d\d \d\d:\d\d:\d\d) \| ")


def message(line: str) -> str:
    """The message of "MM/dd HH:mm:ss | LVL | logger | message"."""
    if not STAMP.match(line):
        return line
    parts = line.split(" | ", 3)
    return parts[3] if len(parts) == 4 else parts[-1]


def tag(line: str) -> str:
    return message(line).split(" ", 1)[0]


def fields(line: str) -> dict:
    """key=value and key="text" pairs of the line's message (after its tag)."""
    rest = message(line).split(" ", 1)
    text = rest[1] if len(rest) == 2 else ""
    return {m.group(1): (m.group(3) if m.group(3) is not None else m.group(2)) for m in FIELD.finditer(text)}


def stamp(line: str) -> str:
    """"MM/dd HH:mm:ss", or "" for a line without the header."""
    m = STAMP.match(line)
    return m.group(1) if m else ""


def num(value, default=0):
    try:
        number = float(value)
    except (TypeError, ValueError):
        return default
    return int(number) if number.is_integer() else number


def median(values):
    values = [v for v in values if v is not None]
    return statistics.median(values) if values else None


def pct(part, whole) -> str:
    return "-" if not whole else f"{100 * part / whole:.0f}%"


def table(header: list[str], rows: list[list]) -> str:
    """A markdown table."""
    out = ["| " + " | ".join(str(h) for h in header) + " |", "|" + "|".join("---" for _ in header) + "|"]
    for row in rows:
        out.append("| " + " | ".join("" if cell is None else str(cell) for cell in row) + " |")
    return "\n".join(out)
