// Finite water left on a lens after surfacing. Height gradients refract the
// scene inside beads and rivulets; there is no white circular outline.
// Visual/technical references: Rain on Glass by Gerardo LCDF (Godot Shaders),
// and Toadstorm's Rainy Glass breakdown. This finite draining field is original
// code, not a redistribution of those authors' shaders.
vec3 lens_seed(float i) {
	return fract(sin(vec3(i * 17.3 + 4.1, i * 31.1 + 1.8, i * 7.7 + 9.2)) * 43758.5453);
}

vec2 lens_water_height(vec2 uv, float age, float aspect) {
	float height = 0.0;
	float coverage = 0.0;
	float swept = 0.0;
	float drying = 1.0 - smoothstep(4.0, 8.0, age);
	for (int i = 0; i < 15; i++) {
		vec3 h = lens_seed(float(i));
		float radius = 0.007 + h.z * 0.012;
		float fall = max(age - h.z * 0.25, 0.0);
		float start_y = h.y * 0.85 - 0.12;
		float y = start_y + fall * (0.026 + h.z * 0.055) + fall * fall * 0.012;
		float x = 0.035 + h.x * 0.93 + sin(y * 13.0 + h.y * 31.0) * 0.002;
		vec2 q = (uv - vec2(x, y)) * vec2(aspect, 1.0) / radius;
		// Rounded lower head, narrower tail. Its shape stretches as it gains speed.
		q.x *= 1.0 + max(-q.y, 0.0) * 0.45;
		q.y /= 1.15 + min(age, 3.0) * 0.10;
		float r2 = dot(q, q);
		float bead = pow(max(0.0, 1.0 - r2), 1.5);
		float historical_x = 0.035 + h.x * 0.93 + sin(uv.y * 13.0 + h.y * 31.0) * 0.002;
		float trail_x = (uv.x - historical_x) * aspect / (radius * 0.16);
		float trail = exp(-trail_x * trail_x) * smoothstep(start_y, start_y + 0.07, uv.y)
			* (1.0 - smoothstep(y - radius * 0.8, y, uv.y));
		swept = max(swept, trail * 2.0);
		trail *= exp(-max(y - uv.y, 0.0) * 9.0) * 0.18;
		height += (bead + trail) * radius * drying;
		coverage = max(coverage, (1.0 - smoothstep(0.65, 1.0, r2) + trail) * drying);
	}
	// Small pinned beads remain between the draining runs, then evaporate.
	vec2 grid = uv * vec2(aspect, 1.0) * 24.0;
	vec2 cell = floor(grid);
	vec3 h = lens_seed(cell.x * 73.0 + cell.y * 19.0 + 200.0);
	vec2 q = (fract(grid) - (0.2 + h.xy * 0.6)) / (0.045 + h.z * 0.075);
	float bead = pow(max(0.0, 1.0 - dot(q, q)), 1.4) * step(0.56, h.z);
	bead *= 1.0 - clamp(swept, 0.0, 1.0);
	height += bead * 0.002 * drying;
	coverage = max(coverage, bead * drying);
	return vec2(height, clamp(coverage, 0.0, 1.0));
}
