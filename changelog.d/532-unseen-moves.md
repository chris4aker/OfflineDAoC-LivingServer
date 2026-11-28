---
version: 1.2.3
section: RvR
---
- **War bots no longer blink in front of you** (#532, step 1a). In the frontiers, battlegrounds and neutral RvR
  dungeons, an autonomous gamebot's non-urgent move within its region waits while a player is within sight of where it
  stands or where it would land. Those moves are a keep route restart at the arrival point, a short hop out of a navmesh
  pocket, a floor step and a listed-pocket nudge. A waiting pocket escape never turns into the move to the capital.
  New row `RVR_TELEPORT_HELD`.
