# Writing bot chat lines

Line files live in `source/server/GameServer/bots/autonomous/data/chat/*.lines`. Check them with
`python tools/bot-voices/build_corpus.py --check`, then run it without `--check` to rebuild
`corpus.json` (commit both). The format is in [DESIGN.md](DESIGN.md) section 3.

## The goal

The priority: **the player feels the world is alive and that bots respond to them**. Authenticity
to DAoC 2001-2003 is a tie-breaker, not the goal. So lines should sound like real people in an MMO:
short, casual, varied, reacting to what just happened, never stiff or formal.

## Format (see docs/bot-voices/DESIGN.md section 3 for the base format)

```
@kind reply          # resets every tag below to its default
@intent thanks       # (replies) the player message intent this answers
@answers chat,joke   # (replies) the bot opener types this answers (comma list)
@who Socialite       # personality types (Leveler, Crafter, Socialite, Explorer, Daredevil, NoLifer) or *
@realm alb           # alb, mid, hib or *
@class Cleric        # only classes of that realm
@levels 20-35
@needs in_group,!in_dungeon   # flags; "!flag" means the flag must NOT be set
@invites boast       # (openers) what reply this line invites (default inferred, see below)
line text
another line
```
Tags carry to every following line until the next `@kind` (which resets them all). `@who`, `@needs` etc.
can change mid-block.

### Kinds
- Openers (start a conversation): `ambient`, `lfg`, `trade`, `rvr`, `camp`, `group` (new: talk inside a
  group), `guild` (new: idle guild chat; `@topic` = leveling, social, rvr, raiding, crafting or general).
- `reply`: answers. Needs `@intent` (answers a PLAYER's line of that intent) and/or `@answers` (answers a
  BOT's opener of that invite type). A line can carry both when it fits both.
- `closing` (new): the opener's speaker after somebody answered it ("ty", "k will try that"). Needs `@answers`
  = the opener type it closes.

### What an opener invites (`@invites`)
`none` (nobody should answer: "brb dinner"), `chat` (an observation; light reactions), `joke`, `boast`,
`complaint`, `news`, `lfg`, `trade`, `ask` (any question), `ask_where`, `ask_advice`, `ask_opinion`,
`ask_anyone` ("anyone want to...?"), or a specific question `ask_<topic>` (falls back to `ask` replies) whose
own replies are written with `@answers ask_<topic>`.
If an opener has no `@invites`, the builder infers it: a line ending in "?" becomes ask_where / ask_opinion
/ ask_anyone / ask_advice / ask by its words; otherwise lfg (kind lfg), trade (kind trade) or chat.

### Player intents (`@intent` on replies)
Existing: greeting, farewell, banter, abuse, grouping, grinding, travel, dungeon, rvr, trading, help, unknown,
ignoreoutofworld. New in se.4: thanks ("ty", "thx"), congrats ("gz" to the bot), ding ("ding!", "ding 20"),
laugh ("lol", "haha"), away ("brb", "afk"), back ("back", "re"), rez ("rez pls", "anyone rez?"), alarm
("inc", "inc 10 mids"), classtalk ("is friar good?", "best spec for cleric", "scouts op"), botcheck
("are you a bot?").

### Flags (`@needs`)
in_group, solo, in_player_group (the bot is in the real player's group), in_town (on a town break),
in_capital (Camelot, Jordheim, Tir na Nog), in_dungeon, in_rvr, outdoors (not in a dungeon or a capital),
at_camp, traveling, just_leveled, just_died, bag_full, can_rez (the bot's class can resurrect), day (6-17
on the PC's clock), evening (17-23), night (23-6), knows_player, player_nearby, player_guildless.

### Slots
{me} {class} {level} {realm} {zone} {monster} (one monster's name) {monsters} (plural) {item} {count}
{camp} {dungeon} {capital} {enemy} (another realm's name) {keep} {frontier} {q} (an optional opener like
"hey", "so") and, in replies only, {player} = the name of whoever is being answered. Builder rules:
{zone} in an opener needs a place flag (outdoors, at_camp, in_dungeon, in_capital, in_rvr, traveling or
in_town); "{count} {monster}" must be "{count} {monsters}"; a line with night words (late night, bed,
midnight, "in the morning") needs @needs evening/night/day. "tonight" needs nothing (by day the server
says "today").

## Voice

- lowercase; mostly no end punctuation except "?"; commas fine. The server adds per-bot styles on top
  (abbreviations, typos, emotes, capitalised "proper" writers), so write the plain middle.
- SHORT. Reactions 1-4 words ("lol", "gz", "same", "ouch"). Most lines 4-9 words. Rarely more than 14
  words. Hard limit 140 characters.
- Period chat words are welcome: ty, np, k, kk, lol, heh, gz, grats, gratz, brb, afk, wb, omw, gl, pst,
  lfg, lfm, inc, oom, med, pull, add, mez, rez, buffs, con (yellow/orange/red/purple/blue/green/grey),
  bubble, ding, camp, twink, zerg, gank, buffbot, uber, gimped, nerf, imo, btw, nvm, rofl, lag.
- Mix moods: helpful, tired, excited, dry, joking. Some lines a little grumpy (never cruel).
- Be specific where it helps (a real zone, dungeon, monster type, class, keep), but use slots for the
  bot's own current place/monster/level.
- Replies must make sense as an answer to MANY different openers of their type: a `boast` reply ("nice,
  gz") must fit any boast. A specific question gets specific answers via `ask_<topic>`.

## Never
- Profanity; insults about real groups; anything sexual; politics or real-world news.
- Self-aware bot lines ("i am just a bot", "why do we all say the same thing"). Exception: intent
  `botcheck` replies may joke around the question in character ("lol no", "i wish, id level while asleep").
- Later internet phrases: change my mind, fight me, vibes, love language, no thoughts, lifestyle, salty,
  the meta, i felt that, pog, based, no cap, sus, gg ez, main character, "o/" (do not add new ones).
- Out-of-era content: Trials of Atlantis (Atlantis, artifacts, master levels, ML), New Frontiers (towers,
  Molvik, Leirvik, Proving Grounds, Lion's Den, Hills of Claret, Killaloe, Braemar, Wilton), Catacombs
  (champion levels, CL, catacombs classes), Mythic, real websites, EverQuest by name (the builder bans it),
  World of Warcraft.
- Promises the server can't keep: a bot must not say it is inviting, trading or coming unless the line is
  written for a kind whose code does that (say "pst me", "inv me", "i might be free later" instead).

## World facts you can use
- Battlegrounds in this world: Abermenai 15-19, Thidranki 20-24, Murdaigean 25-29, Caledonia 30-35,
  Cathal Valley 45-49.
- Capitals: Camelot (Albion), Jordheim (Midgard), Tir na Nog (Hibernia). Starter towns: Cotswold,
  Prydwen Keep, Humberton (alb); Mularn, Audliten, Huginfell (mid); Mag Mell, Tir na mBeo, Ardee (hib).
- Dungeons by level (alb) Tomb of Mithra 7-17, Tepok's Mine 16-24, Keltoi Fogou 19-32, Stonehenge Barrows
  32-50; (mid) Nisse's Lair 8-21, Cursed Tomb 18-28, Vendo Caverns 20-35, Varulvhamn 25-40, Spindelhalla
  33-50; (hib) Muire Tomb 12-20, Spraggon Den 15-25, Koalinth Tribal Caverns 20-30, Treibh Caillte 27-40,
  Coruscating Mine 30-45. Darkness Falls is shared (open to the realm holding the most keeps).
- Old frontiers: Emain Macha (hib), Odin's Gate (mid), Hadrian's Wall (alb); mile gates (MG), portal keeps,
  relic keeps; keeps like Caer Benowyc, Caer Berkstead, Caer Boldiam, Bledmeer Faste, Nottmoor Faste,
  Dun Crauchon, Dun Crimthainn, Dun Bolg.
- More place names, slang and period arguments: docs/bot-voices/RESEARCH-BRIEF.md and
  docs/bot-voices/PERIOD-CHAT-RESEARCH.md.
- Classes by realm: alb Armsman, Paladin, Mercenary, Reaver, Scout, Infiltrator, Minstrel, Cleric, Friar,
  Wizard, Theurgist, Sorcerer, Cabalist, Necromancer; mid Warrior, Thane, Skald, Berserker, Savage, Hunter,
  Shadowblade, Healer, Shaman, Runemaster, Spiritmaster, Bonedancer; hib Hero, Champion, Blademaster,
  Warden, Druid, Bard, Ranger, Nightshade, Eldritch, Enchanter, Mentalist, Animist, Valewalker.
  A `@realm *` line must not name another realm's class as "ours" (a Midgard bot can't be a wizard).

Every line is new writing; don't copy text from games, forums or other projects.
