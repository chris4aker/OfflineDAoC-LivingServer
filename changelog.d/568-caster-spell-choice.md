---
version: 1.2.3
section: Bots and companions
---
- **Casters choose their spells like veterans** (#568; owner decision 2026-11-26; ported from OfflineDAoC-True165, with
  rules from DetShaft's and DividedRealms' Living Server forks). For gamebots and companions alike:
  - **Mezzed adds stay mezzed.** A bot whose own list holds only the mezzed add now joins the fight its group (or you)
    is in instead of breaking the mez.
  - **"Attack spells: None" keeps the shield up.** A companion caster set to buffs only puts its self bladeturn back up
    during the fight too, not only after it.
