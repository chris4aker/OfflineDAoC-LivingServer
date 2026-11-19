extends Node
## Test (owner 2026-10-28: the client froze after /tc, right after Jordheim was built; the server off): the client's
## side of teleporting without the server - region changes by travel_to (the scene reload /tc and every region
## change make: the zone built on the main thread, its neighbours on the zone workers behind the loading screen),
## and after each arrival the player set 200 m from a zone border, so the neighbour behind it streams in on the
## worker as you walking up to it would. A driver under the tree's root (it outlives the world scene) counts the
## freeze's render errors, times every frame by wall clock and reads GpuJobs' counters. Prints [tele] lines.
##   --skip-login --zone=100 --no-npcs --probe=offline_tele_probe

const STEPS := [
	[120, "Jordheim (where the client froze after /tc)"],
	[100, "Vale of Mularn"],
	[0, "Camelot Hills"],
	[200, "Lough Derg"],
	[120, "Jordheim again"],
	[12, "Snowdonia"],
]
const ARRIVE_S := 20.0
const BORDER_S := 20.0


func _ready() -> void:
	var root := get_tree().root
	if root.has_node("TeleProbeDriver"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "TeleProbeDriver"
	root.add_child.call_deferred(d)
	queue_free()


class ErrorCount extends Logger:
	var mutex := Mutex.new()
	var counts := {}

	func _log_error(_function: String, _file: String, _line: int, code: String, rationale: String, _editor_notify: bool,
			_error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
		var text := (rationale + " " + code).to_lower()
		var kind := "other"
		if text.contains("open draw list"):
			kind = "open draw list"
		elif text.contains("updating buffers"):
			kind = "updating buffers"
		elif text.contains("uniform_set"):
			kind = "uniform_set null"
		mutex.lock()
		counts[kind] = int(counts.get(kind, 0)) + 1
		mutex.unlock()

	func take() -> Dictionary:
		mutex.lock()
		var c := counts.duplicate()
		counts.clear()
		mutex.unlock()
		return c


class Driver extends Node:
	var errors := ErrorCount.new()
	var frames := 0
	var max_gap_ms := 0.0
	var last_us := 0
	var t0_us := 0
	var jobs0 := 0
	var totals := {}

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		OS.add_logger(errors)
		GpuJobs.trace = true
		_run()

	func _process(_delta: float) -> void:
		var now := Time.get_ticks_usec()
		if last_us > 0:
			max_gap_ms = maxf(max_gap_ms, (now - last_us) / 1000.0)
		last_us = now
		frames += 1

	func _reset() -> void:
		frames = 0
		max_gap_ms = 0.0
		t0_us = Time.get_ticks_usec()
		jobs0 = GpuJobs.jobs_run
		GpuJobs.max_pump_usec = 0
		errors.take()

	func _report(label: String) -> void:
		var secs := (Time.get_ticks_usec() - t0_us) / 1000000.0
		var v := get_tree().current_scene
		var zone := int(v.zone.info.id) if v and v.get("zone") else -1
		var region: Node = v.get("region") if v else null
		var loaded: Array = (region.get("zones") as Dictionary).keys() if region else []
		var errs := errors.take()
		for k in errs:
			totals[k] = int(totals.get(k, 0)) + int(errs[k])
		print("[tele] %s: zone %d, zones loaded %s | %d frames in %.1f s (%.0f fps), longest frame %.0f ms | GPU steps %d (longest frame's share %.1f ms) | errors %s" % [
				label, zone, str(loaded), frames, secs, frames / maxf(secs, 0.001), max_gap_ms,
				GpuJobs.jobs_run - jobs0, GpuJobs.max_pump_usec / 1000.0, str(errs)])

	func _run() -> void:
		await get_tree().create_timer(15.0).timeout
		_reset()
		await get_tree().create_timer(5.0).timeout
		_report("start")
		for step in STEPS:
			_reset()
			var v := get_tree().current_scene
			print("[tele] -> ", step[1])
			v.travel_to(int(step[0]), [819.2, 0.0, 819.2], 0.0)
			await get_tree().create_timer(ARRIVE_S).timeout
			_report(String(step[1]) + " (arrived)")
			_reset()
			_to_border()
			await get_tree().create_timer(BORDER_S).timeout
			_report(String(step[1]) + " (by a border, a neighbour streaming)")
		print("[tele] done, errors in all: ", totals)
		var slow := []
		for k in GpuJobs.slowest:
			slow.append("%s %.1f" % [k, GpuJobs.slowest[k] / 1000.0])
		print("[tele] slowest main-thread step by kind (ms): ", slow)
		get_tree().quit()

	## The player 200 m from the border with the first neighbour zone (within the region's build distance).
	func _to_border() -> void:
		var v := get_tree().current_scene
		var region: Node = v.get("region") if v else null
		if region == null or not region.has_method("neighbours"):
			print("[tele] (no region: a single zone)")
			return
		var ns: Array = region.call("neighbours")
		if ns.is_empty():
			print("[tele] (no neighbours)")
			return
		var size := float(v.zone.info.size_m)
		var c := Vector3(size / 2.0, 0.0, size / 2.0)
		var nc: Vector3 = region.call("offset_of", int(ns[0])) + c
		var dir := Vector3(nc.x - c.x, 0.0, nc.z - c.z).normalized()
		var p := c + dir * (size / 2.0 - 200.0)
		p.y = float(v.zone.height_at(p.x, p.z)) + 1.0
		v.player.global_position = p
		v.player.velocity = Vector3.ZERO
		print("[tele] by the border with zone %d at %s" % [int(ns[0]), str(p.snapped(Vector3.ONE * 0.1))])
