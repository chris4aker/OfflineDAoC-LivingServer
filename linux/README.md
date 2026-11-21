# Offline DAoC on Linux (Wine)

`start-offline-daoc.sh` is the Linux counterpart of `START OFFLINE DAOC.cmd`. It needs Wine and Python 3, and a Wine
prefix that already runs the game (.NET Framework 3.5, Visual C++ 2019, core fonts).

**This folder is not in any release package.** It is only in the repository, so it is for people who have a clone of
it; the downloads and updates never contain or change these scripts.

```bash
WINEPREFIX=~/OfflineDAoC/wineprefix linux/start-offline-daoc.sh ~/OfflineDAoC/game
```

The game folder defaults to `$ODAOC_GAME`, then `~/OfflineDAoC/game`. The prefix defaults to `wineprefix` next to the
game folder. The script doesn't create a prefix or choose its architecture (`WINEARCH`); it stops when there is none.

What it adds on Linux:

- **Updates:** Wine's `powershell.exe` is a stub that runs nothing, so the launcher's "Get update" (and `INSTALL.cmd`)
  never install anything. When the launcher closes for an update, `finish_launcher_update.py` installs the unpacked
  package with `install_living_server.py`, a port of `install-server-build.ps1` (same checks, backups and
  `install-record.json`). It then writes `last-update.txt` and opens the launcher again, whether the install worked or
  not, so the launcher can show the result (as on Windows).
  - Only an update the launcher handed over in the last 24 hours is installed, only from a package folder inside the
    game folder, and never a build older than the installed one. Anything else is logged in
    `runtime/logs/launcher-updates.log` as `apply-update (Linux): skipped ...` and not tried again.
  - If the server, launcher or game is still running, the update stays waiting and is installed at the next start.
- **Fullscreen:** `daoc_fullscreen.py` makes the client window truly fullscreen, so GNOME's top bar doesn't push the
  Continue / Accept buttons off the screen. A client window that leaves fullscreen is put back within about 2 seconds.
  Set `ODAOC_FULLSCREEN=once` to make each client window fullscreen one time and then leave it alone, or
  `ODAOC_FULLSCREEN=off` to not touch the window at all.

Server memory is set by the launcher's **Lean server memory** option, not by this script.

Logs go to `runtime/logs/linux-launcher.log` and `runtime/logs/linux-fullscreen.log` in the game folder. Both are
appended to, not started over, each time the launcher opens.

This is a stopgap for the Windows packages until the Linux work in #206 lands.
