# NPC pass, phase 2: Midgard

Owner rulings: [HEROES.md](HEROES.md) decisions 10 and 11. Cast and voice: [CAST-MIDGARD.md](CAST-MIDGARD.md),
[VOICE-MIDGARD.md](VOICE-MIDGARD.md). Tests only: nothing here has been seen in game.

## What changed

- **Recurring voices (decision 11).** A real 1.65 NPC now stands in for a generic role in 19 scenes across
  17 files. Each one gets a habit, a stake or a consequence, not just a name swap:
  - **Sven**, skald trainer by the southern gates. 9 new mentions, 12 in all (at the cap). He puts only the dead in his
    songs.
    - `hati`, `mahattava`: he leaves out Brokk and the two warriors, who are living.
    - `soulsong`: he taught Soulsong his first verses.
    - `lydsyg`: he cannot find where the singer's note ends.
    - `icelord-kvasir`: he sang the horn song to a jarl.
    - `trollheim`: he keeps a verse of the payments on the Troll Father's promise.
    - `dodens-gruva`: he has made the verse of the shift's last reading and never sung it.
    - `gjalpinulva`: he stops Geirlaug's song one verse short.
    - `jordheim`: he gives the old-peace reading of the temple street.
  - **Grimma**, shaman trainer of the eastern hall. 4 new mentions, 6 in all. She reads the omens before a warband
    leaves and keeps the tally of the realm's dead.
    - `thorkatla`: the omens she read and never told.
    - `iarnvidiur`: Vemund's thirty, the one tally she was glad to leave short.
    - `dodens-gruva`: the grave Hel was cheated of.
    - `jordheim`: "only a matter of room", said with a straight face.
  - **Morlin Caan**, the smith by the temple. 3 new mentions, 6 in all.
    - `hurjavelen`: the thief-wolf's dagger was sold to him twice, and both times it went back to the wolf.
    - `fostra-orm`: he touches his forge's doorpost every morning.
    - `king-tuscar`: Tuscar's blade on his anvil, "the iron was still angry".
  - **Miri** (`uppland`), **Kalbin** (`vale-of-mularn`, Mularn's healer) and **Aud** (`east-svealand`) each take one
    local role.
  - Each file has an `original` fact that names its NPC and the roster source.
- **Continuity ledger** (`tools/lore/continuity.json`), four new entries:
  - `sven-names-only-the-dead`
  - `geirlaug-song-never-whole`: the skalds of the south now sing "the warband's part" of the song whole. Before, they
    sang the song "whole", which contradicted Malmohus's "nobody alive can sing all of it".
  - `grimma-keeps-the-tally`
  - `morlin-caan-forge-by-the-temple`
- **Housing** (`midgard-housing`): the Market Explorers came after 1.65. The lore now tells them as market criers "who
  know what every consignment merchant in the villages holds". The hidden fact notes the change.
- **Padded summaries (task 2):** none in Midgard. No epic `story` sits at 495-500 words. The longest is `jarl-ormarr`
  at 433, and Ydenia's 495 is not an epic. Nothing was trimmed.
- **Cuts:** a word-subsequence check of every visible string, old against new, found only these removed:
  - the role phrases the names replaced ("the skalds of Jordheim say", "the seers of Jordheim say", "the gothi say",
    "a Jordheim smith");
  - the Market Explorer words;
  - the renamed topic keys.

## The five best moments

1. **`soulsong`, story.**
   - Before: "...and the skalds of Jordheim say that is right, because a song made at the moment of a death is a binding"
   - After: "...and **Sven of Jordheim, who taught him his first verses at the southern gates**, says that is right,
     because a song made at the moment of a death is a binding"
2. **`gjalpinulva`, Hallbjorn's Warband.**
   - Before: "...the skalds of the south still sing it whole, though it takes most of a night."
   - After: "...still sing the warband's part of it whole, though it takes most of a night. In Jordheim **Sven** sings it
     on the longest night and stops one verse short, every year, because Hallbjorn did not hear the last verse either."
3. **`hurjavelen`, Gaut's chapter.**
   - Before: "Both times the one who took it sold it in Jordheim for more than its weight in silver..."
   - After: "...sold it in Jordheim, to **Morlin Caan** at the forge by the temple, ... The smith sold it on both times,
     and both times it was back on the wolf within the season; he will not buy it a third time."
4. **`hati`, story.**
   - Before: "The skalds of Jordheim say that if Hati ever catches the moon the howl will not stop..."
   - After: "**Sven of Jordheim** sings that ... the whole of Aegir will sleep. Brokk is not in the verse; the skald puts
     only the dead in his songs."
5. **`iarnvidiur`, chapter 7.**
   - Before: "...and told Jordheim the old one could not be fought by blades alone."
   - After: "...and told **Grimma** in Jordheim, who keeps the tally of the realm's dead, that the old one could not be
     fought by blades alone. It was the one tally of that winter she was glad to leave short."

## Renames (decision 11: tellers avoid gamebot first names)

All the names below are in `EraBotNames.cs`. Each new name was checked against the gamebot lists (`EraBotNames.cs`,
`make_test_world.py`), the companion names (`Hire165.cs`), the Realm Exchange brokers, the roster and the lore.

| Old | New | Who | Where |
|---|---|---|---|
| Loremaster Sigrun | **Loremaster Svanlaug** | the loremaster NPC | 33 Midgard intros, `summoners-hall`, `Loremasters.cs`, `UT_LoreBible.cs`, `docs/LORE.md` |
| Ragnhild of Galplen | **Geirlaug** | Hallbjorn's skald | `gjalpinulva`, `malmohus`, `myrkwood-forest`, the ledger |
| Hallveig | **Oddbjorg** | Trollheim's Valkyn guide | `trollheim`, `dagar`, `fostra-orm`, `rotodjur`, `trollkarl-bonchar` |
| Ingunn | **Thurid** | the skald home from Eyvind's warband | `king-tuscar`, `queen-kula` |
| Herdis | **Vilborg** | the net-maker of Knarr, book writer | `munin-sound` (topic key too) |
| Thorunn | **Tofa** | the alewife of Mularn, book writer | `vale-of-mularn` (topic key too) |
| Ingvild | **Asvor** | the scribe of Bragi's house | `jordheim` (topic key too) |

The valkyrie Sigrun, who founded the order with Brynhild in Mythic's own text, keeps her name.

## For the owner

- **Sven** is an approved voice (decision 11), but "Sven" is also a gamebot first name (`EraBotNames.MaleFirstNames`)
  and a suggested companion name (`Hire165.cs`). Both conflict with decision 7 and with the teller rule. He is kept
  because the owner named him. Should the companion list drop Sven?
- Not renamed: Midgard names that gamebots share but that belong to characters in the stories, not to tellers.
  - Dead or legend figures: Hrafn the wind-rider, Thordis, Jorunn, Sigrid of Sundby, Astrid of the Circle, Ottar,
    Ketil, Thorvald, Orm in Fostra Orm.
  - Real 1.65 NPCs: Vigdis, Elin, Frode, Leif, Gudrun of Svarhamr.
  - Others: Skeggi of Mularn (`vendo-caverns`) and the sentry Steinar (`hurjavelen`) are one-file witnesses. Rename
    them too?
- Ragnhild (now Geirlaug) was already over 12 mentions as an untitled teller (exempt, decision 11). She got no new
  mentions.
