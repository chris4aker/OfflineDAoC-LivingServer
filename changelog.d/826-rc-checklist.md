---
version: 1.2.3
section: Tools
---
- **The rc.3 checklist checker** (#826; owner decision 63A): `source/tools/rc_checklist.py` reads server logs against
  the rc.3 PC checklists of #503 and #476 (kept as data in `source/tools/rc3-checklist.json`) and prints, per build,
  a summary and then PASS / FAIL / NOT SEEN / IN GAME for each issue and line, with the row counts and the first
  matching line. A test keeps every row and field it looks for tied to the server code that writes it.
