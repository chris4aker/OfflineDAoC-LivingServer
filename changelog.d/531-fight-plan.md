---
version: 1.2.3
section: RvR
---
- **Gamebot groups open a fight on one target** (#531 step 1). In the frontier, a gamebot eight used to spread its opening over several of you and converge only when its main assist called a target a few seconds later. Now the group opens together from the start. It takes the target its assist called, or the one its mates already fight, or the 2003 kill order: casters and healers first, tanks last; versus Midgard the Shaman, versus Hibernia the Bard. It prefers people to pets and siege engines, free enemies to mezzed or rooted ones, and nearer to farther. Solo and duo gamebots open by the same kill order instead of on the nearest enemy. Groups at a keep, battleground assaults, raids, relic carriers, your own group and companions pick as before. Log: `RVR_DECIDE ... why="... pick=called|mates|order|spread|carrier"`. Setting: part of `rvr_bot_assist`.
