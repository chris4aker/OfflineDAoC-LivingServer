# RvR lanes: survey findings (2026-11-16)

**Status: tests only.** Every lane below completed a full walk in the server's own planner and Detour on the
installed 0.37b frontier meshes (code at `origin/integration/1.2`, 6cf5ae46d). Nothing here has been seen in game.

## Answer

Between most objectives there are two more fully walkable lanes beside today's route, but **not from a portal
keep**. Each enemy portal keep sits in a closed valley whose only exit is one milegate about 200 units wide. A
catalog has to be a shared trunk (portal keep to its gate) plus a fan of lanes from the gate.

| Trip kind | Trips | 0 alternates | 1 | 2 |
|---|---|---|---|---|
| Portal keep to keep (whole trip) | 54 | 31 (57%) | 16 (30%) | 7 (13%) |
| Milegate to keep (same trips, measured from the gate) | 54 | 2 (4%) | 19 (35%) | 33 (61%) |
| Home entry to keep | 21 | 0 | 2 (10%) | 19 (90%) |
| Keep to keep (each keep and its two nearest) | 36 | 0 | 7 (19%) | 29 (81%) |
| **All** | **165** | **33 (20%)** | **44 (27%)** | **88 (53%)** |

## Method

- **Tool:** `UT_RvrLaneSurvey`, an `[Explicit]` NUnit tool (commit f690e7a85, local branch `tools/rvr-lane-survey`,
  not pushed). It walks start > via points > goal as `UT_TripProbe` does and writes every path node.
- **Main route:** the same walk with no via points.
- **Candidates:** 6,992 walks, all complete. 3,788 used the offsets asked for (3,000 to 8,000 units either side at
  1/3, 1/2 and 2/3, snapped to `roads.json` anchors). 3,204 took via points from a `roads.json` shortest path barred
  from the main route's middle.
- **Accepted** when at least 2,500 from the main route over the middle half of both, at most 35% longer, and never
  within 4,000 of any keep other than its ends, whoever holds it. A second alternate must also be 2,500 from the
  first. 2,452 walks passed; offsets alone found a lane on 81 of 165 trips.
- **Endpoints:** teleporter arrival points for portal keeps; for keeps, a mesh point 1,800 to 3,200 from the centre
  on the side facing the other end.

## Objective graph

Each frontier has 2 enemy portal keeps (Level 1), 7 keeps (Level 4 or 5) and 2 relic keeps (Level 10). `KeepType`
is 0 on every row and the table holds no towers. The home realm's entry is not in `Keep`; it is the border keeps in
`AutonomousRvrStaging`. All coordinates are in the JSON under `objectives`.

| Region | Portal keep (arrival) | Its only exit | Seams, main ground only |
|---|---|---|---|
| 1 | Hibernia 605743,293676 | Hadrian west milegate 610046,319470 | 14/15: x 600736-620704 |
| 1 | Midgard 655200,293217 | Hadrian east milegate 652200,320700 | 12/14: y 354080-365152; 11/14: x 584736-614752 |
| 100 | Hibernia 596055,581400 | Odin north gate 623500,584575 (no Door rows) | 113/115: y 611104-635104 |
| 100 | Albion 596364,631509 | Odin south milegate 622050,626560 | 111/113: y 608224-663840; 112/113: x 657184-717216 |
| 200 | Midgard 474107,295199 | Emain north milegate 448800,298622 | 212/214: x 426464-441760 |
| 200 | Albion 475835,343661 | Emain south milegate 448800,340606 | 210/212: x 387488-417824; 211/212: y 354336-385504 |

## Sample trips

"Sep" is the least and greatest distance from the main route. All 220 alternates are in the JSON.

| Trip | Main | Alternate 1 | Alternate 2 |
|---|---|---|---|
| Midgard PK > Caer Erasleigh | 93,964 | none: the gate is 30% of the way | |
| Hadrian east milegate > Caer Erasleigh | 62,612 | 67,562, sep 2,772-4,692, via 641178,325888 > 633498,333875 > 626125,340019 > 616602,348314 > 618547,358554 | 75,634, sep 7,117-8,206, via 639027,323738 > 631040,329882 > 620595,340019 > 612608,348314 > 614554,358554 |
| Hibernia PK > Caer Renaris | 158,422 | 157,499, sep 3,399-20,212, via 614451,327731 > 617011,364698 > 604416,399411 | none |
| Castle Sauvage > Caer Erasleigh | 123,911 | 119,462, sep 4,056-10,047, via 585984,438528 > 600115,403712 | 120,507, sep 5,642-13,714, 5 via points |
| Caer Erasleigh > Caer Sursbrooke | 36,933 | 40,615, sep 2,782-4,611, via 612403,364698 > 602266,362547 > 592128,362547 | 41,454, sep 3,712-5,881, via 610560,372992 > 600115,372992 > 592128,370842 |
| Odin south milegate > Nottmoor Faste | 54,878 | 57,339, sep 3,111-4,487, via 639130,622746 > 657664,622643 | 57,345, sep 3,072-5,192, 8 via points |
| Albion PK > Fensalir Faste | 159,731 | 157,693, sep 2,847-11,070, via 632986,622746 > 670259,630938 > 704666,641075 | 162,109, sep 7,043-19,900, via 630835,618752 > 669952,620800 > 704666,634931 |
| Emain south milegate > Dun nGed | 87,615 | 80,976, sep 3,452-8,947, via 430336,358554 > 413747,380979 | 83,341, sep 9,927-14,152, via 440474,356403 > 426035,372992 > 413747,389274 |
| Emain north milegate > Dun Dagda | 187,961 | none found | |

Fifty-eight alternates are shorter than today's main route, the best by 8%.

## Chokepoints

- **Milegates.** A walk from one portal keep to the other in the same frontier passes through both gates:
  104,455 units in Hadrian's Wall, 103,849 in Odin's Gate and 129,905 in Emain Macha, each for about 50,000 in a
  straight line. All 164 to 277 walks from each portal keep crossed the wall inside one window about 200 units
  wide. Portal-keep trips got alternates only where the gate lies in the first 22% of the trip.
- **Seams.** Today's planner sends at least half of its crossings through the two ends of a seam's main ground: 18
  of 35 on Hadrian/Pennine (x 600800 and 620640), 21 of 35 on Odin/Jamtland, 21 of 35 on Emain/Breifine. The
  alternates cross at 26 to 32 different points. The narrowest seam, Snowdonia/Pennine, is 11,072 wide.

## Where today's deaths are

The log holds 5,237 RvR-duty deaths in the three frontiers between 11:45 and 16:39.

| Region | Deaths | Within 1,500 of a portal-keep main route | Share of walkable ground that is |
|---|---|---|---|
| 1 | 2,946 | 81% | 15% |
| 100 | 1,548 | 66% | 17% |
| 200 | 743 | 72% | 14% |

- The gates are not where bots die: at most 4 deaths within 1,500 of any gate.
- Region 1 has one strip. 980 deaths lie between the Hadrian/Pennine seam and 4,000 short of Caer Erasleigh
  (median x 619,400), and 965 more within 4,000 of the keep. Midgard's main route enters at 620640,352320 and runs
  down that strip; 630 of the 980 are within 2,500 of it.
- The east-gate lane entering at 613216 is within 2,500 of 447 of them. The one entering at 617568 is within 2,500
  of 782, because it runs down the strip's centre. A second lane splits the column; it does not take it out of
  the fight.

## What would break live

- **Merging.** Lanes merge at the gate, where a column must narrow to about 200 units. Milegate doors were walked
  with default filters; their live state was not checked.
- **Via points.** 58 of 220 alternates use 8 or more via points; Detour pulls a lane back onto the main route
  between sparse ones. Thirty-nine use one or two.
- **Keeps.** 21 of 165 main routes pass within 4,000 of a third keep. Alternates do not, except two Castle Sauvage >
  Caer Hurbury lanes that pass Albion's own keeps and are marked conditional.
- **Monster camps.** 100 of 220 alternates pass a level 50+ aggressive monster within 1,500 and 27 pass five or
  more (main routes: 82 and 18 of 165). The Green Knight (level 79, 592359,418245) stands on the Forest
  Sauvage/Pennine seam; 11 catalogued lanes, including today's Hibernia PK > Caer Renaris main route, cross within
  2,500 of it in x. Other camps: windswept wraiths (670258,612822), frore liches (656186,618320), icestrider
  interceptors (708130,620076), great brown drakes (612759,380551), spectral manslayers (426861,370464).
- **Swimming.** 40 of 220 alternates and 17 of 165 main routes have over 1,000 units on Swim polygons (worst 15,550
  and 13,467). Seven alternates and six main routes use jump links.

## Recommended catalog format

One file, `navmesh/rvr-lanes.json`, beside `roads.json`:

- **Objective:** id, kind (portal keep, gate, border keep, keep, relic keep), keep id, point.
- **Trunk:** portal keep id, gate id.
- **Lane:** region, from, to, lane index (0 is the planner's own route, no via points), start, goal, via points,
  walked length, seam crossings, `keeps_within_4000` (so the server drops a lane while an enemy holds one), swim
  length, jump count, level 50+ count, and the mesh revision it was proved on.

**Validation at server start**, off the game loop, as `AutonomousZoneRoads.Preload` does:

1. Walk every leg of every lane with the planner (`RvrKeepRoute.TryBuild`, point to point).
2. Check that lanes of one trip still keep their separation.
3. Disable a lane that fails and fall back to lane 0; never use an unproved lane.
4. Log one line with the counts, and check again when a zone's `NavigationGeometryRevision` changes.

Each region's 2,000 or so survey walks took about two minutes, so 400 lanes cost seconds.

## Not verified

- Druim Ligen, Druim Cain and Vindsaul Faste lie in zones whose meshes were not in the survey folder. Hibernian
  home lanes start at the Connacht seam (334368,417640). Snowdonia Fortress was not used as a start.
- Lanes were walked one way only.
- Keep hops cover each keep and its two nearest, not every pair.
- "None found" means none among the candidates tried, on a 2,048-unit anchor grid. Only the gate funnels are shown
  directly.
- Guards spawned by keep code are not in the `Mob` table, so guard counts cover static guards only. Some Realm 0
  NPCs that may not be monsters (Earl Grael at Castle Sauvage, "Albion leader") are counted as monsters.
- Milegate door state, aggro ranges and how a 100-bot column behaves on a lane are untested.

Files: `rvr-lanes-draft.json` (385 lanes: 165 main, 220 alternates); under `lanes/`: `report.txt`, the job and
result files, and the scripts in `py/`.
