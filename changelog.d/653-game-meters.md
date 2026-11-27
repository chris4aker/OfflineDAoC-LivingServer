---
version: 1.2.3
section: Tools
---
- **Bots-online meter and the metrics interval fix** (#653; owner, 2026-11-26). With `MetricsEnabled` on in
  serverconfig.xml (off by default: diagnostics), the server pushes a new OTLP gauge `daoc.online.bot.count`, by `type`
  (`gamebot` or `companion`) and `realm`, numbers only; real players keep their own meters. The metrics interval is
  read from the shipped `MetricsInterval` key as well as `MetricsExportInterval` (it always fell back to 60 s before),
  and an unreadable value gives 60 s instead of stopping the server's start. See docs/GRAFANA.md.
