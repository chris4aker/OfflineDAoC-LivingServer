extends Node
## Debug, headless (owner 2026-10-19: a sheathed two-hander hung out across the cloak): a race figure in its idle with a
## hood-up cloak, a sword and shield in hand and a two-hander sheathed - the skinned body / cloak and the weapons'
## meshes written out (user://sheath_pose_<race>.json) for a back-view raster. Args after --: race (default nor_m).
func _ready() -> void:
	var args := Array(OS.get_cmdline_user_args())
	var race: String = args[0] if args.size() > 0 else "nor_m"
	var gear := {10: {"model": 573, "object_type": 0}, 11: {"model": 60, "object_type": ItemDb.SHIELD_TYPE},
			12: {"model": 6, "object_type": 0}}
	var item_at := func(s: int) -> Dictionary: return gear.get(s, {})
	var f := Fig3Figure.new()
	add_child(f)
	f.load_figure(race)
	var dump: Object = load("res://tests/cloak_pose_dump.gd").new()
	if f.anim and f.anim.has_animation("idle"):
		f.anim.play("idle")
		f.anim.seek(1.0, true)
	var made: Array = Player.dress(f, item_at, "standard", false, false)
	var skel: Skeleton3D = f.find_children("*", "Skeleton3D", true, false)[0]
	var frame := {}
	for part in f._meshes:
		var p := String(part)
		if p == "cloak_2" or p == "body":
			for mi in f._meshes[part]:
				frame[p] = dump._skinned(mi as MeshInstance3D)
	for at in made:
		var bone := skel.find_bone((at as BoneAttachment3D).bone_name)
		var bp := skel.get_bone_global_pose(bone)
		for mi in (at as Node).find_children("*", "MeshInstance3D", true, false):
			var rel := (at as Node3D).global_transform.affine_inverse() * (mi as MeshInstance3D).global_transform
			var t := bp * rel
			var pos := PackedFloat32Array()
			var tris := PackedInt32Array()
			var base := 0
			for s in (mi as MeshInstance3D).mesh.get_surface_count():
				var arr := (mi as MeshInstance3D).mesh.surface_get_arrays(s)
				for v in (arr[Mesh.ARRAY_VERTEX] as PackedVector3Array):
					var q := t * v
					pos.append(q.x); pos.append(q.y); pos.append(q.z)
				if arr[Mesh.ARRAY_INDEX] != null:
					for j in arr[Mesh.ARRAY_INDEX]:
						tris.append(base + int(j))
				base += (arr[Mesh.ARRAY_VERTEX] as PackedVector3Array).size()
			frame["weapon_%s" % (at as BoneAttachment3D).bone_name.replace(" ", "_")] = [pos, tris]
	var fa := FileAccess.open("user://sheath_pose_%s.json" % race, FileAccess.WRITE)
	fa.store_string(JSON.stringify({"1.0": frame}))
	fa.close()
	print("[sheath_pose] ", ProjectSettings.globalize_path("user://sheath_pose_%s.json" % race), " ", frame.keys())
	await get_tree().process_frame
	get_tree().quit()
