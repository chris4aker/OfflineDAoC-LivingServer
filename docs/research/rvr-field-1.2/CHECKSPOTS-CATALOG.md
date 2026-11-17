# RvR check-spot catalog (1.2)

Status: **tests only.** Every proof here is a navmesh walk. No catalog spot or circuit has been seen in an owner PC log.

File: `source/server/GameServer/bots/autonomous/data/rvr-checkspots.json`, embedded in GameServer. WP6 reads it; no
loader exists yet. The design is [RVR-FIELD-1.2.md](../../RVR-FIELD-1.2.md) section 4; the survey behind it is
[CHECKSPOTS-FINDINGS.md](CHECKSPOTS-FINDINGS.md).

## What is in it

Per frontier: the four zone rectangles, the two enemy portal-keep entries, spots and circuits. Per spot: id, type,
area label, x,y,z, a standing point, a check radius, a level band for camps and named monsters, and `via` spots for a
realm whose direct walk fails. Per circuit: walking realm, start entry, approach spots and the ordered loop. Plus
`proof_hash`. No death counts, bot names or monster levels beyond the band.

| Frontier | Camp | Chokepoint | Named | Fringe | Dungeon door | Spots | On circuits | Circuits |
|---|---|---|---|---|---|---|---|---|
| Albion (1) | 19 | 7 | 4 | 3 | 1 | 34 | 15 | 6 |
| Midgard (100) | 20 | 7 | 3 | 4 | 1 | 35 | 14 | 6 |
| Hibernia (200) | 9 | 10 | 3 | 6 | 1 | 29 | 12 | 6 |

Spots not on a circuit are reserve. Loops run 34,169 to 203,275 units.

## How it was built

- **Positions.** Spots come from the 2026-11-16 survey: spawn clusters, named spawns, ZonePoint dungeon doors,
  milegate doors and hasteners. Nine road chokepoints first picked from the kill log were moved onto static geometry:
  the nearest planner zone crossing, or the nearest node of the zone road graph (`roads.json`). The two spots that
  were only death clusters were dropped. So was one crossing about 700 units from a camp.
- **Standing points.** Search mode of `UT_RvrCheckSpotCatalog`. It tries rings around the spot and keeps the nearest
  floor point that has a local exit and a straight walkable line to the spot (a navmesh ray, not a client sight
  check). The point must stay out of aggro range of the spot's own monsters: aggro range plus roaming range plus 150.
  Other aggressive spawns count against a candidate. 93 of 98 points are clear of every aggressive spawn nearby.
- **Proof.** Validate mode walks each standing point from both enemy entries: 196 walks, 15 of them through `via`
  spots (Albion in Emain). It walks every circuit approach and all 90 legs in both directions, as `UT_TripProbe`
  does. 0 checks failed, and no leg was dropped. `proof_hash` is the SHA-256 of that report, so a changed mesh or
  catalog no longer matches.
- **New meshes.** Zones 206 and 207 (Druim Cain, Druim Ligen) and 102 were tried. No route used them. The nine Emain
  spots the Albion entry could not reach became reachable through AMG and the lane hastener. The proof loads only
  the frontier zones, so no route crosses an enemy home zone.

## Not covered

- **The world data.** It came from the static tables (Mob, Zones, Door, ZonePoint) of a played world's copy. These
  are not saved-progress tables, but they were not compared with a clean release world.
- **Distance from keeps.** Not measured on the standing-point routes. The survey estimated 4,500 units or more for
  the circuit spots' centres.
- **Aggro values.** Aggro ranges are the database values; NPC template overrides were not read.
- **Darkness Falls.** No Darkness Falls door is a spot: the frontier jump points admit only the owning realm.
- **The quiet zones.** Forest Sauvage, Snowdonia, Mount Collory and Cruachan Gorge are reserve only.

## Rebuild

1. Edit the spots and circuits in the JSON.
2. `python source/tools/rvr_checkspots.py request --db <clean release world> --catalog <json> --out req.json`
3. Run `UT_RvrCheckSpotCatalog` with `OFFLINE_DAOC_NAV_ROOT`, `OFFLINE_DAOC_PROOF_OUT` and
   `OFFLINE_DAOC_CHECKSPOT_SEARCH=req.json`. Then run `rvr_checkspots.py stands --catalog <json> --search <out>`.
4. Run the test again with `OFFLINE_DAOC_CHECKSPOTS=<json>`, then `rvr_checkspots.py seal --catalog <json> --report
   <out>`.
5. A release check runs it on the embedded file with `OFFLINE_DAOC_CHECKSPOT_STRICT=1`. It fails on any failed walk or
   a hash mismatch.

The `request` step picks each spot's own monsters by radius and level band. The first build picked them by cluster
and name. A trial rebuild on the same tables passed every check (0 failed), but it moved 12 of 98 standing points:
11 by one ring (about 400 units) and one by 2,624.
