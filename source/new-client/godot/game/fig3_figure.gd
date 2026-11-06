class_name Fig3Figure
extends Node3D
## A Catacombs (fig3) player-race figure with the client's character-creation options (pipeline/build_races.py):
## face, eye colour, skin tone, hair style + colour, tattoo, size and the face sliders, applied the client's way.
##   var f := Fig3Figure.new(); add_child(f); f.load_figure("cel_m"); f.apply({"face": 2, "hair": 3, ...})
## A look is a Dictionary; every key is optional (index 0 = the first option, as listed in figure.json):
##   face, eyes, skin, hair (style), hair_color, tattoo, size (0 Short, 1 Average, 2 Tall),
##   sliders (Array of 0..8 per face slider, 4 = neutral), mood (0..8)

const ROOT := "res://content/characters"
const PARTS := ["head", "body", "lbody", "legs", "boots", "arms", "gloves"]

var meta := {}
var model: Node3D
var anim: AnimationPlayer
var look := {}
var _meshes := {}                   # part name -> [MeshInstance3D] (one per shape)
var _textures := {}                 # basename -> Texture2D
var _texture_dir := ""
static var _shaders := {}
static var _scenes := {}                    # race prefix -> PackedScene of its figure
static var _tex_cache := {}                 # textures shared by every figure


## Which clips loop: movement, idles, the cast / aim holds and sitting; everything else (attacks, styles, parries,
## evades, blocks, flinch, death, fire / ready, cast start / end, sit down / stand up) plays once and holds.
const LOOPING := ["ladder", "idle", "walk", "run", "walk_back", "strafe_left", "strafe_right", "swim", "cast_mid", "sit_idle",
	"bow_aim", "xbow_aim", "xbow_idle", "throw_aim", "fly"]


## The loaders below touch no cache: NpcManager's view preparation runs them on a worker thread ahead of a view
## (owner 2026-10-14: a bot's figure, armour textures and helm parsed on the main thread hitched 100-200 ms each
## when the server streamed new ones in), and the main thread keeps what they made.

## A race's figure, packed: tools/pack_scenes.gd's binary copy (~3x faster than the glTF) or the glTF itself.
static func pack_figure(prefix: String) -> PackedScene:
	var dir := "%s/races/%s" % [ROOT, prefix]
	var scn := dir + "/figure.scn"
	if FileAccess.file_exists(scn) and FileAccess.get_modified_time(scn) >= FileAccess.get_modified_time(dir + "/figure.gltf"):
		var ps := ResourceLoader.load(scn, "PackedScene") as PackedScene
		if ps:
			return ps
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	state.base_path = ProjectSettings.globalize_path(dir)
	if doc.append_from_file(dir + "/figure.gltf", state) != OK:
		return null
	GpuJobs.ready_materials(Array(state.get_materials()))   # (made on the main thread: NPC views prepare figures on a worker)
	var root := doc.generate_scene(state)
	var packed := PackedScene.new()
	packed.pack(root)
	root.free()                                        # only the template: a loose node is never freed for us
	return packed


static func texture_dir(texture_set := "x4plus") -> String:
	var d := "%s/fig3_textures/%s" % [ROOT, texture_set]
	return d if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(d)) else ROOT + "/fig3_textures/original"


## A figure texture: the GPU-compressed copy (tools/compress_textures.gd: BC7 with mipmaps, a quarter of the memory, no
## decode; owner 2026-10-10: the game crawled for a minute after loading while the 4x skins were decoded), else the png.
static func load_texture(dir: String, key: String) -> Texture2D:
	var bc := "%s_bc/%s.res" % [dir, key]
	if ResourceLoader.exists(bc):
		var cimg := ResourceLoader.load(bc, "Image") as Image
		if cimg:
			return ImageTexture.create_from_image(cimg)
	var path := "%s/%s.png" % [dir, key]
	if not FileAccess.file_exists(path):
		path = "%s/fig3_textures/original/%s.png" % [ROOT, key]
	var img := Image.load_from_file(path)
	if img == null:
		return null
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


static func pack_helm(path: String) -> PackedScene:
	var doc := GLTFDocument.new()
	var st := GLTFState.new()
	if doc.append_from_file(ProjectSettings.globalize_path(path), st) != OK:
		return null
	CacheTrim.share_textures(st, path)                   # (one copy of each texture file across all helms)
	GpuJobs.ready_materials(Array(st.get_materials()))  # (made on the main thread: helms are prepared on a worker)
	var scene := doc.generate_scene(st)
	var packed := PackedScene.new()
	var ok := packed.pack(scene) == OK
	scene.free()
	return packed if ok else null


## The helm file for a worn helm item on this race prefix ("" none) - helms.json, read once.
static func helm_path(model: int, prefix: String) -> String:
	if _helms.is_empty():
		var t = JSON.parse_string(FileAccess.get_file_as_string("res://content/helms/helms.json")) \
				if FileAccess.file_exists("res://content/helms/helms.json") else null
		_helms = t.get("models", {}) if t is Dictionary else {"_": {}}
	var e: Dictionary = _helms.get(str(model), {}).get(prefix, {})
	return "res://content/helms/" + String(e.file) if e.has("file") else ""


## An NPC's look from its id (the database records no faces / hair for NPCs): the same for a given id every time.
static func npc_look(id: String, meta: Dictionary) -> Dictionary:
	var h := hash(id)
	var count := func(key: String, look: Dictionary, salt: int) -> int:
		var n := option_count_of(meta, look, key)
		return 0 if n <= 0 else absi(h / salt) % n
	var look := {"face": count.call("face", {}, 1), "skin": 0, "hair": count.call("hair", {}, 13),
			"eyes": count.call("eyes", {}, 31), "hair_color": 0}
	look["hair_color"] = count.call("hair_color", look.duplicate(), 97)
	return look


## The texture keys a figure with this look and armour draws (apply / _apply_armor / _apply_cloak) - for preparing.
static func texture_keys(meta: Dictionary, look: Dictionary, parts: Dictionary) -> Array:
	var out := []
	var faces: Array = meta.get("faces", [])
	if faces.size() > 0:
		out.append(String(faces[clampi(int(look.get("face", 0)), 0, faces.size() - 1)].tex))
	var eyes: Array = meta.get("eyes", [])
	if eyes.size() > 0:
		out.append(String(eyes[clampi(int(look.get("eyes", 0)), 0, eyes.size() - 1)]))
	var clothes: Dictionary = meta.get("clothes", {})
	for part in clothes:
		out.append(String(clothes[part].get("tex", "")))
	var styles: Array = meta.get("hair_styles", [])
	if styles.size() > 0:
		var colours: Array = styles[clampi(int(look.get("hair", 0)), 0, styles.size() - 1)].colours
		if colours.size() > 0:
			out.append(String(colours[clampi(int(look.get("hair_color", 0)), 0, colours.size() - 1)].tex))
	for k in parts:
		var want: Array = parts[k]
		if want.size() > 1:
			out.append(String(want[1]))
	return out.filter(func(x): return x != "").map(func(x): return String(x).to_lower())


static func loops(clip: String) -> bool:
	# the spell casting styles' middle clip is the hold-while-casting loop (anims.csv "... Mid")
	return clip in LOOPING or clip.begins_with("combat_idle") or (clip.begins_with("cast_") and clip.ends_with("_mid"))

func load_figure(prefix: String, texture_set := "x4plus") -> bool:
	var dir := "%s/races/%s" % [ROOT, prefix]
	meta = NpcManager._metas[prefix] if NpcManager._metas.has(prefix) and not (NpcManager._metas[prefix] as Dictionary).is_empty() 			else JSON.parse_string(FileAccess.get_file_as_string(dir + "/figure.json"))   # (read only: shared)
	_texture_dir = texture_dir(texture_set)
	# one parsed glTF per race: NPCs of the same race share it (a town has dozens)
	if not _scenes.has(prefix) or _scenes[prefix] == null:
		_scenes[prefix] = pack_figure(prefix)
		if _scenes[prefix] == null:
			push_warning("figure missing: " + prefix)
			return false
	model = (_scenes[prefix] as PackedScene).instantiate() as Node3D
	CacheTrim.track("figure", prefix, model)
	add_child(model)
	for node in model.find_children("*", "MeshInstance3D", true, false):
		var part := String(node.name).get_slice("__", 0)    # a part's extra shapes are named <part>__1, __2 ...
		if not _meshes.has(part):
			_meshes[part] = []
		_meshes[part].append(node)
	var players := model.find_children("*", "AnimationPlayer", true, false)
	if players.size() > 0:
		anim = players[0]
		for clip in anim.get_animation_list():
			anim.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if loops(clip) else Animation.LOOP_NONE
		add_rest_reset(anim)
		if anim.has_animation("idle"):
			anim.play("idle")
	apply({})
	_apply_cloak()
	return true


## Merge `changes` into the current look and redraw.
func apply(changes: Dictionary) -> void:
	look.merge(changes, true)
	var tones: Array = meta.get("skin_tones", [[1, 1, 1]])
	var tone: Array = tones[clampi(int(look.get("skin", 0)), 0, tones.size() - 1)]
	var skin := skin_tint(tone)
	var tint_parts: Dictionary = meta.get("skin_tint_parts", {})
	# head: face texture x skin tone, eye colour multiplied, tattoo
	var faces: Array = meta.get("faces", [])
	var head := _part("head")
	if head and faces.size() > 0:
		var m := _material(false)
		m.set_shader_parameter("albedo_tex", _tex(faces[clampi(int(look.get("face", 0)), 0, faces.size() - 1)].tex))
		m.set_shader_parameter("tint", skin)
		m.set_shader_parameter("tint_mode", 1)
		var eyes: Array = meta.get("eyes", [])
		if eyes.size() > 0:
			m.set_shader_parameter("eye_tex", _tex(eyes[clampi(int(look.get("eyes", 0)), 0, eyes.size() - 1)]))
			m.set_shader_parameter("has_eye", true)
		var tattoos: Array = meta.get("tattoos", [])
		var ti := int(look.get("tattoo", 0))
		var decal: String = tattoos[ti].tex if ti > 0 and ti < tattoos.size() else ""
		m.set_shader_parameter("has_decal", decal != "")
		if decal != "":
			m.set_shader_parameter("decal_tex", _tex(decal))
		for mi in _parts("head"):
			mi.material_override = m
	# starter clothes: skin tone only through alpha-masked ('a') textures on the parts the race tints
	for part in ["body", "lbody", "legs", "boots", "arms", "gloves"]:
		var c: Dictionary = meta.get("clothes", {}).get(part, {})
		if _parts(part).is_empty() or c.is_empty():
			continue
		var m := _material(false)
		m.set_shader_parameter("albedo_tex", _tex(String(c.tex)))
		var tinted := bool(tint_parts.get(part, false)) and String(c.get("chan", "")) == "a"
		m.set_shader_parameter("tint", skin)
		m.set_shader_parameter("tint_mode", 2 if tinted else 0)
		for mi in _parts(part):
			mi.material_override = m
	# hair: one style node visible, its colour = texture x tint
	var styles: Array = meta.get("hair_styles", [])
	var si := clampi(int(look.get("hair", 0)), 0, maxi(styles.size() - 1, 0))
	for i in styles.size():
		var node = styles[i].node
		if node == null or not _meshes.has(node):
			continue
		for mi in _parts(node):
			mi.visible = i == si and not _hair_covered()
		if i == si:
			var colours: Array = styles[i].colours
			if colours.size() > 0:
				var col: Dictionary = colours[clampi(int(look.get("hair_color", 0)), 0, colours.size() - 1)]
				var m := _material(true)
				m.set_shader_parameter("albedo_tex", _tex(String(col.tex)))
				m.set_shader_parameter("tint", Color(col.tint[0], col.tint[1], col.tint[2]))
				m.set_shader_parameter("tint_mode", 1)
				for mi in _parts(node):
					mi.material_override = m
	# face sliders: 0..8, 4 neutral; below 4 blends the low target, above 4 the high one
	if head:
		var values: Array = look.get("sliders", [])
		var sliders: Array = meta.get("sliders", [])
		for i in sliders.size():
			var v := float(values[i]) if i < values.size() else 4.0
			_blend(head, String(sliders[i].low), clampf((4.0 - v) / 4.0, 0.0, 1.0))
			_blend(head, String(sliders[i].high), clampf((v - 4.0) / 4.0, 0.0, 1.0))
	# size: the race's Catacombs scale x Short 0.95 / Average 1.0 / Tall 1.05
	var sizes := [0.95, 1.0, 1.05]
	scale = Vector3.ONE * float(meta.get("scale", 1.0)) * float(sizes[clampi(int(look.get("size", 1)), 0, 2)])
	_apply_armor()                                         # worn armour over the starter clothes; hides unused variants


## Worn armour (content/characters/armor.json from pipeline/build_armor.py): {slot: [variant, texture]} for body /
## legs / boots / arms / gloves. The slot's mesh variant shows (node "<slot>" is the default variant, "<slot>_v<n>"
## the others), the rest of that slot hides, and the armour texture replaces the starter clothes'. Slots not given
## go back to the race's starter clothes.
func set_armor(parts: Dictionary, tints: Dictionary = {}) -> void:
	armor = parts.duplicate()
	armor_tints = tints.duplicate()
	_apply_armor()


var armor := {}                             # the worn armour parts (set_armor)
var hood_up := false                        # /hood: the cloak's hood up (the server's hood state for this figure)
var cloak_hidden := false                   # the server's hidden-cloak flag (inventory / equipment packets, bit 0x01)
var cloak_emblem := 0                       # the worn cloak's guild emblem (Emblems), 0 none - set before set_armor
var helm_hidden := false                    # the hidden-helm flag (bit 0x02)
var _helm: Node3D
var _helm_hides_hair := false
var _helm_hair := 0                         # the helm's 'Cata Helm Hair' type: the hair takes its helm morph (-1/-2/-3)
static var _helms := {}
static var _helm_scenes := {}


## The worn cloak on the race's cloak meshes (build_races: cloak_1 hood down, cloak_2 hood up, cloak_3 hoodless):
## the item's Catacombs cloak texture with its dye; none shown without a cloak or when it is hidden.
func _apply_cloak() -> void:
	var cloak: Array = armor.get("cloak", [])
	var show := cloak.size() > 1 and not cloak_hidden
	var have := _cloak_meshes()
	var want := 2 if hood_up and have.has(2) else (1 if have.has(1) else (int(have[0]) if have.size() > 0 else 0))
	for k in have:
		for mi in _parts("cloak_%d" % int(k)):
			mi.visible = show and int(k) == want
	if show and want > 0:
		# single sided, as the client draws cloaks (owner 2026-10-11: drawn double sided its inner and outer layers
		# fought and streaked it)
		var m := _material(false, true)
		m.set_shader_parameter("albedo_tex", _tex(String(cloak[1])))
		var dyed: Color = armor_tints.get("cloak", Color.WHITE)
		m.set_shader_parameter("tint", dyed)
		m.set_shader_parameter("tint_mode", 1 if dyed != Color.WHITE else 0)
		m.set_shader_parameter("view_pull", CLOAK_PULL)
		# a guild emblem in place of the dye (the server sends one or the other; owner 2026-10-17: guild gamebots)
		m.set_shader_parameter("has_emblem", cloak_emblem > 0)
		if cloak_emblem > 0:
			m.set_shader_parameter("pattern_tex", Emblems.pattern_texture(cloak_emblem))
			var logo := Emblems.logo_texture(Emblems.decode(cloak_emblem).logo)
			m.set_shader_parameter("logo_tex", logo)
			m.set_shader_parameter("has_logo", logo != null)
		for mi in _parts("cloak_%d" % want):
			mi.material_override = m
	_apply_full_helm()
	_refresh_hair()


const CLOAK_PULL := 1.0                     # NIF units (the figure shader's view_pull)


## Hood up (with the cloak showing) or a helm that covers the hair: the hair isn't drawn.
func _hair_covered() -> bool:
	return _hood_covers() or (_helm != null and _helm_hides_hair) or _full_helm > 0


## The race's cloak meshes as whole numbers: figure.json's list parses as floats, and has(2) never matched 2.0 - the
## hood-up mesh was never drawn (owner 2026-10-15: "/hood doesn't put a hood over the player's head", every race).
func _cloak_meshes() -> Array:
	return (meta.get("cloaks", []) as Array).map(func(x): return int(x))


## The hood up over a showing cloak: as the old client, only the face shows - no hair, no helm (owner 2026-10-15).
func _hood_covers() -> bool:
	var cloak_shown := (armor.get("cloak", []) as Array).size() > 1 and not cloak_hidden
	return hood_up and cloak_shown and _cloak_meshes().has(2)


var _full_helm := 0                        # the worn full helm part (fullhelm_<n>), 0 none
var _full_helm_tex := ""


## The race's full helm parts (figure.json full_helms): all hidden but the worn one, which wears the item's texture;
## the head is drawn only without one (the helm stands in for it).
func _apply_full_helm() -> void:
	var have: Array = (meta.get("full_helms", []) as Array).map(func(x): return int(x))
	var n := _full_helm if have.has(_full_helm) else 0
	for k in have:
		for mi in _parts("fullhelm_%d" % k):
			mi.visible = k == n
	if n > 0 and _full_helm_tex != "":
		var m := _material(false)
		m.set_shader_parameter("albedo_tex", _tex(_full_helm_tex))
		m.set_shader_parameter("tint", Color.WHITE)
		m.set_shader_parameter("tint_mode", 0)
		for mi in _parts("fullhelm_%d" % n):
			mi.material_override = m
	for mi in _parts("head"):
		mi.visible = n == 0


func _refresh_hair() -> void:
	var styles: Array = meta.get("hair_styles", [])
	var si := clampi(int(look.get("hair", 0)), 0, maxi(styles.size() - 1, 0))
	for i in styles.size():
		var node = styles[i].node
		if node == null or not _meshes.has(node):
			continue
		for mi in _parts(node):
			mi.visible = i == si and not _hair_covered()
			# under a helm the hair takes that helm type's morph (flattened so it doesn't poke through), else none.
			# Type 1 is the circlets' (the crafted hat06 circlets, the Crown of Zahur - nothing else uses it): a band
			# that shows the hair, which stays as it is (owner 2026-10-14: circlets hid the hair - its morph tore the
			# hair open over the band)
			var morphs: Dictionary = meta.get("helm_hair", {}).get(String(node), {})
			for k in morphs:
				_blend(mi, String(morphs[k]), 1.0 if _helm != null and int(k) == _helm_hair and _helm_hair != 1 else 0.0)


## The helm / hat for this race and gender (content/helms: the item's Catacombs helm, its texture and scale) on the
## head bone; model 0 or hidden takes it off.
func set_helm(model: int, hidden := false, color := 0) -> void:
	if _helm and is_instance_valid(_helm):
		_helm.queue_free()
	_helm = null
	_helm_hides_hair = false
	_helm_hair = 0
	_full_helm = 0
	helm_path(0, "")                                           # (helms.json read once)
	var top: Dictionary = _helms.get(str(model), {}) if model > 0 else {}
	if model > 0 and not hidden and not _hood_covers() and top.has("full"):
		# a head-swap helm (the Dragonsworn helms, the Dubh Sluagh Cairnwarden's Helm): one of the race's full helm
		# parts in place of the head, in the item's own texture (helms.json "full" / "tex"; build_races full_helms)
		_full_helm = int(top.full)
		_full_helm_tex = String(top.get("tex", ""))
	_apply_full_helm()
	if model > 0 and not hidden and not _hood_covers() and _full_helm == 0:     # (a raised hood hides the helm)
		var e: Dictionary = _helms.get(str(model), {}).get(String(meta.get("prefix", "")), {})
		var skel: Skeleton3D = null
		var sk := find_children("*", "Skeleton3D", true, false)
		if sk.size() > 0:
			skel = sk[0]
		# the skeleton's own helm attach node (Bip01 Helmet: turned so helm NIFs sit upright on the head), else the head
		var bone := "Bip01 Helmet" if skel and skel.find_bone("Bip01 Helmet") >= 0 else "Bip01 Head"
		if not e.is_empty() and skel and skel.find_bone(bone) >= 0:
			var path := "res://content/helms/" + String(e.file)
			if not _helm_scenes.has(path):
				_helm_scenes[path] = pack_helm(path)
			var ps: PackedScene = _helm_scenes[path]
			if ps:
				var at := BoneAttachment3D.new()
				at.bone_name = bone
				skel.add_child(at)
				var h := ps.instantiate() as Node3D
				CacheTrim.track("helm", path, h)
				for c in h.find_children("collision*", "", true, false):
					c.queue_free()
				# the mesh as the NIF places it under the helmet node, shape offset included (owner 2026-10-11: undoing the
				# offset floated the hat a hand above the head - the old client keeps it)
				h.transform = WeaponModels.TO_NIF.scaled_local(Vector3.ONE * float(e.get("scale", 100)) / 100.0)
				at.add_child(h)
				var dyed := dye(color)                              # the item's dye on the helm (multiplied)
				for mi in h.find_children("*", "MeshInstance3D", true, false):
					var mesh_i := mi as MeshInstance3D
					mesh_i.layers = 3
					if dyed != Color.WHITE and mesh_i.mesh:
						for si in mesh_i.mesh.get_surface_count():
							var mat := mesh_i.get_active_material(si)
							if mat is BaseMaterial3D:                 # (one shared dyed copy: WeaponModels.dyed_material)
								mesh_i.set_surface_override_material(si, WeaponModels.dyed_material(mat, dyed))
				_helm = at
				_helm_hair = int(e.get("hair", 0))
	_refresh_hair()
var armor_tints := {}                       # part -> Color: the item's dye (tints.csv, the client multiplies the texture)
static var _dyes := {}


## An item's Color as the client draws it on a Catacombs figure (tints.csv Cata colour); white for none / unknown.
static func dye(id: int) -> Color:
	if id <= 0:
		return Color.WHITE
	if _dyes.is_empty():
		var t = JSON.parse_string(FileAccess.get_file_as_string("res://content/rules/dye_tints.json")) 				if FileAccess.file_exists("res://content/rules/dye_tints.json") else null
		_dyes = t if t is Dictionary else {"_": [1, 1, 1]}
	var c: Array = _dyes.get(str(id), [1, 1, 1])
	return Color(float(c[0]), float(c[1]), float(c[2]))


func _apply_armor() -> void:
	var parts := armor
	var variants: Dictionary = meta.get("variants", {})
	var clothes: Dictionary = meta.get("clothes", {})
	var robe := _robe_body()
	for slot in ["body", "legs", "boots", "arms", "gloves"]:
		var default_v := int(clothes.get(slot, {}).get("variant", 1))
		var want: Array = parts.get(slot, [])
		var v := int(want[0]) if want.size() > 0 else default_v
		# under a robe (game.dll 0x424817): body mesh 7 hides the legs and arms and wears boots and gloves mesh 3;
		# body mesh 8 hides the arms and wears gloves mesh 3 (variant 35, past the 25 fig3map columns: not drawn)
		if robe > 0 and (slot == "arms" or (slot == "legs" and robe == 7)):
			v = 0
		elif (slot == "gloves" and robe > 0) or (slot == "boots" and robe == 7):
			v = 3
		if v > 0 and not _have(slot).has(v):
			v = default_v                                    # this race lacks that mesh: keep the default shape
		for k in (variants.get(slot, []) as Array):
			var node: String = slot if int(k) == default_v else "%s_v%d" % [slot, int(k)]
			for mi in _parts(node):
				mi.visible = int(k) == v
		var node_name: String = slot if v == default_v else "%s_v%d" % [slot, v]
		var tex := String(want[1]) if want.size() > 1 else String(clothes.get(slot, {}).get("tex", ""))
		if tex == "" or v == 0:
			continue
		var m := _material(false)
		m.set_shader_parameter("albedo_tex", _tex(tex))
		# the starter clothes keep the skin tone on their bare skin (Tint Chan 'a' on the parts the race tints): this
		# ran after apply() and drew them untinted - dark-skinned figures had pale arms, hands and Kobold legs under a
		# tinted head (owner 2026-10-11, "red-faced Celts"); real armour has no skin to tint
		var starter := want.size() < 2
		var tinted := starter and bool((meta.get("skin_tint_parts", {}) as Dictionary).get(slot, false)) 				and String(clothes.get(slot, {}).get("chan", "")) == "a"
		m.set_shader_parameter("tint", _skin() if tinted else Color.WHITE)
		m.set_shader_parameter("tint_mode", 2 if tinted else 0)
		if not starter and armor_tints.has(slot) and armor_tints[slot] != Color.WHITE:
			m.set_shader_parameter("tint", armor_tints[slot])   # the item's dye
			m.set_shader_parameter("tint_mode", 1)
		for mi in _parts(node_name):
			mi.material_override = m
	_apply_kilt()
	_apply_cloak()


## The worn robe body mesh: 7 (a robe - armor.json from build_armor.robe_bodies) or 8, as the race has it; 0 otherwise.
func _robe_body() -> int:
	var want: Array = armor.get("body", [])
	var v := int(want[0]) if want.size() > 0 else 0
	return v if (v == 7 or v == 8) and _have("body").has(v) else 0


## The race's mesh variants of a part as whole numbers: figure.json's lists parse as floats, and has(5) never matched
## 5.0 - every armour piece fell back to mesh 1 (owner 2026-10-19, the troll's robe on the vest body; as _cloak_meshes).
func _have(slot: String) -> Array:
	return (meta.get("variants", {}).get(slot, []) as Array).map(func(x): return int(x))


## A skin tone as the client draws it (owner 2026-10-11, the old client's darkest Celt is a tan and its darkest Norse
## a greyish tan - not the deep red-brown of the tone colour multiplied in full): the fig3tints colour is the skin
## material's diffuse under untinted ambient light, so on the face it comes out half way between white and the tone.
const SKIN_TINT_STRENGTH := 0.5


static func skin_tint(tone: Array) -> Color:
	return Color.WHITE.lerp(Color(float(tone[0]), float(tone[1]), float(tone[2])), SKIN_TINT_STRENGTH)


## The look's skin tone (the race's fig3skincolormap tints).
func _skin() -> Color:
	var tones: Array = meta.get("skin_tones", [[1, 1, 1]])
	var tone: Array = tones[clampi(int(look.get("skin", 0)), 0, tones.size() - 1)]
	return skin_tint(tone)


## The Highlander kilt (lbody, one mesh per body mesh): only the one matching the body worn shows, always in the race's
## own plaid - the Catacombs armour sets carry no lower-body texture (pskins "Cata Chain" etc.), so the client keeps
## the kilt texture. Showing every kilt variant at once gave white, half-plaid skirts (owner 2026-10-10).
func _apply_kilt() -> void:
	var kilts: Array = meta.get("variants", {}).get("lbody", [])
	var kilt: Dictionary = meta.get("clothes", {}).get("lbody", {})
	if kilts.is_empty() or kilt.is_empty():
		return
	var body_default := int(meta.get("clothes", {}).get("body", {}).get("variant", 1))
	var want: Array = armor.get("body", [])
	var bv := int(want[0]) if want.size() > 0 and _have("body").has(int(want[0])) else body_default
	var kilt_default := int(kilt.get("variant", 1))
	var kv := bv if _have("lbody").has(bv) else kilt_default
	if _robe_body() > 0:
		kv = 0                                                # no kilt under a robe (0x424817: lower body variant 35)
	var m := _material(false)
	m.set_shader_parameter("albedo_tex", _tex(String(kilt.get("tex", ""))))
	m.set_shader_parameter("tint", Color.WHITE)
	m.set_shader_parameter("tint_mode", 0)
	for k in kilts:
		var node: String = "lbody" if int(k) == kilt_default else "lbody_v%d" % int(k)
		for mi in _parts(node):
			mi.visible = int(k) == kv
			if int(k) == kv:
				mi.material_override = m


static var _armor := {}


## The armour parts an item model gives a figure of this gender, or {} (pipeline/build_armor.py).
static func armor_parts(model: int, female: bool) -> Dictionary:
	if _armor.is_empty():
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(ROOT + "/armor.json"))
		_armor = parsed.get("models", {}) if parsed is Dictionary else {"_": {}}
	return _armor.get(str(model), {}).get("f" if female else "m", {})


## A worn item's armour parts on the mesh the client picks for it (game.dll 0x41b5c1, from the equipment packet): a
## robe's body mesh 7 (armor.json), arms and legs always mesh 1, any other piece - chest, gloves, boots - the item's
## extension (0 = mesh 1). The textures stay armor.json's.
static func item_armor(model: int, extension: int, female: bool) -> Dictionary:
	var out := {}
	var parts := armor_parts(model, female)
	for k in parts:
		var p: Array = (parts[k] as Array).duplicate()
		if p.size() > 0 and k in ["body", "gloves", "boots"] and not (k == "body" and int(p[0]) == 7):
			p[0] = maxi(1, extension)
		elif p.size() > 0 and k in ["arms", "legs"]:
			p[0] = 1
		out[k] = p
	return out


## Every animated character (player, NPC, monster, bot) gets this: a "RESET" animation holding every bone at its
## rest pose. The deterministic mixer fills any bone the playing clip doesn't key from RESET, so a bone never keeps
## the previous clip's pose. Needed because Godot's glTF import drops one-key tracks equal to the rest pose - the
## client's idle holds Bip01 Pelvis that way, and the spawn jump's 3-degree pelvis stuck through idle (the owner's
## tilted player, 2026-10-09; WORLD IMPORT LESSONS.md).
static func add_rest_reset(anim: AnimationPlayer) -> void:
	var root := anim.get_node_or_null(anim.root_node)
	if root == null:
		return
	var lib := anim.get_animation_library("")
	if lib == null:
		return
	# every copy of a figure shares its scene's animation library: the first copy adds RESET, the rest only need the
	# deterministic mixer - they returned before it was set, so every figure but the first of its race kept the run
	# clip's cloak bones (the idle doesn't key 'cloak control02') and stopped with the cloak flowing out behind
	# (owner 2026-10-16: bots and NPCs, never your own character)
	anim.deterministic = true
	if lib.has_animation("RESET"):
		return
	var reset := Animation.new()
	reset.length = 0.001
	for sk in root.find_children("*", "Skeleton3D", true, false):
		var skel := sk as Skeleton3D
		var base := String(root.get_path_to(skel))
		for i in skel.get_bone_count():
			var rest := skel.get_bone_rest(i)
			var path := NodePath("%s:%s" % [base, skel.get_bone_name(i)])
			var t := reset.add_track(Animation.TYPE_ROTATION_3D)
			reset.track_set_path(t, path)
			reset.rotation_track_insert_key(t, 0.0, rest.basis.get_rotation_quaternion())
			t = reset.add_track(Animation.TYPE_POSITION_3D)
			reset.track_set_path(t, path)
			reset.position_track_insert_key(t, 0.0, rest.origin)
			t = reset.add_track(Animation.TYPE_SCALE_3D)
			reset.track_set_path(t, path)
			reset.scale_track_insert_key(t, 0.0, rest.basis.get_scale())
	lib.add_animation("RESET", reset)
	anim.deterministic = true


## Option counts for the customize screen.
func option_count(key: String) -> int:
	return option_count_of(meta, look, key)


## The same counts from a figure's meta (figure.json) and a look, without a figure (CharacterFactory: bots' looks).
static func option_count_of(meta: Dictionary, look: Dictionary, key: String) -> int:
	match key:
		"face": return meta.get("faces", []).size()
		"eyes": return meta.get("eyes", []).size()
		"skin": return meta.get("skin_tones", []).size()
		"hair": return meta.get("hair_styles", []).size()
		"hair_color":
			var styles: Array = meta.get("hair_styles", [])
			return styles[clampi(int(look.get("hair", 0)), 0, styles.size() - 1)].colours.size() if styles.size() > 0 else 0
		"tattoo": return meta.get("tattoos", []).size()
		"size": return 3
	return 0


func play(clip: String) -> void:
	if anim and anim.has_animation(clip):
		anim.play(clip, 0.15)


func _part(part_name: String) -> MeshInstance3D:
	var list: Array = _meshes.get(part_name, [])
	return list[0] if list.size() > 0 else null


func _parts(part_name: String) -> Array:
	return _meshes.get(part_name, [])


func _blend(mi: MeshInstance3D, target: String, w: float) -> void:
	if target == "":
		return
	var i := mi.find_blend_shape_by_name(target)
	if i >= 0:
		mi.set_blend_shape_value(i, w)


func _tex(base: String) -> Texture2D:
	var key := base.to_lower()
	if _tex_cache.has(_texture_dir + key):
		CacheTrim.touch("figtex", _texture_dir + key)
		_textures[key] = _tex_cache[_texture_dir + key]          # (held by this figure: CacheTrim keeps it while it lives)
		return _textures[key]
	if not _textures.has(key):
		_textures[key] = load_texture(_texture_dir, key)
		if _textures[key] == null:
			push_warning("missing figure texture %s/%s" % [_texture_dir, key])
		_tex_cache[_texture_dir + key] = _textures[key]
	return _textures[key]


static func _material(cutout: bool, single_sided := false) -> ShaderMaterial:
	var key := ("cutout" if cutout else "solid") + ("_cull" if single_sided else "")
	if not _shaders.has(key):
		var s := Shader.new()
		var code := SHADER
		if single_sided:                                     # (cloaks: fig3parts 'Double Sided' empty - the client culls)
			code = code.replace("cull_disabled", "cull_back")
		if cutout:
			code = code.replace("//CUTOUT", "ALPHA = c.a;\n\tALPHA_SCISSOR_THRESHOLD = 0.5;")
		s.code = code
		_shaders[key] = s
	var m := ShaderMaterial.new()
	m.shader = _shaders[key]
	return m


# Catacombs (fig3) parts, the client's way (pipeline/build_races.py):
#   tint_mode 0 none, 1 whole texture x tint (head: skin tone; hair: the colour's tint),
#   2 tint the bare skin (skins.csv Tint Chan 'a'): the skin is the part whose alpha is under 255 (the hands' and arms'
#     skin sits at 150-240, the leather / cloth around it at 255) - fully, as the head; 2026-10-11, owner: dark-skinned
#     Celts had a dark head on pale arms when the alpha itself was the tint strength
#   eye: the eye-colour texture (white except the iris, same UVs as the face) multiplies the face
#   decal: tattoo, face x lerp(1, 2 x decal, decal alpha): grey-backed Celt tattoos are neutral where unpainted,
#          alpha-masked Troll decals only touch where drawn
#   hair is the only part whose alpha is coverage (cutout); on faces alpha is gloss, on clothes the tint mask
const SHADER := """
shader_type spatial;
render_mode cull_disabled;
uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D eye_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform sampler2D decal_tex : source_color, filter_linear_mipmap, repeat_enable;
uniform vec3 tint : source_color = vec3(1.0);
uniform int tint_mode = 0;
uniform bool has_eye = false;
uniform bool has_decal = false;
// a cloak's guild emblem (Emblems; the client's $insigniaTexture pixel shader): the pattern on the cloak's set 0 (UV2),
// the logo on its set 2 (CUSTOM0), the cloak's own texture on set 1 - pattern x cloak x 2 x logo, in the client's gamma
uniform sampler2D pattern_tex : filter_linear, repeat_enable;
uniform sampler2D logo_tex : filter_linear_mipmap, repeat_disable;
uniform bool has_emblem = false;
uniform bool has_logo = false;
// a cloak drawn this far (in the figure's own NIF units) toward the camera: the spiked pauldrons of some chest meshes
// (Kobold body 3 / 5) come 0.15 - 0.4 units through the cloak's inner edges in the idle, where the old client shows
// the cloak over them (owner 2026-10-19); 0 = drawn where it is
uniform float view_pull = 0.0;
varying vec2 logo_uv;

void vertex() {
	logo_uv = CUSTOM0.xy;
	// (POSITION written on every path: a shader that writes it anywhere no longer gets the default projection - the
	// unpulled parts, body and head, were left unplaced and the character went invisible, owner 2026-10-19)
	vec4 vp = MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
	if (view_pull > 0.0) {
		vp.xyz -= normalize(vp.xyz) * view_pull * length(MODELVIEW_MATRIX[0].xyz);
	}
	POSITION = PROJECTION_MATRIX * vp;
}

void fragment() {
	vec4 c = texture(albedo_tex, UV);
	vec3 col = c.rgb;
	if (has_emblem) {
		vec3 g = pow(col, vec3(1.0 / 2.2)) * texture(pattern_tex, UV2).rgb * 2.0;
		if (has_logo) g *= texture(logo_tex, logo_uv).r;
		col = pow(clamp(g, 0.0, 1.0), vec3(2.2));
	}
	else if (tint_mode == 1) col *= tint;
	else if (tint_mode == 2) col *= mix(tint, vec3(1.0), smoothstep(0.90, 0.99, c.a));
	if (has_eye) col *= texture(eye_tex, UV).rgb;
	if (has_decal) {
		vec4 d = texture(decal_tex, UV);
		col *= mix(vec3(1.0), d.rgb * 2.0, d.a);
	}
	ALBEDO = col;
	ROUGHNESS = 0.85;
	SPECULAR = 0.3;
	//CUTOUT
}
"""
