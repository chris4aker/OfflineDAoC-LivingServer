# Lore: the lore bible and how it reaches players (Living Server 1.2)

Every dungeon, epic dungeon, dragon, raid location and named boss gets a story: Mythic's where it exists, new Living
Server writing where it doesn't, with the source of every fact marked. The stories live in one place, the **lore bible**,
and several channels show them: screen-centre atmosphere lines, boss yells, bot chat (briefings, after-raid tales,
rumours, answers), and (from the loremaster and Herald work) a loremaster NPC, books and a chronicle.

This page describes the data format, the checks every file must pass, and the engine. Nothing here has been played in
game yet; the automated tests are listed at the end.

## The files

One JSON file per location and per boss, under
`source/server/GameServer/bots/autonomous/data/lore/`:

- `locations/<location-id>.json`
- `bosses/<boss-id>.json`

They are built into `GameServer.dll` (`GameServer.csproj` embeds `data/lore/**/*.json`, as it embeds the chat corpus),
so a server build carries them; nothing goes into the world database and no release builder step is needed.

IDs are the English name in lower-case kebab: `krondon`, `scurceol-hyrde`, `cuuldurach-the-glimmer-king`. The file name
is the id.

### Location

```json
{
  "id": "krondon", "name": "Krondon", "realm": "Albion|Midgard|Hibernia|Neutral", "regions": [61],
  "zones": [204],
  "aliases": ["the hall under the hill"],
  "summary": "one sentence for lists",
  "facts": [{"text": "...", "provenance": "mythic|game|legend|myth|original", "source": "url or file"}],
  "story": "150-400 words",
  "legend": "what players say, 40-120 words",
  "atmosphere": {
    "firstEntry": ["<=60 chars"],
    "areas": [{"name": "the throne room", "region": 61, "x": 0, "y": 0, "z": 0, "radius": 600, "lines": ["<=60 chars"]}],
    "raidForming": ["<=60 chars"]
  },
  "botLines": {"lore": ["..."], "rumour": ["..."], "tell": ["answers to 'what's in <place>?'"]},
  "loremaster": {"intro": "...", "topics": {"keyword": "text with [other keywords]"}},
  "book": {"title": "...", "pages": ["~80-120 words each"]},
  "bosses": ["scurceol-hyrde"]
}
```

Optional fields the engine adds to the brief's format:

| Field | What it is for |
|---|---|
| `zones` | For a lair inside a realm's open world (Sheeroe Hills is region 200). With zones, the location is those zones only and wins over an umbrella there; without, it is the whole region. |
| `aliases` | Other names players use, for answers to "whats in ...?". The name, the id's words and the name without "the ..." / "'s lair" count already. |
| `atmosphere.areas[].region` | The area's region when the location has several; defaults to the first. |
| `atmosphere.raidForming` | A realm-only line when a raid on this place forms. |

**Umbrella locations.** Open-world bosses belong to umbrella locations over a realm's open world (`hibernia-wilds`,
`hy-brasil`) with one atmosphere area per zone or lair. Without `zones`, such a location's `firstEntry` shows once a day on
coming into that open world. `"regions": []` is allowed (`shrouded-isles`: Gnat has no known region); such a location has
no first entry, and each of its areas gives its own `region`. Areas are found by their region, whatever location they
belong to.

Area `x`, `y`, `z` are region coordinates (the boss spawns in `docs/encounters/world-spawns.json` are a good source).
With `z` set, the area is that floor only (within 800 units of height); `z: 0` means any height.

### Boss

```json
{
  "id": "scurceol-hyrde", "name": "Scurceol Hyrde", "catalogKey": "krondon/scurceolhyrde", "locationId": "krondon",
  "realm": "Albion", "aliases": ["the storm keeper"],
  "facts": [{"text": "...", "provenance": "...", "source": "..."}],
  "story": "80-300 words, tied to the mechanics",
  "legend": "40-120 words",
  "yells": {"aggro": ["..."], "phases": [{"trigger": "health<=50", "text": "..."}], "death": ["..."], "slay": ["..."]},
  "atmosphere": {"lairEntry": ["<=60"], "phases": [{"trigger": "addsDead", "text": "<=60"}], "realmFirstKill": "<=60", "wake": "<=60"},
  "botLines": {"briefing": ["..."], "aftermathWin": ["..."], "aftermathLoss": ["..."], "rumour": ["..."]},
  "loremasterTopic": {"keyword": "...", "text": "..."},
  "bookPage": "",
  "chronicle": {"win": "... {realm} {raidLeader} {minutes}", "loss": "..."}
}
```

- `catalogKey` is the encounter's id in `docs/encounters/epic-encounters.json` (`krondon/scurceolhyrde`) or its script
  class (`ScurceolHyrde`). The engine finds the boss in the world by its script class, so both work. Gjalpinulva's id is
  now `gjalpinulva-s-lair-malmohus/midgjalpinulva` (the class `MidGjalpinulva` is the safer choice).
- **Phase triggers** (for `yells.phases` and `atmosphere.phases`):

  | Trigger | When |
  |---|---|
  | `health<=N` | N from 1 to 99: once a fight, when its health first falls to N% or less |
  | `addsSpawn` (or `addSpawn`) | its adds appear (each new wave) |
  | `orbDeath` | one of its adds dies, but not the last |
  | `addsDead` | the last of its adds dies (once a fight) |
  | `formChange` | its model changes (a shape-shifter: Hurionthex, Govannon) |
  | `death` | it dies: the yell joins its death yells, the atmosphere line goes to its zone |
  | `wake` | a dragon takes to the sky: the yell is said, the atmosphere line is the wake line if `wake` is empty |
  | `partnerDead` | one of its partner encounters dies (the Horsemen, Lord and Lich Lord Sanguis); needs partners in the catalog |
  | `playerDeath` | it kills a player: the yell joins its slay yells, the atmosphere line goes to its zone once a fight |
  | `throw`, `teleport`, `jail`, `pull`, `barrier` | the boss's script moves a raider (`EncounterDisplacement.Displaced`): its kind's own name; `teleport` is also every move's fallback when the file has no line for the kind (Easmarach's push, Krackenschtein's throw) |
  | `glare` (or `stare`), `stun`, `breath`, `stomp`, `messengers`, `retreat`, `safeSpots` | the script announces the mechanic to the raid (`EncounterSignals.Raised`): the dragons, Xanxicar, Olcasgean |
  | `berserk`, `scream`, `healed`, `twinHeal` | a lore-only moment (`EncounterSignals.Note`): Suttung's berserker stance, Kvasir's primal scream, Funus healing to full from a hit he does not take, Tuscar's or Kula's twin's damage healing it |

  A mechanic trigger fires when the script says so, for the boss itself, or for the nearest watched boss within 6,000
  when an add or an object does it (Scurceol's orbs). Its yells come at most once a minute per trigger (and the fight's
  12 s yell gap); its atmosphere line once a fight, at most two mechanic lines a fight (apart from the two phase lines),
  and each player's own throttle still decides (one line every 30 s, realm news first). A ten-raider throw is one yell.
  `tools/lore/mechanic_trigger_coverage.py --check` lists every mechanic trigger the files use and fails if its boss's
  script never announces it.

  The add triggers need adds in the catalog: its adds, the encounters it spawns, a `<Boss>Add` encounter, a dragon's `...SpawnedAdd` (`data/encounter_adds.json`, built from it by
  `tools/lore/build_encounter_adds.py`).
- `wake` is for the dragons (said when one takes to the sky).

### Realm takes (optional, `realmTakes`)

A neutral place or boss (Summoner's Hall, Govannon) may give each realm its own take:

```json
"realmTakes": {
  "albion":   {"story": "...", "legend": "...", "botLines": {...}, "loremaster": {"intro": "...", "topics": {}}, "loremasterTopic": {"keyword": "...", "text": "..."}},
  "midgard":  {...},
  "hibernia": {...}
}
```

Every field is optional; what a take leaves out stays the shared text. A take's loremaster intro replaces the shared one and its topics join the shared topics. A take's story may be shorter (from 60 words for a
place, 40 for a boss). Realm keys are `albion`, `midgard`, `hibernia` in any case. Bot lines in a take are that realm's bots' lines
(a location's `lore`, `rumour`, `tell`; a boss's `briefing`, `aftermathWin`, `aftermathLoss`, `rumour`), and that realm's
loremaster, /lore codex and bestiary show its story, legend and loremaster words. The checks are the shared ones.

### Bot lines

Bot lines are 2003 chat voice: lower case (caps for shouting, "SPREAD", "MT", are fine; a Capitalized word is not), no `{slots}`, at most 160 characters as said. The corpus's `[a|b]` choices
work (`[a|]` is a or nothing; no nesting). Atmosphere lines are said as written, at most 60 characters, no choices.

## The schema in one place (every writer follows this)

Files under `source/server/GameServer/bots/autonomous/data/lore/`: `locations/<id>.json`, `bosses/<id>.json`,
`history/<realm>.json`. Everything not marked required is optional; a missing optional field is never a problem.

**Location**

| Field | Required | What |
|---|---|---|
| `id`, `name`, `realm` | yes | kebab id = file name; realm Albion, Midgard, Hibernia or Neutral |
| `regions` | yes (may be `[]`) | region ids; `[]` for a place with no known region (its areas name their own) |
| `zones` | | zone ids: the location is those zones only (a lair inside an open-world region). Zone 0 is a zone (Camelot Hills); region 0 is not |
| `summary` | yes | one sentence |
| `facts` | yes (1+) | `{text, provenance, source}`; provenance `mythic`, `game`, `legend`, `myth`, `original`; a source for all but `original` |
| `story`, `legend` | yes | no maximum; under 130 / 30 words is a warning. Epic tier: `story` is a 300-500 word summary |
| `chapters` | | `[{title, text}]` in order (epic tier: 4-10 of 250-700 words); each title is a loremaster keyword (60 characters, no brackets, unique in the file) |
| `tier` | | `"epic"`, `"major"`, `"minor"` or left out |
| `era`, `factions` | | ids in the history files |
| `related` | | `[{id, relation}]`: a location or boss id, and how it is tied ("rival", "servant", "kin") |
| `aliases` | | other names players use |
| `atmosphere.firstEntry` | yes (1+) | lines of 60 characters or fewer |
| `atmosphere.areas` | | `[{name, region?, x, y, z?, radius, lines}]`, radius 100-30,000; no limit on how many |
| `atmosphere.raidForming` | | realm-only lines when a raid here forms |
| `botLines.lore`, `.rumour`, `.tell` | tell 1+, 3+ in all | 2003 chat voice |
| `loremaster` | yes | `{intro, topics: {keyword: text}}`; no limit on topics; every `[keyword]` resolves (here, a boss's `loremasterTopic`, a realm take) |
| `book` | yes | `{title, pages}`: 1+ pages, no maximum. Over 16 pages the book is bound in volumes of 16 (a warning, never a cut); a page too long for one text window becomes more pages |
| `bosses` | yes | boss ids; each boss's `locationId` is this location |
| `realmTakes` | | `{albion|midgard|hibernia: {story, legend, botLines, loremaster, loremasterTopic}}` (any case) |

**Boss**

| Field | Required | What |
|---|---|---|
| `id`, `name`, `realm`, `locationId` | yes | the realm is its location's, or the location is Neutral |
| `catalogKey` or `npcNames` | one of them | `catalogKey`: the epic-encounters.json id or the script class (`MidGjalpinulva`); `npcNames`: exact spawn names of a plain world spawn (a family may list every member). The engine matches the class first, then the exact name |
| `tier` | | `"epic"`, `"minor"` (level 35-49, the minor floor), `"order"` (a file of named members, below) or `"major"`/left out |
| `members` | | tier `"order"` only: `[{name, level, post, story, yells: {aggro, death, slay}, atmosphere: {lairEntry}, loremasterTopic: {keyword, text}}]`, one per named member (the Darkness Falls orders). Each name is one of the file's `npcNames`, which is how a member's spawn finds the file; lines are checked like the boss's own (yells 200, lairEntry 60 characters), stories for game talk. The channels speak the order-wide fields; the members' own lines are kept for later |
| `facts`, `story`, `legend` | yes | as for locations (warnings under 70 / 30 words) |
| `chapters`, `era`, `factions`, `related`, `aliases` | | as for locations |
| `yells` | | `{aggro, phases: [{trigger, text}], death, slay}`, 200 characters each |
| `atmosphere` | 1+ line | `{lairEntry, phases: [{trigger, text}], realmFirstKill, wake}` |
| `botLines` | 3+ in all | `{briefing, aftermathWin, aftermathLoss, rumour}` |
| `loremasterTopic` | | `{keyword, text}` |
| `bookPage` (or `bookPages`) | | one page as text, or a list of pages (epic bosses: 2-4) |
| `chronicle` | | `{win, loss}` with only `{realm}`, `{raidLeader}`, `{minutes}` |
| `realmTakes` | | as for locations (`briefing`, `aftermathWin`, `aftermathLoss`, `rumour` bot lines) |

Phase triggers: `health<=N`, `addsSpawn`, `orbDeath`, `addsDead`, `formChange`, `partnerDead`, `death`, `wake`,
`playerDeath`, and the mechanics `throw`, `teleport`, `jail`, `pull`, `barrier`, `glare`, `stare`, `stun`, `stomp`, `breath`,
`messengers`, `retreat`, `safeSpots`, `berserk`, `scream`, `healed`, `twinHeal` (fired when the boss's script announces
them; see above). The add and partner triggers need adds or partners in the catalog.

**History** (`history/<realm>.json`)

| Field | Required | What |
|---|---|---|
| `realm` | yes | Albion, Midgard, Hibernia or Neutral |
| `id` | | defaults to the realm in lower case; the file is `history/<id>.json` |
| `title`, `summary` (or `intro`) | | |
| `eras` | yes (1+) | `[{id, order, name, when, summary, text, events, facts, places, bosses}]`, sorted by `order`; `events` holds named events `{name, text, places, bosses}` or facts `{text, provenance, source}` |
| `factions` | | `[{id, name, realm, summary, text, places, bosses, related}]` |

Era ids are unique everywhere and never a faction's id. The shared faction ids are `summoners`, `lords-of-the-undead`,
`sidhe-of-the-hall` and `dark-ones`: each realm's file may hold its own telling, and each realm's loremaster and codex
tell its own (then the neutral one). Any other faction id in two realms' files is a warning (one power or two?).
An era or faction a file names must resolve once that realm's history file exists. A place or boss a history file,
`related` or a location's `bosses` names may sit on another writer's branch or not be written yet: a warning until the
files meet; the channels skip a link that leads nowhere.

## What the files must pass

`UT_LoreBible.EveryLoreFilePassesTheSchema` reads `data/lore` as committed (or the folder in `LORE_DATA_DIR`, to check a
writer's branch from an export of it; `LORE_REPORT=<file>` writes every problem and warning) and fails with a list of
every problem;
`python tools/bot-voices/build_corpus.py --check` (and `--verify`, in CI) runs every bot line through the corpus's era,
banned-word, place and realm checks. Failures: invalid JSON, a file outside the three folders or not named after its id,
an unknown field, a duplicate id, `catalogKey` or `npcNames` name, a boss whose `locationId` has no file, an era or
faction missing from its realm's history file, a bad realm, provenance, tier or trigger, a fact without its source, an
atmosphere line over 60 characters (or with `[a|b]` or slots), a bot line that is not chat voice or over 140
characters as said, a yell over 200, a dead loremaster `[keyword]`, a chronicle slot other than the three, a duplicate
chapter title.

**Advisory only** (printed, never failing): lengths (a story, legend, page or chapter shorter than reads well, anything
over 1,500 words); a book bound in volumes; links to places or bosses not in the set; a faction id two realms use
outside the shared four; and **game talk** in in-world text. The depth report follows: `LORE_DEPTH` lines with the
count of entries below each depth floor of LORE-1.2-DEPTH.md (full, minor and epic tiers) and the history files per
realm, `LORE_WARNINGS <count> <cause>` lines, then up to 40 `LORE_WARNING` lines.

**Game talk** (owner rule 2026-11-06, "chapters are not a fight guide"): `chapters`, `story`, a realm take's `story`,
`book.pages` and `bookPage` are history told inside the world. A sentence there that matches the writers' lint patterns
(`chapter_lint.py`, kept the same in `LoreBible.GameTalkPatterns`: raid, healer, tank, pull, aggro, adds, phase,
immune, loot, drops, spawn, player, server, herald, "the fight", "if you"...) is a warning naming the file, the place
and the word, for the writer to fix or justify (a smith's guild, "brace the door"). `legend`, bot lines and yells are
where fight advice belongs and are not checked.

## The engine

| Piece | File | What it does |
|---|---|---|
| Lore bible | `LoreBible.cs` | Parses, validates and indexes the files (by id, region, script class, location); `[a|b]` (`LoreText`); finds what a question is about and answers it. |
| Atmosphere service | `LoreAtmosphere.cs`, `LoreRules.cs` | The one path every screen-centre lore line takes: per-player `AtmosphereThrottle`, the triggers' pure rules, the polling, the memory table. |
| Boss watch | `LoreBossWatch.cs` | Boss yells, fight phases, the first-kill line. |
| Channel source | `LoreBibleSource.cs` | The bible as the channels' `ILoreSource` (`LoreChannelHub.UseSource` at start), realm takes included. |
| Bot chat | `LoreChat.cs` | Corpus lines, raid briefings, after-raid tales, answers. |

### Atmosphere lines

Sent with the game's own centre text (`CT_ScreenCenter` gold for realm-scale lines, `CT_ScreenCenterSmaller` for local
ones; the classic client's 1.127 packet library, `PacketLib1127`, and the new client's `game_ui.gd` show them). Real
players only: bots have no client.

**The throttle** (per player, `AtmosphereThrottle`):

- Realm news always wins: keep captures, relics, raid results and siege starts (`GameWideAnnouncements`,
  `RealmEventNotices`, the keep and relic messages) tell the service they went out; an atmosphere line then waits 10 s.
  Realm-scale lore lines (a first kill, a dragon waking, a raid forming, Darkness Falls) are news themselves and never
  wait for the 30 s gap.
- Atmosphere lines: at most one every 30 s. At most one waits, the newest; each goes stale (first entry 30 s, an area or
  lair 20 s, a phase 8 s) and is dropped rather than shown late.
- Never more than 4 centre lines in 5 s (the new client stacks 4 and shows each 5 s); the rest wait.
- Nothing while the player is not in the world (character screen, loading).

**Triggers:**

| Trigger | How | Who sees it | How often |
|---|---|---|---|
| First entry into a location | Every 3 s the service looks at each player's region and zone | That player, small | Once per character per day (the PC's date), saved |
| A named area | Same look: inside an area's circle | That player, small | Once per session |
| A boss's lair | Same look: a living lore boss with `lairEntry` lines within 1,500 units | That player, small | Once per session per boss |
| A boss phase | `LoreBossWatch`, every 2 s while the boss fights | Players in the boss's zone, small | Each trigger once a fight; at most 2 atmosphere lines a fight |
| A boss mechanic (a throw, a glare, a breath, a berserk) | `LoreBossWatch` hears the script (`EncounterDisplacement.Displaced`, `EncounterSignals.Raised`, `EncounterSignals.Moment`) and says it on the next pulse | Players in the boss's zone, small | Each trigger's line once a fight, at most 2 mechanic lines a fight; its yell at most once a minute |
| A dragon wakes | The three dragon brains, where they set `IsRestless` | Its zone gold; its realm elsewhere small | At most once an hour per dragon |
| A realm's first kill | `LoreFirstKills.FirstKill` (the killer's realm, a pet's owner; not staff or the Special Forces) | Everyone, gold, plus a chat line naming the realm | Once ever per realm and boss, in the realm event ledger |
| A raid forms | `AutonomousRealmRaid`, after the forming notice | That realm, small | Each raid (needs `raidForming` lines) |
| Darkness Falls changes hands | `DFEnterJumpPoint.OnKeepTaken` | Everyone, gold | At most once every 15 minutes |

Switches: the launcher's Living Server tab **Atmosphere lines** (`living_atmosphere_lines`, category bots, on by
default). Darkness Falls follows **RvR announcements** instead, and the raid-forming line also needs **PvE realm
events**.

**Memory:** the table `offline_lore_memory` (`MemoryKey`, `Value`, `UpdatedUtc`): `entry:{location}:{character}` = the
day. It is an optional progress table (`progress-policy.json`), so a release world ships it empty and the progress
importer carries it.

**First kills** are decided and saved once, by `LoreFirstKills` (the channels, below: the realm event ledger, the staff
and Special Forces rules, raid wins from before 1.2 counted). The engine listens to `LoreFirstKills.FirstKill` for its
gold line, so an old win is never announced as new and nothing is detected twice.

### Boss yells

A boss is watched from the first hit on it until it dies or resets (10 s out of combat and back above 95% health). Its
`aggro` lines are yelled when it is first hit, `phases` as their triggers fire (at most one every 12 s), `slay` when it
kills a player (at most every 30 s), `death` when it dies. Yells go through `GameNPC.Yell`, so players nearby hear them.

They are additive. `LoreBossWatch.ScriptSpeaks` lists the bosses whose script already speaks at the start of the fight,
on dying or on killing (scanned from `scripts/namedmobs`); for those, the file's lines for that moment are not used.
No script reads the lore files, so a file never replaces a script's own text.

### Bot chat

- **Briefing:** as a raid leaves its muster (`AutonomousRealmRaid`, the hub departure), the first party leader that is a
  gamebot says one or two of the final boss's `briefing` lines in /faction, a few seconds apart.
- **After the raid:** 90-180 s after a win or a wipe, a raider says an `aftermathWin` or `aftermathLoss` line, then
  50 s later one of the place's `lore` or `rumour` lines.
- **The lore pool:** locations' `lore` and `rumour` and bosses' `rumour` lines are added to the voices corpus at load
  (kind ambient, topic lore, about place, the location's realm; Neutral is every realm).
- **Answers:** a question about a place or a boss ("whats in krondon?", "who is the storm keeper", "ever been to tur
  suil") through any route that uses the knowledge answers (tells, /advice, /broadcast, groups, guilds) gets one of the
  place's `tell` lines, or a boss's `rumour` (else `briefing`) line. "Where is ...?" stays the directions answer.

Every bot line goes through the /faction chat budget: above the channel's ceiling it is not said. Bot lines need bot
voices on.

- **Tales:** the realm's storyteller tells a boss's story in /faction when asked, and a groupmate tells the short version
  in /group while the party rests in the boss's place ([TALES.md](TALES.md)).

### GM check

`/lstest lore` says which lore place you are in and forgets your marks there, so its first-entry, area and lair lines
show again on the next look.

### Long lore in pieces (epic tier)

A long story never goes out as one wall of text. `LorePieces.Split` (`lorechannels/LoreChannelData.cs`) cuts on paragraph
boundaries (a line of the text is a paragraph), then sentences, then words; it never cuts inside a word unless one word
is longer than a whole piece, and keeps the order. The limits are the code's own:

- **The hard limit is 2,048 bytes a packet.** `PacketProcessor.ValidatePacketSize`
  (`GameServer/packets/Server/PacketProcessor.cs`, `if (packetSize <= 2048)`) discards any bigger TCP packet whole and
  tells the player only "Oversized packet detected and discarded". An over-long message is not shortened: it is lost.
- **Loremaster words** go out with `GameNPC.SayTo`, the popup window: one Message packet each
  (`PacketLib1127.SendRawMessage`: header, chat type, `##`, `<Name> says, "..."`, a zero). Each piece is at most 480
  characters (`LorePieces.SayChars`), about a quarter of the packet (`LorePieces.SayBytes`) and a readable popup.
- **Text windows** (book pages, /lore): one DetailWindow packet (`SendCustomTextWindow`, `PacketLib168/175/181.cs`);
  `WriteCustomTextWindowData` stops at `MAX_PACKET_LENGTH` (2,048) and drops the rest without a word, and each line is a
  Pascal string of at most 255 characters (code page 1252: a byte a character). A book page is therefore at most
  1,500 characters (`LorePieces.PageChars`, checked with `LorePieces.WindowBytes`); a longer page goes on over the next
  pages, and nothing is cut.
- **Books** hold 16 pages a volume (`LoreBooks.VolumePages`, `LoreBible.MaxBookPages`); a longer book is bound in
  volumes (below).

The loremaster offers a place's summary first: "[the tale of Krondon] is long in the telling; ask of it a part at a
time: [The Making], [The First Raid]". Each chapter title is a keyword; a chapter is sent in pieces and ends with "Then
ask of [the next chapter]". The /lore codex pages the chapters after the summary. Bots never quote chapters: they keep
to their bot lines.

### Which file is where

A boss is found by its script class first, then by its exact spawn name (`npcNames`). Several location files may claim
one region (an umbrella beside its zone files): the file listing the zone wins, then the narrowest (fewest regions),
then one with placed areas. Every file's areas count wherever the player is.

## Channels: loremasters, lore books, the Herald and the codex

Beyond bot chat, players meet the lore bible (`source/server/GameServer/bots/autonomous/data/lore/`) in four more places.
All four read the same files and treat the three realms alike. The code is in `source/server/GameServer/lorechannels/`.

### Loremasters

- **Who and where.** One per capital, beside a stock NPC of every release world. Each takes the first free spot on the
  floor beside it: in its line of sight, with no other NPC within 85 units, on the anchor's own floor.
  - **Loremaster Aldwin:** Camelot's library, beside Librarian Pelan.
  - **Loremaster Sigrun:** Jordheim's temple of the gods, beside the Gothi of Ymir.
  - **Loremaster Eithne:** Tir na Nog, beside Filidh Meilseior by the bards.
- **How they are added.** They are script-owned NPCs, the way this fork adds its world NPCs (as the faction emissaries
  are). Nothing is written to the world database, so a played world is never edited. ROLLBACK.cmd takes them out with
  the build.
  - `Loremasters.Plan` is the dry run: where each one would stand, or why it can't.
  - `Apply` spawns them and `Rollback` removes them (also when the scripts unload).
  - The log names each one (`LOREMASTER_SPAWNED` / `LOREMASTER_SKIPPED`).
- **What they say.** Keyword dialogue as quest givers have it: [bracketed] words are clickable in both clients.
  - The intro offers [places] and [books].
  - [places] lists the realm's own places and the neutral ones.
  - A place answers with its `loremaster.intro`, then the bosses its intro doesn't already link, then its book.
  - Each `loremaster.topics` keyword, and each boss's `loremasterTopic` keyword or name, answers with its text. A boss
    with no topic answers with its legend.
  - A keyword two places share answers for the place the talk is about.
  - A [keyword] that leads nowhere loses its brackets, so no link is dead.
- **Players only.** The loremasters are not merchants, so gamebots never sell to them, and they ignore bots.

### Lore books

- **One book per location**, with the `book.title` and `book.pages`, followed by each boss's `bookPage`.
- **Volumes.** A book of more than 16 pages (after long pages are split and the bosses' pages added) is bound in
  volumes of 16, in order, so every page is in one of them: `ls_lore_book_<location>` (volume I, the same id a
  one-volume book has, so copies already held still read), then `ls_lore_book_<location>_vol2`, `_vol3`. Each is
  named "<title>, Volume II" (60 characters at most; a long title loses words at its end, never the volume), its delve
  says "Volume II of III", and the last page of a volume names the next. The loremaster sells all volumes together
  (a silver each, room for all checked first); a drop gives the first volume the player does not hold.
- **Reading.** "Use" opens the text window one page at a time, and each use turns a page. The delve shows the
  location's summary and the page count. Each page is a window of its own, because one window holds about 2,000 bytes.
- **The item.** A new template per book: `ls_lore_book_<location>`, the client's stock **Book** model 500 (objects.csv;
  no client file changes), PackageID `LivingServerLore`, ClassType `DOL.GS.LoreChannels.LoreBookItem`, and
  `CanDropAsLoot` off, so no random loot table ever picks one. No stock item is touched.
- **Where players get them.**
  - **From the loremaster:** [book of Krondon], then a buy dialog. A book costs one silver.
  - **Near the place:** a kill by a real player (or their pet) of a monster that isn't grey, in that place's own region
    or within twice an atmosphere area's radius of it. The chance is 1 in 250, at most once every 20 minutes per
    player, and never while the player already has a copy.
- **The world update.** At each start the server checks the book templates against the lore.
  - It adds or updates **its own rows only**. A row of that id from anything else is left alone, and that book is not
    offered.
  - It removes its own rows for books the lore dropped, unless someone holds a copy.
  - `LoreBooks.Plan` is the dry run, `Apply` writes, and `Rollback` removes every own row nobody holds.
  - The log line is `LORE_BOOKS add=.. update=.. keep=.. foreign=.. remove=.. inuse=..`.
- **Offline rollback** (server closed): `python tools\claude-version\lore_books.py rollback` is the dry run, and
  `rollback --apply` backs up first, then removes the rows (a held copy keeps its template).

### The Herald

- **The chronicle.** Each finished raid record is told by its final boss's `chronicle.win` or `chronicle.loss`.
  - It appears in the launcher's Realm Records details and in the Camelot Herald's "From the Frontier and Beyond".
  - **Placeholders:** `{realm}`; `{raidLeader}` (the first party's leader, written at the end of the record's details
    as ` Raid leader: <name>.` from 1.2 on; older records read "its raid leader"); `{minutes}`.
  - **Win or loss:** "Boss defeated" is a win. "Defeated", or a time-out after a wipe, is a loss. Anything else has no
    sentence.
  - The sentence is built when the record is shown, so a better template applies to old records too.
- **The Bestiary** (Realm Records → Bestiary, and a "Bestiary of the Realms" section in the Herald). It lists every boss
  of the chosen realm's own and the neutral places. **Decision (owner to confirm):**
  - **Always shown:** the boss's name, its place and its `legend`, the raid-leader advice bots and loremasters already
    repeat, so fight advice is never hidden.
  - **Shown after the realm's first kill:** its `story`, the first-kill date and who made it, and its raid record (wins
    and losses).
  - Until then the story reads "Unknown".
  - A first kill is a kill in the ledger table `LoreFirstKills`, or for a raid final the realm's earliest "Boss
    defeated" record, so wins from before 1.2 count.
- **First kills.**
  - A boss counts for the realm of the character that struck the killing blow (a pet's owner). A GM's kill doesn't
    count, and neither does a visiting Epic Special Forces member's.
  - Each is written once per realm and boss to `realm-event-records.sqlite3` (beside the world database, never in it),
    off the game loop. DELETE ALL RECORDS clears the raid records and keeps the first kills.
  - `LoreFirstKills.FirstKill` is raised once per first kill, for anything that wants to mark it (the atmosphere
    service's realm-first-kill line).

### The codex (/lore, and the new client's LORE window)

- **In either client.** `/lore` lists the realm's places, each with how many of its bosses the realm has slain.
  `/lore <place>` gives the story, the legend and the bosses with their first-kill status. `/lore <boss>` gives the
  bestiary page, under the same rules as the launcher.
- **Pages.** Pages hold at most 850 characters. A long one ends with `More: [Krondon 2]`, and `/lore krondon 2` shows
  page 2. Captions start `Lore: `.
- **New client.** The journal's **LORE** button sends `/lore`. Pages captioned `Lore: ` open in the LORE window, where
  every [name] is a link that asks for that page.
  - The new client reads nothing from files. The data, and the realm's first kills, come from the server over the
    existing text-window packet. That keeps the two clients and every edition on one source, with no new packet and no
    file in the client package.

### Wiring the lore engine's loader

- **The default source.** The channels read an `ILoreSource` (`LoreChannelData.cs`): `AllLocations`, `AllBosses`,
  `GetLocation`, `GetBoss`. By default this is `EmbeddedLoreSource`, a lenient reader of the embedded files that keeps
  only the fields the channels use.
- **The engine is plugged in.** At start the engine calls `LoreChannelHub.UseSource(new LoreBibleSource(LoreBible.Current))`
  (`LoreBibleSource.cs` maps its records onto `LoreLocation` / `LoreBoss`), so the channels read the validated bible.
  The loremasters, books and first kills rebuild on `SourceChanged`. `EmbeddedLoreSource` stays the reader before that
  and in the launcher.
- **Realm takes.** `LoreRealmView.For(source, realm)` shows each place and boss with the reader's realm's take: the
  loremaster dialogue, the /lore codex and the bestiary use it.
- **The launcher** links `LoreChannelData.cs` and `LoreHerald.cs` and embeds the same JSON files (`LauncherLore`).

## Tests

- `UT_LoreBible`: the schema check on every committed file; the server carries every file; the adds map matches the
  catalog; the script-voices list names real boss classes; indexes; each kind of failure the check catches; `[a|b]`;
  answers; corpus lines; briefings; realm takes (parsing, checks, answers, corpus realm, the channels' view and
  loremaster); the channel adapter.
- `UT_LoreBible` (depth and delivery): chapters and tiers; the summary first, then chapters as keywords; pieces keep
  every word in order under the limit; every loremaster piece fits the popup packet and every page window the 2,048
  bytes; game talk is a warning in chapters, story and books and not in legends; books over 16 pages are a warning, not
  a failure; links across branches are warnings; shared faction ids and each realm's telling; `bookPages`.
- `UT_LoreChannelBooks`: books in volumes (every page, ids, names, delve, world update, footers, the loremaster's offer).
- `UT_LoreAtmosphere`: the throttle (30 s gap, newest waits, stale dropped, news first and 10 s quiet, 4 in 5 s); where a
  player is (zones before regions), first entry once a day, area circles; fight triggers (health once, adds spawn, die and
  all die, reset, form changes, partners); the script-only triggers; rate limits; the switch default.
- `tools/bot-voices/build_corpus.py --verify` checks the bot lines.

None of this has been played in game. To check it: walk into Krondon (first entry), walk to Scurceol Hyrde's room (area
and lair), pull him and kill the four lights (yells, `orbDeath`, `addsDead`), and kill him (death yell, first kill). Check
the line lengths on the classic client (the inventory suggests trying 40, 60 and 80 characters).
