# The release train to Living Server 2.0

Owner: Chris. Chosen 2026-10-30: **2.0 is the destination, not the next release.** The work to get there (crafting
and the economy, live events, questing complete, the guild package, the historical server, and the fixes the
community is reporting) ships as a train of public releases, each played before it goes out. 2.0 is the stop where
the realm is complete.

This file is the plan of record. The design behind each stop lives in its own document, and each stop's checklist
is ticked here as it is done. The older plans ([RELEASE-PLAN.md](RELEASE-PLAN.md), the Living Server
[backlog](LIVING-SERVER-BACKLOG.md)) stay as the dated record.

## Rules for every stop

- A stop ships only after its in-game check on the owner's PC, reported separately from automated tests
  (AGENTS.md). Automated tests and the rehearsal transcripts are reported as what they are.
- One major system at a time in the overnight soak runs, so a lag or a regression can be attributed.
- Release notes show the work and the research and never spoil the Easter eggs or the internal vocabulary.
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

## The base: shadowofze has stopped (checked 2026-10-31)

What is verified, from his repository:
- His README (commit `f630845`, 2026-10-31) says the project **is on hold and will not receive any more updates**;
  the 0.37 / 0.37b / 0.37c downloads stay; he has moved to a separate "True 1.65" version that uses only the new
  client, with no release promised. He points people at the repository's forks and r/daoc.
- There is **no `v0.38` tag** and the changelog still says "0.38 (not released yet)". 0.38 was going to be one
  download with the new client only, the original client no longer supported and the Sluaghbinder discontinued
  (the class code is still there; it is no longer offered).
- His `main` kept moving after the note (five commits in the next three hours, to `b0b855d`), so the tree may not
  be final yet.
- Beta 1.1 already carries his server code as of `9da9091` (2026-10-20). What came after is 40 commits: 127 server
  files (+6,293 / −539 lines), 41 of them files Living Server also changed, and 63 new-client files (+4,102 lines).
  The server part is the largest bot upgrade in the project's history: area spells and add control for every
  class, tank Protect/Guard/Intercept, Reaver auras, camp spread, bots holding one song, relic campaigns that can
  take a relic home, raid loot shared with the player, three Exchange listings per bot, the tasks rework, seal
  merchants, and dozens of fixes (ten or more credited to Living Server). It has no CI and nobody but him has played
  it.
- Living Server's installer needs a 0.37 / 0.37b / 0.37c game and will keep needing one; players are not forced
  anywhere. The downloads being his, a private copy of the three 0.37 downloaders and packages is cheap insurance.

What it changes here: 1.2 can't wait for a release that isn't coming, and "every base release is merged first" has
one last base state to apply. The owner's call (decision 3 below) is whether that last merge is its own stop now or
is harvested piecemeal.

## The stops

### 1.1.1 — the community's first week (days)

Fixes and verifications only; no new systems. Design: [QUESTING.md](QUESTING.md) §1–2 for the quest defects.
State on 2026-10-31: every code item has a branch and a green CI run; the three large ones were reviewed by a
second reader and their fixes applied; nothing is merged and nothing is played yet.

- [ ] Community issues: #32 launcher stays STARTING when the listener isn't reported (PR #43); #33 party doesn't
      disband (PR #35); #38 friends list and #39 bots released in a battleground stand in the capital (Cancain
      has both in his fork: invite the pull requests); #34 new-client hotbars, stealth and the SELL button (fixed
      in shadowofze's unreleased 0.38c client source; there is no upstream to send it to now, so it moves to 1.2
      where Living Server ships client scripts itself); #40 Cancain's pull requests (`/friend` for bots,
      battleground keep claims, `/allrelease`) reviewed and taken where they fit the owner's rules
- [ ] PR #36 base classes get their weapon styles before level 5; PR #37 quiet song twisting (his `main` later
      makes performers hold one song instead of twisting; #37 stays useful until that merge and is retired then)
- [ ] Sieges played at the owner's roster now that their timers run (musters, marches, repels, the four-hour cap)
- [ ] The talking update's "not checked in game yet" list (TALKING-UPDATE.md) worked through
- [ ] Dragon raids call the Epic Special Forces after a wipe, as dungeon raids do (PR #44; the review found and
      fixed the wipe count missing raiders who had released in the lair's region)
- [ ] `Repair.IsAllowedToBeginWork` integer division (PR #44)
- [ ] Quest defects that are code only (PR #46: kills by the player's bot group credit the player; a kill
      advances a step once; hand-ins allowed above a quest's maximum level; the 25-quest limit at login; whisper
      throttle and keyword clicks. PR #42: the new client's marker colour for "your current step", with its
      installer entry. PR #45: quest XP follows XP by level range, decision 2 below). Left for 1.2: the
      255-character journal text where the client allows more
- [ ] The three-quest A/B on the test copy: 1.1 against stock 0.37b via `ROLLBACK.cmd` (a kill quest in a bot
      group, a delivery, an epic step), as a normal account
- [ ] Doc drift: KNOWN-ISSUES to 1.1 (PR #41); the changelog's Beta 1.1 header (PR #41); RELEASE-1.1's boxes
      (PR #41); SHA-256s in the release notes (the owner, on the release page); the "wait for shadowofze's next
      update" lines now that there won't be one (PR #41)
- [ ] Release notes written to the rules above

### 1.2 — the last base merge (proposed 2026-10-31; decision 3 is the owner's)

Headline: the bots of 0.38, in a game that stays 0.37. Checklist: [UPSTREAM-RELEASES.md](UPSTREAM-RELEASES.md),
read with "his final `main`" in place of "the `v0.38` tag".

- [ ] Wait until his `main` has been still for 14 days, then merge it on a `sync/upstream-final` branch: keep both
      clients and the Sluaghbinder switch (Living Server's policy, not his), take his world scripts
      (`fix_quest_reward_stats.py`, Necromancer creation, the night spawns) as Setup steps with rollbacks, and
      leave out `remove_sluaghbinder_npcs.py`
- [ ] Living Server's own new-client fixes shipped at last: the 0.37c client runs its GDScript from source, so the
      four scripts (`/lfg` and `/trade` as realm channels, FACE/FOLLOW/STICK, the stick toggle, the PLAY
      double-click guard) go into `content-patches.json` like the quest-marker script in 1.1.1; `/target` filling
      the target window needs the rebuilt C# client (`MMOEngine.dll`) and is decided with it
- [ ] Which of his 63 new-client files can be shipped the same way (stealth, abilities on the quickbar, the
      merchant SELL button: issue #34; the crafting window), and which need converted content and can't
- [ ] The full suite green, a week of overnight soaks with nothing else moving, the siege and raid ledgers compared
      with 1.1.1's
- [ ] Live events M0 alongside (measurement only: the cost per monster and per bot at the owner's roster, the
      timeline report)
- [ ] Played: one session per realm; a relic campaign watched end to end; a dragon raid with the new add control

If decision 3 goes the other way (live events first), this stop becomes the harvest: his world scripts and the
small server fixes cherry-picked, the bot-combat changes left, and the stops below keep their numbers.

### 1.3 — the world moves

Headline: live events. Design: [LIVE-EVENTS-1.2.md](LIVE-EVENTS-1.2.md) (the plan, written when this was 1.2),
[LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md) (how it is built and seen), the guild tiers entry in the
[backlog](LIVING-SERVER-BACKLOG.md).

- [ ] Live events M1: the engine on a scripted dragon fight (Script, Stage, Chorus, Chronicle, `/lstest event`)
- [ ] Live events M2: the director and the roaming threat
- [ ] The guild social package as one unit (policies, standing, tier benefits and costs)
- [ ] The historical server (opt-in roster import)
- [ ] Questing: the server-side quest harness and the quest audit script; the first world fixes; the epic chains
      linked by Setup (and unlinked by rollback)
- [ ] Played: one session per realm for the fight and the roaming threat; the guild tiers; two epic lines

### 1.4 — the realm has work to do

Headline: the invasion. Design as above plus [CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md).

- [ ] Live events M3: the realm invasion; M4: holiday festivals, then the war-effort chapter
- [ ] Crafting M0: the audit on the PC (recipes, masters, stations, housing), the two bugs, calibration (his
      `main` already fixes the salvage type bug and gives the new client a crafting window; count them)
- [ ] Crafting M1: the player's side (era start, pace, upkeep, coin by band, firsts, the Herald)
- [ ] Crafting M2: crafters exist (ledger crafters, stations, the visible loop, titles, /trade ads)
- [ ] Questing: dialogue and links for the lines that only have pins, realm-equal; the level-50 epics check
      their chains
- [ ] The new client's quest tracker and book-page journal (there is no upstream to send them to now; they ship
      the same way as the other client scripts)
- [ ] Played: the invasion in each realm with the win rate logged; a crafter at each capital's station

### 2.0 — the complete realm

- [ ] Crafting M3: crafting matters (stock into the Exchange, the gear check at town visits, template-aware
      valuation at 50, the quartermaster's commissions, dyes in guild colours)
- [ ] Crafting M4: the player trades (commissions by tell, hand-over delivery, drag-to-sell, bots buying the
      player's crafts)
- [ ] Crafting M5: sinks and services (bots' gear wears, repairs, potions, siege supply, the luxury sink, the
      Exchange tax)
- [ ] Crafting M6: the social layer (storylines, price wars, apprentice work, guild crafters)
- [ ] Questing certified complete: the harness green over every row, the epic lines the owner plays finished end
      to end
- [ ] Two weeks of overnight runs with money supply flat and no price running away
- [ ] Release notes and the Reddit post for 2.0

## Decisions taken and open

Taken (2026-10-30):
1. The train over the bundle.
2. The build order inside live events: the engine on a dragon fight first, the invasion third
   ([LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md) §7).

Taken on the owner's behalf in the 1.1.1 branches, to be confirmed or reversed at the merge:
- `quest_xp_by_level` is one setting for both the level-range rate and the one-level-per-payment cap; off means
  exactly what 1.1 paid (PR #45). The owner may prefer the cap always on.
- Dragons count their timed-out raids as losses, as dungeons do, so a dragon whose last two raids timed out meets
  the Special Forces from the start on the day PR #44 is merged.

Open, decision 3 (2026-10-31), the owner's: **the last base merge as its own stop (1.2 as written above) or a
harvest.** The case for the merge: the overlap only grows (41 shared server files today; crafting, events and
quests all touch raids, valuation and `DataQuest`), it is the largest bot upgrade available anywhere and nobody else
will bring it to 0.37 players, Cancain's synced fork will send pull requests against that code, and the train's
rule was always to merge the base first. The case against: it is unplayed code with no author behind it any more,
it delays the live-events headline one stop, and it costs a merge of 127 files plus a soak week. What would change
the recommendation: his `main` keeps moving (wait), the trial merge fails the suite badly (harvest), or the owner
wants the live-events moment first (harvest now, merge as 1.3).

Open, each listed with its options in the design document named:
- Questing ([QUESTING.md](QUESTING.md) §5): hand-ins above the maximum level and bot-group kills crediting the
  player are implemented in PR #46 as proposed; the tracker and journal on the new client.
- Live events ([LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md) §8): drama curve over choreography; the player's
  beat; how far the director may bend an outcome; occupation; event loot.
- Crafting ([CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md) §13): the two ladders; bot and player crafting
  pace; coin by level band; upkeep; sellback; the Exchange tax; commissions; the crafter share; the level-50
  button; the classic rules; bots' gear wearing.

## Status

| Stop | State |
|---|---|
| 1.1.1 | In progress (2026-10-31): nine branches open and green (#35–#37 the owner's, #41–#46 drafted here); #44–#46 reviewed and their fixes applied; the merge, the played checks and the release notes wait for the owner |
| 1.2 | Proposed as the last base merge; decision 3 open; waits for his `main` to be still |
| 1.3 | Planned (live events) |
| 1.4 | Planned (invasion, crafting M0–M2) |
| 2.0 | Planned |
