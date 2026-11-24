# Realism audit 1.2.3 — channels: the live reply path of every chat channel

- **Everything here is tests only.** Code reading plus three report-only tests run in CI. Nothing was seen in a played world, except where a line says "owner's screenshot".
- **Build audited:** `4a00ab75` (`fix/1.2.3-faction-polish`, main + #469). #469 has since merged; main `5346e5f7` holds the same chat code.
- **Draft PR (do not merge):** #470, branch `audit/1.2.3-channels`. CI runs 45290469765 (commit `b5a32b4c`) and 45293157089 (commit `79e2352c`), both green. No game behaviour changed.
- Paths are under `source/server/GameServer/` unless they start with `commands/` or `tools/`. `Coord` = `bots/autonomous/AutonomousBotChatCoordinator.cs`.

## The one-paragraph answer

The 1.2.2/1.2.3 fixes live in two shared places: the **knowledge lookup** (`AutonomousChatKnowledge.TryAnswer`) and the **reply path** (`AutonomousChatReplyPath`). Every channel that calls the knowledge lookup answers a fresh factual question about as well as /faction does (86–89% answered against 88%). The defects are in what sits **around** it, which each channel wrote for itself: who answers, how fast, what happens when nothing is known, and what is remembered from the line before. Three channels never reach the shared code at all (a hired companion in /say, /trade, and the silent ones).

## Top 10 findings, worst first

### 1. A hired companion in /say answers every line by its intent alone
- **What a player sees:** "Pannax where is the cleric trainer?" → "sure Wynn". "can someone rez me" → "sure Wynn". "add!" → "sure Wynn". "back" → "sure Wynn". "where should i level at 3?" → "Point me at the next one."
- **Where:** `mode165/CompanionChat165.cs:272-326` (`Reply` → `ReplyTo`): three commands (quiet, speak, "how are you"), then one fixed line per intent; the default is "k" / "ok" / "sure {who}".
- **How often:** bench, 417 questions: **3% good** against /faction's 90%; 77% off-topic, 20% fluff. Top replies: "sure wynn" 51, "ok" 48, "im here, what do u need" 52, "lead on, im behind u" 55. Lines of the moment: 0 of 40 fit by more than chance.
- **Also:** `Coord:1766` calls the companion first and then carries on to the gamebots, so a companion's "ok" (1.2–2.4 s) can be followed by a nearby gamebot's real answer. The same companion answers **/group and tells through the gamebot path** (`AutonomousBotGroupChatter.cs:226`, `CompanionChat165.cs:72`), so it knows the trainer in /group and says "sure Wynn" in /say.
- **Class:** one bot, two brains; a channel kept its pre-voices reply path.
- **Fix:** non-formal companions answer /say through `ReplyChoiceToPlayer` (addressed, channel "say"), keeping the three commands. Formal companions keep `ReplyTo`. Stop the gamebot path when a companion took the line.
- **Size S–M. Risk low:** formal speech untouched; the companion gains "dunno" lines (see 3).

### 2. Follow-ups lose their subject
- **What a player sees** (place in play: the player just asked about the Barrows):
  - "what lvl is it?" → /faction silence, /group "no idea", tell **"32"** (the bot's own level).
  - "whats there?" → silence / "no clue, sry" / "no clue sry".
  - "is it good xp?" → "The shade of Uther walks the barrows still…" (completed, then lore took it).
  - After "thinking of rolling a minstrel": "who trains them?" → "Camelot, your **armsman** trainer is there." "what spec for one?" → "Max polearm, rest in shield." "are they good solo?" → "Try Cornwall, thats where the 24s are right now."
  - After "who is the green knight?": "where is he?", "what lvl is he?", "what does he drop?" → nothing or "dunno" in every channel.
- **Where:** `bots/autonomous/AutonomousChatConversation.cs:240-271`. `Resolve` knows 9 anchored phrasings for a place ("how do i get there", "where is it/that", "is it far", "which way", "any good"…) and "how much / can you make" + it/that/one for an item. The conversation holds `ClassName` and `Level` (lines 232-235) and nothing ever uses them. There is no person, mob or NPC in play at all.
- **How often:** 54 follow-up phrasings (report-only test): memory completed **18 (33%)**. Read by hand, about **11 of 54** end in a good answer in /faction: place 10–12 of 35, item 0 of 8, class 0 of 5 (3 answer for the wrong class), person 0 of 6. By tell, 34 of 54 end in "no clue sry" or the like.
- **Class:** context held but not used; phrase list instead of a rule (the same defect as C4 in the 1.2.2 audit).
- **Fix:** replace the phrase list with pronoun substitution: "it / that / there / one / them / they / he / him / she / her / that place" → the newest thing in play of the right kind (place, item, class, named NPC or mob), when the line has no noun of its own. Add `Npc` to `Conversation` (from the lore bible's names and the knowledge answer's subject). Completed level questions go to `AnswerWorld` first (they already do once the place is in the line).
- **Size M. Risk medium:** a wrong substitution is worse than none; substitute only when the line names nothing itself, and pin with the 54 rows.

### 3. "Dunno" is the answer wherever the bot is addressed or grouped
- **What a player sees:** in a group, "can you pull?" → "Dunno". "what now?" → "no clue, sry". "which way?" → "Not sure tbh". "need that drop?" → "Dunno". "how much longer can you stay?" → "Not sure tbh". By tell or by name: "not sure, try /advice" (and /advice is silent on the same question).
- **Where:** `AutonomousChatReplyBudget.cs:92-99` (`DunnoShare`: group 0.9, guild 0.65, say 0.5, faction 0.3) and `:370-371`; a line said to the bot takes `ToContent` `:197-198` every time; `AutonomousChatReplyPath.cs:292` (`MaySayNotSure`) is false only for /faction. F4 took "dunno" out of /faction; 1.2.3 took it out of where-is questions (`:334`). Everything else keeps it.
- **How often:** bench, the questions no lookup knows: /faction 0 non-answers (22 silent), /say 5, /gu 8, **/group 14, by name or tell 24 (6%)**. Follow-ups: /group 18 of 54, tell 34 of 54. Lines of the moment: /group 6 of 40, tell 8 of 40.
- **Owner's screenshot 2** ("not sure tbh" from a groupmate) is this class; only its where-is case was fixed.
- **Class:** a rule fixed in one channel and left in the others.
- **Fix, in order:** (a) questions about the bot or the group ("can you pull", "need that drop", "how long can you stay", "what now") get answers from the bot's own state — most are already facts it holds (class, role, bags, session); (b) one "dunno" per conversation at most, then silence; (c) drop "try /advice" unless /advice would answer.
- **Size M (a), S (b, c). Risk low.**

### 4. In /group, where you are and what you are doing draws silence
- **What a player sees:** "im by camp station", "im at the bridge", "omw", "be there in 5", "rdy", "oom", "need to rest" in /group → **nothing**. "pull" → "lol". "loot is yours" → "kk will try". By tell the first three get the 1.2.3 meet-up answer ("I'm still out in Lyonesse… Inv when your close.").
- **Where:** `AutonomousChatMeetup` is wired into the tell only (`Coord:2182-2186`, `AutonomousChatReplyPath.cs:236`) and into `ToContent` for a line said to the bot with a bot line before it (`AutonomousChatReplyBudget.cs:204-208`). `OnPlayerGroupChat` (`AutonomousBotGroupChatter.cs:215-333`) has no step for it. "oom" and "rdy" are intent Unknown, so nothing fits.
- **How often:** 40 lines of the moment in /group: 8 silent, 6 "dunno", 2 misread; about 18 fit. A group is where these lines are typed.
- **Owner's screenshot 1** ("I'm by camp station" → "lol") was fixed for tells only.
- **Class:** fix applied to the channel of the report, not to the class.
- **Fix:** run the meet-up step in /group (a groupmate that is not beside the player answers where it is; one beside the player says "k"), and give "oom / rdy / need rest / med" a group answer set ("k, medding too", "np take ur time"). "pull" in /group is an order to the puller, not banter.
- **Size S–M. Risk low.**

### 5. Each channel types at its own speed
- **What a player sees:** a groupmate standing next to you takes 15–30 s to answer what /faction answers in under 12. A guildmate types a 120-character answer in 2 seconds.
- **Where and how much:**
  - **/group:** `AutonomousBotGroupChatter.cs:269` and `:321`, flat 40 wpm, no cap, no quick-fact speed. From the bench's 305 knowledge answers (median 41 characters): **median 14.5 s, 62% over 12 s, 26% over 20 s.**
  - **/gu (bot guilds):** `AutonomousBotGuildSocial.cs:369`, 2–4.5 s whatever the length (about 150 wpm at the median).
  - **/gu (gamebots in the player's guild):** `AutonomousBotGuilds.cs:383`, 1.4 s named, 1.8 s + 2 s each otherwise.
  - **Companion /say:** 1.2–2.4 s (`CompanionChat165.cs:237`).
  - **/advice, /chatgroup:** typed at the bot's speed + 2 s / 1.5 s, no cap (`Coord:1895`, `:1957`).
  - **/faction, /say, tell, /broadcast:** the only ones with the 12 s cap (`Coord:1991`, `:2079`, `:1971`); F11's fast short facts are /faction and /say only (`Coord:2979`).
- **Class:** a pacing rule that exists in one channel and not in the others.
- **Fix:** one `AnswerDelay(bot, incoming, reply, route, directed)` used by every handler: the bot's own wpm, quick facts faster, 12 s cap for a line said to the bot or in a group.
- **Size S. Risk low.**

### 6. "why? / how? / what? / who? / which?" after a bot's line get stock lines that ignore the line
- **What a player sees:** bot: "took the horse to humberton" → "how?" → "with a lot of potions". Bot: "heard they lost the relic" → "who?" → "some guy from my old guild". "what?" → "nm, wrong channel" / "typo, nvm". "which?" → "the name escapes me lol".
- **Where:** `Coord:1599-1617` (`FollowUpReply`): only "where" reads the bot's line (`WhereIsIt`, `TryAnswerTownNamedIn`). Every other word draws `@answers followup_<word>` from `data/chat/followups.lines` (327 lines), filtered by the line's family at best.
- **How often:** every such follow-up in /faction, /say, /group and tells. Not measured (needs a prior bot line; the bench has none).
- **Class:** context lost; a last-resort pool used as the first resort.
- **Fix:** field lines already carry their facts (`AutonomousChatFieldTalk.Said`: kind, place, item, mob). Keep them on the thread log entry and answer "who / what / which / how" from them; fall back to the pool only for written lines.
- **Size M. Risk low.**

### 7. A question that mentions a group is read as "invite me" outside /faction
- **What a player sees:** in /b or /cg, "what does lfg mean?" → "32 here, the xp would be awful for you. Try someone closer to 8." In /say or by tell, "do i need a group for my epic?" → "I'm only 32, I would just die out there with you." "how do i invite someone to a group?" → a join answer.
- **Where:** F6 (`AsksAboutGrouping`) guards only the group-post branch (`AutonomousChatReplyPath.cs:154`). The join step runs before it and before the knowledge lookup in `Plan` (`:149-151`, /say and named lines), `Tell` (`:230`) and `OnPlayerOpenChannel` (`Coord:1919`).
- **How often:** 3 of 417 in /b and /cg, 2–3 in /say and tells. Small, but each is a flatly wrong answer.
- **Fix:** `AsksToJoinGroup(message) && !AsksAboutGrouping(message)` in those three places. **Size S. Risk low.**

### 8. Channels where nobody is home: /trade talk, /yell, emotes, /as, /whisper
- **What a player sees:**
  - **/trade:** "anyone know what a 99% cloth cap goes for?" → nothing. A bot answers the player's WTS with "how much?", the player says "5g" → nothing, ever. Bench: **401 of 417 lines silent**; only a WTS/WTB post draws a reply, 35–40% of the time (`Coord:1369-1398`, `bots/autonomous/AutonomousChatMarket.cs:36-56`). No thread, no memory, no screen. `PostOf` reads "do i need a group for my epic?" as WTB ("need a").
  - **Emotes:** /wave, /bow, /hug, /slap, /rude at a bot → nothing. Only /beg at a beggar is read (`commands/playercommands/emotes.cs:364-367`). Bots emote on their own (`AutonomousBotBodyLanguage.cs`), so the gap shows.
  - **/yell:** nothing; only beggars listen (`commands/playercommands/yell.cs:89-91`). A yell for help beside a bot group draws no reaction.
  - **/as:** bots talk in [Alliance] (`AutonomousBotGuildSocial.cs:649`, `:682`) and never answer there (`commands/playercommands/guildchat.cs:103+`, no hook). Same for /o.
  - **/whisper** to a gamebot: nothing (`bots/GameBot.cs:3235`, companions' hire dialog only).
- **Class:** a channel the bots speak in or stand in, with no ear.
- **Fix:** emote back from the targeted bot (wave→wave, bow→bow, rude→rude; **S**). /yell and /whisper → `OnPlayerLocalChat` with the yell radius / the target as addressee (**S**). /trade: thread reply for the bot's own "how much?" and the knowledge lookup for a price or where-to-sell question (**M**). /as → the guild handler with the alliance's speakers (**S**).
- **Risk low**, all additive.

### 9. Memory holes by channel
- **What a player sees:** in /group, "where is spindelhalla?" (answered), then "how do i get there" → "dunno". A bot tells you "want to duo? im in avalon marsh tho, come find me" → you answer "how do i get there" → "no clue sry". In /gu, "why?" after a guildmate's line → a stock line or nothing.
- **Where:**
  - **/group:** `AutonomousBotGroupChatter.Send` (`:373-388`) writes the thread log, not the conversation (`NoteBot`), so no bot line there sets the place or the floor, except the leader's meet-up line (`BotLedGroups.cs:1052-1057`). The knowledge branch (`:266-272`) answers without `NotePlayer`, so a place the player asked about **and got answered** is not kept; one that drew no answer is (`Coord:3820`).
  - **Bot-started tells** (`bots/autonomous/AutonomousLivingServer.cs:410-421`: duo offers, the /lfg answer, the too-far reply, open-channel answers): thread log only. Only the reply inside `OnPlayerTell` notes the conversation (`Coord:2085`).
  - **Ambient bot lines in /faction and /say:** `Note` records the conversation only for a line said **to** the player (`Coord:1414`). "anyone for spindel tonight?" → "how do i get there" has no place. ("where is that" works there, through the thread log.)
  - **/gu, /advice, /b, /cg, /trade, /lfg:** no conversation memory and no `TryThreadReply` at all.
  - **Across channels:** memory is keyed by player + channel. A place named in /lfg, /trade or a tell is unknown in /faction.
- **Fix:** `Send` and `Tell` call `NoteBot`; the group knowledge branch calls `NotePlayer`; a per-player "last place heard in any channel of the window" as the fallback for `Resolve`. **Size S–M. Risk low.**

### 10. /gu is two different channels
- **What a player sees:** in a bot realm guild, one styled answer after 2–4 s. In the player's own guild with invited gamebots: a question typed within 10 s of the last reply is dropped; two guildmates can both answer one factual question in different words; replies land in 1.4–1.8 s.
- **Where:** `commands/playercommands/guildchat.cs:56-58` calls both. `AutonomousBotGuildSocial.OnPlayerGuildChat` (`:327-437`) has the screen, the question exemption from the cooldown, one answer for a non-social line. `AutonomousBotGuilds.OnPlayerGuildChat` (`:365-389`) has a flat 10 s cooldown with no exemption (`:370`), `RollResponseCount(min 2)` with each bot running its own lookup, fixed delays.
- **Also in both:** the knowledge lookup runs before the short-talk check; no thread, no memory (finding 9); "finally 50!!!" → nobody (kind Banter, count 0 on the roll) where /faction says "gz".
- **Fix:** `AutonomousBotGuilds` passes its talkers to the GuildSocial handler. **Size S. Risk low.**

### Smaller, same classes
- **Beggars hear private channels.** Asking for coin in /gu, /group, /trade, /lfg or in a **tell to another player** marks the player, and in a capital the beggars come ("word travels fast"): `commands/playercommands/send.cs:115`, `AutonomousBeggars.cs:774-846`. Fix: mark only on /say, /yell, /b, /faction, /beg. **S.**
- **A chat group is private; a stranger answers it by tell** (`Coord:1904-1958`, channel "chatgroup": any eligible bot of the realm). **S:** drop it or limit to bots in the player's group or guild.
- **/lfg treats any text as a group ask** (`bots/autonomous/AutonomousLivingServer.Tells.cs:290-344`): a question typed in /lfg draws a duo offer or "nobody around your level".
- **A tell reads "sc or drops at 25?" as a question about the bot's gear** ("a mix, a crafted piece or two", 4 rows) where /faction gives the advice.
- **Orders in /group are acknowledged** ("follow me" → "Alright alright", "wait here" → "ok ok", "heal me pls" → "Roger", "buff pls" → "k np") from `@answers player_order`. I did not trace whether the bot then does it; if not, these are promises outside the promise gate.

## The channel table: a player's line

| channel | entry | who may answer | knowledge lookup | classifier | "no dunno" | laugh gate | pacing | last resort |
|---|---|---|---|---|---|---|---|---|
| /faction | `Coord:1699` → `SchedulePlayerReplies:2743` | gamebots of the realm, not fighting, casting, riding; not companions, helpers, beggars | yes, after short-talk and help-call checks (`KnowledgeFirst`) | `Plan` + `Choose` + `Voiced` + `Budget` | **yes** (F4) | named lines only | 12 s gap; 12 s cap on first answer; fast short facts; arbiter pile-ons; budget ceiling | silence; reaction "lol/heh/same" 30% for banter |
| /say | `gameobjects/GamePlayer.cs:7634` → `Coord:1758` | the same, within say range; the bot faced or targeted is "addressed" | yes, same | same | no (50%) | named or faced | 8 s gap; cap only when directed | "dunno" 50%; reaction 45% |
| /say, hired companion | `CompanionChat165.cs:206` | the companion named or holding the floor, else 35% | **no** | intent only | n/a | no | 8 s per companion; 1.2–2.4 s | "k" / "ok" / "sure {who}" |
| /group | `commands/playercommands/group.cs:55` → `GroupChatter:215` | gamebot groupmates **and hired companions**, alive, not fighting or casting | yes, **no short-talk or help-call check** | own branches (warning, "come here", gz), then `Voiced` + `Budget` | no (90%), where-is excepted | named only | 5 s cooldown that also drops questions; 40 wpm, no cap | "dunno" 90%; reaction 75% |
| tell | `commands/playercommands/send.cs:45` → `Coord:2016` | the bot told (a buffbot: fixed "afk buffbot" lines) | yes, after join / where / meet-up / friend / fact | `TellReply` threads, `ReplyPath.Tell`, `Budget` | no (always "no clue sry" to a question) | **yes** | queued, 12 s cap, waits 90 s for a fight with "sec, fighting" | "ya?/hm?" for an empty opener; "k/oh ok/gotcha" for a statement |
| /gu, bot guild | `guildchat.cs:56` → `GuildSocial:327` | bot members, not fighting | yes, no short-talk check | `Lines.Classify` for the count, then `Voiced` + `Budget` | no (65%) | named only | 6 s cooldown (questions pass); 2–4.5 s for knowledge | "dunno" 65%; reaction 55% |
| /gu, player's guild | `guildchat.cs:58` → `AutonomousBotGuilds:365` | invited gamebots, not fighting | yes, per bot | none; up to 2 bots | no | named only | 10 s cooldown, **drops questions**; 1.4–1.8 s | as above |
| /advice | `advice.cs:101` → `Coord:1865` | one gamebot of level 20+ | yes | `Voiced` only, **no `Budget`** | yes (needs fit ≥ specific, no brush-off) | n/a | none; uncapped + 2 s | silence |
| /broadcast | `broadcast.cs:60` → `Coord:1904` | one gamebot within 6,000 units | yes, **after** the join check | `NeedsAnswer` gate, `Voiced` + `Budget` | yes (share 0) | no | none; 12 s cap | silence |
| /chatgroup | `chatgroup.cs:55` → same | any gamebot of the realm, **by tell** | same | same | yes | no | none; uncapped | silence |
| /trade | `trade.cs:47` → `Coord:1369` | a bot the post is true for, 35–40% | **no** | `Market.PostOf` | n/a | n/a | channel ceiling; +4–14 s | silence |
| /lfg | `LFG.cs:59` → `Tells.cs:290` | a leader with room; else solo bots within 2 levels, **by tell** | **no** | none (any text is a group ask) | n/a | n/a | once per bot and player in 6 h | "nobody around your level" by tell |
| /yell, /whisper, /as, /o, /bc, /region, emotes | — | nobody (beggars read /yell and /beg) | no | — | — | — | — | silence |

- **Screen (spam, scams):** /faction, /say, /group, tell, /gu (bot guild), /advice, /b, /cg at the door; /gu (player's guild) inside the reply; **/trade and /lfg not at all**.
- **"Line to someone else" check (`OtherAddressee`):** faction, say, group, /gu bot guild, /b, /cg. Not /advice, not the player's-guild /gu when a bot is named.
- **Named busy bot waits and answers when free:** faction, say, tell. Not group or /gu (a fighting groupmate is simply left out).

## The channel table: a bot's line

| channel | who answers a bot | from what | non-answer filter | naming / hogging rules |
|---|---|---|---|---|
| /faction | field threads (`Coord:564`, `KnownAnswer:677`), written openers (`ScheduleBotReplies:3288`) | the answerer's own facts, then the corpus | **yes** (`IsNonAnswer`, `AutonomousChatFieldTalk.cs:310, 935, 959`) | asker named when lines fall between (`:823`); no opener from a bot that just spoke (`:2636`); group talk kept out (`AutonomousChatFiller.cs:34`) |
| /say | the same thread code | same | yes | **no** naming, **no** hog rule, **group-talk lines allowed** |
| /broadcast | nobody (macro repeats) | — | — | group-talk lines allowed (`AmbientLineFits(false, …)`, `Coord:299`) |
| /group | one groupmate, by the opener's `@invites` chain (`GroupChatter:186-209`) | corpus only | **no** | none; 73 of the 378 "chat" replies are "same / yep / agreed" |
| /gu | 0–2 guildmates (`AutonomousBotGuilds:393`, GuildSocial idle answers) | corpus, guild field lines | no | none |
| /lfg, /trade | one bot the post is true for + the poster's answer + "nvm full / sold" (`Coord:1269`) | real level, class, bags, seats | n/a (built from facts) | channel ceiling; one question an hour per poster |
| [Alliance] | nobody | — | — | — |
| tell | bots don't tell each other | — | — | — |

## Which fixes reach which channel

| fix | reaches | does not reach |
|---|---|---|
| F1, F2, F3, F5, F7, F8, class choice at 5, `TryAnswerTown` (all inside the knowledge lookup) | /faction, /say, /group, tell, both /gu, /advice, /b, /cg | companion /say, /trade, /lfg |
| F0 `Plan` + `Choose` | /faction, /say | everything else (own code) |
| F0 `Voiced` + `Budget` | + /group, tell, both /gu, /b, /cg; /advice `Voiced` only | the knowledge branches of /group and /gu (unbudgeted), companion /say |
| F4 no "dunno" | /faction (and /advice, /b, /cg by share 0) | /say, /group, both /gu, tell, named lines |
| 1.2.3 no "dunno" to where-is | every `Budget` channel, unaddressed | a line said to the bot ("no clue sry" stays) |
| F6 group question ≠ group post | /faction and /say group posts | the join step: /say, named lines, tell, /b, /cg; /lfg; /trade's `PostOf` |
| F9, F10, #469 no laugh after a question | every channel (shared style code) | — |
| F11 fast short facts | /faction, /say | /group, /gu, tell, /advice, /b, /cg |
| 12 s cap | /faction first answer, named lines, tell, /b | /group, /gu, /advice, /cg |
| `AutonomousChatMeetup` | tell; a line said to the bot after a bot line | /group, /say, /gu unaddressed |
| laugh gate (`IsBareLaugh`) | lines said to the bot | unaddressed /group, /say, /gu ("pull" → "lol") |
| `TryAnswerTownNamedIn` ("where?" after a bot line) | /faction, /say, /group, tell | /gu, /b, /cg, /trade, /lfg |
| `IsNonAnswer` (no bot-to-bot "dunno") | /faction and /say field threads | /group, /gu, written-opener replies |
| `SaidToTheGroup` | /faction | /say, /broadcast, /lfg, /trade |
| #469 asker named when apart | /faction bot-to-bot threads | /say; **answers to the player** in any channel |
| #469 no hogging | /faction openers | /say, /lfg, /trade, /gu, /group |

## Replies that leave in another channel than the question

1. **/chatgroup → tell**, from a bot that is not in the chat group (`Coord:1966`).
2. **/broadcast → /say** when the answer doesn't fit under the broadcast ceiling and the player is in earshot; else dropped (`Coord:1966`).
3. **/lfg → tell** (duo offer, leader's invite, "nobody around your level"). In period; fine. Any text counts.
4. **/faction → tell** when the line took a side in a storyline (`Coord:1721`), and guild requests in /faction or /say → the guild's own answer (`:1726`, `:1782`).
5. **Any channel → the beggars' /say** (see "smaller").
6. **/say → two speakers:** companion and gamebot (finding 1).
7. **"Aldwin why?"** in /say or by tell is answered about Aldwin's last line **in any channel** (`Coord:1461`, `:2131`, `LastBy`): the reply stays put, the subject comes from elsewhere. A "…?" check is limited to the same tell (`:2134`); "why / how / who" are not.
8. **Not found:** a /faction reply to a /trade line. The owner's screenshot 6 ("not atm sry" under a [Trade] post) was a /faction thread reading as the answer; #469's naming covers bot-to-bot threads only. A /faction answer to the **player** is not named when /trade or /lfg lines fall between (`HoldForPlayer` holds /faction threads only).

## Ways context is lost

- The phrase list in `Resolve` (finding 2): 36 of 54 follow-ups not completed.
- Class and level are remembered and never used; no person, mob or NPC is remembered.
- Completed questions still lose to lore: "is it good xp?" → Uther; "how do you get there from camelot?" → Merlin's barrow (the "from camelot" is dropped by the rewrite).
- "what lvl is it?" by tell → the bot's own level.
- /group: bot lines and answered questions are not noted (finding 9).
- Bot-started tells and ambient bot lines set no place (finding 9).
- /gu, /advice, /b, /cg, /trade, /lfg: nothing is kept.
- Across channels: nothing carries.
- The floor lasts 45 s (`FloorMs`), the thread follow-up window is its own constant, the conversation 5 min: a second question after 50 s goes to a stranger who has the place but not the thread.
- Follow-up words other than "where" ignore the bot's line (finding 6).
- The 5 s /group cooldown (`GroupChatter:238`) and the player's-guild 10 s cooldown drop a quick second line, question or not; /faction and /say exempt questions (`NeedsAnswer`).

## Every last-resort line, by path

| path | when | what it can say |
|---|---|---|
| `Voiced` "not sure" (`ReplyPath:348, 355, 403`) | a question nothing knew, where `MaySayNotSure` | `@answers ask`: "no idea sorry", "not sure", "dunno", "no clue", "cant help there sorry", "couldnt say" (+ "maybe ask a trainer", "try asking a guard", filtered out) |
| `Budget` → `ToContent` (`ReplyBudget:135-214`) | a weak reply to a line said to the bot | question: "no clue sry", "dunno sry", "not sure, try /advice", "no idea tbh", "hmm no clue". Statement: "ya", "k", "oh ok", "ah ok", "gotcha". After the bot's own question: "cool", "k cool", "ah ok", "nice", "k", "ok ty", "gotcha". Plus the social, sale, heal, rez, buff, level-band and meet-up short answers |
| `Budget` → `Ack` (`:21-34`) | an empty opener to the bot | "?", "ya?", "hm?", "yes?", "ya", "whats up?", "yo", "hmm?", "yeah?", "sup?"; busy: "sec", "1 sec", "sec, fighting", "sec, busy", "one sec" |
| `Budget` → `Rescue` (`:342-380`) | a weak reply to a line said to nobody | "dunno", "no idea", "not sure tbh", "idk sry", "no clue, sry", "hmm dunno"; reactions "lol", "heh", "same", "ha", "lol yeah", "ugh", "ouch", "that sucks", "rip"; "gz"; alarm "where?", "k", "ugh, again"; request truths ("cant heal sry", "use a pot") |
| `GenerateDirectedReply` (`Coord:4072`) | fallback text for a named line | "yeah?", "hm?", "whats up", "sup", "yes?", "here" — always replaced by the budget; dead in practice with voices on |
| voices off (`Coord:2485, 2547, 4072`; guild `Lines.Reply`) | `bot_voices` off | the formal lines ("Greetings. How can I help?", "Perhaps after this task.") |
| `FollowUpReply` (`Coord:1599`) | why / really / how / what / who / which | `followups.lines`: "why not", "long story", "luck mostly", "with a lot of potions", "nvm", "typo, nvm", "nm, wrong channel", "nobody you know", "some guy from my old guild", "the name escapes me lol" |
| "where?" with no place in the bot's line | | where the bot is (`WhereIAm`) |
| help call (`Coord:3081`) | the bot won't come | "cant, sry", "cant atm sry", "what level are the mobs?", "sry cant, busy here. try /lfg" |
| group post, nobody fits (`Coord:2329`) | | "nobody around your level that i know of, sorry", "quiet at that level right now, gl", "everyone's 40+ atm heh…" |
| tell "invited" (`Coord:2171`) | no invite arrived | "didnt get anything, try again?", "no invite came through, send it again?" |
| roleplay (`ReplyPath:366`) | no in-kind line | "*nods*" |
| companion (`CompanionChat165:310, 325`) | any other intent | "k", "ok", "sure {who}"; formal "Aye.", "Understood.", "As you say, {who}." |
| buffbot tell (`NoLiferBuffBots.cs:258`) | any tell | "afk buffbot", "buffbot, msg {main}", "just a buffbot :)" |

## The bench in every channel

Report-only tests added to `UT_ChatQuestionBench` on #470. Each channel's handler needs live bots, so each is read off its code and run on the bench's stub world, as the bench does for /faction. **Tests only.**

**417 /faction questions, the same words and seed in every channel** (run 45290469765, commit `b5a32b4c`):

| channel | good | answered | wrong | non-answer | off-topic | fluff | silent, answer due | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **/faction** | **90%** | 368 (88%) | 0 | 0 | 13 | 1 | 22 (5%) | 6 |
| /say | 89% | 368 (88%) | 0 | 5 (1%) | 15 | 1 | 17 (4%) | 7 |
| /group | 89% | 370 (89%) | 0 | 14 (3%) | 11 | 1 | 12 (3%) | 6 |
| /group, bot named | 89% | 373 (89%) | 0 | 24 (6%) | 12 | 2 | 0 | 6 |
| tell | 88% | 366 (88%) | 0 | 24 (6%) | 18 | 2 | 0 | 7 |
| /gu (bot guild) | 90% | 369 (88%) | 0 | 8 (2%) | 10 | 1 | 18 (4%) | 6 |
| /gu, bot named | 89% | 373 (89%) | 0 | 24 (6%) | 12 | 2 | 0 | 6 |
| /advice | 88% | 358 (86%) | 0 | 0 | 7 | 0 | 35 (8%) | 6 |
| /broadcast | 89% | 358 (86%) | 0 | 0 | 11 | 0 | 29 (7%) | 7 |
| /chatgroup | 89% | 358 (86%) | 0 | 0 | 11 | 0 | 29 (7%) | 7 |
| **/say, hired companion** | **3%** | 12 (3%) | 0 | 0 | 321 (77%) | 84 (20%) | 0 | 0 |
| **/trade** | **3%** | 0 | 0 | 0 | 0 | 0 | 401 (96%) | 2 |

- **Read this honestly:** for a fresh factual question the channels that reach the knowledge lookup are level with /faction. The bench cannot see findings 2, 4, 5, 6, 9 and 10 (timing, cooldowns, memory, the line before). It under-reports /group: the live handler's 5 s cooldown, its gz filter and its missing short-talk check are not in the mirror.
- The scorer's "good" for /say, /b and /cg small talk is a little low: silence to "hi all" in /broadcast is right and counts against it.
- /advice loses class opinions by design ("is friar good?" silent; spec 54% against 73%).

**54 follow-ups** (`Audit_FollowUpsThatLeanOnTheLineBefore`): see finding 2. **40 lines of the moment in 8 channels** (`Audit_LinesOfTheMomentInEveryChannel`, run 45293157089): see findings 1, 3 and 4.

## Checked and found fine

- The knowledge lookup's order and world facts are the same object in every channel that calls it: no channel has its own copy.
- No wrong fact in any channel on the 417 questions (WRONG 0 everywhere).
- No clunky flag in any channel: the style rules (F9, F10, no laugh after a question) are shared.
- A bot named while fighting answers when free in /faction, /say and tells, with "sec, fighting" by tell.
- Spam and scams draw nothing in every channel that answers questions.
- The reply of a thread follow-up, a join answer and a named-bot answer always goes out in the question's channel.
- "where r u", "where are you guys?", "ready?", "what lvl are you?", "whats your spec?", "afk", "back", "ty", "that was close" answer well in /group and by tell.
- /lfg and /trade bot posts are answered from the answering bot's real state.

## Could not check, and why

- **Anything live:** `SayAddressee` (facing, target), the floor, stand-ins, the budget's ceilings, `WillComeRez`, the promise gate. They need bots in a world; no server was started.
- **Whether "follow me", "wait here", "heal me pls" are carried out** after the bot says "Roger". Not traced.
- **How often group-talk lines come out in /say or /broadcast** (the filter is /faction-only by code; no count).
- **/trade and /lfg reply wording for the player's posts** (`AutonomousChatMarket.ReplyTo` needs a bot's field facts; only the gate was run).
- **Crafter commerce tells** (`OnCommerceTell`): not read.
- **Follow-up words after a bot line** (finding 6): the pools were read; no rate, the bench has no prior bot line.
- **/group timing** is computed from answer lengths and the delay formula, not measured.

## Tools worth keeping

- **`Audit_TheSameQuestionsInEveryChannel`** (in `UT_ChatQuestionBench.cs` on #470) + `/tmp/audit2/channels_work/score_channels.py`. Could become a **CI ratchet per channel** once each handler calls the shared path instead of being mirrored: until then a mirror can drift, which is the fault F0 removed for /faction. Suggested ratchet: no channel more than 2 points under /faction's good share; non-answers per channel capped at today's count.
- **`Audit_FollowUpsThatLeanOnTheLineBefore`**: 54 rows; ratchet candidate now ("completed" count may only rise; no reply may name a class other than the one in play).
- **`Audit_LinesOfTheMomentInEveryChannel`**: 40 lines × 8 channels; needs must/wrong facts per line before it can gate. Worth growing to about 100 lines and adding to `player_questions.json` as a "moment" intent in channel group.
- The CI step "Chat channel audit rows" prints `CHAT_CHANNEL_ROW`, `CHAT_CONTEXT_ROW`, `CHAT_MOMENT_ROW` to the job log.
- Raw rows: `/tmp/audit2/channels_work/job.log`, `job2.log`, `context.jsonl`, `moment.jsonl`, `channels_score.md`.

## Hand-back summary

- **Scope:** every chat channel's live reply path, for a player's line and a bot's line. Tests only. Build `4a00ab75` (= main `5346e5f7` chat code). Draft PR #470, not to merge; nothing in the game changed.
- **Main result:** channels that reach the shared knowledge lookup answer a fresh question as well as /faction (86–89% against 88%). The defects are in each channel's own wrapper.
- **1. Companion in /say:** 3% good; "sure Wynn" to anything. Uses its old by-intent replies; the same bot uses the real path in /group and tells. S–M.
- **2. Follow-ups:** 18 of 54 completed, about 11 answered well; class and person never; "what lvl is it?" by tell → the bot's own level. M.
- **3. "Dunno":** removed from /faction only. /group 90% of unknown questions, tell and named lines always. M/S.
- **4. /group ignores "im at the bridge", "omw", "oom", "rdy"** (8 of 40 lines silent, 6 "dunno"); the 1.2.3 meet-up fix reached tells only. S–M.
- **5. Typing speed differs by channel:** /group median 14.5 s, 62% over 12 s; /gu 2–4 s for any length; companion 1–2 s. S.
- **6. "how? / who? / what?" after a bot line** draw stock lines unrelated to it ("nm, wrong channel"). M.
- **7. F6 gap:** "what does lfg mean?" in /b → a level-gap refusal; same in /say, tells, /cg. S.
- **8. No ear:** /trade talk (96% silent), emotes at bots, /yell, /as, /whisper. S each, /trade M.
- **9. Memory holes:** /group bot lines and answered questions not kept; bot-started tells set no place; /gu, /advice, /b, /cg keep nothing; nothing crosses channels. S–M.
- **10. Two /gu handlers** with different rules; the player's-guild one drops questions and can double-answer. S.
- **Also:** beggars react to private tells and guild chat; a stranger answers a private chat group by tell; orders in /group are acknowledged without a check that they are done (not traced).
- **Fix tables in the report:** which of F0–F11, meet-up, town, class choice, `IsNonAnswer`, `SaidToTheGroup`, #469 reach each channel; every cross-channel reply; every last-resort line.
- **One structural fix covers 1, 3, 4, 5, 7, 9, 10:** every handler becomes ports into `AutonomousChatReplyPath` (as F0 did for /faction), with one delay function and one memory write. Then the per-channel bench is real and can ratchet.
- **Not checked:** anything needing a live world; /group timing is computed, not measured.
