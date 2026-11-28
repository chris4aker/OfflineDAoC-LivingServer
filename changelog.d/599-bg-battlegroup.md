---
version: 1.2.3
section: RvR
---
- **A realm's battleground parties at one keep fight as one battlegroup** (#599 step 4, after #530 step 5b). The parties
  that form at a battleground keep's muster used to stay apart: a healer healed only its own party, and an
  incoming-enemy call reached only the party that saw the enemy. Now every party of a realm that is mustering for, or
  attacking, the battleground's keep joins that realm's battlegroup there. This covers muster parties and groups that
  came to the battleground together. The parties share incoming-enemy calls, retreat and regroup calls, the size-up
  before a fight, and single-target heals and buffs. One lead party (the biggest) calls the target, and the other
  parties take its call in the open. A party leaves when it stops mustering or attacking there (it roams or holds the
  walls), when it breaks up, or when a player joins it. A battlegroup ends when only one party is left. The wave, the
  rally and the storm work as before, and keeps and guards are unchanged. New log row `RVR_FORCE_BG_BATTLEGROUP`
  (formed, joined, left, lead, ended). Autonomous gamebots only. Tests only.
