"""Build the Offline DAoC 0.37c package: the new client, added to a 0.37b game folder.

    python build_newclient_037.py --client <the new client's development folder (godot\\, tools\\godot_net\\)>
                                  --repo <this repository> --out <new staging folder>\\OfflineDAoC-v0.37c

0.37c is the 0.37b game plus the new client in a new-client\\ folder beside it. The new client has its own launcher
(new-client\\runtime, the same launcher program in its new-client mode) that runs the 0.37b game's own server through
new-client\\runtime\\server_root.txt = ..\\..\\runtime, so both clients share one account, one bot roster and one world:
a character played with the original client continues with the new one and back. Only one launcher runs at a time.

The package folder is laid out as it unpacks into the 0.37b game folder:
    START OFFLINE DAOC - NEW CLIENT.cmd        (beside START OFFLINE DAOC.cmd)
    READ ME - NEW CLIENT.txt
    new-client\\godot\\                          the client (code, converted content, import cache, built network client)
    new-client\\tools\\godot_net\\                the engine that runs it
    new-client\\runtime\\                         its launcher + server_root.txt
    new-client\\save\\                            its settings, keybinds and logs (made on first start)
It runs on the 0.37b game's bundled .NET (tools\\dotnet). Never shipped: developer mode (godot\\dev_mode.txt), the debug
probes (godot\\tests), the editor's state (godot\\.godot\\editor), anyone's save\\ folder, and backup files.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from datetime import datetime, timezone
from pathlib import Path

VERSION = "0.37c"
GODOT_EXE = "Godot_v4.7.2-stable_mono_win64.exe"
SKIP_GODOT_TOP = {"tests", "dev_mode.txt", "save"}
SKIP_PARTS = {"editor", "obj", "bin", ".vs", "__pycache__"}
SKIP_SUFFIXES = (".pre_valkyn", ".bak", ".tmp", ".log", ".orig")
FORBIDDEN = {"dev_mode.txt", "claude_test_account.txt", "account.txt"}


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def keep_godot(relative: Path) -> bool:
    parts = [p.lower() for p in relative.parts]
    if parts[0] in SKIP_GODOT_TOP:
        return False
    # .godot\editor is the editor's own state and .godot\mono\temp\obj the C# build's scratch; the import cache and
    # the built network client (.godot\mono\temp\bin\Debug) ship
    if parts[0] == ".godot":
        if len(parts) > 1 and parts[1] == "editor" or parts[1:4] == ["mono", "temp", "obj"]:
            return False
    elif any(p in SKIP_PARTS for p in parts[:-1]):
        return False
    return not parts[-1].endswith(SKIP_SUFFIXES)


def copy_tree(source: Path, target: Path, keep=lambda rel: True) -> int:
    count = 0
    for path in sorted(source.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(source)
        if not keep(relative):
            continue
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, destination)
        count += 1
    return count


def check_network_client(godot: Path):
    """The C# network client must be built from the current code (the launcher never compiles it)."""
    dll = godot / ".godot/mono/temp/bin/Debug/MMOEngine.dll"
    if not dll.is_file():
        raise SystemExit("The new client's network client is not built: run tools\\build_csharp.ps1 first")
    newest = max((p.stat().st_mtime, p) for p in godot.rglob("*.cs") if ".godot" not in p.parts)
    if newest[0] > dll.stat().st_mtime:
        raise SystemExit(f"{newest[1]} is newer than the built network client: run tools\\build_csharp.ps1 first")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--client", type=Path, required=True)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    client, repo, out = args.client.resolve(), args.repo.resolve(), args.out.resolve()
    if out.exists():
        raise SystemExit(f"{out} exists; refusing to overwrite a staging folder")
    if out.name != f"OfflineDAoC-v{VERSION}":
        raise SystemExit(f"the package folder must be named OfflineDAoC-v{VERSION}")
    godot = client / "godot"
    engine = client / "tools/godot_net"
    if not (godot / "project.godot").is_file() or not (engine / GODOT_EXE).is_file():
        raise SystemExit("--client must hold godot\\project.godot and tools\\godot_net\\" + GODOT_EXE)
    if not (godot / "content/.gdignore").is_file():
        raise SystemExit("godot\\content\\.gdignore is missing: Godot would try to import the whole content folder")
    check_network_client(godot)
    launcher = repo / "source/tools/DAoCLivingServer.Launcher/bin/Release/net10.0-windows"
    if not (launcher / "OfflineDAoC.exe").is_file():
        raise SystemExit("Build the launcher first (source/tools/DAoCLivingServer.Launcher, Release)")

    new = out / "new-client"
    counts = {}
    print("Copying the new client...", flush=True)
    counts["godot"] = copy_tree(godot, new / "godot", keep_godot)
    print("Copying the engine...", flush=True)
    counts["tools/godot_net"] = copy_tree(engine, new / "tools/godot_net")
    print("Installing the new client's launcher...", flush=True)
    counts["runtime"] = copy_tree(launcher, new / "runtime")
    (new / "runtime/server_root.txt").write_text(
        "# The new client's launcher runs the 0.37b game's own server (account, bots and world are shared).\r\n"
        "# This path is relative to this folder: new-client\\runtime -> the game's runtime folder.\r\n"
        "..\\..\\runtime\r\n", encoding="utf-8")
    (new / "save").mkdir(parents=True)
    (new / "save/README.txt").write_text(
        "The new client keeps its settings, keybinds, quickbars and logs here (made on the first start).\r\n"
        "Your account, characters, bots and world are the game's own, in ..\\..\\runtime\\data.\r\n", encoding="utf-8")
    (new / "VERSION.txt").write_text(f"Offline DAoC {VERSION} new client\r\n", encoding="utf-8")
    counts["package files"] = copy_tree(repo / "package-files-0.36c", out)

    bad = [p.relative_to(out).as_posix() for p in out.rglob("*") if p.is_file() and p.name.lower() in FORBIDDEN]
    assert not bad, f"must not ship: {bad}"
    assert not (new / "godot/tests").exists() and not (new / "godot/dev_mode.txt").exists()
    assert (new / "godot/.godot/mono/temp/bin/Debug/MMOEngine.dll").is_file()
    assert (new / "godot/content/.gdignore").is_file()

    report = {"built_utc": datetime.now(timezone.utc).isoformat(), "client": str(client), "counts": counts,
              "network_client_sha256": sha(new / "godot/.godot/mono/temp/bin/Debug/MMOEngine.dll"),
              "launcher_sha256": sha(new / "runtime/OfflineDAoC.dll")}
    (out.parent / f"{out.name}-build-report.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2), flush=True)


if __name__ == "__main__":
    main()
