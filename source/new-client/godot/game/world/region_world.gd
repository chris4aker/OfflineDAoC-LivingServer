class_name RegionWorld
extends Node
## Seamless zones (bot plan priority 1): the original client streams every outdoor zone of a region as one world -
## walking or riding from one zone into the next has no loading screen and no border. zones.dat places a region's
## zones side by side (content/zones/zones_index.json: x, y, w, h in 8192-unit blocks); here the zone you are in sits
## at the scene origin and its neighbours at their offsets (1 block = 204.8 m). Neighbours within LOAD_M of you are
## built on a worker thread while you play (one at a time) and added whole; beyond KEEP_M they are released. Crossing
## a border makes the neighbour the current zone and shifts the whole scene by its offset (cross), so everything that
## works in "the zone's" metres - heights, exits, the map, quests, saving - keeps working unchanged.
## Loading screens stay where the old client had them: changing region (dungeons, cities, the Shrouded Isles ...).

signal zone_added(z: ZoneLoader)            # a neighbour is in the world (at its offset)
signal zone_removing(z: ZoneLoader)         # a neighbour is about to go

const BLOCK_M := 204.8                       # 8192 units
const LOAD_M := 700.0                        # a neighbour this close to you is built...
const KEEP_M := 1000.0                       # ...and kept until you are this far from it
const RIDE_LOAD_M := 1100.0                  # on a horse route (1500 units/s) the next zones come sooner

var viewer: Node
var current: ZoneLoader
var zones := {}                              # zone id -> ZoneLoader (the current zone too)
var terrain_set := "blend"
var texture_set := "x4plus"
var _index := {}
var _building := -1                          # the zone id on the worker now
var _built: ZoneLoader
var _task: Thread                            # the zone building now (its own thread: GpuJobs.start_job)
var _check_left := 0.0
var _failed := {}                            # zone ids that didn't build (not retried)
var _loading_now := false                    # load_around is building behind the loading screen: no streaming meanwhile


func setup(v: Node, zone: ZoneLoader, terrain: String, textures: String) -> void:
	viewer = v
	current = zone
	terrain_set = terrain
	texture_set = textures
	zones[int(zone.info.id)] = zone
	var parsed = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json")) \
			if FileAccess.file_exists("res://content/zones/zones_index.json") else null
	_index = parsed if parsed is Dictionary else {}


func entry(id: int) -> Dictionary:
	return _index.get(str(id), {})


## Options > VIDEO view distance (1 = the reach above): a longer view loads the zones farther around you too, so
## the land is there out to the fog.
static func _view() -> float:
	return maxf(ZoneLoader.view_mult, 1.0)


## The scene position of a zone's corner (its offset from the current zone's), in metres.
func offset_of(id: int) -> Vector3:
	var me := entry(int(current.info.id))
	var it := entry(id)
	if me.is_empty() or it.is_empty():
		return Vector3.ZERO
	return Vector3((float(it.x) - float(me.x)) * BLOCK_M, 0.0, (float(it.y) - float(me.y)) * BLOCK_M)


## The outdoor zones of the current zone's region that are built (cities and dungeons are their own regions).
func neighbours() -> Array:
	var me := entry(int(current.info.id))
	var out := []
	if me.is_empty() or current.city or bool(current.info.get("dungeon", false)):
		return out
	for k in _index:
		var z: Dictionary = _index[k]
		if int(k) == int(current.info.id) or int(z.get("region", -1)) != int(me.region) or int(z.get("type", 0)) != 0:
			continue
		if FileAccess.file_exists("res://content/zones/zone%03d/zone.json" % int(k)):
			out.append(int(k))
	return out


## How far a scene point is from a zone's ground (0 inside it).
func distance_to(id: int, p: Vector3) -> float:
	var o := offset_of(id)
	var it := entry(id)
	var w := float(it.get("w", 8)) * BLOCK_M
	var h := float(it.get("h", 8)) * BLOCK_M
	var dx := maxf(maxf(o.x - p.x, 0.0), p.x - (o.x + w))
	var dz := maxf(maxf(o.z - p.z, 0.0), p.z - (o.z + h))
	return Vector2(dx, dz).length()


## The region zone (id) under a scene point, built or not; -1 outside every zone of the region.
func zone_id_at(x: float, z: float) -> int:
	var me := entry(int(current.info.id))
	if me.is_empty():
		return int(current.info.id)
	var gx := float(me.x) + x / BLOCK_M
	var gy := float(me.y) + z / BLOCK_M
	var size := float(current.info.size_m)
	if x >= 0.0 and z >= 0.0 and x < size and z < size:
		return int(current.info.id)
	for k in _index:
		var it: Dictionary = _index[k]
		if int(it.get("region", -1)) != int(me.region) or int(it.get("type", 0)) != 0:
			continue
		if gx >= float(it.x) and gy >= float(it.y) and gx < float(it.x) + float(it.w) and gy < float(it.y) + float(it.h):
			return int(k)
	return -1


## The loaded zone under a scene point (the current zone when none is).
func zone_at(x: float, z: float) -> ZoneLoader:
	var size := float(current.info.size_m)
	if x >= 0.0 and z >= 0.0 and x < size and z < size:
		return current
	for id in zones:
		var zl: ZoneLoader = zones[id]
		var o := zl.position
		var s := float(zl.info.size_m)
		if x >= o.x and z >= o.z and x < o.x + s and z < o.z + s:
			return zl
	return current


func height_at(x: float, z: float, near_y := INF) -> float:
	var zl := zone_at(x, z)
	return zl.height_at(x - zl.position.x, z - zl.position.z, near_y)


func water_level_at(x: float, z: float) -> float:
	var zl := zone_at(x, z)
	return zl.water_level_at(x - zl.position.x, z - zl.position.z)


## You walked (or rode) into zone id: it becomes the current zone at the origin; returns the shift d - everything
## else in the scene moves by -d (the zone viewer does that).
func cross(id: int) -> Vector3:
	var nz: ZoneLoader = zones[id]
	var d := nz.position
	for k in zones:
		(zones[k] as ZoneLoader).position -= d
	current = nz                                       # (a zone still on the worker is placed at its offset from this one)
	return d


func _process(delta: float) -> void:
	if current == null:
		return
	if _task != null and GpuJobs.job_done(_task):
		GpuJobs.finish_job(_task)
		_task = null
		_place_built()
	_check_left -= delta
	if _check_left > 0.0 or _loading_now:
		return
	_check_left = 0.5
	var player: Node3D = viewer.get("player")
	if player == null:
		return
	var p := player.global_position
	var reach := (RIDE_LOAD_M if bool(player.get("riding")) else LOAD_M) * _view()
	# release what is far behind (never the zone you're in)
	for id in zones.keys():
		if int(id) != int(current.info.id) and distance_to(int(id), p) > maxf(KEEP_M * _view(), reach + 300.0):
			_release(int(id))
	if _task != null:
		return
	var best := -1
	var best_d := INF
	for id in neighbours():
		if zones.has(id) or _failed.has(id):
			continue
		var d := distance_to(id, p)
		if d <= reach and d < best_d:
			best_d = d
			best = id
	if best >= 0:
		_start_build(best)


var _packed := {}                            # the building zone's monster models (NpcManager.pack_zone_models)


func _start_build(id: int) -> void:
	_building = id
	_built = null
	_packed = {}
	var nm: NpcManager = viewer.get("npcs")
	var skip := _known_models(nm)
	var info: Dictionary = nm.models if nm else {}
	_task = GpuJobs.start_job(_build.bind(id, skip, info))


## The model paths already packed - names only: a copy of the cache itself would let the worker hold the last
## reference to a model the main thread lets go (a zone released meanwhile), and free its textures off the main thread.
func _known_models(nm: NpcManager) -> Dictionary:
	var out := {}
	if nm:
		for k in nm._scenes:
			out[String(k)] = true
	return out


func _build(id: int, skip: Dictionary, info: Dictionary) -> void:
	var z := ZoneLoader.new()
	z.name = "Zone%d" % id
	z.load_zone("res://content/zones/zone%03d" % id, terrain_set, texture_set)
	if not info.is_empty():
		_packed = NpcManager.pack_zone_models("res://content/zones/zone%03d" % id, info, skip)
	_built = z


func _place_built() -> void:
	var z := _built
	var id := _building
	_built = null
	_building = -1
	if z == null or z.info.is_empty() or zones.has(id):
		if z == null or z.info.is_empty():
			_failed[id] = true
		if z:
			z.free()
		return
	var nm: NpcManager = viewer.get("npcs")
	if nm:
		nm.adopt_models(_packed)
	_packed = {}
	z.position = offset_of(id)
	viewer.add_child(z)
	zones[id] = z
	zone_added.emit(z)


func _release(id: int) -> void:
	var z: ZoneLoader = zones.get(id)
	if z == null:
		return
	zone_removing.emit(z)
	zones.erase(id)
	z.queue_free()


## Behind the loading screen: every neighbour within reach of p built now (in parallel on the worker threads), so you
## step into a whole world.
func load_around(p: Vector3) -> void:
	var want := []
	for id in neighbours():
		if not zones.has(id) and distance_to(id, p) <= LOAD_M * _view():
			want.append(id)
	if want.is_empty():
		return
	_loading_now = true
	var built := {}
	var packed := {}
	var tasks := []
	var lock := Mutex.new()
	var nm: NpcManager = viewer.get("npcs")
	var skip := _known_models(nm)
	var info: Dictionary = nm.models if nm else {}
	for id in want:
		tasks.append(GpuJobs.start_job(func():
			var z := ZoneLoader.new()
			z.name = "Zone%d" % id
			z.load_zone("res://content/zones/zone%03d" % id, terrain_set, texture_set)
			var pm := NpcManager.pack_zone_models("res://content/zones/zone%03d" % id, info, skip) if not info.is_empty() else {}
			lock.lock()
			built[id] = z
			packed.merge(pm)
			lock.unlock()))
	while true:
		var done := true
		for t in tasks:
			if not GpuJobs.job_done(t):
				done = false
		if done:
			break
		await get_tree().process_frame
	for t in tasks:
		GpuJobs.finish_job(t)
	if nm:
		nm.adopt_models(packed)
	for id in built:
		var z: ZoneLoader = built[id]
		if zones.has(id):                              # (never two of one zone)
			z.free()
			continue
		z.position = offset_of(id)
		viewer.add_child(z)
		zones[id] = z
		zone_added.emit(z)
	_loading_now = false


func _exit_tree() -> void:
	if _task != null:
		GpuJobs.wait_job(_task)                        # (running the GPU steps it may wait on)
		_task = null
		if _built:
			_built.free()
			_built = null
