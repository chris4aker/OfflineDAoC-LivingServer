# Living Server 1.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for 1.2, "Where Legends Wake" (owner 2026-11-19: "1.2"
final, not a beta). It describes all of Living Server as it is in 1.2, not only what changed. Publish it as Latest, not
as a pre-release: the launcher's update check reads GitHub's latest release, which leaves pre-releases out.

Before publishing:
- **Files table:** the Setup row must keep exactly `(filled in when the exe is built)`: the release workflow replaces only
  that text with the exe's SHA-256. The zip row must keep exactly `(zip SHA-256: filled in by hand at publish)`:
  `publish_release.py notes` replaces it with the zip's SHA-256. Both rows name the build (`1.2`); the tag is `v1.2`.
- **The `<!-- AA537CE -->` marker** in "Tests only" is where the result of the last candidate (`aa537ce`) on the owner's
  PC goes, as one line. GitHub doesn't show the marker.

Nothing here names the Easter eggs or the project's internal vocabulary.

---

# Living Server 1.2: Where Legends Wake and the Realms are Alive

*Offline. Never alone.*

> Living Server is an unofficial, free, non-commercial fan project. It is not affiliated with, endorsed by or
> connected to Electronic Arts or Broadsword Online Games. Dark Age of Camelot belongs to them.

This page describes all of Living Server as it is in 1.2, not only what changed. The tag after each part says which
release brought it: **1.0** (Beta 1.0, which carried every Social Expansion build before it), **1.1**, **1.1.2**,
**1.1.3**, **1.1.4** or **1.2**. **Offline DAoC** marks what the game already did before Living Server; it is listed only
where Living Server builds on it.

A feature counts as working only once it has been seen working on the owner's PC. Everything else says **tests only**.
Each "seen" result names the build it came from. None of them comes from the build you are downloading.

It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client. A new install from Setup is **0.37**
(the original client, Classic and Shrouded Isles classes), from the project's own copy of the 0.37 release files.

---

## 1. What Living Server is

Offline DAoC is the 1.65-era game, Classic and Shrouded Isles, on your own PC, with bots who level, group and fight
*(Offline DAoC)*. Living Server gives those bots the lives of 2003 players. They talk the way we talked, keep their own hours,
join guilds, remember you, craft and trade, call their own raids, take each other's keeps, and invite you along. It
runs while your server runs.

**A new world starts empty.** You make the population on the launcher's main screen with **+ Lv.1**, **+ Lv.20** and
**+ Lv.50** for each realm (Shift adds 10, Ctrl adds 100).

**People**
- **Personalities** *(1.0)*: Leveler, Socialite, Daredevil, Explorer, Crafter or No-lifer, with five traits that decide
  who grinds, who parks in town and who lives in the frontier. Target a gamebot and type `/personality`.
- **Period chat** *(1.0; rewritten 1.1; tuned 1.1.2, 1.1.3 and 1.2)*: hand-written lines in five writing styles, chosen by
  realm, class, level, zone, time and what is really happening, in /faction, /say, /group, /guild, /lfg, /trade,
  /broadcast and tells. Keep, relic, raid and zone talk comes from the live world.
- **They read what you type** *(1.0)*: "ty", "inc", "rez pls", "lfg 24 cleric", "is friar good?".
- **Bot guilds** *(1.0)*: levelling, social, RvR, raiding and crafting guilds with names over their heads, /gu chat,
  recruiting ("lf guild"), dues, merit, guild buffs, alliances and keep claims. Gamebots invited into your own guild
  *(1.1.2)*.
- **Memory** *(1.0; more in 1.1.3)*: who they grouped with, your rezzes and your insults; friends greet you, grat your
  dings and offer a rez.
- **Server life** *(1.0)*: grats, server firsts, rezzes and corpse runs, frontier alarms, nemeses, breaks, body
  language, and a bot schedule that can follow your PC's clock.
- **2003-style names** *(1.1)*, from about 700 real names of 2001-2004.
- **The Herald** *(1.0)*: a news page the launcher writes from your world.
- `/lswhy` *(1.1.2)* shows why a bot said something; **How much bots talk** (0-3) in the launcher sets the volume
  *(1.1.2)*.

**Play**
- **Levelling like people** *(1.0; Offline DAoC underneath)*: zones by personality, roads that bend around monsters far above
  them, memory of where bots keep dying. Reacting to attacks like players *(1.1)*. Gamebots start without armor and earn
  it.
- **Talk to bots by name** *(1.0)*: `/send <name>` any online bot of your realm; `/invite` or `/target` a nearby one.
- **Bots that invite you** *(1.1.3; much more in 1.2)*.
- **Quests** *(1.1, 1.1.2)*: quest markers, epic chains, your bot group's kills count for your quests.
- **XP by level range** *(1.0)*, for you and for the bots.

**The endgame**
- **Realm raids** *(1.0)*: each realm raids its dragon, its epic dungeon, Summoner's Hall, Darkness Falls and its raid
  dungeons on its own schedule. The raid musters, clears its way in, wipes, regroups and goes again. Two realms can meet
  in Darkness Falls.
- **Period-sized raids and the first boss tactics** *(1.1.2)*; raids that keep moving *(1.1.3)*.
- **Keep sieges with a fight on both sides** *(1.0)*: warbands lay siege, defenders turn out, called as the enemy army
  marches *(1.1.3)*.

**The economy:** the Realm Exchange and real loot *(Offline DAoC, kept as they are)*; bots sell only at natural task breaks.

**Yours to run**
- **The Living Server tab** *(1.0)*: every part has its own switch; **ALL OFF** gives you the plain server.
- **One Setup** *(1.1; fixed for new players in 1.1.4)* installs the game and Living Server, or adds Living Server to your
  game.
- **Updates from the launcher** *(1.1.2)*: **Check for updates**, then **GET UPDATE**. Nothing installs until you say so.
- **Backups** *(1.0, 1.1.2)*: every install backs up what it replaces; `ROLLBACK.cmd` puts it back; the server copies
  your world once a day.

**The rules that never change:** real loot, the Realm Exchange, stablemaster routes, selling only at natural breaks,
bots that start without armor and earn it, and Darkness Falls open to solo bots.

---

## 2. New in 1.2, by area

### Chat and bot speak

- **Bots answer you first.** A tell, or a line that names a bot, always gets an answer, however busy the channel.
  Replies to you have their own budget, so on a busy channel bots stop answering each other before they stop answering
  you. Bot exchanges already running wait for your answer to be said first (30 seconds at most).
- **/faction at a 2003 pace.** About 5 to 7 lines a minute on a full realm, never more than 7, with scenes slowed rather
  than cut. **How much bots talk** scales it. On a small server /faction is quieter than before.
- **Promises they keep.** A bot that says "omw" or "coming" to an alarm, a keep call or a call for help has to go there,
  with a deadline. A bot that can't go says something else ("how many?", "cant, sry").
- **Honest answers.** A bot no longer claims to be another class, role or level. "is it far?" after a place gets a
  travel answer. A call for help gets an offer or an honest no, never a story. Another realm's zone is "thats hib
  frontier, rvr only", not a route. /broadcast questions are answered where you hear them.
- **`/lswhy`** grew: `/lswhy me` (why bots answered your lines, or didn't), `held`, `budget`, `duty`, `skill` and `way`.
- **Bots dress up** at the game's own prices: dyes and weapon lusters from the merchants you use. Some never bother, a
  few are peacocks, many wear their realm's colours. Bots never buy glows. Server setting `bot_cosmetics` (on).
- **Arguments that run for days.** Ten more of them play out in /faction over several evenings, so a realm has fifteen
  to draw from.
- **Personalities, every Server life part and the real-clock schedule are on from the start.**

### RvR

- **They notice you the way a player would.** Gamebots in the frontier, battlegrounds and Darkness Falls used to see
  you from 5,000 units, all at once. Now it is chance and distance: always within 1,000 units, never beyond 3,500 on
  their own; the rest hear it when one calls it.
- **Groups fight like groups.** They call a target and switch together, assign crowd control, interrupt, peel for their
  healers, kite, and open from stealth. Armies leave a milegate on more than one road. Eight-man groups roam the
  fringes.
- **A role-built eight looks for a crowd-control build** (a mezzing Healer, Sorcerer or Bard).
- **Battlegrounds:** waves and storms on the keep lord, a rally spot per realm, and **+ Lv.20** bots that arrive with
  starter gear. Battleground keeps can't be claimed by guilds, as in 2003.
- **Movement:** melee bots flank a monster that is fighting someone else and use their side, back and front styles where
  they really stand; they chain styles and use reactive ones after an evade, block or parry; moving parties no longer
  jerk once a second.

### Raids and epics

- **The tale, then the horn.** When a realm's raid comes due, the realm's storyteller tells that boss's tale in /faction
  to its end. Then a large line crosses the screen and a horn sounds, and only then do the bots gather. Every open-world
  raid has a tale of its own now. A raid is never lost to its tale: if it can't start or runs long, the call goes out.
- **The bosses' own rules are back on.** The horsemen's tether heal, Morbus's swarm, the Skeletal Sacristan's patrol,
  Soul Reckoner's souls and small room, Aroon's Scaiths in order, Beatha healing Xaga, Kvasir's cold heal, King Tuscar
  and Queen Kula healing each other, the dragons' messengers and wing gust, and more. Of the 19 restored rules that are
  server settings, 13 are on, and each can be switched off alone. Still off: Funus's class and weapon puzzle, the
  original fire patches, Govannon's full health and full heal, Easmarach's endless heal at the falls, and the
  Earthmenders healing each other.
- **They pick bots too.** Throws, glares, debuffs and "random target" spells choose from the whole raid: you, your
  companions and the gamebots. A thrown bot falls, takes the fall damage you would, and walks or recalls back.
- **A big bot raid faces what a period raid did.** Bosses that make adds or heals per raider count at most a
  period-sized raid.
- **A plan for the real puzzles.** Kula first while one tank holds the King; no cold at Kvasir; a team clears the
  Jailer's tunnel; casters run down the dragons' messengers; Spindler Broodmother's newborns die as they come and her
  curers stand apart from her mesmerize; the Earthmenders taken by class; Aidon fought inside his home; Summoner Pain
  cured by the healers. Your party calls each mechanic in /group ("all on kula", "out of the fire").
- **Raid leaders call it, and a realm learns a boss** (Xanxicar's stomp). A realm new to him loses more raiders; over its
  attempts it loses fewer. **Still being tuned.**
- **One raid every four hours per realm**, and **no clock inside the dungeon**: a raid ends when it wins, runs out of
  attempts, or makes no progress for an hour.
- **After a wipe the raid comes back together** at the way in and goes back in waves; the next pull waits for 70% of the
  living raid. A boss that resets while the raid stands costs no attempt.
- **Raid leaders invite you.** At level 50 the raid leader may offer you a spot, once per raid.
- **Some fights are still being tuned, and the bots lose them.** See "Seen and tests only" below for each one.

### Companions

- **Your companions stay at the fight** when a boss throws you (server setting `epic_companions_stay_when_thrown`, on).
- **Hired companions.** Up to seven of your own, hired beside a healer of your realm (`/hire`, or the healer's
  [companions] link). Slots open at levels 5, 10, 15, 20, 30, 40 and 45; the first is free. You pick race, class, look
  and name; it joins at level 5 with a training weapon, levels only beside you and never passes your level. You gear it
  (`/hire gear <n>`), train its specs (`/hire train <n> <line>`), buy its realm abilities and set its AI options
  (`/hire options <n>`). It talks like the other bots, or keeps a formal 1.65 voice (its **Speech** option). `/hire help`
  lists every word. Some of the code is borrowed from the True 1.65 project. Switch: **Hired companions**.
- **Every bot is trained like a player.** Gamebots, `/spawn` helpers and hired companions get the half-level spec points
  above 40 and pay what you pay for autotrain lines.
- **With friends:** your companions follow you, always, and a friend's `/pull` never takes them.

### Guilds

- **Bot guilds are on from the start**, with their chat, recruiting, keep claims, dues, merit and alliances.
- **Guild crafters.** A guild's crafter bots make for guildmates at material cost. Ask "anyone craft?" in your guild's
  chat and the guild's best crafter answers with its title and skill.
- **Guilds have a look.** A guild often wears one colour scheme.
- **Raid leaders** are officers of a raiding guild in the raid.

### Crafting and the economy

- **Bot crafters** join an order, work at the forges and stations, earn titles, fill craft orders and sell on the Realm
  Exchange. They advertise in /trade, quote, haggle and take commissions by tell ("can you make", "how much for",
  "deal", "ready?").
- **You join an order at its crafting master**, the way a new character did in 1.65 (switch **Era start**). Crafting
  needs the original client.
- **Bots keep an economy:** upkeep and repairs, potions, luxuries, siege supply, and an economy ledger the launcher can
  show.
- Still off by default (the player's choice): the classic rule options **Half below 50% power**, **Secondary skill caps**,
  **Classic quality** and **Classic losses**.

### Lore, tales and storylines

- **Every place has its story.** 108 places, 578 bosses and four realm histories, from the dragons and the epic dungeons
  down to the named monsters of every zone, and the battlegrounds. Where Mythic told a story, it is kept, in our own
  words: 832 Camelot Herald articles from November 2001 to February 2005, read from the web archive, were checked
  against ours, and where they disagreed, the archive won.
- **Where you meet it:** loremasters in each capital (Aldwin in Camelot's library, Sigrun at Jordheim's temple, Eithne by
  the bards of Tir na Nog); lore books, a page at a time; the **Bestiary** in the launcher's **Realm Records** tab;
  `/lore`, `/lore <place or boss>` in either client; bosses that speak; short **Atmosphere lines** on screen (a switch).
- **Bots know the stories.** Ask "whats in krondon?". Ask for a tale in plain words ("tell the one about golestandt").
- **Arguments that run for days** in /faction (see Chat). Talk of Atlantis and the world below stays hearsay.

### Bots as people and 2003 builds

- **Skill that grows:** each gamebot is Casual, Average or Veteran (about 30/50/20) and gets better with play, one tier at
  most. Mistakes come mostly on strangers: a quarter as many in a group with you, and never on a named or a monster more
  than 2 levels up.
- **Knowing the way:** bots know their capital, home town, the zones they levelled through and, from 20, their border
  keep. Heading somewhere new they may ask the way, tag along, or take a wrong road for a minute or two.
- **Moving like people:** circle-strafing in RvR, a strung-out /stick line, sitting while they wait, crowding the bank,
  sitting on a held keep's walls.
- **Personal milestones:** the ding to 50, then realm rank 5 or a real dragon trophy. The Herald lists the last 40
  under **Milestones of the Realms**.
- **2003 class mix and 2003 builds** for **new** bots: each realm's period class shares and a period build for life, fully
  trained from 1.65 templates; one in ten on an odd spec. Bots already in your world keep their class and build. The
  shares and weights are estimates from 2002-2003 forum posts, not counts.
- Switches: the **BOTS AS PEOPLE** card (**Skill that grows**, **Knowing the way**, **Moving like people**, **Personal
  milestones**, **2003 class mix**, **2003 builds**). Touches autonomous gamebots only, never companions or raids.
- Bots never play the Sluaghbinder, in either edition.

### Bots that play with you

- **Bot leaders invite you.** A gamebot party leader near your level may send a tell and an invite. Say yes and it leads:
  the camp, the pulls, the breaks, "last pull then town". You travel yourself; it waits for you. `/lfg`, or a tell like
  "room for a cleric?", gets one sooner; `/invites off` stops unasked ones (at most one every 45 minutes).
- `/lead` takes the lead; `/lead bot [name]` hands it to a gamebot.
- **Loot with gamebots:** an upgrade for you goes to you, an upgrade for a bot to the bot that needs it most, and the
  rest is shared with your share counted twice (**Your loot share** in the launcher).

### Play with friends (experimental)

- **HOST FOR FRIENDS** on the launcher: set a password, press **START HOSTING**, send the invite code. Home network and
  Tailscale are on by default; the internet is a box you tick.
- **JOIN FRIEND** on a friend's PC: paste the code, pick an account name and your own password (6 to 13 characters).
  Five wrong passwords from one PC lock it out for ten minutes.
- **CHARACTER PASSPORT…** takes a character to a friend's world with its gear, quests and the coin you choose; **COME
  HOME** brings it back with what it earned. Every step backs your world up first.
- Original client only; the new client can't use Join Friend yet.

### Launcher and installer

- **Every Living Server feature is on from the start** (the owner's decision of 2026-11-16). A world from an earlier build turns each one
  on once, only where it still had the old default; a switch you set yourself stays.
- **New on the Living Server tab:** the **BOTS AS PEOPLE** card, **BOT-LED GROUPS** (**Bot leaders invite you**, **Raid
  leaders invite you**, **Your loot share**), **Hired companions**, **Atmosphere lines**, **No Lifer buffbots**, and
  **BOT CRAFTERS AND THE ECONOMY** and **CRAFTING (YOUR CHARACTERS)** cards.
- **Realm Records** has a **Bestiary** page: every boss of your realm's places, its full story once your realm has
  brought it down (raids won before 1.2 count).
- **+ Lv.20** joins + Lv.1 and + Lv.50 on each realm card.
- **Setup retries a busy download server** after 30, 60, 120 and 240 seconds, keeping what it already has.
- **Logs take far less room** (three days of server log, every log capped).
- The online count on the main screen refreshes every five minutes again (seen on `c81eb5d` and `43c43aa`).
- The patcher refuses a damaged update that names a file outside the game folder. The loading screen says LIVING SERVER.

### Also

- Epic quest chains are linked on every install, by the server as it reads the quests (nothing written to your world).
  Repeatable quests are offered again.
- **Borrowed from the True 1.65 project**, each credited in the changelog: boss script fixes (Suttung, Legion, Govannon, Vifil,
  Lossren, the dragons), better camp choice, and bot parties that know when to run. **Tests only.**
- **From other forks:** two Darkness Falls exits that take you home and smarter style choice (rambodolphin13), the flank
  step (from the True 1.65 project), no jerking in a moving party (Nikse77, via rambodolphin13).

---

## 3. Seen and tests only

"Seen" means seen on the owner's PC, on screen or in its server log, on the build named. Bot raids below were bots only,
started by the game or by the owner's test runs, unless it says otherwise. **None of these results is from the release
build.**

### Seen on the owner's PC

| Area | What | Build(s) |
|---|---|---|
| Chat | "is it far?" and "how do i get there?" after a place is named, answered with the trip and the route; a call for help answered by three different bots, with honest refusals; a /broadcast question answered in /broadcast; the /lfg tell when nobody is at your level ("quiet at that level right now, gl"); `/personality` and `/lswhy`; /faction at 4.9 lines a minute on average over 39 minutes (median 5, quiet minutes down to 2) | `43c43aa` (19 Nov, helper on screen and server log) |
| Chat | Honest refusals (a bot busy in its own group says so; another realm's zone gets "thats hib frontier rvr only"); /send and /say replies; a placed lore line said in /say (server log) | `c81eb5d` |
| Chat | Bots answering the player in /faction and by tell; a /broadcast question answered; a live keep-defence scene in /faction; `/personality` and `/lswhy`; a companion answering; /faction at about 5 a minute over ten minutes; a bot that couldn't go refusing a promise | `5154cae` (19 Nov, 25 minutes in Camelot) |
| Chat | Crafter replies to "what do you make?"; spec talk answering "anyone know a good paladin spec?" | `b1b6846` |
| Bots with you | A gamebot leader's invite, following it, `/lead`, `/lead bot`, `/makeleader`, `/invites`, the replacement call | test builds 3-5, `1dc66b6` |
| Bots with you | A raid leader's invite and a seat in the raid (Golestandt, Hall of the Corrupt, Caer Sidi, Krondon, Coliath) | test build 5, `1dc66b6` |
| Raids | The boss's tale, then the call and its horn; a tale asked for in plain words; one raid every four hours | test builds 3-4, `1dc66b6` |
| Raids | After a wipe, gathering at the way in and going back in waves | test build 3, `1dc66b6`, `9a173f4` |
| Raids | Raid healers curing poison and mesmerize (RAID_CURE_POISON, RAID_CURE_MEZZ in the log) | `9a173f4`, `c81eb5d` |
| Raids won | Golestandt, Cuuldurach (test build 5); Gjalpinulva (`88b6ab3`, `9a173f4`, `c81eb5d`, `43c43aa`); Golestandt again (`43c43aa`, after one wipe); Krondon (`1dc66b6`, `b1b6846`); Hall of the Corrupt (`1dc66b6`); Avalon City and Crystal Cave (`ccba364`, `b1b6846`); Tur Suil (`ccba364`, `b1b6846`, `9a173f4`); Marfach Caverns (test builds 2, 4, 5; `9a173f4`); Iarnvidiur's Lair (`88b6ab3`); Juggernaut (`88b6ab3`, `9a173f4`); Dodens Gruva (test build 5, `b1b6846`); Coliath (`1dc66b6`, `19e7037`); Evern (`ccba364`, `9a173f4`); Mollunero (`ccba364`); Spirit of Lord Emthoro (`b1b6846`); Darkness Falls (Midgard) with Legion (`e3b8238`, before the Darkness Falls raid work) | as listed |
| RvR | A battleground keep taken: Caer Caledon by Albion at 14:25 (RVR_KEEP_TAKEN), after its keep levels were repaired to guards 39 and lord 46; an Albion warband mustering 63 and marching on Dun Scathaig; frontier alarms; sieges announced and siege engines at a keep door | `43c43aa` |
| RvR | Stance changes, assist calls, crowd control and peels in the log (server log only) | `c81eb5d` |
| RvR | Bots noticing a player at 516-1,575 units, then mezzing and killing him | `3d67b6f` |
| RvR | Keeps changing hands: 15 in one evening, one five times | `ccba364` |
| RvR | Group target calls, crowd control, interrupts, peels and kites in the log | `19e7037` |
| Crafting | Bot crafters at the stations: 2,109 sessions, 11,022 pieces, 6,903 Exchange sales in one evening | `ccba364` |
| Crafting | Craft orders filled: 44 made, 1 expired; 147 Realm Exchange sales by bots in 34 minutes (server log) | `c81eb5d` |
| Launcher | The online count updating on the main screen: 5, then up to 2,875 online | `c81eb5d`, `43c43aa` |
| Cosmetics | Bots buying dyes and lusters at the game's prices | test build 6 |
| Lore | Boss yells, atmosphere lines, realm first-kill lines; the `/lore` window; a loremaster standing in Camelot | `1dc66b6` |
| Companions | `/hire`, the companion following; gear handed over and visible; `/hire train`; a companion summoned at login | `5ddd003`, `b1b6846` |
| Bots as people | Skill tiers 32/48/20 across 535 bots, bots growing a tier, asking the way (131 asks), movement habits (strafing, losing the stick, overshooting, sitting), the era builds loading (server log only, not watched on screen) | `c81eb5d` |
| Friends | A second game client joining over the home network with the host password | test build 7 |

### Lost or still being tuned (bots only)

- **Caer Sidi:** lost at the Crypt Lord, whose court and patrol beat the raid (`b1b6846`, `9a173f4`).
- **Galladoria:** lost at Spindler Broodmother (`9a173f4`); on `b1b6846` it ran six hours without reaching Olcasgean.
  The Earthmenders' mutual heal is off by default; turned on, the bots can't win (`b1b6846`). Fixes for the primals
  since: tests only.
- **Tuscaran Glacier:** lost at Queen Kula after two wipes, every Kula mechanic handled (`b1b6846`); lost at Suttung and
  the Council (`9a173f4`); lost at King Tuscar after 365 minutes and 40 named (`ccba364`).
- **Summoner's Hall (all three realms):** lost at Summoner Roesia (`b1b6846`, `9a173f4`). Healers now cure her Summoner
  Pain first: **tests only**. **Grand Summoner Govannon has never been beaten by bots.**
- **Trollheim:** timed out before Nosdoden fell (`ccba364`, `b1b6846`). Fixes since: tests only.
- **Darkness Falls (Albion):** looped on a chthonic knight (`b1b6846`). Fixed: tests only.
- **Golestandt** lost after two wipes on `9a173f4` and `c81eb5d` (won on test build 5 and on `43c43aa`). **Cuuldurach**
  lost after two wipes on `c81eb5d` and `43c43aa` (won on test build 5). Dragon fights can fall apart partway.
- **Green Knight** and **Marfoirwort** lost (`ccba364`). **Curengkur** never formed (no route; fix tests only).
- **Legion:** some rules are in (wounded raiders step out of his reach, his adds and heals are capped), but the raid does
  not play his platform, the way down or his add waves. Behemoth is never fought; Fomor City and Morgana have no raid.

### Tests only (not seen working on the owner's PC)

- Mistakes and their rate, milestone talk, raid learning, places seeded on a new world, the sit emote in the original
  client.
- Era builds' class mix on a fresh world, and any raid fought by an era-built world.
- Crafting as a player; a completed trade with a crafter bot (tried on `b1b6846`, not finished; fixed since);
  the launcher's crafter card and economy page.
- Hired companions joining a fight on a grey monster (failed on `b1b6846`, fixed since), and their chat.
- Roaming eights finding fights; open-field tactics judged by eye.
- A session played together with a friend; a character passport trip between two PCs.
- Loremasters' dialogs, lore books, the Bestiary page and the Herald's chronicle.
- Promises kept to the end (a bot that says it is coming arrives), and a storyline beat meeting a player's question in
  /faction at the same moment (no overlap came up on `43c43aa`).
- Relays between groups in a dense warband on `c81eb5d` and later (no group was close enough).
- The RvR route fixes after `43c43aa` (the Cruachan Gorge seam, Odin's Gate muster posts, the keep-route restart, the rally
  delay alarm) and the null guard for the casting crash that removed one bot on `43c43aa`.
- Every raid fix after `9a173f4` (Roesia's cure, Suttung's pulls, Steinvor's pillar, Curengkur, Hibernia's dragon hub).
- Updating a 1.1.4 game through Setup, a new install from Setup, and a class-off (0.37) world, on the owner's PC (CI runs
  them).
<!-- AA537CE -->
- **Automated:** the full server suite on integration `631ba1f`: 9,200 passed, 0 failed, 2 skipped. CI's build-and-test
  on the candidate `aa537ce` (`631ba1f` merged into main): 9,109 server tests passed, 0 failed; the other 65 read an
  installed game's files and run only on a PC with the game. The rest of that CI run passed.
- **On the release build** (its commit is in BUILD-INFO.txt and the server log's FORK_BUILD line): it is cut from main
  when 1.2 is published, so none of the results above comes from it. The last candidate tested is `aa537ce`.

---

## 4. Known issues

- **Bot raids still lose some fights**, named above. Each restored boss rule is a server setting of its own if a fight is
  too hard on your world.
- **Battleground keeps rarely change hands.** Bots storm the keep lord again and again and mostly fail; the first
  capture was Caer Caledon, taken by Albion on `43c43aa`.
- **A very busy server can stall for a few seconds**, most often as two raids end together (3.0 s on `9a173f4`; a fix is
  tests only).
- **Open-field regroups are passed between groups more than planned** in dense warbands (17.4% of regroups on `9a173f4`
  against a 15% bar), though never passed on twice.
- **Some gamebots wait a long time at frontier teleporters**, and some search for a camp for minutes.
- **Bots don't fall when they die**: the original client plays the lying-dead pose only for players.
- **Bot-led groups:** gamebots don't share your horse or boat, and the break at the end of a session ends the party.
- **Bot chat** still guesses from your words, and has been tested one line at a time more than as long conversations.
- **A maximized launcher** can ignore START SERVER, STOP SERVER or ENTER REALM when another window sits over those
  buttons. Use the launcher at its normal size.
- **Bot guilds stay in your world when you turn them off**; `ROLLBACK.cmd` doesn't remove saved data.
- **New client (0.37c):** no crafting and no Join Friend; APPRAISE stays hidden; no see-through Stealth.
- **Play with friends** has only been tried on a home network.
- The fix list for 1.2.1 and more: [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md). Every change: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

---

## 5. Upgrade notes

- **Have 1.1.2, 1.1.3 or 1.1.4:** the launcher's **Check for updates** finds 1.2, and START SERVER reads **GET UPDATE**:
  Install, Later or Skip this version. It installs only with the game and server closed, and only a package that matches
  the SHA-256 GitHub publishes.
- **Have 1.0, 1.1 or 1.1.1, or the Social Expansion on 0.37:** run Setup, or unzip
  `OfflineDAoC-LivingServer-1.2.zip` into your game folder and run `INSTALL.cmd` with the game and server closed.
- **New to Offline DAoC:** run `LivingServer-Setup-1.2.exe`. It installs Offline DAoC 0.37 and Living Server.
  Windows SmartScreen may say "Windows protected your PC" because the exe isn't code-signed: More info, then Run anyway.
- **On 0.36, 0.36b or 0.36c?** Setup updates the game to 0.37, keeping your saves, then adds Living Server (**tests only** for 1.2: not yet run on the owner's PC). On 0.36 with the Social Expansion, run `PREPARE-FOR-UPDATE.cmd` from this package first.
- **What stays:** your characters, bots, guilds, bot memory and settings.
- **What changes on an existing world, once:**
  - every Living Server feature still at its old default turns on; a switch you set stays as you set it;
  - raids come every four hours instead of two and a half, unless you chose your own interval;
  - Galladoria's Earthmenders stop healing each other, unless you set that yourself;
  - bots already in your world keep their class and build, and get the spec points a player of their level has;
  - a gamebot of the custom Hibernian class is kept with its items but no longer logs in;
  - buffbot copies saved by earlier 1.2 test builds are retired, with their items kept.
- **Still off after the update:** diagnostics, the classic rule options, and the epic encounter options listed above.
- **To undo:** `ROLLBACK.cmd` puts every replaced file back. It never puts your world back, because that would undo your
  play since. Your daily backups are in `runtime\data\backups`.

Please report anything odd on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
`FORK_BUILD` line from `runtime\server\logs\server.log`.

### Coming next

- **1.2.1:** fixes from the list in [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md).
- **1.2.2:** a Linux build of the server, the launcher and the new client.
- **2.0, the complete realm:** live events, the guild package, bots with alts, friends and grudges, and a server with a
  past.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.2.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.2.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Offline DAoC with Living Server is maintained by [chris4aker](https://github.com/chris4aker) and coded with Claude
(Anthropic). Offline DAoC was created by shadowofze; the server underneath is [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core).
Some code and ideas are borrowed from the [True 1.65 project](https://github.com/zomboden/OfflineDAoC-True165) (zomboden/OfflineDAoC-True165), and fixes from
other Offline DAoC forks, all credited in the README. Boss lore checked against the Camelot Herald archive.
Reports from the community on GitHub and r/daoc. GPL-3.0.

Living Server is a free, non-commercial, unofficial fan project, not affiliated with Electronic Arts or Broadsword.
