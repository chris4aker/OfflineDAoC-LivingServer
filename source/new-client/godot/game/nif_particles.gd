class_name NifParticles
extends RefCounted
## NetImmerse 4.x particle systems (fires, smoke, glows, clouds, snow) as Godot GPU particles. Specs come from
## pipeline/nif_static.particle_systems (glTF extras.nif_particles); see WORLD IMPORT LESSONS.md "Effects".


## One NetImmerse particle system (nif_static.particle_systems spec) as GPU particles at `at`.
static func build(spec: Dictionary, at: Transform3D, far: float, materials: NifMaterials) -> GPUParticles3D:
	var e: Dictionary = spec.emitter
	var b: Array = e.basis
	var emitter := Transform3D(Basis(Vector3(b[0][0], b[1][0], b[2][0]), Vector3(b[0][1], b[1][1], b[2][1]),
			Vector3(b[0][2], b[1][2], b[2][2])), Vector3(e.origin[0], e.origin[1], e.origin[2]))
	var p := GPUParticles3D.new()
	p.name = "particles_%s" % spec.get("texture", "")
	p.set_meta("nif_spec", spec)                         # NifScene's effect timeline (emit windows, moving emitters)
	p.transform = at * emitter
	p.local_coords = false
	var life := maxf(float(spec.lifetime), 0.05)
	p.lifetime = life
	# emit_flags bit 0 ("use the emit rate"): the client births emit_rate a second - num_particles is only the pool it
	# allocates. Taken as the count, a hand glow's 116-particle pool at 0.067 s lives made ~1,740 a second (30 in the
	# client) - a solid orb where the original flickers two-frame flashes (owner 2026-10-29, video of the original;
	# ~360 effect systems: every class hand glow, weapon glows). Without the bit, the pool as before.
	if int(spec.get("emit_flags", 0)) & 1:
		p.amount = clampi(int(ceil(float(spec.emit_rate) * (life + float(spec.lifetime_random)))), 1, 512)
	else:
		p.amount = clampi(maxi(int(spec.max_particles), int(ceil(float(spec.emit_rate) * (life + float(spec.lifetime_random))))), 1, 512)
	p.preprocess = life + float(spec.lifetime_random)    # already burning when it comes into view
	p.visibility_range_end = far
	var reach := (float(spec.speed) + float(spec.speed_random)) * (life + float(spec.lifetime_random))
	for v in spec.box:
		reach += float(v) * 0.5
	if spec.get("quad") != null:
		reach += float(spec.quad[0])
	reach = maxf(reach, 4.0)                              # campfires stay small; Midgard's drifting clouds travel ~300 m
	p.visibility_aabb = AABB(Vector3(-reach, -maxf(reach, 1.0), -reach), Vector3(reach, reach, reach) * 2.0)
	var pm := ParticleProcessMaterial.new()
	# emission direction: the emitter's up axis tilted by the declination, turned by the planar angle; the
	# declination variation is the cone
	pm.direction = Vector3.UP.rotated(Vector3.RIGHT, float(spec.declination)).rotated(Vector3.UP, float(spec.planar_angle))
	pm.spread = rad_to_deg(float(spec.declination_variation))
	pm.initial_velocity_min = maxf(float(spec.speed) - float(spec.speed_random), 0.0)
	pm.initial_velocity_max = float(spec.speed) + float(spec.speed_random)
	pm.lifetime_randomness = clampf(float(spec.lifetime_random) / life, 0.0, 1.0)
	var box: Array = spec.box
	if box[0] + box[1] + box[2] > 0.0:
		pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
		pm.emission_box_extents = Vector3(box[0], box[1], box[2]) * 0.5
	var gravity := Vector3.ZERO
	for m in spec.modifiers:
		match String(m.type):
			"gravity":
				var dir := Vector3(m.direction[0], m.direction[1], m.direction[2])
				if String(m.kind) == "spherical":         # pull towards the gravity point, as seen from the emitter
					dir = (Vector3(m.position[0], m.position[1], m.position[2]) - emitter.origin).normalized()
				gravity += at.basis * dir * float(m.force)
			"color":
				var grad := Gradient.new()
				grad.offsets = PackedFloat32Array()
				grad.colors = PackedColorArray()
				for k in m.keys:
					grad.add_point(clampf(float(k[0]), 0.0, 1.0), Color(k[1][0], k[1][1], k[1][2], k[1][3]))
				if grad.get_point_count() > 0:
					var gt := GradientTexture1D.new()
					gt.gradient = grad
					pm.color_ramp = gt
			"growfade":
				var c := Curve.new()
				var g := clampf(float(m.grow) / life, 0.0, 1.0)
				var f := clampf(float(m.fade) / life, 0.0, 1.0)
				c.add_point(Vector2(0.0, 0.0 if g > 0.0 else 1.0))
				if g > 0.0 and g < 1.0:
					c.add_point(Vector2(g, 1.0))
				if f > 0.0 and 1.0 - f > g:
					c.add_point(Vector2(1.0 - f, 1.0))
				c.add_point(Vector2(1.0, 0.0 if f > 0.0 else 1.0))
				var ct := CurveTexture.new()
				ct.curve = c
				pm.scale_curve = ct
			"rotation":                                   # NiParticleRotation: spin in degrees per second
				pm.angular_velocity_min = rad_to_deg(float(m.speed))
				pm.angular_velocity_max = rad_to_deg(float(m.speed))
	pm.gravity = gravity
	p.process_material = pm
	var quad := QuadMesh.new()
	quad.size = Vector2.ONE * float(spec.size) * 2.0     # the controller size is the particle radius (NiParticlesData particle_radius)
	if spec.get("quad") != null:                         # mesh particles: the plane mesh scaled by the radius
		quad.size = Vector2(float(spec.quad[0]), float(spec.quad[1]))
	p.draw_pass_1 = quad
	p.set_meta("nif_quad", quad.size)
	if str(spec.get("blend", "")) in ["screen", "multiply", "invdest_add"]:
		var sm := ShaderMaterial.new()
		sm.shader = _screen_shader() if str(spec.blend) == "screen" else (
				_multiply_shader() if str(spec.blend) == "multiply" else _invdest_add_shader())
		if spec.get("texture") is String and String(spec.texture) != "":
			sm.set_shader_parameter("tex", materials.texture(String(spec.texture)))
		quad.material = sm
		return p
	var mat := StandardMaterial3D.new()
	mat.billboard_mode = BaseMaterial3D.BILLBOARD_PARTICLES
	mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	mat.vertex_color_use_as_albedo = true
	mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	if str(spec.get("blend", "")) == "add":
		mat.blend_mode = BaseMaterial3D.BLEND_MODE_ADD
	elif str(spec.get("blend", "")) == "invdest":
		# the client's INVDESTCOLOR / INVSRCALPHA (corpse lights' wisps): the colour in full, the alpha dimming what's
		# behind - a premultiplied blend (as 'mix' they were grey smoke)
		mat.blend_mode = BaseMaterial3D.BLEND_MODE_PREMULT_ALPHA

	if spec.get("texture") is String and String(spec.texture) != "":
		mat.albedo_texture = materials.texture(String(spec.texture))
		if mat.albedo_texture == null:
			p.visible = false                            # its texture isn't in the client: not a white square
	quad.material = mat
	return p


## Godot carries a particle node's scale into the particles' speed and emitter box but not their size
## (tests/particle_scale_main.gd). Effects scaled to the character they sit on (speffects / moneffects Auto Scale)
## or riding a scaled model call this once they are in the tree: each quad gets its system's world scale.
static func fit_to_scale(root: Node) -> void:
	if root == null:
		return
	for n in root.find_children("*", "GPUParticles3D", true, false):
		var p := n as GPUParticles3D
		if not p.has_meta("nif_quad") or not p.is_inside_tree():
			continue
		var k := p.global_basis.get_scale().x
		if is_equal_approx(float(p.get_meta("fit_k", -1.0)), k):
			continue                                       # a pooled copy at the same size
		var quad: QuadMesh = p.get_meta("own_quad") if p.has_meta("own_quad") else (p.draw_pass_1 as QuadMesh).duplicate()
		quad.size = (p.get_meta("nif_quad") as Vector2) * k
		p.draw_pass_1 = quad
		p.set_meta("own_quad", quad)
		p.set_meta("fit_k", k)


static var _screen: Shader
static var _multiply: Shader
static var _invdest_add: Shader


## The client's INVDESTCOLOR / ONE (a few glows): the particle's colour added in full - its alpha takes no part.
static func _invdest_add_shader() -> Shader:
	if _invdest_add == null:
		_invdest_add = Shader.new()
		_invdest_add.code = SCREEN_SHADER.replace("render_mode blend_mix,", "render_mode blend_add,").replace(
				"""	vec3 c = t.rgb * COLOR.rgb;
	float m = max(max(c.r, c.g), max(c.b, 0.001));
	ALBEDO = c / m;
	ALPHA = m * t.a * COLOR.a;""", """	ALBEDO = t.rgb * COLOR.rgb;""")
	return _invdest_add


## The client's multiply blend (src DESTCOLOR, dst INVSRCALPHA or ZERO): what is behind x (the particle's colour + the
## part its alpha lets through) - a tint that darkens or colours, never glows (the Shaman's poison mist).
static func _multiply_shader() -> Shader:
	if _multiply == null:
		_multiply = Shader.new()
		_multiply.code = SCREEN_SHADER.replace("render_mode blend_mix,", "render_mode blend_mul,").replace(
				"""	vec3 c = t.rgb * COLOR.rgb;
	float m = max(max(c.r, c.g), max(c.b, 0.001));
	ALBEDO = c / m;
	ALPHA = m * t.a * COLOR.a;""", """	float a = t.a * COLOR.a;
	ALBEDO = clamp(t.rgb * COLOR.rgb + vec3(1.0 - a), 0.0, 1.0);   // the blend exactly, as the card clamped it""")
	return _multiply


## Camera-facing particle quads with the client's screen blend (src SRCALPHA, dst INVSRCCOLOR): black adds nothing,
## the colour-ramp alpha fades each particle in and out.
static func _screen_shader() -> Shader:
	if _screen == null:
		_screen = Shader.new()
		_screen.code = SCREEN_SHADER
	return _screen


const SCREEN_SHADER := """
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded;
uniform sampler2D tex : source_color, filter_linear_mipmap, repeat_disable;

void vertex() {
	mat4 w = mat4(normalize(INV_VIEW_MATRIX[0]), normalize(INV_VIEW_MATRIX[1]), normalize(INV_VIEW_MATRIX[2]), MODEL_MATRIX[3]);
	float a = INSTANCE_CUSTOM.x;
	w = w * mat4(vec4(cos(a), -sin(a), 0.0, 0.0), vec4(sin(a), cos(a), 0.0, 0.0), vec4(0.0, 0.0, 1.0, 0.0), vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_MATRIX = VIEW_MATRIX * w;
	MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0.0, 0.0, 0.0),
			vec4(0.0, length(MODEL_MATRIX[1].xyz), 0.0, 0.0), vec4(0.0, 0.0, length(MODEL_MATRIX[2].xyz), 0.0),
			vec4(0.0, 0.0, 0.0, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec4 t = texture(tex, UV);
	vec3 c = t.rgb * COLOR.rgb;
	float m = max(max(c.r, c.g), max(c.b, 0.001));
	ALBEDO = c / m;
	ALPHA = m * t.a * COLOR.a;
}
"""


