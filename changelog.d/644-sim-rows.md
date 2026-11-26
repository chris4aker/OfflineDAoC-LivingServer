---
version: 1.2.3
section: Tools
---
- **The raid simulations write the server's raid rows** (#644 part 6; tests and tools only, no boss, tactic, raid rule or
  size change). The Summoner Roesia and Green Knight fight models (`UT_RaidSim121SummonersGreenKnight`,
  `UT_RaidSim122GreenKnight`) can write each simulated pull as `RAID_ATTEMPT`, `RAID_DEATH`, `RAID_FIGHT_SAMPLE`,
  `RAID_MECHANIC_FIRE`, `RAID_THREAT`, `RAID_ROLE_TOTALS` and `RAID_ATTEMPT_END` through the server's own formatters
  (`SimRaidRows`), marked `build=sim` under the raid's real event id, so a simulated attempt reads with the same parser
  and compares line by line with a logged one. `UT_SimRaidRows644` pins that the rows carry the server's fields in order
  and agree with the fight, and that writing them changes nothing in it. `raid_postmortem.py --check` checks a log's raid
  rows; the server job runs it on the simulated rows (`raid-sim/*.raid.log`, uploaded as an artifact). Tests only.
