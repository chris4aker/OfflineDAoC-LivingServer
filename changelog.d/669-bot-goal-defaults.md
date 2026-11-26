---
version: 1.2.3
section: Bots and companions
---
- **Bots follow the Bot Goals shares on a fresh install** (#669; owner, 2026-11-26). Without a saved Bot Goals file
  (every new install), gamebots below level 50 used to pick solo PvE every time they finished a task, so the
  battlegrounds, RvR and groups emptied out over a night of play. They now pick their next goal from the default Bot
  Goals shares the launcher shows (levels 20-49: 30% solo, 30% group, 15% RvR, 25% battlegrounds at 20-35; levels 1-19:
  55% solo, 35% group, 10% battlegrounds at 15-19), the same as an install that saved its settings. Level 50 bots and
  saved settings are unchanged.
- **Battlegrounds keep refilling** (#670). When only one or two bots were free for a new goal at once, the goal planner
  never sent them to a battleground, so Thidranki and the other battlegrounds drained once the first tours ended. Free
  bots now take battleground goals in proportion to the open places, never more than there are bots of the right level
  or places left.
- Two new server-log rows for the morning report: `BOT_GOALS` at start-up (which Bot Goals settings are in use: the saved
  file or the defaults) and `GOAL_PASS` every five minutes (per realm and level band: how many bots were free for a goal,
  the open places and the goals given). No names are logged.
