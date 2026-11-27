---
version: 1.2.3
section: Bots and companions
---
- **Archer bots open with Critical Shot, and a Hunter's pet waits for it** (#569; port of OfflineDAoC-True165 6fb0b165,
  908fef5).
  - A Hunter, Scout or Ranger gamebot, hired companion or /bot companion opens on a monster that isn't fighting, running
    or mezzed with a Critical Shot, as players pulled, then goes on with normal shots.
  - A Hunter keeps its pet at heel while it draws that first shot (at most 7 seconds), instead of sending the pet in
    first and losing the opener.
  - A bot's Critical Shot now draws as fast as a player's with the same bow spec.
  - Your own character and pet are unchanged. Switch: `bot_critical_shot_opener` (on).
