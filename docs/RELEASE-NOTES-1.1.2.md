# Living Server Beta 1.1.2: release notes

The text below the line is the GitHub release notes for `v1.1.2-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in by hand when publishing. Nothing here names the Easter eggs or the project's internal
vocabulary.

---

Corrected on 2026-11-08: some claims below were overstated; see Known issues.

### Raids that know their bosses, a realm that talks like a full server, and bots that count in your guild

1.1.1 fixed your first reports. 1.1.2 is the first update with new things to play. Bot raids get their first boss
tactics and are sized like the raids of 2002-2004. The realm talks like a full server on a busy evening. Quests work
with your bot group, and gamebots pull their weight in your guild. Under the hood, your world now opens in a crash-safe
mode and is backed up every day.

It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client, like 1.1 and 1.1.1.

### Raids that know their bosses

Some bosses can't be beaten by hitting harder, and bot raids used to try anyway. Five of them now have a plan, like
the one a 2003 raid leader would have called. Two of the five did not work on a real install (Scurceol and Xanxicar):

- **Scurceol Hyrde (Krondon)** can't be hurt while his four floating orbs live. The raid is meant to shoot the orbs
  first. On a real install (a later build with the same orbs code) it could not find a spot to shoot most of them
  from, so it skips him and goes on to the other bosses.
- **Black Lady (Marfach Caverns)** is fought at home. Raids used to drag her out of her room until she went back at
  full health; now they keep her in reach of her room and lead her back when she strays. Her Ancient Black Oak falls
  with her, so nobody wastes the night on it.
- **Aidon the Archwizard (Hall of the Corrupt)** only summons new illusions once all four are dead, so the raid kills
  three and leaves the last one standing, then everyone fights him.
- **Nosdoden (Trollheim)** sends a wave of essences as his health falls. The raid stops and clears each wave before
  going back to him, so the waves don't pile up.
- **Xanxicar (Crystal Cave)** stomps everything around him at set points in the fight. In 1.1.2 the raid does not get
  out in time: most of the raid can die in the first stomp. Fixed in 1.1.3.

For every raid:
- **A boss that jumps the raid before it's ready** is answered by the whole raid at once, or the raid pulls back
  together and lets it reset, instead of trickling in by twos and threes.
- **A lost skirmish no longer uses up an attempt.** Only a fight most of the raid was in, or lost half its people in,
  counts as one of the raid's two tries.
- **Raids muster where the boss lives**, and walk to where it actually stands, not to where it last chased someone.
- **The Darkness Falls princes no longer eat bots.** Princes Asmoien and Ba'alorien devour the nearest pet every few
  seconds and heal by its health, but they took companion bots and gamebots for pets too, so a raid could never get
  them down. They now take only summoned pets. Found in this release's long test run.

*The encounter catalog:* every scripted boss in the game was read from its script into one catalog: what it does,
when, and what a raid needs to do about it. That reading also found 23 places where the game's bosses had been
softened for bot raids. For 22 of them the original rule, read from the OpenDAoC project's own code, is back in the
game behind 17 switches, **all off**: those bosses play exactly as in 1.1.1, and each original rule is turned on only
once the raid has a tactic for it.

### Raids the size they were in the era

- **Period sizes.** Every big raid used to need 200 level 50s. Each one is now measured against the size a
  comfortable raid had there in 2002-2004, read from the forum raid threads and guides of the time, and starts a
  quarter above it, because bots play worse than 2003 players: Caer Sidi, Galladoria and Tuscaran Glacier 125,
  Crystal Cave 150, the dragons 88, Summoner's Hall, Darkness Falls and the smaller dungeons 80, Hall of the Corrupt
  60, down to 50 for Dodens Gruva and Tur Suil. The table, with each thread's date and quote:
  [docs/RAID-SIZES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RAID-SIZES.md).
- **Mostly won, never a sure thing.** Each encounter's raid is sized from its own record in the Herald: a win trims
  the next raid there a little, a defeat adds more back, so it settles where that encounter is won about two times in
  three. **The bosses are never weakened.** The Herald notes each raid's size.
- **A time-out without a wipe is no longer a loss.** A raid that runs out its four hours without ever wiping counts as
  neither, so it doesn't make the next raid bigger.
- **After a restart** no automatic raid opens until the realm's bots have finished logging in.
- **Keep sieges remember their balance across restarts**, rebuilt from the Herald's recent sieges.
- **When attackers keep winning, fewer of them come.** In a night's run of a later development build, attackers
  still took 8 of 11 keeps.

### Dragons, the Epic Special Forces, and raids that don't get stuck

- **Dragon raids regroup after a wipe.** The raid falls back to its staging ring, the fallen walk back, the living
  rebuff, and it goes in again. After two failed attempts it is defeated and goes home, and every realm hears it.
- **The Epic Special Forces answer a dragon wipe** as they do in dungeons, and now **join a full raid** too: they used
  to be turned away when the realm's own parties had filled the raid. They come with 24, fewer after wins they helped
  with, more after losses.
- **No more hours at a muster.** A raid that can't reach an encounter after three pulls sets it aside for 15 minutes
  and goes on to the others; a muster that stops gaining raiders for 20 minutes does the same. A raid now fights from a
  walkable spot even when a monster attacks it from a hard-to-reach one.

### A realm that talks like a full server

- **More chat, scaled to the crowd.** Chat used to stop growing at about 390 bots a realm. Now /faction, /lfg,
  /trade, /say near you, town /broadcast, guild and group chat each rise with the bots online and follow the time of
  day: busy evenings, calmer nights, never dead.
- **Town /broadcast is used the way players used it:** selling, looking for a group, a little chatter.
- **Big moments ripple through chat.** A keep taken or lost, a relic moved, a raid won or wiped, Darkness Falls
  changing hands: each draws a short burst of reactions in /faction, and the realm-wide ones reach the capital.
- **Bots answer what you said.** Ask a bot its spec, level or weapon by tell and you get the real one. A call for
  help gets "omw" or "where?", a price offer a price or a counter, a goodnight a goodnight. Talking in /say to the bot
  you've targeted, or the one right in front of you, counts as talking to it. A question gets one answer, not five.
- **Regulars and threads.** Each realm and guild has a few talkative regulars on an evening; questions get answered,
  some threads run on for a few lines, and a ding to 50 or a wipe becomes a short story others riff on.
- **Less filler.** Most unprompted lines are now short and about the game in front of the bot: its level, class,
  zone, bags, and the realm's real keeps and relics. Generic agreement and near-copies are gone.
- **How much they talk** is a new setting in the launcher's Living Server tab (0-3). The default is now 2, measured at
  a 900-bot roster; an install that never changed it moves to 2 once.
- **`/lswhy`** shows why a bot said what it said, or why nobody answered; `/lswhy budget` shows how busy each channel
  around you is.

*The research:* the channels, places, hours and bursts come from a study of 2003 chat; the levels are set on purpose
for a busy peak server. In a simulated evening at 1,000 bots a realm, a player hears about 12.7 lines a minute across
the channels, up from 1.9. On a real install with about 600 bots a realm (the 1.1.3 build, same chat) it was 15 to 19,
so the simulation guesses low.
In a blind test of 400 player lines (one line at a time, not conversations), the bots read 53% of them as intended;
re-running the same lines gives about 70%. A blind judge still picked out most bot lines (72% in the first round, 85% in
the latest). Conversations over several turns have not been tested.

### Quests with your bot group

- **Your bot group's kills count for your quests.** Heal, nuke late or just tag: when the gamebots in your group make
  the kill, your step advances and the classic step drop comes. Experience, loot and coin are shared as before.
- **A kill advances a step once**, not two or three steps on one kill.
- **Hand in a quest you've outlevelled**, and keep more than 25 quests: the rest wait and appear as you finish others.
- **Two quick keyword clicks** on a quest NPC both arrive.
- **Quest XP follows your XP-by-level rate**, with at most one level per payment, and the offer window shows what you'll
  really get. The setting `quest_xp_by_level` turns it off for the old amounts.

### Your guild, your power bar

- **Gamebots in your guild earn merit and pay dues,** like players: merit for their levels and realm ranks, dues from
  the coin they loot, into the guild bank. Switch: **Bots in your guild** in the launcher.
- **Power comes back faster by default** (1.5 times the classic rate, between the classic rate and patch 1.87's), and
  the halving below 50% is off. If you turn the halving on, it now applies to bots as well as to you. Switch: **Power
  regeneration** in the launcher; choose 1 with the halving on for the classic rules. A world that never changed
  these two settings moves to the new defaults once, at its first start; values you set yourself are kept.

### Fixes from other forks of Offline DAoC

Chosen from a survey of every active fork; each is credited to its author.
- **Gamebots in your group who die in a battleground come back to you** from the portal keep (issue #39; by Cancain).
- **`/friend` works with gamebots,** and two stock friends-list bugs are fixed (issues #38, #40; by Cancain).
- **`/allrelease`** releases every dead bot in your group at once (issue #40; by Cancain).
- **A raised XP rate now reaches Darkness Falls, battlegrounds and the frontiers** (by rambodolphin13; also found by
  BeezKneezItem9).
- **Fewer stuck gamebots:** zone seams, stalled group members, ranged pulls, mezzed enemies, getting out of Darkness
  Falls and out of a keep just taken (by Nikse77, via rambodolphin13; the keep doors by rambodolphin13).
- **Water breathing potions work again** (by Rmarcoux86).
- **The gamebots' camp list no longer breaks** on a monster with no zone (by MoomoocowPlus).

### Safer saves, private by default

- **Crash-safe world database.** The server always opens your world in a crash-safe mode (WAL), checks it at each
  start, and writes a verified **daily backup** to `runtime\data\backups` (the last 7 are kept).
- **The installer backs up your world** before it changes anything.
- **This PC only by default.** A missing server config now comes back listening on this PC only, with router
  port-forwarding off. Your own config isn't changed; if it's open to the network, the log tells you how to close it.
- **The Herald's raid and siege records move** from `runtime\server` to `runtime\data`, next to your world, so
  backups and progress transfers carry them. The server moves them at its first start; nothing is lost.
- **No more "database is locked"** on a busy read.

### Updates from the launcher

- **Check for updates** beside the launcher's version (and once a day, unless you switch it off) asks GitHub for a
  newer Living Server. When there is one, START SERVER reads **GET UPDATE**: Install, Later or Skip this version. The
  download is checked against GitHub's published SHA-256 and installed with the usual backups. Nothing about you or
  your game is sent.
- **Setup installs the newest Living Server**, even when the Setup you downloaded is older.

### Other fixes

- **An Animist turret no longer vanishes** when its target does.
- **Repairs need the skill** they were always meant to need (half the item's craft level).
- **The Herald counts the gamebots in your guild.**
- **A GM's level or rank set by command is no longer a server first**, and it's "an elementalist", not "a elementalist".
- **Bots don't sell their level-50 starter kit in chat**, and "what does your guild do?" gets an answer about the guild.
- **A race this server doesn't offer is refused cleanly** instead of causing an error.
- **The package carries the server's own libraries**, so the new server starts beside them.
- **The launcher explains a server stuck on STARTING**, and says when the server stopped by itself.
- **INSTALL.cmd installs into the game folder it sits in**, even with other installs beside it.
- **Ctrl-click and Shift-click on "+ Lv."** work from remote-desktop tools.

### Install

- **New to Offline DAoC, or adding Living Server:** run `LivingServer-Setup-1.1.2-beta.exe`. Windows SmartScreen may
  say "Windows protected your PC" because the exe isn't code-signed: click More info, then Run anyway.
- **Have 1.1 or 1.1.1 already:** run the Setup, or unzip `OfflineDAoC-LivingServer-1.1.2-beta.zip` into your game
  folder and run `INSTALL.cmd` with the game and server closed. Your characters, bots, guilds and settings stay.
- **To undo:** `ROLLBACK.cmd`. It puts every replaced file back; it never puts your database back, because that would
  undo your play since. The installer's own database copy is in `runtime\server\deployment-backups\`.

Real loot, the Realm Exchange, stablemaster routes, selling at natural breaks and bots that earn their armor are
untouched.

### How it was tested

- **Automated:** the full server suite (4,820 server tests) passes, and on Windows CI the server tests, the
  launcher's Living Server tab, update and Herald tests and the installer script tests passed on this release's
  build. Setup's own end-to-end tests run when the release is built. The raid sizing was checked against a simulation
  of 2,000 raids; the models of how likely a raid of a given size is to win are estimates, not measurements.
- **Run, not played:** the release candidate ran unattended for 22 hours on a copy of a real install, with nobody in
  the game. 13 of its 19 raids were won (the six losses were all among the hardest epics, one of them to the
  Darkness Falls princes bug fixed above), in its last sieges attackers took about two keeps in three, and the
  server never stopped.
  This release is that build plus the princes fix. Setup also upgraded a copy of a real Beta 1.1 install to it, and
  the server then started with its settings moved to the new defaults.
- **Played:** the owner's own played check
  ([PLAYED-CHECK-1.1.2.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/PLAYED-CHECK-1.1.2.md))
  has not been done yet.
- **Checked by Claude through computer use** (not the owner's play): logging in, tells, chat, a guild invite and guild
  dues in the game client.
- **Seen on a playtest PC before this release:** the chat level at a 900-bot roster (which set the new default), the
  server starting from the package with its own libraries, and the remote-desktop Ctrl-click.
- **Seen on a real install since release** (bot raids, nobody watching). With the 1.1.3 build: crash-safe mode, the
  start check, the daily backup and the siege balance rebuilt at a restart. With a later development build: Black Lady
  fought at home and killed, Prince Ba'alorien killed twice, and a dragon raid regrouping after a wipe with the Special
  Forces.
- Please report anything odd on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
  `FORK_BUILD` line from `runtime\server\logs\server.log`.

### Known issues (it's a beta)

- **/faction can get too busy.** On an evening with about 600 bots a realm (the 1.1.3 build, whose chat is 1.1.2's) it
  ran well above its ceiling, up to 20 lines a minute, with bursts of 10 to 16 reactions. Being fixed. Meanwhile,
  lower **How much they talk** in the Living Server tab.
- **Bot raids skip Scurceol Hyrde.** On a real install the raid killed 1 of his 4 orbs, set him aside and went on to
  the other bosses. Being fixed.
- **Xanxicar's stomp can kill most of the raid.** Fixed in 1.1.3.
- **Attackers still take most keeps.** In a night's run of a later development build they took 8 of 11, with the
  balance at its limit for defenders.
- **Only five bosses have tactics so far.** Darkness Falls' raid bosses, Legion and the other hardest level 70+
  encounters are **not** implemented for bot raids: they are still won on numbers, and a raid can still lose them.
  Other bosses with special rules are fought as before.
- **The new tactics have been seen only in server logs.** In the 22-hour run, Marfach Caverns and Trollheim were won
  and Aidon's illusion tactic acted, but nobody watched those fights in the game client. Krondon and Crystal Cave
  didn't come up in that run; on a real install since, both tactics failed (above).
- **The hardest epics still lose or time out.** In the same run Summoner's Hall, Galladoria, Hall of the Corrupt and
  Caer Sidi were lost or ran out of time. Galladoria's Easmarach and Aidon's last illusion are sometimes set aside as
  "no damage for five minutes"; fixes for both are being built for 1.2.
- **The 22 restored boss rules stay off.** Turning one on before the raid has a tactic for it would make that boss
  much harder for bots.
- **The Darkness Falls princes fix has been seen working since:** on a real install, with a later development build,
  a bot raid killed Prince Ba'alorien twice.
- **New raid sizes need a few raids to settle.** Each encounter's size moves with its own wins and losses, so the
  first nights may swing either way.
- **Updates from the launcher start after 1.1.2.** 1.1.1 has no update check, so 1.1.2 itself comes by Setup or the
  zip. The full GET UPDATE path has run on Windows CI but will first be seen on a player's PC with the next release.
- **The character screen gives no message** when it refuses a race this server doesn't offer: the character just
  doesn't appear.
- **Bots don't fall when they die:** the game client keeps a dead pose only for players.
- **More:** [docs/KNOWN-ISSUES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/KNOWN-ISSUES.md).
  Every change, with its numbers: [CHANGELOG-LIVING-SERVER.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/CHANGELOG-LIVING-SERVER.md).

### Coming next

- **1.2, bots that play with you:** bots that invite you, groups led by a bot that you can simply follow, gamebots
  that see you in RvR the way a player would, and replies that don't give a bot away.
- **1.3:** live events, the guild package, a crowded prime time and a server with a past.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.2-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.2-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC) and
[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core). Fixes ported from the forks of Cancain, rambodolphin13,
Nikse77, Rmarcoux86 and MoomoocowPlus; the save-safety design from lometur's HearthDAoC. Reports from the community on
GitHub and r/daoc. Directed and play-tested by [chris4aker](https://github.com/chris4aker), coded with Claude
(Anthropic). GPL-3.0.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
