"""Checks that the README, the build name and the changelog name the same Living Server release.

    python tools/release/check_versions.py            # exit 0 when they agree, 1 with a list of what disagrees

What has to agree (audit 2026-11-03, section 8.6: main said Beta 1.1 after Beta 1.1.1 was out):
- the newest released CHANGELOG-LIVING-SERVER.md heading ("## Beta 1.1.1 — 2026-10-31"). A heading marked
  "(in development)" is not released yet;
- README.md's "**Beta X is out.**" sentence, every link to one of this repository's release tags, the release file
  names it quotes (LivingServer-Setup-<build>.exe, OfflineDAoC-LivingServer-<build>.zip) and its FORK_BUILD name;
- ForkBuildInfo.BuildName: the released build, or the "(in development)" one while the next release is being built;
- MainForm.DisplayVersion: the launcher shows Offline DAoC's version there ("0.37"), so it must be one of
  ForkBuildInfo.BaseRelease's releases; if it ever holds a Living Server version, it follows BuildName's rule.

Standard library only; CI runs it on every push and pull request (.github/workflows/build-and-test.yml).
"""
import argparse
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
REPO = "chris4aker/OfflineDAoC-LivingServer"

README = "README.md"
CHANGELOG = "CHANGELOG-LIVING-SERVER.md"
FORK_BUILD_INFO = "source/server/GameServer/gameutils/ForkBuildInfo.cs"
MAIN_FORM = "source/tools/OfflineDaoc.Launcher/MainForm.cs"

VERSION = r"\d+(?:\.\d+)+"
HEADING = re.compile(rf"^## Beta ({VERSION})\b(.*)$", re.MULTILINE)
IS_OUT = re.compile(rf"\*\*Beta ({VERSION}) is out\.\*\*")
RELEASE_LINK = re.compile(rf"github\.com/{re.escape(REPO)}/releases/(?:tag|download)/v({VERSION})-beta\b", re.IGNORECASE)
FILE_NAME = re.compile(rf"\b(?:LivingServer-Setup|OfflineDAoC-LivingServer)-({VERSION})-beta\.(?:exe|zip)\b")
FORK_BUILD_NAME = re.compile(rf"FORK_BUILD name=({VERSION})-beta\b")
BUILD_NAME = re.compile(r'const string BuildName = "([^"]+)"')
BASE_RELEASE = re.compile(r'const string BaseRelease = "([^"]+)"')
DISPLAY_VERSION = re.compile(r'const string DisplayVersion = "([^"]+)"')


def read(root, relative):
    return (root / relative).read_text(encoding="utf-8-sig")


def changelog_versions(text):
    """(newest released version, version in development or None) from the changelog's '## Beta' headings."""
    released = development = None
    for match in HEADING.finditer(text):
        version, rest = match.group(1), match.group(2)
        if "in development" in rest.lower():
            if released is None and development is None:
                development = version
            continue
        released = version
        break
    return released, development


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def check(root=ROOT):
    """A list of problems (empty when everything agrees)."""
    problems = []
    changelog = read(root, CHANGELOG)
    readme = read(root, README)
    fork = read(root, FORK_BUILD_INFO)
    form = read(root, MAIN_FORM)

    released, development = changelog_versions(changelog)
    if released is None:
        return [f"{CHANGELOG}: no released '## Beta X' heading"]
    build = f"{released}-beta"

    out = IS_OUT.findall(readme)
    if not out:
        problems.append(f"{README}: no \"**Beta X is out.**\" sentence (the newest release is Beta {released})")
    for version in out:
        if version != released:
            problems.append(f"{README}: says \"Beta {version} is out\", but the newest release in {CHANGELOG} is Beta {released}")

    for pattern, what in ((RELEASE_LINK, "links release"), (FILE_NAME, "names the file of release"),
                          (FORK_BUILD_NAME, "quotes the FORK_BUILD name of")):
        for match in pattern.finditer(readme):
            if match.group(1) != released:
                problems.append(f"{README}:{line_of(readme, match.start())}: {what} {match.group(1)}-beta "
                                f"({match.group(0)}), not the newest release {build}")

    allowed = [build] + ([f"{development}-beta"] if development else [])
    name = BUILD_NAME.search(fork)
    if not name:
        problems.append(f"{FORK_BUILD_INFO}: no BuildName")
    elif name.group(1) not in allowed:
        problems.append(f"{FORK_BUILD_INFO}: BuildName is {name.group(1)}, expected {' or '.join(allowed)} "
                        f"(the newest released or in-development heading of {CHANGELOG})")

    base = BASE_RELEASE.search(fork)
    bases = [b.strip() for b in base.group(1).split(",")] if base else []
    display = DISPLAY_VERSION.search(form)
    if display:
        value = display.group(1)
        if value.split(".")[0] not in ("", "0"):   # a Living Server version (1.x and later), not Offline DAoC's 0.x
            if value not in allowed and value + "-beta" not in allowed:
                problems.append(f"{MAIN_FORM}: DisplayVersion is {value}, expected {' or '.join(allowed)}")
        elif value not in bases:
            problems.append(f"{MAIN_FORM}: DisplayVersion is {value}, but ForkBuildInfo.BaseRelease is "
                            f"{base.group(1) if base else '(missing)'}")
    return problems


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=str(ROOT), help="repository root (default: this checkout)")
    a = ap.parse_args(argv)
    root = Path(a.root)
    problems = check(root)
    released, development = changelog_versions(read(root, CHANGELOG))
    if problems:
        for p in problems:
            print(f"::error title=Release versions disagree::{p}" if "GITHUB_ACTIONS" in os.environ else p)
        print(f"{len(problems)} problem(s). Update README.md and the build name together with the changelog.")
        return 1
    print(f"Release versions agree: Beta {released} released"
          + (f", Beta {development} in development" if development else "") + ".")
    return 0


if __name__ == "__main__":
    sys.exit(main())
