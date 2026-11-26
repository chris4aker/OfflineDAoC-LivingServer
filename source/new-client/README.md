# The new client (0.36c) - source

The new client is a game client for the Offline DAoC server, built in Godot 4.7 (.NET). It speaks the DAoC
network protocol of the original client (game client version 1.127) to the same server as 0.36b, so it needs no
server changes: everything it shows is decoded from packets the server already sends. Players get it ready to
play from the [v0.36c release](https://github.com/shadowofze/OfflineDAoC/releases/tag/v0.36c); see
[docs/NEW-CLIENT.md](../../docs/NEW-CLIENT.md).

## Layout

| Path | What it is |
|---|---|
| `godot/` | The Godot project. `game/` is all client code (GDScript, plus the network client in C#: `game/net/DaocClient.cs`). `game/net/server.gd` and `game/net/server_world.gd` turn the server's packets into the game world and interface. |
| `godot/tests/` | Headless checks and debug probes (`tests/load_check.gd` compiles scripts with the autoloads present). |
| `godot/tools/` | Content maintenance scripts run inside Godot (`pack_scenes.gd` packs converted models into `.scn` files). |
| `pipeline/` | The Python content pipeline: it reads the original DAoC client's files (zones, NIF models, textures, sounds, interface art, game data) and writes the converted content into `godot/content`. `nif_static.py` / `nif_character.py` read NetImmerse models (`spec/nif.xml` from NifTools). |
| `tools/build_csharp.ps1` | Builds the C# network client into `godot/.godot/mono/temp/bin/Debug/MMOEngine.dll`. |

The repository holds the source only. The converted content (`godot/content`, about 33 GB) is derived from the
original game client's files and comes with the 0.36c download.

## Running from source

1. Install the .NET 10 SDK and Godot 4.7.2 (.NET build).
2. Copy `new-client/godot/content` from a 0.36c install into `godot/content` (or build it with the pipeline,
   below). `godot/content/.gdignore` must stay: Godot must not import the content folder.
3. Build the network client: `tools\build_csharp.ps1` (run it again after every change to a `.cs` file; starting
   the game with `--path` does not compile C#).
4. Start a 0.36b server (its launcher), then run
   `Godot_v4.7.2-stable_mono_win64.exe --path godot -- --server=127.0.0.1 --account=<name> --password=<password>`
   with the account from that game's `runtime\account.txt`.

The release package runs it the same way: the launcher in `new-client\runtime` (the main launcher in its new-client
mode, `source/tools/DAoCLivingServer.Launcher`) starts the engine with these arguments and `APPDATA=new-client\save`.

## Developer mode

The debug text (Ctrl+F12), the bot inspector (Ctrl+F11) and the Ctrl+F1..F11 test keys work only when
`godot/dev_mode.txt` exists or the game is started with `--dev` (`game/ui/boot_settings.gd`). Release packages
never include that file.

## The content pipeline

The pipeline needs Python 3.10+ (`pipeline/requirements-full.txt`), Real-ESRGAN for the 4x texture upscale
(`tools/realesrgan/realesrgan-ncnn-vulkan.exe` with its `models` folder), and the original DAoC client's files (set `DAOC_CLIENT` to the client's folder; the 0.36 release
content was built from the 0.36b client, so the Sluaghbinder's custom effects are included). Each `build_*.py`
script says at its top what it builds and how to run it. A full rebuild takes many hours. After a model or effect
rebuild, repack the scenes:
`Godot --headless --path godot -s res://tools/pack_scenes.gd -- --force res://content/monsters/effects`.

## Release

`source/tools/build_newclient_036.py` builds the 0.36c package from a development copy (without `dev_mode.txt`,
the debug probes and anyone's `save` folder) and `source/tools/seal_newclient_036.py` packs it into verified,
independent download parts. `source/tools/sync_newclient_source.py` copies this source into the repository.
