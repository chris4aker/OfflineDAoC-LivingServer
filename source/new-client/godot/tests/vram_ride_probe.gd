extends Node
## Measure (owner 2026-10-29, 0 changes: "zones i was out of were still on the gpu's memory"): riding through one
## region with no loading screen - zones stream in ahead and are released behind. Offline, NPCs on: from Avalon Marsh
## across Campacorentin Forest and Camelot Hills to Forest Sauvage and back the same way, the player carried at
## horse speed. At each zone's centre (after a settle): video / texture / buffer memory, zones loaded, nodes,
## resources, and the caches that outlive a zone - with the figure texture cache's size in MB. A leak shows as the
## memory at the end (Avalon Marsh again, same zones loaded) above the memory at the start.
##   --skip-login --zone=9 --probe=vram_ride_probe

const BLOCK_M := 204.8
const SPEED := 35.0                           # metres a second (a horse: 1500 units/s = 37.5)
const SETTLE_S := 20.0
const ROUTE := [                              # zone-grid points (zones_index units of 8192): zone centres
	[63.0, 75.0, "Campacorentin Forest"], [71.0, 63.0, "Camelot Hills"], [73.0, 55.0, "Forest Sauvage"],
	[71.0, 63.0, "Camelot Hills again"], [63.0, 75.0, "Campacorentin Forest again"], [55.0, 79.0, "Avalon Marsh again"]]
# (--short: the first two legs only)


func _ready() -> void:
	var root := get_tree().root
	if root.has_node("VramRideDriver"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "VramRideDriver"
	root.add_child.call_deferred(d)
	queue_free()


class Driver extends Node:
	var target := Vector2.INF                 # grid point the player is carried to
	var arrived := false

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		_run()

	func _mb(m: int) -> String:
		return "%d" % int(Performance.get_monitor(m) / 1048576.0)

	func _tex_mb() -> float:
		var b := 0.0
		for k in Fig3Figure._tex_cache:
			var t = Fig3Figure._tex_cache[k]
			if t is Texture2D:
				var bpp := 4.0
				if t is ImageTexture:
					var f := (t as ImageTexture).get_format()
					bpp = 0.5 if f in [Image.FORMAT_DXT1, Image.FORMAT_BPTC_RGBA] else (1.0 if f in [Image.FORMAT_DXT3, Image.FORMAT_DXT5, Image.FORMAT_BPTC_RGBA] else 4.0)
				b += (t as Texture2D).get_width() * (t as Texture2D).get_height() * bpp * 1.33
		return b / 1048576.0

	func _report(label: String) -> void:
		var v := get_tree().current_scene
		var region: Node = v.get("region") if v else null
		var loaded: Array = (region.get("zones") as Dictionary).keys() if region else []
		var nm = v.get("npcs") if v else null
		var npc_models: int = (nm.get("_scenes") as Dictionary).size() if nm and nm.get("_scenes") is Dictionary else -1
		print("[vram] %-30s video %s MB (textures %s, buffers %s) | zones %s | nodes %d | resources %d | monster models %d | figure textures %d (~%.0f MB) | helms %d | weapons %d | effects %d" % [
				label, _mb(Performance.RENDER_VIDEO_MEM_USED), _mb(Performance.RENDER_TEXTURE_MEM_USED),
				_mb(Performance.RENDER_BUFFER_MEM_USED), str(loaded), int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)),
				int(Performance.get_monitor(Performance.OBJECT_RESOURCE_COUNT)), npc_models, Fig3Figure._tex_cache.size(),
				_tex_mb(), Fig3Figure._helm_scenes.size(), WeaponModels._scenes.size(), NifScene._packed.size()])

	## The player's place on the zone grid.
	func _grid(v: Node) -> Vector2:
		var me: Dictionary = v.region.entry(int(v.zone.info.id))
		var p: Vector3 = v.player.global_position
		return Vector2(float(me.x) + p.x / BLOCK_M, float(me.y) + p.z / BLOCK_M)

	func _physics_process(delta: float) -> void:
		if target == Vector2.INF:
			return
		var v := get_tree().current_scene
		if v == null or v.get("region") == null or v.get("player") == null:
			return
		var g := _grid(v)
		var to := target - g
		var step := SPEED * delta / BLOCK_M
		if to.length() <= step:
			arrived = true
			return
		var d := to.normalized() * step * BLOCK_M
		var p: Vector3 = v.player.global_position + Vector3(d.x, 0.0, d.y)
		var h = v.region.height_at(p.x, p.z)
		if h is float and is_finite(h):
			p.y = float(h) + 0.5
		v.player.global_position = p
		v.player.velocity = Vector3.ZERO

	## Texture bytes (estimate, with mipmaps) of one texture.
	func _bytes(t: Texture2D) -> float:
		var bpp := 4.0
		if t is ImageTexture:
			var f := (t as ImageTexture).get_format()
			if f in [Image.FORMAT_DXT1, Image.FORMAT_RGTC_R]:
				bpp = 0.5
			elif f in [Image.FORMAT_DXT3, Image.FORMAT_DXT5, Image.FORMAT_BPTC_RGBA, Image.FORMAT_RGTC_RG, Image.FORMAT_L8, Image.FORMAT_R8]:
				bpp = 1.0
			elif f in [Image.FORMAT_RGB8]:
				bpp = 4.0
		return t.get_width() * t.get_height() * bpp * 1.33

	func _add_mat(m: Material, seen: Dictionary) -> void:
		if m == null:
			return
		if m is ShaderMaterial and (m as ShaderMaterial).shader:
			for u in (m as ShaderMaterial).shader.get_shader_uniform_list():
				var v = (m as ShaderMaterial).get_shader_parameter(u.name)
				if v is Texture2D:
					seen[(v as Texture2D).get_rid()] = v
		elif m is BaseMaterial3D:
			for i in BaseMaterial3D.TEXTURE_MAX:
				var t := (m as BaseMaterial3D).get_texture(i)
				if t:
					seen[t.get_rid()] = t
		if m.next_pass:
			_add_mat(m.next_pass, seen)

	## Texture memory still used by something in the tree (meshes' materials, terrain, UI pictures), in MB.
	func _in_use_mb() -> float:
		var seen := {}
		var stack: Array = [get_tree().root]
		while not stack.is_empty():
			var n: Node = stack.pop_back()
			stack.append_array(n.get_children(true))
			if n is GeometryInstance3D:
				_add_mat((n as GeometryInstance3D).material_override, seen)
				_add_mat((n as GeometryInstance3D).material_overlay, seen)
				if n is MeshInstance3D and (n as MeshInstance3D).mesh:
					var mi := n as MeshInstance3D
					for s in mi.mesh.get_surface_count():
						_add_mat(mi.get_surface_override_material(s), seen)
						_add_mat(mi.mesh.surface_get_material(s), seen)
				if n is GPUParticles3D and (n as GPUParticles3D).draw_pass_1:
					for s in (n as GPUParticles3D).draw_pass_1.get_surface_count():
						_add_mat((n as GPUParticles3D).draw_pass_1.surface_get_material(s), seen)
			elif n is TextureRect and (n as TextureRect).texture:
				seen[(n as TextureRect).texture.get_rid()] = (n as TextureRect).texture
			elif n is NinePatchRect and (n as NinePatchRect).texture:
				seen[(n as NinePatchRect).texture.get_rid()] = (n as NinePatchRect).texture
			elif n is Sprite3D and (n as Sprite3D).texture:
				seen[(n as Sprite3D).texture.get_rid()] = (n as Sprite3D).texture
		var b := 0.0
		for r in seen:
			b += _bytes(seen[r])
		return b / 1048576.0

	## Textures reachable from a value: a texture, a material, a mesh, a packed scene, a node (and its children),
	## arrays and dictionaries of those.
	func _collect(v: Variant, seen: Dictionary, depth := 0) -> void:
		if v == null or depth > 6:
			return
		if v is Texture2D:
			seen[(v as Texture2D).get_rid()] = v
		elif v is Material:
			_add_mat(v, seen)
		elif v is Mesh:
			for s in (v as Mesh).get_surface_count():
				_add_mat((v as Mesh).surface_get_material(s), seen)
		elif v is PackedScene:
			var st := (v as PackedScene).get_state()
			for i in st.get_node_count():
				for j in st.get_node_property_count(i):
					_collect(st.get_node_property_value(i, j), seen, depth + 1)
		elif v is Node:
			var stack: Array = [v]
			while not stack.is_empty():
				var n: Node = stack.pop_back()
				stack.append_array(n.get_children(true))
				if n is GeometryInstance3D:
					_add_mat((n as GeometryInstance3D).material_override, seen)
					if n is MeshInstance3D and (n as MeshInstance3D).mesh:
						_collect((n as MeshInstance3D).mesh, seen, depth + 1)
						for si in (n as MeshInstance3D).mesh.get_surface_count():
							_add_mat((n as MeshInstance3D).get_surface_override_material(si), seen)
					if n is GPUParticles3D and (n as GPUParticles3D).draw_pass_1:
						_collect((n as GPUParticles3D).draw_pass_1, seen, depth + 1)
		elif v is Array:
			for x in v:
				_collect(x, seen, depth + 1)
		elif v is Dictionary:
			for x in (v as Dictionary).values():
				_collect(x, seen, depth + 1)

	func _mb_of(seen: Dictionary) -> float:
		var b := 0.0
		for r in seen:
			b += _bytes(seen[r])
		return b / 1048576.0

	func _breakdown() -> void:
		var v := get_tree().current_scene
		var nm = v.get("npcs") if v else null
		var parts := {
			"monster models": nm.get("_scenes") if nm else {},
			"race figures": Fig3Figure._scenes, "figure textures": Fig3Figure._tex_cache, "helms": Fig3Figure._helm_scenes,
			"weapons": WeaponModels._scenes, "effects (NifScene)": NifScene._packed,
			"spell fx templates": SpellFx._scenes, "spell fx pool": SpellFx._pool, "npc fx": Npc._fx_cache,
			"emblems": [Emblems._patterns, Emblems._logos, Emblems._surfaces], "ui art": UiArt._tex}
		var line := ""
		for k in parts:
			var seen := {}
			_collect(parts[k], seen)
			line += "%s %.0f, " % [k, _mb_of(seen)]
		print("[vram]   by cache (MB): ", line)

	func _gap(label: String) -> void:
		var used := _in_use_mb()
		var tex := Performance.get_monitor(Performance.RENDER_TEXTURE_MEM_USED) / 1048576.0
		print("[vram] %-30s textures on the card %.0f MB, used by the world ~%.0f MB, figure cache ~%.0f MB -> not used by anything ~%.0f MB | cache entries let go %d" % [
				label, tex, used, _tex_mb(), tex - used, CacheTrim.trimmed])
		_breakdown()

	func _run() -> void:
		await get_tree().create_timer(55.0).timeout
		_report("start: Avalon Marsh")
		_gap("start: Avalon Marsh")
		var route: Array = ROUTE.slice(0, 2) if "--short" in OS.get_cmdline_user_args() else ROUTE
		for leg in route:
			arrived = false
			target = Vector2(float(leg[0]), float(leg[1]))
			var t0 := Time.get_ticks_msec()
			while not arrived and Time.get_ticks_msec() - t0 < 120000:
				await get_tree().process_frame
			target = Vector2.INF
			await get_tree().create_timer(SETTLE_S).timeout
			_report(String(leg[2]))
			_gap(String(leg[2]))
		print("[vram] done")
		get_tree().quit()
