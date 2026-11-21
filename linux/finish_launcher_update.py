#!/usr/bin/env python3
"""Finish a Living Server update that the launcher started, on Linux.

The launcher's "Get update" downloads and checks the package, unpacks it into the game folder, then hands the install
to runtime\\updates\\apply-update.ps1 through Windows PowerShell and closes. Wine has no Windows PowerShell (its
powershell.exe is a stub that runs nothing), so under Wine the update stops there: the package sits unpacked and the
old build stays installed. This does apply-update.ps1's job instead, with install_living_server.py:

  1. Reads the launcher's update log (runtime/logs/launcher-updates.log). An update is pending when its last
     "Handing over to ..." line has no "apply-update" line after it (on Windows the script logs every step).
  2. Installs the package folder that line names, with the same checks and backups as INSTALL.cmd.
  3. Checks that the server and launcher are now the package's files, writes the result to
     runtime/updates/last-update.txt in apply-update.ps1's format (the launcher shows it when it opens next) and logs
     each step, so the same update is never tried twice.

  python3 -I finish_launcher_update.py <game folder>

Exit codes: 0 installed (open the launcher again), 1 an update was pending but didn't install, 3 nothing pending.
"""
import datetime
import hashlib
import os
import re
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
INSTALLER = os.path.join(HERE, "install_living_server.py")
sys.path.insert(0, HERE)
from install_living_server import running  # noqa: E402  (the installer's own "is the game closed?" check)
HANDOVER = re.compile(r"Handing over to .*?apply-update\.ps1: .*? runs (?P<cmd>.+?\\INSTALL\.cmd)")
TAG = "apply-update (Linux)"


def sha(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for c in iter(lambda: f.read(1 << 22), b""):
            h.update(c)
    return h.hexdigest()


def read_kv(path):
    out = {}
    for line in open(path, encoding="utf-8-sig"):
        m = re.match(r"^\s*([^=#]+?)\s*=\s*(.*?)\s*$", line)
        if m:
            out[m.group(1)] = m.group(2)
    return out


def unix_path(win):
    """A Wine path (Z:\\home\\...) as a Linux one, through the prefix's drive links."""
    m = re.match(r"^([A-Za-z]):\\(.*)$", win)
    if not m:
        return None
    rest = m.group(2).replace("\\", "/")
    drive = m.group(1).lower()
    prefix = os.environ.get("WINEPREFIX") or os.path.expanduser("~/.wine")
    link = os.path.join(prefix, "dosdevices", drive + ":")
    if os.path.exists(link):
        return os.path.join(os.path.realpath(link), rest)
    return "/" + rest if drive == "z" else None


def pending(log):
    """The package folder of the launcher's last hand-over, if nothing has handled it yet."""
    if not os.path.isfile(log):
        return None
    folder = None
    for line in open(log, encoding="utf-8-sig", errors="replace"):
        m = HANDOVER.search(line)
        if m:
            folder = m.group("cmd").rsplit("\\", 1)[0]
        elif "apply-update" in line:
            folder = None   # apply-update.ps1 (Windows) or this script already took it
    return folder


def main():
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    game = os.path.realpath(sys.argv[1])
    log = os.path.join(game, "runtime", "logs", "launcher-updates.log")
    win_folder = pending(log)
    if not win_folder:
        return 3
    busy = running()
    for _ in range(60):   # the launcher has just closed: give its server and Wine a moment to exit (as apply-update.ps1)
        if not busy:
            break
        time.sleep(0.5)
        busy = running()
    if busy:
        # Left pending (nothing logged), so it's finished once everything is closed.
        print("A Living Server update is waiting to be installed, but these are still running: %s. "
              "Close them and run start-offline-daoc.sh again." % ", ".join(sorted(busy)))
        return 1

    def say(text):
        print(text, flush=True)
        stamp = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with open(log, "a", encoding="utf-8") as f:
            f.write("%s  %s: %s\n" % (stamp, TAG, text))

    updates = os.path.join(game, "runtime", "updates")
    result = os.path.join(updates, "last-update.txt")

    def finish(state, detail, build):
        os.makedirs(updates, exist_ok=True)
        now = datetime.datetime.now(datetime.timezone.utc).isoformat()
        with open(result, "w", encoding="utf-8") as f:
            f.write("Build=%s\nResult=%s\nDetail=%s\nTime=%s\n" % (build, state, detail, now))
        say("%s: %s" % (state, detail))
        return 0 if state == "installed" else 1

    folder = unix_path(win_folder)
    info_path = os.path.join(folder, "BUILD-INFO.txt") if folder else None
    if not info_path or not os.path.isfile(info_path):
        say("The launcher handed over %s, but it isn't a package folder here." % win_folder)
        return finish("not installed", "the package folder %s wasn't found; nothing was changed." % win_folder, "")
    info = read_kv(info_path)
    build = info.get("BuildName", "")
    say("Update to %s, from %s into %s (Windows PowerShell isn't available under Wine, so %s installs it)"
        % (build, folder, game, os.path.basename(INSTALLER)))

    out = subprocess.run([sys.executable, "-I", INSTALLER, folder, game],
                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    for line in out.stdout.splitlines():
        say("  " + line)
    say("%s finished (exit %d)." % (os.path.basename(INSTALLER), out.returncode))

    # Installed means: the server and launcher in the game folder are now this package's files (as apply-update.ps1).
    dll = os.path.join(game, "runtime", "server", "GameServer.dll")
    ok = os.path.isfile(dll) and sha(dll) == info.get("GameServer.dll", "").lower()
    if ok and info.get("launcher/OfflineDAoC.dll"):
        ok = sha(os.path.join(game, "runtime", "OfflineDAoC.dll")) == info["launcher/OfflineDAoC.dll"].lower()
    if ok:
        backup = re.search(r"^Backup: (.+)$", out.stdout, re.M)
        return finish("installed", "Living Server %s is installed. %s" % (
            build, "Backup of what it replaced: " + backup.group(1) if backup else "It was already in place."), build)
    return finish("not installed", "the installer did not install it (its output is in the log); "
                  "the game is as it was before.", build)


if __name__ == "__main__":
    sys.exit(main())
