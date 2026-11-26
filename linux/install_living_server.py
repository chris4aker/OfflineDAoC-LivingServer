#!/usr/bin/env python3
"""Install a DAoC - Living Server build into a game folder on Linux.

A faithful port of the add-on's own install-server-build.ps1 (INSTALL.cmd needs Windows PowerShell):
same checks, same backup layout and install-record.json, so the add-on's ROLLBACK.cmd still understands it.
Follows install-server-build.ps1 as of Living Server 1.2: it also installs the server's own libraries
(core/: CoreBase, CoreDatabase, CoreServer, since 1.1.2), backs up the world database first, and (since 1.2) updates
a Living Server file of its own over an earlier Living Server version of it (content-patches.json "Replaces", the
1.1 loading screen), recording "Released" and "Keep" so ROLLBACK.cmd leaves the loading screen in place.

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
# From 1.2.3 (#641) the launcher is DAoCLivingServer.*, plus OfflineDAoC.exe, a byte copy of its apphost (it runs
# DAoCLivingServer.dll whatever its own name). The older launcher's other OfflineDAoC.* files stay where they are.
LAUNCHER_FILES = ["DAoCLivingServer.dll", "DAoCLivingServer.exe", "DAoCLivingServer.pdb", "DAoCLivingServer.deps.json",
                  "DAoCLivingServer.runtimeconfig.json", "OfflineDAoC.exe"]
LAUNCHER_DLL, LEGACY_LAUNCHER_DLL = "DAoCLivingServer.dll", "OfflineDAoC.dll"
# The server's own libraries beside GameServer.dll (Living Server 1.1.2+ ships them in the build's core/ folder; a
# new GameServer.dll with the released CoreDatabase.dll stops at start). Each replaces the folder's copy only where the
# folder has one; its .pdb goes with it where the folder has a .pdb too.
CORE_FILES = ["CoreBase.dll", "CoreDatabase.dll", "CoreServer.dll"]
DB_KEEP = 3   # world database copies kept in deployment-backups (newest installs); older data/ folders are removed
PROCESS_NAMES = {"CoreServer", "DOLServer", "DAoCLivingServer", "OfflineDAoC", "game", "camelot", "connect",
                 "Godot_v4.7.2-stable_mono_win64"}
# Names any Linux program could have: they count only as a Windows program (game.exe). Under Wine the classic client
# runs as game.dll.
GENERIC_NAMES = {"game", "camelot", "connect"}
CLIENT_DLL = "game.dll"


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
            name = running_name(n)
            if name:
                found.add(name)
    return found


def running_name(n):
    """The game program a process name or argument stands for, or None."""
    if n.lower() == CLIENT_DLL:
        return CLIENT_DLL
    exe = n.lower().endswith(".exe")
    stem = n[:-4] if exe else n
    if stem in PROCESS_NAMES and (exe or stem not in GENERIC_NAMES):
        return stem
    return None


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


def patch_replaces(patch):
    """Earlier Living Server versions of a file of its own (1.2): only for entries that ship the whole file."""
    if not patch.get("File") or patch.get("Replaces") is None:
        return []
    return [str(h).lower() for h in as_list(patch["Replaces"]) if h]


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


def prune_db_backups(game, keep):
    """Keep the world database copies (data/) of the newest `keep` installs; server and launcher backups stay."""
    root = p(game, "runtime/server/deployment-backups")
    if not os.path.isdir(root):
        return 0
    with_data = sorted(d for d in os.listdir(root)
                       if d.startswith("fork-build-") and os.path.isdir(os.path.join(root, d, "data")))
    old = with_data[:-keep] if len(with_data) > keep else []
    for d in old:
        shutil.rmtree(os.path.join(root, d, "data"))
    return len(old)


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
    print("Living Server build %s (commit %s) for base game %s" % (info["BuildName"], info["Commit"], info["BaseRelease"]))

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
    cbuild = os.path.join(build, "core")
    cnew = {}
    for name in CORE_FILES:
        for fname in (name, name[:-4] + ".pdb"):
            want, f = info.get("core/" + fname), os.path.join(cbuild, fname)
            if not want and not os.path.isfile(f):
                continue
            if not want or not os.path.isfile(f):
                stop("core/%s is missing from this build folder or BUILD-INFO.txt." % fname)
            if sha(f) != want.lower():
                stop("core/%s doesn't match BUILD-INFO.txt (damaged download?)." % fname)
            cnew[fname] = want.lower()
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
        stop("No game in '%s' (runtime/server/GameServer.dll is missing)." % game)
    print("Game folder: " + game)
    r = running()
    if r:
        stop("Close the server, launcher and game first (running: %s)." % ", ".join(sorted(r)))

    # 3. the right release
    base = info["BaseRelease"]
    manifest = read_manifest(os.path.join(game, "PACKAGE MANIFEST.sha256"))
    if manifest is None:
        if not force:
            stop("No PACKAGE MANIFEST.sha256 in the game folder, so it can't be checked as base game %s." % base)
        manifest = {}
    elif not release_ok(manifest, base) and not force:
        stop("This game folder isn't base game %s; this build only fits that release." % base)

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

    known_c = {e["NewHash"]: rec["BuildName"] for rec in records
               for e in as_list(rec.get("CoreFiles")) if e and e.get("NewHash")}
    coreplan = []
    for folder in SERVER_FOLDERS:
        for name in CORE_FILES:
            if name not in cnew:
                continue
            path = p(game, folder + "/" + name)
            if not os.path.isfile(path):
                continue
            h = sha(path)
            pdb = name[:-4] + ".pdb"
            pdb_path = p(game, folder + "/" + pdb)
            with_pdb = pdb in cnew and os.path.isfile(pdb_path)
            pdb_done = not with_pdb or sha(pdb_path) == cnew[pdb]
            state = ("this build" if h == cnew[name] and pdb_done else
                     "this build, .pdb differs" if h == cnew[name] else
                     "original " + base if manifest.get(folder + "/" + name) == h else
                     "fork build " + known_c[h] if h in known_c else "unknown")
            coreplan.append({"Folder": folder, "Name": name, "Hash": h, "State": state, "WithPdb": with_pdb})
            print("  %-26s %s" % (folder + "/" + name, state))
    if any(x["State"] == "unknown" for x in coreplan) and not force:
        stop("A server library above is neither the original %s file nor a fork build this script installed." % base)

    known_l = {e["NewDll"]: rec["BuildName"] for rec in records
               for e in as_list(rec.get("LauncherFolders")) if e and e.get("NewDll")}
    nc_manifest = read_manifest(p(game, "new-client/PACKAGE MANIFEST.sha256")) or {}
    lplan = []
    if lnew:
        for folder in LAUNCHER_FOLDERS:
            name = LAUNCHER_DLL if os.path.isfile(p(game, folder + "/" + LAUNCHER_DLL)) else LEGACY_LAUNCHER_DLL
            dll = p(game, folder + "/" + name)
            if not os.path.isfile(dll):
                if folder == "runtime":
                    stop("No launcher (runtime/OfflineDAoC.dll or runtime/DAoCLivingServer.dll) in this game folder.")
                continue
            h = sha(dll)
            all_new = all(os.path.isfile(p(game, folder + "/" + n)) and sha(p(game, folder + "/" + n)) == lnew[n]
                          for n in LAUNCHER_FILES)
            state = ("this build" if h == lnew[LAUNCHER_DLL] and all_new else
                     "this build, other files differ" if h == lnew[LAUNCHER_DLL] else
                     "original %s launcher" % base if manifest.get(folder + "/" + name) == h else
                     "original %sc launcher" % base if nc_manifest.get(folder + "/" + name) == h else
                     "fork build %s launcher" % known_l[h] if h in known_l else "unknown")
            lplan.append({"Folder": folder, "Hash": h, "State": state})
            print("  %-26s %s" % (folder + " (launcher)", state))
    if any(x["State"] == "unknown" for x in lplan) and not force:
        stop("A launcher above is neither the original %s file nor a fork build." % base)

    cplan = []
    for patch in patches:
        path = p(game, patch["Path"])
        if not os.path.isfile(path):
            state = "not in this game, skipped"
        else:
            h = sha(path)
            earlier = h in patch_replaces(patch)
            if h == patch["Fixed"].lower():
                state = "fixed already"
            elif h == patch["Released"].lower() or earlier:
                state = "an earlier Living Server version, will be updated" if earlier else "released file, will be fixed"
                if patch.get("File"):
                    shipped = p(build, patch["File"].replace("\\", "/"))
                    if not os.path.isfile(shipped) or sha(shipped) != patch["Fixed"].lower():
                        stop("%s is missing or doesn't match content-patches.json." % patch["File"])
                cplan.append({"Patch": patch, "Path": path, "Hash": h})
            else:
                state = "not the released file (nor an earlier Living Server one), left alone"
        print("  %s: %s" % (patch["Path"], state))

    if (all(x["State"] == "this build" for x in plan) and all(x["State"] == "this build" for x in coreplan)
            and all(x["State"] == "this build" for x in lplan) and not cplan):
        print("\nAlready installed: %s. Nothing to do." % info["BuildName"])
        return
    if dry:
        print("\nDry run: every check passed. Nothing was changed.")
        return

    # 5. back up, copy, verify
    # The world database first (with the game closed, the .db and its -wal are a consistent set; -shm is an index).
    db = p(game, "runtime/data/opendaoc.sqlite3.db")
    dbfiles = [f for f in (db, db + "-wal", db + "-shm") if os.path.isfile(f)] if os.path.isfile(db) else []
    need = sum(os.path.getsize(f) for f in dbfiles) + 256 * 1024 * 1024
    if dbfiles and shutil.disk_usage(p(game, "runtime/server")).free < need:
        stop("Not enough free disk space to back up your world database first (%d MB needed)." % (need >> 20))
    stamp = datetime.datetime.now().strftime("%Y%m%d-%H%M%S")
    backup = p(game, "runtime/server/deployment-backups/fork-build-" + stamp)
    n = 1
    while os.path.exists(backup):
        n += 1
        backup = p(game, "runtime/server/deployment-backups/fork-build-%s-%d" % (stamp, n))
    os.makedirs(backup)
    bname = lambda folder: re.sub(r"[\\/]", "_", folder)

    dentries = []
    if not dbfiles:
        print("No world database in runtime/data yet; nothing to back up there.")
    else:
        saved_data = os.path.join(backup, "data")
        os.makedirs(saved_data)
        for f in dbfiles:
            h = sha(f)
            shutil.copy2(f, saved_data)
            if sha(os.path.join(saved_data, os.path.basename(f))) != h:
                shutil.rmtree(backup)
                stop("The backup of your world database (%s) failed verification. Nothing was installed."
                     % os.path.basename(f))
            dentries.append({"Name": os.path.basename(f), "Hash": h})
        print("World database backed up: " + saved_data)

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

    coreentries = []
    for item in coreplan:
        src, saved = p(game, item["Folder"]), os.path.join(backup, bname(item["Folder"]) + "_core")
        os.makedirs(saved, exist_ok=True)
        shutil.copy2(os.path.join(src, item["Name"]), saved)
        if sha(os.path.join(saved, item["Name"])) != item["Hash"]:
            stop("Backup of %s/%s failed verification." % (item["Folder"], item["Name"]))
        pdb, old_pdb, new_pdb_c = item["Name"][:-4] + ".pdb", None, None
        if item["WithPdb"]:
            shutil.copy2(os.path.join(src, pdb), saved)
            old_pdb, new_pdb_c = sha(os.path.join(saved, pdb)), cnew[pdb]
        coreentries.append({"Folder": item["Folder"], "Name": item["Name"], "OldHash": item["Hash"],
                            "NewHash": cnew[item["Name"]], "OldPdb": old_pdb, "NewPdb": new_pdb_c})

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
        lentries.append({"Folder": item["Folder"], "OldDll": item["Hash"], "NewDll": lnew[LAUNCHER_DLL],
                         "Files": files})

    centries = []
    for item in cplan:
        saved = p(os.path.join(backup, "content"), item["Patch"]["Path"])
        os.makedirs(os.path.dirname(saved), exist_ok=True)
        shutil.copy2(item["Path"], saved)
        if sha(saved) != item["Hash"]:
            stop("Backup of %s failed verification." % item["Patch"]["Path"])
        # Released: the game's own file (PREPARE-FOR-UPDATE.cmd puts it back even when OldHash is an earlier Living
        # Server one). Keep: Living Server's own file (the loading screen), which ROLLBACK.cmd never turns back into
        # the released one.
        centries.append({"Path": item["Patch"]["Path"], "OldHash": item["Hash"], "NewHash": item["Patch"]["Fixed"],
                         "Released": item["Patch"]["Released"],
                         "Keep": bool(item["Patch"].get("File") and item["Patch"].get("Keep"))})

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
              "Database": dentries, "CoreFiles": coreentries,
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
        for e in coreentries:
            tgt = p(game, e["Folder"])
            shutil.copyfile(os.path.join(cbuild, e["Name"]), os.path.join(tgt, e["Name"]))
            if sha(os.path.join(tgt, e["Name"])) != e["NewHash"]:
                raise RuntimeError("copy of %s into %s failed verification" % (e["Name"], e["Folder"]))
            if e["NewPdb"]:
                pdb = e["Name"][:-4] + ".pdb"
                shutil.copyfile(os.path.join(cbuild, pdb), os.path.join(tgt, pdb))
                if sha(os.path.join(tgt, pdb)) != e["NewPdb"]:
                    raise RuntimeError("copy of %s into %s failed verification" % (pdb, e["Folder"]))
        for e in lentries:
            tgt = p(game, e["Folder"])
            for fl in e["Files"]:
                shutil.copyfile(os.path.join(lbuild, fl["Name"]), os.path.join(tgt, fl["Name"]))
                if sha(os.path.join(tgt, fl["Name"])) != fl["NewHash"]:
                    raise RuntimeError("copy of the launcher into %s failed verification" % e["Folder"])
        for item in cplan:
            fixed = expand_patch(item["Patch"], open(item["Path"], "rb").read(), build)
            if hashlib.sha256(fixed).hexdigest() != item["Patch"]["Fixed"].lower():
                raise RuntimeError("the fix for %s didn't come out right" % item["Patch"]["Path"])
            with open(item["Path"], "wb") as f:
                f.write(fixed)
            if sha(item["Path"]) != item["Patch"]["Fixed"].lower():
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
        for e in coreentries:
            saved, tgt = os.path.join(backup, bname(e["Folder"]) + "_core"), p(game, e["Folder"])
            shutil.copyfile(os.path.join(saved, e["Name"]), os.path.join(tgt, e["Name"]))
            if e["OldPdb"]:
                pdb = e["Name"][:-4] + ".pdb"
                shutil.copyfile(os.path.join(saved, pdb), os.path.join(tgt, pdb))
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

    print("\nInstalled %s: the server into %d folder(s) (%d server librar%s), the launcher into %d, %d game file(s) fixed."
          % (info["BuildName"], len(entries), len(coreentries), "y" if len(coreentries) == 1 else "ies",
             len(lentries), len(centries)))
    print("Backup: " + backup)
    pruned = prune_db_backups(game, DB_KEEP)
    if pruned:
        print("Removed the world database copies of %d older install(s) (the newest %d are kept; their server files stay)."
              % (pruned, DB_KEEP))
    print("The server log shows: FORK_BUILD name=%s commit=%s" % (info["BuildName"], info["Commit"]))
    print("Living Server's features are set in the launcher's Living Server tab (change them with the server stopped).")
    print("To undo it: the backup above holds every file it replaced (ROLLBACK.cmd needs Windows PowerShell).")


if __name__ == "__main__":
    main()
