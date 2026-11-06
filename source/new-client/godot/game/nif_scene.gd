class_name NifScene
extends Node3D
## A whole converted NIF scene (the character-select backdrops): every mesh with the client's layered materials,
## camera-facing cards, its particle systems, its vertex animation (banners, grass, mushrooms), and the realm fog
## from pregame/settings.dat. Spell and monster effects add the NIF's own timeline (pipeline/nif_static node_anims):
## nodes a visibility controller shows and hides, keyframed node moves / stretches (Gleam Ray's streak), material
## fades, moving particle emitters, and - with `timeline` (a spell effect, played from its start like the client) -
## each particle system emitting only in its own emit window instead of burning all the time.

var materials: NifMaterials
var info := {}
var timeline := false                       # set before adding to the tree: play the effect's timeline from 0
var _bound_for := 0                         # the instance the animation tables belong to (copies rebind in _ready)
var _anims: Array = []                      # {meshes, a}: nif_node_anims
var _alphas: Array = []                     # {mat, aa}: material fades (alpha_anim)
var _emitters: Array = []                   # {p, spec}: particle systems (emit windows, moving emitters)
var _cycle := 0.0


## Effects drawn closer to the old client:
## - every effect's additive or blended layer with a dark map: the dark map multiplies its colour and alpha, as the
##   client's - the effects' dark maps are shaping masks on black (whtball, lgskull2, whtcore, grystrkcyl ...), so
##   Bark Skin's bubble is a soft ball fading at its rim, a corpse light its skull / core / streaks instead of whole
##   flat tinted cards (owner 2026-10-14/15: "faint gold bubble", "the corpselight npc's are especially messed up").
##   Our alpha-weighted dark map (made for the zones' overlays - vines, lichen - which keep it) ignored the masks.
## - hib_oakleaves_hit, Strength of the Oak's falling leaves ("black leaves"): lit as the client's E_A_D - emissive +
##   the material's dark ambient x the ambient light + its WHITE diffuse x the sun; ours scaled the sun by the dark
##   ambient too (18%), and the falling (morphed) leaves' normals don't follow them - lit without the angle term
const FIXES := {
	"hib_oakleaves_hit": {"sunlit": true},
}


static func _fixed(spec: Dictionary, fix: Dictionary) -> Dictionary:
	var s := spec.duplicate(true)
	if String(s.get("alpha", "")) in ["add", "blend", "destcolor"] and (s.get("slots", {}) as Dictionary).has("dark"):
		s["dark_plain"] = true
	if bool(fix.get("sunlit", false)) and not String(s.get("alpha", "")) in ["add", "destcolor"] and float(s.get("mat_light", 1.0)) < 1.0:
		var k := 1.0 / maxf(float(s.mat_light), 0.05)       # the sun at the white diffuse, over the ambient-darkened albedo
		s["ambient_only"] = true
		s["diffuse_tint"] = [k, k, k]
	return s


func load_scene(gltf_path: String, texture_set := "x4plus") -> bool:
	var dir := gltf_path.get_base_dir()
	var folders := {"original": dir.path_join("textures/original")}
	if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(dir.path_join("textures/x4plus"))):
		folders["x4plus"] = dir.path_join("textures/x4plus")
	materials = NifMaterials.new(folders, texture_set)
	materials.use_material_light = true                 # ambient-lit like the client (dark materials stay dark)
	materials.main_only = true                          # (effects are packed on workers: their materials made on main)
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(gltf_path, state) != OK:
		push_warning("could not load " + gltf_path)
		return false
	var extras: Dictionary = state.json.get("extras", {})
	var specs: Array = extras.get("nif_materials", [])
	var fix: Dictionary = FIXES.get(gltf_path.get_base_dir().get_file(), {})
	specs = specs.map(func(s): return _fixed(s, fix))
	for sp in specs:                                   # textures decoded here (a worker when prefetched / packed)
		for slot in ((sp as Dictionary).get("slots", {}) as Dictionary).values():
			materials.texture(String(slot.tex))
	for spec in extras.get("nif_particles", []):
		if String((spec as Dictionary).get("texture", "")) != "":
			materials.texture(String(spec.texture))
	# the materials' render-server materials made on the main thread (GpuJobs: on a worker - effects are prefetched
	# and packed on one - they raced the render thread: "Updating buffers is forbidden during creation of a draw list")
	var gmats := state.get_materials()                 # (the converted ones in the placeholders' places: none made)
	for i in gmats.size():
		var gname := gmats[i].resource_name if gmats[i] else ""
		if gname.begins_with("nifmat:") and int(gname.substr(7)) < specs.size():
			gmats[i] = materials.material(specs[int(gname.substr(7))])
	state.set_materials(gmats)
	GpuJobs.ready_materials(Array(gmats))
	var root := doc.generate_scene(state)
	for node in root.find_children("*", "MeshInstance3D", true, false):
		var mi := node as MeshInstance3D
		if mi.name.begins_with("collision_"):
			mi.visible = false
			continue
		for i in mi.mesh.get_surface_count():
			var src := mi.mesh.surface_get_material(i)
			var mat_name := src.resource_name if src else ""
			if mat_name.begins_with("nifmat:") and int(mat_name.substr(7)) < specs.size():
				mi.mesh.surface_set_material(i, materials.material(specs[int(mat_name.substr(7))]))
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(root)
	var ps: Array = extras.get("nif_particles", [])
	if not ps.is_empty():
		GpuJobs.run(func():                            # particles set their own materials: made on the main thread
			for spec in ps:
				add_child(NifParticles.build(spec, Transform3D.IDENTITY, 400.0, materials)), "effect particles")
	set_meta("nif_extras", extras)
	_bind()
	return true


## A fresh copy of a converted effect / scene: parsed once, packed, then instantiated - duplicate() reads every
## property back, and a particle system's "emitting" read waits on the render thread (with the separate render
## thread, ~0.5 s of stalls a frame when a raid's spells needed new copies).
static var _packed := {}                       # gltf path -> PackedScene (null: failed to load)


## The packed scene copy_of keeps (no cache: NpcManager prepares a view's effects on a worker with it).
static func pack_copy(gltf_path: String) -> PackedScene:
	var n := NifScene.new()
	var out: PackedScene = null
	if n.load_scene(gltf_path):
		for c in n.find_children("*", "", true, false):
			c.owner = n                            # pack the whole tree
		var ps := PackedScene.new()
		out = ps if ps.pack(n) == OK else null
	n.free()
	return out


## Background loading (owner 2026-10-17, hitches in a companion fight: each spell effect's first copy was parsed,
## textured and packed on the main thread - 60-200 ms a packet, up to 1.5 s frames): the effect is parsed and packed on
## a worker, then kept like copy_of's. is_loaded() says whether copy_of would now be instant.
static var _loading := {}                      # gltf path -> true while a worker packs it


static func is_loaded(gltf_path: String) -> bool:
	return _packed.has(gltf_path)


static func loading(gltf_path: String) -> bool:
	return _loading.has(gltf_path)


## The packing runs on one dedicated thread of its own, one effect after another - never on the WorkerThreadPool
## (2026-10-30, the login freeze: a live login hung for good after the zone loaded, every few logins). Packing waits on
## the main thread for its materials and particles (GpuJobs), and the pool keeps only ~30% of its threads for
## low-priority tasks: two or three prefetches waiting there took them all, and the main thread, in an engine call
## that queues a low-priority pool task of its own and waits for it, waited for a slot those prefetches would only free
## once the main thread ran their steps. A thread of our own can wait on the main thread without holding a pool slot.
static var _fetch_thread: Thread
static var _fetch_queue: Array = []
static var _fetch_mutex := Mutex.new()
static var _fetch_wake := Semaphore.new()
static var _fetch_stop := false


static func prefetch(gltf_path: String) -> void:
	if gltf_path == "" or _packed.has(gltf_path) or _loading.has(gltf_path):
		return
	_loading[gltf_path] = true
	_fetch_mutex.lock()
	_fetch_queue.append(gltf_path)
	_fetch_mutex.unlock()
	if _fetch_thread == null:
		_fetch_stop = false
		_fetch_thread = Thread.new()
		_fetch_thread.start(_fetch_loop)
	_fetch_wake.post()


static func _fetch_loop() -> void:
	while true:
		_fetch_wake.wait()
		if _fetch_stop:
			return
		_fetch_mutex.lock()
		var path: String = _fetch_queue.pop_front() if not _fetch_queue.is_empty() else ""
		_fetch_mutex.unlock()
		if path == "":
			continue
		var ps := NifScene.pack_copy(path)
		(func():
			NifScene._packed[path] = ps
			NifScene._loading.erase(path)).call_deferred()


## Quitting: the prefetch thread ends (GpuJobs._exit_tree - its steps run on it from then on).
static func stop_prefetch() -> void:
	if _fetch_thread == null:
		return
	_fetch_stop = true
	_fetch_wake.post()
	if _fetch_thread.is_started():
		_fetch_thread.wait_to_finish()
	_fetch_thread = null


static func copy_of(gltf_path: String) -> NifScene:
	if not _packed.has(gltf_path):
		_packed[gltf_path] = pack_copy(gltf_path)
	var p: PackedScene = _packed[gltf_path]
	var copy := p.instantiate() as NifScene if p else null
	CacheTrim.track("fx", gltf_path, copy)
	return copy


## Copies (one per spell cast / monster) find their own meshes and particles again.
func _ready() -> void:
	if _bound_for != get_instance_id():
		_bind()
	if timeline:
		for e in _emitters:
			(e.p as GPUParticles3D).preprocess = 0.0
			(e.p as GPUParticles3D).emitting = false
			e["on"] = false
		set_process(true)
		_step(0.0)


func _bind() -> void:
	_bound_for = get_instance_id()
	_morphs.clear()
	_anims.clear()
	_pivots.clear()
	_alphas.clear()
	_emitters.clear()
	var extras: Dictionary = get_meta("nif_extras", {})
	var root: Node = null
	for c in get_children():
		if not (c is GPUParticles3D):
			root = c
			break
	if root == null:
		return
	_setup_morphs(root, extras.get("nif_morphs", []))
	var by_name := {}
	for node in root.find_children("*", "MeshInstance3D", true, false):
		by_name[String(node.name)] = node
	_cycle = 0.0
	for a in extras.get("nif_node_anims", []):
		var meshes := []
		for n in a.get("meshes", []):
			if by_name.has(String(n)):
				meshes.append(by_name[String(n)])
		if meshes.is_empty():
			continue
		if a.has("pivot"):                                  # parts under a camera-facing node: they move in its frame
			var pv := _pivot_node(root, a)
			for mi in meshes:
				if (mi as Node).get_parent() != pv:
					(mi as Node).reparent(pv, false)
		_anims.append({"meshes": meshes, "a": a})
		_cycle = maxf(_cycle, maxf(float(a.timing.stop), float(a.get("vis_timing", {}).get("stop", 0.0))))
		_pose(meshes, a.frames[0])
		for mi in meshes:
			(mi as MeshInstance3D).visible = bool(a.get("visible", true))
	for node in by_name.values():
		var mi := node as MeshInstance3D
		for i in mi.mesh.get_surface_count():
			var m := mi.mesh.surface_get_material(i) as ShaderMaterial
			if m == null or not m.has_meta("spec"):
				continue
			var spec: Dictionary = m.get_meta("spec")
			if spec.has("alpha_anim") or spec.has("color_anims") or spec.has("uv_anim"):
				# this copy fades / changes colour on its own - duplicated on the main thread (2026-10-30, the login freeze:
				# an effect packed on a worker made this copy's render-server material there - duplicate() sets every
				# parameter, the race GpuJobs exists for)
				var own := GpuJobs.run(func(): return m.duplicate(), "effect material") as ShaderMaterial
				mi.set_surface_override_material(i, own)
				var aa: Dictionary = spec.get("alpha_anim", {})
				var cas: Array = spec.get("color_anims", [])
				var uva: Dictionary = spec.get("uv_anim", {})
				_alphas.append({"mat": own, "aa": aa, "ca": cas, "uv": uva, "base": (spec.get("slots", {}) as Dictionary).has("base")})
				if not aa.is_empty():
					_cycle = maxf(_cycle, float(aa.stop))
				for ca in cas:
					_cycle = maxf(_cycle, float(ca.stop))
				for slot in uva:
					_cycle = maxf(_cycle, float(uva[slot].stop))
	for c in get_children():
		if c is GPUParticles3D and c.has_meta("nif_spec"):
			var spec: Dictionary = c.get_meta("nif_spec")
			_emitters.append({"p": c, "spec": spec})
			_cycle = maxf(_cycle, float(spec.get("cycle", 0.0)))
	for m in _morphs:
		_cycle = maxf(_cycle, float(m.stop))
	set_process(not (_morphs.is_empty() and _anims.is_empty() and _alphas.is_empty()
			and not (timeline and not _emitters.is_empty())))


## A pooled copy is used again (SpellFx): its timeline from the start, its particles emptied.
func restart() -> void:
	_time = 0.0
	for e in _emitters:
		var p: GPUParticles3D = e.p
		p.preprocess = 0.0
		p.emitting = false
		p.restart()
		p.emitting = false
		e["on"] = false
	_step(0.0)


func particles() -> Array:
	return _emitters.map(func(e): return e.p)


## A NiBillboardNode that has animated parts under it (Arawn's eyes, the blood shield's cross flares): a node turned to
## the camera every frame (_face_pivots), its parts moving in its frame by their own keys (nif_static chain_animation).
## Made once per scene and packed with it; a copy finds it again by name.
var _pivots: Array = []


func _pivot_node(root: Node, a: Dictionary) -> Node3D:
	var p: Array = a.pivot
	var key := "bbpivot_%d_%d_%d_%s" % [roundi(float(p[0]) * 1000), roundi(float(p[1]) * 1000), roundi(float(p[2]) * 1000),
			String(a.get("pivot_mode", "face"))]
	var pv := root.get_node_or_null(key) as Node3D
	if pv == null:
		pv = Node3D.new()
		pv.name = key
		pv.position = Vector3(float(p[0]), float(p[1]), float(p[2]))
		root.add_child(pv)
		pv.owner = root.owner if root.owner else root
	pv.set_meta("mode", String(a.get("pivot_mode", "face")))
	if not _pivots.has(pv):
		_pivots.append(pv)
	return pv


## The pivots face the camera like the client's billboards (the shader's billboard: the card's own +Z to the camera and
## +Y up - Godot axes x right, y toward the camera, z down; "up" mode turns about the world's up only), at the scene's
## own scale (an effect sized to its target).
func _face_pivots() -> void:
	var cam := get_viewport().get_camera_3d() if is_inside_tree() else null
	if cam == null:
		return
	var cb := cam.global_basis
	for pv in _pivots:
		var n := pv as Node3D
		if not is_instance_valid(n):
			continue
		var k := (n.get_parent() as Node3D).global_basis.get_scale()
		var b: Basis
		if String(n.get_meta("mode", "face")) == "up":
			# about the world's up, the pivot's NIF +y to the camera (Godot -z): its keys carry the billboard's own
			# rotation (nif_static) - the scare faces look at you
			var side := Vector3.UP.cross(cb.z)
			side = side.normalized() if side.length() > 0.001 else Vector3.RIGHT
			b = Basis(-side, Vector3.UP, -side.cross(Vector3.UP))
		else:
			b = Basis(cb.x.normalized(), cb.z.normalized(), -cb.y.normalized())
		n.global_basis = b * Basis.from_scale(k)


static func _pose(meshes: Array, f: Array) -> void:
	var t := Transform3D(Basis(Quaternion(f[4], f[5], f[6], f[7])).scaled(Vector3.ONE * float(f[8])), Vector3(f[1], f[2], f[3]))
	for mi in meshes:
		(mi as Node3D).transform = t


## A controller's own time (Gamebryo: time x frequency + phase, then loop / reverse / clamp within start..stop).
static func _ctl(t: float, c: Dictionary) -> float:
	var start := float(c.get("start", 0.0))
	var stop := float(c.get("stop", 0.0))
	var span := maxf(stop - start, 0.0001)
	var u := t * float(c.get("frequency", 1.0)) + float(c.get("phase", 0.0))
	match String(c.get("cycle", "loop")):
		"clamp": return clampf(u, start, stop)
		"reverse":
			var v := fposmod(u - start, span * 2.0)
			return start + (v if v <= span else span * 2.0 - v)
	return start + fposmod(u - start, span)


## Index of the last key at or before t in [[t, ...], ...].
static func _key_at(keys: Array, t: float) -> int:
	var k := 0
	for i in keys.size():
		if float(keys[i][0]) <= t:
			k = i
		else:
			break
	return k


## A linear key's value at t in [[t, value], ...] (held before the first and after the last).
static func _lerp_key(keys: Array, t: float) -> float:
	if keys.is_empty():
		return 0.0
	var k := _key_at(keys, t)
	if k + 1 >= keys.size() or t <= float(keys[k][0]):
		return float(keys[k][1])
	var a: Array = keys[k]
	var b: Array = keys[k + 1]
	return lerpf(float(a[1]), float(b[1]), clampf((t - float(a[0])) / maxf(float(b[0]) - float(a[0]), 0.0001), 0.0, 1.0))


# --- vertex animation (NiGeomMorpherController): the banners' wave, grass shoots, mushrooms ------------------------
# Each animated mesh carries its morph frames as blend shapes; the client's weight keys and timing come from the
# converter (pipeline/nif_static.py morph_animation). Time runs like a Gamebryo controller: scaled = time x frequency
# + phase, then loop / reverse (ping-pong) / clamp (play once) within start..stop.

var _morphs: Array = []
var _time := 0.0


func _setup_morphs(root: Node, specs: Array) -> void:
	var by_name := {}
	for node in root.find_children("morph_*", "MeshInstance3D", true, false):
		by_name[String(node.name)] = node
	for spec in specs:
		var mi: MeshInstance3D = by_name.get(String(spec.node))
		if mi == null or not bool(spec.get("active", true)):
			continue
		var rows: Array = []
		for w in spec.weights:
			rows.append(PackedFloat32Array(w))
		_morphs.append({"mi": mi, "times": PackedFloat32Array(spec.times), "rows": rows,
				"start": float(spec.start), "stop": float(spec.stop), "freq": float(spec.frequency),
				"phase": float(spec.phase), "cycle": String(spec.cycle), "lit": PackedInt32Array()})
	set_process(not _morphs.is_empty())


static var prof_us := 0                       # debug timing (tests/crowd_probe.gd)
static var prof_live := 0


func _process(delta: float) -> void:
	var t0 := Time.get_ticks_usec()
	_step(delta)
	prof_us += Time.get_ticks_usec() - t0
	Perf.proc_add("effects", Time.get_ticks_usec() - t0)


func _step(delta: float) -> void:
	_time += delta
	if not _pivots.is_empty():
		_face_pivots()
	var now := fposmod(_time, _cycle) if _cycle > 0.0 and (timeline or not _anims.is_empty()) else _time
	for e in _emitters:
		var spec: Dictionary = e.spec
		var p: GPUParticles3D = e.p
		var ea: Dictionary = spec.get("emitter_anim", {})
		if not ea.is_empty():
			var fr: Array = ea.frames
			var f: Array = fr[_key_at(fr, _ctl(now, ea))]
			p.transform = Transform3D(Basis(Quaternion(f[4], f[5], f[6], f[7])).scaled(Vector3.ONE * float(f[8])), Vector3(f[1], f[2], f[3]))
		var pv: Dictionary = spec.get("vis", {})
		if not pv.is_empty():                              # drawn or not; it keeps simulating either way
			var pk: Array = pv.keys
			var shown := int(pk[_key_at(pk, _ctl(now, pv))][1]) != 0
			if bool(e.get("shown", true)) != shown:
				e["shown"] = shown
				p.transparency = 0.0 if shown else 1.0
		if timeline:
			var tc := fposmod(now, maxf(float(spec.get("cycle", 0.0)), 0.0001)) if float(spec.get("cycle", 0.0)) > 0.0 else now
			var on := tc >= float(spec.emit_start) and tc < maxf(float(spec.emit_stop), float(spec.emit_start) + 1.0 / 30.0)
			# our own flag: reading GPUParticles3D.emitting asks the render thread and waits (with the separate
			# render thread a crowd's 400 effects stalled every frame - ~1 fps while casting)
			if bool(e.get("on", false)) != on:
				e["on"] = on
				p.emitting = on
	for a in _anims:
		var d: Dictionary = a.a
		if d.has("vis"):
			var vis: Array = d.vis
			var shown := int(vis[_key_at(vis, _ctl(now, d.vis_timing))][1]) != 0
			for mi in a.meshes:
				(mi as Node3D).visible = shown
		var frames: Array = d.frames
		if frames.size() > 1:
			_pose(a.meshes, frames[_key_at(frames, _ctl(now, d.timing))])
	for al in _alphas:
		var mat := al.mat as ShaderMaterial
		if not (al.aa as Dictionary).is_empty():
			var keys: Array = al.aa.keys
			mat.set_shader_parameter("opacity", float(keys[_key_at(keys, _ctl(now, al.aa))][1]))
		for ca in al.ca:                                   # NiMaterialColorController: the colour over time
			var ck: Array = ca.keys
			var k: Array = ck[_key_at(ck, _ctl(now, ca))]
			var col := Color(float(k[1]), float(k[2]), float(k[3]))
			if String(ca.target) == "emissive":
				mat.set_shader_parameter("emissive", col)
			elif bool(al.base):                            # diffuse over a texture: its tint, kept at full strength
				var pk := maxf(float(ca.get("peak", 1.0)), 0.001)
				mat.set_shader_parameter("tint", Color(col.r / pk, col.g / pk, col.b / pk))
			else:
				mat.set_shader_parameter("tint", col)
		for slot in al.uv:                                  # NiUVController played once / ping-pong: the texture offset
			var ua: Dictionary = al.uv[slot]
			var ut := _ctl(now, ua)
			mat.set_shader_parameter(String(slot) + "_offset", Vector2(_lerp_key(ua.get("u", []), ut), _lerp_key(ua.get("v", []), ut)))
	for m in _morphs:
		var span: float = maxf(m.stop - m.start, 0.0001)
		var t: float = now * m.freq + m.phase
		match m.cycle:
			"clamp": t = clampf(t, m.start, m.stop)
			"reverse":
				var u := fposmod(t - m.start, span * 2.0)
				t = m.start + (u if u <= span else span * 2.0 - u)
			_: t = m.start + fposmod(t - m.start, span)
		var times: PackedFloat32Array = m.times
		var k := clampi(times.bsearch(t) - 1, 0, times.size() - 1)
		var k2 := mini(k + 1, times.size() - 1)
		var f := 0.0 if k2 == k else clampf((t - times[k]) / maxf(times[k2] - times[k], 0.0001), 0.0, 1.0)
		var a: PackedFloat32Array = m.rows[k]
		var b: PackedFloat32Array = m.rows[k2]
		var mi: MeshInstance3D = m.mi
		var lit := PackedInt32Array()
		for i in a.size():
			var w := lerpf(a[i], b[i], f)
			if w != 0.0:
				mi.set_blend_shape_value(i, w)
				lit.append(i)
		for i in m.lit:                                    # frames that were on last time and are off now
			if not lit.has(i):
				mi.set_blend_shape_value(i, 0.0)
		m.lit = lit
