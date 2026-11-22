# NPC pass, phase 2: Hibernia

Owner rulings: [HEROES.md](HEROES.md) decisions 10 and 11. Branch `wt9/npc-hib`. Text was only added or reworded. The
only words removed are the role stand-ins a name replaced ("the druids of Tir na Nog say", "the trainers of Tir na
Nog", "the smith in Mag Mell"), "Brother" before Esras, and "market explorer". A word-subsequence check of every old
visible string against the new one, with the renames mapped, found nothing else cut. Tests only: no in-game check.

## What changed

- **The three approved voices now recur** (decision 11). Each is a real NPC from `tools/lore/npc_roster.json`, and each file
  they appear in has an `original` fact naming them and their source.
  - **Daray**, the druid trainer of Tir na Nog: 12 mentions in 9 files. Daray is the capital's doubter who tests things:
    a question for every class, the sap jar on the teaching stone, the rain records. Daray argues with Lisín and Nóra. No
    pronoun is used, because the capture leaves Daray's sex unset.
  - **Lasairiona**, the champion trainer: 7 mentions in 5 files. She is the plain fighter beside the druids' theories.
    She counts her champions out by name, sends them in pairs and starts them at Ilisa's anvil.
  - **Ilisa**, the smith of Mag Mell: 4 mentions in 4 files. She reads what comes home: the bridge blades soft from the
    edge inward, and Nogoribando's dents.
- **Renames** (decision 11, task 4) are listed below. **Esras** is no longer a churchman, and the housing's **market
  explorer**, a post-1.65 NPC, is now a market crier.
- **Continuity ledger**: `uallach-hundred-and-forty` (9 files) and `esras-no-churchman` (2 files).
- **Padded summaries (task 2): none in Hibernia.** No Hibernian epic story sits at 495-500 words. The longest is
  `corrupt-primal-of-fire` at about 471 words, after Ilisa's sentence went in. None was rewritten or trimmed.

## The 5 best moments

1. **`locations/marfach-caverns`, chapter.** Daray and Nóra face each other.
   - Before: The druids of Tir na Nog say Marfach wants nothing, because a door does not want anything; it is opened or
     it is shut. The keeners' daughters say that is a druid's answer, and that the hill wants...
   - After: **Daray of Tir na Nog** says Marfach wants nothing, because a door does not want anything; it is opened or it
     is shut. **Nóra**, who said it in the grove to the druid's face, says that is a druid's answer, and that the
     hill wants...
2. **`bosses/nogoribando`, story.** The oak and the hornet, as a smith reads them.
   - Added after "one blow is enough": **Ilisa of Mag Mell** can tell from a shield which of the two it met: the oak
     leaves one dent the size of a fist, the hornet a hundred the size of a thumbnail.
3. **`bosses/tine`, chapter.** A theory meets a trainer who knows the survivor.
   - Before: The druids of Tir na Nog think that is fear talking.
   - After: **Daray**, in the druid grove of the capital, thinks that is fear talking. **Lasairiona**, who trained one of
     the two, says that champion was never afraid of anything in her life, and has not sent her down again.
4. **`locations/galladoria`, chapter.** A habit that makes the realm's not-knowing visible.
   - Before: The druids of Tir na Nog have tried to understand the sap and have come no further than the Sylvan. They
     say it is the world soul gone wrong...
   - After: **Daray** keeps a stoppered jar of the sap on the teaching stone of the druid grove in Tir na Nog, and every
     young druid of the city has tried to understand it and come no further than the Sylvan. The druid trainer says it
     is the world soul gone wrong...
5. **`locations/cursed-forest`, story.** It replaces a "Nobody ..." opening (VOICE-HIBERNIA.md).
   - Before: Nobody in Tir na Nog expects to live to see it.
   - After: **Lasairiona**, who sends her champions into the wood by name and counts them out of it by name, does not
     expect to live to see it, and tells every one of them so at the gate.

Also changed:
- `balor`: Daray asks each new class which came first, the king or the eye.
- `easmarach`: Daray asks Lisín for the Underneath on a Sylvan map.
- `beatha`: the shoot is the first thing the grove's pupils are taught to doubt.
- `corrupt-primal-of-air`: the rain records, read out every midsummer.
- `tur-suil`: the tower as Daray's unanswered question.
- `lough-derg`: Lasairiona's pairs at Nock, one of them to watch Aziliz; Ilisa wants the silver.
- `faerie-queen`: the trainers' road starts at Ilisa's anvil.
- `corrupt-primal-of-fire`: Ilisa reforges a bridge blade but never regrinds it.

## Renames

These lore tellers shared a gamebot first name (`EraBotNames.cs`). The new names were checked against the gamebot
lists, the `Hire165.cs` companions, `name_counts.legend_names()`, the roster and the existing lore: none clashes. They
were renamed in every lore file and in the chat `.lines`.

| Old | New | Who | Mentions |
|---|---|---|---|
| Orlaith (of Tir na Nog, of Necht) | **Uallach** | led the hundred and forty into Galladoria | 51, 16 files |
| Fiachra of Howth | **Flann of Howth** | went into Fomor; the room with the hooks | 33, 8 files |
| Eoghan the shepherd (and his grandmother and wife) | **Cuán** | Sheeroe's shepherd-teller | 19, 7 files |
| Ruadhán | **Ruarc** | the warden of the storm height | 19, 3 files |
| Tadhg Rua | **Faílbe Rua** | the Geomancer's first band; the barracks hammer | 13, 2 files |
| Fergal | **Dubhán** | the herder's boy who counted the glimmers | 10, 2 files |
| Cathal of Howth | **Cerball of Howth** | the Black Lady's and the Oak's company | 10, 2 files |
| Lorcán of Tir na Nog | **Muiredach** | the Uaimh party, three of eleven | 9, 1 file |
| Ailbhe | **Cellach** | Ruarc's friend, killed by Beatha | 7, 1 file |
| Senan of Ardee | **Ségán of Ardee** | the straight line across the fire bridge | 5, 1 file |
| Brother Esras | **Esras the hermit** | once a bard of Tir Urphost who gave up the benches for the wind | 6, 2 files |

These names stay as they are:
- **Real NPCs that share a gamebot name:** Eoghan of Lough Derg ("Eoghan's Spell"), Chieftess Niamh, Donnchadh, Tadhg of
  Connla, Gemma, and the trainers Aodh and Blathnaid.
- **Legends:** Oisín, Niamh of the Golden Hair, Eithne, Cormac Conn Longas.
- **Others:** the boss Aoife, and Áine (an allowed shared given name).
- **Left for the Neutral writer:** "Cathal of the long spear" in `duke-bimure` (a Neutral file).

## For the owner

- Esras's fact keeps a note that the game's NPC is titled "Brother". The visible lore drops the title.
- Daray is at the cap of 12 in Hibernian files. If the Neutral pass names Daray in shared places (NPC-PASS-SAMPLE N1),
  the combined count goes over 12.
- CAST-HIBERNIA.md still uses the old teller names. Read them through the table above.
- `tools/bot-voices/vocabulary.txt` gained only `flann` and `uallach`. `--write-vocabulary` would also have pruned 127
  unused words, so it was not used.
