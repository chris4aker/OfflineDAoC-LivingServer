# Live events: the plan for Living Server 1.2

Owner: Chris. Chosen 2026-10-28: live events are the headline of 1.2, explored fully. Nothing here is built yet.

The research behind this plan is in [research/LIVE-EVENTS-RESEARCH.md](research/LIVE-EVENTS-RESEARCH.md). It covers
classic MMOs, modern MMOs, single-player and sandbox games, design talks and postmortems, DAoC and bot servers.
Where this plan says "the research shows", that report has the sources. Choices marked **(proposed)** are this
plan's, not research findings, and are open until the owner settles them.

## The goal

Things happen in the world that you didn't start, that you hear about before they arrive, that the bots react to like
players, and that leave a mark. One human plays a few hours a week among hundreds to thousands of bots, so the
events are paced around your sessions, the bots supply the crowd, and you are never just a spectator.

## What the research says matters most

1. **Visible world change and a shared goal are what players remember:** Ahn'Qiraj's war effort, Asheron's Call's
   Shard of the Herald, EverQuest's Sleeper, DAoC's own Darkness Falls (open to the realm with the most keeps).
2. **The same few things ruin events:**
   - breaking the services players need (WoW's 2008 zombie plague overran vendors and flight masters);
   - lag at the climax (Ahn'Qiraj, Rift, Throne and Liberty);
   - taking an outcome away (EverQuest despawned the Sleeper mid-kill);
   - a battle that plays itself (BfA Warfronts);
   - timer chores (Warhammer public quests, Legion assaults);
   - grind that crowds out RvR (DAoC's Trials of Atlantis).
3. **Warnings build anticipation:** hours of build-up across several channels (7 Days to Die's red day counter, sky and
   scream; ESO's red mist; WoW's "gathering, will begin soon").
4. **Quiet stretches make the peaks land:** Left 4 Dead's AI Director; Valheim's plain random roll was called its
   "most annoying feature".
5. **Every long-lived sandbox shipped controls:** an off switch, frequency, warning time, and a "call it now" option.
6. **Closest prior art for a bot server:** AzerothCore's mod-city-siege. It recruits only idle bots up to a cap,
   brings them in, and returns them afterwards. It is also a warning: its hand-placed routes leave units stuck.

## The four event types, in order

| # | Event | What happens | Reuses |
|---|---|---|---|
| 1 | **Realm invasion** | An army of the realm's own enemy marches on a town or border keep. Scouts and rumours first, then a realm-wide warning naming the place, then waves and a champion. Win: rewards and a chronicle entry. Lose: the place is occupied for a while and can be taken back. | Siege rally and recruiting, realm notices and announcements, true rumours, Realm Events records |
| 2 | **Roaming threat / world boss** | A named warband, a demon breach or a dragon flight roams a zone for about an hour. Bots form up to hunt it; you can join or stay clear. | Dragon raids, raid formation, Leeroy as a rare variant |
| 3 | **War-effort chapter** | Rare, every few weeks of your play time. Bots and you hand in supplies, kills and keep captures toward a visible realm total, ending in a set-piece that unlocks something lasting. | Keep and relic sieges, Darkness Falls and Summoner's Hall expeditions, records |
| 4 | **Holiday festival** | A visitor in each capital on the PC calendar's holidays, a light seasonal boss, cosmetic rewards. Returns every year. | Holiday chatter (`AutonomousHolidays`), lore talk |

**Invaders per realm (proposed, from Shrouded Isles lore):** Drakoran (allied with Morgan Le Fay) in Albion, Morvalt in
Midgard, Fomorians in Hibernia. Shared threats for all three: doppelgangers, and demons breaking out of the maze
ruins (both from Broadsword's own events). The research didn't verify Morgana's undead, jotuns or the Siabra as
invaders; check them before using them. Lower-level towns need lower-level invaders of the same theme, so each realm
needs a level ladder (see M0).

## Design rules

1. **Warn in stages.**
   - Bot scouts and a true rumour first.
   - Then a realm-wide notice naming the place.
   - Then a sign at the target: fires, weather where possible.
   - Then a horn.
   - `/events` (proposed) shows what's coming and what's on.
2. **Time events by your sessions, not the wall clock.**
   - Invasions and roaming threats start only while you're logged in, after you've played a while, with quiet gaps
     between them. A director picks the moment (proposed: like Left 4 Dead's, with build-up, peak and rest).
   - Nothing resolves to a loss while you're offline; a war-effort chapter only creeps forward.
   - Every type has a "call it now" GM command, so you can play the one you want.
3. **Better-than-even odds, soft failure.**
   - Aim for about 60–75% wins when you take part (proposed band).
   - A loss means a retreat, an occupied town that can be taken back, or a phase reset, never a wipe of the place.
   - Losing must never leave things worse than not defending. New World did this, and players hated it.
4. **Services are never hurt.**
   - Merchants, trainers, stablemasters, the Realm Exchange and bind points stay safe, or visibly move to a fallback.
     This keeps the stablemaster routes and selling at natural breaks working.
   - Anything that spreads or occupies has a hard edge in space and time, leaves pets and companions alone, and has
     an off switch.
5. **You stay decisive.**
   - You get a named objective the bots defer to: the banner, the anchor stone, the champion's last blow.
   - Bots visibly organise: form and merge groups, call targets, shout. That is the crowd.
   - A bot win without you is fine, but a smaller one.
6. **Rewards are real but capped.**
   - Event monsters drop normal loot.
   - Event extras are titles, trophies and cosmetics, plus capped tokens that can't be traded. Never best-in-slot gear.
   - Bots earn under the same caps, so the economy and the Realm Exchange don't inflate.
7. **Equal realms.** Every event has three matching versions: same terrain type, difficulty and reward. Win rates are
   logged per realm. A realm that keeps losing gets a counter-event, not a nerf to the winner.
8. **Outcomes are saved and reversible.**
   - Small events reset.
   - Middle ones leave marks you can undo: ruins, a captured or restored garrison, a merchant who arrives after a save.
   - War-effort finales change something for good and get a chronicle entry.
   - Event state lives in the database and survives restarts. It's cleared for release worlds the way every
     saved-progress table is, and there is a one-command reset to the default world.

## Settings

All on by default, on the launcher's Living Server tab (our tab; shadowofze's launcher behaviour is unchanged). Stored
as server properties like the other `bot_living_*` switches (proposed).

| Setting | Default | Notes |
|---|---|---|
| Live events | On | Master switch |
| Per type: invasions, roaming threats, war effort, festivals | All on | |
| Frequency | Normal (Off / Low / Normal / High) | Changeable any time, no penalty |
| Warning time | A few minutes for small events, longer for big ones | |
| Most bots sent to one event | Set by profiling | The lag guard |
| Difficulty | Better than even | Steers the win-rate band |

## Risks and how this plan handles them

| Risk | Handling |
|---|---|
| **Lag at the climax**, the biggest risk; a crash takes the whole world down | Recruit only idle bots that aren't grouped, up to a cap. Stagger arrivals. Give the rest of the realm other work. Profile at your real bot count (600–1,000 per realm) before release. |
| **Stuck armies** | March routes along roads bots already travel, checked against the existing navmeshes. A fallback spawn out of sight. No global navmesh rebuilds. |
| **Event state bugs** (Ahn'Qiraj Classic lost progress on restart; Wrath Classic's timers drifted) | State in the database, counters that can't double-count, separate start and stop. |
| **You as a spectator** | Rule 5. |
| **Fatigue** | The director's quiet gaps, rarity and variety. Frequency setting. |
| **Grind crowding out RvR** | Event rewards never needed for RvR power. |
| **Wrong lore** | Check invader names and monster templates against the game data before use. |
| **Honesty** | Nothing set in Darkness Falls or at level 70+ is described as working until it's checked in game. |

## Milestones

| | Milestone | Done when |
|---|---|---|
| M0 | **Groundwork** | For each realm: the invader family and a level ladder of existing monster templates; 2–3 candidate towns or keeps; march routes checked on the navmesh; a measured cost per extra monster and bot at your real bot count. A short findings note. |
| M1 | **One invasion, all three realms** | Staged warnings, waves and a champion, bots rallying under a cap, protected services, win and occupied outcomes, the chronicle entry, `/events`, the GM "call it now", settings. Automated tests for the rules. Then one play session per realm on the test copy, with the win rate logged. |
| M2 | **Director and roaming threats** | Session-aware pacing for M1 and the roaming threat type, with quiet gaps. Played over at least two normal sessions. |
| M3 | **Holiday festivals** | One visitor per capital per holiday, mirrored. Start with whichever holiday is next on the calendar. |
| M4 | **War-effort chapter** | The realm total, contributions from bots and you, the set-piece and the lasting unlock. Last, because it's the riskiest. |

Each milestone ships only after its in-game check, reported separately from automated tests.

## Open questions for the owner

1. Which town or keep in each realm should see the first invasion? A mid-level frontier town or border keep is the
   proposal, not a starter town or a capital.
2. Should an occupied town block its own services (bind, porter) while occupied, with fallbacks nearby, or stay fully
   usable with only the look and the monsters changed?
3. Should bots of all three realms ever join one shared fight (maze demons), or always realm-only?
4. Easter eggs: real hidden things to find in the world, which bots hint at the way they already talk about Avalon.
   Add them to 1.2 alongside the events?
