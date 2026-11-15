# Living Server Beta 1.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for `v1.2-beta`, "Where Legends Wake". It is a draft for
the owner's approval: the "How it was tested" section is finished after the owner's played check. In the file table the
Setup row must keep exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the
exe's SHA-256. The zip's SHA-256 is filled in by hand when publishing. Publish it as Latest, not as a pre-release: the
launcher's update check reads GitHub's latest release, which leaves pre-releases out. Lines marked "[if it lands: ...]"
describe work still on its own branch: keep them only if it is merged before the tag, and drop the marker. Lines marked
"[owner: ...]" are for the owner to fill in before publishing. Nothing here names the Easter eggs or the project's
internal vocabulary. Rewritten on 2026-11-14 in the release's own voice, covering everything merged into 1.2 that day
(the raid flow and boss tactics from the sweep, the Darkness Falls raid AI, Legion's plan, the Midgard and Hibernia big
bosses, the period research); the Albion big bosses (#220) and the open-world bosses (#222) are marked "[if it lands]".
"A raid plan for every big boss" holds only once #220 and #222 are in; without them, say "for the big bosses of
Darkness Falls, Midgard and Hibernia". Owner's decision (2026-11-14): credit lightly, by project and username in the Credits
section only, never per feature or per piece of code; the licence notices stay in THIRD_PARTY.md and the changelog.
The repository description, the tagline and the Reddit post are in [RELEASE-1.2-COPY.md](RELEASE-1.2-COPY.md).

---

### Where Legends Wake

A bot in /faction asks the realm's storyteller for the tale of Gjalpinulva. Two others tell the channel to hush. The
storyteller tells it line by line, a listener asks a question, the story answers it, and the channel goes quiet. Then a
line crosses your screen, a horn sounds, and Midgard's raid begins to gather. A tell comes in from the raid leader:
"forming for Gjalpinulva at Svasud Faste. want a spot?"

That is Living Server 1.2. **The epic encounters are back at full strength**: the dragons hurl raiders over their lairs,
Kula and Tuscar heal each other, Kvasir drinks your cold spells, and the bosses pick their victims from the whole raid,
bots included. **Every big boss now has a raid plan**, Darkness Falls and Legion among them, built from the boss's own
script and from the guides raid leaders wrote in 2002 and 2003. **Every dungeon, dragon and boss has a story** you can
find in the world. And **the bots play with you**: they invite you, lead groups you can simply follow, and a few
friends can join your world from their own PCs.

It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client, like 1.1 to 1.1.4.

### What's new in 1.2

- **The epic encounters at full strength.** 15 of the bosses' original rules are back on, and throws, jails and glares
  now land on bots as well as on you.
- **A raid plan for every big boss.** Darkness Falls' high lords and princes, Legion, the Glacier's icelords,
  Galladoria's primals, Balor, Nosdoden and more: 77 encounters with a raid plan, 74 of them new or reworked in 1.2
  [owner: recount from the "Encounters with raid tactics" table of docs/encounters/EPIC-ENCOUNTERS.md once #220 and
  #222 are in]. [if it lands: Caer Sidi's keylords, Krondon, Summoner's Hall and Lord Gildas get theirs too, and 16
  open-world big bosses, from the Green Knight to Evern, get a realm raid of their own.]
- **A raid is a realm event.** The boss's tale, then the horn. One raid every four hours a realm, no time limit in the
  dungeon, and a raid that comes back together after a wipe.
- **A story for every place.** 107 places, 578 bosses and 4 realm histories, about 600,000 words, checked against 832
  Camelot Herald articles. Loremasters, books, a Bestiary, and bosses that speak.
- **Bots that play with you.** Gamebot leaders invite you and run the group, raid leaders offer you a spot, and RvR bots
  notice you the way a player would, not like radar.
- **Play with friends (experimental).** HOST FOR FRIENDS, JOIN FRIEND, an invite code, and a character passport to take
  your hero to a friend's world and back.
- **Bots that look like players.** They dye their armor at the game's own prices, flank, circle, chain their styles
  and know when to run.

### The epic encounters, at full strength

For a long time the hardest bosses were softened so that bot raids could win. In 1.2 the gloves come off.

- **The bosses' own rules are back.** The horsemen's tether heal, Morbus hiding behind his swarm, the Skeletal
  Sacristan's endless patrol, Soul Reckoner's souls and small room, Aroon's Scaiths in order, the Earthmenders' class
  puzzle, Beatha healing Xaga, Kvasir's cold heal, King Tuscar and Queen Kula healing each other, the dragons'
  messengers and more. Of the 18 restored rules that are now server settings, 15 are on. Funus's class and weapon
  puzzle, the original fire patches and Govannon's full 300,000 health stay off.
- **They hit bots too.** Bosses used to choose their throws, glares, debuffs and "random target" spells from real players
  only. Now they choose from the whole raid: you, your companions and the gamebots. With bots around you are one target
  among many, never chosen more often than before.
- **The dragons throw again.** Golestandt, Gjalpinulva and Cuuldurach have their wing gust back and hurl two to five
  raiders high over the lair, as high as they did in the original. Xanxicar, Kula, Hakr, Hurika, the Glacier Giant,
  Krackenschtein, Sys'sro and others throw, port and jail bots as well. A thrown bot falls, takes the same fall damage
  you would, and walks back to its party when it can; otherwise it recalls to its raid after 5 seconds. Most thrown
  bots are recalled: in a long run, 4 in 5 were.
- **No floods of adds.** A bot raid can be bigger than a 2003 raid. A few bosses that make adds or heals per raider now
  count at most a period-sized raid, so a big bot raid faces what a full raid of players did.
- **Your companions stay at the fight** when a boss throws you, instead of being dragged along.
- **Bosses wake when they should.** The Green Knight wakes when a bot raid has gathered by him, Qan'ris Duros and Durval
  bring their guards, and Easmarach retreats to his waterfall and comes back down to be finished.

### A raid plan for every big boss

A boss with a puzzle can't be beaten by hitting harder. So the raids learned the fights, one boss at a time, the way a
2003 raid leader would have called them. Every boss script in the game was read into the encounter catalog: 214 scripts,
238 encounters and 865 rules, each one checked by hand against its script. The plans were then set against what the
period wrote down: the raid guides on the VN Boards, FreddysHouse and Allakhazam threads, the Prima guide's maps and
the Camelot Herald, many of them recovered from the web archive.

**Darkness Falls, at last.** The raid takes on the whole of the old dungeon.
- **High Lord Oro** comes with two copies that share his name; the raid fights the real one, and his casters and
  healers keep out of his drain.
- **High Lord Saeor** heals by every raider who dies near him, so the wounded step out of his reach and come back
  healed.
- **High Lord Baln's** wing of Dukes, Earls and Marquis is cleared first, and his minions are shot down as they come.
- **The princes eat pets.** No bot summons a pet or plants a turret near a fighting prince; the pet classes put theirs
  away and fight.
- **Beliathan, Princess Nahemah and Baelerdoth** each have their answer, and late raiders follow the raid's own walked
  trail down to it instead of getting lost on the way.

**Legion.** A main tank holds him on his lair, six healers keep that tank alive, the casters, archers and healers form a
line inside his reach and out of his soul blast, and melee parties join in when a soul wave is too big for the casters.
His room is cleared before the pull. A raider he throws into Behemoth's hall now walks out of it the way players did,
along the north wall and through the portal. Behemoth himself is never fought: the guides of 2003 called him invincible.

**Midgard and Hibernia's big bosses.**
- **Tuscaran Glacier:** Icelord Agmundr's hrimthursa first and everyone out of his cold blast, Steinvor's pillar of frost
  dodged, Guthlac's ice spike shot down before it erupts, Hakr's iceweavers, Otrygg's pets, the Council in their
  period order, and Kula before the King while one tank holds him.
- **Galladoria:** the Water primal left alone while it heals in its pool, the Air primal shot from the room's middle,
  the Organic-Energy Mechanism fought from range, the Giant Sporite Cluster fought in one tight crowd, the Earthmenders
  each taken by the classes that can hurt them, and Spindler Broodmother's newborns killed as they come while the
  parties stand apart against her mesmerize.
- **Tur Suil, Iarnvidiur's Lair and Trollheim:** Balor, Iarnvidiur and Nosdoden each have a plan of their own.

[if it lands: **Albion's big bosses.** Apocalypse's rain of fire dodged on a ring while he flies and his Harbingers
killed first; Warlord Dorinakka's crypt knights, the Silencer, Lord and Lich Lord Sanguis, Bane of Hope and the
Spectral Provisioner in Caer Sidi; all seven of Krondon's bosses, with Scurceol Hyrde's orbs taken one at a time; the
four summoners fought inside their tethers; and Lord Gildas taunted off a stunned tank.]

[if it lands: **The open-world giants.** Before 1.2 no bot ever fought an open-world big boss. Now 16 of them get a raid
of their realm's own, sized from the period: the Green Knight, Coliath and the Spirit of Lord Emthoro in Albion; the
Glacier Giant, Mortufoghus, the Raumarik Revenant and the five Wretches of Winter in Midgard; Evern, the Juggernaut,
Curengkur, Marfoirwort and Mollunero in Hibernia. Myrddraxis's heads are killed in the order Thredh's hydra guide gave
in 2003.]

**And from the plans already in the game:** the Crypt Lord's court cleared while his patrol is far, an ambush for the
real Host, Lich Lord Ilron's dark lights first, Govannon's demon fought from clear of his Shroud, Funus fought only by
the classes that can hurt him, the Sacristan ambushed at stations on his loop, Xanxicar's stomp outrun, Scurceol's orbs
shot from his platform, Orshom's Fire put out with cold, and more. Your party calls each mechanic in /group as it
happens ("all on kula", "out of the fire", "no cold on kvasir, it heals him").

**Raid leaders call it, and a realm learns a boss.** For Xanxicar's stomp the raid leader calls when to get out, and
each bot reacts in its own time, some late. A realm that is new to him loses more raiders; over its attempts it learns
him and loses fewer.

All of these plans are new or reworked in 1.2 and pass their automated tests. Your raids are among their first real
nights: what has been seen on a real install is listed under "How it was tested". **Some secrets take a realm time to
learn**, and a raid of bots still loses some of these fights: they are named under Known issues. If a fight is too hard
on your world, each restored rule is a server setting of its own and can be switched off alone.

### A raid is a realm event

- **The boss's tale, then the horn.** When a realm's raid comes due, or you force one from the launcher, the realm's
  storyteller first tells the tale of that raid's boss in /faction, to its end. Then the realm is called: a large line
  across the screen and a horn. Only then do the bots gather. A raid is never lost to its tale: if the tale can't start
  or runs long, the call goes out anyway, and with no player of that realm online the raid forms at once.
- **One raid every four hours** per realm, instead of every two and a half. A world still on the old default moves to
  four hours once; an interval you chose yourself is kept.
- **No time limit in the dungeon.** A raid used to end four hours after it went in, even one still winning: Caer Sidi
  was cut off with 12 named down. Now a raid ends when it wins, when it runs out of attempts, or when it stops making
  progress: an hour with no named or boss down, no wipe and its target never lower than before. A raid you force from
  the launcher waits three hours.
- **After a wipe the raid comes back together.** Raiders release at their party's post outside the dungeon, not in
  town, gather at the way in and go back in waves, and the next attempt waits for 70% of the living raid, counting the
  ones still walking back. A boss that resets while the raid still stands costs no attempt.
- **A cleared room stays cleared.** A dungeon's epics wait to respawn while its raid is inside, a boss standing on the
  raid's way in is fought as an objective of its own instead of walked into, and no muster is placed inside another
  boss's reach.
- **Your raid spot, clearly.** An invite to a raid that has already left now says so instead of "no spot left", and
  when a raid ends your group window clears.

### A world whose stories come alive

- **Every place has its story.** 107 places, 578 bosses and 4 realm histories (Albion, Midgard, Hibernia and the places
  no realm holds), from the dragons and epic dungeons down to the named monsters of every zone: about 600,000 words, and
  more than 5,000 lines for the bosses to speak.
- **Checked against Mythic's own words.** Where Mythic told a story, it is kept, in our own words, with its source. 832
  Camelot Herald articles from November 2001 to February 2005, read from the web archive, were checked against the
  lore, and where ours disagreed with Mythic's, Mythic's version won: Dartmoor's history, Gjalpinulva's mother, the
  Legendary Afanc's return and Krondon's ogres among them. Where Mythic told no story, the Living Server wrote one. Every
  compass direction in the lore was checked against the world.
- **Where you meet it:**
  - **Loremasters** in each capital: Aldwin in Camelot's library, Sigrun at Jordheim's temple, Eithne by the bards of
    Tir na Nog. Ask them about the [places] of your realm.
  - **Lore books**, one per place, read a page at a time. The loremasters copy them for a silver, and now and then one
    turns up on a monster near the place it tells of. They never crowd out real loot.
  - **The Herald:** each raid in Realm Records ends with a sentence from its boss's story.
  - **The Bestiary**, a new Realm Records page: every boss of your realm's places, with what raid leaders say about it.
    Its full story opens once your realm has brought it down. Raids won before 1.2 count.
  - **`/lore`** in either client. In the new client the journal's LORE button opens it in its own window.
  - **Bosses speak** their own lines as a fight starts, at their phases, at their big moments and when they die.
  - **Short lines on screen** the first time each day you enter a dungeon, when you step into a boss's lair, when a
    dragon takes to the sky, and at a realm's first kill of a boss. At most one every 30 seconds, and realm news always
    comes first. **Atmosphere lines** in the launcher's Living Server tab turns them off.
  - **Bots know the stories.** A raid leader tells the realm what the boss does as the raid sets out. Ask a bot "whats in
    krondon?" and it answers from the lore.
  - **The realm's storyteller** tells a boss's tale in /faction, line by line, and keeps to it until the end. Ask for one
    in plain words ("tell the one about golestandt"). A group resting by the fire sometimes tells one too.

### Bots that play with you

- **Bot leaders invite you.** The leader of a gamebot party near your level may send you a tell and an invite. Yes puts
  you in its group, and the bot leads: the camp, the pulls, the breaks. You travel yourself; it waits for you. Unasked
  invites come at most once every 45 minutes. `/lfg`, or a tell like "room for a cleric?", gets you one sooner.
  `/invites off` stops them.
- **Take the lead, or hand it back.** `/lead` makes you the leader. `/lead bot` hands it to a gamebot.
- **Bot-led parties carry on.** When someone leaves, the leader looks for a replacement in /lfg. It calls its own pulls,
  and it ends at a natural break with "last pull then town".
- **Raid leaders invite you.** When a dragon or epic dungeon raid forms in your realm and you are level 50, its leader may
  offer you a spot, once per raid.
- **Loot in a group with gamebots, whoever leads:** an upgrade for you goes to you, an upgrade for a bot goes to the bot
  that needs it most, and the rest is shared with two turns for you. Coin counts you twice. **Your loot share** is a
  launcher setting.
- **RvR perception.** Gamebots in the frontier, battlegrounds and Darkness Falls used to see you from 5,000 units, all at
  once. Now they notice you by chance: always within 1,000 units, never beyond 3,500 on their own. In the seeded
  simulations, the median time for a bot to notice you is about 1.5 seconds at 1,500 units and 20 seconds at 3,400.
  At most 3 gamebots of a realm look for you each second; the rest hear it when one calls it. Stealth works as before.
- **Replies that don't give bots away.** Bots answer you first: a tell, or a line that names a bot, always gets an answer,
  however busy the channel. Answers have their own budget, so on a busy channel bots stop answering each other before they
  stop answering you. A bot no longer claims to be another class, role or level. `/lswhy me` shows why bots answered your
  lines, or didn't.
- **Quieter /faction on a small server.** With 10 gamebots a realm, /faction ran about four times faster than its own
  budget. In the automated playtester's runs (10 gamebots a realm, two half-hour listens each, on a development machine)
  it went from 1.20 and 1.33 lines a minute to 0.87 and 0.97. A full server is not quieter: its channels were
  rebalanced, not cut.

### Play with friends (experimental)

Your bot world has always been yours alone. Now a few friends can join it from their own PCs, with the original game
client.

- **HOST FOR FRIENDS** in the launcher: set a password for friends, press START HOSTING, and send them the invite code.
  Windows asks once for permission to let them through its firewall. Your home network and Tailscale are on by default;
  joining over the internet is a box you tick. If your server is running, the launcher offers to restart it so hosting
  takes effect.
- **JOIN FRIEND** on a friend's PC: paste the code, choose an account name and your own password (6 to 13 characters),
  and type the host's password the first time. Your character is saved in the host's world.
- **Passwords:** the host's password is needed only to make an account, and never travels: Join Friend sends a short code
  made from it. Your own password is yours alone. Five wrong passwords from one PC lock it out for ten minutes. Your own
  PC is never asked for the host password and is never locked out.
- **Friends play as players.** Make Me GM changes only your own account, and `/travel` and `/bot pilot` work only on the
  host's PC.
- **Bots with more than one of you:** your companions follow you, always, and a friend's `/pull` never takes them.
  Gamebots you invite follow the group's leader. Each of you gets quest credit for a gamebot's kill, and bots answer
  each of you in turn.
- **Character passport:** CHARACTER PASSPORT… takes one of your characters to a friend's world with its gear, quests and
  the coin you choose, and COME HOME brings it back with what it earned. While it's away it's locked at home. Every step
  backs your world up first.
- Not yet: the new client (0.37c) can't use Join Friend.

### Bots that look and fight like players

- **Bots dress up, at the game's own prices.** A gamebot spends spare coin on dyes for its armor and, if it likes to show
  off, a weapon luster, from the same dye merchants you use and at their prices. Each bot has a look of its own: some
  never bother, some wear one tidy colour, a few are peacocks in the dearest shades, many lean on their realm's colours,
  and a guild often wears one. Bots never buy glows: glowing weapons still come from epic and dragon drops and the
  Darkness Falls merchants. The server setting `bot_cosmetics` (on by default) turns it off.
- **Flanking.** A melee gamebot that knows a back or side style steps behind or beside a monster that is fighting
  someone else, and swings that style. It only walks where the navigation mesh says it can. Never for tanks, companions,
  bots in a group you lead, raids or epic encounters.
- **Positional styles.** Bots used to swing front styles even when they stood behind a boss. Now they use their side,
  back and front styles when they are really there.
- **Smarter style choice.** Melee gamebots chain their styles, use reactive styles after an evade, block or parry, and
  stop spamming taunts unless they tank for a group.
- **Circling and stepping.** Melee bots circle and adjust in a fight, and casters step between casts. Stops and turns no
  longer snap to a point, and a moving party no longer jerks once a second. Bots idle at the bank and the usual crowd
  spots in town.
- **Knowing when to leave.** A gamebot party calls RUN while everyone is still alive, when the fight is going wrong, and
  runs to a bindstone or the camp's safe spot. A solo bot bails out of a losing fight, and a healer in your group whose
  player died runs and comes back to raise. A party with you in it never calls RUN.
- **Smarter camps.** Parties stand by role at their camp (tanks in front, healers and casters behind), read what a
  camp holds before they choose it, take the monster standing apart first, and move on when a camp stops paying.

### Questing and battlegrounds

- **The epic chains are linked on every install**, by the server as it reads the quests. Nothing is written to your
  world, and no Python is needed.
- **Repeatable quests are offered again**, deliveries in quests without a collect column can be handed in, and a
  collection quest takes its item whatever the case of its name.
- **Battleground rallies.** Once a keep's gates fall, each realm rallies inside the breach and goes up to the keep lord
  together, with you counting double. (It isn't there yet: see Known issues.)

### Other changes

- **Two Darkness Falls exits work:** the one by Legion's hall and the one near the Midgard boss now take you home.
  Nothing is written to your world.
- **New client (0.37c): skills on the quickbar, Stealth in the Abilities list, and a SELL button at merchants**,
  installed only over the released scripts; ROLLBACK.cmd puts them back.
- **Setup retries a busy download server.** When the download server answers "busy" (429, 500, 502, 503 or 504), Setup
  tries again by itself after 30, 60, 120 and 240 seconds, keeping what it already downloaded.
- **The loading screen says LIVING SERVER**, and upgrades from 1.1, 1.1.1 and 1.1.2 get it too.
- **Commands typed in the server window work again.**
- **Level-50 bots from the launcher arrive trained** in their builds. Before, they loaded with every line at 1 and raids
  formed in their first minutes fought with level-1 skills.
- **Logs take far less room.** A full server wrote about 300 MB of logs a day and kept a week of them. Now the server log
  keeps three days, routine bot travel goes to its own two-day file, and every log has a limit. Existing installs get the
  new settings at their next start; the old ones are kept beside them.
- **Lean server memory**, a new test option in the launcher (off by default), starts the server with .NET's
  memory-saving settings. It changes nothing in play.
- **Safer updates:** the patcher refuses a damaged update that names a file outside the game folder.
- **For testers:** `/epictest sweep` (GM only) runs every raid once, one after another, with the GM seated as a guest.

### Everything Living Server adds to Offline DAoC

Living Server is an add-on for Offline DAoC 0.37, the single-player Classic + Shrouded Isles server. It began as the
Social Expansion; seven releases later, this is what it adds to the game, all of it free, and every part on a switch
of its own.

**A server that talks like 2003.**
- **About 34,000 lines of period chat and 393 /faction scenes and debates**, written for the game as it stood from
  launch (9 October 2001) to patch 1.65 (8 October 2003). A validator turns away anything later: no Atlantis, no master
  levels, no "toon". Each line is chosen by realm, class, level, zone, time of day, what's happening and the bot's
  personality, in five writing styles, typo-prone "*fixes" and all.
- **Arguments the realm never settles:** smite against rejuv, buffbots, zergs, relic panic, dragon raids. Each realm argues its
  own lore in its own voice.
- **They only say what's true.** "where are the mids?" names the zone the enemy is really in. Keep, relic and raid
  news is real, and so are the rumours. Ask about a trainer, a camp or the way to Cornwall and the answer comes from
  the live world.
- **They read what you type.** "ty", "inc", "rez pls", "lfg 24 cleric": read for what they are. Ask "why?" or
  "where?" after a bot's line and that bot answers. Tells carry on as conversations.
- **A realm with regulars.** A handful of talkative bots carry each evening, each with its own manner and running
  jokes, and ask after things that were really said. Some bots have history with each other that plays out over days,
  and you can get involved. Not every raid goes to plan.
- **Names like a 2003 server**, from a study of about 700 real character names of 2001-2004: three in four bots get
  one a 2003 player might have picked, the rest a name from their realm's own lore.
- **How much they talk** is yours to set, chat rises and falls with the crowd and the hour, and `/lswhy` tells you why
  any bot said what it said.

**Bots with lives.**
- **Personalities:** every bot is a Leveler, Socialite, Daredevil, Explorer, Crafter or No-lifer, with five traits that
  decide who grinds, who parks in town and who lives in the frontier. Target one and type `/personality`.
- **Bot guilds:** leveling, social, RvR, raiding and crafting guilds that recruit you, talk in /gu, claim and defend
  keeps, pay dues, earn merit, buy guild buffs and form alliances. Gamebots in your own guild pay dues and earn merit
  too.
- **They remember.** Group with a bot and it remembers you, greets you hours later and asks to group again. Bots
  remember your rezzes, your insults and each other, for better and for worse.
- **Server life**, 17 parts: grats, server firsts, rez requests and corpse runs, frontier alarms, grudges against their
  killers, "brb" breaks and linkdeads, a wave when you walk up, and the **Camelot Herald** printing your world's news.
- **A real clock.** With the Bot schedule on Real clock the realm keeps a real server's day: the evening crowd from 7 to
  11 pm, a few hundred before dawn, busier weekends.
- **Talk to bots by name:** `/send` any bot of your realm, `/invite` or `/target` a nearby one, `/friend` them.

**An endgame that runs itself.**
- **Realm raids on their own schedule:** each realm's dragon, its epic dungeon, Summoner's Hall, Darkness Falls and the
  raid dungeons from Krondon to Marfach Caverns, 21 raid targets in all [if it lands: and 16 open-world big bosses].
  Each musters, travels, clears its way and goes for the final boss, whether or not you come.
- **Raids the size they were in 2002-2004**, from 50 bots for Dodens Gruva and Tur Suil to 150 for Crystal Cave, each read from the
  period's forum threads and guides. A win trims the next raid a little, a defeat adds more back, so each encounter
  settles where it is won about two times in three. The bosses are never weakened.
- **Wipe, regroup, go again,** and up to 24 geared level 50s of the **Epic Special Forces** answer a wiped raid, in a
  dungeon or at a dragon.
- **Contested raids:** two realms in Summoner's Hall or Darkness Falls race for one boss, and only one gets the kill.
- **Keep sieges with a fight on both sides:** warbands open sieges, scouts report the army as it marches, defenders
  man the walls, and attackers break off to save their own keep.
- **The epic encounters at full strength** with a raid plan for every big boss, as above, and an encounter catalog that
  records what every boss script does.

**Bots that play with you, and friends who can join.** Bot-led groups, raid invites, a fair loot share, RvR bots that
notice you like a player would, play with friends and the character passport, as above. Quests count your bot group's
kills, a kill advances a step once, and quests you've outlevelled can still be handed in.

**A world with a past.** The lore, the loremasters, the books, the Bestiary, the tales and the bosses' own words, as
above.

**Your game, your way.**
- **The launcher's Living Server tab:** bot voices, personalities, bot guilds, server life, Atmosphere lines, How much
  they talk, the Bot schedule, Bot leaders invite you, Raid leaders invite you, When you join a bot's group, Your loot
  share, Bots in your guild, Power regeneration and XP by level, each with its own switch, plus forced raids from the
  dashboard.
- **XP by level range**, separately for you and the bots, so levels 1 to 34 stay worth playing and 35 to 50 goes
  faster. Quest XP follows the same rate.
- **Quest fixes:** quests that can be finished, quest markers that mean what they say, the epic chains in order, kills
  by your bot group that count.
- **One-click Setup** installs Offline DAoC and Living Server from nothing, or adds Living Server to the game you have.
  The launcher's **Check for updates** brings new versions, checked against GitHub's published SHA-256.
- **Safe by default:** a crash-safe world database with a verified daily backup (the last 7 kept), a backup before
  every install, the server listening on this PC only unless you host, and `ROLLBACK.cmd` to put every replaced file
  back.
- **Smarter, safer bots:** they bend their road around monsters far above them, remember where bots keep dying, walk
  around epics they didn't come for and stop standing under flying monsters. Fixes for lag spikes and memory leaks found
  in overnight runs.

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
  Darkness Falls (at Legion), and never reached Albion's Summoner's Hall. Known causes with no fix yet: Krondon's Lyft
  Miht room, Golestandt's second attempts, Summoner Roesia.] If a fight is too hard on your world, each restored rule
  is a server setting of its own and can be switched off alone.
- **The new raid plans are tests only.** The Darkness Falls raid AI, Legion's plan, the plans for the Midgard and
  Hibernia big bosses [if it lands: , Albion's big bosses and the open-world bosses] and the raid fixes after the sweep
  of 14 November pass their automated tests but have not been seen on a real install. Behemoth is never fought, Morgana
  has no raid (she was never a raid boss), and Fomor City has no raid of its own.
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
Nikse77 and Cancain. Boss
lore checked against Mythic's Camelot Herald, and raid plans
against the period's guides and forums. Reports from the community on GitHub and r/daoc. Directed and play-tested by
[chris4aker](https://github.com/chris4aker), coded with Claude (Anthropic). GPL-3.0; licences and attributions are in
[THIRD_PARTY.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/THIRD_PARTY.md) and the changelog.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
