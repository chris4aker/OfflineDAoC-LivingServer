"""The frozen update contract's CI check (issue #685, docs/RELEASE-TRAIN.md): would a released launcher take this commit?

Used by .github/workflows/update-contract.yml with tools/release/test-update-contract.ps1 and the
source/tools/DAoCLivingServer.UpdateContract harness.

    python tools/release/update_contract.py candidate --package-dir DIR --out DIR [--repository OWNER/REPO] [--tag vX]
        Zips this commit's package (build-and-test's artifact) exactly as publish_release.py does, under every name
        publish_release.py attaches it (zip_name and legacy_zip_name), and writes the release GitHub would then answer:
        latest.json (GET /releases/latest), releases.json (GET /releases) with the real asset names, the
        https://github.com/<repository>/releases/download/<tag>/<name> URLs and GitHub's "sha256:<hex>" digests
        (publish_release.asset_url, github_digest), plus feed.json for the harness. The repository is the one this
        commit's launcher names (ReleaseFeed.Repository), unless --repository is given.
    python tools/release/update_contract.py released --repository OWNER/REPO --below BUILD --out DIR
        The newest full release older than BUILD (today v1.2.2): its package and Setup downloaded from their release
        files and checked against GitHub's digests. Writes released.json.
    python tools/release/update_contract.py repository --live OWNER/REPO
        Fails unless the repository GitHub runs this in is exactly (case included) the one the launcher names: the
        download prefix check of every released launcher is an ordinal match (#645).

Standard library only. GITHUB_TOKEN or GH_TOKEN, when set, is sent to the API (rate limits).
"""
import argparse
import json
import os
import re
import shutil
import sys
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import publish_release as pr  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
# How GitHub's releases API gives an asset's digest (2025 on). The contract's item 5.
GITHUB_DIGEST = "sha256:{}"
RELEASE_FEED = REPO_ROOT / "source/tools/DAoCLivingServer.Launcher/Updates/ReleaseFeed.cs"


class Stop(Exception):
    pass


def repository_from_source(path=RELEASE_FEED):
    """ReleaseFeed.Repository as this commit's launcher compiles it."""
    m = re.search(r'public const string Repository = "([^"]+)";', Path(path).read_text(encoding="utf-8"))
    if not m:
        raise Stop(f"{path} declares no ReleaseFeed.Repository")
    return m.group(1)


def version_key(build):
    """Orders builds as the launcher's BuildVersion does for full releases: numbers, then a beta before its final."""
    m = re.match(r"^v?(\d+(?:\.\d+)*)(-beta)?$", build or "")
    if not m:
        return None
    nums = [int(n) for n in m.group(1).split(".")]
    nums += [0] * (4 - len(nums))
    return tuple(nums) + ((0,) if m.group(2) else (2,))


def package_folder(package_dir):
    tops = [p for p in Path(package_dir).iterdir() if p.is_dir() and p.name.startswith("LivingServer-")]
    if len(tops) != 1:
        raise Stop(f"{package_dir} should hold one LivingServer-<build> folder, it holds {[p.name for p in tops]}")
    return tops[0]


def candidate(package_dir, out, repository=None, tag=None):
    folder = package_folder(package_dir)
    info = pr.read_build_info(folder / "BUILD-INFO.txt")
    build = info.get("BuildName", "").strip()
    if folder.name != f"LivingServer-{build}":
        raise Stop(f"{folder.name} holds BUILD-INFO.txt BuildName={build}")
    tag = tag or f"v{build}"
    package_build, release = pr.builds_of(tag, "-rc." in tag)
    if package_build != build:
        raise Stop(f"{tag} carries build {package_build}, the package is {build}")
    repository = repository or repository_from_source()
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    names = [pr.zip_name(release)] + ([pr.legacy_zip_name(release)] if pr.legacy_zip_name(release) else [])
    first = out / names[0]
    pr.write_zip(folder, first)
    sha = pr.sha256_file(first)
    assets, files = [], {}
    for name in names:
        path = out / name
        if path != first:
            shutil.copyfile(first, path)
        url = pr.asset_url(repository, tag, name)
        assets.append({"name": name, "size": path.stat().st_size, "digest": pr.github_digest(sha),
                       "browser_download_url": url, "content_type": "application/zip", "state": "uploaded"})
        files[url] = name
    prerelease = "-rc." in tag
    release_json = {
        "tag_name": tag, "name": f"DAoC - Living Server {release}", "draft": False, "prerelease": prerelease,
        "html_url": f"https://github.com/{repository}/releases/tag/{tag}",
        "body": f"Candidate {tag} (update-contract check, commit {info.get('Commit', '?')}).", "assets": assets,
    }
    (out / "latest.json").write_text(json.dumps(release_json, indent=2), encoding="utf-8")
    (out / "releases.json").write_text(json.dumps([release_json], indent=2), encoding="utf-8")
    feed = {"repository": repository, "tag": tag, "build": build, "latest": "latest.json", "releases": "releases.json",
            "files": files, "sha256": sha, "assets": names, "base_release": info.get("BaseRelease", "")}
    (out / "feed.json").write_text(json.dumps(feed, indent=2), encoding="utf-8")
    print(f"candidate {tag} (build {build}) in {repository}: {', '.join(names)} sha256 {sha}")
    return feed


def _get(url, accept="application/vnd.github+json"):
    req = urllib.request.Request(url, headers={"User-Agent": "DAoC-LivingServer-update-contract", "Accept": accept})
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if token and url.startswith(pr.API):
        req.add_header("Authorization", f"Bearer {token}")
    return urllib.request.urlopen(req, timeout=120)


def pick_released(releases, below):
    """The newest published full release whose build is older than below."""
    limit = version_key(below)
    if limit is None:
        raise Stop(f"{below} is not a full-release build")
    best = None
    for r in releases:
        key = version_key(r.get("tag_name"))
        if r.get("draft") or r.get("prerelease") or key is None or key[:4] < (1, 0, 0, 0) or key >= limit:
            continue
        if best is None or key > version_key(best["tag_name"]):
            best = r
    if best is None:
        raise Stop(f"no full release older than {below}")
    return best


def released(repository, below, out):
    with _get(f"{pr.API}/repos/{repository}/releases?per_page=100") as resp:
        releases = json.load(resp)
    rel = pick_released(releases, below)
    tag = rel["tag_name"]
    build = tag[1:]
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    got = {"tag": tag, "build": build}
    for kind, names in (("package", [pr.zip_name(build), f"OfflineDAoC-LivingServer-{build}.zip"]),
                        ("setup", [pr.setup_name(build), f"LivingServer-Setup-{build}.exe"])):
        asset = next((a for n in names for a in rel.get("assets") or [] if a.get("name") == n), None)
        if asset is None:
            raise Stop(f"{tag} has no {' or '.join(names)}")
        path = out / asset["name"]
        with _get(asset["browser_download_url"], "application/octet-stream") as resp, open(path, "wb") as f:
            shutil.copyfileobj(resp, f, 1 << 20)
        sha = pr.sha256_file(path)
        # GitHub's own form, not publish_release.github_digest: this reads what GitHub really publishes.
        if asset.get("digest") != GITHUB_DIGEST.format(sha):
            raise Stop(f"{asset['name']}: GitHub's digest {asset.get('digest')} isn't sha256:{sha}")
        got[kind] = str(path)
        got[kind + "_sha256"] = sha
        print(f"{tag} {asset['name']} sha256 {sha} (matches GitHub's digest)")
    (out / "released.json").write_text(json.dumps(got, indent=2), encoding="utf-8")
    return got


def check_repository(live):
    declared = repository_from_source()
    if live != declared:
        raise Stop(f"the repository is {live}, but ReleaseFeed.Repository (and every released launcher's download "
                   f"prefix, an ordinal match) is {declared}: released launchers refuse its release files (#645). "
                   "Change it only by the two-step rule (docs/RELEASE-TRAIN.md).")
    print(f"repository {live} is the one the launcher names")


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    sub = p.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("candidate")
    c.add_argument("--package-dir", required=True)
    c.add_argument("--out", required=True)
    c.add_argument("--repository")
    c.add_argument("--tag")
    r = sub.add_parser("released")
    r.add_argument("--repository", required=True)
    r.add_argument("--below", required=True)
    r.add_argument("--out", required=True)
    g = sub.add_parser("repository")
    g.add_argument("--live", required=True)
    a = p.parse_args(argv)
    try:
        if a.cmd == "candidate":
            candidate(a.package_dir, a.out, a.repository, a.tag)
        elif a.cmd == "released":
            released(a.repository, a.below, a.out)
        else:
            check_repository(a.live)
    except (Stop, pr.Stop) as e:
        print(f"::error title=Update contract::{e}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
