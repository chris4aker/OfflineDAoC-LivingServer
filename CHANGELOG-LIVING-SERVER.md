# Living Server changelog

What this fork adds to shadowofze's Offline DAoC. The base game's own changes are in
[CHANGELOG.md](CHANGELOG.md). The newest version is first.

Up to 0.37-se.2 the fork was called the **Social Expansion**, and "Living Server" was the name of one
of its feature groups (bots remember you, grats, rezzes, alarms, nemeses, schedules, breaks, body
language). From Beta 1.0 the whole add-on is the **Living Server** and that group is **Server life**.
Older entries below keep the names they were written with.

## Beta 1.1.1 (in development)

Fixes and verifications only, for the first week after Beta 1.1: the community's reports (issues #32, #33, #38,
#39 and the pull requests offered in #40), the stacked fixes #35-#37, sieges played at the owner's roster now that
their timers run, dragon raids calling the Epic Special Forces after a wipe, and the quest defects that are code
only (kill credit in a bot group, hand-ins above a quest's level, the journal, quest XP). The plan is
[docs/RELEASE-TRAIN.md](docs/RELEASE-TRAIN.md); the quest findings are [docs/QUESTING.md](docs/QUESTING.md).

- **Bots stop following you when you leave or disband their group** (issue #33, reported by nindybrew-svg).
  - **The bug:** with two or more invited gamebots in the group, `/disband` took you out but left them marked as
    following you. They trailed you without fighting for you, and refused new invites ("already following another
    player-led group") until you logged out. A single bot was already released correctly.
  - **Whose code:** the cause is in shadowofze's group code (`Group.RemoveMember`), unchanged in his 0.38 branch.
    It isn't a 1.1 regression, but 1.1's pathing to a far-off player made the stuck bots follow more reliably.
  - **Now:** when a player leaves, the gamebots that followed them follow another player still in the group, or,
    with none left, leave the group and go back to their own play. `/spawn` companions are left as before.
  - **Checked:** three new tests (released, handed to a remaining player, companions untouched); 4,096 server tests
    pass. Not yet seen in game.
- **Base classes get their weapon styles before level 5** (reported on r/daoc by Zarthol).
  - **The bug:** the style table lists styles only for the classes chosen at level 5, so a Fighter, Viking, Guardian
    or other base class had no styles at all until then. A Fighter with Slash 2 had nothing where a classic one had
    Slash's first style.
  - **Whose code:** shadowofze's data and style lookup, the same in 0.36c and 0.37b. Bots aren't affected: they start
    in their chosen class.
  - **Now:** a base class gets the styles its classes have in the weapon line it trains, each once (from the game's
    data: Fighter Ruby Slash, Thistle and Daze at 2; Viking Splitter, Thor's Anvil and Whirling Blade; Guardian
    Shining Blade, Contusions and Dragonfly). Chosen classes keep their own lists unchanged.
  - **Checked:** two new tests; the full server suite passes. Not yet seen in game.
- **Quiet twisting** (from a r/daoc report by ThatRebelKid).
  - **The problem:** a Minstrel, Bard or Skald bot twists by restarting a song every few seconds. Each restart played
    its 3-second cast music and its start sound to everyone nearby, so a bot performer near you never stopped.
  - **Now:** a bot restarting a song it started within the last 30 seconds does so without the cast animation and
    with the start effect silent. The first start of each song is still heard. Songs, timings and buffs are unchanged,
    and players' own songs and harmful pulses (a flute mez) keep their sounds.
  - **Checked:** three new tests (`UT_BotQuietSongs`); 4,101 server tests pass. Not yet heard in game.
- **The launcher no longer stays on STARTING when Windows leaves the server out of its listener table** (issue #32,
  reported by joshuaforehand).
  - **The bug:** the launcher asked Windows for the list of listening TCP ports and looked for 10300. On the reporter's
    PC that list left a working listener out, so the server was up, `Test-NetConnection` succeeded, and the launcher
    never left STARTING.
  - **Now:** the list is still the first check; when it doesn't show the port, the launcher tries a short connection
    to 127.0.0.1:10300 (150 ms at most) and takes success as "running". Shared launcher code, so it applies to both
    launchers.
  - **Checked:** four new tests (`ServerPortProbeTests`); the launcher suite (127 tests) passes on CI. Not tried on a
    PC that shows the problem.
- **New client: the NPC your current quest step needs is marked green, not orange.** Since 1.1 the server sends one
  marker scheme (available, current step, finish); the 0.37c client drew "current step" as "in progress elsewhere".
  - **How it reaches you:** the client runs its scripts from source, so the installer patches
    `new-client\godot\game\quests\quest_markers.gd` in place like the gate models (only when it is the released
    file; `ROLLBACK.cmd` puts it back). It is the first client script Living Server ships; the others in its source
    (the target window, FACE/FOLLOW/STICK, `/lfg` and `/trade` channels, the PLAY double-click guard) follow in 1.2.
  - **Checked:** the patch rebuilds the fixed file byte for byte; not run in game.

## Beta 1.1 — 2026-10-29

Beta 1.1 is several pieces of work together: the talking update and the living world (beggars, deeds, storylines,
spammers, roleplayers, rumours, holiday chatter, Leeroy), 2003-style bot names, a first half hour paced for signature
moments, a GM command to check it all in game, Offline DAoC 0.38's server fixes ahead of their
release, fixes taken from other forks of Offline DAoC, bots that react to attacks like players, quests that can be
finished with their markers and epic chains, and the all-in-one Setup. The plan and its checklist are in
[docs/RELEASE-1.1.md](docs/RELEASE-1.1.md).

### The talking update

A Reddit report from Beta 1.0 had a Hibernian bot in Ardee asking about an Albion dungeon. That led to a rework of how
bots talk; the full design is in [docs/bot-voices/TALKING-UPDATE.md](docs/bot-voices/TALKING-UPDATE.md).

**Bots say only what is true**
- Each line that names a place needs the speaker there or on its way, or says it is only talking about it. A place
  list of about 140 zones, towns and dungeons gives the realm and zones of each. The corpus build rejects lines that
  name another realm's zone, town or dungeon.
- Keep, relic, siege, raid and Darkness Falls lines need the news that makes them true, and use the real names.
  When something happens, a bot tells /faction in its own words about a minute later ("heard we lost Caer
  Benowyc"), as a witness if it was there, and others react.
- Xp ("4 bubbles to go"), role ("i can heal"), new-player and veteran lines follow the bot's real state. "Where are
  the mids?" is answered from what the realm has seen. With nothing true to say, a bot stays quiet instead of
  using the old formal lines.
- Lines that promised a bot would invite you are no longer said (a bot can't invite a player). Wizards no longer
  talk about their pet, and solo bots don't apologize to "my healer".

**Conversations**
- "why?", "rly?", "where?" right after a bot's line is answered by that bot. An answer to a bot's question gets
  thanks from the asker, and the others let it be. Tells carry on the same way and answer with where the bot is
  when it replies.

**Bots read what you say**
- Questions get true answers: a bot asked by name, tell or in your group says its own level, class, rank, guild,
  gear, plans or whether it is busy; any bot answers a place's monster levels (from the world data), battleground
  brackets, who holds Darkness Falls, the relics and keeps. Other factual questions get "not sure", not a remark on the
  topic. The bot's name in a line is no longer read as part of it.
- More follows on in /faction, /say and /group: "which one", "how long did that take", "whered they go", "no thanks",
  "ty" to the bot that helped, "x" to a bot's call for a group. Good news, offers, orders (with an honest "cant"),
  warnings, "wb" and real-life talk get replies in kind, and a groupmate reacts when you die.
- Fewer misreadings: "inc" as income, a group only talked about, sarcasm, an insult aimed at a class, a goodbye with
  thanks in it. Measured on lines written blind and not tuned against, about two in three are now read as a player
  would expect, and 12-16% draw a reply that misses (automated; see the design doc).

**Your group**
- Gamebots in your group call the fight: the pull, adds, low health, out of power, the mez, root, stun, taunt, group
  heal or rez they are casting, a wipe, and gj or med up after. Spaced out, never a flood. A bot that joins your
  group says hi.

**Tells and channels**
- Now and then (at most every 25-45 minutes) a bot starts a tell: a friend saying hello or thanks for an old group,
  passing on news or offering a low-level friend a hand; a guildmate; a solo bot of your level nearby offering to
  duo. Your /lfg may get a duo offer. "sure" gets "send me an invite".
- Most bot lfg lines go to /lfg and trade lines to /trade. Realm chat is busier with more bots online and in the
  evening, quieter late at night; new setting `bot_voices_chat_rate` (0-3). New setting `bot_voices_tells`.
- Guild lines and answers are written in each bot's style.

**Defaults**
- Bot voices, bot memory and grats are on by default (chat only). Personalities, guilds and the other Server life
  parts stay off. On a world that ran an earlier build, 1.1 switches them on once where the server had written the
  old default; settings saved in the launcher keep their values.

**Checked**
- Automated: all 3,616 tests in `Tests.dll` (Linux), including 79 new talking-update cases and a chat simulator that
  plays an evening in each realm, a lost keep, a group fight and three conversations through the real corpus. An
  independent code review found eight defects; all are fixed. Not played in game yet; the list of checks is in
  TALKING-UPDATE.md.

**Lore conversations**
- The lore debates are full conversations now: Albion's five tales, the shared ones, Avalon's, and richer lore talk
  in every realm, each in its realm's own voice.

### Server life: the living world

New Server life parts, on by default, each with its own switch in the launcher's Living Server tab. Details are in
[docs/LIVING-SERVER.md](docs/LIVING-SERVER.md) and [docs/bot-voices/TALKING-UPDATE.md](docs/bot-voices/TALKING-UPDATE.md).

- **Beggars** (`bot_living_beggars`): about 4% of each realm's Crafters beg in their capital, take the coin and items you
  drag onto them and hoard every copper. Ask anyone for gold and they follow you around the city insulting you.
- **Deeds and reputation** (`bot_living_deeds`): bots that see you solo a named monster, go on a kill streak, get zerged
  or die to something silly tell /faction, and you earn a reputation and a nickname.
- **Storylines** (`bot_living_storylines`): pairs of bots live out dramas in /faction over days (a kill steal, a guild
  split, a romance, a ninja-loot trial). Take a side and the ending follows you.
- **Spammers and roleplayers** (`bot_living_characters`): a gold spammer floods /faction until it is banned; a few bots
  speak in thee and thou and get mocked for it.
- **True rumours** (`bot_living_rumours`): a rare named monster that just spawned, or an enemy keep with its guards down.
- **The Dunce Rage Quit:** now and then a bot asks something hopeless in /faction, the realm piles on and it rage quits.
- **Bots remember each other** (`bot_group_bonds`): good runs make friends who group again, no-shows make grudges, and
  bots talk about both in /faction.
- **Holiday chatter** (`bot_living_holidays`): around New Year, Valentine's, St Patrick's, April Fools, Easter, Midsummer,
  Halloween, Thanksgiving and Christmas on your PC's calendar, bots talk about the holiday the way 2003 players did, in
  each realm's own flavour (Samhain, Yule, All Hallows' Eve), and answer your holiday wishes in kind.
- **Leeroy in raids** (`bot_living_leeroy`): one end-game raid in twenty has a raider who charges the final boss before
  the raid is ready, yelling his name; the raid has to follow him in, and /faction hears about it either way.

### Lag spikes with a large population (issue #25)

- Reported and diagnosed by Krolgosh-web. A warband walking to a distant frontier medallion merchant searched for a
  spot beside it from far away, proving up to 18 multi-zone routes every 15 seconds per bot. With many level 50s on
  RvR, the server tick stalled for about a second and killed bots stood at 0 HP before they fell. Stock 0.37 never ran
  this, because its far-off bots gave up on the medallion.
- Bots now look for the spot only within 2,000 units of the merchant, and wait 30 seconds after a search that found
  nothing.

### Offline DAoC 0.38's server fixes, ahead of their release

From shadowofze's 0.38 work, which isn't published yet (his `main` at `9da9091`, server code only). Thanks to
shadowofze.

- **Bot raids:**
  - A bot raid can start Caer Sidi's four horsemen and kill Apocalypse: the hidden controller that starts their
    challenge now runs whoever is near.
  - Galladoria's Olcasgean wakes for a bot raid once the four primals are dead.
  - Raiders no longer swing at things a player can't target (the Corrupt Primal of Fire's patches, which also hit
    for less now).
  - A raid target only a few are fighting draws some of the busy fighters over.
- **Armies:**
  - A relic or keep army's new leader is the marcher with most of the army around it, not a straggler far behind.
  - Siege healers stay behind the line, walk out to their dead and raise them; a fallen attacker waits for a raise
    while a healer of its army is within 5,000.
- **Travel:**
  - Hibernian bots no longer loop at their Odin's Gate portal keep door.
  - Albion bots step off the walled-in floor beside theirs instead of being sent home.
- **World code:**
  - Random helmets never roll a model the client draws nothing for.
  - A door the server has no record of opens again (and is logged as DOOR_NOT_IN_DATABASE).
- **Not included:** the new-client changes, the patch tools and the world-data parts (Campacorentin patrols,
  helmets already stored in a world). Those come with shadowofze's own 0.38 patch.

### Fixes from other forks

- **The effect leak.** An effect that ended before it had started was put back in the effect service and ticked
  forever, so the server slowed the longer it ran. Such effects are no longer added, and any ended effect still there
  is removed. From rambodolphin13's fork, which measured 30 million such effects and 326 ms per tick on a 3,000-bot
  run.
- **Siege timers.** Muster deadlines, the march after them, repels and the four-hour cap now run on the live clock.
  A throttle that started at the lowest possible time overflowed and skipped every pass (found by stefanrows' fork;
  the same code is in Offline DAoC 0.37). This had been off on every live server since the throttle was added, so
  sieges will now end, march and repel on their timers where earlier builds did not. It is the biggest change here to
  watch in play.
- **Healers** let a cure they are already casting on the same member finish instead of restarting it every turn
  (stefanrows, via rambodolphin13).
- **Animists** below half power keep one damage shroom on the fight and skip vents and spores, so they regenerate
  between pulls (rambodolphin13, from a tester report).
- **A full bag at the medallion merchant.** A bot with a full bag and nothing to sell no longer waits at the merchant
  until the watchdog sends it home. It stays away for five minutes (rambodolphin13).
- **Siege weapons** no longer throw an error when a bot lets go of one on another thread (rambodolphin13).

### Fewer failed routes

From stefanrows' fork (their 0.314 to 0.328), for autonomous gamebots only.

- A goal that failed because it lies just off the walkable ground moves onto its floor (dungeons and precise
  arrivals are left alone; a big height change must still be walkable).
- An enemy hunt inside another realm's keep courtyard with its outer gate closed is no longer offered. Bots pass the
  gates of keeps their realm owns, and a force sieging a keep keeps its targets there.
- A spot a bot gave up on is blocked for six minutes for the bot and its warband, so camp, roam and crossing choices
  stop picking it again.
- A bot standing on connected ground is no longer sent to its capital because its goal was off the walkable ground.
- The existing floor search is budgeted: fewer repeated failed searches.
- stefanrows measured route give-ups falling from 364 to 122 per game-hour in their world (not checked here). Their
  evidence is in [docs/NAVMESH_HOTSPOTS.md](docs/NAVMESH_HOTSPOTS.md).

### Bots react to attacks like players

From stefanrows' fork (their 0.271). An autonomous gamebot hit by a player, a bot or their pet reacts like a person:

- It answers after a moment: 0.3-0.8 s for melee, up to 1.2 s for a spell or arrow at it, up to 2.2 s for an area
  splash, plus up to a second for an attacker it wasn't facing, never more than 2.5 s. Tanks turn first and pure
  casters last.
- With personalities on, bold bots (Daredevils, No-lifers) turn first and cautious ones (Crafters) last.
- An area spell from a distance doesn't make its caster the bot's target. A bomber next to it does.
- A far attacker doesn't pull a bot off another player it is fighting.
- Healers heal while they turn, and cure a group with three or more members poisoned with the group cure.
- Mobs still get the immediate reaction. Companions and bots in your group follow you, so they are left out.
- Switch: `bot_incoming_fire_reaction` (on).

### Quests

From lometur's HearthDAoC fork.

- **Quests you can now finish:**
  - Quest targets and givers match a spawn's name whatever its capitals. Kills of "arawnite messenger" for "Path of
    the Renegade" never counted before.
  - Delivery items are handed once, on accept, and handed back when a step returns them. "Traveler's Way -- Supply
    Run" gave two bundles, and quests that start with a delivery handed nothing.
  - The quest journal holds 25 quests, as the client shows, and a quest you are on can't be given again.
- **Quest markers.** Every NPC uses one set of rules and they stay current:
  - a yellow knot over a giver with a quest for you (within 15 levels);
  - a white knot over an NPC your current step needs;
  - a blue knot over the one that finishes the quest.

  Markers update as you take, move, finish or abandon a quest and as you level, not only at a level-up.
- **Epic chains:**
  - Quest dependencies can name quest IDs, so a chain's steps come in the right order even where two steps share a
    name.
  - The Guild of Shadows chain (levels 7 to 48, both the Camelot and Shrouded Isles routes) has its period dialogue.
  - Every other guild's epic line has its steps pinned in order.
  - The world part is `tools/claude-version/epic_chains.py`. It is a dry run by default, `--apply` writes after a
    backup, `rollback --apply` puts the rows back, and saves are never touched.
  - GMs get `/epic` to see and set a character's chain.

### All-in-one Setup

- `LivingServerSetup.exe` installs Offline DAoC and Living Server for someone who has nothing, or adds or updates
  Living Server on a game they already have.
- It runs shadowofze's own downloader (pinned by SHA-256) and the Living Server package's own install and rollback
  scripts, so every safety check stays.
- Details are in [tools/installer/README.md](tools/installer/README.md).

### New loading screen and welcome art (owner 2026-10-28)

- The game's loading screen and Setup's welcome page use a new Living Server painting: a Midgard dwarf, an Albion
  knight with the chalice shield and a Hibernian caster (`tools/installer/art/living-server-splash-source.jpg`).
- The loading screen (`pregame/splash.mpk`) is replaced only over shadowofze's exact file, and it is backed up first.
  ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd put his back. Any other splash is left alone.
- The package ships the new file whole under `content/`: a content-patch entry can now name a file ("File") instead of
  byte changes, for files that are entirely Living Server's own. A damaged copy is refused before anything changes.

### Bot names in the 2003 style

- New bots are named the way a 2003 server's players named themselves (owner 2026-10-29: three in four in the 2003
  style, after research). Research of about 300 names from 2001-2004 rosters and forums is in
  [docs/research/BOT-NAMES-2003.md](docs/research/BOT-NAMES-2003.md).
- The mix:
  - made-up names, 35% (Hasys, Xegrek, Qwannox);
  - English compounds, 12% (Steeleater, Frosthide);
  - jokes, 10% (Mushypeas, Mrbiscuits, Captainjelly);
  - everyday first names, 7%;
  - class or race jokes, 3% (Sneakyfriar, Angrycabalist);
  - stretched letters, 2%;
  - xX names, 3% (XxShadowxX, Xxpainxx);
  - number names, 3% (Zerga420, Headshot91);
  - lore names, 25% (the realm's old syllable names, or real Old English, Norse or Irish ones).
- A name is never a word players say in chat (a bot named "Axe" would catch "anyone got an axe?"), never on the
  server's invalid-names list, never pop culture or a joke from after 2003, and never the numbers 88, 14, 18 or 69.
- The server and the launcher's + buttons share one generator (`OfflineDaoc.Shared.EraBotNames`, linked into the
  launcher). Bots you already have keep their names.

### The first half hour

Owner 2026-10-28: "I really want this update to feel like a major difference." A 3h20m session had seen two storylines
and two spam waves.
- A play session starts at the realm's first login, or after half an hour away.
- Then:
  - the first /faction scene comes 5-10 minutes in, and is a storyline episode when one can play;
  - a roleplayer speaks 6-18 minutes in;
  - a gold spammer turns up 12-30 minutes in;
  - a friend's first tell still comes 4-10 minutes in.
- Storylines: one episode per realm every 45 minutes at most (was 75), episodes 3-10 hours apart (was 6-20), a new
  arc every 2 hours (was 4).
- Gold spammers post 5-10 ads before the ban (was 3-6).

### /lstest: every Living Server moment on demand (GMs)

- `/lstest` (GM accounts) fires each part now, so a session can check what is rare by design:
  - storyline, debate, dunce, spammer, roleplayer, rumour;
  - tell, greet, gossip, memory, remember;
  - deeds, holidays;
  - Leeroy and a realm raid sized to its level 50s (`/lstest raid [dragon] [alb|mid|hib]`).
- Each says why when nothing fits.
- Details are in [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).

### Fixes from the in-game Living Server check (2026-10-28 and 2026-10-29)

**Storylines**
- A running arc's two bots had to be out of combat at the moment a scene was chosen, and grinding gamebots almost never
  both are, so arcs hardly ever moved on. Now they only need to be in the game; the scene waits for a busy one.
- A protagonist who dies mid-episode is waited for, not counted as gone, so the episode doesn't end halfway.
- An arc waiting on bots that are out of the game is dropped after a day (was three).

**Deeds**
- A witness mid-pull is retried after 15 seconds (was two minutes), or 60 while a /faction scene plays. A witness who
  logs out is replaced by another bot near you.

**Greetings**
- A bot picked its greeting at 700 units, past the 512 a /say carries, so a bot in between used up your greeting
  unheard. Bots now greet from within 400 units and speak within two seconds.

**Leeroy**
- A raid that reaches its boss all at once used to leave him no moment to charge. Now he jumps the pull.
- He shouts his charge in /faction as well as /yell, so the whole realm hears him go.
- The /faction comment after the raid ("cant believe that worked") never came, because the raid's member list was
  emptied before a speaker was picked. Fixed.

### Launcher and shortcut

- The **Living Server** tab comes second, after Active Population.
- Setup's desktop shortcut has its own icon, from the new loading-screen art.

### Play-test fixes (2026-10-27 and 2026-10-28)

Found by playing the Beta 1.1 test build on a fresh 0.37b install made by the new Setup.

**Resurrection**
- A rez offer that ran out unanswered made the bot's next offer cancel itself: it said "You decline to be
  resurrected", and accepting then said "No one is currently trying to resurrect you". Ten casts, no rez. Only the
  offer you are answering can now accept or decline, and an expired one is dropped.
- A gamebot no longer raises you into a live fight (you stand where it stands). As players do, it steps to a clear
  spot within rez range and raises you from there; with no such spot it clears what is fighting or would aggro first.
  Gamebot corpses keep the combat rez.
- A bot doesn't queue a second rez while one is waiting, and waits a minute after you decline.
- "You must wait 0 seconds" after an interrupt now shows the real wait.

**Your group**
- Groupmates beside you come along when you teleport, take a portal or release to your bind point, and a groupmate
  far behind paths to you. A stuck groupmate is brought back to you instead of leaving the group.
- "Come with me" gets an honest answer: on my way, or how far off the bot is.
- Fight talk comes only from groupmates who saw the fight, hunting talk only after a recent fight, and a reaction to
  you dying only from groupmates near you.
- A bot in your group talks in /group, not /say to strangers ("anyone running Stonehenge Barrows today?").
- A bot in your group no longer quotes its old solo hunt ("mountain algaes at Camelot Hills are the best xp").
- `/invite` by name says when a far-off bot is busy (riding a stable route, a siege, a raid) instead of "too far
  away".

**Answers**
- LFG with nobody near your level gets an honest reply; "where should I level" names your realm's starter zones; the
  dragon, time to 50 and the realm's raid get real answers; a "wtb" gets a trade reply that fits; insulting a bot's
  guild is read as abuse.

**`/tele mob`** ranks camps by their average level and lands you at the spawn nearest your level.

**Setup**
- A first install turns every Living Server feature on at our suggested settings (bot guilds, personalities, voices,
  all of server life, the suggested XP by level, the real-clock schedule), shown as a ticked option you can untick.
  A game that already has Living Server keeps its own choices.
- Shows the real error when the official download fails, and says when the connection dropped.
- Names the folder of a game program that is still running, and points to Task Manager when it has no window.
- Cleaning up after an install removes the "Offline DAoC setup files" folder too, with Setup's copy of the downloader.
- A new install proposes a folder that is free (`playable-v0.37b-2` when you already have `playable-v0.37b`). Choosing
  a folder that holds a game says so ("Offline DAoC 0.37b is already installed in ..."), and a folder problem offers
  CHOOSE ANOTHER FOLDER instead of only Try again.
- The log leaves out curl's progress fragments, which looked like errors.
- Replacing another build with the same name says "replaces it with this one" instead of "upgrades Beta 1.1 to Beta
  1.1".

### Ready for Offline DAoC 0.38

- A build can now fit several releases (`BaseRelease`, e.g. `0.37,0.38`). The installer accepts a game updated to any
  of them and refuses anything newer. It stays `0.37` until 0.38 is published and merged. Setup reads the same list:
  a game on any listed release takes Living Server as it is, and new installs and updates go to the newest.
- Beta 1.1 targets shadowofze's latest release, **0.37** (`v0.37`, `ab4df02`), and carries 0.38's server fixes from his
  `main` (`9da9091`); 0.38 itself is not published yet (checked 2026-10-28).
- [docs/UPSTREAM-RELEASES.md](docs/UPSTREAM-RELEASES.md) is the checklist for that day.

### Checked (Beta 1.1 so far)

- **Automated:** every branch above merged into `release/1.1`. On Linux the combined tree builds and all 4,093 server
  tests pass. CI on Windows runs the full suite and the launcher and installer tests, and builds the package.
- **In game (2026-10-27 and 28, test builds of this tree on a fresh 0.37b install made by Setup):**
  - Setup's new-install, resume after a dropped connection, and update-in-place paths work.
  - The server starts on the build, with no new error types against Beta 1.0.
  - Checked play:
    - LFG and starter-zone answers.
    - A groupmate follows you, comes along through `/tele` and a release, and buffs you.
    - `/tele mob` lands a level 1 at a starter camp.
    - A rez accepted after an earlier offer ran out now works.
    - A cleric in a respawning camp steps to a clear spot within range and raises you from there.
  - A full new install with the release/1.1 Setup on 2026-10-28: 4.8 GB from shadowofze's release, unpacked, updated
    to 0.37b and Living Server added in about 10 minutes with every step clean; cleanup; first server start
    (`FORK_BUILD name=1.1-beta`, only the stock world-data errors), automatic account, a new character, into the world.
- **Not checked here:** the epic chains' world script hasn't been run against a real clean 0.37b world (its test
  needs one).
- **In game, 2026-10-28 and 29 (test builds 2-5 on the owner's test copy, up to about 1,000 bots online, with `/lstest`):**
  - Seen working:
    - holiday chatter and replies (Christmas test date);
    - a gold spammer, banned after 7 ads; a roleplayer line, then mocked;
    - the dunce rage quit and its parting tell;
    - rumours with replies;
    - deeds told to /faction with reactions;
    - storyline episodes (romance, ninja-loot trial) with side-taking and both protagonists' tells;
    - greetings, gossip and tells from bots that remember you;
    - the new session's first half hour: debate at 6 minutes, roleplayer at 18, spammer at 22;
    - 2003-style names from the launcher (Waffleironer, Xxpainxx, Angrycabalist), shown in the world;
    - Leeroy charging Golestandt before the raid was ready, the raid following him in, a wipe and then the kill;
    - a natural Leeroy at Trollheim;
    - automatic raids opening in all three realms once each had enough free level 50s;
    - a 9.5-hour overnight run with no errors from this code.
  - Not yet seen: Leeroy jumping the pull, his /faction shout and his after-raid comment (fixed after the last test
    build); a storyline protagonist dying mid-episode.
- **Not played yet:** sieges at your usual bot count (their timers now run); quests and markers.
- **Known:** automatic raids open only with 80 free level-50 bots in the realm, so a small roster rarely gets them.
  A roster filled only with the Lv.1 and Lv.50 buttons has no bots between levels 20 and 49 until they level up.

## Beta 1.0 — 2026-10-22

The first release under the Living Server name, and the first public build since 0.36-se.1. Beta 1.0 is the build
developed as 0.37-se.2: everything below in this section, plus 0.37-se.1 and 0.36-se.2 to se.4, which were never
published on their own.

**Release (2026-10-22)**
- Renamed to **Living Server**: the launcher tab, the installer, the docs and this changelog
  (`CHANGELOG-SOCIAL-EXPANSION.md` points here). The "Living Server" feature group is now **Server life**. Server
  setting keys, database tables and saved settings are unchanged, so an earlier build's settings carry over.
- The build is named `1.0-beta` (`FORK_BUILD name=1.0-beta`) and still installs on Offline DAoC 0.37 / 0.37b;
  the release it fits is now its own setting (`ForkBuildInfo.BaseRelease`) instead of being read from the name.
- The installer now also fixes game files in place, from byte patches against the released files
  (`tools/fork-build/content-patches.json`, made by `make_content_patch.py`; 8 KB, no game file shipped whole):
  Hall of the Corrupt's `zone277.nav` (the Lord Gildas walk links below) and the eight new-client gate models
  (below). A file is changed only when it has the released fingerprint, the result is checked against the fixed
  one, and the original is backed up; `ROLLBACK.cmd` and `PREPARE-FOR-UPDATE.cmd` put it back. Installer tests
  cover a fixed, a missing and a hand-changed file, rollback, the return to the release and a damaged patch list.

**Checked (Beta 1.0)**
- Automated: 3,444 server tests, the launcher tests and the installer tests (Windows PowerShell 5.1) on the build
  from `main` (`71250e2`), the one in the release zip.
- The patches were applied to the released `zone277.nav` and the eight released gate models with the installer's own
  PowerShell code; every result matched its fixed fingerprint.
- On the owner's PC (0.37c game, 900 bots per realm, every switch on): the release zip installed over 0.37-se.2. It
  found the earlier build, backed it up, and reported the navmesh and gate files as "fixed already" (the test builds
  had fixed them by hand). The launcher shows the **Living Server** tab and "Server life (9 of 9 parts)". The server
  started with `FORK_BUILD name=1.0-beta commit=71250e2aa711` and the same 390 start-up errors as before, the memory
  census was switched off, and the owner's character logged in.
- Raids in the last two days on the builds before this one (all scheduled by the bots): 28 ended, 17 won.
- First raid on Beta 1.0: Albion's scheduled Hall of the Corrupt raid killed Aidon the Archwizard and then Lord Gildas
  (180 bots, 61 minutes). The night before, the same raid had stopped above his room with "Route blocked": the
  `zone277.nav` walk links work in game.

## Developed as 0.37-se.2 — 2026-10-20 to 2026-10-22

The endgame now runs without a player directing it: warbands open and defend keep sieges, each realm raids its
dragon, its epic dungeon, Summoner's Hall and Darkness Falls on a schedule, a wiped raid regroups and tries again with
the Epic Special Forces, and parties in dungeons go for the boss. Tested on the owner's PC with 600 bots per realm
overnight and through 2026-10-20. 0.37-se.1 was never published on its own, so this release also carries everything
in it (below).

From the owner's first day playing 0.37-se.1 (the server log of 2026-10-18: 4,721 bot tasks), then a live check on the
owner's PC the same night (a 20-minute and a 40-minute session on the new build, compared with the same minutes of the
evening's earlier 0.37-se.1 session; see "Checked" below).

**Body language (new server life part, off by default)**
- `bot_living_body_language` (launcher: Living Server > Server life > **Body language**): bots standing around in
  town, waiting for their group or resting look around, emote now and then, take a step or two in town, turn and
  wave at you when you walk up, and cheer a ding nearby. Only where a real player can see them, never in a fight.
  Bots can't jump, sit or strafe: the game sends no such state for an NPC. See [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).

**Safer routes for solo bots**
- 30% of bot tasks ended in a death, more than half of them before the bot reached its camp. Of solo bots that pulled
  an orange monster on their route, 34-54% were dead within 150 seconds (21-27% after a yellow; 7-18% after green or
  blue). Now a solo bot goes around a yellow when it can and gives up the camp rather than fight an orange (a single
  orange is still fought when it has just given up another camp). A solo bot that died in the last half hour treats
  a yellow like an orange. Groups are unchanged.

**Bots waiting for a party stand between pulls**
- 98% of bot tasks were solo: bots waiting for a party grind solo, and party formation only takes bots that aren't
  fighting, so 244 waits ran out after 20 minutes and only 23 PvE parties formed all day. A bot waiting for a party
  now often stands at its camp between pulls for 45-90 seconds ("Looking for a group between pulls"), like a player
  with /lfg on: 60% of the time at level 50 (which gets no experience from soloing), 35% while levelling.

**Chat found wrong in the live check**
- Replies to "lfg" no longer assume you lead a group: "lfg 50 cleric" drew "how many do u have?" in /faction. Lines like
  that still answer "lfm".
- A guildmate no longer asks what you just said: "lfg, anyone want a 50 cleric?" drew "what level and class?" in /gu.
- Gossip about a rez only comes from a bot you really rezzed, and never says "tonight": a bot with three groups and no
  rez in its memory said "Claudecheck rezzed half our group at Campacorentin Forest tonight".
- Bots no longer advertise the level 50 starter kits in /faction ("Does anybody want to make me an offer on Offline
  level 50 Cleric 41/33?").

**New client: gates whose portcullis never rose (2026-10-21)**
- The owner found Snowdonia Fortress's gate doors opening while the portcullis stayed down. The model converter took
  only a door piece's swing, and a portcullis slides, so it was exported with the same open and closed position. The
  converter now reads the slide too. Eight converted models were patched (the Albion and Midgard border keeps, two
  stone halls, Caer Diogel's bars, a Galladoria root barrier); checked in game at Snowdonia: both gates open and the
  north gate can be walked through. The new-client update must carry these eight files.

**A boss could stop the server (2026-10-21)**
- In the live run the Midgard raid (about 150 bots) fought Lydsyg in Iarnvidiur's Lair. His script recasts his area
  disease from OnAttackEnemy, and a spell landing on each target counts as an attack, so each cast set off dozens more:
  the server's memory went from 2.9 GB to 37 GB in ten minutes and the game stopped. 69 scripted encounters cast this
  way; with one player the chain dies out by itself. Now a monster can't ask for the same instant spell on the same
  target again within a second, or queue it twice, and keeps at most 16 cast requests. Bots and players are unchanged.

**Effects that outlived their NPC (2026-10-21)**
- The memory census found 2,755 spell effects still running on NPCs that had left the world, growing by about 400 an
  hour: a Sluaghbinder pet's pulse went on pulsing after the pet was deleted, and a pulse's snares on monsters only end
  when the pulse does. Each one kept its NPC in memory. Now an effect on a deleted monster, pet or bot ends, and so does
  a pulse whose caster was deleted. Players' effects are unchanged.
- The census also listed about 1,500 respawn timers on "deleted" monsters. Those are normal: a slain monster is
  deleted while it waits to respawn, and the timer brings it back.

**Chat that answers what was said (2026-10-21)**
- The owner found conversations still didn't make sense. Nothing in the log showed what was said, so the server log
  now has a chat transcript: `CHAT_LINE` for every bot line a player can see (/say near a player, /faction, /gu,
  /group, /send), with `via=` naming the code that chose it, and for every line a player says, with the intent the
  chat model read in it.
- An audit ran 55 typical player lines through the reply path. Many drew a reply that didn't answer them: "where is a
  good place to level at 24?" drew "take the horse from Camelot", "yes" drew "interesting", "sure" drew "lol what",
  "anyone in camelot?" drew "maybe maybe not", "i died again" drew "what?", "how much is a bloodied sword worth?" drew
  "not my class sorry", and "anyone want to do a dungeon?" drew a dungeon tip. Now:
  - "ok", "sure", "nice", "me too" and other acknowledgements draw no reply, and neither do "really?" and "why?" unless
    you name a bot.
  - A line nothing fits draws no reply, where it used to draw "huh", "lol what" or an old formal line ("Things seem
    steady from where I am."). A question nothing fits draws an honest "not sure".
  - Answers from the live world: "where should I level at 24?" names the zone where bots of that level are camping
    right now and a dungeon for the level; "where are the mids?" names the frontier zone with the most enemy gamebots
    in it (or says it's quiet); "anyone in camelot?" is answered only by bots that are there; "where are you?" in
    /say, /faction, /gu and /group gets the bot's zone, level and class, as /send already did.
  - "anyone want to...?" and "who wants to...?" get yes-or-no-to-coming answers (never "me me me" from a bot that won't
    come), price questions get price answers, "should I...?" and "is it worth...?" an opinion, "can you help me?" "sure,
    whats up?", and a complaint sympathy. An either-or question isn't answered "imo yes". A buyer isn't told to vendor it.
  - A question or request is no longer dropped because bots answered another line in the last 10-12 seconds ("hi" then
    "anyone want to group?" got nothing).
- In-game check the same evening: "anyone in camelot?" drew "im in Camelot Hills" (a place name now means that place
  when a zone or region is called that: Camelot is the City of Camelot), and "where are the mids?" drew two bots both
  naming Uppland, Midgard's own frontier (enemies on our side of the frontier are named first, then neutral ground,
  then their homeland, and such a question gets one answer). Re-checked in game on that build: still wrong. A bot on
  its way to Camelot counted as in Camelot (now only where a bot is counts), and the world data gives every frontier
  zone realm 0, so Uppland didn't read as Midgard's (the twelve frontier zones are now mapped to their realms, and
  enemies all at home are reported as "back home in Uppland").
- A guild no longer hears the same death or group line twice within the hour: in the live run "died. the run back is
  the worst part" came three times in /gu in 17 minutes, and "guild warband forming, frontier bound" twice 30 seconds
  apart. When every line of the set was heard lately, the bot says nothing. Greetings, congratulations and welcomes
  follow the same rule, and a number doesn't make a line new: on the next live run two guildmates both said "hey
  Claudecheck, good timing" 2 seconds apart, and "2 of us heading out to the frontier" was followed by "3 of us...".
- Bot-to-bot: an either-or question takes the choice answers, frontier news the frontier replies ("mids 8man in cathal"
  drew "same :)"), and "anyone else...?" an opinion rather than "count me out this time".

**Bots that logged out stayed in memory (2026-10-22)**
- The overnight run's 2,424 scheduled bot logouts left 1,500 deleted bots in memory, and Windows Event handles grew
  with them (about 1,000 an hour, from 2,200 to 9,800). The extended holder census found what held them: monsters'
  last interrupter and the attack targets they remember (and the same fields of bots holding deleted monsters), which
  nothing cleared after a fight. Every two minutes each living in the world now forgets objects that have left it:
  its last interrupter, remembered attack targets out of a fight, aggro holders and cached nearby-object lists, and a
  bot's recent attacker and route-threat pull. Damage credit (XP, loot, realm points) is left as it was: only a bot's
  or player's record of departed neutral monsters is dropped, which earns nothing. The first live run of the sweep
  still found monsters' aggro lists holding deleted bots; those are cleared too now.
- Live, two hours on the owner's PC: deleted bots still in memory after each full collection stay at 50-125 (overnight
  they only rose, to 1,500), and deleted monsters and pets level off too. The sweep takes about 60 ms every two
  minutes. Event handles still grow about 1,700 an hour, so they had another cause: a .NET Lock makes a Windows event
  the first time a thread waits for it and keeps it for the Lock's life. A test-run census (`SERVER_LOCK_EVENTS`)
  counts which fields hold such Locks. Live: of 2.1 million Locks, 342 had an event 20 minutes after start and 1,026
  after 50; from 12:08 to 12:38 they rose by 684 while handles rose by 858. Two thirds are the stat tables every monster
  and bot carries (`PropertyIndexer._lock`), then ability lists, stat multipliers and effect lists. These belong to
  objects still in the world, each Lock adds at most one handle, and the handle closes when its object is freed: about
  40,000 handles and a few MB of Windows memory a day, against a limit of millions. Not changed for this release; a
  lighter lock for the stat table is the candidate fix afterwards.

**Raid routes found broken in the overnight run (2026-10-22)**
- Hall of the Corrupt: Lord Gildas is reachable. The raid killed Aidon, then stood above Lord Gildas's room for 37
  minutes and ended "Route blocked". The ramp up from his room meets the main floor at a single 32-unit step, a little
  higher than the navmesh's walkable climb, so the mesh had his room as a separate area. Three short two-way walk links
  over that step in `zone277.nav` (one tile changed; new tool `tools/navmesh_splice/add_offmesh.py`). The patched mesh
  ships with the update; checked offline with the real Detour library (from the entrance, from where the raid gave up
  and from Aidon's room, there and back), not yet in a raid.
- Trollheim is raided again. Every staging post beside its portal in Gripklosa Mountains lay on a shelf next to the
  portal that joins neither the portal nor Bjarken, so the raid was never scheduled ("no corridor inside zone 152 to
  the staging post"). A post now has to be walkable from the portal. Offline: all 20 posts route from Bjarken; the
  Darkness Falls posts and Krondon's are unchanged.
- Cruachan Gorge: 23 Hibernian raiders late for Darkness Falls stood at one spot on the Breifine border for 33
  minutes before they found their own way. Their route proves offline from that exact spot, so the cause is not
  found yet; the warning they logged now says why the route failed, where it was going and how long the bot has
  not moved.

**Melee bots no longer stand under flying monsters**
- A melee bot only picks a flying monster it can walk up to. A level 50 Midgard melee group timed out 45 times on
  griffon gliders in Gripklosa from 2,400-3,500 units away, and one member stood still for 15 minutes. Casters and
  archers shoot flyers as before.

**Bots no longer walk past Aidon to reach the other frontiers**
- The four Old Frontiers dungeons (Hall of the Corrupt, Summoner's Hall, Dodens Gruva, Marfach Caverns) were the
  bots' walking road between realms. Albion warbands formed at Snowdonia Fortress, far from the Castle Sauvage
  porter, so they walked to Midgard and Hibernia through Hall of the Corrupt, past Aidon the Archwizard (level 75).
  In one hour about 118 of 895 bot deaths were in those four dungeons, 50 of them to Aidon and his illusions,
  and the same bots died there two and three times.
- Now a bot enters those dungeons only as a destination (or on its way out, or the neutral raid's own approach
  to Summoner's Hall). Warbands reach the other frontiers by their realm's porter: a distant warband rides or
  walks to it. Enemy bots inside those dungeons are fought where they're met, never hunted there.
- Side effect: Albion PvE parties no longer pick Dodens Gruva or Marfach camps, which they could only reach through
  two of those dungeons.

**Level 50s keep searching wide after a failed long pull**
- The 10-minute "look nearby first" pause after a pull that never reached its target now applies to levelling bots
  only. Level 50 camps are spread out: with the pause, level 50 kills per bot fell from 11.0 to 9.5 an hour.

**The endgame runs on its own: sieges and realm raids**
- A level 50 RvR leader now forms a warband of 4 to 8 (or roams alone), and a warband forms with the members it
  finds instead of not at all. Overnight 234 of 250 warbands were pairs, and only a warband of 4 or more can open a
  keep siege: one siege in 7.7 hours. Every siege opening is now logged (`RVR_EVENT_OPENED`).
- Automatic realm raids (the realm dragon, the realm's epic dungeon, Summoner's Hall and Darkness Falls) are sized
  to your roster: half the realm's online level 50s, at least 40 and at most the encounter's 200
  (`realm_raid_minimum_bots`, 0 = scaled). They needed 200 level 50s looking for a group in one realm, so with 150
  per realm none ever started. They now recruit level 50s waiting for a group and level 50s grinding solo (who
  earn no experience there); RvR bots stay on the frontier. Forced raids from the launcher are unchanged.
- Each realm opens a raid on a schedule (`realm_raid_schedule_minutes`, default 150; 0 = off), rotating dragon,
  epic dungeon and neutral raid: the first 20, 30 and 40 minutes after the server starts. Logged as
  `REALM_RAID_SCHEDULED` and `REALM_RAID_ENDED` (with the outcome, size and length).
- Found in the first live run: an automatic raid planned one party of eight at a time and waited until all eight
  were out of combat at once, so the first scheduled dragon rally stalled at 24 of 58 for twenty minutes. It now
  holds three planned parties and forms whichever eight are free first, and a whole PvE party of five to eight
  level 50s can join a raid that is recruiting (only a full eight still opens an unscheduled one).
- Four-hour live run (2026-10-19): Albion and Midgard killed their dragons with 66 and 72 bots, and Albion took
  two Hibernian keeps. But three of five scheduled raids never fought: 82-91 bots were recruited and only 46-51 were
  staged after 90 minutes (stragglers fighting trash at the entrance, still walking or released). A raid scaled to
  the roster now goes in late with 70% of what it needs (a full 200 raid keeps 90%): it can still lose the fight.
- Raid loot is logged (`REALM_RAID_LOOT` with the bot that received each piece, `REALM_RAID_LOOT_UNCLAIMED` when no
  nearby raider could carry it, `REALM_RAID_LOOT_MONEY`).
- Not yet known: whether 40-110 bots can beat encounters tuned for 200. Darkness Falls and Summoner's Hall bosses
  are level 64-80 and were barely scratched by 32 bots in an earlier test; their raid AI is not finished.

**Bots remember where a monster far above them killed one** (from stefanrows/OfflineDAoC)
- When a monster 10 or more levels above a bot kills it, the spot is remembered for 6 hours: bots that monster is
  still far above pick no camp within 3,000 units of it, and a levelling bot counts a straight walk past it as a
  long trip, so it usually picks another camp. In one hour a merucha camp (levels 30-33) on the Connacht road
  killed 18 bots of levels 10-17 on their way to other camps, and 126 of the 159 bots under level 20 that died to
  red or purple monsters hadn't reached their camp yet. Kept in memory only; a restart forgets.

**Levelling bots walk around monsters far above them** (route bends from stefanrows/OfflineDAoC)
- The remembered spots above didn't stop road deaths in the live test: the Connacht meruchas still killed 30 bots
  of levels 10-17 in two hours, some three times running from the same bind point, because roads to camps all
  over Hibernia pass them. 22 of the 30 had seen the pack and chosen the old one-monster sidestep or a new camp.
- Now a solo levelling bot on the road bends its next 3,000 units around every red or purple monster in sight
  (700-1,650 units, wider the higher it is, whether or not it's already fighting someone) and around remembered
  death spots, taking up to 6,000 extra units, every bend checked on the navmesh first. Logged as
  `AUTONOMOUS_DANGER_BYPASS`.

**Every realm attacks, and sieges are contested**
- On 2026-10-19 Albion opened all four bot sieges and took every keep: one server-wide roll decided when a siege
  could start, Albion's siege-sized warbands formed first after each raid (its raids ran ten minutes ahead), and
  with one siege running the next had a 4% chance a minute. Each realm now rolls for its own attacks, and a realm
  that hasn't attacked for 30 minutes gets an extra 2% a minute (at most 50%). Raid slots drift by up to 20 minutes
  so the realms don't keep the same order.
- Defenders could bring half the attacking army (12-17 against 42-53), so every siege fell. The new setting
  `rvr_siege_defense_share` is 0.75 (the stored `rvr_siege_defense_ratio` of 0.5 is no longer read), and the last six sieges steer it by up to 0.25: attackers winning more than 80% bring more defenders,
  less than 45% fewer. The target is a siege won about two times in three.

**A wipe is not the end of a raid: regroup and try again**
- In Galladoria on 2026-10-19 the raid took Conservator (level 77) to 45%, wiped, and he reset to full health. The
  second try reached 42%, nobody damaged him for five minutes, and the raid walked past him with nothing to show.
- The epic encounters keep their full strength. Every named encounter, not only the final one, now waits until
  enough of the raid has gathered, and the raid goes in together. A wipe is when half the raiders in the fight fall,
  or the encounter heals back to 95% after being taken to 85% or lower.
- After a wipe the raid regroups. The fallen release to the hub and walk back, and the living rest and rebuff for at
  least three minutes. The raid goes in again once 60% of its roster stands at the gathering point, or 40% after 20
  minutes. The realm hears that the raid fell and is going in once more.
- After two failed attempts on the same encounter the raid is **Defeated** and goes home (a new outcome in the
  launcher's Herald). The named encounters it killed are listed in the record. Every realm hears it, like a victory.
- An encounter the raid is still standing around is not skipped because it took no damage. One nobody can reach or
  hurt is still set aside as before.
- Logs: `REALM_EXPEDITION_WIPE`, `REALM_RAID_WIPE`, and `attempt=`/`retry=` on `REALM_EXPEDITION_FINAL_ENGAGE`.

**The Epic Special Forces**
- Each realm has an elite guild, the **Epic Special Forces**. When a raid in an epic dungeon, Darkness Falls or
  Summoner's Hall wipes, up to 24 extra level 50s arrive at the raid's hub, in three parties of eight with the same
  endgame kit and full training as a level-50 /spawn companion. They walk in with the fallen and join the retry, and
  every realm hears that they answered.
- They are needed less as the realm wins. A dungeon with no record gets 24; each win there sends one party fewer next
  time and each loss one more, counted over that dungeon's last six results in the Herald. After three wins in a row
  nobody comes. An expedition defeated at that dungeon the last two times brings them from the start.
- They are the one exception to gamebots earning their armor. They live in memory only: they are never saved, never
  counted in the population or a guild roster, take no loot or coin (with only them nearby a drop stays where it
  fell), claim no server firsts, and leave when the raid ends. A restart leaves nothing behind.
- Dragon raids don't call them yet.
- Logs: `REALM_RAID_SPECIAL_FORCES_ARRIVED`, `_NOT_NEEDED`, `_FAILED`, `_LEFT`, and `specialForces=` on `REALM_RAID_ENDED`.

**From the first 600-per-realm night (2026-10-19/20)**
- The regroup and the Epic Special Forces worked in the game: Galladoria's raid wiped at Conservator (taken to 9%),
  24 Special Forces arrived, and the retry killed him; it then beat Spindler Broodmother and Tine on their second
  attempts and was defeated at the Organic-Energy Mechanism. Albion killed Legion; Hibernia (with the Special Forces
  after a wipe at Summoner Roesia) and Midgard both killed Grand Summoner Govannon.
- **Rallies out of time go in short.** Three Albion rallies failed with 90/132, 101/149 and 103/150 staged while about
  90 more recruits were still on the road. A raid scaled to the roster now goes in at its deadline with half of what it
  needs (at least 40), and the rest follow it in (`REALM_RAID_SHORT_START`). Full 200 raids are unchanged.
- **Siege defenders follow the real army.** The defense share was taken of the army that marched (10-60 bots) while
  attacker reinforcements grew it to 70-105: Dun Bolg's defense was capped at 25 against 89, and attackers won every
  siege that finished. Defenders now answer the larger of the marched and the current army.
- A regroup after a wipe waits for 70% of the raid (was 60%) and at least five minutes (was three): the second try at
  the Organic-Energy Mechanism went in five minutes after 152 of 156 raiders fell and did worse than the first.

**From the overnight report (2026-10-20)**
- **Contested raids stay; the kill decides.** Two realms' raids can still be in Summoner's Hall (or Darkness Falls)
  together and fight each other. The final encounter's kill now goes to the raid that did the most damage to it
  (`REALM_RAID_FINAL_CREDIT`); the other raid ends "Beaten to it" and gets no loot. Midgard had been credited with
  Govannon while every drop went to Hibernians.
- **Tuscaran Glacier.** When an epic monster that is not the raid's target attacks the raid, the raid turns on it
  (`REALM_EXPEDITION_TARGET_SWITCH`): Elder Icelord Suttung killed 115 raiders while they waited at the Birghir muster.
  A muster that stays empty for 20 minutes is set aside and tried later (`REALM_EXPEDITION_MUSTER_UNREACHABLE`)
  instead of holding until the four hours run out.
- **Parties no longer pull epic monsters they didn't come for.** In dungeons an epic monster on the route was pulled
  like any corridor monster; eight-bot parties pulled Black Lady (level 65) in Marfach Cavern on the way to and from
  Summoner's Hall, and she and her Ogress adds killed 195. Now they go around it when the navigation mesh has a way
  outside its aggro, and otherwise walk past without pulling (`AUTONOMOUS_DUNGEON_EPIC_AVOIDED`). A raid inside its
  own dungeon still fights its encounters.
- **Raiders walking to their expedition alone bend around danger.** On the way to the Caer Sidi staging area 124
  level-50 raiders died, mostly to dread liches and ancient zombies, and each death sent them 110,000 units back to
  Fort Gwyntell; 25 to 37 were still walking when both rallies ran out of time.
- **Level-50 bots use danger memory.** Route bends around monsters far above the bot were for levels 1-49 only, and a
  bot now also remembers its own deadly places: three deaths within 1,200 units in three hours make a spot it bends
  around and picks no camp beside (`AutonomousRepeatDeathSpots`). A level-50 Animist had died 72 times to one glimmer
  pack near its Sheeroe Hills bind.
- **New bots level in their classic zones first.** No XP camps in the Shrouded Isles below level 10: level-1 Midgard
  parties were sent to Aegir's Landing, where scimitar cats (levels 18-21) killed level-1 bots 201 times in three hours.
- **Home defense comes first.** A warband attacking an enemy keep now breaks off to defend its own realm's keep
  when that siege has begun and the defense is under half its cap, as long as the attack keeps half its army
  (`RVR_HOME_DEFENSE_BREAKOFF`). Raids are untouched: their members are not RvR forces. Albion took Midgard's
  Blendrake Faste with no defender ever arriving while about 80 Midgard bots kept attacking Caer Sursbrooke.
- Committed attackers now actually re-check home defense every 30 seconds, also while mustering, marching or at
  the siege (the first version only checked between plans; in the final test Hlidskialf Faste fell with no Albion
  defense while Albion's army mustered against Dun Crimthain, and one solo bot was the only break-off): a warband in a shared siege never
  re-planned, so the break-off was never evaluated (Caer Hurbury fell in 13 minutes with no Albion defense while 62
  Albion bots attacked Dun Da Behnn).
- **No defense muster once the attackers are at the keep.** Defenders go straight in. At Blendrake Faste the first
  defenders formed 26 minutes into the siege, mustered for six minutes, never marched, and the keep fell.
- **Dragons drawn out of their lair go home.** A dragon takes no damage more than 2,000 units from its lair, but
  its tether is 2,500, so bots kept fighting it in between. Gjalpinulva stayed at 100% for an hour with 130-170
  Midgard raiders on her and killed 55. Bots now treat a dragon outside its lair like a boss outside its tether
  (stop, lead it home), and the raid forms at the lair instead of following the dragon out.
- **Out of the glimmer loop.** Repeat-death memory alone didn't stop it: the release went to the nearest bind
  (about 2,300 units from the pack) and the walk to Pheuloc's horse skipped the route checks. A bot that keeps dying
  in one place now releases to a bind at least 6,000 units from that spot, its walk to a horse bends around danger,
  and it won't board a horse that stands where it keeps dying.
- **Parties clear dungeon bosses.** None of the 178 named monsters in the classic and Shrouded Isles dungeons was
  ever a goal. Dungeons are not made more attractive: a party of three or more that has ground its dungeon camp for
  five minutes also goes for that dungeon's boss, once per task, when the boss is a kill target of a real quest within
  three levels of the party's (the quest table: Favonius Facilis for "Willing Sacrifice" at 15+, Te'Bui for "Lord Lauur's Wife"
  at 35+, the Muire brothers for "Rest in Peace" at 17+) or has its own drop table, and the fight suits the party
  (orange at most for 3-5 bots, red for six or more; below level 20 one level up is already orange). It returns to its camp when the boss dies or after 15
  minutes (`AUTONOMOUS_DUNGEON_BOSS_GOAL`, `AUTONOMOUS_DUNGEON_BOSS_RESULT`).
- **Torst no longer disappears.** His eddies' brain set its body's speed before it had a body; the error removed
  Torst from the world the moment he summoned them.
- **The raid schedule is a random roll.** Each scheduled raid is drawn at random from everything the realm has
  available (its dragon, epic dungeon, Summoner's Hall, Darkness Falls and the raid dungeons below), setting
  aside only the raid it last won. A raid that failed can come straight back. Before, the schedule went dragon,
  epic dungeon, neutral raid in turn, and never repeated the last raid it started.
- **Nine more raid dungeons.** Each realm also raids its other multi-group dungeons: Albion Krondon (Orylle), Avalon City
  (Dura'ek the Empowered), Crystal Cave (Xanxicar) and Hall of the Corrupt (Aidon the Archwizard and Lord Gildas); Midgard Trollheim
  (Nosdoden), Iarnvidiur's Lair (Iarnvidiur) and Dodens Gruva (Aros the Spiritmaster); Hibernia Tur Suil (Balor) and
  Marfach Caverns (Black Lady, Chieftain Caimheul and Avartack). They run like the epic dungeons: the way in is
  cleared for the first hour, then the named encounters, the final boss last; ordinary monsters stay down ten minutes
  while the raid is inside, and a wipe regroups with the Special Forces. Raid sizes are capped at 64-160 by dungeon.
  Each musters at the nearest service settlement (Caer Diogel, Wearyall Village, Bjarken, Hagall, Aalid Feie; the
  Catacombs dungeons by the way to Summoner's Hall use its hubs). On the installed navmeshes a path from each
  entrance to the final room was proven offline. Crystal Cave is entered only through Avalon City: its raid stages
  outside Avalon City and walks through it, the way a Summoner's Hall raid walks through Hall of the Corrupt (the
  path from each Avalon City entrance to the Crystal Cave portal was proven too). Not raided: Fomor City (its
  navmesh has no path from the entrance to Myrddraxis's chamber).
- **A raid by tight ground fills the slots that fit.** Krondon's first raid gathered 7 parties at Caer Diogel and
  waited 90 minutes for an eighth: beside its portal in Aldland only 7 formation slots in a row had a staging post,
  and the raid only ever asked for the next slot. A slot with no post is now set aside and the next one tried, and
  each slot widens its search ring after twelve tries (25 of 38 slots find a post by the Krondon portal, from 19).
- **An encounter that takes no damage stays set aside.** In Krondon the raid skipped Scurceol Hyrde (immune while
  his four Lyft Miht orbs live) and switched straight back to him because he was attacking it, every minute for over
  an hour. A skipped encounter is no longer picked again for attacking until its retry time.
- **A rally leaves its hub with half the raid after 30 minutes.** The Midgard Summoner's Hall rally recruited 102
  for a full 200 raid, never reached the 120 needed to leave early, and failed with nobody staged.
- **A full 200 raid also goes in short at its deadline** with at least half staged. At 900 bots per realm every raid
  is a full 200, and the Hibernian Darkness Falls rally failed with 170 staged (the late start needs 180).
- **A dungeon boss goal always logs its result** (`AUTONOMOUS_DUNGEON_BOSS_RESULT`), also when the party disbands or
  takes another task first; before, the night's only goal (Mutilator Zurabo) left no result.
- **Deleted turret pets were kept in the bots' turret list** until the owner's next pet upkeep (252 held at once).
- **Fixes from the first night on this build:** two healer crashes when a group was disbanded mid-check (a null
  resurrection scope and a null group in the cure check), and the launcher's status queue kept duplicate ids in its
  priority queue (65 to 5,079 overnight).
- **Native memory leak in pathfinding.** In every 600-per-realm run the server's memory outside the .NET heap
  grew about 250 MB an hour (3.3 to 6.1 GB overnight; the working set went from 5.4 to 10 GB). The pathfinder kept
  one native Detour query (about 0.7 MB) for every thread and zone and never freed it: each .NET pool thread that
  checked a town route built its own set, lost when the thread retired, and each game-loop worker kept one for
  every zone it had touched. Queries are now pooled per zone and lent for one call (at most one idle query per
  processor thread is kept per zone). `SERVER_MEMORY` also logs `privateMB`, `nativeMB`, `threads`, `poolThreads`,
  `handles` and the live and created query counts (`navQueries`, `navQueriesMade`).

**Checked**
- Automated: 3,223 server tests.
- Live, on the owner's PC (new client, 650 bots online), first 39 minutes of the new build against the same 39
  minutes of the earlier session:
  - Body language: 78 looks, 82 emotes and 12 greetings were sent to the player's client. Bots standing at the
    Camelot Realm Exchange were seen turning and gesturing. In the build before this one (without "standing around")
    nothing was counted in 20 minutes and bots at the Exchange stood still. No steps or cheers yet (no town break
    or ding happened near the player).
  - Solo bots fought far fewer oranges and yellows on their routes (45 and 133, from 163 and 475).
  - Deaths: level 50 solo tasks ending in a death fell from 35% to 27%; all tasks from 30% to 28%. Most deaths are
    bots of levels 1-9 in the starting zones, and about half of all deaths still happen before the bot reaches its
    camp, with no route threat logged before them. That is not fixed yet.
  - Groups: 13 PvE parties (11 before) and 72 waits that ran out (54 before); no clear change yet. About 15% of
    online bots were in a party at the end of the check (48 RvR, 47 PvE), so "98% of tasks are solo" overstated how
    alone bots are: solo bots start many short tasks.
  - No new server errors (390 at start-up, as before).
  - The chat fixes above came after the check and haven't been played yet.

## 0.37-se.1 — 2026-10-18

The Social Expansion on shadowofze's **0.37 / 0.37b** (with or without the 0.37c new client), with everything in
0.36-se.4. 0.36-se.4 stays the build for 0.36.

**Moving from 0.36**
- 0.37 is an update to a 0.36 game, and it applies only to the game's own files. `PREPARE-FOR-UPDATE.cmd`
  (new, in this build's folder) puts the released 0.36 server and launcher back from the installer's backups,
  whatever was installed over them, and checks each one. Then update the game, then run `INSTALL.cmd`. Your saves,
  bot guilds, bot memory and settings stay in place.
- If you install 0.37 fresh and bring your progress over instead, the game's transfer leaves out the Social
  Expansion's tables. `python tools\claude-version\carry_social_tables.py --from <old game folder> --apply` copies
  them (it backs up first and refuses until the game's own transfer is done).

**Guilds: the base game's and ours together**
- 0.37 lets you invite gamebots into your own guild (they show your guild name and emblem, and talk in your guild
  chat, but don't count for merit, keep claims or buffs). That is now the one way bots join a player's guild. With bot
  guilds on, a member of a bot guild answers your /ginvite for itself (a leader or officer stays, a busy bot or one far
  from your level says so) and leaves its bot guild when it joins yours.
- Bot-run guilds are unchanged. Their members now also wear their guild's emblem on cloak and shield (0.37's emblem
  code).
- A bot's answer to your /ginvite is said aloud when you are near, and otherwise sent to you (/send), since you can
  invite by name from anywhere.
- With bot voices on, bots in your own guild talk from the voices corpus too: their lines are answered in kind, and
  your /gu gets answers like /faction and /group. With voices off they keep 0.37's own lines.

**Bot chat**
- "what are you up to?", "wyd", "whats everyone doing?" are answered with what the bot is doing (camping its camp,
  heading somewhere, selling with full bags, standing around in town...), and "what is everyone up to tonight?" with
  plans. Before, they read as "how are you?" or as nothing ("lol what").
- Developers: our class `AutonomousBotGuilds` is now `AutonomousBotRealmGuilds`, because 0.37 added its own
  `AutonomousBotGuilds`. Database tables are unchanged.

**Tested**
- **Automated:** the full server suite on the merged code (3,176 tests), the installer tests with five new ones for
  `PREPARE-FOR-UPDATE`, and seven tests for `carry_social_tables.py`.
- **Live, scripted:** the merged server on a copy of a played save (cloud workspace, Linux), with a scripted player
  that typed in /gu and /faction and used /gc invite and /gc remove. A se.4 membership in a player guild moved to
  0.37's table at start; a bot guild member joined (and said goodbye to its bot guild), an officer and a level 50
  said no, an unguilded bot joined, /gc remove worked, guildmates answered /gu, and no new server errors.
- **Upgrade on a real install** (the owner's 0.36c game with se.4, Windows, 2026-10-18): PREPARE-FOR-UPDATE put the 12
  released files back, shadowofze's 0.37c update applied, and INSTALL.cmd installed 0.37-se.1. The first INSTALL refused the
  updated game (0.37's update removes the editions folder the installer checked); fixed before release, see below. The 27
  bot guilds and 617 memberships, the 300-bot roster per realm and the Social Expansion settings came through, and the server
  logged the same start-up errors as before the update.
- **In game, briefly:** logged in on the new client; guild chat greeted the character and answered "what are you up to?"
  with "not much, you?" and "the usual, killing stuff". Not played for long.

**Found in the owner's first play, then fixed**
- A level 50 in a level 50 RvR bot guild got no answer to "lfg" in /gu and /faction or to "where is everyone?": the chosen bots
  were in fights when their replies came due, and a reply to a player had no stand-in. Now a busy bot's reply goes to the
  next free bot (a bot you named still answers itself), a question to your own guild always gets an answer, and "where is
  everyone?" is answered with where the bot is ("Pennine Mountains", "at Black Mtns. South, decent xp"). Checked again in
  play: all three got answers.

**Installer fix found in that upgrade**
- INSTALL.cmd now recognises a game updated by a patch: the release is the newest docs/RELEASE-<version>.md in the game's
  manifest (the editions check remains for the 0.36 full downloads). Three new installer tests.

## 0.36-se.4 — 2026-10-18

Bot chat, reworked so the world feels alive and answers you. Same game versions as before (0.36 /
0.36b, with or without 0.36c). Install it over 0.36-se.3 or earlier, or a clean game. Most of this
needs **bot voices** on (the launcher's Social Expansion tab); what changes with it off is marked.

**Bots understand what you type**
- Short chat is read for what it is: "ty", "gz", "lol", "brb", "wb", "inc", "rez pls", "wts", "lfg",
  "ding 20", "is friar good?", "are you a bot?". Before, most of these read as real-world talk, and in
  /say they got no answer at all. On 260 test messages, 233 are now read right. *(Also with voices off.)*
- Saying "dumb question" or "this camp is garbage" no longer counts as an insult that bots remember;
  an insult has to be aimed at someone. *(Also with voices off.)*
- "where is the scout trainer" names the trainer and the zone. Place answers match whole words
  ("camelot" no longer finds a monster called Mel) and sound like a player, not a guidebook.
  *(Also with voices off.)*

**Answers that fit**
- Every line a bot starts says what kind of answer it invites. A boast gets "nice, show off", a
  complaint "ugh, same", a question an answer (a real one for places and trainers), and "brb dinner"
  nothing. Before, replies were picked by guessing the topic from the words, and about two in three
  didn't fit.
- "Fair enough. Safe travels." is gone. The bot who started a talk sometimes closes it in its own
  words ("ty", "k will try that", "heh yeah"), and never after a question.
- What you say gets answers of the right size: a hello or a ding in /faction gets a few, a "lol" usually
  none. "gz" is answered by the bot that just levelled. Only a bot that will really come and rez you
  says "omw".
- Replies take longer to type when they're longer. Unprompted lines come at uneven gaps (2-4 minutes
  in /faction), and about one bot in seven is a regular who talks more, so you get to know names.
- Typo-prone writers sometimes send a "*word" correction.

**Your group and your guild talk**
- Bots in your group chat in /group now and then, out of combat: pulls, breaks, loot, "where to next?",
  jokes. They said nothing before. Bot groups near you talk among themselves in /say; their
  "pull?" and "rez pls" no longer go to the whole realm in /faction.
- Bot guild chat comes from the corpus: about 140 lines by guild type instead of a dozen, with the
  no-repeat window. Guildmates answer a guild line when it asks something, not "haha true" to anything.
  *(With bot guilds on, voices on or off.)*

**More to say**
- The corpus has grown from 979 lines to 2,296. New lines: replies to short chat, replies for each kind
  of opener, closings, dungeon talk (there was none), four more lines for every class, battlegrounds,
  group and guild talk, the frontier and the capitals.
- Lines know where and when they are said. "tonight" reads "today" in the daytime, bedtime talk waits
  for the night, and "anyone want to duo?" isn't said by a grouped bot.
- Lines repeat far less: the no-repeat window is three days instead of six hours (six hours still apply when
  a kind of line runs out), and it is saved across server restarts. A player who plays every evening heard
  about a third of the last evening's lines again before.
- A question whose answering bot got pulled into a fight is answered by another bot. "is friar good?" is
  answered about friars, or in general, not about necromancers.
- Removed later internet phrases ("change my mind", "fight me") and fixed lines that named another
  realm's class.

**Found in a live test, then fixed** (the se.4 server with a scripted player typing in every channel; see
[SE4-CHECKS.md](docs/bot-voices/SE4-CHECKS.md))
- Bots in a capital no longer think they are in a dungeon ("lfg City of Camelot", "this place is a maze").
- Your own /group lines get answers from your groupmates. A group standing in a capital talks about selling and
  heading out, not "pull?" and "oom".
- Questions get answers of their kind: "anyone on?" ("here o/"), "how is everyone?" / "hows it going"
  ("good, slow night though"), "mind if i hunt here?", "cleric or friar?", "where to next?" ("your call"),
  "anyone know if x is ours?" ("last i saw, yes"), "what level is everyone?", "tell me your best death
  story", and "what does lfg mean?" (a short list of chat words). A plain "hi all" gets a hello.
- A reply doesn't ask what was just said ("paladin 6 here" -> "what lvl?"), and "tell me about it" answers
  complaints, not good news.
- A /faction scene and unprompted lines wait a moment after you speak, so they don't read as answers. Scene
  roles can require levels: a level 50 no longer says "i am not 50 yet".
- In /faction the realm answers you, not just your groupmate, and the same few bots don't answer everything.
- "where is camelot?" finds Camelot; trainer answers name the nearest trainer.

**Tested**
- **Automated:** the full server suite (3,144 tests, about 190 for se.4) and the corpus checks pass locally.
- **Live, scripted (not a person playing):** five sessions on the real server with a scripted player, about
  100 minutes in all; what it found is above and in [docs/bot-voices/SE4-CHECKS.md](docs/bot-voices/SE4-CHECKS.md).
- **In game:** not played yet.

## 0.36-se.3 — 2026-10-18

Fixes from a three-hour playtest (a new Albion Scout from level 1 to 5, every feature on). Same game
versions as before (0.36 / 0.36b, with or without 0.36c). Install it over 0.36-se.2, 0.36-se.1 or a
clean game.

**Bot chat**
- A bot no longer seems to answer its own /faction line. Its closing "Fair enough. Safe travels." is
  said only after somebody actually replied; before, it went out even when every reply was dropped.
- A bot in a group no longer asks in chat who wants to group "just for the company".
- Asking for a guild below the level bot guilds take members ("lf guild" at level 3) gets an answer
  saying when to come back, once every 10 minutes, instead of silence.
- "where are you?" by `/send` gets the bot's zone, level and class.

**Logs**
- The rez-ask line is written only when a player could have answered it, and a bot's normal release
  is logged at Debug. Together they were about 8% of the playtest's server log.
- `AUTONOMOUS_GROUP_MEETUP_DEATHS` gives the real wait; it always said 900 seconds.

**Tested**
- **Automated:** the full server suite on CI, with new tests for each fix.
- **In game:** the playtest that found these is in the docs; the fixes themselves haven't been played yet.

## 0.36-se.2 — 2026-10-17

Fixes from an audit of the first release. Same game versions as before (0.36 / 0.36b, with or
without 0.36c). Install it over 0.36-se.1 or a clean game.

**Docs said too much**
- "With everything off it plays exactly like 0.36" wasn't true. Talking to bots by name and two
  bot fixes are always on; the README, the zip's README.txt and the docs now say so.
- New [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md): what's always on, what hasn't been seen in game
  yet, and known problems. The zip ships it instead of the internal release plan.
- The README now has **Getting started** (make bots first, which switches to try, where to check)
  and **Undo and uninstall**. It also says plainly that testing used the 0.36c new client only.

**Removing bot guilds**
- `python tools\claude-version\set_bot_guilds.py --purge --apply` (server and game closed) removes
  the guilds bots formed: it releases keeps they claimed, ends alliances they led, moves your
  characters in a bot guild back to the starting guild and deletes the bot guilds. It backs up the
  database first, refuses while the game is running, and changes nothing on a second run. Without
  `--apply` it only shows what it would do.
- With bot guilds off, the server no longer rewrites bot guilds' ranks and dues at every start.

**Fixes**
- `/invite <name>` no longer pulls a bot out of a siege, a relic run, a realm event or a realm raid,
  or off a stable route; you're told why.
- The log line on every bot death (with a stack trace) is now written only when the Living Server's
  rez part is on, and the stack trace only for moves other than the bot's own release.
- Founding a bot guild and recording heard debates now write to the database under the shared
  write lock, and the debate history is written in batches off the game loop.
- A bot guild's invite box is shown from the right thread.
- The voice lines load at server start (in the background) instead of the first time a bot speaks.
- Bot memory and nemesis rows for deleted characters are removed at start, and old one-kill
  nemesis rows every 6 hours instead of only at start.

**Tested**
- **Automated:** the full server suite on CI, plus 8 Python tests for the purge. The purge was
  also run on a copy of a played database (15 bot guilds, 3 alliances, one character moved back).
- **In game:** not yet. These changes haven't been played.

## 0.36-se.1 — 2026-10-17

The first release, for Offline DAoC **0.36 / 0.36b** (with or without the 0.36c new client). It
replaces the server (`GameServer.dll`) and the launcher (which gets a **Social Expansion** tab).
Every feature starts **switched off**. (Corrected in 0.36-se.2: talking to bots by name and the
two base-game bot fixes below are always on.)

**Features**
- **Bot guilds.** Each realm's bots form guilds with a purpose (leveling, social, RvR, raiding,
  crafting). They wear guild names, talk in /gu, group with guildmates and recruit you when you
  ask "lf guild", even from your realm's starting guild. Guilds claim and defend keeps, pay dues,
  earn merit, buy guild buffs and form alliances. You can also /ginvite bots into your own
  guild. See [docs/BOT-GUILDS.md](docs/BOT-GUILDS.md).
- **Bot personalities.** Six types (Leveler, Socialite, Daredevil, Explorer, Crafter,
  No-lifer) and five traits tilt how each bot spends its time. `/personality` on a bot shows
  its type.
- **Bot voices and /faction debates.** 979 lines written for 2002–2003, chosen by realm, class,
  level, situation and personality, in five writing styles. 296 multi-bot /faction scenes, one
  every 30–45 minutes while you play. A busy speaker is waited for, so scenes play to the end.
  See [docs/BOT-PERSONALITIES.md](docs/BOT-PERSONALITIES.md).
- **XP by level range.** Separate player and bot rates for level bands. See
  [docs/XP-BY-LEVEL-RANGE.md](docs/XP-BY-LEVEL-RANGE.md).
- **Living Server.** Bots remember you (greetings, thanks, gossip by name). Dings get grats, and
  server firsts are announced. Dead bots ask you for a rez, and passing healers rez you. Frontier
  bots call enemy groups in /faction. Bots keep nemesis records. Bots log on and off on a
  schedule, take "brb" breaks and now and then go linkdead. The **Camelot Herald** is a news page
  of your world. Each part has its own switch. See [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).
- **Talking to bots by name.** `/send <bot>` reaches an online bot of your realm, and it answers
  with a tell. Ask to join and it tells you to invite it: you lead, bots join. `/invite <name>`
  invites a nearby bot (same region, within 6,000 units). `/target <name>` targets a nearby
  friendly bot.

**Fixes to the base game's bots** (also with every feature off)
- A bot that dies 3 times on its way to its group's meetup now counts as a no-show. Before, a
  low-level group could spend the full 15-minute wait dying (about 70 deaths in one case).
- Dead bots no longer jump to their spawn point the moment they die. Their corpse stays where it
  fell until they release.

**New client (0.36c) fixes, in the source only.** The target window's FACE, FOLLOW and STICK
buttons, `/target <name>` and `/assist` now work, and a second `/stick` or `/follow` turns it off.
These are in [source/new-client](source/new-client); this release's zip doesn't include new-client
files.

**Tested**
- **Automated:** 2,911 server tests pass on CI.
- **In game** (owner's PC, new client, 300 bots per realm, every feature on):
  - Seen working: guild recruiting and alliances, debates, frontier alarms, breaks, bot memory and
    gossip, rez asks, a bot rezzing you, grats and server firsts, tells, /invite and /target by
    name, and XP by level for players. Bot XP by level raises bots' XP in game, but the exact
    factor wasn't measured; see docs/XP-BY-LEVEL-RANGE.md.
  - Not yet seen in game: keep claims, guild merit, linkdeads, nemesis chat lines, you rezzing a
    bot, and the meetup fix.
  - The original game client wasn't tried.
