---
version: 1.2.3
section: Tools
---
- **Raid mechanic rows count real casts only** (#644). A boss mechanic's `RAID_MECHANIC_FIRE` row now starts when the
  boss's spell really begins, not when its script asks for it, so a spell on its recast delay no longer writes a row on
  every think (Grand Summoner Govannon's demon-form DoT among them). Logging only: no boss casts anything differently.
