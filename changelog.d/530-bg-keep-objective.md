---
version: 1.2.3
section: RvR
---
- **The battleground keep is an RvR objective** (#530 step 5a, piece C). The battleground used to decide on its own
  whether a gamebot assaulted the central keep, roamed or held the walls, and wrote an assault intent with no objective
  behind it. Now the event layer decides one battleground order per turn (`DecideBattleground`), and the force's record
  names the keep (`bg-keep-<id>`) and its part there: mustering with the realm's wave (`/muster`), attacking once the
  wave goes, holding the walls (`/defender`) or roaming. `RVR_DECIDE what=force` shows the wave going in as one change.
  The gather hold that the threat scan, the combat drop and the keep's guard pick each wrote by hand is one rule
  (`MusterAllowsFight`). A bot holding at its wave or keep rally still never picks the keep's lord as a new target,
  and still fights him back when he comes at it. Merging the copies makes one small change: the guard pick also takes
  a contact the rally posted (#617). What a bot does is otherwise unchanged. Autonomous gamebots only. Tests only.
