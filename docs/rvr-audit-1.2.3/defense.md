# RvR audit 7: keep, tower and relic defense (agent: defense, issue #499)

Report only; no game behaviour changes. Judged against the period rubric `reference.md` (issue #493, PR not yet
merged; sections cited as "rubric 5.5" and so on). Structure findings are cited from `structure.md` (#494) and test
evidence from `evidence.md` (#495), not repeated. Attack (rams, target choice, muster, lord) is audit #498.

**Evidence levels.** *played*: seen in a played or logged game (named doc and build). *log*: an owner's-PC or test-copy
log summarised in a repo doc, not watched in game. *unit*: a unit test or sim. *read*: code reading only, on
`origin/main` at `de53d0d3` (2026-11-24). Nothing here was run; no navmesh of a frontier zone was available (see "Could
not check").

**One-line verdict.** A bot defense is a number, not a fight that people choose: it is called by omniscient "scouts",
sized as a share of the attacking army, steered by a server-wide win/loss thermostat, and then fought by bots who
cannot hold a wall, drop their siege engines at the first enemy, never repair a door and never see a stealther. A
player's own keep attack gets a guaranteed, announced, four-hour defense. The fallback to the lord room and the real
friendly-door entry are sound.

---

## Top 10 findings, worst first (ranked by how often a player sees them)

### 1. Every bot siege is defended by a thermostat, not by the people who happened to be near.
- **Player sees:** whichever keep is hit, the defending realm turns up with about three defenders for every four
  attackers, every time, from wherever its bots were. Sieges are never undefended at 4 am and never swamped at prime
  time. Win or lose, the next siege anywhere on the server is nudged the other way.
- **Chain link:** perceive (alarm) and decide (who answers).
- **Where:**
  - Alarm: `CallDefendersAtMarch`, `AutonomousRvrEventLayer.cs:1033-1045`: the moment the attacking army is given
    its march order at its own border keep, the defending realm is told "Scouts report ..."; there is no scout.
  - Size: `DefenderCapFor` (`AutonomousRvrEventLayer.cs:702-708`) = `DefendersFor`
    (`RealmEventPolicy.cs:85-86`): the larger of the marched and current attacking army times the share, clamped to
    8..cap. The share is `rvr_siege_defense_share` 0.75 (`RealmEventPolicy.cs:49-52`) moved by
    `SiegeDefenseBalance` (`SiegeDefenseBalance.cs:109-127`).
  - Thermostat: one server-wide window of the last six bot sieges, any realm, any keep (`SiegeDefenseBalance.cs:26,
    48, 109-116`). Four or more captures in six adds 0.125 to every realm's defense share, up to +0.25, then trims every
    realm's attacker cap 128 → 112 → 96. Midgard losing Bledmeer gives Hibernia more defenders at Crauchon.
  - Who: `PulseDefense` (`AutonomousRvrEventLayer.Defense.cs:259-330`): every level-50 RvR gamebot group not in
    combat, any region, nearest first; a fixed 30% of groups never answer (`ResponseReserve`, `:140`, a hash of the
    lowest member id, so the same groups sit out every defense).
- **How often:** every bot siege; the logs show several per hour on a busy night (23 in one night,
  NIGHT-SIEGES-AND-MARFACH.md section 4).
- **Evidence:** read; log: "the defenders were online and scale with the attacking army" and 3 of 4 defences at
  exactly 1.0 attackers per defender (128/128, 87/88, 128/128) on `168f55d` (NIGHT-SIEGES-AND-MARFACH.md, section 4).
  The 35-65% band is a sim that assumes its lever (evidence.md F9).
- **Period:** "Most defences were won by a defending zerg arriving, not by the guards"; who arrives decides it;
  defended keeps 30-90 minutes, undefended ones 10-20 (rubric 5.5, 5.6). Alerts came from what players saw: guard
  deaths to the owning guild with a count, realm broadcast of keep attacks from 1.52 (rubric 5.1). Nobody knew an army
  had left its border keep unless someone watched it.
- **Class:** defense decided by formula; perception omniscient (structure.md finding 7 names the "told, not seen"
  half; this is the sizing half). Belongs in the objective model (structure.md piece C) fed by contacts (piece B).
- **Fix:** answer with the realm's bots who are in that frontier and free, called by what a bot or guard saw (army
  sighted near the keep, a guard death, a door hit), with a delay that grows with distance. Keep the balance as a
  per-realm, log-only measure, or at most a per-realm nudge to how many bots are in the frontier, never to how many
  answer one siege. **Size M. Risk M**: win rates will swing by hour and realm; that is the period, but count
  `RVR_EVENT_ENDED` per night first (evidence.md F9).

### 2. A player who hits one guard gets a guaranteed, announced, four-hour defense.
- **Player sees:** a level 50 solo crossing Emain near Dun Crauchon is aggroed by a guard and swings back. A horn plays
  and the screen says "Your attack on Dun Crauchon has raised the alarm! A small defense force has been rallied!",
  repeated three times in 15 seconds. About 24 level-50 Hibernians walk into the keep over the next ten minutes and stay
  there for four hours; 8 of the player's own realm and 8 of the third realm are sent too. An eight-man (Standard) draws
  48 defenders, a 9-40 raid 128, more than 40 draws 240.
- **Chain link:** perceive, decide, and the end of the defense.
- **Where:**
  - Trigger: any damage by a player, a player's companion or pet, or a player's siege engine to any keep guard or door
    (`GameKeepGuard.cs:225-228`, `GameKeepDoor.cs:140-145` → `ObserveKeepAttack`,
    `AutonomousRvrEventLayer.Defense.cs:57-74`; `PlayerInstigator` `:113-127`). It does not matter who started the fight.
  - Size: `ScaleOf` and `ResponseCap` (`:23-29`, `:133-139`): defenders 24/48/128/240, helpers 8/16/48/96 for each other
    realm. Group size, not what is there, sets it (`ForceSize`, `:32`).
  - The tells: horn sound 219 to the attacker (`:45-47, 226`), the centre-screen line with the force size every 5 s for
    15 s (`:221-225, 243-252`), "Our forces are attacking ..." to the player's realm (`:235`).
  - Length: `DefenseResponseMilliseconds` = 4 hours (`:15`). The ten-minute repel rule that ends a bot siege skips player
    defences (`AutonomousRvrEventLayer.cs:1730-1737`, `!entry.DefenseReaction`), so it ends only by capture or the
    timer (`:1740-1741`). A later attack by the same player on another keep of that realm moves the same force there
    (`CanRedirectDefense`, `Defense.cs:142-151`).
  - Third realm: joins at once (`BattleStarted = true` at creation, `:173`; the `ThirdRealmIntervenes` 50% roll and the
    5-minute delay of `RealmEventPolicy.CanRespond` are not consulted on this path, `:291`).
- **How often:** every time the player touches a keep: a roaming player near a front keep, several times an hour.
- **Evidence:** read; unit (`UT_PlayerKeepDefense`, evidence.md appendix). No PC count of these lines
  (`PLAYER_KEEP_DEFENSE_SIZED`) is in the docs.
- **Period:** an attack drew whoever heard of it and cared: the owning guild's guard-death spam, a realm message, a
  call in chat (rubric 5.1). A small take at a quiet hour often met nobody (rubric 5.6). There was no message to the
  attacker saying how big a defense was coming.
- **Class:** told, not seen; a fairness rule (owner Goal 11, 2026-10-07) written as a formula. Structure: the player is
  invisible to strategy except here (structure.md finding 5), so here the reaction is total.
- **Fix:** (a) drop the attacker's horn and size line, or move them behind a setting that defaults off (it is a
  diagnostic tell, not a period message); (b) raise the alarm only for a sustained attack (a door hit, or two guard
  deaths), not a guard's own pull; (c) use the bot-siege repel rule (ten minutes with no attacker present ends it);
  (d) size by who is free nearby, as in finding 1. **Size S** for (a)-(c), M for (d). **Risk L-M**: Goal 11 wanted a
  big answer to a big player raid; (d) keeps that for a raid in prime time and gives a solo a fair night.

### 3. Wall defenders do not hold the wall.
- **Player sees:** archers and casters of the defending realm leave the walls and come out to the attackers 2,000-5,000
  units away; melee defenders run out of the gate after the nearest attacker and are killed in the open. A defended
  keep looks like a field fight that started in a courtyard.
- **Chain link:** fight (position).
- **Where:**
  - The wall rule exempts the very bots it should bind: `BotBrain.cs:3412-3431` stops a ranged bot on a wall whose target
    is out of range or out of sight only when it is **not** a committed defender (`!AutonomousRvrDefense.IsCommittedDefender`
    at `:3420`). A committed defender falls through to the normal attack, which closes to spell or bow range.
  - Defenders look out to 5,000 (`TargetSearchRadius`, `AutonomousWorldBotController.cs:42`;
    `AutonomousRvrPerception.ScanRadius`, `AutonomousRvrPerception.cs:224-225`) because the field rules are off at the
    objective (`AutonomousWorldBotController.RvrPerception.cs:30-35`; `RvrTacticsScope.cs:85-86`). That also turns off
    the stance machine's chase leash (structure.md finding 4).
  - The posts themselves are sound: ranged defenders get a guard archer's floor above the gate, proven by a route
    (`AutonomousRvrRally.cs:129-201`).
- **How often:** every defended siege where an attacker stands in sight within 5,000 of a wall: all of them.
- **Evidence:** read. `UT_BotKeepWallSpots` tests the idle wall-sitting rule, not committed defenders (evidence.md
  row 41: never run in game).
- **Period:** archers and casters on the walls shooting down, healers behind them, melee inside the door (rubric 5.5).
  Defenders went out to hit the siege from behind as a group, not one by one.
- **Class:** rules that hold in one path (idle bots) but not in the path that matters (committed defenders); the
  "at objective" engine switch (structure.md finding 4).
- **Fix:** a wall-defender profile: ranged defenders on a wall post hold it (fire at what is in range and in sight,
  wait otherwise); melee defenders hold inside the gate within a leash of their post and sortie only as a called group
  against a ram crew. Smallest step: drop the `!IsCommittedDefender` clause at `BotBrain.cs:3420` and add a post leash
  for melee. **Size S** (the clause) to **M** (profile). **Risk M**: defenders on the walls survive longer; re-count
  captures per night.

### 4. Defenders' siege engines are dropped at the first enemy, and nobody pours oil.
- **Player sees:** at a bot-defended keep a defender buys a catapult or ballista, carries it in, and walks away from it
  when the attackers come in sight; it is never seen firing during the battle. No boiling oil ever falls on a ram.
- **Chain link:** fight (defensive siege).
- **Where:** `TryEngageFrontierThreat`, `AutonomousWorldBotController.FrontierThreat.cs:187` lets a defender's scan
  see everything (the 450-unit operator filter applies only to non-defenders), and `:245-251` releases the defender's
  siege job ("Combat wins over buying, deploying or operating a siege engine") with a 10-second retry. During a battle
  an attacker is always in sight, so the job is released every turn it is taken. Defender jobs exist
  (`AutonomousSiegeJobs.cs:24-29`: ballistas against enemy siege, catapults on the attackers). No bot code uses the base
  server's cauldron (`gameobjects/SiegeWeapon/gamesiegecauldron.cs`); grep finds no bot reference to it.
- **How often:** every bot-defended siege with operator classes present.
- **Evidence:** read. No `RVR_SIEGE` line from a defending operator is quoted in any doc.
- **Period:** boiling oil from the gatehouse on the ram crew and defenders' catapults and trebuchets on rams and the
  crowd were the defense (rubric 5.2). Note the rubric's own tension: 1.2 says no hookpoint-mounted defences in Old
  Frontiers, 5.2 says oil from the gatehouse; the oil item is [M]. Settle it before building oil.
- **Class:** a rule written for one role (field combat first) applied to a role it breaks (siege operator).
- **Fix:** give defending operators the same exemption attackers have: only an enemy within 450 or attacking the
  operator pulls it off its engine (`:187`). Oil only after the rubric question is settled. **Size S. Risk L.**

### 5. Nobody repairs a door; a door beaten nearly down stays nearly down for hours.
- **Player sees:** a siege is beaten off with the front gate at 10%. Twelve minutes later (the target cooldown) the next
  army walks through an open gate. Hours later the gate is still weak. No defender ever stands at a door repairing it.
- **Chain link:** what next, after a beaten attack.
- **Where:** `GameKeepDoor.cs:580-594`: 5% of maximum health every 30 minutes, only when the keep is out of combat and
  no enemy player or bot is within 3,000 (`:596-611`); a door under 15% is open (`:14, 470-473`). From 10% to full is
  18 ticks, nine hours, longer if anyone passes. No bot code repairs a keep door (grep for door repair in `bots/`:
  none); only a player's `/repair` with wood (`commands/playercommands/Repair.cs`). Doors are full only after a
  capture (`Reset`, `:631-641`). Target cooldown 12 minutes (`AutonomousRvrEventLayer.cs:35`).
- **How often:** every siege that is beaten off after a door was hit. The log shows doors down 4-8 minutes into
  battles; "keeps held by another realm fell every time" (NIGHT-SIEGES-AND-MARFACH.md section 4) is consistent with
  weak doors on retakes, not proven by it.
- **Evidence:** read; log for the door regeneration rule (NIGHT-SIEGES-AND-MARFACH.md: "a damaged door outside a capture
  regains 5% every 30 minutes").
- **Period:** players with woodworking repaired and upgraded doors, hauling the wood (rubric 5.3). The rubric already
  flags the self-repair as a stand-in (rubric 9.3).
- **Class:** state change with no actor (a door has health; no one owns its upkeep).
- **Fix:** after a siege ends as defended, the realm's defenders still present take a repair job: a woodworking-capable
  or any level-50 bot buys wood at the keep's siege merchant and repairs with the player's rules (no repair while hit,
  the January 2002 upgrade rule). **Size M. Risk L** (only makes defended keeps sturdier; may lower attacker wins).

### 6. Bot sieges' defenders arrive one by one; the "muster" is bookkeeping.
- **Player sees:** as attacker, defenders trickle into the keep in ones and twos; as defender, the realm says "The
  defense rally marches to relieve ..." minutes after the first defenders are already dead inside.
- **Chain link:** getting there.
- **Where:** a defender's rally post is its interior keep post (`AutonomousRvrRally.TryPost`, `AutonomousRvrRally.cs:33-39`
  → `TryDefenderPost`), so "mustering" means walking into the keep alone. A defense called after the battle began gets no
  rally order at all (`GetRallyOrder`, `AutonomousRvrEventLayer.cs:1306-1308`). The march order (`GiveDefenseMarch`,
  `:968-979`) changes a flag and a chat line.
- **How often:** every bot siege.
- **Evidence:** log: "`RVR_DEFENSE_MUSTER` and `RVR_DEFENSE_MARCH` read `mustered=0` every time ... The 6-10 minutes
  between the two lines are bookkeeping, not a delay"; five keeps fell in 9-13 minutes with defenders committed (53-86)
  who killed 0-2 attackers (NIGHT-SIEGES-AND-MARFACH.md section 4, build `168f55d`).
- **Period:** a realm's answer came in waves from the portal keep or as a group that buffed at a near keep and ran in
  (rubric 1.3); arriving in a clump is period.
- **Class:** the same word for two things (muster = gather outside; post = inside).
- **Fix:** a defense rally point outside the keep on the defenders' side (the nearest friendly keep or a point on the
  route in), gather by group, then enter together through the friendly door. **Size M. Risk M** (slower first
  defenders: more early door losses).

### 7. Stealth at a keep: guards and defenders see none, one guard type sees all.
- **Player sees:** an infiltrator walks along a wall lined with 30 bot defenders unseen; the same player near a keep
  stealther guard is seen at 1,000 units.
- **Where:** committed defenders never take a stealthed target (`StealthOk`, `FrontierThreat.cs:169-170` with field
  rules off at the objective). Keep guards never aggro a stealthed player unless the guard is a `GuardStealther`
  (`KeepGuardBrain.cs:40-41`), which then sees him at its full 1,000 aggro range (`GameKeepGuard.cs:813`); no guard ever
  sees a stealthed gamebot (`KeepGuardBrain.cs:59`).
- **How often:** whenever a stealther comes near a keep; rare for most players, every night for an infiltrator.
- **Evidence:** read.
- **Period:** guards saw stealthers only close up; detection 125 + 20-25 per level difference (rubric 4.1, 1.6).
- **Class:** perception blind in one path, omniscient in another; belongs to the stealth audit's single perception
  model (#502) and structure.md piece B.
- **Fix:** one stealth rule for guards and defenders: the rubric's range by level difference for every guard and
  defender, players and bots alike. **Size S** once the stealth audit's rule exists. **Risk L.**

### 8. Defenders' posts split the group: healers in the lord room, casters on the wall.
- **Player sees:** wall casters die without a heal; healers stand by the lord, away from anything to heal, until the
  inner door falls.
- **Where:** `AutonomousRvrRally.cs:136-150`: ranged (pure casters and the four bow or pet classes,
  `AutonomousRvrDefense.cs:27-29`) post at guard archers above the gate; support classes and Shamans post at the lord;
  melee at the lord or guard fighters near the entry. Posts are per bot, not per group.
  `BotKeepWallSpots` (the group-aware wall rule) is used only by idle bots with no siege (`BotKeepWallSpots.cs:170-171`).
- **How often:** every bot-defended keep.
- **Evidence:** read.
- **Period:** groups defended as groups: casters on the wall with their healer, a defense group outside (rubric 5.5).
- **Class:** per-bot role table where the period used per-group placement.
- **Fix:** post by group: each defending group takes one stretch of wall (or the inner door), its healer just behind
  its casters. **Size M. Risk L.**

### 9. A player's capture brings one guaranteed counterattack of at least 64; a bot's capture none.
- **Player sees:** the player's eight takes a keep. Ten minutes later "X rallies to retake ..." and 64 or more bots come,
  once, for 30 minutes. After bots take a keep, the loser never organises a retake; it comes back only if the siege
  roll picks it.
- **Where:** `StartRetake`, `AutonomousRvrEventLayer.cs:840-866`: only after a player's capture, cap
  `max(64, the failed defense's cap)`, a 10-minute muster (`:883`) and a 30-minute battle (`:884`). After a defended bot
  siege every force is released at once (`EndEvent`, `:1814-1818`): no chase of the broken army, no rest, no repair, no
  upgrade.
- **How often:** every player capture; every defended siege for the "what next".
- **Evidence:** read; the retake is unit-tested only.
- **Period:** a lost front keep was retaken by whoever was around and wanted it, often within the hour; a beaten army
  was chased to its portal keep (rubric 5.5, 5.6, 1.5).
- **Class:** the player path and the bot path ruled separately.
- **Fix:** one "after the siege" step for every siege: winners chase the beaten army for a short leash, rest, repair
  (finding 5); the loser may retake if its free forces are near, sized like any siege (finding 1). **Size M. Risk M.**

### 10. Relic defense is the keep thermostat with bigger numbers.
- **Player sees:** a relic raid is answered like any keep siege: scouts at the march, about 0.75 defenders per attacker
  up to 180, the relic keep's sentinels. A realm does stop relic raids (the logs show Dun Dagda armies wiped), but the
  reason is the formula and the 3x-health guards, not a realm that was awake or asleep.
- **Where:** the same `CallDefendersAtMarch` and `DefenderCapFor` (finding 1) with the relic cap 240
  (`RealmEventPolicy.cs:41-42, 88`); sentinels per held keep (`AutonomousRvrEventLayer.cs:124-159`,
  `RelicGuardManager.SentinelKeepsAt`); carrier interception as its own event (`CarrierEvents`, `:116`, `:523`, `:1777-1793`).
  The base server's New Frontiers relic walls (`keeps/Managers/RelicGateMgr.cs`) have no callers: correctly not in play.
- **How often:** a relic raid is rare (attack audit #498 for how rare); when one runs, every time.
- **Evidence:** log for outcomes (run notes in `AutonomousRvrEventLayer.cs:805-811` comments: runs 9, 13, 15 at Dun
  Dagda; tests only for the campaign, evidence.md rows 50-51); read for the rest.
- **Period:** relic keeps "require active player involvement"; raids were won when the victim realm was asleep or
  busy, and the sentinel rule tied relic strength to keeps held (rubric 1.7, 5.6). The sentinel rule matches.
- **Class:** defense by formula (as finding 1).
- **Fix:** comes with finding 1: relic defenders from who is in the frontier and free, called when the column is seen
  in the realm's own zones. **Size** in finding 1. **Risk M.**

---

## Code numbers beside the era's

| Item | Code | Where | Era (rubric) |
|---|---|---|---|
| Alarm, bot siege | at the attackers' march order, realm-wide | `AutonomousRvrEventLayer.cs:1033` | from what players saw; guild guard spam; realm broadcast from 1.52 (5.1) |
| Alarm, player attack | first damage to any guard or door | `Defense.cs:57-74` | same |
| Defenders, bot siege | 0.75 (0.5-1.0) x attackers, 8..96-128 (keep), ..180-240 (relic) | `RealmEventPolicy.cs:52, 85-89` | whoever came: none to a zerg (5.5, 5.6) |
| Defenders, player attack | 24 / 48 / 128 / 240 by group size | `Defense.cs:133-139` | whoever came |
| Helpers, player attack | 8 / 16 / 48 / 96 per other realm, at once | same | third realm often arrived late (5.6) |
| Third realm, bot siege | 50% of sieges, after 5 min, up to 64 | `RealmEventPolicy.cs:28-29, 89` | often ended the fight (5.6) |
| Defense length, bot siege | ends 10 min after attackers gone; else 4 h | `AutonomousRvrEventLayer.cs:33, 1735` | 30-90 min defended (5.6) |
| Defense length, player attack | 4 h, or capture | `Defense.cs:15` | same |
| Defense muster | 6 min (+4) bot; 10 min / 80% player | `AutonomousRvrEventLayer.cs:878-891` | waves a few minutes apart (1.3) |
| Responders | level 50 only; 30% of groups held back | `Defense.cs:261, 297-298` | any level that wanted RP |
| Guard aggro | 1,000 (portal keep 2,000), 99% | `GameKeepGuard.cs:813` | [M] similar |
| Guard chase | gives up 25 s out of combat; aggro kept to 6,000 | `StandardMobState.cs:89`; `StandardMobBrain.cs:23` | border keep guards killed chasers (1.6) |
| Guard respawn | 15 ± 10 minutes | `ServerProperties.cs:1890-1897` | not found |
| Door repair | 5% per 30 min, none with enemies within 3,000 | `GameKeepDoor.cs:580-611` | players with wood (5.3) |
| Door open below | 15% | `GameKeepDoor.cs:14` | not found |
| Defender raise | RvR healers raise realm-mates within 2,000 (fighting) / 5,000 | `AutonomousSiegeResurrection.cs:14-21`; `BotBrain.cs:6172-6174` | healers raised the fallen (3.9) |
| Corpse wait | attacker 240 s with an army healer near; defender 90 s with its own party healer, else 20 s | `GameBot.cs:1194-1203, 1235` | released to bind; 5-10 min to return (1.3) |
| Siege balance | window 6, step 0.125, band 0.35-0.65, shift -0.25..+0.5 | `SiegeDefenseBalance.cs:26-44` | none: no period equivalent |

## Defenses decided by a formula rather than fought

1. Who answers a bot siege and how many (finding 1).
2. When the defenders learn of it (the march order, finding 1).
3. How many answer a player (group size table, finding 2).
4. The next siege's defense share and attacker cap, from the last six results anywhere (finding 1).
5. How long a player defense lasts (four hours, finding 2).
6. Whether a counterattack happens (only after a player capture, finding 9).
7. The door's recovery (a timer, finding 5).
8. Relic raid defense (finding 10).
What is fought: the battle itself, guards, the lord, the doors, the walk in.

## What I checked and found sound

- **Fallback to the inner keep.** When every outer door is down or open (`ObjectiveExposed`,
  `AutonomousRvrDefense.cs:24-25`), new posts are all at the lord (`AutonomousRvrRally.cs:143-146`), and a change in any
  door's state re-plans every defender's post (`KeepTravelGeometry`, `AutonomousWorldBotController.KeepTravel.cs:87-101`;
  used at `DefensivePost.cs:23-29`). Read.
- **Getting inside.** Defenders walk to a friendly door and use the native door operation, never a wall shortcut
  (`AutonomousWorldBotController.Rally.cs:563-597`); posts are floored on the mesh within 64 units of a guard's height and
  proven by a route (`AutonomousRvrRally.cs:162-200`). No invented wall-top points, so no clipping by construction.
  Read.
- **Home defense first.** A committed attacker breaks off to defend its own keep when that defense is under half its cap
  and the attack keeps half its army (`AutonomousRvrEventLayer.cs:916-962`; `RealmEventPolicy.cs:71-72`). Read; it came
  from played nights (comment `:924-928`).
- **Defended sieges end.** The ten-minute repel rule (`:1730-1737`) fixed the four-hour hold seen on 2026-10-07. Read.
- **Empty musters stand down** (`:1702-1716`). Unit only (evidence.md row 45).
- **Relic sentinels per keep held** match the 1.65 rule (rubric 1.7, 9.3). Read.
- **No New Frontiers relic walls in play** (`RelicGateMgr` has no callers). Read.
- **Defender raises.** An RvR healer raises any realm-mate's corpse near it, defenders included (`BotBrain.cs:6172-6174`),
  but a defender corpse waits only for its own party (`GameBot.cs:1191-1203`). Mostly sound; the shorter wait is small.
- **Idle wall sitting** (`BotKeepWallSpots`) is a careful design: only at a held keep, no siege, archer floors above the
  gate, within 400 of the leader. Unit only, never run in game (evidence.md row 41).

## The player's seat

- **As defender.** A bot siege on the player's realm reaches him as a bot's realm-chat line ("Scouts report ...",
  rate-limited to one notice per realm per minute, kept five minutes; `RealmEventNotices.cs:23-31, 107-125`) and later
  centre-screen lines. The bots do not know he is there: none follow him in, none hold a wall with him, a keep his guild
  claimed gets no priority (claim rank is looked up only for player-attack defences and only for bot guilds,
  `Defense.cs:285-286`). The base server's guard-death message to the owning guild (`GameKeepGuard.cs:265-276`) is the one
  period alert, and it reaches a player guild only. Read.
- **As attacker.** A solo or eight that takes a keep fast (under ten minutes) beats the muster; one that is slower meets
  a defense three to six times its size, announced to it in advance (finding 2), then a retake of at least 64 (finding 9).
  There are no towers in Old Frontiers (rubric 1.2), so "a small group taking a tower" has no case here; the release
  world has no tower rows (rubric 9.2).
- **Can a player reach defenders as they reach him?** Wall defenders shoot from guard-archer floors with a server
  line-of-sight ray; the player can shoot back at the same spot. Defenders inside wait for line of sight, so a player
  cannot pull them one by one from outside except by being seen. Read; PC to confirm.

## What I could not check, and why

- **Wall spots per keep.** No frontier navmesh (only the test mesh `zone078.nav`) and no world database in the checkout,
  so I could not see whether every Old Frontiers keep has guard archers above its gate, or check line of sight from those
  floors. If a keep has none, its ranged defenders get no wall post at all (`AutonomousRvrRally.cs:146-147`).
- **Guards helping each other against bots.** The base Bring-a-Friend fires only for players, their pets and companions
  (`StandardMobBrain.cs:822-838`), and the lord's "bellows for assistance" only on a PvE-type server (`Lord.cs` brain,
  `ai/brain/Guards/Lord.cs:112-143`). Which server type the release runs, and whether keep guards carry a faction, is in
  the world data, not the code.
- **Committed defenders walking off the wall (finding 3)** is from the code path; the attack code after the wall test is
  long, and only a watched siege settles it.
- **Pets and area spells on walls** were not traced.
- **Era numbers** for door hit points, guard counts and respawns were not found (rubric 10).

## What only the owner's PC can settle

1. **Finding 3:** stand inside a held keep during a bot siege; watch a wall caster (`/lswhy` on it) when attackers are
   2,000-4,000 away. Does it stay on its post ("Defending keep") or walk out?
2. **Finding 4:** grep `RVR_SIEGE` for an operator whose realm is the keep's; any `action=hit` by a defender's engine?
3. **Finding 2:** with one character, hit one guard at a front keep, then run away. Note `PLAYER_KEEP_DEFENSE_SIZED`,
   the `RVR_DEFENSE_MUSTER` cap, the time of the first defender inside, and whether `RVR_EVENT_ENDED` comes before the
   four hours (since #784 expected about ten minutes after the last attacker, player or attacker-realm bot, leaves).
4. **Finding 5:** after a defended siege, note the gate's health (`/examine` or target) and again an hour later.
5. **Finding 1:** count `SIEGE_DEFENSE_BALANCE` lines per night and compare `RVR_EVENT_ENDED defenders=` to `attackers=`.
6. **Finding 7:** stealth to a defended wall with an infiltrator; at what range do defenders or guards attack?

## Measuring tools worth building

- **A per-siege timeline** (evidence.md 5.1, structure.md section 6) with three defense rows it lacks today: first
  defender inside, defenders on wall posts per minute, and defender engine hits.
- **`RVR_DEFENSE_POST` with the post kind** (wall, entry, lord) and a `RVR_DEFENSE_LEFT_POST` when a committed defender
  ends more than 300 from its post while fighting.
- **Door health at siege end** in `RVR_EVENT_ENDED` (each door's percent), so repairs and retakes can be counted.
- **A night report of who answered** each siege: free level-50 RvR bots in that frontier at the alarm versus those
  that came, to replace the share with a measured number.

## Hand-back summary

PR: docs-only, `docs/rvr-audit-1.2.3/defense.md`. Evidence: read unless marked; logs from
NIGHT-SIEGES-AND-MARFACH.md (`168f55d`). Nothing was run.

Top defects by class, by how often a player sees them:
1. Defense decided by formula: every bot siege answered by about 0.75 defenders per attacker, called by omniscient
   scouts at the march, steered by a server-wide six-siege thermostat (`RealmEventPolicy.cs:85`,
   `SiegeDefenseBalance.cs:109`, `AutonomousRvrEventLayer.cs:1033`).
2. Told, not seen, for the player: one guard hit → a 24-240 defense, a horn and a size line to the attacker, held four
   hours with no repel end (`AutonomousRvrEventLayer.Defense.cs:15, 57-74, 221-226`; `AutonomousRvrEventLayer.cs:1730`).
3. Wall rule exempts committed defenders (`BotBrain.cs:3420`): with a 5,000 scan and no leash they leave the walls.
4. Defender siege engines released at the first enemy (`FrontierThreat.cs:187, 245-251`); no oil.
5. No door repair by anyone but a player; 5% per 30 minutes (`GameKeepDoor.cs:580-611`).
6. Defense muster is bookkeeping; defenders trickle in (`AutonomousRvrRally.cs:33-39`).
7. Stealth: defenders and guards blind, stealther guards omniscient (`KeepGuardBrain.cs:40, 59`; `FrontierThreat.cs:169`).
8. Per-bot posts split healers from casters (`AutonomousRvrRally.cs:136-150`).
9. Retake only after a player capture; nothing after a beaten attack (`AutonomousRvrEventLayer.cs:840, 1816`).
10. Relic defense is the same formula, bigger.

Fix locations: the event layer's defense sizing and alarm (`AutonomousRvrEventLayer.cs`, `.Defense.cs`,
`RealmEventPolicy`, `SiegeDefenseBalance`) into one objective model fed by contacts (structure.md B, C); a
wall-defender profile in `BotBrain.cs:3412` and `FrontierThreat.cs:187-251`; a repair job beside
`AutonomousSiegeJobs`; group posts in `AutonomousRvrRally.TryDefenderPost`. S fixes first: findings 2 (a-c), 3, 4.
Sound: fallback to the lord, friendly-door entry, home defense first, the repel rule, relic sentinels.
