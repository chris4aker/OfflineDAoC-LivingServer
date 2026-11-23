# Navmesh probe (dfprobe)

`dfprobe.cpp` loads a server navmesh set (RecastDemo `MSET`, the `runtime/server/navmesh/zoneNNN.nav` files) with the
server's own Detour code (`source/server/Pathing/Detour`, `LoadNavMesh` from `DetourInterface.cpp`, 16384 search
nodes, 64-bit poly refs) and queries it as the server does:

- game (x, y, z) to Detour (x/32, z/32, y/32), the swizzle in `PathfindingMgrBase.FillRecastFloats`;
- pick box game (32, 32, 64), Detour (1, 2, 1): `LocalPathfindingMgr._defaultHalfExtents`;
- include `All ^ Disabled`, exclude 0: `PathfindingMgrBase.DefaultFilters`;
- `findStraightPath` with `DT_STRAIGHTPATH_ALL_CROSSINGS`, as `GetPathStraight`.

No navmesh is in Git. Use the `.nav` of the release the player runs (the 1.2 / v0.36b download), read only.

## Build

```
D=source/server/Pathing/Detour
g++ -O2 -std=c++17 -DDT_POLYREF64 -I$D/Include tools/navmesh/dfprobe.cpp $D/Source/*.cpp -o dfprobe
```

## Commands

| Command | What it prints |
|---|---|
| `dfprobe zone.nav nearest x y z` | The polygon the server's pick box finds for a point (ref 0: none) |
| `dfprobe zone.nav path x y z x y z` | The straight path, node by node, with flags, off-mesh links and drops over 64 units |
| `dfprobe zone.nav column x0 y0 x1 y1 step zlo zhi` | Every floor height under a line, every `step` units (steps, ledges, gaps) |
| `dfprobe zone.nav walk x y z x y z step` | `moveAlongSurface` in steps of `step` units, and where it is blocked |
| `dfprobe zone.nav offmesh x0 y0 x1 y1` | The off-mesh links in a box |
| `dfprobe zone.nav fall lip(xyz) air(xyz) landing(xyz) goal(xyz)` | A certified stair drop: the samples on no polygon for the old diagonal slide and for the walk-off-the-lip + drop, then the path on from the landing |
| `dfprobe zone.nav reach x y z x y z [x y z ...]` | Whether each target's polygon is reached from the start's over the mesh links (a flood with the server's filter; off-mesh links one way) |
| `dfprobe zone.nav corridor x y z x y z` | The server's corridor check (`CalculateCompleteCorridorCore`): up to 16 path segments, each partial one continued |
| `dfprobe zone.nav border x y z axis border lo hi step zlo zhi` | axis x or y: every floor 64 inside a zone border, starred where the start reaches it |
| `dfprobe zone.nav batch` | One query per stdin line (`from x y z`, `at x y z`, `floors x y zlo zhi`), for scripts |

`seam_route_sim.py` replays the gamebots' zone itinerary (`AutonomousZoneItinerary.TryNextStep`) over a region with the
installed `seams.json`, `roads.json` and zone meshes, hop by hop, and reports a trip that turns back on itself three
times (`AUTONOMOUS_ZONE_SEAM_CYCLE`). Corridor proofs are replaced by `dfprobe batch` reachability; `--rule old` is the
1.2.1 crossing pick, `--rule new` the 1.2.2 one. An estimate of the server's planner, not the server: pin what it finds
in a unit test.

## Darkness Falls entrance stairs (issue #438, 2026-11-22)

Mesh: `zone249.nav`, sha256 `41f1489c66ec465d203142590e060b136bc1ed4b6310fb64b28a9694dd3a9d8d` (the hash the DF
certificate `darkness_falls_navigation_proofs.json` names; the 1.2 / v0.36b download).

Albion: the arrival point is 31211,27924,22893. `path 31211 27924 22893 36118 30504 22381` (to the Albion exit) is
complete, 55 nodes, but the stair run is not a walkable slope in the mesh: it is three flat landings, each ending in a
sheer edge (`column 33380 27951 34420 27961 16 22300 23000`: 22885 at x 33508, 22708 at x 33524), joined only by three
one-way off-mesh links (Jump flag, radius 28.8):

| Drop | Lip | Landing | Height | Samples on no polygon, lip to landing |
|---|---|---|---|---|
| 1 | 33508.0, 27951.7, 22884.4 | 33541.0, 27951.2, 22709.2 | 175 | 4 of 51 (t 0.46-0.52) |
| 2 | 33805.8, 27957.4, 22704.6 | 33839.9, 27956.9, 22513.8 | 191 | 11 of 51 (t 0.34-0.54) |
| 3 | 34269.0, 27960.2, 22512.6 | 34301.0, 27960.5, 22384.7 | 128 | 0 |

Midgard (16196 to 15373, y 18660-18681) and Hibernia (44310 to 43529, y 37978-38058) have the same three drops with
the same dead windows (drop 1: 7 and 4 samples, drop 2: 11 and 11, drop 3: none). Going up, there is no path at all
(`path 34400 27990 22386 31211 27924 22893` is partial): the links are one-way.

What went wrong for companions: the server's generic NPC mover walked a companion straight from the lip to the landing
(a diagonal slide through the step). A companion re-orders its formation slot on every think while its player walks on,
and a new order replots from where the companion is. Inside the drop no polygon is within the pick box, so the path
failed (NoPathFound), a companion with a formation order pauses on a failed path, and every later replot failed from
the same point: it stood on the step for good. Autonomous gamebots already took these drops as certified falls (walk off
the lip, then down) and did not replot during one.

The fix (1.2.1, server side, no navmesh change; changing `zone249.nav` would also break the DF certificate's hash):
every bot takes the certified fall, a companion's new orders during a fall only update where it goes after landing,
and a bot left inside a drop anyway is set down on that drop's landing (`DF_STAIR_LANDING` in the server log).
`fall` with the shipped links shows the walk off the lip on a polygon at every sample and a complete path from every
landing. The drop itself still has no polygon in its middle, which is why the fall is not replotted and why the landing
rule exists. Tests only.

## Lough Derg / Valley of Bri Leith seam (issue #421, 2026-11-22)

Meshes: `zone200.nav` sha256 `5919e10e04ec50234f842b197ab6a67b1043a50475f14ca7ae1cd34436e45fcc`, `zone206.nav`
`0560feab0807ca4875bd3b48e971669e6b2901ab1ca11583a12b171b8723d462`, `zone207.nav`, `zone210.nav` (the 1.2 / v0.36b
download; the same hashes as the 0.37b package), with that download's `seams.json` (`70774af9...`) and `roads.json`.

The ground (`reach`):
- `reach 346463 490663 5330 384960 486688 5480` (Mag Mell to the zone 200 cycle spot): **NOT reached**. From
  384960,486688,5480 the flood reaches 1,379 polygons, 384960,483424,7264 among them: a ridge along Lough Derg's
  Connacht border (seam ground 6081; its road anchors run x 350,669-384,960, y 483,614-485,457) that joins no other
  Lough Derg ground.
- Zone 206: 385088,486688,5518 is on the Valley's main ground (53,440 polygons, 421892,488015 reached);
  385088,483424,7262 is a shelf of 2,006 polygons (seam ground 10136) that reaches neither.
- `seams.json` 200>206 (x 385024): 6081>10136 at y 483,424-486,496, 6081>61486 (Valley main) at y 486,624-488,352,
  69676 (Lough Derg main)>61486 from y 488,480. 200>207: 6081>69404, a Connacht ledge (x 350,432-352,096). 206>210:
  10136>65110, a Mount Collory ledge that joins nothing else. No seam entry is wrong: each line joins the ground it
  says.

Why bots loop (`seam_route_sim.py --rule old`, Valley main 385088,486688 to the DF portal 325269,433985): the
itinerary plans 206>200>207 and groups the 206>200 crossings by near-side ground. Its "reaches the main ground"
preference looked only at each group's best crossing; toward Connacht the ridge crossings (y 486,624) beat the main
ones (y 488,480) in both groups, so no pick reached the main ground, nothing was left out, and the bot stepped onto the
ridge. From the ridge the plan goes back through the Valley at the same spot: three reversals,
`AUTONOMOUS_ZONE_SEAM_CYCLE at 384960,486688`. From the shelf toward Mount Collory the same happens at 385088,483424.
From the ridge toward Cruachan Gorge an unlisted border sample onto the Connacht ledge passed the chain check (the ledge
goes on to a Cruachan ledge) and looped at 350432,483392.

Fix (server, no navmesh, seam or road file change): no crossing, listed or sampled, onto ground that never reaches the
destination's main ground while the border lists one that does; not on the last border (the walk on to the goal is
proved there) and not for a goal whose road anchor is on other ground.

| Trips (`seam_route_sim.py`) | Before (`--rule old`) | After (`--rule new`) |
|---|---|---|
| From the four seam spots (both sides of both crossings) to 7 goals in 201, 207, 208, 210, 211, 212 | 15 of 28 cycle | 28 of 28 arrive |
| Between 9 places on the main ground of zones 200, 201, 206, 207, 208, 210, 211, 212 (72 trips) | 62 arrive, 10 cycle | 72 arrive |

Valley main to the DF portal, after: 385088,488544 > 384960,488544 (Lough Derg main), then 350240,483392 >
350240,483264 into Connacht, which reaches the portal. Tests only.

## Every zone: navscan (1.2.2, docs/NAVMESH-DROPS.md)

`navscan.cpp` (same build line, `navscan` for the output) prints one JSON line per off-mesh connection direction (dead
samples on the straight lip-to-landing line and on the walk-off-then-descend shape, whether both ends are linked,
whether a path between the ends crosses it, path length with and without Jump links) and per sheer step between two
linked ground polygons (floors more than 64 apart at the same spot, with the dead samples between them).

| Script | What it does |
|---|---|
| `scan_all_zones.py --navscan navscan --out scan.jsonl --dir <navmesh folder>` | Scans every `zoneNNN.nav` (or `--zip-entries entries.json` to pull them from the v0.36b download with HTTP ranges; `--delete` removes each after its scan) |
| `summarize_drops.py scan.jsonl --table navmesh-drops.json --sites sites.json` | Writes the committed table (each zone's sha256 and every stuck-prone link) and the site clusters |

`navmesh-drops.json` is the 2026-11-22 scan of the v0.36b navmeshes (the 1.2 set); `UT_NavLinkLegs` checks the mover's
link rules against it.
