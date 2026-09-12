shader_type sky;
render_mode use_debanding, use_half_res_pass;
uniform float cloud_time = 0.0;
uniform float storm_amount = 0.0;
global uniform float lightning_flash;
global uniform vec3 lightning_position;

// Physically based sky: single-scattering atmosphere, sun and moon discs,
// stars with a Milky Way band, and a ray-marched cloud layer with self-shadowing.

#include "res://shaders/inc/atmosphere.gdshaderinc"
#include "res://shaders/inc/noise.gdshaderinc"

group_uniforms Sun;
uniform vec3 sun_direction = vec3(0.0, 0.3, -1.0);
uniform float sun_intensity : hint_range(0.0, 60.0) = 22.0;
uniform float sun_disc_radius : hint_range(0.001, 0.05) = 0.0047;
uniform float sun_disc_intensity : hint_range(0.0, 200.0) = 40.0;
uniform float haze : hint_range(0.2, 6.0) = 1.6;
uniform float mie_g : hint_range(0.5, 0.95) = 0.78;
uniform float observer_altitude = 120.0;

group_uniforms Moon;
uniform vec3 moon_direction = vec3(0.0, -1.0, 0.0);
uniform float moon_disc_radius : hint_range(0.001, 0.05) = 0.0072;
uniform float moon_intensity : hint_range(0.0, 10.0) = 1.2;
uniform float moon_halo : hint_range(0.0, 1.0) = 0.25;

group_uniforms Night;
uniform float star_intensity : hint_range(0.0, 3.0) = 0.75;
uniform float milky_way_intensity : hint_range(0.0, 3.0) = 0.72;
uniform float night_sky_floor : hint_range(0.0, 0.05) = 0.0028;
uniform float star_rotation = 0.0;

group_uniforms Clouds;
uniform float cloud_coverage : hint_range(0.0, 1.0) = 0.42;
uniform float cloud_altitude = 1800.0;
uniform float cloud_scale : hint_range(0.1, 5.0) = 1.0;
uniform vec2 cloud_wind = vec2(0.6, 0.2);
uniform float cloud_density : hint_range(0.0, 4.0) = 1.6;
uniform float cloud_sharpness : hint_range(0.05, 0.6) = 0.22;
// 0 play/low, 1 default, 2 film: more march steps and a finer erosion octave
// so cumulus keep crisp, cauliflower edges instead of an airbrushed smear.
uniform int cloud_quality : hint_range(0, 2) = 1;

const float REL_TOP = 0.999;

vec3 observer() {
	return vec3(0.0, ATM_PLANET_RADIUS + observer_altitude, 0.0);
}

// Directions below the horizon are folded slightly upward so the ground does
// not read as a hard black hemisphere in reflections.
vec3 clamp_horizon(vec3 dir) {
	if (dir.y < 0.0) {
		dir.y = dir.y * 0.15;
		return normalize(dir);
	}
	return dir;
}

// ---------------------------------------------------------------- night sky

mat3 rotate_y(float a) {
	float c = cos(a);
	float s = sin(a);
	return mat3(vec3(c, 0.0, -s), vec3(0.0, 1.0, 0.0), vec3(s, 0.0, c));
}

float stars(vec3 dir) {
	// Tilt the celestial sphere so the pole sits ~50 degrees up, then rotate
	// with time of day.
	vec3 d = rotate_y(star_rotation) * dir;
	d = normalize(vec3(d.x, d.y * 0.77 + d.z * 0.64, -d.y * 0.64 + d.z * 0.77));
	// Cube-face projection keeps star density uniform across the sky.
	vec3 a = abs(d);
	vec2 uv;
	float face;
	if (a.x >= a.y && a.x >= a.z) {
		uv = d.yz / a.x;
		face = d.x > 0.0 ? 0.0 : 1.0;
	} else if (a.y >= a.z) {
		uv = d.xz / a.y;
		face = d.y > 0.0 ? 2.0 : 3.0;
	} else {
		uv = d.xy / a.z;
		face = d.z > 0.0 ? 4.0 : 5.0;
	}
	float total = 0.0;
	for (int layer = 0; layer < 2; layer++) {
		float cells = layer == 0 ? 90.0 : 220.0;
		vec2 g = uv * cells + face * 17.0;
		vec2 cell = floor(g);
		vec2 f = fract(g);
		vec3 h = hash33(vec3(cell, float(layer) * 3.7 + face));
		vec2 centre = 0.5 + (h.xy - 0.5) * 0.7;
		float dist = length(f - centre);
		float brightness = pow(h.z, layer == 0 ? 25.0 : 55.0);
		float size = layer == 0 ? 0.075 : 0.055;
		// Analytic footprint keeps sub-pixel stars present through TAA and
		// movie supersampling, instead of blinking between individual pixels.
		float footprint = max(fwidth(g.x), fwidth(g.y));
		float radius = max(size, footprint * 0.65);
		float star = exp(-dist * dist / max(radius * radius * 0.3, 0.001)) * brightness;
		star *= min(1.0, size * size / (radius * radius));
		// Colour temperature variation: bluish-white to warm.
		total += star * (layer == 0 ? 3.0 : 1.2);
	}
	return total;
}

vec3 star_tint(vec3 dir) {
	float t = hash13(floor(dir * 400.0));
	return mix(vec3(1.0, 0.85, 0.7), vec3(0.75, 0.85, 1.0), t);
}

float milky_way(vec3 dir) {
	vec3 d = rotate_y(star_rotation) * dir;
	// Band plane tilted from the celestial equator.
	vec3 band_normal = normalize(vec3(0.25, 0.55, 0.8));
	float dist = abs(dot(d, band_normal));
	// Position along the band is taken from a ring in noise space so the
	// pattern is continuous all the way round (no atan seam).
	float n = fbm3(d * 4.6 + vec3(1.7, 3.1, 4.8), 5);
	float n2 = fbm3(d * 19.5 + vec3(5.3, 1.2, 8.0), 4);
	float core = exp(-dist * dist * 75.0);
	float wide = exp(-dist * dist * 16.0) * 0.20;
	float lanes = smoothstep(0.35, 0.75, n) * 0.7 + 0.3;
	float dust = 1.0 - smoothstep(0.55, 0.8, n2) * 0.65 * core;
	return (core * lanes + wide) * dust;
}

// ----------------------------------------------------------------- clouds

vec2 cloud_uv(vec3 dir) {
	// Planar layer with a curvature term so clouds flatten at the horizon.
	float t = cloud_altitude / (dir.y + 0.12);
	return dir.xz * t * 0.00018 * cloud_scale;
}

float cloud_density_at(vec3 p, float time) {
	float h = (p.y - cloud_altitude) / mix(750.0, 2300.0, storm_amount);
	float profile = smoothstep(0.0, 0.18, h) * (1.0 - smoothstep(0.48, 1.0, h));
	vec3 q = p * vec3(0.00055, 0.0013, 0.00055) * cloud_scale;
	q.xz += cloud_wind * time * 0.003;
	float base = fbm3(q, 4);
	float erosion = vnoise3(q * 4.2 + vec3(4.7, 1.2, 3.9));
	float threshold = 0.73 - cloud_coverage * 0.56;
	float detail = erosion * 0.11 + vnoise3(q * 9.0) * 0.05;
	if (cloud_quality >= 2) {
		detail += (vnoise3(q * 21.0 + vec3(1.3, 7.1, 2.9)) - 0.5) * 0.05;
	}
	float ordinary = smoothstep(threshold, threshold + 0.16, base - detail) * profile;
	// One western thunderhead: broad rain base, rising lobes and an upper
	// anvil shoulder. Keep clear sky around its silhouette, not a flat ceiling.
	float anvil = smoothstep(0.52, 0.80, h);
	vec2 radius = mix(vec2(2600.0, 1900.0), vec2(4200.0, 2450.0), anvil);
	vec2 centre = vec2(-6300.0, -1400.0) + vec2(-700.0 * anvil, 0.0);
	float outline = length((p.xz - centre) / radius);
	float mass = 1.0 - smoothstep(0.66, 1.0, outline + (erosion - 0.5) * 0.15);
	float storm = mass * profile * smoothstep(0.28, 0.61, base - erosion * 0.045);
	// As the front reaches camp, its stratiform rain shield fills the sky
	// overhead. A distant anvil alone cannot rain above an otherwise clear lens.
	float shield = smoothstep(0.66, 0.95, storm_amount) * profile
		* (0.40 + 0.55 * base - erosion * 0.08);
	return mix(ordinary, max(shield, max(ordinary * 0.32, storm)), storm_amount);
}

vec3 shade_clouds(vec3 dir, vec3 sky_col, vec3 sun_col, vec3 zenith_col, vec3 horizon_col,
		float time, float flash_energy, out float alpha) {
	alpha = 0.0;
	if (dir.y <= 0.005 || cloud_coverage <= 0.001) {
		return sky_col;
	}
	float horizon_fade = smoothstep(0.0, 0.12, dir.y);
	float ray_y = max(dir.y, 0.055);
	float start = cloud_altitude / ray_y;
	int cloud_steps = (cloud_quality >= 2 ? 40 : (cloud_quality == 1 ? 20 : 12)) + (storm_amount > 0.05 ? 8 : 0);
	float step_len = mix(750.0, 2300.0, storm_amount) / ray_y / float(cloud_steps);
	float trans = 1.0;
	vec3 radiance = vec3(0.0);
	float forward = pow(clamp(dot(dir, sun_direction), 0.0, 1.0), 9.0);
	float sun_up = clamp(sun_direction.y * 6.0 + 0.35, 0.0, 1.0);
	for (int i = 0; i < 48; i++) {
		if (i >= cloud_steps) { break; }
		vec3 p = dir * (start + (float(i) + 0.5) * step_len);
		float d = cloud_density_at(p, time);
		if (d < 0.002) { continue; }
		float shadow_depth = cloud_density_at(p + sun_direction * 220.0, time) * 1.8;
		shadow_depth += cloud_density_at(p + sun_direction * 520.0, time) * 1.1;
		float shadow = exp(-shadow_depth * cloud_density * 2.2);
		float h = clamp((p.y - cloud_altitude) / mix(750.0, 2300.0, storm_amount), 0.0, 1.0);
		vec3 ambient = mix(horizon_col * 0.42, zenith_col * 1.25, h);
		vec3 direct = sun_col * (shadow * 0.8 + forward * shadow * 1.2) * sun_up;
		// Moon-lit lobes stay readable between flashes. A localized internal
		// pulse travels through cloud density, rather than washing the whole sky.
		float moon_edge = pow(max(dot(dir, moon_direction), 0.0), 5.0);
		float moon_shadow = exp(-cloud_density_at(p + moon_direction * 450.0, time) * 5.0);
		vec3 moon_lit = mix(vec3(0.010, 0.015, 0.024), vec3(0.032, 0.048, 0.082), storm_amount)
			* (0.20 + h * 0.7 + moon_edge * moon_shadow) * (1.0 - sun_up);
		vec3 flash_pos = lightning_position * ((cloud_altitude + 600.0) / max(lightning_position.y, 1.0));
		float flash_reach = exp(-length(p - flash_pos) / 2400.0);
		vec3 flash_lit = vec3(0.9, 1.0, 1.2) * flash_energy * flash_reach;
		vec3 lit = max(ambient + direct + moon_lit + flash_lit, vec3(0.002, 0.003, 0.005));
		float absorb = 1.0 - exp(-d * cloud_density * step_len * 0.007);
		radiance += trans * lit * absorb;
		trans *= 1.0 - absorb;
		if (trans < 0.02) { break; }
	}
	alpha = (1.0 - trans) * horizon_fade;
	float aerial = 1.0 - exp(-start * 0.000025 * haze);
	vec3 cloud_col = radiance / max(1.0 - trans, 0.001);
	cloud_col = mix(cloud_col, sky_col, aerial * 0.7);
	return mix(sky_col, cloud_col, alpha);
}

// -------------------------------------------------------------------- main

void sky() {
	if (AT_HALF_RES_PASS || AT_CUBEMAP_PASS) {
	vec3 dir = normalize(EYEDIR);
	vec3 view_dir = clamp_horizon(dir);
	vec3 origin = observer();
	vec3 sun = normalize(sun_direction);
	vec3 moon = normalize(moon_direction);

	int view_steps = AT_CUBEMAP_PASS ? 16 : 12;
	int light_steps = AT_CUBEMAP_PASS ? 8 : 6;

	vec3 transmittance;
	vec3 col = atm_scatter(origin, view_dir, sun, sun_intensity, haze, view_steps, light_steps, mie_g, transmittance);

	// Moonlight scatters too: a cool, faint second atmosphere pass.
	float moon_up = clamp(moon.y * 4.0, 0.0, 1.0);
	float sun_down = smoothstep(0.08, -0.12, sun.y);
	if (moon_up > 0.0 && sun_down > 0.0) {
		vec3 t2;
		vec3 moon_sky = atm_scatter(origin, view_dir, moon, sun_intensity * 0.0025 * moon_intensity, haze, 6, 3, mie_g, t2);
		col += moon_sky * vec3(0.8, 0.9, 1.15) * moon_up * sun_down;
	}
	// Faint airglow so the night sky never clips to black.
	col += vec3(0.55, 0.7, 1.0) * night_sky_floor * sun_down * (0.6 + 0.4 * clamp(1.0 - dir.y, 0.0, 1.0));

	// Ambient references for cloud shading: straight up and towards the sun.
	vec3 tz;
	vec3 th;
	vec3 zenith_col = atm_scatter(origin, vec3(0.0, 1.0, 0.0), sun, sun_intensity, haze, 4, 3, mie_g, tz);
	vec3 horizon_col = atm_scatter(origin, normalize(vec3(sun.x, 0.06, sun.z)), sun, sun_intensity, haze, 6, 3, mie_g, th);
	float cloud_alpha = 0.0;

	// Sun transmittance drives the disc and cloud lighting colour.
	vec3 sun_trans = atm_transmittance(origin, sun, 8, haze);
	vec3 sun_col = sun_trans * sun_intensity * 0.06;

	// Night sky, attenuated by daylight, the moon's glare and the atmosphere.
	float night = smoothstep(0.04, -0.14, sun.y);
	float sky_bright = clamp(dot(col, vec3(0.33)) * 8.0, 0.0, 1.0);
	float star_vis = night * (1.0 - sky_bright) * smoothstep(-0.02, 0.15, dir.y);
	if (star_vis > 0.001 && AT_CUBEMAP_PASS) {
		float s = stars(dir);
		float mw = milky_way(dir);
		vec3 extinction = transmittance;
		col += star_tint(dir) * s * star_intensity * 0.06 * star_vis * extinction;
		col += mix(vec3(0.7, 0.75, 0.95), vec3(0.95, 0.85, 0.8), mw) * mw * milky_way_intensity * 0.035 * star_vis * extinction;
	}

	// Moon disc with phase shading and a soft halo.
	float moon_cos = dot(dir, moon);
	float moon_cos_edge = cos(moon_disc_radius);
	if (moon.y > -0.1) {
		float disc = smoothstep(moon_cos_edge - 0.00006, moon_cos_edge + 0.00003, moon_cos);
		if (disc > 0.0) {
			// Reconstruct the sphere normal at this pixel.
			vec3 offset = dir - moon * moon_cos;
			float r = length(offset) / sin(moon_disc_radius);
			vec3 up_ref = abs(moon.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
			vec3 mx = normalize(cross(up_ref, moon));
			vec3 my = cross(moon, mx);
			vec2 disc_uv = vec2(dot(offset, mx), dot(offset, my)) / sin(moon_disc_radius);
			vec3 n = normalize(mx * disc_uv.x + my * disc_uv.y + moon * sqrt(max(1.0 - r * r, 0.0)));
			float lit = clamp(dot(n, sun) * 1.1 + 0.02, 0.0, 1.0);
			float mare = 0.62 + 0.38 * fbm2(n.xy * 4.0 + n.zx * 2.0, 4);
			float craters = 1.0 - 0.25 * smoothstep(0.55, 0.75, fbm2(n.yz * 11.0 + 3.0, 3));
			vec3 moon_col = vec3(1.0, 0.97, 0.9) * mare * craters * lit * moon_intensity * 1.6;
			col = mix(col, moon_col * transmittance, disc);
		}
		float halo = pow(clamp(moon_cos, 0.0, 1.0), 900.0) * 0.5 + pow(clamp(moon_cos, 0.0, 1.0), 120.0) * 0.06;
		col += vec3(0.85, 0.9, 1.0) * halo * moon_halo * moon_intensity * night * transmittance;
	}

	// A thin cirrus veil high above, then the cumulus layer in front of the
	// stars and moon.
	if (dir.y > 0.02) {
		vec2 cirrus_uv = dir.xz / (dir.y + 0.15) * 0.9 + vec2(cloud_time * 0.0015, cloud_time * 0.0006);
		float streak = fbm2(cirrus_uv * vec2(2.2, 0.55) + vec2(3.7, 9.1), 5);
		float veil = smoothstep(0.52, 0.8, streak) * smoothstep(0.02, 0.2, dir.y) * 0.35;
		vec3 cirrus_col = mix(horizon_col, zenith_col, 0.4) * 1.5 + sun_col * 0.4 * clamp(sun.y * 4.0 + 0.3, 0.0, 1.0);
		col = mix(col, cirrus_col, veil);
	}
	// Incremental radiance filtering spans several frames. The brief pulse
	// belongs in the visible sky (including planar reflections), not cached
	// ambient/specular lighting that would outlive the direct lightning.
	float cloud_flash = AT_CUBEMAP_PASS ? 0.0 : lightning_flash;
	col = shade_clouds(dir, col, sun_col, zenith_col, horizon_col, cloud_time, cloud_flash, cloud_alpha);

	// Sun disc with limb darkening, seen through the clouds.
	float sun_cos = dot(dir, sun);
	float edge = cos(sun_disc_radius);
	float disc = smoothstep(edge - 0.00004, edge + 0.00002, sun_cos);
	if (disc > 0.0 && sun.y > -0.03) {
		float rr = clamp((1.0 - sun_cos) / (1.0 - edge), 0.0, 1.0);
		float limb = 1.0 - 0.45 * rr * rr;
		col += sun_trans * sun_disc_intensity * limb * disc * (1.0 - cloud_alpha * 0.92);
	}
	// Corona / glare around the sun.
	float glare = pow(clamp(sun_cos, 0.0, 1.0), 1800.0) * 0.18 + pow(clamp(sun_cos, 0.0, 1.0), 180.0) * 0.006;
	col += sun_trans * sun_intensity * 0.5 * glare * (1.0 - cloud_alpha * 0.7) * clamp(sun.y * 20.0 + 0.6, 0.0, 1.0);

	COLOR = max(col, vec3(0.0));
	ALPHA = cloud_alpha;
	} else {
		// Expensive atmosphere and volumetric clouds share the half-res pass.
		// Stars stay at the final pixel resolution, including in the movie.
		vec3 dir = normalize(EYEDIR);
		float night = smoothstep(0.03, -0.17, sun_direction.y);
		float horizon = smoothstep(-0.01, 0.14, dir.y);
		float clear_sky = 1.0 - HALF_RES_COLOR.a * 0.98;
		float moon_glare = 1.0 - pow(max(dot(dir, moon_direction), 0.0), 18.0) * 0.80;
		vec3 col = HALF_RES_COLOR.rgb;
		float visibility = night * horizon * clear_sky * moon_glare;
		col += star_tint(dir) * stars(dir) * star_intensity * 0.42 * visibility;
		float mw = milky_way(dir);
		col += mix(vec3(0.32, 0.42, 0.68), vec3(0.80, 0.68, 0.52), mw) * mw * milky_way_intensity * 0.05 * visibility;
		COLOR = max(col, vec3(0.0));
	}
}
