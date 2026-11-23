# Review: decision 8, the fourth wall (#399), the companion-name test (#400) and the Avalon City summary (#401)

The reviewer is independent and did not write these commits. Five commits were reviewed on `wt3/fourthwall`
(`git diff 2c47a7ae 7e05eef0`):
- `0e6f3c22`: 468 player-visible strings in 315 lore files and the four lore chat files reworded (owner decision 8)
- `2e75dcac`: `tools/lore/fourth_wall.py` and its tests; the five Catacombs-era Herald facts dated
- `790924c0`: `name_counts.legend_names` and the companion-name test (#400)
- `6ba1ab30`: Avalon City's summary rewritten (#401)
- `7e05eef0`: decision 8 in HEROES.md; the corpus and DEBATES.md regenerated

The fixes are in `f01e5a46`. Paths are relative to `source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **code** (checked against the boss's script and `EpicEncounterMechanics` in this tree), **traced** (checked
against the C# that shows the field), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a fight tip that is wrong for the code, or a contradiction a player would notice.
- **Low:** prose, consistency, a tool's blind spot.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **Sampled:** 491 changed strings in all. About 230 were read in full: every rewording that touches "bot", "NPC" or
  "player" (291 word changes, listed by script), every history and narrative change (about 40), every chat-file change
  (33), and a random 45 across bosses, locations and bot lines. In the random 45 none of the writer's rewordings was
  wrong; across the 230 there were **four wording slips** that the rewording made (under 2%), plus **seven older fight
  tips** that the writer carried over unchanged and that the code contradicts.
- **The main gap is coverage, not quality.** The writer's rewordings read well, stay in the speaker's voice and keep
  the mechanic. But the first pass and its lint missed 137 visible strings with the same problem: "no spells or styles
  in the data" (55 legends and bot lines), "spawn data", "data quests", "flagged aggressive", relations "with its own
  file" and "the region overview" (shown in `/lore` as "Bound up with: [Caldey] (an isle of the region, with its own
  file)"), "(file to come)", "region overview", "zone" in told text, and two history facts ("two spawns", "the ghost
  flag"). All are reworded, and the lint now catches them (H1).
- **Nothing was cut.** A word-subsequence check of every old visible string against the new one found only the
  fourth-wall words themselves removed, as the writer claimed. Of the 95 pure deletions, 74 are those words ("bot",
  "template", "data", "in Living Server", "2003", "SI") and the other 21 are in the rewritten Avalon summary, which says
  each of those things in other words. The fixes here remove only "data", "file", "overview", "zone", "flagged", and the
  false halves of the seven contradictions (M1).
- **The "never shown" claims hold** (traced): `LoreCodex` shows a history era's events and its facts' text
  (`LoreBibleSource` line 29), the summary, story, chapters, legend and relations; no code but the schema check reads a
  location's or boss's own facts, or any fact's provenance or source. `LoremasterDialogue` would speak a boss's legend
  when it has no `loremasterTopic`, but all 578 bosses have one. `LoreBooks`, `LoreHerald` and the launcher's
  `BestiaryControl` read no facts.
- **The five Catacombs-era facts** (`avalon-city`, `morgana`, `caer-sidi`, `jordheim` twice) now open "Camelot Herald
  (2004)" and keep their camelotherald.com source. No visible text names Catacombs as a product.
- **Every check passes** (see Tools). All results are tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Coverage and the lint (#399) | 1 | 0 | 2 | 2 |
| Fight tips and bot wording (#399) | 0 | 7 | 3 | 3 |
| Companion names (#400) | 0 | 0 | 2 | 1 |
| Avalon City (#401) | 0 | 0 | 0 | 1 |

## Coverage and the lint

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| H1 | 55 boss legends and bot lines; 54 relations in `albion-shrouded-isles`, `hibernia-wilds`, `hy-brasil` and 21 Hibernian locations; 3 overview summaries and legends; `fedail`, `shiake`, `snowdonia`, `morgana`, `earl-ipostian`, `treal`, `beath`, `gunflor`, `shade-of-ambrosius-aurelianus`, `history/neutral`, `history/midgard`, `olcasgean`, `hadrians-wall` | **High** | Decision 8 forbids the software under the world in visible text. The first pass reworded "the world data" (15 times) but not "**the data**": "No spells or styles in the data" is the commonest closing line of a boss legend, and bot lines say "no spells in the data", "hes set aggro in the data", "her row is flagged aggressive". The `/lore` codex's "Bound up with" line printed "(an isle of the region, with its own file)" 31 times and "(the region overview)" 21 times. Gunflor's relation said "(file to come)". `shrouded-isles`' legend opened "The Shrouded Isles file is the neutral overview". Two Darkness Falls history facts read "Two spawns of Marquis Chaosmar" and "Duke Harboris spawns with the ghost flag". The lint matched none of these. | Reworded in the raiders' voice: "No spells or styles, by every account", "(about 80 seconds, by the raiders' count)", "does not stand anywhere in this world", "lots of quests", "she attacks on sight", "a flier", "(her tale is yet to be told)"; relations "with its own tale", "the wider country it belongs to", "a land of the region"; "The haunted places of old Hibernia, told together"; "This is the Shrouded Isles told for all three realms"; "Marquis Chaosmar stands twice over", "Duke Harboris walks as a ghost"; "zone" in told text is "country" or "lands". `fourth_wall.py` now catches "the/his/her/quest/spawn data", "data quests", "flagged aggressive/peaceful/as", "ghost flag", "with its own file", "file to come", "overview", "dev team" and "AI" everywhere, and "zone" and "spawns of/with" in told text. |
| L1 | `fourth_wall.py` | Low | "in-game" slipped through: the pattern `\bin(?: the\|-)? game\b` needs a space after the hyphen. | Now `\bin(?: the \| \|-)game\b`. Tested. |
| L2 | `fourth_wall.py` | Low | **Deliberate bad strings** against the lint: it caught "bots", "NPC", "patched", "1.62", "game master", "level 50", "mobs", "server", "Mythic", "EA", "Prima", "Camelot Herald", "Catacombs's", "expansion", "beta", "XP" and "Offline DAoC". It missed "in-game", "the raid AI", "dev team", "the data" and "overview" (fixed, H1 and L1), and still misses "log off" in chat files, "EverQuest", "realm rank" and "respec" (left: other games are not named anywhere, and the rest is raiders' talk the owner keeps). The exception list is narrow (five phrases) and hides nothing real. "the Catacombs" (capitalised, after "the") passes so that Cornwall's road to the Catacombs of Cardova can stay; no visible text uses it for the expansion. | No change beyond H1. |
| I1 | `LoreCodex.cs`, `LoremasterDialogue.cs`, `LoreBooks.cs`, `LoreHerald.cs`, `BestiaryControl.cs` | Info | The field map in `fourth_wall.py` matches the code (traced, see Verdict). | None. |
| I2 | four lore chat files | Info | The chat rewordings hold: GM is "the king's herald", the devs and patch notes are "the heralds", a patch is "a season", "server reset" is "storm", "the best xp in the game" is "in the realms". The hidden-world debate (L474, L497) still argues that the isles "came all at once one season", which keeps the argument in-world. | None. |

## Fight tips and bot wording

Throws and teleports move a bot only where `AllowBotDisplacement` ships on (`EpicEncounterMechanics`, 35 encounters
including Sys'sro, the Afanc, Moran, Orylle, Krevo, Mortufoghus, Boligar, Ydenia, the Glacier Giant, Krackenschtein and
the four Lyft-Miht lights). Every "companions get thrown too" in the batch is on that list (**code**).

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `high-lord-saeor` legend, bot line, story, fact | Medium | **The flagged self-contradiction.** The legend said companions' deaths heal him and, one clause later, "companions who die to him heal nothing"; a bot line said "companion deaths dont heal him". The code heals him by the maximum health of any raider he kills (`EncounterTargets.AsRaider`: a real player or any bot) dying within 1,000 (**code**). The story echoed the doubt ("Yet not every man... the Inquisitors say he drinks only those who came down of their own will"), and the fact still said "GamePlayer". | Legend: "A dead companion feeds him as well as you would, and so does a fallen adventurer of the realm's warbands; companions who die to him heal him just the same, and only deaths to his servants heal nothing." Bot line: "companion deaths heal him too, so rotate tanks before they drop anyway". Story keeps the Door-Book and the Inquisitors' claim as a doubt that the captains' count settled: "found him fed by sworn servant and free sword alike". Fact (hidden) corrected. |
| M2 | `prince-asmoien`, `prince-baalorien` | Medium | **The flagged "he ate a bot" -> "a pet".** The devour takes the nearest *summoned pet* within 2,000 and never a bot (`NearestDevourablePet`; "never a bot: BotBrain is a controlled brain too") (**code**). So "a pet" is right, and companions are never eaten. But each prince kept a briefing line from before: "yes companions count" and "companions count, not just pets", which contradict the line before them ("companions are fine"). | "yes companions' pets count too" and "companions' pets count, not just ours". The aftermath "he ate a pet" lines stay. |
| M3 | `syssro-the-ruthless` legend | Medium | "Companions are thrown too; only you are." (older, with "Bots"). The pit throw ships on for bots (**code**: `SyssroRuthless`), and never takes his target. | "Companions and the realm's other adventurers are thrown too, not only you; only his target is never thrown." The first sentence names the realm's adventurers as well. |
| M4 | `legendary-afanc` legend | Medium | "Companions are not thrown today, but that may change." The throw ships on for bots in 1.2 (`LegendaryAfanc`, "the bot walks back") (**code**). | "Companions and the realm's other adventurers are thrown as well as you, and they walk back the same way, so keep everyone topped." |
| M5 | `oona` legend | Medium | "she raises an undead copy of anyone, companions included...; companions aren't raised." `EnemyKilled` raises any raider (**code**). | "...; companions are raised just as you would be." |
| M6 | `high-lord-oro` legend and bot line | Medium | "drains every raider within 350, companions too... companions are not drained"; bot line "companions arent drained". The drain takes everyone in `EncounterTargets.InRadius` (bots included) (**code**). | "companions are drained as well as you"; "companions get drained too". |
| M7 | `krevo-ricik` bot line | Medium | The legend says both tricks fire on companions' hits; the briefing said "companions hitting him trigger nothing". `OnAttackedByEnemy` fires on any raider, and the toss ships on for bots (**code**). | "companions hitting him trigger it too". |
| L3 | `tepoks-mine`, `muire-tomb`, `green-knight`, `master-of-pain` | Low | Slips: "Adventurers in their thirties camp the mine" reads as age, not level; "In the first year of the war raiders warned" wants a comma; "companions too,:" (older); the story's "which the scholars of the realm rated at a level no living warrior has reached" kept a game "level" when "Allakhazam" came out. | "Adventurers camp the mine in the level thirties"; the comma; "companions too:"; "ranked beyond the strength of any living warrior". |
| L4 | about 80 raid legends and bot lines | Low | **"Companions" for every bot.** In a raid legend the writer turned "bots" into "companions". In code those claims cover companion bots (`/raid`, `/bot`, hired) *and* gamebots, and a player who joins a gamebot raid as a guest raids with gamebots, not companions. No claim found is false for gamebots after M1-M7: each says companions are included, not that only they are. Archilus, Elro, Veena and Hrolleif already name both ("hired companions" and "the realm's other adventurers"). | No change. Owner question 2. |
| I3 | `archilus`, `elro-the-ancient`, `hy-brasil`, `worlds-end` | Info | "only GamePlayer damage counts" -> "only your own blows count": the shroud and treant triggers check `source is GamePlayer`, so a companion's, a gamebot's and a pet's hits do not count (**code**). On a single-player server "you" is exact. | None. |
| I4 | 26 legends | Info | "no script" -> "no tricks" keeps the meaning (a plain fight). "the old guides" -> "the old raiders' accounts" (45) is consistent across files. Dates: Jan 2003 -> "the second winter of the war", April 2002 -> "the first spring of the realm war", 2001-2002 -> "the first year of the war", later years -> "long afterwards"; they agree with the October 2001 start. | None. |
| I5 | history files | Info | Each real-world detail taken out of a history fact's text (patch 1.49, "Mythic's quest", the 2004 New Frontiers removal, "Allakhazam's bestiary", "Offline DAoC spawns no Lilith") is now in that fact's hidden source note, as claimed. Abermenai "was opened to the young long after the others" keeps 1.62 (June 2003) inside the 1.65 limit. | None. |

## Companion names (#400)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| L5 | `name_counts.legend_names` | Low | **A false negative.** A legend-age sentence is found only by a HEROES name in it. A legend told beside a god or a myth figure who is not a HEROES legend is missed: "Fand left the sea-god Manannán" passed for the companion name Fand. | `LEGEND_ANCHORS` (Arthur, Merlin, Morgana, Arawn, Odin, Thor, Loki, Manannán, Lugh, the Dagda, Nuada, Balor and 26 more) also mark a sentence as legend-age. A fixture tests the case. On the real lore it adds no clash: the only companion name in such a sentence is Eirik, already shared. |
| L6 | `legend_names` | Low | Other limits, left as they are: capitalised words only (`[A-Z][a-z]+`, after folding accents); a legend named only in a boss's or location's own `original` fact is not read (the story normally tells it too); over-inclusion ("Sir", "The") is harmless because only companion names are looked up. False positives: none on today's list. | No change. |
| I6 | `Hire165.cs` (36 names) | Info | `companion_clashes` passes. The only legend names on the list are Áine (the goddess in `lough-gur`'s myth fact) and Eirik (Ragnar's son in `east-svealand`'s story and fact), both allowed as shared given names. Dinadan, Lynette, Brangwen, Loegaire, Fand, Muirne and Gráinne appear nowhere in the lore. Brigid is a companion name, and Brigit of the palace, the Moonstone Twin quest giver (`faerie-queen`, `rioldna`, `bog-of-cullen`), is a present-day character one letter away; the test folds accents, not spellings, so it cannot see it. Present-day characters share other companion names (Brother Aldric in Darkness Falls, Sigrid, Lorcan, Astrid, Gudrun, Leif, Sven, Aodh, Eoghan, Maud, Arnulf); decision 7 allows that. | Owner questions 3 and 4. |

## Avalon City (#401)

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| I7 | `locations/avalon-city` story | Info | 498 words (range 300-500). Every event of the old summary is kept: the second college's dusk-reading of the marsh, Elowen's eleven years, the marsh taking back the lower streets, the colleges in their order and the flamethrowers following it, Rhydderch and the charters, Morgana taught by Angharad and her question, the healers' bed for forty years and the barge that never came, Glastonbury, the bed burned on the second night and the spring gone cold, the fishermen's boats and Ifor, the nine chests, the books paid to Krondon, the drakoran by trade and clan, Weno'iak, the vyr'em, the drau'gyn fire, the catchpole's beasts, Maelon and the bridges, Sefin, the door over the Crystal Cave, the children's list at dusk. Arthur stays dead (told about), Merlin is not in it. It reads well. Shared five-word runs with the chapters fell from 108 to 42 (61 to 23 with "The Colleges in Their Order"). | None. |

## Tools

These ran on `f01e5a46`, all exit 0:
- `tools/lore/lint_lore.py`: 0 problems, 69 template labels (cap 69).
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 19 tests OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK (16,467 lore bot lines; corpus.json matches its sources).
- `tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `python3 -m unittest discover -s source/tools/tests`: 292 OK (1 skipped).
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 OK. `test_lore_claims` caught the first wording of the
  corrected Saeor fact ("a real player or a bot"), and the fact was reworded.

Nothing needed regenerating: the fixes touch no chat `.lines` file. `UT_LoreBible` (dotnet) could not run here because
dotnet is not installed. It must run in CI or on the owner's PC before merge. All of this is tests only; nothing has been
seen in game.

## For the owner

1. **Raiders' fight advice and the realm's loremasters.** The legend keeps levels, health, timers and "respawn" (the
   rule of 2026-11-06), and the fixes keep that voice ("No spells or styles, by every account"). Recommendation: keep
   it. The legend is shown as "What the raid leaders say" and "What is said", never in a loremaster's mouth.
2. **"Companions" for every bot in raid legends (L4).** Gamebots in a realm raid are not companions, but the legends
   call everyone who is not you a "companion". Recommendation: keep "companions" where the claim is "companions too",
   because none is false now, and in the next lore pass write "companions and the realm's adventurers" where a sentence
   is about who a mechanic picks.
3. **Eirik.** Ragnar's son Eirik is told of in `east-svealand` (story and myth fact), and Eirik is a suggested companion
   name. The writer allowed it as a shared given name, like Edith, Gunnar and Áine. Recommendation: allow it. It is the
   commonest Norse name, the lore tells of Eirik once, in passing, and the Audliten quest giver shares it too.
4. **Brigid.** A suggested companion name; the lore has Brigit of the palace, a present-day quest giver, not the
   goddess. Recommendation: keep Brigid. Decision 7 allows names present-day characters share, and Brigit is no legend.
5. **Chat that sounds like 2003 players.** The lore chat files keep "log in" and "log off" (`lore_shared.lines` 126, 556,
   560, 570). Gamebots talk as players of the period, so these read as the period's voice, not as the software.
   Recommendation: keep them. Decision 8 is about the lore, and "the nights i play" is a person talking.
