shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// Tent canvas: UV-mapped weave, double sided, translucent so a lantern inside
// glows through after dark, damp and darker where it meets the ground.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 fire_position;
global uniform float fire_intensity;
global uniform vec2 wind_direction;
global uniform float wind_strength;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : hint_default_white, repeat_enable;
uniform vec3 tint : source_color = vec3(0.98, 0.9, 0.66);
uniform float translucency : hint_range(0.0, 1.5) = 0.38;
uniform float ground_darkening : hint_range(0.0, 1.0) = 0.3;
uniform float normal_strength : hint_range(0.0, 2.0) = 0.24;
uniform float stain_contrast : hint_range(0.0, 1.0) = 0.20;
// Sewn construction: panels of this width along the ridge, a darker seam
// with a dashed stitch line, a slightly different tone per panel, sun-faded
// upper cloth and mildew freckles near the ground line.
uniform float panel_width = 0.75;
uniform float canvas_tile = 0.30;
uniform float seam_strength : hint_range(0.0, 1.0) = 0.6;

varying vec3 world_pos;
varying float v_height;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float gust = sin(dot(world_pos.xz, wind_direction) * 3.0 - TIME * 2.1) + 0.35 * sin(world_pos.z * 7.0 + TIME * 3.7);
	VERTEX += NORMAL * gust * wind_strength * COLOR.g * 0.006;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_height = COLOR.r;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) {
		discard;
	}
	vec3 albedo = texture(albedo_tex, UV).rgb;
	// The baked stains are strong; soften them towards the cloth's mean tone.
	albedo = mix(vec3(0.62, 0.58, 0.46), albedo, stain_contrast) * tint;
	vec3 orm = texture(orm_tex, UV).rgb;
	float stain = texture(noise_tex, world_pos.xz * 0.6 + world_pos.y * 0.4).g;
	albedo *= 0.95 + 0.2 * stain;
	// Panels are cut across the ridge; UV.x runs along it in tile units.
	float along = UV.x * canvas_tile;
	float panel_id = floor(along / panel_width);
	float seam_d = abs(fract(along / panel_width + 0.5) - 0.5) * panel_width;
	float seam = 1.0 - smoothstep(0.004, 0.018, seam_d);
	float stitch = seam * smoothstep(0.1, 0.5, sin(UV.y * canvas_tile * 220.0));
	float fold = 1.0 - smoothstep(0.02, 0.09, seam_d);
	albedo *= 1.0 - seam_strength * (seam * 0.10 + stitch * 0.07 + fold * 0.03);
	float panel_tone = fract(sin(panel_id * 12.9898 + 4.1) * 43758.5453);
	albedo *= 0.95 + 0.09 * panel_tone;
	// Years of sun bleach the upper cloth; the shaded lower cloth keeps its dye.
	albedo = mix(albedo, albedo * vec3(1.06, 1.04, 1.0), smoothstep(0.35, 0.95, v_height) * 0.5);
	// Mud and damp creep up from the ground line, with mildew freckles.
	float low = 1.0 - smoothstep(0.0, 0.35, v_height);
	float mildew = smoothstep(0.62, 0.78, texture(noise_tex, world_pos.xz * 9.0 + world_pos.y * 6.0).r) * low;
	albedo = mix(albedo, albedo * vec3(0.55, 0.5, 0.42), low * ground_darkening);
	albedo = mix(albedo, albedo * vec3(0.42, 0.40, 0.34), mildew * 0.6);
	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.35) * fire_intensity * 0.3;

	// Water first soaks into the fibres; a broken film then catches light
	// along the sloping panels. Keep the weave, rather than polishing it away.
	float soaked = scene_wetness * (0.80 + 0.20 * stain);
	float runoff = smoothstep(0.42, 0.74, texture(noise_tex,
		world_pos.xz * 4.0 + vec2(world_pos.y * 0.07)).b);
	// Rain beads on the outer face. The sheltered inner face can be damp,
	// but must retain a fibrous response instead of reflecting the lantern
	// as two polished white patches across the ceiling.
	float exterior = FRONT_FACING ? 1.0 : 0.0;
	float film = smoothstep(0.35, 0.95, scene_wetness) * (0.30 + runoff * 0.70) * exterior;
	ALBEDO = albedo * (1.0 - soaked * 0.32);
	ROUGHNESS = mix(mix(orm.g, 0.98, 0.3), mix(0.84, 0.39, exterior), soaked) + seam * 0.05;
	SPECULAR = mix(0.25, mix(0.20, 0.38, exterior), soaked);
	CLEARCOAT = film * 0.45;
	CLEARCOAT_ROUGHNESS = 0.22;
	AO = orm.r;
	// Forward+ already orients two-sided normals before fragment().
	NORMAL_MAP = texture(normal_tex, UV).rgb;
	NORMAL_MAP_DEPTH = normal_strength * (1.0 - film * 0.25);
	BACKLIGHT = albedo * translucency;
	EMISSION = albedo * vec3(1.0, 0.42, 0.12) * warmth * 0.1;
}
