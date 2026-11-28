---
version: 1.2.3
section: Bots and companions
---
- **Your group spreads into an attack formation in RvR** (#665). In the frontier, the battlegrounds and Darkness Falls,
  once your group is fighting enemy bots, the casters and healers among your bots (your `/spawn` and `/raid` helpers,
  hired and `/bot` companions, gamebots grouped with you) stop bunching on you:
  - they stand behind the tanks, on the far side from the enemy;
  - they keep more than 350 apart, so one mez can't catch two healers, and spread wider while the enemy is still coming in;
  - they never stand more than 1,000 from you.
  Tanks and melee fight as before. Every step is a normal walk on solid ground. A healer moves only while nobody needs
  a heal, and nobody walks out of a cast. When the fight ends they fall back into the travel formation. One server
  setting, `bot_companion_rvr_formation` (on), turns it off. Log: `COMPANION_RVR_FORMATION`.
