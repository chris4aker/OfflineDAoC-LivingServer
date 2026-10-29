# Living Server backlog (planning)

Status (2026-10-16): **built** on `feature/living-server`, plus bot memory of you. What was built,
and what wasn't, is in [LIVING-SERVER.md](LIVING-SERVER.md). This file is the planning record.

Changes from the plan:
- The Herald is a static page written by the launcher, not the `atlas_api` web API (Chris's choice).
- Schedules have both modes, `session` and `clock`, with a switch (Chris's choice).
- There is no fake patch day (Chris's choice).
- Nemesis lines name enemies by race and class, since enemy names are hidden in the frontier.

Owner: Chris. Chosen 2026-10-16. Goal: make the bots feel like players on a 2003 server, through
continuity and consequence more than through more chat lines.

## The shortlist

Chris picked six ideas. Each was checked against the code on `release/0.36-chris.1` (commit
`8993171`) on 2026-10-16, so this list extends what exists instead of duplicating it.

| # | Idea | What already exists (checked in code) | What's new | Depends on |
|---|---|---|---|---|
| 1 | **Grats chains and server firsts** | Guild grats: with bot guilds on, guildmates congratulate dings in /gu (`AutonomousBotGuildChatLines`, `AutonomousBotGuildSocial` listens for `LevelUp`). Personalities add an occasional ding line. | Grats from non-guild bots who saw it (group, nearby, /faction for 50s); realm-wide "server first" lines (first of each class to 50, first to a realm rank, first kill of a big named) through `GameWideAnnouncements`. | Nothing |
| 2 | **Death economy** | `group_chatter.lines` has "rez pls" and "ty for the rez". | A bot that dies near you asks for a rez if your class can rez, and thanks you after; bots that rez you; corpse-run and release-to-bind chatter; grumbling about resurrection sickness. A rez is a natural event for bot memory of you. | Best after **bot memory of you** |
| 3 | **Frontier alarms** | Keep-attack rallies, raid and siege notices in /faction (`RealmEventNotices`, `AutonomousRvrEventLayer`), in a formal "herald" voice. | Player-voiced sightings from bots that actually see an enemy group: rough size, class mix, place ("~20 hibs crossing at Crauchon, inc"), with a strict rate budget per realm. Matches roadmap M4 ("signs of roaming groups"). | Nothing for a first version |
| 4 | **Camelot Herald** (local page) | An Atlas-era web API is in the server but **off by default** (`atlas_api`, port 5000): `/stats/rp`, `/guild/topRP`, `/guild/{name}`, `/player/{name}`, `/realm/{realm}` keeps, `/relic`, `/news`. | A local Herald page over that API (realm points, top guilds, keeps and relics, server news), opened from the launcher. Later, an "Export Herald" snapshot to share. | A short test first (below) |
| 5 | **Schedules and linkdeads** | None. The roadmap's M1 plans play sessions (log in 2–5 hours, out 1–4). | Logins and logouts that look human; linkdeads (a bot freezes, says "ld", drops and comes back); "brb dinner" style breaks. | Roadmap M1 play sessions |
| 6 | **Nemesis** | `memory.lines` (bot memory of you, written, not yet used). Roadmap M5 plans danger memory from stefanrows. | Enemy bots remember who killed them, say so in /region, and some seek revenge. | **Bot memory of you**, then M5 |

## Suggested order

1. Grats chains and server firsts: smallest, no dependencies.
2. Death economy, alongside or right after bot memory of you (already the next coding project).
3. Frontier alarms: extends `RealmEventNotices`.
4. Camelot Herald: after its test (below) shows the API works.
5. Schedules and linkdeads: with the M1 play-session work.
6. Nemesis: after bot memory of you and M5 danger memory.

This replaced the first order from the chat (schedules first, Herald last), because the code check
showed the Herald is mostly built and schedules depend on unbuilt M1 work.

## Open questions

- **Schedules versus a server that only runs while Chris plays.** Real-clock prime time would make
  a Tuesday-morning session feel empty. Options: shape logins around his session (a ramp, then
  people coming and going), or use time of day only lightly. Chris's call.
- **Linkdeads in groups.** A bot that drops mid-fight must not leave a group or the group
  coordinator stuck (0.36 just fixed one stuck-bot freeze).
- **Herald and bots.** The API reads the normal character table (`DbCoreCharacter`, played in the
  last 31 days). The bot code doesn't reference that table, so bots may not appear at all. To be
  checked.
- **Chat volume.** At about 1,000 bots per realm, every new chat source needs a per-channel budget.

## Herald test (before building)

On Chris's PC, with a test install only:
1. Turn on `atlas_api` and start the server.
2. Open `http://localhost:5000/docs` and try `/stats/rp`, `/guild/topRP` and `/realm/albion`.
3. Check that bots show up, that the server still starts normally, and that the API answers only
   on this PC.

Also to confirm in code: `ApiHost` calls `api.Run()` in its constructor, which may hold up server
startup; and it has a password-protected shutdown endpoint, so it must stay local-only.

## Guild tiers (planning, chosen 2026-10-28)

Owner: Chris. **Planned for 1.2, as one guild social package** (Chris, 2026-10-28): recruitment
policies, standing, tier benefits and costs ship together. Not split; the recruitment half was
considered for 1.1 and declined. Goal: bot guilds that differ in **who they let in**, with the
most exclusive ones worth earning through people and organization, never stat buffs. Ideas borrowed
from offline WoW: a per-bot relationship score (deseven's `mod-pbc` for AzerothCore Playerbots) and
guilds that invite you only once enough members like you (Erenshor). Credit both in the release note.

### What already exists (checked on `release/1.1`, 2026-10-28)

- **Charters** say what a guild is for: Leveling, Social, RvR, Raiding, Crafting
  ([BOT-GUILDS.md](BOT-GUILDS.md) 3.1). Dues, merit buffs, keep claims and RvR/Raiding alliances
  already follow the charter.
- **Recruiting is open everywhere:** any "lf guild" gets an invite from the best-fitting guild, and
  officers near you offer invites unprompted (3.5).
- **Bot memory of you** gives each bot a mood toward you (friendly, neutral, cool) from grouping,
  rezzes and insults ([LIVING-SERVER.md](LIVING-SERVER.md)). By design it is chat only and "never
  refuses you".
- **Formation never moves a bot already in a guild** (3.2).

### What's new

1. **A recruitment policy per guild**, separate from its charter. Starting shares (Chris's
   suggestion, not research):

   | Policy | Share | Who gets in | How it recruits |
   |---|---|---|---|
   | Open | ~40% | almost anyone | spams `/faction`; high churn, many low levels |
   | Friendly | ~30% | you, once enough members like you | whispers you after a good group |
   | Application | ~20% | a level or class need, maybe a member's vouch | posts needs ("lf healers 40+") |
   | Closed | ~10% | a strong relationship only | never posts |

   Policy leans on charter: Social and Leveling mostly Open, Raiding and RvR mostly Application or
   Closed. Each realm varies a little but stays in the same range; no realm is favored.
2. **Relationship standing:** grow the memory mood into a score (groups, rezzes, later heals and
   help; insults lower it) that Friendly, Application and Closed guilds read.
3. **Policies drift:** a struggling guild opens up, a growing one tightens.
4. **Open guilds churn:** members leave for stricter guilds as they level, which keeps low-level
   guilds refreshed.

### Benefits by tier (grow with exclusivity)

- **Raid access:** Closed guilds run epic dungeons and dragons on a schedule, so membership means a
  reliable raid slot. The "Epic Special Forces" bots come from this tier.
- **Organized RvR:** Closed and Application guilds run coordinated frontier raids and keep takes,
  the safer way into the deep frontier.
- **Crafter access:** their master crafters make gear for members at material cost.
- **Gear and loot:** fair handling of raid drops; members pass down old raid gear.
- **Knowledge:** true rumours, hidden places and boss tactics reach their guild chat first.
- **Prestige:** other bots react to the guild tag; server firsts and Herald mentions.

### Costs (so it's a trade-off, not a ladder)

- **Obligations:** raid attendance expected; skip too often and standing drops. Keep it light:
  Chris plays about 5 hours a week.
- **Drama:** loot disputes, rivalries, members quitting over leadership.
- **Less freedom:** pressure to play your main. Realistic, but must barely punish alt-hopping.

Open guilds keep: no obligations, easy low-level groups, the chattiest guild chat. Every tier should
be the right choice for some character at some point.

### Decisions needed before building

- **Memory rule:** gating invites on standing breaks "memory never refuses you". Proposed: guild
  invites only; grouping stays unaffected.
- **Formation rule:** churn means moving bots already in a guild, which formation never does today.
- **Owner's gameplay rules:** passed-down gear must not let gamebots skip earning their armor
  (proposed: hand-me-downs go to you, or only between bots that already earned that slot's tier).
  Crafter access must charge real materials and coin.
- **Guild bank:** bot guilds collect dues but never spend them (3.10). Crafter access and raid
  supplies are a natural first use.
- **Raid slots for you:** automatic raids recruit bots today; how you get a guaranteed slot needs a
  design check against `AutonomousRealmRaid`.
- **Chat volume:** recruiting styles and drama need a per-channel budget at ~1,000 bots per realm.

Depends on: bot memory of you (built); guild-based memory of you (bot-voices `DESIGN.md`, step 3).
