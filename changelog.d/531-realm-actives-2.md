---
version: 1.2.3
section: RvR
---
- **Gamebot Healers and Infiltrators use Perfect Recovery and Vanish in open-field fights** (#531 step 2c-2). This covers
  autonomous gamebots in the frontier and the battlegrounds. Companions, bots in a player's group, keep fights and raids
  are unchanged.
  - A Midgard Healer whose group mate falls during a fight uses **Perfect Recovery**: an instant rez at full health and
    power. It raises a fallen healer first, then a caster, then the nearest mate within 1,500, and never one that
    someone is already raising.
  - An Albion Infiltrator that runs from a fight it is losing to an enemy realm uses **Vanish**. It hides at once, even
    beside its attackers, and nobody sees it for 30 seconds. Hits don't uncover it, and attacking again ends it.
  - Each works as it does for a player and then waits out its 30-minute timer, so it comes once a fight. The bots buy
    them in their realm ability builds after Purge: Perfect Recovery for the Healer, Vanish for the Infiltrator.
  - `RvrRealmActives`, `UT_RvrRealmActives531`; the RvR fight rehearsal prints `realm_abilities_escape_rez`, and
    `open_field_realm_abilities` gains `pr=` and `vanish=`. **Tests only.**
