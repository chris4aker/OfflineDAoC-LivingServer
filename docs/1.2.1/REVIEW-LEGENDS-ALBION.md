# Review: the Albion boss legends written in-world (owner, 2026-11-21: "Rewrite that")

The reviewer is independent and did not write these commits. Twelve Albion commits on `wt4/legends-all` were reviewed
(`git log fix/1.2.1..wt4/legends-all`, `cfde7e5b` to `707fb5be`): every `legend` and game-term bot line of the 163
Albion boss files, rewritten in-world. The Albion location files that retell a boss tip were reviewed with
them (13 of the 34 changed here), though the writers did not change them.

The fixes are in `450085a7`, `054bfe5d` and `8f6561ac` on `wt4/rev-alb`. Paths are relative to
`source/server/GameServer/bots/autonomous/data/lore/`.

Labels: **code** (checked against the boss's script, `EncounterTargets` and `EpicEncounterMechanics` in this tree),
**fact** (checked against the boss's own game fact), **read** (judged as prose).

Severity:
- **High:** breaks an owner rule.
- **Medium:** a fight tip that is wrong for the code, or a contradiction a player would notice.
- **Low:** prose, consistency, a word the standard does not allow.
- **Info:** checked, and fine.

## Verdict: pass after fixes

- **The writing is good.** The legends read as raid leaders talking, keep every mechanic and often correct the old text
  (Morbus's "half a minute" became the forty seconds the code has). One older tip carried over is wrong (M1).
- **The Albion writer used its own scales** (H1): health as "as much killing as N champions" at 2,000 health each, and
  damage as "a fraction of a champion's strength" against the same 2,000. The standard keeps "champions" for the 70+
  tier, counts health in raiders (2,000 each) and damage against a robed caster (1,000). Health kept its numbers; every
  damage share was recomputed from the script's spell value against 1,000, not just doubled.
- **Three hidden facts contradicted the code** (M2-M4), and three files had a duplicated bot line (L3).
- **Nothing was cut.** No visible string of the 163 files disappeared (structure check against `fix/1.2.1`); the only
  strings that got more than 15% shorter are two bot lines that lost "resist buffs" and "low on health". A word check of
  every legend, old against new, found only game words gone ("level", "leash", "aggro", "tank", "wipe", "AoE",
  "untargetable") and their meanings said in other words.
- **Sampled:** 72 fight tips in 41 files, epic and ordinary. 48 were checked against the scripts (spell damage, radius,
  cast and recast times, chances, timers, tethers, add health), the rest against the bosses' own facts. One was wrong
  (M1); every converted number in H1 was checked against its script.
- **Every check passes** (see Tools). All results are tests only; nothing has been seen in game.

| Area | High | Medium | Low | Info |
|---|---|---|---|---|
| Scales and tiers | 1 | 0 | 1 | 1 |
| Fight tips and hidden facts | 0 | 5 | 2 | 2 |
| Game words and bot lines | 0 | 0 | 3 | 1 |
| Location retellings | 0 | 1 | 1 | 0 |

## Scales and tiers

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| H1 | 73 boss legends and 15 bot lines | **High** | The owner's standard counts health in raiders and damage against a robed caster of 1,000; "champions" is the 70+ tier word. The Albion writer wrote "as much killing as fifteen champions" (2,000 each), "no harder to bring down than two or three champions", "a champion and a half", and damage as "a seventh of a champion's strength" (2,000 again: Draargus's orb burst is 300, **code**). Bot lines did the same with "a man's strength". | **99** health and tier phrases by pattern ("N champions" to "N raiders" where it measures health; "a war party of champions" to "of the realm's champions"), and **61** damage and heal shares recomputed from the spell values (**code**), e.g. Mouth's drain 500 "half a robed caster's strength", Xanxicar's stomp 2,500 "two and a half times", Emthoro's pulse 80 "about a twelfth", Valnir's acid 10 x 80 "four-fifths", Atwell's bleed 10 x 65 "about two-thirds", Coliath's drain 700 "seven-tenths". The Green Knight's felwood (800 health) is "less than half a raider's killing". Narrative "champions" (Xanxicar's household, Orylle's champion and consort, the chronicle lines) stays. |
| L1 | `morgana`, `scurceol-hyrde`, `fames` | Low | **The 70-90 tier.** Morgana (90, cannot be fought) was "a power beyond any war party of the realm's champions"; the Lyft Miht lights (90, 15,000, never attack) "each beyond any war party's match"; Fames's bearer "a war party of champions". | Morgana: "a power it would take the realm's whole muster to face, if she would fight at all" (the standard's final-tier words). Lights: "each by its strength a foe for a war party of the realm's champions, though none ever strikes back". Fames's bearer: "the realm's champions". |
| I1 | 163 legends | Info | Every legend's tier was matched to its level in the facts by script: all agree. The four flagged (Mouth, Morgana, the Lindon and Hoges households) were parse errors (a minion's level, aggro level 200) except Morgana (L1). | None. |

## Fight tips and hidden facts

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| M1 | `xanxicar` legend and bot line; `locations/crystal-cave` | Medium | **The stomp lands about seven seconds after the bellow, not one.** `BombAnnounce` runs one second after each fifth, bellows, then casts `XanxicarStomp` with `CastTime = 6`, uninterruptible, radius 2,500 (**code**). The tip predates the rewrite (`fix/1.2.1` said "one second after he bellows"). "You cannot outrun it" still holds: nobody runs 2,500 in seven seconds. The crystal-cave bot line "stay out of his stomps" told raiders to do the impossible. | Legend: "It lands about seven seconds after he bellows (a breath, then six seconds rearing up that nothing breaks), and the ring is too wide to run out of in that time, so do not try." Bot line: "7 sec after the bellow, cant outrun it". Crystal Cave legend and its bot line say the same. |
| M2 | `legendary-afanc` fact 1 (hidden) | Medium | Said both "bots included" and "Bots are not thrown". The throw picks from `EncounterTargets.InRadius(..., bots: DisplacesBots)` and `AllowBotDisplacement` ships on for `LegendaryAfanc` (**code**). The legend was already right. | "a player or a bot: AllowBotDisplacement ships on ...; a thrown bot walks back". |
| M3 | `risnir-cruss` fact 2 (hidden) | Medium | Said only a GamePlayer's death raises an apparition and "Bots (GameNPC) do not raise one today". `EnemyKilled` takes `EncounterTargets.IsRaider` (a player or any `GameBot`), capped by the flood guard (**code**). The legend and bot lines were right. | Fact corrected; the legend's "Companions raise one as well as you do" now names the realm's adventurers too. |
| M4 | `krevo-ricik` fact 0 (hidden) | Medium | "Only on damage from a GamePlayer". `OnAttackedByEnemy` takes any raider; the throw takes bots because `AllowBotDisplacement` ships on for `KrevoRicik` (**code**). | Fact corrected. Legend: "Companions and the realm's adventurers hitting him set off both". |
| M5 | `locations/avalon-city` legend | Medium | "Weno'iak rides with sixteen horsemen and their steeds." His boss file says about six cuirassiers, eight warsteeds and six reeves; his fact says thirteen of his faction stand near him. Sixteen matches neither. | "Weno'iak keeps his cuirassiers, reeves and warsteeds about him, and they all come in with him." The boss file's count is owner question 1. |
| L2 | `moran-the-mighty`, `fames`, `spectral-provisioner`, `spirit-of-lord-emthoro`, `lich-lord-sanguis` legends | Low | **Who a mechanic affects.** These said "companions" for every bot. Each mechanic takes any `GameBot` (**code**: Moran's throw, Fames's `RandomTarget is GameBot`, the Provisioner's `InRadius(500)`, Emthoro's range check, `BotBrain.IsUntargetable`). | "companions and the realm's adventurers" in each. Bot lines keep "companions too" (they are true, and short). |
| L3 | `aidon-the-archwizard` briefing, `duraek-the-empowered` briefing, `funus` aftermathWin | Low | Duplicate bot lines (all older than the rewrite). | Aidon: "fire, ice, air and earth copies, each bursts its own element round its target. spread out" (four copies, radius 350, **code**). Dura'ek: "steel, matter, body, spirit, energy all hurt him. only fire and frost heal him" (`DuraekEmpowered` heals on Heat and Cold, **code**). Funus: "funus went down fast, he really is the soft one". |
| I2 | 72 tips | Info | Checked and right, among others: Amphiptere's zombies (25-35 s, within 500), Beran's casks (15-35 s, 1,200 in 1,000) and Onstal Hyrde (35-65 s), the Cailleach's torches (150) and bolt (200, 4 s), Durval's Smite (550) and mesmerism (450, 60 s), Lurfos's bursts (300 in 300), Morgus's blast (400 in 250) and Lava's Fury (200), Mouth (30%, minions 20-35 s), Ris'nir's stomp (650 in 1,000, 3 s, 20-25 s), Valnir (30% drain), Vortanos (aggro 800, Fire Blast every 10 s in 350, acid 6 x 89), the Wrath (15% proc, 600 in 300), Qan'ris (8-18 s, reach 350), Sarcondina (45%, tether 3,800, servants 25-40 s), Weno'iak (Lights 1,500, Annihilate 20-25 s in 500), Ulor (Lance 6 x 83 in 350, Devourer 400 in 200), Krevo (10%, 15%, 400 up, 700, tether 3,800), Lady Darra's paladins (350 every 5 s from 2,000), Meade's curse (21% in 350), Xanxicar's throw (20-40 s), glare (450) and champions (15-25 s), Sys'sro's pit beasts (5,000, slowed 60%), the Silencer (one in a hundred per ten attackers, 20 s), Orylle's throw (15-20 s), the Green Knight (felwood 25-35 s within 4,000 on any raider, one blow in three, healer every 40-60 s, heal 400), Aidon's blast (10-15 s) and copies (30-45 s), the Afanc's fall (a fifth of maximum health), Morbus (cold -15, forty seconds). | None. |
| I3 | `wenoiak-the-enlightened`, `amminus-pilus` | Info | Amminus's floor burns "you and your pets", and "companions and the realm's other adventurers cross that line unburned": `PilusFury` reads `GetPlayersInRadius` and pets, so this is exact and on `test_lore_claims`' list. | None. |

## Game words and bot lines

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| L4 | 6 legends, 7 bot lines (`aidon`, `captain-atwell`, `lord-gildas`, `captain-heathyr`, `drihten-elreden`, `funus`, `lurfos`, `krevo`) | Low | "tether" was left in 13 Albion strings; the Hibernian and Midgard legends no longer use it. | "drawn more than about fifty paces from his spot" (Aidon, 1,500), "drawn too far from his post" (the Hall, whose range is in the database), "past that" (Drihten, whose range the sentence before gives), "too far from his spot" in bot lines. |
| L5 | `apocalypse`, `golestandt`, `funus`, `krevo-ricik` bot lines | Low | "rebuff" (3) and "nuking" (1). | "get every blessing back on at once", "renew the blessings", "get your blessings back on", "blasting". |
| L6 | `coliath` bot line | Low | My first rewording went to 146 characters (`build_corpus.py --check` caught it). | Shortened to 125. |
| I4 | all Albion bot lines | Info | Kept as the period's chat, counted for the owner: "gg" 18, "wipe/wiped" 9, "autopilot" 1. No chat line is over 140 characters once its alternations are expanded. | None. |

## Location retellings

| # | File | Severity | Finding | Change |
|---|---|---|---|---|
| L7 | `llyn-barfog`, `forest-sauvage`, `pennine-mountains`, `gwyddneau`, `caldey`, `cornwall`, `dales-of-devwy`, `inishail-island`, `avalon-isle`, `crystal-cave`, `avalon-city`, `caer-sidi`, `krondon` legends | Low | The location legends retell boss tips with levels and health ("the Green Knight (79, 100k)", "the Legendary Afanc (70)", "a level 61 epic drake", "catalog nameds", "con red to anyone under forty", "pull"), against the boss files' own new wording. Caer Sidi had the raid leader answer Fames, but only the one he looks at may answer for the first minute (**code**). | 25 edits: tiers and raiders for every boss named (checked against its level), "great foes", "back about an hour after it falls", "at every tenth of his strength", "far beyond anyone not yet seasoned as a hardened veteran", "the one he looks at says yes (after a minute anyone may answer)", "companions and the realm's adventurers". The zones' own level ranges and the other game words in these legends were not touched (owner question 2). |

## Tools

These ran on `8f6561ac`, all exit 0:
- `tools/lore/lint_lore.py`: 0 problems, 69 template labels (cap 69).
- `tools/lore/name_counts.py --check`: 0 over the cap.
- `python3 -m unittest discover -s tools/lore/tests`: 19 tests OK.
- `tools/lore/mechanic_trigger_coverage.py --check`: 15 of 15 fire.
- `tools/bot-voices/build_corpus.py --check` and `--verify`: OK; corpus.json matches its sources.
- `tools/bot-voices/debates_doc.py --check`: up to date, 565 scenes.
- `source/tools/epic_encounter_catalog.py --check` and `tools/lore/build_encounter_adds.py --check`: up to date.
- `tools/bot-voices/check_runtime_names.py`: OK.
- `python3 -m unittest discover -s tools/bot-voices/tests`: 44 OK (`test_lore_claims` passes with the three corrected
  facts, which say "a player or a bot").
- `python3 -m unittest discover -s source/tools/tests`: 295 OK (1 skipped).

Nothing needed regenerating. `UT_LoreBible` (dotnet) could not run here; it must run in CI or on the owner's PC before
merge. All of this is tests only; nothing has been seen in game.

## Counts for the owner's pending decisions

- **Map positions** like "(about 343k/658k)": none in the visible text of the 163 Albion boss files or the 34 Albion
  locations (coordinates appear only in hidden facts).
- **Chat words** (left as they are): boss files melee 212, casters 144, group 91, camp 69 (many are the drakoran camp
  in stories), quest 51, dropped 14, lol 12, taunt 6, pets 5, mana 4, grats 1, loot 0; Albion locations camp 207,
  group 25, quest 24, casters 5, melee 3, pets 3.

## For the owner

1. **Weno'iak's household.** The legend (older than the rewrite) says about six cuirassiers, eight warsteeds and six
   reeves stand with him; his own fact says thirteen of his faction. Without the world database here the count cannot
   be settled. Recommendation: count them in the 0.37b world and make the legend and fact agree.
2. **The Albion location legends are still in raiders' game talk** ("levels 45 to 65", "catalog nameds", "good camps",
   "xp", "zerg", "rvr", "noobs", "death spam"), as are their bot lines ("the six nameds are 62 to 71", "cc immune", "the
   level 75 guards respawn behind you"). The rewrite covered boss legends only, and this review changed only the boss
   tips they retell. Recommendation: a pass over the location legends and bot lines for all three realms with the same
   standard, so a zone reads "for seasoned bands to the realm's best" rather than "levels 20 to 45".
3. **Hits over a robed caster's strength.** The standard's examples stop at "twice over"; Albion needed "more than a
   robed caster's whole strength" (Beran, 1,200), "one and a half times" (Xanxicar's glare) and "two and a half times"
   (his stomp). Recommendation: keep them; they say exactly what the old numbers said.
4. **Bot lines that say "companions too".** Five Albion bot lines say it of mechanics that take every bot (Moran,
   Orylle, Krevo, Scurceol). The claim is true; the legends now add "the realm's adventurers". Recommendation: keep
   the bot lines short.
