# Grafana Cloud kit

Issue #647 (tracker); #649 (setup) and #652 (dashboards). Sends a Living Server's health, bots and world to **your own Grafana Cloud stack**:
- `server.log` goes to Loki, sanitized: no chat, names, accounts, paths or IPs leave the PC. Account and network rows
  are dropped, the line is rebuilt from its parsed parts (no stack traces), and every value with a letter becomes `~`
  unless its key is a reviewed structural key (realm, zone, class, ...), each checked against the code that writes it;
- log-derived metrics (AI tick percentiles, game-loop stages, memory, bots online by realm) and the server process's
  CPU and memory go to Prometheus.

It works on **Ubuntu Linux** (native server or Wine) and **Windows 10/11**, with or without an existing Grafana Cloud
account, and with or without an Alloy or Grafana Agent already installed. It is built to be run by an AI agent such as
Claude Code: the agent follows [RUNBOOK.md](RUNBOOK.md), does every step it can, and stops with exact instructions for
the few steps only you can do (sign-up, tokens, one folder permission).

**Where to start**
- An AI agent setting it up: [RUNBOOK.md](RUNBOOK.md).
- A player who wants to know what they'll be asked to do (sign-up, tokens, one folder permission):
  [USER-ACTIONS.md](USER-ACTIONS.md).

**Status: tests only.** The Ubuntu pipeline is the one a contributor runs today against Grafana Cloud. These scripts
are new and have run in tests and in a real Alloy on Linux, not yet on a Windows PC.

| File | What it does |
|---|---|
| `RUNBOOK.md` | The agent runbook: prerequisites, paths A-D, every step tagged [AGENT] or [USER] with its check, troubleshooting |
| `USER-ACTIONS.md` | Every step only the player can do, in order, for Ubuntu and Windows, by path |
| `detect.sh`, `detect.ps1` | Read-only: logs folder, any Alloy or Grafana Agent, gcx, a free port, the env file's state, the path to take. `--check-env` / `-CheckEnv` checks the env file without printing values |
| `install.sh`, `uninstall.sh` | Ubuntu: this kit's own Alloy as a systemd **user** service (no sudo) |
| `install.ps1`, `uninstall.ps1` | Windows: this kit's own Alloy for the current user, started at logon (no admin) |
| `run-alloy.sh`, `run-alloy.ps1` | What the service or logon task runs |
| `monitoring.env.example` | The settings; the install copies it to `monitoring.env` (gitignored) for you to fill in |
| `alloy/common`, `alloy/linux`, `alloy/windows` | The Alloy pipeline; the install copies `common` plus your OS's folder |
| `dashboards.py`, `dashboard_parts.py` | Generate the three ✦ dashboards (Overview, Server Health, Bots & World) into `dashboards/`, plus gcx resources in `dashboards/gcx/` (and the folder in `dashboards/gcx-folder/`); `--check` fails when they are stale (#652) |
| `pins.json` | The pinned Alloy release and its SHA256, from the release's own `SHA256SUMS` |

Where it installs: `~/.local/share/livingserver-monitoring` on Ubuntu, `%LOCALAPPDATA%\LivingServerMonitoring` on Windows.
It never changes the game install, `serverconfig.xml`, port 10300, or another Alloy or Grafana Agent.

Tests: `python3 -m unittest discover -s tools/grafana/cloud/tests` (in `tools/check.py`). With `ALLOY_BIN=/path/to/alloy`
they also run the configs in a real Alloy, with Loki output swapped for `loki.echo`; with `pwsh` on PATH they parse and run
the PowerShell scripts.

#651 moves the log filter onto #628's generated fail-closed row filter once that lands.
