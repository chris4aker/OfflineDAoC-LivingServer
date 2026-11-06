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
  "facts": [{"text": "...", "provenance": "mythic|game|legend|original", "source": "url or file"}],
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
| `zones` | For a lair inside a realm's open world (Sheeroe Hills is region 200). With zones, the location is those zones only; without, it is the whole region. **Required** when a region is an open-world one (1, 51, 100, 151, 163, 181, 200). |
| `aliases` | Other names players use, for answers to "whats in ...?". The name, the id's words and the name without "the ..." / "'s lair" count already. |
| `atmosphere.areas[].region` | The area's region when the location has several; defaults to the first. |
| `atmosphere.raidForming` | A realm-only line when a raid on this place forms. |

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
- **Phase triggers:** `health<=N` (N from 1 to 99: once a fight, when its health first falls to N% or less), `addSpawn`
  (its adds appear), `orbDeath` (one of its adds dies, but not the last), `addsDead` (the last of its adds dies). The add
  triggers need adds in the catalog (`data/encounter_adds.json`, built from it by `tools/lore/build_encounter_adds.py`).
- `wake` is for the dragons (said when one takes to the sky).

### Bot lines

Bot lines are 2003 chat voice: lower case, no `{slots}`, at most 160 characters as said. The corpus's `[a|b]` choices
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
- **Realm** is Albion, Midgard, Hibernia or Neutral. **Regions** are given; an open-world region needs `zones`.
- **Facts:** at least one; provenance `mythic`, `game`, `legend` or `original`; every fact but an `original` one names its
  source.
- **Lengths** (with some slack around the brief's): location story 130-460 words, boss story 70-345, legends 30-140, book
  pages and `bookPage` 50-160.
- **Atmosphere:** every line 60 characters or fewer, said as written; a location has at least one `firstEntry` line; a
  boss has at least one atmosphere line of any kind. Areas have a name, x/y, a radius from 100 to 5,000, a region of the
  location, and lines.
- **Bot lines:** `[a|b]` parses, lower case, no slots, 160 characters or fewer in every way of saying it. A location has
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
| Boss watch | `LoreBossWatch.cs` | Boss yells, fight phases, a realm's first kill. |
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
| A realm's first kill | The boss's death (`GameLivingEvent.Dying`), the killer's realm (a pet's owner) | Everyone, gold, plus a chat line naming the realm | Once ever per realm and boss, saved |
| A raid forms | `AutonomousRealmRaid`, after the forming notice | That realm, small | Each raid (needs `raidForming` lines) |
| Darkness Falls changes hands | `DFEnterJumpPoint.OnKeepTaken` | Everyone, gold | At most once every 15 minutes |

Switches: the launcher's Living Server tab **Atmosphere lines** (`living_atmosphere_lines`, category bots, on by
default). Darkness Falls follows **RvR announcements** instead, and the raid-forming line also needs **PvE realm
events**.

**Memory:** the table `offline_lore_memory` (`MemoryKey`, `Value`, `UpdatedUtc`): `entry:{location}:{character}` = the
day, and `firstkill:{realm}:{boss}` = when and who. It is an optional progress table (`progress-policy.json`), so a
release world ships it empty and the progress importer carries it. A world that beat a boss before 1.2 has no record of
it: the first kill after the update is announced as the realm's first. `LoreAtmosphere.FirstKills(realm)` lists a realm's
beaten bosses (for the Herald's bestiary).

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

## Tests

- `UT_LoreBible`: the schema check on every committed file; the server carries every file; the adds map matches the
  catalog; the script-voices list names real boss classes; indexes; each kind of failure the check catches; `[a|b]`;
  answers; corpus lines; briefings.
- `UT_LoreAtmosphere`: the throttle (30 s gap, newest waits, stale dropped, news first and 10 s quiet, 4 in 5 s); where a
  player is (zones before regions), first entry once a day, area circles; fight triggers (health once, adds spawn, die and
  all die, reset); rate limits; a realm's first kill claimed once ever and remembered after a restart; the switch default.
- `tools/bot-voices/build_corpus.py --verify` checks the bot lines.

None of this has been played in game. To check it: walk into Krondon (first entry), walk to Scurceol Hyrde's room (area
and lair), pull him and kill the four lights (yells, `orbDeath`, `addsDead`), and kill him (death yell, first kill). Check
the line lengths on the classic client (the inventory suggests trying 40, 60 and 80 characters).
