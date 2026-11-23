# Navmesh drops: where a bot can be left on no polygon (1.2.2)

Issue #438 (fixed in 1.2.1, PR #439): Darkness Falls' entrance stairs are flat landings joined by one-way off-mesh drops.
A companion that replotted in the middle of a drop found no polygon within the server's pick box and stood there for
good. This page is the same check run over **every** navmesh the game ships, and what 1.2.2 does about it.

Status: **tests only.** The scan and the probe ran on the shipped navmeshes with the server's own Detour code; the unit
tests run in CI. Nothing here has been seen in game yet.

## The stuck condition

The server finds a bot's polygon with `findNearestPoly` in a pick box of game (32, 32, 64) around it
(`LocalPathfindingMgr._defaultHalfExtents`). An off-mesh link (a ladder, a wall or tower climb, a ledge drop, a gap, a
lift) is not a surface: a bot walking it moves in a straight line between the two ends, and in the middle of any link
that rises or falls more than about 128 units (or crosses a gap) nothing is within 64 units above or below it. If the
bot's path is replotted there (a companion's formation order changes on every think; a stop and a new order), the path
starts on no polygon: `NoPathFound`. A companion with a formation order pauses on a failed path, and every later replot
fails from the same point.

## How it was measured

- `tools/navmesh/navscan.cpp` loads a navmesh exactly as the server's `Detour.dll` does and, for every off-mesh
  connection in each direction it can be taken, samples 51 points from start to end and counts those on no polygon
  (`dead`), checks that both ends are linked, whether a path between the ends really crosses the link (`used`) and
  whether there is any way round without it. It also lists every pair of linked ground polygons whose floors differ by
  more than 64 units at the same spot (sheer steps a path can cross without a link) and the dead samples between them.
- `tools/navmesh/scan_all_zones.py` runs it on every `zoneNNN.nav` (a folder, or pulled a few at a time from the public
  v0.36b download with HTTP ranges) and `tools/navmesh/summarize_drops.py` writes `tools/navmesh/navmesh-drops.json`:
  each zone's sha256 and every stuck-prone link direction. No navmesh is in Git.
- Source: the 103 navmeshes of the v0.36b full download (the set 1.2 ships; 1.2's content patch for `zone277.nav`
  changes Hall of the Corrupt, which has no links in either version). The Darkness Falls mesh in the scan has the hash
  the DF certificate names (`41f1489c...`), and the scan reproduces the README's DF numbers exactly (Albion drops 1-3:
  4, 11 and 0 dead samples; Midgard 7, 11, 0; Hibernia 4, 11, 0).
- Probe checks (`tools/navmesh/dfprobe`, `nearest`): the middle of the worst link at Camelot Hills, Caer Sidi, Trollheim,
  Tuscaran Glacier, Fomor, Caillte Garran, Galladoria and Cruachan Gorge is on no polygon (ref 0), and each link's far end
  has a floor within 24 units.

## What was found

- **5,202 off-mesh connections** (10,395 directions) in 54 of the 103 navmeshes. Only Darkness Falls has one-way links
  (its 9 entrance stair drops); every other link can be taken both ways.
- **1,262 link directions in 39 zones are stuck-prone** (at least one sample on no polygon); 1,133 of them are on the
  path between their two ends, and 1,240 have no way round without the link.
  - 546 are lifts (rise or drop of 300 or more and at least twice the run). `Pathfinder.IsVerticalLink` already carries a
    bot over these in one step (run 12/13, Caer Sidi), so a bot is not normally inside one.
  - 1,156 are steep (rise or drop of at least 64 and at least the run, run up to 320): ladders, wall and tower climbs,
    ledge drops of 141-420 units.
  - 106 are shallow: gaps and short ice steps (86 in Tuscaran Glacier, 10 in Fomor, the rest in Faraheim, Galladoria,
    Caillte Garran and Pennine Mountains). Most of their dead samples are at one end (the end is off the mesh), so no path
    uses them; 12 are used.
- **Sheer steps: 3,177** linked polygon pairs whose floors differ by more than 64 at the same spot, almost all at a tile
  border where two stacked floors were joined (up to 375 units in Tuscaran Glacier). **None has a dead sample**: between
  the two floors there is always a polygon within the pick box, so a replot from there succeeds. Not stuck-prone.
- **No links at all** (nothing to get stuck on): Krondon, Avalon City, Crystal Cave, Summoner's Hall (248), Marfach Caverns
  (276), Hall of the Corrupt (277), every classic dungeon (Tomb of Mithra, Stonehenge Barrows, Keltoi Fogou, Catacombs of
  Cardova, Tepok's Mine, Spindelhalla, Vendo Caverns, Varulvhamn, Cursed Tomb, Nisse's Lair, Coruscating Mine, Muire Tomb,
  Spraggon Den, Koalinth Caverns, Treibh Caillte), the capitals and Lyonesse. The battlegrounds (250-253), the Midgard
  frontier, Hadrian's Wall, Emain Macha, Breifine and Sheeroe Hills have links, none stuck-prone.

### Zones with stuck-prone links

Directions: link directions in the zone; stuck: stuck-prone directions; used: of those, on a real path; sites: clusters
within 600 units; lift / steep / shallow: as above (a lift is also steep); dead: the most samples of 51 on no polygon;
height: the smallest and largest rise or drop.

| Zone | Name | Links | Stuck | Used | Sites | Lift | Steep | Shallow | Dead | Height |
|---|---|---|---|---|---|---|---|---|---|---|
| 0 | Camelot Hills | 138 | 78 | 78 | 11 | 24 | 78 | 0 | 29 | 141-333 |
| 1 | Salisbury Plains | 42 | 24 | 24 | 3 | 16 | 24 | 0 | 28 | 144-333 |
| 2 | Black Mtns. South | 144 | 68 | 66 | 7 | 44 | 68 | 0 | 32 | 186-387 |
| 3 | Black Mtns. North | 24 | 16 | 16 | 3 | 0 | 16 | 0 | 27 | 221-294 |
| 6 | Cornwall | 60 | 10 | 10 | 2 | 2 | 10 | 0 | 11 | 182-320 |
| 7 | Llyn Barfog | 24 | 16 | 16 | 2 | 8 | 16 | 0 | 29 | 214-333 |
| 8 | Campacorentin Forest | 177 | 34 | 34 | 6 | 32 | 34 | 0 | 36 | 166-486 |
| 9 | Avalon Marsh | 96 | 28 | 28 | 3 | 20 | 28 | 0 | 29 | 266-333 |
| 11 | Forest Sauvage (frontier) | 75 | 36 | 36 | 3 | 24 | 36 | 0 | 29 | 266-333 |
| 12 | Snowdonia (frontier) | 99 | 12 | 12 | 1 | 8 | 12 | 0 | 29 | 266-333 |
| 14 | Pennine Mountains (frontier) | 147 | 4 | 0 | 1 | 0 | 2 | 2 | 18 | 22-83 |
| 52 | Avalon Isle | 75 | 8 | 8 | 2 | 0 | 8 | 0 | 9 | 160-170 |
| 55 | Gwyddneau | 3 | 2 | 2 | 1 | 2 | 2 | 0 | 45 | 1462 |
| 56 | Aldland | 36 | 2 | 2 | 1 | 2 | 2 | 0 | 1 | 512 |
| 60 | Caer Sidi | 6 | 4 | 4 | 1 | 4 | 4 | 0 | 42 | 947 (the lift) |
| 100 | Vale of Mularn | 51 | 6 | 6 | 1 | 6 | 6 | 0 | 32 | 320-384 |
| 101 | East Svealand | 30 | 10 | 10 | 2 | 10 | 10 | 0 | 33 | 320-384 |
| 103 | Gotar | 15 | 4 | 4 | 1 | 4 | 4 | 0 | 33 | 384 |
| 150 | Trollheim | 48 | 32 | 32 | 6 | 24 | 32 | 0 | 34 | 202-419 |
| 153 | Delling Crater | 516 | 172 | 167 | 31 | 124 | 172 | 0 | 38 | 144-1008 |
| 154 | Faraheim | 165 | 78 | 70 | 12 | 50 | 74 | 4 | 29 | 29-403 |
| 158 | Modernagrav | 33 | 16 | 16 | 3 | 8 | 16 | 0 | 28 | 221-419 |
| 160 | Tuscaran Glacier | 1174 | 92 | 8 | 8 | 0 | 6 | 86 | 46 | 2-113 |
| 161 | Iarnvidiur's Lair | 27 | 12 | 12 | 3 | 12 | 12 | 0 | 45 | 1043-1450 |
| 180 | Fomor | 516 | 262 | 254 | 41 | 46 | 252 | 10 | 43 | 0-954 |
| 181 | Domnann | 45 | 12 | 12 | 1 | 0 | 12 | 0 | 13 | 144-182 |
| 182 | Caillte Garran | 285 | 76 | 76 | 16 | 0 | 74 | 2 | 21 | 3-240 |
| 185 | World's End | 3 | 2 | 2 | 1 | 2 | 2 | 0 | 36 | 614 |
| 186 | Vigilant Rock | 90 | 40 | 40 | 10 | 4 | 40 | 0 | 35 | 163-614 |
| 187 | Allta Fearann | 6 | 4 | 4 | 2 | 4 | 4 | 0 | 36 | 614 |
| 190 | Tur Suil | 6 | 4 | 4 | 1 | 4 | 4 | 0 | 40 | 880-883 |
| 191 | Galladoria | 54 | 18 | 14 | 4 | 12 | 16 | 2 | 42 | 0-1059 |
| 201 | Silvermine Mts. | 36 | 8 | 8 | 1 | 0 | 8 | 0 | 21 | 150-240 |
| 202 | Shannon Estuary | 12 | 8 | 8 | 1 | 0 | 8 | 0 | 20 | 150-243 |
| 204 | Lough Gur | 18 | 8 | 8 | 1 | 0 | 8 | 0 | 13 | 182 |
| 205 | Bog of Cullen | 69 | 4 | 4 | 2 | 4 | 4 | 0 | 8 | 323-336 |
| 210 | Mount Collory (frontier) | 54 | 16 | 16 | 4 | 16 | 16 | 0 | 28 | 416 |
| 211 | Cruachan Gorge (frontier) | 48 | 16 | 16 | 4 | 16 | 16 | 0 | 27 | 413-416 |
| 249 | Darkness Falls | 161 | 20 | 6 | 5 | 14 | 20 | 0 | 36 | 174-1123 |

"Links" here counts directions (a two-way link is two). Every link, with its coordinates, is in
`tools/navmesh/navmesh-drops.json`. Examples (game coordinates, start to end):

- Camelot Hills climb 572828.8,504777.6,2198.4 to 572828.8,504803.2,2384.0: 186 up over 26, 13 dead samples (t 0.40-0.64).
- Trollheim climb 26169.6,41852.8,15369.6 to 26134.4,41852.8,15574.4: 205 up over 35, 10 dead.
- Fomor climb 34233.6,25836.8,16000.0 to 34217.6,25852.8,16268.8: 269 up over 23, 22 dead (Fomor has 41 such sites).
- Tuscaran Glacier ice step 52682.6,34041.8,15504.2 to 52789.5,34031.5,15543.5: 39 up over 107, 20 dead (a gap).
- Caer Sidi lift 38976.0,31689.6,13862.4 to 38976.0,31644.8,14809.6: 947 up, 42 dead (already carried by the lift rule).

## What 1.2.2 changes (server only, no navmesh change)

Darkness Falls (zone and region 249) is unchanged: its certified stair falls (`AutonomousDarknessFallsNavigation`, the
certificate's hash guard) and `DF_STAIR_LANDING` stay as they were, and none of the rules below runs there
(`NavLinkLeg.AppliesIn`).

Everywhere else, for every bot (companions and autonomous gamebots; not players, pets or other NPCs), with no table at
run time: the rules read the bot's own path, so they cover any link of whatever navmesh is installed.

1. **No replot on a steep link.** The mover remembers the leg it is walking (from where it was to the next path node).
   A leg that rises or falls at least 64 (the pick box's half height) and at least as far as it runs, over a run of at
   most 320, is a link leg (`NavLinkLeg.IsCommitLeg`; no walkable slope makes one). While a bot walks one, a new path
   order (`PathTo`) only changes where it goes from the far end, and a follower's check waits for its arrival. A direct
   walk order or a stop ends this at once.
2. **Set down on the far end.** If a bot's path fails (`NoPathFound`) while it is inside its last leg (within 40 of it,
   between its two heights, not at its end; legs up to 320 long), on no polygon within the pick box, and the leg's end
   has a floor within 32, it is set down on that floor and paths on from there (`NAV_LINK_LANDING` in the server log,
   200 lines at most). This covers the shallow gaps and ice steps too, and any leg left by a stop or a new order.

`UT_NavLinkLegs` checks the rules against the scan's table: every stuck-prone link outside DF (1,232 with a dead window
away from its end) is set down on its end from the middle of its dead window, every link the table marks steep is a
commit leg (and nothing else is), ordinary walking legs are not, and the scan's DF mesh is the certified one.

## Not covered

- A bot inside a link that it did not walk (placed there by a saved position or a teleport): the mover forgets its leg
  when it is moved by other means. The autonomous floor repair (`AutonomousNavigationSurface.TryFloor`) still applies.
- Pets and other NPCs: unchanged (a pet out of combat is already put at its owner's feet when its path fails).
- The lifts' own rule (`Pathfinder.IsVerticalLink`) is unchanged.

## Re-running

```
D=source/server/Pathing/Detour
g++ -O2 -std=c++17 -DDT_POLYREF64 -I$D/Include tools/navmesh/navscan.cpp $D/Source/*.cpp -o navscan
python3 tools/navmesh/scan_all_zones.py --navscan ./navscan --out scan.jsonl --dir <install>/runtime/server/navmesh
python3 tools/navmesh/summarize_drops.py scan.jsonl --table tools/navmesh/navmesh-drops.json --sites sites.json
```

A changed navmesh shows as a changed `sha256` in the table; `UT_NavLinkLegs` then needs its counts updated.
