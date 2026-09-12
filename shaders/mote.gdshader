shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled;

// Dust motes and fireflies: a tiny soft core that catches whatever COLOR
// the particle (or instance) gives it.

uniform float glow : hint_range(0.2, 8.0) = 1.8;
global uniform float daylight;
global uniform float rainfall;
uniform mat4 tent_inverse;
varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 shelter = (tent_inverse * vec4(world_pos, 1.0)).xyz;
	float roof = 1.6 * (1.0 - abs(shelter.x) / 1.2);
	if (abs(shelter.x) < 1.2 && abs(shelter.z) < 1.5 && shelter.y < roof + 0.025) { discard; }
	float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	float core = pow(facing, 5.0);
	vec3 col = COLOR.rgb * (0.35 + 1.8 * core);
	ALBEDO = (1.0 - smoothstep(0.10, 0.65, rainfall)) * col * COLOR.a * glow * (1.0 - smoothstep(0.25, 0.80, daylight));
	ALPHA = 1.0;
}
