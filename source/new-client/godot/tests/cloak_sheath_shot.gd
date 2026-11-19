extends Node3D
## Debug (owner 2026-10-19): a Kobold in a Mighty Chain Hauberk (model 295, extension 3 - spiked pauldrons) with a
## hood-up cloak, sword + shield in use and a two-hander sheathed - from behind (left) and the front (right), in its
## idle: the whole figure drawn (the cloak's view_pull), the cloak over the pauldrons, the two-hander on its BACK frame.
## user://cloak_sheath.png. Runs once.
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
	var args := Array(OS.get_cmdline_user_args())             # -- <race> <two-hander model> (default kob_m 6)
	var race: String = args[0] if args.size() > 0 else "kob_m"
	var two := int(args[1]) if args.size() > 1 else 6
	var gear := {10: {"model": 573, "object_type": 0}, 11: {"model": 60, "object_type": ItemDb.SHIELD_TYPE},
			12: {"model": two, "object_type": 0}}
	var item_at := func(s: int) -> Dictionary: return gear.get(s, {})
	for i in 2:
		var f := Fig3Figure.new()
		f.position = Vector3(-0.45 + 0.9 * i, 0, 0)
		f.rotation_degrees.y = 0.0 if i == 0 else 180.0
		add_child(f)
		f.load_figure(race)
		f.hood_up = true
		var parts := Fig3Figure.item_armor(295, 3, false)
		parts.merge(Fig3Figure.armor_parts(57, false), true)
		f.set_armor(parts)
		Player.dress(f, item_at, "standard", false, false)
		if f.anim and f.anim.has_animation("idle"):
			f.anim.play("idle")
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 0.75, 1.9)
	cam.look_at(Vector3(0, 0.6, 0))
	await get_tree().create_timer(1.5).timeout
	get_viewport().get_texture().get_image().save_png("user://cloak_sheath.png")
	print("[shot] ", ProjectSettings.globalize_path("user://cloak_sheath.png"))
	get_tree().quit()
