extends Node
## Live test (2026-10-29 batch): the launcher's way in. Run right after a session ended without /quit, so the server
## still holds the character link-dead (60 s): the login screen should keep trying every 10 s until it is let in;
## then on character select PLAY is clicked twice in one frame (a double-click) - one entry, no disconnect.
##   Godot --path godot res://tests/login_play_check.tscn -- --server=127.0.0.1 --account=... --password=...

func _ready() -> void:
	var d := Driver.new()
	d.name = "LoginPlayDriver"
	get_tree().root.add_child.call_deferred(d)
	get_tree().change_scene_to_file.call_deferred("res://game/login_screen.tscn")


class Driver extends Node:
	var t0 := 0
	var disconnects := 0

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		t0 = Time.get_ticks_msec()
		Server.login_failed.connect(func(why: String): print("[login] %.1f s refused: %s" % [_t(), why]))
		Server.logged_in.connect(func(_a): print("[login] %.1f s logged in" % _t()))
		Server.disconnected.connect(func(why: String):
			disconnects += 1
			print("[login] %.1f s disconnected: %s" % [_t(), why]))
		_run()

	func _t() -> float:
		return (Time.get_ticks_msec() - t0) / 1000.0

	func _run() -> void:
		var waited := 0.0
		while waited < 130.0:
			await get_tree().create_timer(1.0).timeout
			waited += 1.0
			var s := get_tree().current_scene
			if s and s.has_method("_play") and Server.logged:
				break
		var pg := get_tree().current_scene
		if pg == null or not pg.has_method("_play"):
			print("[login] never reached character select")
			get_tree().quit()
			return
		print("[login] %.1f s at character select" % _t())
		pg.call("_pick_realm", "hib")
		await get_tree().create_timer(3.0).timeout
		var chars: Array = pg.get("chars")
		var i := -1
		for k in chars.size():
			if String(chars[k].name).to_lower() == "claudeperf":
				i = k
		if i < 0:
			print("[login] Claudeperf not listed: ", chars.map(func(c): return c.name))
			get_tree().quit()
			return
		pg.set("sel", i)
		pg.call("_play")
		pg.call("_play")                                  # the double-click
		print("[login] %.1f s PLAY clicked twice" % _t())
		await get_tree().create_timer(40.0).timeout
		var s := get_tree().current_scene
		print("[login] %.1f s after PLAY: scene %s, playing %s, disconnects %d" % [_t(), s.scene_file_path if s else "none",
				str(Server.playing), disconnects])
		Server.command("/quit")
		await get_tree().create_timer(8.0).timeout
		get_tree().quit()
