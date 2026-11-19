# Bot voices and memory: design

Goal: gamebots that sound like 2002–2003 players and **remember you**. It builds on the existing
chat system (`AutonomousBotChat`, `AutonomousBotChatCoordinator`, `AutonomousChatIntentModel`,
`AutonomousChatKnowledge`) and replaces its fixed phrase lists with a tagged corpus of new lines. It
only affects autonomous gamebots; companions (`/spawn`, `/raid`) keep their current behavior.

Material: [RESEARCH-BRIEF.md](RESEARCH-BRIEF.md), [PERIOD-CHAT-RESEARCH.md](PERIOD-CHAT-RESEARCH.md) (2026-10-18). How to write lines: [WRITING-GUIDE.md](WRITING-GUIDE.md). Lines: `source/server/GameServer/bots/autonomous/data/chat/`.

## 1. Personalities and voices

Each bot gets two things, fixed from its database id so they never change between logins.

### 1.1 Personality type: what it talks about

The types come from the M1 plan, plus the v4 model's no-lifer:

| Type | Talks about | Leans toward |
|---|---|---|
| Leveler | camps, XP, groups, levels | lfg, camp checks, "ding" |
| Crafter | prices, materials, orders, quality | trade, spellcraft, masterpieces |
| Socialite | people, guilds, jokes, invitations | greetings, banter, invites, gossip |
| Explorer | places, named monsters, rumors | "found a spot", directions, legends |
| Daredevil | hard pulls, near deaths, bragging | orange and red cons, dungeons, frontier |
| NoLifer | templates, realm ranks, the meta | specs, RvR, DF, debates |

Types weight topics; they don't forbid them. Any bot can still ask for a group or say "ding".

### 1.2 Voice: how it writes

Set in `voices.json` and applied after a line is chosen:

| Voice | Rough share | Style |
|---|---|---|
| plain | 46% | the line as written: lowercase, light punctuation |
| proper | 4% | capitalized, a full stop on a line of more than three words, some abbreviations spelled out (u → you, pls → please); a line that starts with a chat word ("lol", "np", "ty") stays as typed |
| terse | 25% | shortened: more abbreviations (you → u, thanks → ty), no punctuation |
| chatty | 20% | adds ":)", "heh", "lol" and a little extra |
| typo-prone | 5% | plain, plus a typing slip in 1 line in 12: in one lowercase word of five letters or more, a dropped letter 3 times in 4, else two neighbouring letters swapped, never the first or last letter; a "*word" correction follows about half the time |

The shares changed on 2026-11-03 (fewer tidy writers: proper 15% → 8% → 4%, plain 35% → 42% → 46%, and the typo
chance 1 in 6 → 1 in 12, after the blind rounds found ambient chat too polished).

### 1.3 Experience: what it knows

Taken from the bot's level, as the line flags `newbie` and `veteran` (`AutonomousBotVoices`): a line with `@needs newbie`
is said only by a low-level bot, one with `@needs veteran` only by a high-level one; every other line by anyone. (The
plan was three bands with preferred kinds and topics; that was never read and is not built.)

## 2. Memory of you

### 2.1 What is remembered

One row per bot per character of yours, in a new table `offline_bot_player_memory`:

| Field | Meaning |
|---|---|
| BotId, CharacterId | the pair |
| FirstMetUtc, LastSeenUtc | when the bot first noticed you and last saw you |
| GroupedCount, GroupedMinutes | groups together and how long |
| KillsTogether | monsters killed together |
| HealedByYou, RezzedByYou | help you gave |
| HealedYou, RezzedYou | help it gave |
| TradesWithYou | Realm Exchange sales between you |
| InsultsFromYou | abuse aimed at it (the intent model's "abuse", with its name or nearby) |
| KilledByYou, KilledYou | frontier kills between you (enemy bots) |
| LastEvent, LastEventPlace, LastEventUtc | the most recent memorable thing, for "remember…" lines |
| PendingMention | an event it hasn't brought up yet, such as an unthanked revive |
| Affinity | −100 to +100, from the counters, fading toward 0 over weeks without contact |

### 2.2 How affinity moves

As built (`AutonomousLivingServer.Memory`), a bot reads you as friendly, neutral or cool from what it remembers; the
arithmetic below is the first plan and was not checked against the code line by line.

- **Up:** +2 per group, +1 per 10 kills together, +4 per heal (at most 3 a fight), +10 per revive,
  +3 per trade.
- **Down:** −15 per insult; −20 when you kill an enemy bot. That one is a grudge, not a dislike, and
  the bot also gets a "rival" flag.
- **Fading:** 2% of the distance to 0 per day without contact.

### 2.3 Where events come from (hook points in the existing code)

| Event | Hook | Status |
|---|---|---|
| Grouped / left | `BotGroupInvite.TryInvite`, group membership changes | found |
| Healed | `GameLiving.ChangeHealth` (virtual; override in `GameBot`, spell source = your character) | found |
| Revived | `GameBot.OnResurrectedBy(caster)` | found |
| Kill together | the XP award in `AbstractServerRules` (bot and you in the same group) | found |
| Killed / was killed | `GameLiving.Die(killer)` | found |
| Trade | Realm Exchange sale settlement (`AutonomousBotEconomy`) | to locate |
| Insult | `AutonomousBotChatCoordinator.OnPlayerLocalChat` / `OnPlayerFactionChat` | found |

### 2.4 Limits

- **Only your characters** are remembered (accounts with real players), not bot-to-bot pairs. A
  lighter bot-to-bot memory is a later step.
- **At most 20,000 rows**, pruning the stalest neutral rows first.
- **Writes are batched** into the existing status save (`AutonomousBotStatusPersistence`), never per
  event.
- **Enemy bots** remember you as a character too. You only see what they *do*: they seek you out, or
  avoid you. They never say your name, because they never saw it.

### 2.5 What memory changes

| Situation | Behavior |
|---|---|
| A friendly bot sees you (within say range, at most once per 2 hours per bot) | greets you by name; brings up its pending event ("ty again for the rez in Spindelhalla") |
| Affinity 40+ and both of you looking for a group | it invites you; you get a yes/no dialog |
| You ask nearby for a group | friendly bots answer first, and offer |
| Insulted (affinity below −30) | short, cool replies; it won't join your group for a day |
| After a good group | sometimes praises you in /faction ("{player} healed us through spindel, solid") |
| After a bad wipe you led | sometimes a dry remark (never mean) |
| Enemy rival flag | a bot of the other realm that you killed is more likely to hunt your zone in the frontier (RvR later) |

## 3. The corpus

### 3.1 Authoring format

Plain-text `.lines` files that anyone can read and edit. **Directives** set tags for the lines that
follow, until changed:

```
@kind ambient            # ambient | reply | lfg | trade | rvr | greet | thanks | grudge | gossip | ding | death | camp |
                         # group | guild | closing (and the server life kinds, docs/LIVING-SERVER.md)
@topic leveling          # free label for weighting and variety
@who Leveler,Socialite   # personality types, or *
@realm *                 # alb | mid | hib | *
@class *                 # class names, or *
@levels 20-34            # level band of the speaker
@needs in_group,!in_dungeon  # context flags (see 3.3); "!flag" means the flag must not be set
@intent grouping         # for replies to a player: the chat intent the line answers
@invites boast           # for openers: what kind of answer the line invites (see 3.6)
@answers boast,joke      # for replies and closings: the openers they fit (see 3.6)
@mood friendly           # for memory lines: friendly | neutral | cool | rival
@weight 1                # optional, default 1
lfg {level} {class}, can heal and buff
anyone at {camp}? lf a few more
```

- `@kind` starts a new block and resets every other tag to its default; the other directives change
  one tag and keep the rest.
- Lines starting with `#` are comments, and empty lines are ignored.
- **Lines built from parts** (2026-10-26): `[a|b|c]` is one of a, b or c, and `[a|]` is a or nothing:
  `[ugh|man|heh], [this camp|this spot] is [slow|dead] tonight`. The build writes every way of saying the line as its
  own line (all of them up to 48, else a fixed sample of 48), each checked like any other line, so the no-repeat window
  works line by line. The family shares one line's weight times the square root of its size (48 ways weigh about 7
  lines), so it comes up more often than a single line but rarely in the same words. Every combination must read
  naturally on its own.

**Debates** use scene blocks:

```
@scene buffbots
A: {q} is it just me or is everyone running a buffbot now
B: second account, more fun, less waiting. dont see the harm
C: one person playing a whole group isnt a group tho
A: fair points both. still wish i had one
```

Roles are filled from different bots of the same realm. A role can require a class (`class=` is kept) or prefer a
type (`type=` is relaxed when nobody fits): `@role B type=Crafter,NoLifer`. A role whose lines only fit some levels
("i am not 50 yet") also takes `levels=`: `@role D type=Socialite levels=30-49`. Those roles are cast first. A role
whose line claims a class ability ("rez coming", "my pet pulled") must name the classes that have it.

- **Levels:** bots up to five levels outside the scene's `@levels` may play it, except in a scene whose band spans
  fewer than 12 levels (a battleground, a newbie scene), which is cast inside its band only (1.2).
- **Who is cast:** autonomous gamebots only, never beggars or roleplayers (their old-tongue style would rewrite the
  lines, 1.2). The shared `{dungeon}` and `{camp}` are picked for the level of the bot that speaks first.
- **Time and news (`@needs`):** a scene may need a time of day (`day`, `evening`, `night`) or, since 1.2, a realm news
  flag: `defending`, `attacking`, `keep_lost_recent`, `keep_won_recent`, `relic_lost_recent`, `relic_won_recent`,
  `df_changed_recent`, `df_ours` (and the other flags in `build_corpus.py` `SCENE_NEWS_FLAGS`). A **live scene** plays
  only while its flag is true, comes before ordinary scenes then, and takes `{keep}`, `{relic}` and `{enemy}` from the
  news (`AutonomousBotDebates.LiveSlots`). The live scenes are in `data/chat/scenes_live.lines`; the talk versions of
  the same topics play the rest of the time.
- **Say only what is true:** a scene line says what the realm can see (the warnings, the news, `/realm`, `/relic`). No
  bot says it is coming ("omw"), sending an invite or standing at a keep; a group's talk is told as a story about a
  group, because the cast are not in one.

`tools/bot-voices/build_corpus.py` checks the files and writes `corpus.json`, which is embedded in
the server like the camp catalog.

### 3.2 Slots, filled at runtime

| Slot | Value |
|---|---|
| `{me}` | the speaker's name |
| `{class}`, `{level}`, `{realm}` | the speaker's class, level and realm |
| `{zone}`, `{monster}`, `{npc}`, `{item}`, `{count}` | as today, from the speaker's live task |
| `{monsters}` | the plural of `{monster}` ("2 hill cats", "giant wolves") |
| `{camp}` | a real camp from the catalog for the speaker's (or listener's) level and realm |
| `{dungeon}` | a real dungeon of the speaker's realm, by level |
| `{capital}` | its capital |
| `{enemy}`, `{enemy_mod}`, `{enemy_realm}` | one enemy realm in three shapes (1.2 chat audit, TEXT-018): the people as players typed them ("hibs": "lots of {enemy} at the mile gate"), the word before a noun ("hib": "a {enemy_mod} stealther"), the realm's name ("Hibernia": "{enemy_realm} took a relic"). The news board's lines still get the realm's name in `{enemy}` until news.lines moves to `{enemy_realm}`. The builder refuses `{enemy} is/has` and `{enemy}` before a noun. |
| `{keep}`, `{frontier}` | a real keep or frontier zone (from the world database) |
| `{player}`, `{player_class}`, `{place}` | from memory: your name, your class, where the event happened. In a reply, `{player}` is whoever is answered. |
| `{q}` | an optional opener ("hey", "so", "random question but") |

A line whose slot can't be filled is skipped, never sent half-empty.

### 3.3 Context flags (`@needs`)

`in_group`, `solo`, `in_town`, `in_dungeon`, `in_rvr`, `at_camp`, `traveling`, `just_leveled`,
`just_died`, `bag_full`, `has_listing`, `knows_player`, `player_nearby`, and since 0.36-se.4:

| Flag | Set when |
|---|---|
| `outdoors` | not in a dungeon and not in a capital |
| `in_capital` | in Camelot City, Jordheim or Tir na Nog |
| `in_battleground`, `in_frontier` | in RvR: a battleground (Abermenai, Thidranki, Murdaigean, Caledonia), or the frontier |
| `day`, `evening`, `night` | the PC's clock: 6-17, 17-23, 23-6 |
| `in_player_group` | the bot is in the real player's group |
| `can_rez` | the bot has a resurrection spell it can cast |

A flag with `!` in front must not be set (`@needs !in_group`).

**Build checks** (`tools/bot-voices/build_corpus.py`): an opener with `{zone}` needs a place flag; a line
with bedtime words ("late night", "off to bed", "midnight") needs a time flag; "{count} {monster}" must be
`{monsters}`; later internet phrases ("change my mind", "fight me", "vibes") are banned with the out-of-era
words. "tonight" and "all night" need no flag: by day the server says "today" and "all day".

### 3.4 Choosing a line

1. **Filter** by kind, the speaker's realm, class, level band, type (with type weight) and flags.
2. **Skip** any line used in this realm in the last three days: the time each line was last used,
   per realm (up to 5,000 entries per realm, the oldest forgotten first; the corpus holds about 35,000 lines, so a
   realm heard all day can forget early). When a kind's pool is used up, a line not heard for six hours may come back.
   Grats and rez thanks are short and frequent, so they skip this check (a "gz" can repeat).

2. **Skip** any line used in this realm within the no-repeat window (three days, see 3.8): the time each line was last used,
   per realm (up to 5,000 entries, well above the corpus size). Grats and rez thanks are short and
   frequent, so they skip this check (a "gz" can repeat).
3. **Pick** by weight, fill the slots, apply the voice, and cap the length at 160 characters. 1.2 chat audit: the voice works on
   the line with its slot values masked (no typo lands in a name), adds no laugh to a functional, laughing, shouted or
   spam/alarm/news/combat/roleplay line, and a line over 160 characters once filled is skipped for the next, never cut in
   mid-word.

### 3.5 Long threads

**Format:** a scene can run 15–30 lines with up to eight speakers (roles A–H). `~ N` marks a lull of
N seconds.

**Pacing at runtime:**
- each line waits 3–10 seconds, longer for longer lines (typing time);
- a long thread takes about 4–8 minutes.

**Interruptions:**
- **A speaker who is fighting is waited for** (up to 45 seconds). One who stays busy, dies or logs off before
  speaking is replaced by another bot of the same realm that fits the role; once a role has spoken it keeps its bot
  (1.1.3), and its remaining lines are skipped instead.
- **If fewer than two speakers are left,** the thread ends early, as real ones did.

**Variety:**
- **Spacing:** one scene of any length per realm every 30–45 minutes (less often with a small roster,
  `AutonomousChatBudget.SceneScale`). A live scene may start 8 minutes after the last scene ended while its news is
  true.
- **No repeats until the pool is used up:** the server remembers which scenes each of your
  characters' realms has heard (a small saved list). It never replays a scene until every scene that
  fits has played.
- **Fresh wording each time:** every line has 2–3 alternative wordings (`|`), and optional lines
  (`?`) come and go. When a scene does come back, it reads differently: the median scene has about
  1,500 combinations.

### 3.6 Conversations: what a line invites (0.36-se.4)

Every opener (kinds ambient, lfg, trade, rvr, camp, group, guild) says what answer it invites:

| `@invites` | Meaning | Who answers (when another bot can) |
|---|---|---|
| `none` | nobody would answer ("brb dinner") | nobody |
| `chat` | a remark | one bot, 45% of the time |
| `joke`, `boast` | | one bot 55%, two 15% |
| `complaint` | | one bot 53%, two 12% |
| `news` | | one bot 45%, two 15% |
| `lfg`, `trade` | | one bot 55% |
| `ask`, `ask_where`, `ask_advice`, `ask_opinion`, `ask_anyone`, `ask_<topic>` | a question | one bot 62%, two 30% |

Without `@invites` the builder infers it: a line ending in "?" becomes ask_where, ask_opinion, ask_anyone,
ask_advice or ask by its words; otherwise lfg (kind lfg), trade (kind trade) or chat.

Replies say what they answer with `@answers`. A specific question (`@invites ask_bind`) is answered by
`ask_bind` replies first, then by general `ask` replies. A question about a place or a trainer gets a real
answer from the world (`AutonomousChatKnowledge`) before any written reply.

After a reply was said, the opener's speaker may close in its own words (`@kind closing` with `@answers`
by family: ask 50%, lfg and trade 35%, others 20%). Never after a reply that asks something.

A player's line is read by `AutonomousChatShortTalk` (short chat and common requests) and then the
classifier, and answered by `@intent` replies. How many bots answer depends on what was said: a hello or a
ding in /faction gets 1-3, a "lol" usually none, a named bot always answers. A short reply heard more than
40 minutes ago may be said again rather than leave a player unanswered.

### 3.7 Group and guild talk (0.36-se.4)

- **Your group:** out of combat, one bot in the real player's group says a `group` line in /group every
  4-8 minutes (the first one or two minutes after the group forms). A groupmate may answer it as above.
- **Bot groups near you** say `group` lines in /say; `group` lines never go to /faction.
- **Guild chat** (bot guilds, `bot_guild_social`): idle lines are `guild` lines by `@topic` (leveling,
  social, rvr, raiding, crafting, general), with the no-repeat window, voices on or off. A guildmate answers
  when the line invites it. Player /gu lines are answered from `reply` lines by intent.

### 3.8 Pacing

How often bots talk, in every channel, is set by the chat budget: [CHAT-BUDGETS.md](CHAT-BUDGETS.md) (floors, targets
and ceilings per channel, the population and hour scaling, `bot_voices_chat_rate`, bursts and the reply budget). The
0.36-se.4 numbers that stood here (2-4 minutes between /faction openers) are two versions old.

- Each bot also has a fixed talkativeness from its id (`AutonomousBotPersonality.RegularFactor`): about one bot in seven
  is talkative (2.5 times as often), about a third quiet (half as often). On top of that each realm has a few rotating
  regulars (CHAT-BUDGETS.md); the two stack.
- Typo-prone writers send a "*word" correction for about half their typos.
- The no-repeat window is three days (it was six hours, which had expired by the next evening), saved in
  `offline_bot_lines_heard` and read back at start-up. When a kind's pool is used up within three days, lines
  not heard for six hours may come back; a short reply to a player, after 40 minutes.
- A bot that is fighting when its reply is due hands it to another bot that could have answered.
- Class talk answers with lines about the class asked about, or with class-neutral lines.

## 4. Rates

The rates are in [CHAT-BUDGETS.md](CHAT-BUDGETS.md). What stays from the first plan: never in combat or while casting;
only when you can hear (for /say) or are online (for /faction); per-bot cooldowns; at most one debate per realm every
30–45 minutes.

## 5. Settings

The settings are server settings, category `bots`, and most are in the launcher's Living Server tab, all on by default
since 1.2: `bot_voices_enabled`, `bot_voices_debates`, `bot_voices_chat_rate` (how much bots talk, 0–3, default 2),
`bot_voices_tells`, `bot_living_memory` and the other Server life parts. Each one's text is in the launcher and in
[../LIVING-SERVER.md](../LIVING-SERVER.md). The first plan's `bot_memory_enabled`, `bot_voices_faction_rate`,
`bot_voices_gossip` and `bot_invites_player` were never built under those names.

## 6. Tests

**Automated:**
- **Build script:** every line parses; only known slots, flags and kinds; no out-of-era words; no
  duplicates; length limits; coverage minimums per realm and level band. 1.2 chat audit (`tools/bot-voices/chat_rules.py`):
  the banned list with reasons (`banned.txt`, `@allow <id>`), the length with the longest real slot values (155), realm and
  class sanity, promise words only where the code acts, the enemy's shapes, keep claims, `{monster}` with `{camp}`, `{capital}`
  claims, `{q}` only before a question, role letters in scene beats, the extra time words, near-copies (lines and scene
  beats), a reviewed vocabulary (`vocabulary.txt`), the style round trip (`voice_style.py`, a port of the server's Style) and
  every `@needs` flag set by the server. A hit fails in the files of `STRICT_OWNERS` and is a warning elsewhere
  (`--check --warnings` lists them by owner). `check_runtime_names.py` runs the banned list over the chat string literals in
  code.
- **Unit tests:**
  - the voice transforms;
  - line choice respects the filters and the three-day repeat window;
  - slot filling never leaves `{…}`;
  - affinity math and fading;
  - memory rows survive save and reload;
  - nothing changes when the settings are off.

**In game (owner):**
- An hour in a busy realm: does /faction feel varied and period-true?
- Group with a bot, revive it, leave, then come back two hours later: does it remember?
- Insult one: does it cool off?
- Watch for repetition and anything out of era.

## 7. Order of work

**Status:** all four steps are built and, since 1.2, on by default; memory shipped as `bot_living_memory` (Beta 1.1).
The list below is the plan as written on 2026-10-12, kept for its history. Details:
[../BOT-PERSONALITIES.md](../BOT-PERSONALITIES.md).

1. **Research brief, this design and the first corpus** (this branch). No game code yet.
2. **Voices:**
   - load the corpus;
   - voice transforms;
   - line choice through the existing coordinator;
   - settings and tests.
3. **Memory:**
   - the table;
   - event hooks;
   - greetings and thanks;
   - gossip;
   - invites (needs a bot-to-player invite dialog: the current invite packet expects a player
     inviter).
4. **Debates.**
5. **Later:** bot-to-bot memory, enemy rivals hunting you in the frontier, and lines from any forum
   threads the owner saves.
