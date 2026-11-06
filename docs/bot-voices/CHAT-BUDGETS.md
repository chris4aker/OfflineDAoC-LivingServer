# The chat budgets: how busy the world is, and when a bot answers you

Living Server Beta 1.1.2, with the reply rate budget of 1.2. The goal (owner, 2026-11-03): chat that feels like about 3,000 players across the three
realms on a busy peak evening, realistic and responsive, and busier than before. Bot chat has two separate
budgets:

- **The ambient budget** sets how much the world says on its own: openers, answers between bots, news, bursts, town
  broadcasts, guild and group talk. It has a floor and a ceiling per channel, and it grows with the number of bots
  online, with no cap.
- **The reply budget** sets when a bot answers *you*, and (1.2) how many replies a channel carries. A reply has to fit
  what you said. A weak reply becomes something short that does fit ("ty", "lol", "dunno", "no heals on a thane sry"),
  or silence for spam, scams and lines said to someone else. Replies are counted apart from the ambient lines, against a
  reply target and a reply ceiling of their own. **A tell to a bot, and a line that names a bot, are always answered.**

Silence is only ever the answer to a line no bot should answer, or to a weak reply nothing short can fix. The ambient world is never held back to avoid a bad line:
it picks another line or another channel.

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
- **Ceiling:** above it nothing new starts. It counts the channel's ambient lines: openers, news, stories, debates, the
  gold spammer's ads, every scene. From 1.2 the answers count against the channel's reply budget instead (below), in
  /faction and /say; elsewhere (lfg, trade, broadcast, guild, group) every line still counts here.
- **How the rate is measured (1.2):** over a window long enough to hold about four lines at the target, from 2 to 20
  minutes. 1.1.2 always used two minutes, which can't see a slow channel's floor: with 10 bots a realm the /faction floor
  is 0.18 a minute, and one line in two minutes already reads as 0.5. So "under the floor" fired after every quiet two
  minutes, a bot spoke at once, and its answers came on top. The automated playtester (PR #72) measured /faction at 1.2
  to 1.4 lines a minute there, about four times the 0.27 to 0.36 its own budget asked for.

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

**The research gives the shape; the level is set busier on purpose.** The owner decided on 2026-11-03: "I don't want
the chat to be quiet, even if something in our research says the old servers were quiet."

- **The shape:** the 2003 chat-volume research of 2026-11-03 (research notes, *Peak server chat volume 2003*,
  `chat_volume.md`) gives:
  - which channels carry what (capital /broadcast is mostly selling, groups and recruiting);
  - how talk moves with the hour and the place;
  - the texture: sellers' macros, bursts after big moments, a few talkative regulars.
- **The level:** the defaults are a **busy peak server**, deliberately above the research's own high estimates. Every
  number is an estimate to tune by playing.

| Channel | Floor / target / ceiling at 1,000 bots, prime time | Replies: target / ceiling (1.2) | The research's 2003 estimate (likely / high) |
|---|---|---|---|
| `faction` | 2.0 / 4.0 / 10 | 4.0 / 8.0 | none; this server's own channel [O] |
| `lfg` | 0.6 / 1.8 / 4 (1.1.2: target 1.6) | counted with the ambient lines | public LFG chat group, 1 / 3 |
| `trade` | 0.5 / 1.6 / 3.5 (1.1.2: target 1.3) | counted with the ambient lines | public trade chat group, 1 / 3 |
| `say` | 1.0 / 2.0 / 5 (1.1.2: floor 0.5) | 2.0 / 4.0 | busy camp area, 0.8 / 3 |
| `capital` | 1.5 / 4.0 / 7 | counted with the ambient lines | capital /broadcast, 1.5 / 4 |
| `town` | 0.4 / 1.5 / 3.5 | counted with the ambient lines | bind town /broadcast, 0.6 / 2 |
| `guild` (50 online) | 1.5 / 4.0 / 8 (1.1.2: floor 0.5) | counted with the ambient lines | 2.5 / 5 at 50 online, 6 / 12 at 150 |
| `group` | 0.3 / 1.0 / 3 (bot banter; fight calls come on top) | counted with the ambient lines | PvE camp group, 1.5 / 4 counting calls |

**The 1.2 re-balance** (owner's rule: the amount of chat must not go down; re-balance, don't cut). Counting replies apart
gave /faction's openers back the turns the answers had taken from them, and gave the answers a band of their own. The
turns that 1.1.2's busy /faction had pushed into /lfg and /trade went back to /faction, so their targets were raised to
what 1.1.2 actually delivered. 1.1.2's two-minute window had kept a slow guild or /say near the player alive by accident
(every quiet two minutes read as "under the floor"); their floors were raised instead, so they stay alive on purpose.
`UT_ChatBudgetSimulation`, five evening hours a side at `bot_voices_chat_rate` 1:

| Bots a realm | 1.1.2: /faction, all channels | 1.2: /faction (of which replies), all channels |
|---|---|---|
| 300 | 2.46, 6.02 | 2.63 (1.18), 6.26 |
| 1,000 | 4.86, 12.56 | 5.07 (2.20), 13.22 |

A small realm is where the change shows (rate 2, the default). With 10 bots a realm /faction goes from 0.72 to 0.59 a
minute in the model. On the real server, the playtester's faction-rate scenario measured 1.20 and 1.33 lines a minute
before and 0.87 and 0.97 after (two half-hour runs each, against an opener target of about 0.3 and a reply target of the
same). The model has no gold spammer and no debates; on the server they make most of what is left over the budget,
because they came at the same pace on any server. They count against the openers' budget (bots start fewer lines while
one runs).

**Scenes on a small roster (1.2, owner 2026-11-05).** Debates and the gold spammer's waves wait longer below 300 bots a
realm: their gap is divided by `SceneScale` = the square root of (bots online / 300), between 0.1 and 1, read when a
scene comes due (a server start has few bots on yet), and a wave's ads are scaled the same way (two at least). 10 bots a
realm hear them about a fifth as often; 300, 900 and 1,000 exactly as before.

- **Nights:** calmer than prime time, never dead. The lowest hour factor is 0.45, or 0.7 with the clock schedule.
- **Small rosters:** `population_exponent` is 0.65, so a 300-bot realm still talks at 46% of a full server.
- **How much they talk:** in the launcher's Living Server tab, *Personalities and voices*, **How much they talk**
  (`bot_voices_chat_rate`, 0–3). **2 is the default** from 1.1.2 (owner 2026-11-04, after the playtest): at 900 bots a
  realm, about 10 am, Albion heard 6.4 lines a minute with 1 and about 15 with 2.5. 1 is calmer, 3 very busy. An install
  that never changed it moves from 1 to 2 once; one where it was set in the launcher keeps its value.

The research also found that `/advice` was probably not a public channel in 2002–2003. Bots never use it.

### Bursts

A keep taken or lost, a relic moved, a siege held or broken, a realm raid won or wiped, Darkness Falls changing hands, a
server first, and a bot killed by or paying back its nemesis each start a burst of reactions in /faction:

- 3–6 lines (`burst_min`, `burst_max`), fewer on a thin server;
- most of them in the first minute, all within about 90 seconds (`burst_spread_s`);
- each from a different bot, with no near-copies;
- they are replies to the news (1.2): said up to /faction's reply ceiling, not stopped at its reply target, since a big
  moment may be loud.

A keep lost, a relic moved or Darkness Falls also reaches the capital's /broadcast, wherever a player can hear it.

### What bots say unprompted

Most unprompted lines (about nine in ten) are short and about the game in front of the bot, built from what is true right
now: its level, class, zone, camp and the best item in its bags, and the realm's keeps and relics. "lfg 38 zerk",
"wts 97% chain hauberk /send", "anyone up for vendo?", "inc beno", "who has rena?", "ding 27", "brb food". A keep call
names a keep that really is under attack; "who has X?" asks about a keep the realm really lost; a ding comes only right
after a level. Lines are lowercase, rarely punctuated, with the period's abbreviations ("sm", "zerk", "pst", "x me").
Now and then another bot answers one with the truth: the keep's owner, "omw", "gz". A realm doesn't hear the same line
twice in an evening. The rest come from the written lines, kept to the short and plain ones; jokes, tips, life stories and
long remarks are rare and only in /faction.

Guild chat works the same way: "anyone want to group, 31 in camelot hills", "who's on for df tonight", "gz bran" when a
guildmate really dinged, "need a smith, anyone 500+?", "selling my old ring if anyone wants", plans for the guild's own
keep, "brb", "lag?", "night all". Written guild lines are kept to short, plain ones, about one in ten. Short answers vary
and speak for whoever answers ("me, thane 27"), one item is advertised at most once an hour per realm, and nobody trails
off with "...".

Each realm also has its regulars: about one bot in eighty online (5 to 15), mostly the sociable ones, who carry much of
/faction and guild chat for an evening. They talk more often, answer each other, and follow up their own lines ("ty",
"still lf healer", "nm found one"); most other bots speak there only now and then. A line's idea (a camp, a dungeon at a
level, a keep) rests for 30 to 60 minutes once said, and its wording family for 3 to 6, so the realm doesn't hear one
thought reworded. Written lines are at most one per realm in 20 minutes, lore is never recited unasked, and nobody answers a
remark with "i know right". Gaps between lines are uneven: quick runs, ordinary stretches and the odd lull.

Questions come in many shapes (gear, quests, travel, crafting, the frontier, trainers, named monsters, epic steps), and one
shape ("good xp for...", "any healers on?", "still lf...") is heard at most four times an hour per realm, ten minutes apart
at least. A line that asks for an answer usually gets one, from a regular or a bot near the asker's level; about one thread
in five runs on for a few lines, with a follow-up question, its answer, a thanks, and sometimes a third bot. A ding to 50, a
death now and then, and a bot group's wipe become short stories the realm riffs on. A guild hears at most one life line
(weather, school, work, pets) an evening.

Every bot answer takes at least about 2 seconds plus a third of a second a word, and no new line starts within 3 seconds of
the last one in a channel. Every sentence shape, from "any grps for 38 thane?" to "is cleric fun after 40?", rests 10-15
minutes and is heard at most four times an hour per realm; the terse "lfg 38 thane" posts may repeat, as on any server. An
answer gives what was asked: a place for "where", a price for "how much", a time for "when", a name or class for "who".

### Filler rules

- **Musings:** lines that narrate the game instead of playing it appear only now and then as /faction openers. They
  never appear as replies, in /say or in /broadcast.
- **The Realm Exchange:** bots name it only to someone asking where to buy or sell, and in a seller's own listing.
- **Generic agreement** ("good question", "happy to help", "sounds familiar", "very true"): never said, as an opener
  or a reply.
- **Quips, jokes, tips and life filler:** rare, and only as /faction openers.
- **Template families:** a line written with several wordings counts as one; once a bot says one wording, the realm
  doesn't hear its siblings for six hours.
- **Typing slips:** rarer, and mostly a dropped letter in a long word, never the first letter.
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

A reply needs `reply.min_fit` (2). Below it, the bot falls back to something short that still fits. The first blind
evaluation set the bar: silence scored better than the average reply, and brush-offs to answerable questions were the
most common fault. The second showed bots had become too quiet, so the fallbacks are:

- **A tell or a line naming the bot:** a short reply to what it says. A compliment gets "ty", an offer to buy "not
  selling sry", an offer of help "nah im good ty", a call for heals or a rez the truth for the bot's class ("thane, no
  heals sry"), a question "no clue sry", and an answer to the bot's own question "cool". Only a line with nothing in it
  ("hi", "u there?") still gets "?" or "ya?".
- **Group, guild, /say and /faction:** a short laugh, "ugh" or "same" to banter, a joke or a bad-luck story, at a rate
  that is highest in a group and lowest in /faction. A question nobody can answer gets one "dunno" in a group or guild,
  less often in /say and /faction.
- **Silence** is kept for spam and scams (links, real-money prices, "your account has been flagged", fake GMs, account
  sales or purchases, doubling offers, free plat, password requests), for a line said to someone else by name (in a
  group, a light laugh now and then is fine), and for lines nobody could read. A scam is never answered, not even with
  "oh ok".
- **Questions about the bot** (its level, class, spec, weapon, age) are answered from what is true of it, also as a bare
  "spec?" by tell. An offer to sell it something gets "how much?" or a no.
- **Reactions fit the line:** a call gets "where?" or "omw", never "lol same"; "need heal!!" gets "cant heal sry" or
  "use a pot" from a bot with no heals; a joke a laugh, bad luck an "ugh".
- **Honest about itself:** a bot outside a group's or raid's level band ("45+") says "too low lol" instead of joining,
  and never takes thanks for heals or a rez it can't give.
- **/say to one bot:** a /say line is for one bot when it names it, when the player targets it, when it answers that
  bot's own line within 20 seconds, or when the player stands right in front of it with nobody else that close. That bot
  answers, once.

Roleplay gets an answer in kind: a challenge, a question, a greeting, a mourning (a bow), a toast (a toast back), or
otherwise an emote such as *nods*.

Bots also answer `/advice` (one knowledgeable bot of your realm, when it knows the answer), and questions in
`/broadcast` and chat groups, by tell.

**How many replies a channel carries (1.2).** In /faction and /say, replies are counted apart from the ambient lines,
against the channel's `reply_target` and `reply_ceiling` (scaled like its ambient band, and by `bot_voices_chat_rate` but
never below 1, so "0 = bots only answer" still answers):

| Who the reply answers | Rule | Reason code when held |
|---|---|---|
| A tell to the bot, a line that names it, a /say to the bot in front of you, a follow-up to its own line | always answered | never held (`exempt-tell`, `exempt-named`, `exempt-addressed`, `exempt-thread`) |
| The first answer to your line said to nobody in particular | said below the reply ceiling | `hold-reply-ceiling` |
| A big moment's burst of reactions | said below the reply ceiling | `hold-reply-ceiling` |
| Bots answering bots (an opener's answers and thread, a story's riffs, reactions to the gold spammer), and pile-ons after the first answer to you | said below the reply target; between target and ceiling with a chance that falls from 1 to 0 | `hold-reply-target`, `hold-reply-ceiling` |

So the room between the reply target and the ceiling is mostly left for your lines. A thread is decided once, when it
starts: it is said whole or not at all, and a story over the budget is told by its teller alone. Replies are measured over
a window that holds about eight of them at the target (they come in clumps).

**Saying nothing untrue (1.2).** A reply that says something untrue of the bot about to say it ("im a cleric" from a thane,
"38 zerk here" from a 41) is dropped whatever chose it (`contradicts-self`), and so is a group or trade ad pasted into a
conversation about neither ("need a healer still?" to "gn all"; the blind round's sixth complaint). The bot then says
something short that fits, or nothing. Bots also answer where they are from their own state: "what zone r u in?",
"are you in camelot hills?" (`self-knowledge`).

**Who answers, and when:**
- **One primary answer.** A hello, a ding, a goodbye or a call may draw up to `reply.pileon_cap` (2) more short ones. A
  joke, a mishap or a remark now and then draws a "lol", "+1", "this" or "^".
- **Questions:** a question, or a line said to one bot by name, gets exactly one answer.
- **No interruptions:** no unprompted line starts in a channel until the answers are out.
- **Timing:** a reply comes after reading the line, a moment's thought, and typing at the bot's own speed. Speeds run
  from `wpm_min` to `wpm_max` (25–60 words a minute). So "?" arrives in about two seconds and a full sentence in 8–15.

## Seeing it in game: /lswhy

`/lswhy` (every player) shows why your target, or the last gamebot that spoke to or near you, said what it said. Each
entry gives the channel, the outcome and its **reason code** in brackets, what set it off, the rule it used, its score and
the reason. `/lswhy <name>` shows one bot, `/lswhy last [n]` shows the realm's last decisions (the silences too),
`/lswhy me` (1.2) why bots answered your own lines or didn't, `/lswhy held [n]` (1.2) the last replies the reply budget
held back, and `/lswhy budget` each channel's floor, target, ceiling and current rate around you, with /faction's and
/say's replies apart (their target, ceiling, and how many were held or let through). A GM sees every realm.

**Reason codes** (`AutonomousChatWhy.Codes`):
- **Said:** `exempt-tell`, `exempt-named`, `exempt-addressed`, `exempt-thread` (always answered); `reply-in-budget`,
  `reply-over-target` (the reply budget let it through); `pileon`; `self-knowledge` (from the bot's own state);
  `short-below-minimum`, `rescue-below-minimum` (under the minimum fit, something short that fits); `ambient`, `sent`.
- **Held or silent:** `hold-reply-target`, `hold-reply-ceiling` (the reply budget); `below-minimum` (no reply fits well
  enough); `contradicts-self`; `spam`; `other-addressee` (said to someone else); `ack-line` ("ok", "k"); `undirected`
  ("wb Sigrun" to nobody a bot knows); `no-false-credit` (thanks for heals it can't give); `no-fit`; `held`.

## Tuning

- **More or less chat everywhere:** `bot_voices_chat_rate` (the launcher's **How much they talk**), or every `target`
  in the file.
- **A channel too busy or too dead:** that channel's `target`. Raise its `floor` to keep it alive with few bots; lower
  its `ceiling` to cap a crowd.
- **Bigger or smaller servers:** `population_exponent`. 1 grows linearly with bots online; lower values keep a small
  roster livelier.
- **The day:** `hours`, 24 factors from midnight.
- **Replies:** `reply.min_fit` 1 lets weak replies through (the Beta 1.1.1 behaviour); 3 keeps only specific ones.
- **How many replies (1.2):** a channel's `reply_target` and `reply_ceiling`. Leave them out (as a 1.1.2 copy of the file
  does) and that channel counts its replies with its ambient lines, as before.
- **After a change:** run `UT_ChatBudgetSimulation` with `TALK_BUDGET_OUT=<file>` for an evening hour at 300 and
  1,000 bots a realm (1.1.1, 1.1.2 and 1.2 side by side), and `TALK_BUDGET_SMALL=<file>` for 10 and 30. Check a played
  evening with `/lswhy budget`. The automated playtester's faction-rate scenario predicts the ambient target only; the
  realm hears up to the ambient target plus the reply target.
