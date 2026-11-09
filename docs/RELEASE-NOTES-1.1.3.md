# Living Server Beta 1.1.3: release notes

The text below the line is the GitHub release notes for `v1.1.3-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing. Nothing here names the Easter eggs or the project's internal vocabulary.

---

### A hotfix: chat that holds together, out of Xanxicar's stomp, two Darkness Falls exits, and a patient Setup

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

### The raid gets out of Xanxicar's stomp

In a bot raid on Xanxicar in Crystal Cave, his first stomp killed 110 of 185 raiders in one second. The order to get
clear came too late, reached only some of the raid, sent raiders to a spot inside the cave's rock, and their melee
follow walked them straight back to him.

Now the raid pulls back in good time before each stomp. Every raider near him goes, whatever it is fighting, to the
nearest open spot on the cave floor that the stomp can't reach, and waits there until it is over. The party fighting
him still holds him. Xanxicar himself is unchanged: same health, same stomp.

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
  checked by a tool that reads the chat log. It has not been played in game yet.
- Xanxicar on a real install, with the 1.1.3 build before the chat fix (the stomp code is the same): one Albion raid of 280 bots killed him in 23 minutes with no wipe.
  His four stomps killed 6, at most 1, 6 and 5 raiders. That is one raid, so treat it as a good sign, not a promise.
- Earlier stages of the same fix were run in a test world and overnight on a real install. One of those overnight
  raids still lost 54 raiders to one stomp. This update has two later changes on top of that stage.
- The Darkness Falls exits are covered by tests against the released world's exit spots. They have not been clicked
  in game yet.

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
