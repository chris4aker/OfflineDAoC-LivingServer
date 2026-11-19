extends Node
## Offline test (freeze hunt 2026-10-30, no server): a throwaway character on the throwaway "pregameshot" account
## (never the owner's characters) goes through character select and PLAY, or with --direct straight into the zone
## viewer, so the "Parameter 'material' is null" errors after the zone build can be compared. Removes the character
## afterwards.
##   Godot --path godot res://tests/offline_play_check.tscn [-- --direct]

const NAME := "Brennan"


func _ready() -> void:
	var d := Driver.new()
	d.name = "OfflinePlayDriver"
	get_tree().root.add_child.call_deferred(d)
	queue_free()


class Driver extends Node:
	var t0 := 0

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		t0 = Time.get_ticks_msec()
		Accounts.account = "pregameshot"
		CharacterStore.remove(Accounts.account, NAME)
		_run()

	func _t() -> float:
		return (Time.get_ticks_msec() - t0) / 1000.0

	func _run() -> void:
		var direct := "--direct" in OS.get_cmdline_user_args()
		get_tree().change_scene_to_file("res://game/pregame.tscn")
		for i in 60:
			await get_tree().process_frame
		var pg := get_tree().current_scene
		pg.call("_pick_realm", "hib")
		for i in 30:
			await get_tree().process_frame
		pg.call("_start_create")
		pg.get("draft").name = NAME
		pg.call("_show", "create")
		for i in 10:
			await get_tree().process_frame
		(pg.get("_name_edit") as LineEdit).text = NAME
		pg.call("_create_continue")
		for i in 20:
			await get_tree().process_frame
		pg.call("_customize_continue")
		for i in 60:
			await get_tree().process_frame
		var chars: Array = pg.get("chars")
		var i := -1
		for k in chars.size():
			if String(chars[k].name) == NAME:
				i = k
		if i < 0:
			print("[offline] character not made")
			get_tree().quit()
			return
		print("[offline] %.1f s at select with %s, %s" % [_t(), NAME, "DIRECT" if direct else "PLAY"])
		if direct:
			CharacterStore.selected = chars[i]
			get_tree().change_scene_to_file("res://game/zone_viewer.tscn")
		else:
			pg.set("sel", i)
			pg.call("_play")
		await get_tree().create_timer(30.0).timeout
		var s := get_tree().current_scene
		print("[offline] %.1f s after: scene %s" % [_t(), s.scene_file_path if s else "none"])
		CharacterStore.remove(Accounts.account, NAME)
		print("[offline] done")
		get_tree().quit()
