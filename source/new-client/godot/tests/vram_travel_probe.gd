extends Node
## Measure (owner 2026-10-29: "zones i was out of were still on the gpu's memory causing slowdown" - 0 changes, find
## what stays): the owner's route offline with NPCs - Avalon Marsh, Camelot Hills, Campacorentin Forest, Forest
## Sauvage, the City of Camelot, back to Camelot Hills, the city again - region changes by travel_to and, outdoors, a
## neighbour streamed in by standing near a border. After each step: video / texture / buffer memory, zones loaded,
## nodes, and the client's caches that outlive a zone (monster models, figures, figure textures, helms, weapons,
## effects). A driver under the tree's root outlives the world scene. Prints [vram] lines, then quits.
##   --skip-login --zone=9 --probe=vram_travel_probe

const STEPS := [
	[0, "Camelot Hills", true],
	[8, "Campacorentin Forest", true],
	[11, "Forest Sauvage", true],
	[26, "City of Camelot", false],
	[0, "Camelot Hills again", true],
	[26, "City of Camelot again", false],
]
const ARRIVE_S := 25.0
const BORDER_S := 25.0


func _ready() -> void:
	var root := get_tree().root
	if root.has_node("VramProbeDriver"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "VramProbeDriver"
	root.add_child.call_deferred(d)
	queue_free()


class Driver extends Node:
	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		_run()

	func _mb(m: int) -> String:
		return "%d" % int(Performance.get_monitor(m) / 1048576.0)

	func _report(label: String) -> void:
		var v := get_tree().current_scene
		var region: Node = v.get("region") if v else null
		var loaded: Array = (region.get("zones") as Dictionary).keys() if region else []
		var nm = v.get("npcs") if v else null
		var npc_models: int = (nm.get("_scenes") as Dictionary).size() if nm and nm.get("_scenes") is Dictionary else -1
		print("[vram] %-48s video %s MB (textures %s, buffers %s) | zones %s | nodes %d | resources %d | caches: monster models %d, race figures %d, figure textures %d, helms %d, weapons %d, effects %d" % [
				label, _mb(Performance.RENDER_VIDEO_MEM_USED), _mb(Performance.RENDER_TEXTURE_MEM_USED),
				_mb(Performance.RENDER_BUFFER_MEM_USED), str(loaded), int(Performance.get_monitor(Performance.OBJECT_NODE_COUNT)),
				int(Performance.get_monitor(Performance.OBJECT_RESOURCE_COUNT)), npc_models, Fig3Figure._scenes.size(),
				Fig3Figure._tex_cache.size(), Fig3Figure._helm_scenes.size(), WeaponModels._scenes.size(), NifScene._packed.size()])

	func _run() -> void:
		await get_tree().create_timer(30.0).timeout
		_report("start: Avalon Marsh")
		_to_border()
		await get_tree().create_timer(BORDER_S).timeout
		_report("Avalon Marsh + a neighbour streamed")
		for step in STEPS:
			get_tree().current_scene.travel_to(int(step[0]), [819.2, 0.0, 819.2], 0.0)
			await get_tree().create_timer(ARRIVE_S).timeout
			_report(String(step[1]))
			if bool(step[2]):
				_to_border()
				await get_tree().create_timer(BORDER_S).timeout
				_report(String(step[1]) + " + a neighbour streamed")
		print("[vram] done")
		get_tree().quit()

	func _to_border() -> void:
		var v := get_tree().current_scene
		var region: Node = v.get("region") if v else null
		if region == null or not region.has_method("neighbours"):
			return
		var ns: Array = region.call("neighbours")
		if ns.is_empty():
			return
		var size := float(v.zone.info.size_m)
		var c := Vector3(size / 2.0, 0.0, size / 2.0)
		var nc: Vector3 = region.call("offset_of", int(ns[0])) + c
		var dir := Vector3(nc.x - c.x, 0.0, nc.z - c.z).normalized()
		var p := c + dir * (size / 2.0 - 200.0)
		p.y = float(v.zone.height_at(p.x, p.z)) + 1.0
		v.player.global_position = p
		v.player.velocity = Vector3.ZERO
