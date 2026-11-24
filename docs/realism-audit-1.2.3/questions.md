# Realism audit 1.2.3 — "questions": what real players ask and say that the bots were never tested on

**Tests only.** Nothing here was seen on the owner's PC. Every reply below comes from CI: draft PR #471 (branch
`audit/1.2.3-questions`, commit `3ee8ea28` = PR #469's head `4a00ab75` + one report-only test), build-and-test run
45291200254, job 138100104984, rows printed as `AUDIT_FRESH_ROW`. Main `5346e5f7` (with #469 merged) holds the same chat code.
No game behaviour was changed. PR #471 is a draft and must not be merged.

## What was run

- **623 fresh lines**, written before reading `player_questions.json` (I read only its `world` and `responders` keys, after
  my set was built, because the test needs them).
  - 559 unique lines + 64 of the factual /faction questions asked again in /group, tell and /say.
  - 441 /faction, 76 /group, 57 tell, 49 /say. 495 questions, 100 statements, 28 follow-ups and corrections.
  - Levels 1 / 5 / 10 / 20 / 35 / 50 in all three realms: 105 / 70 / 119 / 210 / 74 / 45 lines.
  - Sources: the new and returning player's journey; Living Server's own features; things said, not asked; and themes
    from surviving 2001-2006 threads (paraphrased, nothing copied): Tom's Hardware "Newbie Questions" and "Another real
    noob question" (2004), FreddysHouse "Real Newbie Questions" (2006), Quarter to Three's Oct 2001 thread, the Camelot
    Herald beginner's guide, the MIT game-terms glossary (2003-2005).
- **Path:** `UT_ChatQuestionBench.AnswerAll` (the real reply path, the bench's three responders and stub world), called by
  `UT_AuditFreshQuestions`. Same limits as the bench: every line opens a fresh channel, one responder per realm.
- **Scoring:** `chat_scorers.py` first, then I read all 623 rows and overrode 113 of them
  (`tools/bot-voices/eval/audit/judge.py` on the branch holds every override).
- **Facts:** expected answers come from `data/chat/places.json` (levels), `AutonomousChatClassChoice.Bases`,
  `GamePlayer.REALMPOINTS_FOR_LEVEL`, `ServerProperties` (xp and con loss from level 6), `Healer.cs`, the player commands
  folder, README and QUICK COMMANDS. **196 questions are marked "data gap"**: the repo cannot give the true answer (horse
  routes, NPC spots, merchants and dungeon entrances are in the world database, which is not in git; UI keys; spell
  levels; caps).

## The headline

**244 of 623 good (39%).** The bench says 90%. On fresh lines the bots mostly say nothing.

| outcome | n | % |
|---|---:|---:|
| good (a fitting reply, or silence where silence is fine) | 244 | 39% |
| silent where an answer was due | 246 | 39% |
| non-answer ("dunno", "try /advice", "where", a laugh) | 51 | 8% |
| off-topic (answers another question) | 50 | 8% |
| wrong (a false or misleading fact) | 13 | 2% |
| stub (bench cannot tell) | 19 | 3% |

- Questions: **32% good**. Statements: **79% good**. Follow-ups and corrections: **18% good**.
- Questions the repo's data can answer: 46% good. Data-gap questions: 11% good.
- /faction hides the hole as silence (220 of 441). Tell and /group show it as "dunno" (35 of 133 lines).

## Top 10 findings, worst first

### 1. A new player's first questions get silence (class: knowledge coverage)
- **Seen:** "just made a fighter, where do i go first", "what does blue con mean", "do greys give xp", "how do i send a
  tell", "how do i sit down", "how do i loot", "i died, now what", "do i lose anything when i die at lvl 3", "what are
  kill tasks", "is there a vault", "how do i get my constitution back": nobody answers. Level 1 bracket: 28% good, 60 of
  105 silent.
- **Where:** `AutonomousChatKnowledge.TryAnswerInOrder` (AutonomousChatKnowledge.cs:106) has no entry; the fallback is
  `"none factual"` (AutonomousChatReplyPath.cs:350), and the budget turns it into silence in /faction
  (AutonomousChatReplyPath.cs:459, 470; `MaySayNotSure` :292).
- **How often:** 169 of 246 silences are `silent (none factual)`, 25 `silent (none unknown)`.
- **Topics with no knowledge at all (0 good):** con colours, dying (what you lose, release or rez, rez sickness, /pray,
  the healer NPC), pets, stealth, buffs and concentration, respec, guild how-to (form, leave, claim), what relics and
  keeps do, realm abilities, group rules (xp split, level gap, loot), money, item terms (quality, durability, AF, mp),
  UI (camera, chat window, map), era questions (ToA, master levels, housing), "what to do at 50", every Living Server
  command but `/tc` (see 5).
- **Fix:** a second knowledge pass like 1.2.2's F2, from the server's own numbers (xp and con loss start at 6, first
  death a third, the healer NPC, `/release`, `/pray`, `/respec`, `/task` to 19, con colours). Size L. Risk low: new
  answers only fire where there is silence today.

### 2. A bot told or named says "not sure, try /advice" about itself (class: tell and named fallback)
- **Seen:** tell "whats your class" → "not sure, try /advice". "Aldwyn what lvl were you when you picked your class" →
  "Not sure, try /advice." "u rvr at all?" → "not sure, try /advice". "hey Siobhan can u help a newbie" → "not sure, try
  /advice". "Brannoc can i hire you" → same. "how far are you" → "no idea tbh". "wanna come emain later" → "gotcha".
- **Where:** `AutonomousChatReplyBudget` short reply pool, AutonomousChatReplyBudget.cs:198. "whats your class" misses
  `AutonomousChatQuestions.SelfQuestion` ("what lvl are you" works: `fact level`).
- **How often:** 19 of 57 tells and 16 of 76 /group lines are non-answers.
- **Fix:** (a) self-questions for class, spec, guild, rvr, "can you come", "how far are you", "can you help"; (b) drop
  "try /advice" from a tell: a person does not send a friend to /advice; (c) a question about the bot never gets "no
  idea". Size M. Risk low.

### 3. Answers to a different question, said with confidence (class: keyword match without reading the question)
- **Seen:**
  - "can i salvage dropped armor" → "blademasters wear reinforced". "can i spellcraft my own armor" → "Sorcerers wear
    cloth" (`ArmorAnswer`, AutonomousChatGuide.cs:225).
  - "lost some con, healer npc wants gold. worth it?" → "Some, pie matters more." (stats, AutonomousChatGuide.cs:265-289).
  - "how do i stop getting group invites" → "/invite their name" (AutonomousChatKnowledge.cs:414). Right: `/invites off`
    (README.md:169).
  - "what is /faction" → "neutral npcs keep score of you..." (AutonomousChatGuide.cs:411).
  - "stuck on the last epic step, need help" → "/stuck works for that." (AutonomousChatContext.cs:126).
  - "where do i buy a smith hammer" → "A weaponcrafter can make one too." (`TryAnswerBuyGear`,
    AutonomousChatKnowledge.cs:1246; the tools answer at AutonomousChatGuide.cs:154 wants "smiths hammer").
  - "i found some food and cant use it. what heals me in a fight" (tell) → "no heals on a thane sry".
- **How often:** 13 wrong + about 20 of the 50 off-topic.
- **Fix:** each answer needs a "this is not my question" guard (a verb the question must hold, words that rule it out),
  and a test with the near-miss lines above. Size M. Risk medium: guards can silence answers the bench has today, so
  run the ratchet.

### 4. The level-5 class choice still fails in the words players use (class: phrase list)
- **Seen:** "im lvl 5 and my trainer says i have to join a guild?? which guild" → "Your fighter trainer in Camelot,
  spend your points there." "trainer wont train me past 5" → "Jordheim, ur viking trainer is there". "stuck at 5, xp
  bar wont move", "what can a naturalist turn into", "what are my choices as an acolyte", "can a mage be a necromancer",
  "do i have to go all the way to jordheim to pick thane": silence. This is the owner's item 3, one wording away.
  The 2004 thread I found is exactly this player.
- **Where:** `AutonomousChatClassChoice.Asks` (AutonomousChatClassChoice.cs:41-44): no "join a guild", "wont train",
  "stuck at 5", "turn into", "choices", "can a X be a Y". Then `TrainerAnswerFor` names the base-class trainer.
- **How often:** 4 of 14 good.
- **Fix:** any trainer or "stuck" line from a base-class asker at level 5+ gets the class-choice answer; add the
  phrases. Never say "your fighter trainer, spend your points" to a level 5+ base class. Size S. Risk low.

### 5. Living Server's own features: 2 of 28 good
- **Seen:** "how do i hire a companion", "where do i /hire", "how many companions can i have", "whats the realm
  exchange", "what does /tc do", "how do i join the raid", "how do i join one of these groups", "whats the command for
  the horse route list": silence. "how do i add more bots" → "Where?".
- **Where:** no knowledge. Only "where is the realm exchange" works (AutonomousChatGuide.cs:133).
- **Fix:** short in-character answers from README.md:102, 165-174 ("/hire next to a healer, up to seven", "/stables
  lists them", "/invites off"). Launcher questions ("add more bots", "living server tab") should stay silent: mark them
  silence-ok. Size S. Risk: owner's call whether bots may name out-of-game commands.

### 6. A question read as good news, banter or a call (class: shape and intent misread)
- **Seen:** "how many rps to hit rr3?" → "gz!! nice". "how long to rr5" → "nice one". "whats a mini ding" → "gz, well
  earned". "am i too high for thid at 25" → "ouch, been there". "spraggon den at 22, good idea or no" → "lol so true".
  "is 2h worth it on a pally" / "are emerald seals worth keeping" → "whatever gets the most xp per hour". "just dinged
  50, now what" → "Ooh nice".
- **Where:** `AutonomousChatShortTalk.QuestionShape` `player_goodnews` (AutonomousChatShortTalk.cs:181), `ask_opinion`
  (:340, pool replies_invites.lines:267), intents `banter` and `ding` from `AutonomousChatIntentModel.Predict`.
- **How often:** 14 lines.
- **Fix:** a line with "?" or a question opener never takes `player_goodnews`, `ding` or `banter`; `ask_opinion` needs
  a topic the pool fits. Size S. Risk low.

### 7. "where" and "where?" as the rescue for anything
- **Seen:** "where do animists train" → "where". "does it cost money to train" → "where?". "can u mez adds" → "where".
  "why do albs always zerg" → "where haha". "that add came outta nowhere" → "where". "how do i add more bots" →
  "Where?".
- **Where:** `AutonomousChatReplyBudget.Rescue`, AutonomousChatReplyBudget.cs:311 (the "call" pool), reached for lines
  that are not calls.
- **How often:** 6 lines.
- **Fix:** "where?" only when the line is an alarm or a help call with no place in it. Size S. Risk low.

### 8. Follow-ups, corrections and nudges are dead ends (class: no context; partly a bench limit)
- **Seen:** "where is that?" (tell) → "no idea tbh". "and then?" → "no clue, sry". "why not" → "hmm no clue". "u
  sure?" → "Not sure, try /advice." "no the horse, not the boat" → "gotcha". "sorry i meant spindelhalla not
  varulvhamn. what lvl" → "lol yeah". "ur wrong, mithra is in camelot hills not salisbury" → "Ouch, been there".
  "anyone?", "nobody knows?", "hello??", "so no one can help" → silence, all four.
- **Where:** the bench has no thread, so the true follow-up path (`AutonomousChatThreads.FollowUpTarget`,
  `AutonomousChatConversation.Resolve`) is not run. What is measured is the fallback when the thread is not found. A
  correction that carries its own question ("i meant spindelhalla... what lvl") needs no thread and still fails: the
  word "sorry" wins (`intent banter`).
- **How often:** 5 of 28 good.
- **Fix:** (a) "anyone?" / "nobody knows?" re-asks the player's last unanswered question; (b) "i meant X" / "not X, Y"
  re-runs the knowledge lookup on Y; (c) a correction of a bot's fact gets "oh my bad"; (d) a two-line bench (see
  tools). Size M. Risk low.

### 9. Period spelling is not read
- **Seen:** "wher do i trian", "whre is jordhiem", "ne1 no where 2 xp @ 20", "any1 kno wher the wepon merchent is",
  "hwo do i get to tnn": silence in /faction; by tell "whre is jordhiem" → "k". Terms: "wtf is a bub", "wts mean?",
  "what does otw mean", "whats kiting", "what is a twink", "what does baf mean", "whats conc", "whats a mp": silence.
- **Where:** `AutonomousChatQuestions.Normalize` has no misspelling fold; `TryExplainTerm`
  (AutonomousChatKnowledge.cs:441-452) wants one sentence form and a 37-word list.
- **How often:** typos 1 of 7 good, terms 3 of 20.
- **Fix:** a small fold table (wher/whre/were→where, ne1/any1, 2→to, @→at, trian→train, the three capitals within one
  edit) before every matcher; 20 more terms; "X mean?" and "wtf is X". Size S. Risk low.

### 10. "Where" answers name the zone the player already stands in, or a horse to where he is
- **Seen:** "how do i get to camelot from cotswold" → "Cotswold? Thats in Camelot Hills." "where does the horse from
  cotswold go" → same. "16 here, bored of silvermine. where now?" → "in Silvermine Mts., follow the road lol".
  "wheres the forge in jordheim" → "jordheim? any stable master runs a horse there". "where should i hunt at 15 and
  where do i bind" → "Stand by a bindstone and type /bind."
- **Where:** the town and location answers take the first place named (`TownNamed`, AutonomousChatKnowledge.cs:1035;
  `Location`); "from X" and "in X" are not read. Bench stub for the wording, real code for the choice of place.
- **How often:** 9 lines.
- **Fix:** in "to A from B" answer A; "X in <capital>" is a question about X; "bored of / done with X" excludes X from
  the hunt answer. Size S-M. Risk low.

## Wrong facts, with the right fact and its source

| id | player | bot said | right | source |
|---|---|---|---|---|
| f051 | im lvl 5 and my trainer says i have to join a guild?? which guild | Your fighter trainer in Camelot, spend your points there. | pick Armsman, Mercenary, Paladin or Reaver at that class's trainer in Camelot | AutonomousChatClassChoice.cs:18-25 |
| f052 | trainer wont train me past 5, whats going on | Jordheim, ur viking trainer is there | pick Berserker, Savage, Skald, Thane or Warrior in Jordheim | same, :30 |
| f104, f578 | 16 here, bored of silvermine. where now? | in Silvermine Mts., follow the road lol | Bri Leith 12-31, Connacht 3-21, Muire Tomb 13-18 | data/chat/places.json |
| f109, f579 | can i do muire tomb at 11 | fine at 11, mobs are 13-18 | 13-18: a bit high for 11; the rule allows two levels under (`level < low - 2`) | places.json; AutonomousChatQuestions.cs:548 |
| f125 | where do i buy a smith hammer | Check the exchange in Camelot. A weaponcrafter can make one too. | the tools merchant in Camelot | AutonomousChatGuide.cs:154-156 |
| f128 | can i salvage dropped armor | blademasters wear reinforced | /salvage exists; nothing about armour type was asked | commands/playercommands/salvage.cs |
| f295 | can i spellcraft my own armor | Sorcerers wear cloth | gems go on crafted gear, up to four | AutonomousChatGuide.cs:349 |
| f162 | lost some con, healer npc wants gold. worth it? | Some, pie matters more. | the healer NPC restores lost constitution | gameobjects/CustomNPC/Healer.cs:9 |
| f035 | what is /faction | neutral npcs keep score of you. Hrodvar Deepvow in Hagall gives a hunt... | the realm chat channel | commands/playercommands/FactionChatCommand.cs |
| f355, f615 | how do i stop getting group invites | /invite their name | /invites off | README.md:169; commands/playercommands/invites.cs |

- Not counted, could not verify here (world database): the DF entrance zones the bots name (Camelot Hills, Vale of
  Mularn, Connacht; AutonomousChatGuide.cs:34-42), "arrows: a fletcher can make one" (merchants sell arrows in the
  live game; `WhoSells` is null in the bench), "where do i get armor im naked" at level 1 → "Check the exchange in
  Camelot" (no word of a town merchant).
- **Bench artifact, not a bot defect:** "can you rez" → "Wish I could, no rez spell." from the cleric. The bench's speaker
  has no `can_rez` flag (live sets it at AutonomousBotVoices.cs:154). Worth fixing in `UT_ChatQuestionBench.SpeakerOf`,
  or the ratchet can never see a rez reply.

## The 50 most damaging misses

| # | id | channel, realm level | player | bot | route | why it hurts |
|---:|---|---|---|---|---|---|
| 1 | f169 | tell, hib 12 | whats your class | not sure, try /advice | `short (not sure)` | asked its own class by tell, says it does not know |
| 2 | f516 | tell, mid 30 | hey brannoc, got a sec? | someone said brannoc hates the stonecrush because they ate his kin | `knowledge` | a hello by name draws lore gossip about a boss with the same name |
| 3 | f274 | faction, alb 41 | how many rps to hit rr3? | gz!! nice | `shape player_goodnews` | grats to a question |
| 4 | f332 | faction, mid 50 | how long to rr5 | nice one | `shape player_goodnews` | grats to a question |
| 5 | f328 | faction, alb 50 | stuck on the last epic step, need help | /stuck works for that. | `context stuck` | "stuck on a quest step" read as stuck in the terrain |
| 6 | f051 | faction, alb 5 | im lvl 5 and my trainer says i have to join a guild?? which guild | Your fighter trainer in Camelot, spend your points there. | `knowledge` | the level 5 class choice missed: sent to the trainer who just refused |
| 7 | f052 | faction, mid 5 | trainer wont train me past 5, whats going on | Jordheim, ur viking trainer is there | `knowledge` | same |
| 8 | f053 | faction, hib 5 | stuck at 5, xp bar wont move | (nobody answers) | `silent (intent grinding)` | same problem as a statement: silence |
| 9 | f162 | faction, alb 14 | lost some con, healer npc wants gold. worth it? | Some, pie matters more. | `knowledge` | con loss answered as a stat question |
| 10 | f128 | faction, hib 15 | can i salvage dropped armor | blademasters wear reinforced | `knowledge` | salvage answered as an armour-type question |
| 11 | f295 | faction, alb 40 | can i spellcraft my own armor | Sorcerers wear cloth | `knowledge` | spellcraft answered as an armour-type question |
| 12 | f355 | faction, mid 30 | how do i stop getting group invites | /invite their name heh | `knowledge` | the opposite command |
| 13 | f035 | faction, mid 1 | what is /faction | neutral npcs keep score of you. Hrodvar Deepvow in Hagall gives a hunt for the remnants haha | `knowledge` | the chat channel answered as NPC factions |
| 14 | f104 | faction, hib 16 | 16 here, bored of silvermine. where now? | in Silvermine Mts., follow the road lol | `knowledge` | sent back to the zone the player is bored of |
| 15 | f172 | tell, alb 10 | got any spare gear for a lvl 10 pally | Coin comes with levels. Level. | `shape ask_charity` | a brush-off line to a polite ask |
| 16 | f046 | say, hib 1 | hey Siobhan can u help a newbie | not sure, try /advice | `short (not sure)` | named, asked for help: "try /advice" |
| 17 | f091 | say, alb 5 | Aldwyn what lvl were you when you picked your class | Not sure, try /advice. | `short (none factual)` | named, asked about itself: "try /advice" |
| 18 | f371 | say, mid 20 | Brannoc can i hire you | not sure, try /advice | `short (not sure)` | named: "try /advice" |
| 19 | f346 | tell, mid 50 | u rvr at all? | not sure, try /advice | `short (none factual)` | tell about itself: "try /advice" |
| 20 | f347 | tell, hib 50 | wanna come emain later | gotcha | `short (intent rvr)` | an invitation gets "gotcha" |
| 21 | f073 | faction, hib 5 | where do animists train | where | `rescue (none factual)` | "where" as an answer to a where question |
| 22 | f574 | say, mid 5 | does it cost money to train | where? | `rescue (none factual)` | "where?" to a yes/no question |
| 23 | f410 | group, hib 30 | can u mez adds | where | `rescue (not sure)` | "where" to a request |
| 24 | f366 | faction, alb 30 | how do i add more bots | Where? | `rescue (none factual)` | "Where?" to a how question |
| 25 | f135 | faction, alb 11 | can someone rez me at mithra | Ouch, sorry, gl | `intent rez` | a cleric shrugs at a rez call (bench: no rez promise) |
| 26 | f137 | faction, mid 10 | dead in vasudheim woods, anyone got a rez | ugh, what got you? | `intent rez` | same |
| 27 | f198 | faction, mid 25 | am i too high for thid at 25 | ouch, been there | `intent banter` | banter line to a real question |
| 28 | f188 | faction, hib 22 | spraggon den at 22, good idea or no | lol so true | `intent banter` | banter line to a real question |
| 29 | f453 | faction, mid 38 | sorry i meant spindelhalla not varulvhamn. what lvl | lol yeah | `intent banter` | a correction with a question gets "lol yeah" |
| 30 | f524 | faction, alb 12 | ur wrong, mithra is in camelot hills not salisbury | Ouch, been there | `intent banter` | a correction of the bot gets "Ouch, been there" |
| 31 | f496 | faction, mid 4 | whats a mini ding | gz, well earned | `intent ding` | grats to "whats a mini ding" |
| 32 | f621 | tell, mid 3 | i found some food and cant use it. what heals me in a fight | no heals on a thane sry | `short (none unknown)` | "what heals me" read as "can you heal" |
| 33 | f125 | faction, alb 11 | where do i buy a smith hammer | Check the exchange in Camelot. A weaponcrafter can make one too. | `knowledge` | tools are a merchant item |
| 34 | f109 | faction, hib 11 | can i do muire tomb at 11 | fine at 11, mobs are 13-18 | `knowledge` | "fine" two levels under a dungeon's range |
| 35 | f094 | faction, alb 5 | how do i get to camelot from cotswold | Cotswold? Thats in Camelot Hills. | `knowledge` | names the zone the player is already in |
| 36 | f126 | faction, mid 12 | wheres the forge in jordheim | jordheim? any stable master runs a horse there | `knowledge` | a horse to the city he is asking about |
| 37 | f262 | faction, hib 36 | are emerald seals worth keeping | whatever gets the most xp per hour :D | `shape ask_opinion` | generic opinion line |
| 38 | f142 | faction, mid 10 | lvl 10 quests anywhere? | Nisse's Lair with a group, or East Svealand solo, thats where the 10s are | `live ask_hunt` | quests answered with a hunting spot |
| 39 | f067 | faction, hib 5 | which hib caster is best solo | Lough Derg, saw a bunch of 5s camping there | `live ask_hunt` | class advice answered with a hunting spot |
| 40 | f484 | faction, mid 4 | hi all, quick q, how do i get to jordheim and is there a vault | greetings, good hunting | `intent greeting` | two questions and a hello: only the hello is answered |
| 41 | f001 | faction, alb 1 | just made a fighter, where do i go first | (nobody answers) | `silent (intent travel)` | the first thing a new player asks: silence |
| 42 | f013 | faction, mid 2 | what does blue con mean | (nobody answers) | `silent (none factual)` | con colours: silence (4 lines) |
| 43 | f018 | faction, alb 1 | how do i send a tell | (nobody answers) | `silent (none factual)` | silence; the how-to knows "talk to my guild" but not "send a tell" |
| 44 | f163 | faction, mid 12 | how do i get my constitution back | (nobody answers) | `silent (none factual)` | the healer NPC is in the code, the bots do not know it (3 lines) |
| 45 | f205 | faction, mid 24 | how do i respec | (nobody answers) | `silent (none factual)` | /respec exists, bots silent |
| 46 | f312 | faction, alb 50 | where is golestandt | (nobody answers) | `silent (none factual)` | "mid dragon" is answered, the dragon by name is not (also Cuuldurach) |
| 47 | f486 | faction, alb 4 | wher do i trian | (nobody answers) | `silent (none unknown)` | typos: all 6 /faction typo lines silent |
| 48 | f474 | faction, alb 5 | anyone? | (nobody answers) | `silent (none factual)` | "anyone?", "nobody knows?", "hello??", "so no one can help": all silent |
| 49 | f462 | tell, mid 30 | where is that? | no idea tbh | `short (not sure)` | a follow-up by tell: "no idea tbh" |
| 50 | f419 | tell, mid 30 | how far are you | no idea tbh | `short (none factual)` | a groupmate asked how far it is: "no idea tbh" |

## The numbers

"good" counts silence where silence is fine. Hand-judged.
### Total

| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| all | 623 | 244 | 39% | 13 | 51 | 246 | 50 | 19 |

### By channel

| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| faction | 441 | 154 | 35% | 10 | 9 | 220 | 37 | 11 |
| group | 76 | 41 | 54% | 1 | 16 | 12 | 4 | 2 |
| tell | 57 | 24 | 42% | 2 | 19 | 0 | 7 | 5 |
| say | 49 | 25 | 51% | 0 | 7 | 14 | 2 | 1 |

### By kind

| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| question | 495 | 160 | 32% | 13 | 39 | 225 | 41 | 17 |
| statement | 100 | 79 | 79% | 0 | 4 | 11 | 4 | 2 |
| followup | 28 | 5 | 18% | 0 | 8 | 10 | 5 | 0 |

### By level bracket

| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 1 | 105 | 29 | 28% | 1 | 8 | 60 | 7 | 0 |
| 5 | 70 | 31 | 44% | 2 | 4 | 24 | 9 | 0 |
| 10 | 119 | 35 | 29% | 7 | 12 | 51 | 11 | 3 |
| 20 | 210 | 103 | 49% | 2 | 21 | 59 | 14 | 11 |
| 35 | 74 | 32 | 43% | 1 | 3 | 32 | 4 | 2 |
| 50 | 45 | 14 | 31% | 0 | 3 | 20 | 5 | 3 |

### By realm

| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| alb | 242 | 93 | 38% | 4 | 20 | 91 | 24 | 10 |
| mid | 204 | 77 | 38% | 4 | 18 | 89 | 12 | 4 |
| hib | 177 | 74 | 42% | 5 | 13 | 66 | 14 | 5 |

### By topic

| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| said | 97 | 77 | 79% | 0 | 5 | 9 | 4 | 2 |
| hunt | 39 | 23 | 59% | 2 | 2 | 9 | 3 | 0 |
| web | 31 | 4 | 13% | 0 | 2 | 24 | 1 | 0 |
| ls | 28 | 2 | 7% | 3 | 3 | 19 | 1 | 0 |
| rvr | 23 | 6 | 26% | 0 | 2 | 11 | 4 | 0 |
| death | 21 | 3 | 14% | 1 | 1 | 13 | 2 | 1 |
| travel | 21 | 5 | 24% | 0 | 0 | 3 | 4 | 9 |
| terms | 20 | 3 | 15% | 0 | 2 | 14 | 1 | 0 |
| followup | 20 | 3 | 15% | 0 | 7 | 8 | 2 | 0 |
| place | 19 | 13 | 68% | 2 | 2 | 1 | 1 | 0 |
| spec | 17 | 4 | 24% | 0 | 0 | 10 | 3 | 0 |
| raid | 16 | 5 | 31% | 0 | 0 | 7 | 1 | 3 |
| trainer | 14 | 13 | 93% | 0 | 1 | 0 | 0 | 0 |
| classchoice | 14 | 4 | 29% | 2 | 1 | 7 | 0 | 0 |
| si | 14 | 11 | 79% | 0 | 0 | 2 | 1 | 0 |
| gear | 13 | 3 | 23% | 0 | 0 | 9 | 1 | 0 |
| craft | 13 | 1 | 8% | 3 | 2 | 6 | 1 | 0 |
| bg | 13 | 6 | 46% | 0 | 1 | 5 | 1 | 0 |
| df | 12 | 7 | 58% | 0 | 1 | 3 | 1 | 0 |
| group | 11 | 3 | 27% | 0 | 1 | 3 | 0 | 4 |
| bot | 11 | 7 | 64% | 0 | 4 | 0 | 0 | 0 |
| ui | 10 | 2 | 20% | 0 | 1 | 7 | 0 | 0 |
| pets | 10 | 0 | 0% | 0 | 1 | 9 | 0 | 0 |
| correction | 10 | 2 | 20% | 0 | 1 | 2 | 5 | 0 |
| social | 9 | 6 | 67% | 0 | 2 | 0 | 1 | 0 |
| twopart | 9 | 7 | 78% | 0 | 0 | 0 | 2 | 0 |
| stealth | 8 | 0 | 0% | 0 | 0 | 8 | 0 | 0 |
| money | 7 | 1 | 14% | 0 | 0 | 6 | 0 | 0 |
| buy | 7 | 2 | 29% | 0 | 0 | 3 | 2 | 0 |
| typo | 7 | 1 | 14% | 0 | 1 | 5 | 0 | 0 |
| howto | 6 | 0 | 0% | 0 | 1 | 5 | 0 | 0 |
| classpick | 6 | 1 | 17% | 0 | 0 | 3 | 2 | 0 |
| guild | 6 | 1 | 17% | 0 | 1 | 4 | 0 | 0 |
| buffs | 6 | 0 | 0% | 0 | 0 | 6 | 0 | 0 |
| epic | 6 | 2 | 33% | 0 | 0 | 2 | 2 | 0 |
| class | 5 | 0 | 0% | 0 | 0 | 4 | 1 | 0 |
| respec | 5 | 0 | 0% | 0 | 1 | 2 | 2 | 0 |
| etiquette | 5 | 1 | 20% | 0 | 1 | 3 | 0 | 0 |
| lfg | 5 | 5 | 100% | 0 | 0 | 0 | 0 | 0 |
| start | 4 | 1 | 25% | 0 | 0 | 3 | 0 | 0 |
| returning | 4 | 1 | 25% | 0 | 0 | 3 | 0 | 0 |
| botcheck | 4 | 3 | 75% | 0 | 0 | 1 | 0 | 0 |
| task | 3 | 2 | 67% | 0 | 0 | 1 | 0 | 0 |
| help | 3 | 0 | 0% | 0 | 3 | 0 | 0 | 0 |
| quest | 3 | 1 | 33% | 0 | 0 | 1 | 1 | 0 |
| trade | 3 | 2 | 67% | 0 | 0 | 1 | 0 | 0 |
| era | 3 | 0 | 0% | 0 | 0 | 3 | 0 | 0 |
| endgame | 2 | 0 | 0% | 0 | 1 | 1 | 0 | 0 |

### Data gap or not (questions only)

| group | n | good | good % | wrong | non-answer | silent | off-topic | stub |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| data has it | 299 | 138 | 46% | 8 | 25 | 101 | 23 | 4 |
| gap | 196 | 22 | 11% | 5 | 14 | 124 | 18 | 13 |


## What I checked and found fine

- **Trainer of a named class:** 13 of 14 ("who trains friars", "wher is hunter trainer", "necro trainer?").
- **Place levels:** 13 of 19 ("keltoi at 21 ok?", "can a 36 do raumarik" → "a bit high for 36, mobs there are mostly 46+").
- **Where to hunt, when the shape is read:** 23 of 39, no wrong realm, no place 8+ levels off.
- **Darkness Falls owner and way in, battlegrounds how-to and brackets, rr2 points, the dragons by realm, kill tasks.**
- **Group small talk:** brb, afk, back, ding, gn, thx, sorry, "oom, hold pulls", "inc 2", "wow that was close": 79% of
  statements good. "lol", "ok", "nvm", "mt", trade posts: quiet or a short word, as they should be.
- **"are you a bot":** 3 of 4 good and in voice.
- **Lfg posts:** an answer or a fair silence in all 5.
- **Same question in another channel:** 21 of 21 that work in /faction also work in /group, tell and /say. No channel
  loses a known answer. The difference is only in the failures (silence in /faction, "dunno" elsewhere).
- **No slot leaks, no doubled words** in 340 replies. No CLUNKY flag; three PADDED flags, all scorer noise.

## What I could not check, and why

- **Follow-ups inside a real thread.** The bench opens a fresh channel per line. The owner's items 1 and 2 live there.
- **Anything the world database decides:** horse routes, trainer NPC names and spots, merchant lists, dungeon entrance
  zones, how-far. 19 rows are stubs. The database is not in git.
- **Rez and heal promises, join replies, "anyone in X?"**: stubbed in the bench.
- **Bot-to-bot threads, [Trade], guild chat, companions (/bot, hired).** This path is autonomous gamebots answering a
  real player only. Companions were not exercised at all.
- **Variance.** One seed per line. A line that is good here may draw a weaker pick on another seed.
- **My own judgments.** One reader. 113 overrides are in `judge.py` for review. "Good" for a statement means "a person
  could have said that".

## Tools worth keeping

- `tools/bot-voices/eval/audit_fresh_questions.json` (623 lines, with `kind`, `bracket`, `data_gap`, `hand`,
  `variant_of`), `audit/make_fresh_questions.py`, `audit/judge.py`, `audit/judged.json`, `audit/proposal.json`, and
  `UT_AuditFreshQuestions.cs`: all on branch `audit/1.2.3-questions`.
- **CI ratchet candidates:**
  - "No non-answer to a self-question by tell" (class, level, spec, guild): exact, cheap.
  - "A line with a question mark never takes `player_goodnews`, `ding`, `banter` or the call rescue": a route check, no
    scorer needed.
  - "Same question, four channels": the 64 variants; fail when a channel loses an answer /faction has.
  - A **two-line bench** (bot line, then the player's follow-up) built on `ChatThreadLog` and
    `AutonomousChatConversation`, both pure. This is the missing instrument for the owner's items 1 and 2.
  - Add `kind` to the bench rows so statements and follow-ups are reported apart from questions.

## Proposal: which lines join the bench and the holdout

Lists by id in `tools/bot-voices/eval/audit/proposal.json` on the branch.

- **Bench, 260 lines:** 132 questions whose facts the repo holds (of 177), 103 statements, follow-ups and corrections
  (of 128), 25 channel variants. Today 86 of the 177 fact questions are good, so the ratchet floor would drop on the
  day they join: set the new baseline from the first run, then ratchet up.
- **Holdout, 80 lines** (drawn at random, seed 123, never to be tuned on): 45 fact questions, 25 said lines, 10
  variants. This replaces a holdout written by the same hand as the fixes.
- **Not yet, 167 lines:** the data-gap questions. Each needs a fact from the owner or the world database first (horse
  routes, entrance zones, caps, spell levels). Add them as the knowledge lands.
- Before they join: tighten my `must_any` patterns. Several were loose enough to pass "not sure, try /advice"; the hand
  overrides show which.

## Hand-back summary

- 623 fresh lines through the real reply path in CI (draft PR #471, do not merge). Tests only.
- **39% good** against the bench's 90%. Questions 32%, statements 79%, follow-ups 18%.
- 246 lines get silence where an answer was due; 169 of them are `none factual`: no knowledge.
- Level 1 is the worst bracket (28%). A new player's first ten questions mostly go unanswered.
- By tell, a bot asked "whats your class" says "not sure, try /advice". 35 of 133 tell and group lines are "dunno".
- 13 wrong answers, all one class: a keyword answer fired on another question (salvage → armour type, con loss →
  piety, stop invites → /invite, /faction → NPC factions).
- The owner's level-5 class question still fails in six other wordings.
- Living Server's own features: 2 of 28. Only "where is the realm exchange" works.
- Questions get "gz!! nice" (rr3), "lol so true", "where" as answers: 20 lines from shape and rescue misreads.
- Typos and period shorthand: 1 of 7; chat terms 3 of 20.
- No channel loses an answer /faction has (21 of 21). Small talk in a group is sound.
- Zero-knowledge topics: con colours, death rules, pets, stealth, buffs, respec, guild how-to, relics and keeps, RAs,
  group rules, money, item terms, UI, era, endgame.
- Bench bug found: its cleric has no `can_rez` flag, so it says "no rez spell".
- Could not check: real threads, world-database facts, companions, bot-to-bot.
- Proposal: 260 lines to the bench, 80 to a new holdout, 167 wait for facts; add a two-line bench and a `kind` column.
- Sizes: findings 4, 6, 7, 9 are S; 2, 3, 8, 10 are M; 1 is L. All low risk except 3 (guards may cost bench answers).
