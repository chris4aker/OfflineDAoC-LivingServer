# Realism audit 1.2.3 — structure and measuring (agent: structure)

**Read from:** `origin/fix/1.2.3-faction-polish` at `4a00ab75` (merged to main as `5346e5f7`, #469). Worktree `/home/claude/a2-structure`.
**Status of everything here:** code reading and counts. **Tests only.** Nothing was run in C#. No game behaviour changed. No branch pushed.
**Counts** come from `/tmp/audit2/structure_count.sh <repo>` (read-only greps; rerun to check).

**Short answer to the owner's two questions**
- **"Is that the right way to audit?" No.** Six agents hunting defects is the 1.2.2 method again, wider. It finds instances. It can't say whether the next hour of play will be better.
- **"Do we even have the right structure?" No.** The parts are good. There is no one place a bot line must pass, no one record of what was said, and no statement on a line of where and to whom it may be said. So every fix is a rule in one path, and the next channel or pool doesn't have it.
- **The good news:** the missing pieces are three, they can be added without a rewrite, and half of each already exists in the code.

---

## Top 10 findings, worst first

Each is a **class**, not an instance. "Seen" = in the owner's screenshots (brief items 1-9). Everything else is tests only.

### 1. There is no say gate. 17 exits, each with its own checks.
- **Player sees:** a line that is fine in one channel and wrong in another. Group talk in /faction (seen, 5). A leader's welcome nobody "heard" (seen, 2).
- **Where:** 17 places put a bot line on a screen (`BotChatTranscript.Bot(` call sites), through about 14 wrappers, fed by **about 223 call sites in 40 files**.
- **Measured:** which pass each exit applies (read from the code):

| exit (file:line) | roleplayer voice | "tonight"→"today" | promise gate | length cap | pace meter | who-spoke lane | ceiling / hog / repeat hold | writes conversation state |
|---|---|---|---|---|---|---|---|---|
| /faction `AutonomousBotChatCoordinator.cs:2620` | yes | yes | yes | yes | yes | yes | **yes** | only if the caller calls `Note` |
| /say `GameBot.cs:321` | yes | only via `SayLine` | yes | no | only via `SayLine` | only via `SayLine` | no | caller |
| /group `AutonomousBotGroupChatter.cs:372` | no | yes | no | no | yes | no | no | yes (thread log) |
| /group `AutonomousLivingServer.cs:286` (`Speak`) | yes | no | no | no | no | no | no | **no** |
| /group `GameBot.cs:1623` (`TellOwnGroup`) | yes | no | no | no | no | no | no | no |
| /group `EpicEncounterTactics.RaidCalls.cs:262` | no | no | no | no | no | no | no | no |
| tell `AutonomousLivingServer.cs:410` | yes | yes | yes | no | no | - | no | yes (thread log) |
| tell `NoLiferBuffBots.cs:683`, `CompanionChat165.cs:82`, `AutonomousGoldSpammers.cs:287` | no | no | no | no | no | - | no | no / own |
| /lfg, /trade `AutonomousBotChatCoordinator.cs:1247` | no | yes | yes | yes | yes | yes | caller | caller |
| /trade `BotCrafterVoice.cs:132` | no | yes | no | 240 | yes | yes | no | no |
| /broadcast `AutonomousChatBroadcast.cs:71` | no | yes | no | 240 | yes | no | caller | no |
| /guild `AutonomousBotGuildSocial.cs:963` | via `Speak` only | yes | yes | yes | yes | yes | caller | own (`NoteHeard`) |
| /advice `AutonomousBotChatCoordinator.cs:1889` | no | no | no | no | no | no | no | no |
| alliance, yell | no | no | no | no | no | no | no | no |

- **Not read line by line:** the buffbot, companion and spammer tell senders, the raid-call, alliance and yell exits; their rows are from the send site only.
- **Style is not on the table at all:** writing style is applied by the caller, at **43 `StyleFor(` sites in 13 files**. A caller that forgets gets an unstyled line; one that styles twice gets two laughs.
- **Class:** missing choke point.
- **Fix:** the say gate (section 3, piece A). **Size L** in total, first step **S/M**. **Risk:** low while pass-through; medium when checks are switched on (channels get quieter; double styling during migration).

### 2. There is no one record of the conversation. At least nine partial ones.
- **Player sees:** "where is that" → "not sure tbh" (seen, 2). A short answer to a question that scrolled away (seen, 6). A bot asking what you just told it.
- **Where:** `ChatConversations.Live` (player turns only, `AutonomousChatConversation.cs:50`), `AutonomousChatThreads.Log` (last bot lines, `:192`), `AutonomousChatExchange.Lanes` (who spoke, `:224`), `AutonomousChatRecall.Stores` (`:179`), `AutonomousChatFieldTalk.Memories` (`:593`), `AutonomousChatFiller.Recent` (`:532`), `AutonomousChatExchange.Shorts` (`:692`), guild `RecentByGuild`/`RecentField` (`AutonomousBotGuildSocial.cs:46,560`), the scenes' `Heard` (`AutonomousBotDebates.cs:511`), and 16 `Last*` timing dictionaries in the coordinator.
- **How it fails:** a line is in a store only if its caller wrote it there. `ChatConversations.Live.Note*` is written from 9 sites in 3 files. A bot-to-bot line is never in it. Lanes are per channel, but the player reads /faction, /trade and /lfg in one window.
- **Class:** split state, opt-in writes.
- **Fix:** one audience model written by the gate (piece B). **Size L. Risk M** (timing rules differ subtly between the stores).

### 3. A line does not say where, to whom, or on what condition it may be said.
- **Player sees:** "nobody has died yet, lets keep it that way" in /faction (seen, 5). "almost 6" answering nothing (seen, 8).
- **Where:** `data/chat/corpus.json`: **34,501 lines**. Fields: kind, topic, who, realm, class, levels, needs, intent, invites, answers, mood, weight. **No channel, no addressee, no claim.** **14,945 of 16,750 ambient lines (89%) have no `@needs` at all.**
- **How channel fit is decided today:** by deny-lists in code after the pick: `AutonomousChatFiller.AmbientFilter`, `SaidToTheGroup` (new in 1.2.3), `IsAmbientChannelLine`, `NoEmoteKinds`. A new pool or kind is allowed everywhere until someone sees it leak.
- **Class:** no contract; allow-by-default.
- **Fix:** the line contract, build-time linter, run-time check in the gate (piece C). **Size M** for schema and linter, **L** to label. **Risk low** (additive, shadow first).

### 4. The same question is answered by at least seven different orderings, one per channel.
- **Player sees:** a question answered in /faction and not in /group, or the other way round.
- **Where:**
  - /faction and /say: `OnPlayerFactionChat` `:1699`, `OnPlayerLocalChat` `:1758` → `SchedulePlayerReplies` `:2743` → `AutonomousChatReplyPath.Plan/Choose`. Nine steps run first (beggars, storylines, debates, screen, guild request, tales, thread reply, debate answer, cooldown).
  - /group: `AutonomousBotGroupChatter.OnPlayerGroupChat` `:215`. Own cascade: thread, other addressee, knowledge **always first**, alarm, "come", then `ReplyChoiceToPlayer`. No help-call rule, no reply budget.
  - tell: `OnPlayerTell` `:2016` → `TellReply` `:2103`: lead invite, follow-ups, counter-offer, then `ReplyPath.Tell`.
  - /advice `:1865`, /broadcast and /chatgroup `:1904`, /trade `:1369`, /lfg (`AutonomousLivingServer.OnPlayerLfg`), /guild (`AutonomousBotGuildSocial.cs:327`, `AutonomousBotGuilds.cs:365`), companions (`CompanionChat165.cs:66,206`).
- **Evidence it bites:** the 1.2.3 changelog says the bench's /group path "now runs the knowledge lookup first, as the live path does" — the mirror had drifted from the live path.
- **Class:** per-channel code paths.
- **Fix:** one planner with the channel as a parameter (piece D). `AutonomousChatReplyPath` with its `Ports` is already the right seam; it covers 3 of the 7. **Size M-L. Risk M.** The bench protects this one.

### 5. Bot-to-bot talk is made by other engines than answers to players.
- **Player sees:** "puny skeletons drop anything good?" → "dunno havent camped them" (seen, 4), one release after "no dunno in realm channels" shipped for player questions.
- **Where:** `AutonomousChatFieldTalk.cs` (3,101 lines of templates and its own `Thread`), the corpus's `@invites`/`@answers` replies (`ScheduleVoicedBotReplies` `:3443`), scenes (`AutonomousBotDebates`), guild threads, tales, rumours, bonds, deeds, storylines, market replies. About **24 feature tags** (`src=`) in all.
- **Why rules don't carry:** the 1.2.2 dunno rule lives in `AutonomousChatReplyBudget.Rescue`, which only the player path calls. 1.2.3 added a second copy for threads (`AutonomousChatFieldTalk.IsNonAnswer`).
- **Class:** duplicated engines.
- **Fix:** a bot's question is answered by the same planner and the same knowledge as a player's (piece D/E). **Size M. Risk M** (thread variety drops until knowledge covers more).

### 6. Style is added after the pick and has to guess what kind of line it is.
- **Player sees:** "camelot hills camp for 4? heh", "nothing humbles you like dying to something green heh" (seen, 7).
- **Where:** `AutonomousBotVoiceCorpus.TakesEmote` `:775` decides by regex on the text: `SeriousWords` (`:786`, a word list that got "dying" and "humbles" in 1.2.3, "dunno" and "raid" in 1.2.2), `FunctionalWords`, `AsksOnly`, `EndsAsQuestion` (new in 1.2.3). `AutonomousChatIdiolect.Apply` is a second, separate habit layer (10 call sites).
- **Class:** a late layer without the facts the early layer had.
- **Fix:** style inside the gate, given the line's act (question, answer, warning, joke...). The word lists become a fallback for unlabelled lines. **Size M. Risk low.**

### 7. The player's line is understood by word lists. Every miss becomes a new phrase.
- **Player sees:** "I'm by camp station" read as talk about grinding, because of "camp" (seen, 1).
- **Measured in the chat files:** **293 regex uses, 354 literal `Contains/StartsWith/EndsWith` checks, 207 `bool Xxx(string)` predicates.** Largest: `AutonomousChatShortTalk.cs` (62 literals, 28 regex, 46 phrase lists), `AutonomousChatFieldTalk.cs`, `AutonomousChatKnowledge.cs`, `AutonomousChatQuestions.cs` (62 regex).
- **Class:** open-ended rule growth.
- **Fix (not a rewrite):** parse a line once into one small record (act, addressee, entities: place, class, level, item, npc) that every later step reads; state decides ties (a bot waiting for you at a place reads "camp station" as a place). A small local model could do this parse later (section 5). **Size M. Risk M.**

### 8. A reply is chosen when planned and said up to 40 s later, unchecked.
- **Player sees:** an answer that was true when written and isn't when it lands; an answer to the line before last.
- **Where:** `SchedulePlayerReplies` captures `text` and sends it from a timer after `due` ms (`:2990-3040`); at send time only "is the bot free" is re-checked. The tell path does it right (works the reply out at send, `:2047`). `ChatPromises.BeforeSend` re-checks promises only.
- **Class:** time-of-check vs time-of-use.
- **Fix:** the gate re-checks the line's claims and its referent at send time (piece A, steps 3-4). **Size S** once the gate exists. **Risk low.**

### 9. The bench measures a few percent of what the player reads, with questions taken from the code's own answers.
- **Measured:** 467 questions: **428 /faction, 16 /group, 12 /say, 11 tell**. One turn each, a fresh channel, a stub world, one responder per realm. The holdout: **60 of 60 in /faction**, written by the same generator; its `source` field names the answering function (`AutonomousChatKnowledge.TryAnswerBuyGear`...).
- **Scale:** /faction is paced at 5-7 bot lines a minute (docs/OPEN-WORK.md:229), so 300-420 lines an hour in one channel. A player asks perhaps 5-15 questions an hour. The bench scores the first answer to those.
- **Result:** of the owner's nine defects, **one** (3) was a kind of thing the bench could see, and it wasn't asked.
- **Class:** measuring what is easy.
- **Fix:** section 4. **Size M. Risk none** (reports first).

### 10. The log says what was said, not why, and not what was held back.
- **Player sees:** nothing; this is why the owner's hour can't be audited afterwards.
- **Where:** `CHAT_LINE` (`BotChatTranscript.cs:47`) carries who, channel, realm, from, via, src, ex, i, role, kind, class, level, cap, text. It lacks: the corpus line id, the rule and fit, the addressee in a channel, which line it answers, who heard it. Reasons and silences go to `AutonomousChatWhy`'s in-memory ring (`/lswhy`), not the log; two `PlanLog.Debug` rows are the exception. System-window text (the "weapon returns to normal" burst, seen, 9) is not logged at all.
- **Class:** no audit trail.
- **Fix:** the gate writes one full row per line and per held line (section 4, capture). **Size S-M. Risk low** (log volume; one row per line a player can see).

---

## 1. The system as built

**Entry points where a bot may speak**
- **17 wire exits** (table in finding 1).
- **About 223 chooser sites in 40 files.** Largest: coordinator 65, guild social 24, `AutonomousLivingServer.Events.cs` 15, `.Engage.cs` 8, deeds 7, schedule 7.
- **About 12 player-line handlers** (finding 4: faction, say, group, tell, advice, broadcast/chatgroup, trade, lfg, two for guild, two for companions) plus beggars, tales, storylines and debates hooking /faction.
- **Unprompted sources:** `TryStartAmbient` `:121` → budgeted ambient (`:154`), field talk (`TryField` `:421`), corpus openers (`BuildAmbientOpening` `:3951`), stories and dings (`TellStory` `:886`), news (`TellNews` `:1148`), exchange adverts, scenes and debates, tales, rumours, bonds, deeds, milestones, storylines, roleplayers, beggars, gold spammers, Leeroy, guild idle and recruiting, group chatter, bot-led leader lines, raid and encounter calls, crafter adverts, started tells, memory greetings, alarms, nemesis and revenge lines, schedule lines (login, logout, afk), companion lines.

**Layers a line may meet (none meets all)**
- **Read the player:** `AutonomousChatScreen` (spam), `AutonomousChatIntentModel.Predict`, `AutonomousChatShortTalk.QuestionShape`, `AutonomousChatThreads` (follow-up words), `ChatConversations.Resolve` ("there", "that").
- **Know:** `AutonomousChatKnowledge.TryAnswer` (11 call sites) → inside it, in call order: class choice, towns, lore (`LoreChat`), guide, common answers, trainers, tickets...; separately `AutonomousChatQuestions.AnswerWorld`, `AutonomousChatLiveAnswers` (26 sites), `AutonomousChatGear`, `AutonomousChatPlaces`, `AutonomousChatMeetup`, `BotWayFinding`. Precedence is the order of the `if`s. The 1.2.2 audit's C1, C2 and C3 were all ordering faults.
- **Pick:** corpus (`AutonomousBotVoices.TryLine`, 51 sites in 15 files), field templates, context replies, memory lines.
- **Budget:** `AutonomousChatReplyBudget` (fit, rescue, brush-off), `AutonomousChatBudget` (per-channel meters), `AutonomousChatArbiter` (how many answer, typing delay), `AutonomousChatExchange` (ordered multi-line queues, `SinkHold`).
- **Truth:** `ChatPromises` ("omw" only if it goes), `SaysUntrue` (2 sites), `CanGiveAnswer`.
- **Style:** `AutonomousBotVoices.StyleFor`, `AutonomousBotVoiceCorpus.Style`, `AutonomousChatIdiolect`, `AutonomousRoleplayers.Voice`, `ForNow`.
- **Fallbacks:** rescue ("dunno", now narrowed), `ToContent`'s last pick, the voices-off legacy lines (`GenerateFactionReply` `:2485`, `GenerateLocalReply` `:2547`, `GenerateDirectedReply` `:4072`, `TryStartAmbientLegacy` `:1062`), or silence.

**What is shared, and what isn't**

| question | where the answer lives | who can read it |
|---|---|---|
| who said what in this channel | thread log (bot lines the caller noted); lanes (names and times only) | the paths that call them |
| who said what *to this player* | `ChatConversations` | faction, say, group (since #467), tell, companions |
| what the player's window showed across channels | **nowhere** | - |
| bot-to-bot lines | field memory, recall, thread log; not the conversation model | field talk only |
| place / person / thing in play | `Conversation.Place/Item/ClassName/Level`, set by regex on player and noted bot lines | the same four paths |
| what the bot promised | `ChatPromises` | promise gate at 5 sends |
| what the bot knows about itself | `AutonomousChatFieldTalk.Facts`, `AutonomousChatContext.Facts`, `ReplyPath.Ports` — three overlapping fact records | each its own engine |

**Special-case rules by release**
- **Proxy used:** version-tagged rule comments in the 66 chat-related files (37,165 lines). Each marks a rule or exception added then. It undercounts untagged rules.

| release | tagged rules | mostly in |
|---|---:|---|
| 1.1.x | 248 | coordinator 63, field talk 38, short talk 14 |
| 1.2 | 261 | coordinator 60, field talk 37, budget 24, short talk 23, corpus/voices 20 |
| 1.2.1 | 7 | coordinator 3, budget 3 |
| 1.2.2 | 52 | coordinator 9, knowledge 8, voice corpus 8, field talk 7 |
| 1.2.3 (so far) | 28 | field talk 6, coordinator 4, knowledge 4, exchange 3, reply budget 2 |
| cites the owner, a PC check or a play test | 254 | field talk 47, coordinator 34, knowledge 18 |

- **1.2.3 in detail (#465-#469):** nine seen defects became **2 new modules** (`AutonomousChatMeetup` 191 lines, `AutonomousChatClassChoice` 166), **about 10 new predicates** (`IsNonAnswer`, `SaidToTheGroup`, `NamedWhenApart`, `EndsAsQuestion`, `JustSpoke`/"hog", `LevelProgress`, `RescuesWithDunno(channel, message)`, `NotePlace`, `AnswererFirst`, `TryAnswerTown`), 2 word-list additions, 2 deleted lines and **5 new test files**. Each is correct. None stops the same fault in the next path.
- **Tests follow the same shape:** 59 chat test files, many named for the incident (`UT_ChatPcCheck0719`, `UT_ChatAfterRC6`, `UT_TellMeetup123`).

**How each of the nine got through**

| # | seen | layer that should have stopped it | why the layer wasn't there | why a 90% bench didn't see it |
|---|---|---|---|---|
| 1 | tell: "I'm by camp station" → "lol" | conversation state: the bot had just said "inv me when ur close", so the next line is about meeting | the tell cascade reads each line alone; intent by keyword; the last fallback was a laugh | 11 tell questions, each a first line; no second turn |
| 2 | "where is that" → "not sure tbh"; then "stablemaster is your friend" | state: the leader named the place; knowledge: towns | the welcome went through `Speak` (finding 1 row 4), which writes no state; towns weren't in any lookup; rescue and a memory line filled the gap | 16 group questions, no leader line before them; stub world had no towns; the bench's group path differed from live |
| 3 | "where do I choose advanced class" → silence | knowledge coverage | nothing knew it; silence is the designed fallback | questions are generated from what the code can answer |
| 4 | bot-to-bot "dunno havent camped them" | the no-non-answer rule | it existed only on the player path | no bot-to-bot lines in the bench |
| 5 | group talk in /faction | channel fit | lines carry no channel; the filter was a deny-list that didn't list this topic | ambient lines aren't in the bench |
| 6 | "not atm sry" after a /trade post | freshness and addressee at send | lanes are per channel; nothing models the player's window | single pairs only |
| 7 | "? heh", "dying ... heh" | style knowing the line's kind | style runs after the pick, on text | the bench's clunky flag covers replies to players only |
| 8 | one bot 3 of 9; "almost 6" | speaker share per audience; a line must carry its referent | regulars are boosted on purpose (2.5x, stacked); no share rule until #469; the opener had no contract | no distribution measure |
| 9 | ten "weapon returns to normal" | none in chat: base effect code | "what the player reads" is wider than bot chat; system text is unowned and unlogged | not chat |

---

## 2. Judgement

**Can this structure reach "passes for a real player"? Not by adding rules.**
- **The material is good enough.** 34.5k period lines, 565 scenes, real world facts, ordered exchanges, per-channel pace, promises that are kept. The 1.2.2 audit found the written lines clean.
- **The faults are at the joins,** and the joins multiply: about 24 features × 8 channels × 5 bot kinds. A rule covers one cell. The owner plays in all of them in one hour.
- **Evidence:** 1.2.2 raised the bench from 60.5% to 90.3%. The owner then saw nine defects in an hour; eight were outside what the bench looks at.

**Structural causes, named**
1. **No choke point.** 17 exits; passes are opt-in per exit (finding 1).
2. **No single conversation state.** Nine stores; writes are opt-in per caller (finding 2).
3. **Lines have no declared audience, channel or precondition.** Fit is a deny-list after the pick (finding 3).
4. **Replies are chosen per channel by separate code paths** (finding 4).
5. **Bot-to-bot talk comes from other engines** than answers to players (finding 5).
6. **Style runs after selection without the line's kind** (finding 6).
7. **Understanding is word lists; knowledge precedence is call order** (finding 7; 1.2.2 audit C1-C3).
8. **Check at plan time, say later** (finding 8).
9. **The player's screen is not modelled.** Channels share one window; system text shares the screen.
10. **Fallbacks say something.** A weak path ends in a laugh, a dunno or an advice line; 1.2.2 and 1.2.3 removed three such fallbacks one at a time.

**What I would not blame**
- The corpus. The exchange queue. The budgets. The promise gate. `ReplyPath`/`Ports`. `BotChatTranscript`. They are the pieces the target is built from.

---

## 3. Target structure, in steps

No rewrite. Five pieces. Each starts as a wrapper that changes nothing and logs what it would do.

### A. The say gate — build first
- **What:** one function every bot line in every channel goes through. Input: an **utterance** = speaker, channel, audience, addressee, act (question, answer, remark, offer, call, reaction, greeting, story, ad), source (feature, exchange, index, role — `AutonomousChatExchange.Source` already), line id or code site, the line it answers, the claims it makes, the text, "already styled".
- **Fixed order of checks:**
  1. **Channel fit:** the line's contract allows this channel and audience.
  2. **Addressee and referent:** a short reply far from its question names the asker or is dropped; a reaction needs its trigger still on the listener's screen, counting every channel that listener sees.
  3. **Freshness:** what it answers is recent; the speaker's state is still what the line needs (checked now, not when planned).
  4. **Grounding:** each claim is true now (place, level, class, grouped, item, promise). `ChatPromises` and `SaysUntrue`, generalised.
  5. **Pacing:** meter, ceiling, speaker share, repeat windows. `SinkHold`, `Shorts`, `Meter`, moved here.
  6. **Style:** voice, idiolect, time of day, roleplayer voice, knowing the act.
  7. **Send and record:** transport, transcript row, state write, meter count.
- **Replaces:** the per-exit pass subsets; 43 `StyleFor`, 8 `ForNow`, 5 `BeforeSend`, 7 `Meter.Count`, 31 `Why.Record` call sites; `SayLine`, `Speak`'s group and guild branches, `TellOwnGroup`, the two extra tell senders.
- **Steps:**
  1. Add the utterance type and the gate as a **pass-through called from inside each of the 17 exits**. No behaviour change. It writes the new log row (section 4) and counts "came in without a contract".
  2. Move the passes every line should get into it: time of day, promise gate, cap, meter, lane, transcript. Small visible change (e.g. /say gets "today").
  3. The gate writes the conversation state (piece B). **Removes the class of defect 2.**
  4. Style in the gate; delete `StyleFor` at call sites file by file (the "already styled" flag prevents doubles meanwhile).
  5. Checks 1-4 in **shadow**: log "would hold" and why. Read them on simulated evenings and on the owner's log. Then enforce one at a time.
  6. CI ratchet: the count of raw sends in `bots/` outside the gate file may only fall (`structure_count.sh` gives today's 13 and 223).
- **Size:** steps 1-2 **M** (a week of careful work), whole piece **L**. **Risk:** low until step 5; then quieter channels (the floors in `AutonomousChatBudget` already push back) and held lines that were fine (shadow data decides).
- **Removes for good:** pools leaking between channels (5), replies to scrolled questions (6), stale answers, untrue claims, style on the wrong kind (7), unlogged decisions, one bot hogging (8), any future exit missing a pass.

### B. One conversation model per audience
- **What:** a **room** per audience (realm /faction, region /say, group, guild, tell pair, /trade, /lfg) holding the last N utterances by anyone (player, bot to player, **bot to bot**), open questions with asker and expiry, things in play (place, person, item, level), the floor, speaker share, promises. Plus a **window view** per player that merges the rooms that player reads.
- **Replaces:** `ChatThreadLog`, `ChatConversations`, `AutonomousChatExchange.Lanes`, `Filler.Recent`, `Shorts`, the field memory's "said" part, guild `RecentByGuild`/`RecentField`, most of the coordinator's 16 `Last*` maps.
- **Steps:** (1) build it as a facade fed only by the gate; (2) move readers one at a time (`Resolve`, `FloorHolder`, `OpenQuestion`, `LastBy`, `JustSpoke`, `Shorts.Fresh`), keeping each old store's unit tests pointed at the facade; (3) delete a store when its last reader moves.
- **Size L. Risk M.** Do it after A step 3; it is safe to go slowly.
- **Removes:** "where is that" with no "that" in any channel (2), second-turn misreads (1), answers that ignore what another bot just said, cross-channel confusion (6).

### C. The line contract
- **What a line declares:** `@channels` (which may carry it), `@to` (the room, the group, one person, nobody), `@act`, `@claims` (in a group, at a camp, just died, a level, a place, an item), `@after` (what must be on screen for it to make sense). Code-built lines carry the same record; `AutonomousChatFieldTalk.Said` already holds Kind, Family, Place, Item — extend it.
- **Build time:** `build_corpus.py` infers defaults for the 34.5k lines from kind, topic, needs and wording, and `chat_rules.py` fails on contradictions: a line that speaks to "everyone/all/we/this group" must be `@to group` with `in_group`; a first-person state claim needs its claim; an ambient line with no `@needs` may claim nothing. Staged by file with the existing `STRICT_OWNERS` switch.
- **Run time:** the gate's check 1 and 4 read the contract. Unlabelled lines fall back to today's filters.
- **Replaces over time:** `AmbientFilter`, `SaidToTheGroup`, `IsAmbientChannelLine`, `NoEmoteKinds`, `SeriousWords`, `FunctionalWords` as primary rules.
- **Size:** schema, inference and linter **M**; review of inferred labels **L** but mechanical and splittable by file. **Risk low**; wrong inference silences a line, so shadow first and report the hold rate per file.
- **Removes:** pool leaks (5), openers with no referent (8), group lines to the realm, tells' lines in channels, anything "written for one place, drawn in another" (also 1.2.2's C12).

### D. One reply planner for every channel, for players and bots
- **What:** `AutonomousChatReplyPath` grown to all channels, one order, channel as data: follow-up in a thread → commitments between these two (invite, meetup, promise) → the bot's own facts → world facts → knowledge with the conversation resolved → a written reply for the act → **silence**. A bot's question in a field thread goes through the same planner with a bot as the asker.
- **Replaces:** the cascades in `OnPlayerGroupChat`, `OnPlayerAdvice`, `OnPlayerOpenChannel`, `TellReply`'s pre-steps, guild and companion reply code, `FieldTalk.Thread`'s answer half, `KnownAnswer`.
- **Size M-L. Risk M.** The question bench is the safety net here; extend it to multi-turn first (section 4).
- **Removes:** rules that hold in one channel only (4), per-channel fallbacks (1, 2), bench-versus-live drift.

### E. Knowledge as one grounded lookup
- **What:** `Ask(parsed question, asker, responder) → answer {text, facts, source, confidence}`. Providers (world state, places, towns, class tree, trainers, merchants, routes, the bot's live facts, lore) are registered with the question types they answer. **Precedence by question type, not by call order.** Wrap today's `TryAnswerX` functions as providers; don't rewrite them.
- **Replaces:** the cascade inside `AutonomousChatKnowledge.TryAnswer`; 11 call sites become one, inside the planner.
- **Why it matters for the gate:** an answer that returns its facts can be checked at send time and scored by fact, not by regex on text.
- **Size M. Risk low-M.**
- **Removes:** lore answering a level question (1.2.2 C1), "epic is a raid" (C2), and makes holes countable: every question type × provider cell is filled or empty.

### Delete
- **The voices-off chat** (`GenerateFactionReply`, `GenerateLocalReply`, `GenerateDirectedReply`, `TryStartAmbientLegacy`, and the `voices ? … : …` branches). Voices are on by default since 1.2. Make "off" mean quiet. Owner's call.
- **Raw sends in `bots/`** outside the gate, once A step 2 lands.
- **The duplicate fact records** (`FieldTalk.Facts`, `ChatContext.Facts`, `Ports`) → one.
- **Fallbacks that speak:** any path whose last resort is a laugh, a dunno or advice. Silence is the fallback.
- **Stores** as piece B absorbs them.
- **Keep:** the incident tests. They are cheap. Stop treating a new one as the fix.

---

## 4. Measuring

**What is wrong with today's measure**
- One question → one answer, 92% in one channel, a stub world, no history (finding 9).
- Its questions and its holdout come from the code's own answer tables, by the author of the fixes.
- A share of good answers says nothing about the other 300+ lines an hour.
- `chat_audit.py` (1,679 lines, already in CI on a simulated evening) is the right idea and has the same habit: each check was added after a defect was seen.

**What already exists to build on**
- `CHAT_LINE` rows with `src=`, `ex=`, `i=`, `role=`, class and level; `CHAT_EMOTE`; `CHAT_SCREEN`; `LIVING_FEATURE_DUE/DROPPED`; `CHAT_PROMISE`; `BOT_INVITE_RESULT`; `AUTONOMOUS_BOT_DEATH`; `FORK_BUILD` per process.
- `source/tools/chat_audit.py` and `played_check_log.py` (reads the owner's log per build, masks player names).
- The playtester (scripted players; scenarios with gamebots run on the owner's PC only).

**Capture: make Tuesday's session a transcript**
- **One row per line, written by the gate** (piece A step 1; until then, add the fields to `BotChatTranscript.Bot`):
  `CHAT_SAY seq= t= ch= room= from= id= kind= cls= lvl= zone= to= re=<seq it answers> ex= i= role= act= src= line=<corpus id or code site> rule= fit= style=<voice,+emote> claims="place=..;level=.." text=`
- **Player lines:** `CHAT_HEAR seq= ch= room= to= zone= grouped=<bot ids> text=` (as `CHAT_LINE who=player` today, plus seq and group).
- **Held lines and silences:** `CHAT_HELD seq= re= ch= code= rule= reason=` for every player line that drew nothing and every bot line the gate stopped. This is `/lswhy` written to the log.
- **System window:** `SCREEN_BURST t= key="weapon returns to normal" n=10 window=2s` when one message key repeats past a threshold near a player. Cheap, and it would have caught 9.
- **Body:** one `BODY` row per visible idle action near a player (section 6).
- **File:** a separate small rolling log (`logs/session-chat.log`), so the owner sends one file.
- **Tool:** `source/tools/session_transcript.py LOG → session-<date>-<build>.jsonl` and a readable `.md` in screen order per window, split per `FORK_BUILD`, player and account names masked as `played_check_log.py` does, bot names kept.
- **Rules:** transcripts and logs stay out of git (`**/*.log` and `/runtime/` are already ignored; add `/sessions/`). Agents read them from where the owner puts them and never commit them. CI uses simulated transcripts only.
- **One more thing for the owner:** a `/odd [note]` command that logs `CHAT_FLAG seq=<last line> note=`. One keystroke marks "that was wrong" at the moment it happens. It beats screenshots and gives the count below a ground truth.

**The number: defects per hour of real play**
- **Count** = owner flags + reader-confirmed defects, per hour, per channel, per build.
- **Baseline:** 1.2.2: 9 seen in about an hour, by eye (seen). The true rate is higher.
- **Use:** a trend per release, with the list behind it. Two or three hours per build before it means much.
- **Honest limit:** it comes only from the owner's PC. It is a report, never a CI gate.

**Whole-transcript scoring**
- **Per line, in context:** fits its channel; has a referent on screen; answers what it follows; true to the speaker's state; in era; not a repeat; no laugh on the wrong kind.
- **Per exchange:** a question answered within N lines by one bot; no dunno; closing fits.
- **Per hour:** player questions unanswered; top speaker's share of any 10 lines; reactions per 100 lines; system bursts; share of lines with no contract.
- **Tools:** `chat_audit.py` gains these as checks over the new rows (exact, since `re=`, `to=` and `act=` are in the row, not guessed).

**A red-team reader**
- An agent that **did not write the fix** and **has no code** reads 30-minute windows as a player and marks every line that reads as a bot, with a reason. Two readers; a line both mark is a defect.
- Mix in real 2002 chat (docs/bot-voices/PERIOD-CHAT-RESEARCH.md) unlabelled now and then, to measure the reader's false alarms.
- Non-deterministic: a report.

**Holdouts that hold**
- **Source:** the owner's real lines (`CHAT_LINE who=player`), masked, become questions. Add period sources. Not the code's answer tables.
- **Author:** not the fix's author. A different agent session, given the logs and no diff.
- **Rotation:** each release, the old holdout joins the open bench and a new one is written.
- **Multi-turn scenarios:** 40-60 short scripted scenes across channels (invite then "im by X" by tell; leader's welcome then "where is that"; a /trade post; a question during a debate; a bot's question a bot must answer). Run through the planner and the gate with a stub transport, fixed seeds. This is where the nine belong as cases, with their neighbours.

**CI gate or report**

| measure | gate | report |
|---|---|---|
| line-contract linter | yes, strict by file as labelled | hold rate per file |
| raw sends outside the gate; exits without a pass | yes, a ratchet that only falls | |
| question bench (existing ratchet) | yes | |
| multi-turn scenarios, fixed seeds | yes, once stable | first two weeks |
| `chat_audit.py` verdict on the simulated evening | yes (it runs today, promote it) | |
| holdout score | no | yes; a fall is a finding |
| defects per hour from the owner's log | no | yes, per build |
| red-team reader, any model-based judge | no | yes |
| speaker share, reactions per 100 on simulations | caps later | yes now |

---

## 5. A local language model

**All numbers here are my estimates from general knowledge, not measured on this project. A two-day spike on the owner's PC must measure them before any decision.**

**What it would be**
- An optional small model run on the player's PC, out of the server process, for **free chat with the player**: tells, /group, a question in /faction that the planner can't answer. Written lines stay the default and the fallback. Ambient chat stays written: its problem is state, not text.

**Costs**
- **Download:** a 3-4B model at 4-bit is about 2-3 GB; a 1-1.5B model about 0.7-1 GB. Not in the installer; a launcher button that downloads and hash-checks it.
- **Memory:** about 3-4 GB RAM (or VRAM) for the larger, about 1-1.5 GB for the smaller, on top of the server and client.
- **Runtime:** llama.cpp through a .NET binding (LLamaSharp) or a bundled helper exe; native DLLs per backend (CPU, Vulkan, CUDA). The CUDA one is hundreds of MB; Vulkan is the sane default on Windows. More files for antivirus to dislike.
- **Latency:** with a 6 GB+ GPU, under a second per line. On CPU only: the reply is 10-20 tokens (1-3 s on a recent 6-8 core), but reading a 300-500 token prompt costs several seconds unless the prefix is cached. An old 4-core laptop: 10 s or more.
- **It fits a slot that already exists:** replies are delayed on purpose to mimic typing (median 14.4 s to the first /faction answer in the 1.2.2 audit; tells capped at 12 s). A 3-8 s generation hides inside that.
- **CPU contention:** the server runs hundreds of bots per realm and has a game-loop stall gate. Inference must be out of process, low priority, thread-capped, one request at a time. On a CPU-only PC that may still cause stalls. The classic client uses little GPU, which helps where there is one.
- **Licences:** differ per model (Apache, MIT, or a vendor's own terms). `THIRD_PARTY.md` needs an entry; some licences restrict redistribution, which is another reason to download rather than ship.

**Failure modes**
- **No model, wrong hash, out of memory, too slow:** timeout (say 6 s) → the written line. Must be invisible.
- **Wrong facts:** small models don't know 1.65 DAoC and do know later expansions. Every fact must come from piece E in the prompt, and the gate's grounding check must reject a line that states anything else.
- **Wrong era and fourth wall:** the banned list, vocabulary and `fourth_wall.py` checks are Python build tools today. They must run on every generated line at run time. That port is real work.
- **Wrong register:** models write tidy sentences; 2002 chat is terse and lower-case. The voice layer helps; expect a tuning job.
- **A player trying to break it** ("ignore your instructions"): low stakes offline, but the gate must cap length and reject system-voice or out-of-world lines.
- **Promises:** a generated "omw" must go through `ChatPromises` like any other.
- **Tests:** output isn't repeatable. It can only be judged by transcript scoring (section 4), not pinned by unit tests.
- **Support:** "my bots talk strangely on my PC" becomes unreproducible. With one edition and one maintainer, that is a real cost.

**Opt-in launcher setting?**
- **Yes, and only that:** off by default, like the items AGENTS.md keeps off for being hardware- or rule-dependent. The "features on by default" rule should not apply to a multi-GB download; that is the owner's call to confirm.

**Are the gate and the state model prerequisites? Yes, either way.**
- The room (piece B) and the lookup (piece E) **are the prompt**. Without them the model has nothing true to say.
- The gate (piece A) is **the only thing that makes generated text safe to show**.
- With them built, written lines get most of the gain at no hardware cost. Without them, a model adds a tenth engine with the same leaks.

**Recommendation**
- **Now (1.2.x): against.** It would hide the structural faults behind nicer text and add a new class of defect.
- **After A, B and E are enforced and the defects-per-hour number has fallen for two releases:** a spike, in this order:
  1. **Reading only:** a 1-1.5B model parses the player's line into the act and entities of finding 7, with the regexes as fallback. Written lines still answer. Small download, short prompt, CPU-friendly, and it attacks the 293-regex problem directly.
  2. **Free text in tells and /group only,** behind the opt-in, grounded and gated, measured by the transcript scores against written-only on the same scenarios.
  3. **/faction answers** last, if ever.
- **Decide on evidence:** the spike reports tokens per second, stall counts and gate rejection rate on the owner's PC and one weak PC.

---

## 6. Movement and interactions (one page)

**Is there one place that decides what an idle bot's body does? No. The same shape as chat.**
- **Two idle systems that overlap:**
  - `AutonomousBotBodyLanguage` (look, emote, shuffle, greet, cheer). Its own header says "Bots can't jump or sit" (`AutonomousBotBodyLanguage.cs:25`).
  - `BotMovementHabits` / `BotMovementHabitsRuntime` (overshoot, step back, bank lean, **sit while waiting**, strafe, face). It does seat bots (`GameBot.IsSeatedByHabit`).
  - Plus `BotRestRecovery` (resting), `BotTurning`, `AutonomousTownHangouts` / `AutonomousTownIdleRouting` (where to stand), `AutonomousGroupFormationFollow`, `BotLedGroups.Breaks`, the schedule's afk, and private idle code in beggars (21 move/turn/emote sites), buffbots (12), tales, the pilot.
- **Opt-in by call site again:** `AutonomousBotBodyLanguage.Idle` is called from **9 places** in the controller's state branches (6 as "Waiting", 2 "Resting", 1 "Town") and `Ambient` from 1; `WaitTurn` from 4 more. A controller state that nobody wired (between pulls at a camp, on a keep post, at a merchant, crafting, in formation on the move, in a dungeon room) gets whatever its branch happens to do, often nothing.
- **Who moves a body at all:** about 67 move/turn/emote call sites in `AutonomousWorldBotController.cs`, 51 in `BotBrain.cs`, and 40 other files. Any of them can cancel an idle action; idle code can't know.
- **Only when watched:** body language runs only with a player in sight (`NearestWatcher`). Right for cost. It means a bot "wakes up" as you arrive unless the first action is delayed and varied; I did not test that.
- **Logging:** `BOT_MOVE_*` rows are rate-limited server-wide (one per tag per gap), and `CHAT_EMOTE` covers emotes. There is no per-bot trace of what a watched idle bot did, so the owner's session can't answer "how long did that bot stand frozen".

**Target, same pattern, smaller**
- **One "body director" per bot:** asked once per tick "you are not acting; a player can see you; here is your situation (town, waiting for group, resting, at camp between pulls, at a post, in a line at a merchant, grouped with the player and standing)". It owns facing, small steps, sit/stand, emotes and glances. Controller branches report the situation; they stop calling idle functions.
- **One arbitration rule:** purposeful movement, casting and combat pre-empt; the director knows it was pre-empted and stands the bot up first.
- **Link to chat through the room:** a bot turns to who it is talking to, looks at who spoke nearby, sits to tell a tale, stands to greet. Today chat and body don't know about each other except in two special cases (ding cheer, memory greeting).
- **Measure:** a `BODY` row per visible action (bot, situation, action, watched-by), and from it "seconds a watched idle bot did nothing", "share of watched idle bots seated in each situation", "turned to the speaker within 3 s".
- **Size M. Risk low-M** (pathing side effects of small steps are the known danger; the leg-proof code in `BotMovementHabitsRuntime.ProveLegs` already guards it).
- **Settle first:** whether a gamebot can sit for a watching original client. The two files disagree. The movement agent should have the fact; the director's design depends on it.

---

## What I checked and found sound
- **`AutonomousChatExchange`:** multi-line exchanges as queues timed from the real send. Keep. `SinkHold` for /faction is a partial gate already; the gate generalises it.
- **`AutonomousChatReplyPath` + `Ports`:** the right seam for one planner and for tests without live bots.
- **`BotChatTranscript` and `chat_audit.py`:** the right base for transcripts.
- **`build_corpus.py` / `chat_rules.py` / `STRICT_OWNERS`:** the right base for the contract and for staging strictness.
- **`ChatPromises`:** the model for grounding checks (say it only if it will happen, re-checked at send).
- **`AutonomousChatWhy` codes:** the right vocabulary for held lines; only the log sink is missing.
- **Privacy:** `.gitignore` covers `**/*.log`, databases and `/runtime/`. `played_check_log.py` masks player names.

## What I could not check
- **No C# run.** No dotnet here, and my part needed none. Every behaviour claim is from reading code.
- **No owner log.** The real per-feature mix of an hour of play is unknown to me. The "few percent" in finding 9 is arithmetic from the designed pace.
- **The 223 chooser sites** are a grep; a handful are NPC yells or helper calls. The order of magnitude is right.
- **The rule-per-release table** is a comment-tag proxy.
- **Label inference** for 34.5k lines: accuracy unknown until tried on two files.
- **All local-model numbers** are unmeasured estimates.
- **Whether a bot can show as seated** on the original client.

## Measuring tools worth keeping
- **`/tmp/audit2/structure_count.sh`** (not in the repo): exits, raw sends, chooser sites, pass call sites, text rules. As a CI ratchet: raw sends outside the gate and exits missing a pass may only fall.
- **The pass table in finding 1:** regenerate it from the gate once it exists; an empty cell is a bug.

---

## Recommendation: what to build first

1. **Before the next PC session:** the gate as a pass-through inside the 17 exits (piece A step 1), the `CHAT_SAY` / `CHAT_HEAR` / `CHAT_HELD` / `SCREEN_BURST` rows, `/odd`, and `session_transcript.py`. No behaviour change. It turns the owner's session into a transcript an agent can audit line by line, with reasons.
2. **Sort the six defect lists, don't fix them yet.** Tag each finding with what would stop its class: gate check 1-6, a contract label, a knowledge provider, the planner's order, the body director, or "not chat". Expect most to land on gate checks 1-4 and the contract. Fix the truly one-off ones (a wrong fact, a bad line) now.
3. **Gate step 2-3 and the room facade** (state written by the gate). This alone closes the class behind defects 2 and 6.
4. **Contract inference and linter in shadow** on the ambient and group pools first (the 89% with no precondition).
5. **Gate checks in shadow → enforced, one at a time,** each judged on the simulated evening, the multi-turn scenarios and the owner's transcript.
6. **Then** one planner (D), one lookup (E), the body director, and only then the local-model spike.

---

## Hand-back summary

- **Verdict:** the audit method and the structure are both the problem. Six defect hunts will find instances; the design will keep producing them. Tests only; nothing run, nothing changed.
- **Counts (at `4a00ab75`):** 17 wire exits for a bot line, about 223 call sites in 40 files feeding them, about 12 player-line handlers with at least 7 different reply orderings, about 24 feature engines, at least 9 separate "what was said" stores, 43 call-site style applications, 293 regex + 354 literal + 207 predicate text rules in the chat files.
- **No exit applies every pass.** The /group `Speak` exit applies none of pace, state or promise checks; that exit caused the "where is that" defect.
- **Lines carry no channel, addressee or claims.** 34,501 lines; 89% of ambient lines have no precondition. Channel fit is a deny-list after the pick.
- **The bench:** 92% /faction, single turn, questions from the code's own answers; holdout 60/60 /faction by the same generator. One of the owner's nine defects was within its view.
- **1.2.3 so far:** 9 seen defects → 2 new modules, about 10 new predicates, 5 new test files. Correct, and each local to one path.
- **Target, incremental:** (A) one say gate with seven fixed steps; (B) one conversation model per audience, written by the gate, with a per-player window view; (C) a line contract checked at build and run time; (D) one reply planner for all channels and for bot-to-bot; (E) knowledge as one lookup that returns facts. Each starts as a pass-through or shadow.
- **Delete:** voices-off chat, raw sends outside the gate, duplicate fact records, fallbacks that speak, the absorbed stores.
- **Measuring:** gate-written log rows (said, heard, held, system bursts), an `/odd` flag command, a transcript tool with masked player names, defects per hour per build, whole-transcript scoring, a red-team reader without code, holdouts from the owner's real lines by a different author, multi-turn scenarios. CI gates: contract linter, raw-send ratchet, bench, scenarios, simulated-evening audit. Reports: everything from the owner's PC and any model-based judge.
- **Local model:** against for now. Gate, state and lookup are prerequisites either way. Later, opt-in only: first as a reader of the player's line, then free text in tells and /group. All hardware numbers are unmeasured estimates; a spike must measure.
- **Movement:** same shape. Two overlapping idle systems that disagree on sitting, idle behaviour wired at 14 call sites in controller branches, no per-bot trace. Target: one body director fed the bot's situation and linked to the room.
- **Build first:** the pass-through gate plus the new log rows and transcript tool, before the next PC session. Then sort the six lists by the gate step that would stop each class, and fix classes.
- **For the main session to consider saving:** none of this is from memory; all from the repo at `4a00ab75` and `/tmp/audit2/BRIEF.md`.
- **Files:** report `/tmp/audit2/structure.md`; counts script `/tmp/audit2/structure_count.sh`; raw lists `/tmp/audit2/structure_sinks.txt`, `structure_choosers.txt`, `structure_chattypes.txt`, `structure_state.txt`; worktree `/home/claude/a2-structure` (detached, no branch, nothing pushed).
