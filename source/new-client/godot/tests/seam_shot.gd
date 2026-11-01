extends Node
## Debug (owner 2026-10-29: "there are still white lines for every zone boundry where you stitched every zone
## together"): the player stood 40 m from the border with the first neighbour, the neighbour streamed in, then shots
## of the seam - looking along it and across it, from 2 m and 12 m up. --out=<abs png prefix>.
##   --skip-login --zone=0 --no-npcs --probe=seam_shot --out=...

func _ready() -> void:
	await get_tree().create_timer(12.0).timeout
	var v := get_tree().current_scene
	var region: Node = v.get("region")
	var ns: Array = region.call("neighbours")
	var size := float(v.zone.info.size_m)
	var c := Vector3(size / 2.0, 0.0, size / 2.0)
	var nb := int(ns[0])
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--neighbour="):
			nb = int(a.substr(12))
	var nc: Vector3 = region.call("offset_of", nb) + c
	var dir := Vector3(nc.x - c.x, 0.0, nc.z - c.z)
	dir = Vector3(signf(dir.x), 0.0, 0.0) if absf(dir.x) > absf(dir.z) else Vector3(0.0, 0.0, signf(dir.z))
	var seam := c + dir * (size / 2.0)                  # a point on the border line
	var p := seam - dir * 40.0
	p.y = float(v.zone.height_at(p.x, p.z)) + 1.0
	v.player.global_position = p
	await get_tree().create_timer(25.0).timeout
	print("[seam] zones loaded ", (region.get("zones") as Dictionary).keys(), " seam at ", seam, " toward ", dir)
	var out := ""
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--out="):
			out = a.substr(6)
	var cam := Camera3D.new()
	cam.far = 1500.0
	cam.fov = 60.0
	v.add_child(cam)
	cam.make_current()
	v.player.visible = false
	var along := Vector3(dir.z, 0.0, dir.x)              # the border's own direction
	var shots := [[seam - dir * 15.0, seam + dir * 10.0, 2.0], [seam - dir * 25.0 + along * 5.0, seam + along * 40.0, 12.0],
			[seam + along * 2.0 - dir * 2.0, seam + along * 60.0, 2.5], [seam - dir * 6.0, seam + dir * 3.0, 6.0]]
	var k := 0
	for s in shots:
		var eye: Vector3 = s[0]
		var at: Vector3 = s[1]
		eye.y = float(region.height_at(eye.x, eye.z)) + float(s[2])
		at.y = float(region.height_at(at.x, at.z))
		cam.global_position = eye
		cam.look_at(at)
		await get_tree().create_timer(2.0).timeout
		await RenderingServer.frame_post_draw
		get_viewport().get_texture().get_image().save_png("%s_%d.png" % [out, k])
		k += 1
	print("[seam] done")
	get_tree().quit()
