# Living Server Beta 1.1.2: release notes (draft)

The text below the line is the draft of the GitHub release notes for `v1.1.2-beta`. It is a draft for the owner's
approval: the "How it was tested" section is finished after the owner's played check, and the SHA-256 lines are
filled in by the release workflow. Nothing here names the Easter eggs or the project's internal vocabulary.

---

### A busier realm, raids sized like the era, and bots that count in your guild

1.1.1 fixed your first reports. 1.1.2 is the first update with new things to play: a realm that talks like a full
server on a busy evening, raids the size they were in 2002-2004, quests that work with your bot group, and gamebots
that pull their weight in your guild. Under the hood, your world is now safe from crashes and backed up every day.

It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new client, like 1.1 and 1.1.1.

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
the channels, up from 1.9. A blind read of 400 earlier bot lines had judged 53% as believable; re-running those lines
gives about 70%. Those lines are known, so that is a regression check, and a fresh blind round comes with 1.2.

### Raids the size they were in the era

- **Period sizes.** Every big raid used to need 200 level 50s. Each one is now measured against the size a
  comfortable raid had there in 2002-2004, from the forum raid threads of the time, and starts a quarter above it,
  because bots play worse than 2003 players: Caer Sidi, Galladoria and Tuscaran Glacier 125, Crystal Cave 150, the
  dragons 88, Summoner's Hall, Darkness Falls and the smaller dungeons 80, down to 50 for Dodens Gruva and Tur Suil.
  The table and its sources: [docs/RAID-SIZES.md](https://github.com/chris4aker/OfflineDAoC-LivingServer/blob/main/docs/RAID-SIZES.md).
- **Mostly won, never a sure thing.** Each encounter's raid is sized from its own record in the Herald: a win trims
  the next raid there a little, a defeat adds more back, so it settles where that encounter is won about two times in
  three. **The bosses are never weakened.** The Herald notes each raid's size.
- **A time-out without a wipe is no longer a loss.** A raid that runs out its four hours without ever wiping counts as
  neither, so it doesn't make the next raid bigger.
- **After a restart** no automatic raid opens until the realm's bots have finished logging in.
- **Keep sieges remember their balance across restarts**, rebuilt from the Herald's recent sieges.

### Dragons, the Epic Special Forces, and raids that don't get stuck

- **Dragon raids regroup after a wipe.** The raid falls back to its staging ring, the fallen walk back, the living
  rebuff, and it goes in again. After two failed attempts it is defeated and goes home, and every realm hears it.
- **The Epic Special Forces answer a dragon wipe** as they do in dungeons, and now **join a full raid** too: they used
  to be turned away when the realm's own parties had filled the raid. They come with 24, fewer after wins they helped
  with, more after losses.
- **No more hours at a muster.** A raid that can't reach an encounter after three pulls sets it aside for 15 minutes
  and goes on to the others; a muster that stops gaining raiders for 20 minutes does the same. A raid now fights from a
  walkable spot even when a monster attacks it from a hard-to-reach one.

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
  regeneration** in the launcher; choose 1 with the halving on for the classic rules.

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

- **Automated:** the full server suite (4,772 server tests) and the launcher and installer tests on Windows
  pass on every build. The raid sizing was checked against a simulation of 2,000 raids; the models of how likely a
  raid of a given size is to win are estimates, not measurements.
- **Played:** [owner: one line on the overnight soak and the played check, filled in before publishing]. Already seen on a playtest PC before this release: the chat level at a 900-bot
  roster (which set the new default), the server starting from the package with its own libraries, and the
  remote-desktop Ctrl-click.
- Please report anything odd on [GitHub](https://github.com/chris4aker/OfflineDAoC-LivingServer/issues) with the
  `FORK_BUILD` line from `runtime\server\logs\server.log`.

### Known issues (it's a beta)

- **Hard encounters are still won on numbers.** Darkness Falls' raid bosses, Legion and the other hardest level 70+
  encounters have no special tactics for bots; their mechanics aren't scripted. A raid can still lose them.
- **Raiders can still stick to a boss they can't hurt** (for example one shielded by adds that must die first). The
  raid now moves on from an encounter it can't reach, but not from one it can reach and can't damage.
- **Night sieges win less often than daytime ones** in long test runs, with the same bots online. The cause isn't
  found yet; 1.1.2 logs each siege's army so the next runs can show why.
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
  that see you in RvR the way a player would, replies that don't give a bot away, and raid tactics for the classic
  encounters.
- **1.3:** live events, the guild package, a crowded prime time and a server with a past.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.2-beta.exe` | (filled in by the release workflow) |
| `OfflineDAoC-LivingServer-1.1.2-beta.zip` | (filled in by the release workflow) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC) and
[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core). Fixes ported from the forks of Cancain, rambodolphin13,
Nikse77, Rmarcoux86 and MoomoocowPlus; the save-safety design from lometur's HearthDAoC. Reports from the community on
GitHub and r/daoc. Directed and play-tested by [chris4aker](https://github.com/chris4aker), coded with Claude
(Anthropic). GPL-3.0.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
