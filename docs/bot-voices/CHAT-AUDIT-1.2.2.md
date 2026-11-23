# Chat audit 1.2.2: /faction and bot answers to player questions

The owner's 1.2.2 ask (2026-11-22): improve /faction and bot chat, "smooth clunkiness, errors, or disjointed phrasing"
and "reduce or eliminate general fluff and non answers". This is phase 1, the audit: a question bench that runs in CI,
scorers, the numbers, the root causes in code and a ranked fix plan. No reply is changed yet.

**Everything here is tests only.** The numbers come from the bench run in CI on commit `e60cf764` (build-and-test run
45114460321), through a mirror of the reply path with stub world facts, not from a played world or a PC log.

## How the bench works

- **The questions:** [tools/bot-voices/eval/player_questions.json](../../tools/bot-voices/eval/player_questions.json),
  453 lines a 1.65-era player types, written by
  [make_player_questions.py](../../tools/bot-voices/eval/make_player_questions.py) (CI checks the file is up to date).
  - 419 in /faction, 12 in /say, 11 in /group, 11 by tell; 153 Albion, 153 Midgard, 147 Hibernia.
  - 18 intents: where to level (45), a place's levels (48), where to buy (23), travel (48), trainers (29), quests (5),
    the epic (7), raids and dragons (21), lore (12), RvR status (42), crafting (12), items and drops (31), specs and
    classes (26), NPC factions (10), the bots' own plans and state (45), small talk (25), chat terms (11), how-to (13).
  - Each question carries the facts a good answer must hold (`must_any`) and the facts that would make it wrong
    (`wrong_any`), from the repo's own data: place levels from `data/chat/places.json` (measured by
    `place_levels.py`), capitals, dragons, realm-rank points and chat terms from the server code, the stub world's
    relics, keeps and Darkness Falls owner. Where to level at N accepts any place of the asker's realm within 4 levels
    of N and calls wrong any place 8 or more levels off, or of another realm.
- **The path:** [UT_ChatQuestionBench](../../source/server/Tests/UnitTests/UT_ChatQuestionBench.cs) answers each
  question the way the server does for its channel, with the real code: `OnPlayerFactionChat`'s screen,
  `SchedulePlayerReplies` (knowledge lookup, help calls, lines said to nobody, grouping, realm-wide thanks and grats),
  `VoicedChoice` (facts, the situation, question shapes, the corpus pick with the server's filters, "not sure") and
  `Budgeted` (credit, contradictions, the wrong situation, fit, acknowledgement and rescue). A tell takes
  `TellReply`'s checks first. Every corpus pick goes through the real `AutonomousBotVoiceCorpus.TryPick`, and code-built
  answers through the real `AutonomousBotVoices.StyleText`, so the writing styles and typing habits are in the replies.
  - It is a **mirror**: the coordinator needs live `GameBot`s, so the bench calls the same pure functions in the same
    order (as `ChatAuditRoute` does). Fix item F0 below removes the mirror.
  - **Stubs** (94 answers carry `stub`): the responder is one bot per realm (Aldwyn, a 32 Albion cleric; Brannoc, a 30
    Midgard thane; Siobhan, a 31 Hibernian druid); trainer and location answers name the catalog place where the live
    lookup names an NPC or a zone from the world data; where-to-hunt takes a catalog zone of the level as the zone bots
    camp in; enemy sightings, how-far and join replies are `[world: ...]` markers (9 replies, scored STUB). Threads,
    debates, tales, storylines, guild requests and a remembered player are not in the bench: each question is the
    first line of a fresh channel.
- **The scorers:** [chat_scorers.py](../../tools/bot-voices/eval/chat_scorers.py), pure and unit-tested
  ([test_chat_bench.py](../../tools/bot-voices/tests/test_chat_bench.py), 26 tests). One category per reply, the first
  that applies: SILENT, STUB, WRONG (a `wrong_any` fact), ANSWERED (a `must_any` fact), NON_ANSWER (a deflection: "not
  sure", "dunno", "depends", "ask around", "try /advice", a bare "ya?"), FLUFF (nothing but filler), OFF_TOPIC (says
  something, not what was asked). Flags beside it: CLUNKY (doubled words, a/an, number agreement, dangling
  connectors, unfilled slots, broken punctuation, a clause said twice, a line that contradicts itself, wrong tense, a
  laugh tacked onto a "dunno" or a serious line, a reply that opens with its own different question, a capitalized line
  with names left in lower case), ECHO (quotes four words of the question back), PADDED (an answer with a
  content-free piece stuck on).
- **CI:** the bench is a normal test in build-and-test; the step "Score the chat question bench (report only)" runs
  [score_bench.py](../../tools/bot-voices/eval/score_bench.py) `bench` on its output and `lines` over the written
  lines, prints the report and every row to the log (`CHAT_BENCH_ROW`), and uploads the `chat-bench` artifact. It
  never fails the build. After the fixes it becomes a ratchet (see the end).
- **Run it yourself:** `dotnet test source/server/Tests/Tests.csproj --filter UT_ChatQuestionBench` with
  `CHAT_BENCH_OUT=bench.json`, then `python tools/bot-voices/eval/score_bench.py bench bench.json --md report.md`.
  `score_bench.py lines` needs only Python.

## The scores

453 questions. **Good outcomes: 274 (60.5%)**: 269 answered and 5 silent where silence is fine (a lone "lol", "gn",
"brb" in /faction).

| category | n | % |
|---|---:|---:|
| ANSWERED | 269 | 59.4% |
| WRONG | 7 | 1.5% |
| NON_ANSWER | 27 | 6.0% |
| OFF_TOPIC | 67 | 14.8% |
| FLUFF | 2 | 0.4% |
| SILENT | 72 | 15.9% (67 where an answer was due) |
| STUB | 9 | 2.0% |
| flag CLUNKY | 43 | 9.5% (48 issues) |
| flag ECHO | 0 | 0% |
| flag PADDED | 4 | 0.9% |

- **Of the 269 answers, 78 are stubbed** (travel 33, where to level 23, trainers 19, buying 3): the live world may word
  them differently or name an NPC, but they take the same route.
- **Bad outcomes by kind: 175 (38.6%).** Not answering is the big problem, not wrong facts: 67 questions get silence,
  67 an off-topic line, 27 a "dunno". Only 7 replies are wrong, all from one template (C7).
- **Fluff is rare as a whole reply (2)** because the budget already holds back weak lines; it returns as the "dunno"
  rescue (27) and as laughs stuck onto answers (19 of the clunky flags).
- **The clunky flags are run-time stitching, not the written lines** (below): 43 replies with 48 issues, 19 tacked
  laughs, 15 replies opening with another question, 14 capitalized lines with lower-case names. 36 of the 43 come from
  the knowledge lookup's answers; none is a doubled word, a broken a/an, an unfilled slot or a dangling connector.
- **Scorer limits:** a few OFF_TOPIC replies are fair answers in chat terms ("are you guys bots?" "if i was a bot id
  pull better"; "back" "re"). Reading all 67 by hand puts about 6 in that group; the rest miss the question.

## By intent, realm and channel

"ok" is the share of good outcomes.

### By intent

| group | n | ok | answered | wrong | non-answer | off-topic | fluff | silent | stub | clunky |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| bot_plans | 45 | 82.2% | 37 | 0 | 2 | 3 | 0 | 0 | 3 | 2 |
| buy_where | 23 | 52.2% | 12 | 0 | 0 | 0 | 0 | 11 | 0 | 3 |
| crafting | 12 | 16.7% | 2 | 0 | 2 | 0 | 0 | 8 | 0 | 0 |
| epic | 7 | 42.9% | 3 | 0 | 0 | 3 | 0 | 1 | 0 | 0 |
| faction | 10 | 0.0% | 0 | 0 | 3 | 0 | 0 | 7 | 0 | 0 |
| howto | 13 | 76.9% | 10 | 0 | 0 | 0 | 0 | 3 | 0 | 0 |
| items | 31 | 41.9% | 13 | 0 | 5 | 6 | 0 | 7 | 0 | 0 |
| level_where | 45 | 51.1% | 23 | 0 | 7 | 3 | 0 | 12 | 0 | 3 |
| lore | 12 | 83.3% | 10 | 0 | 0 | 1 | 0 | 1 | 0 | 4 |
| place_level | 48 | 33.3% | 16 | 0 | 0 | 32 | 0 | 0 | 0 | 16 |
| quest | 5 | 60.0% | 3 | 0 | 0 | 0 | 0 | 2 | 0 | 0 |
| raid | 21 | 81.0% | 17 | 0 | 0 | 4 | 0 | 0 | 0 | 4 |
| rvr_status | 42 | 71.4% | 30 | 0 | 1 | 6 | 0 | 2 | 3 | 0 |
| small_talk | 25 | 72.0% | 13 | 0 | 0 | 3 | 2 | 7 | 0 | 0 |
| spec | 26 | 61.5% | 16 | 0 | 1 | 6 | 0 | 3 | 0 | 0 |
| terms | 11 | 81.8% | 9 | 0 | 1 | 0 | 0 | 1 | 0 | 1 |
| trainer | 29 | 65.5% | 19 | 7 | 2 | 0 | 0 | 1 | 0 | 5 |
| travel | 48 | 75.0% | 36 | 0 | 3 | 0 | 0 | 6 | 3 | 5 |

### By realm

| group | n | ok | answered | wrong | non-answer | off-topic | fluff | silent | stub | clunky |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| alb | 153 | 59.5% | 89 | 3 | 8 | 23 | 1 | 26 | 3 | 19 |
| hib | 147 | 61.9% | 90 | 2 | 8 | 25 | 0 | 19 | 3 | 12 |
| mid | 153 | 60.1% | 90 | 2 | 11 | 19 | 1 | 27 | 3 | 12 |

### By channel

| group | n | ok | answered | wrong | non-answer | off-topic | fluff | silent | stub | clunky |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| faction | 419 | 58.7% | 241 | 7 | 25 | 65 | 2 | 72 | 7 | 41 |
| group | 11 | 81.8% | 9 | 0 | 1 | 0 | 0 | 0 | 1 | 1 |
| say | 12 | 83.3% | 10 | 0 | 0 | 1 | 0 | 0 | 1 | 1 |
| tell | 11 | 81.8% | 9 | 0 | 1 | 1 | 0 | 0 | 0 | 0 |


## The /faction flow

From the same run (419 /faction questions, five bots of the realm able to hear each):

- **How many answer:** 341 questions get one answer, 3 get two, 3 get three (greetings with their pile-ons), 72 none.
  No question got two answers (`IsFactual` and the arbiter hold a question to one), so there is no chorus to fix.
- **No double replies and no quoting:** 0 replies would be said twice by two bots (`TryAddDistinctResponse` and the
  corpus's recent-line rules), 0 replies quote the question back.
- **Timing:** the first answer comes a median **14.4 s** after the question, 90% within 27.7 s, the slowest at 39.2 s
  (`AutonomousChatArbiter.DelayMs`: reading, a think pause, typing at 40 words a minute). Long knowledge and lore
  answers are the slow ones. A player who asked "what lvl is vendo?" waits 15-25 s for "mostly 20-28"; short facts could
  type faster (fix F11).
- **The simulated evening** (`UT_FactionCoherence`, audited by `source/tools/chat_audit.py` in the same run) passes:
  101 of 106 bot questions answered within 60 s, nothing out of order, no pile-ons over the caps. Its unanswered bot
  questions show the same holes as the bench: "where do 24s xp?" (C4), "anyone know where to buy pal stuff at 8?" (C8),
  "pallies, sc or drops at 40?". It also counts 27 short lines (three words or fewer) said again within 10 minutes.

## The written lines

`score_bench.py lines` over all 45,686 written lines (`data/chat/*.lines` expanded, scene beats included; spam and
beggar lines, broken on purpose, left out), each template also filled with sample values:

- **Clean:** 83 clunky hits in all, nearly all on purpose ("jingle jingle", "well well well", "night night"); 0
  templates break once filled. The writing is not where the clunkiness comes from.
- **Replies said to a question: 1,239 lines; 65 are fluff ("yep yep", "no lol", "yea") and 73 non-answers ("dunno, just
  feels that way", "no idea honestly")**, most in `followups.lines` (25 non-answers, 11 fluff), `replies_invites.lines`
  (18 non-answers) and `replies_pools_more.lines` (20 fluff, 6 non-answers).
- **The "not sure" set** (`@answers ask`, what `VoicedChoice` says to a factual question it can't answer) is 17 lines,
  every one a non-answer: "no idea sorry", "beats me", "wish i knew", "ask around maybe", "try asking a guard".
- Counted apart, as they are often deliberate in a written line: 106 lines end on a laugh after a full stop ("reported.
  lol"), 42 open with their own question ("is the iarnwood worth it at our levels? the stories alone keep me out").
  Both misfire when the line is drawn for another question (C12, C10).


## Root causes in code

Each bad reply of the bench was put under the component that produced it, by its route and intent (the "why" column
of the worst examples below names it). Counts are bench questions with a bad outcome (WRONG, NON_ANSWER,
OFF_TOPIC, FLUFF, or SILENT where an answer was due: 170), then the clunky flags apart.

| id | bad outcomes | component | what happens |
|---|---:|---|---|
| C8 | 53 (silent) | `AutonomousChatKnowledge` coverage | Nothing knows the topic, so `VoicedChoice` falls to "not sure" (half the time) or nothing, and `Budgeted` holds even that in /faction (fit Weak). Holes: potions, dyes, poison, gems, crafting tools, bags, selling loot, "who sells horse tickets" (`TryAnswerTicketsInGeneral` asks for "where/buy/get/how", not "sells", line 862), the way into Darkness Falls, the frontier and the battlegrounds, armor weights (`AutonomousChatGear.ArmorWord` exists and is never asked), stats, crafting past "how do i start", NPC factions (0 of 10 answered), "kill tasks". |
| C1 | 40 | `AutonomousChatKnowledge.TryAnswer` order; `LoreChat.TryAnswer` | The lore answer runs inside the knowledge lookup (line 44), and the knowledge lookup runs in `SchedulePlayerReplies` (line 2738) before `VoicedChoice` ever reaches `AutonomousChatQuestions.AnswerWorld`. So any question that names a place the lore bible knows is answered with lore: "what lvl is gotar?" draws "vidalf is west among the imps", "who has darkness falls right now?" draws "darkness falls changes hands like the weather" while `AnswerDarknessFalls` holds the owner. Lore never says a level by design (the owner's fourth-wall rule, `tools/lore/fourth_wall.py`), so a lore answer to a level question is always a non-answer. 32 of the 48 place-level questions go this way. |
| C4 | 22 | `AutonomousChatShortTalk.QuestionShape` ("ask_hunt", line 285) | A fixed phrase list: no "lvl" ("good place to lvl at 18"), no "best xp for 21", "where do 24s hunt", "im 36, where should i go now", "where can a 15 minstrel solo", "where do i go after 33", "any good spots for" (plural). Those fall to "not sure" or silence, and a line naming a class goes to class talk first ("any good spots for a 12 paladin?" draws "Paladin chants carry a whole group."). The simulated evening's "where do 24s xp?" went unanswered for the same reason. Where the shape is found, 23 of 23 answers hold a right place. |
| C6 | 20 | `AutonomousChatReplyBudget.Rescue` (line 343); the "not sure" set | A weak line under the minimum fit becomes a "dunno", "no idea", "not sure tbh", "idk sry", "no clue, sry", "hmm dunno" at `DunnoShare` 0.3 in /faction; with the corpus's 17-line "not sure" set (`@answers ask`) these are 27 of the bench's replies, all non-answers. |
| C9 | 10 | corpus picks by shape and intent | A line written for the question's shape, not its content: "what are you up to?" draws "the usual, killing stuff" from a bot whose target and zone are known; "anyone else lagging?" "The dice hate us all."; "should i roll a necro?" "Worth it imo". |
| C7 | 7 wrong (+5 clunky) | `AutonomousChatKnowledge.GenericTrainerAnswer` (lines 351-359) | Three templates for every class: "he trains ur spells" for an armsman, warrior, hero or blademaster, who have no spells; "your minstrel trainer" said to a sorcerer who asked who trains minstrels. A class of another realm draws nothing (`TryAnswerTrainer` finds no trainer) and the budget makes it "no clue, sry". |
| C3 | 6 | `SchedulePlayerReplies` grouping branch; the intent model's "lfg" and "invite" keywords | A question about a group is read as a group post: "what does lfg mean?", "is a bard needed in groups?", "any rvr groups going?", "do i need a group for my epic?" draw "nobody around your level that i know of" (and `noneNearLevel` wins over a knowledge answer, line 2941). |
| C5 | 6 | `AutonomousChatFieldTalk.SpecAdvice` / `WeaponAdvice` (lines 2819, 2824) | Every class's pool has a generic line appended, so one ask in three or four draws "whatever ur spec is" or "max ur main line first" even for a class the table knows. |
| C2 | 3 | `AutonomousChatKnowledge.AsksAboutRealmRaid` (line 165) | " epic " counts as a raid word, and the realm-raid answer runs before `TryAnswerCommon`'s epic answers: "how long is the epic?", "where do i get my epic armor?" draw "nothing running right now, the next one gets called when enough 50s are on". |
| other | 3 | | "got any pots?" misses the bot's own pots (`SelfQuestions` "pots" needs a "you"), and two small-talk lines. |

The clunky flags (43 replies, 48 issues):

| id | flags | component | what happens |
|---|---:|---|---|
| C10 | 19 | `AutonomousBotVoiceCorpus.Style` emotes and `AutonomousChatIdiolect` | A laugh or smiley is added to any line `TakesEmote` allows (line 755): a "dunno" ("no clue, sry haha", "hmm dunno :P"), a warning ("thats a full raid, dont go alone :)"), a lore line ("trust nothing you meet there heh"). |
| C12 | 15 | `LoreChat.WrittenLoreNaming` (line 102) | Any written lore line that names the place is drawn, including the Q-and-A lines written for another question: "what lvl is spraggon den?" draws "who is eraliwyr? the alchemist for viraniels plague..."; "is vendo caverns ok at 24?" draws "grjotgard is in the bear cave". |
| C11 | 14 | `AutonomousBotVoices.StyleText` capitalizing code-built answers | Answers built in code use lower-case names (`CapitalWord` gives "camelot", place keys and lore names are lower case); a voice that capitalizes makes the first letter big and leaves the names small: "The armsman trainer in camelot", "Three princes sit under legion", "Krondon? Ogre hall under avalon." |

## Ranked fix plan

Impact is the number of bench questions each fix would move to a good outcome (an upper bound from the bench's own
rows; fixes overlap a little). Today 274 of 453 are good (60.5%); all of the first eight together would reach about
390 (86%).

| rank | fix | moves | where |
|---:|---|---:|---|
| F0 | Make the reply path one pure function the coordinator and the bench both call (inputs: the line, channel, the responder's facts, the world facts, a random), so the bench measures the real code and can't drift. Enabler for the ratchet. | 0 | `AutonomousBotChatCoordinator.SchedulePlayerReplies` / `VoicedChoiceCore` / `Budgeted`, `UT_ChatQuestionBench` |
| F1 | **World facts before lore.** Ask `AutonomousChatQuestions.AnswerWorld` (place levels, Darkness Falls, relics, keeps) before `LoreChat` and `LoreLegends`, and let lore answer only "what is / who is / whats in" questions, never one with level or ownership words. | ~40 | `AutonomousChatKnowledge.TryAnswer`, `SchedulePlayerReplies` |
| F2 | **Fill the knowledge holes** (C8): buying (potions, dyes, poison, gems, tools, bags, selling loot, "who sells"), the way into DF, the frontier and the battlegrounds (the portal keeps), armor weights from `AutonomousChatGear.ArmorWord`, main stats by class, crafting basics past the first step, NPC faction basics, kill tasks. Each answer from the code's or the world's own data. | ~45 | `AutonomousChatKnowledge` (new pure answers), tests |
| F3 | **Read where-to-level questions** (C4): a pattern (level words incl. "lvl", a number, "where/best/good/any") instead of the phrase list, ahead of class talk. | ~22 | `AutonomousChatShortTalk.QuestionShape` |
| F4 | **No "dunno" in realm channels** (C6): drop the rescue's dunno picks for /faction (silence is better than a non-answer, owner: "eliminate ... non answers"), and retire the 17-line "not sure" set there; keep a short honest "not sure" only when the bot was named or told. Pairs with F2 so fewer questions reach it. | 20 non-answers removed | `AutonomousChatReplyBudget.Rescue`, `VoicedChoiceCore`, `replies*.lines` |
| F5 | **Trainer answers that fit the class** (C7): "spells" only for a class with spells, "your" only for the asker's own class, a class of another realm said as such ("thats a hib class"). | 7 wrong, 2 non-answers | `AutonomousChatKnowledge.GenericTrainerAnswer`, `TryAnswerTrainer` |
| F6 | **Questions about groups aren't group posts** (C3): a line with a question word or a knowledge answer skips the grouping branch; knowledge before `noneNearLevel`. | 6 | `SchedulePlayerReplies`, `AutonomousChatIntentModel` |
| F7 | **No generic line in a known class's advice** (C5) and **"epic" is not a raid** (C2). | 9 | `AutonomousChatFieldTalk.SpecAdvice` / `WeaponAdvice`, `AsksAboutRealmRaid` |
| F8 | **Lore lines only for their own question** (C12): a written Q-and-A lore line is drawn only when the player's question asks what its opening asks. | 15 clunky | `LoreChat.WrittenLoreNaming` |
| F9 | **No laugh after a non-answer, a warning or a lore line** (C10). | 19 clunky | `AutonomousBotVoiceCorpus.TakesEmote`, `AutonomousChatIdiolect` |
| F10 | **Names keep their capitals** when a voice capitalizes (C11): capitalize the catalog's place names, capitals and bosses in code-built answers. | 14 clunky | `AutonomousBotVoices.StyleText` |
| F11 | Shape-specific corpus lines that name what the bot is doing (C9; "the usual, killing stuff" from a bot whose target is known), the bot's own pots, and faster typing for short facts (14 s median wait). | ~10 | corpus `replies*.lines`, `AutonomousChatQuestions.SelfQuestions`, `AutonomousChatArbiter.DelayMs` |

## The ratchet

Once the fixes land, the bench's good-outcome count, its WRONG count and its CLUNKY count become floors and ceilings
in `UT_ChatQuestionBench` (fail when the good count falls or a WRONG or CLUNKY appears that the baseline didn't have),
and `make_player_questions.py` gets a holdout set written after the fixes, so the fixes are not only fitted to these
questions. Until then the step is report-only.

## Worst examples

The 30 worst of each category, in question order (all of them where there are fewer). "why" names the root cause
above and what the scorer saw.

### Worst: WRONG (7, all shown)

| # | realm, channel, intent | question | reply | route | why |
|---:|---|---|---|---|---|
| 1 | alb faction trainer | where is the armsman trainer? | The armsman trainer in camelot, he trains your spells. | `knowledge` | C7: says "re:\bspells?\b" |
| 2 | alb faction trainer | who trains minstrels? | Camelot, your minstrel trainer is there. | `knowledge` | C7: says "re:\b(your\|ur) minstrel trainer" |
| 3 | alb faction trainer | who trains wizards? | Camelot, your wizard trainer is there. | `knowledge` | C7: says "re:\b(your\|ur) wizard trainer" |
| 4 | mid faction trainer | where is the warrior trainer? | the warrior trainer in jordheim, he trains ur spells | `knowledge` | C7: says "re:\bspells?\b" |
| 5 | mid faction trainer | who trains shadowblades? | the shadowblade trainer in jordheim, he trains ur spells | `knowledge` | C7: says "re:\bspells?\b" |
| 6 | hib faction trainer | where is the hero trainer? | the hero trainer in tir na nog, he trains ur spells | `knowledge` | C7: says "re:\bspells?\b" |
| 7 | hib faction trainer | who trains blademasters? | the blademaster trainer in tir na nog, he trains ur spells | `knowledge` | C7: says "re:\bspells?\b" |

### Worst: NON_ANSWER (27, all shown)

| # | realm, channel, intent | question | reply | route | why |
|---:|---|---|---|---|---|
| 1 | alb faction level_where | best xp for 21? | Dunno | `rescue (intent grinding)` | C4: a deflection with no fact |
| 2 | mid faction level_where | where can a 3 warrior solo? | not sure tbh haha | `rescue (none factual)` | C4: a deflection with no fact |
| 3 | mid faction level_where | best xp for 9? | no clue, sry haha | `rescue (intent grinding)` | C4: a deflection with no fact |
| 4 | hib faction level_where | where do i go after 9? | dunno | `rescue (not sure)` | C4: a deflection with no fact |
| 5 | hib faction level_where | good place to lvl at 18? | not sure tbh :P | `rescue (intent grinding)` | C4: a deflection with no fact |
| 6 | hib faction level_where | where can a 27 champion solo? | not sure tbh | `rescue (none factual)` | C4: a deflection with no fact |
| 7 | hib faction level_where | best xp for 33? | dunno | `rescue (intent grinding)` | C4: a deflection with no fact |
| 8 | alb faction travel | how do i get to the frontier? | hmm dunno | `rescue (none factual)` | C6: a deflection with no fact |
| 9 | mid faction travel | how do i get to the frontier? | hmm dunno | `rescue (none factual)` | C6: a deflection with no fact |
| 10 | hib faction travel | how do i get to the frontier? | hmm dunno | `rescue (none factual)` | C6: a deflection with no fact |
| 11 | alb faction trainer | where is the blademaster trainer? | no clue, sry | `rescue (not sure)` | C6: a deflection with no fact |
| 12 | mid faction trainer | where is the blademaster trainer? | hmm dunno :P | `rescue (not sure)` | C6: a deflection with no fact |
| 13 | alb faction rvr_status | is the frontier busy? | hmm dunno | `rescue (not sure)` | C6: a deflection with no fact |
| 14 | mid faction crafting | where do i learn weaponcrafting? | no idea | `rescue (not sure)` | C6: a deflection with no fact |
| 15 | hib faction crafting | how does spellcrafting work? | dunno | `rescue (none factual)` | C6: a deflection with no fact |
| 16 | alb faction items | is 89% quality good? | Not sure tbh | `rescue (not sure)` | C6: a deflection with no fact |
| 17 | mid faction items | what armor does a thane wear? | dunno | `rescue (none factual)` | C6: a deflection with no fact |
| 18 | hib faction items | what armor can a ranger wear? | no clue, sry | `rescue (none factual)` | C6: a deflection with no fact |
| 19 | alb faction items | what stats for a sorcerer? | Idk sry | `rescue (not sure)` | C6: a deflection with no fact |
| 20 | mid faction items | what stats should a druid get? | idk sry | `rescue (none factual)` | C6: a deflection with no fact |
| 21 | mid faction faction | what faction are the trolls in? | hmm dunno | `rescue (none factual)` | C6: a deflection with no fact |
| 22 | hib faction faction | how do i get the sylvan to like me? | not sure tbh | `rescue (intent travel)` | C6: a deflection with no fact |
| 23 | alb faction faction | how do i check my faction? | Not sure tbh | `rescue (not sure)` | C6: a deflection with no fact |
| 24 | alb tell bot_plans | got any pots? | Dunno sry | `short (none factual)` | C6: a deflection with no fact |
| 25 | mid group bot_plans | got any pots? | dunno :P | `rescue (none factual)` | C6: a deflection with no fact |
| 26 | mid faction terms | whats the command for a tell? | no clue, sry heh | `rescue (not sure)` | C6: a deflection with no fact |
| 27 | mid faction spec | whats better, warrior or thane? | dunno | `rescue (shape ask_choice)` | C6: a deflection with no fact |

### Worst: OFF_TOPIC (67, 30 shown)

| # | realm, channel, intent | question | reply | route | why |
|---:|---|---|---|---|---|
| 1 | alb faction level_where | any good spots for a 12 paladin? | Paladin chants carry a whole group. | `intent classtalk asked` | C4: none of: adribard's retreat, adribards, adribards retreat, avalon marsh |
| 2 | mid faction level_where | any good spots for a 36 healer? | a healer never waits long for a group | `intent classtalk asked` | C4: none of: bjarken, darkness falls, delling, delling crater |
| 3 | hib faction level_where | any good spots for a 24 enchanter? | enchanters are underrated | `intent classtalk asked` | C4: none of: ardagh, ardee, bri leith, caille |
| 4 | alb faction place_level | what lvl is avalon city? | What's in avalon city? Five drakoran lords and the way down to the crystal cave. | `knowledge` | C1: none of: re:\b50\+ |
| 5 | hib faction place_level | can a 50 solo in sheeroe hills? | where is cuuldurach? top of the sheeroe hills, up the pass from the bog of cullen lol | `knowledge` | C1: none of: re:\b45\+ |
| 6 | mid faction place_level | what lvl is east svealand? | queen major is in the ant camp south west, kill her soldiers and she comes out | `knowledge` | C1: none of: re:\b7\b.{0,12}\b13\b |
| 7 | mid faction place_level | gotar what lvl? | vidalf is west among the imps, vefyn south among the winds :D | `knowledge` | C1: none of: re:\b8\b.{0,12}\b17\b |
| 8 | alb faction place_level | is dartmoor ok at 39? | Albion's dragon lives at the bottom of dartmoor. Bring heat wards and a raid. | `knowledge` | C1: none of: re:\b39\b.{0,12}\b49\b |
| 9 | hib faction place_level | can a 50 solo in cursed forest? | whats in the cursed forest? juggernaut, the blight, cailean, caithor, develin, the faerie queen and more :P | `knowledge` | C1: none of: re:\b50\+ |
| 10 | alb faction place_level | whats the level range for devwy? | Devwy? High isle, hardened veterans to our best. Fort gwyntell is the town. | `knowledge` | C1: none of: re:\b37\+ |
| 11 | mid faction place_level | what lvl is iarnvidiurs lair? | is the lair hard? the trash is fine with cures, iarnvidiur needs a raid. thirty wasnt enough back in the day haha | `knowledge` | C1: none of: re:\b48\+ |
| 12 | mid faction place_level | spindelhalla what lvl? | spindelhalla? spider dungeon. good hunting, watch the webs :) | `knowledge` | C1: none of: re:\b37\b.{0,12}\b48\b |
| 13 | hib faction place_level | is balor ok at 31? | the eye of balor at tur suil watches everything, or so the sylvan say heh | `knowledge` | C1: none of: re:\b37\+ |
| 14 | alb faction place_level | can a 21 solo in isle of glass? | Whats there? Caer gothwaite, gothwaite harbor, the three wall forts, drakoran camps. | `knowledge` | C1: none of: re:\b5\b.{0,12}\b15\b |
| 15 | hib faction place_level | whats the level range for worlds end? | is worlds end hard? yes. our best everywhere, octonoid mothers and kontar and elro are big fights | `knowledge` | C1: none of: re:\b45\+ |
| 16 | hib faction place_level | what lvl is spraggon den? | who is eraliwyr? the alchemist for viraniels plague. deep in the den, near spriggit | `knowledge` | C1: none of: re:\b21\b.{0,12}\b25\b |
| 17 | mid faction place_level | vale of mularn what lvl? | prugor is at the lake vendo camp, inaksha's rod and the scholars quest | `knowledge` | C1: none of: re:\b3\b.{0,12}\b8\b |
| 18 | mid faction place_level | is vendo caverns ok at 24? | grjotgard is in the bear cave | `knowledge` | C1: none of: re:\b20\b.{0,12}\b28\b |
| 19 | alb faction place_level | can a 50 solo in sidi? | The fortress of caer sidi sits right on the road to annwn, and lost souls walk in and do not come back. | `knowledge` | C1: none of: re:\b50\+ |
| 20 | mid faction place_level | whats the level range for modernagrav? | you can walk in through the gap in faraheims eastern rockwall too | `knowledge` | C1: none of: re:\b42\+ |
| 21 | mid faction place_level | what lvl is myrkwood forest? | tingler webmother is west among the tinglers lol | `knowledge` | C1: none of: re:\b7\b.{0,12}\b27\b |
| 22 | hib faction place_level | galladoria what lvl? | deep in galladoria, hurionthex wears whatever shape it likes. believe nothing you meet there | `knowledge` | C1: none of: re:\b50\+ |
| 23 | alb faction place_level | is aldland ok at 34? | Whats there? Caer diogel, the krondon entrance, orc camps, lammia, griffins. | `knowledge` | C1: none of: re:\b40\+ |
| 24 | alb faction place_level | can a 2 solo in camelot hills? | Saw lady nimue in camelot hills once, walking like she owned the place. I didn't dare say hello. | `knowledge` | C1: none of: re:\b1\b.{0,12}\b7\b |
| 25 | alb faction place_level | whats the level range for avalon isle? | Whats there? Wearyall, the avalon city door, orchards, caer caddug, drakoran camps. | `knowledge` | C1: none of: re:\b25\b.{0,12}\b35\b |
| 26 | mid faction place_level | iarnwood what lvl? | iarnwood? dark woods, cure disease, dont upset the dwarves | `knowledge` | C1: none of: re:\b45\+ |
| 27 | alb faction place_level | is lyonesse ok at 50? | Where's blythe? The broken cathedral in the middle. You can see the walls from miles off. | `knowledge` | C1: none of: re:\b36\b.{0,12}\b48\b |
| 28 | alb faction place_level | can a 50 solo in gwyddneau? | Whats there? Clifton the lammia town, coliath, a viking wreck, orc prospectors, marw gwlad. | `knowledge` | C1: none of: re:\b50\+ |
| 29 | hib faction place_level | whats the level range for treibh caillte? | treibh caillte, the lost tribe caves. ursine, spiders, golems. long walk in, decent once you get there | `knowledge` | C1: none of: re:\b31\b.{0,12}\b39\b |
| 30 | alb faction place_level | what lvl for abermenai? | Who holds abermenai? Renegades, under the chieftain abermenai. Hes a seasoned band's foe and then some. | `knowledge` | C1: none of: re:\b15\b.{0,4}\b19\b |

### Worst: FLUFF (2, all shown)

| # | realm, channel, intent | question | reply | route | why |
|---:|---|---|---|---|---|
| 1 | mid faction small_talk | happy halloween all! | hi | `intent greeting` | C9: content-free |
| 2 | alb faction small_talk | this camp is dead lol | heh | `intent banter` | C9: content-free |

### Worst: SILENT (67, 30 shown)

| # | realm, channel, intent | question | reply | route | why |
|---:|---|---|---|---|---|
| 1 | alb faction level_where | good place to lvl at 6? | (nothing) | `silent (intent grinding)` | C4: nobody answered |
| 2 | alb faction level_where | where can a 15 minstrel solo? | (nothing) | `silent (none factual)` | C4: nobody answered |
| 3 | alb faction level_where | im 24, where should i go now? | (nothing) | `silent (not sure)` | C4: nobody answered |
| 4 | alb faction level_where | where do i go after 33? | (nothing) | `silent (not sure)` | C4: nobody answered |
| 5 | alb faction level_where | good place to lvl at 44? | (nothing) | `silent (intent grinding)` | C4: nobody answered |
| 6 | mid faction level_where | im 12, where should i go now? | (nothing) | `silent (not sure)` | C4: nobody answered |
| 7 | mid faction level_where | where do i go after 21? | (nothing) | `silent (none factual)` | C4: nobody answered |
| 8 | mid faction level_where | good place to lvl at 30? | (nothing) | `silent (intent grinding)` | C4: nobody answered |
| 9 | mid faction level_where | where can a 40 warrior solo? | (nothing) | `silent (not sure)` | C4: nobody answered |
| 10 | mid faction level_where | best xp for 48? | (nothing) | `silent (intent grinding)` | C4: nobody answered |
| 11 | hib faction level_where | im 36, where should i go now? | (nothing) | `silent (not sure)` | C4: nobody answered |
| 12 | hib faction level_where | where do i go after 48? | (nothing) | `silent (not sure)` | C4: nobody answered |
| 13 | alb faction buy_where | who sells horse tickets? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 14 | mid faction buy_where | where do you buy potions? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 15 | hib faction buy_where | where can i buy dyes | (nothing) | `silent (none factual)` | C8: nobody answered |
| 16 | alb faction buy_where | where do i get spellcraft gems? | (nothing) | `silent (none factual)` | C8: nobody answered |
| 17 | alb faction buy_where | where can i buy poison? | (nothing) | `silent (none factual)` | C8: nobody answered |
| 18 | mid faction buy_where | where do i get crafting tools | (nothing) | `silent (none factual)` | C8: nobody answered |
| 19 | mid faction buy_where | where do i buy food for my horse lol | (nothing) | `silent (intent trading)` | C8: nobody answered |
| 20 | alb faction buy_where | who sells rings and necklaces? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 21 | hib faction buy_where | where do i buy bags? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 22 | alb faction buy_where | where do i sell my loot? | (nothing) | `silent (none factual)` | C8: nobody answered |
| 23 | mid faction buy_where | wheres the realm exchange? | (nothing) | `silent (none factual)` | C8: nobody answered |
| 24 | alb faction travel | how do i get into df? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 25 | alb faction travel | how do i get to the battlegrounds? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 26 | mid faction travel | how do i get into df? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 27 | mid faction travel | how do i get to the battlegrounds? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 28 | hib faction travel | how do i get into df? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 29 | hib faction travel | how do i get to the battlegrounds? | (nothing) | `silent (not sure)` | C8: nobody answered |
| 30 | hib faction trainer | where is the skald trainer? | (nothing) | `silent (not sure)` | C8: nobody answered |

### Worst: CLUNKY (43, 30 shown)

| # | realm, channel, intent | question | reply | route | why |
|---:|---|---|---|---|---|
| 1 | mid faction level_where | where can a 3 warrior solo? | not sure tbh haha | `rescue (none factual)` | tacked_laugh: haha |
| 2 | mid faction level_where | best xp for 9? | no clue, sry haha | `rescue (intent grinding)` | tacked_laugh: haha |
| 3 | hib faction level_where | good place to lvl at 18? | not sure tbh :P | `rescue (intent grinding)` | tacked_laugh: :P |
| 4 | alb faction place_level | what lvl is avalon city? | What's in avalon city? Five drakoran lords and the way down to the crystal cave. | `knowledge` | own_question: whats in avalon city; case_mix: avalon |
| 5 | hib faction place_level | can a 50 solo in sheeroe hills? | where is cuuldurach? top of the sheeroe hills, up the pass from the bog of cullen lol | `knowledge` | tacked_laugh: lol; own_question: where is cuuldurach |
| 6 | hib faction place_level | can a 50 solo in cursed forest? | whats in the cursed forest? juggernaut, the blight, cailean, caithor, develin, the faerie queen and more :P | `knowledge` | tacked_laugh: :P; own_question: whats in the cursed forest |
| 7 | mid faction place_level | what lvl is iarnvidiurs lair? | is the lair hard? the trash is fine with cures, iarnvidiur needs a raid. thirty wasnt enough back in the day haha | `knowledge` | tacked_laugh: haha; own_question: is the lair hard |
| 8 | mid faction place_level | spindelhalla what lvl? | spindelhalla? spider dungeon. good hunting, watch the webs :) | `knowledge` | tacked_laugh: :) |
| 9 | alb faction place_level | can a 21 solo in isle of glass? | Whats there? Caer gothwaite, gothwaite harbor, the three wall forts, drakoran camps. | `knowledge` | own_question: whats there |
| 10 | hib faction place_level | what lvl is spraggon den? | who is eraliwyr? the alchemist for viraniels plague. deep in the den, near spriggit | `knowledge` | own_question: who is eraliwyr |
| 11 | alb faction place_level | is aldland ok at 34? | Whats there? Caer diogel, the krondon entrance, orc camps, lammia, griffins. | `knowledge` | own_question: whats there |
| 12 | alb faction place_level | can a 2 solo in camelot hills? | Saw lady nimue in camelot hills once, walking like she owned the place. I didn't dare say hello. | `knowledge` | case_mix: camelot |
| 13 | alb faction place_level | whats the level range for avalon isle? | Whats there? Wearyall, the avalon city door, orchards, caer caddug, drakoran camps. | `knowledge` | own_question: whats there; case_mix: avalon |
| 14 | alb faction place_level | is lyonesse ok at 50? | Where's blythe? The broken cathedral in the middle. You can see the walls from miles off. | `knowledge` | own_question: wheres blythe |
| 15 | alb faction place_level | can a 50 solo in gwyddneau? | Whats there? Clifton the lammia town, coliath, a viking wreck, orc prospectors, marw gwlad. | `knowledge` | own_question: whats there |
| 16 | alb faction place_level | what lvl for abermenai? | Who holds abermenai? Renegades, under the chieftain abermenai. Hes a seasoned band's foe and then some. | `knowledge` | own_question: who holds abermenai |
| 17 | mid faction place_level | what lvl for abermenai? | who holds abermenai? renegades, under the chieftain abermenai. hes a seasoned band's foe and then some | `knowledge` | own_question: who holds abermenai |
| 18 | hib faction place_level | is abermenai open for lvl 14? | where is abermenai? ask any of the realm's porters for battlegrounds once youre nearly seasoned | `knowledge` | own_question: where is abermenai |
| 19 | mid faction place_level | is cale open for lvl 37? | where is cale? ask one of the realm's porters for battlegrounds once youre well seasoned | `knowledge` | own_question: where is cale |
| 20 | alb faction buy_where | where can i buy a better sword? | Realm exchange in camelot, or get a weaponcrafter to make you one. | `knowledge` | case_mix: camelot |
| 21 | alb faction buy_where | where can i get a cloak | Mostly drops, but check the realm exchange in camelot. | `knowledge` | case_mix: camelot |
| 22 | alb faction buy_where | where is the realm exchange in camelot? | Take a horse to camelot, the roads all lead there. | `knowledge` | case_mix: camelot |
| 23 | mid faction travel | how do i get to prydwen keep? | prydwen is in albion, we cant go there heh | `knowledge` | tacked_laugh: heh |
| 24 | hib faction travel | where is cullen? | bog of cullen? take a horse from a stable master :P | `knowledge` | tacked_laugh: :P |
| 25 | hib faction travel | wheres ardee | ardee? thats in Connacht haha | `knowledge` | tacked_laugh: haha |
| 26 | hib faction travel | where is allta fearann? | allta fearann? take a horse from a stable master :) | `knowledge` | tacked_laugh: :) |
| 27 | hib faction travel | where is svasud faste? | svasud faste is in midgard, we cant go there haha | `knowledge` | tacked_laugh: haha |
| 28 | alb faction trainer | where is the armsman trainer? | The armsman trainer in camelot, he trains your spells. | `knowledge` | case_mix: camelot |
| 29 | alb faction trainer | where is the cleric trainer? | Your cleric trainer in camelot. | `knowledge` | case_mix: camelot |
| 30 | alb faction trainer | where is the cabalist trainer? | The cabalist trainer in camelot, he trains your spells. | `knowledge` | case_mix: camelot |
