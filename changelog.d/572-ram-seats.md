---
version: 1.2.3
section: RvR
---
- **Gamebot armies crew their rams** (#572). This covers autonomous gamebots attacking a keep, in the frontier and the
  battlegrounds. A ram hits harder and reloads faster for each friend standing at it, but until now gamebots stood there
  only by chance, so most bot rams worked alone. Now every ram a gamebot works at a closed keep door takes a crew. The
  army's melee fighters with no siege job each take a seat at the ram with the most empty places (up to 12) and stand in
  rows behind it, on the side away from the door. Arrows and spells from the walls don't move them, in the frontier or a
  battleground; an enemy hitting them in melee does, and the army defends its crew first, as it does its siege
  operators. The crew takes its seats only once the army goes in: while a battleground wave gathers or a frontier
  siege rally holds, the melee stay with the army and the ram's operator works alone. Casters, archers and healers keep their jobs, and melee who find every seat taken batter the gate by hand
  as before. A crew leaves when the door breaks or opens or the ram stops working, and doesn't come back to a broken
  door. Your own ram is unchanged: any friendly gamebot standing at it still counts as its crew, but gamebots aren't
  sent to it. The number of rams per door and siege repair are unchanged. `RVR_SIEGE action=crew_seat` and
  `action=crew_left` log each seat. `UT_RamCrews572`. **Tests only.**
