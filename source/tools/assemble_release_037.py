"""Add the player files, docs, source and tools to a package built by build_release_037.py.

    python assemble_release_037.py --repo <this repository> --package <staging package folder>

Copies from the repository (never from anyone's playable folder):
  * package-files/ (START OFFLINE DAOC.cmd, IMPORT PROGRESS FROM OLD OFFLINE DAOC.cmd, READ ME FIRST.txt)
  * the root guides, docs/ and the command lists,
  * source/ without build output, local config or test results,
  * tools/pet-art, tools/claude-version and tools/asset-tool,
  * the built progress importer into tools/ProgressImporter.
Then checks that every shipped serverconfig.xml listens on 127.0.0.1 only with UPnP off (build_release_037.py).
"""
from __future__ import annotations

import argparse
import shutil
from pathlib import Path

from build_release_037 import check_shipped_configs

ROOT_FILES = ("README.md", "CHANGELOG.md", "AGENTS.md", "CLAUDE.md", "LICENSE", "THIRD_PARTY.md",
              "QUICK COMMANDS.txt", "ALL SERVER COMMANDS.txt", "DEVELOPMENT - START HERE.md", "RELEASE VERIFICATION.txt")
SKIP_PARTS = {"bin", "obj", "release", "debug", "build", "testresults", "__pycache__", ".vs", "developer-state"}
SKIP_NAMES = {"serverconfig.xml", "account.txt"}


def keep(relative: Path) -> bool:
    parts = [part.lower() for part in relative.parts]
    return not any(part in SKIP_PARTS for part in parts[:-1]) and parts[-1] not in SKIP_NAMES \
        and not parts[-1].endswith((".log", ".trx", ".pyc", ".db", ".sqlite3"))


def copy_tree(source: Path, target: Path, filter_=lambda rel: True) -> int:
    count = 0
    for path in sorted(source.rglob("*")):
        if path.is_file() and filter_(path.relative_to(source)):
            destination = target / path.relative_to(source)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, destination)
            count += 1
    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo", type=Path, required=True)
    parser.add_argument("--package", type=Path, required=True)
    args = parser.parse_args()
    repo, package = args.repo.resolve(), args.package.resolve()
    if not (package / "runtime/server/GameServer.dll").exists():
        raise SystemExit("Not a built package folder")
    for existing in ("source", "docs", "tools/pet-art", "tools/claude-version", "tools/asset-tool", "tools/ProgressImporter"):
        if (package / existing).exists():
            raise SystemExit(f"{existing} already exists in the package; start from a fresh build")

    counts = {}
    counts["player files"] = copy_tree(repo / "package-files", package)
    for name in ROOT_FILES:
        shutil.copy2(repo / name, package / name)
    counts["docs"] = copy_tree(repo / "docs", package / "docs")
    counts["source"] = copy_tree(repo / "source", package / "source", keep)
    for tool in ("pet-art", "claude-version", "asset-tool"):
        counts[f"tools/{tool}"] = copy_tree(repo / "tools" / tool, package / "tools" / tool, keep)
    importer = repo / "source/tools/OfflineDaoc.ProgressImport/bin/Release/net10.0-windows"
    counts["tools/ProgressImporter"] = copy_tree(importer, package / "tools/ProgressImporter")
    (package / "editions/README.txt").write_text(
        "The 0.37 edition without the custom Sluaghbinder class is this 0.37b game plus the files in\r\n"
        "0.37-no-custom-class. DOWNLOAD-AND-PLAY-v0.37.cmd copies them into place automatically:\r\n"
        "  runtime\\data\\opendaoc.sqlite3.db             clean world, classes/enable_sluaghbinder = False\r\n"
        "  runtime\\client-opendaoc\\app\\game.dll         the 0.37 client without the Sluaghbinder class\r\n"
        "Use them only on a NEW install: the database is a clean world with no saves.\r\n", encoding="utf-8")
    for name, count in counts.items():
        print(f"{name}: {count} files")
    # The finished package's server still listens on this PC only, with UPnP off (Living Server 1.1.2).
    for config, values in check_shipped_configs(package / "runtime").items():
        print(f"{config}: {values}")


if __name__ == "__main__":
    main()
