shader_type spatial;
render_mode cull_disabled, diffuse_burley;
#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"
global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform float scene_wetness;
uniform float fade_start = 80.0;
uniform float fade_end = 130.0;
varying vec3 v_tint;
varying vec3 v_world_normal;
void vertex() {
	vec3 root = MODEL_MATRIX[3].xyz;
	float phase = INSTANCE_CUSTOM.x;
	v_tint = INSTANCE_CUSTOM.yzw;
	float fade = smoothstep(fade_start, fade_end, length(CAMERA_POSITION_WORLD - root));
	float keep = smoothstep(fade - 0.06, fade + 0.06, phase);
	VERTEX *= keep;
	vec3 ax = MODEL_MATRIX[0].xyz;
	vec3 ay = MODEL_MATRIX[1].xyz;
	vec3 az = MODEL_MATRIX[2].xyz;
	float h = VERTEX.y * length(ay);
	vec2 wind = wind_field(root, wind_direction, wind_strength, TIME);
	vec2 sway = wind * 0.14 + vec2(sin(TIME * 2.1 + phase * 6.283),
		cos(TIME * 1.8 + phase * 8.93)) * 0.025 * wind_strength;
	vec3 offset = vec3(sway.x, 0.0, sway.y) * h * h;
	// All meadow instances are upright with independent axis scales. Apply
	// wind in world space so rotating a clump cannot rotate the wind field.
	VERTEX += vec3(dot(offset, ax) / dot(ax, ax),
		dot(offset, ay) / dot(ay, ay), dot(offset, az) / dot(az, az));
	// Cofactor of the same height-squared displacement preserves the actual
	// stem, leaf and spikelet normals as they bend under the shared breeze.
	vec3 n = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	vec2 slope = sway * (2.0 * h);
	v_world_normal = normalize(vec3(n.x, n.y - dot(n.xz, slope), n.z));
}
void fragment() {
	float wet = clamp(scene_wetness, 0.0, 1.0);
	ALBEDO = COLOR.rgb * v_tint * (1.0 - wet * 0.06);
	NORMAL = view_normal_from_world(VIEW_MATRIX,
		normalize(v_world_normal) * (FRONT_FACING ? 1.0 : -1.0));
	ROUGHNESS = mix(0.76, 0.57, wet);
	SPECULAR = 0.22;
	// Godot applies albedo to this term. A neutral transmission tint avoids
	// multiplying dark green stems by their colour a second time.
	BACKLIGHT = vec3(1.0, 0.97, 0.85) * 0.16;
}
