shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

// Baked-on camp enamel: broad colour variation, sparse chips exposing the
// steel substrate, and weather response. The old 600x procedural speckle was
// smaller than a pixel in normal gameplay and shimmered instead of reading as
// material detail.
#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 fire_position;
global uniform float fire_intensity;

uniform vec3 tint : source_color = vec3(0.25, 0.38, 0.40);
uniform float chip_amount : hint_range(0.0, 1.0) = 0.32;
uniform float age : hint_range(0.0, 1.0) = 0.45;

varying vec3 local_pos;
varying vec3 world_pos;

void vertex() {
	local_pos = VERTEX;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.01)) { discard; }
	vec3 n_world = world_normal_from_view(INV_VIEW_MATRIX, NORMAL);
	// Two object-space scales keep the pattern locked to the mug/kettle rather
	// than crawling in world space as the prop moves.
	float broad = vnoise3(local_pos * 24.0 + vec3(7.1, 2.9, 5.3));
	float fine = vnoise3(local_pos * 135.0 + vec3(2.3, 9.7, 1.4));
	fine = mix(fine, 0.5, smoothstep(0.5, 1.5, length(fwidth(local_pos * 135.0))));
	float islands = vnoise3(local_pos * 58.0 + vec3(11.0, 4.0, 8.0));
	float chip = smoothstep(0.78, 0.94, fine * 0.58 + islands * 0.42) * chip_amount;
	// Horizontal rims/shoulders collect more handling wear, but keep it subtle
	// enough that arbitrary curved surfaces do not gain a fake white outline.
	float exposure = 0.55 + 0.45 * abs(n_world.y);
	chip *= mix(0.72, 1.18, exposure) * mix(0.75, 1.25, age);
	chip = clamp(chip, 0.0, 0.72);

	vec3 enamel = tint * (0.91 + broad * 0.16);
	// Oxidized exposed steel remains dark, not mirror-bright silver confetti.
	vec3 exposed = vec3(0.085, 0.082, 0.074) * (0.82 + islands * 0.28);
	vec3 albedo = mix(enamel, exposed, chip);

	float rain_breakup = vnoise3(world_pos * 9.0 + vec3(0.0, 17.0, 0.0));
	float wet = clamp(scene_wetness, 0.0, 1.0) * smoothstep(0.18, 0.82, rain_breakup * 0.55 + 0.45);
	wet *= 0.55 + 0.45 * max(n_world.y, 0.0);
	albedo *= 1.0 - wet * 0.10;

	float base_rough = mix(0.24, 0.34, broad);
	float rough = mix(base_rough, 0.48, chip);
	rough = mix(rough, 0.10 + broad * 0.05, wet * 0.82);

	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.48) * fire_intensity;

	ALBEDO = albedo;
	ROUGHNESS = clamp(rough, 0.07, 0.65);
	METALLIC = chip * 0.72;
	SPECULAR = mix(0.48, 0.56, wet);
	CLEARCOAT = (1.0 - chip) * (0.20 + wet * 0.62);
	CLEARCOAT_ROUGHNESS = mix(0.18, 0.08, wet);
	EMISSION = enamel * vec3(1.0, 0.36, 0.08) * warmth * 0.025;
}
