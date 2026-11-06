# Lore (Living Server 1.2)

<!-- The lore engine's sections (format, loader, validator, atmosphere, boss yells, bot hooks) go above this one. -->

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
- **Plugging the engine in.** Once the engine's validated loader exists, it calls
  `LoreChannelHub.UseSource(adapter)` at start. The adapter maps its records onto `LoreLocation` / `LoreBoss`, or the
  engine's own types implement `ILoreSource`. The loremasters, books and first kills rebuild on `SourceChanged`.
- **The launcher** links `LoreChannelData.cs` and `LoreHerald.cs` and embeds the same JSON files (`LauncherLore`).
