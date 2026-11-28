---
version: 1.2.3
section: Bots and companions
---
- **Bots pick their weapons like players** (#569).
  - A party's only tank with Guard fights with sword and shield when it has trained its shield, and its Guard goes on
    a groupmate close enough to be guarded: the one being hit first, then the healer.
  - A bot carrying both a two-hander and a one-hander fights with the one whose line it has trained higher.
  - A hired companion keeps the melee set you readied for it in its window, instead of switching back at the next fight.
  - Bots no longer swap weapons back and forth between turns, and archers no longer flip between bow and melee weapon
    on every shot.
  - Only weapons the bot already carries are used. Switch: `bot_weapon_set_rules` (on).
