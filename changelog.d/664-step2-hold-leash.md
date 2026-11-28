---
version: 1.2.3
section: Bots and companions
---
- **`/hold`, and your group stays close in RvR** (#664 step 2).
  - **`/hold`**: every bot in your group (`/spawn` and `/raid` helpers, hired and `/bot` companions, gamebots grouped
    with you) only fights back what attacks it or its pet, from the first hit (a spell too). They start no fight, don't assist your attacks, don't answer
    a `/pull` and don't defend a groupmate. Healers keep healing. Use it to sneak past a fight or wait at a keep.
    `/aggressive` or `/defensive` ends it.
  - **No more chasing off**: in the frontier, the battlegrounds, Darkness Falls and the neutral battle dungeons, a bot in your group lets go of an
    enemy that runs more than 2,250 from you (1,250 on `/defensive`). It keeps fighting your own target, or an enemy
    that is hitting the group, at any distance.
  - **Steadier picks**: the group keeps its target when it steps behind a rock for a moment (up to 6 s). After a kill
    the fight carries on to the next enemy without sizing up again. Mid-fight, an enemy a groupmate has mezzed or
    rooted is taken only when it's the last one left.
  - Log: `COMPANION_RVR_ENGAGE call=drop` when the group lets its target go (`why=leash`, `sight` or `guards`).
