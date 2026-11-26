---
version: 1.2.3
section: Bots and companions
---
- **Refuse a buff from bots** (#661; reported by FarmVibes; owner, 2026-11-26). `/buff refuse ds` stops buffbots,
  companions, No Lifer buffbots and gamebots casting a damage shield on you and your pet, so a damage shield no longer
  breaks the root or mez you just put on a monster. Any short name `/buff help` lists works (`ds`, `bt`, `da`, `dproc`,
  `has`, ...), several at once with commas. `/buff refuse` on its own lists what you refuse; `/buff allow ds` (or
  `/buff allow all`) takes it again. Right-clicking a bot's buff off counts as refusing it, until you allow it again,
  and you're told once. Refusals are saved with your character. Nothing is refused unless you choose it: damage shield
  stays on by default, and buffs from other players are never affected.
