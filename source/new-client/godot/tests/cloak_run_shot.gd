extends Node3D
## Debug (owner 2026-10-20: "feet clip thru the cloak when running on an avalonian male"): a race figure in a cloak, from
## behind, frozen at four points of its run - the legs should stay under the cloak's panels.
## Args after --: the race (default ava_m). user://cloak_run.png. Runs once.
func _ready() -> void:
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.3, 0.42, 0.55)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.8, 0.8, 0.8)
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-40, 30, 0)
	add_child(sun)
	var args := Array(OS.get_cmdline_user_args())
	var race: String = args[0] if args.size() > 0 else "ava_m"
	for i in 4:
		var f := Fig3Figure.new()
		f.position = Vector3(-1.35 + 0.9 * i, 0, 0)
		f.rotation_degrees.y = 180.0
		add_child(f)
		f.load_figure(race)
		f.set_armor(Fig3Figure.armor_parts(57, false))
		if f.anim and f.anim.has_animation("run"):
			f.anim.play("run")
			f.anim.seek(f.anim.current_animation_length * i / 4.0, true)
			f.anim.speed_scale = 0.0
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 0.8, 2.6)
	cam.look_at(Vector3(0, 0.7, 0))
	await get_tree().create_timer(1.5).timeout
	get_viewport().get_texture().get_image().save_png("user://cloak_run.png")
	print("[shot] ", ProjectSettings.globalize_path("user://cloak_run.png"))
	get_tree().quit()
