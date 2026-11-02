# NPC dialogue, bark and conversation systems: a comparative survey for autonomous bot chat (Offline DAoC)

Scope note: this research ran on a budget of about 18 tool calls. Several systems on the brief could not be fetched or verified in this session, including the Valve wiki, which is blocked by the allowlist. Every "Cited Findings" bullet comes from a page fetched or found in this session. Anything from the researcher's background knowledge sits under "Inferences" or "Gaps" and is labelled **[unverified this session]**. The report writer should treat those items as leads to verify, not as facts.

## 1. Which systems exist, and how does each pick what to say?

### Takeaway
The verified shipped approaches fall into three families:
- **Salience-based rule matching:** Valve's L4D2 response rules and Firewatch. The most specific rule that matches the current world state wins, and generic rules act as fall-backs.
- **Quality-based storylets:** Fallen London / StoryNexus. Content is unlocked by numeric "qualities".
- **Context-aware combat dialogue:** Naughty Dog's *The Last of Us*. Selection draws on individual knowledge, group knowledge and the environment.

Procedural casts (Watch Dogs: Legion) solved scale with **personas**, a fixed set of 20 voice/writing archetypes, rather than per-character writing.

### Cited Findings
- **Valve, Elan Ruskin, GDC 2012, "AI-driven Dynamic Dialog through Fuzzy Pattern Matching. Empower Your Writers!"**
  - Dialogue snippets are matched against a continually changing world state by a rule-based system, used in Left 4 Dead 2 and other Valve games — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/); [GDC Vault session](https://www.gdcvault.com/play/1015528/AI-driven-Dynamic-Dialog-through)
  - Like Inform, the system applies "the most specific one it can find," and less-specific rules act as fall-backs — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
  - Its stated design goals:
    - Writers can produce a lot of content easily.
    - It handles very specific situations, such as a special quip when a character is attacked in a circus environment.
    - Exchanges are interruptible: characters break off when one comes under attack.
    — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
  - The talk covered optimization of rule lookups and building tools that writers are comfortable using. The summary gives no figures — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
  - The slides and video were hosted at assemblyrequired.crashworks.org. One commenter said only the embedded videos, not the full talk, were posted there — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
- **Salience-based narrative (Emily Short's term, 2016)**
  - Content is chosen from a pool by asking which element "is judged to be most applicable at the moment." Dialogue is tagged with conditions, the most salient match wins, and ties are broken at random.
  - Short's examples are Ruskin's L4D system and **Firewatch**, both of which tie dialogue to the 3D world state.
  — [Emily Short, "Beyond Branching", 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)
  - Strengths:
    - Authors can start with broad defaults and add more specific lines over time.
    - New content rarely breaks old content.
  - Weaknesses:
    - Sequencing by salience can produce badly paced results; Short's example is *The King of Chicago*.
    - Tuning the numeric values is "largely trial and error."
  — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)
- **Quality-based narrative (Failbetter, Fallen London / StoryNexus)**
  - Storylets are short passages unlocked by numeric "qualities" such as inventory, skills or relationship progress.
  - Strengths:
    - Content is modular, so many authors can add to it at low risk.
    - It handles gathering information in any order well.
  - Weaknesses:
    - Authors must track progress by hand with qualities, which Short calls "a lot of bookkeeping."
    - The system feels thin until the database is large.
  — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/); see also [simple-qbn explainer](https://videlais.github.io/simple-qbn/qbn)
- **Waypoint narrative (Short's term; used in *Glass*)**
  - Conversation topics are nodes, and lines are attached to the transitions between topics.
  - NPCs "pathfind" through topics toward designated target topics.
  — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)
- **The Last of Us, Jason Gregory (Naughty Dog), GDC 2014, "A Context Aware Character Dialog System"**
  - Enemies and allies talk to each other and to the player based on what each character knows, what the group collectively knows, the game state and the environment.
  - The system gives sound designers direct control, which frees programmers to work on AI.
  — [GDC Vault 2014](https://www.gdcvault.com/play/1020386/A-Context-Aware-Character-Dialog)
  - A related talk from the same GDC: "Ellie: Buddy AI in The Last of Us" — [GDC Vault](https://gdcvault.com/play/1020364/Ellie-Buddy-AI-in-The); [Game Developer](https://www.gamedeveloper.com/design/ellie-s-buddy-ai-in-i-the-last-of-us-i-explained-at-gdc-2014)
- **Watch Dogs: Legion (Ubisoft Toronto, GDC 2023 coverage)**
  - Writers created **20 personas** keyed to age and gender. The count started at 10 and was doubled to cover London's dialects and accents.
  - Writers could not know a recruit's age, gender or occupation, so lines could not reference those traits.
  - Every writer worked on every character. The mission writer drafted a base script, persona writers adapted it, and the mission owner revised the result.
  — [Game Developer, 2023](https://gamedeveloper.com/gdc2023/how-i-watch-dogs-legion-i-made-ubisoft-rethink-its-storytelling)
  - Continuity came from authored **Anchors**, uncontrollable antagonists that generated characters react to, and from generated **Profiler bios**. The bio details were deliberately made "weird" so players would remember them — [Game Developer, 2023](https://gamedeveloper.com/gdc2023/how-i-watch-dogs-legion-i-made-ubisoft-rethink-its-storytelling)
  - A writer described the early prototypes as "a tug of war where neither side is fully holding the rope" — [Game Developer, 2023](https://gamedeveloper.com/gdc2023/how-i-watch-dogs-legion-i-made-ubisoft-rethink-its-storytelling)
  - The systemic backbone was the "Census" system (GDC talk) — [GDC Vault](https://gdcvault.com/play/1027018/Census-The-Systemic-Backbone-Behind)
- **Hades (Supergiant, Greg Kasavin)**
  - All dialogue is fully voiced. The GDC 2021 talk "Breathing Life into Greek Myth: The Dialogue of Hades" covered optimizing the script for audio production and how Early Access shaped the narrative — [Game Developer, 2021](https://gamedeveloper.com/audio/dive-into-the-dialogue-of-i-hades-i-at-gdc-2021); [GDC Vault](https://gdcvault.com/play/1026975/Breathing-Life-into-Greek-Myth)
  - Kasavin's design goal is reactivity to small player circumstances. Dionysus comments on the player's nectar, Ares on their weapon choice, and characters acknowledge the death cycle itself: "why not have the characters acknowledge and be self aware of the cycle?" — [ScreenHub interview](https://www.screenhub.com.au/news-article/features/digital/jini-maxwell/hades-greg-kasavin-breaks-down-supergiants-unique-approach-to-narrative-262459)
  - Neither source fetched in this session describes the priority mechanism itself.

### Inferences
- **[unverified this session] Valve response rules, from background knowledge of the Ruskin talk and the Valve Developer Wiki "Response System" page.** The wiki page was blocked by the allowlist.
  - **Query.** The game fires a *concept*, such as `TLK_HURT`, together with a dictionary of facts merged from the speaker, the world and the event.
  - **Rules.** Each rule is a set of *criteria* tests on those facts. The rule whose criteria all pass and that has the most criteria (weighted) wins.
  - **Responses.** The winning rule points to a *response group* of lines. Response flags include `norepeat`, `speakonce`, `odds`, `respeakdelay`, `predelay`, `sequential` and `permitrepeat`.
  - **State.** Rules can write facts back into speaker or world "contexts" with an expiry duration, using `ApplyContext`. This gives "once per" memory and conversation threads: a follow-up line requires a fact set by the previous line.
  - **Scale and speed.** Ruskin reportedly described thousands of rules evaluated quickly through sorted criteria and binary search.
  - These specifics need verification against the [GDC Vault video](https://www.gdcvault.com/play/1015528/AI-driven-Dynamic-Dialog-through) or the Valve wiki before the report states them as fact.
- **[unverified this session] Hades.**
  - Lines carry requirement predicates and priority tiers: "essential" story lines outrank ambient ones.
  - Each line is played once, and the eligible pool is consumed over time.
  - The game reportedly has more than 21,000 voiced lines; a Multiplayer.it headline says Hades II has 50% more.
  - Not verified here.
- **[unverified this session] Systems not researched here because of the budget:**
  - Bethesda Radiant AI and the Story Manager (Skyrim): quest and dialogue "events" filtered by conditions, used for NPC–NPC ambient conversations.
  - Hitman's crowd and bark systems.
  - Uncharted's combat dialogue.
  - Shadow of Mordor's Nemesis dialogue: memory of prior encounters, such as scars and how they died.
  - Façade (Mateas & Stern, 2005): a drama manager that sequences "beats" toward a tension arc.
  - Versu (Evans & Short, about 2013–14): social practices as concurrent, reactive state machines that propose actions to agents.
  - Prom Week and Comme il Faut (McCoy et al., 2012–14): social-exchange rules with volition weighting.
  - Crusader Kings event chains.
  - Ink and Yarn Spinner storylet patterns.
  - Guild Wars 2 ambient dialogue.
  - Dragon Age party banter: banter fires on a timer or area trigger while exploring, gated by companion pairs and plot flags. Players complain about repeated banter (EA forum thread found: [EA forums](https://forums.ea.com/discussions/dragon-age-franchise-discussion-en/repeating-banter-is-getting-annoying/7364760/replies/7364766)).
  - Darkest Dungeon's affliction and virtue barks: [Game Developer deep dive on the affliction system](https://gamedeveloper.com/design/game-design-deep-dive-i-darkest-dungeon-s-i-affliction-system) was found but not read.
  - Disco Elysium.

### Gaps
- No primary detail was fetched for Valve's rule syntax, scores, flags or context expiry. The Valve wiki was blocked, and the Emily Short summary is high level.
- Nothing was fetched on the Hades priority and requirement mechanism. The GDC 2021 video would be the source.
- The following were not researched in this session:
  - Bethesda Radiant / Story Manager
  - Hitman
  - Uncharted
  - Shadow of Mordor
  - Façade
  - Versu
  - Prom Week / CiF
  - Crusader Kings
  - Ink / Yarn Spinner
  - Guild Wars 2
  - BioWare banter internals
  - Darkest Dungeon internals
  - Disco Elysium
- A query for Versu and Prom Week papers returned only irrelevant results.

## 2. What common techniques do these systems share?

### Takeaway
Across the verified sources, the shared core techniques are:
- condition-tagged lines
- choosing the **most specific** match, with generic fall-backs
- random tie-breaks
- **interruption** when higher-urgency events occur
- group and individual knowledge as inputs
- authoring tools aimed at writers and sound designers

Repetition control, expiring facts, "once per" memory and odds of silence are widely described in background knowledge, but this session did not verify them from primary sources.

### Cited Findings
- **Specificity matching with fall-backs** — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
- **Random choice among equally salient matches** — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)
- **Interruption.** Exchanges break off when a character is attacked — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
- **Knowledge layers.** The Last of Us splits knowledge into individual character knowledge and collective group knowledge, alongside game state and environment — [GDC Vault 2014](https://www.gdcvault.com/play/1020386/A-Context-Aware-Character-Dialog)
- **Numeric qualities gate content**, and authors must do the bookkeeping on those qualities — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)
- **Topic-graph pathfinding** steers conversation toward authored targets (waypoint narrative) — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)
- **Persona-based variation for generated speakers.** Watch Dogs: Legion uses 20 personas, and its Profiler bios add distinctiveness — [Game Developer, 2023](https://gamedeveloper.com/gdc2023/how-i-watch-dogs-legion-i-made-ubisoft-rethink-its-storytelling)
- **Writer and sound-designer ownership** of dialogue logic, rather than programmer ownership — [GDC Vault 2014](https://www.gdcvault.com/play/1020386/A-Context-Aware-Character-Dialog); [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)

### Inferences
- **[unverified this session] A standard technique toolkit,** compiled from background knowledge of Valve, Naughty Dog and Bethesda practice:
  - per-line and per-group cooldowns (`respeakdelay`)
  - "speak once" flags
  - `odds` (probability of speaking at all, which yields silence)
  - facts with expiry written back to a blackboard
  - global "someone just spoke" locks, so only one speaker at a time
  - speaker selection by asking every candidate to score the query and taking the best
  - follow-up concepts chained from a line, to build threads
  - shuffle-bag or sequential selection inside a response group, to avoid immediate repeats
- **[unverified this session] Tracery (Kate Compton, about 2015)** is a context-free grammar for procedural surface variation. It is useful for slot filling, such as names, zones and items, but it produces recognisable repetition if templates are few.
- **Inference for this project.**
  - Salience matching maps cleanly onto a 34,000-line tagged corpus: the tags become criteria, and the bot, group, zone and event state becomes the query.
  - Adding more specific lines over time is cheap and safe, which the 2016 source lists as the key strength.

### Gaps
- No primary-source verification of cooldown, expiry, `odds` or speaker-selection mechanics in any of the named games.
- No source on dedicated debugging tools, such as Valve's response-rule debugging console commands. Not found in this session.

## 3. How do studios evaluate dialogue (naturalness, repetition)?

### Takeaway
Little hard evidence was found. The verified items are:
- Hades used Early Access to shape its narrative.
- Watch Dogs: Legion relied on peer review among writers.
- The academic SLM work uses automated metrics: an LLM judge, keyword recall, LanguageTool.

No studio repetition metrics or telemetry figures were found.

### Cited Findings
- **Hades:** the GDC 2021 talk addressed how Early Access feedback shaped the narrative — [Game Developer, 2021](https://gamedeveloper.com/audio/dive-into-the-dialogue-of-i-hades-i-at-gdc-2021)
- **Watch Dogs: Legion:** writers reviewed each other's work, and leads then did polish passes — [Game Developer, 2023](https://gamedeveloper.com/gdc2023/how-i-watch-dogs-legion-i-made-ubisoft-rethink-its-storytelling)
- **Player complaints about repeated banter** are a visible failure signal in Dragon Age communities — [EA forums](https://forums.ea.com/discussions/dragon-age-franchise-discussion-en/repeating-banter-is-getting-annoying/7364760/replies/7364766); see also a PC Gamer reader Q&A on "which NPC bark are you most sick of hearing" — [PC Gamer](https://www.pcgamer.com/which-npc-bark-are-you-most-sick-of-hearing/)
- **SLM NPC research (2025)** used automated evaluation only, with no human player study:
  - an Openchat-3.6 judge on 100 responses per NPC, for factual consistency
  - keyword recall over 30 multi-turn interactions, for context retention
  - LanguageTool, for fluency
  — [arXiv 2511.10277, 2025](https://arxiv.org/html/2511.10277v1)
- **Emily Short (2016):** salience tuning is "largely trial and error" — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)

### Inferences
- For an offline bot server, cheap self-made metrics are likely the most practical evaluation. Examples:
  - distinct-line ratio per hour of play
  - minimum time between repeats of the same line or template, per channel
  - lines per minute per channel compared against a target band
  - share of chat triggered by events versus idle chatter
  - Repetition-in-time is what players notice, judging by the banter complaints above.

### Gaps
- No published studio telemetry or repetition-metric methodology was found in this session.
- Naughty Dog's and Valve's testing processes were not verified.

## 4. Which approach fits DAoC-style text chat for thousands of bots, with a 34,000-line tagged corpus, offline, on modest PCs?

### Takeaway
A salience-based rule matcher in the Valve/Firewatch style (verified as the L4D2/Firewatch pattern) is the best fit, extended with three additions:
- channel-level speaker arbitration
- expiring facts
- explicit odds of silence (these mechanisms are unverified but standard)

This is an inference. It rests on these verified properties:
- fall-back generality
- incremental authoring
- low breakage risk
- interruptibility
- group/individual knowledge split

A storylet layer suits slower, multi-step threads, such as guild drama or group recruitment arcs. Persona sets, as in Watch Dogs: Legion, solve "thousands of speakers, finite writing."

### Cited Findings
- Salience systems let authors start broad and add specific lines without breaking content — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)
- Valve's system was explicitly designed so writers could produce large volumes of content and cover highly specific situations — [Emily Short, 2012](https://emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/)
- Generated characters scale through a small set of personas (20) rather than per-character writing. The trade-off is that writers can't reference individual traits — [Game Developer, 2023](https://gamedeveloper.com/gdc2023/how-i-watch-dogs-legion-i-made-ubisoft-rethink-its-storytelling)
- Group-level knowledge as well as individual knowledge drives believable multi-NPC dialogue in The Last of Us — [GDC Vault 2014](https://www.gdcvault.com/play/1020386/A-Context-Aware-Character-Dialog)
- Salience-based sequencing can produce poor pacing without arc-aware state — [Emily Short, 2016](https://emshort.blog/2016/04/12/beyond-branching-quality-based-and-salience-based-narrative-structures/)

### Inferences
These are the researcher's synthesis and are not verified claims.
- **Channel = speaking context.** Each channel gets its own blackboard and pacing budget:
  - /say per area
  - /group per group
  - /guild per guild
  - realm channel global
  - tells per pair
- **Speaker arbitration.** One event, such as "group wiped", can make all six bots eligible. Score candidates, let one or two speak, and suppress the rest. Real MMO chat is terse and overlapping, so allow occasional piling-on, such as two people saying "gg" or "rez pls", but cap it.
- **Silence as the default.** The base odds for idle chatter should be low, with a global rate limit per channel. A wiped group in 2003 often produced a burst of short lines and then silence.
- **Expiring facts give thread continuity cheaply.** For example, "asked_for_cleric@group, expires 60s" lets a follow-up such as "omw" match, and "lfg_posted@realm by X, expires 10min" stops spam.
- **"Once per" memory per bot** should cover:
  - jokes
  - stories
  - greetings to a specific player
  - Hades-like acknowledgement of the player's specific circumstances, such as a realm rank-up, a new item or a death count. This is the highest-value, most "alive" category, by analogy with Kasavin's reactivity goal.
- **Personas over individuals.** Map the 34,000-line corpus's tags onto about 10 to 30 personas: chatty, terse, roleplayer, min-maxer, newbie, veteran, and so on. Add per-bot slot fills (names, class, zone) through Tracery-style templates.
- **Cost.** A hash lookup on concept plus a criteria sort is effectively free compared with an LLM. It runs on any PC and is deterministic and debuggable.
- **Debugging.** Log every query as "concept + facts → candidate rules → chosen line/why rejected". This addresses the "trial and error" tuning problem Short describes.

### Gaps
- No source specifically on MMO-style text-chat bots, such as the "player bots" in other private servers, or on how real 2003-era players chatted (line rates, terseness). Corpus-derived statistics from the existing 34,000 lines would answer this better than outside research.
- No sourced performance numbers for rule matching at thousands of concurrent speakers.

## 5. Small local language models in games, 2023–2026: a separate option

### Takeaway
Shipped on-device LLM use is real but narrow and experimental:
- inZOI's "Smart Zoi" (March 2025) uses a ~0.5B Mistral NeMo Minitron SLM on GeForce RTX GPUs only, behind an experimental toggle. It drives behaviour, schedules and "thoughts", not open chat.
- NARAKA: BLADEPOINT Mobile PC (March 2025, China only) has an on-device ACE teammate.

The research prototypes report latency of 0.8 to 35 s per reply on an RTX 2070 Super and weaker coherence from small models. That is incompatible with "thousands of bots on modest PCs" except as a rare, gated flourish.

### Cited Findings
- **inZOI Smart Zoi** (Early Access launched 28 March 2025):
  - Behaviour is "governed by an on-device NVIDIA ACE Small Language Model," described as "a .5B billion parameter Mistral NeMo Minitron" (presumably 0.5B).
  - It is GPU-accelerated "exclusively on GeForce RTX GPUs" and enabled through an "experimental feature" setting.
  - Smart Zois adjust their daily schedules, expose inner thoughts and accept free-text player influence.
  — [NVIDIA, 13 March 2025](https://www.nvidia.com/en-gb/geforce/news/nvidia-ace-naraka-bladepoint-inzoi-launch-this-month/)
- **NARAKA: BLADEPOINT Mobile PC Version** (27 March 2025) has an on-device ACE AI teammate that fights, finds items and suggests skills. It is a China-exclusive release — [NVIDIA, 2025](https://www.nvidia.com/en-gb/geforce/news/nvidia-ace-naraka-bladepoint-inzoi-launch-this-month/)
- **Black Vultures: Prey of Greed** uses ACE SLM reasoning for its companion. It was still "in-development technology" with no date — [NVIDIA, 2025](https://www.nvidia.com/en-gb/geforce/news/nvidia-ace-naraka-bladepoint-inzoi-launch-this-month/)
- **arXiv 2511.10277 (2025), "Fixed-Persona SLMs with Modular Memory"**
  - Setup: an i7-8700K and an RTX 2070 Super (8 GB).
  - Measured latency per model:

    | Model | Latency | Notes |
    |---|---|---|
    | DistilGPT-2 | ~0.8 s | poor grammar |
    | TinyLlama-1.1B | 1.7–1.9 s | |
    | Mistral-7B | 5.49 s | 15.9 GB on disk |
    | Mistral-7B, quantized | 34.58 s | "moderate degradation" in factuality and context retention |

  - Personas are fixed by LoRA, and memory uses ChromaDB vector stores per NPC.
  - Not integrated into a game, and no human study.
  — [arXiv, 2025](https://arxiv.org/html/2511.10277v1)
- **Hobbyist offline examples** exist, such as Godot with Gemma 3n for educational NPCs — [Godot forum](https://forum.godotengine.org/t/offline-ai-powered-npcs-teaching-sustainable-farming-built-with-godot-4-x-and-gemma-3n/118722); [DEV Community](https://dev.to/code-forge-temple/how-i-built-an-offline-ai-powered-npc-system-with-godot-and-gemma-3n-3n8g)

### Inferences
- **Scale mismatch.** At roughly 1 to 5 s per reply on a mid-range GPU, an SLM can serve perhaps a handful of utterances per second. That is enough for the occasional /tell reply to the real player, but not for thousands of bots generating ambient chat. Pre-generating lines offline into the corpus, then selecting them by rules at runtime, avoids the runtime cost entirely.
- **Era drift and safety [unverified this session].** Background reports say modern models produce anachronistic MMO slang (post-2003 memes, references to "content creators" or "Discord") and occasional unsafe or off-tone text. Mitigations would be:
  - constrained output: choose from N corpus candidates, or fill slots only
  - era lexicon filters
  - a rule-based fallback
- **The safest hybrid:** the rule system decides *whether* and *who* speaks and *what concept* is being expressed. An optional local model, gated to the real player's direct tells and off by default, only paraphrases or picks among corpus lines.
- **[unverified this session] Other 2023–2026 LLM games:** Inworld-based demos, Ubisoft's "NEO NPC" prototypes, *Suck Up!* (cloud), *Vaudeville* (2023) and others. Most are cloud-based or demos, and their reception noted incoherence and breaking character. Not verified here.

### Gaps
- No verified shipped *offline text-chat* LLM game was found. inZOI and NARAKA are behaviour/companion uses on RTX-only hardware.
- No VRAM requirement for Smart Zoi was stated.
- No sourced reports of era drift or safety incidents in shipped games were gathered this session.
- Cloud-based examples (Inworld titles, Suck Up!, Ubisoft NEO NPC) and their reported problems were not researched.
