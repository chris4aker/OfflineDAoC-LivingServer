extends Node
## Debug, headless (owner 2026-10-19: a ghostly dwarf's hair vanished from some angles): the ghost passes built from the
## figure's cutout (hair) and solid shaders - their cull modes, and that they compile (a shader material on a mesh).
func _ready() -> void:
	for cutout in [true, false]:
		var src := Fig3Figure._material(cutout).shader
		var pair: Array = Npc._ghost_shader(src)
		for i in 2:
			var code := String((pair[i] as Shader).code)
			var m := RegEx.create_from_string("render_mode [^;]*;").search(code)
			print("[ghost] %s pass %d: %s" % ["hair (cutout)" if cutout else "solid", i, m.get_string() if m else "?"])
			var mi := MeshInstance3D.new()
			mi.mesh = BoxMesh.new()
			var mat := ShaderMaterial.new()
			mat.shader = pair[i]
			mi.material_override = mat
			add_child(mi)
	await get_tree().process_frame
	await get_tree().process_frame
	print("[ghost] done")
	get_tree().quit()
