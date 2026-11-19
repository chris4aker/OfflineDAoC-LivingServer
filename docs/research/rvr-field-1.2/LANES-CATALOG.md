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
python3 source/tools/rvr_catalog_inputs.py release-world <world db> <scratch>/release-world.db
python3 source/tools/build_rvr_lanes.py --db <scratch>/release-world.db --navroot <folder holding navmesh/> \
    --tests-dll source/server/build/Tests/Debug/lib/Tests.dll --work <scratch> \
    --out source/server/GameServer/bots/autonomous/data/rvr-lanes.json --built <date> [--lock <flock file>]
```

- The database supplies only the positions in `Keep`, `Zones` and `Mob`.
- The script needs numpy and scipy and takes minutes per region.
- Raise `UT_RvrLaneSurvey.ToolVersion` when the walk changes.
- Pass `--built`: it is the only field that depends on the day. Two builds with the same inputs give the same bytes.

## Release world (2026-11-17)

The 1.2 file was rebuilt from a release world: the static tables of the test world with
`release-world-defaults.json` applied (`rvr_catalog_inputs.py release-world`, as `build_release_037.py` does). That
reset 21 keep owners or levels and 76 door states. The builder reads none of those columns, and no saved-progress
table. `rvr_catalog_inputs.py digest` gives the same hashes for what it does read (33 keeps, 1,846 camp spawns,
42 zones) in the test world, the release copy and five other played worlds from 2026-11-06 to 2026-11-15.

Result: all 332 lanes, their points, lengths and proof hashes are unchanged; only `built` moved to 2026-11-17. Two
builds gave the same bytes. Tests only.

## How the server uses it (work package 5, tests only)

- **Load and check.** `RvrLaneCatalog` reads the embedded file on first use. About 20 seconds after the game loop
  starts, a background thread walks every lane (hops both ways) with `RvrKeepRoute.TryBuild`, the keep walker's own
  planner, in 8 ms slices with 50 ms pauses. A lane is turned off when a leg does not walk, its walk is more than 25%
  off the surveyed length, or an alternate comes within 2,000 units (80% of 2,500) of a lower lane over its middle
  half. A trip's lanes go live together, after that check; an unchecked lane is never used.
  Log: `RVR_LANE_REJECTED` per lane, then one `RVR_LANE_LOADED` line with validated/rejected counts per region and kind.
- **Choice.** `RvrLaneChoice` picks a march's lanes once, from the objective within 8,000 units of the march leader
  to the target keep: the trunk, then up to three lanes from the gate. It leaves out a lane that passes within 4,000
  units of a keep an enemy of the marching realm holds now (not the goal), or crosses one of the
  `AutonomousArmyDetour` hazards before the siege camp. Groups split 50/25/25 by head count (67/33 over two lanes).
  The leader's group takes lane 0. A Casual, Average or Veteran group stays with the main body instead of its flank
  lane 15%, 8% or 3% of the time. Each group keeps its lane for the whole march.
  Log: `RVR_LANE_ASSIGN` per group, `RVR_LANE_FALLBACK` when the march takes today's route.
- **Pace.** The march leader keeps the pace. A group on another lane compares its share of its own lane walked with
  his share. It waits when ahead by 1,000 / 1,500 / 2,500 units (Veteran / Average / Casual) and walks on while he
  waits when behind by as much. The leader waits up to 45 seconds for a flank lane 3,000 units behind, then marches
  at least 45 seconds before he waits again. The march still ends at the camp ring, 11,500 units from the keep, where
  the existing arrival hold gathers the column. Tests bound the extra arrival spread at 60 seconds.
- **Roamers.** `RvrLaneChoice.ForRoam` (switch `rvr_roam_lanes`) is the hook for work package 6. A roaming group
  between two catalog objectives gets a lane fixed by its key, and `UseLaneTravel` hands it to the keep walker.
