shader_type spatial;
render_mode cull_disabled, diffuse_burley;
#include "res://shaders/inc/common.gdshaderinc"
#include "res://shaders/inc/noise.gdshaderinc"
global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform float scene_wetness;
global uniform float water_level;
uniform float habitat_clock = 0.0;
uniform float fade_start = 85.0;
uniform float fade_end = 140.0;
varying vec3 v_tint;
varying vec3 world_pos;
void vertex() {
	vec3 root = MODEL_MATRIX[3].xyz;
	v_tint = INSTANCE_CUSTOM.yzw;
	float phase = INSTANCE_CUSTOM.x;
	float lod = smoothstep(fade_start, fade_end, length(CAMERA_POSITION_WORLD - root));
	VERTEX *= 1.0 - smoothstep(phase * 0.7 + 0.2, phase * 0.7 + 0.3, lod);
	float h = VERTEX.y;
	vec2 wind = wind_field(root, wind_direction, wind_strength, habitat_clock);
	vec3 drift = vec3(wind.x, 0.0, wind.y) * h * h * (0.07 + 0.025 * sin(habitat_clock * 1.8 + phase * 6.28));
	VERTEX += (inverse(MODEL_MATRIX) * vec4(drift, 0.0)).xyz;
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.01)) { discard; }
	float wet = clamp(scene_wetness, 0.0, 1.0);
	ALBEDO = COLOR.rgb * v_tint * (1.0 - wet * 0.12);
	ROUGHNESS = mix(0.82, 0.63, wet);
	SPECULAR = 0.23;
	BACKLIGHT = vec3(0.14, 0.15, 0.09);
}
