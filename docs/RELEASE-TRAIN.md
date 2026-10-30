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
  Living Server never undoes a base change.
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
- 0.38 lands in the middle, and quest fixes are world data like 0.38's own changes.
- 1.1's eight branches merged without conflicts because they were small and separate; crafting, events, quests
  and guilds all touch valuation, town routing, chat budgets, raids and voices.

## The stops

### 1.1.1 — the community's first week (days)

Fixes and verifications only; no new systems. Design: [QUESTING.md](QUESTING.md) §1–2 for the quest defects.

- [ ] Community issues: #32 launcher stays STARTING when the listener isn't reported; #33 party doesn't disband
      (PR #35); #38 friends list; #39 bots released in a battleground stand in the capital; #34 new-client
      hotbars goes upstream to shadowofze; #40 Cancain's pull requests (`/friend` for bots, battleground keep
      claims, `/allrelease`) reviewed and taken where they fit the owner's rules
- [ ] PR #36 base classes get their weapon styles before level 5; PR #37 quiet song twisting
- [ ] Sieges played at the owner's roster now that their timers run (musters, marches, repels, the four-hour cap)
- [ ] The talking update's "not checked in game yet" list (TALKING-UPDATE.md) worked through
- [ ] Dragon raids call the Epic Special Forces after a wipe, as dungeon raids do
- [ ] `Repair.IsAllowedToBeginWork` integer division
- [ ] Quest defects that are code only: kills by the player's bot group credit the player; a kill advances a step
      once; hand-ins allowed above a quest's maximum level; the 25-quest limit at login; whisper throttle and
      keyword clicks; the 255-character journal text where the client allows more; the new client's marker
      colour for "your current step"; quest XP follows XP by level range (decision 2 below)
- [ ] The three-quest A/B on the test copy: 1.1 against stock 0.37b via `ROLLBACK.cmd` (a kill quest in a bot
      group, a delivery, an epic step), as a normal account
- [ ] Doc drift: KNOWN-ISSUES to 1.1; the changelog's Beta 1.1 header; RELEASE-1.1's boxes; SHA-256s in the
      release notes
- [ ] Release notes written to the rules above

### 1.2 — the world moves (after shadowofze's 0.38)

Headline: live events. Design: [LIVE-EVENTS-1.2.md](LIVE-EVENTS-1.2.md) (the plan),
[LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md) (how it is built and seen), the guild tiers entry in the
[backlog](LIVING-SERVER-BACKLOG.md).

- [ ] 0.38 merged and claimed (`BaseRelease`), content patches re-checked
- [ ] Live events M0: groundwork, the cost per monster and per bot at the owner's roster, the timeline report
- [ ] Live events M1: the engine on a scripted dragon fight (Script, Stage, Chorus, Chronicle, `/lstest event`)
- [ ] Live events M2: the director and the roaming threat
- [ ] The guild social package as one unit (policies, standing, tier benefits and costs)
- [ ] The historical server (opt-in roster import)
- [ ] Questing: the server-side quest harness and the quest audit script; the first world fixes; the epic chains
      linked by Setup (and unlinked by rollback)
- [ ] Played: one session per realm for the fight and the roaming threat; the guild tiers; two epic lines

### 1.3 — the realm has work to do

Headline: the invasion. Design as above plus [CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md).

- [ ] Live events M3: the realm invasion; M4: holiday festivals, then the war-effort chapter
- [ ] Crafting M0: the audit on the PC (recipes, masters, stations, housing), the two bugs, calibration
- [ ] Crafting M1: the player's side (era start, pace, upkeep, coin by band, firsts, the Herald)
- [ ] Crafting M2: crafters exist (ledger crafters, stations, the visible loop, titles, /trade ads)
- [ ] Questing: dialogue and links for the lines that only have pins, realm-equal; the level-50 epics check
      their chains
- [ ] The new client's quest tracker and book-page journal, sent upstream
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

Open, each listed with its options in the design document named:
- Questing ([QUESTING.md](QUESTING.md) §5): quest XP follows XP by level; hand-ins above the maximum level;
  bot-group kills credit the player; the tracker and journal on the new client.
- Live events ([LIVE-EVENTS-ENGINE.md](LIVE-EVENTS-ENGINE.md) §8): drama curve over choreography; the player's
  beat; how far the director may bend an outcome; occupation; event loot.
- Crafting ([CRAFTING-AND-ECONOMY.md](CRAFTING-AND-ECONOMY.md) §13): the two ladders; bot and player crafting
  pace; coin by level band; upkeep; sellback; the Exchange tax; commissions; the crafter share; the level-50
  button; the classic rules; bots' gear wearing.

## Status

| Stop | State |
|---|---|
| 1.1.1 | In progress (2026-10-30): fix branches opening; the played checks wait for the owner |
| 1.2 | Planned; waits for 0.38 |
| 1.3 | Planned |
| 2.0 | Planned |
