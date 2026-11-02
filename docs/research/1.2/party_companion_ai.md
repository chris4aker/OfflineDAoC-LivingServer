# Party and Companion AI in Well-Regarded Games: What a Living Server Should Borrow for Bot Groups

Scope note: 17 tool calls were used. Verified, cited sources cover FFXII gambits (2006 game, 2017 interview), Left 4 Dead survivor bots (Valve 2009 deck, GDC 2012 dialog talk), WoW follower dungeons (2024 article), FFXIV Trust (2019 article), EverQuest mercenaries (player guide), Dragon Age: Origins tactics (wiki), Pillars of Eternity 2 behaviour editor (guide), and Dragon's Dogma 1 and 2 pawns (2012 Capcom blog, 2024 interview). The Guild Wars wiki (both domains) sat behind a login wall and could not be fetched. The Dragon's Dogma fandom wiki was not on the fetch allowlist. Mass Effect, Baldur's Gate 3, Persona/SMT, Xenoblade, Divinity, Bethesda followers and Monster Hunter palicos were **not verified** in this pass. Anything said about them below is recall and sits in Gaps.

## 1. How do these systems define companion behaviour and let players shape it?

### Takeaway
Companion control has converged on a few layers. At the bottom is a **fixed, hand-authored role brain**: WoW followers, FFXIV Trust, the L4D survivors. Above that, the player picks **stances or presets**: EQ mercenary stances, PoE2 Aggressive/Cautious, PoE2's auto-attack autonomy levels. At the top, enthusiasts get an **ordered condition→action list**: FFXII gambits, DA:O tactics, the PoE2 behaviour editor. Coarse **positional commands** (GW1 flags, ME squad orders; both unverified here) sit across all three. Stances and presets give most of the control for the least UI. The rule list is the "deep" option. It earns its keep when it is unlocked progressively, as in DA:O, where slots grow with level and a skill.

### Cited Findings
- **FFXII gambits (game 2006; interview 2017).** The player hands party members over to AI that runs player-assigned conditional rules, e.g. "if health drops below 30%, use health potion." Takashi Katano said the precedent of Final Fantasy IV's monster AI (designed by Hiroyuki Ito) let the team "manage the party without micromanaging the characters." Commanding every member individually "would have taken away from" the fun, and a traditional player-controlled party was never seriously considered. One side effect: a member running an active gambit could wander off and trigger a cutscene. — [Game Developer, 2017](https://www.gamedeveloper.com/design/why-i-final-fantasy-iv-i-was-key-to-i-ffxii-i-s-ai-driven-gambit-system)
- **Dragon Age: Origins tactics (2009).** Each tactic pairs a condition with an action, e.g. drink a potion below 50% health. Everyone starts with 2 slots, including companions and summons/pets. Free extra slots come at levels 3, 6, 10, 15, 20, 25 and 30. The Combat Tactics skill line adds +1, +1, +2 and +2 slots and needs Cunning 10/12/14/16. The wiki frames the system as letting the party act without manual tactical intervention. The fetched page did not cover top-to-bottom priority or the named presets. — [Dragon Age Wiki](https://dragonage.fandom.com/wiki/Combat_Tactics)
- **Pillars of Eternity II (2018).** Each character gets an AI menu (portrait → gear icon). There are two default behaviour presets, "Aggressive" and "Cautious", and both open in a Behavior Editor. A separate, fixed **auto-attack autonomy** setting has four levels: Passive (never attacks automatically), Self-defense (fights only when attacked), Defensive (attacks nearby enemies) and Aggressive (attacks all enemies). The editor builds an ordered list of behaviours: a condition (low health, a ranged ally being attacked, and so on), then an ability, a target and a priority (e.g. target an enemy weak to frost). — [Gamepressure guide](https://www.gamepressure.com/pillars-of-eternity-2/partys-ai/z1ae65). A third-party mod ("More Custom AI Conditions") exists, which suggests players wanted richer conditions than shipped. — [Gamepressure mod listing](https://www.gamepressure.com/download/pillars-of-eternity-ii-deadfire-more-custom-ai-conditions-v400-m/z510a03). Pillars of Eternity 1 only got party AI later, in update 2.0. — [PC Gamer](https://www.pcgamer.com/uk/pillars-of-eternity-update-20-adds-party-ai-solo-stealth)
- **EverQuest mercenaries (2009–).** There are four archetypes: Tank, Healer, Rogue (melee) and Wizard (caster). Each has a stance list:
  - Tank: Aggressive (attacks when you or a group member is attacked), Assist (assists the main assist), Passive.
  - Healer: Balanced, Reactive, Efficient (conserves resources), Passive (neither attacks nor heals).
  - Wizard: Balanced, Burn, Burn AE (focuses on area damage), Passive.
  - Rogue: Balanced, Burn, Passive.

  Stances can be hotkeyed. The **Puller role** is assigned by right-clicking the group vitals bar. A "confidence" value governs whether the merc runs away or refuses to act. Mercs level with the owner, don't use player gear, and cost upkeep (2 gold per 15 minutes from level 14). — [Almar's Guides](https://www.almarsguides.com/EQ/General/Mercenaries.cfm)
- **WoW follower dungeons (patch 10.1.5 / 10.2.5, 2023–24).** The roles are fixed named NPCs: a protection paladin tank, a restoration druid healer, a BM hunter or enhancement shaman for damage, and a fire mage. Damage-dealer NPCs are swapped so they don't duplicate the player's class. The player's main control is a **"Dungeon Assistance" toggle**. On, the NPCs pull when you're nearby and call out mechanics like a tutorial group. Off, they wait for the player to lead but still support. — [PC Gamer, Jan 2024](https://www.pcgamer.com/world-of-warcraft-wow-follower-dungeons/)
- **FFXIV Trust (Shadowbringers, 2019).** Yoshida said the team "manually programmed this AI" to bring out each character's personality, and that it took "a lot of development resources". He called extending it to older content "a very difficult decision" because of the cost. The NPCs fill whatever role the party needs. — [GameSpot, 2019](https://www.gamespot.com/articles/ffxiv-shadowbringers-ai-companions-matter-more-tha/1100-6468377/)
- **Left 4 Dead survivor bots (2008; Valve deck 2009).** There is no player configuration. Two concurrent systems run each bot: "Main" (decisions, attention, target selection) and "Legs" (stays near the team unless told otherwise). Behaviours are hierarchical states that interrupt and resume. Every state transition (Continue, ChangeTo, SuspendFor, Done) carries a **reason string**, used for live debug output. — [Mike Booth, "The AI Systems of Left 4 Dead", Valve 2009](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
- **Dragon's Dogma 1 (2012).** Pawns learn from how the player fights rather than from explicit rules: whether the player targets strong or weak enemies first, hesitates or charges, when they flee, how much damage they take before healing, and when and how they give orders. A conversational "armrest" system determines which characteristics grow. — [Capcom-Unity blog, 2012](https://news.capcomusa.com/lets/browse/dragons-dogma-pawns-learn-organically-as-you-play). DD2 (2024) gives each pawn a personality, voice and traits that "signal the role a Pawn will play". — [GamingBolt, 2024](https://gamingbolt.com/dragons-dogma-2s-pawns-explained-in-new-developer-interview)

### Inferences
- For Living Server the cheapest high-value layer is **EQ-style per-role stances**. Concretely:
  - Healer: Efficient / Balanced / Reactive / Passive.
  - Caster: Single-target / AE burn / CC-first / Passive.
  - Tank: Aggressive / Assist / Passive.
  - Group-wide: a puller designation and a main-assist designation.

  This maps directly onto DAoC roles (main tank, main assist, puller, mezzer, healer) and needs no rule editor.
- The PoE2 split between **autonomy level** (when to engage) and **behaviour list** (what to do) cleanly separates two complaints: bots aggroing too eagerly, and bots doing the wrong thing.
- The DA:O and FFXII ordered rule list is a good "advanced" layer, maybe exposed as editable JSON beside `bot-goals.json`. Unlocking it progressively, or shipping strong presets, avoids the blank-page problem.
- The L4D reason strings (each state change logs why) are worth copying for debugging bot groups and for answering player bug reports.
- WoW's Dungeon Assistance toggle shows a simple global "lead me / follow me" switch for the whole group. That is a direct analogue for a companion group that either runs the camp itself or waits for the player to pull.

### Gaps
- **GW1 heroes and henchmen.** The wiki was behind a login wall. From recall, unverified: heroes have three behaviour modes (Aggressive / Guard / Avoid Combat), individual and "all" flags (shown as map markers), and per-skill enable/disable on the hero's 8-slot bar. Players customise the bar and attributes; henchmen are fixed. Seven heroes became possible in 2009. Verify on the GW wiki when accessible.
- **Mass Effect squad commands, BG3 AI scripts, Persona/SMT tactics, Xenoblade, Divinity, Bethesda followers, Monster Hunter palicos:** not researched in this pass.
- **FFXIV Duty Support (6.1, 2022)** extended NPC parties to ARR content despite Yoshida's 2019 cost concern. Siliconera covers it ([link](https://www.siliconera.com/ffxiv-a-realm-reborn-duty-support-trust-system-and-6-1-changes-detailed/)) but the page was not fetched, so its details are unverified.
- The DA:O preset names and top-down evaluation order were not confirmed from the fetched page. From recall they are evaluated top to bottom, first match wins.

## 2. How do they handle group roles: tanking/aggro, healing triage, CC, pulling, assist, positioning, retreat/wipe recovery, mana?

### Takeaway
The commercial MMO companions (WoW followers, FFXIV Trust, EQ mercs) solve roles with **fixed, scripted role brains plus an explicit leader**. The tank (or the player) pulls, the tank always retakes threat, and DPS follow priority targets. Healing is strong, interrupts and CC are weak, and wipes reset to a checkpoint. **The reliability of interrupts and CC is the known weak spot** even in Blizzard's version. A DAoC server built around mez and stun needs to beat this baseline rather than copy it.

### Cited Findings
- **WoW followers: tank behaviour.** The NPC tank (Captain Garrick) "grabs enemy attention even if you pull first" and "can turn mobs so you're positioned behind them". She doesn't pull extra packs on her own and sometimes avoids enemies. If the player pulls the next pack, the tank retakes threat. — [PC Gamer, 2024](https://www.pcgamer.com/world-of-warcraft-wow-follower-dungeons/)
- **WoW followers: target calling, dispels, mechanics, interrupts.** The NPCs switch to priority targets, dispel your debuffs and react to mechanics. Their **interrupts are unreliable**, so many casts get through. Healing is strong. If the player dies, the followers leave and return after release. — [PC Gamer, 2024](https://www.pcgamer.com/world-of-warcraft-wow-follower-dungeons/)
- **FFXIV Trust: mechanics, efficiency, wipes.** The NPCs understand boss mechanics, anticipate enemy attacks and prepare for phase transitions. They are **deliberately less efficient than a skilled human group**. If the player's character is knocked out, the party wipes and resets to the last checkpoint. — [GameSpot, 2019](https://www.gamespot.com/articles/ffxiv-shadowbringers-ai-companions-matter-more-tha/1100-6468377/)
- **EQ mercenaries.**
  - Tank Assist stance assists the group's main assist.
  - The Puller role is set via the vitals bar.
  - The healer's Efficient stance "conserves resources", which handles mana management through a stance.
  - The healer rezzes out of combat and buffs the group.
  - "Confidence" governs fleeing, and pulling one or two even-con mobs is usually safe.

  — [Almar's Guides](https://www.almarsguides.com/EQ/General/Mercenaries.cfm)
- **L4D bots.**
  - Contextual queries arbitrate between concurrent behaviours: SelectMoreDangerousThreat (pick the worst threat to an incapacitated teammate), ShouldHurry, IsHindrance.
  - Bots prioritise human teammates over other bots.
  - They never deal friendly fire and never use Molotovs. The game "cheats" so undesirable events can't happen.
  - A bot far out of position is **teleported near the team when no human is looking at it**.

  — [Valve 2009](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
- **Dragon's Dogma 1.** Pawns watch how much damage the player takes before healing, when they flee, and which enemies they target first, then adapt. This is an implicit form of triage and retreat tuning. — [Capcom-Unity, 2012](https://news.capcomusa.com/lets/browse/dragons-dogma-pawns-learn-organically-as-you-play)

### Inferences
- For 8-person DAoC groups, the EQ model of explicit **main assist plus puller designations** is the right core, since every bot attacks the MA's target. WoW's "tank always retakes threat" rule keeps pulls safe.
- **Not breaking mez is a hard rule, not a heuristic.** None of the verified systems shows good CC discipline, and WoW's interrupt weakness is noted even in a forgiving Normal dungeon. In code: a group-wide "mezzed set" that the MA's target selection and every AE-capable bot consult before acting, and a CC stance on the enchanter/mentalist/sorcerer that re-mezzes and calls adds.
- **PBAE groups** (the community request) need a group "composition stance" in which the tank pulls to a corner and casters use Burn AE, the EQ stance name. This is an argument for letting the player choose specs or stances per bot.
- **Wipe recovery.** Copy the clear reset rules of FFXIV (reset to checkpoint) and WoW (followers return after release): bots release, regroup at a bind point and rebuff before resuming. Don't leave a half-dead group trickling back.
- **Mana.** An "Efficient / Balanced / Reactive" healer stance plus group-level "med break at X% mana" logic is the established pattern.
- The L4D teleport-when-unseen rule is a pragmatic fix for stuck bots. It is safe only when no human can observe it.

### Gaps
- No verified source for how GW1 hero AI handled interrupts and healing priority. Recall, unverified: hero interrupts were notoriously fast, near-instant, which players called superhuman. That is the opposite of WoW's problem.
- No verified source on CC-aware AI in any shipped commercial companion system. This may be a genuine gap in the industry rather than a research gap.
- No verified RvR/PvP companion-AI sources.

## 3. How do companions communicate in combat and outside it, and how does that signal competence?

### Takeaway
The best-documented approach is Valve's **rule/criteria dialogue system**. Characters match the current world facts against thousands of tagged lines and pick the most specific match, so barks are situational ("zombie behind you") rather than generic. Dragon's Dogma shows both sides: chatter that gives real tactical knowledge reads as competence, and repetition is the most-cited annoyance. Capcom made "stop repeating lines" an explicit DD2 goal.

### Cited Findings
- **Valve dynamic dialogue (GDC 2012, Elan Ruskin).** The system matches hundreds of world facts against thousands of possible lines using fuzzy pattern matching. Characters remember history, fall back from special cases to general ones, and pick the best dialog, behaviour or animation for the situation. Writers add special cases without programmer changes. It was used in Left 4 Dead, with characters warning about a zombie behind a teammate as the example. — [GDC Vault, 2012](https://gdcvault.com/play/1015528/AI-driven-Dynamic-Dialog-through)
- **L4D event model.** OnSight, OnLostSight, OnSound and OnInjured events flow through the behaviour system. These are the same hooks the dialogue rules key off. — [Valve 2009](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
- **Dragon's Dogma 2 (2024).** Pawns are talkative by design. Itsuno wanted them to act like "a friend offering guidance", for example suggesting a route or a tactic. Pawns "might have knowledge of certain enemies". The team did "whatever we can to keep Pawns from repeating the same lines multiple times." — [GamingBolt, 2024](https://gamingbolt.com/dragons-dogma-2s-pawns-explained-in-new-developer-interview); repetition coverage also in [GamesRadar](https://www.gamesradar.com/dragons-dogma-2-devs-have-done-whatever-we-can-to-make-pawns-stop-repeating-themselves-over-and-over)
- **WoW followers.** With Dungeon Assistance on, the NPCs **give mechanic instructions**, acting like a tutorial group. Explanatory callouts are part of the design. — [PC Gamer, 2024](https://www.pcgamer.com/world-of-warcraft-wow-follower-dungeons/)
- **FFXIV Trust.** The AI was hand-programmed to express each character's personality. — [GameSpot, 2019](https://www.gamespot.com/articles/ffxiv-shadowbringers-ai-companions-matter-more-tha/1100-6468377/)

### Inferences
- A Living Server could implement a **Ruskin-style rule table** offline and with no LLM, as a JSON or SQLite table of lines. Each line carries criteria such as event, role, target type, the speaker's HP/mana, whether the mob is mezzed, realm and zone, plus a "last said" cooldown and a memory flag.
- Callouts that state **intent and state** signal competence best, because they show the bot is tracking the right facts: "Mezzing the caster", "Mez broke on the add!", "OOM, need 30s", "Pulling the next pack", "Incoming: 3 Albs from the west", "Healer down, retreat".
- Pawn-style **knowledge chatter** ("these undead are weak to crush", "this camp has a roamer") lends itself to bots that have camped a zone before. Bot knowledge per zone and mob, stored in the database, could drive both behaviour and lines. That's a cheap analogue of pawn knowledge sharing.
- Repetition control (per-line and per-concept cooldowns, variant pools) is mandatory. Capcom treated it as a top fix.
- An LLM chat layer would be a separate, optional add-on. A table-driven bark system covers the competence-signalling need offline.

### Gaps
- Dragon's Dogma's actual knowledge-transfer mechanic: pawns gain enemy/quest/area knowledge, return from other players' worlds with it, and the knowledge changes their chatter and tactics. Not verified here. The DD fandom wiki was blocked and the 2012 Capcom post didn't cover it. Recall, unverified: knowledge is tracked per enemy type in stars and goes up through fights both in your world and in other players' worlds.
- No verified GW1, Mass Effect or BG3 bark-system sources.

## 4. What makes AI teammates feel "dumb" versus "smart"?

### Takeaway
Developers consistently say believability comes from **fairness** (simulated senses, reaction times, imperfect knowledge), **predictable, reliable decisions**, **staying with the team**, and **never doing the unforgivable thing** (friendly fire, wasted resources, wandering off). Players read as dumb: unreliable interrupts, repetitive chatter, wandering into trouble, and bots that are either superhumanly perfect or hopeless.

### Cited Findings
- **Valve 2009.**
  - Believability depends on bots seeming fair. That means imperfect knowledge through simulated senses, simulated aiming and reaction times.
  - Decision making should be "reliable and predictable".
  - Bots were built to reproduce the decisions players make in real gameplay.
  - Vision covers line of sight, field of view, "can I see X" queries and the set of entities the bot recognises.
  - Bots are not allowed undesirable actions (no friendly fire, no Molotovs).
  - Reactive path following, with steering toward a look-ahead point plus local avoidance, looks fluid. Fully optimised paths are "costly and look robotic".

  — [Valve 2009](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
- **FFXII.** An autonomous member could wander off and trigger a cutscene, an example of autonomy producing "dumb" side effects. — [Game Developer, 2017](https://www.gamedeveloper.com/design/why-i-final-fantasy-iv-i-was-key-to-i-ffxii-i-s-ai-driven-gambit-system)
- **WoW followers.** Interrupts were unreliable, which hurt players on long-cooldown interrupt classes. — [PC Gamer, 2024](https://www.pcgamer.com/world-of-warcraft-wow-follower-dungeons/)
- **FFXIV Trust.** The NPCs were deliberately made less efficient than skilled humans, a choice to tune competence downward. — [GameSpot, 2019](https://www.gamespot.com/articles/ffxiv-shadowbringers-ai-companions-matter-more-tha/1100-6468377/)
- **Dragon's Dogma 2.** Repeated lines were a known complaint about DD1 pawns that the developers explicitly worked on. — [GamesRadar, 2024](https://www.gamesradar.com/dragons-dogma-2-devs-have-done-whatever-we-can-to-make-pawns-stop-repeating-themselves-over-and-over)
- **EQ mercenaries.** A "confidence" stat makes mercs refuse to act or flee, which reads as either caution or cowardice. — [Almar's Guides](https://www.almarsguides.com/EQ/General/Mercenaries.cfm)

### Inferences
- "Smart" in a DAoC context means:
  - never breaking mez;
  - never pulling adds;
  - the healer never going OOM silently;
  - everyone hitting the MA's target;
  - group movement that looks fluid rather than in single file.
- "Dumb" means: superhuman detection, everyone on the identical path, and pet piles. Fairness rules (perception limits, reaction delays) matter as much as raw competence. A bot that is perfect but cheats feels as bad as one that is clumsy.

### Gaps
- Player-sentiment surveys for GW1 heroes (often praised as the best MMO companion system; unverified) and the Mass Effect squad AI were not gathered.

## 5. Which mechanics would directly answer the Living Server community complaints?

### Takeaway
Each complaint maps to a documented mechanism. RvR detection from far away → L4D simulated senses (FOV, line of sight, range, reaction time, stealth checks). Too many clumped pet classes → WoW's de-duplication of class picks plus composition templates. Bots following the same path → L4D reactive path following with look-ahead points and per-bot offsets. No way to customise group bot specs → EQ stances, PoE2 presets plus editor, DA:O/FFXII rule lists, and (unverified) GW1 hero skill bars.

### Cited Findings
- **Far-away detection.** Valve modelled perception via a Vision component with FOV and LOS, simulated senses, reaction times and imperfect knowledge, explicitly for fairness. — [Valve 2009](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
- **Same path.** Valve used reactive path following (steer toward a look-ahead point plus local obstacle avoidance), noting that optimised or rigid paths "look robotic". — [Valve 2009](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
- **Composition and duplicate classes.** WoW swaps follower DPS NPCs so they don't duplicate the player's class (a hunter player gets an enhancement shaman instead of the hunter NPC). — [PC Gamer, 2024](https://www.pcgamer.com/world-of-warcraft-wow-follower-dungeons/). The L4D AI Director varies spawns, e.g. the same boss can't repeat back to back. — [Valve 2009](https://cdn.fastly.steamstatic.com/apps/valve/2009/ai_systems_of_l4d_mike_booth.pdf)
- **Spec and role customisation.** EQ stances per archetype, including Burn AE for casters. — [Almar's Guides](https://www.almarsguides.com/EQ/General/Mercenaries.cfm). PoE2 presets and behaviour editor. — [Gamepressure](https://www.gamepressure.com/pillars-of-eternity-2/partys-ai/z1ae65). DA:O condition→action tactics. — [Dragon Age Wiki](https://dragonage.fandom.com/wiki/Combat_Tactics)

### Inferences
- **RvR detection.** Give gamebots a perception model:
  - aggro and detection radius scaled by DAoC stealth and detect skills, level difference and facing;
  - LOS checks;
  - a reaction delay (e.g. 0.5–1.5 s) before acquiring a newly seen enemy;
  - a "last known position" memory instead of omniscient tracking.

  This is a server-side code change, with no navmesh rebuild needed, which is consistent with project rules.
- **Pet-class clumping.** Two parts:
  - Composition templates for the group generator: limit pet classes per group (e.g. at most 2 of necro/cabalist/theurgist/animist/bonedancer/enchanter-with-pet), and require 1 tank, 1–2 healers and 1 CC.
  - Keep the Director-like "no back-to-back repeats" rule for roaming warbands.
- **Same path.** Add per-bot lateral offsets (formation slots), randomised look-ahead points and occasional alternative waypoint choices. Inter-group route variety could come from weighting route choice by recent use. It's worth checking whether autonomous groups currently share an exact waypoint list.
- **Group spec customisation.** In order of cost:
  1. Per-bot role and stance dropdowns: Tank / Assist; Healer Efficient / Reactive; Caster Single / Burn AE / CC.
  2. A group "doctrine" preset (Standard, PBAE, Caster-heavy, Melee train).
  3. A per-bot spec selection from the class's spec lines, analogous to GW1 hero skill-bar and attribute editing (unverified source).
  4. An advanced rule list in JSON, like DA:O/FFXII.
- **Debuggability.** Adopt L4D-style reason strings on bot state transitions so that reports like "bot ran off" can be traced.

### Gaps
- No sources on RvR-specific bot perception in other MMOs (e.g. how WoW battleground bots or GW1 PvP AI handled detection).
- No verified source on the GW1 hero skill-bar or attribute customisation. It is the strongest analogue for "customise group bot specs" and should be confirmed.
- Pet-clumping data from other games (e.g. EQ magician/necro mercenary limits) was not found. There is no evidence the industry has a canonical solution beyond composition rules.
