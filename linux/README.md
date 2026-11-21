# Offline DAoC on Linux (Wine)

`start-offline-daoc.sh` is the Linux counterpart of `START OFFLINE DAOC.cmd`. It needs Wine and Python 3, and a Wine
prefix that already runs the game (.NET Framework 3.5, Visual C++ 2019, core fonts).

```bash
WINEPREFIX=~/OfflineDAoC/wineprefix linux/start-offline-daoc.sh ~/OfflineDAoC/game
```

The game folder defaults to `$ODAOC_GAME`, then `~/OfflineDAoC/game`. The prefix defaults to `wineprefix` next to the
game folder.

What it adds on Linux:

- **Updates:** Wine's `powershell.exe` is a stub that runs nothing, so the launcher's "Get update" (and `INSTALL.cmd`)
  never install anything. When the launcher closes for an update, `finish_launcher_update.py` installs the unpacked
  package with `install_living_server.py`, a port of `install-server-build.ps1` (same checks, backups and
  `install-record.json`). It then writes `last-update.txt` and opens the launcher again.
- **Fullscreen:** `daoc_fullscreen.py` makes the client window truly fullscreen, so GNOME's top bar doesn't push the
  Continue / Accept buttons off the screen.

Server memory is set by the launcher's **Lean server memory** option, not by this script.

Logs go to `runtime/logs/linux-launcher.log` and `runtime/logs/linux-fullscreen.log` in the game folder.

This is a stopgap for the Windows packages until the Linux work in #206 lands.
