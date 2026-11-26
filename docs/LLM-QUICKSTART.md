# Customize DAoC - Living Server with your own AI assistant

You can make Living Server your own with an AI coding assistant such as Claude Code or Codex. You
don't need the author's permission to fork it.

## 1. Get your own copy

1. **Fork it:** click **Fork** on this GitHub page, then clone your fork, or use **Code > Download
   ZIP**.
2. **Get a playable folder:** download the matching playable release (0.36, 0.36b or 0.36c) as described
   in [PLAY.md](PLAY.md). The source alone isn't the playable game.
3. **Keep two folders:** the source checkout, where you edit code, and a separate playable folder,
   where you test builds.

## 2. Start your assistant with this prompt

> This is my fork of DAoC - Living Server, a single-player DAoC server with autonomous gamebots and
> companion bots. Read AGENTS.md and docs/DEVELOPMENT.md first. My source checkout is this folder;
> my playable game is at [path to playable folder]. Look at the current code before changing it.
> Don't start the server or replace files in the playable folder without asking me, and back up
> anything you replace. Protect my account, characters, bots, items, coins and saves. Build and run
> the tests separately, make only the change I ask for, and tell me exactly what changed and how it
> was verified. The change I want is: [describe it].

## 3. Where things live

| To change… | Look in |
|---|---|
| Autonomous gamebot goals, camps, groups, travel | `source/server/GameServer/bots/autonomous` |
| Companion bots and class combat AI | `source/server/GameServer/bots` |
| Commands, combat, spells, pets | `source/server/GameServer` |
| The launcher | `source/tools/DAoCLivingServer.Launcher` |
| The new client (0.36c): interface, world, controls | `source/new-client/godot/game` (see its [README](../source/new-client/README.md)) |
| Progress transfer | `source/tools/DAoCLivingServer.ProgressImport` |
| Pet art, weapons, spell effects | `tools/pet-art` (see its README) |
| World data (spawns, spells, items) | the playable folder's `runtime/data/opendaoc.sqlite3.db`. Write changes as a script with a backup, and never commit a played database |

## Good habits

- **Test on a copy.** Use a copy of your playable folder, or at least back up
  `runtime/data/opendaoc.sqlite3.db` first.
- **Commit only source.** Commit source changes to your fork, not files from your playable folder.
- **Verify in game.** Say what was tested in game and what was only covered by automated tests.
- **Keep the licenses and credits** ([THIRD_PARTY.md](../THIRD_PARTY.md)). A pull request to this
  project is a proposal; it won't merge by itself.
