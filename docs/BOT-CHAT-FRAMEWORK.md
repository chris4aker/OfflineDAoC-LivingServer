# The bot chat framework (1.2)

Owner 2026-11-01. The design the 1.2 chat work builds to. Evidence: [research/1.2/REPORT.md](research/1.2/REPORT.md)
and its strand notes; the measurement it starts from: [bot-voices/CHAT-EVAL.md](bot-voices/CHAT-EVAL.md) (blind read
rate 53%, replies 1.02 / 1.27 / 1.15 of 2 on answers / era / human, 34% of replies ignore what was said, silence scores
better than the average reply, a blind judge spots 72% of bot lines).

Every rule below is labelled **[E]** (period evidence, cited in the research), **[D]** (a borrowed design, fit inferred)
or **[O]** (an owner choice that is not period-accurate and says so).

## 1. Goals, in order

1. **Reciprocal:** bots notice the player, reach out first, and their memory shows in what they say. [D: Erenshor,
   Comme il Faut; treated as directions, not proof]
2. **Fallible and uneven:** most bots say little, a few talk a lot; some answers are "dunno" or plausibly wrong;
   skill and manners vary by personality. [E: Peña and Hancock as direction; bias audit]
3. **Period-correct in form:** the channels, words and rituals of 2001–2003 DAoC. [E]
4. **Never the bot tell:** no mass identical reactions, no filler that fits anything, no prose. [E: blind test, OSRS]
5. **Explainable:** every line can say why it was chosen. [D: Valve, Ruskin]

The tie-breaker when goals 1 and 3 conflict is aliveness, with authenticity breaking ties within it (owner priority).

## 2. The channels, as they were by patch 1.65 [E]

| Talk | Where it goes now | Where it goes in 1.2 |
|---|---|---|
| Looking for group | `/lfg` channel | The **LFG flag** (the bot appears in the player's LFG window), short "lfg 24 ranger" lines in **/say** and town **/broadcast**, guild chat, and chat groups |
| Selling and buying | `/trade` channel | **/broadcast** macro lines in towns ("Selling ... for ..."), tells, /say at the Exchange |
| Realm-wide talk | `/faction` | **Chat groups** (`/chat`, public groups, up to 200 from 1.60, 50 before), guild and alliance chat; realm-wide shouting only where the base game has it |
| Questions | anywhere | **/advice** (advisors needed 15 hours played) and guild chat |
| Battlegroups | — | none before 1.66 (28 Oct 2003) |
| Newbie talk | — | **newbie guild chat** for low-level bots (1.62, four per realm), which they leave when they join a real guild |

A convenience `/lfg` or `/faction` channel may stay only as a switch labelled **[O]**, off by default for the period
mode. Every era fact gets a source date, so a feature appears from its patch on.

## 3. The pipeline

```
event or player line
   → Query: concept (what happened), speaker candidates, channel, world facts, speaker's memory, social facts
   → Rules: every rule whose criteria all pass; score = number of criteria (most specific wins); ties random
   → Arbitration: one speaker per channel moment, reactors-per-trigger cap, odds of silence, cooldowns
   → Fallback: SILENCE (except a direct tell or a line naming the bot: a short human acknowledgement)
   → Render: persona and idiolect (case, typos, abbreviations), timing (reading and typing delay)
   → Write-back: facts with expiry ("answered:where_df", "said_gn_to:Chris", "speaking:guild"), social facts
   → /lswhy record
```

### 3.1 Concepts and rules [D: Valve L4D2, Firewatch]

- A **concept** is what happened: `player_line`, `player_ding`, `player_logged_in`, `named_spotted`, `keep_taken`,
  `group_wipe`, `remarkable_seen`, `followup_where` and so on.
- A **rule** is a corpus line (or a line family) plus criteria. The existing line fields are already criteria: kind,
  topic, who (personality), realm, classes, level band, needs (flags such as `at:`, `going:`, `night`), intent, mood,
  answers. New criteria: channel, concept, a fact (`fact:answered:where_df=false`), a social fact
  (`knows_player`, `grouped_with_player_recently`, `grudge:player`), a persona.
- **Specificity:** a rule's score is the number of criteria it names and passes. A vague rule (only `kind`) can win only
  as an opener or ambient line, never as a reply: **a reply needs a minimum score**, below which the answer is silence.
  This one change targets the blind test's largest failure groups (brush-offs, non sequiturs, wrong-situation filler).
- The line files stay the source; the loader turns their tags into criteria. Generic filler lines (`good question`,
  `happy to help`, `sounds familiar`) are marked `@generic` and allowed only where the evaluation shows they read human.

### 3.2 Facts, with expiry [D]

A flat store per scope: **world** (keep owners, raids, relics; read from the live server), **channel** (the topic of
the last 3–5 minutes, who asked what, what was answered), **speaker** (what this bot just did or said), **player**
(what the player said: going to bed, LFG, where he's headed). Every fact has an expiry. Rules read them; responses write
them. Examples:
- "bed time for me, gn" writes `player:leaving` → the gn ritual fires, "safe travels" doesn't.
- An answered "where is the DF entrance" writes `channel:answered:where_df` → no second bot repeats it.
- A follow-up "where?" re-queries with `channel:topic=camp:east_hill`.

### 3.3 The social-facts log [D: Comme il Faut]

A persistent, queryable log of labelled events between characters: actor, target, label (`grouped`, `wiped_with`,
`rezzed`, `ks`, `insulted`, `helped`, `traded`, `ninja`, `gz`), time, affinity change. The existing memory (bots
remember the player and each other, grudges, nemeses) feeds it. It lets dialogue **name shared history** ("you were in
that wipe at the barrows last night", "ty for the rez yesterday") and lets rules gate on relationships. A rule budget
is set from day one (Comme il Faut needed over 5,000 rules; the scope here stays small).

### 3.4 Reach out first [D]

A small **social circle per player** (a handful of bots who have grouped with or helped him, capped as it fills): they
greet him by name when he logs in or passes, whisper an invite when they need his class, ask him to a raid, and congratulate
his dings, from the social facts. How often new bots befriend the player is capped, so the circle grows like a real one.

### 3.5 Arbitration and anti-uniformity [E: OSRS, mod-ollama-chat; D]

- **One speaker per channel moment** (a short speaking lock per channel).
- **Reactors per trigger** capped (one to three bots answer a player line, a few more for a ding or a keep fall), with
  jittered delays: never hundreds of bots answering one "How Many".
- **Odds of silence** per rule and per persona; talkativeness skewed (most bots near-silent, a few regulars).
- **Audience rule:** bot-to-bot chatter mostly where the player can see it, and it decays (chains end).
- **Who speaks:** the best-fitting candidate (the healer for a healer question, a bot that knows the player for his
  tell, the nearest for a call), not a random one.

### 3.6 Fallibility [bias fix]

- **Advice by experience tier:** newbies ask, regulars answer, veterans answer more; every tier has an error budget:
  "dunno", "try /advice", a plausibly wrong direction. The evaluation rubric scores these as human, not as failures.
- **Manners by personality:** most follow the period etiquette (shared camps, turn-taking, no buff begging, no KS); a few
  don't, and the realm talks about them.

### 3.7 Personas and idiolects [D: Watch Dogs: Legion]

About 20 personas set what a bot talks about, how often and in which register (the current five voices become one axis
of them). Each bot also gets small idiolect quirks (a pet phrase, a typo habit, emote use) fixed from its id, so two
bots never sound identical.

### 3.8 Remarkables [D]

Things worth a comment, tagged in the world: a named monster near, a red con walking past, the dragon flying, a keep
changing hands, a corpse on the road, a player in epic armour, a level 50 riding through a newbie zone. A bot that sees
one may comment once (cooldown per remarkable per area). Remarkables replace the lore musings that the spot-the-bot judge
found most bot-like; musings stay only as rare chat-group openers.

### 3.9 Storylets [D: quality-based narrative]

Slow multi-step threads (a guild drama, a rivalry, a recruitment arc, a running joke) as storylets unlocked by social
facts and world state, played over days. They use the same rules and arbitration.

### 3.10 Pacing [D: RimWorld as a source of ideas]

A chat director reads the player's state (fighting, idling in town, levelling, RvR) and the recent intensity: calls only
during a fight, a burst of reactions after a big moment, then a lull; more talk while the player idles in town. Only
ideas are taken from RimWorld; the numbers come from soak runs.

### 3.11 /lswhy [D: Valve 2009]

A GM command: target a bot (or name the last line) and see the concept, the winning rule, its criteria and score, the
runners-up, the facts read and written, and why others stayed silent. Every bot state change also logs a reason string.

## 4. Evaluation, rebuilt [bias fix]

- **Anchor to real period text.** 300–500 verbatim, dated 2001–2003 lines (the Prima guide, FreddysHouse chat-log
  threads, Usenet, academic corpora of neighbouring games) used only as the human side of spot-the-bot and as a style
  reference, never as bot lines.
- **Judges from a different model family** than the writer, with the model recorded for every role; and a **human
  panel** (the owner, r/daoc volunteers) for spot-the-bot each release.
- **A new blind corpus every round;** own-set scores kept only as regression tests.
- **Rubric changes:** "dunno" and plausibly wrong answers can score human; silence is scored against whether a player
  would have answered.
- **Soak metrics:** lines per minute per channel, reactors per trigger, repeats, chain length, speaking-lock conflicts.
- **Gates for 1.2:** blind read rate 80%, replies 1.5 / 1.5 / 1.5, ignore-rate under 15%, spot-the-bot under 50%
  (machine judge) and the human panel no better than chance on short lines.

## 5. Build order

| Step | What | Measured by |
|---|---|---|
| C0 | Reason strings, `/lswhy`, the fact store (empty), the rubric changes, the period-text anchor set | the tools exist; the anchor set is dated |
| C1 | Channels moved to the period ones (§2) and the LFG flag; era facts get source dates | no line in a channel that didn't exist |
| C2 | Specificity scoring with a reply minimum and silence as the fallback; arbitration (§3.5) | blind round 2 |
| C3 | Facts with expiry and follow-ups (§3.2) | multi-turn test set |
| C4 | Social-facts log and reaching out (§3.3, §3.4) | the owner's play session; social circle metrics |
| C5 | Personas and idiolects; fallibility (§3.6, §3.7) | blind round 3; human panel |
| C6 | Remarkables (§3.8) | spot-the-bot "musings" tell gone |
| C7 | Storylets and the pacing director (§3.9, §3.10) | soak metrics at full roster |

Each step ships to `release/1.2` only with its measurement recorded here.
