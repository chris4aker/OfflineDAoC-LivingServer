---
version: 1.2.3
section: Bots and companions
---
- **Hired companions learn from PvP kills** (#571; True165 `de83551a`, owner 2026-11-27: "yes, as True165 de83551a
  does; under the owner's 'only add' rule the player's own share is never lowered by it"). When your group kills an enemy
  player or gamebot, each hired companion in reach earns experience by the player-kill rule: the players and hired
  companions of its group share the kill's value, by their part of the damage, capped by the companion's own level. A
  companion below a battleground's level bracket learns too, and it learns even when you did all the damage. It still
  never passes your level. The repeat-kill window applies. Your own XP, realm points, bounty points and coins are worked
  out as before and never lowered by it. Other gamebots, `/spawn` helpers and `/bot` companions are unchanged.
  `UT_HiredCompanionPvpXp571`. **Tests only.**
