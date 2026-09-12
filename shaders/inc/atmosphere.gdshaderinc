// Single-scattering atmosphere (Rayleigh + Mie + ozone absorption).
// Units are metres. The observer sits on a spherical planet; rays are marched
// through the shell and the light-path optical depth is integrated per sample.
// Mirrored in scripts/world/atmosphere.gd so the sun light matches the sky.

const float ATM_PLANET_RADIUS = 6360000.0;
const float ATM_TOP_RADIUS = 6420000.0;
const vec3 ATM_RAYLEIGH_BETA = vec3(5.802e-6, 13.558e-6, 33.1e-6);
const float ATM_RAYLEIGH_H = 8000.0;
const float ATM_MIE_BETA = 3.996e-6;
const float ATM_MIE_ABSORB = 4.4e-6;
const float ATM_MIE_H = 1200.0;
const vec3 ATM_OZONE_BETA = vec3(0.650e-6, 1.881e-6, 0.085e-6);
const float ATM_OZONE_CENTRE = 25000.0;
const float ATM_OZONE_WIDTH = 15000.0;

// Returns (near, far) distances along the ray to the sphere of `radius`, or
// negative far if there is no hit.
vec2 atm_ray_sphere(vec3 origin, vec3 dir, float radius) {
	float b = dot(origin, dir);
	float c = dot(origin, origin) - radius * radius;
	float disc = b * b - c;
	if (disc < 0.0) {
		return vec2(1e9, -1e9);
	}
	float s = sqrt(disc);
	return vec2(-b - s, -b + s);
}

// Rayleigh, Mie and ozone densities at a world position.
vec3 atm_densities(vec3 p) {
	float h = max(length(p) - ATM_PLANET_RADIUS, 0.0);
	float r = exp(-h / ATM_RAYLEIGH_H);
	float m = exp(-h / ATM_MIE_H);
	float o = max(0.0, 1.0 - abs(h - ATM_OZONE_CENTRE) / ATM_OZONE_WIDTH) * r;
	return vec3(r, m, o);
}

vec3 atm_extinction(vec3 densities, float haze) {
	return ATM_RAYLEIGH_BETA * densities.x
			+ (ATM_MIE_BETA + ATM_MIE_ABSORB) * haze * densities.y
			+ ATM_OZONE_BETA * densities.z;
}

// Optical depth from `p` towards `dir` to the top of the atmosphere.
vec3 atm_optical_depth(vec3 p, vec3 dir, int steps, float haze) {
	vec2 hit = atm_ray_sphere(p, dir, ATM_TOP_RADIUS);
	float len = max(hit.y, 0.0);
	// Rays that enter the ground are fully occluded.
	vec2 ground = atm_ray_sphere(p, dir, ATM_PLANET_RADIUS);
	if (ground.y > 0.0 && ground.x > 0.0) {
		return vec3(1e6);
	}
	float ds = len / float(steps);
	vec3 depth = vec3(0.0);
	for (int i = 0; i < steps; i++) {
		vec3 s = p + dir * (float(i) + 0.5) * ds;
		depth += atm_extinction(atm_densities(s), haze) * ds;
	}
	return depth;
}

vec3 atm_transmittance(vec3 p, vec3 dir, int steps, float haze) {
	return exp(-atm_optical_depth(p, dir, steps, haze));
}

float atm_rayleigh_phase(float mu) {
	return 3.0 / (16.0 * PI) * (1.0 + mu * mu);
}

float atm_mie_phase(float mu, float g) {
	float g2 = g * g;
	float denom = 1.0 + g2 - 2.0 * g * mu;
	return 3.0 / (8.0 * PI) * (1.0 - g2) * (1.0 + mu * mu) / ((2.0 + g2) * pow(max(denom, 1e-4), 1.5));
}

// In-scattered radiance along `dir` from `origin` for a sun of unit radiance
// in direction `sun`. `transmittance_out` receives the view-ray transmittance.
vec3 atm_scatter(vec3 origin, vec3 dir, vec3 sun, float sun_intensity, float haze,
		int view_steps, int light_steps, float mie_g, out vec3 transmittance_out) {
	vec2 hit = atm_ray_sphere(origin, dir, ATM_TOP_RADIUS);
	vec2 ground = atm_ray_sphere(origin, dir, ATM_PLANET_RADIUS);
	float len = hit.y;
	if (ground.x > 0.0) {
		len = min(len, ground.x);
	}
	len = max(len, 0.0);
	float ds = len / float(view_steps);
	float mu = dot(dir, sun);
	float phase_r = atm_rayleigh_phase(mu);
	float phase_m = atm_mie_phase(mu, mie_g);

	vec3 sum_r = vec3(0.0);
	vec3 sum_m = vec3(0.0);
	vec3 depth = vec3(0.0);
	for (int i = 0; i < view_steps; i++) {
		vec3 p = origin + dir * (float(i) + 0.5) * ds;
		vec3 dens = atm_densities(p);
		vec3 ext = atm_extinction(dens, haze);
		depth += ext * ds;
		vec3 light_depth = atm_optical_depth(p, sun, light_steps, haze);
		vec3 attenuation = exp(-(depth + light_depth));
		sum_r += attenuation * dens.x * ds;
		sum_m += attenuation * dens.y * ds;
	}
	transmittance_out = exp(-depth);
	return sun_intensity * (sum_r * ATM_RAYLEIGH_BETA * phase_r + sum_m * ATM_MIE_BETA * haze * phase_m);
}
