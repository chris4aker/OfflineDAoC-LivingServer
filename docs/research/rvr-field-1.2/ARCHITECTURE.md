# Open-field RvR overhaul for 1.2: implementation architecture

Everything cited below was read on `origin/integration/1.2`; paths are relative to `source/server/GameServer/` and unverified items are marked. Nothing here comes from memory.

## Core decisions

- **One scope predicate.** `RvrTacticsScope.Allows(...)` is pure and mirrors `AutonomousRvrPerception.MayAnswerCall` (`bots/autonomous/AutonomousRvrPerception.cs:290`). It is true only for an autonomous gamebot in a bot-only group or solo, with lease `None`, `BotGroupSession`, or `RvrEvent` while not at an objective.
- **What the scope excludes.** `RealmRaid`, `BattlegroundWave`, both player leases, `AutonomousRealmRaid.GetView(group) != null`, committed defenders and siege fighters (`AutonomousRvrDefense.cs:10,17`), `BotSiegeRuntime.Assigned`, and relic carriers. Every hook checks it first.
- **Hooks land first.** One early commit adds a one-line call at each hook site into stub methods, so later packages edit only their own files and stay out of the 6,000-line `BotBrain.cs` and 6,600-line controller.
- **Keep fights are untouched in 1.2.** Bots at an objective keep today's rules; this protects siege balance and is the main thing deferred.

## 1. A–E: what to extend, what to add, where it runs

| | Extend | New pure rules and state | Called from |
|---|---|---|---|
| **A** | `RvrPerceivedKind`, `IsSubject` (`AutonomousRvrPerception.cs:18,177`); `PerceivedKind`, `RvrSkipsLineOfSight`, `PerceiveRvrTargets` (`AutonomousWorldBotController.RvrPerception.cs:20,61,77`); scan radius and rays (`…FrontierThreat.cs:165,183`) | Kinds `EnemyBot`, `EnemyBotPet`. `RvrScanRoster` picks which members look. `RvrPartyKey` keys memory and observer cap by enemy party, so noticing one member notices mates within 1,500. | `TryEngageFrontierThreat` (`BotBrain.cs:1453`) |
| **B** | Target pick (`BotBrain.cs:3335`, `…FrontierThreat.cs:218`); `TryPvpCrowdControl` (`BotBrain.PvpCrowdControl.cs:14`); `MaintainTankPeel`, `BeingHitInMelee` (`BotBrain.TankPeel.cs:19,85`); `FindProtectionTarget` (`BotBrain.cs:4028`); support turn (`BotBrain.cs:3936`) | `RvrTargetPriority`, `RvrAssist`, `RvrCrowdControlPlan`, `RvrInterruptPlan`, `RvrPeelPlan`, `RvrPositioning`; new partials `BotBrain.RvrTactics.cs` and `BotBrain.RvrPositioning.cs` | `AttackMostWanted` (`BotBrain.cs:3254`), before the ambush/flank block (`:3376`) |
| **C** | Gate between perception and target pick (`…FrontierThreat.cs:189–218`); regroup hold at `HoldBeforeNewGroupPull` (`AutonomousWorldBotController.cs:1976`); `Force` and hunt choice (`AutonomousRvrEventLayer.cs:~620`) | `RvrStrength` (count, healers, level, health, power), `RvrEngagement` (stance machine), `RvrGroupState` (wipes, rally point) | Board rebuild; executed by the existing retreat hold (`BotBrain.cs:1423`) |
| **D** | `FollowKeepTravel` route build (`…KeepTravel.cs:74–80`); column follow (`…Rally.cs:394–417`); `IssueVariedRvrPath` (`AutonomousWorldBotController.cs:2432`); `ChooseWaypoint` (`AutonomousRvrTravel.cs:37`) | `RvrLaneCatalog` (lazy load plus `Preload`, as `AutonomousZoneRoads.cs:40–63`), `RvrLaneChoice` (lane per force, hazard rejection, progress along lane) | March and roam travel |
| **E** | RvR branch of `TryFormGroups` (`AutonomousBotGroupCoordinator.cs:1905–1912`, today only the pet cap); roam objectives (`AutonomousWorldBotController.cs:2550–2573`); patrol timeout (`:2166`); `IsEligible` (`BotMeleeStylePolicy.cs:34`); `BotRvrAmbush.cs:13`; `TryApproachAmbush` (`BotBrain.cs:3599`) | `RvrRosterTemplates`, `RvrFrontierPoi`, `RvrRoamCircuit`, `RvrStealthOpener`, `RvrEngagement.SoloMayOpen` | Coordinator serial pulse; `ChooseRvrDestination`; attack turn |

Notes per feature:

- **A.** The retaliation bypass and the `atObjective` bypass (`…RvrPerception.cs:79`) stay as they are, so defenders and siege fighters still acquire instantly.
- **B, targets.** The called target goes onto the aggro list with a high weight, as protection targets do (`BotBrain.cs:3296`). `SelectDistributedRvrTarget` itself is not changed, because the guard and lord path also uses it (`AutonomousWorldBotController.cs:2224`).
- **B, peel.** `BeingHitInMelee` should read `GroupFightBoard.HitInMelee`, which already counts players and enemy bots from attacker lists (`BotBrain.FightBoard.cs:53,147–150`). `FindProtectionTarget` today sends every tank after anyone targeting any member; in an RvR fight it should be limited to the plan's protect assignments.
- **B, slam.** Stun styles are reachable through `bot.CheckStyleStun` (`BotMeleeStylePolicy.cs:117`); how to pick one on demand is unverified.
- **B, positioning.** PvE movement stays excluded for bot targets (`BotBrain.CombatMovement.cs:98–101`). The new partial reuses `ArcAngles` and `RoleSlots` (`AutonomousBotGroupCoordinator.RoleFormation.cs:44–80`) with "front" set to the bearing of the enemy centroid, and the leg walkers in `BotBrain.CombatMovement.cs:69–74` for kiting.
- **C, retreat.** `BotGroupRetreat.Publish` (`BotGroupRetreat.cs:55`) is already read by every autonomous non-player-led member (`BotBrain.Retreat.cs:79–81`), which then drops targets, sprints and uses escapes. RvR retreat therefore needs no new executor.
- **C, clearing the order.** The RvR board must clear its own order, because `CheckGroupRun` and its finish path are PvE-only (`AutonomousBotGroupCoordinator.GroupRun.cs:22`).
- **C, the unused director.** `AutonomousRvrDirector` stays unused. Lift only its wipe rule (`AutonomousRvrDirector.cs:44`) into a new `Force.RecentWipes` field.
- **D.** Lanes are injected as `_keepTravelPoints`, so the existing walker follows them. `MarchLeader` stays as pace keeper, keeping the regroup, hold and arrival logic (`…Rally.cs:283–296, 348–380`). Followers keep up by progress along their own lane. With no catalog entry, or all lanes rejected by `AutonomousArmyDetour` hazards, today's `RvrKeepRoute.TryBuild` and detour run unchanged.
- **E, roams.** The roam list already samples frontier camp cells; add named-mob spots and dungeon entrances (source catalog unverified) and wing-lane midpoints. Roamers check each point for 30–60 s.
- **E, stealth.** Bots never see stealthed targets at all (`…FrontierThreat.cs:158`), so bot stealthers are invisible to bots until they strike. A minimal detection rule is worth adding with the openers.

## 2. Shared fight state

Reuse the fight board's pattern, not the board. `GroupFightBoard` is reactive: it holds only enemies already targeting the group, with no enemy roles and no decisions that persist. Its mechanics fit exactly (`BotBrain.FightBoard.cs:78–112`): one holder per group and region, rebuilt at most once a second by the first asker under the holder's lock, published as an immutable snapshot.

- **`RvrFightBoard` (snapshot).** One area search per group per second from the group centroid: enemies with role, health, casting and control state and who they target; both sides' `RvrStrength`; centroids; and a "third-party fight in view" flag.
- **`RvrFightPlan` (immutable record, replaced atomically by the builder).** Main assist, called target and when it was called, CC, interrupt and protect assignments, stance (Observe, Engage, Hold, Kite, Retreat, Regroup) with its start time, rally point, and skill tier.
- **Cadence.** The board rebuilds every second. The call is held 4–8 s, assignments refresh every 2 s, and stance has 5 s of hysteresis.
- **Threading.** Brains think in parallel (`ECS-Services/NpcService.cs:54`). The builder never takes the coordinator's global lock; session facts are copied into a per-group `RvrGroupState` when they change. Per-member state lives in that member's own brain fields. `BotPvpCrowdControl` reservations (`BotPvpCrowdControl.cs:51`) and `BotGroupRetreat.Orders` are already thread-safe.
- **Warband layer.** `RvrWarbandBoard` is keyed by `RealmWarbandSupport.Roster.Scope` (`RealmWarbandSupport.cs:9`). Other groups adopt a call or a retreat with the existing relay odds and 4 s delay (`AutonomousRvrPerception.cs:135–137`).
- **Reading it.** A brain peeks the plan and never forces a rebuild from inside the coordinator lock.

## 3. Looking human

- **Skill tier per group.** `RvrGroupSkill` gives Casual, Average or Veteran (30/50/20) from the session id and the leader's personality; `rvr_bot_awareness` shifts the mix.
- **Assist lag.** Each call gives every member its own delay, built like `ReactionDelayMilliseconds` (`AutonomousRvrPerception.cs:310`): 0.3–1.5 s veteran, 0.6–2.5 s average, 1–4 s casual.
- **Tunnelling.** A fixed share per fight (10%, 25%, 40%) ignores the call and stays on the nearest enemy or its own attacker. Daredevils (`Boldness`, `:299`) tunnel and over-extend more; low Patience ends "watch the fight" early (`AutonomousBotPersonality.cs:21`).
- **Calling mistakes.** Priority scores carry tier-scaled jitter of about ±20%, and 5–15% of calls land on a tank.
- **Slow support.** Interrupters re-check every 1.5–3 s. A crowd-controlled CC bot is not replaced for 2–4 s.
- **Ragged retreats.** Members react 0.3–2 s apart and some never run.

## 4. Perception budget

Assumed load, not measured: 300 RvR bots per realm in the frontier and out of combat, 80% of them in groups of five. E is the number of enemies within 5,000 units.

| | Scans/s | Candidate pairs/s | Rays/s |
|---|---|---|---|
| Today (`AutonomousFrontierThreatPolicy.cs:8,48`) | 800 | 800 × E, acquired instantly | up to 6,400 |
| Naive A (every bot rolls) | 800 | 800 × E rolls, plus up to 900 × E memory entries | up to 6,400 |
| Planned | about 416 | about 0.25 × today | about 2,000 |

How the planned row is reached:

1. **Lookers.** Two rotating lookers per group plus every solo is 468 scanners, about 416 scans a second.
2. **Radius.** Bots not at an objective scan `max(OuterRange, 2,600)` instead of 5,000, about half the area.
3. **Party-keyed cap.** The observer cap of 3 per enemy party, realm and second gives 324 parties × 2 hostile realms × 3, at most 1,944 rolls a second. `SkipsLineOfSight` (`AutonomousRvrPerception.cs:323`) casts rays only for rolls that can count.
4. **Non-lookers.** They take the plan's target and run no scan.

The fight board adds at most 144 searches a second, only for groups in contact. It can replace the per-member searches in crowd control (`BotBrain.PvpCrowdControl.cs:22`), the free-enemy check (`BotPvpCrowdControl.cs:41`) and tank protection (`BotBrain.cs:4040`). Add `AiSystem.RvrFightBoard` and `RvrTactics` beside `FrontierThreatScan` (`Managers/GameLoop/AiSystem.cs:81`), with a budget of 0.15 ms per build.

## 5. Work packages

WP0 lands first; the other record shapes are frozen on day one so WP1–WP6 can start against them.

| WP | Owns | Settings (all default on) | Log tags | Unit tests |
|---|---|---|---|---|
| **0 Foundation** | Scope, board, plan, skill and state classes; `BotBrain.RvrTactics.cs` stubs; every hook line; `AiSystem`; all property rows | `rvr_bot_field_tactics` | `RVR_FIGHT_BOARD` | `UT_RvrTacticsScope` (every lease kind, player in group, raid), `UT_RvrFightBoard` |
| **1 Perception** | `AutonomousRvrPerception.cs`, `…RvrPerception.cs`, `AutonomousFrontierThreatPolicy.cs`, `RvrScanRoster.cs` | `rvr_bot_perception_bots`, `rvr_bot_group_lookers` | `RVR_NOTICE kind=EnemyBot`, `RVR_SCAN_BUDGET` (per minute) | `UT_RvrPerceptionBots`; extend `UT_RvrPerceptionCalibration`, `UT_RvrPerceptionDuty` |
| **2 Assist, CC, interrupts** | The four rule classes; `BotBrain.PvpCrowdControl.cs` | `rvr_bot_assist`, `rvr_bot_cc_plan`, `rvr_bot_interrupts` | `RVR_ASSIST_CALL`, `RVR_ASSIST_SWITCH`, `RVR_CC_ASSIGN`, `RVR_INTERRUPT` | Priority order, lag and tunnel shares per tier, no CC on the kill target |
| **3 Peel, positioning** | `BotBrain.TankPeel.cs`, `BotBrain.RvrPositioning.cs`, their rules | `rvr_bot_peel`, `rvr_bot_positioning` | `RVR_PEEL`, `RVR_BACKLINE`, `RVR_KITE` | Peel triggers on bot and player attackers, slots, spread |
| **4 Engagement** | `RvrEngagement.cs`, `RvrStrength.cs`, `Force.RecentWipes` | `rvr_bot_size_up`, `rvr_bot_disengage`, `rvr_bot_regroup` | `RVR_SIZE_UP`, `RVR_WATCH`, `RVR_RETREAT`, `RVR_REGROUP` | Stance transitions, hysteresis, retaliation always allowed |
| **5 Lanes** | `RvrLaneCatalog.cs`, `RvrLaneChoice.cs`, `…Rally.cs`, `…KeepTravel.cs`, `AutonomousRvrTravel.cs` | `rvr_march_lanes`, `rvr_roam_lanes` | `RVR_LANE_LOADED`, `RVR_LANE_ASSIGN`, `RVR_LANE_FALLBACK` | Parse, missing file, 50/25/25 split, hazard rejection, fallback |
| **6 Roamers, stealth** | The four E classes; coordinator RvR branch; roam objectives; `BotMeleeStylePolicy.cs`; `BotRvrAmbush.cs` | `rvr_roaming_parties`, `rvr_roam_checks`, `rvr_bot_stealth_openers` | `RVR_PARTY_FORMED`, `RVR_ROAM_CHECK`, `RVR_STEALTH_OPEN` | Templates degrade with a short pool, circuit, opener eligibility |

- **Shared conflict files.** WP0 edits `serverproperty/ServerProperties.cs` (~2548), the launcher's `SocialExpansionSettings.cs:147,155` and `source/tools/release-server-settings.json` once for everyone.
- **No upgrade lines.** New settings need no `LivingServerSettingsUpgrade` line; that list is only for changed defaults.
- **Dependencies.** WP2, WP3 and WP4 need WP0's board. WP1, WP5 and WP6 are independent, except that WP6's solo rule lives in WP4's file.

## 6. Risks and checks

- **Raids.** Darkness Falls counts as a battle region (`…FrontierThreat.cs:129`), so the scope test is the guard. Run `UT_DutyLeaseRealmRaid`; no `RVR_ASSIST_CALL` may ever carry a raid duty.
- **Battleground assaults.** Tactics choose only from targets left after the gather and storm filters (`…FrontierThreat.cs:195–215`). Run `UT_Battleground*` and `UT_DutyLeaseBattleground`; watch `BG_GATE_CLEAR` and the count of bots "Fighting".
- **Siege balance.** Two changes can shift the 50%:
  - Perception on marching columns changes march attrition.
  - Three lanes change attrition and arrival timing.

  Run `UT_SiegeBalanceNov06`, `UT_SiegeDefenseAtMarch`, `UT_StagedSiegeOct6`, `UT_AutonomousArmyPush`, `UT_AllFrontierKeepRoutes`. Watch `SIEGE_DEFENSE_BALANCE`, `RVR_EVENT_ENDED`, `RVR_SIEGE_PRESENCE`, and marched against peak-present attackers.
- **Unreachable called targets.** A called target on a wall would send the whole train at it. Priority must drop anything a member ignores (`IgnoresTarget`, `:64`); watch `AUTONOMOUS_PVP_STALEMATE` and `AUTONOMOUS_UNREACHABLE_TARGET` counts.
- **Roamers taking healers from sieges.** Siege parties form first (`AutonomousBotGroupCoordinator.cs:245–248`); watch `RVR_EVENT_OPENED size=`.
- **Retreat switch.** Turning `bot_veteran_retreat` off also disables RvR retreat; document it.
- **Players and companions.** Run `UT_CompanionFrontierAggro`, `UT_DutyLeasePlayerGroup`, `UT_VeteranFightBoard`, `UT_VeteranGroupRetreat`, `UT_FrontierThreatScan`.
- **Performance.** Check the AI profile report for the frontier scan and the new scopes at p99.

Per AGENTS.md, all of this stays "tests only" until seen in one of the owner's PC logs.

### Critical Files for Implementation
- source/server/GameServer/bots/autonomous/AutonomousWorldBotController.FrontierThreat.cs
- source/server/GameServer/bots/autonomous/AutonomousRvrPerception.cs
- source/server/GameServer/bots/BotBrain.cs
- source/server/GameServer/bots/BotBrain.FightBoard.cs
- source/server/GameServer/bots/autonomous/AutonomousWorldBotController.Rally.cs