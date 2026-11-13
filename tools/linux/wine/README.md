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
