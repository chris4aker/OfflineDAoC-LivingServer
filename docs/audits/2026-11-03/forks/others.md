# Fork survey: Cancain, rambodolphin13, the smaller forks, and upstream

Labels: **[V]** I checked it in git (log, diff, grep, merge-tree). **[I]** My inference from code or commit messages. **[E]** My estimate or judgement.
Repo: /home/claude/wt-12, read-only. I ran the merge tests in a throwaway clone at /home/claude/fork-survey/tmp/cc. This machine has no .NET SDK, so **nothing was built and no test was run** [V].

---

## 1. Cancain / OfflineDAoC-LivingServer (a fork of ours)

**What it is.** It has 19 unique commits, 13 of them non-merge, all from 2026-10-28 to 10-30 [V]. The base is our origin/main at e57d2d88 (Beta 1.1), and the build is named "1.1-purple.4" [V]. Each feature sits in its own new file and hooks into ours through one-line calls marked `// Fork:`, so the diff is small: 20 files, +1,345 lines [V]. The author says it was play-tested in game on 2026-10-28 and 10-30 [I, from the commit messages].

**Applies to release/1.2?** [V, merge-tree]
- `feature/friend-bots` and `feature/bg-claim` merge cleanly.
- `group-release`, `fix/group-return-path` and `main` conflict only in `ForkBuildInfo.cs` and `UT_ForkBuildInfo.cs`, on the build name. Take our side there.
- Every GameBot API they call exists on 1.2 with the same signature [V].
- Our 1.2 now moves the corpse first (`MoveCorpseForRelease`). The hooks still land in the right places [V, merged GameBot.cs read].

| Change | Tests | Verdict |
|---|---|---|
| **/friend works with autonomous bots** (`BotFriends.cs`). Online and parked bots join the friends list by exact name, same realm only, never retired bots, and a real character of the same name wins. Bot logins and logouts send "just entered/left" to both clients. | UT_BotFriends: 14 tests, 24 cases, on a temporary SQLite database [V] | **Take as-is** [E]. It fits "fully alive server" well. |
| Fix to `FriendsManager`: an empty friends list left no status-cache entry, and adding a friend sent a one-name AddFriend that wiped the old client's Friends Monitor. | Covered above [V] | **Take as-is** [E]. These are real bugs in the stock code, and both exist on 1.2 [V]. |
| **/allrelease** (`/grouprelease`) releases every dead bot in your group at once. | Report text only [V] | **Take as-is** [E] |
| **Battleground release fix.** Our player-led gamebots release to the capital, and `ContinueReturnJourney` then does nothing while the bot and its leader are in different regions. That code is unchanged on 1.2 [V], so the bot stands there for good [I]. Cancain releases it to its realm's portal keep instead. | Pure-rule tests [V] | **Take** [E]. It fixes a bug we still have. |
| Return path for player-led bots: walk a planned route, wait while the leader is dead, re-enter the group on arrival, and teleport to the leader after a 20 s cross-region wait or a 15 s stall. The stall check runs first in `BotBrain.Think`. | Pure rules only. Nothing tests the hooks in `ContinueReturnJourney` [V] | **Adapt** [E]. (a) Group returns skip `AutonomousThreatAwarePathing` (our detours around hostiles) [V]. (b) The cross-region teleport runs before the stablemaster block [V], which shortcuts stable routes. Keep the unstick check, but route first and teleport only as a last resort. |
| **Battleground keep claims**: four `fork_*` switches, all off by default, with separate battleground and frontier limits. Bots in your group can count toward `claim_num`. Includes a `set-bg-claim.ps1` tool. | UT_ForkKeepClaims: 8 tests, 27 cases [V] | **Watch** [E]. Counting group bots for claims contradicts our 0.37 rule that ginvited bots "don't count for … keep claims" [V, our changelog]. The script hard-codes Cancain's `D:\OfflineDAOC\…` paths [V]. |
| CLAUDE.md fork rules (Swedish reports, no AI attribution); build-name bumps | — | **Skip** |

**Owner-rule conflicts:** only the stable-route shortcut and the claim counting above [I]. Loot, the Realm Exchange, selling, armour and Darkness Falls are untouched [V].
**EA client files:** none. The only binary is our own `pregame-splash.mpk` [V].
**Credit:** "Cancain" in the changelog entry and with `Co-Authored-By` on any cherry-pick. Their CLAUDE.md asks for no AI attribution in *their* repo. That doesn't bind ours, but ask before naming them beyond the handle [E].

---

## 2. rambodolphin13 (98 unique commits; release/0.38-dev7 has 84)

**Is it a real continuation?** It is active, but it is now a different game [E].
- It merged upstream up to 936bf23f (2026-10-28) and our Beta 1.1 (e57d2d88) [V]. It lacks upstream's last 35 commits, including "bot armor works like a player's" and the solo-loot pickup fix [V].
- Its main branch has moved on to 0.38-dev8 (10-30). The README now says **"Divided Realms … for the retail 1.130 client"**, with a link to `rambodolphin13/DividedRealms` releases [V].
- The big commit c772b17d (+52.8k lines) moves the game to live 1.130a classes, master levels, champion levels, expansion classes (Death Knight, Valkyrie and others), Maulers and Warlocks, and retires the Sluaghbinder [V].
- It also adds Host for Friends multiplayer over Tailscale, player horses and flying mounts, and a 3D World Workshop editor [V].
- The modern-client direction runs against our 2003-era North Star [E].
- What is valuable for us is its bot fixes, found in large soaks: up to 1,500 bots for 9 hours, and a 3,000-bot run [I, commit text].

**Already in our 1.2:** the effect leak (30M effects, 326 ms/tick), healer cure, animist power, full bag at the medallion merchant, and siege weapons. All are credited to rambodolphin13 in CHANGELOG-LIVING-SERVER.md [V]. Six of its commits are our own cherry-picked fixes (same patch-id) [V].

**Fixes we don't have** [V absent by grep; cherry-pick status from merge-tree]:

| Commit | Fix | Cherry-pick | Verdict |
|---|---|---|---|
| 04f487d4 (Nikse77) | A zone-seam route that turns back on itself counts as a failure (+test) | clean | **Take** |
| eafd143b (Nikse77) | A group member stalled 3 min is moved to its leader; the stall log shows its trail (+tests) | clean | **Take** |
| f0195414, 9dd7df66 (Nikse77) | Ranged-pull throttle (a declined ranged pull no longer starves the normal pull); Necromancer/Cleric chosen as ranged puller only as a last resort | clean | **Take** |
| 9e3a2a88 (Nikse77) | A mezzed enemy is finished once no other free enemy is near | clean | **Take** |
| e9572404, c8935008 (Nikse77) | A bot stuck without a Darkness Falls exit is moved out after 3 min; a bot saved in DF logs back in at its realm's DF entrance (+test) | clean | **Take**. Supports "DF open to solo bots". |
| ba1aaf9e (Nikse77) | A travelling Minstrel keeps only its speed song | conflict (BotBrain) | Adapt |
| 9280e524 | An army inside a keep it just took can use the doors (661 give-ups, 213 teleports) | clean | **Take** |
| e6403cae, fed8e7fc, 1510eb0d, 18d647cd, dde0febc | Parties skip monsters 8+ levels above their average; back out of a pack's reach; uphill retreats; XP level-up serialized (an NRE in SortSpells); bench zone crossings that bots stop short of | conflicts in RouteThreats/ZoneItinerary | **Adapt**. The fixes are good, but our route code has diverged. |
| fa4e98d3 | Gamebots created above level 1 train on arrival ("478 of 900 level-50s" had every line at 1) | clean | **Adapt**. Its forced trainer trip "whatever its task" cuts against the natural-break pacing. |
| a966f171 | Players in RvR zones get `xp_rate × rvr_zones_xp_rate`. Today 1.2 replaces `xp_rate` with the RvR rate, so DF PvE is 1× on a 10× world, while bots already multiply [V]. BeezKneez found the same thing. | conflict | **Adapt** |
| 60d94263 | Disbanding /spawn helpers removes their leftover pets | clean | Take |
| 419a9a46, a67c0a02, ab86be99 | Errors in master-level and Stormforged spells; a realm-switch disconnect on 1.1xx clients; dragon lair height on the 1.130 world | — | Skip. They are modern-client or class specific [I]. |
| 57bb0f11, fbbb82ea | **RunTelemetry**: one JSONL record per loot roll and fight, progress snapshots, mob chase failures, economy, plus `run_telemetry_report.py` | clean | **Adapt**. This is the closest anything comes to our "no AI cost profiling" gap, though it measures outcomes, not CPU [E]. |
| Modern classes, 1.130 client, Divided Realms, Host for Friends, mounts, World Workshop, `modern_item_bonuses`, mythirian loot | — | **Skip** (era) / **Watch** (World Workshop) |

**Owner-rule conflicts:**
- 56cef126 sells mounts at every stable master and removes the Horse Trader NPCs, and 85dd7082 adds personal and flying mounts. Both erode stablemaster routes and the era [V].
- `hybrid_coop_pvp` allows cross-realm groups (off by default) [V].
- Loot, the Realm Exchange, selling and DF are otherwise kept [I].

**EA client files:** none committed, apart from fork-made splash `.mpk`s [V]. Watch these, though:
- `mauler-client-art.json` appears to hold client spell-art catalog rows [I].
- `expansion-trainers-eod.json` comes from Eve-of-Darkness db-public [V].
- f7868790 says the modern patch **carries Marfach's map read from the classic client at build time**. The release assets would then contain EA data [I].
- The 1.130 `game.dll` patches record a hash (39d82bb0) [I].

**Credit:** "rambodolphin13" and "Nikse77 (via rambodolphin13)". Nikse77's commits came in through rambodolphin and aren't upstream [V].

---

## 3. Smaller forks

### BeezKneezItem9
17 commits on the three named branches, 22 with the `beez-test-*` download branches [V]. The base is old 0.35 (d337d184). The rest of its unique refs are pre-rewrite shadowofze release commits [V].

| Change | Verdict |
|---|---|
| A "Developer Ring" (+250 stats, +500 hits), a Buff Stone, 100× RvR XP, two-way bind portals, campfire buffs, simultaneous songs | **Skip** [E]. Power-fantasy features, off the North Star. |
| The DF / RvR `xp_rate` investigation (`BEEZ-DARKNESS-FALLS-RVR-XP.md`) | **Watch**. It confirms the a966f171 bug independently. |
| Viewer-relative enemy identity: hostile bots show race only, with no guild | **Watch** for the "radar" gap [E] |
| Restoring 1.78/1.87 regeneration, Mana Roots, `/spawn refresh` level sync (all with tests) | Watch |

No rule conflicts in what we'd take. No EA files [V]. Credit: "BeezKneezItem9 (Bz1903)".

### MoomoocowPlus
21 commits, 14 non-merge, base 9da90910 [V].

| Change | Verdict |
|---|---|
| Camp-catalog guard: drop monsters with a null `CurrentZone`, which otherwise crashes the whole catalog. Absent on 1.2 [V]; cherry-pick clean. | **Take** (one line) |
| `max_active_bots` cap plus `bot_logins_per_second` throttle. Absent on 1.2 [V]. | **Adapt** |
| `WorldSnapshotService`: JSON world snapshots and an event feed over a Unix socket at 10 Hz, plus "NCA" external-agent brains and a gym | **Watch**. A seed for a test harness or AI profiler [E]. |
| Linux arm64 SQLite; Linux BuildNav and recast-cli port | Watch |

Commits build junk: `RecastDemo.exe`, CMake `.o`/`.bin` files [V]. No EA files [V]. Credit: "MoomoocowPlus".

### scaleysandwich
Seven commits, all on `TOA` (by Hobbyengineer, 10-09). They are SQL and docs for opening Oceanus and importing Hesperos Haven NPCs from DOL db-public [V]. `main` and `Development` have no unique work, and I found no spawn-behaviour commit [V]. **Watch**: ToA arrived late 2003, which is borderline for the era [E]. No EA files [V].

### BlakeFL
Four commits on a 0.35 base [V]: a LAN client launcher and installer, and human players listed in the launcher's population view (with tests). **Skip** (multiplayer). Note: its installer "builds client-only payloads from original release downloads", which repackages EA client files outside git [I].

---

## 4. Upstream 12992146 (shadowofze, 2026-11-01 12:37 −0500)

**What it changes** [V]. It is the exact reverse of cd2bf528's discontinuation, which shadowofze put back 52 minutes after our 50ecb30d (11:45):
- `CharacterCreateRequestHandler`: with `ENABLE_SLUAGHBINDER` on, the Hibernian Mauler slot becomes Acolyte again. With it off, creation is refused.
- `AutonomousBotIdentityGenerator` and the launcher's `BotCharacterGenerator` list the class again (Celt/Firbolg), filtered by the switch.
- `/spawn` offers it only when the switch is on.
- `SluaghbinderEpicQuests.Discontinued` changes from `const true` to `!ENABLE_SLUAGHBINDER`.
- `remove_sluaghbinder_npcs.py` now **refuses unless the setting is literally `False`**.
- The CHANGELOG says it "stays in the 'b' editions".

**Where it is:** on upstream/main only. No tag contains it or cd2bf528 [V]. Upstream says 0.37 is the last release [V], so it will never be in a release tag unless he resumes [I].

**To follow it:**
- `git merge upstream/main` into release/1.2 is clean [V]. But it would bring the class back in code while keeping 50ecb30d's two test edits (`UT_SluaghbinderEditionSwitch`, `ClaudeTakeoverEditionTests`). Those assert "never rolled", so they would then fail [I].
- So merge, then revert 50ecb30d's test hunks, or revert 50ecb30d entirely; its code hunks become no-ops.
- Update our changelog. Pick an edition default: the switch defaults to on [V].

**To keep our removal:**
- Merge with our side for those 7 files (or merge and revert 12992146), and fix 50ecb30d's message: "as in the base" is no longer true.
- Every later upstream merge will touch those files again [E].
- On our worlds the switch is on by default [V], so the NPC removal script will refuse. It needs the setting written `False` first, or our own script [I].

The owner decided 1.2's direction on 2026-11-01. Re-confirm it, now that the base has reversed itself [E].
