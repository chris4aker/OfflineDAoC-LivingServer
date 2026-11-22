# Lore reachability (1.2.1)

Written by `python3 tools/lore/reachability.py --write`. For each visible lore field: the code that shows it, and
how many entries the engine can show. The rules are mirrored from the code (see the script's docstring);
**tests only**, nothing here was seen in game.

## Totals per realm

| Realm | entries shown | entries never shown | words shown | words never shown |
|---|---|---|---|---|
| Albion | 8,645 | 59 | 353,538 | 439 |
| Midgard | 9,504 | 83 | 337,526 | 724 |
| Hibernia | 8,629 | 31 | 308,458 | 265 |
| Neutral | 3,330 | 238 | 137,096 | 8,711 |
| All | 30,108 | 411 | 1,136,618 | 10,139 |

## Fields

| Kind | Field | Read by | entries shown | never shown |
|---|---|---|---|---|
| location | `summary` | the /lore codex, the loremasters' place list, the book's delve | 108 | 0 |
| location | `story` | the /lore codex, the loremasters | 108 | 0 |
| location | `legend` | the /lore codex ("What is said") | 108 | 0 |
| location | `chapters` | the /lore codex and the loremasters' chapter keywords | 284 | 0 |
| location | `loremaster` | the loremasters' intro and topics | 1,640 | 0 |
| location | `book` | lore books: the loremasters' sale and the codex (LoreBooks.Pages); drops near the place | 721 | 0 |
| location | `firstEntry` | LoreAtmosphere.Poll: LocationAt, or an overview's region entry | 503 | 0 |
| location | `areas.lines` | LoreAtmosphere.Poll: AreasInRegion and InArea | 2,538 | 0 |
| location | `raidForming` | LoreAtmosphere.RaidForming, from AutonomousRealmRaid when a realm raid forms | 5 | 0 |
| location | `botLines.lore` | LoreChat: the ambient lore corpus and placed lore | 1,354 | 0 |
| location | `botLines.rumour` | LoreChat: the ambient lore corpus and placed lore | 1,188 | 0 |
| location | `botLines.tell` | LoreChat.TryAnswer / LoreBible.TryAnswer ("whats in ...?") | 933 | 0 |
| location | `realmTakes` | each realm's codex, loremasters and bots (ForRealm, TakeFor) | 130 | 0 |
| boss | `story` | the /lore codex, the Bestiary, the loremasters | 578 | 0 |
| boss | `legend` | the codex and the Bestiary ("What the raid leaders say") | 578 | 0 |
| boss | `chapters` | the /lore codex and the loremasters' chapter keywords | 1,414 | 0 |
| boss | `loremasterTopic` | the loremasters' boss topics | 578 | 0 |
| boss | `bookPage` | its place's book (LoreBooks.Pages: "Of <boss>. ...") | 684 | 0 |
| boss | `chronicle` | the Herald's chronicle (LoreHerald) | 1,101 | 0 |
| boss | `yells` | LoreBossWatch: aggro on attack, death on dying, slay on a player's death | 3,926 | 176 |
| boss | `phases` | LoreBossWatch: health, adds, partner, event and mechanic triggers | 992 | 2 |
| boss | `lairEntry` | LoreAtmosphere.Poll: a found boss within 1,500 units | 1,077 | 0 |
| boss | `realmFirstKill` | LoreAtmosphere.FirstKill, from LoreFirstKills | 528 | 0 |
| boss | `wake` | LoreAtmosphere.DragonWakes (the three dragons) | 3 | 5 |
| boss | `botLines.briefing` | LoreChat: the raid leader's briefing | 2,149 | 0 |
| boss | `botLines.aftermath` | LoreChat: a veteran after the raid | 3,980 | 0 |
| boss | `botLines.rumour` | LoreChat: the ambient lore corpus; AutonomousTales | 2,151 | 0 |
| boss | `realmTakes` | each realm's codex, loremasters and bots | 27 | 0 |
| boss | `members.yells` | LoreBossWatch by the member's spawn name (1.2.1) | 232 | 0 |
| boss | `members.lairEntry` | LoreAtmosphere.Poll by the member's spawn name (1.2.1) | 76 | 0 |
| boss | `members.post` | nothing | 0 | 76 |
| boss | `members.story` | nothing | 0 | 76 |
| boss | `members.loremasterTopic` | nothing | 0 | 76 |
| history | `intro` | the /lore codex history and the loremasters' era answers | 4 | 0 |
| history | `eras` | the codex's eras and the loremasters (name, summary, text, events, facts' text) | 209 | 0 |
| history | `factions` | the codex's factions and the loremasters (name, summary, text) | 201 | 0 |

## Per realm and field (entries shown / never shown)

| Kind | Field | Albion | Midgard | Hibernia | Neutral |
|---|---|---|---|---|---|
| location | `summary` | 34 / 0 | 32 / 0 | 34 / 0 | 8 / 0 |
| location | `story` | 34 / 0 | 32 / 0 | 34 / 0 | 8 / 0 |
| location | `legend` | 34 / 0 | 32 / 0 | 34 / 0 | 8 / 0 |
| location | `chapters` | 96 / 0 | 88 / 0 | 70 / 0 | 30 / 0 |
| location | `loremaster` | 563 / 0 | 504 / 0 | 474 / 0 | 99 / 0 |
| location | `book` | 257 / 0 | 209 / 0 | 205 / 0 | 50 / 0 |
| location | `firstEntry` | 163 / 0 | 135 / 0 | 171 / 0 | 34 / 0 |
| location | `areas.lines` | 905 / 0 | 643 / 0 | 859 / 0 | 131 / 0 |
| location | `raidForming` | - | 5 / 0 | - | - |
| location | `botLines.lore` | 417 / 0 | 399 / 0 | 437 / 0 | 101 / 0 |
| location | `botLines.rumour` | 320 / 0 | 407 / 0 | 393 / 0 | 68 / 0 |
| location | `botLines.tell` | 252 / 0 | 306 / 0 | 324 / 0 | 51 / 0 |
| location | `realmTakes` | - | - | - | 130 / 0 |
| boss | `story` | 163 / 0 | 185 / 0 | 175 / 0 | 55 / 0 |
| boss | `legend` | 163 / 0 | 185 / 0 | 175 / 0 | 55 / 0 |
| boss | `chapters` | 446 / 0 | 408 / 0 | 326 / 0 | 234 / 0 |
| boss | `loremasterTopic` | 163 / 0 | 185 / 0 | 175 / 0 | 55 / 0 |
| boss | `bookPage` | 188 / 0 | 230 / 0 | 163 / 0 | 103 / 0 |
| boss | `chronicle` | 326 / 0 | 318 / 0 | 350 / 0 | 107 / 0 |
| boss | `yells` | 1096 / 58 | 1300 / 81 | 1105 / 28 | 425 / 9 |
| boss | `phases` | 234 / 0 | 364 / 0 | 270 / 2 | 124 / 0 |
| boss | `lairEntry` | 298 / 0 | 368 / 0 | 288 / 0 | 123 / 0 |
| boss | `realmFirstKill` | 163 / 0 | 135 / 0 | 175 / 0 | 55 / 0 |
| boss | `wake` | 1 / 1 | 1 / 2 | 1 / 1 | 0 / 1 |
| boss | `botLines.briefing` | 569 / 0 | 783 / 0 | 578 / 0 | 219 / 0 |
| boss | `botLines.aftermath` | 1062 / 0 | 1352 / 0 | 1135 / 0 | 431 / 0 |
| boss | `botLines.rumour` | 610 / 0 | 749 / 0 | 578 / 0 | 214 / 0 |
| boss | `realmTakes` | - | - | - | 27 / 0 |
| boss | `members.yells` | - | - | - | 232 / 0 |
| boss | `members.lairEntry` | - | - | - | 76 / 0 |
| boss | `members.post` | - | - | - | 0 / 76 |
| boss | `members.story` | - | - | - | 0 / 76 |
| boss | `members.loremasterTopic` | - | - | - | 0 / 76 |
| history | `intro` | 1 / 0 | 1 / 0 | 1 / 0 | 1 / 0 |
| history | `eras` | 43 / 0 | 60 / 0 | 52 / 0 | 54 / 0 |
| history | `factions` | 44 / 0 | 88 / 0 | 47 / 0 | 22 / 0 |

## What is left

- **Boss yells the script already speaks** (176 lines in 38 files): `LoreBossWatch` stays quiet on a trigger the boss's own script speaks on (`ScriptSpeaks`), so the two never talk over each other. These lines are codex-only. Wiring them means taking the script's own lines out, boss by boss: not small.
- **`wake` on five bosses that are not dragons** (beliathan, mokerkialfi, nosdoden, olcasgean, xanxicar; and olcasgean's two `wake` phase lines): only the three dragon scripts call `LoreAtmosphere.DragonWakes`. Each needs its own script hook (Beliathan's spawn when the princes die, Olcasgean's waking): not small.
- **Order members' post, story and loremaster topic** (76 members of chthonic-knights, inquisitors, lieutenants and mutilators): `LoreBible.Member` is parsed and checked but no channel reads it. Their yells and lair lines are spoken since 1.2.1. Showing the rest needs the lore channels' boss record to carry members (codex and loremaster topics).
- **Two books that never drop** (`proving-fields`, `shrouded-isles`): they list no regions, and the regions their areas name belong to the field and isle files, whose books drop there. Sold by the loremasters, so still readable.
- **`hibernia-wilds` and `hy-brasil`**: LORE.md said their first entry shows on coming into the open world, but zone files claim every zone of regions 200 and 181 in `Zones.xml`, so `LocationAt` never returned them. Since 1.2.1 they are overviews.

## Never shown, file by file

- **boss `members.loremasterTopic`**, read by no code (LoreBible.Member is only checked): 76 in 4 file(s): `chthonic-knights` (25), `inquisitors` (22), `lieutenants` (3), `mutilators` (26)
- **boss `members.post`**, read by no code (LoreBible.Member is only checked): 76 in 4 file(s): `chthonic-knights` (25), `inquisitors` (22), `lieutenants` (3), `mutilators` (26)
- **boss `members.story`**, read by no code (LoreBible.Member is only checked): 76 in 4 file(s): `chthonic-knights` (25), `inquisitors` (22), `lieutenants` (3), `mutilators` (26)
- **boss `phases`**, wake: only the three dragons call DragonWakes: 2 in 1 file(s): `olcasgean` (2)
- **boss `wake`**, only the three dragons call DragonWakes: 5 in 5 file(s): `beliathan` (1), `mokerkialfi` (1), `nosdoden` (1), `olcasgean` (1), `xanxicar` (1)
- **boss `yells`**, the script speaks on aggro itself (ScriptSpeaks): 124 in 38 file(s): `amphiptere` (3), `aroon-the-urlamhai` (3), `aros-the-spiritmaster` (3), `bellum` (3), `beran-the-supply-master` (3), `blight` (3), `cailleach-uragaig` (3), `cuuldurach-the-glimmer-king` (3), `easmarach` (3), `elder-council-birghir` (4), `elder-council-guthlac` (4), `elder-icelord-hjalmar` (4), `elder-icelord-suttung` (4), `fames` (3), `fornfrusenen` (4), `funus` (3), `gjalpinulva` (3), `gnat` (3), `golestandt` (3), `grand-summoner-govannon` (3), `hurika` (3), `icelord-agmundr` (4), `icelord-hakr` (4), `icelord-kvasir` (4), `king-tuscar` (3), `lich-lord-sanguis` (3), `lord-sanguis` (3), `melancholic-fairy-queen` (3), `mokkurvalve` (3), `morbus` (4), `morgana` (3), `myrddraxis` (3), `olcasgean` (3), `queen-kula` (3), `sister-blythe` (3), `torst` (4), `vortanos` (3), `xanxicar` (3)
- **boss `yells`**, the script speaks on death itself (ScriptSpeaks): 33 in 15 file(s): `aros-the-spiritmaster` (2), `bellum` (2), `easmarach` (2), `fames` (2), `fornfrusenen` (3), `funus` (2), `icelord-agmundr` (3), `icelord-kvasir` (3), `king-tuscar` (2), `lich-lord-sanguis` (2), `lord-sanguis` (2), `mokkurvalve` (2), `morbus` (2), `olcasar-geomancer` (2), `queen-kula` (2)
- **boss `yells`**, the script speaks on slay itself (ScriptSpeaks): 19 in 6 file(s): `cuuldurach-the-glimmer-king` (3), `gjalpinulva` (3), `golestandt` (3), `legion` (3), `nosdoden` (4), `oona` (3)

## First entries shown only as overviews (1.2.1)

`LoreEntryRules.LocationAt` returns these files for no zone, so before 1.2.1 their first-entry lines never showed.
They now show once a day on walking into a region they cover (`LoreEntryRules.OverviewsAt`):

- `albion-shrouded-isles` (4 lines), overview: regions 51
- `hibernia-wilds` (4 lines), overview: regions 200
- `hy-brasil` (5 lines), overview: regions 181
- `proving-fields` (4 lines), overview: regions 251, 252
- `shrouded-isles` (4 lines), overview: regions 151, 181

## Lore books that never drop

Every book is sold by the loremasters and read in the codex. `LoreBookDrops.Near` drops a place's book only in a
region the file lists, where it is the only file or near one of its areas (twice the radius; `z: 0` any height since
1.2.1). 106 of 108 books can drop; these 2 cannot:

`proving-fields`, `shrouded-isles`
