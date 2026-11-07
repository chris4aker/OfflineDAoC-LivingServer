extends Node
## Debug, headless (owner 2026-10-19: a troll Bonedancer companion's robe drawn on the vest body with trousers under it):
## figures dressed in a robe (armor.json body mesh 7, game.dll 0x41b5c1) and other chest pieces - which part meshes show.
func _ready() -> void:
	# [race, chest model, chest extension, boots / gloves extension]
	var cases := [["tro_m", 66, 0, 0], ["nor_f", 66, 0, 0], ["hig_m", 58, 0, 0], ["nor_f", 245, 0, 0], ["tro_m", 0, 0, 0],
			["nor_m", 245, 2, 4], ["hig_m", 245, 5, 0]]
	for c in cases:
		var f := Fig3Figure.new()
		add_child(f)
		if not f.load_figure(String(c[0])):
			print("[robe] no figure ", c[0])
			continue
		var female := String(c[0]).ends_with("_f")
		var parts := {}
		for m in [[int(c[1]), int(c[2])], [249, int(c[3])], [248, int(c[3])]]:   # the chest, Norse cloth boots and gloves
			if int(m[0]) > 0:
				parts.merge(Fig3Figure.item_armor(int(m[0]), int(m[1]), female), true)
		f.set_armor(parts)
		var shown := []
		for part in f._meshes:
			var p := String(part)
			if p.begins_with("body") or p.begins_with("legs") or p.begins_with("arms") or p.begins_with("boots") 					or p.begins_with("gloves") or p.begins_with("lbody"):
				for mi in f._meshes[part]:
					if (mi as MeshInstance3D).visible:
						shown.append(p)
						break
		shown.sort()
		print("[robe] %s chest %d ext %d/%d -> %s" % [c[0], c[1], c[2], c[3], ", ".join(shown)])
		f.queue_free()
	await get_tree().process_frame
	print("[robe] done")
	get_tree().quit()
