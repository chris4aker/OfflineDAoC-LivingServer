#!/usr/bin/env python3
"""Install a Living Server add-on build into an Offline DAoC game folder on Linux.

A faithful port of the add-on's own install-server-build.ps1 (INSTALL.cmd needs Windows PowerShell):
same checks, same backup layout and install-record.json, so the add-on's ROLLBACK.cmd still understands it.

  python3 -I install_living_server.py <unpacked build folder> <game folder> [--dry-run] [--force]
"""
import base64
import datetime
import hashlib
import json
import os
import re
import shutil
import sys

SERVER_FOLDERS = ["runtime/server", "runtime/server/lib", "runtime/server/win-x64"]
LAUNCHER_FOLDERS = ["runtime", "new-client/runtime"]
LAUNCHER_FILES = ["OfflineDAoC.dll", "OfflineDAoC.exe", "OfflineDAoC.pdb", "OfflineDAoC.deps.json",
                  "OfflineDAoC.runtimeconfig.json"]
PROCESS_NAMES = {"CoreServer", "DOLServer", "OfflineDAoC", "game", "camelot", "connect",
                 "Godot_v4.7.2-stable_mono_win64"}


def stop(msg):
    print("\nNOTHING WAS CHANGED: " + msg)
    sys.exit(1)


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for c in iter(lambda: f.read(1 << 22), b""):
            h.update(c)
    return h.hexdigest()


def p(root, rel):
    return os.path.join(root, *rel.split("/"))


def read_kv(path):
    out = {}
    for line in open(path, encoding="utf-8-sig"):
        m = re.match(r"^\s*([^=#]+?)\s*=\s*(.*?)\s*$", line)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def read_manifest(path):
    if not os.path.isfile(path):
        return None
    out = {}
    for line in open(path, encoding="utf-8-sig"):
        m = re.match(r"^([0-9a-fA-F]{64})\s+(.+)$", line.rstrip("\r\n"))
        if m:
            out[m.group(2).strip()] = m.group(1).lower()
    return out


def release_ok(manifest, base):
    bases = [b.strip() for b in base.split(",") if b.strip()]
    vers = [tuple(map(int, m.group(1).split("."))) for k in manifest
            for m in [re.match(r"^docs/RELEASE-(\d+\.\d+)\.md$", k)] if m]
    if vers:
        newest = max(vers)
        return any(tuple(map(int, b.split("."))) == newest for b in bases)
    return any(k.startswith("editions/%s-no-custom-class/" % b) for b in bases for k in manifest)


def running():
    found = set()
    for pid in filter(str.isdigit, os.listdir("/proc")):
        try:
            names = [open("/proc/%s/comm" % pid).read().strip()]
            cmd = open("/proc/%s/cmdline" % pid, "rb").read().split(b"\0")
            names += [os.path.basename(c.decode(errors="ignore").replace("\\", "/")) for c in cmd[:2] if c]
        except OSError:
            continue
        for n in names:
            stem = n[:-4] if n.lower().endswith(".exe") else n
            if stem in PROCESS_NAMES:
                found.add(stem)
    return found


def install_records(game):
    root = p(game, "runtime/server/deployment-backups")
    if not os.path.isdir(root):
        return []
    out = []
    for d in sorted(os.listdir(root)):
        f = os.path.join(root, d, "install-record.json")
        if d.startswith("fork-build-") and os.path.isfile(f):
            out.append(json.load(open(f, encoding="utf-8-sig")))
    return out


def as_list(v):
    return v if isinstance(v, list) else ([] if v is None else [v])


def expand_patch(patch, released, build):
    if patch.get("File"):
        rel = patch["File"]
        if os.path.isabs(rel) or ".." in re.split(r"[\\/]", rel):
            raise ValueError("bad content file path: " + rel)
        return open(p(build, rel.replace("\\", "/")), "rb").read()
    out = bytearray()
    for op in as_list(patch["Ops"]):
        if op.get("copy") is not None:
            a, n = op["copy"]
            out += released[a:a + n]
        else:
            out += base64.b64decode(op["data"])
    return bytes(out)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    dry, force = "--dry-run" in sys.argv, "--force" in sys.argv
    if len(args) != 2:
        print(__doc__)
        sys.exit(2)
    build, game = (os.path.realpath(a) for a in args)

    info_path = os.path.join(build, "BUILD-INFO.txt")
    if not os.path.isfile(info_path):
        stop("BUILD-INFO.txt is missing from this build folder.")
    info = read_kv(info_path)
    for k in ("BuildName", "BaseRelease", "Commit", "GameServer.dll", "GameServer.pdb"):
        if not info.get(k):
            stop("BUILD-INFO.txt has no %s." % k)
    print("Fork server build %s (commit %s) for Offline DAoC %s" % (info["BuildName"], info["Commit"], info["BaseRelease"]))

    # 1. this build's own files
    for name in ("GameServer.dll", "GameServer.pdb"):
        f = os.path.join(build, name)
        if not os.path.isfile(f):
            stop(name + " is missing from this build folder.")
        if sha(f) != info[name].lower():
            stop(name + " doesn't match BUILD-INFO.txt (damaged download?).")
    patches = []
    cp = os.path.join(build, "content-patches.json")
    if os.path.isfile(cp):
        if not info.get("content-patches.json") or sha(cp) != info["content-patches.json"].lower():
            stop("content-patches.json doesn't match BUILD-INFO.txt (damaged download?).")
        patches = as_list(json.load(open(cp, encoding="utf-8-sig")))
    lbuild = os.path.join(build, "launcher")
    lnew = {}
    if os.path.isdir(lbuild):
        for name in LAUNCHER_FILES:
            f, want = os.path.join(lbuild, name), info.get("launcher/" + name)
            if not want or not os.path.isfile(f):
                stop("launcher/%s is missing from this build folder or BUILD-INFO.txt." % name)
            if sha(f) != want.lower():
                stop("launcher/%s doesn't match BUILD-INFO.txt (damaged download?)." % name)
            lnew[name] = want.lower()

    # 2. the game, closed
    if not os.path.isfile(p(game, "runtime/server/GameServer.dll")):
        stop("No Offline DAoC game in '%s' (runtime/server/GameServer.dll is missing)." % game)
    print("Game folder: " + game)
    r = running()
    if r:
        stop("Close the server, launcher and game first (running: %s)." % ", ".join(sorted(r)))

    # 3. the right release
    base = info["BaseRelease"]
    manifest = read_manifest(os.path.join(game, "PACKAGE MANIFEST.sha256"))
    if manifest is None:
        if not force:
            stop("No PACKAGE MANIFEST.sha256 in the game folder, so it can't be checked as Offline DAoC %s." % base)
        manifest = {}
    elif not release_ok(manifest, base) and not force:
        stop("This game folder isn't Offline DAoC %s; this build only fits that release." % base)

    # 4. what is there now
    records = install_records(game)
    known = {e["NewDll"]: rec["BuildName"] for rec in records for e in as_list(rec.get("Folders")) if e.get("NewDll")}
    new_dll, new_pdb = info["GameServer.dll"].lower(), info["GameServer.pdb"].lower()
    plan = []
    for folder in SERVER_FOLDERS:
        dll = p(game, folder + "/GameServer.dll")
        if not os.path.isfile(dll):
            continue
        h = sha(dll)
        state = ("this build" if h == new_dll else
                 "original " + base if manifest.get(folder + "/GameServer.dll") == h else
                 "fork build " + known[h] if h in known else "unknown")
        plan.append({"Folder": folder, "Hash": h, "State": state})
        print("  %-26s %s" % (folder, state))
    if not plan:
        stop("No GameServer.dll found under runtime/server.")
    if any(x["State"] == "unknown" for x in plan) and not force:
        stop("A GameServer.dll above is neither the original %s file nor a fork build this script installed." % base)

    known_l = {e["NewDll"]: rec["BuildName"] for rec in records
               for e in as_list(rec.get("LauncherFolders")) if e and e.get("NewDll")}
    nc_manifest = read_manifest(p(game, "new-client/PACKAGE MANIFEST.sha256")) or {}
    lplan = []
    if lnew:
        for folder in LAUNCHER_FOLDERS:
            dll = p(game, folder + "/OfflineDAoC.dll")
            if not os.path.isfile(dll):
                if folder == "runtime":
                    stop("No launcher (runtime/OfflineDAoC.dll) in this game folder.")
                continue
            h = sha(dll)
            all_new = all(os.path.isfile(p(game, folder + "/" + n)) and sha(p(game, folder + "/" + n)) == lnew[n]
                          for n in LAUNCHER_FILES)
            state = ("this build" if h == lnew["OfflineDAoC.dll"] and all_new else
                     "this build, other files differ" if h == lnew["OfflineDAoC.dll"] else
                     "original %s launcher" % base if manifest.get(folder + "/OfflineDAoC.dll") == h else
                     "original %sc launcher" % base if nc_manifest.get(folder + "/OfflineDAoC.dll") == h else
                     "fork build %s launcher" % known_l[h] if h in known_l else "unknown")
            lplan.append({"Folder": folder, "Hash": h, "State": state})
            print("  %-26s %s" % (folder + " (launcher)", state))
    if any(x["State"] == "unknown" for x in lplan) and not force:
        stop("A launcher (OfflineDAoC.dll) above is neither the original %s file nor a fork build." % base)

    cplan = []
    for patch in patches:
        path = p(game, patch["Path"])
        if not os.path.isfile(path):
            state = "not in this game, skipped"
        else:
            h = sha(path)
            if h == patch["Fixed"]:
                state = "fixed already"
            elif h == patch["Released"]:
                state = "released file, will be fixed"
                if patch.get("File"):
                    shipped = p(build, patch["File"].replace("\\", "/"))
                    if not os.path.isfile(shipped) or sha(shipped) != patch["Fixed"]:
                        stop("%s is missing or doesn't match content-patches.json." % patch["File"])
                cplan.append({"Patch": patch, "Path": path, "Hash": h})
            else:
                state = "not the released file, left alone"
        print("  %s: %s" % (patch["Path"], state))

    if all(x["State"] == "this build" for x in plan) and all(x["State"] == "this build" for x in lplan) and not cplan:
        print("\nAlready installed: %s. Nothing to do." % info["BuildName"])
        return
    if dry:
        print("\nDry run: every check passed. Nothing was changed.")
        return

    # 5. back up, copy, verify
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = p(game, "runtime/server/deployment-backups/fork-build-" + stamp)
    n = 1
    while os.path.exists(backup):
        n += 1
        backup = p(game, "runtime/server/deployment-backups/fork-build-%s-%d" % (stamp, n))
    os.makedirs(backup)
    bname = lambda folder: re.sub(r"[\\/]", "_", folder)

    entries = []
    for item in plan:
        src, saved = p(game, item["Folder"]), os.path.join(backup, bname(item["Folder"]))
        os.makedirs(saved)
        shutil.copy2(os.path.join(src, "GameServer.dll"), saved)
        old_pdb = None
        if os.path.isfile(os.path.join(src, "GameServer.pdb")):
            shutil.copy2(os.path.join(src, "GameServer.pdb"), saved)
            old_pdb = sha(os.path.join(saved, "GameServer.pdb"))
        if sha(os.path.join(saved, "GameServer.dll")) != item["Hash"]:
            stop("Backup of %s failed verification." % item["Folder"])
        entries.append({"Folder": item["Folder"], "OldDll": item["Hash"], "OldPdb": old_pdb,
                        "NewDll": new_dll, "NewPdb": new_pdb})

    lentries = []
    for item in lplan:
        src, saved = p(game, item["Folder"]), os.path.join(backup, bname(item["Folder"]) + "_launcher")
        os.makedirs(saved)
        files = []
        for name in LAUNCHER_FILES:
            f, old = os.path.join(src, name), None
            if os.path.isfile(f):
                shutil.copy2(f, saved)
                old = sha(f)
                if sha(os.path.join(saved, name)) != old:
                    stop("Backup of %s/%s failed verification." % (item["Folder"], name))
            files.append({"Name": name, "OldHash": old, "NewHash": lnew[name]})
        lentries.append({"Folder": item["Folder"], "OldDll": item["Hash"], "NewDll": lnew["OfflineDAoC.dll"],
                         "Files": files})

    centries = []
    for item in cplan:
        saved = p(os.path.join(backup, "content"), item["Patch"]["Path"])
        os.makedirs(os.path.dirname(saved), exist_ok=True)
        shutil.copy2(item["Path"], saved)
        if sha(saved) != item["Hash"]:
            stop("Backup of %s failed verification." % item["Patch"]["Path"])
        centries.append({"Path": item["Patch"]["Path"], "OldHash": item["Hash"], "NewHash": item["Patch"]["Fixed"]})

    helpers = []
    hsrc = p(build, "tools/claude-version")
    htgt = p(game, "tools/claude-version")
    if os.path.isdir(hsrc):
        for name in sorted(os.listdir(hsrc)):
            f = os.path.join(hsrc, name)
            if not os.path.isfile(f):
                continue
            had = os.path.isfile(os.path.join(htgt, name))
            if had:
                os.makedirs(os.path.join(backup, "helpers"), exist_ok=True)
                shutil.copy2(os.path.join(htgt, name), os.path.join(backup, "helpers"))
            helpers.append({"Name": name, "Existed": had, "New": sha(f)})

    record = {"BuildName": info["BuildName"], "BaseRelease": base, "Commit": info["Commit"],
              "Installed": datetime.datetime.now().astimezone().isoformat(), "GameFolder": game,
              "Folders": entries, "Helpers": helpers, "LauncherFolders": lentries, "ContentFiles": centries,
              "InstalledBy": "install_living_server.py (Linux port of install-server-build.ps1)"}
    with open(os.path.join(backup, "install-record.json"), "w", encoding="utf-8") as f:
        json.dump(record, f, indent=2)

    try:
        for e in entries:
            tgt = p(game, e["Folder"])
            shutil.copyfile(os.path.join(build, "GameServer.dll"), os.path.join(tgt, "GameServer.dll"))
            shutil.copyfile(os.path.join(build, "GameServer.pdb"), os.path.join(tgt, "GameServer.pdb"))
            if sha(os.path.join(tgt, "GameServer.dll")) != new_dll or sha(os.path.join(tgt, "GameServer.pdb")) != new_pdb:
                raise RuntimeError("copy into %s failed verification" % e["Folder"])
        for e in lentries:
            tgt = p(game, e["Folder"])
            for fl in e["Files"]:
                shutil.copyfile(os.path.join(lbuild, fl["Name"]), os.path.join(tgt, fl["Name"]))
                if sha(os.path.join(tgt, fl["Name"])) != fl["NewHash"]:
                    raise RuntimeError("copy of the launcher into %s failed verification" % e["Folder"])
        for item in cplan:
            fixed = expand_patch(item["Patch"], open(item["Path"], "rb").read(), build)
            if hashlib.sha256(fixed).hexdigest() != item["Patch"]["Fixed"]:
                raise RuntimeError("the fix for %s didn't come out right" % item["Patch"]["Path"])
            with open(item["Path"], "wb") as f:
                f.write(fixed)
            if sha(item["Path"]) != item["Patch"]["Fixed"]:
                raise RuntimeError("writing %s failed verification" % item["Patch"]["Path"])
        if helpers:
            os.makedirs(htgt, exist_ok=True)
            for hl in helpers:
                shutil.copyfile(os.path.join(hsrc, hl["Name"]), os.path.join(htgt, hl["Name"]))
        docs = os.path.join(build, "docs")
        if os.path.isdir(docs):
            shutil.copytree(docs, p(game, "docs/fork-build-" + info["BuildName"]), dirs_exist_ok=True)
    except Exception as ex:
        print("Install failed: %s. Restoring the backup..." % ex)
        for e in entries:
            saved, tgt = os.path.join(backup, bname(e["Folder"])), p(game, e["Folder"])
            shutil.copyfile(os.path.join(saved, "GameServer.dll"), os.path.join(tgt, "GameServer.dll"))
            if e["OldPdb"]:
                shutil.copyfile(os.path.join(saved, "GameServer.pdb"), os.path.join(tgt, "GameServer.pdb"))
            elif os.path.isfile(os.path.join(tgt, "GameServer.pdb")):
                os.remove(os.path.join(tgt, "GameServer.pdb"))
        for e in lentries:
            saved, tgt = os.path.join(backup, bname(e["Folder"]) + "_launcher"), p(game, e["Folder"])
            for fl in e["Files"]:
                if fl["OldHash"]:
                    shutil.copyfile(os.path.join(saved, fl["Name"]), os.path.join(tgt, fl["Name"]))
                elif os.path.isfile(os.path.join(tgt, fl["Name"])):
                    os.remove(os.path.join(tgt, fl["Name"]))
        for e in centries:
            shutil.copyfile(p(os.path.join(backup, "content"), e["Path"]), p(game, e["Path"]))
        with open(os.path.join(backup, "rolled-back.txt"), "w") as f:
            f.write("Install failed and was undone at %s.\n" % datetime.datetime.now().isoformat())
        print("The original files are back in place.")
        sys.exit(1)

    print("\nInstalled %s: the server into %d folder(s), the launcher into %d, %d game file(s) fixed."
          % (info["BuildName"], len(entries), len(lentries), len(centries)))
    print("Backup: " + backup)
    print("The server log shows: FORK_BUILD name=%s commit=%s" % (info["BuildName"], info["Commit"]))
    print("Every new feature is OFF until you switch it on: launcher -> Living Server tab (server stopped).")


if __name__ == "__main__":
    main()
