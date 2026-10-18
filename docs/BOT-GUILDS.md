# Bot guilds: design and status

Goal: autonomous gamebots belong to guilds the way 2003 players did. Each realm has a spread of
bot guilds, a few big and many small, each with a purpose (its **charter**). Bots carry the guild
name over their heads, show up in the guild window, talk in guild chat, group with guildmates,
recruit you, claim and defend keeps, pay dues, earn merit and buy guild buffs, and form realm
alliances. You can invite a bot into your own guild, or join one of theirs.

This affects only **autonomous gamebots**. Companion bots (`/spawn`, `/raid`) and real players
keep today's behavior.

## 1. What other forks have (checked 2026-10-12)

| Source | Bot guilds? | Use for us |
|---|---|---|
| Original (`shadowofze/OfflineDAoC`, `1cabc77`) | No. `GameBot.Guild` exists, but nothing assigns it. | Base |
| `stefanrows/OfflineDAoC` | **Yes, extensive.** "Camlann crews": up to 15 guilds with **no realm**, mixing all three realms, because that fork replaced three-realm RvR with free-for-all PvP. Includes bot members in the core `Guild` class, bots in the guild window, `/ginvite` on bots, guild chat both ways, charters, guild cohesion, grudges, keep defense and guild armies. | **Ported:** the core bot-member plumbing in `Guild`, adapted (credited in the code). **Ideas only:** charters, cohesion, grudges, keep defense. Its formation code assumes realmless guilds and isn't reused. |
| `lometur/HearthDAoC` | No (4 small guild-related lines, the same as the original) | — |
| Other identified forks (`scaleysandwich`, `BeezKneezItem9`, `BlakeFL`, `ThatFontGuy`, `catastrophic-impact`) | Not in their stated scope; small forks | — |

**Unchecked:** 4 of the original's 13 forks can't be identified from here, because the GitHub fork
list is blocked.

**Outside DAoC:** WoW server projects such as AzerothCore's playerbots module are known for bot
guilds. That is an idea source only; it wasn't examined in detail.

## 2. How guilds work in the base (verified in code)

**For players:**
- **Membership** works through `GamePlayer` and character rows (`DbCoreCharacter.GuildID`): ranks
  0–9, guild chat, the bank and dues, guild buffs (`guild_buff_xp` 5%, crafting 5%, RP 2%), keep
  claims (`AbstractGameKeep.Claim(GamePlayer)`), alliances and guild houses.
- **Bots fit none of this today.** They aren't characters (they're `offline_world_bots` rows),
  `Guild.AddPlayer` takes a `GamePlayer`, and `/ginvite` only targets players.

**Already wired for bots:**
- `GameBot.Guild`.
- The bot XP award applies the guild XP buff when the bot's guild has it.
- A bot NPC shows its `GuildName` over its head, and setting it re-sends the bot to nearby clients.
- Enemy viewers see a realm-rank title instead (`AutonomousNameMask`).

**Shipped guilds:** the release ships only the three starting guilds (Clan Cotswold, Mularn
Protectors, Tir na Nog Adventurers).

## 3. Design

### 3.1 Charters (what a guild is for)

| Charter | Membership leans toward | Later behavior |
|---|---|---|
| Leveling | levels 5–40 | groups with guildmates at camps |
| Social | any level | chatty guild chat, invites, events |
| RvR | level 35+ | musters, frontier raids, keep claims |
| Raiding | level 40+ | dungeon and dragon raids |
| Crafting | any level, crafters | trade talk, orders, bank |

### 3.2 Formation (per realm, deterministic, gradual)

- **Who's eligible:** non-retired bots of level 5 or more (`bot_guild_min_level`) that aren't in a
  player's guild.
- **How many join:** about 70% of eligible bots (`bot_guild_share`). The rest stay unguilded, as
  many players did.
- **Number of guilds:** about one per 22 member slots. There are none until a realm has 8
  members to place (the classic `guild_num` of 8 founders).
- **Guild sizes:** a Zipf-like spread (weights 1/(i+1)^0.8), so a few big guilds and many small
  ones. No guild targets fewer than 8.
- **Charters** follow a fixed pattern by size order: the largest guild is RvR, the next Leveling,
  then Social, Raiding, and so on. Every realm gets a mix.
- **Placement:** unplaced bots join in a fixed order (a hash of their id) at each check. Each goes
  to the guild with the largest shortfall, weighted by charter fit. **A bot already in a guild is
  never moved** by formation.
- **Ranks:**
  - the founder (highest level when the guild forms) is rank 0;
  - about 1 member in 12 becomes an officer (rank 1, at most 3);
  - everyone else is rank 9.
  - If the leader is retired, the highest-level member takes rank 0.
- **Names:** invented, realm-flavored and period-style; never a real guild's name. Examples:
  - Albion: "Order of the Silver Hart", "Avalon Wardens"
  - Midgard: "Sons of the Frost Wolf", "Mjollner Oath"
  - Hibernia: "Clann na Darach", "Circle of the Moonlit Grove"

  Some are casual, as many 2003 guilds were ("Pie and Pints", "Tavern Regulars"). Names are
  unique on the server.
- **Rank titles** are set by charter (for example Warlord, Captain, Soldier, Recruit for RvR).
- **When it runs:** 90 seconds after start, then every 10 minutes. It reads the roster and writes
  only its own two tables (below) and new `Guild`/`GuildRank` rows, so it never competes with the
  bots' own status saves.
- **New guilds:** they are founded on the game loop. A pass that founds guilds places members only in
  guilds that already exist; a follow-up pass 30 seconds later fills the new ones. So the first
  members appear about 2 minutes after start.

### 3.3 Storage

| Table | Columns | Meaning |
|---|---|---|
| `offline_bot_guilds` | GuildId, Realm, Charter, Ordinal, CreatedUtc, FounderBotId | marks a guild as bot-run |
| `offline_bot_guild_members` | BotId, GuildId, RankLevel, JoinedUtc | one row per guilded bot, **including bots in your guild** |

- The guilds themselves are ordinary `Guild`/`GuildRank` rows.
- **Release builder:** empties both tables, deletes every non-starting guild and its ranks, and
  removes the bot guild settings so a release ships with bot guilds off. This also closes an existing
  gap: a guild created in a played install would otherwise ship.
- **Progress transfer:** carries both tables with `offline_world_bots` and `Guild`. They are
  listed as optional tables (`OptionalProgressTables`), because a server creates them on its first
  start; a database that never ran this version simply has nothing to carry.

### 3.4 Membership and the guild window (phase 1)

- **Over its head:** a guilded bot shows its guild name to its own realm.
- **Guild window and `/gc who`:**
  - list bot members with level, class and zone (or "Offline");
  - for a bot guild, you see its members only if you're in it.
- **`/ginvite` on a bot** (targeted or by name), into your guild. The bot accepts if it is:
  - of your realm and alive;
  - not in combat;
  - not a leader or officer of its own bot guild;
  - within 10 levels of you.

  It then leaves its bot guild, joins yours at your guild's lowest rank, and says so. If it
  declines, it says why in /say.
- **`/gc remove <name>`** works on bot members of your guild.
- **Formation leaves bots in your guild alone.**

### 3.5 Guild social life (phase 2)

Bots only "talk" in a guild channel a real member can hear (rank with guild-chat hearing). Lines
are short, lowercase and period-plain (`AutonomousBotGuildChatLines`).

| Event | What happens |
|---|---|
| You log in | 1–3 online guildmates greet you after a few seconds (at most once per 10 minutes) |
| You level | 1–3 guildmates congratulate you |
| A bot levels | half the time it says "ding"; 1–2 others congratulate it |
| A bot dies | sometimes it grumbles in guild chat (at most once per guild per 2 minutes) |
| Quiet times | a guildmate says something that fits the charter (Social every ~4 minutes; Crafting every ~9; busier guilds up to 3× as often); sometimes another answers |
| You write in `/gu` | 0–3 bots answer: greetings, thanks, goodnights, group offers from bots near your level, and questions through the existing knowledge answers; a bot named in your line answers itself (at most once per 6 seconds per guild) |
| A bot group forms with 2+ guildmates | one of them says so in guild chat |

**Recruiting and joining a bot guild:**
- **Recruiting posts:** a bot guild officer posts in `/faction`, at most once per realm every 25
  minutes, and only while an unguilded player of that realm (level 5+) is online.
- **Asking for a guild:** write "lf guild", "looking for a guild" and the like in `/faction` or
  `/say`. Within 5 minutes of a recruiting post, "me" or "interested" also counts.
  - The guild that just recruited answers if one of its members is online. Otherwise the guild
    whose charter best fits your level answers.
  - Its highest-ranked online member replies, and you get a **yes/no dialog**.
  - Yes: you join at rank 8 (one above the bots' rank 9) and are welcomed.
  - No: no more offers for 24 hours.
- **Unprompted offers:** now and then, an officer standing near you while you're unguilded and
  within 10 levels offers an invite, at most once a day per character.

### 3.6 Grouping (phase 3)

**Formation order:** group formation orders candidates guildmates-first (shuffled within each tier).
- In **PvE**, the leader's guildmates get the first claim on role slots; class roles still decide
  the final group.
- In **RvR**, roaming warbands fill with guildmates first. Siege parties and forced raids (the
  0.36 group paths) are formed by role and distance only; guild order and the "we're grouping"
  guild-chat line don't apply to them.

### 3.7 Keeps (phase 4)

- **Claims:** after a realm takes a keep or tower, a bot guild with enough members inside claims it
  under the player rules:
  - the keep's realm;
  - not under attack;
  - not a relic or portal keep, and not a battleground keep unless `allow_bg_claim` is on (off
    in the release);
  - `guilds_claim_limit` (1 in the release);
  - `claim_num` members inside (8; 4 for a tower).
- **When:** attempts run every 20 seconds for about three minutes after the capture.
- **Most members inside wins;** ties go to RvR, then Raiding.
- **You get the first chance:** a bot guild never claims while a real player of that realm is in
  the keep.
- **Effects of a claim:** the keep's guards and banners take the guild, and in the release the keep
  rises from level 4 to 5 (`starting_keep_claim_level`). The claim is announced realm-wide as
  usual, and in guild chat.
- **Defense:** when a claimed keep is attacked, the existing defense response orders the claiming
  guild's warbands first and never holds them in its 30% reserve. An officer calls for help in
  guild chat. This also applies to bots in **your** guild if your guild claimed the keep.
- **`AbstractGameKeep.ClaimForGuild(Guild)`** is the new guild-level claim. `Claim(GamePlayer)`
  now calls it with the player's guild, so player claims behave as before.

### 3.8 Economy (phase 5)

- **Dues:** a guilded bot pays its guild's dues share of the real coin it loots into the guild
  bank, with the player formula. A full bank leaves the coin with the bot, so nothing is created
  or lost. Bot guilds charge by charter:

  | Charter | Dues |
  |---|---|
  | RvR, Raiding | 5% |
  | Crafting | 3% |
  | Leveling | 2% |
  | Social | none |

  Your guild keeps whatever dues its leader set; bots you invited pay them.
- **Merit points:** a bot's level-ups earn its guild the same merit points as a player's
  (`level × (3 + level/25)`, one award per level gained).
- **Buffs:** a bot guild's leader spends 1,000 merit points on a 24-hour buff, the same price and
  effect as `/gc buff`, and only buff types the server offers:
  - RvR: realm points;
  - Crafting: crafting haste;
  - the rest: experience.

  The experience buff already counted for bots (`GameBot` XP award), and it counts for you if
  you're in that guild.

### 3.9 Alliances (phase 6)

- **Formation:** in each realm, the RvR and raiding bot guilds form one alliance of up to four
  guilds, led by the oldest RvR guild. It uses the same `Alliance` objects and `GuildAlliance` table
  as `/gc ainvite`, and is created on the game loop after formation.
- **Player alliances:** alliances that include a player's guild are never touched.
- **Alliance chat:** RvR and raiding bots sometimes talk on the alliance channel when a real member
  of the alliance can hear.

### 3.10 Not built, and why

- **Guild houses:** buying and furnishing a house needs the housing system's lot and rent flow,
  and bots would never use the house. A bot guild house would be decoration only.
- **Emblems:**
  - Bot guilds have no emblem, so a keep they claim shows the default banner.
  - The emblem number packs a logo id, and which logo ids each realm's client offers isn't known
    here. A guessed id could draw nothing or another realm's logo.
  - Needs a check in the client first.
- **Shared Leveling-guild camps:** guildmates group first (phase 3), but a Leveling guild doesn't
  pick one camp for its members outside groups.
- **Spending the guild bank:** bot guilds collect dues but don't spend the bank yet (in this
  server, banks have no built-in use besides withdrawals).
- **Bot-to-bot memory and loyalty, and guild-based memory of you:** not part of bot guilds. The
  Social Expansion's Living Server memory remembers you by name, but not by guild.

### 3.11 Settings (all off until `bot_guilds_enabled`)

| Setting | Default | Meaning |
|---|---|---|
| `bot_guilds_enabled` | False | the master switch: formation, membership, invites and everything below. Off keeps every bot guildless and today's behavior. |
| `bot_guild_share` | 0.70 | share of eligible bots placed in bot guilds |
| `bot_guild_min_level` | 5 | lowest level that joins a bot guild |
| `bot_guild_social` | True | guild chat, greetings, replies, recruiting (phase 2) |
| `bot_guild_recruit_player` | True | bot officers may invite you (phase 2) |
| `bot_guild_claims_keeps` | True | keep claims and claim defense (phase 4) |
| `bot_guild_economy` | True | dues, merit points, buffs (phase 5) |
| `bot_guild_alliances` | True | realm alliances (phase 6) |

Phase 3 (grouping) has no switch of its own; it follows `bot_guilds_enabled`.

**To turn on:**
- **Launcher:** the Social Expansion fork adds a launcher tab with a switch for every part. It is
  not part of this branch; the script below does the same.
- **Script:** `python tools/claude-version/set_bot_guilds.py --on --apply`, with the server
  closed. It backs up the database first.
- **One part off:** for example `--claims off --apply`.
- **In game:** with GM rights, `/serverproperties refresh` after changing the database.

**Release safety:**
- The release builder deletes every `bot_guild*` setting, so a release ships with bot guilds off.
- It empties the bot guild tables and alliances, deletes non-starting guilds and their ranks, and
  blanks starting guilds' alliance ids.

### 3.12 Removing bot guilds

Turning bot guilds off (`--off`, or the launcher tab) keeps the guilds the bots formed, so turning
them on again brings them back. Going back to an older server build keeps them too. To remove them
from your world for good, close the server, launcher and game, then run:

```
python tools\claude-version\set_bot_guilds.py --purge            (shows what it would remove)
python tools\claude-version\set_bot_guilds.py --purge --apply    (removes it)
```

`--purge --apply` backs up the database first (to `runtime\server\deployment-backups\bot-guilds-purge-<time>`), then:
- turns bot guilds off (`bot_guilds_enabled` False);
- releases every keep and tower a bot guild claimed, as `/gc release` does: no claim, and the
  unclaimed keep level (`starting_keep_level`, normally 4). The realm that took it keeps it;
- ends every alliance a bot guild leads. Your guild leaves such an alliance; an alliance your
  guild leads stays, without the bot guilds;
- moves each of your characters in a bot guild to their realm's starting guild (Clan Cotswold,
  Mularn Protectors, Tir na Nog Adventurers) at the starting rank, as for a new character. With
  starting guilds off, or none for that realm, the character is left guildless;
- deletes the bot guilds with their ranks, banks, dues and merit, and their bot guild records
  and bot memberships.

Your own guilds, their claims and alliances, and bots you invited into them are not touched. It
refuses while the server is running (or did not close cleanly), and running it again changes
nothing. Test: `tools/claude-version/tests/test_set_bot_guilds.py`.

With bot guilds off, the server no longer rewrites bot guild ranks or dues at startup, so the saved
guilds stay exactly as they were (`StartupRewrites_*` in `UT_AutonomousBotGuilds`).

## 4. Tests

**Automated, in CI on every pull request and branch push** (`UT_AutonomousBotGuilds`, `UT_AutonomousBotGuildSocial`,
`UT_AutonomousBotGuildKeeps`, `UT_AutonomousBotGuildEconomy`, `UT_AutonomousBotGuildAlliances`):
- **Formation:** guild counts, sizes, charters, names, placement, ranks, invite rules, adding and
  removing bot members.
- **Chat lines:** filled, short and in era; reply classification; guild requests; chat pacing;
  responder counts; which guild answers you; guildmate ordering.
- **Keeps:** every claim rule; members needed; claimant choice; defense priority.
- **Economy:** dues conserve coin; merit formula; charter dues; buff choice.
- **Alliances:** alliance planning.

What the tests cover is the rules. The live behavior (timers, chat delivery, dialogs, claims,
alliances) only runs in a real server.

**In game (owner), with `--on`:**
1. Log in after 10 minutes. Guild names show over your realm's bots.
2. Write "lf guild" in `/faction`: an officer answers and a dialog appears. Accept; you should be
   welcomed in guild chat.
3. In the guild, say "hi" in `/gu`; log out and back in; level up. Bots should answer, greet and
   congratulate, without flooding.
4. Watch guild chat for 30 minutes: idle lines, dings, guild groups forming.
5. `/gc info`: the bank should grow from dues, and merit points from bot level-ups; a buff may
   appear once merit reaches 1,000.
6. Take a keep with a bot warband and stay outside it. A bot guild should claim it within about 3
   minutes. Then attack a keep your bot guild holds from the other side (GM tools), or watch for
   it, and check that its guildmates rally.
7. In an RvR guild, watch `/as` for alliance chatter.
8. `/ginvite` a bot into your own guild and `/gc remove` it.

## 5. Status

- **Phases 1–6** were built on `feature/bot-guilds` and released in 0.36-se.1.
- **Automated tests:** the full server suite plus the guild tests pass, on the Social Expansion
  fork and on this branch alone (bot guilds on upstream `main` with none of the fork's other features).
- **Partly checked in game** (2026-10-17, build 0.36-chris.8, Godot client, about 20 minutes,
  50 bots: 34 Albion, 8 Midgard, 8 Hibernia).
  - **Seen working** (section 4 items):
    1. One guild formed 90 s after start: the RvR guild "Order of the Oaken Shield" in Albion.
       Its name shows over its members ("Seraorith <Order of the Oaken Shield>").
    2. "lf guild" in `/faction` got "inviting you now Claudecheck" from an officer, then the
       yes/no box. On joining: "hey Claudecheck, welcome. ask if you need anything" and
       "welcome Claudecheck!".
    3. "hi all" in `/gu` was answered ("o/ Claudecheck"). A ding to 41 got "nice, 41 already?"
       and "grats Claudecheck!". Logging back in got "yo Claudecheck". No flooding.
    4. Idle guild chat ("guild warband forming, frontier bound", "who is geared for rvr right
       now"). Watched for about 15 minutes, not 30.
    5. `/gc info`: dues 5%, and the bank held 8 silver 56 copper after about 15 minutes. Merit
       was still 0, since no guild bot levelled in that time.
  - **Bug found: the starting guild blocked recruiting.** New characters join their realm's
    starting guild (Clan Cotswold in Albion). `TryAnswerGuildRequest` and recruiting skipped any
    player with `Guild != null`, so out of the box "lf guild" got no bot guild answer. It
    worked only after `/gc quit`. **Fixed in 0.36-chris.9**, below.
  - **Not checked:** keep claims (6) and `/ginvite` and `/gc remove` on a bot (8).
  - **Only one guild formed.** Midgard and Hibernia, with 8 bots each, got none. With a small
    roster, this feature shows little outside the biggest realm.
- **Second check (2026-10-17, build 0.36-chris.9, 300 bots per realm: 150 at level 1, 150 at level 50):**
  - **Starting-guild fix, seen in game.** Claudecheck was put back in Clan Cotswold (rank
    Initiate). "lf guild" in `/faction` got "Claudecheck, sending you an invite to Order of the
    Oaken Shield" and the yes/no box. Accepting left Clan Cotswold by itself, joined the bot guild
    as Soldier, and got a welcome. Test: `AStartingGuildDoesNotStopBotGuildsRecruitingYou`.
  - **Guilds across all realms.** 90 seconds after start, 14 more guilds formed, for 5 per realm
    (leveling, raiding, RvR and social charters).
  - **Alliances (7), seen.** 30 seconds later each realm's raiding guild joined its RvR guild's
    alliance. The game said "Knights of the Golden Chalice has joined the alliance of Order of the
    Oaken Shield". `/as` chatter was not watched.
  - Guild chat stayed lively with no flooding ("2 of us heading out to the frontier, join up if
    you can", "4 guildies grouped up in Snowdonia"), and three guildmates greeted me at login.
