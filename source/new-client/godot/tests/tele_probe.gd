extends Node
## Test (owner 2026-10-28: the client froze after /tc - "fix it ... after you've tested a few teleports"): with Claude's
## test character on the running server (server_session.tscn --char=Claudeperf --probe=tele_probe), a run of region
## and zone changes - /tc and GM /jump into every realm - each followed by 30 s in the new place, while a driver under
## the tree's root (it outlives the world scene, rebuilt on every region change) counts the render errors of the
## freeze ("Found open draw list", "Updating buffers is forbidden", "uniform_set is null"), times every frame by wall
## clock (a frozen client shows no frames) and reads GpuJobs' counters. Prints [tele] lines, then quits (/quit).

const STEPS := [
	["/tc", "Tir na Nog (/tc)"],
	["/jump to 422238 485109 2336 200", "Hibernia outdoors (region 200)"],
	["/jump to 578199 556753 2196 1", "Albion outdoors (region 1)"],
	["/jump to 704904 737619 6536 100", "Midgard outdoors (region 100)"],
	["/tc", "Tir na Nog again (/tc)"],
	["/jump to 476071 343016 4103 200", "Hibernia, another zone (region 200)"],
]
const SETTLE_S := 30.0


func _ready() -> void:
	var root := get_tree().root
	if root.has_node("TeleProbeDriver"):                 # (the world scene was rebuilt: the driver carries on)
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

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		OS.add_logger(errors)
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
		var zone := int(v.call("current_zone_id")) if v and v.has_method("current_zone_id") else -1
		var pos: Vector3 = v.player.global_position if v and v.get("player") else Vector3.INF
		print("[tele] %s: zone %d at %s | %d frames in %.1f s (%.0f fps), longest frame %.0f ms | GPU steps %d (longest frame's share %.1f ms) | errors %s" % [
				label, zone, str(pos.snapped(Vector3.ONE * 0.1)), frames, secs, frames / maxf(secs, 0.001), max_gap_ms,
				GpuJobs.jobs_run - jobs0, GpuJobs.max_pump_usec / 1000.0, str(errors.take())])

	func _run() -> void:
		await get_tree().create_timer(20.0).timeout          # the world and its neighbour zones in
		_reset()
		await get_tree().create_timer(10.0).timeout
		_report("start")
		for step in STEPS:
			_reset()
			print("[tele] -> ", step[1], "   ", step[0])
			Server.command(String(step[0]))
			await get_tree().create_timer(SETTLE_S).timeout
			_report(String(step[1]))
		print("[tele] done")
		Server.command("/quit")
		await get_tree().create_timer(6.0).timeout
		get_tree().quit()
