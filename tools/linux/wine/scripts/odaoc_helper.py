#!/usr/bin/env python3
"""Helper for setup-offline-daoc.sh: JSON parsing, hash checks and safe zip extraction.

Commands
  release-assets <release.json>                 TSV: name size sha256-or-"-" url
  repo-size <repo.json>                          repository size in bytes (GitHub's estimate)
  manifest-summary <download-manifest.json>      human summary of a release manifest
  manifest-parts <download-manifest.json> [key]  TSV: release name bytes sha256 (key = Parts|GamePatch|ClientPatch)
  manifest-field <download-manifest.json> <dotted.path>
  dotnet-sdk <releases.json> <rid>               TSV: version url sha512
  extract-split <dest> <rootfolder> <part>...    extract one zip split into raw parts (.zip.001 ...)
  extract-zips <dest> <rootfolder> <zip>...      extract independent zips, stripping rootfolder/
  sha256-concat <file>...                        sha256 of the files joined end to end
"""
import hashlib
import io
import json
import os
import shutil
import sys
import zipfile


def die(msg):
    print("error: " + msg, file=sys.stderr)
    sys.exit(1)


def load(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def cmd_release_assets(path):
    rel = load(path)
    if "assets" not in rel:
        die("not a GitHub release JSON: " + rel.get("message", "?"))
    for a in rel["assets"]:
        digest = a.get("digest") or ""
        sha = digest[7:] if digest.startswith("sha256:") else "-"
        print("\t".join([a["name"], str(a["size"]), sha, a["browser_download_url"]]))


def cmd_repo_size(path):
    print(int(load(path).get("size", 0)) * 1024)


def cmd_manifest_summary(path):
    m = load(path)
    print("  %s  (mode: %s)" % (m.get("Title", m.get("Version")), m.get("Mode", "full")))
    for key in ("Parts",):
        if m.get(key):
            print("    %d parts, %.2f GB" % (len(m[key]), sum(p["Bytes"] for p in m[key]) / 1e9))
    for key in ("GamePatch", "ClientPatch"):
        if m.get(key):
            p = m[key]
            print("    %s: from release v%s, %.2f GB" % (key, p["Release"], sum(x["Bytes"] for x in p["Parts"]) / 1e9))
    for key in ("BaseVersion", "NewInstallBase"):
        if m.get(key):
            print("    %s: %s" % (key, m[key]))


def cmd_manifest_parts(path, key="Parts"):
    m = load(path)
    if key == "Parts":
        parts, release = m.get("Parts") or [], m["Version"]
    else:
        parts, release = m[key]["Parts"], m[key]["Release"]
    for p in parts:
        print("\t".join([release, p["Name"], str(p["Bytes"]), p["SHA256"].lower()]))


def cmd_manifest_field(path, dotted):
    v = load(path)
    for k in dotted.split("."):
        v = v[k]
    print(v if not isinstance(v, (list, dict)) else json.dumps(v))


def cmd_dotnet_sdk(path, rid):
    d = load(path)
    want = d.get("latest-sdk")
    for r in d["releases"]:
        sdks = [r.get("sdk")] + (r.get("sdks") or [])
        for s in sdks:
            if not s or s.get("version") != want:
                continue
            for f in s.get("files", []):
                if f.get("rid") == rid and f["name"].endswith(".tar.gz"):
                    print("\t".join([s["version"], f["url"], f["hash"].lower()]))
                    return
    die("no .NET SDK %s download for %s in %s" % (want, rid, path))


class SplitReader(io.RawIOBase):
    """Read several files as one seekable stream, so a split zip needs no joined copy on disk."""

    def __init__(self, paths):
        self.files = [open(p, "rb") for p in paths]
        self.sizes = [os.path.getsize(p) for p in paths]
        self.starts = []
        total = 0
        for s in self.sizes:
            self.starts.append(total)
            total += s
        self.total = total
        self.pos = 0

    def readable(self):
        return True

    def seekable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, offset, whence=0):
        if whence == 0:
            self.pos = offset
        elif whence == 1:
            self.pos += offset
        else:
            self.pos = self.total + offset
        return self.pos

    def readinto(self, buf):
        want = len(buf)
        done = 0
        while done < want and self.pos < self.total:
            i = max(j for j, st in enumerate(self.starts) if st <= self.pos)
            f = self.files[i]
            f.seek(self.pos - self.starts[i])
            n = min(want - done, self.sizes[i] - (self.pos - self.starts[i]))
            chunk = f.read(n)
            if not chunk:
                break
            buf[done:done + len(chunk)] = chunk
            done += len(chunk)
            self.pos += len(chunk)
        return done

    def close(self):
        for f in self.files:
            f.close()
        super().close()


def safe_extract(zf, dest, root):
    """Extract every entry under root/ into dest, refusing anything that escapes dest."""
    dest = os.path.realpath(dest)
    prefix = root.rstrip("/") + "/"
    entries = []
    for info in zf.infolist():
        name = info.filename.replace("\\", "/")
        if not name.startswith(prefix):
            die("unexpected archive layout: %s (expected everything under %s)" % (name, prefix))
        rel = name[len(prefix):]
        if not rel:
            continue
        if ":" in rel or rel.startswith("/") or ".." in rel.split("/"):
            die("unsafe archive entry: " + name)
        target = os.path.realpath(os.path.join(dest, rel))
        if not (target == dest or target.startswith(dest + os.sep)):
            die("unsafe archive entry: " + name)
        entries.append((info, rel, target))
    count = 0
    for info, rel, target in entries:
        if rel.endswith("/"):
            os.makedirs(target, exist_ok=True)
            continue
        os.makedirs(os.path.dirname(target), exist_ok=True)
        with zf.open(info) as src, open(target, "wb") as out:
            shutil.copyfileobj(src, out, 1024 * 1024)
        count += 1
        if count % 2000 == 0:
            print("    %d files..." % count, flush=True)
    print("    %d files extracted" % count)


def cmd_extract_split(dest, root, *parts):
    reader = io.BufferedReader(SplitReader(list(parts)), buffer_size=1024 * 1024)
    with zipfile.ZipFile(reader) as zf:
        safe_extract(zf, dest, root)


def cmd_extract_zips(dest, root, *zips):
    for z in zips:
        print("  unpacking " + os.path.basename(z), flush=True)
        with zipfile.ZipFile(z) as zf:
            safe_extract(zf, dest, root)


def cmd_sha256_concat(*paths):
    h = hashlib.sha256()
    for p in paths:
        with open(p, "rb") as f:
            for chunk in iter(lambda: f.read(4 * 1024 * 1024), b""):
                h.update(chunk)
    print(h.hexdigest())


COMMANDS = {
    "release-assets": cmd_release_assets,
    "repo-size": cmd_repo_size,
    "manifest-summary": cmd_manifest_summary,
    "manifest-parts": cmd_manifest_parts,
    "manifest-field": cmd_manifest_field,
    "dotnet-sdk": cmd_dotnet_sdk,
    "extract-split": cmd_extract_split,
    "extract-zips": cmd_extract_zips,
    "sha256-concat": cmd_sha256_concat,
}

if __name__ == "__main__":
    if len(sys.argv) < 2 or sys.argv[1] not in COMMANDS:
        print(__doc__)
        sys.exit(2)
    COMMANDS[sys.argv[1]](*sys.argv[2:])
