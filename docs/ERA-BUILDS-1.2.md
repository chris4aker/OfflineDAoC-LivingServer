# Era builds in 1.2: design and build plan

Owner: Chris. Decided 2026-11-17 07:58: Living Server 1.2 moves about a week, to ship about 24 November, and takes in
**era builds**: the gamebots' spec builds and class shares as players had them in 2002-2003 (patch 1.65, to
8 October 2003). This is the design and the build plan; no production code is written yet. It finishes part 1 of
[BOT-BUILDS-AND-RVR.md](BOT-BUILDS-AND-RVR.md) ("The era's real builds, weighted") and the [RELEASE-TRAIN.md](RELEASE-TRAIN.md)
row "Era builds from the Wayback research (#76's fetcher; #55's build weights and class shares, provisional until
checked against the period pages)".

Inputs: the research commit `ca3d2ee77` on `origin/feature/era-builds` (PR #55, closed unmerged:
`docs/research/ERA-BUILDS-1.65.md` and `docs/research/bot-builds.draft.json`, 39 classes, 106 builds), the Wayback
fetcher (`tools/wayback-fetch`, merged in #76), the local Wayback scrape of 2026-11-13, and the code on
`integration/1.2` at `b5632aca0`.

The owner's review is [the last section](#the-owners-review). A one-page sheet of the shares and the top builds, for
approving from a phone, goes with this design (`ERA-REVIEW-SHEET.md`, in the session's scratchpad, not in Git).

## What it does

- **New gamebots are born in the era's class mix.** The launcher's `+ Lv.1`, `+ Lv.20` and `+ Lv.50` batches draw
  each bot's class from the realm's era share (Cleric 12% of Albion, Reaver 3%) instead of evenly (1 in 14).
- **New gamebots take an era build for life.** At its first load a new gamebot draws one of its class's period builds
  by weight (a Mind Sorcerer 44/31 is more likely than a Matter Sorcerer), with a little per-bot variation, and keeps
  it like today's lifetime build. A `+ Lv.20` or `+ Lv.50` bot still arrives trained in it; a `+ Lv.1` bot trains it
  level by level at its trainer, as today.
- **Variety stays human.** Weights are draws, not the one best build: 10% of new gamebots take one of today's
  builds (the odd spec), and each era build moves up to 2 points between its lower lines per bot.
- **Existing bots keep their class and their build** (recommended; [reasons](#existing-bots-keep-class-and-build)).
- **Every unsourced number stays labelled** in the data (`weightSourced`, `sharesSourced`), in the startup log line
  and in the release notes ("estimates from 2002-2003 forum posts").

It does not change gear (gamebots still start without armor and earn it; `+ Lv.20` / `+ Lv.50` arrivals keep their
current gear rules), realm abilities, races, spells, the class AI, any raid code, or any companion or player.

### Who it touches

| Who | Touched? | How |
|---|---|---|
| Autonomous gamebots added after the update | **Yes** | Class from the era share (launcher); lifetime build from the era data (server, first load) |
| Gamebots already in a world with a saved build | No | Class and build kept |
| Gamebots queued by an earlier launcher but never loaded (no saved build yet) | Build only | Their class was already drawn evenly; they take an era build of that class at first load |
| Companions (`/spawn`, `/raid`, saved `/bot`) | No (recommended, D2) | Keep today's builds; `/spawn <class> <spec>` unchanged |
| Epic Special Forces, No Lifer buffbots | No | In-memory raid helpers and buff specs keep their own builds (raid code is frozen) |
| Real players | No | |

## How bots get their class and spec today (checked on `b5632aca0`)

| Step | Where | What it does |
|---|---|---|
| Class of a gamebot | `source/tools/OfflineDaoc.Launcher/BotCharacterGenerator.cs` `Generate`, called by `MainForm.GenerateBotCharacters` (`+ Lv.1` / `+ Lv.50` / `+ Lv.20` batches of 1, 10 or 100) | `pool[Random.Shared.Next(pool.Length)]`: every class of the realm equally likely; class 63 left out when `enable_sluaghbinder` is False. The row goes into `offline_world_bots` with no build. **The server never creates gamebots itself** (only in-memory Epic Special Forces and buffbots). |
| Same, server copy | `bots/autonomous/AutonomousBotIdentityGenerator.cs` `Generate` | The same even draw (`random.Next`); used by tests only. `GenerateForClass` serves Epic Special Forces, buffbots and `/spawn`. |
| Build family of a gamebot | `bots/GameBot.cs` (record constructor, about line 2700) | `BotSpec.ChoosePersistentSpecialization(class, BotId)`: a hash of the bot ID picks one of `GetSpecializationChoices(class)` evenly |
| Build variant | `bots/specs/<Realm>/<Class>.cs` | Each class file picks a variant of the family with `Util.Random`: spec lines with a cap and a `levelRatio` (how fast the line rises while levelling; 0 = only at 50), weapon types and `SpecType` |
| Locked for life | `bots/BotLifetimeBuild.cs` | The first load writes the plan to `offline_world_bots.SerializedBuildPlan` as JSON; every later load restores it (`Restore`), so the random variant never re-rolls. An old bot without a plan gets one aligned to the weapon lines it already trained (`AlignWithInvestedWeapons`). |
| Training | `GameBot.SpendSpecPoints`, `TrainPendingSpecializations`; `AutonomousWorldBotController` trainer visit | Points by level (`SpecPointsMultiplier`), spent line by line following the ratios and caps, at the real class trainer, at a task break. `+ Lv.20` / `+ Lv.50` bots never saved arrive trained (`ArrivesTrained`, PC 2026-11-11). Log: `AUTONOMOUS_TRAINING` (diagnostics switch). |
| Respec | none | No bot respec exists. The Bonedancer maintenance re-plan (`EnsureBonedancerFocusedSpecPlan`) skips any bot with a saved plan. |
| Realm abilities | `bots/autonomous/AutonomousBotRealmAbilities.cs` | One fixed Atlas build per archetype (melee, caster, healer, archer) by class, bought at the trainer. Not build-aware. |
| Companions | `GameBot(owner, ...)` constructor; `TemporaryGroupSpecCatalog` | `/spawn` draws a family evenly (`ChooseRandomSpecialization`) or takes the asked one; a saved `/bot` companion's profile stores no build, so it re-rolls at every load (found today; not changed by this plan). |
| Roles | `bots/BotPartyRoles.cs`, `RvrRoles`, `RvrRosterTemplates.cs` | Group, raid and RvR roles are by **class**; crowd control in the RvR field is read from the spells a bot really has (`RvrFieldFacts.CanMez`). |

## The research data, checked today

`bot-builds.draft.json` (format: `meta`; `realms.<Realm>.classShareRvR` / `classShareLevelling` / `sharesSourced` /
`sourcedSnapshot`; `classes.<Class>[]` with `label`, `role`, `use`, `weight`, `specs`, `sourced`, `weightSourced`,
`forumMentions`, `sources`, `sourceDates`). Checked against the code with a script (point budgets from each class's
`m_specializationMultiplier`, the same formula as `GetSpecPointsForLevel` with the half levels above 40):

- Every spec line name is a real `Specs` constant; weights sum to 1 per class and shares to 1 per realm.
- **12 builds need more points than a level 50 has** (by 9 to 759): Minstrel (both Instruments builds; 50/50/29 is
  759 over), Theurgist Earth, Wizard Ice, Shadowblade (two), Runemaster and Spiritmaster Suppression, Champion
  Valor/Blades, Ranger Archery, Eldritch Mana, Mentalist Mentalism. Posted totals likely include +skill items from
  "SC templates"; the posts don't say. Proposed: trim the lower lines to the budget (D7).
- **7 builds leave 260-700 points unspent** (Paladin two-handed, Mercenary Thrust, both Infiltrator CS builds, Scout
  melee, Warrior Axe/Shield 50, Warden Nurture/Regrowth); the posts listed only the main lines. Proposed: the rest goes
  to a named line (D7; every fill is an estimate).
- **One weight contradicts its own source text:** Wizard Ice PBAE 35% from one post, while the threads say Albion was
  short of Ice wizards and full of Fire wizards. Proposed 15% (D8).
- **Midgard's draft shares stray from the one sourced table** (level 50s on Excalibur, 2003-01-22): Thane 5% against
  10.6% counted, Runemaster 7% against 10.8%, Shaman 9% against 5.0%. Proposed A′ (D3).
- Thin classes (3 posts or fewer per build): Wizard, Necromancer, Valewalker, Animist, Blademaster, and single builds
  elsewhere (listed in the research doc, section 4). Valewalker and Animist have one era build each.
- The sources are European (GOA ran US patches weeks to months late). No US 2003 spec or census source is cited.

### Can the Wayback tooling source class shares now?

Not for 1.2:

- **The cloud still can't reach the archive.** A CDX request from the build container was reset today (08:05); the
  fetcher runs on the owner's PC only (`tools/wayback-fetch/README.md`).
- **What the local scrape (2026-11-13) holds has no class counts.** The `7-realms-classes` probe's Herald
  `/realms/Merlin/` pages (2002-2003) are top-25 rankings, guild lists and keep owners with character names. The
  fetcher's own rule excludes them ("no character, roster or population harvesting"). `populationbalance.php` and
  `newfrontiers/allserverstats.php` are 2004 captures (New Frontiers, after the era) of realm and top-player figures.
- **The `3-builds-specs` pack has 21 class-guide captures dated 2002-2003** (Classes of Camelot's Albion class
  pages, Allakhazam's Eldritch, Ranger, Shaman, Hero, Warden and Blademaster guides). They describe spec paths in
  words, not point splits or counts. They can check the build list qualitatively (WP0), not weigh it.
- **Where counts were:** the 2003 census sites (Camelot Seer at `daoc.gamescape.com`, `duskwave.com` general stats),
  quoted in the research's June 2003 fragments. They publish aggregate class counts, no characters. A new PC job
  (`c-class-census.json`: CDX listing first, then at most 20 pages, January-October 2003) could source the shares,
  **if the owner allows aggregate class counts under the README's rule** (D10). VN Boards and Allakhazam class boards
  give builds and opinions, not counts. Catacombs-era pages (after December 2003) stay out.
- Recommended: run it after 1.2; if it finds counts, the shares change in a 1.2.x data update (WP0's format makes that
  a data-only change).

## Design

### The data file

`source/server/GameServer/bots/autonomous/data/era-builds-165.json`, embedded in `GameServer.dll` and, as a linked
embedded resource, in the launcher (the pattern the lore data already uses: `data/lore/**/*.json` in both projects).
No file on disk to edit or lose; a change ships as a patch like any other. Built from the draft by WP0 and changed
only through the owner's review.

```json
{
  "schema": 1,
  "era": "DAoC 1.65 (to 2003-10-08), Classic + Shrouded Isles",
  "status": "approved 2026-11-xx",
  "offMetaPercent": 10,
  "jitterPoints": 2,
  "realms": {
    "Midgard": {
      "classShare": { "Healer": 0.15, "Skald": 0.12, "Thane": 0.08, "...": 0 },
      "sharesSourced": false,
      "shareBasis": "estimate; moved toward the Excalibur level-50 count of 2003-01-22",
      "sourcedSnapshot": { "date": "2003-01-22", "source": "https://...", "counts": { "Skald": 157 } }
    }
  },
  "editionClasses": {
    "Sluaghbinder": { "realm": "Hibernia", "share": 0.05, "sourced": false,
                      "note": "custom class, not in 2003; only while classes/enable_sluaghbinder is True" }
  },
  "classes": {
    "Armsman": [
      { "id": "arms-pole-slash", "label": "Polearm/Shield hybrid", "role": "tank", "use": "rvr",
        "family": "TwoHandHybrid", "weight": 0.40, "weightSourced": false,
        "specs": { "Polearm": 50, "Shields": 42, "Slash": 39, "Parry": 6 },
        "posted": { "Polearm": 50, "Shields": 42, "Slash": 39, "Parry": 6 },
        "change": null,
        "forumMentions": 16, "sources": ["https://forums.freddyshouse.com/..."], "sourceDates": ["2003-07-11"] }
    ]
  }
}
```

- `id`: stable, saved with the bot's plan and logged. `family`: an `eSpecType` name, so the class file's weapon and
  equipment rules apply unchanged (`SetWeapons`, `BotWeaponStats.MatchesBuild`, starter gear). The weapon types
  come from the spec lines (`Polearm` → `PolearmWeapon`, the reverse of `SkillBase.ObjectTypeToSpec`).
- `specs`: what bots train; `posted`: what the posts said; `change`: why they differ (`"trimmed to budget"`,
  `"filled: rest to Parry"`), so a trim or fill is never presented as sourced.
- `levelRatio` is not in the data: the loader derives it (top line 1.0; lines of 35+ at 0.6-0.8 by cap; 11-34 at
  cap/50 × 0.6; 10 or less 0, trained at 50), the shape of today's class files. A build may give `ratios` to override.
- `role`: the draft's words (tank, healer, healer-cc, cc-mezzer, pbae, support-pbt, assist-dd, pet, stealther...),
  saved with the plan for WP5 and the log.
- One class-share table per realm (D4). The draft's levelling table stays in the research doc only.

**Validation, at load and in a test:** every line exists for the class and is trainable; every `specs` set fits the
level-50 budget; weights per class and shares per realm sum to 1 (±0.005); every Classic + SI class has at least one
build; no Catacombs class; `family` is in the class's `GetSpecializationChoices` and its weapon setup matches the
lines (a `DualWield` family has a Dual Wield, Left Axe or Celtic Dual line, a shield family has Shields 20+). A build
that fails is dropped and logged (`ERA_BUILDS_INVALID`); a class left with none, or a file that fails to parse, falls
back to today's picker for that class and says so. The bots never stop loading over data.

### The class draw (launcher, and the server's copy)

A shared, pure `OfflineDaoc.Shared.EraBuildCatalog` (one file under `bots/autonomous/`, linked into the launcher like
`EraBotNames.cs`) reads the resource and offers `DrawClass(realm, allowSluaghbinder, random)`. The launcher's
`BotCharacterGenerator.Generate` uses it when `bot_era_class_shares` is True (read from the world's `ServerProperty`
row the way `SluaghbinderEnabled` is; a missing row means True), otherwise today's even draw. Races, names and start
locations follow the class as today. The server's `AutonomousBotIdentityGenerator.Generate` uses the same draw so
tests and any later server-side creation agree.

### The build draw (server, a gamebot's first load)

In the record constructor, where `ChoosePersistentSpecialization` is called today, when `bot_era_builds` is True and
the bot is a persistent gamebot (not an Epic Special Force, not a buffbot):

1. **A saved plan exists:** restored exactly as today (`BotLifetimeBuild.Restore`). Nothing else happens.
2. **No saved plan and no trained line above 1:** draw from the class's era builds by weight, seeded by the bot ID
   (a crash before the first save draws the same build again). With `offMetaPercent` (10) the draw instead takes
   today's class-file builds. Then up to `jitterPoints` (2) levels move between the build's lower lines, inside the
   budget, also seeded. The plan is saved at once, as today.
3. **No saved plan but trained lines** (an old world from before lifetime builds): the era build whose two highest
   lines match the bot's two highest trained lines; none matches → today's picker with `AlignWithInvestedWeapons`.
   No trained point is ever taken back.

Special classes keep their guards: the Savage keeps `AlignWithPersistedSpecs`; the Bonedancer's focused re-plan only
runs without a saved plan, so an era plan saved at first load stays; the Sluaghbinder has no era builds and always
takes its own class file. `BotSpec` gains two fields, `EraBuildId` and `EraRole` (empty for today's builds); the
plan's JSON keeps reading old plans, and `Restore` copies the two fields.

### Existing bots keep class and build

Recommended (D1), for five reasons:
1. **It is the design already chosen.** Part 1 of [BOT-BUILDS-AND-RVR.md](BOT-BUILDS-AND-RVR.md) (2026-11-01): "New
   bots draw their class and build from those weights; existing bots keep theirs." The lifetime build exists so a
   bot's build never changes under it.
2. **People rarely respecced in 2003.** A full respec needed a respec stone or a level gate; a bot that keeps its odd
   2002 spec is what a server had.
3. **The world's class mix wouldn't change anyway.** Classes are kept, so a respec only swaps builds inside an
   even-mix world: all of the risk for part of the effect.
4. **There is no bot respec code.** Writing one means untraining lines, re-buying realm abilities and swapping
   weapons and spells mid-session, the inputs raid tactics read, a week before shipping, under a raid freeze.
5. **The gate's results stay meaningful.** The owner's world's bots won the raids and RvR rows on the gate; with
   their builds kept, the era code changes nothing they do.

Getting the era mix on an existing world is the owner's choice: retire bots with the launcher's purge and add new
batches (those bots' progress is lost). Not recommended for the test world before 1.2 ships. A rare, optional respec
at a trainer for level-50 bots ("a few respecced each month") can come in 2.0 with the respec rules.

### The Sluaghbinder edition switch

`ENABLE_SLUAGHBINDER` (`classes / enable_sluaghbinder`) stays the edition switch. False (the 0.37 edition): the class
is not drawn and Hibernia's era shares are used as they are. True (0.37b): the Sluaghbinder gets its
`editionClasses` share (5% proposed, D6) and the other Hibernian classes are scaled to 95%; its builds stay its own
class file (`SluaghbinderBotSpec`). The `classes-off` smoke session checks it (played check 4).

### Raid and RvR: what assumes which classes

| Code | Assumes | With era shares |
|---|---|---|
| Realm raids (`AutonomousRealmRaid`, `RealmRaidRecruitmentPolicy`) | Raids take whole level-50 PvE parties; parties form by role (`BotPartyRoles.CanFill`: tank, healer, buffer, attacker, by class) | More healing classes per realm (Albion 21→27%, Midgard 17→24%, Hibernia 15→20% of new bots), tanks about the same. Raid code untouched (frozen since #240). |
| Raid tactics (`EpicEncounterTactics.*`) | Healers by class (`IsHealingClass`, including Paladin, Friar, Warden, Shaman) | Unchanged; a Smite Cleric still counts as a healer, as today |
| Epic Special Forces (`AutonomousEpicSpecialForces`) | Fixed eights per realm (two Clerics, two Healers, two Druids...) | Not touched (excluded from era builds) |
| RvR eights (`RvrRosterTemplates`) | Crowd-control slot by class: Albion Sorcerer, Midgard Healer, Hibernia Bard | Today any Healer fills Midgard's slot, Mend healers too (no mez). WP5 prefers a CC build (D9). |
| RvR field (`RvrFieldFacts`, `RvrStrength`) | Mez, root and stun read from spells; healers by class (Cleric, Druid, Healer) | Works with any build |
| Pet cap (`AutonomousRvrPetCap`, `rvr_group_pet_cap`) | Pet classes by class | Fewer pet classes to cap: Albion 21→14%, Midgard 17→8%, Hibernia 15→10% of new bots |
| PBAE | The area-spell engine (merged from shadowofze's `main`) | More PBAE builds (Suppression Spiritmasters, Mana Eldritch and Enchanters); the PvE add rules already gate them |

**What this means for the gate:** on the owner's world nothing changes for raids (bots keep class and build). On a new
install every bot is era-built, and no raid has been fought by an era-mix raid. The raid bar's results come from the
owner's world and stay that world's; the release notes must not claim them for an era-mix world (AGENTS.md). One raid
per realm on an era-mix world is offered as played check 6 (D12).

### Not in 1.2

Era realm-ability picks (no research yet), era races, respec, companion builds, `/lfg pbae` and the build shown on
`/target` (part 4 of BOT-BUILDS-AND-RVR), the census job (D10).

## Work packages

Each package is its own branch and PR into `integration/1.2`, tests in `source/server/Tests` or the launcher tests.
"Tests only" until seen in the owner's PC log.

### WP0 — The data file (0.5 day)

- **Files:** `source/server/GameServer/bots/autonomous/data/era-builds-165.json` (new; from the draft plus the owner's
  review), `source/server/GameServer/GameServer.csproj` (embed), `docs/research/ERA-BUILDS-1.65.md` and
  `bot-builds.draft.json` brought over from `ca3d2ee77` unchanged (the record of what the posts said),
  `source/tools/make_era_builds.py` (draft → data: families, ids, trims, fills, the approved shares; prints every
  change for the review).
- **Check first:** the 21 period class guides in the local scrape (`3-builds-specs`, 2002-2003) against the Albion and
  Hibernia build lists, for any build they call rare or wrong. Findings go in the research doc, not into weights.
- **Tests:** `UT_EraBuildData` (every rule of [Validation](#the-data-file), every class covered, every `change` set
  where `specs` differ from `posted`, `weightSourced`/`sharesSourced` present everywhere).
- **Settings, log tags:** none.

### WP1 — The shared catalog (0.5 day)

- **Files:** `source/server/GameServer/bots/autonomous/EraBuildCatalog.cs` (new, `OfflineDaoc.Shared`, pure: load,
  validate, `DrawClass`, `DrawBuild(class, seed)`, `Jitter`, `NearestBuild`, budget), linked into
  `OfflineDaoc.Launcher.csproj` with the JSON as an embedded resource.
- **Tests:** `UT_EraBuildCatalog`: draws over 100,000 seeds match the shares and weights within 0.5 points; a seed always
  gives the same draw; jitter stays inside the budget and the caps; Sluaghbinder in or out with the switch; a broken
  file or build falls back per class.
- **Log tags:** `ERA_BUILDS_LOADED version=1 status="approved 2026-11-xx" classes=39 builds=106 dropped=0
  sharesSourced=0/3 weightsSourced=0/106 sluaghbinder=on|off` (server start); `ERA_BUILDS_INVALID class= build= reason=`.

### WP2 — Launcher batches draw the era class mix (0.5 day)

- **Files:** `BotCharacterGenerator.cs` (`Generate` takes the catalog draw), `MainForm.cs` (reads
  `bot_era_class_shares` beside `SluaghbinderEnabled`), `SocialExpansionSettings.cs` and
  `SocialExpansionSettingsControl.cs` (the switch, on in `Defaults` and `Recommended`),
  `AutonomousBotIdentityGenerator.cs` (same draw).
- **Tests:** launcher tests: 10,000 identities per realm within 1.5 points of each share; no class 63 with the class
  off; the even draw when the switch is False; `UT_AutonomousBotIdentityGenerator`, `UT_SluaghbinderEditionSwitch`
  extended.
- **Setting:** `bot_era_class_shares` (True).
- **Log:** the launcher has no server log, so the census line of WP3 proves it.

### WP3 — Gamebots take an era build for life (1 day)

- **Files:** `GameBot.cs` (record constructor: the three cases above), `BotSpec.cs` (`EraBuildId`, `EraRole`, a
  factory from a catalog build), `BotLifetimeBuild.cs` (`Restore` copies the new fields), `ServerProperties.cs`
  (settings), `AutonomousWorldBotController.cs` (the training status names the era build).
- **Tests:** `UT_EraLifetimeBuild`: a new record gets an era plan, saved; a saved plan (today's or era) is restored
  unchanged, never re-drawn; a bot with trained lines gets the nearest build or today's picker and keeps every trained
  point; Savage, Bonedancer, Necromancer (`UT_NecromancerLifetime`) and Sluaghbinder cases; Epic Special Forces and
  buffbots untouched; a `+ Lv.50` arrival trains to the build's points exactly (`UT_Level50BotsArriveTrained`);
  starter gear follows the build's weapons (`UT_StarterGearForTrainedArrivals`); `bot_era_builds` False gives today's
  builds.
- **Settings:** `bot_era_builds` (True), `bot_era_offmeta_percent` (10; 0 = era builds only).
- **Log tags (not behind a diagnostics switch, at most one line per bot for life):**
  - `ERA_BUILD_ASSIGNED bot="Name" id=123 class=Sorcerer level=50 build=sorc-mind-body label="Mind/Body (AE mez)"
    role=cc-mezzer draw=era|offmeta|nearest|legacy jitter="Body Magic+1,Matter Magic-1"`, when a plan is first saved;
  - `ERA_BUILD_TRAINED bot="Name" id=123 build=sorc-mind-body level=50 specs="Mind Magic:44,Body Magic:31,..."
    match=yes`, when an arriving bot finishes its arrival training, or any bot first trains at 50;
  - `ERA_BUILD_CENSUS realm=Midgard bots=900 era=120 kept=780 Healer=139(15.4%/15%) Skald=...`, once per server
    start after the roster loads (counts by class with the target share; `era` = bots with an era plan, `kept` =
    with an earlier plan).

### WP4 — Release plumbing and notes (0.5 day)

- **Files:** `source/tools/release-server-settings.json` (the three switches; `test_release_server_settings.py` checks
  they match the code defaults), `docs/LIVING-SERVER.md` (a section on era builds and its switches),
  `docs/RELEASE-NOTES-1.2.md` (the row, with "estimates" and "new bots only"), `docs/PLAYED-CHECK-1.2.md` (the rows
  below), `docs/RELEASE-TRAIN.md` (the row moves from 2.0 to 1.2), `docs/BOT-BUILDS-AND-RVR.md` (part 1 points here).
- **No `LivingServerSettingsUpgrade` line:** every key is new, so a world without the row gets the code default
  (True) at its first start; no existing default changes.

### WP5 — The RvR crowd-control slot prefers a crowd-control build (0.5 day; D9)

- **Files:** `RvrRosterTemplates.cs` (a candidate whose `EraRole` is `healer-cc`, `cc-mezzer` or `cc-root` ranks first
  for the CrowdControl slot; then today's order), `AutonomousBotGroupCoordinator.RvrTactics.cs` (`RVR_PARTY_FORMED`
  names the role: `cc=Healer(healer-cc)`).
- **Tests:** roster template tests with mixed Healer builds; bots without an era role keep today's ranking.
- **Setting:** `rvr_roster_build_roles` (True). Not raid code; RvR field code merged in #302-#304.

### WP6 — Played checks on the owner's PC (two sessions)

Rows for [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md), each on the era candidate's build, from the owner's log:

1. **New bots are era-built.** A test copy: `+ Lv.50 ×100` per realm, start. 300 `ERA_BUILD_ASSIGNED` lines;
   about 10% `draw=offmeta`; `ERA_BUILD_TRAINED match=yes` for each.
2. **The class mix.** Three batches of 100 per realm (`+ Lv.1`, `+ Lv.20`, `+ Lv.50`), restart: `ERA_BUILD_CENSUS`
   over the new bots has each class within 4 points of its share (2 standard deviations at 300 bots).
3. **Existing bots unchanged.** The owner's world after the update: `ERA_BUILD_CENSUS kept=` equals the roster, no
   `ERA_BUILD_ASSIGNED` for a bot with a saved plan; three named bots show the same spec levels before and after
   (trainer window or `/target` info).
4. **Class-off edition.** The class-off smoke session on a 0.37 world: `ERA_BUILDS_LOADED sluaghbinder=off`,
   `Sluaghbinder=0` in the census after a `+ Lv.50 ×100` Hibernia batch.
5. **Battleground.** Thidranki with `+ Lv.20` era bots: `RVR_PARTY_FORMED` lines with a CC role filled; the pet count
   chasing the player against the 2026-11 numbers (BOT-BUILDS-AND-RVR's "How we'll know").
6. **One raid per realm on an era-mix world** (D12): a dragon per realm forced with `/epictest start`, with era-built
   bots only. Reported as era-mix results, separate from the raid bar.

Plus the install checks again on the era candidate (D11): the 2026-11-17 decision 3 counted them only if the final
build changed no launcher file, and WP2 changes the launcher.

### Order and dates

| Day | Work |
|---|---|
| Mon 17 (today) | Owner's review of this plan and the sheet |
| Tue 18 | WP0, WP1 |
| Wed 19 | WP2, WP3 |
| Thu 20 | WP4, WP5; candidate build |
| Fri 21 - Sat 22 | Played checks 1-5 and the install checks; fixes |
| Sun 23 | Played check 6 (if chosen); release notes from what was seen |
| Mon 24 | Ship |

## Settings (all default ON)

| Key | Default | What |
|---|---|---|
| `bot_era_class_shares` | True | The launcher's batches draw each new gamebot's class from the era share |
| `bot_era_builds` | True | A new gamebot takes its lifetime build from the era data |
| `bot_era_offmeta_percent` | 10 | Share of new gamebots taking one of today's builds instead (0-100) |
| `rvr_roster_build_roles` | True | The RvR eight's crowd-control slot prefers a crowd-control build (WP5) |

Each is in the server code, the launcher's `SocialExpansionSettings.Defaults` and `Recommended`, and
`release-server-settings.json`. None is a diagnostics, test-only or gameplay-rule switch (AGENTS.md).

## Risks

- **The numbers are estimates.** Every share and weight; European posts; thin classes. Mitigation: labelled in data,
  log and notes; one data file, so a 1.2.x can re-weight without code.
- **No raid has been fought by an era-mix world.** Raid code is unchanged, but a new install's raids meet a new mix
  (more healers, fewer pets, more PBAE). Mitigation: played check 6; notes keep the raid bar on the owner's world.
- **Over-budget trims and fills are mine, not the posters'.** 19 builds; each marked `change` in the data and `*` on the
  review sheet.
- **A wrong family or weapon mapping** gives a bot a build it can't wield (a shield build without a shield line).
  Mitigation: the validation rules and `UT_StarterGearForTrainedArrivals`; the gear rules themselves don't change.
- **Level-50 kit weapons** (`offline_level50_loadouts`, by class) may not match the drawn build's weapon line, as
  with today's random builds; the bot then skips the kit weapon (`MatchesBuild`) and its starter-gear pass fills the
  build's own weapon slot. A WP3 test checks that a kit bot ends with a usable weapon of its build.
- **The launcher changes a week before release.** The install checks re-run (D11).
- **Thin variety:** Valewalker and Animist have one era build; off-meta draws and jitter are their only variety.
- **Load cost:** a dictionary read once at start and one draw per new bot; no per-tick cost.

## The owner's review

Decisions, with my recommendation in bold. The build lists and shares are on the review sheet; anything not mentioned
is taken as recommended.

| # | Decision | Recommendation |
|---|---|---|
| D1 | Existing bots: keep class and build, or respec to an era build at their next train visit | **Keep both** (reasons above) |
| D2 | Who gets era builds | **New gamebots only**; companions, Epic Special Forces, buffbots and players unchanged |
| D3 | Midgard's shares: A (draft), B (the Jan-2003 count scaled; Healer 12%, Shaman 4.5%), A′ (draft moved toward the count) | **A′**: Thane 8, Runemaster 9, Berserker 8, Shadowblade 10, Hunter 5, Savage 6; healers kept at 15 + 9 |
| D4 | One class-share table for all batches, or the draft's two (RvR and levelling) | **One** (the RvR table) |
| D5 | Variety: off-meta share and per-bot jitter | **10% and 2 points** |
| D6 | Sluaghbinder's share in the 0.37b edition | **5% of Hibernia** (labelled "not era") |
| D7 | Trim the 12 over-budget builds, fill the 7 under-budget ones | **Yes**, as listed on the sheet |
| D8 | Wizard weights | **Fire 60, Earth 25, Ice PBAE 15** |
| D9 | RvR eights prefer a crowd-control build for the CC slot (WP5) | **Yes** |
| D10 | A Wayback job for 2003 class census pages (aggregate counts, no names), as an exception to "no population harvesting" | **Yes, counts only, after 1.2** |
| D11 | Re-run the install checks on the era candidate | **Yes** (the launcher changes) |
| D12 | Played check 6: one raid per realm on an era-mix world before shipping | **Yes, if Sunday allows**; otherwise the notes say it wasn't done |

**Build lists to approve** (on the sheet): the class shares per realm (Albion, Midgard A′, Hibernia), and per class its
builds, spec points and weights; the 19 builds changed by D7/D8 are marked.
