---
version: 1.2.3
section: Bots and companions
---
- **Casters choose their spells like veterans** (#568; owner decision 2026-11-26; ported from OfflineDAoC-True165, with
  rules from DetShaft's and DividedRealms' Living Server forks). For gamebots and companions alike:
  - **A fixed rotation instead of a random pick.** A lifetap when hurt or when the pet is hit, the bolt as the opener
    (Wizards and Theurgists bolt only to open, and nobody bolts into a melee scrum), a snare nuke on a runner, the DoT on
    a fight that lasts, then the best nuke (the cheapest per point of power when low). Up to two debuffs open a fresh,
    lasting fight, and a resist debuff only for the caster's own damage type. Realm raids, warbands and boss fights keep
    their own rules: there casters open with no debuffs and don't wait for the tank.
  - **No more wasted control on the monster being killed.** No root, mez or nearsight on it, a snare only on a runner,
    a stun only to interrupt a cast or pull a monster off a healer. With the tank or pet holding the monster, the plain
    nuke goes instead of a snare nuke. Adds and raid crowd control keep their own rules; RvR keeps its control.
  - **Casters let the tank or pet take the monster first** (up to 8 seconds: the tank under 75%, the pet under 85%, a
    Spiritmaster's pet under 75%), so fewer monsters peel onto casters. Not in realm raids, warbands or boss fights.
  - **Utility spells come into play.** A Cabalist's Life Transfer saves a groupmate under 35%, Amnesia hands a monster
    back to the pet or the tank, and in RvR Confusion goes on enemy pets, Amnesia interrupts a casting enemy and
    Nearsight opens on casters and archers. A companion's "Life transfer: Off" and "Amnesia: Off" are honoured. Bosses
    are never the target: no Amnesia or Confusion on a boss, and every boss's own rules come first.
  - **RvR crowd control wastes less.** No mez or root on an enemy your group is hitting, an area mez only with two or
    more free enemies in it (then held for a minute), immune enemies skipped, and a mez that was interrupted no longer
    keeps the others off its target.
  - **Mezzed adds stay mezzed.** A bot whose own list holds only the mezzed add now joins the fight its group (or you)
    is in instead of breaking the mez.
  - **"Attack spells: None" keeps the shield up.** A companion caster set to buffs only puts its self bladeturn back up
    during the fight too, not only after it.
