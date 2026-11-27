---
version: 1.2.3
section: RvR
---
- **Battleground gamebots group up at their wave's muster** (#530 step 5b, built on step 5a). Each realm's wave used to
  be a crowd of soloers: the whole group fight plan (assist, crowd control, peel) almost never ran at a battleground keep.
  Now the solo gamebots gathering for the keep fill the realm's muster parties there up to eight, and three or more left
  over form new parties, split evenly. The party goes in with the wave, the keep rally and the storm, and its fight board
  is the party's. Each member keeps its own battleground tour, and the party breaks up when the first member's tour
  ends, when fewer than two remain or when a player joins. A battleground party is one force in the event layer
  (`bg:<party id>`), written by its leader. New log row `BG_MUSTER_PARTY` (formed or joined). Companions and siege
  crews working an engine are never put into a muster party. When a wave goes is unchanged. Autonomous gamebots only. Tests only.
