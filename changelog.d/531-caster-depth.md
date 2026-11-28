---
version: 1.2.3
section: RvR
---
- **Gamebot casters and healers stand at their spell range in open-field fights** (#531 step 2e). This covers
  autonomous gamebots in the frontier and the battlegrounds. Companions, bots in a player's group, keep fights and raids
  are unchanged.
  - A caster stands back from the fight by its own spell range: a nuker about 1,350 from the enemy, at most 1,350
    behind its tanks, instead of 250-700 behind them. It still keeps the spread that stops one area mez catching two.
  - A healer stands about 900 from the enemy, never farther from its tanks than its heals reach.
  - When a healer moves for its place, it takes a spot beside it that the nearest enemy caster can't see, behind a rock
    or a wall, if there is one and it still sees its tanks.
  - `RvrPositioning.DepthFor`, `CoverCandidates`, `PickCover`; `UT_RvrPositioning`; the RvR fight rehearsal's
    `open_field` row gains `depth=`. **Tests only.**
