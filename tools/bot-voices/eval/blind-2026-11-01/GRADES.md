# Chat reply grades

400 items graded on the first reply. 15 items show only a placeholder token ([from the world], [JoinReply], [where the bot is]) instead of text; they were scored 1/1/1 and are excluded from the main averages below.

## Averages (0-2)

| Scope | n | answers | era | human | total /6 | answers = 0 |
|---|---|---|---|---|---|---|
| All gradeable | 385 | 1.02 | 1.27 | 1.15 | 3.45 | 132 (34%) |
| All 400 (incl. placeholders) | 400 | 1.02 | 1.26 | 1.15 | 3.43 | 132 (33%) |
| faction | 74 | 0.65 | 1.16 | 0.95 | 2.76 | 34 (46%) |
| group | 87 | 1.31 | 1.34 | 1.40 | 4.06 | 20 (23%) |
| guild | 112 | 1.17 | 1.37 | 1.19 | 3.72 | 31 (28%) |
| lfg | 14 | 0.79 | 1.14 | 1.14 | 3.07 | 5 (36%) |
| say | 52 | 1.04 | 1.19 | 1.12 | 3.35 | 20 (38%) |
| tell | 32 | 0.88 | 1.25 | 1.09 | 3.22 | 14 (44%) |
| trade | 14 | 0.57 | 1.14 | 0.64 | 2.36 | 8 (57%) |
| Text replies only | 301 | 0.98 | 1.35 | 0.97 | 3.30 | 116 (39%) |
| "(no reply)" only | 84 | 1.18 | 1.00 | 1.81 | 3.99 | 16 (19%) |

Second reply vs first (where present): better 29, same 176, worse 38.

## Most common problems

1. **Stock deflection ("no idea / ask around / beats me") to answerable questions** - 41 items
   - b059 (say) "where is the armor trainer?" -> "ask around maybe"
   - b063 (faction) "where is the entrance to DF" -> "beats me"
2. **Ignores or misreads the actual request** - 34 items
   - b046 (guild) "cya all next week, going out of town" -> "bring speed if you have it"
   - b130 (say) "help! im stuck under this rock" -> "Run to the guards if it goes bad."
3. **Canned quip or aphorism ("safe travels", "the grind pays off", "mobs will still be here")** - 27 items
   - b343 (faction) "zerg left the keep" -> "the zerg moved on i think"
   - b132 (faction) "help! train coming out of the dungeon" -> "Careful."
4. **Silence where a real player would have answered** - 24 items
   - b020 (tell) "hi! long time no see" -> "(no reply)"
   - b045 (group) "bed time for me, gn" -> "(no reply)"
5. **Stock non sequitur ("i think so?", "good question", "someone said something about that earlier", "pretty sure yes")** - 24 items
   - b021 (tell) "hey u there?" -> "someone said something about that earlier"
   - b072 (group) "which way to the camp" -> "i think so?"
6. **Canned line from the wrong situation (LFG/RvR/trade filler pasted into another context)** - 18 items
   - b075 (lfg) "where is the best place to solo at 22 as a hunter?" -> "the stable master saves you half the walk"
   - b110 (group) "can we stay one more pull?" -> "need a healer still?"
7. **Does not know its own state (own name, item, hunt, directions, plan)** - 14 items
   - b019 (group) [ctx: A new member joined the group.] "hello, what are we hunting?" -> "not a clue honestly"
   - b023 (guild) "hey Thoriel, you on tonight?" -> "hmm not sure"
8. **Wrong emotional register (agrees with insults, "lol so true" to praise, mocks milestones)** - 11 items
   - b091 (guild) "how many rp for rr2?" -> "nice, gz"
   - b131 (say) "help please, this mob is killing me" -> "dont let it get to you"
9. **LFG/LFM roles inverted or invite flow broken** - 8 items
   - b156 (guild) "inv pls" -> "need a healer still?"
   - b326 (faction) "lfg pl0x" -> "30 thane here if you need one"
10. **Formal, preachy or data-dump register** - 7 items
   - b198 (group) "sb on me!!" -> "thanks for the heads up, watching"
   - b274 (tell) "u suck" -> "lets keep it friendly"

Also: 15 placeholder items; 4 asks-for-given-info; 7 formal/prose register.

## 15 worst items

| id | channel | player | first reply | a/e/h | problem |
|---|---|---|---|---|---|
| b315 | group | ty for rez, how much xp did i lose? | try the exchange, prices are fair | 0/0/0 | Exchange non sequitur to xp question |
| b019 | group | hello, what are we hunting? (ctx: A new member joined the group.) | not a clue honestly | 0/1/0 | groupmate doesn't know own hunt |
| b021 | tell | hey u there? | someone said something about that earlier | 0/1/0 | stock non sequitur to presence check |
| b023 | guild | hey Thoriel, you on tonight? | hmm not sure | 0/1/0 | named player can't say if on |
| b046 | guild | cya all next week, going out of town | bring speed if you have it | 0/1/0 | treats real-life trip as in-game |
| b059 | say | where is the armor trainer? | ask around maybe | 0/1/0 | deflects basic town question |
| b063 | faction | where is the entrance to DF | beats me | 0/1/0 | deflects common-knowledge DF question |
| b072 | group | which way to the camp | i think so? | 0/1/0 | 'i think so?' to which-way question |
| b075 | lfg | where is the best place to solo at 22 as a hunter? | the stable master saves you half the walk | 0/1/0 | irrelevant travel tip to camp question |
| b091 | guild | how many rp for rr2? | nice, gz | 0/1/0 | congratulates a question |
| b110 | group | can we stay one more pull? | need a healer still? | 0/1/0 | LFG line inside active group |
| b130 | say | help! im stuck under this rock | Run to the guards if it goes bad. | 0/1/0 | misreads stuck as a fight |
| b131 | say | help please, this mob is killing me | dont let it get to you | 0/1/0 | emotional platitude to urgent help |
| b156 | guild | inv pls | need a healer still? | 0/1/0 | misreads inv request |
| b169 | trade | selling mc chain set lvl 40, 3p obo | how much are you asking? | 0/1/0 | asks price already stated |

Worst list is capped at two items per problem type for variety; about 95 text replies score 1/6 or lower.
