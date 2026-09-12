shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, shadows_disabled;

#include "res://shaders/inc/common.gdshaderinc"
global uniform float water_level;
uniform mat4 tent_inverse;
uniform mat4 dock_inverse;
uniform float dock_length = 8.9;
varying vec3 world_pos;

// Sparse post-rain droplets shed from procedural crown samples. Instance
// custom: R = normalized fall height, G = phase/density seed, B = speed seed,
// A = size seed. Animation uses a pause-aware clock supplied by GDScript.
global uniform vec2 wind_direction;
global uniform float wind_strength;

uniform float drip_clock = 0.0;
uniform float drip_strength : hint_range(0.0, 1.0) = 0.0;
uniform float density_scale : hint_range(0.0, 1.0) = 1.0;
uniform float fall_range = 32.0;

varying float v_alpha;
varying float v_tip;

void vertex() {
	vec3 source = MODEL_MATRIX[3].xyz;
	float fall_height = max(INSTANCE_CUSTOM.r * fall_range, 0.35);
	float speed = mix(0.82, 1.34, INSTANCE_CUSTOM.b);
	float gravity = mix(7.2, 10.6, INSTANCE_CUSTOM.b);
	float fall_time = sqrt(2.0 * fall_height / gravity);
	// Long, irregular rest gaps are the distinction from active rainfall.
	float rest_time = mix(1.2, 6.8, INSTANCE_CUSTOM.g * INSTANCE_CUSTOM.g);
	float cycle = fall_time + rest_time;
	float local_time = mod(drip_clock * speed + INSTANCE_CUSTOM.g * cycle * 9.73, cycle);
	float falling = 1.0 - step(fall_time, local_time);
	float t = min(local_time, fall_time);
	float distance = min(0.5 * gravity * t * t, fall_height);
	vec3 centre = source - vec3(0.0, distance, 0.0);
	// A very small wind carry keeps drops coherent with the moving canopy while
	// preserving an almost vertical gravitational fall.
	centre.xz += normalize(wind_direction + vec2(1e-4)) * wind_strength * distance * 0.018;

	float density_gate = 1.0 - step(density_scale, INSTANCE_CUSTOM.g);
	float start_fade = smoothstep(0.0, min(0.11, fall_time * 0.20), local_time);
	float end_fade = 1.0 - smoothstep(max(fall_time - 0.10, 0.0), fall_time, local_time);
	v_alpha = falling * density_gate * start_fade * end_fade * drip_strength;

	float size = mix(0.006, 0.013, INSTANCE_CUSTOM.a);
	float stretch = mix(0.040, 0.095, INSTANCE_CUSTOM.b) * mix(0.55, 1.0, clamp(distance / 3.0, 0.0, 1.0));
	vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 fwd = cross(right, up);
	if (length(fwd) < 0.01) {
		fwd = normalize(INV_VIEW_MATRIX[2].xyz);
		right = normalize(cross(up, fwd));
	}
	fwd = normalize(fwd);
	world_pos = centre + right * VERTEX.x * size + up * VERTEX.y * stretch;
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
		vec4(right * size, 0.0),
		vec4(up * stretch, 0.0),
		vec4(fwd * size, 0.0),
		vec4(centre, 1.0));
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	v_tip = UV.y;
}

void fragment() {
	if (v_alpha <= 0.001 || reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) { discard; }
	vec3 tent = (tent_inverse * vec4(world_pos, 1.0)).xyz;
	float roof = 1.6 * (1.0 - abs(tent.x) / 1.2);
	if (abs(tent.x) < 1.2 && abs(tent.z) < 1.5 && tent.y < roof + 0.025) { discard; }
	vec3 dock = (dock_inverse * vec4(world_pos, 1.0)).xyz;
	if (abs(dock.x) < 0.8 && dock.z < 0.0 && dock.z > -dock_length && dock.y < 0.12) { discard; }
	vec2 p = UV - vec2(0.5);
	float side = 1.0 - smoothstep(0.22, 0.49, abs(p.x) * 2.0);
	float end_shape = sin(clamp(UV.y, 0.0, 1.0) * 3.14159265);
	float core = pow(max(side * end_shape, 0.0), 0.65);
	ALBEDO = vec3(0.56, 0.68, 0.76) * (0.72 + core * 0.28);
	EMISSION = vec3(0.015, 0.022, 0.028) * core;
	ALPHA = core * v_alpha * 0.72;
}
