---
version: 1.2.3
section: Tests
---
- **Grafana kit for playtest rows** (#628; owner, 2026-11-25: "Can you integrate tools for GCX and hook it all up?").
  `tools/grafana/` is for contributors who chart the structured log rows in Grafana. It holds:
  - a privacy-first Grafana Alloy pipeline: only allowlisted rows ship, and fields that could carry a player's,
    a bot's, a guild's or an account's name are removed, as are free text and file paths. The field rules fail
    closed: a name is kept only when it is reviewed world data;
  - a localhost Docker stack (Grafana, Loki, Alloy);
  - a portable Windows setup with no installer, service or admin rights;
  - nine dashboards as gcx resources.

  `docs/LOG-ROWS.md` lists every row, generated from the code. `tools/grafana/kit.py --check` (in CI and
  `tools/check.sh`) fails when it is stale or when a dashboard names a row or field that doesn't exist.
  `docs/GRAFANA.md` has the quickstart. Tools and docs only; no game change.
