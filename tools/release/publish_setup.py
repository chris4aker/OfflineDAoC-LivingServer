"""Attaches the Living Server Setup exe to a published release and puts its SHA-256 in the release notes.

Used by .github/workflows/release.yml (docs/DEVELOPMENT.md, "Releasing Living Server"):

    python tools/release/publish_setup.py plan --tag v1.1.2-beta
        Checks the release and its package, and says whether anything is left to do (GitHub step outputs).
    python tools/release/publish_setup.py publish --tag v1.1.2-beta --exe <Setup.exe> --package <LivingServer.zip> [--dry-run]
        Uploads the exe unless it is attached already, then fills the Setup row of the notes' file table:
            | `LivingServer-Setup-1.1.2-beta.exe` | (filled in when the exe is built) |
        becomes
            | `LivingServer-Setup-1.1.2-beta.exe` | `<sha256>` |

Safe to run again:
- an exe already attached with the same SHA-256 is not uploaded again, and notes that already show it aren't edited;
- an exe attached with a different SHA-256 is never replaced: the run stops and says so (delete the asset by hand
  to have it rebuilt);
- only the placeholder in the Setup row is changed, and the rest of the notes, line ends included, stay as they are.

--dry-run does every check and prints what it would upload and the new notes, without changing the release.
--release-json FILE reads the release from a file instead of GitHub (tests and local checks).
Needs GITHUB_TOKEN (or GH_TOKEN) to change a release; reading a public release works without one.
"""
import argparse
import hashlib
import json
import os
import re
import sys
import urllib.parse
import urllib.request
from pathlib import Path

REPO = os.environ.get("GITHUB_REPOSITORY", "chris4aker/OfflineDAoC-LivingServer")
API = os.environ.get("GITHUB_API_URL", "https://api.github.com")
PLACEHOLDER = "(filled in when the exe is built)"
TAG = re.compile(r"^v(\d+(?:\.\d+)+-beta)$")
SHA256 = re.compile(r"\b[0-9a-f]{64}\b")


class Stop(Exception):
    """A problem a person has to look at; the release is left as it is."""


def build_of(tag):
    m = TAG.match(tag)
    if not m:
        raise Stop(f"{tag} is not a Living Server release tag (v<version>-beta, e.g. v1.1.2-beta)")
    return m.group(1)


def names(tag):
    build = build_of(tag)
    return build, f"LivingServer-Setup-{build}.exe", f"OfflineDAoC-LivingServer-{build}.zip"


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def asset(release, name):
    return next((a for a in release.get("assets") or [] if a.get("name") == name), None)


def asset_sha(a):
    digest = (a or {}).get("digest") or ""
    return digest[len("sha256:"):].lower() if digest.startswith("sha256:") else None


def setup_row(body, exe_name):
    """(index, line) of the notes' line that names the Setup exe in a table row, or (None, None)."""
    lines = body.splitlines(keepends=True)
    for i, line in enumerate(lines):
        if line.lstrip().startswith("|") and f"`{exe_name}`" in line:
            return i, line
    return None, None


def fill_notes(body, exe_name, sha):
    """(new body or None when nothing changes, message). Raises Stop when the notes disagree with the exe."""
    body = body or ""
    i, line = setup_row(body, exe_name)
    if line is None:
        return None, f"the notes have no file-table row for `{exe_name}`; left as they are"
    shown = SHA256.findall(line.lower())
    if PLACEHOLDER in line:
        lines = body.splitlines(keepends=True)
        lines[i] = line.replace(PLACEHOLDER, f"`{sha}`", 1)
        return "".join(lines), f"the Setup row's placeholder becomes `{sha}`"
    if sha in shown:
        return None, "the notes already show this SHA-256"
    if shown:
        raise Stop(f"the notes' Setup row shows {shown[0]}, but the attached exe is {sha}")
    return None, f"the notes' Setup row has neither the placeholder nor a SHA-256; left as it is: {line.strip()}"


def decide(release, tag, exe_sha=None, package_sha=None):
    """What publishing needs to do: {'upload': bool, 'body': new body or None, 'messages': [...], 'done': bool}."""
    build, exe_name, zip_name = names(tag)
    if release.get("tag_name") != tag:
        raise Stop(f"the release is {release.get('tag_name')}, not {tag}")
    if release.get("draft"):
        raise Stop(f"{tag} is still a draft: publish it first")
    package = asset(release, zip_name)
    if package is None:
        raise Stop(f"{tag} has no {zip_name}: attach the package before Setup can be built from it")
    if package_sha is not None and asset_sha(package) and asset_sha(package) != package_sha:
        raise Stop(f"the package Setup embeds ({package_sha}) is not the release's {zip_name} ({asset_sha(package)})")

    messages = []
    attached = asset(release, exe_name)
    attached_sha = asset_sha(attached)
    if attached is not None and attached_sha is None:
        raise Stop(f"{exe_name} is attached but GitHub gives no SHA-256 digest for it; check it by hand")
    if exe_sha is None:
        # plan: nothing to build when the exe is attached and the notes already show its hash
        if attached is None:
            return {"upload": True, "body": None, "done": False, "messages": [f"{exe_name} is not attached yet"]}
        body, message = fill_notes(release.get("body"), exe_name, attached_sha)
        done = body is None and attached_sha in (release.get("body") or "")
        return {"upload": False, "body": None, "done": done,
                "messages": [f"{exe_name} is attached (sha256 {attached_sha}); {message}"]}

    if attached is None:
        upload = True
        messages.append(f"upload {exe_name} (sha256 {exe_sha})")
    elif attached_sha == exe_sha:
        upload = False
        messages.append(f"{exe_name} is attached already with the same SHA-256; not uploaded again")
    else:
        raise Stop(f"{exe_name} is attached already with sha256 {attached_sha}, but this build is {exe_sha}. "
                   "A published file is never replaced: delete the asset by hand to attach this build.")
    body, message = fill_notes(release.get("body"), exe_name, exe_sha)
    messages.append(message)
    return {"upload": upload, "body": body, "done": not upload and body is None, "messages": messages}


class GitHub:
    def __init__(self, token=None):
        self.token = token if token is not None else (os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN"))

    def request(self, method, url, data=None, content_type="application/json"):
        headers = {"Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28",
                   "User-Agent": "living-server-release"}
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        if data is not None:
            headers["Content-Type"] = content_type
        req = urllib.request.Request(url, data=data, method=method, headers=headers)
        with urllib.request.urlopen(req, timeout=600) as r:
            raw = r.read()
        return json.loads(raw) if raw else None

    def release(self, tag):
        return self.request("GET", f"{API}/repos/{REPO}/releases/tags/{urllib.parse.quote(tag)}")

    def upload(self, release, path, name):
        url = release["upload_url"].split("{", 1)[0] + "?" + urllib.parse.urlencode({"name": name})
        return self.request("POST", url, Path(path).read_bytes(), "application/octet-stream")

    def edit_body(self, release, body):
        return self.request("PATCH", release["url"], json.dumps({"body": body}).encode("utf-8"))


def output(**values):
    path = os.environ.get("GITHUB_OUTPUT")
    if path:
        with open(path, "a", encoding="utf-8") as f:
            for k, v in values.items():
                f.write(f"{k}={v}\n")


def notice(kind, title, text):
    if os.environ.get("GITHUB_ACTIONS"):
        print(f"::{kind} title={title}::{text}")
    else:
        print(f"{kind}: {title}: {text}")


def load(a, gh):
    if a.release_json:
        return json.loads(Path(a.release_json).read_text(encoding="utf-8"))
    return gh.release(a.tag)


def run(argv=None, gh=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)
    for name in ("plan", "publish"):
        p = sub.add_parser(name)
        p.add_argument("--tag", required=True)
        p.add_argument("--release-json", help="read the release from this file instead of GitHub")
        if name == "publish":
            p.add_argument("--exe", required=True, help="the built Setup exe")
            p.add_argument("--package", required=True, help="the package zip Setup embeds (payload/LivingServer.zip)")
            p.add_argument("--dry-run", action="store_true", help="check and show, change nothing")
    a = ap.parse_args(argv)
    gh = gh or GitHub()
    try:
        build, exe_name, zip_name = names(a.tag)
        release = load(a, gh)
        if a.command == "plan":
            result = decide(release, a.tag)
            for m in result["messages"]:
                print(m)
            output(build=build, setup_name=exe_name, package_name=zip_name, done=str(result["done"]).lower())
            print("Nothing left to do: the exe is attached and the notes show its SHA-256." if result["done"]
                  else f"To do: build and test {exe_name} from {zip_name}, then attach it and fill in the notes.")
            return 0

        if Path(a.exe).name != exe_name:
            raise Stop(f"the built exe is {Path(a.exe).name}, but release {a.tag} needs {exe_name}")
        exe_sha, package_sha = sha256_file(a.exe), sha256_file(a.package)
        result = decide(release, a.tag, exe_sha, package_sha)
        print(f"Setup {exe_name}  sha256 {exe_sha}\nPackage {zip_name}  sha256 {package_sha} (matches the release)")
        for m in result["messages"]:
            print(("would " if a.dry_run and m.startswith("upload") else "") + m)
        if a.dry_run:
            if result["body"] is not None:
                print("--- the notes would become ---")
                print(result["body"])
                print("--- end of notes ---")
            notice("notice", "Dry run", f"{a.tag}: " + "; ".join(result["messages"]) + ". Nothing was changed.")
            return 0
        if not gh.token:
            raise Stop("no GITHUB_TOKEN: can't change the release")
        if result["upload"]:
            gh.upload(release, a.exe, exe_name)
            release = gh.release(a.tag)
            got = asset_sha(asset(release, exe_name))
            if got and got != exe_sha:
                raise Stop(f"GitHub reports sha256 {got} for the uploaded {exe_name}, not {exe_sha}")
            notice("notice", "Setup attached", f"{exe_name} sha256 {exe_sha}")
            # the notes may have changed while the exe was built and tested: fill the newest ones
            result = decide(release, a.tag, exe_sha, package_sha)
        if result["body"] is not None:
            gh.edit_body(release, result["body"])
            notice("notice", "Release notes", f"the Setup row now shows {exe_sha}")
        if result["done"]:
            notice("notice", "Nothing to do", f"{a.tag} already has {exe_name} ({exe_sha}) and its SHA-256 in the notes")
        return 0
    except Stop as e:
        if getattr(a, "dry_run", False):
            # A dry run shows the whole plan: where a real run would stop is its answer, not a failure.
            notice("warning", "Dry run", f"a real run would stop here and leave the release unchanged: {e}")
            return 0
        notice("error", "Release left unchanged", str(e))
        return 1


if __name__ == "__main__":
    sys.exit(run())
