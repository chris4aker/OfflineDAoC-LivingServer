# Living Server Beta 1.1.4: release notes

The text below the line is the GitHub release notes for `v1.1.4-beta`. In the file table the Setup row must keep
exactly `(filled in when the exe is built)`: the release workflow replaces only that text with the exe's SHA-256. The
zip's SHA-256 is filled in when publishing. Nothing here names the Easter eggs or the project's internal vocabulary.

---

### Setup works again for new players

1.1.4 changes one thing: where Setup gets the game.

shadowofze's Offline DAoC repository is no longer online, so the 1.1.3 Setup could no longer download the game for
someone starting from nothing. Setup now downloads Offline DAoC 0.37 from Living Server's own copy of his release
files, and checks every file as it arrives.

**If you already play, nothing in the game changes.** The server and the launcher are the same as in 1.1.3. The launcher
will offer 1.1.4 as an update; taking it or leaving it makes no difference to your game.

### New installs are Offline DAoC 0.37

A new install is the original client with the Classic + Shrouded Isles classes. Setup no longer offers the other two
editions shadowofze made:

- **0.37b**, his custom Sluaghbinder class.
- **0.37c**, his new client. He continues that work in
  [Offline DAoC True 1.65](https://github.com/shadowofze/OfflineDAoC-True165).

If your game already has either, it keeps working with Living Server and keeps getting updates.

### What has been checked

- The game files in Living Server's copy were compared with shadowofze's own SHA-256 lists: the three parts of the
  game, the 0.37 update, and the download lists. All match.
- Setup's install tests passed on this release before it was attached: on a clean Windows machine they downloaded the
  game from the copy and installed it, added Living Server, updated a 0.36b game, and resumed a download that was cut
  off. These are automated tests, not yet a player's PC: please report what you see.
- The zip is for adding Living Server by hand to a game you already have.

### Files

| File | SHA-256 |
|---|---|
| `LivingServer-Setup-1.1.4-beta.exe` | (filled in when the exe is built) |
| `OfflineDAoC-LivingServer-1.1.4-beta.zip` | (zip SHA-256: filled in by hand at publish) |

### Credits

Built on shadowofze's Offline DAoC and [OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core). Reports from the
community on GitHub and r/daoc. Directed and play-tested by [chris4aker](https://github.com/chris4aker), coded with
Claude (Anthropic). GPL-3.0.

Living Server is a free, non-commercial fan project, not affiliated with Electronic Arts or Broadsword.
