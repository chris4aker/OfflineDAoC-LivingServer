# Grafana Cloud setup: agent runbook

**Start here if you are an AI agent** (Claude Code or similar) asked to set up Living Server monitoring on this PC.
People who want to know what they will be asked to do: read [USER-ACTIONS.md](USER-ACTIONS.md). Issue #649.

Goal: the player's server sends sanitized log rows and metrics to their own Grafana Cloud stack, and three dashboards
show them. You do every **[AGENT]** step. At each **[USER]** step you show the player the text from USER-ACTIONS.md
(same step ID, `<...>` filled in), wait, and run that step's check before going on.

## Rules
1. **No secrets in chat.** Never ask for a token or password, never put one on a command line, in a file in the
   repository, or in your output. Tokens go into `monitoring.env` (the player edits it) or gcx's own prompt.
   `detect --check-env` checks the file without printing values; don't `cat` it.
2. **Ask before:** a download, a change to an Alloy or Grafana Agent that is already there, overwriting dashboards
   that already exist, anything that costs money. One yes per thing.
3. **Never touch the game:** no edits to the install or `serverconfig.xml`, never start or stop the server (port 10300).
4. **Leave other collectors alone.** This kit runs its own user-level Alloy beside them.
5. **Report honestly:** say which checks ran live against Grafana Cloud and which didn't.

## Prerequisites
| | Ubuntu (20.04+, native server or Wine) | Windows 10/11 |
|---|---|---|
| Shell | bash, `curl`, `python3`, `sha256sum` (all stock) | Windows PowerShell 5.1 (stock) or PowerShell 7 |
| Service | systemd user session (`systemctl --user` works) | Task Scheduler, or the Startup folder |
| Rights | your user; no sudo | your user; no admin |
| Network | HTTPS to github.com (download) and `*.grafana.net` (sending) | same |
| This repository | a checkout; run every command from its root | same |

Command forms below: Ubuntu first, then Windows. `PS>` means
`powershell -NoProfile -ExecutionPolicy Bypass -File` (in PowerShell 7, `pwsh` works the same).

## The steps at a glance
| Step | Who | Ubuntu | Windows | Done when |
|---|---|---|---|---|
| A0 Detect | agent | `tools/grafana/cloud/detect.sh` | `PS> tools\grafana\cloud\detect.ps1` | JSON with `path` A-D |
| U0 Plan and yes | player | | | logs folder, account answer, yes to download |
| U1 Sign up (A, B) | player | | | `/api/health` answers |
| A1 Existing stack (C, D) | agent | | | `/api/health` answers |
| U-B Keep existing collector (B, D) | player | | | yes |
| A2 Install | agent | `install.sh --port <p>` | `PS> install.ps1 -Port <p>` | binary verified, env file created |
| U2 Token and settings | player | | | `detect --check-env` exits 0 |
| A3 Start | agent | `install.sh` again | `PS> install.ps1` again | collector healthy |
| U3 gcx access | player | | | `gcx config check` passes |
| A4 Dashboards | agent | `gcx resources push -p tools/grafana/cloud/dashboards/gcx` | same | three dashboards read back |
| U4 Start the server | player | | | |
| A5 Verify | agent | `curl` + gcx queries | `Invoke-WebRequest` + gcx | data under `instance=<LS_INSTANCE>` |

---

## A0 [AGENT] Detect
```bash
tools/grafana/cloud/detect.sh
```
```
PS> tools\grafana\cloud\detect.ps1
```
Read the JSON: `logs_dirs`, `alloy`, `grafana_agent`, `existing_collector`, `kit`, `gcx`, `free_port`, `env`, `next`.
- `logs_dirs` empty: ask for the folder (U0) and rerun with `--logs <dir>` / `-Logs <dir>`. Several: ask which.
- `kit.running` true: it is already set up; go to A5.
- Then ask whether they have a Grafana Cloud account (U0) and rerun with `--has-account yes|no` /
  `-HasAccount yes|no`. `path` is now set:

| | No Alloy | Alloy or Grafana Agent already installed |
|---|---|---|
| **No Grafana Cloud account** | A | B |
| **Has a Grafana Cloud account** | C | D |

`next` tells you where to go: `ask_has_account`, `ask_logs_folder`, `user_credentials` (A2 then U2), `install`
(A2/A3), `verify` (A5).

## U0 [USER] Plan and yes
Summarise what detect found in two or three lines, then show USER-ACTIONS.md U0. Download only after a yes.

## U1 [USER] Create a Grafana Cloud account (paths A, B)
Show USER-ACTIONS.md U1. **Check:** `https://<name>.grafana.net/api/health` returns JSON with `"database": "ok"`
(`curl -fsS <url>` / `Invoke-RestMethod <url>`).

## A1 [AGENT] Existing stack (paths C, D)
Ask for the stack name only, then the same health check. If `gcx.logged_in` is true, run `gcx dashboards list` and
tell the player whether "Living Server" dashboards already exist; ask before A4 overwrites them.

## U-B [USER] Keep the existing collector (paths B, D)
Say: "You already have <Alloy / Grafana Agent>. I'll leave it as it is and run a separate Alloy just for the game on
port <free_port>." (USER-ACTIONS.md U-B has the full text.) Wait for a yes. If the existing one ships host metrics (`node_*`), the dashboards' host panels use
them.

## A2 [AGENT] Install
```bash
tools/grafana/cloud/install.sh --port <free_port>
```
```
PS> tools\grafana\cloud\install.ps1 -Port <free_port>
```
It downloads Alloy (cached in the kit's `downloads/`), refuses it unless the SHA256 matches `pins.json`, copies the
configs, creates `monitoring.env` from the example (mode 600 / this user only), registers the service or logon start,
and **doesn't start** Alloy until the env file is complete. Add `--dry-run` / `-DryRun` to show the plan first.
**Check:** the output ends with `done.`; the env file exists.
Then fill in what you know: in `monitoring.env` set `LS_LOG_DIR=<logs_dirs[0]>` and `LS_INSTANCE=<short name>`
(ask, or propose `living-server-1`). Edit only those two lines.

## U2 [USER] Token and settings
Show USER-ACTIONS.md U2 with the env file's real path. **Check:**
```bash
tools/grafana/cloud/detect.sh --check-env ~/.local/share/livingserver-monitoring/monitoring.env
```
```
PS> tools\grafana\cloud\detect.ps1 -CheckEnv "$env:LOCALAPPDATA\LivingServerMonitoring\monitoring.env"
```
Exit 0 and `env ok: all 7 keys set`. Otherwise it names the missing keys; tell the player which.

## A3 [AGENT] Start
Run the install command again (same port). With a complete env file it (re)starts Alloy.
- Ubuntu: `systemctl --user status livingserver-alloy` shows `active (running)`.
- Windows: a hidden PowerShell runs `run-alloy.ps1`, which runs `alloy.exe`; scheduled task `LivingServerAlloy` (or a
  Startup-folder shortcut) starts it at every logon.

**Check** (after about 30 s): `http://127.0.0.1:<port>/-/healthy` says all components are healthy
(`curl -s` / `(Invoke-WebRequest -UseBasicParsing <url>).Content`).

## U3 [USER] gcx access
If `gcx.installed` is false, install it first (ask; it is Grafana's CLI from github.com/grafana/gcx):
Ubuntu `curl -fsSL https://raw.githubusercontent.com/grafana/gcx/main/scripts/install.sh | sh`;
Windows `irm https://raw.githubusercontent.com/grafana/gcx/main/scripts/install.ps1 | iex`.
Then show USER-ACTIONS.md U3. **Check:** `gcx config check`, then `gcx dashboards list` (read-only).

## A4 [AGENT] Dashboards
The three ✦ dashboards (Overview, Server Health, Bots & World) go into the "Living Server" folder:
```bash
gcx resources validate -p tools/grafana/cloud/dashboards/gcx
gcx resources push -p tools/grafana/cloud/dashboards/gcx --dry-run --on-error abort
gcx resources push -p tools/grafana/cloud/dashboards/gcx --on-error abort
```
(Windows: the same with `tools\grafana\cloud\dashboards\gcx`.) That folder holds only the three dashboards, which
land in the folder `living-server` the player made in U3. If the service account may create folders, push
`dashboards/gcx-folder` first instead of U3 step 3. **Check:** `gcx dashboards get <uid>` for each of
`living-server-overview-showcase`, `living-server-health-showcase` and `living-server-bots-world-showcase`, then give
the player `https://<name>.grafana.net/d/<uid>`. Without gcx, the player can import `dashboards/<uid>.json` in
Grafana (Dashboards > New > Import) into the Living Server folder.

## U4 [USER] Start the server
Show USER-ACTIONS.md U4. Game data only exists while the server runs.

## A5 [AGENT] Verify
About 2 minutes after the server starts:
1. `http://127.0.0.1:<port>/metrics`: `prometheus_remote_storage_samples_total` rising,
   `prometheus_remote_storage_samples_failed_total` 0, `loki_write_sent_entries_total` rising.
2. With gcx, metrics: `loki_process_custom_lines_total{instance="<LS_INSTANCE>"}` has data; while the server runs,
   `loki_process_custom_bots_online{instance="<LS_INSTANCE>"}` too (Ubuntu also `namedprocess_namegroup_num_procs`).
3. With gcx, logs: `{job="livingserver", instance="<LS_INSTANCE>"}` has lines from the last 10 minutes; spot-check
   that names show as `~`.
4. Report each check as **live** or **not run**, and the dashboard links.

---

## Troubleshooting
| Symptom | Likely cause | What to do |
|---|---|---|
| detect finds no logs folder | non-standard install path | ask for it; rerun with `--logs` / `-Logs` |
| `SHA256 mismatch ... refusing it` | broken or tampered download | the file is deleted; rerun once; if it repeats, stop and tell the player |
| `not started: fill in ...` | env file incomplete | U2; `--check-env` names the missing keys |
| `systemctl --user` fails (Ubuntu) | no user session (e.g. over plain SSH) | run from a desktop login, or the player enables lingering: `loginctl enable-linger "$USER"` |
| `scheduled task refused` (Windows) | policy blocks per-user tasks | handled: the install uses a Startup-folder shortcut instead |
| `/-/healthy` doesn't answer | Alloy not running or another port | Ubuntu `journalctl --user -u livingserver-alloy -n 50`; Windows `%LOCALAPPDATA%\LivingServerMonitoring\logs\alloy.log` |
| port in use | another program, or an older hand-run copy of this pipeline (#658) | rerun install with `--port <free_port>`; stop the old copy only with a yes |
| `samples_failed_total` rising, HTTP 401/403 in the log | wrong user ID or token, or missing write scope | recheck U2 (each URL with its own user ID; scopes metrics:write and logs:write) |
| gcx push 403 "access denied to folder" | service account can't create or write the folder | U3 step 3 (create the folder, give the service account Edit) |
| tiles say "No data: collector off" | collector not sending, or the dashboards' `job`/`instance` doesn't match | A5 step 1; set the dashboard's Job to `livingserver` and Server to `<LS_INSTANCE>` |
| tiles say "Server stopped" | the server is off | normal; start it (U4) |
| Windows: process tiles empty while running | known gap (#656) | logs and log-derived tiles still work |
| host panels empty | no node_exporter on the PC (#657) | known gap |

## Undo
- Ubuntu: `tools/grafana/cloud/uninstall.sh` (add `--purge` to delete `monitoring.env` too).
- Windows: `PS> tools\grafana\cloud\uninstall.ps1` (add `-Purge`).
- Then show the player USER-ACTIONS.md "Removing it" (revoke the token and the service account).
