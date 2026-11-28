---
version: 1.2.3
section: RvR
---
- **Gamebots no longer buy Avoidance of Magic** (#531 step 2b). Version 1.65 removed it, with Brilliant Aura of
  Deflection and Volcanic Pillar. Autonomous gamebots never buy any of the three. The points they spent on Avoidance of
  Magic go to the next steps of their build, from the next time they load. Players can still buy it as before, and
  hired companions keep what their player chose for them. `AutonomousBotRealmAbilities.RemovedAt165`,
  `UT_BotsDropAvoidanceOfMagic531`. **Tests only.**
