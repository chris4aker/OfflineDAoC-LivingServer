> **In Living Server:** Ryan's scripts from [ryan-grafana/offline-daoc-linux](https://github.com/ryan-grafana/offline-daoc-linux)
> (commit 80dc67b, GPL-3.0, with his permission). They were written for Living Server **1.1-beta** on 0.37b and are
> kept here as he published them. The one change: `install_living_server.py` refuses Living Server 1.1.2 and later
> builds, because it doesn't install their server libraries or world backup. The Linux route built from them is in
> [docs/LINUX.md](../../../docs/LINUX.md) §0.

# Offline DAoC on Linux

Scripts to install and run [Offline DAoC](https://github.com/shadowofze/OfflineDAoC) with the
[Living Server](https://github.com/chris4aker/OfflineDAoC-LivingServer) add-on on
Ubuntu/GNOME using Wine. No game files are included: the setup script downloads everything
from the upstream releases.

## Setup

```bash
./scripts/setup-offline-daoc.sh          # every step, asking before each
./scripts/setup-offline-daoc.sh list     # show the steps
```

Steps: `dirs packages assets addon git dotnet nuget winetricks wineprefix game`. Each one says
what it downloads, where, and how big, then waits for y/N. Nothing runs `sudo`; needed system
packages are printed for you to install. The project folder defaults to `~/OfflineDAoC`
(override with `ODAOC_ROOT`); see the top of the script for the other settings.

## Run

```bash
./start-daoc.sh
```

Starts the launcher under Wine with a smaller .NET server GC footprint, and runs
`scripts/daoc_fullscreen.py` to keep the client truly fullscreen so GNOME's top bar doesn't
push the bottom of the game window off-screen.

## Scripts

| File | What it does |
|---|---|
| `scripts/setup-offline-daoc.sh` | Mirrors releases and sources locally, installs .NET, winetricks deps, the Wine prefix and the game |
| `scripts/odaoc_helper.py` | JSON parsing, hash checks and safe zip extraction for the setup script |
| `scripts/install_living_server.py` | Linux port of the add-on's `install-server-build.ps1` (same backups and `install-record.json`, so `ROLLBACK.cmd` still works) |
| `scripts/daoc_fullscreen.py` | Watches for the client window and makes it fullscreen |
| `start-daoc.sh` | Launches everything |
