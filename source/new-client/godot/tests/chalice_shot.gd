extends Node3D
## Debug (owner 2026-10-20: the paladin chants' effects - "the chalice and stuff like that"): the Refreshment chant's
## landing effect (alb_palgrpheal_hit, its chalice an untextured, colour-fading mesh) and the blessing halo on a Briton,
## shot 0.6 / 1.2 / 1.8 s in - user://chalice_<n>.png. Runs once.
func _ready() -> void:
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.25, 0.3, 0.38)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.7, 0.7, 0.7)
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-40, 30, 0)
	add_child(sun)
	var figs := []
	for i in 2:
		var f := Fig3Figure.new()
		f.position = Vector3(-0.8 + 1.6 * i, 0, 0)
		f.rotation_degrees.y = 180.0
		add_child(f)
		f.load_figure("bri_m")
		figs.append(f)
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 1.3, 4.0)
	cam.look_at(Vector3(0, 1.0, 0))
	await get_tree().create_timer(0.5).timeout
	var paths := ["res://content/monsters/effects/alb_palgrpheal_hit/model.gltf", "res://content/monsters/effects/blesshalo/model.gltf"]
	for i in 2:
		var fx := NifScene.copy_of(paths[i])
		print("[chalice] ", paths[i].get_base_dir().get_file(), " -> ", fx != null)
		if fx:
			(figs[i] as Node3D).add_child(fx)
	for n in 3:
		await get_tree().create_timer(0.6).timeout
		get_viewport().get_texture().get_image().save_png("user://chalice_%d.png" % n)
	print("[shot] ", ProjectSettings.globalize_path("user://"))
	get_tree().quit()
