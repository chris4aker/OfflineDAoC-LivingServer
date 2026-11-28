---
version: 1.2.3
section: RvR
---
- **Gamebot bombers walk in on a mezzed enemy group in open-field fights** (#531 step 2f). This covers autonomous
  gamebots in the frontier and the battlegrounds. Companions, bots in a player's group, keep fights and raids are
  unchanged.
  - Once a gamebot group's area mez has put two or more enemies to sleep together, its point blank casters (such as the
    Eldritch, Enchanter, Mentalist, Wizard, Runemaster and Spiritmaster) walk in, up to 1,500, on the sleeping pile.
  - A group with two or more bombers bombs the pile at once: the first to arrive holds a moment (up to 3 seconds) for
    the others.
  - A group with one bomber walks in and waits beside the pile, and bombs when the first mez there has about 2 seconds
    left, however long the mez still had to run.
  - A bomber that is hit, or that an enemy turns on, gives up the walk, and so does one whose pile wakes before it gets
    there. Without a sleeping pile, bombers keep today's
    rule: they never walk in among enemy players and never bomb anyone asleep.
  - Logged as `BOT_AREA_SPELL kind=RVR_BOMB_WALK`, `RVR_BOMB_WAIT` and `RVR_BOMB`. `RvrBombPlan`, `UT_RvrBombPlan531`; the
    RvR fight rehearsal's `open_field` row gains `bomb=`. **Tests only.**
