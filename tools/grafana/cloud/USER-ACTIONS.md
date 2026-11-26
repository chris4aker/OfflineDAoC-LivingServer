# Grafana Cloud kit: what only you can do

The setup is built so an AI agent (Claude Code or similar) does almost everything: it checks your PC, downloads and
verifies Alloy, writes the configs, starts the service, pushes the dashboards and checks that data arrives. This page
lists **every step that needs you**, in order, for Ubuntu and Windows. The agent stops at each one, shows you the
same text, and waits until its check passes. If you set things up without an agent, do these steps where
[RUNBOOK.md](RUNBOOK.md) shows them.

**Never paste a token or password into a chat with an agent.** Tokens go into a settings file you edit yourself, or
into gcx's own prompt in a terminal. The agent only checks that they are there.

## Which steps apply to you

| Your situation | Path | Your steps |
|---|---|---|
| No Grafana Cloud account, no Alloy or Grafana Agent on the PC | A | U0, U1, U2, U3, U4 |
| No Grafana Cloud account, Alloy or Grafana Agent already installed | B | U0, U1, **U-B**, U2, U3, U4 |
| Grafana Cloud account, no Alloy or Grafana Agent | C | U0, U2, U3, U4 |
| Grafana Cloud account, Alloy or Grafana Agent already installed | D | U0, **U-B**, U2, U3, U4 |

Not sure whether you have Alloy or Grafana Agent? The agent's first step finds out.
Takes about 15 minutes in all, most of it in the Grafana Cloud web pages.

---

## U0. Say yes to the plan (all paths)
The agent tells you what it found and what it will do. You answer:
- **Your server's folder**, if it couldn't find it: the folder that contains `runtime/server/logs`
  (Windows default `C:\Games\LivingServer`; Ubuntu with Wine often `~/.wine/drive_c/Games/LivingServer`).
- **Do you have a Grafana Cloud account?** A stack at `https://<name>.grafana.net`.
- **Yes to the download**: Grafana Alloy 1.22.0 from github.com/grafana/alloy, about 180 MB, checked against its
  published SHA256. It installs only for your user: no sudo on Ubuntu, no admin on Windows.

Nothing is installed before you say yes.

## U1. Create a Grafana Cloud account (paths A and B)
1. Open <https://grafana.com/auth/sign-up/create-user> and sign up. The free tier is enough for one server.
2. Confirm your email.
3. Choose a **stack name** (your Grafana becomes `https://<name>.grafana.net`) and a region close to you.
4. Tell the agent the stack name. Nothing else.

**Done when:** the agent can open `https://<name>.grafana.net/api/health`.

## U-B. Keep your existing Alloy or Grafana Agent (paths B and D)
The agent leaves it exactly as it is and runs a second, separate Alloy just for the game, on its own port. You only
confirm that. (Moving an old Grafana Agent to Alloy is a separate, optional step: issue #650.)

## U2. Make the write token and fill in the settings file (all paths)
The agent creates the settings file and tells you where it is:
- Ubuntu: `~/.local/share/livingserver-monitoring/monitoring.env`
- Windows: `%LOCALAPPDATA%\LivingServerMonitoring\monitoring.env`

1. On <https://grafana.com>, open **My Account > Security > Access Policies > Create access policy**.
   - Name: `livingserver-alloy`
   - Realm: your stack
   - Scopes: **metrics: Write** and **logs: Write**. Nothing else.
2. On the new policy: **Add token**, name it after this PC, pick an expiry (90 days is a good default),
   **Create**, and copy the token. It is shown only once.
3. Open the settings file in a text editor:
   - Ubuntu: `nano ~/.local/share/livingserver-monitoring/monitoring.env` (or `gedit`)
   - Windows: `notepad $env:LOCALAPPDATA\LivingServerMonitoring\monitoring.env`
4. Paste the token after `GC_TOKEN=`.
5. On grafana.com, open your stack's page and click **Details** on the Prometheus card, then on the Loki card. Copy:

   | From | Into |
   |---|---|
   | Prometheus "Remote Write Endpoint" | `GC_PROM_URL=` |
   | Prometheus "Username / Instance ID" | `GC_PROM_USER=` |
   | Loki "URL" | `GC_LOKI_URL=` |
   | Loki "User" | `GC_LOKI_USER=` |

6. The agent fills in `LS_LOG_DIR` (your logs folder) and `LS_INSTANCE` (a short name for this PC, such as
   `living-server-1`) or asks you to. Save the file and tell the agent "done".

**Done when:** the agent's check reports `env ok: all 7 keys set`. It never shows the values.

## U3. Let the agent manage dashboards (all paths)
1. Open `https://<name>.grafana.net` > **Administration > Users and access > Service accounts > Add service account**.
   Name it `gcx-<this PC>`, role **Editor**, **Create**.
2. **Add service account token**, expiry 90 days, **Generate token**, and copy it.
3. **Dashboards > New > New folder**, name it **Living Server**. Open the folder's **Permissions** and give the
   `gcx-<this PC>` service account **Edit**. (An Editor service account can't create folders, so this one is yours.)
4. In a terminal (Ubuntu: Terminal; Windows: PowerShell), run
   `gcx login livingserver --server https://<name>.grafana.net`, choose **Service account token**, and paste the
   token at gcx's prompt. gcx keeps it in your system keyring.
5. Tell the agent "done".

**Done when:** the agent's `gcx config check` passes and it can list your dashboards.

## U4. Start the game server and look (all paths)
The agent never starts or stops your server. Once it says the collector is running:
1. Start the Living Server as usual and play or let it run for a few minutes.
2. Tell the agent, so it can check that data arrives.
3. Open the links it gives you: **Living Server ✦ Overview**, **✦ Server Health** and **✦ Bots & World**.

"Server stopped" on a tile is normal while the server is off. "No data: collector off" means the collector isn't
sending (see Troubleshooting in [RUNBOOK.md](RUNBOOK.md)).

---

## Later, also yours
- **Tokens expire.** Make a new token (U2 step 2) and replace `GC_TOKEN=`; the agent restarts the collector.
  For gcx, make a new service account token (U3 step 2) and run `gcx login` again.
- **Removing it.** The agent runs the uninstall. Then, on grafana.com, delete the `livingserver-alloy` access policy
  and, in your stack, the `gcx-<this PC>` service account.
- **Ubuntu, keep it running after you log out** (optional): `loginctl enable-linger "$USER"`.

## What leaves your PC
Only sanitized log rows and numbers: no chat, no player, bot, guild or account names, no file paths, no IP
addresses, no account or network rows. Your saves and database never leave the PC.
