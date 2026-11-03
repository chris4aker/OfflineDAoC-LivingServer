# Upstream and fork survey (2026-11-03)

Owner 2026-11-03: "recheck Shadow's main for updates as well as any other fork out there" before the roadmap is final.
Read-only; every fork was fetched with git and read, not built or played (this machine has no .NET SDK). The
per-fork reviews are in [forks/](forks/). Labels as elsewhere: [V] verified in git, [I] inferred, [E] estimate.

## Upstream (shadowofze/OfflineDAoC)

- **One new commit since our merge:** `12992146` (2026-11-01 12:37), "Sluaghbinder follows the edition switch again
  instead of being off for everyone". It undoes his own discontinuation 52 minutes after our `50ecb30d` removed the
  class "as in the base" [V]. On main only, in no release tag; the repo stays "on hold", no 0.38 [V].
- **Consequence:** our removal's stated reason is gone. Following him means merging upstream/main and reverting
  `50ecb30d` (the two test edits expect the class gone); keeping our removal means holding our version of seven files
  on every future merge, and his NPC-removal script now refuses unless the switch is off [V/I]. **Owner decision.**

## Forks (commits in neither upstream nor ours)

| Fork | Unique | Last | What it is | Verdict |
|---|---|---|---|---|
| lometur (HearthDAoC) | 731 | 2026-11-03 | An always-on Linux Docker server a few people share; period-focused, synced to 0.37b; CI-published images | Borrow engineering (below) |
| stefanrows | 684 | 2026-10-26 | Two worlds (classic RvR default, Camlann full-PvP); update packs to v0.3xx; group doctrine and RvR movement | Borrow selected AI pieces |
| Rmarcoux86 | 328 | 2026-10-31 | A seeded roguelite on 0.37 (`/run`, champions, hard mode), fishing, a Hall of Fame; launcher update button | A few small pieces; the mode itself is off-theme |
| rambodolphin13 | 98 | 2026-10-30 | Now "Divided Realms" for the retail 1.130 client, modern classes and mounts; **not** a 1.65 continuation | Cherry-pick bot fixes |
| Cancain | 19 | 2026-10-30 | A fork of **our** Beta 1.1 | Take two branches, adapt one |
| BeezKneezItem9, MoomoocowPlus, scaleysandwich, BlakeFL | 4–22 | Oct–Nov | Power features on 0.35, a crash guard, ToA SQL, a LAN launcher | One-line guard; otherwise skip |

## What to take, mapped to our priorities

| Our priority | Source | What | Effort [E] |
|---|---|---|---|
| Save safety | lometur | WAL with `Synchronous=Normal`, online backup with an integrity check, atomic rename and rotation | 1–2 days |
| Measure before adding (`AI_PROFILE`) | lometur + stefanrows | CPU and allocation meter by bot kind and tick stage (hooks apply cleanly) plus `BotThinkProfiler` for think time; we add per-system scopes and tick percentiles | 2–3 days |
| Test harness | lometur | The socketless `HeadlessClient` as the scripted player's start (it runs in-process; the protocol-level player stays the target) | 3–5 days |
| RvR that isn't radar | stefanrows | Watch a fight before joining (`AutonomousRvrObserve`), after our observer cap; varied routes out of the hub and memory of where groups died (roaming groups only); new leader when the leader dies; retarget without stopping | 5–8 days |
| Era builds | stefanrows | 118 plans for 39 classes and spec-aware healers, as a second data source (modern Uthgard advice: informs weights, doesn't set them) | 1–3 days |
| A world that has a past | stefanrows | World speed up to 20× while no client is connected, to age a fresh world; **without** its level-50 gear seeding | 3–5 days |
| Bot reliability | rambodolphin13 (Nikse77's fixes) | Six clean cherry-picks: zone-seam loops, group stalls, ranged pulls, mezzed enemies, Darkness Falls stuck and re-login; leaving a keep just taken | 1–2 days |
| Bug we still have | Cancain | Gamebots in a player's group dying in a battleground are sent to the portal keep instead of standing in the capital forever; `/friend` for gamebots; two stock friends-list bugs; `/allrelease` | 1–2 days |
| Bug we still have | rambodolphin13, BeezKneezItem9 | RvR and DF XP rate: on a 10× world players get 1× in DF while bots don't | <1 day |
| Bug we likely have | Rmarcoux86 | Water breathing potions do nothing (no `CreateECSEffect`) | ~1 hour |
| Smaller fixes | lometur | Quest, buff and chant fixes (#190, #193), relic region crossing (#189), bridge decks on horse routes | 2–4 days |
| Crash guard | MoomoocowPlus | Camp list on monsters with no zone | minutes |
| Optional extras | Rmarcoux86 | Fishing as an off-by-default Easter egg (no `/fish go` teleport, no guard immunity; gamebots fishing in town); a character-planner launcher tab; server-only buff icons for Easter eggs | later |

**Adapt, not as-is:** Cancain's way back to the group (it can teleport before stablemaster routes); stefanrows's group
doctrine (its best-measured work, RvR deaths 1,763 → 790 an hour in bots-only runs, but tied to one shared switch).
**Watch:** lometur's real-player bots (could someday lead a real group), stefanrows's saved companion roster (needs a
patched client), Cancain's battleground keep claims (contradicts our 0.37 rule that grouped bots don't count).
**Skip:** Camlann, the roguelite mode, Divided Realms, the Open BG planner (off in its own world after crashes).

**Rule conflicts to keep out:** grey monsters dropping nothing and endless arrows (Rmarcoux86); level-50 gear seeding
(stefanrows, lometur's player bots at first login); GM-only Exchange teleport (lometur default); teleports that skip
stablemaster routes (Cancain, Rmarcoux86's `/fish go`).

**Credit:** all are GPL-3.0 like ours. Cherry-pick with `-x`, name the fork and authors in the file header, the
commit message ("Port from <fork> <sha>") and `CHANGELOG-LIVING-SERVER.md`, as our earlier ports from stefanrows and
lometur did. Many commits are authored "Claude", so credit the fork owner by handle. Never copy other players' names
or Hall of Fame data.

## The finding that matters most: EA files in our repo

lometur removed 54 EA client files today that came from upstream. **All 54 are in our `main` and `release/1.2`,
byte-identical** [V], added by shadowofze on 2026-09-15 (`4d8b513f`) and 2026-10-01 (`08ebffe3`):
- 51 stock 1.127 game-data files in `source/tools/observer-model-inspection/` (catalog CSVs and `loading.dat`);
- the client's login background, `source/reference/client-launcher/WPFLauncher/img/options-bg.png`;
- two `barrow_deflection_*.png` icons in `tools/pet-art/work/barrow-deflection/`.

Two uses need a change before removal: `tools/pet-art/install_bulwark_update.py:41` (its `draw_icon.py` can rebuild the
icon from the player's own client) and the launcher reference `OptionsWindow.xaml:40` / `WPFLauncher.csproj:163`. The
release-baseline manifests list them only as hashes and can stay. The reviews also flagged a 14–17 MB launcher `.mpk`
splash and `connect.exe` for the owner to check the origin of, with `pregame-splash.mpk` and six installer images.

Removing them at the tip is easy; they stay in public history unless history is rewritten, which our rules forbid for
legacy branches and tags. **Owner decision**, and it bears on PR #51 (Setup's copy of the base files) too.
