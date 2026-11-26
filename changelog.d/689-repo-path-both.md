---
version: 1.2.3
section: Setup and launcher
---
- **The launcher and Setup keep finding updates if the project's GitHub address changes** (#689). They now take an
  update from the project's current GitHub address and from its planned new one, `chris4aker/DAoC-LivingServer`,
  whatever the capitals, and still from nowhere else. So when the project moves, players on this version get the next
  update as usual, with no new download. Players on 1.2.2 and earlier need this update first. The project stays at its
  current address until then. **Tests only.**
