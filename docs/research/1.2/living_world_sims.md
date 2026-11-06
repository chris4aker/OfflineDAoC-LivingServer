# Living-world simulations and "simulated MMO" projects: what makes AI characters feel like a populated server

Scope note: this covers mechanisms only. Pages fetched and read in this session are cited as
plain [Source](URL). Items marked **[background, not fetched this session]** come from the
researcher's prior knowledge and must be checked before the report states them as fact. Search-result
links that were not opened are marked **[search hit only]**.

## 1. Single-player "fake MMO" games and mods: how do they fake guilds, chat, grouping, economy, rivalries, and what do communities praise and complain about?

### Takeaway
The best-received fake MMO, Erenshor (2025), uses no LLM. It relies on state machines, decision trees,
a text parser and pre-written lines keyed to personality, plus three things that make SimPlayers feel
like people: **they remember you** (wipes, good groups), **they progress while you're offline**, and
**they reach out first** (greetings by name, whisper invites to groups and raids). WoW playerbot projects
offer population scale (thousands of bots) and full content coverage, but their stock chat is thin, and
LLM chat add-ons need several brakes to stop bot-to-bot loops and nonsense. In OSRS, players spot bots by
their uniform, scripted reactions in chat.

### Cited Findings
**Erenshor (Burgee Media, solo developer "Brian"; Steam Early Access 14 April 2025)**
- The developer says: "SimPlayers do not use LLM or any other emerging AI model. They are run by a mixture of state machines and decision trees." — [PC Gamer, 13 Mar 2025](https://www.pcgamer.com/games/rpg/this-runescape-looking-simulated-mmorpg-has-all-the-nostalgia-without-the-drama-because-all-the-other-players-are-npcs)
- In the developer's Reddit post, SimPlayers run on "decision trees, text parsers, and pre-written responses based on their personalities." Their progression and memory persist, and they "remember if you wiped or if you had a great group." They greet you by name after a while and eventually "begin to send you whispers to invite you to groups or raids when you get online." — [r/MMORPG, Erenshor developer post, 21 Oct 2024](https://redlib.hyperreal.coffee/r/MMORPG/comments/1g8u0q1/erenshor_a_simulated_mmorpg)
- **Offline progression:** players may "log on a day or two after your last session to find your friends having gotten some new toys." SimPlayers are **tethered to the player's level**: "They won't out level you!" — [same Reddit post, 2024](https://redlib.hyperreal.coffee/r/MMORPG/comments/1g8u0q1/erenshor_a_simulated_mmorpg)
- **Economy and chat flavour:** SimPlayers shout trade lines such as "WTB Godless Amarion, offering 710 gold," ask for loot when it drops, buy and sell items, join and leave guilds, invite you to raids "once they get to know you," and hold "opinions about you and about other players." — [PC Gamer, 2025](https://www.pcgamer.com/games/rpg/this-runescape-looking-simulated-mmorpg-has-all-the-nostalgia-without-the-drama-because-all-the-other-players-are-npcs)
- **Player praise:** one player was delighted when a SimPlayer answered an early "LFG" with "i dont carry trash." Others wrote "each NPC you interact with has a unique personality and playstyle" and "scratch[es] that grinding itch without the preasure." — [Reddit thread, 2024](https://redlib.hyperreal.coffee/r/MMORPG/comments/1g8u0q1/erenshor_a_simulated_mmorpg)
- **Steam review themes** (aggregator, 941 reviews; weights are relative mention frequency, not percentages):
  - "SimPlayers make world alive" (0.34)
  - distinct personalities and skill levels (0.37)
  - NPC guilds with guild chat, rankings and gear interactions (0.22)
  - whispers and trash talk (0.03)
  - the main SimPlayer complaint: poor equipment choices and asking for gear **downgrades** (0.05)

  — [Vaporlens review summary, accessed Nov 2026](https://vaporlens.app/app/2382520/erenshor.md). Secondary press: [Gamepressure](https://www.gamepressure.com/newsroom/simulated-open-world-in-classic-mmorpgs-style-but-for-single-play/z27d1f) **[search hit only]** reports "very positive" Steam reviews.
- **Criticisms in the Reddit thread were mostly not about AI:** the camera, art style and asset packs, clunky UI, limited weapons and classes, line-of-sight bugs, and requests for drop-in co-op. One commenter argued that "taking away the other players and it is no longer an MMORPG." — [Reddit, 2024](https://redlib.hyperreal.coffee/r/MMORPG/comments/1g8u0q1/erenshor_a_simulated_mmorpg)

**WoW: AzerothCore mod-playerbots and its LLM add-on**
- The mod-playerbots README says random bots roam the world, complete quests and act like players "simulating the MMO experience." Bots can run most raids and battlegrounds, behaviour is highly configurable, and the project claims good performance "even with thousands of bots." The README doesn't detail guild, auction house or chat systems; those are on the wiki. — [mod-playerbots README (fork mirror), GitHub, accessed 2026](https://github.com/Merkerhood/mod-playerbots)
- **mod-ollama-chat (LLM chat for playerbots):**
  - Bots send prompts to a local Ollama endpoint (example model `llama3.2:1b`). Each prompt includes context: class, race, zone, group, guild and a lore cheat sheet.
  - Bots react to events (quests, loot, deaths, level-ups) and make random remarks about their surroundings.
  - Personality types (Gamer, Roleplayer, Trickster) are reloadable SQL packs.
  - Memory: a short sliding chat window plus optional long-term "scored notes" and model-written **relationship notes** about frequently mentioned players.
  - Four brakes stop runaway chatter:
    - (a) chain depth with decaying reply chance for bot-to-bot replies
    - (b) an "audience rule": bots reply to bots only if a real player spoke recently in that channel
    - (c) cooldowns per bot, per channel and server-wide
    - (d) repetition scoring
  - Stated problems: replies "may be irrelevant or nonsensical," bot chatter "can loop," and the module "can bog down your server."

  — [mod-ollama-chat README, GitHub, accessed 2026](https://github.com/DustinHendrickson/mod-ollama-chat)
- A sibling project, mod-ollama-bot-buddy (an LLM driving bot behaviour), exists — [GitHub](https://github.com/DustinHendrickson/mod-ollama-bot-buddy) **[search hit only]**.

**Old School RuneScape bots as seen by players**
- A player typed "How Many" in chat and hundreds of bots answered with "huge strings of numbers," revealing themselves. — [OpenCritic news, 3 Sep 2025](https://opencritic.com/news/20960/runescape-player-discovers-hilariously-effective-way-to-uncover-bots)
- A community bot-detection project exists — [OSRS Bot-Detector, GitHub](https://github.com/Bot-detector) **[search hit only]**.

### Inferences
- In Erenshor, praise is about **social reciprocity** (being greeted, invited, remembered, mocked), not combat AI. Living Server's "boring" complaint likely needs bots that start social contact toward the player and refer back to shared history, more than smarter pathing.
- Erenshor's level tether is a design concession: it keeps friends relevant. With thousands of bots, Living Server can't tether everyone, but it could tether a **small "social circle"** (bots the player has grouped with).
- The OSRS story is a negative lesson. If many bots answer the same trigger the same way at the same moment, the illusion breaks at once. Response probability, delay and wording need jitter per bot, and only a few bots should react to any one broadcast.
- The mod-ollama-chat brakes, especially the audience rule, apply to any chat system, LLM or template. Bot-to-bot chatter should exist mainly where the player can see it.
- The "gear downgrade" complaint shows that visible mistakes in item evaluation read as "dumb." That matters for Living Server's real loot and upgrade rules.

### Gaps
- **.hack (The World), SAO games, EQEmu bots, "MMORPG simulator" projects, SPP Classics, TrinityCore NPCBots:** not researched in this session (tool budget). No sourced claims.
- mod-playerbots wiki and issue tracker detail on guilds, auction house and chat was not read. Community complaints (Reddit or Discord) about playerbots feeling dumb were not found.
- Erenshor dev logs or Steam announcements on how relationship memory is stored were not found.
- No quantitative data on Erenshor review complaints about repetition. The aggregator listed none, which isn't proof that there are none.

## 2. Emergent social simulation: which mechanisms create memorable stories and relationships with the player?

### Takeaway
Memorable stories come from a few elements combined:
- **persistent named individuals** who carry history
- **personal stakes** (revenge, betrayal, grudges)
- simulated needs and personality that make bad choices **legible** ("I know what's going on")
- a **queryable memory of past social events** that dialogue can name

Off-screen simulation (A-Life, UO ecology) was impressive but fragile and costly to run. In both, parts were cut or broke.

### Cited Findings
- **Nemesis system (Shadow of War, 2017):**
  - Monolith's Bob Roberts said the system depends on revenge and antagonism. If players dominate everyone, rivalries don't form.
  - Betrayal by an ally who becomes your nemesis is "a knob we can turn" for tension.
  - The team chose to "embrace letting the chaos happen." An unscripted event (a general killing a warchief) was later written up as dialogue about a paranoid orc.
  - Story missions release authored orcs into the systemic pool.

  — [Game Developer, Bryant Francis, 2017](https://gamedeveloper.com/design/upgrading-the-nemesis-system-for-i-middle-earth-shadow-of-war-i-)
- A GDC 2018 talk by Chris Hoge (Monolith) covered "helping players hate or love their nemesis." The fetched coverage gave only context, no mechanism detail. — [80.lv, 2018](https://80.lv/articles/gdc-helping-players-hate-or-love-their-nemesis); [GDC news](https://gdconf.com/news/attend-gdc-2018-behind-scenes-look-shadow-wars-nemesis-system) **[search hit only]**
- **Patent:** Warner Bros. patented the Nemesis system, and commentators call it "a brilliant idea trapped by patents." — [Acer blog](https://blog.acer.com/en/discussion/3936/the-nemesis-system-a-brilliant-idea-trapped-by-patents) **[search hit only]**. **[background, not fetched]:** US patent granted ~Feb 2021. Scope and claims not verified.
- **S.T.A.L.K.E.R. A-Life (2007):**
  - AI within ~150 m of the player runs "online." Beyond that it runs "offline" on an abstract graph, and offline results are applied over game time.
  - NPCs act on their own, branching on type and role. A healed bandit, for example, may treat the player differently.
  - "Smart Terrains" impose rules on AI that enter them. A ranking table in the PDA ranks the player and NPCs by points.
  - Many features (factions, fireplace sitting, defending camps) were cut.

  — [Game Developer, Alex Cicala, 16 Aug 2017](https://gamedeveloper.com/design/a-life-an-insight-into-ambitious-ai)
- **S.T.A.L.K.E.R. 2 (Nov 2024):** GSC acknowledged "several known issues with A-life 2.0" and promised fixes. Players were confused about whether A-Life existed at all, and a Steam-page reference to it was removed. — [PC Gamer, 22 Nov 2024](https://pcgamer.com/games/fps/stalker-2-devs-reassure-players-yes-a-life-2-0-is-in-the-game-no-its-not-working-right-and-yes-fixes-are-on-the-way). Other coverage says the developers attributed the breakage to aggressive optimisation: [VideoGamer](https://www.videogamer.com/?p=1125404), [Dexerto](https://www.dexerto.com/gaming/stalker-2-devs-nerfed-a-life-2-0-major-flaw-2997539) **[search hits only; headlines]**.
- **Ultima Online ecology (1997):**
  - Raph Koster: the "dynamic ecology" ran in alpha and was disabled in beta, mainly for **performance**. Radial searches plus pathfinding cost too much.
  - Separately, the fixed-pool resource economy collapsed: players hoarded wool and shirts, the central bank emptied, and sheep could no longer spawn.
  - This gave rise to the "faucet-drain economy" lesson, credited to Zach Simpson's analysis.

  — [Raph Koster, "Did players destroy the UO ecology?", updated 28 Aug 2024](https://www.raphkoster.com/?p=46439); [UO's resource system, Koster](https://www.raphkoster.com/?p=517) **[search hit only]**
- **Dwarf Fortress:**
  - Tarn Adams (GDC 2025) says dwarves act on simulated needs and desires and can "embody bigger emotions … make bigger mistakes."
  - Players accept self-destructive acts (fishing past an invading army) because the behaviour fits the dwarf archetype: "I know what's going on."

  — [PC Gamer, 31 Mar 2025](https://www.pcgamer.com/games/sim/dwarf-fortress-dwarves-are-more-human-than-human-creator-says-theyre-allowed-to-embody-bigger-emotions-theyre-allowed-to-make-bigger-mistakes-theyre-allowed-to-do-anything)
- **Comme il Faut / Prom Week (academic, 2014):**
  - **State:** permanent traits, temporary and directional statuses ("angry at"), relationships (friends, dating, enemies), and scalar networks (buddy, romance, cool).
  - **Volition:** characters score every possible "social exchange" with each other character by summing weighted influence rules. Responders accept or reject using their own rules, and volition is recalculated after each exchange.
  - **Memory:** the **Social Facts Database** logs labelled past events, including authored backstory with negative timestamps. Rules can query it, and dialogue can name past events.
  - **Trigger rules** set derived statuses, such as "two-timer."
  - **Cost:** Prom Week needed **over 5,000 influence rules**, and contradictory rules appeared.

  — [McCoy et al., IEEE TCIAIG 6(2), 2014](https://cs.uky.edu/~sgware/reading/papers/mccoy2014cif.pdf)
- **Crusader Kings, Mount & Blade lords, Kenshi, The Sims:** not researched this session (see Gaps).

### Inferences
- **Nemesis analogue for Living Server:** gamebots that kill the player, or that the player kills in PvP or in contested camps, can become named rivals who remember the encounter, gain rank or level from it, and reappear and taunt with a reference to it. This fits a DAoC RvR world, but check patent risk before copying the hierarchy, promotion and memory mechanics closely.
- **A-Life's online/offline split** is the obvious model for thousands of bots. Simulate bots near the player at full fidelity, and "resolve" distant bots on an abstract zone graph (level-ups, deaths, loot, guild changes). Both A-Life and UO show that the cheap layer must stay coherent with what the player later sees.
- UO's collapse is directly relevant to Living Server's real loot, coins and Realm Exchange. Bot hoarding with no sinks can drain or bloat the economy, so bot inventories and gold need explicit faucets and drains.
- **CiF's Social Facts Database is a cheap, LLM-free memory pattern:** an event log keyed by (actor, target, label, time) that both choice rules and chat templates query. It scales better than branching dialogue, but rule count grows quickly.
- **The Dwarf Fortress lesson:** bot mistakes read as character, not as "dumb," when they follow a visible personality (greedy, cowardly, reckless). Mistakes with no personality behind them, like Erenshor's gear downgrades, read as bugs.

### Gaps
- Specific Nemesis mechanics (scars, promotion, strengths and weaknesses, Shadow of War's "blood brothers" and friendships) were not confirmed from a fetched primary source.
- Crusader Kings character AI (traits, stress, opinions), Mount & Blade lord relations, Kenshi's world states and squads, and The Sims' needs and relationship model: no sources gathered.
- Tarn Adams material on legends and historical-figure relationships was not fetched. Candidates: [Game Developer, "simulate the universe"](https://gamedeveloper.com/design/-i-dwarf-fortress-i-figuring-out-how-to-simulate-the-universe-one-step-at-a-time) **[search hit only]**.

## 3. Pacing and drama directors: RimWorld storytellers, Valheim events, Darkest Dungeon, Alien: Isolation

### Takeaway
RimWorld is the documented model. Before each event, a storyteller checks colony state (wealth,
population, recent deaths or injuries, time since the last major event). Threat is scaled by an
**adaptation** score that rises over time and drops when the player suffers. Each storyteller has its
own pacing: a rising tension curve with breathing room (Cassandra), long gaps with help after a raid
(Phoebe), or no pacing (Randy). Population "intent" curves throttle arrivals as the colony grows.

### Cited Findings
- **Storyteller personalities:** Cassandra follows "a classic increasing curve of challenge and tension" with breathing room. Phoebe gives "lots of time between disasters" and may send traders or helpers after a tough raid. Randy has no pacing rules. — [RimWorld Wiki, AI Storytellers, accessed 2026](https://rimworldwiki.com/wiki/Storyteller)
- **Factors checked before each event:** colony wealth, building wealth, colonist and animal counts, whether a colonist recently died or was badly wounded, and time since the last major event. — [RimWorld Wiki](https://rimworldwiki.com/wiki/Storyteller)
- **Threat scale by difficulty:** 10% / 30% / 60% / 100% / 155% / 220%. Adaptation growth and impact vary by difficulty; for example, "Losing is fun" is 100% / 40%. — [RimWorld Wiki](https://rimworldwiki.com/wiki/Storyteller)
- **Wealth-independent mode** swaps wealth scaling for a time curve, with "years until max threat" from 1 to 20 (default 12). — [RimWorld Wiki](https://rimworldwiki.com/wiki/Storyteller)
- **Population intent curve:** 0 colonists → 8.0, 5 → 1.0, 12 → 0.0, 20 → −1.0. It's multiplied by a days-at-location curve and floored by a minimum factor (0.05, or 0.20 for Randy). — [RimWorld Wiki](https://rimworldwiki.com/wiki/Storyteller)
- **Left 4 Dead AI Director, Valheim events, Darkest Dungeon, Alien: Isolation:** not researched this session. **[background, not fetched]:** Mike Booth's 2009 talk "The AI Systems of Left 4 Dead" describes tracking each survivor's "emotional intensity" and cycling build-up, peak and relax phases.

### Inferences
- **A "server storyteller" layer for Living Server** could schedule bot-driven "server events" from player state and recent history, with breathing room after each one. Events could include a realm raid on a frontier keep the player is near, a guild recruiting drive, a rival's revenge hunt, a world-boss call in /region, or an "LFG for dungeon X" wave matched to the player's level.
- **Population intent → social intent:** throttle how often new bots befriend the player, based on the size of the player's current circle. Many offers early, few once the circle is "full." This avoids both loneliness and spam.
- **Adaptation scoring** (falls after the player dies or wipes) is a ready-made way to make rival or ganker pressure feel fair.

### Gaps
- No sourced material on the L4D director, Alien: Isolation's two-layer (director plus alien) menace gauge, Valheim's raid-event triggers, or Darkest Dungeon's stress and affliction system.
- Exact RimWorld storyteller timing parameters (on/off cycles, minimum days between big threats) were not on the wiki page fetched.

## 4. Persistence and memory: how do AI characters remember the player and each other over days, and avoid repetition?

### Takeaway
Three working patterns:
1. **Structured, symbolic event memory** queried by rules and templates: CiF's Social Facts Database, Erenshor's "remembers if you wiped."
2. **Offline progression applied on return:** Erenshor's friends with new gear, A-Life's offline graph.
3. **For LLM approaches,** a short sliding window plus compressed, scored long-term notes and relationship summaries (mod-ollama-chat).

Repetition is handled with repetition scoring, cooldowns and audience gating. No source gave hard data on how well any of these work over weeks of play.

### Cited Findings
- Erenshor: progression and memory persist, SimPlayers remember wipes and great groups, and they gain gear between your sessions. — [Reddit dev post, 2024](https://redlib.hyperreal.coffee/r/MMORPG/comments/1g8u0q1/erenshor_a_simulated_mmorpg). "They'll remember you and how you treat them" (Steam page, quoted by) — [PC Gamer, 2025](https://www.pcgamer.com/games/rpg/this-runescape-looking-simulated-mmorpg-has-all-the-nostalgia-without-the-drama-because-all-the-other-players-are-npcs)
- CiF: the Social Facts Database stores labelled events with timestamps, including authored backstory before play starts. Rules and dialogue templates reference them by name. — [McCoy et al., 2014](https://cs.uky.edu/~sgware/reading/papers/mccoy2014cif.pdf)
- A-Life tracks events and applies results to offline AI as game time passes. — [Game Developer, 2017](https://gamedeveloper.com/design/a-life-an-insight-into-ambitious-ai)
- mod-ollama-chat: a sliding history window, long-term memory condensed into scored notes, LLM-written relationship notes, repetition scoring of lines and opening phrases, and per-bot, per-channel and global cooldowns. — [GitHub README, accessed 2026](https://github.com/DustinHendrickson/mod-ollama-chat)
- **[background, not fetched]:** Park et al., "Generative Agents" (2023), [arXiv:2304.03442](https://arxiv.org/abs/2304.03442). A memory stream scored by recency, importance and relevance, plus reflection and planning, for 25 agents. It needed many LLM calls per agent and was not real-time at scale.

### Inferences
- **For thousands of offline bots,** the cheap, durable choice is a compact relationship table: (bot, player or other bot) → affinity, last-interaction time, notable-event tags such as "wiped together," "ganked me," "sold me X," "same guild since day N." Chat templates would draw on those tags. This is CiF-lite and needs no LLM.
- **To avoid repetition:** draw lines from large template pools with per-bot cooldown memory and per-personality vocabularies, gate bot-to-bot talk on player presence, and limit how many bots react to any single trigger (the OSRS failure).
- **"Welcome back" moments** create the sense of persistence cheaply: friends levelled while you were away, a guildmate whispers when you log in, a rival has gained rank.

### Gaps
- No source measured memory decay design or player perception of repetition over long play.
- How Erenshor stores or ages relationship memory is unknown.

## 5. Which of these would answer complaints that Living Server's bots feel "boring" and "dumb" compared with real players?

### Takeaway
Across sources, "alive" comes from social reciprocity and shared history more than tactical skill:
being noticed, remembered, invited, needled and rivalled. "Dumb" comes from visibly bad choices with
no personality behind them, and from uniform mass reactions. LLM chat can add variety but brings
nonsense, loops and server load, so it should be an optional layer, not the base.

### Cited Findings
- Erenshor's praised features are personalities, grouping, guild chat and rankings, whispers and trash talk. Its chief AI complaint is bad gear choices. — [Vaporlens, 2026](https://vaporlens.app/app/2382520/erenshor.md)
- Erenshor achieves this with no LLM: state machines, decision trees, text parsers and pre-written personality responses. — [PC Gamer, 2025](https://www.pcgamer.com/games/rpg/this-runescape-looking-simulated-mmorpg-has-all-the-nostalgia-without-the-drama-because-all-the-other-players-are-npcs); [Reddit dev post, 2024](https://redlib.hyperreal.coffee/r/MMORPG/comments/1g8u0q1/erenshor_a_simulated_mmorpg)
- Nemesis-style rivalry needs the player to lose sometimes ("if players dominate everyone, rivalries don't develop"), and betrayal is a tunable source of tension. — [Game Developer, 2017](https://gamedeveloper.com/design/upgrading-the-nemesis-system-for-i-middle-earth-shadow-of-war-i-)
- Mass identical reactions expose bots. — [OpenCritic, 2025](https://opencritic.com/news/20960/runescape-player-discovers-hilariously-effective-way-to-uncover-bots)
- LLM chat problems: irrelevant or nonsensical replies, loops, server load. — [mod-ollama-chat README](https://github.com/DustinHendrickson/mod-ollama-chat)
- Ambitious off-screen simulation is fragile and costly to run: UO's ecology was cut for performance, and STALKER 2's A-Life shipped broken. — [Koster, 2024](https://www.raphkoster.com/?p=46439); [PC Gamer, 2024](https://pcgamer.com/games/fps/stalker-2-devs-reassure-players-yes-a-life-2-0-is-in-the-game-no-its-not-working-right-and-yes-fixes-are-on-the-way)

### Inferences (speculative; ranked by likely impact for Living Server)
1. **A social circle with memory** (Erenshor plus CiF-lite): track a few dozen bots per player with affinity and event tags. They greet by name, whisper group or raid invites at login, remember wipes and successes, and join or leave the player's guild.
2. **A rival system** (Nemesis-like, RvR flavoured): enemy-realm gamebots that kill or are killed by the player become named rivals, persist, level up, hunt the player and taunt with references to past fights. Check patent scope first.
3. **Server storyteller** (RimWorld-like): paced, player-relevant world events (keep attacks, LFG waves, boss calls, guild drama) with cooldowns and adaptation after player deaths.
4. **Two-tier simulation** (A-Life-like): full AI near the player, abstract resolution elsewhere, and visible "while you were away" results such as levels, gear and keep changes.
5. **Personality-legible mistakes:** tie suboptimal bot choices to declared traits (greedy, reckless, cautious) and remove bare errors like gear downgrades.
6. **Anti-uniformity chat:** large template pools, per-bot cooldowns, a cap on reactors per trigger, an audience rule for bot-to-bot talk, and a /who and /region presence that varies by time of day.
7. **Economy faucets and drains** (UO lesson) so bot trading and the Realm Exchange don't collapse or inflate over months.
8. **Optional LLM layer** (separate category): local models via something like mod-ollama-chat. Use it only with strict brakes, keep it off by default for performance on players' PCs, and expect irrelevant replies.

### Gaps
- No direct evidence on which mechanism most improves players' perception in a *large* bot population (as opposed to Erenshor's small circle). Real player-to-bot ratios and Erenshor's SimPlayer counts were not found.
- Community feedback specific to Living Server or OfflineDAoC was not part of this research.
