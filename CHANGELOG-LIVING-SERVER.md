# Living Server changelog

What this fork adds to shadowofze's Offline DAoC. The base game's own changes are in
[CHANGELOG.md](CHANGELOG.md). The newest version is first.

Up to 0.37-se.2 the fork was called the **Social Expansion**, and "Living Server" was the name of one
of its feature groups (bots remember you, grats, rezzes, alarms, nemeses, schedules, breaks, body
language). From Beta 1.0 the whole add-on is the **Living Server** and that group is **Server life**.
Older entries below keep the names they were written with.

## 1.2 (in development)

**Dungeons and bosses have their lore.** Living Server 1.2 gives every dungeon, epic dungeon, dragon and named
boss its own lore (written in `data/lore`, one file per place and per boss; see [docs/LORE.md](docs/LORE.md)), and this is
the part that shows it to you. All the stories are now in: 107 places and 578 bosses (with 4 realm histories), written for
Albion, Midgard, Hibernia and the neutral places, from the dragons and epic dungeons down to the named spawns of every zone.
Where Mythic told a story it is kept (in our own words, with its source); where it did not, the Living Server wrote one. The
lore's own characters no longer share a name with a trainer, merchant or banker of their realm. The lore is checked by
automated tests; nobody has read it in game yet.

- **Atmosphere lines.** Short lines in the middle of the screen, like the ones for a keep taken or a relic:
  - the first time each day you walk into a dungeon or epic zone ("Old words are carved here. The ogres cannot read them.");
  - stepping into a boss's room or lair (once a session);
  - a boss changing phase (at most two a fight: Scurceol Hyrde's last light going out);
  - a dragon taking to the sky, in gold for everyone in its zone and smaller for its realm elsewhere (at most once an hour,
    beside the dragon's own bellow in chat);
  - a realm's first-ever kill of a boss, to the whole world, with a line in your chat naming the realm. It is remembered
    across restarts, once per realm per boss;
  - a raid forming, a line about the place for that realm only;
  - Darkness Falls changing hands is now a gold line in the middle of the screen as well (at most once every 15 minutes;
    it follows the RvR announcements switch).

  At most one atmosphere line every 30 seconds, never more than four lines on screen at once, and realm news (keeps, relics,
  raid results, sieges) always comes first: an atmosphere line waits 10 seconds after it. Only you see them; bots never get
  them. A new switch in the launcher's Living Server tab, **Atmosphere lines** (on by default), turns them off.
- **Bosses speak their lore.** A boss with a lore file yells its own lines when the fight starts, at its phases, when it
  kills someone and when it dies. A boss whose script already speaks at that moment keeps its own words, and nothing is said
  twice.
- **Bots know the stories.** As a raid leaves its muster, its leader tells the realm in /faction what the boss does
  ("storm keeper next. four lights in the corners, kill the lights first"). A minute or two after a raid, a veteran tells
  how it went and then something about the place. Ask a bot "whats in krondon?" or "who is the storm keeper?" and it answers
  from the lore. The lore and rumour lines join the bots' lore talk, checked like every other bot line.
- **Fixes.** Gjalpinulva's lair is in Malmohus, not Myrkwood (the encounter catalog said Myrkwood). Bots no longer say Aidon
  the Archwizard waits in Summoner's Hall: he is in the Hall of the Corrupt.
- GMs: `/lstest lore` says which lore place you are in and lets its lines show again.

**The lore reaches you beyond bot chat** (Living Server 1.2: every dungeon, dragon and boss gets a story). Built and
covered by automated tests; none of it has been seen in game yet.
- **A loremaster in each capital:** Loremaster Aldwin in Camelot's library, Loremaster Sigrun at Jordheim's temple of the
  gods and Loremaster Eithne by the bards of Tir na Nog. Ask them of the [places] of your realm and the places no realm
  holds. Every place and every boss has its own words, and the links lead from one to the next.
- **Lore books:** one book per place, which you read with "use", a page at a time. The loremasters copy them out for a
  silver, and now and then a book turns up on a monster near the place it tells of. It's rare, never one you already
  carry, and they never crowd out real loot. The delve says what each book is about. A long book (more than 16 pages)
  comes in volumes: "..., Volume II", a silver each, sold together; the last page of a volume names the next. Nothing
  is left out.
- **Long tales in parts:** the loremaster tells a long story's summary first, then offers its chapters as [names] to
  ask for one at a time. Each answer comes in short pieces that never break a word and always fit the game's message
  limit (a message over 2,048 bytes would be lost whole). Bots keep to their own short lines and never recite chapters.
- **The Herald tells the tale:** each raid in the launcher's Realm Records, and in the Camelot Herald, ends with a sentence
  from its boss's story: who led it, how long it took, won or lost.
- **The Bestiary:** a new page under Realm Records lists every boss of your realm's places and the neutral ones, with
  what the raid leaders say. A boss's full story, your realm's first kill and its raid record open once your realm has
  brought it down. Raids won before 1.2 count. The Camelot Herald gains a "Bestiary of the Realms".
- **/lore in either client** shows your realm's places and bosses and what your realm has slain. In the new client the
  journal's LORE button opens it in its own window, where every name is a link.

**Battleground keeps change hands** (playtest 2026-11-04: in Thidranki 210 level-22 bots fought for 45 minutes, the
gates fell, and the Renegade Lord never dropped below 100%).
- **Before:** once the gates were down, every gamebot walked up to the lord on its own, from wherever its last fight or
  its release had left it. They reached him one at a time and died to him one at a time.
- **Now:** each realm rallies just inside the broken gate, under the lord's room. Its gamebots fight whatever comes at
  them there, and go up to the lord together once there are enough of them.
  - **"Enough"** is worked out from the lord's real level and health, the guards standing with him, and the levels and
    health of the bots at the rally, with no extra safety margin: the realm tries often and accepts that some attempts
    fail. In Thidranki that is about 12 level-22s against the lord alone, and about 24 with three guards beside him. A
    lord already down to half health needs fewer.
  - **You count double.** A real player standing at the rally counts as two gamebots, so you and ten bots go up where
    twelve bots would. The server log's `BG_RALLY_STORM` line shows how many players went up (`players=`).
  - **No endless waiting:** after 3 minutes the rally goes up with half of what it needs. After 6 minutes it goes up
    with whoever is there, never fewer than 8.
  - **A failed attempt** ends like a raid attempt: half the stormers fell, the lord healed back to 95%, or he lost
    nothing for 3 minutes. The survivors then rally again.
  - **The dead rejoin the rally.** A bot that died comes back to the rally instead of running up to the lord alone.
    Bots that arrive while an attempt is running go up in groups of at least 4.
  - **Only the bots changed.** The lord, his guards, their respawns and the gates are exactly as the base game has them.
- **Smaller fixes:**
  - A gamebot that has left the lord (it died, or was chased out) comes back in through a gate again. Before, once it
    had passed a gate it walked straight at the lord for the rest of the tour.
  - Gamebots roaming the field around an enemy-held keep stay outside its walls. Before, they were sent to points in
    its courtyard and on its walls with the gates shut, and gave up their route ("route recovery" in the log).
  - A battleground fight is logged under the battleground goal. Before, the server log showed "Find a reachable
    level-appropriate XP camp" for almost every battleground death.
- **For testers:** the server log's `BG_KEEP_STATUS` line now shows each realm's rally (`rally="Albion:rallying 9/14"`),
  and new lines say when a rally goes up (`BG_RALLY_STORM`), when late arrivals join it (`BG_RALLY_REINFORCE`) and
  why an attempt ended (`BG_RALLY_REGROUP`).

**Who has a gamebot's time, in one place** (groundwork for gamebots that lead groups and invite you).
- Raids, sieges and keep defenses, battleground rallies, gamebot groups and your own group now each put a claim on a
  gamebot's time, with a priority. Your group is the strongest claim: no raid, siege, RvR march or rally ever takes a
  gamebot from your group, exactly as before. What gamebots do has not changed, with one exception: an RvR march and
  a battleground rally now rank the same, so neither pulls a gamebot away from the other (before, a march could take a
  gamebot out of its battleground rally).
- **`/lswhy duty`** on a targeted gamebot (or `/lswhy duty <name>`) says what has its time now ("realm raid
  (Gjalpinulva), since 4m", "in a player's group", "free") and its last claims: who asked for it, and whether it went
  or why not. Plain `/lswhy` on a gamebot shows the same under its chat decisions.
- **One fix:** a gamebot recruited for a raid that logged off before its party formed stayed reserved when it came
  back, and sat out every raid, siege party and gamebot group until that raid ended. It now comes back free.
- **For testers:** `DUTY_LEASE` lines in the server log record each claim granted, taken, refused (with its reason)
  and ended, once per change.

**Gamebots in RvR notice you like players, not radar** (a player's report on r/daoc; owner 2026-11-01; North Star
audit 2026-11-03).
- **Before:** in the frontier, the battlegrounds and Darkness Falls, every gamebot with line of sight saw you from 5,000
  units at once, and they all came together.
- **Now:** a gamebot notices you by chance. Within 1,000 units it always does; beyond 3,500 it never does on its own.
  A gamebot looking on its own takes about 1.5 s to notice you at 1,500 units, 6 s at 2,500 and 20 s at 3,400 (the
  median). Fighting, casting, being in a group of 3 or more or standing in front of it makes that quicker; standing
  still makes it slower. Stealth works as before: a stealthed player is never seen this way.
  - **Only a few look at once:** at most 3 gamebots of a realm look for you each second. The rest learn of you only
    when one of them calls it. In a crowd of 50 spread out in groups, about a third have engaged you after 5 seconds and
    the rest drip in, where before nine in ten came within 5 seconds.
  - **They react, then call:** a gamebot that notices you engages 0.5-2 seconds later and tells its group. Other groups
    marching to the same keep hear it half the time, 4 seconds later. With bot voices on and you in that group, it
    says "inc 3 north" in /group.
  - **Duty first:** a gamebot on a raid, in a battleground assault or in your group doesn't leave it to answer a call.
  - **Your bots are not affected:** your companions and the gamebots in your group follow and fight as before.
- **Fewer pet classes in one group:** when gamebots form an RvR group or a siege party, at most 2 pet classes
  (Cabalist, Theurgist, Necromancer, Spiritmaster, Bonedancer, Enchanter, Animist, Sluaghbinder) join a group of five
  or more, and 1 a smaller group. Groups you lead or join are never changed.
- **Settings:** `rvr_bot_perception` (on), `rvr_bot_awareness` (easy, normal or veteran; normal), `rvr_bot_spotters`
  (on) and `rvr_group_pet_cap` (2; 0 turns it off). See [docs/LIVING-SERVER.md](docs/LIVING-SERVER.md).
- **For testers:** `RVR_NOTICE` lines now show the chance and `time_since_first_los_ms`; `RVR_SPOT` lines show how many
  mates were told, relayed and held by their duty.
- **Checked:** automated tests only, including seeded simulations of the medians and of crowds. Not yet played.

**Gamebot leaders invite you, and you can just follow** (bot-led groups, first part; docs/BOT-LED-GROUPS.md).
- **The invite:** the leader of a gamebot party of your realm, within 3 levels of you on average, with room and
  hunting where you can walk or ride, may send you a tell ("lf 1 more for skeletons in Cursed Forest, want in?") and
  then the usual group invite box. Yes puts you in its group; the gamebot stays the leader.
  - **Never** while you fight, are dead, ride, are on a boat, have another yes/no box open, trade, are in a group, have
    `/spawn` companions out, are `/afk`, or are in a frontier, battleground or raid.
  - **How often:** unasked, at most once every 45 minutes and not in your first 10 minutes online. It counts as that
    period's bot tell, so a tell and an invite never stack.
  - **Asking:** `/lfg`, or a tell to a bot leader with room ("lfg?", "room for a cleric?"): when a party fits, its
    leader answers by tell and the invite box follows within 15 seconds (and then no duo offers come). An asked invite
    skips the 10-minute start, the 45-minute gap and the quiet after a no.
  - **Saying no:** No in the box, or "no thanks" to the tell. Then no unasked invite for 30 minutes, nothing from that
    leader for 6 hours, and no unasked invites for the rest of the session after three noes.
  - The server setting `bot_group_invite_player` (the launcher's Living Server tab, **Bot leaders invite you**) turns
    unasked invites off; `/lfg` still gets you one.
- **Following:** the leader runs the party as a gamebot party does: the camp, the pulls, the breaks.
  - The camp is for the group's level counting you, and never more than 6 levels above you.
  - The leader tells you where to meet and waits there up to 20 minutes (it says where it is at 5 and 10), then goes
    on and keeps your spot. You travel yourself: you are never teleported, summoned or given money. Gamebots beside
    you when you take a porter come with you, as invited gamebots do.
  - On the way the leader stops when you fall 1,500 units behind, asks "where u at?" after 2 minutes, and after 10
    minutes with you not getting closer goes on to the camp and says so in `/group`. Inside a dungeon entrance the
    party waits for you the same way.
  - The party pulls only when you are near, alive, not sitting and up on health and power like the rest.
  - **Away:** `/afk`, or 10 minutes without doing anything: the party pulls where it stands but goes nowhere new; at 15
    minutes it holds; at 30 it lets you go at the next break, with a tell. Linkdead for 5 minutes counts as leaving.
  - **Leaving:** `/disband` at any time. The party carries on without you.
  - A gamebot party hosting you counts your class: when you are its only healer, it still has a healer.
  - `/pull` in a party a gamebot leads says the gamebot is leading instead of taking over. Taking the lead
    (`/lead`) comes in the next part.
  - Quest kills: a kill by the party counts for your kill quests when you are alive and in range, as in a group you
    lead.
  - No raid, siege, RvR march, battleground rally or other gamebot group takes a gamebot from a party that hosts you
    (`/lswhy duty` on one says "leading a group with a player").
- **Loot changes for every group with you and gamebots, whoever leads** (owner's decision): you get the primary
  spoils, and the gamebots still gear up and fund their own improvements. Each item, in this order:
  - an item your class can use that beats what you wear goes to you;
  - otherwise, an item that is an upgrade for a gamebot nearby goes to the gamebot it improves most (gamebots start
    without armour and earn it, so they need drops);
  - everything else (most of the sell value) goes round the members in range with two turns for you: with seven
    gamebots nearby you get 2 of every 9 of these items.
  - Coin is split evenly among the members in range with you counted twice (2/9 with seven gamebots nearby).
  - Before, in a group you led you got everything, and in a gamebot's group you would have got nothing. Your share is
    the setting `bot_group_player_loot_weight` (1 to 8; the launcher's **Your loot share**). Groups of only gamebots,
    and your `/spawn` companions (who take no loot), are unchanged.
- **Gamebot chat:** the leader says what comes next in `/group`, short and lowercase ("moving to skeletons", "where u
  at?", "everyone med"). Lines like "you pull great" or "lead the way" are no longer said in a party a gamebot leads.
- **For testers:** server log lines `BOT_LED_INVITE` (sent, not sent, declined, accepted, with the reason),
  `BOT_LED_GROUP_HOSTING`, `BOT_LED_GROUP_GOING_ON`, `BOT_LED_PLAYER_LEFT` and `BOT_LED_LOOT` (who got each item and
  why: `upgrade-player`, `upgrade-bot` or `rotation`); the startup
  `FORK_BUILD` line shows both settings.
- **Not yet:** `/lead` and handing the lead back, the join setting (follow or lead) and raid leaders who invite you
  come in the next parts. Gamebots don't yet ride the same horse or boat as you: the leader takes its own and waits
  for you at the other end. Nothing here has been played in game yet.

**Take the lead, or hand it back** (bot-led groups, second part; docs/BOT-LED-GROUPS.md).
- **`/lead`** in a party a gamebot leads: the leader says "all yours", you become the group leader, and the gamebots
  follow you exactly as gamebots you `/invite` do (`/pull`, your camps, your pace). The group, its loot share and your
  quest credit carry on unchanged.
- **`/lead bot`** (or `/lead bot <name>`, or `/makeleader <gamebot>`, or `/makeleader` with a gamebot targeted) in a
  group you lead: a gamebot takes the lead (the one you name or target, else the highest level one), says it is looking
  for a camp, and picks one near where you stand; you follow as in a party that invited you. It needs at least two
  gamebots, a tank, a healer and a damage dealer (you count for your class), no `/spawn` companions and no fight going
  on, and only works where you can walk or ride (not in the frontier, a battleground or Darkness Falls). If something
  is missing you are told what.
- **`/pull`, `/invite` and `/makeleader`** in a party a gamebot leads now say "Brannoc is leading this group. /lead to
  take over." instead of "You are not the leader of your group".
- **When a party you follow is down to one gamebot**, you get a yes/no box: "Only Brannoc is left in your group. Do you
  want to lead the group?" Yes: you lead and Brannoc follows you. No, or no answer within a minute: Brannoc says
  goodbye and goes its own way. Before, the party just broke up. You never become the leader any other way unless you
  type `/lead`.
- **`/invites off`** stops unasked invites from gamebot leaders for this character (saved with it); `/invites on` turns
  them back on, `/invites` says which. `/lfg`, or a tell to a leader, still gets you one.
- **The setting `bot_led_group_join_mode`** (the launcher's Living Server tab, **When you join a bot's group**): with
  *follow* (the default) you follow the leader; with *lead*, saying yes makes you the leader at once.
- **A fix from B1:** a party following a bot leader whose only healer was you counted you for its roster but would not
  start a pull. It now counts you there too.
- **No gamebot is free for a moment during a handover:** it goes straight from your group to the bot-led group (or
  back), so no raid, siege or RvR march can take it in between. `/lswhy duty` on one shows "handed over".
- **For testers:** server log lines `BOT_LED_HANDOVER` (to_player, to_bot, offered, accepted, declined, refused, with the
  reason) and `AUTONOMOUS_GROUP_TASK_ENDED ... disbanded=false` for a party that was handed over; the startup
  `FORK_BUILD` line shows `bot_led_group_join_mode`.
- **Not yet:** the leader's "pulling", "last pull then town" and "lf 1 more" calls are written but not said yet. If you
  leave a group after `/lead`, its gamebots leave too (as with gamebots you invite) instead of carrying on as a party.
  Nothing here has been played in game yet.

**Raid leaders invite you** (bot-led groups, third part; docs/BOT-LED-GROUPS.md).
- When a dragon or epic dungeon raid forms in your realm and you are level 50, its **raid leader** may send you a tell
  ("forming for Gjalpinulva at Svasud Faste. want a spot?") and then a box: "Haldor invites you to join the Gjalpinulva
  raid. Do you wish to join?" The raid leader is a level-50 officer of a raiding guild in the raid (else the leader of
  its first party). It works the same in Albion, Midgard and Hibernia.
- **Once per raid.** A no is final for that raid. Never while you fight, are grouped (a gamebot party hosting you
  included), ride, trade, have `/spawn` companions out, are `/afk` or have another box open, never in your first 10
  minutes online, and never for a neutral raid (Darkness Falls and the like) or a raid already in its battle.
- **Yes** puts you in the raid party that needs your class most, one still at the muster town first. If that party is
  full, one gamebot of your role (never the party's or the raid's leader) steps out and goes back to its own play,
  where the raid's refills may take it again. You travel to the muster town yourself (never teleported); your party
  waits there until the raid leaves, and after that you catch up on your own. The party leader welcomes you in
  `/group`.
- **In the raid party** the raid runs the party: `/lead` is refused, its gamebots don't ride porters with you, and no
  bot-led party invites you. Your kill quests count the party's kills as in a party that hosts you; trash loot is
  shared as in every group with gamebots, and the raid's own rule for its big drops (a quarter to players) is
  unchanged. If you leave, the raid fills your seat with a gamebot again.
- **No gamebot is taken from your group for a raid** (as before): a gamebot in your group or in a party hosting you
  is never recruited. The gamebots of a raid party you sit in stay the raid's.
- **Settings:** `bot_raid_invite_player` (on; the launcher's **Raid leaders invite you**). `/invites off` stops these
  invites too.
- **For testers:** `RAID_PLAYER_INVITE` lines in the server log (sent, not sent, declined, accepted, seated, not joined,
  with the reason and who stepped out); the startup `FORK_BUILD` line shows the setting.
- Nothing here has been played in game yet.

**Bot-led parties look for a replacement, take their breaks, and call their pulls** (bot-led groups, fourth part).
- **"lf 1 more":** when a member leaves a gamebot party that hosts you, or you leave it, its leader asks in `/lfg` for
  the missing role ("lf 1 more, need a healer for skeletons") and takes a free gamebot of that role and a fitting level
  (within 5 levels, or both 50) that is waiting for a group and at most 8 minutes away. The recruit travels to the
  party by the usual roads and stablemasters; the leader says "got someone coming, hang tight". A gamebot that a raid,
  an RvR march, a battleground rally, another party or your own group holds is never taken. The ask is said once per
  search and only while the realm's `/lfg` is under its chat budget.
  - **Lost its healer, tank or damage dealer?** The party waits for the recruit instead of breaking up, and pulls
    nothing meanwhile. After 5 minutes without one it gives up: it carries on short-handed when it still has a tank, a
    healer and a damage dealer, and ends otherwise ("couldnt find anyone, gonna call it here").
  - **When you leave** a party you followed, it carries on and looks for someone of your class's role (it used to
    break up when you were its only healer). A group you lead is unchanged: gamebots you lead leave with you.
  - Gamebot-only parties keep their old rules (they carry on short or end); raid parties refill as before.
- **"last pull then town":** a party hosting you now takes the same natural break every gamebot party has: when half
  or more of its gamebots have nearly full bags, or its task has 5 minutes or less to run, the leader says "last pull
  then town", the party finishes that pull (the fight going on, or the next one; if no pull comes within 3 minutes it
  goes anyway), and then the party ends there, as at any task's end: each gamebot goes to sell, and the leader says
  "ok town to sell, good group all". Selling still happens only at that break, never in the middle of a camp.
- **"pulling":** in a party a gamebot leads, the leader calls its own party's pulls ("pulling", "inc, on me", "here it
  comes", and more), and that call is the pull's only line: the other gamebots' "inc" isn't added beside it. In a group
  you lead, pulls are called as before.
- **For testers:** `BOT_LED_RECRUIT` (searching, recruited, gave up) and `BOT_LED_BREAK` (called, ended) lines in the
  server log.
- **Not yet:** the party doesn't walk to town and back together with you: the break ends the party. Gamebots have no
  potions or wearing gear, so there are no breaks for those. Nothing here has been played in game yet.

**Replies that don't give bots away** (roadmap item 6; how it works: [docs/bot-voices/CHAT-BUDGETS.md](docs/bot-voices/CHAT-BUDGETS.md))
- **Bots answer you first.** A tell to a bot, and a line in /faction or /say that names a bot, are always answered, however
  busy the channel is. So is a /say to the bot right in front of you, and a "why?" or "where?" about a bot's own line.
- **Replies have a budget of their own.** In /faction and /say, bots' answers are now counted apart from the lines bots
  start on their own, with their own target and ceiling. When a channel is busy, bots stop answering each other first; the
  room that's left goes to answering you.
- **/faction on a small server keeps to its budget.** With 10 gamebots a realm, /faction ran at about four times the pace
  the server's own budget asked for (the automated playtester measured 1.2 to 1.4 lines a minute against 0.27 to 0.36).
  The server measured each channel over the last two minutes, which can't tell a slow channel's floor from silence, so
  every quiet two minutes a bot spoke at once, and the answers came on top. It now measures over a window long enough for
  the channel's pace (2 to 20 minutes). The automated re-run (the playtester's faction-rate scenario, 10 gamebots a
  realm, two half-hour listens each, in the development container): **before 1.20 and 1.33 lines a minute, after 0.87
  and 0.97**, against an opener target of 0.27 to 0.31 and a reply target of the same. Most of what is still over the
  budget is the gold spammer's waves (6 to 11 ads in half an hour) and the debate scenes: they come at the same pace on
  any server, however few bots it has. They now count against the openers' budget, so bots start fewer lines of their
  own while one runs.
- **Debates and gold spammers fit the crowd** (owner 2026-11-05). On a realm with fewer than 300 gamebots online, the
  /faction debates and the gold spammer's waves come less often, and a wave has fewer ads: with 10 bots a realm they come
  about a fifth as often and a wave is two ads. From 300 bots a realm up, nothing changes, so at 900 they come exactly
  as before. In the evening model at "How much they talk" 2, /faction with 10 bots a realm goes from 0.86 to 0.74 lines a
  minute, and with 300, 900 and 1,000 it stays at 3.96, 6.78 and 6.77.
  The playtester's faction-rate scenario with 10 gamebots a realm (two runs, in the development container): 0.62 and
  1.03 lines a minute against the 0.66 to 0.69 it now expects (the opener target plus the reply target), with no debate
  or spam wave in the listen, and neither run flagged. The playtester now listens until 10 lines are expected, so these
  listens were 14.5 minutes, shorter than the half hour of the earlier runs.
- **No quieter on a full server.** The world was re-balanced, not cut: /faction's openers keep 4 a minute at a full server
  and its answers come on top of them; /lfg and /trade aim at what 1.1.2 actually delivered (1.8 and 1.6 a minute, were
  1.6 and 1.3); a guild's chat and /say near you have higher floors, so a few bots keep them going. In the evening model
  (`UT_ChatBudgetSimulation`) at the default "How much they talk" (2), all the channels you hear together go from 7.9 to
  8.4 lines a minute with 300 bots a realm, and from 16.4 to 18.0 with 1,000. A 10-bot realm is quieter than in 1.1.2,
  because it was running far over its budget.
- **Nothing untrue about themselves.** A reply that would say the bot is another class, role or level ("im a cleric" from
  a thane, "38 zerk here" from a 41) is dropped for something short that fits, or nothing. So is a group or trade ad said
  to a line about neither ("need a healer still?" to "gn all"). Bots also say where they are: "what zone r u in?", "are
  you in camelot hills?".
- **`/lswhy` shows a reason code** for every decision ("said [exempt-named]", "held [hold-reply-target]", "silent
  [below-minimum]"). New: `/lswhy me` shows why bots answered your own lines or didn't, and `/lswhy held` the replies the
  budget held back. `/lswhy budget` shows /faction's and /say's replies apart from the lines bots start.
- **Tuning:** `reply_target` and `reply_ceiling` per channel in `chat-budget.json`. A copy of the file from 1.1.2, without
  them, works as before.
- **Checked:** automated only. New tests (`UT_RepliesDontGiveBotsAway`) and the evening model compared against 1.1.1 and
  1.1.2; the full server suite passes. The playtester's faction-rate scenario, before and after, is above. Not played in
  game.

**Quests: the epic chains linked on every install, repeatable quests, deliveries, and a machine that plays them**
([docs/QUESTING.md](docs/QUESTING.md))
- **The epic chains are linked on a normal install.**
  - **Before:** Beta 1.1 put the Guild of Shadows chain (levels 7 to 48, the Camelot and Shrouded Isles routes) in order,
    with its period dialogue, and pinned every other guild's epic steps in order, but only if you ran
    `epic_chains.py --apply` by hand with Python, which the game doesn't ship. On a normal install `/epic` said "no epic
    chain linked by quest IDs", and a world it was applied to broke the released game's Guild of Shadows after a
    rollback.
  - **Now:** the server makes the same changes itself each time it reads the quests, in memory. Nothing is written to
    your world, so ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd have nothing to undo for them and the released game's quests
    are as they were. Quest rows you changed yourself are left alone, as the tool did. Setting `epic_chains_linked` (on)
    switches it off.
  - **A world you ran `epic_chains.py --apply` on** still holds the links in its rows. ROLLBACK.cmd and
    PREPARE-FOR-UPDATE.cmd now refuse on such a world until you put them back with
    `python tools\claude-version\epic_chains.py rollback --apply` (or run them with `-Force`), because the released
    server would never offer the Guild of Shadows. The server notices such a world when it starts and says so in its
    log and in `runtime\server\epic-chains-in-world.txt`.
- **Repeatable quests are offered again.** A quest written to be done several times (MaxCount 2 or more) was never
  offered after you finished it once. Quests with no limit (MaxCount 0) and one-time quests are still offered once, as
  before, until the world report shows how the classic quests use 0.
- **Deliveries in quests without a collect column can be handed in.** A quest whose row had no CollectItemTemplate at
  all ignored every item you gave, so its delivery steps could never be done.
- **Collection quests take their item whatever the case of its name.** Dera's Human Leg XP Item never took a human leg:
  the quest asks for `Human_Leg` and the item is `Human_leg`, and the turn-in compared the names letter case and all.
- **Classic quest steps run on the game loop.** The classic quests' 5-second check (map markers, the monsters that appear
  for your step, items handed to you, places reached, items held) ran beside the game loop and could race with it ("it
  just didn't count"). The timer now only asks the game loop to run it.
- **The quest harness.** A test plays a quest from offer to finish on the server's own quest code with a fake player:
  talking, kills by you, by a groupmate and by a gamebot in your group, deliveries, collections, whispers, searches and
  every classic custom step, checking the rewards, the XP at your XP-by-level rate and the quest markers. A world report
  walks every quest of a game's world database the same way (read from a copy, never written) and lists which quests
  can't be completed and why. How to run it on your PC: docs/QUESTING.md section 6.
- **Checked:** automated only. New tests `UT_QuestHarness`, `UT_QuestHarnessWorld`, `UT_EpicChainLinks`; the full server
  suite passes (4,796 tests); the chain links match `epic_chains.py`'s output exactly on its own test world; the installer
  tests pass with the new rollback checks. The world report on a copy of the owner's test world: all 1,540 quest rows finish
  every walk. Not played in game.

**An automated playtester** (for testing, not in the game; [docs/PLAYTESTER.md](docs/PLAYTESTER.md)). Until now every
release was checked by hand in the game client. A scripted player can now run the same checks with no person at the
keyboard, overnight or in CI:
- **`make_test_world.py`** makes a test world from a clean release world: a new file, every save emptied as a release
  is, three test accounts and a small roster of gamebots. Each realm's first gamebot is level 12 and trained for it,
  so the group scenarios have someone to group with. It only reads the world you give it and never writes over a
  file. A `minimal` world needs no game files at all.
- **`OfflineDaoc.Playtester`** connects to the server as the original 1.127 client does, logs in, makes characters
  and plays scenarios: log in, save and restart the server (the character comes back where it quit); say and tells
  between two players; a group invite with group chat; a tell to a gamebot (it answers); /faction lines a minute,
  passing at half the rate the server's own chat budget predicts for the bots online and the hour; a delivery quest
  taken, carried and handed in.
  - **A kill quest in a group with a gamebot.** A player is made an Armsman a few levels above the quest. It trains
    at its trainer, buys a weapon, invites the level-12 gamebot, kills the target with it and hands the quest in.
  - **Guild dues and merit.** A player's own guild sets dues, earns merit for its levels and takes a gamebot in. The
    dues from the coin they loot together go to the guild bank.
  - Each run writes a report in Markdown and JSON with timings and every chat line it saw.
- **/faction counts the reply budget** (owner 2026-11-05, with PR #82). The expected /faction rate is now the chat budget's
  target plus its reply target, as the 1.2 server paces them. A run above the server's ceiling (the ceiling plus the reply
  ceiling) is flagged in the report and the console; `--fail-over-ceiling` makes it a failure. Against Beta 1.1.2's
  budget, which has no reply target, the prediction is unchanged, and its 1.2 to 1.4 lines a minute at 10 bots a realm
  are flagged.
- It refuses any database that `make_test_world.py` did not make, and runs the server from a copy of its folder, so a
  test run leaves nothing in your install.
- Its protocol code comes from the new client's network client (`source/new-client`); lometur's HearthDAoC headless
  client was the model for the idea (it runs inside the server, so it was not ported).

**New client (0.37c): skills on the quickbar, Stealth and the merchant's SELL button** (GitHub issue #34). These are
shadowofze's own 1.2 fixes, installed as patches to the new client's scripts (only where the script is the released
0.37c one; ROLLBACK.cmd puts it back).
- **Skills onto the quickbar.**
  - **Before:** an ability dragged from the skills window (Sprint, Stealth, Safe Fall, a realm ability ...) showed a
    no-entry sign over the quickbar and wouldn't go on it. Spells and styles already did.
  - **Now:** abilities go on the quickbar too, and one click on the slot uses them. Shift + right-click empties a slot.
- **Stealth.**
  - **Before:** Stealth was only a line in the specialization list (the server treats it as a specialization you
    use, not an ability), so the Abilities list had no Stealth to click or drag, and /stealth was "not a command".
  - **Now:** the Abilities list has a Stealth row: double-click it, drag it onto the quickbar, or type /stealth (or
    /hide) to hide and to come out again. You don't see yourself as a see-through ghost yet, and the effect bar may
    show another picture for Stealth: both need the 1.2 network client.
- **SELL at merchants.**
  - **Before:** the merchant window had BUY only. Selling worked by dragging an item onto the window, which nothing
    told you.
  - **Now:** right-click an item in your backpack, then click SELL (dragging an item onto the window still sells it).
    The window also gets a quantity for BUY (arrows, KEEP), a scroll bar for long pages and the pack size of arrows,
    bolts and poisons.
  - **APPRAISE** stays hidden in this version of the new client: the server's appraise needs the 1.2 network client
    (its MMOEngine.dll), which this update doesn't install. In shadowofze's 1.2 scripts the button was there but a
    click did nothing.
- The account vault window keeps its title (with the 1.2 scripts alone it would have said HOUSE VAULT).

**Epic raids learn four more bosses (raid tactics, round 3).** Everything below is built and covered by automated
tests; none of it has been seen in a real raid yet. Govannon and Orshom are level 80: this teaches the raid their rules,
it does not make them easy, and the hardest level 70+ encounters, Darkness Falls raid AI and Legion still have no raid
tactics.
- **Grand Summoner Govannon (Summoner's Hall, all three realms).**
  - **Before:** on 2026-11-06 the Midgard raid took him to 53% and then 51%, twice. Each time, at 50%, he turned into a
    demon at full health and the raid fell in about a minute: 88 and then 101 raider deaths, most with nobody hitting
    them. His Shroud of Agony hurts everyone within 300 of his target, and the raid stood in one pile on that target.
  - **Now:** in his demon form, casters, archers and healers stand clear of his target (550 from it) and fight and heal
    from there. Melee can't get clear and fight on, but the healers are no longer dying beside them. The party he is
    fighting holds him.
  - At 80% (in each form) he summons a sacrifice and a demon that walk slowly to his altar. Only if both reach it does the
    Shade of Aelfgar come. The raid's casters and archers now kill them on the way while everyone else stays on him.
  - His full 300,000 health stays switched off.
- **Orshom Brong and Orylle (Krondon).**
  - **Before:** the Albion raid lost at Orshom twice overnight (lowest 59%, then 28%). His fire pit, Orshom's Fire, burns
    500 every 3 seconds within 300 of it and only cold hurts it. Bots it burned fought back, standing in it.
  - **Now:** the fire is never a target, and the raid never turns on it because "it is fighting the raid". A raider within
    450 of it steps out to 600 first and fights on.
  - When Orylle joins at 50%, the party she attacks holds her and everyone else finishes Orshom first. The raid also goes
    for Orshom before her.
- **Galladoria.**
  - **The Earthmenders' class rule is back on.** Each of the four Earthmenders that heal the Corrupt Primal of Earth
    again takes damage only from its own classes, as in the original:
    - Guardian: blademasters, heroes and champions.
    - Magical: eldritchs, enchanters and mentalists.
    - Natural: bards, druids and wardens.
    - Shadowy: nightshades and rangers.
    - Animists, valewalkers and pets hurt all four.
  - The raid now kills the Earthmenders before the primal, and each raider takes one its class can hurt. A class none of
    them takes damage from (bainshee, vampiir) fights the primal and keeps it busy. A player fighting them yourself plays
    by the same rule.
  - **Spindler Broodmother.** Casters and archers kill each newborn spindler as it comes, while melee and healers stay on
    her. The 20-25 small spindlers that burst from her body when she dies are cleared there before the raid moves on.
- **A quieter log.** `REALM_EXPEDITION_EARLY_PULL` had one line for every Darkness Falls named the raid walked into (53
  in four hours). It is now logged for a raid's final boss and for catalogued scripted bosses (once per boss every ten
  minutes), and always for a pull-back. The rest go to the debug log, and the next logged line counts them
  (`quietSinceLast=`).
- **The encounter catalog** (`docs/encounters/EPIC-ENCOUNTERS.md`) marks the new tactics "1.2 (new)". It also shows
  which restored mechanic is now on, and records what the 2026-11-06 logs showed for each of these bosses.

**Also in 1.2: measuring the AI**
- **AI_PROFILE: what the bots' AI costs, measured every minute** (technical audit 2026-11-03, "measure before
  adding"). Nothing in the game changes: it only measures.
  - **What:** once a minute the server log gets one `AI_PROFILE` line: the game-loop tick's p50, p95, p99 and
    maximum, split into the serial planning before the brains and the brains' parallel pass, the time spent waiting
    for the coordinator, raid, RvR and database locks, and the ten costliest AI systems. About 70 systems are timed:
    brain turns by kind, each serial coordinator pulse, the world controller and its branches, route planning and
    navmesh queries, the frontier threat scan, combat AI, pets, persistence, chat, economy and town visits.
  - **Where:** `logs/ai-profile.csv` (one row per system a minute), `/lsprofile` in game (GM), and
    `tools/perf/ai_profile_report.py`, which turns a log into a top-ten table, a tick timeline and the L300/L600/L900
    load-test verdict. How to read it and how to run the load tests: [docs/PERFORMANCE.md](docs/PERFORMANCE.md).
  - **Setting:** `ai_profile` (on). One timed call costs about 65 ns (a CI test fails it above about 100 ns).
  - **Honest settings:** `ai_budget_ms` was described as a budget that defers work, but nothing reads it. Its
    description now says it is not enforced yet; the deferral queue is decided after the L300/L600 baselines.
  - **Credit:** built on lometur's HearthDAoC bot CPU meter (per-thread counters, brain time by bot kind) and
    stefanrows's `BotThinkProfiler` (the timed scope, the measured lock, the minute line), both GPL-3.0.
  - **Checked:** 33 new unit tests (percentiles, merging, self time, locks, output, the scope's cost) and 12 for the
    report tool; the full server suite passes (4,123). Not yet run on a live server: the first L300 run is the owner's.

## Beta 1.1.2 (in development)

Chat: a much busier realm, and bots that answer what you actually said. Raids the size they were in the era, sized
by their own wins and losses, that no longer stand for hours at an encounter they can't reach, and their first boss tactics. Playing with your bot
group: dragon raids that regroup after a wipe and call the Epic Special Forces, quest credit and hand-ins, and quest XP that follows your
XP-by-level rate. Gamebots in your guild earn merit and pay dues, power comes back faster, fixes from other forks of
Offline DAoC, updates from the launcher, and a world database that is safe from crashes and backed up every day.
Built and tested; it ships once it has been played (the checks are in [docs/RELEASE-TRAIN.md](docs/RELEASE-TRAIN.md)).

**Epic raids learn their first encounter rules** (owner 2026-11-06: shipped in 1.1.2, not 1.2). Raids of gamebots now
know five bosses that beat them by their rules, not by their damage. Everything below is built and covered by automated tests; none of it has been seen in a real raid yet.
- **Scurceol Hyrde (Krondon).**
  - **Before:** he takes no damage while his four floating Lyft Miht orbs live, but the raid hit only him, for an hour, and
    all 42 raider deaths were his.
  - **Now:** while an orb lives the raid's target is an orb. Each party takes one; casters and archers shoot it from the
    floor, and melee wait under it, away from him. Nobody hits Scurceol until the last orb is down, then the whole raid
    turns on him. Orbs stay dead if he resets.
  - The raid also fights him in his own room: past 1,500 from his spawn it stops hitting him, and the raider he chases
    leads him back.
- **Black Lady and the Ancient Black Oak (Marfach Caverns).**
  - **Before:** the raid dragged Black Lady out of her room until she went home at full health, then pulled her Oak 91
    times without ever reaching it.
  - **Now:** the Oak dies when Black Lady dies, so the raid fights her instead. Past 1,100 from her spawn (her leash is
    1,500) the raid stops hitting her and leads her back, so she never resets mid-fight.
- **Original boss mechanics are back, switched off.** In 23 places the fork had changed a boss for bot raids; 22 of
  them have their original back. Examples:
  Aroon's damage-type order, Funus healing from the wrong weapons, the Earthmenders' class puzzle, Beatha healing Xaga,
  King Tuscar and Queen Kula healing each other, Kvasir healed by cold, Govannon's full 300,000 health. The originals,
  read from OpenDAoC's own code, are now behind 17 new switches in `EpicEncounterMechanics`, all off, so bosses play
  exactly as before. Each one gets turned on once the raid knows how to beat it.
- **Where to look.** The encounter catalog
  ([docs/encounters/EPIC-ENCOUNTERS.md](docs/encounters/EPIC-ENCOUNTERS.md)) has three new lists: encounters the raid
  has tactics for, the restored mechanics and their switches, and the mechanics that still only pick real players. The
  server log shows each decision as `REALM_EXPEDITION_TACTIC` (the raid's target) and `EPIC_TACTIC_BOT` (a raider's).
- **Raids answer a boss that jumps them before the muster.** Before, whoever was nearby fought it, and the rest arrived
  in twos and threes. Now the whole raid answers together:
  - With half the raid already near, or with a boss that would chase them down, everyone goes in at once.
  - A boss also chases them down when the muster is inside its leash. On 2026-11-06 Fuladl (Krondon) could chase raiders
    up to 3,800 from his spawn, and his muster was 1,400 from it. Then the raid goes in at once too. Most catalog bosses
    have a leash longer than the 1,400 a muster stands off, Aidon's 1,500 included, so most early pulls now commit.
  - Otherwise everyone pulls back to the muster, the boss walks home and resets, and the raid comes back together.
  - Each decision is in the log as `REALM_EXPEDITION_EARLY_PULL`.
- **A lost skirmish no longer counts as a wipe.** A failed fight now uses up one of the raid's two attempts (and calls
  the Special Forces) only if at least half the raid was in it, or half the raid fell. Example: on 2026-11-05 Aidon
  killed 31 raiders while 56 were elsewhere, and that counted as the raid's first wipe. It would now be a lost skirmish
  (`REALM_EXPEDITION_SKIRMISH_LOST`): the raid rests and musters again without losing an attempt.
- **Musters wait where the boss lives.** A boss that was out chasing raiders is mustered for at its home, and the
  muster moves when the boss walks back (`REALM_EXPEDITION_MUSTER_MOVED`). On 2026-11-05 Aidon's muster sat on the ramp
  his chase had reached, 2,600 from his room.
- **Pulls go to where the boss stands.** The spot the raid walks to when it goes in (the front) is worked out again when
  a boss that is not fighting stands more than 1,000 from it (`REALM_EXPEDITION_FRONT_REFRESHED`). Pulls that went to
  the old spot no longer count toward setting the boss aside. On 2026-11-05 three full pulls on Aidon (75 and then 106
  raiders) walked to the spot from his earlier chase and stopped 1,979 from him. He never entered combat and was set
  aside (`REALM_EXPEDITION_PULL_UNREACHED`, which now also logs `frontDistance`). If no new route to the boss is found,
  the old spot is kept (in Darkness Falls the last waypoint of a route is the spot on purpose).
- **Floating required adds are shot from the floor (Scurceol Hyrde, Krondon).** On 2026-11-06 the raid turned to
  Scurceol's four Lyft Miht orbs twice, but no route to them was found and each was set aside. They float 230-580 above
  him, and the route only looks for a path to the spot it fights at. The raid went to Fuladl and never fought him. Now
  the route never walks to a required add:
  - The raid fights it from a floor spot it can walk to: under the add, beside its boss, or in between.
  - The spot must be within 1,400 of the add and see it.
  - Raiders who can't hit the add in the air wait at that spot.
  - Only when no add has such a spot is the boss set aside, with the reason for each add (`REALM_EXPEDITION_TACTIC_ASIDE`).
- **Aidon the Archwizard (Hall of the Corrupt).** The raid kills three of his four illusions and leaves the last one
  alive, because he only summons a new set once all four are dead. Then everyone fights him, near his room. His damage
  shield only hurts melee attackers; it doesn't make him harder to damage, so he can be beaten by design.
- **Nosdoden (Trollheim).** When a wave of essences comes (every 10% of his health), the raid stops hitting him and
  kills the wave first. His health then drops one step at a time and waves don't pile up.
- **Xanxicar (Crystal Cave).** Before 80/60/40/20% and until his stomp has landed, everyone except the party he is
  fighting moves 2,700 away. That party pushes him over and takes the 2,500-radius stomp, which is meant to kill whoever
  is in it; the rest of the raid survives and comes back.
- Not changed: Darkness Falls raid AI, Legion and the hardest level 70+ encounters still have no raid AI.

**Chat: a busier, more human realm** (owner 2026-11-04: shipped in 1.1.2, not as a separate 1.1.3). The goal is a
realm that feels like a full server at its busiest. How it works and how to tune it:
[docs/bot-voices/CHAT-BUDGETS.md](docs/bot-voices/CHAT-BUDGETS.md).
- **More chat, scaled to the crowd.**
  - **Before:** chat stopped growing at about 390 bots a realm.
  - **Now:** /faction, /lfg, /trade, /say near you, town /broadcast, guild and group talk each have a floor, a target and
    a ceiling. They rise with the bots online, with no cap, and follow the time of day.
  - **Town /broadcast:** bots now use it the way players did in 2003: selling (sometimes as a repeating macro), looking
    for a group, a little chatter. You hear it where the base game's /broadcast reaches.
  - **One file:** all the numbers are in `chat-budget.json`, with comments. Research into 2003 chat gives the shape
    (which channels, places and hours, sellers' macros, bursts). The levels are set for a busy peak server, on purpose
    busier than the 2003 estimates. Nights are calmer, never dead.
  - **Turn it up:** the launcher's Living Server tab has a new **How much they talk** setting (0–3). 1 is a busy peak
    server, 2–3 very busy.
- **Big moments ripple through chat.** A keep taken or lost, a relic moved, a raid won or wiped, Darkness Falls changing
  hands, a server first and a nemesis paid back each draw a short burst of reactions in /faction. The realm-wide ones also
  reach the capital's /broadcast.
- **Bots answer what you said.**
  - **Weak replies:** a reply that doesn't fit your line is dropped for something short that does. A tell gets a reply
    to what it says ("ty", "not selling sry", "nah im good ty", "no heals on a thane sry", "cool"); only "hi" or "u
    there?" gets "?" or "ya?". Banter and bad luck get a "lol" or "ugh" back now and then, most in a group, and a
    question nobody knows gets one "dunno" in a group or guild. No more "ask around maybe" to a question anyone could
    answer.
  - **Silence where it belongs:** spam and scams (links, real-money prices, fake GMs, "your account has been flagged",
    account sales, doubling offers, free plat, password requests) and lines said to someone else by name get no answer.
  - **Honest answers:** "spec?", "lvl?", "what weapon is that?" by tell get the bot's real spec, level and weapon. A call
    gets "where?" or "omw", a cry for heals the truth for the bot's class, a "45+" raid "too low lol" from a level 30,
    and a thane never says "np" to thanks for heals.
  - **/say to the bot in front of you:** a /say line counts as said to a bot when you target it, answer its line, or
    stand right in front of it with nobody else close.
  - **More places to ask:** bots answer `/advice` when one of them knows, and questions in `/broadcast` and chat groups
    by tell.
  - **About themselves:** bots answer their name, class, spec, level, zone, group and what they're hunting from their
    real state.
  - **How things work:** they explain equipping, binding, `/stuck`, `/anon`, `/release`, training, and how many realm
    points a realm rank takes.
  - **By situation:** "gn" gets a goodnight, "stuck" gets `/stuck`, "inc" gets "where?" or "omw", a call for help an offer
    to come, a sale a price or a no, and a counter-offer to a bot's price a counter or a deal.
  - **Words they now read:** "lo", "re", "wb", "long time no see", "u there?", "ne1 on?", mishaps ("lol rip"),
    apologies, offers of help, "where?" after a bot named a place, "thx mythic" (sarcasm, not thanks), "free rps at…"
    (a rally), "loot the named" and "fake inc" ("k"). A call that already says where and how many gets "omw", not "how
    many?". `/stuck` is for you, not your pet.
  - **Roleplay in kind:** a challenge gets a challenge, a toast a toast, a mourning a bow, otherwise an emote.
- **One answer, then maybe a few more.** A question gets one answer. A hello or a ding gets a few. A joke now and then
  draws a "lol" or "+1". Replies take longer to type the longer they are, at each bot's own typing speed.
- **Less filler.**
  - **Chat about the game in front of them:** most unprompted lines are now short and real: "lfg 38 zerk", "wts 97%
    chain hauberk /send", "anyone up for vendo?", "inc beno", "who has rena?", from the bot's real level, class, zone and
    bags and the realm's real keeps and relics: camps for its level, "lf a healer for keltoi", class questions, rallies
    while a keep is under attack, and how many keeps the realm holds. Guild chat too: group calls, raid and keep plans, "gz" to a guildmate
    who really dinged, a crafter wanted, an old item offered, "brb" and "night all". The same item isn't advertised all
    evening, and short answers vary.
  - **Regulars:** each realm and guild has a few talkative regulars for the evening who carry much of the chat, answer
    each other and follow up their own lines; one thought isn't reworded by someone else minutes later, and chat comes
    in runs and lulls rather than evenly.
  - **Threads and stories:** a question usually gets an answer, and some run on for a few lines between two or three
    bots. A ding to 50, a death or a group's wipe becomes a short story others riff on. Help questions come in many
    shapes, and guild chat hears at most one weather-or-school line an evening. Answers take typing time and give what
    was asked ("where" gets a place, "how much" a price).
  - **Musings and quips:** lore musings, polished jokes, tips and life stories appear only rarely in /faction, never as
    replies.
  - **Generic agreement:** "good question", "very true" and the like are gone.
  - **Repeats and polish:** near-copies are dropped, the wordings of one line rest for hours once one is said, and most
    bots type in lowercase. Typos are rarer and look typed (a dropped letter).
  - **The Realm Exchange:** bots mention it only when someone asks where to buy or sell.
  - **Templates:** a few grammar slips are fixed.
- **Typing habits.** About a third of bots have small habits of their own: "u" and "ur", "??", "...", a trailing "lol",
  and for a rare few, the period's leetspeak.
- **`/lswhy`** (every player) shows why a bot said what it said, or why nobody answered, and `/lswhy budget` shows how
  busy each channel around you is.
- **Checked:**
  - **Automated:** new tests for every part; the full server suite passes.
  - **Simulated evening at 1,000 bots a realm:** about 12.7 lines a minute across the channels a player hears, up from
    about 1.9, with /faction above its busy-peak target of 4 a minute. At 300 bots it's about 5.9, up from 1.2.
  - **Re-running the first blind evaluation's 400 lines:** the read rate rises from 53% to about 70%, and about a quarter
    of those lines now get no reply. Those lines are known, so this is a regression check; a fresh blind round judges
    the result.
  - **In game:** not yet played.

**Raids the size they were in the era, and raids that keep winning bring fewer bots** (2026-11-04)
- The goal for raids is "mostly successful, high stakes, never a guarantee": about two wins in three. **The bosses are
  never weakened**; only the raid's own size and its help change.
- **Period sizes.** Every dragon, epic dungeon, Summoner's Hall and Darkness Falls raid needed 200 level 50s and
  recruited up to 300. Each raid is now measured against the size a comfortable raid had there in 2002-2004, from forum
  raid threads of the time ([docs/RAID-SIZES.md](docs/RAID-SIZES.md) has the table and its sources), and starts at a
  quarter above it, because bots play worse than 2003 players (the first night on the period sizes won 1 raid in 7):
  - **Caer Sidi, Galladoria and Tuscaran Glacier:** 125 (period size 100).
  - **Crystal Cave:** 150 (120).
  - **Dragons:** 88 (70).
  - **Summoner's Hall, Darkness Falls, Krondon, Avalon City, Trollheim, Iarnvidiur's Lair and Marfach:** 80 (64).
  - **Hall of the Corrupt** 60 (48), **Dodens Gruva and Tur Suil** 50 (40).
  - **Recruiting:** a raid gathers about a quarter more than it needs (Caer Sidi up to 160).
- **Sizing by results.** Every automatic raid, dragons too, is sized from that encounter's own record in the Herald.
  - **How it moves:** each win trims the next raid there by 10% of its period size; each defeat or time-out adds 20%
    back. Two wins and a defeat leave it about where it was, so the size settles where the encounter is won about two
    times in three. Four defeats take a raid from its start to its largest size. The Herald notes each raid's size
    ("Raid size: 145.3%."), and the next raid there carries on from it.
  - **The limits:** it goes from 70% of the period size (Caer Sidi 70, a dragon 49) up to twice the period size for
    raids that keep losing: Caer Sidi, Galladoria and Tuscaran Glacier 200, dragons 140, Summoner's Hall and Darkness
    Falls 128 (recruiting up to 256, 176 and 160). Never past 300.
  - **What counts:** raids cut short by a restart, beaten to the boss by another realm, failed rallies and stopped
    raids don't count.
- **After a restart** no automatic raid opens until the realm's bots have finished logging in. Before, a raid could
  open two minutes after a restart and be sized from the few already online (Tuscaran Glacier needed 58, not 100).
- **Short starts.** A raid out of time still goes in with half of what it needs, now at least 24 (three parties)
  instead of 40, so a small raid isn't held to nearly its full size.
- **The Epic Special Forces come with 24.** If they helped win at an encounter, they come with 16 the next time a raid
  there calls them, then 8; they always answer a wipe with at least 8. A win without them changes nothing, and each
  defeat or time-out there adds 8 back, up to 24. Two defeats in a row still bring them from the start. Before, every
  win cut them by 8, whether they had been there or not, and three wins in a row meant nobody came. The Herald now
  notes "Epic Special Forces joined: N." on a raid they joined; older records count as raids without them.
- **Parties that answer on their own** now stop at the raid's recruit limit too. Before, they could fill any automatic
  raid to 300, so a trimmed raid would have grown back to full size by itself.
- **The Special Forces** follow the encounter's whole record of wins and losses; before, their last six rows could be
  restarts or failed rallies that pushed real results out.
- Raids you start from the launcher keep their 200 and 300, and their wins and losses count for neither the raid's size
  nor the Special Forces: the Herald now marks them ("Forced raid: ..."), and older unmarked records count as
  automatic. `realm_raid_minimum_bots`, when set, is used as it stands.
- **Settings:** `realm_raid_target_win_percent` (67; 0 turns the sizing off), `realm_raid_smallest_size_percent` (70),
  `realm_raid_largest_size_percent` (200), `realm_raid_start_size_percent` (125) and `realm_raid_win_trim_percent` (10).
  A world that ran an earlier test build of this change moves to these defaults once (step 5 to 10, largest 100 or 150
  to 200, smallest 60 to 70), unless you changed them yourself. The `REALM_RAID_SIZED` log line now ends with the
  rules in force (`rules="start=125% range=70-200% win=-10 loss=+20.3 target=67%"`).
- **Seeing it:** `/lstest raid` names the size and why, and the server log has a `REALM_RAID_SIZED` line for every
  automatic raid (period size, size, required, recruit limit, wins and losses, the Special Forces it would get, and why);
  `REALM_RAID_SCHEDULED` now shows the size too.
- **Keep sieges:** the siege balance that moves the defenders' share by up to 0.25 forgot everything at every restart,
  and needed three sieges in one session before it moved at all. It is now rebuilt from the Herald's last bot sieges at
  start (sieges answering a player's attack are left out), and every change is logged (`SIEGE_DEFENSE_BALANCE`).
- Checked: unit tests (`UT_RealmRaidWinBalance`), including a simulation of 2,000 raids against simple models of the
  chance to win by raid size (bots as good as 2003 players, and worse), which settles at 67-70% wins, including an
  encounter that needs 140% of its period size; only one that needs nearly twice it wins about 6 in 10. The models are guesses, not measurements;
  the first night on the period sizes (before the 125% start) won 1 raid in 7. The new sizes are not played in game yet.

**A raid no longer pulls an encounter it can't reach for hours; sieges log their army** (2026-11-05)
- **Marfach Caverns:** Hibernia's raid killed Avartack, drove Black Lady to 44% and then turned on her Ancient Black
  Oak. It stood at its muster for three and a half hours: it went in 91 times, never got within 1,000 units of the Oak,
  and timed out with one named kill. Two causes:
  - **No formation posts:** a raid that turns on a monster attacking it took that monster's own spot as its front, and
    no formation post fitted around the Oak's spot. It now fights from a walkable approach checked from the raid's
    positions, as it does every other target.
  - **No give-up rule:** an encounter that is still out of combat after three pulls in a row is now set aside for 15
    minutes, and the raid goes on to its other encounters (`REALM_EXPEDITION_PULL_UNREACHED`).
  - **Unchanged:** a wipe is still a failed attempt.
- **Krondon:** the Albion raid mustered for Beran the Supply Master for over an hour with 2-5 of the 50 it needed.
  The rest of the raid was fighting Scurceol Hyrde, who can't be hurt while his four orbs live and killed 42 of them.
  The raid kept turning back to him every 15 minutes, and each time it came back to Beran the muster clock started
  again. Now a muster that hasn't gained a raider in 20 minutes, and still needs more, is set aside for 15 minutes and
  the raid goes on to another encounter; the clock carries over when the raid turns away and back
  (`REALM_EXPEDITION_MUSTER_STALLED`). The raid's hold line now lists every encounter still alive in the dungeon and
  what is holding the raid, so the next run can show why the orbs were never attacked. Raiders stuck fighting an
  encounter they can't hurt aren't fixed yet.
- **A time-out without a wipe is no longer a loss** (owner, 2026-11-05). A raid that runs out its four hours without
  ever wiping now counts as neither a win nor a loss: the next raid there is the same size, and the Special Forces
  call-up doesn't change. A time-out after one or more wipes is still a loss. The Herald still says "Timed out" and
  now ends each raid's record with its wipes ("Wipes: 0."). Older records don't have that note, so their time-outs
  still count as losses, including the overnight Marfach raid's.
- **Night sieges:** 2 of 11 overnight sieges were captured, against 4 of 5 by day. This was not fewer bots online:
  580-600 level 50s per realm were online all night, and the login schedule was off. What lost were small armies: keep
  sieges that marched with 49 or more won all 4, those with 9-45 won 2 of 11. Nothing in the logs shows why the night
  musters were thinner, so the siege rules are unchanged. `RVR_SIEGE_MARCH` now logs the army assigned, the realm's
  level 50s online and on RvR duty, its other sieges and the defense share, and `RVR_EVENT_ENDED` logs the armies on
  each side. Both now name the kind of attack and say whether a siege marched short of its muster (`thin=true`), so
  the planned follow-up's deliberate small strikes on towers and lightly held keeps will never be mistaken for one. The next night run will show the cause ([docs/NIGHT-SIEGES-AND-MARFACH.md](docs/NIGHT-SIEGES-AND-MARFACH.md)).
- Checked: unit tests (`UT_MarfachAndNightSieges`); the Marfach approach probe needs the installed navmeshes and has not
  run. Not played in game.

**Keep sieges: when attackers keep winning, fewer of them come** (2026-11-06)
- **What happened:** the next night, attackers took 13 of 17 keep sieges. The siege balance had already given
  defenders all it could (one defender allowed per attacker), but that limit was rarely what held the defense back:
  - **Defenders:** in most lost keeps the realm sent 35-90, all it had free.
  - **Attackers:** reinforcements filled to the full 128 in 10 of 17 battles.
  - **Fast losses:** five keeps fell in 9-13 minutes, before their defenders arrived.
- **Change:** once the defenders' share is at its limit and attackers still take more than 5 of the last 6 keeps, the
  balance now lowers the attackers' limit. It drops one step at a time, from 128 to 112 to 96 (240 to 210 to 180 for a
  relic keep). When defenders start winning again, the attackers' limit comes back first, and then the defenders'
  share falls as before.
- **What stays the same:**
  - the muster still waits for 64 (120 for a relic keep);
  - the third realm still sends up to 64;
  - forced sieges, retakes after your own capture, and the defense of a keep you attack keep their sizes.
- **New log lines:**
  - `RVR_SIEGE_PRESENCE`, every 5 minutes of a battle, shows who is actually at the keep beside who is committed.
  - `RVR_EVENT_ENDED` adds the battle's length, who was at the keep at the end and at most, when the first defender
    arrived (`defendersFirstAt`), and the attackers' limit.
  - `SIEGE_DEFENSE_BALANCE` shows the attackers' limit.
  - Details: [docs/NIGHT-SIEGES-AND-MARFACH.md](docs/NIGHT-SIEGES-AND-MARFACH.md), section 4.
- **Checked:** unit tests only (`UT_SiegeBalanceNov06`). Not played in game; a night's run will show whether it brings
  captures near half.

**Dragon raids call the Epic Special Forces after a wipe** (2026-10-30)
- Dragon raids had no wipe rule at all: after a wipe at the dragon the fallen released to the nearest bind and walked
  back by twos and threes to a dragon at full health, until it died or the four hours ran out, and the Special Forces
  were never called. A dragon raid now keeps the dungeon raids' rules. An attempt begins when the raid goes in from its
  staging ring and ends when the dragon is out of combat after the three-minute approach, or when it has taken no
  damage for five minutes while a trickle keeps it busy; the attempt was a wipe when half the raiders in it fell, or
  the dragon healed back to 95% after being taken to 85% or lower. The raid then regroups at its staging ring, outside
  the dragon's aggro, by the checked route around the lair: the fallen walk back, the living rest and rebuff for at
  least five minutes, and the raid goes in again once 70% of its roster stands there (40% after 20 minutes) and the
  dragon has landed. After two failed attempts the raid is **Defeated** and goes home, and every realm hears it. The
  lair radius, the 600 damage cap and the landing rule are unchanged.
- Review fixes (2026-10-31): who fell is remembered for the whole attempt. A dragon raider releases to a bind in the
  dragon's own region and is on its feet again seconds later, so judging the wipe from the corpses at the moment the
  dragon left combat would have called most wipes a break-off and left the raid trickling back as before. And a
  regroup nobody has reached for twenty minutes (the dungeon muster's rule) is given up and the raid is Defeated, so a
  forced dragon raid, which has no four-hour window, cannot hold at an empty ring until the player presses Stop
  (`REALM_RAID_DRAGON_REGROUP_UNREACHABLE`).
- On the first wipe the realm hears that the raid fell, and the Epic Special Forces are asked for on the same terms as
  at a dungeon: up to 24, one party fewer per win and one more per loss over that dragon's last six results in the
  Herald, nobody after three kills in a row. They arrive at the raid's muster hub and walk to the staging ring like any
  late party. A dragon beaten twice in a row (defeats and time-outs) brings them from the start, as a dungeon does.
- The launcher's Realm Events tab shows "RAID — regrouping at the staging ring after a wipe (staged/needed, attempt 2 of
  2)". Logs: `REALM_RAID_WIPE` now for dragons too, `REALM_RAID_DRAGON_WIPE` (engaged, fallen, health, how),
  `REALM_RAID_DRAGON_REGROUP`, `REALM_RAID_DRAGON_ENGAGE` (the retry) and `REALM_RAID_DRAGON_BROKE_OFF` (an attempt
  that ended with most of the raid standing). Automated tests only (`UT_DragonRaidWipes`); not played in game yet.

**Quests with your bot group** (2026-10-30, review fixes 2026-10-31; [docs/QUESTING.md](docs/QUESTING.md))
- A kill by the gamebots in your group counts for your quests. Before, only a kill you damaged yourself, or your own
  `/spawn` companion made, gave quest credit, so a player who healed, nuked late or only tagged while the invited bots
  made the kill got nothing and no classic step drop. The group's leader is now credited as a companion's owner is,
  when the bot still fights in the leader's group and the leader stands alive in the same region within the group's
  kill-credit range (6,000 units); who gets the experience, the loot and the coin does not change.
- A kill advances a quest step once. The killer's own quests heard the same kill once more for every groupmate in
  range who hadn't attacked, so a quest with consecutive kill steps jumped two or three steps on one kill. The quest
  now remembers the last kill it counted (two seconds), which also keeps the forced credit that nine named monsters
  (the dragons, Legion, Olcasgean, Myrddraxis, Xanxicar, Nosdoden, King Tuscar) give to everyone in sight.
- A hand-in is accepted above the quest's maximum level: a player who outlevelled a quest they already hold could not
  hand its item in while the kill, talk and whisper steps still worked. The level window stays where a quest is offered.
- More than 25 quests at login: a save with more than the client's 25 active quests kept them all and said so at
  login, instead of losing the ones past the limit; finishing one brings the next hidden quest into the journal.
- Whispering a keyword to a quest NPC twice quickly works: the silent 1.5-second pause between whispers now applies
  to whispers to players only, so two quick keyword clicks both arrive.
- Automated tests only (`UT_QuestKillCredit`, `UT_QuestLevelWindow`, `UT_QuestJournal`, `UT_WhisperThrottle`); not
  played in game yet. The in-game check for the owner: a kill quest as a normal account in a group of invited bots.

**Quest XP follows your XP-by-level rate** (2026-10-30, review fixes 2026-10-31)
- Quest XP follows your XP-by-level rate. When `xp_rate_by_level` covers your level, the experience from quests,
  bounties and the daily, weekly and battleground quests is multiplied by that level's rate (quest XP used to skip
  every rate); the 0.36 "share of your level" amounts stay the base, one payment never pays more than one level, and
  the offer window shows what you will actually get. New server setting `quest_xp_by_level`, on by default. It is a
  kill switch: off, the rate and the one-level cap are both gone and quests and bounties pay exactly as before
  (2026-10-31). With it on, a bounty's `xp_rate` is replaced by the range rate inside a range and a bounty pays at
  most one level, where the launcher's 5× and 10× presets used to pay several. The three Shrouded Isles quests
  Ancestral Secrets, The Lost Seed and Lost Stone of Arawn and the Wolf Pelt Cloak payments, which paid outside
  every rate, now pay like every other quest. Details in [docs/XP-BY-LEVEL-RANGE.md](docs/XP-BY-LEVEL-RANGE.md).
  Automated tests only; not played in game yet.

**Gamebots in your guild earn merit and pay dues, like players** (from a r/daoc report; owner's choice 2026-11-03: "Merit + dues")
- **The problem:** a gamebot you `/gc invite` joined your guild in name only. Its levels and realm ranks earned the guild
  nothing and it paid no dues, so a guild of level-50 bots earned almost no merit.
- **Now:** a gamebot in your guild earns it merit points for each level it gains and each realm level it reaches, with
  the player formulas (`GuildEvents.LevelUp` and `GuildEvents.RealmRankUp`), and pays the dues rate your guild leader set
  from the real coin it loots into the guild bank. If the bank is full the coin stays with the bot, so nothing is created
  or lost. Guild realm points, keep claims and guild buffs still never count a bot. `/spawn` companions never join guilds.
- **Bot guilds too:** bot guild members now earn realm-rank merit as well, and every gamebot pays dues on group-split and
  dragon-raid coin, not only on coin it picks up alone (that was missing for bot guilds as well).
- **What you see:** at most one line per guild every five minutes, such as "Seraorith has earned 69 merit points for
  your guild!", or "3 guildmates have earned ..." when several did. Your own merit keeps its usual line.
- **Setting:** `bot_player_guild_economy`, on by default, works whether bot guilds are on or off: the launcher's Living
  Server tab, **Bots in your guild**.
- **Checked:** unit tests for the dues split, merit for levels and realm ranks, the full bank, the switch off and the
  merit lines. Not yet seen in game.

**Faster power regeneration by default, the same rule for bots and players** (from a r/daoc report; owner's choice
2026-11-03: "Faster by default")
- **The problem:** power came back slowly. The server uses the pre-1.87 rate (about 12 a tick at level 50; patch 1.87
  doubled it to about 23), and list casters below half power got half of that. Gamebots and companions were exempt
  from the halving, so the player's own caster recovered slower than the bots around him.
- **Now:** the rate is 1.5 times the classic one (`mana_regen_amount_modifier` 1.5, a middle ground between the classic
  rate and 1.87's doubling), and the halving below 50% is off (`mana_regen_amount_halved_below_50_percent` False). When
  you switch the halving on, it applies to players, gamebots and companions alike. A bot's out-of-combat fast rest is
  unchanged.
- **Existing worlds:** on the first start of this build, a world whose two settings are still the untouched old defaults
  (1 and True, written by the server itself) gets the new ones once. A value you set yourself, or saved from the
  launcher, is kept. The server log says what changed.
- **Setting:** the launcher's Living Server tab, **Power regeneration**: the rate (0.25 to 10) and the halving. For the
  classic regeneration choose 1 and switch the halving on.
- **Checked:** unit tests for bot and player parity, the halving on and off, the rate, and the settings upgrade. Not
  yet played.

**From other forks of Offline DAoC** (chosen by the fork survey of 2026-11-03; each entry credits its author)
- **Gamebots in your group who die in a battleground come back to you** (issue #39; from Cancain's fork, by Cancain).
  - **The bug:** battlegrounds have no bind points, so a gamebot in a player's group released to its capital and stood
    there for good: its way back did nothing while it and its leader were in different regions.
  - **Now:** it releases to its realm's portal keep (under the battleground's level cap), walks back on a planned route,
    waits while you are dead, and rejoins the group on arrival. Solo gamebots and bot-only groups release as before.
  - **Adapted for Living Server:** the way back keeps our detours around hostile monsters and our stablemaster routes.
    A gamebot that makes no headway first plans its way back again, and only when that stalls too is it moved to you;
    never during a stablemaster ride. From another region (no route to try) it joins you after 20 seconds, as in
    Cancain's version. His battleground keep claims (also offered in #40) are not taken: grouped bots don't count for
    keep claims.
  - **Checked:** Cancain's tests plus nine new ones (`UT_BotGroupRelease`). Not yet seen in game here.
- **`/friend` works with gamebots, and two stock friends-list bugs are fixed** (issues #40 and #38; by Cancain).
  Online and parked gamebots of your realm join your friends list by exact name; a real character of the same name wins. Their
  logins and logouts show as "just entered / left the game" in both clients. Adding a friend no longer wipes the old
  client's Friends Monitor, and the first friend on an empty list gets its status. Tests: `UT_BotFriends`.
- **`/allrelease`** (also `/grouprelease`) releases every dead bot in your group at once (issue #40; by Cancain).
  `/spawn` companions still wait while you are dead or a fight is near.
- **On a world with a raised XP rate, Darkness Falls, battlegrounds and the frontiers get it too** (by rambodolphin13;
  BeezKneezItem9 found the same bug).
  - **The bug:** in RvR regions and zones a player's kill XP used `rvr_zones_xp_rate` (1 by default) in place of
    `xp_rate`, so a 10× world was 1× in DF while gamebots there got 10×. The base game's rule (from OpenDAoC),
    unchanged in 0.37b.
  - **Now:** players get `xp_rate` (or `xp_rate_by_level`) times `rvr_zones_xp_rate`, the rule gamebots already had.
  - **Checked:** two new tests in `UT_LevelRangeXpRates`. Not yet seen in game.
- **Gamebot fixes from long soak runs** (by Nikse77, via rambodolphin13; the keep doors by rambodolphin13). Autonomous
  gamebots only; `/spawn` companions and players are unchanged.
  - **Zone seams:** a route that steps over a zone seam and straight back three times is treated as a route failure,
    so the bot takes its usual way out instead of standing there (bots stood 10 to 40 minutes in Vindsaul Faste).
  - **Group stalls:** a member of a bot-only group stalled for 3 minutes, idle and in its leader's region, is moved to
    its leader (at most once in five minutes; never on a stablemaster ride, never in a player's group). The stall log
    names both zones and the member's recent trail.
  - **Ranged pulls:** a declined ranged pull no longer holds up the ordinary pull (a party of seven stood 30 minutes at
    its camp). A Necromancer pulls only when no one else can, a Cleric only before that.
  - **Mezzed enemies:** a mezzed enemy is left alone only while another free enemy is within 1,500 units; when the mez
    is all that is left, bots finish it instead of walking away.
  - **Darkness Falls:** a bot with no certified route out of DF probes less often after four empty tries and, after
    three minutes, goes to its realm's capital. A gamebot saved inside DF logs back in at its realm's entrance of the
    dungeon, not in a pack. DF stays open to solo bots.
  - **Keep doors:** an army inside a keep it just took can leave through the doors (four captured keeps held their
    armies: 661 route give-ups, then 213 teleports home).
  - **Checked:** their tests (`UT_AutonomousZoneSeamCycle`, `UT_SupportReachAndGroupStall`,
    `UT_DarknessFallsLoginEntrance`, `UT_FrontierTravelContracts`) and a new `UT_DarknessFallsExitHold`. Not yet seen
    in game here. Not taken: the travelling-Minstrel song change (it conflicts with our bot brain) and the forks'
    route-threat and zone-crossing changes, where our route code has diverged.
- **Water breathing potions work again** (by Rmarcoux86). Nereid potions and other water-breathing spells did
  nothing: their handler still used the old effect system, which the server no longer runs. They now let you breathe
  under water and swim faster for their duration, as before. Base game code; players only, as in the original handler.
  Not taken: the fork's default swim speed for everyone and its Nereid's Blessing buff. Checked: two new tests
  (`UT_WaterBreathingEffect`). Not yet tried in game.
- **The gamebots' camp list no longer breaks on a monster with no zone** (by MoomoocowPlus). One such monster stopped
  the whole list from being built, so every gamebot kept "Searching for a valid XP camp". It is now left out.

**Fixes**
- **An Animist turret no longer breaks when its target vanishes** (found in the 2026-11-04 overnight test). A pet's
  ordered target could be cleared at the very moment the pet checked it; that threw an error and the server removed the
  turret. The pet now checks the target it read, and a pet that has left the world has no target. Base game code.
  Checked: unit tests (`UT_PetOrderTargetGuard`); not yet seen in game.
- **The Epic Special Forces join a full raid after a wipe** (found in the 2026-11-04 playtest). Albion's automatic
  Summoner's Hall raid was made of small parties: 34 left the hub and four more joined on the road, 38 parties with 277
  bots. It wiped at Grand Summoner Govannon, and all three Special Forces parties were turned away ("the raid did not
  accept the party"), because a raid takes at most 38 parties and that limit counted them too. They now come on top of
  the realm's limits, as intended: the realm's 38 parties and 300 bots count only the realm's own bots, and the Special
  Forces have three parties and three formation places of their own beside the raid. Forced raids and dragon raids keep
  their rules. They also no longer need a "waiting for a group" goal to answer an automatic raid, and the realm doesn't
  top up their parties with its own bots. When a party is still refused, `REALM_RAID_SPECIAL_FORCES_FAILED` (and
  `REALM_RAID_FORCED_PARTY_RETRY`) now say why (`refusal="..."`), and raid and Special Forces log lines name Albion as
  Albion, not `_FirstPlayerRealm`. Checked: unit tests (`UT_SpecialForcesJoinFullRaids`); not played in game yet.
- Repairing an item never checked the crafter's skill: the gate was written as `(percentNeeded / 100) * craftLevel`,
  integer division, so the 50% a repair asks for became 0 and anyone could start one (with a 0% chance below 44% of the
  item's craft level). A repair now needs half the item's craft level in the skill, a repair in the trade window the
  full level, as intended (`Repair.HasSkillToRepair`, `UT_RepairSkillGate`).
- **The Herald counts the gamebots you invited into your guild** (from a r/daoc report). Its "Guilds of Note" read
  only the bot guilds' members, so a player's guild with 30 invited gamebots showed 1 member. It now also reads the
  invitations (`offline_world_bot_guild`) and counts each gamebot once; retired gamebots still don't count. Our own
  launcher bug. Checked: a new `HeraldPageTests` case on a small SQLite world (run on Linux here; CI runs it on
  Windows).
- **Bots talk twice as much by default: "How much they talk" is 2** (owner 2026-11-04, after the playtest measured it at
  your roster: 900 bots a realm, about 10 am, Albion heard 6.4 lines a minute across /faction, /lfg and /trade with 1,
  and about 15 with 2.5). An install that never changed the setting moves from 1 to 2 once, at its first start; one where
  it was set in the launcher keeps its value. Each channel's ceiling still holds, so it can't become spam. Checked: unit
  tests for the default, the one-time move and a value set in the launcher; the levels themselves were measured in game.
- **Creating a character of a race this server doesn't offer is refused cleanly** (found in the 2026-11-04 playtest; also
  offered to the base game). The game client shows every race up to Labyrinth, but the server's world is Classic and
  Shrouded Isles: Half Ogre, Frostalf, Shar and the Minotaurs have no starting stats. Creating one (a Half Ogre wizard)
  threw an exception in the stat check; it is now an invalid character with a log line that says why. The player still
  only sees the character not appear: the character screen has no message for it. Checked: a new
  `UT_CharacterStatValidator` case.
- **A GM's level or rank set by command is no server first** (found in the 2026-11-04 playtest: `/player level 50` on a
  GM test character announced "the first on the server to reach level 50" to every realm, put it in the Herald and set
  off a wave of grats). Server firsts now skip GM and admin accounts, the launcher's "Make Me a GM" included. Not
  covered by a unit test (it needs a logged-in client); checked by reading the code.
- **"an elementalist", not "a elementalist"** (same playtest: "first to 50 as a elementalist"). The server-first text and
  every bot line with "a {slot}" use "an" before a vowel. Checked: new `UT_AutonomousBotVoices` cases.
- **Bots no longer sell their level 50 starter kit in chat** (found in the 2026-11-04 playtest: "wts offline level 50
  reaver 3/10 pst" in /trade). The new trade and voice lines named the most valuable item in a bot's bags, which for a
  launcher-made level 50 is its starter kit. They now skip it, as the Realm Exchange adverts already did.
- **"What does your guild do?" gets an answer about the guild, not just its name** (same playtest: a tell asking it drew
  "Salisbury Brethren"). Each guild has one focus (RvR, levelling, a bit of everything...), so every member tells the
  same story; "what guild are you in?" still gets the name. Checked: a new `UT_ChatResponsive` case.
- **The 1.1.2 server failed to start on a real game: the package now ships the server's own libraries** (found in the
  2026-11-04 playtest, before release). Save safety and the network defaults changed `CoreDatabase`, `CoreBase` and
  `CoreServer` as well as `GameServer.dll`, but the package carried only `GameServer.dll`, so the new server stopped at
  start beside the released libraries ("Could not load type 'Result' from assembly 'CoreDatabase'"). CI didn't see it:
  its tests build everything together and its installer tests use a fake game. The package now has a `core\` folder:
  `INSTALL.cmd` replaces each of those libraries where the game has one (the original or a Living Server build only,
  backed up and checked like `GameServer.dll`), `ROLLBACK.cmd` and `PREPARE-FOR-UPDATE.cmd` put them back, and Setup's
  safety copy before an Offline DAoC update includes them. Checked: new installer checks (each library replaced where the game has one and nowhere else, rollback, -ToRelease, a hand-changed library refused), and on the playtest PC the
  repacked build installed over the broken one and the server started.
- **The launcher says what to do when the server is stuck on STARTING** (a Reddit report, 2026-11-04: "stuck at
  Starting…, Enter Realm greyed out; yesterday it worked"). When this game's server has run for 3 minutes without
  answering, the footer says so (most likely a server left from an earlier session, or one stuck while loading) and
  points to STOP SERVER, which already offers to end a server this launcher didn't start, and to the log. The other
  known cause, Windows leaving the server out of its listener list, was fixed in 1.1.1 (issue #32). Checked: a new
  `ServerPortProbeTests` case (Windows CI).
- **The launcher says when the server stops by itself.** A server that stopped while starting left the launcher on
  STOPPED with no word, which looked like START SERVER doing nothing. The footer now says it stopped, with its exit
  code, and that the end of `runtime\logs\server-console.log` says why.
- **The installer installs into the game folder it sits in, even with other installs beside it** (found in the
  2026-11-04 playtest). With the build inside one game and other `playable*` folders next to it, `INSTALL.cmd` stopped
  with "More than one game folder found". The folder the build sits in now wins; the search of nearby folders is only
  for a build placed elsewhere. Checked: a new installer check with two side-by-side fake games.
- **Ctrl-click and Shift-click on the launcher's "+ Lv." buttons work from remote-desktop and automation tools** (found
  in the same playtest). They read the keys as they are when the click is handled, and a fast click had already let go
  of Ctrl, so one bot was made instead of 100. They now read the keys as they were when the click arrived, and either
  Ctrl or Shift key counts. Checked in the playtest: a remote Ctrl-click made 100 bots.

**Updates from the launcher, and Setup installs the newest package**
- **Updates from the launcher** (approved by the owner on 2026-11-03).
  - **The check:** when the launcher opens (at most once a day), or when you click **Check for updates** beside its
    version, it makes one request to GitHub's releases API for Living Server's newest release, with a generic
    User-Agent and nothing about you or your game. Prereleases aren't offered. Any failure is silent unless you asked.
    **Check automatically** beside the link turns the daily check off.
  - **The offer:** when a newer build is out, START SERVER reads **GET UPDATE**, and the line beside the version shows
    the build and the first line of its release notes. GET UPDATE asks "Update to Living Server Beta X? Your
    characters and world are kept" with **Install**, **Later** (START SERVER is back for this session) and **Skip
    this version** (not offered again until a newer one; Check for updates still shows it).
  - **The install:** only with the server, both clients and any other launcher closed. The package downloads to
    `runtime\updates\` with its progress shown, and is used only if it matches the SHA-256 digest GitHub publishes
    for the file; a missing or wrong digest is refused and the file deleted. The zip must hold one
    `LivingServer-<build>` folder whose `BUILD-INFO.txt` names the release's build. It is unpacked to a staging folder,
    then moved into the game folder as a player would put it. The launcher then closes and `apply-update.ps1` waits
    for it, runs the package's `INSTALL.cmd` without elevation (the same checks, backups and verified copies as a
    double-click), checks the result and opens the launcher again, which says how it went. `ROLLBACK.cmd` in the new
    package folder undoes it, as before. Every step goes to `runtime\logs\launcher-updates.log`.
  - **Kept:** the downloaded package and the one before it stay in `runtime\updates\` (older ones are removed), and
    the earlier package folder stays in the game folder, so either can be installed again. Your database and saves
    are never touched. `OfflineDAoC.exe --apply-update <zip> --sha256 <hex>` does the same without the window (tests
    and support).
  - **The launcher knows its build:** it is stamped at build time from the server's `ForkBuildInfo.BuildName`.
- **Setup installs the newest Living Server.** Before installing, Setup asks the same API for the newest release. If
  its package is newer than the one inside Setup and made for the same Offline DAoC release, Setup downloads it,
  checks it the same way and installs it. Offline, a rate limit, a bad or missing digest, or a package for another
  release: Setup installs the package inside it and says why on the progress page's first step, which always names
  the build being installed. `--no-update` skips the question (tests and offline installs); `--info` logs which
  package it would install.
- **Checked:** 69 new launcher tests (`Tests.Update*`: versions, the recorded release JSON, digests, the once-a-day and
  skip rules, the package checks, the download and staging with a fake network) in the gated CI step. On Windows
  CI: the launcher's update path end to end on a fake game folder (the published 1.1-beta package installed, then
  1.1.1-beta applied with `--apply-update`, refusals for a wrong or missing digest and a running game, and
  `ROLLBACK.cmd`), and Setup's choice of package against the live API with wrong and missing digests. Not yet seen on
  a player's PC: see [docs/KNOWN-ISSUES.md](docs/KNOWN-ISSUES.md).

**Save safety and network defaults** (technical audit of 2026-11-03, §2 and the co-op bind defaults). Nothing
here changes gameplay. The WAL settings and the backup design are ported from lometur's HearthDAoC fork.
- **The world database always opens in a crash-safe mode.**
  - **The problem:** the server's code default opened the database with the journal off and syncing off
    (`Journal Mode=Off;Synchronous=Off`, unpooled). If the server crashed or the PC lost power during a save, the
    world could be corrupted. Setup's world import puts the file in WAL, but a server with that default switched it
    back on each open.
  - **Now:** whatever `serverconfig.xml` says, the server opens the world in WAL with `Synchronous=Normal`, pooled
    connections and a busy timeout of at least 10 s. The file isn't rewritten, so your other settings stay. The log
    says what it changed (`DB_SAFETY ... changed="journal Off -> WAL; ..."`) and confirms the mode
    (`DB_SAFETY journal_mode=wal confirmed`). At a clean stop the server moves the WAL into the database file.
  - **Why pooled:** the server's fast read path for WAL needs it. In a test (1,000 saves, then 1,000 reads), pooled
    WAL took 0.7 s and 0.45 s, unpooled WAL 3.8 s and 1.0 s, and the old unpooled journal-off default 1.4 s and 1.2 s.
- **An integrity check at each start.** The server runs SQLite's `quick_check` before it loads the world, and logs
  the time (`DB_INTEGRITY mode=quick_check result=ok ms=...`). On a 537 MB test database it took 0.5 s (the full
  `integrity_check` took 4.5 s). The server setting `db_integrity_check` picks `quick` (default), `full` or `off`. A
  damaged database is logged as an error saying how to restore a backup, and daily backups pause so they can't
  replace a good one. The server still starts.
- **Daily backups.** Once a day, while the server runs, it writes an online copy of the world to
  `runtime\data\backups\opendaoc-<UTC time>-daily.sqlite3.db`.
  - The copy is taken with SQLite's backup API while you play. It's written under a temporary name, checked with
    `quick_check`, and only then renamed. The oldest daily copies beyond the last 7 are deleted afterwards
    (`db_backup_keep`; 0 turns daily backups off). A failed backup deletes nothing.
  - It runs on a timer thread, never on the game loop. The first look is 5 minutes after the start, then hourly, and
    it skips when the last daily copy is under 24 hours old or the disk is short of space. The log says
    `DB_BACKUP result=written|skipped|failed` with the reason.
- **The installer backs up your world before installing.** `INSTALL.cmd` copies `runtime\data\opendaoc.sqlite3.db`
  (with its `-wal` and `-shm` files) into its backup folder (`runtime\server\deployment-backups\fork-build-<time>\data`)
  and checks the copy by SHA-256 before it replaces any file. It stops if the disk can't hold the copy. The newest 3
  installs keep their database copy, and older installs keep only their server files. `ROLLBACK.cmd` is unchanged:
  it never puts the database back, because that would undo your play since.
- **The realm event ledger moved to `runtime\data`.** `realm-event-records.sqlite3` (the Herald's and the Realm
  Records tab's history) was in `runtime\server`, where backups and progress transfers missed it. The server moves it
  at its first start (`REALM_EVENT_LEDGER moved`). If an older build wrote a new one later, the server merges it in
  without duplicates. The launcher reads it from either place. The progress importer now carries it
  (`progress-policy.json` `ProgressFiles`).
- **Network defaults reach this PC only.**
  - **The problem:** the code defaults listened on every network (`0.0.0.0`) with UPnP on, so a lost
    `serverconfig.xml` came back with the router asked to open TCP 10300 and UDP 10400 to the internet. The server
    never removed those router mappings.
  - **Now:** the defaults are `127.0.0.1` with UPnP and DetectRegionIP off. A missing config is written that way, and
    a setting a config leaves out takes the safe value. The UPnP mappings a server added are removed when it stops
    (`UPNP_MAPPING removed ...`), and mappings it didn't add are left alone.
  - **Your own config isn't changed.** When it listens beyond this PC or has UPnP on, the log warns at each start
    (`NETWORK_EXPOSURE Your server can be reached from other computers...`) and says how to turn it off.
  - `build_release_037.py` sets and checks `127.0.0.1` and UPnP off in every shipped `serverconfig.xml`, and
    `assemble_release_037.py` checks the finished package again.
- **No more "database is locked" on a busy read.** Found by the release-gates work: about 3 runs in 50 on Windows,
  the pooled WAL test failed when a `SelectAllObjects` read met a brief lock and gave up in under a second.
  - **The cause:** the read path that `SelectAllObjects` and every other `WhereClause` select use had no retry, unlike
    the server's other read path. Connections also had no native busy wait (SQLite's default wait is zero).
  - **Now:** that path retries a brief lock conflict the same way, up to 40 times with a short back-off (about 1.7 s
    in all), and other errors are not retried. Every SQLite database the server opens waits at least 10 s for a
    lock (`BusyTimeout`), even with a `serverconfig.xml` that sets none.
  - **Cleanup:** the command is disposed before its connection, and connections are disposed as well as closed.
  - **A second race in the same test:** a query clause shared between threads (the empty clause every
    `SelectAllObjects` uses) was marked ready before its parameters were set. A second thread could then read no
    parameters and fail with a NullReferenceException. This was seen once in a 50-run loop before the fix. The
    parameters are now set first.
  - **Checked:** 6 new tests (`UT_SqlitePooledReadContention`: the retry, its bound, the busy wait, disposal, a clause
    read by many threads at once, and three rounds of pooled readers and writers on every read path). A local loop (Linux) ran these with the two existing WAL pool tests: 49 of
    50 runs passed before the fix (the one failure was the NullReferenceException), and 60 of 60 passed after it, run
    as three processes at once to add CPU contention.
- **Checked:** these are automated tests only. 17 new server tests (`UT_WorldDatabaseSafety`: the connection string,
  WAL on a file that was journal-off, the integrity check on a damaged file, backup names, rotation, the
  once-a-day rule, the online copy, the stop checkpoint, the ledger move and merge, the network defaults and
  warnings, and the UPnP clean-up with a fake router). There are also 8 new installer checks and 5 Python tests for
  the release config. None of this has run on a real install or in game yet.

**Release gates**
- **Release gates** (audit 2026-11-03, §8.6). CI also runs on pushes to main. A `versions` check fails when
  README.md, the build name and this changelog name different releases (README.md still said Beta 1.1; it now says
  1.1.1, without the test count that went stale). A release workflow builds Setup from a published release's package,
  runs the installer's end-to-end tests, attaches the exe and puts its SHA-256 in the notes; see "Releasing Living
  Server" in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md). The SQLite tests' cleanup no longer fails on Windows while a
  closing connection still holds the file (PR #56): each test has its own temporary folder, deleted with retries.

## Beta 1.1.1 — 2026-10-31

Fixes only, for the first week after Beta 1.1: the community's first reports (the party that wouldn't disband, the
launcher stuck on STARTING), base classes' weapon styles, quieter bot performers, and the new client's quest marker,
the first new-client file Living Server ships. Dragon raids regrouping after a wipe and the quest fixes (kill credit
in a bot group, one count per kill, hand-ins above a quest's level, the journal, quest XP by level) come next, once
they are played. The plan is [docs/RELEASE-TRAIN.md](docs/RELEASE-TRAIN.md).

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
  - **Checked:** four new tests (`ServerPortProbeTests`); the launcher suite (127 tests) passes on CI. The reporter
    built the branch and confirmed it on the PC that shows the problem (2026-10-30): the launcher went from STARTING
    to RUNNING and enabled Enter Realm while Windows still left the port out of its table. An in-game login on that
    PC wasn't tried yet.
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
- Dragon raids don't call them yet (they do from Beta 1.1.2, above).
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
