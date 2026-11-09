# Living Server changelog

What this fork adds to shadowofze's Offline DAoC. The base game's own changes are in
[CHANGELOG.md](CHANGELOG.md). The newest version is first.

Up to 0.37-se.2 the fork was called the **Social Expansion**, and "Living Server" was the name of one
of its feature groups (bots remember you, grats, rezzes, alarms, nemeses, schedules, breaks, body
language). From Beta 1.0 the whole add-on is the **Living Server** and that group is **Server life**.
Older entries below keep the names they were written with.

## Beta 1.1.3 — 2026-11-08

Started as a hotfix (/faction chat that holds together, out of Xanxicar's stomp, Scurceol Hyrde's orbs shot down from the
floor, two Darkness Falls exits, Setup waiting out a busy download server) and grew after the owner's overnight checks of
the release candidates on 2026-11-09 (owner: "Hold 1.1.3 and fix everything you identified", "You need to come up with
fixes for ALL of that"): bot chat that sounds like a real server, bots that notice the player, raids that don't stall,
keeps defended in time, quiet nights, fewer stalls. How it was tested, and on which build, is in the release notes
([docs/RELEASE-NOTES-1.1.3.md](docs/RELEASE-NOTES-1.1.3.md)).

**/faction that holds together** (owner 2026-11-08, watching 1.1.3's /faction: "faction feels disconnected... The chats
MUST be top notch"). Checked by automated tests and a simulated evening, then on the owner's PC: the chat checker
read 68 minutes of an evening on the second release candidate (6a83c03) and 34 minutes on the third (b72716e, the same
chat code) and passed both.
- **Conversations in order, each speaker in its part.** Every line of a bot conversation (a question and its answers, a
  story about a death, the replies to a remark) was its own timer, set when the conversation was planned. A line held
  back 2-4 s because the channel had just carried one (up to three times) then came after the line meant to follow it,
  and a line whose speaker was fighting was dropped while the rest still played: "main line first" before the question
  it answers, the asker's thanks before the answer, "sup" last; "kk inv" before "what lvl r u?". Now a conversation is
  said one line after another, each timed from the line before it. A line whose speaker can't say it (fighting, gone)
  ends the conversation, so nobody thanks an answer that never came; a third bot's aside is just left out. A short reply
  ("which part?", "ty") is not said once three other lines have scrolled its question away, and an "anyone?" two minutes
  later only if its speaker said nothing else meanwhile. At most three conversations run in a realm's /faction at once.
- **Answers that are true of the bot.** "need a tank for sh barrows" drew "sec, me" and "cab, 50" from a cabalist. A
  group ask is now answered only by a bot that fills the role it asks for (tank, healer, crowd control, puller, damage, or
  a named class) and is in its levels (the range it names, else within 6 of the asker); "any reavers on?" is answered
  "me" by a reaver only. Asks name only classes of the asker's realm (an Albion "any druids on?" was possible).
- **Follow-ups that fit.** The asker no longer asks the answerer's question ("solo or grp?" after "any reavers on?"); it
  asks the question it had ("what do u put points in at 50?"). A gear, crafting or epic question no longer runs on to
  "which part?" and "the map helps"; only quest, hand-in and bind questions get a "where exactly?", answered with a place.
  Nobody asks a level or class the answer already gave. An answer that asks back ("where at?") is answered by the asker,
  with no "ty" to a question. In a death story "where?" comes only when the teller didn't say where, and the teller then
  says where. A trade post answered "gl selling, someone will want it" no longer gets "ill keep it then".
- **Reactions in proportion.** A bot's revenge or nemesis line drew three to eight reactions over a minute and a half,
  and two of them ran into each other ("that stinks" and "nice!" to two lines at once). Now a bot's own moment draws one
  or two reactions within 20 s, realm news (keeps, relics, raids, Darkness Falls) three at most (four from 300 bots a
  realm) within a minute, one burst at a time in a realm, each from a bot free to type, and a reaction is left out once
  four other lines came after what it reacts to. Guild threads are said in order and answered by guildmates who fit too.
- **One "oof" in ten minutes.** A short reaction ("oof", "rip", "lol rip", "gz") a realm's /faction heard in the last ten
  minutes is picked again or left out, in stories, bursts, threads' answers and single reactions.
- **/faction keeps to its ceiling, whoever speaks.** On the owner's evening /faction ran at 13.6 lines a minute for ten
  minutes (minutes of 17-20) against its ceiling of 10: unprompted lines stopped at the ceiling, but replies, stories,
  bursts, nemesis and revenge lines, news, debates, rumours, bonds, gold spammers and Leeroy's friends didn't. Now every
  bot line in /faction passes one gate: it is said only if it fits under the realm's ceiling (the rate it would make, over
  two minutes, as the server measures it). A conversation's line waits for room like a busy speaker, a debate's beat waits
  (the scene owns the channel, so it drains), and a line that starts more (a rumour, a bond, a nemesis line and its burst)
  starts nothing when it is held. Two kinds pass the gate, tagged in the log so a check can tell them apart: `src=event`, a
  realm's big moment said on purpose (a keep or relic taken, a raid's end, Darkness Falls, a server first, the raid's
  Leeroy), whose burst is itself capped at three reactions within 40 s; and `src=player`, a line to or about the real player
  (bots answer you first). With bot voices on, a server first no longer adds its separate cheers on top of its burst.
- **A debate, a storyline episode or a dunce's roast plays to its end.** While a scene plays it owns the realm's
  /faction: every other line but an event or one to the player yields to it, so each beat finds room under the ceiling
  within seconds. A beat waits for room three seconds at a time (an optional beat isn't re-rolled meanwhile); only after
  two minutes is a scene cut (`BOT_DEBATE_END reason="no room in /faction"`), and a dunce who already spoke still sends
  its parting tell and rage quits then. Checked on every scene in the corpus, on a realm already at its ceiling with a
  keep falling mid-scene.
- **You are answered during a scene too.** A line to or about you is never held by a scene or the ceiling, and "where do i
  buy a better staff for my elem?" (no answer at all in the live test) now gets a true one: the Realm Exchange in your
  capital, or the crafter who makes it (a fletcher for staves and bows, a weaponcrafter, an armorcrafter or a tailor).
- **No rush when a scene ends.** For two minutes after a scene a line that starts something waits ten seconds after the
  line before it (three seconds otherwise), and /lfg and /trade no longer wait for a /faction scene at all (the live test
  heard 7 lines in 7 s as a debate ended). A roleplayer held back tries again in a few seconds.
- **`/lstest roleplayer` and `/lstest spammer` say when the line really comes:** within a few seconds, once the scene
  playing now is over, or as soon as /faction has room.
- **A scene's part keeps its bot (after RC1).** The owner's check of RC1 (d30ba0fa, 2026-11-08) heard the dunce of
  `dunce_pause_game` start as Iagoys and go on as Yno, who rage quit: Iagoys was in a fight for over 45 s, and the scene
  gave the part to another bot. Now a part that has spoken is never given to another bot, in a dunce scene, a storyline
  episode or a debate: a speaker fighting, casting or dead is waited for (45 s) and then says its line anyway, as players
  typed between pulls; one that left the game ends the scene (`BOT_DEBATE_END reason="role A spoke and its bot left"`).
  Tests (every scene in the corpus, with its first speaker busy for minutes, and gone), then on the owner's PC with
  RC2 (6a83c038): `/lstest dunce` played `dunce_pet_warrior` with one speaker from start to rage quit.
- **A gold spammer, a rumour or a roleplayer that is due gets its turn (after RC1).** In the same check
  `/lstest spammer` logged `GOLD_SPAMMER_START` and the spammer said nothing for over two minutes: it came as a debate
  began, /faction stayed at its ceiling after it, and every held ad was spent with the next tried 18-48 s later.
  `/lstest rumour` said "no free bot of your realm had a line for it" when /faction had held the line, and a held rumour
  was lost. Now a held ad, rumour or in-character line keeps its turn and is tried again every few seconds; its lines still
  count against the ceiling. Time behind a scene doesn't count; after three minutes without room it gives up
  (`LIVING_FEATURE_DROPPED`). The log says when one is due (`LIVING_FEATURE_DUE feature=spammer|rumour|roleplayer ex=`),
  `GOLD_SPAMMER_START` comes with the first ad really said, and a spammer that never spoke is not banned or cheered.
  `/lstest rumour` tells apart a scene playing, a full /faction, no free bot and no written line, and the first free bot
  with a line tells it (only one bot was asked before). The rumour's replies, the spammer's reactions and the
  roleplayer's mockery share the line's exchange id. On the owner's PC with RC2 (6a83c038): `/lstest spammer` spoke
  after a 7 s wait and ran its five ads with reactions; `/lstest rumour` said honestly that no rare named was near.
- **No wall of "released, running back" at the bind (after RC1).** Standing at Camelot's bind the owner heard about 25
  lines in 45 s from different bots ("released, running back", "at bind, omw back", "no rez, released. coming back to
  you"): a gamebot's word to its own group about its way back went out in /say to everyone near. These lines (released,
  horse, off the horse, back, still on the way) now go to the bot's own group, and only when a player is in it; a group of
  bots says nothing. /say lines carry their ceiling (`cap=`), and `chat_audit.py` prints the /say lines a minute and fails a
  log with a minute of /say above its ceiling around the player. On the owner's PC with RC2 (6a83c038), at a busy
  bind: 1.3-2.6 /say lines a minute.
- **One greeting per bot per meeting (after RC1).** Cabowyn said "hey Manawatch! good to see you again" and a second later
  "hey hey Manawatch, City of Camelot treat you well after i left?": a GM test had cleared the greeting cooldowns while the
  first greeting was on its way. A bot that greeted a player, or is about to, doesn't greet them again for ten minutes,
  whatever cleared the cooldowns; `/lstest greet` says so. Tests only.
- **A deed names a place it could happen (after RC1).** A forced deed read "saw Manawatch's group take a named monster in
  City of Camelot". A real deed names the zone where it happened (the monster's or the player's body); `/lstest deed` names
  where the GM stands only if it could happen there (no capital; a streak or a gank only in the frontier), else a hunting
  zone of the realm or a frontier zone. Tests only.
- **A death story's opener keeps the ten-minute rule too (after RC3).** On the owner's RC3 evening (b72716e, 34 minutes)
  different bots opened a death story with "dead. again." seven times, twice within a minute. A story line of three
  words or more is told once an evening, so once the longer openers had each been told only the two-word one was left,
  and the teller's own short line was let past the channel's ten-minute window (only the replies kept it). Now the
  teller's short line keeps the window as well: the story picks another opener, and with none left it isn't told.
  Tests only.
- **A frontier death isn't told as a monster (after RC3).** In the owner's RC2 and RC3 evenings most death stories from
  the frontier read "siuses in murdaigean hate me", "train of sneakywarriors, im dead" or "killed by xdemonxes at 27": the
  story took the bot's last target, an enemy player, as a kind of monster and made it plural. Named creatures came out
  the same way ("died to summoner roesias again"). Now only a kind of monster ("thrawn ogre miner") is talked about as
  one; a person, a guard or a named creature is not, in stories and in the bots' other lines. A death in the frontier is
  told as one ("got ganked lol", "zerged in emain", "stealther got me"). Tests only.
- **Realm news in a player's words, not the herald's (after RC5).** On the owner's RC5 evening bots said the realm's
  announcements in /faction word for word: "Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel; at least
  93 must arrive before assault.", "The Krondon expedition is advancing with 200 staged level-50 adventurers.", "Fighting
  has broken out at Dun Crimthain. There is an opportunity to intervene." and "Warbands are gathering to assault Nottmoor
  Faste. Join them at the siege camp". When a bot says one now, it says it the way a player typed it, short and in
  lowercase, with the names as chat shortened them and no head counts ("krondon raid forming at diogel, need 50s", "fight
  going at crimthain, anyone?", "warband going to nottm, siege camp"), in several ways for each announcement. Bots
  telling the news in their own words shorten the names too ("we lost ailinne to hibs", not "we lost Dun Ailinne to
  Hibernia"). The announcement at the top of the screen, and a notice with no bot to say it, read as before. Tests only.
- **Reactions to the realm's news vary over an evening (after RC5).** "ugh, that one again" (RC3 and RC5), "oh well not
  my keep" and "lol of course" came back to the next keep lost. A reaction of three words or more is now heard once in a
  channel in three hours, however it is punctuated, and each kind of news (a keep taken or lost, a siege held or failed,
  a relic, a raid's end, Darkness Falls) has about twice as many reactions to choose from. A burst that runs out of fresh
  reactions is shorter, never longer: one short reaction per ten minutes and the reaction caps hold as before. Tests only.
- **Debaters type, they don't narrate (after RC5).** Next to "lol rip", some debaters wrote like a book: "People said the
  same before the dragon lairs. One winter they were just there, all three realms, same week." A debate line is now typed
  in its speaker's own style and habits: a long turn goes out as two or three chat lines split where a player would hit
  enter, no writer capitalises the second sentence of a line or ends it with a full stop, and a terse writer takes the
  shortest way the scene gives to say it, without commas. What each speaker says is unchanged. Tests only.
- **More ways to tell a frontier death (after RC5).** "got ganked lol" came twice in 13 minutes on RC5. A frontier death
  story now has hundreds of openers (ganked, zerged, stealthed, caught at the milegate or the bridge, ran into a group,
  died to a guard...), more answers to "where?", "by who?" and "how many?", and the teller's lines are kept for the
  evening however short. "same happened to me in ..." names a frontier zone, an enemy realm's only as "their frontier":
  RC5's "ganked in jamtland mtns" drew "same happened to me in dales of devwy", a hunting zone. Tests only.
- **A deed names the monster and where (after RC5).** RC5's `/lstest deed solo` read "not lying, i just saw Manawatch kill
  a named monster, no help at all". A witness now names where it happened in most of its lines, with the zone as chat
  wrote it ("black mtns"), and names the real monster; the forced test takes the targeted or nearest named monster and
  says so when there is none. Tests only.
- **The checker finds lines in system voice (after RC5).** `chat_audit.py` counts bot lines in /faction, /say, /lfg, /trade
  and /broadcast that read as a game announcement ("level-50 adventurers", "There is an opportunity", "Join them at", a
  semicolon, a formally capitalised paragraph of more than 15 words), with examples under `--show`; one is enough for
  `VERDICT FAIL`. It also counts, for information, the same reaction of three words or more again within an hour. It
  fails the owner's RC3, RC4 and RC5 logs and passes the simulated evening of this build. Tests only.
- **Bots answer each other's questions, and /lfg and /trade get replies (after RC5).** On the owner's RC3 evening (b72716e,
  151 minutes) 71 of the 106 questions bots asked in /faction got no other line at all, and on RC5 (b8f30f9, 30 minutes)
  15 of 21; counting only replies from someone else within a minute, 86 of 106 and 17 of 20 went unanswered. Half of them
  were written with no answers ("where's the paladin trainer", "what spec for a 50 inf?", "any stealthers out?", "fastest
  way to darkness falls?"); the rest were dropped by a 30% chance of no answer, an answer that could be said only once an
  evening, or an answerer who was fighting by the time its answer was due. Now every question has answers that fit it (the
  trainer in the capital, the class's usual spec and weapon, drops or spellcrafted gear by level, back to whoever gave the
  quest, kills and keeps for realm points), and where the world has the fact the bot says that: the real class trainer and
  its zone, the stable master with the route, the camps bots of that level use, an enemy stealther the realm can see, who
  is really at a keep. A question is always answered when a bot can answer it; one that would find the channel at its
  ceiling or with three threads running isn't asked. A heard answer comes back after 30-60 minutes, another bot that can
  answer says a busy answerer's line when it says nothing about that bot, and a regular answers a question first two times
  in five, so the regulars still carry about half of /faction. A player's "where do i get my spells
  trained?" (RC2: "no idea") names the trainer, and a player's spec, crafting, quest, realm point and epic questions, in
  /faction, /say or a tell, get the same answers. In /lfg and /trade (RC3: 260 and 213 posts, no reply to any) a share of
  posts get a short reply from a bot it is true for ("x" or "still need a healer?" from a solo healer in the levels asked
  for, "how much?" from a bot that wears that armour, "/t me, have one" from a bot carrying it), the poster's answer is
  true (its asking price, its real spec, "ya, 1 spot" only while its group has room), and a few minutes later "nvm full",
  "got a grp" or "sold" only when that happened. Every line still waits for room under its channel's ceiling.
  `chat_audit.py` counts bot questions with no reply within 60 s (`VERDICT FAIL` above 25%; RC3 and RC5 fail) and reports
  the share of /lfg and /trade posts with a reply. On the simulated evening 80 of 90 questions are answered within a
  minute (34 of 114 before) and the checker passes. Tests only.
- **"ya, 1 spot" only from a party that is still recruiting (after RC5).** The /lfg replies above had the poster say "ya,
  1 spot" or "ya, still room" for any bot party under eight. Bot parties are formed at their full size and never take
  anyone later, so a party has a seat only while it is still assembling with seats free; and "still need a healer?" is
  yes only while nobody in it fills that role. A party that set off says "nope full, sry"; one still forming that has its
  healer says "got a healer already, sry". Tests only.
- **An item is paired only with a class that can use it (after RC5).** On the owner's PC: "99% adroit focus staff any good
  for pally?", "95% adroit focus staff any good for friar?", "99% earthshaker chain boots any good for sorc?", "99%
  matterbender medium shield any good for infil?" and "wtb a shield for a 30 cabalist pst". A bot asks about its own item
  only when its class can use it, by the server's own rule for the live bot (the class's real weapon and armour skills)
  and a focus staff only for a caster; armour it wants is the weight its class wears at its level, by the game's item
  generator ("armor for a 29 cabalist" is cloth, a 15 paladin's is chain); arrows only for an archer; "my old ..." only
  for an item it could have worn; a written line that pairs an item with a class that can't use it is skipped.
  `chat_audit.py` counts "an item paired with a class that can't use it" (FAIL). Tests only.
- **One story per bot in its group posts (after RC5).** Thorroir, a 50 minstrel, said in /broadcast within twelve
  minutes "minst lfg 50, x me", "lfm barrows 47-50", "lf1m dps barrows pst" and "lf1m 47-50 xp grp stonehenge", and
  later "any minsts around? need tips". A bot now keeps one story for 30 minutes across /lfg, /broadcast, /trade,
  /faction and /say: it looks for a group or forms one, not both, and its posts name one dungeon, by the word it first
  used for it ("barrows", not "barrows" then "stonehenge"). When its state changes it says so where it posted, once ("nm got a
  grp", "nvm, full", or "nm, heading out" for a party that set off short of eight; one closing per post, whether the
  /lfg reply's closing or the bot's next post says it) and may then start a new story. A WTS line may still repeat every few minutes as a macro, as players'
  /macro did in 2003, but a bot runs one macro at a time and it stops once the item is no longer in its bags; it only
  sells what it carries. `chat_audit.py` counts "a speaker looks for a group and forms one within 30 min" and "a
  speaker's group posts name two dungeons within 10 min" (FAIL). Tests only.
- **Group posts follow the bot's real group state (after RC5).** The server log shows Thorroir in a group from 05:36
  when it posted "minst lfg 50, x me" at 05:38. Now only a bot out of a group that wants one (group matchmaking holds it,
  or it is out for RvR) posts "lfg"; a bot on a solo task doesn't, and a bot in a group never does. Only the leader of a
  bot party that is still assembling with seats free posts "lfm", naming the role the party really lacks (healer, tank,
  cc, else dps), its real camp and its members' levels; the post stops when the party sets off. Bot parties are formed at
  their full size and not backfilled, so such a post is the assembly's last call. `chat_audit.py` reports, for
  information, an "lfg" from a bot the log shows grouped (AUTONOMOUS_GROUP_FORMED until its TASK_ENDED or NO_SHOW).
  Tests only.
- **A question fits the asker (after RC5).** A level 50 paladin asked "where's the paladin trainer", a 50 cleric "where's
  the clr trainer", a 50 minstrel "any minsts around? need tips". Only a character of level 20 or less asks where its own
  trainer is, only one under 40 asks for tips on its own class, and a 50 asks for no xp group ("50 minst lf xp grp"), no
  xp spot and no "still 50 lol"; every level a question names is the asker's own. `chat_audit.py` counts "a 50 asks where
  its class trainer is or for tips on its own class" (FAIL). Tests only.
- **A PvE line names no enemy realm's zone (after RC5).** Albion bots in the Midgard and Hibernia frontier said "where do i
  hand in the uppland quest?", "which stable for uppland?", "grinding odin's gate at 29", "back in uppland", "where do i
  bind in jamtland mtns" and "dread commanders in jamtland mountains hate me". A quest, stable, bind, camp, grinding, xp
  or duo line, and a monster death story, now names only a zone of the bot's own realm (a death in an enemy frontier is
  "in frontier"); talk of the frontier itself may still name it ("inc", keeps, relics, "lfg 50 merc, odin's gate").
  `chat_audit.py` counts "a PvE line names an enemy realm's zone" (FAIL). Tests only.
- **The checker reads "arms" in a trade line as armour (after RC2).** On the owner's RC2 evening (6a83c038, 68 minutes)
  `chat_audit.py` failed only on "wtb leather arms 4 26 inf", an Infiltrator 26 buying arm armour, read as an armsman
  claim ("class claim not the speaker's", "one speaker claims two classes"). A class word that is also an armour slot is
  now the item in a trade line (wtb, wts, selling, ...) or after an armour word (leather, chain, studded, ...); "arms 45
  lfg" is still an armsman. With it the RC2 log passes. A checker fix only; the server is unchanged.
- **Replies to the player each have their own exchange id.** RC1 tagged them all `ex=0`, so the checker read them as one
  conversation and reported it out of order. `chat_audit.py` also treats `ex=0` as no exchange (for RC1 logs), and now
  counts features that were due and never spoke in /faction: one is enough for `VERDICT FAIL`. The simulated evening has
  a spammer every 40 minutes, a rumour every 20 and a roleplayer's line every 15, each waiting for its turn, and none silent.
- **One reaction in ten minutes, whatever says it.** The ten-minute window also holds back a pure reaction ("lol", "been
  there", "nice going") at the moment it is sent, from any path, not only the ones that plan around it. A conversation's
  line comes at most about 40 s after the one before it, or not at all.
- **Every /faction line carries its ceiling** (`cap=6.9`), so a log can be checked on its own.
- **No more "Fair enough. Safe travels."** It came back 30 times in one night: a fixed line from the oldest reply path,
  outside the no-repeat memory every written line has, like its formal replies ("A merchant price can provide a
  reasonable minimum value."). With bot voices on that path now says only written lines, or nothing.
- **Realm Exchange adverts now and then.** A seller advertised 70% of its listings, only three minutes after the last
  advert or news line (five in 29 minutes on the owner's evening). An advert is now an unprompted /faction line like any
  other: only while /faction is under its target, and one a realm in 15 minutes. One that no written line fits says "anyone
  want ... ? make me an offer" or "pc ...?" (was "Does anybody want to make me an offer on ...?") and gets written trade
  answers.
- **Every line names its source.** `CHAT_LINE` rows now say which feature said a line (`src=field`, `thread`, `story`,
  `reply`, `burst`, `reaction`, `news`, `debate`, ...), and inside a conversation its id, the line's place and the
  speaker's part (`ex=12 i=2 role=answerer`), with the speaker's real class and level (`cls="Cabalist" lvl=50`). `via=`
  skips the delivery wrappers, so it names the code that chose the line (most lines showed
  `BroadcastFactionAsWritten`).
- **Measured:** `source/tools/chat_audit.py` reads `CHAT_LINE` logs and counts conversations out of order or with swapped
  parts (exact with the new fields, heuristic without), thanks and follow-ups with nothing to refer to, untrue class,
  level and role answers, reactions with no trigger, late reactions, pile-ons over the caps, repeated reactions, lines a
  minute per channel, and the seconds and minutes /faction ran above its ceiling (events and lines to the player aside).
  Its last line is a verdict: `VERDICT PASS` only with nothing out of order, no untrue answer, no late or repeated
  reaction, no pile-on over the caps and no minute above the ceiling. On the owner's 1.1.3 evening (13 min of /faction at 11.8 lines a minute): one presence answer after the asker
  had closed, one level question after the invite, one follow-up before any answer, one thanks with no answer, one trade
  closing that didn't follow, one join answer from a class that can't fill the role, 6 pile-ons (13 reactions past the
  second), 3 late reactions, 2 repeated reactions. On a 4.6-hour overnight 1.2-line soak: 207 repeated reactions within ten
  minutes, 9 pile-ons, 5 thanks with no answer.
  A simulated three-hour evening (`UT_FactionCoherence.AnEveningHoldsTogether`: 160 made-up Albion bots, the band of 594
  bots a realm at 8 pm, every path that writes to /faction, the same plans played the old way and the new; audited on
  GitHub): conversations out of order 30 before, 0 after; pile-ons 23 (+31 reactions past the second) before, 2 after
  (keep news, three reactions each, the new cap); late reactions 28 before, 0 after; repeated reactions within ten
  minutes 72 before, 0 after; thanks with no answer 2 before, 0 after; untrue role, class or level answers 0 after;
  5.55 lines a minute before, 4.57 after (most of the difference is the reactions past the cap and the time over the
  ceiling). Busiest two minutes 10.0 lines a minute before, 7.0 after, against a ceiling of 7.1; minutes above the
  ceiling 81 before, 0 after (events and lines to the player aside). The checker's verdict: FAIL before, PASS after.

**Bots that notice you, an evening that remembers, regulars you get to know** (owner's PC, RC3: in 2.5 hours with the
player online in Albion, 5 bot lines named the player, no topic ever came back, and 301 bots spoke in /faction, the busiest
34 lines, the top ten a quarter of them). All behind the switches they already had; none of it seen on the owner's PC yet.
- **Bots take notice of you.** A friend (a bot you grouped with or rezzed, never one that is cool towards you) sends a
  hello when you log in, grats when you ding while it is elsewhere (the bots by you still say it in /say, your guild in
  /guild), and a wave when you reach a zone it is hunting in. A bot standing by when you die alone says "ouch, u ok?",
  and a healer that has really set out to rez you says "need a rez? omw" (once a death, by tell when it is farther off).
  A bot next to you notices a good item you just put on (99-100% quality or above your level). Unprompted lines to you
  share one allowance: about five an hour, five minutes apart, solo; about nine, three minutes apart, grouped or with two
  friends online. The bots' old tells and their /faction gossip about you take a turn too. A death seen in front of a bot
  needs only a minute since the last line. Tests only.
- **Real invites.** Half of a solo bot's "want to duo?" tells now come with a real invitation, a yes/no box; yes groups
  you with it (you lead, as when you invite), and a bot that got busy meanwhile says so. Tests only.
- **A bot that turns your invite down says why (RC5 on the owner's PC).** Captainwaffle, a friend inside Krondon on an
  automatic realm raid, ignored an invite without a word. A bot on a raid, a siege, a relic run, a realm force or a horse,
  or in another player's group, now answers by tell besides the system line ("sry, on the raid in Krondon atm", "in a grp,
  maybe later?"; a friend adds "catch u after?"). An invite that reached the bot but could not add it to the group was
  silent; it now says "... could not join your group". Every invite of a gamebot logs `BOT_INVITE_RESULT` (grouped,
  declined and why, failed), so the next evening's log shows what happened. Why the RC5 invite was silent could not be
  read from the logs (they end before it). Tests only.
- **A friend answers your small talk (RC5 on the owner's PC).** "hey what are you up to tonight?" to Ilaan, who had
  grouped with the player twice at Avalon City, drew "might wander somewhere new". A friend now answers "hey", "wyd" or
  "what are you up to" with your name and what it is really doing ("hey Manawatch! killing thrawn ogres in Gwyddneau, want
  in?"), asks you along only when it is free to group and near your level ("sure" is answered with how to group), and
  otherwise names where you grouped ("Avalon City again sometime?"). Tests only.
- **An evening that remembers itself.** Each realm keeps a short memory of what its /faction really carried tonight: who
  asked for a group or a camp, whose death was told and to what, who argued with whom in a debate and about what. A note
  is made only once its line was heard. Between 12 minutes and three hours later, at most one in 6-12 minutes and only
  while /faction is under its target, a regular (or the other side of the debate) asks after it: "aldric u ever find a
  grp earlier?", "staying away from bwgans now? lol", "still think ur wrong about houses btw". The bot asked answers from
  what is true for it now: "ya, been grouped since" only when it is in a group, "found one in Lyonesse" only when it is at a
  camp there; a second death tonight is counted ("ur 2 deaths in tonight lol"). The lines are `src=followup` exchanges. Tests only.
- **Regulars you get to know.** The realm's talkative regulars used to rotate one by one through the evening (a three-hour
  turn staggered by bot), so the channel slowly passed through everyone. The cast now holds for six hours, so one evening
  has one cast. Each regular has a part (the helpful one, the joker, the complainer, the guild recruiter, the old hand) and
  two running bits of its own it comes back to every 35-65 minutes ("if anyone needs me ill be face down in a bog
  somewhere"; a recruiter names its real guild), at most one a realm in 8-14 minutes, under the target. A regular tells
  its own deaths and wipes; anyone else about four times in ten. Who answers a line is looked for among 200 bots of the
  speaker's realm (it was 200 of all three realms), so a regular is found most times. Tests only.
- **Group chat that fits the group (RC5 on the owner's PC).** In a duo of the player and Ratoul: "ty for the inv, where
  are you all?", "who is main assist?", and "back" out of nowhere. A /group line to several people, or about the main
  assist or the puller, now needs three or more in the group; "back" needs a "brb" of the bot's own in the last 15
  minutes or a death. A bot that walked over after an invite says "here" or "made it, ready", not "back" (that is for a
  bot back from a release). Tests only.
- **Guild chat in a player's guild sounds like players and doesn't repeat (RC5 on the owner's PC).** Anjaaaaa said "Just
  checking in. All well with everyone?" twice in eight minutes. The fallback lines of bots in your own guild are now
  written as players typed ("hows everyone doing tonight"), in the bot's own style, and the guild doesn't hear one again
  within the hour, like the other guild lines. Tests only.
- **The transcript covers guild chat and your posts.** Bot lines in your own guild's chat, and your /lfg, /trade and /gu
  posts (whatever the guild settings), are now `CHAT_LINE` rows (`ch=guild`, `who=player ch=lfg`, ...). Tests only.
- **Measured.** `chat_audit.py` prints three informational lines per log (not part of the verdict): lines aimed at or
  naming the player per hour (tells to them, `src=player`, their name in a line), the top ten speakers' share of /faction,
  and follow-up exchanges with how many refer to a real earlier line (the bot asked after spoke in that channel at least
  ten minutes before, and the question names it), plus running bits. On the owner's RC logs (before this change): RC3
  (b72716e, 2.5 h) 3.2 lines an hour aimed at the player, top ten 24% of /faction from 301 speakers; RC4 8.3 an hour over
  43 minutes (greetings in /say), top ten 30%; RC5 6.0 an hour over 30 minutes, top ten 38%; no follow-ups in any.
  A simulated three-hour evening (`UT_ChatEngagement`: 594 made-up Albion bots, the 1.1.3 /faction rules with the answers
  of #155 (a question only with room for its answer, a regular first two times in five, a stand-in for a busy answerer), a
  player who logs in, dings every 25 minutes, dies three times, moves zone, puts on two good items and is grouped for an
  hour; the old way and this branch on the same seed; audited on GitHub with the other simulated evenings): top ten 33% of
  /faction from 291 speakers before, 42% from 265 after (the busiest regular 67 lines); bot questions answered within a
  minute 93% before, 94% after; follow-ups 0 before, 15 after, each answered and about a line its bot really said at least
  ten minutes before; running bits 13; lines aimed at the player 4.3 an hour before, 6.7 after; never above the /faction
  ceiling.
  The checker's verdict on both: PASS. Tests only.

**Chat fixes from the sixth and seventh release candidates on the owner's PC** (eff81a0 and b036b3c, 2026-11-09, played
as an Albion 50 and a new Hibernian with the Bot schedule on Real clock). The chat checker passed RC6's 74 minutes, but
reading the lines showed what it couldn't see; it now checks for each of these, and fails RC6's log.
- **A friend's answer to small talk is kept.** "hey! what u up to?" by tell to a friend drew "not sure, try /advice": the
  friend's real answer had no score in the reply budget, which swapped it for a stock line. On RC7 the same tell drew "hey
  Manawatch! rvr in Odin's Gate, its busy, wanna duo?", true of the bot.
- **Group banter only about a hunt you shared.** A wizard grouped with the player fought alone, died, ran back and said
  "clean pull, ty", "loot is yours", "xp is flying, good call on this camp", "oom, give me a minute" (at full power) and
  "pull?", every 20-40 seconds, while the player had not fought. Hunting talk now needs a fight the player took part in
  that ended with nobody dead; buff talk counts as hunting talk; "I'm low" lines need the bot to be low. On RC7 an idle duo
  heard one line in three minutes.
- **A far bot says where it is.** A bot in another zone offered to group and the player's invite said "too far away". A
  bot close enough to invite now offers first; one further off says where it is; an invite from too far gets the bot's
  tell ("other end of pennine mtns lol meet up?", RC7).
- **One line on release.** A dead groupmate said "long run back to Pennine Mountains" and "released, running back" in
  the same second, and asked "any rez near ...?" in a duo where the player can't rez. Now one line, and the group ask
  only when a player in it can rez.
- **Threads that follow their answers.** "any grps out?" "quiet atm" ran on with "how many?" "small grp" "count me in"
  "omw". An answer that says there is nothing ends the run-on (another bot may answer "anyone else?" from the same true
  pool); rvr follow-ups come only after a line about enemies or the zerg, and a count is no answer to "which way".
- **Deaths told by their real killer.** A death to a monster in the frontier was told as a gank ("got caught by the
  bridge"), one or two attackers became "a zerg, 20+", an enemy bot became "the guards", and an objective name became a
  monster ("died to frontier patrol near death stalkers"). A bot now remembers what killed it, how many were on it and
  what class it was, and the story and the death line follow that. One death story per realm in seven minutes, one story
  of any kind at a time with three minutes between, and a place ("the bridge", "outside the gate") not twice in half an
  hour. A rez request gets a rez answer, not "oof".
- **The time of day.** "busy tonight" at 10 AM, "anyone heading to the frontier tonight?" in guild chat at 11 AM: on
  every channel "tonight" reads "today" by day and "this morning" "earlier" after noon.
- **The bot's real state.** "ok back" only after a brb or a death. No "how many bubbles?" to an rvr post or to a 50. A duo
  offer only from a bot that is free. Written /lfg and /faction group posts ("group at Coruscating Mine needs a healer",
  "group has room near Connacht, all classes welcome", "lfg Stonehenge Barrows or anywhere with xp") are checked against
  the bot's real group and task: those three came from bots in full rvr parties or out for rvr.
- **No internal names.** "which stable 4 group rendezvous?": an internal goal name is never a destination in chat.
- **Your /trade post gets answers too.** The player's "wts 99% arcane cloth cap, cheap" drew nothing; only bot posts were
  answered. A player's sale or wanted post now draws a bot's reply as often as a bot's does.

**Two Darkness Falls exits work** (tested against the world data, and clicked in game on the owner's PC with the second
release candidate, 6a83c03: both took an Albion character to Camelot Hills; found and fixed by
rambodolphin13, DividedRealms 428ccd7d). The exit by Legion's hall
and the one near the Midgard boss did nothing when clicked: the game client has them, the released world had no
destination for them. Each now takes you to your realm's usual Darkness Falls exit spot, like every other exit. Nothing
is written to your world: the server supplies the two missing destinations itself. Thank you, rambodolphin13.

**The raid gets out of Xanxicar's stomp** (owner's PC test, 2026-11-07). At the 80% stomp 110 of 185 Albion raiders died
in one second. The order to get clear came only 3% above the threshold, it reached only the raiders whose target was
Xanxicar himself (not those on his elites and crystals, nor healers), the spot it sent them to lay in the cave's rock,
and a bot's melee follow walked it straight back to him. Now the raid withdraws ten seconds of its own damage ahead of
each threshold (at least 6%), every raider near him goes whatever it is fighting, to the reachable spot on the cave floor
clear of the stomp nearest to it (never past him), without its melee follow, and waits out there until the stomp is over. The party he fights still holds him. His
stomp is unchanged. Container check (1.2 line, the same fix, 152 Albion raiders): Xanxicar killed with no wipe; his
80% stomp killed 5 (before: 71, 49 and 48 in three earlier runs).

**Scurceol Hyrde's orbs are shot down from real floor spots** (owner's PC soak, 2026-11-08). Seen once on the owner's
PC with the third release candidate (b72716e), 2026-11-09: a forced Albion raid of 200 shot the four orbs down in 10
minutes, each from its own floor spot (`approach=ranged-stand`, 1,053 and 1,279 from the orbs it logged), killed
Scurceol 50 seconds after the last, and lost nobody to him or his orbs. One raider stuck in Avalon City held the raid
at Caer Diogel until it was killed by hand and walked back (known issues). 1.1.2's tactic (the orbs first, from the floor) never got going on the real world. Three times the raid found
"no reachable floor spot within 1400 has line of sight to it" and set it aside. One orb of four died, Scurceol was never
killed, he killed 156 raiders, and the raid won Krondon by going around him.
- **Why** (from the real Krondon navigation mesh): each orb hangs about 50 above the top of its own rock perch. The
  perch rises out of a pit about 900 deep, 400-530 across from the platform where the raid and Scurceol stand. The
  sight check walked the mesh's floor to the spot under the orb, and a floor walk can't cross a pit. From the 277-321
  reachable spots within 1,400 of each orb, none passed. The 1.1.2 test used a pretend floor that always saw, so it
  could not fail.
- **Now:**
  - The search tries spots all around each orb (every 50 out to 1,400, every 10 degrees) on Scurceol's floor, at least
    400 from him, those beyond 700 from him first.
  - Sight is a line from a raider's eyes to the orb. No ledge, rock top or perch may be in the way, and the floor
    toward the orb must end at a drop, not at a wall.
  - Every orb gets its own spot. A caster or archer out of reach of its party's orb walks to that orb's spot first,
    then shoots. Before, only the raid's current orb had a spot, and the shooters given the other three walked under
    them.
  - Scurceol and his orbs are unchanged.
- **Tested:** automated tests on a recorded copy of the real Krondon mesh around the orbs. Every orb gets a spot the raid
  walks to, in range and clear of him. The 1.1.2 search finds none on the same copy, as on the PC. Container runs
  (test world, real meshes, a test-only way to open the raid; the 1.1.3 build before the chat fix plus this fix):
  - Albion, 80 needed (125% of Krondon's 64), 101-104 in the raid at the fight: all four orbs dead in 43 minutes,
    Scurceol 1.3 minutes later, 16 raiders killed by him, the tactic never set aside.
  - A second run, same build and size: all four orbs dead in 105 minutes (the first took 38), Scurceol 0.8 minutes
    later, 6 raiders killed by him, the tactic never set aside.
  - The stage before every orb had its own spot: stopped at the first orb, at half after 15 minutes, with 9-11 raiders
    on it.

**Raid fixes from the forced Krondon raid** (owner's PC, third release candidate b72716e, 2026-11-09: a forced Albion
raid from the launcher's Realm Events tab, 176 minutes from start to Orylle's death, 38 of them waiting at Caer Diogel).
- **A forced raid no longer waits forever for its last member.** It reserved 300 level 50s, but Caer Diogel had room
  for only 25 parties (13 of its 38 formation posts are on ground too tight), so 200 bots formed parties and the raid
  needed every one of them at the hub. It had 125 there at 01:30 and 199 from 01:45 on; the last one was stuck in
  Avalon City, and the raid left at 01:57 only after he was killed by hand. Now a forced raid waits 15 minutes for
  everyone, then goes with 90% (180 of 200), and after 30 minutes with half; the rest follow as late members, as in
  an automatic raid. The same applies at its staging outside the dungeon, counted from when it left the hub. When the
  hub has no post left, the raid needs only what its posts hold, and the other reserved bots go back to their own
  play: on the PC 100 of them waited three hours for a post that never came, kept out of every other group. Tests only.
- **A raider left behind in another dungeon walks out.** That straggler, Ferken, stood in Avalon City on "Holding before
  dungeon threat" from 01:16 to 01:55, waiting for his party's puller, who was at Caer Diogel. The wait is meant to end
  after 90 seconds, but its clock started again whenever another monster became the first one in his way. Now the
  clock runs for the whole wait whichever monster is in the way, nobody waits for a puller when no one of their party
  is in their region, and such a raider goes around the monster when the floor allows it (otherwise fights it) on its
  way to the party. Tests only.
- **The raid walks to its next pack on its own floor.** Inside Krondon several packs took 3-4 minutes to reach, with
  nobody within 1,000 of the target, and then died in 10-20 seconds. The raid took the nearest monster in a straight
  line, which in Krondon is often one on the floor 1,500 below: at 02:30 it went down to an atelic ooze, at 02:34 back up
  to a foreman, at 02:38 down to another ooze. Now a monster on another floor counts as eight times as far for each unit
  of height, so the raid clears the floor it stands on first. Tests only.
- **Parties clear the trash on their way to a muster.** While the raid gathers for a named encounter, a party may not
  start a fight, so each party stopped for 90 seconds before every monster on its walk there and then fought it anyway.
  The musters took 6 to 20 minutes (Ulor se Bysen: 13 of 100 after 10 minutes, in at 19 minutes, with 174 coin drops
  from monsters killed in Krondon in that time). Now the party's puller pulls an ordinary monster in the way at once, with the usual
  checks (health, a whole party, close together). An epic encounter in the way still waits for the raid. Tests only.
- **Nobody fights Orshom's Fire.** The fire pit Orshom Brong lights when his fight starts burns everything within 300
  every 3 seconds and can only be hurt by cold. The raid took it for an encounter that was fighting it, turned on it,
  and 100 raiders burned in it between 04:12 and 04:18; after five minutes without damage it was set aside. Now it is
  never the raid's target and no party post is placed in it. A raider with a cold spell shoots it from outside its
  radius, everyone else steps out of it and fights the twins, and anyone standing in it steps out first, whatever it is
  fighting. The fire and the twins are unchanged. Tests only.
- **Melee stays off Scurceol Hyrde's floating orbs.** 11 seconds after the raid turned to the orbs from a floor spot,
  it switched to another orb "fighting the raid" and made the orb's spot in the air its destination: 111 of the 172 route
  recoveries toward Lyft Miht that followed aimed into the air. Now an orb is fought only through the orbs' own plan,
  and a melee raider whose target is an orb it can't reach waits at the orbs' floor spot. Tests only.

**A day with a prime time and a quiet night** (owner's PC test copy, 2026-11-09). At 05:20 2,673 gamebots were online,
about 900 a realm, and Albion's /faction ran 323 lines an hour at 02:00 and 291 at 03:00, nearly the evening's 300-450.
The server log said `bot_schedule=off`: the Bot schedule on the launcher's Living Server tab was off on that copy, so
every bot stayed online all night. The rise seen that night from about 840 online to 2,673 was not the roster growing:
the server was restarted at 01:10, 04:27 and 05:25, and each time the startup ramp logged the whole roster back in over
15 minutes, about 180 bots a minute (844 four minutes after the 04:27 start, 2,718 at 04:42, the same ~2,715 each time).
With the schedule off the ramp's target is the whole roster whatever the hour. With "Real clock" on:
- **A deeper night.** The clock curve now keeps about an eighth of the roster before dawn on a weekday (about 330-370
  of 2,700 at 4-6 am; the old curve kept a fifth, about 560) and nearly all of it from 7 to 11 pm; weekends are busier
  all day.
- **No jumps.** Every bot's place in the day's line changed at midnight at once, so about 450 bots wanted to swap in the
  same minute; the places now move a little at a time between 2 and 5 am. The weekday and weekend curves stepped at
  Friday 6 pm and Monday midnight (about 400 bots at once); they now run smoothly into each other. The most bots the
  schedule sends home in any minute of a week is 26 (logouts run at up to 36 a minute).
- **RvR bots log off too.** A bot that had ever run its RvR goal could never log off or take a break: it keeps its force
  name for good, and that was read as "busy in a realm event". Now only a bot whose force is in a running event (a siege,
  a defense, a relic run) stays.
- **Finish, then leave.** A bot the schedule is sending home joins no new party and answers no automatic raid, and an
  automatic raid is sized to the level 50s who stay. A bot in a party, a raid or a siege still finishes it before logging
  off. After a start in the night the startup ramp spreads the schedule's number over its 15 minutes (it let the few
  hundred all in within two minutes).
- **The launcher** shows the Bot schedule first in the Server life card, with what Off and Real clock mean, and the
  status line always says whether it is on. Setup already turns Real clock on for a new install; the server's own
  default stays off.
- **Chat follows the crowd.** /faction's target and ceiling already scale with the bots online and the hour; with fewer
  bots at night the quiet hours are quiet. A simulated weekday (`UT_LivingServerDay`: 2,700 bots, a third in parties,
  the real schedule, login, logout and chat-budget rules, a model of one realm's /faction): with the schedule off,
  258-291 /faction lines an hour at 2-5 am against 407-427 at 7-11 pm (the PC's night); with Real clock, 2,532-2,620
  online at 7-9 pm and 371 at 4:30 am, no party member logged out, at most 21 logouts in a minute and 132 in ten
  minutes, and /faction 118-137 lines an hour at 3-6 am against 388-421 at 7-11 pm, every hour under its ceiling.
  Tests only.

**Defenders are called when the enemy army marches, so a defended keep often holds** (owner's PC, 1.1.3 release
candidate nights, 2026-11-08 and 11-09: keeps "flipping all over"). In about 15 hours of logs 18 bot sieges ended: 12 keeps
captured, 4 defended, 2 failed rallies.
- **Why the attackers won:** the keep's realm was told only when the battle began (`RVR_DEFENSE_MUSTER` in the same second
  as `RVR_SIEGE_STARTED`). Eight of the 12 keeps fell 4-11 minutes into the battle. The first defender reached the keep 3-14
  minutes after the start in 7 of the captures and never in the other 5 (`defendersFirstAt`); several were warbands breaking
  off an attack in another realm's frontier. Every siege with defenders at the keep within two minutes of the start was
  held (3 of 3). Committed defenders were not the limit: they were walking.
- **Now:** scouts see the army leave its border keep. The keep's realm is told at once ("Scouts report Albion's army
  marching on Dun Bolg!", `RVR_DEFENSE_CALLED`), its free level 50 warbands are called within seconds, gather at their
  muster for up to 6 minutes (10 when it is thin) and then man the walls. The march took 9-37 minutes in these logs, so
  the defense is usually in place when the attackers arrive. The third realm still joins only once the battle is on. A
  player's own attack and the retake after a player's capture keep their rules.
- **The siege balance aims at about half** (owner 2026-11-09). It used to steer towards attackers winning about two sieges
  in three (owner, 2026-10-19); with defenders arriving in time it would have pulled them away again until attackers won
  most. Now, of the last six sieges, four or more captured adds defenders, two or fewer takes them away, and three holds
  (thresholds 0.65 and 0.35, were 0.8 and 0.45). In a simulation over ten models of the attackers' chance (`UT_SiegeBalanceNov06`)
  it settles at 44-56% captured, 50% on average; the old thresholds settled at 53-61%. In the logs it sat at its limit
  all night (share 1.0, attackers 96) and still lost 12 of 16 finished sieges.
- **Battleground keeps** do change hands, rarely: Dun Murdaigean went from unclaimed to Albion between 04:34 and 19:32 on
  11/08 (outside the logs). In the 15 logged hours no battleground lord fell below 85% and at most 7 bots were on one.
  Not changed here.
- Tests only.

**Gamebots in their capital go to their own frontier for the porter** (owner's PC, 1.1.3 nights). 779 times a gamebot
stood 15 minutes "Waiting for a frontier teleporter" and the watchdog moved it on (Tir na Nog 404, Camelot 227, Jordheim
148). Each stood on its capital's bind after a release, sent to a keep in another realm's frontier (Mjollner Faste 118
times, Glenlock Faste 66, Dun Bolg 59 and more). Only its own frontier's porter goes there, and that porter is used only
from inside the frontier, so it waited in a city with no porter, and the watchdog put it back on the same spot (recovery counts up to 24). Now such a bot first travels to its realm's border keep (Castle Sauvage, Svasud Faste, Druim Ligen), where the
medallion merchant and the porter are, and rides from there. Tests only.

**A camp search that holds a bot for minutes is reset** (owner's PC, 1.1.3 nights). 464 times a gamebot stood 15 minutes
"Searching for a valid XP camp", nearly all level 50s on their capital's bind (Tir na Nog most). In the 11/09 log 72 of 91
went on to a camp only after their next login, from the same spot, so what held them was in the bot's own planning
memory, not the world; which part is not shown by these logs. After 3 minutes of failed searches a bot now drops its own
death caution and failed-camp memories as a fresh login does, and logs what it held (`AUTONOMOUS_CAMP_SEARCH_RESET`), so
the next night shows the cause. Tests only.

**Fewer server stalls on a busy night** (owner's PC: AI profile of 2026-11-07/08 and the 1.1.3 nights; 17 stalls over a
second in the server logs, 3 of 4-6 seconds in the night before).
- **The 4.5-6.1 s stalls** were Krondon raids: the raid pulse took 4,520-6,140 ms in the same second as each
  "no reachable floor spot within 1400" for Scurceol's orbs (05:38, 05:54, 06:19 on 11/08). That build's search
  checked 277-321 reachable floor spots per orb; 1.1.3's new orb search (above) tries at most 8 walks per orb. Not changed
  again here, and not yet timed on a real install.
- **One bot's AI turn of 2.0-2.2 s** (3 of the 17): a Sluaghbinder searching a dampwood mite camp proved one target's
  route after another (1,906-1,970 path queries and 1,013-1,060 corridor checks in one turn). A camp search now proves
  at most 4 routes and casts at most 24 dungeon sight lines; the rest wait for the next search a second later, and a
  target whose route failed is skipped for a minute. In a test on a mesh with no routes (79 queries per failed proof),
  one search over 200 targets makes 316 queries instead of 15,800.
- **Group formation passes of 0.3-1.3 s** (46 times in 800 one-minute profile samples, 10 of them over 0.7 s), held under the lock every grouped bot's turn waits on
  (that lock's waits were 16% of all AI time). Nearly all of it was rendezvous route searches (up to 390 ms each). A pass
  now stops once it has spent 150 ms after its first leader and goes on 15 seconds later.
- **Not changed:** 12 of the 17 stalls also had a 443-687 ms garbage collection pause in them (heap about 2 GB).
- Tests only.

**Bots still don't fall when they die** (looked at again for 1.1.3; nothing shipped). The left-out fix (23c88a12, Beta 1.1)
sent the player-death packet for a bot; the client plays that pose only for player characters, so in game it did nothing.
The other server-side ways all break something:
- The only death animation the client plays for a non-player is on the remove-object packet for a dead NPC (how monsters
  die). The client then deletes the object: the corpse can't be targeted, so it can't be rezzed, and the group window
  loses it until the bot is sent again, standing.
- The NPC create and update packets have no dead or lying-down flag (statue only freezes the model), and a 0% health
  update leaves the model standing, as the bots do now.
- A separate corpse prop would need a client model and new client art IDs, and still couldn't be the rez target.
- Tests only (nothing in game changed).

## Beta 1.1.2 — 2026-11-07

Chat: a much busier realm, and bots that answer what you actually said. Raids the size they were in the era, sized
by their own wins and losses, that no longer stand for hours at an encounter they can't reach, and their first boss tactics. Playing with your bot
group: dragon raids that regroup after a wipe and call the Epic Special Forces, quest credit and hand-ins, and quest XP that follows your
XP-by-level rate. Gamebots in your guild earn merit and pay dues, power comes back faster, fixes from other forks of
Offline DAoC, updates from the launcher, and a world database that opens in a crash-safe mode and is backed up every
day.
How it was tested, and what hasn't been played yet, is in the release notes
([docs/RELEASE-NOTES-1.1.2.md](docs/RELEASE-NOTES-1.1.2.md)).

**Epic raids learn their first encounter rules** (owner 2026-11-06: shipped in 1.1.2, not 1.2). Raids of gamebots now
know five bosses that beat them by their rules, not by their damage. At release all of this had tests only. Seen since
on a real install, in bot raids on a later development build: Black Lady was fought at home and killed; Scurceol's orbs and Xanxicar's stomp did
not work (below).
- **Scurceol Hyrde (Krondon).**
  - **Before:** he takes no damage while his four floating Lyft Miht orbs live, but the raid hit only him, for an hour, and
    all 42 raider deaths were his.
  - **Now:** while an orb lives the raid's target is an orb. Each party takes one; casters and archers shoot it from the
    floor, and melee wait under it, away from him. Nobody hits Scurceol until the last orb is down, then the whole raid
    turns on him. Orbs stay dead if he resets.
  - The raid also fights him in his own room: past 1,500 from his spawn it stops hitting him, and the raider he chases
    leads him back.
  - **On a real install this did not work** (a later development build with the same orbs code). The raid found no floor spot that sees most of the orbs. It killed 1 of
    the 4, set Scurceol aside three times and took Krondon by going around him. Being fixed.
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
  - On a real install the raid found no such spot for Scurceol's orbs (above). Its test uses a flat test floor, not
    Krondon's.
- **Aidon the Archwizard (Hall of the Corrupt).** The raid kills three of his four illusions and leaves the last one
  alive, because he only summons a new set once all four are dead. Then everyone fights him, near his room. His damage
  shield only hurts melee attackers; it doesn't make him harder to damage, so he can be beaten by design.
- **Nosdoden (Trollheim).** When a wave of essences comes (every 10% of his health), the raid stops hitting him and
  kills the wave first. His health then drops one step at a time and waves don't pile up.
- **Xanxicar (Crystal Cave).** Before 80/60/40/20% and until his stomp has landed, everyone except the party he is
  fighting moves 2,700 away. That party pushes him over and takes the 2,500-radius stomp, which is meant to kill whoever
  is in it; the rest of the raid survives and comes back. **This did not work:** on the owner's PC (2026-11-07) the
  first stomp killed 110 of 185 raiders. Fixed in 1.1.3.
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
  - **The Realm Exchange:** bots mention it less often. On a real install they still advertised it unprompted.
  - **Templates:** a few grammar slips are fixed.
- **Typing habits.** About a third of bots have small habits of their own: "u" and "ur", "??", "...", a trailing "lol",
  and for a rare few, the period's leetspeak.
- **`/lswhy`** (every player) shows why a bot said what it said, or why nobody answered, and `/lswhy budget` shows how
  busy each channel around you is.
- **Checked:**
  - **Automated:** new tests for every part; the full server suite passes.
  - **Simulated evening at 1,000 bots a realm:** about 12.7 lines a minute across the channels a player hears, up from
    about 1.9, with /faction above its busy-peak target of 4 a minute. At 300 bots it's about 5.9, up from 1.2.
  - **The blind test:** 400 player lines, read one at a time, not conversations. The bots read 53% of them as
    intended; re-running the same lines gives about 70%, and about a quarter of them now get no reply. Those lines are
    known, so this is a regression check. A blind judge still picked out most bot lines (72% in the first round, 85% in
    the latest). Conversations over several turns have not been tested.
  - **On a real install** (the 1.1.3 build, about 600 bots a realm, an evening): /faction ran at about 14 lines a minute
    for ten minutes, with peaks of 20, above its ceiling, and reactions came in bursts of 10 to 16 lines. A player heard
    about 15 to 19 lines a minute in all, more than the simulation predicts. Being fixed.

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
- **Checked:** unit tests (`UT_SiegeBalanceNov06`). In a night's run of a later development build on a real install,
  the attackers' limit sat at its lowest all night and attackers still took 8 of 11 keeps. It did not bring captures
  near half.

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
  that ended with most of the raid standing). Automated tests (`UT_DragonRaidWipes`). Seen since on a real install (a
  later development build): a dragon raid wiped, the Special Forces came with 24 and the raid regrouped. Its second try hasn't been seen yet.

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
  Albion, not `_FirstPlayerRealm`. Checked: unit tests (`UT_SpecialForcesJoinFullRaids`). Seen since on a real
  install (a later development build): the Special Forces joined full raids after a wipe.
- **The Darkness Falls princes no longer eat bots** (found in the 2026-11-06 overnight test). Princes Asmoien and
  Ba'alorien devour the nearest pet every five seconds and heal by its health, but their pet test matched a bot's own
  brain too, so they ate companion bots and gamebots: the overnight raid did no damage for five minutes and never got a
  prince below 95%. They now eat only summoned pets, a player's or a bot's, never a bot
  (`EpicEncounterMechanics.IsDevourablePet`). Checked: unit tests (`UT_PrinceDevour`). Seen since on a real install
  (a later development build): a bot raid killed Prince Ba'alorien twice.
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
  it was set in the launcher keeps its value. Checked: unit tests for the default, the one-time move and a value set in
  the launcher; the levels themselves were measured once on a playtest PC. On a real install at 2, /faction can run
  above its ceiling (see the chat entry above).
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
  the release config. Seen since on the owner's install (2026-11-05 and 2026-11-08): crash-safe mode confirmed, the
  integrity check passed and a daily backup was written. The installer's database copy and the ledger move have tests
  only.

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
