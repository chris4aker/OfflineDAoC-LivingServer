extends SceneTree
## Pipeline step (owner 2026-10-10: the loading screen took long): converted glTF scenes -> Godot's own binary
## scene (.scn next to the .gltf). A race figure's glTF (22 MB of text, every clip) took ~2.5 s to parse the first
## time a zone needed that race; the .scn loads in a fraction of that. The glTF stays the source; a .scn older than
## its glTF is rebuilt. Fig3Figure / NpcManager load the .scn when it is there.
##   Godot --headless --path godot -s res://tools/pack_scenes.gd -- res://content/characters/races [res://content/monsters ...]


var keep_textures := false
var force := false                             # --force: repack every scene (a packing rule changed)


func _pack(gltf: String) -> bool:
	var scn := gltf.get_basename() + ".scn"
	if not force and FileAccess.file_exists(scn) and FileAccess.get_modified_time(scn) >= FileAccess.get_modified_time(gltf):
		return false
	var doc := GLTFDocument.new()
	var state := GLTFState.new()
	state.base_path = ProjectSettings.globalize_path(gltf.get_base_dir())
	if doc.append_from_file(gltf, state) != OK:
		printerr("cannot read ", gltf)
		return false
	var root := doc.generate_scene(state)
	for c in root.find_children("*", "", true, false):
		c.owner = root
	# the materials' own textures go: the glTF points at the 4x skins, which a figure / model draws through its own
	# materials anyway (Fig3Figure, NifMaterials) - kept, they were decoded at every load and stored raw (141 MB)
	if not keep_textures:
		for mi in root.find_children("*", "MeshInstance3D", true, false):
			var mesh := (mi as MeshInstance3D).mesh
			for i in (mesh.get_surface_count() if mesh else 0):
				var m := mesh.surface_get_material(i)
				if m is BaseMaterial3D:
					(m as BaseMaterial3D).albedo_texture = null
	# a race figure's placeholder materials go altogether (2026-10-30): Fig3Figure gives every part it shows its own
	# material, and Fig3Figure.pack_figure loads this .scn on the NPC-view worker - loading a mesh sets its surface
	# materials, which made their render-server materials on the worker while a frame was drawn ("Parameter 'material'
	# is null" after a live login; the freeze GpuJobs exists to prevent)
	if gltf.contains("/characters/races/"):
		for mi in root.find_children("*", "MeshInstance3D", true, false):
			var mesh := (mi as MeshInstance3D).mesh
			for i in (mesh.get_surface_count() if mesh else 0):
				mesh.surface_set_material(i, null)
	# Godot's animation compression (bit-packed, page-based tracks): a race's 189 clips held ~736,000 full keys
	for ap in root.find_children("*", "AnimationPlayer", true, false):
		for lib_name in (ap as AnimationPlayer).get_animation_library_list():
			var lib := (ap as AnimationPlayer).get_animation_library(lib_name)
			for an in lib.get_animation_list():
				# at 60 frames a second, not the default 120: at 120 the root's position track lost its first
				# quarter second - Bip01 held standing height while the legs crouched, and the feet lifted off the
				# ground at every cast start (owner 2026-10-11, a troll's Minor Dusk Dart; tests/compress_probe.gd:
				# 30 and 60 give the clips' 15-a-second keys back exactly)
				lib.get_animation(an).compress(8192, 60, 0.0)
	var ps := PackedScene.new()
	var ok := ps.pack(root) == OK and ResourceSaver.save(ps, scn, ResourceSaver.FLAG_COMPRESS) == OK
	root.free()
	return ok


func _walk(dir: String, out: Array) -> void:
	for f in DirAccess.get_files_at(dir):
		if f.ends_with(".gltf"):
			out.append(dir.path_join(f))
	for d in DirAccess.get_directories_at(dir):
		_walk(dir.path_join(d), out)


func _initialize() -> void:
	var t0 := Time.get_ticks_msec()
	var files := []
	for d in OS.get_cmdline_user_args():
		if d == "--force":
			force = true
			continue
		_walk(d, files)
	var n := 0
	for f in files:
		if _pack(f):
			n += 1
	print("[scn] packed %d of %d scenes in %.1f s" % [n, files.size(), (Time.get_ticks_msec() - t0) / 1000.0])
	quit()
