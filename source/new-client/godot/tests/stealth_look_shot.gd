extends Node3D
## Debug (owner 2026-10-28: a stealthed realm-mate / yourself is "a dark green ghostly form"): a race figure as drawn,
## the same figure hidden (StealthLook) and the hidden one switched back, in front of a block to show the see-through.
## Args after --: the race (default lur_m). user://stealth_look.png. Runs once.
const StealthLook := preload("res://game/stealth_look.gd")


func _ready() -> void:
	var env := WorldEnvironment.new()
	env.environment = Environment.new()
	env.environment.background_mode = Environment.BG_COLOR
	env.environment.background_color = Color(0.45, 0.5, 0.55)
	env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.environment.ambient_light_color = Color(0.8, 0.8, 0.8)
	add_child(env)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-40, 30, 0)
	sun.shadow_enabled = true
	add_child(sun)
	var floor_mi := MeshInstance3D.new()
	var pm := PlaneMesh.new()
	pm.size = Vector2(8, 6)
	floor_mi.mesh = pm
	add_child(floor_mi)
	var wall := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = Vector3(4.0, 1.2, 0.2)
	wall.mesh = bm
	var wm := StandardMaterial3D.new()
	wm.albedo_color = Color(0.75, 0.35, 0.15)
	wall.material_override = wm
	wall.position = Vector3(0, 0.6, -0.8)
	add_child(wall)
	var args := Array(OS.get_cmdline_user_args())
	var race: String = args[0] if args.size() > 0 else "lur_m"
	var figs: Array = []
	for i in 3:
		var f := Fig3Figure.new()
		f.position = Vector3(-1.0 + 1.0 * i, 0, 0)
		add_child(f)
		f.load_figure(race)
		f.set_armor(Fig3Figure.armor_parts(57, false))
		if f.anim and f.anim.has_animation("idle"):
			f.anim.play("idle")
		figs.append(f)
	StealthLook.set_on(figs[1], true)
	StealthLook.set_on(figs[2], true)
	await get_tree().create_timer(0.6).timeout
	StealthLook.set_on(figs[2], false)                     # hidden, then out again: its own look back
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 1.0, 3.2)
	cam.look_at(Vector3(0, 0.7, 0))
	await get_tree().create_timer(1.0).timeout
	get_viewport().get_texture().get_image().save_png("user://stealth_look.png")
	print("[shot] ", ProjectSettings.globalize_path("user://stealth_look.png"))
	get_tree().quit()
