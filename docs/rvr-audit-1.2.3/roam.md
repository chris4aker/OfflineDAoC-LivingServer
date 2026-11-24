# RvR audit 1.2.3: roaming groups, the gank squads and the zerg (agent: roam)

**Issue:** #496, part of the RvR audit (#506). **Read from:** `origin/main` at `de53d0d3` (worktree `/home/claude/wt/496`).
**Report only.** No game behaviour changed, no C# run, no server started.
**Judged against** the period rubric [reference.md](reference.md) (#493, unmerged; cited as "rubric §n"). The structure review
[structure.md](structure.md) (#494) and the evidence ledger [evidence.md](evidence.md) (#495) are cited, not repeated.
**Evidence tags:** **[played]** an owner's PC log (build named), **[sim]** a world or in-test sim, **[unit]** a unit test,
**[read]** code reading only, **[calc]** arithmetic below from the code's constants and stated assumptions. Almost all of
this is [read] and [calc]. Line numbers are of `source/server/GameServer/` unless a path says otherwise.

**Short answer**
- **The parts are good and the joins break.** Rosters follow the 2003 skeleton, the odds model is sensible, the kill
  target is never mezzed, the solo gate reads the odds, and bot-on-bot fights are real fights. But the chain is broken
  at the joins. The places groups go ignore the geography of the war. There is no roaming zerg. Warbands hunt a remembered
  point, not an enemy. CC on bots follows monster rules. The player's own group gets none of the group tactics. And the
  player's death is not counted as a kill by the roam rules.
- **What a solo level-50 player meets in an hour** is about the same in all three frontiers: 2-9 random crossings with an
  enemy party while walking open ground, 3-8 enemy stealthers arriving at a milegate, and 3-7 passes by a roaming eight
  at a gate on its circuit [calc, §6]. About half of those parties are soloers or duos, so a fight of his own size is easy
  to find. Escaping a group depends on run speed alone, because the chase leash does not work when the whole group
  chases (finding 7).
- **Who wins an equal-numbers fight is not known.** No test or harness runs one (evidence F2). §5 lists what would decide it.

---

## Top 10 findings, ranked by how often a player sees them per hour

### 1. The war has no geography: every frontier and every zone draws the same traffic, and the milegate draws none
- **Player sees:** Emain Macha is no busier than Hadrian's Wall. The milegate mouth is not the busiest spot. Deep Pennine
  gets as many warbands as Benowyc. Rubric §1.5 asks for about 80% of fights in Emain, the milegate as the busiest spot
  and quiet deep zones. Its Fails line includes "fights spread evenly over all twelve zones".
- **Chain link:** decide (where to go).
- **Where:**
  - Warband destinations are built from three pools over *all* reachable frontiers. Hunts are every enemy RvR bot, taken
    in hashed order (`bots/autonomous/AutonomousWorldBotController.cs:2565-2588`). Patrol anchors are every frontier keep,
    "equal selection weight" (`:2615-2643`). Patrol cells are 24 random monster camps (`:2651-2671`).
  - The event layer picks a roam target with `OrderBy(_ => Random.Shared.Next())` (`AutonomousRvrEventLayer.cs:632-633`,
    `ReservePlan :684-687`).
  - Nothing weights a frontier, a zone, a milegate or the front keep. Only one keep in nine is in each arrival zone
    (Benowyc, Bledmeer, Crauchon), so about 11% of keep patrols land where the enemy arrives [calc]. No milegate is a
    warband anchor at all.
  - The only frontier term is the 20-25 minute commitment (`:2556-2561`, `:2703-2708`). It keeps a group in a frontier;
    it does not choose one.
- **How often:** the whole hour, for every player in every frontier. Warbands are 55% of the RvR bots not in a siege
  (`RvrRosterTemplates.cs:21`).
- **Evidence:** read. No PC log counts fights by zone. `RVR_BOT_KILLED` has a region; zone shares were never reported.
- **Class:** objective choice with no prior from the period's map. It belongs in the objective model (structure piece C),
  not in each picker.
- **Fix:** give each frontier, zone and anchor a weight (the milegate mouths, the front keep and Emain for all three
  realms; deep zones low except leveller camps). Make the milegates warband anchors. Size M. Risk M: it concentrates bots.
  Check the frame budget at the milegates.

### 2. There is no zerg in the field: the biggest roaming unit is one group, and as played it is mostly a pair
- **Player sees:** no 20-50 blob at a milegate or a front keep, at any hour, unless a siege army is marching. Rubric §2.1
  and §2.3 call for a zerg of 20-50 at prime time; its Fails line includes "no zerg".
- **Chain link:** forming.
- **Where:**
  - A "warband" is one `Group` of 2-8 (`AutonomousBotGroupCoordinator.cs:1886-1940`). Recruits must be in the leader's
    region (`:1904`). `AcceptWarband` forms with whatever it finds, down to a pair (`AutonomousRvrStaging.cs:184`).
  - Nothing joins several groups into a roaming force. Multi-group forces exist only as siege, defence and relic events
    (`RealmWarbandSupport`, `AutonomousArmyPush`, the muster). The warband "relay" only passes stances between nearby
    groups (`RvrEngagement.cs:245-254`).
- **How often:** the whole hour. **Played:** on `19e7037`, 178 of 235 warbands were pairs (evidence F10,
  `RvrRosterTemplates.cs:99-101`). The level-50 roll asks for 4-8 (`AutonomousRvrStaging.cs:173-181`), but the
  same-region pool rarely holds that many.
- **Class:** a period force with no owner. The zerg is a third kind of force beside the event army and the group.
- **Fix:** a "realm force" made of the warbands in one zone that follow a loose lead, with no event, slow (no speed),
  staying near the milegate or front keep (rubric §2.1: "Zergs move slow and rarely venture too far"). Draw recruits from
  the frontier, not only the leader's region. Size M-L. Risk M (load, siege balance).

### 3. Warbands hunt a remembered point: one enemy picked by hash, any distance away, refreshed every 45-60 s
- **Player sees:** pairs and groups walking a long way across the zone, stopping in empty fields for 90 s ("Searching
  near X"), then walking off somewhere else. Rubric §3.1: groups scouted and moved on an inc call. Here they walk to where
  a bot was up to a minute ago.
- **Chain link:** perceive → decide → move.
- **Where:**
  - Every force that cannot siege hunts whenever any enemy exists (`AutonomousRvrEventLayer.cs:413-415`). That covers
    every pair and every force under level 50.
  - The hunt is "closest in size", always a tie (`:630-631`), so the first in hashed order wins (structure finding 6).
    That hash mixes enemy id and leader id (`AutonomousWorldBotController.cs:2573`), so the same leader picks **the same
    enemy bot** on every re-plan while it lives. It is a stalker, not a hunter. Distance plays no part.
  - The destination is a `CampDestination` copied from the enemy's position at plan time (`:2580-2585`). It is
    re-planned every 45-60 s (`:2183`). On arrival the group patrols a 700 radius (`PatrolRvr`, `:2725-2741`). After 90 s
    with nobody there it re-plans (`:2259-2266`).
  - A target moving at 250 u/s is up to about 15,000 units from that point after 60 s [calc].
- **How often:** every warband, every 45-60 s. With R = 150 (§6), that is about 14-32 warbands per realm [calc].
- **Evidence:** read. The 90-second refresh and the "Awaiting" stand are visible fallbacks (structure finding 9).
- **Class:** omniscient and stale perception driving movement (structure finding 1, piece B).
- **Fix:** hunt the nearest contact the realm has actually seen, by its last-seen point and age. Drop a contact that is
  too old. Size M. Risk M (fewer meetings until scouting exists).

### 4. The home realm has no roaming eights in its own frontier
- **Player sees:** in Emain, Albion and Midgard eights walk their circuits, but no Hibernian eight ever does. The same
  holds for Albion eights in Hadrian's Wall and Pennine, and for Midgard eights in Odin's Gate. Rubric §1.3: "The home
  realm walks"; Hibs raced to Crim or Bolg, then went into Emain.
- **Chain link:** forming → roaming.
- **Where:**
  - `data/rvr-checkspots.json` has 18 circuits, six per frontier, all for the two *invading* realms: Midgard and
    Hibernia in region 1, Albion and Hibernia in 100, Albion and Midgard in 200.
  - `ChooseCircuitWalk` keeps only circuits of the bot's own realm (`AutonomousWorldBotController.RvrTactics.cs:187-190`).
    So a home eight has nowhere to go at home, and walks an enemy frontier.
- **How often:** the whole hour, for a player in any frontier. A third of the eights that could be there are missing
  (the home realm's), so for a player of an invading realm, half his enemy eights are missing [calc].
- **Evidence:** read (data and code).
- **Class:** catalog data that encodes a one-sided rule.
- **Fix:** add home-realm circuits per frontier: the gate mouths and the front keep for defence, the leveller camps the
  gankers checked. Size S (data plus the catalog walk on the PC). Risk low.

### 5. Crowd control on a bot follows monster rules, not RvR rules
- **Player sees:** as a Sorcerer, Bard or Healer, his mez on a hurt bot fails with "Your target is enraged and resists
  the spell!". A bot can be mezzed again a few seconds after the last mez, at half the time, where a player would be
  immune for 60 s. Bot fights show the same thing from the outside: once both sides are hurt, mez stops landing on
  anyone but a fresh bot. Rubric §3.3: about 60 s immunity in RvR for players and guards; no "enraged" rule exists in
  RvR.
- **Chain link:** fight (CC).
- **Where:**
  - `AppliedImmunityType` is `Player` only for a `GamePlayer`. Every `GameBot` gets `Npc` immunity
    (`ECS-Effects/ECSGameEffect.cs:33`). NPC immunity halves the duration on each new mez in a 60 s window
    (`ECS-Effects/ECSImmunityEffect.cs:38-53`), instead of refusing it.
  - The mez handler refuses any `GameNPC` under 75% health (`spells/CCSpellHandler.cs:174-177`).
  - The bots' own CC plan copies both rules so it does not waste casts (`bots/autonomous/RvrCrowdControlPlan.cs:180-193`).
    That is correct for the planner, and it confirms the rule bites.
- **How often:** every fight where a mezzer, on either side, faces bots. That is most group fights (each realm's eight
  has a CC slot, `RvrRosterTemplates.cs:216-238`).
- **Evidence:** read. **Not** in any test: every tactics test runs on hand-made boards (evidence F2).
- **Class:** a PvE combat rule applied to gamebots in PvP. This is the mechanics audit's ground; it is listed here
  because it changes every roaming fight.
- **Fix:** in RvR, give an autonomous gamebot the player immunity type and skip the "enraged" check (both in the server's
  CC handlers), then delete the planner's copies. Size S. Risk M: bot-on-bot fight length and siege balance change.
  Measure fight length before and after.

### 6. A player in a bot 8-man gets none of the group tactics, and cannot join a roaming eight
- **Player sees:** his group of bots (companions, or gamebots he leads) never calls a target of its own, never plans
  mez, never peels for his healer and never kites. It fights what he attacks and what attacks him. A roaming eight
  never invites him: bot-led invites are refused in the frontier. Rubric §3 describes the 8-man the player wants to be in.
- **Chain link:** forming → fight, for the player's own side.
- **Where:**
  - The scope refuses every bot in a group a player leads or is in (`bots/autonomous/RvrTacticsScope.cs:77-78`).
  - The threat scan skips them (`AutonomousWorldBotController.FrontierThreat.cs:145`).
  - Companions follow the player's target and defend, against enemy bots only (`bots/CompanionPvpEngagement.cs:36-66`).
  - Bot-led invites count only a leader the player can reach outside the frontier, battlegrounds and DF (`AutonomousBotGroupCoordinator.BotLed.cs:28-32`).
- **How often:** the whole session for a player who groups. For a solo player, never.
- **Evidence:** read. Structure finding 4 covers the scope line in general; this is its cost from the player's seat.
- **Class:** rules that hold for bot groups but not for the player's group (an owner decision for 1.2).
- **Fix:** a "player's eight" profile. The player is the main assist, and the CC plan, peel and healer kite run for his
  bots under that call. Then let a roaming eight invite a level-50 player who /lfg's in the frontier. Size L. Risk M
  (owner decision; companions' current rules).

### 7. The chase leash measures from the group's centre, which moves with the chasers: a group never gives up a runner
- **Player sees:** a solo player who runs from an eight is followed by all of it until he is out of sight, at a guard,
  or dead. Rubric §3.7: a group breaks off "when a member cannot follow", and tanks overextend sometimes, not every time.
- **Chain link:** fight → disengage.
- **Where:**
  - `MaintainRvrChaseLeash` measures the bot's distance from `board.FriendlyCentroid` (`bots/BotBrain.RvrTactics.cs:503-512`).
    The centroid is the centre of every friendly member on the board (`RvrFightBoard.cs:137,191`).
  - `StopsChasing` needs the bot past the leash (1,500-2,500, or 2.5 times that when bold) *from that centre*
    (`RvrEngagement.cs:777-793`). When all eight chase, the centre travels with them and the test never fires.
  - The test checks fixed distances only (`Tests/UnitTests/UT_RvrEngagement.cs:561-566`).
  - The 90 s stalemate break needs the bot standing still (`FrontierThreat.cs:31-52`), so it cannot end a chase.
- **How often:** every escape attempt from a group, so a few times an hour for a solo player [calc, §6].
- **Evidence:** read; unit (the pure rule only).
- **Class:** a pure rule correct on its own and wrong at the join (the board feeds it a moving frame).
- **Fix:** measure from where the fight started (the board's first contact point) or from the group's healers. Give a
  pursuit time limit by tier. Size S. Risk low.

### 8. Bots use four realm-ability actives; the ones a period fight turned on are never used
- **Player sees:** no Albion group opens with Speed of Sound. No caster casts through Mastery of Concentration, and no
  Bunker of Faith or Perfect Recovery is ever seen. Purge, Ignore Pain, Second Wind and First Aid are the only actives a
  bot fires. Rubric §3.8 lists Purge, Group Purge, MoC, SoS, BoF, Perfect Recovery and Vanish as in use; §3.4 makes MoC
  a switch trigger, which the design reads (rubric §9.1) but which never happens.
- **Chain link:** fight.
- **Where:** `UsableActives` (`bots/autonomous/AutonomousBotRealmAbilities.cs:47-48`): "every other active is never
  bought". The fight plan's MoC switch trigger therefore never fires against a bot caster.
- **How often:** every group fight. Each long timer should show about once a fight, then not again for 15-30 minutes.
- **Evidence:** read.
- **Class:** a missing actor capability that the plan assumes.
- **Fix:** let bots buy and fire SoS (on the engage), MoC (when interrupted twice), BoF and Perfect Recovery (healer,
  after a death) and Vanish (assassin, losing). Each is one rule in the plan. Size M. Risk M.

### 9. The player's death is not a kill to the roam rules: no move-on after killing the human
- **Player sees:** a stealther or soloer that kills him at a milegate or camp keeps camping it for the rest of its
  2-8 minute stay. A roaming eight that kills him at a spot stays its full dwell. The 3-minute move-on rule, meant as
  the fairness rule against corpse camping (rubric §9.1), protects bots, not the human.
- **Chain link:** after.
- **Where:**
  - Kills are counted only for `box.Tracked`, which holds only the `GameBot` prey that `RvrPreyAt` returned
    (`AutonomousWorldBotController.RvrTactics.cs:258-291, 335-347`).
  - A soloer's prey is levelling bots only (`:270-271`). An eight counts players only towards the odds (`:274-285`).
  - So `RecordKill` (`:338`) never sees the player.
  - Stealthers dwell 4-8 minutes and soloers 2-4 (`RvrRoamCircuit.cs:101-102`).
- **How often:** each time the player dies at a check spot: at a gate, about once an hour or more [calc, §6].
- **Evidence:** read. The roam rules never moved on in play either (0 `RVR_ROAM_MOVE_ON` on `19e7037`, evidence row 25).
- **Class:** the player invisible to a strategic layer (structure finding 5), here in the after-fight rule.
- **Fix:** record any enemy-realm death the walker took part in (the brain's kill event), the player included. Size S.
  Risk low.

### 10. Bots earn full realm points every time they kill the player, and from greys
- **Player sees:** little directly. The bots that farm him at a gate rise in realm rank much faster than a period
  farmer would. Rubric §2.4: nobody corpse-camped one body for long; a fresh kill was worth points only after the
  repeat window.
- **Chain link:** after (realm points).
- **Where:**
  - `PayGamebotsForPlayerKill` pays from `killedPlayer.RealmPointsValue` with no repeat window
    (`bots/autonomous/AutonomousBotRealmPointRewards.cs:275-290`, called at `serverrules/AbstractServerRules.cs:1950`).
  - Players are paid only past `RP_WORTH_SECONDS` (default 300; `AbstractServerRules.cs:2068`,
    `serverproperty/ServerProperties.cs:876`). A dead bot gets the same window (`AutonomousBotRealmPointRewards.cs:126-128`).
  - Neither path has a grey (con) floor, so a level-50 bot is paid for a level-30 victim (worth (30-20)^2 = 100,
    `:24-29`).
- **How often:** every player death in RvR.
- **Evidence:** read. Tests check the formula, not the window on the player path
  (`UT_AutonomousBotRealmPointRewards.cs:95-171`).
- **Class:** a rule copied for one path (bot victims) and not the other (player victims).
- **Fix:** apply `RP_WORTH_SECONDS` and the grey rule in `PayGamebotsForPlayerKill`. Size S. Risk none.

**Also seen, smaller:**
- **No texture after a kill.** No emote follows a PvP kill. The idle emotes in `AutonomousBotBodyLanguage.cs` are town
  and waiting moods, and there is no kill trigger. Rubric §7: "/laugh at the corpse", /bow after a fair 1v1. Size S.
  [read]
- **Long circuits.** The loops run 30,600-191,600 units (computed from the catalog): `hib-alb-2-crauchon` is 30,629,
  `mid-alb-3-jamtland` and `mid-hib-3-jamtland` are 191,625. At 250 u/s a Jamtland lap is about 13 minutes of walking
  plus 7 dwells, so an eight on it is mostly on the road in deep Jamtland. That is period for a leveller gank
  (rubric §1.5), but 2 of Midgard's 6 enemy circuits are this one loop. [calc]
- **Groups never take a size gate against a solo or a grey.** One head skips the watch (`RvrEngagement.cs:197, 507`),
  and the odds against one level-50 head are about 0.9, so a group always engages at once. This is period for a gank
  (rubric §3.10: 3-10 s). No level floor exists, so greys are ganked too; rubric §1.5 calls Yggdra "THE greyganking
  area", so that passes. [read]

---

## 1. Forming (who groups, and with what)

| Link | Code | Period (rubric) | Judgement |
|---|---|---|---|
| Who does RvR | Level 50: 20% solo PvE, 40% group PvE, 40% RvR. Levels 20-49: 15% RvR and 15% battleground (`BotGoalSettings.cs:41`) | Most 50s RvR'd at prime time (§2.3) | Plausible; flat by the hour unless the owner's schedule switch is on (off by default, AGENTS.md) [read] |
| Split | 55 / 30 / 15 warband / eight / small, by deficit (`RvrRosterTemplates.cs:21, 81-97`) | No source (§2.3); fine at prime time in Emain | Assumption; played contradicts it (178 of 235 warbands were pairs; evidence F10) [played `19e7037`] |
| Eight's roster | 2 main healers, CC, shield tank, speed, realm extra, 2 cure-mez, damage in era order (`RvrRosterTemplates.cs:212-238, 245-253`) | §2.2 skeleton per realm | **Sound** on paper [unit `UT_RvrRosterTemplates`]. Hibernia has no realm extra slot, which matches the rubric's Hib eight |
| Short eight | Waits up to 5 min, then forms a warband (`RvrRosterTemplates.cs:103-120`); at least 4 (`:264`) | Pugs were common (§2.2) | Sound |
| Recruits | Same realm, **same region as the leader** (`AutonomousBotGroupCoordinator.cs:1903-1904`) | Groups formed at the border keep or the portal keep and travelled together | Causes the pairs (finding 2) [read; played] |
| Skill tiers | 30 / 50 / 20 casual / average / veteran (`RvrGroupSkill.cs`) | Not period-sourced (§9.1) | Played 32/48/20 across 535 bots [played `c81eb5d`, evidence row 24] |
| Pet cap | `rvr_group_pet_cap` (`AutonomousRvrPetCap`) | "ten Cabalist pets" was a tell (§9.4) | Sound [read] |
| Realm ranks | Earned from kills like a player (`AutonomousBotRealmPointRewards.cs:254-273`); RAs bought along a fixed build (`AutonomousBotRealmAbilities.cs`) | RR2-4 typical, RR5 serious (§2.5) | Formula sound (L50 worth 900 + RR, `:24-29`; rubric 900). The repeat-window leak inflates it (finding 10) |
| Speed | The song's speed pulse plays while travelling out of combat (`BotSongTwistPolicy.cs:86, 106`) | Speed "practically required"; breaks on combat (§3.1) | Sound for Skald, Minstrel and Bard. No Speed of Sound (finding 8) [read] |
| Buffs | Normal bot upkeep (not traced here) | Buffbot-free in OF; self and group buffs | Not checked |

## 2. Roaming (where they go, how they find enemies)

- **Eights** walk a circuit chosen by a weight: 0.15 per spot, plus 4 per enemy leveller at the spot now, plus 1 if one
  was seen in the last 20 minutes, plus 1.5 per enemy RvR bot within 4,000 (capped at 12). Circuits in the bot's own
  region count 1.5 times (`RvrRoamCircuit.cs:111-131, 329-366`; `AutonomousWorldBotController.RvrTactics.cs:185-215`).
  The weights come from a 3-minute snapshot of the whole bot registry (`RvrRoamCircuit.cs:474-510`). **That is
  omniscient:** a circuit is chosen by bots nobody has seen (structure finding 1). Dwell is 30-60 s per spot, 2 laps
  (`:98-107`).
- **At a spot** the walker looks every 3 s (`RvrTactics.cs:85`) and misses a look 5-30% of the time by tier
  (`RvrRoamCircuit.cs:195-200`). It hunts what it sees, never past 6 minutes (`:105, 313`). An eight takes on enemy RvR
  bots only at even odds, +1 or +2 heads by tier (`:181-192`). The hunt walks to within 700 and does not set a target;
  the threat scan must still notice (structure §1.4, step 5).
- **#417's change** makes an eight follow the planner's hunt within 10,000 units (`RvrRoamCircuit.cs:124, 345`;
  `RvrTactics.cs:128-140`). That inherits finding 3's stale, hash-picked target. [unit `UT_RvrRoamFindsFights`; never
  played, evidence row 22]
- **Soloers and stealthers** pick a chokepoint or camp. Stealthers pick chokepoints only, and like company: +1.5 per
  realm-mate camping there, up to 3, and a camp is "full" at 6 (`RvrRoamCircuit.cs:382-390`). Soloers dwell 2-4 min,
  stealthers 4-8 min, hidden (`:101-102`; `RvrTactics.cs:391-392`). **Period match:** stealther packs at gates
  (rubric §2.4) [read].
- **Warbands:** see findings 1 and 3. With nothing found: patrol 700 around the point for 90 s, then re-plan, or
  "Awaiting a frontier objective" when nothing is reachable (`AutonomousWorldBotController.cs:2187-2191, 2258-2266`).
- **Travel shape:** followers use `FollowDynamicGroupLeader`, and field positioning spreads the group by 350 in a fight
  (`RvrPositioning`). The shape on the road was not traced. The pathing audit (#500) owns it.
- **Milegates and camping:** the catalog has five milegates (rubric §9.2; the Hib gate in Odin's Gate is missing). Only
  eights (on 4 of 18 circuits) and stealthers ever stand at one.

## 3. Choosing a fight

| Case | What the code does | Period | Judgement |
|---|---|---|---|
| Group sees one player | No watch for one head (`RvrEngagement.cs:197, 507`); odds about 0.9: engage at once (`:520-555`) | Gank, 3-10 s | Pass |
| Group sees a player and a companion | Two heads: watch 6-12 s by tier (`:199, 325-336`), then engage | Hesitation is a human tell (§7) | Pass |
| Equal group | Odds about 0.5; engage at 0.45 ± 0.06 by boldness, kite from 0.30 to 0.45, retreat under 0.30 ± 0.05 (`:174-193, 317-323`) | FGs ran when a second FG appeared (§3.7); odds 0.30 means the enemy is 2.3 times stronger | Pass on paper [unit `UT_RvrEngagement`] |
| A third-party fight | Size up against the stronger side (`:357-377`); watch | Adds were normal (§7) | Pass |
| Soloer vs player | `RvrSoloEngage.MayOpen`: alone, health at least 70%, power at least 40%, level at most own +1 (+3 from stealth, +1 with a duo partner), 4-20% misreads (`RvrSoloEngage.cs:40-84`) | A stealther opens when he can win, runs when the opener fails (§4) | Pass. The "runs when the opener fails" half is the stealth audit's |
| Player fighting a monster | Groups: no check. Soloers count it as `TargetBusy`, a "weaker" target (`RvrTactics.cs:480`) | Common gank (§1.5 levelling camps) | Pass |
| Greys | No level floor anywhere | Greyganking was real, worth no RP | Pass in behaviour; fails in RP (finding 10) |
| Near guards | No check; a bold bot's leash runs into "the enemy's guards" (`BotBrain.RvrTactics.cs:497-499`) | "you WILL die" chasing into a TK (§1.3) | Pass. The guards kill them natively |
| At a portal keep / bind | `IsSafeArea` counts bind areas as safe (`AutonomousWorldBotController.cs:6283-6290`) | Portal keeps are safe | Pass [read] |

## 4. The fight

- **First target:** spread by database id over the closest enemies, then the call converges (structure finding 3).
  Against a solo player both land on him.
- **Assist:** a called target with per-member follow delays, tunnelling members, and 4-8 s holds (`RvrAssist.cs:22-37`;
  `BotBrain.RvrTactics.cs:144-187`). The kill order is that of rubric §3.4 [unit `UT_RvrAssist`]. 386 calls and 636
  switches fired; "worked" was never measured [played `19e7037`, fired only].
- **CC:** the plan never mezzes the kill target and skips an area mez that would reach it
  (`BotBrain.PvpCrowdControl.cs:73-118`). Against a lone player the plan returns "no CC" (`BotBrain.RvrTactics.cs:248-262`),
  so he is never mezzed by a group in scope. Outside the plan, the old `TryPvpCrowdControl` mezzes the nearest free enemy
  (`BotBrain.PvpCrowdControl.cs:16-59`). Played: Midgard bots "mezzed, rooted and killed him" [played `3d67b6f`, before
  WP2]. The CC rules on bots are monster rules (finding 5).
- **A mezzed target** is left alone while another enemy is free within 1,500, then finished (`BotPvpCrowdControl.cs:28-46`).
  That passes rubric §3.3 ("hold damage on the mezzed").
- **Interrupts, peel, positioning:** each fires [played `19e7037`: 118, 202, 340 kites]. None is judged; that is owed by
  the "group tactics look like players" row (PLAYED-CHECK-1.2.md:346, Not done).
- **Healing, cure, rez:** not traced in depth. Rezzing strangers after RvR is tests only (evidence row 52).
- **Disengage:** a ragged retreat by tier, with holdouts and hit members fighting back (`RvrEngagement.cs:217-223`;
  `BotBrain.RvrTactics.cs:478-486`). The chase leash is broken at the join (finding 7).
- **Fight length:** bounded only by the 90 s stalemate break (bot still and target not losing health) and the
  unreachable check (`FrontierThreat.cs:16-120`). No fight length was ever logged.

## 5. Who wins at equal numbers, and why

Not measured: there is no fight harness (evidence F2) and no damage log. The code points to these deciders, in order:
1. **Who opens with mez on fresh bots.** Mez lands only above 75% health on a bot (finding 5), so the opener is
   everything and a later mez rarely lands. That favours the side whose CC slot is filled (the roster's `cc=` field in
   `RVR_PARTY_FORMED`).
2. **Tier.** Veterans react in 0.3-1.0 s against 0.8-2.0 s for casuals (`RvrEngagement.cs:217-219`) and misread the odds
   less.
3. **Healers.** Each healer adds 15% to power, up to 3 (`RvrEngagement.cs:165-166`). "Lost" is no healers left against
   enemy healers (`:525`).
4. **Pairs.** As played, most warbands are pairs (finding 2), so most "group" fights are 2v2 or 2 against an eight.
- **Bot group against bot group, no player near:** a real fight. Combat thinks every 350-500 ms away from the player
  against 250 ms near him (`AutonomousFidelityPolicy.cs:33-39`; `AutonomousAiBudget.cs:24`). No fight is shortcut or
  simulated [read]. What the player walks into is a fight in progress, but its noticing ran on a coarser clock
  (structure finding 1).

## 6. A player's hour, worked from the constants [calc]

Assumptions, stated: **R** = level-50 RvR gamebots per realm online and not in a siege. R = 60 for a quiet hour, R = 150
for an evening; `ccba364` had 854-871 online per realm, all levels (PLAYED-CHECK-1.2.md:344). Warbands, smalls and the
home realm spread evenly over the three frontiers (finding 1). Eights split over the two enemy frontiers (finding 4).
Speed is 250 u/s with a song and dwell is 45 s at an eight's spot. The walkable frontier is 60% of 4 zones of
65,536². A player walking open ground notices and is noticed at about 2,500 (median 6 s; `AutonomousRvrPerception.cs:178`).
Enemy parties are moving half the time. Of the smalls, 40% are stealthers. The script is in the PR description.

| | R = 60, code's roll (warband 6) | R = 60, as played (warband 2.6) | R = 150, code's roll | R = 150, as played |
|---|---|---|---|---|
| Enemy parties in **any one** frontier | 11 | 16 | 27 | 39 |
| ...of which soloers or duos | about 5 | about 5 | about 12.5 | about 12.5 |
| Random crossings per hour, walking open ground | 2.4 | 3.4 | 6.0 | 8.6 |
| Enemy stealthers camping one milegate at a time / arriving per hour | 0.3 / 3 | same | 0.75 / 7.5 | same |
| Passes by one realm's eights at a gate on its circuit (e.g. MMG on `hib-mid-1-mmg`, lap about 8 min) | 2.8 an hour, 45 s each | same | 6.9 an hour | same |

- **A level-50 solo player in Emain:** the same as in Hadrian's Wall or Odin's Gate, which is finding 1. At an evening R,
  that is a crossing every 7-10 minutes, mostly with a soloer, a duo or a pair. Against a group he dies in seconds or
  outruns it (finding 7). At a milegate he sees a stealther every 8 minutes, and an eight every 9 minutes if the gate is
  on a circuit. The rubric's Emain at prime time is a fight at the gate within 10 minutes, 2-4 FGs a realm and a zerg
  (§2.3, §11). The count is near; the shape (no zerg, no FG at the gate) is not.
- **A player in a bot 8-man:** he meets the same parties. His group is an eight without the plan (finding 6). Against
  a bot eight of equal size, the bots have the call, the CC plan and the peel, and his have only his target.
- **Fairness:** one human can easily find a fight his size: about half the enemy parties are soloers or duos, and the
  solo gate makes them open only on fair odds. He can escape a group only by speed. He is never hunted on purpose,
  which is fair, and is never moved on from after a kill, which is not (finding 9).

## What I checked and found sound
- The eight's roster against the 2003 skeleton for all three realms (`RvrRosterTemplates.cs:212-253`) [read, unit].
- The odds model: monotone, healers and controlled heads counted, misread by tier (`RvrEngagement.cs:283-306`) [read, unit].
- Kill target never mezzed; mezzed enemies left alone while another is free [read].
- Soloer odds gate, with the period's "invisible or join a zerg" logic (`RvrSoloEngage.cs`) [read, unit].
- Stealther packs at chokepoints (`RvrRoamCircuit.cs:382-390`) [read].
- Realm point value of a level-50 kill: 900 + RR, which matches rubric §2.5 (`AutonomousBotRealmPointRewards.cs:24-29`) [read, unit].
- Bot-on-bot fights are real combat at every fidelity (`AutonomousFidelityPolicy.cs:13-16, 33-39`) [read].
- Speed songs pulse while travelling, stop in combat [read].

## What I could not check, and why
- Who wins at equal numbers, fight length, the first death's class: no harness, no damage log (evidence F2).
- Healing, cure and rez chains inside a fight: not traced (time).
- Movement shape on the road and in a fight on real meshes: no navmeshes here; the pathing audit (#500) owns it.
- Perception at objectives and stealth detection: the stealth audit (`stealth.md`, #497) owns it.
- The real per-realm count of level-50 RvR gamebots at any hour: not in any doc; §6 is parametric.

## What only the owner's PC can settle
1. **Where fights are.** One evening's `RVR_BOT_KILLED` grouped by region and zone: is Emain near 80% or near 33%?
   Grouped by distance to the nearest milegate: is the gate the busiest 2,000 units?
2. **Zerg.** `RVR_PARTY_FORMED kind=Warband size=` per hour, and the number of same-realm parties within 3,000 of each
   other outside an event (no tag exists; see tools).
3. **Hunts.** Watch one Midgard warband in Emain for 10 minutes with `/lswhy`: does it walk to empty fields and show
   "Searching near" or "Refreshing frontier patrol"? Count those statuses per hour.
4. **Mez.** As a Bard or Sorcerer at a bot fight: mez a bot under 75% ("enraged and resists"), then mez the same bot
   twice in 30 s.
5. **Chase.** Run from an eight in Emain on open ground: count `RVR_CHASE` lines (expect about none) and the seconds
   until it stops.
6. **Move-on.** Die to a stealther at AMG, release and come back at once: is it still there? `RVR_ROAM_MOVE_ON` should
   not fire (finding 9).
7. **Home eights.** `RVR_ROAM_CIRCUIT region=200 realm=Hibernia` never appears.

## Measuring tools worth building
- **`RVR_FIGHT` row**: one per fight (board contact to last death or break), with the sides, heads, duration, the first
  death's class and zone. It answers §5 and rubric §3.10 at once. Size S-M.
- **Zone and milegate shares** in `played_check_log.py` from `RVR_BOT_KILLED` positions and the check-spot catalog.
  Size S.
- **A force-cluster sampler**: every 5 minutes, same-realm RvR parties within 3,000 of each other. It shows whether a
  zerg ever exists. Size S.
- **The fight harness** of evidence §5.2, with the server's real CC handlers, so finding 5 shows in CI. Size L.

## Hand-back summary
- Report: `docs/rvr-audit-1.2.3/roam.md` (this file). Docs only, no behaviour change. Mostly read and calc; the played
  figures are from `19e7037` and `3d67b6f` via PLAYED-CHECK-1.2.md.
- Top defects, ranked by how often a player sees them:
  1. No geography: frontiers, zones and milegates are drawn evenly (`AutonomousWorldBotController.cs:2565-2671`). Weight
     anchors in the objective model. M.
  2. No zerg in the field: warbands are one group, mostly pairs, recruited from the leader's region
     (`AutonomousBotGroupCoordinator.cs:1904`). Add a slow realm-force kind. M-L.
  3. Warband hunts are a hash-picked, stale point, re-planned every 45-60 s (`:2573, 2580, 2259`). Hunt the nearest
     seen contact. M.
  4. Home realms have no eights in their own frontier (`data/rvr-checkspots.json`). Add circuits. S.
  5. Mez on bots follows monster rules: "enraged" under 75%, halved instead of 60 s immunity
     (`spells/CCSpellHandler.cs:174`, `ECS-Effects/ECSGameEffect.cs:33`). Player immunity type for gamebots in RvR. S,
     risk M.
  6. The player's bot 8-man gets no group tactics and cannot join a roaming eight (`RvrTacticsScope.cs:77`,
     `BotLed.cs:32`). L.
  7. The chase leash is measured from a centre that moves with the chasers (`BotBrain.RvrTactics.cs:503-512`). S.
  8. Four RA actives only; no SoS, MoC, BoF, PR (`AutonomousBotRealmAbilities.cs:48`). M.
  9. The player's death never starts the move-on (`RvrTactics.cs:335-347`). S.
  10. Bots are paid RP for every player death, no repeat window or grey rule (`AutonomousBotRealmPointRewards.cs:275`). S.
- Sound: rosters, the odds model, CC sparing the kill target, the soloer gate, stealther packs, the RP value, real
  bot-on-bot combat.
- Unknown: who wins at equal numbers and fight length (no harness); real per-realm RvR counts.
- PC checks: zone share of `RVR_BOT_KILLED`, statuses per hour, a mez on a hurt bot, an escape from an eight, a death at AMG.
