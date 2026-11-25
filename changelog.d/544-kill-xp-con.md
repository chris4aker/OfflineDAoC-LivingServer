---
version: 1.2.3
section: Tests
---
- **Realm pace diagnostics: target con, group arrival, zone names, level bands** (#544; from FarmVibes' second-PC
  data). No gameplay change. `kill_xp_report.py` shows the target con (the camp's target level minus the bot's level)
  per realm for solo and group kills beside XP per member, merges zone spellings (`Black Mtns. North` / `Black Mtns
  North`, `Silvermine Mts` / `Silvermine Mts.`, `Aegirs Landing` / `Aegir's Landing`), compares by 5-level band, flags
  bots whose level dropped and gives the solo and group arrival share. The server logs the camp's level
  (`campLevel=` on `AUTONOMOUS_KILL_XP`, `targetLevel` on `AUTONOMOUS_GOAL_ATTEMPT`; older logs still read), and a
  group's arrival (`AUTONOMOUS_GROUP_ARRIVED`) now stamps every member's camp attempt, which had left `arrivedUtc`
  empty on most group attempts. New `bot_level_snapshots.py`: levels gained by realm and band between database
  snapshots, with re-rolled, replaced, new and retired bots flagged and left out.
