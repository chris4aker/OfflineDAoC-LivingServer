"""Publishes a Living Server release from a build-and-test push run on main (.github/workflows/publish-release.yml).

For sessions that can't create releases or upload files themselves: the workflow runs this on GitHub's runner.

    python tools/release/publish_release.py package --artifact DIR --tag v1.2 --commit SHA --out DIR
        Checks the push run's package (its one LivingServer-<build> folder, BUILD-INFO.txt naming this build, this
        commit and main, GameServer.dll matching its hash) and zips it as OfflineDAoC-LivingServer-<build>.zip in the
        layout of the earlier releases: one top folder, folder entries, files in name order. Prints its SHA-256.
    python tools/release/publish_release.py notes --notes docs/RELEASE-NOTES-1.2.md --tag v1.2 --zip ZIP --out FILE
        The release notes: the text below the notes file's first "---" line, with the zip row's placeholder replaced
        by the zip's SHA-256. The Setup row must still read "(filled in when the exe is built)" (publish_setup.py
        fills it), and no other placeholder may be left.
    python tools/release/publish_release.py publish --tag v1.2 --commit SHA --title T --body FILE --zip ZIP [--dry-run]
        Creates the tag and a draft release on that commit, attaches the zip, checks GitHub's SHA-256 of it, then
        publishes the release as Latest (not a pre-release). Stops, changing nothing, when the tag or a release for it
        exists already. --dry-run does every check and prints what it would do.

Test builds (issue #637): with --prerelease, the tag is a release candidate v<X>-rc.<N> (v1.2.3-rc.1). The push
run's package is still build X (its LivingServer-<X> folder and BUILD-INFO.txt BuildName=X, checked the same way), zipped
as OfflineDAoC-LivingServer-<X>-rc.<N>.zip; `notes --prerelease` writes short player-facing notes (no notes file
needed); `publish --prerelease` publishes it as a pre-release that never becomes Latest (make_latest false), so
/releases/latest and every player on the normal channel keep the final release. A candidate tag without --prerelease,
or --prerelease with a final tag, is refused.

A release published by the workflow's token doesn't start the release workflow (GitHub doesn't start workflows from
that token's events), so Setup is built by running "release" by hand afterwards (docs/DEVELOPMENT.md).
Standard library only. Needs GITHUB_TOKEN (or GH_TOKEN) to publish.
"""
import argparse
import hashlib
import json
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

REPO = os.environ.get("GITHUB_REPOSITORY", "chris4aker/OfflineDAoC-LivingServer")
API = os.environ.get("GITHUB_API_URL", "https://api.github.com")
SETUP_PLACEHOLDER = "(filled in when the exe is built)"
ZIP_PLACEHOLDER = "(zip SHA-256: filled in by hand at publish)"
# v1.1.4-beta (the betas, up to 1.1.4) or v1.2 (final releases, from 1.2 on: owner 2026-11-19). The build is the tag
# without its "v"; 0.x tags (v0.37) are Offline DAoC's own releases, never a Living Server one.
TAG = re.compile(r"^v((\d+)(?:\.\d+)+(?:-beta)?)$")
# A release candidate, published as a GitHub pre-release (issue #637): v1.2.3-rc.1. Its package is build 1.2.3.
RC_TAG = re.compile(r"^v((\d+)(?:\.\d+)+)-rc\.([1-9]\d*)$")
SHA1 = re.compile(r"^[0-9a-f]{40}$")
LEFTOVER = re.compile(r"\((?:owner|zip SHA-256)[^)]*\)|@@[A-Z_]+@@")


class Stop(Exception):
    """A problem a person has to look at; nothing was changed."""


def build_of(tag):
    m = TAG.match(tag or "")
    if not m or int(m.group(2)) < 1:
        raise Stop(f"{tag} is not a Living Server release tag (v<version> or v<version>-beta, e.g. v1.2 or "
                   "v1.1.4-beta)")
    return m.group(1)


def candidate_of(tag):
    """(package build, tag build) of a release-candidate tag: ("1.2.3", "1.2.3-rc.1") for v1.2.3-rc.1."""
    m = RC_TAG.match(tag or "")
    if not m or int(m.group(2)) < 1:
        raise Stop(f"{tag} is not a test-build tag (v<version>-rc.<N>, e.g. v1.2.3-rc.1)")
    return m.group(1), f"{m.group(1)}-rc.{m.group(3)}"


def builds_of(tag, prerelease):
    """(package build, release build): the same for a final release; for a pre-release the candidate's final build
    and its own name. A candidate tag must be published as a pre-release, and a pre-release must have one."""
    if prerelease:
        return candidate_of(tag)
    if RC_TAG.match(tag or ""):
        raise Stop(f"{tag} is a test-build tag: publish it as a pre-release (prerelease input on)")
    build = build_of(tag)
    return build, build


# Issue #641 (owner, 2026-11-25): from 1.2.3 the files are named for the product, "DAoC - Living Server":
# DAoC-LivingServer-<build>.zip and DAoC-LivingServer-Setup-<build>.exe. Up to 1.2.2 they were
# OfflineDAoC-LivingServer-<build>.zip and LivingServer-Setup-<build>.exe, and those releases keep their names. A 1.2.3
# or later release also carries the package under its old name (legacy_zip_name), because 1.2.0-1.2.2 launchers look
# for exactly that name to update.
RENAMED_FROM = (1, 2, 3)


def renamed(build):
    """True for a build named the new way (1.2.3 and later, its candidates included)."""
    m = re.match(r"^(\d+(?:\.\d+)*)", build or "")
    if not m:
        return True
    parts = tuple(int(p) for p in m.group(1).split("."))
    parts = parts + (0,) * (3 - len(parts))
    return parts >= RENAMED_FROM


def zip_name(build):
    return f"DAoC-LivingServer-{build}.zip" if renamed(build) else f"OfflineDAoC-LivingServer-{build}.zip"


def legacy_zip_name(build):
    """The package's pre-1.2.3 name: the copy a renamed release carries for older launchers, or None if it needs none."""
    return f"OfflineDAoC-LivingServer-{build}.zip" if renamed(build) else None


def setup_name(build):
    return f"DAoC-LivingServer-Setup-{build}.exe" if renamed(build) else f"LivingServer-Setup-{build}.exe"


# The update contract (docs/RELEASE-TRAIN.md, #685): released launchers download a package only from
# https://github.com/<repo>/releases/download/ (ReleaseFeed.DownloadPrefix, an ordinal match, so the repository path's
# case counts), and only with GitHub's digest in this form (PackageDigest.Sha256Of). update_contract.py builds the
# candidate's release with these, and publish() checks GitHub's answer with them.
def asset_url(repo, tag, name):
    return f"https://github.com/{repo}/releases/download/{urllib.parse.quote(tag)}/{urllib.parse.quote(name)}"


def github_digest(sha):
    return f"sha256:{sha}"


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def read_build_info(path):
    text = Path(path).read_text(encoding="utf-8-sig")
    return dict(line.split("=", 1) for line in text.splitlines() if "=" in line)


def check_package_folder(artifact, build, commit):
    """The package folder inside the downloaded artifact, after checking it is this build of this commit on main."""
    artifact = Path(artifact)
    tops = sorted(p for p in artifact.iterdir()) if artifact.is_dir() else []
    if [p.name for p in tops] != [f"LivingServer-{build}"] or not tops[0].is_dir():
        raise Stop(f"the artifact should hold exactly one folder LivingServer-{build}, it holds "
                   f"{[p.name for p in tops] or 'nothing'}")
    folder = tops[0]
    info_path = folder / "BUILD-INFO.txt"
    if not info_path.is_file():
        raise Stop(f"{folder.name} has no BUILD-INFO.txt")
    info = read_build_info(info_path)
    expected = {"BuildName": build, "Commit": commit, "Branch": "main"}
    for key, want in expected.items():
        if info.get(key) != want:
            raise Stop(f"BUILD-INFO.txt says {key}={info.get(key)}, expected {want} (take the package from the "
                       "build-and-test push run on main, not a pull-request run)")
    for needed in ("GameServer.dll", "GameServer.pdb", "INSTALL.cmd", "ROLLBACK.cmd", "install-server-build.ps1",
                   "rollback-server-build.ps1", "fork-build-common.ps1"):
        if not (folder / needed).is_file():
            raise Stop(f"{folder.name} has no {needed}")
    for name, want in info.items():
        path = folder / name
        if re.fullmatch(r"[0-9a-f]{64}", want.strip().lower()) and path.is_file():
            if sha256_file(path) != want.strip().lower():
                raise Stop(f"{name} doesn't match its SHA-256 in BUILD-INFO.txt")
    if sha256_file(folder / "GameServer.dll") != (info.get("GameServer.dll") or "").strip().lower():
        raise Stop("GameServer.dll doesn't match its SHA-256 in BUILD-INFO.txt")
    return folder


def write_zip(folder, out_zip):
    """Zips the folder with its own name as the one top folder: folder entries, then each folder's files in name
    order, then its subfolders (the layout of v1.1.1-beta's zip)."""
    folder = Path(folder)
    with zipfile.ZipFile(out_zip, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        def add_dir(d, arc):
            z.write(d, arc + "/", compress_type=zipfile.ZIP_STORED)
            entries = sorted(d.iterdir(), key=lambda p: p.name)
            for p in entries:
                if p.is_file():
                    z.write(p, f"{arc}/{p.name}")
            for p in entries:
                if p.is_dir():
                    add_dir(p, f"{arc}/{p.name}")
        add_dir(folder, folder.name)


def package(artifact, tag, commit, out, prerelease=False):
    build, release = builds_of(tag, prerelease)
    if not SHA1.match(commit or ""):
        raise Stop(f"{commit} is not a full commit SHA")
    folder = check_package_folder(artifact, build, commit)
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    target = out / zip_name(release)
    write_zip(folder, target)
    with zipfile.ZipFile(target) as z:
        bad = z.testzip()
        if bad:
            raise Stop(f"{target.name}: {bad} is damaged")
    return target, sha256_file(target)


def release_body(notes_text, build, zip_sha):
    """The notes below the first '---' line, with the zip row filled in."""
    lines = notes_text.splitlines(keepends=True)
    try:
        start = next(i for i, line in enumerate(lines) if line.strip() == "---")
    except StopIteration:
        raise Stop("the notes file has no '---' line above the release notes")
    body = "".join(lines[start + 1:]).lstrip("\n")
    exe_row = [l for l in body.splitlines() if l.lstrip().startswith("|") and f"`{setup_name(build)}`" in l]
    if len(exe_row) != 1 or SETUP_PLACEHOLDER not in exe_row[0]:
        raise Stop(f"the notes need one Setup row reading `{setup_name(build)}` | {SETUP_PLACEHOLDER}")
    zip_rows = [l for l in body.splitlines() if l.lstrip().startswith("|") and f"`{zip_name(build)}`" in l]
    if len(zip_rows) != 1 or ZIP_PLACEHOLDER not in zip_rows[0]:
        raise Stop(f"the notes need one zip row reading `{zip_name(build)}` | {ZIP_PLACEHOLDER}")
    body = body.replace(zip_rows[0], zip_rows[0].replace(ZIP_PLACEHOLDER, f"`{zip_sha}`"), 1)
    left = LEFTOVER.findall(body)
    if left:
        raise Stop(f"the notes still have placeholders: {left}")
    return body


def prerelease_body(release, zip_sha):
    """Short, player-facing notes for a test build (AGENTS.md, "Release notes are for players")."""
    build, candidate = release.split("-rc.")
    return (f"### Living Server {build}, test build {candidate}\n\n"
            f"A test build of the coming Living Server {build}, for testers. It is a pre-release: the launcher offers it "
            "only with \"Offer test builds (pre-releases)\" turned on, and players on the normal updates keep the current "
            "release.\n\n"
            "Install it from the launcher with **Install into a new test folder**: a fresh install beside your game, with "
            "a new world, so your own world and characters are not touched.\n\n"
            "### Files\n\n| File | SHA-256 |\n|---|---|\n"
            f"| `{zip_name(release)}` | `{zip_sha}` |\n")


class GitHub:
    def __init__(self, token=None):
        self.token = token if token is not None else (os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN"))

    def request(self, method, url, data=None, content_type="application/json", missing_ok=False):
        headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
                   "User-Agent": "living-server-release"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        if data is not None:
            headers["Content-Type"] = content_type
        req = urllib.request.Request(url, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=600) as r:
                raw = r.read()
        except urllib.error.HTTPError as e:
            if missing_ok and e.code == 404:
                return None
            raise Stop(f"{method} {url}: HTTP {e.code} {e.read()[:300]!r}")
        return json.loads(raw) if raw else None

    def tag_exists(self, tag):
        return self.request("GET", f"{API}/repos/{REPO}/git/ref/tags/{urllib.parse.quote(tag)}", missing_ok=True)

    def release_for(self, tag):
        found = self.request("GET", f"{API}/repos/{REPO}/releases/tags/{urllib.parse.quote(tag)}", missing_ok=True)
        if found:
            return found
        # drafts have no tag yet, so releases/tags can't see them
        for rel in self.request("GET", f"{API}/repos/{REPO}/releases?per_page=100") or []:
            if rel.get("tag_name") == tag:
                return rel
        return None

    def create_draft(self, tag, commit, title, body, prerelease=False):
        data = {"tag_name": tag, "target_commitish": commit, "name": title, "body": body,
                "draft": True, "prerelease": prerelease}
        return self.request("POST", f"{API}/repos/{REPO}/releases", json.dumps(data).encode("utf-8"))

    def upload(self, release, path, name):
        url = release["upload_url"].split("{", 1)[0] + "?" + urllib.parse.urlencode({"name": name})
        return self.request("POST", url, Path(path).read_bytes(), "application/zip")

    def get(self, release):
        return self.request("GET", release["url"])

    def publish(self, release, prerelease=False):
        # A pre-release never becomes Latest: /releases/latest (the normal update channel) keeps the final release.
        data = {"draft": False, "prerelease": prerelease, "make_latest": "false" if prerelease else "true"}
        return self.request("PATCH", release["url"], json.dumps(data).encode("utf-8"))


def publish(gh, tag, commit, title, body, zip_path, dry_run, prerelease=False):
    _, release = builds_of(tag, prerelease)
    name = zip_name(release)
    if Path(zip_path).name != name:
        raise Stop(f"the zip is {Path(zip_path).name}, but {tag} needs {name}")
    zip_sha = sha256_file(zip_path)
    if f"`{zip_sha}`" not in body:
        raise Stop(f"the notes don't show the zip's SHA-256 {zip_sha}")
    if gh.tag_exists(tag):
        raise Stop(f"the tag {tag} exists already; a published tag is never moved")
    existing = gh.release_for(tag)
    if existing:
        raise Stop(f"a release for {tag} exists already ({'draft' if existing.get('draft') else 'published'}, "
                   f"{existing.get('html_url')}); delete a stray draft by hand first")
    legacy = legacy_zip_name(release)
    steps = [f"create tag {tag} on {commit} and a draft release \"{title}\"",
             f"attach {name} (sha256 {zip_sha})"] + \
            ([f"attach the same file as {legacy}, for 1.2.0-1.2.2 launchers"] if legacy else []) + [
             "publish it as a pre-release, never Latest (make_latest false)" if prerelease
             else "publish it as Latest, not a pre-release"]
    if dry_run:
        for s in steps:
            print(f"would {s}")
        return None
    if not gh.token:
        raise Stop("no GITHUB_TOKEN: can't publish")
    draft = gh.create_draft(tag, commit, title, body, prerelease)
    print(f"created draft {draft.get('html_url')}")
    gh.upload(draft, zip_path, name)
    if legacy:
        gh.upload(draft, zip_path, legacy)
    draft = gh.get(draft)
    for each in [name] + ([legacy] if legacy else []):
        attached = next((a for a in draft.get("assets") or [] if a.get("name") == each), None)
        digest = (attached or {}).get("digest") or ""
        if digest != github_digest(zip_sha):
            raise Stop(f"GitHub shows {digest or 'no digest'} for {each}, not {github_digest(zip_sha)}; the draft is left "
                       "unpublished for a person to look at")
    published = gh.publish(draft, prerelease)
    if published.get("draft") or bool(published.get("prerelease")) != prerelease:
        raise Stop("the release didn't publish as a " + ("pre-release" if prerelease else "full release"))
    print(f"published {published.get('html_url')} ({'pre-release' if prerelease else 'Latest'}), {name} sha256 {zip_sha}"
          + (f" (also as {legacy})" if legacy else ""))
    return published


def notice(kind, title, text):
    if os.environ.get("GITHUB_ACTIONS"):
        print(f"::{kind} title={title}::{text}")
    else:
        print(f"{kind}: {title}: {text}")


def output(**values):
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with open(path, "a", encoding="utf-8") as f:
            for k, v in values.items():
                f.write(f"{k}={v}\n")


def run(argv=None, gh=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)
    p = sub.add_parser("package")
    p.add_argument("--artifact", required=True)
    p.add_argument("--tag", required=True)
    p.add_argument("--commit", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--prerelease", action="store_true")
    p = sub.add_parser("notes")
    p.add_argument("--notes", help="the release notes file (not used for a pre-release)")
    p.add_argument("--prerelease", action="store_true")
    p.add_argument("--tag", required=True)
    p.add_argument("--zip", required=True)
    p.add_argument("--out", required=True)
    p = sub.add_parser("publish")
    p.add_argument("--tag", required=True)
    p.add_argument("--commit", required=True)
    p.add_argument("--title", required=True)
    p.add_argument("--body", required=True)
    p.add_argument("--zip", required=True)
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--prerelease", action="store_true")
    a = ap.parse_args(argv)
    try:
        if a.command == "package":
            target, digest = package(a.artifact, a.tag, a.commit, a.out, a.prerelease)
            print(f"{target.name} sha256 {digest}")
            output(zip=str(target), zip_sha256=digest)
        elif a.command == "notes":
            if a.prerelease:
                body = prerelease_body(builds_of(a.tag, True)[1], sha256_file(a.zip))
            elif not a.notes:
                raise Stop("notes needs --notes for a final release")
            else:
                body = release_body(Path(a.notes).read_text(encoding="utf-8"), build_of(a.tag), sha256_file(a.zip))
            Path(a.out).write_text(body, encoding="utf-8", newline="\n")
            print(f"notes written to {a.out} ({len(body)} characters)")
        else:
            publish(gh or GitHub(), a.tag, a.commit, a.title, Path(a.body).read_text(encoding="utf-8"), a.zip,
                    a.dry_run, a.prerelease)
        return 0
    except Stop as e:
        notice("error", "Nothing was published", str(e))
        return 1


if __name__ == "__main__":
    sys.exit(run())
