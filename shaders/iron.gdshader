shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

// Blackened/cast camp iron. Dedicated material avoids treating metal as a
// metallic rock photoscan, which transferred stone colour/normal structure to
// cleats, lantern frames and hardware.
#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 fire_position;
global uniform float fire_intensity;

uniform sampler2D noise_tex : hint_default_white, repeat_enable;
uniform vec3 tint : source_color = vec3(0.16, 0.15, 0.14);
uniform float roughness : hint_range(0.05, 1.0) = 0.46;
uniform float patina : hint_range(0.0, 1.0) = 0.32;
uniform float metallic : hint_range(0.0, 1.0) = 0.88;

varying vec3 world_pos;
varying vec3 local_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	local_pos = VERTEX;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.01)) { discard; }
	vec3 n_world = world_normal_from_view(INV_VIEW_MATRIX, NORMAL);
	float macro = texture(noise_tex, world_pos.xz * 0.72 + world_pos.y * 0.31).r;
	float pits = vnoise3(local_pos * 74.0 + vec3(3.7, 8.1, 1.9));
	pits = mix(pits, 0.5, smoothstep(0.5, 1.5, length(fwidth(local_pos * 74.0))));
	float pit_mask = smoothstep(0.63, 0.91, pits) * patina;
	float oxide_noise = texture(noise_tex, world_pos.xz * 1.7 + world_pos.y * 0.8).g;
	float sheltered = 1.0 - max(n_world.y, 0.0);
	float oxide = smoothstep(0.48, 0.82, oxide_noise * 0.64 + macro * 0.36) * patina;
	oxide *= 0.45 + 0.55 * sheltered;

	vec3 black_iron = tint * (0.80 + macro * 0.30);
	vec3 oxide_color = vec3(0.20, 0.095, 0.035) * (0.72 + macro * 0.42);
	vec3 albedo = mix(black_iron, oxide_color, oxide * 0.42);
	albedo *= 1.0 - pit_mask * 0.16;

	float rain_pattern = texture(noise_tex, world_pos.xz * 5.2 + world_pos.y * 0.19).b;
	float wet = clamp(scene_wetness, 0.0, 1.0) * smoothstep(0.22, 0.84, rain_pattern);
	wet *= 0.62 + 0.38 * max(n_world.y, 0.0);
	albedo *= 1.0 - wet * 0.18;

	float r = roughness + pit_mask * 0.20 + oxide * 0.18;
	r = mix(r, 0.13 + macro * 0.08, wet * 0.78);
	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.44) * fire_intensity;

	ALBEDO = albedo;
	ROUGHNESS = clamp(r, 0.08, 0.95);
	METALLIC = clamp(metallic * (1.0 - oxide * 0.34), 0.0, 1.0);
	SPECULAR = mix(0.42, 0.52, wet);
	CLEARCOAT = wet * 0.36;
	CLEARCOAT_ROUGHNESS = 0.10 + macro * 0.08;
	EMISSION = black_iron * vec3(1.0, 0.30, 0.045) * warmth * 0.018;
}
