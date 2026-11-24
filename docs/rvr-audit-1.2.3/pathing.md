# RvR audit 8: frontier pathing and movement in a fight

Agent: pathing (issue #500). Report only; no game behaviour changes. Built on commit `de53d0d3` (origin/main,
2026-11-24). Judged against the rubric in `reference.md` (PR for #493), sections 1.3-1.4, 3.6-3.7, 6, 9.2 and the
tester's checklist (11). The structure report (`structure.md`, #494) already names the class "many hands move a bot's
legs" (its finding 9) and proposes one movement arbiter (piece E); the evidence report (`evidence.md`, #495) already
shows the route tests run on a fake mesh (its F7). I cite those and do not repeat them. The realism movement audit
(`../realism-audit-1.2.3/movement.md`) covers idle bodies, the formation ring, the identical road track (its tell 10)
and the hidden catch-up speed (tell 14); I do not repeat those either.

**Evidence tags.** [read] code or data read here; [unit] a unit test exists; [sim] a script or test on the real mesh
data, no world; [log] a server log line someone recorded; [played] seen on the owner's PC. Nothing in this report was
played or run by me. No dotnet was run; no navmesh was available here.

**What real mesh data the repo holds** (all from the 1.2 / v0.36b download, the meshes 1.2 ships):
- `tools/navmesh/navmesh-drops.json`: a sha256 and every off-mesh link and sheer step for every zone, including all
  twelve frontier zones and the four battleground zones (250-253).
- `source/server/GameServer/bots/autonomous/data/rvr-lanes.json`: 149 planner walks (lane 0) and 183 alternates between
  45 frontier objectives. Its mesh hashes for zones 11, 12, 14, 15, 111, 112, 113, 115, 210, 211, 212, 214 are the same
  as the 1.2 hashes in `navmesh-drops.json` (checked here). So the survey ran on the meshes players have.
- `tools/navmesh/seams-region200-v036b.json.gz`: the zone seam lines of region 200 (Hibernia) only.

## The ten findings, worst first

### 1. In most RvR fights melee bots stand on one spot: they do not circle, flank or step out of each other
- **What a player sees.** A level 50 solo meets an enemy bot, or watches a keep fight: the melee bots run in, stop at
  sword reach and stand there, facing their target, until it dies. Two or three bots on one target stand inside each
  other. A bot never walks round a player to land a back or side style. In a bot 8-man in the open field, only some
  melee strafe now and then; in a keep fight, a siege, a battleground assault, any solo fight, or any group with a human
  in it, none do.
- **Chain link.** Fight → move. The PvE habits (circle, flank, crowd step, caster side step) are switched off against
  players and bots, and the RvR habit only runs in one path.
- **Where.**
  - PvE habits require a monster target: `BotCombatMovement.cs:79-80` (`monsterTarget`), passed as
    `target is GameNPC and not GameBot` (`BotBrain.CombatMovement.cs:98-101`). The flank step refuses players and bots
    (`BotBrain.Flank.cs:134`). So the crowd step (`CrowdSide`, 28 units) never runs in RvR.
  - The RvR strafe (`BotBrain.MovementHabits.cs:115-185`) is called only from the open-field positioning turn
    (`BotBrain.RvrPositioning.cs:197`), which returns at once for a bot without a group of two or more
    (`BotBrain.RvrPositioning.cs:120`), and only where `RvrTacticsApplyTo` holds (`BotBrain.RvrTactics.cs:51-54`):
    not for a group a player leads or is in, a realm raid, a battleground assault, a committed keep defender or siege
    fighter, a siege operator or a bot at its objective (`RvrTacticsScope.cs:66-89`).
  - Where it runs, a bot strafes in 20% (Casual), 50% (Average) or 85% (Veteran) of fights, every 3-8 s, 30-66 units of
    arc, at most 60 degrees, and never while either side is moving (`BotMovementHabits.cs:62-71, 90-94`). A melee bot
    that is not strafing this fight stands still for the whole fight.
- **How often.** Computed from the code: every keep and siege fight, every battleground storm, every solo or duo fight,
  every fight of a group with the player in it: no movement at all. In open-field bot 8-man fights, 15-80% of melee
  bots (by tier) stand still all fight. A player in the frontier sees one of these in nearly every fight.
- **Rubric.** 6 "Passes: melee stick and circle"; 3.6, 6 "circle-strafe so positional styles fail; melee chase the
  target round and round". Fails.
- **Evidence.** [read]; the strafe rules have unit tests [unit]; strafe lines fired in a played log but were not watched
  (`evidence.md` row 42) [log].
- **Class.** A rule added per path: PvE movement and RvR movement are two engines, each with its own scope, and the
  joins (solo RvR, keep fights, groups with a player) fall between them. Belongs in the movement arbiter of
  `structure.md` piece E: one "melee contact" behaviour for any enemy, with the RvR scope only choosing its numbers.
- **Fix.** Let the flank rule and the crowd step apply to enemy players and bots for every autonomous gamebot in the
  frontier (not companions, not a player's group), with a per-tier share; let a strafe chain several arcs toward the
  target's back over a few seconds instead of one 60-degree arc. Size M. Risk M: more path requests in big fights (the
  leg budget in `BotMovementHabitsRuntime` already caps it); a keep's courtyard and stairs need the leg proof that
  already exists.

### 2. A beaten group runs in a straight line, not to its guards, and after one route it walks straight through walls
- **What a player sees.** His group beats an enemy bot group. The losers sprint off together in one straight line,
  often away from their own keep, into a lake or at a cliff face, sometimes past the winners' keep. After the first
  route they walk straight on, through rocks and fences. After 20 s they stop.
- **Chain link.** Lose → what next.
- **Where.**
  - RvR retreat point: the realm's bindstone if one is within 4,000 and away from the enemy, else 3,000 units straight
    away from the enemy centre (`RvrEngagementRuntime.cs:251-256`, `BotGroupRetreat.cs:97-148`). No friendly keep,
    portal keep, milegate or guard is considered; the point is not checked on the mesh.
  - One route request per run, a second for a party member that stopped short, then `WalkTo` "straight on"
    (`BotBrain.Retreat.cs:244-282`). The run lasts at most 20 s (`BotGroupRetreat.cs:28`).
  - The escape actives are not used: "Living Server's gamebots don't use those actives" (Vanish, Speed of Sound;
    `BotBrain.Retreat.cs:247`).
  - The solo bail uses the same point rule (`BotBrain.Retreat.cs:131-162`).
- **How often.** Every RvR retreat (`RVR_RETREAT`), which the plan calls on bad odds or a lost fight. In the frontier
  bindstones are few, so the straight-away point is the usual one (a guess; the `to=` field of `RVR_RETREAT` settles it).
- **Rubric.** 3.7 "kill and then run like hell to somewhere else"; 1.3 "do not chase enemy to Alb TK... you WILL die"
  (people ran to their guards). Fails.
- **Evidence.** [read]; `BotGroupRetreat` rules [unit].
- **Class.** A PvE rule (run from a monster pack) reused for RvR without the RvR facts (keeps, guards, enemy groups).
- **Fix.** RvR retreat point = the nearest friendly keep, tower guards, portal keep or own milegate the planner reaches,
  else the way the group came (the field positions already record it), checked with `RvrKeepRoute.TryBuild`; keep path
  requests every 3 s instead of the straight walk; Speed of Sound and Vanish for bots that own them. Size S-M. Risk L.

### 3. Bots teleport and snap in the frontier, and none of it checks whether a player is watching
- **What a player sees.** A bot that trailed its group appears beside its leader; a bot by the portal keep vanishes and
  is standing 4,000 units away; a bot that fell off the mesh vanishes to its capital in the middle of a fight. Each is
  in plain sight of anyone near.
- **Chain link.** Move → no path. The fallbacks are moves, not walks.
- **Where (every in-region move or snap an RvR gamebot can make).**

| Move | Distance | Trigger | Where | Log tag |
|---|---|---|---|---|
| Group member to its leader | any (>600, same region) | no progress 45 s, still stalled 3 min | `AutonomousGroupMemberStall.cs:21-24, 232-246` | `AUTONOMOUS_GROUP_MEMBER_RECOVERED` |
| Keep route restart at the arrival point / muster | 150-4,000 | 2 route failures near the arrival | `AutonomousWorldBotController.KeepTravel.cs:241-262`, `AutonomousRvrMusterPost.cs:82-86, 99-122` | `RVR_KEEP_ROUTE_RESTART` |
| Floor step | up to 300 | a route fails from just off the mesh | `AutonomousWorldBotController.cs:5873-5882`, called at `:5426` | `AUTONOMOUS_FLOOR_STEP` |
| Listed pocket escape | up to 600 | standing in `pockets.json` | `AutonomousWorldBotController.cs:347-355` | |
| Route pocket short hops, then capital | hop, then region change | 3 terminal route failures in one spot | `AutonomousWorldBotController.cs:5601-5720`, `AutonomousRouteRecoveryPolicy.cs:19` | `AUTONOMOUS_ROUTE_POCKET_ESCAPE` |
| Invalid position to capital | region change | no mesh within 768 (at once, even in a fight); 96+ off the floor and 15 min still | `AutonomousStuckWatchdog.cs:336-354, 719-752` | `AUTONOMOUS_POSITION_RECOVERY` |
| 15-minute still to capital | region change | no 96-unit move in 15 min | `AutonomousStuckWatchdog.cs:427-433` | `AUTONOMOUS_STUCK_15M_RECOVERY` |
| Nav-link landing | link length | stopped inside an off-mesh link | `NpcMovementComponent.cs:991-1005` | `NAV_LINK_LANDING` |
| Pet to its owner's feet | any | no path, both out of combat (base server) | `NpcMovementComponent.cs:1073-1076` | |
| Keep door pass | 100-150 | own realm's door on the route | `GameKeepDoor.cs:288-340`, `AutonomousRvrTravel.cs:11-30`, `Pathfinder.cs:316-331` | |
| Porter arrival | region change | ceremony | `AutonomousFrontierTransport.cs:315-333` | `RVR_FRONTIER_DEPARTURE` |

  The last two are what a player does too (clicking a door, the porting ceremony): fair. The rest have no watcher test;
  the only one in the bot code is the stable-travel hide check (`TemporaryGroupStableTravel.cs:293`).
- **How often.** Unknown; no hourly roll-up exists (`evidence.md`, "Where bots got stuck"). The owner's PC run on
  `43c43aa` saw Odin's Gate muster posts send bots to their capital (`structure.md` finding 9) [played].
- **Rubric.** Section 11 is silent on teleports; the brief's test is "passes for the real one ... fought by people".
  A person never blinks. Fails wherever seen.
- **Evidence.** [read]; movement.md listed the watchdog's `MoveTo` calls as unchecked; this is the full RvR list.
- **Class.** Fallbacks that are seen (`structure.md` finding 9).
- **Fix.** One `RvrTeleport` gate: a non-urgent move (member to leader, restart, short hop) waits until no player of
  any realm is within visibility range of either end, and is logged as `RVR_TELEPORT` with the distance (the tag
  `structure.md` proposes). The member-to-leader move becomes a walk with the leader waiting, where a route exists.
  Size S-M. Risk L-M (a bot may stay stuck longer near a player; the watchdog still catches it).

### 4. With no path, a bot stands still: a melee bot under a ledge for 90 s, a siege bot for up to two minutes
- **What a player sees.** He jumps onto a rock or a cart, or stands on the milegate's edge, less than 150 units up.
  The enemy melee bot below stops dead and stands, facing him, for 90 s. A siege bot on its way stands in the road with
  "Keep route retry" for 30 s to 2 minutes.
- **Chain link.** Fight → move; move → no path.
- **Where.**
  - An autonomous gamebot with no path, or at the end of a partial one, pauses (`NpcMovementComponent.cs:918-923,
    1049-1058`); other NPCs jump to the nearest reachable node, bots never do (owner's rule after 2026-10-07).
  - The "unreachable target" rule drops only a target 150 or more above the bot (`AutonomousWorldBotController.FrontierThreat.cs:85-93`);
    anything lower waits for the stalemate rule: no health lost and no 150-unit move for 90 s
    (`AutonomousWorldBotController.FrontierThreat.cs:18-19, 26-55`).
  - Keep route failure: stand, retry after 30, 60, 90, 120 s plus up to 3 s (`AutonomousWorldBotController.KeepTravel.cs:176-187`);
    three stalls of a minute at one spot: two more minutes (`AutonomousKeepTravelStallPolicy.cs:245-251`).
- **How often.** Each time a player stands where the mesh has no link (rocks, carts, wall edges, keep stairs out of
  the mesh). It is also an exploit: a player can make one melee bot harmless for 90 s.
- **Rubric.** 3.7 and 6: a person who cannot reach goes round, switches target or leaves. Fails.
- **Evidence.** [read].
- **Class.** No owner of "what next when the legs fail".
- **Fix.** When the mover pauses on a PvP target, give up after two failed checks 2-4 s apart at any height (the
  unreachable rule without its 150-unit floor), then take the next target or a ranged weapon; keep route retries walk to
  the last reachable route point instead of standing. Size S. Risk L.

### 5. Casters and healers stand too close, in the open, and never use cover
- **What a player sees.** An enemy bot group fights his. Their casters stand 250-700 units behind their own tanks, in
  the open, so a player melee reaches them in 2-4 s. Nobody steps behind a rock, a wall or the milegate's edge.
  Healers back off 220-330 units and come back.
- **Chain link.** Fight → move (positioning).
- **Where.** Backline depth 250-700 behind the tanks' line (`RvrPositioning.cs:123-125`); healer kite 220-330
  (`RvrPositioning.cs:151-153`); no line-of-sight or cover term anywhere in the placement (`RvrPositioning.cs`,
  `BotBrain.RvrPositioning.cs`: no `HasLineOfSight` call). Only the PvE caster side step checks sight
  (`BotBrain.CombatMovement.cs:207-212`), and it never runs in RvR (finding 1).
- **How often.** Every open-field bot group fight; keep and siege fights have no placement at all (finding 1).
- **Rubric.** 6 "casters stand at 1,200-1,500 ... use rocks, walls and the milegate's edges to break line of sight";
  3.6 healers "behind the tanks and kite". "Fails: nobody using cover." Partly passes (behind and spread, 350 apart),
  fails on depth and cover.
- **Evidence.** [read]; `RvrPositioning` layout rules [unit].
- **Class.** A rule with numbers below the period and one missing input (sight).
- **Fix.** Depth from the caster's own spell range (range minus 100-300, so 1,200-1,500 for a nuker), and a cover pick
  for healers: of the proven slots, prefer one that has sight of the friendly tank and none of the nearest enemy caster.
  Size M. Risk M: deeper casters take longer walks (`MaxWalk` 900) and may lose sight on hills; the existing LOS ray
  budget applies.

### 6. Midgard's walk from its gate to the Albion front keep is twice the straight line; other trips detour too
- **What a player sees.** Midgard's army leaves the Hadrian's Wall east milegate for Caer Benowyc, 22,800 units to the
  north, and walks 43,550 (about 4 minutes at run speed instead of 2). Albion's defenders meet it from the side.
- **Chain link.** Decide → move (route).
- **Where.** Lane 0 (the planner's own route) in `rvr-lanes.json`, ratio walked / straight, the worst:

| From | To | Walked | Straight | Ratio | Why (my reading) |
|---|---|---|---|---|---|
| Hadrian's Wall east milegate | Caer Benowyc | 43,550 | 22,783 | 1.91 | both in zone 15: a mesh path inside one zone. Terrain or a gap: PC check P3 |
| Hadrian's Wall west milegate | Caer Hurbury | 113,006 | 64,503 | 1.75 | no zone covers x 565,248-598,016 south of y 352,256: the walk must go north through Pennine. Geography |
| Hadrian's Wall west milegate | Castle Myrddin | 165,234 | 101,859 | 1.62 | same |
| Druim Ligen | Dun Scathaig | 141,418 | 82,167 | 1.72 | home lane; PC check |
| Dun nGed | Dun Ailinne | 39,005 | 23,951 | 1.63 | keep hop; PC check |
| Dun Crauchon | Dun Bolg | 78,337 | 49,234 | 1.59 | keep hop; PC check |
| Odin's Gate north milegate | Bledmeer Faste | 33,230 | 22,738 | 1.46 | Hibernia's walk to Midgard's front keep |
| Caer Benowyc | Caer Erasleigh | 53,033 | 36,272 | 1.46 | keep hop |

  Whole portal-keep trips (trunk plus gate lane): Hibernia's portal keep in Hadrian's Wall to Caer Hurbury 1.97 and
  Castle Myrddin 1.98 (12 and 17 minutes on foot); Midgard's to Caer Benowyc 1.44. Every other portal-keep trip is
  1.06-1.37. Also: 17 of 165 main routes swim more than 1,000 units (worst 15,550) and six use jump links
  (`docs/research/rvr-field-1.2/LANES-FINDINGS.md:104-105`).
- **Can't be planned:** none. Lane 0 completed both ways on all 149 catalog trips (`LANES-CATALOG.md:31-46`) [sim].
  Not covered: relic keeps from their own border keeps, keep pairs over 60,000 apart, live door states
  (`LANES-CATALOG.md:48-56`).
- **Rubric.** 6 "Fails: bots moving in straight lines that ignore terrain" is the opposite risk; here the question is
  whether a person would take the same way. A period army took the road. Only the PC can say whether the 43,550 is the
  road.
- **Evidence.** [sim] (the survey ran the server's planner on the 1.2 meshes, world-less); ratios computed here.
- **Class.** One-offs on the mesh, not a code class. The planner also never re-enters a zone it left and stops at 24
  zones (`RvrKeepRoute.cs:37`), which can force a long way where a road dips out of a zone and back (none of the trips
  above needs it, by the zone rectangles).
- **Fix.** None until P3 says whether Detour's path is the road or a mesh gap; a gap is a hash-guarded content patch
  for that spot (AGENTS.md), not a rebuild. Size S each. Risk L.

### 7. Odin's Gate's Hibernian exit is a gap with no gate, and no bot treats it as a milegate
- **What a player sees.** Hibernians leave their portal keep in Odin's Gate through an opening at 623500,584575 with no
  doors. No Midgard bot camps it. The Albion milegate in the same zone has a chokepoint spot; the Hibernian one has none.
- **Chain link.** Map → where the fighting is.
- **Where.** The lane survey found Hibernia's portal keep exits only through "Odin north gate 623500,584575 (no Door
  rows)" (`LANES-FINDINGS.md:44`). The check-spot catalog lists one Odin's Gate chokepoint, `og-amg`; Hibernia's
  circuit starts at the road hastener (`rvr-checkspots.json`, region 100). The rubric's question (9.2: five milegates,
  not six) is answered in part: the mesh has the sixth opening; the world has no door there.
- **How often.** Every Hibernian arrival in Odin's Gate; that frontier's second chokepoint is never fought over.
- **Rubric.** 1.4 "a milegate is the one gap every arrival must pass"; 9.2. Fails on the Hibernian side of Odin's Gate.
- **Evidence.** [sim] for the opening (survey on the 1.2 mesh); [read] for the missing doors and spot.
- **Class.** World data gap.
- **Fix.** If the client shows a gate structure there (P4), add the door rows and an `og-hmg` chokepoint spot. Size S.
  Risk L. If the client shows no gate, record that Odin's Gate has one milegate in this world.

### 8. Vertical lifts by the Hibernian relic keeps and in tower stairs: a bot floats up a wall
- **What a player sees.** Near Dun Dagda or Dun Lamfhota a bot rises 416 units in a straight line, as if on a rope, or
  climbs a tower in three 300-unit jumps. A player cannot follow it.
- **Chain link.** Move (mesh links).
- **Where.** `navmesh-drops.json` (1.2 meshes) lists the frontier's stuck-prone links:
  - Mount Collory (210), 8 lifts of 416 units, 960-1,770 from Dun Dagda (around 399,500-402,100 / 464,200-466,800);
    Cruachan Gorge (211), 12 lifts of 413-416, 1,110-1,630 from Dun Lamfhota (around 347,600-349,500 /
    371,900-374,400). Most are the only way between their two floors (`onlyWay`).
  - Chains of three 265-333 lifts (tower stairs, by their shape) in Forest Sauvage at 581,900-582,100 / 473,200-473,400
    and 587,300-587,500 / 473,400-473,700 (about 4,500 from Castle Sauvage) and 594,900-595,200 / 449,400-449,700;
    in Snowdonia at 531,250-531,460 / 335,700-335,870.
  - None in Midgard's four zones, Hadrian's Wall, Breifine, Emain or the battlegrounds; no frontier zone has a one-way
    link (only Darkness Falls does).
  - A bot walks a lift in a straight line between its ends (`docs/NAVMESH-DROPS.md:14`); `NavLinkLeg` keeps it from
    stopping inside one.
- **How often.** Whenever a route uses one: relic raids on Hibernia, and bots by Castle Sauvage.
- **Rubric.** 1.2: a keep is entered through its doors; climbing at a few fixed points is period, walls of lifts are not.
- **Evidence.** [sim] for the links (real scan); what the client shows is PC only (P5).
- **Class.** Mesh one-offs.
- **Fix.** After P5: if a lift is a stair, nothing; if it is a cliff no player can climb, a filter for those link ids in
  RvR planning (no mesh change). Size S. Risk L.

### 9. The fallback route ignores enemy keeps, and only red monsters are steered round
- **What a player sees.** A small group of enemy bots walks past his realm's keep within its guards' reach and is
  shot to pieces, or a column walks along the same strip under a keep every time.
- **Chain link.** Decide → move.
- **Where.** A lane passing an enemy-held keep within 4,000 is dropped (`RvrLaneChoice.cs:208-224`); when every lane is
  dropped the walk falls back to the plain planner route (`AutonomousWorldBotController.KeepTravel.cs:144-148`), which
  knows no keeps. Only the march leader detours round keeps (`AutonomousArmyDetour.cs:1-15`, used at
  `AutonomousWorldBotController.Rally.cs:241`). Dynamic danger steering counts only monsters that con red or worse
  (`AutonomousThreatAwarePathing.cs:79-90`), so keep guards, enemy bots and players are never steered round. 21 of 165
  main routes pass within 4,000 of a third keep (`LANES-FINDINGS.md:97-98`). A logged run counted 980 deaths in one
  strip short of Caer Erasleigh and 965 more within 4,000 of the keep (`LANES-FINDINGS.md:77-93`) [log].
- **How often.** Every roam or small force without a march leader whose lanes are all rejected.
- **Rubric.** 3.7 "chasing into enemy guards" is a mocked mistake: some is fine, every time is not.
- **Evidence.** [read], [log] for the death counts.
- **Class.** The same rule (avoid enemy keeps) in one path only (the march), not in the travel layer.
- **Fix.** Use the march's detour point for any RvR keep walk whose lanes all failed on `KeepHeld`. Size S. Risk L.

### 10. Every arrival lands on one point, and a force walks one line through one gap
- **What a player sees.** At his realm's portal keep in Emain, a ceremony drops twenty bots on the same spot; they then
  file out along the same line, single file, through the milegate.
- **Chain link.** Arrive → move.
- **Where.** Every passenger is moved to the passage's one point (`AutonomousFrontierTransport.cs:194-206, 329`).
  Every bot of a force follows the same route points (`RvrKeepRoute.cs`); lanes split a force into at most three,
  weighted 2:1:1 (`RvrLaneChoice.cs:128`), and all merge at the gate, about 200 units wide (`LANES-FINDINGS.md:67-73`).
  The only sideways spreads are ±600 at 2,400 ahead on long hunting legs (`AutonomousRvrTravel.cs:36-55`) and ±480 at
  an enemy gate (`AutonomousWorldBotController.cs:2491`).
- **How often.** Every ceremony (about every 2 minutes per porter, `AutonomousFrontierTransport.cs:81-84`) and every
  march.
- **Rubric.** 1.3 waves: pass. 6 "a zerg is a slow column that bunches at gates": pass; "a group runs in a loose line,
  not a block": pass on the road (the /stick line), fail at the arrival point. Players also landed on the pad together,
  so the stack itself is period [M]; one line for eighty is not.
- **Evidence.** [read].
- **Class.** Deterministic pathing (movement.md tell 10).
- **Fix.** Land each passenger at `WaitingOffset` round the arrival (the same proven offsets the porter queue uses,
  `AutonomousFrontierTransport.cs:109-136`); movement.md's per-bot lane shift, outdoors only. Size S. Risk L.

## Checked and found sound

- **Waves, not a trickle.** Porter ceremonies batch passengers, a 30 s boarding window, three priority passengers to one
  ordinary, a warband held up to 60 s (`FrontierBoardingQueue.cs`, `AutonomousFrontierTransport.cs:66-95`). Matches
  rubric 1.3. [read], [unit]
- **Every frontier and battleground zone is meshed.** All twelve frontier zones and zones 250-253 have a hashed mesh in
  `navmesh-drops.json` [sim]. The survey and the shipped meshes are the same files (hashes match).
- **Hibernia's frontier joins up.** Region 200 seams join main ground to main ground: Mount Collory/Breifine 30,400
  units of border, Cruachan/Breifine 31,232, Breifine/Emain 15,360; Mount Collory and Cruachan share no border. The
  home side meets the frontier at Bri Leith/Collory (2,944) and Connacht/Cruachan (3,520). Computed from
  `seams-region200-v036b.json.gz` [sim].
- **Every catalog trip plans, both ways.** 149 trips, lane 0 complete both ways; no lane is one-way
  (`LANES-CATALOG.md:31-46`) [sim, world-less].
- **Doors as a player uses them.** A bot passes only its own realm's keep doors, by the same traverse a player's click
  does (`GameKeepDoor.cs:288-294`); shut enemy doors block planning, and the six relic gates missing from the mesh are
  checked against the walk (`AutonomousKeepApproachNavigation.cs:319-376`). [read], [unit]
- **Chasing.** A fleeing enemy is chased 1,500-2,500 from the group by tier, 2.5 times as far by a bold bot, into the
  guards (`RvrEngagement.cs:258-261`): the period mistake at a period rate. [read], [unit]
- **Speed.** Run speed is the player's; speed songs are kept up while travelling and while running
  (`BotBrain.cs:2266-2267`, `BotBrain.Retreat.cs:284`). The hidden catch-up bonus is movement.md tell 14.
- **Horses.** A bot rides only its own realm's stablemasters (`AutonomousStableRoutePlanner`), so no horse goes into an
  enemy frontier (rubric 6). Whether any frontier stable route exists in the database was not checked.
- **Seams.** The #421 rule is on main (`AutonomousZoneItinerary.cs:410-513`); on the 1.2 seam data the sim went from 10
  cycles in 72 trips to none (`tools/navmesh/README.md`, "Lough Derg") [sim]. #421 stays open for a PC log.
- **Long legs and planning cost.** Legs over 6,000 are walked in 4,000-unit proven pieces; planning runs in 8 ms slices,
  at most 8,192 queries and 120 s of work (`AutonomousKeepTravelStallPolicy.cs`, `RvrPlanningNavigation.cs:155-161`).
- **Collision.** Bots pass through each other; so did players in 2003 [M]. Not a defect by itself (finding 1 is about
  standing on one point, not about overlap).

## Could not check, and why

- **Anything on screen**: how the client draws a 22-unit sideways leg, a 416-unit lift, a bot's heading during a
  strafe, swimming. No client.
- **Albion's and Midgard's seams** (only region 200's seam file is in the repo), **keep interiors** (courtyards, inner
  doors, stairs, wall walks, lord rooms), **islands and dead pockets** other than the lifts, **water** depth: these need
  the `.nav` files.
- **Line of sight on casting.** Bots cast through the base server's checks; I did not trace what a caster does when
  the server refuses a cast for sight in RvR.
- **The frontier/home border.** An enemy bot stops dead at a home-zone line (`NpcMovementComponent.cs:911-915`,
  `AutonomousWorldBotController.cs:6254-6269`). Whether a fleeing player can cross there on foot outside a border keep
  (the 2,944- and 3,520-unit main-ground strips above) needs the mesh and the client.
- **Fifty bots through a milegate**, tier mix of groups, how often any fallback in finding 3 fires: no PC log here.

## What only the PC can settle

All read-only. Build the probe once: `D=source/server/Pathing/Detour; g++ -O2 -std=c++17 -DDT_POLYREF64 -I$D/Include
tools/navmesh/dfprobe.cpp $D/Source/*.cpp -o dfprobe` (`tools/navmesh/README.md`). `NAV` = the install's
`runtime/server/navmesh`. Tests: `OFFLINE_DAOC_NAV_ROOT=<install>/runtime/server OFFLINE_DAOC_DB_PATH=<install>/runtime/data/opendaoc.sqlite3.db
dotnet test source/server/Tests --filter FullyQualifiedName~<name>` (they are `[Explicit]`; run one at a time).

| # | Check | How | What settles it |
|---|---|---|---|
| P1 | Same meshes as this report | `sha256sum NAV/zone0{11,12,14,15}.nav NAV/zone11{1,2,3,5}.nav NAV/zone21{0,1,2,4}.nav NAV/zone25{0,1,2,3}.nav` | All equal the `navmesh-drops.json` hashes; else every [sim] claim here is for another mesh |
| P2 | Keeps connected inside (gates, courtyard, inner door, stairs, lord room) | `UT_InstalledKeepRoutes`, `UT_KeepDoorNavmeshProbe` (needs `OFFLINE_DAOC_DOOR_LIST` from the start-up log), `UT_SiegeFiringPositions` | Every classic keep: an entrance and its interior floors reached; list any that fail |
| P3 | Midgard's 43,550 walk to Benowyc | `dfprobe NAV/zone015.nav path 651796 321126 4314 653312 343859 5811`, then `column 651796 321126 653312 343859 64 3000 8000`; in game, run it with `/loc` | Detour's path equals the road: sound. A short straight line blocked by one missing step: a spot patch |
| P4 | Odin's Gate Hib exit | Stand at 623500,584575 (zone 115); `dfprobe NAV/zone115.nav column 623300 584575 623700 584575 16 4000 8000` | A gate building with no doors: add door rows and `og-hmg`. Open hillside: Odin's Gate has one milegate |
| P5 | Lifts | Watch at 400,700,464,200 (Mount Collory) and 348,500,372,200 (Cruachan); `dfprobe NAV/zone210.nav offmesh 399000 463500 402500 467500` and `NAV/zone211.nav offmesh 347000 371500 350000 374800`; same at the Forest Sauvage and Snowdonia boxes in finding 8 | A stair a player walks: fine. A cliff: filter those links for RvR |
| P6 | Albion and Midgard seams | Export region 1 and 100 seam lines as region 200's were; `python3 tools/navmesh/seam_route_sim.py --region 1 --nav NAV --zones ... --seams ... --roads ... --start ... --goal ...` from each portal keep and border keep to each keep | No `AUTONOMOUS_ZONE_SEAM_CYCLE` in the sim |
| P7 | Frontier/home border | `dfprobe NAV/zone206.nav border ...` on y 483,328 and `zone207.nav` on y 417,792 (the 206/210 and 207/211 lines), compared with Druim Cain's and Druim Ligen's walls | The walkable strips lie inside the keeps: fine. Outside: a player can flee where bots freeze |
| P8 | Melee in a fight (finding 1) | Fight one enemy bot solo for 60 s, then watch a bot 8-man fight and a keep fight | Count moves; `BOT_MOVE_STRAFE` lines per fight against what you saw |
| P9 | Retreats (finding 2) | Beat an enemy bot group | `RVR_RETREAT to=`: where it points (keep, bindstone or straight away), and where they went |
| P10 | Teleports (finding 3) | One hour in Emain with `grep -E 'AUTONOMOUS_GROUP_MEMBER_RECOVERED|RVR_KEEP_ROUTE_RESTART|AUTONOMOUS_FLOOR_STEP|AUTONOMOUS_ROUTE_POCKET_ESCAPE|AUTONOMOUS_POSITION_RECOVERY|AUTONOMOUS_STUCK_15M_RECOVERY|NAV_LINK_LANDING'` on regions 1, 100, 200 | Count per hour, and how many `from=` were within 5,000 of where you stood |
| P11 | Statues (finding 4) | Stand on a rock about 100 up next to an enemy melee bot | Seconds until it leaves; `AUTONOMOUS_PVP_STALEMATE` / `AUTONOMOUS_UNREACHABLE_TARGET` |
| P12 | Fifty through a milegate | Emain south milegate 448205,340200 at a siege muster | The column's shape; `RVR_LANE_ASSIGN`, `RVR_LANE_FALLBACK`, `RVR_KEEP_TRAVEL_STALL` near the gate |
| P13 | Refresh the survey | `UT_RvrLaneSurvey`, `UT_RvrCheckSpotCatalog`, `UT_RvrRouteTrapProbe` | Same lane lengths as `rvr-lanes.json`; record the result in the repo (`evidence.md` F7: most have none) |

## Tools worth building

- **`RVR_TELEPORT`**: one line for every in-region move or snap of an RvR bot (the eleven rows in finding 3), with the
  distance and the nearest player's distance. An hourly count by kind is the "teleports per hour" number.
- **`RVR_MOVE_FIGHT`**: once per RvR fight per bot, seconds standing still in melee, strafes, flanks, back/side styles
  landed. Turns finding 1 into a measured share, before and after a fix.
- **A frontier mesh audit script** beside `scan_all_zones.py`: from `rvr-lanes.json`, every lane 0 with ratio over 1.4,
  swim over 1,000 or a lift, printed with `dfprobe path` and `column` lines ready to paste, so P3 and P5 are one run.

## Hand-back summary

1. Report only; docs only; nothing run, nothing played. Base `de53d0d3`. Navmeshes not here: claims on the mesh come
   from the committed 1.2 scans (`navmesh-drops.json`, `rvr-lanes.json`, region 200 seams), whose hashes match.
2. Worst: in most RvR fights melee bots stand still. PvE circle/flank/crowd step skip players and bots
   (`BotCombatMovement.cs:79-80`, `BotBrain.Flank.cs:134`); the RvR strafe runs only for bot groups in the open field,
   in 20-85% of fights (`BotBrain.RvrPositioning.cs:120,197`, `RvrTacticsScope.cs:66-89`). Keep fights, sieges, solos
   and groups with the player: no movement. Fix in the arbiter (`structure.md` E). M.
3. Beaten groups run 3,000 straight away, not to guards, then walk straight through walls; 20 s
   (`RvrEngagementRuntime.cs:251-256`, `BotBrain.Retreat.cs:281`). S-M.
4. Eleven teleports/snaps an RvR bot can make, none checks a watcher (table in finding 3). An `RVR_TELEPORT` gate. S-M.
5. No path = statue: 90 s under a low ledge (unreachable rule needs 150 up), 30-120 s "Keep route retry". S.
6. Backline 250-700 behind the tanks, no cover or sight in placement (`RvrPositioning.cs:123-125`). M.
7. Detours: Hadrian east milegate to Benowyc 1.91x (one zone: PC check P3); Hurbury/Myrddin from the west gate are
   geography (no zone south of Pennine there). No catalog trip fails to plan.
8. Odin's Gate's Hibernian exit (623500,584575) is a doorless gap with no chokepoint spot: answers rubric 9.2 in part.
9. 416-unit lifts at Dun Dagda and Dun Lamfhota and lift-chain tower stairs in Forest Sauvage and Snowdonia; no one-way
   links in any frontier zone.
10. Lane fallback ignores enemy keeps; only red monsters are steered round. One arrival point per ceremony.
11. PC checks P1-P13 above, each with its command and what settles it.
