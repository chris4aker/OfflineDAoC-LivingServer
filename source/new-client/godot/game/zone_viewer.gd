extends Node3D
## MMOEngine milestone 1: walk around one zone with the converted DAoC assets.
##   Godot --path godot -- --zone=200 [--at="Mag Mell"] [--terrain=tex165_x4plus] [--textures=x4plus]
##   --tour=<abs png prefix> [--spots=a,b]   visit the teleport spots, save a screenshot facing each way, then quit
##   --bench                                 stand at every spot facing four ways, print the average FPS, then quit
##   --anim=run                              hold one character animation (for test shots)
##   --time=14 --storm=1                     start at a game hour / force a storm level (0..1)
##   --shots="x,y,face,dist;x,y,face,dist" --out=<abs png prefix>   free camera shots at DAoC zone coordinates
##                                           (camera stands dist metres back from x,y looking along face), then quit
## Keys: WoW-style movement (see player.gd): W/S, A/D turn, Q/E strafe, right mouse steer, left mouse look,
##       F7 +1 game hour, F8 time speed (x1/x20/x120), F9 force storm on/off, Esc temporary menu (movement mode),
##       Ctrl+F1 ground set, Ctrl+F2 texture set (original / x4plus), Ctrl+F3 ground grain, Ctrl+F4 grass tufts,
##       Ctrl+F5 next spot, Ctrl+F6 fly mode (plain F-keys are the client's bindings), Esc menu.

var zone: ZoneLoader
var region: RegionWorld                     # seamless zones: the region's loaded zones around you (game/world/region_world.gd)
var player: Player
var clutter: GroundClutter                  # the current zone's (each loaded zone has its own: clutters)
var clutters := {}                          # zone id -> GroundClutter
var sky: SkySystem
var ui: GameUI
var npcs: NpcManager
var combat: Combat
var quests: QuestEngine
var emissaries: Emissaries                 # the three faction emissaries (the only custom NPCs)
var tasks: TaskSystem                      # the 1.65 /task system
var loot: Loot                             # monster drops, auto loot, items on the ground
var companions: Companions                 # your hired companions (bot plan phase 6)
var _inn_window: InnkeeperWindow
var _click_from := Vector2.ZERO
var _click_moved := 0.0
var _click_down := false
var _rclick_from := Vector2.ZERO
var _rclick_moved := 0.0
var _rclick_down := false
var audio: ZoneAudio
var _loading := false                       # a loading screen is up: zone audio waits until it is gone
var hud: Label
var args := {}
var _spots: Array = []
var _spot_index := 0
var _grain := true
var _shadow_filter := 5
var _env: Environment
# zone travel (zonejump.csv exits -> the old server's ZonePoint arrival): set before the scene reloads, read once
static var travel := {}
var _exit_armed := false                    # an exit only fires once the player has stood outside every exit
var _exit_warned := ""
var _save_left := 5.0


func _ready() -> void:
	LoginScreen.set_game_title(get_window())
	for a in OS.get_cmdline_user_args():
		var kv := a.trim_prefix("--").split("=", true, 1)
		args[kv[0]] = kv[1] if kv.size() > 1 else ""
	if args.has("probe") or args.has("shots") or args.has("bench") or args.has("tour"):
		UiArt.settings_path = "user://settings_tests.cfg"   # test runs keep their window layout off the player's
	if args.has("char-file"):                            # tests: a COPY of a character (json), saved only under a
		var cf = JSON.parse_string(FileAccess.get_file_as_string(String(args["char-file"])))   # test account
		if cf is Dictionary:
			Accounts.account = "testcopy"
			CharacterStore.selected = cf
	var start := CharacterStore.start_of(CharacterStore.selected) if not CharacterStore.selected.is_empty() else {}
	var loc: Dictionary = CharacterStore.selected.get("location", {}) if CharacterStore.selected.get("location") is Dictionary else {}
	var zone_id := int(args.get("zone", str(loc.get("zone", start.get("zone", 200)))))
	if not travel.is_empty():
		zone_id = int(travel.zone)
		loc = travel
	if Server.active and not Server.entry.is_empty():
		# the launcher's server game: the zone and place the server put you in (Server.entry, PositionAndObjectID)
		zone_id = int(Server.entry.zone)
		var ep: Vector3 = Server.entry.pos
		loc = {"zone": zone_id, "pos": [ep.x, ep.y, ep.z], "face": float(Server.entry.face)}
		travel = {}
		if zone_id < 0 or not FileAccess.file_exists("res://content/zones/zone%03d/zone.json" % zone_id):
			Server.last_error = "That place (region %d, zone %d) is not built in this client yet." % [int(Server.entry.region), zone_id]
			push_error("[server] " + Server.last_error)
			get_tree().change_scene_to_file.call_deferred("res://game/pregame.tscn")
			return
	var zone_dir := "res://content/zones/zone%03d" % zone_id
	# loading screen (box art + the client's bronze bar) with the title music, unless this is a test run
	var loading: LoadingScreen = null
	if not (args.has("shots") or args.has("probe") or args.has("tour") or args.has("bench")) or args.has("with-loading"):
		var head := FileAccess.get_file_as_string(zone_dir.path_join("zone.json")).substr(0, 400)
		var m := RegEx.create_from_string('"name": *"([^"]+)"').search(head)
		loading = LoadingScreen.new(m.get_string(1) if m else "the world")
		_loading = true
		add_child(loading)
		if travel.is_empty():
			PregameMusic.play()                              # first entry only (owner: title music until zone-in)
		await get_tree().process_frame
		await get_tree().process_frame
		if args.has("loading-shot"):                     # debug: save the loading screen once it is up
			loading.set_progress(0.45)
			await get_tree().create_timer(0.8).timeout
			get_viewport().get_texture().get_image().save_png(String(args["loading-shot"]))
			get_tree().quit()
			return
	zone = ZoneLoader.new()
	zone.name = "Zone"
	add_child(zone)
	zone.load_zone(zone_dir, args.get("terrain", "blend"), args.get("textures", "x4plus"))
	if loading:
		loading.set_progress(0.7)
		await get_tree().process_frame

	player = Player.new()
	player.name = "Player"
	player.zone = zone
	add_child(player)
	if not args.has("single-zone") and not zone.city and not bool(zone.info.get("dungeon", false)):
		region = RegionWorld.new()                         # the neighbours of this zone's region, streamed in as you go
		region.name = "Region"
		add_child(region)
		region.setup(self, zone, args.get("terrain", "blend"), args.get("textures", "x4plus"))
		region.zone_added.connect(_on_zone_added)
		region.zone_removing.connect(_on_zone_removing)
		player.region = region
		if loading:
			region.set_process(false)                       # (the loading screen builds the first neighbours itself)
	# the owner's camera is locked to the client's zoom range; my test runs (and --free-camera) keep it unlocked
	player.free_camera = args.has("free-camera") or args.has("probe") or args.has("shots") or args.has("tour") or args.has("bench")
	if not CharacterStore.selected.is_empty() and not args.has("model"):
		player.set_figure(CharacterStore.selected)       # the character picked on the select screen
	elif args.has("figure"):
		player.set_figure({"prefix": String(args["figure"])})   # test runs: --figure=tro_f
	else:
		player.set_model("res://content/characters/%s/celt_animated.gltf" % args.get("model", "celt_male_cata"))   # Catacombs Celt (owner 2026-10-09); --model=celt_male = classic
	# the 1.65 interface (game/ui): the character from the select screen, its inventory, quickbars, options (Esc)
	if args.has("ui-test"):
		UiArt.settings_path = "user://settings_uitest.cfg"     # test runs never move the owner's windows
	Graphics.load_from(UiArt.settings_path)                 # Options > VIDEO quality, before the world is built
	if args.has("ui-test") and CharacterStore.selected.is_empty():
		# tests: a sample Celt Guardian that is never saved (no account), --ui-shots=<folder> saves the windows
		# --realm=alb / mid: a sample Briton Fighter / Norseman Warrior instead (Albion / Midgard zone tests)
		var sample := {"alb": {"name": "Uitest", "realm": "alb", "prefix": "bri_m", "race": "Briton", "base": "BRIT-FIGH",
				"class_name": "Fighter", "level": 1},
				"mid": {"name": "Uitest", "realm": "mid", "prefix": "nor_m", "race": "Norseman", "base": "NORS-WARR",
				"class_name": "Viking", "level": 1}}
		var sc: Dictionary = sample.get(String(args.get("realm", "")), {"name": "Uitest", "realm": "hib", "prefix": "cel_m",
				"race": "Celt", "base": "CELT-ARMS", "class_name": "Guardian", "level": 1})
		PlayerData.load_character(sc)
		CharacterStore.selected = sc                     # the next zone keeps this sample (its companions, level...)
	elif not CharacterStore.selected.is_empty():
		PlayerData.load_character(CharacterStore.selected)
		if Server.active:                                # the server's items, money, level ... (they came before the zone)
			Server.apply_to_player()
			player.refresh_gear()
			Server.gear_changed.connect(player.refresh_gear)
	var cfg := ConfigFile.new()
	cfg.load("user://settings.cfg")
	player.movement_mode = String(args.get("movement", cfg.get_value("player", "movement", "modern")))
	ui = GameUI.new()
	ui.player = player
	add_child(ui)
	PlayerData.attack_toggle_requested.connect(_toggle_attack)
	if ui.warmap:
		ui.warmap.zone_id = zone_id                          # your marker on the realm war map
	if ui.map_window:                                        # M: the zone map (tests/zone_map_render.gd made it)
		ui.map_window.set_zone(zone.dir.path_join("map.png"), float(zone.info.size_m), String(zone.info.get("name", "")))
	if args.has("ui-shots"):
		_ui_shots.call_deferred(String(args["ui-shots"]))
	_build_sky()
	ui.sky = sky
	# the zone's people and monsters (pipeline/build_npcs.py + build_monsters.py; LOUGH DERG LEDGER.md)
	if Server.active:
		# the server's NPCs, monsters and bots (game/net/server_world.gd) - none of the game's own systems run
		var sw := ServerWorld.new()
		sw.name = "Npcs"
		add_child(sw)
		sw.region = region
		sw.setup_server(self, zone, player)
		npcs = sw
		ui.clear_target = npcs.clear_target
		Server.jumped.connect(_on_server_jump)
		Server.world.connect(_on_server_world)
		player.landed.connect(func(fall: float): _fall_speed = fall)
		PlayerData.sit_requested.connect(_server_sit)      # /sit is the server's too (the Sit key, the sit window)
		PlayerData.track_requested.connect(func(mode: String):   # /follow /stick /face your target (the client's own)
			var t: Npc = npcs.target if npcs and npcs.target and is_instance_valid(npcs.target) else null
			player.start_track(mode, t, (t.display_name(true) if t else "")))
		PlayerData.use_skill.connect(_server_use)          # spells, styles, abilities from the sheets and quickbars
		ui.item_dropped_on_world.connect(_on_item_dropped)   # an item dragged out: onto an NPC (give) or the ground
		ui.npc_dialog.keyword.connect(_server_keyword)     # a [keyword] in an NPC's words: whispered to it
		PlayerData.release_requested.connect(_server_release)   # the death window's RELEASE: the server's /release
		PlayerData.release_to.connect(_server_release_to)       # its CITY / HOUSE / BIND buttons: /release city ...
		PlayerData.invite_requested.connect(_server_invite)     # the group page's buttons: /invite, /disband
		PlayerData.disband_requested.connect(_server_disband)
		PlayerData.pet_order.connect(_server_pet)               # the pet window's commands: PetWindowHandler
		ui.raid_target.connect(sw.target_oid)                  # a raid window member clicked
		PlayerData.hook(self, PlayerData.member_clicked, func(row: Dictionary):   # a group page / mini group row clicked
			_member_pick = {} if bool(row.get("me", false)) else row          # (DISBAND then removes that member)
			sw.target_oid(Server.me_oid if bool(row.get("me", false)) else int(row.get("oid", 0))))
		PlayerData.dismount_requested.connect(_server_dismount)  # jump / /dismount while riding
		PlayerData.delve.connect(_server_item_info)             # an item's / skill's information: the server's delve
		PlayerData.summary.connect(_server_item_info)
		PlayerData.skill_info.connect(_server_skill_info)
		var qmarks := QuestMarkers.new()                   # the server's quest indicators: ground circles + map marks
		qmarks.name = "QuestMarkers"
		ui.add_child(qmarks)
		ui.move_child(qmarks, 0)
		qmarks.setup(self)
	elif FileAccess.file_exists(zone.dir.path_join("npcs.json")) and not args.has("no-npcs"):
		npcs = NpcManager.new()
		npcs.name = "Npcs"
		add_child(npcs)
		npcs.region = region
		npcs.setup(zone.dir, zone, player)
		npcs.interacted.connect(_on_interact)
		ui.clear_target = npcs.clear_target
		combat = Combat.new()                            # melee: attack mode, swings, monsters fighting back, XP
		combat.name = "Combat"
		add_child(combat)
		combat.setup(player, npcs)
		var cast := Casting.new()                        # spells: cast bar, power, effects
		cast.name = "Casting"
		add_child(cast)
		cast.setup(player, npcs, combat)
		combat.casting = cast
		combat.npc_casting = NpcCasting.new()            # monsters' spells (their templates')
		combat.npc_casting.combat = combat
		combat.npc_casting.casting = cast
		combat.pets = Pets.new()                         # summoned pets (pets.json)
		combat.pets.name = "Pets"
		add_child(combat.pets)
		combat.pets.setup(combat)
		var abil := Abilities.new()                      # Sprint, Quickcast
		abil.name = "Abilities"
		add_child(abil)
		abil.setup(player, combat, cast)
		combat.abilities = abil
		cast.cast_progress.connect(ui.show_cast)
		ui.casting = cast
		ui.item_dropped_on_world.connect(_on_item_dropped)
		ui.npc_dialog.keyword.connect(_on_keyword)
		combat.player_died.connect(ui.show_release.bind(true))
		var routes := HorseRoutes.new()                   # stable master horse routes (tickets)
		routes.name = "HorseRoutes"
		routes.viewer = self
		routes.player = player
		routes.npcs = npcs
		add_child(routes)
		quests = QuestEngine.new()                        # the classic quests (QuestDb)
		quests.name = "Quests"
		add_child(quests)
		quests.setup(self)
		ui.quest_abandon = quests.abandon
		var markers := QuestMarkers.new()                  # "!" over givers with a quest for you, "?" over step targets
		markers.name = "QuestMarkers"
		ui.add_child(markers)
		ui.move_child(markers, 0)
		markers.setup(self)
		combat.monster_killed.connect(quests.on_kill)
		emissaries = Emissaries.new()
		emissaries.name = "Emissaries"
		add_child(emissaries)
		emissaries.setup(self)
		combat.monster_killed.connect(emissaries.on_kill)
		combat.killed_by.connect(quests.on_player_died)
		tasks = TaskSystem.new()                            # the 1.65 /task system (kill / delivery tasks, levels 1-19)
		tasks.name = "Tasks"
		add_child(tasks)
		tasks.setup(self)
		combat.monster_killed.connect(tasks.on_kill)
		combat.killed_by.connect(tasks.on_player_died)
		PlayerData.task_command.connect(tasks.command)
		loot = Loot.new()                                   # drops (auto loot or on the ground) and dropped items
		loot.name = "Loot"
		add_child(loot)
		loot.setup(self)
		combat.monster_killed.connect(loot.on_kill)
		companions = Companions.new()                      # your companions: hiring, the inns, the courier quest
		companions.name = "Companions"
		add_child(companions)
		companions.setup(self)
		combat.group_xp = companions.group_xp
		_inn_window = InnkeeperWindow.new()
		ui.root.add_child(_inn_window)
		_inn_window.build()
		_inn_window.visible = false
		ui.windows["innkeeper"] = _inn_window
		var cw := CompanionWindow.new()
		ui.root.add_child(cw)
		cw.build()
		cw.visible = false
		ui.windows["companion"] = cw
		companions.window = cw
		combat.attack_mode_changed.connect(func(on: bool):
				if audio:
					audio.set_battle(on))
		combat.player_released.connect(ui.show_release.bind(false))
	clutter = _add_clutter(zone)
	if sky and region:
		sky.region = region

	_load_spots()
	if args.has("at"):
		for i in _spots.size():
			if String(_spots[i].name).to_lower() == String(args["at"]).to_lower():
				_spot_index = i
	_go_to_spot(_spot_index)
	_place_arrival(loc, zone_id, start)
	if Server.active and not zone.city:                    # (the server's Z, on the drawn ground if that is higher)
		var gp := player.global_position
		player.global_position.y = maxf(gp.y, zone.height_at(gp.x, gp.z) + 0.3)
	if loading:
		loading.set_progress(0.8)
		await get_tree().process_frame
		if region:                                         # the neighbours in reach, behind the loading screen
			await region.load_around(player.global_position)
			region.set_process(true)
		loading.set_progress(0.9)
		await get_tree().process_frame
	_ready_rest(loading)


## The teleport spots: the designer list in zone.json (pipeline/data/zone_spots.json), else the zone's jump points.
func _load_spots() -> void:
	_spots = []
	_spot_index = 0
	for sp in zone.info.get("spots", []):
		_spots.append(sp)
	if _spots.is_empty() and zone.info.jump_points.is_empty():
		var c := float(zone.info.size_m) * 0.5               # nothing named at all: the zone's middle
		_spots.append({"name": String(zone.info.name), "pos": [c, 0.0, c], "face": 0.0})
	if _spots.is_empty():
		for jn in zone.info.jump_points:
			_spots.append({"name": jn, "pos": zone.info.jump_points[jn], "face": 0.0})
	for i in _spots.size():
		if _spots[i].get("start", false):
			_spot_index = i


## Where you come into the world: your saved place, the far side of an exit, a new character's start, or --pos.
func _place_arrival(loc: Dictionary, zone_id: int, start: Dictionary) -> void:
	if not loc.is_empty() and int(loc.get("zone", -1)) == zone_id and (not args.has("at") or not travel.is_empty()):
		var lp: Array = loc.pos                             # where the character was (or arrives through an exit)
		player.global_position = Vector3(float(lp[0]), float(lp[1]) + 0.3, float(lp[2]))
		player.face(-deg_to_rad(float(loc.get("face", 0.0))))
	travel = {}
	if start.has("x") and int(start.zone) == zone_id and loc.is_empty():   # a new character: the old server's start
		var sx := float(start.x) * 0.025
		var sz := float(start.y) * 0.025
		# StartupLocation's Z is the floor the character starts on - a raised platform (the Lethantis tower's) can sit
		# well above the terrain, so stand on whichever is higher (owner: an Avalonian mage spawned inside the tower)
		var sy := maxf(zone.height_at(sx, sz), float(start.get("z", -1e9)) * 0.025)
		player.global_position = Vector3(sx, sy + 0.3, sz)
		if not CharacterStore.selected.is_empty() and not (PlayerData.character.get("bind") is Dictionary):
			# CharacterStore / StartupLocation: a new character is bound where it starts
			PlayerData.character["bind"] = {"zone": zone_id, "pos": [player.global_position.x, player.global_position.y,
					player.global_position.z], "face": fposmod(-rad_to_deg(player.facing()), 360.0)}
	if args.has("pos"):                                  # tests: start at x,y,z (engine metres)
		var pv := String(args["pos"]).split(",")
		player.global_position = Vector3(float(pv[0]), float(pv[1]), float(pv[2]))
	if combat:
		combat.bind_point = player.global_position         # (a character with no bind: here)


func _ready_rest(loading: LoadingScreen) -> void:
	Graphics.apply_viewport()                               # Options > VIDEO quality on the built world
	Graphics.apply_scene(self)
	hud = Label.new()
	hud.position = Vector2(12, 10)
	hud.add_theme_color_override("font_color", Color(1, 0.96, 0.85))
	hud.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.8))
	hud.add_theme_constant_override("shadow_offset_x", 1)
	hud.add_theme_constant_override("shadow_offset_y", 1)
	var layer := CanvasLayer.new()
	add_child(layer)
	layer.add_child(hud)
	# the debug text is off in the player's game (owner 2026-10-10) - Ctrl+F12 shows it for issue screenshots (kept
	# in the settings file); test runs always show it
	var test_run := args.has("probe") or args.has("shots") or args.has("tour") or args.has("bench")
	var hcfg := ConfigFile.new()
	hcfg.load(UiArt.settings_path)
	# 0.36c (the server's client) always starts with it hidden (owner 2026-10-11); Ctrl+F12 still shows it
	hud.visible = test_run or (BootSettings.dev and not Server.active and bool(hcfg.get_value("debug", "hud", false)))

	if args.has("probe"):                                # debug scripts from tests/ (e.g. --probe=strafe_probe)
		var probe: Node = load("res://tests/%s.gd" % args["probe"]).new()
		add_child(probe)
	if args.has("anim"):
		player.force_clip = String(args["anim"])
	if args.has("shots"):
		_shots.call_deferred(args["shots"], args.get("out", "user://shot"))
	elif args.has("tour"):
		_tour.call_deferred(args["tour"])
	elif args.has("bench"):
		_bench.call_deferred()
	if loading:
		# the NPCs around you are built while the loading screen is still up, and a few frames are drawn so their
		# textures upload and their shaders compile before you see the world (owner 2026-10-10)
		loading.set_progress(0.95)
		await get_tree().process_frame
		if npcs:
			npcs.prebuild()
			SpellFx.preload_effects(SpellFx.zone_effects(npcs.states))
		for i in 5:
			await get_tree().process_frame
		if Server.active:
			# the server: PlayerInit, GameOpen - you are in the game. Sent behind the loading screen: its answer
			# carries the time of day (SendTime), and the world is shown only once the sky has it (owner 2026-10-14:
			# no daylight that turns to night a second after the loading screen) - up to 1.5 s
			Server.zone_ready()
			_server_ready = true
			var until := Time.get_ticks_msec() + 1500
			while Server.game_time_ms < 0 and Time.get_ticks_msec() < until:
				await get_tree().process_frame
			if Server.game_hour() >= 0.0:
				sky.day_increment = Server.day_increment
				sky.set_hour(Server.game_hour())
		loading.finish()
		await loading.tree_exited                        # the loading screen has faded: you are in the world
		if not Server.active:
			WorldLink.zone_entered(zone, int(zone.info.id))  # the zone handover: this zone's bots now run in the game
		if companions:
			companions.zone_ready()                         # your companions arrive with you
		PregameMusic.fade_out(1.5)                       # owner: the title music plays until you zone in
		if audio:
			audio.begin()
	else:                                                # test runs (no loading screen): in the world at once
		await get_tree().process_frame
		if Server.active:
			Server.zone_ready()
			_server_ready = true
		else:
			WorldLink.zone_entered(zone, int(zone.info.id))
		if companions:
			companions.zone_ready()


func _build_sky() -> void:
	# the client's own sky, light, fog, day/night and weather (content/sky from pipeline/build_sky.py)
	sky = SkySystem.new()
	sky.name = "Sky"
	add_child(sky)
	sky.setup(zone, player if player else self, args.get("textures", "x4plus"))
	sky.set_hour(float(args.get("time", "10")))
	sky.server_driven = Server.active                      # the server's storms only (Server.weather)
	if Server.active and Server.game_hour() >= 0.0:
		# a new world (another region, a dungeon): the server's clock carries on - not a fresh 10:00 until its next
		# Time packet (owner 2026-10-14: the time of day jumped from 11:00 to 19:00)
		sky.day_increment = Server.day_increment
		sky.set_hour(Server.game_hour())
	# render debug switches, for comparing image-quality effects
	if args.has("no-ssao"):
		sky.environment.ssao_enabled = false
	if args.has("shadow-filter"):
		RenderingServer.directional_soft_shadow_filter_set_quality(int(args["shadow-filter"]))
		RenderingServer.positional_soft_shadow_filter_set_quality(int(args["shadow-filter"]))
	if args.has("no-shadow-blur"):
		sky.sun.shadow_blur = 0.0
		sky.sun.light_angular_distance = 0.0
	if args.has("storm"):
		sky.storm_forced = float(args["storm"])
		sky.storm = sky.storm_forced
	# the client's zone ambience + entry music (content/zones/zoneNNN/audio.json from pipeline/build_sounds.py)
	if not args.has("mute"):
		audio = ZoneAudio.new()
		audio.name = "ZoneAudio"
		add_child(audio)
		audio.setup(zone.dir, sky, player if player else self, not _loading)
		ui.options.apply_saved()


func _go_to_spot(i: int) -> void:
	_spot_index = i
	var p: Array = _spots[i].pos
	var y := maxf(zone.height_at(p[0], p[2]) + 0.3, zone.water_level_at(p[0], p[2]) - Player.SWIM_FLOAT)
	if zone.city:
		y = float(p[1]) + 0.3                                # city spots carry their floor height (no terrain)
	player.global_position = Vector3(p[0], y, p[2])
	player.velocity = Vector3.ZERO
	player.hold()
	player.face(-deg_to_rad(float(_spots[i].get("face", 0.0))))   # compass heading: 0 north, 90 east


func _notification(what: int) -> void:
	if what == NOTIFICATION_WM_CLOSE_REQUEST and player and zone and travel.is_empty() and not Server.active:
		_remember_location()
		PlayerData.save_character()


## The character's place in the world, saved with it (the next login starts there).
func _remember_location() -> void:
	if PlayerData.character.is_empty() or player.dead:
		return
	var p := player.global_position
	PlayerData.character["location"] = {"zone": int(zone.info.id), "pos": [p.x, p.y, p.z],
			"face": fposmod(-rad_to_deg(player.facing()), 360.0)}


## Zone borders inside a region (the client's zones.dat places a realm's outdoor zones side by side in one region,
## content/zones/zones_index.json): walking off this zone's edge loads the neighbour at the same region spot, height
## and heading. Where no zone lies beyond (the world's edge) or it isn't built yet, you stay inside.
static var _zone_index := {}
var _border_warned := ""


func _check_border() -> bool:
	var p := player.global_position
	var size := float(zone.info.size_m)
	if p.x >= 0.0 and p.z >= 0.0 and p.x < size and p.z < size:
		return false
	if zone.city:
		return false
	if region:
		# seamless (the client's way): the neighbour is in the world - walk on into it; it becomes the zone you're in
		# once you're CROSS_SLACK past the line (walking along a border doesn't flip back and forth)
		var out := maxf(maxf(-p.x, p.x - size), maxf(-p.z, p.z - size))
		var nid := region.zone_id_at(p.x, p.z)
		if nid >= 0 and region.zones.has(nid):
			if out >= CROSS_SLACK:
				_cross(nid)
			return false
		if player.dead:
			return false
	elif player.dead:
		return false
	if _zone_index.is_empty():
		var parsed = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json")) \
				if FileAccess.file_exists("res://content/zones/zones_index.json") else null
		_zone_index = parsed if parsed is Dictionary else {"": {}}
	var my_id := str(int(zone.info.id))
	var me: Dictionary = _zone_index.get(my_id, {})
	var gx := float(me.get("x", 0)) * 8192.0 + p.x / 0.025
	var gy := float(me.get("y", 0)) * 8192.0 + p.z / 0.025
	for k in _zone_index:
		var z: Dictionary = _zone_index[k]
		if me.is_empty() or str(k) == my_id or int(z.get("region", -1)) != int(me.region):
			continue
		var x0 := float(z.x) * 8192.0
		var y0 := float(z.y) * 8192.0
		if gx < x0 or gy < y0 or gx >= x0 + float(z.w) * 8192.0 or gy >= y0 + float(z.h) * 8192.0:
			continue
		if not FileAccess.file_exists("res://content/zones/zone%03d/zone.json" % int(k)):
			if _border_warned != str(k):
				_border_warned = str(k)
				PlayerData.message.emit("The way to %s is not built yet." % String(z.name), "system")
			break
		travel = {"zone": int(k), "pos": [(gx - x0) * 0.025, p.y, (gy - y0) * 0.025],
				"face": fposmod(-rad_to_deg(player.facing()), 360.0)}
		PlayerData.character["location"] = travel.duplicate()
		PlayerData.save_character()
		get_tree().change_scene_to_file("res://game/zone_viewer.tscn")
		return true
	player.global_position = Vector3(clampf(p.x, 0.05, size - 0.05), p.y, clampf(p.z, 0.05, size - 0.05))
	return false


const CROSS_SLACK := 3.0                      # metres past a border before the next zone is the one you're in


## Seamless zones: you walked (or rode) into neighbour zone id. It becomes the zone you're in at the scene origin and
## everything in the scene moves by its offset; the systems that follow "the zone" switch to it - its sky and fog,
## ambience and music, map, the world host's bot handover - with no loading screen, as in the original client.
func _cross(id: int) -> void:
	var t0 := Time.get_ticks_usec()
	var d := region.cross(id)
	zone = region.current
	player.global_position -= d
	player.rebase(d)
	player.zone = zone
	for c in get_children():
		if c == player or c is ZoneLoader or c == region or c == npcs or c == sky or c.has_meta("fixed_origin"):
			continue                                       # (holders that place their children from shifted data)
		if c.has_method("rebase"):
			c.call("rebase", d)
		elif c is Node3D:
			(c as Node3D).global_position -= d
	if npcs:
		npcs.zone = zone
		npcs.rebase(d)
	clutter = clutters.get(id)
	if sky:
		sky.set_zone(zone)
		player.camera.far = Graphics.far_clip(sky)        # (the new zone's fog distance)
	_switch_audio()
	if ui.warmap:
		ui.warmap.zone_id = id
	if ui.map_window:
		ui.map_window.set_zone(zone.dir.path_join("map.png"), float(zone.info.size_m), String(zone.info.get("name", "")))
	_load_spots()
	_exit_prev = Vector3.INF
	WorldLink.zone_switched(zone, id)                    # the old zone's bots go back to the host, this zone's come
	_remember_location()
	print("[region] crossed into %s (%d) - shifted %s in %.1f ms" % [zone.info.name, id, d, (Time.get_ticks_usec() - t0) / 1000.0])


func _switch_audio() -> void:
	if audio == null:
		return
	var old := audio
	old.name = "ZoneAudioOld"
	audio = ZoneAudio.new()
	audio.name = "ZoneAudio"
	add_child(audio)
	audio.setup(zone.dir, sky, player, false)
	audio.begin(old.hand_over(audio))                      # the old zone fades out; its music carries on
	if (combat and combat.attack_mode) or _server_attack:
		audio.set_battle(true)


func _add_clutter(z: ZoneLoader) -> GroundClutter:
	var c := GroundClutter.new()
	c.name = "GroundClutter"
	z.add_child(c)
	if not c.setup(z, player):
		c.queue_free()
		return null
	clutters[int(z.info.id)] = c
	return c


## A neighbour came into the world (RegionWorld): its people and monsters, its grass, the lighting of the hour.
func _on_zone_added(z: ZoneLoader) -> void:
	if npcs:
		npcs.add_zone(z)
	if combat:
		combat.spawns_changed()
	var c := _add_clutter(z)
	if c and clutter and not clutter.visible:
		c.visible = false                                  # (Ctrl+F4 turned grass off)
	if sky:
		z.set_light_level(sky.night_level)
	print("[region] %s (%d) loaded at %s" % [z.info.name, int(z.info.id), z.position])


func _on_zone_removing(z: ZoneLoader) -> void:
	if npcs:
		npcs.remove_zone(int(z.info.id))
	if combat:
		combat.spawns_changed()
	clutters.erase(int(z.info.id))
	print("[region] %s (%d) released" % [z.info.name, int(z.info.id)])


## Another zone at a spot (zone-local metres) - /release to a bind point there, etc.
func travel_to(zone_id: int, pos: Array, face: float) -> void:
	travel = {"zone": zone_id, "pos": pos, "face": face}
	PlayerData.character["location"] = travel.duplicate()
	PlayerData.save_character()
	get_tree().change_scene_to_file("res://game/zone_viewer.tscn")


## zonejump.csv exits: walking into one loads the zone behind it at the old server's arrival point (ZonePoint).
## The client makes each row a vertical quad from (x1, y1) to (x2, y2), z1..z2 ('jump%03d' in its zone jump collision
## group, game.dll 0x5802..) and jumps when the player's collider touches it: here, when the player comes within the
## collider's radius of the line, or crosses it between two frames (a fast move), at a height inside z1..z2.
const EXIT_TOUCH := 0.35                         # the capsule's 0.25 m radius + the client's slide margin
var _exit_prev := Vector3.INF
var exit_fired := ""                           # tests: the last exit that fired (--exit_test)


func _check_exits() -> bool:
	var p := player.global_position
	var prev := _exit_prev if _exit_prev != Vector3.INF else p
	_exit_prev = p
	var inside := {}
	for e in zone.info.get("exits", []):
		var lo: Array = e.min
		var hi: Array = e.max
		if p.y < lo[1] - 2.0 or p.y > hi[1] + 2.0:
			continue
		var a := Vector2(e.a[0], e.a[1]) if e.has("a") else Vector2(lo[0], lo[2])
		var b := Vector2(e.b[0], e.b[1]) if e.has("b") else Vector2(hi[0], hi[2])
		var q := Vector2(p.x, p.z)
		if Geometry2D.get_closest_point_to_segment(q, a, b).distance_to(q) <= EXIT_TOUCH 				or Geometry2D.segment_intersects_segment(Vector2(prev.x, prev.z), q, a, b) != null:
			inside = e
			break
	if inside.is_empty():
		_exit_armed = true
		_exit_warned = ""
		return false
	if Server.active:                                     # the server's game: ask it to take you through (the old
		if _exit_armed and not player.dead and int(inside.get("target", 0)) > 0:   # client's zone jump: its ZonePoint id)
			_exit_armed = false
			Server.client.call("ZonePoint", int(inside.target))
		return false
	if not _exit_armed or (player.dead and not args.has("exit_test")) or not inside.has("to_zone"):
		return false
	var to := int(inside.to_zone)
	if not FileAccess.file_exists("res://content/zones/zone%03d/zone.json" % to) and not args.has("exit_test"):
		if _exit_warned != String(inside.name):
			_exit_warned = String(inside.name)
			PlayerData.message.emit("The way to zone %d is not built yet." % to, "system")
		return false
	if args.has("exit_test"):                            # tests/exit_probe.gd: report the jump, stay in the zone
		print("[exit] fired ", inside.name, " -> zone ", to)
		exit_fired = String(inside.name)
		_exit_armed = false
		return false
	travel = {"zone": to, "pos": inside.to_pos, "face": float(inside.get("to_face", 0.0))}
	PlayerData.character["location"] = travel.duplicate()
	PlayerData.save_character()
	_exit_armed = false
	get_tree().change_scene_to_file("res://game/zone_viewer.tscn")
	return true


## Targeting: a left click that didn't drag the camera targets the NPC under the cursor (the classic client's
## click-to-target); a left click on the ground (no NPC, no door) untargets, as in the client (owner checked);
## the Target Enemy key picks the nearest enemy.
func _exit_tree() -> void:
	if companions:
		companions.store_all()                           # their sheets into their records (on your character)
		PlayerData.save_character()
	if not Server.active:
		WorldLink.zone_left()                                # the zone handover: this zone's bots go back to the host


var _click_placed := false


func _input(event: InputEvent) -> void:
	# Ctrl+F12: the debug text, for issue screenshots - here, ahead of the interface's keybinds (which took it)
	if event is InputEventKey and event.pressed and not event.echo and event.ctrl_pressed 			and event.physical_keycode == KEY_F12 and hud and BootSettings.dev:
		hud.visible = not hud.visible
		var hcfg := ConfigFile.new()
		hcfg.load(UiArt.settings_path)
		hcfg.set_value("debug", "hud", hud.visible)
		hcfg.save(UiArt.settings_path)
		get_viewport().set_input_as_handled()
		return
	# Ctrl+F11: inspect the targeted bot (bot plan diagnostics) - what it's doing and its last decisions, in the chat
	if event is InputEventKey and event.pressed and not event.echo and event.ctrl_pressed and event.physical_keycode == KEY_F11 			and BootSettings.dev:
		WorldLink.inspect_target()
		get_viewport().set_input_as_handled()
		return
	# an item held on the mouse pointer (a click picked it up - InventorySheet.click_hold): right-click or Esc puts it back
	if get_viewport().gui_is_dragging() and InventorySheet.is_item(get_viewport().gui_get_drag_data()) and (
			(event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT)
			or (event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_ESCAPE)):
		get_viewport().gui_cancel_drag()
		get_viewport().set_input_as_handled()
		return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if event.pressed:
			_click_down = true
			_click_from = event.position
			_click_moved = 0.0
			_click_placed = get_viewport().gui_is_dragging()    # this click puts down what the pointer holds
		elif _click_down:
			_click_down = false
			if _click_moved < 6.0 and get_viewport().gui_get_hovered_control() == null and not _click_placed:
				var picked := _click_pick(_click_from)
				var n: Npc = picked.get("npc")
				var bv: Node3D = null if n or picked.has("kind") else WorldLink.pick_bot(player.camera, _click_from)
				if String(picked.get("kind", "")) == "self":
					target_self()                                # a click on your own character targets you
				elif String(picked.get("kind", "")) == "door":
					_click_door(_click_from)
				elif n:
					WorldLink.clear_bot_target()
					npcs.select(n)
				elif bv:
					if npcs:
						npcs.target = null                       # a bot targeted: no monster target behind it
					WorldLink.target_bot(bv)
				elif loot and loot.click(_click_from):
					pass                                         # picked up something lying on the ground
				elif npcs is ServerWorld and (npcs as ServerWorld).click_object(player.camera, _click_from):
					pass                                         # the server's: something on the ground, targeted
				elif not _click_door(_click_from) and npcs:
					WorldLink.clear_bot_target()
					npcs.clear_target()
	elif event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_RIGHT and npcs:
		# right click (no drag) on an NPC: target it and interact - talk, merchant window (period: "when I right
		# clicked on Reeni, she just looked friendly and opened her merchant window")
		if event.pressed:
			_rclick_down = true
			_rclick_from = event.position
			_rclick_moved = 0.0
		elif _rclick_down:
			_rclick_down = false
			if _rclick_moved < 6.0 and get_viewport().gui_get_hovered_control() == null:
				var rn: Npc = npcs.pick(player.camera, _rclick_from)
				if rn:
					npcs.select(rn)
					_note_interact(rn)
					npcs.interact(rn)
				elif npcs is ServerWorld and (npcs as ServerWorld).use_object(player.camera, _rclick_from):
					pass                                         # a relic taken up, loot picked up (a grave: targeted)
				elif companions:
					var rb: Node3D = WorldLink.pick_bot(player.camera, _rclick_from)
					if rb and bool(rb.get("companion")):       # your companion: its window (gear, training, tactics, stance)
						WorldLink.target_bot(rb)
						companions.manage(int(rb.get("bot_id")))
	elif event is InputEventMouseMotion and (_click_down or _rclick_down):
		_click_moved += event.relative.length() if _click_down else 0.0
		_rclick_moved += event.relative.length() if _rclick_down else 0.0
	elif Keybinds.pressed(event, "attack_mode") and not player._ui_typing():
		_toggle_attack()
	elif Keybinds.pressed(event, "get_item") and npcs is ServerWorld and not player._ui_typing():
		(npcs as ServerWorld).get_object()                      # Get Item (G): pick up the targeted ground object
	elif Keybinds.pressed(event, "target_loot") and npcs is ServerWorld and not player._ui_typing():
		(npcs as ServerWorld).target_nearest_loot()             # Nearest Loot (F7): target the closest ground object
	elif Keybinds.pressed(event, "sit") and Server.active and not player._ui_typing():
		_server_sit(not player.sitting)                    # the Sit/Stand key (the game's own: Abilities)
		# handled here: Abilities' own Sit key (_unhandled_input) saw it too and stood you straight back up (owner
		# 2026-10-15: "my sit hotkey of x doesn't do anything at all")
		get_viewport().set_input_as_handled()
	elif Keybinds.pressed(event, "sprint") and Server.active and not player._ui_typing():
		Server.command("/sprint")                          # the Sprint key: the client's &sprint
		get_viewport().set_input_as_handled()              # (not Abilities' offline sprint as well)
	elif Keybinds.pressed(event, "ground_target") and Server.active and npcs is ServerWorld and not player._ui_typing():
		_server_ground_target(get_viewport().get_mouse_position())
	elif Keybinds.pressed(event, "bot_report") and Server.active and npcs is ServerWorld and not player._ui_typing():
		# the unseen-characters report (owner 2026-10-17): why nearby gamebots / pets aren't drawn, to bot_report.log
		PlayerData.message.emit((npcs as ServerWorld).write_bot_report(player.global_position), "system")
		get_viewport().set_input_as_handled()
	elif Keybinds.pressed(event, "ground_target") and combat and combat.casting and not player._ui_typing():
		combat.casting.set_ground_target(player.camera, get_viewport().get_mouse_position())
		if combat.casting.ground_target != null:
			show_ground_target(combat.casting.ground_target)
	elif Keybinds.pressed(event, "target_group_1") and not player._ui_typing():
		target_self()                                      # Target Group #1 (F1): you - always the first of your group
	elif Keybinds.pressed(event, "target_enemy") and npcs:
		var n := npcs.nearest_enemy()
		if n:
			npcs.select(n)


## You as your own target (the original client: F1 / Target Group #1, or a click on your character): your name and
## health in the target part of the status panel, so a heal you cast is plainly on you (owner 2026-10-11).
func target_self() -> void:
	_server_target_self()
	WorldLink.clear_bot_target()
	if npcs:
		npcs.target = null
	PlayerData.set_target({"name": PlayerData.display_name(), "level": PlayerData.level, "con_color": Color.WHITE,
			"health": float(PlayerData.hp) / maxf(1.0, float(PlayerData.max_hp)), "guild": String(PlayerData.character.get("guild", "")),
			"self": true})


## What a left click hits FIRST along its ray (owner 2026-10-14: the click went to a distant NPC - one behind the door
## being clicked, or standing behind your own character): the nearest of an NPC (its body or its shown name), your
## own character and a door; an NPC behind a wall, floor or door is never picked. {} when none of them -
## {"npc": Npc} / {"kind": "self"} / {"kind": "door"}.
func _click_pick(at: Vector2) -> Dictionary:
	if player == null or player.camera == null:
		return {}
	var cam := player.camera
	var from := cam.project_ray_origin(at)
	var dir := cam.project_ray_normal(at)
	var best := {}
	var best_d := INF
	var n: Npc = npcs.pick(cam, at) if npcs else null
	if n:
		var q := PhysicsRayQueryParameters3D.create(from, from + dir * 120.0, ZoneLoader.LAYER_WALL | ZoneLoader.LAYER_FLOOR)
		var wall := get_world_3d().direct_space_state.intersect_ray(q)
		var wall_d := from.distance_to(wall.position) if not wall.is_empty() else INF
		if npcs.last_pick_dist <= wall_d + 0.3:              # (not behind a wall, a floor, a closed door)
			best = {"npc": n}
			best_d = npcs.last_pick_dist
	var self_d := _clicked_self_at(at)
	if self_d < best_d:
		best = {"kind": "self"}
		best_d = self_d
	var dq := PhysicsRayQueryParameters3D.create(from, from + dir * 60.0, ZoneDoor.PICK_LAYER)
	var door := get_world_3d().direct_space_state.intersect_ray(dq)
	if not door.is_empty() and door.collider.has_meta("door") and from.distance_to(door.position) < best_d:
		best = {"kind": "door"}
	return best


## How far along the click ray it passes your own character (within 0.45 m of the body, feet to head); INF if not.
func _clicked_self_at(at: Vector2) -> float:
	if player == null or player.camera == null:
		return INF
	var o := player.camera.project_ray_origin(at)
	var d := player.camera.project_ray_normal(at)
	var feet := player.global_position
	var pts := Geometry3D.get_closest_points_between_segments(o, o + d * 80.0, feet + Vector3(0, 0.1, 0), feet + Vector3(0, 1.8, 0))
	return o.distance_to(pts[0]) if pts[0].distance_to(pts[1]) < 0.45 else INF


## Did a click land on your own character (the ray passes within 0.45 m of your body, feet to head)?
func _clicked_self(at: Vector2) -> bool:
	if player == null or player.camera == null:
		return false
	var o := player.camera.project_ray_origin(at)
	var d := player.camera.project_ray_normal(at)
	var feet := player.global_position
	var pts := Geometry3D.get_closest_points_between_segments(o, o + d * 80.0, feet + Vector3(0, 0.1, 0), feet + Vector3(0, 1.8, 0))
	return pts[0].distance_to(pts[1]) < 0.45


## An inventory item dropped on the world: on a smith, a repair (old server Blacksmith: "It will cost {0} to repair
## {1}. Do you accept?", "You pay {0} {1}.", "{0} says, \"There, {1} is ready for combat.\""), on a merchant a sale.
func _on_item_dropped(slot: int, screen_pos: Vector2) -> void:
	var n: Npc = npcs.pick(player.camera, screen_pos) if npcs else null
	if Server.active:                                       # the server: onto an NPC = give it (slot 1000 + its id), else
		var count := int(PlayerData.entry(slot).get("count", 1))   # onto the ground (slot 1) - PlayerMoveItem
		Server.client.call("MoveItem", slot, 1000 + int(n.spawn.get("oid", 0)) if n else 1, count)
		return
	if n == null:
		if loot:
			loot.drop_from_inventory(slot)                  # onto the ground at your feet
		return
	if n.global_position.distance_to(player.global_position) > 12.0:
		PlayerData.message.emit("You are too far away to interact with %s." % n.display_name(true), "error")
		return
	var who := n.display_name(true)
	var it := PlayerData.item_at(slot)
	if tasks and tasks.on_give(n, slot):
		return                                               # a delivery task's item reached its receiver
	if quests and quests.on_give(n, slot):
		return                                               # a quest took it (delivery, collection, trade)
	if n.spawn.role == "smith":
		var max_c := maxi(1, int(it.get("maxcondition", 1)))
		if int(it.get("object_type", 0)) <= 0 or int(it.get("object_type", 0)) > 42:
			PlayerData.message.emit("%s says, \"I can't repair that.\"" % _cap(who), "npc")
		elif int(it.get("condition", max_c)) >= max_c:
			PlayerData.message.emit("Your %s is already in perfect condition." % String(it.name), "system")
		elif int(it.get("durability", 0)) <= 0:
			PlayerData.message.emit("This object can't be repaired.", "system")
		else:
			var cost := PlayerData.repair_cost(slot)
			UiArt.confirm(ui.root, "", "It will cost %s to repair %s. Do you accept?" % [PlayerData.money_string(cost), String(it.name)],
					"ACCEPT", "DECLINE", func():
				if cost > PlayerData.total_copper():
					PlayerData.message.emit("%s says, \"It costs %s to repair %s. You don't have that much.\"" % [_cap(who), PlayerData.money_string(cost), String(it.name)], "npc")
					return
				PlayerData.set_copper(PlayerData.total_copper() - cost)
				PlayerData.message.emit("You pay %s %s." % [who, PlayerData.money_string(cost)], "merchant")
				PlayerData.repair(slot)
				if int(PlayerData.item_at(slot).get("durability", 0)) <= 0:
					PlayerData.message.emit("%s says, \"Uhh, %s is rather old. I won't be able to repair it again, so be careful!\"" % [_cap(who), String(it.name)], "npc")
				else:
					PlayerData.message.emit("%s says, \"There, %s is ready for combat.\"" % [_cap(who), String(it.name)], "npc"),
					func(): PlayerData.message.emit("You decline to have your %s repaired." % String(it.name), "system"))
	elif String(n.spawn.get("items", "")) != "":
		PlayerData.sell(slot, String(n.spawn.name))


## Right-click on an NPC: merchants open their shop window (old server: "[Right click to display a shop window]").
func _on_interact(n: Npc) -> void:
	var who := _cap(n.display_name(true))
	if emissaries and emissaries.on_interact(n):
		return
	if tasks and tasks.on_interact(n):
		return                                               # a kill task's guard paid out
	if quests and quests.on_interact(n) and n.spawn.role not in ["merchant", "trainer", "smith", "healer", "vaultkeeper", "innkeeper"]:
		return                                               # a quest giver / step target spoke
	if n.spawn.role == "innkeeper" and companions:       # the roadside inn: companions (bot plan phase 6)
		_inn_window.open_for(String(n.spawn.name), companions)
	elif n.spawn.role == "horn_handler":                   # the Horn of Rallying (phase 7: not working yet)
		PlayerData.message.emit("%s says, \"The Horn of Rallying is silent for now. When the realm has need of it, I will teach its call to those who have earned the right.\"" % who, "npc")
	elif n.spawn.role == "trainer":
		_trainer_interact(n)
	elif n.spawn.role == "healer":
		_healer_interact(n)
	elif n.spawn.role == "vaultkeeper":                      # old server GameVaultKeeper: the player's vault
		ui.vault.open_for(String(n.spawn.name))
	elif n.spawn.role == "smith":                            # old server GameNPC.Blacksmith.Say
		PlayerData.message.emit("%s says, \"I can repair weapons or armor for you. Just hand me the item you want repaired and I'll see what I can do, for a small fee.\"" % who, "npc")
	elif String(n.spawn.get("items", "")) != "":
		ui.merchant.open_for(String(n.spawn.name), String(n.spawn.items))


## Two windows opened together: if they overlap, the second moves beside the first (right, else left) so neither
## hides the other, and the talk stays on top (owner: the trainer's words opened under the training window).
func _side_by_side(first: ClassicWindow, second: ClassicWindow) -> void:
	if Rect2(first.position, first.size).intersects(Rect2(second.position, second.size)):
		var area := first.get_parent_area_size()
		var right := first.position.x + first.size.x + 6.0
		var left := first.position.x - second.size.x - 6.0
		second.position = Vector2(right if right + second.size.x <= area.x else maxf(0.0, left),
				clampf(first.position.y, 0.0, maxf(0.0, area.y - second.size.y)))
		second.save_position()
	first.move_to_front()


## A healer (old server GameHealer): greets, then offers to restore constitution lost at deaths for the level's
## price per point (GamePlayer.prcRestore).
func _healer_interact(n: Npc) -> void:
	var who := String(n.spawn.name)
	PlayerData.message.emit("%s says, \"Greetings, %s. What can I do for you today?\"" % [who, String(ClassDb.cls(PlayerData.class_id()).get("name", ""))], "npc")
	# any healer of your realm - the roadside inns' own and every town's (owner 2026-10-11) - brings a fallen companion
	# back - free below level 20, then a player's one-death constitution price (Companions.revive_cost)
	if (bool(n.spawn.get("revives_companions", false)) or int(n.spawn.get("realm", 0)) == PlayerData.realm_id()) 			and companions and not companions.dead().is_empty():
		var rec: Dictionary = companions.dead()[0]
		var cid := int(Companions.info(rec).id)
		var fee := Companions.revive_cost(rec)
		var ask := ("It will cost %s to bring %s back from the dead. Do you accept?" % [PlayerData.money_string(fee), rec.name]) if fee > 0 				else ("%s will bring %s back from the dead for free (until level %d). Do you accept?" % [who, rec.name, Companions.FREE_REVIVE_BELOW])
		UiArt.confirm(ui.root, "", ask,
				"ACCEPT", "DECLINE", func():
			if player.global_position.distance_to(n.global_position) > 6.0:
				PlayerData.message.emit("You are too far away to interact with %s." % who, "system")
				return
			companions.revive(cid, who),
				func(): PlayerData.message.emit("You decide not to have %s brought back yet." % rec.name, "system"))
		return
	if PlayerData.con_lost <= 0:
		PlayerData.message.emit("Your constitution is already fully restored!", "system")
		return
	var table: Array = CombatRules.r().get("con_restore_cost", [1])
	var one := int(table[clampi(PlayerData.level, 0, table.size() - 1)])
	UiArt.confirm(ui.root, "", "It will cost %s to have your lost constitution restored. Do you accept?" % PlayerData.money_string(one * PlayerData.con_lost),
			"ACCEPT", "DECLINE", func():
		if player.global_position.distance_to(n.global_position) > 6.0:   # WorldMgr.INTERACT_DISTANCE
			PlayerData.message.emit("You are too far away to interact with %s." % String(n.spawn.name), "system")
			return
		var pts := maxi(1, mini(PlayerData.con_lost, PlayerData.total_copper() / maxi(one, 1)))
		var cost := pts * one
		if PlayerData.total_copper() < cost:
			PlayerData.message.emit("%s says, \"It costs %s to restore %d lost constitution. You don't have that much.\"" % [who, PlayerData.money_string(cost), pts], "npc")
			return
		PlayerData.set_copper(PlayerData.total_copper() - cost)
		PlayerData.message.emit("%s restores %d point%s of your lost constitution." % [who, pts, "" if pts == 1 else "s"], "system")
		PlayerData.message.emit("You give %s a donation of %s." % [String({"He": "him", "She": "her"}.get(n.pronoun(), "it")), PlayerData.money_string(cost)], "system")
		PlayerData.con_lost -= pts
		PlayerData.recalc_max()
		PlayerData.set_hp(mini(PlayerData.hp, PlayerData.max_hp))
		PlayerData.save_character(),
			func(): PlayerData.message.emit("You decline to have your constitution restored.", "system"))


## A trainer (old server GameTrainer + the class trainer scripts): your own class's trainer (or your base class's,
## while you are still one) trains you - "Do you wish to learn some more, <name>? Step up and receive your training!"
## and the training window; an advanced class's trainer offers a level 5 base class of its line the class
## ("<name>, do you choose the <guild>, and life as a [<Class>]?"); anyone else must seek elsewhere.
func _trainer_interact(n: Npc) -> void:
	var who := String(n.spawn.name)
	var trains := ClassDb.by_name(str(n.spawn.get("trains", "") if n.spawn.get("trains") != null else ""))
	var mine := PlayerData.class_id()
	var me := PlayerData.display_name()
	if trains == mine:
		ui.npc_dialog.say(String(n.spawn.id), who, "Do you wish to learn some more, %s? Step up and receive your training!" % me)
		ui.trainer.open()
		_side_by_side(ui.npc_dialog, ui.trainer)
	elif ClassDb.can_promote(trains, mine, PlayerData.level, String(PlayerData.character.get("race", ""))):
		var c := ClassDb.cls(trains)
		ui.npc_dialog.say(String(n.spawn.id), who, "%s, do you choose the %s, and life as a [%s]?" % [me, String(c.get("profession", "")), String(c.name)])
	else:
		ui.npc_dialog.say(String(n.spawn.id), who, "You must seek elsewhere for your training.")


## A clicked [keyword] in the NPC window: a trainer's class name promotes ("Very well, <name>. I gladly take your
## training into my hands. Congratulations, from this day forth, you are a <Class>.").
func _on_keyword(npc_id: String, word: String) -> void:
	var n: Npc = null
	for i in npcs.active:
		if String((npcs.active[i] as Npc).spawn.id) == npc_id:
			n = npcs.active[i]
	if n == null:
		return
	if emissaries and emissaries.on_keyword(n, word):
		return
	if quests:
		quests.on_keyword(n, word)
	if n.spawn.role == "trainer":
		var trains := ClassDb.by_name(str(n.spawn.get("trains", "") if n.spawn.get("trains") != null else ""))
		if word == String(ClassDb.cls(trains).get("name", "")) and ClassDb.can_promote(trains, PlayerData.class_id(), PlayerData.level, String(PlayerData.character.get("race", ""))):
			ui.npc_dialog.say(npc_id, String(n.spawn.name), "Very well, %s. I gladly take your training into my hands. Congratulations, from this day forth, you are a %s." % [PlayerData.display_name(), word])
			PlayerData.promote(trains)


func _cap(t: String) -> String:
	return t.substr(0, 1).to_upper() + t.substr(1)


## A click on a door opens it (the classic client's way), within reach.
## The server's game: the ground target under the mouse goes to the server (PlayerGroundTargetHandler) - ground-target
## spells, and a siege weapon you control rolls there (MOVE) or a catapult / trebuchet aims at it (owner 2026-10-14).
func _server_ground_target(screen_pos: Vector2) -> void:
	var from := player.camera.project_ray_origin(screen_pos)
	var to := from + player.camera.project_ray_normal(screen_pos) * 250.0
	var q := PhysicsRayQueryParameters3D.create(from, to, ZoneLoader.LAYER_FLOOR | ZoneLoader.LAYER_WALL)
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	if hit.is_empty():
		PlayerData.message.emit("Your ground target is not visible!", "system")
		return
	var r := Server.to_region((npcs as ServerWorld)._current_zone(), hit.position)
	Server.client.call("GroundTarget", int(r.x), int(r.y), int(r.z), 0x100)
	_server_gt = hit.position
	show_ground_target(hit.position)


var _server_gt := Vector3.INF                    # your ground target on the server (for the position packets' flag)


## The ground target marker (pipeline/build_gtarget.py; owner 2026-10-14: "yellow and black with a black dot in the
## middle", shown by the Ground Target key (C) on any character in the old client): the client's stylemap/target.bmp
## projected on the ground - a black X with four yellow triangles - and its "Ground Target" object (objects.csv 1293
## -> items/gtarget.nif) in the middle, drawn yellow. Offline and on the server.
const GT_TILE_M := 2.0
var _gt_marker: Node3D


func show_ground_target(at: Vector3) -> void:
	if _gt_marker == null or not is_instance_valid(_gt_marker):
		_gt_marker = Node3D.new()
		_gt_marker.name = "GroundTarget"
		add_child(_gt_marker)
		var img := Image.load_from_file(ProjectSettings.globalize_path("res://content/ui/gtarget/target.png"))
		if img:
			img.generate_mipmaps()
			var decal := Decal.new()
			decal.texture_albedo = ImageTexture.create_from_image(img)
			decal.size = Vector3(GT_TILE_M, 3.0, GT_TILE_M)
			decal.cull_mask = 1                               # the ground and the world, not figures
			_gt_marker.add_child(decal)
		var ball := NifScene.copy_of("res://content/ui/gtarget/model.gltf")
		if ball:
			_gt_marker.add_child(ball)
			ball.scale = Vector3.ONE * 0.7
			# a solid yellow glow, as the owner's client shows it: its ring texture on the box's faces drew an "8"
			var glow := StandardMaterial3D.new()
			glow.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
			glow.albedo_color = Color(1.0, 0.88, 0.12)
			for mi in ball.find_children("*", "MeshInstance3D", true, false):
				(mi as MeshInstance3D).material_override = glow
	_gt_marker.global_position = at
	_gt_marker.visible = true


func _click_door(screen_pos: Vector2) -> bool:
	var from := player.camera.project_ray_origin(screen_pos)
	var to := from + player.camera.project_ray_normal(screen_pos) * 60.0
	var q := PhysicsRayQueryParameters3D.create(from, to, ZoneDoor.PICK_LAYER)
	var hit := get_world_3d().direct_space_state.intersect_ray(q)
	if hit.is_empty() or not hit.collider.has_meta("door"):
		return false
	var d: ZoneDoor = hit.collider.get_meta("door")
	if Server.active and npcs is ServerWorld:
		# the server's game: the server opens it (or not) and keep doors are targeted to be attacked - it checks the
		# distance itself (DoorRequestHandler: three times the pick-up distance for keep doors)
		(npcs as ServerWorld).request_door(d)
		return true
	if d.global_position.distance_to(player.global_position) > 8.0:
		PlayerData.message.emit("You are too far away to open that.", "error")
		return true
	d.toggle()
	return true


func _unhandled_input(event: InputEvent) -> void:
	if not (event is InputEventKey and event.pressed and not event.echo):
		return
	if not event.ctrl_pressed or not BootSettings.dev:      # (developer mode only: BootSettings.dev)
		return                                               # developer keys are Ctrl+F-keys: plain F-keys are the
	match event.physical_keycode:                            # client's bindings (group targets, consider ...)
		KEY_ESCAPE:
			pass
		KEY_F1:
			var sets: Array = zone.terrain_sets()
			if not sets.is_empty():
				zone.set_terrain_set(sets[(sets.find(zone.terrain_set) + 1) % sets.size()])
		KEY_F2:
			var ts := "original" if zone.texture_set == "x4plus" else "x4plus"
			for zl in (region.zones.values() if region else [zone]):
				(zl as ZoneLoader).set_texture_set(ts)
			for c in clutters.values():
				if is_instance_valid(c):
					(c as GroundClutter).refresh_textures()
		KEY_F4:
			var show := not clutter.visible if clutter else true
			for c in clutters.values():
				if is_instance_valid(c):
					(c as GroundClutter).visible = show
		KEY_F3:
			_grain = not _grain
			zone.set_detail_strength(0.45 if _grain else 0.0)
		KEY_F5:
			_go_to_spot((_spot_index + 1) % _spots.size())
		KEY_F6:
			player.set_flying(not player.flying)
		KEY_F7:
			sky.set_hour(sky.hour() + 1.0)                 # tests: jump an hour ahead
		KEY_F8:
			var speeds := [1.0, 20.0, 120.0]
			sky.time_scale = speeds[(speeds.find(sky.time_scale) + 1) % speeds.size()]
		KEY_F9:
			sky.storm_forced = 1.0 if sky.storm_forced < 0.0 else (-1.0 if sky.storm_forced > 0.5 else 1.0)
		KEY_F10:
			sky.environment.ssao_enabled = not sky.environment.ssao_enabled   # image-quality check: SSAO
		KEY_F11:
			# image-quality check: soft shadow filter 0 hard .. 5 ultra (the soft ones dither)
			_shadow_filter = (_shadow_filter + 1) % 6
			RenderingServer.directional_soft_shadow_filter_set_quality(_shadow_filter)
			RenderingServer.positional_soft_shadow_filter_set_quality(_shadow_filter)


func _process(delta: float) -> void:
	var t0 := Time.get_ticks_usec()
	_process_inner(delta)
	Perf.proc_add("viewer", Time.get_ticks_usec() - t0)   # (the live log: process by part)


## The NPC windows (merchant, vault keeper, trainer, the NPC's talk window) close when you walk away from the NPC, as
## the original client's: the server never closes them (no packet for it) - the client does once you are out of
## interaction range (WorldMgr.INTERACT_DISTANCE 192 units = 4.8 m; closed past 6 m, the range our own interactions
## use, so a step aside while shopping keeps it). Owner 2026-10-15: "vendor windows dont close when you walk away".
## Accept / Decline boxes stay: they also carry group invites, resurrection and the like, not tied to an NPC.
const WALK_AWAY_M := 6.0
var _talk_with: Node3D = null                # the NPC the open windows belong to (null: the place they opened at)
var _talk_at := Vector3.ZERO
var _talk_open := 0                          # how many of those windows were open last frame
# owner 2026-10-29 ("any npc window open ... walk away ... despawn by distance like wow"): the other windows an NPC
# opens - the information window (a stable master's routes, an NPC's text), the Realm Exchange, the emblem picker,
# the quest guide, the innkeeper's - are the NPC's when they open within INTERACT_WINDOW_MS of the right-click on it
# (the server answers at once); the same window opened by a /command or a key stays open.
const INTERACT_WINDOW_MS := 3000
var _interact_npc: Node3D = null             # the NPC last right-clicked (interacted with)
var _interact_ms := -100000
var _npc_owned := {}                         # instance id -> true: an "other" window open now that an NPC opened


func _note_interact(n: Node3D) -> void:
	_interact_npc = n
	_interact_ms = Time.get_ticks_msec()


## The windows an NPC may open besides the four NPC windows (those may open by other means too).
func _other_npc_windows() -> Array:
	var out: Array = []
	for w in [ui.windows.get("info"), ui.windows.get("exchange"), ui.get("emblem_window"), ui.get("guide"), _inn_window]:
		if w != null and is_instance_valid(w):
			out.append(w)
	return out


func _watch_npc_windows() -> void:
	if ui == null or player == null:
		return
	var just_talked := Time.get_ticks_msec() - _interact_ms < INTERACT_WINDOW_MS
	for w in _other_npc_windows():
		var id := (w as Node).get_instance_id()
		if not w.visible:
			_npc_owned.erase(id)
		elif not _npc_owned.has(id):
			_npc_owned[id] = just_talked                     # (decided once, when it opens)
	var open: Array = [ui.merchant, ui.vault, ui.trainer, ui.npc_dialog].filter(func(w): return w != null and w.visible)
	open.append_array(_other_npc_windows().filter(func(w): return w.visible and bool(_npc_owned.get((w as Node).get_instance_id(), false))))
	if open.is_empty():
		_talk_open = 0
		_talk_with = null
		return
	var t: Node3D = npcs.target if npcs and npcs.target and is_instance_valid(npcs.target) else null
	# a window more than before, or talking to another NPC near you: the windows belong to the NPC you target - or,
	# right after a right-click, to the NPC clicked, at whatever distance it answered from (a GM account's reach)
	if open.size() > _talk_open or (t != null and t != _talk_with
			and player.global_position.distance_to(t.global_position) <= WALK_AWAY_M):
		if open.size() > _talk_open and just_talked and _interact_npc != null and is_instance_valid(_interact_npc):
			_talk_with = _interact_npc
		else:
			_talk_with = t if t != null and player.global_position.distance_to(t.global_position) <= WALK_AWAY_M else null
		_talk_at = _talk_with.global_position if _talk_with else player.global_position
	_talk_open = open.size()
	if _talk_with and is_instance_valid(_talk_with):
		_talk_at = _talk_with.global_position                # (a merchant walking about)
	var d := player.global_position - _talk_at
	d.y = 0.0
	if d.length() > WALK_AWAY_M:
		for w in open:
			if w.has_method("hide_window"):
				w.hide_window()
			else:
				w.visible = false
		_talk_open = 0
		_talk_with = null


func _process_inner(delta: float) -> void:
	_watch_npc_windows()
	if CacheTrim.tick(delta) and CacheTrim.over_budget() and npcs:
		npcs.drop_models_without_views()                   # (near the card's limit: monsters out of view too)
	if sky and zone:                                       # (zone-only sun shadow splits: SkySystem.set_shadow_zone)
		sky.set_shadow_zone(int(region.current.info.id) if region and region.current else int(zone.info.id))
	if PlayerData.target.get("self", false):             # your own target: your health as it changes
		var h := float(PlayerData.hp) / maxf(1.0, float(PlayerData.max_hp))
		if absf(h - float(PlayerData.target.get("health", 1.0))) > 0.001:
			PlayerData.target["health"] = h
			PlayerData.target_changed.emit()
	if player and zone:
		if _jumping:
			return                                         # a teleport inside the region is arriving (_jump_in_region)
		if _check_exits() or _check_border():
			return                                         # the scene is changing to the next zone
		if Server.active:
			_ride_server_steed()
			if _server_ready and Server.playing:
				_send_position(delta)
				_server_attack_tick()
				if sky and sky.storm_forced < 0.0:         # the server's storm where you stand (rain, dark sky)
					var wz := int(region.current.info.id) if region and region.current else int(zone.info.id)
					sky.storm_target = Server.storm_at(Server.to_region(wz, player.global_position).x)
				if player.sitting and Vector2(player.velocity.x, player.velocity.z).length() > 0.2:
					_server_sit(false)                     # moving stands you up (GamePlayer.OnPlayerMove)
		else:
			_save_left -= delta
		if _save_left <= 0.0 and not Server.active:
			_save_left = 30.0
			_remember_location()
			PlayerData.save_character()
	if not hud:
		return
	player.input_enabled = not ui.options.visible
	if not hud.visible:
		return
	var p := player.global_position
	var lines := [
		"%s - %s   FPS %d   movement: %s" % [zone.info.name, _spots[_spot_index].name, Engine.get_frames_per_second(), player.movement_mode],
		"loc %d, %d, %d (DAoC units)%s" % [p.x / 0.025, p.z / 0.025, p.y / 0.025, "   FLYING" if player.flying else ("   swimming" if player.swimming else "")],
		"ground: %s   textures: %s   sky: %s   time %s%s   storm %d%%" % [zone.terrain_set, zone.texture_set, sky.sky_name,
			sky.clock_text(), "" if sky.time_scale == 1.0 else " (x%d)" % int(sky.time_scale), int(sky.storm * 100.0)] + ("   " + audio.debug_text() if audio else ""),
		"W/S move  A/D turn  Q/E strafe  RMB steer  LMB look  NumLock autorun  ScrollLock walk  T torch  Space jump  |  dev: Ctrl+F1 ground  Ctrl+F2 textures  Ctrl+F4 grass  Ctrl+F5 spot  Ctrl+F6 fly  Ctrl+F7 +1h  Ctrl+F8 time  Ctrl+F9 storm  Ctrl+F12 hide this  |  Esc menu",
		"image checks:  F10 SSAO %s   F11 shadow filter %d (0 hard .. 5 ultra)" % ["on" if sky.environment.ssao_enabled else "OFF", _shadow_filter],
	]
	if Server.active:                                      # what can hold you still, for issue screenshots
		var fo := get_viewport().gui_get_focus_owner()
		lines.append("server: playing %s  ready %s  speed %d%%  enc %.2f  hold %d  cc '%s'  dead %s  riding %s  sitting %s  input %s  focus %s  vel %.2f  oid %d" % [
				Server.playing, _server_ready, (npcs as ServerWorld).max_speed_pct if npcs is ServerWorld else -1, player.encumbrance,
				player.hold_frames, player.cc, player.dead, player.riding, player.sitting, player.input_enabled,
				fo.get_class() if fo else "-", Vector2(player.velocity.x, player.velocity.z).length(), Server.me_oid])
	hud.text = "\n".join(lines)


# ---------------------------------------------------------------- the launcher's server (Offline DAoC Rebuilt)

var _server_ready := false                     # the zone is loaded and the server told (positions go out)
var _server_attack := false
var _pos_left := 0.0
var _last_sent := {}
var _fall_speed := 0.0                         # a landing's fall speed (units / s): the server's fall damage
# The places you sent in the last TRAIL_MS, [msec, zone id, zone position] (owner 2026-10-29: "hiding server lag
# instead of rubberbanding"). The server's speed-hack check (PlayerMovementMonitor) times your updates by when its
# tick read them; a long tick under the bot load made you look 3-6x too fast and it pulled you back to one of your own
# recent places (its cheat log: ~76 "Speed hack (teleport)" on the owner's account). After such a pull-back it checks
# nothing for 0.75 s and takes your next place as it is - so the client keeps you where you are (a small desync, as
# the original client's), unless they come too often (ABSORB_MAX in ABSORB_WINDOW_MS: then it is honoured).
const TRAIL_MS := 3000
const ABSORB_M := 0.15
const ABSORB_MAX := 3
const ABSORB_WINDOW_MS := 10000
var _sent_trail: Array = []
var _absorbed: Array = []                      # msec of the pull-backs kept off lately


## Your position to the server, as the client sent it: often while you move, once a second standing still.
func _send_position(delta: float) -> void:
	_pos_left -= delta
	var p := player.global_position
	var zid := int(region.current.info.id) if region and region.current else int(zone.info.id)
	var heading := Server.heading_of(fposmod(-rad_to_deg(player.facing()), 360.0))
	var v := player.velocity
	var speed := Vector2(v.x, v.z).length() / 0.025
	var state := 0
	if player.swimming:
		state |= 0x04
	if player.sitting:
		state |= 0x10
	if _server_steed != 0:                                 # riding (only while the server has you on its steed)
		state = 0x18
	if player.climbing:                                    # on a ladder / vines: the client's state 7, sent as SITTING |
		state = 0x1C                                       # JUMPING | SWIMMING (StateFlags.CLIMBING; owner 2026-10-14)
	# the action flags (PlayerPositionUpdateHandler.ActionFlags): TARGET_IN_VIEW 0x30 when your target is in sight. Sent
	# as 0 every update cleared the server's TargetInView, so melee said "<target> is not in view!" at a monster hitting
	# you (owner 2026-10-15, Domnann) - only the occasional skill / target packet had set it. A change goes out at once.
	var action := 0
	if npcs is ServerWorld and (npcs as ServerWorld).target_in_view(player.global_position):
		action |= 0x30
	# GROUNT_TARGET_IN_VIEW 0x08: your ground target in sight. Sent as 0, every update cleared the server's
	# GroundTargetInView, so an Animist's turret failed "Your ground target is not in view." at the end of its cast
	# (player reports 2026-10-17; the cast's own packet sets it, the next position update took it away)
	if _server_gt != Vector3.INF and npcs is ServerWorld and (npcs as ServerWorld).sees(player.global_position, _server_gt):
		action |= 0x08
	var moved := _last_sent.is_empty() or p.distance_to(_last_sent.get("p", p)) > 0.05 \
			or heading != int(_last_sent.get("h", -1)) or speed != float(_last_sent.get("s", -1.0)) or _fall_speed > 0.0 \
			or action != int(_last_sent.get("a", -1))
	if _pos_left > 0.0 and not (moved and _pos_left < 0.85):
		return
	_pos_left = 0.1 if moved else 1.0
	var r := Server.to_region(zid, p)
	var z_speed := v.y / 0.025
	var on_ground := player.is_on_floor()
	if _fall_speed > 0.0:                                  # landed: the fall's speed with the ground bit
		z_speed = -_fall_speed
		on_ground = true
		_fall_speed = 0.0
	Server.client.call("Position", r.x, r.y, r.z, speed, z_speed, zid, state, on_ground, heading, action,
			int(PlayerData.health * 100.0), Server.me_oid)
	_last_sent = {"p": p, "h": heading, "s": speed, "a": action}
	var now := Time.get_ticks_msec()
	_sent_trail.append([now, zid, p])
	while not _sent_trail.is_empty() and now - int(_sent_trail[0][0]) > TRAIL_MS:
		_sent_trail.pop_front()


## A jump to one of the places you sent in the last few seconds, in this zone, on foot: the server's lag pull-back.
func _lag_pull_back(e: Dictionary, zid: int) -> bool:
	if int(e.zone) != zid or _server_steed != 0 or player.dead or int(e.get("house", 0)) != 0:
		return false
	var now := Time.get_ticks_msec()
	_absorbed = _absorbed.filter(func(t): return now - int(t) < ABSORB_WINDOW_MS)
	if _absorbed.size() >= ABSORB_MAX:
		return false                                       # (too often: the server means it)
	var pos: Vector3 = e.pos
	for s in _sent_trail:
		if int(s[1]) == zid and Vector2(pos.x - (s[2] as Vector3).x, pos.z - (s[2] as Vector3).z).length() <= ABSORB_M:
			_absorbed.append(now)
			return true
	return false


## The server moved you inside the region (a teleport, a recall, the launcher's teleport to a bot).
func _on_server_jump(e: Dictionary) -> void:
	var zid := int(region.current.info.id) if region and region.current else int(zone.info.id)
	if _lag_pull_back(e, zid):
		_last_sent = {}                                    # your real place goes out at once
		_pos_left = 0.0
		return
	if int(e.zone) == zid:
		var pos: Vector3 = e.pos
		if not zone.city:
			pos.y = maxf(pos.y, zone.height_at(pos.x, pos.z) + 0.3)
		player.global_position = pos
		player.velocity = Vector3.ZERO
		player.hold()
		player.face(-deg_to_rad(float(e.face)))
		_last_sent = {}
	elif region and int(e.zone) in region.neighbours():
		_jump_in_region(e)                                 # another zone of this region: no reload (the client's way)
	else:
		get_tree().change_scene_to_file("res://game/zone_viewer.tscn")   # another region's zone: load it (Server.entry)


## The server moved you into another zone of the region you are in (a realm teleporter Mularn -> West Skona, a
## release to your bind stone, /tele): the original client kept the region and just put you there. Reloading the scene
## here dropped every living the server had already sent - it never sends them again, so they stood there invisible
## until you logged in again (owner 2026-10-13: "all the NPC's are missing around me", invisible mobs killing you).
## Now the scene stays: you wait where you were (frozen, nothing sent) while the zone is built if it isn't yet, then
## are set down there and it becomes the zone you're in, as walking across its border does.
var _jumping := false


func _jump_in_region(e: Dictionary) -> void:
	_jumping = true
	var dest := int(e.zone)
	var at := Server.to_local(_server_world_zone(), float(e.x), float(e.y), float(e.z))   # in this scene
	player.velocity = Vector3.ZERO
	player.hold_frames = 1 << 30
	if not region.zones.has(dest):
		await region.load_around(at)
	if not region.zones.has(dest):                        # it couldn't be built: the old way
		_jumping = false
		get_tree().change_scene_to_file("res://game/zone_viewer.tscn")
		return
	var zl: ZoneLoader = region.zones[dest]
	if not zl.city:
		at.y = maxf(at.y, region.height_at(at.x, at.z) + 0.3)
	player.global_position = at
	player.face(-deg_to_rad(float(e.face)))
	_cross(dest)
	player.hold()                                         # (the floors' collision is in after one physics step)
	_last_sent = {}
	_jumping = false


## The zone the scene's origin belongs to (ServerWorld's positions are relative to it).
func _server_world_zone() -> int:
	return int(region.current.info.id) if region and region.current else int(zone.info.id)


func _server_use(e: Dictionary) -> void:
	if not Server.use(String(e.get("id", ""))):
		PlayerData.message.emit("That isn't one of your skills on this server - drag it again from your skill lists.", "error")


## A clicked [keyword] in the NPC window: the old client whispered it to the NPC you are talking to.
func _server_keyword(_npc_id: String, word: String) -> void:
	Server.command("/whisper " + word)


func _server_release() -> void:
	ui.show_release(false)
	Server.command("/release")


func _server_release_to(kind: String) -> void:
	ui.show_release(false)
	Server.command(("/release " + kind).strip_edges())    # ReleaseCommandHandler: city / house / bind


func _server_invite(who: String) -> void:
	Server.command(("/invite " + who).strip_edges())


func _server_pet(command: String) -> void:
	var c := command.to_lower()
	var aggro: int = {"aggressive": 1, "defensive": 2, "passive": 3}.get(c, 0)
	var walk: int = {"follow": 1, "stay": 2, "goto": 3, "go to": 3, "here": 4}.get(c, 0)
	var order: int = {"attack": 1, "release": 2}.get(c, 0)
	if aggro or walk or order:
		Server.client.call("PetCommand", aggro, walk, order)


## You as your own target on the server too (heals, buffs on yourself).
func _server_target_self() -> void:
	if Server.active and Server.client:
		Server.client.call("Target", Server.me_oid, 0x6000)


var _server_steed := 0                         # the object id of the server's steed you sit on (Riding), or 0


func _server_mount(steed: int) -> void:
	_server_steed = steed
	player.riding = steed != 0
	if steed == 0:
		var at := player.global_position
		player.global_position = Vector3(at.x, npcs.ground_at(at) + 0.3, at.z)


func _server_dismount() -> void:
	if _server_steed != 0:
		Server.client.call("Dismount")


## On the server's steed: you sit in its saddle and face its way (the server moves it).
func _ride_server_steed() -> void:
	if _server_steed == 0 or not (npcs is ServerWorld):
		return
	var steed: NpcState = (npcs as ServerWorld).by_oid.get(_server_steed)
	if steed == null:
		return
	player.global_position = (npcs as ServerWorld).seat_of(steed)
	player.char_yaw = steed.yaw + PI                     # the steed model faces +Z, the player faces -Z
	player._apply_model_yaw()


func _server_item_info(it: Dictionary) -> void:
	var id := String(it.get("id", ""))
	var parts := id.split(":")
	if id.begins_with("srv:") and parts.size() == 2:
		Server.info_item_slot = int(parts[1])              # (its answer gets SALVAGE / REPAIR: GameUI.show_item_buttons)
		Server.detail_shown(1, int(parts[1]))
	elif id.begins_with("srvm:") and parts.size() == 3:
		Server.detail_shown(4, int(parts[1]) * 30 + int(parts[2]))


func _server_skill_info(e: Dictionary) -> void:
	var r := Server.skill_detail(e)                       # (a hybrid's spell asks as a spell, not an ability)
	if not r.is_empty():
		Server.detail_shown(r[0], r[1])


var _member_pick := {}                         # the group member whose row you clicked last (not you)


## The group page's DISBAND: your target, if it is in your group, goes - the server's /disband <name> (owner
## 2026-10-17: with a bot targeted - its row, a click on it, the group-target keys - DISBAND broke up the whole group;
## the bare /disband takes you out and deletes your /spawn companions). A member's row clicked while it isn't drawn
## (out of view: no target here) counts too, as long as nothing else has been targeted since. Otherwise the bare
## /disband: you leave.
func _server_disband() -> void:
	var name := ""
	var pick := _member_pick
	_member_pick = {}
	if npcs is ServerWorld:
		var sw := npcs as ServerWorld
		var t: Npc = sw.target if sw.target and is_instance_valid(sw.target) else null
		for m in PlayerData.group_rows():
			if bool(m.get("me", false)):
				continue
			var st: NpcState = sw.by_oid.get(int(m.get("oid", 0)))
			if t != null and st != null and st.view == t:
				name = String(m.get("name", ""))
				break
			if t == null and not pick.is_empty() and int(m.get("oid", 0)) == int(pick.get("oid", -1)):
				name = String(m.get("name", ""))
	Server.command("/disband " + name if name != "" else "/disband")


## The client's /loc line: the zone, your place in it (DAoC units, as the debug text) and the way you face
## (game.dll "%s: loc=%d,%d,%d dir=%d"; dir in degrees of the server's heading).
func loc_line() -> String:
	var p := player.global_position
	var heading := Server.heading_of(fposmod(-rad_to_deg(player.facing()), 360.0))
	return "%s: loc=%d,%d,%d dir=%d" % [zone.info.name, roundi(p.x / 0.025), roundi(p.z / 0.025), roundi(p.y / 0.025),
			int(heading * 360 / 4096) % 360]


## Attack mode on / off: the key and the target window's ATTACK (the server's PlayerAttackRequest, or the game's).
func _toggle_attack() -> void:
	if Server.active:
		_set_server_attack(not _server_attack, true)
	elif combat:
		combat.toggle()


var _attack_on: NpcState = null                # the target last seen alive in the server's attack mode


## Attack mode on the server (the key, the server's own AttackMode, your target dying): the weapon drawn or sheathed
## with its sound, the battle music in or out (owner 2026-10-14: no battle music in the server's game) - `tell` sends
## it to the server (PlayerAttackRequest).
func _set_server_attack(on: bool, tell: bool) -> void:
	if tell:
		Server.client.call("Attack", on)
	if on == _server_attack:
		return
	_server_attack = on
	_attack_on = null
	player.set_combat(on, player.weapon_stance())        # a staff held as a staff (owner 2026-10-11)
	var set_id := int(WeaponModels.info(int(MeleeMath.weapon(PlayerData).get("model", 0))).get("sound_set", 0))
	if set_id > 0 and player.sounds:
		player.sounds.weapon(set_id, "activate" if on else "deactivate")   # the client's draw / sheathe sounds
	if audio:
		audio.set_battle(on)


## Attack mode drops by itself when your target dies - by your blow, a pet's, a spell, anyone's (owner 2026-10-10,
## tested on the old client; owner 2026-10-14: it stayed on in the server's game) - the client ends it and tells the
## server, which keeps swinging at nothing otherwise; dead yourself, it is over too.
func _server_attack_tick() -> void:
	if not _server_attack:
		return
	if PlayerData.hp <= 0:
		_set_server_attack(false, false)
		return
	var t: Npc = npcs.target if npcs and npcs.target and is_instance_valid(npcs.target) else null
	if t and t.state.alive:
		_attack_on = t.state
	elif _attack_on and not _attack_on.alive:
		_set_server_attack(false, true)


var sit_changed_at := -100000                  # your own sit / stand (the server's status catches up a moment later)


func _server_sit(on: bool) -> void:
	sit_changed_at = Time.get_ticks_msec()
	player.sit(on)
	Server.client.call("Sit", on)


func _on_server_world(kind: String, d: Dictionary) -> void:
	match kind:
		"attack_mode":                                     # the server ended (or began) your attack
			_set_server_attack(bool(d.on), false)
		"time":                                            # the server's time of day (ms into the game day)
			if sky:
				sky.day_increment = Server.day_increment
				sky.set_hour(fposmod(float(d.time) / 3600000.0, 24.0))


## Tests: screenshots of the interface windows (character, inventory, options pages), then quit.
func _ui_shots(folder: String) -> void:
	DirAccess.make_dir_recursive_absolute(folder)
	await get_tree().create_timer(6.0).timeout
	var shots := [["hud", func(): pass], ["npc_target", func():
				if npcs:
					var best: Npc = null
					for i in npcs.active:
						var n: Npc = npcs.active[i]
						if n.spawn.role == "merchant" and (best == null or n.global_position.distance_to(player.global_position) < best.global_position.distance_to(player.global_position)):
							best = n
					if best:
						npcs.select(best)
						player.face(atan2(-(best.global_position.x - player.global_position.x), -(best.global_position.z - player.global_position.z)))],
			["merchant", func():
				if npcs and npcs.target:
					PlayerData.set_copper(25000)
					ui.merchant.open_for(String(npcs.target.spawn.name), String(npcs.target.spawn.items))
					var first: Array = ItemDb.merchant_list(String(npcs.target.spawn.items))
					if first.size() > 0:
						ui.merchant.select_item(String(first[0][2]))
						ui.merchant.buy_selected()
						ui.merchant.select_item(String(first[0][2]))],
			["door_open", func():
				ui.merchant.hide_window()
				if zone.doors.size() > 0:
					var best: ZoneDoor = zone.doors[0]
					for d in zone.doors:
						if d.global_position.distance_to(player.global_position) < best.global_position.distance_to(player.global_position):
							best = d
					best.open_door()
					player.global_position = best._world(best.closed.origin) + best.closed.basis.x.normalized() * 4.0 + Vector3(0, 0.3, 0)
					player.face(atan2(-(best.global_position.x - player.global_position.x), -(best.global_position.z - player.global_position.z)))],
			["plates_friendly", func():
				if npcs and npcs.target:
					var t: Npc = npcs.target
					npcs.clear_target()
					_stand_near(t.global_position, 6.0)],
			["plates_monster", func():
				if npcs:
					var best: NpcState = null
					for st in npcs.states:
						var s: NpcState = st
						if int(s.spawn.realm) == 0 and s.level > 0 and s.pos.distance_to(player.global_position) > 60.0 								and (best == null or s.pos.distance_to(player.global_position) < best.pos.distance_to(player.global_position)):
							best = s
					if best:
						_stand_near(best.pos, 7.0)],
			["combat_start", func():                     # stand by a monster, target it, attack mode
				PlayerData.message.connect(func(t, _k): print("[chat] ", t))
				if npcs and combat:
					var best: NpcState = null
					for st in npcs.states:
						var s: NpcState = st
						if int(s.spawn.realm) == 0 and s.level <= 1 and s.alive 								and (best == null or s.pos.distance_to(player.global_position) < best.pos.distance_to(player.global_position)):
							best = s
					if best:
						_stand_near(best.pos, 2.2)
						await get_tree().create_timer(0.6).timeout
						if best.view:
							npcs.select(best.view)
							PlayerData.level = 5                     # test: a Blademaster with Blades 5
							PlayerData.promote(43)
							PlayerData.specs["Blades"] = 5
							combat.set_attack_mode(true)
							PlayerData.use_skill.emit({"kind": "style", "id": "784"})   # Shining Blade
						print("[test] fighting ", best.spawn.name, " lvl ", best.level, " hp ", best.hp)],
			["combat_1", func(): PlayerData.use_skill.emit({"kind": "style", "id": "784"})], ["combat_2", func(): PlayerData.use_skill.emit({"kind": "style", "id": "784"})], ["combat_3", func(): PlayerData.use_skill.emit({"kind": "style", "id": "784"})],
			["combat_4", func(): PlayerData.use_skill.emit({"kind": "style", "id": "784"})], ["combat_5", func(): PlayerData.use_skill.emit({"kind": "style", "id": "784"})], ["combat_6", func(): pass],
			["combat_7", func():
				print("[test] player hp ", PlayerData.hp, "/", PlayerData.max_hp, " xp ", PlayerData.xp, " level ", PlayerData.level)],
			["map", func():
				ui.map_window.toggle_window()],
			["map_close", func():
				ui.map_window.toggle_window()],
			["sheet_skills", func():
				PlayerData.level = 5
				PlayerData.promote(43)                       # Blademaster (test character, never saved)
				PlayerData.specs["Blades"] = 5
				PlayerData.specs["Parry"] = 2
				PlayerData._load_specs()
				ui.show_sheet("skills")],
			["sheet_combat", func(): ui.show_sheet("combat")],
			["trainer", func():
				for i in npcs.active:
					var n: Npc = npcs.active[i]
					if str(n.spawn.get("trains", "") if n.spawn.get("trains") != null else "") == "Guardian":
						_trainer_interact(n)
						break],
			["trainer_close", func():
				ui.trainer.hide_window()
				ui.npc_dialog.hide_window()],
			["esc_untarget", func():                     # target the monster, then Esc: no target, no options
				if npcs and not npcs.active.is_empty():
					npcs.select(npcs.active.values()[0])
					var k := InputEventKey.new()
					k.keycode = KEY_ESCAPE
					k.pressed = true
					ui.handle_input(k)
					print("esc_untarget: target=", PlayerData.target, " options=", ui.options.visible)],
			["inventory", func():
				ui.sheet_tab = ""
				ui.show_sheet("inventory")
				for slot in PlayerData.inventory:                # right-click an item: the mini delve
					PlayerData.select_slot(int(slot))
					break],
			["character", func(): ui.show_sheet("character")],
			["vertical_bar", func():
				ui.quickbars[1].set_vertical(true)
				PlayerData.set_pet({"name": "Test Pet"})
				PlayerData.set_target({"name": "Wood Imp", "con_color": Color(0.4, 0.6, 1.0), "health": 0.6})],
			["chat_typing", func():
				ui.chat.start_typing("hello")
				for i in 3:
					ui.message("You hit the wood imp for %d damage!" % (12 + i), "combat")
				PlayerData.delve.emit(ItemDb.item("training_sword_hib"))],
			["options_video", func():
				ui.chat.stop_typing()
				ui.options.show_window()], ["options_controls", func(): ui.options.show_page("controls")],
			["options_chat", func(): ui.options.show_page("chat")],
			["options_windows", func(): ui.options.show_page("windows")]]
	for s in shots:
		s[1].call()
		await get_tree().create_timer(1.5).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s/%s.png" % [folder, s[0]])
	get_tree().quit()


## Tests: put the player a few metres from a point, facing it.
func _stand_near(at: Vector3, metres: float) -> void:
	var p := at + Vector3(metres, 0, 0)
	p.y = zone.height_at(p.x, p.z) + 0.2
	player.global_position = p
	player.face(atan2(-(at.x - p.x), -(at.z - p.z)))


func _tour(prefix: String) -> void:
	get_window().size = Vector2i(1600, 900)
	var only: PackedStringArray = String(args.get("spots", "")).to_lower().split(",", false)
	for i in _spots.size():
		var spot_name := String(_spots[i].name)
		if only.size() and not only.has(spot_name.to_lower()):
			continue
		_go_to_spot(i)
		var facing := player.cam_yaw
		for turn in [0.0, PI]:
			player.face(facing + turn)
			player.cam_pitch = -0.18
			player.cam_distance = 7.0
			for f in 75:                                   # clutter chunks build a couple per frame
				await get_tree().process_frame
			var path := "%s_%s_%d.png" % [prefix, spot_name.replace(" ", "_"), int(rad_to_deg(turn))]
			get_viewport().get_texture().get_image().save_png(path)
			print("saved ", path)
	get_tree().quit()


func _bench() -> void:
	get_window().size = Vector2i(int(args.get("width", "1920")), int(args.get("height", "1080")))
	DisplayServer.window_set_vsync_mode(DisplayServer.VSYNC_DISABLED)   # measure headroom, not the monitor cap
	var total := 0.0
	var samples := 0
	for i in _spots.size():
		_go_to_spot(i)
		for yaw in [0.0, PI * 0.5, PI, PI * 1.5]:
			player.face(yaw)
			player.cam_pitch = -0.15
			player.cam_distance = 7.0
			for f in 30:                                   # settle
				await get_tree().process_frame
			var t0 := Time.get_ticks_usec()
			for f in 120:
				await get_tree().process_frame
			var fps := 120.0 / ((Time.get_ticks_usec() - t0) / 1e6)
			print("bench %-22s yaw %3d: %.0f fps" % [_spots[i].name, int(rad_to_deg(yaw)), fps])
			total += fps
			samples += 1
	print("bench average %.0f fps at %s" % [total / samples, get_window().size])
	get_tree().quit()


func _shots(spec: String, prefix: String) -> void:
	get_window().size = Vector2i(1600, 900)
	player.visible = args.has("show-player")         # --show-player: stand the player at each shot target
	var cam := Camera3D.new()
	cam.far = 1500.0
	cam.fov = 60.0
	add_child(cam)
	cam.make_current()
	var k := 0
	for item in spec.split(";", false):
		var v := item.split(",")
		var x := float(v[0]) * 0.025
		var z := float(v[1]) * 0.025
		var face := deg_to_rad(float(v[2]))
		var dist := float(v[3]) if v.size() > 3 else 25.0
		var aim := float(v[4]) if v.size() > 4 else 3.0      # optional 5th value: aim height above the ground (m)
		var dir := Vector3(sin(face), 0.0, -cos(face))       # compass heading: 0 north (-z), 90 east (+x)
		var target := Vector3(x, zone.height_at(x, z) + aim, z)
		var eye := target - dir * dist
		eye.y = maxf(zone.height_at(eye.x, eye.z) + 1.8, target.y + dist * 0.12)
		if player.visible:
			player.global_position = Vector3(x, zone.height_at(x, z), z)
			player.face(face + PI)                           # facing the camera
			target.y = player.global_position.y + 1.0
		cam.global_position = eye
		cam.look_at(target)
		for f in 30:
			await get_tree().process_frame
		var path := "%s_%d.png" % [prefix, k]
		get_viewport().get_texture().get_image().save_png(path)
		print("saved ", path)
		k += 1
	get_tree().quit()
