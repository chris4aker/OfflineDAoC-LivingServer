#!/usr/bin/env python3
"""Writes ALL SERVER COMMANDS.txt from the server's command registrations (the [Cmd]/[CmdAttribute] attributes in
source/server), with each localization key replaced by its English text (source/server/GameServer/language/EN).

Chat audit 2026-11-18 (TEXT-045): the file had been written once from the 0.3 assemblies and missed every later command
(/hire, /lswhy, /personality, /invites, /lead, /lore, /tradewindow, /lstest, /lscraft). Run it after a command is added
or its text changes:

    python3 source/tools/make_command_reference.py          # rewrite the file
    python3 source/tools/make_command_reference.py --check  # exit 1 if the file is out of date (CI)
"""
from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
SERVER = REPO / "source/server"
LANGUAGE = SERVER / "GameServer/language/EN"
OUT = REPO / "ALL SERVER COMMANDS.txt"

HEADER = """OFFLINE DAOC - SERVER COMMAND REFERENCE

{count} command registrations read from the server source (source/tools/make_command_reference.py).
Normal commands can still have level, realm, cooldown or state requirements.
GM/admin commands can change or delete game data. Back up before using them.
Descriptions containing a localization key have no matching shipped English text.

COMMON HELPER AND COMPANION COMMANDS
/spawn opens the helper class picker; /spawn <class> creates that class.
/spawn specs opens the class and build picker; /spawn <class> <spec> creates that build (for macros).
/raid 40 or /raid 80 enables a level-50 helper raid (total includes you).
There is no /spawn 40 or /spawn 80 raid-size command.
/aggressive and /defensive control how your helpers and companions engage; /hold makes your group only defend itself.
/hire beside a healer of your realm hires and manages your companions; /hire help lists every /hire word.
/grind, /tc and /disband are documented in their registrations below.
Fresh accounts are normal players; the launcher GM option is OFF by default.
"""

SECTIONS = [("Player", "NORMAL PLAYER COMMANDS"), ("GM", "GM ONLY COMMANDS"), ("Admin", "ADMIN ONLY COMMANDS")]
KEY = re.compile(r"^[A-Z][A-Za-z0-9]*(\.[A-Za-z0-9_]+){2,}$")
STRING = re.compile(r'@?\$?"((?:[^"\\]|\\.)*)"')
CLASS = re.compile(r"^[ \t]*(?:(?:public|internal|private|protected|sealed|static|abstract|partial)\s+)*class\s+(\w+)", re.M)
NAMESPACE = re.compile(r"^[ \t]*namespace\s+([\w.]+)", re.M)


@dataclass
class Command:
    name: str
    aliases: list[str]
    level: str
    description: str
    usage: list[str]
    handler: str
    order: int = field(default=0)


def translations() -> dict[str, str]:
    out = {}
    for path in LANGUAGE.rglob("*.txt"):
        for line in path.read_text(encoding="utf-8-sig", errors="replace").splitlines():
            match = re.match(r"^([A-Za-z0-9_.]+):\s*(.*)$", line)
            if match and not line.startswith("#"):
                out[match.group(1)] = match.group(2).strip()
    return out


def balanced(text: str, start: int) -> tuple[str, int]:
    """The text inside the parenthesis that opens at start, and the index after it (strings and comments skipped)."""
    depth, i, n = 0, start, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
        elif c == "/" and text.startswith("//", i):
            i = text.find("\n", i)
            i = n if i < 0 else i
            continue
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return text[start + 1:i], i + 1
        i += 1
    raise ValueError("unbalanced attribute")


def split_args(inner: str) -> list[str]:
    """Top-level comma-separated arguments, comments dropped."""
    inner = re.sub(r"//[^\n]*", "", inner)
    args, depth, current, i = [], 0, [], 0
    while i < len(inner):
        c = inner[i]
        if c == '"':
            j = i + 1
            while inner[j] != '"':
                j += 2 if inner[j] == "\\" else 1
            current.append(inner[i:j + 1])
            i = j + 1
            continue
        if c in "({[":
            depth += 1
        elif c in ")}]":
            depth -= 1
        if c == "," and depth == 0:
            args.append("".join(current).strip())
            current = []
        else:
            current.append(c)
        i += 1
    if "".join(current).strip():
        args.append("".join(current).strip())
    return args


def unescape(s: str) -> str:
    return re.sub(r'\\(["\\nt])', lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), s)


def is_array(arg: str) -> bool:
    return arg.lstrip().startswith(("new", "["))


def strings_of(arg: str) -> list[str]:
    """A string argument (literals joined with +) as one string, or an array argument (new[] { } or [ ]) as its strings."""
    if is_array(arg):
        open_at = arg.index("{") if arg.lstrip().startswith("new") else arg.index("[")
        close_at = arg.rindex("}") if arg.lstrip().startswith("new") else arg.rindex("]")
        return [s for part in split_args(arg[open_at + 1:close_at]) for s in strings_of(part)]
    parts = STRING.findall(arg)
    return ["".join(unescape(p) for p in parts)] if parts else []


def level_of(arg: str) -> str | None:
    match = re.search(r"ePrivLevel\.(\w+)", arg)
    return match.group(1) if match else None


def commands() -> list[Command]:
    found = []
    for path in sorted(SERVER.rglob("*.cs")):
        if any(part in ("bin", "obj", "Tests", "build") for part in path.relative_to(SERVER).parts):
            continue
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        if "[Cmd" not in text:
            continue
        for match in re.finditer(r"\[(?:Cmd|CmdAttribute)\(", text):
            line_start = text.rfind("\n", 0, match.start()) + 1
            if "//" in text[line_start:match.start()]:
                continue   # a commented-out command
            inner, after = balanced(text, match.end() - 1)
            args = split_args(inner)
            if not args or not args[0].strip().startswith('"'):
                continue
            name = strings_of(args[0])[0]
            rest = args[1:]
            aliases = []
            if rest and is_array(rest[0]):
                aliases = strings_of(rest[0])
                rest = rest[1:]
            # (name, [aliases], [header key], level, description, usage...): the header is only shown by /cmd itself.
            at = next((i for i, arg in enumerate(rest) if level_of(arg)), None)
            if at is None:
                continue
            level = level_of(rest[at])
            texts = [s for arg in rest[at + 1:] for s in strings_of(arg)]
            handler = CLASS.search(text, after)
            spaces = [m for m in NAMESPACE.finditer(text) if m.start() < match.start()]
            namespace = spaces[-1].group(1) if spaces else ""
            found.append(Command(name.lstrip("&"), [a.lstrip("&") for a in aliases], level, texts[0] if texts else "",
                                 texts[1:], f"{namespace}.{handler.group(1) if handler else '?'}"))
    return found


def render(cmds: list[Command]) -> str:
    words = translations()

    def say(s: str) -> str:
        return words.get(s, s) if KEY.match(s) else s

    out = [HEADER.format(count=len(cmds))]
    for level, title in SECTIONS:
        out.append(title)
        out.append("=" * len(title))
        out.append("")
        for c in sorted((c for c in cmds if c.level == level), key=lambda c: (c.name.lower(), c.handler)):
            alias = f" (aliases: {', '.join('/' + a for a in c.aliases)})" if c.aliases else ""
            out.append(f"/{c.name}{alias}")
            if c.description:
                out.append("  " + say(c.description))
            for usage in c.usage:
                for line in say(usage).split("\n"):
                    out.append("  " + line)
            out.append(f"  Handler: {c.handler}")
            out.append("")
    others = sorted({c.level for c in cmds} - {level for level, _ in SECTIONS})
    if others:
        raise SystemExit(f"unknown privilege levels: {others}")
    return "\n".join(out).rstrip("\n") + "\n"


def main() -> int:
    text = render(commands())
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if current.replace("\r\n", "\n") != text:
            print(f"{OUT.name} is out of date: run python3 source/tools/make_command_reference.py", file=sys.stderr)
            return 1
        return 0
    OUT.write_bytes(text.replace("\n", "\r\n").encode("utf-8"))   # the file is CRLF, for Notepad
    print(f"wrote {OUT.name}: {text.count('  Handler: ')} registrations")
    return 0


if __name__ == "__main__":
    sys.exit(main())
