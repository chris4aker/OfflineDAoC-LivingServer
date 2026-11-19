class_name SkySystem
extends Node3D
## Day/night, sky dome, lighting, fog and weather for a zone, driven by the client's sky_*.dat (via
## pipeline/build_sky.py -> content/sky/<sky>.json) and the client's world clock:
##   - one game day = 24 game hours; daytime (06:00-18:00) runs at WORLD_DAY_INCREMENT (24) x real time and night
##     (18:00-06:00) 25% faster, so a full day/night is 30 + 24 = 54 real minutes (server DayNightCycleTimer data)
##   - dawn / day / dusk / night colour sets for ambient light, sun light, distance fog, sky rings and cloud rings,
##     each with a clear and a stormy version, blended by time of day and storm level
##   - storms from [storm_system]: every `period` minutes a `percent_chance` roll starts one; rain from [rain]
## Gap-fills (mine, not in the files): how the four colour sets blend across the hours, light strength scaling to
## Godot units, rain drawn as thin streak particles, storm ramp time.

const DAY_INCREMENT := 24.0            # world_day_increment in the 1.65 world's server properties
var day_increment := DAY_INCREMENT     # the clock's speed: the server's own (its Time packet) when it sends one
const NIGHT_FACTOR := 1.25             # night runs 25% faster than day
const LIGHT_SCALE := 1.75              # client light amounts (0..1) to Godot light energy
const AMBIENT_SCALE := 1.4
const UNITS := 0.025                   # DAoC units to metres
# when each colour set is at full strength (game hours); between keys the neighbouring sets cross-fade
const PHASE_KEYS := [[0.0, "night"], [4.5, "night"], [6.0, "dawn"], [7.5, "day"], [16.5, "day"], [18.0, "dusk"],
		[19.5, "night"], [24.0, "night"]]

var zone: ZoneLoader
var region: RegionWorld                # seamless zones: every loaded zone gets the night lights and water light
var sky_name := ""
var data: Dictionary
var game_seconds := 10.0 * 3600.0      # time of day in game seconds
var time_scale := 1.0                  # test speed-up on top of the client's rates
var storm := 0.0                       # current storm level 0..1
var storm_target := 0.0
var storm_forced := -1.0               # tests: -1 = let the storm system decide
var server_driven := false             # the server's weather sets storm_target (ZoneViewer): no storms of our own
var night_level := 0.0                 # 0 day .. 1 night, for lamps and glows (zone lights)
var environment: Environment
var sun: DirectionalLight3D

var _sky_mat: ShaderMaterial
var _tex := {}
var _storm_clock := 0.0
var _storm_seed := RandomNumberGenerator.new()
var _rain: GPUParticles3D
var _rain_ceiling := 10.0              # metres above the player that drops start (sky_*.dat [rain] ceiling)
var _follow: Node3D
var dungeon := false                   # zone.json "dungeon": no sky, sun, day/night or weather (see _setup_dungeon)


## Zone-only sun shadow splits (owner 2026-10-15: the Grove of Domnann's ground shadows flickered under the canopy as
## you moved - "without modifying all shadows across the whole game"). Measured there at a running pace
## (tests/shadow_flicker_probe.gd, the shadow term alone): the default splits 0.1 / 0.2 / 0.5 jumped 25.9 of 7200
## sampled pixels a step, 0.06 / 0.15 / 0.4 jumped 1.6 (edges about a fifth softer). Every other zone keeps Godot's.
const ZONE_SHADOW_SPLITS := {181: [0.06, 0.15, 0.4]}
const DEFAULT_SHADOW_SPLITS := [0.1, 0.2, 0.5]
## ... and a bigger shadow map there (owner 2026-10-15, after the splits: "there's still shadow flickering" under the
## canopy): the project's 2048 atlas holds the 4 cascades at 1024 each, coarser than the leaf cut-outs whose shadows
## shimmer as the cascades follow you; 4096 doubles every cascade's texels in that zone alone.
const ZONE_SHADOW_ATLAS := {181: 4096}
var _shadow_zone := -1


## The zone you are in now (ZoneViewer, as it changes): its shadow splits on the sun, and its shadow map size.
func set_shadow_zone(zone_id: int) -> void:
	if zone_id == _shadow_zone or sun == null:
		return
	_shadow_zone = zone_id
	var sp: Array = ZONE_SHADOW_SPLITS.get(zone_id, DEFAULT_SHADOW_SPLITS)
	sun.directional_shadow_split_1 = float(sp[0])
	sun.directional_shadow_split_2 = float(sp[1])
	sun.directional_shadow_split_3 = float(sp[2])
	var atlas := int(ZONE_SHADOW_ATLAS.get(zone_id, ProjectSettings.get_setting("rendering/lights_and_shadows/directional_shadow/size", 2048)))
	RenderingServer.directional_shadow_atlas_set_size(atlas, bool(ProjectSettings.get_setting(
			"rendering/lights_and_shadows/directional_shadow/16_bits", true)))


func setup(z: ZoneLoader, follow: Node3D, texture_set := "x4plus") -> void:
	zone = z
	_follow = follow
	var skies: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://content/sky/zone_skies.json"))
	sky_name = String(skies.get(str(int(z.info.id)), "sky_default"))
	data = JSON.parse_string(FileAccess.get_file_as_string("res://content/sky/%s.json" % sky_name))
	_storm_seed.seed = int(z.info.id) * 7919
	_texture_set = texture_set

	var shader: Shader = load("res://game/sky.gdshader")
	_sky_mat = ShaderMaterial.new()
	_sky_mat.shader = shader
	_apply_sky_data()
	_setup_rest(z)


var _texture_set := "x4plus"


## Seamless zones: you crossed into another zone of the region - its fog, and its sky if the client gives it another.
func set_zone(z: ZoneLoader) -> void:
	zone = z
	if dungeon:
		return
	apply_view()
	var skies: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://content/sky/zone_skies.json"))
	var nm := String(skies.get(str(int(z.info.id)), "sky_default"))
	if nm != sky_name:
		sky_name = nm
		data = JSON.parse_string(FileAccess.get_file_as_string("res://content/sky/%s.json" % sky_name))
		_apply_sky_data()
	_update(0.0)


## The region's fog (the client's clip distance) times Options > VIDEO view distance; dungeons keep theirs.
## The original client reads sector.dat's [fog] once per region, from the region's lowest-numbered zone (zone-table
## slot 0: game.dll 1.64 0x466c0f, the table filled in zone order at 0x454f9d) and never another zone's (owner
## 2026-10-29: Avalon Isle's own clip=1000, 25 m, made its fog "too thick" - the client used Isle of Glass's 400 m).
func apply_view() -> void:
	if dungeon or zone == null or environment == null:
		return
	var clip := region_fog_clip(zone) * ZoneLoader.view_mult
	environment.fog_depth_begin = clip * 0.3
	environment.fog_depth_end = clip * 1.6


static var _region_clip := {}                # region -> the fog clip (m) of its lowest-numbered built zone
static var _zone_region := {}                # zone id -> its region
static var _fog_read := false
static var _fog_mutex := Mutex.new()          # (zone workers ask too: ZoneLoader's world-object draw range)


## A zone's fog distance as the original drew it: its region's first zone's (its own when that one isn't built).
static func region_fog_clip(z: ZoneLoader) -> float:
	_fog_mutex.lock()
	if not _fog_read:
		_fog_read = true
		var index = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json")) 				if FileAccess.file_exists("res://content/zones/zones_index.json") else null
		var first := {}
		if index is Dictionary:
			for k in index:
				var r := int((index[k] as Dictionary).get("region", -1))
				_zone_region[int(k)] = r
				if not first.has(r) or int(k) < int(first[r]):
					first[r] = int(k)
		for r in first:
			var path := "res://content/zones/zone%03d/zone.json" % int(first[r])
			if FileAccess.file_exists(path):
				var zj = JSON.parse_string(FileAccess.get_file_as_string(path))
				if zj is Dictionary and zj.get("fog") is Dictionary:
					_region_clip[r] = float((zj.fog as Dictionary).get("clip_m", 400.0))
	var own := float(z.info.fog.get("clip_m", 400.0))
	var clip := float(_region_clip.get(int(_zone_region.get(int(z.info.id), -1)), own))
	_fog_mutex.unlock()
	return clip


func _zones() -> Array:
	if region:
		return region.zones.values()
	return [zone] if zone else []


func _apply_sky_data() -> void:
	var texture_set := _texture_set
	var main: Dictionary = data.get("main", {})
	for pair in [["cloud_top", "cloud_top_texture"], ["cloud_bot", "cloud_bot_texture"], ["cloud_top2", "cloud_top_texture2"],
			["cloud_bot2", "cloud_bot_texture2"], ["sun_disk_tex", "sun_texture"], ["sun_glow_tex", "sunglow_texture"],
			["sun_flare_tex", "sunflare_texture"], ["moon_tex", "moon_texture"], ["moonback_tex", "moonback_texture"],
			["star_tex", "star_texture"]]:
		var file := String(main.get(pair[1], ""))
		if file != "":
			_sky_mat.set_shader_parameter(pair[0], _texture(file, texture_set))
	_sky_mat.set_shader_parameter("cloud_tiles", Vector4(_f(main, "cloud_top_tile", 4), _f(main, "cloud_bot_tile", 5),
			_f(main, "cloud_top_tile2", 4), _f(main, "cloud_bot_tile2", 3)))
	_sky_mat.set_shader_parameter("cloud_speeds", Vector4(_f(main, "cloud_top_speed", 4), _f(main, "cloud_bot_speed", 6),
			_f(main, "cloud_top_speed2", 5), _f(main, "cloud_bot_speed2", 9)))
	_sky_mat.set_shader_parameter("sizes", Vector3(_f(main, "sun_scale", 15), _f(main, "sunglow_scale", 175), _f(main, "sunflare_scale", 175)))
	_sky_mat.set_shader_parameter("moon_size", _f(main, "moon_scale", 15))
	_sky_mat.set_shader_parameter("star_scale", _f(main, "star_scale", 2.5))
	_sky_mat.set_shader_parameter("star_density", _f(main, "max_stars", 700))
	_sky_mat.set_shader_parameter("moon_color", _rgba(main.get("moon_color", [255, 247, 191, 255])))
	_sky_mat.set_shader_parameter("moonback_color", _rgba(main.get("moonback_color", [16, 51, 89, 255])))


func _setup_rest(z: ZoneLoader) -> void:
	var sky := Sky.new()
	sky.sky_material = _sky_mat
	sky.process_mode = Sky.PROCESS_MODE_REALTIME
	environment = Environment.new()
	environment.background_mode = Environment.BG_SKY
	environment.sky = sky
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	environment.tonemap_white = 6.0
	environment.ssao_enabled = true
	environment.fog_enabled = true
	environment.fog_mode = Environment.FOG_MODE_DEPTH
	apply_view()
	environment.fog_sky_affect = 0.2
	var world := WorldEnvironment.new()
	world.environment = environment
	add_child(world)

	sun = DirectionalLight3D.new()
	sun.shadow_enabled = true
	# soft, low-detail shadow maps (no ray tracing): trees cast dappled blobs rather than crisp leaf outlines
	sun.directional_shadow_max_distance = 140.0
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	sun.directional_shadow_blend_splits = true
	sun.shadow_blur = 2.5
	sun.light_angular_distance = 1.2
	add_child(sun)
	_build_rain()
	dungeon = bool(z.info.get("dungeon", false))
	if dungeon:
		_setup_dungeon()
		return
	_update(0.0)


## A dungeon (owner 2026-10-10: Darkness Falls "has no proper lighting, it's missing all its atmosphere" - it was lit
## like a field at noon under the realm sky). The client draws a dungeon with no sky and no sun: the pieces' own baked
## light (vertex colours, dark maps), the zone's lights.csv lights, and the dungeon's fog from its sector.dat - Darkness
## Falls / Dodens Gruva / Tur Suil purple at amount 85, 125 m; the classic dungeons a light blue-grey haze (15, 400 m).
## Mine (no client numbers): the ambient level, how fog amount maps to the fog's reach, the lights always full.
func _setup_dungeon() -> void:
	sky_name = "dungeon"
	sun.visible = false
	if _rain:
		_rain.emitting = false
		_rain.visible = false
	var fc: Array = zone.info.fog.get("color", [0, 0, 0])
	var fog_color := Color(float(fc[0]), float(fc[1]), float(fc[2]))
	var amount := clampf(float(zone.info.fog.get("amount", 0)) / 100.0, 0.0, 1.0)
	var clip := float(zone.info.fog.get("clip_m", 400.0))
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = fog_color.darkened(0.5)
	environment.reflected_light_source = Environment.REFLECTION_SOURCE_DISABLED
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.82, 0.8, 0.86).lerp(fog_color, 0.12 + 0.2 * amount)
	environment.ambient_light_energy = 0.75
	environment.fog_light_color = fog_color
	environment.fog_light_energy = 1.0
	environment.fog_depth_begin = clip * 0.05
	environment.fog_depth_end = clip * (2.0 - 1.5 * amount)     # amount 85: 90 m to full fog; 15: ~710 m (a haze)
	environment.fog_depth_curve = 1.0
	if zone:
		zone.set_light_level(1.0)                            # the client's lights burn all day underground


func set_hour(h: float) -> void:
	game_seconds = fposmod(h, 24.0) * 3600.0
	if not dungeon:
		_update(0.0)


func hour() -> float:
	return game_seconds / 3600.0


## Night for the old server's IsNightTime: the last and first quarter of the day (18:00 - 6:00).
func is_night() -> bool:
	return hour() < 6.0 or hour() >= 18.0


func clock_text() -> String:
	var h := int(hour())
	var m := int(fposmod(game_seconds / 60.0, 60.0))
	return "%02d:%02d" % [h, m]


func _process(delta: float) -> void:
	if dungeon:                                              # the clock still runs (camp timers, night NPCs)
		game_seconds = fposmod(game_seconds + delta * day_increment * time_scale, 86400.0)
		return
	var h := hour()
	var rate := day_increment * (NIGHT_FACTOR if (h >= 18.0 or h < 6.0) else 1.0)
	game_seconds = fposmod(game_seconds + delta * rate * time_scale, 86400.0)
	_weather(delta * time_scale)
	_update(delta)


func _weather(delta: float) -> void:
	var ss: Dictionary = data.get("storm_system", {})
	var period := _f(ss, "period", 5) * 60.0
	_storm_clock += delta
	if _storm_clock >= period and not server_driven:
		_storm_clock = 0.0
		if _storm_seed.randf() * 100.0 < _f(ss, "percent_chance", 25):
			storm_target = _storm_seed.randf_range(_f(ss, "min_intensity", 0), _f(ss, "max_intensity", 100)) / 100.0
		else:
			storm_target = 0.0
	var target := storm_forced if storm_forced >= 0.0 else storm_target
	storm = move_toward(storm, target, delta / 45.0)          # storms build and clear over ~45 s


func _phase_weights(h: float) -> Dictionary:
	var w := {"dawn": 0.0, "day": 0.0, "dusk": 0.0, "night": 0.0}
	for i in PHASE_KEYS.size() - 1:
		var a: Array = PHASE_KEYS[i]
		var b: Array = PHASE_KEYS[i + 1]
		if h >= a[0] and h <= b[0]:
			var t := (h - float(a[0])) / maxf(float(b[0]) - float(a[0]), 0.001)
			t = t * t * (3.0 - 2.0 * t)
			w[a[1]] += 1.0 - t
			w[b[1]] += t
			break
	return w


func _blend(section: String, key: String, w: Dictionary) -> Variant:
	# clear and stormy versions, each weighted over dawn/day/dusk/night
	var out = null
	for weather in [["clear", 1.0 - storm], ["stormy", storm]]:
		var sec: Dictionary = data.get("%s_%s" % [section, weather[0]], data.get("%s_clear" % section, {}))
		for phase in w:
			var weight: float = w[phase] * weather[1]
			if weight <= 0.0:
				continue
			var v = sec.get("%s_%s" % [phase, key], null)
			if v == null:
				continue
			if v is Array:
				var c := _rgba(v)
				out = c * weight if out == null else out + c * weight
			else:
				out = float(v) * weight if out == null else out + float(v) * weight
	return out


func _update(_delta: float) -> void:
	var h := hour()
	var w := _phase_weights(h)
	night_level = clampf(float(w.night) + 0.5 * (float(w.dawn) + float(w.dusk)) + storm * 0.3, 0.0, 1.0)
	for zl in _zones():
		(zl as ZoneLoader).set_light_level(night_level)
	# sun path: rises in the east at 06:00, highest at noon tilted south by the ecliptic angle, sets west at 18:00
	var tilt := deg_to_rad(_f(data.get("main", {}), "ecliptic_angle", 15))
	var theta := (h - 6.0) / 12.0 * PI
	var sun_dir := Vector3(cos(theta), sin(theta) * cos(tilt), sin(theta) * sin(tilt)).normalized()
	var moon_dir := -sun_dir
	_sky_mat.set_shader_parameter("sun_dir", sun_dir)
	_sky_mat.set_shader_parameter("moon_dir", moon_dir)

	for ring in ["zenith", "nadir"]:
		var c = _blend("canopy_color", ring, w)
		if c != null:
			_sky_mat.set_shader_parameter("canopy_" + ring, c)
	var cz = _blend("cloud_color", "zenith", w)
	if cz != null:
		_sky_mat.set_shader_parameter("cloud_zenith", cz)
	for side in ["east", "west"]:
		var canopy: Array = []
		var cloud: Array = []
		for i in range(1, 5):
			var c1 = _blend("canopy_color", "%s%d" % [side, i], w)
			canopy.append(c1 if c1 != null else Color.BLACK)
			var c2 = _blend("cloud_color", "%s%d" % [side, i], w)
			cloud.append(c2 if c2 != null else Color(1, 1, 1, 0))
		_sky_mat.set_shader_parameter("canopy_" + side, canopy)
		_sky_mat.set_shader_parameter("cloud_" + side, cloud)

	# sun sprites: dawn/dusk colours, noon colours in the day, storm variants by storm level
	var main: Dictionary = data.get("main", {})
	var noon: float = w.day
	for pair in [["sun_disk_color", "sundisk_color"], ["sun_glow_color", "sunglow_color"], ["sun_flare_color", "sunflare_color"]]:
		var low := _rgba(main.get(pair[1], [255, 255, 255, 0])).lerp(_rgba(main.get(pair[1] + "_storm", [0, 0, 0, 0])), storm)
		var high := _rgba(main.get(pair[1] + "_noon", [255, 255, 255, 0])).lerp(_rgba(main.get(pair[1] + "_noon_storm", [0, 0, 0, 0])), storm)
		var c := low.lerp(high, noon)
		c.a *= clampf(sun_dir.y * 6.0 + 0.3, 0.0, 1.0)      # fade as it sinks below the horizon
		_sky_mat.set_shader_parameter(pair[0], c)
	_sky_mat.set_shader_parameter("star_visibility", float(w.night) * (1.0 - storm))
	_sky_mat.set_shader_parameter("storm", storm)
	_sky_mat.set_shader_parameter("cloud_time", Time.get_ticks_msec() / 1000.0)

	# lights and fog from the file
	var amb = _blend("lights_and_fog", "ambient_light", w)
	var amb_amount = _blend("lights_and_fog", "ambient_light_amount", w)
	var dyn = _blend("lights_and_fog", "dynamic_light", w)
	var dyn_amount = _blend("lights_and_fog", "dynamic_light_amount", w)
	var fog = _blend("lights_and_fog", "distance_fog", w)
	if amb != null:
		environment.ambient_light_color = amb
		environment.ambient_light_energy = float(amb_amount if amb_amount != null else 0.5) * AMBIENT_SCALE
	if fog != null:
		environment.fog_light_color = fog
	var light_dir := sun_dir if sun_dir.y > -0.05 else moon_dir     # at night the moon lights the world
	var up := light_dir if light_dir.y > 0.02 else Vector3(light_dir.x, 0.02, light_dir.z).normalized()
	sun.look_at_from_position(Vector3.ZERO, -up, Vector3.UP if absf(up.y) < 0.99 else Vector3.FORWARD)
	if dyn != null:
		sun.light_color = dyn
	var horizon_fade := clampf(absf(light_dir.y) * 5.0, 0.0, 1.0)
	sun.light_energy = float(dyn_amount if dyn_amount != null else 0.6) * LIGHT_SCALE * horizon_fade
	if fog != null:
		for zl in _zones():
			(zl as ZoneLoader).set_water_light(up, sun.light_color, Color(fog).lightened(0.15))
	if _rain:
		_rain.amount_ratio = clampf(storm * 1.2 - 0.15, 0.0, 1.0)
		_rain.emitting = _rain.amount_ratio > 0.01
		if _follow:
			# emit from the client's rain ceiling above the player (following the feet put the emitter at ground
			# level, so every drop fell straight into the terrain and storms showed no rain)
			_rain.global_position = _follow.global_position + Vector3(0.0, _rain_ceiling, 0.0)


func _build_rain() -> void:
	var r: Dictionary = data.get("rain", {})
	_rain = GPUParticles3D.new()
	_rain.amount = int(_f(r, "max_drops", 1000))
	var speed := _f(r, "velocity", 500) * UNITS
	var ceiling := _f(r, "ceiling", 400) * UNITS
	var floor_y := _f(r, "floor", -200) * UNITS
	var outer := _f(r, "outer_radius", 800) * UNITS
	_rain_ceiling = ceiling
	_rain.lifetime = (ceiling - floor_y) / maxf(speed, 0.1)
	# the emitter sits at the ceiling and drops fall from there to the floor below the player
	_rain.visibility_aabb = AABB(Vector3(-outer, -(ceiling - floor_y) - 1.0, -outer), Vector3(outer * 2, ceiling - floor_y + 2.0, outer * 2))
	var pm := ParticleProcessMaterial.new()
	pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	pm.emission_box_extents = Vector3(outer, 0.1, outer)
	pm.direction = Vector3(0, -1, 0)
	pm.spread = 2.0
	pm.initial_velocity_min = speed
	pm.initial_velocity_max = speed
	pm.gravity = Vector3.ZERO
	_rain.process_material = pm
	_rain.position.y = ceiling
	var quad := QuadMesh.new()
	quad.size = Vector2(0.02, _f(r, "drop_length", 20) * UNITS)
	var mat := StandardMaterial3D.new()
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_FIXED_Y
	mat.billboard_keep_scale = true
	# streak colour: top to bottom colours from the file (alpha fades in toward the bottom of each drop)
	var grad := Gradient.new()
	grad.set_color(0, _rgba(r.get("top_color", [160, 160, 160, 0])))
	grad.set_color(1, _rgba(r.get("bot_color", [160, 160, 160, 64])))
	var gt := GradientTexture2D.new()
	gt.gradient = grad
	gt.fill_from = Vector2(0, 0)
	gt.fill_to = Vector2(0, 1)
	gt.width = 4
	gt.height = 32
	mat.albedo_texture = gt
	mat.albedo_color = Color(1, 1, 1, 1)
	quad.material = mat
	_rain.draw_pass_1 = quad
	_rain.emitting = false
	add_child(_rain)


func _texture(file: String, texture_set: String) -> Texture2D:
	var key := file.to_lower().get_basename()
	if _tex.has(key):
		return _tex[key]
	var path := "res://content/sky/textures/%s/%s.png" % [texture_set, key]
	if not FileAccess.file_exists(path):
		path = "res://content/sky/textures/original/%s.png" % key
	var img := Image.load_from_file(path)
	var t: Texture2D = null
	if img:
		img.generate_mipmaps()
		t = ImageTexture.create_from_image(img)
	_tex[key] = t
	return t


static func _f(d: Dictionary, k: String, fallback: float) -> float:
	var v = d.get(k, fallback)
	return float(v) if not (v is Array) else float(v[0])


static func _rgba(v) -> Color:
	if v is Array:
		var a: float = v[3] if v.size() > 3 else 255.0
		return Color(v[0] / 255.0, v[1] / 255.0, v[2] / 255.0, a / 255.0)
	return Color.WHITE
