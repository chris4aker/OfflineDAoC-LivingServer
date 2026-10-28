extends Node
## Debug, headless (owner 2026-10-20: weapon glows trailed far behind the sword): the abomination's two-handed sword
## (model 6) with its glow (effect 30) - the glow's particle systems, and that they live in the weapon's space.
func _ready() -> void:
	var w := WeaponModels.make(6)
	add_child(w)
	var fx := WeaponModels.add_effect(w, 30)
	var ps := fx.find_children("*", "GPUParticles3D", true, false) if fx else []
	print("[wfx] effect %s, %d particle systems, local: %s" % [fx != null, ps.size(), ps.map(func(p): return (p as GPUParticles3D).local_coords)])
	await get_tree().process_frame
	get_tree().quit()
