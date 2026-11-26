---
version: 1.2.3
section: Tools
---
- **Raid attempt reports** (#644 part 6; reads the part-1 rows `RAID_ATTEMPT`, `RAID_ATTEMPT_END` and `RAID_DEATH`). The
  morning report (`living_report.py`) has a **Raid attempts** section: each boss's pulls and outcomes, kill share, size
  against the period size, time to kill and to fail, the boss's lowest health on a failed pull, deaths by role, kind and
  last hit, the top killers and mechanics, died against left, and the wait from a wipe to the next pull. New
  `source/tools/raid_postmortem.py <attempt> <logs>` prints one attempt's timeline: the pull, the end, every raider death
  in order, the deaths per 15 s and every other row of that attempt (`--list` lists the attempts). No raider names.
