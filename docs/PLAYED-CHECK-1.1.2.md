# Played check for Beta 1.1.2 (about 30 minutes)

This is for the owner's test copy (`C:\Games\OfflineDAoC\playable-v0.37b-fresh`) with the **classic client**, on the
1.1.2 release candidate build (`test/1.1.2-rc`). It covers four things the automated tests can't show: guild dues and
merit from gamebots in your guild, a quest in a bot group, chat in Camelot, and one raid record. Write down what you
see; one line each is enough for the "Played" line of the release notes.

**Before you start**
- Close the launcher, install the candidate build (its `INSTALL.cmd`), then open the launcher and START SERVER.
- In `runtime\server\logs\server.log`, the `FORK_BUILD` line should say `name=1.1.2-beta`,
  `bot_player_guild_economy=on`, `mana_regen=1.5` and `quest_xp_by_level=on`. If your copy last ran an earlier 1.1.2
  test build, you may also see `Living Server 1.1.2.1: realm_raid_... set to the new default` lines. That is the
  raid sizing catching up once, and it's expected.
- On the launcher's **Living Server** tab, **Bots in your guild** should be on and **How much bots talk** should be 2.
- Use a normal character, not a GM. The automated runs found that a level set with a GM command gives odd XP on the
  next kill, so GM changes would spoil the XP readings below.

## 1. Guild dues and merit with gamebots (10 minutes, together with step 2)

1. Use a guild your character leads. If it has none: tick **Make Me a GM** on the launcher's Options tab, log in,
   target yourself, then `/gc quit` and `/gc create <name>`. Untick it again before step 2.
2. Set dues: `/gc dues 25` (25% is the server's maximum). Then run `/gc info` and note the **Merit Points** and
   **Bank** values.
3. Find a gamebot of your realm within a few levels of you (a low level is best: it levels fast). Target it, then
   `/gc invite` and `/invite`. It joins the guild at once, shows your guild's name over its head and talks in guild
   chat.
4. Hunt with it (step 2 below does this). Then run `/gc info` again.

**Good:**
- The **Bank** has grown: the bot pays 25% of the coin it loots into it, from its share of a group split as well.
- When the bot gains a level, the chat shows "<bot> has earned N merit points for your guild!", and **Merit
  Points** in `/gc info` goes up. There is at most one such line per guild every five minutes.
- Guild realm points don't change because of the bot. That is intended: bots never count for realm points, keep
  claims or buffs.

**Not good:** the bank never moves after coin drops the bot looted; merit never moves after the bot visibly levels.

## 2. A quest in a bot group (10 minutes)

**Which quest:** in Albion, **Hunt for Slith**. It's the kill quest with a hand-in that the automated test ran: the
target Slith drops "Slith's Tail", which you hand in. On another realm, or if you can't find its giver, any kill quest
from a quest giver in your home town will do. Your level must be inside the quest's range.

1. Take the quest, then invite one or two gamebots a few levels above the target (`/invite` on each).
2. Let the bots make the kill. Stand back, heal, or land one late hit at most.
3. If the quest asks for several kills, watch the journal after each one.
4. Hand in the quest.

**Good:**
- **Kill credit:** your quest step advances when the bots make the kill, and the step drop comes to you
  ("You receive the Slith's Tail from Slith!").
- **One count per kill:** each kill moves the counter by one, never by two or three, even with the whole group close
  by.
- **XP:** the offer window's experience matches what you get at the hand-in ("You get N experience points"), and a
  single payment never gives more than one level. The automated run's hand-in gave 9,900 XP and 9 silver 55 copper,
  for comparison. The kill's own XP is shared as before.

**Also worth a click if there's time:** two quick keyword clicks on a quest NPC's text should both get an answer.

## 3. Chat in Camelot (about 10 minutes)

Go to Camelot (or your realm's capital) at a normal evening hour with your full bot roster online. Stand in a busy
spot and just watch /faction, town /broadcast, /trade, /lfg and /say for about 10 minutes.

**Good (what the evening should feel like):**
- A steady but readable flow: about 4 lines a minute in /faction on a busy evening, plus town /broadcast (mostly
  selling, looking for a group, a little chatter), and some /say from bots passing by. Quieter late at night, never
  dead.
- Short, in-game lines about levels, classes, camps, gear and the realm's real keeps and relics, mostly lowercase,
  with the odd typo or "u".
- A question in /faction gets an answer, sometimes a short back-and-forth. It doesn't get five answers.
- No line repeated word for word within minutes, and no generic "good question" or "very true".

**Try three things:**
- Send a tell to a bot: `lvl?`, then `spec?`. You should get its real level and spec.
- Target a bot and `/say hi`. One bot answers, the one you targeted.
- Type `/lswhy budget`. It lists each channel's floor, target and ceiling and its lines per minute right now.
  `/lswhy` on a bot that just spoke shows why it said that.

**Not good:** minutes of silence in a full capital, a wall of near-identical lines, or several bots answering one
question at once. Note the time and run `/lswhy budget` when you see it.

## 4. One raid record (2 minutes, if a raid ran during the soak)

On the launcher's **Realm Records** tab, filter by **Epic dungeon** or **Dragon** and open a finished automatic raid.

**Good:**
- Its details end with "Raid size: N%." and "Wipes: N.", plus "Epic Special Forces joined: N." if they helped.
- A four-hour time-out with "Wipes: 0." is shown as a time-out but doesn't make the next raid there bigger.
- If the raid was at Krondon, Marfach or Hall of the Corrupt, the server log has `REALM_EXPEDITION_TACTIC` lines
  naming what the raid targeted (for example Scurceol's orbs before Scurceol). In a command prompt in the game folder:
  `findstr REALM_EXPEDITION_TACTIC runtime\server\logs\server.log`

**Not expected to work yet (don't count it as a failure):** Darkness Falls' raid bosses, Legion and the hardest level
70+ encounters have no raid tactics. Raids win them on numbers or lose. The 22 restored boss rules are all off.

## What to send back

One line per section: worked / didn't, with the numbers you saw (bank before/after, merit line, quest XP offered and
received, a rough /faction count per minute, the raid's size and wipes). Add the time of anything odd and the
`FORK_BUILD` line.
