# Route give-ups: why bots give up a route, and the goal fixes

Ported from [stefanrows/OfflineDAoC](https://github.com/stefanrows/OfflineDAoC) `docs/NAVMESH_HOTSPOTS.md`
(their versions 0.314–0.328). Their measurements are summarized here as the evidence for the port; the
numbers are **theirs**, from their meshes, save and Camlann world. Living Server has not been measured yet.

## What they found

stefanrows probed all 1,307 `AUTONOMOUS_ROUTE_RECOVERY` give-ups of one evening (2026-10-23) against the
installed meshes with the server's own Detour code, read-only. Most give-ups were not mesh defects:

| Cause | Give-ups | Fix |
| --- | --- | --- |
| Goal more than the server's 32/32/64 pick box from any polygon (e.g. 210 units above the floor) | 421 | Snap the goal to its floor |
| Goal behind a keep gate the bot may not pass (DF portals in held keeps, PvP targets on keep walls) | 440 | Don't offer the goal |
| Bot itself off the mesh | 100 | Floor step (Living Server already had one) |
| Start cut off, one-way seams, dead-end detours, cross-zone | ~346 | Not fixed here (road fallback, probes) |

The worst single loop: six bots re-picked one off-mesh goal next to Druim Ligen every ~22 seconds for
ten minutes, and each round trip went through the capital relocation and back by horse.

Their meshes differ byte for byte from ours, and Living Server's keeps follow realm ownership, not Camlann
guild claims, so the counts here do not carry over. Their conclusion does: **do not replace navmeshes
for these hotspots**; fix the goals.

Their measured effect (offline log analysis on their server, no real-client check): route give-ups per
game-hour 364 → 157 (0.314) → 122 (0.320), give-ups per 1,000 goal attempts 69.8 → 21.7, longest
same-bot same-spot loop 50 → 3.

## What Living Server took

All in `bots/autonomous/AutonomousRouteGoalFix.cs`, `AutonomousKeepGateGoals.cs` and their hooks in
`AutonomousWorldBotController`. Autonomous gamebots only; companions and players are untouched.

1. **Goal snap.** Only after a path to the goal failed with NoPathFound: if the goal (or its validated
   continuation) lies outside the normal pick box, it moves to the nearest floor of its own zone (boxes
   96/96/512, then 256/256/1024, never larger). Dungeons, precise arrivals (crossings, boarding,
   formation points) and goals on the mesh are never touched. A snap that moves the goal more than 256
   units in height within 3,000 units of the bot must be walkable from the bot. A goal with no floor,
   or whose floor failed again, is not snapped a second time. Log: `AUTONOMOUS_GOAL_SNAP`,
   `AUTONOMOUS_GOAL_SNAP_REJECTED` (at most 30 lines a minute).
2. **Foreign failures.** A NoPathFound more than 96 units from the route's goal, at a point off the mesh,
   came from another mover (a threat detour, a retreat point). It is set aside once instead of ending the
   route.
3. **Keep gates (adapted).** A PvP target standing in the courtyard of another realm's keep with an
   outer gate still closed is not offered as an RvR roam goal. Living Server rule: a bot passes the gates
   of keeps its own realm owns, as `GameKeepDoor.TryTraverse` does; stefanrows' Camlann guild/alliance
   rule is not used. A broken outer gate opens the keep, the keep the bot's own force is sieging keeps
   its targets, and fighters outside the walls stay targets. The outer gate ring is the farthest gate,
   not the farthest door (a postern can stand farther out than the main gate). Keep, siege and relic
   objectives are not filtered; they have their own rules.
   Living Server bots already enter Darkness Falls only through their home entrance
   (`AutonomousDarknessFallsPolicy`), never through a relic keep's portal, so stefanrows' DF-entrance
   gate check is not needed.
4. **Give-up memory.** A route give-up blocks its spot (200 units) for 6 minutes, for the bot and, when
   the warband leader gave up, for the warband. RvR roam picks (enemy hunts, frontier patrol camps), PvE
   camp choice and zone crossings skip it; a crossing that was the only route is still used. One entry
   per give-up, nothing per tick; pickers check a cheap "any entry" flag first.
5. **Capital relocation.** After repeated failures, a bot standing on connected ground whose goal is off
   the mesh is no longer sent to its capital (the goal, not the bot, is the problem). A bot in a listed
   walled-off pocket still escapes.
6. **Floor step budget.** Living Server's existing floor step (a bot standing off the mesh steps onto
   the nearest floor that leads on) now runs at most once per 45 s and twice per destination, only for a
   bot that isn't fighting, casting or riding, never from a spot where it just found nothing, and never
   into a keep courtyard the bot is gated out of. Each search costs up to ~50 navmesh queries, so this
   cuts repeated failed searches (open issue #25 is about such stalls).

Counters: one `AUTONOMOUS_ROUTE_GOAL_FIX` line a minute when anything happened (`snapped`,
`snapFailed`, `snapDzRejected`, `snapRefailed`, `foreignFailureSkipped`, `gatedSkipped`, `repickBlocked`,
`crossingSteered`, `crossingFallback`, `campRepickBlocked`, `pocketEscapeSkipped`, `floorStepHeld`,
`floorStepGated`).

## Not taken

- The road fallback (`AutonomousZoneRoads`, `roads.json`): a bigger change with its own data file and
  deploy steps; it fixes dead-end detours only.
- Their probe fixture (`UT_Task139HotspotProbe`) reads their install and save.
- Their DF-entrance keep check and Camlann keep-pass rule (see 3).

## What to check in game

- `AUTONOMOUS_ROUTE_GOAL_FIX` counts and `AUTONOMOUS_ROUTE_RECOVERY` give-ups before and after, at the
  same bot count.
- No bot snapped onto a wall top or another floor (`AUTONOMOUS_GOAL_SNAP` lines with a large `dz`).
- Warbands still attack keeps they siege, and still fight enemies outside keep walls.
