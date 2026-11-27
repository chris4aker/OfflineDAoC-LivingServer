---
version: 1.2.3
section: RvR
---
- **Frontier warbands roam as battlegroups of two or three groups** (#599 step 1, built on #530 steps 5a and 5b).
  Outside a keep or relic event a warband used to be the biggest unit in the field, so two eights that met went their
  own ways. Now frontier warbands of one realm whose leaders stand within 3,000 of each other link into a battlegroup:
  at most three groups and 24 bots, led by the biggest group. The other groups trail the lead group instead of picking
  their own destinations, and the groups share calls the way the groups of a siege already do: incoming-enemy calls,
  retreat and regroup calls, the size-up before a fight, and single-target heals and buffs across groups. Some groups
  never link: a duo, a group in an event, and a group with a player in it. A group leaves when it joins an event, gets
  a player, changes region, strays more than 6,000 from the lead for a minute, or reaches the end of its 15-40 minute
  stay. A battlegroup ends when its lead group goes or only one group is left. New log row `RVR_FORCE_BATTLEGROUP`
  (formed, joined, left, ended). Autonomous gamebots only. Tests only.
