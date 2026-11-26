---
version: 1.2.3
section: The server
---
- **Raid attempt rows, part 1** (#644; owner, 2026-11-25: "come up with and log what epic raid and encounter data and
  stats should all be logged and tracked"; decisions a and b). New server-log rows, logging only, on by default and
  without any player, account or bot name: `RAID_ATTEMPT` at each pull of a dungeon encounter, dragon or open-world
  boss (attempt id `attempt=<event>.<n>`, kind, boss, pull number, the raid's size in the region against its period
  size, Special Forces, roles and classes, players, build, and after a failed attempt the previous id and the seconds
  from it to the pull), `RAID_ATTEMPT_END` at each end beside the existing rows (outcome code, seconds, engaged and
  fallen as before, and `fallen` split into `died` and `left`, deaths, boss health) and `RAID_DEATH` for every raider
  death during a live attempt, with no cap (kind, class, role, level, killer kind and monster, the last hit's source and
  spell, the boss's active mechanic, distance, healers in range, attackers). The four existing attempt-end rows
  (`REALM_EXPEDITION_ATTEMPT_ENDED`, `_ENCOUNTER_RESET`, `_SKIRMISH_LOST`, `REALM_EXPEDITION_WIPE`) gain `attempt=` at
  their end; their other fields are unchanged. No boss, tactic or raid rule changes. Rows and fields in
  `docs/RAID-ATTEMPT-ROWS.md`. **Tests only.**
