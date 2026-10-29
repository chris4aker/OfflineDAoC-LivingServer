# Server life

Part of the Living Server fork. The goal (Chris, 2026-10-16): make the autonomous gamebots feel
like players on a 2003 server, through continuity and consequence more than through more chat.

**Status:** released in 0.36-se.1. Automated tests pass (see "Tests" below). **Partly checked in
game** (2026-10-17, see "In-game check" below). Every part starts **off**. With everything off, the
only changes left are the always-on ones listed in [KNOWN-ISSUES.md](KNOWN-ISSUES.md).

The server life parts change only autonomous gamebots; your `/spawn` companions and real players
are not affected by them. (One always-on fix does reach companions: a dead bot's corpse now stays
where it fell until it releases.)
Lines come from the bot-voices corpus (`living_server.lines` and `memory.lines`), in each bot's
own writing style. They work whether or not "Bot voices" is on, and they are only said where a
real player can hear them (your group, `/say` near you, your realm's `/faction`, a guild with you
in it).

## Switches

All are in the launcher's **Living Server** tab, card **SERVER LIFE**. Save with the server
stopped; they are used at the next start. The server keys (category `bots`) are:

| Switch | Key | Default |
|---|---|---|
| Bots remember you | `bot_living_memory` | True (since Beta 1.1) |
| Grats | `bot_living_grats` | True (since Beta 1.1) |
| Server firsts | `bot_living_firsts` | False |
| Rezzes and corpse runs | `bot_living_death` | False |
| Frontier alarms | `bot_living_alarms` | False |
| Nemeses | `bot_living_nemesis` | False |
| Bot schedule | `bot_schedule_mode` | `off` (also `session`, `clock`) |
| Breaks and linkdeads | `bot_living_breaks` | False |
| Body language | `bot_living_body_language` | False |
| Beggars | `bot_living_beggars` | True |
| Deeds and reputation | `bot_living_deeds` | True |
| Storylines | `bot_living_storylines` | True |
| Spammers and roleplayers | `bot_living_characters` | True |
| True rumours | `bot_living_rumours` | True |
| Holiday chatter | `bot_living_holidays` | True (Beta 1.1) |
| Leeroy in raids | `bot_living_leeroy` | True (Beta 1.1) |
| Bots remember each other | `bot_group_bonds` | True (server key only; not in the launcher tab yet) |

**ALL OFF** in the tab switches every part off, along with the other Living Server features.

## What each part does

### Bots remember you (`bot_living_memory`)
- A bot of your realm remembers:
  - grouping with you (noted at most once per 30 minutes per bot);
  - you rezzing it;
  - you insulting it in chat (the chat model's "abuse" intent).
- Its mood toward you follows from that: friendly, neutral, or cool after insults.
- What it does with the memory:
  - **Greets you** when you pass within about 400 units: at most one greeting every 3 minutes, and
    the same bot at most every 45 minutes. Groupmates don't greet.
  - Sometimes **thanks you for an old rez or a good group**, naming the place.
  - **Answers you by name** (most of the time) when you talk near it, in `/faction`, or by its
    name. A cool bot gives you a short brush-off. Insults and out-of-game talk keep the old replies.
  - Now and then (every 30–60 minutes per realm) a bot that likes you **mentions you in
    `/faction`**, never one standing next to you.
- Memory is chat only. It never changes who groups with whom, and it never refuses you.
- Kept in the table `offline_bot_memory`.
- Not built: memory of heals and trades. Lines for them exist but are never chosen.

### Grats (`bot_living_grats`)
When you or a bot gains a level:
- 1–3 bots in the group say grats in `/group` (only when a real player is in the group).
- Bots standing nearby say grats in `/say`: 0–2 for your dings, 0–1 for a bot's (only when a real
  player is near).
- At level 50, 1–3 bots of the realm cheer in `/faction`.

Each character gets at most one grats chain every 20 seconds. This is separate from guild grats
(bot guilds), which stay in `/gu`.

### Server firsts (`bot_living_firsts`)
- **What counts:**
  - the first character on the server, you or a bot, to reach level 50;
  - the first of each class to reach 50;
  - the first to each realm rank from 2 up.
- **How it's announced:** everyone gets one line on screen and in the system window. Other realms
  see "a Hibernian champion" (or "an Albion…", "a Midgard…"), not the name. In the achiever's realm, 1–2 bots cheer in `/faction`.
- **Recorded:** in `offline_server_firsts`, and shown in the Camelot Herald.
- **Turning it on in a played world:** titles someone already holds are marked "reached before
  records began" (no name, never announced). So the next character to get there isn't wrongly
  called the first.
  - This happens once: at the first start with the switch on, and again only if the switch was
    off in between. Bots made later with the launcher's **+ Lv.50** button never reached 50, so
    they don't take the level 50 titles.
- **Not built:** "first kill of a big named monster".

### Rezzes and corpse runs (`bot_living_death`)
- **When a bot dies:**
  - If a real player of its realm who can rez is within about 2,500 units, the bot asks that
    player for a rez by name, after a few seconds: in `/group` if you're grouped, in `/say` if
    you're close, otherwise as a `/send`.
  - "Can rez" means a Cleric, Friar, Healer, Shaman, Druid, Warden or Bard who has learned a
    resurrection spell (from the spells it lists, or a resurrection spell in its spell lines at
    or below the line's level).
  - The bot's corpse stays where it died until it releases, so it can be rezzed there.
  - With nobody who can rez near, a bot sometimes asks generally ("rez pls?", "anyone able to
    rez, dead by the spirit hound") in `/group`, or in `/say` when you're close.
  - The bot then waits the long release timer (90 s) instead of 20 s, even if you aren't in its
    group.
- **Releasing and being rezzed:**
  - A bot that releases may say it's running back from bind.
  - A bot in your group sometimes grumbles about resurrection sickness. Bots don't actually get
    the illness; this is chat only.
  - A rezzed bot thanks the rezzer by name. This replaces the old "My thanks... formation" lines
    for autonomous gamebots.
- **Bots rez you:**
  - An autonomous gamebot that can rez, and isn't in your group, rezzes you when you lie dead
    within 1,500 units of it.
  - You get the normal accept/decline box. Once a box from a bot closes, no bot offers again for
    about 90 seconds.
  - It never rezzes during a fight (its own or its group's), or with hostile monsters within about
    700 units of you. Only one bot works on a corpse at a time.
  - A bot on a siege job, holding a siege position, carrying a relic or in an RvR event force
    doesn't come. A bot that can't reach you within 20 seconds gives up on you for 2 minutes.
  - Groupmates already rezzed you before; that is unchanged.
- **Server log:** every ask is logged as `LIVING_SERVER_REZ_ASK` (with who could have rezzed),
  every rez a bot starts on you as `LIVING_SERVER_REZ`, and a rez bot near your corpse that
  doesn't rez says why as `LIVING_SERVER_REZ_SKIP` (at most every 15 seconds per bot).

### Frontier alarms (`bot_living_alarms`)
- **When:** a bot in the frontier or a battleground sees 3 or more enemy players or gamebots
  during its normal threat scan. It calls them in `/faction`, for example "inc 8+ mids at Caer
  Benowyc, healer, warrior, skald".
- **What it says:**
  - the head count is rough (exact below 10, then 10, 15, 20);
  - up to three classes;
  - the place is the nearest frontier keep within 6,000 units, or the zone.
- **Limits:** at most one alarm per realm every 2 minutes, and one per place every 5 minutes. None
  while a `/faction` debate is running.

### Nemeses (`bot_living_nemesis`)
- **Recording:** a bot killed by an enemy player or enemy gamebot records the killer (pets count
  for their owner) in `offline_bot_nemesis`.
- **Complaining:** from the second kill by the same enemy, the bot may complain in `/faction`,
  once per 20 minutes at most.
  - Enemy names are hidden in the frontier, so it names the killer by race and class: "that troll
    berserker is camping Caer Benowyc".
- **Revenge:** when the bot later kills that enemy, it may gloat in `/faction`.
- **Herald:** the Camelot Herald's **Most Wanted** lists the enemies with the most recorded kills.
- **Size:** each bot remembers at most 8 killers (a new one replaces its least-deadly one), and
  with this switch on, one-kill rows older than a week are forgotten at server start.

### Bot schedule (`bot_schedule_mode`)
- `off` (default): every bot stays online, as before.
- `session`: each bot plays sessions of 1–4 hours, with 20 minutes to 2 hours away. Timing is
  counted from server start. About two-thirds of the roster is online at any time.
  - No-lifers play longer and are away less; Socialites play a little longer.
- `clock`: follows your PC's clock like a 2002 North American server.
  - About 95% of the roster is online from 8 to 10 p.m.
  - About 20% at 4–5 a.m.
  - Weekends (from Friday 6 p.m.) are busier in the daytime.
  - The same bots stay on as the evening fills up; no-lifers stay up late.

**How logging out works:**
- Only bots that can leave without breaking anything log out: ungrouped, alive, out of combat, not
  casting, not on a stable route, not walking back after a release, not carrying a relic, not on a
  siege job and not in an RvR event force. A scheduled-off bot that is busy stays until it's free.
- At most 3 bots log out every 5 seconds.
- A bot may say goodnight first: in guild chat when a guild member can hear, otherwise in `/say`
  near you.
- Scheduled-off bots aren't logged back in until their schedule says so. When they return, some
  say hello. Nobody greets during the first 10 minutes after the server starts.
- Busy scheduled-off bots (often in a bot group) stay online until they're free. For their first
  15 minutes they count against the schedule, so logins wait for them (with 10% slack); after that
  they no longer hold back the bots whose turn it is, so the total can run above the schedule's
  number for a while, but never more than a quarter above it.

### Breaks and linkdeads (`bot_living_breaks`)
- **Breaks:** an idle bot (the same rules as logging out) now and then says "brb" and logs off for
  5–25 minutes, about once per 4 hours online. When it comes back it says "back".
- **Linkdeads:** a bot in a group with you rarely goes linkdead.
  - How often: at most once per group every 45 minutes; on average about once every hour and a
    half of grouping.
  - What happens: it stands still and does nothing for 30–90 seconds. Another bot in the group may
    say "X went ld", and the bot comes back with "sry ld".
  - Never during a fight: it wakes at once if anyone in the group fights, if it's attacked, or if
    the group breaks up. Bots on a stable route, a siege job, a relic or an RvR event force never
    go linkdead.

### Body language (`bot_living_body_language`)
Bots standing around stop standing like statues. Only autonomous gamebots do this, only where a
real player can see them, and never in a fight, while casting, stealthed or on a horse route.
- **In town** (a town break): every 8–25 seconds a bot mostly looks left or right (30–150
  degrees); about one time in five it plays an emote (ponder, shrug, laugh, nod, point, flex,
  clap, cheer, wave, beckon, salute, drink, now and then a dance) and one in five it takes a step
  or two around its spot (it stays within 60 units). So a bot emotes about every minute and a half.
- **Waiting** for its group, formed up at a rendezvous, or standing between pulls while it looks
  for a group: every 10–30 seconds it looks around, and one time in four emotes (ponder, shrug,
  "let's go", point, nod, flex, beckon); it never steps, so formations stay put.
- **Resting between pulls:** now and then looks around.
- **Standing around anywhere else** (at a merchant, the Realm Exchange or a trainer, choosing its next
  goal, holding on a road): after 5 seconds standing still with no enemy targeted, it looks around and
  emotes now and then, like waiting. It never steps there.
- **Greeting:** a bot of your realm that you walk up to (within 650 units) usually turns to you
  and waves, bows or salutes; it doesn't greet you again for 20 minutes.
- **Cheers:** when you ding, one to three bots standing nearby turn to you and cheer, clap or
  throw a victory pose; a bot's ding nearby gets at most one.

What it can't do: bots can't jump, sit or strafe. The game's updates for an NPC (which a gamebot
is) carry its position, heading and speed but no jump or sit state, and the new client puts NPCs
on the ground. All emotes used are ones the new client animates (ids 1–46); the original client
hasn't been tried.

### Beggars (`bot_living_beggars`)
About 4% of each realm's Crafter bots (4-5 a capital with 900 bots a realm) live as beggars in their own capital. They beg you and
passing bots for coin and loot, sell what they are given and hoard every copper. Drag coin or an item onto one to give
it; the gift really moves to the bot. Ask anyone for gold or items, on any channel, and for the next hour every beggar
in your capital that hears of it or sees you follows you around the city insulting you and defending its corner
until you leave the city. Beggars never group, fight for
the realm or talk in /faction. Details: [bot-voices/TALKING-UPDATE.md](bot-voices/TALKING-UPDATE.md) section 11.

### Bots remember each other (`bot_group_bonds`)
Added 2026-10-27, the groundwork for the bots' memory of one another. Only autonomous gamebots are remembered here;
companion bots and real players are not.
- **Recording:** when a PvE party ends after starting its task, every member remembers every other in
  `offline_bot_bonds`: grouped, a good run (the task ran its full time with at most one wipe), and wipes together. When
  a member never reaches the meeting point, the members who waited remember it as a no-show.
- **Feelings:** a score of 2 per good run, 1 per party (up to 5), minus 1 per wipe and 2 per no-show. Two good runs
  make friends (5 or more); two no-shows make a grudge (-4 or less).
- **Matchmaking:** a leader takes its guildmates first, then its friends, then the nearest. A leader and a bot with a
  grudge either way don't group until one of them has waited 12 minutes.
- **Size:** each bot remembers at most 40 others; the least memorable go first.
- **Logs:** `BOND_FRIENDS` and `BOND_GRUDGE` when a pair crosses a line, and `friendPairs=` on each
  `AUTONOMOUS_GROUP_FORMED`.
- **Talk** (since 2026-10-27, `bonds.lines`): a bot tells its realm's /faction when a groupmate becomes a friend, now and
  then when a new party has an old friend in it (40%), and grumbles about a no-show (50%). One line per topic per realm
  every 10 minutes, with up to two reactions. Logs `BOND_TALK`. The Herald doesn't show bonds yet.

### Deeds, storylines, spammers, roleplayers and rumours
- **Deeds and reputation** (`bot_living_deeds`): bots that see you solo a named monster, go on a kill streak, get zerged
  or die to something silly tell /faction and others react. It builds a reputation the realm brings up, and a nickname.
- **Storylines** (`bot_living_storylines`): pairs of bots live out four-episode dramas in /faction over days (a kill steal,
  a guild split, a romance, a ninja-loot trial). Name one of them during an episode to take a side; the ending follows you.
- **Spammers and roleplayers** (`bot_living_characters`): a gold spammer floods /faction now and then until it is
  banned; a few bots always speak in thee and thou and get mocked for it.
- **True rumours** (`bot_living_rumours`): a rare named monster that just spawned, or an enemy keep with its guards down.

Details: [bot-voices/TALKING-UPDATE.md](bot-voices/TALKING-UPDATE.md) sections 12-15.

### Holiday chatter (`bot_living_holidays`)
Around a holiday on your PC's calendar, bots talk about it the way 2003 players did, in /faction, /say and your group:
candy and trick-or-treaters, costume parties, turkey comas, Christmas lists, New Year resolutions. Each realm has its
own flavour: Samhain in Hibernia, Yule and Midsummer in Midgard, All Hallows' Eve and midwinter in Albion. A "happy
halloween" or "merry christmas" from you is answered in kind. About one realm line in ten is a holiday line in the days
around a holiday, and about one in five on the day itself.

| Holiday | Days | The day itself |
|---|---|---|
| New Year | 30 December - 2 January | 31 December, 1 January |
| Valentine's Day | 12-14 February | 14 February |
| St Patrick's Day | 15-17 March | 17 March |
| April Fools | 1 April | 1 April |
| Easter | Good Friday - Easter Monday | Easter Sunday |
| Midsummer | 19-24 June | 21 June |
| Halloween | 24 October - 1 November | 31 October |
| Thanksgiving (US) | Tuesday - Sunday around the fourth Thursday of November | the Thursday |
| Christmas | 18-26 December | 24-25 December |

The lines are in `data/chat/holidays.lines`; the calendar is `AutonomousHolidays`.

### Leeroy in raids (`bot_living_leeroy`)
One end-game raid in twenty (dungeon raids and dragons) has a raider who can't wait. While the raid musters for its
final encounter, once a quarter of the raid is there but not all of it (half, at a dragon), he yells
"LEEEEROOOOOOY JENKINS!" (in /yell and in /faction, so the whole realm hears it) and charges in alone (a raid that arrives all at once gets him too: he jumps its pull). The boss is fighting now, so the raid goes in after him with whoever is
there; a few raiders react in /say ("oh my god he just ran in", "stick to the plan!!") and one tells /faction. The
raid's own rules take it from there: a wipe still regroups and tries again. After the raid a raider tells /faction how
it went ("thanks a lot ..." or "cant believe that worked"). He is a damage dealer, never a healer or support class, and
usually a Daredevil or No-lifer. Never on a retry after a wipe. Logs `RAID_LEEROY_ROLLED`, `RAID_LEEROY`,
`REALM_EXPEDITION_LEEROY` (dungeons) and `REALM_RAID_LEEROY_START` (dragons).

### Bot names (always on)
New bots are named the way a 2003 server's players named themselves:
- **Three in four in the 2003 style:**
  - short made-up names (Grizlas-style);
  - English compounds (Souleater-style);
  - jokes and puns, everyday first names, class or race jokes, and the odd stretched name (Droooood);
  - xX and number names (XxShadowxX, Zerga420).
- **One in four lore names:** the realm's old syllable names, or a real Old English, Norse or Irish name.
- **Never:** a word players say in chat (a bot named "Axe" would answer "anyone got an axe?"), a name the server's
  invalid-names list rejects, pop culture, or a joke from after 2003.
- **Who gets them:** bots made by the launcher's + buttons and by the server. Bots you already have keep their names.
- **Details:** the research and the mix are in [research/BOT-NAMES-2003.md](research/BOT-NAMES-2003.md).

## New client (0.36c)

Everything here runs in the server and the launcher, so it works the same with the original client and the Godot
client. Checked on 2026-10-17 with `tools/newclient-compat` (network level, no game world): the Godot client's own
network code and interface received every server life chat line and both yes / no boxes from the server's own
packet code. Every line landed in the right channel and chat box, server firsts showed on screen, and the server
read the ACCEPT answers. Result: 30 of 30 passed.

One gap is the Godot client's, not this build's: it has no guild window yet, so a bot guild's member list can't be
browsed there. Guild chat, guild names over heads and the invite box work.

## Camelot Herald

**CAMELOT HERALD** in the Living Server tab writes a news page from the game database and
opens it in your browser. It reads only and is safe while the server runs.
- **Where:** one self-contained file at `runtime\herald\herald.html`. Nothing is loaded from the
  internet.
- **What it shows:** realm points leaders, guilds, keeps held by enemies, relics, server firsts,
  Most Wanted, and recent realm events.
- **Releases:** the page names your characters, so the release builder never ships the `herald`
  folder.

## Data and releases

New tables, all empty in a release and carried by the progress transfer when both databases have
them:
- `offline_bot_memory`
- `offline_bot_nemesis`
- `offline_server_firsts`
- `offline_bot_bonds` (2026-10-27)

Writes are batched off the game loop every 5 seconds, under the same database lock as bot status
saves. The release builder deletes every `bot_living_*` setting and `bot_schedule_mode`, so
releases ship with everything off.

## Checking it in game: /lstest (GMs)

Most of these parts are rare by design, so a play session may never show one. `/lstest` (GM accounts only: tick
**Make me GM** in the launcher's Options with the server stopped) makes one part's next moment due now. It then runs
through the part's own code, lines and logging, in your realm and around you.

| Command | What happens |
|---|---|
| `/lstest list` | Every part, on or off |
| `/lstest storyline`, `debate`, `dunce` | The next /faction scene starts within seconds: a storyline episode, any debate, or a dunce rage quit. When every storyline is waiting for bots that are out of the game, `storyline` drops the one that has waited longest so a new one starts |
| `/lstest spammer`, `roleplayer`, `rumour` | A gold spammer starts; a roleplayer speaks; a bot tells the realm about a rare named up in your region |
| `/lstest tell` | A bot that fits starts a tell to you now, or you're told why none fits |
| `/lstest memory` | What every bot remembers about you |
| `/lstest remember` | Your target (or the nearest gamebot) remembers two good groups with you; walk away and back to be greeted |
| `/lstest greet`, `gossip` | A remembered bot near you greets you; a bot that likes you mentions you in /faction. Each happens now, or tells you why not |
| `/lstest deed <solo, group, streak, ganked, silly, fell>` | A bot near you tells /faction it saw you do that (your reputation counts don't change) |
| `/lstest holiday <name, off>` | Bots treat today as that holiday until `off` or a restart |
| `/lstest leeroy` | The next realm raid has a Leeroy (start one with `/lstest raid dragon`) |
| `/lstest leeroy now` | A raid with a Leeroy that is gathering for its final boss charges at once |
| `/lstest raid [dragon] [alb, mid, hib]` | A realm's automatic raid (yours unless named) opens now, sized to its free level 50s (at least 40; raids from the launcher need 300) |

## Tests

Automated (no running world):
- **Server, `UT_AutonomousLivingServer`:**
  - defaults are off;
  - schedule parsing, session lengths and online share, and how often a bot's state changes;
  - the clock curve and that clock mode keeps the same bots online;
  - mood rules, alarm head counts, race and realm words, first claims;
  - tables save changes and deletions to a real SQLite file and load them back;
  - nemesis and alarm lines fill every slot; greetings follow mood;
  - this document lists every key.
- **Server, `UT_AutonomousBotVoices`:**
  - every server life kind has lines for every personality;
  - topic filtering;
  - the six-hour no-repeat window.
- **Launcher:**
  - `SocialExpansionSettingsTests`: the new keys and defaults match the server, the schedule mode
    is read like the server reads it, and ALL OFF;
  - `HeraldPageTests`.
- **Corpus:** `build_corpus.py --verify` and `check_runtime_names.py` in CI.

## In-game check (2026-10-17)

Played on the owner's PC with build 0.36-chris.3 in the Godot client (0.36c), all eight switches
on, schedule `session`, 50 bots made with the launcher. About two hours, an Albion Cleric.

**Seen working in game:**
- **Grats:** on a ding with two bots in the group: `[Party] Hareralfrey: "gratz"`,
  `[Party] Maraenisette: "grats Claudecheck, go train :P"`.
- **Server first:** reaching realm rank 2 put "Server first: Claudecheck is the first on the
  server to reach realm rank 2" on screen and in the system window. Two bots cheered in `/faction`.
- **Memory:** grouping was saved within seconds (`offline_bot_memory`). A former groupmate passing
  by said "had fun at Camelot Hills with you Claudecheck, again sometime? :P", and later "Claudecheck!
  we should group again sometime".
- **Rezzes:** a groupmate that died asked in `/group` "anyone able to rez, dead by the spirit
  hound", then after releasing said "long run back to Camelot Hills :)".
- **Schedule:** bots logged out (one said "gn all" in `/say` beside me, out of combat) and came
  back about 25 minutes later. The server log has 10 `LIVING_SERVER_LOGOUT` lines in two hours,
  two of them breaks (`reason=brb`); I wasn't near those two, so their "brb" wasn't seen in chat.
- **Nemesis:** a Hibernian Enchanter killing an Albion bot at Hadrian's Wall was saved with race,
  class and place.
- **Camelot Herald:** the page listed the server first, the realm point leaders and Most Wanted
  (two Midgard bots with two kills each).

**Bugs found and fixed after this check:**
- A bot that knew you answered by name only about one time in five: its known-player replies were
  mixed with the general replies. They now have their own topic, and there are known-player
  replies for every ordinary intent (test `ARememberedPlayerIsAnsweredByNameForEveryOrdinaryIntent`).
- Every server start re-seeded the "reached before records began" titles, so bots made at level 50
  with the launcher took "first to level 50" and all the class titles. Seeding now happens once
  (test `FirstsAreSeededOnceSoBotsMadeAtFiftyLaterDontTakeThem`).

**Not seen in the first check** (both fixed and then seen in the second check, below):
- A bot rezzing me. Four tries near a level 50 Friar or Cleric: one bot was logged out by the
  schedule, one stayed in a fight, and two walked on while travelling.
- A bot asking me for a rez by name. With my level 50 Cleric (Revive learned) beside a bot that
  died, no ask came.
- Frontier alarms, the nemesis complaint and revenge lines, the "brb" line and linkdeads: not
  seen in two hours. They need a frontier fight, repeated kills or an hour or more in one group.

**Second check, builds 0.36-chris.5 to chris.8** (same PC, Godot client, schedule `off` so the
level 50 healers stayed online). The new log lines found two causes:
- **"Can rez" was false for a Cleric 50 with Revive.** It only looked at the spells the client
  lists, and those didn't include Revive. It now also checks the character's spell lines for a
  resurrection spell at or below the line's level (chris.5). The log then showed `can_rez=True`.
- **A dead gamebot's corpse moved to its spawn point within milliseconds.** When a bot dies its
  brain stops, and stopping a brain sent the body "home". `LIVING_SERVER_DEAD_MOVE` showed
  `by=ABrain.Stop deadMs=33`. So the bot asked for a rez from a corpse that was no longer beside
  you, and no player or bot could reach it. This probably also broke bot-to-bot rezzes in bot
  groups before this branch. A dead gamebot now stays where it died. Its release still moves it
  to its bind, at 20 s (or 90 s when someone near can rez), and a rezzed bot's brain starts again
  (chris.8).

Seen in game with chris.8:
- **A bot asks me by name:** a level 4 Friar that died 655 units from my Cleric sent "Claudecheck
  rez pls?". The log has `LIVING_SERVER_REZ_ASK ... rezzer="Claudecheck" died_at=... now_at=...`
  with the same position twice. The corpse stayed there for the full 90 s, then released.
- **Every other bot death** in that session also kept its corpse in place until the release at
  21 s. No `by=ABrain.Stop` moves were logged.
- **A bot rezzes me:** I died beside a level 50 Cleric bot that wasn't in my group. It said "rez
  incoming", the log has `LIVING_SERVER_REZ bot="Seraorith" corpse="Claudecheck" distance=6`, and
  the accept box appeared. My first accept came after the stock 15-second offer had expired.
  About 90 s later it offered again ("got you Claudecheck, one sec lol"); I accepted and was
  rezzed.

Not checked: a player rezzing a dead bot, and its thank-you line. In the Godot client, Revive had
no target ("You must select a target for this spell!"). Whether the client clears the target or
hides the corpse is **not confirmed**: I click slowly, and a dead bot's corpse was visible in the
2026-10-17 play session. The original client wasn't tried.

**Godot client gaps seen** (the client's, not this build's):
- The FACE, FOLLOW and STICK buttons sent `/face`, `/follow` and `/stick`, which the server
  doesn't have. **Fixed in chris.10** and checked in game.
- The client ignored the server's ChangeTarget packet, so `/target <name>` and `/assist` never
  changed your target. **Fixed in chris.10** and checked in game with `/target Rhuz`.
- Targeting a corpse to rez it: not confirmed (see above).

**Third check: a play session, build 0.36-chris.9** (2026-10-17, 14:17 to 15:37, Godot client).
All eight switches were on, the schedule was `session`, and each realm had 300 bots (150 at level
1, 150 at level 50). I played Claudecheck (GM, Cleric 50) and judged results from the server log
rather than my own reaction times.
- **Seen working:**
  - Two debates played to the end. Each waited once for a busy speaker (`BOT_DEBATE_WAIT`), so the
    chris.9 wait is now seen in game. The second started 36½ minutes after the first ended.
  - 9 frontier alarms in `/faction`, such as "11 hibs at Nottmoor Faste".
  - 52 "brb" breaks and 218 session logouts.
  - 87 new nemesis records, one kill each; no revenge yet.
  - Bot memory: Maraenisette remembered grouping with me (Grouped=3), and gossiped about me by
    name in `/faction`.
  - Maraenisette joined my group, followed me and buffed me.
  - No errors after start.
- **Not seen:** a linkdead, nemesis chat lines, a keep claim, and me rezzing a bot.
  - Linkdeads roll only for bots in your own group (1 in 90 every 30 s, then 45 minutes'
    cooldown). I was grouped for about 30 minutes, so not seeing one is about a coin flip.
- **Found and fixed in 0.36-chris.10** (pull request #8). Checked in game afterwards unless
  marked otherwise:
  - **Tells to bots.** Bots say "pst me" (38 corpus lines), but `/send <bot>` answered "not in the
    game". A tell now reaches an online bot of your realm, and it answers with a `/send`. "hey,
    how is it going?" got "hey good to see someone else out here".
  - **Join requests.** Players lead and bots join, so a tell asking to join is answered with
    "invite me", or "meet up and inv me" when the bot is far away.
  - **Invite by name.** `/invite <name>` found only companion bots. It now invites a nearby
    autonomous bot (same region, within 6,000 units). Farther away it says "too far away to join
    you. Meet up first."
  - **Target by name.** `/target <name>` now finds a same-realm bot nearby, for players too.
  - **The new client's target buttons.** See "Godot client gaps seen" above.
  - **A second `/stick` or `/follow`** on the same target now turns it off, as in the old client.
    (Checked with FACE, which uses the same code: "now facing", then "no longer facing".)
  - **Guild gossip.** "we should get Claudecheck into a guild" was said while I was in a bot
    guild. It is now said only about a player with no guild; a starting guild counts as none.
    (Unit test only: gossip comes about once every 30–60 minutes.)
- **Found in stock 0.36 code (not server life), fixed in 0.36-chris.11, not yet seen in game:**
  - **A group could die on its way to a meetup.** A Midgard group of five bots, levels 1 to 3, spent
    the full 15-minute meetup wait dying near its meetup point: about 70 deaths in all. Then it was
    dropped as no-shows. Now a member that dies 3 times during one meetup wait counts as a no-show
    at once, and the usual no-show rules shrink or end the group. The log has
    `AUTONOMOUS_GROUP_MEETUP_DEATHS`. Tests: `DyingThreeTimesOnTheWayToAMeetupIsANoShow`,
    `MeetupDeathsCarryOverASmallerPartyButNotANewMeetup`.
  - **"lfg" got answers like "we have room for 3"**, but bots never invite players. The two lines
    that promised a bot's group now offer to join yours ("cleric here, where are you? invite me").

Suggested checks that remain:
1. Turn on **Grats**, group with bots and level: groupmates say grats in `/group`.
2. Turn on **Rezzes and corpse runs** on a rez class, in the original client: rez a bot that
   asked you by name, and it thanks you. (The ask and a bot rezzing you were seen with chris.8.)
3. Turn on **Bots remember you**: group with a bot for a while, leave, and walk past it later. It
   greets you by name.
4. ~~Frontier alarms~~: seen in the chris.9 play session (9 in 80 minutes).
5. Set **Bot schedule** to `clock` and start the server at different times of day: the online
   count in the launcher follows the time.
6. Turn on **Breaks and linkdeads** and group with bots for an hour: watch for "ld". ("brb" was
   seen in the chris.9 play session.)
7. Click **CAMELOT HERALD**.
