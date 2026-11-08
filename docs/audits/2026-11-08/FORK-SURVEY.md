# Fork survey, 2026-11-08: has anyone solved something on our roadmap since 2026-11-04?

Read-only. Every fork was fetched with git and read. **Nothing was built, run or played**, and no unit test was run.
Where I say "tested" I mean the commit carries unit tests. A run or play report is the author's own claim, quoted
from their commit or changelog. Labels: **[V]** I read it in their code or git, **[I]** my inference, **[C]** their claim.

Roadmap read at `origin/integration/1.2` (008c2270): RELEASE-TRAIN.md (1.2, 1.2.x, 2.0) and EPIC-ENCOUNTERS.md
("Restored behind switches", "Player-only mechanics", "Priority"). 1.2.1 and 1.2.2 exist only on `origin/feature/linux-1.2.1`'s
RELEASE-TRAIN.md (1.2.1 = strafing/circling, no-snap stops, flank step 2, town idling; 1.2.2 = Linux), so I read them there.
Baseline: the 11-03 survey (`docs/audits/2026-11-03/FORK-SURVEY.md` on `audit/north-star-2026-11-03`) and its fork
snapshots in our clone's `refs/forks/*` (taken about 11-03).

---

## 1. The coverage frame

### Checked (git, anonymous clone or fetch)

| Source | Remote heads vs our 11-03 snapshot | New work since then |
|---|---|---|
| shadowofze/OfflineDAoC (upstream) | `main` still `12992146` (11-01) [V] | none |
| **shadowofze/OfflineDAoC-True165** (new repo, not in any earlier survey) | first commit 10-31; tags v0.1 to v0.4.1; `main` a2163099 (11-08 09:11, "Coming in 0.5") [V] | **493 commits**, unrelated history (a fresh repo) |
| rambodolphin13/OfflineDAoC, now **rambodolphin13/DividedRealms** (same repo, renamed) | `main` 843dd60c → 8824e653 (11-08); 14 new branches; releases DR-dev-0.1a to 0.1e [V] | **419 commits on main** |
| Nikse77/OfflineDAoC (own repo, not surveyed before; we only had his commits via rambodolphin13) | `mod-0.37` 80d8cdb3 (11-05): "this repository is frozen; the work continues in OfflineDAoC-Crown" [V] | 402 unique commits from 10-26, **68 since 11-03** |
| FriendMaple/OfflineDAoC-True165 (new) | `main` 1e155380 (11-05) [V] | 7 commits |
| ThatFontGuy/OfflineDAoC (named in BOT-GUILDS.md, never surveyed) | `main` 8d63747e (11-01) [V] | 5 commits ("MacDAOC" scope: only the Godot client) |
| Cancain/OfflineDAoC-LivingServer (our only fork, `gh api .../forks`) | unchanged since 10-30 [V] | none; no PRs to us from anyone [V] |
| stefanrows, Rmarcoux86 (OfflineDAoC-private), BeezKneezItem9, MoomoocowPlus, BlakeFL, scaleysandwich (`OfflineDAoC-`, trailing dash) | every head identical to our snapshot [V] | none |
| lometur | `lometur/OfflineDAoC` is now **`lometur/HearthDAoC-fork-archive`**, archived 11-03, heads unchanged [V]. Its README points to `lometur/HearthDAoC`, which doesn't answer to anonymous git (private or not created) | unreachable |
| OpenDAoC/OpenDAoC-Core `master` (fetched since 10-20) | 3513bad499 (our read, 11-04) → 3356ed9f97 (11-08) [V] | 30 commits: performance, effect-list races, Cure Nearsight/Bladeturn, UDP hardening. Nothing on bot AI, raid tactics or movement |
| OpenDAoC-Core `fen/raid-encounters` | last commit 09-01 [V] | none new (see ruled out) |
| Dawn-of-Light/DOLSharp | 3 commits since 10-01 (NUnit 5, deps, radius checks) [V] | nothing relevant |
| mod-playerbots/mod-playerbots (AzerothCore) | merge 405f811 (11-05) brought in PRs from 10-29 to 11-02 [V] | raid role picker, class-call handling, login rotation (patterns only) |

### Couldn't reach

- **The GitHub fork lists** of shadowofze/OfflineDAoC and OfflineDAoC-True165. The API is blocked for other repos and the
  `/forks` pages aren't on the fetch allowlist. I found forks through web search, through other projects' credits
  (True165's `THIRD_PARTY.md`, rambodolphin13's docs) and by probing likely names. **A fork that nobody credits and no search
  indexes would be missed.**
- `rsandersjr/OfflineDAoC-True165-MP` (a multiplayer fork of True165, credited in True165 `THIRD_PARTY.md:49`, commit `bed80d6`),
  `Nikse77/OfflineDAoC-Crown`, `lometur/HearthDAoC`, `catastrophic-impact/OfflineDAoC`. Anonymous git asks for credentials, so
  each is private, renamed or deleted.
- Reddit r/daoc and r/OfflineDAoC: web search returned nothing from November. Discord: not checked (not public).
- "OpenDAoC-Core-Bots": no such public repo found under OpenDAoC, Fenyn or ozlo, and web search finds nothing by that name.
- Atlas: `AtlasFreeshard/AtlasCore` now resolves to OpenDAoC-Core (same HEAD). Eden: closed source.

---

## 2. Findings table

Licence: every DAoC fork here is GPL-3.0 [V, LICENSE files], like ours. mod-playerbots is GPL-2.0, in C++: design only.
"Ported from us" means the fork took the item from Living Server; it is listed so we don't re-port our own code.

| Roadmap item | Project / author | What they did (links) | Evidence | Portability to ours |
|---|---|---|---|---|
| **1.2.1 positional styles step 2: take the flank or back** | True165 (shadowofze) | `BotBrain.Flank.cs` (148 lines): a non-tank gamebot steps behind or beside a monster that fights someone else, only when it knows a back/side style for the weapon in hand; 4 s per try; retries when the mob turns over 45° or moves 100 units; companions get a "Positioning" option. [d2187a49](https://github.com/shadowofze/OfflineDAoC-True165/commit/d2187a498) (11-06), [UT_CompanionFlank165](https://github.com/shadowofze/OfflineDAoC-True165/blob/main/development-source/server/Tests/UnitTests/UT_CompanionFlank165.cs) | **In code and tested** (6 tests, pure rules) [V]; shipped in v0.3, changelog line 126 [C] | **Medium, hand port.** Unrelated history, so no cherry-pick. Gated by `Classic165Mode` and `CompanionOptions165`, neither in ours [V]. The gamebot rule (`FlankAllowed(..., companion:false)` = "not a tank") stands alone. Sits right on top of our #123 |
| Style choice: chains, positionals, reactive styles, no taunt spam | rambodolphin13 | `BotMeleeStylePolicy.Select` scores styles: chain follow-up +3, positional when in place +2, reactive after evade/block/parry +1.5, chain opener +0.6; taunts count a tenth unless grouped tank. [0146664a](https://github.com/rambodolphin13/DividedRealms/commit/0146664a5) (11-05), `UT_BotMeleeStyleChoice` | **In code and tested** [V]. The problem was measured in their night run ("Warriors used Plague 134, Draw Out 81…") [C] | **High.** Same file lineage. `merge-tree` shows 2 conflicts (`BotMeleeStylePolicy.cs`, `UT_BotCombatRefinement.cs`), both from our #123 [V]. Ours is 77 lines with no chain scoring [V] |
| **2.0: assassins stealth and open from stealth** (1.2.1 step 3) | True165 | `BotBrain.StealthOpener.cs` (135 lines): an assassin with a stealth style hides (player rules, `CompanionStealth.MayHide`), walks at stealth speed to the back (Backstab I/II) or front (Perforate Artery, not when the mob faces a groupmate), opens, gives up after 25 s. [246ac7f8](https://github.com/shadowofze/OfflineDAoC-True165/commit/246ac7f8d) (11-05) | **In code, untested** (no unit test names the opener) [V]; shipped in v0.3, changelog lines 127 and 136 [C] | **Medium.** We have `CompanionStealth.cs` and `BotPoisonSupply` [V]; needs the 165-mode gate removed and an option store. Our `BotMeleeStylePolicy.IsEligible` excludes stealth styles today [V] |
| RvR stealthers: hunt alone or in pairs, travel hidden, pick easy kills | True165; Nikse77 | True165 `AutonomousRvrStealthers.cs`: assassins and 3 in 4 archers hunt solo (1 in 5 pair up) at milegates and portal-keep roads; target priority sitting > caster under 30% power > hurt; `UT_RvrStealthers165` (15 tests). Nikse77 [f5729a48](https://github.com/Nikse77/OfflineDAoC/commit/f5729a4818) (10-31): solo stealth classes become zone ambushers, `UT_RvrFormation` | **In code and tested** (both) [V]. True165 shipped in v0.3 [C] | Medium. Both build on `BotRvrAmbush.cs`, which we already have [V]. Nikse77's sits inside his whole RvR rework (steps 1-5), now frozen |
| Bots and pets lose stealthed targets | Nikse77 (also in rambodolphin13) | Pets and battleground monsters drop a target that stealths. [1599685d](https://github.com/Nikse77/OfflineDAoC/commit/1599685dfa) / rambodolphin13 [8123f6e0](https://github.com/rambodolphin13/DividedRealms/commit/8123f6e0) | In code, untested [V] | High: 1 conflict, `ControlledMobBrain.cs` [V] |
| **1.2.1 movement: stops and group walking that don't snap** | Nikse77 (via rambodolphin13) | A group follower no longer takes an independent route every turn ("the party visibly jerked once per second"): one `bot == leader` guard. [0996fff6](https://github.com/rambodolphin13/DividedRealms/commit/0996fff6ea) (cherry-pick of Nikse77 b0fcd555, 11-01) | In code, untested [V] | **Applies cleanly** to `integration/1.2` (`merge-tree` rc=0) [V]. Our `AutonomousWorldBotController.cs:1789` lacks the guard [V] |
| Movement: armies march in formation | Nikse77 | Patrol-formation follow, each group on its own side, no stop-start when a leader replots. [5ccd56ce](https://github.com/Nikse77/OfflineDAoC/commit/5ccd56ced) (11-03), [5792e921](https://github.com/Nikse77/OfflineDAoC/commit/5792e92145) | In code, untested (no test in the commit) [V] | Medium. It is in `AutonomousWorldBotController.Rally.cs`, which is part of his RvR rework |
| **Strafing / circling in melee; town idling** | nobody | No bot strafing code in True165, rambodolphin13 or Nikse77 (`strafe` greps to 0 in bot code) [V]. True165's `AutonomousTownIdleRouting.cs` matches ours except 7 lines (inherited) [V] | — | Still ours to build |
| **2.0 crafting bots (M2-M3: crafters exist, crafting matters)** | rambodolphin13 | Bots join an order at a craft master, buy materials, work at a real station, gain skill on the player tables, post to the Exchange. Craft orders: a bot pays a crafter of its realm, coin held in escrow, made on the crafter's next trip, delivered when the trip ends, refunded after 3 h. [93c25b69](https://github.com/rambodolphin13/DividedRealms/commit/93c25b698) (11-01), [ceac061a](https://github.com/rambodolphin13/DividedRealms/commit/ceac061a3), [e37f8810](https://github.com/rambodolphin13/DividedRealms/commit/e37f8810a), [24f8a2e7](https://github.com/rambodolphin13/DividedRealms/commit/24f8a2e78); `AutonomousBotCrafting.cs` (553+), `AutonomousBotCraftOrders.cs` (428) | **In code and tested** (`UT_AutonomousBotQuestsCrafting` 652 lines, `UT_AutonomousBotCraftingOrders` 574) [V]. Run report: night 11-04, "1,147 bots made 8,155 items", but crafter skill median 8, top 28 [C] | **Medium-high.** The series root conflicts in only 3 files (`AutonomousLivingServer.cs`, CHANGELOG, `progress-policy.json`) [V]; the follow-ups need the series in order. Our CRAFTING-AND-ECONOMY.md §3.2 says "Bots do not craft" [V], so this is a real gap |
| Crafting bots (simpler model) | True165 | `mode165/CrafterBots165.cs` (383 lines): 1 in 5 bots get a primary craft and make one recipe on each town errand at 94-99% quality, with player-like skill gain, posting to the Exchange; siegecrafters supply siege crews. v0.1 (11-01) | In code; only siege tiers tested (`UT_SiegeCraftTier165`) [V]; changelog line 484 [C] | Low-medium: gated by 1.65 mode, and bots don't visit stations |
| **2.0 questing certified** (bots on quests) | rambodolphin13 | Gamebots run classic DataQuests as `BotQuestPlan`; real XP, coin and items; a quest is parked (not given up) on a missing monster or blocked road; quests that every bot fails are picked rarely and logged every 30 min. [93c25b69](https://github.com/rambodolphin13/DividedRealms/commit/93c25b698), [5467fad2](https://github.com/rambodolphin13/DividedRealms/commit/5467fad2b) | **In code and tested** [V] | Medium (6 conflicts, most in files from the root commit) [V]. [I] The "every bot fails it" log is a free quest-health signal for our certification |
| **2.0 #2 skill that improves** (per monster) | rambodolphin13 | `AutonomousMobLessons.cs`: wins and deaths per monster, solo and group kept apart (`offline_bot_mob_lessons`); twelve fights shift the bot's felt con by one step; parties break off a lost fight. [98873c3a](https://github.com/rambodolphin13/DividedRealms/commit/98873c3ab) (10-31) | **In code and tested** (`UT_BotConLessons`) [V] | Medium (7 conflicts in the route-threat files) [V]. [I] It is per bot, not per raid, so it doesn't cover "raids learn a boss" |
| **2.0 #4 knowledge / rumours** (hard foes) | rambodolphin13; True165 | rambodolphin13: red or purple route blockers passed on to same-realm bots of the same level band, [2d237a7d](https://github.com/rambodolphin13/DividedRealms/commit/2d237a7d4) + `UT_OvernightRunFixes`. True165: low bots avoid spots where bots of their level keep dying; solo bots go around packs and their own death spot (changelog 0.3 and 0.5) | rambodolphin13: in code and tested [V]. True165: claimed [C], code not read | Medium. [I] It is the opposite of "knows only where it has been" but fits rumours |
| 2.0 #1 alt roster (keep low levels active) | rambodolphin13; mod-playerbots | rambodolphin13 "level spread": XP stop levels so some bots stay in low bands, and level-25 creation, [852391a5](https://github.com/rambodolphin13/DividedRealms/commit/852391a5), [85db49f9](https://github.com/rambodolphin13/DividedRealms/commit/85db49f9). mod-playerbots #2774 rewrites periodic login rotation without accounts ([09da76b](https://github.com/mod-playerbots/mod-playerbots/commit/09da76b)) | In code (rambodolphin13; tests not checked) [V]; mod-playerbots merged 11-05 [V] | Low as code. **Not an alt roster:** nobody built bot players owning several characters [V, grep]. Stop levels freeze mains, against "alts add, never replace" [I] |
| 2.0 guild package | Nikse77 | Parties of exactly eight gamebots found "automatic" guilds (generated names and emblems, cap 64), which stay silent in guild chat, refuse players and claim keeps their realm takes. [e61918f2](https://github.com/Nikse77/OfflineDAoC/commit/e61918f29), [abdf12d1](https://github.com/Nikse77/OfflineDAoC/commit/abdf12d10), [72fcfdf5](https://github.com/Nikse77/OfflineDAoC/commit/72fcfdf52) (11-04) | In code and tested (`UT_AutonomousGuildFounding`, `UT_AutonomousKeepClaims`). Earlier commits in the series say "not compiled yet" [V] | Low fit: silent, closed guilds run against our social guild-tier design. The name and emblem generators (`AutonomousGuildNames.cs`, `AutonomousGuildEmblems.cs` with a contrast rule) could be reused |
| **1.2.x play with friends** | rambodolphin13 | Since our port plan's source (`feature/play-with-friends` 0805dd14): invite codes holding the host's addresses, one window, a **host password** for new accounts from other PCs ([af1fcf85](https://github.com/rambodolphin13/DividedRealms/commit/af1fcf858), `UT_HostPassword`, `JoinFriendTests`); a host on a public IP ([b0744a2a](https://github.com/rambodolphin13/DividedRealms/commit/b0744a2a6)); **Character passport**: bring your own character to a friend's world and back, locked at home while away ([da78651f](https://github.com/rambodolphin13/DividedRealms/commit/da78651f7d), `PassportRoundTripTests` 491 lines) | **In code and tested** [V]. "dev10 went to a friend" (fe6493fc) [C]; no played co-op report found | Medium. **These commits are on DR `main`, not on `feature/play-with-friends`** [V], so our 1.2.x line ("ported from … feature/play-with-friends") names a stale source. Design change to weigh: they **dropped the friend list**, so the firewall admits any address and the password is the only gate [V, af1fcf85 text], unlike our plan's "internet mode limited to listed friends" |
| 1.2.x play with friends (True165 base) | rsandersjr | `OfflineDAoC-True165-MP`; True165 took one fix from it (`bed80d6`) | **Claimed only**: repo unreachable | — |
| **1.2.2 Linux** | ThatFontGuy | Cross-platform `build_csharp.py` (replaces the Windows-only `build_csharp.ps1`) and `run_dev.py` for the new client, macOS-focused. [8d63747e](https://github.com/ThatFontGuy/OfflineDAoC/commit/8d63747e1f) (11-01) | In code, untested (no run report) [V] | High for the new-client build step (3 small files) |
| 1.2.2 Linux (server) | True165 | `development-source/server/Dockerfile`, `docker-compose.yml`, `DOLLinux.sln`, added in the v0.1 import b097caf3; they are OpenDAoC's (clones OpenDAoC-Database, `sslVerify false`) [V] | Inherited, not new work | Low: our Linux branch already has container prototypes |
| Raid tactics, ported from us | rambodolphin13 | Our DF princes (2ef5e7c0) as [71e3567a](https://github.com/rambodolphin13/DividedRealms/commit/71e3567a0), the Nahemah corridor (803882de) as [1d546331](https://github.com/rambodolphin13/DividedRealms/commit/1d546331c), raid route rules as [3b42bcce](https://github.com/rambodolphin13/DividedRealms/commit/3b42bcce9), the launcher updater as [b72ff109](https://github.com/rambodolphin13/DividedRealms/commit/b72ff1099) | Ported from us [V] | Nothing to take back |
| DF: Legion's hall exit | rambodolphin13 | Client zone jumps 71 (MidBoss) and 77 (LordExit, by Legion's hall) had no ZonePoint rows; the exit policy covers 70-77 and a world edit adds one row per realm. [428ccd7d](https://github.com/rambodolphin13/DividedRealms/commit/428ccd7df) | In code and tested (`UT_DarknessFallsExitPolicy`); the world edit is a JSON file [V] | Medium: a world update for the owner to decide. Not raid AI |
| Raid pattern: class-role picker | mod-playerbots | One picker for special class roles (an assistant of the class, then a preferred spec, then any bot of the class; gear score breaks ties), refreshed every second into instance state and announced; reassigned on a death. [acec521](https://github.com/mod-playerbots/mod-playerbots/commit/acec521) (#3004, merged 11-05) | In code (C++); their PR text [C] | Design only. [I] The shape for Funus' class/weapon puzzle and the Earthmender assignment |
| Raid pattern: switch to totems or adds | mod-playerbots | Nefarian's shaman call: DPS switch to a corrupted totem and stay on it until it dies; ranged take the nearest, melee only one they can reach. [75b8c22](https://github.com/mod-playerbots/mod-playerbots/commit/75b8c22) (#2979). Magtheridon's channelers split between tanks (`src/Ai/Raid/Mag/`, older than our window) | In code (C++) [V]; PR text [C] | Design only. [I] It matches our "ranged adds" tactic for Spindler newborns and Scurceol's orbs |

### Open problems with no solution anywhere [V, grep of every fetched fork]

- **Spindler Broodmother's adds, Govannon's demon phase, Funus' class/weapon puzzle**: True165 and rambodolphin13 name these bosses
  only in the inherited raid list (`AutonomousRealmRaid.cs`, `LordOfBoss.cs`). Neither has `EpicEncounterTactics` or any
  per-boss tactic. rambodolphin13's only raid work since our survey is ported from us.
- **Darkness Falls raid AI and Legion**: nobody has raid AI. rambodolphin13 fixed DF *exits* (above). True165 shows the DF raid
  greyed out on its Horn of Rallying while the realm can't enter (changelog line 162).
- **Strafing / melee circling**: none (above).
- **The alt roster** (bot players owning several characters): none.

---

## 3. Candidates ruled out, and why

| Candidate | Why |
|---|---|
| OpenDAoC-Core `fen/raid-encounters` (`RaidEncounter.cs`, `UT_RaidEncounterScaling`) | Last commit 09-01, so not new. It **scales encounters to raid size**, which the owner rejected ("against scaling epic bosses down") |
| OpenDAoC-Core master, 11-04 to 11-08 | Engine work only. Bug candidates for later (not roadmap): b9b32cf03 Cure Nearsight stripping Bladeturn (rambodolphin13 hand-ported it as 37259115), e645e31165 an effect-list race, a1e02efe16 `CanExpire` |
| rambodolphin13: auto-update ([b72ff109](https://github.com/rambodolphin13/DividedRealms/commit/b72ff1099)) | Ported from our cc305fe0 / b1aba915 |
| rambodolphin13: headless test client ([350b6754](https://github.com/rambodolphin13/DividedRealms/commit/350b67548)) | We already have a protocol-level playtester (`OfflineDaoc.Playtester`). Theirs adds Detour walking and an MCP tool mode, if we ever want that |
| rambodolphin13: Sovereign/Pendragon/Tuatha remakes, Regalia art, fishing, ML trials, boats, loot phases 0-6, modern classes | Modern 1.130 era, against the 2003 North Star |
| rambodolphin13: bots swim on the surface ([12d0f789](https://github.com/rambodolphin13/DividedRealms/commit/12d0f789)) | Touches the modern client's packet libraries (`PacketLib1124.cs`); 4 conflicts |
| True165: downloader as an "installer robustness" answer | Its `Get-OfflineDAoC-True165.ps1` uses `curl --retry 10 --continue-at -`, per-part SHA-256, `.done` markers and a zip-slip check [V]. shadowofze's 0.37 `Get-OfflineDAoC.ps1`, which our Setup drives, already has `--retry 5 --continue-at -` [V]. The only additions are the in-place update mode and more retries. [I] If "robustness" means issue #113's antivirus deleting Setup.exe, True165's answer is a few-KB `.cmd` + `.ps1` downloader zip, not an exe. That is an idea, not code to port |
| True165: hired companions at innkeepers, housing, new-client 1.64 pre-game | Not on our roadmap. Companions replace `/spawn` and `/raid` |
| FriendMaple | Warden speed-twist fix (already in True165) and a companion paperdoll UI for the new client |
| Nikse77: Paladin chant order, BAF diagnostics, navmesh probes, siege coalition | Not roadmap items. The siege coalition (two groups and 16 bots to march) could matter for our siege balance work later |
| Cancain, stefanrows, Rmarcoux86, BeezKneezItem9, MoomoocowPlus, BlakeFL, scaleysandwich | No new commits since our snapshot |
| DOLSharp, Atlas | Nothing relevant / now OpenDAoC |
| cmangos/playerbots, celguar/mangosbot-bots | Only HEAD probed. mod-playerbots, their active descendant, was read instead |

---

## 4. Final missing-competitor check

- **The miss that matters:** our RELEASE-TRAIN.md ("The base: shadowofze has stopped") and UPSTREAM-RELEASES.md say only that he
  "moved to a separate True 1.65 version". **It is public, GPL-3.0 and shipping**: v0.1 (11-01) to v0.4.1 (11-07) with in-place
  updates, 0.5 under way on 11-08, and player bug reports on its GitHub Issues page (its changelog cites #6, #14, #16) [V]. Its `THIRD_PARTY.md` credits us (`chris4aker/OfflineDAoC-LivingServer`
  rows 45 and 47, and Social Expansion rows 43 and 48) [V]. It is now the most active base-lineage project, and it has its
  own forks (FriendMaple, rsandersjr). Our "rechecked 2026-11-03 … no fork continues 1.65 Offline DAoC as a whole" is
  out of date.
- **rambodolphin13 is porting from us** (DF princes, Nahemah, raid route rules, save safety, updater) and runs a regular
  "fork survey" batch of his own (87e3088a, 11-06: True165, OpenDAoC, Nikse77) [V]. He is the second most active, but on the
  modern 1.130 client.
- **Forks that moved where I can't see:** Nikse77 → `OfflineDAoC-Crown`; lometur → `HearthDAoC` (private or not created);
  rsandersjr's MP fork. Worth a manual look by the owner in a browser; this session can't reach them.
- **Next time, check:** True165's GitHub fork list and issues page in a browser (both blocked here), and r/daoc by hand.

---

## 5. Recommendations: what to port

Verified facts are in the table. The order and the effort below are my inference [I].

1. **Nikse77's follower-stutter guard ([0996fff6](https://github.com/rambodolphin13/DividedRealms/commit/0996fff6ea)) into 1.2.1.**
   A one-line change that applies cleanly and targets 1.2.1's "stopping that doesn't snap" for every bot group. Credit
   Nikse77 (via rambodolphin13). Needs a played look, as the train says.
2. **True165's flank step (`BotBrain.Flank.cs`) as 1.2.1's "positional styles, step 2", hand-ported.** It is exactly our next
   step, with pure-rule tests. Drop the companion option and the 165-mode gate, keep `FlankAllowed`'s gamebot rule and the
   45°/100-unit retry. With it, take **rambodolphin13's style scoring (0146664a)** so the styles the flank unlocks are chosen
   and chained. Two small conflicts with our #123. Credit shadowofze (True165) and rambodolphin13.
3. **rambodolphin13's bot crafting and craft orders (93c25b69 → ceac061a → e37f8810 → 24f8a2e7) as the start of 2.0 crafting
   M2-M3.** It is the only working bot-crafting code that uses our framework and real stations, with tests and an overnight
   report. Their own numbers warn that skill gain is slow (median skill 8 after a night), so pair it with our M1 pace settings.
   Check it against our rules first: orders hand over at a trip end, and selling stays at task breaks [V, e37f8810]. Our
   default share is 7%.
4. **True165's stealth opener for 2.0's Assassins**, after the flank. True165 has no unit test for it, so we write the tests.
   Our `IsEligible` must stop excluding stealth styles for this path only.
5. **Update our 1.2.x plan's source:** port Host for Friends from DR `main` (af1fcf85, b0744a2a), not `feature/play-with-friends`.
   **The owner decides** whether to keep a friend list next to the host password; rambodolphin13 dropped it. Look at the
   Character passport (da78651f) as the "bring your own character" answer for co-op.
6. **Later, 2.0:** rambodolphin13's `AutonomousMobLessons` (per-monster lessons) as a seed for "skill that improves", and its
   bot quest runner's "every bot fails it" log as an input to questing certification. mod-playerbots' role picker
   (#3004) as the design model for Funus' class/weapon assignment.
7. **Nothing to port for** Spindler's adds, Govannon's demon phase, Funus, DF raid AI, Legion, strafing or the alt roster. Those
   are still ours to solve.
8. **Docs:** correct RELEASE-TRAIN.md's "The base: shadowofze has stopped" and UPSTREAM-RELEASES.md: True165 is public and active
   (v0.4.1, 0.5 in progress), and our fork-check routine should follow it as a second upstream.

Credit as before: cherry-pick with `-x` where the history allows; otherwise "Hand port from <repo> <sha>" in the commit
message, the file header and CHANGELOG-LIVING-SERVER.md, crediting the handle. True165's history is unrelated to ours, so
its code can only be hand-ported.
