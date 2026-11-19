shader_type spatial;
// Zone ground, two modes:
//  blend (use_splat): the Catacombs-era ground the current client draws. One terrain chunk = one 8192-unit patch:
//    a base texture, then up to 8 layers laid over it in order by their weight maps (pipeline/terrain_splat.py);
//    tiling layers repeat `tiles` times across the whole zone (256 = every 6.4 m, matching the client's pebble and
//    grass scale), the zone colour map spans the whole zone once. Far away it fades to
//    the client's own baked ground (far_tex), as the original's LOD did.
//  painted: one whole-zone colour image (1.65 painted ground or the baked ground) with a tiling grain shaded in.

uniform bool use_splat = false;
uniform sampler2D tile_tex : source_color, filter_linear_mipmap_anisotropic, repeat_disable;   // painted / far ground
uniform sampler2D detail_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float detail_repeat_m = 3.0;
uniform float detail_strength = 0.45;
uniform float detail_mean = 0.4;
uniform float detail_fade_m = 90.0;

uniform sampler2D base_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float base_tiles = 256.0;
uniform sampler2D layer_tex[8] : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
// the same textures, clamped: a layer drawn once across the zone (not tileable - the zone colour map) sampled with
// wrap-around blended the far side of the zone into every border - a pale strip along each zone seam (owner
// 2026-10-29: "white lines for every zone boundry"; the client has no visible borders)
uniform sampler2D layer_tex_clamped[8] : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float layer_tiles[8];
uniform float layer_tileable[8];
uniform int layer_count = 0;
uniform sampler2D weights0 : filter_linear, repeat_disable;
uniform sampler2D weights1 : filter_linear, repeat_disable;
uniform vec2 patch_index;              // which patch of the zone this chunk is (column, row)
uniform float patches = 8.0;
uniform float far_fade_m = 260.0;      // blend gives way to the baked ground beyond this distance

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float layer_weight(int i, vec2 puv) {
	vec4 w = i < 4 ? texture(weights0, puv) : texture(weights1, puv);
	int k = i % 4;
	return k == 0 ? w.r : (k == 1 ? w.g : (k == 2 ? w.b : w.a));
}

void fragment() {
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	vec3 painted = texture(tile_tex, UV).rgb;
	if (use_splat) {
		vec2 puv = UV * patches - patch_index;         // 0..1 across this patch
		// screen-space UV rates taken once, outside the loop: sampling inside the per-layer branches with implicit
		// derivatives picked wrong mip levels, and the fine pebble textures sparkled as grain (owner, 2026-10-09)
		vec2 dx = dFdx(UV);
		vec2 dy = dFdy(UV);
		vec3 c = textureGrad(base_tex, UV * base_tiles, dx * base_tiles, dy * base_tiles).rgb;
		for (int i = 0; i < 8; i++) {
			if (i >= layer_count) break;
			float w = layer_weight(i, puv);
			if (w <= 0.002) continue;
			vec3 lc;
			if (layer_tileable[i] > 0.5) {
				float tiles = layer_tiles[i];
				lc = textureGrad(layer_tex[i], UV * tiles, dx * tiles, dy * tiles).rgb;
			} else {
				lc = textureGrad(layer_tex_clamped[i], UV, dx, dy).rgb;
			}
			c = mix(c, lc, w);
		}
		float far = smoothstep(far_fade_m * 0.6, far_fade_m, dist);
		ALBEDO = mix(c, painted, far);
	} else {
		vec3 d = texture(detail_tex, world_pos.xz / detail_repeat_m).rgb;
		float lum = dot(d, vec3(0.299, 0.587, 0.114)) / max(detail_mean, 0.01);
		float fade = 1.0 - smoothstep(detail_fade_m * 0.4, detail_fade_m, dist);
		ALBEDO = painted * mix(1.0, lum, detail_strength * fade);
	}
	ROUGHNESS = 0.95;
	SPECULAR = 0.25;
}
