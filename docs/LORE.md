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
  | `teleport`, `berserk`, `breath`, `glare` | accepted, never fired: only the boss's own script sees these moments |

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

## What the files must pass

`UT_LoreBible.EveryLoreFilePassesTheSchema` reads `data/lore` as committed and fails with a list of every problem.
`python tools/bot-voices/build_corpus.py --check` (and `--verify`, in CI) also runs every bot line through the corpus's
era, banned-word, place and realm checks. In short:

- **Valid JSON**, in `locations/` or `bosses/`, named after its id; no unknown fields (a typo is caught).
- **IDs** lower-case kebab, unique among locations and among bosses; no two boss files with the same `catalogKey`.
- **References resolve:** every boss in a location's `bosses` has a file whose `locationId` is that location, and every
  boss's location lists it; `catalogKey` is a catalog id or script class; a boss's realm is its location's (or the
  location is Neutral).
- **Realm** is Albion, Midgard, Hibernia or Neutral. `zones` need a region; `regions` may be empty.
- **Facts:** at least one; provenance `mythic` (Mythic's canon), `game` (scripts, world data), `legend` (era player talk),
  `myth` (the real-world myth a name comes from, not game canon) or `original`; every fact but an `original` one names
  its source.
- **Lengths** (with some slack around the brief's): location story 130-460 words, boss story 70-345, legends 30-140, book
  pages and `bookPage` 50-160.
- **Atmosphere:** every line 60 characters or fewer, said as written; a location has at least one `firstEntry` line; a
  boss has at least one atmosphere line of any kind. Areas have a name, x/y, a radius from 100 to 30,000 (a whole zone in an
  umbrella), a region of the location (their own when the location has none), and lines.
- **Bot lines:** `[a|b]` parses, lower case (caps only for shouting), no slots, 160 characters or fewer in every way of saying it. A location has
  at least one `tell` line and three bot lines in all; a boss three bot lines in all.
- **Yells:** 200 characters or fewer; phase triggers parse; add triggers only for bosses with adds.
- **Loremaster:** a location has an intro and topics; every `[keyword]` in them is a topic of the location or one of its
  bosses' `loremasterTopic` keyword. A `loremasterTopic` has both keyword and text.
- **Book:** a title and at least one page.
- **Chronicle** slots are only `{realm}`, `{raidLeader}` and `{minutes}`.

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

### GM check

`/lstest lore` says which lore place you are in and forgets your marks there, so its first-entry, area and lair lines
show again on the next look.

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
- `UT_LoreAtmosphere`: the throttle (30 s gap, newest waits, stale dropped, news first and 10 s quiet, 4 in 5 s); where a
  player is (zones before regions), first entry once a day, area circles; fight triggers (health once, adds spawn, die and
  all die, reset, form changes, partners); the script-only triggers; rate limits; the switch default.
- `tools/bot-voices/build_corpus.py --verify` checks the bot lines.

None of this has been played in game. To check it: walk into Krondon (first entry), walk to Scurceol Hyrde's room (area
and lair), pull him and kill the four lights (yells, `orbDeath`, `addsDead`), and kill him (death yell, first kill). Check
the line lengths on the classic client (the inventory suggests trying 40, 60 and 80 characters).
