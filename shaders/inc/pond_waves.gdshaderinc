// Shared by the pond surface and its floating leaves and blossoms. The same
// world position and clock must yield the same waterline in every material.
uniform float wave_height = 0.012;
uniform float wave_length = 3.2;
global uniform float pond_time;
global uniform bool pond_interaction_enabled;
global uniform vec4 pond_interaction_bounds;
global uniform sampler2D pond_interaction_tex;

vec4 pond_interaction(vec2 p) {
	vec2 uv = (p - pond_interaction_bounds.xy) / pond_interaction_bounds.zw;
	if (!pond_interaction_enabled || any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {
		return vec4(0.0);
	}
	return textureLod(pond_interaction_tex, uv, 0.0);
}

vec3 pond_base_wave(vec2 p, float t, out vec3 n) {
	vec2 dir = normalize(wind_direction + vec2(1e-4));
	vec3 disp = vec3(0.0);
	n = vec3(0.0, 1.0, 0.0);
	for (int i = 0; i < 3; i++) {
		float fi = float(i);
		vec2 d = normalize(dir + vec2(sin(fi * 2.1), cos(fi * 1.7)) * 0.55);
		float k = 6.2831853 / (wave_length * (1.0 - fi * 0.28));
		float c = sqrt(9.8 / k);
		float a = wave_height * (1.0 - fi * 0.3) * (0.4 + 0.6 * clamp(wind_strength, 0.0, 1.5));
		float phase = k * (dot(d, p) - c * pond_time);
		disp += vec3(d.x * a * cos(phase) * 0.6, a * sin(phase), d.y * a * cos(phase) * 0.6);
		n.xz -= d * a * k * cos(phase);
	}
	n = normalize(n);
	return disp;
}

vec3 pond_wave(vec2 p, float t, out vec3 n) {
	vec3 disp = pond_base_wave(p, t, n);
	if (pond_interaction_enabled) {
		vec2 at = p + disp.xz;
		vec2 cell = pond_interaction_bounds.zw / vec2(textureSize(pond_interaction_tex, 0));
		disp.y += pond_interaction(at).r;
		vec2 gradient = vec2(pond_interaction(at + vec2(cell.x, 0.0)).r - pond_interaction(at - vec2(cell.x, 0.0)).r,
			pond_interaction(at + vec2(0.0, cell.y)).r - pond_interaction(at - vec2(0.0, cell.y)).r) / (2.0 * cell);
		n = normalize(vec3(n.x / max(n.y, 0.1) - gradient.x, 1.0, n.z / max(n.y, 0.1) - gradient.y));
	}
	return disp;
}

// Signed distance in Y to the displaced surface. The inverse step accounts
// for Gerstner's horizontal motion. Away from the wave envelope only the
// sign matters, so full-screen post passes avoid unnecessary trigonometry.
float pond_surface_offset(vec3 p, float level, float t) {
	float offset = p.y - level;
	float envelope = wave_height * 2.1 * (0.4 + 0.6 * clamp(wind_strength, 0.0, 1.5)) + 0.003 + (pond_interaction_enabled ? 0.055 : 0.0);
	if (abs(offset) > envelope) { return offset; }
	vec3 n;
	vec3 displacement = pond_base_wave(p.xz, t, n);
	return offset - pond_wave(p.xz - displacement.xz, t, n).y;
}

// Millimetre-scale capillary motion where water adheres to the lens. Both
// lens passes use this boundary so the wet edge and absorption stay aligned.
float pond_lens_offset(vec3 p, vec2 uv, float level, float t) {
	float offset = pond_surface_offset(p, level, t);
	float adhesion = sin(uv.x * 17.0 + t * 2.1) * 0.0007
		+ sin(uv.x * 43.0 - t * 3.7) * 0.0003;
	return offset + adhesion * exp(-abs(offset) * 90.0);
}
