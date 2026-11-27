---
version: 1.2.3
section: Bots and companions
---
- **Your group fights your target** (#571 step 1; port of DividedRealms `ad8e087c`, BotBrain.LeaderFocus.cs). When you
  attack something, cast harmful magic at it or send your pet in, the damage dealers in your group take that target before
  whoever is hitting them or whatever they were already fighting, the way a period group follows its main assist. That
  covers your `/spawn` and `/raid` helpers, hired companions, `/bot` companions and the gamebots grouped with you, in
  PvE and RvR alike. Before, a companion turned on anything that hit it, so your group's damage spread over a pack or an
  enemy group. Tanks keep guarding you and peeling, healers keep healing, a target a groupmate has mesmerized or rooted is
  left alone, and `/defensive` keeps its short range. Gamebots outside your group are unchanged. One server setting,
  `bot_leader_focus` (on), turns it off. Log: `COMPANION_LEADER_FOCUS`.
