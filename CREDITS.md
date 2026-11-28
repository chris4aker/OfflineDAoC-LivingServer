# Credits

**DAoC - Living Server** is a free, non-commercial, single-player server for Dark Age of Camelot. It is maintained by
[chris4aker](https://github.com/chris4aker) and built first of all on **OpenDAoC** and **Dawn of Light (DOL)**, whose
open-source server code it runs on, and on the other work below. The launcher's **About** shows the
same list.

> Dark Age of Camelot is the intellectual property of Electronic Arts and Broadsword Online Games. DAoC - Living Server
> is not affiliated with, endorsed by, or connected to either company. The game client, textures, models, sounds and
> other assets it uses belong to them.

## License

DAoC - Living Server is free software under the **GNU General Public License, version 3** ([LICENSE](LICENSE)), the
license of the work it is built on. The server keeps its own license texts and per-file notices:
[source/server/LICENSE](source/server/LICENSE), [gpl.txt](source/server/gpl.txt) and
[gplheader.txt](source/server/gplheader.txt). Those notices, and the authorship of ported commits, are kept as GPL v3
section 5 requires. Third-party notices for dependencies, tools and data sources are in [THIRD_PARTY.md](THIRD_PARTY.md).

## Built on

- **Dawn of Light (DOL)**, "the first free open source DAoC server emulator", by the Dawn of Light Development Team.
  Its GPL headers stay in the source files.
- **[OpenDAoC](https://github.com/OpenDAoC/OpenDAoC-Core)** (OpenDAoC-Core), the server emulator underneath, by the
  OpenDAoC team and contributors. Ported upstream code keeps its authors, among them Baptiste Marie and Telchar.
- **The original Offline DAoC**, by [shadowofze](https://github.com/shadowofze). It is the single-player game with bots
  this project started from: the 0.3x releases, their bots, launcher and world, and the downloader script Setup still
  uses to unpack the base game. Its release files are kept in
  [chris4aker/OfflineDAoC-base-files](https://github.com/chris4aker/OfflineDAoC-base-files), checked against the
  original SHA256SUMS, since the original repository went offline in November 2026. Its README is kept in
  [docs/history/OFFLINE-DAOC-README.md](docs/history/OFFLINE-DAOC-README.md).
- **[OfflineDAoC-True165](https://github.com/zomboden/OfflineDAoC-True165)** (True 1.65), shadowofze's later project:
  hired companions and several boss, camp and raid fixes. Each ported commit keeps him as its author.

## Forks and contributors

- **[stefanrows/OfflineDAoC](https://github.com/stefanrows/OfflineDAoC)**: bot-guild membership, route bends, danger
  memory and Join a Friend.
- **[rambodolphin13/DividedRealms](https://github.com/rambodolphin13/DividedRealms)**: fixes and ideas, and the Join a
  Friend port.
- **lometur's HearthDAoC**: the save-safety design, among other fixes.
- **[FelixGagnonB/OfflineDAoC](https://github.com/FelixGagnonB/OfflineDAoC)**: an engine fix.
- **[DetShaft/OfflineDAoC-LivingServer](https://github.com/DetShaft/OfflineDAoC-LivingServer)**: a fork that is watched
  for fixes.
- **Cancain**: bots returning from battlegrounds, `/friend` with gamebots and `/allrelease`.
- **Nikse77, Rmarcoux86 and MoomoocowPlus**: fixes and ideas.
- **[mod-playerbots](https://github.com/mod-playerbots/mod-playerbots)** (AzerothCore): the idea of a per-pair trade
  discount for the bot trade (no code taken).
- **Krolgosh-web**: the report behind the lag fix for large populations (issue #25).
- **nindybrew-svg**: the report behind bots that kept following you after a disband (issue #33).
- **joshuaforehand**: the report behind the launcher staying on STARTING (issue #32).
- **BeezKneezItem9**: the report behind the raised XP rate in Darkness Falls, battlegrounds and the frontiers.
- **Zarthol** (r/daoc): the report behind weapon styles for base classes before level 5.
- **ThatRebelKid** (r/daoc): the report behind quiet song twisting.
- Everyone on GitHub and r/daoc who reported what they found.

## Formerly

Living Server was first published as **the Social Expansion** (0.36-se.1 to 0.37-se.2), then as **Offline DAoC:
Living Server** (1.0 to 1.2.2), an add-on to Offline DAoC. From 1.2.3 it is named **DAoC - Living Server** (owner,
2026-11-25, issue #641). The GitHub repositories keep their names, and older releases keep theirs.

The code is written with Claude (Anthropic).
