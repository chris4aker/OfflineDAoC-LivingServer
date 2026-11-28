# Charting the log rows in Grafana

Issue #628. This page is for contributors who chart the server's structured log rows in Grafana, by hand or from
their own Claude sessions through [gcx](https://github.com/grafana/gcx). Every row the server writes, with its fields
and the fields the pipeline drops, is in [LOG-ROWS.md](LOG-ROWS.md). The kit lives in `tools/grafana/`.

**Status: tests only.** The pipeline, the dashboards and the Windows setup have run on synthetic logs:
- in a real Alloy, Loki, Grafana and gcx on Linux;
- the Windows scripts under PowerShell 7 on Linux, with Linux binaries.

None of it has run on a contributor's real playtest yet.

## What ships and what never does

AGENTS.md, "Protect player data", applies. Shipping is opt-in, it goes to your own Loki, and it is filtered:

- **Only rows.** Alloy keeps only a line whose message starts with an allowlisted row marker (`tools/grafana/alloy/config.alloy`,
  generated from the code). It drops:
  - every other line, so chat text, player names, account names, raw errors and stack traces never ship;
  - account and network rows such as `ACCOUNT_LOGIN`, `HOST_PASSWORD` and `CLIENT_HANDSHAKE`;
  - rows that are only free text;
  - the say gate's `session-chat.log`, which is never read.
- **No names inside a row.** A field is removed when the code that fills it reads a name: a player's, a gamebot's, a
  companion's, a guild's or an account's (`from=`, `bot=`, `seller=`, `target=` and so on). Free text such as
  `text=`, file paths (they can hold your user folder's name) and anything shaped like an IPv4 address are removed
  too. JSON rows get their name values blanked to `"-"`. LOG-ROWS.md lists the dropped fields per row.
  - The rules fail closed: any name the code reads is dropped unless it is reviewed world data (a keep, a zone, an
    item, a class, a spell, a monster's name). A new way of writing a name is dropped until someone reviews it.
  - A pending row whose fields aren't known yet ships only its marker and realm.
  - `tools/grafana/privacy.py` holds the rules.
  - `tools/grafana/row-policy.json` holds the reviewed exceptions, each with its reason.
- **Few labels.** The labels are:
  - `job` (`offlinedaoc`) and `log` (`server` or `detail`);
  - `row`, the marker (a few hundred values at most);
  - `realm` (`Albion`, `Midgard`, `Hibernia` or `other`);
  - `build`, from `OFFLINEDAOC_BUILD`.

  Everything else stays in the line for `| logfmt`. The file-path label is dropped, because on Windows it holds your
  user folder's name.
- **Nothing secret in the repository.** The Loki URL and the Cloud credentials come from environment variables.

Never commit or share a played log, a database, `account.txt`, a roster or a token.

## Local quickstart (Docker: Linux, macOS, Windows, Wine)

Everything runs on this PC: Grafana, Loki and Alloy are published on 127.0.0.1 only, and nothing is sent anywhere.

```bash
export OFFLINEDAOC_LOGS_DIR="/path/to/LivingServer/runtime/server/logs"
export OFFLINEDAOC_TZ="America/New_York"   # the server PC's time zone (required)
docker compose -f tools/grafana/docker-compose.yml up -d
# open http://127.0.0.1:3000 (admin / admin, or the GRAFANA_ADMIN_PASSWORD you set); stop it with:
docker compose -f tools/grafana/docker-compose.yml down        # add -v to delete the stored rows too
```

The logs folder, by platform:
- **Windows:** `C:\Games\OfflineDAoC\<your install>\runtime\server\logs`. In PowerShell, use
  `$env:OFFLINEDAOC_LOGS_DIR = '...'`.
- **Linux, native server:** `<install>/runtime/server/logs`.
- **Wine:** inside the prefix, for example `~/.wine/drive_c/Games/OfflineDAoC/<install>/runtime/server/logs`.

`OFFLINEDAOC_TZ` is required: your IANA time zone (for example `America/New_York`). The server writes local times,
and a container can't see the PC's zone, so compose refuses to start without it rather than store every row hours off.

The dashboards are file-provisioned here, so they appear at the first start, but gcx can't update them. Grafana
refuses to change a provisioned dashboard's manager. To iterate on them with gcx, comment out the two provisioning
mounts in the compose file and push instead (below).

### Without Docker

Run the three binaries with the repository's configs:
- `loki -config.file=tools/grafana/local/loki.yaml`, after changing its `0.0.0.0` to `127.0.0.1` and `/loki` to a
  folder;
- Grafana with the `tools/grafana/local/provisioning` files;
- `alloy run tools/grafana/alloy/config.alloy`, with `OFFLINEDAOC_LOGS` set to the logs folder.
  main's copy can lag the code by one merge (#817); `python3 tools/grafana/kit.py --write --generated` brings yours up to date.

The Windows setup below does all of this for you.

### Grafana Cloud instead

Point Alloy at your stack. The config reads `LOKI_URL`, `LOKI_USERNAME` and `LOKI_PASSWORD`:

```bash
OFFLINEDAOC_LOGS=/path/to/runtime/server/logs LOKI_URL=https://logs-prod-XXX.grafana.net/loki/api/v1/push \
LOKI_USERNAME=<user id> LOKI_PASSWORD=<access policy token with logs:write> alloy run tools/grafana/alloy/config.alloy
```

## Windows portable (owner PC)

`tools/grafana/windows/` holds a setup with no Docker, no installer, no admin rights, no service and no registry or
PATH change. Everything goes in **one folder**, `C:\Games\OfflineDAoC\observability\`. Uninstalling deletes that
folder. Grafana, Loki and Alloy listen on 127.0.0.1 only, and the game server's port is not touched.

From a checkout of this branch, in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File tools\grafana\windows\setup.ps1 -InstallRoot C:\Games\OfflineDAoC\LivingServer1.2.3-test
```

The setup:
1. Downloads pinned Grafana OSS 13.3.2 (the standalone zip), Loki 3.7.10, Alloy 1.22.0 and gcx 1.8.0, over HTTPS
   from their official URLs. It checks each file's SHA256 against the pin in `setup.ps1`, taken from the release's own
   checksum file, and refuses a mismatch.
2. Writes the configs:
   - Loki: local disk under `observability\data`, 14 days' retention, 127.0.0.1.
   - Alloy: the kit's filter, reading `<InstallRoot>\runtime\server\logs`.
   - Grafana: 127.0.0.1:3000, no anonymous access, no sign-up, no calls home, and the Loki data source provisioned.
     The admin password is generated into `observability\admin-password.txt`. The user is `admin`.
3. Starts the three and makes a Grafana service-account token (`gcx-local`). It runs `gcx login local` with that
   token, keeping gcx's config in `observability\gcx` (plaintext, so nothing goes to the Windows credential store),
   and pushes the dashboards with gcx. If gcx fails, it file-provisions them instead (`-DashboardMode Files` forces
   that).
4. Opens http://127.0.0.1:3000.

Then, in `C:\Games\OfflineDAoC\observability\`:

| File | Does |
|---|---|
| `start.cmd` | Starts Grafana, Loki and Alloy as background processes (logs in `logs\`, process ids in `run\`), waits until they answer, opens the browser. |
| `stop.cmd` | Stops all three. |
| `status.cmd` | Which ones run and answer. |
| `gcx.cmd` | gcx against this Grafana, e.g. `gcx.cmd resources push -p resources` or `gcx.cmd logs metrics -d offlinedaoc-loki "..." --since 1h`. |
| `uninstall.cmd` | Stops everything, then deletes the folder after you type `DELETE`. |

Running the setup again keeps the password, the downloads and the stored rows, and refreshes the configs and
dashboards. `observability.ps1 -Action gcx-login` makes a new token if gcx's is lost.

To bump a version, change its `Version`, `File`, `Url` and `Sha256` in `setup.ps1`. Take the hash from the release's
checksum file, never from a download. Also change the image tag in `docker-compose.yml`:
`tools/grafana/tests/test_windows.py` fails when the two disagree.

## Dashboards as gcx resources

`tools/grafana/resources/` holds one folder ("Offline DAoC Living Server") and nine dashboards:
- battlegrounds, combat and chat;
- levelling, groups and economy;
- movement, RvR and server health.

They are in the format `gcx resources` reads: `folder.grafana.app/v1beta1` and `dashboard.grafana.app/v1beta1`
manifests, the dashboard placed in the folder by its `grafana.app/folder` annotation. Each one asks for its Loki data
source (`$datasource`). Every panel is a LogQL metric query over the rows.

```bash
gcx resources validate -p tools/grafana/resources   # a server-side dry run: needs a live Grafana
gcx resources push -p tools/grafana/resources
```

Don't edit the JSON by hand: change `tools/grafana/dashboards.py` and run `python3 tools/grafana/kit.py --write`.
`--check`, which runs in CI and in `tools/check.sh`, fails when:
- a panel names a row that is neither in code nor in `tools/grafana/pending-rows.json`;
- a panel reads a field the row doesn't have, or one the filter drops.

## gcx for your Claude session

1. Install gcx:
   - Linux or macOS: `curl -fsSL https://raw.githubusercontent.com/grafana/gcx/main/scripts/install.sh | sh`
   - Windows: `irm https://raw.githubusercontent.com/grafana/gcx/main/scripts/install.ps1 | iex`
   - or `go install github.com/grafana/gcx/cmd/gcx@v1.8.0`
2. Log in:
   - local: `gcx login local --server http://127.0.0.1:3000 --token <service account token>`. The token is made in
     Grafana under Administration, Service accounts.
   - Cloud: `gcx login --cloud --oauth`.
3. The Claude Code plugin: `/plugin marketplace add grafana/gcx`, then `/plugin install gcx@gcx-marketplace`.

Example queries:

```bash
# Player lines answered within 30 s, by realm (PLAYER_LINE_OUTCOME, #625)
gcx logs metrics 'sum by (realm) (count_over_time({row="PLAYER_LINE_OUTCOME"} | logfmt | answered="1" [1h]))' --since 24h
# Median kill XP per member, by realm
gcx logs metrics 'quantile_over_time(0.5, {row="AUTONOMOUS_KILL_XP"} | logfmt | unwrap xpPerMember | __error__="" [1h]) by (realm)' --since 24h
# Worst game-loop tick
gcx logs metrics 'max(max_over_time({row="SERVER_STALL"} | logfmt | unwrap worstTickMs | __error__="" [5m]))' --since 6h
```

Add `-d offlinedaoc-loki` when your context has no default Loki data source. The rows write booleans as `1` and `0`,
so the filter is `answered="1"`, not `"true"`.

## When a row changes

A pull request that adds, renames or changes a row doesn't commit LOG-ROWS.md or the allowlist
(`tools/grafana/alloy/config.alloy`): main regenerates both after each merge (#817, the `generated-files` workflow and
its `bot/generated-files` pull request), and `generated_guard.py` fails a pull request that commits them. If a
dashboard reads the row, run `python3 tools/grafana/kit.py --write` and commit the dashboards; `tools/check.sh` fails
until you do. `--write --generated` also writes the two files locally, to read them or to run Alloy on your branch:
restore them before you commit (`git checkout origin/main -- docs/LOG-ROWS.md tools/grafana/alloy/config.alloy`).
- If the new row writes a name in a way the rules miss, add it to `extra_drop` in `row-policy.json`, with the reason.
- If a field is wrongly dropped, add it to `keep`, with the reason.
- Once a pending row is on main, remove its `pending-rows.json` entry; `--check` prints a note until you do.

The tests (`python3 -m unittest discover -s tools/grafana/tests`) replay the shipped config on synthetic lines.
- `ALLOY_BIN=/path/to/alloy` also runs it in a real Alloy and compares the two.
- With `pwsh` on PATH, the tests also parse the Windows scripts and run `setup.ps1 -ConfigOnly`.

[CONTRIBUTING.md](CONTRIBUTING.md) has the rest of the workflow.
