# Check spots for roaming 8-man groups: findings (draft, 2026-11-16)

Status: **tests only.** Every "proven" below is a navmesh probe (`UT_TripProbe`, `origin/integration/1.2` test build), not an in-game result. Nothing was seen on the owner's PC. Data file: `rvr-checkspots-draft.json` in this folder. Scratch work: `spots/`.

## 1. Method

- **World data** (`world-static.sqlite`, read only): 5,349 Mob rows lie in the 12 frontier zones. "Monster" here means realm 0, level above 0, not a guard, hastener, merchant or dummy class, and not peaceful or untargetable.
- **Camps:** greedy clusters of level 30-58 spawns within 1,200 units, at least 4 spawns, proper-named spawns left out. Result: 277 camps (Jamtland 59, Yggdra 54, Uppland 47, Pennine 35, Odin's Gate 25, Emain 23, Hadrian's Wall 13, Snowdonia 7, Breifine 7, Collory 3, Cruachan 3, Forest Sauvage 1).
- **Named monsters:** 60 single or proper-named spawns. 43 are `ClassicQuest` package rows (quest monsters the bots never hunt) and were dropped. Kept: Draco Magnificens, Green Knight, Glacier Giant, Evern, the frore liches and the mindworm field.
- **Entrances:** ZonePoint has three frontier dungeon doors: Hall of the Corrupt (Pennine), Dodens Gruva (Jamtland), Marfach Caverns (Breifine). The five Darkness Falls jump points inside the frontiers sit 220-740 units from a relic keep and admit the owning realm only, so they are not check spots. The public Darkness Falls doors are outside the frontier zones.
- **Chokepoints:** Door rows far from any keep give five milegates (two in Hadrian's Wall, one in Odin's Gate, two in Emain). Sixteen neutral Hasteners stand in the open, 9,600 units or more from any keep. The third source is today's death hot spots.
- **Safe points to avoid:** all Keep rows, plus the border keeps found from Area, Teleport, BindPoint and guard rows: Castle Sauvage, Snowdonia Fortress, Svasud Faste, and the guard posts of Vindsaul Faste, Druim Ligen and Druim Cain (those three keeps lie just outside the frontier zones). Portal-keep guards stand up to about 3,650 units from the keep centre.
- **Reachability:** start points are the portal-keep arrival points in `AutonomousFrontierTransport`. 101 candidates were probed from both enemy portal keeps (202 trips): 184 complete, 18 failed. Then every final spot, via-route and circuit leg was probed again. Keep clearance is measured on the probe's waypoints, which are 2,500 units apart, so it is an estimate.

## 2. What the code does today

- **The earlier note is half right.** The embedded CapnBry file has 0 of 5,460 goals in frontier zones. But `BuildCampCatalogDraft` also builds "classic165-restored" camps from the restored-spawn id lists, and 3,304 frontier Mob rows are on those lists (about 1,084 name-by-cell groups; my count from static tables, not the live catalog). Cruachan Gorge also gets live "source-empty" camps. So the live catalog does hold frontier camps.
- **Only the owning realm levels there.** `AutonomousOrdinaryPveRealmPolicy.CanAssignCamp` gives a frontier camp only to the realm that owns that frontier.
- **Roam objectives** (`ChooseRvrDestination`, `AutonomousWorldBotController.cs` 2451-2610):
  - up to 24 enemy bots **whose own objective is RvR** (hunt). An enemy bot with a PvE goal is never a hunt target; it is fought only if met.
  - relic carriers and dropped relics.
  - every siege keep, as an assault or defence target and as a "frontier near <keep>" anchor at the keep's own position.
  - "frontier patrol near <monster>": 24 random frontier camp cells with live monsters, snapped to the mesh.
- The event layer picks a roam objective at random (`AutonomousRvrEventLayer.cs` 622, 674). There is no spot type, no level filter (a level 13-20 Forest Sauvage cell counts the same as the bwca camp), no keep clearance, no order and no circuit.

## 3. Where PvE bots really die (log 11:45:59-16:39:12, 4 h 53 min)

9,766 kills; 5,809 in the three frontiers (5,296 RvR goal, 513 PvE goal).

| Frontier | Home-realm PvE deaths (distinct bots) | By zone | 4,000+ from a keep | Visitor PvE deaths | RvR deaths 4,000+ from a keep |
|---|---|---|---|---|---|
| Albion | 148 (132) | Pennine 136, Hadrian's Wall 12 | 135 | 45 | 1,976 of 2,999 |
| Midgard | 85 (82) | Jamtland 37, Odin's Gate 24, Uppland 19, Yggdra 5 | 59 | 133 | 1,214 of 1,552 |
| Hibernia | 52 (51) | Emain 43, Breifine 9 | 47 | 50 | 691 of 745 |

- Largest home-realm clusters: Pennine 613100,369780 (21 solo Albion bots, all killed by Midgard, 3,900 from the bwca camp); Pennine 611845,352764 (14, mostly a group, at the northern hill scrags); Odin's Gate 648600,586905 (17, only 3,254 from Bledmeer).
- "Visitor" deaths are bots with a PvE goal inside an enemy frontier. The code sends such a bot back to its portal keep (`TryReturnFromForeignFrontier`), so these look like bots walking home after their RvR task ended. I did not confirm that per bot.
- Share of home-realm PvE deaths within 5,000 units of a catalog spot: Albion 61 of 148, Midgard 27 of 85, Hibernia 22 of 52. Midgard's are scattered: apart from the 17 near Bledmeer, no cluster has more than 4.

## 4. Check spots

Lengths are walked units from each enemy realm's portal keep. "via" means the direct query failed and the trip was proven through waypoints. Last column: deaths today within 2,500 (RvR / home PvE / visitor PvE).

### Albion frontier (region 1)

| Spot | Type | x,y,z | Nearest keep | Mid PK | Hib PK | Deaths |
|---|---|---|---|---|---|---|
| hw-mid-milegate | chokepoint | 652209,320672,4312 | 25,247 | 28,908 | 74,128 | 0/0/0 |
| hw-hib-milegate | chokepoint | 610032,319468,3872 | 26,060 | 75,633 | 27,665 | 2/0/0 |
| hw-road-hastener | chokepoint | 629767,342311,4200 | 23,932 | 62,197 | 60,946 | 2/1/0 |
| hw-south-road | chokepoint | 619530,349774,4682 | 20,906 | 76,817 | 63,773 | 124/0/0 |
| camp-fellwood-north | camp | 619034,356263,4338 | 14,509 | 81,436 | 73,623 | 172/2/0 |
| camp-hill-scrag-north | camp | 611446,353259,4662 | 19,881 | 87,611 | 68,948 | 62/14/0 |
| camp-angry-bwca | camp | 611644,373415,4515 | 9,896 | 101,241 | 87,583 | 9/9/0 |
| camp-boggart | camp | 596132,359056,5812 | 15,206 | 104,765 | 79,185 | 10/5/1 |
| dungeon-hall-of-the-corrupt | dungeon | 598581,361828,3605 | 16,630 | 104,167 | 79,386 | 6/0/1 |
| named-draco-magnificens | named | 605913,381264,4408 | 18,576 | 112,870 | 98,622 | 0/0/0 |
| camp-cave-fairy-west | camp | 637955,341472,5526 | 16,093 | 59,395 | 67,667 | 0/0/0 |
| camp-exiled | camp | 647056,331769,4201 | 15,492 | 41,525 | 71,742 | 0/0/0 |
| camp-templar | camp | 606716,329393,4200 | 35,621 | 80,374 | 38,429 | 0/0/0 |
| camp-cliff-crawler | camp | 603501,415111,6481 | 15,068 | 150,324 | 138,479 | 0/0/0 |
| named-green-knight | named (fringe) | 592359,418245,4988 | 14,971 | 166,748 | 145,327 | 0/0/0 |

Why: every probed route south from either portal keep crosses into Pennine between x 600,000 and 620,000, past the wall gates and this road. The era research names fellwoods, cliff crawlers and angry bwca as Albion's Pennine levelling camps, and today's PvE deaths sit at the bwca, hill scrag and boggart camps.

### Midgard frontier (region 100)

| Spot | Type | x,y,z | Nearest keep | Alb PK | Hib PK | Deaths |
|---|---|---|---|---|---|---|
| og-alb-milegate | chokepoint | 622052,626565,5027 | 26,126 | 28,243 | 75,823 | 2/1/0 |
| og-road-hastener | chokepoint | 636279,601930,5484 | 22,119 | 60,225 | 53,184 | 24/0/0 |
| og-bledmeer-road | chokepoint | 646493,589739,6359 | 6,486 | 77,679 | 56,673 | 130/1/5 |
| camp-isalf-bledmeer | camp | 641732,593993,7722 | 12,490 | 70,542 | 52,803 | 3/1/4 |
| camp-isalf-hib-side | camp | 606924,595587,4071 | 17,643 | 95,783 | 18,250 | 0/0/0 |
| camp-icestrider | camp | 635989,613329,5408 | 32,298 | 49,908 | 63,494 | 0/0/0 |
| named-glacier-giant | named | 651972,626960,5468 | 24,921 | 60,300 | 83,134 | 12/0/4 |
| jamt-west-hastener | chokepoint | 657220,628357,5368 | 19,703 | 67,234 | 93,055 | 2/0/0 |
| camp-jamt-skeletons | camp | 665733,622635,4080 | 12,069 | 75,941 | 90,188 | 0/0/0 |
| camp-cockatrice | camp | 679310,640238,3940 | 13,225 | 94,022 | 112,439 | 15/0/4 |
| camp-wight | camp | 706365,614784,5195 | 14,547 | 123,082 | 130,664 | 0/0/0 |
| camp-chattering-skeleton | camp | 721478,644011,6267 | 20,016 | 141,603 | 164,360 | 97/1/9 |
| dungeon-dodens-gruva | dungeon | 718392,654864,6386 | 11,792 | 139,775 | 157,084 | 1/1/0 |
| camp-undead-viking | camp | 725428,620080,5715 | 25,313 | 138,485 | 153,018 | 0/0/0 |
| camp-yggdra-wolves | camp (fringe) | 699662,691610,5999 | 11,360 | 144,860 | 171,286 | 0/0/0 |

Why: the gate and the two hasteners lie on the probed routes east. The frore liches ("Nottmoor liches" in the era research) stand 2,600 east of jamt-west-hastener. Dodens Gruva, the Blendrake wights and the Arvakr hills in Yggdra are era-named Midgard levelling places.

### Hibernia frontier (region 200)

| Spot | Type | x,y,z | Nearest keep | Alb PK | Mid PK | Deaths |
|---|---|---|---|---|---|---|
| emain-alb-milegate (AMG) | chokepoint | 448817,340604,3456 | 27,203 | 27,781 | 75,833 | 5/1/1 |
| emain-mid-milegate (MMG) | chokepoint | 448810,298626,4256 | 25,548 | 78,911 via | 27,097 | 1/4/0 |
| emain-hastener | chokepoint | 440789,337516,3962 | 23,992 | 37,204 | 70,984 | 9/1/0 |
| emain-crossroads | chokepoint | 442639,319435,3575 | 16,777 | 55,708 via | 52,033 | 66/3/2 |
| emain-crauchon-road | chokepoint | 433583,317138,4104 | 7,866 | 65,545 via | 54,742 | 86/0/6 |
| camp-grogan | camp | 438187,320948,4945 | 12,509 | 60,800 via | 55,548 | 17/1/3 |
| camp-cliff-beetle | camp | 441757,327892,4737 | 18,338 | 46,956 via | 61,429 | 43/0/5 |
| camp-granny | camp | 455477,343485,4215 | 20,368 | 21,046 | 83,113 | 2/2/0 |
| camp-megafelid | camp | 469073,310649,3187 | 16,248 | 103,165 via | 17,396 | 0/0/0 |
| named-mindworms | named | 434872,297616,4348 | 22,940 | 79,985 via | 41,336 | 0/0/0 |
| breifine-crimthain-hastener | chokepoint | 435815,358546,2463 | 9,636 | 52,140 | 95,629 | 0/0/0 |
| camp-spectral-manslayer | camp | 418240,358570,4857 | 17,359 | 70,146 | 113,636 | 0/0/0 |
| named-evern | named | 429883,380409,2328 | 14,604 | 75,442 | 118,932 | 0/0/0 |
| dungeon-marfach-caverns | dungeon | 445881,392547,6411 | 17,270 | 85,816 | 129,305 | 0/0/0 |
| camp-invaders | camp (fringe) | 415463,413300,2677 | 19,695 | 115,342 | 158,831 | 0/0/0 |

Why: AMG, MMG, the lane between them, "Grogans plain" and "Grannies" are the era's own Emain place names. Breifine and Marfach Caverns are the quieter second stop.

## 5. Patrol circuits (every leg proven; loop length excludes the approach)

| Circuit | For | Order (then back to the first) | Loop | Approach | Closest keep |
|---|---|---|---|---|---|
| alb-mid-1-east-wall | Mid | hw-mid-milegate, camp-exiled, camp-cave-fairy-west, hw-road-hastener | 66,523 | 28,908 | 14,456 |
| alb-mid-2-pennine-pass | Mid | hw-road-hastener, hw-south-road, camp-fellwood-north, camp-angry-bwca, camp-hill-scrag-north | 85,776 | 62,197 | 6,108 |
| alb-mid-3-deep-pennine | Mid | camp-hill-scrag-north, camp-boggart, dungeon-hall-of-the-corrupt, named-draco-magnificens, camp-angry-bwca, camp-fellwood-north | 87,256 | 87,611 | 6,156 |
| alb-hib-1-west-wall | Hib | hw-hib-milegate, camp-templar, camp-hill-scrag-north, hw-south-road, hw-road-hastener | 94,674 | 27,665 | 18,888 |
| alb-hib-2-west-pennine | Hib | camp-hill-scrag-north, camp-boggart, dungeon-hall-of-the-corrupt, named-draco-magnificens, camp-angry-bwca | 78,998 | 68,948 | 9,526 |
| alb-hib-3-south-fringe | Hib | dungeon-hall-of-the-corrupt, named-draco-magnificens, camp-cliff-crawler, named-green-knight, camp-boggart | 145,521 | 79,386 | 11,504 |
| mid-alb-1-milegate | Alb | og-alb-milegate, camp-icestrider, named-glacier-giant, jamt-west-hastener | 84,315 | 28,243 | 19,703 |
| mid-alb-2-bledmeer | Alb | camp-icestrider, og-road-hastener, camp-isalf-bledmeer, og-bledmeer-road | 56,901 | 49,908 | 6,486 |
| mid-alb-3-jamtland | Alb | jamt-west-hastener, camp-jamt-skeletons, camp-cockatrice, camp-wight, camp-undead-viking, camp-chattering-skeleton, dungeon-dodens-gruva | 205,314 | 67,234 | 4,499 |
| mid-hib-1-north | Hib | camp-isalf-hib-side, og-road-hastener, camp-isalf-bledmeer, og-bledmeer-road | 112,032 | 18,250 | 6,486 |
| mid-hib-2-gate | Hib | og-road-hastener, camp-icestrider, named-glacier-giant, jamt-west-hastener, camp-jamt-skeletons | 88,446 | 53,184 | 12,069 |
| mid-hib-3-jamtland | Hib | same seven spots as mid-alb-3-jamtland | 205,314 | 93,055 | 4,499 |
| hib-alb-1-amg | Alb | emain-alb-milegate, emain-hastener, camp-cliff-beetle, emain-crossroads | 51,309 | 27,781 | 16,725 |
| hib-alb-2-crauchon | Alb | emain-crossroads, camp-grogan, emain-crauchon-road, camp-cliff-beetle | 34,364 | 55,708 via | 7,866 |
| hib-alb-3-breifine | Alb | emain-hastener, breifine-crimthain-hastener, camp-spectral-manslayer, named-evern, dungeon-marfach-caverns | 147,196 | 37,204 | 4,682 |
| hib-mid-1-mmg | Mid | emain-mid-milegate, named-mindworms, emain-crauchon-road, camp-grogan, emain-crossroads | 69,440 | 27,097 | 7,866 |
| hib-mid-2-crossroads | Mid | emain-crossroads, camp-cliff-beetle, emain-hastener, camp-grogan | 41,181 | 52,033 | 12,509 |
| hib-mid-3-breifine | Mid | same five spots as hib-alb-3-breifine | 147,196 | 70,984 | 4,682 |

Per-leg lengths and each leg's closest keep are in the JSON.

## 6. Gaps

- **No evidence of anyone in four zones.** Forest Sauvage, Snowdonia, Mount Collory and Cruachan Gorge had 0 PvE-goal deaths and 0, 0, 2 and 5 RvR deaths in 4 h 53 min. They also hold few level 35-50 camps (1, 7, 3, 3). The log records deaths only, so this can mean "no PvE bots there" or "nobody went to kill them"; either way there is nothing proven to hunt. Their camps are in `reserve`, not in circuits. Yggdra had 5 PvE deaths.
- **Direct pathing from the Albion portal keep in Emain fails** for 16 of the 30 candidates (partial path, no zone road route); the Midgard portal keep reaches all 30. Seven final spots are proven only via AMG and emain-hastener. Nine reserve spots are not proven for Albion.
- **Direct routes to deep spots pass keeps.** Examples: Hibernia to camp-chattering-skeleton passes about 1,571 from Blendrake; Albion to camp-undead-viking 2,441 from Nottmoor; both realms to camp-invaders 2,867 from Crimthain. Deep spots should be reached through circuit waypoints.
- One candidate (619802,352685, 429 units from a zone border) failed zone-step planning and was replaced by hw-south-road.
- Era names I could not match to spawns: Glenlock rock giants, the crab plateau, boogie men behind APK (one spawn, in Breifine), Arawnites.

## 7. Recommended shipped format

One embedded JSON, built by a tool from a clean release world and never from a played database, in the style of `capnbry_classic_si_goals.json`:

```json
{"schema":1,"frontiers":[{"region":200,
  "spots":[{"id":"emain-alb-milegate","type":"chokepoint","zone_id":214,
            "local_x":31025,"local_y":53884,"z":3456,"radius":600,"min_level":35}],
  "circuits":[{"id":"hib-alb-1-amg","realms":["Albion"],
               "approach":["emain-alb-milegate"],
               "spots":["emain-alb-milegate","emain-hastener","camp-cliff-beetle","emain-crossroads"]}]}]}
```

- Zone-local coordinates, as the CapnBry file uses; the loader snaps to the mesh and requires a local exit, as the "frontier patrol" code already does.
- Keep clearance is checked at run time against keeps the group's realm does not hold, because keeps change hands.
- Circuits are ordered spot lists so each leg is a short, proven query; `approach` holds the via-points where the direct query fails.
- A release check replays every approach and leg with `UT_TripProbe` on the shipped meshes.
