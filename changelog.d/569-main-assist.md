---
version: 1.2.3
section: Bots and companions
---
- **Gamebot parties fight one kill target** (#569; port of OfflineDAoC-True165 897ddfa and f745061).
  - In a party of gamebots doing group PvE, the tank's target is the party's kill target. The other members hit it
    before whoever is hitting them, their pet's fight or their own top threat, so a pull dies one monster at a time
    instead of the damage spreading over the whole pack.
  - A member below half health that is being hit fights back. A monster a groupmate has mesmerized or rooted is never
    the kill target. A second tank on an add, or a tank taking an add off the healer, doesn't pull the party off the
    main monster.
  - Groups with you in them keep following your target (`bot_leader_focus`). RvR warbands and companions are unchanged.
  - Switch: `bot_party_main_assist` (on). Log: `AUTONOMOUS_PARTY_KILL_TARGET`.
