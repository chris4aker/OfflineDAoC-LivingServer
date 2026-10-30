extends Control
## Offline (no server): the merchant window with a server-style page of 28 arrows (packs of 20) - the QTY row, KEEP,
## the scrolling list and its thumb, and an arrow's info lines (ItemDb.summary). Screenshot with --out=<png>.

func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	var bg := ColorRect.new()
	bg.color = Color(0.2, 0.22, 0.25)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var w := MerchantWindow.new()
	w.setup("merchant_test", "merchant", Rect2(), Vector2(40, 20))
	add_child(w)
	w.build()
	var names := ["rough blunt arrows", "rough flight blunt arrows", "rough clout bodkin arrows", "footed broadhead arrows"]
	var ids := []
	for i in 28:
		var id := "srvm:0:%d" % i
		ItemDb.add_runtime(id, {"id": id, "name": "%s %d" % [names[i % 4], i], "level": 1 + i / 4, "pack": 20, "dps_af": 0,
				"spd_abs": [0x10, 0x0d, 0x32, 0x3f][i % 4], "type_damage": [1, 1, 3, 2][i % 4], "object_type": 43,
				"weight": 20, "price": 19 + i, "model": 3437, "icon": 0, "server": true})
		ids.append(id)
	w.open_server("Sigrun", {0: ids}, 0)
	w.select_item(ids[2])
	w.set_qty(10)
	w.scroll(5)
	for l in ItemDb.summary(ItemDb.item(ids[3])):
		print("[merchant] ", l)
	for i in 10:
		await get_tree().process_frame
	await RenderingServer.frame_post_draw
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--out="):
			get_viewport().get_texture().get_image().save_png(a.substr(6))
	print("[merchant] rows shown from ", w._offset, ", qty ", w.qty)
	get_tree().quit()
