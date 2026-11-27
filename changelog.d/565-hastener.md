---
version: 1.2.3
section: RvR
---
- **Keep hasteners** (#565, OpenDAoC port X4 step 4a; port of OpenDAoC-Core `43924def4`; tests only). A hastener at a
  keep with no realm now wears the model its guards picked instead of always Albion's. On PvE and PvP server types the
  server rules decide who a hastener serves, so every player there can use one; on the normal RvR server a hastener
  still serves only its own realm (upstream's change as written would have let an enemy use it, since a hastener is a
  peaceful NPC). Players only. `UT_HastenerRealm`.
