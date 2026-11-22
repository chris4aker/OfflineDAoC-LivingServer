# Review: the last game-term clean-up (place legends, bot lines, boss lines and history, all realms)

The reviewer is independent and did not write the seven commits reviewed on `wt5/clean-review`
(`45e7c8da` to `d24ad7b1`, the first seven of `git log fix/1.2.1..wt5/clean-review`): about 1,750 lint hits cleared in place (location)
legends and bot lines, boss bot lines and history across Albion, Midgard, Neutral and Hibernia. The standard is the
three earlier reviews' (`REVIEW-LEGENDS-*.md`): strength tiers, paces at 30 units, damage as a share of a robed caster's
strength at 1,000, health as "as much killing as N raiders" at 2,000, fractions, exact times, "companions and the realm's
adventurers".

The fixes are in `81b69129`, `ed2ff0a6`, `0b8f5e5f`, `29d81f1c`, `00426ea8`, `3ccca50d`, `4304857e` and the commit that adds
this file. Paths are relative to `source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **code** (checked against the boss's script and `EpicEncounterMechanics` in this tree), **data** (the boss's own
facts), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a tip that is wrong for the code, or a contradiction a player would notice.
- **Low:** prose, consistency across realms, the standard, a word the lint missed.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **The writing is good and nothing was cut.** 925 visible strings changed in 331 files. A word-difference of every old
  visible string against the new one (1,821 changed runs, 243 pure deletions) shows only game words gone (bare levels,
  "faction", "zone", "named", "resists", "spawn", "rvr", "death spam", "region 201", "is one bind area") and their meanings
  said again in other words. No visible string disappeared (string counts per file are equal before and after, 345 files
  with this review's fixes); the only structural change is the renamed loremaster keyword (`epics` to `great quests`).
- **Sampled 115 changed strings** at random across the realms and kinds (legends, bot lines, told text, history), plus
  every hit of the new lint rules. The tiers sit on the scale the boss reviews use (young adventurers, seasoned bands,
  hardened veterans, the realm's best, a war party of the realm's champions, the whole muster), the numbers are in words
  or paces, and the realms now say the same idea the same way (L1-L3).
- **One owner rule was broken** (H1, the Bane of Hope): the code throws and four texts said it never moves a man again.
  The first commit had already fixed its legend, briefing, rumours and facts.
- **The lint missed levels in words and some camp slang** (L4): 31 strings, now reworded, and the rules are in
  `fourth_wall.py` with tests. The lint is now strict (`GAME_TERMS_FAIL = True`) with 0 hits.
- **Every check passes** (see Tools). All of this is tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Fight tips and the code | 1 | 1 | 1 | 2 |
| Consistency across realms | 0 | 0 | 3 | 1 |
| Words the lint missed | 0 | 0 | 2 | 1 |
| Links, cuts and the schema | 0 | 0 | 1 | 3 |

## Fight tips and the code

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| H1 | `bosses/bane-of-hope` story, chapters "Three Halls Apart" and "There Is a Door", loremasterTopic; `locations/caer-sidi` story and chapter 7 | **High** | The code throws: on a one-in-four chance a side attacker gets Essense of Souls and 4.5 s later `TeleportEnemy` sends him to one of three far spots, `AllowDisplacement` and `AllowBotDisplacement` ship on for `BaneOfHope` (`EpicEncounterMechanics.cs` 234-235, `UT_EpicWave2Albion`) (**code**). The story, both chapters, the loremaster and Caer Sidi said the third company broke it and "it has not moved a man since". Owner rule: never cut, elaborate. | Each keeps its sentence as the past ("for years after it did not move a man") and goes on in the same voice: the companies that came after climbed as strangers, the part of it that died grew back, and it casts men away again, still burning; the Inconnu, the Church and the bards each stretch their old readings to fit. Loremaster: "the third company broke that for a time, and it scatters them again ... and then casts him three halls away". Caer Sidi: "broke the Bane on its way, for a time", and the garrison's clerks now mark each man who walked back alone. A rumour that read "it forgot how to throw people once, after someone came back, and then it remembered" is "for years after someone came back, then remembered". |
| M1 | `locations/caer-sidi` chapter 7 | Medium | Said the part of the Bane that cast men away "believed they would come back"; the boss file says "would not come back", and its chapter's logic needs "not" (that part "died the first time someone did"). | "would not come back" in both files. |
| L1 | `bosses/cailean`, `corrupt-primal-of-water`, `easmarach` (legend and briefing), `uaimh-lairmaster` (legend and briefing) | Low | A share of health in tens, which the standard tells as fractions: "heals itself at thirty", "runs to its pool at thirty", "At thirty, once a fight", "At sixty he breaks". | Checked in code: Cailean `HealthPercent <= 30`, Easmarach `<= 30` (heals `MaxHealth / 8`, back in 50-80 s), the water primal below 30% (its fact), the Lairmaster `<= 60` (**code**). Now "three-tenths of its strength" and "three-fifths of his strength" (short forms in bot lines). |
| I1 | 115 sampled strings | Info | Checked against code and facts where a mechanic is stated: Council Ozur's crowd count (50 and 100 attackers, `Ozur.cs` 99-100); Elro's treants (95-90% and 60-55%, `Elro` 159-165); Koalinth Inflict Suffering 17% told as "about a sixth"; Caledon's renegades (38 against a field capped at 35) as "far stronger than anyone the field lets in"; the Bane's twenty paces (radius 600) and twelve to eighteen seconds. All right. | None. |
| I2 | `locations/hy-brasil` legend, `bosses/elro-the-ancient` | Info | Elro's script has a third treant window, `HealthPercent < 25 && HealthPercent > 30`, which can never be true, so the legends' "two windows" is right for the code (owner question 2). | None. |

## Consistency across realms

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| L2 | `hadrians-wall`, `snowdonia`, `cruachan-gorge`, `mount-collory` legends | Low | **Keep upgrade levels** said three ways: Albion "built up to the fifth rank" / "tenth rank" / "a keep of the fifth rank", Hibernia "its walls built up four courses of the ten" and "as high as walls go". "Rank" already means the court's and the legions' ranks in 130 strings. Midgard's legends name no keep level. | One wording: **"its walls built up N courses of the ten"**, and **"all ten courses"** for a relic keep (Hibernia's "as high as walls go" kept beside it). |
| L3 | 48 files (Midgard bosses and places most, Hibernian bot lines) | Low | **Named foes** said seven ways: "foes of name" and "great foes" (Albion, Hibernia), "known foe(s)" and "known beast(s)" (Midgard, 33 times), "one of the old ones of this lair" (Iarnvidiur's lair, where "the old one" is Iarnvidiur herself), "X with a name", "one with a name", "ones with names", "old ones with names", "the old ones" (Hibernia and Albion bot lines, 34 times), "a great one with a name". | Two forms: **"foe(s) of name"** (and "beast(s) of name", "a leprechaun of name", "one of name") for the named, **"great foes"** for the big ones. Told text keeps its own phrasing ("A shade with a name.", "one man with a name"). |
| L5 | `bosses/luriquay` aftermathLoss | Low | "duo'd him" had become "went at him two-handed", which reads as a weapon style (25 other strings use "two-handed" for weapons). | "went at him with just the two of us". |
| I3 | every "great quest" and porter string | Info | **Great quests** are already one wording in all realms: "the vikings' / seers' / rogues' / mystics' great quest", "the Church's", "the Academy's", "Defenders of Albion's great quest", "the realm's great quests". **Teleporters** too: the NPC is "a porter" or "the realm's porters", the act "sends you by the stones" (Master Visur, Stor Gothi Annark, Channeler Glasny). | None. |

## Words the lint missed

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| L4 | 31 strings in 27 files | Low | Levels in words and camp slang the lint did not catch: bare decades in bot lines ("twenties", "teens to twenties", "thirties and forties", "[four fifties\|...]", 15 lines), "lowbie(s)" (5), "newbie", "twinks", "timed out", "the lesser named", and "character(s)" for an adventurer in 6 legends. No "fifty-ish", "sixty-plus", "noob", "Tier A/B/C" was left in visible text (the Silvermine's "Tier C named" is in a hidden fact). | Reworded on the tier scale: "seasoned bands", "young adventurers up to seasoned", "seasoned to hardened veterans", "four of the realm's best", "the young", "camps for the young", "the ones kitted out far beyond their seasons", "ran out of time", "the lesser foes of name", "every adventurer of Midgard". Two bot lines shortened to stay under 140 characters (`build_corpus.py --check`). |
| L6 | `tools/lore/fourth_wall.py`, `tools/lore/tests/test_fourth_wall.py` | Low | The rules for L4. | New `GAME_TERMS`: bare decades (raiders' fields); "fifty-ish", "sixty-plus" (every field); "a forty could", "any thirty." (raiders' fields); a share of health in tens ("heals at thirty", "At sixty", "at thirty [runs", raiders' fields); lowbie, low-level, newbie, newb, noob(ie), twink(ed), "timed out", "lesser named", "Tier A/B/C" (every field); "character(s)" (raiders' fields). Head counts, ages and paces pass ("twenty-eight fish", "keep it under fifty", "at fifty or more he gets stronger", "the thirty lords", "walked out at fifteen", "a sergeant at twenty-two", "the young, in their twenties," in a story). New `GAME_TERM_EXCEPTIONS`: the spraggonoll named Boss (`\bits boss\s*$\|^boss is a spraggonoll\b\|\bcall(?:s\|ed)? (?:their\|its\|his) (?:leader\|chief) Boss\b`) and a bell rope (`\bpull(?:s\|ed\|ing)? the (?:bell[- ]?)?rope\b`); Black Mountains South's bell line has its original "nobody pulls the rope" back. Two new tests. |
| I4 | `tools/lore/fourth_wall.py` | Info | **Strict.** `GAME_TERMS_FAIL = True`: `lint_lore.py` exits 0 with 0 game-term hits; any new hit fails CI. `BAN_PENDING` stays False. The switch test now sets the switch off inside itself to keep the warning path covered; `LINT-LEGENDS-HITS.md` is regenerated (0 hits) and its table no longer leaves an empty column. | As described. |

## Links, cuts and the schema

| # | Where | Severity | Finding | Change |
|---|---|---|---|---|
| I5 | `locations/raumarik`, `locations/cursed-forest`, `bosses/undead-patrol` | Info | **"great quests"** (was `epics`): the topic key moved with its text unchanged; both intros link `[great quests]` to their own file's topic, and the Undead Patrol's loremaster topic ("See [great quests]") resolves through its location, Raumarik (boss topics join their location's, `LoreBible.Validate`). No `[epics]` link is left anywhere. | None. |
| I6 | all lore files | Info | **Nothing cut** (see the verdict): pure deletions were read one by one; the longest ("it threw people to one of three far spots in Caer Sidi; it no longer does") is the Bane fix, now told as the throw. | None. |
| L7 | `bosses/hurika` atmosphere phase | Low | **Schema mirror** (coordinator's request): a throwaway Python mirror of `LoreBible.Validate`'s failures found the 75-character phase line CI caught on `fix/1.2.1` (`38123276`). | "Hurika's wings raise a gale. Someone leaves the ground!", as on `fix/1.2.1`. The mirror then reports 0 problems. |
| I7 | seven Midgard legends (`bjornvarg`, `earl-fenex`, `earl-zhalzhak`, `hrolleif`, `khrin`, `morbax`, `rolf`) | Info | "some about N paces", the typo the Midgard review fixed in Hurika. | "about N paces". |

**What the schema mirror covers** (problems only, as `EveryLoreFilePassesTheSchema` asserts): JSON validity and folder;
unknown fields at every level (location, boss, atmosphere, areas, bot lines, loremaster, book, yells, chronicle, members,
realm takes, facts, phases, chapters, related, history, eras, events, factions); value types (lists of strings, whole
numbers, region and zone numbers); ids (kebab case, file name, duplicates; catalog class keys; npcNames claimed twice);
facts (at least one, text, provenance in the five values, a source unless "original"); chapters (title, text, title at
most 60 characters with no brackets, duplicate titles); atmosphere lines (non-empty, at most 60, no choices or slots);
bot lines (choice syntax, lower case, no slots, every expansion at most 140); yells (non-empty, at most 200, choice
syntax); phase triggers (health 1-99, the named triggers, the 17 mechanic triggers; adds and partners in the catalog
built as `UT_LoreBible.CatalogFrom`); a location's name, realm, zones-need-regions, summary, first entry, areas (name,
radius 100-30,000, x/y, region, lines), a tell line, three bot lines, loremaster intro and every `[keyword]` link,
realm takes, tier, book title and pages, its bosses' `locationId`, a boss listed twice; a boss's name, realm, catalogKey
or npcNames (exact), catalog knowledge, location file and realm, an atmosphere line, three bot lines, loremasterTopic
pairs, tier, members (order files only, names in npcNames, yells, lairEntry, topics, duplicates), chronicle slots; the
history checks (ids, names, era/faction clashes, era text, era fact provenance and source, event name and text, faction
text); era, faction and related links. **Not mirrored:** the warnings (word counts, `CheckGameTalk`, links to files not in
the set, shared faction ids), and the C# 24-way sample of a bot line with more ways than that (the mirror checks every
way, which is stricter). A deliberate break of each kind (a 61-character line, a 201-character yell, a dangling keyword,
a missing source, an unknown field, a bad trigger, an unclosed choice, a duplicate chapter, half a topic) is caught.

## Tools

These ran on `4304857e` (with this file the only later change), all exit 0:
- `tools/lore/lint_lore.py`: 0 problems, 0 game-term warnings, 69 template labels (cap 69); `GAME_TERMS_FAIL = True`.
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 29 tests OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK; corpus.json matches its sources.
- `tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 OK.
- `python3 -m unittest discover -s source/tools/tests`: 295 OK (1 skipped).
- The schema mirror of `LoreBible.Validate`: 108 locations, 578 bosses, 4 history files, 0 problems.

Nothing needed regenerating but `LINT-LEGENDS-HITS.md`. `UT_LoreBible` (dotnet) could not run here; it must run in CI or
on the owner's PC before merge. All of this is tests only; nothing has been seen in game.

## For the owner

1. **The Bane of Hope throws again in its lore.** The passage says the part of it that died grew back because the
   companies climb as strangers. Recommendation: keep it; if the owner would rather the throw stay off, that is
   `AllowDisplacement` for `BaneOfHope`, and the lore would need the old "since" back.
2. **Elro's third treant window** (`HealthPercent < 25 && > 30`) never fires. Recommendation: decide whether it was meant
   to be 30-25%; the lore follows the code either way.
3. **The pending chat words and map marks** (`PENDING_OWNER_DECISION`) are still allowed; map marks remain in a few
   legends ("(about 32k/37k)", Hibernia housing's "(297k/480k)"). Recommendation: rule on them, then `BAN_PENDING = True`.
4. **"Foes of name" in chat** reads more formal than "with a name" did. Recommendation: keep the one wording, so the
   bestiary and the camp talk agree; if the owner prefers the chat looser, allow "X with a name" in bot lines only.
