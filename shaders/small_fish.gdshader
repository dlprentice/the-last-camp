shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/inc/common.gdshaderinc"
global uniform float water_level;
uniform float swim_clock = 0.0;
instance uniform float swim_phase = 0.0;
instance uniform float body_variant = 0.0;
varying vec3 world_pos;
varying vec3 body_pos;
void vertex() {
	body_pos = VERTEX;
	float u = clamp((VERTEX.z + 0.025) / 0.140, 0.0, 1.0);
	float envelope = u * u * (3.0 - 2.0 * u);
	float envelope_derivative = 6.0 * u * (1.0 - u) / 0.140;
	float phase = swim_clock * 10.0 - VERTEX.z * 40.0 + swim_phase;
	float bend = sin(phase) * envelope * 0.008;
	float slope = (cos(phase) * -40.0 * envelope + sin(phase) * envelope_derivative) * 0.008;
	VERTEX.x += bend;
	// Inverse transpose of the bending shear keeps highlights continuous
	// while the peduncle and fins move; the head and eyes barely move.
	NORMAL = normalize(vec3(NORMAL.xy, NORMAL.z - slope * NORMAL.x));
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) { discard; }
	float body = smoothstep(0.7, 0.95, COLOR.a);
	float eye = 1.0 - smoothstep(0.05, 0.2, COLOR.a);
	vec3 tone = mix(vec3(0.31, 0.33, 0.24), vec3(0.065, 0.105, 0.045), smoothstep(-0.022, 0.025, body_pos.y));
	// A narrow lateral line and curved operculum, restrained at actual scale.
	float lateral_delta = (body_pos.y - 0.001) / 0.0022;
	float lateral = exp(-lateral_delta * lateral_delta)
		* smoothstep(-0.059, -0.042, body_pos.z) * (1.0 - smoothstep(0.048, 0.070, body_pos.z));
	float gill_y = body_pos.y / 0.025;
	float gill_z = -0.051 + gill_y * gill_y * 0.009;
	float gill_delta = (body_pos.z - gill_z) / 0.0012;
	float gill = exp(-gill_delta * gill_delta)
		* (1.0 - smoothstep(0.018, 0.026, abs(body_pos.y)));
	float bars = smoothstep(0.35, 0.85, sin(body_pos.z * 225.0 + body_pos.y * 25.0));
	if (body_variant > 1.5) {
		tone *= vec3(0.78, 0.92, 1.16);
	} else if (body_variant > 0.5) {
		tone *= vec3(1.08, 0.93, 0.69) * (1.0 - bars * body * 0.22);
	}
	tone *= 1.0 - (lateral * 0.20 + gill * 0.40) * body;
	float rays = 0.92 + 0.08 * cos(UV.x * 75.398);
	vec3 fin_tone = vec3(0.24, 0.27, 0.16) * rays;
	ALBEDO = mix(mix(fin_tone, tone, body) * COLOR.rgb, COLOR.rgb, eye);
	ROUGHNESS = mix(0.40, 0.23, eye);
	SPECULAR = mix(0.42, 0.52, eye);
}
