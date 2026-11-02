# Bias audit: blind spots in the Living Server's research, assumptions and evaluation (red team before 1.2)

Scope note: written 2026-11-01. Project evidence comes from the repo (read-only, `git show`): release/1.2
`docs/bot-voices/PERIOD-CHAT-RESEARCH.md`, `RESEARCH-BRIEF.md`, `WRITING-GUIDE.md`, `DESIGN.md`,
`docs/research/BOT-NAMES-2003.md`, `docs/research/LIVE-EVENTS-RESEARCH.md`, `docs/LIVE-EVENTS-ENGINE.md`,
`docs/CRAFTING-AND-ECONOMY.md`, `docs/BOT-PERSONALITIES.md`, `docs/LIVING-SERVER.md`, `docs/RELEASE-TRAIN.md`;
`plan/rvr-builds:docs/BOT-BUILDS-AND-RVR.md`; `feature/era-builds:docs/research/ERA-BUILDS-1.65.md`;
`eval/chat-blind-2026-11-01:docs/bot-voices/CHAT-EVAL.md`. It also covers the interim synthesis
(`reports/Living Server research before 1.2.md`) and the four finished strand notes in this folder.
External research: WebFetch hit the account's weekly limit partway through. Three sources were fetched and read:
Panickssery et al. 2024, Jakesch et al. 2023, and the Gizmodo BotPrize 2012 piece. Every other external source was
found by search, but its page was **not fetched**. Where one of those is cited, the claim is the paper's widely
reported headline, and it is marked **[search hit; content not fetched]**. The report writer should treat those as
leads to confirm. Repo citations are given as `branch:path`. Severity is for the project's stated goal ("the player
feels the world is alive and that bots respond to them", with 2003 authenticity as the tie-breaker; WRITING-GUIDE.md).

## 1. Source biases: what the project's picture of "a 2003 DAoC server" rests on

### Takeaway
The project's quantitative picture of 2003 players comes almost entirely from the European GOA servers. FreddysHouse
is the only large dated corpus the project could reach, and it is a corpus of forum posters, not players. US boards,
census sites and the Wayback Machine were never reached, so what the project knows is shaped by what was accessible.
The docs are honest about this in footnotes. But the numbers go straight into data files (build weights, class
shares, name mix) with no correction, and later docs cite them as "era" facts. Severity: **high** for builds, class
shares and names; **medium** for chat style.

### Cited Findings
- **The European skew is acknowledged, then built on.**
  - ERA-BUILDS: "Main source: FreddysHouse forums ... European (GOA) servers"; 2,666 builds from 18,766 posts. "**No
    US 2003 spec source is cited.**" "GOA ran the US patches a few weeks to months late", so "a European spring-2003
    post often describes the US's late 2002." — `feature/era-builds:docs/research/ERA-BUILDS-1.65.md` §0
  - BOT-NAMES: "the 2001-2004 sample is about 70% European servers, and US coverage from 2002-2003 is thin." —
    `release/1.2:docs/research/BOT-NAMES-2003.md`
  - The chat strand's realm stereotypes ("Hibernia: the greenpeace of DAoC", "Midgard elitist", "avoid Albion on
    Excalibur") come from **one** FreddysHouse thread about the Excalibur and Prydwen servers, Sep 2002. —
    `chat_realm_speak.md` §2
- **Patch lag makes the dating wrong.** The era cut is US 1.65 (8 Oct 2003). A European post dated before 8 Oct 2003
  describes an older patch state than a US post of the same date. So the European corpus is internally *in era* but
  shifted against the US timeline the project uses. — ERA-BUILDS §0 (above)
- **Inaccessible archives set the boundary of what was found.**
  - "every web.archive.org page (Vault and VN boards) ... Period in-game chat logs were the thinnest evidence." —
    `release/1.2:docs/bot-voices/PERIOD-CHAT-RESEARCH.md`, "Pages that could not be opened"
  - The census sites (Camelot Seer, duskwave) exist "only archived on the Wayback Machine". The US June 2003 census
    "survives only as fragments" pasted on FreddysHouse. — ERA-BUILDS §0, §2
  - RESEARCH-BRIEF: "Not reachable ... the archived IGN/Vault boards". — `release/1.2:docs/bot-voices/RESEARCH-BRIEF.md` §2
- **Forum posters are not players.**
  - Participation inequality: in online communities roughly 90% lurk, 9% contribute a little and 1% produce most
    content. — [Nielsen, "Participation Inequality", 2006, via P2P Foundation wiki](https://wiki.p2pfoundation.net/Participation_Inequality) [search hit; content not fetched]
  - The era-builds method counts posts in threads titled spec, template, build, respec or group setup. Those threads
    are written by people asking for spec help and by theorycrafters answering them. ERA-BUILDS itself says this is
    "a proxy for how often they were played, not a count." — ERA-BUILDS §0
- **Min-maxer framing in the group compositions.** Every "typical 8-man" in ERA-BUILDS §3 comes from threads such as
  "the perfect albion rvr group", "ideal mid rvr group" and "what's your perfect rvr group". These describe
  aspirational fixed-group setups. One poster in the same corpus asks "how many balanced alb FGs do you ever see",
  which implies they were rare. — ERA-BUILDS §3 (thread `how-many-balanced-ald-fgs-do-you-ever-see`, 2003-04-18)
- **The build weights are not derived from the counts.** The weights are labelled estimates, but they invert the post
  counts in several places:
  - Scout: Longbow/Slash has 52 dated posts and weight 0.15; Longbow archer has 38 posts and weight 0.75.
  - Infiltrator: the "5-spec" has 71 posts and weight 0.35; the CS assassin has 59 posts and weight 0.40.
  - Cleric: Smite has **0** dated posts and weight 0.20.
  - — ERA-BUILDS §1, Albion tables. §4 admits that "how they split between the two builds is a guess".
- **Nostalgia and later memory.**
  - The project leans on retrospectives and memories, labelled [R]: GameBanshee/Eurogamer 2020, Engadget 2010 reader
    memories, Massively OP 2016, DOL emulator forum "what players miss" threads, KillTenRats 2011. —
    PERIOD-CHAT-RESEARCH §A, RESEARCH-BRIEF §2
  - Rosy retrospection: people rate past experiences (vacations) more positively in hindsight than they did at the
    time. — [Mitchell, Thompson, Peterson & Cronk 1997, J. Exp. Social Psychology (Kellogg page)](https://www.kellogg.northwestern.edu/academics-research/research/detail/1997/temporal-adjustments-in-the-evaluation-of-events-the-rosy) [search hit; content not fetched]
  - Video game nostalgia as a driver of returning to old games. — [Wulf, Bowman, Rieger, Velez & Breuer 2018, "Video Games as Time Machines", Media and Communication 6(2)](https://ideas.repec.org/a/cog/meanco/v6y2018i2p60-68.html) [search hit; content not fetched]
  - WoW Classic is the industry's live test of "players want the old game". Blizzard's 2013 "you think you do, but
    you don't" stance, and a VP later speaking of "rose-colored glasses". — [Wccftech, Blizzard VP on WoW Classic and nostalgia](https://wccftech.com/wow-classic-graphics-nostalgia/amp/); [Mein-MMO on the Brack quote](https://mein-mmo.de/en/the-new-blizzard-ceo-was-the-hate-figure-of-wow-classic-fans,275984/) [search hits; content not fetched]
  - A scholarly study of WoW Classic nostalgia, sense of place and social presence exists. — [Texas Tech, "Returning to Azeroth"](https://scholars.ttu.edu/en/publications/returning-to-azeroth-nostalgia-sense-of-place-and-social-presence); [Mediekultur, "Going home again?"](https://tidsskrift.dk/mediekultur/article/view/106494) [search hits; content not fetched]
- **English only.** The chat strand: "Not researched: non-English EU servers (GOA's German and French servers ...).
  Code-switching evidence is a gap." The bot-voice research cites one German academic paper (the Konstanz 2002/2004
  log study) as an exception. — `chat_realm_speak.md` §2 Gaps; PERIOD-CHAT-RESEARCH §A3
- **A few famous talks are the canon.**
  - Ruskin (Valve, GDC 2012) is the dialogue model.
  - The L4D Director and RimWorld are the pacing models.
  - The WoW AQ war effort is the event model.
  - The live-events research admits: "no quantitative retention or participation data for any event in any game, so
    every hit-or-flop judgement rests on press retrospectives, developer statements and player commentary." —
    `release/1.2:docs/research/LIVE-EVENTS-RESEARCH.md`, "What this report covers"
- **Small, non-MMO academic samples are used as targets.**
  - "3 social lines to 1 task line" comes from Peña & Hancock. Their sample was *Jedi Knight II*, an FPS, on a clan
    server: 65 participants, six 1-hour sessions.
  - The SD (0.18) is large and the output per person very uneven. — `chat_realm_speak.md` §1
  - The BZFlag message length (1.4 words) is used beside IRC and MUD figures to set the bots' line lengths. —
    PERIOD-CHAT-RESEARCH §A3
  - The Project Massive figures (442 DAoC players) are self-report survey shares. — PERIOD-CHAT-RESEARCH §B4 (source as
    listed there: hci.rwth-aachen.de CHI2004 PDF; recruitment method not checked here)

### Inferences
- **(High) The European proxy compounds three errors.**
  - **Population:** GOA servers were smaller and had a different realm split ("around 40% alb ... around 25% hib" on
    Excalibur; ERA-BUILDS §2).
  - **Timing:** the patch lag.
  - **Culture:** British and European humour, the "lewt dewds" register, possibly different zerg and FG norms.
  - The chat style and stereotypes are probably close enough. Class shares and realm balance are the most exposed,
    because they go straight into `bot-builds.json` weights.
- **(High) The min-maxer filter makes the bot population too competent and too organised.**
  - The posted builds and "perfect 8s" describe what a vocal, optimising minority aimed for.
  - Off-meta builds, half-respecced characters, mis-specced levellers, pick-up groups and zergs were probably much more
    common than the corpus shows.
  - BOT-BUILDS-AND-RVR plans to fill groups as "a 2003-style eight" by role template. That writes the forum ideal into
    every group.
- **(Medium) Survivorship of evidence.** What survives online is what someone kept: famous threads, events that
  became legends, memorable flops. Ordinary evenings, which are what a "living server" mostly has to simulate, are
  under-recorded. The live-events research and the strand notes study hits and spectacular flops, not the median
  Tuesday night.
- **(Medium) Nostalgia enters through the owner and the r/daoc audience as well as the sources.** People who loved DAoC
  remember the social camps and the Emain fights. They forget the 40-minute LFG waits, the downtime, the empty zones
  and the rudeness.
- **(Low–medium) The English-only scope** hides GOA's German and French communities. That only matters if the project
  ever claims to model "a European server". It does matter that the European corpus is English-language *British*
  posting.

### Gaps
- No US 2002–2003 spec or class census source was reached. Whether the Wayback Machine is reachable now (Vault Network
  class boards, Camelot Seer, duskwave) is open; that is the other unfinished strand.
- How Project Massive recruited its respondents (forum links, probably self-selected) was not checked.
- The nostalgia and WoW Classic studies were found by title only, so their findings are not verified here.

## 2. Method biases: how the project evaluates its own bots

### Takeaway
The project's own data shows the core method bias. Intent tests written by the chat's own authors scored 95.6% and
95.9%. A blind corpus scored 53.0%. The new blind evaluation is a real improvement, but it is still AI judging AI
against an AI-written "human" baseline, and it shares the same stylistic priors. Separately, in-game evidence is thin
and narrow: about five hours a week, one PC, small rosters, mostly one Albion Cleric GM, some sessions driven by an
AI agent. Severity: **high**.

### Cited Findings
- **Tuning to one's own tests.** "Read as expected, own sets 95.6% / 95.9%", but "**Read as expected, blind set
  53.0%**". The doc also says "every test set was written by the people tuning the chat". —
  `eval/chat-blind-2026-11-01:docs/bot-voices/CHAT-EVAL.md`
- **The evaluation is circular.**
  - "Both judges and the corpus writer are models, not 2003 players".
  - "The 90 'player' lines in spot-the-bot were also written by a model ... so the test asks whether bot lines stand
    out from period-style lines, not from real logs." — CHAT-EVAL.md, Caveats
  - The bot lines are also AI-written: every line is new writing for the corpus (`WRITING-GUIDE.md`), drafted in
    Claude sessions per the repo's history.
- **LLM judges favour their own generations.** GPT-4 and Llama 2 rate their own outputs above others' even when humans
  judge the outputs equal. They recognise their own text better than chance, and self-recognition correlates
  linearly with self-preference. — [Panickssery, Bowman & Feng, NeurIPS 2024](https://papers.nips.cc/paper_files/paper/2024/hash/7f1f0218e45f5414c79c0679633e47bc-Abstract-Conference.html) (fetched)
- **Other documented judge biases.** LLM-as-judge shows position bias, verbosity bias and self-enhancement bias, while
  agreeing with humans about as often as humans agree with each other (over 80%) on general chat quality. —
  [Zheng et al. 2023, "Judging LLM-as-a-Judge with MT-Bench and Chatbot Arena"](https://arxiv.org/abs/2306.05685) [search hit; content not fetched this session]
- **Humans are poor detectors too.** In six experiments (N = 4,600), people could not detect AI-generated
  self-presentations. They relied on flawed cues (first-person pronouns, contractions, family topics), so AI text can
  come across as "more human than human". — [Jakesch, Hancock & Naaman, PNAS 2023](https://arxiv.org/abs/2206.07271v4) (fetched abstract)
- **Bots can out-score humans on "humanness".** In the 2012 BotPrize (Unreal Tournament 2004), two bots scored 52%
  humanness; the human players scored 40%. The winning bot was evolved by selecting the bots that "appeared to be the
  most human". The game was chosen because it brings out "irrationality, anger, and impulsivity". —
  [Gizmodo 2012](https://gizmodo.com/how-did-this-game-bot-score-higher-than-humans-on-a-tur-5947796) (fetched); [UT Austin news 2012](https://news.utexas.edu/2012/09/26/artificially-intelligent-game-bots-pass-the-turing-test-on-turings-centenary/)
- **Modern LLMs can pass text Turing tests.** A 2025 three-party test reported GPT-4.5, given a persona prompt, judged
  human more often than the real humans it was compared with. — [Jones & Bergen 2025, arXiv 2503.23674](https://arxiv.org/abs/2503.23674v1) [search hit; content not fetched]
- **AI-written content homogenises.**
  - Generative AI raised individual stories' rated creativity but made the stories more similar to each other. —
    [Doshi & Hauser 2024, Science Advances (PMC)](https://pmc.ncbi.nlm.nih.gov/articles/PMC11244532) [search hit; content not fetched]
  - Models trained on recursively generated data lose the tails of the distribution. —
    [Shumailov et al. 2024, Nature](https://ideas.repec.org/a/nat/nature/v631y2024i8022d10.1038_s41586-024-07566-y.html) [search hit; content not fetched]
  - LLM "silicon samples" have reduced variance compared with real human samples. —
    ["ChatGPT is not A Man but Das Man", arXiv 2507.02919](https://arxiv.org/pdf/2507.02919) [search hit; content not fetched]
- **The blind test's weak spots match the tails.** The blind read rate missed "roleplay (16 of 17 missed), banter (21 of
  29), offers of help (16 of 19), RvR calls (13 of 23)". The judges' top tell was "lore musings that talk about the
  game instead of playing it". — CHAT-EVAL.md
- **Unit tests and simulators stand in for play.**
  - SE4-CHECKS: "**Nobody has played it in game yet.**" — `release/1.2:docs/bot-voices/SE4-CHECKS.md`
  - The live-events engine plans a "rehearsal simulator (cloud, every change)" with a fake world, and the transcript is
    "the artifact Claude reviews". — `release/1.2:docs/LIVE-EVENTS-ENGINE.md` §6
  - The economy plan rests on `persona_sim.py` models: "Every number is an assumption-driven model". —
    `release/1.2:docs/CRAFTING-AND-ECONOMY.md` §3.3
- **In-game evidence is thin and narrow.**
  - "the owner plays about five hours a week". — `release/1.2:docs/RELEASE-TRAIN.md`
  - Logged checks: "About two hours, an Albion Cleric", 50 bots. Then "I played Claudecheck (GM, Cleric 50) and judged
    results from the server log rather than my own reaction times", with 300 bots a realm. —
    `release/1.2:docs/LIVING-SERVER.md`, in-game checks
  - The design targets "600–1,000 bots per realm". — LIVE-EVENTS-ENGINE §5
- **Playtesting practice.**
  - Valve's playtesting is built on watching outside players rather than relying on the team's own play. —
    [Valve, "Valve's Approach to Playtesting", GDC 2009 (PDF)](https://cdn.fastly.steamstatic.com/apps/valve/2009/GDC2009_ValvesApproachToPlaytesting.pdf) [search hit; content not fetched]
  - Microsoft's Games User Research group published user-testing case studies for fun-focused games. —
    [Pagulayan et al., "Designing for fun: User-testing case studies" (Microsoft Research)](https://www.microsoft.com/en-us/research/?p=588829); [Games User Research (Springer)](https://doi.org/10.1007/978-3-319-68213-6_27) [search hits; content not fetched]
- **Community feedback is a single voice.** The phase-B RvR redesign was "opened ... from community feedback on the Beta
  1.1 post on r/daoc (a player who tested battleground RvR at about 30% of the population)". —
  `plan/rvr-builds:docs/BOT-BUILDS-AND-RVR.md`

### Inferences
- **(High) Shared priors make AI judging AI optimistic, and blind to style.**
  - The corpus writer, the judges and the line writers are probably from the same model family. Its idea of "a 2003
    player" is one smoothed prior: tidy-casual, positive, never genuinely rude, never wrong in a weird way.
  - Self-preference (Panickssery 2024) and homogenisation (Doshi & Hauser 2024) predict that the judges will
    under-detect bot lines that share that prior, and will rate "period-style" AI lines as human.
  - The 72% catch rate is therefore probably a *lower* bound on how bot-like the lines are against real logs. The
    reply-quality scores are probably *upper* bounds.
- **(High) The AI baseline loses the tails.**
  - Real 2003 chat had flames, leetspeak, non sequiturs, inside jokes, wrong facts and long silences. An AI-written
    "player corpus" under-represents exactly those.
  - The blind set missed roleplay, banter and RvR calls, which are the tails. The judges then rewarded silence (3.99
    against 3.30 for text replies). Silence may read as human partly because the AI baseline lines are bland.
- **(High) A new corpus each round does not cure tuning when the same generator writes every round.** The chat can
  still be tuned to that generator's prior. Holdouts only help if they come from a different distribution.
- **(Medium) Human detection is also unreliable** (Jakesch 2023; BotPrize, where humans scored 40%). "A human panel of
  the owner and r/daoc volunteers" will carry its own heuristics: nostalgia, and the belief that bots are tidy. Humans
  need an anchor of real logs and calibration items (known-real lines mixed in).
- **(High) In-game evidence is one player, one PC, one class, one realm, GM mode, small rosters.**
  - An Albion Cleric healer sees group chat, rez requests and grats. It rarely sees what a stealther, a pet class, a
    Midgard or Hibernian player, or a level-1 newcomer sees.
  - GM status (`/gmstealth`, GM account) may change how bots treat the player. That is not checked.
  - The log-reading sessions run by an AI agent are evidence that **systems fired**. They are not evidence of **how it
    felt**. They should be reported apart from human sessions, as AGENTS.md already does for tests against play.
- **(Medium) Redesigning from one Reddit post is salience bias.**
  - The 30%-population battleground complaint is probably real, since it was checked in code (5,000-unit
    acquisition).
  - But the redesign's other claims ("bots all follow the same path") are partly inferred ("not measured in a
    session"). One loud tester sets the priority list, and quiet players' preferences go unheard (participation
    inequality).
- **(Medium) Simulator-first development risks optimising the transcript, not the experience.** A clean timeline of
  beats does not mean the event felt alive. The live-events research itself says that question "can only be settled
  by the owner playing it."

### Gaps
- No study was found that measures LLM-judge accuracy specifically on game chat or on period-specific register.
- The repo does not record which model families wrote the lines, the blind corpus and the judges. If they are all the
  same family, the self-preference risk is at its maximum. Record it.

## 3. Design biases: what "alive" and "good bot behaviour" are assumed to mean

### Takeaway
Several design defaults pull away from what a 2003 server was:
- more chat where real servers were mostly quiet;
- correct, helpful bots where real players were often wrong, rude or absent;
- organised, competent groups where real groups were mediocre;
- a world that waits for and centres the one human, where a real MMO went on without you.

Some of these are deliberate owner choices (aliveness over authenticity). Others are unexamined defaults or internal
contradictions. Severity: **high** for competence and centring, **medium** for chat volume and accuracy, **low** for
the purity-rule conflicts.

### Cited Findings
- **The stated priority.** "The priority: the player feels the world is alive and that bots respond to them.
  Authenticity to DAoC 2001-2003 is a tie-breaker, not the goal." — `release/1.2:docs/bot-voices/WRITING-GUIDE.md`
- **Chat volume.**
  - The research recommends "Talkativeness heavily skewed: most bots near-silent, a few regulars" (#1).
  - SWG cantinas: "Mean 10–13.5 utterances a day, median about 3".
  - The Peña & Hancock SDs are larger than the means. — PERIOD-CHAT-RESEARCH §B4, §C; `chat_realm_speak.md` §1
  - The implementation: chattiness moves the rate "×0.25 to ×1.56 ... (the realm average stays about the same)". Guild
    chatter fires "Social every ~4 minutes ... busier guilds up to 3× as often". — `release/1.2:docs/BOT-PERSONALITIES.md`
    §2.2; `release/1.2:docs/BOT-GUILDS.md`
  - "Alone Together": WoW players valued others as an audience more than as partners. —
    PERIOD-CHAT-RESEARCH §B4, citing Ducheneaut et al. CHI 2006 ([typeset copy](https://typeset.io/pdf/alone-together-exploring-the-social-dynamics-of-massively-2199ib5pkn.pdf))
- **Accuracy against real players' errors.**
  - The research says to avoid "perfectly accurate advice". — PERIOD-CHAT-RESEARCH §C "Avoid"
  - The evaluation penalises brush-offs and plans "Answer the answerable", with place questions going "to the knowledge
    lookup first". — CHAT-EVAL.md fix 2
  - Bots never send you somewhere "that isn't in your world". — RESEARCH-BRIEF §6
- **Competence and difficulty.**
  - BOT-BUILDS-AND-RVR wants groups to "fill a 2003-style eight", healers who "never break their own mez", and
    "/lfg pbae" groups that "actually bomb".
  - It proposes a difficulty setting "easy, 2003-normal, veteran", with 2003-normal as the default.
  - The companion strand: "Not breaking mez is a hard rule, not a heuristic." — `party_companion_ai.md`
  - The BotPrize winners were selected for looking human. The context was chosen to show "irrationality, anger, and
    impulsivity", and humans scored only 40%. — [Gizmodo 2012](https://gizmodo.com/how-did-this-game-bot-score-higher-than-humans-on-a-tur-5947796)
  - The project's own research says the bots read as "dumb" when they make visibly bad choices with no personality
    behind them, and as characters when the mistakes follow a personality (the Dwarf Fortress lesson). —
    `living_world_sims.md`
- **Centring the player.**
  - "Major events should start only while the human is logged in and should never resolve to failure offline". —
    LIVE-EVENTS-RESEARCH, recommendations
  - "Keep the human decisive inside the crowd ... a named objective that bots defer to". — LIVE-EVENTS-RESEARCH
  - "The player's beat. One objective only a player can do". — LIVE-EVENTS-ENGINE §4
  - Notices are dropped when no player of the realm is online. — LIVE-EVENTS-ENGINE §6
  - The same research quotes a GW2 critique: "50 players in a blob is too many to feel like you matter". —
    LIVE-EVENTS-RESEARCH
  - Erenshor tethers SimPlayers to the player's level ("They won't out level you!"). — `living_world_sims.md` §1
- **The thumb on the scale.** "The director lets the first attempt wipe or stall more often than the numbers would". —
  LIVE-EVENTS-ENGINE §4
- **The success-rate target.** "aim for a success rate of roughly 60–75%", derived from one Hartsman quote contrasting
  80% with 10%. — LIVE-EVENTS-RESEARCH
- **The purity rule conflicts with the research.**
  - The research recommends talk about other games: "EQ always". — PERIOD-CHAT-RESEARCH §C #15
  - The writing guide bans "EverQuest by name (the builder bans it), World of Warcraft". — WRITING-GUIDE.md "Never"
  - "Clean language: no profanity. Period chat was saltier". — RESEARCH-BRIEF §1
  - Bot names: "xX and number names were added on the owner's call", while "DAoC's own filter took letters only, so the
    research found almost none." — BOT-NAMES-2003.md
- **Personality types.**
  - The bot types are Leveler, Socialite, Daredevil, Explorer, Crafter and NoLifer, each with a fixed share. —
    BOT-PERSONALITIES.md §2.1
  - Yee's factor-analytic work found player motivations to be correlated components, not exclusive Bartle-style
    types. — [Yee, "Motivations for Play in Online Games", 2006 (PDF)](https://eclass.uniwa.gr/modules/document/file.php/EEE195/2023-2024/Online%20gaming%20addiction/yee%202006.pdf); [Yee, "Revisiting Bartle's Types"](https://nickyee.com/facets/revisiting.html) [search hits; content not fetched]
- **Economy assumptions.**
  - "every serious level 50 wore a spellcrafted template of crafted pieces" is stated as era fact in CRAFTING §1.
  - The vendor buy-back rate comes from "Allakhazam comments (2002)", a player report.
  - Overcharge comes from an Uthgard thread ("era, weak"). — CRAFTING-AND-ECONOMY.md §1–2

### Inferences
- **(Medium) "More chat = more alive" is partly corrected on paper but not in the defaults.**
  - Per-bot skew is implemented. But the realm average is held constant, and guild chatter on a ~4-minute clock is
    timer-driven.
  - The research and the blind result both say quiet plus well-timed reactions read as more human.
  - What is missing is a **population-level** quiet target: lines per channel per hour by time of day, calibrated to
    something real such as the SWG medians or a real log, not set by feel.
- **(High) Competence bias: the better the bots get, the less they look like 2003.**
  - The RvR plan, the mez rules and the role templates aim for the FreddysHouse "perfect 8". Real 2003 groups broke
    mez, lacked healers, wiped on adds, ran gimped specs and argued about loot.
  - Hard "never" rules remove the mistakes players remember and joke about ("who broke the mez", which is itself on
    the project's debate list, RESEARCH-BRIEF §7).
  - Proposed correction: competence as a per-bot distribution (skill and discipline traits), with personality-coherent
    errors. Keep hard rules only where an error would feel unfair to the human ("never the unforgivable thing").
- **(Medium) Accuracy bias.**
  - "Answer the answerable" plus a knowledge lookup gives a server where every bot knows the way. Period /advice was
    full of half-right answers, outdated patch info and "dunno".
  - The research's own "avoid perfectly accurate advice" is not encoded anywhere. The evaluation penalises
    "beats me", while a real player says it often.
  - Proposed correction: an error budget by experience tier. Newbies are wrong or vague, regulars are mostly right,
    veterans are confident and sometimes outdated (pre-1.62 facts). Grade replies on plausibility, not correctness.
- **(High) Player-centring: the design swings between over-centring and under-centring.**
  - **Over-centred:** events wait for login, never fail offline, give the player the decisive beat, drop notices when
    no player is online, and wipe on cue for drama. A 2003 server's defining feature was that it **didn't care about
    you**. You logged in to find your keep had fallen overnight, and a relic raid happened while you slept.
    "Alive" includes things you missed.
  - **Under-centred:** the strand notes show bots rarely start contact and their memory rarely shows, which is the
    Erenshor lesson.
  - The two errors need opposite fixes. Make the **world** less player-centred (offline outcomes, with consequences
    reported on login). Make the **social circle** more player-centred (the bots who know you).
- **(Medium) "Bots must win" and drama-curve framing.** A director that schedules near-losses is reasonable for one
  player. But a 60–75% success target taken from one quote, plus scripted near-wipes, can make every event feel the
  same, which is the "clockwork" critique the research cites for GW2. Vary the outcome distribution, allow honest
  routs, and log whether the human could tell it was scripted.
- **(Low–medium) The purity rule is applied unevenly.**
  - Banning EverQuest by name removes one of the best-attested period behaviours: constant EQ comparisons, "its like
    eq but better", 2001.
  - Allowing xX/number names that the 2003 filter could not produce adds an anachronism.
  - Banning profanity lowers authenticity. That is defensible as owner policy, but docs should not then call the chat
    "period-accurate".
  - The rule is fine. The bias is that owner taste and authenticity get mixed without saying which is which. Label
    each rule "evidence" or "owner choice".
- **(Medium) The personality types are a designer taxonomy, not data.** Fixed type shares (Leveler 28%, Socialite 20%,
  NoLifer 4% ...) look like Bartle's types. Yee's work suggests continuous, overlapping motivations. Traits already
  exist (0–9), so let traits drive behaviour, and treat types as labels.
- **(Medium) The crafting plan inherits the min-maxer view.** "Every serious level 50 wore a spellcrafted template" is
  true of the forum-active, RvR-focused players. Many casual 50s wore drops and epic armour. A template-hungry bot
  economy may over-demand crafting. Calibrate against a share, not "every".
- **(Low) Single-player and modern taste.** The 2003 purity rule versus what modern players enjoy (convenience, less
  downtime) is mostly handled through owner settings (XP multipliers, upkeep "light"). The risk is low as long as each
  setting is labelled as a deviation.

### Gaps
- No real 2003 lines-per-hour figure for a DAoC channel was found. The SWG and BZFlag numbers are other games.
- No evidence on whether players of a single-player MMO prefer an indifferent world or a player-centred one. Erenshor
  (level-tethered) suggests tolerance for centring, but that is one game.
- The success-rate target has no source beyond one developer quote.

## 4. Biases in this round's own research: the interim synthesis and the four strand notes

### Takeaway
This research round repeats the project's biases. It treats one well-received indie game (Erenshor) as proof. It
favours systems because they are well documented. It moves numbers from non-MMO studies into targets. It quietly
mixes post-2004 sources into 2003 channel facts. And it praises the project's own evaluation. Severity: **medium**
overall; **high** for the /lfg and /trade channel question, because it changes what bots do.

### Cited Findings
- **Headline from one exemplar.**
  - The synthesis: "The best-received single-player fake MMO, Erenshor (2025) ... feel like people because of three
    things". — `reports/Living Server research before 1.2.md` §1
  - The evidence: one developer Reddit post, one Reddit thread and a Steam review aggregator (941 reviews, "weights are
    relative mention frequency"). The press confirmation is "[search hit only]".
  - Its own gap: "No quantitative data on Erenshor review complaints about repetition." — `living_world_sims.md` §1
- **Streetlight effect in the pacing model.** The synthesis prefers RimWorld because it is "a better-documented
  template ... than the Left 4 Dead Director, whose internals are less public". — synthesis §5. Being documented is
  not the same as fitting a 3,000-bot MMO.
- **Moving rates across genres.** The chat strand turns Peña & Hancock (an FPS clan server, 65 players) into "A bot
  population should aim for roughly 3 social lines to 1 task line". — `chat_realm_speak.md` §1 Inferences. The
  synthesis repeats it as what "real players did". — synthesis §3
- **Post-era sources mixed into era channel facts.**
  - The channel list cites the Camelot Herald wiki "Chat" page. That page lists /broadcast in "New Frontiers keeps and
    towers" (June 2004+), and "Ywain1-10 ... clustered" (2009 merger), so it describes a later game.
  - The etiquette guide is "undated" and mentions "LFG, the CGF tool, region chat". — `chat_realm_speak.md` §2
  - PERIOD-CHAT-RESEARCH: "no live general, trade, LFG or region channel", from a German paper on 2004 logs. —
    PERIOD-CHAT-RESEARCH §A2
  - The synthesis says "chat groups up to 200", while PERIOD-CHAT-RESEARCH has "50 members by 1.50". — synthesis §3;
    PERIOD-CHAT-RESEARCH §A2
- **Self-congratulation.** "No studio repetition metrics were found, so our blind evaluation is already more
  measurement than most." — synthesis §4
- **Generalising from shipped successes.** "Every successful system layers control the same way". — synthesis §2.
  Only successful, documented systems were surveyed. The strand says Guild Wars 1 heroes, Mass Effect and Baldur's
  Gate 3 were not covered. — `party_companion_ai.md`
- **PvE companion research applied to PvP.** "No verified RvR/PvP companion-AI sources." — `party_companion_ai.md`.
  Even so, the four RvR complaints are mapped onto L4D and WoW follower mechanisms. — synthesis §2

### Inferences
- **(High) The /lfg and /trade question is unresolved, and the two strands' sources disagree.**
  - Both "region chat exists" and the 200-member chat groups probably come from post-2004 pages. The 2004 log paper
    and the 1.50 notes are the better-dated evidence.
  - Settle it with dated patch notes (1.50–1.65) before moving bot chat between channels.
  - Separately, the owner may choose to keep a convenience /lfg channel as a labelled deviation.
- **(Medium) Erenshor's lessons may not transfer.** Erenshor has a small cast tethered to one player's level. The
  Living Server has thousands of untethered bots. Reciprocity (greetings, memory) probably transfers; the "friends
  progress with you" feel may not. The novelty of the first such game also inflates early review sentiment
  (inference).
- **(Medium) Canon bias across all strands.** Valve (2009 bots, 2012 dialogue), RimWorld, WoW and the GDC canon recur.
  These are Western, well-funded, talk-giving studios. Korean and Chinese MMO bot and NPC practice and the private
  server communities (Discord) are absent. That is partly a search-language effect.
- **(Low) Pattern-mapping confidence.** "Each complaint maps to a documented mechanism" is a design inference
  presented as a finding. It is useful, but it should be labelled as an inference.

### Gaps
- Not checked: whether the Camelot Herald "Beginner's Guide" (node/254) dates from before or after 2004. Its mention of
  "region chat" decides part of the channel question.
- The four strands did not run under a shared "who wrote this and when" provenance check. Each labels [unverified]
  items, but the synthesis drops those labels in places (for example the 200-member chat groups).

## 5. Corrections: what to add, change and test, ranked

### Takeaway
The cheapest high-value corrections:
1. Anchor every chat evaluation to **real period text**, not AI text.
2. Separate **owner choices** from **evidence** in every rule.
3. Make **competence and accuracy distributions** (mistakes included) explicit design targets.
4. Widen in-game evidence: other realms and classes, non-GM, full roster, and at least one outside human.
5. Get **US-dated sources** before freezing build and class weights.

### Cited Findings
- The project already names the next step: "A human panel (the owner, r/daoc volunteers) is the next step for the
  spot-the-bot part", and "an in-game command for the owner and players to flag a bad reply with its context, which
  becomes the next round's corpus." — CHAT-EVAL.md
- AGENTS.md: "Report automated tests separately from real in-game checks." — `AGENTS.md` (project instructions)
- Mitigations for judge self-preference include using judges from a different model family. — [Panickssery et al. 2024](https://papers.nips.cc/paper_files/paper/2024/hash/7f1f0218e45f5414c79c0679633e47bc-Abstract-Conference.html) (implication of the finding; the specific mitigation is my inference)
- Humans need calibration because their heuristics are exploitable. — [Jakesch et al. 2023](https://arxiv.org/abs/2206.07271v4)

### Inferences (all corrections are proposals, ranked by value for cost)

| # | Bias | Severity | Correction | Kind |
|---|---|---|---|---|
| 1 | AI judges grading AI lines against an AI "human" baseline | High | Build a **real reference set**: 300–500 verbatim period lines from dated sources (the Konstanz 2002/2004 logs, FreddysHouse "funny chat log" threads, guild-site log pages, period screenshots, Usenet). Use them **only** as the spot-the-bot human side and as a style anchor, never as line text (keeps the no-copy rule). Report the catch rate against real lines and against AI lines separately | Test |
| 2 | Same model family wrote, judged and baselined | High | Record the model for each role. Use judges from a different family, or two families, and report their agreement. Shuffle order (position bias). Cap reply length in grading (verbosity bias) | Method |
| 3 | Own-set tests standing in for quality | High | Retire "own set" read rates as a quality metric. Keep them as regression tests only. Report blind numbers in release notes | Method |
| 4 | Humans as judges are unreliable too | Medium | The r/daoc panel gets calibration items (known real, known bot), with a short explanation for each call. Weight judges by calibration accuracy | Test |
| 5 | Narrow in-game evidence | High | A test matrix: three realms × {healer, stealther, pet class, melee} × {non-GM, GM}. Full roster (600+ per realm). One newcomer session at level 1. One outside player (not the owner) per release. Tag sessions "human-played" or "agent-run". Track play hours per realm and class in the release notes | Test |
| 6 | Competence bias | High | Add a per-bot **discipline/skill trait** (spec quality, mez discipline, reaction, positioning). Set target error rates by tier, for example: share of groups that wipe on adds, mez breaks per hour, off-meta builds (10–25% suggested; inference). Make mistakes personality-coherent (the Dwarf Fortress lesson). Keep hard "never" rules only for errors that feel unfair to the human | Design |
| 7 | Min-maxer build and comp data | High | Down-weight the "perfect group" threads. Draw RvR groups from a *mix* of templates: FG ideal, PUG (whoever is near), half-built. Add a "levelling or gimped spec" tail per class. Before freezing `bot-builds.json`, try the Wayback Machine again for Camelot Seer and Vault class boards (US). Re-derive weights proportional to counts unless a reason is written down (Scout, Infiltrator, Smite cases) | Source |
| 8 | European proxy | High | Label the data "GOA-derived". Shift dates by the known patch lag when matching era tags. When US data arrives, compare the class shares and record the delta | Source |
| 9 | Accuracy bias | Medium | An error budget by experience tier: newbie wrong or vague, veteran sometimes outdated. Let "dunno" and wrong-but-plausible answers score as human in the grader. Keep exact directions for the bots who know you, or veterans | Design |
| 10 | Over-centred world | High | Let majors resolve offline at low frequency (keeps change hands, relics move) and report on login through bot gossip and the Herald ("you missed it"). Keep the player's personal beats for the social circle. Make "fires only while logged in" a setting, not a law | Design |
| 11 | "More chat = more alive" | Medium | A population-level quiet target: lines per channel per hour by time of day and population, with a "dead hour" baseline. Measure it in the planned "hour at 900 bots a realm" run. A silent-night A/B session for the owner | Test |
| 12 | Purity rule mixed with taste | Low–Med | Tag each rule "evidence" or "owner choice". Reconsider the EverQuest ban (allow generic "eq" comparisons). Keep xX/number names labelled as a deviation | Design |
| 13 | Personality types as a taxonomy | Medium | Drive behaviour from the continuous traits. Validate type shares only if a period source exists; otherwise label them as designer estimates | Design |
| 14 | Nostalgia | Medium | In owner and panel reviews, rate "felt like 2003" separately from "fun now", and include unpleasant period realities (downtime, LFG waits, rude chat) as options to switch on, not omissions | Method |
| 15 | Single Reddit voice | Medium | Collect feedback with a short structured form (realm, class, roster, what happened, log excerpt) through the planned in-game flag command. Weight issues by reproduction in the log, not by loudness | Method |
| 16 | Era errors from post-2004 pages | High (channels) | Settle /lfg, /trade, region chat and chat-group size from dated patch notes before moving any chat. Add a "source date" field to every era fact in the research docs | Source |
| 17 | Simulator-first development | Medium | Every simulator target gets a matched in-game check before the feature is called done (already the AGENTS.md rule). Add "felt" notes (a few honest lines) beside the timeline report | Method |
| 18 | Canon and survivorship in the design research | Low–Med | For each borrowed system, find one documented failure of the same pattern. Add one non-Western or non-GDC source per strand where possible | Source |

### Gaps
- Whether verbatim period chat logs survive in quantity (guild sites, the Konstanz paper's appendix) is the deciding
  unknown for correction 1. It belongs to the under-credited sources strand.
- The suggested error-rate targets (10–25% off-meta, wipe rates) are my estimates. No period measurement was found.
