#!/usr/bin/env python3
"""CI glue that fails honestly (issue #618): test-result files and the files the server tests write must exist and say
what they should, or the step fails.

    python3 source/tools/ci_test_results.py trx RESULTS.trx --title "Server tests"
        Puts the counts on the run page (::notice) and exits 1 when the file is missing or unreadable, when fewer than
        --min-executed tests ran (default 1: a filter that matches nothing is a failure, not a pass), or when any failed
        (each failed test is an ::error line).
    python3 source/tools/ci_test_results.py trx RESULTS.trx --title "Feature tests" --classes UT_A UT_B
        The same, for the tests whose full name contains one of the classes (dotnet test's FullyQualifiedName~ match),
        read from the results the gate already wrote: no second run. Exits 1 also when a class has no test in the file.
    python3 source/tools/ci_test_results.py require --title "Chat audit" 'chat-sim/faction-sim-*.chat.log' ...
        Each pattern (a glob, searched under --root at any depth) must match at least one file; prints the matches,
        one per line, and exits 1 (::error) when one matches nothing.
    python3 source/tools/ci_test_results.py find --root . 'TestResults/server.trx'
        Prints the first match of a pattern under --root (for a step's variable), or nothing.
    python3 source/tools/ci_test_results.py excluded-filter .github/ci-excluded-tests.txt
        Prints the dotnet test filter that leaves out the listed tests, and exits 1 (::error) when an entry has no reason
        and issue link ("UT_Name  # reason (#619)"), names no test, or is listed twice.

Python standard library only. Tests: source/tools/tests/test_ci_test_results.py.
"""
from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from dataclasses import dataclass
from pathlib import Path

NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
# Folders a search never walks into: build output and dependencies, never where the tests write.
SKIP_DIRS = {".git", "node_modules", "obj", ".vs"}


@dataclass
class Result:
    full_name: str
    outcome: str
    message: str = ""


def read_trx(path: Path) -> list[Result]:
    """Every test result in a .trx file, with its full name (class.method) and outcome."""
    root = ET.parse(path).getroot()
    names: dict[str, str] = {}
    for test in root.iter(f"{NS}UnitTest"):
        method = test.find(f"{NS}TestMethod")
        if method is not None:
            names[test.get("id", "")] = f"{method.get('className', '')}.{test.get('name') or method.get('name', '')}"
    out = []
    for result in root.iter(f"{NS}UnitTestResult"):
        message = result.find(f"{NS}Output/{NS}ErrorInfo/{NS}Message")
        out.append(Result(names.get(result.get("testId", ""), result.get("testName", "")), result.get("outcome", ""),
                          (message.text or "").strip() if message is not None else ""))
    return out


def annotate(kind: str, title: str, text: str) -> str:
    # GitHub reads one line per annotation; keep it short and on one line.
    text = " ".join(text.split())
    return f"::{kind} title={title}::{text[:400]}"


def check_trx(path: Path, title: str, classes: list[str], min_executed: int, out=print) -> int:
    if not path.is_file():
        out(annotate("error", title, f"no test results file at {path}: the tests did not run, or wrote elsewhere"))
        return 1
    try:
        results = read_trx(path)
    except ET.ParseError as e:
        out(annotate("error", title, f"{path} is not a readable .trx file: {e}"))
        return 1
    problems = 0
    if classes:
        picked = [r for r in results if any(c in r.full_name for c in classes)]
        for c in classes:
            if not any(c in r.full_name for r in picked):
                out(annotate("error", title, f"no test of {c} in {path.name}: renamed, deleted or filtered out?"))
                problems += 1
        results = picked
    counts = Counter(r.outcome for r in results)
    executed = sum(n for o, n in counts.items() if o not in ("NotExecuted", "Pending", "Disconnected"))
    failed = [r for r in results if r.outcome in ("Failed", "Error", "Timeout", "Aborted")]
    out(annotate("notice", title, f"total {len(results)}, executed {executed}, passed {counts['Passed']}, failed {len(failed)}, "
                                  f"not executed {counts['NotExecuted']}" + (f" ({' '.join(classes)})" if classes else "")))
    for r in failed[:20]:
        out(annotate("error", f"{title}: failed", f"{r.full_name}: {r.message.splitlines()[0] if r.message else r.outcome}"))
    if len(failed) > 20:
        out(annotate("error", title, f"and {len(failed) - 20} more failed tests"))
    if executed < min_executed:
        out(annotate("error", title, f"only {executed} tests ran (at least {min_executed} expected): a filter that matches "
                                     f"nothing is not a pass"))
        problems += 1
    return 1 if failed or problems else 0


def search(root: Path, pattern: str) -> list[Path]:
    """Files under root whose path ends with the pattern's parts (glob per part), at any depth, sorted."""
    found = []
    for p in sorted(root.rglob(pattern)):
        if p.is_file() and not SKIP_DIRS.intersection(p.relative_to(root).parts):
            found.append(p)
    return found


def require(root: Path, title: str, patterns: list[str], out=print) -> int:
    missing = 0
    for pattern in patterns:
        found = search(root, pattern)
        if not found:
            out(annotate("error", title, f"nothing matches {pattern} under {root}: the test that writes it did not run, "
                                         f"or wrote elsewhere"))
            missing += 1
        for p in found:
            out(p.as_posix())
    return 1 if missing else 0


EXCLUDED_RE = re.compile(r"^(?P<name>UT_\w+(?:\.\w+)?)\s+#\s+(?P<reason>\S.*#\d+.*)$")


def parse_excluded(text: str) -> tuple[list[tuple[str, str]], list[str]]:
    """(name, reason) per entry of ci-excluded-tests.txt, and the problems. Blank lines and lines starting with # are notes."""
    entries, problems, seen = [], [], set()
    for number, raw in enumerate(text.splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        m = EXCLUDED_RE.match(line)
        if not m:
            problems.append(f"line {number}: {line!r} needs a reason and an issue link: UT_Name  # reason (#619)")
            continue
        if m["name"] in seen:
            problems.append(f"line {number}: {m['name']} is listed twice")
        seen.add(m["name"])
        entries.append((m["name"], m["reason"]))
    return entries, problems


def excluded_filter(entries: list[tuple[str, str]]) -> str:
    # "!~.UT_Name" as before: the leading dot keeps UT_Name from matching inside another class's name ending in it.
    return "&".join(f"FullyQualifiedName!~.{name}" for name, _ in entries)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    t = sub.add_parser("trx", help="check a .trx file")
    t.add_argument("trx", type=Path)
    t.add_argument("--title", required=True)
    t.add_argument("--classes", nargs="*", default=[])
    t.add_argument("--min-executed", type=int, default=1)
    r = sub.add_parser("require", help="each pattern must match a file")
    r.add_argument("patterns", nargs="+")
    r.add_argument("--title", required=True)
    r.add_argument("--root", type=Path, default=Path("."))
    e = sub.add_parser("excluded-filter", help="the dotnet test filter for ci-excluded-tests.txt")
    e.add_argument("file", type=Path)
    f = sub.add_parser("find", help="print the first match of a pattern")
    f.add_argument("pattern")
    f.add_argument("--root", type=Path, default=Path("."))
    args = ap.parse_args(argv)
    if args.cmd == "trx":
        classes = [c for item in args.classes for c in item.split()]
        return check_trx(args.trx, args.title, classes, args.min_executed)
    if args.cmd == "require":
        return require(args.root, args.title, args.patterns)
    if args.cmd == "excluded-filter":
        entries, problems = parse_excluded(args.file.read_text(encoding="utf-8"))
        for p in problems:
            print(annotate("error", "Excluded tests", f"{args.file}: {p}"))
        if problems:
            return 1
        print(excluded_filter(entries))
        return 0
    found = search(args.root, args.pattern)
    if found:
        print(found[0].as_posix())
    return 0


if __name__ == "__main__":
    sys.exit(main())
