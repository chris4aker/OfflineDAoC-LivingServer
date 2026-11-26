# Grafana Cloud setup: agent runbook

For an AI agent (Claude Code or similar) with a shell on the player's PC, Ubuntu or Windows. Issue #649.

## Rules for the agent
- Run the steps in order. **[AGENT]** steps are yours. At a **[USER]** step, show the player the prompt text exactly
  (fill in the `<...>` parts), then wait until the step's check passes.
- **Never ask for a token or password in chat**, and never put one on a command line or in a file in the repository.
  Tokens go into `monitoring.env` (the player edits it in a text editor) or into gcx's own prompt.
- Before a download, say what (Alloy version, file, source, about 180 MB) and get a yes. The installs check the SHA256.
- Never change the game install, `serverconfig.xml`, or start or stop the server (port 10300). Never change an Alloy or
  Grafana Agent that is already there unless the player says yes to that change (issue #650 covers migrations).
- At the end, report which checks ran live against Grafana Cloud and which did not.

Commands below: Ubuntu first, then Windows (PowerShell; `PS>` means
`powershell -NoProfile -ExecutionPolicy Bypass -File`). Run them from the repository root.

---

## Step A0 [AGENT] Detect
```bash
tools/grafana/cloud/detect.sh
```
```
PS> tools\grafana\cloud\detect.ps1
```
Show the player a short summary (logs folder, Alloy or Grafana Agent found, gcx, free port). If `logs_dirs` is empty,
ask: "Where is your Living Server installed? I need the folder that contains `runtime\server\logs`." and rerun with
`--logs <folder>` / `-Logs <folder>`. If there are several, ask which one.

Ask once: **"Do you already have a Grafana Cloud account (a stack at `<name>.grafana.net`)?"** Rerun with
`--has-account yes|no` / `-HasAccount yes|no`. The `path` field is then:

| | No Alloy | Alloy or Grafana Agent already installed |
|---|---|---|
| **No Grafana Cloud account** | A | B |
| **Has a Grafana Cloud account** | C | D |

- A, B: do U1 (new account). C, D: do A1 (existing account).
- B, D: tell the player: "You already have <Alloy / Grafana Agent>. I'll leave it exactly as it is and run a separate,
  user-level Alloy just for the game, on port <free_port>." Get a yes.
- `next` says where to continue: `user_credentials` (U2), `install` (A2), `verify` (A4).

## Step U1 [USER] Create a Grafana Cloud account (paths A, B)
> I need you to create a free Grafana Cloud account. It takes about 3 minutes:
> 1. Open https://grafana.com/auth/sign-up/create-user and sign up. The free tier is enough.
> 2. Confirm your email address.
> 3. Pick a stack name (your Grafana will be at `https://<name>.grafana.net`) and a region close to you.
> 4. Tell me the stack name. Don't send me any password or token.

Check [AGENT]: `https://<name>.grafana.net/api/health` answers with `"database": "ok"`.

## Step A1 [AGENT] Existing account (paths C, D)
Ask for the stack name only, then check `/api/health` as above. If gcx is already logged in (`gcx.logged_in`), run
`gcx dashboards list` and tell the player if a "Living Server" folder or dashboards already exist; ask before reusing them.

## Step A2 [AGENT] Install this kit's Alloy
Say what will be downloaded, get a yes, then:
```bash
tools/grafana/cloud/install.sh --port <free_port>
```
```
PS> tools\grafana\cloud\install.ps1 -Port <free_port>
```
It creates `monitoring.env` (readable by the player only) and does not start Alloy until the file is complete.

## Step U2 [USER] Write token and stack details
> Now two things from your Grafana Cloud account go into a settings file on this PC. Please don't paste them here.
> 1. Open https://grafana.com/orgs/<org>/access-policies (My Account > Security > Access Policies) > **Create access policy**.
>    Name: `livingserver-alloy`. Realm: your stack `<name>`. Scopes: **metrics: Write** and **logs: Write**, nothing else. Create.
> 2. On that policy, **Add token**, name it `alloy-<this PC>`, pick an expiry, Create, and copy the token (it is shown once).
> 3. Open `<env file>` in a text editor (Ubuntu: `gedit <env file>` or `nano <env file>`; Windows: `notepad <env file>`),
>    and paste the token after `GC_TOKEN=`.
> 4. On https://grafana.com, open your stack's page (**Details**):
>    - Prometheus: copy "Remote Write Endpoint" after `GC_PROM_URL=` and "Username / Instance ID" after `GC_PROM_USER=`;
>    - Loki: copy "URL" after `GC_LOKI_URL=` and "User" after `GC_LOKI_USER=`.
> 5. Save the file and tell me "done".

The agent fills in, or proposes, `LS_LOG_DIR` (from detect) and `LS_INSTANCE` (a short name for this PC, for example
`living-server-1`) itself; it may edit those two lines, but never reads the token lines aloud.

Check [AGENT]: `detect.sh --check-env <env file>` / `detect.ps1 -CheckEnv <env file>` exits 0. It never prints values.

## Step A3 [AGENT] Start
Run the install again (A2). With a complete env file it starts Alloy:
- Ubuntu: `systemctl --user status livingserver-alloy`. It runs while the player is logged in; to keep it running after
  logout the player may run `loginctl enable-linger` (optional; ask).
- Windows: a scheduled task `LivingServerAlloy` (or a Startup-folder shortcut) starts it at logon; it is running now.

## Step A4 [AGENT] Verify
About 2 minutes after the start (the server must be running for game data; ask the player whether it is, never start it):
1. Alloy's own page, `http://127.0.0.1:<port>/-/healthy`, says all components are healthy.
2. `http://127.0.0.1:<port>/metrics`: `prometheus_remote_storage_samples_total` is rising and
   `prometheus_remote_storage_samples_failed_total` is 0; `loki_write_sent_entries_total` is rising (Ubuntu:
   `curl -s`; Windows: `(Invoke-WebRequest -UseBasicParsing <url>).Content`).
3. With gcx (step U3): `loki_process_custom_lines_total{instance="<LS_INSTANCE>"}` returns data, and while the server runs
   so do `loki_process_custom_bots_online{instance="<LS_INSTANCE>"}` and the process metrics.
Report each check as live or not run.

## Step U3 [USER] Let the agent manage dashboards (gcx)
Only needed for dashboards and alerts (issue #652). Install gcx first [AGENT, with a yes]:
Ubuntu `curl -fsSL https://raw.githubusercontent.com/grafana/gcx/main/scripts/install.sh | sh`,
Windows `irm https://raw.githubusercontent.com/grafana/gcx/main/scripts/install.ps1 | iex`.
> 1. Open `https://<name>.grafana.net` > Administration > Users and access > Service accounts > **Add service account**.
>    Name `gcx-<this PC>`, role **Editor**, Create. Then **Add service account token**, expiry 90 days, Generate.
> 2. Dashboards > **New folder** "Living Server". Open its Permissions and give the `gcx-<this PC>` service account **Edit**.
>    (An Editor service account can't create folders itself, so this one is yours.)
> 3. In a terminal, run `gcx login livingserver --server https://<name>.grafana.net`, choose "Service account token" and
>    paste the token at gcx's prompt, not here. Tell me "done".

Check [AGENT]: `gcx config check`, then read-only `gcx dashboards list`.

## Undo
- Ubuntu: `tools/grafana/cloud/uninstall.sh` (add `--purge` to delete `monitoring.env` too).
- Windows: `PS> tools\grafana\cloud\uninstall.ps1` (add `-Purge`).
- [USER] In Grafana Cloud: delete the `livingserver-alloy` access policy token and the `gcx-<this PC>` service account if
  they are no longer used.
