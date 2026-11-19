extends Node
## Debug, headless (owner 2026-10-19: a chain hauberk's shoulders through the cloak at idle): a race figure posed in its
## idle at a few times, the skinned body meshes and the hood-down cloak written out (user://cloak_pose_<race>.json) for
## tools to compare from behind. Args after --: races (default kob_m nor_m).
func _ready() -> void:
	var races := Array(OS.get_cmdline_user_args())
	if races.is_empty():
		races = ["kob_m", "nor_m"]
	for race in races:
		var f := Fig3Figure.new()
		add_child(f)
		if not f.load_figure(String(race)):
			continue
		var out := {}
		for t in [-1.0, 0.0, 1.0, 2.0, 3.0]:
			if t < 0.0:                                    # -1: the bind pose (every bone at rest, no clip)
				if f.anim:
					f.anim.stop()
				for sk in f.find_children("*", "Skeleton3D", true, false):
					(sk as Skeleton3D).reset_bone_poses()
			elif f.anim and f.anim.has_animation("idle"):
				f.anim.play("idle")
				f.anim.seek(t, true)
			var frame := {}
			for part in f._meshes:
				var p := String(part)
				if not (p.begins_with("cloak_") or p.begins_with("body")):
					continue
				for mi in f._meshes[part]:
					frame[p] = _skinned(mi as MeshInstance3D)
			out[str(t)] = frame
		var fa := FileAccess.open("user://cloak_pose_%s.json" % race, FileAccess.WRITE)
		fa.store_string(JSON.stringify(out))
		fa.close()
		print("[pose] ", race, " -> ", ProjectSettings.globalize_path("user://cloak_pose_%s.json" % race))
		f.queue_free()
	await get_tree().process_frame
	get_tree().quit()


## [positions (flat xyz), triangle indices] in the skeleton's space, skinned on the CPU.
func _skinned(mi: MeshInstance3D) -> Array:
	var skel := mi.get_node_or_null(mi.skeleton) as Skeleton3D
	var skin := mi.skin
	var pos := PackedFloat32Array()
	var tris := PackedInt32Array()
	if mi.mesh == null:
		return [pos, tris]
	var mats := []
	if skel and skin:
		for i in skin.get_bind_count():
			var b := skin.get_bind_bone(i)
			if b < 0:
				b = skel.find_bone(skin.get_bind_name(i))
			mats.append(skel.get_bone_global_pose(b) * skin.get_bind_pose(i))
	var base := 0
	for s in mi.mesh.get_surface_count():
		var arr := mi.mesh.surface_get_arrays(s)
		var v: PackedVector3Array = arr[Mesh.ARRAY_VERTEX]
		var bones = arr[Mesh.ARRAY_BONES]
		var w = arr[Mesh.ARRAY_WEIGHTS]
		var per := 4 if bones != null and bones.size() == v.size() * 4 else 8
		for i in v.size():
			var p := v[i]
			if not mats.is_empty() and bones != null and bones.size() >= (i + 1) * per:
				var acc := Vector3.ZERO
				for k in per:
					var wt := float(w[i * per + k])
					if wt > 0.0:
						acc += (mats[int(bones[i * per + k])] as Transform3D) * p * wt
				p = acc
			pos.append(p.x); pos.append(p.y); pos.append(p.z)
		var idx = arr[Mesh.ARRAY_INDEX]
		if idx != null:
			for j in idx:
				tris.append(base + int(j))
		base += v.size()
	return [pos, tris]
