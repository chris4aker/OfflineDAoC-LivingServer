"""Original client: "Not available in this era" in the creation descriptions of out-of-era races and classes (#634).

Living Server is set at DAoC 1.65 (Classic and Shrouded Isles). The server refuses the Catacombs and Labyrinth of the
Minotaur races and classes at character creation, but the original 1.127e client still offers them, and a refused
character just doesn't appear. Owner, 2026-11-25 (#634): build the "Not in this era" text only. This tool rewrites the
description of each out-of-era class (and race, where its description lives in game.dll) so the creation screen says
it isn't available in this era. Buttons, class names, class IDs and the server's refusal are unchanged.

What it changes, and only in the verified 1.x client game.dll (SHA-256 RELEASED_SHA256, the 0.37 edition's client):
- Classes: Heretic, Warlock, Valkyrie, Vampiir and Bainshee (Catacombs), Mauler (Labyrinth of the Minotaur).
- Races: Half Ogre, Frostalf and Shar (Catacombs), Minotaur (Labyrinth), when game.dll holds their description.
  The race and base-class texts of charman/summary.mpk are a stock mpk and are not touched (owner, #634).
- Each description is found by searching the bytes, the way source/new-client/pipeline/build_class_lore.py reads the
  Sluaghbinder's: the name as its own NUL-terminated string, NUL padding, then a description of 40+ printable bytes
  that starts with the name. Each target has an expected count; any surprise refuses the whole patch.
- The new text is written over the old one in place, NUL-padded to the old length, so every string keeps its address
  and byte length. The PE checksum is recomputed (as patch_sluaghbinder_client_races.py does).

It refuses every other file, 0.37b's Sluaghbinder client (SLUAGH_SHA256) included, so legacy b installs are untouched.

    python patch_era_creation_text.py find    <game.dll>     # read-only: what would change (run this on the PC first)
    python patch_era_creation_text.py patch   <game.dll>     # back up the original, then patch in place
    python patch_era_creation_text.py verify  <game.dll>     # exit 0 patched, 1 released (not patched), 2 anything else
    python patch_era_creation_text.py restore <game.dll>     # put the backed-up original back
    python patch_era_creation_text.py content-patch <released game.dll> [--patches tools/fork-build/content-patches.json]

<game.dll> may also be the client folder (runtime/client-opendaoc/app). Close the launcher, the game and the server first.
The backup is <client folder>/backups/era-creation-text-20261125/game.dll, written once and never overwritten.

How players get it: `content-patch` writes the game.dll entry of tools/fork-build/content-patches.json (byte ops against
the released file). The Living Server installer (install-server-build.ps1, Setup, linux/install_living_server.py)
applies it only to a game.dll with the released hash, backs the file up first and checks the result; ROLLBACK.cmd,
Setup's Remove and PREPARE-FOR-UPDATE.cmd put the released file back. That entry can only be made from the real
game.dll (it records the patched file's SHA-256), so it is made on a PC that has the client (#634).
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import json
import os
import re
import shutil
import struct
import sys
from dataclasses import dataclass
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CONTENT_PATCHES = REPO / "tools" / "fork-build" / "content-patches.json"
CONTENT_PATH = "runtime/client-opendaoc/app/game.dll"

# The 1.x original client: the 0.37 edition's game.dll (build_release_037.py EDITION_GAME_DLL_SHA256).
RELEASED_SHA256 = "f55ed6b068e22ce8e1106871c2fad6ee10c18390bbb8b5dc772219ad8c8b83bb"
# Legacy 0.37b's Sluaghbinder client (build_release_037.py SLUAGH_GAME_DLL_SHA256): never patched.
SLUAGH_SHA256 = "e1d471bb19108610ab9c8ca77dd41af40afe716685b03f4cdeda88c05678463b"
KNOWN_OTHER = {SLUAGH_SHA256: "the legacy 0.37b Sluaghbinder client"}

BACKUP = Path("backups") / "era-creation-text-20261125" / "game.dll"

ERA = "Classic and Shrouded Isles"
TEXTS = (f"Not available in this era ({ERA}).", "Not available in this era.")
MIN_DESCRIPTION = 40          # build_class_lore.py: a description is longer than 40 bytes
MAX_PADDING = 16              # NUL bytes between the name string and its description


@dataclass(frozen=True)
class Target:
    kind: str                 # "class" or "race"
    label: str                # how reports name it
    names: tuple[str, ...]    # the name string(s) game.dll may hold
    expansion: str
    least: int                # expected number of descriptions found
    most: int


TARGETS = (
    Target("class", "Heretic", ("Heretic",), "Catacombs", 1, 1),
    Target("class", "Warlock", ("Warlock",), "Catacombs", 1, 1),
    Target("class", "Valkyrie", ("Valkyrie",), "Catacombs", 1, 1),
    Target("class", "Vampiir", ("Vampiir",), "Catacombs", 1, 1),
    Target("class", "Bainshee", ("Bainshee",), "Catacombs", 1, 1),
    # One Mauler per realm (classes 60-62): one shared description or one each.
    Target("class", "Mauler", ("Mauler",), "Labyrinth of the Minotaur", 1, 3),
    # The race texts are in charman/summary.mpk (the #634 research); game.dll may hold none of them.
    Target("race", "Half Ogre", ("Half Ogre", "Half-Ogre", "HalfOgre"), "Catacombs", 0, 1),
    Target("race", "Frostalf", ("Frostalf",), "Catacombs", 0, 1),
    Target("race", "Shar", ("Shar",), "Catacombs", 0, 1),
    Target("race", "Minotaur", ("Minotaur",), "Labyrinth of the Minotaur", 0, 1),
)

PRINTABLE = re.compile(rb"[\x09\x0a\x0d\x20-\x7e\x80-\xff]+")


class Refused(Exception):
    """The file is not one this tool may change."""


@dataclass(frozen=True)
class Slot:
    target: Target
    name_offset: int
    offset: int               # file offset of the description
    length: int               # its length in bytes, without the terminating NUL
    text: bytes

    def replacement(self) -> bytes:
        for text in TEXTS:
            raw = text.encode("ascii")
            if len(raw) <= self.length:
                return raw + b"\0" * (self.length - len(raw))
        raise Refused(f"{self.target.label}: the description at {self.offset:#x} is only {self.length} bytes")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def _starts_with_name(text: bytes, name: str) -> bool:
    low, n = text.lower(), name.lower().encode("latin1")
    return any(low.startswith(p + n) for p in (b"", b"the ", b"a ", b"an "))


def find_slots(data: bytes) -> list[Slot]:
    """Every out-of-era description in data, checked against each target's expected count."""
    slots, problems = [], []
    for target in TARGETS:
        found = []
        for name in target.names:
            raw = name.encode("latin1")
            pattern = re.compile(rb"(?:(?<=\x00)|^)" + re.escape(raw) + rb"\x00{1,%d}" % MAX_PADDING)
            for m in pattern.finditer(data):
                start = m.end()
                end = data.find(b"\0", start)
                if end < 0:
                    continue
                text = data[start:end]
                if len(text) < MIN_DESCRIPTION or PRINTABLE.fullmatch(text) is None:
                    continue
                if not _starts_with_name(text, name):
                    continue
                found.append(Slot(target, m.start(), start, end - start, text))
        if not target.least <= len(found) <= target.most:
            expected = (f"{target.least}" if target.least == target.most else f"{target.least} to {target.most}")
            problems.append(f"{target.label}: found {len(found)} descriptions, expected {expected}"
                            + "".join(f"\n    at {s.offset:#x}: {s.text[:60]!r}" for s in found))
        slots += found
    offsets = sorted(s.offset for s in slots)
    if len(set(offsets)) != len(offsets):
        problems.append("two targets found the same description")
    if problems:
        raise Refused("unexpected client layout; refusing to patch:\n  " + "\n  ".join(problems))
    return sorted(slots, key=lambda s: s.offset)


def _check_pe(data: bytes) -> int:
    """The PE checksum field's file offset, after checking this is the x86 client DLL."""
    if len(data) < 0x40 or data[:2] != b"MZ":
        raise Refused("not a PE image")
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise Refused("not a PE image")
    if struct.unpack_from("<H", data, pe + 4)[0] != 0x14C:
        raise Refused("not the x86 client")
    return pe + 24 + 64


def _write_checksum(data: bytearray) -> None:
    # Same computation as patch_sluaghbinder_client_races.py / _label.py.
    checksum_offset = _check_pe(data)
    struct.pack_into("<I", data, checksum_offset, 0)
    checksum = 0
    for offset in range(0, len(data), 2):
        word = int.from_bytes(data[offset:offset + 2], "little")
        checksum = (checksum + word) & 0xFFFFFFFF
        checksum = (checksum & 0xFFFF) + (checksum >> 16)
    checksum = (checksum & 0xFFFF) + (checksum >> 16)
    struct.pack_into("<I", data, checksum_offset, (checksum + len(data)) & 0xFFFFFFFF)


def check_released(data: bytes, released_sha256: str = RELEASED_SHA256) -> None:
    digest = sha256(data)
    if digest != released_sha256:
        what = KNOWN_OTHER.get(digest, "not the verified 1.127e client")
        raise Refused(f"game.dll SHA-256 {digest} is {what} (expected {released_sha256}); refusing to patch")


def patch_bytes(original: bytes, released_sha256: str = RELEASED_SHA256) -> tuple[bytes, list[Slot]]:
    """The patched file and the descriptions it changed. Refuses anything but the released client."""
    check_released(original, released_sha256)
    _check_pe(original)
    slots = find_slots(original)
    data = bytearray(original)
    for slot in slots:
        new = slot.replacement()
        assert len(new) == slot.length
        data[slot.offset:slot.offset + slot.length] = new
        assert data[slot.offset + slot.length] == 0, "the description lost its terminator"
    _write_checksum(data)
    assert len(data) == len(original)
    return bytes(data), slots


def changed_ranges(old: bytes, new: bytes) -> list[tuple[int, int]]:
    assert len(old) == len(new)
    ranges, i, n = [], 0, len(old)
    while i < n:
        if old[i] != new[i]:
            j = i
            while j < n and old[j] != new[j]:
                j += 1
            ranges.append((i, j))
            i = j
        else:
            i += 1
    return ranges


def content_ops(old: bytes, new: bytes) -> list[dict]:
    """make_content_patch.py's op format ({"copy": [offset, length]} / {"data": base64}) for a same-length patch."""
    ops, at = [], 0
    for start, end in changed_ranges(old, new):
        if start > at:
            ops.append({"copy": [at, start - at]})
        ops.append({"data": base64.b64encode(new[start:end]).decode("ascii")})
        at = end
    if at < len(old):
        ops.append({"copy": [at, len(old) - at]})
    return ops


def apply_ops(old: bytes, ops: list[dict]) -> bytes:
    out = bytearray()
    for op in ops:
        if "copy" in op:
            offset, length = op["copy"]
            out += old[offset:offset + length]
        else:
            out += base64.b64decode(op["data"])
    return bytes(out)


def content_entry(released: bytes, released_sha256: str = RELEASED_SHA256) -> dict:
    patched, _ = patch_bytes(released, released_sha256)
    ops = content_ops(released, patched)
    assert apply_ops(released, ops) == patched
    return {"Path": CONTENT_PATH, "Released": sha256(released), "Fixed": sha256(patched), "Size": len(patched), "Ops": ops}


def write_content_entry(patches: Path, entry: dict) -> None:
    entries = json.loads(patches.read_text(encoding="utf-8")) if patches.exists() else []
    entries = [e for e in entries if e.get("Path") != entry["Path"]] + [entry]
    tmp = patches.with_name(patches.name + ".tmp")
    with open(tmp, "w", encoding="utf-8", newline="\n") as f:
        json.dump(entries, f, indent=1)
        f.write("\n")
    os.replace(tmp, patches)


def known_fixed(patches: Path = CONTENT_PATCHES) -> set[str]:
    """The patched hash(es) the repository's content patch records, if it has a game.dll entry."""
    try:
        entries = json.loads(patches.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return set()
    return {e["Fixed"] for e in entries if e.get("Path") == CONTENT_PATH and e.get("Released") == RELEASED_SHA256}


def resolve(path: Path) -> Path:
    return path / "game.dll" if path.is_dir() else path


def backup_of(dll: Path) -> Path:
    return dll.parent / BACKUP


def state(dll: Path, released_sha256: str = RELEASED_SHA256, fixed: set[str] | None = None) -> str:
    """'released', 'patched' or 'other'."""
    data = dll.read_bytes()
    digest = sha256(data)
    if digest == released_sha256:
        return "released"
    if digest in (known_fixed() if fixed is None else fixed):
        return "patched"
    saved = backup_of(dll)
    if saved.is_file():
        original = saved.read_bytes()
        if sha256(original) == released_sha256:
            try:
                if patch_bytes(original, released_sha256)[0] == data:
                    return "patched"
            except Refused:
                pass
    return "other"


def _atomic_write(path: Path, data: bytes) -> None:
    tmp = path.with_name(path.name + ".era-text.tmp")
    tmp.write_bytes(data)
    try:
        os.replace(tmp, path)
    except PermissionError:
        tmp.unlink(missing_ok=True)
        raise SystemExit(f"{path} is in use: close the launcher, the game and the server, then run this again")


def do_patch(dll: Path, released_sha256: str = RELEASED_SHA256) -> list[Slot]:
    now = state(dll, released_sha256)
    if now == "patched":
        print(f"{dll}: already patched")
        return []
    original = dll.read_bytes()
    patched, slots = patch_bytes(original, released_sha256)     # refuses anything but the released client
    saved = backup_of(dll)
    if saved.exists():
        if sha256(saved.read_bytes()) != released_sha256:
            raise Refused(f"{saved} exists and is not the released game.dll; refusing to overwrite it")
    else:
        saved.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(dll, saved)
        if sha256(saved.read_bytes()) != released_sha256:
            raise Refused(f"backup {saved} failed verification")
    _atomic_write(dll, patched)
    if sha256(dll.read_bytes()) != sha256(patched):
        raise Refused(f"{dll}: the patched file failed verification (the original is in {saved})")
    return slots


def do_restore(dll: Path, released_sha256: str = RELEASED_SHA256) -> None:
    now = state(dll, released_sha256)
    if now == "released":
        print(f"{dll}: already the released game.dll")
        return
    if now != "patched":
        raise Refused(f"{dll} is neither the released nor this tool's patched game.dll; leaving it alone")
    saved = backup_of(dll)
    if not saved.is_file() or sha256(saved.read_bytes()) != released_sha256:
        raise Refused(f"no verified backup at {saved}")
    _atomic_write(dll, saved.read_bytes())
    if sha256(dll.read_bytes()) != released_sha256:
        raise Refused(f"{dll}: the restored file failed verification (the original is still in {saved})")


def _report(slots: list[Slot]) -> None:
    seen = {s.target.label for s in slots}
    for slot in slots:
        print(f"  {slot.target.kind} {slot.target.label} ({slot.target.expansion}): name at {slot.name_offset:#x}, "
              f"description at {slot.offset:#x}, {slot.length} bytes")
        print(f"    old: {slot.text[:100].decode('latin1')!r}{'...' if slot.length > 100 else ''}")
        print(f"    new: {slot.replacement().rstrip(bytes(1)).decode('ascii')!r}")
    for target in TARGETS:
        if target.label not in seen:
            print(f"  {target.kind} {target.label}: no description in game.dll (left as it is)")


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="command", required=True)
    for name in ("find", "patch", "verify", "restore"):
        sub.add_parser(name).add_argument("dll", type=Path, help="game.dll or the client folder")
    cp = sub.add_parser("content-patch")
    cp.add_argument("dll", type=Path, help="the released game.dll (it is only read)")
    cp.add_argument("--patches", type=Path, default=CONTENT_PATCHES)
    args = ap.parse_args(argv)
    dll = resolve(args.dll)
    if not dll.is_file():
        print(f"{dll}: not found", file=sys.stderr)
        return 2
    try:
        if args.command == "find":
            data = dll.read_bytes()
            print(f"game.dll SHA-256 {sha256(data)}")
            patched, slots = patch_bytes(data)
            _report(slots)
            print(f"patched SHA-256 would be {sha256(patched)} (nothing was written)")
        elif args.command == "patch":
            slots = do_patch(dll)
            if slots:
                _report(slots)
                print(f"patched {dll} (SHA-256 {sha256(dll.read_bytes())}); original kept in {backup_of(dll)}")
        elif args.command == "verify":
            now = state(dll)
            print(f"{dll}: {now} (SHA-256 {sha256(dll.read_bytes())})")
            return {"patched": 0, "released": 1}.get(now, 2)
        elif args.command == "restore":
            do_restore(dll)
            print(f"{dll}: the released game.dll is back")
        elif args.command == "content-patch":
            entry = content_entry(dll.read_bytes())
            write_content_entry(args.patches, entry)
            print(f"{args.patches}: {CONTENT_PATH} {entry['Released']} -> {entry['Fixed']}, {len(entry['Ops'])} ops")
    except Refused as e:
        print(f"refused: {e}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
