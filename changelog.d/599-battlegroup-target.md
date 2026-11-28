---
version: 1.2.3
section: RvR
---
- **A battlegroup's groups fight one target** (#599 step 2, after step 1). A follower group's main assist now takes the
  lead group's called target whenever that enemy is in its own reach: within the call radius of the group, reachable,
  not on a wall and not mezzed. That call comes before the group's own call and its own switch to a rising enemy. The
  follower's first pick also opens on the lead's call. When the lead's target is out of reach, the group calls its own
  as before. The members still follow the call after their own delay, and some still tunnel. Log: `RVR_ASSIST_CALL`
  gains `from=group|battlegroup`, and `RVR_DECIDE` shows `pick=battlegroup`. Autonomous gamebots only; companions keep
  their own pick. Tests only.
