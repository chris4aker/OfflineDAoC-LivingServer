extends Node
## Check (2026-10-28, GpuJobs): a zone model's faces read from its parsed glTF arrays (ZoneLoader.importer_faces, no
## GPU) against the old way, Mesh.get_faces() on the generated scene (a read-back from the GPU) - every mesh, every
## triangle, in order. Args after --: zone folders (default 012 101 060 056). Prints [faces] lines. Runs once.


func _ready() -> void:
	await get_tree().process_frame
	var args := Array(OS.get_cmdline_user_args())
	var zones: Array = args if not args.is_empty() else ["012", "101", "060", "056"]
	var models := 0
	var meshes := 0
	var bad := 0
	var tris := 0
	var rel := {}
	for z in zones:
		var dir := "res://content/zones/zone%s/models" % z
		var files := DirAccess.get_files_at(dir)
		for f in files:
			if not f.ends_with(".gltf"):
				continue
			var doc := GLTFDocument.new()
			var state := GLTFState.new()
			if doc.append_from_file(dir.path_join(f), state) != OK:
				continue
			var cpu := []
			for gm in state.get_meshes():
				cpu.append(ZoneLoader.importer_faces((gm as GLTFMesh).mesh))
			var mesh_of := {}
			for g in state.get_nodes():
				mesh_of[String((g as GLTFNode).resource_name)] = (g as GLTFNode).mesh
			var root := doc.generate_scene(state)
			for node in root.find_children("*", "MeshInstance3D", true, false):
				var mi := node as MeshInstance3D
				var gi := int(mesh_of.get(String(mi.name), -1))
				var old := mi.mesh.get_faces()
				var new_f: PackedVector3Array = cpu[gi] if gi >= 0 else PackedVector3Array()
				meshes += 1
				tris += old.size() / 3
				var same := old.size() == new_f.size()
				if same:
					for k in old.size():
						if old[k].distance_to(new_f[k]) > 0.01:   # (the GPU copy is position-compressed: ~mm off)
							same = false
							break
				if not same:
					bad += 1
					if same == false and old.size() == new_f.size():
						var kind := _relation(old, new_f)
						rel[kind] = int(rel.get(kind, 0)) + 1
					if bad <= 10:
						print("[faces] DIFF zone%s %s %s: get_faces %d, importer %d (glTF mesh %d)" % [z, f, mi.name,
								old.size(), new_f.size(), gi])
			root.free()
			models += 1
	print("[faces] %d models, %d meshes, %d triangles: %d differ" % [models, meshes, tris, bad])
	print("[faces] relations ", rel)
	get_tree().quit()


## How two face lists of the same size relate: "reversed" (each triangle a, c, b - the other winding), "rotated",
## "reordered" (the same triangles in another order) or "other".
func _relation(a: PackedVector3Array, b: PackedVector3Array) -> String:
	var rev := true
	var rot := true
	for t in a.size() / 3:
		var a0 := a[t * 3]
		var a1 := a[t * 3 + 1]
		var a2 := a[t * 3 + 2]
		var b0 := b[t * 3]
		var b1 := b[t * 3 + 1]
		var b2 := b[t * 3 + 2]
		if not (a0.is_equal_approx(b0) and a1.is_equal_approx(b2) and a2.is_equal_approx(b1)):
			rev = false
		if not ((a0.is_equal_approx(b1) and a1.is_equal_approx(b2) and a2.is_equal_approx(b0)) or
				(a0.is_equal_approx(b2) and a1.is_equal_approx(b0) and a2.is_equal_approx(b1))):
			rot = false
	if rev:
		return "reversed"
	if rot:
		return "rotated"
	var keys := {}
	for t in b.size() / 3:
		keys[str([b[t * 3].snapped(Vector3.ONE * 0.001), b[t * 3 + 1].snapped(Vector3.ONE * 0.001), b[t * 3 + 2].snapped(Vector3.ONE * 0.001)])] = true
	for t in a.size() / 3:
		if not keys.has(str([a[t * 3].snapped(Vector3.ONE * 0.001), a[t * 3 + 1].snapped(Vector3.ONE * 0.001), a[t * 3 + 2].snapped(Vector3.ONE * 0.001)])):
			return "other"
	return "reordered"
