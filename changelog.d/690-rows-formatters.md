---
version: 1.2.3
section: Tools
---
- **Grafana kit: the row scanner reads the LivingRows formatters** (#690). `tools/grafana/rows.py` now finds
  `BG_POPULATION`, `CAST_INTERRUPT`, `BG_TARGET_PICK` and `GOAL_PASS` in the code, with their real fields, instead of
  hand-copied pending entries (`GOAL_PASS` wasn't found at all). Two rows that shared a const name with another row in the
  same file had been given that row's fields: `CC_BREAK` carried `CAST_INTERRUPT`'s and `BG_RALLY_RESPONSE` carried
  `BG_TARGET_PICK`'s; each now lists only its own. A const marker now resolves to the class that encloses its use, a
  `StringBuilder(Marker, capacity)` is read, and a builder's fields end at its `ToString()`. `kit.py --check` now fails,
  instead of only noting it, when `pending-rows.json` still lists a row that is in code.
