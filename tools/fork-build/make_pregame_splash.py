"""Builds the classic client's loading screen, content/pregame-splash.mpk (installed over the game's pregame/splash.mpk
through content-patches.json), from a 1024x768 picture. Living Server 1.2: the splash painting with its "LIVING SERVER"
title, tools/installer/art/living-server-title-1024x768.jpg (made by tools/installer/art/make_art.py splash-title).

    python tools/fork-build/make_pregame_splash.py [--image <jpg/png>] [--out <mpk>] [--mpk-tool <OfflineDaoc.Mpk.dll>]
                                                   [--update-patches]

The same archive source/tools/OfflineDaoc.Mpk/Build-ClientSplash.ps1 makes on Windows, without System.Drawing: eight
identical uncompressed 32-bit TGAs (type 2, BGRA, bottom-left origin, descriptor 8, TRUEVISION-XFILE footer; the client
draws RLE TGAs black), splash1.tga to splash8.tga, packed by OfflineDaoc.Mpk under the internal name splash.mpk. Without
--mpk-tool the tool is built with dotnet into a temporary folder, used and removed. The TGAs get a fixed file time and
OfflineDaoc.Mpk stores each entry's file time, so the same picture always gives the same archive.

Then the archive is checked: unpacked again by the tool, it must hold exactly splash1..8.tga, each the TGA written, and
the TGA must decode to the picture pixel for pixel.

--update-patches points the loading screen's entry in content-patches.json at the new archive ("Fixed" and "Size"),
adds the archive it replaces to "Replaces" (an earlier Living Server loading screen the installer may replace) and
marks it "Keep" (owner 2026-11-07: ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd never put shadowofze's back; see
fork-build-common.ps1, Get-ContentPatchMap).
"""
import argparse
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
IMAGE = ROOT / "tools/installer/art/living-server-title-1024x768.jpg"
OUT = ROOT / "tools/fork-build/content/pregame-splash.mpk"
PATCHES = ROOT / "tools/fork-build/content-patches.json"
SPLASH_PATH = "runtime/client-opendaoc/app/pregame/splash.mpk"
TOOL_PROJECT = ROOT / "source/tools/OfflineDaoc.Mpk/OfflineDaoc.Mpk.csproj"
WIDTH, HEIGHT = 1024, 768
# 2026-11-07 00:00 UTC: the entries' file time inside the archive.
ENTRY_TIME = 1794009600
NAMES = [f"splash{i}.tga" for i in range(1, 9)]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def tga_bytes(img):
    """The stock client's splash TGA: type 2, 32 bits, descriptor 8 (8 alpha bits, bottom-left origin), BGRA rows from
    the bottom up, then the TGA 2.0 footer with no extension or developer area."""
    img = img.convert("RGBA")
    header = struct.pack("<BBB5sHHHHBB", 0, 0, 2, b"\0" * 5, 0, 0, WIDTH, HEIGHT, 32, 8)
    r, g, b, a = img.split()
    bgra = Image.merge("RGBA", (b, g, r, a)).transpose(Image.FLIP_TOP_BOTTOM).tobytes()
    footer = struct.pack("<II", 0, 0) + b"TRUEVISION-XFILE.\0"
    return header + bgra + footer


def decode_tga(data):
    """Reads back exactly the format tga_bytes writes (checked field by field) and returns the picture as RGB."""
    idlen, cmap, kind, _, xo, yo, w, h, bits, desc = struct.unpack("<BBB5sHHHHBB", data[:18])
    assert (idlen, cmap, kind, xo, yo, w, h, bits, desc) == (0, 0, 2, 0, 0, WIDTH, HEIGHT, 32, 8), "TGA header"
    assert data[-26:] == struct.pack("<II", 0, 0) + b"TRUEVISION-XFILE.\0", "TGA footer"
    assert len(data) == 18 + WIDTH * HEIGHT * 4 + 26, "TGA size"
    img = Image.frombytes("RGBA", (WIDTH, HEIGHT), data[18:18 + WIDTH * HEIGHT * 4]).transpose(Image.FLIP_TOP_BOTTOM)
    b, g, r, a = img.split()
    assert a.getextrema() == (255, 255), "TGA alpha must be opaque"
    return Image.merge("RGB", (r, g, b))


def run_tool(tool, *args):
    cmd = ["dotnet", str(tool), *map(str, args)] if str(tool).endswith(".dll") else [str(tool), *map(str, args)]
    subprocess.run(cmd, check=True)


def build_tool(into):
    subprocess.run(["dotnet", "build", str(TOOL_PROJECT), "-c", "Release", "-nologo", "-v", "q", "-o", str(into)],
                   check=True)
    return into / "OfflineDaoc.Mpk.dll"


def update_patches(new_hash, size):
    entries = json.loads(PATCHES.read_text(encoding="utf-8"))
    i, entry = next((i, e) for i, e in enumerate(entries) if e["Path"] == SPLASH_PATH)
    replaces = list(entry.get("Replaces", []))
    if entry["Fixed"] not in (new_hash, entry["Released"]) and entry["Fixed"] not in replaces:
        replaces.append(entry["Fixed"])
    entry = entries[i] = {"Path": entry["Path"], "Released": entry["Released"], "Fixed": new_hash, "Size": size,
                          "Replaces": replaces, "Keep": True, "File": entry["File"]}
    with open(PATCHES, "w", encoding="utf-8", newline="\n") as f:
        json.dump(entries, f, indent=1)
        f.write("\n")
    print(f"content-patches.json: {SPLASH_PATH} Fixed {new_hash}, Replaces {entry.get('Replaces', [])}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--image", type=Path, default=IMAGE)
    ap.add_argument("--out", type=Path, default=OUT)
    ap.add_argument("--mpk-tool", type=Path, help="a built OfflineDaoc.Mpk(.dll); default: build one for this run")
    ap.add_argument("--update-patches", action="store_true")
    a = ap.parse_args()

    picture = Image.open(a.image).convert("RGB")
    if picture.size != (WIDTH, HEIGHT):
        sys.exit(f"{a.image} is {picture.width}x{picture.height}; the loading screen must be {WIDTH}x{HEIGHT}.")
    tga = tga_bytes(picture)

    with tempfile.TemporaryDirectory(prefix="pregame-splash-") as tmp:
        tmp = Path(tmp)
        tool = a.mpk_tool or build_tool(tmp / "tool")
        tgas = tmp / "tgas"
        tgas.mkdir()
        for name in NAMES:
            (tgas / name).write_bytes(tga)
            os.utime(tgas / name, (ENTRY_TIME, ENTRY_TIME))
        a.out.parent.mkdir(parents=True, exist_ok=True)
        run_tool(tool, "pack", tgas, a.out, "splash.mpk")

        back = tmp / "unpacked"
        run_tool(tool, a.out, back)
        found = sorted(p.name for p in back.iterdir())
        if found != sorted(NAMES):
            sys.exit(f"{a.out} holds {found}, not {NAMES}.")
        for name in NAMES:
            if (back / name).read_bytes() != tga:
                sys.exit(f"{name} in {a.out} is not the TGA written.")
        if decode_tga((back / NAMES[0]).read_bytes()).tobytes() != picture.tobytes():
            sys.exit(f"The TGA in {a.out} doesn't decode to {a.image}.")

    digest, size = sha256(a.out), a.out.stat().st_size
    print(f"{a.out}: 8 entries (splash1..8.tga, {len(tga)} bytes each), decode to {a.image.name} exactly")
    print(f"sha256 {digest}  size {size}")
    if a.update_patches:
        update_patches(digest, size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
