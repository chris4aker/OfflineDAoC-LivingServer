---
version: 1.2.3
section: Bots and companions
---
- **Albion and Midgard gamebots find as many hunting camps as Hibernia's** (#544; from True 1.65's
  `AddUncoveredLiveCamps`, upstream 8d0dfdda). In a zone the CapnBry bestiary covers, a monster spawn had a camp only
  near a sighting of its name, while the source-empty zones (13 of Hibernia's outdoor zones, 7 of Albion's, none of
  Midgard's) made a camp of every live cluster. So Hibernia had about twice Albion's camps per level band, and its
  gamebots levelled about twice as fast. Now, in every realm alike, three or more outdoor spawns of one name and level
  (1,500-unit cell), or of one name at mixed levels (4,200-unit cell), with no camp of that name within 5,000 units,
  become a `live-uncovered:` camp at their middle spawn. Its targets get the same route proof as the source-empty
  camps. No monster is added, moved or changed. Player XP and the XP-by-level settings are untouched. Starter
  creatures stay with the starter camps. `AUTONOMOUS_CAMP_CATALOG_DEDUPE` adds `liveUncovered=`.
  `UT_UncoveredLiveCamps`. **Tests only.**
