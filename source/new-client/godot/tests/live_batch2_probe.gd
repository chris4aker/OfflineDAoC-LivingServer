extends Node
## Live test, part 2 (Tir na Nog: empty messages, APPRAISE; ends without /quit for the login retry test) - of the 2026-10-29 batch (owner: "turn on server and run some live tests"), with Claude's test character
## on the running server (server_session.tscn --char=Claudeperf --probe=live_batch_probe):
##   1. chat gaps - a /spawn bard companion's songs pulse; empty server messages counted against blank chat lines;
##   2. lag pull-back - a jump to a place just sent is kept off, a jump elsewhere honoured;
##   3. max speed 0 - stops you (and the speed comes back);
##   4. APPRAISE - at the nearest merchant, on a backpack item: the server's answer in chat;
##   5. teleports (/tc, /jump) with NPCs about - render / script errors, frame gaps (the freeze fix).
## A driver under the tree's root outlives the world scene. Prints [live] lines, then /quit.

func _ready() -> void:
	var root := get_tree().root
	if root.has_node("LiveBatch2Driver"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "LiveBatch2Driver"
	root.add_child.call_deferred(d)
	queue_free()


class ErrorCount extends Logger:
	var mutex := Mutex.new()
	var counts := {}
	var script_errors: Array = []

	func _log_error(_function: String, file: String, line: int, code: String, rationale: String, _editor_notify: bool,
			error_type: int, _script_backtraces: Array[ScriptBacktrace]) -> void:
		var text := (rationale + " " + code).to_lower()
		var kind := "other"
		if text.contains("open draw list"):
			kind = "open draw list"
		elif text.contains("updating buffers"):
			kind = "updating buffers"
		elif text.contains("uniform_set"):
			kind = "uniform_set null"
		elif error_type == ERROR_TYPE_SCRIPT:
			kind = "script"
		mutex.lock()
		counts[kind] = int(counts.get(kind, 0)) + 1
		if kind == "script" and script_errors.size() < 10:
			script_errors.append("%s:%d %s %s" % [file, line, rationale, code])
		mutex.unlock()


class Driver extends Node:
	var errors := ErrorCount.new()
	var empty_packets := 0
	var all_packets := 0
	var blank_shown := 0
	var chat: Array = []
	var last_us := 0
	var max_gap_ms := 0.0

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		OS.add_logger(errors)
		Server.world.connect(func(kind: String, d: Dictionary):
			if kind == "message":
				all_packets += 1
				if String(d.get("text", "")).strip_edges().is_empty():
					empty_packets += 1)
		PlayerData.message.connect(func(t: String, k: String):
			if t.strip_edges().is_empty():
				blank_shown += 1
			chat.append("%s: %s" % [k, t]))
		_run()

	func _process(_d: float) -> void:
		var now := Time.get_ticks_usec()
		if last_us > 0:
			max_gap_ms = maxf(max_gap_ms, (now - last_us) / 1000.0)
		last_us = now

	func _v() -> Node:
		return get_tree().current_scene

	func _say(t: String) -> void:
		print("[live] ", t)

	func _run() -> void:
		await get_tree().create_timer(25.0).timeout
		var v := _v()
		_say("in the world: zone %d, %s" % [Server.current_zone_id(), str(v.player.global_position.snapped(Vector3.ONE * 0.1))])
		Server.command("/tc")
		await get_tree().create_timer(30.0).timeout
		await _inject_empty()
		await _appraise()
		_say("script errors: %s" % str(errors.script_errors))
		_say("errors by kind: %s" % str(errors.counts))
		_say("done - quitting WITHOUT /quit (the character stays link-dead on the server for the login test)")
		get_tree().quit()

	# 1b: empty server messages through the real handler (ServerWorld._message), next to a normal one
	func _inject_empty() -> void:
		var shown := blank_shown
		var lines := chat.size()
		for t in [16, 25, 0]:
			Server.world.emit("message", {"kind": "message", "type": t, "text": "", "popup": false, "chat": false})
			Server.world.emit("message", {"kind": "message", "type": t, "text": "  ", "popup": false, "chat": false})
		Server.world.emit("message", {"kind": "message", "type": 16, "text": "Probe line after the empty ones.", "popup": false, "chat": false})
		await get_tree().create_timer(0.5).timeout
		_say("1b empty messages: 6 empty sent through the handler, %d blank lines shown, %d lines added (1 expected)" % [blank_shown - shown, chat.size() - lines])

	# 1 ------------------------------------------------------------------------------------------------------------
	func _chat_gaps() -> void:
		chat.clear()
		Server.command("/spawn bard")
		await get_tree().create_timer(45.0).timeout
		var songs := chat.filter(func(l): return String(l).contains("speed") or String(l).contains("calm") or String(l).contains("song") or String(l).contains("Song")).size()
		_say("1 chat gaps: %d message packets, %d empty from the server, %d blank lines shown; song-ish lines %d" % [all_packets, empty_packets, blank_shown, songs])
		var tail := chat.slice(maxi(0, chat.size() - 12))
		_say("   last chat: %s" % str(tail))

	# 2 ------------------------------------------------------------------------------------------------------------
	func _pull_back() -> void:
		var v := _v()
		var p: Vector3 = v.player.global_position
		var dir := Vector3(1, 0, 0)
		for i in 60:                                       # walk 6 m east over a second (positions go out)
			p += dir * 0.1
			p.y = float(v.region.height_at(p.x, p.z)) + 0.2 if v.get("region") else p.y
			v.player.global_position = p
			await get_tree().physics_frame
		await get_tree().create_timer(0.3).timeout
		var trail: Array = v.get("_sent_trail")
		var zid := Server.current_zone_id()
		if trail.size() < 2:
			_say("2 pull-back: no trail (%d)" % trail.size())
			return
		var old: Vector3 = (trail[0] as Array)[2]
		var here: Vector3 = v.player.global_position
		v.call("_on_server_jump", {"zone": zid, "pos": old, "face": 0.0})
		await get_tree().create_timer(0.3).timeout
		var kept: bool = v.player.global_position.distance_to(here) < 0.5
		var far := here + Vector3(30, 0, 0)
		far.y = float(v.region.height_at(far.x, far.z)) + 0.3 if v.get("region") else far.y
		v.call("_on_server_jump", {"zone": zid, "pos": far, "face": 0.0})
		await get_tree().create_timer(0.3).timeout
		var moved: bool = v.player.global_position.distance_to(far) < 1.0
		_say("2 pull-back: jump to a place sent %.1f m back kept off: %s; a jump 30 m away honoured: %s" % [here.distance_to(old), str(kept), str(moved)])

	# 3 ------------------------------------------------------------------------------------------------------------
	func _speed_zero() -> void:
		var v := _v()
		var before: float = v.player.encumbrance
		var pct := int(Server.max_speed_pct)
		Server.world.emit("max_speed", {"kind": "max_speed", "percent": 0, "no_turn": 1, "water": 0})
		var stopped: bool = is_zero_approx(float(v.player.encumbrance)) and bool(v.player.no_turn)
		Server.world.emit("max_speed", {"kind": "max_speed", "percent": pct, "no_turn": 0, "water": 0})
		_say("3 speed 0: stopped and no turning: %s; back to %d%% (encumbrance %.2f, before %.2f)" % [str(stopped), pct, float(v.player.encumbrance), before])

	# 4 ------------------------------------------------------------------------------------------------------------
	func _appraise() -> void:
		var v := _v()
		var nm = v.get("npcs")
		var best = null
		var best_d := INF
		for oid in nm.by_oid:
			var st = nm.by_oid[oid]
			if String(st.spawn.get("guild", "")).to_lower().contains("merchant"):
				var d: float = (st.pos as Vector3).distance_to(v.player.global_position)
				if d < best_d:
					best_d = d
					best = st
		if best == null:
			_say("4 appraise: no merchant in view")
			return
		var pos: Vector3 = best.pos
		var r: Vector3 = Server.to_region(Server.current_zone_id(), pos)
		Server.command("/jump to %d %d %d %d" % [int(r.x) + 60, int(r.y), int(r.z), int(Server.entry.get("region", 0))])
		await get_tree().create_timer(4.0).timeout
		Server.client.call("Target", int(best.spawn.oid), 0x8000 | 0x6000)
		await get_tree().create_timer(1.0).timeout
		var slot := -1
		for s in PlayerData.inventory:
			if PlayerData.is_backpack(int(s)) and not PlayerData.entry(int(s)).is_empty():
				slot = int(s)
				break
		if slot < 0:
			_say("4 appraise: no backpack item")
			return
		chat.clear()
		PlayerData.appraise(slot, String(best.spawn.name))
		await get_tree().create_timer(2.0).timeout
		_say("4 appraise at %s (%s, %.0f m) on slot %d: %s" % [best.spawn.name, best.spawn.guild, best_d, slot,
				str(chat.filter(func(l): return String(l).contains("offers") or String(l).contains("interested")))])

	# 5 ------------------------------------------------------------------------------------------------------------
	func _teleports() -> void:
		for step in [["/tc", "Tir na Nog"], ["/jump to 422238 485109 2336 200", "Hibernia outdoors"],
				["/jump to 578199 556753 2196 1", "Albion outdoors"], ["/tc", "Tir na Nog again"]]:
			max_gap_ms = 0.0
			var e0 := errors.counts.duplicate()
			Server.command(String(step[0]))
			await get_tree().create_timer(30.0).timeout
			var v := _v()
			_say("5 %s: zone %d, longest frame gap %.0f ms (loading included), errors now %s (before %s)" % [step[1],
					Server.current_zone_id() if v and v.has_method("current_zone_id") else -1, max_gap_ms,
					str(errors.counts), str(e0)])
