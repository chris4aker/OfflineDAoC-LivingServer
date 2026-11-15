# Living Server Beta 1.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for `v1.2-beta`, "Where Legends Wake". It is a draft for
the owner's approval: the "How it was tested" section is finished after the owner's played check. In the file table the
Setup row must keep exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the
exe's SHA-256. The zip's SHA-256 is filled in by hand when publishing. Publish it as Latest, not as a pre-release: the
launcher's update check reads GitHub's latest release, which leaves pre-releases out. Lines marked "[if it lands: ...]"
describe work still on its own branch: keep them only if it is merged before the tag, and drop the marker. Lines marked
"[owner: ...]" are for the owner to fill in before publishing. Nothing here names the Easter eggs or the project's
internal vocabulary. Polished on 2026-11-14 (evening) for the launch, covering everything merged into 1.2 through #224:
the raid flow and boss tactics from the sweep, the Darkness Falls raid AI, Legion, the raid plans for every realm's big
bosses and the open-world bosses, the route blockers and the period research. The upstream boss fixes and the epic
armor rule (#225) merged the same evening. The encounter counts are from docs/encounters/EPIC-ENCOUNTERS.md on
integration/1.2 at #224. Owner's decision (2026-11-14): credit lightly, by project and username in the Credits section
only, never per feature or per piece of code; the licence notices stay in THIRD_PARTY.md and the changelog. The
repository description, the tagline, the posts and the pitch are in [RELEASE-1.2-COPY.md](RELEASE-1.2-COPY.md).

---

### Where Legends Wake

*The bosses remember their rules. The realm remembers their stories. And the bots want you in the group.*

It's eight o'clock and you're medding at Svasud Faste when /faction goes still.

Someone has asked the realm's storyteller for the tale of Gjalpinulva. Two others tell the channel to hush. The tale
comes line by line; a listener asks a question, and the next lines answer it. Then nobody types for half a minute.

A line crosses the middle of your screen. A horn sounds. Midgard is calling its raid.

A tell arrives: "forming for Gjalpinulva at Svasud Faste. want a spot?"

You say yes. When she hurls you high over her lair, you are not the only one in the air, and the healers turn to you
first. Somewhere behind you, the casters are already running down her messengers.

That is Living Server 1.2. It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client, like 1.1
to 1.1.4.

### What's new in 1.2

- **Full-strength epics.** 15 of the bosses' original rules are back on. Throws, jails and glares now land on bots too.
- **A raid plan for every big boss.** 111 encounters, 108 of them new or reworked in 1.2, from Legion to the Glacier
  Giant.
- **37 raids on the schedule.** 16 open-world giants now draw a realm raid of their own.
- **Darkness Falls, with a battle plan.** High lords, princes, Legion, and the long walk out of Behemoth's hall.
- **A story for every boss.** 578 bosses and 107 places, about 600,000 words, checked against 832 Camelot Herald
  articles.
- **Bots that play with you.** They invite you, lead the group, and save you a raid spot.
- **Bring your friends.** Host your bot world for a few friends, and take your character to theirs. Experimental.

### The epics, at full strength

For years the hardest bosses ran softened so that bot raids could win. Not anymore.

- **The old rules are back.** The horsemen heal when dragged from their rooms. Morbus hides behind his swarm. The
  Skeletal Sacristan walks his round and never stops. Soul Reckoner shrugs off damage while a soul lives. Aroon's Scaiths
  die in order or not at all. Beatha heals Xaga. Kvasir drinks cold. King Tuscar and Queen Kula heal each other.
- **15 of 18 restored rules are on**, each a server setting of its own. Funus's class and weapon puzzle, the original
  fire patches and Govannon's full 300,000 health stay off.
- **Bosses pick from the whole raid.** Throws, glares, debuffs and "random target" spells used to pick real players
  only. Now they pick you, your companions and the gamebots alike. With bots around you are one target among many,
  never chosen more often than before.
- **The dragons throw again.** Golestandt, Gjalpinulva and Cuuldurach have their wing gust back and hurl two to five
  raiders high over the lair, as high as the original. Xanxicar, Kula, Hakr, Hurika, the Glacier Giant,
  Krackenschtein, Sys'sro and others throw, port and jail bots as well.
- **Thrown bots get back.** A thrown bot falls, takes the fall damage you would, and walks back to its party. When it
  can't, it recalls after 5 seconds: in a long run, 4 in 5 did.
- **No add floods.** A bot raid can outnumber a 2003 raid. Bosses that spawn adds or heal per raider count at most a
  period-sized raid, so a big bot raid faces what a full raid of players did.
- **No zerg fests.** An epic's armor now wears down under bot attackers too, at half a player's strength,
  and bots alone can never take it below 100%. Numbers help. A zerg still doesn't win.
- **Your companions stay at the fight** when a boss throws you.

### A raid plan for every big boss

You can't out-damage a puzzle. So the raids learned the fights, the way a 2003 raid leader would have called them.

Every boss script in the game was read into one catalog: 214 scripts, 238 encounters, 865 rules, each checked by hand
against its script. The plans were set against what the period wrote down: the raid guides on the VN Boards,
FreddysHouse and Allakhazam threads, the Prima guide's maps and the Herald itself, much of it recovered from the web
archive. Where the period and the script disagree, the bots play the script.

**Darkness Falls.** On the owner's PC, before any of this existed, Midgard's bots killed 41 named and then Legion, in
one attempt. Hibernia's lost to him twice. Now both realms go in with a plan.
- **High Lord Oro** brings two copies that wear his name. The raid kills the real one, and his casters and healers stay
  out of his drain.
- **High Lord Saeor** heals by every raider who dies near him, so the wounded step out of his reach and come back
  healed.
- **High Lord Baln's** wing of Dukes, Earls and Marquis is cleared first; his minions are shot down as they rise.
- **The princes eat pets.** No bot summons a pet or plants a turret near a fighting prince. The pet classes put theirs
  away and fight.
- **Beliathan, Princess Nahemah and Baelerdoth** each get their answer. Late raiders walk the raid's own trail down
  instead of getting lost on the way.

**Legion.** A main tank holds him on his lair and six healers keep that tank standing. Casters, archers and healers
form a line inside his reach and out of his soul blast. When a soul wave is too big for the casters, melee parties peel
off for it. His room is cleared before the pull, and no bot puts a bladeturn up near him. A raider he throws into
Behemoth's hall walks out the way players did, along the north wall and through the portal. Behemoth is never fought:
the guides of 2003 called him invincible.

**Albion.**
- **Apocalypse** takes to the air and rains fire; the raid moves to a ring outside the rain, then kills the Harbingers
  first. The back lines spread so one rain hits one party.
- **Caer Sidi's keylords:** Warlord Dorinakka's crypt knights cleared first, the Silencer hit by few enough raiders
  that he never turns his resist on, Sanguis's blood mages shot down, Fate's Chosen before Bane of Hope, and the
  Spectral Provisioner ambushed on his loop. The Crypt Lord's court, the real Host and Lich Lord Ilron's dark lights
  each have their own plan.
- **Krondon, all seven bosses:** Drihten's students, Deben's mages, Beran's barrels, the parts of Fuladl and
  Fulafeallan, Lurfos's splash, Ulor before Jehrog, and Scurceol Hyrde's orbs taken one at a time by the whole raid.
- **Summoner's Hall:** the four summoners fought inside their leashes, Lossren's ghoul shot by every caster, and
  Govannon's demon fought from clear of his Shroud.
- **Lord Gildas:** when he stuns his tank, the next tank taunts him off it.

**Midgard and Hibernia.**
- **Tuscaran Glacier:** Agmundr's hrimthursa first and everyone out of his cold blast, Steinvor's pillar of frost
  dodged, Guthlac's ice spike shot down before it erupts, the Council in their period order, and Kula before the King
  while one tank holds him.
- **Galladoria:** the Water primal left alone in its pool, the Air primal shot from the room's middle, the
  Organic-Energy Mechanism fought from range, the Sporite Cluster fought in one tight crowd, each Earthmender taken by
  the classes that can hurt it, and an /assist train on every one of Spindler Broodmother's newborns.
- **Balor, Iarnvidiur and Nosdoden** each get a plan of their own.

**The open-world giants.** Before 1.2, no bot ever fought an open-world big boss. Now every one has a plan, and 16 draw
a realm raid sized from the period:
- **Albion:** the Green Knight, Coliath, the Spirit of Lord Emthoro.
- **Midgard:** the Glacier Giant, Mortufoghus, the Raumarik Revenant, the five Wretches of Winter (pulsing bladeturn
  and all).
- **Hibernia:** Evern, the Juggernaut, Curengkur, Marfoirwort, Mollunero.
- **Myrddraxis's heads** die in the order Thredh's hydra guide gave in 2003. Morgana gets no raid: she never was a raid
  boss.

**Your party calls it.** Each mechanic is called in /group as it happens: "all on kula", "out of the fire", "no cold on
kvasir, it heals him". For Xanxicar's stomp the raid leader calls the run, and each bot reacts in its own time, some
late. A realm new to him loses more raiders; over its attempts it learns him.

Every plan here is new or reworked in 1.2 and passes its automated tests. Your raids are among their first real nights.
What has been seen on a real install is under "How it was tested", and the fights bot raids still lose are named under
"Known issues". Too hard on your world? Each restored rule switches off on its own.

### A raid is a realm event

- **The tale, then the horn.** When a realm's raid comes due, its storyteller tells the boss's tale in /faction, to the
  end. Then the call goes out across the screen with a horn, and only then do the bots gather. No raid is ever lost to
  its tale.
- **Every four hours** per realm, instead of every two and a half. An interval you set yourself is kept.
- **No clock in the dungeon.** Caer Sidi used to be cut off at four hours with 12 named down. Now a raid ends when it
  wins, runs out of attempts, or goes an hour without progress.
- **Wipe, regroup, go again.** Raiders release at their party's post outside the dungeon, not in town, and go back in
  waves. The next pull waits for 70% of the living raid, counting the ones still walking back. A boss that resets while
  the raid stands costs no attempt.
- **Cleared stays cleared.** A dungeon's epics wait to respawn while its raid is inside. A boss standing on the way in
  is fought as an objective of its own, not walked into, and no muster lands inside another boss's reach.
- **Raids keep moving.** The approach dungeon's monsters are cleared on the walk, a raider stuck in a pocket rejoins
  the raid's front instead of going back to the capital, and no party is posted where it can't walk to its hub.
- **Your seat, clearly.** An invite to a raid that already left says so, and your group window clears when a raid
  ends.

### A world with a past

Every dungeon, dragon and boss has a story, and you meet it everywhere.

- **107 places, 578 bosses and 4 realm histories**, from the dragons down to the named of every zone: about 600,000
  words, and more than 5,000 lines for the bosses to speak.
- **Mythic's word comes first.** 832 Camelot Herald articles, November 2001 to February 2005, were read from the web
  archive and checked against the lore. Where ours disagreed, Mythic's won: Dartmoor's history, Gjalpinulva's mother,
  the Legendary Afanc's return, Krondon's ogres. Where Mythic told no story, Living Server wrote one. Every compass
  direction was checked against the world.
- **Loremasters** in each capital: Aldwin in Camelot's library, Sigrun at Jordheim's temple, Eithne by the bards of
  Tir na Nog. Ask them about the [places] of your realm.
- **Lore books**, one per place, read a page at a time. The loremasters copy them for a silver, and now and then one
  drops near the place it tells of, never crowding out real loot.
- **The Bestiary**, a new Realm Records page: every boss of your realm, with what raid leaders say about it. Its full
  story opens once your realm brings it down. Raids won before 1.2 count.
- **The Herald** closes each raid's record with a sentence from its boss's story.
- **Bosses speak** as the fight starts, at their phases, at their big moments and as they die.
- **Atmosphere lines** cross your screen the first time each day you enter a dungeon, as you step into a lair, when a
  dragon takes to the sky, and at a realm's first kill of a boss. One every 30 seconds at most; realm news always comes
  first. A switch in the Living Server tab turns them off.
- **Bots know the stories.** The raid leader briefs the realm as the raid sets out. Ask a bot "whats in krondon?" and
  it answers from the lore.
- **The storyteller** tells 15 tales across the realms. Ask in plain words ("tell the one about golestandt"), or rest
  by the fire with a bot in your group and hear the short version. `/lore` works in either client.

### Bots that play with you

- **They invite you.** The leader of a gamebot party near your level may send a tell and an invite. Say yes and the bot
  runs the group: the camp, the pulls, the breaks. You travel yourself; it waits. `/lfg`, or a tell like "room for a
  cleric?", brings an invite sooner. `/invites off` stops them.
- **Lead or follow.** `/lead` takes the group. `/lead bot` hands it back.
- **Parties carry on.** Lose a member and the leader asks /lfg for a replacement, calls its own pulls, and ends at a
  natural break with "last pull then town".
- **Raid leaders save you a seat.** When a dragon or epic dungeon raid forms and you are 50, its leader may offer you a
  spot, once per raid.
- **Fair loot.** Your upgrades go to you, a bot's upgrades to the bot that needs them most, and the rest goes round with
  two turns for you. Coin counts you twice. **Your loot share** is a launcher setting.
- **RvR bots stop acting like radar.** They used to see you from 5,000 units, all at once. Now they notice you by chance:
  always within 1,000, never past 3,500 on their own. In seeded simulations the median is about 1.5 seconds at 1,500
  units and 20 seconds at 3,400. At most three bots a realm look each second; the rest hear it when one calls "inc".
  Stealth works as before.
- **They answer you first.** A tell, or a line that names a bot, always gets an answer, however busy the channel. A bot
  never claims another class, role or level, and `/lswhy me` shows why bots answered you, or didn't.
- **Small servers stay calm.** With 10 gamebots a realm, /faction ran at four times its budget. In the automated
  playtester's runs it fell from 1.20 and 1.33 lines a minute to 0.87 and 0.97. A full server isn't quieter: its
  channels were rebalanced, not cut.

### Bring your friends (experimental)

Your bot world has always been yours alone. Now a few friends can join it, from their own PCs, with the original client.

- **HOST FOR FRIENDS:** set a password, press START HOSTING, send the invite code. Windows asks once to let them
  through its firewall. Home network and Tailscale are on by default; the internet is a box you tick. If your server is
  running, the launcher offers to restart it.
- **JOIN FRIEND:** paste the code, pick an account name and your own password (6 to 13 characters), and type the host's
  password the first time. Your character lives in the host's world.
- **Passwords stay safe.** The host's password is needed only to make an account and never travels: Join Friend sends
  a short code made from it. Five wrong passwords from one PC lock it out for ten minutes. Your own PC is never asked
  and never locked out.
- **Friends play as players.** Make Me GM changes only your own account; `/travel` and `/bot pilot` work only on the
  host's PC.
- **Bots share nicely.** Your companions follow you, always. Invited gamebots follow the group's leader. Each of you
  gets quest credit for a gamebot's kill, and bots answer each of you in turn.
- **Character passport.** CHARACTER PASSPORT… takes a character to a friend's world with its gear, quests and the coin
  you choose; COME HOME brings it back with what it earned. While it's away it's locked at home, and every step backs
  your world up first.
- Not yet: the new client (0.37c) can't use Join Friend.

### Bots that look and fight like players

- **They dress up, at the game's own prices.** Gamebots spend spare coin on armor dyes and, the vain ones, weapon
  lusters, at the same merchants you use. Some never bother, some wear one tidy colour, a few are peacocks in the
  dearest shades, many wear their realm's colours, and a guild often matches. Glows still come only from epic and
  dragon drops and the Darkness Falls merchants. `bot_cosmetics` turns it off.
- **They flank.** A melee bot that knows a back or side style steps around a monster fighting someone else and swings
  it, walking only where the navmesh allows. Never for tanks, companions, your group, raids or epics.
- **Real style play.** Side, back and front styles where the bot really stands, chains followed through, reactive
  styles after an evade, block or parry, and no taunt spam unless it tanks.
- **They move like people.** Melee circle, casters step between casts, turns and stops stop snapping, a moving party no
  longer jerks once a second, and town idlers drift to the bank and the usual crowd spots.
- **They know when to run.** A gamebot party calls RUN while everyone still stands and heads for a bindstone or the
  camp's safe spot. A solo bot bails out of a losing fight. A healer in your group whose player died runs and comes back
  to rez. A party with you in it never calls RUN.
- **They pick better camps.** Parties stand by role, read what a camp holds before they choose it, pull the monster
  standing apart first, and move on when a camp stops paying.

### Quests, battlegrounds and the rest

- **The epic chains are linked on every install**, by the server as it reads the quests. Nothing is written to your
  world, and no Python is needed.
- **Repeatable quests are offered again**, deliveries in quests without a collect column can be handed in, and
  collection quests take their item whatever the case of its name.
- **Battleground rallies:** once the gates fall, each realm rallies in the breach and goes up to the keep lord
  together, with you counting double. It doesn't win yet: see Known issues.
- **Two Darkness Falls exits work:** the one by Legion's hall and the one near the Midgard boss take you home. Nothing
  is written to your world.
- **New client (0.37c):** skills on the quickbar, Stealth in the Abilities list, and a SELL button at merchants,
  installed only over the released scripts; ROLLBACK.cmd puts them back.
- **Boss scripts, hardened:** each boss's death runs once, patrols follow the navmesh, the dragons face
  their lairs, and bosses keep their own state. No boss was weakened.
- **Setup retries a busy download server** (429, 500, 502, 503 or 504) after 30, 60, 120 and 240 seconds, keeping what
  it already has.
- **The loading screen says LIVING SERVER**, on upgrades from 1.1, 1.1.1 and 1.1.2 too.
- **Level-50 bots from the launcher arrive trained** in their builds, instead of fighting their first raid with level-1
  skills.
- **Logs take far less room.** A full server wrote about 300 MB a day and kept a week. Now the server log keeps three
  days, routine bot travel has its own two-day file, and every log has a limit.
- **Commands typed in the server window work again**, the patcher refuses an update that names a file outside the game
  folder, and **Lean server memory** is a new test option in the launcher (off by default).
- **For testers:** `/epictest sweep` (GM only) runs every raid once, back to back, with the GM seated as a guest.

### Everything Living Server adds to Offline DAoC

Living Server is an add-on for Offline DAoC 0.37, the single-player Classic + Shrouded Isles server. It began as the
Social Expansion. Seven releases later, this is the whole of it: free, and each part of bot life on its own switch.

**A server that talks like 2003.**
- **About 34,000 lines and 393 /faction scenes**, written for the game from launch (9 October 2001) to patch 1.65
  (8 October 2003). A validator turns away anything later: no Atlantis, no master levels, no "toon". Five writing
  styles, typo-prone "*fixes" and all.
- **The arguments never settle:** smite against rejuv, buffbots, zergs, relic panic, dragon raids, each realm in its own
  voice.
- **Nothing made up.** "where are the mids?" names the zone they're really in. Keep, relic and raid news is real, and so
  are the rumours. Ask about a trainer, a camp or the road to Cornwall and the answer comes from the live world.
- **They read what you type.** "ty", "inc", "rez pls", "lfg 24 cleric". Ask "why?" after a bot's line and that bot
  answers; tells carry on as conversations.
- **Regulars.** A handful of talkative bots carry each evening, each with its own manner and running jokes. Some have
  history with each other that plays out over days, and you can get involved. Not every raid goes to plan.
- **Names like a 2003 server**, from a study of about 700 real character names of 2001-2004.

**Bots with lives.**
- **Personalities:** Leveler, Socialite, Daredevil, Explorer, Crafter or No-lifer, with five traits that decide who
  grinds, who parks in town and who lives in the frontier. `/personality` on any bot.
- **Guilds:** leveling, social, RvR, raiding and crafting guilds that recruit you, talk in /gu, claim and defend keeps,
  pay dues, earn merit, buy guild buffs and form alliances. Gamebots in your own guild pay their dues too.
- **Memory:** group with a bot and it remembers you, greets you hours later, asks to group again. Bots remember your
  rezzes, your insults and each other.
- **Server life**, 17 parts: grats, server firsts, rez requests, frontier alarms, grudges against their killers, "brb"
  breaks and linkdeads, a wave when you walk up, and the **Camelot Herald** printing your world's news.
- **A real clock:** the evening crowd from 7 to 11 pm, a few hundred before dawn, busier weekends.

**An endgame that runs itself.**
- **37 raids on the schedule:** each realm's dragon, its epic dungeon, Summoner's Hall, Darkness Falls, the raid
  dungeons from Krondon to Marfach Caverns, and 16 open-world giants. Each musters, travels, clears its way and goes for
  the final boss, with you or without you.
- **Period-sized raids:** from 50 bots for Dodens Gruva and Tur Suil to 150 for Crystal Cave, each read from the forum
  threads of 2002-2004. A win trims the next raid, a defeat adds more back, so each settles where it is won about two
  times in three. The bosses are never weakened.
- **The Epic Special Forces:** up to 24 geared level 50s who answer a wiped raid, in a dungeon or at a dragon.
- **Contested raids:** two realms race for one boss in Summoner's Hall or Darkness Falls, and only one gets the kill.
- **Keep sieges with two sides:** warbands march, scouts report the army, defenders man the walls, and attackers break
  off to save their own keep.

**Your game, your way.**
- **The Living Server tab:** voices, personalities, guilds, server life, Atmosphere lines, How much they talk, the Bot
  schedule, bot and raid invites, the join mode, your loot share, Bots in your guild, Power regeneration and XP by level,
  each on its own switch, plus forced raids from the dashboard.
- **XP by level range** for you and the bots, so 1 to 34 stays worth playing and 35 to 50 moves. Quest XP follows it.
- **Quests that work:** markers that mean what they say, epic chains in order, and kills by your bot group that count.
- **One-click Setup** from nothing, or over the game you have, and **Check for updates** in the launcher, verified
  against GitHub's published SHA-256.
- **Safe by default:** a crash-safe world with a verified daily backup (the last 7 kept), a backup before every
  install, a server that listens on this PC only unless you host, and `ROLLBACK.cmd`.
- **Smarter, safer bots** that route around monsters far above them, remember where bots keep dying, walk around epics
  they didn't come for, and stop standing under flying monsters.

### Install

- **Have 1.1.2, 1.1.3 or 1.1.4:** the launcher's **Check for updates** finds 1.2. START SERVER reads **GET UPDATE**:
  Install, Later or Skip this version.
- **New to Offline DAoC, or adding Living Server:** run `LivingServer-Setup-1.2-beta.exe`. A new install is Offline DAoC
  0.37. Windows SmartScreen may say "Windows protected your PC" because the exe isn't code-signed: click More info, then
  Run anyway.
- **Have 1.1 to 1.1.4:** run the Setup, or unzip `OfflineDAoC-LivingServer-1.2-beta.zip` into your game folder and run
  `INSTALL.cmd` with the game and server closed. Your characters, bots, guilds and settings stay.
- **To undo:** `ROLLBACK.cmd`. It puts every replaced file back; it never puts your database back, because that would
  undo your play since.

Real loot, the Realm Exchange, stablemaster routes, selling at natural breaks, bots that earn their armor and Darkness
Falls open to solo bots are untouched.

### How it was tested

A feature counts as working here only once it has been seen working on a real install. Everything else is "tests
only".

- **Automated (tests only):** [filled in from this release's CI run: the server suite's count, and the Windows launcher
  and installer tests]
- **Bot raids in a test world (tests only, bots only):** Easmarach killed (build 8ab2279e), Soul Reckoner killed
  (4cc22ab4) and Torst killed (8ab2279e). Govannon was not beaten in about 12 runs, and Spindler Broodmother not in
  about 9. Funus was beaten once, only with fixes that are not in this build.
- **On the owner's PC, test builds of 1.2 (10 to 14 November):**
  - Bot-led groups: a gamebot leader's invite, following it, `/lead` and `/lead bot`, leaving and the replacement call,
    and the party letting you go after half an hour when you never come (builds 3 to 5).
  - A raid leader's invite to Golestandt, accepted, and a seat in the raid (build 5).
  - The boss's tale, then the call with its horn, before a raid of the game's own (builds 3 and 4).
  - A tale asked for in plain words, with the storyteller silent about anything else until it ended (build 4).
  - After a wipe, the raid regrouping and going back in waves; the next attempt waiting for 70% of the raid (build 3).
  - Raids won: Golestandt (build 5, second attempt), Cuuldurach (build 5, a raid the game opened itself), Marfach
    Caverns (builds 2, 4 and 5) and Dodens Gruva (build 5). Bots beat Spindler Broodmother for the first time (build 5),
    and Midgard's raid won Summoner's Hall, Grand Summoner Govannon included (build 3e64140, 11 November).
  - Raids lost: Galladoria at the Organic-Energy Mechanism, Trollheim at Nosdoden, and Caer Sidi cut off by the old
    four-hour limit with 12 named down (build 5).
  - Bots buying dyes and lusters at the game's prices (build 6).
  - Play with friends: a second game client joined over the home network with the host password and made its account
    (build 7). A session played together has not been checked yet.
  - The sweep and the raids of 14 November (build e3b8238, bots only, before the Darkness Falls raid AI, Legion's plan
    and the sweep's raid fixes): Midgard's Darkness Falls raid, which the game opened itself, killed 41 named and Legion
    in one attempt, with no wipe. Hibernia's lost to Legion twice (lowest 92% and 60%). Caer Sidi, Tuscaran Glacier and
    Galladoria were lost, and Albion's Summoner's Hall was never reached.
- **On a real install** (an overnight run of development build 76600af7, bots only, 7.8 hours): Fornfrusenen, the
  Council Vagn and Nokkvi, Torst and Hurika were killed; Tuscaran Glacier was lost at Icelord Agmundr. Bot raids skipped
  Scurceol Hyrde: they could not shoot his orbs. In the battlegrounds, 168 of 169 storms on a keep lord failed and no
  keep changed hands. The server stalled for 4 to 6 seconds three times.
- **Played:** [owner: one line on the played check, filled in before publishing]
- Please report anything odd on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
  `FORK_BUILD` line from `runtime\server\logs\server.log`.

### Known issues (it's a beta)

- **Fights the bot raids still lose.** [owner: the final list from the new sweep, filled in before publishing. On the
  last sweep (build e3b8238, 2026-11-14) bot raids lost Caer Sidi, Tuscaran Glacier, Galladoria and Hibernia's
  Darkness Falls (at Legion), and never reached Albion's Summoner's Hall. Known causes still open then: Krondon's Lyft
  Miht room (a tests-only fix since), Golestandt's second attempts, Summoner Roesia.] If a fight is too hard on your world, each restored rule
  is a server setting of its own and can be switched off alone.
- **The new raid plans are tests only.** The Darkness Falls raid AI, Legion's plan, the plans for every realm's big
  bosses and the open-world bosses, the 16 open-world raids, the route fixes and the raid fixes after the sweep of
  14 November pass their automated tests but have not been seen on a real install. The open-world raids' hubs and
  routes have not been walked on a real install either. The same goes for the epic armor rule and the
  boss script fixes. Behemoth is never fought, Morgana has no raid (she was never a raid boss), and Fomor City has no
  raid of its own.
- **Parts of 1.2 have only been tested automatically.** The bot-led groups, the tales and several raids have been seen
  on a real install (see above). Not yet seen there: most of the new boss tactics in a raid with a player in it, RvR
  perception, the loremasters and books, a session played together with a friend, and a character passport trip
  between two PCs. Real play may still find cases the tests miss.
- **Dragon fights can fall apart partway.** In the play tests a raid on Cuuldurach collapsed at 69% and one on
  Golestandt at 46%, with most of the raid dying within a few minutes. Both dragons were beaten later.
- **Play with friends is experimental.** It needs the original client, and it has been tried on a home network only.
- **Some fights are harder than before**, and a raid of bots may lose them for a while (see above).
- **/faction can get too busy.** On an evening with about 600 bots a realm, it ran well above its ceiling, up to 20
  lines a minute, with bursts of 10 to 16 reactions. Lower **How much they talk** in the Living Server tab if it is too
  much.
- **A very busy server can stall for a few seconds.** In an overnight run with about 600 bots a realm, it stopped for
  up to 6.2 seconds.
- **Some gamebots wait a long time at frontier teleporters.** In one night, 368 times a gamebot stood 15 minutes waiting
  for one before the server moved it on.
- **Battlegrounds are an endless fight.** Bots storm the keep lord again and again and almost always fail, so the keeps
  don't change hands and the same bots die over and over.
- **Bot-led groups:** gamebots don't share your horse or boat; the leader takes its own and waits at the other end. The
  break at the end of a session ends the party: it doesn't walk to town and back with you.
- **New client:** APPRAISE stays hidden, and you don't see yourself as see-through in Stealth yet. Both need a newer
  network client than this update installs.
- **More:** [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md).
  Every change, with its numbers: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

### Coming next

- **Linux:** the 0.37 game with Living Server, the original client under Wine. [owner: the version number]
- **2.0, the complete realm:** live events, the guild package, bots with alts, friends and grudges, crafting and a
  server with a past.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.2-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.2-beta.zip` | (owner: the zip's SHA-256, filled in by hand at publish) |

### Credits

Living Server is an add-on for Offline DAoC 0.37, built on shadowofze's Offline DAoC and the OpenDAoC project. Thanks
to the community whose forks, work and ideas helped along the way: shadowofze, rambodolphin13, stefanrows, lometur,
Nikse77, Cancain and ryan-grafana. Boss
lore checked against Mythic's Camelot Herald, and raid plans
against the period's guides and forums. Reports from the community on GitHub and r/daoc. Directed and play-tested by
[chris4aker](https://github.com/chris4aker), coded with Claude (Anthropic). GPL-3.0; licences and attributions are in
[THIRD_PARTY.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/THIRD_PARTY.md) and the changelog.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
