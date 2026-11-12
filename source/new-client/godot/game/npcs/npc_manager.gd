class_name NpcManager
extends Node3D
## The zone's people and monsters (content/zones/zoneNNN/npcs.json + content/monsters/monsters.json).
## The WORLD SIMULATION runs every spawn all the time (NpcState: idle / roam now; health, aggro, respawn later) -
## the world is alive wherever the player is, as on the original servers. Only the 3D VIEW of a spawn (Npc) is
## attached within VIEW_RANGE and freed beyond it, the way the client only drew what was near. Clicking an NPC targets it: "You target [X]." and the examine line
## in chat, its name / con colour / health on the status panel (PlayerData.target).
## Seamless zones: with a RegionWorld (the zone viewer's), the spawns of every loaded zone of the region run here, in
## the scene's frame (the zone you're in at the origin, the neighbours at their region offsets): add_zone / remove_zone
## as neighbours load and unload, rebase when you cross a border (everything shifts by the new zone's offset).

const VIEW_RANGE := 140.0                   # models drawn within this distance (rendering only)
const FREE_RANGE := 170.0

var data := {}
var models := {}
var player: Node3D
var zone: ZoneLoader
var region: RegionWorld                     # null: one zone (tests, the world host)
var states: Array = []                      # NpcState for EVERY spawn - the simulation
var active := {}                            # spawn index -> Npc (views near the player)
var target: Npc
var _scenes := {}
var _tick := 0.0
var _frame := 0
var _sim_tick := 0
const SIM_NEAR_M := 120.0                    # full-rate simulation within this of you
var _con: Array = []


func setup(zone_dir: String, zone_loader: ZoneLoader, p: Node3D) -> void:
	zone = zone_loader
	player = p
	var t = JSON.parse_string(FileAccess.get_file_as_string(zone_dir.path_join("npcs.json")))
	data = t if t is Dictionary else {"spawns": []}
	var m = JSON.parse_string(FileAccess.get_file_as_string("res://content/monsters/monsters.json"))
	models = m if m is Dictionary else {}
	_con = data.get("con", [])
	var added := _add_spawns(data.get("spawns", []), int(zone.info.id) if zone else -1, Vector3.ZERO)
	set_physics_process(true)
	_settle_heights.call_deferred(added)
	var plates := NamePlates.new()                         # overhead names (Label3Ds on the views, depth-tested)
	plates.manager = self
	add_child(plates)


func _add_spawns(spawns: Array, zone_id: int, offset: Vector3) -> Array:
	var added := []
	for s in spawns:
		# a template's model / size lists: one picked per spawn (old server GameNPC.LoadTemplate)
		var ml: Array = s.get("models", [])
		if ml.size() > 1:
			s["model"] = String(ml[randi() % ml.size()])
		var sl: Array = s.get("sizes", [])
		if sl.size() > 1:
			s["size"] = int(sl[randi() % sl.size()])
		var eqs := String(s.get("equipment", "")).split(";", false)   # a template's inventory list: one per spawn
		if eqs.size() > 1:
			s["equipment"] = eqs[randi() % eqs.size()]
		if offset != Vector3.ZERO:
			_shift_spawn(s, offset)
		var st := NpcState.new(states.size(), s, models.get(String(s.model), {}))
		st.zone_id = zone_id
		states.append(st)
		added.append(st)
	return added


## A neighbour zone of the region came into the world (RegionWorld): its spawns join the simulation at its offset.
## Its equipment / styles / ambient lines join the tables (their keys are the templates', the same in every zone).
func add_zone(z: ZoneLoader) -> void:
	var zid := int(z.info.id)
	for st in states:
		if (st as NpcState).zone_id == zid:
			return
	if not FileAccess.file_exists(z.dir.path_join("npcs.json")):
		return
	var t = JSON.parse_string(FileAccess.get_file_as_string(z.dir.path_join("npcs.json")))
	if not (t is Dictionary):
		return
	for key in ["equipment", "styles", "ambient"]:
		var mine: Dictionary = data.get(key, {})
		for k in (t as Dictionary).get(key, {}):
			if not mine.has(k):
				mine[k] = t[key][k]
		data[key] = mine
	var added := _add_spawns((t as Dictionary).get("spawns", []), zid, z.position)
	_settle_heights(added)


## A neighbour zone left the world: its spawns go (not one in a fight - it stays until the fight is over, then goes
## with the next unload; spawns added while playing - pets, quest events - never go with a zone).
func remove_zone(zone_id: int) -> void:
	var keep := []
	for st in states:
		var s: NpcState = st
		if s.zone_id == zone_id and not s.fighting and not s.spawn.get("temp", false):
			if s.view:
				if s.view == target:
					clear_target()
				s.view.queue_free()
				s.view = null
			_seen.erase(s)
			continue
		keep.append(s)
	if keep.size() == states.size():
		return
	states = keep
	_drop_unused_models()
	var views := {}
	for i in states.size():                                   # indices are the views' keys
		var s: NpcState = states[i]
		s.index = i
		if s.view:
			views[i] = s.view
	active = views


## You crossed a border: the new zone is now at the origin, everything moved by -d.
func rebase(d: Vector3) -> void:
	var done := {}
	for st in states:
		var s: NpcState = st
		s.home -= d
		s.pos -= d
		s.walk_to -= d
		if not done.has(s.spawn):
			done[s.spawn] = true
			_shift_spawn(s.spawn, -d)
		if s.view:
			(s.view as Node3D).position -= d


func _shift_spawn(s: Dictionary, d: Vector3) -> void:
	var p: Array = s.pos
	s["pos"] = [float(p[0]) + d.x, float(p[1]) + d.y, float(p[2]) + d.z]
	var path = s.get("path")
	if path is Dictionary and (path as Dictionary).get("points") is Array:
		var pts := []
		for q in path.points:
			var a: Array = (q as Array).duplicate()
			a[0] = float(a[0]) + d.x
			a[1] = float(a[1]) + d.y
			a[2] = float(a[2]) + d.z
			pts.append(a)
		var np: Dictionary = (path as Dictionary).duplicate()   # (a path may be shared by several spawns)
		np["points"] = pts
		s["path"] = np


## Stand every spawn on the floor under it (owner 2026-10-10: Gavina, Shannon Estuary, floated 0.6 m up). The DB's
## Z is the old server's, and the old server let the client put NPCs down; here a still NPC kept that Z exactly (only
## walkers followed the ground). Each spawn not flagged FLYING (0x20) or SWIMMING (0x100) takes the highest floor
## (terrain or any fixture floor: porches, house floors, bridges) at most 1.2 m above its Z, within 6 m below; with
## nothing there it keeps its Z. settle_report holds what moved (tests/float_audit.gd).
var settle_report: Array = []


func _settle_heights(which: Array) -> void:
	for i in 3:
		await get_tree().physics_frame                     # the zone's colliders are in the physics world
	var space := get_world_3d().direct_space_state
	settle_report.clear()
	for st in which:
		var s: NpcState = st
		if int(s.spawn.get("flags", 0)) & (0x20 | 0x100):
			continue
		var p := s.home
		var best := -INF
		var t := _grid_at(p) if zone and not zone.city else -INF   # (a city's grid is its highest floor)
		if t <= p.y + 1.2 and t >= p.y - 6.0:
			best = t
		var q := PhysicsRayQueryParameters3D.create(Vector3(p.x, p.y + 1.2, p.z), Vector3(p.x, p.y - 6.0, p.z), ZoneLoader.LAYER_FLOOR)
		q.hit_back_faces = false
		var hit := space.intersect_ray(q)
		if not hit.is_empty() and float(hit.position.y) > best:
			best = float(hit.position.y)
		if best == -INF or absf(best - p.y) < 0.02:
			continue
		if zone and _water_at(p) > best + 0.3:
			continue                                       # over water: swimmers keep their height (water beetles)
		settle_report.append([String(s.spawn.name), snappedf(p.y - best, 0.01), Vector3(p.x, p.y, p.z)])
		s.home.y = best
		s.pos.y = best
		if s.view:
			(s.view as Node3D).global_position.y = best


## A spawn added while the game runs (quest event monsters): simulated like the rest, never respawns ("temp").
func add_spawn(s: Dictionary) -> NpcState:
	s["temp"] = true
	var st := NpcState.new(states.size(), s, models.get(String(s.get("model", "0")), {}))
	states.append(st)
	return st


## Take a temporary spawn out of the world (its view goes with the next view pass).
func remove_spawn(st: NpcState) -> void:
	st.alive = false
	st.fighting = false
	st.corpse_left = 0.0
	st.respawn_left = 1e9


## The con of a monster to a character of a level: -3 grey .. 0 yellow (its own level) .. 3 purple - the zone's
## table, else the level difference (a zone without one).
func con_of(level: int, npc_level: int) -> int:
	return con_level(level, npc_level) if not _con.is_empty() else clampi(npc_level - level, -3, 3)


func con_level(player_level: int, npc_level: int) -> int:
	if player_level < 0 or player_level >= _con.size():
		return 0
	var row: Array = _con[player_level]
	return int(row[npc_level]) if npc_level >= 0 and npc_level < row.size() else 3


func ground_at(p: Vector3) -> float:
	if region:
		return region.height_at(p.x, p.z, p.y)
	return zone.height_at(p.x, p.z, p.y) if zone else p.y


func _grid_at(p: Vector3) -> float:
	return region.height_at(p.x, p.z) if region else zone.height_at(p.x, p.z)


func _water_at(p: Vector3) -> float:
	return region.water_level_at(p.x, p.z) if region else zone.water_level_at(p.x, p.z)


## The floor under a walker that goes indoors (a spawn flagged "floors" - the roadside inns' adventurers): the highest
## floor at most 1.2 m above it (house floors, porches, bridges), else the ground. The player's floor pick, for NPCs.
func floor_at(p: Vector3) -> float:
	var g := ground_at(p)
	if zone == null or not zone.is_inside_tree():
		return g
	var q := PhysicsRayQueryParameters3D.create(Vector3(p.x, p.y + 1.2, p.z), Vector3(p.x, p.y - 6.0, p.z), ZoneLoader.LAYER_FLOOR)
	var hit := zone.get_world_3d().direct_space_state.intersect_ray(q)
	return maxf(g, float(hit.position.y)) if not hit.is_empty() else g


func instance_model(path: String) -> Node3D:
	if not _scenes.has(path):
		var tp := Time.get_ticks_usec()
		_scenes[path] = _pack_model(path)
		prof_parse_us += Time.get_ticks_usec() - tp
		prof_parsed += 1
	var ps: PackedScene = _scenes[path]
	return ps.instantiate() as Node3D if ps else null


static func _pack_model(path: String) -> PackedScene:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	state.base_path = ProjectSettings.globalize_path(path.get_base_dir())
	if doc.append_from_file(path, state) != OK:
		return null
	CacheTrim.share_textures(state, path)                # (one copy of each texture file across all monsters)
	# a glow map laid out on the model's second UV set (build_monsters: emissiveTexture texCoord 1 - the Glowing Demon
	# Knight's eye slits; owner 2026-10-20: its eye glowed on the chest): Godot's glTF import drops texCoord there
	var mats_json: Array = state.json.get("materials", []) if state.json is Dictionary else []
	var mats := state.get_materials()
	for i in mini(mats_json.size(), mats.size()):
		var et = (mats_json[i] as Dictionary).get("emissiveTexture", {})
		if et is Dictionary and int(et.get("texCoord", 0)) == 1 and mats[i] is BaseMaterial3D:
			(mats[i] as BaseMaterial3D).emission_on_uv2 = true
	# their render-server materials made on the main thread (GpuJobs: made on a worker - this runs on the zone and
	# NPC-view workers - they raced the render thread's draw lists)
	GpuJobs.ready_materials(Array(mats))
	# the generated tree is only the template: free it once packed - a loose node is never freed for us, and
	# every zone left all its monster models and their textures behind (owner 2026-10-10: 7-8 fps after a zone
	# change, video memory full; ~970 MB more after Camelot Hills -> Shannon Estuary than a fresh start)
	var root := doc.generate_scene(state)
	var packed := PackedScene.new()
	packed.pack(root)
	root.free()
	return packed


## Seamless zones: a neighbour's monster models parsed and packed off the main thread (RegionWorld's worker, while
## the zone builds) - a monster seen for the first time otherwise parsed its glTF on the spot (70-200 ms hitches at a
## gallop). skip: the models already known (a copy made on the main thread); model_info: monsters.json (read only).
static func pack_zone_models(zone_dir: String, model_info: Dictionary, skip: Dictionary) -> Dictionary:
	var out := {}
	var t = JSON.parse_string(FileAccess.get_file_as_string(zone_dir.path_join("npcs.json"))) 			if FileAccess.file_exists(zone_dir.path_join("npcs.json")) else null
	if not (t is Dictionary):
		return out
	for s in (t as Dictionary).get("spawns", []):
		var ids: Array = s.get("models", [])
		if ids.is_empty():
			ids = [s.get("model", "")]
		for id in ids:
			var info: Dictionary = model_info.get(String(id), {})
			var path := String(info.get("gltf", ""))
			if path != "" and not skip.has(path) and not out.has(path):
				out[path] = _pack_model(path)
	return out


func adopt_models(packed: Dictionary) -> void:
	for k in packed:
		if not _scenes.has(k):
			_scenes[k] = packed[k]


## The models no spawn still in the world uses are let go (a ride across a region kept every zone's monsters' models
## and textures; views alive keep their own copies).
func _drop_unused_models() -> void:
	var used := {}
	for st in states:
		var info: Dictionary = (st as NpcState).info
		if info.has("gltf"):
			used[String(info.gltf)] = true
	for k in _scenes.keys():
		if not used.has(k):
			_scenes.erase(k)


## Past the video memory budget (CacheTrim.over_budget; owner 2026-10-29, the 6 GB card filled and the frame rate
## never came back): the monster models no view near you draws go as well - a spawn coming into view gets its model
## again on the NPC-view worker (_prepared), as when its zone first loaded. Returns how many went.
func drop_models_without_views() -> int:
	var used := {}
	for i in active:
		if int(i) >= 0 and int(i) < states.size():
			var info: Dictionary = (states[int(i)] as NpcState).info
			if info.has("gltf"):
				used[String(info.gltf)] = true
	var n := 0
	for k in _scenes.keys():
		if not used.has(k) and _scenes[k] != null:
			_scenes.erase(k)
			n += 1
	return n


## The world simulation: every spawn, every physics tick, near the player or not.
var prof_phys_us := 0
var prof_proc_us := 0


func _physics_process(delta: float) -> void:
	var t0 := Time.get_ticks_usec()
	_physics_step(delta)
	Perf.phys_add("npcs", Time.get_ticks_usec() - t0)        # (Perf's live log: the physics tick by part)


func _physics_step(delta: float) -> void:
	var tq := Time.get_ticks_usec()
	_physics_body(delta)
	prof_phys_us += Time.get_ticks_usec() - tq


func _physics_body(delta: float) -> void:
	var g := Callable(self, "ground_at")
	var gf := Callable(self, "floor_at")
	# simulation level of detail: spawns far from you (and not fighting) step every 4th tick with the time gathered -
	# the whole zone (1,000+ spawns) stepped 60 times a second cost 10 ms a tick in a raid (crowd_probe)
	_sim_tick += 1
	var pp: Vector3 = player.global_position if player else Vector3.ZERO
	if player == null:
		_step_host(delta, g)
		return
	for st in states:
		var s: NpcState = st
		# (no player - the world host's zones: every spawn is far from any player, the same reduced rate; a fight runs
		# every tick)
		if not s.fighting and (player == null or (not Perf.brute and s.pos.distance_squared_to(pp) > SIM_NEAR_M * SIM_NEAR_M)):
			s.sim_pending += delta
			if (_sim_tick + s.index) % 4 != 0:
				continue
			s.step(s.sim_pending, gf if s.spawn.get("floors", false) else g)
			s.sim_pending = 0.0
		else:
			s.step(delta + s.sim_pending, gf if s.spawn.get("floors", false) else g)
			s.sim_pending = 0.0
		if not (st as NpcState).events.is_empty():
			for e in (st as NpcState).events:
				ambient(st, e, String((st as NpcState).spawn.name))
			(st as NpcState).events.clear()
	_seeing(delta)


## A world host zone (no player): nobody sees these spawns, so each steps every 8th tick with the time gathered - an
## eighth of the zone each tick (iterating every spawn each tick cost the host ~20 ms for 15 zones). Their fights are
## Combat's (every tick); a spawn's own step is its roaming, regeneration, corpse and respawn timers.
const HOST_STRIDE := 8


func _step_host(delta: float, g: Callable) -> void:
	_host_time += delta
	var k := _sim_tick % HOST_STRIDE
	for i in range(k, states.size(), HOST_STRIDE):
		var s: NpcState = states[i]
		s.step(_host_time - s.sim_pending, g)                # sim_pending: when it last stepped
		s.sim_pending = _host_time
		if not s.events.is_empty():
			for e in s.events:
				ambient(s, e, String(s.spawn.name))
			s.events.clear()


var _host_time := 0.0


## GameNPC.FireAmbientSentence (MobXAmbientBehaviour, by name): on a trigger - spawning, dying, aggroing, fighting,
## roaming, killing, interact, seeing - one of its lines for that trigger is picked and said at its chance:
## {sourcename} its name, {targetname} the target's, {class} / {race} yours (an NPC target: "NPC"). Voice: say
## (default: "the X says, ..." within 512 units), c = the text as it is (within 512), y = yell (1024), s = a system
## line (512), b = broadcast, p = a popup to whoever spoke to it.
func ambient(st: NpcState, trigger: String, target := "", target_is_player := false) -> void:
	var list: Array = data.get("ambient", {}).get(String(st.spawn.name).to_lower(), [])
	if list.is_empty():
		return
	var c := list.filter(func(a): return String(a.trigger) == trigger)
	if c.is_empty():
		return
	var a: Dictionary = c[randi() % c.size()]
	if randf() * 100.0 >= float(a.get("chance", 100)):
		return
	var text := String(a.text).replace("{sourcename}", String(st.spawn.name)).replace("{targetname}", target) \
			.replace("{controller}", "")
	text = text.replace("{class}", PlayerData.class_title() if target_is_player else "NPC") \
			.replace("{race}", String(PlayerData.character.get("race", "")) if target_is_player else "NPC")
	if int(a.get("emote", 0)) > 0 and st.view:
		(st.view as Npc).play_emote(int(a.emote))           # GameNPC.Emote
	var units := st.pos.distance_to(player.global_position) / 0.025 if player else 0.0
	var n := String(st.spawn.name)
	var who := ("the " + n) if n.length() > 0 and n[0] == n[0].to_lower() else n
	match String(a.get("voice", "")):
		"b":
			if units <= 25000.0:
				PlayerData.message.emit(text, "broadcast")
		"y":
			if units <= 1024.0:
				PlayerData.message.emit("%s yells, \"%s\"" % [who.substr(0, 1).to_upper() + who.substr(1), text], "say")
		"s":
			if units <= 512.0:
				PlayerData.message.emit(text, "system")
		"c":
			if units <= 512.0:
				PlayerData.message.emit(text, "say")
		"p":
			if target_is_player:
				PlayerData.message.emit(text, "system")
		_:
			if units <= 512.0:
				PlayerData.message.emit("%s says, \"%s\"" % [who.substr(0, 1).to_upper() + who.substr(1), text], "say")


## StandardMobBrain.FireAmbientSentence: a monster with a "seeing" line says it when you come into its aggro range
## (once until you leave it again).
var _seen := {}
var _seeing_left := 0.0


func _seeing(delta: float) -> void:
	_seeing_left -= delta
	if _seeing_left > 0.0 or player == null or data.get("ambient", {}).is_empty():
		return
	_seeing_left = 0.5
	var amb: Dictionary = data.get("ambient", {})
	for s in states:
		var st: NpcState = s
		if not st.alive or not amb.has(String(st.spawn.name).to_lower()):
			continue
		var inside := st.pos.distance_to(player.global_position) <= mini(int(st.spawn.get("aggro_range", 0)), 3600) * 0.025
		if inside and not _seen.has(st):
			_seen[st] = true
			ambient(st, "seeing", PlayerData.display_name(), true)
		elif not inside:
			_seen.erase(st)


## Views: attach models to spawns within view range, free them beyond (a few per tick so nothing hitches).
func _process(delta: float) -> void:
	var tq := Time.get_ticks_usec()
	_process_body(delta)
	prof_proc_us += Time.get_ticks_usec() - tq
	Perf.proc_add("npc manager", Time.get_ticks_usec() - tq)


func _process_body(delta: float) -> void:
	if player == null:
		return
	_frame += 1
	var cam := get_viewport().get_camera_3d()
	var tt := Time.get_ticks_usec()
	if AnimLod.threaded:
		AnimLod.snapshot(cam)                          # the views step themselves after this, on worker threads
		for i in active:
			(active[i] as Npc).sync_pick()
		prof_tick_us += Time.get_ticks_usec() - tt
		_tick -= delta
		if _tick > 0.0:
			return
		_tick = 0.25
		update_views(12)
		return
	for i in active:
		var n: Npc = active[i]
		n.pending += delta
		var stride := 1 if AnimLod.FULL or Perf.brute or n.last_dist < AnimLod.NEAR_FULL_M else (2 if n.last_dist < 45.0 else 4)
		if (_frame + int(i)) % stride == 0:
			n.tick(n.pending, cam)
			n.pending = 0.0
	prof_tick_us += Time.get_ticks_usec() - tt
	_tick -= delta
	if _tick > 0.0:
		return
	_tick = 0.25
	update_views(12)


## Everything in view range gets its model now - called behind the loading screen (owner 2026-10-10: load it during
## the loading screen, not while I play), so the textures, shaders and models of the NPCs around you are ready.
func prebuild() -> void:
	if player:
		update_views(1 << 30)


var prof_build_us := 0                      # debug timing (tests/seamless_probe.gd): view builds, model parses, ticks
var prof_built := 0
var prof_parse_us := 0
var prof_parsed := 0
var prof_tick_us := 0


const PREP_GIVE_UP_MS := 3000                # a view waits at most this long for its files from the worker
const VIEW_BUILD_US := 4000                   # view building per pass: at most this long (one at least) - seamless
											# zones bring new monsters into view fast; a burst stalled whole frames


func update_views(max_build: int) -> void:
	var pp := player.global_position
	var built := 0
	var t0 := Time.get_ticks_usec()
	for st in states:
		var s: NpcState = st
		var d := Vector2(s.pos.x - pp.x, s.pos.z - pp.z).length()
		if d > VIEW_RANGE:
			s.prep_since = 0                               # (out of range: its wait starts over when it comes back)
		if d <= VIEW_RANGE and not active.has(s.index) and built < max_build 				and (built == 0 or max_build > 1000 or Time.get_ticks_usec() - t0 < VIEW_BUILD_US):
			if (s.info.has("missing") or (not s.info.has("gltf") and not s.info.has("race") and not s.info.has("object_model"))) 					and not s.info.has("effects"):
				continue                                   # nothing to show (effect-only models do get a view)
			if max_build <= 1000 and not _prepared(s):     # its files load on a worker first (_prepared) ...
				var now := Time.get_ticks_msec()
				if s.prep_since == 0:
					s.prep_since = now
				# ... but never forever (owner 2026-10-14: at a 200-bot rally half the gamebots were invisible - a druid's
				# pet with no druid): a file that failed to load, or a load that never finished, left a view unbuilt
				# for good. After PREP_GIVE_UP_MS the view is built on the spot (its files load here, a hitch at worst).
				if now - s.prep_since < PREP_GIVE_UP_MS:
					continue
			s.prep_since = 0
			var tb := Time.get_ticks_usec()
			var n := Npc.new()
			add_child(n)
			n.setup(s, self)
			prof_build_us += Time.get_ticks_usec() - tb
			prof_built += 1
			s.view = n
			active[s.index] = n
			built += 1
		elif d > FREE_RANGE and active.has(s.index):
			if active[s.index] == target:
				clear_target()
			active[s.index].queue_free()
			active.erase(s.index)
			s.view = null
	_prep_start()
	if target and is_instance_valid(target) and not target.state.alive:
		clear_target()                                     # it died: no target on a corpse (owner's old client)
	if target and is_instance_valid(target):
		PlayerData.target["con_color"] = target.con_color(PlayerData.level)
		# its health now, whoever hurt it (owner 2026-10-11: a monster a gamebot fought stayed full on the target bar)
		PlayerData.target["health"] = target.state.health
		PlayerData.target_changed.emit()


## Views prepared off the main thread (owner 2026-10-14: hitches of 150-420 ms walking near the server's bots - each
## first sighting of a monster model, a race's figure, an armour texture, a helm, a weapon or its glow parsed its files
## on the main thread; the old client loads them in the background). Before a view is built, the files it will use
## and that no cache holds yet load on a worker thread (the caches' own loaders: NpcManager._pack_model,
## Fig3Figure.pack_figure / load_texture / pack_helm, WeaponModels.pack, NifScene.pack_copy); the view is built on a
## later pass - a quarter of a second or so - when they are in. The loading screen's prebuild still builds at once.
var _prep_task: Thread                        # the preparing job (its own thread: GpuJobs.start_job)
var _prep_jobs := {}                         # what the running task loads: key -> [kind, arg]
var _prep_out := {}                          # its results (the worker's own until the task is done)
var _prep_next := {}                         # what views waiting now need, for the next task
static var _metas := {}                      # race prefix -> figure.json (for a figure's look and textures)
static var _prep_failed := {}                # jobs whose file failed to load (not queued again)


## Whether everything this spawn's view uses is loaded; queues what is not (and returns false).
func _prepared(s: NpcState) -> bool:
	var need := _prep_needs(s)
	if need.is_empty():
		return true
	for k in need:
		if not _prep_jobs.has(k):
			_prep_next[k] = need[k]
	return false


func _prep_needs(s: NpcState) -> Dictionary:
	var need := {}
	var info: Dictionary = s.info
	var spawn: Dictionary = s.spawn
	var rows: Array = data.get("equipment", {}).get(String(spawn.get("equipment", "")), [])
	if info.has("race") and info.race:
		var prefix := String(info.race)
		if not Fig3Figure._scenes.has(prefix) and not _prep_failed.has("figure:" + prefix):
			need["figure:" + prefix] = ["figure", prefix]
		if not _metas.has(prefix):
			need["meta:" + prefix] = ["meta", prefix]
		else:
			var meta: Dictionary = _metas[prefix]
			var female := prefix.ends_with("_f")
			var parts := {}
			var helm := 0
			for e in rows:
				var slot := int(e.slot)
				if slot in [22, 23, 25, 26, 27, 28]:
					parts.merge(Fig3Figure.armor_parts(int(e.model), female), true)
				elif slot == 21:
					helm = int(e.model)
			var dir := Fig3Figure.texture_dir()
			for key in Fig3Figure.texture_keys(meta, Fig3Figure.npc_look(String(spawn.id), meta), parts):
				if not Fig3Figure._tex_cache.has(dir + String(key)):
					need["tex:" + dir + String(key)] = ["tex", [dir, key]]
			if helm > 0:
				var hp := Fig3Figure.helm_path(helm, String(meta.get("prefix", prefix)))
				if hp != "" and not Fig3Figure._helm_scenes.has(hp):
					need["helm:" + hp] = ["helm", hp]
	elif info.has("object_model"):                         # a siege weapon: its objects.csv model (animated copy)
		var op := WeaponModels.anim_path_of(int(info.object_model))
		if op == "":
			op = WeaponModels.path_of(int(info.object_model))
		if op != "" and not WeaponModels._scenes.has(op):
			need["weapon:" + op] = ["weapon", op]
	elif info.has("gltf"):
		var path := String(info.gltf)
		if not _scenes.has(path):
			need["model:" + path] = ["model", path]
	for e in rows:                                         # its weapons and their glows
		var slot := int(e.slot)
		if slot in [10, 11, 12, 13]:
			var wp := WeaponModels.path_of(int(e.model))
			if wp != "" and not WeaponModels._scenes.has(wp):
				need["weapon:" + wp] = ["weapon", wp]
			var fp := WeaponModels.effect_path(int(e.get("effect", 0))) if int(e.get("effect", 0)) > 0 else ""
			if fp != "" and not NifScene._packed.has(fp):
				need["fx:" + fp] = ["fx", fp]
	for e in info.get("effects", []):                      # the model's own effects (moneffects)
		var fp := String(e.get("gltf", ""))
		if fp != "" and not NifScene._packed.has(fp):
			need["fx:" + fp] = ["fx", fp]
	return need


## The finished task's files into the caches; the next task started with what views wait for now.
func _prep_start() -> void:
	if _prep_task != null:
		if not GpuJobs.job_done(_prep_task):
			return
		GpuJobs.finish_job(_prep_task)
		_prep_task = null
		for k in _prep_out:
			var job: Array = _prep_jobs.get(k, [])
			if job.is_empty():
				continue
			var v = _prep_out[k]
			match String(job[0]):
				"model":
					if not _scenes.has(job[1]):
						_scenes[job[1]] = v
				"figure":
					if v != null and not Fig3Figure._scenes.has(job[1]):
						Fig3Figure._scenes[job[1]] = v
					elif v == null:
						_prep_failed[k] = true             # (not asked again and again: the view builds without it)
				"meta":
					_metas[job[1]] = v if v is Dictionary else {}
				"tex":
					var key: String = String(job[1][0]) + String(job[1][1])
					if not Fig3Figure._tex_cache.has(key):
						Fig3Figure._tex_cache[key] = v
				"helm":
					if not Fig3Figure._helm_scenes.has(job[1]):
						Fig3Figure._helm_scenes[job[1]] = v
				"weapon":
					if not WeaponModels._scenes.has(job[1]):
						WeaponModels._scenes[job[1]] = v
				"fx":
					if not NifScene._packed.has(job[1]):
						NifScene._packed[job[1]] = v
		_prep_jobs = {}
		_prep_out = {}
	if _prep_next.is_empty():
		return
	_prep_jobs = _prep_next
	_prep_next = {}
	_prep_out = {}
	_prep_task = GpuJobs.start_job(_prep_run.bind(_prep_jobs, _prep_out))


## The worker: each file loaded by its cache's own loader (no cache touched here).
static func _prep_run(jobs: Dictionary, out: Dictionary) -> void:
	for k in jobs:
		var job: Array = jobs[k]
		match String(job[0]):
			"model": out[k] = _pack_model(String(job[1]))
			"figure": out[k] = Fig3Figure.pack_figure(String(job[1]))
			"meta": out[k] = JSON.parse_string(FileAccess.get_file_as_string("%s/races/%s/figure.json" % [Fig3Figure.ROOT, job[1]]))
			"tex": out[k] = Fig3Figure.load_texture(String(job[1][0]), String(job[1][1]))
			"helm": out[k] = Fig3Figure.pack_helm(String(job[1]))
			"weapon": out[k] = WeaponModels.pack(String(job[1]))
			"fx": out[k] = NifScene.pack_copy(String(job[1]))


func _exit_tree() -> void:
	if _prep_task != null:
		GpuJobs.wait_job(_prep_task)                   # (running the GPU steps it may wait on)
		_prep_task = null


## Click: the NPC under the mouse (or nothing). Called by the zone viewer on a left click that wasn't a drag.
var last_pick_dist := INF                    # how far along the click ray the last pick() hit (metres from the camera)


func pick(camera: Camera3D, screen_pos: Vector2) -> Npc:
	last_pick_dist = INF
	var from := camera.project_ray_origin(screen_pos)
	var to := from + camera.project_ray_normal(screen_pos) * 120.0
	var q := PhysicsRayQueryParameters3D.create(from, to, 1 << 9)
	q.collide_with_areas = false
	# a corpse isn't clickable: the ray goes on through it, so a dead bot lying under it can still be targeted for
	# a resurrection (owner 2026-10-11)
	for _i in 4:
		var hit := get_world_3d().direct_space_state.intersect_ray(q)
		if hit.is_empty():
			break
		var c: Object = hit.collider
		if c and c.has_meta("npc"):
			var n: Npc = c.get_meta("npc")
			if is_instance_valid(n) and (n.state.alive or n.rez_target()):   # (a dead bot / companion: to resurrect)
				last_pick_dist = from.distance_to(hit.position)
				return n
			q.exclude = q.exclude + [hit.rid]
			continue
		break
	return _pick_name(camera, screen_pos)


## The overhead names are clickable too (owner 2026-10-10: "you could click on visible names to target on the old
## client"): a click on a shown name or its guild line targets that NPC; the one nearest the camera wins.
func _pick_name(camera: Camera3D, screen_pos: Vector2) -> Npc:
	var best: Npc = null
	var best_d := INF
	for i in active:
		var n: Npc = active[i]
		if not is_instance_valid(n) or not bool(n.get_meta("plate_shown", false)):
			continue
		var labels: Array = n.get_meta("plate", [])
		if labels.is_empty():
			continue
		var anchor: Vector3 = (labels[0] as Label3D).global_position
		if camera.is_position_behind(anchor):
			continue
		var p := camera.unproject_position(anchor)
		var top := p.y
		var half := 0.0
		for l in labels:                                    # name above guild line: bottom-aligned at the anchor
			var lb := l as Label3D
			var sz := lb.font.get_string_size(lb.text, HORIZONTAL_ALIGNMENT_LEFT, -1, lb.font_size)
			half = maxf(half, sz.x * 0.5)
			top = minf(top, p.y - lb.offset.y - sz.y)
		if screen_pos.x >= p.x - half - 3.0 and screen_pos.x <= p.x + half + 3.0 and screen_pos.y >= top - 3.0 				and screen_pos.y <= p.y + 3.0:
			var d := camera.global_position.distance_to(anchor)
			if d < best_d:
				best_d = d
				best = n
				last_pick_dist = d
	return best


func select(n: Npc) -> void:
	if n == null or not (n.state.alive or n.rez_target()):
		return                                             # (a corpse can't be targeted - owner's old client)
	target = n
	var s := n.spawn
	var nm := n.display_name(true)
	var ex: Dictionary = data.get("examine", {})
	var lower := String(s.name).length() > 0 and String(s.name)[0] == String(s.name)[0].to_lower()
	# period chat logs: "You target [Rory]" / "You target the [tomte scavenger]."
	PlayerData.message.emit(("You target the [%s]." % s.name) if lower else ("You target [%s]." % s.name), "system")
	var st := n.stance()
	var suffix := ""
	if s.role and ex.get("suffix", {}).has(s.role):
		suffix = String(ex.suffix[s.role]).format({"trains": str(s.get("trains", "")) if s.get("trains") else ""})
	var line := "You examine %s. %s is %s%s%s" % [nm, n.pronoun(),
			String(ex.get("stance", {}).get(st, st + " towards you")), suffix, String(ex.get("end", {}).get(st, "."))]
	PlayerData.message.emit(line[0].to_upper() + line.substr(1), "system")
	PlayerData.set_target({"name": String(s.name), "level": n.state.level, "con_color": n.con_color(PlayerData.level),
			"health": 1.0, "guild": String(s.get("guild", ""))})


signal interacted(npc: Npc)


## Right-click: talk / trade / train with the NPC (dialogue + merchant window hook here; GameUI listens).
func interact(n: Npc) -> void:
	if n.global_position.distance_to(player.global_position) > 12.0:
		PlayerData.message.emit("You are too far away to interact with %s." % n.display_name(true), "error")
		return
	# GameNPC.Interact: a faction NPC not of your realm won't deal with you while you're hostile or worse to its
	# faction (the Shrouded Isles' faction stable masters: Callisa, Nimea, Minerva, Zrrazk, Dalniver, Calvine...)
	var fs := PlayerData.faction_standing(int(n.spawn.get("faction", 0)))
	if int(n.spawn.realm) != PlayerData.realm_id() and (fs == "hostile" or fs == "aggressive"):
		var who := n.display_name(true)
		PlayerData.message.emit("%s gives you a dirty look." % (who.left(1).to_upper() + who.substr(1)), "system")
		return
	ambient(n.state, "interact", PlayerData.display_name(), true)   # GameNPC.Interact
	interacted.emit(n)


func clear_target() -> void:
	target = null
	PlayerData.set_target({})


## Tab-style: the nearest enemy (realm 0, targetable) in front of the player within range.
func nearest_enemy(max_range := 60.0) -> Npc:
	var best: Npc = null
	var best_d := max_range
	for i in active:
		var n: Npc = active[i]
		if int(n.spawn.realm) != 0 or (int(n.spawn.flags) & Npc.FLAG_CANTTARGET) or n == target or n.state.stealthed \
				or not n.state.alive:
			continue
		var d := n.global_position.distance_to(player.global_position)
		if d < best_d:
			best_d = d
			best = n
	return best
