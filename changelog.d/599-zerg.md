---
version: 1.2.3
section: RvR
---
- **Each realm fields a zerg of 16-24 bots at the frontier gates** (#599 step 3, with #530 step 5d; owner 50A). Before
  this, the biggest force outside a siege was a battlegroup of a few groups that happened to meet. Now each realm forms
  one zerg, sized at a third of its frontier gamebots online (those at a siege included), from 16 up to 24. With the bot
  schedule on, it never drops below 16 while at least 16 bots are free. It recruits only free groups, so with too few
  free bots no zerg forms, but battlegroups still do.
  - **Who joins:** warbands of two or more from anywhere in the realm's frontiers, not only those standing nearby, at
    most eight groups. A group in an event, a group with a player in it, and a lone bot never join. A group taken from a
    battlegroup leaves it.
  - **How it moves:** the zerg forms in the frontier where most of the realm's free warbands are. Its lead group walks
    slowly from gate to gate, never by faster stable routes. It holds each milegate, and a road close beside it, for
    about fifteen minutes, then moves on (in Emain, AMG and MMG and back). A lead that can't reach its gate moves on to
    the next. Groups from other frontiers travel to it.
  - **How it fights:** the zerg shares calls, the size-up, cross-group support and one target call, as a battlegroup
    does. When its lead group decides to attack or defend a keep or relic keep in the zerg's frontier, the whole zerg
    goes. Every group signs up for the siege like any warband: it musters, marches and counts toward the siege's limits.
    The keep's defence is sized for the whole zerg, not just its lead group. A group that doesn't fit under the siege's
    limit leaves the zerg and carries on as a warband for a while.
  - **Leaving:** a recruit that hasn't arrived after 15 minutes leaves. A zerg lasts 45 to 60 minutes, then breaks up
    (after its current attack). When the lead group is called away to another siege or defence, the zerg ends. In both
    cases its groups carry on as warbands for a while (and answer the call) before a new zerg forms.
  - New log row `RVR_FORCE_ZERG` (formed, joined, left, moved, ended). Autonomous gamebots only. Tests only.
