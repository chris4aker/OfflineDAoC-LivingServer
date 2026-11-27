---
version: 1.2.3
section: RvR
---
- **Gamebot groups fight only the enemies they know are there** (#529, RvR plan #506, piece B step 3). This applies to
  autonomous gamebots in the frontier, the battlegrounds and the raid dungeons. Groups you are in, companions and
  temporary helpers are unchanged.
  - A gamebot group's fight board (its called target, crowd control, size-up and retreat read it) now lists only the
    enemies one of its members noticed, was told of by a mate, or was hit by in the last 15 seconds. Before, it listed
    every enemy within 3,500 units, behind hills and walls and before anyone had seen them, so a group could call a
    target, hold or size up on an enemy nobody in it had seen.
  - A gamebot in a fight now looks around every 2 seconds: the enemies within 2,500 units, a few line-of-sight checks
    (from eye height, and a closed keep door blocks them, as in the usual scan), the usual notice rule. A new enemy
    joining the fight is seen without having to hit first. As on any sighting, it is called to the bot's mates (they
    take it as a called contact, and an idle mate wakes for it), and these looks share the per-second notice cap with
    the bots that are not fighting. Only bots whose fight board reads contacts look around: siege fighters, committed
    keep defenders, battleground assaults and raiders don't.
  - Switch `rvr_board_from_contacts` (on). Off: the board searches the area as before, and fighting bots don't look
    around. Logs: `RVR_FIGHT_BOARD` has `from=contacts|area`, `RVR_CONTACT_SHADOW` and `RVR_CONTACT_UNKNOWN` have
    `board=` (with `board=contacts` the unknown enemies are the ones the board left out), `RVR_SCAN_BUDGET` counts
    the in-combat looks (`combatRefreshes`, `combatRays`), and `RVR_NOTICE` has `look=scan|combat` (the played-log
    check's notice-time table reads `look=scan` only).
  - `RvrCombatRefresh`, `RvrContacts.Group`, `UT_RvrBoardFromContacts529`, two new cases on the RvR fight rehearsal (a
    mid-fight enemy who never hits; an enemy behind a wall). **Tests only.**
