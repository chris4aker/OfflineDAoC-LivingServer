class_name NifMaterials
extends RefCounted
## Engine materials for converted DAoC models. pipeline/nif_static.py writes each model's materials into the glTF
## (extras.nif_materials): every texture slot with its texture coordinate (0 = UV, 1 = UV2, 2/3 = CUSTOM0.xy/.zw),
## wrap mode, alpha, sidedness, vertex colours and colours. This builds one ShaderMaterial per spec that layers them
## the way the original renderer did: base x tint x dark (light map) x detail (pre-doubled), decal over the top,
## vertex colour multiply, glow and emissive added, gloss as the specular mask. Every layer is weighted by its own
## alpha, because DAoC also puts transparent overlays (vines, lichen) in the dark and detail slots.

const SLOTS := ["base", "dark", "detail", "decal", "glow", "gloss", "tex1", "tex2", "env"]

var texture_set := "x4plus"
var env_strength := 1.0          # how strong the environment-map reflections are (zone_loader: Tuscaran Glacier)
var use_material_light := false     # pregame scenes: keep the material's ambient darkness (spec mat_light), as the client
var _folders := {}                  # texture set name -> folder with <name>.png
# variant key -> Shader, shared by every zone, scene and effect: one compile a variant a session (owner 2026-10-28:
# each zone's own copy compiled them all again - 40 ms frames once materials were made on the main thread, GpuJobs)
static var _shaders := {}
static var _shaders_mutex := Mutex.new()           # (zone workers build at once)
var _materials := {}                # spec json -> ShaderMaterial
var _textures := {}                 # "set/name" -> Texture2D


func _init(folders: Dictionary, initial_set: String) -> void:
	_folders = folders
	texture_set = initial_set if folders.has(initial_set) else folders.keys()[0]


static var WEB := RegEx.create_from_string("(?i)spiderweb|cob_crnr|cobweb")


## Material for one spec (a Dictionary from extras.nif_materials); identical specs share one material.
## main_only: built on the main thread when asked from a worker (GpuJobs) - a ShaderMaterial's parameters set on a
## worker make its render-server material there, the race GpuJobs exists for (2026-10-30: the login freeze -
## "Parameter 'material' is null" x4, then the main thread stalled with effect-prefetch workers waiting on it, or a
## crash; NifScene sets it for effects, which are packed on workers ahead of time).
var main_only := false


func material(spec: Dictionary) -> ShaderMaterial:
	var key := JSON.stringify(spec, "", true)
	if _materials.has(key):
		return _materials[key]
	if main_only and not GpuJobs.on_main_thread():
		return GpuJobs.run(func(): return material(spec), "effect material") as ShaderMaterial
	var mat := ShaderMaterial.new()
	var alpha := String(spec.get("alpha", "opaque"))
	# spider webs (nspiderweb, cob_crnr): soft alpha the client blended - as a cut-out at their near-zero test
	# threshold they drew as solid white sheets (owner 2026-10-11: Myrkwood's Creepy Web Pine). Only webs: grass,
	# leaves, beams and banners share the low threshold but their alpha is all or nothing - a cut-out is right there.
	if alpha == "mask" and WEB.search(String((spec.get("slots", {}) as Dictionary).get("base", {}).get("tex", ""))):
		alpha = "blend"
	var dt: Array = spec.get("diffuse_tint", [])
	mat.shader = _shader(alpha, bool(spec.get("double_sided", false)), bool(spec.get("unlit", false)), dt.size() == 3,
			bool(spec.get("ambient_only", false)), bool(spec.get("src_invdest", false)) or String(spec.get("dest_keep", "")) == "invsrcalpha")
	if dt.size() == 3:                                    # ambient and diffuse colours of different hues (see _shader)
		mat.set_shader_parameter("diffuse_tint", Vector3(float(dt[0]), float(dt[1]), float(dt[2])))
		var tn: Array = spec.get("tint", [1, 1, 1])
		var dc: Array = spec.get("diffuse", [float(tn[0]) * float(dt[0]), float(tn[1]) * float(dt[1]), float(tn[2]) * float(dt[2])])
		mat.set_shader_parameter("diffuse_col", Vector3(float(dc[0]), float(dc[1]), float(dc[2])))
		mat.set_shader_parameter("split_tint", true)
	mat.set_meta("spec", spec)
	var slots: Dictionary = spec.get("slots", {})
	for slot in SLOTS:
		var has: bool = slots.has(slot)
		mat.set_shader_parameter("has_" + slot, has)
		if has:
			mat.set_shader_parameter(slot + "_uv", int(slots[slot].uv))
			mat.set_shader_parameter(slot + "_wrap", bool(slots[slot].get("wrap", true)))
	if slots.has("env"):
		mat.set_shader_parameter("env_strength", env_strength)
	var tint: Array = spec.get("tint", [1, 1, 1])
	var emissive: Array = spec.get("emissive", [0, 0, 0])
	var k := 1.0
	if use_material_light:
		# fixed-function light: texture x min(emissive + material ambient x ambient light, 1). Our shader adds lit
		# albedo and emission, so cap the albedo part where the emissive already fills the light (sky domes,
		# grass cards); vertex-coloured shapes take their light from the vertex colours
		var e: float = maxf(maxf(float(emissive[0]), float(emissive[1])), float(emissive[2]))
		var m: float = 1.0 if bool(spec.get("vertex_colors", false)) else float(spec.get("mat_light", 1.0))
		k = clampf(minf(m, 1.0 - e), 0.0, 1.0)
	mat.set_shader_parameter("tint", Color(tint[0], tint[1], tint[2]))
	mat.set_shader_parameter("light_scale", k)
	mat.set_shader_parameter("emissive", Color(emissive[0], emissive[1], emissive[2]))
	mat.set_shader_parameter("use_vertex_color", bool(spec.get("vertex_colors", false)))
	mat.set_shader_parameter("alpha_cut", float(spec.get("cut", 0.5)))
	mat.set_shader_parameter("opacity", float(spec.get("opacity", 1.0)))
	mat.set_shader_parameter("specular_amount", 0.5 if spec.get("specular", false) else 0.25)
	mat.set_shader_parameter("blend2", bool(spec.get("blend2", false)))
	mat.set_shader_parameter("detail_x2", bool(spec.get("detail_x2", false)))
	mat.set_shader_parameter("dark_plain", bool(spec.get("dark_plain", false)))
	mat.set_shader_parameter("billboard", {"face": 1, "up": 2}.get(String(spec.get("billboard", "")), 0))
	var scroll: Dictionary = spec.get("uv_scroll", {})        # NiTextureTransformController: looping scrolls
	for slot in scroll:
		var sc: Array = scroll[slot]
		mat.set_shader_parameter(String(slot) + "_scroll", Vector3(float(sc[0]), float(sc[1]), float(sc[2])))
	_bind_textures(mat)
	_materials[key] = mat
	return mat


func set_texture_set(set_name: String) -> void:
	if not _folders.has(set_name):
		return
	texture_set = set_name
	for mat in _materials.values():
		_bind_textures(mat)


func _bind_textures(mat: ShaderMaterial) -> void:
	var slots: Dictionary = mat.get_meta("spec").get("slots", {})
	for slot in slots:
		mat.set_shader_parameter(slot + "_tex", texture(String(slots[slot].tex)))


func _path(tex_name: String) -> String:
	var path := String(_folders[texture_set]).path_join(tex_name + ".png")
	if not FileAccess.file_exists(path) and _folders.has("original"):
		path = String(_folders["original"]).path_join(tex_name + ".png")   # not upscaled yet: use the client's own
	return path


## The texture's picture read from its file (null if none) - never Texture2D.get_image(), which reads the texture
## back from the GPU: on the seamless-zone worker that broke the frame being drawn ("open draw list"), and every
## texture made after it drew black / pink (owner 2026-10-17: black skeletons and horses, pink forge smoke).
func image(tex_name: String) -> Image:
	return Image.load_from_file(_path(tex_name)) if FileAccess.file_exists(_path(tex_name)) else null


func texture(tex_name: String) -> Texture2D:
	var key := "%s/%s" % [texture_set, tex_name]
	if not _textures.has(key):
		var path := _path(tex_name)
		var img := Image.load_from_file(path)
		if img == null:
			push_warning("missing texture " + path)
			_textures[key] = null
		else:
			img.generate_mipmaps()
			_textures[key] = ImageTexture.create_from_image(img)
	return _textures[key]


## invdest: the layer's source factor is INVDESTCOLOR (spec src_invdest, nif_static): its colour added in full (no
## alpha multiply) and, for a blend, its alpha only dims what's behind - a premultiplied blend (corpse lights ...).
## destcolor: source factor DESTCOLOR (nif_static): what's behind x (1 + colour), or with invdest (destination
## INVSRCALPHA, spec dest_keep) x (colour + 1 - alpha) - a multiply blend; black leaves what's behind as it was.
func _shader(alpha: String, double_sided: bool, unlit := false, split := false, ambient_only := false, invdest := false) -> Shader:
	var key := "%s/%s/%s/%s/%s/%s" % [alpha, double_sided, unlit, split, ambient_only, invdest]
	_shaders_mutex.lock()
	var have: Shader = _shaders.get(key)
	_shaders_mutex.unlock()
	if have:
		return have
	var modes := ["cull_disabled" if double_sided else "cull_back"]
	unlit = unlit and alpha != "add" and alpha != "destcolor"   # spec "unlit": the client's emissive-only lighting mode
	if unlit and alpha != "screen":
		modes.append("unshaded")
	if alpha == "blend" and invdest:
		modes.append_array(["blend_premul_alpha", "depth_draw_never"])
	elif alpha == "blend":
		modes.append("depth_draw_opaque")
	elif alpha == "add":                                 # additive glows (flares, cores, plasma): no depth write
		# fog off: fogging an additive card ADDS the fog colour (torch flames turned into grey cones, owner 2026-10-09)
		modes.append_array(["blend_add", "depth_draw_never", "unshaded", "fog_disabled"])
	elif alpha == "screen":                              # client src SRCALPHA, dst INVSRCCOLOR (black-backed clouds)
		modes.append_array(["blend_mix", "depth_draw_never", "unshaded"])
	elif alpha == "destcolor":                           # behind x ALBEDO (above 1 brightens: the HDR buffer keeps it)
		modes.append_array(["blend_mul", "depth_draw_never", "unshaded", "fog_disabled"])
	var code := "shader_type spatial;\nrender_mode %s;\n" % ", ".join(modes) + SHADER_BODY
	if ambient_only and not unlit and alpha != "add" and alpha != "screen":
		# the zone's nifs.csv "Ambient Only" column (trees, evergreens, bushes): the client lights these without any
		# shading - ambient plus the whole sun / moon light, no angle falloff, no shadows (Midgard noon: ambient
		# 0.42/0.49/0.60 + sun 0.5 = the owner's screenshot of a Small Evergreen at its texture's own colour). Their
		# cards share normals at the trunk line, which angle shading turned into a black column (owner 2026-10-14:
		# Malmohus' pines, "giant black lines"). Lamps and the torch still fall off with distance.
		code += """
uniform vec3 diffuse_tint = vec3(1.0);
void light() {
	DIFFUSE_LIGHT += (LIGHT_IS_DIRECTIONAL ? 1.0 : ATTENUATION) * LIGHT_COLOR / PI * diffuse_tint;
}
"""
	elif split and not unlit and alpha != "add" and alpha != "screen":
		# the client's E_A_D lighting with an ambient colour of another hue than the diffuse: ALBEDO carries the
		# ambient colour (the ambient light shows it) and the direct light is tinted diffuse / ambient - so the sun
		# shows the diffuse colour, as the client (owner 2026-10-14: Jordheim's grass bright green, paths orange)
		# ... the direct light on the UNTINTED texture x the diffuse colour, as specular light (not multiplied by the
		# ambient-tinted ALBEDO): a ratio diffuse / ambient can't bring back a channel the ambient lacks (owner
		# 2026-10-15: Trollheim's ice wall, ambient 0 / 0.72 / 1 and diffuse 0.84 / 0.96 / 1, was cyan in the sun -
		# the old client's is pale). Where the ratio was exact (most materials) the result is the same as before.
		code += """
uniform vec3 diffuse_col = vec3(1.0);
void light() {
	SPECULAR_LIGHT += clamp(dot(NORMAL, LIGHT), 0.0, 1.0) * ATTENUATION * LIGHT_COLOR / PI * split_raw * diffuse_col;
}
"""
	if unlit:                                            # unshaded draws ALBEDO alone: the emission is its colour
		code = code.replace("//UNLIT_OUT", "ALBEDO = EMISSION;")
	if alpha == "mask":
		code = code.replace("//ALPHA_OUT", "ALPHA = c.a * opacity;\n\tALPHA_SCISSOR_THRESHOLD = alpha_cut;")
	elif alpha == "blend":
		code = code.replace("//ALPHA_OUT", "ALPHA = c.a * opacity;")
	elif alpha == "add":
		# unshaded, so the fixed-function light goes in here: texture x min(emissive + lit part, 1) (an emissive-only
		# flame card had light_scale 0 and drew nothing)
		code = code.replace("//ALPHA_OUT", "ALBEDO = c.rgb * min(emissive + vec3(light_scale), vec3(1.0)) * %s;" % (
				"opacity" if invdest else "c.a * opacity"))
	elif alpha == "destcolor":
		# the client blends in gamma space with its colour capped at 1 (fixed function): a mid grey brightens 1.5x in
		# gamma = 2.4x in our linear buffer, not 1.2x
		code = code.replace("//ALPHA_OUT", "ALBEDO = pow(pow(clamp(c.rgb * min(emissive + vec3(light_scale), vec3(1.0)), 0.0, 1.0), vec3(1.0 / 2.2)) + vec3(%s), vec3(2.2));" % (
				"1.0 - clamp(c.a * opacity, 0.0, 1.0)" if invdest else "1.0"))
	elif alpha == "screen":                              # c*a + behind*(1 - c*a) as a mix blend: alpha = brightest channel
		code = code.replace("//ALPHA_OUT", "float m = max(max(ALBEDO.r, ALBEDO.g), max(ALBEDO.b, 0.001));
	ALPHA = m * c.a * opacity;
	ALBEDO /= m;")
	var shader := Shader.new()
	shader.code = code
	_shaders_mutex.lock()
	if _shaders.has(key):                               # (another worker made it meanwhile: share that one)
		shader = _shaders[key]
	else:
		_shaders[key] = shader
	_shaders_mutex.unlock()
	return shader


const SHADER_BODY := """
uniform sampler2D base_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D dark_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D detail_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D decal_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D glow_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gloss_tex : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D env_tex : source_color, filter_linear_mipmap, repeat_enable;   // NiTextureEffect sphere map
uniform bool has_env;
uniform float env_strength = 1.0;
uniform sampler2D tex1_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D tex2_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform bool has_tex1; uniform bool has_tex2; uniform int tex1_uv; uniform int tex2_uv;
uniform bool tex1_wrap = true; uniform bool tex2_wrap = true;
uniform bool blend2;            // client two-texture blend: mix(tex2, tex1, vertex alpha), vertex rgb = baked light
uniform bool detail_x2 = false; // detail texture stored as is: multiply x2 here (pregame light maps go above 1.0)
uniform bool dark_plain = false; // the dark map multiplies colour and alpha as the client's (an effect's shaping mask: NifScene)
uniform int billboard = 0;      // 1 face the camera, 2 turn about the up axis (NiBillboardNode)
uniform bool has_base; uniform bool has_dark; uniform bool has_detail;
uniform bool has_decal; uniform bool has_glow; uniform bool has_gloss;
uniform int base_uv; uniform int dark_uv; uniform int detail_uv; uniform int decal_uv; uniform int glow_uv; uniform int gloss_uv;
uniform bool base_wrap = true; uniform bool dark_wrap = true; uniform bool detail_wrap = true;
uniform bool decal_wrap = true; uniform bool glow_wrap = true; uniform bool gloss_wrap = true;
uniform vec3 tint : source_color = vec3(1.0);
uniform vec3 emissive : source_color = vec3(0.0);
uniform bool use_vertex_color;
uniform float alpha_cut = 0.5;
uniform float opacity = 1.0;
uniform float specular_amount = 0.25;
// NiTextureTransformController scrolls: (du, dv, period in s) per slot, 0 period = still
uniform vec3 base_scroll = vec3(0.0); uniform vec3 dark_scroll = vec3(0.0); uniform vec3 detail_scroll = vec3(0.0);
uniform vec3 decal_scroll = vec3(0.0); uniform vec3 glow_scroll = vec3(0.0);
// NiUVController that plays once / ping-pongs: the offset at the effect's time, set by NifScene (uv_anim)
uniform vec2 base_offset = vec2(0.0); uniform vec2 dark_offset = vec2(0.0); uniform vec2 detail_offset = vec2(0.0);
uniform vec2 decal_offset = vec2(0.0); uniform vec2 glow_offset = vec2(0.0);
uniform float light_scale = 1.0;   // pregame: the lit part only (emission keeps the full colour)
uniform bool split_tint = false;   // split light (_shader): the tint on ALBEDO only, split_raw keeps the colour without it
varying vec3 split_raw;

varying vec2 uv_a;
varying vec2 uv_b;
varying vec2 uv_c;
varying vec2 uv_d;
varying vec2 env_sm;               // the sphere map coordinate, per vertex as the client made it

void vertex() {
	if (billboard == 1) {
		// NetImmerse NiBillboardNode: the card's own +Z faces the camera and +Y is up. The vertices arrive axis-
		// converted (Godot = NIF x, z, -y), so take the NIF axes back: view right = x, up = NIF y, toward = NIF z
		// ... turned half round about the up axis: the card's +Z points AWAY from the camera (owner 2026-10-15: a corpse
		// light's 2.5 m streak cylinder is a dim ring round the flame with the core in front - pointed at the camera
		// it was a giant cone; a flat card looks the same from either side)
		VERTEX = vec3(-VERTEX.x, -VERTEX.z, -VERTEX.y);
		NORMAL = vec3(0.0, 0.0, 1.0);
		// at the node's own scale (an effect sized to its target - the cards were always drawn full size), facing the
		// camera's POSITION (not just its view plane): the corpse lights' cylinders off to the side of the screen ran
		// toward its middle as smears; facing the camera's position each is seen end-on, a ring round its core
		float s = length(MODEL_MATRIX[0].xyz);
		vec3 to_cam = INV_VIEW_MATRIX[3].xyz - MODEL_MATRIX[3].xyz;
		to_cam = length(to_cam) > 0.0001 ? normalize(to_cam) : INV_VIEW_MATRIX[2].xyz;
		vec3 right = cross(INV_VIEW_MATRIX[1].xyz, to_cam);
		right = length(right) > 0.0001 ? normalize(right) : INV_VIEW_MATRIX[0].xyz;
		vec3 up = cross(to_cam, right);
		MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(right * s, 0.0), vec4(up * s, 0.0), vec4(to_cam * s, 0.0), MODEL_MATRIX[3]);
	} else if (billboard == 2) {
		float s = length(MODEL_MATRIX[0].xyz);
		vec3 up = vec3(0.0, 1.0, 0.0);
		vec3 side = normalize(cross(up, INV_VIEW_MATRIX[2].xyz));
		vec3 fwd = normalize(cross(side, up));
		MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(side * s, 0.0), vec4(up * s, 0.0), vec4(fwd * s, 0.0), MODEL_MATRIX[3]);
	}
	uv_a = UV;
	uv_b = UV2;
	uv_c = CUSTOM0.xy;
	uv_d = CUSTOM0.zw;
	if (has_env) {
		// the client's sphere-mapped environment map: NetImmerse's fixed function made the coordinate per VERTEX
		// (the camera-space reflection) and the card blended it across the triangle. Per pixel it hits the sphere
		// map's pole at grazing angles - white streaks fanning from a point on Tuscaran Glacier's floor that swung
		// with the camera (owner 2026-10-17)
		vec3 vp = (MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
		vec3 vn = normalize((MODELVIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
		vec3 r = reflect(normalize(vp), vn);
		float sm = max(2.0 * sqrt(r.x * r.x + r.y * r.y + (r.z + 1.0) * (r.z + 1.0)), 1e-4);
		env_sm = vec2(r.x / sm + 0.5, 0.5 - r.y / sm);
	}
}

// a clamped layer (the NIF's CLAMP_S_CLAMP_T): its coordinates held half a texel - at the mip level drawn - inside
// the picture. The samplers repeat, and clamping to 0 / 1 alone let the filter blend in the opposite edge (owner
// 2026-10-11: the earth crack's plates took its hot rim glow's bright bottom row and looked like orange playdough)
vec2 pick(int i, bool wrap, vec2 size) {
	vec2 uv = i == 0 ? uv_a : (i == 1 ? uv_b : (i == 2 ? uv_c : uv_d));
	if (wrap) return uv;
	vec2 d = max(abs(dFdx(uv)), abs(dFdy(uv))) * size;
	vec2 inset = 0.5 * exp2(max(0.0, log2(max(max(d.x, d.y), 1e-6)))) / size;
	return clamp(uv, min(inset, vec2(0.5)), max(1.0 - inset, vec2(0.5)));
}

vec2 scr(vec3 s) {
	return s.z > 0.0 ? s.xy * fract(TIME / s.z) : vec2(0.0);
}

bool inside(int i) {
	vec2 uv = i == 0 ? uv_a : (i == 1 ? uv_b : (i == 2 ? uv_c : uv_d));
	return all(greaterThanEqual(uv, vec2(0.0))) && all(lessThanEqual(uv, vec2(1.0)));
}

void fragment() {
	vec4 c = has_base ? texture(base_tex, pick(base_uv, base_wrap, vec2(textureSize(base_tex, 0))) + scr(base_scroll) + base_offset) : vec4(1.0);
	if (blend2 && has_tex1 && has_tex2) {
		vec4 t1 = texture(tex1_tex, pick(tex1_uv, tex1_wrap, vec2(textureSize(tex1_tex, 0))));
		vec4 t2 = texture(tex2_tex, pick(tex2_uv, tex2_wrap, vec2(textureSize(tex2_tex, 0))));
		c = vec4(mix(t2.rgb, t1.rgb, COLOR.a), 1.0);
	}
	if (!split_tint) c.rgb *= tint;
	// dark and detail maps multiply, weighted by their own alpha: opaque light maps darken everywhere, while DAoC's
	// transparent overlays in these slots (climbing vines, lichen) only touch the surface where they're drawn
	if (has_dark) {
		vec4 d = texture(dark_tex, pick(dark_uv, dark_wrap, vec2(textureSize(dark_tex, 0))) + scr(dark_scroll) + dark_offset);
		if (dark_plain) c *= d; else c.rgb *= mix(vec3(1.0), d.rgb, d.a);
	}
	if (has_detail) {
		vec4 d = texture(detail_tex, pick(detail_uv, detail_wrap, vec2(textureSize(detail_tex, 0))) + scr(detail_scroll) + detail_offset);
		c.rgb *= mix(vec3(1.0), d.rgb * (detail_x2 ? 4.594 : 1.0), d.a);   // x2 in the client's gamma space = x2^2.2 in linear
	}
	if (has_decal && (decal_wrap || inside(decal_uv))) {
		vec4 d = texture(decal_tex, pick(decal_uv, decal_wrap, vec2(textureSize(decal_tex, 0))) + scr(decal_scroll) + decal_offset);
		c.rgb = mix(c.rgb, d.rgb, d.a);
	}
	if (use_vertex_color) c *= blend2 ? vec4(COLOR.rgb, 1.0) : COLOR;
	split_raw = c.rgb * light_scale;
	if (split_tint) c.rgb *= tint;
	ALBEDO = c.rgb * light_scale;
	vec4 g = has_glow ? texture(glow_tex, pick(glow_uv, glow_wrap, vec2(textureSize(glow_tex, 0))) + scr(glow_scroll) + glow_offset) : vec4(0.0);
	EMISSION = emissive * c.rgb + g.rgb * g.a;
	if (has_env) {
		// the client's sphere-mapped environment map (NiTextureEffect): the view reflection's sphere-map coordinate,
		// added over the surface (the glassy shields, potions, slime blobs - owner 2026-10-11: missing)
		vec3 env = texture(env_tex, env_sm).rgb;
		// a gloss map says how reflective each spot is (NetImmerse: the environment map scaled by the gloss map) -
		// the client multiplied in its gamma space, so the raw gloss bytes count squared-ish here in linear. Unscaled,
		// Tuscaran Glacier's ice (icymap over icesheetnoalpha) drowned its own blue sheet in a moving white glare
		// (owner 2026-10-17: "the old client is reflective too but its nowhere near what ours is")
		if (has_gloss)
			env *= pow(texture(gloss_tex, pick(gloss_uv, gloss_wrap, vec2(textureSize(gloss_tex, 0)))).rgb, vec3(2.2));
		EMISSION += env * env_strength;
	}
	ROUGHNESS = 0.9;
	SPECULAR = has_gloss ? texture(gloss_tex, pick(gloss_uv, gloss_wrap, vec2(textureSize(gloss_tex, 0)))).r * 0.8 : specular_amount;
	//UNLIT_OUT
	//ALPHA_OUT
}
"""
