shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled;

// Fireflies: a soft core like the dust motes, but each one flashes on its own
// rhythm (a slow rise, a short hold, a quick fade every few seconds) and
// glows a little between flashes so it can be followed across the meadow.
//   INSTANCE_CUSTOM.y  lifetime phase (0 birth .. 1 death)

uniform float glow : hint_range(0.2, 12.0) = 6.0;
uniform float lifetime = 11.0;
global uniform float daylight;
global uniform float rainfall;
uniform mat4 tent_inverse;

varying float v_blink;
varying vec3 world_pos;

// Hash without Sine: Copyright (c) 2014 David Hoskins, MIT.
// See THIRD_PARTY_NOTICES.md and https://www.shadertoy.com/view/4djSRW.
float hash11(float p) {
	p = fract(p * 0.1031);
	p *= p + 33.33;
	return fract((p + p) * p);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float id = float(INSTANCE_ID);
	float r0 = hash11(id + 0.17);
	float r1 = hash11(id + 3.71);
	float period = 2.4 + r0 * 3.4;
	float t = INSTANCE_CUSTOM.y * lifetime + r1 * period;
	float f = fract(t / period);
	float flash = smoothstep(0.0, 0.12, f) * (1.0 - smoothstep(0.2, 0.34, f));
	float alive = smoothstep(0.0, 0.08, INSTANCE_CUSTOM.y) * (1.0 - smoothstep(0.88, 1.0, INSTANCE_CUSTOM.y));
	v_blink = (0.08 + 0.92 * flash) * alive;
}

void fragment() {
	vec3 shelter = (tent_inverse * vec4(world_pos, 1.0)).xyz;
	float roof = 1.6 * (1.0 - abs(shelter.x) / 1.2);
	if (abs(shelter.x) < 1.2 && abs(shelter.z) < 1.5 && shelter.y < roof + 0.025) { discard; }
	float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	float core = pow(facing, 5.0);
	vec3 col = COLOR.rgb * (0.35 + 1.8 * core);
	// Living particles also go dark at dawn; stopping emission alone leaves
	// the last eleven seconds of flashes hanging in the sunlit meadow.
	ALBEDO = (1.0 - smoothstep(0.10, 0.65, rainfall)) * col * COLOR.a * glow * v_blink * (1.0 - smoothstep(0.12, 0.48, daylight));
	ALPHA = 1.0;
}
