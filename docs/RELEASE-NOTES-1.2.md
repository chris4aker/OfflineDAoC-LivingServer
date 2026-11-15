# Living Server Beta 1.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for `v1.2-beta`, "Where Legends Wake". It is a draft for
the owner's approval: the "How it was tested" section is finished after the owner's played check. In the file table the
Setup row must keep exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the
exe's SHA-256. The zip's SHA-256 is filled in by hand when publishing. Publish it as Latest, not as a pre-release: the
launcher's update check reads GitHub's latest release, which leaves pre-releases out. Lines marked "[if it lands: ...]"
describe work still on its own branch: keep them only if it is merged before the tag, and drop the marker. Lines marked
"[owner: ...]" are for the owner to fill in before publishing. Nothing here names the Easter eggs or the project's
internal vocabulary. Polished on 2026-11-14 (evening) for the launch, covering everything merged into 1.2 through #225
(and since then #226, #228, #229, #230 and #231):
the raid flow and boss tactics from the sweep, the Darkness Falls raid AI, Legion, the raid plans for every realm's big
bosses and the open-world bosses, the route blockers, the period research, the upstream boss fixes and the epic armor
rule. Every figure was checked on 2026-11-14 against integration/1.2 at 1dc66b6 (#225): the encounter counts against
docs/encounters/EPIC-ENCOUNTERS.md and epic-encounters.json, the lore and tale counts against the lore and chat files,
the settings against their defaults, and "new in 1.2" against Beta 1.1.3. Rechecked on 2026-11-15 against
integration/1.2 at b56f62ff7 (#231): the epic settings' defaults in ServerProperties.cs and EpicEncounterMechanics.cs
(20 restored rules with a setting of their own, 15 on; Govannon 200,000 health unless `epic_govannon_full_health` is
on) and the encounter counts (202 fought, 105 with tactics on the routes and open-world raids). Brought up to date on
2026-11-15 with integration/1.2 at 1cd6b88ef (#245, Beta 1.1.4 merged in), which adds #235 to #244 and #246: the
catalog's rules went from 862 to 871 (its plan counts and the settings are unchanged), and the owner's decisions of
2026-11-15 ([PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md), "Owner decisions 2026-11-15") apply: the storytellers,
fireside tales, loremasters and the Epic Special Forces are hinted at, never explained (17); the new client is
maintenance-only (16); Trials of Atlantis and Catacombs are only rumours (19); Behemoth, Fomor City and Morgana are
signed off without a raid (3 to 5). In-game results name their build and come from PLAYED-CHECK-1.2.md. Lines marked
"[owner: confirm ...]" are
results from the owner's PC that the repository has no record of; confirm or drop them before publishing. Owner's
decision (2026-11-14): credit lightly, by project and username in the Credits section only, never per feature or
per piece of code; the licence notices stay in THIRD_PARTY.md and the changelog. The repository description, the
tagline, the posts and the pitch are in [RELEASE-1.2-COPY.md](RELEASE-1.2-COPY.md).

---

### Where Legends Wake

*The bosses remember their rules. The realm remembers their stories. And the bots want you in the group.*

It's eight o'clock and you're medding at Svasud Faste when /faction goes still.

Someone has asked for the old tale of Gjalpinulva. Two others tell the channel to hush. The tale comes line by line,
and then nobody types for half a minute.

A line crosses the middle of your screen. A horn sounds. Midgard is calling its raid.

A tell arrives: "forming for Gjalpinulva at West Skona. want a spot?"

You say yes. When she hurls you high over her lair, you are not the only one in the air, and the healers turn to you
first. Somewhere behind you, the casters are already running down her messengers.

That is Living Server 1.2. It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client, like 1.1
to 1.1.4. As in 1.1.4, a new install from Setup is **0.37**.

### What's new in 1.2

- **Most of the epics' own rules, back.** 15 of 20 restored original rules are on by default. Throws, jails and
  glares now land on bots too.
- **A raid plan for every big boss.** 111 encounters with a plan of their own, 109 of them new or reworked in 1.2, from
  Legion to the Glacier Giant.
- **37 raids on the schedule.** 16 open-world giants now draw a realm raid of their own.
- **Darkness Falls, with a battle plan.** High lords, princes, Legion, and the long walk out of Behemoth's hall.
- **A story for every boss.** 578 bosses and 107 places, more than a million words, checked against 832 Camelot
  Herald articles.
- **Bots that play with you.** They invite you, lead the group, and save you a raid spot.
- **Bring your friends.** Host your bot world for a few friends, and take your character to theirs. Experimental.

### The epics, back on most of their own rules

For years the hardest bosses ran softened so that bot raids could win. In 1.2 most of that softening is gone.

- **The old rules are back.** The horsemen heal when dragged from their rooms. Morbus hides behind his swarm. The
  Skeletal Sacristan walks his round and never stops. Soul Reckoner shrugs off damage while a soul lives. Aroon's Scaiths
  die in order or not at all. Beatha heals Xaga. Kvasir drinks cold. King Tuscar and Queen Kula heal each other.
- **15 of 20 restored rules are on by default**, each a server setting of its own. Five stay off: Funus's class and
  weapon puzzle, the Fire primal's original fire patches, Easmarach's endless heal on the falls, Govannon's full heal
  at 50% (he heals to 75%), and Govannon's full health: he has 200,000 by default, and the original 300,000 is one
  setting away (`epic_govannon_full_health`).
- **Bosses pick from the whole raid.** Throws, glares, debuffs and "random target" spells used to pick real players
  only. Now they pick you, your companions and the gamebots alike. With bots around you are one target among many,
  never chosen more often than before.
- **The dragons throw again.** Golestandt, Gjalpinulva and Cuuldurach have their wing gust back and hurl two to five
  raiders high over the lair, as high as the original. Xanxicar, Kula, Hakr, Hurika, the Glacier Giant,
  Krackenschtein, Sys'sro and others throw, port and jail bots as well.
- **Thrown bots get back.** A thrown bot falls, takes the fall damage you would, and walks back to its party. When it
  can't, it is back with its raid after a 5-second recall.
- **No add floods.** A bot raid can outnumber a 2003 raid. Bosses that spawn adds or heal per raider count at most a
  period-sized raid, so a big bot raid faces what a full raid of players did.
- **No zerg fests.** An epic's armor now wears down under bot attackers too, at half a player's strength, and bots
  alone take off at most half of it: an epic boss goes from 160% to 100%, never to the 40% that players can reach.
  Numbers help. A zerg still doesn't win.
- **Your companions stay at the fight** when a boss throws you.

### A raid plan for every big boss

You can't out-damage a puzzle. So the raids learned the fights, the way a 2003 raid leader would have called them.

Every boss script in the game was read into one catalog: 214 scripts, 238 encounters, 871 rules, each checked by hand
against its script. Bot raids now fight 202 of those 238 encounters (the other 36 are not raid content), and 111 have
a plan of their own: 105 on the dungeon routes and in the open-world raids, the three dragons, and three Fomor City
bosses (Myrddraxis, Anurigunda and Krackenschtein), whose plans serve the gamebots who fight there: Fomor City has no
realm raid. The plans were set against what the period wrote down: the raid guides on
the VN Boards, FreddysHouse and Allakhazam threads, the Prima guide's maps and the Herald itself, much of it recovered
from the web archive. Where the period and the script disagree, the bots play the script.

**Darkness Falls.** On the owner's PC (build e3b8238), before any of this existed, Midgard's bots killed 41 named and
then Legion, in one attempt. Hibernia's lost to him twice. Now every realm goes in with a plan. On build 1dc66b6, with
it, Hibernia's raid, one the game opened itself with 136 raiders, had 69 targets down and no wipe after three hours and
forty minutes; how it ended is not recorded.
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
Behemoth's hall walks out along the hall's north side, clear of Behemoth, and through the portal. Behemoth is never
fought: the guides of 2003 called him invincible.

**Albion.**
- **Apocalypse** takes to the air and rains fire; the raid moves to a ring outside the rain, then kills the Harbingers
  first. The back lines spread so one rain hits one party.
- **Caer Sidi's keylords:** Warlord Dorinakka and his twelve crypt knights fought as the one fight they are, the
  Silencer hit by at most nine raiders, so his resist chance stays at zero, Sanguis's blood mages shot down, Fate's
  Chosen before Bane of Hope, and the Spectral Provisioner ambushed on his loop. The Crypt Lord's court is cleared and
  his raid musters clear of his court and his patrol; the real Host and Lich Lord Ilron's dark lights each have their
  own plan.
- **Krondon:** Drihten's students, Deben's mages, Beran's barrels, the parts of Fuladl and
  Fulafeallan, Lurfos's splash, Ulor before Jehrog, and Scurceol Hyrde's orbs taken one at a time by the whole raid.
  Rooms already cleared stay behind the raid: the sweep leaves respawned ogres alone unless they stand on its way back.
- **Summoner's Hall:** the four summoners fought inside their leashes, Lossren's ghoul shot by every caster, one of
  Lossren's tortured souls left alive and held mesmerized so she calls no fresh wave, and Govannon's demon fought from
  clear of his Shroud.
- **Lord Gildas:** when he stuns his tank, the next tank taunts him off it.

**Midgard and Hibernia.**
- **Tuscaran Glacier:** Agmundr's hrimthursa first and everyone out of his cold blast, Steinvor's pillar of frost
  dodged, Guthlac's ice spike shot down before it erupts, the Council in their period order, Kula before the King
  while one tank holds him, and nothing cold near Kvasir: no bot, pet, style, area spell or cold-proc weapon or armor
  goes at him.
- **Galladoria:** the Water primal left alone in its pool, the Air primal shot from the room's middle, the
  Organic-Energy Mechanism fought from range, the Sporite Cluster fought in one tight crowd, each Earthmender taken by
  the classes that can hurt it, and add parties that take Spindler Broodmother's newborns off the healers and casters
  first.
- **Balor, Iarnvidiur and Nosdoden** each get a plan of their own.

**The open-world giants.** Before 1.2, no bot ever fought an open-world big boss. Now every one has a plan, and 16 draw
a realm raid sized from the period:
- **Albion:** the Green Knight, Coliath, the Spirit of Lord Emthoro.
- **Midgard:** the Glacier Giant, Mortufoghus, the Raumarik Revenant, the five Wretches of Winter (pulsing bladeturn
  and all).
- **Hibernia:** Evern, the Juggernaut, Curengkur, Marfoirwort, Mollunero.
- **Myrddraxis's heads** die in the order Thredh's hydra guide gave in 2003. Morgana gets no raid: she hosts her event
  and can't be attacked.

**Your party calls it.** Each mechanic is called in /group as it happens: "all on kula", "out of the fire", "no cold on
kvasir, it heals him". For Xanxicar's stomp the raid leader calls the run, and each bot reacts in its own time, some
late. A realm new to him loses more raiders; over its attempts it learns him.

Every plan here is new or reworked in 1.2 and passes its automated tests. Your raids are among their first real nights.
What has been seen on a real install is under "How it was tested", and the fights bot raids still lose are named under
"Known issues". Too hard on your world? Each restored rule switches off on its own.

### A raid is a realm event

- **The tale, then the horn.** When a realm's raid comes due, the boss's tale is told in /faction first. Then the call
  goes out across the screen with a horn, and the realm gathers.
- **Every four hours** per realm, instead of every two and a half. An interval you set yourself is kept.
- **No clock in the dungeon.** Caer Sidi used to be cut off at four hours with 12 named down. Now a raid ends when it
  wins, runs out of attempts, or goes an hour without progress.
- **Wipe, regroup, go again.** Raiders release at their party's post outside the dungeon, not in town, and go back in
  waves. Where that post is in the frontier, the whole raid gathers at one post instead, or back at its hub while
  enemies hold that one, so enemy patrols can't pick off raiders one by one. The next pull waits for 70% of the living
  raid, counting the ones still walking back. The first two times a boss resets while the raid still stands cost no
  attempt.
- **Cleared stays cleared.** A dungeon's epics wait to respawn while its raid is inside. A boss standing on the way in
  is fought as an objective of its own, not walked into, and no muster lands inside another boss's reach.
- **Raids keep moving.** The approach dungeon's monsters are cleared on the walk, a raider stuck in a pocket rejoins
  the raid's front instead of going back to the capital, and no party is posted where it can't walk to its hub.
- **Your seat, clearly.** An invite to a raid that already left says so, and your group window clears when a raid
  ends.

### A world with a past

Every dungeon, dragon and boss has a story, and you meet it everywhere.

- **107 places, 578 bosses and 4 realm histories**, from the dragons down to the zones' named: more than a million
  words written for Living Server, nearly 4,900 lines for the bosses to yell and about 5,200 atmosphere lines.
- **Mythic's word comes first.** 832 Camelot Herald articles, November 2001 to February 2005, were read from the web
  archive and checked against the lore. Where ours disagreed, Mythic's won: Dartmoor's history, Gjalpinulva's mother,
  the Legendary Afanc's return, Krondon's ogres. Where Mythic told no story, Living Server wrote one. Every compass
  direction was checked against the world.
- **Someone in each capital keeps the old stories**, among the books and the bards, and will talk to anyone who
  asks. What they know is written down too, and a volume sometimes turns up near the place it tells of, never in place
  of real loot.
- **The Bestiary**, a new Realm Records page: every boss of your realm, with what raid leaders say about it. Its full
  story opens once your realm brings it down. Raids won before 1.2 count.
- **The Herald** closes each raid's record with a sentence from its boss's story.
- **Bosses speak** as the fight starts, at their phases, at their big moments and as they die.
- **Atmosphere lines** cross your screen the first time each day you enter a dungeon, as you step into a lair, when a
  dragon takes to the sky, and at a realm's first kill of a boss. One every 30 seconds at most; realm news always comes
  first. A switch in the Living Server tab turns them off.
- **Bots know the stories.** The raid leader briefs the realm as the raid sets out. Ask a bot "whats in krondon?" and
  it answers from the lore.
- **Tales.** Every realm has someone who remembers how its great fights went, and each realm tells Legion and
  Govannon its own way. Ask, and listen. Rest long enough with good company and you may hear one by the fire.

### Bots that play with you

- **They invite you.** The leader of a gamebot party near your level may send a tell and an invite. Say yes and the bot
  runs the group: the camp, the pulls, the breaks. You travel yourself; it waits. `/lfg`, or a tell like "room for a
  cleric?", brings an invite sooner. `/invites off` stops them.
- **Lead or follow.** `/lead` takes the group. `/lead bot` hands it back.
- **They stay on their side of the border.** Port, jump or take a portal into another realm's lands and your bots stay
  where they are, your companions too; the party waits for you to come back. The frontier, the battlegrounds and
  Darkness Falls are unchanged.
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
- **Bots for the battlegrounds.** Each realm card in the launcher has **+ Lv.20** beside + Lv.1 and + Lv.50: bots of
  levels 20 to 24 for Thidranki, with that level's purse and no armor (they earn it). Like + Lv.50 bots, they arrive
  trained. They go to Thidranki for the Battlegrounds share you set for levels 20 to 49 (0% by default).
- **RvR and battleground kill quests can be finished.** Pazz's battleground dailies and the realm's daily, weekly and
  monthly player-kill quests counted only real players, so in a world of bots none could be done. An enemy realm's
  gamebot counts now. Caledonia's dailies ask for its own bracket, 30 to 35.
- **New client (0.37c) fixes:** skills on the quickbar, Stealth in the Abilities list, and a SELL button at merchants,
  installed only over the released scripts; ROLLBACK.cmd puts them back. The new client is in maintenance now: fixes
  only, no new features.
- **Boss scripts, hardened:** each boss's death runs once, patrols follow the navmesh, the dragons face
  their lairs, and bosses keep their own state. No boss was weakened.
- **The loading screen says LIVING SERVER**, on upgrades from 1.1, 1.1.1 and 1.1.2 too.
- **Level-50 bots from the launcher arrive trained** in their builds, instead of fighting their first raid with level-1
  skills.
- **Logs take far less room.** A full server wrote about 300 MB a day and kept a week. Now the server log keeps three
  days, routine bot travel has its own two-day file, and every log has a limit.
- **Commands typed in the server window work again**, the patcher refuses an update that names a file outside the game
  folder, and **Lean server memory** is a new test option in the launcher (off by default).
- **For testers:** `/epictest sweep` (GM only) runs every raid once, back to back, with the GM seated as a guest. If the
  server stops mid-sweep, the guest's GM level and realm come back at the next start or login (tests only).

### Everything Living Server adds to Offline DAoC

Living Server is an add-on for Offline DAoC 0.37, the single-player Classic + Shrouded Isles server. It began as the
Social Expansion. Seven releases later, this is the whole of it: free, and each part of bot life on its own switch.

**A server that talks like 2003.**
- **About 35,000 lines and 418 /faction scenes**, written for the game from launch (9 October 2001) to patch 1.65
  (8 October 2003). A validator turns away anything later: no master levels, no "toon", and Atlantis is only
  harbour talk that nobody has come back to prove. Five writing styles, typo-prone "*fixes" and all.
- **The arguments never settle:** smite against rejuv, buffbots, zergs, relic panic, dragon raids, each realm in its own
  voice.
- **Nothing made up.** "where are the mids?" names the zone they're really in. Keep, relic and raid news is real, and so
  are the rumours. Ask about a trainer, a camp or the road to Cornwall and the answer comes from the live world.
- **They read what you type.** "ty", "inc", "rez pls", "lfg 24 cleric". Ask "why?" after a bot's line and that bot
  answers; tells carry on as conversations.
- **Regulars.** A handful of talkative bots carry each evening, each with its own manner and running jokes. Some have
  history with each other that plays out over days, and you can get involved. Not every raid goes to plan.
- **Names like a 2003 server**, from a study of about 700 real character names, about 300 of them from 2001-2004.

**Bots with lives.**
- **Personalities:** Leveler, Socialite, Daredevil, Explorer, Crafter or No-lifer, with five traits that decide who
  grinds, who parks in town and who lives in the frontier. `/personality` on any bot.
- **Guilds:** leveling, social, RvR, raiding and crafting guilds that recruit you, talk in /gu, claim and defend keeps,
  pay dues, earn merit, buy guild buffs and form alliances. Gamebots in your own guild pay their dues too.
- **Memory:** group with a bot and it remembers you, greets you hours later, asks to group again. Bots remember your
  rezzes, your insults and each other.
- **Server life**, 18 parts: grats, server firsts, rez requests, frontier alarms, grudges against their killers, "brb"
  breaks and linkdeads, a wave when you walk up, and the **Camelot Herald** printing your world's news.
- **A real clock:** the evening crowd from 7 to 11 pm, about one bot in eight before dawn, busier weekends.

**An endgame that runs itself.**
- **37 raids on the schedule:** each realm's dragon, its epic dungeon, Summoner's Hall, Darkness Falls, the raid
  dungeons from Krondon to Marfach Caverns, and 16 open-world giants. Each musters, travels, clears its way and goes for
  the final boss, with you or without you.
- **Period-sized raids:** from 40 bots for Dodens Gruva and Tur Suil to 120 for Crystal Cave, each read from the forum
  threads of 2002-2004, and each raid starts a quarter above its period size. A win trims the next raid, a defeat
  adds more back, so each settles where it is won about two times in three. The bosses are never weakened.
- **The Epic Special Forces:** when a raid is on its knees, it may not be alone for long.
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
  0.37 (the original client and the Classic + Shrouded Isles classes), downloaded from Living Server's own copy of
  shadowofze's release files. Windows SmartScreen may say "Windows protected your PC" because the exe isn't code-signed:
  click More info, then Run anyway.
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
- **On the owner's PC, test builds of 1.2 (10 to 15 November):**
  - Bot-led groups: a gamebot leader's invite, following it, `/lead` and `/lead bot`, leaving and the replacement call,
    and the party letting you go after half an hour when you never come (builds 3 to 5, and 1dc66b6, where
    `/makeleader` and `/invites` were seen too). On 1dc66b6 bot-led parties called no pulls in /group (fixed since in
    #234, tests only).
  - A raid leader's invite, accepted, and a seat in the raid: Golestandt (build 5); Hall of the Corrupt, Caer Sidi,
    Krondon and Coliath (1dc66b6).
  - The boss's tale, then the call with its horn, before a raid of the game's own (builds 3, 4 and 1dc66b6).
  - A tale asked for and heard to its end (build 4 and 1dc66b6).
  - Boss yells, atmosphere lines and realm first-kill lines on screen (1dc66b6).
  - After a wipe, the raid going back in waves (build 3). On build e3b8238 the waves still fired, but raiders released
    in town and took 20 to 30 minutes to get back, and the early pull counted only the raiders already inside (16 of
    18 in a raid of 118). Both are reworked since (#213, tests only).
  - [owner: confirm against the PC logs; PLAYED-CHECK-1.2.md takes these test-build results from this draft.]
    Raids won: Golestandt (build 5, second attempt), Cuuldurach (build 5, a raid the game opened itself), Marfach
    Caverns (builds 2, 4 and 5) and Dodens Gruva (build 5).
    Raids lost: Galladoria at the Organic-Energy Mechanism, Trollheim at Nosdoden, and Caer Sidi cut off by the old
    four-hour limit with 12 named down (build 5).
  - Midgard's raid won Summoner's Hall, Grand Summoner Govannon included (build 3e64140, 11 November).
  - Bots buying dyes and lusters at the game's prices (build 6).
  - Play with friends: a second game client joined over the home network with the host password and made its account
    (build 7). A session played together has not been checked yet.
  - The sweep and the raids of 14 November (build e3b8238, bots only, before the Darkness Falls raid AI, Legion's plan
    and the sweep's raid fixes): Midgard's Darkness Falls raid, which the game opened itself, killed 41 named and Legion
    in one attempt, with no wipe. Hibernia's lost to Legion twice (lowest 92% and 60%). Caer Sidi, Tuscaran Glacier and
    Galladoria were lost, and Albion's Summoner's Hall was never reached.
  - The night of 14 to 15 November (build 1dc66b6, bots only in the sweep, a GM character all night):
    - **Won:** Hall of the Corrupt, a raid the game opened itself with the boss's tale first and then the horn call:
      Aidon the Archwizard in one attempt, 71 minutes, no wipe. Krondon (sweep, at the game's raid size): Drihten
      Elreden, Lurfos the Herald and Orylle, 54 minutes, 63 raiders, no wipe. Coliath, a raid the game opened itself:
      Albion's first kill of him, on screen.
    - **Lost or stopped:** Galladoria killed Spindler Broodmother in 12.7 minutes with the new party posts, then
      stalled behind Easmarach's falls. Tuscaran Glacier killed Icelord Agmundr, then could not take Kvasir below 81%
      because the raid's own Spiritmasters' pets healed him with cold. Caer Sidi killed Lich Lord Ilron, then wiped
      once at Warlord Dorinakka and twice at the Crypt Lord (10 named, 54 minutes). Albion's
      Summoner's Hall lost at Aidon on its way in (two wipes, 58 minutes). Hibernia's Summoner's Hall lost at Summoner
      Lossren (two wipes, her lowest 20%). Dodens Gruva lost at Aros the Spiritmaster (two wipes, 103 minutes).
      Midgard's Summoner's Hall lost; the defeat line named Summoner Roesia, but on that build it named the raid's final
      boss, so where it was lost is not certain. Trollheim was stopped by the owner after 78 minutes, before Nosdoden,
      with no wipe.
    - **Darkness Falls:** Hibernia's raid, opened by the game with 136 raiders, had 69 targets down and no wipe at
      05:21; its end is not recorded.
    - Each of those losses that a bug caused has a fix since (#229, #230, #235, #236, #237, #240; tests only), and each
      of those raids is to be run again on the final candidate.
  - [owner: confirm; PLAYED-CHECK-1.2.md has no record of these yet.] The morning of 15 November (build 3d67b6f), a
    non-GM Albion character on Hadrian's Wall: Midgard's bots noticed him at 1,275 to 1,575 units with reactions of 0.6
    to 1.3 seconds, and at 516 to 660 up close, then mezzed, rooted and killed him. Bots made with the launcher's
    + Lv.20 button arrived trained and went to Thidranki. Midgard and Hibernia laid siege to Albion's Thidranki Faste,
    with the siege notice on screen. A Hibernian battleground wave went in after 22 minutes but never reached the
    gate, and the keep has not changed hands.
- **On a real install** (an overnight run of development build 76600af7, bots only, 7.8 hours): Fornfrusenen, the
  Council Vagn and Nokkvi, Torst and Hurika were killed; Tuscaran Glacier was lost at Icelord Agmundr. Bot raids skipped
  Scurceol Hyrde: they could not shoot his orbs. In the battlegrounds, 168 of 169 storms on a keep lord failed and no
  keep changed hands. The server stalled for 4 to 6 seconds three times.
- **Played:** [owner: one line on the played check, filled in before publishing]
- Please report anything odd on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
  `FORK_BUILD` line from `runtime\server\logs\server.log`.

### Known issues (it's a beta)

- **Fights the bot raids still lose.** On build 1dc66b6 (14 to 15 November): Galladoria stalled behind Easmarach's
  falls after Spindler Broodmother died (fixed since in #230); Tuscaran Glacier stopped at Icelord Kvasir, whom the
  raid's own pets healed (#235); Caer Sidi lost at Warlord Dorinakka and the Crypt Lord, whose plans were wrong
  (#236); Albion's Summoner's Hall lost at Aidon on its way in, two wipes in 58 minutes (#229, and #240 for raiders
  killed walking back through the Pennine Mountains); Hibernia's Summoner's Hall lost at Summoner Lossren, whose
  tortured souls kept coming (#237); Dodens Gruva lost at Aros the Spiritmaster; Midgard's Summoner's Hall lost, where
  is not certain; Trollheim was stopped before Nosdoden and has no result. The fixes are tests only, and each of those
  raids is to be run again on the final candidate. On build e3b8238 (14 November) Caer Sidi, Tuscaran Glacier and
  Hibernia's Darkness Falls (at Legion) were lost. [owner: the final candidate's re-runs, filled in before publishing:
  Caer Sidi, Tuscaran Glacier, Galladoria, both Summoner's Halls and Trollheim, each with its build.] Known causes
  still open: Krondon's Lyft Miht room (a tests-only fix since), raids leaving a boss for the monsters around it (High
  Lord Oro, Gatekeeper Dommel, High Lord Baln), Golestandt's second attempts and Summoner Roesia. If a fight is too
  hard on your world, each restored rule is a server setting of its own and can be switched off alone.
- **No raid for Behemoth, Fomor City or Morgana, by design.** Behemoth is never fought: a raider Legion throws into his hall walks back
  out. Fomor City has no realm raid, because there is no way in on its navigation mesh; its bosses keep their plans for
  the gamebots who fight there. Morgana has no raid: she hosts her event and can't be attacked.
- **The new raid plans are mostly tests only.** The Darkness Falls raid AI, Legion's plan, the plans for every realm's
  big bosses and the open-world bosses, the 16 open-world raids, the route fixes and the raid fixes after the sweeps of
  14 and 15 November pass their automated tests. Beyond the raids on build 1dc66b6 (see How it was tested: Darkness
  Falls there is Hibernia's raid, still running with no wipe when the record ends) they have not been seen on a real
  install. Of the open-world raids only Coliath's has been seen (1dc66b6); the others' hubs and routes have not been
  walked on a real install. The same goes for the epic armor rule and the boss script fixes.
- **Parts of 1.2 have only been tested automatically.** The bot-led groups, the tales and several raids have been seen
  on a real install (see above). Not yet seen there: most of the new boss tactics in a raid with a player in it, RvR
  perception [owner: confirm the 3d67b6f result under How it was tested, then drop it from this list], the keepers of
  the old stories and their books, the bots staying behind at another realm's border, the RvR and battleground kill
  quests, a session played together with a friend, and a character passport trip between two PCs. Real play may still
  find cases the tests miss.
- **Dragon fights can fall apart partway.** In the play tests [owner: confirm the builds] a raid on Cuuldurach
  collapsed at 69% and one on Golestandt at 46%, with most of the raid dying within a few minutes. Both dragons were
  beaten later.
- **Play with friends is experimental.** It needs the original client, and it has been tried on a home network only.
- **Some fights are harder than before**, and a raid of bots may lose them for a while (see above).
- **/faction can get too busy.** On an evening with about 600 bots a realm (the 1.1.3 build), it ran well above its
  ceiling, up to 20 lines a minute, with bursts of 10 to 16 reactions. Lower **How much they talk** in the Living
  Server tab if it is too much.
- **A very busy server can stall for a few seconds.** In an overnight run of development build 76600af7 it stopped for
  4 to 6 seconds three times. The searches behind those stalls are capped since, which has not been timed on a real
  install.
- **Some gamebots wait a long time at frontier teleporters.** Before 1.1.3, in one night, 368 times a gamebot stood 15
  minutes waiting for one before the server moved it on. Since 1.1.3 they go to the border keep first; that has not
  been checked on a real install.
- **Battlegrounds are an endless fight.** Bots storm the keep lord again and again and almost always fail, so the keeps
  don't change hands and the same bots die over and over. [owner: confirm, not yet in PLAYED-CHECK-1.2.md: on build
  3d67b6f a Hibernian wave in Thidranki never reached the gate and the keep did not change hands; the waves are being
  fixed.]
- **Bot-led groups:** gamebots don't share your horse or boat; the leader takes its own and waits at the other end. The
  break at the end of a session ends the party: it doesn't walk to town and back with you.
- **New client:** APPRAISE stays hidden, and you don't see yourself as see-through in Stealth yet. Both need a newer
  network client than this update installs.
- **More:** [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md).
  Every change, with its numbers: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

### Coming next

- **Linux:** a release of its own after 1.2: the 0.37 game with Living Server and the original client under Wine. The
  new client stays on Windows. [owner: the version number]
- **The new client (0.37c)** stays in maintenance: fixes, no new features.
- **2.0, the complete realm:** live events, the guild package, bots with alts, friends and grudges, crafting and a
  server with a past.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.2-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.2-beta.zip` | (owner: the zip's SHA-256, filled in by hand at publish) |

### Credits

Living Server is an add-on for Offline DAoC 0.37, built on shadowofze's Offline DAoC and the
[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core) project. Thanks to the community whose forks, work and ideas
helped along the way: shadowofze, rambodolphin13, stefanrows, lometur, Nikse77, Cancain and ryan-grafana. Boss lore
checked against Mythic's Camelot Herald, and raid plans against the period's guides and forums. Reports from the
community on GitHub and r/daoc. Directed and play-tested by [chris4aker](https://github.com/chris4aker), coded with
Claude (Anthropic). GPL-3.0; licences and attributions are in
[THIRD_PARTY.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/THIRD_PARTY.md) and the changelog.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
