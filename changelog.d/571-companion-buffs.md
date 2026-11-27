---
version: 1.2.3
section: Bots and companions
---
- **Companions buff like a veteran buffer** (#571 step 4, from True165 `1ed3b24`). This covers hired, `/spawn` and `/bot`
  companions and any bot in your group.
  - **Weapon buffs go to weapon users.** Haste, base strength, endurance regen, damage adds and weapon procs no longer go
    to casters or healers, so the concentration goes to the fighters who use them. The Valewalker and the Vampiir count
    as fighters.
  - **The right buffs first when concentration runs short.** Endurance and power regen come first, then the spec buffs,
    then base constitution and dexterity. Acuity and haste come next, then base strength, and base armor last.
    Spec buffs first and weapon buffs only for weapon users follow the 2003 buffbots. Regen first and base armor last
    come from True 1.65's companion design; the 2003 buffbots put base armor before the base stats and regen last.
  - **Buffed by job.** Tanks first, then the other fighters, healers and casters. Base dexterity (it speeds casting) and
    power regen go to healers and casters first.
  - **New AI option, "Buff priority"** (`/hire options`), for hired buffers: Automatic (by job), Front line first, or
    Casters and healers first.
  - A buffer no longer leaves a base buff to a groupmate who is out of range, or, in your group, one who is in a fight.
    This range fix applies to every bot.
