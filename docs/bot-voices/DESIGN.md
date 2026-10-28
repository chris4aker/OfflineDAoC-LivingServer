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
| plain | 35% | the line as written: lowercase, light punctuation |
| proper | 15% | capitalized, full stops, some abbreviations spelled out (u → you, pls → please) |
| terse | 25% | shortened: more abbreviations (you → u, thanks → ty), no punctuation |
| chatty | 20% | adds ":)", "heh", "lol" and a little extra |
| typo-prone | 5% | plain, plus an occasional swapped-letter typo (at most one per line, 1 line in 6) |

### 1.3 Experience: what it knows

Taken from the bot's level and played time:
- **Newbie** (below 15, or its first character): asks.
- **Regular:** asks and answers.
- **Veteran** (40+, or 50 with alts): answers, gives camp and spec advice, joins debates.

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

Roles are filled from different bots of the same realm. A role can require a class or type:
`@role B type=Crafter,NoLifer`. A role whose lines only fit some levels ("i am not 50 yet") also takes
`levels=`: `@role D type=Socialite levels=30-49`. Those roles are cast first.

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
| `{capital}`, `{enemy}` | its capital; an enemy realm |
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
| `in_battleground`, `in_frontier` | in RvR: a battleground (Abermenai, Thidranki, Murdaigean, Caledonia, Cathal Valley), or the frontier |
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
2. **Skip** any line used in this realm in the last 6 hours: the time each line was last used,
   per realm (up to 5,000 entries, well above the corpus size). Grats and rez thanks are short and
   frequent, so they skip this check (a "gz" can repeat).
3. **Pick** by weight, fill the slots, apply the voice, and cap the length at 160 characters.

### 3.5 Long threads

**Format:** a scene can run 15–30 lines with up to six speakers (roles A–F). `~ N` marks a lull of
N seconds.

**Pacing at runtime:**
- each line waits 3–10 seconds, longer for longer lines (typing time);
- a long thread takes about 4–8 minutes.

**Interruptions:**
- **A speaker who goes into combat or logs off is replaced** by another bot of the same realm that fits
  the role, or the thread skips that speaker's remaining lines.
- **If fewer than two speakers are left,** the thread ends early, as real ones did.

**Variety:**
- **Spacing:** at most one long thread per realm every 45–60 minutes. Short scenes fill the gaps.
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

### 3.8 Pacing (0.36-se.4, voices on)

- Unprompted lines: 2-4 minutes apart per realm in /faction and 1-2 minutes per region in /say (they were a
  fixed 3 minutes and 90 seconds).
- About one bot in seven is a regular who talks 2.5 times as often; about a third talk half as often.
- Replies take longer to type when longer (about 40-60 ms a character after a moment to read).
- Typo-prone writers send a "*word" correction for about half their typos.
- The no-repeat window is three days (it was six hours, which had expired by the next evening), saved in
  `offline_bot_lines_heard` and read back at start-up. When a kind's pool is used up within three days, lines
  not heard for six hours may come back; a short reply to a player, after 40 minutes.
- A bot that is fighting when its reply is due hands it to another bot that could have answered.
- Class talk answers with lines about the class asked about, or with class-neutral lines.

## 4. Rates

- **Kept from today:** never in combat or while casting; only when you can hear (for /say) or are
  online (for /faction); per-bot cooldowns.
- **Changed:**
  - **/faction openers** scale gently with realm population: about 20 an hour at up to 300 online,
    rising to about 40 an hour at 1,000 or more (`bot_voices_faction_rate`).
  - **Debates:** at most one per realm every 30–45 minutes.
  - **Gossip about you:** at most one line per realm every 30 minutes.
  - **Greetings:** at most one per bot every 2 hours. Nearby greetings to you are capped at 1 a
    minute.

## 5. Settings

**Beta 1.1 (the talking update):** `bot_voices_enabled` is on by default, `bot_voices_chat_rate` (the planned
`bot_voices_faction_rate`) and `bot_voices_tells` exist; see [TALKING-UPDATE.md](TALKING-UPDATE.md). The table below is
the original plan.

**Status (2026-10-12):** `bot_voices_enabled` and `bot_voices_debates` exist (see
[../BOT-PERSONALITIES.md](../BOT-PERSONALITIES.md)). The others come with memory (step 3) and aren't
in the server yet.

| Setting | Default | Meaning |
|---|---|---|
| `bot_voices_enabled` | False | use the corpus; off keeps today's lines |
| `bot_memory_enabled` | False | record and use memory of your characters |
| `bot_voices_faction_rate` | 1.0 | multiplier on /faction openers |
| `bot_voices_debates` | True | allow debate scenes (when voices are on) |
| `bot_voices_gossip` | True | allow /faction lines about you (when memory is on) |
| `bot_invites_player` | True | friendly bots may invite you (when memory is on) |

## 6. Tests

**Automated:**
- **Build script:** every line parses; only known slots, flags and kinds; no out-of-era words; no
  duplicates; length limits; coverage minimums per realm and level band.
- **Unit tests:**
  - the voice transforms;
  - line choice respects the filters and the 6-hour repeat window;
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

**Status (2026-10-12):** steps 1, 2 and 4 are in the server on `feature/bot-personalities`, off by
default, with unit tests; nothing has been checked in game yet. Step 3 (memory) hasn't started, so
lines with `{player}`, `{place}` or `{player_class}` are skipped. Details:
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
