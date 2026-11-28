---
version: 1.2.3
section: The server
---
- **Daily, weekly and monthly quests reset on time, even when the server was off at midnight** (#565, package X4
  step 2; tests only). One `RolloverSchedulerService` replaces the three quest services: hourly, daily (midnight),
  weekly (Monday) and monthly (the 1st) rollovers by the server's clock, saved in the new `ScheduledRollovers` table
  (made at start, seeded once from the old `TaskRefreshIntervals` rows), with a rollover missed while the server was off
  run once at start. The old weekly check (`DayOfYear + 7`) reset 8 days after the last
  reset, not on Monday. A player who was offline at the rollover gets the quests back at login, in
  place of the old unindexed delete over every character's quests; a zone change doesn't count as a login (otherwise the
  finished dailies would reset again at every zone change after a midnight, so they could be repeated on demand). A callback that fails is logged and the others
  still run. Anything timed can subscribe (the house rent service of #562 is meant to). `ScheduledRollovers` is run
  state: the release world ships it empty (`progress-policy.json` ClearTables). Players only. `UT_RolloverSchedule`.
