extends Node
## Autoload "Graphics": the render-quality options of Options > VIDEO (owner 2026-10-13: "Add more graphical options
## with reset to defaults (what I have now) including view distance (let it be cranked way up). Intelligent presets
## for different spec pcs ... shadow quality, ssao, the shadow values 0-5"). Only what the engine and the client's
## own assets support as they are - nothing new drawn. Saved in the settings file's [graphics] section; applied at
## start-up (the viewport ones) and when the world loads, and live as they change.
##   DEFAULTS = what the owner played with until then (project.godot + SkySystem + ZoneLoader + GroundClutter):
##   view distance 1x (the client's fog per zone), sun shadows on, 2048 shadow map over 140 m, soft shadow filter 5,
##   SSAO high, MSAA 2x + FXAA, 4x anisotropic, grass out to 72 m at full density, no bloom, no SSIL, render 100%.
##   View distance stretches the zone fog, the far clip, every object's draw range and how far around you the
##   region's zones are loaded - high values load more zones (more video memory).

signal changed

const DEFAULTS := {
	"view_distance": 1.0,          # x the client's fog distance, object draw ranges and zone streaming
	"shadows": true,
	"shadow_size": 2048,           # the sun's shadow map (px)
	"shadow_distance": 140.0,      # metres the sun's shadows reach
	"shadow_filter": 5,            # soft shadow filter 0 hard .. 5 ultra (RenderingServer)
	"ssao": 3,                     # -1 off, 0 very low .. 4 ultra
	"ssil": false,                 # screen-space indirect light
	"glow": false,                 # bloom on bright spell effects and lights
	"tonemap": 0,                  # 0 filmic (the client's look until now), 1 AgX (Godot 4.4+: richer highlights)
	"msaa": 2,                     # 0 / 2 / 4 / 8 samples
	"screen_aa": 1,                # 0 off, 1 FXAA, 2 SMAA
	"taa": false,
	"aniso": 4,                    # 1 (off) / 2 / 4 / 8 / 16
	"render_scale": 1.0,           # 1 = native; above 1 = supersampling (no upscaling, owner)
	"grass_distance": 72.0,        # metres
	"grass_density": 1.0,
}
const PRESET_NAMES := ["low", "medium", "high", "ultra", "extreme"]
# HIGH is the defaults (the owner's own game until now)
const PRESETS := {
	"low": {"view_distance": 0.8, "shadows": true, "shadow_size": 1024, "shadow_distance": 80.0, "shadow_filter": 1,
			"ssao": -1, "ssil": false, "glow": false, "msaa": 0, "screen_aa": 1, "taa": false, "aniso": 2,
			"render_scale": 1.0, "grass_distance": 45.0, "grass_density": 0.5},
	"medium": {"view_distance": 1.0, "shadows": true, "shadow_size": 2048, "shadow_distance": 110.0, "shadow_filter": 3,
			"ssao": 1, "ssil": false, "glow": false, "msaa": 0, "screen_aa": 1, "taa": false, "aniso": 4,
			"render_scale": 1.0, "grass_distance": 60.0, "grass_density": 0.75},
	"high": DEFAULTS,
	"ultra": {"view_distance": 1.6, "shadows": true, "shadow_size": 4096, "shadow_distance": 220.0, "shadow_filter": 5,
			"ssao": 4, "ssil": false, "glow": false, "msaa": 4, "screen_aa": 1, "taa": false, "aniso": 16,
			"render_scale": 1.0, "grass_distance": 110.0, "grass_density": 1.0},
	"extreme": {"view_distance": 2.5, "shadows": true, "shadow_size": 8192, "shadow_distance": 400.0, "shadow_filter": 5,
			"ssao": 4, "ssil": true, "glow": false, "msaa": 4, "screen_aa": 2, "taa": false, "aniso": 16,
			"render_scale": 1.0, "grass_distance": 160.0, "grass_density": 1.0},
}
const VIEW_MAX := 4.0                    # the slider's top: 4x the client's fog and draw ranges

var values := DEFAULTS.duplicate()


func _ready() -> void:
	for a in OS.get_cmdline_user_args():                 # test runs keep the defaults (their own settings file)
		if a.trim_prefix("--").split("=")[0] in BootSettings.TEST_FLAGS:
			return
	load_from("user://settings.cfg")
	apply_viewport.call_deferred()


func load_from(path: String) -> void:
	values = DEFAULTS.duplicate()
	var cfg := ConfigFile.new()
	if cfg.load(path) != OK:
		return
	for k in DEFAULTS:
		if cfg.has_section_key("graphics", k):
			values[k] = cfg.get_value("graphics", k)
	_statics()


## What the world's builders read as they build (zones on worker threads, grass chunks): plain statics.
func _statics() -> void:
	ZoneLoader.view_mult = view()
	GroundClutter.radius_m = clampf(float(value("grass_distance")), 20.0, 250.0)
	GroundClutter.density_mult = clampf(float(value("grass_density")), 0.0, 1.0)


func value(key: String) -> Variant:
	return values.get(key, DEFAULTS.get(key))


func view() -> float:
	return clampf(float(value("view_distance")), 0.5, VIEW_MAX)


## One setting changed: saved, applied, announced.
func set_value(key: String, v: Variant) -> void:
	values[key] = v
	_save()
	apply_all()


## A whole preset ("low" .. "extreme", or "defaults").
func use_preset(name: String) -> void:
	values = (DEFAULTS if name == "defaults" else PRESETS.get(name, DEFAULTS)).duplicate()
	_save()
	apply_all()


## The preset the current values are, or "" (custom).
func current_preset() -> String:
	for n in PRESET_NAMES:
		var p: Dictionary = PRESETS[n]
		var same := true
		for k in DEFAULTS:
			if not _same(values.get(k), p.get(k, DEFAULTS[k])):
				same = false
				break
		if same:
			return n
	return ""


static func _same(a: Variant, b: Variant) -> bool:
	if (a is float or a is int) and (b is float or b is int):
		return absf(float(a) - float(b)) < 0.001
	return a == b


func _save() -> void:
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)                         # (re-read: other parts of the UI write the file too)
	for k in values:
		cfg.set_value("graphics", k, values[k])
	cfg.save(UiArt.settings_path)


# ---------------------------------------------------------------- the recommendation

## A preset for this PC from its graphics card (the name the driver gives - the engine can't measure a card's
## speed or memory before it plays): [preset, why].
func recommended() -> Array:
	var gpu := RenderingServer.get_video_adapter_name()
	var g := gpu.to_lower()
	var tier := "medium"
	var num := 0
	var m := RegEx.create_from_string("(\\d{3,4})").search(g)
	if m:
		num = int(m.get_string(1))
	if g.contains("intel") and not g.contains("arc") or g.contains("uhd") or g.contains("iris") \
			or (g.contains("radeon") and (g.contains("graphics") and num == 0 or g.contains("vega"))):
		tier = "low"                                      # integrated graphics
	elif g.contains("rtx"):
		# RTX 20xx / 30xx 50-60 class: high; 3070+ / 40xx / 50xx: ultra; 4080+ / 5080+: extreme
		if num >= 5080 or (num >= 4080 and num < 5000):
			tier = "extreme"
		elif num >= 4060 or (num >= 3070 and num < 4000):
			tier = "ultra"
		else:
			tier = "high"
	elif g.contains("gtx") or g.contains("quadro"):
		tier = "medium" if num >= 1060 or num < 1000 else "low"
	elif g.contains("radeon") or g.contains(" rx "):
		# RX 9070 / 7900 / 7800: extreme / ultra; 6700+: ultra; 6600 / 7600 / 5700: high; older: medium
		if num >= 9070 or (num >= 7900 and num < 8000):
			tier = "extreme"
		elif (num >= 6700 and num < 7000) or (num >= 7700 and num < 8000) or (num >= 9000 and num < 9070):
			tier = "ultra"
		elif (num >= 5700 and num < 6000) or (num >= 6600 and num < 6700) or (num >= 7600 and num < 7700):
			tier = "high"
		else:
			tier = "medium"
	elif g.contains("arc"):
		tier = "high" if num >= 700 else "medium"
	return [tier, gpu]


# ---------------------------------------------------------------- applying

func apply_all() -> void:
	_statics()
	apply_viewport()
	apply_scene(get_tree().current_scene)
	changed.emit()


## The window's own: anti-aliasing, texture filtering, render scale, the shadow map and its filter.
func apply_viewport() -> void:
	var vp := get_viewport()
	if vp == null:
		return
	vp.msaa_3d = {0: Viewport.MSAA_DISABLED, 2: Viewport.MSAA_2X, 4: Viewport.MSAA_4X, 8: Viewport.MSAA_8X}.get(
			int(value("msaa")), Viewport.MSAA_2X)
	vp.screen_space_aa = clampi(int(value("screen_aa")), 0, 2) as Viewport.ScreenSpaceAA
	vp.use_taa = bool(value("taa"))
	if "anisotropic_filtering_level" in vp:
		vp.set("anisotropic_filtering_level", {1: 0, 2: 1, 4: 2, 8: 3, 16: 4}.get(int(value("aniso")), 2))
	vp.scaling_3d_mode = Viewport.SCALING_3D_MODE_BILINEAR
	vp.scaling_3d_scale = clampf(float(value("render_scale")), 1.0, 2.0)
	RenderingServer.directional_shadow_atlas_set_size(int(value("shadow_size")), true)
	var f := clampi(int(value("shadow_filter")), 0, 5)
	RenderingServer.directional_soft_shadow_filter_set_quality(f as RenderingServer.ShadowQuality)
	RenderingServer.positional_soft_shadow_filter_set_quality(f as RenderingServer.ShadowQuality)
	var q := int(value("ssao"))
	RenderingServer.environment_set_ssao_quality(clampi(q, 0, 4) as RenderingServer.EnvironmentSSAOQuality, false,
			0.5, 2, 50.0, 300.0)


## The world's: sky (fog, sun shadows, SSAO / SSIL / glow), the camera's far clip, the zones' draw ranges and
## streaming reach, the grass. Called by the zone viewer once the world is up, and on every change.
func apply_scene(scene: Node) -> void:
	if scene == null or scene.get("sky") == null:
		return
	var sky: SkySystem = scene.get("sky")
	if sky and sky.environment:
		var env := sky.environment
		env.ssao_enabled = int(value("ssao")) >= 0
		env.ssil_enabled = bool(value("ssil"))
		env.glow_enabled = bool(value("glow"))
		env.tonemap_mode = Environment.TONE_MAPPER_AGX if int(value("tonemap")) == 1 else Environment.TONE_MAPPER_FILMIC
		if env.glow_enabled:
			env.glow_intensity = 0.6
			env.glow_bloom = 0.05
			env.glow_hdr_threshold = 1.2
		sky.apply_view()
	if sky and sky.sun:
		sky.sun.shadow_enabled = bool(value("shadows"))
		sky.sun.directional_shadow_max_distance = float(value("shadow_distance"))
	var player: Node = scene.get("player")
	if player and player.get("camera"):
		(player.camera as Camera3D).far = far_clip(sky)
	var region: RegionWorld = scene.get("region")
	var zones: Array = region.zones.values() if region else ([scene.get("zone")] if scene.get("zone") else [])
	for z in zones:
		if z is ZoneLoader:
			(z as ZoneLoader).apply_view(view())
	var clutters: Dictionary = scene.get("clutters") if scene.get("clutters") is Dictionary else {}
	for c in clutters.values():
		if is_instance_valid(c):
			(c as GroundClutter).apply_settings()


## The camera's far clip: past the fog's end, at least the old 1500 m.
func far_clip(sky: SkySystem) -> float:
	var fog_end := sky.environment.fog_depth_end if sky and sky.environment else 0.0
	return maxf(1500.0 * view(), fog_end * 1.15)
