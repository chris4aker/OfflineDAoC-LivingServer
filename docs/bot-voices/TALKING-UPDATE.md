# The talking update (Living Server Beta 1.1)

Bot chat in Beta 1.0 had three problems. Bots said things that weren't true: a Hibernian in Ardee asked about an
Albion dungeon, "heading into myrkwood" came from anywhere in Midgard, and Darkness Falls lines came from inside Tomb
of Mithra. Every line also stood alone: a player who answered a bot's question got no reply, and a "why?" got nothing.
And bots in the player's group said nothing during a fight. The talking update fixes these, and makes realm chat
sound like a busy server.

It affects **autonomous gamebots** only. Companion bots (`/spawn`, `/raid`) and real players are unchanged. Content
rules for new lines are in [TALKING-UPDATE-CONTENT.md](TALKING-UPDATE-CONTENT.md).

## Settings

All are server settings, category `bots`. The first three are in the launcher's Living Server tab.

| Key | Default | What it does |
|---|---|---|
| `bot_voices_enabled` | **True** (was False) | Corpus lines in each bot's own style. Everything below needs it. |
| `bot_living_memory` | **True** (was False) | Bots remember grouping with you, your rezzes and rude words. Bot tells need it. |
| `bot_living_grats` | **True** (was False) | Grats on a ding in /group, /say and, for a 50, /faction. |
| `bot_voices_tells` | True (new) | Bots now and then start a tell with you (below). |
| `bot_voices_chat_rate` | 1.0 (new) | How much bots talk unprompted: 0 (they only answer) to 3. |

Only these chat parts are on by default. Personalities, guilds and the other Server life parts stay off.

**Worlds that ran an earlier build.** The server keeps a setting's stored row, so on its own the new default would
never reach a world that has already run. On the first start of 1.1 the server switches on, once, each of the three
settings whose row it wrote itself with the old default (`LivingServerSettingsUpgrade`; the server log says which).
Rows saved in the launcher are left as they are. The row `living_server_settings_version` records that the switch ran.
To keep a part off, switch it off again in the Living Server tab; it is not switched on a second time.

## What changed

### 1. Bots only say what is true

- **Places.** `data/chat/places.json` lists about 140 zones, towns, dungeons, capitals, frontiers and battlegrounds
  with their realm and the zones that count as being there. A line that names a place must either claim the speaker
  is there or on the way (`@needs at:<place>`, `going:<place>`, `near:<place>`), or say it is only talking about it
  (`@about place`). The build (`tools/bot-voices/build_corpus.py`) rejects a line that names another realm's zone,
  town or dungeon. Frontiers and capitals are exempt. The speaker's `at:` and `going:` flags come from its zone,
  region and travel destination (`AutonomousChatPlaces`).
- **News.** `AutonomousChatNews` watches keeps changing hands, relics taken and returned, Darkness Falls, realm raids
  won or wiped, and sieges. Lines about a keep, relic, raid or Darkness Falls need the flag that makes them true:
  `defending`, `attacking`, `keep_lost_recent`, `relic_won_recent`, `df_ours`, and so on. The flag fills the line's
  `{keep}`, `{relic}` or `{boss}` with the real one. Defenders hear of a siege only once the walls are hit or their
  defense is called.
- **Experience and roles.** `{bubbles}` is the speaker's real xp in the level. `xp_low`, `xp_mid` and `xp_high` gate
  "almost there" lines. `is_healer`, `is_tank`, `is_caster` and `is_support` gate role claims, and `newbie` (1-14) and
  `veteran` (40+) gate experience talk.
- **No fallback lies.** With voices on, a bot with nothing true to say stays quiet. It no longer falls back to the
  old formal lines ("How fares everyone across the realm?").
- **Enemy sightings.** "Where are the mids?" is answered from what the realm has seen. Frontier bots put the enemies
  they see on the news board, with how long ago, and enemies within sight of a bot or player of the realm also count.
  Before, the answer came from where every enemy gamebot was.
- **Lines that promised what the server can't do** are off. A bot can't invite a player, so the "we have room at
  {camp}, want in?" and "sending an invite" lines are never said.
- **The simulator found more.** It turned up "my pet has better gear than me" from wizards (now pet classes only), a
  solo bot apologizing to "my healer" (now in a group only), and a scout sitting "to get my power back" (now health).

### 2. Conversations follow on

`AutonomousChatThreads` remembers the last 8 bot lines per channel (`/faction`, `/say` per region, each tell pair,
`/group`):

- A **follow-up** within 90 seconds of a bot's line ("why?", "rly?", "how so", "wdym", "who?", "where?") is answered
  by that bot. The answer fits the kind of line (a boast, complaint, joke, news...), and "where?" gets where the bot
  really is.
- An **answer** to a bot's open question within 75 seconds is thanked by the asker 85% of the time ("ty, heading
  there"), and the other bots leave it alone. A line that names another bot is not taken as an answer.
- **Tells** continue the exchange for 4 minutes: a follow-up to the bot's last line, or thanks for an answer.
  A tell's reply is worked out when it is sent, so "where are you?" gives where the bot is then, not where it was
  30 seconds before.

### 3. News in the bots' own words

When a keep falls, a relic moves, Darkness Falls changes hands or a realm raid wins or wipes, a bot tells /faction,
50-80 seconds later. A bot that was there speaks as a witness ("we just lost the gate at {keep}, they had rams");
otherwise it's heard ("heard we lost {keep}"). Others react ("again??", "omw"). When a herald notice already covered
the event, the bot doesn't repeat it, and with voices on the herald notices themselves are said by a bot where one
fits.

### 4. Combat calls in your group

`AutonomousBotCombatCalls` runs once a second for each gamebot in the player's group. Every call comes from real
state:

| Call | When |
|---|---|
| pull ("inc", "here it comes") | a fight starts, 35%; said by a bot in the fight, a tank first. The player usually pulls, so no "pulling now", and "inc 1" only with one enemy |
| add ("add, add") | more enemies on the group than before, at most every 15 s, 75% |
| low_hp ("heal me") | the bot's health drops under 30% in a fight, once until it is back over 50% |
| oom ("oom") | in a fight, a caster's or healer's power drops under 15%, once until it is back over 40% |
| mez, root, stun | the bot cast it and it landed (the target is mezzed, stunned or snared); a mez only with two or more enemies |
| taunt, heal_big, rez | the bot is casting that spell now |
| wipe ("run!") | half the group has died in this fight (groups of 3+), once |
| kill / rest ("gj", "med up") | a fight ends without a wipe, rest more likely when the group is worn |

The group hears at most one call every 3.5 seconds (a wipe call always goes out), and each bot speaks at most every
8 seconds. A gamebot that joins your group says hello in /group most of the time.

### 5. Bots start tells (rarely)

Needs voices, memory and `bot_voices_tells`. The first tell can come 4-10 minutes after you log in, then at most one
every 25-45 minutes, and a given bot tells you at most once every 6 hours:

- **friends** (bots that grouped with you or that you rezzed, not ones you insulted): hello, thanks for the group at
  {place}, the realm's news, or a hand if you are under level 20 and at least 8 levels below them;
- **guildmates** in a bot guild you belong to: small talk;
- **a solo bot of your level** (±2) in your zone, within 6,000 units, while you are ungrouped: a duo offer.

A friend standing next to you greets you in /say instead. A player's **/lfg** may also get a duo offer by tell from a
solo bot within 2 levels, the same zone first (this needs voices and `bot_voices_tells`, not memory). A bot busy with a
siege, a relic, a raid or a horse route says so instead of "send me an invite". Answer "sure" and the bot tells you how to group ("sure, send me an
invite": you invite, because a bot can't). "Nah" gets "np, maybe later".

### 6. A busier, more natural realm chat

- The gap between bot-started /faction lines is 2-4 minutes at 120 bots of the realm online, an ordinary hour and
  rate 1. It is shorter with more bots online (√ scale, 0.5-1.8×) and in the evening (18-23 h, 1.35×), and longer
  late at night (2-7 h, 0.55×) and in the morning (0.85×). It never drops below 40 seconds. `/say` gaps follow the
  hour and the rate. `bot_voices_chat_rate` scales both, and each bot's own chattiness.
- Most `lfg` lines go to the realm's **/lfg** channel (`[LFG] Name (level, class): ...`) and most `trade` lines to
  **/trade**, as players used them, instead of /faction. The new client shows both with /faction chat.
- Guild template lines, knowledge answers in /gu and /group, join replies and "where are you?" answers are written in
  each bot's own style (lower case, typos, abbreviations).

### 7. Bots read what you say, and answer it

An audit ran realistic player lines through the reply path, each labelled with what a sensible player would answer.
Factual questions drew remarks on the topic ("is thid open for lvl 21" drew "if you see the zerg coming, call it
out"), follow-ups were missed, "inc" read as income and tells about groups as invitations. Changes:

- **Questions get real answers.** About the bot, when it is asked by name, by tell or in your group: its level, class,
  realm rank, speed, potions, guild, main or alt, epic, how long it has played, whether it is busy, its plans (by
  personality), and its gear (how much of what it wears a crafter made; it can't tell a drop from a merchant buy, so
  it doesn't claim either). About the world: a place's
  monster levels (the 20th-80th percentile of each zone's monster levels in the world data, written to `places.json` by
  `tools/bot-voices/place_levels.py`), battleground brackets, who holds Darkness Falls, the relics and keeps, and a few
  commands (/stuck, /anon, /bind, /played, /relic). Any other factual question gets an honest "not sure" now and then,
  never a remark on its topic.
- **The bot's name is not part of the line.** "torvald u still a thane?" is read as "u still a thane?" by Torvald.
- **More of the conversation follows on**, in /faction, /say and now /group: "which bridge", "how long did that take",
  "the one by the inn?", "whered they go", "o ya what stats", "into what"; "no thanks" to a bot's offer; "ty" to the bot
  that just helped (not for its own "gz" or "np"); a laugh to the joker; "x" or "me" to a bot's call for a group.
- **New kinds of line**: good news ("finally hit 50") gets grats, an offer ("need buffs?") thanks, "wait for me" an ok,
  "wb" a thanks, an order ("heal me") a plain "k" (bots don't act on orders, so they don't promise to) or an honest
  "cant heal, im a thane", a warning a "ty for the heads up",
  real-life talk (late nights, school, food, the computer, pets, weather, TV, work, a sore back) a reply in kind.
- **Fewer misreadings**: "inc" as income, "group" only talked about ("my old group quit", "love this group"), sarcasm
  ("thanks for the train"), insults aimed at a class rather than a person ("ppl say spiritmasters are useless"), "pc"
  as a computer, school "class", goodbyes with thanks in them, a brb at the end of a line.
- **Group requests**: "want to group?" in /say or by name gets the bot's answer with its real reasons (already grouped,
  on a keep or relic, too far, a level gap of more than 8). An /lfm is answered only by bots of the role it needs and
  within 6 levels.
- **When you die in a group**, a groupmate says so, a healer first ("rez coming, dont release").

**How well it reads, measured.** These are automated measures of how a line is *read* (which route it takes), not
of the reply text, and not checked in game:

| Line set | Before the audit | After |
|---|---|---|
| The 320 audit lines (the fixes were made against these) | 47% | 96% |
| Fresh set 1, 160 lines written blind, first run | — | 62% |
| Fresh set 2, written blind after the fixes for set 1, first run | — | 65% |
| Fresh set 3, written blind after the fixes for set 2, first run | — | 62.5% |

The tuned rates overstate what new chat gets: on lines nobody tuned against, about **two in three** are read the way a
player would expect. The rest split roughly evenly between no reply, an honest "not sure" and a reply that misses (12-16%
of fresh lines on sets 2 and 3, against 21% on set 1). The code from before the audit was not measured on fresh lines. About six of set 3's misses are a labelling difference (a
"gz" for the bot's own ding is answered "ty", as it should be, but was labelled as the player's news). Sets 1 and 2 were
then fixed against and are kept as a regression test; set 3 (`tools/bot-voices/eval/fresh_lines_3.json`) is kept
untuned. Word rules have reached about their ceiling here; reading more of free chat right would need a different
approach (a trained model on many more labelled lines), not more rules.

To measure a new blind set: `TALK_AUDIT_IN=<lines.json> TALK_AUDIT_OUT=<misses.txt> dotnet vstest Tests.dll
--Tests:MeasureReadRate` (each line `{text, channel, context?, expect, to?}`; `to` is the bot named in the line).

### 8. Joining a debate, and how often lines repeat

- **A player can join a running debate.** In /faction within 75 s of a debate line, "yeah exactly" draws thanks from the
  last speaker ("see, Wynn gets it") and sometimes a groan from the other side; "nah, buffbots are fine" draws pushback
  and sometimes a bot on the player's side; an opinion on the topic a nod from a debater. At most two answers per
  debate, and the debate then goes on. Debate lines are now in the realm's thread, so "why?" or "lol" after one goes to
  the bot that said it (`AutonomousBotDebates.TryAnswerPlayer`, `StanceOf`).
- **The month simulation** (`UT_ChatMonth`, an audit tool: `TALK_MONTH_OUT=<file>`, optional `TALK_MONTH_RATE`) runs 30
  evenings of 4 hours per realm through the real picker and its 3-day no-repeat window, at the evening pace 900 bots
  give (/faction openers about every 74 s, replies, /guild, about 25 player lines). It is an approximate model: speakers'
  places and flags are random, news and place lines are not modelled, debates are left out.
  - A player reads about 340-360 bot lines an evening. From week 2 about **77-84% are the same words they read on an
    earlier evening** (week 1: about 40%). At half the chat rate it is 62-81%, at a quarter 41-73%.
  - The reply pools ran dry first ("chat" replies: 19 lines for about 1,000 openers a month, 423 left unanswered).
    `replies_pools.lines` adds about 210 replies (chat, joke, boast, complaint, lfg, real life, newbie answers); after it no
    reply goes unanswered and joke, boast and complaint replies no longer repeat within 3 days. The overall rate barely
    moves: the openers (512 ambient lines per realm against about 140 a night, 130 lfg lines, 210 guild lines) are the
    limit. Lowering the pace alone doesn't fix it; it needs several times more distinct openers (lines built from parts,
    or many more written lines).
  - **Then both were done** (2026-10-26): lines built from parts (`[a|b|c]`, DESIGN.md 3.1) and about 2,400 new written
    rows in `ambient_life_more`, `ambient_realms_more`, `ambient_classes_more`, `guild_chat_more`, `lfg_parts` and
    `replies_parts`. The corpus grew from about 3,800 to about 15,000 lines (ambient 752 to 7,934, guild 210 to 3,063, lfg
    130 to 299, trade 33 to 237, replies 1,616 to 2,192). Word-for-word repeats from an earlier evening fell to about 9% in
    week 1, 37-38% in week 2 and 58-60% by week 4 (from 40%, 77% and 83%). The measure counts exact words, so two ways of
    saying one family line count as different; a player may notice the family sooner than the numbers suggest.

### 9. Lore, myths and exploration

Researched from the era (patch notes up to 1.65, the last before Trials of Atlantis; zone lore; Allakhazam comments from
2002-2003), with each item labelled documented, player rumour or invented. Bots state documented facts plainly and frame
rumours and inventions as rumour ("heard that...", "my old guild swore..."). Only monsters that exist in this world are
named (checked against the world database: not Yar, Apocalypse, Morgana as a monster, Flame or Beliathan); Legion,
Behemoth, Govannon and the dragons are lore only, and no bot claims to have killed a raid boss.

- `lore_albion.lines`, `lore_midgard.lines`, `lore_hibernia.lines`: each realm's legends, named monsters, dungeon stories,
  superstitions and its own hidden-place myth, given equal weight: Avalon hidden in the marsh (the launch race text named a
  city of Avalon, a 2001 preview said the marsh ran "to the edges of Avalon City", and there was no way in until Shrouded
  Isles); the road to Asgard behind a waterfall and the gate nobody has found in Odin's Gate; a door under a hill in
  Connacht and the Fianna asleep beneath it, and Hy Brasil seen from the cliffs. About 10-12 lore debates per realm.
- `lore_shared.lines`: Darkness Falls (Legion's court, the imps and seals, Behemoth, Govannon), the tunnels under all three
  frontiers and "the third power", the three dragons compared, con colours that lie, drop superstitions, the world finding
  itself (passes out of mist, new islands), swimming to the edge, old maps; 12 debates.
- Lore by weight per realm: 357 / 385 / 356 (alb / mid / hib); all realm talk 1,327 / 1,303 / 1,283.
- **Second pass** (the owner found the first debates thin): every lore debate rewritten as a full conversation, 19-30
  beats with 5 speakers, concrete lore in nearly every line, evidence against counter-evidence, the old legend the game
  drew on (Arthurian, Norse, Irish) set against what the game shows, a newcomer who asks "wait, who is that?", and an
  ending with a plan, a dare or a shrug; plus tale scenes, where one bot tells a legend and others interrupt (5 per realm,
  3 shared). 16 / 15 / 17 / 16 lore scenes (alb / mid / hib / shared).

### 10. The Dunce Rage Quit

Now and then a bot asks something truly dumb in /faction ("how do i level up my horse?", "which merchant sells realm
points?", "how do i pause the game, dinner is ready"), six or seven bots pile on, the dunce gets angrier until it rage
quits in capitals, and then it really logs off for 2-6 hours (scenes with `@exit A`, `AutonomousBotSchedule.RageQuit`,
whatever the schedule mode). When it logs back in it says something sheepish in /faction ("ok im back. we dont talk about
earlier"). Ten scenes (`dunce.lines`); scenes now take up to eight speakers (roles A-H). A dunce that can't log out
(grouped, fighting) is tried for about a minute, then stays. The time away and the sheepish return are saved
(`offline_bot_ragequits`, an optional progress table), so a server restart does not bring the dunce back early. Beggars
are never cast in a debate or a pile-on, and the roasts call the dunce "they".

### 11. Beggars

About 4% of the Crafter personalities are beggars (`bot_living_beggars`, on by default; `AutonomousBeggars`; 2% until
2026-10-26). Each realm gets 4% of its own Crafters (rounded, at least one): the ones with the lowest stable roll on the
bot's id, so the same bots beg every run. On a 900-per-realm roster that is 4-5 beggars a capital. (Until 2026-10-27 the
roll alone decided, which gave one owner's roster 1 / 2 / 4 beggars and left Camelot empty when its one logged off.) A
beggar:

- **Lives in its own capital.** It logs in there, picks a corner beside one of the city's merchants, and every few
  minutes either stays on it or wanders to another merchant, trainer or the broker to beg there. It never groups (an
  invite gets "X won't leave their corner"), is never picked for groups, raids, sieges or realm forces, and takes no
  part in /faction chat: it talks only in /say on its corner.
- **Begs.** It walks up to a player of its realm within 900 units (each player at most every four minutes), plays the
  beg emote and asks for coin or loot by name and class. It calls out to the street now and then and mutters over its
  purse ("3g 12s. never enough"). It begs passing bots too: about one in three gives 1 copper to 2 silver (about 1% of
  its purse) from its own saved coin; the others say no and get called a cheapskate.
- **Takes real gifts.** Drag coin or an item onto it. Coin moves from your purse into the bot's saved purse; a tradable
  item from your backpack moves into its saved inventory (a full pack takes coin only). It thanks you and names the gift.
- **Hoards.** It never buys, upgrades or spends. Every few minutes it takes begged loot to the Realm Exchange (an item
  worth listing, by the same rules other bots use) or to a merchant (vendor trash) and keeps the coin.
- **Turns on you if you ask ANYONE for gold or items**, on any channel: /say, /faction, /group, /guild, /lfg, /trade,
  /yell, /broadcast, a tell to a bot or a player, or a /beg at anyone. Begging is their job. You are marked for an hour
  from your last ask. Beggars in earshot (or the one you asked) turn on you at once; the other beggars in the city hear
  about it within 5-45 seconds and come for you ("word travels fast on the street"); and any beggar that sees you in its
  capital within 1,500 units while you are marked goes after you, even if you asked out in the frontier. It follows you
  around the city (no combat) insulting you and claiming its corner every 6-13 seconds, with rude, taunt and throw-dirt
  emotes, until you leave the capital, then crows ("and STAY out") and goes back. Coin given to the beggar chasing you
  buys a truce with that one beggar for ten minutes (the rest still want you). A 30-minute limit stops one beggar's
  chase if you stay in the city; that beggar then leaves you be for ten minutes.

What reads as asking (`AsksForCharity`): an asking phrase ("spare", "gimme", "can i have", "lend me", "got any ... for
me", "i need") with something of value ("gold", "coin", "5g", "loot", "armor", "anything"). Offers are not ("u want
some gold?", "here, take this", "i'll give you some gold"), nor are "give me a sec" or "can i get an invite". About 3,100
lines (`beggars.lines`, kind `beggar`, 19 topics), spiced the way the dunces are: 2003 chat, no real swear words.

### 12. Deeds and reputation

`bot_living_deeds` (on by default; `AutonomousPlayerDeeds`). When you do something a gamebot of your realm sees (a
groupmate first, else any bot within 3,000 units), it tells /faction 2-10 minutes later and one to three bots react:

- **Named kills:** a named monster (capitalised name, no realm) you kill alone (`named_solo`) or with your group
  (`named_group`) at blue or harder. (Solo needed yellow until 2026-10-27.)
- **Kill streaks:** three or more enemies killed within ten minutes (`rvr_streak`).
- **Your deaths:** zerged by three or more enemies (`ganked`; four until 2026-10-27), killed by a green or grey monster (`silly_death`), or a
  fall or drowning (`fell`).

Only what a bot saw counts: an unseen deed, an ordinary enemy kill included, is not told and does not count toward
reputation. Each kind is told at most every 15 minutes per player; a solo named kill once per monster in 15 minutes and
at most one every 5 minutes. Deeds add up in
`offline_player_reputation` (an optional progress table, emptied in release builds). Every 50-100 minutes a bot who was
not there brings up an online player's reputation ("isnt Kaelith the one who soloed Grimwold?", `rep_named`, `rep_rvr`,
`rep_silly`). Three solo named kills, 25 enemy kills or four silly deaths earn a nickname ("Kaelith the Slayer",
"Kaelith Facefloor"), announced in /faction; a heroic nickname replaces a silly one and is then for good.
`deeds.lines`: about 1,800 lines.

### 13. Storylines

`bot_living_storylines` (on by default; `AutonomousBotStorylines`). Four four-episode dramas per realm, played in
/faction through the debate engine: `killsteal`, `guildsplit`, `romance` and `ninjaloot` (`storylines.lines`, 24 scenes).
The same two bots (roles A and B, saved in `offline_bot_storylines`) play every episode and name each other; the other
roles are cast fresh. A realm plays at most one episode every 45 minutes, an arc's episodes are 3-10 hours apart, a new
arc starts at most every two hours, and a finished arc starts again with a new pair a week later. The first /faction scene
of a play session (5-10 minutes after you log in, or come back after half an hour away) is an episode when one can play.
An arc plays while its pair is in the game, even fighting; it waits while one of them is offline and is dropped after
a day. The two protagonists are never replaced by stand-ins: an episode waits up
to three minutes for a busy one, and ends unfinished (to play again later) if one leaves.

Take a side by naming one of the two in /faction during an episode or up to ten minutes after it ("Aelwyn is right",
"brannoc is lying", "team aelwy"; a line that names both counts when its clauses agree: "Brannoc ninja'd it, Aelwyn is
right"). An episode (or any debate) pauses while nobody of the realm is in the game, as when a player releases or changes
zone, and ends only after two minutes of that.
The one you backed thanks you by tell, the other tells you off, and the last episode plays that side's ending (`_4a` or
`_4b`; plain `_4` when nobody took a side), naming you. Storyline scenes never play as ordinary debates.

### 14. Gold spammers and roleplayers

`bot_living_characters` (on by default).

- **Gold spammers** (`AutonomousGoldSpammers`): 12-30 minutes into a play session, then every 1-2.5 hours, a throwaway
  name of gibberish ("Xkqhoaa") posts five to ten gold-selling ads in /faction for a made-up site. Every site has a zero or a
  three for a letter ("c0m", "n 3 t"), so none is a real address. Bots react ("reported", "/ignore"), then it is
  "banned" and the realm cheers. A /send to the spammer while it is around gets more spam back. It is only a name in
  /faction, not a character in the world.
- **Roleplayers** (`AutonomousRoleplayers`): about 4% of the Socialite and Explorer personalities (about 1.6% of all
  bots) roleplay. Everything they say, in every channel, comes out in thee and thou ("art thou well? aye, forsooth").
  The first comes 6-18 minutes into a play session, then every 20-45 minutes one posts an in-character line in /faction; other bots mock them ("/rp off plz") and they answer in
  character.

`spammers.lines` and `roleplayers.lines`: about 3,000 lines.

### 15. True rumours

`bot_living_rumours` (on by default; `AutonomousRumours`). Bots pass on rumours only when they are true:

- **A rare named spawn:** a named monster with a respawn of 20 minutes or more, level 20+, comes back in a realm's lands
  while a player of that realm within ten levels under to five over it is online. One to four minutes later, if it is
  still alive, a bot says so vaguely ("heard Grimwold is up again in Black Mountains South").
- **A thin keep:** an enemy frontier keep with four or more guards meant to be up (in the world or respawning; a captured
  keep's guards above its new level don't count), under 30% of them alive ("scout says Caer Benowyc is
  wide open"). Checked every five minutes.

A realm hears at most one rumour every 20 minutes, a monster at most every six hours, a keep at most every two.
`rumours.lines`: about 370 lines.

## Content

The corpus went from 2,326 to about 3,820 lines (`talking_audit.lines` holds the audit's reply sets, `replies_pools.lines`
the month simulation's). These files are new: `news.lines`, `news_replies.lines`, `tells.lines`,
`memory_more.lines`, `combat_calls.lines`, `lfg_real.lines`, `followups.lines`, `closings_more.lines`, `camp_more.lines`,
`guild_more.lines` and `experience.lines`. The existing files were annotated with place, news, xp and role flags,
and a few were reworded or removed.

The `lfm` lines (a bot group looking for {need} at {camp}) are written, but they are not said yet. A bot group can't
take a player, so a player who answered one would have nowhere to go.

## Tests

- `UT_TalkingUpdate`: 79 cases, covering places, `near:` needs, news topics and flag windows, siege flags and
  outcomes, sightings, bubbles, follow-up words, answers, yes/no, the thread log, combat-call rules and gaps, tell
  timing and fit, chat gaps, pull and add line fit, the one-time settings upgrade, and corpus coverage for every topic the code asks for.
- `UT_ChatSimulation`: an evening in each realm (30 openers with replies), a keep lost, a group fight and three
  conversations, played through the real corpus picker. It fails on a speaker naming another realm's zone, town or
  dungeon, an unfilled slot, or a keep named without news. Set `TALK_TRANSCRIPT=<path>` to keep the transcript.
- `UT_TalkingAudit`: the 320 audit lines (floor 93%) and the 320 holdout lines fixed after them (floor 92%), plus
  cases for names, threads, closings, self answers, place levels and world answers. `ChatAuditRoute` mirrors the live
  reply path for these; keep the two in step.
- A second independent review (2026-10-26) of the audit code found 16 problems, all fixed and covered by
  `TheReviewsMisreadsAreFixed`. The worst was a "...?" follow-up answering "yep" to a question about the bot ("u 50?" by
  tell). Others: the gear count took a drop's mob name for a crafter, realm rank was written one rank high (2L4 for 2L3),
  Darkness Falls and keep answers fired on group requests ("anyone have a df group?"), real lfg lines that mentioned a
  lost group were dropped, goodbyes and alarms read too broadly ("lfg later", "pushing the 40s"), "how many?" got a
  time, memory lines replaced factual answers, and the death reaction promised a rez from bots out of range.
  `ChatAuditRoute` still doesn't model everything live does: memory replies, knowledge lookups, the role and level filter
  on /lfm answers, and follow-ups across channels by tell. Its rates are for how a line is read, not a full replay.
- All 3,682 tests in `Tests.dll` pass (2026-10-26, Linux, no game world). CI on Windows ran the server tests and the
  launcher tests on the branch.
- An independent review of the code (2026-10-26) found eight defects, all fixed: the defaults not reaching existing
  worlds, help offers answered as duo offers, "pulling" said after the player pulled, "mezzed" before a resist, relic
  notices lost for two realms, keep news told twice, a player's /lfg taken as an answer, and "oom" outside fights.

## Not checked in game yet

Everything above was tested without a running world. These need the PC:

1. /lfg and /trade lines show in the **original client**. The server sends chat types 0x23 and 0x24, which its own
   /lfg and /trade commands use. Also check how the new client shows them (mapped to the faction tab).
2. Combat calls in a real group fight: pull, add, low health, oom, a Bard's or Enchanter's mez (said only once it
   lands), a group heal, a rez after a death. Check that they are timely, not spammy, and say nothing outside a fight.
3. A keep taken in the frontier: a bot tells /faction in its own words about a minute later, and the herald text
   isn't said twice.
4. Ask "why?" right after a bot's /faction line, and answer a bot's question ("try spindelhalla"). Check that the
   right bot answers and the others stay quiet.
5. A friend's tell 4-10 minutes after login. A duo offer from a bot of your level nearby, and "sure" to it.
6. /lfg "lfg 20 cleric" while ungrouped: a duo offer by tell within about 20 seconds.
7. Chat volume over an evening with 900 bots per realm: /faction should feel busy but readable (no more than one bot
   opener every 40 s).
8. "Where are the mids?" while enemies are in the frontier and while they aren't.
9. The server log shows no `Bot voices:` warnings from the new code (combat call, world flags).
10. Start 1.1 on the owner's world: the log names the settings it switched on, and the Living Server tab shows them.
11. Ask a bot by name and by tell: "r u 50 yet", "what lvl is vendo caverns", "is df ours", "whered u get that armor",
    "u busy". The answers should be true of that bot and the world (its level, gear, the real owner of Darkness Falls).
12. Die in a group with a healer bot: one groupmate says so, the healer first. Give a bot an order it can't do
    ("heal me" to a thane) and one it can.
13. Answer a bot's call for a group with "x", and say "ty" right after a bot's "gz": the first gets a join answer, the
    second nothing.
14. Beggars: find one in each capital (`BEGGAR_CORNER` in the log names its merchant). Check that it walks up and
    begs, that dragging coin and an item onto it moves them (your purse and pack, its purse after a restart), that
    "spare some gold?" in /say makes it follow and insult you, that asking in /faction from outside and then walking
    into the city brings the beggars to you, that it stops when you zone out of the city, and that
    it walks and paths sensibly between merchants (no walking into walls). Watch `BEGGAR_SOLD` and `BEGGAR_LISTED`.
15. Deeds: solo a blue or harder named, die to a grey monster, chain three enemy kills. A bot that was there should tell
    /faction within 10 minutes (`PLAYER_DEED` in the log); check that the named check (capitalised names) doesn't catch
    quest NPCs or other things that aren't monsters.
16. Storylines: `STORYLINE_EPISODE` in the log; the first episode should come within a few hours of play. Name one of
    the two during an episode and check the tells and, days later, the ending that names you.
17. Gold spammer (`GOLD_SPAMMER_START`, `GOLD_SPAMMER_BANNED`) and a /send to it; a roleplayer's /faction line and the
    mockery.
18. Rumours (`RUMOUR_NAMED`, `RUMOUR_KEEP`): go to the place named and check the monster or the empty keep is there.
