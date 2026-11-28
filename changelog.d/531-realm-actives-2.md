---
version: 1.2.3
section: RvR
---
- **Gamebot Healers, Infiltrators and archers use Perfect Recovery, Vanish and True Sight in open-field fights** (#531
  step 2c-2). This covers autonomous gamebots in the frontier and the battlegrounds. Companions, bots in a player's
  group, keep fights and raids are unchanged.
  - A Midgard Healer whose group mate falls during a fight uses **Perfect Recovery**: an instant rez at full health and
    power. It raises a fallen healer first, then a caster, then the nearest mate within 1,500, and never one that
    someone is already raising.
  - An Albion Infiltrator that runs from a fight it is losing to an enemy realm uses **Vanish**. It hides at once, even
    beside its attackers, and nobody sees it for 30 seconds. Hits don't uncover it; it ends when the Infiltrator unhides,
    as it does before it attacks again.
  - A Scout, Hunter or Ranger whose group an enemy assassin strikes uses **True Sight**. For 60 seconds it sees every
    hidden enemy in its line of sight, at any range, as a player on True Sight does. Vanish still hides from it.
  - A hidden gamebot is uncovered by the player's rule: a hit uncovers it, and a miss doesn't. Companions are unchanged.
  - Each works as it does for a player and then waits out its 30-minute timer, so it comes once a fight. The bots buy
    them in their realm ability builds after Purge: Perfect Recovery for the Healer, Vanish for the Infiltrator, True Sight
    for the archers.
  - `RvrRealmActives`, `UT_RvrRealmActives531`; the RvR fight rehearsal prints `realm_abilities_escape_rez`, and
    `open_field_realm_abilities` gains `pr=`, `vanish=` and `ts=`. **Tests only.**
