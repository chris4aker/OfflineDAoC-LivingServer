class_name ZoneLoader
extends Node3D
## Builds one zone from MMOEngine runtime content (content/zones/zoneNNN, written by pipeline/build_zone.py):
## terrain chunks with collision, water, and every placed object drawn as MultiMesh batches with collision.

const CHUNKS := 8
const FIXTURE_CELL_M := 102.4          # fixtures are batched per 4096-unit map cell (16 x 16 per zone)
const VIEW_RANGE_PER_M := 30.0         # objects stay drawn out to 30x their own size...
const VIEW_RANGE_MIN_M := 160.0        # ...but at least this far (small props)...
const VIEW_RANGE_MAX_M := 900.0        # ...and at most this far (trees, buildings, towers)
## Options > VIDEO view distance (Graphics): every draw range above times this (zones build on worker threads - a
## plain static they can read)
static var view_mult := 1.0
const TERRAIN_SHADER := preload("res://game/terrain.gdshader")
const WATER_SHADER := preload("res://game/water.gdshader")

var info: Dictionary
var dir: String
var heights := PackedFloat32Array()
var hsize := 0
var apron := PackedFloat32Array()      # heights_2x with one more sample all round from the neighbour zones (edge normals)
var cell := 0.0
var terrain_set := ""
var texture_set := ""
var water_mask: Image
var city := false                       # zone.json "city": the chunks are the ground (height_at casts onto them)

var materials: NifMaterials
var _terrain_mats: Array[ShaderMaterial] = []
var _water_mats: Array[ShaderMaterial] = []
var _lights: Array[OmniLight3D] = []
var _terrain_textures := {}     # terrain set -> Texture2D
var _detail_mean := 0.4


## Ground looks F1 cycles through: "blend" (the Catacombs-era ground the current client draws) plus each painted set.
func terrain_sets() -> Array:
	var sets: Array = []
	if info.get("ground_splat"):
		sets.append("blend")
	sets.append_array(info.terrain_sets.keys())
	return sets


func load_zone(content_dir: String, terrain_set_name := "blend", texture_set_name := "x4plus") -> void:
	dir = content_dir
	info = JSON.parse_string(FileAccess.get_file_as_string(dir.path_join("zone.json")))
	city = bool(info.get("city", false))
	terrain_set = "" if terrain_sets().is_empty() else (terrain_set_name if terrain_sets().has(terrain_set_name) else terrain_sets()[0])
	texture_set = texture_set_name if info.texture_sets.has(texture_set_name) else "original"
	var folders := {}
	for k in info.texture_sets:
		folders[k] = dir.path_join(info.texture_sets[k])
	materials = NifMaterials.new(folders, texture_set)
	if int(info.get("id", 0)) == 160:
		# Tuscaran Glacier's ice: its reflections turned down further (owner 2026-10-17: "its really missing the gritty
		# ice texture of the old client") - this zone only
		materials.env_strength = 0.35
	var t0 := Time.get_ticks_msec()
	_load_heights()
	if not city:                                           # a city (Tir na Nog...) is all city chunks: no terrain, no water
		_build_terrain()
		_build_water()
	_build_fixtures()
	_build_lights()
	print("zone %s built in %d ms" % [info.name, Time.get_ticks_msec() - t0])


# ---------------------------------------------------------------- heights

func _load_heights() -> void:
	var file: String = info.get("heights_2x", info.heights)
	hsize = int(info.get("heights_2x_size", info.heights_size))
	heights = FileAccess.get_file_as_bytes(dir.path_join(file)).to_float32_array()
	if info.get("heights_2x_apron") != null and int(info.get("heights_2x_apron_size", 0)) == hsize + 2:
		apron = FileAccess.get_file_as_bytes(dir.path_join(String(info.heights_2x_apron))).to_float32_array()
	cell = float(info.size_m) / float(hsize - 1)
	if info.has("water_mask"):
		water_mask = Image.load_from_file(dir.path_join(info.water_mask))


func _h(ix: int, iz: int) -> float:
	return heights[clampi(iz, 0, hsize - 1) * hsize + clampi(ix, 0, hsize - 1)]


## Height for normals: one sample past the zone's edge comes from the neighbouring zone (pipeline/stitch_zone_edges.py),
## so the ground is lit the same on both sides of a border.
func _hn(ix: int, iz: int) -> float:
	if apron.is_empty():
		return _h(ix, iz)
	var n := hsize + 2
	return apron[clampi(iz + 1, 0, n - 1) * n + clampi(ix + 1, 0, n - 1)]


## Ground height in metres at a world position (bilinear on the rendered grid). In a city, with a reference height
## (near_y: where an NPC or the player stands now) the floor under that point is cast for - bridges over streets,
## halls over tunnels; nothing below gives the reference height back; without one, the highest floor there (the grid).
func height_at(x: float, z: float, near_y := INF) -> float:
	if city and near_y != INF and is_inside_tree():
		var q := PhysicsRayQueryParameters3D.create(Vector3(x, near_y + 1.2, z), Vector3(x, near_y - 40.0, z), LAYER_FLOOR)
		var hit := get_world_3d().direct_space_state.intersect_ray(q)
		if not hit.is_empty():
			return float(hit.position.y)
		return near_y                                      # nothing below yet (the physics comes up a frame late): stay put
	return _grid_height(x, z)


func _grid_height(x: float, z: float) -> float:
	var gx := clampf(x / cell, 0.0, hsize - 1.001)
	var gz := clampf(z / cell, 0.0, hsize - 1.001)
	var ix := int(gx)
	var iz := int(gz)
	var fx := gx - ix
	var fz := gz - iz
	var top := lerpf(_h(ix, iz), _h(ix + 1, iz), fx)
	var bottom := lerpf(_h(ix, iz + 1), _h(ix + 1, iz + 1), fx)
	return lerpf(top, bottom, fz)


## Surface height of the water body covering a world position, or -INF when dry.
func water_level_at(x: float, z: float) -> float:
	if water_mask == null:
		return -INF
	var n := water_mask.get_width()
	var px := clampi(int(x / float(info.size_m) * n), 0, n - 1)
	var pz := clampi(int(z / float(info.size_m) * n), 0, n - 1)
	var id := roundi(water_mask.get_pixel(px, pz).r * 255.0)
	for body in info.water:
		if int(body.id) == id:
			return float(body.height)
	return -INF


# ---------------------------------------------------------------- terrain

## Physics layers, following the client's movement (game.dll 0x4365b2): walls are only collision geometry, tested
## against the player's collider; floors are found by a ground pick against the terrain, the Collide fixtures and the
## world objects - the outdoor pick (0x467692) walks only the Collide fixtures' scene table (0x57fd8b, 0x2457418; the
## others go to 0x2457738, 0x478387 / 0x57fbe9), so trees and bushes are never stood on (owner 2026-10-11: bots
## walked up the Avalon Marsh willows' hanging leaves). Their faces are scenery: only the camera arm meets them.
const LAYER_WALL := 1
const LAYER_FLOOR := 2
const LAYER_SCENERY := 4


## Whether the client's ground pick sees a placed fixture (see the layers above).
static func picked(f: Dictionary) -> bool:
	return (bool(f.get("collide", false)) and not f.get("no_collision", false)) or String(f.get("source", "")) == "world_object"


## The client's stand-in wall for a fixture it doesn't collide with but gives a Radius (fixtures.csv; trees): within
## 1024 units of the player game.dll 0x49084e spawns an invisible entity of size 2 x Radius x Scale / 100 units, raised
## onto the ground (0x43f4db), and the player's step tests (0x435d7a / 0x43609b) stop where |dx| or |dy| to it would
## be under size + 24 units, between 96 units under its base and 1024 over it: an upright square on the world axes
## (not turned with the tree). The capsule's own radius comes off, so its centre stops where the client's point does.
## Faces wound like the converted models' (front outside); sides only - the client's test has no top to stand on, and
## a top would bake a walkable island 25 m up on every tree. (owner 2026-10-11: bots walked up the Myrkwood pines.)
static func proxy_faces(radius: int, scale: float, base_y: float, at: Vector3, capsule_r: float) -> PackedVector3Array:
	var size := int(2 * radius * int(round(scale * 100.0)) / 100)
	var h := maxf((size + 24) * 0.025 - capsule_r, 0.05)
	var y0 := base_y - 96.0 * 0.025 - at.y
	var y1 := base_y + 1024.0 * 0.025 - at.y
	var c := [Vector3(-h, y0, -h), Vector3(h, y0, -h), Vector3(h, y0, h), Vector3(-h, y0, h),
			Vector3(-h, y1, -h), Vector3(h, y1, -h), Vector3(h, y1, h), Vector3(-h, y1, h)]
	var out := PackedVector3Array()
	for q in [[0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]]:
		for t in [[q[0], q[1], q[2]], [q[0], q[2], q[3]]]:
			for k in t:
				out.append(c[k])
	return out


func _build_terrain() -> void:
	var body := StaticBody3D.new()
	body.name = "TerrainBody"
	body.collision_layer = LAYER_FLOOR                    # the client's terrain is a floor (picked), never a wall
	body.collision_mask = 0
	add_child(body)
	var per := (hsize - 1) / CHUNKS
	var detail_name: String = String(info.get("terrain_detail", "")) if info.get("terrain_detail") != null else ""   # null in some zones
	var detail := materials.texture(detail_name) if detail_name != "" else null
	if detail:
		var img := materials.image(detail_name)                 # (from the file: load_zone runs on a worker)
		if img:
			_detail_mean = _mean_luminance(img)
	var chunks := []                                          # [mesh, cx, cz]
	for cz in CHUNKS:
		for cx in CHUNKS:
			var verts := PackedVector3Array()
			var normals := PackedVector3Array()
			var uvs := PackedVector2Array()
			var idx := PackedInt32Array()
			for j in per + 1:
				for i in per + 1:
					var gx := cx * per + i
					var gz := cz * per + j
					verts.append(Vector3(gx * cell, _h(gx, gz), gz * cell))
					var n := Vector3(_hn(gx - 1, gz) - _hn(gx + 1, gz), 2.0 * cell, _hn(gx, gz - 1) - _hn(gx, gz + 1))
					normals.append(n.normalized())
					uvs.append(Vector2(float(gx) / (hsize - 1), float(gz) / (hsize - 1)))   # whole-zone ground image
			for j in per:
				for i in per:
					var a := j * (per + 1) + i
					idx.append_array([a, a + 1, a + per + 1, a + 1, a + per + 2, a + per + 1])
			var arrays := []
			arrays.resize(Mesh.ARRAY_MAX)
			arrays[Mesh.ARRAY_VERTEX] = verts
			arrays[Mesh.ARRAY_NORMAL] = normals
			arrays[Mesh.ARRAY_TEX_UV] = uvs
			arrays[Mesh.ARRAY_INDEX] = idx
			var mesh := ArrayMesh.new()
			mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
			chunks.append([mesh, cx, cz])
			var mi := MeshInstance3D.new()
			mi.name = "Terrain_%d_%d" % [cx, cz]
			mi.mesh = mesh
			add_child(mi)
			# its collision from the arrays just built - never create_trimesh_shape(), which reads the mesh back from
			# the GPU (on the zone worker that ended the frame being drawn: GpuJobs)
			var faces := PackedVector3Array()
			faces.resize(idx.size())
			for k in idx.size():
				faces[k] = verts[idx[k]]
			var tri := ConcavePolygonShape3D.new()
			tri.set_faces(faces)
			var shape := CollisionShape3D.new()
			shape.shape = tri
			body.add_child(shape)
	for c in chunks:
		var mat := ShaderMaterial.new()
		mat.shader = TERRAIN_SHADER
		mat.set_meta("patch", "%d_%d" % [c[1], c[2]])
		mat.set_shader_parameter("patch_index", Vector2(c[1], c[2]))
		mat.set_shader_parameter("patches", float(CHUNKS))
		_terrain_mats.append(mat)
	GpuJobs.ready_materials(_terrain_mats)                 # (their render-server materials made on the main thread)
	for i in chunks.size():
		(chunks[i][0] as ArrayMesh).surface_set_material(0, _terrain_mats[i])
	set_terrain_set(terrain_set)


## The terrain set's textures read and decoded (kept): the painted image, the detail map and, for the blend, every
## patch's layers and weight maps - on the zone worker, so the materials made on the main thread only bind them.
func _terrain_texture_cache(set_name: String) -> String:
	var blend := set_name == "blend"
	# the painted image: the chosen set, or for the blend the baked ground it fades to in the distance
	var painted := set_name
	if blend:
		painted = "lod_x4plus" if texture_set == "x4plus" and info.terrain_sets.has("lod_x4plus") else "lod"
	if not _terrain_textures.has(painted):
		_terrain_textures[painted] = _load_texture(dir.path_join(info.terrain_sets[painted]))
	if blend and info.get("ground_splat"):
		for patch in (info.ground_splat.patches as Dictionary).values():
			for l in patch.get("layers", []):
				materials.texture(String(l.tex))
			for key in patch.get("weights", []):
				if not _terrain_textures.has(String(key)):
					_terrain_textures[String(key)] = ImageTexture.create_from_image(Image.load_from_file(dir.path_join(String(key))))
	return painted


func set_terrain_set(set_name: String) -> void:
	terrain_set = set_name
	var blend := set_name == "blend"
	var painted := _terrain_texture_cache(set_name)
	var detail_name: String = String(info.get("terrain_detail", "")) if info.get("terrain_detail") != null else ""   # null in some zones
	var detail := materials.texture(detail_name) if detail_name != "" else null
	for mat in _terrain_mats:
		mat.set_shader_parameter("use_splat", blend)
		mat.set_shader_parameter("tile_tex", _terrain_textures[painted])
		mat.set_shader_parameter("detail_tex", detail)
		mat.set_shader_parameter("detail_mean", _detail_mean)
		if blend:
			_bind_splat(mat)


func _bind_splat(mat: ShaderMaterial) -> void:
	var patch: Dictionary = info.ground_splat.patches.get(mat.get_meta("patch"), {})
	var layers: Array = patch.get("layers", [])
	if layers.is_empty():
		mat.set_shader_parameter("use_splat", false)
		return
	mat.set_shader_parameter("base_tex", materials.texture(String(layers[0].tex)))
	mat.set_shader_parameter("base_tiles", float(layers[0].tiles))
	var texs: Array = []
	var tiles := PackedFloat32Array()
	var tileable := PackedFloat32Array()
	for i in 8:
		var l: Dictionary = layers[i + 1] if i + 1 < layers.size() else layers[0]
		texs.append(materials.texture(String(l.tex)))
		tiles.append(float(l.tiles))
		tileable.append(1.0 if l.tileable else 0.0)
	mat.set_shader_parameter("layer_tex", texs)
	mat.set_shader_parameter("layer_tex_clamped", texs)        # (the whole-zone layers: no wrap at the borders)
	mat.set_shader_parameter("layer_tiles", tiles)
	mat.set_shader_parameter("layer_tileable", tileable)
	mat.set_shader_parameter("layer_count", mini(layers.size() - 1, 8))
	var weights: Array = patch.weights
	for k in 2:
		var key := String(weights[k])
		if not _terrain_textures.has(key):
			var img := Image.load_from_file(dir.path_join(key))
			_terrain_textures[key] = ImageTexture.create_from_image(img)
		mat.set_shader_parameter("weights%d" % k, _terrain_textures[key])


func set_detail_strength(v: float) -> void:
	for mat in _terrain_mats:
		mat.set_shader_parameter("detail_strength", v)


# ---------------------------------------------------------------- water

func _build_water() -> void:
	var n := water_mask.get_width()
	var step := float(info.size_m) / n
	_water_mats.clear()
	var bodies := []                                          # [body, mesh, texture name]
	var ids := PackedByteArray()
	ids.resize(n * n)
	for z in n:
		for x in n:
			ids[z * n + x] = roundi(water_mask.get_pixel(x, z).r * 255.0)
	for body in info.water:
		var id := int(body.id)
		var level := float(body.height)
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		st.set_normal(Vector3.UP)
		var quads := 0
		var border: Array[Vector2i] = []
		for z in n:
			for x in n:
				if ids[z * n + x] == id:
					_water_quad(st, x * step, z * step, step, level)
					quads += 1
					continue
				# a one-cell border round the body's cells, so the surface meets the shore under the terrain
				var near := false
				for dz in range(-1, 2):
					for dx in range(-1, 2):
						var xx := clampi(x + dx, 0, n - 1)
						var zz := clampi(z + dz, 0, n - 1)
						if ids[zz * n + xx] == id:
							near = true
				if near:
					border.append(Vector2i(x, z))
		quads += _water_border(st, border, ids, id, n, step, level)
		if quads == 0:
			continue
		var wtex := _water_texture_name(body)
		if wtex != "":
			materials.texture(wtex)                          # (decoded here, on the worker)
		bodies.append([body, st.commit(), wtex])
	_water_maps_cache()
	var mats := []
	for b in bodies:
		mats.append(_water_material(b[0], b[2]))
	GpuJobs.ready_materials(mats)                          # (their render-server materials made on the main thread)
	for i in bodies.size():
		var mi := MeshInstance3D.new()
		mi.name = "Water_%s" % bodies[i][0].name
		mi.mesh = bodies[i][1]
		mi.material_override = mats[i]
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(mi)
	_bind_water_textures()


func _water_texture_name(body: Dictionary) -> String:
	var wtex := String(body.get("texture", ""))
	if wtex == "" and body.get("base_color") == null:    # neither (Cornwall's ocean): the zone's other water
		for b2 in info.get("water", []):
			if String(b2.get("texture", "")) != "":
				wtex = String(b2.texture)
				break
	return wtex


## One water body's material.
func _water_material(body: Dictionary, wtex: String) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = WATER_SHADER
	mat.set_meta("texture", wtex)
	if wtex == "":
		# the Shrouded Isles' water has no texture - sector.dat base_color (AARRGGBB 5a555040) instead, under the
		# same ripple and sky reflection (it drew plain white: owner 2026-10-10, Inishail Island / World's End)
		var bc: Array = body.get("base_color") if body.get("base_color") is Array else [0.3333, 0.3137, 0.251, 0.3529]
		mat.set_shader_parameter("use_color", true)
		mat.set_shader_parameter("base_color", Color(bc[0], bc[1], bc[2]))
		mat.set_shader_parameter("opacity", clampf(float(bc[3]), 0.3, 0.9))   # its own alpha (0x5a = 0.35): the ground
		mat.set_shader_parameter("env_amount", 0.2)                          # shows through (Inishail's shallow swamp)
		# deep water darkens to near opaque (the shallows keep the alpha); the open sea takes more of the rippled
		# sky (owner 2026-10-15: Modernagrav's sea was see-through to the bottom - the client's is dark grey)
		mat.set_shader_parameter("depth_fade", true)
		if String(body.get("type", "")) == "OCEAN":
			mat.set_shader_parameter("env_amount", 0.4)
	# DAoC flow values run 0..~30; still ponds keep a slow shimmer
	var speed := 0.004 + float(body.get("flow", 0.0)) * 0.0009
	mat.set_shader_parameter("drift", Vector2(speed, speed * 0.4))
	_water_mats.append(mat)
	return mat


func _water_quad(st: SurfaceTool, x0: float, z0: float, w: float, level: float) -> void:
	st.add_vertex(Vector3(x0, level, z0))
	st.add_vertex(Vector3(x0 + w, level, z0))
	st.add_vertex(Vector3(x0, level, z0 + w))
	st.add_vertex(Vector3(x0 + w, level, z0))
	st.add_vertex(Vector3(x0 + w, level, z0 + w))
	st.add_vertex(Vector3(x0, level, z0 + w))


## The border cells, at the drawn ground's own spacing: under the ground (hidden) they stay, so the surface meets the
## shore; where the ground dips below the water they stay only when that dip runs on from the body's own water - a
## hollow beside the pond, behind a rise, is dry land in the client (owner 2026-10-14: West Svealand loc 12915 51372,
## "water sticking out of the ground" - Pond 2's border over a hollow below its level).
func _water_border(st: SurfaceTool, border: Array[Vector2i], ids: PackedByteArray, id: int, n: int, step: float,
		level: float) -> int:
	var k := maxi(1, roundi(step / cell))                  # sub-cells per mask cell (2 on the 2x ground)
	var sub := step / k
	var wet := func(sx: int, sz: int) -> bool:             # the ground dips below the water somewhere in this sub-cell
		var x0 := sx * sub
		var z0 := sz * sub
		return minf(minf(_grid_height(x0, z0), _grid_height(x0 + sub, z0)),
				minf(_grid_height(x0, z0 + sub), _grid_height(x0 + sub, z0 + sub))) < level
	var in_body := func(sx: int, sz: int) -> bool:
		if sx < 0 or sz < 0:
			return false
		var cx := sx / k
		var cz := sz / k
		return cx >= 0 and cz >= 0 and cx < n and cz < n and ids[cz * n + cx] == id
	var cells := {}                                        # border sub-cell -> wet
	for c in border:
		for j in k:
			for i in k:
				var s := Vector2i(c.x * k + i, c.y * k + j)
				cells[s] = wet.call(s.x, s.y)
	var reached := {}
	var todo: Array[Vector2i] = []
	var sides: Array[Vector2i] = [Vector2i(1, 0), Vector2i(-1, 0), Vector2i(0, 1), Vector2i(0, -1)]
	for s in cells:
		if not cells[s]:
			continue
		for d in sides:                                    # beside the body's own water
			var o: Vector2i = s + d
			if in_body.call(o.x, o.y) and wet.call(o.x, o.y):
				reached[s] = true
				todo.append(s)
				break
	while not todo.is_empty():                             # and on through the wet border
		var s: Vector2i = todo.pop_back()
		for d in sides:
			var o: Vector2i = s + d
			if cells.get(o, false) and not reached.has(o):
				reached[o] = true
				todo.append(o)
	var quads := 0
	for s in cells:
		if not cells[s] or reached.has(s):
			_water_quad(st, s.x * sub, s.y * sub, sub, level)
			quads += 1
	return quads


## Sun direction (towards the sun) and sky colour for the water's glints and reflection fallback.
func set_water_light(sun_dir: Vector3, sun_color: Color, sky: Color) -> void:
	for mat in _water_mats:
		mat.set_shader_parameter("sun_dir", sun_dir)
		mat.set_shader_parameter("sun_color", sun_color)
		mat.set_shader_parameter("sky_color", sky)


## The client's water data maps, read and decoded (kept) - on the zone worker; the materials only bind them.
func _water_maps_cache() -> void:
	var maps: Dictionary = info.get("water_maps", {})
	for key in maps:                                  # client water data maps, loaded as-is (never upscaled)
		if not _terrain_textures.has(maps[key]):
			var img := Image.load_from_file(dir.path_join(maps[key]))
			img.generate_mipmaps()
			_terrain_textures[maps[key]] = ImageTexture.create_from_image(img)


func _bind_water_textures() -> void:
	var maps: Dictionary = info.get("water_maps", {})
	_water_maps_cache()
	for mat in _water_mats:
		var tex_name: String = mat.get_meta("texture")
		if tex_name != "":
			mat.set_shader_parameter("water_tex", materials.texture(tex_name))
		if maps.has("bump"):
			mat.set_shader_parameter("bump_tex", _terrain_textures[maps.bump])
		if maps.has("env"):
			mat.set_shader_parameter("env_tex", _terrain_textures[maps.env])


# ---------------------------------------------------------------- lights

## The zone's point lights (lights.csv typed by zones/textures/LIGHTDEF.CSV): torches, fires, the purple glows of
## Hibernian towns. Colour and radius come from the client; how bright they are by time of day is set_light_level.
func _build_lights() -> void:
	for l in info.get("lights", []):
		var o := OmniLight3D.new()
		o.position = Vector3(l.pos[0], l.pos[1], l.pos[2])
		o.light_color = Color(l.color[0], l.color[1], l.color[2])
		o.omni_range = float(l.range_m) * 1.5               # gap-fill: client radius is where the glow fades out
		o.omni_attenuation = 1.2
		if String(l.get("name", "")).to_lower().contains("fire"):
			# fires (fire / BIG / GIANT fire-light): the old client lit per vertex - a fire coloured every vertex in
			# its radius and the colour spread over whole big triangles, a broad orange wash over the rock round a
			# brazier (owner 2026-10-15, Trollheim's entrance: "why does our trollheim fires not light up like this" -
			# here a small hot spot). Wider and flatter, every zone ("fires should match the old client everywhere").
			o.omni_range = float(l.range_m) * 2.5
			o.omni_attenuation = 0.5
		o.shadow_enabled = false
		o.distance_fade_enabled = true                      # keep dozens of town lights cheap
		o.distance_fade_begin = 120.0
		o.distance_fade_length = 40.0
		o.set_meta("base_energy", 2.2)
		add_child(o)
		_lights.append(o)
	set_light_level(0.0)


## Lights that move with something (the player's torch) join the zone's night scaling.
func add_dynamic_light(o: OmniLight3D, base_energy: float) -> void:
	o.set_meta("base_energy", base_energy)
	_lights.append(o)
	o.light_energy = base_energy * (0.15 + 0.85 * _light_level)


var _light_level := 0.0


## 0 = full daylight (lights barely show), 1 = night (full glow).
func set_light_level(level: float) -> void:
	_light_level = level
	for o in _lights:
		if is_instance_valid(o):
			o.light_energy = float(o.get_meta("base_energy")) * (0.15 + 0.85 * level)


# ---------------------------------------------------------------- fixtures

func _build_fixtures() -> void:
	var per_model := {}
	for f in info.fixtures:
		var m := int(f.m)
		if not per_model.has(m):
			per_model[m] = []
		per_model[m].append(f)
	var body := StaticBody3D.new()
	body.name = "FixtureBodies"
	body.collision_layer = LAYER_WALL
	body.collision_mask = 0
	add_child(body)
	# floors: the client's ground pick sees the Collide fixtures' scene nodes plus the dynamic objects (see picked), so
	# their visible faces are floors on the floor layer; every other fixture's faces are scenery (the camera arm)
	var floor_body := StaticBody3D.new()
	floor_body.name = "FloorBodies"
	floor_body.collision_layer = LAYER_FLOOR
	floor_body.collision_mask = 0
	add_child(floor_body)
	var scenery_body := StaticBody3D.new()
	scenery_body.name = "SceneryBodies"
	scenery_body.collision_layer = LAYER_SCENERY
	scenery_body.collision_mask = 0
	add_child(scenery_body)
	var proxy_cache := {}
	var floor_cache := {}
	var shape_cache := {}
	# ladders: the client's 'climb' nodes are walls, and touching one starts climbing (Player reads the body's name)
	var climb_body := StaticBody3D.new()
	climb_body.name = "ClimbBodies"
	climb_body.collision_layer = LAYER_WALL
	climb_body.collision_mask = 0
	add_child(climb_body)
	var climb_cache := {}
	for m in per_model:
		var model: Dictionary = info.models[m]
		var parts := _load_model(dir.path_join(model.file), bool(model.get("ambient_only", false)))
		if parts.is_empty() or (parts.meshes.is_empty() and parts.particles.is_empty() and parts.doors.is_empty()):
			continue
		var xforms: Array[Transform3D] = []
		for f in per_model[m]:
			var basis := Basis(Vector3.UP, deg_to_rad(float(f.yaw))).scaled(Vector3.ONE * float(f.scale))
			if f.has("basis"):                                 # dungeon pieces: a full rotation (columns x, y, z)
				var bb: Array = f.basis
				basis = Basis(Vector3(bb[0], bb[1], bb[2]), Vector3(bb[3], bb[4], bb[5]), Vector3(bb[6], bb[7], bb[8]))
			xforms.append(Transform3D(basis, Vector3(f.pos[0], f.pos[1], f.pos[2])))
		# one batch per model part per map cell, so the renderer can skip cells that are off screen or far away
		var cells := {}
		for i in xforms.size():
			var c := Vector2i(clampi(int(xforms[i].origin.x / FIXTURE_CELL_M), 0, 255),
					clampi(int(xforms[i].origin.z / FIXTURE_CELL_M), 0, 255))
			if not cells.has(c):
				cells[c] = []
			cells[c].append(i)
		var size := _parts_radius(parts) * 2.0 if not parts.meshes.is_empty() else 2.0
		var far := clampf(VIEW_RANGE_PER_M * size, VIEW_RANGE_MIN_M, VIEW_RANGE_MAX_M)
		# the server's world objects baked in (items/...: banners, pennants, barrels) end at the original's far plane,
		# the region's fog clip (game.dll sets the camera's far to it). A tower's 40 m pennant got the 900 m range of
		# its height and hung over the treeline from far beyond what the client drew, its pole too thin to see and
		# the tower hidden by the ridge (owner 2026-10-29: "floating banner at salisbury plains")
		if str(model.get("nif", "")).begins_with("items/") and info.has("fog"):
			far = minf(far, maxf(SkySystem.region_fog_clip(self), VIEW_RANGE_MIN_M))
		if model.has("effect_range_m"):
			far = float(model.effect_range_m)            # moneffects.csv max visible range
		if not parts.particles.is_empty():              # fires, smoke, glows: the NIF's own particle systems
			for spec in parts.particles:
				if String((spec as Dictionary).get("texture", "")) != "":
					materials.texture(String(spec.texture))  # (decoded here, on the worker)
			GpuJobs.run(func():                            # their materials on the main thread (GpuJobs)
				for spec in parts.particles:
					for x in xforms:
						var ps := _make_particles(spec, x, far * view_mult)
						ps.set_meta("base_far", far)
						add_child(ps), "zone particles")
		if not parts.doors.is_empty():
			for i in xforms.size():
				var fid := int((per_model[m][i] as Dictionary).get("id", -1))
				_make_doors(parts, xforms[i], int(info.get("id", 0)) * 1000000 + fid * 100 if fid > 0 else -1)
		for part in parts.meshes:
			for c in cells:
				var mm := MultiMesh.new()
				mm.transform_format = MultiMesh.TRANSFORM_3D
				mm.mesh = part[0]
				mm.instance_count = cells[c].size()
				for k in cells[c].size():
					mm.set_instance_transform(k, xforms[cells[c][k]] * part[1])
				var mmi := MultiMeshInstance3D.new()
				mmi.name = "%s_%s_%d_%d" % [model.name, part[0].resource_name, c.x, c.y]
				mmi.multimesh = mm
				mmi.set_meta("base_far", far)
				mmi.visibility_range_end = far * view_mult
				mmi.visibility_range_end_margin = far * view_mult * 0.1
				mmi.visibility_range_fade_mode = GeometryInstance3D.VISIBILITY_RANGE_FADE_SELF
				add_child(mmi)
		for i in xforms.size():
			var vf: PackedVector3Array = parts.visible_faces
			if vf.is_empty():
				continue
			var fk := "%d/%.2f" % [m, float(per_model[m][i].scale)]
			if not floor_cache.has(fk):
				var sc := PackedVector3Array()
				sc.resize(vf.size())
				for k in vf.size():
					sc[k] = vf[k] * float(per_model[m][i].scale)
				var fs := ConcavePolygonShape3D.new()
				fs.backface_collision = true
				fs.set_faces(sc)
				floor_cache[fk] = fs
			var fcs := CollisionShape3D.new()
			fcs.shape = floor_cache[fk]
			fcs.transform = xforms[i] if per_model[m][i].has("basis") else Transform3D(Basis(Vector3.UP, deg_to_rad(float(per_model[m][i].yaw))), xforms[i].origin)
			(floor_body if picked(per_model[m][i]) else scenery_body).add_child(fcs)
		# trees: the client's stand-in box around the trunk (proxy_faces), for fixtures it doesn't collide with
		for i in xforms.size():
			var pf: Dictionary = per_model[m][i]
			if picked(pf) or pf.get("no_collision", false) or int(pf.get("radius", 0)) <= 0 or pf.has("basis"):
				continue
			var at := xforms[i].origin
			var base_y := at.y if city else maxf(at.y - 512.0 * 0.025, _grid_height(at.x, at.z))
			base_y = at.y + snappedf(base_y - at.y, 0.1)        # (shapes shared between trees of a size)
			var pk := "%d/%.2f/%.1f" % [int(pf.radius), float(pf.scale), base_y - at.y]
			if not proxy_cache.has(pk):
				var ps := ConcavePolygonShape3D.new()
				ps.set_faces(proxy_faces(int(pf.radius), float(pf.scale), base_y, at, Player.STEP_LEAD))
				proxy_cache[pk] = ps
			var pcs := CollisionShape3D.new()
			pcs.shape = proxy_cache[pk]
			pcs.transform = Transform3D(Basis.IDENTITY, at)
			body.add_child(pcs)
		# walls, as the client does it: only the model's "collidee" geometry, and only for fixtures the zone marks
		# Collide (fixtures.csv). Everything else (rocks, tents, logs, docks...) is a floor you can walk up onto
		# (64-unit pick) but not a wall, like the original
		var faces: PackedVector3Array = parts.collision if parts.collision.size() > 0 else PackedVector3Array()
		for i in xforms.size():
			var f: Dictionary = per_model[m][i]
			var use: PackedVector3Array = faces
			if f.get("no_collision", false) or not f.collide:
				use = PackedVector3Array()
			if use.is_empty():
				continue
			var key := "%d/%.2f" % [m, float(f.scale)]
			if not shape_cache.has(key):
				var scaled := PackedVector3Array()
				scaled.resize(use.size())
				for k in use.size():
					scaled[k] = use[k] * float(f.scale)
				var s := ConcavePolygonShape3D.new()
				s.set_faces(scaled)
				shape_cache[key] = s
			var cs := CollisionShape3D.new()
			cs.shape = shape_cache[key]
			cs.transform = xforms[i] if f.has("basis") else Transform3D(Basis(Vector3.UP, deg_to_rad(float(f.yaw))), xforms[i].origin)
			body.add_child(cs)
		var cfaces: PackedVector3Array = parts.climb
		for i in xforms.size():
			var f: Dictionary = per_model[m][i]
			if cfaces.is_empty() or f.get("no_collision", false) or not f.collide:
				continue
			var ckey := "%d/%.2f" % [m, float(f.scale)]
			if not climb_cache.has(ckey):
				var cs2 := PackedVector3Array()
				cs2.resize(cfaces.size())
				for k in cfaces.size():
					cs2[k] = cfaces[k] * float(f.scale)
				var csh := ConcavePolygonShape3D.new()
				csh.backface_collision = true
				csh.set_faces(cs2)
				climb_cache[ckey] = csh
			var ccs := CollisionShape3D.new()
			ccs.shape = climb_cache[ckey]
			ccs.transform = xforms[i] if f.has("basis") else Transform3D(Basis(Vector3.UP, deg_to_rad(float(f.yaw))), xforms[i].origin)
			climb_body.add_child(ccs)


## The door leaves of one placed object: each leaf posed closed / open from the NIF's keyframes (ZoneDoor); leaves
## of the same doorway (group) open together.
var doors: Array = []
var door_by_id := {}                         # the client's door id -> [ZoneDoor] (both leaves of a double door)


## base: the client's door id of this placed object without the piece - zone x 1,000,000 + fixtures.csv ID x 100
## (DoorRequestHandler: fixture = id / 100 % 10000, piece = id % 100); the piece is the door group's number
## ("door001" = 1, both leaves of a double door share it). -1: not a zone fixture (no server door).
func _make_doors(parts: Dictionary, at: Transform3D, base := -1) -> void:
	var by_group := {}
	for spec in parts.door_specs:
		var k := int(spec.id)
		if not parts.doors.has(k):
			continue
		var c: Array = spec.closed
		var o: Array = spec.open
		var closed := at * Transform3D(Basis(Vector3(c[0], c[1], c[2]), Vector3(c[3], c[4], c[5]), Vector3(c[6], c[7], c[8])),
				Vector3(c[9], c[10], c[11]))
		var opened := at * Transform3D(Basis(Vector3(o[0], o[1], o[2]), Vector3(o[3], o[4], o[5]), Vector3(o[6], o[7], o[8])),
				Vector3(o[9], o[10], o[11]))
		var d := ZoneDoor.new()
		add_child(d)
		d.setup(closed, opened, float(spec.get("seconds", 1.0)))
		for mesh in parts.doors[k].meshes:
			d.add_panel(mesh)
		var col: PackedVector3Array = parts.doors[k].collision
		if col.is_empty():                               # some NIFs have no collision piece for the leaf (Camelot
			col = parts.doors[k].panel_faces             # Hills' brit_shop): its own panel stops you and takes the click
		d.add_collision(col, LAYER_WALL)
		d.sound_flag = _door_flag()
		var g := String(spec.get("group", spec.name))
		var digits := RegEx.create_from_string("(\\d+)\\s*$").search(g)
		if base >= 0 and digits:
			d.door_id = base + int(digits.get_string(1))
			if not door_by_id.has(d.door_id):
				door_by_id[d.door_id] = []
			(door_by_id[d.door_id] as Array).append(d)
		if by_group.has(g):
			var other: ZoneDoor = by_group[g]
			d.partner = other
			if other.partner == null:
				other.partner = d
		else:
			by_group[g] = d
		doors.append(d)


## The cc_doors sound row for this zone's doors (sounds.json zone_door_flag: 1 outside dungeons, else silent).
var _door_flag_cache := -1


func _door_flag() -> int:
	if _door_flag_cache < 0:
		var idx = JSON.parse_string(FileAccess.get_file_as_string("res://content/sounds/sounds.json"))
		_door_flag_cache = int((idx.get("zone_door_flag", {}) as Dictionary).get(str(int(info.get("id", -1))), 1)) if idx is Dictionary else 0
	return _door_flag_cache


## Options > VIDEO view distance changed: every object's draw range times m (their own ranges kept as base_far).
func apply_view(m: float) -> void:
	for c in get_children():
		if c is GeometryInstance3D and c.has_meta("base_far"):
			var g := c as GeometryInstance3D
			var far := float(g.get_meta("base_far")) * m
			g.visibility_range_end = far
			if g is MultiMeshInstance3D:
				g.visibility_range_end_margin = far * 0.1


## One NetImmerse particle system as GPU particles at a fixture instance (see NifParticles).
func _make_particles(spec: Dictionary, at: Transform3D, far: float) -> GPUParticles3D:
	return NifParticles.build(spec, at, far, materials)


func _parts_radius(parts: Dictionary) -> float:
	var box := AABB()
	var first := true
	for part in parts.meshes:
		var b: AABB = part[1] * (part[0] as Mesh).get_aabb()
		box = b if first else box.merge(b)
		first = false
	return box.size.length() * 0.5


## {meshes: [[Mesh, local Transform3D]], collision: faces, visible_faces: faces} for one converted model.
## ambient_only: the zone's nifs.csv "Ambient Only" column - lit by the ambient light alone (NifMaterials).
## On the zone worker: the file parsed, its textures decoded and its scene made here; every face read from the parsed
## arrays (never Mesh.get_faces(), a read-back from the GPU), and its materials' render-server materials made on the
## main thread before they are set on meshes (GpuJobs.ready_materials).
func _load_model(path: String, ambient_only := false) -> Dictionary:
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	if doc.append_from_file(path, state) != OK:
		push_warning("could not load " + path)
		return {}
	var specs: Array = state.json.get("extras", {}).get("nif_materials", [])
	if ambient_only:
		specs = specs.map(func(sp): return (sp as Dictionary).merged({"ambient_only": true}, true))
	for sp in specs:
		for slot in ((sp as Dictionary).get("slots", {}) as Dictionary).values():
			materials.texture(String(slot.tex))
	var mesh_faces := []                               # glTF mesh index -> its triangles (mesh space)
	for gm in state.get_meshes():
		mesh_faces.append(importer_faces((gm as GLTFMesh).mesh))
	# the converted materials put in the glTF's own placeholders' places ("nifmat:<n>"), so the scene is made with them:
	# a placeholder made on its way (a StandardMaterial3D, its shader compiled) was thrown away - 539 of them, ~20 ms
	# each, in a few zone loads
	var gmats := state.get_materials()
	for i in gmats.size():
		var gname := gmats[i].resource_name if gmats[i] else ""
		if gname.begins_with("nifmat:") and int(gname.substr(7)) < specs.size():
			gmats[i] = materials.material(specs[int(gname.substr(7))])
	state.set_materials(gmats)
	GpuJobs.ready_materials(Array(gmats))
	var made := _model_scene(doc, state, specs, path)
	var out := {"meshes": [], "collision": PackedVector3Array(), "visible_faces": PackedVector3Array(),
			"climb": PackedVector3Array(),
			"particles": state.json.get("extras", {}).get("nif_particles", []),
			"doors": {}, "door_specs": state.json.get("extras", {}).get("nif_doors", [])}
	for e in made:
		var mi_name: String = e[0]
		var local: Transform3D = e[1]
		var mesh: Mesh = e[2]
		var gi: int = e[3]
		var raw: PackedVector3Array = mesh_faces[gi] if gi >= 0 and gi < mesh_faces.size() else PackedVector3Array()
		# door leaves (nif_doors): panels "door<k>_..." and collision "doorcol<k>_..." in the leaf's own frame,
		# kept apart from the static batches so they can swing open
		if mi_name.begins_with("door"):
			var is_col := mi_name.begins_with("doorcol")
			var k := int(mi_name.trim_prefix("doorcol" if is_col else "door").get_slice("_", 0))
			if not out.doors.has(k):
				out.doors[k] = {"meshes": [], "collision": PackedVector3Array(), "panel_faces": PackedVector3Array()}
			if is_col:
				out.doors[k].collision.append_array(raw)
			else:
				out.doors[k].meshes.append(mesh)
				out.doors[k].panel_faces.append_array(raw)
			continue
		var faces: PackedVector3Array = local * raw
		if mi_name.begins_with("collision_"):
			out.collision.append_array(faces)
			continue
		if mi_name.begins_with("climb_"):                  # a ladder's climb wall (nif_static.climb_shapes)
			out.climb.append_array(faces)
			continue
		out.visible_faces.append_array(faces)
		out.meshes.append([mesh, local])
	return out


## The parsed model's scene made, the converted materials set on its drawn meshes, and each mesh with where it sits in
## the model and its glTF mesh: [[name, local Transform3D, Mesh, glTF mesh index], ...].
func _model_scene(doc: GLTFDocument, state: GLTFState, specs: Array, path: String) -> Array:
	# each node's glTF mesh by its name (unique in the file; the generated mesh nodes keep it - get_scene_node() gives
	# the importer's nodes, replaced by the time the scene is returned)
	var mesh_of := {}
	for g in state.get_nodes():
		mesh_of[String((g as GLTFNode).resource_name)] = (g as GLTFNode).mesh
	var root := doc.generate_scene(state)
	var made := []
	for node in root.find_children("*", "MeshInstance3D", true, false):
		var mi := node as MeshInstance3D
		var local := Transform3D.IDENTITY
		var p: Node = mi
		while p != root and p is Node3D:
			local = (p as Node3D).transform * local
			p = p.get_parent()
		var n := String(mi.name)
		var drawn := not (n.begins_with("doorcol") or n.begins_with("collision_") or n.begins_with("climb_"))
		if drawn:
			var mesh := mi.mesh
			for i in mesh.get_surface_count():
				var src := mesh.surface_get_material(i)
				var mat_name := src.resource_name if src else ""
				if src and src.has_meta("spec"):           # (already the converted one: _load_model)
					continue
				if mat_name.begins_with("nifmat:") and int(mat_name.substr(7)) < specs.size():
					mesh.surface_set_material(i, materials.material(specs[int(mat_name.substr(7))]))
				elif not n.begins_with("door"):
					push_warning("%s: surface %d has no converted material (%s)" % [path, i, mat_name])
		made.append([n, local, mi.mesh, int(mesh_of.get(n, -1))])
	root.free()
	return made


## A parsed glTF mesh's triangles from its own arrays, as Mesh.get_faces() gives them - without asking the GPU.
static func importer_faces(im: ImporterMesh) -> PackedVector3Array:
	var out := PackedVector3Array()
	if im == null:
		return out
	for s in im.get_surface_count():
		if im.get_surface_primitive_type(s) != Mesh.PRIMITIVE_TRIANGLES:
			continue
		var arr := im.get_surface_arrays(s)
		var v: PackedVector3Array = arr[Mesh.ARRAY_VERTEX]
		var ix = arr[Mesh.ARRAY_INDEX]
		if ix is PackedInt32Array and not (ix as PackedInt32Array).is_empty():
			var ixs: PackedInt32Array = ix
			var n0 := out.size()
			out.resize(n0 + ixs.size())
			for k in ixs.size():
				out[n0 + k] = v[ixs[k]]
		else:
			out.append_array(v)
	return out


func set_texture_set(set_name: String) -> void:
	texture_set = set_name
	materials.set_texture_set(set_name)
	_bind_water_textures()
	set_terrain_set(terrain_set)


# ---------------------------------------------------------------- textures

func _load_texture(path: String) -> Texture2D:
	var img := Image.load_from_file(path)
	if img == null:
		push_warning("missing texture " + path)
		return null
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


func _mean_luminance(img: Image) -> float:
	var small := img.duplicate() as Image
	small.resize(16, 16, Image.INTERPOLATE_BILINEAR)
	var total := 0.0
	for y in 16:
		for x in 16:
			var c := small.get_pixel(x, y)
			total += c.r * 0.299 + c.g * 0.587 + c.b * 0.114
	return total / 256.0
