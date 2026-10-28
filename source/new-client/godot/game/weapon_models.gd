class_name WeaponModels
extends RefCounted
## Held / sheathed weapons and shields (pipeline/build_weapons.py -> content/weapons). Each item's 'model' is an
## objects.csv row: its NIF from app/items, and the client's flags - Belt (1 = a one-hand weapon hangs at the belt
## when sheathed, else it goes on the back), Combat Stance (1 one-hand, 2 two-hand, 3 polearm, 4 staff, ...),
## Left Hand. Weapons hang on the figure's own attach bones (Bip01 R Held / L Held, L Shield, L Belt, R Back ...). The figures
## stay in NIF space (DAoC units, Z up) under their root, so the converted model (Godot axes, metres) is turned back
## into that space on its holder.

const DIR := "res://content/weapons"
# Godot axes, metres -> NIF axes, DAoC units: nif = (x, -z, y) * 40
const TO_NIF := Transform3D(Basis(Vector3(1, 0, 0), Vector3(0, 0, 1), Vector3(0, -1, 0)) * 40.0, Vector3.ZERO)

static var _data := {}
static var _scenes := {}


static func info(model: int) -> Dictionary:
	if _data.is_empty():
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(DIR + "/weapons.json"))
		_data = parsed.get("models", {}) if parsed is Dictionary else {}
	return _data.get(str(model), {})


## The combat idle / swing set for a weapon (objects.csv Combat Stance -> the canims.csv columns the figures build,
## owner 2026-10-15: "all clips need to be proper ... so nothing is incorrect or using the wrong clip"): "" one-hand
## (1, and 0 / 12 nothing special), "2h" two-hand, "pole" polearm, "staff", "bow", "xbow" crossbow, "1ht" thrust
## (dirks, rapiers), "throw" thrown, "fws" / "fwm" / "fwl" flexible (chains / flails / whips, the client's small /
## medium / large flex columns in order), "h2h" hand to hand (the Savage's claws and greaves), "scy" scythe.
const STANCES := {2: "2h", 3: "pole", 4: "staff", 5: "bow", 6: "xbow", 7: "1ht", 8: "throw", 9: "fws", 10: "fwm",
		11: "fwl", 13: "h2h", 14: "scy"}
const RANGED := ["bow", "xbow", "throw"]


static func stance(model: int) -> String:
	return STANCES.get(int(info(model).get("stance", 1)), "")


## A stance's version of a combat clip when the figure has it, else the one-hand clip: combat_idle -> combat_idle_2h /
## _pole / _staff / _h2h / _scy / _bow (a crossbow: xbow_idle), attack_med -> attack_2h_med / attack_1ht_med /
## attack_fwm_med / attack_h2h_med ... A ranged stance swings the one-hand way (a blow with the bow out).
static func stance_clip(anim: AnimationPlayer, st: String, base: String) -> String:
	if st == "" or anim == null:
		return base
	var c := ""
	if st == "xbow" and base == "combat_idle":
		c = "xbow_idle"
	elif base.begins_with("attack_"):
		c = "" if st in RANGED else base.replace("attack_", "attack_%s_" % st)
	else:
		c = "%s_%s" % [base, st]
	return c if c != "" and anim.has_animation(c) else base


## A defence's clip for the stance (canims Parry / Evade / Block): two-hand and scythe the 2H parries / evade, polearm
## and staff the polearm ones, a flex weapon its own parry, everything else the one-hand ones; "" when the figure has
## none. Parries and blocks pick the low or the high one.
static func defend_clip(anim: AnimationPlayer, st: String, kind: String) -> String:
	if anim == null:
		return ""
	var hl: String = ["low", "high"][randi() % 2]
	var fam: String = {"2h": "2h", "scy": "2h", "pole": "pole", "staff": "pole"}.get(st, "")
	var want := ""
	match kind:
		"parry":
			if st in ["fws", "fwm", "fwl"]:
				want = "parry_" + st
			else:
				want = "parry_%s_%s" % [fam, hl] if fam != "" else "parry_" + hl
		"evade":
			want = "evade_" + fam if fam != "" else "evade"
		"block":
			want = "block_" + hl
	if anim.has_animation(want):
		return want
	var plain: String = {"parry": "parry_" + hl, "evade": "evade", "block": "block_" + hl}.get(kind, "")
	return plain if anim.has_animation(plain) else ""


## The ranged clips' family for a ranged weapon: "bow", "xbow" (crossbow: Xbow Ready / Aim / Fire) or "throw"
## (thrown weapons: Throw Ready / Aim / Fire).
static func ranged_kind(model: int) -> String:
	var st := stance(model)
	return st if st in ["xbow", "throw"] else "bow"


## A melee blow's clip: hand "" the stance's swing, "left" the off hand's (the server's OneDualWeaponHit), "dual" both
## weapons (BothDualWeaponHit) - canims Att Left / Att Dual; a random high / medium / low one.
static func attack_clip(anim: AnimationPlayer, st: String, hand := "") -> String:
	var lvl: String = ["med", "high", "low"][randi() % 3]
	if hand != "" and anim and anim.has_animation("attack_%s_%s" % [hand, lvl]):
		return "attack_%s_%s" % [hand, lvl]
	return stance_clip(anim, st, "attack_" + lvl)


## The weapon's model file ("" none) and the packed scene of one (no cache: NpcManager prepares them on a worker).
static func path_of(model: int) -> String:
	var i := info(model)
	return "" if i.is_empty() else DIR + "/" + String(i.get("flex_file", i.file))


## A siege weapon's animated copy (pipeline/build_siege_anims.py: skinned, with the client's idle / load / fire /
## walk clips) - "" when it has none (the catapult and trebuchet stand still in the client too).
static func anim_path_of(model: int) -> String:
	var i := info(model)
	return DIR + "/" + String(i.anim_file) if i.has("anim_file") else ""


## The animated siege weapon, standing in the world (its glTF carries its own axes and scale), or null.
static func make_animated(model: int) -> Node3D:
	var path := anim_path_of(model)
	if path == "":
		return null
	if not _scenes.has(path):
		_scenes[path] = pack(path)
	var p: PackedScene = _scenes[path]
	if p == null:
		return null
	var holder := Node3D.new()
	holder.name = "Siege_%d" % model
	holder.add_child(p.instantiate())
	return holder


static func pack(path: String) -> PackedScene:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(ProjectSettings.globalize_path(path), state) != OK:
		return null
	var scene := doc.generate_scene(state)
	var packed := PackedScene.new()
	var ok := packed.pack(scene) == OK
	scene.free()
	return packed if ok else null


## The weapon effect's file for an item effect id ("" none) - weapon_effects.json.
static func effect_path(effect: int) -> String:
	if _effects.is_empty():
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(DIR + "/weapon_effects.json")) \
				if FileAccess.file_exists(DIR + "/weapon_effects.json") else null
		_effects = parsed if parsed is Dictionary else {"_": ""}
	return String(_effects.get(str(effect), ""))


## A new instance of the weapon, already in its holder's NIF space, or null. A flex weapon (flail, chain, whip:
## pipeline/build_flex_weapons.py) is its skinned model with the chain's own clips - "idle" hangs the chain, "combat_idle"
## swings it while the weapon is drawn (drawn = true).
## An item's dye on a weapon / shield / instrument (weapon luster and enamel - DyeItem; owner 2026-10-14: "dye ...
## shows up the color in the client"): the client multiplies the item's texture by its tints.csv colour, as on armour.
## This copy only (the materials are shared by every copy of the model).
static func dye(w: Node3D, color: int) -> void:
	if w == null or color <= 0:
		return
	var c := Fig3Figure.dye(color)
	if c == Color.WHITE:
		return
	for n in w.find_children("*", "MeshInstance3D", true, false):
		var mi := n as MeshInstance3D
		if mi.mesh == null:
			continue
		for i in mi.mesh.get_surface_count():
			var m := mi.get_active_material(i)
			if m is ShaderMaterial:
				var d := (m as ShaderMaterial).duplicate() as ShaderMaterial
				var t = d.get_shader_parameter("tint")
				var base: Color = t if t is Color else Color.WHITE
				d.set_shader_parameter("tint", Color(base.r * c.r, base.g * c.g, base.b * c.b))
				mi.set_surface_override_material(i, d)
			elif m is BaseMaterial3D:
				var s := (m as BaseMaterial3D).duplicate() as BaseMaterial3D
				s.albedo_color = s.albedo_color * c
				mi.set_surface_override_material(i, s)


## left: hung on a left-hand bone (Bip01 L Held / L Shield) - gripped by its NIF's HELD2 node when it has one, as the
## client (game.dll 0x47296a; weapons.json "held2", pipeline/add_weapon_held2.py): the claw greaves' HELD2 is turned
## 180 degrees, so the off-hand claw faces the same way as the main hand's (owner 2026-10-17, the Savage)
static func make(model: int, drawn := false, left := false, sheath := "") -> Node3D:
	var i := info(model)
	if i.is_empty():
		return null
	var path := path_of(model)
	if not _scenes.has(path):
		_scenes[path] = pack(path)
	var p: PackedScene = _scenes[path]
	if p == null:
		return null
	var holder := Node3D.new()
	holder.name = "Weapon_%d" % model
	var w := p.instantiate() as Node3D
	for c in w.find_children("collision*", "", true, false):
		c.queue_free()                                    # the NIF's collidee shapes: not part of the held model
	w.transform = TO_NIF
	if sheath != "" and i.has(sheath + "_frame"):
		# hung by the NIF's own BELT / BACK node, as game.dll sheathes (pipeline/add_sheath_frames.py; owner 2026-10-19:
		# a two-hander hung out across the cloak by its grip)
		var f: Array = i[sheath + "_frame"]
		w.transform = Transform3D(Basis(Vector3(f[0], f[1], f[2]), Vector3(f[3], f[4], f[5]), Vector3(f[6], f[7], f[8])),
				Vector3(f[9], f[10], f[11])) * TO_NIF
	elif left and i.has("held2"):
		var h2: Array = i.held2
		w.transform = Transform3D(Basis(Vector3(h2[0], h2[1], h2[2]), Vector3(h2[3], h2[4], h2[5]), Vector3(h2[6], h2[7], h2[8])),
				Vector3(h2[9], h2[10], h2[11])) * TO_NIF
	elif i.has("held"):
		# a mesh turned inside its NIF ('item' node) that its grip turns back - weapons.json "held" (nif_static.
		# held_frame; owner 2026-10-13: the Bonedancer commander's axe hung rolled 90 degrees on its hip)
		var h: Array = i.held
		w.transform = Transform3D(Basis(Vector3(h[0], h[1], h[2]), Vector3(h[3], h[4], h[5]), Vector3(h[6], h[7], h[8])),
				Vector3(h[9], h[10], h[11])) * TO_NIF
	holder.add_child(w)
	if i.has("flex_file"):
		var players := w.find_children("*", "AnimationPlayer", true, false)
		if players.size() > 0:
			var ap := players[0] as AnimationPlayer
			for clip in ap.get_animation_list():
				ap.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if clip in ["idle", "combat_idle"] else Animation.LOOP_NONE
			var want := "combat_idle" if drawn and ap.has_animation("combat_idle") else "idle"
			if ap.has_animation(want):
				ap.play(want)
	return holder


## A shield's guild emblem (owner 2026-10-17, the server's gamebot guilds): an emblem shield (weapons.json "emblem" = its
## surface texture, pipeline/build_weapons.py) drawn as the client - Emblems.shield_material. 0: as it is.
static func set_emblem(holder: Node3D, model: int, emblem: int) -> void:
	var surface := String(info(model).get("emblem", ""))
	if emblem <= 0 or surface == "" or holder == null:
		return
	var m := Emblems.shield_material("%s/textures/x4plus/%s.png" % [DIR, surface], emblem)
	for mi in holder.find_children("*", "MeshInstance3D", true, false):
		var mesh := (mi as MeshInstance3D).mesh
		if mesh:
			for i in mesh.get_surface_count():
				(mi as MeshInstance3D).set_surface_override_material(i, m)


## A weapon's effect (an item's effect number: spweapons.csv -> its effect NIF, pipeline/build_weapon_effects.py -
## the period glows, flames, frost) on a holder from make(): drawn in the weapon's own frame, looping. Null when the
## number has none (owner 2026-10-13: the abomination's two-hander, effect 30, glows red).
static var _effects := {}


static func add_effect(holder: Node3D, effect: int) -> Node3D:
	if holder == null or effect <= 0 or holder.get_child_count() == 0:
		return null
	var path := effect_path(effect)
	if path == "":
		return null
	var fx := NifScene.copy_of(path)
	if fx == null:
		return null
	fx.name = "WeaponEffect"
	fx.transform = (holder.get_child(0) as Node3D).transform   # the weapon's own frame (TO_NIF / its grip)
	# the glow rides the blade: its particles live in the weapon's space (owner 2026-10-20: an abomination's and the
	# gamebots' weapon glows trailed far behind the sword - emitted in world space, each particle stayed where it was
	# born for its whole life while the blade swung on)
	for ps in fx.find_children("*", "GPUParticles3D", true, false):
		(ps as GPUParticles3D).local_coords = true
	holder.add_child(fx)
	AnimLod.keep_on_main(fx)                              # (its particles and timeline: the main thread)
	NifParticles.fit_to_scale(fx)
	return fx


## Hang a weapon / shield model on one of a figure's attach bones; returns the BoneAttachment3D, or null.
static func attach(root: Node, bone: String, model: int) -> Node3D:
	var found := root.find_children("*", "Skeleton3D", true, false)
	if found.is_empty():
		return null
	var skel := found[0] as Skeleton3D
	if skel.find_bone(bone) < 0:
		return null
	var w := make(model)
	if w == null:
		return null
	var at := BoneAttachment3D.new()
	at.bone_name = bone
	skel.add_child(at)
	at.add_child(w)
	return at


## A blow with a flex weapon: its chain plays the swing's own clip (items.csv Atk High / Atk Mid, or the style's
## CS clip), then goes back to its combat idle. body_clip: the wielder's clip ("attack_high", "style_1hc_3" ...);
## style: true for a styled blow. Every flex weapon hanging under root takes part.
static func flex_swing(root: Node, body_clip: String, style := false) -> void:
	if root == null:
		return
	for holder in root.find_children("Weapon_*", "", true, false):
		for ap in holder.find_children("*", "AnimationPlayer", true, false):
			var p := ap as AnimationPlayer
			var clip := _flex_clip(p, body_clip, style)
			if clip == "":
				continue
			p.play(clip)
			if p.has_animation("combat_idle"):
				p.queue("combat_idle")


## The chain clip for a body clip: styles by their number to the nearest of CS01 / 03 / 04 / 06 / 07, a high attack
## to Atk High, the medium and low ones to Atk Mid.
static func _flex_clip(p: AnimationPlayer, body_clip: String, style: bool) -> String:
	if style:
		var m := RegEx.create_from_string("([0-9]+)$").search(body_clip)
		var n := int(m.get_string(1)) if m else 1
		var best := ""
		var best_d := 99
		for k in [1, 3, 4, 6, 7]:
			if p.has_animation("style_%d" % k) and absi(k - n) < best_d:
				best_d = absi(k - n)
				best = "style_%d" % k
		if best != "":
			return best
	var high := body_clip.contains("high")
	for c in (["attack_high", "attack_low"] if high else ["attack_low", "attack_high"]):
		if p.has_animation(c):
			return c
	return ""


## An objects.csv model placed in the world (Godot axes, metres) rather than on a bone - gravestones, dropped items.
static func make_world(model: int) -> Node3D:
	var holder := make(model)
	if holder == null:
		return null
	for c in holder.get_children():
		(c as Node3D).transform = Transform3D.IDENTITY
	return holder
