# RvR audit 5: solo gankers, stealth and detection (agent: stealth)

Issue #497, part of the RvR audit (#506). Report only; no game behaviour changes. Judged against the period rubric
[reference.md](reference.md) (#493, section 4 and the checklist in section 11). It builds on the structure review
[structure.md](structure.md) (#494, finding 1 and the "solo stealther" chain in 1.4) and the evidence ledger
[evidence.md](evidence.md) (#495, F1 and F10, rows 5 and 14). Those are not repeated here. This report adds the base
server's stealth and detection code, which the structure review did not read. One of its claims needs correcting
(finding 3).

Code read on `origin/main` at `de53d0d3`. Evidence tags: **[played]** (a PC log, with its build), **[unit]** (a unit test),
**[read]** (code read only). No sim was run and no dotnet was used. Every claim not tagged otherwise is [read].
Paths are under `source/server/GameServer/` unless they say otherwise.

## The chain, from the player's seat

One bot stealther's hour (all [read]):
1. **Decide.** The coordinator leaves it ungrouped or in a duo (the 15% small share; `RvrRoamSplit.Choose`, a
   stealther leader stays small). `ChooseSoloWalk` picks one of the 24 chokepoints (7 Albion, 7 Midgard, 10 Hibernia
   in `bots/autonomous/data/rvr-checkspots.json`). The weights come from omniscient leveller counts and pack-mates
   (`AutonomousWorldBotController.RvrTactics.cs:217-241`).
2. **Move.** It walks there **visible** at full speed (`:385-386`) and hides only on the stand point (`:391-392`). It
   camps for 4-8 minutes (`RvrRoamCircuit.cs:102`), then **unstealths** and walks on (`RvrTactics.cs:404-405`).
3. **Perceive.** Every 3 s it looks for levelling **bots** only (`:258-271`). The player shows up only in the frontier
   scan (`AutonomousWorldBotController.FrontierThreat.cs:136-269`), by the perception rule (structure.md finding 1).
4. **Decide to open.** `RvrSoloGate` / `RvrSoloEngage.OddsAllow`: the target is alone or weaker, no group near it, and
   the bot is at 70% health or more (`RvrSoloEngage.cs:55-69`). Greys are allowed; no class is preferred.
5. **Approach.** `BotBrain.TryApproachAmbush` (`bots/BotBrain.cs:3668-3680`): a target within 2,000, so it stealths
   (again) and follows at full run speed, to melee range or, for an archer, to 1,000.
6. **Fight.** An assassin uses one stealth style, stepping behind for up to 4 s first (`bots/BotRvrAmbush.cs:72-142`). Then
   it fights normally. An archer unstealths (`BotBrain.cs:3455-3456`) and fires a plain shot.
7. **What next.** It fights to the end unless it is below 30% health while its target is above 50%, or below 50% with
   two attackers (`bots/BotGroupRetreat.cs:151-154`). Then it runs **visible** (it is in combat). Below 33% health it is
   slowed to 20-100% speed (`propertycalc/MaxSpeedCalculator.cs:142-143`). No Vanish, no re-stealth while running
   (`bots/BotBrain.Retreat.cs:247`). After a kill it stands visible until the combat timer ends, then walks back to the
   stand point and hides.

The player as a stealther, against bots (all [read]): enemy bots see him by two rules that do not agree (finding 3).
Guards and monsters see him by the base server's rule. A bot stealther meets neither guards nor monsters (finding 2).

---

## Top 10 findings, worst first

Ranked by how often a player in the frontier would see each one in an hour.

### 1. Bot stealthers walk to their camp visible and vanish on the spot.
- **Player sees:** an enemy Infiltrator or Ranger walks up the road to AMG or HMG in plain view, at 3,000 or more, stops
  at the gate mouth and disappears. Four to eight minutes later it reappears on the same spot and walks off. The camp is
  given away before it starts.
- **Chain link:** move. Stealth is switched on and off only by the roam turn's "Check" and "move on" steps.
- **Where:** `bots/autonomous/AutonomousWorldBotController.RvrTactics.cs:385-392` (walk, then `Stealth(true)` within 100
  of the stand point); `:404-405` (`Stealth(false)` before walking on). No other path hides a travelling gamebot
  (every `Stealth(true)` call: `RvrTactics.cs:392`, `BotBrain.cs:3676`, `CompanionStealth.cs:67`).
- **How often:** every stealther, on every camp. Each one arrives and leaves once every 4-8 minutes plus its walk, so
  at a busy gate it happens several times an hour [computed from `RvrRoamCircuit.cs:102`].
- **Evidence:** read.
- **Rubric:** "Fails: stealthers seen at 1,000+ by a visible player with no tools" (reference.md 4.4); checklist
  "Stealthers in the gap... Fail: seen at range".
- **Class:** stealth state owned by one turn, not by the bot. It belongs in the bot's movement layer: one place that
  says "this stealther travels hidden".
- **Fix:** hide on leaving the portal keep or the bind area, once no enemy is in the "too close" circle. Travel hidden
  at stealth speed (finding 2), and unhide only to fight, to ride a horse or to use a porter. **Size S** (with finding
  2's speed rule). **Risk M:** slower stealthers reach their camps later. Check the roam timers.

### 2. Bot stealth costs nothing: full speed, no "too close" rule, invisible to guards and monsters.
- **Player sees:**
  - A bot assassin you spot at 425 reaches you in about 2 s. A player with the same spec would need about 4 s.
  - A visible bot hides in front of you at 200 units, where a player could not.
  - A bot stealther sits inside a guarded milegate or walks past a keep's guards and is never uncovered. Your own
    stealther is uncovered there.
- **Chain link:** move and perceive. Bots get the player's stealth benefits (`GamePlayer.CanDetectStealthedBot`) but
  none of its costs.
- **Where:**
  - Speed: the stealth slowdown, 0.3 + 0.3 x (spec + 10) / (level + 10), applies to `GamePlayer` only
    (`propertycalc/MaxSpeedCalculator.cs:67-77`). A `GameBot` is a `GameNPC` and keeps 100%, so a spec-30 level-50
    bot moves at twice a spec-30 player's stealth speed.
  - Too close: `GameBot.Stealth` (`bots/GameBot.cs:2158-2188`) checks only relic, pulse and horse. A player's Stealth
    click is refused inside `IsObjectTooClose` of any enemy player or NPC (`skillhandler/StealthSpecHandler.cs:105,123`):
    1,024 - 896 x spec / level, so about 400 for a spec-35 assassin. Only companions follow that rule
    (`bots/CompanionStealth.cs:70-102`).
  - Uncover: `GameBot.StartStealthUncoverAction` is empty (`GameBot.cs:2190-2196`), so no monster or guard ever
    uncovers a bot. Keep guards skip stealthed autonomous bots, even a `GuardStealther` (`ai/brain/Guards/KeepGuardBrain.cs:59`;
    players are skipped only by non-stealther guards, `:40`).
- **How often:** every engagement by a bot stealther (finding 9 puts these at a few an hour server-wide). Uncovering by
  guards and monsters comes up whenever a bot camp is near either.
- **Evidence:** read.
- **Rubric:** reference.md 4.1 ("Monsters and guards see stealthers only at a short range set by their level"); 4.2.
- **Class:** "a gamebot is an NPC". The player's rules are re-implemented piece by piece, and the stealth pieces are
  missing. This belongs in one stealth-rules module that both `GamePlayer` and `GameBot` call.
- **Fix:** route `IGamePlayer` through the stealth speed formula; check `IsObjectTooClose` in `GameBot.Stealth`; give
  `GameBot` the uncover timer for monsters and guards; let `GuardStealther` see bot stealthers. **Size M. Risk M:** bot
  openers get rarer (finding 9), so measure them before and after.

### 3. A stealthed player meets two detection rules from bots, and the one nobody designed is the stronger.
- **Player sees:** "*Haldor uncovers you!*" from a visible enemy Armsman, as if he were a monster. You drop out of
  stealth, and every bot near you then sees you at once. This happens at keeps too, where the design says bots are
  stealth-blind.
- **Chain link:** perceive.
- **Where:** the base server's `UncoverStealthAction` (`gameobjects/GamePlayer.cs:11094-11196`) runs every second for a
  stealthed player.
  - It loops over every hostile `GameNPC` within 1,024 (`:11112`). Its only exception is a pet with a player owner
    (`:11121`), and an autonomous gamebot's owner is null (`bots/BotBrain.cs:113-115`; a bot's pet resolves the same
    way, `ai/brain/ControlledMob/ControlledMobBrain.cs:125-126`).
  - So every enemy gamebot and every pet of one acts as a monster. The radius is 125 + 20 x (bot level - your stealth)
    (`:11126`), plus 125 with Detect Hidden (`:11134`). It sees in a 90-degree front cone and two side cones (only the
    30 degrees straight behind are safe). The chance is 10% a second plus 1% a level (`:11169`), then a client LOS
    check uncovers you (`:11192`). Outside the front cone the bot turns to face you for 10 s (`:11176`).
  - The designed rule, `RvrStealthNotice` (`bots/autonomous/RvrStealthNotice.cs:30-100`), is 350 at most, needs line
    of sight, is 0 while the bot fights someone else, is 0 at an objective (`AutonomousWorldBotController.RvrPerception.cs:27-36`),
    and is logged as `RVR_NOTICE ... stealthLimit=`. The base rule has none of these limits and logs nothing.
- **Correction to structure.md finding 1:** "a stealther at a keep meets bots that are... stealth-blind". That holds for
  a **bot** stealther. A **player** stealther at a keep is still uncovered by every bot defender, by this rule.
- **One-sided:** no enemy, bot or player, ever uncovers a bot stealther (finding 2). A real player in 2003 could only
  *see* you, never uncover you (reference.md 4.2: found "by another stealther, by an archer on True Sight, or by
  walking into a guard").
- **How often:** for a player who plays a stealther, every pass within about 125-550 of an enemy bot, so many times an
  hour. For other players, never.
- **Evidence:** read. Not looked for in any PC log: no tag exists.
- **Class:** two rules for one question, the second inherited from the base server and never switched off for
  stand-ins for players. It belongs in the one contact table (structure.md, target B).
- **Fix:** skip autonomous gamebots and their pets in `UncoverStealthAction`, so they see stealthers only by
  `RvrStealthNotice` (extended to objectives with the same cap). Keep the timer for monsters and guards, and give it to
  bot stealthers (finding 2). **Size S. Risk L.**

### 4. Line of sight is "a walkable straight line", not sight.
- **Player sees:** a bot group notices you behind the crest of a hill at 1,500-3,000 where you could not see them. A bot
  across a gully, stream or rock field does not notice you in plain view. A field bot "sees" through a closed keep door.
- **Chain link:** perceive.
- **Where:** `HasLineOfSight` is Detour's `raycast` along the walkable mesh surface, in 2D
  (`source/server/Pathing/Detour/Source/DetourInterface.cpp:331-355`). Height is ignored, so a hill does not block. A
  gap with no walkable mesh does block. The field scan uses `DefaultFilters`, which include the blocking-door polygons
  (`world/Pathfinding/PathfindingMgrBase.cs:91-94`; `FrontierThreat.cs:204-207`). Only siege fighters exclude doors
  and raise the ray 48 units (`:198-200`). The ray is also per zone, so it cannot cross a zone line.
- **How often:** every scan whose sight line crosses terrain. In Emain and Pennine that is most fights started at
  range [stated guess].
- **Evidence:** read. Whether water and slopes are walkable on the shipped meshes could not be checked (meshes are not
  in git).
- **Class:** perception built on the movement mesh. It belongs in the perception layer: a sight test of its own (a
  height-aware ray over the mesh, or terrain height samples) used by every eye.
- **Fix:** a sight test that walks the ray and compares the sight line with the mesh or terrain height at steps, with
  doors blocking. **Size M. Risk M:** CPU (`RVR_SCAN_BUDGET` exists to measure it).

### 5. Bot archers have no stealth shot: they close to 1,000, unhide and fire a plain arrow.
- **Player sees:** a Scout's first arrow comes from about 1,000 with no big first hit, from an archer that was visible
  for its draw. Period archers opened with Critical Shot from 1,500-2,300.
- **Chain link:** fight (the opener).
- **Where:** `TryApproachAmbush` follows an archer's target to 1,000 before it may shoot (`bots/BotBrain.cs:3671-3674`).
  The brain then unhides before any attack (`:3455-3456`). Nothing in `bots/` sets `eRangedAttackType.Critical`, so
  Critical Shot exists only for players (`skillhandler/CriticalShotAbilityHandler.cs:55`). Rapid Fire is never set
  either. Bots use only six actives (`bots/autonomous/AutonomousBotRealmAbilities.cs:48`): no Longshot, Volley or True
  Sight.
- **How often:** every engagement by a bot archer. Archers are 5-9% of each realm's bots
  (`bots/autonomous/data/era-builds-165.json` shares).
- **Evidence:** read. No log tag (`RVR_STEALTH_OPEN` is melee-only).
- **Rubric:** reference.md 4.2, 4.3; checklist "An archer's first hit comes from 1,500-2,300".
- **Class:** class AI missing a period opener. It belongs in the class policy, beside `RvrStealthOpener`.
- **Fix:** an archer opener: from stealth at bow range, draw Critical Shot, then the normal rotation (Rapid Fire when
  the target closes). Use the player's stealth-keep chance on nocking (`ECS-Components/AttackComponent.cs:641-652`).
  **Size M. Risk M:** archers get deadlier against the player.

### 6. A stealther that fails its opener never runs or re-hides.
- **Player sees:** you survive the Nightshade's opener and turn on it. It fights to the death, or bails only at 30%
  health. Below 33% health it runs at a crawl, so you catch it every time.
- **Chain link:** what next.
- **Where:** the solo bail rule (`bots/BotGroupRetreat.cs:151-154`, `bots/BotBrain.Retreat.cs:132-161`) knows nothing
  about stealth, the opener, or who has the edge. Vanish is never used (`BotBrain.Retreat.cs:247`). The NPC
  low-health slowdown applies to bots (`MaxSpeedCalculator.cs:139-143`); players have none.
- **How often:** every opener that does not kill. With a level 50 target that is most of them [stated guess].
- **Evidence:** read.
- **Rubric:** reference.md 4.4 ("a stealther who loses the opener usually runs"); checklist "A stealther who fails the
  opener runs. Fail: always fights to the death."
- **Class:** the flee model has no "my edge is gone" input (structure.md target F, one flee model).
- **Fix:** a stealther runs when its opener did not take the target below half health, or when a second enemy joins.
  It re-hides once out of combat, and uses Vanish where it has it. Exempt bots from the NPC low-health slowdown.
  **Size M. Risk L.**

### 7. A stealth duo is one hidden bot and one visible one.
- **Player sees:** a lone visible Shadowblade standing still at the milegate. Its partner is hidden beside it.
- **Chain link:** move.
- **Where:** only a party's leader gets a roam destination (`RvrTactics.cs:126`), and only the roam turn hides a bot
  (`:391-392`). The duo's second, often a second stealther (`RvrRosterTemplates.cs:456-469`), follows visible.
- **How often:** every stealth duo, all the time it camps. Duos are part of the 15% small share; evidence.md F10 says
  the real split differs.
- **Evidence:** read.
- **Class:** group state not shared with followers (structure.md 1.4: "followers read the leader's TempProperty; they do
  not read the plan").
- **Fix:** followers copy the leader's stealth when they can stealth (the `CompanionStealth.Maintain` pattern). **Size S.
  Risk L.**

### 8. Bot warbands hunt hidden stealthers, because the planner sees through stealth.
- **Player sees:** an enemy eight or warband walks straight to the spot where your realm's stealthers hide, with nobody
  having seen them.
- **Chain link:** decide (strategy).
- **Where:** hunt targets are every live enemy RvR gamebot in the frontier, stealthed or not
  (`AutonomousWorldBotController.cs:2565-2586`, `rvr-enemy-<id>` at its exact position). The roam prey sample does
  filter stealth (`RvrRoamCircuit.cs:494`), and so does the local dungeon scan (`AutonomousWorldBotController.cs:2388`).
- **How often:** a hunt picks up to 24 enemy RvR bots. Stealthers are about 15% of RvR bots and hide most of the time,
  so roughly one hunt target in eight is a hidden stealther [computed estimate].
- **Evidence:** read.
- **Class:** omniscient planning (structure.md finding 1). Here it also defeats stealth.
- **Fix:** filter `IsStealthed` now (**Size S, Risk L**). Then the contact table (structure.md B).

### 9. Openers are rare: a player at a milegate meets far fewer stealthers than in 2003.
- **Player sees:** an hour at AMG with no stealther striking you.
- **Where and why:**
  - 56 `RVR_STEALTH_OPEN` in 4 h 42 min on `19e7037` **[played, fired only; PLAYED-CHECK-1.2.md:346]**. That is
    about 12 an hour across all three frontiers and all victims, mostly bots. The tag's cap is 60 a minute
    (`RvrRoamCircuit.cs:532`), so the cap does not hide any.
  - Spread over 24 chokepoints, that leaves a player at one gate well under one melee opener an hour [computed].
  - Archer openers are not logged at all.
  - Likely causes: the small share played differs from the plan (evidence.md F10); the camp look hunts levelling bots
    only (`RvrTactics.cs:270-271`); `RvrSoloGate` turns down anything near company.
- **Rubric:** checklist "Stealthers in the gap or on the wall... Fail: none in an hour"; reference.md 2.1, 2.4
  (stealthers common at milegates, in packs).
- **Class:** an unmeasured population. It belongs in the "RvR hour" instrument (evidence.md 5.1).
- **Fix:** measure first: openers by victim kind, per chokepoint, per hour, with archers included. Then tune the share
  and the camp spots. **Size S** (logging). **Risk none.**

### 10. The stealth realm abilities are one-sided and use the pre-1.62 numbers.
- **Player sees:** with an assassin and See Hidden, you spot every bot archer at 900-1,900, because bots never
  Camouflage. Bots never use See Hidden, True Sight or Vanish on you.
- **Where:**
  - See Hidden uses the pre-1.62 formula, 2,700 - 36 x spec, capped at 1,900, with no 1.62 cut
    (`gameobjects/GamePlayer.cs:11214-11215` for bots, `:11272-11278` for players). The rubric: 1.62 cut it to a
    quarter to three quarters against archers, and Camouflage cancels it (reference.md 4.1).
  - Bots never activate Camouflage, True Sight or Vanish (`AutonomousBotRealmAbilities.cs:48,166`). The bot rule caps
    any bot at 350 whatever it has (`RvrStealthNotice.cs:35,69`).
- **How often:** only for a player with these abilities. For one who has them, every stealth encounter.
- **Evidence:** read.
- **Class:** era rules (mechanics.md's domain) plus class AI.
- **Fix:** apply the 1.62 See Hidden cut on 1.65 worlds; bot archers camouflage when camping. True Sight and Vanish for
  bots later. **Size M. Risk L.**

---

## Other observations (smaller, all [read])

- **The ambush approach has no scope check.** `BotRvrAmbush.CanApproach` (`bots/BotRvrAmbush.cs:22-28`) does not
  check scope. So keep defenders, battleground stormers, Darkness Falls bots and **companions** that can stealth all
  hide and follow an enemy within 2,000. A keep defender assassin leaves its wall post this way. For a companion,
  `CompanionStealth.Maintain` (`BotBrain.cs:1419`) unhides it each think while its player is visible, and the
  approach hides it again, so it may flicker (check on the PC).
- **Victim choice.** The odds gate allows greys and has no class preference (`RvrSoloEngage.cs:55-69`; the target
  comes from `SelectDistributedRvrTarget`). Period archers went for casters (reference.md 4.3). Greys were ganked
  too, so this is a texture point, not a defect.
- **After the opener.** The assassin steps behind or to the side only against monsters fighting someone else
  (`BotBrain.cs:3447-3450`, `TryFlank`). Against a player it stands where it opened. Positionals belong to
  mechanics.md.
- **A stealthed bot is "never called".** A noticed stealther is fought by the one bot that saw it, and mates are not
  told (`RvrPerception.cs:254-257`). A group had no way to see a stealther that one member saw either, so this is
  fair. But nobody searches the spot when a stealther escapes notice; the memory forgets it after 15 s
  (`AutonomousRvrPerception.cs:161`).
- **Emotes.** Stealthed bots do not emote (`AutonomousBotBodyLanguage.cs:160`). No stealther /bow before or after a
  duel exists (war.md's domain).
- **When the player stealths, attackers lose their target.** `StealthSpecHandler.cs:132-139` clears it, a rule marked
  "since 1.88 (?)", which is after the era. Bots then need `RvrStealthNotice` to find the player again.

## Every place perception differs by path

| Path | How it learns an enemy exists | Stealth | Sight test |
|---|---|---|---|
| Field gamebot, in scope (roamer, eight, soloer) | Frontier scan, 1.0-1.25 s; the perception rule for players and bots (2,600-3,500); two lookers per group | `RvrStealthNotice`, 350 at most, chance, LOS | Mesh ray at foot height, doors see-through (finding 4) |
| Soloer or stealther at its camp | As above, plus the 3 s prey look: enemy **levelling bots** within the spot radius + 1,500, no ray, a tier roll (`RvrTactics.cs:258-271,344`) | Prey look skips stealthed | None for the prey look |
| Eight at a check spot | The prey look also counts enemy RvR bots and players near the prey, no ray (`RvrTactics.cs:272-283`) | Skips stealthed | None |
| Strategy (`ChooseRvrDestination`, roam circuits) | Every enemy RvR **gamebot** in the frontier, each plan (`:2565-2586`); a 3-minute sample of all three frontiers (`RvrRoamCircuit.cs:474-500`) | **Sees stealthed bots** (finding 8); the player is never in it | None |
| Group fight board | Every unstealthed enemy within 3,500, no ray (structure.md finding 1) | Skips stealthed | None |
| Out of scope (raids, battleground assaults, player-led groups) | Perception for player kinds; enemy **bots instantly at 5,000** (`RvrFieldRules.None`, `RvrPerception.cs:32-34`, `AutonomousRvrPerception.cs:217-219`) | Blind to bots; the base uncover still hits the player | Mesh ray |
| Keep defender or siege fighter at its objective | Instant at up to 5,000 (`RvrPerception.cs:139`) | Blind to stealthed bots; **the base uncover still hits the player** (finding 3) | Siege fighters: ray +48, doors block; defenders: as the field |
| Companion bots | Around their player only, unstealthed (`CompanionPvpEngagement.cs:171,181`) | Never | `BotSiegeRuntime.Visible` |
| Keep guards | Aggro range, client LOS for players, mesh LOS for bots (`KeepGuardBrain.cs:34-60`) | Uncover players (base); stealthed bots never, not even `GuardStealther` | Client / mesh |
| Monsters | `StandardMobBrain` aggro skips stealthed players and bots (`:103,137`) | Uncover players only (finding 2) | Client / mesh |
| Bot pets | As monsters for uncovering (finding 3) | Uncover players | Client |
| The player | Client: every unstealthed object within 6,000; stealthed bots by the player rule at once, no LOS (`GamePlayer.cs:11199-11218`) | Fair against bots; See Hidden pre-1.62 (finding 10) | Client |

## What I checked and found sound

- **`RvrStealthNotice.HumanRange`** mirrors the server's own `CanDetect` (normal, Detect Hidden against Detect
  Hidden, See Hidden against assassins, Camouflage, Vanish, the 1,900 cap) and never lets a bot beat a human. **[unit]**
  `UT_RvrPerceptionBots.A_stealther_is_noticed_only_close_up_and_never_beyond_what_a_player_could_see` (`:366`) and the
  scan-clock test (`:408`). The tests check the formula, not a fight.
- **The player sees bot stealthers by the player rule.** `CanDetectStealthedBot` (`GamePlayer.cs:11199-11218`)
  works at once, through walls, as the 2003 client did. A hiding bot is removed from players who cannot detect it
  (`GameBot.cs:2179-2183`). The owner saw this fixed on 2026-10-07 (comment at `GamePlayer.cs:11338-11341`).
- **Aggro never goes to a stealther:** monsters (`StandardMobBrain.cs:103,137`), guards (`KeepGuardBrain.cs:40,59`), the
  fight board (`RvrFightBoards.cs:211`), the chat alarms (`FrontierThreat.cs:192`), the roam prey
  (`RvrRoamCircuit.cs:494`, `RvrTactics.cs:267,275`) and companions all skip stealthed targets.
- **Uncover on damage** applies to every living, player or bot, except under Vanish (`GameLiving.cs:1569-1573`).
- **The melee opener:** once per target; the style rules re-check stealth and position at the swing; veterans try to
  get behind 90% of the time, casual bots 40%, for up to 4 s (`RvrStealthOpener.cs:16,41-46,66-78`). This fits 1.65's
  narrow rear cone. **[unit]** `UT_RvrSoloEngage:75,119`. Fired 56 times on `19e7037` **[played, fired only]**.
- **The odds gate** never opens on a group, counts healers twice, and makes tiered mistakes (`RvrSoloEngage.cs:47-84`).
- **Stealth packs:** camps are weighted by realm-mates there, up to a point (**[unit]** `UT_RvrRoamCircuits:232`).
- **Companion stealth** follows the player's rules, the "too close" circle included (`CompanionStealth.cs:70-102`;
  **[unit]** `UT_CompanionStealth`).
- **Poisons** are applied from real vials (`BotPoisonSupply.cs`). Relic carriers cannot hide (`GameBot.cs:2166`).
- **The war map** shows no fights in the Old Frontiers (`serverrules/AbstractServerRules.cs:1945`, region 163 only).
  That is right for 1.65, so the player is not short of a period tool the bots have.

## What I could not check, and why

- **The navmeshes are not in git.** I could not check whether water, slopes and rock are walkable (finding 4), whether
  a ray crosses zone seams near the milegates, or whether each chokepoint's stand point is inside a guard's uncover
  radius.
- **Guard levels and `GuardStealther` spawns** in the Old Frontiers keeps and milegates: these are world data, not
  read.
- **Whether bot assassins buy See Hidden** in the "remaining abilities, cheapest first" fill
  (`AutonomousBotRealmAbilities.cs`, `Plan`). Under the bot rule it would make no difference (capped at 350).
- **How many stealthers are online per frontier.** It depends on the owner's roster. evidence.md F10 says the played
  split is not the planned 55/30/15.
- **Bumping** (rubric "uncover on bumping"): the 1.65 client has no player collision; nothing to check server-side.
- No test or sim was run.

## What only the owner's PC can settle

| # | Where | Action | Look at |
|---|---|---|---|
| 1 | Emain AMG (or HMG, Odin's Gate road) | Watch from your side, out of range, for 20 minutes | Do enemy stealthers walk in visible and vanish at the gate (finding 1)? Count `RVR_ROAM_CHECK type=chokepoint` and `RVR_STEALTH_OPEN` |
| 2 | Same | Walk through the gate 5 times, level 50 | Opened on? From where, how fast it closed, did it run when it failed (findings 2, 6)? `RVR_STEALTH_OPEN target=<you>`, `BOT_SOLO_BAIL` |
| 3 | Any road, as an archer's target | Stand in the open near a camp | First arrow distance and size (finding 5); no tag exists |
| 4 | Any frontier, as a stealther (Infiltrator, Scout...) | Pass an enemy bot group at 150, 300 and 500 | "uncovers you!" messages and at what distance (finding 3); `RVR_NOTICE stealthLimit=` lines (designed path) |
| 5 | An enemy keep under bot defence | Stealth up to the wall | Uncovered by defenders (finding 3) or only by guards? |
| 6 | A milegate or keep of your realm with an enemy bot stealther near | Watch the guards | Never uncovered (finding 2)? |
| 7 | A hilly field (Emain hills, Pennine) | Stand behind a crest from a bot group at 1,500-2,500 | Do they notice you through the hill (finding 4)? `RVR_NOTICE distance=` |
| 8 | A frontier with your companion assassin | Target an enemy player | Does the companion flicker in and out of stealth? |

## Measuring tools worth building

- **`RVR_STEALTH_UNCOVER`:** the base uncover timer firing on the player, with the bot, the distance, the cone and
  whether it was at an objective (finding 3).
- **`RVR_STEALTH_TRAVEL`:** a stealther leaving or reaching its camp, visible or hidden, and the nearest enemy player's
  distance (findings 1, 7).
- **An archer opener line**, the twin of `RVR_STEALTH_OPEN`, with the distance and whether the shot was Critical
  (finding 5).
- **Per-hour stealth rows in the "RvR hour" report** (evidence.md 5.1): openers by victim kind and chokepoint,
  failed-opener outcomes (ran, died, killed), uncovers of the player, and stealthers seen visible by the player within
  3,000.

## Hand-back summary

- Bot stealthers **walk visible to their camp and vanish there**, then unhide to leave (`RvrTactics.cs:385-405`). Every
  camp gives itself away.
- **Bot stealth costs nothing.** Bots move at full speed (the slowdown in `MaxSpeedCalculator.cs:67-77` is player-only),
  have no "too close" rule (`GameBot.Stealth`), and monsters and guards never uncover them (`GameBot.cs:2190-2196`,
  `KeepGuardBrain.cs:59`).
- **The player as a stealther meets an undesigned second rule.** The base server's mob uncover timer
  (`GamePlayer.cs:11094-11196`) treats every enemy gamebot and its pets as a monster: "X uncovers you!", unlogged,
  at keeps too. This corrects structure.md's "stealth-blind at keeps", which is true only for bot stealthers.
- **Bot line of sight is a walkable 2D mesh ray** (`DetourInterface.cpp:331-355`). Hills do not hide anyone, unwalkable
  gaps do, and doors are see-through in the field.
- **Bot archers have no stealth shot.** They close to 1,000, unhide and fire a plain arrow. There is no Critical Shot or
  Rapid Fire in `bots/`.
- **Stealthers never run** after a failed opener and never re-hide. Below 33% health they crawl (the NPC slowdown).
- **A stealth duo is half visible**: the follower never hides.
- **Strategy hunts hidden stealthers**: planning candidates include stealthed bots (`AutonomousWorldBotController.cs:2565`).
- **Openers are rare**: 56 in 4 h 42 min on `19e7037` across all frontiers [played, fired], so a player at one gate
  meets well under one an hour. Archers are not logged.
- **The stealth realm abilities are one-sided and pre-1.62** (See Hidden at full range; bots never Camouflage, Vanish
  or use True Sight).
- **Sound:** `RvrStealthNotice` mirrors the server's `CanDetect`; the player sees bot stealthers by the player rule; no
  aggro on stealthers anywhere; uncover on damage; the melee opener; the odds gate; companion stealth.
- **Fix locations:** one stealth-rules module shared by `GamePlayer` and `GameBot` (speed, too close, uncover by
  monsters and guards, skip bots in `UncoverStealthAction`); stealth owned by the movement layer for travel, duos
  included; an archer opener beside `RvrStealthOpener`; "my edge is gone" in the flee model; a height-aware sight
  test in the perception layer; an `IsStealthed` filter in the planning view now, then the contact table
  (structure.md B).
- **Evidence:** code read on `de53d0d3`. The opener count is the only played fact. Nothing here was run.
