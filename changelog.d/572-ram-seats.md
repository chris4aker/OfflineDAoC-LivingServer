---
version: 1.2.3
section: RvR
---
- **Gamebot armies crew their rams** (#572, port of zomboden/OfflineDAoC-True165 `433740c6f`). This covers
  autonomous gamebots attacking a keep, in the frontier and the battlegrounds. A ram hits harder and reloads faster for
  each friend standing at it, but until now gamebots stood there only by chance, so most bot rams worked alone. Now
  every ram a gamebot builds at a keep door takes a crew. The army's melee fighters with no siege job each take a seat
  at the ram with the most empty places (up to 12) and stand in rows behind it, on the side away from the door. Arrows
  and spells from the walls don't move them; an enemy hitting them in melee does. Casters, archers and healers keep
  their jobs, and melee who find every seat taken batter the gate by hand as before. A crew leaves when the door
  breaks or the ram stops working. Your own ram is unchanged: any friendly gamebot standing at it still counts as its
  crew, but gamebots aren't sent to it. The number of rams per door and siege repair are unchanged.
  `RVR_SIEGE action=crew_seat` and `action=crew_left` log each seat. `UT_RamCrews572`. **Tests only.**
