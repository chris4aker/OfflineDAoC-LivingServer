---
version: 1.2.3
section: The server
---
- **Player and GM command fixes** (#563, ledger package X2; tests only):
  - `/bonuses`: the first resist figure is the primary layer (items, buffs, race) instead of counting the secondary
    layer twice, and the power pool and flat power bonuses are cut at the caps the server applies (`c9c597a78`,
    `0db2d9d9e`).
  - Items in the quiver can be dragged onto the ground, like backpack items (`b28922b5e`, `117370277`).
  - `/stuck` says that you will be moved to your last safe point when you exit, and a refused quit no longer leaves that
    move armed for a later ordinary quit (`74587b664`).
  - `/stats` shows realm points per hour as a whole number (`e845e011f`).
  - `/targetstats` no longer uses up a damage-variance roll of the target each time it runs, and shows a player's shield
    attack (`87ff8bafe`, `f0aaa1ea2`).
  - Names that start with "Sapph" (Sapphire, Sapphira) are allowed; "sapphic" is still refused (`a8c4f88a0`).
  - GM and admin accounts are not moved away from a keep or out of a battleground at login (`c0f4c72bb`).
  - GM `/door` on a keep or relic door no longer crashes: `info`, `heal` and `kill` work there, and the other
    subcommands, which belong to the keep, say so (`3f0f163a3`).
  - GM `/ngg` reads its item models the first time it is used instead of at every server start (`f449b8769`; the
    command stays on).
  - Admin `/fixnpcspawn`: a stricter on-mesh test, the nearer of floor and roof, a `visible` scope, larger default snap
    distances, and scripted NPCs listed but never moved (`1a3b12ba7`, `ec9cfe94d`, `9ff5d3c7e`, `29a935897`,
    `b2770fa85`). Gamebots, companion bots and pets are never touched by it.
  - `UT_UpstreamCommandPorts`.
