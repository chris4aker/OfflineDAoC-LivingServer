---
version: 1.2.3
section: RvR
---
- **Each realm fields a zerg of 16-24 bots at the frontier gates** (#599 step 3, with #530 step 5d; owner 50A). Before
  this, the biggest force outside a siege was a battlegroup of a few groups that happened to meet. Now each realm forms
  one zerg, sized at a third of its free frontier gamebots online, from 16 up to 24. With the bot schedule on, it never
  drops below 16 while at least 16 bots are free. With fewer free bots, no zerg forms, but battlegroups still do.
  - **Who joins:** warbands of two or more from anywhere in the realm's frontiers, not only those standing nearby, at
    most eight groups. A group in an event, a group with a player in it, and a lone bot never join. A group taken from a
    battlegroup leaves it.
  - **How it moves:** the zerg forms in the frontier where most of the realm's free warbands are. Its lead group walks
    slowly from gate to gate, never by faster stable routes. It holds each milegate and the road beside it for eight
    minutes, then moves on (in Emain, AMG and MMG and back). Groups from other frontiers travel to it.
  - **How it fights:** the zerg shares calls, the size-up, cross-group support and one target call, as a battlegroup
    does.
  - **Leaving:** a recruit that hasn't arrived after 15 minutes leaves. When the lead group is called into a siege or a
    defence, the zerg ends and its groups answer the call.
  - New log row `RVR_FORCE_ZERG` (formed, joined, left, moved, ended). Autonomous gamebots only. Tests only.
