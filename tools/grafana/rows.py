"""Finds the structured log rows in the server code (issue #628).

A row is one log message that starts with a stable upper-case marker and carries key=value fields
(`MARKER key=value key="text" ...`), or JSON after the marker (`AUTONOMOUS_GOAL_ATTEMPT {...}`). This module reads
source/server (never the tests) and returns, per marker: its writing sites, the log file it lands in, its fields, and
for each field the code that fills it, so the privacy filter can tell which fields may carry a name.

It is a reader of C#, not a parser: it knows the shapes rows are written in here.
- a string literal that starts with the marker: "MARKER key=...", $"MARKER key={x}", "MARKER " + Json
- a const marker (`const string Marker = "MARKER";`, the EconomyLog tags) used as `$"{Marker} ..."`, as
  `new StringBuilder(Marker)` followed by `.Append(" key=")`, or as `EconomyLog.Line(Tag, ("key", value))`
- a doc comment that spells the row, `<c>MARKER key= key=</c>`, which adds its fields

A const marker resolves to the declaration in the innermost class, struct or record that encloses its use (#690): the
LivingRows files declare `const string Marker` once per row class, several to a file, and taking the file's last one gave
one row the sites and fields of another (CC_BREAK read CAST_INTERRUPT's). `new StringBuilder(Marker, 200)` (a capacity
after the marker) is read like `new StringBuilder(Marker)`, and its `.Append(" key=")` chain ends at its `ToString()`.
"""
from __future__ import annotations

import re
from dataclasses import dataclass, field
from pathlib import Path

MARKER = r"[A-Z][A-Z0-9]*(?:_[A-Z0-9]+)+"
MARKER_RE = re.compile(rf"^{MARKER}$")

# Rows that logconfig.xml sends to detail.log (its starts-with filter), read from the file itself.
LOGCONFIG = Path("source/server/GameServer/config/logconfig.xml")
SESSION_CHAT_LOGGER = 'LoggerName = "sessionchat"'

_LITERAL_SITE = re.compile(rf'(?P<prefix>\$@|@\$|\$|@)?"(?P<marker>{MARKER})(?P<after>[ "])')
_CONST = re.compile(rf'\bconst\s+string\s+(?P<name>\w+)\s*=\s*"(?P<marker>{MARKER})"\s*;')
_DOC_ROW = re.compile(rf"<c>(?P<marker>{MARKER})(?P<body>(?: [A-Za-z][A-Za-z0-9_]*=[^ <]*)+)\s*(?:</c>|$)", re.M)
_KEY_IN_TEXT = re.compile(r'(?:(?<= )|(?<=\$")|(?<="))(?P<key>[a-z][A-Za-z0-9_]*)=(?![=>])')
_APPEND_KEY = re.compile(r'\.Append\(\s*"\s*(?P<key>[a-z][A-Za-z0-9_]*)="?\s*"\s*\)')
_PAIR = re.compile(r'\(\s*"(?P<key>[a-z][A-Za-z0-9_]*)"\s*,\s*')
_LINE_CALL = re.compile(r"\bLine\(\s*(?:EconomyLog\.)?(?P<name>\w+)\s*,")
_JSON_AFTER = re.compile(r'^\s*"?\s*\+\s*(?:System\.Text\.Json\.)?JsonSerializer\.Serialize')


@dataclass
class Site:
    path: str
    line: int


@dataclass
class Row:
    marker: str
    sites: list[Site] = field(default_factory=list)
    fields: dict[str, set[str]] = field(default_factory=dict)  # key -> the code expressions that fill it
    json: bool = False
    log: str = "server.log"
    comment: str = ""

    def add_field(self, key: str, expr: str = "") -> None:
        self.fields.setdefault(key, set())
        if expr:
            self.fields[key].add(" ".join(expr.split())[:160])


def detail_prefixes(repo: Path) -> list[str]:
    text = (repo / LOGCONFIG).read_text(encoding="utf-8")
    return re.findall(r"starts-with\('\$\{message\}','([A-Z0-9_]+)'\)", text)


def _statement(text: str, start: int) -> str:
    """The C# statement from start to its ';' (outside strings; good enough for log calls)."""
    i, depth, n = start, 0, len(text)
    in_str = False
    while i < n:
        c = text[i]
        if in_str:
            if c == "\\":
                i += 2
                continue
            if c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c in "({[":
            depth += 1
        elif c in ")}]":
            depth -= 1
            if depth < -1:
                break
        elif c == ";" and depth <= 0:
            break
        i += 1
        if i - start > 4000:
            break
    return text[start:i]


_CHAR = re.compile(r"'(?:\\.[^'\n]{0,4}|[^'\\\n])'")


def _literal_end(text: str, i: int) -> int:
    """The end of the string literal starting at i (its prefix of $ and @ included). An interpolated string's holes
    ({...}) are read as code, so a literal inside a hole, braces and all, is skipped whole."""
    n = len(text)
    j = i
    interpolated = verbatim = False
    while j < n and text[j] in "@$":
        interpolated |= text[j] == "$"
        verbatim |= text[j] == "@"
        j += 1
    if text.startswith('"""', j):   # a raw string literal
        k = text.find('"""', j + 3)
        return n if k < 0 else k + 3
    k = j + 1
    while k < n:
        c = text[k]
        if not verbatim and c == "\\":
            k += 2
        elif c == '"':
            if verbatim and text.startswith('""', k):
                k += 2
            else:
                return k + 1
        elif not verbatim and c == "\n":
            return k
        elif interpolated and c == "{":
            if text.startswith("{{", k):
                k += 2
                continue
            depth, k = 1, k + 1
            while k < n and depth:
                d = text[k]
                if d == '"' or (d in "@$" and text[k + 1:k + 2] in ('"', "@", "$")):
                    k = _literal_end(text, k)
                    continue
                if d == "'" and (ch := _CHAR.match(text, k)):
                    k = ch.end()
                    continue
                depth += {"{": 1, "}": -1}.get(d, 0)
                k += 1
        else:
            k += 1
    return n


def mask(text: str) -> str:
    """The text with comments and string and char literals blanked (same length, newlines kept), for brace matching."""
    out = list(text)
    i, n = 0, len(text)

    def blank(a: int, b: int) -> None:
        for k in range(a, min(b, n)):
            if out[k] != "\n":
                out[k] = " "

    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
        elif c == '"' or (c in "@$" and text[i + 1:i + 2] in ('"', "@", "$") and '"' in text[i + 1:i + 3]):
            j = _literal_end(text, i)
        elif c == "'" and (ch := _CHAR.match(text, i)):
            j = ch.end()
        else:
            i += 1
            continue
        blank(i, j)
        i = j
    return "".join(out)


_TYPE_DECL = re.compile(r"\b(?:class|struct|record|interface)\s+\w+[^;{]*\{")


def type_spans(text: str) -> list[tuple[int, int]]:
    """(start, end) of every class, struct, record and interface body in the text (positions of its braces)."""
    masked = mask(text)
    spans = []
    for m in _TYPE_DECL.finditer(masked):
        start = m.end() - 1
        depth = 0
        for i in range(start, len(masked)):
            if masked[i] == "{":
                depth += 1
            elif masked[i] == "}":
                depth -= 1
                if depth == 0:
                    spans.append((start, i))
                    break
    return spans


def resolve_const(decls: list[tuple[str, int]], spans: list[tuple[int, int]], pos: int) -> str | None:
    """The marker of the const declaration (marker, position) in effect at pos: the one whose innermost enclosing type
    also encloses pos, innermost first; else the only one; else the nearest one before pos."""
    if not decls:
        return None
    if len(decls) == 1:
        return decls[0][0]

    def innermost(at: int):
        inside = [sp for sp in spans if sp[0] < at < sp[1]]
        return min(inside, key=lambda sp: sp[1] - sp[0]) if inside else None

    enclosing = sorted((sp for sp in spans if sp[0] < pos < sp[1]), key=lambda sp: sp[1] - sp[0])
    for sp in enclosing:
        for marker, at in decls:
            if innermost(at) == sp:
                return marker
    before = [d for d in decls if d[1] < pos]
    return (before[-1] if before else decls[0])[0]


def _balanced(text: str, start: int, open_c: str, close_c: str) -> str:
    """The text inside the bracket at start (text[start] == open_c)."""
    depth = 0
    for i in range(start, min(len(text), start + 600)):
        if text[i] == open_c:
            depth += 1
        elif text[i] == close_c:
            depth -= 1
            if depth == 0:
                return text[start + 1:i]
    return text[start + 1:start + 120]


def _fields_from_statement(row: Row, stmt: str) -> None:
    for m in _KEY_IN_TEXT.finditer(stmt):
        key, rest = m.group("key"), stmt[m.end():]
        expr = ""
        stripped = rest.lstrip("\\\"")
        if stripped.startswith("{"):
            expr = _balanced(stripped, 0, "{", "}")
        else:
            ap = re.match(r'[^"]*"\s*\)\s*\.Append\(', rest)
            if ap:
                expr = _balanced(rest, ap.end() - 1, "(", ")")
            else:
                plus = re.match(r'\\?"?\s*"\s*\+\s*', rest)
                if plus:
                    expr = re.split(r'\s*\+\s*"|\s*[;,]\s*', rest[plus.end():], maxsplit=1)[0]
        row.add_field(key, expr)
    for m in _PAIR.finditer(stmt):
        expr = _balanced(stmt, m.start(), "(", ")")
        row.add_field(m.group("key"), expr.split(",", 1)[1] if "," in expr else "")


def _append_fields(row: Row, text: str, start: int) -> None:
    """The .Append(" key=") fragments after a StringBuilder(marker), up to its ToString(), the next StringBuilder or 6000
    characters, whichever comes first."""
    stop = min(len(text), start + 6000)
    ends = [m.start() for m in (re.search(r"StringBuilder\(", text[start:stop]), re.search(r"\.ToString\(\s*\)", text[start:stop]))
            if m]
    block = text[start:start + min(ends)] if ends else text[start:stop]
    for a in _APPEND_KEY.finditer(block):
        expr_start = block.find(".Append(", a.end())
        expr = _balanced(block, expr_start + 7, "(", ")") if expr_start == a.end() else ""
        row.add_field(a.group("key"), expr)
    _fields_from_statement(row, "\n".join(re.findall(r'\.Append\((\$"[^\n]*")\)', block)))


def _comment_before(lines: list[str], index: int) -> str:
    """The comment block just above a line (/// summary or //), joined, first sentence."""
    out: list[str] = []
    i = index - 1
    while i >= 0 and index - i <= 25:
        s = lines[i].strip()
        if s.startswith("//"):
            out.append(re.sub(r"^/+\s?", "", s))
        elif s.startswith("[") or s == "" and out:
            pass
        elif out or s in ("{", "}") or s.startswith(("if", "try", "else", "catch")):
            if out:
                break
        else:
            break
        i -= 1
    text = " ".join(reversed(out))
    text = re.sub(r"</?(?:summary|c|para|see[^>]*|remarks|item|list[^>]*|description)/?>", "", text)
    text = re.sub(r'<see cref="([^"]+)"\s*/>', r"\1", text)
    text = " ".join(text.split())
    m = re.match(r"(.{20,220}?[.:;])(?:\s|$)", text)
    return (m.group(1) if m else text[:220]).replace("|", "/")


def scan(repo: Path) -> dict[str, Row]:
    rows: dict[str, Row] = {}
    detail = detail_prefixes(repo)
    server = repo / "source/server"
    files = sorted(p for p in server.rglob("*.cs") if "Tests" not in p.relative_to(server).parts
                   and not any(part in ("bin", "obj") for part in p.parts))
    # Pass 1: const markers everywhere (EconomyLog's tags are used from other files).
    consts_by_file: dict[Path, dict[str, list[tuple[str, int]]]] = {}
    economy_tags: dict[str, str] = {}
    texts: dict[Path, str] = {}
    for p in files:
        text = p.read_text(encoding="utf-8", errors="replace")
        texts[p] = text
        found: dict[str, list[tuple[str, int]]] = {}
        for m in _CONST.finditer(text):
            name = m.group("name")
            if re.search(r"(Marker|Tag|Row|Prefix)$", name) or p.name == "EconomyLog.cs":
                found.setdefault(name, []).append((m.group("marker"), m.start()))
        consts_by_file[p] = found
        if p.name == "EconomyLog.cs":
            economy_tags = {name: decls[-1][0] for name, decls in found.items()}

    def row_for(marker: str) -> Row:
        return rows.setdefault(marker, Row(marker))

    for p in files:
        text, rel = texts[p], p.relative_to(repo).as_posix()
        lines = text.split("\n")
        consts = consts_by_file[p]
        spans = type_spans(text) if any(len(d) > 1 for d in consts.values()) else []
        session = SESSION_CHAT_LOGGER in text

        def lineno(pos: int) -> int:
            return text.count("\n", 0, pos) + 1

        def site(marker: str, pos: int, stmt: str) -> None:
            ln = lineno(pos)
            if lines[ln - 1].lstrip().startswith("//"):
                return
            r = row_for(marker)
            r.sites.append(Site(rel, ln))
            if session:
                r.log = "session-chat.log"
            if not r.comment:
                r.comment = _comment_before(lines, ln - 1)
            _fields_from_statement(r, stmt)

        for m in _LITERAL_SITE.finditer(text):
            marker, after = m.group("marker"), m.group("after")
            rest = text[m.end() - 1:]
            if after == '"':
                if not re.match(r'"\s*\+', rest):
                    continue  # a bare "MARKER" literal is a comparison or a key, not a row
            elif after == " " and re.match(r' ?\w*:', rest):
                continue  # "MARKER: could not ..." is an error message
            if _JSON_AFTER.match(text[m.end():]) or re.match(r' "\s*\+\s*(?:System\.Text\.Json\.)?JsonSerializer', rest):
                row_for(marker).json = True
            site(marker, m.start(), _statement(text, m.start()))

        for m in re.finditer(rf'StringBuilder\(\s*"(?P<marker>{MARKER})"\s*\)', text):
            site(m.group("marker"), m.start(), _statement(text, m.start()))
            _append_fields(row_for(m.group("marker")), text, m.end())

        for name, decls in consts.items():
            # $"{Marker} ...", new StringBuilder(Marker[, capacity]), Line(Marker, ...), Emit(Marker + ...)
            pattern = re.compile(rf'\$@?"\{{{name}\}}|StringBuilder\(\s*{name}\s*[,)]|\b{name}\s*\+\s*"\s')
            for m in pattern.finditer(text):
                marker = resolve_const(decls, spans, m.start())
                site(marker, m.start(), _statement(text, m.start()))
            for m in re.finditer(rf"StringBuilder\(\s*{name}\s*(?:,[^)]*)?\)", text):
                _append_fields(row_for(resolve_const(decls, spans, m.start())), text, m.end())

        for m in _LINE_CALL.finditer(text):
            name = m.group("name")
            marker = (resolve_const(consts[name], spans, m.start()) if name in consts else None) or economy_tags.get(name)
            if marker:
                site(marker, m.start(), _statement(text, m.start()))

        for m in _DOC_ROW.finditer(text):
            r = rows.get(m.group("marker"))
            if r is None and m.group("marker") not in {marker for decls in consts.values() for marker, _ in decls}:
                continue
            r = row_for(m.group("marker"))
            for key in re.findall(r" ([A-Za-z][A-Za-z0-9_]*)=", m.group("body")):
                r.add_field(key)
            if not r.comment:
                r.comment = _comment_before(lines, lineno(m.start()))

    for r in rows.values():
        if r.log == "server.log" and any(r.marker.startswith(pre) for pre in detail):
            r.log = "detail.log"
        seen, uniq = set(), []
        for s in r.sites:
            if (s.path, s.line) not in seen:
                seen.add((s.path, s.line))
                uniq.append(s)
        r.sites = uniq
    return {k: rows[k] for k in sorted(rows) if rows[k].sites}
