class_name CacheTrim
## Lets go of what the shared character caches hold once nothing drawn uses it (owner 2026-10-29: "zones i was out of
## were still on the gpu's memory causing slowdown" - "dont swap out textures for lighter ones"). Measured that day
## (tests/vram_ride_probe.gd): unloaded zones are freed (a ride loop without NPCs: 801 -> 823 MB of textures), but with
## NPCs the same loop grew ~770 MB - the caches every figure, helm, weapon and effect is made from (Fig3Figure's
## textures and race figures, helms, WeaponModels, NifScene's effects) kept everything they had ever loaded, so a
## long ride or a busy server filled the 6 GB card; past it the driver moves memory to system RAM and the frame rate
## stays low until a restart (22:36 on 2026-10-28: 6.58 GB, 56 -> 4-12 fps).
##
## An entry goes when no live figure uses it and it wasn't asked for in IDLE_MS. The same files load again, at the
## same quality, if an NPC wearing it comes back. Textures count as used while anything holds them besides the cache
## (each figure keeps its own references); a packed scene while any copy made from it is alive (track()). Above
## BUDGET_MB of Godot's video memory the wait is cut to BUSY_IDLE_MS. Main thread only (the caches are filled there).

const EVERY_S := 20.0
const IDLE_MS := 60000
const BUSY_IDLE_MS := 5000
const BUDGET_MB := 4300.0                      # (an RTX 2060's 6 GB less Windows, the driver and other programs)

static var _left := EVERY_S
static var _first_seen := {}                  # "cache|key" -> msec it was first seen or last asked for
static var _live := {}                        # "cache|key" -> [WeakRef] copies made from a packed scene
static var trimmed := 0                       # (tests: entries let go so far)
# One copy of each texture file the weapon, helm and monster models use (owner 2026-10-29). A glTF load decodes its
# own copy of every image it names, so the 175 weapons on b_weapons01 (2048 x 2048, ~22 MB with mipmaps), the 117
# helms on b_lea2_helm1 and the 49 monsters on a half-ogre skin each held their own - measured: the weapon cache at
# 300-430 MB for 9-30 weapons. Same files, same quality: the second model's materials take the first's texture.
static var _shared := {}                      # absolute image path -> Texture2D
static var _shared_mutex := Mutex.new()       # (models are packed on the zone and NPC-view workers)


## A glTF just read (append_from_file, before generate_scene): its materials draw the shared copy of each image file
## another model already loaded; its own first copies become the shared ones. Any thread.
static func share_textures(state: GLTFState, gltf_path: String) -> void:
	var images := state.get_images()
	var listed: Array = state.json.get("images", []) if state.json is Dictionary else []
	if images.is_empty() or listed.size() != images.size():
		return
	var swap := {}                                     # this load's texture -> the shared one
	var dir := ProjectSettings.globalize_path(gltf_path).get_base_dir()
	_shared_mutex.lock()
	for i in images.size():
		var uri := String((listed[i] as Dictionary).get("uri", ""))
		if uri == "" or uri.begins_with("data:") or images[i] == null:
			continue
		var key := dir.path_join(uri.uri_decode()).simplify_path().to_lower()
		var have = _shared.get(key)
		if have != null and have != images[i]:
			swap[images[i]] = have
		elif have == null:
			_shared[key] = images[i]
	_shared_mutex.unlock()
	if swap.is_empty():
		return
	for m in state.get_materials():
		if m is BaseMaterial3D:
			for t in BaseMaterial3D.TEXTURE_MAX:
				var tex := (m as BaseMaterial3D).get_texture(t)
				if tex != null and swap.has(tex):
					(m as BaseMaterial3D).set_texture(t, swap[tex])
	var out: Array[Texture2D] = []
	for t in images:
		out.append(swap.get(t, t))
	state.set_images(out)


## A copy was made from the cache's entry (it is in use while the copy lives).
static func track(cache: String, key: String, node: Object) -> void:
	if node == null:
		return
	var k := cache + "|" + key
	_first_seen[k] = Time.get_ticks_msec()
	var list: Array = _live.get(k, [])
	list = list.filter(func(w): return (w as WeakRef).get_ref() != null)
	list.append(weakref(node))
	_live[k] = list


## The entry was asked for (a texture handed out).
static func touch(cache: String, key: String) -> void:
	_first_seen[cache + "|" + key] = Time.get_ticks_msec()


## Every frame from the world (ZoneViewer): a pass every EVERY_S (true on the frames it ran).
static func tick(delta: float) -> bool:
	_left -= delta
	if _left > 0.0:
		return false
	_left = EVERY_S
	trim()
	return true


static func over_budget() -> bool:
	return Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED) / 1048576.0 > BUDGET_MB


## One pass over the caches; returns how many entries were let go.
static func trim() -> int:
	var idle := BUSY_IDLE_MS if over_budget() else IDLE_MS
	var now := Time.get_ticks_msec()
	var n := 0
	var base := _base_refs()
	n += _trim_textures(Fig3Figure._tex_cache, "figtex", base, now, idle)
	n += _trim_scenes(Fig3Figure._scenes, "figure", now, idle)
	n += _trim_scenes(Fig3Figure._helm_scenes, "helm", now, idle)
	n += _trim_scenes(WeaponModels._scenes, "weapon", now, idle)
	n += _trim_scenes(NifScene._packed, "fx", now, idle)
	_shared_mutex.lock()
	n += _trim_textures(_shared, "shared", base, now, idle)
	_shared_mutex.unlock()
	trimmed += n
	return n


## How many references a RefCounted held only by a Dictionary shows when read the way _refs_of reads it.
static func _base_refs() -> int:
	var d := {"k": RefCounted.new()}
	return _refs_of(d, "k")


static func _refs_of(d: Dictionary, k: Variant) -> int:
	var v: RefCounted = d[k]
	return v.get_reference_count()


## Recently asked for (or never seen before: noted now, judged on a later pass).
static func _fresh(k: String, now: int, idle: int) -> bool:
	if not _first_seen.has(k):
		_first_seen[k] = now
		return true
	return now - int(_first_seen[k]) < idle


static func _trim_textures(cache: Dictionary, name: String, base: int, now: int, idle: int) -> int:
	var n := 0
	for key in cache.keys():
		var k := name + "|" + String(key)
		if cache[key] == null or not (cache[key] is RefCounted):
			continue                                       # (a missing file: kept, so it isn't looked for again)
		if _fresh(k, now, idle) or _refs_of(cache, key) > base:
			continue
		cache.erase(key)
		_first_seen.erase(k)
		n += 1
	return n


static func _trim_scenes(cache: Dictionary, name: String, now: int, idle: int) -> int:
	var n := 0
	for key in cache.keys():
		var k := name + "|" + String(key)
		if cache[key] == null:
			continue
		var list: Array = (_live.get(k, []) as Array).filter(func(w): return (w as WeakRef).get_ref() != null)
		if list.is_empty():
			_live.erase(k)
		else:
			_live[k] = list
		if _fresh(k, now, idle) or not list.is_empty():
			continue
		cache.erase(key)
		_first_seen.erase(k)
		n += 1
	return n
