extends Node
## A stealthed figure you can still see - yourself, a realm-mate, a group member: a dark green ghostly form, as the old
## client draws them (game.dll's StealthOnlyFigureShader; owner 2026-10-28: "its like a dark green ghostly form").
## Enemies you can't detect are never drawn at all: the server deletes them from your view (StealthECSGameEffect /
## GameBot.Stealth: ObjectDelete to every player who can't detect them).
##
## Hung under the figure's root as a child: every MeshInstance3D below the root (body, armour, cloak, hair, weapons)
## takes the ghost material while it is there - checked a few times a second, so parts the figure dresses or swaps
## while you are hidden turn too - and gets its own material (and shadow) back when it goes (set_on(root, false)).

const NAME := "StealthLook"
const RECHECK_S := 0.25

static var _material: ShaderMaterial = null


## Hides / shows the ghost on a figure (no-op when it is already that way).
static func set_on(root: Node, on: bool) -> void:
	if root == null or not is_instance_valid(root):
		return
	var cur := root.get_node_or_null(NAME)
	if on and cur == null:
		var n := (load("res://game/stealth_look.gd") as GDScript).new() as Node
		n.name = NAME
		root.add_child(n)
	elif not on and cur:
		cur.call("restore")
		cur.queue_free()


static func ghost_material() -> ShaderMaterial:
	if _material == null:
		var sh := Shader.new()
		# unlit dark green, a little brighter at the edges (a silhouette), see-through: the front surface only
		# (depth prepass), so the far side of the body doesn't show through it
		sh.code = """
shader_type spatial;
render_mode unshaded, blend_mix, depth_prepass_alpha, cull_back;
uniform vec3 body : source_color = vec3(0.09, 0.36, 0.12);
uniform vec3 edge : source_color = vec3(0.22, 0.62, 0.26);
void fragment() {
	float rim = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 2.0);
	ALBEDO = mix(body, edge, rim);
	ALPHA = mix(0.75, 0.92, rim);
}
"""
		_material = ShaderMaterial.new()
		_material.shader = sh
	return _material


var _timer := 0.0


func _ready() -> void:
	_apply()


func _process(delta: float) -> void:
	_timer -= delta
	if _timer <= 0.0:
		_timer = RECHECK_S
		_apply()


## Every mesh under the root takes the ghost; what it had is kept on it to give back.
func _apply() -> void:
	var root := get_parent()
	if root == null:
		return
	var ghost := ghost_material()
	for mi in root.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		if m.material_override != ghost:
			m.set_meta("stealth_kept", m.material_override)       # (null: its surfaces' own materials)
			m.material_override = ghost
		if not m.has_meta("stealth_shadow"):                       # a ghost casts no solid shadow
			m.set_meta("stealth_shadow", m.cast_shadow)
			m.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF


func restore() -> void:
	var root := get_parent()
	if root == null:
		return
	var ghost := ghost_material()
	for mi in root.find_children("*", "MeshInstance3D", true, false):
		var m := mi as MeshInstance3D
		if m.material_override == ghost:
			m.material_override = m.get_meta("stealth_kept", null)
		if m.has_meta("stealth_kept"):
			m.remove_meta("stealth_kept")
		if m.has_meta("stealth_shadow"):
			m.cast_shadow = m.get_meta("stealth_shadow")
			m.remove_meta("stealth_shadow")
