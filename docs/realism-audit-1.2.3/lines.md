# lines: every written chat line and every sentence built in code (realism audit 1.2.3)

**Everything here is tests only.** Nothing was run on a server or seen on the owner's PC. I ran no C#; I opened no PR. All counts come from Python scripts over the `.lines` sources and from reading the code. Rates are worked out from the constants in the code and `chat-budget.json`, not measured.

- **Source read:** worktree `/home/claude/a2-lines`, branch `fix/1.2.3-faction-polish` at `4a00ab75` (main + PR #469). No file changed.
- **Corpus size:** 86 `.lines` files, 9,178 line templates (34,515 ways once `[a|b]` parts expand) and 565 scenes with 8,398 beats.
- **Read in full (30 files, about 5,000 raw lines):** replies, replies_113, replies_more, replies_pools, replies_pools_more, replies_invites, replies_short_chat, replies_player, followups, tells, memory, memory_more, group_talk, group_chatter, bot_led_groups, lfg_grouping, lfg_parts, lfg_real, closings, living_server, moments, camp_more, extras, ambient_leveling, personalities, experience, capitals, dungeons, rvr, rvr_more. Also all 299 ambient/rvr/camp templates the runtime filters let through freely.
- **Sampled by hand (25-50 templates each, 17 scenes of 565):** ambient_classes_more, ambient_life_more, ambient_realms_more, ambient_realms_balance, guild_chat, guild_chat_more, guild_more, classes, classes_more, replies_classtalk, replies_parts, closings_more, news, news_replies, combat_calls, and scenes from gen_*, storylines*, debates_general, dunce, threads_long, scenes_live, craft_drama, tales_more.
- **Scripts only, not read line by line:** beggars, roleplayers, spammers, deeds, bonds, rumours, milestones, holidays, craft_commerce, bot_trade, charity, lore_*, tales, battleground*, era_rumours, more_realms, trade_crafting.
- **Code read:** `AutonomousChatFieldTalk.cs` (every generator, the stories, `Help`, `ClassTips`), `AutonomousBotVoiceCorpus.cs` (pick, style, no-repeat), `AutonomousBotVoices.cs` (style entry points), `AutonomousChatIdiolect.cs`, `AutonomousChatFiller.cs` (the runtime filters), `AutonomousLivingServer.Tells.cs`, `AutonomousBotGroupChatter.cs` (first half), `BotLedGroups.cs` fallbacks, `GameBot.cs` companion lines, and the string literals of 55 chat and bot files by script.
- **Code not read:** `AutonomousChatShortTalk.cs`, `AutonomousChatKnowledge.cs`, `AutonomousChatQuestions.cs`, `AutonomousChatGuide.cs`, `AutonomousChatMarket.cs`, `AutonomousChatThreads.cs`, the guild `Lines` class, `AutonomousBotDebates.cs`, `AutonomousChatNews.cs`. Their literals were only searched for later-era words.

## The 10 findings that most break the illusion, worst first

### 1. The bots have two voices, and the written one is a writer's
- **What a player sees:** one bot types `lf1m heal vendo pst`. The next types `tanking is simple: face it, hold it, trust the healer` or `nobody remembers the xp, everyone remembers the group`.
- **Where:** code-built lines in `AutonomousChatFieldTalk.cs` against the written ambient files, mainly `ambient_classes_more.lines`, `ambient_life_more.lines`, `ambient_realms_more.lines`, `ambient_realms_balance.lines`, `classes*.lines`, and every scene file.
- **Measured (script `voice_metrics.py`, per template):**

| source | templates | median words | 10+ words | has a chat abbreviation | general saying, no "i/you", no slot |
|---|---:|---:|---:|---:|---:|
| FieldTalk literals (code) | 1,121 | 3 | 0% | 42% | 2% |
| ambient_classes_more | 452 | 10 | 63% | 34% | 71% |
| ambient_realms_more (ambient) | 391 | 11 | 73% | 28% | 54% |
| ambient_life_more | 505 | 10 | 52% | 13% | 32% |
| ambient_realms_balance | 100 | 10 | 51% | 17% | 65% |
| gen_alb / gen_mid / gen_hib threads (beats) | 1,594 | 8-9 | 37-45% | 21-25% | 29-33% |
| tales, tales_more (beats) | 1,124 | 11-12 | 63-67% | 2-3% | 47-53% |

- **The runtime already knows.** `AutonomousChatFiller.IsCrafted` (line 276) hides any ambient line over nine words, any joke, and 39 whole topics. Mirrored in `reach.py`: of 3,012 ambient templates, **235 (8%) are freely sayable**, 2,138 (71%) sit behind a 12% draw on the one opener in five that goes to the corpus (about 2.4% of /faction openers, roughly 6 lines an hour at 4 openers a minute), and 631 lore lines are never an opener. So most of the corpus is dead weight, and the defect is patched with substring lists in C# (`" work"`, `"like a "`, `"family"`) instead of being fixed in the lines. `" work"` also blocks `this camp works for me`.
- **What still gets through:** the free 235 hold the owner's screenshot lines and their kin: `nothing humbles you like dying to something [blue|green]` (ambient_life_more:96), `the scariest words in the game: it has friends` (:99), `the group that wipes together laughs together, eventually` (:205), `ludlow never changes, i find that comforting` (ambient_realms_more:197). Scenes are not filtered at all, and one plays every 30-45 minutes for 4-8 minutes.
- **Class:** tone and period (d).
- **Fix:** rewrite the 299 free ambient/rvr/camp templates to the target voice first (sample at the end). Then add the length and shape rules below to the builder so new lines cannot bring it back. Then decide what to do with the 2,138 gated lines: cut them or rewrite in batches.
- **Size:** M for the 299, L for the rest. **Risk:** none to behaviour; a smaller pool repeats sooner (finding 6), so rewrite rather than delete.

### 2. One reply pool serves every channel, so a pile-on line becomes the whole answer
- **What a player sees:** the owner's tell: player `I'm by camp station`, bot `lol`. Also a follow-up `why?` answered `habit i guess`, `which one?` answered `the first one you come to` (followups:254), `where?` answered `i got lost there too` (replies_invites:232).
- **Where:** `@intent` and `@answers` pools in `replies*.lines` and `followups.lines`. `Candidates` (`AutonomousBotVoiceCorpus.cs:239`) filters by kind, intent and answers only. A line has no way to say "never as the only answer" or "not in a tell".
- **How often:** of 2,527 reply and closing templates, **227 are pure filler and 16 are non-answers** by the repo's own scorers. `followups.lines` is 202 templates and nearly all are evasions by design. The `@answers ask` set is 8 lines, all "no idea".
- **Class:** channel fit (a), fluff (c).
- **Fix:** a `@fit` tag on reply lines: `pileon` (only after another bot answered, never in a tell, never when the bot was named), `direct` (may stand alone). The builder defaults a reply of three words or fewer with no slot to `pileon`. The picker passes the moment.
- **Size:** M. **Risk:** tells and named bots go silent where no direct line exists. That is better than `lol`, but pair it with the context answers from #465-#467.

### 3. Group talk and personal status still reach /faction
- **What a player sees in /faction:** `this group reminds me of the old days` (experience:143), `i swear we passed that torch already` (dungeons:14), `pull to the hallway, not the room` (dungeons:39), `someone remember the way back in case we wipe` (dungeons:38), `late night groups are the best, nobody rushes` (ambient_life_more:381), `ty group, that ding was a team effort` (ambient_life_more:712; the `[all|group|everyone]` part gives a way the word list misses).
- **Where:** #469's `SaidToTheGroup` (`AutonomousChatFiller.cs:45`) blocks topic `group` and `in_group` lines containing `everyone|all|guys|lets|you all|yall|ur|you`. A group line that says `we`, `this group` or gives an order passes. Script count: **8 freely sayable templates** and 11 gated ones.
- **Also from code:** `AutonomousChatFieldTalk.Social` (line 1782) and `Status` (line 1616) feed /faction with `brb phone`, `afk 5`, `brb drink`, `back`, `ok back`, `sitting, need hp`, `sitting for pow`, `back to {monsters}`, `more {monsters} lol`. By the weights in `Faction` (line 1112) these two plus the bubble count are about 4 of 20 for a levelling bot at a camp, before the family rests thin them. Nobody tells a realm of hundreds they are getting a drink.
- **Class:** channel fit (a).
- **Fix:** a `@channel` tag on lines (default from kind; any `in_group` ambient line is `say,group` unless it says `faction`). In code, give each `V` family a channel list and keep `brb`, `afk`, `back`, `sitting` to /say, /group and /gu.
- **Size:** S. **Risk:** /faction loses volume against the owner's 5-7 a minute; fill with questions and group posts, which the same function already makes.

### 4. Style habits are stuck on by dice, not by person
- **What a player sees:** `ouch, sorry, gl :D` after they say they died. `your trainer in camelot :P`. One bot ends lines with `:)`, then `haha`, then `:P`, then `heh`.
- **Where:** `voices.json` "chatty" (20% of bots): 35% of eligible lines get one of six emotes drawn fresh each time (`AutonomousBotVoiceCorpus.Style`, line ~880). `TakesEmote` (line 755) exempts kinds and a "serious" word list, but the list has no `sorry`, `ouch`, `ugh`, `sucks`, `bad luck`.
- **Measured (`voice_style.py` mirror):** `reply` with `@intent rez`: 77% of 35 lines can take an emote and **52% of those are condolences**. `session` lines (`gn all`, `brb kids`) 100% eligible. Code-built answers go through `StyleFor` with no kind, so any answer without a "serious" word is eligible.
- **Second habit:** `AutonomousChatIdiolect` ShortWords (17% of bots) turns `you/your/are/people/because` into `u/ur/r/ppl/cuz` in any lower-case line, long ones included: `u have the patience of a saint with these pulls`. 20% of the 4,878 lore ways hold such a word, and lore answers pass through it (`StyleText`, `AutonomousBotVoices.cs:716`).
- **Class:** style habits (e).
- **Fix:** each bot gets one or two emotes from its id, used on whitelisted intents only (banter, joke, a boast's reply, greeting). Never on `StyleFor` answers. Skip the idiolect on lines of ten words or more and on lore.
- **Size:** S. **Risk:** fewer smileys overall; `UT_ChatVoice*` expectations will need new values.

### 5. Groupmates praise the player without cause
- **What a player sees in /group, minutes apart:** `you have the patience of a saint with these pulls` (group_talk:33), `this is the most fun i have had all week` (:24), `glad i grouped with you, honestly` (:25), `good group and a good leader, cant ask for more` (:32), `best group i have had all week honestly` (group_chatter:35), `anyone want to stay in touch after this?` (:36).
- **Where:** `group_talk.lines`, `group_chatter.lines`, plus greetings and gossip in `memory.lines` and `extras.lines`: `{player}, my favorite {player_class}, hi` (memory:14), `{player} is the kind of {player_class} you hope shows up when things go wrong` (extras:162).
- **How often:** 45 praise templates. In the player-group pool they are about one line in six.
- **Class:** fluff (c).
- **Fix:** keep three, and say them only on an event: after a rez or a save, after a ding, when the group ends.
- **Size:** S. **Risk:** none.

### 6. Small pools plus the no-repeat rule: a burst, then silence, then the same set tomorrow
- **Mechanism (read, not run):** a line is not said again in a realm for three days. When a pool is used up, `TryLine` lets a line back after 6 hours, or 40 minutes with `relax: true`. All ways of one template also rest 6 hours as a family (`AutonomousBotVoiceCorpus.cs:372-386`). The memory is per realm, not per group.
- **What that does to calls:** `lead_pull` has 10 templates (19 ways) and no relax (`AutonomousBotCombatCalls.cs:437`). A bot leader calls about 19 pulls, then `TryLine` fails and it calls none for up to 6 hours. Combat topics have 3-20 lines with the 40-minute rest: `epic_stomp` has 4, so the raid calls four stomps and then goes quiet for 40 minutes. A real player types `inc` on every pull.
- **What it does to banter:** the player's group pool is 62 templates. At the budget's 0.3-1.0 lines a minute that is one or two evenings. After that the group goes quiet, and the next evening the same 62 come back.
- **Class:** repetition (f).
- **Fix:** exempt functional calls (pull, inc, add, oom, rez, mechanic calls) from the window, as `grats` already is. Grow the sentence pools in the table below.
- **Size:** S for the exemption, M for the pools. **Risk:** the same `inc` twice in a row, which is correct.

### 7. Replies that invent a fact to sound helpful
- **What a player sees:** `where is X?` answered `past the bridge i think` (replies_invites:230), `north of town, keep going` (:231), `oh i know that one, follow the river` (:239), `i went that way earlier, watch out for adds near the bridge` (replies:115). `is the keep ours?` answered `was ours when i rode past earlier` (replies_more:137) or `yes, been watching it all night` (:141). An either-or question answered `the first one` (replies_more:47).
- **Where:** `@answers ask_where`, `ask_know`, `ask_choice`, `@intent travel` in `replies*.lines`. Also unprompted: `there is a waterfall past the hills, worth the walk just to see it` (ambient_leveling:61), `been here an hour, {monsters} respawn nice and fast` (moments:54).
- **How often:** 102 templates claim a place, a time or a history the bot does not have. In `ask_where` about 10 of 17 lines do.
- **Class:** context dependence (b).
- **Fix:** delete them. Where, who-holds and which questions are answered from world facts or not at all. Builder rule: a reply to those `@answers` must hold a slot filled from the world.
- **Size:** S. **Risk:** more silence until the knowledge answers cover the question.

### 8. Tells: a stitched question mark, a guild shout sent in private, and a tutor's voice
- **Stitch:** `WithWhere` (`AutonomousLivingServer.Tells.cs:197`, called at :336) trims the offer's end and adds `? im in {z} tho, come find me`. On a line that ends in a statement this gives `duo? i can heal, you hit things? im in avalon marsh tho, come find me`. Three of the 15 `group_offer` templates end that way (tells:74, :78, :80, the healer, tank and caster lines).
- **Wrong channel:** `hey, anyone in the guild grouping tonight?` (tells:155) is a /gu line sent as a tell to one person.
- **Tutor:** `any questions about your class? happy to help` (tells:98), `hey, remember to train when you ding` (:103), `was just telling the others about you lol, all good things` (:169).
- **Pools:** 9-15 templates per topic, and a tell is the most noticed line there is.
- **Class:** channel fit (a), stitching (e), tone (d).
- **Fix:** end the offer with `.` or `,` when it is not a question; move tells:155 to guild; cut the tutor lines.
- **Size:** S. **Risk:** none.

### 9. The companion's butler voice, and a gamebot can fall into it
- **What a player sees in /say after a rez:** `My thanks, Bob. I am ready to continue.` or `Back among the living. I will stay nearer the group this time.` On the walk back: `I woke at the bindstone. Hold the road; I will return on foot.`
- **Where:** `GameBot.cs:1400-1404` and `CompanionReturnDialogue` (`GameBot.cs:1674-1690`). `/bot` companions always use them. A gamebot uses the first set whenever `AutonomousLivingServer.OnBotResurrected` returns false (`AutonomousLivingServer.Events.cs:626, 631`: not a speaker, or death talk off), because the code then falls through to the companion's `Say`.
- **Class:** channel fit by bot type (a), tone (d).
- **Fix:** a gamebot returns before the companion line. Give `/bot` companions `GamebotReturnLine` and a plain `ty` unless their speech is set to Formal.
- **Size:** S. **Risk:** none; the Formal option keeps the old lines.

### 10. Words and joke shapes from after 2003, and two period words the linter keeps out
- **Later words in code:** `hbu` (`AutonomousChatEngagement.cs:236`), `tbh` in six places (`Engagement:231`, `Recall:242, 246`, `ReplyBudget:198, 371`), `yikes` in eight (it replaced `oof`; a bare `yikes` is as late), `^ that` and `x2` as thread chimes (`FieldTalk:1927, 1929, 1951`).
- **Later words in lines (56 templates):** `you got this` (replies_pools:205), `i feel you` (:135), `stay safe` (:224), `thats a thing` (:18), `interesting take` (replies_more:381), `what a legend` (memory:67), `nice and chill` (lfg_real:103), `i'm literally typing` (storylines_more:1001), `storytime.` (threads_long:292), `burn` (storylines_more:1649), `need before greed` (debates_general:170), `pats` for wanderers (replies:125, extras:90, followups:94), `trash mobs` (deeds:259), `broker` (crafters:18).
- **Shapes said over and over (362 templates):** `X life: a, b, c` (13), `X envy:` (10), `rule one:` and `tip:` (12), `a, b, c, repeat` (4), `young X,` / `high level X,` (84), `nothing beats` (26), `more X than a Y` (19), `right up until` (46), lines that open with the class name (337).
- **Missing period words:** `baf` appears 0 times; DAoC players said it daily. `banned.txt` bans `vault` (as the website), so 38 templates say `bank`; the game's word is vault keeper.
- **Class:** tone and period (d), facts (g).
- **Fix:** add the words to `banned.txt` and the shapes to the builder; allow `vault` unless it follows `camelot` or `the`.
- **Size:** S. **Risk:** none.

## Counts per defect class per file

One count per template (a `[a|b]` family counts once; a scene beat counts once). Columns:
- **A_shape:** an opener shaped like a reply or leaning on something unseen (`ty ...`, `that ...`, `still ...`, `so ...`).
- **A_grp_noflag:** group words (`this group`, `the healer`, `lets pull`, `guys`) with no group flag on the line.
- **B_claim:** a made-up place or a claimed history (`the bridge`, `an hour`, `last night`, `all week`).
- **C_fluff / C_nonans:** reply or closing that is pure filler, or a "dunno" (the repo's `chat_scorers`).
- **C_praise:** praise of the player or the group.
- **D_10w+:** ten words or more (lore, beggars, roleplayers, spam and storyline tells left out).
- **D_aphorism:** six words or more, no slot, no question, no "i/my/we/you/anyone": a general saying.
- **D_frame:** one of the repeated joke or sentence shapes of finding 10.
- **D_later:** a later-era word. **D_emote\*:** `*nods*`-style emote text outside roleplay (all 23 are replies to a roleplaying player, which is fair).

The regexes are coarse. Treat a column as the size of a problem, not a list of guilty lines. The script is `final_table.py`.

| file | n | A_shape | A_grp_noflag | B_claim | C_fluff | C_nonans | C_praise | D_10w+ | D_aphorism | D_frame | D_later | D_emote* |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| ambient_classes_more | 452 | 4 | 27 | 1 | 0 | 0 | 0 | 285 | 323 | 116 | 0 | 0 |
| ambient_leveling | 97 | 0 | 0 | 5 | 0 | 0 | 0 | 73 | 25 | 2 | 1 | 0 |
| ambient_life_more | 505 | 12 | 14 | 9 | 0 | 0 | 5 | 262 | 160 | 34 | 1 | 0 |
| ambient_realms | 63 | 1 | 0 | 0 | 0 | 0 | 2 | 30 | 26 | 2 | 0 | 0 |
| ambient_realms_balance | 100 | 0 | 0 | 1 | 0 | 0 | 0 | 51 | 65 | 9 | 0 | 0 |
| ambient_realms_more | 542 | 4 | 5 | 6 | 0 | 0 | 3 | 397 | 295 | 48 | 0 | 0 |
| battleground_lore | 96 | 0 | 0 | 0 | 0 | 0 | 0 | 19 | 11 | 1 | 0 | 0 |
| battlegrounds | 28 | 1 | 0 | 0 | 0 | 0 | 0 | 12 | 10 | 0 | 0 | 0 |
| beggars | 206 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| bonds | 113 | 0 | 0 | 1 | 0 | 0 | 0 | 47 | 0 | 2 | 0 | 0 |
| bot_led_groups | 85 | 3 | 0 | 0 | 0 | 0 | 4 | 11 | 9 | 0 | 0 | 0 |
| bot_trade | 38 | 1 | 0 | 0 | 0 | 0 | 0 | 5 | 0 | 0 | 0 | 0 |
| camp_more | 44 | 1 | 0 | 0 | 0 | 0 | 0 | 4 | 9 | 0 | 1 | 0 |
| capitals | 31 | 1 | 0 | 0 | 0 | 0 | 1 | 13 | 13 | 2 | 0 | 0 |
| charity | 20 | 0 | 0 | 0 | 0 | 0 | 0 | 5 | 8 | 0 | 1 | 0 |
| classes | 80 | 1 | 5 | 0 | 0 | 0 | 0 | 12 | 51 | 14 | 0 | 0 |
| classes_more | 155 | 1 | 12 | 0 | 0 | 0 | 1 | 90 | 64 | 6 | 0 | 0 |
| closings | 61 | 0 | 0 | 1 | 17 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| closings_more | 119 | 0 | 0 | 1 | 39 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| combat_calls | 456 | 0 | 0 | 1 | 0 | 0 | 0 | 25 | 0 | 1 | 0 | 0 |
| craft_commerce | 100 | 0 | 0 | 0 | 0 | 0 | 0 | 25 | 0 | 0 | 0 | 0 |
| craft_drama | 70 | 0 | 0 | 0 | 0 | 0 | 0 | 35 | 6 | 2 | 0 | 0 |
| crafters | 12 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| debates | 119 | 0 | 0 | 0 | 0 | 0 | 0 | 42 | 45 | 4 | 0 | 0 |
| debates_general | 136 | 0 | 0 | 0 | 0 | 0 | 0 | 57 | 51 | 3 | 2 | 0 |
| debates_realms | 129 | 0 | 0 | 0 | 0 | 0 | 0 | 39 | 60 | 1 | 0 | 0 |
| deeds | 434 | 0 | 0 | 3 | 0 | 0 | 0 | 148 | 0 | 3 | 13 | 0 |
| dunce | 283 | 0 | 0 | 1 | 0 | 0 | 0 | 127 | 44 | 1 | 1 | 0 |
| dungeons | 45 | 0 | 0 | 0 | 0 | 0 | 1 | 19 | 20 | 3 | 0 | 0 |
| era_rumours | 44 | 0 | 0 | 0 | 0 | 0 | 0 | 12 | 8 | 1 | 0 | 0 |
| experience | 105 | 3 | 2 | 2 | 0 | 0 | 0 | 38 | 23 | 4 | 0 | 0 |
| extras | 58 | 0 | 0 | 2 | 0 | 0 | 0 | 18 | 12 | 1 | 1 | 0 |
| followups | 202 | 0 | 0 | 5 | 0 | 0 | 0 | 1 | 12 | 3 | 3 | 0 |
| gen_alb_threads | 543 | 0 | 0 | 0 | 0 | 0 | 0 | 247 | 155 | 6 | 0 | 0 |
| gen_hib_threads | 546 | 0 | 0 | 0 | 0 | 0 | 0 | 203 | 182 | 15 | 1 | 0 |
| gen_mid_threads | 505 | 0 | 0 | 0 | 0 | 0 | 0 | 200 | 146 | 7 | 1 | 0 |
| gen_rvr_threads | 510 | 0 | 0 | 0 | 0 | 0 | 0 | 153 | 147 | 7 | 0 | 0 |
| gen_short_scenes | 547 | 0 | 0 | 0 | 0 | 0 | 0 | 59 | 110 | 4 | 0 | 0 |
| gen_social_threads | 492 | 0 | 0 | 0 | 0 | 0 | 0 | 152 | 140 | 6 | 3 | 0 |
| group_chatter | 36 | 0 | 0 | 1 | 0 | 0 | 2 | 1 | 3 | 0 | 0 | 0 |
| group_talk | 78 | 4 | 0 | 2 | 0 | 0 | 7 | 12 | 13 | 1 | 0 | 0 |
| guild_chat | 130 | 1 | 0 | 5 | 0 | 0 | 1 | 0 | 27 | 2 | 0 | 0 |
| guild_chat_more | 392 | 5 | 0 | 12 | 0 | 0 | 3 | 12 | 143 | 3 | 0 | 0 |
| guild_more | 71 | 1 | 0 | 1 | 0 | 0 | 1 | 0 | 7 | 1 | 0 | 0 |
| holidays | 144 | 1 | 2 | 2 | 0 | 0 | 0 | 55 | 43 | 4 | 0 | 0 |
| lfg_grouping | 26 | 0 | 0 | 2 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 |
| lfg_parts | 22 | 0 | 0 | 2 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 |
| lfg_real | 90 | 5 | 0 | 3 | 0 | 0 | 0 | 1 | 0 | 0 | 1 | 0 |
| living_server | 115 | 0 | 0 | 1 | 0 | 0 | 0 | 4 | 0 | 2 | 0 | 0 |
| lore_albion | 486 | 6 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 3 | 0 |
| lore_hibernia | 482 | 5 | 1 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| lore_midgard | 466 | 3 | 3 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 |
| lore_shared | 459 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| memory | 69 | 0 | 0 | 1 | 0 | 0 | 8 | 5 | 0 | 0 | 4 | 0 |
| memory_more | 26 | 0 | 0 | 0 | 0 | 0 | 1 | 5 | 0 | 0 | 0 | 0 |
| milestones | 86 | 0 | 0 | 0 | 0 | 0 | 0 | 14 | 1 | 0 | 0 | 0 |
| moments | 48 | 0 | 0 | 2 | 3 | 0 | 0 | 4 | 9 | 1 | 0 | 0 |
| more_realms | 49 | 0 | 0 | 0 | 0 | 0 | 0 | 14 | 25 | 3 | 1 | 0 |
| news | 208 | 6 | 0 | 0 | 0 | 0 | 0 | 26 | 0 | 0 | 0 | 0 |
| news_replies | 297 | 0 | 0 | 1 | 18 | 0 | 0 | 1 | 25 | 1 | 3 | 0 |
| personalities | 189 | 0 | 2 | 5 | 0 | 0 | 2 | 80 | 63 | 5 | 0 | 0 |
| replies | 104 | 0 | 0 | 1 | 11 | 3 | 0 | 13 | 17 | 1 | 1 | 0 |
| replies_113 | 87 | 0 | 0 | 0 | 12 | 0 | 0 | 0 | 4 | 0 | 0 | 23 |
| replies_classtalk | 257 | 0 | 0 | 0 | 0 | 0 | 0 | 9 | 180 | 1 | 0 | 0 |
| replies_invites | 208 | 0 | 0 | 5 | 23 | 5 | 0 | 1 | 17 | 0 | 0 | 0 |
| replies_more | 204 | 0 | 0 | 5 | 1 | 4 | 0 | 4 | 27 | 1 | 1 | 0 |
| replies_parts | 43 | 0 | 0 | 0 | 7 | 1 | 0 | 1 | 8 | 0 | 0 | 0 |
| replies_player | 88 | 0 | 0 | 1 | 22 | 0 | 0 | 1 | 10 | 2 | 0 | 0 |
| replies_pools | 203 | 0 | 0 | 1 | 22 | 0 | 0 | 13 | 40 | 0 | 4 | 0 |
| replies_pools_more | 186 | 0 | 0 | 0 | 18 | 2 | 0 | 0 | 9 | 1 | 0 | 0 |
| replies_short_chat | 265 | 0 | 0 | 2 | 34 | 0 | 0 | 0 | 39 | 1 | 0 | 0 |
| roleplayers | 143 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| rumours | 97 | 0 | 0 | 3 | 0 | 0 | 0 | 35 | 0 | 1 | 0 | 0 |
| rvr | 30 | 2 | 0 | 0 | 0 | 0 | 0 | 9 | 6 | 0 | 0 | 0 |
| rvr_more | 39 | 0 | 0 | 1 | 0 | 0 | 0 | 8 | 8 | 1 | 0 | 0 |
| scenes_live | 118 | 0 | 0 | 0 | 0 | 0 | 0 | 8 | 35 | 0 | 0 | 0 |
| spammers | 165 | 0 | 0 | 4 | 0 | 0 | 0 | 18 | 0 | 0 | 2 | 0 |
| storylines | 568 | 0 | 0 | 0 | 0 | 0 | 0 | 183 | 58 | 2 | 2 | 0 |
| storylines_more | 1177 | 0 | 0 | 0 | 0 | 0 | 0 | 327 | 178 | 14 | 5 | 0 |
| tales | 625 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| tales_more | 499 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| tells | 75 | 3 | 2 | 0 | 0 | 0 | 1 | 14 | 0 | 0 | 0 | 0 |
| threads_long | 234 | 0 | 0 | 0 | 0 | 0 | 0 | 81 | 73 | 5 | 0 | 0 |
| trade_crafting | 21 | 0 | 0 | 0 | 0 | 0 | 0 | 6 | 2 | 1 | 0 | 0 |
| **total** | 17576 | 77 | 75 | 102 | 227 | 16 | 45 | 3885 | 3300 | 362 | 56 | 23 |

## The 100 worst lines

File names are under `source/server/GameServer/bots/autonomous/data/chat/` without `.lines`. A line with `[a|b]` parts is quoted in one of its ways. Reason codes: **A** channel fit, **B** needs a fact the speaker lacks, **C** fluff or non-answer, **D** tone or period, **G** fact.

### Wrong channel or wrong moment (A)
1. experience:143 `this group reminds me of the old days`. Group talk; sayable in /faction (the owner's item 5 again).
2. dungeons:14 `i swear we passed that torch already`. Said to a group; sayable in /faction.
3. dungeons:39 `pull to the hallway, not the room`. An order to a group; sayable in /faction.
4. dungeons:38 `someone remember the way back in case we wipe`. Same.
5. ambient_life_more:381 `late night groups are the best, nobody rushes`. Same, and a saying.
6. ambient_life_more:712 `ty group, that ding was a team effort`. The `group` way passes #469's word list.
7. tells:155 `hey, anyone in the guild grouping tonight?` A guild question sent as a private tell.
8. replies:94 `try {camp}, there were people looking earlier`. Carries `@needs in_group` from the line above by accident, so only grouped bots can say it.
9. replies:84 `need a healer still?` Any class can say it, to any group post.
10. memory:152 `{player}, i'd go if we had a healer`. Said to the player even when the player is the healer.
11. replies_more:286 `talking to all of you :)` Answer to "what are you doing"; in a tell it is nonsense.
12. replies_more:253 `best thing this week? this group honestly`. `@answers ask_story` with no group flag.
13. group_talk:55 `one sec, phone`. Fine in /group; the same family in code goes to /faction (finding 3).
14. lfg_real:141 `ty for the inv, where are you all?` Kind `lfg`; one missed topic check and it is a /lfg post.

### Needs a fact the speaker does not have (B)
15. replies:115 `i went that way earlier, watch out for adds near the bridge`. No trip, no bridge.
16. replies:112 `follow the road and avoid the hills at night`. Invented route.
17. replies_more:61 `past the bridge and keep going`.
18. replies_invites:230 `past the bridge i think`.
19. replies_invites:231 `north of town, keep going`.
20. replies_invites:239 `oh i know that one, follow the river`.
21. replies_more:137 `was ours when i rode past earlier`. Answer to "is it ours?" from a bot that rode nowhere.
22. replies_more:141 `yes, been watching it all night`.
23. replies_more:132 `someone was there an hour ago`.
24. replies_more:47 `the first one`. Answer to an either-or question the bot did not parse.
25. replies_more:52 `the first, no contest`.
26. replies_more:54 `tried both, liked the second`.
27. replies_more:214 `keep raid sunday?` Invents a plan.
28. followups:59 `yeah, third time today`.
29. group_talk:72 `{zone} is all ours, nobody else down here`. Not checked against who is there.
30. group_talk:17 `almost half a bubble a kill, not bad at all`. Levels 1-20; false past the first few levels.
31. group_chatter:27 `half a bubble per kill, love it`. Same.
32. group_talk:26 `i can stay about an hour more`. A promise nothing keeps.
33. moments:54 `been here an hour, {monsters} respawn nice and fast`. The bot may have just arrived.
34. ambient_leveling:126 `got two levels at {camp} tonight, best session in a week`.
35. ambient_leveling:61 `there is a waterfall past the hills, worth the walk just to see it`. No such place is checked.
36. ambient_leveling:132 `finally made it to {zone}, the view from the ridge is something`.
37. personalities:148 `there is a spot near {zone} where the sunset is perfect`.
38. personalities:168 `there is a ruin past {zone} that nobody talks about`.
39. tells:103 `hey, remember to train when you ding`. Sent to a known player of any experience.
40. living_server:84 `take it {target}, you owe me one`. One of 5 rez-offer lines; a debt the bot never tracks.

### Fluff, non-answers, flattery (C)
41. replies:178 `interesting`.
42. replies:180 `maybe, maybe not`.
43. replies:185 `is that near {zone}?` Reply to a line the bot did not understand.
44. replies_invites:150 `ooh, tell me more`.
45. followups:15 `ask me again in an hour`.
46. followups:17 `because i said so lol`.
47. followups:254 `the first one you come to`. Answer to "which?".
48. followups:252 `the name escapes me lol`.
49. followups:125 `sorry, lag ate half my line`.
50. followups:119 `nm, wrong channel`.
51. replies_invites:256 `i asked that once, nobody knew`.
52. replies_invites:261 `good luck with that one`.
53. replies_invites:258 `whatever works for you`.
54. replies_invites:232 `i got lost there too`.
55. group_talk:33 `you have the patience of a saint with these pulls`. The code comment at `AutonomousBotGroupChatter.cs:90-91` already records it from a play test.
56. group_talk:24 `this is the most fun i have had all week`.
57. group_talk:25 `glad i grouped with you, honestly`.
58. group_talk:32 `good group and a good leader, cant ask for more`.
59. group_talk:81 `late night xp with a good group, my favorite kind`.
60. group_chatter:35 `best group i have had all week honestly`.
61. group_chatter:36 `anyone want to stay in touch after this?`
62. memory:14 `{player}, my favorite {player_class}, hi`.
63. memory:13 `evening {player}, the realm missed you`.
64. extras:148 `{player}! the best {player_class} i know`.
65. extras:162 `{player} is the kind of {player_class} you hope shows up when things go wrong`.
66. memory:58 `anyone grouped with {player}? great {player_class}, highly recommend`. Review-site wording.
67. tells:169 `was just telling the others about you lol, all good things`.
68. tells:98 `any questions about your class? happy to help`. A tutorial NPC.

### Tone and period (D)
69. ambient_life_more:96 `nothing humbles you like dying to something blue`. The owner saw the green way. Free in /faction with no death behind it.
70. ambient_life_more:99 `the scariest words in the game: it has friends`.
71. ambient_life_more:196 `nobody remembers the xp, everyone remembers the group`.
72. ambient_life_more:205 `the group that wipes together laughs together, eventually`.
73. ambient_life_more:109 `paid the healer for my con again, my wallet weeps`.
74. ambient_life_more:124 `just found out what the floor tastes like`.
75. ambient_life_more:604 `pull, kill, sit. pull, kill, sit. love this camp`.
76. ambient_life_more:462 `i keep stopping to watch the clouds`.
77. ambient_life_more:654 `love travelling between zones, a nice break from fighting`.
78. ambient_life_more:761 `that moment a rare drop hits the ground is pure magic`.
79. ambient_realms_more:197 `ludlow never changes, i find that comforting`.
80. ambient_realms_more:775 `howth again, the sea air is doing me good`.
81. ambient_realms_more:209 `these campacorentin woods creak like an old ship, creepy`.
82. battlegrounds:12 `first time in abermenai, everyone is tiny and furious`.
83. classes:55 `cleric life: heal, rez, get blamed, repeat`. A 2010s shape.
84. ambient_classes_more:1054 `tanking is simple: face it, hold it, trust the healer`.
85. ambient_classes_more:475 `healer rule one: never stand next to the tank`.
86. ambient_classes_more:455 `a shadowblade's best friend is the shadow behind the tank`.
87. ambient_classes_more:209 `friar envy: watching a cleric walk around in chain while i bonk things in robes`.
88. ambient_leveling:114 `does anyone actually like the 20s or do we all just survive them`.
89. ambient_leveling:156 `anyone want company at {camp}? i bring snacks and bad jokes`.
90. personalities:213 `life is short, pull the big one`.
91. personalities:90 `my bank is half materials, half regret`.
92. moments:112 `the forge is warm and the coin is cold`.
93. lfg_grouping:29 `healer lfg, {level}, wont let you die (much)`. Nobody joked in an lfg post.
94. lfg_grouping:34 `{level} {class} lfg, can mez, will mez, love mez`.
95. lfg_grouping:32 `tank lfg, {level}, i hold aggro and complain about it`.
96. replies_pools:205 `good luck, you got this`. Later phrase. With :135 `ugh, i feel you`, :224 `yikes, stay safe`, :18 `yeah thats a thing`, replies_more:381 `interesting take`.
97. replies:40 `sleep well, the mobs will still be here tomorrow`. With replies:18 `greetings, good hunting` and replies:21 `hello! nice night for it`.
98. storylines_more:1001 `i'm literally typing right now`. With threads_long:292 `storytime.` and storylines_more:1649 `burn`.
99. holidays:48 `happy halloween realm!` Nobody addressed "realm"; also holidays:30, :112 and gen_short_scenes:496 `good morning realm`.
100. combat_calls:164 `and stay down`. A kill call from a film; with :699 `down it goes` and :709 `catch your breath`.

### Facts (G), all minor
- experience:110 `spent more on gems this month than on my house`. Reads as game housing, which came after Shrouded Isles. Reword or cut.
- rvr:74 `we have darkness falls this week`. Access follows keep count, not weeks.
- debates_general:170 `need before greed`. A later game's loot rule.
- crafters:18 `will post it at the broker otherwise`. This server's word is the Realm Exchange.
- camp_more:33 `{monsters} are social here`. The DAoC word was `baf`.
- capitals:14 `spent ten minutes looking for the bank again`. Vault keeper; see finding 10.
- dungeons:69 `the goblins in the barrows never stop coming`. I am not sure Stonehenge Barrows has goblins; check against the world data.
- `AutonomousChatFieldTalk.cs:2582` `is there a port to {dungeon}?` A question nobody had reason to ask in 2002.

## Pools that are too small

Templates a plain speaker can use (no personality, class or realm limit). "Sentences" are those of four words or more, the ones a player notices twice. Script: `pools.py`.

| pool | templates | sentences | when it is said | what the player gets |
|---|---:|---:|---|---|
| `group` topic `lead_pull` | 10 (19 ways) | 4 | every pull a bot leader makes | about 19 calls, then none for up to 6 h |
| `group` lead topics (welcome, wait, move, break, where, handover...) | 2-4 each | 2-4 | every bot-led group | the same two or three, then the one code fallback |
| `combat` epic and mechanic topics | 3-7 each | 3-7 | each time the mechanic fires | 3-7 calls, then quiet for 40 min |
| `combat` pull / add / kill / oom / rest | 15-20 each | 0-5 | every fight | 20 calls, then each line once per 40 min in a fixed loop |
| `trade_reply` (14 topics) | 2 each | 2 | every trade window with a bot | two wordings per step |
| `rez_offer` | 5 | 3 | each rez a bot gives the player | may repeat back to back (no window) |
| `sickness` / `corpse` | 4 / 5 | 4 / 5 | each bot death near the player | one in four |
| `rez_thanks` | 6 | 5 | each rez the player gives | one in six; a healer player hears each many times a night |
| `linkdead` gone / back | 4 / 5 | 2 / 0 | each drop | fine short, but only 4 |
| `nemesis` / `revenge` | 5 / 4 | 5 / 4 | rvr deaths | repeats within a night |
| `grats` topic fifty | 4 | 3 | each 50 | fine if rare |
| `tell` hello_friend / after_group / help_offer / guildmate / group_offer | 13 / 8 / 9 / 10 / 14 | 9-14 | each tell a bot starts | a week of evenings, then the same tells |
| `reply` `@answers ask` (the "not sure" set) | 8 | 3 | any question nothing else answers | all eight are "no idea" |
| `reply` ask_level / ask_plans / ask_whereall | 8 / 5 / 12 | 2-4 | common questions to a bot | mostly unconditional lines are 2-3 |
| `reply` `@intent rvr` / travel / dungeon / grinding | 16 / 15 / 17 / 19 | 14-19 | the generic fallback for a topic | each is advice-shaped, heard again within days |
| `group`, player's group | 60 | 53 | 0.3-1.0 a minute out of combat | one or two evenings, then quiet, then the same set |
| `group`, bot groups near the player (/say) | 40 | 30 | same budget | same, and it shares the realm's memory |
| `ambient` free set (plain + situated) | 235 (727 ways) | most | up to 40 an hour in /faction, each once an evening | realm, class and level cut it to about 100 per bot; a nightly player hears the same ones every night |
| `ambient` musing | 8 (42 ways) | 8 | 5% of corpus openers | a handful of lines carry the whole share |
| `milestone` topics | 3-7 each | 3-7 | a bot nears 50 or realm rank 5 | fine if rare |
| `session` logon / logoff / brb / back | 5 / 6 / 7 / 5 | 0-2 | bots coming and going | fine short |

## Rules to add to the line format and the linter (`tools/bot-voices`)

Each rule is a build check in `build_corpus.py` or `chat_rules.py` unless marked "format".

1. **`@channel` (format).** Which channels may carry the line: `faction`, `say`, `group`, `guild`, `tell`, `lfg`, `trade`. Default from the kind. Any opener with `@needs in_group` or `in_player_group` defaults to `say,group` and fails the build if it names `faction` while holding `we`, `our`, `us`, `this group`, an imperative, or `ty`.
2. **`@fit` on replies (format).** `direct` or `pileon`. A reply of three words or fewer with no slot is `pileon` by default. `pileon` is never picked for a tell, a named bot, or as the first answer.
3. **World-fact replies.** A reply whose `@answers` is `ask_where`, `ask_know`, `ask_choice`, `followup_which`, `followup_whereto` or whose `@intent` is `travel` must hold a world slot (`{zone}`, `{camp}`, `{capital}`, `{keep}`, `{destination}`), or the build fails.
4. **No invented places or histories.** Fail on `the bridge`, `the river`, `the waterfall`, `the ridge`, `the ruin`, `north of town` in any kind but scenes and roleplay. Fail on `an hour`, `all night`, `all week`, `last night`, `third time`, `earlier` in a `CLAIM_KINDS` line unless a flag backs it (new flags: `here_long`, `died_twice`).
5. **Length.** Openers of kinds ambient, rvr, camp, group, guild, lfg: at most 9 words; replies at most 8; scene beats at most 12 (tales and lore exempt). This is `IsCrafted`'s rule moved to build time, so the C# marker lists can go.
6. **No general sayings.** Fail an opener of six words or more with no slot, no question mark and none of `i im my me we our u you anyone` (the "aphorism" test). 3,300 templates fail it today, so start it as a ratchet: the count may not rise.
7. **Banned shapes.** `^\w+ life[:,]`, `envy:`, `rule (one|number one):`, `tip:`, `, repeat$`, `^nothing (beats|humbles|like)`, `more \w+ than (a|my)`, `right up until`, `is the real (enemy|game)`, `, (mostly|probably|eventually)$`, `the \w+ way$`, three short sentences in a row (`a. b. c.`), `note to self`, a parenthesis in an lfg line.
8. **Banned words (add to `banned.txt`).** `tbh`, `hbu`, `yikes`, `you got this`, `i feel you`, `stay safe`, `thats a thing`, `interesting take`, `what a legend`, `highly recommend`, `nice and chill`, `literally`, `storytime`, `need before greed`, `broker`, `trash mobs`, `\bpats?\b` for a wanderer, `^ that`, `x2`, `realm[!?]?$` as a form of address. Change `vault` to ban only `camelot vault`.
9. **Praise cap.** Lines matching the praise list need `@needs` on an event flag (`player_rezzed_me`, `just_leveled`, a new `group_ending`), or the build fails.
10. **Minimum pool.** For every (kind, topic or intent or answers) the code asks for, at least 12 plain templates, or the pool is marked `@repeat ok` (functional calls). The builder prints the table above and fails under the floor. This needs the list of selectors the code uses; `pools.py` has a first cut.
11. **`@repeat ok` (format).** Lines exempt from the no-repeat window: pull, inc, add, oom, rez and mechanic calls, `gz`, `ty`, `brb`. The picker skips `Recent` for them.
12. **Emotes.** `@emote ok` on the intents that may take one (banter, joke, boast replies, greeting). The style adds none elsewhere. The style test (`style_cases.json`) pins that a line with `sorry`, `ouch`, `ugh` or `sucks` never gains one.
13. **Tag carry-over.** The builder already warns when `@invites` or `@who` carries across a `@class`. Do the same for `@needs` across a blank `@needs` that is set again on the next line (replies:94 is the case).
14. **Code literals.** `test_code_literals.py` exists; add the banned words and the channel rule to it, with a list of `V` families allowed in /faction.

## Rewrite sample: 30 lines in the target voice

**The target:** lower case, four or five words, one thought, about what is in front of the speaker. Words of the day (`lvl`, `grp`, `xp`, `baf`, `pls`, `u`). No punchline, no simile, no advice nobody asked for. If the bot does not know, it says nothing.

| # | where | now | rewrite |
|---:|---|---|---|
| 1 | ambient_life_more:96 | nothing humbles you like dying to something blue | died to a blue. a blue (needs `just_died`) |
| 2 | ambient_life_more:99 | the scariest words in the game: it has friends | careful, {monsters} baf (needs `at_camp`) |
| 3 | ambient_life_more:109 | paid the healer for my con again, my wallet weeps | buying con back again, broke |
| 4 | ambient_life_more:604 | pull, kill, sit. pull, kill, sit. love this camp | good camp, no downtime |
| 5 | ambient_life_more:124 | just found out what the floor tastes like | dead. my fault |
| 6 | ambient_life_more:462 | i keep stopping to watch the clouds | cut |
| 7 | ambient_realms_more:197 | ludlow never changes, i find that comforting | anyone in ludlow? |
| 8 | ambient_realms_more:209 | these campacorentin woods creak like an old ship, creepy | campa is creepy at night (needs `night`) |
| 9 | battlegrounds:12 | first time in abermenai, everyone is tiny and furious | first time in aber, where do i go |
| 10 | ambient_classes_more:1054 | tanking is simple: face it, hold it, trust the healer | cut as an opener; as an asked answer: taunt and hold it, thats about it |
| 11 | classes:55 | cleric life: heal, rez, get blamed, repeat | clerics always get blamed lol |
| 12 | group_talk:33 | you have the patience of a saint with these pulls | nice pulls |
| 13 | group_talk:24 | this is the most fun i have had all week | good grp |
| 14 | group_talk:32 | good group and a good leader, cant ask for more | xp is good here |
| 15 | group_talk:81 | late night xp with a good group, my favorite kind | 1 more bub then bed |
| 16 | group_talk:25 | glad i grouped with you, honestly | good grp, ty all (when the group ends) |
| 17 | lfg_grouping:29 | healer lfg, {level}, wont let you die (much) | {level} {class} lfg, can rez |
| 18 | lfg_grouping:34 | {level} {class} lfg, can mez, will mez, love mez | {level} {class} lfg, have mez |
| 19 | lfg_real:103 | lfm {need}, {camp}, nice and chill | lf {need} {camp}, slow pulls |
| 20 | replies:21 | hello! nice night for it | hey |
| 21 | replies:40 | sleep well, the mobs will still be here tomorrow | gn |
| 22 | replies:115 | i went that way earlier, watch out for adds near the bridge | cut; a world answer or nothing |
| 23 | replies_pools:205 | good luck, you got this | gl |
| 24 | replies_pools:224 | yikes, stay safe | ouch |
| 25 | memory:14 | {player}, my favorite {player_class}, hi | hey {player} |
| 26 | memory:67 | {player} rezzed me at {place} once, what a legend | {player} rezzed me at {place}, ty again |
| 27 | tells:155 | hey, anyone in the guild grouping tonight? | {player} u grouping tonight? |
| 28 | tells:98 | any questions about your class? happy to help | u need anything? |
| 29 | moments:34 | note to self: do not pull two {monsters} | 2 {monsters} was too many lol |
| 30 | living_server:84 | take it {target}, you owe me one | rez inc {target} |

## Checked and found fine
- **Code-built field talk reads right for the period.** `lf1m heal vendo pst`, `x me`, `wts 99% chain legs 2p obo`, `any rvr grps lf sm?`, `ding 31`. Its faults are channel and non-answer pools, not voice.
- **Class and spec facts in `ClassTips`** (`AutonomousChatFieldTalk.cs:2830-2875`): all 39 classes name real spec lines and weapons for classic plus Shrouded Isles. One weak tip: sorcerer `matter if u solo`.
- **Battleground levels** in the lines match (Abermenai under 20, Thidranki 20-24, Murdaigean 25-29, Caledonia from 30).
- **No expansion leaks found** by search: no housing system, mounts, mail, auction, master levels, towers, battlegroups, instances. The existing `banned.txt` does that job.
- **`FitsDeath`** already stops `lost a bubble` after an rvr death.
- **Functional lines are exempt from laughs** and stay lower case. The `IsFunctional` list works.
- **Newbie help replies** (`personalities.lines`, `ambient_leveling.lines`: con colours, /bind, trainers, realm points, `/gc invite`, buying con back) are correct, if a little long.
- **`*emote*` text:** none outside replies to a roleplaying player. The owner's `long day *chuckles*` did not come from a written line or a literal I could find; see below.
- **Period touches worth keeping:** `worth the box`, `the paper map from the box`, `modem dropped, back`, `off to pray at my grave`.

## Could not check, and why
- **How often each written line is really heard.** It depends on live flags, realm memory and the budget meter. The reach numbers are the filters mirrored in Python, not a played evening. A CI run of `UT_ChatMonth` printing line ids per channel would settle it.
- **Where `long day *chuckles*` came from.** `long day lol` is `FieldTalk.Social`; no literal adds `*chuckles*`. It may be the roleplayer voice (`AutonomousRoleplayers.Voice`) applied to a field line, which I did not read.
- **The silence after a pool runs out** (finding 6). Read from `TryPick`; not run. A small C# test would prove it: ask for `group` / `lead_pull` 25 times with one `RecentLines`.
- **Scenes as heard.** I read 17 of 565. The metrics cover all; my judgement of tone covers the sample.
- **Guild `Lines`, knowledge, questions, guide, market and thread code.** Not read. Other agents may hold them.
- **Goblins in Stonehenge Barrows, and whether `port` questions fit this server's porters.** Needs the world data.

## Tools worth keeping
All in `/tmp/audit2/lines_work/` (not in the repo). Each is a short Python script that imports `build_corpus.py`.
- **`voice_metrics.py`:** median words, share of 10+ word lines, abbreviation share and "saying" share per file, with the FieldTalk literals as the baseline. **Good CI ratchet:** the saying share and the 10+ share of the freely sayable set may not rise.
- **`reach.py`:** mirrors `AutonomousChatFiller` (generic, lore, musing, crafted, situated) and lists what each channel can freely say. **Good CI check:** it reads the marker lists out of the C# file, so it cannot drift; it would have shown the owner's item 5 and 7 lines as free before anyone played.
- **`pools.py`:** pool sizes per kind and selector. **Good CI check** once the code's selectors are listed (rule 10).
- **`final_table.py` and `flags.py`:** the defect table. Coarse; keep as a report, not a gate.
- **`lits.py`:** string literals of the chat code with file and line; useful input for `test_code_literals.py`.

## Hand-back summary
1. Tests only. Static reading and Python counts; no C# run, no PR, no file changed. Worktree `/home/claude/a2-lines` at `4a00ab75`.
2. The written corpus and the code speak in two voices. Code lines: median 3 words. Written ambient: median 10-11, half to three quarters over ten words, a third to two thirds general sayings.
3. The runtime hides this with substring lists: only 235 of 3,012 ambient templates are freely sayable. Those 235 still hold the owner's screenshot lines and their kin.
4. Reply pools are shared by all channels. 227 filler and 16 "dunno" templates can be the whole answer in a tell or to a named bot. `followups.lines` is evasions by design.
5. Group talk still reaches /faction after #469 (8 free templates), and code sends `brb phone`, `afk 5`, `sitting, need hp` to the realm.
6. Emotes are drawn from six per line, per bot, and land on condolences (`ouch, sorry, gl :D`) and on answers. The `u/r` habit rewrites long written lines and lore.
7. 45 templates praise the player or the group for nothing.
8. Small pools plus the no-repeat rule make calls stop: a bot leader calls about 19 pulls, then none for up to 6 hours; boss mechanic calls stop after 3-7. Banter runs out in an evening or two and then loops.
9. 102 templates invent a place or a history (`past the bridge i think`, `was ours when i rode past earlier`).
10. Tell stitching makes `duo? i can heal, you hit things? im in X tho, come find me`. One guild question is sent as a tell.
11. `/bot` companions, and a gamebot when death talk is off, say `My thanks, Bob. I am ready to continue.` (`GameBot.cs:1400`).
12. Later words remain: `tbh`, `hbu`, `yikes`, `^ that` in code; 56 templates in lines. 362 templates use a handful of joke shapes. `baf` is never said; `vault` is banned so lines say `bank`.
13. Facts are mostly right. Eight small ones listed.
14. Delivered in `/tmp/audit2/lines.md`: the per-file table, 100 worst lines, the small pools, 14 linter and format rules, 30 rewrites.
15. First three fixes I would make: the `@channel` and `@fit` tags (S-M), `@repeat ok` for functional calls (S), rewrite the 299 free ambient lines (M).
16. Nothing from the user's memory was used. Nothing new worth saving beyond this report.
