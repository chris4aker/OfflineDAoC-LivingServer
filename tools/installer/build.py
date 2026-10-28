"""Builds the Living Server Setup exe: one file that installs Offline DAoC and Living Server.

    python tools/installer/build.py                                  # embeds the published Beta 1.0 package
    python tools/installer/build.py --package path/to/OfflineDAoC-LivingServer-<build>.zip
    python tools/installer/build.py --release v1.0-beta              # downloads that release's package

Writes tools/installer/dist/LivingServer-Setup-<build>.exe (needs the .NET SDK; builds on Windows or Linux).

The exe carries two payloads:
- the Living Server release package (its BUILD-INFO.txt names the build and the Offline DAoC release it fits);
- shadowofze's official downloader, Get-OfflineDAoC.ps1, taken from this repository's root (the same file as upstream's
  v0.37 / v0.37b / v0.37c releases) and pinned below by SHA-256, so a changed downloader can't slip in unnoticed.
"""
import argparse
import hashlib
import shutil
import subprocess
import sys
import urllib.request
import zipfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
PAYLOAD = HERE / "payload"
DIST = HERE / "dist"
REPO = "chris4aker/OfflineDAoC-LivingServer"

# shadowofze/OfflineDAoC Get-OfflineDAoC.ps1 at tags v0.37, v0.37b and v0.37c (identical); checked 2026-10-27.
DOWNLOADER_SHA256 = "8f000a12d11fa3d69d89a6bae0149adb2b345990b9f7eb33fbef1bf4592557b4"
# Packages known by hash. Any other package is accepted when it has a complete BUILD-INFO.txt (CI builds new ones).
KNOWN_PACKAGES = {
    "OfflineDAoC-LivingServer-1.0-beta.zip": "ce31cf1721b3405ff0f8b971c5b58d237aaebfd1bbc737bb3a79acd8c3dd1d4b",
}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def fetch_release(tag):
    name = f"OfflineDAoC-LivingServer-{tag.lstrip('v')}.zip"
    url = f"https://github.com/{REPO}/releases/download/{tag}/{name}"
    dest = PAYLOAD / "download" / name
    dest.parent.mkdir(parents=True, exist_ok=True)
    if not dest.exists():
        print(f"Downloading {url}")
        part = dest.with_suffix(".part")
        with urllib.request.urlopen(url) as r, open(part, "wb") as f:
            shutil.copyfileobj(r, f)
        part.replace(dest)   # only a complete download gets the real name
    return dest


def check_package(path):
    with zipfile.ZipFile(path) as z:
        infos = [n for n in z.namelist() if n.count("/") == 1 and n.endswith("/BUILD-INFO.txt")]
        if len(infos) != 1:
            sys.exit(f"{path} is not a Living Server package (no BUILD-INFO.txt at its top folder).")
        root = infos[0].split("/")[0]
        info = dict(l.split("=", 1) for l in z.read(infos[0]).decode("utf-8-sig").splitlines() if "=" in l)
        for key in ("BuildName", "BaseRelease", "GameServer.dll", "GameServer.pdb"):
            if not info.get(key):
                sys.exit(f"{path}: BUILD-INFO.txt has no {key}.")
        for needed in ("install-server-build.ps1", "rollback-server-build.ps1", "fork-build-common.ps1", "GameServer.dll"):
            if f"{root}/{needed}" not in z.namelist():
                sys.exit(f"{path} has no {needed}.")
        dll = hashlib.sha256(z.read(f"{root}/GameServer.dll")).hexdigest()
        if dll != info["GameServer.dll"].lower():
            sys.exit(f"{path}: GameServer.dll doesn't match its BUILD-INFO.txt.")
    known = KNOWN_PACKAGES.get(Path(path).name)
    if known and sha256(path) != known:
        sys.exit(f"{path} doesn't match the published {Path(path).name} (sha256 {known}).")
    return info


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--package", help="a Living Server package zip")
    ap.add_argument("--release", default="v1.0-beta", help="a release tag to download the package from (default v1.0-beta)")
    ap.add_argument("--no-build", action="store_true", help="only stage the payloads")
    a = ap.parse_args()

    PAYLOAD.mkdir(parents=True, exist_ok=True)
    package = Path(a.package) if a.package else fetch_release(a.release)
    info = check_package(package)
    shutil.copyfile(package, PAYLOAD / "LivingServer.zip")
    print(f"Package: {package.name}  build {info['BuildName']} for Offline DAoC {info['BaseRelease']}  sha256 {sha256(package)}")

    # Compared and embedded with LF line ends, as upstream publishes it: a Windows checkout turns them into CRLF.
    data = (ROOT / "Get-OfflineDAoC.ps1").read_bytes().replace(b"\r\n", b"\n")
    got = hashlib.sha256(data).hexdigest()
    if got != DOWNLOADER_SHA256:
        sys.exit(f"Get-OfflineDAoC.ps1 is not the pinned upstream downloader (sha256 {got}). "
                 "If shadowofze released a new one, check it and update DOWNLOADER_SHA256.")
    (PAYLOAD / "Get-OfflineDAoC.ps1").write_bytes(data)
    print(f"Downloader: Get-OfflineDAoC.ps1  sha256 {got} (pinned)")
    if a.no_build:
        return

    proj = HERE / "src" / "LivingServerSetup.csproj"
    out = HERE / "src" / "bin" / "Release" / "net472"
    subprocess.run(["dotnet", "build", str(proj), "-c", "Release", "-nologo", "-v", "minimal",
                    f"-p:PayloadDir={PAYLOAD}{'/' if not str(PAYLOAD).endswith('/') else ''}"], check=True)
    DIST.mkdir(exist_ok=True)
    exe = DIST / f"LivingServer-Setup-{info['BuildName']}.exe"
    shutil.copyfile(out / "LivingServerSetup.exe", exe)
    print(f"Built {exe.relative_to(ROOT)}  {exe.stat().st_size / 1048576:.1f} MB  sha256 {sha256(exe)}")


if __name__ == "__main__":
    main()
