# Repetition in the lore (1.2.1)

Written by `python3 tools/lore/repetition.py --write`; the ratchet is `--check` against
`tools/lore/repetition_caps.json` (see the script). Words are counted over the visible text only. A phrase is a run of
6 or more words in 3 or more files; its count is how often it appears. Nothing here is rewritten: this is a
list for the writers (owner: never trim; reword by adding).

## Summary

| Realm | narrative words | in repeated phrases | top page opening |
|---|---|---|---|
| Albion | 283,806 | 7,785 (2.7%) | "Of the" 95 of 682 pages |
| Midgard | 265,348 | 6,874 (2.6%) | "From the" 90 of 655 pages |
| Hibernia | 237,722 | 8,618 (3.6%) | "From Derbáil" 27 of 532 pages |
| Neutral | 120,991 | 3,932 (3.2%) | "From the" 21 of 277 pages |

## Narrative phrases (stories, books, chapters, loremasters, atmosphere, history)

1063 phrases recur in 3+ files; the top 40:

| # | Phrase | files | times | by realm | e.g. |
|---|---|---|---|---|---|
| 1 | the druids of tir na nog | 48 | 78 | Hib 58, Neu 20 | `abomos-the-soultrapper`, `anurigunda`, `avartack-the-champion` |
| 2 | is the only one of the | 26 | 29 | Hib 13, Alb 6, Neu 6, Mid 4 | `anhylaw`, `avartack-the-champion`, `azure-coseal` |
| 3 | the druids of tir na nog say | 24 | 30 | Neu 16, Hib 14 | `beatha`, `behemoth`, `beliathan` |
| 4 | the wider country it belongs to | 21 | 21 | Hib 21 | `allta-fearann`, `bog-of-cullen`, `breifine` |
| 5 | at the far end of the | 20 | 26 | Mid 16, Alb 8, Neu 1, Hib 1 | `basilius`, `delegalt`, `drihten-elreden` |
| 6 | the druids of lough gur say | 19 | 29 | Hib 29 | `ancient-black-oak`, `anurigunda`, `avartack-the-champion` |
| 7 | from coll of the domnann guides | 19 | 19 | Hib 19 | `adar-llwch-gwin`, `archilus`, `balere` |
| 8 | for the rest of his life | 17 | 19 | Hib 8, Mid 7, Alb 3, Neu 1 | `abomos-the-soultrapper`, `corrupt-primal-of-air`, `corrupt-primal-of-fire` |
| 9 | he is the only one of | 15 | 16 | Hib 8, Alb 4, Mid 4 | `anhylaw`, `avartack-the-champion`, `azure-coseal` |
| 10 | the feud of the blade and the hook | 14 | 26 | Neu 26 | `chthonic-knights`, `director-kobil`, `duke-alloc` |
| 11 | from the readings of brisa the beam cutter of svarhamr | 14 | 14 | Mid 14 | `arnulv`, `bjornvarg`, `desukva` |
| 12 | from what ivrik the kobold lamp carrier of bjarken told the skalds | 14 | 14 | Mid 14 | `alchemist-phorzbreg`, `apologet-nesnuir`, `grindvaktare` |
| 13 | the first company of albion to | 13 | 14 | Alb 13, Neu 1 | `apocalypse`, `beran-the-supply-master`, `deben-se-gecynde` |
| 14 | from derbáil of ardee's list the | 13 | 13 | Hib 13 | `badb`, `donal`, `faerie-queen` |
| 15 | the bards of tir na nog | 11 | 14 | Hib 7, Neu 6, Mid 1 | `balor`, `caisha`, `duke-bimure` |
| 16 | is the only one of them | 11 | 11 | Mid 5, Alb 4, Hib 2 | `dagar`, `deniela`, `funus` |
| 17 | the druids of tir na nog say the | 11 | 11 | Hib 6, Neu 5 | `corrupt-primal-of-fire`, `grand-summoner-govannon`, `high-lord-baln` |
| 18 | at the top of the stair | 10 | 18 | Neu 9, Hib 5, Alb 3, Mid 1 | `apocalypse`, `aros-the-spiritmaster`, `fates-chosen` |
| 19 | on the night of the rising | 10 | 17 | Alb 17 | `bellum`, `fates-chosen`, `funus` |
| 20 | the drovers of the cornish road | 10 | 13 | Alb 13 | `borimo`, `brannoc`, `draco-magnificens` |
| 21 | noirin the enchanter of altan garran | 10 | 12 | Hib 12 | `aodhnait`, `cuinn`, `elnara-erea` |
| 22 | she is the only one of | 10 | 12 | Neu 5, Hib 3, Mid 2, Alb 2 | `deniela`, `gjalpinulva`, `inquisitors` |
| 23 | for a long time and then | 10 | 11 | Mid 7, Hib 2, Neu 1, Alb 1 | `atur`, `council-nokkvi`, `duke-alloc` |
| 24 | from the ledger of little haven kept by mordwywr | 10 | 10 | Alb 10 | `amphiptere`, `clan-matron`, `crypt-guardian` |
| 25 | why does it matter because the | 10 | 10 | Hib 10 | `elro-the-ancient`, `bog-of-cullen`, `cliffs-of-moher` |
| 26 | why it matters to albion the | 10 | 10 | Alb 10 | `albion-housing`, `black-mountains-north`, `black-mountains-south` |
| 27 | in the first summer of the | 9 | 14 | Hib 5, Neu 5, Alb 4 | `aodhnait`, `beonoc-dyer`, `cabalist-meical` |
| 28 | in the second summer of the | 9 | 13 | Alb 7, Hib 5, Neu 1 | `clan-matron`, `deben-se-gecynde`, `draargus-the-mighty` |
| 29 | the one in front of him | 9 | 13 | Mid 13 | `boligar`, `council-ozur`, `council-vagn` |
| 30 | from coll of the domnann guides the | 9 | 9 | Hib 9 | `adar-llwch-gwin`, `beomarbhan`, `dremcis-fuiloltair` |
| 31 | from derbáil of ardee's list grandmother | 9 | 9 | Hib 9 | `blight`, `cailean`, `flame` |
| 32 | from fenrir's folly as told in galplen by thyri | 9 | 9 | Mid 9 | `hagall-the-red-dagger`, `hugi`, `kelic` |
| 33 | from the trap lines of solvi of hagall | 9 | 9 | Mid 9 | `bokko`, `dalicren`, `gorp` |
| 34 | the only one of them who | 9 | 9 | Mid 5, Alb 2, Hib 1, Neu 1 | `captain-heathyr`, `dagar`, `deniela` |
| 35 | the skalds of jordheim say the | 9 | 9 | Mid 7, Neu 2 | `gjalpinulva`, `grand-summoner-govannon`, `hati` |
| 36 | the fishermen of the avalon shore | 8 | 14 | Alb 14 | `draargus-the-mighty`, `fuladl`, `fulafeallan` |
| 37 | the knight commander of the isle of glass | 8 | 13 | Alb 13 | `fulafeallan`, `jehrog-se-bysen`, `krevo-ricik` |
| 38 | the cast out sculptors at dellingstad | 8 | 11 | Mid 11 | `council-otrygg`, `council-ozur`, `council-vagn` |
| 39 | the druids of tir na nog believe | 8 | 10 | Hib 10 | `balor`, `black-lady`, `corrupt-primal-of-fire` |
| 40 | the sylvan of the western coast | 8 | 10 | Hib 10 | `abomos-the-soultrapper`, `balor`, `kierac-the-destroyer` |

## Bot-line phrases

76 phrases recur in 3+ files; the top 40:

| # | Phrase | files | times | by realm | e.g. |
|---|---|---|---|---|---|
| 1 | a fight for the realm's best | 22 | 22 | Mid 22 | `arnulv`, `bjornvarg`, `desukva` |
| 2 | job for our best comes at you from 17 paces | 12 | 12 | Alb 12 | `belgrik`, `beonoc-dyer`, `borimo` |
| 3 | job killing of 15 of us | 9 | 9 | Alb 9 | `legendary-afanc`, `mouth`, `qanris-duros` |
| 4 | two fifths of a robed caster | 8 | 8 | Mid 6, Alb 2 | `chief-tarek`, `cronk`, `issorden` |
| 5 | down one less on the watch | 7 | 8 | Alb 8 | `captain-atwell`, `captain-heathyr`, `lieutenant-elva` |
| 6 | is a fight for the realm's best | 7 | 7 | Mid 7 | `fjall`, `gorp`, `igo` |
| 7 | melee full band of our best | 7 | 7 | Alb 7 | `cabalist-meical`, `crypt-guardian`, `heavy-metal-mark-i` |
| 8 | a fight for the realm's best no tricks | 6 | 6 | Mid 6 | `eildird`, `grindvaktare`, `hrolleif` |
| 9 | champions war party job killing of | 6 | 6 | Alb 6 | `green-knight`, `legendary-afanc`, `risnir-cruss` |
| 10 | back 80 sec after he dies | 5 | 5 | Alb 5 | `beonoc-dyer`, `borimo`, `chieftain-toca` |
| 11 | back 80 sec after it falls | 5 | 5 | Alb 5 | `heavy-metal-mark-i`, `pazia`, `throttler` |
| 12 | could take a band of our best | 5 | 5 | Alb 5 | `adelon`, `huidelon-cariston`, `rose` |
| 13 | no spells melee full band of our best | 5 | 5 | Alb 5 | `cabalist-meical`, `crypt-guardian`, `heavy-metal-mark-i` |
| 14 | several bands job killing of 15 of us | 5 | 5 | Alb 5 | `mouth`, `qanris-duros`, `sarcondina` |
| 15 | someone dragged him off his post again untouchable out there | 5 | 5 | Alb 5 | `captain-atwell`, `lieutenant-meade`, `lieutenant-saxe` |
| 16 | want several bands of our best | 5 | 5 | Hib 4, Alb 1 | `allta-fearann`, `bog-of-cullen`, `cothrom-gorge` |
| 17 | for a seasoned group in the | 4 | 8 | Hib 8 | `connacht`, `lough-gur`, `silvermine-mountains` |
| 18 | for a seasoned group on the | 4 | 6 | Hib 6 | `cliffs-of-moher`, `shannon-estuary`, `silvermine-mountains` |
| 19 | in the hall of the corrupt | 4 | 6 | Alb 5, Neu 1 | `lieutenant-elva`, `lieutenant-meade`, `hall-of-the-corrupt` |
| 20 | a match for our best and | 4 | 4 | Alb 4 | `chieftain-toca`, `huidelon-cariston`, `veena` |
| 21 | back 80 sec after he drops | 4 | 4 | Hib 4 | `manus`, `treal`, `tudeg` |
| 22 | champions war party job killing of 15 of us | 4 | 4 | Alb 4 | `legendary-afanc`, `risnir-cruss`, `spirit-of-lord-emthoro` |
| 23 | dragged him and he went home | 4 | 4 | Alb 3, Hib 1 | `lich-lord-ilron`, `sargan-the-conqueror`, `silencer` |
| 24 | he hits harder than he looks | 4 | 4 | Mid 2, Hib 2 | `hamar`, `jango`, `oscar` |
| 25 | he hits way harder than he looks | 4 | 4 | Alb 2, Mid 1, Hib 1 | `belgrik`, `draco-magnificens`, `masher` |
| 26 | is one of the four royals for beliathan | 4 | 4 | Neu 4 | `prince-abdin`, `prince-asmoien`, `prince-baalorien` |
| 27 | job for several bands of our best | 4 | 4 | Alb 4 | `draco-magnificens`, `durval`, `fester` |
| 28 | of a robed caster in cold | 4 | 4 | Mid 4 | `icelord-steinvor`, `issorden`, `raumarik-revenant` |
| 29 | of the realm's porters for battlegrounds once youre | 4 | 4 | Neu 4 | `abermenai`, `caledon`, `murdaigean` |
| 30 | one of the great ones takes | 4 | 4 | Mid 4 | `jarala-den-kvinnlig`, `master-smed`, `olen-den-bartender` |
| 31 | only as tough as two or three of us | 4 | 4 | Alb 4 | `lieutenant-elva`, `lieutenant-saxe`, `sergeant-darryn` |
| 32 | someone dragged her off her post again untouchable out there | 4 | 4 | Alb 4 | `captain-bardalph`, `captain-heathyr`, `lieutenant-elva` |
| 33 | two fifths of a robed caster in | 4 | 4 | Mid 4 | `cronk`, `issorden`, `raumarik-revenant` |
| 34 | cure poison on the shield bearer | 3 | 4 | Mid 2, Alb 2 | `fostra-orm`, `sarcondina`, `skoll` |
| 35 | the gap east of aalid feie | 3 | 4 | Hib 4 | `adwyth`, `drygau`, `vale-of-balor` |
| 36 | a few steps short of our best | 3 | 3 | Alb 3 | `rose`, `soulfear`, `trahern` |
| 37 | a fight for the realm's best with | 3 | 3 | Mid 3 | `fjall`, `khrin`, `luso` |
| 38 | a full band of our best | 3 | 3 | Alb 2, Hib 1 | `azure-dsaldor`, `cirfans`, `draco-magnificens` |
| 39 | a troll father walked into jordheim | 3 | 3 | Mid 3 | `aegirs-landing`, `jordheim`, `trollheim` |
| 40 | ask one of the realm's porters for battlegrounds once youre | 3 | 3 | Neu 3 | `caledon`, `murdaigean`, `thidranki` |

## Fight-advice phrases (the set strength tiers are meant; listed for reading, not capped)

707 phrases recur in 3+ files; the top 40:

| # | Phrase | files | times | by realm | e.g. |
|---|---|---|---|---|---|
| 1 | a foe for a hardened band of veterans | 191 | 191 | Hib 70, Mid 59, Alb 56, Neu 6 | `abbadona`, `ailbe`, `ajniollia` |
| 2 | a foe for the realm's best | 190 | 198 | Mid 71, Alb 65, Hib 59, Neu 3 | `adar-llwch-gwin`, `adelon`, `adwyth` |
| 3 | a foe for the realm's best a full band of them | 147 | 147 | Mid 61, Alb 46, Hib 37, Neu 3 | `adar-llwch-gwin`, `adelon`, `agidad` |
| 4 | a foe for a war party of | 116 | 124 | Alb 56, Neu 28, Mid 27, Hib 13 | `aidon-the-archwizard`, `amphiptere`, `bane-of-hope` |
| 5 | a foe for a war party of the realm's champions | 89 | 96 | Alb 56, Mid 27, Hib 13 | `aidon-the-archwizard`, `amphiptere`, `bane-of-hope` |
| 6 | a foe for several bands of the realm's best together | 89 | 91 | Alb 43, Mid 35, Hib 13 | `albion-leader`, `alchemist-phorzbreg`, `amphiptere` |
| 7 | as much killing as fifteen raiders | 64 | 64 | Alb 36, Mid 25, Hib 2, Neu 1 | `amphiptere`, `ancient-syver`, `basilius` |
| 8 | a foe for a hardened band of veterans is | 57 | 57 | Hib 49, Alb 8 | `ailbe`, `amadan`, `ansol` |
| 9 | as much killing as fifty raiders | 46 | 46 | Mid 20, Alb 16, Neu 9, Hib 1 | `bane-of-hope`, `beliathan`, `bellum` |
| 10 | no charm or stun holds him | 44 | 44 | Mid 37, Neu 7 | `basilius`, `boligar`, `council-hord` |
| 11 | he takes as much killing as | 43 | 43 | Mid 37, Neu 6 | `basilius`, `boligar`, `council-nokkvi` |
| 12 | a band of the realm's best | 42 | 44 | Hib 18, Alb 16, Mid 10 | `adar-llwch-gwin`, `agidad`, `alivor` |
| 13 | each a foe for the realm's best | 41 | 41 | Mid 17, Alb 14, Hib 9, Neu 1 | `apologet-nesnuir`, `azure-dsaldor`, `bayn` |
| 14 | is a foe for a war party of the realm's champions | 39 | 39 | Alb 22, Mid 14, Hib 3 | `aidon-the-archwizard`, `bane-of-hope`, `beran-the-supply-master` |
| 15 | charms trances and stuns hold him | 35 | 35 | Mid 35 | `albion-leader`, `alchemist-phorzbreg`, `alivor` |
| 16 | tricks and charms trances and stuns hold | 33 | 33 | Mid 33 | `albion-leader`, `alchemist-phorzbreg`, `alivor` |
| 17 | tenths of every spell are lost on | 32 | 32 | Mid 32 | `ancient-syver`, `basilius`, `chief-tarek` |
| 18 | a foe for a hardened band of veterans is a | 31 | 31 | Hib 30, Alb 1 | `ailbe`, `aoife`, `arracht` |
| 19 | takes as much killing as fifty raiders | 31 | 31 | Mid 20, Neu 9, Alb 2 | `bane-of-hope`, `beliathan`, `council-hord` |
| 20 | a foe for a hardened band of veterans in | 30 | 30 | Mid 22, Alb 8 | `beskydda-grundar`, `danin`, `eiorharn` |
| 21 | takes as much killing as fifteen raiders | 30 | 30 | Mid 25, Alb 3, Hib 2 | `amphiptere`, `ancient-syver`, `basilius` |
| 22 | and a foe for a hardened band of veterans | 28 | 28 | Alb 28 | `angau`, `bayn`, `bevvny` |
| 23 | a foe for a war party of any realm's champions | 27 | 28 | Neu 28 | `duke-alloc`, `duke-aypol`, `duke-bimure` |
| 24 | no spells or styles by every account | 27 | 27 | Alb 27 | `agidad`, `beonoc-dyer`, `borimo` |
| 25 | no tricks he returns eighty seconds after he falls | 25 | 25 | Hib 25 | `amadan-dubh`, `amadan`, `ansol` |
| 26 | a foe for a war party of the realm's champions and | 24 | 25 | Alb 22, Mid 2, Hib 1 | `aidon-the-archwizard`, `cailleach-uragaig`, `coliath` |
| 27 | a foe for several bands of the realm's best together and | 24 | 24 | Alb 21, Mid 2, Hib 1 | `amphiptere`, `bokko`, `captain-bardalph` |
| 28 | rises again about eighty seconds after | 24 | 24 | Alb 23, Hib 1 | `agidad`, `beonoc-dyer`, `borimo` |
| 29 | a foe for a war party of the realm's champions as much killing as | 22 | 22 | Alb 22 | `bellum`, `beran-the-supply-master`, `crypt-lord` |
| 30 | fifths of a robed caster's strength | 22 | 22 | Alb 11, Mid 7, Hib 4 | `blue-lady`, `chief-tarek`, `council-vagn` |
| 31 | tenths of every spell are lost on him | 22 | 22 | Mid 22 | `basilius`, `council-otrygg`, `dalgo` |
| 32 | a fifth of every blow and three tenths of every spell are lost on | 20 | 20 | Mid 20 | `ancient-syver`, `basilius`, `chief-tarek` |
| 33 | no special tricks and charms trances and stuns hold | 20 | 20 | Mid 20 | `albion-leader`, `alchemist-phorzbreg`, `alivor` |
| 34 | a foe for the realm's best a full band of them in | 19 | 19 | Mid 12, Hib 5, Alb 2 | `atur`, `borimo`, `ixthiar-broodmother` |
| 35 | a foe for several bands of the realm's best together and as | 18 | 18 | Alb 18 | `amphiptere`, `captain-bardalph`, `captain-heathyr` |
| 36 | and as much killing as fifteen raiders | 18 | 18 | Alb 18 | `captain-bardalph`, `captain-heathyr`, `durval` |
| 37 | companions and the realm's adventurers as | 18 | 18 | Alb 11, Hib 4, Mid 3 | `bane-of-hope`, `crypt-lord`, `cuuldurach-the-glimmer-king` |
| 38 | no spells or styles by every account a | 18 | 18 | Alb 18 | `agidad`, `cabalist-meical`, `chieftain-toca` |
| 39 | a match for the realm's best | 17 | 19 | Mid 13, Alb 6 | `bjornvarg`, `clan-matron`, `geirolf` |
| 40 | a foe for a seasoned band | 17 | 17 | Alb 7, Mid 6, Neu 3, Hib 1 | `captain-gruff`, `cargila`, `gnat` |

## Page openings

The first two words of every book page and chapter, per realm (the six most used):

| Realm | pages | first word "From" | openings |
|---|---|---|---|
| Albion | 682 | 78 (11%) | "Of the" 95; "From the" 72; "In the" 17; "The first" 15; "On the" 13; "To the" 9 |
| Midgard | 655 | 162 (25%) | "From the" 90; "In the" 19; "From what" 14; "When the" 13; "From Thorgrim" 10; "At the" 10 |
| Hibernia | 532 | 74 (14%) | "From Derbáil" 27; "The Sylvan" 24; "From Coll" 19; "The first" 18; "Of the" 15; "On the" 11 |
| Neutral | 277 | 56 (20%) | "From the" 21; "From a" 19; "In the" 11; "From The" 9; "When the" 8; "Of the" 7 |

## Set formulas

Count per realm in narrative text (per 10,000 words in brackets):

| Formula | Albion | Midgard | Hibernia | Neutral | e.g. |
|---|---|---|---|---|---|
| teller's list ("From X's list") | - | - | 27 (1.14) | - | `badb`: "From Derbáil of Ardee's list"; `blight`: "From Derbáil of Ardee's list" |
| "From the readings / ledger / trap lines of" | 17 (0.6) | 29 (1.09) | 1 (0.04) | 3 (0.25) | `agidad`: "From the count of"; `amphiptere`: "From the Ledger of" |
| "From what X told" | - | 15 (0.57) | - | - | `alchemist-phorzbreg`: "From what Ivrik the kobold lamp-carrier of Bjarken told"; `apologet-nesnuir`: "From what Ivrik the kobold lamp-carrier of Bjarken told" |
| "as told in" | - | 10 (0.38) | 1 (0.04) | - | `hagall-the-red-dagger`: "as told in"; `hugi`: "as told in" |
| "the druids / bards / skalds of ... say" | 16 (0.56) | 36 (1.36) | 58 (2.44) | 32 (2.64) | `ancient-black-oak`: "the druids of Lough Gur say"; `anurigunda`: "The druids of Lough Gur say" |
| "the wider country it belongs to" | - | - | 21 (0.88) | - | `allta-fearann`: "the wider country it belongs to"; `bog-of-cullen`: "the wider country it belongs to" |
| "why it matters" / "why does it matter" | 27 (0.95) | - | 18 (0.76) | - | `elro-the-ancient`: "Why does it matter"; `albion-housing`: "Why it matters" |
| "whence the name" | - | - | 2 (0.08) | - | `ancient-black-oak`: "whence the name"; `balor`: "whence the name" |
| "is the only one of" | 11 (0.39) | 9 (0.34) | 16 (0.67) | 7 (0.58) | `anhylaw`: "is the only one of"; `avartack-the-champion`: "is the only one of" |
| "for the rest of his/her life" | 3 (0.11) | 11 (0.41) | 12 (0.5) | 1 (0.08) | `abomos-the-soultrapper`: "for the rest of his life"; `corrupt-primal-of-air`: "for the rest of his life" |
| "the way (s)he said everything" | - | 1 (0.04) | - | - | `wretch-of-winter`: "the way she said everything" |
| "they do not say how" | - | 1 (0.04) | 1 (0.04) | 1 (0.08) | `gnat`: "they do not say why"; `iarnvidiurs-lair`: "they do not say how" |
| "to this day" | 2 (0.07) | 2 (0.08) | 4 (0.17) | 4 (0.33) | `chieftain-caimheul`: "to this day"; `chthonic-knights`: "to this day" |
| "never once" | 79 (2.78) | 51 (1.92) | 31 (1.3) | 38 (3.14) | `adelon`: "never once"; `amminus-pilus`: "never once" |
| "no one / nobody knows" | 17 (0.6) | 34 (1.28) | 18 (0.76) | 7 (0.58) | `agidad`: "nobody knows"; `chthonic-knights`: "nobody knows" |
| "it is said" | 2 (0.07) | 5 (0.19) | - | 1 (0.08) | `aros-the-spiritmaster`: "it is said"; `council-nokkvi`: "It is said" |
| "for a long time, and then" | 1 (0.04) | 7 (0.26) | 2 (0.08) | 1 (0.08) | `atur`: "for a long time, and then"; `council-nokkvi`: "for a long time and then" |
| "at the far end of the" | 8 (0.28) | 15 (0.57) | 1 (0.04) | 1 (0.08) | `basilius`: "at the far end of the"; `delegalt`: "at the far end of the" |
