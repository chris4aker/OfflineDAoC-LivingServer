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
| Server firsts | `bot_living_firsts` | True (since 1.2) |
| Rezzes and corpse runs | `bot_living_death` | True (since 1.2) |
| Frontier alarms | `bot_living_alarms` | True (since 1.2) |
| Nemeses | `bot_living_nemesis` | True (since 1.2) |
| Bot schedule | `bot_schedule_mode` | `clock` since 1.2 (also `off`, `session`) |
| Breaks and linkdeads | `bot_living_breaks` | True (since 1.2) |
| Body language | `bot_living_body_language` | True (since 1.2) |
| Beggars | `bot_living_beggars` | True |
| Deeds and reputation | `bot_living_deeds` | True |
| Storylines | `bot_living_storylines` | True |
| Spammers and roleplayers | `bot_living_characters` | True |
| True rumours | `bot_living_rumours` | True |
| Holiday chatter | `bot_living_holidays` | True (Beta 1.1) |
| Leeroy in raids | `bot_living_leeroy` | True (Beta 1.1) |
| Bots remember each other | `bot_group_bonds` | True (server key only; not in the launcher tab yet) |
| Bots dress up | `bot_cosmetics`, `bot_cosmetics_price_percent` | True, 100 = the game's prices (1.2; server keys only, not in the launcher tab yet) |
| Personal milestones (Bots as people) | `bot_living_milestones` | True (1.2; card **BOTS AS PEOPLE**; see below) |

Every part is on by default since 1.2 (owner 2026-11-16: "all our living server features should be in by default if
someone downloads our installer"). A world from an earlier build moves once, only where a row still holds the old
default the server wrote; a value set in the tab is kept. **ALL OFF** in the tab switches every part off, along with
the other Living Server features.

### Bots as people (1.2)

[BOTS-AS-PEOPLE-1.2.md](BOTS-AS-PEOPLE-1.2.md): skill that varies and improves, knowing only what a player would know,
movement that doesn't look scripted, and personal milestones. The launcher's card **BOTS AS PEOPLE** has the four master
switches; the others are server keys (category `bots`). **All twelve are on by default** and new in 1.2 (no row in an
older world, so no upgrade line). A master switch turned off turns its sub-switches off with it. **ALL OFF** is the classic
server: it turns the four masters off with the rest, and `raid_learns_from_raiders` too (it has no master and no
checkbox; turning "Skill that grows" back on in the tab turns it back on). Work packages 1 to 6 and 2R are built.
**Everything here is tests only** until it is seen in the
owner's PC log ([PLAYED-CHECK-1.2.md](PLAYED-CHECK-1.2.md), "Bots as people").

Only autonomous gamebots are touched. Companions (hired ones too), real players, `/spawn` and `/raid` helpers, Epic
Special Forces and buffbots keep today's rules, and so do realm raids (apart from `raid_learns_from_raiders`), raid
dungeons, battleground keep assaults, fights at a keep, siege jobs, relic carriers and stable rides. One test,
`BotPeopleScope`, guards every rule. In a group with a real player, mistakes come at a quarter of the rate; way finding
and the movement habits don't happen there at all.

**One tier per bot.** Each gamebot is Casual, Average or Veteran (30/50/20, shifted by `rvr_bot_awareness`, the one
difficulty knob), rolled from its own id. Every package reads that one tier: its skill profile's tier, which can grow by
one tier with play, or the same start roll when `bot_skill_profiles` is off. In an RvR group the group's tier counts
(owner decision 5); a member's own facets only move its delays inside the group tier's band.

| Switch | Key | What it does (default True) |
|---|---|---|
| Skill profiles (master) | `bot_skill_profiles` | Each bot gets its tier and six facets (reactions, positioning, pulls, standing in fire, buffs, power) and makes the mistakes its tier gives it: a pulled add in 10/5/1.5% of party pulls (Casual/Average/Veteran), a buff left off in 25/12/4% of expiries, its dearest nuke in 30/15/5% of casts, resting at 25/35/45% power, a circle step into a monster's face in 12/6/2%, reactions x1.3/1.0/0.75. Caps: never a named, never a monster more than 2 levels above, never in a dungeon for pulls and buffs. In open-field RvR a backline member steps out of an area effect after 2-5/1-3/0.5-1.5 s, and a member follows its group's call after a delay in the group tier's band, moved by its reactions (a group of grown bots follows sooner, never outside the band) |
| Skill grows with play | `bot_skill_growth` | Fights won (the bot or a gamebot groupmate within 3,000), deaths, pulls, buffs and rests add experience; about 40 hours of fighting moves a bot from one band's bottom to the next. One tier at most, and it never falls (lowering `rvr_bot_awareness` can pull a grown bot down, owner 2026-11-17) |
| Fewer mistakes in your group | `bot_skill_spare_player_group` | In a group with a real player, mistakes come at a quarter of the rate and the rest floor moves most of the way back to today's 45%. Delays are not shortened |
| Raids learn from their raiders (no master; off with ALL OFF) | `raid_learns_from_raiders` | Xanxicar's stomp only: the raid's familiarity comes from the raiders who have seen it (full from 60% of them, a quarter with none), so a newcomer lapses more than the veteran beside him. An upgraded world is seeded once so its average stays its record's. Off: the record's familiarity, exactly as before |
| Place knowledge (master) | `bot_place_knowledge` | Each bot knows its capital, a home town, its realm's zones below its level and, from level 20, its border keep; never a dungeon, a frontier zone or another realm's place at the start. It learns a place when it arrives. Routes and camp choice are unchanged |
| Asking the way, tagging along | `bot_ask_the_way` | Going somewhere it doesn't know, a bot may ask in /say or /faction (one /faction ask per realm per 10 minutes, inside the chat budgets); only bots that know the place answer, and the asker then half knows it. Or it follows a realm-mate within 2 levels going there, until the zone line, and stops at any threat |
| Wrong roads | `bot_wrong_roads` | Sometimes (15/8/2% of unknown trips) a bot walks 600-1,500 units along another proven walking leg, looks around and walks back: at most 3 minutes, once a trip, never in a frontier, a dungeon, a group or below 80% health, and never toward a yellow or worse |
| Movement habits (master) | `bot_movement_habits` | Overshoot at town spots (25/15/8%: 30-60 units past, then a step back), and the bank crowd |
| Circle-strafing in RvR | `bot_move_strafe` | Melee bots circle-strafe their target (20/50/85% of fights, never into its front); casters and healers face a melee enemy and back off, healers along their usual kite (also needs `rvr_bot_field_tactics`) |
| /stick lines | `bot_move_stick_line` | A travelling bot group follows in a line at 80-200 units, with now and then a lost stick for 3-10 s |
| Sitting, and keep walls | `bot_move_sit_and_walls` | After 30-50 s of waiting, most waits sit (wait sites, rendezvous, the bank). At a keep the realm holds, with no siege and no enemy noticed, formation members sit on the guard archers' floors and stand within 1 s when an enemy comes within 2,500 |
| Personal milestones (master) | `bot_living_milestones` | One goal at a time, from the bot's real numbers: the ding to 50 from level 45; at 50 realm rank 5 (an RvR leaning: Daredevils, No-lifers, Risk 7+) or a dragon trophy (a raid leaning: Socialites, No-lifers, some Explorers, Daredevils and Levelers), an item the bot really received from the loot of a dragon, an epic boss or a raid's final boss, never a boss's add. A raid drop is remembered whatever the bot is chasing, so none ever says it won nothing after winning. No epic armour (owner decision 2). At about 50% and 90% of the way (the trophy: the 2nd and 5th big kill without a drop) a bot may say so (35-80% by chattiness), in its group with you, /say near you, its guild or /faction: at most one milestone line a realm in /faction every 5 minutes and one a minute elsewhere, under the channel's ceiling. Fighting when its line is due, it tries again after 15-30 s (at most 8 times); nobody hearing, it waits 10 minutes. Realm rank 5 and a trophy are told in /faction and the realm cheers (1-3 reactions), one moment a kill; the ding to 50 keeps its own ding line, story and grats. The Herald lists the last 40 under **Milestones of the Realm**. The small push: within 2 levels of 50, 40-60% of the town breaks; past halfway to realm rank 5, x1.5 (x2 past 90%) the wish for an RvR slot, inside the realm's exact goal mix |

**Log tags** (each event line is rate-limited; the summaries come every 5 minutes, one `BOT_PEOPLE_SUMMARY` line with the
switches, the rows saved and the scope's answers, then one line per package):

| Package | Per event | Every 5 minutes |
|---|---|---|
| All | | `BOT_PEOPLE_SUMMARY` |
| 1 Skill profiles | `BOT_SKILL_PROFILE` (at login), `BOT_SKILL_GROWTH` (a tier gained) | `BOT_SKILL_BUDGET` |
| 2 Skill in the hands | `BOT_SKILL_MISTAKE kind= spared=` | `BOT_SKILL_MISTAKES` (`rate_out=`, `rate_in=`) |
| 2R Raid learning | `RAID_LEARN raid= mechanic= record= seenShare= familiarity=`, `RAID_MECHANIC_SEEN how=raid\|seeded` | |
| 3 Place knowledge | `BOT_PLACE_LEARNED how=seeded\|visited\|told` | `BOT_PLACE_BUDGET` |
| 4 Way finding | `BOT_WAY_ASK`, `BOT_WAY_TOLD`, `BOT_WAY_FOLLOW`, `BOT_WAY_WRONG_ROAD cost_s=` | `BOT_WAY_SUMMARY` |
| 5 Movement habits | `BOT_MOVE_STRAFE kind=circle\|face`, `BOT_MOVE_STICK_LOST`, `BOT_MOVE_OVERSHOOT`, `BOT_MOVE_SIT`, `BOT_MOVE_WALL` | `BOT_MOVE_HABITS` |
| 6 Milestones | `BOT_MILESTONE_ARC state=active\|dropped`, `BOT_MILESTONE_PROGRESS point= talk=yes\|no\|later reason=in_combat\|nobody_hears\|channel_gap`, `BOT_MILESTONE_TALK channel= bubbles= left_k= count= line=`, `BOT_MILESTONE_REACHED arc= trophy= source= sought= announce=` | `BOT_MILESTONE_SUMMARY` |

AI_PROFILE times the packages as `people.skill`, `people.places`, `people.way_finding`, `people.movement` and
`people.milestones`. GM checks: `/lstest skill`, `/lstest lost`, `/lstest ask`,
`/lstest wall`, `/lstest milestone` (the target's passed talk point is said now; never one its numbers have not reached); every player: `/lswhy skill [name]`, `/lswhy way [name]`. The saved tables (`offline_bot_skill`,
`offline_bot_places`, `offline_bot_milestones`, `offline_bot_mechanics_seen`) are saved progress: a release ships them
empty.

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
- `off`: every bot stays online (the default before 1.2).
- `session`: each bot plays sessions of 1–4 hours, with 20 minutes to 2 hours away. Timing is
  counted from server start. About two-thirds of the roster is online at any time.
  - No-lifers play longer and are away less; Socialites play a little longer.
- `clock` (the default since 1.2): follows your PC's clock like a 2002 North American server.
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

### Bots dress up (`bot_cosmetics`)
Gamebots spend their coin on how they look, not only on gear. Each bot has its own style, the same every time you
see it: about a fifth never bother, most dye what they wear in one colour, some wear two colours and colour their weapon
with a weapon luster, and a few show-offs do every weapon. Their colours lean on their realm's
(Albion reds and blues, Midgard blues and greys, Hibernia greens and purples), and guild members often wear their
guild's colour.

They buy at a merchant during the town visit they already make between tasks, after selling and buying upgrades,
and only from coin above what they keep for gear and repairs. They buy the game's own dyes at the game's own prices:
cloth dye, leather dye or enamel to fit the piece, from 50 copper for a light shade to over 16 gold for royal and black
ones, and about 196 gold for a weapon luster. Tidy bots take the cheap shades, show-offs the dear ones. Bots buy no
weapon glows: as in 1.65, a glowing weapon comes from an epic boss, a dragon or a Darkness Falls seal merchant, so a
bot with one earned it. The Effect Master in the capital is still there for you. New gear comes in its plain colour, so
they pay to dye it again. Guild emblems on cloaks and shields stay. `bot_cosmetics_price_percent` scales the prices.
Your own gear and your companions are never touched.

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

### Raid wipes and the Epic Special Forces (always on)
A raid that wipes at a named encounter is not over: it regroups, rests and rebuffs for at least five minutes, and goes
in once more together when 70% of its roster is back (40% after 20 minutes). On the first wipe the realm's **Epic
Special Forces** answer: 24 geared level 50s at first; a win they took part in sends one party (8) fewer next time at
that encounter, down to 8 (they always answer a wipe), a win without them changes nothing, and every defeat or
time-out sends a party more, up to 24. Each raid's own
size follows the same record: it starts at the period's raid size there and shrinks after wins and grows after losses,
70-150% of it, aiming at two wins in three ([RAID-SIZES.md](RAID-SIZES.md)). They arrive at the raid's muster hub, walk in
like a late party, take no loot and leave when the raid ends. Two failed attempts and the raid is **Defeated** and goes
home. Dungeon raids have done this since 0.37-se.2; dragon raids do it since 2026-10-30 (Beta 1.1.1): an attempt at the
dragon ends when it is out of combat after the approach or has taken no damage for five minutes while a trickle keeps
it busy, and it was a wipe by the same half-the-raid-fallen or healed-back-to-95% rule (a raider who died and released
counts as fallen even though it is back on its feet, since dragon raiders release to a bind in the dragon's own
region). The regroup is at the staging ring around the lair, outside the dragon's aggro, the retry waits for the dragon
to land, and a ring nobody has reached for twenty minutes ends the raid as Defeated. Logs: `REALM_RAID_WIPE`,
`REALM_RAID_DRAGON_WIPE`, `REALM_RAID_DRAGON_REGROUP`, `REALM_RAID_DRAGON_ENGAGE`, `REALM_RAID_DRAGON_REGROUP_UNREACHABLE`,
`REALM_RAID_SPECIAL_FORCES_ARRIVED` / `_NOT_NEEDED`. The dragon part has automated tests (`UT_DragonRaidWipes`) and has
not been played in game yet.

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

### Era builds (`bot_era_class_shares`, `bot_era_builds`, `bot_era_offmeta_percent`, `rvr_roster_build_roles`)

Added for 1.2 (owner Chris, 2026-11-17; design and review in [ERA-BUILDS-1.2.md](ERA-BUILDS-1.2.md)). **Tests only**:
nothing here has been seen on the owner's PC yet. New gamebots are built the way players built their characters in
2002-2003 (patch 1.65):
- **The class mix** (`bot_era_class_shares`, on): the launcher's `+ Lv.1`, `+ Lv.20` and `+ Lv.50` batches draw each
  bot's class from its realm's 2003 share (Cleric 12% of Albion, Healer 15% of Midgard, Druid 12% of Hibernia, Reaver
  3%), not evenly.
- **A build for life** (`bot_era_builds`, on): at its first load a new gamebot draws one of its class's period builds by
  weight (a Mind/Body Sorcerer more often than a Matter one), seeded by its roster id, moves up to 2 points between its
  lower lines, and keeps that build for life, as it kept today's. A `+ Lv.20` or `+ Lv.50` bot arrives trained in it; a
  `+ Lv.1` bot trains it level by level at its trainer. Its weapons and its starter gear follow the build's weapon lines.
- **The odd spec** (`bot_era_offmeta_percent`, 10): one new gamebot in ten takes one of today's class-file builds
  instead. 0: era builds only. Which bots do is fixed by their roster ids.
- **Crowd control in RvR** (`rvr_roster_build_roles`, on): when a role-built eight fills its crowd-control slot, it first
  takes a member or bot with a crowd-control build (a Pacification Healer, a Mind Sorcerer, a Music Bard), then the
  classes as before. Bots without an era build rank as before.
- **Who keeps what:** a gamebot that already has a saved build keeps its class and build; nothing is re-drawn and no
  point is ever untrained. One from before lifetime builds that already trained lines takes the era build of its two
  highest lines, or today's build if none matches. Your `/spawn`, `/raid` and `/bot` companions, hired companions, Epic
  Special Forces and No Lifers' buffbots keep today's builds. Bots never play the custom class (the Sluaghbinder), in
  either edition.
- **Level 41-50 points:** since 1.2 a gamebot that trains every level gets the half-level spec points above 40 that a
  player gets at each "x.5", so it reaches 50 with the same points as a `+ Lv.50` bot. Points it missed before 1.2 are
  not given back (its trained levels don't change on the update).
- **Every bot has a player's points** (owner, 2026-11-17: "companions and bots should be the same as a player"). A hired
  companion's spec window counts its points as yours are counted: every level, every half level above 40 it has
  passed, and the current one once its bar reaches the half, less what its lines cost you. A companion saved before
  1.2 keeps every trained line and gets its missing half-level points to spend (up to 220/330/445 at level 50 for
  x1.0/x1.5/x2.0 classes); its free respec gives back the same total. `/spawn` helpers promoted from 49 to 50 now get
  the 49.5 points too. A gamebot gets each "x.5" point with its next level's training, so it matches a player at every
  whole level. Tests only until seen on the owner's PC.
- **Autotrain, as yours.** Eleven classes have autotrain lines (Armsman and Mercenary: Slash, Thrust; Paladin: Slash,
  Chants; Reaver: Slash, Flexible; Scout: Longbows; Minstrel: Instruments; Warrior: Sword, Axe, Hammer; Infiltrator,
  Shadowblade and Nightshade: Stealth; Ranger: Recurve Bow; this server's Hunter has none). You pay nothing for those
  lines up to a quarter of your level, whenever you train them: 77 points a line at 50. Every bot now pays the same.
  - This server's rule is the later "autotrain refund" one (its code names patch 1.87): nothing rises by itself and
    training a line early loses nothing. In 1.65 itself only a line left untrained got the free levels; the most a 1.65
    player could have is what every player and bot has here.
  - A hired companion's free levels show "next: 0 points"; one saved before gets the points back to spend (77 a line at
    50), and its respec leaves autotrain lines at a quarter of its level, as yours does.
  - A gamebot gets nothing back for levels it already trained; from its next level on it pays a player's cost.
  - Era builds: a build fits when a level 50 player can pay for it. Shadowblade Axe/Left Axe, Shadowblade Axe/CS and
    Ranger Archery/Pathfinding now fit as posted (no longer trimmed); Minstrel Instruments/Stealth/Slash keeps Stealth 33
    (was 31) and Minstrel Instruments/Slash keeps Slash 42 (was 40); 9 builds are still trimmed. Bots that already
    have a saved build keep it.
  - Tests only until seen on the owner's PC.
- **Every number is an estimate** from 2002-2003 forum posts (European servers); none is a count. The data file says so
  for each share and weight, and a later data update can re-weight them.
- **Log:** `ERA_BUILDS_LOADED` at start; `ERA_BUILD_ASSIGNED` when a bot's plan is first saved (`draw=era`, `offmeta`,
  `nearest` or `legacy`, with the jitter); `ERA_BUILD_TRAINED` once when an era-built bot first stands trained at 50
  (`match=yes`, or `match=no short=...`); `ERA_BUILD_CENSUS` once per start and realm: the bots by plan (`era`, `kept`,
  `new` = not loaded yet) and by class against the target share (`Healer=139(15.4%/15%)`; Hibernia always lists
  `Sluaghbinder`), then the same by class over the era-built bots only (`eraClasses="Healer=12(16.0%/15%),..."`). `RVR_PARTY_FORMED` names the crowd controller's build role (`cc=Healer(healer-cc)`). None of these
  is behind a diagnostics switch; ASSIGNED and TRAINED are at most one line per bot for life.
- **Switches:** `bot_era_class_shares` and `bot_era_builds` are in the launcher's Living Server tab (economy card,
  "2003 class mix" and "2003 builds"); the other two are server keys only (category `bots`). **ALL OFF** switches the
  two tab switches off with the other Living Server features: new bots then come evenly and with today's builds, and
  bots that already have a build keep it.

### RvR perception (`rvr_bot_perception`, `rvr_bot_awareness`, `rvr_bot_spotters`, `rvr_bot_perception_bots`, `rvr_bot_group_lookers`, `rvr_group_pet_cap`)

Added for 1.2 (owner Chris, 2026-11-01) after a player's report on r/daoc: gamebots in the frontier saw you from
5,000 units, instantly and all at once, whenever you were in their line of sight. Now they notice you the way players
did in 2003. The observer cap, the calibration, duty and the pet caps follow the North Star audit of 2026-11-03
(challenge 2 and priority 7; `docs/audits/2026-11-03/technical.md` §8.2 on the audit branch). The design notes are in
[BOT-BUILDS-AND-RVR.md](BOT-BUILDS-AND-RVR.md) (from the `plan/rvr-builds` branch, PR #52).

- **Who it changes:** autonomous gamebots in the frontier, the battlegrounds and Darkness Falls, when they pick an enemy
  player, a player's companion or a player's pet. Since 1.2 work package 1 (`rvr_bot_perception_bots`, below) enemy
  gamebots and their pets are noticed the same way. Siege engines, keep guards, doors and monsters are acquired as
  before, and so are the targets of bots at a keep they hold or besiege. A bot that is hit, or whose realm-mates near
  it or group are attacked, fights back at once (`bot_incoming_fire_reaction` handles that delay). Your own companions
  and the gamebots in your group never scan for enemies this way and never hear these calls.
- **At a keep means near it (1.2):** a bot counts as at its keep only within 9,500 units of it (the ring the siege
  camps stand in, which the siege code already counts as "there"), or while it works a siege engine. Until 1.2 a whole
  army counted as at the keep from the moment its warband chose to attack or defend it, so a column on the road saw
  everything at once. Now a marching column, attackers and defenders alike, notices like everyone else until it is
  9,500 units from the keep. In the lane survey's 165 main routes that is most of each march (about 85% of the
  distance; 70% for the short keep-to-keep hops).
- **Noticing:** within 1,000 units a bot always notices you; beyond 3,500 it never notices on its own. In between it
  is a chance on each scan, set so that one bot looking on its own needs this long (the median) for a plain sighting
  (you alone, moving, not fighting or casting, not in front of it):

  | Distance | easy | normal | veteran |
  |---|---|---|---|
  | 1,500 | 3 s | 1.5 s | 0.75 s |
  | 2,500 | 12 s | 6 s | 3 s |
  | 3,400 | never | 20 s | 10 s |

  The normal column is the audit's target. Between the rows the time grows evenly on a log scale. The chance per scan
  is worked out from the real time since the bot's last scan, so the times hold if bots scan more slowly under load.
  Fighting halves the time, casting and a group of 3 or more divide it by 1.5, being in the bot's 120° view by 1.75,
  and standing still out of combat doubles it. Line of sight and stealth still apply first.
- **The observer cap:** at most 3 bots of a realm (easy 2, veteran 5) look for the same enemy group in each second
  (until 1.2: the same enemy). The others don't roll at all: they learn of you only from a call. Bots within 1,000 units
  don't need a slot. A bot looks at a group once per scan, for the member it is most likely to notice, and noticing
  one member notices that member's group mates within 1,500 units of it; a mate who walks up later, near one already
  noticed, is noticed then. In the audit's spread-out crowd (50 bots in groups of 8 between 1,000 and 3,500 units)
  about a third have engaged after 5 s and three fifths after
  10 s (two lookers per group: 33% and 59%; every bot looking, as before 1.2: 35% and 62%), against 90% after 5 s
  without the cap; someone still notices you within 5 s 96% of the time (94% before).
- **Lookers (`rvr_bot_group_lookers`, 1.2):** away from keeps, two members of each gamebot group look for enemies,
  you included, and the pair changes every 10 seconds; a soloer or a pair looks for itself. The others run no scan
  until something wakes them: a mate calls an enemy, a group mate is fighting, or the group's plan has a target. The
  table above is one bot looking on its own, so it holds for each looker and each soloer; a group of 8 now first
  notices you in about half the table's time (two lookers), where
  every member looking under the cap took about a quarter (normal: 0.8 s at 1,500, 2.9 s at 2,500 and 10.3 s at 3,400,
  against 0.2, 1.4 and 5.5 s).
- **How far (1.2):** away from keeps a bot looks only as far as it could notice anyone: 3,500 units on normal (easy
  2,600, veteran 4,500), not 5,000. That is about half the area. Bots at a keep, raids and battleground assaults still
  look 5,000.
- **Stealth (1.2, part of `rvr_bot_perception_bots`):** until 1.2 a bot never saw a stealthed enemy until it struck.
  Now a bot may notice one, player or bot, by chance on each scan, never better than a player in its place: only
  within the range the server gives a player to see that stealther ((level - stealth spec) x 20 + 125, wider with
  Detect Hidden or See Hidden, never through Vanish), and never beyond 350 units. Within 125 units the chance rises to
  about 50% a second at point blank (veteran 60%, easy 35%); from 125 it falls to nothing at 350 or at the player's
  range, whichever is nearer. A bot fighting someone else never notices, line of sight is needed, and its mates are
  not told (they cannot see it). These numbers are a first cut, not from a source.
- **Reacting:** a bot that notices you engages 0.5-2 seconds later. Higher level bots and bold personalities
  (Daredevils, then high Risk) are faster.
- **Spotters (`rvr_bot_spotters`):** the bot that notices you tells its group mates within 2,500 units, and each of them
  engages after its own reaction delay. Each other group of its warband (the groups marching to the same keep or
  relic) hears it relayed half the time, as a whole group, 4 seconds later. With bot voices on and you in that group,
  it calls it in /group ("inc 3 north"), at most every 12 seconds. Warbands have no chat channel yet, so relayed calls
  are only in the log.
- **Duty:** a gamebot told by a call leaves what it is doing only if its duty allows it (`/lswhy duty`): a free bot, a
  bot marching with an RvR event and a bot in a gamebot group answer; a bot on a realm raid, in a battleground assault
  or in a player's group does not. It still notices you itself, and fights what it sees.
- **Difficulty (`rvr_bot_awareness`):** `easy` (always within 700, never beyond 2,500, the easy column, 30% slower
  reactions, calls heard within 2,000), `normal` or `veteran` (always within 1,300, never beyond 4,500, the veteran
  column, 30% faster reactions, calls heard within 3,000).
- **Pet caps (`rvr_group_pet_cap`, 2):** when gamebots form an RvR group (a roaming warband of 2-8 or a siege party),
  at most 2 pet classes (Cabalist, Theurgist, Necromancer, Spiritmaster, Bonedancer, Enchanter, Animist,
  Sluaghbinder) join a group of five or more and 1 a group of four or fewer; the others wait for the next group.
  Druids and Hunters don't count. Groups you lead or join are never changed. 0 turns the cap off. Which classes
  gamebots play is unchanged; a cap across a whole warband is not done yet.
- **Settings:** `rvr_bot_perception` on, `rvr_bot_awareness` normal, `rvr_bot_spotters` on and `rvr_group_pet_cap` 2 by
  default. With `rvr_bot_perception` off bots see as before. `rvr_bot_perception_bots` and `rvr_bot_group_lookers`
  are on by default and need `rvr_bot_perception` and the master switch `rvr_bot_field_tactics`; they apply only to
  gamebots away from keeps and not on a raid, in a battleground assault or in a group with a player. With
  `rvr_bot_perception_bots` off enemy gamebots are acquired at once again, up to 5,000 units, and stealthers are never
  seen; with `rvr_bot_group_lookers` off every bot looks.
- **Log:** `RVR_NOTICE` for each sighting (bot, target, `kind` = Player, PlayerCompanion, PlayerPet, EnemyBot or
  EnemyBotPet, `party` (solo or the enemy group), `partyNoticed` (members noticed with it), distance, the factors
  (Party for a mate noticed with another, Stealth for a stealther, with its `stealthLimit`), the chance,
  `time_since_first_los_ms`, the delay), one line per group noticed, and `RVR_SPOT` for each call (`kind`, `noticed`,
  `told` in the group, `relayed` to other groups, `held` by their duty, and where it was said). Once a minute
  `RVR_SCAN_BUDGET` per realm: scans, which bots scanned (lookers, solo, woken, everyone), idle non-lookers, notice
  rolls, rolls refused by the cap, rays, notices and stealth rolls. The medians in the table can be checked against
  `time_since_first_los_ms`.
- **Cost:** the frontier scan still casts at most 8 line-of-sight rays per scan (`VisibilityBudget`), and now casts
  none at a target a bot couldn't notice on that scan (beyond the outer range, or over the observer cap for a group it
  is not already watching). In a test with 900 gamebots of one realm around a player, rays at the player fell from 99
  to 11 a second; with 2,700 bots of three realms in one zone the scan casts about 18,700 rays a second, as before
  (`UT_RvrPerceptionCalibration`). With 1.2 work package 1, in the scene the RvR design assumed (300 gamebots per
  realm, 80% in groups of five, nobody fighting, 10 s from a cold start) scans fell from 782 to 754 a second and rays
  from 6,262 to 5,797 in a 24,000-unit square, and from 782 to 685 scans and 5,875 to 4,054 rays in a 48,000-unit
  square. That is an upper bound: in the test nobody fights, so the bots woken by a call keep scanning; the design's
  estimate of about 416 scans and 2,000 rays a second is for a quiet field. Not measured in game.
- **Owner's decisions (confirmed by the owner, 2026-11-05):** easy is twice the normal times and veteran half; bots
  within 1,000 units are outside the observer cap; gamebot-group members answer calls; other groups of a warband hear
  a call half the time, 4 seconds later; the pet-class list above (Druids and Hunters out, the Sluaghbinder in) with a
  cap of 2; a median of 0.75 s just outside 1,000 units (normal).
- **Checked:** automated tests only (`UT_RvrPerception`, `UT_RvrPerceptionCalibration`, `UT_RvrPerceptionDuty`,
  `UT_RvrPetCap`; for 1.2 work package 1 `UT_RvrPerceptionBots`). Noticing was seen on the owner's PC on build
  `3d67b6f` (2026-11-15: bots noticed the owner's character at 516-1,575 units, by chance and after a delay). The
  spotter call (`RVR_SPOT`) and the pet cap have not been seen yet. The 1.2 work package 1 parts (enemy gamebots and
  their groups, lookers, the radius, "at a keep" by position, stealth, `RVR_SCAN_BUDGET`) are tests only: not seen in a
  log on the owner's PC yet. The design is [RVR-FIELD-1.2.md](RVR-FIELD-1.2.md).

### Playing with friends: bots and companions (`companion_budget_total`, `companion_budget_per_player`)

Added for 1.2 (owner Chris, 2026-11-08: 1-4 friends in your bot world). How bots behave with more than one real player
online; joining a friend's server is the hosting half. With one player online nothing here changes.
- **Who a bot follows (`BotHumanBinding`):** a companion (`/spawn`, `/raid`, a saved `/bot` companion) follows its owner,
  always: no other player's `/pull`, attack, `/bot pilot` or invite takes it. A gamebot invited into a player's group
  follows the group's leader: `/makeleader` hands the gamebots to the new leader, and when the leader leaves they follow
  the new leader (another player only when no player leads). A player who leaves a group another player stays in takes
  their companions along (a `/spawn` companion vanishes, as it does whenever it leaves its group).
- **`/pull` and attacks:** a player's `/pull` orders their own companions, plus the gamebots when they lead the group.
  The leader's tank-gated pull holds only the leader's bots; a friend's attack or `/pull` meanwhile sends the friend's own
  companions in and leaves the leader's tank gate alone. A friend with no companions near is told who the gamebots follow.
- **Host-PC only:** `/travel` (it holds the run key in the game window) and `/bot pilot` (it reads the quickbars the game
  saved in `%APPDATA%`) work only for the game on the PC that runs the server. A friend on another PC is told so and
  nothing starts.
- **Companion budget:** while more than one player is online, a new companion needs room in the server's total
  (`companion_budget_total`, 160) and in the player's own share (`companion_budget_per_player`, 79, a full `/raid 80`).
  0 turns either off. Companions already out stay out; only new ones are refused, with the numbers. Each companion is a
  bot thinking at the player-led cadence (a `/raid 80` is about 260 decisions a second), so four full raids would cost
  more than the rest of the world.
- **Chat:** the bots' reply cooldowns in `/faction` and `/say` are per player, so one player's line no longer stops the
  bots answering another for 10-12 seconds. The pause in unprompted `/faction` lines after anyone speaks stays realm-wide
  (everyone of the realm hears that channel).
- **Quest credit:** a gamebot's kill credits every player of its group within range once, as a player's kill does; when
  the leader is dead or away, another player of the group in range is credited instead of nobody.
- **Bot leaders' invites:** each player has at most one invite box waiting, and a gamebot leader whose box waits for one
  player's answer invites nobody else meanwhile.
- **Checked:** automated tests only (`UT_PlayWithFriends`). It has not been played with two people yet.

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
| `/lstest leeroy now` | A raid with a Leeroy that is gathering for its final boss charges at once (once, within 10 minutes) |
| `/lstest raid [dragon] [alb, mid, hib]` | A realm's automatic raid (yours unless named) opens now, sized to its free level 50s and its record (at least 40 before sizing; raids from the launcher need 300) |

## Why a bot said that: /lswhy (every player)

`/lswhy` shows the last chat decisions of your target, or of the last gamebot that spoke to or near you. Each one gives
the channel, its reason code (`exempt-named`, `hold-reply-target`, `below-minimum`...), what set it off, the rule and score
it used, and why, or why it stayed quiet. `/lswhy <name>` shows one bot, `/lswhy last [n]` the realm's last decisions,
`/lswhy me` why bots answered your own lines or didn't, `/lswhy held [n]` the replies the reply budget held back, and
`/lswhy budget` how busy each channel around you is against its floor, target and ceiling, with /faction's and /say's
replies counted apart. A tell to a bot, or a line naming one, is always answered. How bots decide how much to say: [bot-voices/CHAT-BUDGETS.md](bot-voices/CHAT-BUDGETS.md).

## Tests

Automated (no running world):
- **Server, `UT_AutonomousLivingServer`:**
  - every part is on by default (since 1.2), and the schedule is clock;
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
