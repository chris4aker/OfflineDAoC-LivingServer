extends Node3D
## Offline: the dyed staff (model 19, colour 52 - the test character's) drawn a few frames; "material is null" shows
## when the dyed copy's material is bad (freeze hunt 2026-10-30).
func _ready() -> void:
	var cam := Camera3D.new()
	cam.position = Vector3(0, 0, 3)
	add_child(cam)
	var w := WeaponModels.make(19)
	add_child(w)
	for mi in w.find_children("*", "MeshInstance3D", true, false):
		for i in (mi as MeshInstance3D).mesh.get_surface_count():
			var m := (mi as MeshInstance3D).get_active_material(i)
			print("[dye] surface %d: %s shader %s" % [i, m.get_class() if m else "null", str((m as ShaderMaterial).shader) if m is ShaderMaterial else "-"])
	WeaponModels.dye(w, 52)
	await get_tree().process_frame
	# as the game: dressed again at once - the first copy freed, a second made and dyed in the same frame
	w.queue_free()
	var w2 := WeaponModels.make(19)
	add_child(w2)
	WeaponModels.dye(w2, 52)
	var w3 := WeaponModels.make(19)
	add_child(w3)
	WeaponModels.dye(w3, 52)
	w2.queue_free()
	for f in 10:
		await get_tree().process_frame
	print("[dye] done")
	get_tree().quit()
