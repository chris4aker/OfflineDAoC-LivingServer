# The release train to Living Server 2.0

Owner: Chris. Chosen 2026-10-30: **2.0 is the destination, not the next release.** The work to get there (crafting
and the economy, live events, questing complete, the guild package, the historical server, and the fixes the
community is reporting) ships as a train of public releases, each played before it goes out. 2.0 is the stop where
the realm is complete.

This file is the plan of record. The design behind each stop lives in its own document, and each stop's checklist
is ticked here as it is done. The older plans ([RELEASE-PLAN.md](RELEASE-PLAN.md), the Living Server
[backlog](LIVING-SERVER-BACKLOG.md)) stay as the dated record. Text a later decision replaced is kept below, marked
**superseded** with the date, not deleted.

A few documents named here are still on their own branches until those merge: `BOT-BUILDS-AND-RVR.md`
(`plan/rvr-builds`), `NORTH-STAR-AUDIT.md` and `audits/2026-11-03/` (`audit/north-star-2026-11-03`, PR #58). PR #58
was closed without being merged; its branch still holds the files.

## The split (owner, 2026-11-06)

The owner split the plan into three releases, so that what is already built and checked reaches players now and
the large new systems each get their own release:

- **1.1.2 now:** the combined candidate (PR #66: #44-#46, #50, #60-#65) plus the raid fixes (#73 Epic Special
  Forces join full raids, #75 raid sizes and the win balance, #77 Marfach/Krondon set-aside rules, time-outs without
  a wipe count as neither, siege logging). The release candidate is `test/1.1.2-rc`.
  - Changed the same day (owner, 07:13): **the epic encounter catalog and the first raid tactics ship with 1.1.2**
    (#81, #83, #85), not 1.2.
- **1.2 = "bots that play with you":** what's built on the 1.2 line: bot-led groups B1-B4 (#74, #78, #79, #84) on the
  duty lease (#71), RvR perception (#80), replies that don't give bots away (#82), the quest harness (#70), the
  battleground keep rally (#69), the automated playtester (#72) and the AI profiler (#67). (As first decided it also
  held the epic encounter catalog and raid tactics, #81/#83/#85; moved to 1.1.2 at 07:13.)
- **1.3:** live events, the guild social package with guild tiers, a crowded prime time, a server with a past, and
  era builds from the Wayback research.

Changed 2026-11-07 (owner): **1.3 and a "bots as people" stop become 2.0** ("I think 1.3 and 1.4 become 2.0"). After
1.2 (and the 1.2.x friends stop) the next release is 2.0; its section below lists everything 1.3 held, the bots as
people work and the complete realm.

This replaces "1.2 is one comprehensive update" (2026-11-01) and the 1.3 written on 2026-11-03; both are kept below,
marked superseded.

Changed 2026-11-10 (owner): **the epic encounters and the lore are part of 1.2, not a 1.2.1.** Asked whether 1.2 should
ship as its headline alone with the epic encounters and lore as a 1.2.1, the owner chose one release: "I want 1.2.1 in
1.2. I feel like the lore and epic encounters is a huge massive part and main selling point." So 1.2 is bots that play
with you, the epic encounters (the restored boss mechanics, the raid tactics and the raid skill layer) and the lore.
Play with friends stays the stop right after it (1.2.x); the Linux build stays parked. Beta 1.1.3 (published
2026-11-09) is merged into the 1.2 line.

Changed 2026-11-12 (owner): **1.2 is held until it works properly** ("I don't want it to be slop"); the list that decides
it is [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md). The same day the owner took over the classic (old-client) Offline DAoC
line, hosted from his own downloads, and shipped the downloader fix as Beta 1.1.4.

Changed 2026-11-14, evening (owner): **every big boss is in 1.2**: the Darkness Falls raid AI and Legion (they had been
left out), Behemoth, the other level 70+ encounters, and the big bosses that have only the generic dungeon route today
(Apocalypse, Hurika, the Glacier's icelords, Galladoria's primals, Balor and others) or no raid at all (Morgana, the
Glacier Giant and the other open-world bosses).

Changed 2026-11-13 (owner): **play with friends and the character passport ship in 1.2**, not a 1.2.x. Linux is its own
release after 1.2: the 0.37 base plus Living Server, no Godot client carried forward.

Changed 2026-11-14 (owner): **shadowofze's True 1.65 boss and raid fixes are taken into 1.2**, cleanly consolidated with
Living Server's own (merged as `feature/1.2-from-true165`), and True 1.65's Prima encounters and corrections go into the
next world update, inert until then. Separately, Living Server gets **its own edition of True 1.65**
(`chris4aker/OfflineDAoC-True165-LivingServ`): 1.1.4 is carried over now (`living-server/1.1.4`, features that clash with
1.65 rules off by default), 1.2 after it ships. That edition is its own track and does not hold 1.2.

Changed 2026-11-14 (owner, later that night): **the True 1.65 edition gets 1.2 only once 1.2 is fully released on the
0.37 base.** Until then the edition stays at 1.1.4 (`living-server/1.1.4`); no 1.2 work is ported to it before that.

## Rules for every stop

- A stop ships only after its in-game check on the owner's PC, reported separately from automated tests
  (AGENTS.md). Automated tests and the rehearsal transcripts are reported as what they are.
- One major system at a time in the overnight soak runs, so a lag or a regression can be attributed.
- Release notes show the work and the research and never spoil the Easter eggs or the internal vocabulary.
  - Teased features (owner, 2026-11-15): the storytellers, fireside tales, loremasters and the Epic Special Forces.
    The notes hint at them without explaining them or naming the eggs.
  - The era (owner, 2026-11-15): Trials of Atlantis and Catacombs appear only as rumours, from a lore perspective,
    never as played content ([bot-voices/WRITING-GUIDE.md](bot-voices/WRITING-GUIDE.md)). Post-2003 jokes are cut.
- Every base release from shadowofze is merged first (the checklist is [UPSTREAM-RELEASES.md](UPSTREAM-RELEASES.md));
  Living Server never undoes a base change. Since 2026-10-31 there is one last base state and no more releases
  (see "The base" below).
- Community issues and pull requests go into the next stop, not the next major.
- Versions: patch stops are `1.x.y`; the number 2.0 is kept for the complete realm. If the number matters for a
  Reddit moment sooner, the same stops may be tagged `2.0-beta.N` instead; that is the owner's call at the time.

## Why a train and not one 2.0 bundle (decided 2026-10-30)

- Live events is five milestones, crafting seven, the guild package one large one, questing a program of its own;
  every milestone needs a played check, and the owner plays about five hours a week. One bundle is months of
  silence after a release a week.
- The community arrived the day after 1.1: 90 Setup and 20 zip downloads in the first day, six issues from three
  users, a synced fork offering pull requests. A patch stop in days keeps them; a 2.0 months out loses them.
- Crafting moves what bots wear and where gold goes; events move spawns, rallies and tick cost. Soaked together
  they can't be told apart (issue #25 was found because nothing else was moving).
- 0.38 was expected to land in the middle (see the next section: it won't be released), and quest fixes are world
  data like 0.38's own changes.
- 1.1's eight branches merged without conflicts because they were small and separate; crafting, events, quests
  and guilds all touch valuation, town routing, chat budgets, raids and voices.

## The base: shadowofze has stopped (checked 2026-10-31, rechecked 2026-11-03)

- His README (2026-10-31) says the project **is on hold and will not receive any more updates**; the 0.37 / 0.37b /
  0.37c downloads stay. There is **no `v0.38` tag**.
- Living Server merged his final `main` (`cd2bf52`) into `release/1.2` on 2026-11-01 (decision 3), and his one later
  commit (`12992146`, 2026-11-01: the Sluaghbinder follows the edition switch again) on 2026-11-03.
- Rechecked 2026-11-03 together with every active fork: [audits/2026-11-03/FORK-SURVEY.md](audits/2026-11-03/FORK-SURVEY.md)
  (on the audit branch, PR #58). No fork continues 1.65 Offline DAoC as a whole; several carry fixes and tools worth
  porting, listed per stop below and credited to their authors.

## The North Star and how the order was set (2026-11-03)

The goal (owner): the most amazing DAoC experience offline with bots and a world that feels like a fully alive peak
server from the era, with added features and Easter eggs. The order below comes from the North Star audit and the
reply to the independent audit ([NORTH-STAR-AUDIT.md](NORTH-STAR-AUDIT.md), PR #58): measure and protect first, then
the cheapest things a player feels (being invited, replies that don't give bots away, a crowded prime time, a
server with a past), with the two small foundations that stop real failures (a record of who owns a bot's time, a
cap on how many bots notice one target). Era authenticity breaks ties; it doesn't veto aliveness.

## The stops

### 1.1.1 — the community's first week (merged; publishing is the owner's step)

- [x] Candidate A merged to `main` as Beta 1.1.1 (PR #47, `15f521e`): the doc drift (#41), party disband (#35),
      base-class styles (#36), quiet song twisting (#37), the quest-marker colour (#42), the launcher port probe (#43),
      the SQLite test wait (#48), unique install backup folders (#49)
- [x] The GitHub release `v1.1.1-beta` published (owner, 2026-11-03), Setup built and tested on Windows by the
      installer workflow and attached

### 1.1.2 — safe and correct, plus the raid fixes (release candidate 2026-11-06)

The release candidate (2026-11-06) is `test/1.1.2-rc`: `test/1.1.2-combined` (PR #66) with #73, #75, #77 and #85
(which carries #81 and #83) merged in that order, then the readiness review's #87 (the flaky launcher preview test,
test side only), #89 (the played check, [PLAYED-CHECK-1.1.2.md](PLAYED-CHECK-1.1.2.md)) and #88 (the siege balance
trims the attackers' cap once the defenders' share is at its limit; owner approved 2026-11-06 with its defaults: the
next run may start at 96 attackers per keep, same-night retakes keep counting). Draft release notes:
[RELEASE-NOTES-1.1.2.md](RELEASE-NOTES-1.1.2.md).

- [ ] **In the candidate, waiting for the owner's merges:** save safety and network defaults (#64), release gates
      (#61), ported fork fixes (#62), gamebots in your guild and power regeneration (#63), updates from the launcher
      (#60), quests with bots (#50: #44, #45, #46), the chat track (#65, folded in 2026-11-04), the Epic Special Forces
      joining full raids (#73), raid sizes and the win balance (#75), Marfach/Krondon and siege logging (#77), the
      epic encounter catalog (#81) and the first raid tactics (#83, #85: Scurceol's orbs, Black Lady fought at home,
      Aidon's illusions, Nosdoden's waves, Xanxicar's stomps, bosses that jump the raid early, lost skirmishes that
      don't use an attempt). The 22 original boss mechanics restored behind switches stay off. Darkness Falls raid
      AI, Legion and the hardest level 70+ encounters are not implemented
- [ ] One overnight soak of the candidate's build on the owner's test copy
- [ ] The owner's short played check: guild dues and merit, a quest in a bot group, chat feel in Camelot
      ([PLAYED-CHECK-1.1.2.md](PLAYED-CHECK-1.1.2.md))
- [ ] One real 1.1.1 -> 1.1.2 package install on Windows (so far checked on paper and in CI's fake game folder only)
- [ ] Before the tag, in one commit on `main` (the `versions` CI job checks they agree): the changelog heading
      "Beta 1.1.2 (in development)" becomes "Beta 1.1.2 — <date>", `ForkBuildInfo.BuildName` stays `1.1.2-beta`, and
      README.md says "**Beta 1.1.2 is out.**" with its release links, file names and `FORK_BUILD name=` on 1.1.2
- [ ] Publish `v1.1.2-beta` as Latest (not a pre-release), with the zip built by a push run on `main` attached as
      exactly `OfflineDAoC-LivingServer-1.1.2-beta.zip` (not the pull-request run's, whose BUILD-INFO says
      `Branch=86/merge`). The release workflow builds Setup and fills in **only Setup's** SHA-256 (from the placeholder
      `(filled in when the exe is built)`); the zip's SHA-256 is filled in by hand. Then close #61-#65

The checklist as written on 2026-11-03 (its items are now in the candidate above unless noted):

- [ ] **Save safety:** the owner's `runtime/server/config/serverconfig.xml` checked (the default string turns the
      journal and sync off); WAL with `Synchronous=Normal`, a daily online backup with an integrity check and
      rotation, and a backup before every install (lometur's HearthDAoC has working code); and the network
      defaults checked in the same file: the code's defaults listen on every interface with router port-forwarding
      (UPnP) on, and a missing config is rewritten with them. Defaults become this PC only and UPnP off, the release
      build asserts them, and UPnP mappings are removed on stop ([coop_assumptions.md](audits/2026-11-03/coop_assumptions.md))
- [ ] **Quests with bots** (PR #50: #44 dragons call the Special Forces, #45 quest XP by level range, #46 group kill
      credit and hand-ins), after the three-quest A/B on the test copy
- [ ] **r/daoc fixes:** the Herald counts bots in a player's guild; bots in a player's guild earn merit and pay dues
      (owner decision); launcher options for mana regeneration and the below-50% halving, one rule for bots and
      players
- [ ] **Ported fixes:** grouped bots dying in a battleground go to the portal keep, `/friend` for gamebots, two stock
      friends-list bugs, `/allrelease` (Cancain; issues #38, #39, #40); RvR and Darkness Falls XP rate on non-1×
      worlds (rambodolphin13, BeezKneezItem9); Nikse77's six bot fixes (zone seams, group stalls, ranged pulls,
      mezzed enemies, Darkness Falls stuck and re-login) and leaving a keep just taken (via rambodolphin13); water
      breathing (Rmarcoux86); the camp-list guard (MoomoocowPlus)
- [ ] **Release gates:** the temp-file cleanup race in `UT_AtomicExchangePersistence`, CI on pushes to `main`, a
      README/version check, `main` protected with the owner able to bypass (owner approval); a release workflow that
      builds and tests Setup, attaches it to the owner's release and fills in its SHA-256 (the owner only publishes)
- [x] **No EA client files:** the 54 inherited from upstream removed (PR #59, merged into `release/1.2`); history
      kept. The origin of the two splash `.mpk` files and `connect.exe` to be confirmed by the owner
- [ ] PR #51, Setup's fallback copy of the base files: an owner and legal decision before it merges. Not in the
      candidate. Checked 2026-11-06: it merges onto the candidate with two small conflicts beside #60's `--info` and
      `--no-update` (both kept), and Setup builds; the copy repository it needs doesn't exist yet
- [ ] Played: the three-quest A/B, a battleground death in a bot group, a guild of bots on the Herald

### 1.2 — Where Legends Wake (state on 2026-11-14)

Headline (draft release notes, [RELEASE-NOTES-1.2.md](RELEASE-NOTES-1.2.md)): the epic encounters at full strength with
bots that have to survive them, a story for every dungeon, dragon and boss, and bots that play with you. Everything
below is built and merged on `integration/1.2`. What decides the release is [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md):
each row counts only when seen on the owner's PC on the release-candidate line.

| Part | Built | Seen on the owner's PC |
|---|---|---|
| Epic encounters: the bosses' own rules back on (15 of 20 settings on), throws and jails that take bots, add caps | Merged | Partly: raids fought with them since test build 2 |
| Raid tactics (about 40 encounters new or reworked), the raid skill layer, the raid flow after a wipe | Merged; the sweep fixes of 2026-11-14 (#211-#214) are tests only | Dragons and the smaller dungeons won on earlier builds; Midgard's Darkness Falls (Legion) won on `e3b8238`; Midgard's Summoner's Hall (Grand Summoner Govannon) won once on `3e64140` (run 2); **Caer Sidi, Tuscaran Glacier, Galladoria, Trollheim, Krondon and Summoner's Hall (Albion, Hibernia) not won by bots on the PC yet** |
| A raid is a realm event: the boss's tale, then the call and the horn; one raid every four hours | Merged | Seen on test builds 3 and 4 |
| The lore: 108 places, 578 bosses, 4 realm histories; loremasters, books, the Herald, the Bestiary, `/lore`, boss lines, atmosphere lines | Merged | Tales and boss lines seen on earlier 1.2 builds; loremasters, books, Herald and Bestiary have no played record |
| Tales: the storyteller, asked-for tales, fireside tales | Merged | Seen on earlier builds; a tale that starts by itself not seen |
| Bots that play with you: bot-led groups B1-B4, raid invites, RvR perception, replies that don't give bots away, cosmetics | Merged | Bot-led groups, a raid invite and dyes seen on builds 3 to 6; RvR perception has no played record |
| Play with friends and the character passport (experimental) | Merged | A second client joined and made its account (build 7); no session played together, no passport trip between PCs |
| Movement, flanking and style choice; ports from other forks and True 1.65 | Merged | Tests only |
| Every big boss (in 1.2 since 2026-11-14): the Darkness Falls raid AI, Legion, Behemoth, the level 70+ bosses with no plan yet, the open-world bosses | Being built: Legion; the rest of Darkness Falls; then the epic dungeons' remaining bosses and the open-world bosses | Before this work, on `e3b8238`: Midgard's raid killed Legion in one attempt; Hibernia's lost to him twice |
| Install: 1.1.4 updated through Setup; a new install | Built | Not done |

Back in 1.2 (owner, 2026-11-14, evening: "Put Darkness Falls raid AI and Legion back in", then "No ALL big bosses are
in"): the Darkness Falls raid AI, Legion, Behemoth and every other big boss, the open-world ones included.

Changed 2026-11-15 (owner, answers to the roadmap review; all 23 are recorded in
[PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md), "Owner decisions 2026-11-15"):
- **Raid code is frozen** since #240 (`35d52b6f8`): only fixes for bugs seen on the owner's PC, each re-arming a
  re-run of the raids it touches, listed in the gate.
- **Signed off:** Behemoth is never fought; Fomor City and Morgana have no raid.
- **Sweep raids count toward the raid bar.** The Beta 1.0 items never seen become *must* rows. A class-off smoke
  session on a 0.37 world comes before tagging. The overnight row has a stall threshold (no stall of 2.0 s or more, at
  most 5 of 1.0 s or more an hour, over 8 hours), and it stands in for the L600 baseline, which moves to after 1.2.
- **The new client (0.37c) is maintenance-only:** fixes only, no new features, and no rows on the gate.
- Nothing of the economy ledger is in 1.2 (it is 2.0's first milestone).

Left, in order (2026-11-15):
1. Merge Beta 1.1.4 (`main`) into `integration/1.2` (owner decision 1).
2. The battleground test on `3d67b6f` with the Lv.20 button (#242), and RvR perception as a non-GM character.
3. The re-runs the gate lists (Caer Sidi, Tuscaran Glacier, Galladoria, both Summoner's Halls, Trollheim), and the
   missing wins for Midgard's and Hibernia's bars, on a candidate with the fixes.
4. The Beta 1.0 items and the hands-on rows the gate still lists.
5. Install checks: Setup over 1.1.4, a new install, the class-off smoke session, the overnight row.
6. Release notes finished from what was seen; merge to `main`; publish (the owner's step).

Left, in order (2026-11-14; superseded 2026-11-15 by the list above):
1. The sweep on test build `16b3054` (installed on the test copy 2026-11-14, server running): `/epictest sweep start all`.
2. Fix what the sweep shows; repeat for the raids that still lose.
3. The short hands-on checks with the owner (bot-led group, raid invite, a tale starting by itself).
4. Install checks: Setup over 1.1.4, and a new install.
5. Release notes finished from what was seen; merge to `main`; publish (the owner's step).

### 1.2.x — after 1.2 ships (owner, 2026-11-15)

Patch stops after 1.2, before 2.0. Nothing here starts before the raid freeze ends and the 1.2 gate is met.

- [ ] **A player-called realm raid:** a player command calls their own realm's next raid, with the existing cooldowns.
      Today only the four-hour schedule, the launcher and GM commands start one, and at five hours of play a week the
      schedule often misses the session.
- [ ] **The small chat-only Easter eggs** from the review of 2026-11-15, each a chat line built on data the server already
      keeps (names and details in [the idea backlog](#the-idea-backlog-owner-2026-11-15)). A new `bot_living_*` switch
      ships on unless its default is False.
- [ ] **The L300 and L600 baselines** (moved here from 1.2: "after 1.2"). For 1.2 the gate's overnight row with its stall
      threshold stands in for them.
- [ ] Not placed yet: Linux (PR #206) stays "after 1.2".

### 1.2 — bots that play with you (owner 2026-11-06; checklist as of 2026-11-07, superseded 2026-11-14 by the section above)

Headline: the world's gamebots play *with* you: they invite you, lead groups you can simply follow, see you in RvR
the way a 2003 player would, and answer without giving themselves away.
Everything below is built on branches with green automated tests; nothing is merged into `release/1.2` or played yet.

- [ ] The duty lease record (#71): raids and RvR events claim a bot's time with a priority; the watchdog exempts
      leased bots; reason codes
- [ ] Bot-led groups: B1 gamebot leaders invite you and you can follow (#74); B2 `/lead`, `/lead bot`,
      `/makeleader`, `/invites` and the join mode (#78); B3 the raid leader invites you (#79); B4 replacements, the
      natural break and the pull call (#84)
- [ ] RvR perception 1.2 (#80): an observer cap per target, calibrated time to notice, duty, pet caps
- [ ] Replies that don't give bots away (#82): the reply budget, reason codes, `/lswhy me`
- [ ] More raid tactics, if the 1.1.2 ones hold up in play: the next restored mechanics turned on one at a time as the
      raid learns them (the catalog's open question). The catalog and first tactics themselves moved to 1.1.2
      (owner 2026-11-06 07:13)
- [ ] The quest harness, repeatable quests and deliveries, epic chains linked without Python (#70)
- [ ] Battlegrounds: realms rally inside the breach and take the keep lord together (#69)
- [ ] The automated playtester: a scripted player, `make_test_world.py`, eight scenarios (#72)
- [ ] `AI_PROFILE`, the per-system AI cost profiler (#67), and its baselines before the systems above are soaked
- [ ] Already on `release/1.2` and shipping with it: the base's final `main` and the Sluaghbinder edition switch; the
      EA client files removed (#59)
- [ ] Not placed by the split, for the owner to place: his world scripts as Setup steps; the new-client scripts
      (#54); era build weights (#55) go with 1.3's era builds
- [ ] The epic encounters (owner 2026-11-10: part of 1.2): boss throws, teleports and jails that take bots too, the
      restored mechanics of the dragons, Xanxicar, Galladoria, Tuscaran Glacier, Summoner's Hall, Caer Sidi, Krondon,
      Avalon City and the Darkness Falls princes, each a setting; raid tactics round 3; the raid skill layer (the leader
      calls, bots react in their own time, the realm learns a boss). Not in it: a Darkness Falls raid AI, a Legion raid
      plan, and the hardest level 70+ encounters
- [ ] The lore (owner 2026-11-10: part of 1.2): the lore engine, atmosphere lines, boss yells and mechanic lines,
      loremasters, books, the Herald chronicle and bestiary, the new client's codex
- [x] Beta 1.1.3 merged into the 1.2 line (its chat, raid and siege fixes; #174, 2026-11-10)
- [ ] Tales (owner 2026-11-10: "I love the fuller version", "it needs to be in 1.2"; [TALES.md](TALES.md)): the realm's
      storyteller asked for a boss's story in /faction, hushed and told, the quiet after it, a rumour that is sometimes a
      raid really forming, a player's own request and questions, fireside tales in a resting group. Played: one tale
      heard to its end, one asked for, one that led to a raid, one fireside tale
- [ ] Soaks one system at a time; played: invited into a group and a raid, following a bot leader, a battleground
      session; for the epic encounters and the lore, one realm's raids at a time in overnight runs on the test copy,
      each fight counted as working only once it is seen in a PC log, and the release notes say plainly which fights
      the raids still lose

### 1.2 as planned on 2026-11-01 — the comprehensive update (superseded 2026-11-06 by the split)

Kept for the record. Phase B's groups and RvR became the new 1.2; phases C and D, live events and the era builds
moved to the new 1.3.

Must-haves ship 1.2; anything from a phase that doesn't fit ships as a 1.2.x patch rather than holding the release.

**Phase A — foundations.**
- [x] The base's final `main` merged (`1ac3406`, `ebbaf83`); the Sluaghbinder follows the edition switch as in the
      base (owner 2026-11-03)
- [ ] `AI_PROFILE`: per-system and per-pulse cost and tick p50/p95/p99 once a minute, from lometur's CPU and
      allocation meter plus stefanrows's bot think-time profiler; then the L300 and L600 baselines on `release/1.2`
      before any new system lands
- [ ] The test world (`make_test_world.py` from the clean release world, no saves) and a scripted player (starting
      from lometur's headless client); nightly scenarios: login/save/restart, a kill quest in a bot group, a delivery
- [ ] The duty lease record: raids and RvR events claim a bot's time with a priority; the watchdog exempts leased bots;
      reason codes; `/lswhy` for bot decisions
- [ ] His world scripts as Setup steps with row-level rollbacks (quest reward stats, Necromancer creation, the night
      spawns; `remove_sluaghbinder_npcs.py` only on worlds with the class off)
- [ ] The new-client scripts (PR #54, without the APPRAISE error) and his shippable client files (issue #34)
- [ ] Soak: a week of overnight runs, the siege and raid ledgers compared with 1.1.x

**Phase B — groups and RvR** ([BOT-BUILDS-AND-RVR.md](BOT-BUILDS-AND-RVR.md), the group framework in PR #57).
- [ ] **You are wanted:** bots invite the player; bot-led groups the player can follow without leading (r/daoc
      2026-11-03), with a toggle back to leading; a raid leader bot who invites the player
- [ ] RvR perception (PR #53) with an observer cap per target and calibrated time-to-notice; then "watch the fight
      before joining", varied routes out of the hub, memory of where groups died, a new leader when one dies
      (stefanrows)
- [ ] Pet caps per group and warband; era build weights (PR #55, provisional, with stefanrows's 118 plans as a second
      source); stances, a named puller and main assist, the discipline trait
- [ ] Soak and the battleground session measured before and after

**Phase C — a crowded prime time** (the chat framework in PR #57, the blind evaluation in PR #56).
- [ ] Replies: silence below a reply minimum (except direct tells and a bot's name), self-knowledge, context fits;
      a separate ambient budget with floor and ceiling by hour and place
- [ ] The era's channels with reach: public chat groups, town `/broadcast`, the LFG flag; `/lfg`, `/trade` and
      `/faction` kept as labelled conveniences, on by default
- [ ] A "2003 server" population preset (a level curve, earned gear), aged by running the world fast while no client
      is connected (stefanrows, without its gear seeding); the `clock` schedule on by default; crowd targets
- [ ] The chat benchmark's grounded-state and multi-turn tiers; the second blind round

**Phase D — a server with a past.**
- [ ] The shared world-history record; the Herald as dated stories, "since you last played", the server first for a
      big named kill
- [ ] Live events M1 (the engine on a scripted dragon fight) with the timeline report; `LastSuccessful` and raid
      cooldowns persisted
- [ ] The economy ledger (instrumentation only). Moved 2026-11-15 (owner): 2.0's first milestone
- [ ] Five Easter eggs from a sealed file (fishing, off by default, is a candidate). Dropped 2026-11-15 (owner): good
      ideas go into [the idea backlog](#the-idea-backlog-owner-2026-11-15)
- [ ] Questing: the quest harness over the classic quests; the epic chains linked by Setup

**Before it ships**
- [ ] An overnight run at 600 bots per realm within the tick budget; the second blind chat round better than the first
- [ ] Played: one session per realm; invited into a group and a raid; a battleground session; a relic campaign watched;
      a dragon raid; a returning session read on the Herald
- [ ] Release notes to the rules above

### 1.2.x — play with friends (experimental, right after 1.2; superseded 2026-11-13: folded into 1.2)

Unchanged by the split of 2026-11-06. Owner 2026-11-03: private co-op for about 1–4 players in a thriving bot world; no public server.
- [ ] The must-fixes from the co-op audit: one region address per client, idle time-outs for remote players, account
      names other than `offline`, the one-account-per-IP rule, bots following the group's current leader, `/pull` and
      the pilot taking only your own bots, a server-wide companion cap
- [ ] Host for Friends and Join Friend in the launcher, ported with credit from rambodolphin13's
      `feature/play-with-friends` (home network, Tailscale by default, an internet mode limited to listed friends,
      firewall rules for this game only); automatic account creation off while hosting
- [ ] The bot population scaled to the extra players' load; the original client first, the new client after
- [ ] Played: a weekend session with two or three friends

### 1.3 — a living realm (owner 2026-11-06; superseded 2026-11-07: folded into 2.0, see there)

Headline: the realm moves and has a past. Design: [LIVE-EVENTS-1.2.md](LIVE-EVENTS-1.2.md),
[LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md), the guild tiers entry in the [backlog](LIVING-SERVER-BACKLOG.md), and the
superseded 1.2 phases C and D above.

- [ ] Live events: the engine on a scripted dragon fight first (M1), with the timeline report
- [ ] The guild social package with guild tiers (policies, standing, tier benefits and costs)
- [ ] A crowded prime time: the era's channels with reach, a "2003 server" population preset, crowd targets, the
      chat benchmark's second blind round
- [ ] A server with a past: the shared world-history record, the Herald as dated stories, "since you last played"
- [ ] Era builds from the Wayback research (#76's fetcher; #55's build weights and class shares, provisional until
      checked against the period pages)
- [ ] Played: one session per realm; a live event watched; a guild tier earned; a returning session on the Herald

### 1.3 as planned on 2026-11-03 — the world goes on without you (superseded 2026-11-06 by the split)

Kept for the record. Not placed by the split, for the owner to schedule after 1.3: the catch-up, live events M2-M3,
crafting M0-M2, role playbooks and warband lanes, personas, co-op made first-class.

- [ ] A bounded catch-up when the server starts after days off (keeps, raid outcomes, bot levels, guild changes),
      told by bots and the Herald; losses allowed, never gear; an opt-out (owner decision)
- [ ] Durable event state and restart recovery; live events M2 (the director, the roaming threat) and M3 (the invasion)
- [ ] Crafting M0–M2 after weeks of ledger data: crafters at stations, commissions, ads
- [ ] The guild package (tiers, rival guilds), reputation tags, a realm summary for the director
- [ ] Role playbooks for the RvR builds; warband lanes and roaming groups; stefanrows's group doctrine, untangled
- [ ] Personas and longer storylines if the 1.2 chat measurements justify them; the historical server
- [ ] Co-op made first-class: the systems that assume one human (memory and circles, "since you last played",
      events that wait for the player, bots inviting the player, quest credit in bot groups) work for each of up to
      four players

### 2.0 — the complete realm (owner 2026-11-07: 1.3 and bots as people fold in)

Headline: the realm moves, has a past, and its bots read as the people behind them. One release, built in parts that
are each soaked and played on their own first (the rules above).

**First (owner, 2026-11-15): the economy ledger.** Instrumentation only (as planned on 2026-11-01): the money supply
per realm and where coin comes from and goes, logged and soaked before anything uses it
([CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md)). 2.0's Exchange tax and crafting rules depend on it ("no Exchange
tax or new sink until the ledger shows a need", below). It was dropped after the 1.2 plan of 2026-11-01 and is restored
here.

**Easter eggs that change behaviour (owner, 2026-11-15: allowed).** Planned for 2.0 unless noted:
- [ ] Visible loot rolls: the raiders' real `/random 100` rolls decide the bots' share. The roll is the real decision,
      and the player's guaranteed quarter stays outside it (real loot, AGENTS.md).
- [ ] The debt repaid (named in the review of 2026-11-15; no design yet).
- [ ] The nemesis hunts you (named in the review; no design yet).
- These are the first eggs where a bot's memory changes what it does, not only what it says (until now memory is chat
  only).

**A living realm (what 1.3 held).** Design: [LIVE-EVENTS-1.2.md](LIVE-EVENTS-1.2.md),
[LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md), the guild tiers entry in the [backlog](LIVING-SERVER-BACKLOG.md).
- [ ] Live events: the engine on a scripted dragon fight first (M1), with the timeline report
- [ ] The guild social package with guild tiers (policies, standing, tier benefits and costs)
- [ ] A crowded prime time: the era's channels with reach, a "2003 server" population preset, crowd targets, the
      chat benchmark's second blind round
- [ ] A server with a past: the shared world-history record, the Herald as dated stories, "since you last played"
- [ ] Era builds from the Wayback research (#76's fetcher; #55's build weights and class shares, provisional until
      checked against the period pages)

**Bots as people (owner 2026-11-07: "I like options 1-6").** Each item is a 2003 player habit, never random noise:
"more human" must not mean "worse". Items 1 and 3 share one data model (a bot player who owns characters and has
friends) and go with the guild social package.
- [ ] **1. Bot players own characters (the alt roster, designed 2026-10-12, never built).** Up to 3 characters per bot
      player; alts are added, never rerolled; mains, guilds and gold persist; occasional retirement and new players.
      A bot player picks which character to log in each session and shares a personality, schedule and friends across
      them, so its "on my alt" chat becomes true. Unblocks crafter alts (CRAFTING-AND-ECONOMY.md, row Q). Check
      low-level combat first (LOG-ERROR-BACKLOG.md: new characters can barely fight).
- [ ] **2. Skill that varies and improves.** A lasting skill profile per bot player (reactions, positioning, pulls,
      standing in fire, buffs, power use) that gets better with play. Raids learn a boss's mechanic after enough of
      their people have seen it (the "secrets had to be learned" idea, owner 2026-11-07). Mistakes fall mostly on
      strangers, not the player's own group, with a switch.
- [ ] **3. Friends, regulars and grudges.** A relationship graph that drives who groups with whom: bots seek people
      they grouped with before, form static groups, hold a grudge after a gank, leave a guild after a bad night, argue
      over loot. Builds on "bots remember you", the rage-quit records and mod-pbc's relationship scoring.
- [ ] **4. Knowing only what a player would know, in PvE too.** A per-bot memory of places visited; a bot that has never
      been somewhere asks the way, follows someone and can get lost (extends 1.2's RvR perception).
- [ ] **5. Movement that doesn't look scripted.** Strafing, sitting to regen, /stick, overshooting, idling at the bank
      and crowding town spots, sitting on keep walls. Touches pathing: no navmesh changes without evidence (AGENTS.md).
- [ ] **6. Personal milestones that get talked about.** A goal arc per bot player (epic armour, realm rank 5, an
      artifact, the ding to 50) that chat and the Herald pick up; feeds "a server with a past".
- [ ] **Measured by a played "who's real?" session:** the owner and a friend play an evening and tag every bot that gave
      itself away, before and after. The blind chat read tests lines only.

**The complete realm.**
- [ ] Crafting M3–M6 (with no Exchange tax or new sink until the ledger shows a need); the war effort
- [ ] Questing certified complete; the nightly scenarios gate every release
- [ ] Overnight runs at 900 bots per realm; two weeks with the money supply flat
- [ ] Release notes and the Reddit post for 2.0
- [ ] Played: one session per realm; a live event watched; a guild tier earned; a returning session on the Herald; a bot
      logging its alt and talking about its main; a static group forming on its own; the "who's real?" evening

Cut: the per-bot JSON rule list. Kept out on purpose: Camlann full-PvP, roguelite modes, modern classes, free
level-50 gear, teleports that skip stablemaster routes, grey monsters that drop nothing.

## The idea backlog (owner, 2026-11-15)

Ideas worth keeping that no stop has taken yet (it replaces the five-egg sealed file). An idea leaves this list when a
stop takes it. Nothing here is built or tested. Source: the roadmap and Easter egg review of 2026-11-15.

- **Small, chat only (for 1.2.x, owner 2026-11-15):** your first friend remembers (tells at levels 20, 40, 50 and a realm
  rank); Camelot's birthday and this server's birthday; the cross-realm echo (your other realm's bots complain about your
  race and class, never your name); "pulled a {player}"; enemies gloat on your corpse; your old sword lives on (the
  Exchange buyer thanks you while wearing it); fell through Camelot again (`/stuck`); the first dragon kill dropped
  nothing (a veteran's tale).
- **Behaviour (for 2.0, above):** visible loot rolls, the debt repaid, the nemesis hunts you.
- **Not placed:** hidden feats with quiet titles; `/monty` answers; the show-off nuker; old bugs the veterans remember;
  the keep held forever; Grab Bag Friday (next to the "no fake patch day" rule); launcher eggs (they change the
  launcher's look); "New alt?"; a Hall of Firsts crier; bot celebrities; guild founding day; level-20 alts; weekend relic
  raids; tips from your world (new client only, which is maintenance-only); the realm's legend; pay it forward; you
  become a tale; the midnight fortune teller; the beggar who made it; the rival of your class; the third realm shows up;
  the truce against the runaway realm; locked in Darkness Falls; the duelist at the bridge; the riddle trail to Avalon;
  the poetry contest; the bomb from the hill; the stealther on the road.
- **Need 2.0's organic retirement first:** farewells (a quitting bot gives its things away) and the ghost in Darkness
  Falls; both meet "gamebots start without armor and earn it".
- **Cut:** Leeroy veterans and any other idea built on a post-2003 joke (owner, 2026-11-15: post-2003 jokes are cut).

## Decisions taken and open

Taken (2026-11-15), the owner's answers to the roadmap review (all 23 in [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md),
"Owner decisions 2026-11-15"). For this plan:
- The L600 baseline comes after 1.2 (1.2.x above); 1.2's overnight row has a stall threshold instead.
- The economy ledger is 2.0's first milestone; nothing of it is in 1.2.
- A player-called realm raid is 1.2.x.
- The new client (0.37c) is maintenance-only.
- The small chat-only eggs are 1.2.x, after the raid freeze and the gate.
- Eggs that change behaviour are allowed and planned for 2.0 unless noted.
- The five-egg sealed file is dropped; ideas go into the idea backlog above.
- Rage quits keep ignoring the schedule's `off`.
- The purity rules R0-R10 stay open: the owner asked for a recommendation per rule first.

Taken (2026-10-30):
1. The train over the bundle.
2. The build order inside live events: the engine on a dragon fight first, the invasion third
   ([LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md) §7).

Taken on the owner's behalf in the 1.1.1 branches, to be confirmed or reversed at the merge:
- `quest_xp_by_level` is one setting for both the level-range rate and the one-level-per-payment cap; off means
  exactly what 1.1 paid (PR #45). The owner may prefer the cap always on.
- Dragons count their timed-out raids as losses, as dungeons do, so a dragon whose last two raids timed out meets
  the Special Forces from the start on the day PR #44 is merged.

Taken (2026-11-01): the answer to "too many pets, bots see you from miles, no PBAE group" is the world's own
gamebots playing the era's builds properly, not summoned companions ([BOT-BUILDS-AND-RVR.md](BOT-BUILDS-AND-RVR.md)).
It also strengthens the case for decision 3: the spell-use engine it builds on (area spells that fire, add control,
tank guarding) is in shadowofze's final `main`.

Taken (2026-11-06), the owner's: **the split.** 1.1.2 now (the combined candidate plus the raid fixes #73, #75,
#77, and from 07:13 the epic encounter catalog and first raid tactics #81, #83, #85); 1.2 = "bots that play with you"; 1.3 = live events, the guild social package with guild tiers, a crowded prime
time, a server with a past and the era builds from the Wayback research (see "The split" at the top). It supersedes
the next entry.

Superseded 2026-11-06 by the split. Taken (2026-11-01): **1.2 is one comprehensive update**: the old 1.3 (live events M1–M2, the guild package, the
historical server, the quest harness) and both halves of the builds work fold into it, built in phases that are
soaked one at a time; the invasion and crafting stop becomes 1.3.

Taken (2026-11-03): **private co-op for about 1–4 players** in the bot world (1.2.x, first-class in 1.3); no public
server, and nothing formal about taking over from shadowofze.

Taken (2026-11-03): **follow the base on the Sluaghbinder** (it follows the edition switch again, `12992146`), and
**no EA client files in the repo** (the 54 inherited from upstream removed; history kept). The North Star order above
and the purity rules in [NORTH-STAR-AUDIT.md](NORTH-STAR-AUDIT.md) §4 are proposed for the owner's confirmation.
Still open on 2026-11-15: the owner asked for a recommendation per rule before confirming any.

Taken (2026-11-01), decision 3: **the base's final code is merged whole** as 1.2's phase A, starting at once. The
reasoning that led there, kept for the record: The case for the merge: the overlap only grows (41 shared server files today; crafting, events and
quests all touch raids, valuation and `DataQuest`), it is the largest bot upgrade available anywhere and nobody else
will bring it to 0.37 players, Cancain's synced fork will send pull requests against that code, and the train's
rule was always to merge the base first. The case against: it is unplayed code with no author behind it any more,
it delays the rest of 1.2 by a phase, and it costs a merge of 127 files plus a soak week. What would change
the recommendation: his `main` keeps moving (wait), the trial merge fails the suite badly (harvest), or the owner
wants live events sooner (harvest now, merge later).

Open, each listed with its options in the design document named:
- Questing ([QUESTING.md](QUESTING.md) §5): hand-ins above the maximum level and bot-group kills crediting the
  player are implemented in PR #46 as proposed; the tracker and journal on the new client.
- Live events ([LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md) §8): drama curve over choreography; the player's
  beat; how far the director may bend an outcome; occupation; event loot.
- Crafting ([CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md) §13): the two ladders; bot and player crafting
  pace; coin by level band; upkeep; sellback; the Exchange tax; commissions; the crafter share; the level-50
  button; the classic rules; bots' gear wearing.

## Status

On 2026-11-15:

| Stop | State |
|---|---|
| 1.1.4 | Released 2026-11-12 (the latest release); being merged into `integration/1.2` |
| 1.2 | All of it merged on `integration/1.2`; raid code frozen since #240; `1dc66b6` played overnight 2026-11-14/15, `3d67b6f` for the battleground test; held until [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md) is met |
| 1.2.x | Planned: a player-called realm raid, the small chat-only eggs, the L300/L600 baselines |
| New client (0.37c) | Maintenance-only (owner, 2026-11-15) |
| Linux | After 1.2 (PR #206) |
| 2.0 | Planned: the economy ledger first, then a living realm (was 1.3), bots as people, the complete realm |
| True 1.65 edition | Separate track, unchanged from 2026-11-14 |

On 2026-11-14:

| Stop | State |
|---|---|
| 1.1.4 | Released 2026-11-12 (the latest release) |
| 1.2 | All of it merged on `integration/1.2`; test build `1dc66b6` on the owner's test copy (installed 2026-11-14 21:16); held until [PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md) is met |
| Linux | After 1.2 (PR #206) |
| 2.0 | Planned: a living realm (was 1.3), bots as people, the complete realm |
| True 1.65 edition | Separate track: 1.1.4 carried over on `living-server/1.1.4`, waiting for its PR into the edition's `main`; 1.2 is ported only after 1.2 is fully released on 0.37 (owner, 2026-11-14) |

On 2026-11-07:

| Stop | State |
|---|---|
| 1.1.2 | Released 2026-11-07 (`v1.1.2-beta`, "Raise the Banners", the latest release; Setup built and tested by CI) |
| 1.2 | On `integration/1.2`: bots that play with you, the epic encounters at full strength (#102, #106, #110), the lore bible (#107, #108), the titled loading screen (#115); automated tests, container raids and one PC raid |
| 1.2.x | Planned (play with friends) |
| 2.0 | Planned: a living realm (was 1.3), bots as people, the complete realm |

On 2026-11-06 (superseded):

| Stop | State |
|---|---|
| 1.1.1 | Released 2026-11-03 (`v1.1.1-beta`, the latest release; Setup built and tested on Windows by CI) |
| 1.1.2 | Release candidate `test/1.1.2-rc` (PR #86: #66 plus #73, #75, #77, #85 with #81 and #83, #87, #88, #89), 4,819 server tests passing; waits for the owner's merges, an overnight soak, his played check and publishing. #51 not in it (owner and legal decision) |
| 1.2 | Bots that play with you: built on branches (#67, #69-#72, #74, #78-#80, #82, #84), automated tests only; merges into the 1.2 line after 1.1.2 |
| 1.2.x | Planned (play with friends) |
| 1.3 | Planned (live events, guild tiers, a crowded prime time, a server with a past, era builds) |
| 2.0 | Planned |

On 2026-11-03 (superseded):

| Stop | State |
|---|---|
| 1.1.1 | Released 2026-11-03 (`v1.1.1-beta`, the latest release; Setup built and tested on Windows by CI) |
| 1.1.2 | Planned (2026-11-03): PR #50 plus save safety, the r/daoc fixes, ported fork fixes, release gates; PR #59 (EA files) open |
| 1.2 | Phase A: base merged and current (`ebbaf83`); profiling, harness and the duty record next |
| 1.3 | Planned |
| 2.0 | Planned |
