extends Node3D
## Debug (owner 2026-10-19: Albion hats and a minstrel's helm not showing): a row of race figures each wearing a helm -
## Briton female + male in the Albion leather helm (62), a Half Ogre in the cloth helm (823), Olred's Kobold chain helm
## (833), an Avalonian in the wizard hat (1278) and the Albion cloth cap (822), a Celt in the Hibernian cap (1279), a
## Briton in the full plate helm (93) - user://helm_row.png, with which helms.json entry each found. Runs once.
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
	var row := [["bri_f", 62], ["bri_m", 62], ["hal_m", 823], ["kob_m", 833], ["ava_m", 1278], ["ava_m", 822],
			["cel_m", 1279], ["bri_m", 93]]
	for i in row.size():
		var f := Fig3Figure.new()
		f.position = Vector3((i - (row.size() - 1) / 2.0) * 0.75, 0, 0)
		f.rotation_degrees.y = 180.0
		add_child(f)
		f.load_figure(String(row[i][0]))
		f.set_helm(int(row[i][1]))
		print("[helm] %s %d -> %s" % [row[i][0], row[i][1], "drawn" if f._helm != null or f._full_helm > 0 else "NONE"])
	var cam := Camera3D.new()
	add_child(cam)
	cam.make_current()
	cam.position = Vector3(0, 1.5, 4.2)
	cam.look_at(Vector3(0, 1.3, 0))
	await get_tree().create_timer(1.5).timeout
	get_viewport().get_texture().get_image().save_png("user://helm_row.png")
	print("[shot] ", ProjectSettings.globalize_path("user://helm_row.png"))
	get_tree().quit()
