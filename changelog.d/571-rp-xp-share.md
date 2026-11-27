---
version: 1.2.3
section: Bots and companions
---
- **Your hired companions and the gamebots in your group share the kill with you** (#571 step 2; idea from
  OfflineDAoC-True165 `74334692` and `de83551a`). When your group kills an enemy player or gamebot, a hired companion or
  a befriended gamebot in your group now counts as a groupmate for the realm points, experience and bounty points, the
  way a group of players does: its damage is the group's damage, and the group's value is split between you. Before, a
  player who healed while his companion landed every blow got nothing, and the companion kept the whole kill. In a
  battleground, a low-level kill split this way no longer rounds your share down to 0 realm points. `/spawn` helpers and
  `/bot` companions work as before (their damage already counts as yours), groups of players or of gamebots alone are
  unchanged, and the repeat-kill window still applies.
