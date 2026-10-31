"""Keeps Living Server's copy of the Offline DAoC release files (owner 2026-10-31: shadowofze put the project on hold).

    python tools/installer/mirror_base_files.py --copy            # copy every missing file, one at a time
    python tools/installer/mirror_base_files.py --check           # list what the copy has, and any file that differs
    python tools/installer/mirror_base_files.py --copy --only v0.37b

The copy lives in chris4aker/OfflineDAoC-base-files, one release per original tag (v0.36 ... v0.37c) with the same
file names, so the downloader works against it with only its release link changed (build.py makes that variant). Each
file is downloaded from shadowofze's release, checked against src/Assets/base-files.sha256, uploaded, and then
deleted here, so the work folder never holds more than one file (the largest is 1.9 GB). An upload is checked by the
SHA-256 GitHub reports for it. Each copied release also gets the original's SHA256SUMS.txt.

Needs: gh (authenticated for chris4aker/OfflineDAoC-base-files) and curl. The copy repository runs this same script
on GitHub Actions (its .github/workflows/copy.yml: on a change, by hand, and a weekly --check), with the pin list at its
root; keep the two copies of the script and the pin list the same.
"""
import argparse
import hashlib
import json
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
PINS = HERE / "src" / "Assets" / "base-files.sha256"
UPSTREAM = "https://github.com/shadowofze/OfflineDAoC/releases/download"
COPY_REPO = "chris4aker/OfflineDAoC-base-files"
API = f"repos/{COPY_REPO.lower()}"


def pins():
    out = {}
    for line in PINS.read_text().splitlines():
        if line and not line.startswith("#"):
            sha, path = line.split()
            tag, name = path.split("/", 1)
            out.setdefault(tag, {})[name] = sha
    return out


def gh(*args, data=None):
    r = subprocess.run(["gh", "api", *args], input=data, capture_output=True)
    if r.returncode:
        raise RuntimeError(f"gh api {' '.join(args[:3])}: {r.stderr.decode(errors='replace').strip()}")
    return json.loads(r.stdout) if r.stdout.strip() else None


def release(tag, create):
    try:
        return gh(f"{API}/releases/tags/{tag}")
    except RuntimeError:
        if not create:
            return None
    body = (f"A byte-identical copy of the files of shadowofze's Offline DAoC release {tag} "
            f"(https://github.com/shadowofze/OfflineDAoC/releases/tag/{tag}), kept so Living Server Setup can still "
            "install the game if the original releases are ever removed. Setup checks every file against the "
            "originals' SHA-256 (SHA256SUMS.txt here). Dark Age of Camelot is the property of Electronic Arts and "
            "Broadsword; this is a free, non-commercial fan project, not affiliated with them.")
    return gh(f"{API}/releases", "-X", "POST", "-f", f"tag_name={tag}", "-f", f"name=Offline DAoC {tag} (copy)",
              "-f", f"body={body}", "-F", "draft=false", "-F", "prerelease=false")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def upload(rel, name, path):
    url = f"https://uploads.github.com/{API}/releases/{rel['id']}/assets?name={name}"
    asset = gh(url, "-X", "POST", "-H", "Content-Type: application/octet-stream", "--input", str(path))
    return asset


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--copy", action="store_true")
    ap.add_argument("--check", action="store_true")
    ap.add_argument("--only", nargs="+")
    ap.add_argument("--work", default=tempfile.gettempdir())
    a = ap.parse_args()
    if not (a.copy or a.check):
        ap.error("say --copy or --check")
    work = Path(a.work)
    work.mkdir(parents=True, exist_ok=True)
    bad = 0
    for tag, files in pins().items():
        if a.only and tag not in a.only:
            continue
        rel = release(tag, a.copy)
        have = {x["name"]: x for x in (rel or {}).get("assets", [])}
        for name, want in list(files.items()) + [("SHA256SUMS.txt", None)]:
            asset = have.get(name)
            digest = (asset or {}).get("digest") or ""
            if asset and (want is None or digest == f"sha256:{want}"):
                print(f"ok      {tag}/{name}")
                continue
            if asset:
                print(f"DIFFERS {tag}/{name}  ({digest or 'no digest'}, want sha256:{want})")
                bad += 1
                if not a.copy:
                    continue
                gh(f"{API}/releases/assets/{asset['id']}", "-X", "DELETE")
            elif not a.copy:
                print(f"missing {tag}/{name}")
                bad += 1
                continue
            path = work / f"{tag}-{name}"
            subprocess.run(["curl", "-sS", "-L", "--fail", "--retry", "5", "--retry-delay", "5", "-C", "-",
                            "-o", str(path), f"{UPSTREAM}/{tag}/{name}"], check=True)
            if want is not None and sha256(path) != want:
                path.unlink()
                sys.exit(f"{tag}/{name} from shadowofze's release doesn't match its pinned SHA-256; stopped.")
            up = upload(rel, name, path)
            path.unlink()
            got = (up or {}).get("digest") or ""
            if want is not None and got != f"sha256:{want}":
                sys.exit(f"{tag}/{name} uploaded but GitHub reports {got or 'no digest'}; stopped.")
            print(f"copied  {tag}/{name}  {up.get('size')} bytes")
    if bad and not a.copy:
        sys.exit(f"{bad} file(s) missing or different")


if __name__ == "__main__":
    main()
