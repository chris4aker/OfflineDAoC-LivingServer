# New-client scripts for Living Server 1.2

The 0.37c new client (Godot 4.7, C#) runs its GDScript from source in `new-client/godot/`, so a changed `.gd` file
can reach players as a byte patch in `tools/fork-build/content-patches.json`. Its C# code is compiled into
`new-client/godot/.godot/mono/temp/bin/Debug/MMOEngine.dll` and cannot. This page lists every new-client file that
differs between shadowofze's tag `v0.37` and `release/1.2` (shadowofze's final code, merged 2026-11-01). For each file
it says whether 1.2 ships it, and why.

How it was checked (2026-11-01):
- Released bytes come from `source/tools/release-baselines/0.37/0.37c-new-client.sha256`. Every modified script's
  `v0.37` git blob matches that manifest exactly as stored, so no line-ending conversion was needed. The released
  files mix conventions: most are CRLF, and `server.gd`, `player.gd`, `game_ui.gd`, `zone_loader.gd` and eight other scripts
  are LF (as is `DaocClient.cs`). Every shipped `release/1.2` blob already uses the same convention as its released file.
- A script was refused when its 1.2 version needs something the installer does not ship (see "Why scripts are
  held back"). It was also refused when it uses a public function, variable or constant that only a held-back
  script's 1.2 version defines. Calls through `has_method` / `call()` count as soft: they fail quietly.
- No public function or variable that an unchanged or held-back script still uses was removed from a shipped script,
  and none had its argument count changed.
- None of this has been run in Godot or on Windows; the checks are static. In-game checks are still to do (below).

## Why scripts are held back

1. **New global classes.** In 1.2, `CacheTrim`, `GpuJobs`, `CraftWindow` and `TimerBarWindow` are new
   `class_name` scripts. The game is started as `godot --path ...` (no editor), so Godot only knows the global
   classes in the released `.godot/global_script_class_cache.cfg`. It never rescans at runtime. A script that names
   one of these classes fails to parse. To ship them, that cache would need a patch. The released copy isn't in Git,
   and a copy rebuilt from the 0.37 sources did not match the released SHA-256 in any format tried. Release-page
   downloads are blocked from this environment.
2. **New files can't be installed.** The installer skips any entry whose target file isn't in the game
   (`install-server-build.ps1`: "not in this game, skipped"). That includes a whole-file (`"File"`) entry. So
   `stealth_look.gd` and the other new scripts can't be added without changes to the installer and to rollback,
   which would have to delete them.
3. **C# changes** need a new `MMOEngine.dll` (below).

## (a) Scripts Godot loads from source, released bytes verified

Shipped as byte patches (14 new entries, plus `quest_markers.gd`, which is unchanged from Beta 1.1.1 because 1.2 has
the same fixed bytes):

| File (under `new-client/godot/`) | Line ends | What it brings |
|---|---|---|
| `game/ui/quickbar.gd` | CRLF | Abilities (Sprint, Stealth, Quickcast...) drop onto the quickbar; shift + right-click empties a slot |
| `game/ui/panel_sheets.gd` | CRLF | The Stealth spec gets its own Abilities row (click it, or drag it to the quickbar) |
| `game/net/server.gd` | LF | Uses a spec on the server (`srv:spec:Stealth` -> UseSkill type 0); spells check "target in view" (an unseen target is refused at once); stores the slot of the item being inspected; crafting, salvage and repair plumbing (not reachable without game_ui.gd and the DLL) |
| `game/ui/chat_window.gd` | CRLF | `/stealth` and `/hide` (the server has no such command); `/craft` (does nothing without game_ui.gd) |
| `game/ui/merchant_window.gd` | CRLF | **SELL** and **APPRAISE** buttons, quantity, scrolling, packs |
| `game/ui/player_data.gd` | CRLF | Sends APPRAISE to the server (needs the DLL, see below) |
| `game/character_state.gd` | CRLF | APPRAISE text for the client's own world |
| `game/ui/item_db.gd` | CRLF | Arrow, bolt and poison info; pack size per purchase |
| `game/pregame.gd` | CRLF | One PLAY click enters the world once (Living Server's double-click guard, from Beta 1.1) |
| `game/ui/trainer_window.gd` | CRLF | The X on the skill preview closes the trainer window |
| `game/npcs/name_plates.gd` | CRLF | Names hide behind building walls |
| `game/sky_system.gd` | CRLF | Fog per region from its first zone, as the original client (Avalon Isle) |
| `game/nif_particles.gd` | CRLF | Particles use the NIF's emit rate: casting hands flicker instead of showing a solid orb |
| `game/npcs/npc_state.gd` | CRLF | One field for the stealth ghost (not used until npc.gd ships) |
| `game/quests/quest_markers.gd` | CRLF | (already shipped in Beta 1.1.1, same bytes) |

Held back, though their released bytes verify:

| File | Why |
|---|---|
| `game/ui/game_ui.gd` | Uses `CraftWindow` and `TimerBarWindow`. This also holds back Living Server's FACE / FOLLOW / STICK fix in the server's game, the stance button and the item information buttons. |
| `game/player.gd` | Uses `Fig3Figure.item_armor`, which is new in the held-back fig3_figure.gd. This holds back the second /stick or /follow turning tracking off, and the stun / root "no turn" handling. |
| `game/net/server_world.gd` | Preloads the new `stealth_look.gd`. This holds back the stealth ghost, server target changes (`/target`, `/assist`), fighters turning to face their target, the spell log, and the /lfg and /trade channel names. |
| `game/npcs/npc.gd` | Preloads `stealth_look.gd`; uses `Fig3Figure.item_armor` |
| `game/fig3_figure.gd`, `nif_scene.gd`, `nif_materials.gd`, `npcs/npc_manager.gd`, `weapon_models.gd`, `world/region_world.gd`, `zone_loader.gd`, `zone_viewer.gd` | Use `CacheTrim` / `GpuJobs`. This holds back the login / teleport freeze fixes, the video-memory trimming, zone seams, walls and shared dyes. |
| `tools/pack_scenes.gd` | Uses `GpuJobs`; a build tool, not run by the game |
| `game/terrain.gdshader` | Its new `layer_tex_clamped` textures are set only by the 1.2 zone_loader.gd. With the released loader, every non-tiling ground layer would sample an unset texture. |
| `game/login_screen.gd` | Sets the window title to "Offline DAoC Rebuilt v0.38", which is wrong on a 0.37c game. This also holds back the login retry while the server holds a link-dead session. |

## (b) C# sources (need MMOEngine.dll)

`game/net/DaocClient.cs` (released bytes verify, LF). 1.2 adds:
- `AppraiseItem` (0xE0): the merchant APPRAISE button on the server
- `Craft` (0xED): the crafting window's Make button
- 0x8D PlayerModelTypeChange (`model_type`): you yourself going into or out of stealth (the ghost; server.gd stores it)
- 0xF6 ChangeTarget (`change_target`): the server picks your target (`/target <name>`, `/assist`)
- UpdateIcons' ability-icon byte: Stealth's own buff icon instead of another spell's picture
- VariousUpdate 8 (`craft_skills`): the tradeskill list
- DebugMode 'H' companion messages: the group's stance

A build here works. Copy the C# files and `MMOEngine.csproj`, point `nuget.config` at nuget.org (the repo's points
at `tools/godot_net`, which isn't in Git), then run `dotnet build -c Debug` (SDK 10.0.403, Godot.NET.Sdk 4.7.2). It
built with 0 warnings and 0 errors in 6 s. The DLL's SHA-256 was
`ee1e0f16d546e3bc27913a3b3bc99093efe9c506ae27ded9c2fac209c9fb544a`. The same build of the **v0.37** sources gives
`8ac60314...`, not the released `8ee8f156...`, so this build doesn't reproduce shadowofze's binary. A DLL could only
ship whole (with its `.pdb`, and perhaps `deps.json`), and it has not been loaded in Godot. The DLL is not added to the
installer; whether to ship a compiled DLL is the owner's call.

## (c) Everything else

- New scripts not in the release (can't be byte patches; the installer can't add new files, see above):
  `game/cache_trim.gd`, `game/gpu_jobs.gd`, `game/stealth_look.gd`, `game/ui/craft_window.gd`,
  `game/ui/timer_bar_window.gd`. All five are shadowofze's own GPL source; only `stealth_look.gd` has no `class_name`.
- 47 test scenes and scripts under `tests/`: the release doesn't ship `tests/`.
- `project.godot`, the `.csproj`, `.import` files and converted content: unchanged between v0.37 and 1.2 (no new
  autoloads). The 1.2 crafting window would also need `content/crafting/recipes.json`, which isn't in the release.

## Side effects of the shipped set (check in game)

- **APPRAISE** appears in the merchant window. On the server (every 0.37c game) it calls `AppraiseItem`, which the
  released DLL doesn't have. The click does nothing, apart from a script error in the client's log. SELL uses the
  released `SellItem` and should work.
- The account vault window will be titled "HOUSE VAULT". The 1.2 server.gd looks for the last NPC you spoke to in
  zone_viewer.gd, and only the held-back 1.2 version keeps it.
- Stealth: the Abilities row, `/stealth` and the quickbar slot should hide you. You won't see yourself as a green ghost,
  and the buff bar may show the wrong picture for Stealth (both need the DLL or the held-back scripts).

In-game checks still to do: drag Sprint and Stealth to the quickbar, use them, run `/stealth`, sell through the
merchant's SELL button, cast at a target behind a wall, enter the world with a double-click on PLAY, and open the
trainer preview and close it with its X.
