# Player-experience audit: Living Server against the North Star

Auditor lens: what a player sees and does. Sources: `origin/release/1.2` at `50ecb30` (2026-11-01) unless named;
`origin/plan/rvr-builds`, `origin/research/1.2-before-coding`, `origin/eval/chat-blind-2026-11-01`; open PRs #44-#57;
the owner's independent audit (attachment, final section). Read-only; no branch changed.

Labels: **[V]** verified in a cited doc or code path. **[V-game]** a documented in-game check saw it. **[I]** my
inference. Most features have automated tests only; where a doc says "not seen in game" I say so.

North Star (owner): "the most amazing DAoC experience offline with bots and a world that feels like it's a fully
alive peak server from the era, with added features and Easter eggs." Peak = a crowded 2002-2003 server at prime
time.

**One-line verdict.** The *realm channel* feels alive (34k lines, debates, storylines, spammers, news that is true).
The *street* and the *group* do not yet. A 2003 server was lived in /say at a camp, in /broadcast at a bind stone, in
an invite from a stranger, and in your guild. Here the richest life is in a realm-wide `/faction` channel that did not
exist in 2003. Meanwhile the player can never be invited into a bot group, and the world stops when the server stops.

---

## 1. Journey table

Score: **A** = feels like a peak server. **B** = mostly. **C** = present but thin or fake-feeling. **D** = missing or broken.

### 1a. The player's timeline

| Moment | What exists today | Alive / empty | Gap vs a 2003 peak server | Fix idea |
|---|---|---|---|---|
| **Install (min 0-5)** | One-click Setup downloads the base, updates 0.36→0.37, adds LS, turns **everything on** with schedule `clock` [V `tools/installer/src/Engine.cs:160`, `SocialExpansionSettings.Recommended` L60-68]. SmartScreen warning [V README]. | **B.** Smooth, honest, with rollback. | The end screen tells you to "Fill the realms" by hand [V `Pages.cs:866`]. The server you joined is **empty**. | Setup's last step offers "Populate my server" and builds a level-spread roster per realm (see gap 1). |
| **First launch, empty world** | "A fresh world has no bots yet" [V README "Getting started"]. Bots come from the + Lv.1 / + Lv.50 buttons. A Lv.1+Lv.50 roster has **nobody at 20-49** until bots level [V KNOWN-ISSUES]. Lv.50 batch bots get a q100 kit and 10,000 gold each [V CRAFTING §3.2, `MainForm.cs:2224-2257`]. | **D** out of the box. The player builds the population, so the illusion breaks at minute one. | A peak server had a bell curve of levels, alts and twinks, and a lot of 50s *who earned it*. | A "2003 server" preset: level-distributed bots, ages and /played spread. Pre-age the world (sim-level bots offline before first login, gap 1). Stop giving free kits to "server" bots, or tag them "transfer" characters. |
| **Character creation (min 5)** | Stock creation. 1.2 discontinues the Sluaghbinder as the base did [V `50ecb30`]. Bot names are researched 2003-style [V `research/BOT-NAMES-2003.md`]. | **C.** Nothing tells you the server is "populated". There is no "server status: High" moment. | Server-select population ("Heavy"), a crowded starting town, and /who returning 200 names. | Make `/who` and the realm population count read like a busy server (verify that `/who` lists gamebots: **not checked by me** [I]). |
| **First zone (min 10)** | Body language in town: looks, emotes, waves at you [V-game LIVING-SERVER "Body language"; steps and cheers not seen]. Beggars in capitals, 4-5 per capital at 900/realm, follow and insult you if you ask anyone for gold [V TALKING-UPDATE §11; in-game list item 14 unchecked]. Bots greet you only once they remember you. | **C+.** Waves and beggars delight. Starting zones fill only if you made Lv.1 bots; there are no level-1 *newbies asking questions in /say* near you [I]. | 2003 newbie yards were crowded, with KS fights over the same mob, "where is the trainer?", buff begging, and /advice. | Newbie-yard density and etiquette: shared camps, "camp check", a polite KS complaint (REPORT #11, BOT-GROUP-FRAMEWORK §3). Add newbie-guild chat for bots under 15 (REPORT #2; patch 1.62 is dated). |
| **First hour: levelling** | XP by level range [V `XP-BY-LEVEL-RANGE.md`]. Grats [V-game]. Deeds: solo a named and /faction hears of it [V; in-game list item 15 unchecked]. True rumours of rare named spawns [V; item 18 unchecked]. Bots path around red cons and remember death spots [V README]. | **B-.** Chat around you is plausible. Your *camp* is not contested. | Camps were crowded. You waited your turn, or you got told off. | Camp occupancy and turn-taking as a visible behaviour: bots queue, ask "camp check?", and leave for a free camp. |
| **First quest** | 448 classic quests in the base. 1.1 ported HearthDAoC quest code **unplayed**. Defects: a bot-group kill gives no credit; hand-ins are refused above max level; quest XP ignores XP rates; epic chains are unlinked [V QUESTING §1]. Fixes are in PR #45/#46, open and unplayed [V gh]. | **D** for a solo-with-bots player, by the code [V]. Not confirmed in game. | Quests were a minor 2003 activity, but broken quests read as a broken game. | Merge #46/#45 after the three-quest A/B (RELEASE-TRAIN 1.1.1). Run the quest harness over all 448 rows (QUESTING §2.2). |
| **First group** | **Players lead, bots join.** "A bot can't invite a player" [V `AutonomousLivingServer.Tells.cs:14`, `memory.lines:195`, TALKING-UPDATE §1, §5]. The `lfm` lines are written but never said because "a bot group can't take a player" [V TALKING-UPDATE "Content"]. Your /lfg may get a duo offer by tell, and then *you* invite [V; item 6 unchecked]. `/invite <name>` works within 6,000 units [V-game]. The between-pulls LFG wait didn't raise group counts in a 39-min check [V KNOWN-ISSUES]. | **C-.** Functionally you can always get a group by inviting. Socially it is backwards: *you are never wanted*. | The defining 2003 moment was "/invite? we need a healer at the Spindelhalla 3rd floor". Getting picked up, or not. | **Let bot groups invite the player** (the base group code supports a bot leader [I]; this is the single highest-value missing loop). Turn on the dormant `lfm` and "recruiting" lines once it works. Have bots set the LFG flag so they show in the LFG window (REPORT #1; no LFG-flag code in `bots/` [V grep]). |
| **First group fight** | Combat calls from real state: inc, add, oom, mez landed, wipe [V TALKING-UPDATE §4; in-game item 2 unchecked]. Linkdeads and brb [V-game: brb; ld seen in the 2026-10-18 playtest, KNOWN-ISSUES]. Base spell engine (PBAE, add control, guard) merged in phase A [V RELEASE-TRAIN plan]. | **B** on paper. Whether calls are timely is **not seen in game** [V]. | Real groups also broke mez, argued about loot, had a bossy leader and a silent one. | The discipline trait (BOT-GROUP-FRAMEWORK G2) with personality-fitting mistakes. Stances and a named puller (G1). |
| **First evening (hours 2-4)** | /faction: openers every 2-4 min, scaled by roster and evening [V TALKING-UPDATE §6]. 393 debates, dunce rage-quits, gold spammers who get "banned", thee-and-thou roleplayers, four-episode storylines you can take sides in [V TALKING-UPDATE §10-14; storylines and spammers unchecked in game]. Leeroy in 1 of 20 raids [V; later fixes unplayed]. Holiday chatter [V]. | **A-** for density and wit. **But** the blind judge caught **72%** of bot lines; 34% of replies ignore what was said; silence scored better than the average reply [V CHAT-EVAL]. Repeats reach 58-60% word-for-word by week 4 [V TALKING-UPDATE §8]. | The realm-wide chat is itself anachronistic: "No /lfg or /trade chat channel existed by 1.65", and realm talk lived in chat groups, guilds and /broadcast [V REPORT channel table]. `/faction` is sent as `CT_Broadcast` to every player of the realm [V `AutonomousBotChatCoordinator.cs:981-990`]. | Move the life to period channels (C1): town /broadcast trade macros, LFG in /say near binds, public chat groups the player joins. Keep `/faction` as a labelled owner choice (owner priority is aliveness). **Silence over weak replies** (CHAT-EVAL fix 1) is the cheapest believability gain. |
| **First RvR (levels 20+)** | Battlegrounds, frontier alarms ("inc 8+ mids at Caer Benowyc") [V-game: 9 in 80 min], nemeses and Most Wanted [V-game: records; lines not seen], warbands, sieges with timers that really run since 1.1 [V KNOWN-ISSUES; checked only at about 1,000 bots]. Community: bots spot you from 5,000 units at once, about 10 Cabalist pets chase you, every bot walks the same path [V BOT-BUILDS-AND-RVR]. Perception fix is PR #53 [V gh]. | **C.** Alarms feel right. Combat reads as radar-aimbots plus a pet zoo. | 2003 RvR = zergs at the milegates, 8-man roams, stealther wars, a bard speed train, "/assist X", PBAE groups. Bots that *don't* see you. | Ship PR #53 perception and pet caps first in phase B. Lanes and roaming 8s (G5). Weight builds by evidence *and* tail (REPORT #6). |
| **First week: 20-50 levelling** | Personalities tilt camps and dungeons [V]. Guilds recruit you on "lf guild" [V-game], greet you at login, and chat by charter [V-game]. Bot memory greets and thanks you [V-game]. Bots remember each other and form friends and grudges [V LIVING-SERVER "bonds"]. | **B.** Guild life is the most convincing *social* system seen in game. | Guild identity is generic: no named leaders with reputations, no drama between guilds, no guild you can't get into. | Rival guild per realm (CHAT-FRAMEWORK 3.4.1). Recognisable officers. The guild-tiers package (backlog). |
| **First week: crafting** | Players can craft. **No bot crafts**, and the Crafter personality "does nothing craft-related". Guild banks fill and are never spent. A player's /trade line is heard only by beggars [V CRAFTING §3.2]. | **D.** The market is dead apart from beggars and bot Exchange listings (one item per bot, 24 h expiry). | 2003: "/broadcast LGC Selling 99% chain, /send", masters' titles, a famous tailor per realm, commission requests. | Before the full M0-M6 plan: a **visible** crafter (a bot at the station with a /broadcast ad that takes commissions by tell), even if it is ledger-backed. |
| **First week: raids** | 17 raid targets per realm, on their own schedule [V README]. Wipe→regroup→Epic Special Forces [V; dragons not until PR #44]. Contested DF raids [V]. Automatic raids need **80 eligible level-50s online** [V KNOWN-ISSUES]. Mechanics are won on numbers; no raid leader voice [V LIVE-EVENTS-ENGINE §2 "Not there"]. Lord Gildas fell to 180 bots in 61 min [V-game]. | **B-** as spectacle when your roster can run it. **D** on a small roster. Can the player *join* a raid? The docs describe raids as bot affairs; I found no "raid invite to player" [I]. | 2003 raids were player-run events: /bc "raid forming at Mag Mell, /send for invite", loot drama. | A raid leader bot who calls and *invites the player*. The live-events "player's beat". Scale the raid threshold to roster size. |
| **First relic raid** | "No relic has been captured by a bot army yet" [V README "Good to know"]. The base's final main adds relic campaigns that can take a relic home [V RELEASE-TRAIN]; that has not been played. | **D** as an experienced event. | Relic raids were the peak server's top story: realm-wide panic, broadcast, a zerg. | Play one relic campaign end to end (already on the 1.2 played list). Give it the full news treatment. |
| **Returning after a week away** | `clock` schedule: about 95% online 8-10 pm, 20% at 4 am [V]. Session restart starts the first /faction scenes within 30 min [V]. **Nothing happens while the server is off:** no catch-up code [V grep; no offline resolution], and live events "never resolve to failure offline" by design [V REPORT citing LIVE-EVENTS-ENGINE §4, §6]. Herald is a static launcher page [V LIVING-SERVER "Camelot Herald"]. | **D.** The world waited for you. That is the clearest break from "a server". | "You missed it: mids took our relic Tuesday, Avalon raid wiped twice, Vaelin hit 50." | Offline progression at login (gap 3). Gossip "you missed it" lines. A Herald front page dated "since you last logged in". |

### 1b. The independent audit's nine questions, scored honestly

| Question (owner's audit) | Score | What the code and in-game checks show today |
|---|---|---|
| **Can I find a functioning group?** | **C** | Yes if *you* invite: `/invite <name>` within 6,000 units [V-game], duo offers by tell [V; unchecked in game], bots join and buff you [V-game]. No: a bot group never invites you [V Tells.cs:14]. Bots don't fill your LFG window [V grep]. The LFG wait didn't raise group counts in a 39-min check [V KNOWN-ISSUES]. Role-filled groups and `/lfg pbae` are phase B, not built [V plan]. Quest credit in bot groups is broken until #46 [V QUESTING]. |
| **Do the bots behave differently?** | **B-** | Six personalities × five traits change goals, camps and chat rate; measured shares: Socialite 88% group, Crafter 70% solo [V BOT-PERSONALITIES 2.2]; `/personality` checked in game [V-game]. But class and build choice are uniform random [V BOT-BUILDS], combat competence is uniform, and "Crafter" does nothing craft-related [V CRAFTING]. The player sees difference in *talk* far more than in *play* [I]. |
| **Does RvR feel believable?** | **C-** | Alarms and nemesis records seen [V-game]. Community report: 5,000-unit instant aggro, pet floods, one shared route [V BOT-BUILDS]. Perception PR #53 open [V gh]. Relic never taken by bots [V README]. Sieges played only at ~1,000 bots [V]. |
| **Can I complete the quests?** | **D (by code), unknown (in game)** | Four defects hit a solo-with-bots player directly [V QUESTING §1]. Fixes open in #45/#46, unplayed [V gh]. The three-quest A/B hasn't been run [V RELEASE-TRAIN unchecked]. Epic chains are not linked by Setup [V]. |
| **Do guilds have recognisable identities?** | **C+** | Charters (leveling/social/RvR/raiding/crafting), names over heads, alliances, recruiting [V-game BOT-GUILDS §5]. Not seen: keep claims [V]. Identity is the charter's line pool, not a history: no famous members, no feuds, no reputation [I]. Guild tiers are planned only [V backlog]. |
| **Does a raid produce memorable events?** | **B-** | Wipe and regroup, Special Forces, Leeroy (1 in 20), contested DF kills, and after-raid /faction lines [V]. Seen: Gildas live [V-game]. Missing: a raid leader voice, phases, and the player's place in it [V LIVE-EVENTS-ENGINE §2]. Memorability depends on roster ≥80 online 50s [V]. |
| **Does the world change when I'm away?** | **D** | Only while the server process runs. No offline resolution, no login summary [V grep]. Events are designed to fire only with the player online [V REPORT; LIVE-EVENTS-ENGINE §6.2]. The Herald reads the DB but reports no "since you left" [V]. |
| **Does the economy support meaningful progression?** | **D+** | Real loot, Exchange (no fee, bots prefer player listings by +650) [V CRAFTING]. Bots shop only with a near-full bag. No bot crafts. Guild banks never spend. Free 10k gold + q100 kits for Lv.50 batches [V]. A player's crafting has almost no bot demand [V]. Progression is drops and grinding, which is era-true for PvE, but there's no market life. |
| **Can the server do all this without becoming unstable?** | **B-** | Overnight soaks at 600/realm, flat server memory (~1.7 GB at 1,800 bots), handles grow ~1,700/h [V KNOWN-ISSUES]. Issue #25 stall fixed [V]. **`ai_budget_ms` is not read in production** [V LIVE-EVENTS-ENGINE §2, §5]. Phase A merged 127 unplayed upstream files [V RELEASE-TRAIN]. The risk is ahead of us, not behind [I]. |

---

## 2. The 15 biggest gaps, ranked by impact on the North Star

Effort: S = days, M = 1-3 weeks, L = a month or more at the project's pace.

| # | Gap | Why it matters to the player | Evidence | Effort |
|---|---|---|---|---|
| 1 | **The world starts empty and is built by the player with two buttons** (no 20-49 band; free-kit 50s). | The first impression of "a peak server" is a launcher chore, then a level gap. Every later system (guilds need 8 bots at 5+, raids need 80 online 50s) depends on a roster the player has to know how to build. | [V] README, KNOWN-ISSUES, `Pages.cs:866`, CRAFTING §3.2 | **M**: a "2003 server" population preset in Setup and the launcher (level curve, alts, a few twinks, honest gear); a short headless pre-age |
| 2 | **Bots can never invite the player; bot groups and raids can't take him.** | You are never wanted. The core 2003 loop ("lf healer?" → invite → run to camp) runs only in reverse. Written `lfm`/"recruiting" lines sit dormant. | [V] `Tells.cs:14`, `memory.lines:195`, TALKING-UPDATE §1, §5, "Content" | **M**: bot leader invite with a yes/no box (the guild recruit box already works this way [V-game]); then a raid invite |
| 3 | **The world doesn't move while you're away.** | Returning players find the same keeps and the same relics, and nobody missed anything. A 2003 server's defining feature was that it went on without you. | [V] no offline code; REPORT §"Dialogue and world design"; LIVE-EVENTS-ENGINE §6 | **M**: bounded, low-frequency offline resolution at server start (keeps, a raid result, bot dings and guild changes) consistent with the sim; "you missed it" gossip |
| 4 | **Chat reads as bots to a blind judge (72%), and 34% of replies ignore the speaker.** | Every bad reply to *your* line is a direct illusion break; ambient lines are forgiven, replies aren't. | [V] CHAT-EVAL | **S→M**: silence below a reply-score minimum (C2), self-knowledge, gn/stuck context fits |
| 5 | **The life lives in an anachronistic realm-wide `/faction`; towns lack /broadcast and LFG flags.** | Peak-server texture was *local*: bind-stone /broadcast spam, "LFG 24 ranger" in /say, a guild chat that was your home. The realm-wide channel is a convenience that reads as an MMO-era chat room. | [V] REPORT channel table; `AutonomousBotChatCoordinator.cs:981-990`; no LFG-flag code | **M**: C1 re-routing plus town /broadcast ads; keep /faction as a labelled option |
| 6 | **Quests are broken for a solo-with-bots player (by code).** | Kill credit, hand-ins and XP: the most "this is broken" feeling in the first hour. | [V] QUESTING §1; PRs #45/#46 open | **S** to merge after the A/B; **L** to certify 448 rows |
| 7 | **RvR perception and composition: radar aggro, pet floods, one path.** | The first battleground or frontier trip is where r/daoc players judged it. | [V] BOT-BUILDS-AND-RVR; PR #53 | **M**: #53 plus pet caps plus lanes first; playbooks later |
| 8 | **No relic raid has happened and none is a server event.** | The biggest 2003 story never happens. | [V] README "Good to know" | **M**: play the base's relic campaign; full news, alarm and Herald coverage |
| 9 | **Raids are spectator sport, gated at 80 online 50s, with no leader voice.** | The player watches the endgame happen; he isn't recruited into it, and small rosters never see it. | [V] KNOWN-ISSUES; LIVE-EVENTS-ENGINE §2 | **M**: a raid leader bot (calls plus invite), threshold scaled to roster |
| 10 | **A dead economy: no bot crafters, no market chatter, no commissions.** | Crafting players have nothing to sell to and nobody to buy from; "famous crafter" culture is absent. | [V] CRAFTING §3.2 | **M** for a visible crafter loop; **L** for the M0-M6 plan |
| 11 | **Repetition over weeks (58-60% word-for-word by week 4).** | Long-term players are exactly the "first week / returning" audience; they will notice families before the numbers say. | [V] TALKING-UPDATE §8 | **M**: lines tied to live facts and social history (C4) beat more ambient lines |
| 12 | **The social circle isn't player-centred enough:** memory is chat-only, tells are rare, no login whisper-invite. | "Alive" = being noticed and remembered (Erenshor evidence). Today memory greets but never *acts*. | [V] LIVING-SERVER "memory is chat only"; REPORT #4 | **M**: C4 social-facts log plus circle invites (needs gap 2) |
| 13 | **Herald is a launcher-opened static stats page, not living history or in-game news.** | A returning player has nowhere in the game to read "what happened". | [V] LIVING-SERVER "Camelot Herald"; backlog "static page" | **S→M**: dated front page, "since you last played", guild histories, raid write-ups |
| 14 | **Competence is uniform: no visible human mistakes, and class/build choice is uniform random.** | Bots feel like a machine: too even in play, and the class mix is wrong for the era. | [V] BOT-BUILDS "chosen evenly at random"; REPORT #6 | **M**: discipline trait plus evidence-and-tail weights (G0/G2) |
| 15 | **Most "rare by design" moments are unseen in game** (storylines, spammers, rumours, deeds, nemesis lines, bot rez of the player, clock schedule). | A feature a player never sees doesn't exist for him. Rarity tuned on a 5-hour/week test budget is untested rarity. | [V] TALKING-UPDATE "Not checked" 1-18; KNOWN-ISSUES "Not yet seen" | **S**: a "first evening" director that guarantees one of each within a player's first few sessions, then lets rarity take over |

---

## 3. Ten Easter eggs and added features that fit the era's spirit

None of these is in the corpus as far as I can tell [I]; check before building. All respect the owner's rules (real loot, earned armor).

1. **The /played confession.** A no-lifer bot's /played leaks in /faction ("47 days played... dont tell my gf") and the realm roasts him; ask him `/played` by tell and he dodges.
2. **Patch-day panic, opt-in.** A fake "patch notes" Herald page on the 1st of the month (the backlog rejected a fake *patch day* [V], so make it a switch). Bots rage at a "nerf" that changed nothing, and theorycrafters swear it did.
3. **Vault boards in the Herald.** A "Class Boards" page in the local Herald: bot threads titled "Smite clerics: are we a joke?" built from the debate corpus and signed by real bot names, with the debates you joined in game quoted.
4. **The Darkness Falls doorbell.** When DF changes hands, one bot always shows up at the entrance in /say: "is DF ours??", for the third time that day. A period running joke.
5. **Server first, the dumb kind.** Herald "firsts" for the absurd: first to die to a grey rat, first to fall off Snowdonia Fortress, first naked level 20. They come from deeds data that already exists [V `offline_player_reputation`].
6. **The famous crafter.** One named master crafter per realm whose /broadcast is known server-wide, with "99% or nothing" lines and a waiting list. Bots name-drop him ("got my chain from X lol").
7. **Ghost of the old guild.** A bot guild that disbanded (guild-split storyline [V]) leaves members with the old tag in their memory lines: "miss the old Oaken Shield days".
8. **Stealther lore.** A Midgard bot swears an infiltrator has been /stuck under the Emain bridge for three days. If the player goes, a stealthed bot is really there and waves.
9. **Midnight /who.** At 3 a.m. under the `clock` schedule, the 20% online are the weird ones: a roleplayer narrating to an empty town, two no-lifers duelling at the bind stone, a bot AFK-fishing on a dock (body language plus place).
10. **The legendary AFK.** A bot left AFK at the Camelot bind for hours; bots stack in front of him, emote at him, take bets. He returns: "wtf why is everyone dancing on me".

Added features (not eggs) a player would *feel*:
- **"Since you last played" login whisper** from your best friend bot: three true facts (gap 3).
- **Chat groups you can join** (`/cg` public group "AlbRvR" with 60 bots in it) as the period home for realm talk.
- **Server status flavour**: a launcher badge and /who count that call the population "Heavy" at 8 pm.

---

## 4. The owner's audit ideas, judged from the player's side

**New-player and returning-player guide: yes, but make it diegetic, and make it about the *server*, not the client.**
The real confusion (verified from the docs) is the concept: you must make bots, a Lv.1+Lv.50 roster has a gap, raids
need 80 online 50s, bots can't invite you, and features are hidden behind switches and rarity. A text guide fixes
that only for readers. The player-side version:
- an **advisor bot** (period /advice existed from 1.33 [V REPORT]) who answers "how does this server work?" in-game;
- a one-page Herald "Welcome to the server" with the realm's population, top guilds and how to get a group (*you
  invite; type /invite name*);
- for a returning player, the same page in "while you were away" mode.

Effort S. High value, because it removes the first-hour friction that otherwise reads as "dead" or "broken". Keep
it from becoming a tutorial wall: never pop it up uninvited.

**The world changing while the player is away: the most important of the three.** Score today: **D** [V]. From the
player's side this *is* the difference between a server and a diorama. Rules for doing it without breaking trust:
- **Bounded and consistent.** Resolve only a few majors per real day at server start: a keep flip, a raid
  outcome, some bot dings and guild changes. What the player then sees (keep owners, levels, gear) must match
  (REPORT's A-Life lesson [V]).
- **Told by people, not a log.** Two or three bots bring it up in their own words in the first 20 minutes, and the
  Herald has the dated record.
- **Loss is allowed.** Your realm can lose a keep or a relic while you're away (REPORT #8 [V]). Never take the
  *player's* items or progress.
- **Opt-out switch**, for players who want a frozen world.

This is the feature that answers the "returning player" moment, which is currently the worst row in the journey.

**The Herald as living history: yes, as the returning player's front page, not as a stats dump.** Today it lists RP
leaders, guilds, keeps, relics, firsts, Most Wanted and recent events [V]. That is a 2003 Herald *stats* page, era-true
but cold. Player-side value comes from **stories with names and dates**: "Tuesday: Order of the Oaken Shield took Caer
Benowyc; Brannoc and Aelwyn's feud ended with you on Aelwyn's side; the Avalon City raid wiped twice before the Special
Forces arrived." All of that data already exists (realm event records, storylines, bonds, deeds) [V]. Two cautions:
- it is opened from the launcher, outside the game, so it needs an **in-game hook** too (a town crier NPC or the
  login whisper);
- it must never spoil Easter eggs (RELEASE-TRAIN rule [V]).

Effort S→M, best built together with offline progression.

---

## 5. Where the current plan over-invests relative to what a player would notice

1. **Era-purity research beyond the point of player perception.** The 1.2 plan spends real effort on build-share
   provenance (EU vs US posts, PR #55), source-dating every era fact, and channel archaeology [V REPORT, #55, #57].
   Some of it pays: the channel finding changes what players see. But a player will never notice that Cleric Smite
   is 0.20 rather than 0.12. They *will* notice that a bot group never invites them. **Cap research at "changes a
   visible behaviour".**
2. **Two large frameworks before the two cheapest fixes.** BOT-CHAT-FRAMEWORK has C0-C7 and BOT-GROUP-FRAMEWORK
   has G0-G6, with personas, storylets, a pacing director and a rule-list JSON [V]. Meanwhile "silence over a weak
   reply" (CHAT-EVAL fix 1) and "bots can invite the player" are each days of work with an outsized felt effect.
   **Do those first.** The optional rule-list editor (G6) is for a handful of enthusiasts.
3. **Scripted epic fights and a drama director** (LIVE-EVENTS-ENGINE) before the player can be *in* the raid. A
   near-wipe curve the player watches from outside is a cutscene. Invite first, choreography second.
4. **Crafting M3-M6** (template-aware valuation, gear wear, an Exchange tax, price wars) [V RELEASE-TRAIN 2.0] before
   anyone visibly crafts. The economic sim and dashboards are good engineering, but the felt gap is one crafter at a
   station taking a commission.
5. **Corpus volume.** 34k lines and 393 scenes already exceed what a player reads (about 350 lines an evening [V]).
   More lines don't fix the 72% catch rate. Grounding and silence do. **Stop adding ambient lines; add *fact-bound*
   lines.**
6. **Automated test counts as progress.** 4,100 tests, while 18 talking-update items and most "rare" features are
   unseen in game [V]. The owner's audit is right: measure the nine questions above in a played session per release.
7. **Phase A's 127-file upstream merge in the same release as everything else** [V]. It is invisible to players
   except as risk. It's defensible (the bot spell engine is real value), but it should ship with only the
   RvR-perception headline, not wait for live events and guild tiers.

---

## 6. Suggested priority order for the player (my synthesis) [I]

1. Bots and bot groups invite the player (gap 2). 2. Silence over weak replies plus self-knowledge (gap 4).
3. Populate-my-server preset (gap 1). 4. Quest fixes merged after the A/B (gap 6). 5. RvR perception and pet caps
(gap 7). 6. Offline progression plus a "since you left" Herald and gossip (gaps 3, 13). 7. Period channels: /broadcast,
LFG flag, chat groups (gap 5). 8. One relic campaign played and celebrated (gap 8). Everything else after.

*Verified items cite the doc or code path; an in-game claim is marked [V-game] only where a dated in-game check
in the repo records it. This audit played nothing.*
