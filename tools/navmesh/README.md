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
| `dfprobe zone.nav climb landing(xyz) air(xyz) lip(xyz) goal(xyz)` | A certified drop walked backwards: the path up from the landing (before), the samples on no polygon for the rise and the step onto the lip, then the path on from the lip |

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

## The way up (1.2.2, 2026-11-22)

Same mesh. **Leaving DF does not need the stairs:** each realm's exit zone point is on its hall floor, below them, and
the paths there are complete from the hall floor and from the arrival point (Albion `path 34400 27990 22386 36118 30504
22381` 32 nodes, `path 31211 27924 22893 36118 30504 22381` 55; Midgard to 11920,18671,22380: 16 and 35; Hibernia to
41722,36569,20333: 18 and 52).

**Going up there is no path**, and the partial one depends on where the goal is:

| From the hall floor to | Albion | Midgard | Hibernia |
|---|---|---|---|
| the top of the stairs | 4 nodes, ends at the foot | 5 nodes, the foot | 4 nodes, the foot |
| the arrival point | 103 nodes, ends at 30395,27411,20180 (2,700 below it) | 235 nodes, ends at 18315,21055,21543 | 31 nodes, ends at 42945,40751,20701 |

A companion follows a partial path ("Continue to the next node, even on partial paths"), so with its player near the
arrival point it walked into another level and stayed there: the end is within the 3,000-unit recall range of the
arrival for Albion and Midgard. A companion with a formation order (its player walking) and an autonomous gamebot (DF
refuses it partial paths) stood at the foot instead.

`climb` with each drop's landing, air point and lip (the certificate's `directedFalls`) toward the realm's arrival:
before, the path from every landing is partial; the rise has the fall's own dead window (Albion 4, 10, 0 of 51; Midgard
4, 11, 0; Hibernia 4, 10, 0), which the 1.2.1 landing repair covers; the step onto the lip is on a polygon at every
sample; from the top lip the path to the arrival is complete in all three realms, and from each lower lip the next
drop's landing is a complete walk (3-5 nodes). The server climbs this chain (`TryPlanStairClimb`, `DF_STAIR_CLIMB`).
Tests only.
