"""Checks that the README, the build name and the changelog name the same Living Server release.

    python tools/release/check_versions.py            # exit 0 when they agree, 1 with a list of what disagrees
    python tools/release/check_versions.py --tag v1.2 # also: this tag is the newest release, and BuildName is its build
    python tools/release/check_versions.py --tag v1.2.3-rc.1  # a test build (pre-release, issue #637): 1.2.3 is the
                                                    # version in development and BuildName is 1.2.3

What has to agree (audit 2026-11-03, section 8.6: main said Beta 1.1 after Beta 1.1.1 was out):
- the newest released CHANGELOG-LIVING-SERVER.md heading. Up to 1.1.4 the releases were betas ("## Beta 1.1.4 —
  2026-11-12": build 1.1.4-beta, tag v1.1.4-beta); from 1.2 on they are final releases (owner, 2026-11-19: "1.2" final,
  not "Beta 1.2"), written without "Beta" ("## 1.2 — 2026-11-DD": build 1.2, tag v1.2). A heading marked
  "(in development)" is not released yet ("## 1.2 (in development)"), and there can be more than one above the newest
  release (1.2.1's and 1.2's on integration/1.2). "## 0.37-se.1 — ..." and other 0.x headings are not Living Server
  releases;
- README.md's "**Beta X is out.**" sentence for a beta, "**X is out.**" or "**Living Server X is out.**" for a final
  release, every link to one of this repository's release tags, the release file names it quotes
  (LivingServer-Setup-<build>.exe, OfflineDAoC-LivingServer-<build>.zip) and its FORK_BUILD name;
- ForkBuildInfo.BuildName: the released build, or one of the "(in development)" ones while the next release is being
  built ("X" or "X-beta" for "## X (in development)", only "X-beta" for "## Beta X (in development)");
- MainForm.DisplayVersion: the version the launcher shows. Up to 1.2.1 it was pinned to Offline DAoC's base release
  ("0.37"); the owner retired the 0.3x line (AGENTS.md, "One edition from now on", 2026-11-22), so from 1.2.2 on it is
  the Living Server build, read from LauncherBuild.Name (stamped from ForkBuildInfo.BuildName). A pinned string
  constant fails here: it would drift from the build. ForkBuildInfo.BaseRelease still names the game the build
  installs on (BUILD-INFO.txt, the installer and the launcher's package check); it is just not the shown version.

Standard library only; CI runs it on every push and pull request (.github/workflows/build-and-test.yml), and
publish-release runs it with --tag before it publishes anything.
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
BUILD = rf"{VERSION}(?:-beta)?"
END = r"(?![\w.-])"   # the build name ends here: "v1.2" in ".../tag/v1.2)" but not in "v1.2-rc1" or "v1.2.3"
HEADING = re.compile(rf"^## (Beta )?({VERSION})\b(.*)$", re.MULTILINE)
RELEASED_FINAL = re.compile(r"^\s+—\s")   # "## 1.2 — 2026-11-20", not "## 0.37-se.1 — ..." or "## 1.2 (in development)"
IS_OUT = re.compile(rf"\*\*(?:(Beta|Living Server) )?({VERSION}) is out\.\*\*")
RELEASE_LINK = re.compile(rf"github\.com/{re.escape(REPO)}/releases/(?:tag|download)/v({BUILD}){END}", re.IGNORECASE)
FILE_NAME = re.compile(rf"\b(?:LivingServer-Setup|OfflineDAoC-LivingServer)-({BUILD})\.(?:exe|zip)\b")
FORK_BUILD_NAME = re.compile(rf"FORK_BUILD name=({BUILD}){END}")
BUILD_NAME = re.compile(r'const string BuildName = "([^"]+)"')
BASE_RELEASE = re.compile(r'const string BaseRelease = "([^"]+)"')
DISPLAY_VERSION = re.compile(r'const string DisplayVersion = "([^"]+)"')
CANDIDATE_TAG = re.compile(rf"^v({VERSION})-rc\.[1-9]\d*$")
DISPLAY_FROM_BUILD = re.compile(r'string DisplayVersion\s*=>[^;]*\bLauncherBuild\.Name\b')


def build_name(version, beta):
    """The build a release of this version is: "1.1.4-beta" for Beta 1.1.4, "1.2" for the final 1.2."""
    return f"{version}-beta" if beta else version


def display(build):
    """How the release pages name a build: "Beta 1.1.4" for 1.1.4-beta, "1.2" for 1.2."""
    return f"Beta {build[:-len('-beta')]}" if build.endswith("-beta") else build


def is_living_server(version):
    return int(version.split(".")[0]) >= 1


def read(root, relative):
    return (root / relative).read_text(encoding="utf-8-sig")


def changelog_headings(text):
    """(newest released build, [builds allowed while in development above it, newest first]) from the changelog.

    A released heading is "## Beta X — <date>" (build X-beta, up to 1.1.4) or "## X — <date>" (build X, from 1.2).
    A heading in development is "## Beta X (in development)" (build X-beta) or, as the 1.2 line writes them,
    "## X (in development)" (X or X-beta: the 1.2 candidates were built as 1.2-beta before the release was named).
    Other "## X..." headings (0.37-se.1 and the like) are not releases.
    """
    released, development = None, []
    for match in HEADING.finditer(text):
        beta, version, rest = match.group(1), match.group(2), match.group(3)
        if not is_living_server(version):
            continue
        if "in development" in rest.lower():
            for build in ([build_name(version, True)] if beta else [version, build_name(version, True)]):
                if build not in development:
                    development.append(build)
            continue
        if not beta and not RELEASED_FINAL.match(rest):
            continue
        released = build_name(version, bool(beta))
        break
    return released, development


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def check(root=ROOT, tag=None):
    """A list of problems (empty when everything agrees). With tag, also that the tag is the newest release's."""
    problems = []
    changelog = read(root, CHANGELOG)
    readme = read(root, README)
    fork = read(root, FORK_BUILD_INFO)
    form = read(root, MAIN_FORM)

    build, development = changelog_headings(changelog)
    if build is None:
        return [f"{CHANGELOG}: no released '## X — <date>' or '## Beta X — <date>' heading"]
    beta = build.endswith("-beta")
    released = build[:-len("-beta")] if beta else build

    out = IS_OUT.findall(readme)
    if not out:
        problems.append(f"{README}: no \"**{display(build)} is out.**\" sentence (the newest release is {display(build)})")
    for prefix, version in out:
        said = build_name(version, prefix == "Beta")
        if said != build:
            problems.append(f"{README}: says \"{(prefix + ' ') if prefix else ''}{version} is out\", but the newest "
                            f"release in {CHANGELOG} is {display(build)}")

    for pattern, what in ((RELEASE_LINK, "links release"), (FILE_NAME, "names the file of release"),
                          (FORK_BUILD_NAME, "quotes the FORK_BUILD name of")):
        for match in pattern.finditer(readme):
            if match.group(1).lower() != build:
                problems.append(f"{README}:{line_of(readme, match.start())}: {what} {match.group(1)} "
                                f"({match.group(0)}), not the newest release {build}")

    allowed = [build] + [b for b in development if b != build]
    name = BUILD_NAME.search(fork)
    if not name:
        problems.append(f"{FORK_BUILD_INFO}: no BuildName")
    elif name.group(1) not in allowed:
        problems.append(f"{FORK_BUILD_INFO}: BuildName is {name.group(1)}, expected {' or '.join(allowed)} "
                        f"(the newest released or in-development heading of {CHANGELOG})")

    candidate = CANDIDATE_TAG.match(tag or "")
    if candidate:
        # A test build of the version in development: its package is that build (BUILD-INFO.txt BuildName).
        final = candidate.group(1)
        if final not in development:
            problems.append(f"the test-build tag {tag} is not of a version in development in {CHANGELOG} "
                            f"(in development: {', '.join(development) or 'none'})")
        elif name and name.group(1) != final:
            problems.append(f"{FORK_BUILD_INFO}: BuildName is {name.group(1)}, but the test build {tag} needs {final} "
                            "(its package folder and BUILD-INFO.txt are named from it)")
    elif tag is not None:
        if tag != f"v{build}":
            problems.append(f"the tag {tag} is not the newest release in {CHANGELOG}: {display(build)} is tagged v{build}")
        elif name and name.group(1) != build:
            problems.append(f"{FORK_BUILD_INFO}: BuildName is {name.group(1)}, but the release {tag} needs {build} "
                            "(its package folder, zip and BUILD-INFO.txt are named from it)")

    if not BASE_RELEASE.search(fork):
        problems.append(f"{FORK_BUILD_INFO}: no BaseRelease (the Offline DAoC release the build installs on)")
    pinned = DISPLAY_VERSION.search(form)
    if pinned:
        problems.append(f"{MAIN_FORM}: DisplayVersion is pinned to \"{pinned.group(1)}\"; the launcher shows the Living "
                        "Server build, so it must come from LauncherBuild.Name (ForkBuildInfo.BuildName)")
    elif not DISPLAY_FROM_BUILD.search(form):
        problems.append(f"{MAIN_FORM}: DisplayVersion doesn't read LauncherBuild.Name, so the launcher's shown version "
                        "can drift from ForkBuildInfo.BuildName")
    return problems


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=str(ROOT), help="repository root (default: this checkout)")
    ap.add_argument("--tag", help="the tag about to be published: it must be the newest release's (v1.2, v1.1.4-beta)")
    a = ap.parse_args(argv)
    root = Path(a.root)
    problems = check(root, a.tag)
    build, development = changelog_headings(read(root, CHANGELOG))
    if problems:
        for p in problems:
            print(f"::error title=Release versions disagree::{p}" if "GITHUB_ACTIONS" in os.environ else p)
        print(f"{len(problems)} problem(s). Update README.md and the build name together with the changelog.")
        return 1
    print(f"Release versions agree: {display(build)} released (build {build}, tag v{build})"
          + (f", {' and '.join(development)} in development" if development else "") + ".")
    return 0


if __name__ == "__main__":
    sys.exit(main())
