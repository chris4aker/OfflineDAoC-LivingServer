extends Node
## Live test (owner 2026-10-30: the crafting UI, salvage, self-repair): with Claude's test character on the running
## server (server_session.tscn --char=Claudeperf --probe=craft_probe --out=<png prefix>): the tradeskills arrive
## (VariousUpdate 8), the tradeskill window opens on Woodworking (shot), GM /item create gives 4 rowan wooden boards,
## MAKE on the rowan breadboard (recipe 15283, no station needed), then SALVAGE and REPAIR on what was made - the
## server's answers in chat, the timer window's packets counted. Prints [craft] lines, then /quit.

func _ready() -> void:
	var root := get_tree().root
	if root.has_node("CraftProbeDriver"):
		queue_free()
		return
	var d := Driver.new()
	d.name = "CraftProbeDriver"
	root.add_child.call_deferred(d)
	queue_free()


class Driver extends Node:
	var chat: Array = []
	var timers: Array = []

	func _ready() -> void:
		process_mode = Node.PROCESS_MODE_ALWAYS
		PlayerData.message.connect(func(t: String, k: String): chat.append("%s: %s" % [k, t]))
		Server.craft_timer.connect(func(s: int, title: String): timers.append("%d '%s'" % [s, title]))
		_run()

	func _shot(tag: String) -> void:
		await RenderingServer.frame_post_draw
		for a in OS.get_cmdline_user_args():
			if a.begins_with("--out="):
				get_viewport().get_texture().get_image().save_png("%s_%s.png" % [a.substr(6), tag])

	func _lines(words: Array) -> Array:
		return chat.filter(func(l): return words.any(func(w): return String(l).to_lower().contains(w)))

	func _run() -> void:
		await get_tree().create_timer(25.0).timeout
		var v := get_tree().current_scene
		print("[craft] tradeskills: ", Server.craft_skills)
		print("[craft] primary: '%s' title '%s'" % [Server.info.get("craft_skill", ""), Server.info.get("craft_title", "")])
		PlayerData.window_toggle.emit("craft:14")
		await get_tree().create_timer(1.0).timeout
		var w: CraftWindow = v.ui.craft
		print("[craft] window open: %s on %s, %d trades" % [str(w.visible), Server.trade_name(w.trade), w._trades().size()])
		chat.clear()
		Server.command("/item create rowan_wooden_boards 4")
		await get_tree().create_timer(3.0).timeout
		var recipe := {}
		for r in Server.recipes_for(Server.my_realm()).get("14", []):
			if int(r.id) == 15283:
				recipe = r
		w.selected = recipe
		w.refresh()
		await get_tree().create_timer(0.5).timeout
		print("[craft] recipe: %s, have %d of %s" % [recipe.get("name", "?"), CraftWindow.have("rowan wooden boards"), str(recipe.get("ingredients", []))])
		await _shot("window")
		chat.clear()
		w.make_selected()
		await get_tree().create_timer(5.0).timeout
		print("[craft] make: ", _lines(["make", "begin", "skill", "ingredient", "missing", "tools", "craft"]))
		var made := -1
		for s in Server.inv:
			if int(s) >= 40 and int(s) <= 79 and String(ItemDb.item("srv:%d" % int(s)).get("name", "")).to_lower().contains("breadboard"):
				made = int(s)
		print("[craft] breadboard in slot ", made)
		if made >= 0:
			PlayerData.summary.emit(ItemDb.item("srv:%d" % made))
			await get_tree().create_timer(1.0).timeout
			print("[craft] info window buttons shown: ", v.ui._info_buttons.map(func(b): return b.visible))
			await _shot("info")
			chat.clear()
			Server.self_repair(made)
			await get_tree().create_timer(3.0).timeout
			print("[craft] repair: ", chat)
			chat.clear()
			Server.salvage(made)
			await get_tree().create_timer(4.0).timeout
			print("[craft] salvage: ", chat)
		print("[craft] timer packets: ", timers)
		print("[craft] done")
		Server.command("/quit")
		await get_tree().create_timer(8.0).timeout
		get_tree().quit()
