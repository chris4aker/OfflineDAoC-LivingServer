# Living Server Beta 1.1.3: release notes

The text below the line is the GitHub release notes for `v1.1.3-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing. Nothing here names the Easter eggs or the project's internal vocabulary.

---

### A hotfix: chat that holds together, out of Xanxicar's stomp, Scurceol's orbs, two Darkness Falls exits, and a patient Setup

1.1.3 is a small update on top of 1.1.2. It's for Offline DAoC **0.37 and 0.37b**, with or without the **0.37c** new
client, like 1.1.2. Install it over 1.1.2 (or earlier) and keep everything; the launcher offers it as an update.

### Chat that holds together

In /faction the bots' conversations could come apart: an answer before its question, a thanks for an answer that never
came, "sup" at the very end, a cabalist saying "me" to "need a tank", and a kill line cheered by five or six bots over a
minute and a half, with "oof" heard again and again.

Now a conversation is said in order, one line after another, and stops when someone can't go on, so nobody answers
nothing. A bot answers a call for a tank, a healer or its own class only if that is what it plays, and only near the
level asked for. Follow-up questions fit what was said. A bot's own good or bad moment draws one or two replies, big
realm news a few, and the same short "oof" or "rip" isn't heard twice in ten minutes. The realm is just as busy; it
reads like people talking to each other.

In a dunce's scene, a debate or a storyline episode, a part stays with the bot that started it: if that bot is in a
fight it still has its say a little later, and if it has left the game the scene ends. A gold spammer, a rumour or a
roleplayer's line that has to wait for a busy /faction now gets its turn a few seconds later instead of being lost.
Bots coming back from their bind tell their own group, so a busy bind is no longer a wall of "released, running back"
in /say. A bot greets you once when you meet, and a bot's story of your deeds names a place where it could have
happened. These were found in the owner's check of the first release candidate and checked again on the second.

### The raid gets out of Xanxicar's stomp

In a bot raid on Xanxicar in Crystal Cave, his first stomp killed 110 of 185 raiders in one second. The order to get
clear came too late, reached only some of the raid, sent raiders to a spot inside the cave's rock, and their melee
follow walked them straight back to him.

Now the raid pulls back in good time before each stomp. Every raider near him goes, whatever it is fighting, to the
nearest open spot on the cave floor that the stomp can't reach, and waits there until it is over. The party fighting
him still holds him. Xanxicar himself is unchanged: same health, same stomp.

### Scurceol Hyrde's orbs are shot down from the floor

Scurceol Hyrde in Krondon can't be hurt while his four floating orbs live. 1.1.2 taught the raid to shoot the orbs
first, from the floor. In the owner's overnight test the raid never found a place to shoot from: one orb died, Scurceol
lived, and he killed 156 raiders.

The reason was in the dungeon's map data. Each orb floats just above its own rock perch, across a deep pit from the
platform the raid stands on. The check for "can I see it from here" walked along the floor to the spot under the orb,
and nobody can walk across a pit. Now the raid looks for spots all around each orb on Scurceol's platform, well away
from him, from which a straight line to the orb is clear. Each party's casters and archers go to their own orb's spot
and shoot from there. Scurceol and his orbs are unchanged.

### Two Darkness Falls exits work

The exit by Legion's hall and the one near the Midgard boss did nothing when you clicked them. Each now takes you to
your realm's usual Darkness Falls exit, like the others. Nothing is written to your world for this. Found and fixed by
rambodolphin13.

### Setup waits out a busy download server

A player's Setup stopped with a "503" error while downloading. That error means GitHub's download server was busy for a
moment. Setup now waits and tries again by itself, up to four times over about seven and a half minutes, and only for errors like
that one. If it still can't download, it says so plainly and changes nothing.

### How it was tested

- Automated: every server, launcher and Setup test passes on GitHub, including a real download install.
- Chat: new tests for each part, and a simulated three-hour evening of /faction played with the old timing and the new,
  checked by a tool that reads the chat log.
  On a real install (the owner's PC, a test copy of the game), the same tool read 68 minutes of an evening on the
  second release candidate and 34 minutes on the third (the server code that ships): no line out of order, no bot
  claiming a class, level or role it doesn't have, and /faction under its ceiling. Started on purpose with a test command: a dunce scene played by one
  bot from start to finish, rage quit included; a gold spammer; a rumour that honestly says it doesn't know one.
  Seen without any prompting: a guild invite, a bot joining the group and talking in it, that bot remembering the group
  afterwards, and tells from bots that remembered earlier groups. Not seen yet: a bot greeting you again when you meet
  it later. Still too repetitive: different bots said the same "dead. again" seven times in half an hour.
- Xanxicar on a real install, with the 1.1.3 build before the chat fix (the stomp code is the same): one Albion raid of 280 bots killed him in 23 minutes with no wipe.
  His four stomps killed 6, at most 1, 6 and 5 raiders. That is one raid, so treat it as a good sign, not a promise.
- Earlier stages of the same fix were run in a test world and overnight on a real install. One of those overnight
  raids still lost 54 raiders to one stomp. This update has two later changes on top of that stage.
- Scurceol's orbs on a real install, with the third release candidate (the server code that ships): one Albion raid
  of 200, started from the launcher's Realm Events tab, shot the four orbs down in 10 minutes, each from its own spot
  on the floor, and killed Scurceol 50 seconds after the last one. None of the raid died to him or his orbs. One
  raider was stuck in another dungeon and the raid waited for it at the gathering town, so it was sent back by hand
  (it is in the known issues). That is one raid, so treat it as a good sign, not a promise.
- Scurceol's orbs in tests. Automated tests run on a recorded copy of Krondon's real map data around the orbs: every
  orb gets a spot the raid can walk to, in range and with a clear line, and the 1.1.2 search finds none there, as on
  the owner's PC.
  In a test world (the development container, not a real install), with this fix on the 1.1.3 build before the chat
  fix and a test-only way to open the raid: an Albion Krondon raid (80 needed, about 100 taking part) killed all four orbs in 43 minutes and
  then Scurceol in just over a minute. He killed 16 raiders. A second run killed them too, more
  slowly: the orbs took an hour and 45 minutes, and he killed 6. An earlier stage of the fix, with one spot for
  all four orbs, was stopped at the first orb: in 15 minutes the raid had brought it to half.
- The Darkness Falls exits are covered by tests against the released world's exit spots. On a real install, with the
  second release candidate (the exit code is the same in this build), both exits took an Albion character to Albion's
  usual Darkness Falls exit in Camelot Hills.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.3-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.3-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on [shadowofze's Offline DAoC](https://github.com/shadowofze/OfflineDAoC) and
[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core). The Darkness Falls exits from rambodolphin13's fork. Reports
from the community on GitHub and r/daoc. Directed and play-tested by [chris4aker](https://github.com/chris4aker), coded
with Claude (Anthropic). GPL-3.0.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
