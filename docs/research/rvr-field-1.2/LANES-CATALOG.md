# RvR lane catalog (1.2)

**Status: tests only.** Every lane completed a walk in both directions through the server's own route planner on
the installed 0.37b frontier meshes. None of them has been seen in game.

The catalog is `source/server/GameServer/bots/autonomous/data/rvr-lanes.json`, embedded in GameServer; WP5 loads it.
It holds geometry only: keep, milegate and entry points, via points, walked lengths and a proof hash.

## How it was built

`source/tools/build_rvr_lanes.py` writes walks for `UT_RvrLaneSurvey/2`, an `[Explicit]` NUnit tool that walks as
a bot does (zone steps from `AutonomousZoneItinerary.ProbeNextStep`, then Detour).

- **Shape:** trunk, then fan. Each portal keep's valley has one exit, so a **trunk** (lane 0 only) runs from the
  portal keep to its milegate. Lanes fan out from each milegate to every keep. **Entry** lanes run from each realm's
  two border keeps to its own keeps. **Hops** join every keep pair within 60,000 units.
- **Lane 0** is the planner's own route.
- **Alternates** come from via points offset to either side of lane 0 or from road-graph detours. One ships only
  if both directions:
  - complete;
  - are at most 35% longer than lane 0;
  - stay 2,500 units from lane 0 over the middle half;
  - pass no keep within 4,000 units except the lane's own start and goal;
  - swim at most 1,000 units;
  - pass no level 50+ aggressive monster camp within 1,500 units unless lane 0 passes it.

  Two alternates must be 2,500 units apart.
- **Proof hash:** hashes the tool version, the region, the SHA-256 of every mesh, road and seam file (listed under
  `meshes`) and the lane's points.

## Counts

There are 149 trips and 332 lanes, of which 183 are alternates.

| Kind | Trips | Lanes | 0 alternates | 1 | 2 |
|---|---|---|---|---|---|
| Trunk | 6 | 6 | n/a | n/a | n/a |
| Milegate to keep | 54 | 112 | 12 | 26 | 16 |
| Border keep to keep | 42 | 91 | 12 | 11 | 19 |
| Keep hop | 47 | 123 | 1 | 16 | 30 |

- **Lane 0:** it completed both ways on all 149 trips.
- **Reverse walks:** all 1,903 alternates that passed going out completed on the way back; no lane is one-way.
  86 then failed a filter on the way back and were dropped.
- **Outbound filter losses:** swim, monster camps and keeps cut most of all; the draft had alternates on 52 of 54
  milegate trips.

## Not covered

- Relic keeps from their own realm's border keeps.
- Keep pairs farther apart than 60,000 units.
- Live keep ownership: the keep filter rejects every keep, whoever holds it.
- Keep-spawned guards.
- Milegate door state.

The server must re-walk every lane at start: Detour can choose another corridor after a mesh change.

## Rebuild

Build `Tests.dll` with `Detour.so` or `Detour.dll` in its `lib/`, then run:

```
python3 source/tools/build_rvr_lanes.py --db <world db> --navroot <folder holding navmesh/> \
    --tests-dll source/server/build/Tests/Debug/lib/Tests.dll --work <scratch> \
    --out source/server/GameServer/bots/autonomous/data/rvr-lanes.json [--lock <flock file>]
```

- The database supplies only the positions in `Keep`, `Zones` and `Mob`.
- The script needs numpy and scipy and takes minutes per region.
- Raise `UT_RvrLaneSurvey.ToolVersion` when the walk changes.

This build read the static tables of a played world; rebuild it from a clean release world before 1.2 ships.
