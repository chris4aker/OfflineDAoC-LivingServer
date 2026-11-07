extends Node
## Debug (owner 2026-10-20: Snowdonia Fortress's metal gates never opened in the new client): opens the border keep's
## gates (doors 12000101 / 12000102: zone 12, fixture 1) as the server's DoorState does and prints how far each leaf
## moved; shots of the first gate shut and open in user://gate_shut.png / gate_open.png.
##   --skip-login --zone=12 --no-npcs --time=12 --probe=gate_slide_probe


func _ready() -> void:
	await get_tree().create_timer(8.0).timeout
	var v := get_tree().current_scene
	var z: ZoneLoader = v.zone
	var leaves: Array = []
	for id in [12000101, 12000102]:
		for d in z.door_by_id.get(id, []):
			leaves.append([id, d, (d as ZoneDoor).global_position])
	print("[gate] %d leaves" % leaves.size())
	var first: ZoneDoor = null
	for l in leaves:
		if first == null and int(l[0]) == 12000101:
			first = l[1]
	var cam := Camera3D.new()
	z.add_child(cam)
	if first:
		var c := first.global_position
		cam.global_position = c + Vector3(0, 4, 0) + (first.global_transform.basis.z.normalized() * 22.0)
		cam.look_at(c + Vector3(0, 3, 0))
	cam.make_current()
	await get_tree().create_timer(1.0).timeout
	get_viewport().get_texture().get_image().save_png("user://gate_shut.png")
	for l in leaves:
		(l[1] as ZoneDoor).set_server_state(true)
	await get_tree().create_timer(4.0).timeout
	for l in leaves:
		var d: ZoneDoor = l[1]
		print("[gate] door %d moved %s m (%s)" % [int(l[0]), str((d.global_position - (l[2] as Vector3)).snapped(Vector3.ONE * 0.01)),
				"open" if d.is_open else "shut"])
	get_viewport().get_texture().get_image().save_png("user://gate_open.png")
	print("[shot] ", ProjectSettings.globalize_path("user://gate_open.png"))
	get_tree().quit()
