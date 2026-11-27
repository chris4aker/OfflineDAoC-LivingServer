---
version: 1.2.3
section: Bots and companions
---
- **A gamebot party that stops pulling moves on after 11 minutes** (#569; port of OfflineDAoC-True165 0e0a397). A
  grinding party that has had no fight and gained no experience for 6 minutes now logs `AUTONOMOUS_GROUP_PULL_STALL`,
  naming what holds its pull (a member low on power, one out of formation, a missing role and so on). At 11 minutes it
  ends its task and its members pick new work, instead of standing at the camp until the 15-minute stuck check sends
  everyone home. Parties hosting a player, parties formed from a player's group and realm expeditions are never ended
  by it. Switches: `bot_party_pull_stall_log` and `bot_party_pull_stall_end` (both on).
