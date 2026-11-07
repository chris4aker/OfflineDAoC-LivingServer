# New-client check for the Living Server

Checks that the Godot client (0.36c) shows what the Living Server sends. It is a
network-level test with no game world, so it runs on any PC with no 0.36c download.

- **Server side:** `FakeServer` accepts the client's real connection. It sends each feature's chat lines and yes / no
  boxes through the game server's own packet code (`GameClient`, `PacketLib1127`, `PacketProcessor`), with lines
  picked from the real bot-voices corpus. It also reads the client's answers to the boxes as `DialogResponseHandler`
  does.
- **Client side:** `living_server_probe.gd` runs the real network client (`DaocClient.cs`). It passes every packet to
  the game's own `ServerWorld._message` and `_dialog`, with the real in-game interface built (`GameUI`), clicks
  ACCEPT on each box, and records what the chat boxes and the screen showed.
- **Comparison:** `compare.py` checks each line's chat channel, which chat box shows it and any screen-centre text.

## Run

Needs the .NET 10 SDK, Python 3 and Godot 4.7.2 (.NET build).

1. Build the server: `dotnet build source/server/Tests/Tests.csproj -c Release` (copy
   `source/server/CoreServer/config/serverconfig.example.xml` to `serverconfig.xml` first).
2. Copy `source/new-client/godot` somewhere and import it once: `Godot --headless --path <copy> --import`.
3. `tools/newclient-compat/run.sh <Godot folder> <copy>`.

`placeholders.py` gives the copy plain stand-ins for the 36 interface files it needs (`placeholders.txt`) when the
converted art isn't there. It only adds files that are missing.

## The scripts as a 0.37c player gets them

`installed_check.py <Godot 4.7.2 executable> <work folder>` (the plain Godot build is enough; CI job `new-client`):
- takes the released 0.37c scripts from the 0.37 release commit, each checked against
  `source/tools/release-baselines/0.37/0.37c-new-client.sha256`, and lets Godot make the class cache from them;
- applies every script entry of `tools/fork-build/content-patches.json` as the installer does, and checks that each
  result is the script in `source/new-client` (a script changed without a new patch fails);
- compiles every script (`tests/load_check.gd`), then runs `tests/issue34_probe.gd`: issue #34 (Stealth in the
  Abilities list, skills onto the quickbar and used, `/stealth`, the merchant's SELL, no APPRAISE without the 1.2
  network client) with the real interface and a stand-in network client that records what would be sent.

`--released-only` skips the patches: the probe then fails on the released scripts, which is issue #34 itself.

## Not covered

- What things look like: the art is placeholders.
- Packets that need a game world: name plates with guild names, and enemy bots shown by race. They come from the
  server's NPC packets, which these features don't change.
- Real gameplay timing. Use the in-game checks in `docs/LIVING-SERVER.md` for that.
