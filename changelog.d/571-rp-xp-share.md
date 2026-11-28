---
version: 1.2.3
section: Bots and companions
---
- **A player who heals while his companion does the damage now gets his share of the kill** (#571 step 2).
  When your group kills an enemy player or gamebot, a hired companion or a
  befriended gamebot in your group can now raise your share of the realm points, experience, bounty points and coins, the
  way a groupmate's damage does in a group of players. It never lowers it: your reward is worked out as before, and the
  group share with your companions counted is used only when it pays you more. Who gains: a player who dealt little or no
  damage while his companions did most of it (a healer beside his hired companion used to get nothing). In a
  battleground, such a share of a low-level kill no longer rounds down to 0 realm points. Nothing is ever reduced: a
  player who does the damage keeps his whole reward however many bots are in his group, your companions' own realm
  points are paid as before and never come out of yours, and `/spawn` helpers, `/bot` companions, groups of players only
  and groups of gamebots only work as before.
