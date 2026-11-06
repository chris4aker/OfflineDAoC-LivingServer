extends Node3D
## Debug (owner 2026-10-20: the abomination's glowing eyes sat on its armour): the Glowing Demon Knight (monster 653)
## and the Abomination pet body, loaded as NpcManager loads monsters (its glow map on the model's second UV set),
## front views, dim light so the glow shows - user://glow_uv.png. Runs once.
func _ready() -> void:
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.12, 0.14, 0.18)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.35, 0.35, 0.35)
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-30, 20, 0)
	sun.light_energy = 0.5
	add_child(sun)
	var paths := ["res://content/monsters/demon_knight__demon_knight__fe89fa/model.gltf",
			"res://content/monsters/ab_pet01__ab_body001__5e588a/model.gltf"]
	for i in paths.size():
		var ps := NpcManager._pack_model(paths[i])
		if ps == null:
			print("[glow] cannot load ", paths[i])
			continue
		var m := ps.instantiate() as Node3D
		m.position = Vector3(-1.1 + 2.2 * i, 0, 0)
		add_child(m)
		for mi in m.find_children("*", "MeshInstance3D", true, false):
			var mat := (mi as MeshInstance3D).get_active_material(0)
			if mat is BaseMaterial3D:
				print("[glow] %s emission %s on uv2 %s" % [paths[i].get_base_dir().get_file(), (mat as BaseMaterial3D).emission_enabled, (mat as BaseMaterial3D).emission_on_uv2])
		var ap := m.find_children("*", "AnimationPlayer", true, false)
		if ap.size() > 0 and (ap[0] as AnimationPlayer).has_animation("idle"):
			(ap[0] as AnimationPlayer).play("idle")
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 1.6, 4.5)
	cam.look_at(Vector3(0, 1.3, 0))
	await get_tree().create_timer(1.5).timeout
	get_viewport().get_texture().get_image().save_png("user://glow_uv.png")
	print("[shot] ", ProjectSettings.globalize_path("user://glow_uv.png"))
	get_tree().quit()
