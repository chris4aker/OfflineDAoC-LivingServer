<!-- Research for bot voices 0.36-se.4 (2026-10-18). Complements RESEARCH-BRIEF.md; where they differ, the dated corrections in section D apply. -->
# How DAoC players talked, and how to make bot chat believable (research, 2026-10-18)

Evidence labels: **[V]** verified in a cited source (period = 2001–2004); **[R]** verified, later
memory; **[K]** general game knowledge not checked here; **[I]** inference or design judgement.
Example lines marked *pattern* are paraphrased shapes; *verbatim* quotes are under 15 words and came
through a summarizing fetch tool, so treat exact wording as close but not guaranteed.

## A. How DAoC players talked (2001–2004)

### A1. Slang by era

**From launch (October 2001) [V]:**

| Area | Terms | Sources |
|---|---|---|
| Group and combat | inc, add, BAF, pull, bait, aggro / agro, taunt, tank, mez, stun, root, snare, kite, AoE, PBAoE / PBAE, CAE, DD, DoT, HoT, LT, BT, PBT, OOM, OOP, LoM, med, GTG, RFP, r, rez, loop, roll, MA, wipe, pat, pop, camp | Camelot Herald beginner's guide; Herald wiki jargon page |
| Levels and monsters | ding, grats / gratz / gz / cg, bub / bubble (a tenth of a level), con colors grey to purple, grape / PTM, gray, twink, PL, KT | Herald guide; wiki jargon |
| General chat | afk, brb, brt, omw, wb, np, k / kk, ty / thx, lol, rofl, imo, btw, irl, jk, nvm, cya, gn / nn | Herald guide; wiki jargon |
| Connection and trade | LD / DC, lag, warp, WTS, WTB, WTT, LFG, LFM | Herald guide |
| Tells | "tell" for a private message ("Give me tell!", verbatim) | AnandTech, Nov 2001 |
| Class talk | gimped, uber, nerf ("so gimped not even funny", verbatim) | AnandTech, Feb 2002 |
| Internet slang | n00b, teh, pwn / ownz | AnandTech, 2001 and 2004 |
| Mistakes | MT (mistell) | Daedalus glossary |
| Other | zerg, gank, alt, mule, bind, CSR / appeal | Herald guide; Daedalus glossary |

**Period, dated:** buffbot (2002–2004); radar users (2004); QQ (attested July 2004); FTW (2005
glossary, so late 2003 at the earliest); carebear (period-plausible [R]).

**RvR [V]:** RvR, RP, RR, BP. Realm abilities from 1.50 (about May 2002): MoC, Purge, IP, Det, SoS,
AM, DI, PR, LW. Uncertain date (keep out of pre-2004 lines): BoF, BAoD, TWF, VP. AJ; bomb group
(originally Hibernian [R]); 8man and "adding" with Ventrilo; stealther war; speed songs [R]; relic
raids [R]. Old frontier: MG, AMG, HMG, MMG ("diving off the wall at Alb MG in Emain", 2013, verbatim);
neck check; PK, APK, HPK, MPK. Places: OG, Jamt, Upp, Spin, TG, DF. /stick: date unverified.

**Out of era before these dates:**

| Date | Terms |
|---|---|
| ToA, 28 Oct 2003 | ML, artifacts, scrolls, trials, Atlantis (battlegroups attested in ToA-era notes, may be earlier) |
| New Frontiers, 22 Jun 2004 | NF towers; battlegrounds Proving Grounds, Lion's Den, Hills of Claret, Killaloe, Braemar, Wilton, **Molvik, Leirvik** |
| Catacombs, 7 Dec 2004 | Catacombs classes, Cata, ID, TD |
| Feb 2005 | Server clusters |
| Aug 2005 | Agramon |
| Darkness Rising, 11 Oct 2005 | CL (Champion Levels) |

**Probably too late [I]:** o/, &lt;3, XD, lulz, "fail" as a noun, l2p, imba, OP, toon, DPS as
everyday chat (KillTenRats [R] says it wasn't yet common), any 2010s slang.

### A2. Channels and message shapes

**Live channels [V]:** /say (small radius); /yell; /whisper; /send, /tell, /reply (own realm); /g,
/p; /gu; /o; /as; /cg, /chat (player chat groups, 50 members by 1.50; public, private, password,
listen); /bg, /bc (battlegroups, ToA-era use attested); /broadcast (designated areas only); /em;
/advice ("for asking questions of fellow realm mates", Herald); /afk with a message (1.65); /rpfilter,
/who RP; an LFG window by 1.65 (a listing, not a channel).

**Not found:** no live general, trade, LFG or region channel. /trade only toggles trading. A German
paper on 2004 logs lists nine chat options, none of them trade or LFG.

**[I]** Realm-wide talk went through /advice and public /cg; trading went through /broadcast, /say in
capitals and /send. OpenDAoC's /advice is realm-wide with a 60-second slow mode (`advice.cs`); that's
the emulator's design, not confirmed for live.

**Shapes (patterns):** `lfg 34 druid`; `lfm spraggon need tank+healer`; `WTS MP 2h sword /send
offers`; `INC`; `inc 20+ albs from mg`; `crauchon under siege need def`; `ram on door at benowyc`;
`relic raid sunday morning, all welcome`; `rez pls?` → `ty for rez` → `np`; `can i get buffs?`; `r`;
`oom, med`; `add!`; `mez the casters`; `assist X`; `ding 30!` → `gz`; `where do i train?`; "Mid 4
life" (verbatim [R]); `scouts OP from 2500 range`, `zerkers nerfed`, `friars gimped`; "its like eq but
better" (verbatim, 2001); WoW anticipation in 2004.

Support: Mythic's guide, the wiki jargon page, AnandTech threads 2001–2004, Engadget's 2010 reader
memories, Project Massive's topic shares (support and advice 77%, small talk 77%, coordination 76%).

### A3. Style

- Period forum posts: lowercase and full of typos ("Awsome", "opertunity", "bakcwards") [V].
- 2002 and 2004 DAoC chat logs (Konstanz paper) [V]: heavy abbreviation (thx, afk, lol, lvl, gratz),
  spelling that follows speech, dropped subjects and verbs, asterisk actions, emoticons, arrows used
  to point; full sentences only in roleplay.
- Mythic's guide: all caps is yelling [V].
- Message length [V]: BZFlag 1.4 words; IRC 4–6; MUD groups 6–7; one-to-one MUD chat 10–14.
- **[I] Targets:** RvR and group calls 1–4 words; banter 4–8 words; lines near 160 characters about
  5% of the total.
- Servers [V]: Mordred was PvP (guild against guild); Gaheris co-op (DF focus); Classic servers came
  2004–2005, partly against buffbots. Guinevere and Percival as official RP servers: unconfirmed [K].

### A4. Realm culture and places

- Names [V]: Albs, Mids, Hibs; race names as-is; no "smurf" or "kobbie" found.
- Stereotypes, 2004 [V]: Albion "whiney", Midgard "underpopulated", Hibernia "fruity". Hibernia
  unfinished at launch. "albs on all the servers are owning the relics" (verbatim, July 2004). 2002:
  Albion crowded and disorganized. The third realm cleaning up after a fight [R].
- Classes [V]: 2002 Theurgist and Thane uber; Friar, Warrior, Paladin, Sorcerer, Warden gimped. 2004
  "everyone and their donkey can heal" (verbatim); archers mocked; Scout range (2,500) and Berserker
  nerf complaints. Memories [R]: Spiritmasters at the Alb MG in Emain; Hibernian bomb groups; a
  Shadowblade camping the Sauvage road; Clerics as rez bots.

| Place | Notes | Status |
|---|---|---|
| Emain Macha | "the only place to RvR" (verbatim, 2004) | [V] |
| Darkness Falls | From 1.49; the realm with most keeps gets access, which caused fights | [V] |
| Thidranki, Murdaigean, Caledonia, Abermenai | Classic battlegrounds; Abermenai ("BG0") from 1.62 | [V] |
| Salisbury Plains, Forest Sauvage, Muire Tomb, Lyonesse, Cornwall, Caer Boldiam, Humberton, Fort Atla | Named in memories | [R] |
| Caer Benowyc, Bledmeer Faste, Dun Crauchon | Keep names | [V] |

Memes [R]: the "Cloudsong" rant was famous but is profane and undated; skip it.

### A5. Dates

| Date | Event |
|---|---|
| 9–10 Oct 2001 | Launch; 51,000 boxes in four days |
| ~Feb 2002 | 1.45: realm dragons |
| ~Mar 2002 | 1.47: guilds can claim keeps |
| Apr 2002 | 1.49: Darkness Falls |
| ~May 2002 | 1.50: realm abilities; chat groups to 50 |
| — | 1.52: full respec |
| Jul 2002 | About 250,000 subscribers |
| 12 Nov / 3 Dec 2002 | Shrouded Isles gold / NA retail |
| Jun 2003 | 1.62: /level, newbie guilds, Abermenai, block/parry fix, alliance and officer chat tabs |
| 18–19 Jun 2003 | Housing |
| 8 Oct 2003 | 1.65: /afk messages, LFG window |
| 28 Oct 2003 | ToA; grind complaints follow ("long camps"; RvR paused to grind; later "reviled") |
| 22 Jun 2004 | New Frontiers |
| Nov 2004 | WoW and EQ2 launch |
| 7 Dec 2004 | Catacombs |
| Feb 2005 | Clusters |
| 11 Oct 2005 | Darkness Rising (Champion Levels) |
| Sep 2009 | All servers merged into Ywain |

Context [V]: EverQuest comparisons constant from 2001; players came from Ultima Online and Asheron's
Call; WoW anticipation through 2004; voice chat almost never used in the 2003-era survey.

### A6. Norms

- Mythic's etiquette [V]: don't kill-steal or leech; ask before buffing; don't beg; ask if someone has
  time.
- Players camped one spawn; nothing instanced; good groups scarce [R]. 54% grouped with strangers at
  least once per session; 2% only with guildmates (Project Massive) [V].
- Rituals [V]: hi, gn, nn, cya, wb, brb, afk, ding / gratz, ty / np, LD. "wife aggro" [I]. Running
  joke [R]: "after I finish this level and train". Newbie guilds, /who and /advice for help.

## B. Design research

### B1. Ruskin's dynamic dialogue (Valve, GDC 2012) [V]

- The world is a flat set of facts. A query (concept + who) merges event, speaker, speaker memory and
  world facts. A rule passes only if all its criteria pass; score = number of criteria; the most
  specific rule wins; ties random; fallbacks always exist.
- Responses write facts back with expiry ("played", a short "speaking" lock) → running gags and
  escalation. Ruskin's retrospective: better if a response removes itself after playing.
- Follow-ups send a concept to another character or broadcast it; they re-query when they fire, so
  interruptions need no extra logic. Monologues are chained short lines.
- Per-line odds (Coach's per-survivor lines under 30%). Over 10,000 lines; Valve engine flags
  norepeat, permitrepeats, odds, speakonce, respeakdelay, matchonce, applyContext.
- Advice: give writers the state, not the events; a plain format; additive; build debugging tools.
- **[I] Borrow:** specificity scoring, write-back with expiry, re-checking follow-ups, odds of
  silence, a debug view. Skip the performance work.

### B2. Barks [V]

- Kotaku (Dansky): frequency drives repetition; "all the good lines are gone" at the end of a pool;
  one piece of information per bark; context-blind triggers sound absurd.
- Quake III bot chat: initial chats plus reply chats, match templates, synonyms, random strings,
  priority keys with required (&amp;) and forbidden (!) terms.
- mod-ollama-chat brakes: chain decay; replies only if a human spoke recently; cooldowns per bot,
  channel and server; a penalty for repeated opening phrases.

### B3. Bot projects [V]

- mod-playerbots: event broadcasts (loot by quality, quests, kills, levels, guild);
  `ai_playerbot_texts` has 134 `suggest_something` and 130 `reply` lines; default repeat delay 2 s.
  Weak lines: generic ("Anyone feels alone?"), meta ("Why is everybody always saying the same
  things???"), modern slang, taunt lists.
- mod-ollama-chat docs tell users to turn off seven built-in playerbots chatter settings. NerdZap on
  an 1,800-bot LLM server: "a series of disconnected statements". A video: bots "mindlessly argue".
  An LLM module author: silent bots "broke immersion".
- Not researched: SPP Classics, EQEmu, Old School RuneScape bots.

### B4. Numbers [V]

| Source | Findings |
|---|---|
| Project Massive (442 DAoC players) | Topics: advice 77%, small talk 77%, coordination 76%. 78% in guilds (41–45 members on average). Voice almost never used. 39% play mainly for the social side. |
| WoW ("Alone Together") | About 30% of time grouped at low levels; 62% in guilds; median guild 9; only 11% of guildmates grouped with a player for over 10 minutes; others valued as an audience. |
| Suznjevic and Matijasevic (WoW) | Communication second only to raiding; median session 25 minutes. |
| Daedalus | About 60% have roleplayed; a fifth weekly. |
| SWG cantinas | Mean 10–13.5 utterances a day, median about 3; 35–71% of sentences unique. |
| Peña and Hancock (Jedi Knight II) | 77% social, 23% task; positive to negative about 3 to 1; greetings about 5%. |
| Herring et al. (BZFlag) | 57% react to play; 40% respond to another message; bursts of about 25 messages. |

**[I]** A few chatty regulars, bursts tied to events, about 25% task talk.

### B5. Variety

Tracery and Quake III style grammars [V] (keep nesting to 1–2 levels); style transforms [I]
(`*correction`, mistells, "heh", "..", dropped words); talkativeness as its own axis [I]; ritual pairs
with re-checking follow-ups [V/I]; a per-channel topic memory [I].

## C. Ranked recommendations (S = under a day, M = 1–3 days)

| # | Recommendation | Effort | Evidence |
|---|---|---|---|
| 1 | Talkativeness heavily skewed: most bots near-silent, a few regulars | S | SWG medians |
| 2 | More lines from real world events, fewer from timers | M | BZFlag 57%; Ruskin; playerbots |
| 3 | Most-specific line wins, with fact write-back and expiry | M | Ruskin |
| 4 | Never answer everything: 0–2 replies, chain decay, recent-human rule, reading and typing delays | S | mod-ollama-chat; bot-server reports |
| 5 | Faithful channels: /faction as advice and RvR calls; trade and banter local, /gu, /g, /send | S–M | Live channel lists |
| 6 | Era tags; reject NF battleground names, Agramon, clusters, CL and ML, modern slang; hold /stick and late realm abilities until dated | S | §A5 |
| 7 | Shorter lines: calls 1–4 words, banter 4–8, about 5% long | S | §A3 |
| 8 | Writing styles: proper case at most 15% and mostly without full stops; caps for alarms only; `*corrections`; mistells | S | §A3 |
| 9 | Ritual pairs: staggered grats; rez → ty → np; LD → wb; inc → where / omw | S | Peña and Hancock |
| 10 | Realm and class flavor from the confirmed arguments, kept friendly | M | §A4 |
| 11 | Threads that can be cut off; a 3–5 minute channel topic memory; quiet afterwards | M | Ruskin; BZFlag |
| 12 | Repetition checks on opening words and templates | S | mod-ollama-chat; Kotaku |
| 13 | Daily and weekly rhythm (quiet nights, Sunday relic raids) | S | Reader memories [R] |
| 14 | A GM "why this line" debug command | S–M | Ruskin |
| 15 | Era-aware talk about patches and games (EQ always; SI hype late 2002; ToA complaints only after 28 Oct 2003; WoW in 2004) | S | §A5 |

**Avoid:** bots answering everything; even, constant chatter; polished grammar; self-aware bot lines
and joke or taunt lists; modern slang or later-expansion vocabulary; realm-wide WTS spam; perfectly
accurate advice; profanity.

## D. Against the existing RESEARCH-BRIEF.md

- **Confirmed:** realm abilities in 1.50; DF in 1.49 with access by keep control; Abermenai in 1.62;
  classic battlegrounds Thidranki, Murdaigean, Caledonia, Abermenai; /level and newbie guilds in 1.62;
  /afk messages in 1.65; chat groups of 50 at 1.50; mile gate and portal keep jargon; Emain as the main
  fighting ground; relics following population; buffbots; Scout range complaints.
- **Corrected:** SI retail was 3 Dec 2002 (12 Nov was gold); Caledonia's 30–35 range unverified;
  Molvik and Leirvik are New Frontiers battlegrounds; "o/" and "toon" probably late.
- **Open:** the /stick date; dates of BoF and other late realm abilities; whether Guinevere and
  Percival were official RP servers; when battlegroups arrived; whether live /advice was realm-wide.

## Pages that could not be opened

Wikipedia's DAoC article, every web.archive.org page (Vault and VN boards),
assemblyrequired.crashworks.org, eden-daoc.net, ResearchGate. Period in-game chat logs were the
thinnest evidence.

## Sources

**DAoC, official or period:** camelotherald.com/node/254, /node/255, /node/43, /node/7;
camelotherald.fandom.com/wiki/ Chat, Jargon, Patch_Notes:_Version_1.50, 1.52, 1.65, 1.81,
Battleground_Limits?oldid=8531, Frontiers, Odin%27s_Gate, Hadrian%27s_Wall, Harassment_Policy?oldid=4381;
camelotherald.com/article/new-frontiers-update-coming; gamebanshee.com 3r6y, kpd9c, aay4, k8d4q,
k69rp, ku6ya, k69hf, kkawk and /news/72748-daoc-new-frontiers-beta-now-available.html;
archive.rpgamer.com/news/Q4-2002/111202c.html;
worthplaying.com/article/2003/11/11/reviews/13709-pc-review-dark-age-of-camelot-trials-of-atlantis/;
gamedeveloper.com (Darkness Rising press release; Mythic DAoC postmortem);
gamesradar.com/camelot-goes-classic;
wikipedia.classicistranieri.com/en/articles/d/a/r/Dark_Age_of_Camelot_5d26.html.

**Period player forums:** forums.anandtech.com threads 644146, 632591, 721022, 749563, 1276423,
1364207, 1481009; ling.sprachwiss.uni-konstanz.de/pages/home/butt/main/material/mmorpg.pdf;
valarguild.org/community/NewsArchives/news2001.htm; oocities.org/nikkye1/daoclinks.html.

**Retrospectives:** newoutriders.org/?p=2151;
massivelyop.com/2016/07/02/the-game-archaeologist-dark-age-of-camelot/;
engadget.com/2010-08-17-the-game-archaeologist-and-the-quest-for-camelot-your-journeys.html;
gamebanshee.com/3u3uf; killtenrats.com/2011/01/07/becoming-hardcore-dark-age-of-camelot;
tropedia.fandom.com/wiki/Dark_Age_of_Camelot; dolserver.sourceforge.net viewtopic p=128212, 148080,
18907; forums.anandtech.com threads 202651 and 2300816;
gamestar.de/artikel/dark-age-of-camelot-20-jahre-zurueck-nach-midgard,3373222.html;
ugp.rug.nl/tvs/article/view/36929.

**Dialogue systems and barks:**
cdn.akamai.steamstatic.com/apps/valve/2012/GDC2012_Ruskin_Elan_DynamicDialog.pdf;
gdcvault.com/play/1015528/ and /play/1015317/; emshort.blog/2012/03/16/gdc-2012-talk-on-dynamic-dialogue/;
developer.valvesoftware.com/wiki/Response_System; github.com/subtalegames/mimir;
gamedeveloper.com/blogs/creating-behaviours-with-fuzzy-pattern-matching;
kotaku.com/why-video-game-characters-say-such-ridiculous-things-5921878;
icculus.org/gtkradiant/documentation/bot_manual/chatsystem.html;
graphics.tudelft.nl/Publications-new/2015/CKM15; screenhub.com.au Kasavin interview; gamedeveloper.com
GDC 2021 Hades dialogue piece.

**Bot projects:** github.com/mod-playerbots/mod-playerbots;
deepwiki.com/liyunfan1223/mod-playerbots/3-bot-creation-and-management;
github.com/DustinHendrickson/mod-ollama-chat (and discussions/23);
github.com/azerothcore/azerothcore-wotlk/discussions/25107;
github.com/mod-playerbots/mod-playerbots/discussions/2237; github.com/cmangos/playerbots;
nerdzap.com/news/wow-private-server-1800-ai-bots-deepseek/;
sozai.app/transcript/me-1472-bots-world-warcraft/.

**Research on MMO chat:** hci.rwth-aachen.de/materials/conferences/CHI2004/2p1421.pdf;
dl.digra.org/index.php/dl/article/download/75/75;
ics.uci.edu/~wscacchi/GameLab/Recommended%20Readings/Sociability-MMOG-Ducheneaunt-2004.pdf;
dl.eusset.eu/items/f01f68ba-a365-40de-bd86-046477557db5/full; dx.doi.org/10.1145/1124772.1124834;
engadget.com/2006-02-08-massively-multiplayer-single-player-gaming.html;
nideffer.net/classes/270-10/week_09_wow/buildinganmmo_wow_etal.pdf; Stanford Social Media Lab PDF of
Peña and Hancock; homes.luddy.indiana.edu/herring/hicss.bzflag.pdf;
tel.fer.unizg.hr/images/50008067/IJAMC2010-preprint.pdf; nickyee.com/daedalus/archives/2005_03_30.php;
raphkoster.com/?p=532; dspace.tul.cz/items/c2c96327-2d5c-4aa8-8a98-7eb123a67319/full.
