extends Node
## Debug, headless (owner 2026-10-19: the old client keeps your two-hander on your back while you use one hand and a
## shield): a race figure carrying a sword, a shield, a two-hander and a bow, dressed in each weapon set - where each
## weapon hangs (Player.dress).
func _ready() -> void:
	var gear := {10: {"model": 573, "object_type": 0}, 11: {"model": 60, "object_type": ItemDb.SHIELD_TYPE},
			12: {"model": 577, "object_type": 0}, 13: {"model": 132, "object_type": 0}}
	var item_at := func(s: int) -> Dictionary: return gear.get(s, {})
	for active in ["standard", "twohand", "ranged"]:
		for combat in [false, true]:
			var f := Fig3Figure.new()
			add_child(f)
			f.load_figure("kob_m")
			var made: Array = Player.dress(f, item_at, active, combat, false)
			var where := []
			for at in made:
				var w := (at as Node).get_child(0)
				where.append("%s@%s" % [String(w.name).trim_prefix("Weapon_"), (at as BoneAttachment3D).bone_name])
			print("[sheath] %s %s: %s" % [active, "combat" if combat else "peace", ", ".join(where)])
			f.queue_free()
	await get_tree().process_frame
	get_tree().quit()
