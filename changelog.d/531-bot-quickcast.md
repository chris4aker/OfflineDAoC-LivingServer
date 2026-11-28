---
version: 1.2.3
section: RvR
---
- **Gamebot casters use Quickcast for one key spell under attack** (#531 step 2d). This covers autonomous gamebots in
  open-field fights in the frontier and the battlegrounds. When an enemy's hits would cost a gamebot its mez or its big
  heal, it uses Quickcast as a player would: the spell takes two seconds and can't be interrupted, it costs double
  power, and Quickcast then waits 30 seconds. Quickcast is never used in melee, and never for other spells.
  Companions, bots in a player's group, keep fights and raids still never quickcast. Log: `BOT_REALM_ABILITY_USED ability=QuickCast`.
  `BotQuickCast`, `UT_BotQuickCast531`; the RvR fight rehearsal's `open_field_realm_abilities` row gains `qc=`.
  **Tests only.**
