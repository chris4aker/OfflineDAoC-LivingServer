# Bot builds, roles and RvR that feels like people

Owner: Chris. Opened 2026-11-01 from community feedback on the Beta 1.1 post on r/daoc (a player who tested
battleground RvR at about 30% of the population): the bots spot you from very far away, there are far too many pet
classes (about ten Cabalist pets chasing him at once), the bots all follow the same path, and there's no way to get
the specs a proper PBAE group needs. The fix is in how the world's own gamebots are built and how they play, not in
summoning companions. This is the design; it ships in 1.2 as phase B ([RELEASE-TRAIN.md](RELEASE-TRAIN.md)).

## What the code does today (checked 2026-11-01)

- **Every gamebot has a real spec build** and keeps it for life. Each class has a file of builds
  (`bots/specs/<Realm>/<Class>.cs`, for example a Cabalist's Matter, Body and Spirit builds in several point
  splits), and `BotLifetimeBuild` saves the chosen one.
- **The build and the class are chosen evenly at random.** `AutonomousBotIdentityGenerator` picks the class with
  `random.Next` over the realm's classes, so each Albion class is about one in twelve; each class file picks its
  build the same way (`Util.Random`). Nothing weights either by what was actually played.
- **The spell use doesn't follow the build.** Point-blank area spells were never cast (a range check could not
  pass), ground-targeted area spells always failed, and healers never controlled adds. shadowofze's unreleased
  `main` fixes those (area spells for every class, add control, Celerity, tank Protect/Guard/Intercept, Reaver
  auras); Living Server doesn't have them yet.
- **Frontier and battleground bots acquire you from 5,000 units**, at once, whenever they have line of sight and
  you aren't stealthed (`AutonomousFrontierThreatPolicy`, `TargetSearchRadius`), every 2.5 seconds, all of them.
- **Warbands march one shared route.** (Inferred from the march code; not measured in a session.)
- **You can't ask for a build.** `/lfg` and `/invite` get whoever is near and of the right level.

## The four parts

### 1. The era's real builds, weighted (research first)

For each class, the builds that were common in 1.65 (to 8 October 2003), in RvR and in PvE, with a share each, and
for each realm the share of each class seen in RvR. Sources must be dated to the era: Camelot Herald class pages and
patch notes, period template and spec calculators, class forums and guides from 2002–2003 (the same method as the
bot names in [research/BOT-NAMES-2003.md](research/BOT-NAMES-2003.md)). The result is one data file the code reads
(`bot-builds.json`: class → builds with spec points and weight; realm → class weights for RvR and for levelling).

- New bots draw their class and build from those weights; existing bots keep theirs.
- Builds that aren't in the files yet are added as new variants; the current ones stay, re-weighted.
- A pet-class cap per warband and per battleground wave stops a flood even when the weights say pets are common.

### 2. A playbook per build

How each build plays, written as priorities the class AI already understands (open, control, damage, support,
when to use area spells, when to back off). On shadowofze's spell-use engine, so area spells actually fire.

- PBAE casters close in and bomb only once the group holds the monsters or the enemy is bunched, and back off when
  hit (his code already has the walk-in and the two-failure rule).
- Healers heal first, then control adds, and keep power for healing; support builds buff what the group lacks.
- Crowd control builds mez or stun adds, and never break their own mez with area damage.
- Tanks guard the healer or the squishiest member, protect, intercept and taunt.

First the builds RvR groups rely on: PBAE casters, healers, crowd control, tanks. Then the rest, class by class.

### 3. Groups and warbands built by role

Bot groups and RvR groups fill a 2003-style eight: a tank or two, two or three healers or support, crowd control,
damage. They pick from bots whose builds fit instead of whoever is nearest. Warbands take several such groups plus a
few soloists and stealthers, and split into lanes and small roaming groups instead of one line of march.

### 4. You can ask for what you need

- `/lfg pbae`, `/lfg healer`, `/lfg cc`, `/lfg tank` (and the plain `/lfg`) bring answers from bots whose builds
  fit, near your level, as /lfg does today.
- A bot's build shows when you /target or /invite it ("Theurgist, Earth 50 / Ice 20 — PBAE"), so you know what you
  are inviting.
- Your own group of bots can be shaped: an invited bot you've grouped with before can be asked to respec at a
  trainer between sessions ("can you go PBAE?"), on the same rules a player has (respec stones or level gates).
  Nothing is summoned; the world just has those players in it.

## RvR perception (with part 1, in the same stop)

- A bot notices an enemy by chance per scan, not by certainty. The chance falls with distance and rises when the
  enemy is fighting, casting, in a group, or in the bot's facing cone. Close up it always notices.
- A reaction delay of 0.5–2 seconds, by personality and level.
- The one who spots you calls it ("inc 3 north"), and the group reacts to the call: realm awareness travels the
  way it did in 2003.
- A difficulty setting for bot RvR (easy, 2003-normal, veteran) scales the noticing range, the reaction time and how
  well a group focuses one target.

## How we'll know it worked

The same battleground session before and after, at the owner's roster, with numbers from the server log:
- how far away the first bot to engage you was, and how long after you came into its sight;
- how many pets were chasing you at once, and the share of each class in the battleground;
- how many distinct routes a warband took between two keeps;
- whether a group from `/lfg pbae` actually bombs: area spells cast, monsters or enemies hit per cast.

Automated tests cover the weights, the playbook choices and the perception rules without a running world; the
session numbers are the real check, reported separately (AGENTS.md).

## Open

- Who owns the research for part 1 (a research pass like the bot names, then the owner's review of the build
  lists before they go into the data).
- Whether respeccing your grouped bots should cost what a player pays.
- The default RvR difficulty: 2003-normal is proposed.
