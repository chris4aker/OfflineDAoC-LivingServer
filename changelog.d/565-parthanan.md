---
version: 1.2.3
section: Bosses
---
- **The Parthanan farms' Amalgamate Parthanan rises again** (#565, package X4 step 4b;
  tests only). The world's three farm controllers (Connacht, Lough Derg, Lough Gur) are rows of a class
  the server didn't have, so they loaded as plain NPCs and the boss never spawned. The rewritten farm runs them: 60-120
  parthanan kills start the sacrifice, the parthanans near the controller walk in and die, the Amalgamate Parthanan
  (level 26) rises immune until five have, then fights with the farm hidden; its death starts the next cycle. Each
  farm keeps its own count behind one lock (the old code shared static counters between threads). The boss keeps its
  old immunities: magic, archery and ranged attacks by NPCs, and unarmed blows do nothing; the magic-only
  rule is one switch, `AmalgamateParthananDamage.UseUpstreamRule`, for the owner. Kills by players, companions and
  gamebots all count; the boss is not a gamebot raid goal. `UT_ParthananFarm`.
