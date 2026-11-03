# Fork survey: Rmarcoux86/OfflineDAoC-private

Surveyed 2026-11-03, read-only, from `refs/forks/Rmarcoux86/*` in `/home/claude/wt-12`. Labels: [V] verified in git, [I] inferred, [E] estimated.

## 1. What it is and who it's for

- **Theme.** The fork is Ryan Marcoux's "fishing and Expedition mode" fork of stock 0.37 [V]. Its base is upstream `9da90910` (2026-10-20), which already carries our Social Expansion changes [V]. It adds:
  - **Expedition**, a seeded roguelite run over 5 acts from level 1 to 50, with permadeath through the Hardcore flag (a death deletes the character), Diablo-style rarity loot, boons, elite affixes, shrines and act bosses [V];
  - **the Gauntlet**, a post-win arena of five scripted bot "champions" [V];
  - **hard mode** [V];
  - **fishing** [V];
  - a **Hall of Fame** [V].
- **Audience.** It is for players who want an ARPG run on DAoC, not a living world. The launcher's bot pages (population, RvR, realm events, the exchange) are hidden "because the launcher is about the Expedition" (`MainForm.ShowBotFeatures = false`) [V].
- **Size and authorship.** 328 commits are in neither upstream nor ours [V]. Claude wrote 301 of them, Ryan Marcoux 23 and github-actions 4 [V]. Across 168 files the diff is +43.5k / -92 lines [V]. Every `check/*` branch is merged into main [V].
- **Releases and updates.**
  - A tag push runs `.github/workflows/fork-release.yml`. It builds the server and launcher, runs only the fork's tests (a filter on `UT_Fishing|UT_Expedition|...`), and attaches `fork-update-<tag>.zip` [V].
  - Players install the zip once with `fork-release/INSTALL FORK UPDATE.cmd`. The script checks that the server and launcher are closed, backs up the files to `fork-backups\`, and copies `GameServer.dll` and the launcher [V]. `RESTORE STOCK SERVER.cmd` undoes it [V].
  - After that, `ForkUpdater.cs` polls `api.github.com/.../releases/latest`, and START SERVER turns into a gold **GET UPDATE** button with a "What's new" view (`build-history.json`) [V].
  - There is no hash or signature check on the zip [V], and no database migration. New content is written to the database from code at startup (`FishingStartup`, `MobRespawnOverrides`, `[DataTable]` auto-creation) [V].
  - The fork shipped fork-v116 in about 9 days [V].

## 2. Notable features

| Feature | Where | Quality read | We have it? | Verdict |
|---|---|---|---|---|
| Water breathing ECS fix (Nereid potions did nothing) | `ECS-Effects/WaterBreathingECSEffect.cs`, `spells/WaterBreathingSpellHandler.cs` (016e2078, ea532b1b) | Small, explained [V] | No: our handler has no `CreateECSEffect` [V], so it is likely broken in ours too [I] | **Borrow** |
| Character Planner tab (specs, spells, styles, RAs from the game DB, read-only) | `Launcher/CharacterPlanner*.cs` and tests (1cc57439…320ee74f) | About 2.6k lines plus 976 lines of tests; mirrors `SkillBase` / `Specialization` [V] | No [V] | **Borrow** (out of game, and era-true: 2003 players used template planners) |
| Fishing (`/fish`, legendary catches, skill, spots, stock models) | `gameutils/Fishing/*`, `commands/playercommands/Fish.cs`, `UT_Fishing*` | Clean and tested, player-only [V] | No [V] | **Adapt as optional** (Easter egg; see §3) |
| Launcher auto-update and What's new | `ForkUpdater.cs`, `MainForm.ForkUpdate.cs`, `ForkNotesForm.cs`, `ForkUpdaterTests.cs` | Good UX, tested, no integrity check [V] | We have a Patcher and installer workflow; no in-launcher check found [I] | **Adapt** (UX only; keep our Patcher and its hash guards) |
| Ownerless scripted `GameBot` constructor plus champion brains (positionals, RA plans, Purge) | `bots/GameBot.cs` (+58), `GauntletChampion.cs` (3.4k), `AutonomousBotRealmAbilities.GrantChampion/TryUse` | Ambitious; the geometry tests need no server [V]; the brain is one 3.4k-line file [V] | No [I] | **Adapt** (named rivals or duelists) |
| Buff-bar icon with custom delve text, server-only | `Boons/BoonIconEffect.cs`, `DetailDisplayHandler.cs` | Small, no client change [V] | No [I] | **Borrow** (pattern) |
| Expedition roguelite (acts, boons, elites, shrines, uniques, `/junk`) | `gameutils/Expedition/*` (about 15k lines [E]), `Run.cs`, `Junk.cs` | Big, well tested (25 test files) [V]; leans on many global tweaks [V] | No. Our `AutonomousWorldBotController.Expedition.cs` is something else, and the name collides [V] | **Watch** (optional mode at most, later) |
| Gauntlet and hard mode | `Expedition/Gauntlet/*`, `ExpeditionHard*.cs` | Depends on Expedition [V] | No | Skip |
| Hall of Fame and community spectators (pre-filled GitHub issue, Actions intake, raw JSON shipped back) | `HallOfFame*.cs`, `.github/workflows/hall-of-fame.yml`, `hall-of-fame/`, `gauntlet/guest-champions.json` | Clever, no token needed [V] | No | **Watch** (a "realm records" idea; see the privacy note in §4) |
| Respawn overrides by name in code | `gameutils/MobRespawnOverrides.cs` | Trivial [V] | No [I] | Borrow the pattern only |
| Turn-ins (timed XP items no longer drop) | `LootGeneratorTemplate.cs` (d83b1aae) | Global, not gated [V] | No | **Skip** (era loss) |
| Hide-bots | `MainForm.ShowBotFeatures` | Hides launcher pages [V] | n/a | Skip (the opposite of our North Star) |
| Global QoL: `respec_anywhere` on, unlimited arrows, rest regen up to 10% a tick, 3 s spawn aggro delay, dragons passive in flight, Reaver flex at level 1 | `ServerProperties.cs`, `RangeAttackComponent.cs`, `ClassicRestRegeneration.cs`, `StandardMobBrain.cs`, `Dragons/*.cs`, `AbstractServerRules.cs` | Ungated [V] | No | **Skip** (era-breaking) |

**Our gaps.** The fork addresses none of them:
- No bot-invites-player code: `invite` greps empty [V].
- No offline-time simulation [V].
- No AI cost profiling: no `Stopwatch` or profiler code [V].
- Stealth: its only perception change is a stealth reveal radius for Spectral elites. The code that drops a stealthed `GameBot` from the client was already in upstream [V].
- Chat: the only thing near a chat fix is a "shuffled bag" for taunts, so no line repeats until all have been said [V]. It is a small trick for making bot chat feel less canned [I].
- Test harness: the fork's tests are pure NUnit with no world. They work through seams such as `ExpeditionCampSources` / `WorldCampSource`, which abstract the world's camps, plus GM fixtures (`/gauntlet kit`, `/gauntlet testwinner`) [V]. That is a pattern for us, not a harness [I].
- Empty world: the "guest champions" flow (players share a character snapshot that ships to everyone as an NPC) is the one idea that bears on it [I].

## 3. Conflicts with our owner's rules

| Rule | Conflict |
|---|---|
| Real loot | **Yes, if ported wholesale.** During runs, stock gear is suppressed (`expedition_replace_stock_gear`, gated to runs) [V]. Outside runs, two changes are global: monsters that con grey to the player drop **no items or coin** (`AbstractServerRules.DropLoot`, be12375a), and timed turn-in drops are gone [V]. Arrows are never used up, for players and for bots [V]. |
| Realm Exchange | No server change found [I]. The launcher hides the exchange page [V]. `ExpeditionExchanger` is an unrelated unique-weapon reforger [V]. |
| Stablemaster routes | **Partly.** `/fish go <n>` teleports to shore spots outside runs too [I], which bypasses horse routes. Runs add a recall stone with no cooldown and a Hastener at camp (gated to runs) [V]. If we port fishing we drop `/fish go`. |
| Selling only at natural breaks | `/junk auto` sells a drop on the spot. It is for players only and opt-in [V], and bots are untouched [I]. Fine for an optional mode. |
| Gamebots earn armour | The ownerless constructor calls `EquipBot(Level)` for champions [V]. They are not registered as autonomous bots [V], so there is no conflict unless we reuse it for world bots. |
| Darkness Falls open to solo bots | Access is unchanged [I]. Two global tweaks change Darkness Falls itself: two monster types respawn after 180 s, and thrown weapons ignore 1,000 units of height (282d882d) [V]. The difficulty shift for solo bots is minor [I]. |

Fishing's aggro immunity also covers guards (`GuardBrain`) and is on by default [V]. If we port fishing it should default off, or skip guards.

## 4. Legal notes

- **EA client files.** The fork **adds** no client binaries or assets: its only new non-code files are two `.cmd` scripts [V]. It inherits what upstream and ours already track [V]:
  - `source/tools/observer-model-inspection/*.csv` (50 files, named like the client's catalogs, e.g. `objects.csv`) [V]; they are likely client-derived [I];
  - `source/tools/OfflineDaoc.Launcher/Assets/offline-daoc-client-splash.mpk` (14 MB, client archive format) [V/I];
  - `source/reference/daocportal/connect.exe` (35 KB) [V]; its origin is unclear [I].

  The fork's `FishingCatalog` uses stock model IDs from those CSVs and makes no client edits [V].
- **Licence.** GPLv3, unchanged. `LICENSE`, `THIRD_PARTY.md` and `AGENTS.md` have no diff from upstream [V]. The README keeps upstream's "you don't need permission to fork… keep the existing licenses and credits" [V].
- **"Private".** The word appears only in the repo name and the git remote name (`push to the remote private`, docs/EXPEDITION-GAUNTLET-PLAN.md) [V]. No README line or licence term restricts reuse [V]. The name may only mean the author's own copy [I]. You said the repo is public; I could not check that from git alone.
- **People's data.**
  - `hall-of-fame/hall-of-fame.json` holds other players' GitHub handles and character names [V] (names left out of this survey).
  - The champions are presented as a named real guild [V]. They are probably real people's characters [I].
  - **Do not copy names, looks or Hall of Fame data.**
- **Our obligations.** Borrowed code stays GPLv3, which we already are [I].

## 5. Top 5 to borrow (North Star value per effort)

1. **Water breathing ECS fix.** It is an era bug fix: Nereid potions work again. About 1 hour plus an in-game check [E]. Take 016e2078 but not d0f4a206 (a default swim speed for everyone; ea532b1b removed it again).
2. **Character Planner launcher tab.** It is era-flavoured and outside the game, so it costs no era feel. About 1–2 days to port [E], because our launcher has diverged [I].
3. **Fishing as an optional Easter egg.** Keep the setting off by default, drop `/fish go`, and limit the aggro immunity to monsters. Our own extension: autonomous gamebots fish at shores as idle town life, an "alive server" touch the fork doesn't have [V]. Port in about 1–2 days, plus 2–4 days for bot fishing [E].
4. **Server-only buff icon and code-defined content patterns** (`BoonIconEffect`, `FishingStartup`, `MobRespawnOverrides`). These are cheap rails for Easter eggs and realm-event buffs with no client patch and no migration. About half a day to a day [E].
5. **Scripted ownerless `GameBot` and positional duel brain.** Use it for named rival "famous players": town duelists, or a hidden arena champion as an Easter egg. Take the constructor and the `GauntletPositioning` math and tests, not the 3.4k-line brain wholesale. About 3–5 days [E].

Just outside the top 5: the launcher **GET UPDATE / What's new** UX on top of our own Patcher (2–3 days [E]).

**Credit.**
- Cherry-pick with `git cherry-pick -x`, so each commit records the fork's original SHA.
- Add a CHANGELOG line in the convention upstream already uses for us ("(from Rmarcoux86/OfflineDAoC-private)") [V].
- Add an entry in `THIRD_PARTY.md` or a credits section naming Ryan Marcoux / Rmarcoux86, under GPLv3.
- Most commits are authored "Claude", so credit the fork and owner by name, not by git author.
- Do not reuse community names or Hall of Fame data.
