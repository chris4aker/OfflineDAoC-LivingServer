---
version: 1.2.3
section: Tools
---
- **Period loot and merchants: the diff, before any world change** (#575; research only, no world row changes).
  `source/tools/period_loot_diff.py` reads a world copy read-only and compares it with True 1.65's drop table, its
  Trollheim spawn, its Traveler's Inn dye merchants and its period alchemy prices, with db-public's Thidranki merchants
  and, as an audit only, with Uth-Moras's Midgard dragon and epic-zone items. Run against the released base world, it
  finds that 1,198 of True 1.65's monsters have a loot table with no item in ours (they drop only coins and random
  items), that db-public's Thidranki merchants belong to the later Thidranki map (region 238) and sell later-era gear, and
  that all 551 alchemy prices differ. The counts and sources are in
  [docs/research/PERIOD-LOOT-MERCHANTS-575.md](docs/research/PERIOD-LOOT-MERCHANTS-575.md); what to change waits on the
  owner (#575). Tests: `source/tools/tests/test_period_loot_diff.py`.
