# Open work (Living Server)

Everything known to be unfinished, with a GitHub issue each, so contributors and agents can pick an item up. Read
[AGENTS.md](../AGENTS.md) first (the owner's rules), then [CONTRIBUTING.md](CONTRIBUTING.md) (how to pick and claim an
issue) and [DEVELOPMENT.md](DEVELOPMENT.md) (building and testing).

- **Branch from `main` and open a pull request against `main`.** Open it as a draft at your first commit and push every
  checkpoint; run `tools/check.sh` before each push. Only the owner merges.
- **Claim an issue before you start:** assign yourself or comment "taking this" on it, and check that nobody has an open
  pull request or a branch for it. Issues labelled `needs-owner`, `needs-PC` or `blocked` are not free to take
  ([CONTRIBUTING.md](CONTRIBUTING.md)).
- **Changelog fragments:** each pull request adds `changelog.d/<issue-or-branch>.md`
  ([changelog.d/README.md](../changelog.d/README.md)) and leaves `CHANGELOG-LIVING-SERVER.md` alone.
- **This file:** a pull request adds or removes only its own line here; anything more goes on the issue.

A fix counts as working only once it is seen in a log from the owner's PC. Until then it is "tests only" (label
`tests-only`), and every result names the build it came from. Player-facing status is in
[KNOWN-ISSUES.md](KNOWN-ISSUES.md); every change is in [CHANGELOG-LIVING-SERVER.md](../CHANGELOG-LIVING-SERVER.md).

**Status on 2026-11-26:**
- **1.2.2** is the latest full release (2026-11-23, [RELEASE-NOTES-1.2.2.md](RELEASE-NOTES-1.2.2.md)).
- **1.2.3** is in release candidates: `v1.2.3-rc.2` was published as a pre-release on 2026-11-26 ([#709](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/709)); the merges
  for rc.3 are going into `main`. 1.2.3 is now the polish release, with no deadline (owner, 2026-11-26).
- **1.2.4** carries on after it: epic encounters and raids, the alt roster, the held upstream ports and the risky
  navigation work, with no single headline. **1.2.5** holds only the keep patrols the owner is holding. Every open issue
  has a milestone; the plan and build order are on [#738](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/738).
- **Done since 1.2** (closed; what shipped is in each issue's last comment and in the changelog): [#396](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/396), [#397](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/397),
  [#399](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/399)-[#409](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/409), [#411](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/411), [#413](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/413), [#415](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/415)-[#424](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/424), [#437](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/437), [#438](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/438), [#473](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/473), [#475](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/475), [#477](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/477), [#478](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/478), [#482](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/482),
  [#485](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/485), [#487](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/487), [#491](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/491), [#505](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/505), [#519](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/519), [#521](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/521), [#522](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/522), [#525](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/525), [#535](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/535)-[#539](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/539), [#574](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/574), [#598](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/598). Most of them
  are **tests only**: closing an issue does not mean it was seen on the owner's PC. Their notes, the 1.2.2 section, the
  1.2.1 PC checklist and the evidence behind the known-issues list are kept in
  [history/OPEN-WORK-1.2.1-1.2.2.md](history/OPEN-WORK-1.2.1-1.2.2.md).

"Labels:" at an item are its triage labels on GitHub as of 2026-11-27; the issue itself is always the current word.

## 1.2.3: the polish release

The [1.2.3 milestone](https://github.com/chris4aker/OfflineDAoC-LivingServer/milestone/1) (owner, 2026-11-26): every fixable bug and optimization, and the RvR and bot-chat
enhancements we can package, so players notice how polished it has become, and (owner, 22:09) Grafana and the
large ports that fit. No deadline. Grouped and ordered as the
milestone plan, [#738](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/738), describes: anything another item builds on comes first (an arrow means "then").
Build order inside 1.2.3: realism 2b (#479) first, RvR piece C (#530) before pieces B (#529) and D (#531), and #571
before the companion work.

- [#738](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/738) Milestone plan: every unplaced issue into 1.2.4 or 1.2.5, in build order.

### Release, install and CI

- [#709](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/709) v1.2.3-rc.2: contents, build, fresh-world overnight run.
- [#725](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/725) DAoCLivingServer.Setup rejects the current `classic165_period_restored_spawn_ids.txt` (expects 1,290 IDs,
  throws on `#` lines).
- [#732](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/732) Launcher: opened without `START OFFLINE DAOC.cmd`, START SERVER fails silently (no bundled .NET for
  CoreServer). Labels: `needs-PC`, `tests-only`.
- [#687](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/687) CI navmesh set: run the database-needing navmesh tests, and make release builds take navmeshes from the set
  (#684 follow-up).
- [#704](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/704) Installed-navmesh tests that fail on the fresh install's set (10 classes, #687 dry runs). Labels: `needs-PC`.
- [#739](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/739) CI: one build-and-test run per pull request commit, cancel superseded runs, cache NuGet.

### Server

- [#731](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/731) Server memory: data the game keeps grew about 40 MB an hour for 18 hours in the rc.1 overnight run. Labels: `needs-PC`, `tests-only`.
- [#540](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/540) Blocking gen-2 GC pauses of 1.1-2.4 s (49 in 5 h on 1.2.2 `868a82d`) and handles growing about 700 an hour.
  **Fixed in part in 1.2.3, tests only:** the gear checks' allocations (the log's largest allocator, 27-35% of all
  bytes) are gone, the stat tables no longer hold a `Lock` (the 2026-10-22 census's main owner of Windows events), and
  `SERVER_HANDLE_CENSUS` (handles by type, every 10 min) and the memory-load fields of `SERVER_MEMORY` /
  `SERVER_GC_PAUSE` run in every session. Open until a 6 h+ PC log on 1.2.3 shows: `topAllocations` without the tuple
  arrays, the pause count and length, which handle type grows, and whether `memoryLoadPct` is at `highLoadPct` when a
  blocking collection runs (then the fix is memory on the PC or the launcher's "Lean server memory", not a GC setting). Labels: `needs-PC`, `tests-only`.

### Bots and the world

- [#544](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/544) Hibernian bots level about twice as fast as Albion's. **Open.** Albion has the fewest camps per band from 5
  to 19 (world data, owner decision); the 10-19 group XP gap is not explained yet. 1.2.3 adds the sampled
  `AUTONOMOUS_KILL_XP` row and `source/tools/kill_xp_report.py` (tests only); next: one played hour at 10-19 to tell
  shared kills from off-target kills. Labels: `needs-PC`, `tests-only`.
- [#567](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/567) Harvest: healer and support bots (heal triage, HoT precheck, pet buffs, healer queue). Parts are in 1.2.3's
  changelog fragments (`changelog.d/567-*.md`).
- [#576](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/576) Harvest: period spawn audit from DataLoggin live logs (audit only).
- [#728](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/728) Restore period spawns in the Midgard and Hibernia home zones (follow-up to the #576 audit).

### Bot play polish (harvests)

From [upstream/HARVEST-2026-11.md](upstream/HARVEST-2026-11.md).

- [#570](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/570) Harvest: solo levelling life and learned dangers (hotspots, seams, hunting ground, corpse walk).
- [#578](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/578) Harvest: classic starter quests from DOL scripts. Labels: `needs-owner`.
- [#569](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/569) Harvest: True165 party play, pulls and travel files never taken.
- [#571](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/571) Harvest: the player's group and companions (DetShaft fight mode, leader focus, RP shares). Companions #664
  builds on it. Labels: `needs-PC`, `tests-only`.
- [#572](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/572) Harvest: RvR and siege group AI sources (FelixGagnonB 8v8, True165 RvR files, ram seats), with the RvR
  package.
- [#575](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/575) Harvest: period loot and merchants (True165 rows, Thidranki, Uth-Moras audit). Labels: `needs-owner`.
- [#580](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/580) Harvest: client recipe check with craft_extract (separate tool).

### Client polish (OpenDAoC ports)

From [upstream/OPENDAOC-LEDGER-2026-11.md](upstream/OPENDAOC-LEDGER-2026-11.md).

- [#563](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/563) OpenDAoC port: player and GM commands. Labels: `tests-only`.
- [#564](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/564) OpenDAoC port: client display, chat and group window. Labels: `needs-owner`, `needs-PC`, `tests-only`.

### Realism and bot chat

The plan and the audit: [realism-audit-1.2.3/README.md](realism-audit-1.2.3/README.md) (read `structure.md` first).

- [#489](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/489) Bots that pass for players: the realism plan (tracker).
- [#479](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/479) Realism 2b: one reply path, one memory and one typing delay for every channel. Then #480, then #481. Labels: `needs-owner`, `needs-PC`, `tests-only`.
- [#480](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/480) Realism 2c: follow-ups, corrections and nudges keep their subject (after #479).
- [#481](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/481) Realism 2d: a contract on every written line, and style that knows the line's kind (after #480).
- [#483](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/483) Realism 2f: chat that lasts an evening (pools, reactions, pacing, the player included).
- [#484](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/484) Realism 2g: the written lines in the period voice.
- [#486](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/486) Realism 3b: words with acts behind them (orders, brb, camps, thanks, goodbyes). Independent.
- [#488](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/488) Realism 4b: one idle-life layer, the player's own group first. Independent.

### RvR

The plan and the audit: [rvr-audit-1.2.3/README.md](rvr-audit-1.2.3/README.md). Read it before taking any of these. Fix
routes spot by spot; never rebuild navmeshes globally. Piece C (#530) comes before B (#529) and D (#531); most of the
package builds on those.

- [#506](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/506) RvR that passes for the real realm war: the audit and plan (tracker).
- [#530](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/530) RvR work 12 (piece C): one force intent owned by the event layer. **Steps 1-4 built in 1.2.3, tests only**:
  the dead director and `ObjectiveState.Next` deleted, the caller check in CI, one `ForceStrength` siege rule, every
  intent write proposed to the event layer and written once per turn (`RVR_DECIDE what=force`), followers read
  `IntentOf`, the shadow (`what=shadow`). Owed on the PC: `what=destination` and `what=force` show one writer per
  change, and the `shadow.mismatch` count falls to 0. Open: step 5 (the battleground keep as an objective with its wave
  as muster, the small strike with #523, the slow realm force), and then retiring the TempProperty mirror. Labels: `needs-PC`, `tests-only`.
- [#529](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/529) RvR work 11 (piece B): one contact table per bot and group (one perception model, shadow first). Then #531. Labels: `needs-PC`, `tests-only`.
- [#531](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/531) RvR work 13 (piece D): one fight plan with profiles (first pick, PBAoE, realm abilities, keep profiles), after
  #529. Rapid Fire, Camouflage, Vanish and True Sight for bots are left here from #522.
- [#528](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/528) RvR work 10: after the kill (move-on counts the player, true inc calls, kill emotes, realm-rank measure). A
  bug fix. Labels: `needs-PC`, `tests-only`.
- [#532](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/532) RvR work 14 (piece E): one movement owner (no teleports in sight, no statues, melee that moves, the legs
  lease).
- [#533](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/533) RvR work 15 (piece F): one flee model (a chase leash that works, retreats to guards, stealthers that run),
  after #531.
- [#523](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/523) RvR work 5: sieges and defenses decided by who is there (one alarm for any enemy, small strikes, ends on
  quiet, what comes after), after #529.
- [#524](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/524) RvR work 6: where and when the war happens (front and geography weights, siege reasons, the war's clock).
- [#527](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/527) RvR work 9: the player's seat (RvR groups, companions and musters that include the human), after #530 and
  #531.
- [#520](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/520) An end-to-end RvR fight harness in CI. **Built, tests only** (`UT_RvrFightRehearsal`): three report rows in
  the CI log, none a gate until a PC log agrees (evidence.md 5.3). Open: the two fight scenarios do not yet repeat
  exactly by seed (`Random.Shared` in the server's fight code); the world database's spells, specs and keeps are stood
  in. Labels: `needs-PC`, `tests-only`.
- [#526](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/526) World data and map one-offs. **Item 1 fixed in 1.2.3, tests only:** Dun nGed, Da Behnn, Ailinne and Crauchon
  at level 5, with a one-time repair for older worlds (`FrontierKeepLevelRepair`); PC check: `/keep info` at nGed on a
  fresh build and on an old world (`KEEP_LEVEL_DEFAULT_RAISED`, doors at 50,000). **Owner decisions open:** Darkness
  Falls on a fresh or tied world, and the 15-minute grace (item 2; the holder isn't persisted until then). **PC
  (#503):** home-realm circuits (P13), Odin's Gate Hibernian exit (P4), lifts (P5), Midgard's walk to Caer Benowyc
  (P3). Item 7: `GetRealmKeepBonusLevel` is called by `UpdateBaseLevels` when `use_keep_balancing` is on (off by
  default), so it is kept. Item 8 is the battleground-fill issue's. Labels: `needs-PC`, `tests-only`.

#### The RvR package

- [#606](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/606) RvR roadmap: the next RvR package, part of 1.2.3 (tracker).
- [#599](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/599) Multi-group forces outside keeps (2-3 group roams, 16-24 zergs, battlegroups), after #530: it waits on #530
  step 5.
- [#600](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/600) Group tactics everywhere: battlegrounds, keeps and the player's group, after #531.
- [#601](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/601) Roaming eights that find fights: hunt what is seen, milegates and chokepoints, home-realm eights.
- [#602](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/602) Bot realm abilities: the full 1.65 active set and smart use.
- [#603](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/603) Class group roles in a fight: bomb groups, mez/stun chains, healers, the period group make-ups.
- [#604](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/604) Fight voice: group and battlegroup calls in chat (inc, targets, CC, regroup, rez). Needs #529, #531 and
  realism #481.
- [#605](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/605) Fight harness at scale: 8v8 per realm make-up, 16v8, 24v16, battleground storm, seeded and reported (on
  #520).

### Companions in RvR

The owner answered #663 (2026-11-26). Order: #571, #529 and #531, then #664, then #665, then #667 (which also
needs #486).

- [#663](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/663) Companions in battlegrounds and RvR: engage nearby enemies on their own and spread into an attack formation
  (tracker). Labels: `needs-PC`.
- [#664](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/664) Companions: assess and attack nearby enemies on their own in battlegrounds and RvR, not only the player's
  target (after #571, #529 and #531). Labels: `needs-PC`.
- [#665](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/665) Companions: spread into an attack formation once the enemy is engaged in battlegrounds and RvR. Labels: `needs-PC`.
- [#667](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/667) Group controls: let the player order companions and their bot group in a fight (attack, assist on me, kite
  back, PBAoE, hold, regroup). Also needs #486. Labels: `needs-PC`.

### Grafana Cloud kit

See [GRAFANA.md](GRAFANA.md) and [LOG-ROWS.md](LOG-ROWS.md). Kit 1 (#647), then existing agents (#650), then one
pipeline (#651), then the dashboards and extras. Only #643 stays `needs-owner`.

- [#647](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/647) Agent-run setup on Ubuntu and Windows, with or without a Cloud account and Alloy. Then #650, then #651.
- [#650](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/650) Kit 2: existing agents (legacy Grafana Agent migration, Alloy module option), after #647.
- [#651](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/651) Kit 3: one pipeline on #628's fail-closed filter, with the metrics side, after #650.
- [#656](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/656) Dashboards must work on Windows (the server-running gate uses Linux-only metrics).
- [#643](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/643) Developer Grafana Cloud stack: collaborators ship filtered rows to one shared Cloud stack (never in player
  releases). Labels: `needs-owner`.
- [#653](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/653) Kit 5: game-side meters (bots online by type and realm, metrics interval key, MetricsEnabled switch). Answered; in PR #747. Labels: `tests-only`.
- [#655](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/655) Alerts and SLOs (split from #652).
- [#657](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/657) Host panels for players without node_exporter.
- [#658](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/658) Upgrade from a hand-run copy of the pipeline (job label, port, positions).

### Upstream ports (fixes and polish, ported by hand from OpenDAoC)

From [upstream/OPENDAOC-LEDGER-2026-11.md](upstream/OPENDAOC-LEDGER-2026-11.md).

- [#562](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/562) OpenDAoC port: housing, guild and market. House-vault and guild-house crash safety, consignment fixes. Labels: `tests-only`.
- [#565](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/565) OpenDAoC port: quests, gravestones and timed services. The quest-list race fix and periodic quests. Labels: `needs-owner`, `tests-only`.

### Checks on a PC

- [#425](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/425) 1.2.1 PC check: the original client's handshake build, and the bot death fall. **Open.** CI's playtester
  scenarios `bot-death`, `bot-corpse-target` and `bot-rez` pass (on `b644529a`), but they check packets, not what the
  client draws. The full 1.2.1 checklist is in [history/OPEN-WORK-1.2.1-1.2.2.md](history/OPEN-WORK-1.2.1-1.2.2.md). Labels: `needs-PC`.
- [#476](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/476) Realism: PC session checklist (what only a played game can settle). Labels: `needs-PC`.
- [#503](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/503) RvR audit: PC frontier session checklist (what only a played game can settle). Labels: `needs-PC`.

### 1.2.3 work without an issue of its own

- **Uthgard drops:** run `source/tools/uthgard_drops_content.py` (dry run first) as [UTHGARD-DROPS.md](UTHGARD-DROPS.md)
  says; the tool is in the code since 1.2.1, and its tables ship in 1.2.3 (owner, 2026-11-22).
- **PC checks owed by the merged port packages** (all **tests only**):
  - P1, server core: a long session's log shows the periodic saves and no growth in held deleted objects. Not ported:
    the quest-list locking refactor (`22197f1f6`), the equipment broadcast rework (`ec54287b1`) and the UDP receive
    hardening (`a981b22f7`).
  - P2, effects, spells, pets: re-run the raid sims under [RAID-SIZES.md](RAID-SIZES.md) (caster output on boss fights
    changes; no size was changed); casting under melee (player and gamebot), bow draws under fire, Bonedancer focus
    snare on a monster, Speed of Sound with a snare, and a Minstrel charm under recast. Left for later: `1713a0e8f` +
    `9f0bd91b1` (savage buffs, resist buff cap), `42024abdc` (failed spells and cooldowns; check bot retries first),
    `3374caf2c` (needs a world data change).
  - P3, combat, XP, crafting and items. Left for later: the attack-speed recalculation series in `AttackAction`
    (`6258566a3`, `27bc0293f`, `a7468c046`, `109a38cf0`).
  - P4, NPC AI, pathing, keeps and world: a monster or guard landing a spell after its target stepped behind a wall; no
    monster or gamebot cast through a dungeon or keep wall away from the player; a mob climbing a ladder without
    stalling; a mob chasing over a zone border; a gravestone still standing after a restart; group dots on the minimap.
    Re-run the raid sims before any period size decision (bosses now land started casts). Finding for #521:
    `BotCastSight` checks bot-to-bot casts at any height, so a bot caster on a keep wall may be refused at bots below
    (the navmesh ray can't cross a drop); shots use the one-floor rule.
  - P5, the Offline DAoC forks: a two-Bard group's endurance with the song up (`EFFECT_STALE_COPY_RETIRED`); a grouped
    Bard keeping its drum and song in a fight; a group through a keep door without freezing; no camp shared by two
    parties (`AutonomousMobClaims`); `AUTONOMOUS_DEFENSIVE_PULL_IGNORED` runs of two at most. Left for later: True165's
    role sheet (d2fc3fe3d needs its BotBrain.MainAssist), its Mag Mell release spots (needs its BesideBindstone) and
    the Trollheim pillar follow fix (b390827f).

## 1.2.4

The [1.2.4 milestone](https://github.com/chris4aker/OfflineDAoC-LivingServer/milestone/2): what carries on after the polish release, with no single headline (owner,
2026-11-26). Order as in [#738](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/738).

### Epic encounters and raids

Never weaken a boss. A raid plan counts as working only once it is won on the owner's PC; most of these need the
owner's raid log first. 1.2.2's size raises (#441) were all withdrawn (2026-11-22 and, on audit, 2026-11-23); the
raise rule is in [RAID-SIZES.md](RAID-SIZES.md).

- [#410](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/410) Summoner's Hall: Summoner Roesia wins every time, and Grand Summoner Govannon has never been beaten.
  **Open;** 1.2.2's bigger raid (#441) was withdrawn. Labels: `needs-PC`, `reruns-owed`.
- [#412](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/412) The Green Knight and Marfoirwort: bot raids lose. **Open;** 1.2.2's bigger Green Knight raid (#441) was
  withdrawn. Labels: `needs-PC`, `reruns-owed`.
- [#414](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/414) Behemoth, Fomor City and Morgana: no bot raid yet. **Open.** 1.2.3: Fomor City has its raid
  (`dungeon-hibernia-fomor`, a zone180.nav walk-link patch and the period's named order; **tests only**, for the next
  sweep). Behemoth: no raid, by design (Mythic, 2008: "not killable"). Morgana: no raid, a quest host in the period too;
  a raid would need her changed (owner's call). Labels: `needs-PC`, `tests-only`, `reruns-owed`.
- [#644](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/644) Raid and epic encounter telemetry: log why raids win or lose (attempts, fight timeline, deaths, mechanics,
  roles, threat, funnel). Labels: `tests-only`.
- [#730](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/730) Green Knight sim: Might of the Forrest lands around his target in the model, around him in the script. Labels: `reruns-owed`.

### The alt roster

- [#631](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/631) Alt roster: bot players own up to 3 characters (the 2026-10-12 design). Planned for 1.2.4 (owner, 2026-11-26: one item, not a headline). Its "before building" asks to check low-level combat first, so #570 and #578
  (both in 1.2.3) come first.
- [#573](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/573) Harvest: bot memory, social acts, gear value and economy from True165 (builds on the alt roster's shared
  memory).

### Held upstream ports

Held for playtest evidence or owner decisions; they can't fit until those logs exist.

- [#613](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/613) Upstream ports held for evidence or an owner decision (from P2, P4 and P5). The packages themselves are
  merged into 1.2.3 (changelog 1.2.3, "From OpenDAoC upstream" P1-P4 and "From the Offline DAoC forks" P5), **tests
  only**; their PC checks are under 1.2.3, "1.2.3 work without an issue of its own". Ledger: [upstream/OPENDAOC-LEDGER-2026-11.md](upstream/OPENDAOC-LEDGER-2026-11.md);
  harvest: [upstream/HARVEST-2026-11.md](upstream/HARVEST-2026-11.md). Labels: `blocked`.

### Risky navigation work (evidence first)

- [#581](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/581) Harvest: geometry line of sight for mobs and bots (Amtenael LosCheckMgr). Touches navigation: evidence
  first; needs the PC.
- [#582](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/582) Harvest: single-zone navmesh rebuild tooling (Uthgard buildnav). Touches navigation: evidence first; needs
  the PC.

## 1.2.5

The [1.2.5 milestone](https://github.com/chris4aker/OfflineDAoC-LivingServer/milestone/3): held by the owner (#577, 2026-11-26: "Patrols are held for later"). Listed to keep them
visible, not to schedule them.

- [#577](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/577) Harvest: keep patrol paths from db-public. Labels: `needs-PC`, `blocked`.
- [#707](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/707) Roadmap: patrols for the classic frontier's keeps (generated routes, period check first; owner, 2026-11-26:
  not for 1.2.3).

## Lore

Read [docs/1.2.1/HEROES.md](1.2.1/HEROES.md) and [docs/1.2.1/HANDOFF.md](1.2.1/HANDOFF.md) first. Every batch gets an
independent review, and the owner sees samples before it merges. No lore issue is open (#399-#406 are done).

## Not planned

- [#209](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues/209) Crownfire Lance (Pendragon Sovereign): not
  part of Living Server (closed).
- Random magic items and coin stay as they are (owner, 2026-11-21); Uthgard's drops are added on top.
