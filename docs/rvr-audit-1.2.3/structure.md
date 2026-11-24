# RvR audit 1.2.3: the structure that decides RvR behaviour (agent: structure)

**Issue:** #494, part of the RvR audit (#506). **Read from:** `origin/main` at `de53d0d3` (worktree `/home/claude/wt/494`).
**Status of everything here:** code reading, counts and the repo's own docs. **Tests only.** No C# was run, no server
was started, nothing in the game changed. Where a claim rests on the owner's PC it cites `docs/PLAYED-CHECK-1.2.md` or
the changelog and the build.
**Evidence tags on every claim:** **[played]** seen in an owner's PC log (build named), **[sim]** a world sim or bot
run, **[unit]** a unit test, **[read]** read from the code only. Almost everything below is [read].
**Period judgments** use [reference-draft.md](reference-draft.md) (section 1 only) and the README's brief. The final
rubric (`reference.md`, #493) is being written in parallel; the places to re-check against it are marked **(check
rubric)**.
**Counts** come from read-only greps (appendix A); rerun them to check.

**Short answer**
- **Who decides what a bot does in the frontier? Six layers, and no one of them sees the whole decision.** A goal
  quota, the group coordinator, the event layer, the world bot controller (twelve RvR partials and a 260-line
  `ExecuteRvr`), the brain (threat scan, fight plan, retreat) and, for the player's own side, the companion code. A
  seventh, the original `AutonomousRvrDirector`, is dead code with a green unit test.
- **Do the layers agree? No, in four structural ways.** They see different worlds (omniscient strategy, chance-based
  eyes, omniscient group mind); they make the same decision more than once and the last writer wins (destination
  four times a turn, target three times a fight); the field model and the keep model are two different fight engines by
  design; and the player is invisible to the strategic layers that hunt bots.
- **Does a fix land once? Rarely.** Of the eleven past RvR bugs traced in section 3, three were fixed in one place. The
  rest became a rule in one path (frontier, not battleground; field, not keep; bots, not companions), or a copy in two
  or three.
- **The good news:** the pure rule files (`Rvr*.cs`, 8,641 lines, mostly unit tested) are good material, and the event
  layer already is a single owner for sieges. What is missing is one record of what each bot *knows*, one record of
  what each force *intends*, one owner of each bot's *legs*, and one log row that says *why*.

---

## Top 10 findings, worst first

Each is a class, not an instance.

### 1. Three perception models in one bot, and the one with the reaction delay is the weakest of the three.
- **Player sees:** a bot group that "knows" where enemies are before any member has noticed them: it sizes up, holds,
  retreats or calls a target on an enemy it has no line of sight to. A bot shouts "inc" before it could have seen you.
  Meanwhile the same bot, alone, notices you by chance at 516-1,575 units **[played, `3d67b6f`, PLAYED-CHECK-1.2.md:329]**.
- **The three models:**
  - **Strategy is omniscient over gamebots.** `ChooseRvrDestination` builds hunt targets from every live enemy RvR
    gamebot in the committed frontier (`AutonomousWorldBotController.cs:2565-2586`), from a per-tick snapshot of the
    whole bot registry (`AutonomousRvrPlanningView.cs:33-45`). The roaming circuits weigh spots by a 3-minute sample of
    every levelling and RvR gamebot in all three frontiers (`RvrRoamCircuit.cs:474-500`). No line of sight, no notice.
  - **The eyes have period rules.** `TryEngageFrontierThreat` scans, casts rays, and lets a target through only once
    `AutonomousRvrPerception` has noticed it, after a reaction delay (`AutonomousWorldBotController.FrontierThreat.cs:156-211`,
    `AutonomousWorldBotController.RvrPerception.cs:137-233`).
  - **The group's mind is omniscient within 3,500.** The fight board that drives size-up, watch, retreat and the called
    target lists every unstealthed enemy within `OuterRange` from one area search, with no ray and no notice
    (`RvrFightBoards.cs:301-312`, `RvrFightBoard.cs:122`). A member then switches to the called target after checking
    only alive, unstealthed, legal and in radius (`BotBrain.RvrTactics.cs:118-129`), not "has anyone seen it".
  - **And a fourth leak:** the chat alarms hear the pre-ray, pre-notice list (`FrontierThreat.cs:192`, `nearby`, not
    `visibleTargets`).
- **Where the rule is switched off entirely:** at an objective (a committed defender or siege fighter near its keep)
  perception is bypassed (`RvrPerception.cs:30-35,139`): instant acquisition at up to 5,000, and stealthed enemies are
  never candidates at all (`FrontierThreat.cs:169-170` needs `Rules.Stealth`, which is `None` there). This is the
  documented owner decision 1 (RVR-FIELD-1.2.md:69,85,293). Its structural cost is that a stealther at a keep meets
  bots that are both all-seeing and stealth-blind.
- **Also:** bots far from the player think less often (travel up to 5 s, planning up to 12 s;
  `AutonomousFidelityPolicy.cs:33-66`), so the eyes look on a coarser clock exactly where the owner can't see; the
  chance per scan is corrected for the interval, the reaction granularity is not **[read]**.
- **Evidence it bites:** the notice delay is "about 1 s at every range" for bot-on-bot notices on `19e7037`
  **[played, PLAYED-CHECK-1.2.md:343]**; RVR-FIELD-1.2.md:46 says bots "still acquire enemy bots instantly at 5,000"
  before WP1.
- **Class:** split perception; omniscient planning.
- **Fix:** one contact table per bot and group (piece B, section 5). **Size L. Risk M** (fewer fights while the
  strategic layer learns to search instead of know).

### 2. The destination is decided up to four times in one turn; the last writer wins and nobody logs who.
- **Player sees:** a warband that turns round, an eight that walks past a fight (#417, closed), a bot that "defends"
  a keep the event layer doesn't know it is defending.
- **Writers in order, one `ExecuteRvr` turn (`AutonomousWorldBotController.cs:2013-2270`):**
  1. the committed event plan (`:2039-2053`);
  2. the leader's intent copied from a TempProperty by every follower (`:2060-2062`);
  3. `ChooseRvrDestination` → `AutonomousRvrEventLayer.ChooseOrJoin` (`:2685-2691`);
  4. then **overruled** by the roam override `RvrTacticsRoamDestination` (`:2702`; `AutonomousWorldBotController.RvrTactics.cs:56-166`);
  5. and **overruled again** by a chat promise ("omw bled"), which sets `_rvrIntent = DefendEvent` with no event
     registration (`AutonomousWorldBotController.ChatPromise.cs:31-62`, called at `:2174`).
- **Counted:** 22 writes to `_rvrDestination` in 5 files, 5 writes to `_rvrIntent` in 3 files (main, ChatPromise,
  Battleground). The intent also lives in the leader's `RvrWarbandIntent` and `RvrDefendingKeep` TempProperties and in
  the event layer's `Attackers/Defenders/ThirdRealm` maps.
- **Evidence it bites:** #417's fix is a special case inside the override ("an eight goes after the live enemy force
  the planner found near it", `RvrTactics.cs:128-140`): the override had been replacing every hunt **[read; the fix is
  tests only]**.
- **Class:** decision made twice by disagreeing layers.
- **Fix:** one force intent record, owned by the event layer, which the override and the chat promise *propose to*
  (piece C). **Size M. Risk M.**

### 3. The first target is spread on purpose; the second layer then focuses. They fight each other.
- **Player sees:** a bot group opens on two or three of you at once, then converges after each member's delay. Against
  a solo player that is harmless; against a group it looks like no assist, then a sudden train.
- **Where:** the threat scan picks by `SelectDistributedRvrTarget` (`FrontierThreat.cs:241`,
  `AutonomousWorldBotController.cs:2350-2364`): a member's database id modulo the party size picks one of the closest
  visible enemies (`AutonomousRvrStaging.cs:186-198`, "spreads a warband deterministically"). Once in combat
  `AttackMostWanted` sets the target from the aggro list and then lets `RvrTacticsTarget` overwrite it with the
  group's call (`BotBrain.cs:3400-3401`).
- **Count of target pickers in RvR code:** at least 12 (threat scan; `FindRvrTarget` for guards and lords
  `:2272-2318`; door battering `:2098`; relic sentinel and lord step-in, `AutonomousWorldBotController.Relic.cs:327-370`;
  the brain's aggro pick; the call and the interrupt subject, `BotBrain.RvrTactics.cs:138-211`; crowd control,
  `BotBrain.PvpCrowdControl.cs:16`; battleground picks, `AutonomousWorldBotController.Battleground.cs:129,170-205,406,718`;
  `AutonomousDefensivePull`; the roam hunt quarry, `RvrTactics.cs:359`; companion PvP, `CompanionPvpEngagement.cs:134-136`;
  the companion raid siege). 125 `TargetObject =` writes in 30 bot files, 40 `AddToAggroList` in 15.
- **Class:** decision made twice.
- **Fix:** the plan owns the first pick too (piece D). **Size S** for the field (call the planner from the scan),
  **M** with keeps. **Risk M** (more focus fire on the player: tune by tier).

### 4. The field and the keep are two fight engines, by design, and the line between them moves with every rule.
- **Player sees:** bots that peel, kite, assist and retreat on a road, and then, 2,000 units later at the keep,
  stand on a wall post, see everything instantly, never retreat, and never notice a stealther.
- **Where:** `RvrTacticsScope.Refusal` (`RvrTacticsScope.cs:79-95`) switches the whole field package off for a realm
  raid, a battleground assault, a player's group, a companion, a relic carrier, a siege operator and a bot "at its
  objective". Every WP0-6 hook asks it first (`BotBrain.RvrTactics.cs:34-54`).
- **What a bot loses when it crosses the line (all [read]):** noticing and stealth detection (finding 1), the called
  target and interrupts, peel and positioning, the stance machine (size-up, watch, retreat, regroup;
  `BotBrain.RvrTactics.cs:462-491`), the chase leash. What it keeps: the solo bail if it is ungrouped
  (`BotBrain.Retreat.cs:132-161`), the 90-second PvP stalemate break (`FrontierThreat.cs:18-60`).
- **Why it matters structurally:** "at its objective" is decided by distance to a remembered point
  (`RvrAtObjective`), so a marching column changes engine on arrival, and a field fight that drifts to a keep
  changes engine mid-fight.
- **Class:** rules that hold in the field but not at a keep, in a battleground, for a companion, a solo, a pet.
- **Fix:** keep the scope test but make it choose a *profile* of one engine (field, siege attacker, wall defender,
  storm), not on/off. Owner decision 1 binds 1.2; lifting it is the owner's call (piece D). **Size L. Risk H** (siege
  balance; RVR-FIELD-1.2.md:239 targets 35-65% attacker wins).

### 5. The real player is invisible to every strategic layer.
- **Player sees:** levelling in Pennine or roaming Emain, nobody comes looking for you. A bot finds you only if its
  own walk brings it within its scan.
- **Where:** hunt objectives are `GameBot`s only (`:2565`, from `RvrPlanningView.Candidates`, bots on RvR duty);
  region enemy counts for keeps count bots only (`AutonomousRvrPlanningView.cs:36-44`); roam prey is "levelling
  gamebots" (`RvrRoamCircuit.cs:483-499`); a check-spot look takes bots as prey and counts players only towards the size
  of the bots' party (`RvrTactics.cs:258-291`). Only a keep attack sees the player: `ObserveKeepAttack` raises an alarm
  for a *player* instigator only, sized to the player's group, with a horn and "A massive defense force has been
  rallied!" (`AutonomousRvrEventLayer.Defense.cs:16-64`).
- **Judgment:** half of this is right for fairness (no global hunt of one human). The other half is wrong for the
  period: in 2003 a lone player in Emain was found by the same roamers that found everyone, at the same spots; and a
  realm learned of an attack from what its players saw, not from a sized announcement. **(check rubric: who was out
  there, how alarms spread)**
- **Class:** perception omniscient in one path and blind in another.
- **Fix:** comes free with piece B (the strategic layers read contacts, and a player is a contact like any other).
  **Size** in B. **Risk M** (the player meets more bots; measure per hour, section 6).

### 6. The siege thresholds are written three times, and the healer count is computed and never read.
- **Where:** "can this force siege" is level 35 and 4 members in the controller (`:2684`), level 40, 5 members and a
  healer in the dead director (`AutonomousRvrDirector.cs:84-92`), and level 50 average **and** minimum with 4 members in
  the event layer (`AutonomousRvrEventLayer.cs:414,508-509,618`). The event layer's rule is the one that holds; the
  controller's 35 does nothing.
- **Healers:** counted by class-name substring, `cleric|healer|druid|bard` (`:2679-2682`). Shaman, Friar and Warden are
  missing, so a Midgard group of Shamans counts none. It doesn't matter today because `Force.HealerCount` is never read
  by the event layer (only by the dead director). The roster code has a proper role table (`RvrRoles.For`, used at
  `RvrTactics.cs:451-460`): a third definition.
- **Hunts ignore distance and size.** Each enemy bot is one objective with `EnemyCount = 1`; the event layer picks the
  hunt "closest in size to the force" (`AutonomousRvrEventLayer.cs:630-631`), which is always a tie, so the first in a
  hashed shuffle wins (`:2573`). A warband can be sent across Emain after one bot, while one is 800 away. Only the
  20-minute frontier commitment bounds it (`:2556-2561`).
- **Class:** duplicated rules; dead state.
- **Fix:** delete the director and the unused field; one `ForceStrength` used by every reader; hunts by contact
  distance and party size (piece C). **Size S. Risk low.**

### 7. Defense is told, not seen; and a bot attack has no small form.
- **Player sees:** a realm that answers a bot army the moment it leaves its border keep ("Scouts report ...",
  `AutonomousRvrEventLayer.cs:1025-1045`), and answers the player's own keep attack with a sized force announced to
  them. A three-bot gank of a keep's guards, by contrast, has no event and draws no defense.
- **Where:** a bot siege exists only as an event the attacker opens (`ChooseCore`, `:638-671`); its defense is
  scheduled by the same event (`CallDefendersAtMarch`, `BeginDefenseMuster`). The keep-damage hook raises an alarm only
  for a player instigator (`Defense.cs:48-64`). Every bot attack is a full muster (`:1068-1073`: "Today every bot
  attack is a full siege").
- **Period:** the front keeps changed hands several times a day and deep keeps were taken by small night raids
  (reference-draft 1.2; NIGHT-SIEGES-AND-MARFACH.md, owner 2026-11-05). **(check rubric: siege sizes, alarms)**
- **Evidence:** the change was made after defenders arrived too late on 1.1.3 RC nights **[played, the comment at
  `:1026-1031` cites 18 sieges]**; the scouts fix itself is tests only.
- **Class:** state one layer has and the next lacks (the keep knows it is hit; only the player path turns that into
  an alarm).
- **Fix:** an alarm from the keep for any enemy, raised through contacts (piece B) and answered through the objective
  model (piece C); a small-strike event kind. **Size M. Risk M** (siege balance).

### 8. The battleground is a third siege system that borrows the frontier's fields.
- **Where:** `ExecuteBattleground` (`AutonomousWorldBotController.Battleground.cs:26-210`, 1,091 lines, plus
  `AutonomousBattlegroundWave` 313 and `AutonomousBattlegroundKeepRally` 1,951) sets `_rvrDestination`, writes a fake
  `_rvrIntent = AssaultKeep` with no event (`:69`), and calls the frontier's `FindRvrTarget` (`:129`), which then needs
  battleground clauses of its own (`AutonomousWorldBotController.cs:2296-2304`). The gather hold is enforced in three
  places: the threat scan filter (`FrontierThreat.cs:217-237`), the combat drop in `Tick` (`:585-590`) and
  `FindRvrTarget` (`:2301-2304`).
- **Counted:** 3,355 lines; 23 dated rule comments and 30 owner/PC citations in the rally file alone.
- **Evidence:** no battleground keep had changed hands on the first PC sessions **[played, `3d67b6f`,
  PLAYED-CHECK-1.2.md:331]**; the lord took damage on `43c43aa` **[played, :362]**; #416's lord-reach fix landed in the
  battleground first, then again for the frontier ("Frontier sieges too", CHANGELOG-LIVING-SERVER.md:485-491).
- **Class:** parallel engines; fixes by path.
- **Fix:** the battleground keep as one more objective in the objective model, with its wave as that objective's
  muster (piece C). **Size M-L. Risk M.**

### 9. Many hands move a bot's legs, and the fallbacks are visible.
- **Owners of movement in RvR (all [read]):** retreat (`BotBrain.Retreat.cs`), the stable route, the threat scan's
  stop (`FrontierThreat.cs:260-261`), the combat FSM, the field positioning (backline, spread, kite;
  `BotBrain.RvrPositioning.cs`), the chase leash, the lord formation, the relic carrier run, the siege rally and march
  (`AutonomousWorldBotController.Rally.cs`), keep travel, defensive posts, frontier transport, follow-the-leader, the
  roam turn, the battleground gather, rally and storm, chat-promise walks, the stuck watchdog and pocket escapes. 143
  path-issuing calls in 45 bot files.
- **Visible fallbacks:**
  - stand still with "Awaiting a frontier objective" (`AutonomousWorldBotController.cs:2187-2191`);
  - stand at a patrol point for exactly 90 s, then re-plan (`:2258-2266`);
  - a keep route that fails twice near the arrival point **moves the bot up to 4,000 units** in one step
    (`AutonomousWorldBotController.KeepTravel.cs:241-262`, `MoveInRegion`);
  - pocket escapes and the 15-minute watchdog send bots to their capital (`AutonomousStuckWatchdog.cs:526`) **[played,
    `43c43aa`: Odin's Gate muster posts, CHANGELOG-LIVING-SERVER.md:613-620]**.
- **Class:** no movement arbiter; fallbacks that are seen.
- **Fix:** one legs lease per bot with fixed priorities, logged (piece E). **Size M. Risk M** (pathing side effects).

### 10. The log says what happened at sieges, and almost nothing about why a bot in the field acted.
- **Counted:** 76 distinct `RVR_*` log tags written from 28 files. **23 come from `AutonomousRvrEventLayer.cs` alone.**
  Plus 18 `BG_*` and 6 `RELIC_*` tags.
- **Missing rows (decisions with no log line, all [read]):**
  - the threat scan's engage (who engaged whom, at what range, from which list: `FrontierThreat.cs:241-269` writes nothing);
  - the plan's choice for a hunt or a roam (only sieges log, `RVR_EVENT_OPENED`);
  - which layer overruled the destination (finding 2);
  - perception at an objective (no `RVR_NOTICE`, by design);
  - companion PvP engages; chat-promise overrides;
  - the "Awaiting" stand and the 90 s patrol refresh.
- **Status text is not a log.** `SetRvrStatus` (74 calls in 11 files) writes free text for the launcher's dashboard,
  not a coded reason.
- **Rate limits hide the hour.** Several tags pass through `RvrLogBudget.Take` (for example `RVR_ROAM_CHECK` 120 a
  minute, `RVR_ROAM_CIRCUIT` 60). A budget hides which bot was dropped.
- **Class:** no audit trail.
- **Fix:** one `RVR_DECIDE` row per decision change, written where the decision is made (piece A). **Size S-M.
  Risk low.**

---

## 1. The system as built

### 1.1 The decision makers

| # | Layer | Files / lines | Decides | Tick | Reads | Overrules / is overruled by |
|---|---|---|---|---|---|---|
| 0 | Goal quota (`AutonomousObjectiveAssignments`) | 1 / ~450 | that a bot is on RvR, battleground or PvE duty | assignment passes, by level band (`bot-goals.json`) | roster, quotas (`:380-400`) | overrules everything below; RvR is a lease that can end mid-siege (`:441-449` clears all RvR state) |
| 1 | **Director** (`AutonomousRvrDirector`) | 1 / 96 | nothing: **no production caller** | - | - | its wipe rule was "lifted" into `RvrEngagement.cs:265,797`; its unit test (`UT_AutonomousRvrDirector`) is green |
| 2 | Group coordinator (RvR parts) | 8 / 1,095 | party kind (warband 55 / eight 30 / small 15), roster, siege parties, size | coordinator pass, ~1.5 s per bot pulse (`AutonomousWorldBotController.cs:434-439`) | census of RvR bots not in a siege (`AutonomousBotGroupCoordinator.RvrTactics.cs:61-108`) | runs under its own lock; its directive overrules the bot's own task (`:477-503`) |
| 3 | Event layer (+ realm events, staging, rally, defense) | 18 / 5,097 | sieges: open, join, muster, march, start, end, retake; defense calls; relic campaigns and carrier events; per-realm caps | inside each bot's `ChooseRvrDestination` (45-60 s review) under one lock; `PulseDefense` every 5 s from the coordinator (`AutonomousBotGroupCoordinator.cs:226`, `Defense.cs:191`); a per-realm siege roll once a minute (`AutonomousRvrEventLayer.cs:458-470`) | the objectives the bot passes in; its own `Events`, `CarrierEvents`, `Campaigns`, `Cooldowns`, `DefenseAlarms` | a committed plan overrules the controller's own choice (`AutonomousWorldBotController.cs:2039-2053`); itself overruled by the roam override and chat promises (finding 2) |
| 4 | Objective state (`AutonomousRvrObjectiveState`, keep policy, siege jobs, `BotSiegeRuntime`) | 14 / 1,343 | a phase label for status text; what is a siege objective; siege engine jobs | per turn | the target's type | `ForTarget` only labels status (`:2109-2110`); `Next` has no production caller |
| 5 | World bot controller, RvR parts | 13 partials / 4,078 + `ExecuteRvr`..`ChooseRvrDestination` (`AutonomousWorldBotController.cs:2013-2745`) | destination, travel, siege target, relic pickup, defense post, roam turn | each brain think it is reached (1-12 s by fidelity) | event plan, planning view, catalogs, TempProperties | a 26-step ladder in `ExecuteRvr` (1.3) |
| 6 | Battleground | 3 / 3,355 | the whole battleground: travel, wave, rally, storm, lord | per turn | its own wave and rally state | borrows layer 5's fields (finding 8) |
| 7 | Perception | 4 / 1,224 + the controller's 357-line partial | what a scanning bot may engage | scan due every 1,000-1,249 ms (`AutonomousFrontierThreatPolicy.cs:47-48`) when the brain thinks | area search, rays, notice memory | bypassed at objectives; not used by layers 3, 5 (planning), 8 (board) |
| 8 | Field tactics: fight board, plan, assist, CC, interrupts, peel, positioning, stances, lanes, circuits (pure rules) | 30 / 8,641 | the group's plan and stance | board at most once a second per group | omniscient area search within 3,500 (`RvrFightBoards.cs:301-312`) | only where `RvrTacticsScope` allows |
| 9 | BotBrain RvR (threat scan entry, hooks, retreat) | 8 / 2,354 | target in combat, CC, support, moves, retreat | every think | the plan, the aggro list | the threat scan runs **before** the controller (`BotBrain.cs:1466-1477` vs `:1723-1726`); retreat before both (`:1437`) |
| 10 | Companion PvP and raid siege | 4 / 1,024 | what the player's companions fight; the player's `/raid` siege jobs | every think | the leader's surroundings, enemy **bots** only (`CompanionPvpEngagement.cs:43-44`) | out of scope of 7-9 entirely |

Totals: about **93 files and 26,400 lines** of RvR code in `bots/` (appendix A), plus the RvR sections of
`AutonomousWorldBotController.cs` (6,739 lines) and `BotBrain.cs`.

### 1.2 Who goes first in one brain think (`BotBrain.cs`, autonomous gamebot)

1. retreat (`:1437`) → 2. stable route (`:1445`) → 3. companion raid siege (`:1458`) → 4. **frontier threat scan**
(`:1471`, only when not already fighting) → 5. defensive pull hold (`:1483`) → upkeep, buffs, pets, chat, merchants →
6. **controller `Tick`** (`:1726`) → 7. the FSM (`:1740`), whose AGGRO state runs `AttackMostWanted` (`:3319-3401`) with the
plan's call last.

Inside `Tick` (`AutonomousWorldBotController.cs:410-769`): stable travel → relic carrier run → shared dungeon →
siege hold → **combat returns to the FSM** (`:573-613`) → chat-promise walk → realm raid travel → foreign frontier
return → relic custody → event force (`:706-716`) → **PvE regroup and assembly logic also governs non-event RvR
groups** (`:717-754`) → `ExecuteRvr` (`:769`).

### 1.3 Inside `ExecuteRvr` (`AutonomousWorldBotController.cs:2013-2270`)

The order is the precedence. Each line returns the turn when it acts.

| step | line | what |
|---|---|---|
| 1 | 2021 | relic carrier's own turn |
| 2 | 2028 | home defense check, every 30-35 s (2026-10-20 fix) |
| 3 | 2039 | committed event plan replaces the destination |
| 4 | 2058-2059 | siege job; rally order |
| 5 | 2060 | follower copies the leader's intent |
| 6 | 2064 | field stance travel hold (WP4) |
| 7 | 2065 | **PvE** "hold before new group pull" |
| 8 | 2067 | solo border-keep staging |
| 9 | 2073 | drop a resolved event destination |
| 10 | 2085 | siege job again |
| 11 | 2088 | relic pickup within 400 |
| 12 | 2091-2112 | siege target (`FindRvrTarget`), back line, door battering, sentinel step-in, engage |
| 13 | 2117-2129 | follower: wall post or follow the leader |
| 14 | 2131-2162 | relic carrier escort or recovery |
| 15 | 2174 | chat promise destination |
| 16 | 2176 | roam turn at a check spot (WP6) |
| 17 | 2177-2193 | re-plan (`ChooseRvrDestination`) or stand "Awaiting" |
| 18 | 2195 | rally order again |
| 19 | 2202 | relic pickup again |
| 20 | 2206 | cross-region travel |
| 21 | 2216 | defend: post |
| 22 | 2222 | keep travel |
| 23 | 2227 | walk (or stable ride) |
| 24 | 2241 | siege approach hold |
| 25 | 2258 | patrol |
| 26 | 2259 | 90 s refresh |

The same check appears twice (siege job 4/10, rally 4/18, relic pickup 11/19), and the rules come from three
packages: events, field tactics and PvE grouping.

### 1.4 The chains, with every hand-off and the state that crosses it

**A roaming group, from spawn to a finished fight**
1. Quota sets RvR duty → `PersistentRecord.ObjectiveKind` (layer 0).
2. Coordinator forms a party, chooses kind and roster → `RvrParties[Group]` (ConditionalWeakTable), `Session`,
   directive (layer 2). *Hand-off:* the controller reads the directive every 1.5 s.
3. Leader's `ChooseRvrDestination`: planning view (all bots) + keeps + camp cells → `Force` → event layer → `Plan`
   → `_rvrDestination`, `_rvrIntent`, TempProperties `RvrWarbandIntent`, `RvrDefendingKeep`; then the roam override
   may replace it with a check spot (`RvrRoamBox` walk). *Hand-off:* followers read the leader's TempProperty;
   they do not read the plan.
4. Travel: frontier porter (`AutonomousFrontierTransport.Request` TempProperty), stable route, `IssueVariedRvrPath`;
   followers `FollowDynamicGroupLeader`.
5. At a spot: `RvrTacticsRoamTurn` looks every 3 s for **bot** prey (`RvrTactics.cs:329-350`); a hunt walks the leader
   to within 700. *Hand-off:* the hunt moves the body; it does not set a target.
6. Any member's threat scan (two lookers per group) notices → `RvrPerceptionMemory` → spotter call to mates →
   `SelectDistributedRvrTarget` → aggro list, FSM AGGRO. *Hand-off:* from controller state to brain state; the
   controller's `_rvrDestination` is kept for after the fight (`:594-600`).
7. Fight: board (omniscient 3,500) → plan (call, CC, interrupts, peel) → members switch after their delay; stance →
   retreat order `BotGroupRetreat.Order` read in `TryHoldRetreat`.
8. Deaths: `RecentWipes` on `RvrGroupState`; release and return (`HandOverReleaseReturnToGroup`); regroup.
9. Next plan: the event layer reads `RecentWipes` only in `ChooseIntent` (`:412`), which a committed force never
   reaches.
- **State that crosses no layer:** what a member noticed (per bot memory) never reaches the leader's next
  `ChooseRvrDestination`; what the board knew never reaches the event layer.

**A solo stealther**
1. Quota → RvR; coordinator leaves it ungrouped (the 15% share) → `RvrPartyKindOf = Small`.
2. `ChooseRvrDestination` → event layer (reserve or hunt) → roam override → `ChooseSoloWalk`, a stealther spot
   weighted by **omniscient** levelling-bot counts and by how many of its realm already camp it (`RvrTactics.cs:217-241`).
3. Walks visible, stealths at the stand point (`:391-392`); looks every 3 s for levelling **bots** only (`:270-271`).
4. A hunt walks to within 700 (`:368-369`), then the threat scan must notice it (perception + `RvrSoloGate` odds,
   `:496-506`) before it engages; the opener is `BotRvrAmbush.TryStealthOpener` in the brain (`BotBrain.RvrTactics.cs:428-440`).
5. The player is a target only through step 4's scan, never through steps 2-3.
6. Enemy field bots can notice it close up by chance (`RvrStealthNotice`); bots at a keep cannot at all (finding 1).

**A keep take (choice to claim)**
1. A level-50 force asks `ChooseOrJoin`; the realm's minute roll allows a new siege (`:458-470`, `:619`);
   `ChooseVariedTarget` picks a keep → `ActiveEvent` opened, `RVR_EVENT_OPENED`, notice to the realm (`:638-671`).
2. Muster at the border keep: `Mustered`, `MusterAttendance`, posts (`AutonomousRvrMusterPost`) → `GiveMarchOrder`
   (`:999`) → `MarchLeader`, `RVR_SIEGE_MARCH`; **defenders called at the same moment** (`:1010`).
3. March: `HandleSiegeRally` / `TryMarchWithArmy` (`Rally.cs:91,264`), lanes (`RvrLaneChoice`), `RVR_MARCH_*`.
4. Arrival quorum → `StartBattle` (`:1610`), `RVR_SIEGE_STARTED`; `RvrAtObjective` set → the bot leaves field rules.
5. Siege jobs (rams, engines), door battering by hand (`:2097`), guards by `FindRvrTarget` (closed door keeps the
   lord off-limits, `:2286-2300`), lord formation and step-in (`RVR_LORD_FORMATION`, `RVR_LORD_STEP_IN`).
6. The lord's native death captures the keep → `KeepEvent.KeepTaken` → `AutonomousRvrDashboard.OnKeepTaken` →
   `EndTarget`, `RVR_KEEP_TAKEN` (`AutonomousRvrDashboard.cs:251-260`) → `StartRetake` for the loser (`:840`).
7. Claim: `AutonomousBotGuildKeeps` (a bot guild with enough members inside, no player of the realm present;
   `AutonomousBotGuildKeeps.cs:36-41`).
- **Hand-offs that lose state:** the attacking force's `RecentWipes` and odds never enter the siege rules; the
  members' field plan is dropped at step 4; the event ends by timer or capture, not by the army's own judgement.

**A defense**
- *Against a bot siege:* step 2 above calls the defenders → each defending force's next `ChooseRvrDestination` (up
  to 45-60 s later, or the 30 s home-defense check) joins `Defenders` → `BeginDefenseMuster` → `GiveDefenseMarch` →
  `TravelToDefensivePost` / `HoldDefensiveKeepPost` (`BotKeepWallSpots`) → instant, stealth-blind acquisition at the
  post → `RepelMilliseconds` (10 min with no attacker present) ends it as defended (`:33`).
- *Against the player:* `ObserveKeepAttack` (any damage callback) → `DefenseAlarms` → `PulseDefense` every 5 s →
  `DefenseReaction` event sized by the player's group → horn sound and rally line to the player (`Defense.cs:16-64`).
- *Against a few bots with no event:* nothing (finding 7).
- *By a chat promise:* `DefendEvent` intent with no event slot (finding 2).

**A relic raid**
1. `ChooseCore` (`:595-617`): relic keeps of other realms; a realm whose own relic is held goes there first; if
   more than 2 sentinels stand (`HeldSentinelKeeps`), 12% of the time a feeder keep is taken instead.
   Forced raids run a `RelicCampaign` of feeder sieges (`:124-153`, `:238-297`).
2. Muster, march (2-hour window, `:22`), battle as above; sentinels need the 800 step-in (`Relic.cs:327`).
3. Pad: `RELIC_PLAYERS_REQUIRED_ON_PAD` bots on the pad; one carrier chosen (`RELIC_CARRIER_CHOSEN`, `Relic.cs:258-306`).
4. `RelicPadEvent.RelicStolen` → `TransferToRelicCarrier`: the keep event becomes a carrier event
   (`AutonomousRvrDashboard.cs:281-297`).
5. Every realm's `ChooseRvrDestination` now sees the carrier as an objective, joined under per-realm caps of 240
   (`:519-528`, `:38`).
6. The carrier runs home (`RelicCarrierRuns`, `Tick :555`; `RelicCarrierTurn`, `ExecuteRvr :2021`); it takes no
   target (`FrontierThreat.cs:140`, `:2084`). → `RelicMounted` / `RelicReturned` → `EndTarget`.
- **The carrier rule is in four places** (Tick, ExecuteRvr, threat scan, FindRvrTarget guard): relic audit L4.

**A battleground wave**
1. Quota → Battleground duty; bracket by level (`BattlegroundBrackets`) → travel.
2. Central keep → `_rvrDestination`, fake `AssaultKeep` intent (`Battleground.cs:51-69`).
3. Too few of the realm → roam the field (`:70-71`). Else siege job, then `AutonomousBattlegroundWave.ShouldGather`
   at 3,500 from the keep (`:79-93`); gatherers fight only what comes at them (filters in three places, finding 8).
4. Wave goes (most gathered, or half after 4 min) → guards by the frontier's `FindRvrTarget` (`:129`).
5. Lord exposed → `AutonomousBattlegroundKeepRally.Decide` → rally outside the breach → storm (`:94-120`).
6. Capture → `RVR_KEEP_TAKEN battleground=true`; `EndTarget` finds no event.
- **Out of scope of the field tactics throughout** (`RvrScopeRefusal.BattlegroundAssault`).

### 1.5 The separate records of the same thing

| question | records (all [read]) | count |
|---|---|---|
| **where the enemy is** | `RvrPlanningView` (whole registry, per tick); `RvrRoamPrey` snapshot (3 min, all frontiers); `RvrPerceptionMemory` (per bot); `RvrFightBoards` (per group, 3,500, no ray); `RvrRoamBox.Tracked`; `RvrObservers` budget; chat `ObserveEnemies`; `CompanionPvpEngagement.State.Threats`; keep `LastAttackedByEnemyTick`, `KeepCombatPressure`, `DefenseAlarms`, `BattlegroundSiegePressure`; the brain's aggro list; battleground wave counts | **13** |
| **what our group is doing** | coordinator `Session`/directive `Phase`; `_rvrIntent` per bot; leader TempProperties `RvrWarbandIntent`, `RvrDefendingKeep`, `RvrEventForce`; event `Attackers/Defenders/ThirdRealm/Slots/Travel/Present/Mustered`; `RvrParties` kind; `RvrFightBoards` plan and stance; `RvrRoamBox` walk; duty lease; objective assignment; `RvrAtObjective` point; `BotSiegeRuntime`; battleground `Waves` and keep rally; chat promise | **15** |
| **who is my target** | `TargetObject`; aggro list; `plan.CalledTarget`; `_rvrInterruptSubject`; `_rvrCcSubject`; `RvrRoamBox.Hunting`; `_pvpWatch` / `_ignoredPvpTargets`; `_reachWatch`; `_rvrStanceLeashed`; companion `Focus`; siege engine target; `_battlegroundStormLord` | **12** |
| **what objective is live** | `Events`; `CarrierEvents` + `CarrierTargets`; `Campaigns`; `Cooldowns`; `SelectedKeeps`/`SelectedRelics`; `ReleasedForces`; `DefenseAlarms`; battleground keep state; `CompanionRaidSiege` requests; `RealmEventRecords`; chat promises | **11** |

### 1.6 Places that pick a target, a destination, engage or flee

- **Target:** at least 12 pickers (finding 3).
- **Destination:** `ChooseRvrDestination`, the event layer's `Plan`, the roam override, the roam turn's next spot,
  the chat promise, the committed plan, the rally and march, keep travel, defense posts, the relic escort, the
  battleground, frontier transport. 22 writes to `_rvrDestination`.
- **Engage:** the threat scan (`FrontierThreat.cs:260-268`), `ExecuteRvr :2101-2111`, the battleground (4
  `SetCurrentState(AGGRO)`), the defensive pull, the companion, the solo gate (`RvrSoloGate`), the size-up stance.
- **Flee:** the party RUN (PvE only, `AutonomousBotGroupCoordinator.GroupRun.cs:23`); the field stance retreat
  (`RvrEngagement`, groups in scope only); the solo bail (anyone ungrouped, `BotBrain.Retreat.cs:132`); the relic
  carrier run; the siege "repel" (event-level, by timer); the PvP stalemate and unreachable breaks
  (`FrontierThreat.cs:26-120`); the chase leash; the battleground chase drop. **Eight**, each with its own trigger.
  A group at a keep has none of its own.

### 1.7 Version-tagged special-case rules and where they sit

Proxy: comments naming a release, a work package, an issue, a date or an incident, in the 93 RvR files
(26,421 lines). It undercounts untagged rules.

| tag | rule comments | mostly in |
|---|---:|---|
| 1.1.x | 9 | realm event chat and records |
| 1.2 | 45 | event layer 5, scope 3, muster posts 3 |
| 1.2.1 | 0 | - |
| 1.2.2 | 12 | event records 4, lord formation 2, relic 2, battleground rally 2 |
| WP0-WP7 (field 1.2) | 106 | `RvrTacticsScope` 18, `BotBrain.RvrTactics` 15, positioning 19 |
| an issue (#4xx) | 23 | roam circuits 8, battleground rally 4 |
| a date (2026-xx-xx) | 166 | **event layer 43**, battleground rally 23, battleground controller 13 |
| the owner, a PC, a build, a run, a sweep | 232 | **event layer 39**, battleground rally 30, battleground controller 22, rally 11 |

- `ExecuteRvr` alone carries 13 such comments in 260 lines.
- **Reading:** the field package (WP0-6) was designed first and tagged by package. The siege and battleground code
  grew run by run: 398 dated or incident comments, most of them a rule added after a log.

### 1.8 RVR_* log tags by writer

| layer | tags | which |
|---|---:|---|
| event layer (+ Defense, Relic, Dashboard, relic persistence) | 30 | `EVENT_*`, `SIEGE_MARCH/STARTED/PRESENCE`, `DEFENSE_*` (6), `MUSTER_EXTENDED`, `RALLY_DELAY`, `MARCH_LEADER_REPLACED`, `HOME_DEFENSE_BREAKOFF`, `RETAKE_STARTED`, `RELIC_CAMPAIGN*` (8), `RELIC_CARRIER_*`, `RELIC_STATE_*`, `REINFORCEMENTS`, `KEEP_TAKEN` |
| world controller partials | 19 | `ROAM_*` (5), `KEEP_ROUTE_*`, `KEEP_TRAVEL_STALL`, `LANE_ASSIGN/FALLBACK`, `MARCH_DETOUR/PUSH`, `LORD_*`, `SIEGE`, `DEFENSE_POST_RETRY`, `FRONTIER_WAIT_LONG`, `NOTICE`, `SPOT`, `BORDER_STAGING` |
| field rules and brain | 17 | `FIGHT_BOARD`, `ASSIST_*`, `CC_ASSIGN`, `INTERRUPT`, `PEEL`, `BACKLINE`, `KITE`, `CHASE`, `SIZE_UP`, `WATCH`, `STANCE`, `RETREAT`, `REGROUP`, `STEALTH_OPEN`, `SCAN_BUDGET` |
| coordinator | 3 | `PARTY_FORMED`, `PARTY_WAIT`, `SIEGE_PARTY_FORMED` |
| catalogs | 5 | `CHECKSPOT_*`, `LANE_LOADED/REJECTED` |
| other | 2 | `BOT_KILLED` (realm points), `FRONTIER_DEPARTURE` |

No tag is written by the threat scan's engage, by `ChooseRvrDestination`, by companions or by the director.

---

## 2. Judgement: can this structure pass for a real realm war?

**Not by adding rules.** It can produce the parts of one, and the parts are often well made. It cannot keep them
consistent, because each part runs on its own picture of the world.

- **What a real frontier was (reference-draft 1.4-1.6):** arrivals in waves at milegates; roamers finding each other
  and the soloers at the same few spots; the front keeps traded several times a day; deep keeps taken for relics and
  Darkness Falls; stealthers seen only close up; everyone, alarms included, knowing only what someone saw.
  **(check rubric: section 2-5 of reference.md for group fights, stealth, sieges)**
- **What the structure does:** decides where to go from perfect knowledge of the bots and no knowledge of the player;
  fights with period eyes in the field and perfect eyes at a keep; plans as a group from perfect knowledge within
  3,500; announces sieges to the defending realm the moment they march; and moves the body through a ladder of
  26 precedence steps plus 8 flee rules.
- **The joins multiply.** About 6 decision layers × 4 places (field, keep, battleground, relic) × 4 bot kinds
  (gamebot group, gamebot solo, companion, pet) × 3 perception models. A rule covers one cell; the owner plays in many.
- **Evidence the method shows it:** the 1.2 field work fired in the owner's log at volume (1,022 assist lines, 750 CC
  assignments, 340 kites on `19e7037`), yet the roaming eights found 4 hunts in 96 checks, and the "tactics look like
  players" row is not done **[played, PLAYED-CHECK-1.2.md:345-346]**. The parts work; the chain from "know" to
  "find" does not.

**Structural causes, named**
1. **No one record of what a bot knows** (finding 1, 5).
2. **No one record of what a force intends** (finding 2, 6).
3. **Two fight engines split by a moving scope line** (finding 4).
4. **Target picked first by a spreading rule, then by a focusing plan** (finding 3).
5. **Sieges and defenses exist only as attacker-opened events** (finding 7).
6. **A third siege system for battlegrounds** (finding 8).
7. **No owner of movement; fallbacks that teleport or stand** (finding 9).
8. **Fixes land in the path where they were seen** (section 3).
9. **The log tells what, not why** (finding 10).
10. **Dead and duplicated rules stay green** (finding 6; the director's test).

**What I would not blame**
- The pure rule files (perception, assist, CC, interrupts, peel, stances, lanes, circuits, rosters): small, pure,
  unit tested, period-minded. They are the parts the target is built from.
- The event layer as an owner of sieges: one lock, one place, rich logs. It should own more, not less.
- `RvrTacticsScope` as an idea: one test, asked first. It should choose a profile, not switch off.

---

## 3. Structural causes, traced through past RvR bugs

For each: which join failed, and did the fix land as a rule in one path? All fixes are **tests only** unless marked.

| bug (source) | join that failed | fix and where it sits | one path? |
|---|---|---|---|
| Hlidskialf fell while Albion's army mustered (2026-10-20; comment `:2024-2027`) | the committed plan returned before any re-plan, so home defense never got a turn | home-defense check at the top of `ExecuteRvr` (`:2028-2037`) | **yes**: frontier only; the battleground and companions have no equivalent |
| Defenders called only when the battle began (1.1.3 RC nights; `:1026-1031`) [played] | the defense learned of the siege from the event's battle start, not from anything seen | `CallDefendersAtMarch` in the event layer | one place, but by omniscience ("scouts") |
| 6,500 frontier departures an hour (run 16; `:2556-2559`) | re-planning across frontiers every minute | 20-minute frontier commitment in `ChooseRvrDestination` (`:2560`) **and a copy** in `RvrRoamRegions` (`RvrTactics.cs:180`) | two copies |
| #417 eights walk past fights | the roam override replaced the event layer's hunt | `EightFollowsHunt` special case inside the override (`RvrTactics.cs:128-140`) | yes: a rule at the join, not a shared intent |
| #416 lord reach | target pick vs. the lord's 400 damage rule | battleground storm reach, then "Frontier sieges too" (CHANGELOG-LIVING-SERVER.md:485-491; `Relic.cs:352`); companions "keep their own paths" (:251) | **two paths fixed separately, a third left** |
| Relic audit L3: guards before the relic | target pick ran before the relic | relic-first check at `:2088` **and** `:2202` | two copies |
| Relic audit L4: carrier stood and fought | four layers could give the carrier a fight | rule in `Tick :555`, `ExecuteRvr :2021`, threat scan `FrontierThreat.cs:140`, `:2084` | four copies |
| Run 10: 44 "attackers" grinding PvE (`:445-446`) | the duty changed, the event reservation stayed | `RemoveForce` on assignment change (`:447-448`) and on group join (`:485`) | two copies |
| Thidranki gatherers pulled into guards (`3d67b6f`) [played] | the threat scan and the combat FSM didn't know about the wave's hold | filter in the threat scan (`FrontierThreat.cs:217-237`), drop in `Tick` (`:585`), clause in `FindRvrTarget` (`:2301-2304`) | three copies |
| Sieges held 55 defenders for four hours (2026-10-07; `:28-32`) | the event ended only by timer | `RepelMilliseconds` in the event layer | **one place** |
| Regroups flapped (`19e7037`, #304) [played; fix **seen** on `4be6fab`, PLAYED-CHECK-1.2.md:342] | relays between groups | relay rule in `RvrEngagement` | **one place** |
| Muster posts no walk reaches (`43c43aa`) [played] | the post was placed without the mesh | posts proved in `AutonomousRvrMusterPost`, plus a 4,000-unit move fallback | one place, plus a visible fallback |
| Enemies hunted into frontier dungeons (2026-10-19; `:2569-2572`) | strategic hunt had no notion of where it is safe to follow | filter in `ChooseRvrDestination` only | yes: roam prey and the threat scan don't share it |

**Count:** of 13 fixes, 3 landed once in a layer that owns the decision (repel, relays, muster posts); 10 are rules in
one path or copies in two to four.

---

## 4. What is sound

- **`AutonomousRvrPerception` and `RvrStealthNotice`:** a period rule (sure radius, outer range, chance per look,
  reaction delay, facing, party notice, observer cap) as pure code; seen working against the player on `3d67b6f`
  **[played]**. Make it the only eyes.
- **The event layer:** one lock, staged musters, caps per realm, repel, retake, campaigns, rich `RVR_*` rows. The right
  owner for every objective.
- **`RvrFightPlan` / `RvrAssist` / `RvrCrowdControlPlan` / `RvrInterruptPlan` / `RvrPeelPlan` / `RvrEngagement`:** the
  right shape for "one group plan, every member obeys with a human delay".
- **`RvrTacticsScope`:** one test, asked by every hook; the right seam for profiles.
- **`AutonomousRvrDashboard.OnKeepTaken` / relic pad events:** native events end objectives; no ownership is faked.
- **`AutonomousRvrMusterPost`:** posts proved on the mesh once and shared.
- **Lane and check-spot catalogs:** geography as data, validated at start (`RVR_LANE_*`, `RVR_CHECKSPOT_*`).

## What I could not check

- **No C# run, no world sim.** Every behaviour claim is from reading. Counts are greps (appendix A); a few `TargetObject`
  writes are clears or PvE.
- **The base server's guard and keep code** (guard aggro, stealth detection by guards, door and lord damage rules):
  not read. The stealth-blind claim is about bots, not guards.
- **Navmeshes:** not read; the movement claims are about who issues orders, not whether routes exist.
- **The final rubric** (#493): period judgments use the draft's section 1 and the README; marked **(check rubric)**.
- **Whether Old Frontiers battlegrounds had towers:** the code has `BG_TOWER_CLIMB` and tower lord reaches (250); the
  draft says no towers in Old Frontiers keeps (1.2, 1.10). **(check rubric)**
- **How often each finding bites per hour:** needs the owner's log and the rows of section 6.

## What only the owner's PC can settle

- **Finding 1:** stand still in Emain at 3,000 from a bot group, out of its members' line of sight (behind a hill).
  Does an `RVR_FIGHT_BOARD` line for that group list you, and does an `RVR_STANCE`/`RVR_WATCH` follow, with no
  `RVR_NOTICE` naming you? Do bots near you say "inc" before any `RVR_NOTICE`?
- **Finding 4:** stealth up and walk to a keep under a bot siege; note the range at which defenders on posts attack
  (no `RVR_NOTICE` expected).
- **Finding 5:** level in Pennine for an hour; count `RVR_ROAM_HUNT` lines near you (expected: none name you).
- **Finding 7:** attack a keep's guards with one character; time from first hit to `RVR_DEFENSE_*` lines and the rally
  announcement.
- **Finding 9:** count `RVR_KEEP_ROUTE_RESTART` and pocket-escape lines per hour during a siege muster.

---

## 5. Target structure, in steps

No rewrite. Six pieces. Each starts as a pass-through or a shadow that logs what it would do. Order is section 7.

### A. One trace of why a bot acted (build first)
- **What:** one row per *change* of a decision, written where it is made:
  `RVR_DECIDE t= bot= force= layer= what=<destination|intent|target|engage|flee|legs> from= to= rule=<code> why=<fields>`.
  `rule` is a short code per site (`exec.committed`, `exec.homeDefense`, `roam.override`, `chat.promise`,
  `scan.distributed`, `plan.call`, `bg.gatherHold` ...). Change-only, so cost scales with decisions, not ticks.
- **Replaces:** the free text of `SetRvrStatus` as the only record (74 sites keep their text for the dashboard and
  gain a code); the per-tag budgets for decision rows.
- **Steps:** (1) add the row type and write it at the 26 `ExecuteRvr` steps, the threat scan's engage, the brain's
  target override and the 8 flee rules; (2) a CI test that every `SetRvrStatus` and every target write in RvR files
  passes a rule code (a ratchet that only falls).
- **Size S-M. Risk low** (log volume; change-only).
- **Removes:** "why did it do that" as a question nobody can answer; makes every later piece measurable.

### B. One contact table per bot and group (one perception model)
- **What:** `Contacts` = what this bot or group knows of each enemy: first seen, last seen, where, how (noticed,
  called by a mate, hit by it, heard an alarm, a keep's alarm), confidence decaying with time. **Written only** by the
  perception rule, calls, damage and alarms. **Read** by everyone: the threat scan, the fight board, the roam
  circuits, `ChooseRvrDestination`'s hunts, chat alarms, keep defense.
- **Profiles, not bypasses:** the "at objective" rule becomes a profile (a wall defender has a wide sure radius and
  looks often; still finite, still stealth-aware by the period rule) instead of `RvrFieldRules.None`.
- **Replaces:** `RvrPlanningView.Candidates` as a hunt source, `RvrRoamPrey`'s registry sample, the board's area
  search, `ObserveEnemies(nearby)`, the objective bypass. A realm's strategic view becomes the union of its bots'
  contacts (what its people have seen), which is also what makes the player a contact.
- **Steps:** (1) build the table as a facade written by today's `PerceiveRvrTargets` and calls; (2) the board reads
  contacts in shadow and logs the difference ("listed but unknown"); (3) switch the board; (4) hunts and roam weights
  from realm contacts; (5) the objective profile, behind a switch, measured against siege balance.
- **Size L. Risk M:** fewer fights at first (the realm must search); siege balance moves at step 5 (owner decision 1).
- **Removes:** finding 1 and 5 as classes; finding 7's omniscient scouts (an army is called when a contact sees it).

### C. One objective and intent model (the event layer owns every objective)
- **What:** every live objective is an `Objective` in the event layer: field hunt, roam circuit, keep siege, small
  strike, keep defense (raised by a keep alarm for any enemy), relic carrier, battleground keep, a companion raid
  siege, a chat promise. Each force has **one** `ForceIntent` there (objective, role, slot, since, why). The roam
  override, the chat promise and the battleground *propose* to it; the event layer decides and logs.
- **Replaces:** `_rvrIntent`, the `RvrWarbandIntent`/`RvrDefendingKeep` TempProperties, the fake battleground intent,
  the director, `AutonomousRvrObjectiveState.Next`, the three siege thresholds and the unused healer count (one
  `ForceStrength` from `RvrRoles`).
- **Steps:** (1) delete the director and its test, the unused field and the controller's dead 35 (S); (2) `ForceIntent`
  written by the event layer, read alongside today's fields, mismatches logged; (3) followers read the force intent,
  not the leader's TempProperty; (4) the roam override and chat promise become proposals; (5) battleground keeps and
  small strikes as objective kinds; keep alarms for any enemy.
- **Size M-L. Risk M.** **Removes:** finding 2, 6, 7, 8 as classes.

### D. One group fight plan with roles every member obeys, everywhere
- **What:** the plan (call, CC, interrupts, peel, stance) is the only target authority for a gamebot group, from the
  first pick. `RvrTacticsScope` chooses a profile (field, siege attacker, wall defender, storm, raid) instead of on/off;
  a profile sets which plan parts run and their numbers.
- **Replaces:** `SelectDistributedRvrTarget` in the threat scan (the scan proposes contacts; the plan picks), the
  battleground's own pickers, `FindRvrTarget`'s ordering (the siege profile's kill order), the "at objective" refusal.
- **Steps:** (1) field: the scan asks the plan for the first target (S); (2) the siege attacker profile for guards and
  doors (M); (3) the wall defender profile (M); (4) the storm profile, absorbing the battleground rally's pickers (M).
  Steps 2-4 need the owner to lift decision 1, one profile at a time, each against the 35-65% siege band.
- **Size L in total. Risk H at keeps** (balance), low in the field.
- **Removes:** finding 3 and 4 as classes; the copies in section 3 rows "lord reach", "relic first", "gatherers".

### E. One movement arbiter (legs lease)
- **What:** a per-bot lease `Legs(owner, priority, until, why)` with a fixed order: retreat > relic carrier >
  combat positioning > formation > objective travel > patrol > idle. A path request without the lease is refused and
  logged; a lost lease is logged as `RVR_DECIDE what=legs`.
- **Replaces:** the implicit precedence of `BotBrain.Think`, `Tick` and `ExecuteRvr`; the duplicate checks in the
  ladder (siege job, rally, relic pickup twice each).
- **Fallbacks:** no in-region move of 4,000 as a route fix; "Awaiting" and the 90 s refresh become a patrol the
  arbiter owns (move, look, move on), never a stand.
- **Steps:** (1) the lease as a log-only shadow around today's calls; (2) enforce for retreat and carrier; (3) the rest.
- **Size M. Risk M** (pathing side effects; the existing leg-proof code helps).
- **Removes:** finding 9; the "four copies of the carrier rule" class.

### F. One flee model
- **What:** one `Disengage` decision with inputs (odds from contacts, wipes, health, duty) and profiles (party RUN,
  field stance, solo bail, carrier run, siege fall-back), owned by the plan for groups and by the brain for solos.
- **Replaces:** eight flee rules (section 1.6). Gives a group at a keep a retreat it lacks today.
- **Size M. Risk M.** After D.

### Delete
- `AutonomousRvrDirector` and `UT_AutonomousRvrDirector` (a green test of dead code).
- `AutonomousRvrObjectiveState.Next` (no caller); `Force.HealerCount` until something reads it.
- The controller's `siegeReady` level 35 (`:2684`), dead behind the event layer's 50.
- Duplicate checks in `ExecuteRvr` once the arbiter owns the order.
- **Keep:** the incident tests; stop treating a new one as the fix.

---

## 6. Measuring

**What already exists to build on:** 76 `RVR_*` tags (section 1.8), `BG_*` and `RELIC_*` rows, `RVR_BOT_KILLED`,
`AUTONOMOUS_BOT_DEATH`, `FORK_BUILD`; `source/tools/played_check_log.py` (reads the owner's log per build, masks player
names) and its checker rows (`rvr/porter_wait`, the regroup row of #419); `/lswhy`.

**Capture: an hour of frontier play as an auditable record**
- **Missing rows to add first** (piece A, no behaviour change): `RVR_DECIDE` at every site in sections 1.3 and 1.6;
  `RVR_ENGAGE` from the threat scan (bot, target, kind, range, list it came from: noticed / firing on realm / at
  objective / called); `RVR_PLAN` for every `ChooseRvrDestination` (hunt and roam too, with the rejected candidates'
  count); `RVR_COMPANION_ENGAGE`; `RVR_LEGS_LOST`; `RVR_TELEPORT` for every in-region move and pocket escape of an RvR bot.
- **`/odd [note]`, shared with the chat plan:** one command, one row `ODD_FLAG seq= t= player-zone= nearest-fight=
  nearest-siege= note=`. RvR adds the nearest fight id (`RvrAssist.FightId` exists) and siege id, so a flag lands on a
  timeline.
- **Per-fight timeline tool:** `source/tools/rvr_fight_timeline.py LOG` groups `RVR_NOTICE`, `RVR_ENGAGE`,
  `RVR_FIGHT_BOARD`, `RVR_ASSIST_*`, `RVR_CC_ASSIGN`, `RVR_INTERRUPT`, `RVR_PEEL`, `RVR_STANCE`, `RVR_RETREAT`, deaths
  and `RVR_DECIDE` by fight id into one readable page, split by `FORK_BUILD`, player names masked as
  `played_check_log.py` does. Logs stay out of git.
- **Per-siege timeline:** the same for `RVR_EVENT_OPENED` → `RVR_EVENT_ENDED` (the rows exist; the tool doesn't).

**The numbers that predict the player's experience (per hour, per build, from the owner's log)**
- time from entering the frontier to the first enemy *noticing* the player; to the first fight;
- encounters within 3,500 of the player, and how many were bots that came *looking* (a plan naming the player);
- share of bot-on-bot notices with a delay under 1 s by range band (today's `19e7037` reading: about 1 s everywhere);
- board-listed enemies never noticed by any member (finding 1; from the shadow of piece B);
- target switches per fight per member, and the share of a group's damage on the called target;
- seconds watched bots stood "Awaiting" or in the 90 s patrol; in-region moves and pocket escapes per hour;
- sieges opened per hour, captures, defenders present at battle start, small strikes (0 today);
- deaths at milegates and front keeps vs. deep zones **(check rubric: the 80% Emain reading)**;
- owner flags per hour.

**CI gate or report**

| measure | gate | report |
|---|---|---|
| every RvR decision site writes a rule code (piece A) | yes, a ratchet that only falls | |
| no production class without a caller among `Rvr*`/`AutonomousRvr*` (the director case) | yes | |
| target writes and path requests outside the plan / arbiter in RvR files | yes, a ratchet | |
| the pure rule tests (today's ~86 RvR test files) | yes (they run today) | |
| shadow diffs: board vs. contacts, intent vs. force intent | | yes, on simulated evenings |
| siege attacker win rate in the 35-65% band | | yes; a gate only once a sim matches a PC log |
| the per-hour numbers above | no | yes, per build, owner's PC only |
| `/odd` flags per hour | no | yes |

---

## 7. Recommendation: what to build first

1. **Before the next frontier session on the owner's PC: piece A (the trace) and the missing rows,** `/odd` shared with
   the chat plan, and the fight and siege timeline tools. No behaviour change. It turns the session into a record an
   agent can audit fight by fight, with the layer and rule behind every decision.
2. **Delete the dead layer** (piece C step 1): the director and its test, the unused healer count, the controller's
   dead threshold, `ObjectiveState.Next`. Small, safe, and it removes a misleading green test.
3. **Piece B steps 1-2 (contacts as a facade, the board in shadow).** The shadow diff answers finding 1 with numbers
   from the owner's log before anything changes.
4. **Piece D step 1 (the field scan asks the plan for the first target).** Small, field-only, inside owner decision 1.
5. **Piece C steps 2-4 (one force intent; the override and the chat promise as proposals).** Closes the
   "destination decided four times" class.
6. **Then** the legs arbiter (E), contacts for strategy (B steps 3-4), and only with the owner's agreement the keep
   profiles (B step 5, D steps 2-4) and the battleground as an objective (C step 5), each judged against the siege band
   on a sim and then on the PC.

**Where a fix would land once, not in many places**

| class of defect | lands once in |
|---|---|
| who knows about an enemy (perception, alarms, hunts) | `Contacts` written by `AutonomousRvrPerception` (B) |
| where a force goes and why | `ForceIntent` in `AutonomousRvrEventLayer` (C) |
| who a member attacks | `RvrFightPlan` with profiles chosen by `RvrTacticsScope` (D) |
| what moves the body | the legs lease (E) |
| when to leave a fight | one `Disengage` model (F) |
| why it happened | the `RVR_DECIDE` row (A) |

---

## Appendix A: how the counts were made (read-only)

```
B=source/server/GameServer/bots; A=$B/autonomous
# layers (files and lines): ls/cat | wc -l over the file groups named in table 1.1
# RVR log tags and writers
grep -rhoE '"RVR_[A-Z][A-Z0-9_]+' --include=*.cs source/server/GameServer | tr -d '"' | sort -u     # 76
# callers of a decision maker (0 production callers for AutonomousRvrDirector.Choose)
grep -rn 'AutonomousRvrDirector\.' --include=*.cs source/server/GameServer
# version-tagged rule comments in the 93 RvR files (Rvr*, *Siege*, *Keep*, *Relic*, *Battleground*, *Frontier*,
# RealmEvent*, the coordinator's RvrTactics, BotBrain.Rvr*, BotRvrAmbush, BotPvpCrowdControl, Companion PvP/siege)
grep -hE '//.*(20[0-9][0-9]-[0-9][0-9]-[0-9][0-9])' $FILES | wc -l                                  # 166
grep -hE '//.*(owner|PC|build [0-9a-f]{6,}|[Rr]un [0-9]+|playtest|sweep)' $FILES | wc -l            # 232
# decision writes
grep -rcE 'TargetObject = [^n]' --include=*.cs $B        # 125 in 30 files
grep -rcE '_rvrDestination = ' --include=*.cs $B         # 22 in 5 files
grep -rn  '_rvrIntent = ' --include=*.cs $B              # 5 in 3 files
```

---

## Hand-back summary

- **Verdict:** the parts are good; the structure can't keep them consistent. Report only; tests only; nothing run.
- **Layers:** goal quota, coordinator, event layer, world controller (13 RvR partials + a 26-step `ExecuteRvr`),
  battleground, perception, field rules, brain, companions: about 93 files, 26,400 lines. The original director has no
  caller and a green test.
- **Three perception models in one bot:** omniscient strategy over bots, period eyes, an omniscient group board within
  3,500. Perception is off at keeps (instant and stealth-blind). The player is invisible to every strategic layer.
- **Decided more than once:** the destination up to four times a turn (event plan, roam override, chat promise, the
  leader's TempProperty); the target spread first, then focused by the plan; 12 target pickers, 8 flee rules.
- **Separate records:** 13 of where the enemy is, 15 of what the group is doing, 12 of who is my target, 11 of
  what objective is live.
- **Fixes by path:** of 13 traced fixes, 3 landed once in an owning layer; 10 are rules in one path or 2-4 copies.
- **Target:** (A) one `RVR_DECIDE` trace; (B) one contact table; (C) one force intent in the event layer;
  (D) one plan with profiles; (E) one legs lease; (F) one flee model. Each starts in shadow.
- **Build first:** the trace, the missing rows, `/odd` and timeline tools; then delete the dead layer; then contacts in
  shadow.
- **For the main session to consider saving:** none; nothing here came from memory.
