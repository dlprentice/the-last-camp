shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Weathered granite for Tom's steps, his bench, the outcrop's bedrock and the old
// ring's stones. The colour is built at the stone's own scale: a grey-buff base
// with millimetre feldspar, quartz and biotite grains, pale crustose lichen
// rosettes on the faces that see the sky, and moss and soil where the stone meets
// the ground (baked per vertex from the real ground height: COLOR.r soil, COLOR.g
// moss). The credited rock photoscan only supplies surface relief and a faint
// tonal drift, sampled small, so no stone shows its large blotches.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 fire_position;
global uniform float fire_intensity;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;

uniform vec3 base_color : source_color = vec3(0.40, 0.385, 0.355);
uniform float relief_tile = 1.9;
uniform float normal_strength : hint_range(0.0, 2.0) = 1.25;
uniform float lichen_amount : hint_range(0.0, 1.0) = 0.45;
uniform float moss_amount : hint_range(0.0, 1.0) = 0.7;
uniform vec3 lichen_color : source_color = vec3(0.56, 0.58, 0.50);
uniform vec3 moss_color : source_color = vec3(0.20, 0.29, 0.11);
uniform vec3 soil_color : source_color = vec3(0.17, 0.13, 0.09);

varying vec3 world_pos;
varying vec3 world_nrm;
varying vec3 tex_offset;
varying float v_soil;
varying float v_moss;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_nrm = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	vec3 origin = MODEL_MATRIX[3].xyz;
	float seed = fract(sin(dot(floor(origin * 4.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453);
	tex_offset = vec3(seed * 7.3, fract(seed * 5.71) * 6.1, fract(seed * 3.37) * 8.7);
	v_soil = COLOR.r;
	v_moss = COLOR.g;
}

vec3 tri_w(vec3 n) {
	vec3 w = pow(abs(n), vec3(4.0));
	return w / max(w.x + w.y + w.z, 1e-4);
}

// Godot needs one helper per sampler: a sampler argument must keep its hints.
vec4 tri_noise(vec3 p, vec3 w, float scale) {
	return texture(noise_tex, p.zy * scale) * w.x + texture(noise_tex, p.xz * scale) * w.y + texture(noise_tex, p.xy * scale) * w.z;
}

vec4 tri_albedo(vec3 p, vec3 w, float scale) {
	return texture(albedo_tex, p.zy * scale) * w.x + texture(albedo_tex, p.xz * scale) * w.y + texture(albedo_tex, p.xy * scale) * w.z;
}

vec4 tri_orm(vec3 p, vec3 w, float scale) {
	return texture(orm_tex, p.zy * scale) * w.x + texture(orm_tex, p.xz * scale) * w.y + texture(orm_tex, p.xy * scale) * w.z;
}

vec3 tri_normal(vec3 p, vec3 n, vec3 w, float scale) {
	vec3 nx = texture(normal_tex, p.zy * scale).xyz * 2.0 - 1.0;
	vec3 ny = texture(normal_tex, p.xz * scale).xyz * 2.0 - 1.0;
	vec3 nz = texture(normal_tex, p.xy * scale).xyz * 2.0 - 1.0;
	nx = normalize(vec3(nx.z * sign(n.x), nx.y, nx.x));
	ny = normalize(vec3(ny.x, ny.z * sign(n.y), ny.y));
	nz = normalize(vec3(nz.x, nz.y, nz.z * sign(n.z)));
	vec3 blended = nx * w.x + ny * w.y + nz * w.z + n;
	return normalize(mix(n, normalize(blended), normal_strength));
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.05)) {
		discard;
	}
	vec3 n = normalize(world_nrm);
	vec3 w = tri_w(n);
	vec3 p = world_pos + tex_offset;
	STREAMING_LOD = streaming_lod(dFdx(p.xz * relief_tile * 0.5), dFdy(p.xz * relief_tile * 0.5));

	// A faint tonal drift from the photoscan, without its colour.
	float drift = dot(tri_albedo(p, w, relief_tile).rgb, vec3(0.3, 0.59, 0.11));
	vec3 albedo = base_color * mix(0.82, 1.14, clamp(drift * 2.4, 0.0, 1.0));
	// Grains: dark biotite flecks and pale quartz, a few millimetres across.
	float grain = tri_noise(p, w, 11.0).r;
	float grain2 = tri_noise(p * 1.7, w, 23.0).b;
	albedo *= mix(1.0, 0.42, smoothstep(0.72, 0.84, grain));
	albedo = mix(albedo, vec3(0.80, 0.78, 0.74), smoothstep(0.80, 0.92, grain2) * 0.6);
	albedo *= mix(0.94, 1.05, tri_noise(p, w, 4.0).g);

	vec3 n_world = tri_normal(p, n, w, relief_tile);
	float up = clamp(n_world.y, 0.0, 1.0);
	// Crustose lichen: pale rosettes a hand across, with a few rust-orange ones.
	float rosette = tri_noise(p, w, 2.6).g * 0.7 + tri_noise(p, w, 7.0).a * 0.3;
	float lichen = smoothstep(0.58, 0.66, rosette) * lichen_amount * (0.35 + 0.65 * up) * (1.0 - v_soil);
	albedo = mix(albedo, lichen_color * mix(0.9, 1.05, grain), lichen * 0.85);
	float orange = smoothstep(0.80, 0.86, tri_noise(p + 3.7, w, 1.7).b) * lichen_amount * up;
	albedo = mix(albedo, vec3(0.62, 0.43, 0.22), orange * 0.7);
	// Moss where the stone meets the soil and in hollows that hold water.
	float breakup = tri_noise(p, w, 5.0).r;
	float moss = clamp(v_moss * 1.2 + up * 0.15 - 0.15, 0.0, 1.0) * smoothstep(0.25, 0.6, breakup + v_moss * 0.4) * moss_amount;
	albedo = mix(albedo, moss_color * (0.8 + 0.4 * breakup), moss);
	albedo = mix(albedo, soil_color, v_soil * 0.85);

	float rough = mix(0.82, 0.93, max(lichen, moss));
	float wet = 1.0 - smoothstep(0.0, 0.6, world_pos.y - water_level);
	wet = max(wet, scene_wetness * (0.55 + up * 0.45));
	albedo *= 1.0 - wet * 0.4 * (1.0 - moss * 0.5);
	rough = mix(rough, 0.45 + breakup * 0.1, wet * (0.9 - moss * 0.4));

	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.42) * fire_intensity * 0.5;

	ALBEDO = albedo;
	ROUGHNESS = clamp(rough, 0.05, 1.0);
	SPECULAR = 0.42;
	AO = mix(1.0, tri_orm(p, w, relief_tile).r, 0.8) * mix(1.0, 0.7, v_soil);
	AO_LIGHT_AFFECT = 0.4;
	NORMAL = view_normal_from_world(VIEW_MATRIX, n_world);
	EMISSION = albedo * vec3(1.0, 0.42, 0.12) * warmth * 0.12;
}
