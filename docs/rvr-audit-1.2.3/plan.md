# RvR audit 1.2.3: the plan (synthesis of the ten reports)

Issue #504, part of the RvR audit (#506). **Docs and issues only: no game behaviour changes.** Written 2026-11-24 on `main`
at `63b99f02`.

**Inputs.** All ten reports:
- On main: [reference.md](reference.md) (#493), [structure.md](structure.md) (#494) and [evidence.md](evidence.md) (#495).
- Unmerged when this plan was written, read from their PR branches: `roam.md` (PR #514, #496), `stealth.md` (PR #515,
  #497), `siege.md` (PR #512, #498), `defense.md` (PR #511, #499), `pathing.md` (PR #516, #500), `mechanics.md` (PR #517,
  #501) and `war.md` (PR #513, #502).
- The hand-back comments on #493-#502, and the realism plan #489 (its say gate #475 and `/odd`).
- The owner's decisions of 2026-11-24.

**Evidence.** Every claim in this plan comes from the reports, and their levels carry over. Almost all of it is
**[read]** or computed. The played figures come from builds older than today's RvR code (evidence.md F5). The
exception is the 1.2.2 battleground counts. **Nothing in this plan is seen working.** Each work issue repeats the
evidence rules: a fix is "tests only" until an owner's PC log shows it, and that log names the build.

## Short answer

- **The parts are mostly good. The joins and the measuring are not.** All seven topic audits found the structure's
  classes again, in their own ground:
  - bots are monsters to the base rules;
  - the player is invisible to strategy, and the war reacts to him by formula;
  - perception is omniscient in one path and blind in another;
  - the same decision is made in several places;
  - fallbacks are seen.
- **About 90 findings (ten per report, plus the smaller notes) fold into 15 work packages.** Two are instruments, one is a class of rules (player/bot parity),
  five are the structure's pieces B-F, and seven are classes of behaviour with small fixes first.
- **Order:**
  1. Record and measure first: #505 with piece A, then #519 and #520.
  2. Then the small, safe fixes that each package lists as its step 1.
  3. Then the structural pieces, each in shadow (logging what it would do) before it acts.
  4. Keep fights change only with the owner's word, one profile at a time.
- **1.2.3 is held** until the audits and fixes are in (owner, 2026-11-24). The plan does not say what part of it ships
  in 1.2.3. That is the owner's call, after a PC session on a build that carries #505.

## 1. The classes of defect, and where each lands once

Pieces A-F are structure.md's target structure (§5). "PR" in the last column is the parity class from mechanics.md.

| # | Class of defect | What a player sees (worst) | Lands once in | Issue |
|---|---|---|---|---|
| 0 | No audit trail; the log says what happened, not why | nobody can say why a bot acted, or how a fight went | piece A, `RVR_DECIDE` and the missing rows | #505 |
| 1 | The measuring grades itself | green tests and "Seen" rows that say nothing about today's play | the checker, the test names, the Seen stamps | #519 |
| 2 | No test runs a fight | "groups fight like groups" has no outcome test | a CI fight harness | #520 |
| 3 | A PvP rule keyed on `is GamePlayer` | bots get monster mez, interrupts, crits, speed, no rez sickness, fast regen, RP with no window | one `IsPlayerCharacter` predicate (PR) | #521 |
| 4 | Stealth rules not shared; stealthers that give themselves away | bot stealthers walk in visible and cost nothing; bots uncover the player like monsters | one stealth-rules module, then stealther behaviour | #522 |
| 5 | Sieges and defenses decided by formula | 24-240 bots and a horn for one guard hit; every attack 100+; scouts that don't exist | one alarm for any enemy, sizes by who is there (pieces B, C) | #523 |
| 6 | Objectives with no map and no clock | Emain no busier than Hadrian's Wall; deep keeps fall as often as front ones; 04:00 = 21:00 | objective weights and an hour factor (piece C) | #524 |
| 7 | Siegecraft stand-ins visible as cheats | engines from nothing; doors nobody repairs; the player's ram a seventh as strong | siege jobs and the ram crew rule | #525 |
| 8 | Shipped data and mesh one-offs | Hib keeps at level 1; DF shut on a fresh world; no home eights; Odin's Gate gap; lifts | release data and spot fixes, PC-gated | #526 |
| 9 | No seat for the human | no RvR group takes him; companions don't peel enemy players; musters don't wait | the force model counts a human (pieces C, D) | #527 |
| 10 | After the kill | the move-on ignores his death; inc calls are made up; no emotes; rank inflation | the kill event and the say gate | #528 |
| 11 | Three perception models in one bot | groups that know where you are; hills that don't hide; keeps blind to bot stealthers | piece B, contacts | #529 |
| 12 | One decision made by several layers | warbands that turn round; no zerg; the battleground a third siege system | piece C, force intent | #530 |
| 13 | Two fight engines; a plan without the kit | spread first picks; no PBAoE; no MoC/SoS/BoF/PR/QC; wall defenders walk out | piece D, one plan with profiles | #531 |
| 14 | No movement owner; seen fallbacks | bots blink to their leader; statues under a rock; melee that never moves | piece E, the legs lease | #532 |
| 15 | Eight flee rules | an eight never gives up a runner; retreats in a straight line through walls | piece F, one flee model | #533 |

## 2. Deduplication: findings that more than one audit made

Each row is one defect, reported several times. It is fixed once, in the issue named.

| Defect | Reported in | Issue (step) |
|---|---|---|
| Mez on a bot follows monster rules ("enraged" under 75%, NPC immunity) | roam 5, mechanics 2, rubric 3.3 | #521 (4) |
| Bots paid RP for every player death, no window, no grey floor | roam 10, mechanics 9 | #521 (1) |
| No realm-ability actives beyond four: SoS, MoC, BoF, PR, Vanish, True Sight; no QC | roam 8, mechanics 6, stealth 10, pathing 2 (escapes) | #531 (2) |
| One hit on a keep guard: horn, size line, 24-240 bots, 4 h, certain retake | siege 7, defense 2, war 2, structure 5 and 7 | #523 (1) |
| Defenders by a 0.75 share, called at the march by scouts that don't exist | defense 1 and 10, structure 7 | #523 (3) |
| Every bot attack is a full siege; no small strike | siege 1, structure 7, rubric 9.3 | #523 (4) |
| Uniform keep targets and roam anchors; no front, no Emain, no milegate | siege 2, war 4, roam 1, structure 6 | #524 |
| No hours: flat RvR duty and siege pace | siege 3, war 3 | #524 (4) |
| Hibernian keeps nGed, Da Behnn and Ailinne at level 1, Crauchon at 4 | siege 9, war 8, rubric 9.2 | #526 (1) |
| The player is outside RvR groups, tactics and musters | roam 6, war 1, mechanics 10, siege 6, structure 4 and 5 | #527 |
| Bots uncover a stealthed player like monsters; bot stealthers never uncovered; defenders stealth-blind | stealth 2 and 3, defense 7, structure 1 (corrected by stealth 3) | #522 (1, 2) |
| Stealthed bots move at full speed | stealth 2, mechanics 4 | #522 (2) |
| Hurt bots crawl; bot sprint does nothing | mechanics 4, stealth 6 | #521 (6) |
| Strategy is omniscient (hunts every enemy bot, sees through stealth, stale points) | structure 1, stealth 8, roam 3 | #529 (4); the stealth filter now in #522 (1) |
| Line of sight is a 2D walkable-mesh ray; bot-on-bot casts unchecked | stealth 4, mechanics 8 | #529 (6), #521 (8) |
| Teleports and snaps in sight; "end by shortcut" | structure 9, pathing 3, siege (other) | #532 (1) |
| Field vs keep engines: no movement, no hold, no retreat at keeps | structure 4, pathing 1, defense 3 | #531 (3), #532 (2) |
| Flee: leash on a moving centre; straight-line retreats; stealthers never run | roam 7, pathing 2, stealth 6, structure F | #533 |
| Duplicated thresholds, a dead healer count, a dead director | structure 6, siege (other) | #530 (1) |
| The battleground is a third siege system; one capture in eight builds | structure 8, evidence F6 | #530 (5) |
| Inc calls not tied to state; chat alarms hear the pre-ray list | war 7, structure 1 | #528 (2), #529 (2) |
| No emote after a kill | war 10, roam ("also seen") | #528 (3) |
| Who wins at equal numbers is unknown | roam §5, evidence F2 | #520 |
| The missing log rows (fight id, player notice and death, siege stall, hour roll-up, teleports) | evidence §4, structure §6 and 10, every topic audit's tools | #505 |

**Battleground fill** (war 6) is not filed here. On the owner's 1.2.2 world, Abermenai had 62 bots (Hibernia 37
against 12 and 13), Thidranki 5, and Murdaigean and Caledonia 0 [played, 1.2.2]. A separate agent is investigating
this and files its own issue. #526 points to it.

## 3. The order of work

### Phase 0: instruments first (no behaviour change)
1. **#505, record real frontier play, with piece A.** The consolidated row list is in the #505 comment of 2026-11-24:
   - `RVR_DECIDE` at every decision site, with a CI ratchet;
   - the fight id, `RVR_FIGHT_OPEN`/`END`, `RVR_PLAYER_NOTICED`/`KILLED`, `RVR_SIEGE_STALL` and `RVR_LORD_BLOCKED`;
   - `RVR_TELEPORT`, `RVR_STEALTH_UNCOVER` and `RVR_EVENT_OPENED reason=`;
   - the RvR-hour and player-seat lines, and the realm-rank histogram;
   - `/odd`, shared with #475, and the fight and siege timeline tools.
   Build this before the next PC frontier session.
2. **#519, honest measuring.** The checker reads `time_since_first_los_ms`, the regroup gate uses the exact checks,
   siege outcomes are counted, the misleading tests are renamed and the Seen rows are stamped. Tools only.
3. **#520, the fight harness** (CI, report-only rows). This can run in parallel. It is L, so don't let it block Phase 1.

### Phase 1: small, safe fixes (each package's step 1)
Each is S, with low or stated risk, and lands where its class belongs, so it doesn't need re-doing later.

| Issue | Step 1 | Needs |
|---|---|---|
| #521 | RP window and grey floor; drop the extra 2.5 s interrupt on bots | - |
| #522 | Bots out of `UncoverStealthAction`; stealth filter on planning; duo follower hides; ambush scope check | `RVR_STEALTH_UNCOVER` from #505 first |
| #530 | Delete the dead director, `ObjectiveState.Next`, the dead 35 threshold and the substring healer count; add the caller check | - |
| #525 | True status text; crew for the player's ram; defending operators keep their engines; ram output line | - |
| #528 | The move-on counts the player; true inc calls (through #475); kill emotes; rank histogram | the say gate #475 for the words |
| #533 | The chase leash measured from the fight's start, plus a time limit | - |
| #532 | `RVR_TELEPORT` gate; no statues; the keep detour for every RvR walk; spread arrivals; no "Awaiting" stand | `RVR_TELEPORT` log first |
| #527 | Companion peel counts enemy players, bots and pets | - |
| #523 | Player-attack defense ends on quiet; no event for a lone guard pull; horn and size line off; retake by chance; relic freeze per frontier; lord barred only by route doors; claim hold; muster place named | **owner: Goal 11** for (a)-(e) |
| #524 | `reason=`/`depth=`/`defendersNear=` on `RVR_EVENT_OPENED`; then keep weights | - |
| #526 | Data: Hib keep levels; DF holder; home circuits; Odin's Gate; lifts | **owner** (levels, DF) and **PC** (P3-P5) |

### Phase 2: structural pieces, in shadow first
Each piece logs what it would do beside today's code. It switches only when the shadow difference, read from a PC
log, says it is safe.

1. **#529, piece B, contacts:** facade → board in shadow → switch the board → strategy from realm contacts.
2. **#530, piece C, force intent** in shadow → followers read it → the override and the chat promise become
   proposals. Then the battleground keep, the small strike and the realm force (zerg) become objective kinds.
3. **#531, piece D:** the field first pick; the plan's kit (PBAoE, AE mez opener, realm abilities, QC, caster
   depth and cover).
4. **#532, piece E:** melee contact movement; the legs lease in shadow, then enforced.
5. **#533, piece F:** the RvR retreat point; then one `Disengage` model after D.
6. Built on B and C: **#523** steps 3-4 (alarms and sizes by who is there), **#524** steps 3-5 (anchors, the clock,
   the split by zone and hour), **#527** steps 2-5 (the player's seat), **#522** steps 2-3.

### Phase 3: keep fights (owner decision 1)
These are the keep profiles: perception at an objective (#529 step 5), wall defender, siege attacker and storm (#531
step 3), and a keep fall-back (#533 step 3). One profile at a time. Each is measured against siege outcomes per night
(#519 step 3) and a PC night before the next one starts.

### Rules for every package
- **One class, one place.** A new word list, a new per-path rule or a third copy of a check is a sign the fix is in
  the wrong place (structure.md §3).
- **Raise, never weaken.** Keeps, guards, lords and bosses are never weakened (AGENTS.md). Siege balance is moved by
  who answers, never by weaker doors or guards.
- **Default on.** Any new player-facing switch defaults on, with a `LivingServerSettingsUpgrade` line. Diagnostics stay
  off.
- **Tell bot types apart.** Say for each change whether it touches autonomous gamebots, companions or the player.
- **Close on play.** An issue closes only on a PC log of a named build. Fixers don't write their own acceptance
  scenario (#519 step 9).

## 4. What needs the owner's PC

The checklists of all ten audits are folded into one deduplicated list on #503 (comment of 2026-11-24). It is grouped
as before the session, perception and stealth, fights, the player's seat, keeps and sieges, movement, whole-log counts,
and offline probes. Each row names its issue.

Issues that cannot close without the PC: every one but #520. Whether #519 needs it depends on the step: the old-log
re-read and the probe runs do. Issues that need the PC before their first step: #526 (P3-P5) and the shadow switches of
#529, #530 and #532.

## 5. The new issues, in recommended order

| Order | Issue | Title (short) | Phase | Size |
|---|---|---|---|---|
| 0 | #505 | Record real frontier play, with piece A (existing; rows consolidated) | 0 | M |
| 1 | #519 | Honest measuring: checker, misleading tests, Seen stamps | 0 | S |
| 2 | #520 | End-to-end RvR fight harness in CI | 0 | L |
| 3 | #521 | Player/bot rule parity: one predicate for PvP rules | 1 | M |
| 4 | #522 | Stealth: shared rules, stealthers that play like 2003 | 1 → 2 | M |
| 5 | #530 | Piece C: one force intent (dead layer deleted first) | 1 → 2 | M-L |
| 6 | #525 | Siegecraft stand-ins: engines, doors, operators, the player's ram | 1 | S-M |
| 7 | #528 | After the kill: move-on, inc calls, emotes, rank measure | 1 | S |
| 8 | #533 | Piece F: one flee model (leash first) | 1 → 2 | M |
| 9 | #532 | Piece E: one movement owner (fallbacks first) | 1 → 2 | M |
| 10 | #527 | The player's seat: groups, companions, musters | 1 → 2 | M-L |
| 11 | #523 | Sieges and defenses by who is there | 1 → 2 | M-L |
| 12 | #524 | Where and when: front weights, reasons, the clock | 1 → 2 | M |
| 13 | #526 | World data and map one-offs | 1 (PC) | S each |
| 14 | #529 | Piece B: one contact table | 2 | L |
| 15 | #531 | Piece D: one fight plan with profiles | 2 → 3 | L |

#529 is listed late because it is L. Start its steps 1-2 (the facade and the shadow) early, beside Phase 1: #523, #524
and #527 depend on it.

## 6. What stays out of this plan

- **Battleground fill:** the separate investigation's issue (see §2).
- **Chat beyond frontier incs:** the realism plan #489. Frontier lines go through its say gate (#475).
- **Raids and the Darkness Falls raid AI:** not RvR. They are covered by docs/RAID-SIZES.md and its own rules.
- **Navmesh rebuilds:** never global (AGENTS.md). Only hash-guarded spot fixes, with PC evidence (#526).

## Owner decisions needed

1. **Lift owner decision 1** (RVR-FIELD-1.2.md:293, "fights at a keep keep today's rules"), one profile at a time:
   - perception at keeps (#529 step 5);
   - the wall defender, including the committed-defender clause at `BotBrain.cs:3435` (#531 step 3);
   - the siege attacker and the storm (#531 step 3);
   - a keep fall-back (#533 step 3).
2. **Goal 11 defense sizing** (2026-10-07). For a player attack, #523 proposes:
   - end on 10 minutes of quiet;
   - no event for a lone guard pull;
   - no horn or size line to the attacker;
   - a retake by chance;
   - later, defenders sized by who is free nearby, for bots and players alike.
3. **Door self-repair** (5% per 30 minutes; not period, rubric 9.3): keep it as a fallback beside a bot repair job, or
   replace it (#525 step 3). Also: is boiling oil period at 1.65? The rubric disagrees with itself (#525 step 4).
4. **Raise Dun nGed, Dun Da Behnn and Dun Ailinne (level 1) and Dun Crauchon (level 4) to 5** in
   `release-world-defaults.json`, with a one-time repair for existing worlds (#526 step 1). This is a raise, which
   AGENTS.md allows, but it changes a world default.
5. **Player/bot parity:** bots take the player branch of the PvP rules (#521). This revisits RVR-FIELD decision 9,
   "keep the server's current values": the bots get the monster set. Also: do bots keep the fast regeneration floor in
   PvE?
6. **The war's clock:** an hour-of-day curve on RvR duty and siege pace, as a default-on setting. The bot schedule
   stays off. Also: a per-realm phase, and the Emain share target. The 55/30/15 split (decision 2) varies by zone and
   hour (#524).
7. **Frontier invites:** a bot RvR party may take a human who joins it, and companions get RvR duties (#527). The
   2026-11-24 invite decision on #485 covers PvE groups only.
8. **Synthetic siege engines:** a visible build, an engine budget and a replacement cooldown, within the 2026-10-07
   "skip the shopping trips" decision (#525 step 2).
9. **Darkness Falls on a fresh tied world:** shut, or open to all until the first flip; and the 15-minute grace, a
   later-patch rule (#526 step 2).
10. **A bot realm-rank brake**, after the `RVR_RANKS` histogram is in. Never touch the player's points (#528 step 4).
11. **Measuring:**
    - drop or freeze the regroup share bar, which was the owner's (#519 step 2);
    - give #416, #417 and #418 a "shipped, owed in play" state (#519 step 8);
    - say what measures siege balance, since the 35-65% band is sim-only (#519 step 3, #523).
