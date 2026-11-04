# The chat budgets: how busy the world is, and when a bot answers you

Living Server Beta 1.1.3. The goal (owner, 2026-11-03): chat that feels like about 3,000 players across the three
realms on a busy 2003 evening, realistic and responsive, and never quieter than before. Bot chat has two separate
budgets:

- **The ambient budget** sets how much the world says on its own: openers, answers between bots, news, bursts, town
  broadcasts, guild and group talk. It has a floor and a ceiling per channel, and it grows with the number of bots
  online, with no cap.
- **The reply budget** sets when a bot answers *you*. A reply has to fit what you said. A weak reply becomes silence,
  or a short "?" or "ya?" when you sent the bot a tell or named it.

Silence is only ever the answer to a weak reply to the player. The ambient world never goes quiet to avoid a bad line.
It picks another line or another channel.

All the numbers live in one file, `source/server/GameServer/bots/autonomous/data/chat/chat-budget.json`, which has
comments on every value. It is built into the server. To tune a running install, copy it to `runtime/server/chat-budget.json`
and edit the copy. The server reads that copy when it starts. A broken copy is ignored and the built-in file is used.

## The ambient budget

### Channels

| Key | What it paces | 2003 counterpart |
|---|---|---|
| `faction` | the realm's /faction | none; this server's realm channel [O] |
| `lfg`, `trade` | the realm's /lfg and /trade channels | public chat groups, at best [O] |
| `say` | /say near the player | a busy camp or dungeon entrance |
| `capital` | /broadcast in Camelot, Jordheim or Tir na Nog, heard where the base game's broadcast reaches | capital broadcast |
| `town` | /broadcast in other towns with a broadcast area | a bind town |
| `guild` | one guild's chat | guild chat |
| `group` | gamebots' banter in the player's group, out of combat (fight calls are separate) | a PvE camp group |

Bots broadcast the way a player's `/b` does: with the server's `broadcast_type` (an area by default), only from a place
that allows it, and only to players who are there. Town broadcast is mostly selling and looking for a group, as in
2003. A seller's line may repeat as a macro every 3–8 minutes for 15–40 minutes.

### Floor, target and ceiling

Each channel has three numbers, in lines per minute as one player hears that channel, at the **design point**:
`design_bots_per_realm` bots online in the realm (1,000, a full 2003 server) at prime time.

- **Target:** the pace at which bots start unprompted lines. The gap between two openers is a minute divided by the
  target, varied from 0.4 to 1.6 times, so chat comes in bursts and lulls.
- **Floor:** below it, a bot with something to say starts at once without waiting for the gap. While a channel is under
  its target, bots there also try to talk more often, up to four times as often, resting two minutes between lines
  instead of four. A thin crowd can still keep a capital or a guild alive.
- **Ceiling:** every bot line in the channel counts, including answers, news and bursts. Above the ceiling nothing new
  starts and bursts drop their remaining lines.

A bot that wants to talk picks the channel it can be heard in that runs furthest under its target.

### Scaling

- **Bots online:** the band is multiplied by `(bots / design_bots_per_realm) ^ population_exponent`. With the default
  0.75, 300 bots a realm talk at 41% of a full server and 3,000 at 228%. There is no cap. Beta 1.1.1 stopped growing at
  about 390 bots a realm. A guild scales with its own members online, as `(online / members) ^ members_exponent`.
- **The hour:** `hours` holds 24 factors by the PC's clock, with prime time (20:00–21:00) at 1. With
  `bot_schedule_mode clock` the night already has fewer bots online, so `hours_clock_schedule`, a flatter curve, is used
  instead.
- **`bot_voices_chat_rate`** (the existing setting) still multiplies every target. 0 means bots only answer.

### Where the starting values come from

Every number is an estimate **[E]**. They are calibrated against the 2003 chat-volume research of 2026-11-03 (research
notes, *Peak server chat volume 2003*, `chat_volume.md`, section 6 and its texture rules). No counted 2003 figure exists,
so that research brackets the rates from server populations. The defaults sit toward the likely-to-high end of each
range, because the owner's first rule is that the server feels alive and busy at prime time. They are never below the
Beta 1.1.1 volume at 1,000 bots a realm.

| Research, prime time (lines a minute, likely / high) | Default target / ceiling |
|---|---|
| Capital /broadcast, 1.5 / 4 | `capital` 2.5 / 4 |
| Bind town /broadcast, 0.6 / 2 | `town` 0.8 / 2 |
| Busy camp area /say, 0.8 / 3 | `say` 1.0 / 3 |
| Public LFG or trade chat group, 1 / 3 | `lfg` 1.0 / 3, `trade` 0.8 / 2.5 |
| Guild, 50 online, 2.5 / 5; 150 online, 6 / 12 | `guild` 3.0 / 6 at 50 online, growing as members ^ 0.8 |
| PvE camp group, 1.5 / 4 (everyone, calls included) | `group` 0.6 / 2 (bot banter only) |
| A player in Camelot sees 3–6 lines a minute in all | about 4 from /broadcast, /say, guild and group, plus the realm channels |

Texture rules taken from the research: sellers' macros repeat every 3–8 minutes for 15–40 minutes; an alarm or a big
moment sets off a burst that fades with a half-life of about two minutes; a few talkative bots carry much of the
chat. The last one already holds: about one bot in seven talks two and a half times as often as the rest.

The research also found that `/advice` was probably not a public channel in 2002–2003. Bots never use it.

### Bursts

A keep taken or lost, a relic moved, a siege held or broken, a realm raid won or wiped, Darkness Falls changing hands, a
server first, and a bot killed by or paying back its nemesis each start a burst of reactions in /faction:

- 3–6 lines (`burst_min`, `burst_max`), fewer on a thin server;
- most of them in the first minute, all within about 90 seconds (`burst_spread_s`);
- each from a different bot, with no near-copies;
- none once /faction is at its ceiling.

A keep lost, a relic moved or Darkness Falls also reaches the capital's /broadcast, wherever a player can hear it.

### Filler rules

- **Musings:** lines that narrate the game instead of playing it appear only now and then as /faction openers. They
  never appear as replies, in /say or in /broadcast.
- **The Realm Exchange:** bots name it only to someone asking where to buy or sell, and in a seller's own listing.
- **Generic agreement** ("good question", "happy to help", "sounds familiar"): at most once per channel per hour.
- **Near-copies:** a line that differs from one the channel carried in the last half hour only by an interjection or a
  number isn't said again. Short reactions ("lol", "gz", "omw") are exempt.

## The reply budget

Every reply to a player is chosen by a route: the bot's own state, the world, the knowledge lookup, the situation (a
goodnight, "stuck", "inc", a sale), the conversation so far, a reply set for that kind of question, or a remark on the
line's topic. Each route has a fit:

| Fit | Routes |
|---|---|
| 4, known | the bot's own state, the world, the knowledge lookup, the situation, a join answer |
| 3, specific | the conversation thread, a reply set for that question or statement, news |
| 2, fits | a social reply to a social line (hello, thanks, gz, gn, brb, a laugh), roleplay in kind |
| 1, weak | a remark on the topic, a stock "not sure", a brush-off ("ask around maybe", "beats me") |

A reply needs `reply.min_fit` (2). Below it, the bot stays quiet. A tell to it, or a line that names it, gets a short
acknowledgement ("?", "ya?", "hm?", or "sec" when it's busy) instead. The first blind evaluation set this rule: silence
scored better than the average reply, and brush-offs to answerable questions were the most common fault.

**Who answers, and when:**
- **One primary answer.** A hello, a ding, a goodbye or a call may draw up to `reply.pileon_cap` (2) more short ones. A
  joke, a mishap or a remark now and then draws a "lol", "+1", "this" or "^".
- **Questions:** a question, or a line said to one bot by name, gets exactly one answer.
- **No interruptions:** no unprompted line starts in a channel until the answers are out.
- **Timing:** a reply comes after reading the line, a moment's thought, and typing at the bot's own speed. Speeds run
  from `wpm_min` to `wpm_max` (25–60 words a minute). So "?" arrives in about two seconds and a full sentence in 8–15.

## Seeing it in game: /lswhy

`/lswhy` (every player) shows why your target, or the last gamebot that spoke to or near you, said what it said. Each
entry gives the channel, what set it off, the rule it used, its score and the reason. `/lswhy <name>` shows one bot,
`/lswhy last [n]` shows the realm's last decisions (the quiet ones too), and `/lswhy budget` shows each channel's floor,
target, ceiling and current rate around you.

## Tuning

- **More or less chat everywhere:** `bot_voices_chat_rate`, or every `target` in the file.
- **A channel too busy or too dead:** that channel's `target`. Raise its `floor` to keep it alive with few bots; lower
  its `ceiling` to cap a crowd.
- **Bigger or smaller servers:** `population_exponent`. Use 1 for linear growth with bots online, and lower values to
  flatten it.
- **The day:** `hours`, 24 factors from midnight.
- **Replies:** `reply.min_fit` 1 lets weak replies through (the Beta 1.1.1 behaviour); 3 keeps only specific ones.
- **After a change:** run `UT_ChatBudgetSimulation` with `TALK_BUDGET_OUT=<file>` for an evening hour at 300 and
  1,000 bots a realm. Check a played evening with `/lswhy budget`.
