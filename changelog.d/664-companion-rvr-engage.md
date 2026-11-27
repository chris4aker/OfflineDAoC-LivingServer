---
version: 1.2.3
section: Bots and companions
---
- **Your group fights on its own in RvR** (#664 step 1). In the frontier, the battlegrounds and Darkness Falls, the bots
  in your group no longer wait for you to attack: your `/spawn` and `/raid` helpers, hired companions, `/bot` companions
  and the gamebots grouped with you spot enemy bots the way gamebots do and go for them. They need line of sight and
  notice sooner when an enemy is fighting, casting or close; when one of them sees an enemy, the whole group knows; a
  stealther is found only close up. They stay near you: within 2,000 on `/aggressive` and 1,000 on `/defensive`.
  They take the period kill order: healers first, then casters, then fighters, pets last. They never open a fight on
  an enemy you or a groupmate has mezzed or rooted, and in a fight a held enemy waits while a free one is there. Before
  they open, they size the fight up and hold off an enemy force they would clearly lose to, but they always defend you
  and each other. Keep guards and lords are left alone unless you attack them, and so is an enemy standing under its
  tower or keep guards or in a safe area. Your own target still comes first, and a bot on passive, `/bot stay` or
  `/bot hold` stays out. PvE and gamebots outside your group are unchanged. One server setting,
  `bot_companion_rvr_engage` (on), turns it off. Log: `COMPANION_RVR_ENGAGE`.
