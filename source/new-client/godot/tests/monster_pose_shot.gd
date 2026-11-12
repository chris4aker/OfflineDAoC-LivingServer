extends Node3D
## Debug (owner 2026-10-20: "this crafted keeper mob is wrong" - monster 847, Clock Work Human): a monster model loaded as
## NpcManager loads it, in its rest pose, idle and walk side by side - user://monster_pose.png. Args after --: the model
## folder (default cwhuman__clockwork__clkwrk_h_dark), then a size factor for big models (spacing and camera). Runs once.
func _ready() -> void:
	var args := Array(OS.get_cmdline_user_args())
	var folder: String = args[0] if args.size() > 0 else "cwhuman__clockwork__clkwrk_h_dark"
	var k: float = float(args[1]) if args.size() > 1 else 1.0
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
	var ps := NpcManager._pack_model("res://content/monsters/%s/model.gltf" % folder)
	var clips := ["", "idle", "walk"]
	for i in 3:
		var m := ps.instantiate() as Node3D
		m.position = Vector3((-1.6 + 1.6 * i) * k, 0, 0)
		add_child(m)
		var ap := m.find_children("*", "AnimationPlayer", true, false)
		if ap.size() > 0:
			var p := ap[0] as AnimationPlayer
			print("[pose] clips: ", p.get_animation_list().slice(0, 12))
			if clips[i] != "" and p.has_animation(clips[i]):
				p.play(clips[i])
				p.seek(0.5, true)
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 1.4, 6.0) * k
	cam.look_at(Vector3(0, 1.1 * k, 0))
	await get_tree().create_timer(1.0).timeout
	get_viewport().get_texture().get_image().save_png("user://monster_pose.png")
	print("[shot] ", ProjectSettings.globalize_path("user://monster_pose.png"))
	get_tree().quit()
