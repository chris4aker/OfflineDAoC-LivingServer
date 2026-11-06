# The bot group framework (1.2)

Owner 2026-11-01. The design the 1.2 group-play work builds to: how gamebots fill roles, fight together, can be shaped
by the player, and stay believably uneven. Evidence: [research/1.2/REPORT.md](research/1.2/REPORT.md) (party AI,
bias audit), [BOT-BUILDS-AND-RVR.md](BOT-BUILDS-AND-RVR.md), the builds research (PR #55), RvR perception (PR #53),
and shadowofze's spell-use engine merged in phase A (area spells, add control, Celerity, tank Protect/Guard/Intercept).
Labels as in the chat framework: **[E]** period evidence, **[D]** borrowed design, **[O]** owner choice.

## North star, and how decisions are made

**The goal (owner 2026-11-01):** the smartest, best offline Dark Age of Camelot: amazing bots, and a real living
server for the player to witness and play in.

Every source serves that goal; none of them is the goal. So:
- **No single source decides a design.** A mechanism goes in when at least two independent kinds of support agree
  (period evidence, more than one shipped game, research, or our own measurements), or when it is cheap and our own
  play test or soak shows it works. One game's choice (Erenshor, Left 4 Dead, RimWorld, EverQuest) is a candidate idea,
  not a decision.
- **Our own measurements break ties:** the blind chat evaluation, the soak metrics, the battleground before/after, and
  the owner's play sessions outrank any outside source about this game.
- **Ideas are tested where they will be judged:** in a DAoC world at full roster, not in the context they came from.

## 1. Goals

1. A group of bots plays like a 2003 group: someone pulls, someone assists, crowd control holds, healers triage. [E]
2. The player can shape his group cheaply: stances first, presets next, per-bot builds last. [D: EQ, PoE2, DA:O]
3. Competence is a distribution, not a constant: forum-ideal groups, pick-up groups and half-built ones; disciplined
   and sloppy players. [bias fix: real 2003 players were often bad]
4. Never the unforgivable: no friendly fire, no wandering off, no wasting the group's resources, no breaking the
   player's own mez. [D: Valve 2009]
5. Explainable: every role decision logs a reason; `/lswhy` covers groups too.

**Where unevenness goes (candidate, to be measured):** bots in the player's own group default to dependable, and
sloppiness is a personality he can see and choose or avoid; unevenness stays full in world bots, pick-up groups and
enemy realms. Support: Erenshor's developer equalised his SimPlayers after players restarted until the good ones were
online; the WoW and FFXIV companion systems keep companions reliable; the bias audit's point that 2003 players were
uneven still holds for the world around the player. Test: does the player keep or kick sloppy bots in play sessions.

## 2. Layers of control (cheapest first)

| Layer | What it is | Who uses it |
|---|---|---|
| **Role brain** | Hand-written behaviour per role, on the base's spell-use engine: tank, healer, crowd control, nuker / PBAE, assist melee, stealther, support / buffer, puller, pet class | every bot |
| **Stance** | EverQuest-style per role: healer Efficient / Balanced / Reactive / Passive; caster Burn / Conserve / AoE; tank Hold / Peel; CC Mez adds / Hold | the player, per bot (`/bot stance` or a menu), and the bot's personality by default |
| **Group roles** | A named **puller** and **main assist**, and a lead/follow toggle | the player, or the group's leader bot |
| **Preset** | Whole-group setups: Standard, PBAE (CC holds, casters close in, healers stand back), Camp (pull to the group), RvR roam | the player, or a bot leader |
| **Build** | The bot's spec, from the era's builds (PR #55), chosen at creation by weight, or asked for ("can you go PBAE?") on the player's respec rules | creation; the player for bots he groups with |
| **Rule list** | An ordered if-this-then-that list per bot (JSON), for enthusiasts | last, optional |

## 3. Role behaviour that matters most in DAoC

- **Pulling:** the puller pulls to the group's spot; the group waits for "inc"; the assist train follows the main
  assist's target. [E: period etiquette]
- **Crowd control as discipline, not perfection:** each bot carries a **discipline** trait. A disciplined bot never
  damages a mezzed target, calls its mezzes, and takes the adds it's assigned; a sloppy one sometimes breaks a mez or
  pulls an extra, and says so ("oops my bad"). The only hard rule is never breaking **the player's** mez. [bias fix;
  D: Trust, Dwarf Fortress]
- **Healing:** triage by role (tank first, then healers, then the rest), power kept for heals; the base's add control
  for healers after heals. Stances change the threshold.
- **Tanking:** the base's Protect / Guard / Intercept, with peel on the stance.
- **PBAE groups:** the PBAE preset sequences CC → close in → bomb → back off when hit, using the base's walk-in and
  two-failure rule.
- **Period etiquette as tendencies:** shared camps with turn-taking, mezzed or engaged monsters treated as someone's,
  no buff begging from strangers, no KS; weighted by personality, not absolutes. [E: Prima 2002]

## 4. Composition

- **Templates per realm** from the period posts (PR #55): an eight with a tank or two, two or three healers or support,
  crowd control, damage. RvR groups are drawn from a **mix**: forum-ideal, pick-up and half-built. [E; bias fix]
- **Class de-duplication and caps:** at most a set number of pet classes per group and per warband. [D: WoW followers]
- **Builds by weight, re-derived from the post counts** unless a written reason says otherwise, with a tail of levelling
  and gimp builds. The audit found weights that contradict their own counts; they are fixed before the data is used.
- **US cross-check:** the class shares stay provisional until the US sources (Vault boards, Camelot Seer, Herald census)
  are read and compared. [bias fix]

## 5. Movement and perception

- **Perception** as in PR #53: noticing by distance and chance, a reaction delay, spotters, an awareness setting.
- **Paths that aren't robotic:** look-ahead steering points and per-bot formation offsets; warbands spread over a few
  routes and send small roaming groups. [D: Valve 2009]
- **No visible gear mistakes:** a bot never wears a downgrade it can see is worse, under the real-loot rules; item
  mistakes without a personality behind them read as bugs (Erenshor's top AI complaint; Dwarf Fortress's lesson that
  mistakes read as character only when a personality explains them). Test: a gear audit over a soak run.
- **Stay with the team:** a bot far behind its group catches up out of the player's sight rather than running a lone
  line through enemies. [D: Valve 2009]

## 6. Talk inside the group

Group chat follows the chat framework: calls that state what the bot is doing ("inc 2", "mezzing the blue", "oom",
"rez pls"), from the rule engine, one speaker at a time, terse. Competence shows in the calls, and so do mistakes.

## 7. Evaluation

- Unit tests for each role brain and stance (choices given a fight state), the composition templates, the discipline
  trait's error rates.
- **The test matrix** (from the bias audit): all three realms; a healer, a stealther, a pet class and a melee class;
  non-GM and GM; full rosters of 600 or more per realm; a level-1 newcomer session; at least one outside player per
  release. Each session tagged human-played or agent-run.
- **Measurements:** mez breaks per hour (by discipline), wipes per hour by group type, pull-to-kill time, distinct
  routes per warband, pets per warband, how far away the first enemy engages the player (PR #53's before/after).

## 8. Build order

| Step | What |
|---|---|
| G0 | Reason strings for group decisions; the fixed build weights; composition templates with pet caps; class shares marked provisional |
| G1 | Stances, the named puller and main assist, lead/follow |
| G2 | The discipline trait and crowd-control behaviour; the PBAE preset |
| G3 | Role playbooks for the RvR-critical builds (PBAE casters, healers, CC, tanks), then the rest |
| G4 | Group presets; asking a grouped bot for a build; `/lfg`-style requests through the period channels (the LFG flag, /say, /broadcast) |
| G5 | Warband lanes and roaming groups; the test-matrix session |
| G6 | The optional rule list |
