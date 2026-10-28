class_name Npc
extends Node3D
## The VIEW of one spawn (NpcState holds its life in the always-running world simulation): its model (a Catacombs
## figure for player races, the classic monster model otherwise - content/monsters), its name plate and its click
## shape. Created while the spawn is within view range, freed beyond; the state carries on either way.
## Its overhead name is drawn by NamePlates (screen space, the client's font, measured range).

# con colours from the client's own name-font palette (fonts/14font.bmp rows); purple isn't in it
const CON_COLORS := {-3: Color8(130, 129, 128), -2: Color8(135, 225, 113), -1: Color8(85, 93, 214),
		0: Color8(197, 193, 0), 1: Color8(254, 131, 49), 2: Color8(245, 0, 0), 3: Color(0.78, 0.35, 1.0)}
const FLAG_GHOST := 0x01
const FLAG_DONTSHOWNAME := 0x04
const FLAG_CANTTARGET := 0x08
const FLAG_PEACE := 0x10

var state: NpcState


func is_alive() -> bool:
	return state == null or state.alive
var spawn := {}
var info := {}                              # monsters.json entry for its model
var manager: Node
var model: Node3D
var anim: AnimationPlayer
var height := 1.8                           # metres, for the name plate and the click shape
var _shown_mode := -1
var _shown_running := false                 # walk or run clip shown (a patrol's legs change speed, owner 2026-10-10)
var _shown_swim := false                    # the swim clip shown (in deep water, NpcState.swimming)
var _pick: StaticBody3D
var sounds: CharacterSounds


func setup(st: NpcState, mgr: Node) -> void:
	state = st
	spawn = st.spawn
	info = st.info
	set_process(false)                             # ticked by the manager
	manager = mgr
	name = "npc_%s" % String(spawn.id).left(8)
	position = st.pos
	rotation.y = st.yaw
	var t0 := Time.get_ticks_usec()
	_build_model()
	var t1 := Time.get_ticks_usec()
	if not (int(spawn.flags) & FLAG_CANTTARGET):
		_build_pick()
	prof_build["model"] = int(prof_build.get("model", 0)) + t1 - t0
	prof_build["pick"] = int(prof_build.get("pick", 0)) + Time.get_ticks_usec() - t1
	if AnimLod.threaded:                                  # steps itself, on a worker thread (AnimLod.run_threaded)
		_phase = int(get_instance_id() % 4)
		AnimLod.run_threaded(self)
		set_process(true)


var _phase := 0


## A parked view (ServerWorld._park: its living left your view a moment) taken back by the same living's new state:
## the model, gear and plates stay; what it shows follows the new state from the next tick.
func rebind(st: NpcState) -> void:
	state = st
	spawn = st.spawn
	info = st.info
	position = st.pos
	rotation.y = st.yaw
	casting_spell = false
	_bow_hold = false
	_action_left = 0.0
	_shown_mode = -1
	pending = 0.0
	process_mode = Node.PROCESS_MODE_INHERIT
	visible = true


## Threaded posing: near ones every frame, farther ones every 2nd / 4th with the time gathered (the manager's loop did
## this on the main thread) - the click box is the manager's (sync_pick, main thread).
func _process(delta: float) -> void:
	pending += delta
	var stride := 1 if AnimLod.FULL or Perf.brute or last_dist < AnimLod.NEAR_FULL_M else (2 if last_dist < 45.0 else 4)
	if (Engine.get_process_frames() + _phase) % stride != 0:
		return
	_process_view(pending, null)
	pending = 0.0


## The click box on / off with its life (main thread: a body's processing changes the scene's lists).
func sync_pick() -> void:
	if state and AnimLod.threaded:                    # shown / hidden with its life (main thread only)
		var vis := (state.alive or state.corpse_left > 0.0) and not state.stealthed
		if visible != vis:
			visible = vis
	if _pick and state:
		var pm := Node.PROCESS_MODE_INHERIT if state.alive and not state.stealthed else Node.PROCESS_MODE_DISABLED
		if _pick.process_mode != pm:
			_pick.process_mode = pm


static var prof_build := {}                   # debug timing of view building, microseconds (tests/crowd_probe.gd)


static var prof_peak := {}                    # the longest single step of each (a hitch: tests/walk_perf.gd)


static func _pb(key: String, since: int) -> int:
	var now := Time.get_ticks_usec()
	prof_build[key] = int(prof_build.get(key, 0)) + now - since
	if now - since > int(prof_peak.get(key, 0)):
		prof_peak[key] = now - since
	return now


func _build_model() -> void:
	var size := float(spawn.get("size", 50)) / 50.0
	var tb := Time.get_ticks_usec()
	if info.has("race") and info.race:
		var f := Fig3Figure.new()
		add_child(f)
		if f.load_figure(String(info.race)):
			tb = _pb("figure", tb)
			f.apply(_look(f))
			tb = _pb("look", tb)
			_dress(f)
			tb = _pb("dress", tb)
			model = f
			anim = f.anim
		f.scale *= size
		# its height (name plate, click box) at the size it is drawn: the /spawn "Companion Class Menu" is a size-1
		# troll (model 150, the server's "invisible" model) - measured before the size, its click box and name stood
		# at full troll height (owner 2026-10-17)
		height = 1.85 * f.scale.y
		tb = _pb("scale", tb)
	elif info.has("object_model"):
		# an object drawn as a living (the server's siege weapons: objects.csv item models - ServerWorld._model_info);
		# the rams, scorpions and boiling oil move (their animated copies: idle / load / fire / walk)
		model = WeaponModels.make_animated(int(info.object_model))
		if model == null:
			model = WeaponModels.make_world(int(info.object_model))
		tb = _pb("mob_instance", tb)
		if model:
			add_child(model)
			model.scale *= size
			height = maxf(_model_height(model), 1.0)
			var players := model.find_children("*", "AnimationPlayer", true, false)
			if players.size() > 0:
				anim = players[0]
				Fig3Figure.add_rest_reset(anim)
				for clip in anim.get_animation_list():   # (animnifs.csv: idle / walk loop, load / fire clamp)
					anim.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if clip in ["idle", "walk", "run"] \
							else Animation.LOOP_NONE
	elif info.has("gltf"):
		model = manager.instance_model(String(info.gltf))
		tb = _pb("mob_instance", tb)
		if model:
			add_child(model)
			model.rotation.y = 0.0
			# the spawn's size AND the client's own scale for the model (monsters.csv column 12, e.g. crabs 30%, giants
			# 500%) - the glTF is at the NIF's raw size (owner 2026-10-10: sand crabs were drawn gigantic)
			model.scale *= size * float(info.get("scale", 1.0))
			var players := model.find_children("*", "AnimationPlayer", true, false)
			if players.size() > 0:
				anim = players[0]
				Fig3Figure.add_rest_reset(anim)
				for clip in anim.get_animation_list():
					anim.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if Fig3Figure.loops(clip) else Animation.LOOP_NONE
			tb = _pb("mob_anims", tb)
			height = maxf(_model_height(model), _idle_top(model))
			var hover := figure_look(model, info, state.index if state else get_instance_id())
			model.position.y += hover
			height += hover
			_hover = hover
			_hover_node = model
			tb = _pb("mob_height", tb)
			_hold_weapons(model)
			tb = _pb("mob_weapons", tb)
	if model:                                              # render layer 2: the quest circles (decals on layer 1)
		for g in model.find_children("*", "GeometryInstance3D", true, false):   # lie on the ground, not on legs
			(g as GeometryInstance3D).layers = 2
	tb = _pb("layers", tb)
	var sound_set := int(info.get("sound_set", 0))
	if model is Fig3Figure:
		sound_set = int((model as Fig3Figure).meta.get("sound_set", sound_set))
	tb = Time.get_ticks_usec()
	if sound_set > 0 and anim and model:
		sounds = CharacterSounds.new()                     # its vocals (attack, pain, death) and footsteps
		sounds.name = "Sounds"
		AnimLod.keep_on_main(sounds)                       # (audio, shared stream caches)
		add_child(sounds)
		# footsteps are only your own in the original game (owner 2026-10-10): NPCs keep their vocals / spells / hits
		sounds.setup(model if not (model is Fig3Figure) else (model as Fig3Figure).model, anim, sound_set, false)
	if anim and anim.has_animation("idle"):
		anim.play("idle")
		anim.seek(randf() * anim.current_animation_length, true)
	_lod = AnimLod.new(self, model, anim)                # manual stepping by distance / screen, shadow cut-off
	if int(spawn.flags) & FLAG_GHOST and model:
		ghost_look(model, model is Fig3Figure)            # a race figure: its own parts, one see-through layer
	tb = _pb("sounds+anim", tb)
	_attach_effects()
	_pb("effects", tb)


## The client's monster effects (build_monsters.py: monsters.csv Effect -> moneffects -> effect NIFs): glows, flames,
## wisps on the node moneffects names (a skeleton bone - Bip01 etc. - when the model has it, else the model's origin);
## AutoScale effects follow the model's size. A wisp's figure has no mesh at all: the effect IS the monster.
static var _fx_cache := {}                    # effect glTF path -> a loaded NifScene to copy (or null: failed)


func _attach_effects() -> void:
	for e in info.get("effects", []):
		var path := String(e.get("gltf", ""))
		var fx: Node3D = NifScene.copy_of(path)
		if fx == null:
			continue
		AnimLod.keep_on_main(fx)                           # (particles, its timeline: on the main thread)
		var skel: Skeleton3D = null
		if model:
			var sk := model.find_children("*", "Skeleton3D", true, false)
			skel = sk[0] if sk.size() > 0 else null
		var bone := skel.find_bone(String(e.get("attach", ""))) if skel else -1
		if bone >= 0:
			var at := BoneAttachment3D.new()
			at.bone_name = skel.get_bone_name(bone)
			skel.add_child(at)
			at.add_child(fx)                               # the model's own scale carries over
		else:
			add_child(fx)
			if bool(e.get("autoscale", false)):
				fx.scale *= float(spawn.get("size", 50)) / 50.0 * float(info.get("scale", 1.0))
			if model == null:
				# a figure that is only its effect (cold lights, wisps): the client floats it too - its min / max float
				# height (owner 2026-10-16: cold lights sat on the ground; the hover was only applied to meshes)
				var hover := float_m(info, state.index if state else get_instance_id())
				fx.position.y += hover
				height = maxf(height, hover + 0.6)
				_hover = hover
				_hover_node = fx
				# its drawn size is the effect's (owner 2026-10-16: a sulphurous cloud's name sat small inside the cloud and
				# only a little box in its middle took clicks): the name over it, the click box round it
				var b := _effect_bounds(fx)
				if b.size.y > 0.1:
					_fx_bounds = b if _fx_bounds.size.y <= 0.0 else _fx_bounds.merge(b)
					height = maxf(height, b.end.y)
		NifParticles.fit_to_scale.call_deferred(fx)          # particle sprites follow the scale too (once placed)


## Weapons in a monster model's hands (skeletons, the Bonedancer's commander and minions - biped monster NIFs carry the
## same Held / Shield bones as the player figures; owner 2026-10-10: pets' period weapons): a two-hander or the
## right-hand weapon on Bip01 R Held (else R Hand), a shield on Bip01 L Shield or an off-hand weapon on L Held, a bow
## alone on the back. Models without those bones simply hold nothing.
func _hold_weapons(root: Node3D) -> void:
	var rows: Array = manager.data.get("equipment", {}).get(String(spawn.get("equipment", "")), [])
	if rows.is_empty():
		return
	var by_slot := {}
	_weapon_fx = {}
	_weapon_colors = {}
	_weapon_emblems = {}
	for e in rows:
		by_slot[int(e.slot)] = int(e.model)
		_weapon_fx[int(e.slot)] = int(e.get("effect", 0))
		_weapon_colors[int(e.slot)] = int(e.get("color", 0))
		_weapon_emblems[int(e.slot)] = int(e.get("emblem", 0))
	_weapon_root = root
	_weapon_slots = by_slot
	_hang_weapons(state != null and state.fighting)


## Weapons drawn in combat, put away out of it - Player.dress's rule (objects.csv Belt / Combat Stance: one-handers
## at the belt, two-handers / polearms on the back, staves stay in the hand; a shield on the arm), the old client's
## way for everyone (owner 2026-10-11: a pet's flail was always in its hand). A skeleton without the belt / back
## bones keeps the weapon in hand.
var _weapon_root: Node3D
var _weapon_slots := {}
var _weapon_fx := {}                           # slot -> the item's weapon effect (a glow / flame), 0 none
var _weapon_colors := {}                       # slot -> the item's dye (tints.csv), 0 none
var _weapon_emblems := {}                      # slot -> a shield's guild emblem, 0 none
var _weapon_nodes: Array = []
var _weapons_drawn := false


## A swing with a melee weapon while the ranged set (a bow, a bard's drum) is out: the set that weapon belongs to is
## drawn (owner 2026-10-15: a gamebot bard used its styles with the drum still in its hand).
func show_weapon_for(model: int) -> void:
	if model <= 0 or _weapon_slots.is_empty() or (int(spawn.get("weapon_slots", 0xFF)) & 0x0F) != 3:
		return
	if int(_weapon_slots.get(12, 0)) == model:
		set_weapon_slots(0x22)
	elif int(_weapon_slots.get(10, 0)) == model or int(_weapon_slots.get(11, 0)) == model:
		set_weapon_slots(0x10)


## The server switched this living's active weapon set (a bow drawn, back to the sword): hung again.
func set_weapon_slots(ws: int) -> void:
	spawn["weapon_slots"] = ws
	if _weapon_root:
		_hang_weapons(_weapons_drawn)


func _hang_weapons(drawn: bool) -> void:
	for n in _weapon_nodes:
		if is_instance_valid(n):
			n.queue_free()
	_weapon_nodes.clear()
	_weapons_drawn = drawn
	if _weapon_root == null or _weapon_slots.is_empty():
		return
	var slots := _weapon_slots
	var active := "twohand" if slots.has(12) else ("standard" if slots.has(10) or slots.has(11) else "ranged")
	# the server's active weapon set when it says (EquipmentUpdate: right hand 0 / 1 standard, 2 two-handed, 3 ranged)
	# - a bone archer with a short sword draws its bow to shoot (owner 2026-10-13, the 1.65 pet sheet)
	var ws := int(spawn.get("weapon_slots", -1))
	if ws >= 0 and ws != 0xFF:
		match ws & 0x0F:
			2: active = "twohand"
			3: active = "ranged"
			0, 1: active = "standard"
	combat_stance = WeaponModels.ranged_kind(int(slots[13])) if active == "ranged" and slots.has(13) 			else WeaponModels.stance(int(slots.get(12 if active == "twohand" else 10, 0)))
	var item_at := func(slot: int) -> Dictionary:
		if not slots.has(slot):
			return {}
		var m := int(slots[slot])
		return {"model": m, "color": int(_weapon_colors.get(slot, 0)),
				"object_type": 42 if slot == 11 and int(WeaponModels.info(m).get("shield", 0)) > 0 else 0,
				"effect": int(_weapon_fx.get(slot, 0)), "emblem": int(_weapon_emblems.get(slot, 0))}
	var skel_root: Node3D = (_weapon_root as Fig3Figure).model if _weapon_root is Fig3Figure else _weapon_root
	var sk := skel_root.find_children("*", "Skeleton3D", true, false)
	var can_sheathe := sk.size() > 0 and (sk[0] as Skeleton3D).find_bone("Bip01 R Belt") >= 0
	_weapon_nodes = Player.dress(skel_root, item_at, active, drawn or not can_sheathe, false)
	if int(spawn.get("flags", 0)) & FLAG_GHOST:                # a spirit's weapons are as ghostly as it is
		for w in _weapon_nodes:
			if is_instance_valid(w):
				ghost_look(w as Node3D, _weapon_root is Fig3Figure)


## The drawn weapon's stance ("" one-hand, "2h", "staff", "pole") and its version of a combat clip: combat_idle ->
## combat_idle_staff, attack_med -> attack_staff_med ... (as the player's; owner 2026-10-11: staffs swung like swords),
## else the one-hand clip.
var combat_stance := ""


func _stance_clip(base: String) -> String:
	return WeaponModels.stance_clip(anim, combat_stance, base)


## Combat starts / ends: draw or put away (on the main thread - the views may step on worker threads).
func _check_weapons() -> void:
	if state and not _weapon_slots.is_empty() and state.fighting != _weapons_drawn:
		_weapons_drawn = state.fighting                       # (once: the re-hang itself runs deferred)
		_hang_weapons.call_deferred(state.fighting)


## The NPC's worn equipment (old DB NPCEquipment via npcs.json "equipment"; ZONE CONTENT PROCESS step 9): armour on
## the Catacombs figure (Fig3Figure.set_armor, pipeline/build_armor.py) and weapons in its hands - a two-hander or the
## right-hand weapon on Bip01 R Held, a shield on the left arm or an off-hand weapon on Bip01 L Held; a bow alone
## goes on the back. Helms and cloaks are not drawn yet (AUTONOMOUS WORK LOG).
func _dress(f: Fig3Figure) -> void:
	var rows: Array = manager.data.get("equipment", {}).get(String(spawn.get("equipment", "")), [])
	if rows.is_empty():
		return
	var female := String(info.get("race", "")).ends_with("_f")
	var parts := {}
	var tints := {}
	var by_slot := {}
	_weapon_fx = {}
	_weapon_colors = {}
	_weapon_emblems = {}
	for e in rows:
		var slot := int(e.slot)
		by_slot[slot] = int(e.model)
		# the weapons' effects, dyes and a shield's guild emblem, as _hold_weapons keeps them (a race figure's weapons
		# hung without them)
		_weapon_fx[slot] = int(e.get("effect", 0))
		_weapon_colors[slot] = int(e.get("color", 0))
		_weapon_emblems[slot] = int(e.get("emblem", 0))
		if slot in [22, 23, 25, 27, 28]:
			var p := Fig3Figure.item_armor(int(e.model), int(e.get("extension", 0)), female)   # (its mesh: the extension)
			parts.merge(p, true)
			for k in p:                                             # the server's colour for it (EquipmentUpdate)
				tints[k] = Fig3Figure.dye(int(e.get("color", 0)))
		elif slot == 26:                                            # the cloak (hood up / down: the server's hood flag)
			f.cloak_emblem = int(e.get("emblem", 0))                # a guild gamebot's / companion's emblem
			var cp := Fig3Figure.armor_parts(int(e.model), female)
			if cp.has("cloak"):
				parts["cloak"] = cp.cloak
				tints["cloak"] = Fig3Figure.dye(int(e.get("color", 0)))
	var gear: Dictionary = spawn.get("gear", {}) if spawn.get("gear") is Dictionary else {}
	f.hood_up = bool(gear.get("hood", false))
	f.cloak_hidden = bool(gear.get("cloak_hidden", false))
	f.helm_hidden = bool(gear.get("helm_hidden", false))
	f.set_armor(parts, tints)
	var helm_color := 0
	for e in rows:
		if int(e.slot) == 21:
			helm_color = int(e.get("color", 0))
	f.set_helm(int(by_slot.get(21, 0)), f.helm_hidden, helm_color)
	_weapon_root = f
	_weapon_slots = by_slot
	_hang_weapons(state != null and state.fighting)


## The GHOST flag's look (a spiritmaster's spirit pets, ghostly NPCs): see-through, with a subtle white glow over it
## (ghost_glow.gdshader) - not a plain grey half-transparency (owner 2026-10-14).
static var _ghost_glow: ShaderMaterial


static var _ghost_shaders := {}                 # a Catacombs figure shader -> [its depth pass, its see-through pass]


## figure: a ghost race figure (a spiritmaster's spirit pets - Midgard player-race models): its own textured parts kept
## under the glow, as the old client (owner 2026-10-15: "the original client has the model under the translucent
## effects"), but drawn see-through as ONE layer: each part first writes only its depth (front faces), then draws its
## colour half see-through on that surface alone. The figure shader is double-sided, and half see-through the inside of
## the head (eye sockets, mouth) and the far side showed through the face as grey blobs. Parts on other shaders
## (weapons, helms) the plain way.
static func ghost_look(model: Node3D, figure := false) -> void:
	if _ghost_glow == null:
		_ghost_glow = ShaderMaterial.new()
		_ghost_glow.shader = load("res://game/npcs/ghost_glow.gdshader")
		# drawn after every ghost's depth and colour passes (they are 0 and 1): its order against the depth passes was
		# left to the distance sort - before them it lit every inner face (the ghost went white), after them only the
		# front (darker) - and among a crowd of ghosts walking about that order changed frame to frame: "dark to white
		# highlighted repeatedly" (owner 2026-10-17, frost spectres at a keep)
		_ghost_glow.render_priority = 2
	for mi in model.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		var sm := m.material_override as ShaderMaterial
		if figure and sm and sm.shader and String(sm.shader.code).contains("uniform int tint_mode"):
			var pair: Array = _ghost_shader(sm.shader)
			var depth := sm.duplicate() as ShaderMaterial
			depth.shader = pair[0]
			var look := sm.duplicate() as ShaderMaterial
			look.shader = pair[1]
			look.render_priority = 1                         # after every depth pass
			depth.next_pass = look
			m.material_override = depth
			m.material_overlay = _ghost_glow
			continue
		# a monster model (the spectral snake, forest skrats, spirits): one see-through layer too - each surface first
		# writes only its depth, then draws its colour half see-through on that surface alone. The plain 50 %
		# transparency showed its inner faces and re-sorted them as it moved: the ghost flickered (owner 2026-10-16)
		if m.mesh == null:
			continue
		for i in m.mesh.get_surface_count():
			var base := m.get_active_material(i) as BaseMaterial3D
			var two_sided := base != null and base.cull_mode == BaseMaterial3D.CULL_DISABLED
			var tex: Texture2D = base.albedo_texture if base else null
			var depth := ShaderMaterial.new()
			depth.shader = _monster_ghost_shader(true, two_sided)
			var look := ShaderMaterial.new()
			look.shader = _monster_ghost_shader(false, two_sided)
			look.render_priority = 1                             # after every depth pass
			for mat in [depth, look]:
				(mat as ShaderMaterial).set_shader_parameter("albedo_tex", tex)
				(mat as ShaderMaterial).set_shader_parameter("has_tex", tex != null)
			look.set_shader_parameter("albedo_col", base.albedo_color if base else Color.WHITE)
			depth.next_pass = look
			m.set_surface_override_material(i, depth)
		m.material_overlay = _ghost_glow


static var _monster_ghost_shaders := {}                  # [depth pass?, two-sided?] -> Shader


static func _monster_ghost_shader(depth_pass: bool, two_sided: bool) -> Shader:
	var key := "%s:%s" % [depth_pass, two_sided]
	if not _monster_ghost_shaders.has(key):
		var cull := "cull_disabled" if two_sided else "cull_back"
		var code := ""
		if depth_pass:
			code = """shader_type spatial;
render_mode blend_mix, depth_draw_always, %s, unshaded, shadows_disabled;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_tex = false;
void fragment() {
	float a = has_tex ? texture(albedo_tex, UV).a : 1.0;
	if (a < 0.5) discard;
	ALPHA = 0.0;
	DEPTH = FRAGCOORD.z * 0.9993;
}
""" % cull
		else:
			code = """shader_type spatial;
render_mode blend_mix, depth_draw_never, %s, shadows_disabled;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform bool has_tex = false;
uniform vec4 albedo_col : source_color = vec4(1.0);
uniform float ghost_alpha = 0.5;
void fragment() {
	vec4 c = albedo_col * (has_tex ? texture(albedo_tex, UV) : vec4(1.0));
	ALBEDO = c.rgb;
	ALPHA = c.a * ghost_alpha;
}
""" % cull
		var sh := Shader.new()
		sh.code = code
		_monster_ghost_shaders[key] = sh
	return _monster_ghost_shaders[key]


static func _ghost_shader(src: Shader) -> Array:
	if not _ghost_shaders.has(src):
		var code := String(src.code)

		var mode := RegEx.create_from_string("render_mode [^;]*;")
		# hair stays double-sided (the cutout shader is hair's only): fig3parts.csv marks nearly every hair style Double
		# Sided, and culled its back half went - a ghostly dwarf's braided head looked bald from the front (owner
		# 2026-10-19, a spirit champion). The depth pass still keeps only the nearest layer, so nothing shows through
		var cull := "cull_disabled" if code.contains("ALPHA_SCISSOR_THRESHOLD") else "cull_back"
		var d := mode.sub(code, "render_mode blend_mix, depth_draw_always, %s, shadows_disabled;" % cull)
		# its depth a hair behind the surface (reverse Z: smaller is farther; x 0.9993): the two passes are different
		# shaders, so their depths for the same face differ by rounding, and the colour pass flickered in and out as the
		# camera turned (owner 2026-10-15). 0.3 % let what lies a few cm behind the skin - the eyes, the inside of the
		# mouth - show through the face (owner 2026-10-16); 0.07 % is still hundreds of times the rounding.
		d = d.replace("ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;", "if (c.a < 0.5) discard;
	ALPHA = 0.0;
	DEPTH = FRAGCOORD.z * 0.9993;")
		d = d.replace("//CUTOUT", "ALPHA = 0.0;
	DEPTH = FRAGCOORD.z * 0.9993;")
		# the see-through pass: the part's own colours - skin tone, dyes - evenly see-through, a little whitened. Only
		# hair takes its texture alpha (its coverage); on faces the alpha is gloss and on clothes the tint mask, and as
		# opacity it left skin and dyed armour the most see-through - no skin or armour colour under the ghost (owner
		# 2026-10-16, after the 2026-10-15 texture-alpha pass)
		var l := mode.sub(code, ("render_mode blend_mix, depth_draw_never, %s, shadows_disabled;" % cull) + "
uniform float ghost_alpha = 0.9;
uniform float ghost_white = 0.0;")
		l = l.replace("ALPHA = c.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;", "ALBEDO = mix(ALBEDO, vec3(1.0), ghost_white);
	ALPHA = c.a * ghost_alpha;")
		l = l.replace("//CUTOUT", "ALBEDO = mix(ALBEDO, vec3(1.0), ghost_white);
	ALPHA = ghost_alpha;")
		var ds := Shader.new()
		ds.code = d
		var ls := Shader.new()
		ls.code = l
		_ghost_shaders[src] = [ds, ls]
	return _ghost_shaders[src]


## A stable look for a figure NPC from its id (Fig3Figure.npc_look).
func _look(f: Fig3Figure) -> Dictionary:
	return Fig3Figure.npc_look(String(spawn.id), f.meta)   # (NpcManager prepares its textures from the same look)


## A monster figure drawn the client's way (monsters.json from build_monsters.table_fields): its skin's alpha - "cut"
## (skeleton ribs, harpy feathers), "blend" (ghosts, the necromancer's shade), "blend_depth" (treant leaves, djinn
## smoke) - and its hover: a random height between the client's min / max float height (world units), the same for a
## given seed. Returns the hover in metres (owner 2026-10-13: the shade was opaque and too low, mylings didn't float).
static func figure_look(m: Node3D, model_info: Dictionary, seed: int) -> float:
	# the client's "Invisible Warrior (Spell FX)" (model 666, figure Invisible): nothing drawn - a GM in /fly (the
	# server's observer flight turns you into it), the unseen casters of spell effects (the Dream Tree ...). Its body
	# mesh drew as a plain cloth figure (owner 2026-10-15: /fly has to hide the character); its bones stay for effects
	if String(model_info.get("figure", "")).to_lower() == "invisible":
		for mi in m.find_children("*", "MeshInstance3D", true, false):
			(mi as MeshInstance3D).visible = false
	var alpha := String(model_info.get("alpha", ""))
	if alpha != "":
		var mode: int = {"cut": BaseMaterial3D.TRANSPARENCY_ALPHA_SCISSOR, "blend": BaseMaterial3D.TRANSPARENCY_ALPHA,
				"blend_depth": BaseMaterial3D.TRANSPARENCY_ALPHA_DEPTH_PRE_PASS}.get(alpha, BaseMaterial3D.TRANSPARENCY_DISABLED)
		for mi in m.find_children("*", "MeshInstance3D", true, false):
			var mesh := (mi as MeshInstance3D).mesh
			for i in (mesh.get_surface_count() if mesh else 0):
				var mat := mesh.surface_get_material(i) as BaseMaterial3D
				if mat and mat.transparency != mode:          # (shared by every copy of the model: set once)
					mat.transparency = mode
					mat.alpha_scissor_threshold = 0.5
	return float_m(model_info, seed)


## The client's float height for a model (monsters.csv min / max float height, world units): a random height between
## them, the same for a given seed, in metres; 0 without one.
static func float_m(model_info: Dictionary, seed: int) -> float:
	var fl: Array = model_info.get("float_units", [])
	if fl.size() < 2:
		return 0.0
	var rng := RandomNumberGenerator.new()
	rng.seed = hash(seed)
	return rng.randf_range(float(fl[0]), float(fl[1])) * 0.025


## How high the model's skeleton reaches in its idle pose, in the view's space (0 without one). A creature whose own
## clip carries it up - the client's cicadas fly a few metres up in their idle .kfa (Dyranapur, Lydsyg, the giant
## cicadas) - has its rest-pose mesh down at the ground: measured from that alone, its name and click box sat on the
## floor under the flying body (owner 2026-10-17).
func _idle_top(m: Node3D) -> float:
	if anim == null or not anim.has_animation("idle"):
		return 0.0
	var skels := m.find_children("*", "Skeleton3D", true, false)
	if skels.is_empty():
		return 0.0
	anim.play("idle")
	anim.advance(0.0)
	var sk := skels[0] as Skeleton3D
	var to_view := Transform3D.IDENTITY                   # the skeleton's transform up to this view (the model's scale)
	var n: Node = sk
	while n != self and n is Node3D:
		to_view = (n as Node3D).transform * to_view
		n = n.get_parent()
	var top := 0.0
	for i in sk.get_bone_count():
		top = maxf(top, (to_view * sk.get_bone_global_pose(i)).origin.y)
	return top + 0.25 if top > 0.0 else 0.0


func _model_height(m: Node3D) -> float:
	var aabb := AABB()
	var first := true
	for mi in m.find_children("*", "MeshInstance3D", true, false):
		if not (mi as MeshInstance3D).visible:
			continue
		# the mesh's box in the model's own space (its nodes' transforms up to the model), then the model's scale once
		# (owner 2026-10-11: the global transform already held the model's scale and it was applied again - scaled
		# monsters like a fomorian or calico had their names far above their heads)
		var t := Transform3D.IDENTITY
		var n: Node = mi
		while n != m and n is Node3D:
			t = (n as Node3D).transform * t
			n = n.get_parent()
		var b: AABB = t * (mi as MeshInstance3D).get_aabb()
		aabb = b if first else aabb.merge(b)
		first = false
	return clampf(aabb.end.y * m.scale.y if not first else 1.5, 0.3, 12.0)


func _build_pick() -> void:
	_pick = StaticBody3D.new()
	_pick.collision_layer = 1 << 9                           # layer 10: NPCs (clicks only, nothing collides)
	_pick.collision_mask = 0
	_pick.set_meta("npc", self)
	# the click volume is the whole drawn model's box (owner 2026-10-10: clicks missed - "too small vs old client";
	# the old capsule was a quarter of the height wide, so arms, weapons, a wide monster's body missed), grown 10%,
	# at least 0.6 m across
	var box := BoxShape3D.new()
	var bounds := _local_bounds()
	var size := bounds.size * 1.1
	box.size = Vector3(maxf(size.x, 0.6), maxf(size.y, 0.5), maxf(size.z, 0.6))
	var shape := CollisionShape3D.new()
	shape.shape = box
	shape.position = bounds.get_center()
	_pick.add_child(shape)
	add_child(_pick)


## The drawn model's bounds in this NPC's own space (every mesh incl. held weapons), or a figure-sized box.
var _fx_bounds := AABB()                                  # an effect-only figure's drawn box (local), else empty


## An attached effect's drawn box in this view's space: its meshes' and particle systems' boxes (a particle system's
## visibility box), through the effect's own transforms and scale. Capped at 30 m (a runaway default box).
func _effect_bounds(fx: Node3D) -> AABB:
	var out := AABB()
	var first := true
	for g in fx.find_children("*", "GeometryInstance3D", true, false):
		var gi := g as GeometryInstance3D
		var t := Transform3D.IDENTITY
		var n: Node = gi
		while n != self and n is Node3D:
			t = (n as Node3D).transform * t
			n = n.get_parent()
		var box: AABB = t * gi.get_aabb()
		if box.size.length() <= 0.0 or box.size.length() > 30.0:
			continue
		out = box if first else out.merge(box)
		first = false
	return out if not first else AABB()


func _local_bounds() -> AABB:
	var out := AABB()
	var first := true
	var inv := global_transform.affine_inverse()
	if model:
		for mi in model.find_children("*", "MeshInstance3D", true, false):
			var m := mi as MeshInstance3D
			if not m.visible or m.mesh == null:
				continue
			var b := inv * m.global_transform * m.get_aabb()
			out = b if first else out.merge(b)
			first = false
	if (first or out.size.y < 0.1) and _fx_bounds.size.y > 0.1:
		return _fx_bounds                                   # a figure that is only its effect: the effect's box
	if first or out.size.y < 0.1:
		var h := maxf(height, 0.5)
		return AABB(Vector3(-h * 0.25, 0.0, -h * 0.25), Vector3(h * 0.5, h, h * 0.5))
	return out


func con_to(player_level: int) -> int:
	return manager.con_level(player_level, state.level)


## The NPC's con colour to you - friendly NPCs con too (period: "you target him (con purple at lvl 14), it will say
## his name and that is a friendly guard"); the target display uses it, overhead names don't.
func con_color(player_level: int) -> Color:
	return CON_COLORS.get(con_to(player_level), Color.WHITE)


## Friendly to the player: overhead name in light aqua, not its con colour (NamePlates).
func is_friendly() -> bool:
	return int(spawn.realm) == PlayerData.realm_id()           # your own realm's people (and your pets)


## GameNPC.GetAggroLevelString: your realm friendly; a faction monster that would aggro (level and range) by your
## standing with its faction; otherwise aggressive with an aggro level, else neutral.
func stance() -> String:
	if int(spawn.realm) == PlayerData.realm_id():
		return "friendly"
	var fs := PlayerData.faction_standing(int(spawn.get("faction", 0)))
	if fs != "" and int(spawn.aggro_level) > 0 and int(spawn.aggro_range) > 0:
		return fs
	return "aggressive" if int(spawn.aggro_level) > 0 else "neutral"


func pronoun() -> String:
	var g := int(spawn.get("gender", 0))
	var model_name := String(info.get("name", "")).to_lower()
	if g == 2 or model_name.contains("female") or model_name.begins_with("fem"):
		return "She"
	if g == 1 or info.has("race"):
		return "He"
	return "It"


func display_name(with_article: bool) -> String:
	var n := String(spawn.name)
	var lower := n.length() > 0 and n[0] == n[0].to_lower()  # monsters are lower-case: "the lough wolf"
	return ("the " + n) if with_article and lower else n


## Follow the simulated state: position, facing, and the matching animation (walk / idle, combat idle while it
## fights, the death pose; the body disappears when its corpse time runs out and returns at respawn).
static var prof_us := 0                       # debug timing (tests/crowd_probe.gd)


## Called by NpcManager (one loop for every view: near ones every frame, farther ones every 2nd / 4th frame with the
## time gathered) - 265 separate engine-to-script _process calls cost a raid crowd ~12 ms a frame (crowd_probe).
var last_dist := 0.0                          # metres from the camera at the last tick
var pending := 0.0                            # time gathered since the last tick


func tick(delta: float, cam: Camera3D) -> void:
	var tp0 := Time.get_ticks_usec()
	_process_view(delta, cam)
	prof_us += Time.get_ticks_usec() - tp0


func _process_view(delta: float, cam: Camera3D) -> void:
	if state == null:
		return
	# only on change: setting a transform (even to the same value) pushes new transforms to every child - ~34 mesh
	# parts, the skeleton, the labels, the click shape - and 265 standing NPCs did that every frame (crowd_probe)
	if position != state.pos:
		position = state.pos
	if rotation.y != state.yaw:
		rotation.y = state.yaw
	if not AnimLod.threaded:                          # (threaded: sync_pick shows / hides it - main thread only)
		var vis := (state.alive or state.corpse_left > 0.0) and not state.stealthed
		if visible != vis:
			visible = vis
	_animate_lod(delta, cam)
	if _swim_fit > 0:
		_swim_fit -= 1
		if _swim_fit == 0:
			_fit_swim()
	_check_weapons()
	if not AnimLod.threaded:
		sync_pick()
	_action_left -= delta
	if casting_spell:
		_cast_left -= delta
		if _cast_left < -CAST_GRACE:                     # the spell's effect never came (out of view): stop holding
			casting_spell = false
	if anim == null or _action_left > 0.0:
		return
	if casting_spell:                                 # the cast's loop, held until the spell lands (cast_end)
		if anim.current_animation != _cast_clips[1] and anim.has_animation(_cast_clips[1]):
			anim.play(_cast_clips[1], 0.1)
			_shown_mode = -1
		return
	if _bow_hold and state.fighting and state.alive:   # the bow drawn: the aim held until it looses (bow_release)
		var aim := _ranged() + "_aim"
		if anim.current_animation != aim and anim.has_animation(aim):
			anim.play(aim, 0.1)
			_shown_mode = -1
		return
	_bow_hold = false
	if state.ride_clip != "":                        # riding (the server's Riding packet): the rider's pose
		if anim.current_animation != state.ride_clip and anim.has_animation(state.ride_clip):
			anim.play(state.ride_clip, 0.2)
			_shown_mode = -1
		return
	var run_now: bool = state.fighting or state.running
	var swim: bool = state.swimming and state.mode != NpcState.Mode.DEAD and anim.has_animation("swim")
	if int(state.mode) != _shown_mode or (_shown_mode == int(NpcState.Mode.FIGHT) and anim.current_animation == "") 			or (_shown_mode == int(NpcState.Mode.WALK) and run_now != _shown_running) or swim != _shown_swim:
		_shown_mode = int(state.mode)
		_shown_running = run_now
		if swim != _shown_swim:
			_swim_fit = 2 if swim else 0                   # (measured once the swim pose is on: _fit_swim)
			if not swim:
				_set_swim_drop(0.0)
		_shown_swim = swim
		var clip := "idle"
		match state.mode:
			NpcState.Mode.WALK:
				clip = "run" if run_now and anim.has_animation("run") else "walk"
			NpcState.Mode.FIGHT:
				clip = _stance_clip("combat_idle") if anim.has_animation("combat_idle") else "idle"
			NpcState.Mode.DEAD:
				return                                     # play_death() holds the last frame
		if swim:
			clip = "swim"                                  # swimming, still or moving (as you: Player)
		if anim.has_animation(clip):
			anim.play(clip, 0.2)


var _action_left := 0.0


## A swimmer half in the water (owner 2026-10-15: the sjoalf waverunners - and companions, their pets - lay on top of the
## water in their swim pose): the server holds a swimmer at the surface and _ground puts its feet Player.SWIM_FLOAT under
## it, which suits the race figures' low swim pose (yours); the monsters' classic swim poses keep the body up at standing
## height. Once the swim clip is on, the model is lowered so its pelvis lies at the waterline - the body half under,
## the back and head out - whatever the figure; back up when it leaves the water.
var _swim_fit := 0
var _swim_drop := 0.0


func _fit_swim() -> void:
	if model == null or not is_instance_valid(model) or not _shown_swim:
		return
	if info.has("race") or model is Fig3Figure:
		# a race figure (gamebots, companions) swims as you do, at the height _ground gives it - its swim pose is the
		# low one you swim in; lowered to the pelvis as well it went under (owner 2026-10-17: gamebots and companions
		# swam far below you, names on the surface)
		return
	var sk: Skeleton3D = null
	for s in model.find_children("*", "Skeleton3D", true, false):
		sk = s
		break
	if sk == null:
		return
	var b := -1
	for name in ["Bip01 Pelvis", "Bip01", "Bip01 Spine"]:
		b = sk.find_bone(name)
		if b >= 0:
			break
	if b < 0:
		return
	var pelvis := to_local(sk.global_transform * sk.get_bone_global_pose(b).origin).y - _swim_drop
	# its feet hang SWIM_FLOAT under the surface: the pelvis at the surface itself
	_set_swim_drop(clampf(Player.SWIM_FLOAT - pelvis, -2.5, 0.0))


func _set_swim_drop(d: float) -> void:
	if model and is_instance_valid(model):
		model.position.y += d - _swim_drop
	_swim_drop = d


## Animation and shadow level of detail: AnimLod (shared with the gamebots' and companions' figures).
var _lod: AnimLod


func _animate_lod(delta: float, cam: Camera3D) -> void:
	if _lod:
		_lod.step(delta, cam)
		last_dist = _lod.last_dist


## A siege weapon's action (SiegeWeaponAnimation: 2 arming / 3 loading -> load, 4 fire -> fire; the ram's swing).
func play_siege(action: int) -> void:
	var clip: String = {2: "load", 3: "load", 4: "fire"}.get(action, "")
	if clip != "" and anim and anim.has_animation(clip):
		_one_shot(clip)


## A swing; returns the clip's length (Combat lands the blow part-way through). hand: "" the stance's swing, "left" /
## "dual" a dual wielder's off-hand / both-weapons blow (WeaponModels.attack_clip).
func play_attack(hand := "") -> float:
	var clip := WeaponModels.attack_clip(anim, combat_stance, hand)
	WeaponModels.flex_swing(self, clip)                    # a flex weapon's chain swings with the blow
	return _one_shot(clip)


## A styled blow (CombatAnimation's style): the figure's style animation when it has it, the chain's style swing.
func play_style(animation: int) -> float:
	var clip := String(Player.style_clips().get(str(animation), ""))
	WeaponModels.flex_swing(self, clip if clip != "" else "style_1", true)
	if clip != "" and anim and anim.has_animation(clip):
		return _one_shot(clip)
	return _one_shot(WeaponModels.attack_clip(anim, combat_stance))


## The bow (the server's CombatAnimation BowPrepare / BowShoot; canims Bow Ready / Bow Aim / Bow Fire): draw, hold
## the aim, loose - as the player's (owner 2026-10-15: "bone archers don't pull out and use bows they just swing a sword
## at range" - both packets played a melee swing). A figure without bow clips just stands (no sword swing at range).
var _bow_hold := false


func bow_draw() -> void:
	_bow_hold = true
	if anim and anim.has_animation(_ranged() + "_ready"):
		_one_shot(_ranged() + "_ready")


func bow_release() -> void:
	_bow_hold = false
	if anim and anim.has_animation(_ranged() + "_fire"):
		_one_shot(_ranged() + "_fire")


## The ranged clips' family: a crossbow's Xbow clips, a thrown weapon's Throw clips, else the bow's.
func _ranged() -> String:
	if combat_stance in WeaponModels.RANGED:
		return combat_stance
	return WeaponModels.ranged_kind(int(_weapon_slots.get(13, 0)))


func bow_cancel() -> void:
	if _bow_hold:
		_bow_hold = false
		_action_left = 0.0
		_shown_mode = -1


## A spell cast (the server's SpellCastAnimation), as the player's: the cast's opening clip, its loop held for the cast
## time, the release when the spell lands (owner 2026-10-11: a bard's buffs stopped after the opening clip - only
## the first clip played, then idle). `clips` = the spell effect's [start, loop, release] (SpellFx), seconds = the
## packet's cast time.
## A casting style's clip name in this model's own clips: a monster model's anim set has its own clip per style
## (monsters.json cast_clips, pipeline/build_monsters.py: the necroservant's RD_Cast_A / B / C for every style).
func clip_for(name: String) -> String:
	var own: Dictionary = info.get("cast_clips", {}) if info is Dictionary else {}
	return String(own.get(name, name))


func cast_start(clips: Array, seconds: float) -> void:
	_cast_clips = [clip_for("cast_start"), clip_for("cast_mid"), clip_for("cast_end")]
	for i in mini(clips.size(), 3):
		var c := clip_for(String(clips[i]))
		if c != "" and anim and anim.has_animation(c):
			_cast_clips[i] = c
	if anim and not anim.has_animation(_cast_clips[0]) and anim.has_animation("cast"):
		_cast_clips[0] = "cast"                            # monster figures: one cast clip
	casting_spell = true
	_cast_left = seconds
	_one_shot(_cast_clips[0])


func cast_end(landed := false) -> void:
	if not casting_spell:
		return
	casting_spell = false
	if landed and anim and anim.has_animation(_cast_clips[2]):
		_one_shot(_cast_clips[2])
	else:
		_action_left = 0.0
		_shown_mode = -1


const CAST_GRACE := 2.0
var casting_spell := false
var _cast_left := 0.0
var _cast_clips := ["cast_start", "cast_mid", "cast_end"]


## An emote clip (emotes.csv id) when its figure has one (player-race NPCs: build_races.py 'emote_<id>').
func play_emote(id: int) -> void:
	if anim and anim.has_animation("emote_%d" % id) and _action_left <= 0.0:
		_one_shot("emote_%d" % id)


func play_defend(kind: String) -> void:
	if _action_left > 0.0 or anim == null:
		return
	var clip := WeaponModels.defend_clip(anim, combat_stance, kind)
	if clip != "":
		_one_shot(clip)


func flinch() -> void:
	if _action_left <= 0.0:
		_one_shot("flinch")


## vocal: its death cry too (the server's deaths; offline combat plays its own).
var _hover := 0.0                                         # its float height (monsters.csv), metres
var _hover_node: Node3D                                    # what was lifted by it (the model, or a wisp's effect)
var _fell := false


func play_death(vocal := false) -> void:
	if vocal and sounds and _shown_mode != int(NpcState.Mode.DEAD):
		sounds.event("die")
	_action_left = 0.0
	_shown_mode = int(NpcState.Mode.DEAD)
	# a cast or a drawn bow ends with its caster (owner 2026-10-17: /disband with a bonedancer companion's boneknitters
	# mid-cast - the bodies stood in the cast loop, the loop being replayed over the death every frame, till they sank)
	casting_spell = false
	_bow_hold = false
	if anim and anim.has_animation("death"):
		anim.play("death", 0.1)
	# a hovering monster's body drops to the ground as it dies - bodies lie on the ground to be looted (owner
	# 2026-10-16: a dead forest skrat hung in the air, then floated down and was gone)
	if _hover > 0.0 and not _fell and is_instance_valid(_hover_node):
		_fell = true
		var tw := create_tween()
		tw.tween_property(_hover_node, "position:y", _hover_node.position.y - _hover, clampf(_hover * 0.35, 0.25, 0.8)) 				.set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_IN)


## A body the server let go (RemoveObject type 0: the server deletes a monster the moment it dies; the old client
## kept the body - owner 2026-10-13: "monster bodies just disappear when they die instead of having a death animation
## and falling thru the ground after awhile"): it dies here if it hadn't yet, lies there CORPSE_S, sinks into the
## ground over SINK_S, and is gone. No longer clickable. (The lengths are mine: the client's are not in its data.)
const CORPSE_S := 12.0
const SINK_S := 4.0
var corpse := false
var _corpse_y := 0.0


func make_corpse() -> void:
	if corpse:
		return
	corpse = true
	play_death(true)
	if _pick:
		_pick.process_mode = Node.PROCESS_MODE_DISABLED
	for l in get_meta("plate", []):                        # (the plates follow only the living views)
		if is_instance_valid(l):
			(l as Node3D).visible = false
	if state:
		state.alive = false
		state.mode = NpcState.Mode.DEAD
		state.corpse_left = 1e9
		_corpse_y = state.pos.y
	var tw := create_tween()
	tw.tween_interval(CORPSE_S)
	tw.tween_method(_sink, 0.0, height + 0.5, SINK_S)
	tw.tween_callback(queue_free)


func _sink(depth: float) -> void:
	if state:
		state.pos.y = _corpse_y - depth                     # (the view follows its state's place)


func _one_shot(clip: String) -> float:
	# a dead body moves no more: no swing, style, flinch or parry over its death (owner 2026-10-19: an NPC that died
	# kept attacking a second or two - a blow / hit packet a moment after the death replayed a swing over it)
	if state and not state.alive:
		return 0.0
	clip = clip_for(clip)                                  # (a casting style: this model's own clip for it)
	if anim == null or not anim.has_animation(clip):
		return 0.6
	anim.play(clip, 0.1)
	anim.seek(0.0, true)
	_action_left = anim.get_animation(clip).length
	_shown_mode = -1
	return _action_left
