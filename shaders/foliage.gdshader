shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

// Tree canopy cards. Vertex attributes from TreeGenerator:
//   COLOR.r  crown-depth AO      COLOR.g  per-card variation
//   COLOR.b  LOD importance      CUSTOM0  pivot.xyz + wind phase
// Cards collapse by importance with distance (continuous LOD), sway on the
// shared trunk bend plus branch motion and edge flutter, and light with a
// spherical "canopy" normal blended into the card normal so the crown reads
// as a volume rather than a pile of planes.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"
#include "res://shaders/inc/tree_wind.gdshaderinc"

global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform float water_level;
global uniform float scene_wetness;
// 1 = write unlit albedo, 2 = write the view-space normal, while the tree
// impostor atlases are baked (see scripts/camp/tree_impostors.gd).
global uniform int impostor_bake;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_trans_tex : filter_linear_mipmap_anisotropic;
uniform float alpha_cutoff : hint_range(0.05, 0.95) = 0.45;
uniform float translucency : hint_range(0.0, 2.0) = 0.75;
uniform float spherical_normal_mix : hint_range(0.0, 1.0) = 0.65;
uniform float flutter : hint_range(0.0, 1.0) = 0.14;
uniform float lod_start = 45.0;
uniform float lod_end = 120.0;
// Share of cards (by importance) collapsed at full LOD and how much the
// survivors grow to keep the crown filled. Presets tune both per distance.
uniform float lod_keep : hint_range(0.0, 0.95) = 0.48;
uniform float lod_grow : hint_range(0.0, 1.5) = 0.12;
uniform float roughness : hint_range(0.0, 1.0) = 0.55;

// MultiMesh instances carry (height/40, crown height/40, crown radius/20,
// tint variation) in INSTANCE_CUSTOM instead of per-instance uniforms.
uniform bool use_instance_custom = false;
uniform vec2 crown_offset = vec2(0.0);
instance uniform float tree_height = 12.0;
instance uniform vec4 crown = vec4(0.0, 8.0, 0.0, 4.0);
instance uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);

varying float v_ao;
varying float v_variation;
varying vec3 v_sphere_normal;
varying vec3 world_pos;
varying vec3 v_tint;

void vertex() {
	float th = tree_height;
	vec4 cr = crown;
	v_tint = tint;
	if (use_instance_custom) {
		th = INSTANCE_CUSTOM.x * 40.0;
		cr = vec4(crown_offset.x, INSTANCE_CUSTOM.y * 40.0, crown_offset.y, INSTANCE_CUSTOM.z * 20.0);
		v_tint = mix(vec3(0.92, 0.98, 0.85), vec3(1.05, 1.0, 0.95), INSTANCE_CUSTOM.w);
	}
	vec3 base_world = MODEL_MATRIX[3].xyz;
	vec2 wind = wind_field(base_world, wind_direction, wind_strength, TIME);
	vec3 pivot = CUSTOM0.xyz;
	float phase = CUSTOM0.w;
	float importance = COLOR.b;
	vec3 local = VERTEX;

	// Continuous LOD: unimportant cards shrink to their pivot with distance
	// while the survivors grow to keep the silhouette filled.
	float cam_dist = length(CAMERA_POSITION_WORLD - base_world);
	float lod = smoothstep(lod_start, lod_end, cam_dist);
	vec3 reach = local - pivot;
	float keep = smoothstep(lod * lod_keep - 0.10, lod * lod_keep, importance);
	reach *= keep * (1.0 + lod * lod_grow);
	float reach_len = length(reach);

	// Branch sway and leaf flutter, stronger high in the crown.
	float rel = clamp(pivot.y / max(th, 0.1), 0.0, 1.0);
	float sway_wave = sin(TIME * 1.6 + phase * 6.2832 + pivot.x * 0.4 + pivot.z * 0.3);
	vec3 sway = vec3(wind.x, 0.0, wind.y) * (0.04 + 0.22 * rel * rel) * (0.55 + 0.45 * sway_wave);
	float flut = sin(TIME * (3.5 + phase * 2.5) + phase * 19.0 + local.x * 1.7 + local.z * 1.3);
	flut *= flutter * reach_len * clamp(wind_strength, 0.2, 1.5);
	local = pivot + reach + sway * reach_len + vec3(flut * 0.35, flut * 0.5, flut * 0.35);
	local = tree_bend(local, th, wind);
	VERTEX = local;

	// Canopy normal: from the crown centre through the card, biased upward so
	// the top of the crown catches sky light.
	vec3 sphere_local = pivot - cr.xyz;
	sphere_local.y += cr.w * 0.12;
	v_sphere_normal = normalize(MODEL_NORMAL_MATRIX * normalize(sphere_local + vec3(1e-4)));
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_ao = COLOR.r;
	v_variation = COLOR.g;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) {
		discard;
	}
	vec4 tex = texture(albedo_tex, UV);
	// Alpha shrinks in the mip chain; boost it so distant canopies stay full.
	float mip = textureQueryLod(albedo_tex, UV).x;
	float alpha = tex.a * (1.0 + clamp(mip, 0.0, 6.0) * 0.22);
	ALPHA = alpha;
	ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;

	vec4 nt = texture(normal_trans_tex, UV);
	vec3 n_ts = nt.rgb * 2.0 - 1.0;

	// Forward+ already orients NORMAL on cull_disabled back faces.
	vec3 face_n = NORMAL;
	vec3 sphere_view = view_normal_from_world(VIEW_MATRIX, v_sphere_normal);
	vec3 n = normalize(mix(face_n, sphere_view, spherical_normal_mix));
	n = normalize(n + TANGENT * n_ts.x * 0.45 + BINORMAL * n_ts.y * 0.45);
	NORMAL = n;

	vec3 albedo = tex.rgb * v_tint;
	albedo *= 0.9 + 0.2 * v_variation;
	albedo = mix(albedo, albedo * vec3(1.1, 1.0, 0.82), (v_variation - 0.5) * 0.45);

	float exposed = mix(0.35, 1.0, v_ao) * (0.55 + 0.45 * max(v_sphere_normal.y, 0.0));
	float wet = clamp(scene_wetness, 0.0, 1.0) * exposed;
	ALBEDO = albedo * (1.0 - wet * 0.08);
	AO = mix(0.4, 1.0, v_ao);
	AO_LIGHT_AFFECT = 0.45;
	ROUGHNESS = mix(roughness, 0.28, wet * 0.75);
	SPECULAR = mix(0.3, 0.42, wet);
	// Native backlighting is multiplied by ALBEDO by the renderer. A second
	// albedo multiplication here used to suppress low-angle transmitted sun.
	BACKLIGHT = vec3(1.0, 0.98, 0.88) * nt.a * translucency;
	if (impostor_bake == 1) {
		EMISSION = ALBEDO * mix(0.55, 1.0, v_ao);
		ALBEDO = vec3(0.0);
		BACKLIGHT = vec3(0.0);
	} else if (impostor_bake == 2) {
		// Pre-compensate the 8-bit sRGB write so the raw texel holds n * 0.5 + 0.5.
		EMISSION = pow(n * 0.5 + 0.5, vec3(2.2));
		ALBEDO = vec3(0.0);
		BACKLIGHT = vec3(0.0);
	}
}
