extends Node
## Test (owner 2026-10-29: "at loc 38907 33958 1792 dir=4 the castle at caer ulfwych has no collision at all i can
## just walk straight thru its walls"): in Campacorentin Forest (zone 8), rays at chest height in 16 directions from
## the owner's /loc, and from 30 m out toward it - what wall each meets (the zone's collision bodies). Prints [wall].
##   --skip-login --zone=8 --no-npcs --probe=wall_ray_probe

const LOC := Vector2(38907.0, 33958.0)


func _ready() -> void:
	await get_tree().create_timer(12.0).timeout
	var v := get_tree().current_scene
	var space := (v as Node3D).get_world_3d().direct_space_state
	var c := Vector3(LOC.x * 0.025, 0.0, LOC.y * 0.025)
	c.y = float(v.zone.height_at(c.x, c.z)) + 1.2
	var hits := 0
	for i in 16:
		var a := TAU * i / 16.0
		var d := Vector3(sin(a), 0.0, -cos(a))
		for from in [c, c + d * 30.0]:
			var to: Vector3 = c + d * 30.0 if from == c else c
			var q := PhysicsRayQueryParameters3D.create(from, to)
			var hit := space.intersect_ray(q)
			if not hit.is_empty():
				hits += 1
			print("[wall] dir %3d %s: %s" % [int(rad_to_deg(a)), "out" if from == c else "in ",
					("hit %s at %.1f m" % [str(hit.collider.name) if hit.collider else "?", from.distance_to(hit.position)]) if not hit.is_empty() else "nothing"])
	print("[wall] %d of 32 rays met something" % hits)
	get_tree().quit()
