# RvR audit 6: keep, tower and relic attack (siege.md)

Issue #498, part of the RvR audit (#506). **Report only. No game behaviour changed.** Read from `origin/main` at
`de53d0d3`. Framework and format: [README.md](README.md). Judged against the period rubric `reference.md` (#493, in
its PR; cited as "rubric §n"). The structure review `structure.md` (#494) and the evidence ledger `evidence.md` (#495)
are cited, not repeated. Keep *defense* is #499's; this report takes the attacker's seat and the player's.

**Evidence tags** (as in `evidence.md`): **[played]** seen on the owner's PC with a human playing, build named;
**[PC log]** the owner's test copy running bots only, no human (`docs/NIGHT-SIEGES-AND-MARFACH.md`; by AGENTS.md this
is a test, not "working"); **[unit]** a unit test of a pure rule; **[read]** read from the code only. No C# was run,
no server started. Paths are under `source/server/GameServer/` unless they start with `docs/` or `source/tools/`.

## Short answer

- **The chain exists end to end and is mostly sound in mechanics**: one owner (the event layer) opens every bot siege;
  the army musters at its portal outpost, marches as one column, opens on arrival, rams and engines go on the gate,
  the lord dies by the native pipeline, the keep changes hands. 13 of 23 sieges captured in one bot-only night, 2 of 12
  the night before [PC log, `168f55d`, `616190a`]. A played keep take exists only on builds before the current siege
  and lord code (`evidence.md` rows 28-29, stale).
- **What fails the period is the shape, not the count.** About one keep changes hands an hour, all night, every night
  (era-like on a busy server). But every attack is a 100-plus army (rubric 5.6: an empty keep fell to 1-3 FGs); targets
  are picked uniformly, so a deep keep falls as often as the front keep (rubric 1.2 "Fails"); the clock is flat round
  the hour (rubric 2.3); and engines appear from nothing on the spot (rubric 5.2).
- **The player is a guest, not a member.** Bots never wait for, invite or follow a human; a player's ram works at about
  a seventh of a bot ram's rate and takes one of the two ram places; a player who swings at a keep alone raises a
  four-hour, 24-plus-bot defense. There is no quiet small take for anyone, bot or human.

## Takes per evening, from the director's constants [read; checked against PC log]

The live scheduler is `AutonomousRvrEventLayer` (the old `AutonomousRvrDirector` has no caller, `structure.md` §1.1).

| Step | Constant (file:line) | Value |
|---|---|---|
| New-siege roll | `SiegeStartChancePerMinute`, `AutonomousRvrEventLayer.cs:433` | none running: 25% + 25%/idle minute; one: 4%/min; two: 1.2%/min |
| Per-realm catch-up | `RealmCatchUpChance`, `:446` | +2%/min after 30 min since that realm last *opened* one, max 50% |
| One attack per realm | `:590-592` | a realm with a siege open joins no second; any relic in motion blocks all (finding 10) |
| Muster | `HomeMusterMilliseconds` `:803`, extension `:804`, `MarchThreshold` `:817` | 12 min + one 5 min; march at 85% of assigned, max 108; else at the deadline with whoever is there |
| Relic muster | `:811-813` | 25 min + up to six 10 min extensions (85 min) |
| Open on arrival | `ArrivalQuorum` `:821` | 60% of the marched army at the posts, or the march leader arrives |
| Battle end | `:1736-1745`, `RepelMilliseconds` `:33` | capture; no attacker at the keep for 10 min; 4-hour timer; "Rally failed" at 1 hour |
| Cooldown | `TargetCooldownMilliseconds` `:35` | 12 min on the keep |
| Caps | `RealmEventPolicy.cs:42-43,90-91` | 128 attackers (trimmed to 112/96 by `SiegeDefenseBalance`), defenders 0.75-1.0 of them, third realm 64 half the time |

Because the catch-up clock runs from the *opening* and a siege takes 30-120 minutes (muster 12-17, march 9-37
[PC log], battle 9-243 [PC log]), a realm's catch-up chance is already at or near 50% a minute when its siege ends.
**So each realm runs one siege after another, three at once, all day and night.** That gives about 1.8-2.5 sieges
an hour server-wide (computed); the PC logs show 23 in 12.8 hours (1.8/h) [PC log, `168f55d`]. With captures of 17-57%
a night, **a 4-hour evening sees about 7-10 sieges and 2-5 keeps change hands**, spread evenly over all 21 keeps.
The rubric's busy-server picture (front keeps several times a day, deep keeps rarely, relic raids rare and late,
small night strikes) has the right total but none of its distribution.

## Top 10 findings, worst first (ranked by how often a player sees them)

### 1. Every bot attack is a 100-plus army; there is no small take
- **Player sees:** at any keep attack, 65-128 attackers (reinforcements fill to the cap within a minute of the battle
  starting [PC log, NIGHT-SIEGES §4: "Keep 50 marched with 12 and fought with 116"]), plus 30-128 defenders and
  sometimes 64 of the third realm. Every siege, roughly two an hour. Never one or two FGs taking an empty keep.
- **Chain link:** choice and muster. `ChooseCore` has one attack kind (`AutonomousRvrEventLayer.cs:618-671`); the muster
  waits for 108 (`:817`); a siege-capable force is just 4 bots at level 50 (`:618`). `SiegeKind` admits it: "Today every
  bot attack is a full siege" (`:1068-1073`). Reinforcements are drawn to the attacker cap by `PulseDefense`
  (`AutonomousRvrEventLayer.Defense.cs:270-330`).
- **Period:** an undefended front keep fell to 1-3 FGs in 10-20 min; a defended one drew 50-150 over three realms
  (rubric 5.6). Fixed musters fail the rubric explicitly (rubric 9.3). Owner direction 2026-11-05 (NIGHT-SIEGES §3):
  "small raiding parties or attacks are still realistic from time to time". Not built.
- **Evidence:** [read]; sizes [PC log]. **Class:** one size for every objective; missing objective kind.
  **Where it belongs:** the event layer (already the single owner of sieges; `structure.md` finding 7 and piece C).
- **Fix:** a `keep-strike` kind: 1-3 eights, chosen when the target's realm has few RvR bots near it, muster 3-5 min,
  no reinforcement draw, logged as its own kind and weighted down in `SiegeDefenseBalance` (the owner's plan). Full
  sieges muster to the realm's `realmRvr` pool, not 108. **Size M. Risk M** (siege balance; must not become free wins).

### 2. Targets are picked uniformly: a deep keep falls as often as the front keep
- **Player sees:** Caer Renaris or Dun Ailinne (deep) attacked as often as Caer Benowyc or Dun Crauchon (front); the
  same keep never matters more than another; no reason given beyond "Warbands are gathering".
- **Chain link:** choice. Candidates are every enemy keep in the frontier the opening warband stands in
  (`AutonomousWorldBotController.cs:2560-2640`, comment at `:2615`: "All legal reachable frontier keeps get equal
  selection weight"); `ChooseVariedTarget` (`AutonomousRvrEventLayer.cs:368-375`) is a shuffle bag shared by all three
  realms. Defenders present, keep level, depth, Darkness Falls and relic plans do not enter it; `AssaultPressure`
  (`:1916`) is computed and unused. The only weighted case is the relic feeder (`:604-616`, 12% of choices).
- **Period:** "difficulty by depth", front keeps flip several times a day, deep ones taken to open a relic or flip DF
  (rubric 1.2); "deep keeps flipping as often as the front ones" is listed under Fails (rubric 1.2).
- **Evidence:** [read]. **Class:** choice blind to geography and purpose. **Where:** event layer target choice.
- **Fix:** weight candidates by depth (distance from the attacker's portal outpost), by enemy presence (few defenders:
  strike; many: skip or full siege), and by a reason (DF count, relic plan, retake of one's own keep); log the reason
  in `RVR_EVENT_OPENED`. **Size S-M. Risk L.**

### 3. The war has no clock: three sieges at once, round the hour
- **Player sees:** the same tempo at 21:00 and 04:00; a new realm-wide siege notice within minutes of the last one
  ending; centre-screen "has laid siege to" lines for all three realms several times an hour
  (`AutonomousRvrEventLayer.cs:1622-1627`).
- **Chain link:** choice timing (table above; `:433-470`). No time-of-day or population input anywhere in the siege
  code [PC log: "No clock in the code", NIGHT-SIEGES §2].
- **Period:** prime-time zergs, quiet nights won by small forces against sleeping realms; relic raids late at night
  (rubric 2.3, 5.6). The owner's bot schedule is off by default (AGENTS.md), so population is flat; the siege
  scheduler must make the curve itself.
- **Evidence:** [read], [PC log]. **Class:** pacing by fixed probability, not by the state of the war.
  **Where:** event layer scheduler. **Fix:** a per-hour siege budget by the server's local hour (busy evening, thin
  night), night attacks as strikes (finding 1), relic raids only at night or very late; keep the per-realm fairness
  roll. **Size S. Risk L.**

### 4. Siege engines appear from nothing, on the spot, and are replaced at once
- **Player sees:** a "heavy siege ram" pops onto the gate approach the moment an operator reaches its spot; a broken
  one is back 15 s later; an army of 64+ brings two rams, a trebuchet, a catapult and ballistas
  (`AutonomousSiegeJobs.cs:16,27`). Nobody hauls wood, nobody builds. The bot's status says "Real siege equipment,
  finite inventory" (`AutonomousWorldBotController.Siege.cs:512`), which is false with synthetic engines.
- **Chain link:** engines. `SyntheticSiegeEquipment = true` (`Siege.cs:33`); with no kit carried the engine is created
  and added to the world directly (`:195,234-247`), no build time, no material.
- **Period:** siegecraft built rams and engines from wood and metal; players hauled the wood (rubric 5.2, 5.3).
- **Evidence:** [read]; "deployed synthetic=true" lines [PC log, NIGHT-SIEGES §4 `RVR_SIEGE action=hit`].
  **Class:** stand-in mechanic visible as a cheat. Owner decision 2026-10-07 (skip shopping trips) binds.
- **Fix (keeps the owner's decision):** a visible build: the operator stands at the spot for the kit's deploy time
  (as a player's /use of a siege kit), a per-siege engine budget sized to the army (wood carried), and a cooldown
  before a destroyed engine is replaced. Fix the status text now. **Size S. Risk L** (slower doors; check with
  `RVR_SIEGE action=hit` timings).

### 5. The player's ram is a seventh as strong as a bot's and takes one of the two ram places
- **Player sees:** they set up a ram at a bot siege; bots stand beside it and do not crew it; it hits for 200 every
  ~18 s, while the bot ram next to theirs hits for up to 1,400 every ~8 s; and while theirs stands, the bots deploy only
  one ram of their own.
- **Chain link:** engines, player's seat. `GameSiegeRam.Crew` counts bystanding bots only when the *operator* is a
  GameBot (`gameobjects/SiegeWeapon/GameSiegeRam.cs:204`); bots cannot mount, so a player's ram has crew = its riders.
  Damage is `BaseDamage + BaseDamage/2 * Crew` (`:231`), reload `10000 + (Level+1)*2000 - 10000*Crew/12` (`:224`).
  The bots' cap of two working rams counts the player's ram (`Siege.cs:198-203`). Computed: bot ram, 12 crew: about
  150 damage a second; player ram alone: about 11.
- **Evidence:** [read], computed. **Class:** a rule made for bots that leaves the human out (`structure.md` finding 5).
- **Fix:** count friendly gamebots within 300 as crew for any friendly ram (the player's included), and let a bot
  operator hop onto the player's ram when the player steps off (it already does: `AbandonedSiegeKind`, `Siege.cs:410`).
  **Size S. Risk L.**

### 6. Bots never wait for, invite or follow a human
- **Player sees:** realm chat says "Warbands are gathering to assault X. Join them at the siege camp!" (`:663-666`);
  the muster is actually at the realm's portal outpost in that frontier (`AutonomousRvrStaging.cs:24-35`), and nothing
  says where. If the player goes there, nothing waits: the march leaves on the bot count or the clock
  (`AutonomousRvrEventLayer.cs:1263`, `MusterAttendance` counts only bots it recorded, `:995`). No bot invites the
  player into a group, no column slows for them, no bot follows the player's lead. A human can only (a) tag along,
  (b) open their own `/raid` siege with companions (`bots/CompanionRaidSiege.cs`), or (c) force one from the event
  controls (`RealmEventControls.cs:108-124`, `ForceStart`).
- **Sound part:** a player who joins a planned bot siege is treated as helping it, not as a new attack
  (`AutonomousRvrEventLayer.Defense.cs:161-167`).
- **Period:** a raid leader called it, people came and the leader waited a little (rubric 5.1).
- **Evidence:** [read]; no played record of a player joining a bot siege (`evidence.md`). **Class:** the player
  invisible to strategic layers (`structure.md` finding 5). **Where:** event layer muster.
- **Fix:** name the muster place in the notice; count a level-appropriate player of the realm at the muster as
  attendance; hold the march up to 2 min for a player who is on the way (they say it in chat, or are within one zone);
  offer a group invite from a warband with room. **Size M. Risk L.**

### 7. A player's own small strike always raises a four-hour, 24-plus-bot defense
- **Player sees:** solo, they hit a guard or the door of an enemy keep at 03:00. Within seconds the horn sounds, the
  centre of the screen says "Your attack on X has raised the alarm! A small defense force..."; at least 24 defenders,
  8 of the player's own realm and the third realm are recruited; the event lives 4 hours. With a group of eight
  companions it is 48. If they take it, the losing realm musters a retake (`StartRetake`, `AutonomousRvrEventLayer.cs:840`).
- **Chain link:** `ObserveKeepAttack` on any guard or door damage by a player (`keeps/Gameobjects/Guards/GameKeepGuard.cs:228`,
  `GameKeepDoor.cs:145`, `AutonomousRvrEventLayer.Defense.cs:57-74`); sized by group size (`:23-32,133-139`); lifetime
  `DefenseResponseMilliseconds` 4 h (`:15,173`). By contrast a three-bot gank of the same guards draws nothing
  (`structure.md` finding 7).
- **Period:** a small night raid on an empty keep was the classic small take (rubric 5.6, 2.3); keep alerts went to the
  owning guild, and the defenders came only if they were online and cared.
- **Evidence:** [read]. **Class:** player path and bot path with different rules. Overlaps #499 (defense); here it is
  the attacker's side: the human cannot make the period's commonest small take.
- **Fix:** size the response to the defenders actually near and to the hour (finding 3), not a floor of 24; one alarm
  path for player and bot attackers alike (`structure.md` piece B). **Size M. Risk M.**

### 8. The lord stays off-limits while any attackable door of the keep is shut; a door off the bots' path stalls the siege to its timer
- **Player sees:** the yard is cleared, the army stands in the courtyard or the lord's stairs, nobody touches the
  lord, and an hour or more later the siege ends "Defended (timeout)" or "repelled".
- **Chain link:** lord room. `FindRvrTarget` blocks the lord while *any* closed attackable door of the keep exists,
  however far (`AutonomousWorldBotController.cs:2286-2290,2300,2316`). Rams go for `SiegeDoor`: gate first, then by
  number and distance (`Siege.cs:361-366`); hand battering only reaches doors within 1,200 of the bot
  (`AutonomousWorldBotController.Relic.cs:26,389-401`). A door the column never comes within 1,200 of, and for which no
  ram spot is reachable (`placement_blocked`, `Siege.cs:213-221`), is never broken. The shipped world has about four
  attackable door rows per frontier keep (87 at 10,000-51,000 health for 21 keeps, `source/tools/release-world-defaults.json`),
  and the 1.65 layout adds a lord-tower door (rubric 1.2).
- **How often:** unknown. Battles of 25-243 minutes are on record [PC log, NIGHT-SIEGES §4]; whether any was this
  stall is not logged (no line names the door that kept the lord safe).
- **Evidence:** [read]. **Class:** a guard rule with no fallback (stall forever). **Fix:** block the lord only by the
  doors between the bot and him (the door set on the keep route to the lord room), log `RVR_LORD_BLOCKED door=...` once
  a minute, and send battering crews to that door. **Size S. Risk L.** Needs the door geometry check (pathing, #500).

### 9. Three Hibernian keeps ship at level 1: doors a quarter of the others, and they guard both Hib relics
- **Player sees:** Dun nGed, Dun Da Behnn and Dun Ailinne fall noticeably faster than any other keep until their first
  capture; Hibernia loses relic sentinels faster than the other realms.
- **Chain link:** data. Shipped levels: every Albion and Midgard keep 5, Crauchon 4, nGed / Da Behnn / Ailinne **1**
  (`source/tools/release-world-defaults.json`, keeps 103, 104, 106). Door health is `BaseLevel * 200 * (1 + (Level-1))`
  (`propertycalc/MaxHealthCalculator.cs:93-94`, `keep_doors_base_health` 200): 10,000 at level 1, 40,000 at 4, 50,000
  at 5 (the shipped rows show 8 doors at 10,000). Level also scales guards and the lord. Hand damage against doors is
  cut 5% per level (`GameKeepDoor.cs:252-255`), so a level-1 door also takes more per hit. After a capture the keep
  resets to `starting_keep_level` (4) (`keeps/AbstractGameKeep.cs:1141`), and unclaimed level-5 keeps drift down to 4
  at start (`:586-587`), so the gap closes once each has fallen once. nGed, Da Behnn and Ailinne each hold a sentinel at
  a Hibernian relic keep (`events/keep/RelicGuardsOnKeepTaken.cs` `CreateGuards`).
- **Period:** a data accident, not a rule (rubric 9.2). **Evidence:** [read] data. **Class:** shipped data drift.
- **Fix:** raise the three to 5 in the release defaults (raising, never lowering: AGENTS.md), plus a
  `LivingServerSettingsUpgrade`-style one-time repair for worlds that still hold level 1 there. **Size S. Risk L.**

### 10. After the take: claims are a 160-second window that rarely opens, and the army walks off
- **Player sees:** the keep changes flag, the army disperses at once (each force is released, `EndEvent`,
  `AutonomousRvrEventLayer.cs:1812-1816`) and re-plans; no guild banner goes up in most cases; the keep sits at level 4
  with its default guards; the next target is random (finding 2). No realm points are given by bot code (the native
  lord death pays as for players [read, not traced]).
- **Chain link:** claim. `AutonomousBotGuildKeeps` tries 8 times, 20 s apart, after `KeepTaken`
  (`AutonomousBotGuildKeeps.cs:22-23,82-101`) and needs `claim_num` (8) members of **one** bot guild inside the keep
  area at that moment, no player of the realm inside, and the guild under `guilds_claim_limit` (1)
  (`:120-151`). Warbands are formed by the coordinator, not by guild, and are released as the lord dies.
- **Evidence:** "keep claims: Not seen" (`evidence.md` row 40); [unit] `UT_AutonomousBotGuildKeeps`; [read].
- **Period:** claiming from 1.47, any full group of 8 from 1.65 (rubric 5.4); claimed keeps upgraded and fell back on
  release (rubric 5.3).
- **Class:** the hand-off from capture to holding loses the army. **Fix:** hold the capturing force at the keep for
  2-3 minutes (rez, regroup, claim), let the claim count the realm's guild members across the capturing force, and log
  `BOT_GUILD_CLAIM_SKIPPED reason=` on the last attempt. **Size S. Risk L.**

## Other defects, smaller or rarer

- **A relic in motion freezes every new siege on the server.** `CarrierEvents.Count != 0` sends every force to the
  reserve (`AutonomousRvrEventLayer.cs:590`); a carrier event lives at least 2 h and is kept open while the relic is
  carried or on the ground (`AutonomousRvrEventLayer.Relic.cs:13,30-56`). Period: a relic run drew the zerg, but the
  other front still fought. [read] Fix: block only the carrier's frontier. **S.**
- **Relic raids only when the victim holds at most 2 sentinel keeps** (`MaxSentinelsForRelicAssault`, `:129,604-616`),
  then 12% of new sieges, strong warbands only (`:621-625`). Each relic keep has six or seven sentinel keeps
  (`RelicGuardsOnKeepTaken.cs` `CreateGuards`), and keeps held by another realm were retaken every time [PC log,
  NIGHT-SIEGES §4: 7 of 7]. So relics move rarely, which fits the period (rubric 1.7) in kind; how rarely is not
  measured. "Relic raids resume as campaigns" is unit only (`evidence.md` row 51). Campaigns open only from
  `ForceStart` (player or harness).
- **Healer count, `CanSupplySiege` and level gates differ by caller** (35 in the controller `:2684`, 50 in the event
  layer `:618`): `structure.md` finding 6. A force of four bots of level 50 with no healer opens a 128-bot siege.
- **End by shortcut.** A keep route that fails twice near the arrival point moves the bot up to 4,000 units
  (`KeepTravel.cs:241-262`, `RVR_KEEP_ROUTE_RESTART`; `structure.md` finding 9); the 15-minute stuck watchdog sends a
  standing bot to its capital (NIGHT-SIEGES §1: 185 raiders at once in a raid). Siege participants are protected from
  it (`ProtectsParticipantFromInactivity`, `AutonomousRvrEventLayer.cs:1387-1428`).
- **Dead attackers release at the portal outpost**, not at their bind (`TrySiegeRelease`, `:1280-1296`). Close to the
  period's portal-keep bind (rubric 1.3); a player's own release still goes to their bind. Fair, but different.
- **The siege notice promises a "siege camp"** while the muster is at the portal outpost (finding 6).
- **Battlegrounds** are a third siege system (`structure.md` finding 8): one capture in about eight PC builds
  (`evidence.md` F6). Not re-audited here.
- **Towers:** none in the shipped world (rubric 9.2), so the tower branches (`GameKeepTower` claim halves, tower lord
  reach 250) are inert in the frontier. Correct for Old Frontiers.

## What I checked and found sound [read unless marked]

- **One owner for bot sieges.** Opening, joining, muster, march, battle and end all live in `AutonomousRvrEventLayer`;
  the portal keeps are never targets (`AutonomousRvrKeepPolicy.cs`, `ChooseOrJoin` `:478-481`).
- **Muster where the period mustered.** At the realm's portal outpost in the target frontier, as the porting ceremony
  put people (`AutonomousRvrStaging.cs:24-35`; rubric 1.3), then one column with a live leader chosen by the main body
  (`MainBodyLeader`, `:1189-1210`).
- **Opening only with the army there.** Arrival quorum of 60% or the leader's arrival (`:821-829,1453-1459`); a
  straggler cannot open a siege alone (fix for run 2026-10-13).
- **Defenders called at the march**, after 12 of 18 keeps fell in 4-11 minutes with defenders called late
  (`CallDefendersAtMarch`, `:1033-1047`; [unit] `UT_SiegeDefenseAtMarch`; no PC count since: `evidence.md` row 30).
- **Home defense first**: a committed attacker breaks off when its own keep's defense is under half
  (`RealmEventPolicy.BreaksOffToDefend`; `AutonomousRvrEventLayer.cs:945-965`).
- **A third realm half the time, after 5 minutes, capped at 64** (`RealmEventPolicy.cs:29-35,91`): matches "often ended
  by the third realm arriving" (rubric 5.6).
- **Doors, not walls.** Bots aim rams and trebuchets at doors, catapults at people, ballistas at engines
  (`SelectSiegeTarget`, `Siege.cs:390-407`); no bot path chooses a wall (`GameKeepComponent` is a legal aggro target
  in `BotBrain.cs:453` but no picker selects one). Walls can still be damaged by the NF engine (`GameKeepComponent.cs:427`).
- **Ram placement is checked on the navmesh** (floor, reach band, crowding, connected corridor, `Siege.cs:311-345`)
  and logs why it fails (`placement_blocked tried= ... unconnected=`). Gate before inner door; postern halves set aside
  (`:361-385`).
- **Doors close again above 15% and regrow 5% per 30 minutes only with no enemy within 3,000** (`GameKeepDoor.cs:14,
  582-593`). Not period (rubric 9.3) but harmless.
- **Lord fight rules** (1.2.2): reach inside the lord's 400 damage radius (`RvrLordReach.cs`), melee in front, casters
  and healers behind on the raid's side (`RvrLordFormation.cs`). [unit only]; never seen played (`evidence.md`
  rows 32-33). Whether a 400-unit immunity existed at 1.65 is not in the rubric.
- **Siege resurrection**: any healer of the army raises the army's dead within 2,000-5,000 (`AutonomousSiegeResurrection.cs`).
  Matches "rez and stay together" (rubric 3.9).
- **Relic rules in kind**: sentinels per held keep, take keeps first (rubric 1.7 matches the 1.65 rule), no porter for
  a carrier (`Relic.cs` `RelicCarrierTurn`), no stealthed carrier picked, the carrier runs and fights only when
  cornered. 8 on the pad (`relic_players_required_on_pad`, owner 2026-10-15) is the owner's, not a period figure.
- **A Herald-style view exists**: `rvr-world.json` (`AutonomousRvrDashboard.cs:27`) with who holds what, live events,
  present and assigned counts.

## What I could not check, and why

- **Navmesh paths into keeps** (gate, courtyard, inner door, stairs, lord room): no frontier navmesh in this container
  (only a test `zone078.nav`); the v0.36b base download was not fetched. Finding 8 and the "owners-only rampart doors"
  of the 1.65 layout (rubric 1.2) need it. Left to #500.
- **Which door rows are attackable and where they stand**: the door table is in the world database, not in git; the
  release defaults give only health and state.
- **Hand damage against doors**: depends on live weapon and spell numbers; I did not estimate bot battering speed.
- **Realm points for a keep take**: native code, not traced.
- **Whether any of the 4-hour battles was the lord-behind-a-door stall** (finding 8): not logged.

## What only the owner's PC can settle (exact things to look at)

1. **One evening, `server.log`:** count `RVR_EVENT_OPENED` and `RVR_KEEP_TAKEN` per hour and per keep; expected from
   the code: about 2 sieges an hour, uniform over keeps. Period target: front keeps most, deep rarely.
2. **Size at a take:** `RVR_SIEGE_PRESENCE present=a/d/3` for each capture. Any capture under 24 attackers present?
3. **Stand by Caer Benowyc or Dun Crauchon during a bot siege:** time from the battle start to the first door
   (`RVR_SIEGE action=hit targetHp`), to the lord (`RVR_LORD_STEP_IN`, `RVR_LORD_FORMATION`), to `RVR_KEEP_TAKEN`.
4. **Bring a ram to a bot siege:** watch whether bots stand at it, and the hit size (`RVR_SIEGE` lines name only bot
   engines; note the door's health per hit from the client).
5. **Go to the muster** (portal outpost) when "Warbands are gathering" appears: does anything wait for you?
6. **Solo, hit one guard of an enemy keep at night:** `PLAYER_KEEP_DEFENSE_SIZED force=1 roster=1 scale=Small defenders=24`
   (since #788 `force=` also counts your realm's gamebots at the keep);
   count what arrives.
7. **Dun nGed, Da Behnn or Ailinne:** `/keep info` or the door's health; level 1 on a fresh 1.2.2 install?
8. **After a capture:** `BOT_GUILD_CLAIM` in the next 160 s, or nothing.
9. **During a relic carry:** any `RVR_EVENT_OPENED` while `RVR_RELIC_CARRIER_EVENT` is open (expected: none).

## Measuring tools worth building

- **Per-siege timeline** (from existing tags): opened, march, started, first door, each door, lord engaged, taken or
  ended; with present counts. Feeds the "RvR hour" numbers (`evidence.md` instruments).
- **`RVR_LORD_BLOCKED keep= door= state= nearestBot=`** once a minute while the yard is clear and the lord is barred.
- **`RVR_EVENT_OPENED reason= depth= defendersNear=`**, so target choice can be graded against rubric 1.2.
- **A per-keep capture table over a week** from `RealmEventRecords` (front vs deep), shown in the Herald view.
- **A ram-output line** per engine per minute (owner, kind, crew, damage), player engines included.

## Hand-back summary

- PR: docs only, `docs/rvr-audit-1.2.3/siege.md`. Everything is [read] or bot-only PC logs; nothing played on the
  current siege or lord code.
- Pace: about 2 sieges an hour, three at once, round the clock; 2-5 keeps change hands in a 4-hour evening. Count is
  era-like; distribution is not.
- Top defects by class, most visible first:
  1. One size for every attack: 100-plus armies, no small take (event layer, `ChooseCore`, `MarchThreshold`).
  2. Target choice blind to depth and purpose: uniform shuffle bag (`ChooseVariedTarget`, controller `:2615`).
  3. No clock: flat per-minute rolls, catch-up from the last opening (`SiegeStartChancePerMinute`, `RealmCatchUpChance`).
  4. Engines from nothing, replaced at once; false "finite inventory" status (`Siege.cs:33,195,512`).
  5. Player's ram gets no bot crew and takes a ram place (`GameSiegeRam.Crew:204`, `Siege.cs:198-203`).
  6. Bots never wait for or invite a human; notice names no place (muster `:1263`, notice `:663`).
  7. A player's solo touch raises a 4-hour 24-plus defense; bots' small ganks raise none (`Defense.cs:57-74,23-32`).
  8. Lord barred by any shut door, no fallback: possible stall to the timer (`AutonomousWorldBotController.cs:2286`).
  9. Data: Hib nGed, Da Behnn, Ailinne ship at level 1 (`release-world-defaults.json`).
  10. Claims: 160 s window, one guild's 8 inside, army released at once (`AutonomousBotGuildKeeps.cs:22-23,147`).
- Also: any relic in motion blocks every new siege (`AutonomousRvrEventLayer.cs:590`).
- Sound: single siege owner, portal-outpost muster, column march, arrival quorum, defenders at the march, home-defense
  break-off, third realm, doors not walls, checked ram spots, siege rez, relic sentinel rule, Herald JSON.
- Fix locations: event layer (choice, kinds, clock, muster attendance, notices, carrier block); siege jobs and
  `GameSiegeRam.Crew` (engines, player's ram); `FindRvrTarget` (lord gate); release defaults (keep levels);
  `AutonomousBotGuildKeeps` plus a post-capture hold (claims). Sizes S-M; the strike kind is the one M with balance risk.
- Could not check: navmeshes into keeps, door positions, battering speed. PC checklist above (9 items).
