# North Star audit: the plan and the designs

Auditor's lens: are the roadmap and frameworks aimed at the North Star, in the right order, at the right size, and
free of constraints the project put on itself? Written 2026-11-02/03, read-only. No branch, PR or file in the repo
was changed.

> **North Star (owner):** "the most amazing DAoC experience offline with bots and a world that feels like it's a fully
> alive peak server from the era, with added features and Easter eggs." A *peak server* is busy and crowded at prime
> time. *Added features and Easter eggs* means era purity serves the experience; it does not get a veto.

**Labels.** **[V]** verified in this audit (a doc read with `git show`, code in `/home/claude/wt-12/source/server` at
`release/1.2` `50ecb30`, or `gh`). **[I]** inferred by this audit. **[E]** an estimate with no measurement behind it.
**[C]** a claim from the independent audit (the attachment) that I have *not* re-checked. A [V] next to a [C] means I
checked it.

**What I read [V]:** `plan/rvr-builds`: RELEASE-TRAIN.md, BOT-BUILDS-AND-RVR.md. `research/1.2-before-coding`:
BOT-CHAT-FRAMEWORK.md, BOT-GROUP-FRAMEWORK.md, research/1.2/REPORT.md, bias_audit.md. `eval/chat-blind-2026-11-01`:
CHAT-EVAL.md. `release/1.2`: LIVE-EVENTS-ENGINE.md, LIVE-EVENTS-1.2.md, CRAFTING-AND-ECONOMY.md, QUESTING.md,
LIVING-SERVER-BACKLOG.md, plus LIVING-SERVER.md for what is already built. The open PRs (#44–#46, #50–#57). The
`feature/rvr-perception` diff (PR #53). `living-world/trade-sim:tools/living-world-sim/persona_sim.py`. The
independent audit at `/mnt/user-data/attachments/.../6229d49a-attachment.txt`.

---

## 0. The verdict in one paragraph

The plan is honest, well researched and mostly aimed at the right target. It drifts in one consistent direction,
though: since the 2026-11-01 research round, **era purity and bot fallibility are being used as gates, when the
owner's own rule makes them tie-breakers** (WRITING-GUIDE: "Authenticity to DAoC 2001-2003 is a tie-breaker, not
the goal"). That shows up in three decisions that would make a *peak* server feel *quieter*: moving bot LFG and trade
talk off /lfg and /trade, "silence as the fallback", and a 20-persona model where most bots are near-silent. These
choices are correct for replies to the player. They are wrong for the ambient crowd. Meanwhile 1.2 has grown to
include about 25 milestones across five design docs. The chat (C0–C7) and group (G0–G6) build orders are not in the
release train at all. And the cheapest, most visible North Star items (prime-time crowding, the Herald as a living
history, server firsts, Easter eggs, emotes and /anon, newbie flow) have no milestone in any document.

---

## 1. Verdict per document

| Document | Verdict | Why |
|---|---|---|
| **RELEASE-TRAIN.md** (plan/rvr-builds) | **Keep the train, change 1.2.** | The train and the rules for each stop (play-checked, one system per soak) are right [V]. But 1.2 "the comprehensive update" now holds the base merge, builds and RvR, events M1–M2, the guild package, the historical server and the quest harness [V]. The chat and group frameworks add 15 more steps that the train doesn't list [V]. That is the "months of silence" the train was created to avoid (its own reasoning, 2026-10-30) [V]. Re-scope it as in §4. |
| **BOT-BUILDS-AND-RVR.md** | **Keep, change two lines.** | It answers a real, code-verified complaint (5,000-unit acquisition, `AutonomousFrontierThreatPolicy`) [V via doc]. Change "groups fill a 2003-style eight" to the group framework's mix, and "never break their own mez" to the discipline trait. Add a **density cap** to the perception rule (see §2, item 9). |
| **BOT-CHAT-FRAMEWORK.md** | **Change.** | The pipeline (rules with specificity, arbitration, facts with expiry, /lswhy) is excellent and should be built [I]. Change §2: era channels become the *mechanism* and volume is kept (§3.1). Change §3 "Fallback: SILENCE" so it applies to replies only. Cut §3.9 storylets as a new system, because `AutonomousBotStorylines` (427 lines, `offline_bot_storylines`) already does this [V]: extend that instead. C5's 20 personas move to "later". |
| **BOT-GROUP-FRAMEWORK.md** | **Keep, cut G6.** | Its order is right: role brains, then stances, then presets, then builds. "Dependable in your own group, uneven in the world" is the best resolution of fallible versus amazing anywhere in the plan [I]. **Cut G6**, the JSON rule list: no North Star value for one player at five hours a week. Watch §5 "catches up out of the player's sight": if that means teleporting, it conflicts with the stablemaster-routes rule [I]. |
| **research/1.2/REPORT.md** | **Keep as evidence, not as a plan.** | The channel table, the competence-bias point and the "world goes on without you" point are its best findings [V]. Its ranked change list, though, puts era channels at #1 by "value for cost" with no North Star weighting. For a peak-server feel, #1 should be the anti-uniformity brakes (#5) [I]. |
| **bias_audit.md** | **Keep.** | Its corrections are right. Its one blind spot: it argues against "more chat = more alive" without asking what *peak* volume was. A crowded 2003 capital at prime time was loud. Silence-for-replies and noise-in-the-crowd are not in tension [I]. |
| **CHAT-EVAL.md** (PR #56) | **Keep and extend** (§6). | It is the most important measurement in the project. The independent audit agrees [C]. Its fix 1 ("silence over a weak reply") is right for replies. Its fix 2 ("answer the answerable") conflicts with the REPORT's error budget. Resolve that by experience tier, as the chat framework §3.6 already does [V]. |
| **LIVE-EVENTS-ENGINE.md** | **Keep, change three things.** | "Script the stage, not the crowd" is the right thesis [V]. Change: (1) state durability (it says state is in memory, which contradicts LIVE-EVENTS-1.2 rule 8, see §2); (2) the thumb on the scale, so the director may delay help but routs are allowed; (3) "nothing fires offline / notices dropped when no player is online" becomes a setting. |
| **LIVE-EVENTS-1.2.md** | **Change (stale).** | Its milestones (invasion M1, festivals M3, war effort M4) were overtaken by train decision 2 and the engine doc's order [V]. Its rule 2 ("nothing resolves to a loss offline") and rule 5 ("you stay decisive") over-centre the player (bias audit) [V]. Update it or mark it superseded. Its open question 4 (Easter eggs) is the only Easter-egg plan in the repo [V]. Answer it: yes (§9). |
| **CRAFTING-AND-ECONOMY.md** | **Keep the analysis. Change the order: ledger first, crafting later.** | It is the most thorough doc. It challenges its own assumptions, and the two-ladder resolution is sound [I]. But it builds crafters (M2) before measuring the economy, and its simulator assumes sinks the server doesn't have (§7). Its /trade routing (M2 "adverts go to /trade") contradicts the chat framework [V]. The Exchange tax touches the owner's Realm Exchange rule. |
| **QUESTING.md** | **Keep, raise priority.** | Broken quests hurt the experience more than any missing AI feature (the independent audit agrees [C]). Defects 1–4 are code-verified [V via doc], and PR #46 fixes several. The harness is the lever. "Send it upstream" is stale because upstream has stopped [V]. |
| **LIVING-SERVER-BACKLOG.md** | **Keep as a record.** Lift three items into 1.2. | The undone parts of its shortlist are cheap North Star wins: server first for "first kill of a big named" (not built [V], LIVING-SERVER.md:89), schedules defaulting to `clock`, and Herald growth. Its chat-volume warning (per-channel budget at about 1,000 per realm) is still unaddressed by any measured target. |

---

## 2. Contradictions

Between documents [V unless marked]:

1. **/lfg and /trade, both ways in 1.2.** RELEASE-TRAIN phase A ships the new-client script "`/lfg` and `/trade` as
   realm channels". BOT-CHAT-FRAMEWORK C1 moves bot talk off those channels ("no line in a channel that didn't exist").
   CRAFTING M2 sends ads to /trade. BOT-BUILDS adds `/lfg pbae|healer|cc|tank` as player commands. GROUP-FRAMEWORK G4
   routes those requests "through the period channels". Four documents, three positions.
2. **/faction is treated as exempt.** The era argument removes /lfg and /trade. But the 1.1 news rule, the events
   Chorus and the eval all use /faction, an OpenDAoC/Atlas realm channel that is not in the Prima command table
   either (REPORT's own table: `/em, /g, /s, /send, /whisper, /yell, /gu, /oc, /as, /b, /cg, /chat`). If purity
   decided channels, /faction would go too. It shouldn't, which shows the rule is being applied selectively [I].
3. **Event state.** ENGINE §3: "event state stays in memory… a restart… records it Interrupted". LIVE-EVENTS-1.2 rule
   8 and its risk table: "Event state lives in the database and survives restarts".
4. **Event order.** LIVE-EVENTS-1.2 milestones: invasion first. ENGINE §7 and train decision 2: dragon fight first,
   invasion third (now 1.3). The festival and war-effort order differs between the two as well.
5. **Who the world centres on.** LIVE-EVENTS-1.2 rule 2 (start only while you're logged in, never lose offline), ENGINE
   §6 ("no beat fires while the player is offline"), and REPORT change #8 / bias audit #10 (majors resolve offline and
   are reported at login). Nothing has decided between them.
6. **Outcomes.** ENGINE §4: the director "lets the first attempt wipe or stall more often than the numbers would", with
   a 60–75% win band. REPORT: "vary outcomes, including honest routs". The band rests on one Hartsman quote (bias audit).
7. **Group competence.** BOT-BUILDS §3: "fill a 2003-style eight"; healers "never break their own mez". GROUP-FRAMEWORK
   §1/§3: a mix of compositions and a discipline trait.
8. **Accuracy.** CHAT-EVAL fix 2: the knowledge lookup answers place questions. REPORT/bias audit: an error budget, so
   "dunno" scores as human. The chat framework §3.6 reconciles these by tier, but CHAT-EVAL's rubric hasn't been changed.
9. **Storylets and storylines.** BOT-CHAT-FRAMEWORK §3.9/C7 proposes storylets as new. `AutonomousBotStorylines.cs`
   already plays multi-episode arcs over days with protagonists and player sides.
10. **The train omits the frameworks.** RELEASE-TRAIN 1.2 phases A–D contain none of C0–C7 or G0–G6, and the frameworks
    don't say which phase they belong to.
11. **The perception math.** PR #53: 3% per scan near 3,500 units and 50% just outside 1,000, scanning about once a
    second, with spotters [V in the diff]. The independent audit's point [C, V] holds, and **it understates the
    problem**: over 10 s, 20 independent bots at 3% give 1 − 0.97^200 ≈ 99.8%. At the density of a peak-hour
    battleground, distance still works as a radar unless the number of noticers per target is capped [I].
12. **Crafter share.** The owner's 7% versus the code's 12% (`TypeSharePercent`) [V via doc]. The crafting doc handles
    this (new bots only), but no other doc knows about it.

Against the owner's rules:

| Rule | Where a plan strains it | Finding |
|---|---|---|
| Gamebots start without armor and earn it | The launcher's level-50 batch kit (q100 plus 10,000 g) [V via CRAFTING §3.2]. The guild-tier "hand-me-downs". Any offline progression that grants gear. | Make the kit a test-world setting (crafting decision 10). Hand-me-downs only to bots that already earned that tier. Offline catch-up may move world state and XP, **never gear**. |
| Realm Exchange | CRAFTING's 5% Exchange tax. CHAT-EVAL fix 12 (mention the Exchange only when asked: fine). | A tax changes the owner's feature. The independent audit's OSRS citation [C] supports not adding a tax before the ledger shows a need. **Defer.** |
| Selling only at natural task breaks | CRAFTING M3 "gear check at every town visit". | Buying, not selling, so it can stand, but only if a "town visit" is itself a task break [I]. State it. |
| Stablemaster routes | GROUP-FRAMEWORK §5 "catch up out of sight". Event occupation. | Catching up must use routes, not teleports [I]. LIVE-EVENTS rule 4 protects stablemasters [V]. |
| DF open to solo bots | Guild tiers ("organized RvR… safer way into the deep frontier"). The Realm Pulse "seeking DF access" idea [C]. | No plan closes DF [V by search]. Keep it that way: DF access is by keeps (era), and solo bots still enter. |
| Real loot | Event tokens (no-trade, capped) [V]. | Fine. |
| "Memory never refuses you" (owner design rule) | Guild-tier gating, the chat framework's insult consequence, the crafter who "doesn't deal". | Each is flagged as an owner decision in its doc [V]. Decide them once, together. |

---

## 3. Where the plan serves the North Star, and where it drifts

**Serves it well [I]:** the train and played gates; perception plus pet caps, which answer the loudest real
complaint; the drama-curve thesis; reason strings and /lswhy; "dependable in your group, uneven in the world";
anti-uniformity brakes (reactors per trigger, speaking locks); reaching out first; the social circle; remarkables
replacing lore musings; the quest harness; the crafting doc refusing a shortcut the numbers won't give.

**Drifts:**

### 3.1 /lfg and /trade → /say and /broadcast makes a peak server quieter [I, with V parts]

- `/b` defaults to **area** scope (`broadcast_type` 1, `ServerProperties.cs:500`) [V]. A /say or /b advert reaches only
  the people standing near it. Today an /lfg line reaches the realm. Moving the talk keeps the line count but divides
  its audience by the realm's spread. The player hears it only in towns, and nothing in the field, at a portal keep or
  in a dungeon.
- The LFG window flag is invisible unless the player opens the window.
- A peak 2003 server *was* noisy. The noise was spread across /b in the capital, public chat groups and guild chat.
  The plan keeps the era *forms* and loses their *reach*.

**Resolution: era mechanisms first, with the reach kept.**
1. **Public chat groups** (1.60+, up to 200; a primary source per the REPORT [V via doc]) are the era's realm-wide
   LFG and trade channels. Bots create and fill public groups such as "alb lfg 20-35", "alb trade" and "mid rvr", and
   advertise them in /b ("/cg join lfg if u need grp"). The player joins with `/cg`. **Unverified:** whether named
   public LFG or trade chat groups were common practice on US servers. The mechanism existed. Its use is [E].
2. **/b in towns, at bind stones and at portal keeps** for the WTS macros and "lfg 24 ranger".
3. **The LFG window flag** for every bot that's looking.
4. **Keep /lfg and /trade as an owner channel [O], default ON**, labelled "convenience channel, not in the 2003
   game", carrying a *budgeted digest* (few lines, real requests only). It can be turned off for an era-purist mode.
   No working busy-feel feature is removed without a replacement measured to be at least as busy (rule R4, §10).

### 3.2 "Silence as the fallback" versus a crowded server's noise [I]

Right for **replies to the player**: the eval shows a wrong reply costs more than none [V]. Wrong as a world
default. Split the pipeline into two budgets:
- **Reply budget:** silence when unsure, as designed.
- **Ambient budget:** a population-level loudness *floor and ceiling* by hour and place. Prime-time capital /b,
  public LFG groups, /faction news chatter, emotes, horses and crowds at the bind stone.

Neither target exists anywhere today. The bias audit asked for a "quiet target" only. Peak bands are [E] until
calibrated against a real log (no period lines-per-hour figure exists, bias audit gap).

### 3.3 "Fallible bots" versus "amazing bots" [I]

Resolved correctly by GROUP-FRAMEWORK §1: the player's own group defaults to dependable, and unevenness lives in the
world. Extend that to chat (the bots who know you are accurate, strangers have the error budget) and to RvR (the enemy
warband is beatable *and* sometimes brilliant). "Amazing" means the server as a whole feels amazing, not each bot.

### 3.4 Era purity as a gate [V for the wording]

C1's success measure is "no line in a channel that didn't exist", a purity gate. The owner's stated order puts
aliveness first.

---

## 4. Proposed 1.2 scope, re-ordered for the North Star (must / should / later)

Ordered by visible improvement per hour of work. Hours are [E].

**Must (1.2 ships when these are played):**
1. **Phase A soak** of the base merge (done on `release/1.2` [V]); the world scripts as Setup steps; PR #51.
2. **Quest defects fixed (PR #46, #45) plus the three-quest A/B**, and the quest harness started. A broken quest
   costs more than any AI feature.
3. **PR #53 perception plus a density cap** (at most k noticers per target per scan window, the rest learn through the
   spotter call with delay), pet caps per warband, and provisional build weights. This is the community's complaint.
4. **Prime time feels like prime time:** `bot_schedule_mode` defaults to `clock` (it exists, with a 2002 North
   American evening curve, `AutonomousBotSchedule.cs:84-96` [V], but defaults to `off` [V]). Capital, bind-stone and
   portal-keep **crowd density targets** at 8 pm. RvR population swings by hour. Low cost, high visibility.
5. **Chat C0 and C2** (/lswhy, reason strings, reply minimum plus silence for replies, reactors-per-trigger cap,
   speaking lock) **with the two-budget split**, and the channel resolution of §3.1 (chat groups, /b, LFG flag, the
   labelled /lfg and /trade digest).
6. **The Herald as a living history**, fed by RealmEventRecords and server firsts, with the unbuilt "first kill of a
   big named" first. A cheap, very visible "this server has a past".
7. **Live events M1** (the engine on the dragon fight) with the timeline report and **real tick-cost measurement**.
   `ai_budget_ms` is unread in production (ENGINE §5 [V via doc]; independent audit [C]).
8. **The economy ledger (instrumentation only, §7).** No behaviour change. Calibrates 1.3.

**Should (in 1.2 if they fit, otherwise 1.2.x):**
- Chat C4 (social-facts log plus reaching out) built on the existing tables (§8).
- Group G1–G2 (stances, puller and main assist, the discipline trait). The PBAE preset.
- Director M2 and the roaming threat.
- Five Easter eggs, and remarkables (C6).
- Bots using /anon, AFK and LD behaviour, emotes at crowds, trash talk in RvR (/yell after a kill, bounded).
- Newbie flow: newbie-guild chat (1.62), an /advice responder tier, starter-zone crowding.

**Later:**
- Chat C3 (facts with expiry) only after C2's blind round.
- C5 (20 personas).
- C7 (storylets): extend storylines instead.
- G5 warband lanes (after the perception soak).
- **G6 rule list: cut.**
- The guild social package, the historical server (1.3).
- The invasion (1.3), crafting M0–M2 (1.3, after the ledger has run for weeks), the war effort (2.0).
- Offline catch-up (1.3, §11).

Over-engineered: G6 rule lists; storylets as a new engine; 20 personas before C2 is measured; the crafting plan's 16
settings rows; an "events engine plus director plus pulse" stack before one event has been played [I].

Under-scoped: scale. Every chat, perception and event cap needs a number at 600–1,000 bots per realm. Only events have
one (ENGINE §5) [V]. Perception, chat and the ledger have none.

---

## 5. Challenge #4: historical evidence, audited

### 5.1 Every 1.65 assumption in the 1.2 builds, chat, RvR and crafting plans, classified

Classes: **P** primary (dated, official or contemporary game text), **F** forum sample, **G** guide or secondary
period text, **M** modern doc or memory, **E** estimate or inference, **O** owner choice. US/EU means the server
region. Source = the doc that makes the claim [V that the doc says it].

| # | Assumption | Used in | Class | Notes |
|---|---|---|---|---|
| B1 | Class shares per realm in RvR | `bot-builds.json` (PR #55) | F + E (EU) | Excalibur/Prydwen forum activity; US census only "fragments" (bias audit §1) |
| B2 | Build weights per class | PR #55 | E over F | Some invert their own counts (Cleric Smite: 0 posts, weight 0.20) |
| B3 | The 8-man RvR composition | BOT-BUILDS §3, GROUP §4 | F (aspirational threads) | "Perfect group" threads; "how many balanced FGs do you ever see" |
| B4 | PBAE groups were a common 1.65 RvR form | BOT-BUILDS | F + M | Plausible; not quantified |
| B5 | Pet classes common, but capped | BOT-BUILDS | E + O | The cap is a design choice; label it [O] |
| B6 | Respec on player rules (stones, level gates) | BOT-BUILDS §4 | P (the game's rules) + O (applied to bots) | |
| R1 | 2003 detection distance and reaction (0.5–2 s; 1,000/3,500 units) | PR #53 | E | No source for how far 2003 players noticed others |
| R2 | "inc 3 north" call syntax | BOT-BUILDS, CHAT §3 | E | REPORT: period RvR call syntax unsourced |
| R3 | Warbands in lanes plus roaming groups | BOT-BUILDS | E / M | "One shared route" is itself inferred, not measured |
| R4 | DF open to the realm with the most keeps | LIVE-EVENTS | P | Era rule |
| R5 | Battlegroup chat from 1.66 | CHAT §2 | P | Patch notes via the fandom API |
| R6 | "2003-normal" RvR difficulty | BOT-BUILDS | O | A name, not a measurement |
| C1 | No /lfg or /trade channel by 1.65 | CHAT §2, REPORT | P (negative claim) | See 5.2 |
| C2 | LFG window existed by 1.59 | CHAT §2 | P | First appearance unknown |
| C3 | /b is area-scoped ("all in your town or city") | REPORT | P (Prima glossary; 1.53 note) | Matches server default 1 [V] |
| C4 | Chat groups: 50 at 1.50, 200 and public from 1.60 | CHAT §2 | P | Two dated notes agree |
| C5 | Newbie guilds 1.62, four per realm | CHAT §2 | P | |
| C6 | /advice advisors needed 15 h | CHAT §2 | G (Prima 2002) | |
| C7 | Etiquette: shared camps, no buff begging, KS | CHAT §3.6, GROUP §3 | G (Prima 2002) | Guide norms, not observed behaviour |
| C8 | Lowercase, typo-heavy style; realm stereotypes | WRITING-GUIDE, research | F (one EU thread, Sep 2002) | |
| C9 | 77/23 social/task chat | research | E (another game, an FPS) | Not a target |
| C10 | EQ comparisons were constant | REPORT | G + F | The guide bans them: [O] conflict |
| C11 | No profanity; xX and number names | WRITING-GUIDE, BOT-NAMES | O | Label as deviations |
| C12 | Buffbots by late 2002 | REPORT | F (EU) | How common: unknown |
| C13 | /faction realm channel | 1.1 news rule, events Chorus | **O, unlabelled** | Not in the Prima table [V via REPORT]; never classified |
| C14 | Talkativeness skew (most near-silent) | CHAT §1, §3.5 | E (SWG, an FPS study) | No DAoC figure |
| K1 | Join an order at a Master; Basic Crafting is 1.76b | CRAFTING §2 | P (test notes via an archive) | |
| K2 | Secondaries at 75% / 40% | CRAFTING §2 | F (Allakhazam comment) | |
| K3 | 1.63 recipe flattening | CRAFTING §2 | P | |
| K4 | Stations | CRAFTING §2 | M (modern page) | "unchanged since classic" is itself a claim |
| K5 | Vendor buyback 50% / 65% | CRAFTING §2 | F (2002 comments) | |
| K6 | Overcharge outcomes | CRAFTING §2 | F (anecdote) | The server formula decides |
| K7 | Quality 94–99, masterpiece at any skill | CRAFTING §3.1 | **M (memory)** | The doc says "not verified" |
| K8 | An explosion destroyed the item | CRAFTING §3.1 | M | |
| K9 | "Every serious 50 wore an SC template" | CRAFTING §1 | F + M (min-maxer) | Use a share, not "every" |
| K10 | Siegecraft full skill only from 1.90 | CRAFTING §2 | M (modern official) | Independent audit agrees [C] |
| K11 | Consignment with Foundations (June 2003) | CRAFTING §2 | P-ish | Housing present: unchecked |
| K12 | 7% crafters, doubled commissions, Atlas drop rate | CRAFTING | O | Atlas drops are post-era: label [O] |

### 5.2 How strong is "no /lfg or /trade channel at 1.65"?

What the evidence actually is [V that the REPORT cites these; not re-fetched here]:
- **Prima Revised & Expanded (2002), Game Talk table.** A *list of commands*. Absence from a list is evidence of
  absence for that edition's date (2002), not for October 2003.
- **1.59, 1.60, 1.65 notes on the LFG *window*.** Positive evidence for a window. They say nothing either way about a
  channel. The independent audit makes the same point [C, and I agree].
- **1.62 chat tabs: "send as Say, Broadcast, Group, Guild, Alliance Chat, Officer Chat, or Chat Group".** The
  strongest single item: an official enumeration from mid-2003 of the send types the client had. If /lfg and /trade
  channels had existed, this is where they would appear.
- **1.64 /rpfilter example "/s LFG …".** Consistent with LFG typed in /say. Not proof.
- **1.66 and 1.67 add battlegroup chat and a new LFG window, still no channel.** An argument from silence.
- All the patch notes were read through the **fandom wiki's MediaWiki API**: a transcription of the Herald, not the
  Herald itself.

**Assessment [I]:** a negative claim, moderately strong. Two independent primary kinds agree: a 2002 guide's command
list, and a mid-2003 official enumeration of chat send types. The patch-note silence across 1.59–1.67 adds weight
(new channels were the kind of thing patch notes announced). It does **not** rule out:
- player-run *public chat groups* used as de facto LFG and trade channels (the mechanism existed from 1.60);
- server-specific practice.

It is strong enough to say "no built-in LFG or trade channel". It is **not** strong enough to say "realm-wide LFG talk
didn't exist". That supports the §3.1 resolution: use chat groups, and keep the owner channel labelled.

To make it strong:
1. A dated in-game screenshot or chat log from 2003 showing the chat tab options.
2. The archived Herald 1.62 page itself (Wayback), rather than the wiki copy.
3. The 1.0 manual's chat chapter.
4. The patch that later *added* /lfg or /trade: its date bounds the claim from the other side.

### 5.3 The source catalog and provenance format (proposed)

- `docs/research/era/sources.json`: one row per source.
- `docs/research/era/facts.json`: one row per claim.
- Data files (`bot-builds.json`, channel tables, crafting rules) reference fact ids.
- A CI lint fails when an era-tagged data row has no fact id, or cites a fact whose `confidence` is `lead`.

```json
{ "source": { "id": "prima-2002-re", "title": "DAoC Revised & Expanded Prima Official eGuide",
    "kind": "guide",            // official-patch | official-doc | guide | census | forum | usenet | chatlog |
                                // retrospective | modern-doc | code | inference | owner
    "date": "2002", "region": "US", "url": "...", "archive_url": "...", "accessed": "2026-11-01",
    "access_path": "archive.org OCR", "transcription": false } }
{ "fact": { "id": "chan.no-lfg-channel", "claim": "No built-in /lfg or /trade chat channel",
    "valid_from_patch": "1.0", "valid_to_patch": "1.65", "polarity": "negative",
    "sources": ["prima-2002-re", "patch-1.62", "patch-1.66"], "independent_kinds": 2,
    "n_observations": null, "population": "n/a",   // typical | optimized | unknown | n/a
    "confidence": "medium",      // high | medium | low | lead
    "region": "US", "used_by": ["BOT-CHAT-FRAMEWORK.md §2", "chat routing"],
    "owner_override": "convenience /lfg digest kept, labelled [O]", "checked_by": "...", "checked": "2026-11-03" } }
```

The build data should carry the independent audit's four distributions [C, agreed]: population prevalence,
competitive-RvR prevalence, levelling/PvE, and a deliberate gimp tail. Each gets its own weights and fact ids.

---

## 6. Challenge #5: PR #56 turned into a benchmark that adding lines can't game

Keep PR #56's blind round as tier 0. Add the following.

| Tier | What it measures | How | 1.2 target |
|---|---|---|---|
| **T1 Grounded state** | Does the bot know itself and the world: name, class and spec, level, zone, current target and activity, group, keep owners, whether a raid is on, what it promised, what it remembers about the player | Scenario fixtures on the real reply path with real state objects (the `UT_ChatSimulation` pattern plus world stubs). The answer is checked **against the state value**, not graded for style | ≥95% correct *or* an honest "dunno" for the bot's tier; **0 hallucinated places or facts**; 0 contradictions of state |
| **T2 Multi-turn** | Coherence over 5–10 turns: follow-ups ("where?", "lol no", "nvm"), memory carry-over, no self-contradiction, no repeated line | 40 sealed scripted sessions, written by a different model family *and* by humans; auto-checks for contradiction and repetition, plus human coherence ratings | 0 self-contradictions; ≥75% of turns rated coherent; repeated line within a session < 2% |
| **T3 Human panel** | Human-likeness, appropriateness, era, distinctness | DAoC veterans (r/daoc, the owner), shuffled items mixed with a real-period **anchor set** (300–500 dated lines) and calibration items; raters weighted by calibration accuracy; "felt like 2003" rated apart from "fun now" | Detection of bot short lines not distinguishable from raters' false-positive rate on real lines (d′ < 0.5, n ≥ 10 raters) |
| **T4 Population soak** | The crowd: loudness, uniformity | At 600 per realm: lines per minute per channel by hour against **floor and ceiling bands**; reactors per trigger; the "How Many" probe (one injected line: responders ≤ 3); cross-bot duplicate rate; per-bot style variance | Bands met at prime time and the dead hour; probe ≤ 3; duplicates < 1% per hour |
| **T5 Field** | Real play | An in-game `/flag` command captures the line, its context and its /lswhy record; flags become the next sealed corpus | Flagged-bad lines per hour of owner play falling release over release |

**Anti-gaming rules:**
- Scores are normalised by opportunities, not line counts.
- A **line-injection test** adds 200 generic lines to the corpus and re-runs T0–T2: the score must not rise.
- Sealed sets live outside the repo.
- Own-set read rates are regression tests only.
- Record the model family for writer, judges and baseline (bias audit #2).
- Change the rubric first so a "dunno" by tier can score human (fixes contradiction 8).

---

## 7. Challenge #7: the economy as it is in code

### 7.1 Coin faucets, sinks and transfers in `wt-12/source/server/GameServer` [V]

All bot purses go through `AutonomousBotEconomy.TrySpend/AddMoney` (`AutonomousBotEconomy.cs:75-123`).

| Flow | Kind | Where |
|---|---|---|
| Monster coin picked up by a bot (net of guild dues) | Faucet | `GameBot.TryAutoPickUpMoney` (`GameBot.cs:1701-1716`) |
| Group coin split among bots | Faucet | `Group.cs:585-606`; player share `Group.cs:668` |
| Raid coin split (bots plus a player share) | Faucet | `RealmRaidLootOwner.TryAutoPickUpMoney` (`:67-95`) |
| Vendor sale of loot | Faucet (item destroyed) | `AutonomousBotEconomy.TrySellToVendor` (`:606-640`); players `GameMerchant.cs:296`, `GameGuardMerchant.cs:346` |
| Quest, bounty, task and mission rewards (players only) | Faucet | `DataQuest.cs:1729…3251`, `RewardQuest.cs:240`, `BountyRewardService.cs:90`, `AbstractTask.cs:366`, `AbstractMission.cs:192`, about 90 scripted quests (Atlas dailies, battlegrounds) |
| Launcher level-50 batch: q100 kit plus 10,000 g each | Faucet (coin and items) | `MainForm.cs:2224-2257` [V via CRAFTING §3.2] |
| GM `/player money` | Faucet | `commands/gmcommands/Player.cs:645-698` |
| Vendor upgrade purchase (an item created from a template) | Sink (item faucet) | `AutonomousBotEconomy.cs:690-720` |
| Stable tickets (raid travel, return trips) | Sink | `AutonomousWorldBotController.cs:4157, 4333`; `GameBot.cs:1395` |
| Frontier medallions | Sink | `AutonomousWorldBotController.FrontierTransport.cs:109` |
| Siege supplies; siege repair kits | Sink | `AutonomousWorldBotController.Siege.cs:444`; `BotSiegeRuntime.cs:81` |
| **Guild dues into guild banks that nothing spends** | **De facto sink (coin locked up)** | `AutonomousBotGuildEconomy.DepositDues` (`:93-99`); the only guild spend is *merit points* for a buff (`TryBuyBuff :130-145`), not bank coin |
| Exchange: bot buys a listing (bot → seller, bot or player) | Transfer | `AutonomousBotEconomy.cs:960-1010`, `RealmExchangeSaleLedger.CommitSale` |
| Exchange: player buys a bot's listing (player → bot) | Transfer | `RealmExchangeBroker.cs:358-390, 448` |
| Unsold bot listing deleted after 24 h | Item sink | `RealmExchangeExpiry.cs:62` |
| Beggars: player → bot, bot → bot gifts | Transfer | `AutonomousBeggars.cs:513-517, 700` |
| Player trade window | Transfer | `PlayerTradeWindow.cs:433-695` |
| Player sinks: merchants, the smith, stables, training and respec | Sink | stock OpenDAoC (not enumerated here) |
| **Absent for bots:** gear wear and repair, potions, poisons (free, `BotPoisonSupply`), the Exchange fee (none) | — | [V via CRAFTING §3.1–3.2] |
| **No economy logging:** no `ECON_*` line or money-supply record exists | — | grep found none [V] |

### 7.2 What the simulator assumed (`persona_sim.py`, `living-world/trade-sim`) [V]

- `KILLS_PER_HOUR = 40` ("ASSUMPTION … not measured").
- `UPKEEP = 0.05`, `REPAIR_SHARE = 0.06`, `CONSUMABLE_SHARE = 0.04` for bots (the PLAN case uses 0.02 / 0.01).
- `EXCHANGE_TAX = 0.05`; fixed Exchange prices with no supply or demand; `SLOTS = 7`.
- `ROG_SOLO = 0.24`; `COMMISSIONS_PER_HOUR = 30` at 3× materials.

**The mismatch [I]:** the simulator charges bots repairs, consumables and a tax that **don't exist in the server**, and
doesn't model guild-bank lock-up. Its "gold roughly doubled in 24 weeks" is therefore probably *optimistic* about
inflation in today's code. It also can't see the level-50 batch faucet.

**Agreed with the independent audit [C]:** do not add a tax or a sink before the ledger shows one is needed. I would
go further: the guild bank already acts as the largest unmeasured sink, and giving banks something to spend on (the
crafting plan's facet P) would *add* inflation [I].

### 7.3 The ledger to build first (in 1.2, no behaviour change)

1. Add a `reason` argument to `TrySpend` and `AddMoney` and to the player `AddMoney`/`RemoveMoney` hot paths (about
   20 call sites above).
2. Keep in-memory counters per realm × reason × level band. Flush hourly to an `offline_economy_ledger` row and log
   `ECON_FLOW` / `ECON_SUPPLY` lines for the timeline report.
3. The hourly snapshot: total coin in bot purses, guild banks and player purses; p50/p90/p99 wealth by band and by
   personality; item creations by source (loot, vendor, kit, Exchange relist) and destructions (vendor, expiry).
4. Register it in `progress-policy.json`.
5. Two weeks of overnight logs, then rerun `persona_sim.py` with measured kill rates and flows before crafting M0.

The cohort questions in the independent audit (new player, hunter, crafter, farmer, guild member, alts) become
queries over this ledger [C, agreed].

---

## 8. Challenge #10: persistent society

### 8.1 What exists [V]

| System | Store | What it remembers |
|---|---|---|
| Bot memory of you | `offline_bot_memory` (`AutonomousLivingServerStore.cs:19`) | grouped with, rezzed by, insulted by; mood; chat only, "never refuses you" |
| Nemesis | `offline_bot_nemesis` (`:38`) | enemy bots who killed you or were killed by you |
| Deeds and reputation | `offline_player_reputation` (`AutonomousPlayerDeeds.cs:18`) | named kills, counts, nickname (players only) |
| Bonds (bot ↔ bot) | `offline_bot_bonds` (`AutonomousBotBonds.cs`, 349 lines) | tasks together, clean runs, no-shows; at most 40 per bot |
| Storylines | `offline_bot_storylines` (427 lines) | two-protagonist arcs over days; the side the player took |
| Guilds, alliances, keeps | `offline_bot_guilds`, `offline_bot_guild_members`, `offline_world_bot_guild` | membership, charter, dues, merit |
| Server firsts | `offline_server_firsts` | first to 50, class firsts, realm ranks |
| Realm events | a separate `realm-event-records.sqlite3` (`RealmEventRecordStore.cs:27`) | raids and sieges: kind, realm, phase, outcome, assigned and present |
| Rage quits; lines and scenes heard | `offline_bot_ragequits`, `offline_bot_lines_heard`, `offline_bot_scenes_heard` | anti-repetition, return timing |
| Rumours, news | in memory (`AutonomousRumours`, `AutonomousChatNews`) | voiced once per realm |

All of these except the event store are in `progress-policy.json` `OptionalProgressTables` [V].

### 8.2 What is missing [I]

There is no **shared world-history model**. Each system remembers its own slice, with its own keys, and none of them
point at the others:
- a raid record doesn't know which bonds it formed;
- a guild doesn't remember its campaigns;
- a nemesis doesn't link to the siege it happened in;
- the Herald reads three separate stores;
- the events database is a different file.

Nothing lets a bot say "that wipe at the barrows" from one fact (the chat framework's §3.3 social-facts log
is the proposal for exactly this, unbuilt [V]).

### 8.3 The smallest design

1. **`offline_world_history`**, append-only:
   `id, utc, kind, realm, region, place, outcome, salience, source_system, ref_id`, plus
   `offline_world_history_actor (history_id, actor_id, actor_kind[player|bot|guild], role)`.
   - **Writers:** raids and sieges (on Finish), firsts, deeds, storyline episodes, nemesis kills, guild formation and
     splits, keep changes.
   - Write only what is consequential, capped per hour. Salience decays; low-salience rows are pruned.
2. **The relation edges** stay where they are (memory, bonds, nemesis) and gain a `last_history_id`, so shared history
   can be named. This is the chat framework's social-facts log, built *on* these tables rather than beside them.
3. **Readers:** chat (shared history, gossip at login), the Herald (narrative), the director and Realm Pulse (a
   window of recent outcomes), and guild recruitment pressure.
4. It must be deterministic, local and explainable (/lswhy shows the history id). Register it in the progress policy.

Placement: **1.2 phase D, ahead of the guild package**, because the guild package's standing and reputation should
read from it.

---

## 9. What is missing from the plan entirely (for a "fully alive peak server")

Status: [V] = checked that it exists or doesn't; others [I].

| Item | Status | Where it should live |
|---|---|---|
| Population by time of day | Built (`clock` mode), **default off** [V] | Default `clock`; add a roster-size recommendation so prime time is *crowded* (at 300 per realm, a 96% peak is 288) |
| Crowd density targets (capital, bind, portal keep, Emain at prime time) | Missing [V by search] | T4 bands; the soak report |
| Server-wide events | Partly (raids, sieges, relic campaigns, holidays) | Events director |
| Rumours | Built (true rumours) [V] | Extend with *false* rumours by experience tier (the error budget) |
| Server firsts | Built, minus "first big named kill" [V] | 1.2 must |
| The Herald | Built (static page) [V] | Living history (§11) |
| Notable players, the realm's famous names | Partly (deeds, Most Wanted) [V] | Herald plus chat ("that's Kaelthas, top RP on alb") |
| Drama | Storylines, debates, rage quits [V] | Storylines, fed by history |
| Economy pressure (prices that move) | Missing [V] | After the ledger |
| Newbie flow (starter zones busy, newbie guilds, /advice) | Missing | 1.2 should |
| Alts | Missing (no roster of alts per bot player) [V via CRAFTING §14] | 1.3 with crafting |
| AFK and LD behaviour | Built (`bot_living_breaks`, off by default) [V] | Default on |
| Emotes | Built for idle bots [V]; no crowd or event emotes | Should |
| /anon | **Missing** (no bot uses it) [V] | Should: a share of bots /anon, era-authentic, cheap |
| Trash talk across realms, /yell after a kill | Partly (nemesis lines) | Should, bounded |
| RvR population swings (prime-time zergs, overnight PvE) | Partly (clock mode is global, not per activity) | Should |
| Easter eggs | **No plan** (one open question) [V] | §9.1 |

### 9.1 Easter eggs and added features

There is no plan, only LIVE-EVENTS-1.2 open question 4 and the release-notes rule "never spoil the Easter eggs" [V].
Proposal:
- A sealed data file of eggs (location, trigger, the bot hint lines), kept out of the release notes and docs.
- Delivered through the existing rumour, chat and Herald channels, and labelled [O].
- Five for 1.2. Candidates [E]:
  - Avalon-myth hints that pay off in Shrouded Isles;
  - a named bot who only appears at 3 a.m.;
  - a Leeroy shrine after a server first;
  - a hidden emote reply;
  - the bugged-NPC legend.

Added features (the Exchange, the Herald, Leeroy, the Special Forces) get an "era-plus" label, so purity rules never
target them.

---

## 10. Rules for resolving purity-versus-fun trade-offs

- **R0.** The owner's gameplay rules (real loot, Exchange, stablemasters, earned armor, DF for solo bots) outrank everything below.
- **R1.** North Star order: alive and busy at prime time > fun > era *form* > era *detail*. Authenticity breaks ties.
- **R2.** The era governs *form* (channel names, words, mechanics the player sees), not *volume*. A peak server's noise is authentic.
- **R3.** Where the era lacked a convenience, use the era's mechanism first (chat groups, /b, the LFG window). If that
  can't carry the feel at our scale and on our client, add an owner feature labelled [O], default on, with a purist switch.
- **R4.** Never remove a working busy-feel feature for purity without a replacement measured at least as busy (T4).
- **R5.** Silence is the fallback for *replies to the player*, never for the ambient world.
- **R6.** Fallible in the world, dependable in the player's group and among bots who know him. Mistakes need a personality behind them.
- **R7.** The world goes on without you (outcomes can be lost), but your circle centres on you.
- **R8.** Every rule and data row is tagged E/D/O with a fact id and a source date. An undated era claim is a lead, not a rule.
- **R9.** Own measurements (play sessions, T1–T5, soak) outrank outside sources. One game's choice is a candidate, not a decision (the frameworks' rule, kept).
- **R10.** Easter eggs and added features may break era purity if they are discoverable in-fiction and don't break R0.

---

## 11. The independent audit's extra ideas

| Idea | Verdict | Already have it as | Release placement |
|---|---|---|---|
| **Realm Pulse** | **Change**: a small derived *read model*, not a new simulation | `AutonomousRvrDirector` (pure, tests only) [V via doc]; defender share steered by the last six sieges; the Special Forces' win record; the Chronicle | 1.2 phase C with director M2: four or five numbers per realm (territory, recent outcomes, free 50s, raid readiness) computed from records. Add economic health and morale only after the ledger and history exist. I disagree with making it "the connective tissue" up front: that is a rewrite risk with no visible gain until the director uses it [I] |
| **Persistent social graph** | **Keep**, built on the existing tables | memory, bonds, nemesis, reputation, guild membership [V]; the chat framework §3.3 | 1.2 phase D (§8), before the guild package |
| **Offline world progression** | **Change** | Nothing: the population controller "never invents offline progress" [V, `AutonomousPopulationController.cs` header] | 1.3. When the *server* starts after days off, a bounded catch-up rolls keeps, relics and raid outcomes from records, and levels the player's social circle with the real XP rules. **Never gear** (the earned-armor rule). Gossip and the Herald report it. Note: the server only runs while the PC does, so "offline" means "server off" |
| **Campaign logistics and supply** | **Reject for now** | Siege supply purchases and kits (`BotSiegeRuntime`) [V]; the war-effort chapter | 2.0 with crafting M5. Supply chores are the "timer chores" the events research warns about [V via doc] |
| **Bot careers** | **Change**: derived reputation tags, not a progression system | lifetime builds, personalities, bonds' clean-run counts, deeds (players only) [V] | 1.3: "reliable healer" or "raid veteran" read from history and bonds, shown on /target and in chat |
| **The Herald as living history** | **Keep, and move earlier** | The static Herald page, server firsts, Most Wanted, RealmEventRecords [V] | **1.2 must**: narrative news lines from records. The highest visible value per hour of any idea here [I] |
| **Deterministic replay** | **Change** | The chat simulator and the planned event rehearsal; `/lstest` | Seeded RNG for pure rules and the rehearsal simulator; "scenario capture" (seed, timeline, state snapshot) for raids and sieges. **Reject full live replay**: thread-pool timers (`ClassicQuests`, QUESTING #13 [V via doc]) and 3,000 bots make it infeasible [I]. Ships with M0's timeline report |

Where I disagree with the independent audit:
- **Its 1.2 "foundation-quality release"** (a global AI scheduler and a shared decision architecture first). The budget
  measurement, yes: it's a must above. A shared decision rewrite before visible improvements, no. Do reason codes and
  /lswhy first. They expose the competing goal selectors cheaply. Then arbitrate where the logs show conflicts [I].
- **Its staging** (1.2 foundations, 1.3 living realm) pushes the Herald, history and prime-time feel to 1.3. Those are
  the cheapest North Star wins, so I keep them in 1.2.
- **I agree with** the perception math (and it is understated), provenance, the human benchmark, ledger before sinks,
  durable event state, and not shipping PR #54's known APPRAISE error [C].
