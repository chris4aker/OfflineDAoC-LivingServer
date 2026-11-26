---
version: 1.2.3
section: Tests
---
- **Grafana Cloud kit, setup core** (#649, part of #647). `tools/grafana/cloud/` sends a server's sanitized
  `server.log` rows and its log-derived and process metrics to the player's own Grafana Cloud stack. It works on Ubuntu
  (native or Wine) and on Windows, with or without an existing Grafana Cloud account, Alloy or Grafana Agent. An AI agent
  runs the setup from `RUNBOOK.md`:
  - a read-only detect step;
  - an env-file check that never prints a value;
  - this kit's own user-level Alloy (a systemd user service on Ubuntu, a logon task on Windows), with no sudo or admin,
    and an existing Alloy left alone.

  The player only signs up, makes the tokens and fills in the settings file. Three dashboards (#652), Overview, Server
  Health and Bots & World, are generated and pushed with gcx. Tools and docs only; no game change.
