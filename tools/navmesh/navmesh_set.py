#!/usr/bin/env python3
"""The published navmesh set (issue #684): a versioned copy of an install's runtime/server navmeshes and the Detour
library, kept outside Git as the assets of a `navmesh-vN` pre-release, and described by tools/navmesh/manifest.json.

The owner makes a set once with tools/navmesh/Publish-NavmeshSet.ps1 on his PC and uploads it in the browser. CI's
navmesh workflow (.github/workflows/navmesh.yml) downloads it once, checks every hash against the manifest, caches it
and runs the navmesh-only tests (.github/navmesh-tests.txt) against it.

    python tools/navmesh/navmesh_set.py check
        The manifest in Git, the navmesh test list and (once a set is published) the navmesh files the installer patches
        (tools/fork-build/content-patches.json) agree. Needs nothing but the repository; tools/check.py runs it.
    python tools/navmesh/navmesh_set.py status tools/navmesh/manifest.json
        published=true|false (also into $GITHUB_OUTPUT). The placeholder manifest means "no set yet": a notice, exit 0.
    python tools/navmesh/navmesh_set.py fetch tools/navmesh/manifest.json --dest DIR [--from FOLDER]
        Downloads the set's zip parts from its release (public assets, no token), checks each part's size and SHA-256,
        unpacks them into DIR and checks every file. --from reads the parts from a folder instead (tests).
    python tools/navmesh/navmesh_set.py verify tools/navmesh/manifest.json --root DIR
        Every file of the manifest is in DIR with its size and SHA-256, and nothing else is (after a cache restore too).
    python tools/navmesh/navmesh_set.py filter .github/navmesh-tests.txt
        The dotnet test filter that selects the listed tests (they are [Explicit], so only a filter naming them runs them).
    python tools/navmesh/navmesh_set.py results TRX .github/navmesh-tests.txt [--cache-hit true|false]
        Every result, one line each (and in $GITHUB_STEP_SUMMARY). Fails when an entry has no result, or a test did
        anything but pass (a skipped or inconclusive test proves nothing).

A planted mismatch (a part or a file whose bytes differ from the manifest) fails fetch and verify:
tools/navmesh/tests/test_navmesh_set.py. Python standard library only.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import time
import urllib.request
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(REPO / "source" / "tools"))
import ci_test_results as ci  # noqa: E402

MANIFEST = HERE / "manifest.json"
TEST_LIST = REPO / ".github" / "navmesh-tests.txt"
EXCLUDED_LIST = REPO / ".github" / "ci-excluded-tests.txt"
TESTS = REPO / "source" / "server" / "Tests"
CONTENT_PATCHES = REPO / "tools" / "fork-build" / "content-patches.json"

REPOSITORY = "chris4aker/OfflineDAoC-LivingServer"
SCHEMA = 1
# GitHub takes release files under 2 GiB; the owner's script keeps each part under 1.9 GB.
PART_LIMIT = 1_900_000_000
REQUIRED = ("lib/Detour.dll", "navmesh/roads.json", "navmesh/seams.json", "navmesh/pockets.json")
VERSION_RE = re.compile(r"^v[1-9]\d{0,3}$")
PATH_RE = re.compile(r"^(navmesh|lib)/[A-Za-z0-9_][A-Za-z0-9_.-]*$")
PART_RE = re.compile(r"^navmesh-(v[1-9]\d{0,3})\.part(\d{2})\.zip$")
SHA_RE = re.compile(r"^[0-9a-f]{64}$")
MARKER = ".navmesh-set.json"   # the manifest the unpacked set was checked against, written last


class SetError(Exception):
    """The set or its manifest is wrong; the message says what."""


@dataclass(frozen=True)
class Entry:
    path: str
    size: int
    sha256: str
    part: str


@dataclass(frozen=True)
class Part:
    name: str
    size: int
    sha256: str


@dataclass(frozen=True)
class Manifest:
    version: str
    tag: str
    repository: str
    parts: tuple[Part, ...]
    files: tuple[Entry, ...]
    source: dict

    def url(self, part: Part) -> str:
        return f"https://github.com/{self.repository}/releases/download/{self.tag}/{part.name}"


def annotate(kind: str, text: str) -> str:
    return ci.annotate(kind, "Navmesh set", text)


def read_json(path: Path) -> dict:
    try:
        data = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as e:
        raise SetError(f"{path}: not readable JSON ({e})")
    if not isinstance(data, dict):
        raise SetError(f"{path}: not a JSON object")
    return data


def is_placeholder(data: dict) -> bool:
    return data.get("placeholder") is True


def parse(data: dict) -> Manifest:
    """The manifest, checked. Raises SetError with every problem found."""
    problems: list[str] = []

    def need(cond: bool, text: str) -> bool:
        if not cond:
            problems.append(text)
        return cond

    if is_placeholder(data):
        raise SetError("this is the placeholder manifest: no navmesh set is published yet")
    need(data.get("schema") == SCHEMA, f"schema must be {SCHEMA}, not {data.get('schema')!r}")
    version = data.get("version")
    if need(isinstance(version, str) and VERSION_RE.match(version) is not None, f"version {version!r} must be v1, v2, ..."):
        need(data.get("tag") == f"navmesh-{version}", f"tag must be navmesh-{version}, not {data.get('tag')!r}")
    need(data.get("repository") == REPOSITORY, f"repository must be {REPOSITORY}")
    source = data.get("source") if isinstance(data.get("source"), dict) else {}

    parts: list[Part] = []
    raw_parts = data.get("parts")
    if need(isinstance(raw_parts, list) and raw_parts, "parts must list at least one zip"):
        for i, p in enumerate(raw_parts, 1):
            if not need(isinstance(p, dict), f"part {i} is not an object"):
                continue
            name, size, sha = p.get("name"), p.get("bytes"), p.get("sha256")
            m = PART_RE.match(name) if isinstance(name, str) else None
            need(m is not None and m.group(1) == version and int(m.group(2)) == i,
                 f"part {i} must be named navmesh-{version}.part{i:02d}.zip, not {name!r}")
            need(isinstance(size, int) and 0 < size < PART_LIMIT, f"part {name}: bytes must be 1..{PART_LIMIT - 1}")
            need(isinstance(sha, str) and SHA_RE.match(sha) is not None, f"part {name}: sha256 must be 64 lowercase hex")
            parts.append(Part(str(name), size if isinstance(size, int) else -1, str(sha)))
    names = {p.name for p in parts}

    files: list[Entry] = []
    raw_files = data.get("files")
    seen: set[str] = set()
    if need(isinstance(raw_files, list) and raw_files, "files must list the set's files"):
        for i, f in enumerate(raw_files, 1):
            if not need(isinstance(f, dict), f"file {i} is not an object"):
                continue
            path, size, sha, part = f.get("path"), f.get("bytes"), f.get("sha256"), f.get("part")
            if not need(isinstance(path, str) and PATH_RE.match(path) is not None,
                        f"file {i}: path {path!r} must be navmesh/<name> or lib/<name>"):
                continue
            need(path.lower() not in seen, f"{path} is listed twice")
            seen.add(path.lower())
            need(isinstance(size, int) and size >= 0, f"{path}: bytes must be a whole number")
            need(isinstance(sha, str) and SHA_RE.match(sha) is not None, f"{path}: sha256 must be 64 lowercase hex")
            need(part in names, f"{path}: part {part!r} is not one of the parts")
            files.append(Entry(path, size if isinstance(size, int) else -1, str(sha), str(part)))
    for required in REQUIRED:
        need(required in {f.path for f in files}, f"{required} is missing from the set")
    need(any(re.match(r"^navmesh/zone\d{3}\.nav$", f.path) for f in files), "the set has no navmesh/zoneNNN.nav")
    for p in parts:
        need(any(f.part == p.name for f in files), f"part {p.name} holds no file")
    total = data.get("total_bytes")
    need(total == sum(f.size for f in files), f"total_bytes {total!r} is not the sum of the files' bytes")
    if problems:
        raise SetError("; ".join(problems))
    return Manifest(version, data["tag"], data["repository"], tuple(parts), tuple(sorted(files, key=lambda f: f.path)), source)


def load(path: Path) -> Manifest | None:
    """The manifest at path, or None for the placeholder. Raises SetError on a damaged one."""
    data = read_json(path)
    return None if is_placeholder(data) else parse(data)


def sha256_of(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1 << 20), b""):
            digest.update(block)
    return digest.hexdigest()


def manifest_hash(path: Path) -> str:
    return sha256_of(path)


# ---- fetch and verify -------------------------------------------------------------------------------------------

def download(url: str, target: Path, attempts: int = 3, opener=urllib.request.urlopen) -> None:
    """url into target, through a .partial file. Retries a dropped connection; raises SetError after the last try."""
    partial = target.with_name(target.name + ".partial")
    last: Exception | None = None
    for attempt in range(1, attempts + 1):
        try:
            request = urllib.request.Request(url, headers={"User-Agent": "navmesh-set-ci", "Accept": "application/octet-stream"})
            with opener(request, timeout=120) as response, partial.open("wb") as out:
                shutil.copyfileobj(response, out, 1 << 20)
            partial.replace(target)
            return
        except OSError as e:   # URLError and HTTPError are OSErrors
            last = e
            partial.unlink(missing_ok=True)
            if getattr(e, "code", None) == 404:
                break   # not published (yet): trying again won't help
            time.sleep(5 * attempt)
    raise SetError(f"could not download {url}: {last}")


def check_part(path: Path, part: Part) -> None:
    size = path.stat().st_size
    if size != part.size:
        raise SetError(f"{part.name} is {size} bytes, the manifest says {part.size}")
    got = sha256_of(path)
    if got != part.sha256:
        raise SetError(f"{part.name} has SHA-256 {got}, the manifest says {part.sha256}")


def unpack(zip_path: Path, part: Part, manifest: Manifest, dest: Path) -> list[str]:
    """The part's files into dest, each checked as it is written. Returns the paths written."""
    expected = {f.path: f for f in manifest.files if f.part == part.name}
    written: list[str] = []
    with zipfile.ZipFile(zip_path) as archive:
        for info in archive.infolist():
            if info.is_dir():
                continue
            name = info.filename
            entry = expected.get(name)
            if entry is None:
                raise SetError(f"{part.name} holds {name!r}, which the manifest doesn't list in this part")
            target = dest.joinpath(*PurePosixPath(name).parts)
            if dest.resolve() not in target.resolve().parents:
                raise SetError(f"{part.name}: {name!r} would land outside {dest}")
            target.parent.mkdir(parents=True, exist_ok=True)
            digest, size = hashlib.sha256(), 0
            with archive.open(info) as src, target.open("wb") as out:
                for block in iter(lambda: src.read(1 << 20), b""):
                    digest.update(block)
                    size += len(block)
                    out.write(block)
            if size != entry.size or digest.hexdigest() != entry.sha256:
                target.unlink()
                raise SetError(f"{name} in {part.name} is {size} bytes with SHA-256 {digest.hexdigest()}; "
                               f"the manifest says {entry.size} bytes, {entry.sha256}")
            written.append(name)
            del expected[name]
    if expected:
        raise SetError(f"{part.name} lacks {', '.join(sorted(expected))}")
    return written


def verify(manifest: Manifest, root: Path, out=print) -> int:
    """0 when root holds exactly the manifest's files with their sizes and hashes; 1 with an ::error per problem."""
    problems = 0
    listed = {f.path for f in manifest.files}
    for f in manifest.files:
        path = root.joinpath(*PurePosixPath(f.path).parts)
        if not path.is_file():
            out(annotate("error", f"{f.path} is missing from {root}"))
            problems += 1
            continue
        size = path.stat().st_size
        got = sha256_of(path) if size == f.size else None
        if size != f.size or got != f.sha256:
            out(annotate("error", f"{f.path} is {size} bytes, SHA-256 {got or '(not computed)'}; "
                                  f"the manifest says {f.size} bytes, {f.sha256}"))
            problems += 1
    for folder in ("navmesh", "lib"):
        base = root / folder
        if base.is_dir():
            for p in sorted(base.rglob("*")):
                rel = p.relative_to(root).as_posix()
                if p.is_file() and rel not in listed:
                    out(annotate("error", f"{rel} is in {root} but not in the manifest"))
                    problems += 1
    if problems:
        out(annotate("error", f"{problems} problem(s): the navmesh set at {root} is not the one the manifest names"))
        return 1
    out(annotate("notice", f"{len(manifest.files)} files, {sum(f.size for f in manifest.files):,} bytes: every SHA-256 "
                           f"matches the manifest ({manifest.tag})"))
    return 0


def fetch(manifest: Manifest, manifest_path: Path, dest: Path, source: Path | None = None, out=print,
          downloader=download) -> int:
    """Downloads (or reads from source) every part, checks it, unpacks it into dest and checks every file."""
    if dest.exists():
        shutil.rmtree(dest)
    parts_dir = dest.parent / (dest.name + "-parts")
    if parts_dir.exists():
        shutil.rmtree(parts_dir)
    parts_dir.mkdir(parents=True)
    dest.mkdir(parents=True)
    try:
        for part in manifest.parts:
            local = parts_dir / part.name
            if source is not None:
                shutil.copyfile(source / part.name, local)
            else:
                out(f"GET {manifest.url(part)} ({part.size:,} bytes)")
                downloader(manifest.url(part), local)
            check_part(local, part)
            written = unpack(local, part, manifest, dest)
            out(annotate("notice", f"{part.name}: SHA-256 matches; {len(written)} files unpacked and checked"))
            local.unlink()
    except (SetError, zipfile.BadZipFile, OSError) as e:
        out(annotate("error", str(e)))
        shutil.rmtree(dest, ignore_errors=True)
        return 1
    finally:
        shutil.rmtree(parts_dir, ignore_errors=True)
    if verify(manifest, dest, out) != 0:
        shutil.rmtree(dest, ignore_errors=True)
        return 1
    shutil.copyfile(manifest_path, dest / MARKER)
    return 0


# ---- the test list ----------------------------------------------------------------------------------------------

def read_list(path: Path) -> list[tuple[str, str]]:
    entries, problems = ci.parse_excluded(path.read_text(encoding="utf-8"))
    if problems:
        raise SetError(f"{path.name}: " + "; ".join(problems))
    if not entries:
        raise SetError(f"{path.name} lists no test")
    return entries


def test_filter(entries: list[tuple[str, str]]) -> str:
    # The leading dot, as excluded-filter uses: UT_A doesn't match a class whose name ends in UT_A.
    return "|".join(f"FullyQualifiedName~.{name}" for name, _ in entries)


def check_list(entries: list[tuple[str, str]], excluded_text: str, tests: Path = TESTS) -> list[str]:
    """Problems with the navmesh test list: each class exists, is [Explicit] and stays on the excluded list (so the
    server job never runs it); a Class.Method entry names a method of that class."""
    excluded = {name for name, _ in ci.parse_excluded(excluded_text)[0]}
    problems = []
    for name, _ in entries:
        cls, _, method = name.partition(".")
        found = sorted(tests.rglob(f"{cls}.cs"))
        if not found:
            problems.append(f"{name}: no {cls}.cs under {tests}")
            continue
        text = found[0].read_text(encoding="utf-8", errors="replace")
        if not re.search(r"\[[^\]]*TestFixture[^\]]*Explicit|\[[^\]]*Explicit[^\]]*\]\s*(?:\[[^\]]*\]\s*)*public[^{]*class\s+" + cls, text):
            problems.append(f"{name}: the {cls} fixture is not [Explicit]")
        if cls not in excluded:
            problems.append(f"{name}: {cls} must stay on .github/ci-excluded-tests.txt (the server job has no navmeshes)")
        if method and not re.search(r"\bpublic\s+void\s+" + re.escape(method) + r"\s*\(", text):
            problems.append(f"{name}: {cls} has no test method {method}")
    return problems


# ---- the installer's navmesh patches ----------------------------------------------------------------------------

def check_content_patches(manifest: Manifest, patches_text: str) -> list[str]:
    """Each navmesh the installer patches (content-patches.json) is in the set as the patch leaves it (Fixed), so the
    set is the navmesh a player of that build has."""
    try:
        patches = json.loads(patches_text)
    except ValueError as e:
        return [f"content-patches.json is not JSON: {e}"]
    files = {f.path: f for f in manifest.files}
    problems = []
    for patch in patches if isinstance(patches, list) else []:
        path = str(patch.get("Path", ""))
        if not path.startswith("runtime/server/navmesh/"):
            continue
        rel = path[len("runtime/server/"):]
        entry = files.get(rel)
        if entry is None:
            problems.append(f"{rel}: the installer patches it, but it is not in the set")
        elif entry.sha256 != str(patch.get("Fixed", "")).lower():
            problems.append(f"{rel}: the set's SHA-256 {entry.sha256} is not the installer's fixed {patch.get('Fixed')}"
                            f" (the set was captured before that fix, or from another build)")
    return problems


# ---- results ----------------------------------------------------------------------------------------------------

def results(trx: Path, entries: list[tuple[str, str]], cache_hit: str = "", out=print, summary: Path | None = None) -> int:
    if not trx.is_file():
        out(annotate("error", f"no test results at {trx}: the navmesh tests did not run"))
        return 1
    rows, problems = [], 0
    all_results = ci.read_trx(trx)
    for name, reason in entries:
        mine = [r for r in all_results if f".{name}." in f".{r.full_name}." or r.full_name.endswith("." + name)
                or f".{name}(" in f".{r.full_name}"]
        if not mine:
            out(annotate("error", f"{name}: no result: renamed, deleted, or not selected by the filter?"))
            rows.append((name, "NO RESULT", ""))
            problems += 1
        for r in mine:
            line = (r.message.splitlines() or [""])[0]
            rows.append((r.full_name, r.outcome, line))
            out(f"{r.outcome:<12} {r.full_name}" + (f"  {line}" if line else ""))
            if r.outcome != "Passed":
                out(annotate("error", f"{r.full_name}: {r.outcome}" + (f": {line}" if line else "")))
                problems += 1
    passed = sum(1 for _, o, _ in rows if o == "Passed")
    out(annotate("notice" if not problems else "error",
                 f"navmesh tests: {len(rows)} results, {passed} passed, {len(rows) - passed} not passed; cache hit: {cache_hit or 'n/a'}"))
    if summary is not None:
        lines = ["## Navmesh tests", "", f"Cache hit: **{cache_hit or 'n/a'}**", "", "| Test | Outcome | Message |", "|---|---|---|"]
        lines += [f"| `{n}` | {o} | {m.replace('|', '/')[:200]} |" for n, o, m in rows]
        with summary.open("a", encoding="utf-8") as handle:
            handle.write("\n".join(lines) + "\n")
    return 1 if problems else 0


# ---- the local check --------------------------------------------------------------------------------------------

def check(manifest_path: Path = MANIFEST, list_path: Path = TEST_LIST, excluded_path: Path = EXCLUDED_LIST,
          patches_path: Path = CONTENT_PATCHES, tests: Path = TESTS, out=print) -> int:
    problems: list[str] = []
    manifest = None
    try:
        manifest = load(manifest_path)
    except SetError as e:
        problems.append(str(e))
    try:
        problems += check_list(read_list(list_path), excluded_path.read_text(encoding="utf-8"), tests)
    except SetError as e:
        problems.append(str(e))
    if manifest is not None:
        problems += check_content_patches(manifest, patches_path.read_text(encoding="utf-8"))
    for p in problems:
        out(annotate("error", p))
    if not problems:
        out(f"ok: {manifest_path.name} ({'placeholder: no set yet' if manifest is None else manifest.tag}), "
            f"{list_path.name} ({len(read_list(list_path))} entries)")
    return 1 if problems else 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("check", help="the manifest in Git, the test list and the installer's navmesh patches agree")
    s = sub.add_parser("status", help="published=true|false")
    s.add_argument("manifest", type=Path)
    f = sub.add_parser("fetch", help="download, check and unpack the set")
    f.add_argument("manifest", type=Path)
    f.add_argument("--dest", type=Path, required=True)
    f.add_argument("--from", dest="source", type=Path, help="read the parts from this folder instead of GitHub")
    v = sub.add_parser("verify", help="every file matches the manifest")
    v.add_argument("manifest", type=Path)
    v.add_argument("--root", type=Path, required=True)
    t = sub.add_parser("filter", help="the dotnet test filter for the navmesh test list")
    t.add_argument("list", type=Path)
    r = sub.add_parser("results", help="every navmesh test result; fails unless all passed")
    r.add_argument("trx", type=Path)
    r.add_argument("list", type=Path)
    r.add_argument("--cache-hit", default="")
    args = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(newline="\n")
    try:
        if args.cmd == "check":
            return check()
        if args.cmd == "status":
            manifest = load(args.manifest)
            published = manifest is not None
            if published:
                print(annotate("notice", f"navmesh set {manifest.tag}: {len(manifest.files)} files in {len(manifest.parts)} "
                                         f"part(s), from build {manifest.source.get('build', '?')}"))
            else:
                print(annotate("notice", "No navmesh set is published yet (tools/navmesh/manifest.json is the placeholder): "
                                         "the navmesh tests are skipped. See docs/CI.md, \"The navmesh job\"."))
            output = os.environ.get("GITHUB_OUTPUT")
            if output:
                with open(output, "a", encoding="utf-8") as handle:
                    handle.write(f"published={'true' if published else 'false'}\n")
                    handle.write(f"key={manifest_hash(args.manifest)}\n")
            print(f"published={'true' if published else 'false'}")
            return 0
        if args.cmd in ("fetch", "verify"):
            manifest = load(args.manifest)
            if manifest is None:
                print(annotate("error", f"{args.manifest} is the placeholder: there is no set to {args.cmd}"))
                return 1
            if args.cmd == "fetch":
                return fetch(manifest, args.manifest, args.dest, args.source)
            return verify(manifest, args.root)
        if args.cmd == "filter":
            print(test_filter(read_list(args.list)))
            return 0
        summary = os.environ.get("GITHUB_STEP_SUMMARY")
        return results(args.trx, read_list(args.list), args.cache_hit, summary=Path(summary) if summary else None)
    except SetError as e:
        print(annotate("error", str(e)))
        return 1


if __name__ == "__main__":
    sys.exit(main())
