---
version: 1.2.3
section: Tests
---
- **Morning summary and four long-running log rows** (#625 set B; owner, 2026-11-25: "yes build all of A and B now
  before the candidate final release for testing"). `source/tools/living_report.py` reads server.log and detail.log
  (rotated copies too) and prints one markdown summary, a section per goal from `source/tools/living_report_sections/`;
  a broken section shows as an error instead of stopping the report. New rows in server.log, on by default, no names:
  `REALM_LEVEL_SPREAD` (hourly, per realm and 5-level band: gamebots, levels gained, new and re-rolled bots; at most
  33 rows an hour), `GROUP_ENDED` (per ended gamebot group: the end's reason and cause, duration, size, who left, and
  the members' GroupChanged camp-attempt ends by their real cause; about 300-600 an hour at 3,000 bots, capped at 30 a
  minute, `bot_group_ended_sample_every`), `ECONOMY_PULSE` (hourly, per realm: Exchange listings, sales and median
  prices by slot band, and gamebot coin by source; 3 rows an hour) and `STUCK_HOTSPOT` (hourly, the ten busiest
  500-unit cells of stuck and recovery events; at most 11 rows an hour). Sections also cover kill XP, RvR by hour and
  chat counts. Diagnostic only; no gameplay change.
