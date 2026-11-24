# RvR audit 9: the PvP rules for bots and the player, and each class's RvR job

Agent: mechanics (issue #501). Report only; no game behaviour change. Read on `origin/main` at `de53d0d3`. Judged
against the rubric [reference.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/pull/509) (PR #509, sections 2.2,
2.5, 3.1-3.10, 8), with the structure ([structure.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/pull/507),
PR #507) and evidence ([evidence.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/pull/508), PR #508) reports
cited, not repeated.

**Evidence.** Every claim below is **read** (code read on `de53d0d3`) unless marked otherwise. No test was run, no sim
was run, nothing here was played. Where a period number is given it comes from the rubric and keeps its tag there
([S], [R], [M]); my own period memory is marked [M]. All paths are under `source/server/GameServer/` unless they start
with `docs/` or `source/`.

**The three kinds of character.** "Bot" below means an autonomous gamebot (`GameBot`, `IsAutonomousWorldBot`). A
companion (`IsTemporaryGroupHelper`, `/bot`, hired) is named as such. "The player" is the one real `GamePlayer`.

## The one cause behind most of this

`GameBot` is a `GameNPC` (`bots/GameBot.cs:32`). The base server writes its PvP rules as "if the target (or the
attacker) is a `GamePlayer`, the player rule; otherwise the monster rule". So wherever a rule asks `is GamePlayer`, a
bot gets the monster rule: monster mez immunity, monster interrupts, monster run speed, monster crits, no rez sickness,
no line-of-sight check. Some sites were already widened to `GamePlayer or GameBot` / `IGamePlayer` (armor,
`AttackComponent.cs:1425`; armor bonus, `:2240`; crit when the attacker is a bot, `:2190`; stealth detection of a bot,
`gameobjects/GamePlayer.cs:11199-11218`; bolt armor location, `spells/BoltSpellHandler.cs:68-71`). The rest were not.
The owner's decision 9 in `docs/RVR-FIELD-1.2.md:306-307` ("keep the server's current values") assumed one set of
values; there are two, and the bots get the monster set.

**Class of defect:** a rule keyed on the C# type instead of on "is this a player character". **Where it belongs:** one
predicate in the rules layer, for example `PvpRules.IsPlayerCharacter(GameLiving)` = `GamePlayer` or any persistent or
companion `GameBot`, used at every site in findings 2, 3, 4, 5, 7 and 8. One change, many sites; a test per site that a
bot and a player get the same answer. Structurally this is the "fix lands in one path only" class of structure.md
section 3.

## Top 10 findings, worst first

### 1. Bots refill 10% of health, power and endurance every second, on the move, ten seconds after a fight.

- **Player sees:** in Emain his group beats a bot eight, chases the two survivors over a hill, and twenty seconds later
  meets them again at full health and power, with no sitting. His own character sits for a minute or more to get power
  back. A bot group that loses half a fight is back to full before the player has finished rezzing.
- **Chain link:** win or lose → what next. The rubric (3.7, 3.10): groups rest after a fight, "average groups rest where
  they fought", and a lost fight costs minutes.
- **Where:** `bots/BotRestRecovery.cs:26-27` (each tick gives at least 10% of the pool), `:68-80` (an autonomous gamebot
  recovers whenever it has not fought for 2 s and `BlocksRest` is false; no stand-still check), `:100-104` (`InCombat`,
  10 s, `gameobjects/GameLiving.cs:31`, `:225-226`, blocks it), `bots/GameBot.cs:441-443` (`IsEnhancedResting` needs no
  stillness on that branch), `:520-575` (1 s ticks for health, power and endurance). The comment at `BotRestRecovery.cs:60-67`
  says it is "a logical recovery window only: it never stops movement". The player regenerates one native tick per 6 s
  standing or 3 s sitting (`gameutils/ClassicRestRegeneration.cs:7-10`).
- **How often:** every fight a bot survives, all day. From empty to full in about 10 s once `InCombat` lapses, so about
  20 s after the last blow (computed). A level 50 player caster takes minutes to refill power (stated guess, [M]).
- **Evidence:** read; `UT_BotResting` tests the rule computes (unit), nothing measures it against a player.
- **Class:** a PvE convenience (bots that never sit) applied in RvR. Belongs in the rest policy, scoped by region.
- **Fix:** in a battle region (frontier, battleground), autonomous gamebots use the player's regeneration and sit to rest
  as `AutonomousRestPolicy` already lets them (the sitting is visual today, `GameBot.cs:353-372`). Keep the fast floor in
  PvE if the owner wants it. **Size S. Risk M:** bot groups will rest and be caught resting, which is period-right, and
  roam pacing changes; measure fights per hour before and after (structure.md section 6).

### 2. A hurt bot cannot be mezzed; a bot's mez immunity is a monster's; roots and snares leave a bot no immunity at all.

- **Player sees:** as a Sorcerer or Bard he opens on a bot group, mezzes two, and his mez on the third is refused: "Your
  target is enraged and resists the spell!" (any bot below 75% health). Later he notices the reverse: a bot whose 60 s
  mez has just run out is mezzed again at once by his own AE, for the full duration; a rooted bot can be rooted again the
  moment it breaks. Bots against bots do the same to each other. The player himself gets the period rule: about 60 s
  immune after a mez, stun or root wears off, and can be mezzed at any health.
- **Chain link:** fight (crowd control). The rubric 3.3 and 8: immunity about 60 s after the CC wears off, RvR players
  and guards alike [R, S]; no health rule.
- **Where:**
  - "Enraged": `spells/CCSpellHandler.cs:174-178`, `target is GameNPC && target.HealthPercent < 75`. A bot is a
    `GameNPC`. The bots' planner knows and works around it (`bots/autonomous/RvrCrowdControlPlan.cs:175-193`), so bots
    stop casting on hurt bots; the player is not told.
  - Monster immunity: `ECS-Effects/ECSGameEffect.cs:33` (player immunity only when `Owner is GamePlayer`);
    `ECS-Effects/CrowdControlECSEffect.cs:93-94` and `:131-132` (an NPC's immunity starts when the CC *starts* and lasts
    60 s, `ECS-Effects/ECSImmunityEffect.cs:28-57`, each new CC inside it at half, a quarter... of its duration, refused
    below 1 s). So a 60 s mez on a bot ends together with its immunity; a 30 s mez can be followed by a 15 s one.
  - No root, snare or nearsight immunity for bots: the player's immunity comes from `TryApplyImmunity`
    (`ECS-Effects/ECSGameSpellEffect.cs:62-75`), players only; NPC immunity exists for stun and mez only.
  - A CC on a bot can be refreshed by the same spell; on a player it cannot (`ECS-Components/EffectListComponent.cs:692-694`).
- **How often:** every group fight with a mezzer on either side; several times a fight (stated guess). In the
  battleground at about 30% population this decides most group fights (stated guess).
- **Evidence:** read. `UT_RvrCrowdControlPlan` pins that the plan *reads* these rules (`Tests/UnitTests/UT_RvrCrowdControlPlan.cs:95-131`);
  no test checks that a bot and a player get the same rule. evidence.md section 2.7 already lists mez immunity timers as
  untested.
- **Class:** the GamePlayer-keyed rule (above).
- **Fix:** with the predicate, a bot target takes the player branch at all four sites; drop the planner's `HealthPercent`
  copy. **Size S-M. Risk M:** bot-vs-bot fights get slower to lock down; the planner already reads immunity and adapts.

### 3. Every hit on an autonomous gamebot interrupts it 2.5 s longer than it would interrupt the player.

- **Player sees:** as an archer or caster he plinks an enemy bot healer once and it stands idle for five and a half
  seconds; two hits a few seconds apart lock it out of casting for the whole fight. His own casts recover after 3 s. A
  bot archer he hits stops drawing for the swing time plus 2.5 s.
- **Chain link:** fight (interrupts). Rubric 3.5: the 1.65 interrupt length is unsourced, but it is one value for every
  player [R].
- **Where:** `gameobjects/GameNPC.cs:3146-3156` adds 2,500 ms to any interrupt on an NPC whose brain has no player
  owner. `BotBrain` is an `IControlledBrain` (`bots/BotBrain.cs:22`) whose owner is `BotBody.Owner`
  (`bots/BotBrain.cs:113-116`); an autonomous gamebot's `Owner` is never set (`bots/GameBot.cs:38`; only the companion
  constructor sets it, `:2724`). So the 2.5 s goes to every autonomous gamebot and not to companions. The base interrupt
  is `SPELL_INTERRUPT_DURATION` = 3,000 ms (`serverproperty/ServerProperties.cs:2401-2402`), melee uses the swing
  interval (`ECS-Components/AttackComponent.cs:1312`); while interrupted an NPC's ranged attack stops
  (`ECS-Components/Actions/AttackAction.cs:146-154`).
- **How often:** every hit on a bot, in every fight (computed: a spell interrupt is 5.5 s instead of 3 s, +83%).
- **Evidence:** read.
- **Class:** the GamePlayer-keyed rule (a monster-pulling convenience, "makes repositioning them easier").
- **Fix:** skip the 2.5 s for any `GameBot` (or use the predicate). **Size S. Risk S** for RvR; check bot tanking in PvE,
  where the delay hid nothing a player would see.

### 4. A hurt bot slows to a crawl, a stealthed bot runs at full speed, and a bot's sprint does nothing.

- **Player sees:** a bot below a third of its health slows down to as little as a fifth of run speed, so no bot can
  ever run away from him; when he is low he runs at full speed. An enemy assassin bot closes on him in stealth at full
  run speed (a player stealther moves at about 60% of run at 50 Stealth). A fleeing bot "sprints" with no speed gain.
- **Chain link:** move (kiting, disengage, stealth approach). Rubric 3.7 (kite, run like hell) and 4.3 (openers).
- **Where:** `propertycalc/MaxSpeedCalculator.cs:142-143` (NPC branch: below 33% health, 20%-100% speed; a bot takes
  it); the stealth factor and Mastery of Stealth (`:67-90`) and sprint (`:100-101`) are in the `GamePlayer` branch only;
  `bots/GameBot.cs:1706-1726` turns a sprint effect on, which the calculator ignores for a bot, and `:555-575` drains no
  endurance for it. Retreats ask for it (`bots/BotBrain.Retreat.cs:271`). A companion that follows its player gets the
  pet rule, the player's speed plus 30% even in combat (`MaxSpeedCalculator.cs:113-137`).
- **How often:** every chase and every stealth approach; stealth openers are rubric 4.3's whole game.
- **Evidence:** read.
- **Class:** the GamePlayer-keyed rule.
- **Fix:** move stealth speed, sprint and its drain into a shared path for `GameBot`; skip the low-health slow for
  bots. **Size S. Risk M:** stealth bots get slower to their targets (period-right); stealth.md should own the timing.

### 5. A rezzed bot fights at full strength at once; the player gets rez sickness.

- **Player sees:** his group kills an enemy healer bot, its friend rezzes it, and it heals at full effect in the same
  fight. When he is rezzed in RvR he carries the RvR resurrection illness (spell 8181) and fights at reduced
  effectiveness.
- **Chain link:** win or lose (rez). Rubric 3.9: rez sickness applied after a rez [M]; 3.4 re-kill the freshly rezzed.
- **Where:** `bots/GameBot.cs:5918-5922` (`Effectiveness` is fixed at 1.0 and ignores sets);
  `bots/autonomous/AutonomousLivingServer.Events.cs:606` ("Bots do not get resurrection illness");
  `bots/GameBot.cs:1385-1407` applies none. The player: `gameobjects/GamePlayer.cs:1841-1858`.
- **How often:** every bot rez in RvR (stated guess: one or two per group fight that lasts a minute).
- **Evidence:** read.
- **Class:** the GamePlayer-keyed rule, plus a bot-side shortcut.
- **Fix:** apply the same illness spell to a bot on revival above `RESS_SICKNESS_LEVEL` and let `Effectiveness` follow
  it. **Size S-M** (Effectiveness is read by many bot paths). **Risk M.**

### 6. Bots have half the period kit: no Mastery of Concentration, no Quickcast, no Speed of Sound, Bunker of Faith or other big actives; and they buy an ability 1.65 removed.

- **Player sees:** no bot caster ever casts through his hits (no MoC, no QC); no Albion bot group comes in at double
  speed under Speed of Sound; no Cleric drops Bunker of Faith; no Healer chain-rezzes with Perfect Recovery; no bot uses
  True Sight or Vanish. With finding 3, a bot caster under pressure casts only instants.
- **Chain link:** fight (realm abilities, quickcast). Rubric 3.8 lists MoC, Speed of Sound, Bunker of Faith, Perfect
  Recovery, Determination, Raging Power, See Hidden, True Sight, Vanish, Purge, Group Purge and Ignore Pain as "in use".
- **Where:** `bots/autonomous/AutonomousBotRealmAbilities.cs:48` (the only actives a bot may own: Purge, Ignore Pain,
  Second Wind, First Aid), `:166` (every other timed ability left out of the catalog); `bots/GameBot.cs:3307-3309`
  (quickcast "explicitly disabled for all bot kinds"). The builds buy Avoidance of Magic (`:62`, `:67`, `:72`, `:79`),
  which the rubric lists as removed at 1.65 [S, wiki]; the server also still has Brilliant Aura of Deflection and
  Volcanic Pillar (`realmabilities/handlers/AtlasOF_BrilliantAura.cs`, `AtlasOF_VolcanicPillar.cs`), so the player
  may be able to buy them too (could not check the class tables, see below). The builds are by archetype, not by build
  (ERA-BUILDS-1.2.md:59).
- **How often:** every fight (MoC and QC); once a fight per group for the long timers (rubric 3.8).
- **Evidence:** read. `UT_BotRealmAbilitiesOct6` tests the plan computes (unit).
- **Class:** missing capability; belongs in the realm-ability catalog plus the fight plan (who fires which timer when).
- **Fix:** let bots use QC on a single important cast (a mez, a big heal) under interrupt, with its real timer; add MoC,
  Speed of Sound, Bunker of Faith, Perfect Recovery and True Sight as actives the RvR plan fires once a fight; drop
  Avoidance of Magic from the builds and check the period list of buyable abilities for players too. **Size M. Risk M.**

### 7. Crits against a bot go to 100%, against the player to 50%; a bot never has to face to hit.

- **Player sees:** his crits on bots are often huge; bots' crits on him never are. Bot-on-bot fights end fast. A bot he
  circles behind keeps swinging; he must face his target.
- **Chain link:** fight (damage).
- **Where:** melee crit range `ECS-Components/AttackComponent.cs:2211` and `:2214` (`ad.Target is GamePlayer ? 0.5 : 1.0`);
  spell crit `spells/SpellHandler.cs:3230`; the in-front and line-of-sight check for melee is for a `GamePlayer`
  attacker only (`AttackComponent.cs:1077-1084`).
- **How often:** about one hit in ten crits (stated guess); every melee exchange for facing.
- **Evidence:** read.
- **Class:** the GamePlayer-keyed rule.
- **Fix:** the predicate at both crit sites; for facing, a bot turns before it swings (NPCs turn as they attack, so the
  visible effect may be small; check in play). **Size S. Risk S.**

### 8. A bot's spell at another bot is never checked for line of sight; a bot's spell level is its own level.

- **Player sees:** at a keep or on a hill, bot casters of his realm and the enemy's nuke and mez each other through the
  wall or the ridge. His own casts need sight (his client checks). A bot casting a level 40 spell at him rolls as if it
  were level 50.
- **Chain link:** fight (perception at cast time).
- **Where:** `ECS-Components/NpcCastingComponent.cs:88-118`: the line-of-sight checker is the target if it is a
  `GamePlayer`; for a `GameBot` caster the owner proxy is refused (`:100-103`); the nearby-player fallback is for
  `StandardMobBrain` only (`:106`); with no checker the cast goes unchecked (`:51-52`). Spell level: `spells/SpellHandler.cs:2426-2427`
  ("NPCs go there too": a caster that is not a `GamePlayer` uses its own level).
- **How often:** every bot-on-bot cast with terrain in between; at keeps often (stated guess). The spell-level bonus is
  small: (caster level - spell level) / 2 percent to hit, about 0-5% for the spells bots pick.
- **Evidence:** read.
- **Class:** the GamePlayer-keyed rule. Not seen by the player when he is the target, which is why it went unnoticed.
- **Fix:** for a `GameBot` caster on a non-player target, ask the navmesh (`PathfindingProvider.HasLineOfSight`, which the
  brain already uses, `bots/BotBrain.cs:623`, `:3421`) at cast start and end; use the spell's level for bot casters.
  **Size M. Risk M** (navmesh cost; one check per cast).

### 9. Bots are paid realm points for every death of the player, with no repeat-kill window.

- **Player sees:** camped at a milegate, he dies five times in five minutes; the same bots rank up each time. Kill the
  same bot twice inside five minutes and he is told it "has been killed recently and is worth no realm points".
- **Chain link:** win or lose (reward), and rank progression for bots (rubric 2.5: ranks cluster RR2-4).
- **Where:** `serverrules/AbstractServerRules.cs:1950` calls `PayGamebotsForPlayerKill` before, and without, the
  player's worth check (`:2068`, `RP_WORTH_SECONDS` = 300, `serverproperty/ServerProperties.cs:876-877`);
  `bots/autonomous/AutonomousBotRealmPointRewards.cs:275-289` pays the full value. A bot victim does honour the window
  (`:125-136`).
- **How often:** every player death in RvR; worst when he is camped (rubric 2.4: gate camping was the norm).
- **Evidence:** read. `UT_AutonomousBotRealmPointRewards` tests the formula (unit).
- **Class:** a rule written twice (one per victim kind), one copy missing a clause. Belongs in one award path.
- **Fix:** pay bots only when `DeathTime + RP_WORTH_SECONDS <= PlayedTime`, as the player path does. **Size S. Risk S.**

### 10. The player's own group plays no RvR job: companions get none of the RvR tactics, and their peel sees only monsters.

- **Player sees:** in Emain with his companions (a tank, a healer), an enemy Berserker bot jumps his companion healer.
  His companion tank keeps its Guard where it was and never Intercepts, because the threat is not a monster. No assist
  train forms on his target unless he leads it; no interrupt duty; no mez assignment. Autonomous bot eights get all of
  it.
- **Chain link:** decide (the group's plan) for the player's seat. Rubric 3.4-3.6.
- **Where:** `bots/BotBrain.RvrTactics.cs:51-54` (tactics only for autonomous gamebots not led by a player, not
  companions, not hired); `bots/BotBrain.TankPeel.cs:105-112` (today's peel counts only a non-bot NPC hitting in melee as
  a threat); `bots/BotBrain.TankPeel.cs:25-38`. `docs/RVR-FIELD-1.2.md:313` puts "Companions, and any group a player
  leads or is in" out of the design.
- **How often:** every RvR fight the player takes with companions (stated guess: most of his RvR if he plays with them).
- **Evidence:** read. structure.md finding 5 ("the real player is invisible to every strategic layer") is the same
  structural cause seen from the strategy side.
- **Class:** a fight plan that exists in one path only (structure.md section 3).
- **Fix:** first the S part: `BeingHitInMelee` counts enemy players, bots and their pets (as `BotPvpCrowdControl.PlayerLike`
  does). Then the M part: run the plan's peel, demez and interrupt duties for companions in a player's group, with the
  player's target as the call (structure.md target D). **Size S then M. Risk M** (companion behaviour the owner has
  seen and accepted changes).

## A. The rules, item by item

| Item (issue A) | Player | Bot | Same? | Against 1.65 | Where |
|---|---|---|---|---|---|
| CC duration and resists | spell hit chance shortens below 55, lengthens above 100 | same | yes | lengthening is not period (resists never lengthen, rubric 3.2 [S]); only reached with big level gaps | `spells/CCSpellHandler.cs:58-93` |
| Mez/stun immunity | 60 s after wear-off | NPC diminishing, from start; mez refused below 75% health | **no** (F2) | player right | F2 |
| Root, snare, nearsight immunity | 60 s after wear-off | none | **no** (F2) | roots right [R]; a 60 s immunity after a snare is not sourced [M] | `ECS-Effects/ECSGameSpellEffect.cs:62-75`, `gameutils/EffectHelper.cs:294-299` |
| What breaks mez/root | damage | same | yes | right | base server; `bots/BotBrain.CrowdControlRespect.cs` keeps bots off mezzed targets |
| AE mez falloff | half at the edge, RvR only | same (bot's brain counts as controlled) | yes | right (rubric 3.2 [S]) | `spells/SpellHandler.cs:2237-2238` |
| Stun styles | 60 s immunity; pet stuns none | NPC rule | **no** (F2) | unsourced (decision 9) | `ECS-Effects/CrowdControlECSEffect.cs:78-84` |
| Interrupts | 3 s / swing time | +2.5 s for autonomous gamebots | **no** (F3) | length unsourced [R] | F3 |
| Interrupt window | only the first half of a cast | same | yes | probably later than 1.65 [M]; flag for the rubric | `spells/SpellHandler.cs:351-356` |
| Pet hit interrupts the caster | no | no | yes | right (rubric 3.5 [R]) | base server |
| Quickcast, MoC | yes | no | **no** (F6) | bots short | F6 |
| Purge and first-generation RAs | full list | 4 actives | **no** (F6) | bots short | F6 |
| Spell damage, PvP modifier | 1.0 | 1.0 | yes | right (default) | `spells/SpellHandler.cs:3187-3194`, `ServerProperties.cs:811-817` |
| Spell to-hit level | spell level | caster level | **no** (F8, small) | player right | F8 |
| Bolt block, in-combat bolt miss | block yes; in-combat miss player-on-player only | block yes; none | yes for bot vs player | the in-combat miss is a 1.84 rule, so "none" is period-right | `spells/BoltSpellHandler.cs:64-82`, `:93-104` |
| Crits | 50% cap on a player | 100% on a bot | **no** (F7) | player right | F7 |
| Positional styles | angle and front checks | same | yes | right | `styles/StyleProcessor.cs:81-110` |
| Melee facing | must face | need not | **no** (F7) | player right | F7 |
| Power cost | paid | paid (own formula, same inputs) | yes | right | `spells/SpellHandler.cs:1222-1223`, `bots/BotSpellPower.cs:35-67`, `bots/GameBot.cs:3292-3298` |
| Endurance for styles | paid | paid | yes | right | `styles/StyleProcessor.cs:305-307`, `:481-488` |
| Sprint | +30%, drains | no speed, no drain | **no** (F4) | player right | F4 |
| Cooldowns | kept | kept (spells, RAs, Intercept) | yes | right | `AutonomousBotRealmAbilities.cs:300`, `bots/BotBrain.TankPeel.cs:67-74` |
| Regeneration | native ticks | 10%/s floor on the move | **no** (F1) | player right | F1 |
| Run speed hurt / stealthed | full / stealth factor | slowed / full | **no** (F4) | player right | F4 |
| Bow range and damage | weapon range | same | yes | not measured against 2,000-2,300 | `bots/BotRangedCombat.cs:162-169` |
| Doors and guards | keep damage bonus for players only | none | **no**, small | for siege.md | `ECS-Components/AttackComponent.cs:900` |
| Pets in RvR | normal | normal; at most 2 pet classes per formed group of 5+ (1 below 5) | n/a | a fix for a 2003-false "10 Cabalist pets" complaint | below |
| Rez and rez sickness | RvR illness | none | **no** (F5) | player right | F5 |
| Con or XP loss in RvR | none (normal server) | none | yes | right [M] | `gameobjects/GamePlayer.cs:6050-6067` |
| RP value | (level-20)^2 + realm level | same formula | yes | right (L50 about 900, rubric 2.5 [S retro]) | `bots/autonomous/AutonomousBotRealmPointRewards.cs:24-29` |
| Repeat kills | 300 s window | window for bot victims; none when bots kill the player | **no** (F9) | player right | F9 |
| Group credit | 12.5% per extra contributor | same | yes | right | `AutonomousBotRealmPointRewards.cs:31-55` |
| Rank and RA purchase for bots | at trainer | at trainer, archetype build, RR5 left out | yes | period list wrong (F6) | `AutonomousBotRealmAbilities.cs:155-172`, `:209-231` |
| Stealth detection of the other side | formula | bot's detection is its own model | see stealth.md | | `gameobjects/GamePlayer.cs:11199-11218`; `bots/autonomous/AutonomousRvrPerception.cs` |
| Buff cap | 20 effects | none | no, small | player right | `bots/GameBot.cs:343-346` |
| Line of sight for spells | checked | bot on bot unchecked | **no** (F8) | player right | F8 |

**Do bots pay costs and wait for cooldowns?** Yes, for power, style endurance, spell recast timers and realm-ability
timers (table rows above). They do not pay sprint endurance (it buys them nothing either) and they skip downtime (F1).
**Instant casts, perfect interrupts, seeing through stealth:** no instant-cast cheat found; interrupts are slower than
the player's, not perfect (`bots/autonomous/RvrInterruptPlan.cs:36-49`, a look every 1.5-3 s); stealth is stealth.md's.

**The pet cap (AutonomousRvrPetCap).** Why: the r/daoc report of "about ten Cabalist pets" chasing one player
(`bots/autonomous/AutonomousRvrPetCap.cs:6-12`, `docs/BOT-BUILDS-AND-RVR.md`). What: `rvr_group_pet_cap` = 2
(`serverproperty/ServerProperties.cs:2699-2700`) pet classes per gamebot group of five or more, one below five; applied
when warbands, siege parties and eights form (`bots/autonomous/AutonomousBotGroupCoordinator.cs:1933-1935`,
`AutonomousBotGroupCoordinator.SiegeParties.cs:40-45`, `AutonomousBotGroupCoordinator.RvrTactics.cs:150`). Visible:
only in the log (`RVR_PARTY_FORMED`) and as fewer pets; the player is never told. Not covered: the 15% who go alone or in
duos, and battleground waves if they do not form through the coordinator (I found no use of the cap in
`AutonomousBattlegroundWave.cs`; could not confirm how waves group). Hunters' and Druids' pets are not counted (by
design, `:15-16`). Against the period: two pet classes in an eight is inside the rubric's skeletons (2.2: a Theurgist in
Albion's eight, a Spiritmaster or Bonedancer common in Midgard) [R].

## B. Each class's RvR job

What the code does is read from the class AI, the RvR tactics (autonomous gamebots only, F10) and the era builds
(`bots/autonomous/data/era-builds-165.json`). "Generic" means the class has no RvR-specific code: it attacks its
target, casts its damage, heals if it can. Gaps ranked by how much they change who wins and how real it looks:
**A** changes fights, **B** visible to a watcher, **C** detail. Fix size S/M/L.

### The cross-class gaps (apply to many rows)

- **PBAoE casters never walk in on players or bots.** `bots/BotBrain.AreaSpells.cs:283-285`: the walk-in is refused when
  any enemy player or bot is within 1,200 (`HasBlastPlayerLike`, `:81-92`), and no RvR tactics code moves a bomber in (no
  match for PBAE in `BotBrain.RvrTactics.cs`, `BotBrain.RvrPositioning.cs`, `autonomous/Rvr*.cs`). A bomber only blasts
  when enemies stand on it (`:280-282`). The Hibernian template builds "two point-blank casters" into its eight
  (`bots/autonomous/RvrRosterTemplates.cs:243`) who then never do their job. Rank **A** for Hibernia (rubric 2.2: the
  caster group, 3-4 Enchanters behind a slam tank; mez then bomb). Fix **M**: after the group's AE mez lands, the bomber
  walks in on the mezzed pile only when the plan says so, and stops casting AE damage on a pile its group mezzed until
  the train moves on.
- **The mez that opens a fight is not planned as an opener.** The planned CC (`bots/BotBrain.PvpCrowdControl.cs:66-118`)
  holds the enemies the kill target is not; nothing makes the group's AE mez the first action on the charge (rubric 3.2).
  Roam.md should own this; rank **A**.
- **Interrupt duty is our design** (`bots/autonomous/RvrInterruptPlan.cs:19-28`, stated as unsourced); archers first,
  then instants, pets, fast casts, melee in reach. Fine against the rubric 3.5 as observable behaviour. Rank C.
- **Companions run none of it** (F10).
- **The older CC path** used by everything outside the tactics scope (`TryPvpCrowdControl`, `BotBrain.PvpCrowdControl.cs:13-54`)
  does not read immunity, so it wastes casts on immune players ("Your target is immune"). Rank C, fix S (call
  `RvrCrowdControlPlan.MezImmune` as the planned path does).

### The table (classic and Shrouded Isles, never class 63)

| Realm | Class | Should do in a group fight (rubric) | What the code does | Worst gap | Rank | Fix |
|---|---|---|---|---|---|---|
| Alb | Armsman | shield tank: guard, intercept, slam a meleer on the healer; or polearm damage | Tank role (`bots/BotPartyRoles.cs:14-18`); Protect, Guard with shield, Intercept below half health, slam the attacker it pulled (`bots/BotBrain.TankPeel.cs:149-200`, `:238-268`) | none big in bot eights; nothing in the player's group (F10) | B | S |
| Alb | Paladin | chant tank, guard the healer, endurance chant | Tank role; one chant (endurance in a fight, `bots/BotSongTwistPolicy.cs:130-131`); melee hybrid healer (`BotPartyRoles.cs:38-39`) | none found beyond the generic | C | - |
| Alb | Mercenary | burst damage on the assist, slam (shield builds) | off-tank, generic melee; 30% "DW/Shield (slam)" builds | slam only through the tank peel, which is for Tank roles | C | S |
| Alb | Reaver | tank, PBT, interrupts with flex | Tank role; generic | none found | C | - |
| Alb | Cleric | main healer, rez, stand back; smite builds assist | healer; heals, demez (`bots/BotBrain.cs:5831-5845`), rez; no Bunker of Faith, no Perfect Recovery (F6) | the period's Albion signature (two Bunkers) missing | B | M |
| Alb | Friar | staff melee healer | melee hybrid healer (`BotPartyRoles.cs:38-39`) | none found | C | - |
| Alb | Minstrel | speed, mez, "front line rupt", stealth | speed while travelling, endurance in fight (`BotSongTwistPolicy.cs:98-102`); mez via CC; interrupt duty if instant/fast spell | no Speed of Sound (F6) | A | M |
| Alb | Infiltrator | stealth opener, kill casters, Vanish | stealth opener (stealth.md); full run speed in stealth (F4); no Vanish (F6) | F4 | B | S |
| Alb | Scout | arrows on casters (interrupts), finish runners | dedicated archer (`bots/BotRangedCombat.cs:37`), first pick for interrupt duty | none found | C | - |
| Alb | Sorcerer | main CC: AE mez, single mez; body DD | 40% "Mind/Body (AE mez)" builds, the CC slot prefers a CC build (ERA-BUILDS WP5); planned CC mez first | F2 (cannot mez a hurt bot; can chain-mez bots); no MoC/QC | A | S |
| Alb | Theurgist | pets interrupt, root, speed of pets | pet class (cap 2); pet as interrupt means | no special pet use against casters beyond "the pet goes where its owner fights" (`RvrInterruptPlan.cs:12`) | C | M |
| Alb | Wizard | primary nuke on the assist; ice PBAE | generic nukes; 15% Ice PBAE builds that never bomb | PBAE gap | B | M |
| Alb | Cabalist | pet, DoTs, debuff then nuke | pet class; Cabalist pet shield disabled for bots (`bots/GameBot.cs:3277-3288`) | debuff-then-nuke not checked in RvR | C | - |
| Alb | Necromancer | pet damage, Facilitate Painworking, lifetap | shade and servant handled (`bots/GameBot.cs:702-735`, `:2212-2265`) | not checked in RvR | C | - |
| Mid | Warrior | shield tank, guard, slam | Tank role; peel as Armsman | as Armsman | B | S |
| Mid | Thane | hybrid: storm DD and melee | Tank role; generic | none found | C | - |
| Mid | Berserker | burst damage, Berserk | off-tank, generic; Berserk crit range in the server (`AttackComponent.cs:2196-2209`) | none found | C | - |
| Mid | Savage | burst damage; must be stunned on sight (rubric 3.6) | self-buff policy (`bots/SavageBotCombatPolicy.cs:7-11`) | none found | C | - |
| Mid | Skald | speed, interrupt, damage | speed while travelling (`BotSongTwistPolicy.cs:98-102`); melee | none found | C | - |
| Mid | Healer | main healer; Pac builds mez and stun; demez | healer; demez; 70% Pac or tri-spec builds; CC via the planned path | F2; no Perfect Recovery (F6) | A | S |
| Mid | Shaman | heal, cure, disease and debuff | hybrid policy: nukes, DoT, disease; roots kept for adds (`bots/ShamanBotCombatPolicy.cs:3-50`) | roots kept "for adds" also in RvR | C | S |
| Mid | Runemaster | primary nuke (Darkness), bolts | generic nukes and bolts (`GameBot.cs:2413-2417` sorts bolts) | none found | C | - |
| Mid | Spiritmaster | PBAE (Suppression), pet, mez | pet class; 45% Suppression PBAE builds that never bomb | PBAE gap | B | M |
| Mid | Bonedancer | pet army, lifetap | commander and minions maintained (`bots/autonomous/AutonomousPetSupport.cs:422-535`) | not checked in RvR | C | - |
| Mid | Hunter | arrows, pet | dedicated archer | none found | C | - |
| Mid | Shadowblade | stealth opener, kill casters | as Infiltrator | F4 | B | S |
| Hib | Hero | shield tank, guard, slam | Tank role; peel | as Armsman | B | S |
| Hib | Champion | debuffs, interrupts (rubric 3.5) | Tank role; generic | no Champion debuff or interrupt role | C | M |
| Hib | Blademaster | burst damage | off-tank, generic | none found | C | - |
| Hib | Valewalker | scythe damage | off-tank, generic | none found | C | - |
| Hib | Warden | heals, guard, interrupts | melee hybrid healer | no interrupt role (rubric 3.5 names it) | C | M |
| Hib | Druid | main healer, root | healer; demez | F6 (no Perfect Recovery) | B | M |
| Hib | Bard | speed, AE mez, instant mez, heals | speed in travel; 70% Music/Nurture builds; CC slot | F2 | A | S |
| Hib | Eldritch | PBAE "manabomb", Void bolts | generic nukes; 40% Mana PBAE builds that never bomb | PBAE gap | A | M |
| Hib | Enchanter | PBAE behind a slam tank; pet | pet class (capped); 65% Mana PBAE builds that never bomb | PBAE gap; the cap also limits the period's "3 Chanters" (rubric 2.2) | A | M |
| Hib | Mentalist | PBAE or light DD, heals | 45% Mana/Mentalism PBAE builds that never bomb | PBAE gap | B | M |
| Hib | Animist | turrets, bomber | turret policy (`bots/autonomous/AutonomousPetSupport.cs:337-338`) | not checked in RvR | C | - |
| Hib | Nightshade | stealth opener | as Infiltrator | F4 | B | S |
| Hib | Ranger | arrows on casters | dedicated archer | none found | C | - |

**Builds and realm abilities against ERA-BUILDS-1.2.md.** The class shares and builds come from the era data file and
match the doc's sources (`docs/ERA-BUILDS-1.2.md:63-185`; not re-checked here). The realm abilities do not: one Atlas
build per archetype (`AutonomousBotRealmAbilities.cs:56-80`), not per era build, as ERA-BUILDS-1.2.md:59 says, with the
gaps of F6. **Do bot ranks give period-right abilities?** Partly. RR5 abilities are left out (`:166`), as the rubric
asks; the period's group-defining actives are missing; Avoidance of Magic, removed at 1.65, is bought.

## What I checked and found sound

- Bots pay power, style endurance, spell and ability timers (table A).
- Positional style rules, AE mez falloff, the PvP damage modifiers (1.0), bolt blocking against bots: one rule for both.
- RP value per kill (900 + rank at L50), group bonus and the repeat window for bot victims match the player rule.
- No RvR con or XP loss for the player on a normal server.
- Demez by bot healers, the player first (`bots/BotBrain.cs:5838-5845`).
- Tanks' Protect, Guard, Intercept (on its one-minute timer) and slam in bot-only groups.
- Bots never get the custom class here: none of the code above makes one, and `AutonomousRvrPetCap.IsPetClass` lists it
  only so the cap counts it (`AutonomousRvrPetCap.cs:16-19`).

## What I could not check, and why

- Which realm abilities a player can buy at 1.65 (Avoidance of Magic, Brilliant Aura, Volcanic Pillar): the class-to-RA
  tables live in the world database, not in Git.
- Whether battleground waves are capped for pets, and the battleground realm-rank cap for bots: not found by search.
- Facing in bot melee (F7): whether NPC turning hides it in play.
- Whether players' 60 s immunity after a *snare* is period-right: no source at hand [M].
- Exact period interrupt length and interrupt window: the rubric leaves them open [R].
- No dotnet run, so no unit test or sim was run for this report.

## What only the owner's PC can settle

1. **F1, regeneration.** In Emain, kill all but one of a bot eight; note the survivor's health in `/target` every 5 s
   for 30 s. Expect full in about 20 s on this build. Log: `RVR_BOT_KILLED` for the fight's end.
2. **F2, enraged.** As a mezzer, damage one enemy bot below 75% and mez it: look for "Your target is enraged and resists
   the spell!" in the combat log.
3. **F2, chain mez.** AE mez a bot group with a mez of 30 s or more, let it run out, mez again at once: does it land for
   full or half duration?
4. **F3, interrupts.** Hit an enemy bot healer once with an arrow or instant spell and time how long it stands idle
   (expect about 5.5 s).
5. **F4, speed.** Watch a bot below a third of its health run; watch an enemy stealther bot approach on `/stick` from a
   companion, or measure with two `/loc`s.
6. **F5.** `/target` a bot just rezzed: any resurrection illness icon?
7. **F9.** Die twice inside five minutes to the same bots; check whether their rank rises twice (the launcher's roster
   or `BOT_REALM_ABILITY_TRAINED`).
8. **F10.** With companions in Emain, let an enemy melee bot hit your companion healer: `RVR_PEEL` lines (none expected
   for companions).

## Measuring tools worth building

- **A rule-parity test.** One NUnit fixture that builds a `GamePlayer` and a `GameBot` of the same class and level and
  asks each rule site of table A the same question (immunity after a mez, interrupt length, max speed hurt or stealthed,
  crit range, LOS checker, rez illness, RP worth window). Every row "same" turns green only when the predicate is used.
  Small, pure enough, and it would have caught eight of the ten findings. Reports to evidence.md's ledger.
- **A per-fight resource trace.** At each `RVR_BOT_KILLED`, log the survivors' health, power and endurance and again 30 s
  later (one line, `RVR_AFTER_FIGHT`), so the PC log shows the reset of F1 and its fix.
- **A CC ledger line.** `RVR_CC_APPLIED target kind=player|bot duration immunity_left` from the CC effect start, so the
  chain-mez of F2 shows in one PC log.

## Hand-back

Report: `docs/rvr-audit-1.2.3/mechanics.md` (issue #501), read-only audit on `de53d0d3`; no tests or sims run.
Root cause of most defects: `GameBot` is a `GameNPC`, and the base server's PvP rules branch on `is GamePlayer`, so bots
get monster rules. One predicate ("is a player character") at about ten sites fixes F2-F5, F7, F8 (size M overall).
Top asymmetries (all read, file:line in the findings):
1. Bots regain 10%/s of health, power and endurance on the move 10 s after a fight (`bots/BotRestRecovery.cs:26-27,68-80`).
2. A bot below 75% health cannot be mezzed; its immunity is the monster one; roots/snares give it none
   (`spells/CCSpellHandler.cs:174-178`, `ECS-Effects/ECSGameEffect.cs:33`, `ECSImmunityEffect.cs:28-57`).
3. Autonomous gamebots are interrupted 2.5 s longer than the player (`gameobjects/GameNPC.cs:3146-3156`).
4. Hurt bots slow to 20% speed; stealthed bots run full speed; bot sprint does nothing (`propertycalc/MaxSpeedCalculator.cs:67-101,142-143`).
5. No rez sickness for bots; Effectiveness fixed at 1.0 (`bots/GameBot.cs:5918-5922`).
6. Bots lack MoC, QC, Speed of Sound, Bunker of Faith, Perfect Recovery, True Sight; buy Avoidance of Magic
   (`AutonomousBotRealmAbilities.cs:48,166`, `GameBot.cs:3307`).
7. Crits on bots up to 100%, on the player 50%; bots need not face (`AttackComponent.cs:2211-2214,1077-1084`).
8. Bot-on-bot spells skip line of sight (`ECS-Components/NpcCastingComponent.cs:88-118`).
9. Bots get RP for every death of the player, no 300 s window (`serverrules/AbstractServerRules.cs:1950`).
10. Companions and player-led groups get no RvR tactics; companion peel ignores enemy players and bots
    (`BotBrain.RvrTactics.cs:51-54`, `BotBrain.TankPeel.cs:105-112`).
Class jobs: worst gap is PBAoE casters never walking in on players (`BotBrain.AreaSpells.cs:283-285`), which empties the
Hibernian caster group and Spiritmaster/Ice Wizard builds; then Albion's missing Speed of Sound and Bunker of Faith.
Sound: power, endurance and cooldowns are paid; RP values, group credit, AE mez falloff and positionals match.
PC checks: eight concrete looks listed above. Tool worth building first: a player/bot rule-parity unit test.
