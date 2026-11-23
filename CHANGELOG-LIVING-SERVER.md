# Living Server changelog

What Living Server adds to Offline DAoC. The base game's own changes (up to 0.37, the releases made by Offline DAoC's
creator, shadowofze) are in [CHANGELOG.md](CHANGELOG.md). The newest version is first.

Up to 0.37-se.2 the fork was called the **Social Expansion**, and "Living Server" was the name of one
of its feature groups (bots remember you, grats, rezzes, alarms, nemeses, schedules, breaks, body
language). From Beta 1.0 the whole add-on is the **Living Server** and that group is **Server life**.
Older entries below keep the names they were written with.

## 1.2.2 (in development)

The raid release (owner, 2026-11-22: 1.2.2 is the raids only; Uthgard's drop tables and the PC-check fixes come in
1.2.3). **Nothing here is seen in game yet: tests only.** The three raid-size raises first made here were withdrawn on
audit (owner, 2026-11-23, "Revert all three"): every raid keeps its period size. Player notes: [docs/RELEASE-NOTES-1.2.2.md](docs/RELEASE-NOTES-1.2.2.md).

**Bosses**
- **Raid sizes: the 1.2.2 raises withdrawn; every raid at its period size** (owner, 2026-11-23: "Revert all three").
  Summoner's Hall back to 64 (from 80, all three realms), Galladoria back to 100 (from 112), Tuscaran Glacier back to
  100 (from 120); the Green Knight's raise to 80 had been withdrawn on 2026-11-22. An independent audit of the sims
  behind them found none sound: Roesia's model loses at 80 (0-15 of 64 with real stats), gets worse with more curers and
  turns on a hand-fitted heal, and its control wins at the 100 a raid starts at; Spindler's is a newborn count with no
  win or loss, its target is the model's own value, and `1dc66b6` killed her at the period size with the old share;
  Kula's model cannot lose, its "20.1 min" target is a model estimate, and the losses were at King Tuscar (about 124,
  unmodelled). The tactic changes (the 60% add-party share at Spindler, the pet and area-spell guard at the King, the
  fresh-Pain cure) stay. `RaidPeriodSizes.Raises` is empty; `/epictest start <raid> source` is gone with it.
  [docs/RAID-SIZES.md](docs/RAID-SIZES.md), "Withdrawn on audit". **Tests only** (#408, #409, #410).
- **The rule for raising a raid's size stays, with stricter evidence** (owner, 2026-11-22: "If the mechanics and
  tactics are working but the period raider size from logs isn't cutting it, and adding more bots without it becoming a
  Zerg wins, we should bump the suggested raider and period size"; tightened 2026-11-23). A period size goes up only
  when the encounter's sim shows its tactic working but still losing at the period size, to the smallest whole-party
  size winning about two in three, at most twice the period guide's own figure and never past 300. Its evidence now
  needs: the game's loss condition (half the engaged raiders fallen, or a reset from 70% or lower) with real raider
  deaths; at least one PC kill and one PC loss reproduced at their logged sizes; the no-zerg control losing at the real
  start (125%) and with the 24 Special Forces, not only at 100%; a shared raid row split before one boss's evidence
  raises a dungeon; and the evidence pinned in a C# test (`RaidPeriodSizes.Raise.EvidenceTest`, checked by
  `UT_RaidPeriodSizes`). No boss is touched, and no flood guard moves. [docs/RAID-SIZES.md](docs/RAID-SIZES.md),
  "Raising a period size". **Tests only.**
- **The Green Knight: back to 40; tag and burn reworked so every raider fights (#412; tests only; not yet won at 40).**
  The owner withdrew his raise to 80 (2026-11-22, "option 1"). The first 1.2.2 tactic sent the casters and archers
  without an area spell 4,600 out, where nothing they cast reached him, and its model left his walks out (owner and
  audits, 2026-11-23). Now every raider fights from where it reaches (`EpicEncounterTactics.GreenKnight.cs`):
  - healers 650 from him away from his tank, in the melee's peel reach and clear of Might of the Forest; the four
    nearest his target heal it first; a raider a tree hits waits for a heal until under 35% ("gk-heal-spot");
  - area casters 700 out with one melee guard, burning tagged trees ("gk-burn"); casters and archers without an area
    spell 1,300 out, shooting the tagged trees they won't pull, else him ("gk-shot"); Theurgists 850 out, their
    elementals on the trees hitting healers and casters ("gk-summon");
  - owners of lasting pets 4,600 out, where no tree grows at them, their pets on the trees, else him (a new `PetOn`
    stand-off keeps the pet fighting; "gk-pet");
  - the melee tag every tree, healers' first, and go back to him; Paladins, Friars and Wardens fight in melee;
  - spots are laid out once along the line away from his tank, kept while he walks or fights a healer he picked,
    snapped to the navmesh and checked for line of sight; once a raider is at its spot it gets no new spot;
  - while he walks to one of his four points the raid holds its ground and keeps on the trees: he comes back to his tank.
  Model (`UT_RaidSim122GreenKnight`, his walks, the bots' own buffs, loss = no raider left): wins of 32 at 40 / 48 / 56 /
  80, buffed 0 / 5 / 26 / 32, unbuffed 0 / 0 / 1 / 28; the old rules 0 of 32 at every size to 104. Every raider
  contributes. **Tests only.**
- **Real boss stats in the raid sims** (`UT_RaidRealStats122`; docs/RAID-SIZES.md, "Real stats"). The sims' assumed hits
  are replaced by the public OpenDAoC database's templates, the scripts and the server's damage formulas (templates'
  STR / QUI / INT are auto-set to 30 + (level - 1) unless a script sets them; epic bosses deal 2.25 times). The Green
  Knight's trees hit 22-36 a second on mail and plate and 53-78 on cloth (assumed 5-15); Roesia's melee 200-289 a second
  and her damage shield 224-280 a swing (assumed 200 and 64); Marfoirwort swings every 2.71 s (assumed 3.0-4.5).
  Roesia with real stats: 0-15 of 64 at 80, 43-64 from 88-112 (one reason the raise to 80 was withdrawn, above); the
  Green Knight back to 40 (above); Caer Sidi, Trollheim, the dragons, Darkness Falls (in Legion's model, where he deals
  no damage and the raid cannot wipe, still killed inside 20 minutes from 48 at the real 6.2 a raider-second) and
  Marfoirwort (won at 40 at his real swing in a model with no raider deaths) kept. Behemoth (#414): every swing kills one, 23 a
  minute; a raid would need about 440 bots in his hall at once, past the realm's 300: no bot raid. **Tests only.**
- **Kept:** Caer Sidi 100 (the Crypt Lord's guarded pull holds at every size; size is not the lever), Trollheim 64
  (the 1.2.1 stall clock restarts after a wipe; the sim's "no time-out at any size" is true by its construction, so
  whether a retry fits inside the hour is untested), the three dragons 70, Darkness Falls 64 and Marfoirwort 40: no sim
  gives grounds for a change. **Tests only** (#407, #411, #413, #415, #412).
- **Records carry over in bots.** A raid's Herald record now says what its size is a percentage of ("Raid size: 125% of
  64."), and the next raid reads it against the period size today, so a world that ran a 1.2.2 test build at a
  withdrawn size carries on with the same number of bots: "125% of 80" at Summoner's Hall (100 bots) reads as 156.25% of
  64. **Tests only.**
  80."), and a 1.2.1 record at a raised raid is read against the old size: a Summoner's Hall raid grown to 166% of 64
  (106 bots) carries on at 132.8% of 80, the same 106. **Tests only.**
- **A raise needs the roster.** The full size is half the realm's free level 50s, up to the period size: Galladoria's
  112 takes 224 free level 50s, Tuscaran Glacier's 120 takes 240, Summoner's Hall's 80 takes 160.
- **GM:** `/epictest start <raid> source` runs a raised raid at its size before the raise; `era` runs the new size.
- **Raid tactic audit fixes** (everything but the Green Knight; issues #407-#413, #416; **tests only**):
  - **Marfoirwort:** he stands on his target (his follow distance, 131), so no spot in bot melee reach clears his
    350 Life-leech; the 1.2.1 rule moved every melee raider every turn and they never swung. Now only his target fights
    him in melee; the other melee shoot him from clear of the leech with their distance weapon or wait clear of it (no
    new spot once there), and tanks go in only to take him back from a non-tank. Animist shrooms are planted clear of
    the leech and fight him; other pets stay off. Sim: each raider counts only while in range; 16 of 16 kills, the
    1.2.1 rule 0 of 16.
  - **Paladin, Friar, Warden:** a melee-hybrid rule (`BotPartyRoles.IsMeleeHybridHealer`,
    `EpicEncounterTactics.FightsFromRange`, `RuleContext.StandsBack`) for the new rules of unwon fights (Marfoirwort,
    King Tuscar and Kula's spreads, Legion's ranged line, the Sacristan ambush, the Glacier Giant's healers). Won fights
    keep v1.2's test (the dragons' stun and breath step-out, the catalog's spread and melee-behind, blast roles).
  - **Legion:** a wounded raider steps out under 35% and comes back only at 60% (no 34-35% shuttle).
  - **Summoner Roesia:** with its own party in danger a curer cures only its own party's fresh members, never another
    party's. The 1.2.1 Pain rules (25% dotted danger, fresh override, 2.5 s) apply in Summoner's Hall only; every other
    raid has v1.2's cure rule again.
  - **King Tuscar:** his target must hit him to hold him (heals on it give its healers threat), so a tank uses its taunts.
  - **Keep lords:** the battleground storm's lord reach is the frontier's (350 keep, 250 tower) for casters, archers and
    healers; melee and tanks keep 1.2's 900 and keep the lord when no walk to him is found.
  - **Crypt Lord:** the lapsed court guard covers the 35 s court fight and never lapses: after the hold it waits for
    his next open window (`REALM_EXPEDITION_PULL_WAIT_GUARD_HOLDS`), never a blind pull.
  - **Spindler:** archers with a short bow, crossbow or thrown weapon take a post on the 750 ring, in reach of her.
  - **Dragons:** the dragon return wave no longer runs for open-world raids (it measured them to the realm's dragon lair).

**Chat**
- **The /faction answer audit** (owner, 2026-11-22: "smooth clunkiness, errors, or disjointed phrasing", "reduce or
  eliminate general fluff and non answers"). A bench of 453 player questions (`tools/bot-voices/eval/player_questions.json`)
  goes through the reply path in CI (`UT_ChatQuestionBench`) and is scored by `score_bench.py`: 60.5% good answers, 16%
  silence, 15% off-topic, 6% "dunno", 1.5% wrong, 9.5% clunky. The root causes (lore answering level questions before
  the world facts, the where-to-level phrasings missed, the knowledge holes, the "dunno" rescue) and the ranked fixes are
  in [docs/bot-voices/CHAT-AUDIT-1.2.2.md](docs/bot-voices/CHAT-AUDIT-1.2.2.md). No reply changes yet; the bench is
  report-only until the fixes land. **Tests only.**
- **/faction answers, the routing fixes (F0, F1, F4, F6, F7; #452, PR #456).** The reply path is now one set of pure functions,
  `AutonomousChatReplyPath` and `AutonomousChatKnowledge.TryAnswer(KnowledgePorts)`. The server and the question bench both
  call them, so the bench runs the server's own routing instead of a copy. The world's facts (a place's levels, Darkness
  Falls, relics, keeps, the realm's dragon) now come before the lore, and lore never answers a level or ownership question.
  /faction no longer gets a "dunno" rescue or the "not sure" set: silence instead, while a bot named or sent a tell may still
  say it doesn't know. A question about grouping ("what does lfg mean?", "looking for a spot to grind") is answered as a
  question, not with "nobody around your level". "Epic" counts as a raid word only for an epic fight. A named bot asked about
  itself answers from its own facts, and no epic answer says "did mine". Bench results: good answers rose from 274 to 314 of
  453 (60.5% to 69.3%). Non-answers fell from 27 to 3, off-topic from 67 to 25 and clunky from 43 to 21; place-level
  questions are now 48 of 48. Silence where an answer was due rose from 67 to 92, since the dunnos became silence. CI now
  gates on `score_bench.py ratchet` (`bench_ratchet.json`: the good share may only rise, no bad category may grow). It also
  reports a 60-question holdout set written after these fixes (40 of 60 good). **Tests only.**
- **Chat shapes (the audit's F3, F5, F7 first half, F11):** a where-to-level question is read as a pattern (`lvl`, `xp`,
  `best xp for 21`, `where do 24s hunt`, `im 36, where should i go now`, a realm or a class in it) ahead of class talk, and
  answered with a zone of the realm for the level from `places.json` when no bot camps there (`CatalogZone`, `HuntText`; a
  solo question gets no group dungeon). Trainer answers say "your" only for the asker's own class, no longer "he trains ur
  spells", and another realm's class as such ("thats a hib class"); "mercenaries", "armsmen", "heroes" are read as classes.
  A known class's spec and weapon advice is its own lines only (no "whatever ur spec is"). "what are you up to?" said to a
  bot is answered from its hunt, "got any pots?" from its backpack, and a short known fact (eight words or fewer) as the
  first answer to a question types at 1.5x the bot's speed. Trainer answers name the capital as written ("Camelot"), and a
  dungeon whose monsters are far from the level is left out of a where-to-level answer (`DungeonFits`). Bench on top of the
  routing fixes: good answers from 314 to 359 of 453 (69.3% to 79.2%), wrong 9 to 0, off-topic 25 to 13, silence where an
  answer was due 92 to 70, clunky 21 to 16; where to level 45 of 45, trainers 29 of 29; the holdout from 40 to 49 of 60.
  `bench_ratchet.json` tightened to these counts. `UT_ChatShapes122`. **Tests only.**
- **Chat polish: replies stitched together read as one line** (fixes F8-F10 of the audit). A lore line that opens with
  its own question ("who is eraliwyr? ...") answers only that question, and without its opening (`LoreChat.FitAnswer`).
  No laugh is tacked onto a "dunno", a warning, a death, a raid line or a lore line (`AutonomousBotVoiceCorpus.IsSerious`,
  kind `lore`). A tidy writer capitalizes the world's names ("The armsman trainer in Camelot", `AutonomousChatNames`, ported
  to `voice_style.py`). 99 fluff and non-answer reply lines were deleted, 38 rewritten and 26 written to carry a fact (`{zone}`,
  `{level}`, `{class}`) or a real pointer (reply lines 1,239 to 1,166; fluff 65 to 0; non-answers 73 to 5, the short honest
  "not sure" set kept for tells). A property test (`UT_ChatStitching`) runs every
  stitched fragment through every style and the scorer's CLUNKY checks (`ChatClunky`, kept equal to `chat_scorers.py` by
  `clunky_cases.json`). Bench on top of the routing and shapes fixes: good answers 359 to 361 of 453,
  clunky 16 to 0, padded 8 to 4, non-answers 1 to 0, fluff 2 to 1, silence unchanged at 70; the holdout 49 to 50 of 60,
  clunky 3 to 0. `bench_ratchet.json` tightened to these counts. **Tests only.**
- **The knowledge holes (audit F2, C8):** bots now answer where the realm sells potions (nobody does: alchemists make
  them), dyes, poison, spellcraft gems, crafting tools and materials, bags (there are none: the bank), where to sell loot
  and where the Realm Exchange is; who sells horse tickets; how to get into Darkness Falls (each realm's home entrance and
  who holds it), the frontier (the porter to the border keep and its medallion teleporter) and the battlegrounds (the
  porter's bracket for the asker's level); what armour a class wears (the item generator's rule, with the level the
  heavier kind starts) and its stats (the class script's); who teaches each craft in the capital, how crafting skill rises
  (recipe colours), what alchemy needs, how spellcraft works; NPC factions as the server keeps them (kill their enemies,
  the examine line, the realm's emissary); kill tasks (`/task` on a named guard, to 19); and "who sells X" from the world's
  merchant lists, with prices. New `AutonomousChatGuide`; names and prices from `tools/lore/npc_roster.json` and the public
  OpenDAoC database. `UT_ChatGuide`. Bench on top of the routing and shapes fixes: good answers 359 to 407 of 453, silence
  where an answer was due 70 to 22; `bench_ratchet.json` tightened. **Tests only.**

**RvR**
- **Keep and tower lords are fought in formation** (owner: "add the rule about standing behind melee"). Inside a lord's
  reach (1.2.1, #416: 350 in a keep, 250 in a tower) tanks and melee fight him at melee range; casters and archers stand
  behind them on the raid's side (toward his keep's or tower's door), 230-300 from him in a keep and 160-205 in a tower,
  on nine spots in two rows over a 120 / 90 degree arc; healers behind the casters, 305-345 / 210-245, still inside his
  reach. Each spot is snapped to the navmesh on his floor with a straight floor line to him (narrower, then nearer the
  centre line when the room refuses it; none: the old behaviour). A lone tank on a lord now taunts as a grouped one
  does. Frontier sieges and battleground storms; autonomous gamebots only (companions keep their own paths). Lord radii
  and stats unchanged. `RvrLordFormation`, log `RVR_LORD_FORMATION role= dist=`. **Tests only** (#416).

**Launcher, GM tools and tests**
- **The launcher shows the Living Server version.** The label under the title reads "LIVING SERVER 1.2.2" and the window
  title "Offline DAoC Living Server 1.2.2", both from `ForkBuildInfo.BuildName` (stamped into the launcher, the name the
  server logs as `FORK_BUILD`), so they can't drift from the build. Up to 1.2.1 they said "VERSION 0.37" (0.37b, 0.37c):
  the 0.3x line is retired (AGENTS.md, "One edition from now on"). The update strip's separate build label is gone.
  `ForkBuildInfo.BaseRelease` (0.37) still names the game the build installs on; `check_versions.py` now fails a
  pinned `DisplayVersion`. **Tests only.**
- **GM `/jump` and dead or travelling bots (#438 follow-up).** A dead bot is refused before anything about it changes,
  and the GM is told why (its body stays where it fell, `GameBot.RefuseCorpseMove`; 1.2.1 stripped its state, hit the
  refusal with a stack-trace warning and said only "could not be moved there"); the log has `GM_JUMP_BOT_REFUSED
  reason=Dead`. A jumped gamebot forgets the trip it planned from the old spot (camp, patrol, town teleporter, frontier
  porter request, a stable ride it meant to take, its keep route) and plans again; its task and group stay.
  `GM_JUMP_BOT` now logs `region_change=`, `was_riding=` and `was_fighting=`. Other regions, stablemaster rides and
  fights were already right (the move leaves the world, which takes the bot off every monster's aggro list); bots
  never ride boats. The playtester's `bot-jump` scenario also jumps a dead helper. **Tests only.**
- **`UT_AiProfile.AScopeAllocatesNothing` no longer flakes.** It failed once in CI with 3,040 bytes (2026-11-22), a
  one-off runtime allocation on the test thread. Each of seven samples now re-enables the profiler, settles the GC and
  counts only if the profiler stayed on; the least must stay within 64 bytes (one allocation per scope would be 2.4 MB).
  The measured lock has its own test, with its gate made outside the measurement.

**Bots and companions**
- **Up Darkness Falls' entrance stairs (#438).** zone249.nav has each realm's stairs as three one-way drops, so no path
  leads up them (`tools/navmesh/dfprobe`). Leaving DF never needs them: every realm's exit (zone points 74, 70, 72) is on
  its hall floor, a complete path from the hall and from the arrival point. But a companion following its player up got
  a partial path: it stood at the foot, or, with the player near the arrival point, walked 103 (Albion) to 235 (Midgard)
  nodes into another level, within the 3,000-unit recall range. A gamebot in a player's group stood (DF refuses it partial
  paths). Now a bot whose path toward a goal at or above a drop's lip is partial walks to that drop's landing and climbs
  the certified fall backwards (rise over the landing to the fall's air point, step onto the lip), one drop at a time,
  only where the navmesh agrees (the landing reachable, a complete path from the top of the stairs to the goal) and with
  the falls' hash-guarded links (companions: the installed mesh; gamebots: the full certificate). New orders during a
  climb only change where it goes after the lip. Server log `DF_STAIR_CLIMB`. No navmesh change. **Tests only**
  (`UT_DarknessFallsStairClimb`; dfprobe `climb` on all nine drops).
- **Ladders, climbs and ledge drops everywhere (#438 generalized).** A scan of all 103 shipped navmeshes found 1,262
  off-mesh link directions in 39 zones whose middle is on no polygon, the Darkness Falls stair stall: frontier towers,
  Fomor, Delling Crater, Caillte Garran, Trollheim, Tuscaran Glacier, Galladoria, Caer Sidi, Iarnvidiur's Lair and more.
  Outside Darkness Falls (unchanged) a bot on a steep link walks it to the end before it replots, and a bot left inside
  one is set down on its far end (`NAV_LINK_LANDING`). [docs/NAVMESH-DROPS.md](docs/NAVMESH-DROPS.md),
  `tools/navmesh/navscan.cpp`. **Tests only.**

**Routes**
- **The Lough Derg / Valley of Bri Leith seam loops (#421; 200:384960,486688 and 385088,483424).** On the installed
  meshes Lough Derg has a ridge along its Connacht border (seam ground 6081, 1,379 polygons) that joins no other Lough
  Derg ground: only the Valley's main ground (y 486,624-488,352), the Valley shelf (10136, y 483,424-486,496) and a
  Connacht ledge (69404). The zone itinerary's "reaches the destination's main ground" preference looked at each
  ground group's best crossing only; from the Valley toward Connacht both best picks were ridge crossings, so nothing
  was left out and the bot stepped onto the ridge, which sent it straight back (and the shelf did the same toward
  Mount Collory). Now no crossing, listed or sampled, onto ground that never reaches the destination's main ground is
  taken while the border lists one that does; the last border (whose walk to the goal is proved itself) and goals on
  other ground of their zone keep the old rule. `tools/navmesh/seam_route_sim.py` replays the itinerary on the 1.2
  meshes: 15 of 28 trips from the four seam spots cycled before, none after; 72 trips between nine places on the main
  ground of eight Hibernian zones: 62 arrived before, 72 after (the other 10 cycled, 6 of them at 384960,486688). No navmesh,
  seam file or road file changed. **Tests only** (`UT_LoughDergRidgeSeam421`).

**Regression guards (what was seen working on 1.2 stays as it was)**
- **Guards for 1.2-proven paths** (regression audit of `integration/1.2.2` at `0270e78c`, items 6-9). **Tests only.**
  - *Lifts ride as in 1.2:* a lift leg (`Pathfinder.IsVerticalLink`: 300 or more up or down, at least twice the run) is
    no longer a NavLinkLeg commit leg (`NavLinkLeg.IsLiftLeg`). Every stuck-prone link in Tur Suil (190), Iarnvidiur's
    Lair (161), Aldland (56, Coliath) and Cruachan Gorge (211) is a lift, so those zones' legs behave as in 1.2 except
    the stranded set-down. 546 of the table's 1,262 links are lifts; `navmesh-drops.json`'s `commit` and
    `summarize_drops.py` follow (`UT_NavLinkLegs`).
  - *Seam choice pinned (#421):* on the 1.2 download's region 200 seam lines
    (`tools/navmesh/seams-region200-v036b.json.gz`), the crossings of the Cruachan Gorge column (Druim Ligen to Dun nGed,
    seen single file on `aa537ce`) and of the walk to Druim Ligen from both sides of the 211/212 seam equal 1.2's
    (`seam_route_sim.py --rule old`; `--rule new` picks the same) (`UT_SeamChoiceSince12`).
  - *Death fall:* a dead bot's removal goes to the original client again when the bot really leaves (logout, removal
    from the world, a move to another region), so no corpse of a gone bot stays; a move within the region (a release
    nearby) is still held (`BotDeathAnimation.HoldsRemoval`, `GameBot.MovingWithinRegion`; `UT_BotDeathAnimation`).
  - *Packet queue:* an add from inside the drain on the draining thread gives up at once, as in 1.2, instead of
    spinning up to 1 s on the send step (`DrainArray.IsDrainingOnThisThread`; `UT_DrainArray`).
  - Reviewed, already pinned, no change: the pocket-escape loop (#420; first two escapes as before, `UT_PocketEscapeLoop`),
    the casting line-of-sight refusal (an accepted check waits for its answer as in 1.2, `UT_CastingLosCheckGap121`), the
    hired companions' `LevelRule` and the one-time raise of 1.2 hires (raised once, handed-over gear kept, `/hire train`
    numbers still parse: `UT_HiredCompanionJoinsAtPlayerLevel`, `UT_HireTrainCommand`). `bot_schedule_mode` off (the
    owner's call, 2026-11-21) keeps the whole roster online: a load note for the overnight stall row, not a code guard.

## 1.2.1 — 2026-11-22

Fixes and lore after the 1.2 release (2026-11-21). **Nothing here is seen in game yet: tests only** (owner, 2026-11-22:
1.2.1 ships with every change that passes CI, each one labelled; [plan](docs/1.2.1/RELEASE-PLAN-1.2.1.md)). Automated
results so far: CI on `fix/1.2.1` at `d56adeda` (9,258 server tests passed, 0 failed, 1 skipped), and the playtester's
`bot-death`, `bot-corpse-target` and `bot-rez` scenarios on `b644529a`, which check packets, not what the client draws.
The raid fixes (#407-#415) are added under **Bosses** as their branches merge. Player notes:
[docs/RELEASE-NOTES-1.2.1.md](docs/RELEASE-NOTES-1.2.1.md).

**Bots and companions**
- **Darkness Falls entrance stairs (#438).** Companions following you down a realm's entrance stairs no longer get
  stuck on a step. The navigation mesh has no walkable stairs there: three landings per realm, each ending in a sheer
  edge, joined by one-way drops of 128-191 units (Albion x 33508-34301, y ~27955; Midgard and Hibernia the same;
  `tools/navmesh/README.md`). A companion slid diagonally down each drop, and a new follow order in mid-drop replotted
  from a point on no polygon, failed, and left it there. Every bot now takes the drops as the certified falls autonomous
  gamebots already used (off the lip, then down), new orders during a fall only change where it goes after landing,
  and a bot left in a drop is set down on its landing (`DF_STAIR_LANDING`). No navmesh change. **Tests only.**
- **GM `/jump <bot> to me` (#438).** `/jump <name> to me`, `/jump <name> to <name>` and `/jump <name> to x y z
  [region]` now find bots by name (companions, `/spawn` and `/raid` helpers, hired companions, gamebots, buffbots):
  an exact name first, then the start of a name, your own and your group's bots first. Bots had no client, so the
  command answered "not in the game". A moved bot drops its fight, path and ride and plans again; a gamebot's watchdog
  counts the move as progress. Log line `GM_JUMP_BOT ... kind=`. **Tests only.**
- **Resurrecting a dead bot in the original client.** That client won't let you select a dead bot, so a rez answered
  "You must select a target for this spell!". A resurrection cast with nothing dead selected now goes to a dead bot of
  your realm in range: the one you named with `/target <name>`, otherwise one from your group, otherwise the nearest. A
  line says who it goes to. Companions, hired companions and gamebots alike. Their bodies still can't be clicked there.
- **Bots fall when they die in the original client.** A companion, hired companion or gamebot now falls and lies like a
  monster's corpse instead of standing at zero health. Raised, it gets up where it lay; released, it appears at its bind
  point with no second body left behind (the 2026-10-30 bug came from a corpse left at release, and nothing is sent at
  the death spot now). The new client already showed the fall and gets exactly what it got before: the server tells
  the clients apart by the build in their first packet (`CLIENT_HANDSHAKE ... client=original|new-or-unknown`).
  Setting `bot_death_animation` ("Bots fall when they die" in the launcher), on by default; off, every packet is as
  before. GM check: `/lstest deathanim [raise|release|off]` on a targeted bot lists each packet sent or held.
- **Hired companions join at your level** instead of level 5, with their spec points spent in their class's build
  (respec stays free) and, above level 5, plain merchant armour, a weapon and a shield where their class carries one.
  Companions hired on 1.2 catch up once, the next time they come to you: they keep their gear, bag, coin, realm points
  and trained lines, spend only their new points, and get basic gear only in empty slots. Their training weapon counts
  as empty and is swapped for a merchant weapon. Gear that can't be found is tried again at the next summon. Log lines:
  `HIRED_COMPANION_RAISED`, `HIRED_COMPANION_TRAINED`, `HIRED_COMPANION_GEAR_RETRIED`.
- **Training hired companions (#397).** `/hire train` reads line names: `/hire train 1 smite`, `/hire train 1 mind
  twisting`, or the start of a name, with an optional level to train to (`/hire train 1 smite 10`). Line numbers still
  work, and an unclear line lists the companion's lines and what to type. The trainer window shows the next skill to
  train at the top of each line, so it no longer slides below the window.
- **Companion names.** The names the hire window suggests no longer include the lore's own heroes: Gareth, Isolde,
  Diarmuid and Deirdre gave way to Dinadan, Lynette, Loegaire and Fand (owner: lesser legend names are fine, the lore's
  are not). Companions already hired keep their names.

- **No pet cap for players** (owner 2026-11-21). An Animist can place as many turrets as power and casting allow (the
  cap of 12 per player and 10 in one spot no longer applies to players), and a Theurgist's pets have no cap either.
  Companions and gamebots keep their limits.

- **Bot parties take one more on the way (#423).** A bot party that has set off with a seat free looks for the role it
  lacks (healer, else tank, else damage) two minutes after leaving and then every ten minutes, for five minutes each
  time: a fitting free gamebot, or you by /lfg or a tell. Its "lfm" in /lfg is true again (`BOT_LED_RECRUIT ...
  kind=open_seat`). Raid, RvR and regrouping parties never do this.
- A click on a bot is logged (`PLAYER_CLICK_TARGET ... result=`), so a click that doesn't select one can be traced
  (#424; no server-side cause found, no gameplay change).

**Rewards and loot**
- **Enemy gamebot kills give experience (#396).** They only ever paid realm points. A kill now pays what a player kill
  pays: experience for a player of its level (shared by damage, capped at 125% of your own worth, with the keep bonus)
  and, outside battlegrounds, bounty points. A bot killed again within five minutes is still worth nothing.
- **Named drops in gamebot-led groups.** A kill you finished in a group a gamebot leads dropped none of the monster's
  template loot (coin and random items still dropped). It drops again.
- **Uthgard's drop tables** (owner 2026-11-21). A new tool, `source/tools/uthgard_drops_content.py`, adds the classic-era
  drops measured on Uthgard (hides, teeth, gems, vendor loot, period gear) to monsters that have no loot of their own,
  at Uthgard's rates (whole percents; rarer than 0.5% rounds up to 1%). Monsters with their own drops keep them, and
  random magic items and coin drop as before. `build_release_037.py --uthgard-drops <pipeline data>` puts them in the
  release world, and a world update made from it (`make_patch.py`) carries them to existing worlds. The pipeline data is
  not in Git, so this runs on the owner's PC ([docs/UTHGARD-DROPS.md](docs/UTHGARD-DROPS.md)); nothing has run on the
  real world yet. **Not in 1.2.1's world:** the 1.2.1 package changes the server and launcher only, and Setup installs
  the unchanged 0.37 world, so the drops ship with the first release whose world is built with them.
- Loot rows that name a missing item are one `LOOT_ITEMS_MISSING` warning per loot source with a count; the rows are at
  debug level.

**Crafting (#422)**
- **A piece you ordered from a crafter bot is handed over.** The hand-over ran only at the very edges of the crafter's
  break, where the code saw no break, so a made commission was never handed over or announced and lapsed after six
  hours. Now, while the break lasts, the crafter opens the trade if you are near or tells you once where to collect it
  (`ECON_PLAYER_ORDER outcome="ready_told"`, `"handover_tried"`).
- A stock trade window that can't open makes the crafter say why and keeps the deal (`outcome="stock_trade_failed"
  why=`); a bot never takes a trade hold for a player who is already trading.
- `ECON_INDEX` gains `craftedListed20`, the crafted bot listings of level 20 and up, to show whether sellable work
  reaches the Exchange.

**Bosses**
- **Elro the Ancient's third treant.** His script asked for health under a quarter and over three-tenths at once, so
  the third treant never came. It now comes between three-tenths and a quarter of his strength left, and his fight
  advice tells all three windows.
- **Raid simulations for Summoner's Hall, Marfoirwort and the Green Knight (#410, #412; tests only).** Models of the
  fights on the bosses' own numbers and the bots' own decisions (`UT_RaidSim121SummonersGreenKnight`); no boss changed.
  - **Marfoirwort:** his Life-leech heals him by the damage it deals to everyone within 350 of his target, and the melee
    and pets all stood there (about 25 a leech), so he stayed at 79% and above on `ccba364`. Now his target holds 185 from
    him, the other melee fight from behind him (370-380 from his target), everyone at range stands clear and no bot's pet
    attacks him. Model: before, 2 of 128 attempts killed him in 30 minutes; after, 128 of 128 in under 10 minutes. Log:
    `EPIC_LEECH_EXPOSURE ... inside=` (should be 0-1).
  - **Summoner Roesia:** a raider carrying Summoner Pain now holds the raid cure back only under 25% (was 50%), and a
    raid curer cures again after 2.5 s (was 5 s). Model, calibrated to `b1b6846`: with a group cure in every party both
    rules win; with single cures only, the old rule never wins and the new one still loses at 77 raiders but wins 5 of
    16 at 100 and 15 of 16 at 128 (the win balance grows the raid after a loss). `RAID_CURE_POISON` now prints `groups=`.
  - **The Green Knight:** the melee outside his target's party kill the tree nearest them (their own first) before going
    back to him. Model: the raid lasts longer and has fewer trees, but **still loses**; the next PC log (`EPIC_RANGED_ADDS
    boss="Green Knight" alive=`) is needed to see why.
  - **Grand Summoner Govannon:** not simulated; no bot raid has reached him since Roesia began winning every time.
  - **Round 2 (tests only).** No realm has a group cure poison (OpenDAoC database: every Cure Poison is single-target;
    Cleric, Friar, Healer, Shaman, Druid and Bard have it, Paladin and Warden don't), so the Roesia model now uses each
    realm's curers. A Summoner Pain with two thirds of its time left is now cured even while a member of the curer's
    party is in danger, and the own party's dotted member with the fewest health points goes first. Model, 16 attempts a
    size: wins at 93 / 106 / 128 raiders (the win balance's first, second and fourth steps up from its 80 start) go from
    6 / 9 / 15 to 13 / 16 / 16 in Midgard, 0 / 0 / 4 to 7 / 15 / 16 in Albion and 0 / 2 / 4 to 13 / 16 / 16 in Hibernia; 64
    and 80 still lose. `RAID_CURE_POISON` prints `pastDanger=`.
  - **The Green Knight, round 2 (tests only).** A tree that hits a caster or a healer silences it, and a shot pulls a
    tree onto the shooter. Now the casters, archers and healers stand together 750 from him away from his target, every
    melee raider but his target peels the trees off the area casters, the healers and the other casters (then kills its
    own), and only area casters hit trees. Model: the 1.2 rules and #432 never win; this wins at 80 raiders and more
    (past his 70-trees-a-wave flood guard) when a raider deals 80 or more a second to a tree and a tree hits for 8 or less,
    at 50 only in the kindest case. **Not fixed at his period size.** Log: `EPIC_GK_TREES alive= onHealers=` (every 30 s).
- **Dragon raids keep their thrown and resurrected raiders in the fight (#415, tests only).** Golestandt and Cuuldurach
  fell apart partway (lost after two wipes on `9a173f4`, `c81eb5d` and `43c43aa`) while Gjalpinulva kept winning. Four of
  Golestandt's five throw spots and three of Cuuldurach's land 7,300-12,200 from the lair, past the return wave's 6,700
  line (one of Gjalpinulva's does), and the return wave held such a raider at the staging ring as if it had released; a
  raider resurrected where it fell was held the same way. Both now stay with the raid (`REALM_RAID_RETURN_NOT_HELD`). No
  dragon is changed. A simulation on the real return-wave and wipe code (`UT_DragonThrowReturnWave415`) reproduces the
  pattern (Gjalpinulva wins most fights, the other two about half) and the fix lifts Golestandt and Cuuldurach to about
  seven in ten; not yet seen in game.
- **Legion's wounded step out where the healers reach them (#413, tests only).** A raider under 35% stepped straight away
  from him to 1,700, so on his far side it stood 2,500-2,900 from the healers' line, out of heal range; souls found it and
  each death healed him 2.5%. It now steps out toward the raid's side, about 500 behind the healers (`UT_LegionWounded413`).
  Legion is unchanged; not yet seen in game.
- **Tuscaran Glacier's twins (#409, tests only).** While Queen Kula lives every point dealt to King Tuscar heals her. No
  bot's pet now attacks him or defends its owner against him, and no bot but his own target hits him, area spells at
  Kula that would reach him included, as the period raids did ("kill all pets", "no AoE"). Once Kula is dead,
  casters and healers outside his target's party keep clear of the hammers that follow his styles, 500 around his
  target. King and Queen are unchanged.
- **Trollheim's stall clock after a wipe (#411, tests only).** The raid's hour without progress kept Nosdoden's low from
  before the wipe and did not count the retry's pull, so a retry that came late in the hour was ended as "Timed out".
  The retry's pull and, once the boss is back at full, its own low now count, once per wipe
  (`REALM_RAID_STALL_CLOCK why=retry|low_reset`). `REALM_RAID_STALLED` now says what the route was doing
  (`status=`, `living=`, `retries=`).
- **Caer Sidi, the Crypt Lord's court (#407; tests only, a simulation).** When the raid's wait for a safe moment to pull
  one of his court ran out (20 minutes, unchanged), it pulled at once, wherever he walked: on build `b1b6846` that brought
  him and his whole court in and ended the raid. After the 20 minutes the pull now still waits, at most 20 minutes more,
  for a moment when he stays 950 from that court monster while the raid walks to it and pulls it, and 550 from the walk
  itself. In the simulation an immediate pull brought him in 21-68% of the time and a guarded one never did.
  `REALM_EXPEDITION_PULL_WAIT_GUARDED`, `..._GUARD_CLEAR`, `..._GUARD_EXPIRED`. He and his court are unchanged.
- **Galladoria, Spindler Broodmother's newborns (#408; tests only, a simulation).** The parties sent against her newborns
  could be at most 45% of the raid: a 100-bot raid (13 parties) sent 5 and killed about 62% of what she spawned, so newborns
  piled up. They may now be 60%: 7 parties (88%, as in the 128-bot raid that killed her in 4 minutes on `ccba364`), and 8
  from 14 parties. She is unchanged.

**RvR and travel**
- Albion bots whose route out of Castle Sauvage failed restart from the army's muster at the border keep.
- Gamebots that logged out beside another realm's frontier keep log in at their capital, as players do
  (`AUTONOMOUS_POSITION_RECOVERY reason="saved beside the enemy keep ..."`).
- A keep defence with no defenders left stands down (`RVR_DEFENSE_MUSTER_EMPTY`) instead of marching empty.
- A GM's `/jump` into an enemy keep's area is no longer sent to the bind point.
- **Battleground keep lords take damage from the storm (#416).** A keep lord takes no damage from anyone more than 400
  away, but stormers struck him from up to 900, so casters, archers and healers in his room did nothing. They now
  strike him only from within 350 and walk in otherwise (`BG_RALLY_STATUS inLordReach=`). Keep, guard and lord
  levels are unchanged.
- **Frontier sieges too.** Frontier attackers picked an exposed keep or tower lord from up to 5,000 away and stood
  there, and an ungrouped caster stepped back from a rooted lord out of his reach. Attackers now walk to within 350 of
  a keep lord or 250 of a tower lord before striking, and never step back from a lord (`RVR_LORD_STEP_IN`).
- **Roaming eight-man groups find fights (#417).** Their patrols now favour the parts of the frontier where enemy RvR
  bots are, and an eight follows the planner's hunt of an enemy group within 10,000 in its own region instead of
  patrolling past it (`RVR_ROAM_CHASE`, `RVR_ROAM_CIRCUIT enemiesNear=`).
- **Frontier porters (#418).** A gamebot without a medallion picked the nearest porter by straight line and, where no
  medallion merchant stood beside it, gave up every turn until the 15-minute watchdog moved it. Bots now go to the
  nearest porter that sells their medallion, and a wait past 5 minutes logs `RVR_FRONTIER_WAIT_LONG` with what held
  the bot (checker row `rvr/porter_wait`).
- The log checker's regroup row shows how far each regroup call spread (#419; the relay itself is unchanged: 9a173f4's
  17.4% is under the owner's 20% bar of 2026-11-19).
- **Repeated pocket rescues stop (#420).** A bot moved off disconnected ground by an audited pocket escape, and
  back where the same route fails again, was moved again with no end (the Connacht hill camp at 200:314846,471535 lies
  inside the 4,000-unit pocket whose escape is the road 3,300 units off). After two identical rescues for one goal within
  half an hour the third is not made: the goal is given up as any failed route is (camp route quarantine, a new camp)
  and `AUTONOMOUS_ROUTE_POCKET_ESCAPE_LOOP` names the bot, the from and to points, the camp, its target and the goal.
  `AUTONOMOUS_ROUTE_POCKET_ESCAPE` now names the camp, target, goal and `sameEscapes`. No navmesh or pocket changed;
  the spot fixes themselves still need those PC log lines (the two Lough Derg / Bri Leith seam spots of #421 lie in no
  audited pocket, so this does not reach them).

**Bot schedule off by default** (owner 2026-11-21): `bot_schedule_mode` now defaults to `off`, the whole roster online,
in the server, the launcher's defaults and Recommended, the shipped world and Setup. A world that ran 1.2 with the
untouched real-clock default moves to off once (`Living Server 1.2.1: bot_schedule_mode set to the new default off`);
a schedule chosen in the launcher is kept. Settings upgrades now apply in order, so a 1.1.4 world also ends on off.
Bots' breaks are a separate setting and stay on.

**Chat, targeting and the launcher**
- **/faction quiet minutes** fill back to about 5 lines, judged over the same 60 seconds as the cap of 7, which still
  holds.
- **`/target`** finds a nearby NPC for a player (Caer Caledon's guards), not only for a GM.
- The launcher's chat setting reads "HOW MUCH BOTS TALK", as the docs.

**Stability**
- The casting line-of-sight check treats a client it can't ask as no check, instead of leaving a gap behind 1.2's null
  guard.
- A packet queued while its queue is drained waits instead of raising "DrainTo in progress".
- The missing stock merchant item templates at start-up are one warning with a count; the rows are at debug level.

**Lore** (owner decisions in [docs/1.2.1/HEROES.md](docs/1.2.1/HEROES.md); review in
[docs/1.2.1/REVIEW-COMPANIONS-2.md](docs/1.2.1/REVIEW-COMPANIONS-2.md))
- The clean-up, provenance, flagship and folklore-hero passes merged into `integration/1.2.1` earlier (#378-#391).
- Legend companions, second pass: Albion (Tristan, Lancelot and the Round Table in Lyonesse, Cornwall, Camelot, Avalon
  Marsh, Morgana and the Shade of Uther), Midgard (Starkad, Ragnar's sons and Beowulf in Gotar, the Cursed Tomb,
  Varulvhamn, Mahattava, Malmohus, East Svealand and Jordheim) and Hibernia (Ferdiad and Láeg at Ardee, Scáthach,
  the Red Branch and Deirdre, Medb and Fergus, Mac Dathó's feast at Ailbe). Nothing was cut. An independent review
  checked the myth facts against their sources and fixed what it found, including Saint Patrick taken out of
  Hibernia's Oisín lore.
- Third pass (deepening, HANDOFF 1.5; review in [docs/1.2.1/REVIEW-THIRD-PASS.md](docs/1.2.1/REVIEW-THIRD-PASS.md)):
  Albion's six epic places, the Shrouded Isles and five thin bosses, and King Mark made Duke Marcus's son; the neutral
  proving fields (Caledonia, Abermenai, Thidranki, Murdaigean), Luso, six Midgard bosses and three saga-voice chapters;
  Hibernia's Blight, the Cursed Forest and fourteen thin bosses from the Mythological, Ulster and Fenian cycles. Nothing
  was cut. Caer Sidi's toll now agrees in every telling: sixty-seven of eighty came home.
- **No real companies and nothing that breaks the fourth wall** (owner 2026-11-21; #399, review in
  [docs/1.2.1/REVIEW-FOURTH-WALL.md](docs/1.2.1/REVIEW-FOURTH-WALL.md)). About 860 player-visible lines that named
  real companies, sites, patches, years or game software are reworded in-world, with nothing cut; real sources stay in
  the facts' hidden source notes. `tools/lore/fourth_wall.py` makes CI fail if they come back. Seven fight tips that
  the bosses' own rules contradicted now match them (Saeor, Asmoien, Ba'alorien, Sys'sro, the afanc, Oona, Oro,
  Krevo). The companion-name check covers every legend the lore tells of (#400), and the Avalon City summary is new
  writing (#401).
- **Fight advice in-world** (owner 2026-11-21, "Rewrite that"). Every boss's legend ("What the raid leaders say") and
  its bot lines are rewritten without game terms, about 3,800 strings across all 578 bosses: strength as a tier ("a foe
  for the realm's best, a full band of them"), distance in paces, damage as a share of a robed caster's strength,
  health as "as much killing as N raiders", times exact. Independent reviews per realm
  ([Albion](docs/1.2.1/REVIEW-LEGENDS-ALBION.md), [Midgard](docs/1.2.1/REVIEW-LEGENDS-MIDGARD.md),
  [Hibernia](docs/1.2.1/REVIEW-LEGENDS-HIBERNIA.md)) checked over 300 tips against the bosses' code and fixed every
  one that was wrong, many older than this pass: throws, jails and cold heals that ship on but were told as off
  (Hurika, Hakr, Vifil, Kvasir, Easmarach, Spindler, Legion), missing mechanics (Golestandt, Cuuldurach, Gjalpinulva,
  Xanxicar, Maldahar), and wrong numbers (Xanxicar's stomp lands seven seconds after his bellow). Stories that
  contradicted the mechanics now tell how the favour came back, with nothing cut. The lore lint now checks every
  visible field for game terms (`tools/lore/fourth_wall.py`).
- **Place lore and bot lines in-world too.** The last 1,756 game terms (levels, numbers, "faction", "zone", "epic",
  "named", teleporters, battleground level ranges) are reworded on the same standard; a place's level range now says
  who it suits ("hunting for seasoned bands"). One wording each across the realms: keeps "built up N courses of the
  ten", named foes "foes of name". The loremaster topics "epics" are now "great quests". The Bane of Hope throws again
  in every telling, as its code does, and Vagdush plants his own feet rather than rooting you (review in
  [docs/1.2.1/REVIEW-GAME-TERMS.md](docs/1.2.1/REVIEW-GAME-TERMS.md)). The lint is now strict: a game term in any
  player-visible lore fails CI. `tools/lore/schema_mirror.py` runs the C# lore schema checks without dotnet.
- **Every great foe has its book (#399-#402).** All 157 epic bosses now have at least four chapters, a 300-500 word
  story and two to four book pages written as documents of their world (letters, tallies, slates, daybooks, triads).
  The Behemoth has a fifth chapter. Three independent reviews, one per realm, checked every new page against the old
  chapters and place lore and fixed 27 contradictions by rewording the new text, never cutting the old
  ([REVIEW-EPICS-ALBION](docs/1.2.1/REVIEW-EPICS-ALBION.md), [-MIDGARD](docs/1.2.1/REVIEW-EPICS-MIDGARD.md),
  [-HIBERNIA](docs/1.2.1/REVIEW-EPICS-HIBERNIA.md)). Two older ones are fixed by owner decision 10 (Mythic canon
  first, then the rule of cool): Iorwerth sang in the deepest cellar but never went past the walled vein, and
  Weno'iak moves his books before any company comes. `tools/lore/depth_report.py` lists every file below a floor.

**Linux** (merged to main): the launcher's tooltip bleed-through and alt-tab crash under Wine (#393; Windows unchanged)
and the Linux start script that installs launcher updates and keeps the game fullscreen (#395). Neither has run under
Wine since the review changes.

**Not a change:** #209 (Crownfire Lance, Pendragon Sovereign): that class and spell are not part of Living Server. #34
(new-client hotbars, Stealth, SELL): already in 1.2's new-client script patches.

## RvR route traps from the PC log of build 43c43aa: the Cruachan Gorge seam, Odin's Gate muster posts, no exterior route, the rally-delay false alarm, a casting crash (1.2, 2026-11-19; tests only)

Seen on the owner's PC on build `43c43aa` (2026-11-19 13:20-14:22, restart 13:40:58; `DIAG-43c-rally`). Owner: "Fix
before publishing". Nothing below is seen in game yet: **tests only** (`UT_RvrRouteTraps43c`, `UT_CastingLosCheckNull43c`; navmesh probes with the
explicit `UT_ZoneStepProbe`, `UT_TripProbe` and the new `UT_RvrRouteTrapProbe` on the 0.37b fresh runtime's meshes). No
navmesh, seam file or road file changed.
- **The Cruachan Gorge / Breifine seam (200:384960,385504).** 206 `AUTONOMOUS_ZONE_SEAM_CYCLE` lines from 75 Hibernian
  bots after the restart on the walk to Druim Ligen (the frontier medallion, goal 333231,420812), each ending in a pocket
  escape to Tir na Nog. A bot that had just crossed from Breifine stood on the gorge's main ground, but the zone itinerary
  took the ground from the nearest zone road anchor (384768,386202,7080, 723 away on the ledge above), so it planned
  211>212>210>206>200>207 and walked back into Breifine, whose plan (212>211>207) sent it into the gorge again. A point that
  stands on a crossing the seam file lists (within 96 of the border, 96 along it and 160 in height) now takes that
  crossing's own ground (`AutonomousZoneItinerary.SeamGroundAt`). On the installed meshes the gorge side now steps on to
  Connacht (334880,417728) and the whole trip from the seam to Druim Ligen walks through (73,485 units, no turn back).
  The other seam-cycle spots of the log (Lough Derg / Bri Leith at 384960,486688 and 385088,483424) get the same ground as
  before and are not changed by this.
- **Muster posts no walk reaches (Odin's Gate, 100:596055,581400).** Miadur 4598, Yvik 4824 and Blotiogg 4677 stood
  90-230 short of their posts (slots 13, 28 and 20 of the Hibernian muster), failed three local recoveries each and were
  sent to Tir na Nog at 13:58, a minute before the march. On the installed meshes 29 of the first 80 slots there, and 11
  of 80 at the Albion muster in Emain Macha (200:475835,343661), have no walk from the muster centre. A slot's post is
  now proved from the centre (a complete corridor); a spot that fails takes the nearest spot round the same ring (12
  tries, then 12 on a ring 60% as wide), else the centre's floor (`AutonomousRvrMusterPost`). Posts are proved once per
  centre and slot and shared by every bot. With the meshes: 0 of 80 unreachable at both musters. A reachable slot keeps
  its old spot.
- **"No connected exterior route" by the arrival point.** Poshozois 4911 and Clumsychampion 5149 (rvr-keep-77, from
  100:596048,582909) and Linaelorbel 3236 (rvr-keep-105, from 200:476630,343539) walked toward unreachable posts onto a
  scrap of mesh with no way off and failed their keep route 5 times each, from 14:04; the first two were sent home by the
  15-minute watchdog. The posts above remove the cause. As a fallback, a bot whose keep route fails twice with "No
  connected exterior route" within 4,000 of its realm's arrival point in that frontier (the frontier porter's drop, the
  muster centre) is moved to that point and plans again (`RVR_KEEP_ROUTE_RESTART`). From both arrival points the routes
  exist on the meshes.
- **RVR_RALLY_DELAY no longer names parties holding their muster post.** 39 of the 50 lines were siege and defense
  parties standing at their posts as ordered: the muster path stops a bot there and never reported travel. A member that
  reported itself at its post within the attendance window (15 s), or stands within its post radius (160), counts as
  making progress. A member with no step and no post for 8 minutes is still reported, as before.
- **A spell's line-of-sight check no longer throws when the cast is cleared mid-tick.** At 14:56:33 `CastingService`
  logged a `NullReferenceException` in `CastingComponent.StartDuringCastLosCheck` (from `SpellHandler.CheckDuringCast`)
  and removed the gamebot Carik from the world: the component's spell handler had been cleared during the tick (an
  interrupt or the cast ending). Both LoS checks now use the asking handler (the one whose tick asks, so nothing else
  changes) and its checker, read once; a missing handler or checker means no check (`UT_CastingLosCheckNull43c`). Once in
  about 1.75 hours on that log.
- **Caer Caledon's lord at 100% (looked into, no change).** Damage reaches the lord: the status lines show it at 99%,
  98%, 97% and 99% after the storms of 13:30, 14:05 and 14:22, and Gantend was attacking it at its feet (14:05:25). It
  heals back between storms. At most 7 storm bots (levels 30-35) reached the level-46 lord (three times a guard's health)
  at once, under its guards. Siege engines never hurt a lord, by design (`GuardLord.TakeDamage`). Expected, not a bug;
  nothing was weakened.

## Chat from the PC log of build c81eb5d: Tepok's Mine, help calls to everyone, /broadcast answers, /lfg at 22, the /faction minimum (1.2, 2026-11-19; tests only)

Seen on the owner's PC on build `c81eb5d` (2026-11-19 09:07-09:42, Testrunner, Albion 22, Camelot; `DIAG-c81-nonraid`).
Nothing below is seen in game yet: **tests only** (`UT_ChatPcCheckC81`, `test_played_check_log.py`).
- **Travel follow-ups find places with an apostrophe.** After "Tepok's Mine for 22 bring a group", "is it far?" became "is
  tepoks mine far?" (the catalog alias) and "how do i get there?" "how do i get to tepoks mine?"; both got "no clue"
  (09:20:36, 09:21:26), because the travel answers matched the world's "Tepok's Mine" with its apostrophe. Both sides are
  now compared without case or punctuation (`AutonomousChatKnowledge.NamesPlace`), and a catalog alias of a place that is
  itself a zone ("tepoks", "cardova", "pennine") finds that zone (`NamedPlaceKey`). The stable-route answer matches the
  same way. The sweep over places.json: Hadrian's Wall, Odin's Gate, Nisse's Lair, Iarnvidiur's Lair, World's End and
  every short alias of a zone (cardova, pennine, salisbury, skona, emain...) missed the same way and are now found.
- **A help call or a line to everyone is open to every bot.** "can someone help me in Camelot Hills?" went to Sven alone
  (he had the floor from the earlier answers), who refused (`in_bot_group`), so no other bot could answer or offer to come
  through `ChatPromises.Gate`. `KeepsTheFloor` now lets go of help calls and of lines with "someone", "anyone", "any1",
  "anybody" or "ne1".
- **A /broadcast question is answered where it was asked.** "anyone know where to get a good shield?" in /broadcast was
  answered by /tell, 19 s later. It is now answered in /broadcast when the answer fits under that channel's ceiling, else
  in /say when the player is within earshot, else not at all; within 12 s (`DirectTypingCapMs`). /chatgroup keeps its tell.
- **/lfg nobody can answer gets an honest reply.** "lfg 22, any group" met silence: no bot party near 22 and no solo bot
  within 2 levels. When no invite and no duo offer can go out, one bot of the realm now says so by tell ("nobody around
  your level that i know of, sorry"). When solo bots near the level exist, the last of them offers if none has. The
  `BOT_LED_INVITE not_sent ... no_party_fits` line is written once per ask (it was three: the ask and two pulses).
- **/faction keeps the owner's 5-7 (decision 5, 2026-11-18: "Yeah I think 5-7 is the right pace").** With no scene it
  settled at 3.9 a minute (09:30-09:40). `chat-budget.json` has `pace.faction_min_per_minute: 5`: /faction's floor is at
  least 5 and its target 5.5, at any hour, scaled by "How much bots talk" against its default 2 (1 gives 2.5, 0 only
  answers), fading on a roster under 300 bots a realm ((bots / 300)^2), and not with the clock schedule (its quiet night
  is its purpose; the owner's PC runs it off). Under that floor the realm's bots try to talk as
  often as they can (pressure 4; it read about 2). The cap of 7 is unchanged. In the simulation of a quiet 09h hour with
  the player online (960 bots a realm) c81eb5d's budget gives 4.1-4.2 a minute and this one 5.2-5.4 (five seeds); with a thinner
  opener supply (3.6 before) it reaches only about 4.6, so the PC count decides.
- **A storyline beat waits for the player's answer.** Ealhswith's beat came at 09:23:07, between "how do i get to
  breifine?" (09:22:57) and its answer (09:23:09). Scenes (`AutonomousBotDebates.PlayBeat`, its split pieces too) now hold
  while a player's /faction line has the floor, as running threads do (the same capped hold).
- **The equipment upgrade line logs the gain that decided it.** 109 of 200 `AUTONOMOUS_EQUIPMENT_UPGRADE` lines read
  newValue under oldValue: the line printed `EquipmentValue`, while the set score (`GearImprovement`) decided. The line now
  adds `gain=` and `decidedBy=set|value`, the gain read before the move. Log only; no decision changed.
- **The launcher label is "How much bots talk"** in the 1.2 release notes, CHAT-BUDGETS and chat-budget.json (they said
  "How much they talk").
- **Checker:** the regroup row's (c) says "nothing to judge (no relayed starts)" when there were none, not "a build before
  the sender was logged". `RELAY_SHARE_BAR` is 20% (owner decision 2026-11-19, "Raise to 20%"; it was 15%, and `9a173f4`
  read 17.4% with (a), (b) and (c) all 0).

## Battleground keeps: guards and lords at their battleground's level (1.2, 2026-11-19; tests only)

Owner, 2026-11-19: "I want the guards and lord levels appropriate for their battleground". This is the owner's own request,
so it overrides the rule against changing keep, lord or guard levels, for the four battlegrounds' own keeps only. Seen on
the owner's PC on build `c81eb5d` (`BG_KEEP_STATUS` 09:07-09:40): Caer Caledon's lord was level 57 and its guards up to 48
in Caledonia (levels 30-35). The gate clear stalled twice and the keep was never taken. Nothing below is seen in game yet:
**tests only** (`UT_BattlegroundKeepGuardLevels`).
- **Why Caledon was 57.** Guard and lord levels come from the keep's base level in the world's `Keep` table, through DOL's
  formulas: guards `base + 1 + keep level x 1.6`, the lord `base + (base / 10 + 1) x 2 + keep level x 1.6`. The shipped world
  gives three battleground keeps their bracket's top level + 2 (Dun Abermenai 21, Thidranki Faste 26, Dun Murdaigean 31)
  and Caer Caledon 46, eleven above its 35. At keep level 1 that is guards 48 and lord 57. Nothing raised it while playing:
  the log matches base 46 at keep level 1 exactly (battleground keeps stay at level 1 since 2026-11-15), and keep
  balancing only runs in frontier regions.
- **The rule.** A battleground keep's base level is its bracket's top + 2, the world's own value for the three keeps
  nobody complained about. So guards are the top + 4 and the lord the top + 9 (Abermenai, Thidranki) or + 11 (Murdaigean,
  Caledonia). Abermenai 23/28, Thidranki 28/33 and Murdaigean 33/40 are unchanged; **Caer Caledon goes to guards 39 and
  lord 46** (base 37). Hasteners stay level 1.
- **A cap.** Whatever raises a battleground keep (a claim and upgrades with `allow_bg_claim`, keep balancing, a bigger
  `keep_guard_level_multiplier`, a GM's `/keep` command), its guards and lord never go above those level-1 values.
- **Existing and fresh worlds.** At every start, a battleground keep whose base level is not its bracket's is set to it
  and its `Keep` row saved (`BG_KEEP_BASE_LEVEL_REPAIRED`, once on a played world; a second start changes nothing).
  `release-world-defaults.json` now carries the four base levels and `build_release_037.py` writes and checks them, so
  the shipped world has them too.
- **Unchanged:** every frontier keep and tower, the portal keeps, and the old battleground regions that have no bracket
  in `BattlegroundBrackets` (Cathal Valley's Dun Orseo, Killaloe, Braemar, Molvik).

## The release name: "1.2", not "Beta 1.2" (1.2, 2026-11-19; tests only)

The owner named this release **1.2** final (2026-11-19), not Beta 1.2: tag `v1.2`, build `1.2`, title "Living
Server 1.2 — Where Legends Wake". Every release up to 1.1.4 was a beta (`v1.1.4-beta`); from 1.2 on they are final.
- **The build is `1.2`.** `ForkBuildInfo.BuildName` is `1.2`, so the server logs `FORK_BUILD name=1.2`, the launcher
  shows LIVING SERVER 1.2, and the package is `OfflineDAoC-LivingServer-1.2.zip` with one `LivingServer-1.2` folder
  (BUILD-INFO `BuildName=1.2`), Setup is `LivingServer-Setup-1.2.exe` and CI's candidate artifact is
  `OfflineDAoC-LivingServer-1.2-<sha7>`. Candidates built before this change say `1.2-beta`.
- **Updates from 1.1.x.** The launchers' version check (unchanged since 1.1.2) already ranks a final release above
  every beta of lower numbers and above the beta of the same numbers (1.1.4-beta < 1.2-beta < 1.2 < 1.2.1-beta), so
  launchers on 1.1.2-1.1.4 see v1.2 as newer and offer GET UPDATE; their package check looks for
  `OfflineDAoC-LivingServer-1.2.zip` and `LivingServer-1.2`, the names the build now makes. Tests only: the launcher
  tests (`UpdateTests`, Windows CI) and the release tools' tests; the 1.1.4 → 1.2 update hasn't been seen on a PC.
- **Release tools.** `check_versions.py` reads a released `## X — <date>` heading without "Beta" (0.x headings still
  aren't releases), README's "**X is out.**" sentence and `v1.2` links, and with `--tag` (publish-release runs it so)
  fails unless the tag is the newest release's. `publish_release.py` and `publish_setup.py` take `v1.2` as well as
  `v1.1.4-beta` tags, and refuse 0.x tags. `played_check_log.py`'s build row takes `name=1.2` (and the earlier
  candidates' `1.2-beta`).

## Chat answer timing from the in-game check: /faction answers in 12 s, the companion yields a line for another bot (1.2, 2026-11-19; tests only)

Seen on the owner's PC on build `5154cae` (2026-11-19 07:04-07:27, Testrunner in Camelot). Nothing below is seen in game
yet: **tests only** (`UT_ChatAnswerLatency0730`).
- **A player's /faction question is answered in 12 s at most.** "is it far?" (07:08:25) and "can someone help me in
  Camelot Hills?" (07:10:24) were answered 33 s later (Ogria 07:08:58, Thaxard 07:10:57). The whole delay was typing: a
  103-107 character line at about 40 wpm (the bots type at 25-60). The first answer to a player's question in /faction
  now comes within 12 s, the cap DESIGN-018 set for tells and lines that name the bot. The pace from #368 never held answers
  to the player and still holds openers and scenes to the owner's 5-7 a minute; pile-ons and other talk keep their own
  typing time. The hold from #370 (running exchanges wait until the answer is due plus 2 s) is now 14 s at most for such
  a question, not 30.
- **The companion leaves a /say line aimed at another bot to that bot.** At 07:17:26 "you ready?" was said with the
  gamebot Linaibel targeted (she had the floor after her "oh hey" at 07:17:14). Her answer was planned
  (`CHAT_PLAYER_REPLY_PLAN ... directed=True planned=1`) but never said, and Pannax answered "ok" on the companion's 35%
  chance to chime in. A companion now chimes in unnamed only when the line is for nobody: not when the player targets
  another bot, nor when another bot has the floor and the line goes on with it (the gamebots' own addressee rule).
- **A dropped answer says why in the log.** Linaibel had been given a solo task at 07:17:13 and was next seen outside
  Camelot; the drop was recorded only in `/lswhy`'s memory, so the log cannot show whether she rode off, took a route or
  left the region. A planned answer that cannot be said now also writes `CHAT_PLAYER_REPLY_DROPPED ... reason=` (riding,
  stable_route, left_region, out_of_earshot, fighting, casting, gone, dead).

## Chat from the in-game check: "is it far?", help calls, placed lore, other realms' zones, "an", the launcher's count (1.2, 2026-11-19; tests only)

Seen on the owner's PC on build `5154cae` (2026-11-19 07:04-07:27, Testrunner in Camelot; `SWEEP8-RESULTS-1119`). Nothing
below is seen in game yet: **tests only** (`UT_ChatPcCheck0719`; the launcher test runs on the Windows CI job only).
- **"is it far?" gets a travel answer.** After "Keltoi Fogou is good around 22", "is it far?" was completed to "is keltoi
  fogou far?" and drew the lore bible's description (Ogria, 07:08:58). A how-far question now goes to the travel answer
  (`AutonomousChatKnowledge.TryAnswerHowFar`): a dungeon by its entrance zone, how far that is from the asker (or from
  where the asker's capital opens into that region), and the horse only when a stable route of the realm ends there
  ("not far, take the horse to Campacorentin, Keltoi Fogou entrance is there"). The lore bible no longer takes such a
  question.
- **A player's /faction line has the floor.** Six seconds after "is it far?", Emberreaver's answer to Grittath's question
  ("pennine mountains, a horse gets you most of the way") read as the answer to the player. Bot exchanges already running
  in the channel now wait until the player's first answer is said (its due time plus 2 s, 30 s at most), then go on.
- **A call for help is not a lore question.** "can someone help me in Camelot Hills?" drew the Fielath lore line
  (Thaxard, 07:10:57). Such a call now skips the knowledge and lore answers: the bot offers to come only through
  `ChatPromises.Gate` (reason Help), else it says "cant, sry" or "what level are the mobs?".
- **Placed lore is logged only when it is heard.** `LORE_PLACED ... bot="Trorirrum" channel=Say` (07:30:14) came with no
  line: the bot was picked within 1,000 units (past /say's 512) and `Speak` reported a /say nobody could hear as said. The
  bot is now picked within /say range, not from an RvR party, and `Speak` returns false when nothing went out (/say,
  /group and guild).
- **Other realms' zones are told honestly.** An Albion bot answered "how do i get to breifine" with "new frontiers, a
  horse gets u most of the way", from a leftover zone of that name in region 163. A zone of another realm's land is now
  "thats hib frontier, rvr only" (its frontier) or "thats hib land, cant get there from here" (its home zones).
- **"an armorcrafter".** "a" before a word said with a vowel is "an" in the corpus filler (now numbers too: "an 18
  suit"), in field talk, bot-led group lines, styled lines and the gear answer that said "a armorcrafter".
- **The launcher's online count.** The dashboard showed "5 online" and "Bot status updated 6:58:55" for half an hour
  while 563+ bots were on, in the session where the server was started straight after INSTALL.cmd; after a later START
  (07:57) it counted 2,877 at 08:14. The five-minute refresh was armed only after the launcher's first dashboard and
  Realm Exchange reads finished. START clicked while those ran got its own refresh (the 6:58:55 one) and then no timer
  until the exchange read ended, and that read joined every listing to every bot row with an expression no index serves
  (10 s for 2,000 listings and 6,000 bots on a synthetic world, growing with both). The timer is now armed first, the
  seller is looked up by the bot's id (0.01 s for the same world), and a read that never returns is given up after 60 s
  so the refresh gate opens again. How long the read took on the PC is not known: the launcher writes no log.

## Raid fixes from sweep 7c: Suttung's empty pulls, Steinvor's pillar, cure pile-ups, Curengkur's staging, hub posts, raid-end stall (1.2, 2026-11-19; tests only)

Seen on the owner's PC on build `9a173f4` (2026-11-19 01:30 to 05:07, `DIAG-9a17-sweep7c`). Nothing below is seen in game
yet: **tests only** (`UT_RaidSweep7c`, `test_played_check_log.py`; `UT_CurengkurEntryProbe` read zone187.nav). No boss is
weakened: Suttung, Steinvor, Curengkur and their scripts' fights are as they were.
- **A stranded raider is no longer teleported into the raid's own resting target (T-1).** Both of Elder Icelord Suttung's
  attempts began with nobody at the muster: at 04:03:29 Pwnage9 (and 63 others in that minute) was moved from a navmesh
  island to 32787,53968, Suttung's muster, 764 from him while he rested (aggro 600 + the 300 margin); he died at 04:03:42
  and the pull was committed at 04:03:44. The same shape began attempt 1 at 03:44:21. The stranded-regroup guard left the
  raid's own target out of its dangers. Now, while the target rests and is a mustered encounter, it is a danger for that
  teleport (`RealmRaidDungeonRoute.RestingTargetDangers`: aggro + 300, never more than its muster's stand-off, so a muster a
  short tether puts closer still takes raiders); refused raiders stay put and `REGROUP_REFUSED` names him. The muster itself
  is also moved out of its own target's aggro + 300 (`InTargetClearance`): Suttung's, set when he joined the Guthlac regroup
  (03:44:11), stood 785 from his spawn with a 1,400 stand-off; most likely the walk's "the raid is already close" choice
  put it where the main body stood (inference: the log has no `MUSTER_GUARDED` line for him). Such a muster is now moved
  back along the walk or to a clear point around him, as for another boss's aggro.
- **Steinvor's Pillar of Ice leaves when it should (P-1).** It was removed only by his own timer, and only while he lived:
  his last pillar (02:25:54) outlived him (02:26:00) and 73 raiders stepped out of it for 80 minutes (2,779+
  `EPIC_TACTIC_BOT ... StandOff "Pillar of Ice"` lines, filling the shared 1-a-second tactic log). The pillar now has its
  own removal timer, 8 s after it is raised, the same moment he removed it; its one Pillar of Frost (3.5 s in) and his
  fight are unchanged. Catalog regenerated; the Steinvor and Skuf hand notes re-stamped after reading the script.
- **Healers no longer pile onto one mesmerized raider (H-1).** At 02:21:52 twelve Midgard Healers cast Cleanse Mind on
  Captainfluffy in the same second; 263 of the night's 1,121 `RAID_CURE_MEZZ` lines (23%) were a second or later curer
  on the same raider in the same second. A curer now claims the raider it cures for the cast time plus 500 ms
  (`RaidCureClaims`); the others leave him out and cure someone else or heal. A cast that does not start gives him back.
  The single cure poison of sweep 7b claims its raider the same way (two dotted raiders and twelve curers split six and
  six); the group cure is the curer's own party's and is not claimed.
- **Curengkur's raid tries the next staging posts (C-1).** It has never started on the PC. At 04:32:18 his first post
  (381686,172803,4074) walked to him but the hub's route found "no corridor inside zone 187 to the staging post". On
  zone187.nav (`UT_CurengkurEntryProbe`, read only) that post has no corridor from any of 16 points of the zone 185/187
  seam, while posts 1, 3, 4, 6 and 7 have one from some. Now a dragon's or open-world boss's start tries posts 1-7 when
  the route stops inside the staging zone; the first the route reaches becomes the raid's first post
  (`REALM_RAID_STAGING_REROUTED`), and a rejected post is taken by no other slot. Which seam point the live route uses is
  not known from here, so whether Curengkur now starts is owed to the PC.
- **A hub's muster posts are walked to from its floor, and a failed hub post says why.** dragon-hibernia failed "No muster
  post could be placed at the hub" at Innis Carthaig on both of its tries (22:26:23, 04:07:42), and no start of it is
  logged since #224 added the walk from the hub's centre to each post. That walk started at the centre itself, a service
  NPC's spot; when that spot has no floor right under it (within 2 across) every post fails. Now it starts on the
  centre's floor, or the nearest walkable floor within 160 (`RealmRaidMuster.HubFloor`). zone204.nav is not in this
  repository, so that this was the refusing check is an inference: a failed hub post now logs
  `REALM_RAID_HUB_POST_REJECTED ... rejectedBy=<check>` with every check's count, the hub floor used and the NPCs that
  refused posts.
- **The formation pass after two raids end together no longer stalls the server.** Tuscaran (04:07:08) and Evern
  (04:07:31) released about 150 raiders; the next formation pass held the coordinator lock 2,817 ms and the server
  stalled 3,040 ms at 04:07:46. Its time budget was checked only between leaders, and one party's rendezvous proves a
  zone itinerary for each member. Past twice the budget (300 ms) a party's rendezvous checks now stop and the party forms
  on a later pass (`AUTONOMOUS_GROUP_FORMATION_DEFERRED`); its members stay free and the zone steps already proven are
  remembered. Timing only.
- **Checker** (`played_check_log.py`):
  - T-2: a `TARGET_STATUS inCombat=False` after a boss's last fight-ledger line ends the engagement that line kept (one
    ledger line after Suttung's wipe kept him "engaged" until 04:07). The raid's own pull (FINAL_ENGAGE, EARLY_PULL
    Commit, TARGET_CALLED) still ends only at its wipe, defeat, heal or the lapse. On `9a173f4` the
    `teleport_resting_boss` row is now Contradicted: 64 teleports at 04:03 within 900 of the resting Suttung.
  - W-1: a return wave at `step=fighting` whose `kills=` rose is moving (its step is `step_at=`). Krondon's wave 1 at
    step 23 (01:54:50-02:11:27, kills 20 to 44) is no longer counted as held; epic-midgard wave 5 still is.
  - A new row, `empty_pull_teleport`: an `EARLY_PULL` Commit within 120 s of a stranded teleport into the region while the
    region's last `FINAL_MUSTER` showed 0. Contradicted on `9a173f4`: Suttung at 03:44:21 and 04:03:44, and an icebound
    skeleton at 01:54:29 and Issorden at 01:55:10 (33 teleports to the skeleton's muster at 01:53).
  - The regroup relay share (17.4% against the 15% bar) is unchanged: the exact checks (a), (b) and (c) are all 0 on
    `9a173f4` (2,885 relayed starts named their sender).

## Chat from sweep 7c: /faction held to the owner's 5-7 a minute with scenes, /broadcast answers heard (1.2, 2026-11-19; tests only)

Seen on the owner's PC on build `9a173f4` (2026-11-18 22:13-22:25, `DIAG-9a17-sweep7c`, the player online 2 min 51 s).
Nothing below is seen in game yet: **tests only** (`UT_FactionPace`). No line, scene, storyline or tale is removed.
- **/faction keeps the owner's pace with scenes playing** (owner 2026-11-18: "5-7 is the right pace"). The player heard 13
  lines a minute (11.9 and 14.6): the storyline `story_mistell_1` (18 lines) and the tale of Govannon (7 lines) said their
  beats 4-12 s apart, 25 of the 37 lines. A scene owned the channel, but its own beats were paced only by the ceiling
  (17-19 a minute). Now `chat-budget.json` has a `pace`: at most 7 /faction lines a minute, every line counted. An opener
  waits until it and its answer fit (hold `pace`; answers, realm events and lines to the player are never held). A running
  debate, storyline or tale says a line no sooner than 10 s after the channel's last one (6 a minute), so it is slowed,
  not cut. A scene's start is never held, so the live keep and relic scenes start as before. Both scale with
  `bot_voices_chat_rate` against its default 2. `UT_FactionPace` plays an hour with the real storyline and tale: 9.9 a
  minute and a busiest minute of 19 on `9a173f4`'s rules, 5.8 a minute and no minute over 7 now, each scene at 5-7.
- **/broadcast questions are answered where the player hears them** (owner's decision 12). 0 of 16 /broadcast lines were
  answered; 4 of them were questions that have answers. The region's three-minute answer turn was taken when the question
  was asked, before any answerer was found. The answer could be said only in /say, with a player beside the answerer,
  next to the asker. Now the turn is taken only when an answer is ready and a player hears it: in /say beside the player,
  else in /broadcast in the answerer's area.
- **Not bugs (report only):** 0 `CHAT_PROMISE` lines because the player typed nothing (0 player chat lines in the whole
  log) and no bot line said "omw" or "coming". Conversation memory (#350) writes no log line of its own, so "0 lines" says
  nothing about it either way; it only acts on a player's lines, and there were none.

## Raid fixes from sweep 7b: Roesia's camps out, Summoner Pain cured, walk-back cap, dragon wipe line (1.2, 2026-11-19; tests only)

Seen on the owner's PC on build `9a173f4` (2026-11-18 21:59 to 11-19 01:30, `DIAG-9a17-sweep7b`). Nothing below is seen
in game yet: **tests only** (`UT_RaidSweep7b`, `UT_DragonRaidWipes`, `test_played_check_log.py`). No boss is changed:
Summoner Roesia's script, Summoner Pain, her health and damage are as they were.
- **Summoner Pain's split camps (#359) are removed** after the owner's PC showed them failing. All four attempts at Roesia
  (Hibernia and Albion) were lost at 89-93%. Her Pain has a range of 1,800 and two casts are always active (36 s, recast
  every 20 s), so it reached both camps, which stood about 1,670 apart. She walked onto one camp, and the other camp's
  casters stood about 1,670 from her, out of nuke range, while the camp rule walked them back to their post. The raid dealt
  her 0.2-4.5k in the first minute, against about 17k on `b1b6846` without camps. `EpicEncounterTactics.PainCamps.cs`
  and its rule are gone. The raid is back on `b1b6846`'s rule: it fights within 900 of her spawn (`SummonersInsideTether`).
  That restores `b1b6846`'s play, which also lost to her (lowest 76%); the cure below is the real answer.
- **Healers cure Summoner Pain** (and any curable damage over time that reaches several raiders). Build `9a173f4` logged
  0 `RAID_CURE_POISON` lines all night. A raid healer cured only when no heal had been chosen, and the raid-wide patient
  check (anyone within 1,800 under 40%) chose one on every turn under Summoner Pain. A single cure poison was also handed
  to the raid's heal reservation, which re-aimed it at the raid's lowest raider under 80%, poisoned or not, or dropped it.
  Now (`RaidCurePoison`, `BotBrain.TryRaidCurePoison`):
  - an autonomous raider in a realm raid cures before its turn's heal when two or more raiders in its reach carry a
    curable damage over time and nobody of its own party in reach is under 50%, and no boss mechanic marks a patient;
  - it uses its class's own cure poison spells only (CurePoison, CurePoisonGroup; nothing new): the group cure when two or
    more of its own party carry it, else the single cure on its own party's lowest, then on another party's raider (a
    different one for each curer);
  - its own party's emergency heals stay first, and it cures at most once every 5 s (the existing cure delay, now for an
    instant cure too), so its other turns heal;
  - a cure (poison, disease, mesmerize, nearsight) is never re-aimed by the raid's heal reservation
    (`BotGroupSupport.Reserves`), so a cure mesmerize also lands on the raider it was cast for;
  - each cure poison cast is counted in #359's `RAID_CURE_POISON` summary line, as before.
  A deterministic model of a party under Summoner Pain (`UT_RaidSweep7b`) cures it on the first healer turn and keeps
  every member alive for two minutes of casts; `9a173f4`'s order cured nothing.
- **#358's walk-back has a distance cap.** A raider outside a fight-at-home boss's home walks back in only from within
  1,500 of the home's edge (`EpicEncounterTactics.WalkBackReach`). At Roesia 40 of the 102 raiders told to walk back
  stood 2,600-9,947 away (Unclespud 9,947, Lordnugget 8,454): they were joining or returning, and the raid's waves bring
  them.
- **A dragon's wipe line says who fell.** `REALM_RAID_WIPE` for a dragon now prints `engaged=` and `fallen=` too, as a
  dungeon's does. Golestandt's second wipe said `living=96`, which counts raiders who released and stood again; the dragon's
  own line said 72 of 91 fell.
- **Checker** (`played_check_log.py`): a new row, `raid_cure_dot`. A fight with a boss whose curable damage over time
  reaches the raid (Summoner Roesia: she hits the raid in an `EPIC_FIGHT_LEDGER` minute) should see `RAID_CURE_POISON` in
  her region during the fight. Contradicted on `9a173f4`: region 248 at 00:04-00:06, 00:12-00:14 and 00:57-01:11, with
  0 lines each.

## Bot chat audit follow-ups: the code (1.2, 2026-11-18; tests only)

What the chat audit's engineers left for each other in code (F1). Nothing below has been seen on the owner's PC yet:
**tests only** (`UT_ChatFollowUp12`, `UT_ChatFollowUpGate`, `UT_ChatPromises`, `test_played_check_log.py`,
`tools/bot-voices/tests/test_code_literals.py`).
- **"omw" goes through the promise gate everywhere it can be said** (owner, 2026-11-18 09:14: "omw" only when the bot goes).
  - Field talk: an answer to an "inc" or a rally ("omw", "porting to bled"), "on my way" to "anyone in emain?", a healer's
    "omw" to "rez pls", a guild raid's "count me in", the opener's closing "k omw" and a third bot's "omw too" each go
    through the gate for the bot that really says it, when it says it. A bot that won't go says "how many?", "not me,
    maybe later", "cant, sry, too far", "maybe" or "k ty" instead, and a third bot's chime is left out. The "inc" and
    rally lines carry the full keep name for the gate ("Bledmeer Faste" for "inc bled").
  - News reactions ("heading over if i can get away") come only from a bot the gate would send, promised when said; a
    town's /broadcast says none.
  - Bot-to-bot replies: an alarm reply is chosen when the bot could go and promised when it is said (a stand-in that says
    it promises for itself); other replies and the asker's closing say "omw" only the same way, or not at all.
  - Replies to a player: a written (corpus or memory) reply that says the bot is coming is said only when the gate sends
    the bot; otherwise the old reply, or none.
  - Everywhere else a written reply or closing never says it (`AutonomousBotVoices.PromiseLines`: only a caller that gates
    opens it). A source test lists every string literal in the bot chat code that says "omw", "coming" or "on my way" and
    fails on one that is not marked `// promise: gated|true|none`.
  - Scenes: no debate, tale or one-off scene beat says "omw", "on my way" or "ill be there" (confirmed, and now a test).
    Five storyline beats speak of a later meeting in their own arc ("i'll be there first. like always", "count me in");
    they are the arc's plot, not a walk owed now, and are left to the corpus owners.
  - A promise is made when its line is chosen; one whose line is never sent ends quietly,
    `CHAT_PROMISE broken ... why=unsaid`, which the played check counts as no failure (`unsaid=` in its detail). It waits
    for its line the longest a chat line can be held before it is sent or given up (a due feature's 180 s) and 30 s more:
    210 s, derived from that hold, so it never runs out first. Its deadline counts from the moment the line is said.
  - A line still held longer than that (behind a running scene) is checked again when it is sent (/say, /faction, /send,
    /guild, /lfg, /trade): the gate is asked again with what it was asked the first time, and if the bot won't go now, the
    moment's no-promise line ("how many?", "not me, maybe later", "cant, sry, too far", "maybe", "where are you?") goes
    instead. /group lines are not re-checked: a groupmate's "omw" there is true (it follows the player).
- **A bot under 50 tells its ding** (LOG-035): `AutonomousBotVoices.OnBotLevelUp` now calls `TellDing` below 50 (a ding to
  50 stays a story).
- **The memory reply reads the player's line** (DESIGN-007): both `TryKnownReply` call sites pass it, so "lfg 30 cleric"
  is checked against the bot's band and refusals before "sure, send an invite".
- **A plain "hi" gets no holiday answer** (CORPUS-B2-025): `TryGreetingReply`'s last pick leaves out `holiday_` lines.
- **The siege stages are said by topic** (B2-003): a notice no rule reads is said by its news topic (`siege_muster`,
  `siege_warning`, `siege_attack`, `siege_defend`) instead of as plain text.
- **The third realm's siege timer** (LOG-004, timer case): when a siege runs out its timer with no holder known, the realm
  that neither attacked nor defended hears "The fighting for X has ended", not "Our time to take X has run out".
- **Channel lines are cut at a word**: the /faction, /lfg and /trade sends cut at 240 characters on a whole word, ending
  "...".
- **No "lol" in a no-laugh beat** (CORPUS-B2-032 gap): a storyline's two leads and a rage-quit's roles no longer get the
  typing habit's "lol" either.
- **No towers**: the keep-claim setting's code comment no longer mentions towers (TEXT-013).
- **Period words in code**: "o/" is gone from guild greetings; "oof" said by bots is "yikes" (and "train of X, im dead" is
  "train of X, died"); the reaction lists (`AutonomousChatExchange.Reactions`, `chat_audit.py`) both know "lol ouch",
  "ouch gl" and "yikes". Word lists that read player text are marked `// chat-check: input`, and two lines that keep a
  word on purpose (the glossary's "ml" answer, the Atlantis legend) carry `// chat-check: allow <id> (why)`, which
  `check_runtime_names.py` now reads: it shows no code warnings.
- **/lstest deed drowned**: the GM test tells a drowning too.

## Lore follow-up: who a mechanic picks, and stories told once (1.2, 2026-11-18; tests only)

Follow-up to the chat follow-up F3. Lore is never cut: every claim and story was reworded or folded, nothing dropped. None
of it has been seen on the owner's PC.

- **Bots are picked where the scripts pick them.** 47 boss and place lore files said a mechanic picks "real players" only
  ("bots are never chosen", "only the human gets thrown"). For each one I read the boss's script. Where the pick is
  `EncounterTargets` (InRadius, InRegion, AsRaider, IsRaider), or a throw whose encounter ships with bot displacement on, the
  facts, legends and bot briefings now say "a raider, bots included". This covers Anurigunda, Aros, Beliathan, Boligar,
  Ozur, the Crypt Lord, Cuuldurach's glare, the Drau'gyn Sphere, Birghir, Hjalmar's howl, the Glacier Giant, the Green
  Knight, Hamar, Oro, Saeor, Hurika, Hakr, Kvasir's tunnels, Steinvor, Vifil, Krevo Ricik, the Legendary Afanc, Moran,
  Mortufoghus, Myrddraxis, Nosdoden, Oona, Orylle and Orshom's fire, Queen Kula, Risnir Cruss, Scurceol's orbs, the
  Spectral Provisioner, Cunovinda, Lossren, Roesia, Sys'sro, Torst, Xanxicar, Yar, Ydenia and Galladoria's Eyes.
  - **Ozur:** his crowd count includes bots, so a raid of 50 or more, bots and all, makes him hit harder. The lore had said
    it never happens.
  - **Hurika and Hakr:** their lore said the throws are off. Both ship with AllowDisplacement and AllowBotDisplacement on.
  - **Asmoien and Ba'alorien:** these two were the other way round. Their lore said they eat bots, but
    `IsDevourablePet` leaves out every GameBot, so they eat pets only.
- **Still "real player", because the script picks players only:** Archilus (`source as GamePlayer`), Elro the Ancient,
  Amminus Pilus's Pilus'Fury (players and summoned pets), Morgana's quest-step appearance and High Priest Andania.
- **Stories told once.** 33 lore stories opened with a summary paragraph that the later paragraphs told again (Abomos,
  Wyvernic Wraith, Uaimh, the primals and others), and a few places repeated a paragraph (Shrouded Isles, Muire Tomb,
  Spraggon Den, Treibh Caillte). Each is folded into one telling, with every detail the summary alone had woven in.
- A new test fails if any lore story repeats a sentence, if two of its paragraphs share a fifth of their word trigrams,
  or if a lore file outside the players-only list says a mechanic picks real players only.

## Chat follow-up: lore and tales warnings, Evern, Juggernaut, five bosses who do pick bots (1.2, 2026-11-18; tests only)

Chat follow-up F3. Lore is never cut: every line was reworded, no tale was dropped. None of it has been seen on the owner's PC.

- **No file without an owner:** tales_more.lines is E6's (the tales), storylines_more.lines E4's (the storylines) and
  scenes_live.lines E8's (the scenes), so their warnings will fail once STRICT_OWNERS covers them. A test fails if a new line
  file has no owner.
- **Builder warnings in E6's files and those three are 0** (`build_corpus.py --check --warnings`): {q} only before a
  question (statements lose it, questions get their "?"), near-copies of other scenes' beats reworded, "dps" in boss briefings
  said the 2003 way ("damage", "hitting"), "{enemy} stealther" and "{enemy} zerg" as {enemy_mod}, figures of speech
  that read as a time of day ("over breakfast", "on a quiet night") reworded, two "i'll be there" lines no code keeps,
  "album", "audit", "callback", "community" and "edition" replaced, and the remaining new words reviewed into
  vocabulary.txt (names from the lore bible, period words).
- **Evern** told his story twice and leapt to his target. Evern.cs has no leap (the move in his catalog entry is the
  fairies flying to the stones), so the lore, the legend and the bot lines speak of his long reach (350) instead, and the
  repeated paragraph is folded into the other two with every detail kept. **Juggernaut** told his story twice too, and his
  legend and one aggro emote said the same thing twice; each is said once now, nothing dropped.
- **Grand Summoner Govannon, Mistress of Runes, Morgus Urgalorg, Krackenschtein, Elder Council Guthlac:** their lore said only
  real players were picked ("bots are never chosen", "only the human gets those"). Their scripts pick from
  EncounterTargets.InRadius, bots included, and the two throws (the Shade of Aelfgar's ledge, Krackenschtein's throw) ship
  with bot displacement on, so the facts, legends and bot briefings now say a random raider, bots included. Krackenschtein's
  Bolt of Doom picks within 2,000 (the script), not 2,500.

## Darkness Falls: the raid no longer loops on a chthonic knight behind it (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18, the Albion Darkness Falls raid, `darkness-albion`, region 249).
Nothing below is seen in game yet. No knight, boss or monster is changed: their stats and their own respawn timers are as
they were, and Darkness Falls outside a raid (solo bots included) is unchanged.
- **What happened.** The raid started at 15:42 and killed Princess Nahemah (16:09) and Prince Abdin (16:40). It then
  walked its certified chain towards Prince Asmoien. Chthonic Knight Ukobat (level 66, aggro 500) stands on the raid's
  trail, the way its late members walk, and comes back about 3 minutes after each death: under the ten-minute minimum of
  the epic respawn hold, so he was not held. Each time he came back behind the raid the trail gate turned the whole raid
  back to him: a new muster (2 to 8 minutes), a kill, then the chain again, which reached waypoint 6 of 10 before he was
  back. He was killed 14 times in a row from 16:42:30 to 18:14:46, with no other named killed. After each kill the route
  also kept his name while it walked to Asmoien, so the raid's goal read "Advancing to Chthonic Knight Ukobat (waypoint
  6/10)" and the trail gate logged `TARGET_SWITCH from="Chthonic Knight Ukobat" to="Chthonic Knight Ukobat"` 12 times.
- **A knight the raid killed on its way stays down while the raid runs.** In Darkness Falls an epic the raid's route
  killed as its target (not a final, not a required add) is now held by the epic respawn hold whatever its respawn
  (`AutonomousRealmRaid.HoldKilledOnTheWay`, `RealmRaidRouteGate.HoldsRespawn`), and comes back once the raid has ended
  (the same end rules as the long-respawn epics). `REALM_RAID_EPIC_RESPAWN_HELD ... killedOnWay=True` logs each one. A
  knight killed by a solo bot keeps its own timer; the switch is the existing `realm_raid_hold_epic_respawn` (on).
- **The trail gate passes a knight it has cleared three times.** If one comes back anyway (the hold off), the trail gate
  turns the raid on it at most three times between named kills; then it logs `REALM_EXPEDITION_TRAIL_GATE_PASSED` once and
  the route goes on. It is still fought if it attacks the raid. `REALM_EXPEDITION_TRAIL_GATE` lines now log `kills=`.
- **The route names its chain target again after a trail-gate kill**, so the goal and the next switch say "Prince
  Asmoien", not the dead knight.
- **#358's epic-trash gate (W-1) does not touch Darkness Falls:** it is built only for a raid that walks through another
  dungeon, and every Darkness Falls raid enters region 249 straight from its realm (a test covers it).
- **Checker:** new row `rekill_loop` (a raid's route never kills the same named target 6 times in a row, 5+ re-kills,
  with no other target killed between). On `b1b6846` it is **Contradicted**: Ukobat, 14 kills in a row from 16:42:30 to
  18:14:46. Packs of trash with one name and encounter adds are not counted.

## Raid fixes from sweep 6f: rebased return waves, the summoners' musters, Summoner Pain's camps (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 15:15-16:46, `DIAG-b1b6-sweep6f`). Nothing below is seen in game yet.
No boss, guard or monster is changed: Summoner Roesia's script, Summoner Pain, her health and damage are as they were.
- **A rebased return wave stands at its step and fights** (W-2, Tur Suil and Trollheim). When a wave's front reached the
  raid, the raiders still catching up became the wave at the step they were walking to, not one they stood at, so the new
  core was not at its step: the wave waited (it waits before it looks at monsters), fought nothing in its way and logged
  nothing. Tur Suil's wave 1 (rebased to 10/27 at 15:38:03) sat silent 27.5 minutes while the Sargan muster held 24 of 33
  and the half rule pulled with 24; Trollheim's wave 4 (rebased to 15/33 at 15:10:54) was stuck with a core of 1, Bonchar
  was set aside and the raid timed out. Now:
  - the rebase puts the wave at the furthest step one of its catching-up raiders stands at (`RealmRaidReturnWave.Rebase`
    with `LatestStepAt`; the step before the one it walks to when none is known), and the rebased line logs `core=` and
    `standing=` (the new core standing at the step);
  - a catcher stepping on no longer counts as the wave moving (`RealmRaidReturnWave.Moved`), so a wave whose own raiders
    don't move is stuck after 4 minutes, is helped twice and is then released by #356's rule; while it is not moving it
    no longer holds its muster (`MUSTER_WAITS_FOR_WAVE`) either;
  - a waiting wave logs `step=waiting` at most every 2 minutes (living, at its step, needed, catching, how long, closest).
- **The summoners keep their 900 muster** (#358 side effect, Summoner's Hall). #358's home cap
  (`RealmRaidTetherReach.MusterReach`) moved Cunovinda's, Roesia's and Lossren's musters from 900 to 700 from their spawns
  (their 1,000 home is their damage reach), 100 outside their 600 aggro instead of 300, which invites the early pull that
  cost Cunovinda's attempt 1. The cap now applies only when the home radius lies inside the damage reach, or when it still
  leaves the muster 150 or more outside the aggro. Aidon keeps #358's 800; Kula, Tuscar, Caer Sidi and every other
  fight-at-home boss are unchanged.
- **Summoner Pain's split camps** (R-1, owner's decision 2026-11-18: "Add a split-camp tactic"). Roesia's Summoner Pain
  (1,000 around a random raider, bots included) hit 71 raiders for 124,913 damage in the first minute because the raid
  stood together. While a summoner who carries the spell is fought (gated on the spell, not her name; she is the only one
  in the three halls), the parties split evenly between two camps on opposite sides of her spawn, 920 out (inside her
  1,000 home, outside her 600 aggro, about 1,840 apart), with healers in each camp: each party's back line fights from its
  camp, its melee from her side facing that camp (`EpicEncounterTactics.PainCamps.cs`; `REALM_EXPEDITION_TACTIC
  tactic=summoner-pain/camps` logs the camps and who went where). A cast on one camp's back line now reaches its own half
  only; a cast on the melee still reaches all the melee and the camp they face. A deterministic model (`UT_RaidSweep6f`,
  72 raiders, 27 melee) gives 41.4 raiders a cast against 71.1 before: a little over half, not less. The owner asked for
  camps about 1,600 apart; at 1,600 the melee on her far side stand inside a cast on either camp (the model: about 50).
  **Removed in sweep 7b**: the owner's PC (build `9a173f4`) showed the camps failing (see the sweep 7b entry above).
- **Raid cures are logged.** This entry said bots already cured Summoner Pain; they did not while a heal was due, which in a
  raid under her Pain was always (fixed in sweep 7b). The cure path logged nothing, so `b1b6846` could not show it. Each region's raid cure poison casts are now
  summed once a minute (`RAID_CURE_POISON casts= curers= targets= dots=`).
- **Roesia's lore** no longer says GameBots are never Summoner Pain's centre: her script picks bots too (text only).
- **Checker** (`played_check_log.py`): a new row, `rebase_silent`: a return wave rebased to a step none of its raiders
  stand at (`standing=0`, or no `standing=` on builds before this) that then makes no step, fight or end of its own for 10
  minutes or more. Contradicted on `b1b6846`: Trollheim wave 4 from 15:10:54 (34.8 min) and Tur Suil wave 1 from 15:38:03
  (27.5 min), and three earlier ones: epic-midgard waves 1 (08:11:48, 25.4 min) and 2 (10:18:36, 25.8 min) and Krondon
  wave 1 (11:28:27, 79.0 min).

## Bot chat audit fixes (1.2, 2026-11-18; tests only)

The owner's chat audit (2026-11-18 08:10: "fully audit all bot language and chat design ... then make the fixes"). Nothing
below has been seen on the owner's PC yet: **tests only**.

### Hired companions, buffbots, system text, launcher and docs

- **A hired companion talks in one voice, the same as any gamebot** (owner 2026-11-18 09:14: "Companions should have all
  the same chat framework and structure as any bot you interact or group with, if not more"). Seen on build b1b6846
  (11/18 02:26 and 07:28): Pannax said "Say the word and I will follow, Testrunner." in /say and "heya, thx for the invite"
  and "nice xp, next :P" in /group. Now a companion keeps every gamebot part it had (grats, memory, group talk, fight
  calls, the "X went ld" line) and gains the rest: its death lines, rez asks and rez thanks, nemesis, the gamebot return
  lines, and tells (`/send` to your companion reaches it). Its own lines (`CompanionChat165`: follow, rest, power, health,
  level, quiet) are rewritten as a 2003 player types them and sent in its own writing style. **Assumption to confirm with
  the owner:** that voice is a normal per-bot chat voice; True 1.65's formal register is the companion's new **Speech**
  AI option ("Formal"), and a formal companion then says only its own formal lines (no gamebot chat), so it too has one
  voice. Never a roleplayer or a beggar (world characters). Server firsts: a companion counts, as any character (it never
  outlevels its player, so it can only tie them). /personality shows its personality and voice. The launcher's Living
  Server tab has a **Hired companions** switch (`hired_companions`, on) and the Thanksgiving choice
  (`bot_living_holidays_thanksgiving`: us, canada or off).
- **Conversation memory for companions** (with #350): the player's /say line is a turn of the conversation before their
  companion answers; the companion that has the floor answers a line that names nobody, and its answer is a turn that
  gives it the floor (`CompanionChat165.ChooseSpeaker`, `NoteSaid`).
- **The companion's own lines:** "keep quiet" is kept (no ambient line, an answer only when named, until told to talk
  again); the quiet and health words are whole words ("don't stop", "shut the gate" and "healthy" no longer trigger them);
  no danger line in a capital or town, and a city is named plainly; twelve idle lines instead of five, never the one it
  said last.
- **Words:** /spawn and /raid bots are "helpers" and /hire bots "companions" in every message and command text; "gamebot"
  for the world's bots; "No Lifer"; "they/them" for companions; one phrase for where to hire ("a healer of your realm"),
  with the number in every "/hire revive <n>" hint; "slots filled"; US spelling in game text (color, traveling, honor,
  defense, armor); `/hire help` in its usage; "stays behind", not "stays at the inn"; a dragon wipe notice says "where the
  raid gathered", not "the staging ring"; /lfg and /trade say they post to the realm.
- **GM and player command text:** /lstest names the real setting keys and says when a setting is server-only;
  /lscraft drops its work-package notes ("not registered on this server"); /lswhy skill and way read as words ("learns
  from what they do: yes; makes mistakes right now: no (on a realm raid)"), not the person key and refusal codes.
- **Transcript:** each bot line carries `kind=gamebot|companion|buffbot|helper`, and a companion's and a buffbot's lines
  their own class and level. Alliance chat is a `CHAT_LINE`; bots' emotes are `CHAT_EMOTE`; atmosphere lines, game-wide
  announcements and server firsts are `CHAT_SCREEN` (separate tags, so chat_audit's line counts are unchanged).
- **Settings text:** the launcher and the server now hold one text per setting (65 keys; 30 differed), without
  work-package notes, dates, "today's" or "towers"; body language no longer says bots can't sit. `release-server-settings.json` lists every key the Living Server
  tab writes (18 were missing, `bot_voices_chat_rate` among them), so a developer's saved value never ships.
- **ALL SERVER COMMANDS.txt** is written by `source/tools/make_command_reference.py` from the source (302 registrations;
  /hire, /lswhy, /personality, /invites, /lead, /lore, /tradewindow, /lstest, /lscraft and others were missing).
- **Docs made true:** voice shares 46/4/25/20/5 and the typo rule; the three-day no-repeat window; the chat budget's gaps,
  population exponent 0.65 (also the code's fallback), what the rate multiplies, bursts of at most three, the regulars and
  each bot's fixed talkativeness, and what the owner's PC delivered (5 to 7 /faction lines a minute on a full realm, the
  pace the owner chose; pacing unchanged); the release note's "/faction can get too busy" (an earlier build's result)
  replaced by build 5378dbe's; power rest floors 25/35/45; new bots' personality shares; a "Beta
  1.1 as shipped" banner on TALKING-UPDATE.md; README's "switch features on".
- Buffbot copies: on build b1b6846 (which has #334) seven buffbot logouts wrote no new roster row; the copies seen there
  were rows from earlier builds, which #338 retires. No change.
- Tests: `UT_ChatAuditCompanionsSystemText`, `UT_BotPeopleTables`, `UT_CraftEconomyTestCommand`, `UT_EggPolish12`,
  `test_release_server_settings.py`, `test_command_reference.py`; launcher `HeraldPageTests` (Windows CI).

### Replies, groups, guild, trade and crafters (fix/chat-replies-content)
- Replies that fit and promise nothing: no "omw" to an alarm from a bot that does not come, no "inv sent", no "heal me" from the
  group's healer, no thanks for advice after "no idea", no "hey all" to one player, no "o/". A bot's question about the player is
  closed as one (`ask_personal`), a ready check ("pull?") from a new `ask_ready` set, "who is main assist?" from `ask_who`; new
  sets for a price check, a brush-off closing and a ding at 50 (written; their routes are E1's and E3's).
- Class talk for every class in its realm (six lines or more each, the twelve missing classes included) and the other realms'
  view of it.
- Thin reply sets (farewell, back, away, ding, congrats, rez, abuse, ask_on, how are you, good news, mishap) to 20+ lines any
  bot can say; the core combat calls (pull, add, kill, rest, low hp, wipe, root, stun, oom, mez, taunt, group heal, rez) to 20+.
- Combat calls: no "clean"/"easy" after a groupmate died, power and med calls only from a caster under 40% power, "kill the other"
  only with two enemies; topics with no trigger (mez broken, healer oom) removed.
- Guild: every one of the 596 written guild lines can now be said: the ~400 that never passed were shortened to 8 words or
  fewer, keeping their content (owner, 09:14); in guild chat a short joke and the life words pass, a life line every 90 minutes
  instead of six hours. The login hello comes from the corpus and says "evening" only in the evening; "slow night" is no
  goodbye; the pre-voices formal fallback is gone with voices on.
- Crafters: "at my break in now" fixed (says where it is), crafter tells take the normal typing time, prices over a thousand
  gold in platinum, the price war is craft-neutral, more want-ad and commerce wordings; "crafter here" adverts from bots with no
  craft removed.
- `talking_audit.lines` split into the files of its sets (no line changed).

### Follow-up: event and scene lines clear the builder's checks (fix/chat-followup-events-scenes)
- Every 1.2 chat audit warning in the event files (storylines, dunce, deeds, news, rumours, holidays, spammers, roleplayers,
  beggars, milestones, memory) and the scene files (debates, gen_*, threads_long) is fixed, so those files now fail the build
  on a new hit (`chat_rules.STRICT_OWNERS` adds E4 and E8). Lines were reworded, none removed, and the scene count is the same
  (565).
- `{q}` ("quick question,") now goes only before a question: 113 openers got their question mark or lost the `{q}`.
- Time of day: "dinner" and "breakfast" lines say "a meal", "the table" or "after i eat"; the dunce's "pause the game, dinner
  is ready" scene plays only in the evening.
- No promise words where nothing acts ("on my way out", "ill be there for the next one", "i'll be there first"); "the
  {enemy} keep dying" reads as people, and four RvR thread beats name `{enemy_mod}` ("a hib stealther").
- Near-copies: a single line and a family with the same words got new wordings; the scene beats that shared 80% of their
  words with another scene's beat were rewritten; "real talk", "unpopular opinion", "playstyle" and "pics or it didnt
  happen" became 2003 wordings. The words the checks flagged that a 2003 player would use (autosplit, greaves, golem, pub,
  crauchon...) are in `vocabulary.txt`.
- `extras.lines`: "you kept me alive at {place}" was a thanks line for a "heal" topic the server never asks for; it is now a
  thanks for the group. "want to group again" needs `player_grouped`.
- File headers no longer say real swear words are not in the corpus (owner, 2026-11-18: swearing is fine, slurs are not).
- A debate piece already at its split limit takes no "lol" tail and no style emote any more
  (`AutonomousChatDebateSpeech.TailFits`, used by `AutonomousChatIdiolect.Apply` and `VC.Style`): a terse writer typed
  "gripklosa is nothing but mountains, trolls walked down from them, mystery solved lol", one word over what the split allows.

## Raid fixes from sweep 6e: Aidon's room, an attempt over his reset, epic trash on the way in, musters a wave walks to (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 13:31-15:15, `DIAG-b1b6-sweep6e`). Nothing below is seen in game yet.
Aidon the Archwizard's script (his out-of-tether heal is the original anti-kite rule), his tether, the xanxicarian
sentries and every other boss, guard and trash monster are unchanged; no fight is made easier.
- **The raid stands in Aidon's room** (S-1, Summoner's Hall, Albion). His muster stood 1,100 from his spawn, his
  fight-at-home edge; healers and regroupers stood 1,100-2,200 out. He followed them past his 1,500 tether and his script
  healed him to full eight times, the last one at 1% (14:18:56), which took away a near-kill. Now:
  - a boss with a catalog fight-at-home radius is mustered for inside it (`RealmRaidTetherReach.MusterReach`: the radius
    less 200; Aidon's muster about 800 from his spawn, still outside his 600 aggro);
  - every raider of that boss's fight (its target the boss or an add, a healer whose target is in the fight, or a party
    member in it) standing past the radius walks back to 200 inside it (`EpicEncounterTactics.StandInsideHome`, from
    `ThinkLeadHome`; a raider beside a boss that is itself inside its home is left alone);
  - while the boss is out of its home, "keep-alive add: fight the boss" yields to fight-at-home (the raider he chases
    leads him home, the others stand off inside his home). At 14:18:48 Eva and Wayne were sent back onto him out there.
  This holds for every boss with a fight-at-home radius (Aidon, King Tuscar, Queen Kula, Caer Sidi's three); for Caer
  Sidi's 2,700 radius the 1,400 muster is unchanged.
- **An attempt ends when its boss resets to full** (S-2). After the 14:18:56 leash the attempt stayed open for 21 minutes
  because the new illusions kept Aidon "in combat"; return waves walked into his room one after another and 85 raiders died
  298 times. An open attempt now ends (`how="reset to full"`) when its boss is back at 95% or more after 70% or less and is
  outside its tether, walking home or has an empty aggro list. It is judged as always (wipe, free reset or skirmish) and the
  raid regroups at the muster. A heal inside the fight (inside the tether, with aggro) is not a reset.
- **Epic trash with no way round on a raid's way in is a route gate** (W-1, Crystal Cave). The xanxicarian sentries (levels
  70-77, aggro 200) on the walk through Avalon City were walked past (`EPIC_AVOIDED action=pass`, 190 times): 37 raiders
  died to them during the raid and 57 after it. The route gate (`RealmRaidDungeonRoute.TryCreateGate`) now takes any
  hostile epic monster whose aggro covers the walk, not only a `GameEpicBoss`; a non-boss gates the walk only when the mesh
  has no way round it (the parties' own detour search). The raid clears them as objectives, and the epic respawn hold
  (#213, and #356 after the raid's end) keeps them down for the walk out. Epic monsters off the walk are left alone.
- **A muster is not set aside while a return wave walks to it** (T-1, Trollheim). The Storrow and Trollkarl Bonchar musters
  were set aside three times as "gained nobody for 20 minutes" while wave 4, up to 56 of 64 raiders, was walking to them
  through respawned trash; each flip re-planned the wave to a muster 15,000 away, and the raid made no boss progress for 90
  minutes. A muster stall now waits while a return wave of the raid walks to that muster and made headway in the last 4
  minutes (`REALM_EXPEDITION_MUSTER_WAITS_FOR_WAVE`); a wave stuck for 4 minutes is left to the stuck-wave release
  (#355/#356) and the stall applies as before.
- **A boss back inside its tether starts its next excursion with the full grace** (M-1). The stranded-leash clocks were
  cleared only on a leash, an engagement or a walk home; a boss that walked back in kept its old time and its next
  excursion was leashed at once. They are now cleared when a raider finds the boss inside its tether.
- **Checker** (`played_check_log.py`): four rows, all Contradicted on `b1b6846`: `tether_heal` (Aidon healed to full past
  his home 7 times, Queen Kula once), `reset_closes` (255 deaths to Aidon and his illusions after his reset at 14:22:35
  while the attempt stayed open), `way_in_epics` (37 deaths to the xanxicarian sentries while Crystal Cave ran) and
  `muster_wave` (5 musters set aside beside a wave with headway). `deaths_after_end` now also judges the raid's way-in region
  and counts the epics parties walked past and the required adds of bosses (209 deaths on `b1b6846`, was 107); the stuck-wave
  row no longer counts a wave stuck less than 5 minutes before its raid won (Crystal Cave's wave 2, 51 s).

## Galladoria's Earthmenders: mutual heal off by default, and a tactic that reads their heals (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 11:59-12:47, `DIAG-b1b6-sweep6d` D-1): the raid dealt the four
Earthmenders 1.44M damage in 48 minutes and none went below 99%. Owner: "Tactic first, then decide", then, with the
numbers below, **"Mutual heal off"**.
- **`epic_earthmender_mutual_heal` is now off by default** (`ServerProperties`, the release world's settings, where it is
  listed with the switches kept off, and the epic encounter catalog). The menders heal only the Earth primal, as they did
  from run 15 until 1.2 wave 2. A world that ran an earlier build moves to off once, only where its row still holds the
  untouched old default (`LivingServerSettingsUpgrade` 1.2.0.2); a value someone set is kept. The class puzzle, the
  menders, their heal spell and every other epic setting are unchanged. The launcher has no switch for it: it is a server
  setting. **Simulated with b1b6846's raid (tests only): all four die in about 10 minutes** (class rule, whole raid:
  Guardian 3.9, Natural 6.1, Magical 8.4, Shadowy 10.3 minutes; b1b6846's own split of attackers: 16.3 minutes). Turned
  back on, Galladoria is unwinnable for the bots (below).
- **How they heal (from the code).** Every 500 ms a mender that is not casting asks for a heal on every hurt NPC of its
  radius list that its rule allows (the Earth primal, or another mender; never itself), in the list's order. Its casting
  component starts the first and keeps only the last as the queued spell, so each mender heals the first and the last
  hurt one: two casts of 2,000 x 0.6-1.0 a round, 2.7 s each, about 33,000 a minute per mender. Four menders can heal
  about 130,000 a minute; the raid dealt them 31,300 a minute (Guardian 14,900 with 34 attackers, Natural 7,000 with 22,
  Magical 5,200 with 22, Shadowy 4,200 with 9), and all of its 118 raiders together could deal about 42,000.
- **The new tactic** (`EpicEncounterTactics.Earthmenders.cs`, with the class puzzle and mutual heal on): the raid watches
  the menders' casts and kills the mender the fewest heals land on; animists and valewalkers go there and its own class
  group stays on it. Half of each other mender's group keeps that mender hurt, so it draws its healers' first casts; the
  other half keeps the Earth primal hurt, so one of each mender's two casts goes to him (not while he is out of his
  tether: he takes no damage there). A dead mender's group fights the primal. The plan is logged as
  `REALM_EXPEDITION_TACTIC ... tactic=galladoria/earthmenders kill=... heals=... health=... assign=...` when it changes
  and once a minute. Each raider's pick is an `EPIC_TACTIC_BOT` line (`earthmenders: kill/decoy/keep the Earth primal hurt`).
- **Nothing the old tactic did was against the puzzle**: in the log no raider was sent to a mender its class can't hurt.
  What it missed: it spread animists and valewalkers by party instead of where they count, and kept only 8 raiders on
  the primal, so he was often at full health and the menders' second casts went to each other.
- **Simulated, not won.** `UT_EarthmenderTactic` runs the fight on the boss's own heal spell, heal rule and class puzzle
  with b1b6846's roster: the old split kills no mender in 120 minutes (as on the PC); the new tactic kills one in 5-10
  minutes, and then the other three hold. To kill all four, the raid would need about 2.0-2.75 times its damage with the
  new tactic and 3.5 times with the old split. Whether to change the encounter is the owner's decision.
- The four mender brains now ask `EpicEncounterRestores.EarthmenderHealsTarget` for the same condition they tested inline
  (a pure copy, so the tactic and the tests use the boss's own rule); the epic encounter catalog was regenerated and the
  seven encounters of `OlcasgeanAndPrimals.cs` re-stamped, their behaviour unchanged.

## Raid fixes from sweep 6d: a wave stuck in a fight, the sweep cap's label, the walk out after a raid (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 12:00-13:31, `DIAG-b1b6-sweep6d`). Nothing below is seen in game yet.
Bosses, epic settings, respawn timers and the Earthmenders (their heal and class puzzle, owner decision D-1, pending) are
unchanged.
- **A stuck return wave is released during a fight too** (G-5). #355 released a wave stuck with no help left only while
  the raid mustered. Galladoria's wave 10 ran out of help at 11:37:40 while the route fought the Earth primal (an
  `EARLY_PULL` Commit, not a muster), so it stayed: 45 of 117 raiders stood at its mid-air middle `38492,62016,12372` until
  the raid ended at 12:47:28. Such a wave now ends whatever the route is doing (`REALM_RAID_RETURN_WAVE step=released`,
  now with `mustering=`), and its raiders take the raid's own view: its muster, or its fight.
- **A sweep cap is "Sweep cap", not "Stopped by player"** (L-1). The play-test sweep ended Galladoria at its 6-hour cap at
  12:47:28 through the launcher's stop, so the outcome read "Stopped by player" although nobody pressed Stop. The sweep now
  ends a raid at its cap with the reason "Sweep cap: the play-test sweep's 360-minute limit." and the outcome "Sweep cap":
  as before no cooldown and no win or loss for the raid's record. The launcher's outcome filter lists it, the realm hears
  the expedition "is called off" (no "we gave up" line), and `played_check_log.py` reads it as "sweep cap". The launcher's
  Stop and the sweep's own stop and skip stay "Stopped by player".
- **Raiders walk out of a dungeon raid that ended** (P-1). After the sweep cap, 58 raiders stayed on the passive
  Earthmenders in the Earth primal's reach and he killed 59 of them by 12:59 (9 in the raid's own last 48 minutes). When a
  dungeon raid ends, each raider still in its dungeon drops a target that is an encounter monster (an epic, a boss with a
  tactic, or a required add) and is not hitting it, so its own goal walks it out; one the encounter is hitting keeps
  defending itself (`REALM_RAID_END_WALKOUT ... droppedTargets=`).
- **A raid's held epics wait while its raiders walk out** (P-2). Krondon won at 13:01:08; Lurfos the Herald and Drihten
  Elreden, held while the raid ran, came back on the next minute's recheck and killed 28 different raiders 34 times from
  13:01:47 to 13:04:38. The dungeon's epic respawn hold now lasts after the end until no raider of the ended raid is left in
  the dungeon, or 10 minutes, whichever is first (`REALM_RAID_EPIC_HOLD_RELEASED`). Then the epics respawn as always, with
  their full fights; respawn timers are not changed otherwise.
- **A required-adds phase with no progress is logged** (D-1 diagnostics, information only). The four Earthmenders took
  1.44M (6 times their combined 240,000 health) in 48 minutes and none died; nothing said so until the cap. When no
  required add has died for 20 minutes while the raid dealt them more than their combined health, the route logs
  `REALM_EXPEDITION_NO_PROGRESS region= boss= target= adds= health= dealt= minutes=` once for the phase. No behaviour changes.
- **Checker:** a sweep-cap outcome, and three rows: `adds_progress` (a required-adds phase with no add death for 20+ minutes
  while `raid_dealt` on the adds exceeds their health from the encounter catalog, or the new line), `deaths_after_end`
  (raider deaths to an epic within 10 minutes after their raid ended, in its region) and `stranded_at_end` (raiders' end
  positions at a `STRANDED_REGROUP` point with `ROUTE_RETRY` from it). All three are Contradicted on `b1b6846`.

## Raid fixes from sweep 6c: Galladoria's primals, a Krondon return wave, the raid-end stall (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 10:27-12:00, `DIAG-b1b6-sweep6c`). Nothing below is seen in game yet.
- **The Earth primal walks home once nobody is on his aggro list** (Galladoria). His script sets his speed to 0 while he
  chases a raider standing more than 900 from his spawn, and gave it back only when someone stood inside 900 or on a full
  reset; every leash then sent him home at speed 0. He stood 1,044 from his spawn, outside his 890 tether, through 13
  leashes in a row (10:51-11:12), took no damage there, and killed 177 raiders. Out of his tether with an empty aggro list
  he now gets his template speed back (a return begun at speed 0 is begun again), and `AutonomousTetherReset` walks any
  leashed boss its script left at speed 0 home at its template's speed (`AUTONOMOUS_TETHER_LEASH` now logs `speed=`). The
  freeze in the fight, his health, damage, tether and Earthmenders are unchanged.
- **No melee are sent to a primal outside its own tether.** The Fire primal's rule sent every melee raider to the nearest
  Water or Earth primal (1,735 retargets to the frozen Earth primal, each stopped at once by the tether rule). A primal out
  of its tether is skipped; with none left the melee fight the Fire primal in the fire, as they do when it is the last.
- **A target switch keeps the raid's front on the floor.** Both switches to the flying Air primal set the front to his
  raw position 690 above the floor (`front=38492,62016,12372 approach=raw`): 102 stranded regroups and the Fire primal's
  muster went there, 335 route retries came from it, and a return wave of up to 48 stood there for 37 minutes. When the
  approach does not resolve, a switch (the "on its way" switch and the switch to a monster fighting the raid) now takes the
  navmesh floor under the monster (`approach=floor`), and with none keeps the old front (`approach=unresolved-front-kept`),
  as the front refresh already did.
- **A stuck return wave is released to the muster.** In Krondon a wave stuck at step 14 of 34 from 11:28 with its help
  used up ("the stall clock decides") grew to 19 raiders while the Krevo Ricik muster stood at 6 of 44; the route's kills
  elsewhere kept the stall clock from deciding. While the raid musters, such a wave now ends (`REALM_RAID_RETURN_WAVE
  step=released`) and its raiders take the raid's own view; the stranded regroup still moves one that can't walk.
- **The raid-end sweep no longer stalls the server.** Right after a raid ended, the broken-party sweep ended all its parties
  at once under the coordinator lock (`pulse.broken_sessions` 1,914 ms, `SERVER_STALL worstTickMs=2060` at 12:00:08, 5 s after
  epic-midgard ended; 1,091 ms at 08:03 and 734 ms at 10:04 after the earlier raid ends). It now ends at most 2 parties a
  tick and goes on at the next tick from where it stopped. Every party still ends, with the same reason; no gameplay change.
- **Queen Kula's lore said her frozen circle and her drag to the throne were off** (and, before #341, the twin heal).
  All are on since 1.2 (`AllowPositionPuzzle`, `AllowDisplacement` and `AllowBotDisplacement` for QueenKula,
  `AllowTuscarTwinHeal`; on `b1b6846` 22 teleports and 97 step-outs of the circle). Her facts, legend and two chapter passages
  now say so, and the script comment the encounter catalog quotes ("no heal-on-hit for bot raids") was corrected (catalog
  regenerated with `epic_encounter_catalog.py`).
- **Investigated, not changed: the RvR relay share** rose from about 10% to about 14% after 10:00 (12.6% over the log, bar
  11.5%). The hop checks (a), (b) and (c) are 0 with `from=` on every relayed start; each relaying retreat reached more
  groups as the warbands' fights grew denser (0.49 to 1.10 groups a retreat), while a delivered relay started a regroup no
  more often. A legitimate rise of one hop, not an over-fire (`docs/PLAYED-CHECK-1.2.md`, the regroup row). The owner then raised the
  checker's bar to 15% (2026-11-18 18:19): one hop in dense warbands passes; the exact checks still catch a second hop or an echo.
- **Checker** (`played_check_log.py`): three new raid rows. "A leashed boss goes home" is Contradicted by 3 or more
  `AUTONOMOUS_TETHER_LEASH` lines in a row for one boss at about the same `fromSpawn`; "No regroup point at a monster's raw
  position raiders can't walk from" by a `TARGET_SWITCH approach=raw` front with 10 or more stranded regroups to it and 10
  or more route retries from it; "A return wave moves" by a wave whose `at=` step stays the same for 15 minutes (the
  stuck-wave row counts `step=ahead`, raiders leaving the wave, as headway). On `cb1b6.log.all` (`--build b1b6846`) all
  three are Contradicted: the Earth primal (13 at 1,044), the Air primal's front (102 regroups, 335 retries) and 6 waves.
  A released wave counts as no longer stuck in the stuck-wave row.
- Tests: `UT_RaidSweep6c` (the Earth primal's legs and the leash speed; no melee to a primal out of its tether; the switch
  front kept; the stuck wave released only while mustering with no help left; the capped sweep reaches every party) and
  `test_played_check_log.py` (the three rows).

## Raid fixes from sweep 6b: Fornfrusenen, Aroon, Easmarach (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 08:55-10:27, `DIAG-b1b6-sweep6b`). Nothing below is seen in game yet.
- **Fornfrusenen's retry raised no shards** (Tuscaran Glacier, 09:15:44): after the 09:01 wipe a few raiders kept his
  aggro list filled, so his full reset never ran. His two heals to full (the tether heal and the 30-35 s quiet heal) left
  the shards disarmed, and the retry fought him alone (dead in 2.2 minutes). A heal to full with **no shard alive** now
  re-arms him as the reset does, so the next pull raises fresh shards. A live shard is never removed mid-fight, and his
  health, the shards' health, their number (2 or 3) and the quarter each takes are unchanged.
- **Aroon's attempts were cut off between his Scaiths** (Galladoria, 09:04, 09:12 and 09:21: `never entered combat` with
  14, 35 and 19 fallen; he was then set aside as unreached). While the route fought a Scaith, the add fight never counted
  as contact, and the 120 s grace still ran from the `FINAL_ENGAGE`. A required add of the attempt's boss in combat is now
  the attempt's contact, and the grace runs from the last moment one was seen in combat (a separate field: "killed since
  the attempt began" still keys on the pull). The attempt is still judged 120 s after the last add fight, and a wipe still
  scores. The add fight is seen at the start of each route turn, so when the route judges an open attempt before it
  turns to another monster fighting the raid (#346), an attempt whose adds were fought is judged "left combat". In
  attempt 1 no Scaith was fought for 2 min 21 s after Taes died, and that gap is not covered: it still ends the
  attempt, but now as "left combat", which does not count towards "unreached".
- **Easmarach was set aside 9 s after he became the target** (`MUSTER_STALLED minutes=105`): his 07:29-07:34 muster
  record was reused 108 minutes later. A muster record that has not been looked at for a whole stall window (20 minutes)
  now starts a fresh count. A shorter interruption keeps the clock, as Krondon's Beran fix needs (#195 follow-up).
- **Not changed: Dra'argus set aside while fighting the raid** (Avalon City, 09:05:30). Both of his `TARGET_STATUS` lines
  around it say `inCombat=False`, and the set-aside check counts only encounters in combat, so a raider's pet as his target
  cannot be shown to be the cause. Left for a log with his add's target in it.
- **Checker** (`played_check_log.py`): two new raid rows. "A retry of a boss that raises adds raises them again" is
  Contradicted by a `FINAL_ENGAGE retry=True` on a boss whose earlier fight showed adds (an `addsSpawn` lore line, an add
  retarget or reposition, a `required-add` switch), when the retry ends with none of them and no damage to a known add. "No attempt
  ends "never entered combat" with 10 or more fallen" is Contradicted by such an `ATTEMPT_ENDED`. On `cb1b6.log.all`
  (`--build b1b6846`) both are Contradicted: Fornfrusenen at 09:15:44, Aroon at 09:04:49 (3 such ends).
- Tests: `UT_RaidSweep6b` (re-arm only with no shard alive, both heal branches, his numbers unchanged; add fight as
  contact and the grace from it; old muster records; Krondon's clock kept) and `test_played_check_log.py` (the two rows).

## Bot chat audit fixes (tests only): lore and tales (1.2, 2026-11-18)

Chat audit findings LORE-001 to LORE-029 and LOG-025 (owner's decision 10, 09:14: lore is never cut; stand-in storytellers).
None of this has been seen on the owner's PC yet.

- **A tale nobody told** (LOG-025, build 5378dbe: the realm hushed for Orylle's tale, the stand-in had just died and logged
  off five seconds later): a storyteller or stand-in must be able to stay (not leaving within ten minutes, not dead in the
  last two, a stand-in idle). A teller who leaves before its first line is replaced once, with a listener's line
  (`TALE_HANDOVER`); otherwise a listener closes it. The cut remark says "logged", not "pulled into a fight", and comes after
  any cut tale. The realm's own tale logs why it did not start (`TALE_NOT_STARTED kind=own`).
- **Requests and questions:** "tell" alone no longer starts a tale ("can someone tell me what spec..."); during a tale, trade,
  group, rez, travel and class questions go to the ordinary replies instead of "patience, i am getting there".
- **Lore is heard:** a question naming a place or boss can be answered with a written lore line (`data/chat/lore_*.lines`);
  a gamebot standing with the player in a place now and then remarks on it in /group or /say (placed lore, about two an
  hour at most, `LORE_PLACED`). Tales of a place (tales_more.lines) are asked for by the place's name.
- **Canon and world fixes:** no Govannon nodes (a New Frontiers change; the scene is now "sacrifice or demon first"),
  Summoner's Hall under the frontier tunnels, Arward sent long after Uther, Kula first (twin heal is on), Nosdoden's
  spirits, Xanxicar's lightning as hearsay, Behemoth's one-swing kill, levels and places per the world database,
  Golestandt's emotes say "she", "this autumn" for the islands; the lore bible's Kula and Olcasgean facts and briefings.
- **Craft:** every tale's teller says yes first, eight endings rewritten, apostrophes and lower-case coinages, four teller lines
  that answered a skipped question, six older tale scenes now half-remembered versions pointing at the storytellers, later
  slang replaced; `/lore` heading "What is said"; WRITING-GUIDE allows three short sentences in a scene beat.

## Sweep 6 fixes: Caer Sidi's Crypt Lord, the stall count, buffbot logouts (1.2, 2026-11-18; tests only)

From the owner's PC log of build `b1b6846` (2026-11-18 06:33-08:55). None of this has been seen on the PC yet.
- **A wipe is no longer dropped when the raid turns to a monster fighting it.** In Caer Sidi the Crypt Lord left combat
  71 s after an early-pull Commit while his court still fought; the route switched to a forerunner and the next muster
  cleared the open attempt unjudged, so 88 raiders' deaths (fight A, 07:26-07:28) were never scored. The attempt is now
  judged (REALM_EXPEDITION_WIPE or ATTEMPT_ENDED) before the TARGET_SWITCH; a second wipe ends the raid there.
- **The re-clear before the Crypt Lord's pull prefers a court monster it may pull now.** It took the nearest one, a
  reckoned soul on his patrol leg whose window never opened, and held on it for 20 minutes (07:38-07:59); the untimed pull
  after the lapse brought him in and ended the raid. A free court monster now comes first, and while the raid holds on one
  it takes another that is free (checked every 10 s). The 20-minute lapse is unchanged (the owner's call), and neither he nor
  his court is changed.
- **His court is fought at the raid's muster** (owner, 2026-11-18: "I err towards holding at the gather point"). In fight B
  a clean court pull came as a pack and the raid followed it to within 800 of his legs; he joined 109 s later. While the
  route fights his court from a muster and he is out of combat, a court monster in combat more than 600 from the muster and
  within 950 of his patrol is not followed: the raiders stand at the muster and it comes to them, for at most 60 s per
  monster. `REALM_EXPEDITION_COURT_LEASH` says when it holds the raid back.
- **The checker counts every stall minute.** `played_check_log.py` merged one minute's AI_PROFILE line into the next
  minute's SERVER_STALL (59 s apart) and reported 10 stalls, at most 9 in an hour, where the log has 14 and 13. The overnight
  row's definition is unchanged; on `b1b6846` it stays Contradicted because of the 13 stalls of the login burst (06:35-06:47).
- **A buffbot's logout writes no roster row.** `GameBot.RemoveFromWorld` saved every bot but the Special Forces; seven
  "0 rows affected" errors in `offline_world_bots` came with buffbot logouts. It now skips every in-memory bot.
- **A resurrecting bot reads its group once** (two NullReferenceExceptions at 07:08:17 in `TryResurrectGroupMember`).
- **A Savage buys on the Realm Exchange only a weapon it would wear.** The buy side asked the Savage weapon policy only;
  the no-undress guard asks the set score. Fre paid 39,863 copper for a "Lifetaker Greatsword" that scored 11 below the
  worn "Sturdy Greatsword" and stayed in the backpack (`ECON_UNDRESS_GUARD event="blocked"`). Both now use one rule.

## Bot chat audit fixes (1.2, 2026-11-18; tests only)

- **"omw" is true** (owner decision 2 of 2026-11-18: "Make it real for 1.2"; audit DESIGN-006, CORPUS-C-018, CORPUS-B2-047,
  LOG-019). `ChatPromises.TryPromiseCome` decides whether a gamebot can and will go now: alive, out of combat, not on a ride
  or carrying a relic, not held by a raid, a siege force, a battleground rally, a bot group or a player's group, not already
  promised elsewhere, not timid; for RvR an RvR gamebot of level 50 within 8 minutes of the keep (a porter hop counts), for a
  battleground one in that battleground and its bracket; a call for help only from a free bot within 4,000 units; heals only
  from a healer in your group; a rez as before (`WillComeRez`); "ill be there" to a raid time only from a bot already
  reserved for the realm's raid. Yes gives the bot a duty with a deadline (half again its travel estimate plus two minutes,
  3-15 minutes): an RvR bot's frontier turn takes the keep as its destination (its own realm's keep with the defence intent
  and a verified defensive post; another realm's keep only as a patrol near it), a free bot walks to you and fights the
  monster fighting you, and either stays three minutes. `CHAT_PROMISE made`, `kept` (arrived in time), `broken` (`why=timeout`,
  `died`, `taken`, `logged_out`, `keep_fell`, `asker_gone`, `duty_ended`) and `refused` (`why=`) lines go to the server log;
  `played_check_log.py` judges them (`chat` / `promises`, docs/PLAYED-CHECK-1.2.md). The replies that call the gate come with
  the other chat fixes. Tests: `UT_ChatPromises`, `test_played_check_log.py`.


### Scenes, threads and debates (corpus-b1; tests only, not yet seen on the owner's PC)

- **Live keep, relic and Darkness Falls scenes** (owner, 2026-11-18): a scene can now need a realm news flag
  (`defending`, `attacking`, `keep_lost_recent`, `keep_won_recent`, `relic_lost_recent`, `relic_won_recent`,
  `df_changed_recent`, `df_ours`). It plays only while that is true, comes before ordinary talk, may start 8 minutes
  after the last scene instead of waiting 30-45, and names the news's own keep, relic and enemy. 18 live scenes
  (`scenes_live.lines`): the nine that had been flattened into talk, rewritten so nobody claims to be at the keep or to
  be riding there, and nine new ones (any realm and each realm). The talk versions stay for the rest of the time.
- **Who plays a scene:** a scene with a band under 12 levels is cast inside it (Thidranki 20-24 was said by level 26-28
  bots on 11/18 01:36); roleplayers are no longer cast (their style rewrote a scene line as "... forgive me everyone,
  huzzah!", 11/18 02:15); roles that claim a class ability, a level or a craft now require it; the shared `{dungeon}`
  follows the first speaker's level.
- **Lines:** the audit's 8 replacement scenes and 155 line edits; no role letters spoken as names, no `{q}` before a
  statement, Cabalist, Healer and Shaman facts corrected, Darkness Falls no longer "throws you out", `/realm` and
  `/relic` answer the questions they answer, no enemy heard in /faction, clock times and dinner only in scenes with a
  time flag, live rez requests and train alarms turned into past events, group talk told as stories, no "inv sent",
  "we have a spot" or "omw" (nothing makes a scene bot travel), {enemy} used as a people word, contractions in the six files that
  had none, 22 of the 62 debates end on B's or C's line, the third copy of eight class questions retired.
- **Docs:** DESIGN.md section 3 matches the code; DEBATES.md is regenerated and CI checks it
  (`debates_doc.py --check`).

- **Field talk, market and realm event phrasing (engineer E2).** Nothing here has been seen
  on the owner's PC yet; every item is covered by `UT_ChatFieldTalkAudit12`.
- Out-of-era words gone from generated lines: "o/", the bare "rip", "dps" (now "nuker"), towers, "rn", "the map" (/realm),
  "/t" (/send), item links, count-kill quests; spec tips corrected (wizard and runemaster PBAE lines, theurgist, ranger).
- One realm word in two shapes ("hibs took nged", "hib zerg"), one name per keep ("beno", "bled", "ail") and per zone
  ("pennine mountains", "black mtns south", no full stops), used by every feature that names them.
- A defended siege is told as held ("hibs held nged"), never as a capture.
- Market: a buyer says what it pays, no "still have it?" seconds after an ad, no "what lvl?" to a post with a level, more
  ways to answer a group post, and a poster is asked at most once an hour. Exchange adverts are sales, never "pc x?", and
  the exchange has one name.
- Level 50s ask no beginner's questions; quest, hand-in and bind questions only in a home zone with towns; class plurals are
  words ("arms", "wizards"); "quick q" is always followed by its question; answers that made things up ("decent stuff",
  "by the inn", "nearest town", "not much") are gone; "did it" only from a bot of the quest's level; "saw" only from a bot in
  that zone; battleground sightings only for an asker in the bracket.
- Death openers: one a realm in four minutes, each wording once an evening, the cause named when known. Wipes are told only
  when half the party is down, where it happened, and as an enemy wipe only when an enemy did it.
- Town /say questions are questions and get answered; small talk has no orphan follow-up ("anyone for ?").
- Frames have three wordings each, one per bot; new true frames (realm rank, group at its camp). A real level-up may be said
  in /faction with its "gz" (`AutonomousBotChatCoordinator.TellDing`, once it is called from the level-up hook).
- Owner's decision 6: one /faction opener in five goes to the written corpus, whose short lines true where the bot stands
  pass every 90 seconds (the 20-minute rule stays for musings); a field line is said when none fits. Measured on a simulated
  4.6-hour Albion evening (`CHAT_WRITTEN_SHARE`, see the PR). Decision 12: about one /broadcast question in three gets one
  short answer in /say from a bot in range, at most one a region in three minutes.

## Crafter replies and the crafter roster (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 07:35, Testrunner and Leofrunn, an Assistant Weaponcrafter at 499):
- **"what do you make?" was cut mid-word** ("... in stock: fine alloy except"): every bot line is capped at 160 characters
  (`AutonomousBotVoiceCorpus.MaxLength`) and the cap cut wherever it fell. The summary now fits its item list and stock
  list, whole names only, to the room its longest line leaves; a material the names share is said once ("fine alloy
  archmace, bardiche, needle mace"); a stock list that does not fit is said as "1 piece in stock" and listed whole in a
  second tell. Any bot line still over the cap now ends at its last whole word with "..." instead of mid-word.
- **"can you make a fine alloy needle mace" was answered with a different piece from stock** (a fine alloy exceptional
  mace, which shares "fine alloy" and "mace") and the needle mace it makes was never quoted. A stock piece is now offered
  only when it answers the request at least as well as the best piece the crafter makes; otherwise the named piece is
  quoted as a commission.
- **"deal" on a stock piece an hour before the crafter's break** got "catch me at my next break and ill trade it to u",
  but the quote lapsed after 15 minutes and nothing was logged. The agreed sale now stands until half an hour after the
  break the crafter named, and (owner 2026-11-18: "Yes fix those issues") the piece is **set aside** for the player until
  then: the crafter does not sell it to anyone else (by tell, in a trade window or to a vendor), list it on the Realm
  Exchange or wear it. The hold ends when the deal lapses, when the player calls it off ("cancel", "forget it", "no") or
  asks for something else, or when the sale completes. Log lines: `ECON_PLAYER_ORDER outcome="stock_held"` (with `until=`)
  and `outcome="stock_released"` (with `why=` expired, cancelled, declined, replaced, sold or gone). The hold is kept in
  memory with the deal and the piece never leaves the crafter's bag: a crafter logout keeps the hold until it lapses, and
  a server restart drops the deal and the hold together, leaving the piece in the crafter's stock. No item can be lost.
- **When its break begins** the crafter holding a player's piece tells that player once, if they are in the game, where
  it is and to come and say yes (corpus topic `craft_break_ready`, five lines in `craft_commerce.lines`).
- **`/lscraft craft` printed only "Crafter roster: registered."** It now lists each realm's crafter gamebots: how many,
  how many are in the game, and those by name with their order, skill, level, zone and whether they are on a break or a
  task (12 a realm, best skill first). The command's help line says so.
- Tests: `UT_CrafterRepliesFit` (each case above, with every corpus summary and break line filled from the reply's slots) and
  `UT_CrafterCommerce` (a held piece is kept from another buyer, the trade-window goods and the Exchange; released when it lapses
  or is called off; the break tell comes once, only to a player in the game, only with an open deal).


## Bot chat audit fixes (1.2, 2026-11-18; tests only)

The voice layer, the corpus builder and the ambient corpus (chat audit E3; none of it seen in game yet):
- **Writing styles:** a tidy writer no longer capitalises or full-stops trade, group and call lines ("Wts 97% chain hauberk
  /send."); words expand at word boundaries ("where r u?" -> "Where are you?"); no capital after "..." or "!!". No laugh is
  added to a line that already laughs, a trade/group/call line, a line in capitals or a gold-spam ad, alarm, rez ask, death,
  news, combat call, roleplay or beggar line (the owner's PC: "SUPER CHEAP GOLD!!! ... N3T haha"); a storyline's lovers and a
  rage quit's cast say their beats with no "lol". Gold spam is said exactly as written. The terse style keeps a list's commas,
  "!!!" in capitals and a sentence break ("i do both, zerg when..."). Typos and "teh" never land in a class, monster, item or
  place name. A line too long once filled is skipped, never cut in mid-word.
- **Field talk, threads and market answers** take the speaker's writing style too, functional lines (wts, lfg, pc, inc)
  staying as typed (only one line in five had a style before).
- **The enemy:** `{enemy}` is the people ("hibs"), new `{enemy_mod}` ("hib stealther") and `{enemy_realm}` ("Hibernia"):
  no more "crawling with Albion" or "the Midgard took a relic". Alarms are picked by head count: "small group of" under 8,
  "or more" from 8, "zerg" only from 16 ("Hibs zerg at Breifine, 3 or more" on the owner's PC).
- **Season flags** winter, spring, summer, autumn from the PC's calendar for weather lines.
- **Ambient corpus (corpus-a slice):** no more sieges and keep news nobody checked (now on the real siege and keep flags),
  "still leveling" lines kept from level 50s, 43 broken word-by-word templates rewritten whole, leaked `@invites`/`@who`
  reset, newbie questions sent to the right answers (with new answers for "can i lose a level", "oom" and "pst"),
  `{monsters} at {camp}` only at a camp, class and place facts, out-of-era words ("toon", horses, food), time-of-day flags,
  untrue camp scenery, group and errand promises, Caledon, battleground rumours about one's own realm, near-copies, and 20
  short lines for level 50s.
- **Builder checks** (`chat_rules.py`, `banned.txt`, `vocabulary.txt`, `voice_style.py`): see docs/bot-voices/DESIGN.md
  section 6. They fail for the ambient corpus now and warn for the other files until their fixes land. Swearing is no longer
  banned (owner: "Swearing is fine"); slurs, sexual content and politics still are. `preview.py` and SAMPLE.md show the
  server's real styles.

## Bot chat audit fixes (1.2, 2026-11-18; tests only)

Reading and replies (the chat audit's E1 items; unit tests only, none seen on the owner's PC yet):
- **Read right:** "sorry guys dumb question", "ur not stupid", "my guild leader is an idiot" are no longer abuse that bots
  remember; "no ding yet", "he is afk", "dont rez me", "is the inc over", "im out of power" are no longer answered as the
  ding, the brb, the rez, the call or the goodbye; "stock up on arrows" is game talk; "albs inc from the south", "4 mids
  on the bridge", "rams on the door at crim" are calls; "ttyl", "bbl", "signing off", "bak", "back in 5" are read. On the
  design auditor's fresh 780-line set: positive lines 73.7% -> 85.4% (the held-out third, never tuned on: 71.4% -> 76.2%);
  lines kept clear of a wrong reading 60.7% -> 92.7%.
- **The group's questions:** "where we headed?", "what next", "whats the plan" have their answers; "ready?", "we pulling?",
  "is the healer oom", "how much power you got" are answered from the bot's real power and health.
- **Never lost:** a question without its "?" and a line that names a bot pass the reply cooldown; a named bot that is
  fighting answers when free, in /faction and /say; a tell waits 90 s for a busy bot, which says "sec, fighting" once; a
  quick second tell is queued; a reply to a tell or a named line is typed in 12 s at most; every dropped line is recorded
  for `/lswhy me` with a reason code.
- **No false promises:** every "omw", "coming", "ill be there", "healing" said to a player goes through one gate
  (`ChatPromises.TryPromiseCome`, the duty that sends the bot); a bot the gate refuses reacts instead ("how many?",
  "where are you?", "might make it").
- **Threads in a busy channel:** an answer finds the question it answers past other lines (said to the player, or sharing
  its words); an answer with other lines between names the asker; a closing after more than two such lines is dropped; a
  bot's question that bots answered is closed; a bot's written question ("frontier later?") is answered; a price check is
  answered as one, not as a sale; a closing after "no idea" is thanks anyway, and a tell's closing fits what the player
  said about themselves; bot alarms are not asked what they said.
- **Fairer answers:** thread answers spread over the realm's regulars (one rests 90 s after answering); a player's spec
  question gets the spec answers bots give each other.
- **/lswhy** reads plainly for players (codes, rules and scores for GMs); an event's reactions show as exempt, not held.
- **Conversation memory** (owner 2026-11-18: in 1.2): for five minutes a bot remembers its conversation with you, per bot and
  per channel: the turns, its open question, the place, item, class and level in play, and who has the floor. Your next
  line goes on with the bot you were talking to (not a hello, goodbye, call or group post); "how do i get there", "is it
  far", "how much for it" are completed from the last place or item; a bot never asks the level, class, place or item you
  already gave. Gamebots in /faction, /say, /group and tells; hired companions too (their /say lines, with the chat audit E7 entry).

## Bot chat audit fixes (1.2, 2026-11-18; tests only)

### Events: storylines, dunce, deeds, news, rumours, holidays, spammers, roleplayers, beggars, bonds, milestones, memory

From the chat audit of 2026-11-18 (corpus-b2 findings and the PC logs of build `5378dbe`). **Tests only: nothing here has been
seen on the owner's PC.**
- **Siege news follows the siege's stage.** The muster and march notices are new news topics, `siege_muster` (attackers)
  and `siege_warning` (defenders); `siege_attack` and `siege_defend` are said only once the battle has begun. No siege line
  claims a door, ram, oil, lord room or head count any more (the PC log had "outer door at ailinne is down" while the army
  was still mustering). Held and failed sieges no longer claim wipes or broken rams (they also end by running out of time).
- **"Wide open" keep rumours** are never told about a portal keep, a keep under siege, one taken in the last 30 minutes,
  or one with its owners within 3,000 units (all three of the night came minutes after a capture). Rumours name keeps and
  zones as chat did ("hlid"), like the news.
- **Deeds:** a drowning is its own deed (`drowned`), not "fell off a cliff"; a solo named kill needs 90% of the damage to be
  the player's (bots helping no longer count as solo); the awe lines need a yellow or harder named; a nickname is named only
  where it fits ("heard the name came from soloing" is never said of "the Clumsy"); RvR reputation talk starts at 50 kills;
  a bystander no longer says "our group just dropped"; silly-death counts say "silly deaths".
- **Storylines:** an arc keeps one keep for all its episodes, its side characters (roles C-H: the duel's referee, the
  trial's judge, the officiant) are the same bots in every episode, the guild split is cast from a bot guild's leader and
  officer when they are online, the romance's healer is a healing class, the side endings play only while the player who
  took the side is online, and time words fit episodes 3-10 hours apart.
- **The dunce** is one scene draw in eight, at most once in three hours per realm (it came about once in 16 hours).
- **Holidays:** "soon" and "plans" lines are said only before the day (`holiday_before`), trick-or-treaters only on the
  evening itself, New Year's day is January 1; "any holiday plans?" gets holiday answers; Valentine's, St Patrick's, April
  Fools, Easter and Midsummer have eight or more lines each. New setting `bots / bot_living_holidays_thanksgiving`: `us`
  (default), `canada` (second Monday of October) or `off`.
- **Roleplayers** name the enemy as a people ("the Hibernians", never "the Hibernia"), sword and shield lines are for armed
  classes, and a mock that quotes a word ("forsooth? FORSOOTH?") follows only a line that said it.
- **Memory:** the remembered place follows the group and is never a capital; group lines need a real group
  (`player_grouped`); a remembered player is not asked their level after saying it, nor promised an invite the bot would
  refuse.
- **Trophies:** a dragon trophy must be an item the bot's class can use; the cheers no longer say "worth the wait".
- Many smaller line fixes: grammar with the realm word, WoW loot words, phrases from after 2003, a beggar line of dashes,
  "you asked ME" to a player who asked someone else, out-of-era mechanics (keep map, boiling oil, water breathing), and dead
  lines no code asks for (enemy sightings, neutral and rival greetings, heal and trade thanks).
- Tests: `UT_ChatAuditEvents`; `UT_HolidaysAndLeeroy` updated for the before/after flags and January 1.

## Installer and launcher text: Living Server features are on by default (1.2, 2026-11-18; tests only)

Seen on the owner's PC on build `b1b6846` (2026-11-18 ~05:57): `INSTALL.cmd` ended with "Every new feature is OFF until
you switch it on", which has not been true since the owner's decision of 2026-11-16 (every Living Server feature on by
default). No default changed here; only the words.
- `INSTALL.cmd` (`tools/fork-build/install-server-build.ps1`) now ends with "Living Server features are ON by default. Their
  switches are in the launcher's Living Server tab (change them with the server stopped); diagnostics and test switches stay
  off."
- The package README (`tools/fork-build/README.txt`): "OFF UNTIL YOU SWITCH THEM ON" is now "ON BY DEFAULT"; XP by level and
  the classic rule and epic encounter options are listed apart as the player's choice; the sample `FORK_BUILD` line shows
  the features on; "SWITCHING FEATURES ON" is "CHANGING THE SWITCHES" and says how a world from an earlier build moves.
- The launcher's Living Server tab no longer says "Everything here starts off".
- Docs that still said the features start or ship off: README.md ("the manual install leaves them off"),
  docs/LIVING-SERVER.md, docs/BOT-GUILDS.md, docs/BOT-PERSONALITIES.md, docs/CRAFTING.md (trading with gamebots), and the
  summary comment of `AutonomousLivingServer`.
- Test: `source/tools/tests/test_living_server_on_text.py`.

The maximized launcher whose STOP SERVER / ENTER REALM clicks did nothing (same session) is most likely another window over
the buttons, not the launcher (no launcher cause was found; see docs/KNOWN-ISSUES.md). `LauncherWindowSizeTests` (tests only) lays the launcher
out at its normal size and maximized at 1080p, 1440p and 4K and checks with Windows' hit test that each header button is
the top-most control at its centre.

## Hired companions (1.2, owner 2026-11-17; tests only until seen on the owner's PC)

Ported from shadowofze's Offline DAoC True 1.65 (v0.7, `f68cc75`; github.com/zomboden/OfflineDAoC-True165), GPL-3.0, via
True165. **Tests only: nothing here has been played on the owner's PC.** Setting: `bots / hired_companions`, **on** by
default (owner 2026-11-16: Living Server features default on for installer users); off, nobody can hire and no hired
companion is summoned, and the rows stay.

- **Hire up to seven companions of your own** beside a healer of your realm (every capital and major town has one): talk
  to the healer and click **[companions]**, or type `/hire`. Slots open at levels 5, 10, 15, 20, 30, 40 and 45; the first
  companion is free, the others cost 20 s, 60 s, 2 g, 6 g, 15 g and 30 g (True 1.65's prices). You pick its race, class,
  sex, classic look, hair colour, height and name; a figure only you see shows the look. It joins your group at level 5
  with a training weapon and nothing else.
- **It is yours.** It levels only beside you, at your XP rate, and never passes your level. Its gear, bag (10 slots),
  coin and realm points are its own and saved. You train its specs and buy its realm abilities; respec is free. It leaves
  with you when you log out and comes back when you log in; dismiss it, summon it again beside a healer, or release it for
  good (it leaves with everything it carries). Dead, its body waits five minutes for a raise, then fades; any healer of
  your realm brings it back free.
- **The original client's own windows**, all server side (True 1.65's original-client overlay was never published): the
  healer's and the companion's dialog links, the stock text window for the roster, stats, specs, realm abilities and AI
  options, the house-vault window to hand it gear (drag from your backpack, or off it to take items back), and the
  champion trainer window for its specs. Right-click your companion for its links; `/hire help` lists the typed words.
- **AI Options** (`/hire options <n>`, `/hire option <n> <row> <choice>`): Role "Melee and heal" (healers), Attack spells
  and Save power Strict (casters and hybrids), Bolts "Nukes only", Debuff before nuking "Never" and each debuff Use or
  Skip, Life transfer, Amnesia, the Hunter's pet, charming, Crowd control Off, and a Stance of its own (or your
  /aggressive and /defensive). Read where the companion casts and where its stance is decided; every other bot reads
  Automatic. True 1.65's other rows steer AI that 1.2 does not have and are not offered.
- **Repairs:** a smith repairs the gear of the companions with you in one go, at the smith's price under `player_upkeep`
  (True 1.65: free). **The economy ledger names both new coin moves:** the hire cost (`companionhire`, a sink; the
  launcher's economy page calls it "Companion hires") and a companion's repairs (`repair`), so the residual stays 0.
- **Kept apart from the world's bots.** A hired companion is never in the gamebot population count, the planners'
  roster, raids, guild groups or world chat; the population never logs one in or out; the stuck watchdog never moves
  one. Like a player-led group, it is outside the open-field RvR tactics (RvrTacticsScope: no stances, calls or lookers;
  enemy bots notice it by the player rule), and it is no trader, crafter or craft-order buyer for the bot trade (only the
  hire and the smith's repair move its coin). `/spawn` and `/raid` are unchanged. The launcher's DELETE never deletes a player's companion (DELETE ALL keeps
  them and deletes only orphans whose character is gone); a release build empties `offline_hired_companions`
  (progress-policy.json, checked by build_release_037.py); the Passport keeps them in their world.
- Not yet: True 1.65's traveler's inns and innkeepers (they need a base-game world update), the courier, the Horn of
  Rallying, its 1.65 mode rules. The hire window's DebugMode 'H' protocol is kept byte for byte, but sent only to a
  client that asks for it (`/companionui protocol`); no client does yet, so nobody gets 'H' packets.
- **Fix: a companion joins its player's fight on a grey monster** (seen on the owner's PC, build `b1b6846`, 2026-11-18
  07:23-07:33, Isle of Glass; the fix is tests only). Hired Armsman Pannax (level 5) stood idle while his player
  (Fighter 22) killed three level 1-2 monsters ("headless dead", "urchin quib"), with stance Aggressive too. The bots'
  shared pull gate (`BotBrain.CanAggroTarget`) refused every monster grey to the bot itself, the player's own target
  included, and a monster that never hits back is never defended against. Now a bot in a player's group (hired,
  `/spawn` or a gamebot) takes the monster its player, or the player's pet, is attacking whatever its con; a grey
  monster the player is not fighting is still no pull of the bot's own, and Defensive still keeps it beside its player.
  Not a GM effect: no rule here reads the player's privilege level. `UT_PlayerLedPullCoordinator`.
- **Fix: "nice xp, next" only after a kill that gave experience** (same session: Pannax said "nice xp, next :P" after a
  grey kill that gave nobody experience). The group's end-of-fight call (`AutonomousBotCombatCalls`) remembers whether
  the group's experience grew during the fight; a line about xp is said only then, the plain ones ("gj", "nice") after
  any kill. Tests only. `UT_TalkingUpdate`.
- **Log lines** for the owner's PC check: `HIRED_COMPANION` (hired), `HIRED_COMPANION_SUMMONED` (why=hired, summon or
  revived; grouped=), `HIRED_COMPANION_DISMISSED`, `HIRED_COMPANION_REVIVED` (body=lying or faded),
  `HIRED_COMPANION_LOGOUT` and `HIRED_COMPANION_LOGIN`, `HIRE_WORD_165` (every /hire word or dialog link), `COMPANION_SKILLS_165`, `COMPANION_STATS_165`,
  `COMPANION_OPTION_165`, `COMPANION_REPAIR_165`.

## 2.0: crafting and the economy

In development on `integration/2.0`. The plan is [docs/CRAFTING-2.0-BUILD-PLAN.md](docs/CRAFTING-2.0-BUILD-PLAN.md);
the design is [docs/CRAFTING-AND-ECONOMY.md](docs/CRAFTING-AND-ECONOMY.md) (build status in its §15). Each package adds
its own block below. Nothing here has been played; every result is from automated tests.

### WP0 Foundation

Groundwork only: **nothing a player or a bot does changes**. Every new setting is declared but not read yet, every
seam's default is today's code, and the new tables are created empty.

- **The craft's numbers as pure functions.** `CraftRules` (con, chance to make, chance to gain with the capital bonus,
  crafting time, quality, secondary-skill rules) and `SpellcraftRules` (imbue points, gem points, overcharge, preserve)
  are copied line for line from `AbstractCraftingSkill` and `SpellCrafting`, so a gamebot will roll exactly as a player
  does. Nothing calls them yet (WP1 does). Tests compare them with the originals over grids of skills and levels,
  including the quality roll's distribution and the 32/28/18 imbue points of a level-51 piece. The same split as
  rambodolphin13's DividedRealms refactor (93c25b69), as static functions.
- **Contracts and seams for the later packages**, so they register code instead of editing the shared files: listing
  caps and item rules for the bot economy, expired-listing reclaim, seller proceeds (the tax), a sale-committed event,
  town-visit needs, merchant and Exchange steps, task-break activities and hooks, store lifecycle, drag-and-drop onto
  bots, and coin-flow attribution on `AddMoney`/`TrySpend` (a kind and the caller, no call-site edits).
- **Six tables** (`offline_bot_types`, `offline_bot_crafters`, `offline_bot_craft_orders`, `offline_exchange_index`,
  `offline_economy_ledger`, `offline_crafter_customers`), saved progress that stays in its world: in the progress
  policy (release builds empty them), the Passport's table map and the Passport guide.
- **Every setting of 2.0** (`CraftingEconomyProperties`). Bot-side switches are off by default (`bot_crafters`,
  `bot_craft_orders`, `bot_upkeep`, `bot_luxury_spending`, `bot_siege_supply`); the Exchange tax is built with a default
  of 0, 5% being the documented option (owner, 2026-11-15: until the ledger shows a need); new Crafters are 7% of new
  bots only.
- **The `CRAFT_*` / `ECON_*` log format**, five AI_PROFILE scopes, and the voice registry's new kinds, flags, slots and
  the `crafting` intent (no lines yet; `corpus.json` unchanged).
- Ported contracts: the crafting-skill storage format and the craft-order table's columns from rambodolphin13
  (93c25b69, ceac061a), via DividedRealms, GPL-3.0; the coin-flow kinds and the `crafting_skill_rate` key from
  shadowofze (a8c793e, 00f9fbf), via True165, GPL-3.0.

### WP1 Crafting core and the M0 fixes

For **real players** on the original client; gamebots and companions craft nothing yet (WP4). Tests only: nobody has
crafted with this build in game.

- **One set of crafting rules.** Every number of a craft (con, chance to make, chance to gain with the capital bonus,
  crafting time, quality, secondary skills and their required levels, salvage yield, repair chance) now comes from `CraftRules` and
  `SpellcraftRules`, which WP0 copied from the old code; the old methods are one-line delegations, so a gamebot crafter
  will roll exactly as a player does. DividedRealms' `ChanceToMakeAt`, `BaseChanceToGainAt`, `QualityAt` and
  `CraftingTimeAt` stay as delegations, so its bot crafting ports unchanged.
- **Player crafting pace** (`crafting_skill_rate`, default 1 = classic): a won skill gain gives that many points to the
  trade and its secondary skills, never past a cap (secondaries stop at 1,300); the gain chance and the grey rule are
  unchanged, and a gain of several points no longer steps over a crafting title (announced from the crafts' gain code,
  not `GamePlayer`, which is WP3's). From shadowofze (00f9fbf), via True165, GPL-3.0.
- **Classic options, both off by default.** `crafting_classic_quality`: 94-99% with a 1% masterpiece chance at any skill
  (2% at 1000 or on a grey recipe), the pre-1.87 roll; the odds are from memory of the live game, not a dated source.
  `crafting_classic_losses`: a failed craft uses up its materials, and a spellcraft explosion destroys the item as well
  as the gems.
- **Repairs and the upkeep setting** (`player_upkeep`, default light, the design's decision 5): only the classic upkeep
  spends durability on a repair; light and free restore the condition without wearing the item out. **This is the one
  default a player can notice**: with the default, repairs stop spending durability. `player_upkeep classic` keeps the
  old rule. The smith's price is WP3's.
- **Spellcraft fixes from upstream OpenDAoC.** An item can no longer take a fifth bonus (the count check let one through),
  and focus gems go on staves only. From Baptiste Marie (b62885a86, aa8b5232b), via rambodolphin13's DividedRealms
  (a3f3e4b8, 60d3a4fb), GPL-3.0. The combine check is now `SpellcraftRules.CheckImbue`, the same for players and bots.
- **Salvage fixes from upstream OpenDAoC.** Null checks in the salvage timer, siege weapon salvage on a 10-second timer
  that the weapon must survive, materials topped up into partial stacks; merged by hand with our own fix that a magical
  item counts as cloth only for the calculation (c7ee233d0). Ours on top: a salvage whose materials do not fit in the
  backpack drops the rest on the ground (the item is already gone), and an item with no craft level no longer divides
  by zero. From Baptiste Marie (5302c79e9), via OpenDAoC, GPL-3.0.
- **Crafting answers instead of failing silently.** Another realm's recipe says why it cannot be made (rambodolphin13,
  abe7b327, via DividedRealms, GPL-3.0); an unknown recipe says so (fc6d2a95 via DividedRealms, from shadowofze's
  4bf3b8f3e via True165, GPL-3.0; M0 had already made the lookup safe).
- **The recipe catalog.** At start-up every recipe is loaded once in the background, ids matched without case as the
  server matches them, each hidden by the server's own rule (M0's `RecipeDB.FindProblem`), then indexed with the
  masters, forges, lathes, alchemy tables and the merchants of each material. The log gets one `CRAFT_RECIPE_AUDIT` line
  per craft plus a total, a `CRAFT_RECIPE_BROKEN` line per hidden recipe and `CRAFT_WORLD_AUDIT` lines per realm
  (DividedRealms' `AUTONOMOUS_CRAFT_CATALOG` line is still written). Nothing reads the catalog yet; WP4's bot crafters
  will. From rambodolphin13 (93c25b69), via DividedRealms, GPL-3.0.

### WP4 Bot crafters (M2)

**Off by default** (`bot_crafters`): with the switch off no gamebot crafts, no type is stored and the server behaves as
before. It also does nothing until WP1's recipe catalog is registered (with no catalog no trip starts). Autonomous
gamebots only: companions (`/bot`), Epic Special Force visitors, beggars and real players never craft through it.

- **Crafter gamebots craft for real.** At a natural task break an ungrouped Crafter-type gamebot (level 5+) may make a
  craft trip, the break's new `C` letter: it joins an order at a real craft master of its realm, buys the recipe's
  materials from a real merchant with its own coin, walks to a real forge, lathe or alchemy table and works there.
  Every attempt rolls as a player's craft does (`CraftRules`: chance to make, the 1.87 quality roll, chance to gain
  with the capital bonus, the crafting time with relics, keep bonuses and `crafting_speed`); a failure costs nothing,
  a success takes the materials and makes a real item (`IsCrafted`, `Creator`). It wears an upgrade; the rest goes to
  the inventory stop that ends the trip.
- **Only the best of a session is posted.** A session's best piece (a masterpiece, else the highest quality) waits in
  the bag for a Realm Exchange post and is worth a post whatever it fetches; the session's other pieces go to a vendor.
  Only a crafter's own crafts' materials count as materials, so it no longer hoards every stack. The best piece may be
  enchanted first by the realm's best spellcrafter and alchemist gamebots, paid a 10% fee, within the item's imbue
  limit and the spellcrafter's skill (no overcharge, nothing explodes), with real tinctures.
- **Orders by realm weights.** A new crafter joins the order furthest below its share of the realm's crafters
  (armorcraft 24%, tailoring 22%, weaponcraft 18%, spellcraft 14%, alchemy 12%, fletching 8%, siegecraft 2%); within
  half a crafter, DividedRealms' class rule decides (archers fletch, list casters tailor). An order with no master in
  the realm is skipped and logged once (siegecraft has none in our world); one whose master stands only in another
  town (Albion alchemy, Midgard spellcraft, Hibernian tailoring, M0 findings §2) is joined there.
- **A pace that keeps up.** DividedRealms' night of 2026-11-04 left the median crafter at skill 8. A Crafter now makes
  a trip at half its task breaks, and a trip works up to 3 sessions of up to 10 pieces in its first 15 minutes, cut to
  the bag's room and the time left; with `bot_crafting_pace` `xp` (the default) a gain gives as many points as the bot
  XP multiplier of the crafter's level band (whole points; `classic` gives one). On the pure rules DR's night comes out
  at a median of 6, ours at about 60 at `bot_xp_rate` 1 and about 300 at 5 (tests only: a night is assumed to hold
  three trips).
- **Stored types; 7% for new bots.** The first start with `bot_crafters` on stores every bot's personality type as it
  was (`offline_bot_types`), so no existing bot, beggar or voice changes type; a bot created after that is typed by
  the share table with Crafters at `bot_crafter_share_percent` (7) and stored. A stored type never changes again.
- **Titles and the Legendary first.** A crafter reaching a title (each hundred up to Legendary at 1000) logs it and
  sometimes says it in /guild or /say; the first Legendary of an order in a realm claims "First Legendary <craft> of
  <realm>" through the server firsts (with `bot_living_firsts` on), the same title a player can take.
- **True /trade adverts.** Now and then after a session a crafter advertises the piece it really made and still holds,
  with its real quality and the price it would ask; the stock is checked again when the line is said, and the realm's
  /trade budget applies (`bot_crafter_chat_rate`, 0 is silent). New lines in `crafters.lines`.
- **Station hangouts.** On a town break a crafter gamebot spends some of its wanders at its craft's station in town.
- **Bounded work:** server-wide at most 4 session plans and 8 attempts a game-loop tick; a refused bot tries again on
  its next turn. A skill gain holds a short per-bot lock (an enchanter gains from another bot's turn).
- **Logs:** `CRAFT_ENROLL` (joined, skipped, typed, types_began), `CRAFT_SESSION_START`, `CRAFT_SESSION_END` (attempts,
  made, masterpieces, best, seconds, enchant), `CRAFT_TITLE`, `CRAFT_FIRST`, `CRAFT_MADE` (only with
  `craft_made_lines`); `AUTONOMOUS_TOWN_HANGOUT` counts `stations=`.
- Ported: the crafting and the craft trip from rambodolphin13 (93c25b69, ceac061a, e37f8810), via DividedRealms,
  GPL-3.0; "list only the best of a session", the siege kit tiers and the points per gain from shadowofze (b097caf,
  75267dd, 00f9fbf), via True165, GPL-3.0; the enchanting from shadowofze (5f31c2b, a8c793e), via True165, GPL-3.0,
  on our `SpellcraftRules`. Ours: the Crafter-only gate, stored types and the 7% share, realm weights, the pace and
  sessions per trip, titles and the first, the adverts, station hangouts, the tick budgets. DividedRealms' quest runner
  is not taken. Tests only: nothing here has been seen in game.
### WP2 Economy ledger

Instrumentation only: **no amount changes**; tests only so far. Until now no log line recorded a gamebot's coin
coming in or going out (M0); this makes coin per hour measurable. Guide: [docs/ECONOMY-LEDGER.md](docs/ECONOMY-LEDGER.md).

- **Every gamebot coin move is counted by kind, per realm and level band**, with no call-site edit: the caller's file
  and member (WP0's caller info on `AddMoney`/`TrySpend`) name the kind (loot coin, loot share, vendor sale, vendor
  gear, stable ticket, frontier medallion, repair kit, siege, dye, beggar gift, Exchange credit). A refund counts
  against its own spend. A new call site that names nothing is listed by name in the hourly line, and a unit test
  scanning the source fails until it is named.
- **Realm Exchange sales** are counted from the sale-committed seam: sales, value, tax, crafted pieces, and what bots
  and players paid and received.
- **An hourly snapshot of the money supply per realm** (bot purses, player wallets, unclaimed Exchange proceeds, guild
  banks, plus the Exchange's listed value), read on a background task, with a residual = change in supply − the flows
  seen, and the bots' own residual. Bots that appear or go bring or take their purse.
- **Lines**: `ECON_SUPPLY` and `ECON_FLOWS` each UTC hour for each realm and All (True165's `in_<kind>=copper/count`
  keys kept, and True165's own `BOT_MONEY_LEDGER_165` line once an hour as an alias), a baseline at start, a partial
  hour at shutdown; one `offline_economy_ledger` row per realm and hour.
- Setting `economy_ledger_enabled` (on; logs and one row an hour per realm only).
- Ported: the money ledger from shadowofze (a8c793e, df86f9f), via True165, GPL-3.0; its test adapted. Ours: the
  classifier, per-realm and per-band sums, the supply snapshot, the residual, the Exchange counters, the table rows.
### WP10 Economy rehearsal and diagnostics

Test and GM tooling only: **nothing a player or a bot does changes**. Written new (no fork code).

- **The economy rehearsal** (`source/server/Tests/Rehearsal`): plays 1–3 realms of gamebots (1,000 a realm by
  default) in one-minute steps, a week in 10,080, on the server's own pure code: today's equipment value, listing
  price, vendor appraisal, listing cap and Exchange seams, and the registered price index. Hunting, drops, shopping
  and listing follow M0's measured rates (`fixtures/calibration.json`, every assumed number marked); recipes are the
  0.37b world's 8,479 (`fixtures/recipes.json`, made by `source/tools/export_recipe_fixture.py` from a read-only copy
  of the world tables only, never player data). Selling, listing and buying happen only at the natural task break.
  Each package adds its own adapter (`Tests/Rehearsal/Adapters/WPn*.cs`, found by reflection); until WP4 a stand-in
  crafts with `CraftRules` at player pace when `bot_crafters` is on in a scenario. A setting no adapter reads yet is
  reported as "not built yet" and the run uses today's behaviour.
- **The plan's gates**: no negative purse; the flows equal the change in supply every hour, exactly; Bid < Ask in every
  band; supply growth within a band; no undress; the median order wait (not applicable until WP5). Crafter against
  hunter wealth is reported, not asserted. `UT_EconomyRehearsalSmoke` runs one day in CI; the week scenarios
  (`UT_EconomyRehearsalWeek`, `[Explicit]`, `ECON_REHEARSAL_OUT`) write a readable ledger per scenario: markdown,
  CSV and a log of the server's `ECON_SUPPLY` / `ECON_FLOWS` / `CRAFT_*` / `ECON_REHEARSAL` lines.
- **`source/tools/econ_timeline_report.py`** summarizes those lines from a server log or a rehearsal ledger: supply and
  its residual per realm, flows by kind, crafting sessions, titles and firsts, gates, and an optional direction check
  against a `persona_sim.py` result.
- **`/lscraft craft | wtb | commission | ledger | index | crafter <name>`** (GM): the switches, what each part has
  registered, the online gamebots' coin by realm, the price index's floor/fair/ask/bid for the first backpack item,
  and a gamebot's type, crafting skills and purse. Parts not merged answer "not built yet (WPn …)"; a package that
  lands registers its own answer (`CraftEconomyTestHooks`). `/lstest craft | wtb | commission` delegates to it.
- Rehearsal results are tests, not in-game results; every ledger names its build (`ECON_REHEARSAL_BUILD`).
### WP3 The player's side (M1)

For real players only; gamebots and /bot companions craft nothing and are unchanged (a companion's kill only counts
for its player's coin). Automated tests only; nothing here has been played. Player guide: [docs/CRAFTING.md](docs/CRAFTING.md)
(crafting works in the original client only for now).

- **Era start** (`crafting_era_start`, on): a new character joins a crafting order at a master, as in 1.65, instead of
  starting with Basic Crafting and every skill at 1. Only a crafting record created from now on (an account's first
  character in a realm) starts without an order; **an existing record keeps every skill and its order**, whatever the
  setting. With era start on, the Basic Crafters Master and siegecraft take no new members (not orders in 1.65) and
  say where to go; every one of the six orders has a working master in both capitals of each realm.
- **The masters M0 counted as decorations work.** 56 of M0's 57 "decoration-only" masters (ClassType `GameNPC`) are
  made into the master of their title by the guild-script rule (`use_npcguildscripts`, on by default and in the shipped
  world), among them Camelot's alchemist, Aegir's Landing's spellcrafter and Domnann's tailor; M0's audit read the
  ClassType column only. The one left is Domnann's Siegecrafters Master (no class has that title). No world data is
  changed; the server log warns at start-up if era start is on and the guild-script rule is off.
- **Fixes the era start needed:** joining an order never added its skills (`AddCraftingSkill` tested the wrong way and
  would have thrown for a held skill; no character lacked one before), a gain in a skill never trained threw
  `KeyNotFoundException`, and crafting a recipe of an untrained skill now says so. A record with skills but no primary
  plays as Basic Crafting instead of losing its skills on the next save.
- **Secondary caps** (`crafting_secondary_caps`, off): another order's craft stops at 75% or 40% of the primary, from
  the masters' own descriptions and a 2002 player report; material skills are never capped and no skill is lowered.
- **Repair upkeep** (`player_upkeep`, light): the smith's price, half (rounded up) under light, nothing under free, the
  old price under classic; WP1 already makes light and free repairs spend no durability.
- **Coin by level band** (`player_coin_by_level`, on): a real player's monster coin (bags and chests) is multiplied by
  their XP rate for their level when that is above 1, so a level is worth the classic coin at 10x XP; never lowered.
- **Titles**: no Legendary Grandmaster (1000 and above is Legendary) and no Basic Crafting title. From Baptiste Marie
  (bm01), upstream OpenDAoC `7b0df1b12` and `ad789ee4d`, GPL (the commit's dead `CRAFTER_TITLE_PREFIX` and its
  `SiegeCrafting` refactor are in WP1's files and not taken here).
- **Launcher:** a Crafting card in the Living Server tab for era start, crafting pace (`crafting_skill_rate`, 1-10),
  repair upkeep, coin by level, secondary caps, classic quality and classic losses, saved and read like the tab's other
  rows (the pace and the two classic options are WP1's rules; this adds their rows).
- Log lines `CRAFT_ERA_JOIN` (a record started without an order, an order joined) and `CRAFT_PLAYER_TITLE` (every
  hundred to 1000). Tests: `UT_PlayerCraftingRules`, `SocialExpansionSettingsTests.CraftingRowsAreSavedReadAndChecked`.

### WP5 Market (orders, price index, Exchange, tax, commissions)

**Off by default**: the bots' own orders need `bot_crafters` and `bot_craft_orders`, the realm commissions, the flood
cap, the crafters' extra posts and the reclaim need `bot_crafters`; all are off, so the server behaves as before. The
Exchange tax is built at **0** (`exchange_tax_percent`; owner 2026-11-15: 5% is the documented option, only once the
ledger shows a need). Autonomous gamebots only, except the tax, which a real player pays only when they sell on the
Exchange. Everything happens at a natural task break.

- **Craft orders between gamebots.** At a task break a gamebot that a crafted armor or weapon piece would upgrade (up to
  20 levels below it, within half its coin) pays a Crafter gamebot of its realm who makes the recipe at orange or
  better and whose task ends within 45 minutes (or who is on its craft trip): the least busy, then the soonest, then the
  most skilled, at most 2 orders each. The price is the materials and a fee of 25% (at least 1 silver). The coin is held
  by the saved order (`offline_bot_craft_orders`); the crafter makes the piece first on its next craft trip with
  materials it buys, and at the end of that break hands the real item into the buyer's bag and takes the coin. A piece
  waiting for its buyer is never worn, listed or sold by its maker. Not delivered in 3 hours: the buyer is refunded
  and a piece already made stays with its maker.
- **Milestone sets.** At levels 20, 30 and 40 a gamebot works out once the crafted set it would wear (the heaviest
  armour of each body slot near the milestone, its main weapon), leaves out slots already as good, and orders the rest
  on the same board once its coin covers the whole set above its reserve.
- **Realm commissions** (`realm_commissions`: classic 30 a realm an hour, **doubled 60**, off). Each realm's
  quartermaster orders guards' armor and weapons from its crafters above skill 50, sized to the crafter: the hardest
  recipe of its own order it makes at orange or better, so an Apprentice gets apprentice work. The crafter makes it on
  its craft trip and hands it to the realm at the end of the break (the piece leaves the world); the realm pays the
  materials and a wage for the crafting time at the crafter's skill, at least three times the materials. That pay is
  new coin, recorded as a `commission` faucet in the economy ledger. `QuartermasterCommissions.GovernorMultiplier`
  (0-2, 1 until WP7's governor sets it) scales the rate. Work goes round: the crafter asked longest ago first.
- **Crafters' own posts.** A crafter's own work may hold `bot_crafter_exchange_listings` (6) Realm Exchange posts;
  everything else keeps every bot's 3.
- **Flood cap.** A gamebot no longer posts an item when its realm already has 25 bot listings of the same type and
  slot in the same five-level band; a vendor takes it. Player listings are never counted or limited.
- **Unsold work comes back.** A crafter's own piece that expires unsold on the Exchange goes back into its bag (when it
  is in the world with room) and to a vendor at its next break, instead of being deleted.
- **Price index** (offline_exchange_index). Per band (realm, type, slot, five-level band, quality tier): today's
  valuation, moved by stock against demand (0.8-1.25) and, after 3 sales, toward what the band sold for; never below
  what a merchant pays, never above three times the materials. A bot's bid is below every ask by at least the vendor
  spread, so no buy-and-sell loop makes coin. It answers `PriceIndexes.Current`; nothing prices by it until WP6.
- **The Exchange tax** is kept from the seller's proceeds of a sale (gamebot or player), never charged to the buyer,
  and recorded with the sale for the ledger. At 0 nothing changes.
- **Logs:** `ECON_ORDER` (placed, made, delivered, refunded, expired, set; source bot, milestone or quartermaster),
  `ECON_COMMISSION` (hourly per realm: posted, delivered, expired, paid; one line per commission with
  `econ_trade_lines`), `ECON_INDEX` (hourly per realm: bands, stock, demand, sales, flooded, starved), `ECON_RECLAIM`,
  `ECON_LIST` (with `econ_trade_lines`), and DividedRealms' `AUTONOMOUS_EXCHANGE_SOLD ... crafted=1|0 tax=` per sale
  while `bot_crafters` or `econ_trade_lines` is on. `EconomyLog` now writes realms by name (`realm=Albion`; it wrote
  `realm=_FirstPlayerRealm`).
- **/lscraft and the rehearsal.** `/lscraft commission` shows the mode, the governor, the tax, each realm's open orders
  and escrow and the hour's counts; `/lscraft index` shows the first backpack item's band, stock, demand, sales and its
  floor, fair, ask and bid. WP10's rehearsal runs the index, the tax, the commissions and the reclaim on WP5's code
  (`Tests/Rehearsal/Adapters/WP5Market.cs`): an aged realm of 300 bots for a day at doubled commissions posted 808,
  delivered 674 (56 lapsed), paid crafters 395 gold for 132 gold of materials; every gate passed (tests only: a
  rehearsal, not a game).
- Ported: the craft orders with escrow from rambodolphin13 (ceac061a, e37f8810, 24f8a2e7), via DividedRealms, GPL-3.0;
  the Exchange flood cap from rambodolphin13 (702ef108), via DividedRealms, GPL-3.0; the crafters' extra posts for their
  own work and the crafted sale line from rambodolphin13 (ceac061a), via DividedRealms, GPL-3.0; the milestone crafted
  sets from shadowofze (f6dbe48), via True165, GPL-3.0, as a second source on DividedRealms' board at its price. Ours:
  the price index, the quartermaster commissions, the Exchange tax, the reclaim of unsold work. Tests only: nothing here
  has been seen in game.

### WP6 Demand and the gear pipeline (M3)

**Off by default** (`bot_crafters`): with the switch off every gear decision is today's (`EquipmentValue`), no check
runs at a task break, nothing is posted and no random number is drawn. Want-to-buy orders also need `bot_craft_orders`
(off). Saved autonomous gamebots only: companions (`/bot`), Epic Special Force visitors, No Lifer buffbots and real
players keep today's rules. The level 20 and 50 starter gear is unchanged.

- **Template-aware valuation.** With `bot_crafters` on, a gamebot scores what it wears as one set: each bonus at its
  class's weight (True165's: primary stat and acuity 4, secondary and Constitution 3, resists 3, trained skills 6,
  unused stats 1) and only up to the set's item cap (stats 1.5 x level, hit points 4 x level, resists level/2 + 1,
  skills level/5 + 1); a piece's level counts up to the bot's, its DPS or AF up to the bot's cap. At level 50 the six
  armour slots also count their quality above 94% (6 a point): a capped spellcrafted set of 99-100% crafted pieces is
  the template, and a 96% drop with odd lines loses to a 99% crafted piece with the right gems. Loot, vendor,
  Exchange and crafted-piece upgrades all decide on this score.
- **The no-undress guard.** A gamebot never puts on anything that lowers its set score and never leaves a worn slot
  empty: every equip is checked last (`TryEquipOwnedUpgrade`, `TryEquipPurchasedUpgrade`). The upgrade rules decide on
  the same score, so the guard should never refuse; a refusal is an `ECON_UNDRESS_GUARD event="blocked"` line, and
  the hourly `ECON_UNDRESS_GUARD` line must read `blocked=0`. A property test runs 10,000 random loadouts (levels 1-50,
  negative and over-cap bonuses, rings and bracers) through the real loot and merchant equip paths: no equip lowered a
  set or emptied a slot and the guard refused nothing (tests only; the overnight check is still to do).
- **A gear check at every task break.** The design's second shopping trigger, independent of the bag: a gamebot looks
  at its six armour slots for a gap (a slot empty, a piece a full five-level band or more below it, or at 50 a slot
  without a crafted 99%+ piece). When its realm's Exchange has a piece that fills a gap, raises its set score, costs
  at most 80% of its purse and no more than the price index's bid, an ungrouped bot makes the town stop and buys and
  wears it there (crafted and template pieces first), after any listing and before the ordinary purchase. The
  full-bag visit is unchanged.
- **Want-to-buy orders.** With no such piece on the Exchange, the bot posts a want-to-buy order for a crafted piece on
  the order board (WP0's `ICraftOrderBoard`, which WP5 implements): its class's armour type at its level, the recipe
  nearest its level that it could wear and that raises its set, 99% minimum for a template gap. The price is
  DividedRealms' quote (materials plus a quarter, at least 100 copper) within half the purse; the board takes the
  escrow, as DividedRealms' `Place` does, so WP6 moves no coin for an order. One open order a buyer; a refused or
  unaffordable post waits 30 minutes. With no board registered nothing is posted and the hourly line counts it.
- **ItemValue165 as the price index.** True165's value score (floor, fair, ask) is registered as `IPriceIndex` unless a
  package already registered one; with `bot_crafters` off it answers today's numbers. Listing prices are not changed
  by WP6 (WP5's price index and listing policy).
- **Coin:** WP6 adds no coin move of its own (an Exchange purchase goes through `TryBuy`, an order's escrow through the
  board). The vendor sale and vendor upgrade now name their ledger kind (`VendorSale`, `VendorGear`).
- **Logs:** hourly `ECON_GEAR_CHECK event="hour"` (checks, gaps, empty, template, below, routed, bought,
  template_bought, copper, wtb_posted, wtb_refused, wtb_open, wtb_unaffordable, wtb_no_recipe, wtb_no_board) and
  `ECON_UNDRESS_GUARD event="hour"` (checked, blocked, blocked_since_start); `ECON_TEMPLATE_BUY` for each template
  piece a level 50 buys; `ECON_GEAR_CHECK event="wtb"` for each order posted only with `econ_trade_lines`.
- Ported: `ItemValue165` from shadowofze (32a88e0, 145bdee), via True165, GPL-3.0, with the two `Exchange165` factors
  it reads copied in and without True165's 1.65 mode; `BotGearValue165` and `FitValue` from shadowofze (1edbcf5), via
  True165, GPL-3.0 (its `UT_BotGearValue165` too), without the `WeaponSpecBonus`/`WastedFocus` terms of earlier True165
  work. Ours: the set score and the template term, the no-undress guard, the gear check, the Exchange route and step,
  the want-to-buy orders, the hourly lines.
- **`/lscraft wtb`** now answers from WP6 (switches, board, this hour's counts, the guard), and the economy rehearsal
  runs WP6's adapter (`WP6.Demand`, with `bot_crafters` on): the set score as the upgrade rule, the gear check and gap
  purchases, want-to-buy demand counted (no board in the rehearsal yet), and `wp6.setScoreDrops`, which must be 0. A
  simulated fresh-start day (1 realm, 200 bots) passed every gate with 528 gap purchases and no set score falling
  (a rehearsal, not a game result). Tests only: nothing here has been seen in game.
- **The rehearsal's commissions now follow the server (a rehearsal-only fix).** WP5's adapter posted a commission to
  any crafter of skill 50, grouped or not and however far its break was, and made one a break, a failure waiting a
  whole task; the median wait sat at the 120-minute limit with or without WP6 (12 fresh-start days without WP6:
  118-124 minutes, 9 of 12 over the limit; with WP6: 120-128, 11 of 12 over; gap purchases only reshuffle the realm's
  random numbers). As `QuartermasterCommissions.Candidates` and the craft trip do, the adapter now asks only an
  ungrouped crafter whose break is within 45 minutes (or that is on a craft trip), the least busy first, and the trip
  works every open commission it can pay for (up to three sessions, a failed attempt tried again at the station), handed
  over at the end of the break. The same 12 days: 63-75 minutes without WP6, 64-75 with it. The server is unchanged.
- **The rehearsal's no-undress gate now checks the server's guard (a rehearsal-only fix).** The week of the aged world
  with crafters on (`aged-legacy-12pct`, 3 x 1,000 bots, seed 1) failed the gate with 41 "undress events". All 41 were
  level 50 bots taking a looted armour drop of higher quality (2-14 points) with a few fewer bonus points: the set
  score rose by 9-29 (6 a quality point above 94) while the old per-slot `EquipmentValue` (1 a quality point) fell by
  1-24. The server makes the same swap with `bot_crafters` on and `BotGearGuard` allows it; in game the piece's armour
  factor is scaled by its quality. The world's gate had kept the `EquipmentValue` rule of `bot_crafters` off. It now
  asks the rule the server enforces: today's `EquipmentValue` with the switch off, and with WP6 the guard itself
  (`BotGearGuard.Allows`: the set score falls or a worn slot is emptied). A swap that raises the set score with a lower
  slot value is not an undress, but it is still counted (`wp6.slotValueTradeoffs`, in the gate's detail) and logged as
  an `ECON_UNDRESS_GUARD event="tradeoff"` line. Every undress line now names the path (`loot`, `bag`, `exchange`,
  `vendor`, `gap-buy`, `crafted`), the bot's level, both scores and both pieces. The same week now passes every gate:
  0 undress events in 225,807 equips, the same 41 swaps counted as trades, and the rest of the log unchanged; with
  WP7's three switches on (rebased on WP7, 39d78c3fe) 0 in 225,559 equips and 47 trades of the same kind (a rehearsal,
  tests only). The server is unchanged.

### WP9 Launcher and release plumbing

Launcher and release tools only; no server file changed. Automated tests only: nothing here has been seen on the owner's
PC. Every default is the server's own (`CraftingEconomyProperties`, `ServerProperties`).

- **Bot crafters and the economy**, a new card in the Living Server tab below the Crafting card: bot crafters
  (`bot_crafters`, off), crafters among new bots (`bot_crafter_share_percent`, 7%), bot crafting pace
  (`bot_crafting_pace`, xp), craft orders (`bot_craft_orders`, off), realm commissions (`realm_commissions`, doubled),
  a crafter's Exchange posts (`bot_crafter_exchange_listings`, 6, 3-100), crafters' chat (`bot_crafter_chat_rate`, 1,
  0-3), the Realm Exchange tax (`exchange_tax_percent`, 0, 0-50), the economy ledger (`economy_ledger_enabled`, on), the
  level-50 kit (`launcher_level50_kit`, on) and an ECONOMY PAGE button. The crafter rows are dimmed while bot crafters
  are off. ALL OFF switches bot crafters and craft orders off and leaves the tax, the ledger, the kit and the shares as
  they are. A first install (Setup's suggested settings) keeps bot crafters and orders off: turning them on for new
  installs is the owner's call.
- **No Lifer buffbots:** two rows under the buffbot switch, the share of No Lifers with one
  (`bot_nolifer_buffbot_percent`, 30) and the 2003 concentration range (`bot_nolifer_buffbot_era_conc_range`, on).
- **The level-50 kit setting.** With `launcher_level50_kit` off, "+ Lv.50" bots arrive without the quality-100 kit and
  10,000 gold: a purse of the level squared in silver (25 gold), and the server's plain merchant gear for their level at
  their first login (AutonomousStarterGear), as "+ Lv.20" bots get. On (the default) is as before.
- **The economy page** (`runtime\herald\economy.html`, from the ECONOMY PAGE button and beside the Herald): the economy
  as the ledger records it, read-only from `offline_economy_ledger` and `offline_exchange_index`: each realm's money
  supply at the last recorded hour (bots, players, guild banks, the Exchange's listed goods, bots and crafters), the last
  24 hours with faucets, sinks, **the Exchange tax** and the residual, the top sinks, flows by kind per realm, and the
  price index's ten bands with the most stock per realm. It says when the ledger is off or has no hour yet.
- **The Herald's Master Crafters:** the top five of each of the six orders, players (per account and realm, named by the
  account's highest-level character there) and gamebots alike, with the title their skill gives; and a link to the
  economy page.
- **Deleting bots** (the bot list, server stopped) now also deletes the bots' rows in the Crafting 2.0 tables: their
  stored types (the "types began" marker stays), crafter rows, customers' memories and the craft orders they placed.
  An order a deleted bot was to make stays, so its buyer is refunded when it expires; a player's or the quartermaster's
  order is never deleted. The world's price index and ledger stay. A running server is still sent a Delete command
  (that path does not clean these tables yet; see the follow-ups).
- **Release plumbing.** `build_release_037.py` reads `EconomyTables.All` from the server source, stops if a table is
  missing from `progress-policy.json`, and checks every one empty in both editions' worlds (`economy_tables` in the
  build report). `make_patch.py` and `find_changes_since_release.py` now skip the optional progress tables as well as
  the progress tables, so a world update never carries or removes a saved row (it still creates a new table empty).
- Tests: `CraftingEconomyLauncherTests` (economy page, purge, kit), `SocialExpansionSettingsTests.EconomyRowsAreSavedReadAndChecked`,
  `HeraldPageTests.TheHeraldListsTheTopCraftersOfEachOrder` (CI's Living Server tab step), and
  `source/tools/tests/test_release_economy_tables.py`.
### WP7 Sinks (wear, repairs, potions, poisons, siege supply, luxury, the governor)

**Off by default**: `bot_upkeep`, `bot_luxury_spending` and `bot_siege_supply` are all off (owner, plan 5.7), and each
needs `bot_crafters` too, as its setting says; with them off the server behaves as before. Autonomous gamebots only:
companions (/bot), Epic Special Force visitors and real players are never touched (players keep WP3's `player_upkeep`).
Buying and repairing happen only at a natural task break, at the merchant the break already visits.

- **Gear wears** (`bot_upkeep`). A gamebot's weapon wears on its swings and harmful spells and its armor on the hits it
  takes, exactly as a player's: the 5% roll (`item_condition_loss_chance`), the con-based amount (now one shared
  formula, `GameInventoryItem.ConditionLoss`; the player's code calls it and is otherwise unchanged), doubled below 91%,
  never below 70%. Three one-line hooks in shared files call it (`GameLiving.OnArmorHit`, `WeaponAction.MakeAttack`,
  `SpellHandler.FinishSpellCast`), because a bot's saved items load as plain rows that the player hooks never reach.
- **The repair errand.** Below 88% a gamebot that can pay goes to a merchant at its next task break (a town-visit need);
  there everything below 90% is repaired, worst first: with its own crafting skill first (a crafter), then at the nearer
  of a smith or a crafter gamebot of its realm that repairs the piece every time (the crafter is paid). The price and the
  durability follow the player's upkeep mode (light by default: half the smith's price, no durability lost), so a bot
  repairs as a player does on this server. One errand every 30 minutes.
- **Potions.** A gamebot drinks the draughts it carries (heal at 35% when hit and not casting or swinging; after a kill
  with an add on it, heal at 50%, power or endurance at 20%), with the item's shared timer and charges. A rich gamebot of
  level 20+ keeps one heal draught (two when very rich), bought from a real merchant near its merchant. Two one-line
  hooks in `GameBot` (attacked, enemy killed).
- **Paid poisons.** An assassin gamebot's coat now comes out of a real vial in its bag, one vial a coat; vials of its
  best damage and debuff coats are bought below 20 up to 60, at a real poison merchant near it, at the merchant's pack
  price per vial. No vial, no coat, as for a player. Companions, and everything with the switch off, keep the free coat.
  The draughts and vials it bought are never sold, listed or cleared from a full bag.
- **Siege supply** (`bot_siege_supply`). Siege engines and field repair kits a gamebot buys are supplied by a siegecrafter
  gamebot of its realm (skill 100+) when there is one, at 80% of the merchant price; the crafter keeps the margin over
  its materials (half the merchant price). Otherwise the merchant, as before. The engine purchase in
  `AutonomousWorldBotController.Siege.cs` calls it (a two-line change).
- **The luxury sink** (`bot_luxury_spending`). A rich gamebot (above 100 gold a level) spends at a task break up to 2% of
  its surplus, times its realm's governor intensity, at least 1 gold: first on fine draughts it drinks (up to two more
  than the upkeep keeps), then on the town's services (board, drink and a bard; the coin leaves the world).
- **The money-supply governor** (with `bot_luxury_spending`). Every hour it reads WP2's ledger row of each realm (the
  bots' coin and every faucet and sink of the hour, so it treats the sinks as a whole: dye, which the owner's PC ledger
  showed as by far the biggest sink, counts like any other) and keeps the supply's growth inside the band the
  rehearsal's gates use (400% a week aged, 100 gold a bot-hour fresh), aiming at half of it. Its levers: the luxury
  intensity per realm (0-1, starting at 0, at most 0.2 a step) and, while a realm is over the limit, WP5's commission
  multiplier (halved). Off: the multiplier goes back to 1.
- **Every coin move names its kind** (Repair, Potion, Poison, Siege, RepairKit, Luxury), so the ledger stays balanced.
- **Logs** (hourly per realm unless noted): `ECON_WEAR` (wear rolls, condition lost, pieces gone below 88%, pieces
  repaired, repair copper, paid to crafters), `ECON_REPAIR` (one per errand, with True165's BOT_REPAIRED_165 keys
  own_skill, paid, copper, by), `ECON_CONSUMABLE` (draughts bought and drunk, vials bought, coats, coat checks without a
  vial), `ECON_LUXURY`, `ECON_SIEGE_SUPPLY`, `ECON_GOVERNOR` (one per realm and hour: ratio, smoothed, luxury,
  commissions). Per-purchase lines with `econ_trade_lines`.
- **The rehearsal** runs the wear, the repairs, the draughts, the vials, the luxury and the governor on WP7's code
  (`Tests/Rehearsal/Adapters/WP7Sinks.cs`; siege supply is not rehearsed, the rehearsal has no sieges). An aged world of
  3 realms and 1,000 bots each, a week, with every WP7 switch on, against the same world with them off (WP7 on
  integration/1.2 at 928005396): repairs 4,099 pieces for 414 gold (the period wear is slow: a worn piece lost about 11%
  condition in the week), 30,081 heal draughts for 69,779 gold, 914,297 poison vials for 33,849 gold (118,722 coats went
  without a vial), luxury 6,366 gold. The money supply ended at 45,009,888 gold with the sinks on against 45,052,925 off
  (83.7% against 83.9% a week; gate 400%), so the governor, aiming at half the gates' wide band, kept the luxury at
  about 0; with a tight band (2% a week) a day's growth fell from 64.6% to 3.9% a week. Every gate passed but WP6's
  no-undress gate, which failed with the sinks off too (41 off, 47 on). Tests only: a rehearsal, not a game.
- Ported: the gear wear and the repair from shadowofze (ItemWear165, 36358de and 2e4212e), via True165, GPL-3.0; the
  repair errand at 88% from shadowofze (TownErrands165, 36358de), via True165, GPL-3.0; the potions from shadowofze
  (BotPotions165, e7f6f8d and 2e4212e), via True165, GPL-3.0, with its unit tests (UT_BotPotions165, UT_RepairErrand165).
  Ours: autonomous gamebots only behind `bot_upkeep`, the seams, the player's upkeep price for bots, the paid poisons,
  the siege supply, the luxury sink and the governor. Tests only: nothing here has been seen in game.

### WP8 Commerce and social (M4, M6)

**Off by default**: player commerce needs `bot_crafters` and the new `bot_crafter_commerce`; the social layer needs
`bot_crafters` and the new `bot_crafting_social`. All three are off, so the server behaves as before. Real players start
every trade; only autonomous gamebots answer (a `/bot` companion never trades, crafts or is asked). Nothing a gamebot
sells or hands over moves during its task: only at its natural task break.

- **Ask a crafter by /tell.** "what do you make?" gets its real title, skill and craft, a few pieces it makes surely
  (the craft-order board's 84% chance) up to its highest level, and the crafted stock in its bag with prices. "wtb chain
  hauberk lvl 23", "can you make a studded helm", "full leather set lvl 20" get a quote: the piece (or the six set
  pieces it makes) at the board's price (materials at the merchant's price plus the crafter's fee: what a gamebot buyer
  pays), when it will be ready (its next break and the work, three pieces a trip) and how it is paid. It says honestly
  what it cannot make ("cant make lvl 45 yet, my armorcraft is only 310"), that it cannot promise a quality, or that it is
  not its trade. A gamebot that does not craft says so and names one of the realm that makes it.
- **/trade WTB.** A player's "wtb ..." in /trade is answered by tell, a few seconds later, by the realm's best crafter
  gamebot that holds it or can make it (once per player in two minutes).
- **Haggling and memory.** "too much", "how about 80s": at most three counters, never below materials plus half the fee,
  further for a regular or friend; a lowball ends it. Each crafter remembers each player (`offline_crafter_customers`:
  deals, coin spent, pieces not collected, refunds owed): 5% off the fee after a deal, 10% for a friend (three deals or a
  friend in the bot's memory), 15% after 10 gold. A player the bot is cool towards (an insult) or who left two pieces
  uncollected gets no deal.
- **Orders through the craft-order board.** "deal" puts the pieces on WP5's board as the player's orders (source
  Player, one order a piece, 6 hours to deliver); the crafter makes them on its next craft trip like any order. A stranger
  pays up front if the crafter is impatient (and anyone who left a piece uncollected): the coin is dragged onto the
  crafter and held in the order's escrow. Regulars, friends and guildmates pay on delivery.
- **Hand-over at the break, trade window.** At the crafter's task break, with the player within about a /say's reach, a
  piece paid up front is handed straight over (the escrow then pays the crafter); a piece to pay on delivery comes in the
  client's own trade window, titled "Trading with <bot>", the bot's pieces on its side and its price; the bot accepts
  once the coin is in, and nothing moves until the player accepts. Crafted stock is sold the same way. A player who is
  not near gets a tell once per break: done, where it is, come and say "ready". "ready?" and "cancel" work by tell (an
  order not yet made is cancelled and refunded).
- **Lapsed orders.** Not delivered in 6 hours: an up-front player is refunded (at once, or at their next login when
  offline); a made pay-on-delivery piece that was never collected stays the crafter's own work and counts against the
  player.
- **Guild crafters (8b).** A guild's crafter gamebots (a bot guild's, or bots a player invited into his guild) make for
  guildmates at material cost; a Crafting-charter bot guild's bank pays its crafter the fee it waived (only while the
  bank holds twice that; a player's guild bank is never spent). "anyone craft?" in a player's guild chat is answered by
  the guild's best real crafter with its title, skill and craft. A guild no longer buys the crafting-haste buff without
  a real player crafter online (gamebots' crafting ignores guild buffs).
- **The price-war storyline (8b).** A fourth storyline arc, "pricewar", four episodes in /faction with a side ending: two
  crafter gamebots of the same gear order and realm undercut each other at the broker. The pair is cast only from bots
  that really share an order, and the scenes name their real craft and the first one's real title; a pair that no longer
  shares an order is dropped.
- **Every coin move names its kind**, so WP2's ledger residual stays 0: the player's coin into and out of escrow
  (`EconomyFlows.RecordPlayerEscrow`, new, counted in the snapshot's flows), the escrow to the crafter (`CraftOrder`),
  coin in a trade window (`PlayerTrade`, new kind, a transfer within the supply), the guild bank's fee (`Guild`).
- **Logs:** `ECON_PLAYER_ORDER` (placed; expired or cancelled, with `_refund` when escrow is owed back; uncollected; owed_paid when the refund reaches the player),
  `ECON_PLAYER_DELIVERY` (source order or stock, pay upfront/delivery/trade, guild fee), `ECON_HAGGLE`, `ECON_GUILD_SPEND`
  (what=crafter_fee), `CRAFT_STORY` (each price-war episode: arc, episode, pair, craft, title, side). AI_PROFILE scope
  `craft.commerce`.
- **Seams in other packages' files (small):** WP0: the two settings, the `PlayerTrade` kind, `RecordPlayerEscrow` on the
  flow sink, `SpentCopper` and `OwedCopper` on the customers' table (WP8's table). WP2: `PlayerTrade` is a transfer, the
  players' escrow counts in the hour's flows. WP5: the board hands a player's made piece and a player's lapsed escrow to
  WP8 (`DeliverToPlayer`, `PlayerOrderClosed`) and can cancel an open order (`TryCancelOpen`); the chat coordinator's
  tell handler asks WP8 first after the beggars. Packets 1.109/1.72: the trade window names a gamebot partner.
- **Rehearsal:** `Tests/Rehearsal/Adapters/WP8Commerce.cs` (a stand-in player per realm commissions crafter agents and
  buys their stock on WP8's pure code). An aged realm of 300 bots for a day: 23 player orders, 22 delivered, 3.8 gold paid
  for 2.9 gold of materials; every gate passed, residual 0 (tests only: a rehearsal, not a game).
- Not built in WP8 itself, all built since in "WP8 The full bot trade" below: drag-to-sell to a WTB bot, spellcraft and
  repair jobs, Crafting-charter guilds taking crafter bots first, a bot guild's bank buying materials and raid potions,
  bot-to-bot guild orders.
- Ported: nothing (the forks had no player commerce; the per-pair discount is an idea from mod-playerbots, no code
  taken). Tests only: nothing here has been seen in game.

### WP8 The full bot trade (owner 2026-11-16: "It should be built out fully, look pretty and have functionality to the fullest")

The design doc said "No trade windows with bots"; WP8 built player trade with crafter gamebots anyway, and asked whether to
keep it the owner chose to build it out. **Off by default** with WP8's own switches (`bot_crafters` and
`bot_crafter_commerce`; the guild parts also `bot_crafting_social`); no new setting. Only autonomous gamebots trade (a `/bot`
companion never does: a drop on it falls through to its own handling), only real players start a trade. Tests only: nothing
here has been seen in game.

- **Any gamebot trades, both ways, in the client's own window** (`craft/economy/commerce/BotTrades.cs`, the pure terms in
  `BotTradeTerms.cs`, the window `BotTradeWindow.cs`, rewritten from WP8's one-way one). Open it as with a player: drop an
  item or coin on the bot, `/tradewindow` with it targeted, or "trade me" / "buy 2" by tell. Titled "Trading with <bot>".
  - **Buy:** the bot lists what it would part with, numbered, in the system window ("[Aldwyn sells] 1. Studded Vest (96%,
    level 23): 1 gold, 20 silver"): a crafter's own stock at its tell quote, spare loot and gear in its bag
    (`AutonomousBotEconomy.FindPlayerTradeGoods`: never worn gear, its operational loadout, an upgrade it would wear, its
    materials or a reserved piece) at the price index's ask, and its own Realm Exchange listings at their posted price
    ("buy" takes the listing back off the Exchange into its bag, `ECON_RECLAIM`). "buy <n>" puts the piece on its side.
  - **Sell:** the bot wants a piece that fills a gap or beats what it wears (`AutonomousBotEconomy.TryGetEquipmentUpgrade`,
    WP6's set score: the same test as every purchase), the best one per slot; it bids the price index's bid (the keen mood for
    an empty slot) and never more than its purse share (WP6's Exchange budget, 80% of its purse); short of that it offers what
    it can spare and says so. A piece for one of its own open craft orders (it is the buyer, not made yet) calls the order off:
    the escrow comes back to its purse (`CraftOrder`) and pays the player. It refuses anything else with the reason, and a
    refused piece keeps it from accepting (nothing is taken for free). What it buys it wears at once through the no-undress
    guard (`TryEquipOwnedUpgrade`).
  - **Swap:** both at once, the coin settles the difference either way; an even swap moves no coin.
  - **Jobs:** the window's Repair box with one piece (the material skill at the item's craft level, the trade window's 100%
    rule as for a player; half the smith's price under the Repair upkeep setting; refunded when the roll fails) and the Combine
    box with the player's crafted piece and gems (a bot spellcrafter, `SpellcraftRules.CheckImbue` with its skill; never past
    the item's imbue points, so nothing explodes; 10% of the gems' value, at least 1 silver a gem). The item stays in the
    player's bag.
  - **Commissions** (WP8) come through the same window: a piece paid up front shows "already paid", one paid on delivery its
    price.
  - The bot accepts by itself when the terms hold ("<bot> has accepted the trade.", a player's words); any change takes it
    back; everything moves at once when the player accepts, or nothing does. Exactly what is owed is taken, never more.
- **A busy bot** (decided here): dead, fighting (or its group is), casting, or on a raid, siege or battleground duty, it says
  so in its own voice and no window opens. Anything else (between pulls, travelling, a break) is the player's to interrupt:
  the bot stops, faces the player and trades for up to two minutes (`BotBrain`: `BotTrades.HoldsStill`, closed on the game
  loop); a fight, the player walking off or the two minutes close the window. The bots' own selling stays at natural task
  breaks; a crafter's stock is now offered mid-task too when the player asks (`CrafterTalk.CrafterFacts.FreeToTrade`).
- **Want-to-buy in /trade:** a gamebot whose gear check finds a gap now and then posts it at a task break ("wtb chain chest
  piece lvl 25, paying up to 2g, pst"), with a real player in the realm, at most one a realm in 8 minutes, scaled by
  `bot_crafter_chat_rate`. (WP6's own want-to-buy orders never reach the board: `BotGearPipeline` posts them with no
  crafter and WP5's `TryAdd` refuses that, so they count as `wtb_refused`; the gap is the demand here. Left for WP6's owner.)
- **The voice:** `bot_trade.lines` (new kind `trade_reply` for every personality, and the want-to-buy adverts as kind
  `trade` with `@needs wants_gear`); every number is said in the system window in fixed words, never by a line.
  `corpus.json` rebuilt with `build_corpus.py`.
- **The original client:** its own trade window, no client change. The bot's side shows its real pieces (their delve as any
  item) and the coin it pays (`PartnerTradeMoney`); the system window has each piece's price or the bot's reason, the total,
  what the bot waits for and its acceptance. PacketLib168 names a bot partner too (it read `Partner.Name`, null for a bot).
- **The new client (0.37c) had no trade window; it has one now**, as content patches of two shipped scripts (the installer
  cannot add a script or a global class): `net/server.gd` reads the TradeWindow packet (0xEA, which the 0.37c network
  client leaves undecoded) and the trade's system lines, and `ui/merchant_window.gd` holds the window as an inner class,
  built beside the merchant window. A granite window like the companion windows, titled as the server says ("TRADING WITH
  ALDWYN"); the bot's offer and yours, six rows each with icon, name, quality and level and the delve as the hover box
  (trade window items, DetailRequest 7); the coin the bot pays and yours, with gold / silver / copper fields and SET; the
  bot's word in green when it has accepted, red when it waits; its last three terms lines; its goods with a BUY button each
  (wheel scrolls); REPAIR and COMBINE toggles; ACCEPT (on only when the bot has accepted) and CANCEL. Drag items from the
  backpack onto it; click one of yours to take it out. Every button is a `/tradewindow` command (the 0.37c network client has
  no ModifyTrade packet). `tests/trade_probe.gd` checks it with the real interface (20 checks), run by
  `tools/newclient-compat/installed_check.py` (CI job `new-client`); run here on Godot 4.7.2: 95 scripts compile, all checks
  pass (tests only: placeholder art).
- **Guilds (8b, `bot_crafting_social`):** Crafting-charter bot guilds take crafter gamebots first when placing members
  (`AutonomousBotGuildPlanner.Candidate.Crafter`); a gamebot commissioning a piece asks a crafter of its own guild first
  (WP5 `PickCrafter`) and pays it the materials only, and a Crafting-charter bank pays the waived fee at the hand-over
  (`CraftingDrama.BankFee`); a Crafting-charter bank pays its crafters' materials while it holds ten times the price
  (`AutonomousBotCrafting.TryBuy`); a Raiding-charter bank buys a heal draught for each raider of level 40+ at a merchant
  visit, up to two carried (also needs WP7's `bot_upkeep`). A player's guild bank is never spent. All in
  `BotGuildCommerce.cs`.
- **Every coin move names its kind:** coin between player and bot in the window `PlayerTrade` (a transfer within the
  supply); an order's escrow back to the bot `CraftOrder`; bank to member `Guild` (a transfer), then member to merchant
  `CraftMaterials` or `Potion`. Residual 0 on every path in the tests.
- **Logs:** `ECON_PLAYER_TRADE` (new: outcome=open|refused|closed|done, the coin each way, pieces, job), `ECON_PLAYER_SELL`
  (a piece a bot bought, its order if any), `ECON_PLAYER_DELIVERY` (a piece a bot sold, source=stock|bag|listing),
  `ECON_SC_JOB` (job=repair|spellcraft, result), `ECON_GUILD_SPEND` (what=crafter_fee|materials|raid_potion).
- **Seams in other packages' files (small):** WP0 `EconomyLog` (the new tag); WP5 `AutonomousBotCraftOrders` (`PreferCrafter`,
  `GuildPrice`, `BotOrderDelivered`); WP4 `AutonomousBotCrafting.TryBuy` (the bank's materials); WP4 `BotCrafterVoice.SendTrade`
  internal; WP6 `AutonomousBotEconomy.FindPlayerTradeGoods`; the guild planner and `AutonomousBotRealmGuilds` (crafters
  first); `BotBrain` (the hold); `PacketLib168` (the title).
- **Rehearsal:** `WP8Commerce.cs` now also trades with any agent on the pure terms: the stand-in player sells a piece for a
  gap (empty, or a band behind) at the eager bid within the purse share, and buys loot from bags at the ask. The aged realm of
  300 bots for a day: 18 pieces sold to bots (13.5 gold), 5 bought from bags (7.5 gold); every gate passed, residual 0 (a
  rehearsal, not a game).
- **Tests:** `UT_BotTrade` (32: the terms, buy, sell, purse share, swap, refusals, the busy rule and the hold, a companion,
  the switch, repairs, spellcraft, a bot's own order filled, a commission through the window, the command, the corpus, the
  want-to-buy text, stock mid-task, a scripted session end to end, the guild placement, guild orders and banks), plus
  `UT_CommerceRehearsal`'s new metrics. Ported: nothing. Tests only.

## Movement, style choice, flanking and the True 1.65 boss and camp fixes (planned as 1.2.1, in 1.2; tests only)

This work was first planned as 1.2.1. It is on integration/1.2 and ships in 1.2 (owner 2026-11-08: "Pull all the
1.2.1 into 1.2"), so its heading was folded into 1.2 on 2026-11-19.

Movement that doesn't look scripted, first slice. Three ports from other forks (fork survey, 2026-11-08), for autonomous
gamebots only. They pass automated tests; nobody has played them in game yet.

- **A moving party no longer jerks once a second.** A group follower stopped taking its own faster route (a horse or a
  town teleporter) from the follow step every turn, which released and re-took its formation follow each second; only the
  leader picks such a route now. From Nikse77 (b0fcd555), via rambodolphin13's DividedRealms (0996fff6ea), GPL-3.0.
- **Melee bots chain their styles and stop spamming taunts.** Each usable style is scored: a chain follow-up after its
  opener landed first, then a back or side style when the bot is there, then reactive styles after an evade, block, parry
  or miss, then the opener of a chain the bot knows, then the rest by growth. Taunt styles count for little unless the bot
  is a grouped tank, and two near-equal plain styles take turns. From rambodolphin13's DividedRealms (0146664a), GPL-3.0,
  merged with 1.2's side, back and front styles (#123).
- **Melee bots take the flank or the back (positional styles, step 2).** A gamebot that is not a tank and knows a back or
  side style for its weapon steps behind (or beside) a monster that is fighting someone else, then swings that style. It
  tries for at most 4 seconds, and again when the monster turns more than 45 degrees or moves more than 100 units (at most
  every 3 seconds). It only walks to a spot the navigation mesh says it can reach on its own floor; otherwise it stays put.
  Never for tanks, companions, bots in a player-led group, raid parties, raid dungeons or epic encounters (their own
  tactics place the raiders). Hand port from the True 1.65 project (zomboden/OfflineDAoC-True165: `BotBrain.Flank.cs`, d2187a498, by shadowofze), GPL-3.0,
  without its 1.65-mode switch and its companion "Positioning" option.

**Boss fixes and raid play from True 1.65.** Ported from the True 1.65 project (zomboden/OfflineDAoC-True165, v0.7, `f68cc75`; GPL-3.0),
each commit credited to its author. They pass automated tests; nobody has played them in game yet.

- Boss scripts: Suttung's berserker stance can't be lost for good; Legion's blast lock-out can't stick; Govannon summons a
  Shade of Aelfgar every time his adds reach the centre, his reset restores his strength, size and model, and the
  database Shade beside him no longer spawns; Vifil's tunnel guardians and Lossren's infected ghoul come back after a
  reset; a dragon lands with an empty aggro list, resets after 8 s out of combat, and Cuuldurach's take-off has the
  other dragons' route guard; boss kill news counts the gamebots; High Lord Baln and Moran the Mighty call only their
  own kind; Olcasgean and the Giant Sporite Cluster pay out once, from one body.
- Raids: Caer Sidi no longer locks after a failed horseman fight (the raid schedule skipped it until a restart), and
  Tuscaran opens only with King Tuscar and Queen Kula both up.
- Raid play: healers purge a long stun or mez (10 s or more left) in any fight; raid healers heal a realm player fighting beside the raid without
  joining a party; casters and archers stand inside Legion's 1,400 damage range; pets leave Summoner Lossren (5 damage
  from pets) for an add, or come back; outside raids, damage dealers step behind a monster that parries or evades 40% or
  more.
- Camps: a chosen camp counts at once; a solo bot leaves a crowded, emptied camp after 15 s; a party counts as all its
  members; camps of the same monster within 5,000 share their bots and monsters; the camp draw favours empty and near
  camps; a party's puller takes the monster standing apart first (Prima); a party moves on after five kills in a row
  with under a quarter of the camp bonus left; spots that keep coming up empty draw less and then rest (capnbry source-empty camps stay on: True 1.65 replaces them
  with a live-camp pass 1.2 doesn't have yet); overlapping camps
  of one monster merge, and a monster with many camps no longer takes most draws; a camp among aggressive monsters 10+
  levels above it is skipped by bots they would outlevel; a solo bot with no ranged attack skips flyer camps; a solo bot
  that left a crowded camp skips its copies for 10 minutes, and one held to easier camps after a death may take an
  uncrowded camp one con harder when all of its own are crowded (never the con that killed it).
- Camp knowledge (Prima): the camp builder works out each camp's pack density, casters, creature type and weaknesses.
  Parties without crowd control draw dense camps less, pure melee classes draw caster camps less, Hunters,
  Spiritmasters and Nightshades draw their favourite prey more, and a solo bot draws camps weak to its damage more.
- Parties stand by role once they have a camp: tanks in front toward it, melee on the flanks, healers and casters
  behind.
- Knowing when to leave (switch `bot_veteran_retreat`, on): a gamebot party calls RUN while everyone is still alive,
  when more monsters are loose than its tanks can hold with no mez ready and the fight already going wrong, or its
  healers are out of power, or dead. It runs to a friendly bindstone within 4,000, else the camp's safe spot (worked out
  ahead on the side with the fewest monsters), else back the way it came, and its next camp is easier. A solo gamebot
  losing badly with no heal or crowd control ready bails out the same way, once per fight; a solo caster steps back
  from a monster it rooted or snared; a healer in a player's group (a hired one, or a gamebot with a resurrection spell) whose player died in a lost
  fight runs and comes back to raise. A party hosting a real player never calls RUN.
  The party reads one shared picture of its fight (the fight board), rebuilt at most once a second.
- Darkness Falls: every keep capture recounts all three realms. The realm with the most keeps holds the door; on a tie
  for most, a holder among the tied keeps it, otherwise nobody does (a capture with no holder used to hand it to the
  taker).

- Prima content (prepared, not in a world yet): the scripts of True 1.65's 16 Prima scripted encounters (Cuinn,
  Talvor's Spirit, Brungar, Zargin, the Unseelie Envoy's convoy, Thergus's ogre war and more) are in the server; they
  start only on world rows that name them, which only a base-game world update can add. `source/tools/
  true165_prima_content.py` adds those rows, and the Prima monsters, items, quests and drops a world lacks, to a
  release's reference world; see docs/TRUE165-CONTENT.md. True 1.65's keep-guard restore is not needed in 1.2's world.

Not taken, because 1.2 already does it its own way: the route-proof cap, Sanguis's last 5%, Govannon's raid calls and
area spells, the dragon lair circle, the Spectral Provisioner (raid bots already wake it) and the Cunovinda and Shade
teleports (already behind 1.2's displacement switch). Not taken: the party-pull camp turn (it fixes a
stall 1.2's pull code doesn't have), level-1 blue-only targeting, the full-backpack fallback (built for True 1.65's
brokers at inns), True 1.65's escape actives (1.2's gamebots don't use them) and its keep-guard restore (not needed in
1.2's world).

## 1.2 — 2026-11-19

- **The 1.2 release docs, and how the project names itself (owner 2026-11-19; text only).** The release notes,
  docs/KNOWN-ISSUES.md and README.md's Living Server part now describe all of Living Server as it is in 1.2, call the
  release "1.2" (not "Beta 1.2"), say epic raids are still being tuned and mark everything not seen on the owner's PC
  "tests only". Following the owner ("we own Offline DAoC now and our living server, true 1.65 is just a different
  version now we borrow some code or ideas from"), Setup's pages and its download step no longer present Offline DAoC as
  "by shadowofze" or his "official update(r)", the launcher's and the server's Hired companions text reads "some code from
  the True 1.65 project", docs/PLAY.md's downloads point at `chris4aker/OfflineDAoC-base-files`, and the True 1.65
  address is `zomboden/OfflineDAoC-True165` (BotBrain.Flank.cs and UT_BotFlank.cs headers too). shadowofze stays credited
  as Offline DAoC's creator and as the author of every ported file (THIRD_PARTY.md and the code headers are unchanged).
  The launcher label is **How much bots talk** in every current doc. No behaviour changed; the server suite passes.

- **Ten more storylines (owner 2026-11-18 09:14: "10 new arcs added to 1.2"; tests only until heard on the owner's PC).**
  `storylines_more.lines` adds ten four-episode arcs, each with its three endings (the player sided with A, with B, or
  nobody), 60 scenes of 18-24 beats with two wordings each, all realm-neutral and 1.65-era: `mentor` (an apprentice
  outgrows the veteran who taught them), `bounty` (two archers race to drop the enemy stealther ganking the mile gate),
  `ballad` (a bard's song about a drowning sweeps the realm), `masterpiece` (a crafted masterpiece sold to a merchant by
  mistake), `mistell` (a tell about a healer's slow heals reaches the healer), `buffbot` (a rumour that a healer is
  somebody's buffbot, and a buff strike), `keepgrudge` (the melee and caster leaders of a keep defense: door or wall),
  `loan` (gold lent for armor, paid back in silver), `gimped` (a player kicked for their class gets one run to prove it)
  and `campspot` (two strangers sharing one camp end up friends). The engine reads every `*.lines` file, so no code
  changed; class-bound roles are cast from that class in every realm (the healer story's healer, the bard, the archers),
  and a side character keeps its letter across an arc. A realm now has 15 arcs instead of 5, so it runs out of new stories
  (each finished arc rests a week) far less often. `UT_LivingWorld.TheMoreArcsCastInEveryRealmAndKeepTheirRoles`.

- **Bot chat audit fixes (tests only): more tales** (owner 2026-11-18 09:14: "Yes to all the new tales"; LORE-028).
  `data/chat/tales_more.lines` adds 14 storyteller tales, each with its fireside version: the open-world bosses (the
  Green Knight, Coliath, Lord Emthoro; the Glacier Giant, Mortufoghus, the Raumarik Revenant, the Wretch of Winter;
  Evern, Juggernaut, Curengkur, Marfoirwort, Mollunero), Myrddraxis and Behemoth. Every one of the 17 open-world raids
  now begins with its boss's tale; the five Wretch raids share one (`AutonomousTales.SameRaid`). Myrddraxis (Fomor has
  no raid) and Behemoth (Darkness Falls stays Legion's) are told on their own, their rumour always lore
  (`RaidlessTales`). 16 more fireside tales for the thin places: Tepok's Mine, Keltoi Fogou, Ilron, Scurceol Hyrde and
  Lady Darra (Albion); Jarl Ormarr, Aros, Hresvelgr, the Cursed Tomb, Nisse's Lair and Vendo (Midgard); King Vian, Koalinth,
  Eraliwyr, the Ancient Black Oak and the Black Lady (Hibernia). A place with no boss file (Keltoi Fogou, the Cursed
  Tomb, Nisse's Lair, Vendo, Koalinth) has a fireside of its own (`AutonomousTales.FiresideLoreIds`). The mechanics
  told are the server's fights. Corpus check and `UT_Tales` only: none heard in game yet.
- **Fixes from sweep 5 (build `5378dbe`, the owner's PC log of 2026-11-17/18; tests only until seen on the PC):**
  - **No Lifer buffbots were saved as gamebots.** When a buffbot logged out with its main, its in-memory record was
    written to the roster as a new row, and that row logged in as an ordinary level-50 healer gamebot with an era
    build. Examples: `BUFFBOT_LOGOUT "Eilriorrin"` at 22:26:05, then roster bot 5876 "Eilriorrin" `ERA_BUILD_ASSIGNED` at
    22:26:34. All 13 `ERA_BUILD_ASSIGNED` lines of that log were such copies, and names repeated across restarts. A
    buffbot is now never saved, like the Epic Special Forces. The buffbots themselves always kept their buff specs.
    `UT_EraLifetimeBuild`.
  - **The copies already saved are retired (owner decision 2026-11-18: kept, never logged in, never deleted).** Once per
    world, at the first start of this build (`living_server_world_repairs` records `buffbot-copies`), the server retires
    each roster row that is provably a saved buffbot: no `ClassName` (the buffbot's record never had one; every other way
    a row is made sets it), a buffbot class at level 50, and the realm, class, gender, race and one of the possible names
    of the buffbot of a main with a lower id. Each one logs `BOT_BUFFBOT_COPY_RETIRED`, with a `BOT_BUFFBOT_COPIES`
    summary; a row without a class name that matches no main is only logged (`BOT_BUFFBOT_COPY_SUSPECTED`). The rows
    get `IsRetired` and the Activity "Retired: copy of <main>'s buffbot (kept, never logs in)"; their items stay. The
    launcher's bot list shows that text and the state "Retired" for such a row (IsRetired, no class name and that
    marker; `BotDeletionText`), not "Deletion pending"; every other `IsRetired` row still shows as a deletion, and Delete
    works as before (nothing deletes a copy unless the player presses Delete). Fresh worlds have none. All 48 `BUFFBOT_CREATED` names in the owner's logs (`5378dbe`,
    `b1b6846`) are among the names it looks for. `UT_BuffBotCopyRetirement`.
  - **RvR: no relayed regroup in contact.** A group in contact and inside its own 60 s retreat cooldown that heard a
    relayed retreat took a "relayed" regroup, and the next board ended it ("contact while regrouping"). That happened
    123 times, 121 of them lasting 1 s. In contact the group now fights its own fight, and the relay still regroups it
    if contact ends while the relay is valid. Relayed `RVR_REGROUP` and `RVR_RETREAT` starts now log their sender
    (`from=<group>`). `UT_RvrEngagement`.
  - **Realm names in three log lines:** `AUTONOMOUS_CAMP_WIDENED`, `EPIC_SWEEP_RAID_START` and `EPIC_SWEEP_START`/`_END`
    printed `realm=_FirstPlayerRealm` and `guestRealm=_First` (aliases of Albion and None). They now print the realm's
    name. `UT_AutonomousCampCrowdWidening`, `UT_EpicPlaytestSweep`.
  - **The played-check log reader** (`source/tools/played_check_log.py`):
    - A boss counts as engaged from a `FINAL_ENGAGE`, an `EARLY_PULL` Commit, a `TARGET_CALLED`, fight-ledger damage
      or a required-adds switch until its wipe, defeat or heal. It used to count only the last `TARGET_STATUS`, so
      teleports "near a resting boss" went from 305 to 36 on `5378dbe`. The 36 left are Conservator's 34 and Xaga's 2,
      before their muster, which were real.
    - Stuck return waves have their own row, apart from the gather-and-go-in claim.
    - A pull that logged no engage (a required-adds switch, or a wipe outside an attempt) now contradicts the 70% row:
      on `5378dbe` that was Aroon and Xaga.
    - `ERA_BUILD_CENSUS Sluaghbinder=` is held against the number of `BOT_CUSTOM_CLASS_RETIRED` lines.
    - `ERA_BUILD_TRAINED` reads "Not applicable" when no bot drew `draw=era`.
    - (c) "back at its sender" is exact from `from=`.
    - The relay-started regroup share's bar is 11.5%, not 10% (owner decision 2026-11-18): the exact checks (a), (b)
      and (c) decide one hop and no echo, and the share is a backstop about halfway between one hop (about 9.9-10.1%)
      and two hops (about 12.7%). First PC result after the in-contact fix, on `b1b6846`: 9.5% (17 of 179), (c) = 0.
    - A new row: buffbot copies retired once and never played (`BOT_BUFFBOT_COPIES`).
    - `source/tools/tests/test_played_check_log.py`.
- **Raid regroups, return waves, Agmundr's pack and the Crypt Lord's court: fixes from sweep 5** (owner's PC, build
  5378dbe, 2026-11-17/18; tests only: none of it has been seen on the PC yet).
  - **No stranded raider is moved to a resting target's door before its muster.** In the route turn between picking a
    mustered encounter and setting its muster, the raid is not holding and its front is the approach 200 from the
    target, which the guard leaves out as the raid's own: 34 raiders were moved 200 from the resting Conservator and 2
    beside Xaga (she killed one 46 s later). That turn now moves nobody (`REGROUP_REFUSED ... the muster for the resting
    X is not set yet`); the next turn sends them to the muster.
  - **A muster is placed only where stranded raiders may be sent.** Musters were guarded against bosses only, the
    stranded-regroup guard against bosses, named, finals and linked packs; in Galladoria the guard refused the olcasar
    warrior's muster for ~18 minutes and 123 raiders stood on the landing at the way in. Musters now stay out of both
    lists, and `REALM_EXPEDITION_REGROUP_REFUSED` names the refusing danger (`by=` and `musterBy=`: its name, spot, the
    distance and the strict edge). Musters in rooms full of named may lie further back.
  - **A return wave calls for help at most twice, and a mustering raid sends none.** The wave's own help call never
    checked the cap (Avalon: helps=9 of 2, 11 calls in 53 minutes); after two it fights what is in its way, and helpers
    already out go back when the raid musters.
  - **Icelord Agmundr's hrimthursa are cleared again before a retry** (`ClearsAgainBeforePull`, as the Crypt Lord has):
    the second attempt had gone in with all six alive and 89 of 90 fell. The catalog's "boosted stats" for his pack was
    wrong: once he fights they get no tether and speed 300, not raised stats.
  - **The Crypt Lord's court is pulled only while his patrol also keeps 550 from the raid's walk** (his walking aggro,
    400, plus 150) from the muster to the court monster, over the walk's time plus 35 s: the old window checked the court
    monster's spot only, and he joined 5-7 s after two "clean" pulls. For the western forerunner the window is about 7 s
    of his 132 s loop; the 20-minute hold lapse still applies. Each clear window logs `REALM_EXPEDITION_COURT_WINDOW`
    with his spot, next point and closest approaches, so the next PC log can confirm it.
  - Bots no longer cast mez or stun on a monster with CC immunity (the Crypt Lord: 4 Minstrel casts).
- **A boss dragged off his floor no longer loses his adds' stands, and a fight in progress is never set aside for
  want of one** (tests only; owner's PC, build 5378dbe, 2026-11-17, Galladoria). Aroon, dragged 3,700 from his spawn and
  370 higher, left "none of 463 floor spots ... on his floor" for the six Scaiths still alive in his room, and
  `REALM_EXPEDITION_TACTIC_ASIDE` (23:38:08) sent the route to an olcasar muster while he and the Scaiths killed 68 more
  raiders. The ranged stands for a boss's required adds are now looked for on his spawn's floor (his room), still clear
  of where he stands; and a boss whose fight is on with the raid is kept (`REALM_EXPEDITION_TACTIC_KEPT`, at most once a
  minute) and its stands are tried again, never set aside. Scurceol Hyrde's orbs, searched from his own spot at home,
  find the same stands as before.

- **Wipes are counted per encounter, not per monster** (tests only; owner's PC, build 5378dbe, 2026-11-17, Galladoria).
  One continuous 19-minute fight in Xaga's room (408 deaths) was scored as four wipes on three names: Beatha (00:15:21),
  Tine (00:19:13, ten seconds after Beatha died), then Xaga twice, so the two-attempt rule ran per monster. A wipe at a
  required add is now the wipe of the boss that owns it (`WipeEncounter`), in attempts and in raid-wide deaths outside
  one, and the "already counted" guard follows that encounter until it and all its adds are out of combat
  (`CountedFightGoesOn`), not one monster. The defeat and the wipe line name the boss (Xaga); the line adds
  `killer="Beatha"` (outside an attempt) or `fought="..."` when they differ. Stricter: two tries per linked encounter.

- **A boss with required adds is pulled as an attempt** (tests only; owner's PC, build 5378dbe, 2026-11-17, Galladoria).
  Neither Aroon the Urlamhai nor Xaga's room had a `FINAL_ENGAGE` or an `EARLY_PULL`: a Scaith in combat during Aroon's
  muster (0 of 86) and Beatha in combat during Xaga's (2 of 86) sent the route straight to the add with no attempt open,
  so the 70% pull, the early-pull Hold or Commit, the regroup after a wipe and the return-wave hold never applied, and
  all five wipes were "raid-wide deaths outside an attempt". The route now fights a mustered boss's required adds only
  inside a committed attempt on it (`FightsRequiredAddsNow`); before that the boss stays the target, and the boss or any
  of its adds in combat is an early pull answered by the muster (raiders at an add in the fight count as at the fight).
  Once the boss's fight is off (a wipe), the route goes back to the boss so its muster scores the attempt and regroups.
  Scurceol Hyrde's orbs, Soul Reckoner's souls, Xaga's fairies and Aroon's Scaiths all go through this. Stricter, never
  easier: proven on the PC only once an Aroon or Xaga pull shows `FINAL_ENGAGE` or `EARLY_PULL` before the first
  `REALM_EXPEDITION_TACTIC ... required-adds`.

- **A boss moved home is no longer scored as killed** (tests only; owner's PC, build 5378dbe, 2026-11-17, Caer Sidi). Two
  full wipes at the Crypt Lord (93 raiders dead each, he never fell below 96%) ended `REALM_EXPEDITION_ATTEMPT_ENDED
  how="killed; it came back since" wipe=False`, so the raid had four attempts instead of two. The attempt took any new
  `SpawnTick` as a respawn, but every AddToWorld sets it and `ABrain.Stop` moves an idle NPC home a minute after the
  last raider near it fell. A kill now needs the route to have seen the encounter dead (health 0 or respawning) after
  the pull, and a new life after that (`KilledSinceAttempt`); Chthonic Knight Ukobat's real respawns (`ccba364`) still
  end their attempts as wins. Raids now end at the true second wipe, never later.

- **Every Living Server feature is on by default** (owner 2026-11-16: "They should be on by default - all our living
  server features should be in by default if someone downloads our installer"; this replaces the rule that bot-side
  switches default off). Tests only: nothing of this change has been seen on the owner's PC.
  - Now on in the server code, the launcher's defaults and Setup's suggested settings: bot guilds
    (`bot_guilds_enabled`), bot personalities, the Server life parts that were still off (`bot_living_firsts`, `_death`,
    `_alarms`, `_nemesis`, `_breaks`, `_body_language`), the real-clock bot schedule (`bot_schedule_mode` = clock), and
    Crafting 2.0's gamebot side: `bot_crafters`, `bot_craft_orders`, `bot_upkeep`, `bot_luxury_spending`,
    `bot_siege_supply`, plus WP8's `bot_crafter_commerce` and `bot_crafting_social`. Setup's first install now turns
    bot crafters and craft orders on too.
  - Still off, and why: the diagnostics lines (`craft_made_lines`, `econ_trade_lines`); the classic rule options
    (power halving below 50%, secondary crafting caps, classic quality, classic losses: rules, not features); the
    five epic encounter options the owner set off until a played check (Funus's weapon puzzle, Galladoria's full fire
    patches and Easmarach's endless heal, Govannon's full health and full heal); the owner-only
    `allow_character_deletion`. The Exchange tax stays 0, and the XP-by-level ranges stay rates that Setup sets. The
    Sluaghbinder edition switch stays per edition.
  - **An existing world moves once.** At the first start of this build the server changes a switch only where the
    row still holds the old default the server wrote itself (Value and DefaultValue both the old default; settings
    version "1.2.0.1"), and logs `Living Server 1.2.0.1: <key> set to the new default`. A value set in the launcher's
    Living Server tab is kept, and from this build every save in that tab marks its rows as the player's choice
    (DefaultValue cleared), so a later default change never overrides it.
  - **Release worlds ship them on.** `build_release_037.py` writes every Living Server switch at its release value
    (`source/tools/release-server-settings.json`, checked against the code defaults by
    `test_release_server_settings.py`) into both editions' worlds, whatever the developer's install had, and checks it.
  - The economy rehearsal's scenarios now start from these defaults (everything on); `today-aged` sets the gamebot
    switches off to stay the pre-Crafting 2.0 baseline. A switch that is on but that no rehearsal module plays is now
    named in the report (today: `bot_crafting_social`).
  - **Rehearsal with everything on** (tests only; this branch on integration/1.2 3fb711722, 3 realms of 1,000 bots,
    seed 1): a day of each of the 10 plan scenarios and a week of `fresh-7pct` and `aged-legacy-12pct` pass every gate.
    The aged week's undress gate, on the set score since 5b0066a11, counts 46 swaps that raised the set score with a
    lower slot value. On the base before 5b0066a11, the same aged week failed "0 undress events" with any switch set
    (all on 50, the earlier switches 41), so that was the gate's rule, not these defaults. `bot_crafting_social` has no
    rehearsal module.

- **A bot listing on the Realm Exchange no longer stalls the server while the database writer is busy** (tests only;
  owner's PC, build 1dc66b6, overnight 2026-11-14/15). At 02:38:03 the game loop stopped for 5.6 s: Waffleironer
  (level 50 Midgard Spiritmaster, solo PvE, between tasks in a capital, full backpack) was listing Bodybender Cloth Pants,
  and its AI turn (5,582 ms) waited for the shared database write lock while the once-a-second bot status save held it
  through a 6,062 ms Inventory INSERT (`lock.db_write` 4 waits / 17.0 s, `db.inventory_batch` 7.95 s that minute against
  about 1.3 s normally). Why that one INSERT took 6 s is not known (that minute also had a 682 ms gen-2 GC, a 2.1 GB heap
  and 20,000 page faults a second). A bot's listing or purchase now waits at most 50 ms for the lock; if it is still
  held, nothing changes and the bot keeps the item and tries again a second later (a purchase at its next exchange
  check, a beggar at its next line). A status save that holds the lock over 500 ms now logs
  `AUTONOMOUS_STATUS_BATCH_SLOW` (its time split, bots, items, GC and heap), and a deferral logs
  `AUTONOMOUS_DB_WRITE_DEFERRED` at most once a minute, so the next slow write is named. The SQL itself still runs on
  the bot's turn once it has the lock. `UT_RealmExchangeEconomy`.
- **No Lifer buffbots** (tests only; owner 2026-11-15, "I want 30% of the No Lifer personality bots to have a 'buff bot.'"):
  - **Who and what:** 30% of the No Lifers (fixed per main from its roster id; existing and new rosters alike) run a
    level-50 buffbot. Albion gets a Cleric (Enhancement 50), Midgard a Shaman (Augmentation 50), Hibernia a Druid
    (Nurture 50).
  - **Gear:** the launcher's level-50 starter gear. No coin, no loot.
  - **Where it goes:** it follows its main through its realm's PvE land and stays out of the main's fights. It parks at
    the nearest bind (the portal keep in the frontier, the main's bind for dungeons, battlegrounds and raids).
  - **Buffs:** it keeps every single-target buff on its main, concentration buffs first, and recasts them as they drop.
  - **Never groups:** a buffbot never joins a group or raid. `Group.AddMember` refuses it, and `/invite` answers "is a
    buffbot and never joins a group". Raid and epic code is unchanged.
  - **Memory only:** like the Epic Special Forces it lives in memory only: never saved, not in the roster or the
    population. It logs in and out with its main, shows in `/who`, and answers tells with "afk buffbot".
  - **Concentration range:** its buffs follow the 2003 rule of no range (`bot_nolifer_buffbot_era_conc_range`). Mythic's
    5,000-unit limit came with the July 2005 Classic servers. Everyone else keeps this server's range.
  - **Settings:** `bot_nolifer_buffbots` (launcher switch, on) and `bot_nolifer_buffbot_percent` (30).
  - **Log lines:** `BUFFBOT_CREATED`, `BUFFBOT_FOLLOW`, `BUFFBOT_PARKED`, `BUFFBOT_BUFFED`, `BUFFBOT_RELOCATED` and others.
  - **Docs and tests:** research in `docs/research/BUFFBOTS-2003.md`, details in `docs/BOT-PERSONALITIES.md` 2.3.
    `UT_NoLiferBuffBots`.
- **The battlegrounds' lore** (tests only; owner 2026-11-15, "We need full lore for the battlegrounds written"): one
  backstory for the four fields, why only the young may fight there, why the fallen wake at their own portal keep and who
  keeps the gates, told three ways by the three loremasters (a new neutral place, `proving-fields.json`, with no region
  of its own). Abermenai, Thidranki, Murdaigean and Caledonia are rewritten: each realm's claim on the field, a book in
  one voice, and a storyteller's legend per field (Abermenai in every realm, the others in the realm whose tradition
  names the keep), with the rumours that follow it and a few lines said in the field (`data/chat/battleground_lore.lines`).
  A player standing in a field is told its legend first. The old battleground stories that no longer fit (the Ferryman's
  Peace, the Spider Summer, the Headhunters' Autumn) are gone. Caledon's lore file is now named Caledonia, as the game
  names the zone. `UT_BattlegroundLore`.
- **Bots no longer talk about Cathal Valley** (owner 2026-11-15, "Yeah leave it out"). The 45-49 battleground came
  after this era, so its seven chat lines are gone and the voice guides no longer list it. The world database still has
  the zone; no bot goal sends anyone there (`BattlegroundBrackets` stops at Caledonia).
- **Heretics are cellar talk, not a class** (tests only; owner 2026-11-15, era rules applied conservatively, "unsure"
  on the item). The Heretic is a Catacombs class (2004), but Albion's history stated its origin as fact: an acolyte
  who renounces the Church for Arawn. The Living Present now tells it as a whisper of Camelot's cellar watch, that some
  of the Church's own acolytes have turned to Arawn in secret, which the Church calls slander; the Arawn power says his
  worship is heresy and leaves the rest as cellar talk. The 2003 NPCs Heretic Elam and the Heretical Hermit, men of
  "some wrong faith", are unchanged. `UT_EraRumours` checks that no realm history names a Heretic, Bainshee or Vampiir.
  Pazz, the Atlas freeshard's daily-quest giver in the Thidranki and Caledonia portal keeps, was left as world content
  here; the captains entry below replaces it.
- **Captains give the battleground and frontier quests** (tests only; owner 2026-11-15, "Rename to captains"). Pazz
  and Rey, the Atlas freeshard's skeleton "Bone Collector" who gathered bones for his master Fen (brought in by OpenDAoC
  in 2022), are gone. Each portal keep has a captain of its realm in the keep commander's outfit, standing where they
  stood and giving the same quests with the same levels, goals and rewards: Thidranki Captain Circa (Albion), Captain
  Cody (Hibernia) and Captain Astridr (Midgard), the names the Camelot Herald's battleground quest guide gives;
  Caledonia Captain Aldwyn, Captain Fiachra and Captain Thorgrim; the frontier portal keeps Captain Edric (Castle
  Sauvage), Captain Hallvard (Svasud Faste) and Captain Eithne (Druim Ligen). The Caledonia and frontier names are new,
  in each realm's style. The quests read as a captain's orders ("Thidranki Skirmishes", "Seize the Keep in Thidranki",
  "Enemies of the Realm", "Every Little Bit Helps", "Slay the Enemies", "For the Realm"), with no "[Daily]" or
  "[Weekly]" tag; they still reset each day or week. A quest is saved under its class name, so a quest in progress
  keeps its step and kill count; one waiting for its reward is handed in by whispering [report]. A world that saved a
  Pazz or Rey (the scripts never did: their NPC was made by script) has that row renamed to the captain, and any second
  one at the post removed, once; the next start changes nothing (`QuestCaptains`). The Herald's guide is from the 2004
  battlegrounds (it lists Killaloe and gives bounty points), so the captains are the closest period source, not a 2003
  one. `UT_QuestCaptains`.
- **Rumours of a sunken land, and of what lies under the capitals** (tests only; owner 2026-11-15, "Yes agreed on lore"):
  Trials of Atlantis and Catacombs exist only as hearsay. Now and then the realm hears of it: an old family legend,
  a /faction argument, a rare remark at the harbours and in the capitals, an answer when a player asks ("what is
  atlantis?", "is there anything under camelot?"), and each capital's loremaster and book in that realm's voice. Nobody
  has been, nobody can go, and no master level, artifact or Catacombs class is mentioned. The corpus builder allows the
  words "atlantis" and "catacombs" only in lines tagged `rumour_atlantis` or `rumour_below`, and refuses any that claims a
  trip there (`tools/bot-voices/tests/test_era_rumours.py`, `UT_EraRumours`). A loremaster now reads a question asked in
  words for its keyword. The prose (the legend, the loremasters' topics, the book pages) was rewritten after the owner's read.

**Era cuts and polish for the playful features** (tests only; owner's decisions 2026-11-15 07:10 after the Easter egg
inventory: cut the jokes from after 2003, keep Leeroy; Trials of Atlantis and Catacombs "as rumors only from a lore
perspective"; Caledonia's quests follow its bracket).

- **Jokes from after October 2003 are gone.** Leeroy still charges and yells, but "at least he has chicken" is now "who
  brought him. seriously, who brought him" and "he can buy the ale tonight"; the joker's "achievement unlocked: died to a
  rabbit again" is "died to a rabbit again. third one this week"; the gold spammer's "over 9000000 happy customer" is
  "100000"; "cool story {target}" is "nice speech {target}"; "i see what you did there" is "heh, clever"; the laugh
  reply "ikr" is "hehe yeah"; a boss's after-kill "aoe ftw" is "aoe emptied the classroom". The corpus builder now bans
  these phrases (`tools/bot-voices/build_corpus.py`).
- **Trials of Atlantis and Catacombs are rumours only.** The chat lines already named neither (the builder bans them).
  The realm histories' Atlantis entry, which the loremasters and the Herald read, said each realm "found the island
  remnants of Atlantis" and that its ships sail there; it is now harbour talk of a dream that "nobody has sailed there
  and come back to prove", and the three place notes say the same. "what does ml mean" is answered "master level, from
  the atlantis rumours. nobody here has one".
- **Caledonia's six dailies ask for levels 30-35**, Caledonia's bracket (`BattlegroundBrackets`), instead of 34-39, so every
  level that can enter Caledonia can take them, and a 36-39, who can't enter it, is no longer offered them. The Midgard keep quest sent the player back
  to "Thidranki Portal Keep"; it now says Caledonia. A test holds every Caledonia and Thidranki daily to its bracket.
- **Beggars read what is asked for.** "i need to repair my armor", "i need to change my spec", "can i get some help with
  this quest item", "we need something to do tonight", "i need a new weapon, where do i buy one" and "i need 1 more for
  the group, anything works" no longer mark the player as a beggar for an hour: the thing of value has to follow the
  asking words, before a "to", "with", "for" or a clause break, and a shopping question is not begging. "i need money",
  "i need to borrow some gold" and the old requests still count.
- **A raid named after its boss is not named twice.** Every dragon and open-world raid carries its boss's name, so bots
  said "the raid killed Golestandt in Golestandt" and "Coliath raid got Coliath". A line that names both is now skipped
  when the raid is named for its boss (`AutonomousBotVoiceCorpus.NamesTheBossTwice`), and a tell that names only the
  boss was added. An open-world raid's wipe call no longer sends the raid to "the staging ring" for "the dragon".
- **Holidays.** "happy new years" (and "new years eve", "newyear") is a New Year wish. `/lstest holiday` tells the GM a
  wish the bots answer ("happy new year", "merry christmas"; none for April Fools, which has jokes only). When April
  Fools falls on Easter weekend (Easter Monday 2024, Easter Sunday 2029), Easter is the holiday and April Fools' lines
  are still said.
- **Roleplayers read their words by place.** "see ya" is "see thee", "ur welcome" "thou art welcome", "can u" "canst
  thou", "no one" "none", "you go first" "thou go first", "your armor" "thine armor"; "no" is "nay" only as an answer
  ("i have no gold" stays).
- **`/lstest leeroy now` lapses after 10 minutes** if no Leeroy raid is gathering, instead of waiting for good and firing
  on a Leeroy raid hours later. It is still used once.
- **`/lstest list` names every part**, now with tales, tale raids, beggars, bonds, atmosphere lines and cosmetics.
- **"tonight" at any hour.** The rumour after a tale that a raid is forming says "soon" or "in a bit" (it opens 6-12
  minutes later), and the "{keep} fell" tell says "good rp out there right now".
- **The joker no longer owns a horse.** "my horse has better gear than me" is "the stablemaster's horse has better gear
  than me", which agrees with the dunce scene: horses are rented stable routes.
**Battleground waves with a small population** (tests only; owner's PC, build `3d67b6f`, Thidranki 2026-11-15, 07:28-07:54:
11-21 bots of each realm inside; Midgard and Hibernia "Gathering for the assault" for 16-22 minutes; 329 bot deaths in
Thidranki, 104 of them Midgard and Hibernian bots killed one at a time by the keep's level-33 guards; the only wave,
Hibernia's at 07:49:41 with 8 of 21, lost 19 bots in three minutes, 10 of them to guards, and was spent without reaching
the gate; lord 100%, guards 19/19, both gates standing).

- **A bot that has just fought keeps gathering.** For ten seconds after any hit, and while it recovered, a gatherer skipped
  the gathering and went for the nearest keep guard up to 5,000 away on its own. Now the wave decides first, in a fight or
  not.
- **A gatherer fights only what comes at it.** It used to take any enemy it saw within 5,000, so it was pulled into the
  holder's defenders and under the guards. Now it fights an enemy that walks into the gathered realm (700), or one firing
  on the realm near the staging point (1,500) or near itself (1,000). It drops a target that broke off and left the
  staging point instead of chasing it.
- **The staging point is 3,500 out from the keep (was 2,500).** The guards killed 45 Midgard bots 1,512-3,109 from the keep
  (median 2,230), 20 of them within 900 of Midgard's own staging point.
- **After four minutes a wave goes with half of the realm inside** (at least 4), instead of a fixed 8. That is 7 of 13,
  or 11 of 21. A realm also goes with half when another realm's wave is going in on the same keep. Most of the realm
  (60%, at least 8) still goes at once.
- **A going wave marches on the gate together.** While the gates stand, a bot fights a keep guard that is within 1,200 or
  firing on the realm, and otherwise walks on with the wave. Each bot used to run at its own nearest guard.
- **`BG_WAVE_STATUS`** once a minute for each attacking realm: inside, gathered (and how many are in a fight), needed,
  patienceNeeded, waitedSeconds, staging x,y,z, nearKeep, holderNearKeep, going, and the fights its gatherers turned
  down or broke off. `BG_WAVE_ASSAULT` now also logs patienceNeeded and withOtherRealm.
- Unchanged: the keep, its guards, lord and gates; the holder's defence (a third hold the walls; at 07:49:54 Albion had 15
  bots within 2,500 of its keep); siege crews; the rally inside the breach; and releases (a gamebot releases to its bind,
  as a player does with `bg_release_to_portal_keep` off).

**Battleground storms go in by the keep's own door order, and a wave fights the guard that hits one of its own** (tests
only; owner's PC, build `ea882e6`, Thidranki 2026-11-15 08:39-09:02: the waves now gather and go (Hibernia 15 at 08:57:20,
Midgard 9 five seconds later), walk round the walls toward the broken outer gate in 20-55 s, and are "spent" at 09:00:23/27
because they died on the way: in those three minutes 16 Hibernian and 4 Midgard bots fell to the keep's level-33 guards
800-2,400 from the keep's centre, most of them with one attacker on them; guards 19/19, lord 100%).

- **The door a storm marches on is the way in, not the nearest door.** While a main door stands shut, the wave goes for the
  outer gate first and then the keep door behind it (the order the siege crews already used). On build `3d67b6f`, with
  both doors of Thidranki Faste shut, 18 of 19 storm samples marched on the keep door, which lies behind the outer wall,
  from outside it and stood at the south wall with no path.
- **Through a broken outer gate, on to the shut keep door.** With the outer gate broken and the keep door shut, a bot that
  got through the gate walked at the lord, whose path the shut door refuses, and held just inside the gate, 1,700 from the
  door it had to hit. Now it walks to the keep door's courtyard side and batters it.
- **A storm's walk ends in front of the door** (230 out, on the attackers' side), not on the door's own floor.
- **A going wave fights together.** While a door stands, a bot of a going wave fights a keep guard that is hitting one of
  its realm within 1,500 of it, and keeps that guard until it is down. Each bot used to keep its own pick among the guards
  near it, so the guard hitting its neighbour was ignored. A melee bot helps only against a guard beside its victim, not
  an archer or caster on the wall.
- **Logs.** `BG_STORM_TRACE` writes a sample every 20 s for each realm (was one server-wide; `ea882e6` wrote four, all
  Hibernian), with the realm, how long its wave has been going, and `pending=` (the order was given that turn, so moving,
  path and dest are still the previous order's: the "dest next to the bot" of `ea882e6` was the staging hold, not a short
  path). `BG_WAVE_SPENT` and `BG_WAVE_STATUS` count the wave's deaths to the keep's guards and to anything else.
- Unchanged: the navmesh (the bots' route round the walls was real: they reached 570 from the broken gate), door flags (a
  broken door is open and passable), the keep, guards, lord and gates, the 3-minute spent check, and siege crews.

**Battleground keeps stay at level 1, as they shipped** (tests only; owner's PC, build `3b808b4`, Thidranki 2026-11-15
09:39-10:10: four two-realm waves of 9-12 bots each were spent, 8-15 of each killed by Thidranki Faste's guards, none
closer than 1,425 to the door; guards 17-19/19, lord 100%; the guards were level 33 against levels 20-24).

- **Why the guards were 33.** Every release world gives the battleground keeps level 1 (`release-world-defaults.json`), but
  a capture reset any keep to `starting_keep_level` (4): DOL's 2009 value for frontier keeps ("only levels 4 and 5 are
  supported correctly"; it was 0, then 1). Battleground keeps can't be claimed (`allow_bg_claim` off, and the bot guilds
  check the same rule), so nothing else raised them: on the owner's world Thidranki Faste and Dun Murdaigean, the two that
  had changed hands, were at level 4 with no claim, the other six battleground keeps at 1. At level 4 Thidranki's guards
  are 26 + 1 + 4 x 1.6 = 33 (seen in the deaths) and its lord 38; its doors have four times their level-1 health.
- **The original.** A keep rose above level 1 only through the claiming guild's upgrades (Herald, New Frontiers keep guide:
  "Keeps and Towers start out at level 1"), and the battleground keeps were never claimed: the Herald's realm war page of
  April 2003 lists a claiming guild for every frontier keep and only an owning realm for Abermenai, Thidranki, Murdaigean
  and Caledonia. So a battleground keep now falls back to level 1 on a capture or a release, and a played world's
  battleground keep that a capture raised is put back to 1 when the server loads (`BG_KEEP_LEVEL_RESTORED` in the log).
  Thidranki's guards are 28 and its lord 33 again; frontier keeps, the guard formula and `keep_guard_level_multiplier`
  (1.6, hard-coded in DOL before it became a property in 2009) are unchanged.
- **`BG_KEEP_STATUS`** now logs the keep's level and its guards' and lord's levels (`keepLevel=`, `guardLevel=`,
  `lordLevel=`).
- Unchanged: guard respawn (15 minutes, ±10, the upstream default), claiming rules, the waves and their siege crews (the
  2002-2004 Thidranki threads describe the same thing: rams, two or three groups, a keep that changed hands "twice a week").

**Battleground lord rallies gather outside the breach and storm together** (tests only; owner's PC, build `3b808b4`,
Thidranki 2026-11-15 10:12-10:58: all six doors of Thidranki Faste down, 30-46 bots of each attacking realm inside, and in
all eleven `BG_KEEP_STATUS` samples both rallies read "rallying 0/31-34": no rally ever went up, the lord stayed at 100% with
no attacker on his floor, and 276 Midgard and Hibernian bots died to the keep's guards).

- **Why nobody rallied.** The rally point stood just inside the keep door (32475,38224,3728): under the lord's floor, 1,860
  past the outer gate, with the courtyard's guards (Armsmen and Wizards, aggro 1,000) in between. Every bot crossed the
  courtyard alone and was fought there or on the way, so none ever counted as rallied. Only bots within 4,000 of the lord
  took the rally at all (17-33 others "marched" on the keep's centre), and with the waves bypassed once the doors are down
  nothing held anyone out of fights: 33-57 bots were "Fighting" at every sample, 173 deaths were "Fighting Armsman" or
  "Fighting Armswoman", guards picked up to 5,000 away. The `step=rally` samples with `dest` at the bot were orders given
  that very turn (`pending=True`, 88 of 119 samples); the 31 others all walked a found path toward the outer gate.
- **The rally stands outside the breach.** The breach is the outermost broken main gate (the outer gate before the keep
  door, never a postern, the one on the realm's side when there are two); the rally stands 900-1,800 out from it, on the
  navmesh with a complete walk to the gate, and as far as it can be from every guard's post (1,300: a guard's aggro of
  1,000 and a few steps). Every assaulting bot of the realm goes there, wherever it is in the battleground.
- **Holders fight only what comes at them,** as a wave's gatherers do (an enemy at the rally, or one firing on the realm
  near it or near the bot); never the lord, and never a guard on the walls that isn't attacking them.
- **Enough is at most a wave of the realm.** The strength model asked 31-34 of 30-46 bots dying about every four minutes.
  A rally now waits for the model's number or 60% of the realm inside, whichever is smaller (at least 8); half of that
  after three minutes, whoever is there (at least 8) after six, as before.
- **The two attacking realms cue each other.** While the other realm storms the same lord, a rally goes with half of
  what it needs, as #247's waves do.
- **The storm keeps the wave's discipline.** From the rally the stormers go through the gate and up to the lord together.
  On the way they fight the guard that is hitting one of their own nearby (a going wave's rule), a guard in their way or
  firing on them, and an enemy that is upon them or firing on the realm near them; everything else is left alone.
- **`BG_RALLY_STATUS`** once a minute for each attacking realm: state, rallying, needed (and the model's number), inside,
  the rally point and its gate, its distance to the nearest guard post, onLordFloor, atLord, the deaths to guards and to
  others, and the fights its holders and stormers turned down or broke off. `BG_RALLY_STORM` also logs inside, the model's
  number, withOtherRealm and the gate. A wave still "going" when the doors fall now stands down (Hibernia's of `3b808b4`
  went on counting deaths to guards as its own, 11 to 203 between 10:10 and 10:58).
- Not verified here: the walk from the courtyard up to the lord's floor (the Thidranki navmesh isn't in the build
  container). On `3b808b4` Albion's own defenders were killed on the tower stairs (z 4,012 and 4,209) and on the lord's floor
  (z 4,592), so a way up exists for them; the 2026-10-17 navmesh probe found the lord reachable from the gates. Unchanged:
  the keep, its guards, lord, doors and respawns, the strength model, attempt endings, reinforcement batches and leases.

**Each attacking realm rallies on its own spot** (tests only; owner's PC, build `d64d005`, Thidranki 2026-11-15
12:55-13:02: all six doors of Thidranki Faste down, Albion holding it, 33-43 bots of each attacking realm inside; every
`BG_RALLY_STATUS` of Midgard and of Hibernia read `point=36133,38275,3368 gate=252000301`. Midgard went up with 15 at
12:59:49 and regrouped a minute later, "half the stormers fell", lord 100%, nobody on his floor: seven of its ten deaths in
that minute were to Hibernian bots at and around the shared point, one to a guard at the gate, two to Albion bots).

- **Why they shared it.** The rally point depended only on the keep and its breach, and both realms take the same breach
  (the outer gate), so the two realms that should each gather and storm met at one spot and fought each other there.
- **The rallies now stand at least 2,250 apart** (twice the 1,000 rally radius, so no bot is at both, plus the 250 a holder
  stands within: each realm's holders are 2,000 or more from the other realm's point, beyond the 1,500 at which holders
  fight back at an enemy firing on them). A rally may stand straight out from the breach or 30° or 60° to either side, at
  900-1,800 out as before; a realm prefers the spots toward its own arrival, still needs a complete walk to the gate and
  still keeps 1,300 from every guard's post. The realm that placed its rally first keeps it; the later one moves off.
  With no spot apart, clear and walkable the rally stands where it did before.
- **No truce.** Realms that meet still fight; only their rallies no longer share a spot.
- **Logs.** `BG_RALLY_STATUS` adds the rally's `bearing=` (degrees off straight out) and `apart=` (distance to the other
  attacking realm's rally, `none` without one); a new `BG_RALLY_PLACED` line logs each placement with the other realms'
  rallies it kept away from.
- Unchanged: the breach each realm takes, the guards, the lord, keep levels, the storm thresholds, attempt endings and
  the holders' and stormers' fight rules. Not verified here: whether the rotated spots at Thidranki Faste are on the
  navmesh and clear of its guards (the Thidranki navmesh and guard posts aren't in the build container; the unit test
  models one guard post that reproduces `d64d005`'s `guardClear=1459`).

**Battleground stormers run for the lord like a zerg** (tests only; owner's PC, build `ef76db3`, Thidranki 2026-11-15
16:00-16:24, with the separate rallies and the launcher's starter gear: all six doors of Thidranki Faste down, Albion
holding it, 100 level 20-24 gamebots per realm, 25-39 of each attacking realm inside. Eight attempts, every one over in
0.8-2.6 minutes with "half the stormers fell", lord 100%, `onLordFloor=0`; builds `0026378` (41 attempts) and `d64d005`
(11) ended the same way).

- **Why they never got there.** The stormers did not fail at the lord or on his stairs: they never left their rally. Of
  the attacking realm's 110 deaths during the eight attempts, 106 were "Fighting ..." and 4 "Storming the keep"; 53 within
  1,300 of the realm's own rally, 37 elsewhere in the field, 17 at the gate (most of them to the Wizards) and 3 inside; to
  Albion's bots 43, the keep's guards 37 and the other attacking realm 26, mostly with one or two attackers on them. At
  16:07:26, with both realms storming, `BG_KEEP_STATUS` read "Fighting:43" and the nearest bot "Storming" stood 3,562 from
  the lord. A stormer took any enemy within 700 of it and any within 1,500 firing on a realm-mate within 1,000, and helped
  against any guard hitting a neighbour; and while its ten-second combat timer ran it stood where it was, even with nothing
  it would fight. Albion's bots (a third hold the walls, the rest roam the field around the keep) and the other realm's
  passing bots kept every stormer in a skirmish at its rally until half of them were dead. Two Hibernians did reach the
  lord's tower at 16:11 (78 and 154 from him, killed by a Scout and the lord), so the way up exists.
- **On the way up a stormer fights only what is on it.** Once its realm goes up, a stormer fights an enemy (guard, bot,
  player or pet) that has it targeted from within 500 on its own floor, and runs past everything else: the Wizards and
  archers on the walls, enemies fighting someone else, a realm-mate's fight. Hits from what it leaves alone no longer
  turn it round (it takes no aggro from them), and a stormer with nothing it may fight goes on up at once instead of
  waiting out its combat timer; a cast in progress is finished first. Beside the lord, on his floor, it fights the lord and
  everything with him, and helps against the guard hitting one of its own there, as before. Battleground lord rushes on
  live servers ran past the courtyard and fought at the lord.
- **Logs.** `BG_STORM_TRACE` now samples the committed stormers too, every 10 s per realm (`step=storm`, with `phase=`:
  gate, lord, target, strike, fight or march-under-fire, and the attempt as engaged/standing/seconds/passedGate, the
  height to the lord, whether it is in his fight, its target, the target's kind (lord, guard and its type, holder- or
  attacker- bot, player or pet, monster), distance and height, whether the target has it targeted and whether it may fight
  it, its aggro, attack, cast and health). The rally samples keep their 20 s. `BG_RALLY_REGROUP` says where the attempt's
  stormers fell (`fell=outside,gate,inside,lordFloor`), to what (`killers=guard,lord,holder,otherAttacker,other`), the
  fallen's median distance to the gate and the lord, how many got nearer the lord than the gate is (`passedGate=`) and how
  near any came (`bestToLord=`); `BG_RALLY_STATUS` adds `passedGate=` and `bestToLord=`.
- Unchanged: the keep, its level, guards, lord, doors and respawns; the rallies and their holders' fight rule; when a rally
  goes up (the strength model, patience, the other realm's cue) and when an attempt ends; reinforcements and leases;
  rally holders, gatherers, defenders, companion bots and real players. No truce, nobody made unhittable, nobody moved.
  Not verified here: whether the stormers now get through the gate and up the tower stairs in numbers (the Thidranki
  navmesh isn't in the build container), and whether the class AIs pick fights of their own on the way.

**Battleground storms go with the whole realm and clear the gate first** (tests only; not yet seen on the owner's PC. Owner,
2026-11-16: the bots should "successfully coordinate BG-appropriate attacks and defense, and ... the keep changes hands".
Owner's PC, builds `4d48f42` (05:39-07:56) and `c3cd93c` (07:59-08:25), Thidranki and Murdaigean: 111 attempts, the lord
never below 96%, no battleground keep changed hands; frontier keeps changed hands three times.)

- **What the logs showed:**
  - **The storms were half the realm.** The 107 `BG_RALLY_STORM`s of `4d48f42` went with 8-17 (median 10) of 11-26 inside.
    The rally waited for the strength model capped at a wave (60% of the realm), then went with half of that after 3
    minutes or at once beside the other realm's storm. `BG_RALLY_STATUS` never saw more than 72% of a realm gathered.
  - **They died before or at the gate.** Of the 718 stormers that fell: outside 315, gate 215, inside 174, lordFloor 14.
    Killers: the keep's guards 471, the other attacking realm 132, the holder's bots 88, the lord 15. The fallen's median
    distance to the gate was 797-1,825 (quartiles).
  - **One against one, strung out.** All 140 stormer `fight` samples were one bot with a GuardFighter (128) or
    GuardCommander (11) on it, 62-75 away, 218-798 from the gate. Not one was casting. The `gate` (march) samples of seconds
    0-49 stood 1,514-2,697 from the gate. The stormers reached the gate's guards one at a time, ran past the live ones and
    fought each guard alone.
  - **Healers did not heal the stormers.** An ungrouped gamebot healer (as stormers are) healed only itself: the realm-mate
    heal is for the frontier objective only. And the storm's turn returned before the brain's own heal check.
  - **The doors were already down:** Thidranki's outer gate 252000301 (the keep door 252000302 stands open), and both of
    Dun Murdaigean's doors. There was no door left to batter. The guards stood at 14-19 of 19 (Thidranki) and 17-27 of 27
    (Murdaigean) in every `BG_KEEP_STATUS`. Guards respawn after 15 minutes, give or take 10 (`guard_respawn`,
    `guard_respawn_variance`, unchanged in the owner's world), so a cleared guard stays dead 5-25 minutes.
  - **Every attempt ended within 1-3 minutes.** On `4d48f42`, 105 of 106 ended "half the stormers fell", after a median 1.2
    minutes (at most 3.2). All 5 of `c3cd93c`: `passedGate` 0-2, `climbs=-`, `footPeak=0`. #284's climb was never reached.
  - **Defence:** a third of the holder's bots hold the walls and the rest roam around the keep (`near=` 1-9 Albion). Those
    roamers killed 88 of the stormers.
- **Now:**
  - **The full muster.** A rally waits for 85% of its realm's gamebots in the battleground. After 4 minutes it goes with 80%
    of that, and after 7 minutes with 60%, never fewer than 8. The strength model is still logged (`modelled=`) but no longer
    lowers the muster.
  - **Joint storms stay (#280).** Joining the other realm's storm, or going with a realm that waits for this one, now takes
    80% of the muster, not half. A ready rally holds up to 90 s for the other realm (was 45 s).
  - **The march goes as a pack.** In the attempt's first 75 s, a stormer more than 450 ahead of its pack's median distance
    to the gate waits for the pack. This applies only outside the gate zone and never while it fights.
  - **The gate is cleared first.** Within 1,600 of the breach, before passing it, the whole storm fights one called gate
    guard at a time. A gate guard is a keep guard posted within 1,100 of the gate on its floor, never the lord and never a
    guard on the wall. The called guard is the one on a stormer nearest the gate, otherwise the nearest the gate; when it
    falls, the next is called.
    - Melee bots go at the called guard.
    - Casters and archers strike it from up to 1,350, closing in when farther.
    - Healers stand back within 1,200 and heal.
    - The storm goes in once the gate guards are down, or when none has fallen for 45 s (stalled; then in as before).
    - Hit-back on the way is unchanged.
  - **Storming healers heal the stormers.** They heal fellow stormers of their realm within 2,000 that are below 85%, with
    the same line-of-sight check as the frontier heal, and the storm turn runs the heal check first. Companions and
    players' groups are unaffected.
  - **A storm that kills guards goes on.** "Half the stormers fell" ends an attempt only 45 s after the last keep guard
    fell, and a guard kill counts as progress against "no progress on the lord" (3 minutes). A wipe still ends it.
- **Logs:**
  - `BG_RALLY_STORM` adds `share=` (going as a share of inside) and `rule=` (`full`, `joined`, `patient`, `lastcall`).
  - `BG_RALLY_STATUS` adds `musterNeeded=`, `share=`, `gateGuards=` and `clear=`.
  - `BG_GATE_CLEAR` (new) is written once per attempt when the clear ends. It gives `result=` (`cleared`, `stalled`,
    `none`), `second=`, `clearSeconds=`, `gateGuardsAtStart=`, `gateGuardsKilled=`, `gateGuardsLeft=`, `guardsKilled=`,
    `atGate=` and `clearPeak=`.
  - `BG_RALLY_REGROUP` adds:
    - strength: `going=`, `insideAtStorm=`, `goneShare=`
    - guards and the clear: `guardsKilled=`, `gateGuards=start->end`, `gateGuardsKilled=`, `clear=`, `clearSeconds=`,
      `clearPeak=`
    - the march and heals: `marchHoldTurns=`, `healPicks=`
    - `guardKillers=` (the stormers' guard killers by kind, to say whether wall archers or the gate's fighters kill them)
  - Storm traces add the phases `march-hold`, `clear`, `clear-approach` and `clear-heal`, and the fields `calledTarget=` and
    `role=`.
- **Unchanged:**
  - the keep, its level, guards, lord, doors and respawns
  - the holder's defence
  - the raid code and the navmeshes
  - the rally spots, the foot and the climb (#284)
  - reinforcements, leases and the waves while a gate stands
  - companion bots and real players
  - no truce, nobody made unhittable, nobody teleported
- **Not verified here:**
  - whether the full muster gathers in time
  - whether the gate guards come out to the clear or stay at their posts
  - whether wall archers (aggro 2,100) kill the pack at the gate (`guardKillers=` will say)
  - whether casters cast once given the guard (no fight sample on `4d48f42` showed a cast)
  - whether the keep changes hands

**Battleground storms: the climb from the tower's foot goes** (tests only; owner's PC, build `4d48f42` = the change below,
Thidranki and Murdaigean 2026-11-16 05:39-06:27. 25 storms; the lord stayed at 100% at both keeps; nobody reached her floor.)

- **What the log showed:**
  - No climb went from the foot in any of the 25 attempts (`climbs=-` in every `BG_RALLY_REGROUP`), and not one storm
    trace was of the walk up to the lord (`phase=lord`: 0 samples; 25 on `95af6fd`). Everyone was sent to the foot instead.
  - At Thidranki the foot (32278,38202,3750, 842 under Lady Thidranki) was reached and held: two `foot-hold` samples there,
    past keep door 252000302. The best attempts got only as far as the foot (`bestToLord` 725-874). At most 2 stood at
    the foot at once (`footPeak`).
  - The climb waited for 70% of every standing stormer, including those still fighting outside or back at the rally. Or it
    waited for one waiter's unbroken 25 s, and that clock went back to 0 whenever the foot stood empty. Both together held
    the climb shut.
  - Dun Murdaigean's walk had no point 600 from the guards' posts, so the first choice stood: 4,314 up its ramp, 188 from a
    post (`guardClear=188`). No stormer ever reached it (`footPeak=0`).
  - The `phase=gate moving=False` samples (31) are the turn the walk to the gate was given (`pending=True`: the trace
    is written before the mover takes the order). They are at the rally at second 0, or when a reinforcement is sent. The
    same bots stand 1,000-2,000 on 10 s later. `95af6fd` and `0acc99b` have them at the same rate.
  - The same goes for 8 of the 11 `to-foot moving=False` samples: the turn a bot reached the gate (`toGate=2-3`).
  - Most `march-under-fire moving=False` samples are written just before the turn gives the walk again. They come at the
    same rate as on `95af6fd` (8 of 22) and `0acc99b` (16 of 42). A few bots stood 10 s or more under fire although the walk
    was given again every 1.5 s (Rormur, Weeturnip, Oisoinnan, Ivargeildrik). The trace doesn't say why.
  - Bots that reached the rally as it went up kept the last attempt's `gatePassed=True` and walked to the foot straight from
    the rally (Crandira, Svaetiah, Ivargeildrik at second 0).
  - The other attacking realm killed 32 of the 154 stormers that fell. 19 of the 28 storms went with the other realm's.
- **Now:**
  - **The climb waits only for the stormers inside the walls** (nearer her than the outer gate is). It goes once 70% of
    those are at the foot, or 12 s after the first reached it. That clock keeps running when the first waiter falls.
    Nobody else inside: it goes at once.
  - **A climb that is working stays open** while anyone is up her tower, so stormers arriving later go straight up. It shuts
    once they are gone, and the foot gathers again.
  - **No foot when no point of the walk is clear of the guards' posts** (Dun Murdaigean). The stormers walk straight at the
    lord there, as before `#280`.
  - **Each attempt comes in through the gate again**, unless the bot already stands within the walls.
  - **Beside the lord**, a stormer fights the other attacking realm only when that bot is on it: the keep's guards and the
    holder come first. The realms stay enemies. On the way up nothing changes: stormers still hit back only at what is on
    them.
- **Logs:**
  - `BG_TOWER_CLIMB` (new): each climb, with `second=`, `atFoot=`, `comingInside=`, `upTheTower=`, `standing=` and
    `footHeldSeconds=`.
  - Storm traces add `cc=` (mez, stun or -) and `speed=` (a rooted bot reads 0), to say why a bot given a walk stands.
- **Kept from `#280`:** the joint storms (join window, holding for the other realm), the attempt going on while it works,
  and the lord pick on her floor. **Reverted from `#280`:** only the fallback that kept a foot beside a guard's post.
- **Unchanged:** the keep, its level, guards, lord, doors and respawns, the raid code and the navmeshes.

**Battleground storms reach the lord together** (tests only; owner's PC, build `95af6fd` = the change below, Thidranki
2026-11-15 22:37-23:00. The rallies were right: Hibernia at -90, Midgard at -30, 2,381 apart. There were 17 storms, and the
stormers reached Lady Thidranki's floor (`bestToLord=0`), but she never went below 97%.)

- **What the log showed:**
  - Of the 122 stormers that fell in 15 attempts, 107 fell before her floor (57 outside, 27 at the gate, 23 inside). Guards
    killed 57, Albion's bots 30 and the other attacking realm 23.
  - The 15 that fell on her floor reached it one or two at a time, 30-60 s into the attempt. Storm samples on her floor:
    4 at 30-44 s, 2 at 45-59 s, 1 at 60-74 s.
  - Every attempt ended "half the stormers fell" after 0.4-2.0 minutes. The two that reached her in numbers (`lordFloor:7`
    and `:3`, her lowest 97%) ended 1.5-1.6 minutes in. Their 13 and 9 still standing walked back out to the rally.
  - 9 of the 17 storms were tails: 8-12 bots sent 0.0-0.6 minutes after the realm's last attempt, because the other realm
    was still storming. Together they put one stormer on her floor.
  - The two storms that went with the other realm's, within 1-5 s (22:46:45/50 and 22:51:53/54), put 14 of the 15 that fell
    on her floor there.
  - On her floor everyone took the guard hitting a realm-mate first, and she only lost 2-3%.
- **Now:**
  - **The wave climbs together.** Each attempt finds the foot of her tower: the last ground-floor point (600 or more below
    her) of the navmesh walk from the gate to her, kept 600 from guard posts. The stormers walk there and wait. The climb
    goes once 70% of those still on the way are there, or after 25 s. It stays open 10 s, and a stormer that has started up
    goes on. `BG_TOWER_FOOT` logs the walk and the foot.
  - **An attempt goes on** while stormers are up her tower, or she lost health in the last 30 s, even with half fallen. The
    fallen come back in the reinforcement groups. A wipe ends it ("every stormer fell"); the healed-back and no-progress
    ends are unchanged.
  - **The two realms go up together.**
    - The other realm's storm is joined with half of what is needed only in its first 45 s: its push, not its tail.
    - A ready rally holds up to 45 s while the other realm gathers with 40% or more of its need. The other realm then
      goes with half, as it would to join a storm.
  - **On her floor**, within 900 and on her height, casters, archers and healers strike her. Melee bots take the guard or
    defender on them, else her. The pick holds in their combat turns too.
- **Logs:**
  - `BG_RALLY_REGROUP` adds `floorArrivals=`, `arrivalSeconds=` (each stormer's first second on her floor),
    `arrivalSpread=`, `climbs=` and `footPeak=`.
  - `BG_RALLY_STORM` adds `otherStormSeconds=`, `otherWaiting=`, `heldSeconds=` and `foot=`.
  - `BG_RALLY_STATUS` adds `readySeconds=`, `floorArrivals=`, `climbs=` and `climbOpen=`.
  - Storm traces add the phases `to-foot`, `foot-hold`, `foot-path-refused` and `lord-fight-pick`.
- **Unchanged:** the keep, its level, guards, lord, doors and respawns; the strength model and patience lengths; the rally
  spots. No truce, nobody made unhittable, nobody teleported.
- **Not verified here:** where the foot falls on the real navmesh, whether guards reach it, and whether the waves bring her
  down. The Thidranki navmesh isn't in the build container; `BG_TOWER_FOOT` and the new `BG_RALLY_REGROUP` fields will
  tell.

**Battleground rallies stay on each realm's own side and never share a spot** (tests only; owner's PC, build `0acc99b` =
the change below, Thidranki 2026-11-15 20:43-21:02. `BG_LORD_PATH` showed the walk to Lady Thidranki reaching her floor:
`status=PathFound`, last point her own, `lastZMinusLord=0`, `lordOnMesh=True`. Only Midgard stormed, three times with 8-16,
and nobody got near her.)

- **Why.** Thidranki Faste has one way in, outer gate 252000301 on the east side. Keep door 252000302 stands inside the
  walls, behind that gate, and the other four doors are not attackable (posterns, always shut). So there is no second gate
  for Hibernia to rally at. Both attacking realms arrive on the gate's south side: Midgard 33 degrees off straight out,
  Hibernia 127 degrees. In the rally spots tried until now (900-1,800 out, up to 60 degrees round), the only two 2,250
  apart were +60 and -30. -60 at 1,800 stands by the wall's corner. So the pair put one realm across the gate front:
  - Midgard at +60 drew 5 in 5 minutes (needed 14).
  - The barren rule swapped the two rallies, and Hibernia at +60 drew 5 (needed 18).
  - That re-plan left out both barren spots, found nothing 2,250 apart, and put both realms on 36133,38275 (`apart=0`), as
    on `d64d005`.
- **Why the storms were small.** A rally that moved kept its old gathering clock: Midgard went up with 9 of 13, 56 s after
  its rally moved, on the six-minute last call of the spot it had left. Its third storm went with 8 of 15 from the spot it
  shared with Hibernia. The stormers fought back as before: `#271`'s floor rule only applies above 150, which the hit-back
  rule already had.
- **Now:**
  - Rally spots are also tried farther out (2,100, 2,400 and 2,700, up to 90 degrees round), but only well outside the walls
    (600 farther from the keep's centre than the gate). At Thidranki Faste that gives Midgard -30 at 1,800 (its spot on the
    PC) and Hibernia -90 at 2,700, south of the wall's corner, 3 and 37 degrees off their arrivals.
  - A realm planning alone takes a spot on its own side (within 90 degrees) first, the near spots before the far ones.
  - A spot that gathered nobody is taken only after every other separated spot, never dropped for one on top of another
    rally.
  - A re-planned rally keeps its spot if it is still apart and the new plan is not, or the new plan only found a barren spot
    (`BG_RALLY_KEPT`).
  - A rally that moves starts its patience and last call again.
- **Logs:** `BG_RALLY_CANDIDATES` after each placement lists every spot tried: bearing/stand-off, guard clearance, `x`
  under a guard, `+`/`-` walk checked, `b`/`p` barren for the realm/partner, `*` taken, `^` left to the partner.
  `BG_RALLY_PLACED` adds `usedBarren=` and `restartedGathering=`.
- **Unchanged:** the keep, guards, lord, doors and respawns; which gate is the breach; the strength model, patience lengths
  and attempt endings; the stormers' fighting rules. No truce, nobody made unhittable, nobody teleported.
- **Not verified here:** whether -90 at 2,700 is walkable and clear of the corner's guards on the real navmesh (the
  Thidranki navmesh isn't in the build container: `BG_RALLY_CANDIDATES` will say), and whether the bigger storms bring her
  down.

**Battleground stormers climb to the lord, and the two rallies are placed as a pair** (tests only; owner's PC, build
`c165c90` = the zerg change above, Thidranki 2026-11-15 17:23-18:02: Albion holding Thidranki Faste with every door down.
Midgard stormed five times: up to 11 past the gate, `bestToLord` 398-2,162, up to 2 beside the lord. Lady Thidranki stayed
at 100% every time and nobody stood on her floor. Hibernia never stormed: its rally read 0-4 of 19-20 for 23 minutes.)

- **Why nobody reached her floor.** The navmesh does reach it. On builds `0026378` and `d64d005` Hibernian bots died beside
  her at 4,592 and on the tower's square stair at 4,423-4,524 (the stair winds round her column: 3,955 at the keep door
  side, then 4,064, 4,157, 4,221, 4,284, 4,380, 4,465 and 4,524). Two things on `c165c90` stopped the stormers:
  - **They were stopped every turn.** The controller's combat turn stopped a moving bot (and replotted its route) before
    the stormer's "go on up" rule ran, on every turn its ten-second combat timer ran. Every `march-under-fire` sample read
    `moving=False`, and the stormers crossed the courtyard to the keep door in 10-second steps of 150-600. That is why
    they stood at 32591-32641,38213,3725, just inside keep door 252000302, under her.
  - **A target above stopped the climb.** Conyff stood on the stair at 32285,38215,4053, 539 under her, with her as its
    target, `attacking=False` and `moving=False`. Any target the stormer may fight, and the lord within the 900 strike
    radius, stopped its walk, and neither a swing nor the brain's chase climbs a stair.
- **Now a marching stormer keeps walking**: the "go on up" rule runs before the combat stop.
- **A stormer fights from where it stands only what is on its floor** (within 150 of its height; the lord also within
  900). A target above it, such as the lord upstairs or a scout a floor up, has its aggro dropped and is walked toward. Melee
  bots close in on the same floor. Ranged bots and casters fire from where they stand on that floor: we have no
  line-of-sight test for a bot's cast, so being on the same floor stands in for one.
- **The walk goes to the navmesh point on the lord's floor** (within 96, then 256, of her and 150 of her height) when her own position
  is off the mesh. Before, a failed walk could be settled by the mover's goal snap on a floor below her.
- **Why Hibernia never stormed.** Both realms rally at the same breach, outer gate 252000301, and the realm that placed
  first took its own side. Midgard placed 4 s before Hibernia and took -30, its own side, at 35892,37375. Hibernia arrives
  at -128, from far behind the wall. The only candidate 2,250 from Midgard's rally was +60, at 35233,39834: the far side
  of the gate. Hibernian bots walked round the keep past Midgard's rally and Albion's roaming holders, and 187 of them
  died in 23 minutes (145 to bots). The `gate=252000302` in the Hibernian rally traces is just the door nearest the bot,
  not the rally's gate: both realms' rallies use 252000301. On `0026378` Hibernia happened to place first.
- **Now the two attacking realms' rallies are planned as a pair.** Each realm works out the same pair from its own side,
  so the order they place in no longer matters:
  - both spots clear of the guards' posts and 2,250 apart
  - the pair whose worse side is nearest its realm's arrival comes first
  - at Thidranki Faste that is Midgard +30 and Hibernia -60, 63 and 67 degrees off their arrivals
  - a gathering rally that stands where the pair wants the other realm moves there on its next turn; a storming one stays
    put, and the later realm is placed alone, away from it
- **A rally that gathers nobody moves.** A gathering rally that never drew 8 in 5 minutes, with 8 or more of its realm in
  the battleground, is re-placed. Both realms' plans then pass over that spot (up to four are remembered per keep and
  holder), and `BG_RALLY_BARREN` logs the move.
- **Logs.**
  - `BG_LORD_PATH`, once per attempt for the first walk to the lord from inside the gate and once for the first stormer in
    his tower below his floor. It gives: the walk target and its kind (`lord`, `lordFloor`, `lord-offmesh`); the path
    queried as the mover queries it (status, nodes, last node, `lastZMinusLord`, `lastToLord`, `highestZ`); whether the
    lord stands on the mesh, and the nearest floor to him; and the bot's goal snap, mover destination and state.
  - `BG_RALLY_REGROUP` and `BG_RALLY_STATUS` add `bestBelowLord=`, the least height under the lord of a stormer within 400
    of him.
  - `BG_RALLY_STATUS` adds `peakRallied=` and `plan=pair|alone`.
  - `BG_RALLY_PLACED` adds `arrivalBearing=`, `reason=` (`new`, `gate`, `yield`, `replan`, `barren`), `plan=`,
    `generation=`, `barren=` and `partner=`.
- **Unchanged:** the keep, its level, guards, lord, doors and respawns; the strength model, patience and attempt endings;
  the stormers' rule of which enemies they may fight; rally holders, gatherers, companion bots and real players. No truce,
  nobody made unhittable, nobody teleported.
- **Not verified here:** whether the stormers now climb the stair in numbers and bring her down (the Thidranki navmesh
  isn't in the build container), and whether casters on her floor really have line of sight.

**Battleground and RvR kill quests count enemy gamebots** (tests only; owner's question 2026-11-15: "are there quests in the
BG? Are those functioning correctly?").

- **Killing an enemy realm's gamebot counts as an enemy player kill.** Pazz's battleground dailies ("Fen's New Friends" in
  Thidranki and Caledonia) and the Atlas realm quests (the realm's daily and weekly player kills, Every Little Bit Helps,
  For the Realm, the Darkness Falls weekly, the old-frontiers monthly and the hardcore "kill a player") counted only a real
  player victim, and in an offline world the enemy players are gamebots, so none of them could be finished. One rule
  (`RvrQuestTargets.IsEnemyPlayer`) now counts a real player as before and a dying gamebot of another realm; every other
  condition (realm, con, region, steps) is unchanged, and a kill is credited as before (your own, your pet's or
  companion's, an invited gamebot's, or a groupmate's in range). A bot's pet doesn't count, as a player's pet never did,
  and a gamebot that leaves the world mid-fight (a stable ride, a port, a despawn) is not a kill.
- **Caledonia's Midgard kill quest can be taken.** It asked for levels 20-24 (copied from Thidranki upstream) and looked
  for a giver named "Rey", so a second Pazz stood in Midgard's portal keep; it now asks for Caledonia's 30-35 like its
  Albion and Hibernia twins (see the polish entry above) and shares the one Pazz (since replaced by Caledonia's captains,
  above).

**Bots no longer follow the player into another realm** (tests only; owner's decision 2026-11-15: "No, bots should not
follow a teleport into another realm."; owner's PC, build `1dc66b6`, 2026-11-15 ~01:46: a GM `/jump` to a Midgard raider in
Tuscaran Glacier carried all six Albion gamebots of a bot-led party into Midgard's Kvasir fight).

- **A player's teleport, portal, jump or MoveTo into another realm's territory leaves every bot where it is.** That is
  another realm's classic home zones, dungeons, capital, housing, Shrouded Isles and epic dungeons (`RealmTerritory`);
  frontier zones, battlegrounds, Darkness Falls, the Old Frontiers dungeons and neutral zones are unchanged. It covers
  gamebots in the player's group and in a bot-led party hosting the player, and /spawn companions (also the end of a
  stable ride, the stuck watchdog's move to the player and a released gamebot's return to the player). A bot-led party
  logs `BOT_LED_NO_FOLLOW reason=enemy_realm` once (`BOT_NO_FOLLOW` for other bots), is not disbanded, and its
  "player far away / where u at" pacing takes over.
**A "+ Lv.20" button for the battlegrounds** (tests only; owner's PC, build `1dc66b6`, 2026-11-15: every gamebot on the
test world was level 50, because the realm cards made only level 1 or 50, so every battleground was empty,
`BG_KEEP_STATUS near=0/0/0`; owner, same day: "a Lvl 20 button for bot adds makes sense").

- **Each realm card has "+ Lv.1", "+ Lv.20" and "+ Lv.50".** "+ Lv.20" makes bots for the Thidranki battleground: each
  gets a random level from 20 to 24 (the server's bracket; a Battleground table row overrides it, as on the server), that
  level's experience from the server's own table and a purse of its level squared in silver (4 to 5.76 gold), and starts
  in its capital beside the realm teleporters (its starter gear: see "They arrive with starter gear" below). Shift-click makes 10, Ctrl-click 100, in
  one protected batch, and the Sluaghbinder edition switch applies as on the other buttons. Where no merchant weapon
  fits, the server gives it its usual level-matched starter weapon; a bow or instrument where the class has one, as
  every gamebot gets.
- **They arrive trained, like "+ Lv.50" bots.** A never-saved bot above level 1 is trained in its lifetime build at its
  first login (`GameBot.ArrivesTrained`). Before, it would have fought with every line at 1 until a break between PvE
  tasks took it to a trainer. A bot that levelled by playing still visits its trainer.
- **They go to Thidranki only for the Battlegrounds % share** the Bot Goals Setting tab gives Levels 20–49 (0% by
  default). The rest do solo/group PvE or RvR as usual.

**They arrive with starter gear** (tests only; owner 2026-11-15, "Arrive with starter gear"; owner's PC, build `0026378`,
Thidranki 13:45-14:00: 183 of 410 bot deaths there were bots wearing no armour, and storms of about 20 "+ Lv.20" bots all
died before they reached the keep gate).

- **A bot that arrives already trained gets plain merchant gear for its level, as if it had levelled normally.** At its
  first login, each armour slot its class can wear, its weapon (and its shield, for a sword-and-board build, or its
  offhand weapon, for a dual wielder) gets the best plain item its own realm's merchants sell at its level: real merchant
  items, nothing magical, dropped or crafted, and only what its class may use. It is free: the bot keeps its purse. The
  server log shows one `AUTONOMOUS_STARTER_GEAR` line per bot with every piece.
- **Once, and never over gear it already has.** A bot that wears any armour is left as it is, nothing it carries is moved
  or replaced, and a bot that had its starter gear never gets it again (it is saved with the bot). If no merchant item
  fits, it tries again at its next login.
- **"+ Lv.20" bots added before this version** that still wear no armour get it at their next login too (the launcher
  binds such a bot in its capital; a "+ Lv.1" bot is bound in a starting area).
- **Unchanged:** "+ Lv.1" bots still start without armour and earn it, "+ Lv.50" bots keep their level-50 set, and /spawn
  companions and players are not affected. A Savage keeps its usual starter weapon (its armour comes from the merchants).

**A bot-led party stuck in Caer Diogel** (tests only; owner's PC, build `1dc66b6`, 2026-11-15: party albion-f86e88-1370,
on its way to griffins in Gwyddneau with the player hosted, stood in Caer Diogel from 00:55 to past 01:19 in phase
"Traveling" with no travel line in the log, while two of its members were in Camelot at 01:05).

- **A PvE party's member far away no longer holds it for good.** The 45-second stall rule only let go of a member in the
  leader's region that stood still; one in another region, or one walking without getting closer, held the leader in
  "Waiting for group members" with no limit. After 10 minutes without coming 200 units closer (or back into the leader's
  region) such a member no longer holds the party (`AUTONOMOUS_GROUP_MEMBER_LONG_HOLD` names it, where it is and what it
  does): the leader goes on to the camp and the member keeps travelling there. Warbands and raid parties are unchanged.
- **The fight is called in /group in a party that hosts the player.** A gamebot that leads never joins the player's group,
  so its combat calls (and B4's leader pull call) never ran: from 01:36 to 01:43 the party fought orcs and a griffin with
  the player and said nothing in /group. They run now, as in the player's group; a raid party's guest seat is unchanged.
- **No pull calls in /say from a party on the road, in town or with the player.** "pulling" and "need pow" came in /say
  from the party standing in Caer Diogel, and "griffin inc" beside its /group calls. A grouped bot says those only at its
  camp or in a fight, and never in /say when the player is in its group. The /group "join" hellos ("hi all, healer here,
  try not to die") are no longer posted in /lfg.
- **The welcome fits where the player is.** "hey, we are out at Aldland, come find us" went to a player 1,186 units from
  the leader in Caer Diogel. A player within 1,500 units gets "stick with me, we head to griffin" now, and the place named
  is the town the leader stands in (Caer Diogel), not only its zone.

**Raids after the sweep of 2026-11-14** (owner's PC, build `e3b8238`: Caer Sidi, Tuscaran Glacier and Galladoria all
lost, and Albion's Summoner's Hall never reached). They pass automated tests; nobody has played them in game yet.

- **A pull counts the whole living raid.** The early-pull rule counted only the raiders already inside, so a handful
  after a wipe passed for half the raid (16 of 18 in a raid of 118). Raiders walking back count now; a boss that chases
  is fought by everyone inside only when they are 70% of the raid, otherwise those it caught fight and the rest gather.
- **After a wipe the raid regroups outside, not in town.** Raiders release at their party's staging post outside the
  dungeon (`realm_raid_release_at_staging`), and a corpse with no living raider within 3,000 releases after 20 seconds.
  The dungeon a raid walks through counts as inside. A "waiting for formed parties" hold moves its front to the main
  body after 6 minutes.
- **A living boss on the way in is a raid objective.** The three Summoner's Hall routes and Crystal Cave walk through
  another dungeon. A boss whose aggro covers that walk (Aidon, who killed 182 raiders on the Albion route) is mustered
  for and fought with its own tactic, attempts and wipes counted, before the raid goes on (`realm_raid_route_gates`).
- **A raid dungeon's epics wait while its raid is inside** (`realm_raid_hold_epic_respawn`): an epic with a respawn of
  10 minutes or more comes back after the raid ends, so a cleared room stays cleared (Easmarach came back mid-raid and
  closed Galladoria's last room).
- **No muster in a boss's aggro.** A muster point is kept 300 outside every other living boss's aggro (one was placed
  76 from Icelord Agmundr). Raid-wide deaths outside an attempt now count as a wipe.
- **Boss plans.** Icelord Agmundr: his hrimthursa first, casters and healers 750 from his target, everyone out of the 500
  around it during his cold blast. The Crypt Lord: his court cleared first while his patrol is far, melee on him. The
  Host: his linked monsters first, an ambush on his patrol, every raider on the real one. Lich Lord Ilron: his dark
  lights and eyes first. Spindler Broodmother: party posts 700 apart against her mesmerize, add parties sized to her
  spawns without the druids and wardens, a burn below 25%. A perched griffon is left until it lands. A tactic's hold on
  a pull lapses after 20 minutes.
- **A raid fights the monster attacking it.** A route rule could turn a monster attacking the raid into a "switch" to the
  raid's own idle target every 10 seconds (Easmarach 208 times); in-combat monsters are now left alone.
- **A boss no longer yells its aggro line as it dies** (tests only). The killing blow's attack came after the boss's
  death had closed its fight, so it opened a new one: Spindler Broodmother and Aidon yelled their aggro line right after
  their death line (sweep of 2026-11-15, build `1dc66b6`). A dead boss starts no fight now.
- **Nothing behind Easmarach's falls while he lives** (owner's PC, 2026-11-15, build `1dc66b6`). After Spindler
  Broodmother the Galladoria raid mustered for a deep shrouder behind his push; it bounced off the falls 165 times in
  eight minutes (78% fall damage each) and never fought him. The route's gate looked only at the last room's bosses; now
  no target, muster or staging lies behind the falls while he lives, and the raid fights him first. A bot the push moves
  waits where it lands while its way back goes into the falls (at most 3 minutes). The push and its fall damage are
  unchanged. Tests only.
- **Summoner Lossren's exploding ghoul** came only at exactly 20 killed souls at a 5-8 s check, so a quick raid never saw
  it again that fight; now at 20 or more.
- **The sweep's guest** follows a living raider at the raid's front, never a dead or released one, and lands on that
  raider's floor (the GM fell into the void five times).
- **A sweep that never ended no longer leaves its guest a plain player.** The server was stopped while a sweep ran with
  a guest, and the GM account came back at player level ("No such command" for every GM command until Make Me a GM was
  turned off and on). The sweep now saves the guest's GM level and realm before it changes them; the next server start
  or login puts them back (`EPIC_SWEEP_GUEST_RESTORED` in the server log), and a normal stop ends the sweep first. Only a
  level the account had before the sweep is restored, so no account gets GM it did not have. A progress transfer does
  not carry the record (a transfer turns GM off), but it puts the guest's realm back. Tests only so far.
- **A raid's defeat names the boss that beat it** (tests only). Albion's Summoner's Hall raid lost at Aidon in Hall of the
  Corrupt (sweep of 2026-11-14, build `1dc66b6`) and every player read "driven out of Summoner's Hall by
  GrandSummonerGovannon!". The defeat of a boss on the way in is read now, and with no defeat the final boss's display
  name is used, never its class name. The raid news and chat say the same boss.
- **The raid's wipe line says who was in the attempt** (tests only). `REALM_RAID_WIPE` adds `engaged=` and `fallen=`: its
  `living=` counts the raiders who released and stand at the staging post outside, so Aidon's second wipe (65 of 65 in the
  attempt fell) read as a wipe scored while the raid stood. The scoring itself was right.
- **Nothing a bot raid does heals Icelord Kvasir with cold** (tests only). On the owner's PC (2026-11-15, build
  `1dc66b6`) the Midgard raid fought him 18 minutes and never took him below 81%: he jumped 90→99, 81→99 and 90→97 in
  combat, and in the second attempt the chat read "thank you spirit champion for healing me !" five times in 30 seconds
  while the raid did 24,000 a minute to him. The rule (cold heals him a fifth) stays on and his script is unchanged; the
  bot filter had stopped only bots' own cold spells. Now no bot's pet that deals cold goes at him (the Spiritmaster's
  spirit swordsman, warrior and champion buff themselves with a 70 cold DD proc; the Sluaghbinder's zombie magician casts
  cold), and no bot casts a spell whose sub spell is cold, an area spell that would reach him from an add, or a style whose
  proc is cold (the berserker's Aurora Borealis). A bot whose melee weapon deals cold switches to its clean weapon set or
  does not swing; an autonomous one fights his icetouch adds instead, like one whose armor's reactive proc is cold (95
  cold DD on Tuscaran loot), and a healer with such armor heals from 1,000 away. Every heal is logged as
  `EPIC_KVASIR_HEALED` (who, pet and owner, class, spell, style, weapon). His attempt also ended with no wipe and no line
  at all after 85 raiders had died 104 times in it: the fallen were counted only when he reset, after the return waves
  had brought them back. Raiders who fall during an attempt now count as fallen in it, and an attempt that was no wipe
  says so (`REALM_EXPEDITION_ATTEMPT_ENDED`).
- **Whoever a fight-at-home boss chases out of its home leads it back** (tests only). Only a raider whose own target was
  the boss did; Aidon (home 1,100, tether 1,500) went after the healers at the muster 1,400 out, past his tether, and
  healed from 58% to 100% in combat. Now the raider he chases, a healer too, walks to his spawn. The boss is unchanged.
- **Warlord Dorinakka and his knights are one fight** (tests only). On the owner's PC (2026-11-15, build `1dc66b6`) the
  Albion sweep raid (~123 raiders) cleared his crypt knights more than 700 from him one by one. It mustered for each one
  1,400 from the knight, which is 2,250 from him and past his 2,000 tether. The first knight died alone. 16 s after the
  second was called, he joined with the ten knights left (02:46:53). The fight then ran at two places at once, the
  knights' muster and the hall's mouth. He spent much of it out of his tether, where he takes no damage, and was reset
  nine times. The raid did him 3,334 damage and the knights about 206,000. It took 218,888 from the knights and 59,443
  from him, and wiped at 02:50:41 (91 engaged, 91 fallen, him at 96% at the lowest; 88 of the 108 deaths were to
  knights, 23 of them healers). Every knight joins him once he fights, and a knight's fight reaches the next knight in
  its column (190 apart, aggro 200). So no knight is pulled on its own while he lives. A knight the route picks counts
  as his fight, and the whole raid musters for him 1,400 from his spawn, inside his tether. In the fight, the party he
  fights holds him (its melee from behind). Everyone else but the healers, melee included, kills the knights one at a
  time: the most hurt first, a mesmerized one last. He and the knights are unchanged.
- **The Crypt Lord's muster stays off his patrol, and his court is cleared again before he is pulled** (tests only).
  The same raid (owner's PC, 2026-11-15, build `1dc66b6`) was lost at him with him at 99%:
  - A forerunner's muster was moved off where he stood to a spot 700 from his patrol and 355 from a court fate's
    bearer. That fate's bearer, 430 from his patrol, then fought the raid where it stood. He walked past and joined, and
    his court ran to the raid. In the minute to 02:57:27 the raid took 204,613 damage and lost 99 raiders in two
    minutes.
  - The route then mustered for him at 27,960,40,280, 115 from his patrol and 194 from a court reckoned soul, with most
    of his court alive. Deaths there counted as wipes at 03:00:10 and 03:04:54, and the raid ended (31 of the 296 deaths
    from 02:55 to 03:05 were his; the rest were his court's).
  - Now no muster, his own included, lies within 1,100 of his patrol loop while he walks it, or within 500 of a court
    monster. When the raid musters for him again after a skirmish or a wipe, the route clears his living court first, as
    in Darkness Falls. He and his court are unchanged.
- **The first-hour sweep does not chase respawns off its way back** (tests only). In Krondon (owner's PC, 2026-11-15,
  build `1dc66b6`) the raid spent about 13 minutes after Drihten Elreden died (03:35:35) killing thrawn ogre bocere
  again (180 s respawn) in rooms it had already cleared, 101 kills by 03:48; Orylle was still won at 04:03. Now the
  sweep skips a monster that has come back since the route began, unless it stands on, or within its aggro of, the walk
  from where raiders come in from their staging post to the current target, since raiders coming back after a wipe walk
  that way. A boss's clear-first monsters, encounter adds, epics and the final are always targets. The skipped ones are
  logged once a minute (`REALM_EXPEDITION_RESPAWNS_LEFT`). By the owner's decision the ten-minute "trash stays down"
  respawn hold stays for the neutral raids only (Darkness Falls, Summoner's Hall). The code had listed Caer Sidi,
  Galladoria and the realm raid dungeons too, but the hold never applied in them, so that list now names only the two
  neutral dungeons. No respawn timer changes.
- **The raid leaves one of Summoner Lossren's tortured souls alive** (tests only). Her script calls her next wave of
  18-25 souls only when none of hers lives, and every 20 that die bring an infected ghoul. On the owner's PC (2026-11-15,
  build `1dc66b6`) Hibernia's Summoner's Hall raid lost twice at her (64 bots, then 88 with the Epic Special Forces). It
  cleared every wave, so she called 14 waves in five minutes and 18 in ten:
  - Her souls killed 98 of the 170 raiders who fell (45 of 73, then 53 of 97), up to 21 of them on one raider; she
    killed 65 and her ghouls 7. In one minute the ghouls' bursts hit 80 raiders and pets for 66,748.
  - The melee fell first and the healers later. The healers trying to resurrect had 2-6% power from the third minute.
  - Both attempts ended with nobody left on her ("left combat", lowest 63% and then 20%). No raider was pulled or
    moved (no displacement in her room). At the first pull she chased a druid 2,290-3,825 from her spawn for two
    minutes, out of her tether, until she was leashed home. The lead-home change above (after `1dc66b6`) covers that.

  Now one soul of hers is left alive, the healthiest of her wave with no damage over time on it
  (`EPIC_LOSSREN_KEPT_SOUL` when it is chosen). No bot, bot's pet or area spell hits it, the casters' add and clump lists
  skip it, and a raider on it goes back to her (a caster to a living ghoul). After her first wave no new one comes, and at
  most one ghoul. The owner approved this on 2026-11-15 with one change, now in: the kept soul is held mesmerized, the
  period way. One crowd-control bot of the raid (`EPIC_LOSSREN_SOUL_HOLDER`) keeps it mesmerized with a single-target
  mesmerize and casts it again before it wakes; failing a mesmerizer a stun, then a root damage doesn't break. It never
  damages the soul. With no such bot in the raid the soul is only left alone. Lossren, her souls and her ghoul are
  unchanged. Sources: in 2003 her adds were trivial; the 2006 walkthrough says casters clear the souls and all melee go
  on Lossren; a later guide (Eden) says mesmerize the adds and use no area damage.
- **A raid released in the frontier stands together** (tests only). On the owner's PC (sweep of 2026-11-14/15, build
  `1dc66b6`) Albion's Summoner's Hall raid walks through Hall of the Corrupt, whose way in is in Pennine Mountains. All 59
  Pennine deaths came after the first Aidon fight: raiders released at their party's staging post there were picked off
  by enemy-realm bots, some two or three times in a row (Edor at 22:57:53 in Hall of the Corrupt, then 22:59:41 and
  23:00:05 in Pennine; Armois 22:59:43 and 23:00:21). The owner's decision (2026-11-15):
  - A raider released at a staging post in an RvR/frontier zone goes to one raid-wide post (the staging post nearest the
    middle of them all), and the return wave there gathers at it too, so the released raiders stand and fight as a raid
    (`realm_raid_frontier_raid_post`, on).
  - When enemy players, bots, pets or guards are within 2,300 of that post (the threat scan radius), the raider releases
    at the raid's hub instead. Those released there wait at the hub and go back together (all of them there after
    90 seconds, eight after 4 minutes, or anyone after 8), then join the return wave, never one by one. RvR is unchanged:
    nobody is immune.
  - Staging posts outside the frontier keep the party's own post. `REALM_RAID_RELEASE_POST post=raid|party|hub reason=...`
    logs each frontier release, and `REALM_RAID_HUB_GROUP` each group leaving the hub.

**The play test's sweep (GM only).** `/epictest sweep start [all|event,event] [guest]` runs every raid once, one after
another, on the server alone: the 21 raid targets in the game's order, Darkness Falls last. Each raid runs at its period
size and ends on its own clocks, and the sweep stops one after 4 hours. A raid that can't start (its boss respawning, no
bots free) is tried again after the rest, up to three times. With `guest`, the GM takes a seat in every raid. Their realm
is set to the raid's realm until the sweep ends, and the server keeps them with their party: away for 20 seconds, they
are moved back; dead for 30 seconds, they are raised there. Every step is logged as `EPIC_SWEEP_*`. `status`, `skip` and
`stop` follow it. Nothing runs unless a GM starts it.

For a night that has to fit many raids (2026-11-15, [OVERNIGHT-SWEEP-1.2.md](docs/OVERNIGHT-SWEEP-1.2.md)): a comma list
runs in the order given; `parallel` runs one lane per realm side by side, one raid at a time in each, bots only;
`/epictest sweep add <list>` adds raids to a running sweep (a long list typed in parts); `cap=<minutes>|off` replaces
the 4-hour cap; and a lane never sends its raid into Summoner's Hall or Darkness Falls while another realm's raid is
inside (`EPIC_SWEEP_HELD`). `skip <event>` names the raid to end. Tests only: no parallel sweep has run on the owner's PC.

**Lean server memory (a test option, off by default).** A new Options-tab switch starts the server with .NET's
memory-saving garbage collection (4 heaps and "conserve memory", from the owner's friend Ryan's Linux setup). On a
16-thread PC the server reached 7 GB after three hours of build 8, of which 1.9 GB was in use. The option changes
nothing in play and takes effect at START SERVER. The server log's `SERVER_MEMORY_DIAGNOSTICS` line names the settings in
force, so a session with it on can be compared with one without. Tests only so far.

**Audit fixes (2026-11-13).** A review of everything the fork added since its last upstream sync, on the 1.2 candidate.
- **A passport carries only character tables.** Its checksum proves the file is whole, not who wrote it, and arrival
  inserted every table the file named; a hand-made passport could have planted an account (a GM one) or server settings
  through the Passport Desk. Anything but the travelling tables now refuses the whole passport, with nothing changed.
- **The Passport Desk's port (login port + 10) is opened** with the game's ports while hosting, and on the router with
  internet hosting. It was left closed, so a passport could not reach it through Windows Firewall.
- **Wrong passwords from another PC lock that address out** (five in ten minutes), for game logins to existing accounts
  and for the Passport Desk, as for the host password. Upstream had no limit. A new friend's own password is 6 or more.
- **Logs:** server.log and warn.log keep 3 days, routine bot travel and goal lines go to detail.log (2 days), and every
  log has a limit; an install's older log settings are replaced at start (kept as `logconfig.xml.before-2`). A full
  server wrote about 300 MB a day and kept 7 days.
- A raid forced from the launcher ends after three hours without progress; the patcher refuses paths outside the game
  folder; MySqlConnector 2.6.2 and OpenTelemetry 1.20.1 (no advisories left); a small leak of player groups fixed.

**Bots dress up, at the game's own prices** (owner, 2026-11-12/13). A gamebot spends part of the coin above its reserve
(5 gold a level) on the world's own dye items at a merchant: cloth dye, leather dye or enamel for its armor, and a weapon
luster for the ones that like to show off (about 196 gold, as the game prices it). Each bot has a fixed style: about a
fifth never bother, others wear one tidy everyday colour, a few are peacocks in the dearest shades; colours lean on the
realm, and a guild often wears one. Bots never buy glows: glowing weapons come from epic and dragon drops and the
Darkness Falls merchants, and the Effect Master stays a player's choice. Settings: `bot_cosmetics`,
`bot_cosmetics_price_percent`. Log: `BOT_COSMETIC`. Seen on the owner's PC: build 6 loaded 207 dyes and bought them at
their real prices; tidy bots paid up to 19 gold a piece there, so they now keep to shades of a gold or less.

**A raid has no time limit; it ends when it is beaten, blocked or stalled** (owner, 2026-11-13: "if that's what it takes
to get through the dungeon, we shouldn't have time outs"). Caer Sidi was cut off by the four-hour window with 12 named
down and still moving. A raid in battle now ends when it wins, runs out of attempts, or goes an hour with no named or
boss down, no wipe and no new low on its target (`REALM_RAID_STALLED`; three hours for a raid forced from the launcher).

**The boss's tale, then the call with a horn; a realm's raids every four hours** (owner, 2026-11-12). The realm's
storyteller tells the raid boss's tale in /faction first; about fifteen seconds after its last line the realm is called
with a line across the screen and a horn, and only then are bots recruited. A raid is never lost to its tale. Each realm
opens its own raid every four hours (240 minutes; a world on the old 150 default moves once). Seen on the owner's PC,
builds 3 and 4.

**Smaller raid fixes from the play tests.** A raid invite box whose raid already left says so instead of "no spot left"
(seen expiring on build 5). A player in a disbanded group is told, so the old raid party leaves the group window. A
storyteller says nothing else mid-tale, and a player's tale request is always answered. Return waves hold only raiders
who have been inside. A bot-led group's let-go logs its real reason.

**A bot the launcher makes at level 50 arrives trained in its build** (owner's PC, 2026-11-11: of 2,700 new level-50
bots, 792 had trained after 80 minutes). A level-50 gamebot that never trained spends its build's points when it loads.

**Character passport: take your character to a friend's world and back** (built by rambodolphin13 for DividedRealms,
`da78651` and `da92848`, cherry-picked with his authorship; thank you, rambodolphin13). In the launcher, **CHARACTER
PASSPORT…** takes one of your characters to a friend's hosted world with its gear, quests and the coin you choose, and
**COME HOME** brings it back with everything it earned. While it is away it is locked at home: it can't enter the world,
be deleted or be moved. See [docs/CHARACTER-PASSPORT.md](docs/CHARACTER-PASSPORT.md).
- A character is playable in one world at a time. A travel file works once (its trip id): home takes back only the trip
  it is away on, and the host takes an outbound passport only once. Coin moves as a purse and is never copied.
- Every step backs the world up first (`runtime\passport-backups`, the newest 6) and is all or nothing: a step that
  fails leaves the world exactly as it was.
- Living Server's own changes: every saved-progress table has a passport decision, and a test fails when a new one has
  none; Living Server's social tables (gamebot memories, deeds, lore memory, companions) stay in their world; the passport
  carries no account name or password; a name taken in the friend's world is refused and you pick a visiting name; a
  Sluaghbinder visits only a world with the class on; the host's desk never creates accounts; and the host can turn new
  visitors off (`passport` / `passport_visitors`) while visitors already there can still go home. Recall brings a
  character back as it left if the friend's world is gone.
- Automated tests only so far; nobody has made the trip between two real PCs yet.

**Play with friends (experimental).** About one to four friends can play in your bot world: HOST FOR FRIENDS and JOIN
FRIEND in the launcher, an invite code, and a password. Home network and Tailscale by default; internet hosting is a box
you tick. See [docs/PLAY-WITH-FRIENDS.md](docs/PLAY-WITH-FRIENDS.md). Hand port from rambodolphin13's DividedRealms
(3c6cc9d, 6f22a7a, ec0e456, af1fcf8, b0744a2; Join Friend first came from stefanrows/OfflineDAoC), GPL-3.0. Thank you,
rambodolphin13. The Character passport (da78651f) is not part of it. It passes automated tests; nobody has played it
between two real PCs yet.
- **The host password is the gate** (owner's decision, 2026-11-08). While you host, a new account from another PC needs
  your password, and without one set no other PC can make an account. Your own PC never needs it, so single-player
  automatic account creation is unchanged. Five wrong host passwords from one address lock it out for 10 minutes. No
  friend list.
- **Every friend has their own password** (owner, 2026-11-08). Join Friend asks for the host password (first time only)
  and your own password. The account keeps only your own password, so one friend can't log in as another, and later
  logins never need the host password. The host password itself is never sent: the first login sends a short join token
  made from it (see the developer notes in the guide).
- **One region address per client.** Each client is told the address it connected on (this PC, the home network or
  Tailscale), or the router's internet address for a friend from the internet. Before, a friend on Tailscale or on a
  172.16 network was told an address it could not use.
- **Firewall rules for this game only:** `CoreServer.exe`, TCP 10300 and UDP 10400, from the home network and Tailscale
  (any address only with internet hosting). Made with Windows' permission. STOP HOSTING removes exactly those rules, by
  name, and says so in red if it can't.
- **Internet hosting (opt-in):** the router opens the two ports over UPnP while the server runs and closes them when it
  stops. The router mapping now points at this PC's address on the router's network, each port is asked for on its own,
  and the router's internet address is kept as `PublicIP`.
- **Friends on the home network or Tailscale get this PC's idle time-out exemptions** (ping, position, idle kick), so
  they aren't dropped while standing in `/grind`. A public address keeps the normal time-outs.
- **Two friends behind one router** can each make an account at once: the 15-minute same-address rule is skipped for
  this PC, the home network, Tailscale and the host password. The total per address still applies.
- **Join Friend refuses `offline`** (the host's own account) and suggests a name.
- **Friends never get GM.** Make Me GM changes only your own account; a test with two friends' accounts checks it.
- **Release builds** check that no host password or `PublicIP` ships, and leave out Host for Friends' own files. The
  network defaults (this PC only, UPnP off, mappings removed at stop) came in 1.1.2 and are unchanged.
- Not yet: the new (Godot) client can't use Join Friend, and a friend from a public address in `/grind` can still be
  dropped by the ping time-out.

**Play with friends: bots, pulls, credit and caps with more than one player** (owner's call, 2026-11-08; the co-op
audit of 2026-11-03). With one player online nothing changes. With friends online:
- **A companion follows its owner, always.** Another player's `/pull`, attack, `/bot pilot`, invite or `/makeleader` no
  longer takes your `/spawn` or `/raid` companions, and when you leave a group a friend stays in, your companions leave
  with you.
- **Invited gamebots follow the group's leader.** `/makeleader` hands them to the new leader, and when the leader leaves
  they follow the new leader instead of whichever player came first in the group.
- **`/pull` orders only your bots:** your companions, plus the gamebots when you lead. The leader's tank-gated pull holds
  only the leader's bots, and a friend's attack sends the friend's companions in without breaking it.
- **`/travel` and `/bot pilot` are host only.** They press keys in, and read files from, the PC that runs the server; a
  friend on another PC is told so and nothing starts.
- **A server-wide companion budget:** while more than one player is online, new companions need room in the server's
  total (`companion_budget_total`, 160) and the player's own share (`companion_budget_per_player`, 79 = one full
  `/raid 80`). 0 turns either off.
- **Bots answer each of you.** The reply cooldowns in `/faction` and `/say` are per player, so one player's line no longer
  silences the replies to another.
- **Quest credit for everyone:** a gamebot's kill credits every player of the group in range once; when the leader is
  dead or away, another player of the group in range is credited instead of nobody.
- **One invite at a time:** a gamebot leader whose invite box waits for one player's answer invites nobody else meanwhile.

Covered by automated two-player tests (`UT_PlayWithFriends`); nobody has played it with two people yet. Joining a
friend's server (networking, accounts, the launcher) is a separate change. Details: [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).

**Tales: the realm's storyteller, and stories by the fire** (the owner's idea, 2026-11-10: "a bot in faction asks another
famous bot to tell the story about X..."; how it works: [docs/TALES.md](docs/TALES.md)). Built and covered by automated
tests; nobody has heard one in game yet.
- **A tale in /faction.** Now and then a bot asks the realm's storyteller, by name, for the story of a dragon or a boss.
  Others hush the channel and beg, the storyteller tells it in about a dozen lines with a question from a listener leading
  it along, the channel goes quiet for half a minute, and a rumour about the same boss follows.
- **Sometimes the rumour is true.** About half the time, when the realm's raid on that boss could form anyway, the rumour
  is that one is forming, and six to twelve minutes later the realm's own automatic raid on it opens.
- **Storytellers are people.** Up to two per realm, kept across restarts; other bots name them and only they tell.
- **You can ask** ("tell the one about legion"), and a question asked during a tale is answered by the storyteller.
- **It can go wrong.** A storyteller pulled into a fight for more than three minutes never finishes, and a listener says
  so; the tale is told another night.
- **Fireside tales.** Sit down out of combat in a boss's place with a gamebot in your group and a groupmate tells the short
  version in /group, once a day per tale.
- Fifteen tales in 1.2: three bosses per realm, and Legion and Govannon as each realm tells them, each kept to its lore
  file. A realm starts one on its own at most every three hours or so. Settings `bot_living_tales` and
  `bot_living_tale_raids` (both on); GM checks `/lstest tale [boss]` and `/lstest fireside`.

**A bot-led group keeps you through a zone change** (owner's PC test, 2026-11-07). A player who joined a gamebot's
group was let go ("left") as soon as they were out of the world for a moment: a region change, a zone change across
regions or a GM /jump takes the player out of the world while the client loads. The group now waits for you while you
are still in it, for up to two minutes out of the world (linkdead keeps its own five minutes).

**Melee fight Grand Summoner Govannon's demon from behind** (owner, 2026-11-07: "teach positioning"). In his demon form
Govannon casts Shroud of Agony, about 1,440 damage to everyone within 300 of his target, every 20-35 seconds; every bot in
melee stood inside it, and in the play test 45 raiders died in the first 90 seconds of the demon. Now his target keeps
150 from him and the other melee fight him from about 190 behind him on the line from his target, clear of the Shroud,
re-placing themselves when he changes target or his target moves. His health, damage and spells are unchanged. The melee position order is
general (EncounterMeleeStations) but switched on for Govannon only.

**The Darkness Falls raid walks to Princess Nahemah from Ba'alorien's room** (owner's 1.1.2 test, 2026-11-07). After
Prince Ba'alorien died, every Hibernian raider's path to Nahemah's muster failed: Detour cuts a 20,000-unit corridor at
its 256-polygon limit, and Darkness Falls accepts only complete paths, so each raider got "no path", 37 gave up the goal
and the raid's own event camp was benched for two hours. A Darkness Falls bot now walks a cut corridor's own end when
that walk is itself a complete, checked path (both halves of this route are), then goes on from there. The navmesh is
unchanged. A realm raid's own event camp is never benched by the generic camp-route rule.

**The raid kills Spindler Broodmother's newborns before they kill its healers** (play test, Galladoria). Her newborn
spindlers (one or two every half minute, 8,000 health each, going for bards, druids, mentalists and wardens) piled up to
11 alive while the raid's casters and archers were split one party per newborn, and they killed 62 of the 120 raiders who
died in the lost attempt. Now all the casters and archers take one newborn at a time (the one with the least health
left, so they finish it together), and a party's melee peel a newborn off their own party's casters and healers. Her health, damage, tether and spawn rate are unchanged.

**The raid gets out of Xanxicar's stomp** (owner's PC test, 2026-11-07; also in 1.1.3). At the 80% stomp 110 of 185
Albion raiders died in one second: the order to get clear came only 3% above the threshold (the raid took him from 83% to
80% in 15 s while a bot needs about 12 s to walk out of 2,500), it reached only the raiders whose target was Xanxicar
himself (not those on his elites and crystals, nor healers), and nothing kept a bot from being walked back in. Now the
raid withdraws ten seconds of its own damage ahead of each threshold (at least 6%), every raider near him goes,
whatever it is fighting, to the nearest open spot on the cave floor clear of the stomp (which is centred on his
target), and it waits out there until the stomp is over. The party he fights still holds him. His
stomp is unchanged.

**/faction that holds together** (owner 2026-11-08, watching /faction: "faction feels disconnected... The chats MUST be
top notch"). The same fix as Beta 1.1.3 (see its entry: conversations said in order with each speaker in its part,
answers only from bots they are true of, follow-ups that fit, reactions in proportion, one short reaction in ten
minutes, and every `CHAT_LINE` naming its source, exchange, line and the speaker's class and level), on the 1.2 line with
its reply budget: a thread or a burst is still allowed or held once, as a whole, and its lines count as replies. Also on
1.2:
- **A story's teller doesn't repeat itself either.** "dead. again" opened 45 death stories in the overnight soak; a
  teller's short line the realm heard in the last ten minutes is now said another way, and a story whose first line
  can't be said isn't told.
- **A frontier death isn't told as a monster.** The same fix as Beta 1.1.3 (after its RC3): most frontier death stories
  read "siuses in murdaigean hate me" or "train of sneakywarriors, im dead", an enemy player taken for a kind of monster
  and made plural (named creatures too: "summoner roesias"). Only a kind of monster is talked about as one now, and a
  frontier death is told as one ("got ganked lol"). Tests only.
- **Exchange adverts in the period's words.** When no written trade line fits a listing, the seller says "anyone want
  ... ? make me an offer" or "pc ...?" (was "Does anybody want to make me an offer on ...?"), and the answers are the
  written trade answers, not "A merchant price can provide a reasonable minimum value." followed by "Fair enough. Safe
  travels." That formal closing now only comes with bot voices off.
- **One ceiling for every /faction line.** As in 1.1.3: every bot line in /faction is said only if it fits under the
  realm's ceiling, but a tagged realm event (`src=event`: a keep or relic taken, a raid's end, Darkness Falls, a server
  first, Leeroy; its burst capped at three within 40 s, and not held by the reply budget either) or a line to or about the
  real player (`src=player`). Lines that start more start nothing when held; the lore briefings and after-raid lines go
  through it too. A running debate, storyline or dunce scene owns /faction (other lines yield to it), waits for room beat
  by beat, and a dunce cut short after it spoke still rage quits. Every /faction line carries `cap=`, and
  `chat_audit.py` ends with `VERDICT PASS` or `FAIL`.
- **The fixes after 1.1.3 RC1, as in 1.1.3** (see its entry): a scene's part keeps the bot that started it (a busy or dead
  one is waited for and then speaks; one that left the game ends the scene; never recast, unlike the RC1 dunce that went
  from Iagoys to Yno); a gold spammer, rumour or roleplayer line held by /faction keeps its turn (tried every few seconds,
  given up and logged after three minutes outside a scene, `LIVING_FEATURE_DUE`/`DROPPED`), `GOLD_SPAMMER_START` comes with
  the first ad said, and `/lstest rumour` tells its reasons apart; each reply to the player has its own exchange id; and
  `chat_audit.py` fails a log with a feature that was due and never spoke. On 1.2 the spammer's ads keep their scene-sized
  count and its reactions stay within the reply budget. Also from the RC1 check: a gamebot's way back after a release is
  told to its own group (only with a player in it), not in /say (a wall of "released, running back" at Camelot's bind);
  a bot greets a player once per meeting (ten minutes); a deed names a place it could happen (`/lstest deed` never names
  a capital, and a streak or gank only a frontier zone); /say lines carry `cap=` and `chat_audit.py` checks them. Tests only. After RC2 the checker reads an armour slot in a
  trade line ("wtb leather arms 4 26 inf") as the item, not an armsman claim.
- **Checked:** automated and simulated only, not played. `source/tools/chat_audit.py` on the owner's 1.1.3 evening: one each
  of an answer after the asker closed, a level question after the invite, a follow-up before any answer, a thanks with no
  answer, a trade closing that didn't follow and a join answer from a class that can't fill the role; 6 pile-ons (+13
  reactions), 3 late reactions, 2 repeated reactions. On the overnight 1.2-line soak (4.6 h): 207 repeated reactions
  within ten minutes, 9 pile-ons, 5 thanks with no answer. The simulated evening (the same as 1.1.3's; it doesn't model
  1.2's separate reply budget): out of order 30 before, 0 after; pile-ons 23 before, 2 after (keep news at its cap of
  three); late reactions 28 before, 0 after; repeated reactions 72 before, 0 after; 5.72 lines a minute before, 5.06
  after; busiest two minutes 9.5 before, 8.5 after against a ceiling of 7.1.

**Two Darkness Falls exits work** (found and fixed by rambodolphin13, DividedRealms 428ccd7d). The exit by Legion's hall
and the one near the Midgard boss did nothing when clicked: the game client has them, the released world had no
destination for them. Each now takes you to your realm's usual Darkness Falls exit spot, like every other exit. Nothing
is written to your world: the server supplies the two missing destinations itself. Thank you, rambodolphin13.

**Dungeons and bosses have their lore.** Living Server 1.2 gives every dungeon, epic dungeon, dragon and named
boss its own lore (written in `data/lore`, one file per place and per boss; see [docs/LORE.md](docs/LORE.md)), and this is
the part that shows it to you. All the stories are now in: 107 places and 578 bosses (with 4 realm histories), written for
Albion, Midgard, Hibernia and the neutral places, from the dragons and epic dungeons down to the named spawns of every zone.
Where Mythic told a story it is kept (in our own words, with its source); where it did not, the Living Server wrote one. The
lore's own characters no longer share a name with a trainer, merchant or banker of their realm. The lore is checked by
automated tests; nobody has read it in game yet.

- **Atmosphere lines.** Short lines in the middle of the screen, like the ones for a keep taken or a relic:
  - the first time each day you walk into a dungeon or epic zone ("Old words are carved here. The ogres cannot read them.");
  - stepping into a boss's room or lair (once a session);
  - a boss changing phase (at most two a fight: Scurceol Hyrde's last light going out);
  - a dragon taking to the sky, in gold for everyone in its zone and smaller for its realm elsewhere (at most once an hour,
    beside the dragon's own bellow in chat);
  - a realm's first-ever kill of a boss, to the whole world, with a line in your chat naming the realm. It is remembered
    across restarts, once per realm per boss;
  - a raid forming, a line about the place for that realm only;
  - Darkness Falls changing hands is now a gold line in the middle of the screen as well (at most once every 15 minutes;
    it follows the RvR announcements switch).

  At most one atmosphere line every 30 seconds, never more than four lines on screen at once, and realm news (keeps, relics,
  raid results, sieges) always comes first: an atmosphere line waits 10 seconds after it. Only you see them; bots never get
  them. A new switch in the launcher's Living Server tab, **Atmosphere lines** (on by default), turns them off.
- **Bosses speak their lore.** A boss with a lore file yells its own lines when the fight starts, at its phases, when it
  kills someone and when it dies. A boss whose script already speaks at that moment keeps its own words, and nothing is said
  twice.
- **Bosses speak at their big moments too.** When a boss throws or ports someone, stares, breathes, goes berserk or heals
  itself, its lore lines for that moment are said now: Krackenschtein, Cunovinda, Ydenia, the Glacier Giant and the other
  throwers yell as they throw, Gjalpinulva and Cuuldurach as they stare and breathe, Suttung as he goes berserk, and a short
  line shows in the middle of the screen for the players in the zone. It is the same for a throw that hits you or a bot. One
  yell a minute for each moment, one screen line a fight for each, two at most, and the 30-second gap above still holds.
- **Bots know the stories.** As a raid leaves its muster, its leader tells the realm in /faction what the boss does
  ("storm keeper next. four lights in the corners, kill the lights first"). A minute or two after a raid, a veteran tells
  how it went and then something about the place. Ask a bot "whats in krondon?" or "who is the storm keeper?" and it answers
  from the lore. The lore and rumour lines join the bots' lore talk, checked like every other bot line.
- **Fixes.** Gjalpinulva's lair is in Malmohus, not Myrkwood (the encounter catalog said Myrkwood). Bots no longer say Aidon
  the Archwizard waits in Summoner's Hall: he is in the Hall of the Corrupt.
- GMs: `/lstest lore` says which lore place you are in and lets its lines show again.

**The lore reaches you beyond bot chat** (Living Server 1.2: every dungeon, dragon and boss gets a story). Built and
covered by automated tests; none of it has been seen in game yet.
- **A loremaster in each capital:** Loremaster Aldwin in Camelot's library, Loremaster Sigrun at Jordheim's temple of the
  gods and Loremaster Eithne by the bards of Tir na Nog. Ask them of the [places] of your realm and the places no realm
  holds. Every place and every boss has its own words, and the links lead from one to the next.
- **Lore books:** one book per place, which you read with "use", a page at a time. The loremasters copy them out for a
  silver, and now and then a book turns up on a monster near the place it tells of. It's rare, never one you already
  carry, and they never crowd out real loot. The delve says what each book is about. A long book (more than 16 pages)
  comes in volumes: "..., Volume II", a silver each, sold together; the last page of a volume names the next. Nothing
  is left out.
- **Long tales in parts:** the loremaster tells a long story's summary first, then offers its chapters as [names] to
  ask for one at a time. Each answer comes in short pieces that never break a word and always fit the game's message
  limit (a message over 2,048 bytes would be lost whole). Bots keep to their own short lines and never recite chapters.
- **The Herald tells the tale:** each raid in the launcher's Realm Records, and in the Camelot Herald, ends with a sentence
  from its boss's story: who led it, how long it took, won or lost.
- **The Bestiary:** a new page under Realm Records lists every boss of your realm's places and the neutral ones, with
  what the raid leaders say. A boss's full story, your realm's first kill and its raid record open once your realm has
  brought it down. Raids won before 1.2 count. The Camelot Herald gains a "Bestiary of the Realms".
- **/lore in either client** shows your realm's places and bosses and what your realm has slain. In the new client the
  journal's LORE button opens it in its own window, where every name is a link.

**More of the epic fights play in full, and the bots play them** (the epic encounters, wave 3). Tested by automated
tests only; not played in game yet. Some of these fights get harder: try them first.
- **Funus: the archers join in.** His weapon puzzle is off by default; do not turn it on yet, the tactic is not
  finished (in a test world it was beaten once, only with fixes not yet merged). With his weapon puzzle on, mercenaries, armsmen and infiltrators who own their short
  bow or crossbow now draw it and fight him with it, like the scouts with their longbows; none of them ever swings a melee
  weapon at him, not even when he is on top of them. Bots of those classes start with a plain training bow or crossbow of
  their own kind when they learn it, and earn better ones as before. No bot's pet attacks him or defends its owner
  against him, and the sting of a damage shield on a bot no longer heals him to full (`epic_funus_bot_shield_guard`).
- **Caer Sidi's final room bars its door.** While a horseman or Apocalypse fights, anyone at the door is moved back to the
  middle of the room, bots too. The raid gathers and waits inside the room and doesn't leave it mid-fight.
- **Easmarach retreats to the waterfall and you can finish him there.** Below 30% he walks up to the top of the falls and
  heals; the raid's casters, archers and healers shoot him from the floor below and the melee wait for him at the bottom.
  His current behind the falls is back: while he lives, anyone walking into the falls is pushed back, so the raid kills
  him before it goes on to the last room.
- **Legion:** his throw to the platform takes bots too (they come back down after a few seconds), wounded raiders step
  out of his reach so their deaths don't heal him, and no bot puts a bladeturn up near him (he eats it, and five of them
  set off his soul blast). This is not a Darkness Falls raid AI: the platform, the way down and his add waves are not
  played by the bots.
- **Legendary Afanc** throws bots to the lake shore too, and it only throws raiders who are still by him.
- **The Green Knight** wakes when a bot raid has gathered by him, as well as when you whisper "defend it".
- **Qan'ris Duros and Durval** (Shrouded Isles) bring their guards into the fight as they were meant to.
- **Torst and Hurika's flying patrol** stays off: the raid now knows how to pull a flying griffon down, but it hasn't been
  played. Turn it on with `epic_encounter_switches` `AllowGlacierGriffonPatrol@Torst=True; AllowGlacierGriffonPatrol@Hurika=True`.
- Each of these can be turned off alone with `epic_encounter_switches`, for example `AllowDisplacement@Easmarach=False`,
  `AllowDisplacement@ApocInitializator=False` or `AllowBotDisplacement@Legion=False`.

**Every restored boss mechanic is a setting you can change** (the epic encounters, wave 2). Each one is its own server
property (`/serverproperty`, section `npc`), and the game reads it as it plays, so no rebuild is needed:
`epic_horsemen_tether_heal`, `epic_funus_weapon_puzzle`, `epic_sacristan_patrol`, `epic_soul_reckoner_full_shield`,
`epic_soul_reckoner_small_room`, `epic_aroon_damage_order`, `epic_galladoria_bridge_spot`, `epic_full_fire_patches`,
`epic_earthmender_class_puzzle`, `epic_earthmenders_passive`, `epic_earthmender_quick_reset`,
`epic_earthmender_mutual_heal`, `epic_beatha_heals_xaga`, `epic_fairy_flight`, `epic_govannon_full_health`,
`epic_kvasir_cold_heal`, `epic_tuscar_twin_heal`, `epic_dragon_messengers`, `epic_govannon_full_heal` and
`epic_easmarach_endless_falls_heal`. All are on except Funus's weapon puzzle, the full fire patches, Govannon's full
health, Govannon's full heal at his change and Easmarach's endless heal on the falls, which stay off (15 of the 20 are on). `epic_encounter_switches` changes any one boss's
switch, for example `AllowBotDisplacement@Orylle=False; AllowPositionPuzzle@Olcasgean=False`.

**The dragons, Xanxicar and Galladoria play their full mechanics, and the bots play them back** (the epic encounters,
wave 2). Tested by automated tests only; not played in game yet. These fights are harder than before: try them first.
- **The dragons send their messengers again.** From half health, every 80-90 seconds, four to six messengers run out of
  the lair; each one that gets away calls four to six adds. The raid's casters and archers now run them down while the
  melee stay on the dragon, and a bot in your party shouts "runners!".
- **The raid reacts to the big hits.** When a dragon stares at someone, everyone near that raider (except the tank)
  steps away from them and the healers heal them first. When a dragon looks around (its long stun) or breathes, the
  casters, archers and healers step out of reach until it lands, so someone is left standing to heal the melee.
  Xanxicar's glare works the same way, and when he bellows his party's healers top it up. Your party calls each one.
- **Olcasgean's two safe spots are back.** Two parties hold the two spots the whole fight, or he casts his Root without
  pause; below half health he throws raiders again (the bots walk back). His throw had a bug that kept it from ever
  starting; it now works as in the original. The current at the waterfall keeps the raid in his room until he dies.
- **Galladoria's other mechanics are back:** the Water primal's and Spindler Broodmother's teleports (bots too), the
  Fire primal's trail of fire (melee fight the other primals while casters shoot it; the patches stay at the 2026-10-19
  strength by default, and `epic_full_fire_patches` brings back the original's), Aroon's Scaiths that must die in order (the raid kills them one by one), the original
  Earthmenders (they never attack, heal each other and reset when left alone), Beatha healing Xaga (the raid kills
  Beatha, then Tine, then Xaga) and the fairies circling the room. Conservator and Easmarach take damage only from up
  close again; the bots step in. The primals come when a raider stands on the bridge spot again (a bot raid that has
  waited on the bridge for three minutes starts it anyway).
- **Still off:** Easmarach pushing raiders back behind the falls (it would block the way to the last room, and the bots
  can't finish him on the waterfall yet), and Legion throwing bots (his platform has no way down). Legion's adds, his
  range and his heal on a kill have raid rules now, but the Darkness Falls raid is still not a full raid AI.

**Tuscaran Glacier, Summoner's Hall, Iarnvidiur's Lair and the Fomor and Marfach bosses play their full mechanics, and
the raid plays them** (the epic encounters, wave 2, Midgard and Hibernia). Tested by automated tests only; not played in
game yet.
- **King Tuscar and Queen Kula heal each other again:** damage to one heals the other while it is hurt. The raid kills
  Kula first, only the King's own tank keeps him busy, and the casters on Kula stay out of his hammer blows. Kula drags
  raiders (bots too) to her throne, where her frost circle freezes them: a bot waits it out like you would and walks back,
  and nobody fights inside the circle.
- **Icelord Kvasir drinks cold again:** cold heals him a fifth of his health. Bots never cast cold at him (your companions
  neither). When his primal scream is due, everyone but his tank's group steps away from his tank before the mass
  mesmerize lands, then goes back in.
- **Jailer Vifil's jail is back, for bots too.** Once the fight starts, one or two groups of bots run down the tunnel,
  kill the two guards at the jail and come back, so whoever he jails after that walks straight out. Your own group is
  never sent; you hear when a team goes.
- **Fornfrusenen:** the raid kills his shards first (each one takes a quarter of his health while he is above 35%), and
  everyone, casters too, fights him from close by, since he only takes damage from within 200.
- **Council Vagn and Nokkvi:** casters fight Nokkvi, everyone else Vagn (each shrugs off 90% of the other kind).
- **Icelord Hakr's toss and Hurika's gust** now hit bots too; they fall, take the fall damage and run back, and the
  healers see to them first. Torst and Hurika still sit on their perches (their flying patrol stays off: the raid can't
  pull a flying patrol yet).
- **Grand Summoner Govannon:** the raid kills his sacrifice and demon at range and spreads in his demon form. He keeps
  200,000 health by default; `epic_govannon_full_health` gives him the original 300,000. If both his walkers reach the
  altar, the raid kills the Shade of Aelfgar first; the Shade sends raiders (bots too) to the gallery. Summoner Cunovinda's ledge throw hits bots
  too. At Summoner Lossren, the raider the infected ghoul crawls toward walks away from it while the casters kill it.
- **Iarnvidiur, Boligar, Mortufoghus, Ydenia and the Glacier Giant** throw and teleport bots too; the bots run back.
- **Fomor:** Krackenschtein is fought from close by (he only takes damage within 300) and his throw hits bots too; his
  throw no longer breaks when nobody is near him. Myrddraxis's raid steps back before his stunning breath and his breath
  of annihilation. Anurigunda pushes bots back from her doorway too.
- **Marfach Caverns:** casters and healers stay clear of the Black Lady's Void Strike around her tank, and whoever
  Avartack fights turns to face him (his back strike hurts).
- **Your group calls it:** a bot in your group calls each mechanic in /group as the raid plays it ("dont touch the king,
  all on kula", "no cold on kvasir, it heals him", "shards first").
- **Test first:** Kula and Tuscar with the twin heal (and Govannon, if you give him his full health) may be much harder
  than before for a raid of bots.

**Caer Sidi, Krondon, Avalon City and the Darkness Falls princes play their original mechanics, and bots play them
too** (the epic encounters, wave 2, Albion). Tested by automated tests only; not played in game yet. These fights are
harder than before; tell us where a raid can no longer win.
- **Funus heals from the wrong hits again** (off by default since; do not turn it on yet). Only clerics and scouts (with their longbow) hurt him; a hit from anyone
  else, or a pet, heals him to full, as in the original. The raid's clerics and scouts fight him, everyone else stands
  well back, calls their pets off and keeps area spells off him. If you play another class, stay off him too.
- **The horsemen heal when dragged too far again.** The raid keeps each horseman near his room and leads him back
  before he gets there.
- **Morbus hides behind his swarm.** While a swarm of his lives he takes no damage; the raid kills the swarm first.
- **The Skeletal Sacristan walks his round at full speed and never stops.** The raid splits into parties waiting along
  his loop and hits him as he walks past, instead of chasing him.
- **Soul Reckoner takes no damage while a soul lives, and only in his small room.** The raid kills his souls first and
  leads him back into the room when he wanders out.
- **Bane of Hope ports you (and bots) away again** after his poison; they walk back.
- **Apocalypse's harbingers come when they should.** The first pair now always comes after the raid's fourth death (it
  could be skipped when two died at once), and the second pair at half health once the first pair is dead.
- **The Crypt Lord's court comes as one pull.** The raid no longer pulls his court a few at a time; it gathers and
  fights him and his whole court together.
- **Krondon:** Scurceol Hyrde's orbs, Krevo Ricik's toss and Orylle's throw into Orshom's Fire now pick bots as well as
  you. A bot thrown into the fire walks out of it first. Raiders with cold spells put the fire out (only cold hurts it);
  with the fire out, Orylle has nothing to throw anyone into. Casters and archers kill Krevo's ghost before it explodes.
- **Avalon City:** Sys'sro throws bots into his pit too; they walk out, ignoring the pit monsters (which can't be hurt).
  Dra'argus: casters and archers kill the Drau'gyn Sphere from range while melee wait out of its reach, then everyone
  fights him. The sphere's pull takes bots too.
- **Darkness Falls:** raiders with pets keep them away from Princes Asmoien and Ba'alorien, who eat pets to heal.
  Once the princes and Princess Nahemah are dead and Beliathan wakes, the raid goes for him before Legion.
- **Moran the Mighty's gust throws one raider into a nearby camp**, as it was meant to. It used to throw everyone near
  him at once.
- **Your party calls the mechanics** in /group: "kill the adds first", "out of the fire", "only clerics and scouts on
  Funus", "casters on the sphere", and so on.

**Boss throws, teleports and jails now hit bots too, and bots get back** (the epic encounters, wave 1). Tested by
automated tests only; not played in game yet.
- **Before:** a boss's throw or teleport only ever picked you. Bots were never thrown, so the mechanic either landed on
  you alone or was switched off. When a boss did throw you, your companions were dragged along with you.
- **The dragons throw again.** Golestandt, Gjalpinulva and Cuuldurach have their wing gust back, as in the original
  scripts. Every 60-80 seconds of a fight on the ground the dragon picks two to five raiders within reach (never the one
  it is fighting) and hurls them high over its lair. You and the bots are picked alike, so with a raid of bots you are
  thrown less often than before, never more.
- **Xanxicar's throw hits bots too.** It already threw you; now it picks from you and the bots alike, and the bots it
  throws come back the same way.
- **One switch per boss.** A boss that may throw bots picks them for its throw, and one that may not (every other boss
  for now, Legion included) picks only real players for it, as before. The boss's other targets (glares, debuffs, adds)
  pick the whole raid everywhere.
- **A thrown bot comes back.** It falls and takes the same fall damage you would. Then it walks back to its party when
  it can: your companions to you, a raid bot to its party leader. If something attacks it on the way, it fights back
  first. Most thrown bots are recalled instead: in a long run on a real install, 4 in 5 were.
  - **Never stuck:** if there is no way back on foot, it hasn't got closer in 10 seconds, or the walk takes too long, it
    says so and is back with its raid after a 5-second recall.
  - **Jails:** a jailed bot waits and fights whatever attacks it until the jail ends. Jailer Vifil's jail ends when his
    two tunnel guardians are dead (or the fight ends). Vifil's jail is still switched off.
  - **Healers** heal a raider who was just thrown, you included, before others, unless someone else is about to die.
  - **Your party says it:** "Mira got tossed, heals on Mira", "got tossed, omw back", "someone's in the jail".
- **Your companions stay at the fight when a boss throws you.** Before, they were teleported along with you. Your
  own throw is exactly as before. The server setting `epic_companions_stay_when_thrown` (on) turns this off; Legion's
  throw always leaves them down, since his platform has no way back for a bot.
- **The dragons throw as high as they did.** Up to about 1,400 units: the fall costs about 60% of your health without
  Safe Fall. Each dragon has a setting to throw lower: `epic_dragon_throw_height_golestandt`, `_gjalpinulva` and
  `_cuuldurach` (0, the original; 712 costs about 34%).
- **The dragons' messengers are back.** From half health they send runners out to fetch more monsters (on since the
  bots learned to run them down; see the dragons above).
- **Lich Lord Sanguis can be finished.** At 5% health he can't be targeted. Bots never fight what can't be targeted,
  so a raid could be stuck on his last 5% until he reset. Now spellcasting bots keep casting at him. Melee and archers
  still can't hit him, the same as for you.
- **Switches:** every throw, teleport and jail now goes through one place. Each mechanic can be turned on or off per
  boss. Off unless listed above, so everything else plays as before. The server log's `EPIC_DISPLACEMENT` lines show
  each throw. `EPIC_DISPLACEMENT_FALLBACK` and `EPIC_DISPLACEMENT_RECALLED` show a bot that had to recall.

**Boss mechanics pick bots too** (owner, 2026-11-06: "the throws and other unique triggers or scripts or anything
target bots too").
- **Before:** the epic and named bosses chose their special targets from real players only. Every glare, stare,
  debuff, "random target" spell, aggro wipe and add trigger went to you, and the bots beside you were never chosen.
- **Now:** they choose from the whole raid: you, your companions and the gamebots. With bots around you are one target
  among many, never chosen more often than before. Some of what changes:
  - the dragons' glare and roaming glare, Xanxicar's glare, Myrddraxis's flame breath and poison, Krackenschtein's
    bolt, Roesia's pain, Lossren's ghoul, the Mistress of Runes's nearsight, Aros's debuff, Birghir's frost, Guthlac's
    root and frozen bomb, Steinvor's pillar, Kula's Thor's Might, Balor's eye, Aroon's Scaiths and the Eyes Watching
    You in Galladoria;
  - triggers on a death or a hit: Nosdoden's "Spirit of ..." ghosts, Oona's undead, Beliathan's minions, High Lord
    Saeor's and Legion's heals, the Risnir Cruss apparition and the Spectral Provisioner;
  - crowd counts: Ozur grows stronger and Legion calls more adds with a big bot raid, as they did with a big raid of
    players.
  - Messages still go to you only; a bot gets the spell, not the text.
- **Throws, teleports and jails pick bots only where the boss may throw them** (the three dragons and Xanxicar, see
  above). Everywhere else they still picked real players only at this step. Later steps (above) let more bosses pick
  bots, Scurceol's orbs and Legion's throw among them.
- **No floods of adds.** With bots counted, a few bosses would make far more adds or heals than any 2003 raid saw: a
  bot raid can be twice its period size. They now count at most the period raid (docs/RAID-SIZES.md), so up to that
  size nothing changes, and a bigger bot raid faces what a full raid of players did:
  - Legion: adds for at most 64 raiders (16 a 5% step), and heals from at most 64 deaths in 5 minutes;
  - High Lord Saeor: heals from at most 64 deaths in 5 minutes; Beliathan: at most 64 minions up;
  - Nosdoden: at most 64 spirits of the fallen up; the Green Knight: at most 70 trees a wave; Oona: at most 70 undead
    up; Risnir Cruss: at most 40 apparitions up.
  - Each is a setting of its own (EncounterFloodGuard). Ozur, Aroon's Scaiths and Apocalypse's harbingers needed none.
- **The Darkness Falls princes no longer eat bots.** Asmoien and Ba'alorien eat the nearest pet every five seconds and
  heal by it. Their pet check also matched your companions and the gamebots, so in a bot raid they ate a bot every five
  seconds. Now they eat summoned pets only: yours and the bots' pets.
- **Raids are harder:** mechanics that used to skip the bots now hit them. Not yet played in game.

**Battleground rallies: realms go up to the keep lord together** (playtest 2026-11-04: in Thidranki 210 level-22 bots
fought for 45 minutes, the gates fell, and the Renegade Lord never dropped below 100%). **This did not work on a real
install:** in an overnight run (development build 76600af7), 168 of 169 storms failed, no keep changed hands in 8
hours, and the same bots died over and over.
- **Before:** once the gates were down, every gamebot walked up to the lord on its own, from wherever its last fight or
  its release had left it. They reached him one at a time and died to him one at a time.
- **Now:** each realm rallies just inside the broken gate, under the lord's room. Its gamebots fight whatever comes at
  them there, and go up to the lord together once there are enough of them.
  - **"Enough"** is worked out from the lord's real level and health, the guards standing with him, and the levels and
    health of the bots at the rally, with no extra safety margin: the realm tries often and accepts that some attempts
    fail. In Thidranki that is about 12 level-22s against the lord alone, and about 24 with three guards beside him. A
    lord already down to half health needs fewer.
  - **You count double.** A real player standing at the rally counts as two gamebots, so you and ten bots go up where
    twelve bots would. The server log's `BG_RALLY_STORM` line shows how many players went up (`players=`).
  - **No endless waiting:** after 3 minutes the rally goes up with half of what it needs. After 6 minutes it goes up
    with whoever is there, never fewer than 8.
  - **A failed attempt** ends like a raid attempt: half the stormers fell, the lord healed back to 95%, or he lost
    nothing for 3 minutes. The survivors then rally again.
  - **The dead rejoin the rally.** A bot that died comes back to the rally instead of running up to the lord alone.
    Bots that arrive while an attempt is running go up in groups of at least 4.
  - **Only the bots changed.** The lord, his guards, their respawns and the gates are exactly as the base game has them.
- **Smaller fixes:**
  - A gamebot that has left the lord (it died, or was chased out) comes back in through a gate again. Before, once it
    had passed a gate it walked straight at the lord for the rest of the tour.
  - Gamebots roaming the field around an enemy-held keep stay outside its walls. Before, they were sent to points in
    its courtyard and on its walls with the gates shut, and gave up their route ("route recovery" in the log).
  - A battleground fight is logged under the battleground goal. Before, the server log showed "Find a reachable
    level-appropriate XP camp" for almost every battleground death.
- **For testers:** the server log's `BG_KEEP_STATUS` line now shows each realm's rally (`rally="Albion:rallying 9/14"`),
  and new lines say when a rally goes up (`BG_RALLY_STORM`), when late arrivals join it (`BG_RALLY_REINFORCE`) and
  why an attempt ended (`BG_RALLY_REGROUP`).

**Who has a gamebot's time, in one place** (groundwork for gamebots that lead groups and invite you).
- Raids, sieges and keep defenses, battleground rallies, gamebot groups and your own group now each put a claim on a
  gamebot's time, with a priority. Your group is the strongest claim: no raid, siege, RvR march or rally ever takes a
  gamebot from your group, exactly as before. What gamebots do has not changed, with one exception: an RvR march and
  a battleground rally now rank the same, so neither pulls a gamebot away from the other (before, a march could take a
  gamebot out of its battleground rally).
- **`/lswhy duty`** on a targeted gamebot (or `/lswhy duty <name>`) says what has its time now ("realm raid
  (Gjalpinulva), since 4m", "in a player's group", "free") and its last claims: who asked for it, and whether it went
  or why not. Plain `/lswhy` on a gamebot shows the same under its chat decisions.
- **One fix:** a gamebot recruited for a raid that logged off before its party formed stayed reserved when it came
  back, and sat out every raid, siege party and gamebot group until that raid ended. It now comes back free.
- **For testers:** `DUTY_LEASE` lines in the server log record each claim granted, taken, refused (with its reason)
  and ended, once per change.

**Gamebots in RvR notice you like players, not radar** (a player's report on r/daoc; owner 2026-11-01; North Star
audit 2026-11-03).
- **Before:** in the frontier, the battlegrounds and Darkness Falls, every gamebot with line of sight saw you from 5,000
  units at once, and they all came together.
- **Now:** a gamebot notices you by chance. Within 1,000 units it always does; beyond 3,500 it never does on its own.
  A gamebot looking on its own takes about 1.5 s to notice you at 1,500 units, 6 s at 2,500 and 20 s at 3,400 (the
  median). Fighting, casting, being in a group of 3 or more or standing in front of it makes that quicker; standing
  still makes it slower. Stealth works as before: a stealthed player is never seen this way.
  - **Only a few look at once:** at most 3 gamebots of a realm look for you each second. The rest learn of you only
    when one of them calls it. In a crowd of 50 spread out in groups, about a third have engaged you after 5 seconds and
    the rest drip in, where before nine in ten came within 5 seconds.
  - **They react, then call:** a gamebot that notices you engages 0.5-2 seconds later and tells its group. Other groups
    marching to the same keep hear it half the time, 4 seconds later. With bot voices on and you in that group, it
    says "inc 3 north" in /group.
  - **Duty first:** a gamebot on a raid, in a battleground assault or in your group doesn't leave it to answer a call.
  - **Your bots are not affected:** your companions and the gamebots in your group follow and fight as before.
- **Fewer pet classes in one group:** when gamebots form an RvR group or a siege party, at most 2 pet classes
  (Cabalist, Theurgist, Necromancer, Spiritmaster, Bonedancer, Enchanter, Animist, Sluaghbinder) join a group of five
  or more, and 1 a smaller group. Groups you lead or join are never changed.
- **Settings:** `rvr_bot_perception` (on), `rvr_bot_awareness` (easy, normal or veteran; normal), `rvr_bot_spotters`
  (on) and `rvr_group_pet_cap` (2; 0 turns it off). See [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).
- **For testers:** `RVR_NOTICE` lines now show the chance and `time_since_first_los_ms`; `RVR_SPOT` lines show how many
  mates were told, relayed and held by their duty.
- **Checked:** automated tests only, including seeded simulations of the medians and of crowds. Not yet played.

**Gamebot leaders invite you, and you can just follow** (bot-led groups, first part; docs/BOT-LED-GROUPS.md).
- **The invite:** the leader of a gamebot party of your realm, within 3 levels of you on average, with room and
  hunting where you can walk or ride, may send you a tell ("lf 1 more for skeletons in Cursed Forest, want in?") and
  then the usual group invite box. Yes puts you in its group; the gamebot stays the leader.
  - **Never** while you fight, are dead, ride, are on a boat, have another yes/no box open, trade, are in a group, have
    `/spawn` companions out, are `/afk`, or are in a frontier, battleground or raid.
  - **How often:** unasked, at most once every 45 minutes and not in your first 10 minutes online. It counts as that
    period's bot tell, so a tell and an invite never stack.
  - **Asking:** `/lfg`, or a tell to a bot leader with room ("lfg?", "room for a cleric?"): when a party fits, its
    leader answers by tell and the invite box follows within 15 seconds (and then no duo offers come). An asked invite
    skips the 10-minute start, the 45-minute gap and the quiet after a no.
  - **Saying no:** No in the box, or "no thanks" to the tell. Then no unasked invite for 30 minutes, nothing from that
    leader for 6 hours, and no unasked invites for the rest of the session after three noes.
  - The server setting `bot_group_invite_player` (the launcher's Living Server tab, **Bot leaders invite you**) turns
    unasked invites off; `/lfg` still gets you one.
- **Following:** the leader runs the party as a gamebot party does: the camp, the pulls, the breaks.
  - The camp is for the group's level counting you, and never more than 6 levels above you.
  - The leader tells you where to meet and waits there up to 20 minutes (it says where it is at 5 and 10), then goes
    on and keeps your spot. You travel yourself: you are never teleported, summoned or given money. Gamebots beside
    you when you take a porter come with you, as invited gamebots do.
  - On the way the leader stops when you fall 1,500 units behind, asks "where u at?" after 2 minutes, and after 10
    minutes with you not getting closer goes on to the camp and says so in `/group`. Inside a dungeon entrance the
    party waits for you the same way.
  - The party pulls only when you are near, alive, not sitting and up on health and power like the rest.
  - **Away:** `/afk`, or 10 minutes without doing anything: the party pulls where it stands but goes nowhere new; at 15
    minutes it holds; at 30 it lets you go at the next break, with a tell. Linkdead for 5 minutes counts as leaving.
  - **Leaving:** `/disband` at any time. The party carries on without you.
  - A gamebot party hosting you counts your class: when you are its only healer, it still has a healer.
  - `/pull` in a party a gamebot leads says the gamebot is leading instead of taking over. Taking the lead
    (`/lead`) comes in the next part.
  - Quest kills: a kill by the party counts for your kill quests when you are alive and in range, as in a group you
    lead.
  - No raid, siege, RvR march, battleground rally or other gamebot group takes a gamebot from a party that hosts you
    (`/lswhy duty` on one says "leading a group with a player").
- **Loot changes for every group with you and gamebots, whoever leads** (owner's decision): you get the primary
  spoils, and the gamebots still gear up and fund their own improvements. Each item, in this order:
  - an item your class can use that beats what you wear goes to you;
  - otherwise, an item that is an upgrade for a gamebot nearby goes to the gamebot it improves most (gamebots start
    without armour and earn it, so they need drops);
  - everything else (most of the sell value) goes round the members in range with two turns for you: with seven
    gamebots nearby you get 2 of every 9 of these items.
  - Coin is split evenly among the members in range with you counted twice (2/9 with seven gamebots nearby).
  - Before, in a group you led you got everything, and in a gamebot's group you would have got nothing. Your share is
    the setting `bot_group_player_loot_weight` (1 to 8; the launcher's **Your loot share**). Groups of only gamebots,
    and your `/spawn` companions (who take no loot), are unchanged.
- **Gamebot chat:** the leader says what comes next in `/group`, short and lowercase ("moving to skeletons", "where u
  at?", "everyone med"). Lines like "you pull great" or "lead the way" are no longer said in a party a gamebot leads.
- **For testers:** server log lines `BOT_LED_INVITE` (sent, not sent, declined, accepted, with the reason),
  `BOT_LED_GROUP_HOSTING`, `BOT_LED_GROUP_GOING_ON`, `BOT_LED_PLAYER_LEFT` and `BOT_LED_LOOT` (who got each item and
  why: `upgrade-player`, `upgrade-bot` or `rotation`); the startup
  `FORK_BUILD` line shows both settings.
- **Not yet:** `/lead` and handing the lead back, the join setting (follow or lead) and raid leaders who invite you
  come in the next parts. Gamebots don't yet ride the same horse or boat as you: the leader takes its own and waits
  for you at the other end. Nothing here has been played in game yet.

**Take the lead, or hand it back** (bot-led groups, second part; docs/BOT-LED-GROUPS.md).
- **`/lead`** in a party a gamebot leads: the leader says "all yours", you become the group leader, and the gamebots
  follow you exactly as gamebots you `/invite` do (`/pull`, your camps, your pace). The group, its loot share and your
  quest credit carry on unchanged.
- **`/lead bot`** (or `/lead bot <name>`, or `/makeleader <gamebot>`, or `/makeleader` with a gamebot targeted) in a
  group you lead: a gamebot takes the lead (the one you name or target, else the highest level one), says it is looking
  for a camp, and picks one near where you stand; you follow as in a party that invited you. It needs at least two
  gamebots, a tank, a healer and a damage dealer (you count for your class), no `/spawn` companions and no fight going
  on, and only works where you can walk or ride (not in the frontier, a battleground or Darkness Falls). If something
  is missing you are told what.
- **`/pull`, `/invite` and `/makeleader`** in a party a gamebot leads now say "Brannoc is leading this group. /lead to
  take over." instead of "You are not the leader of your group".
- **When a party you follow is down to one gamebot**, you get a yes/no box: "Only Brannoc is left in your group. Do you
  want to lead the group?" Yes: you lead and Brannoc follows you. No, or no answer within a minute: Brannoc says
  goodbye and goes its own way. Before, the party just broke up. You never become the leader any other way unless you
  type `/lead`.
- **`/invites off`** stops unasked invites from gamebot leaders for this character (saved with it); `/invites on` turns
  them back on, `/invites` says which. `/lfg`, or a tell to a leader, still gets you one.
- **The setting `bot_led_group_join_mode`** (the launcher's Living Server tab, **When you join a bot's group**): with
  *follow* (the default) you follow the leader; with *lead*, saying yes makes you the leader at once.
- **A fix from B1:** a party following a bot leader whose only healer was you counted you for its roster but would not
  start a pull. It now counts you there too.
- **No gamebot is free for a moment during a handover:** it goes straight from your group to the bot-led group (or
  back), so no raid, siege or RvR march can take it in between. `/lswhy duty` on one shows "handed over".
- **For testers:** server log lines `BOT_LED_HANDOVER` (to_player, to_bot, offered, accepted, declined, refused, with the
  reason) and `AUTONOMOUS_GROUP_TASK_ENDED ... disbanded=false` for a party that was handed over; the startup
  `FORK_BUILD` line shows `bot_led_group_join_mode`.
- **Not yet:** the leader's "pulling", "last pull then town" and "lf 1 more" calls are written but not said yet. If you
  leave a group after `/lead`, its gamebots leave too (as with gamebots you invite) instead of carrying on as a party.
  Nothing here has been played in game yet.

**Raid leaders invite you** (bot-led groups, third part; docs/BOT-LED-GROUPS.md).
- When a dragon or epic dungeon raid forms in your realm and you are level 50, its **raid leader** may send you a tell
  ("forming for Gjalpinulva at Svasud Faste. want a spot?") and then a box: "Haldor invites you to join the Gjalpinulva
  raid. Do you wish to join?" The raid leader is a level-50 officer of a raiding guild in the raid (else the leader of
  its first party). It works the same in Albion, Midgard and Hibernia.
- **Once per raid.** A no is final for that raid. Never while you fight, are grouped (a gamebot party hosting you
  included), ride, trade, have `/spawn` companions out, are `/afk` or have another box open, never in your first 10
  minutes online, and never for a neutral raid (Darkness Falls and the like) or a raid already in its battle.
- **Yes** puts you in the raid party that needs your class most, one still at the muster town first. If that party is
  full, one gamebot of your role (never the party's or the raid's leader) steps out and goes back to its own play,
  where the raid's refills may take it again. You travel to the muster town yourself (never teleported); your party
  waits there until the raid leaves, and after that you catch up on your own. The party leader welcomes you in
  `/group`.
- **In the raid party** the raid runs the party: `/lead` is refused, its gamebots don't ride porters with you, and no
  bot-led party invites you. Your kill quests count the party's kills as in a party that hosts you; trash loot is
  shared as in every group with gamebots, and the raid's own rule for its big drops (a quarter to players) is
  unchanged. If you leave, the raid fills your seat with a gamebot again.
- **No gamebot is taken from your group for a raid** (as before): a gamebot in your group or in a party hosting you
  is never recruited. The gamebots of a raid party you sit in stay the raid's.
- **Settings:** `bot_raid_invite_player` (on; the launcher's **Raid leaders invite you**). `/invites off` stops these
  invites too.
- **For testers:** `RAID_PLAYER_INVITE` lines in the server log (sent, not sent, declined, accepted, seated, not joined,
  with the reason and who stepped out); the startup `FORK_BUILD` line shows the setting.
- Nothing here has been played in game yet.

**Bot-led parties look for a replacement, take their breaks, and call their pulls** (bot-led groups, fourth part).
- **"lf 1 more":** when a member leaves a gamebot party that hosts you, or you leave it, its leader asks in `/lfg` for
  the missing role ("lf 1 more, need a healer for skeletons") and takes a free gamebot of that role and a fitting level
  (within 5 levels, or both 50) that is waiting for a group and at most 8 minutes away. The recruit travels to the
  party by the usual roads and stablemasters; the leader says "got someone coming, hang tight". A gamebot that a raid,
  an RvR march, a battleground rally, another party or your own group holds is never taken. The ask is said once per
  search and only while the realm's `/lfg` is under its chat budget.
  - **Lost its healer, tank or damage dealer?** The party waits for the recruit instead of breaking up, and pulls
    nothing meanwhile. After 5 minutes without one it gives up: it carries on short-handed when it still has a tank, a
    healer and a damage dealer, and ends otherwise ("couldnt find anyone, gonna call it here").
  - **When you leave** a party you followed, it carries on and looks for someone of your class's role (it used to
    break up when you were its only healer). A group you lead is unchanged: gamebots you lead leave with you.
  - Gamebot-only parties keep their old rules (they carry on short or end); raid parties refill as before.
- **"last pull then town":** a party hosting you now takes the same natural break every gamebot party has: when half
  or more of its gamebots have nearly full bags, or its task has 5 minutes or less to run, the leader says "last pull
  then town", the party finishes that pull (the fight going on, or the next one; if no pull comes within 3 minutes it
  goes anyway), and then the party ends there, as at any task's end: each gamebot goes to sell, and the leader says
  "ok town to sell, good group all". Selling still happens only at that break, never in the middle of a camp.
- **"pulling":** in a party a gamebot leads, the leader calls its own party's pulls ("pulling", "inc, on me", "here it
  comes", and more), and that call is the pull's only line: the other gamebots' "inc" isn't added beside it. In a group
  you lead, pulls are called as before.
- **For testers:** `BOT_LED_RECRUIT` (searching, recruited, gave up) and `BOT_LED_BREAK` (called, ended) lines in the
  server log.
- **Not yet:** the party doesn't walk to town and back together with you: the break ends the party. Gamebots have no
  potions or wearing gear, so there are no breaks for those. Nothing here has been played in game yet.

**Replies that don't give bots away** (roadmap item 6; how it works: [docs/bot-voices/CHAT-BUDGETS.md](docs/bot-voices/CHAT-BUDGETS.md))
- **Bots answer you first.** A tell to a bot, and a line in /faction or /say that names a bot, are always answered, however
  busy the channel is. So is a /say to the bot right in front of you, and a "why?" or "where?" about a bot's own line.
- **Replies have a budget of their own.** In /faction and /say, bots' answers are now counted apart from the lines bots
  start on their own, with their own target and ceiling. When a channel is busy, bots stop answering each other first; the
  room that's left goes to answering you.
- **/faction on a small server keeps to its budget.** With 10 gamebots a realm, /faction ran at about four times the pace
  the server's own budget asked for (the automated playtester measured 1.2 to 1.4 lines a minute against 0.27 to 0.36).
  The server measured each channel over the last two minutes, which can't tell a slow channel's floor from silence, so
  every quiet two minutes a bot spoke at once, and the answers came on top. It now measures over a window long enough for
  the channel's pace (2 to 20 minutes). The automated re-run (the playtester's faction-rate scenario, 10 gamebots a
  realm, two half-hour listens each, in the development container): **before 1.20 and 1.33 lines a minute, after 0.87
  and 0.97**, against an opener target of 0.27 to 0.31 and a reply target of the same. Most of what is still over the
  budget is the gold spammer's waves (6 to 11 ads in half an hour) and the debate scenes: they come at the same pace on
  any server, however few bots it has. They now count against the openers' budget, so bots start fewer lines of their
  own while one runs.
- **Debates and gold spammers fit the crowd** (owner 2026-11-05). On a realm with fewer than 300 gamebots online, the
  /faction debates and the gold spammer's waves come less often, and a wave has fewer ads: with 10 bots a realm they come
  about a fifth as often and a wave is two ads. From 300 bots a realm up, nothing changes, so at 900 they come exactly
  as before. In the evening model at "How much they talk" 2, /faction with 10 bots a realm goes from 0.86 to 0.74 lines a
  minute, and with 300, 900 and 1,000 it stays at 3.96, 6.78 and 6.77.
  The playtester's faction-rate scenario with 10 gamebots a realm (two runs, in the development container): 0.62 and
  1.03 lines a minute against the 0.66 to 0.69 it now expects (the opener target plus the reply target), with no debate
  or spam wave in the listen, and neither run flagged. The playtester now listens until 10 lines are expected, so these
  listens were 14.5 minutes, shorter than the half hour of the earlier runs.
- **No quieter on a full server.** The world was re-balanced, not cut: /faction's openers keep 4 a minute at a full server
  and its answers come on top of them; /lfg and /trade aim at what the 1.1.2 chat model predicted (1.8 and 1.6 a minute,
  were 1.6 and 1.3; on a real install 1.1.3 delivered about 3.2 and 2.3); a guild's chat and /say near you have higher floors, so a few bots keep them going. In the evening model
  (`UT_ChatBudgetSimulation`) at the default "How much they talk" (2), all the channels you hear together go from 7.9 to
  8.4 lines a minute with 300 bots a realm, and from 16.4 to 18.0 with 1,000. A 10-bot realm is quieter than in 1.1.2,
  because it was running far over its budget.
- **Nothing untrue about themselves.** A reply that would say the bot is another class, role or level ("im a cleric" from
  a thane, "38 zerk here" from a 41) is dropped for something short that fits, or nothing. So is a group or trade ad said
  to a line about neither ("need a healer still?" to "gn all"). Bots also say where they are: "what zone r u in?", "are
  you in camelot hills?".
- **`/lswhy` shows a reason code** for every decision ("said [exempt-named]", "held [hold-reply-target]", "silent
  [below-minimum]"). New: `/lswhy me` shows why bots answered your own lines or didn't, and `/lswhy held` the replies the
  budget held back. `/lswhy budget` shows /faction's and /say's replies apart from the lines bots start.
- **Tuning:** `reply_target` and `reply_ceiling` per channel in `chat-budget.json`. A copy of the file from 1.1.2, without
  them, works as before.
- **Checked:** automated only. New tests (`UT_RepliesDontGiveBotsAway`) and the evening model compared against 1.1.1 and
  1.1.2; the full server suite passes. The playtester's faction-rate scenario, before and after, is above. Not played in
  game.

**Quests: the epic chains linked on every install, repeatable quests, deliveries, and a machine that plays them**
([docs/QUESTING.md](docs/QUESTING.md))
- **The epic chains are linked on a normal install.**
  - **Before:** Beta 1.1 put the Guild of Shadows chain (levels 7 to 48, the Camelot and Shrouded Isles routes) in order,
    with its period dialogue, and pinned every other guild's epic steps in order, but only if you ran
    `epic_chains.py --apply` by hand with Python, which the game doesn't ship. On a normal install `/epic` said "no epic
    chain linked by quest IDs", and a world it was applied to broke the released game's Guild of Shadows after a
    rollback.
  - **Now:** the server makes the same changes itself each time it reads the quests, in memory. Nothing is written to
    your world, so ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd have nothing to undo for them and the released game's quests
    are as they were. Quest rows you changed yourself are left alone, as the tool did. Setting `epic_chains_linked` (on)
    switches it off.
  - **A world you ran `epic_chains.py --apply` on** still holds the links in its rows. ROLLBACK.cmd and
    PREPARE-FOR-UPDATE.cmd now refuse on such a world until you put them back with
    `python tools\claude-version\epic_chains.py rollback --apply` (or run them with `-Force`), because the released
    server would never offer the Guild of Shadows. The server notices such a world when it starts and says so in its
    log and in `runtime\server\epic-chains-in-world.txt`.
- **Repeatable quests are offered again.** A quest written to be done several times (MaxCount 2 or more) was never
  offered after you finished it once. Quests with no limit (MaxCount 0) and one-time quests are still offered once, as
  before, until the world report shows how the classic quests use 0.
- **Deliveries in quests without a collect column can be handed in.** A quest whose row had no CollectItemTemplate at
  all ignored every item you gave, so its delivery steps could never be done.
- **Collection quests take their item whatever the case of its name.** Dera's Human Leg XP Item never took a human leg:
  the quest asks for `Human_Leg` and the item is `Human_leg`, and the turn-in compared the names letter case and all.
- **Classic quest steps run on the game loop.** The classic quests' 5-second check (map markers, the monsters that appear
  for your step, items handed to you, places reached, items held) ran beside the game loop and could race with it ("it
  just didn't count"). The timer now only asks the game loop to run it.
- **The quest harness.** A test plays a quest from offer to finish on the server's own quest code with a fake player:
  talking, kills by you, by a groupmate and by a gamebot in your group, deliveries, collections, whispers, searches and
  every classic custom step, checking the rewards, the XP at your XP-by-level rate and the quest markers. A world report
  walks every quest of a game's world database the same way (read from a copy, never written) and lists which quests
  can't be completed and why. How to run it on your PC: docs/QUESTING.md section 6.
- **Checked:** automated only. New tests `UT_QuestHarness`, `UT_QuestHarnessWorld`, `UT_EpicChainLinks`; the full server
  suite passes (4,796 tests); the chain links match `epic_chains.py`'s output exactly on its own test world; the installer
  tests pass with the new rollback checks. The world report on a copy of the owner's test world: all 1,540 quest rows finish
  every walk. Not played in game.

**An automated playtester** (for testing, not in the game; [docs/PLAYTESTER.md](docs/PLAYTESTER.md)). Until now every
release was checked by hand in the game client. A scripted player can now run the same checks with no person at the
keyboard, overnight or in CI:
- **`make_test_world.py`** makes a test world from a clean release world: a new file, every save emptied as a release
  is, three test accounts and a small roster of gamebots. Each realm's first gamebot is level 12 and trained for it,
  so the group scenarios have someone to group with. It only reads the world you give it and never writes over a
  file. A `minimal` world needs no game files at all.
- **`OfflineDaoc.Playtester`** connects to the server as the original 1.127 client does, logs in, makes characters
  and plays scenarios: log in, save and restart the server (the character comes back where it quit); say and tells
  between two players; a group invite with group chat; a tell to a gamebot (it answers); /faction lines a minute,
  passing at half the rate the server's own chat budget predicts for the bots online and the hour; a delivery quest
  taken, carried and handed in.
  - **A kill quest in a group with a gamebot.** A player is made an Armsman a few levels above the quest. It trains
    at its trainer, buys a weapon, invites the level-12 gamebot, kills the target with it and hands the quest in.
  - **Guild dues and merit.** A player's own guild sets dues, earns merit for its levels and takes a gamebot in. The
    dues from the coin they loot together go to the guild bank.
  - Each run writes a report in Markdown and JSON with timings and every chat line it saw.
- **/faction counts the reply budget** (owner 2026-11-05, with PR #82). The expected /faction rate is now the chat budget's
  target plus its reply target, as the 1.2 server paces them. A run above the server's ceiling (the ceiling plus the reply
  ceiling) is flagged in the report and the console; `--fail-over-ceiling` makes it a failure. Against Beta 1.1.2's
  budget, which has no reply target, the prediction is unchanged, and its 1.2 to 1.4 lines a minute at 10 bots a realm
  are flagged.
- It refuses any database that `make_test_world.py` did not make, and runs the server from a copy of its folder, so a
  test run leaves nothing in your install.
- Its protocol code comes from the new client's network client (`source/new-client`); lometur's HearthDAoC headless
  client was the model for the idea (it runs inside the server, so it was not ported).

**New client (0.37c): skills on the quickbar, Stealth and the merchant's SELL button** (GitHub issue #34). These are
shadowofze's own 1.2 fixes, installed as patches to the new client's scripts (only where the script is the released
0.37c one; ROLLBACK.cmd puts it back).
- **Skills onto the quickbar.**
  - **Before:** an ability dragged from the skills window (Sprint, Stealth, Safe Fall, a realm ability ...) showed a
    no-entry sign over the quickbar and wouldn't go on it. Spells and styles already did.
  - **Now:** abilities go on the quickbar too, and one click on the slot uses them. Shift + right-click empties a slot.
- **Stealth.**
  - **Before:** Stealth was only a line in the specialization list (the server treats it as a specialization you
    use, not an ability), so the Abilities list had no Stealth to click or drag, and /stealth was "not a command".
  - **Now:** the Abilities list has a Stealth row: double-click it, drag it onto the quickbar, or type /stealth (or
    /hide) to hide and to come out again. You don't see yourself as a see-through ghost yet, and the effect bar may
    show another picture for Stealth: both need the 1.2 network client.
- **SELL at merchants.**
  - **Before:** the merchant window had BUY only. Selling worked by dragging an item onto the window, which nothing
    told you.
  - **Now:** right-click an item in your backpack, then click SELL (dragging an item onto the window still sells it).
    The window also gets a quantity for BUY (arrows, KEEP), a scroll bar for long pages and the pack size of arrows,
    bolts and poisons.
  - **APPRAISE** stays hidden in this version of the new client: the server's appraise needs the 1.2 network client
    (its MMOEngine.dll), which this update doesn't install. In shadowofze's 1.2 scripts the button was there but a
    click did nothing.
- The account vault window keeps its title (with the 1.2 scripts alone it would have said HOUSE VAULT).

**Epic raids learn four more bosses (raid tactics, round 3).** Everything below is built and covered by automated
tests; none of it has been seen in a real raid yet. Govannon and Orshom are level 80: this teaches the raid their rules,
it does not make them easy, and the hardest level 70+ encounters, Darkness Falls raid AI and Legion still have no raid
tactics.
- **Grand Summoner Govannon (Summoner's Hall, all three realms).**
  - **Before:** on 2026-11-06 the Midgard raid took him to 53% and then 51%, twice. Each time, at 50%, he turned into a
    demon at full health and the raid fell in about a minute: 88 and then 101 raider deaths, most with nobody hitting
    them. His Shroud of Agony hurts everyone within 300 of his target, and the raid stood in one pile on that target.
  - **Now:** in his demon form, casters, archers and healers stand clear of his target (550 from it) and fight and heal
    from there. Melee can't get clear and fight on, but the healers are no longer dying beside them. The party he is
    fighting holds him.
  - At 80% (in each form) he summons a sacrifice and a demon that walk slowly to his altar. Only if both reach it does the
    Shade of Aelfgar come. The raid's casters and archers now kill them on the way while everyone else stays on him.
  - His full 300,000 health stays switched off.
- **Orshom Brong and Orylle (Krondon).**
  - **Before:** the Albion raid lost at Orshom twice overnight (lowest 59%, then 28%). His fire pit, Orshom's Fire, burns
    500 every 3 seconds within 300 of it and only cold hurts it. Bots it burned fought back, standing in it.
  - **Now:** the fire is never a target, and the raid never turns on it because "it is fighting the raid". A raider within
    450 of it steps out to 600 first and fights on.
  - When Orylle joins at 50%, the party she attacks holds her and everyone else finishes Orshom first. The raid also goes
    for Orshom before her.
- **Galladoria.**
  - **The Earthmenders' class rule is back on.** Each of the four Earthmenders that heal the Corrupt Primal of Earth
    again takes damage only from its own classes, as in the original:
    - Guardian: blademasters, heroes and champions.
    - Magical: eldritchs, enchanters and mentalists.
    - Natural: bards, druids and wardens.
    - Shadowy: nightshades and rangers.
    - Animists, valewalkers and pets hurt all four.
  - The raid now kills the Earthmenders before the primal, and each raider takes one its class can hurt. A class none of
    them takes damage from (bainshee, vampiir) fights the primal and keeps it busy. A player fighting them yourself plays
    by the same rule.
  - **Spindler Broodmother.** Casters and archers kill each newborn spindler as it comes, while melee and healers stay on
    her. The 20-25 small spindlers that burst from her body when she dies are cleared there before the raid moves on.
- **A quieter log.** `REALM_EXPEDITION_EARLY_PULL` had one line for every Darkness Falls named the raid walked into (53
  in four hours). It is now logged for a raid's final boss and for catalogued scripted bosses (once per boss every ten
  minutes), and always for a pull-back. The rest go to the debug log, and the next logged line counts them
  (`quietSinceLast=`).
- **The encounter catalog** (`docs/encounters/EPIC-ENCOUNTERS.md`) marks the new tactics "1.2 (new)". It also shows
  which restored mechanic is now on, and records what the 2026-11-06 logs showed for each of these bosses.

**Also in 1.2: measuring the AI**
- **AI_PROFILE: what the bots' AI costs, measured every minute** (technical audit 2026-11-03, "measure before
  adding"). Nothing in the game changes: it only measures.
  - **What:** once a minute the server log gets one `AI_PROFILE` line: the game-loop tick's p50, p95, p99 and
    maximum, split into the serial planning before the brains and the brains' parallel pass, the time spent waiting
    for the coordinator, raid, RvR and database locks, and the ten costliest AI systems. About 70 systems are timed:
    brain turns by kind, each serial coordinator pulse, the world controller and its branches, route planning and
    navmesh queries, the frontier threat scan, combat AI, pets, persistence, chat, economy and town visits.
  - **Where:** `logs/ai-profile.csv` (one row per system a minute), `/lsprofile` in game (GM), and
    `tools/perf/ai_profile_report.py`, which turns a log into a top-ten table, a tick timeline and the L300/L600/L900
    load-test verdict. How to read it and how to run the load tests: [docs/PERFORMANCE.md](docs/PERFORMANCE.md).
  - **Setting:** `ai_profile` (on). One timed call costs about 65 ns (a CI test fails it above about 100 ns).
  - **Honest settings:** `ai_budget_ms` was described as a budget that defers work, but nothing reads it. Its
    description now says it is not enforced yet; the deferral queue is decided after the L300/L600 baselines.
  - **Credit:** built on lometur's HearthDAoC bot CPU meter (per-thread counters, brain time by bot kind) and
    stefanrows's `BotThinkProfiler` (the timed scope, the measured lock, the minute line), both GPL-3.0.
  - **Checked:** 33 new unit tests (percentiles, merging, self time, locks, output, the scope's cost) and 12 for the
    report tool; the full server suite passes (4,123). Not yet run on a live server: the first L300 run is the owner's.

**Epic encounter play test (1.2).** The epic encounters were run on a real server with the real world and navmeshes, with
gamebot raids, and these came out of it:

- **`/epictest`, a raid on a chosen encounter now.** For the owner and testers (GM in game, or typed in the server window):
  `/epictest start <event> [era|start|size] [focus=Name,Name]` opens the launcher's forced raid at the encounter's period
  size (docs/RAID-SIZES.md) instead of 300, and moves the reserved level 50s to the raid's muster hub; from there the raid
  forms, travels, stages and fights as any raid. It goes in when 80% are staged, runs on the automatic raid's clocks (a late
  or short start, the four-hour window) and does not count toward the raid's win-rate record. `focus=` sends the dungeon
  route only to those named encounters (the way in is still cleared, and a named that attacks the raid is fought).
  `/epictest gather`, `reset` (an event's cooldown), `status`, `stop`, `list`.
- **Commands typed in the server window work again.** The console ran every command without an account, so each one
  failed with an error in the log; it now runs them as an admin named Console.
- **The server log says what the lore engine says.** Each boss yell and centre-screen line it shows is a `LORE_LINE` line,
  and each hit that heals Funus to full is an `EPIC_FUNUS_HEALED` line (who, class, weapon), so a fight can be read
  without the game client.
- **`/epictest start ... atstaging`** musters the raid at its own staging post (outside the dungeon door, or the dragon's
  ring) instead of the realm's hub, to test the fight alone.
- **A boss that heals itself is no longer skipped while the raid wears it down.** The dungeon route set an encounter aside
  after "no damage for five minutes" unless its health went below its lowest yet. Easmarach takes back an eighth of his
  health on the waterfall top each time he falls to 30%, so the raid shooting him from the falls stands took him from 42% to
  35% and he was still skipped (as on the owner's test server on 2026-11-06, at 39-42%); out of combat he healed to full.
  Health going down since the last look now counts as damage (also Grand Summoner Govannon after his full heal at 50%, and
  Funus after a stray hit heals him): only a target nobody hurts is set aside.
- **Easmarach comes back down from the waterfall.** At 30% he climbs the waterfall top and heals an eighth. In the original
  he heals there every time he falls to 30% again, and on this server he never came back down (no walked path leads off the
  top), so a bot raid, which can only shoot him up there (his spells resist is 70%), wore him from 42% to 34% in ten minutes,
  he healed back to 42%, and so on for good: he could not die, and with his push behind the falls on, Galladoria's last room
  never came. He now heals there once a fight, rests, and walks back down to his spawn to fight on. The original is the
  owner setting `epic_easmarach_endless_falls_heal` (off).
- **A raid on its way turns on a named that attacks it.** Walking to its next encounter or its final staging with no
  target of its own, the dungeon route ignored an epic named fighting it: Caer Sidi's raid walked past the Host to the
  horsemen's room and he killed 40 of it. It now fights the named that attacks it, as it already did while gathering for
  another encounter.
- **Soul Reckoner is led back into his room, and hit while he is in it.** The raid stopped hitting him 200 short of his
  room's edge, so with him 790 out (still in his 900 room, taking damage) nobody hit him; and the raider he chased was busy
  with one of his souls, so nobody led him back. The souls spawn where he stands and kept the raid there. Now the raid holds
  only when he is out of the room, and whoever he chases leads him back to the middle, soul or no soul.
- **A navmesh audit of the encounters' positions** (`UT_EpicPositionAudit`, a tool run by hand): every throw, teleport and
  jail landing, and the tactics' waiting spots (Easmarach's waterfall stands, the Sacristan's stations, the Vifil tunnel,
  Kula's frost circle, the Apocalypse room), checked both ways, to the boss and back.

**The loading screen says LIVING SERVER** (owner 2026-11-07). The classic client's loading screen
(`pregame/splash.mpk`) is the Living Server painting with "LIVING SERVER" in gold at the top left, over a gold rule.
Built and checked by tools; nobody has seen it in the game client yet.
- **Upgrades get it too.** The installer now replaces the 1.1 loading screen as well as shadowofze's, so every
  1.1, 1.1.1 and 1.1.2 player gets the new one. A content-patch entry can name earlier Living Server versions of its
  file ("Replaces"); each is backed up and checked like the released file. Any other loading screen is left alone.
- **Living Server's loading screen stays** (owner: "I don't want the original splash screens, I want mine"). ROLLBACK.cmd
  puts back the 1.1 loading screen if that is what the install replaced, but never shadowofze's: after a first install
  it leaves the new one in place, and puts only the server, launcher and other game-file fixes back.
  PREPARE-FOR-UPDATE.cmd leaves it too. shadowofze's update tool checks only the files an update replaces or removes,
  and his loading screen is the same in 0.36, 0.36b and 0.37b, so the 0.37 update never looks at it. (Should a later
  update ever change it, his tool stops and names the file before changing anything.) The entry's "Keep" says this.
- PREPARE-FOR-UPDATE.cmd finds a released game file in any install's backup, not only the newest one's, since the newest
  may hold an earlier Living Server version.
- The picture is made by `tools/installer/art/make_art.py splash-title` and the archive by
  `tools/fork-build/make_pregame_splash.py`, both repeatable byte for byte (the archive keeps its files' times, not the
  time it was packed). The archive is 14,958,372 bytes, SHA-256 `01cfe3e0...c66660`.
- **Checked:** the installer tests (fake game folders) for shadowofze's loading screen to the new one, the 1.1 one to the
  new one, an unknown one left alone, ROLLBACK.cmd after each and PREPARE-FOR-UPDATE.cmd. The archive was unpacked again:
  eight entries, and the picture decodes to the approved image pixel for pixel. Not yet checked: the game client showing it.

**Epic encounter play test, round 2.**

- **A brush with a named monster on the way no longer counts as the raid's wipe.** A dungeon raid counted an attempt as a
  wipe when the encounter healed back to full after being taken to 85% or lower. In Caer Sidi a calcified manes (a named
  monster on the way) nicked to 84% and left by 91 of 95 living raiders was counted as the raid's first wipe and called
  the Special Forces. A reset now counts as a wipe only from 70% or lower (Spindler Broodmother lost at 42% still does);
  half the raid falling is a wipe as before.
- **Bow-only raiders keep their bow on Funus.** With his weapon puzzle on, a scout he was fighting had every bow draw
  interrupted by his hits and switched to her sword each time; every swing healed him to full (462 times in 25 minutes,
  never below 89%). A bot that may fight a target with its distance weapon only now keeps it when its draw is
  interrupted or it loses sight.
- **A boss attacking the raid comes before a lesser named.** On Caer Sidi's lower floor the raid fought the archlich and
  other named monsters while the Host killed 219 raiders over the run; a boss fighting the raid is now turned on first.
- **Play-test harness:** a dragon raid's stragglers are gathered on its staging ring, never onto the dragon (the gather
  pulled Gjalpinulva twice), `/epictest front <event>` moves a raid inside a dungeon to its route's front (to test a final
  room without the climb), and sizes are whole parties (a dragon's 70 is 72).

**Movement that doesn't look scripted** (planned as 1.2.1, the first slice of 2.0's "bots as people"; owner 2026-11-08:
"Pull all the 1.2.1 into 1.2"). For gamebots only: your companions and the bots in your own group behave as before, and
raids and epic encounters keep their own positions (their tactics, stations and blast dodges stay in charge). Each of
these is a habit players had, not a random wobble. They pass automated tests; nobody has played them in game yet.
Nobody else's fork has bot strafing (fork survey, 2026-11-08), so this is our own.
- **Melee bots circle in a fight instead of standing frozen.** A melee gamebot hitting a monster that fights someone else
  takes a short arc around it now and then (every 7-15 seconds, 28-60 units, never more than 45 degrees), at the distance
  it already stands. It steps away from a party member it is standing in (checked every 2.5 seconds), out of the monster's
  face to the nearer side, and never out of the spot for its back or side style: the flank step picks the spot, circling
  keeps the bot from standing there like a post. It keeps swinging and keeps its styles queued while it steps.
- **Tanks holding aggro stay put and face the monster.** A bot the monster is hitting in a solo fight takes only a small
  step now and then (every 21-45 seconds).
- **Casters step between casts.** Right after a cast ends, now and then (every 12-24 seconds), a caster takes one short step
  to the side and a little back, only to a spot it still sees its target from, and casts again as it stops. Never during
  a cast, never with a monster on it.
- **Every step stays on the ground it can walk.** Each step is a few short legs walked on the navigation mesh first: on dry
  ground, on the bot's own floor (no step up or down more than 32), never through a wall or off a ledge. If the mesh
  doesn't allow it, the bot doesn't move. No navigation mesh was changed.
- **Fewer snapped turns and stops.** Each leg is short enough that the bot keeps facing its target instead of turning
  sideways to walk and snapping back. A gamebot following its party leader, or standing at a monster, no longer re-faces
  it on every follow check (twice a second): only when it is more than 30 degrees off. A wide look around, a wave at you or
  a cheer is turned in steps of at most 45 degrees about a tenth of a second apart instead of one snap. Each gamebot
  stops its melee approach a few units short of the usual distance (the same for it every fight), so a monster's
  attackers don't all stand on one exact ring.
- **Town breaks at the bank and the usual crowd spots.** A gamebot on a town break no longer stands on one spot for its
  15-30 minutes. A minute or so after it walks in, and then every 2-6 minutes, it wanders over to the bank (the vault
  keeper, the usual spot), a merchant, a trainer, or a crowd already standing in town (other bots or you; a crowd of 12 is
  left alone), stands in front of what it came to or on the crowd's edge on its side, and faces it. Only a short walk
  with a complete path, inside the town, on dry ground, on the same floor, out of every monster's aggro range, and never
  while it rests or fights.
- **Measured.** The AI profiler (`AI_PROFILE`) has two new systems, `combat.movement` and `town.hangout`. Every 5 minutes the
  server log counts the steps (`BOT_COMBAT_MOVEMENT`: circles, small steps, steps aside, legs, blocked steps, caster steps)
  and the town wandering (`AUTONOMOUS_TOWN_HANGOUT`).
- **What the server can't do.** The game has no "strafe" for an NPC: the original client shows a bot that walks while
  facing its target either as a side step or as a slide, and only playing it shows which. A walk longer than a step still
  turns the bot to face where it goes in one update, and the client's own smoothing of a stop is the client's.

## Beta 1.1.4 — 2026-11-12

Setup only. The server and the launcher are the same as in 1.1.3.

- **Setup downloads the game from Living Server's own copy.** shadowofze's Offline DAoC repository is no longer online,
  so the 1.1.3 Setup could not download the game for a new player. Setup and the downloader now read the same release
  files from `chris4aker/OfflineDAoC-base-files`, checked against his SHA-256 lists. The downloader is his 0.37 script
  with only that address changed.
- **New installs are Offline DAoC 0.37**: the original client and the Classic + Shrouded Isles classes. Setup no longer
  offers the 0.37b edition (his Sluaghbinder class) or the 0.37c new client. Games that already have either keep working
  with Living Server and keep getting updates.
- README: dead links to his repository removed.

## Beta 1.1.3 — 2026-11-08

Started as a hotfix (/faction chat that holds together, out of Xanxicar's stomp, Scurceol Hyrde's orbs shot down from the
floor, two Darkness Falls exits, Setup waiting out a busy download server) and grew after the owner's overnight checks of
the release candidates on 2026-11-09 (owner: "Hold 1.1.3 and fix everything you identified", "You need to come up with
fixes for ALL of that"): bot chat that sounds like a real server, bots that notice the player, raids that don't stall,
keeps defended in time, quiet nights, fewer stalls. How it was tested, and on which build, is in the release notes
([docs/RELEASE-NOTES-1.1.3.md](docs/RELEASE-NOTES-1.1.3.md)).

**/faction that holds together** (owner 2026-11-08, watching 1.1.3's /faction: "faction feels disconnected... The chats
MUST be top notch"). Checked by automated tests and a simulated evening, then on the owner's PC: the chat checker
read 68 minutes of an evening on the second release candidate (6a83c03) and 34 minutes on the third (b72716e, the same
chat code) and passed both.
- **Conversations in order, each speaker in its part.** Every line of a bot conversation (a question and its answers, a
  story about a death, the replies to a remark) was its own timer, set when the conversation was planned. A line held
  back 2-4 s because the channel had just carried one (up to three times) then came after the line meant to follow it,
  and a line whose speaker was fighting was dropped while the rest still played: "main line first" before the question
  it answers, the asker's thanks before the answer, "sup" last; "kk inv" before "what lvl r u?". Now a conversation is
  said one line after another, each timed from the line before it. A line whose speaker can't say it (fighting, gone)
  ends the conversation, so nobody thanks an answer that never came; a third bot's aside is just left out. A short reply
  ("which part?", "ty") is not said once three other lines have scrolled its question away, and an "anyone?" two minutes
  later only if its speaker said nothing else meanwhile. At most three conversations run in a realm's /faction at once.
- **Answers that are true of the bot.** "need a tank for sh barrows" drew "sec, me" and "cab, 50" from a cabalist. A
  group ask is now answered only by a bot that fills the role it asks for (tank, healer, crowd control, puller, damage, or
  a named class) and is in its levels (the range it names, else within 6 of the asker); "any reavers on?" is answered
  "me" by a reaver only. Asks name only classes of the asker's realm (an Albion "any druids on?" was possible).
- **Follow-ups that fit.** The asker no longer asks the answerer's question ("solo or grp?" after "any reavers on?"); it
  asks the question it had ("what do u put points in at 50?"). A gear, crafting or epic question no longer runs on to
  "which part?" and "the map helps"; only quest, hand-in and bind questions get a "where exactly?", answered with a place.
  Nobody asks a level or class the answer already gave. An answer that asks back ("where at?") is answered by the asker,
  with no "ty" to a question. In a death story "where?" comes only when the teller didn't say where, and the teller then
  says where. A trade post answered "gl selling, someone will want it" no longer gets "ill keep it then".
- **Reactions in proportion.** A bot's revenge or nemesis line drew three to eight reactions over a minute and a half,
  and two of them ran into each other ("that stinks" and "nice!" to two lines at once). Now a bot's own moment draws one
  or two reactions within 20 s, realm news (keeps, relics, raids, Darkness Falls) three at most (four from 300 bots a
  realm) within a minute, one burst at a time in a realm, each from a bot free to type, and a reaction is left out once
  four other lines came after what it reacts to. Guild threads are said in order and answered by guildmates who fit too.
- **One "oof" in ten minutes.** A short reaction ("oof", "rip", "lol rip", "gz") a realm's /faction heard in the last ten
  minutes is picked again or left out, in stories, bursts, threads' answers and single reactions.
- **/faction keeps to its ceiling, whoever speaks.** On the owner's evening /faction ran at 13.6 lines a minute for ten
  minutes (minutes of 17-20) against its ceiling of 10: unprompted lines stopped at the ceiling, but replies, stories,
  bursts, nemesis and revenge lines, news, debates, rumours, bonds, gold spammers and Leeroy's friends didn't. Now every
  bot line in /faction passes one gate: it is said only if it fits under the realm's ceiling (the rate it would make, over
  two minutes, as the server measures it). A conversation's line waits for room like a busy speaker, a debate's beat waits
  (the scene owns the channel, so it drains), and a line that starts more (a rumour, a bond, a nemesis line and its burst)
  starts nothing when it is held. Two kinds pass the gate, tagged in the log so a check can tell them apart: `src=event`, a
  realm's big moment said on purpose (a keep or relic taken, a raid's end, Darkness Falls, a server first, the raid's
  Leeroy), whose burst is itself capped at three reactions within 40 s; and `src=player`, a line to or about the real player
  (bots answer you first). With bot voices on, a server first no longer adds its separate cheers on top of its burst.
- **A debate, a storyline episode or a dunce's roast plays to its end.** While a scene plays it owns the realm's
  /faction: every other line but an event or one to the player yields to it, so each beat finds room under the ceiling
  within seconds. A beat waits for room three seconds at a time (an optional beat isn't re-rolled meanwhile); only after
  two minutes is a scene cut (`BOT_DEBATE_END reason="no room in /faction"`), and a dunce who already spoke still sends
  its parting tell and rage quits then. Checked on every scene in the corpus, on a realm already at its ceiling with a
  keep falling mid-scene.
- **You are answered during a scene too.** A line to or about you is never held by a scene or the ceiling, and "where do i
  buy a better staff for my elem?" (no answer at all in the live test) now gets a true one: the Realm Exchange in your
  capital, or the crafter who makes it (a fletcher for staves and bows, a weaponcrafter, an armorcrafter or a tailor).
- **No rush when a scene ends.** For two minutes after a scene a line that starts something waits ten seconds after the
  line before it (three seconds otherwise), and /lfg and /trade no longer wait for a /faction scene at all (the live test
  heard 7 lines in 7 s as a debate ended). A roleplayer held back tries again in a few seconds.
- **`/lstest roleplayer` and `/lstest spammer` say when the line really comes:** within a few seconds, once the scene
  playing now is over, or as soon as /faction has room.
- **A scene's part keeps its bot (after RC1).** The owner's check of RC1 (d30ba0fa, 2026-11-08) heard the dunce of
  `dunce_pause_game` start as Iagoys and go on as Yno, who rage quit: Iagoys was in a fight for over 45 s, and the scene
  gave the part to another bot. Now a part that has spoken is never given to another bot, in a dunce scene, a storyline
  episode or a debate: a speaker fighting, casting or dead is waited for (45 s) and then says its line anyway, as players
  typed between pulls; one that left the game ends the scene (`BOT_DEBATE_END reason="role A spoke and its bot left"`).
  Tests (every scene in the corpus, with its first speaker busy for minutes, and gone), then on the owner's PC with
  RC2 (6a83c038): `/lstest dunce` played `dunce_pet_warrior` with one speaker from start to rage quit.
- **A gold spammer, a rumour or a roleplayer that is due gets its turn (after RC1).** In the same check
  `/lstest spammer` logged `GOLD_SPAMMER_START` and the spammer said nothing for over two minutes: it came as a debate
  began, /faction stayed at its ceiling after it, and every held ad was spent with the next tried 18-48 s later.
  `/lstest rumour` said "no free bot of your realm had a line for it" when /faction had held the line, and a held rumour
  was lost. Now a held ad, rumour or in-character line keeps its turn and is tried again every few seconds; its lines still
  count against the ceiling. Time behind a scene doesn't count; after three minutes without room it gives up
  (`LIVING_FEATURE_DROPPED`). The log says when one is due (`LIVING_FEATURE_DUE feature=spammer|rumour|roleplayer ex=`),
  `GOLD_SPAMMER_START` comes with the first ad really said, and a spammer that never spoke is not banned or cheered.
  `/lstest rumour` tells apart a scene playing, a full /faction, no free bot and no written line, and the first free bot
  with a line tells it (only one bot was asked before). The rumour's replies, the spammer's reactions and the
  roleplayer's mockery share the line's exchange id. On the owner's PC with RC2 (6a83c038): `/lstest spammer` spoke
  after a 7 s wait and ran its five ads with reactions; `/lstest rumour` said honestly that no rare named was near.
- **No wall of "released, running back" at the bind (after RC1).** Standing at Camelot's bind the owner heard about 25
  lines in 45 s from different bots ("released, running back", "at bind, omw back", "no rez, released. coming back to
  you"): a gamebot's word to its own group about its way back went out in /say to everyone near. These lines (released,
  horse, off the horse, back, still on the way) now go to the bot's own group, and only when a player is in it; a group of
  bots says nothing. /say lines carry their ceiling (`cap=`), and `chat_audit.py` prints the /say lines a minute and fails a
  log with a minute of /say above its ceiling around the player. On the owner's PC with RC2 (6a83c038), at a busy
  bind: 1.3-2.6 /say lines a minute.
- **One greeting per bot per meeting (after RC1).** Cabowyn said "hey {player}! good to see you again" and a second later
  "hey hey {player}, City of Camelot treat you well after i left?": a GM test had cleared the greeting cooldowns while the
  first greeting was on its way. A bot that greeted a player, or is about to, doesn't greet them again for ten minutes,
  whatever cleared the cooldowns; `/lstest greet` says so. Tests only.
- **A deed names a place it could happen (after RC1).** A forced deed read "saw {player}'s group take a named monster in
  City of Camelot". A real deed names the zone where it happened (the monster's or the player's body); `/lstest deed` names
  where the GM stands only if it could happen there (no capital; a streak or a gank only in the frontier), else a hunting
  zone of the realm or a frontier zone. Tests only.
- **A death story's opener keeps the ten-minute rule too (after RC3).** On the owner's RC3 evening (b72716e, 34 minutes)
  different bots opened a death story with "dead. again." seven times, twice within a minute. A story line of three
  words or more is told once an evening, so once the longer openers had each been told only the two-word one was left,
  and the teller's own short line was let past the channel's ten-minute window (only the replies kept it). Now the
  teller's short line keeps the window as well: the story picks another opener, and with none left it isn't told.
  Tests only.
- **A frontier death isn't told as a monster (after RC3).** In the owner's RC2 and RC3 evenings most death stories from
  the frontier read "siuses in murdaigean hate me", "train of sneakywarriors, im dead" or "killed by xdemonxes at 27": the
  story took the bot's last target, an enemy player, as a kind of monster and made it plural. Named creatures came out
  the same way ("died to summoner roesias again"). Now only a kind of monster ("thrawn ogre miner") is talked about as
  one; a person, a guard or a named creature is not, in stories and in the bots' other lines. A death in the frontier is
  told as one ("got ganked lol", "zerged in emain", "stealther got me"). Tests only.
- **Realm news in a player's words, not the herald's (after RC5).** On the owner's RC5 evening bots said the realm's
  announcements in /faction word for word: "Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel; at least
  93 must arrive before assault.", "The Krondon expedition is advancing with 200 staged level-50 adventurers.", "Fighting
  has broken out at Dun Crimthain. There is an opportunity to intervene." and "Warbands are gathering to assault Nottmoor
  Faste. Join them at the siege camp". When a bot says one now, it says it the way a player typed it, short and in
  lowercase, with the names as chat shortened them and no head counts ("krondon raid forming at diogel, need 50s", "fight
  going at crimthain, anyone?", "warband going to nottm, siege camp"), in several ways for each announcement. Bots
  telling the news in their own words shorten the names too ("we lost ailinne to hibs", not "we lost Dun Ailinne to
  Hibernia"). The announcement at the top of the screen, and a notice with no bot to say it, read as before. Tests only.
- **Reactions to the realm's news vary over an evening (after RC5).** "ugh, that one again" (RC3 and RC5), "oh well not
  my keep" and "lol of course" came back to the next keep lost. A reaction of three words or more is now heard once in a
  channel in three hours, however it is punctuated, and each kind of news (a keep taken or lost, a siege held or failed,
  a relic, a raid's end, Darkness Falls) has about twice as many reactions to choose from. A burst that runs out of fresh
  reactions is shorter, never longer: one short reaction per ten minutes and the reaction caps hold as before. Tests only.
- **Debaters type, they don't narrate (after RC5).** Next to "lol rip", some debaters wrote like a book: "People said the
  same before the dragon lairs. One winter they were just there, all three realms, same week." A debate line is now typed
  in its speaker's own style and habits: a long turn goes out as two or three chat lines split where a player would hit
  enter, no writer capitalises the second sentence of a line or ends it with a full stop, and a terse writer takes the
  shortest way the scene gives to say it, without commas. What each speaker says is unchanged. Tests only.
- **More ways to tell a frontier death (after RC5).** "got ganked lol" came twice in 13 minutes on RC5. A frontier death
  story now has hundreds of openers (ganked, zerged, stealthed, caught at the milegate or the bridge, ran into a group,
  died to a guard...), more answers to "where?", "by who?" and "how many?", and the teller's lines are kept for the
  evening however short. "same happened to me in ..." names a frontier zone, an enemy realm's only as "their frontier":
  RC5's "ganked in jamtland mtns" drew "same happened to me in dales of devwy", a hunting zone. Tests only.
- **A deed names the monster and where (after RC5).** RC5's `/lstest deed solo` read "not lying, i just saw {player} kill
  a named monster, no help at all". A witness now names where it happened in most of its lines, with the zone as chat
  wrote it ("black mtns"), and names the real monster; the forced test takes the targeted or nearest named monster and
  says so when there is none. Tests only.
- **The checker finds lines in system voice (after RC5).** `chat_audit.py` counts bot lines in /faction, /say, /lfg, /trade
  and /broadcast that read as a game announcement ("level-50 adventurers", "There is an opportunity", "Join them at", a
  semicolon, a formally capitalised paragraph of more than 15 words), with examples under `--show`; one is enough for
  `VERDICT FAIL`. It also counts, for information, the same reaction of three words or more again within an hour. It
  fails the owner's RC3, RC4 and RC5 logs and passes the simulated evening of this build. Tests only.
- **Bots answer each other's questions, and /lfg and /trade get replies (after RC5).** On the owner's RC3 evening (b72716e,
  151 minutes) 71 of the 106 questions bots asked in /faction got no other line at all, and on RC5 (b8f30f9, 30 minutes)
  15 of 21; counting only replies from someone else within a minute, 86 of 106 and 17 of 20 went unanswered. Half of them
  were written with no answers ("where's the paladin trainer", "what spec for a 50 inf?", "any stealthers out?", "fastest
  way to darkness falls?"); the rest were dropped by a 30% chance of no answer, an answer that could be said only once an
  evening, or an answerer who was fighting by the time its answer was due. Now every question has answers that fit it (the
  trainer in the capital, the class's usual spec and weapon, drops or spellcrafted gear by level, back to whoever gave the
  quest, kills and keeps for realm points), and where the world has the fact the bot says that: the real class trainer and
  its zone, the stable master with the route, the camps bots of that level use, an enemy stealther the realm can see, who
  is really at a keep. A question is always answered when a bot can answer it; one that would find the channel at its
  ceiling or with three threads running isn't asked. A heard answer comes back after 30-60 minutes, another bot that can
  answer says a busy answerer's line when it says nothing about that bot, and a regular answers a question first two times
  in five, so the regulars still carry about half of /faction. A player's "where do i get my spells
  trained?" (RC2: "no idea") names the trainer, and a player's spec, crafting, quest, realm point and epic questions, in
  /faction, /say or a tell, get the same answers. In /lfg and /trade (RC3: 260 and 213 posts, no reply to any) a share of
  posts get a short reply from a bot it is true for ("x" or "still need a healer?" from a solo healer in the levels asked
  for, "how much?" from a bot that wears that armour, "/t me, have one" from a bot carrying it), the poster's answer is
  true (its asking price, its real spec, "ya, 1 spot" only while its group has room), and a few minutes later "nvm full",
  "got a grp" or "sold" only when that happened. Every line still waits for room under its channel's ceiling.
  `chat_audit.py` counts bot questions with no reply within 60 s (`VERDICT FAIL` above 25%; RC3 and RC5 fail) and reports
  the share of /lfg and /trade posts with a reply. On the simulated evening 80 of 90 questions are answered within a
  minute (34 of 114 before) and the checker passes. Tests only.
- **"ya, 1 spot" only from a party that is still recruiting (after RC5).** The /lfg replies above had the poster say "ya,
  1 spot" or "ya, still room" for any bot party under eight. Bot parties are formed at their full size and never take
  anyone later, so a party has a seat only while it is still assembling with seats free; and "still need a healer?" is
  yes only while nobody in it fills that role. A party that set off says "nope full, sry"; one still forming that has its
  healer says "got a healer already, sry". Tests only.
- **An item is paired only with a class that can use it (after RC5).** On the owner's PC: "99% adroit focus staff any good
  for pally?", "95% adroit focus staff any good for friar?", "99% earthshaker chain boots any good for sorc?", "99%
  matterbender medium shield any good for infil?" and "wtb a shield for a 30 cabalist pst". A bot asks about its own item
  only when its class can use it, by the server's own rule for the live bot (the class's real weapon and armour skills)
  and a focus staff only for a caster; armour it wants is the weight its class wears at its level, by the game's item
  generator ("armor for a 29 cabalist" is cloth, a 15 paladin's is chain); arrows only for an archer; "my old ..." only
  for an item it could have worn; a written line that pairs an item with a class that can't use it is skipped.
  `chat_audit.py` counts "an item paired with a class that can't use it" (FAIL). Tests only.
- **One story per bot in its group posts (after RC5).** Thorroir, a 50 minstrel, said in /broadcast within twelve
  minutes "minst lfg 50, x me", "lfm barrows 47-50", "lf1m dps barrows pst" and "lf1m 47-50 xp grp stonehenge", and
  later "any minsts around? need tips". A bot now keeps one story for 30 minutes across /lfg, /broadcast, /trade,
  /faction and /say: it looks for a group or forms one, not both, and its posts name one dungeon, by the word it first
  used for it ("barrows", not "barrows" then "stonehenge"). When its state changes it says so where it posted, once ("nm got a
  grp", "nvm, full", or "nm, heading out" for a party that set off short of eight; one closing per post, whether the
  /lfg reply's closing or the bot's next post says it) and may then start a new story. A WTS line may still repeat every few minutes as a macro, as players'
  /macro did in 2003, but a bot runs one macro at a time and it stops once the item is no longer in its bags; it only
  sells what it carries. `chat_audit.py` counts "a speaker looks for a group and forms one within 30 min" and "a
  speaker's group posts name two dungeons within 10 min" (FAIL). Tests only.
- **Group posts follow the bot's real group state (after RC5).** The server log shows Thorroir in a group from 05:36
  when it posted "minst lfg 50, x me" at 05:38. Now only a bot out of a group that wants one (group matchmaking holds it,
  or it is out for RvR) posts "lfg"; a bot on a solo task doesn't, and a bot in a group never does. Only the leader of a
  bot party that is still assembling with seats free posts "lfm", naming the role the party really lacks (healer, tank,
  cc, else dps), its real camp and its members' levels; the post stops when the party sets off. Bot parties are formed at
  their full size and not backfilled, so such a post is the assembly's last call. `chat_audit.py` reports, for
  information, an "lfg" from a bot the log shows grouped (AUTONOMOUS_GROUP_FORMED until its TASK_ENDED or NO_SHOW).
  Tests only.
- **A question fits the asker (after RC5).** A level 50 paladin asked "where's the paladin trainer", a 50 cleric "where's
  the clr trainer", a 50 minstrel "any minsts around? need tips". Only a character of level 20 or less asks where its own
  trainer is, only one under 40 asks for tips on its own class, and a 50 asks for no xp group ("50 minst lf xp grp"), no
  xp spot and no "still 50 lol"; every level a question names is the asker's own. `chat_audit.py` counts "a 50 asks where
  its class trainer is or for tips on its own class" (FAIL). Tests only.
- **A PvE line names no enemy realm's zone (after RC5).** Albion bots in the Midgard and Hibernia frontier said "where do i
  hand in the uppland quest?", "which stable for uppland?", "grinding odin's gate at 29", "back in uppland", "where do i
  bind in jamtland mtns" and "dread commanders in jamtland mountains hate me". A quest, stable, bind, camp, grinding, xp
  or duo line, and a monster death story, now names only a zone of the bot's own realm (a death in an enemy frontier is
  "in frontier"); talk of the frontier itself may still name it ("inc", keeps, relics, "lfg 50 merc, odin's gate").
  `chat_audit.py` counts "a PvE line names an enemy realm's zone" (FAIL). Tests only.
- **The checker reads "arms" in a trade line as armour (after RC2).** On the owner's RC2 evening (6a83c038, 68 minutes)
  `chat_audit.py` failed only on "wtb leather arms 4 26 inf", an Infiltrator 26 buying arm armour, read as an armsman
  claim ("class claim not the speaker's", "one speaker claims two classes"). A class word that is also an armour slot is
  now the item in a trade line (wtb, wts, selling, ...) or after an armour word (leather, chain, studded, ...); "arms 45
  lfg" is still an armsman. With it the RC2 log passes. A checker fix only; the server is unchanged.
- **Replies to the player each have their own exchange id.** RC1 tagged them all `ex=0`, so the checker read them as one
  conversation and reported it out of order. `chat_audit.py` also treats `ex=0` as no exchange (for RC1 logs), and now
  counts features that were due and never spoke in /faction: one is enough for `VERDICT FAIL`. The simulated evening has
  a spammer every 40 minutes, a rumour every 20 and a roleplayer's line every 15, each waiting for its turn, and none silent.
- **One reaction in ten minutes, whatever says it.** The ten-minute window also holds back a pure reaction ("lol", "been
  there", "nice going") at the moment it is sent, from any path, not only the ones that plan around it. A conversation's
  line comes at most about 40 s after the one before it, or not at all.
- **Every /faction line carries its ceiling** (`cap=6.9`), so a log can be checked on its own.
- **No more "Fair enough. Safe travels."** It came back 30 times in one night: a fixed line from the oldest reply path,
  outside the no-repeat memory every written line has, like its formal replies ("A merchant price can provide a
  reasonable minimum value."). With bot voices on that path now says only written lines, or nothing.
- **Realm Exchange adverts now and then.** A seller advertised 70% of its listings, only three minutes after the last
  advert or news line (five in 29 minutes on the owner's evening). An advert is now an unprompted /faction line like any
  other: only while /faction is under its target, and one a realm in 15 minutes. One that no written line fits says "anyone
  want ... ? make me an offer" or "pc ...?" (was "Does anybody want to make me an offer on ...?") and gets written trade
  answers.
- **Every line names its source.** `CHAT_LINE` rows now say which feature said a line (`src=field`, `thread`, `story`,
  `reply`, `burst`, `reaction`, `news`, `debate`, ...), and inside a conversation its id, the line's place and the
  speaker's part (`ex=12 i=2 role=answerer`), with the speaker's real class and level (`cls="Cabalist" lvl=50`). `via=`
  skips the delivery wrappers, so it names the code that chose the line (most lines showed
  `BroadcastFactionAsWritten`).
- **Measured:** `source/tools/chat_audit.py` reads `CHAT_LINE` logs and counts conversations out of order or with swapped
  parts (exact with the new fields, heuristic without), thanks and follow-ups with nothing to refer to, untrue class,
  level and role answers, reactions with no trigger, late reactions, pile-ons over the caps, repeated reactions, lines a
  minute per channel, and the seconds and minutes /faction ran above its ceiling (events and lines to the player aside).
  Its last line is a verdict: `VERDICT PASS` only with nothing out of order, no untrue answer, no late or repeated
  reaction, no pile-on over the caps and no minute above the ceiling. On the owner's 1.1.3 evening (13 min of /faction at 11.8 lines a minute): one presence answer after the asker
  had closed, one level question after the invite, one follow-up before any answer, one thanks with no answer, one trade
  closing that didn't follow, one join answer from a class that can't fill the role, 6 pile-ons (13 reactions past the
  second), 3 late reactions, 2 repeated reactions. On a 4.6-hour overnight 1.2-line soak: 207 repeated reactions within ten
  minutes, 9 pile-ons, 5 thanks with no answer.
  A simulated three-hour evening (`UT_FactionCoherence.AnEveningHoldsTogether`: 160 made-up Albion bots, the band of 594
  bots a realm at 8 pm, every path that writes to /faction, the same plans played the old way and the new; audited on
  GitHub): conversations out of order 30 before, 0 after; pile-ons 23 (+31 reactions past the second) before, 2 after
  (keep news, three reactions each, the new cap); late reactions 28 before, 0 after; repeated reactions within ten
  minutes 72 before, 0 after; thanks with no answer 2 before, 0 after; untrue role, class or level answers 0 after;
  5.55 lines a minute before, 4.57 after (most of the difference is the reactions past the cap and the time over the
  ceiling). Busiest two minutes 10.0 lines a minute before, 7.0 after, against a ceiling of 7.1; minutes above the
  ceiling 81 before, 0 after (events and lines to the player aside). The checker's verdict: FAIL before, PASS after.

**Bots that notice you, an evening that remembers, regulars you get to know** (owner's PC, RC3: in 2.5 hours with the
player online in Albion, 5 bot lines named the player, no topic ever came back, and 301 bots spoke in /faction, the busiest
34 lines, the top ten a quarter of them). All behind the switches they already had; none of it seen on the owner's PC yet.
- **Bots take notice of you.** A friend (a bot you grouped with or rezzed, never one that is cool towards you) sends a
  hello when you log in, grats when you ding while it is elsewhere (the bots by you still say it in /say, your guild in
  /guild), and a wave when you reach a zone it is hunting in. A bot standing by when you die alone says "ouch, u ok?",
  and a healer that has really set out to rez you says "need a rez? omw" (once a death, by tell when it is farther off).
  A bot next to you notices a good item you just put on (99-100% quality or above your level). Unprompted lines to you
  share one allowance: about five an hour, five minutes apart, solo; about nine, three minutes apart, grouped or with two
  friends online. The bots' old tells and their /faction gossip about you take a turn too. A death seen in front of a bot
  needs only a minute since the last line. Tests only.
- **Real invites.** Half of a solo bot's "want to duo?" tells now come with a real invitation, a yes/no box; yes groups
  you with it (you lead, as when you invite), and a bot that got busy meanwhile says so. Tests only.
- **A bot that turns your invite down says why (RC5 on the owner's PC).** Captainwaffle, a friend inside Krondon on an
  automatic realm raid, ignored an invite without a word. A bot on a raid, a siege, a relic run, a realm force or a horse,
  or in another player's group, now answers by tell besides the system line ("sry, on the raid in Krondon atm", "in a grp,
  maybe later?"; a friend adds "catch u after?"). An invite that reached the bot but could not add it to the group was
  silent; it now says "... could not join your group". Every invite of a gamebot logs `BOT_INVITE_RESULT` (grouped,
  declined and why, failed), so the next evening's log shows what happened. Why the RC5 invite was silent could not be
  read from the logs (they end before it). Tests only.
- **A friend answers your small talk (RC5 on the owner's PC).** "hey what are you up to tonight?" to Ilaan, who had
  grouped with the player twice at Avalon City, drew "might wander somewhere new". A friend now answers "hey", "wyd" or
  "what are you up to" with your name and what it is really doing ("hey {player}! killing thrawn ogres in Gwyddneau, want
  in?"), asks you along only when it is free to group and near your level ("sure" is answered with how to group), and
  otherwise names where you grouped ("Avalon City again sometime?"). Tests only.
- **An evening that remembers itself.** Each realm keeps a short memory of what its /faction really carried tonight: who
  asked for a group or a camp, whose death was told and to what, who argued with whom in a debate and about what. A note
  is made only once its line was heard. Between 12 minutes and three hours later, at most one in 6-12 minutes and only
  while /faction is under its target, a regular (or the other side of the debate) asks after it: "aldric u ever find a
  grp earlier?", "staying away from bwgans now? lol", "still think ur wrong about houses btw". The bot asked answers from
  what is true for it now: "ya, been grouped since" only when it is in a group, "found one in Lyonesse" only when it is at a
  camp there; a second death tonight is counted ("ur 2 deaths in tonight lol"). The lines are `src=followup` exchanges. Tests only.
- **Regulars you get to know.** The realm's talkative regulars used to rotate one by one through the evening (a three-hour
  turn staggered by bot), so the channel slowly passed through everyone. The cast now holds for six hours, so one evening
  has one cast. Each regular has a part (the helpful one, the joker, the complainer, the guild recruiter, the old hand) and
  two running bits of its own it comes back to every 35-65 minutes ("if anyone needs me ill be face down in a bog
  somewhere"; a recruiter names its real guild), at most one a realm in 8-14 minutes, under the target. A regular tells
  its own deaths and wipes; anyone else about four times in ten. Who answers a line is looked for among 200 bots of the
  speaker's realm (it was 200 of all three realms), so a regular is found most times. Tests only.
- **Group chat that fits the group (RC5 on the owner's PC).** In a duo of the player and Ratoul: "ty for the inv, where
  are you all?", "who is main assist?", and "back" out of nowhere. A /group line to several people, or about the main
  assist or the puller, now needs three or more in the group; "back" needs a "brb" of the bot's own in the last 15
  minutes or a death. A bot that walked over after an invite says "here" or "made it, ready", not "back" (that is for a
  bot back from a release). Tests only.
- **Guild chat in a player's guild sounds like players and doesn't repeat (RC5 on the owner's PC).** Anjaaaaa said "Just
  checking in. All well with everyone?" twice in eight minutes. The fallback lines of bots in your own guild are now
  written as players typed ("hows everyone doing tonight"), in the bot's own style, and the guild doesn't hear one again
  within the hour, like the other guild lines. Tests only.
- **The transcript covers guild chat and your posts.** Bot lines in your own guild's chat, and your /lfg, /trade and /gu
  posts (whatever the guild settings), are now `CHAT_LINE` rows (`ch=guild`, `who=player ch=lfg`, ...). Tests only.
- **Measured.** `chat_audit.py` prints three informational lines per log (not part of the verdict): lines aimed at or
  naming the player per hour (tells to them, `src=player`, their name in a line), the top ten speakers' share of /faction,
  and follow-up exchanges with how many refer to a real earlier line (the bot asked after spoke in that channel at least
  ten minutes before, and the question names it), plus running bits. On the owner's RC logs (before this change): RC3
  (b72716e, 2.5 h) 3.2 lines an hour aimed at the player, top ten 24% of /faction from 301 speakers; RC4 8.3 an hour over
  43 minutes (greetings in /say), top ten 30%; RC5 6.0 an hour over 30 minutes, top ten 38%; no follow-ups in any.
  A simulated three-hour evening (`UT_ChatEngagement`: 594 made-up Albion bots, the 1.1.3 /faction rules with the answers
  of #155 (a question only with room for its answer, a regular first two times in five, a stand-in for a busy answerer), a
  player who logs in, dings every 25 minutes, dies three times, moves zone, puts on two good items and is grouped for an
  hour; the old way and this branch on the same seed; audited on GitHub with the other simulated evenings): top ten 33% of
  /faction from 291 speakers before, 42% from 265 after (the busiest regular 67 lines); bot questions answered within a
  minute 93% before, 94% after; follow-ups 0 before, 15 after, each answered and about a line its bot really said at least
  ten minutes before; running bits 13; lines aimed at the player 4.3 an hour before, 6.7 after; never above the /faction
  ceiling.
  The checker's verdict on both: PASS. Tests only.

**Chat fixes from the sixth, seventh and eighth release candidates on the owner's PC** (eff81a0, b036b3c and f18b44f, 2026-11-09, played
as an Albion 50 and a new Hibernian with the Bot schedule on Real clock). The chat checker passed RC6's 74 minutes, but
reading the lines showed what it couldn't see; it now checks for each of these, and fails RC6's log.
- **A friend's answer to small talk is kept.** "hey! what u up to?" by tell to a friend drew "not sure, try /advice": the
  friend's real answer had no score in the reply budget, which swapped it for a stock line. On RC7 the same tell drew "hey
  {player}! rvr in Odin's Gate, its busy, wanna duo?", true of the bot.
- **Group banter only about a hunt you shared.** A wizard grouped with the player fought alone, died, ran back and said
  "clean pull, ty", "loot is yours", "xp is flying, good call on this camp", "oom, give me a minute" (at full power) and
  "pull?", every 20-40 seconds, while the player had not fought. Hunting talk now needs a fight the player took part in
  that ended with nobody dead; buff talk counts as hunting talk; "I'm low" lines need the bot to be low. On RC7 an idle duo
  heard one line in three minutes.
- **A far bot says where it is.** A bot in another zone offered to group and the player's invite said "too far away". A
  bot close enough to invite now offers first; one further off says where it is; an invite from too far gets the bot's
  tell ("other end of pennine mtns lol meet up?", RC7).
- **One line on release.** A dead groupmate said "long run back to Pennine Mountains" and "released, running back" in
  the same second, and asked "any rez near ...?" in a duo where the player can't rez. Now one line, and the group ask
  only when a player in it can rez.
- **Threads that follow their answers.** "any grps out?" "quiet atm" ran on with "how many?" "small grp" "count me in"
  "omw". An answer that says there is nothing ends the run-on (another bot may answer "anyone else?" from the same true
  pool); rvr follow-ups come only after a line about enemies or the zerg, and a count is no answer to "which way".
- **Deaths told by their real killer.** A death to a monster in the frontier was told as a gank ("got caught by the
  bridge"), one or two attackers became "a zerg, 20+", an enemy bot became "the guards", and an objective name became a
  monster ("died to frontier patrol near death stalkers"). A bot now remembers what killed it, how many were on it and
  what class it was, and the story and the death line follow that. One death story per realm in seven minutes, one story
  of any kind at a time with three minutes between, and a place ("the bridge", "outside the gate") not twice in half an
  hour. A rez request gets a rez answer, not "oof".
- **The time of day.** "busy tonight" at 10 AM, "anyone heading to the frontier tonight?" in guild chat at 11 AM: on
  every channel "tonight" reads "today" by day and "this morning" "earlier" after noon.
- **The bot's real state.** "ok back" only after a brb or a death. No "how many bubbles?" to an rvr post or to a 50. A duo
  offer only from a bot that is free. Written /lfg and /faction group posts ("group at Coruscating Mine needs a healer",
  "group has room near Connacht, all classes welcome", "lfg Stonehenge Barrows or anywhere with xp") are checked against
  the bot's real group and task: those three came from bots in full rvr parties or out for rvr.
- **No internal names.** "which stable 4 group rendezvous?": an internal goal name is never a destination in chat.
- **Debates and scenes don't invent the frontier.** In a debate about scouts a paladin said "it is us scouts", and the
  debate ended with "INC, mid group crossing toward Caer Benowyc" though no army was there. Scripted threads and short
  scenes no longer announce attacks, keeps changing hands, lost relics or enemies at a named place (about 240 lines
  reworded into opinions and calls to head out), and a role that speaks as a class, level or race is cast from it.
- **Stories that fit the death, after RC7 and RC8.** An enemy gamebot was read as a monster (its brain is a controlled
  one with no owner), so "adds in murdaigean hate me" followed a death to one enemy friar; a group wiped by enemy bots
  told "wiped on adds"; "aggroed the guards, dead" drew "stick with the zerg". A bot is a bot first, a wipe to enemies is
  told as one, and a guard death or one to one or two enemies draws no crowd riff. After a "no" the asker says "k ty",
  not "ty, inv".
- **Your /trade post gets answers too.** The player's "wts 99% arcane cloth cap, cheap" drew nothing; only bot posts were
  answered. A player's sale or wanted post now draws a bot's reply as often as a bot's does.

**Chat fixes from the tenth to twelfth release candidates on the owner's PC** (1517b73, 9f44f75 and 153370a, 2026-11-09,
played as an Albion 50 with the Bot schedule on Real clock). The checker passed all three once it counted a group's wipe
as one fight; reading the lines showed the rest.
- **"what class?" is answered with a class.** "spraggon grp?" "sec, me" "what class?" "coming from mount collory" (RC6):
  any answer that wasn't a number fitted. It now takes a class or a dunno. Fewer bots fit that answer, so the simulated
  /faction leans a little more on its regulars; the test's bound moved from 55% to 58% (the owner's call).
- **A roleplayer's question stays a question.** "hail saw thee soloing duo, by my troth": the flourish ate the "?". It
  now goes before it.
- **No goal names as monsters or places.** "careful group rendezvouses link" in /say, and "which stable for master
  visur?" from a bot taking the porter: a goal name is no monster, and a porter or trainer is no place.
- **Gossip about you while you're on asks you.** "anyone know if {player} is on today? want to group again" was said
  while the player was online; it is now "{player}, u around? want to group again", heard on RC11.
- **A scene tells the frontier only through a bot that's there.** A 40 grinding in Avalon played the newcomer's "made
  it to Castle Excalibur", "first realm points", "dead now but worth it". That role now goes only to a bot out for rvr,
  and "first night in the frontier" reads "first time" by day.
- **No grinding a town.** "grinding city of camelot at 50" from a bot back at its bind, and "city of camelot anyone"
  from one that had just ported there: in a town a bot asks for a group to head out with.
- **One story about finding a group.** "any healers on?" "me" "got one ty", then 24 minutes later "u ever find a grp
  earlier?" "nah, ended up soloing": a bot that said it found one is not asked after.
- **A way to a zone of your realm.** "how do i get to pennine mountains from here" was answered "pennine mountains?
  thats in albion". On RC11 "how do i get to cornwall from here?" drew "Cornwall is its own zone, a stable master can get u
  close".
- **An offer to your /lfg doesn't ask what you said.** "50 lf xp grp" drew "hey, im 50, u?" (RC11); on RC12 the offers
  didn't. A bot too far to invite no longer says "we keep going after the same mobs" while it says where it is (RC12;
  tests only).

**Two Darkness Falls exits work** (tested against the world data, and clicked in game on the owner's PC with the second
release candidate, 6a83c03: both took an Albion character to Camelot Hills; found and fixed by
rambodolphin13, DividedRealms 428ccd7d). The exit by Legion's hall
and the one near the Midgard boss did nothing when clicked: the game client has them, the released world had no
destination for them. Each now takes you to your realm's usual Darkness Falls exit spot, like every other exit. Nothing
is written to your world: the server supplies the two missing destinations itself. Thank you, rambodolphin13.

**The raid gets out of Xanxicar's stomp** (owner's PC test, 2026-11-07). At the 80% stomp 110 of 185 Albion raiders died
in one second. The order to get clear came only 3% above the threshold, it reached only the raiders whose target was
Xanxicar himself (not those on his elites and crystals, nor healers), the spot it sent them to lay in the cave's rock,
and a bot's melee follow walked it straight back to him. Now the raid withdraws ten seconds of its own damage ahead of
each threshold (at least 6%), every raider near him goes whatever it is fighting, to the reachable spot on the cave floor
clear of the stomp nearest to it (never past him), without its melee follow, and waits out there until the stomp is over. The party he fights still holds him. His
stomp is unchanged. Container check (1.2 line, the same fix, 152 Albion raiders): Xanxicar killed with no wipe; his
80% stomp killed 5 (before: 71, 49 and 48 in three earlier runs).

**Scurceol Hyrde's orbs are shot down from real floor spots** (owner's PC soak, 2026-11-08). Seen once on the owner's
PC with the third release candidate (b72716e), 2026-11-09: a forced Albion raid of 200 shot the four orbs down in 10
minutes, each from its own floor spot (`approach=ranged-stand`, 1,053 and 1,279 from the orbs it logged), killed
Scurceol 50 seconds after the last, and lost nobody to him or his orbs. One raider stuck in Avalon City held the raid
at Caer Diogel until it was killed by hand and walked back (known issues). 1.1.2's tactic (the orbs first, from the floor) never got going on the real world. Three times the raid found
"no reachable floor spot within 1400 has line of sight to it" and set it aside. One orb of four died, Scurceol was never
killed, he killed 156 raiders, and the raid won Krondon by going around him.
- **Why** (from the real Krondon navigation mesh): each orb hangs about 50 above the top of its own rock perch. The
  perch rises out of a pit about 900 deep, 400-530 across from the platform where the raid and Scurceol stand. The
  sight check walked the mesh's floor to the spot under the orb, and a floor walk can't cross a pit. From the 277-321
  reachable spots within 1,400 of each orb, none passed. The 1.1.2 test used a pretend floor that always saw, so it
  could not fail.
- **Now:**
  - The search tries spots all around each orb (every 50 out to 1,400, every 10 degrees) on Scurceol's floor, at least
    400 from him, those beyond 700 from him first.
  - Sight is a line from a raider's eyes to the orb. No ledge, rock top or perch may be in the way, and the floor
    toward the orb must end at a drop, not at a wall.
  - Every orb gets its own spot. A caster or archer out of reach of its party's orb walks to that orb's spot first,
    then shoots. Before, only the raid's current orb had a spot, and the shooters given the other three walked under
    them.
  - Scurceol and his orbs are unchanged.
- **Tested:** automated tests on a recorded copy of the real Krondon mesh around the orbs. Every orb gets a spot the raid
  walks to, in range and clear of him. The 1.1.2 search finds none on the same copy, as on the PC. Container runs
  (test world, real meshes, a test-only way to open the raid; the 1.1.3 build before the chat fix plus this fix):
  - Albion, 80 needed (125% of Krondon's 64), 101-104 in the raid at the fight: all four orbs dead in 43 minutes,
    Scurceol 1.3 minutes later, 16 raiders killed by him, the tactic never set aside.
  - A second run, same build and size: all four orbs dead in 105 minutes (the first took 38), Scurceol 0.8 minutes
    later, 6 raiders killed by him, the tactic never set aside.
  - The stage before every orb had its own spot: stopped at the first orb, at half after 15 minutes, with 9-11 raiders
    on it.

**Raid fixes from the forced Krondon raid** (owner's PC, third release candidate b72716e, 2026-11-09: a forced Albion
raid from the launcher's Realm Events tab, 176 minutes from start to Orylle's death, 38 of them waiting at Caer Diogel).
- **A forced raid no longer waits forever for its last member.** It reserved 300 level 50s, but Caer Diogel had room
  for only 25 parties (13 of its 38 formation posts are on ground too tight), so 200 bots formed parties and the raid
  needed every one of them at the hub. It had 125 there at 01:30 and 199 from 01:45 on; the last one was stuck in
  Avalon City, and the raid left at 01:57 only after he was killed by hand. Now a forced raid waits 15 minutes for
  everyone, then goes with 90% (180 of 200), and after 30 minutes with half; the rest follow as late members, as in
  an automatic raid. The same applies at its staging outside the dungeon, counted from when it left the hub. When the
  hub has no post left, the raid needs only what its posts hold, and the other reserved bots go back to their own
  play: on the PC 100 of them waited three hours for a post that never came, kept out of every other group. Tests only.
- **A raider left behind in another dungeon walks out.** That straggler, Ferken, stood in Avalon City on "Holding before
  dungeon threat" from 01:16 to 01:55, waiting for his party's puller, who was at Caer Diogel. The wait is meant to end
  after 90 seconds, but its clock started again whenever another monster became the first one in his way. Now the
  clock runs for the whole wait whichever monster is in the way, nobody waits for a puller when no one of their party
  is in their region, and such a raider goes around the monster when the floor allows it (otherwise fights it) on its
  way to the party. Tests only.
- **The raid walks to its next pack on its own floor.** Inside Krondon several packs took 3-4 minutes to reach, with
  nobody within 1,000 of the target, and then died in 10-20 seconds. The raid took the nearest monster in a straight
  line, which in Krondon is often one on the floor 1,500 below: at 02:30 it went down to an atelic ooze, at 02:34 back up
  to a foreman, at 02:38 down to another ooze. Now a monster on another floor counts as eight times as far for each unit
  of height, so the raid clears the floor it stands on first. Tests only.
- **Parties clear the trash on their way to a muster.** While the raid gathers for a named encounter, a party may not
  start a fight, so each party stopped for 90 seconds before every monster on its walk there and then fought it anyway.
  The musters took 6 to 20 minutes (Ulor se Bysen: 13 of 100 after 10 minutes, in at 19 minutes, with 174 coin drops
  from monsters killed in Krondon in that time). Now the party's puller pulls an ordinary monster in the way at once, with the usual
  checks (health, a whole party, close together). An epic encounter in the way still waits for the raid. Tests only.
- **Nobody fights Orshom's Fire.** The fire pit Orshom Brong lights when his fight starts burns everything within 300
  every 3 seconds and can only be hurt by cold. The raid took it for an encounter that was fighting it, turned on it,
  and 100 raiders burned in it between 04:12 and 04:18; after five minutes without damage it was set aside. Now it is
  never the raid's target and no party post is placed in it. A raider with a cold spell shoots it from outside its
  radius, everyone else steps out of it and fights the twins, and anyone standing in it steps out first, whatever it is
  fighting. The fire and the twins are unchanged. Tests only.
- **Melee stays off Scurceol Hyrde's floating orbs.** 11 seconds after the raid turned to the orbs from a floor spot,
  it switched to another orb "fighting the raid" and made the orb's spot in the air its destination: 111 of the 172 route
  recoveries toward Lyft Miht that followed aimed into the air. Now an orb is fought only through the orbs' own plan,
  and a melee raider whose target is an orb it can't reach waits at the orbs' floor spot. Tests only.

**A day with a prime time and a quiet night** (owner's PC test copy, 2026-11-09). At 05:20 2,673 gamebots were online,
about 900 a realm, and Albion's /faction ran 323 lines an hour at 02:00 and 291 at 03:00, nearly the evening's 300-450.
The server log said `bot_schedule=off`: the Bot schedule on the launcher's Living Server tab was off on that copy, so
every bot stayed online all night. The rise seen that night from about 840 online to 2,673 was not the roster growing:
the server was restarted at 01:10, 04:27 and 05:25, and each time the startup ramp logged the whole roster back in over
15 minutes, about 180 bots a minute (844 four minutes after the 04:27 start, 2,718 at 04:42, the same ~2,715 each time).
With the schedule off the ramp's target is the whole roster whatever the hour. With "Real clock" on:
- **A deeper night.** The clock curve now keeps about an eighth of the roster before dawn on a weekday (about 330-370
  of 2,700 at 4-6 am; the old curve kept a fifth, about 560) and nearly all of it from 7 to 11 pm; weekends are busier
  all day.
- **No jumps.** Every bot's place in the day's line changed at midnight at once, so about 450 bots wanted to swap in the
  same minute; the places now move a little at a time between 2 and 5 am. The weekday and weekend curves stepped at
  Friday 6 pm and Monday midnight (about 400 bots at once); they now run smoothly into each other. The most bots the
  schedule sends home in any minute of a week is 26 (logouts run at up to 36 a minute).
- **RvR bots log off too.** A bot that had ever run its RvR goal could never log off or take a break: it keeps its force
  name for good, and that was read as "busy in a realm event". Now only a bot whose force is in a running event (a siege,
  a defense, a relic run) stays.
- **Finish, then leave.** A bot the schedule is sending home joins no new party and answers no automatic raid, and an
  automatic raid is sized to the level 50s who stay. A bot in a party, a raid or a siege still finishes it before logging
  off. After a start in the night the startup ramp spreads the schedule's number over its 15 minutes (it let the few
  hundred all in within two minutes).
- **The launcher** shows the Bot schedule first in the Server life card, with what Off and Real clock mean, and the
  status line always says whether it is on. Setup already turns Real clock on for a new install; the server's own
  default stays off.
- **Chat follows the crowd.** /faction's target and ceiling already scale with the bots online and the hour; with fewer
  bots at night the quiet hours are quiet. A simulated weekday (`UT_LivingServerDay`: 2,700 bots, a third in parties,
  the real schedule, login, logout and chat-budget rules, a model of one realm's /faction): with the schedule off,
  258-291 /faction lines an hour at 2-5 am against 407-427 at 7-11 pm (the PC's night); with Real clock, 2,532-2,620
  online at 7-9 pm and 371 at 4:30 am, no party member logged out, at most 21 logouts in a minute and 132 in ten
  minutes, and /faction 118-137 lines an hour at 3-6 am against 388-421 at 7-11 pm, every hour under its ceiling.
  Tests only.

**Defenders are called when the enemy army marches, so a defended keep often holds** (owner's PC, 1.1.3 release
candidate nights, 2026-11-08 and 11-09: keeps "flipping all over"). In about 15 hours of logs 18 bot sieges ended: 12 keeps
captured, 4 defended, 2 failed rallies.
- **Why the attackers won:** the keep's realm was told only when the battle began (`RVR_DEFENSE_MUSTER` in the same second
  as `RVR_SIEGE_STARTED`). Eight of the 12 keeps fell 4-11 minutes into the battle. The first defender reached the keep 3-14
  minutes after the start in 7 of the captures and never in the other 5 (`defendersFirstAt`); several were warbands breaking
  off an attack in another realm's frontier. Every siege with defenders at the keep within two minutes of the start was
  held (3 of 3). Committed defenders were not the limit: they were walking.
- **Now:** scouts see the army leave its border keep. The keep's realm is told at once ("Scouts report Albion's army
  marching on Dun Bolg!", `RVR_DEFENSE_CALLED`), its free level 50 warbands are called within seconds, gather at their
  muster for up to 6 minutes (10 when it is thin) and then man the walls. The march took 9-37 minutes in these logs, so
  the defense is usually in place when the attackers arrive. The third realm still joins only once the battle is on. A
  player's own attack and the retake after a player's capture keep their rules.
- **The siege balance aims at about half** (owner 2026-11-09). It used to steer towards attackers winning about two sieges
  in three (owner, 2026-10-19); with defenders arriving in time it would have pulled them away again until attackers won
  most. Now, of the last six sieges, four or more captured adds defenders, two or fewer takes them away, and three holds
  (thresholds 0.65 and 0.35, were 0.8 and 0.45). In a simulation over ten models of the attackers' chance (`UT_SiegeBalanceNov06`)
  it settles at 44-56% captured, 50% on average; the old thresholds settled at 53-61%. In the logs it sat at its limit
  all night (share 1.0, attackers 96) and still lost 12 of 16 finished sieges.
- **Battleground keeps** do change hands, rarely: Dun Murdaigean went from unclaimed to Albion between 04:34 and 19:32 on
  11/08 (outside the logs). In the 15 logged hours no battleground lord fell below 85% and at most 7 bots were on one.
  Not changed here.
- Tests only.

**Gamebots in their capital go to their own frontier for the porter** (owner's PC, 1.1.3 nights). 779 times a gamebot
stood 15 minutes "Waiting for a frontier teleporter" and the watchdog moved it on (Tir na Nog 404, Camelot 227, Jordheim
148). Each stood on its capital's bind after a release, sent to a keep in another realm's frontier (Mjollner Faste 118
times, Glenlock Faste 66, Dun Bolg 59 and more). Only its own frontier's porter goes there, and that porter is used only
from inside the frontier, so it waited in a city with no porter, and the watchdog put it back on the same spot (recovery counts up to 24). Now such a bot first travels to its realm's border keep (Castle Sauvage, Svasud Faste, Druim Ligen), where the
medallion merchant and the porter are, and rides from there. Tests only.

**A camp search that holds a bot for minutes is reset** (owner's PC, 1.1.3 nights). 464 times a gamebot stood 15 minutes
"Searching for a valid XP camp", nearly all level 50s on their capital's bind (Tir na Nog most). In the 11/09 log 72 of 91
went on to a camp only after their next login, from the same spot, so what held them was in the bot's own planning
memory, not the world; which part is not shown by these logs. After 3 minutes of failed searches a bot now drops its own
death caution and failed-camp memories as a fresh login does, and logs what it held (`AUTONOMOUS_CAMP_SEARCH_RESET`), so
the next night shows the cause. Tests only.

**Fewer server stalls on a busy night** (owner's PC: AI profile of 2026-11-07/08 and the 1.1.3 nights; 17 stalls over a
second in the server logs, 3 of 4-6 seconds in the night before).
- **The 4.5-6.1 s stalls** were Krondon raids: the raid pulse took 4,520-6,140 ms in the same second as each
  "no reachable floor spot within 1400" for Scurceol's orbs (05:38, 05:54, 06:19 on 11/08). That build's search
  checked 277-321 reachable floor spots per orb; 1.1.3's new orb search (above) tries at most 8 walks per orb. Not changed
  again here, and not yet timed on a real install.
- **One bot's AI turn of 2.0-2.2 s** (3 of the 17): a Sluaghbinder searching a dampwood mite camp proved one target's
  route after another (1,906-1,970 path queries and 1,013-1,060 corridor checks in one turn). A camp search now proves
  at most 4 routes and casts at most 24 dungeon sight lines; the rest wait for the next search a second later, and a
  target whose route failed is skipped for a minute. In a test on a mesh with no routes (79 queries per failed proof),
  one search over 200 targets makes 316 queries instead of 15,800.
- **Group formation passes of 0.3-1.3 s** (46 times in 800 one-minute profile samples, 10 of them over 0.7 s), held under the lock every grouped bot's turn waits on
  (that lock's waits were 16% of all AI time). Nearly all of it was rendezvous route searches (up to 390 ms each). A pass
  now stops once it has spent 150 ms after its first leader and goes on 15 seconds later.
- **Not changed:** 12 of the 17 stalls also had a 443-687 ms garbage collection pause in them (heap about 2 GB).
- Tests only.

**Bots still don't fall when they die** (looked at again for 1.1.3; nothing shipped). The left-out fix (23c88a12, Beta 1.1)
sent the player-death packet for a bot; the client plays that pose only for player characters, so in game it did nothing.
The other server-side ways all break something:
- The only death animation the client plays for a non-player is on the remove-object packet for a dead NPC (how monsters
  die). The client then deletes the object: the corpse can't be targeted, so it can't be rezzed, and the group window
  loses it until the bot is sent again, standing.
- The NPC create and update packets have no dead or lying-down flag (statue only freezes the model), and a 0% health
  update leaves the model standing, as the bots do now.
- A separate corpse prop would need a client model and new client art IDs, and still couldn't be the rez target.
- Tests only (nothing in game changed).

## Beta 1.1.2 — 2026-11-07

Chat: a much busier realm, and bots that answer what you actually said. Raids the size they were in the era, sized
by their own wins and losses, that no longer stand for hours at an encounter they can't reach, and their first boss tactics. Playing with your bot
group: dragon raids that regroup after a wipe and call the Epic Special Forces, quest credit and hand-ins, and quest XP that follows your
XP-by-level rate. Gamebots in your guild earn merit and pay dues, power comes back faster, fixes from other forks of
Offline DAoC, updates from the launcher, and a world database that opens in a crash-safe mode and is backed up every
day.
How it was tested, and what hasn't been played yet, is in the release notes
([docs/RELEASE-NOTES-1.1.2.md](docs/RELEASE-NOTES-1.1.2.md)).

**Epic raids learn their first encounter rules** (owner 2026-11-06: shipped in 1.1.2, not 1.2). Raids of gamebots now
know five bosses that beat them by their rules, not by their damage. At release all of this had tests only. Seen since
on a real install, in bot raids on a later development build: Black Lady was fought at home and killed; Scurceol's orbs and Xanxicar's stomp did
not work (below).
- **Scurceol Hyrde (Krondon).**
  - **Before:** he takes no damage while his four floating Lyft Miht orbs live, but the raid hit only him, for an hour, and
    all 42 raider deaths were his.
  - **Now:** while an orb lives the raid's target is an orb. Each party takes one; casters and archers shoot it from the
    floor, and melee wait under it, away from him. Nobody hits Scurceol until the last orb is down, then the whole raid
    turns on him. Orbs stay dead if he resets.
  - The raid also fights him in his own room: past 1,500 from his spawn it stops hitting him, and the raider he chases
    leads him back.
  - **On a real install this did not work** (a later development build with the same orbs code). The raid found no floor spot that sees most of the orbs. It killed 1 of
    the 4, set Scurceol aside three times and took Krondon by going around him. Being fixed.
- **Black Lady and the Ancient Black Oak (Marfach Caverns).**
  - **Before:** the raid dragged Black Lady out of her room until she went home at full health, then pulled her Oak 91
    times without ever reaching it.
  - **Now:** the Oak dies when Black Lady dies, so the raid fights her instead. Past 1,100 from her spawn (her leash is
    1,500) the raid stops hitting her and leads her back, so she never resets mid-fight.
- **Original boss mechanics are back, switched off.** In 23 places the fork had changed a boss for bot raids; 22 of
  them have their original back. Examples:
  Aroon's damage-type order, Funus healing from the wrong weapons, the Earthmenders' class puzzle, Beatha healing Xaga,
  King Tuscar and Queen Kula healing each other, Kvasir healed by cold, Govannon's full 300,000 health. The originals,
  read from OpenDAoC's own code, are now behind 17 new switches in `EpicEncounterMechanics`, all off, so bosses play
  exactly as before. Each one gets turned on once the raid knows how to beat it.
- **Where to look.** The encounter catalog
  ([docs/encounters/EPIC-ENCOUNTERS.md](docs/encounters/EPIC-ENCOUNTERS.md)) has three new lists: encounters the raid
  has tactics for, the restored mechanics and their switches, and the mechanics that still only pick real players. The
  server log shows each decision as `REALM_EXPEDITION_TACTIC` (the raid's target) and `EPIC_TACTIC_BOT` (a raider's).
- **Raids answer a boss that jumps them before the muster.** Before, whoever was nearby fought it, and the rest arrived
  in twos and threes. Now the whole raid answers together:
  - With half the raid already near, or with a boss that would chase them down, everyone goes in at once.
  - A boss also chases them down when the muster is inside its leash. On 2026-11-06 Fuladl (Krondon) could chase raiders
    up to 3,800 from his spawn, and his muster was 1,400 from it. Then the raid goes in at once too. Most catalog bosses
    have a leash longer than the 1,400 a muster stands off, Aidon's 1,500 included, so most early pulls now commit.
  - Otherwise everyone pulls back to the muster, the boss walks home and resets, and the raid comes back together.
  - Each decision is in the log as `REALM_EXPEDITION_EARLY_PULL`.
- **A lost skirmish no longer counts as a wipe.** A failed fight now uses up one of the raid's two attempts (and calls
  the Special Forces) only if at least half the raid was in it, or half the raid fell. Example: on 2026-11-05 Aidon
  killed 31 raiders while 56 were elsewhere, and that counted as the raid's first wipe. It would now be a lost skirmish
  (`REALM_EXPEDITION_SKIRMISH_LOST`): the raid rests and musters again without losing an attempt.
- **Musters wait where the boss lives.** A boss that was out chasing raiders is mustered for at its home, and the
  muster moves when the boss walks back (`REALM_EXPEDITION_MUSTER_MOVED`). On 2026-11-05 Aidon's muster sat on the ramp
  his chase had reached, 2,600 from his room.
- **Pulls go to where the boss stands.** The spot the raid walks to when it goes in (the front) is worked out again when
  a boss that is not fighting stands more than 1,000 from it (`REALM_EXPEDITION_FRONT_REFRESHED`). Pulls that went to
  the old spot no longer count toward setting the boss aside. On 2026-11-05 three full pulls on Aidon (75 and then 106
  raiders) walked to the spot from his earlier chase and stopped 1,979 from him. He never entered combat and was set
  aside (`REALM_EXPEDITION_PULL_UNREACHED`, which now also logs `frontDistance`). If no new route to the boss is found,
  the old spot is kept (in Darkness Falls the last waypoint of a route is the spot on purpose).
- **Floating required adds are shot from the floor (Scurceol Hyrde, Krondon).** On 2026-11-06 the raid turned to
  Scurceol's four Lyft Miht orbs twice, but no route to them was found and each was set aside. They float 230-580 above
  him, and the route only looks for a path to the spot it fights at. The raid went to Fuladl and never fought him. Now
  the route never walks to a required add:
  - The raid fights it from a floor spot it can walk to: under the add, beside its boss, or in between.
  - The spot must be within 1,400 of the add and see it.
  - Raiders who can't hit the add in the air wait at that spot.
  - Only when no add has such a spot is the boss set aside, with the reason for each add (`REALM_EXPEDITION_TACTIC_ASIDE`).
  - On a real install the raid found no such spot for Scurceol's orbs (above). Its test uses a flat test floor, not
    Krondon's.
- **Aidon the Archwizard (Hall of the Corrupt).** The raid kills three of his four illusions and leaves the last one
  alive, because he only summons a new set once all four are dead. Then everyone fights him, near his room. His damage
  shield only hurts melee attackers; it doesn't make him harder to damage, so he can be beaten by design.
- **Nosdoden (Trollheim).** When a wave of essences comes (every 10% of his health), the raid stops hitting him and
  kills the wave first. His health then drops one step at a time and waves don't pile up.
- **Xanxicar (Crystal Cave).** Before 80/60/40/20% and until his stomp has landed, everyone except the party he is
  fighting moves 2,700 away. That party pushes him over and takes the 2,500-radius stomp, which is meant to kill whoever
  is in it; the rest of the raid survives and comes back. **This did not work:** on the owner's PC (2026-11-07) the
  first stomp killed 110 of 185 raiders. Fixed in 1.1.3.
- Not changed: Darkness Falls raid AI, Legion and the hardest level 70+ encounters still have no raid AI.

**Chat: a busier, more human realm** (owner 2026-11-04: shipped in 1.1.2, not as a separate 1.1.3). The goal is a
realm that feels like a full server at its busiest. How it works and how to tune it:
[docs/bot-voices/CHAT-BUDGETS.md](docs/bot-voices/CHAT-BUDGETS.md).
- **More chat, scaled to the crowd.**
  - **Before:** chat stopped growing at about 390 bots a realm.
  - **Now:** /faction, /lfg, /trade, /say near you, town /broadcast, guild and group talk each have a floor, a target and
    a ceiling. They rise with the bots online, with no cap, and follow the time of day.
  - **Town /broadcast:** bots now use it the way players did in 2003: selling (sometimes as a repeating macro), looking
    for a group, a little chatter. You hear it where the base game's /broadcast reaches.
  - **One file:** all the numbers are in `chat-budget.json`, with comments. Research into 2003 chat gives the shape
    (which channels, places and hours, sellers' macros, bursts). The levels are set for a busy peak server, on purpose
    busier than the 2003 estimates. Nights are calmer, never dead.
  - **Turn it up:** the launcher's Living Server tab has a new **How much they talk** setting (0–3). 1 is a busy peak
    server, 2–3 very busy.
- **Big moments ripple through chat.** A keep taken or lost, a relic moved, a raid won or wiped, Darkness Falls changing
  hands, a server first and a nemesis paid back each draw a short burst of reactions in /faction. The realm-wide ones also
  reach the capital's /broadcast.
- **Bots answer what you said.**
  - **Weak replies:** a reply that doesn't fit your line is dropped for something short that does. A tell gets a reply
    to what it says ("ty", "not selling sry", "nah im good ty", "no heals on a thane sry", "cool"); only "hi" or "u
    there?" gets "?" or "ya?". Banter and bad luck get a "lol" or "ugh" back now and then, most in a group, and a
    question nobody knows gets one "dunno" in a group or guild. No more "ask around maybe" to a question anyone could
    answer.
  - **Silence where it belongs:** spam and scams (links, real-money prices, fake GMs, "your account has been flagged",
    account sales, doubling offers, free plat, password requests) and lines said to someone else by name get no answer.
  - **Honest answers:** "spec?", "lvl?", "what weapon is that?" by tell get the bot's real spec, level and weapon. A call
    gets "where?" or "omw", a cry for heals the truth for the bot's class, a "45+" raid "too low lol" from a level 30,
    and a thane never says "np" to thanks for heals.
  - **/say to the bot in front of you:** a /say line counts as said to a bot when you target it, answer its line, or
    stand right in front of it with nobody else close.
  - **More places to ask:** bots answer `/advice` when one of them knows, and questions in `/broadcast` and chat groups
    by tell.
  - **About themselves:** bots answer their name, class, spec, level, zone, group and what they're hunting from their
    real state.
  - **How things work:** they explain equipping, binding, `/stuck`, `/anon`, `/release`, training, and how many realm
    points a realm rank takes.
  - **By situation:** "gn" gets a goodnight, "stuck" gets `/stuck`, "inc" gets "where?" or "omw", a call for help an offer
    to come, a sale a price or a no, and a counter-offer to a bot's price a counter or a deal.
  - **Words they now read:** "lo", "re", "wb", "long time no see", "u there?", "ne1 on?", mishaps ("lol rip"),
    apologies, offers of help, "where?" after a bot named a place, "thx mythic" (sarcasm, not thanks), "free rps at…"
    (a rally), "loot the named" and "fake inc" ("k"). A call that already says where and how many gets "omw", not "how
    many?". `/stuck` is for you, not your pet.
  - **Roleplay in kind:** a challenge gets a challenge, a toast a toast, a mourning a bow, otherwise an emote.
- **One answer, then maybe a few more.** A question gets one answer. A hello or a ding gets a few. A joke now and then
  draws a "lol" or "+1". Replies take longer to type the longer they are, at each bot's own typing speed.
- **Less filler.**
  - **Chat about the game in front of them:** most unprompted lines are now short and real: "lfg 38 zerk", "wts 97%
    chain hauberk /send", "anyone up for vendo?", "inc beno", "who has rena?", from the bot's real level, class, zone and
    bags and the realm's real keeps and relics: camps for its level, "lf a healer for keltoi", class questions, rallies
    while a keep is under attack, and how many keeps the realm holds. Guild chat too: group calls, raid and keep plans, "gz" to a guildmate
    who really dinged, a crafter wanted, an old item offered, "brb" and "night all". The same item isn't advertised all
    evening, and short answers vary.
  - **Regulars:** each realm and guild has a few talkative regulars for the evening who carry much of the chat, answer
    each other and follow up their own lines; one thought isn't reworded by someone else minutes later, and chat comes
    in runs and lulls rather than evenly.
  - **Threads and stories:** a question usually gets an answer, and some run on for a few lines between two or three
    bots. A ding to 50, a death or a group's wipe becomes a short story others riff on. Help questions come in many
    shapes, and guild chat hears at most one weather-or-school line an evening. Answers take typing time and give what
    was asked ("where" gets a place, "how much" a price).
  - **Musings and quips:** lore musings, polished jokes, tips and life stories appear only rarely in /faction, never as
    replies.
  - **Generic agreement:** "good question", "very true" and the like are gone.
  - **Repeats and polish:** near-copies are dropped, the wordings of one line rest for hours once one is said, and most
    bots type in lowercase. Typos are rarer and look typed (a dropped letter).
  - **The Realm Exchange:** bots mention it less often. On a real install they still advertised it unprompted.
  - **Templates:** a few grammar slips are fixed.
- **Typing habits.** About a third of bots have small habits of their own: "u" and "ur", "??", "...", a trailing "lol",
  and for a rare few, the period's leetspeak.
- **`/lswhy`** (every player) shows why a bot said what it said, or why nobody answered, and `/lswhy budget` shows how
  busy each channel around you is.
- **Checked:**
  - **Automated:** new tests for every part; the full server suite passes.
  - **Simulated evening at 1,000 bots a realm:** about 12.7 lines a minute across the channels a player hears, up from
    about 1.9, with /faction above its busy-peak target of 4 a minute. At 300 bots it's about 5.9, up from 1.2.
  - **The blind test:** 400 player lines, read one at a time, not conversations. The bots read 53% of them as
    intended; re-running the same lines gives about 70%, and about a quarter of them now get no reply. Those lines are
    known, so this is a regression check. A blind judge still picked out most bot lines (72% in the first round, 85% in
    the latest). Conversations over several turns have not been tested.
  - **On a real install** (the 1.1.3 build, about 600 bots a realm, an evening): /faction ran at about 14 lines a minute
    for ten minutes, with peaks of 20, above its ceiling, and reactions came in bursts of 10 to 16 lines. A player heard
    about 15 to 19 lines a minute in all, more than the simulation predicts. Being fixed.

**Raids the size they were in the era, and raids that keep winning bring fewer bots** (2026-11-04)
- The goal for raids is "mostly successful, high stakes, never a guarantee": about two wins in three. **The bosses are
  never weakened**; only the raid's own size and its help change.
- **Period sizes.** Every dragon, epic dungeon, Summoner's Hall and Darkness Falls raid needed 200 level 50s and
  recruited up to 300. Each raid is now measured against the size a comfortable raid had there in 2002-2004, from forum
  raid threads of the time ([docs/RAID-SIZES.md](docs/RAID-SIZES.md) has the table and its sources), and starts at a
  quarter above it, because bots play worse than 2003 players (the first night on the period sizes won 1 raid in 7):
  - **Caer Sidi, Galladoria and Tuscaran Glacier:** 125 (period size 100).
  - **Crystal Cave:** 150 (120).
  - **Dragons:** 88 (70).
  - **Summoner's Hall, Darkness Falls, Krondon, Avalon City, Trollheim, Iarnvidiur's Lair and Marfach:** 80 (64).
  - **Hall of the Corrupt** 60 (48), **Dodens Gruva and Tur Suil** 50 (40).
  - **Recruiting:** a raid gathers about a quarter more than it needs (Caer Sidi up to 160).
- **Sizing by results.** Every automatic raid, dragons too, is sized from that encounter's own record in the Herald.
  - **How it moves:** each win trims the next raid there by 10% of its period size; each defeat or time-out adds 20%
    back. Two wins and a defeat leave it about where it was, so the size settles where the encounter is won about two
    times in three. Four defeats take a raid from its start to its largest size. The Herald notes each raid's size
    ("Raid size: 145.3%."), and the next raid there carries on from it.
  - **The limits:** it goes from 70% of the period size (Caer Sidi 70, a dragon 49) up to twice the period size for
    raids that keep losing: Caer Sidi, Galladoria and Tuscaran Glacier 200, dragons 140, Summoner's Hall and Darkness
    Falls 128 (recruiting up to 256, 176 and 160). Never past 300.
  - **What counts:** raids cut short by a restart, beaten to the boss by another realm, failed rallies and stopped
    raids don't count.
- **After a restart** no automatic raid opens until the realm's bots have finished logging in. Before, a raid could
  open two minutes after a restart and be sized from the few already online (Tuscaran Glacier needed 58, not 100).
- **Short starts.** A raid out of time still goes in with half of what it needs, now at least 24 (three parties)
  instead of 40, so a small raid isn't held to nearly its full size.
- **The Epic Special Forces come with 24.** If they helped win at an encounter, they come with 16 the next time a raid
  there calls them, then 8; they always answer a wipe with at least 8. A win without them changes nothing, and each
  defeat or time-out there adds 8 back, up to 24. Two defeats in a row still bring them from the start. Before, every
  win cut them by 8, whether they had been there or not, and three wins in a row meant nobody came. The Herald now
  notes "Epic Special Forces joined: N." on a raid they joined; older records count as raids without them.
- **Parties that answer on their own** now stop at the raid's recruit limit too. Before, they could fill any automatic
  raid to 300, so a trimmed raid would have grown back to full size by itself.
- **The Special Forces** follow the encounter's whole record of wins and losses; before, their last six rows could be
  restarts or failed rallies that pushed real results out.
- Raids you start from the launcher keep their 200 and 300, and their wins and losses count for neither the raid's size
  nor the Special Forces: the Herald now marks them ("Forced raid: ..."), and older unmarked records count as
  automatic. `realm_raid_minimum_bots`, when set, is used as it stands.
- **Settings:** `realm_raid_target_win_percent` (67; 0 turns the sizing off), `realm_raid_smallest_size_percent` (70),
  `realm_raid_largest_size_percent` (200), `realm_raid_start_size_percent` (125) and `realm_raid_win_trim_percent` (10).
  A world that ran an earlier test build of this change moves to these defaults once (step 5 to 10, largest 100 or 150
  to 200, smallest 60 to 70), unless you changed them yourself. The `REALM_RAID_SIZED` log line now ends with the
  rules in force (`rules="start=125% range=70-200% win=-10 loss=+20.3 target=67%"`).
- **Seeing it:** `/lstest raid` names the size and why, and the server log has a `REALM_RAID_SIZED` line for every
  automatic raid (period size, size, required, recruit limit, wins and losses, the Special Forces it would get, and why);
  `REALM_RAID_SCHEDULED` now shows the size too.
- **Keep sieges:** the siege balance that moves the defenders' share by up to 0.25 forgot everything at every restart,
  and needed three sieges in one session before it moved at all. It is now rebuilt from the Herald's last bot sieges at
  start (sieges answering a player's attack are left out), and every change is logged (`SIEGE_DEFENSE_BALANCE`).
- Checked: unit tests (`UT_RealmRaidWinBalance`), including a simulation of 2,000 raids against simple models of the
  chance to win by raid size (bots as good as 2003 players, and worse), which settles at 67-70% wins, including an
  encounter that needs 140% of its period size; only one that needs nearly twice it wins about 6 in 10. The models are guesses, not measurements;
  the first night on the period sizes (before the 125% start) won 1 raid in 7. The new sizes are not played in game yet.

**A raid no longer pulls an encounter it can't reach for hours; sieges log their army** (2026-11-05)
- **Marfach Caverns:** Hibernia's raid killed Avartack, drove Black Lady to 44% and then turned on her Ancient Black
  Oak. It stood at its muster for three and a half hours: it went in 91 times, never got within 1,000 units of the Oak,
  and timed out with one named kill. Two causes:
  - **No formation posts:** a raid that turns on a monster attacking it took that monster's own spot as its front, and
    no formation post fitted around the Oak's spot. It now fights from a walkable approach checked from the raid's
    positions, as it does every other target.
  - **No give-up rule:** an encounter that is still out of combat after three pulls in a row is now set aside for 15
    minutes, and the raid goes on to its other encounters (`REALM_EXPEDITION_PULL_UNREACHED`).
  - **Unchanged:** a wipe is still a failed attempt.
- **Krondon:** the Albion raid mustered for Beran the Supply Master for over an hour with 2-5 of the 50 it needed.
  The rest of the raid was fighting Scurceol Hyrde, who can't be hurt while his four orbs live and killed 42 of them.
  The raid kept turning back to him every 15 minutes, and each time it came back to Beran the muster clock started
  again. Now a muster that hasn't gained a raider in 20 minutes, and still needs more, is set aside for 15 minutes and
  the raid goes on to another encounter; the clock carries over when the raid turns away and back
  (`REALM_EXPEDITION_MUSTER_STALLED`). The raid's hold line now lists every encounter still alive in the dungeon and
  what is holding the raid, so the next run can show why the orbs were never attacked. Raiders stuck fighting an
  encounter they can't hurt aren't fixed yet.
- **A time-out without a wipe is no longer a loss** (owner, 2026-11-05). A raid that runs out its four hours without
  ever wiping now counts as neither a win nor a loss: the next raid there is the same size, and the Special Forces
  call-up doesn't change. A time-out after one or more wipes is still a loss. The Herald still says "Timed out" and
  now ends each raid's record with its wipes ("Wipes: 0."). Older records don't have that note, so their time-outs
  still count as losses, including the overnight Marfach raid's.
- **Night sieges:** 2 of 11 overnight sieges were captured, against 4 of 5 by day. This was not fewer bots online:
  580-600 level 50s per realm were online all night, and the login schedule was off. What lost were small armies: keep
  sieges that marched with 49 or more won all 4, those with 9-45 won 2 of 11. Nothing in the logs shows why the night
  musters were thinner, so the siege rules are unchanged. `RVR_SIEGE_MARCH` now logs the army assigned, the realm's
  level 50s online and on RvR duty, its other sieges and the defense share, and `RVR_EVENT_ENDED` logs the armies on
  each side. Both now name the kind of attack and say whether a siege marched short of its muster (`thin=true`), so
  the planned follow-up's deliberate small strikes on towers and lightly held keeps will never be mistaken for one. The next night run will show the cause ([docs/NIGHT-SIEGES-AND-MARFACH.md](docs/NIGHT-SIEGES-AND-MARFACH.md)).
- Checked: unit tests (`UT_MarfachAndNightSieges`); the Marfach approach probe needs the installed navmeshes and has not
  run. Not played in game.

**Keep sieges: when attackers keep winning, fewer of them come** (2026-11-06)
- **What happened:** the next night, attackers took 13 of 17 keep sieges. The siege balance had already given
  defenders all it could (one defender allowed per attacker), but that limit was rarely what held the defense back:
  - **Defenders:** in most lost keeps the realm sent 35-90, all it had free.
  - **Attackers:** reinforcements filled to the full 128 in 10 of 17 battles.
  - **Fast losses:** five keeps fell in 9-13 minutes, before their defenders arrived.
- **Change:** once the defenders' share is at its limit and attackers still take more than 5 of the last 6 keeps, the
  balance now lowers the attackers' limit. It drops one step at a time, from 128 to 112 to 96 (240 to 210 to 180 for a
  relic keep). When defenders start winning again, the attackers' limit comes back first, and then the defenders'
  share falls as before.
- **What stays the same:**
  - the muster still waits for 64 (120 for a relic keep);
  - the third realm still sends up to 64;
  - forced sieges, retakes after your own capture, and the defense of a keep you attack keep their sizes.
- **New log lines:**
  - `RVR_SIEGE_PRESENCE`, every 5 minutes of a battle, shows who is actually at the keep beside who is committed.
  - `RVR_EVENT_ENDED` adds the battle's length, who was at the keep at the end and at most, when the first defender
    arrived (`defendersFirstAt`), and the attackers' limit.
  - `SIEGE_DEFENSE_BALANCE` shows the attackers' limit.
  - Details: [docs/NIGHT-SIEGES-AND-MARFACH.md](docs/NIGHT-SIEGES-AND-MARFACH.md), section 4.
- **Checked:** unit tests (`UT_SiegeBalanceNov06`). In a night's run of a later development build on a real install,
  the attackers' limit sat at its lowest all night and attackers still took 8 of 11 keeps. It did not bring captures
  near half.

**Dragon raids call the Epic Special Forces after a wipe** (2026-10-30)
- Dragon raids had no wipe rule at all: after a wipe at the dragon the fallen released to the nearest bind and walked
  back by twos and threes to a dragon at full health, until it died or the four hours ran out, and the Special Forces
  were never called. A dragon raid now keeps the dungeon raids' rules. An attempt begins when the raid goes in from its
  staging ring and ends when the dragon is out of combat after the three-minute approach, or when it has taken no
  damage for five minutes while a trickle keeps it busy; the attempt was a wipe when half the raiders in it fell, or
  the dragon healed back to 95% after being taken to 85% or lower. The raid then regroups at its staging ring, outside
  the dragon's aggro, by the checked route around the lair: the fallen walk back, the living rest and rebuff for at
  least five minutes, and the raid goes in again once 70% of its roster stands there (40% after 20 minutes) and the
  dragon has landed. After two failed attempts the raid is **Defeated** and goes home, and every realm hears it. The
  lair radius, the 600 damage cap and the landing rule are unchanged.
- Review fixes (2026-10-31): who fell is remembered for the whole attempt. A dragon raider releases to a bind in the
  dragon's own region and is on its feet again seconds later, so judging the wipe from the corpses at the moment the
  dragon left combat would have called most wipes a break-off and left the raid trickling back as before. And a
  regroup nobody has reached for twenty minutes (the dungeon muster's rule) is given up and the raid is Defeated, so a
  forced dragon raid, which has no four-hour window, cannot hold at an empty ring until the player presses Stop
  (`REALM_RAID_DRAGON_REGROUP_UNREACHABLE`).
- On the first wipe the realm hears that the raid fell, and the Epic Special Forces are asked for on the same terms as
  at a dungeon: up to 24, one party fewer per win and one more per loss over that dragon's last six results in the
  Herald, nobody after three kills in a row. They arrive at the raid's muster hub and walk to the staging ring like any
  late party. A dragon beaten twice in a row (defeats and time-outs) brings them from the start, as a dungeon does.
- The launcher's Realm Events tab shows "RAID — regrouping at the staging ring after a wipe (staged/needed, attempt 2 of
  2)". Logs: `REALM_RAID_WIPE` now for dragons too, `REALM_RAID_DRAGON_WIPE` (engaged, fallen, health, how),
  `REALM_RAID_DRAGON_REGROUP`, `REALM_RAID_DRAGON_ENGAGE` (the retry) and `REALM_RAID_DRAGON_BROKE_OFF` (an attempt
  that ended with most of the raid standing). Automated tests (`UT_DragonRaidWipes`). Seen since on a real install (a
  later development build): a dragon raid wiped, the Special Forces came with 24 and the raid regrouped. Its second try hasn't been seen yet.

**Quests with your bot group** (2026-10-30, review fixes 2026-10-31; [docs/QUESTING.md](docs/QUESTING.md))
- A kill by the gamebots in your group counts for your quests. Before, only a kill you damaged yourself, or your own
  `/spawn` companion made, gave quest credit, so a player who healed, nuked late or only tagged while the invited bots
  made the kill got nothing and no classic step drop. The group's leader is now credited as a companion's owner is,
  when the bot still fights in the leader's group and the leader stands alive in the same region within the group's
  kill-credit range (6,000 units); who gets the experience, the loot and the coin does not change.
- A kill advances a quest step once. The killer's own quests heard the same kill once more for every groupmate in
  range who hadn't attacked, so a quest with consecutive kill steps jumped two or three steps on one kill. The quest
  now remembers the last kill it counted (two seconds), which also keeps the forced credit that nine named monsters
  (the dragons, Legion, Olcasgean, Myrddraxis, Xanxicar, Nosdoden, King Tuscar) give to everyone in sight.
- A hand-in is accepted above the quest's maximum level: a player who outlevelled a quest they already hold could not
  hand its item in while the kill, talk and whisper steps still worked. The level window stays where a quest is offered.
- More than 25 quests at login: a save with more than the client's 25 active quests kept them all and said so at
  login, instead of losing the ones past the limit; finishing one brings the next hidden quest into the journal.
- Whispering a keyword to a quest NPC twice quickly works: the silent 1.5-second pause between whispers now applies
  to whispers to players only, so two quick keyword clicks both arrive.
- Automated tests only (`UT_QuestKillCredit`, `UT_QuestLevelWindow`, `UT_QuestJournal`, `UT_WhisperThrottle`); not
  played in game yet. The in-game check for the owner: a kill quest as a normal account in a group of invited bots.

**Quest XP follows your XP-by-level rate** (2026-10-30, review fixes 2026-10-31)
- Quest XP follows your XP-by-level rate. When `xp_rate_by_level` covers your level, the experience from quests,
  bounties and the daily, weekly and battleground quests is multiplied by that level's rate (quest XP used to skip
  every rate); the 0.36 "share of your level" amounts stay the base, one payment never pays more than one level, and
  the offer window shows what you will actually get. New server setting `quest_xp_by_level`, on by default. It is a
  kill switch: off, the rate and the one-level cap are both gone and quests and bounties pay exactly as before
  (2026-10-31). With it on, a bounty's `xp_rate` is replaced by the range rate inside a range and a bounty pays at
  most one level, where the launcher's 5× and 10× presets used to pay several. The three Shrouded Isles quests
  Ancestral Secrets, The Lost Seed and Lost Stone of Arawn and the Wolf Pelt Cloak payments, which paid outside
  every rate, now pay like every other quest. Details in [docs/XP-BY-LEVEL-RANGE.md](docs/XP-BY-LEVEL-RANGE.md).
  Automated tests only; not played in game yet.

**Gamebots in your guild earn merit and pay dues, like players** (from a r/daoc report; owner's choice 2026-11-03: "Merit + dues")
- **The problem:** a gamebot you `/gc invite` joined your guild in name only. Its levels and realm ranks earned the guild
  nothing and it paid no dues, so a guild of level-50 bots earned almost no merit.
- **Now:** a gamebot in your guild earns it merit points for each level it gains and each realm level it reaches, with
  the player formulas (`GuildEvents.LevelUp` and `GuildEvents.RealmRankUp`), and pays the dues rate your guild leader set
  from the real coin it loots into the guild bank. If the bank is full the coin stays with the bot, so nothing is created
  or lost. Guild realm points, keep claims and guild buffs still never count a bot. `/spawn` companions never join guilds.
- **Bot guilds too:** bot guild members now earn realm-rank merit as well, and every gamebot pays dues on group-split and
  dragon-raid coin, not only on coin it picks up alone (that was missing for bot guilds as well).
- **What you see:** at most one line per guild every five minutes, such as "Seraorith has earned 69 merit points for
  your guild!", or "3 guildmates have earned ..." when several did. Your own merit keeps its usual line.
- **Setting:** `bot_player_guild_economy`, on by default, works whether bot guilds are on or off: the launcher's Living
  Server tab, **Bots in your guild**.
- **Checked:** unit tests for the dues split, merit for levels and realm ranks, the full bank, the switch off and the
  merit lines. Not yet seen in game.

**Faster power regeneration by default, the same rule for bots and players** (from a r/daoc report; owner's choice
2026-11-03: "Faster by default")
- **The problem:** power came back slowly. The server uses the pre-1.87 rate (about 12 a tick at level 50; patch 1.87
  doubled it to about 23), and list casters below half power got half of that. Gamebots and companions were exempt
  from the halving, so the player's own caster recovered slower than the bots around him.
- **Now:** the rate is 1.5 times the classic one (`mana_regen_amount_modifier` 1.5, a middle ground between the classic
  rate and 1.87's doubling), and the halving below 50% is off (`mana_regen_amount_halved_below_50_percent` False). When
  you switch the halving on, it applies to players, gamebots and companions alike. A bot's out-of-combat fast rest is
  unchanged.
- **Existing worlds:** on the first start of this build, a world whose two settings are still the untouched old defaults
  (1 and True, written by the server itself) gets the new ones once. A value you set yourself, or saved from the
  launcher, is kept. The server log says what changed.
- **Setting:** the launcher's Living Server tab, **Power regeneration**: the rate (0.25 to 10) and the halving. For the
  classic regeneration choose 1 and switch the halving on.
- **Checked:** unit tests for bot and player parity, the halving on and off, the rate, and the settings upgrade. Not
  yet played.

**From other forks of Offline DAoC** (chosen by the fork survey of 2026-11-03; each entry credits its author)
- **Gamebots in your group who die in a battleground come back to you** (issue #39; from Cancain's fork, by Cancain).
  - **The bug:** battlegrounds have no bind points, so a gamebot in a player's group released to its capital and stood
    there for good: its way back did nothing while it and its leader were in different regions.
  - **Now:** it releases to its realm's portal keep (under the battleground's level cap), walks back on a planned route,
    waits while you are dead, and rejoins the group on arrival. Solo gamebots and bot-only groups release as before.
  - **Adapted for Living Server:** the way back keeps our detours around hostile monsters and our stablemaster routes.
    A gamebot that makes no headway first plans its way back again, and only when that stalls too is it moved to you;
    never during a stablemaster ride. From another region (no route to try) it joins you after 20 seconds, as in
    Cancain's version. His battleground keep claims (also offered in #40) are not taken: grouped bots don't count for
    keep claims.
  - **Checked:** Cancain's tests plus nine new ones (`UT_BotGroupRelease`). Not yet seen in game here.
- **`/friend` works with gamebots, and two stock friends-list bugs are fixed** (issues #40 and #38; by Cancain).
  Online and parked gamebots of your realm join your friends list by exact name; a real character of the same name wins. Their
  logins and logouts show as "just entered / left the game" in both clients. Adding a friend no longer wipes the old
  client's Friends Monitor, and the first friend on an empty list gets its status. Tests: `UT_BotFriends`.
- **`/allrelease`** (also `/grouprelease`) releases every dead bot in your group at once (issue #40; by Cancain).
  `/spawn` companions still wait while you are dead or a fight is near.
- **On a world with a raised XP rate, Darkness Falls, battlegrounds and the frontiers get it too** (by rambodolphin13;
  BeezKneezItem9 found the same bug).
  - **The bug:** in RvR regions and zones a player's kill XP used `rvr_zones_xp_rate` (1 by default) in place of
    `xp_rate`, so a 10× world was 1× in DF while gamebots there got 10×. The base game's rule (from OpenDAoC),
    unchanged in 0.37b.
  - **Now:** players get `xp_rate` (or `xp_rate_by_level`) times `rvr_zones_xp_rate`, the rule gamebots already had.
  - **Checked:** two new tests in `UT_LevelRangeXpRates`. Not yet seen in game.
- **Gamebot fixes from long soak runs** (by Nikse77, via rambodolphin13; the keep doors by rambodolphin13). Autonomous
  gamebots only; `/spawn` companions and players are unchanged.
  - **Zone seams:** a route that steps over a zone seam and straight back three times is treated as a route failure,
    so the bot takes its usual way out instead of standing there (bots stood 10 to 40 minutes in Vindsaul Faste).
  - **Group stalls:** a member of a bot-only group stalled for 3 minutes, idle and in its leader's region, is moved to
    its leader (at most once in five minutes; never on a stablemaster ride, never in a player's group). The stall log
    names both zones and the member's recent trail.
  - **Ranged pulls:** a declined ranged pull no longer holds up the ordinary pull (a party of seven stood 30 minutes at
    its camp). A Necromancer pulls only when no one else can, a Cleric only before that.
  - **Mezzed enemies:** a mezzed enemy is left alone only while another free enemy is within 1,500 units; when the mez
    is all that is left, bots finish it instead of walking away.
  - **Darkness Falls:** a bot with no certified route out of DF probes less often after four empty tries and, after
    three minutes, goes to its realm's capital. A gamebot saved inside DF logs back in at its realm's entrance of the
    dungeon, not in a pack. DF stays open to solo bots.
  - **Keep doors:** an army inside a keep it just took can leave through the doors (four captured keeps held their
    armies: 661 route give-ups, then 213 teleports home).
  - **Checked:** their tests (`UT_AutonomousZoneSeamCycle`, `UT_SupportReachAndGroupStall`,
    `UT_DarknessFallsLoginEntrance`, `UT_FrontierTravelContracts`) and a new `UT_DarknessFallsExitHold`. Not yet seen
    in game here. Not taken: the travelling-Minstrel song change (it conflicts with our bot brain) and the forks'
    route-threat and zone-crossing changes, where our route code has diverged.
- **Water breathing potions work again** (by Rmarcoux86). Nereid potions and other water-breathing spells did
  nothing: their handler still used the old effect system, which the server no longer runs. They now let you breathe
  under water and swim faster for their duration, as before. Base game code; players only, as in the original handler.
  Not taken: the fork's default swim speed for everyone and its Nereid's Blessing buff. Checked: two new tests
  (`UT_WaterBreathingEffect`). Not yet tried in game.
- **The gamebots' camp list no longer breaks on a monster with no zone** (by MoomoocowPlus). One such monster stopped
  the whole list from being built, so every gamebot kept "Searching for a valid XP camp". It is now left out.

**Fixes**
- **An Animist turret no longer breaks when its target vanishes** (found in the 2026-11-04 overnight test). A pet's
  ordered target could be cleared at the very moment the pet checked it; that threw an error and the server removed the
  turret. The pet now checks the target it read, and a pet that has left the world has no target. Base game code.
  Checked: unit tests (`UT_PetOrderTargetGuard`); not yet seen in game.
- **The Epic Special Forces join a full raid after a wipe** (found in the 2026-11-04 playtest). Albion's automatic
  Summoner's Hall raid was made of small parties: 34 left the hub and four more joined on the road, 38 parties with 277
  bots. It wiped at Grand Summoner Govannon, and all three Special Forces parties were turned away ("the raid did not
  accept the party"), because a raid takes at most 38 parties and that limit counted them too. They now come on top of
  the realm's limits, as intended: the realm's 38 parties and 300 bots count only the realm's own bots, and the Special
  Forces have three parties and three formation places of their own beside the raid. Forced raids and dragon raids keep
  their rules. They also no longer need a "waiting for a group" goal to answer an automatic raid, and the realm doesn't
  top up their parties with its own bots. When a party is still refused, `REALM_RAID_SPECIAL_FORCES_FAILED` (and
  `REALM_RAID_FORCED_PARTY_RETRY`) now say why (`refusal="..."`), and raid and Special Forces log lines name Albion as
  Albion, not `_FirstPlayerRealm`. Checked: unit tests (`UT_SpecialForcesJoinFullRaids`). Seen since on a real
  install (a later development build): the Special Forces joined full raids after a wipe.
- **The Darkness Falls princes no longer eat bots** (found in the 2026-11-06 overnight test). Princes Asmoien and
  Ba'alorien devour the nearest pet every five seconds and heal by its health, but their pet test matched a bot's own
  brain too, so they ate companion bots and gamebots: the overnight raid did no damage for five minutes and never got a
  prince below 95%. They now eat only summoned pets, a player's or a bot's, never a bot
  (`EpicEncounterMechanics.IsDevourablePet`). Checked: unit tests (`UT_PrinceDevour`). Seen since on a real install
  (a later development build): a bot raid killed Prince Ba'alorien twice.
- Repairing an item never checked the crafter's skill: the gate was written as `(percentNeeded / 100) * craftLevel`,
  integer division, so the 50% a repair asks for became 0 and anyone could start one (with a 0% chance below 44% of the
  item's craft level). A repair now needs half the item's craft level in the skill, a repair in the trade window the
  full level, as intended (`Repair.HasSkillToRepair`, `UT_RepairSkillGate`).
- **The Herald counts the gamebots you invited into your guild** (from a r/daoc report). Its "Guilds of Note" read
  only the bot guilds' members, so a player's guild with 30 invited gamebots showed 1 member. It now also reads the
  invitations (`offline_world_bot_guild`) and counts each gamebot once; retired gamebots still don't count. Our own
  launcher bug. Checked: a new `HeraldPageTests` case on a small SQLite world (run on Linux here; CI runs it on
  Windows).
- **Bots talk twice as much by default: "How much they talk" is 2** (owner 2026-11-04, after the playtest measured it at
  your roster: 900 bots a realm, about 10 am, Albion heard 6.4 lines a minute across /faction, /lfg and /trade with 1,
  and about 15 with 2.5). An install that never changed the setting moves from 1 to 2 once, at its first start; one where
  it was set in the launcher keeps its value. Checked: unit tests for the default, the one-time move and a value set in
  the launcher; the levels themselves were measured once on a playtest PC. On a real install at 2, /faction can run
  above its ceiling (see the chat entry above).
- **Creating a character of a race this server doesn't offer is refused cleanly** (found in the 2026-11-04 playtest; also
  offered to the base game). The game client shows every race up to Labyrinth, but the server's world is Classic and
  Shrouded Isles: Half Ogre, Frostalf, Shar and the Minotaurs have no starting stats. Creating one (a Half Ogre wizard)
  threw an exception in the stat check; it is now an invalid character with a log line that says why. The player still
  only sees the character not appear: the character screen has no message for it. Checked: a new
  `UT_CharacterStatValidator` case.
- **A GM's level or rank set by command is no server first** (found in the 2026-11-04 playtest: `/player level 50` on a
  GM test character announced "the first on the server to reach level 50" to every realm, put it in the Herald and set
  off a wave of grats). Server firsts now skip GM and admin accounts, the launcher's "Make Me a GM" included. Not
  covered by a unit test (it needs a logged-in client); checked by reading the code.
- **"an elementalist", not "a elementalist"** (same playtest: "first to 50 as a elementalist"). The server-first text and
  every bot line with "a {slot}" use "an" before a vowel. Checked: new `UT_AutonomousBotVoices` cases.
- **Bots no longer sell their level 50 starter kit in chat** (found in the 2026-11-04 playtest: "wts offline level 50
  reaver 3/10 pst" in /trade). The new trade and voice lines named the most valuable item in a bot's bags, which for a
  launcher-made level 50 is its starter kit. They now skip it, as the Realm Exchange adverts already did.
- **"What does your guild do?" gets an answer about the guild, not just its name** (same playtest: a tell asking it drew
  "Salisbury Brethren"). Each guild has one focus (RvR, levelling, a bit of everything...), so every member tells the
  same story; "what guild are you in?" still gets the name. Checked: a new `UT_ChatResponsive` case.
- **The 1.1.2 server failed to start on a real game: the package now ships the server's own libraries** (found in the
  2026-11-04 playtest, before release). Save safety and the network defaults changed `CoreDatabase`, `CoreBase` and
  `CoreServer` as well as `GameServer.dll`, but the package carried only `GameServer.dll`, so the new server stopped at
  start beside the released libraries ("Could not load type 'Result' from assembly 'CoreDatabase'"). CI didn't see it:
  its tests build everything together and its installer tests use a fake game. The package now has a `core\` folder:
  `INSTALL.cmd` replaces each of those libraries where the game has one (the original or a Living Server build only,
  backed up and checked like `GameServer.dll`), `ROLLBACK.cmd` and `PREPARE-FOR-UPDATE.cmd` put them back, and Setup's
  safety copy before an Offline DAoC update includes them. Checked: new installer checks (each library replaced where the game has one and nowhere else, rollback, -ToRelease, a hand-changed library refused), and on the playtest PC the
  repacked build installed over the broken one and the server started.
- **The launcher says what to do when the server is stuck on STARTING** (a Reddit report, 2026-11-04: "stuck at
  Starting…, Enter Realm greyed out; yesterday it worked"). When this game's server has run for 3 minutes without
  answering, the footer says so (most likely a server left from an earlier session, or one stuck while loading) and
  points to STOP SERVER, which already offers to end a server this launcher didn't start, and to the log. The other
  known cause, Windows leaving the server out of its listener list, was fixed in 1.1.1 (issue #32). Checked: a new
  `ServerPortProbeTests` case (Windows CI).
- **The launcher says when the server stops by itself.** A server that stopped while starting left the launcher on
  STOPPED with no word, which looked like START SERVER doing nothing. The footer now says it stopped, with its exit
  code, and that the end of `runtime\logs\server-console.log` says why.
- **The installer installs into the game folder it sits in, even with other installs beside it** (found in the
  2026-11-04 playtest). With the build inside one game and other `playable*` folders next to it, `INSTALL.cmd` stopped
  with "More than one game folder found". The folder the build sits in now wins; the search of nearby folders is only
  for a build placed elsewhere. Checked: a new installer check with two side-by-side fake games.
- **Ctrl-click and Shift-click on the launcher's "+ Lv." buttons work from remote-desktop and automation tools** (found
  in the same playtest). They read the keys as they are when the click is handled, and a fast click had already let go
  of Ctrl, so one bot was made instead of 100. They now read the keys as they were when the click arrived, and either
  Ctrl or Shift key counts. Checked in the playtest: a remote Ctrl-click made 100 bots.

**Updates from the launcher, and Setup installs the newest package**
- **Updates from the launcher** (approved by the owner on 2026-11-03).
  - **The check:** when the launcher opens (at most once a day), or when you click **Check for updates** beside its
    version, it makes one request to GitHub's releases API for Living Server's newest release, with a generic
    User-Agent and nothing about you or your game. Prereleases aren't offered. Any failure is silent unless you asked.
    **Check automatically** beside the link turns the daily check off.
  - **The offer:** when a newer build is out, START SERVER reads **GET UPDATE**, and the line beside the version shows
    the build and the first line of its release notes. GET UPDATE asks "Update to Living Server Beta X? Your
    characters and world are kept" with **Install**, **Later** (START SERVER is back for this session) and **Skip
    this version** (not offered again until a newer one; Check for updates still shows it).
  - **The install:** only with the server, both clients and any other launcher closed. The package downloads to
    `runtime\updates\` with its progress shown, and is used only if it matches the SHA-256 digest GitHub publishes
    for the file; a missing or wrong digest is refused and the file deleted. The zip must hold one
    `LivingServer-<build>` folder whose `BUILD-INFO.txt` names the release's build. It is unpacked to a staging folder,
    then moved into the game folder as a player would put it. The launcher then closes and `apply-update.ps1` waits
    for it, runs the package's `INSTALL.cmd` without elevation (the same checks, backups and verified copies as a
    double-click), checks the result and opens the launcher again, which says how it went. `ROLLBACK.cmd` in the new
    package folder undoes it, as before. Every step goes to `runtime\logs\launcher-updates.log`.
  - **Kept:** the downloaded package and the one before it stay in `runtime\updates\` (older ones are removed), and
    the earlier package folder stays in the game folder, so either can be installed again. Your database and saves
    are never touched. `OfflineDAoC.exe --apply-update <zip> --sha256 <hex>` does the same without the window (tests
    and support).
  - **The launcher knows its build:** it is stamped at build time from the server's `ForkBuildInfo.BuildName`.
- **Setup installs the newest Living Server.** Before installing, Setup asks the same API for the newest release. If
  its package is newer than the one inside Setup and made for the same Offline DAoC release, Setup downloads it,
  checks it the same way and installs it. Offline, a rate limit, a bad or missing digest, or a package for another
  release: Setup installs the package inside it and says why on the progress page's first step, which always names
  the build being installed. `--no-update` skips the question (tests and offline installs); `--info` logs which
  package it would install.
- **Checked:** 69 new launcher tests (`Tests.Update*`: versions, the recorded release JSON, digests, the once-a-day and
  skip rules, the package checks, the download and staging with a fake network) in the gated CI step. On Windows
  CI: the launcher's update path end to end on a fake game folder (the published 1.1-beta package installed, then
  1.1.1-beta applied with `--apply-update`, refusals for a wrong or missing digest and a running game, and
  `ROLLBACK.cmd`), and Setup's choice of package against the live API with wrong and missing digests. Not yet seen on
  a player's PC: see [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md).

**Save safety and network defaults** (technical audit of 2026-11-03, §2 and the co-op bind defaults). Nothing
here changes gameplay. The WAL settings and the backup design are ported from lometur's HearthDAoC fork.
- **The world database always opens in a crash-safe mode.**
  - **The problem:** the server's code default opened the database with the journal off and syncing off
    (`Journal Mode=Off;Synchronous=Off`, unpooled). If the server crashed or the PC lost power during a save, the
    world could be corrupted. Setup's world import puts the file in WAL, but a server with that default switched it
    back on each open.
  - **Now:** whatever `serverconfig.xml` says, the server opens the world in WAL with `Synchronous=Normal`, pooled
    connections and a busy timeout of at least 10 s. The file isn't rewritten, so your other settings stay. The log
    says what it changed (`DB_SAFETY ... changed="journal Off -> WAL; ..."`) and confirms the mode
    (`DB_SAFETY journal_mode=wal confirmed`). At a clean stop the server moves the WAL into the database file.
  - **Why pooled:** the server's fast read path for WAL needs it. In a test (1,000 saves, then 1,000 reads), pooled
    WAL took 0.7 s and 0.45 s, unpooled WAL 3.8 s and 1.0 s, and the old unpooled journal-off default 1.4 s and 1.2 s.
- **An integrity check at each start.** The server runs SQLite's `quick_check` before it loads the world, and logs
  the time (`DB_INTEGRITY mode=quick_check result=ok ms=...`). On a 537 MB test database it took 0.5 s (the full
  `integrity_check` took 4.5 s). The server setting `db_integrity_check` picks `quick` (default), `full` or `off`. A
  damaged database is logged as an error saying how to restore a backup, and daily backups pause so they can't
  replace a good one. The server still starts.
- **Daily backups.** Once a day, while the server runs, it writes an online copy of the world to
  `runtime\data\backups\opendaoc-<UTC time>-daily.sqlite3.db`.
  - The copy is taken with SQLite's backup API while you play. It's written under a temporary name, checked with
    `quick_check`, and only then renamed. The oldest daily copies beyond the last 7 are deleted afterwards
    (`db_backup_keep`; 0 turns daily backups off). A failed backup deletes nothing.
  - It runs on a timer thread, never on the game loop. The first look is 5 minutes after the start, then hourly, and
    it skips when the last daily copy is under 24 hours old or the disk is short of space. The log says
    `DB_BACKUP result=written|skipped|failed` with the reason.
- **The installer backs up your world before installing.** `INSTALL.cmd` copies `runtime\data\opendaoc.sqlite3.db`
  (with its `-wal` and `-shm` files) into its backup folder (`runtime\server\deployment-backups\fork-build-<time>\data`)
  and checks the copy by SHA-256 before it replaces any file. It stops if the disk can't hold the copy. The newest 3
  installs keep their database copy, and older installs keep only their server files. `ROLLBACK.cmd` is unchanged:
  it never puts the database back, because that would undo your play since.
- **The realm event ledger moved to `runtime\data`.** `realm-event-records.sqlite3` (the Herald's and the Realm
  Records tab's history) was in `runtime\server`, where backups and progress transfers missed it. The server moves it
  at its first start (`REALM_EVENT_LEDGER moved`). If an older build wrote a new one later, the server merges it in
  without duplicates. The launcher reads it from either place. The progress importer now carries it
  (`progress-policy.json` `ProgressFiles`).
- **Network defaults reach this PC only.**
  - **The problem:** the code defaults listened on every network (`0.0.0.0`) with UPnP on, so a lost
    `serverconfig.xml` came back with the router asked to open TCP 10300 and UDP 10400 to the internet. The server
    never removed those router mappings.
  - **Now:** the defaults are `127.0.0.1` with UPnP and DetectRegionIP off. A missing config is written that way, and
    a setting a config leaves out takes the safe value. The UPnP mappings a server added are removed when it stops
    (`UPNP_MAPPING removed ...`), and mappings it didn't add are left alone.
  - **Your own config isn't changed.** When it listens beyond this PC or has UPnP on, the log warns at each start
    (`NETWORK_EXPOSURE Your server can be reached from other computers...`) and says how to turn it off.
  - `build_release_037.py` sets and checks `127.0.0.1` and UPnP off in every shipped `serverconfig.xml`, and
    `assemble_release_037.py` checks the finished package again.
- **No more "database is locked" on a busy read.** Found by the release-gates work: about 3 runs in 50 on Windows,
  the pooled WAL test failed when a `SelectAllObjects` read met a brief lock and gave up in under a second.
  - **The cause:** the read path that `SelectAllObjects` and every other `WhereClause` select use had no retry, unlike
    the server's other read path. Connections also had no native busy wait (SQLite's default wait is zero).
  - **Now:** that path retries a brief lock conflict the same way, up to 40 times with a short back-off (about 1.7 s
    in all), and other errors are not retried. Every SQLite database the server opens waits at least 10 s for a
    lock (`BusyTimeout`), even with a `serverconfig.xml` that sets none.
  - **Cleanup:** the command is disposed before its connection, and connections are disposed as well as closed.
  - **A second race in the same test:** a query clause shared between threads (the empty clause every
    `SelectAllObjects` uses) was marked ready before its parameters were set. A second thread could then read no
    parameters and fail with a NullReferenceException. This was seen once in a 50-run loop before the fix. The
    parameters are now set first.
  - **Checked:** 6 new tests (`UT_SqlitePooledReadContention`: the retry, its bound, the busy wait, disposal, a clause
    read by many threads at once, and three rounds of pooled readers and writers on every read path). A local loop (Linux) ran these with the two existing WAL pool tests: 49 of
    50 runs passed before the fix (the one failure was the NullReferenceException), and 60 of 60 passed after it, run
    as three processes at once to add CPU contention.
- **Checked:** these are automated tests only. 17 new server tests (`UT_WorldDatabaseSafety`: the connection string,
  WAL on a file that was journal-off, the integrity check on a damaged file, backup names, rotation, the
  once-a-day rule, the online copy, the stop checkpoint, the ledger move and merge, the network defaults and
  warnings, and the UPnP clean-up with a fake router). There are also 8 new installer checks and 5 Python tests for
  the release config. Seen since on the owner's install (2026-11-05 and 2026-11-08): crash-safe mode confirmed, the
  integrity check passed and a daily backup was written. The installer's database copy and the ledger move have tests
  only.

**Release gates**
- **Release gates** (audit 2026-11-03, §8.6). CI also runs on pushes to main. A `versions` check fails when
  README.md, the build name and this changelog name different releases (README.md still said Beta 1.1; it now says
  1.1.1, without the test count that went stale). A release workflow builds Setup from a published release's package,
  runs the installer's end-to-end tests, attaches the exe and puts its SHA-256 in the notes; see "Releasing Living
  Server" in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). The SQLite tests' cleanup no longer fails on Windows while a
  closing connection still holds the file (PR #56): each test has its own temporary folder, deleted with retries.

## Beta 1.1.1 — 2026-10-31

Fixes only, for the first week after Beta 1.1: the community's first reports (the party that wouldn't disband, the
launcher stuck on STARTING), base classes' weapon styles, quieter bot performers, and the new client's quest marker,
the first new-client file Living Server ships. Dragon raids regrouping after a wipe and the quest fixes (kill credit
in a bot group, one count per kill, hand-ins above a quest's level, the journal, quest XP by level) come next, once
they are played. The plan is [docs/RELEASE-TRAIN.md](docs/RELEASE-TRAIN.md).

- **Bots stop following you when you leave or disband their group** (issue #33, reported by nindybrew-svg).
  - **The bug:** with two or more invited gamebots in the group, `/disband` took you out but left them marked as
    following you. They trailed you without fighting for you, and refused new invites ("already following another
    player-led group") until you logged out. A single bot was already released correctly.
  - **Whose code:** the cause is in shadowofze's group code (`Group.RemoveMember`), unchanged in his 0.38 branch.
    It isn't a 1.1 regression, but 1.1's pathing to a far-off player made the stuck bots follow more reliably.
  - **Now:** when a player leaves, the gamebots that followed them follow another player still in the group, or,
    with none left, leave the group and go back to their own play. `/spawn` companions are left as before.
  - **Checked:** three new tests (released, handed to a remaining player, companions untouched); 4,096 server tests
    pass. Not yet seen in game.
- **Base classes get their weapon styles before level 5** (reported on r/daoc by Zarthol).
  - **The bug:** the style table lists styles only for the classes chosen at level 5, so a Fighter, Viking, Guardian
    or other base class had no styles at all until then. A Fighter with Slash 2 had nothing where a classic one had
    Slash's first style.
  - **Whose code:** shadowofze's data and style lookup, the same in 0.36c and 0.37b. Bots aren't affected: they start
    in their chosen class.
  - **Now:** a base class gets the styles its classes have in the weapon line it trains, each once (from the game's
    data: Fighter Ruby Slash, Thistle and Daze at 2; Viking Splitter, Thor's Anvil and Whirling Blade; Guardian
    Shining Blade, Contusions and Dragonfly). Chosen classes keep their own lists unchanged.
  - **Checked:** two new tests; the full server suite passes. Not yet seen in game.
- **Quiet twisting** (from a r/daoc report by ThatRebelKid).
  - **The problem:** a Minstrel, Bard or Skald bot twists by restarting a song every few seconds. Each restart played
    its 3-second cast music and its start sound to everyone nearby, so a bot performer near you never stopped.
  - **Now:** a bot restarting a song it started within the last 30 seconds does so without the cast animation and
    with the start effect silent. The first start of each song is still heard. Songs, timings and buffs are unchanged,
    and players' own songs and harmful pulses (a flute mez) keep their sounds.
  - **Checked:** three new tests (`UT_BotQuietSongs`); 4,101 server tests pass. Not yet heard in game.
- **The launcher no longer stays on STARTING when Windows leaves the server out of its listener table** (issue #32,
  reported by joshuaforehand).
  - **The bug:** the launcher asked Windows for the list of listening TCP ports and looked for 10300. On the reporter's
    PC that list left a working listener out, so the server was up, `Test-NetConnection` succeeded, and the launcher
    never left STARTING.
  - **Now:** the list is still the first check; when it doesn't show the port, the launcher tries a short connection
    to 127.0.0.1:10300 (150 ms at most) and takes success as "running". Shared launcher code, so it applies to both
    launchers.
  - **Checked:** four new tests (`ServerPortProbeTests`); the launcher suite (127 tests) passes on CI. The reporter
    built the branch and confirmed it on the PC that shows the problem (2026-10-30): the launcher went from STARTING
    to RUNNING and enabled Enter Realm while Windows still left the port out of its table. An in-game login on that
    PC wasn't tried yet.
- **New client: the NPC your current quest step needs is marked green, not orange.** Since 1.1 the server sends one
  marker scheme (available, current step, finish); the 0.37c client drew "current step" as "in progress elsewhere".
  - **How it reaches you:** the client runs its scripts from source, so the installer patches
    `new-client\godot\game\quests\quest_markers.gd` in place like the gate models (only when it is the released
    file; `ROLLBACK.cmd` puts it back). It is the first client script Living Server ships; the others in its source
    (the target window, FACE/FOLLOW/STICK, `/lfg` and `/trade` channels, the PLAY double-click guard) follow in 1.2.
  - **Checked:** the patch rebuilds the fixed file byte for byte; not run in game.

## Beta 1.1 — 2026-10-29

Beta 1.1 is several pieces of work together: the talking update and the living world (beggars, deeds, storylines,
spammers, roleplayers, rumours, holiday chatter, Leeroy), 2003-style bot names, a first half hour paced for signature
moments, a GM command to check it all in game, Offline DAoC 0.38's server fixes ahead of their
release, fixes taken from other forks of Offline DAoC, bots that react to attacks like players, quests that can be
finished with their markers and epic chains, and the all-in-one Setup. The plan and its checklist are in
[docs/RELEASE-1.1.md](docs/RELEASE-1.1.md).

### The talking update

A Reddit report from Beta 1.0 had a Hibernian bot in Ardee asking about an Albion dungeon. That led to a rework of how
bots talk; the full design is in [docs/bot-voices/TALKING-UPDATE.md](docs/bot-voices/TALKING-UPDATE.md).

**Bots say only what is true**
- Each line that names a place needs the speaker there or on its way, or says it is only talking about it. A place
  list of about 140 zones, towns and dungeons gives the realm and zones of each. The corpus build rejects lines that
  name another realm's zone, town or dungeon.
- Keep, relic, siege, raid and Darkness Falls lines need the news that makes them true, and use the real names.
  When something happens, a bot tells /faction in its own words about a minute later ("heard we lost Caer
  Benowyc"), as a witness if it was there, and others react.
- Xp ("4 bubbles to go"), role ("i can heal"), new-player and veteran lines follow the bot's real state. "Where are
  the mids?" is answered from what the realm has seen. With nothing true to say, a bot stays quiet instead of
  using the old formal lines.
- Lines that promised a bot would invite you are no longer said (a bot can't invite a player). Wizards no longer
  talk about their pet, and solo bots don't apologize to "my healer".

**Conversations**
- "why?", "rly?", "where?" right after a bot's line is answered by that bot. An answer to a bot's question gets
  thanks from the asker, and the others let it be. Tells carry on the same way and answer with where the bot is
  when it replies.

**Bots read what you say**
- Questions get true answers: a bot asked by name, tell or in your group says its own level, class, rank, guild,
  gear, plans or whether it is busy; any bot answers a place's monster levels (from the world data), battleground
  brackets, who holds Darkness Falls, the relics and keeps. Other factual questions get "not sure", not a remark on the
  topic. The bot's name in a line is no longer read as part of it.
- More follows on in /faction, /say and /group: "which one", "how long did that take", "whered they go", "no thanks",
  "ty" to the bot that helped, "x" to a bot's call for a group. Good news, offers, orders (with an honest "cant"),
  warnings, "wb" and real-life talk get replies in kind, and a groupmate reacts when you die.
- Fewer misreadings: "inc" as income, a group only talked about, sarcasm, an insult aimed at a class, a goodbye with
  thanks in it. Measured on lines written blind and not tuned against, about two in three are now read as a player
  would expect, and 12-16% draw a reply that misses (automated; see the design doc).

**Your group**
- Gamebots in your group call the fight: the pull, adds, low health, out of power, the mez, root, stun, taunt, group
  heal or rez they are casting, a wipe, and gj or med up after. Spaced out, never a flood. A bot that joins your
  group says hi.

**Tells and channels**
- Now and then (at most every 25-45 minutes) a bot starts a tell: a friend saying hello or thanks for an old group,
  passing on news or offering a low-level friend a hand; a guildmate; a solo bot of your level nearby offering to
  duo. Your /lfg may get a duo offer. "sure" gets "send me an invite".
- Most bot lfg lines go to /lfg and trade lines to /trade. Realm chat is busier with more bots online and in the
  evening, quieter late at night; new setting `bot_voices_chat_rate` (0-3). New setting `bot_voices_tells`.
- Guild lines and answers are written in each bot's style.

**Defaults**
- Bot voices, bot memory and grats are on by default (chat only). Personalities, guilds and the other Server life
  parts stay off. On a world that ran an earlier build, 1.1 switches them on once where the server had written the
  old default; settings saved in the launcher keep their values.

**Checked**
- Automated: all 3,616 tests in `Tests.dll` (Linux), including 79 new talking-update cases and a chat simulator that
  plays an evening in each realm, a lost keep, a group fight and three conversations through the real corpus. An
  independent code review found eight defects; all are fixed. Not played in game yet; the list of checks is in
  TALKING-UPDATE.md.

**Lore conversations**
- The lore debates are full conversations now: Albion's five tales, the shared ones, Avalon's, and richer lore talk
  in every realm, each in its realm's own voice.

### Server life: the living world

New Server life parts, on by default, each with its own switch in the launcher's Living Server tab. Details are in
[docs/LIVING-SERVER.md](docs/LIVING-SERVER.md) and [docs/bot-voices/TALKING-UPDATE.md](docs/bot-voices/TALKING-UPDATE.md).

- **Beggars** (`bot_living_beggars`): about 4% of each realm's Crafters beg in their capital, take the coin and items you
  drag onto them and hoard every copper. Ask anyone for gold and they follow you around the city insulting you.
- **Deeds and reputation** (`bot_living_deeds`): bots that see you solo a named monster, go on a kill streak, get zerged
  or die to something silly tell /faction, and you earn a reputation and a nickname.
- **Storylines** (`bot_living_storylines`): pairs of bots live out dramas in /faction over days (a kill steal, a guild
  split, a romance, a ninja-loot trial). Take a side and the ending follows you.
- **Spammers and roleplayers** (`bot_living_characters`): a gold spammer floods /faction until it is banned; a few bots
  speak in thee and thou and get mocked for it.
- **True rumours** (`bot_living_rumours`): a rare named monster that just spawned, or an enemy keep with its guards down.
- **The Dunce Rage Quit:** now and then a bot asks something hopeless in /faction, the realm piles on and it rage quits.
- **Bots remember each other** (`bot_group_bonds`): good runs make friends who group again, no-shows make grudges, and
  bots talk about both in /faction.
- **Holiday chatter** (`bot_living_holidays`): around New Year, Valentine's, St Patrick's, April Fools, Easter, Midsummer,
  Halloween, Thanksgiving and Christmas on your PC's calendar, bots talk about the holiday the way 2003 players did, in
  each realm's own flavour (Samhain, Yule, All Hallows' Eve), and answer your holiday wishes in kind.
- **Leeroy in raids** (`bot_living_leeroy`): one end-game raid in twenty has a raider who charges the final boss before
  the raid is ready, yelling his name; the raid has to follow him in, and /faction hears about it either way.

### Lag spikes with a large population (issue #25)

- Reported and diagnosed by Krolgosh-web. A warband walking to a distant frontier medallion merchant searched for a
  spot beside it from far away, proving up to 18 multi-zone routes every 15 seconds per bot. With many level 50s on
  RvR, the server tick stalled for about a second and killed bots stood at 0 HP before they fell. Stock 0.37 never ran
  this, because its far-off bots gave up on the medallion.
- Bots now look for the spot only within 2,000 units of the merchant, and wait 30 seconds after a search that found
  nothing.

### Offline DAoC 0.38's server fixes, ahead of their release

From shadowofze's 0.38 work, which isn't published yet (his `main` at `9da9091`, server code only). Thanks to
shadowofze.

- **Bot raids:**
  - A bot raid can start Caer Sidi's four horsemen and kill Apocalypse: the hidden controller that starts their
    challenge now runs whoever is near.
  - Galladoria's Olcasgean wakes for a bot raid once the four primals are dead.
  - Raiders no longer swing at things a player can't target (the Corrupt Primal of Fire's patches, which also hit
    for less now).
  - A raid target only a few are fighting draws some of the busy fighters over.
- **Armies:**
  - A relic or keep army's new leader is the marcher with most of the army around it, not a straggler far behind.
  - Siege healers stay behind the line, walk out to their dead and raise them; a fallen attacker waits for a raise
    while a healer of its army is within 5,000.
- **Travel:**
  - Hibernian bots no longer loop at their Odin's Gate portal keep door.
  - Albion bots step off the walled-in floor beside theirs instead of being sent home.
- **World code:**
  - Random helmets never roll a model the client draws nothing for.
  - A door the server has no record of opens again (and is logged as DOOR_NOT_IN_DATABASE).
- **Not included:** the new-client changes, the patch tools and the world-data parts (Campacorentin patrols,
  helmets already stored in a world). Those come with shadowofze's own 0.38 patch.

### Fixes from other forks

- **The effect leak.** An effect that ended before it had started was put back in the effect service and ticked
  forever, so the server slowed the longer it ran. Such effects are no longer added, and any ended effect still there
  is removed. From rambodolphin13's fork, which measured 30 million such effects and 326 ms per tick on a 3,000-bot
  run.
- **Siege timers.** Muster deadlines, the march after them, repels and the four-hour cap now run on the live clock.
  A throttle that started at the lowest possible time overflowed and skipped every pass (found by stefanrows' fork;
  the same code is in Offline DAoC 0.37). This had been off on every live server since the throttle was added, so
  sieges will now end, march and repel on their timers where earlier builds did not. It is the biggest change here to
  watch in play.
- **Healers** let a cure they are already casting on the same member finish instead of restarting it every turn
  (stefanrows, via rambodolphin13).
- **Animists** below half power keep one damage shroom on the fight and skip vents and spores, so they regenerate
  between pulls (rambodolphin13, from a tester report).
- **A full bag at the medallion merchant.** A bot with a full bag and nothing to sell no longer waits at the merchant
  until the watchdog sends it home. It stays away for five minutes (rambodolphin13).
- **Siege weapons** no longer throw an error when a bot lets go of one on another thread (rambodolphin13).

### Fewer failed routes

From stefanrows' fork (their 0.314 to 0.328), for autonomous gamebots only.

- A goal that failed because it lies just off the walkable ground moves onto its floor (dungeons and precise
  arrivals are left alone; a big height change must still be walkable).
- An enemy hunt inside another realm's keep courtyard with its outer gate closed is no longer offered. Bots pass the
  gates of keeps their realm owns, and a force sieging a keep keeps its targets there.
- A spot a bot gave up on is blocked for six minutes for the bot and its warband, so camp, roam and crossing choices
  stop picking it again.
- A bot standing on connected ground is no longer sent to its capital because its goal was off the walkable ground.
- The existing floor search is budgeted: fewer repeated failed searches.
- stefanrows measured route give-ups falling from 364 to 122 per game-hour in their world (not checked here). Their
  evidence is in [docs/NAVMESH_HOTSPOTS.md](docs/NAVMESH_HOTSPOTS.md).

### Bots react to attacks like players

From stefanrows' fork (their 0.271). An autonomous gamebot hit by a player, a bot or their pet reacts like a person:

- It answers after a moment: 0.3-0.8 s for melee, up to 1.2 s for a spell or arrow at it, up to 2.2 s for an area
  splash, plus up to a second for an attacker it wasn't facing, never more than 2.5 s. Tanks turn first and pure
  casters last.
- With personalities on, bold bots (Daredevils, No-lifers) turn first and cautious ones (Crafters) last.
- An area spell from a distance doesn't make its caster the bot's target. A bomber next to it does.
- A far attacker doesn't pull a bot off another player it is fighting.
- Healers heal while they turn, and cure a group with three or more members poisoned with the group cure.
- Mobs still get the immediate reaction. Companions and bots in your group follow you, so they are left out.
- Switch: `bot_incoming_fire_reaction` (on).

### Quests

From lometur's HearthDAoC fork.

- **Quests you can now finish:**
  - Quest targets and givers match a spawn's name whatever its capitals. Kills of "arawnite messenger" for "Path of
    the Renegade" never counted before.
  - Delivery items are handed once, on accept, and handed back when a step returns them. "Traveler's Way -- Supply
    Run" gave two bundles, and quests that start with a delivery handed nothing.
  - The quest journal holds 25 quests, as the client shows, and a quest you are on can't be given again.
- **Quest markers.** Every NPC uses one set of rules and they stay current:
  - a yellow knot over a giver with a quest for you (within 15 levels);
  - a white knot over an NPC your current step needs;
  - a blue knot over the one that finishes the quest.

  Markers update as you take, move, finish or abandon a quest and as you level, not only at a level-up.
- **Epic chains:**
  - Quest dependencies can name quest IDs, so a chain's steps come in the right order even where two steps share a
    name.
  - The Guild of Shadows chain (levels 7 to 48, both the Camelot and Shrouded Isles routes) has its period dialogue.
  - Every other guild's epic line has its steps pinned in order.
  - The world part is `tools/claude-version/epic_chains.py`. It is a dry run by default, `--apply` writes after a
    backup, `rollback --apply` puts the rows back, and saves are never touched.
  - GMs get `/epic` to see and set a character's chain.

### All-in-one Setup

- `LivingServerSetup.exe` installs Offline DAoC and Living Server for someone who has nothing, or adds or updates
  Living Server on a game they already have.
- It runs shadowofze's own downloader (pinned by SHA-256) and the Living Server package's own install and rollback
  scripts, so every safety check stays.
- Details are in [tools/installer/README.md](tools/installer/README.md).

### New loading screen and welcome art (owner 2026-10-28)

- The game's loading screen and Setup's welcome page use a new Living Server painting: a Midgard dwarf, an Albion
  knight with the chalice shield and a Hibernian caster (`tools/installer/art/living-server-splash-source.jpg`).
- The loading screen (`pregame/splash.mpk`) is replaced only over shadowofze's exact file, and it is backed up first.
  ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd put his back. Any other splash is left alone.
- The package ships the new file whole under `content/`: a content-patch entry can now name a file ("File") instead of
  byte changes, for files that are entirely Living Server's own. A damaged copy is refused before anything changes.

### Bot names in the 2003 style

- New bots are named the way a 2003 server's players named themselves (owner 2026-10-29: three in four in the 2003
  style, after research). Research of about 300 names from 2001-2004 rosters and forums is in
  [docs/research/BOT-NAMES-2003.md](docs/research/BOT-NAMES-2003.md).
- The mix:
  - made-up names, 35% (Hasys, Xegrek, Qwannox);
  - English compounds, 12% (Steeleater, Frosthide);
  - jokes, 10% (Mushypeas, Mrbiscuits, Captainjelly);
  - everyday first names, 7%;
  - class or race jokes, 3% (Sneakyfriar, Angrycabalist);
  - stretched letters, 2%;
  - xX names, 3% (XxShadowxX, Xxpainxx);
  - number names, 3% (Zerga420, Headshot91);
  - lore names, 25% (the realm's old syllable names, or real Old English, Norse or Irish ones).
- A name is never a word players say in chat (a bot named "Axe" would catch "anyone got an axe?"), never on the
  server's invalid-names list, never pop culture or a joke from after 2003, and never the numbers 88, 14, 18 or 69.
- The server and the launcher's + buttons share one generator (`OfflineDaoc.Shared.EraBotNames`, linked into the
  launcher). Bots you already have keep their names.

### The first half hour

Owner 2026-10-28: "I really want this update to feel like a major difference." A 3h20m session had seen two storylines
and two spam waves.
- A play session starts at the realm's first login, or after half an hour away.
- Then:
  - the first /faction scene comes 5-10 minutes in, and is a storyline episode when one can play;
  - a roleplayer speaks 6-18 minutes in;
  - a gold spammer turns up 12-30 minutes in;
  - a friend's first tell still comes 4-10 minutes in.
- Storylines: one episode per realm every 45 minutes at most (was 75), episodes 3-10 hours apart (was 6-20), a new
  arc every 2 hours (was 4).
- Gold spammers post 5-10 ads before the ban (was 3-6).

### /lstest: every Living Server moment on demand (GMs)

- `/lstest` (GM accounts) fires each part now, so a session can check what is rare by design:
  - storyline, debate, dunce, spammer, roleplayer, rumour;
  - tell, greet, gossip, memory, remember;
  - deeds, holidays;
  - Leeroy and a realm raid sized to its level 50s (`/lstest raid [dragon] [alb|mid|hib]`).
- Each says why when nothing fits.
- Details are in [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).

### Fixes from the in-game Living Server check (2026-10-28 and 2026-10-29)

**Storylines**
- A running arc's two bots had to be out of combat at the moment a scene was chosen, and grinding gamebots almost never
  both are, so arcs hardly ever moved on. Now they only need to be in the game; the scene waits for a busy one.
- A protagonist who dies mid-episode is waited for, not counted as gone, so the episode doesn't end halfway.
- An arc waiting on bots that are out of the game is dropped after a day (was three).

**Deeds**
- A witness mid-pull is retried after 15 seconds (was two minutes), or 60 while a /faction scene plays. A witness who
  logs out is replaced by another bot near you.

**Greetings**
- A bot picked its greeting at 700 units, past the 512 a /say carries, so a bot in between used up your greeting
  unheard. Bots now greet from within 400 units and speak within two seconds.

**Leeroy**
- A raid that reaches its boss all at once used to leave him no moment to charge. Now he jumps the pull.
- He shouts his charge in /faction as well as /yell, so the whole realm hears him go.
- The /faction comment after the raid ("cant believe that worked") never came, because the raid's member list was
  emptied before a speaker was picked. Fixed.

### Launcher and shortcut

- The **Living Server** tab comes second, after Active Population.
- Setup's desktop shortcut has its own icon, from the new loading-screen art.

### Play-test fixes (2026-10-27 and 2026-10-28)

Found by playing the Beta 1.1 test build on a fresh 0.37b install made by the new Setup.

**Resurrection**
- A rez offer that ran out unanswered made the bot's next offer cancel itself: it said "You decline to be
  resurrected", and accepting then said "No one is currently trying to resurrect you". Ten casts, no rez. Only the
  offer you are answering can now accept or decline, and an expired one is dropped.
- A gamebot no longer raises you into a live fight (you stand where it stands). As players do, it steps to a clear
  spot within rez range and raises you from there; with no such spot it clears what is fighting or would aggro first.
  Gamebot corpses keep the combat rez.
- A bot doesn't queue a second rez while one is waiting, and waits a minute after you decline.
- "You must wait 0 seconds" after an interrupt now shows the real wait.

**Your group**
- Groupmates beside you come along when you teleport, take a portal or release to your bind point, and a groupmate
  far behind paths to you. A stuck groupmate is brought back to you instead of leaving the group.
- "Come with me" gets an honest answer: on my way, or how far off the bot is.
- Fight talk comes only from groupmates who saw the fight, hunting talk only after a recent fight, and a reaction to
  you dying only from groupmates near you.
- A bot in your group talks in /group, not /say to strangers ("anyone running Stonehenge Barrows today?").
- A bot in your group no longer quotes its old solo hunt ("mountain algaes at Camelot Hills are the best xp").
- `/invite` by name says when a far-off bot is busy (riding a stable route, a siege, a raid) instead of "too far
  away".

**Answers**
- LFG with nobody near your level gets an honest reply; "where should I level" names your realm's starter zones; the
  dragon, time to 50 and the realm's raid get real answers; a "wtb" gets a trade reply that fits; insulting a bot's
  guild is read as abuse.

**`/tele mob`** ranks camps by their average level and lands you at the spawn nearest your level.

**Setup**
- A first install turns every Living Server feature on at our suggested settings (bot guilds, personalities, voices,
  all of server life, the suggested XP by level, the real-clock schedule), shown as a ticked option you can untick.
  A game that already has Living Server keeps its own choices.
- Shows the real error when the official download fails, and says when the connection dropped.
- Names the folder of a game program that is still running, and points to Task Manager when it has no window.
- Cleaning up after an install removes the "Offline DAoC setup files" folder too, with Setup's copy of the downloader.
- A new install proposes a folder that is free (`playable-v0.37b-2` when you already have `playable-v0.37b`). Choosing
  a folder that holds a game says so ("Offline DAoC 0.37b is already installed in ..."), and a folder problem offers
  CHOOSE ANOTHER FOLDER instead of only Try again.
- The log leaves out curl's progress fragments, which looked like errors.
- Replacing another build with the same name says "replaces it with this one" instead of "upgrades Beta 1.1 to Beta
  1.1".

### Ready for Offline DAoC 0.38

- A build can now fit several releases (`BaseRelease`, e.g. `0.37,0.38`). The installer accepts a game updated to any
  of them and refuses anything newer. It stays `0.37` until 0.38 is published and merged. Setup reads the same list:
  a game on any listed release takes Living Server as it is, and new installs and updates go to the newest.
- Beta 1.1 targets shadowofze's latest release, **0.37** (`v0.37`, `ab4df02`), and carries 0.38's server fixes from his
  `main` (`9da9091`); 0.38 itself is not published yet (checked 2026-10-28).
- [docs/UPSTREAM-RELEASES.md](docs/UPSTREAM-RELEASES.md) is the checklist for that day.

### Checked (Beta 1.1 so far)

- **Automated:** every branch above merged into `release/1.1`. On Linux the combined tree builds and all 4,093 server
  tests pass. CI on Windows runs the full suite and the launcher and installer tests, and builds the package.
- **In game (2026-10-27 and 28, test builds of this tree on a fresh 0.37b install made by Setup):**
  - Setup's new-install, resume after a dropped connection, and update-in-place paths work.
  - The server starts on the build, with no new error types against Beta 1.0.
  - Checked play:
    - LFG and starter-zone answers.
    - A groupmate follows you, comes along through `/tele` and a release, and buffs you.
    - `/tele mob` lands a level 1 at a starter camp.
    - A rez accepted after an earlier offer ran out now works.
    - A cleric in a respawning camp steps to a clear spot within range and raises you from there.
  - A full new install with the release/1.1 Setup on 2026-10-28: 4.8 GB from shadowofze's release, unpacked, updated
    to 0.37b and Living Server added in about 10 minutes with every step clean; cleanup; first server start
    (`FORK_BUILD name=1.1-beta`, only the stock world-data errors), automatic account, a new character, into the world.
- **Not checked here:** the epic chains' world script hasn't been run against a real clean 0.37b world (its test
  needs one).
- **In game, 2026-10-28 and 29 (test builds 2-5 on the owner's test copy, up to about 1,000 bots online, with `/lstest`):**
  - Seen working:
    - holiday chatter and replies (Christmas test date);
    - a gold spammer, banned after 7 ads; a roleplayer line, then mocked;
    - the dunce rage quit and its parting tell;
    - rumours with replies;
    - deeds told to /faction with reactions;
    - storyline episodes (romance, ninja-loot trial) with side-taking and both protagonists' tells;
    - greetings, gossip and tells from bots that remember you;
    - the new session's first half hour: debate at 6 minutes, roleplayer at 18, spammer at 22;
    - 2003-style names from the launcher (Waffleironer, Xxpainxx, Angrycabalist), shown in the world;
    - Leeroy charging Golestandt before the raid was ready, the raid following him in, a wipe and then the kill;
    - a natural Leeroy at Trollheim;
    - automatic raids opening in all three realms once each had enough free level 50s;
    - a 9.5-hour overnight run with no errors from this code.
  - Not yet seen: Leeroy jumping the pull, his /faction shout and his after-raid comment (fixed after the last test
    build); a storyline protagonist dying mid-episode.
- **Not played yet:** sieges at your usual bot count (their timers now run); quests and markers.
- **Known:** automatic raids open only with 80 free level-50 bots in the realm, so a small roster rarely gets them.
  A roster filled only with the Lv.1 and Lv.50 buttons has no bots between levels 20 and 49 until they level up.

## Beta 1.0 — 2026-10-22

The first release under the Living Server name, and the first public build since 0.36-se.1. Beta 1.0 is the build
developed as 0.37-se.2: everything below in this section, plus 0.37-se.1 and 0.36-se.2 to se.4, which were never
published on their own.

**Release (2026-10-22)**
- Renamed to **Living Server**: the launcher tab, the installer, the docs and this changelog
  (`CHANGELOG-SOCIAL-EXPANSION.md` points here). The "Living Server" feature group is now **Server life**. Server
  setting keys, database tables and saved settings are unchanged, so an earlier build's settings carry over.
- The build is named `1.0-beta` (`FORK_BUILD name=1.0-beta`) and still installs on Offline DAoC 0.37 / 0.37b;
  the release it fits is now its own setting (`ForkBuildInfo.BaseRelease`) instead of being read from the name.
- The installer now also fixes game files in place, from byte patches against the released files
  (`tools/fork-build/content-patches.json`, made by `make_content_patch.py`; 8 KB, no game file shipped whole):
  Hall of the Corrupt's `zone277.nav` (the Lord Gildas walk links below) and the eight new-client gate models
  (below). A file is changed only when it has the released fingerprint, the result is checked against the fixed
  one, and the original is backed up; `ROLLBACK.cmd` and `PREPARE-FOR-UPDATE.cmd` put it back. Installer tests
  cover a fixed, a missing and a hand-changed file, rollback, the return to the release and a damaged patch list.

**Checked (Beta 1.0)**
- Automated: 3,444 server tests, the launcher tests and the installer tests (Windows PowerShell 5.1) on the build
  from `main` (`71250e2`), the one in the release zip.
- The patches were applied to the released `zone277.nav` and the eight released gate models with the installer's own
  PowerShell code; every result matched its fixed fingerprint.
- On the owner's PC (0.37c game, 900 bots per realm, every switch on): the release zip installed over 0.37-se.2. It
  found the earlier build, backed it up, and reported the navmesh and gate files as "fixed already" (the test builds
  had fixed them by hand). The launcher shows the **Living Server** tab and "Server life (9 of 9 parts)". The server
  started with `FORK_BUILD name=1.0-beta commit=71250e2aa711` and the same 390 start-up errors as before, the memory
  census was switched off, and the owner's character logged in.
- Raids in the last two days on the builds before this one (all scheduled by the bots): 28 ended, 17 won.
- First raid on Beta 1.0: Albion's scheduled Hall of the Corrupt raid killed Aidon the Archwizard and then Lord Gildas
  (180 bots, 61 minutes). The night before, the same raid had stopped above his room with "Route blocked": the
  `zone277.nav` walk links work in game.

## Developed as 0.37-se.2 — 2026-10-20 to 2026-10-22

The endgame now runs without a player directing it: warbands open and defend keep sieges, each realm raids its
dragon, its epic dungeon, Summoner's Hall and Darkness Falls on a schedule, a wiped raid regroups and tries again with
the Epic Special Forces, and parties in dungeons go for the boss. Tested on the owner's PC with 600 bots per realm
overnight and through 2026-10-20. 0.37-se.1 was never published on its own, so this release also carries everything
in it (below).

From the owner's first day playing 0.37-se.1 (the server log of 2026-10-18: 4,721 bot tasks), then a live check on the
owner's PC the same night (a 20-minute and a 40-minute session on the new build, compared with the same minutes of the
evening's earlier 0.37-se.1 session; see "Checked" below).

**Body language (new server life part, off by default)**
- `bot_living_body_language` (launcher: Living Server > Server life > **Body language**): bots standing around in
  town, waiting for their group or resting look around, emote now and then, take a step or two in town, turn and
  wave at you when you walk up, and cheer a ding nearby. Only where a real player can see them, never in a fight.
  Bots can't jump, sit or strafe: the game sends no such state for an NPC. See [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).

**Safer routes for solo bots**
- 30% of bot tasks ended in a death, more than half of them before the bot reached its camp. Of solo bots that pulled
  an orange monster on their route, 34-54% were dead within 150 seconds (21-27% after a yellow; 7-18% after green or
  blue). Now a solo bot goes around a yellow when it can and gives up the camp rather than fight an orange (a single
  orange is still fought when it has just given up another camp). A solo bot that died in the last half hour treats
  a yellow like an orange. Groups are unchanged.

**Bots waiting for a party stand between pulls**
- 98% of bot tasks were solo: bots waiting for a party grind solo, and party formation only takes bots that aren't
  fighting, so 244 waits ran out after 20 minutes and only 23 PvE parties formed all day. A bot waiting for a party
  now often stands at its camp between pulls for 45-90 seconds ("Looking for a group between pulls"), like a player
  with /lfg on: 60% of the time at level 50 (which gets no experience from soloing), 35% while levelling.

**Chat found wrong in the live check**
- Replies to "lfg" no longer assume you lead a group: "lfg 50 cleric" drew "how many do u have?" in /faction. Lines like
  that still answer "lfm".
- A guildmate no longer asks what you just said: "lfg, anyone want a 50 cleric?" drew "what level and class?" in /gu.
- Gossip about a rez only comes from a bot you really rezzed, and never says "tonight": a bot with three groups and no
  rez in its memory said "Claudecheck rezzed half our group at Campacorentin Forest tonight".
- Bots no longer advertise the level 50 starter kits in /faction ("Does anybody want to make me an offer on Offline
  level 50 Cleric 41/33?").

**New client: gates whose portcullis never rose (2026-10-21)**
- The owner found Snowdonia Fortress's gate doors opening while the portcullis stayed down. The model converter took
  only a door piece's swing, and a portcullis slides, so it was exported with the same open and closed position. The
  converter now reads the slide too. Eight converted models were patched (the Albion and Midgard border keeps, two
  stone halls, Caer Diogel's bars, a Galladoria root barrier); checked in game at Snowdonia: both gates open and the
  north gate can be walked through. The new-client update must carry these eight files.

**A boss could stop the server (2026-10-21)**
- In the live run the Midgard raid (about 150 bots) fought Lydsyg in Iarnvidiur's Lair. His script recasts his area
  disease from OnAttackEnemy, and a spell landing on each target counts as an attack, so each cast set off dozens more:
  the server's memory went from 2.9 GB to 37 GB in ten minutes and the game stopped. 69 scripted encounters cast this
  way; with one player the chain dies out by itself. Now a monster can't ask for the same instant spell on the same
  target again within a second, or queue it twice, and keeps at most 16 cast requests. Bots and players are unchanged.

**Effects that outlived their NPC (2026-10-21)**
- The memory census found 2,755 spell effects still running on NPCs that had left the world, growing by about 400 an
  hour: a Sluaghbinder pet's pulse went on pulsing after the pet was deleted, and a pulse's snares on monsters only end
  when the pulse does. Each one kept its NPC in memory. Now an effect on a deleted monster, pet or bot ends, and so does
  a pulse whose caster was deleted. Players' effects are unchanged.
- The census also listed about 1,500 respawn timers on "deleted" monsters. Those are normal: a slain monster is
  deleted while it waits to respawn, and the timer brings it back.

**Chat that answers what was said (2026-10-21)**
- The owner found conversations still didn't make sense. Nothing in the log showed what was said, so the server log
  now has a chat transcript: `CHAT_LINE` for every bot line a player can see (/say near a player, /faction, /gu,
  /group, /send), with `via=` naming the code that chose it, and for every line a player says, with the intent the
  chat model read in it.
- An audit ran 55 typical player lines through the reply path. Many drew a reply that didn't answer them: "where is a
  good place to level at 24?" drew "take the horse from Camelot", "yes" drew "interesting", "sure" drew "lol what",
  "anyone in camelot?" drew "maybe maybe not", "i died again" drew "what?", "how much is a bloodied sword worth?" drew
  "not my class sorry", and "anyone want to do a dungeon?" drew a dungeon tip. Now:
  - "ok", "sure", "nice", "me too" and other acknowledgements draw no reply, and neither do "really?" and "why?" unless
    you name a bot.
  - A line nothing fits draws no reply, where it used to draw "huh", "lol what" or an old formal line ("Things seem
    steady from where I am."). A question nothing fits draws an honest "not sure".
  - Answers from the live world: "where should I level at 24?" names the zone where bots of that level are camping
    right now and a dungeon for the level; "where are the mids?" names the frontier zone with the most enemy gamebots
    in it (or says it's quiet); "anyone in camelot?" is answered only by bots that are there; "where are you?" in
    /say, /faction, /gu and /group gets the bot's zone, level and class, as /send already did.
  - "anyone want to...?" and "who wants to...?" get yes-or-no-to-coming answers (never "me me me" from a bot that won't
    come), price questions get price answers, "should I...?" and "is it worth...?" an opinion, "can you help me?" "sure,
    whats up?", and a complaint sympathy. An either-or question isn't answered "imo yes". A buyer isn't told to vendor it.
  - A question or request is no longer dropped because bots answered another line in the last 10-12 seconds ("hi" then
    "anyone want to group?" got nothing).
- In-game check the same evening: "anyone in camelot?" drew "im in Camelot Hills" (a place name now means that place
  when a zone or region is called that: Camelot is the City of Camelot), and "where are the mids?" drew two bots both
  naming Uppland, Midgard's own frontier (enemies on our side of the frontier are named first, then neutral ground,
  then their homeland, and such a question gets one answer). Re-checked in game on that build: still wrong. A bot on
  its way to Camelot counted as in Camelot (now only where a bot is counts), and the world data gives every frontier
  zone realm 0, so Uppland didn't read as Midgard's (the twelve frontier zones are now mapped to their realms, and
  enemies all at home are reported as "back home in Uppland").
- A guild no longer hears the same death or group line twice within the hour: in the live run "died. the run back is
  the worst part" came three times in /gu in 17 minutes, and "guild warband forming, frontier bound" twice 30 seconds
  apart. When every line of the set was heard lately, the bot says nothing. Greetings, congratulations and welcomes
  follow the same rule, and a number doesn't make a line new: on the next live run two guildmates both said "hey
  Claudecheck, good timing" 2 seconds apart, and "2 of us heading out to the frontier" was followed by "3 of us...".
- Bot-to-bot: an either-or question takes the choice answers, frontier news the frontier replies ("mids 8man in cathal"
  drew "same :)"), and "anyone else...?" an opinion rather than "count me out this time".

**Bots that logged out stayed in memory (2026-10-22)**
- The overnight run's 2,424 scheduled bot logouts left 1,500 deleted bots in memory, and Windows Event handles grew
  with them (about 1,000 an hour, from 2,200 to 9,800). The extended holder census found what held them: monsters'
  last interrupter and the attack targets they remember (and the same fields of bots holding deleted monsters), which
  nothing cleared after a fight. Every two minutes each living in the world now forgets objects that have left it:
  its last interrupter, remembered attack targets out of a fight, aggro holders and cached nearby-object lists, and a
  bot's recent attacker and route-threat pull. Damage credit (XP, loot, realm points) is left as it was: only a bot's
  or player's record of departed neutral monsters is dropped, which earns nothing. The first live run of the sweep
  still found monsters' aggro lists holding deleted bots; those are cleared too now.
- Live, two hours on the owner's PC: deleted bots still in memory after each full collection stay at 50-125 (overnight
  they only rose, to 1,500), and deleted monsters and pets level off too. The sweep takes about 60 ms every two
  minutes. Event handles still grow about 1,700 an hour, so they had another cause: a .NET Lock makes a Windows event
  the first time a thread waits for it and keeps it for the Lock's life. A test-run census (`SERVER_LOCK_EVENTS`)
  counts which fields hold such Locks. Live: of 2.1 million Locks, 342 had an event 20 minutes after start and 1,026
  after 50; from 12:08 to 12:38 they rose by 684 while handles rose by 858. Two thirds are the stat tables every monster
  and bot carries (`PropertyIndexer._lock`), then ability lists, stat multipliers and effect lists. These belong to
  objects still in the world, each Lock adds at most one handle, and the handle closes when its object is freed: about
  40,000 handles and a few MB of Windows memory a day, against a limit of millions. Not changed for this release; a
  lighter lock for the stat table is the candidate fix afterwards.

**Raid routes found broken in the overnight run (2026-10-22)**
- Hall of the Corrupt: Lord Gildas is reachable. The raid killed Aidon, then stood above Lord Gildas's room for 37
  minutes and ended "Route blocked". The ramp up from his room meets the main floor at a single 32-unit step, a little
  higher than the navmesh's walkable climb, so the mesh had his room as a separate area. Three short two-way walk links
  over that step in `zone277.nav` (one tile changed; new tool `tools/navmesh_splice/add_offmesh.py`). The patched mesh
  ships with the update; checked offline with the real Detour library (from the entrance, from where the raid gave up
  and from Aidon's room, there and back), not yet in a raid.
- Trollheim is raided again. Every staging post beside its portal in Gripklosa Mountains lay on a shelf next to the
  portal that joins neither the portal nor Bjarken, so the raid was never scheduled ("no corridor inside zone 152 to
  the staging post"). A post now has to be walkable from the portal. Offline: all 20 posts route from Bjarken; the
  Darkness Falls posts and Krondon's are unchanged.
- Cruachan Gorge: 23 Hibernian raiders late for Darkness Falls stood at one spot on the Breifine border for 33
  minutes before they found their own way. Their route proves offline from that exact spot, so the cause is not
  found yet; the warning they logged now says why the route failed, where it was going and how long the bot has
  not moved.

**Melee bots no longer stand under flying monsters**
- A melee bot only picks a flying monster it can walk up to. A level 50 Midgard melee group timed out 45 times on
  griffon gliders in Gripklosa from 2,400-3,500 units away, and one member stood still for 15 minutes. Casters and
  archers shoot flyers as before.

**Bots no longer walk past Aidon to reach the other frontiers**
- The four Old Frontiers dungeons (Hall of the Corrupt, Summoner's Hall, Dodens Gruva, Marfach Caverns) were the
  bots' walking road between realms. Albion warbands formed at Snowdonia Fortress, far from the Castle Sauvage
  porter, so they walked to Midgard and Hibernia through Hall of the Corrupt, past Aidon the Archwizard (level 75).
  In one hour about 118 of 895 bot deaths were in those four dungeons, 50 of them to Aidon and his illusions,
  and the same bots died there two and three times.
- Now a bot enters those dungeons only as a destination (or on its way out, or the neutral raid's own approach
  to Summoner's Hall). Warbands reach the other frontiers by their realm's porter: a distant warband rides or
  walks to it. Enemy bots inside those dungeons are fought where they're met, never hunted there.
- Side effect: Albion PvE parties no longer pick Dodens Gruva or Marfach camps, which they could only reach through
  two of those dungeons.

**Level 50s keep searching wide after a failed long pull**
- The 10-minute "look nearby first" pause after a pull that never reached its target now applies to levelling bots
  only. Level 50 camps are spread out: with the pause, level 50 kills per bot fell from 11.0 to 9.5 an hour.

**The endgame runs on its own: sieges and realm raids**
- A level 50 RvR leader now forms a warband of 4 to 8 (or roams alone), and a warband forms with the members it
  finds instead of not at all. Overnight 234 of 250 warbands were pairs, and only a warband of 4 or more can open a
  keep siege: one siege in 7.7 hours. Every siege opening is now logged (`RVR_EVENT_OPENED`).
- Automatic realm raids (the realm dragon, the realm's epic dungeon, Summoner's Hall and Darkness Falls) are sized
  to your roster: half the realm's online level 50s, at least 40 and at most the encounter's 200
  (`realm_raid_minimum_bots`, 0 = scaled). They needed 200 level 50s looking for a group in one realm, so with 150
  per realm none ever started. They now recruit level 50s waiting for a group and level 50s grinding solo (who
  earn no experience there); RvR bots stay on the frontier. Forced raids from the launcher are unchanged.
- Each realm opens a raid on a schedule (`realm_raid_schedule_minutes`, default 150; 0 = off), rotating dragon,
  epic dungeon and neutral raid: the first 20, 30 and 40 minutes after the server starts. Logged as
  `REALM_RAID_SCHEDULED` and `REALM_RAID_ENDED` (with the outcome, size and length).
- Found in the first live run: an automatic raid planned one party of eight at a time and waited until all eight
  were out of combat at once, so the first scheduled dragon rally stalled at 24 of 58 for twenty minutes. It now
  holds three planned parties and forms whichever eight are free first, and a whole PvE party of five to eight
  level 50s can join a raid that is recruiting (only a full eight still opens an unscheduled one).
- Four-hour live run (2026-10-19): Albion and Midgard killed their dragons with 66 and 72 bots, and Albion took
  two Hibernian keeps. But three of five scheduled raids never fought: 82-91 bots were recruited and only 46-51 were
  staged after 90 minutes (stragglers fighting trash at the entrance, still walking or released). A raid scaled to
  the roster now goes in late with 70% of what it needs (a full 200 raid keeps 90%): it can still lose the fight.
- Raid loot is logged (`REALM_RAID_LOOT` with the bot that received each piece, `REALM_RAID_LOOT_UNCLAIMED` when no
  nearby raider could carry it, `REALM_RAID_LOOT_MONEY`).
- Not yet known: whether 40-110 bots can beat encounters tuned for 200. Darkness Falls and Summoner's Hall bosses
  are level 64-80 and were barely scratched by 32 bots in an earlier test; their raid AI is not finished.

**Bots remember where a monster far above them killed one** (from stefanrows/OfflineDAoC)
- When a monster 10 or more levels above a bot kills it, the spot is remembered for 6 hours: bots that monster is
  still far above pick no camp within 3,000 units of it, and a levelling bot counts a straight walk past it as a
  long trip, so it usually picks another camp. In one hour a merucha camp (levels 30-33) on the Connacht road
  killed 18 bots of levels 10-17 on their way to other camps, and 126 of the 159 bots under level 20 that died to
  red or purple monsters hadn't reached their camp yet. Kept in memory only; a restart forgets.

**Levelling bots walk around monsters far above them** (route bends from stefanrows/OfflineDAoC)
- The remembered spots above didn't stop road deaths in the live test: the Connacht meruchas still killed 30 bots
  of levels 10-17 in two hours, some three times running from the same bind point, because roads to camps all
  over Hibernia pass them. 22 of the 30 had seen the pack and chosen the old one-monster sidestep or a new camp.
- Now a solo levelling bot on the road bends its next 3,000 units around every red or purple monster in sight
  (700-1,650 units, wider the higher it is, whether or not it's already fighting someone) and around remembered
  death spots, taking up to 6,000 extra units, every bend checked on the navmesh first. Logged as
  `AUTONOMOUS_DANGER_BYPASS`.

**Every realm attacks, and sieges are contested**
- On 2026-10-19 Albion opened all four bot sieges and took every keep: one server-wide roll decided when a siege
  could start, Albion's siege-sized warbands formed first after each raid (its raids ran ten minutes ahead), and
  with one siege running the next had a 4% chance a minute. Each realm now rolls for its own attacks, and a realm
  that hasn't attacked for 30 minutes gets an extra 2% a minute (at most 50%). Raid slots drift by up to 20 minutes
  so the realms don't keep the same order.
- Defenders could bring half the attacking army (12-17 against 42-53), so every siege fell. The new setting
  `rvr_siege_defense_share` is 0.75 (the stored `rvr_siege_defense_ratio` of 0.5 is no longer read), and the last six sieges steer it by up to 0.25: attackers winning more than 80% bring more defenders,
  less than 45% fewer. The target is a siege won about two times in three.

**A wipe is not the end of a raid: regroup and try again**
- In Galladoria on 2026-10-19 the raid took Conservator (level 77) to 45%, wiped, and he reset to full health. The
  second try reached 42%, nobody damaged him for five minutes, and the raid walked past him with nothing to show.
- The epic encounters keep their full strength. Every named encounter, not only the final one, now waits until
  enough of the raid has gathered, and the raid goes in together. A wipe is when half the raiders in the fight fall,
  or the encounter heals back to 95% after being taken to 85% or lower.
- After a wipe the raid regroups. The fallen release to the hub and walk back, and the living rest and rebuff for at
  least three minutes. The raid goes in again once 60% of its roster stands at the gathering point, or 40% after 20
  minutes. The realm hears that the raid fell and is going in once more.
- After two failed attempts on the same encounter the raid is **Defeated** and goes home (a new outcome in the
  launcher's Herald). The named encounters it killed are listed in the record. Every realm hears it, like a victory.
- An encounter the raid is still standing around is not skipped because it took no damage. One nobody can reach or
  hurt is still set aside as before.
- Logs: `REALM_EXPEDITION_WIPE`, `REALM_RAID_WIPE`, and `attempt=`/`retry=` on `REALM_EXPEDITION_FINAL_ENGAGE`.

**The Epic Special Forces**
- Each realm has an elite guild, the **Epic Special Forces**. When a raid in an epic dungeon, Darkness Falls or
  Summoner's Hall wipes, up to 24 extra level 50s arrive at the raid's hub, in three parties of eight with the same
  endgame kit and full training as a level-50 /spawn companion. They walk in with the fallen and join the retry, and
  every realm hears that they answered.
- They are needed less as the realm wins. A dungeon with no record gets 24; each win there sends one party fewer next
  time and each loss one more, counted over that dungeon's last six results in the Herald. After three wins in a row
  nobody comes. An expedition defeated at that dungeon the last two times brings them from the start.
- They are the one exception to gamebots earning their armor. They live in memory only: they are never saved, never
  counted in the population or a guild roster, take no loot or coin (with only them nearby a drop stays where it
  fell), claim no server firsts, and leave when the raid ends. A restart leaves nothing behind.
- Dragon raids don't call them yet (they do from Beta 1.1.2, above).
- Logs: `REALM_RAID_SPECIAL_FORCES_ARRIVED`, `_NOT_NEEDED`, `_FAILED`, `_LEFT`, and `specialForces=` on `REALM_RAID_ENDED`.

**From the first 600-per-realm night (2026-10-19/20)**
- The regroup and the Epic Special Forces worked in the game: Galladoria's raid wiped at Conservator (taken to 9%),
  24 Special Forces arrived, and the retry killed him; it then beat Spindler Broodmother and Tine on their second
  attempts and was defeated at the Organic-Energy Mechanism. Albion killed Legion; Hibernia (with the Special Forces
  after a wipe at Summoner Roesia) and Midgard both killed Grand Summoner Govannon.
- **Rallies out of time go in short.** Three Albion rallies failed with 90/132, 101/149 and 103/150 staged while about
  90 more recruits were still on the road. A raid scaled to the roster now goes in at its deadline with half of what it
  needs (at least 40), and the rest follow it in (`REALM_RAID_SHORT_START`). Full 200 raids are unchanged.
- **Siege defenders follow the real army.** The defense share was taken of the army that marched (10-60 bots) while
  attacker reinforcements grew it to 70-105: Dun Bolg's defense was capped at 25 against 89, and attackers won every
  siege that finished. Defenders now answer the larger of the marched and the current army.
- A regroup after a wipe waits for 70% of the raid (was 60%) and at least five minutes (was three): the second try at
  the Organic-Energy Mechanism went in five minutes after 152 of 156 raiders fell and did worse than the first.

**From the overnight report (2026-10-20)**
- **Contested raids stay; the kill decides.** Two realms' raids can still be in Summoner's Hall (or Darkness Falls)
  together and fight each other. The final encounter's kill now goes to the raid that did the most damage to it
  (`REALM_RAID_FINAL_CREDIT`); the other raid ends "Beaten to it" and gets no loot. Midgard had been credited with
  Govannon while every drop went to Hibernians.
- **Tuscaran Glacier.** When an epic monster that is not the raid's target attacks the raid, the raid turns on it
  (`REALM_EXPEDITION_TARGET_SWITCH`): Elder Icelord Suttung killed 115 raiders while they waited at the Birghir muster.
  A muster that stays empty for 20 minutes is set aside and tried later (`REALM_EXPEDITION_MUSTER_UNREACHABLE`)
  instead of holding until the four hours run out.
- **Parties no longer pull epic monsters they didn't come for.** In dungeons an epic monster on the route was pulled
  like any corridor monster; eight-bot parties pulled Black Lady (level 65) in Marfach Cavern on the way to and from
  Summoner's Hall, and she and her Ogress adds killed 195. Now they go around it when the navigation mesh has a way
  outside its aggro, and otherwise walk past without pulling (`AUTONOMOUS_DUNGEON_EPIC_AVOIDED`). A raid inside its
  own dungeon still fights its encounters.
- **Raiders walking to their expedition alone bend around danger.** On the way to the Caer Sidi staging area 124
  level-50 raiders died, mostly to dread liches and ancient zombies, and each death sent them 110,000 units back to
  Fort Gwyntell; 25 to 37 were still walking when both rallies ran out of time.
- **Level-50 bots use danger memory.** Route bends around monsters far above the bot were for levels 1-49 only, and a
  bot now also remembers its own deadly places: three deaths within 1,200 units in three hours make a spot it bends
  around and picks no camp beside (`AutonomousRepeatDeathSpots`). A level-50 Animist had died 72 times to one glimmer
  pack near its Sheeroe Hills bind.
- **New bots level in their classic zones first.** No XP camps in the Shrouded Isles below level 10: level-1 Midgard
  parties were sent to Aegir's Landing, where scimitar cats (levels 18-21) killed level-1 bots 201 times in three hours.
- **Home defense comes first.** A warband attacking an enemy keep now breaks off to defend its own realm's keep
  when that siege has begun and the defense is under half its cap, as long as the attack keeps half its army
  (`RVR_HOME_DEFENSE_BREAKOFF`). Raids are untouched: their members are not RvR forces. Albion took Midgard's
  Blendrake Faste with no defender ever arriving while about 80 Midgard bots kept attacking Caer Sursbrooke.
- Committed attackers now actually re-check home defense every 30 seconds, also while mustering, marching or at
  the siege (the first version only checked between plans; in the final test Hlidskialf Faste fell with no Albion
  defense while Albion's army mustered against Dun Crimthain, and one solo bot was the only break-off): a warband in a shared siege never
  re-planned, so the break-off was never evaluated (Caer Hurbury fell in 13 minutes with no Albion defense while 62
  Albion bots attacked Dun Da Behnn).
- **No defense muster once the attackers are at the keep.** Defenders go straight in. At Blendrake Faste the first
  defenders formed 26 minutes into the siege, mustered for six minutes, never marched, and the keep fell.
- **Dragons drawn out of their lair go home.** A dragon takes no damage more than 2,000 units from its lair, but
  its tether is 2,500, so bots kept fighting it in between. Gjalpinulva stayed at 100% for an hour with 130-170
  Midgard raiders on her and killed 55. Bots now treat a dragon outside its lair like a boss outside its tether
  (stop, lead it home), and the raid forms at the lair instead of following the dragon out.
- **Out of the glimmer loop.** Repeat-death memory alone didn't stop it: the release went to the nearest bind
  (about 2,300 units from the pack) and the walk to Pheuloc's horse skipped the route checks. A bot that keeps dying
  in one place now releases to a bind at least 6,000 units from that spot, its walk to a horse bends around danger,
  and it won't board a horse that stands where it keeps dying.
- **Parties clear dungeon bosses.** None of the 178 named monsters in the classic and Shrouded Isles dungeons was
  ever a goal. Dungeons are not made more attractive: a party of three or more that has ground its dungeon camp for
  five minutes also goes for that dungeon's boss, once per task, when the boss is a kill target of a real quest within
  three levels of the party's (the quest table: Favonius Facilis for "Willing Sacrifice" at 15+, Te'Bui for "Lord Lauur's Wife"
  at 35+, the Muire brothers for "Rest in Peace" at 17+) or has its own drop table, and the fight suits the party
  (orange at most for 3-5 bots, red for six or more; below level 20 one level up is already orange). It returns to its camp when the boss dies or after 15
  minutes (`AUTONOMOUS_DUNGEON_BOSS_GOAL`, `AUTONOMOUS_DUNGEON_BOSS_RESULT`).
- **Torst no longer disappears.** His eddies' brain set its body's speed before it had a body; the error removed
  Torst from the world the moment he summoned them.
- **The raid schedule is a random roll.** Each scheduled raid is drawn at random from everything the realm has
  available (its dragon, epic dungeon, Summoner's Hall, Darkness Falls and the raid dungeons below), setting
  aside only the raid it last won. A raid that failed can come straight back. Before, the schedule went dragon,
  epic dungeon, neutral raid in turn, and never repeated the last raid it started.
- **Nine more raid dungeons.** Each realm also raids its other multi-group dungeons: Albion Krondon (Orylle), Avalon City
  (Dura'ek the Empowered), Crystal Cave (Xanxicar) and Hall of the Corrupt (Aidon the Archwizard and Lord Gildas); Midgard Trollheim
  (Nosdoden), Iarnvidiur's Lair (Iarnvidiur) and Dodens Gruva (Aros the Spiritmaster); Hibernia Tur Suil (Balor) and
  Marfach Caverns (Black Lady, Chieftain Caimheul and Avartack). They run like the epic dungeons: the way in is
  cleared for the first hour, then the named encounters, the final boss last; ordinary monsters stay down ten minutes
  while the raid is inside, and a wipe regroups with the Special Forces. Raid sizes are capped at 64-160 by dungeon.
  Each musters at the nearest service settlement (Caer Diogel, Wearyall Village, Bjarken, Hagall, Aalid Feie; the
  Catacombs dungeons by the way to Summoner's Hall use its hubs). On the installed navmeshes a path from each
  entrance to the final room was proven offline. Crystal Cave is entered only through Avalon City: its raid stages
  outside Avalon City and walks through it, the way a Summoner's Hall raid walks through Hall of the Corrupt (the
  path from each Avalon City entrance to the Crystal Cave portal was proven too). Not raided: Fomor City (its
  navmesh has no path from the entrance to Myrddraxis's chamber).
- **A raid by tight ground fills the slots that fit.** Krondon's first raid gathered 7 parties at Caer Diogel and
  waited 90 minutes for an eighth: beside its portal in Aldland only 7 formation slots in a row had a staging post,
  and the raid only ever asked for the next slot. A slot with no post is now set aside and the next one tried, and
  each slot widens its search ring after twelve tries (25 of 38 slots find a post by the Krondon portal, from 19).
- **An encounter that takes no damage stays set aside.** In Krondon the raid skipped Scurceol Hyrde (immune while
  his four Lyft Miht orbs live) and switched straight back to him because he was attacking it, every minute for over
  an hour. A skipped encounter is no longer picked again for attacking until its retry time.
- **A rally leaves its hub with half the raid after 30 minutes.** The Midgard Summoner's Hall rally recruited 102
  for a full 200 raid, never reached the 120 needed to leave early, and failed with nobody staged.
- **A full 200 raid also goes in short at its deadline** with at least half staged. At 900 bots per realm every raid
  is a full 200, and the Hibernian Darkness Falls rally failed with 170 staged (the late start needs 180).
- **A dungeon boss goal always logs its result** (`AUTONOMOUS_DUNGEON_BOSS_RESULT`), also when the party disbands or
  takes another task first; before, the night's only goal (Mutilator Zurabo) left no result.
- **Deleted turret pets were kept in the bots' turret list** until the owner's next pet upkeep (252 held at once).
- **Fixes from the first night on this build:** two healer crashes when a group was disbanded mid-check (a null
  resurrection scope and a null group in the cure check), and the launcher's status queue kept duplicate ids in its
  priority queue (65 to 5,079 overnight).
- **Native memory leak in pathfinding.** In every 600-per-realm run the server's memory outside the .NET heap
  grew about 250 MB an hour (3.3 to 6.1 GB overnight; the working set went from 5.4 to 10 GB). The pathfinder kept
  one native Detour query (about 0.7 MB) for every thread and zone and never freed it: each .NET pool thread that
  checked a town route built its own set, lost when the thread retired, and each game-loop worker kept one for
  every zone it had touched. Queries are now pooled per zone and lent for one call (at most one idle query per
  processor thread is kept per zone). `SERVER_MEMORY` also logs `privateMB`, `nativeMB`, `threads`, `poolThreads`,
  `handles` and the live and created query counts (`navQueries`, `navQueriesMade`).

**Checked**
- Automated: 3,223 server tests.
- Live, on the owner's PC (new client, 650 bots online), first 39 minutes of the new build against the same 39
  minutes of the earlier session:
  - Body language: 78 looks, 82 emotes and 12 greetings were sent to the player's client. Bots standing at the
    Camelot Realm Exchange were seen turning and gesturing. In the build before this one (without "standing around")
    nothing was counted in 20 minutes and bots at the Exchange stood still. No steps or cheers yet (no town break
    or ding happened near the player).
  - Solo bots fought far fewer oranges and yellows on their routes (45 and 133, from 163 and 475).
  - Deaths: level 50 solo tasks ending in a death fell from 35% to 27%; all tasks from 30% to 28%. Most deaths are
    bots of levels 1-9 in the starting zones, and about half of all deaths still happen before the bot reaches its
    camp, with no route threat logged before them. That is not fixed yet.
  - Groups: 13 PvE parties (11 before) and 72 waits that ran out (54 before); no clear change yet. About 15% of
    online bots were in a party at the end of the check (48 RvR, 47 PvE), so "98% of tasks are solo" overstated how
    alone bots are: solo bots start many short tasks.
  - No new server errors (390 at start-up, as before).
  - The chat fixes above came after the check and haven't been played yet.

## 0.37-se.1 — 2026-10-18

The Social Expansion on shadowofze's **0.37 / 0.37b** (with or without the 0.37c new client), with everything in
0.36-se.4. 0.36-se.4 stays the build for 0.36.

**Moving from 0.36**
- 0.37 is an update to a 0.36 game, and it applies only to the game's own files. `PREPARE-FOR-UPDATE.cmd`
  (new, in this build's folder) puts the released 0.36 server and launcher back from the installer's backups,
  whatever was installed over them, and checks each one. Then update the game, then run `INSTALL.cmd`. Your saves,
  bot guilds, bot memory and settings stay in place.
- If you install 0.37 fresh and bring your progress over instead, the game's transfer leaves out the Social
  Expansion's tables. `python tools\claude-version\carry_social_tables.py --from <old game folder> --apply` copies
  them (it backs up first and refuses until the game's own transfer is done).

**Guilds: the base game's and ours together**
- 0.37 lets you invite gamebots into your own guild (they show your guild name and emblem, and talk in your guild
  chat, but don't count for merit, keep claims or buffs). That is now the one way bots join a player's guild. With bot
  guilds on, a member of a bot guild answers your /ginvite for itself (a leader or officer stays, a busy bot or one far
  from your level says so) and leaves its bot guild when it joins yours.
- Bot-run guilds are unchanged. Their members now also wear their guild's emblem on cloak and shield (0.37's emblem
  code).
- A bot's answer to your /ginvite is said aloud when you are near, and otherwise sent to you (/send), since you can
  invite by name from anywhere.
- With bot voices on, bots in your own guild talk from the voices corpus too: their lines are answered in kind, and
  your /gu gets answers like /faction and /group. With voices off they keep 0.37's own lines.

**Bot chat**
- "what are you up to?", "wyd", "whats everyone doing?" are answered with what the bot is doing (camping its camp,
  heading somewhere, selling with full bags, standing around in town...), and "what is everyone up to tonight?" with
  plans. Before, they read as "how are you?" or as nothing ("lol what").
- Developers: our class `AutonomousBotGuilds` is now `AutonomousBotRealmGuilds`, because 0.37 added its own
  `AutonomousBotGuilds`. Database tables are unchanged.

**Tested**
- **Automated:** the full server suite on the merged code (3,176 tests), the installer tests with five new ones for
  `PREPARE-FOR-UPDATE`, and seven tests for `carry_social_tables.py`.
- **Live, scripted:** the merged server on a copy of a played save (cloud workspace, Linux), with a scripted player
  that typed in /gu and /faction and used /gc invite and /gc remove. A se.4 membership in a player guild moved to
  0.37's table at start; a bot guild member joined (and said goodbye to its bot guild), an officer and a level 50
  said no, an unguilded bot joined, /gc remove worked, guildmates answered /gu, and no new server errors.
- **Upgrade on a real install** (the owner's 0.36c game with se.4, Windows, 2026-10-18): PREPARE-FOR-UPDATE put the 12
  released files back, shadowofze's 0.37c update applied, and INSTALL.cmd installed 0.37-se.1. The first INSTALL refused the
  updated game (0.37's update removes the editions folder the installer checked); fixed before release, see below. The 27
  bot guilds and 617 memberships, the 300-bot roster per realm and the Social Expansion settings came through, and the server
  logged the same start-up errors as before the update.
- **In game, briefly:** logged in on the new client; guild chat greeted the character and answered "what are you up to?"
  with "not much, you?" and "the usual, killing stuff". Not played for long.

**Found in the owner's first play, then fixed**
- A level 50 in a level 50 RvR bot guild got no answer to "lfg" in /gu and /faction or to "where is everyone?": the chosen bots
  were in fights when their replies came due, and a reply to a player had no stand-in. Now a busy bot's reply goes to the
  next free bot (a bot you named still answers itself), a question to your own guild always gets an answer, and "where is
  everyone?" is answered with where the bot is ("Pennine Mountains", "at Black Mtns. South, decent xp"). Checked again in
  play: all three got answers.

**Installer fix found in that upgrade**
- INSTALL.cmd now recognises a game updated by a patch: the release is the newest docs/RELEASE-<version>.md in the game's
  manifest (the editions check remains for the 0.36 full downloads). Three new installer tests.

## 0.36-se.4 — 2026-10-18

Bot chat, reworked so the world feels alive and answers you. Same game versions as before (0.36 /
0.36b, with or without 0.36c). Install it over 0.36-se.3 or earlier, or a clean game. Most of this
needs **bot voices** on (the launcher's Social Expansion tab); what changes with it off is marked.

**Bots understand what you type**
- Short chat is read for what it is: "ty", "gz", "lol", "brb", "wb", "inc", "rez pls", "wts", "lfg",
  "ding 20", "is friar good?", "are you a bot?". Before, most of these read as real-world talk, and in
  /say they got no answer at all. On 260 test messages, 233 are now read right. *(Also with voices off.)*
- Saying "dumb question" or "this camp is garbage" no longer counts as an insult that bots remember;
  an insult has to be aimed at someone. *(Also with voices off.)*
- "where is the scout trainer" names the trainer and the zone. Place answers match whole words
  ("camelot" no longer finds a monster called Mel) and sound like a player, not a guidebook.
  *(Also with voices off.)*

**Answers that fit**
- Every line a bot starts says what kind of answer it invites. A boast gets "nice, show off", a
  complaint "ugh, same", a question an answer (a real one for places and trainers), and "brb dinner"
  nothing. Before, replies were picked by guessing the topic from the words, and about two in three
  didn't fit.
- "Fair enough. Safe travels." is gone. The bot who started a talk sometimes closes it in its own
  words ("ty", "k will try that", "heh yeah"), and never after a question.
- What you say gets answers of the right size: a hello or a ding in /faction gets a few, a "lol" usually
  none. "gz" is answered by the bot that just levelled. Only a bot that will really come and rez you
  says "omw".
- Replies take longer to type when they're longer. Unprompted lines come at uneven gaps (2-4 minutes
  in /faction), and about one bot in seven is a regular who talks more, so you get to know names.
- Typo-prone writers sometimes send a "*word" correction.

**Your group and your guild talk**
- Bots in your group chat in /group now and then, out of combat: pulls, breaks, loot, "where to next?",
  jokes. They said nothing before. Bot groups near you talk among themselves in /say; their
  "pull?" and "rez pls" no longer go to the whole realm in /faction.
- Bot guild chat comes from the corpus: about 140 lines by guild type instead of a dozen, with the
  no-repeat window. Guildmates answer a guild line when it asks something, not "haha true" to anything.
  *(With bot guilds on, voices on or off.)*

**More to say**
- The corpus has grown from 979 lines to 2,296. New lines: replies to short chat, replies for each kind
  of opener, closings, dungeon talk (there was none), four more lines for every class, battlegrounds,
  group and guild talk, the frontier and the capitals.
- Lines know where and when they are said. "tonight" reads "today" in the daytime, bedtime talk waits
  for the night, and "anyone want to duo?" isn't said by a grouped bot.
- Lines repeat far less: the no-repeat window is three days instead of six hours (six hours still apply when
  a kind of line runs out), and it is saved across server restarts. A player who plays every evening heard
  about a third of the last evening's lines again before.
- A question whose answering bot got pulled into a fight is answered by another bot. "is friar good?" is
  answered about friars, or in general, not about necromancers.
- Removed later internet phrases ("change my mind", "fight me") and fixed lines that named another
  realm's class.

**Found in a live test, then fixed** (the se.4 server with a scripted player typing in every channel; see
[SE4-CHECKS.md](docs/bot-voices/SE4-CHECKS.md))
- Bots in a capital no longer think they are in a dungeon ("lfg City of Camelot", "this place is a maze").
- Your own /group lines get answers from your groupmates. A group standing in a capital talks about selling and
  heading out, not "pull?" and "oom".
- Questions get answers of their kind: "anyone on?" ("here o/"), "how is everyone?" / "hows it going"
  ("good, slow night though"), "mind if i hunt here?", "cleric or friar?", "where to next?" ("your call"),
  "anyone know if x is ours?" ("last i saw, yes"), "what level is everyone?", "tell me your best death
  story", and "what does lfg mean?" (a short list of chat words). A plain "hi all" gets a hello.
- A reply doesn't ask what was just said ("paladin 6 here" -> "what lvl?"), and "tell me about it" answers
  complaints, not good news.
- A /faction scene and unprompted lines wait a moment after you speak, so they don't read as answers. Scene
  roles can require levels: a level 50 no longer says "i am not 50 yet".
- In /faction the realm answers you, not just your groupmate, and the same few bots don't answer everything.
- "where is camelot?" finds Camelot; trainer answers name the nearest trainer.

**Tested**
- **Automated:** the full server suite (3,144 tests, about 190 for se.4) and the corpus checks pass locally.
- **Live, scripted (not a person playing):** five sessions on the real server with a scripted player, about
  100 minutes in all; what it found is above and in [docs/bot-voices/SE4-CHECKS.md](docs/bot-voices/SE4-CHECKS.md).
- **In game:** not played yet.

## 0.36-se.3 — 2026-10-18

Fixes from a three-hour playtest (a new Albion Scout from level 1 to 5, every feature on). Same game
versions as before (0.36 / 0.36b, with or without 0.36c). Install it over 0.36-se.2, 0.36-se.1 or a
clean game.

**Bot chat**
- A bot no longer seems to answer its own /faction line. Its closing "Fair enough. Safe travels." is
  said only after somebody actually replied; before, it went out even when every reply was dropped.
- A bot in a group no longer asks in chat who wants to group "just for the company".
- Asking for a guild below the level bot guilds take members ("lf guild" at level 3) gets an answer
  saying when to come back, once every 10 minutes, instead of silence.
- "where are you?" by `/send` gets the bot's zone, level and class.

**Logs**
- The rez-ask line is written only when a player could have answered it, and a bot's normal release
  is logged at Debug. Together they were about 8% of the playtest's server log.
- `AUTONOMOUS_GROUP_MEETUP_DEATHS` gives the real wait; it always said 900 seconds.

**Tested**
- **Automated:** the full server suite on CI, with new tests for each fix.
- **In game:** the playtest that found these is in the docs; the fixes themselves haven't been played yet.

## 0.36-se.2 — 2026-10-17

Fixes from an audit of the first release. Same game versions as before (0.36 / 0.36b, with or
without 0.36c). Install it over 0.36-se.1 or a clean game.

**Docs said too much**
- "With everything off it plays exactly like 0.36" wasn't true. Talking to bots by name and two
  bot fixes are always on; the README, the zip's README.txt and the docs now say so.
- New [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md): what's always on, what hasn't been seen in game
  yet, and known problems. The zip ships it instead of the internal release plan.
- The README now has **Getting started** (make bots first, which switches to try, where to check)
  and **Undo and uninstall**. It also says plainly that testing used the 0.36c new client only.

**Removing bot guilds**
- `python tools\claude-version\set_bot_guilds.py --purge --apply` (server and game closed) removes
  the guilds bots formed: it releases keeps they claimed, ends alliances they led, moves your
  characters in a bot guild back to the starting guild and deletes the bot guilds. It backs up the
  database first, refuses while the game is running, and changes nothing on a second run. Without
  `--apply` it only shows what it would do.
- With bot guilds off, the server no longer rewrites bot guilds' ranks and dues at every start.

**Fixes**
- `/invite <name>` no longer pulls a bot out of a siege, a relic run, a realm event or a realm raid,
  or off a stable route; you're told why.
- The log line on every bot death (with a stack trace) is now written only when the Living Server's
  rez part is on, and the stack trace only for moves other than the bot's own release.
- Founding a bot guild and recording heard debates now write to the database under the shared
  write lock, and the debate history is written in batches off the game loop.
- A bot guild's invite box is shown from the right thread.
- The voice lines load at server start (in the background) instead of the first time a bot speaks.
- Bot memory and nemesis rows for deleted characters are removed at start, and old one-kill
  nemesis rows every 6 hours instead of only at start.

**Tested**
- **Automated:** the full server suite on CI, plus 8 Python tests for the purge. The purge was
  also run on a copy of a played database (15 bot guilds, 3 alliances, one character moved back).
- **In game:** not yet. These changes haven't been played.

## 0.36-se.1 — 2026-10-17

The first release, for Offline DAoC **0.36 / 0.36b** (with or without the 0.36c new client). It
replaces the server (`GameServer.dll`) and the launcher (which gets a **Social Expansion** tab).
Every feature starts **switched off**. (Corrected in 0.36-se.2: talking to bots by name and the
two base-game bot fixes below are always on.)

**Features**
- **Bot guilds.** Each realm's bots form guilds with a purpose (leveling, social, RvR, raiding,
  crafting). They wear guild names, talk in /gu, group with guildmates and recruit you when you
  ask "lf guild", even from your realm's starting guild. Guilds claim and defend keeps, pay dues,
  earn merit, buy guild buffs and form alliances. You can also /ginvite bots into your own
  guild. See [docs/BOT-GUILDS.md](docs/BOT-GUILDS.md).
- **Bot personalities.** Six types (Leveler, Socialite, Daredevil, Explorer, Crafter,
  No-lifer) and five traits tilt how each bot spends its time. `/personality` on a bot shows
  its type.
- **Bot voices and /faction debates.** 979 lines written for 2002–2003, chosen by realm, class,
  level, situation and personality, in five writing styles. 296 multi-bot /faction scenes, one
  every 30–45 minutes while you play. A busy speaker is waited for, so scenes play to the end.
  See [docs/BOT-PERSONALITIES.md](docs/BOT-PERSONALITIES.md).
- **XP by level range.** Separate player and bot rates for level bands. See
  [docs/XP-BY-LEVEL-RANGE.md](docs/XP-BY-LEVEL-RANGE.md).
- **Living Server.** Bots remember you (greetings, thanks, gossip by name). Dings get grats, and
  server firsts are announced. Dead bots ask you for a rez, and passing healers rez you. Frontier
  bots call enemy groups in /faction. Bots keep nemesis records. Bots log on and off on a
  schedule, take "brb" breaks and now and then go linkdead. The **Camelot Herald** is a news page
  of your world. Each part has its own switch. See [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).
- **Talking to bots by name.** `/send <bot>` reaches an online bot of your realm, and it answers
  with a tell. Ask to join and it tells you to invite it: you lead, bots join. `/invite <name>`
  invites a nearby bot (same region, within 6,000 units). `/target <name>` targets a nearby
  friendly bot.

**Fixes to the base game's bots** (also with every feature off)
- A bot that dies 3 times on its way to its group's meetup now counts as a no-show. Before, a
  low-level group could spend the full 15-minute wait dying (about 70 deaths in one case).
- Dead bots no longer jump to their spawn point the moment they die. Their corpse stays where it
  fell until they release.

**New client (0.36c) fixes, in the source only.** The target window's FACE, FOLLOW and STICK
buttons, `/target <name>` and `/assist` now work, and a second `/stick` or `/follow` turns it off.
These are in [source/new-client](source/new-client); this release's zip doesn't include new-client
files.

**Tested**
- **Automated:** 2,911 server tests pass on CI.
- **In game** (owner's PC, new client, 300 bots per realm, every feature on):
  - Seen working: guild recruiting and alliances, debates, frontier alarms, breaks, bot memory and
    gossip, rez asks, a bot rezzing you, grats and server firsts, tells, /invite and /target by
    name, and XP by level for players. Bot XP by level raises bots' XP in game, but the exact
    factor wasn't measured; see docs/XP-BY-LEVEL-RANGE.md.
  - Not yet seen in game: keep claims, guild merit, linkdeads, nemesis chat lines, you rezzing a
    bot, and the meetup fix.
  - The original game client wasn't tried.
