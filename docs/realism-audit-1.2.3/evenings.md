# Realism audit 1.2.3 — evenings: what one player reads over two hours

Agent: evenings. Everything here is **tests only** (a simulation in CI and code reading). Nothing was seen in a played game.
The code audited is `fix/1.2.3-faction-polish`, now main `5346e5f7` (#469 merged). No game behaviour was changed.

- **Draft PR (do not merge):** #472, branch `audit/1.2.3-evenings`.
- **The sim:** `source/server/Tests/UnitTests/UT_EveningTranscripts.cs`. 18 seeded two-hour evenings, 36 hours, 36,249 rows.
- **The reader:** `tools/bot-voices/eval/evening_read.py` (metrics, a transcript as the client shows it, flagged moments).
- **Runs:** R1 = commit `02876fc5` (CI run 45291240472): the model as `UT_ChatBudgetSimulation` has it. R2 = commit `56e5dd9d`
  (CI run 45343708976): the same plus five live guards the model lacked (below). Numbers are R2 unless marked R1.
- **Rows kept at:** `/tmp/audit2/evenings_work/rows2.tsv` (R2), `rows_run1.tsv` (R1), `stats.py`.

## How the sim is built (so the numbers can be judged)

- **Roster:** 800 gamebots a realm, chat rate 2, 8 pm (the owner's PC figures in `UT_FactionPace`).
- **Scenarios:** level 5 solo in the starter hills; level 24 in a bot-led party of five in a dungeon, in a guild; level 50 solo in
  the frontier, in a guild. Albion: 2 seeds x (speaks, silent). Midgard and Hibernia: 1 seed, speaks.
- **Realm stream (/faction, /lfg, /trade, /say nearby, /guild):** the budget simulation's loop with the real corpus picker,
  `AutonomousChatFieldTalk` lines, threads and stories, real debate scenes, real market replies (`AutonomousChatMarket`).
- **R2 adds:** `StyleGenerated`, `NamedWhenApart`, the "hog" hold on openers, three threads at once, fresh reactions
  (`Shorts`), the group stance (`AutonomousChatStance`).
- **Party:** real `AutonomousBotCombatCalls.GroupEvent/SelfEvent/Gate`, the lead lines, the group banter rules and answer chain,
  over a scripted list of fights, one groupmate death, one groupmate ding, the player's death and ding, a leave and a join.
- **Player:** 20-21 scripted lines an evening through `UT_ChatQuestionBench`'s path (`AutonomousChatReplyPath`), the bots that
  hear it taken from the sim. A silent twin of each evening has the same seed.
- **It is a model.** Bot facts are synthetic. Section "What the sim cannot see" lists what that hides.

## The 10 findings, worst first

### 1. The party talks for half an hour, then never again
- **Player sees:** a chatty group for 30-38 minutes, then only fight calls for the rest of the night. Next session the same lines.
- **Measured:** 6 of 6 party evenings said exactly **43** banter lines and the last came at minute 29-39. Minutes 60-120: 0.
- **Where:** `AutonomousBotGroupChatter.TryGroupLine` (lines 81-128) draws kind `group` with no relax and no fallback.
  `data/chat/group_chatter.lines` + `group_talk.lines` hold 101 `group` lines; 43 fit a bot-led party with the player. Each rests
  3 days (`AutonomousBotVoiceCorpus.RecentLines.Window`, line 978), 6 hours when relaxed, realm-wide, in memory. The group
  budget at rate 2 asks for about 2 lines a minute (`chat-budget.json` "group" target 1.0).
- **Class:** a small pool behind a long no-repeat window. The same shape can hide in `combat` topics (20 lines each), `grats`
  (12) and the lead lines (3-9 each).
- **Fix:** party banter from the party's own state (a field-talk branch for `/group`: the last kill, who is low, the next room,
  the drop that just fell), the written pool as seasoning with a 40-minute rest. And a pace of one line every 2-4 minutes.
- **Size:** M. **Risk:** low. Only `/group` banter changes; calls and lead lines stay.

### 2. /say beside the player is four words said to nobody
- **Player sees:** `Rosstan says, "sry ks"` every few minutes, with no kill stolen. `"rdy"`, `"brb"`, `"1 sec"` from strangers.
- **Measured:** of 917 bot /say lines in 36 h (R1), **250 are "sry ks"** (27%), 115 "rdy", 97 "brb", 94 "1 sec": 61% in all.
  "sry ks" comes 7 times an hour. A level 50 in the frontier hears "this camp taken?" and "anyone camping this?".
- **Where:** `AutonomousChatFieldTalk.Say` (line 1169). The solo list is lines 1209-1216: `new V("sry ks", null)` has no family, so
  it never rests. The grouped list is line 1204 (`"brb", "rdy", "1 sec"`). `AutonomousChatFiller.RecentTexts.TryAdd` line 504 lets
  any line of two words and ten characters repeat ("reaction"). `Say` has a town branch, a group branch and a camp branch: no
  frontier, no dungeon, no road.
- **Class:** situational lines said without the situation. Short lines exempt from every repeat guard.
- **Fix:** say "sry ks" only after a real shared-target kill; "rdy" only in a party about to pull; give the short lines a family
  and a 10-minute rest per speaker; add a frontier branch (scouting, "any inc?") and a dungeon branch.
- **Size:** S-M. **Risk:** low. /say gets quieter; its floor (1.0 a minute) may need real lines to fill it.

### 3. A direct line from the player in the party gets nothing, or "dunno"
- **Player sees:** "can we invite my friend Tobias?" → silence. "ouch, who died" → silence. "this xp is slow tonight" → silence.
  A joke → `Quilys: "dunno"`. "whats the plan after this" → "leveling probably" (the leader knows the plan).
- **Measured:** 7 of 20 scripted /group lines unanswered in every party evening (6 of 6); the joke drew "dunno" 6 of 6.
  All player lines: 2.4-3.3 unanswered an hour.
- **Where:** `AutonomousBotGroupChatter.OnPlayerGroupChat` (215-320) → `ReplyChoiceToPlayer` → `AutonomousChatReplyBudget.Rescue`
  (line 342; `RescuesWithDunno` line 327 allows "dunno" in a group). No route for a request to the leader (invite, wait, move,
  stop), none for "who died", none for a joke.
- **Class:** the reply path knows questions of fact. Statements, jokes, complaints and requests fall through.
- **Fix:** in a bot-led party the leader owns requests ("sure, whats their name", "after this pull"); a joke or complaint gets a
  short social line from a groupmate (laugh, agree), never "dunno"; "who died" reads the group's state.
- **Size:** M. **Risk:** medium (the bench ratchet must not move; add party lines to the bench first).
- **Sim limit:** "where is that" also went unanswered here, but the live fix (#467) needs the group's conversation memory.

### 4. Party replies are stock agreement that does not fit the line
- **Player sees:** "endurance is low, give me a moment" → "heh, very true". "this camp works for me" → "same thing happened to me".
  "want to push one room further?" → "i liked it". "anyone want to stay in touch after this?" → "maybe later".
- **Measured (hand count, R1 Albion seed 1101):** 26 banter replies in 38 minutes, 12 do not fit what was said.
- **Where:** `AutonomousBotGroupChatter.Answer` (186-213): any `reply` line whose `answers` set matches the opener's `invites`
  ("chat", "ask_opinion"), checked only by `ShapedFits`. Data: `replies_pools.lines`, `replies_pools_more.lines`, `replies_short_chat.lines`.
- **Class:** generic reply pools matched by invitation type, not by content. Same mechanism as /faction's written reactions.
- **Fix:** pair replies with their openers in the data (an opener carries its own two or three answers, as field talk does), and
  drop the open "chat" pool for /group.
- **Size:** M (data). **Risk:** low.

### 5. Ten bots say almost half of /faction, each with no thread of its own
- **Player sees:** the same ten names all night, each one asking unrelated things: Lorcan needs a rez, is "otw" to a keep at 21,
  is lfg for a dungeon, wants a ring, all in 12 minutes. Valo asks "xp spot for 18?", thanks, and asks it again 7 minutes later.
- **Measured:** top-10 share of /faction lines **42-48%** in all 18 evenings; the busiest bot says 40-60 lines in two hours (one
  every 2-3 minutes); the other 270 speakers say 1.5 each. A bot has 3 of 10 lines in a /faction window about 10 times an hour,
  after #469's hold (it holds openers only; answers and follow-ups pass).
- **Where:** `AutonomousChatRegulars` (lines 22-34: rest 35 s, chance 0.7; others 8%). Every line a regular says is a fresh draw
  from `AutonomousChatFieldTalk.Line` with no memory of what that bot said (only `AutonomousChatStance` for lfg/lfm).
- **Class:** context across lines, per speaker.
- **Fix:** a small per-bot record for the evening (asked X, was told Y, said it was going to Z): no second ask of an answered
  question, a follow-up instead ("fogou was good, ty"); cap a regular at one opener in 4-5 minutes; let the archetypes
  (`ArchetypeOf`) pick the kind of line.
- **Size:** M-L. **Risk:** medium (the pace floor leans on regulars; the cap needs others to fill in).
- **Sim limit:** bot facts are synthetic, so some jumps (dead, then at a keep) are the sim's. The share and the repeat asks are not.

### 6. /trade and /lfg are three stock exchanges on a loop
- **Player sees:** `"wtb X"` → `"how much u paying?"` → `"80g tops"`. `"wts X /send"` → `"price?"` → `"offers, pst"`.
  `"lfg 22 clr"` → `"me too, duo?"` / `"where at?"` / `"what spec?"`.
- **Measured:** "how much u paying?" **24-26 times an hour** in /trade (one every 2.4 minutes); the same line again within the hour
  37 times an hour in /trade and 18 in /lfg; "x", "same lol", "/send me", "offers, pst" again within 10 minutes 28 times an hour.
- **Where:** `AutonomousChatMarket.ReplyTo` (188-241): two to four replies per post kind; a wanted post draws "how much u paying?"
  from a bot that does not carry the item (line 233-236). `PosterAnswer` (290-345). Reply shares 0.3-0.45 (line 49).
- **Class:** tiny answer sets at high volume; public haggling that 2002 players did by /send.
- **Fix:** reply publicly far less (0.1), only when the replier has the item or the seat; more wordings built from the item, level
  and price; most answers go silent ("pst" then nothing).
- **Size:** S. **Risk:** low (channel volume falls; the budget floors refill with posts).

### 7. Nobody reacts when a groupmate dies; nobody says hello or bye
- **Player sees:** `Sigys was just killed`. Silence. Then the leader: "anyone want to stay in touch after this?" and two minutes
  later "best group i have had all week honestly". A groupmate leaves and another joins: no word.
- **Measured:** groupmate death unanswered in 6 of 6 party evenings unless the healer's rez call fired; leave and join 6 of 6
  (the sim has no hook because the code has none for a bot member). The player's own death and ding were answered in most evenings.
- **Where:** `AutonomousBotCombatCalls.GroupEvent` (127-178) has pull, add, wipe, kill, rest; a single death is no event. The
  `combat` topics have `player_down` but no groupmate-down. `OnJoinedPlayerGroup` (170) runs only when a bot joins a
  player-led group (`BotGroupInvite.cs:151`). Banter (`TryGroupLine`) does not look at recent deaths.
- **Class:** bots ignoring what a player would react to.
- **Fix:** a `mate_down` call ("rip sig", "rez inc"), a 3-minute hush on cheerful banter after any death, a hello from a recruit
  and a "wb"/"cya" from one groupmate.
- **Size:** S-M. **Risk:** low.

### 8. Fight calls and directions end on a laugh
- **Player sees:** `Perys: "ok pulling, tank it lol"`, `"incoming, get ready haha"`, `"pulling :P"`, `"moving to Keltoi Fogou :P"`,
  `"Humberton Castle is in Black Mtns. South lol"`. And the tank leader tells others to "tank it" and "hold aggro".
- **Measured:** 37 of 342 fight calls end on a laugh or smiley; 13 of 24 lead lines do. The laughing leader does it all night.
- **Where:** `AutonomousBotVoiceCorpus.TakesEmote` (line 760) keeps emotes off functional and serious lines by word list; #469 added
  questions. Calls and lead lines are not in it. `bot_led_groups.lines` `lead_pull` lines are not class-aware.
- **Class:** style habits stuck onto functional lines (the "camp for 4? heh" class, other kinds).
- **Fix:** no emote on kinds `combat`, `group` with a `lead_*` topic, and `knowledge` answers; `lead_pull` lines that say "tank it"
  need `!is_tank`.
- **Size:** S. **Risk:** very low.

### 9. Since #469, a third of /faction answers start with the asker's name
- **Player sees:** `"Fenwyn, x2"`, `"Fenwyn, 11 merc"`, `"Fenwyn, sec, me"` in five seconds. `"Iselund, grats"`. `"Dagcan, nope mids"`.
  13 name-first answers in one 18-minute stretch.
- **Measured:** 1,602 of 4,997 answers by another bot in /faction are name-first (**32%**, 44 an hour). At 16 lines a minute across
  /faction, /lfg and /trade there are nearly always two lines between a question and its answer.
- **Where:** `AutonomousChatFieldTalk.NamedWhenApart` (line 1328, `linesBetween < 2`), called from
  `AutonomousBotChatCoordinator.PlayThread` with `RealmLinesSince` (line 864), which counts all three channels.
- **Class:** a fix tuned on one screenshot that becomes a tic at the owner's volume.
- **Fix:** name the asker only when another *question* or another thread's line in the *same channel* came between, at most once
  per thread, and never on "grats", "x", "me".
- **Size:** S. **Risk:** low. Short unnamed answers still land right under a /trade or /lfg post 24 times an hour (screenshot 6's
  class), so some naming must stay.

### 10. The player is a spectator
- **Player sees (never speaking):** 950-1,050 lines an hour, of which **2-4 an hour** are to them (a duo tell every half hour; in a
  party the leader's five lines). About 250-280 bot-to-bot threads an hour, none of which turn to the player.
- **Player sees (speaking):** jokes get silence in /faction (2 of 2 in every evening), a typo ("wher do i buy arows") silence,
  "ty" silence, "where do i spend realm points" silence. "who has darkness falls right now?" → "no, the hibs have it".
  "how many relics do we have" → "yep, both of ours are home...".
- **Where:** `AutonomousLivingServer.Tells` (gap 25-45 minutes, line 21-22); `AutonomousChatReplyPath.Plan` (banter "draws no
  answer here"); `AutonomousChatQuestions.AnswerWorld` wordings open with yes/no for who/how-many questions.
- **Class:** conversations among bots that never include the player; reply shapes that do not match the question word.
- **Fix:** once or twice an hour a bot near the player's level addresses them in /say or /faction from real state ("Wynn u still
  in the hills? skeletons by the bridge are free"); a joke gets one laugh in three; typo-tolerant matching for buy/where
  questions; answers start with the fact when the question starts with who/how many.
- **Size:** M. **Risk:** medium (unprompted lines to the player must stay rare and true).

## Rates per hour per scenario (R2, 36 h)

| | L5 hills | L24 dungeon party | L50 frontier |
|---|---:|---:|---:|
| Lines a minute, all channels | 15.2-15.5 | 17.5-17.8 | 16.6-16.8 |
| /faction, /lfg, /trade a minute | 7.3 / 3.9 / 3.6 | 7.6 / 4.0 / 3.5 | 7.6 / 3.9 / 3.5 |
| /say, /group, /guild a minute | 0.52 / - / - | 0.27 / 1.1-1.2 / 1.2 | 0.4-0.5 / - / 1.2 |
| Tells from bots an hour | 1.5-3 | 0.5 (the invite) | 1.5-3 |
| Lines to the player an hour, silent / speaking | 2.3 / 10.3 | 3.5 / 10.4 | 2.3 / 11.0 |
| Bot-to-bot threads an hour | 241-246 | 279-282 | 264-267 |
| Player lines with no answer an hour (10 typed an hour) | 2.5 | 3.3 | 2.4 |
| Another bot's answer late or with no visible question | 0.1-0.25 | 0-0.1 | 0-0.25 |
| Answer in the wrong channel | 0 | 0 | 0 |
| One bot with 3+ of 10 lines (all channels; /group 5+) | 12.5-15.6 | 24.9-25.3 | 21.5-22.5 |
| of which /faction | about 10 | about 10 | about 10 |
| Threads ending on filler ("true", "ouch", "heh", "same lol") | 13-15 | 16 | 14.5-15.8 |
| Same line again within the hour (3+ words) | 50-66 | 54-61 | 54-60 |
| of which /faction | 2.7 | 2.7 | 2.7 |
| Same short line again within 10 minutes, same channel | 46-56 | 48-57 | 47-51 |
| Same shape, other numbers, within the hour | 5.8-10 | 7.3-10 | 8-11.8 |
| Bot questions nobody answers (/faction, /say, /group, /guild) | 5.8-6.5 | 11 | 8.5-10.8 |
| Bots contradicting themselves (level, class, place; regex) | 0 found | 0 found | 0 found |
| Events with no reaction, of those scripted | a bot's death nearby: 6 of 6 evenings; a bot's ding nearby: 4 of 6 | groupmate death, leave, join: 6 of 6 each | a bot's death nearby: 6 of 6 |

- **Against a 2002-2003 server** (the repo's own research table in `docs/bot-voices/CHAT-BUDGETS.md`: lfg 1 likely / 3 high,
  trade 1 / 3, /say at a busy camp 0.8 / 3, a PvE group 1.5 / 4 with calls): /lfg (3.9) and /trade (3.5) are above the "high"
  figure; /say (0.3-0.5) is under the "likely" figure and under its own floor; the party is on target for 35 minutes and
  then at 0.5 (calls only). One merged window scrolls a line every 3.5-4 seconds. The owner chose busy on purpose
  (2026-11-03); the cost is that every template in findings 2, 6 and 9 comes round within minutes.
- **The zero for contradictions is a sim limit**, not a pass: each bot's level, class and zone are fixed inputs.
- **The near-zero for stale and wrong-channel answers is a sim limit** too: the sim plays every thread on its written delays.

## The 40 worst moments

R1 lines are as typed by the model without `StyleGenerated`; the mechanism is the same in R2.

**Party (L24 dungeon, Albion, seed 1101, R1; party code is the same in R2)**

1. Before any fight, 4 minutes after joining:
   `[0:06:20] [Party] Rosys: "good group tonight"` / `[0:06:28] [Party] Quilys: "that is pretty true lol"` / first pull at 0:09:32.
2. `[0:14:15] [Party] Perys: "should we push on or stay here a bit?"` / `[0:14:23] Sigys: "personal taste really"` /
   `[0:14:35] Perys: "ill follow your lead"` (Perys is the leader) / `[0:14:41] Quilys: "ha, i like that"`.
3. `[0:16:25] Rosys: "this camp works for me"` / `[0:16:34] Sigys: "same thing happened to me"`.
4. `[0:17:00] Sigys: "all good on my end"` / `[0:17:12] Perys: "ha, you always notice the good stuff"`.
5. `[0:17:20] Perys: "endurance is low, give me a moment"` / `[0:17:26] Sigys: "heh, very true"` / `[0:18:13] Perys: "incoming, get ready haha"`.
6. `[0:20:35] Quilys: "want to push one room further?"` / `[0:20:40] Rosys: "i liked it"`.
7. `[0:21:05] Quilys: "need a sec, sitting for power"` / ... / `[0:24:25] Quilys: "good timing on the pulls, i barely have to sit lol"`.
8. `[0:32:25] Quilys: "lets try the room past the corner, i think we can do it :P"` / `[0:32:31] Rosys: "huh, interesting"`.
9. `[0:32:45] Sigys: "ready when u are"` / `[0:32:55] Quilys: "ha, sounds like a good time"`.
10. Three afk lines in ten minutes, nobody ever "back": `[0:02:25] Sigys: "one sec, phone"`, `[0:10:40] Sigys: "brb, door"`,
    `[0:11:30] Torys: "brb, grabbing a drink"`, `[0:31:20] Quilys: "brb sec"`.
11. The death: `[0:35:25] *** Sigys was just killed by a moor boogey!` / `[0:36:00] Wynn: "ouch, who died"` / (nothing) /
    `[0:36:40] Perys: "anyone want to stay in touch after this?"` / `[0:36:44] Rosys: "maybe later"` /
    `[0:38:35] Perys: "best group i have had all week honestly"` / `[0:38:40] Rosys: "noticed that too"`.
12. Then the banter ends for good: last banter line 0:38:40; from 0:40 to 2:00 the party says only calls
    (`"sry ks"` from strangers in /say goes on).
13. `[0:44:00] Wynn: "why do casters wear robes? no pockets for the loot lol"` / `[0:44:04] Perys: "dunno"`.
14. `[0:43:42] Perys: "ok pulling, tank it lol"` (Perys is the Armsman) ... `[1:32:00] Perys: "k pulling, hold aggro :)"`.
15. `[1:24:00] Wynn: "can we invite my friend Tobias?"` / (nothing) / `[1:24:45] Sigys: "endurance break"`.
16. `[1:30:00] *** Torys has left the group.` / (nothing) / `[1:33:00] *** Torys has joined the group.` / (nothing).
17. `[1:10:00] Wynn: "this xp is slow tonight"` / (nothing) / `[1:12:25] Perys: "one coming, be ready lol"`.
18. `[0:04:00] Wynn: "where is Humberton Castle"` / `[0:04:10] Quilys: "dinner soon here, but i can do a few more"` /
    `[0:04:11] Perys: "Humberton Castle is in Black Mtns. South lol"`.
19. `[1:38:00] Wynn: "whats the plan after this"` / `[1:38:08] Rosys: "leveling probably"` / ten minutes later the leader:
    `"last pull then town"`.
20. `[0:02:00] [Party] Quilys: "Keltoi Fogou is all ours, nobody else down here :P"` 15 seconds after the welcome that says to
    meet at Humberton Castle (partly the sim: it puts the party in the dungeon from the start).

**/say near the player**

21. Dungeon, R1: `[0:02:44] Zanstan says, "sry ks"`, `[0:05:50] Rosstan says, "sry ks"`, `[0:07:07] Zanstan says, "sry ks"`,
    `[0:10:09] Rosstan says, "sry ks"`: 16 in the evening, 10 by Rosstan. No kill was shared.
22. Hills, R2: `[0:43:04] Isela says, "1 sec"` / `[0:45:21] Isela says, "rdy"` / `[0:47:02] Rosstan says, "brb"` /
    `[0:47:21] Pera says, "rdy"` / `[0:50:41] Rosstan says, "rdy"` / `[0:50:59] Pera says, "rdy"`. Nobody is grouped with the player.
23. Hills, R2: `[0:29:00] Wynn says, "can anyone rez?"` / `Zanstan says, "Ouch, where did you die?"` (the body is in /say range).
24. Frontier, R1: `[0:41:00] Wynn says, "rez pls"` / `Isela says, "nooo"` / `[0:43:00] Wynn says, "ty"` / `Isela says, "np"`.
25. Frontier, R1: `[1:35:36] Isela says, "was empty"` / `Zanstan says, "was there at 50"`: camp talk in Forest Sauvage.
26. Dungeon, R2 /say: `"nice pulls everyone thats how it should be"`, `"this group is solid glad i joined"`,
    `"nobody has died yet lets keep it that way"` from bots of another party, heard by a player who is not in it
    (meant by #469, but it reads as said to the player).
27. Hills, R2: `[0:46:00] *** Pera glows beside you: reached level 6.` / (nothing in /say) / `[0:47:21] Pera says, "rdy"`.

**/faction, /lfg, /trade**

28. Trade, R1, first two minutes: `Wilna: "wts bone bracelet /send me"` / `Cadwyn: "price?"` / `Wilna: "offers pst"` /
    `Mormund: "wts 99% mithril bound gloves /send"` / `Peren: "price?"` / `Mormund: "offers, pst"` /
    `Braneth: "wts wyvern scale /send"` / `Aelven: "pst price?"` / `Braneth: "offers, pst"`.
29. Trade, R1: `[0:06:48] Braneth: "wtb vest for lvl 45 asap"` / `Ormcan: "how much u paying?"` / `Braneth: "3p tops"`; and at
    0:03:59, 0:04:55, 0:09:19, 0:15:32 the same question from four other bots.
30. One bot's night, R1 hills: Lorcan `[0:02:29] "need rez pls"` → `[0:03:57] "otw"` and `"big grp"` to a keep call →
    `[0:06:23] [LFG] "21 cabby lfg fogou"` → `[0:12:10] "anyone got spare a ring"` → `[0:15:57] "mithra with a grp"`.
31. Asked and answered, then asked again, R1: `[0:00:42] Valo: "xp spot for 18?"` / `Dunek: "fogou if u get a grp"` /
    `Valo: "ty, will try it"` ... `[0:07:57] Valo: "cornwall camp for 18?"` / `Kaelgar: "try mithra"` ... `[0:15:26] Valo: "cornwall again at 18"`.
32. Same line, same bot, 5 minutes apart, R1: `[0:03:31] Roshan: "walked off the road in Cornwall and found ruins"` /
    `[0:08:57] Roshan: "wandered off the road in Cornwall and found ruins"`.
33. Bots against each other, R1: `[0:05:45] Valo: "busy tonight"` / `[0:07:11] Yornoc: "quiet tonight"`. And one bot against itself,
    R2: `[0:42:33] Kaelna: "pull kill sit, pull kill sit, love this camp"` / `[0:54:37] Kaelna: "this spot is dead tonite"`.
34. R2: `[0:44:24] Joric: "any grps in barrows?"` / `Rosys: "none i saw"` / `Joric: "anyone else?"` /
    `Ormna: "few posting in lfg earlier"` / `Joric: "inv sent"`.
35. R2: `[0:55:10] Roshan: "anyone near lyonesse?"` / `Branhan: "Roshan, not atm sry"` / `Roshan: "kk inv"`.
36. R2, the name tic: `[0:55:33] Fenwyn: "any1 up for mithra tomb??"` / `Fenwyn: "what class??"` / `Yorric: "Fenwyn, x2"` /
    `Dunvard: "Fenwyn, 11 merc"` / `Fenwyn: "k x"` / `Dunvard: "Fenwyn, sec, me"`.
37. Bare numbers, R2: `[0:41:31] Iselund: "8"` / `Joric: "Iselund, grats"`; `[0:47:46] Kaelna: "21"`; `[0:48:25] Valo: "21!!"`
    (`AutonomousChatFieldTalk.cs:1724`, `$"{f.Level}!!"`; the idiolect drops the marks). 2.5 an hour.
38. A rez asked of the whole realm, always refused, R1: `[0:02:29] Lorcan: "need rez pls"` / `Dunra: "cant, sry, too far"` /
    `Lorcan: "k"`; `[0:11:58] Joric: "rez pls, cornwall"` / `Nedia: "too far sry"` / `Joric: "np"`. 2.5-3.5 asks an hour, half refused.
39. Player's joke, every evening: `[0:43:00] Wynn: "why did the skeleton cross the road? to get to the other side lol"` / (nothing;
    the route says "intent travel") / `[0:43:04] Isela says, "1 sec"`.
40. Question words, R2 frontier: `Wynn: "who has darkness falls right now?"` / `Dagna: "no, the hibs have it"`;
    `Wynn: "how many relics do we have"` / `Ulfvard: "yep both of ours are home and we hold 1 of theirs"`; Midgard:
    `Joric: "the albs have Midgard's Relic of Thor lol"`. And tidy writers put a full stop on a question: `Valo: "Who's camping
    the Tepok named."`, `"Where's the cabby trainer."` (1.5 an hour).

## What the sim cannot see (why it is rosier than the owner's game)

| Gap | What it hides |
|---|---|
| No live bots: level, class, zone, group, target are fixed inputs | contradictions with the real world (a bot "in avalon marsh" that is not), stale camp and target slots, the real ding and death rates |
| Threads play on written delays; no `AutonomousChatExchange.Play` holds, busy speakers, stand-ins, ceilings | answers that arrive late, out of order, or after the question scrolled away; an answer whose question was held |
| No conversation memory (`ChatConversations`, `ChatThreadLog`, `TryThreadReply`); each player line opens a fresh channel | "where is that", "ty" after an answer, replies to a bot's tell, a second question in a row |
| One responder per player line, picked by the sim | which of 800 bots answers, pile-ons, the level match for "lfg" (the "nobody around your level" answers are the sim's) |
| Stub world in the bench (`WillComeRez` false, no stable routes, stub keeps) | rez promises, "how far", join replies (shown as `[world: ...]`) |
| No /broadcast, /advice, chat groups, /region, no client combat or system window | pools leaking between those channels; screenshot 9 |
| Missing features: tales, storylines, rumours, deeds, milestones, bonds, beggars, roleplayers, realm news, sieges, guild requests, holidays, body language, greetings by memory | about 9,000 corpus lines never drawn here; each can leak or repeat |
| Gold-spam ads did not fill (their slots), only the reactions | the ad wave itself |
| Scripted fights; one party; no real rez, no loot, no movement | calls against real fights, loot talk, "omw" promises |
| The bench seeds its random by the line's text | every evening gives the same answer to the same line: variety of answers is unmeasured |
| Sim errors found on reading | a thread's lines can swap when two fall within 2 s (my deferral); the party fights on after "last pull" |

**The nine screenshots**

| # | Screenshot | In the sim? | Why |
|---:|---|---|---|
| 1 | Tell "I'm by camp station" → "lol" | No. It gives "ah ok" / "gotcha" | The post-#466 short answer shows; the meet-up answer needs a tell exchange in memory, which the bench does not keep |
| 2 | Party "where is that" → "not sure tbh" | Half. "where is that" → silence; "where is Humberton Castle" → answered (+ "lol") | No group conversation memory; the town answer (#467) works |
| 3 | "where do I choose advanced class" unanswered | Yes, as fixed: answered 6 of 6, 12 s | Pure knowledge path |
| 4 | "drop anything good?" → "dunno havent camped them" | Yes, as fixed: 2 "dunno exactly, ask a guard" in 4,997 answers | `Thread` is pure. Those two remain |
| 5 | Group-fight lines in /faction | Yes, as fixed: 3 mild lines in 36 h of /faction; they now sit in /say (moment 26) | The opener filter is pure |
| 6 | /trade post answered in /faction "not atm sry" | Yes, the class: short unnamed /faction answers land right under a /trade or /lfg line 24 times an hour, after #469 | One merged window; this is why finding 9 cannot simply drop the names |
| 7 | "camp for 4? heh", "...green heh", "long day *chuckles*" | Partly: no laugh on bot questions (5 left in 36 h: gold-spam reactions and one debate line); laughs moved to calls and directions (finding 8) | Styling is pure; `*chuckles*` roleplay lines are not drawn |
| 8 | One bot 3 of 9 lines; bare "almost 6" | Yes: 3 of 10 about 10 times an hour in /faction after the hold; "almost N" gone, bare "8" / "21!!" remain | Regulars and the ding line are pure |
| 9 | Ten "weapon returns to normal" lines | No | No combat or system window in any chat sim |

## Checked and found fine (tests only)

- **Fact answers to the player:** advanced class, trainer, where to level at 5, Darkness Falls, the dragon, relic count, a named
  town, a groupmate's spec: right and within 5-12 s, in all three realms.
- **Greetings and partings:** "hi all" → "hi there"; /group "hi all" → "evening Wynn"; "brb 2 min" → "ok, ill be nearby";
  "back" → "re"; "gn all" → "nite".
- **Alarms:** "inc at Caer Berkstead!" → "how many?"; "mids everywhere again tonight" → "where at?".
- **The player's own death and ding in the party:** "oh no" within 2 s; three grats lines, one naming the player.
- **Duo tells:** true and readable ("hi, im 6 sorcerer, want to duo in Camelot Hills?"); "where are you" → "im in Forest
  Sauvage, 48 paladin".
- **No bot answer in the wrong channel**, no answer before its question, in the sim's reach.
- **Debate scenes** read as conversations (crafting brag, the necromancer's shade form).
- **Wipe and death stories** in /faction hold together ("grp wiped in lyonesse" / "lol how" / "someone pulled the named").
- **The asker's own follow-ups** ("guess ill figure it out", "guess it's down", "bump keltoi") are a good touch.
- **Realm parity:** Midgard and Hibernia evenings show the same rates and the right place and class names.

## Not checked, and why

- **Anything on the owner's PC.** No played log was read; no server was started.
- **Guild chat directed at the player** (guild replies need a live guild).
- **/broadcast, /advice, chat groups, /lfg posts by the player** (`OnPlayerLfg` and `OnPlayerOpenChannel` need live bots).
- **Companion bots (/bot, hired).** Every line here is from autonomous gamebots. Companions' chat was not simulated.
- **Raids, battlegrounds, capitals.**
- **Whether 16 lines a minute feels right.** That is the owner's call; the table gives the figures.
- **Non-sequitur rate beyond one evening's party** (hand count; no scorer yet).

## Tools worth keeping

- **`UT_EveningTranscripts` + `evening_read.py`** (PR #472). A few minutes of CI. It could become a ratchet on counts that need
  no judgement: party banter lines after minute 60 (today 0), the share of /say that is "sry ks/rdy/brb/1 sec" (61%), the top-10
  share of /faction (42-48%), "how much u paying?" an hour (25), name-first answers (32%), laughs on calls (11%), unanswered
  party lines from the script (7 of 20).
- **Before it is a ratchet:** move the copied loop into `UT_ChatBudgetSimulation` (one model, not two), print the rows from the
  test instead of a workflow step, and fix the two sim errors above.
- **A missing scorer:** "does this reply fit that line" for bot-to-bot pairs. The 1.2.2 bench scores only player questions.

## Hand-back summary

- Report-only sim of 18 two-hour evenings (36 h, 36k rows) as one player reads them; draft PR #472, not to merge. Tests only.
- Worst: **the party's banter pool is 43 lines and runs dry at minute 30-39 in 6 of 6 evenings**; after that only fight calls.
- **/say beside the player is 61% "sry ks", "rdy", "brb", "1 sec"**; "sry ks" 7 times an hour with no kill stolen.
- **In a bot-led party 7 of 20 player lines get nothing**, a joke gets "dunno", a request to the leader is ignored.
- **Party replies are stock agreement**: 12 of 26 did not fit the line they answered (hand count).
- **Ten regulars say 42-48% of /faction**, 40-60 lines each, with no memory of what they asked ten minutes ago.
- **/trade loops**: "how much u paying?" 25 times an hour; /lfg and /trade run above the repo's own 2003 "high" figures.
- **A groupmate's death, a leave and a join draw no word**; cheerful banter follows a death.
- **Laughs on fight calls and directions** (11% of calls, 13 of 24 lead lines); the tank leader says "tank it".
- **#469's asker-naming fires on 32% of answers** at the owner's volume: a new tic. It cannot just be removed (24 short answers an
  hour still land under a /trade or /lfg line).
- **A silent player gets 2-4 lines an hour** out of about 1,000; jokes, typos and thanks in /faction get silence.
- Of the nine screenshots the sim reproduces 3, 4, 5, 6, 8 (as fixed or as a class), half of 2 and 7, and cannot see 1 and 9.
- The sim is blind to stale and out-of-order answers, conversation memory, real bot state and ten chat features; its zeros
  for contradictions and wrong-channel answers are limits, not passes.
- Smallest fixes with the most effect: findings 2, 6, 8, 9 (all S), then 1 and 7.
