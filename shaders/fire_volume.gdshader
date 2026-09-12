shader_type spatial;
render_mode blend_premul_alpha, depth_draw_never, depth_test_disabled, cull_front, unshaded, shadows_disabled, fog_disabled;

// Volumetric campfire. The flames are a ray-marched density field inside this
// box: a tapering column rising from the ember bed, torn by scrolling 3D noise
// so it has real internal structure and tongues that pinch off at the top,
// instead of overlapping billboards. Colour follows a black-body ramp with
// the hottest core low and central; the march stops at the scene depth so
// the pot, the tripod and the logs occlude the fire properly.

#include "res://shaders/inc/noise.gdshaderinc"

global uniform float fire_intensity;
global uniform vec2 wind_direction;
global uniform float wind_strength;

uniform sampler3D noise_tex : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform vec3 box_half = vec3(0.55, 0.65, 0.55);
uniform int steps : hint_range(8, 64) = 40;
uniform float radius : hint_range(0.05, 1.0) = 0.46;
uniform float height : hint_range(0.2, 2.0) = 0.8;
uniform float density : hint_range(0.5, 8.0) = 3.0;
uniform float extinction : hint_range(0.1, 20.0) = 4.2;
uniform float brightness : hint_range(0.0, 12.0) = 1.35;
uniform float rise_speed : hint_range(0.0, 4.0) = 1.2;
uniform float noise_scale : hint_range(0.5, 6.0) = 4.3;
uniform float flare : hint_range(0.0, 2.0) = 1.0;
uniform float wind_lean : hint_range(0.0, 1.0) = 0.3;
// 1 shows the noise field on the box surface, 2 the density envelope.
uniform int debug_mode = 0;

varying vec3 v_local;
varying vec3 v_cam_local;
varying float v_model_scale;

void vertex() {
	v_local = VERTEX;
	v_cam_local = (inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz;
	v_model_scale = length(MODEL_MATRIX[1].xyz);
}

// Black-body-ish ramp: dark red -> orange -> yellow. It stops short of white:
// the tonemapper already bleaches the brightest core, and a ramp that went
// white on its own turned the whole column into pale luminous fog.
vec3 fire_ramp(float t) {
	vec3 a = vec3(0.45, 0.03, 0.0);
	vec3 b = vec3(1.0, 0.22, 0.02);
	vec3 c = vec3(1.0, 0.52, 0.07);
	vec3 d = vec3(1.0, 0.76, 0.26);
	if (t < 0.33) {
		return mix(a, b, t / 0.33);
	} else if (t < 0.66) {
		return mix(b, c, (t - 0.33) / 0.33);
	}
	return mix(c, d, (t - 0.66) / 0.34);
}

void fragment() {
	if (debug_mode == 1) {
		float n = texture(noise_tex, v_local * noise_scale * 0.5).r;
		ALBEDO = vec3(n);
		ALPHA = 1.0;
	} else if (debug_mode == 8) {
		// Uniform readout: red = box_half.y / 2, green = height / 2, blue = v_local.y + 0.5.
		ALBEDO = vec3(box_half.y * 0.5, height * 0.5, v_local.y + 0.5);
		ALPHA = 1.0;
	} else if (debug_mode == 2) {
		float h = (v_local.y + box_half.y) / height;
		ALBEDO = vec3(clamp(h, 0.0, 1.0), 1.0 - length(v_local.xz) / radius, 0.0);
		ALPHA = 1.0;
	} else {
	vec3 ro = v_cam_local;
	vec3 rd = normalize(v_local - ro);
	vec3 safe_rd = vec3(
			abs(rd.x) < 1e-5 ? 1e-5 : rd.x,
			abs(rd.y) < 1e-5 ? 1e-5 : rd.y,
			abs(rd.z) < 1e-5 ? 1e-5 : rd.z);
	vec3 inv = 1.0 / safe_rd;
	vec3 t0 = (-box_half - ro) * inv;
	vec3 t1 = (box_half - ro) * inv;
	vec3 tmin = min(t0, t1);
	vec3 tmax = max(t0, t1);
	float t_enter = max(max(max(tmin.x, tmin.y), tmin.z), 0.0);
	float t_exit = min(min(tmax.x, tmax.y), tmax.z);

	// The proxy's exit face may lie behind a log. Hardware depth testing is
	// disabled so we can integrate the fire IN FRONT of that log; this sampled
	// opaque depth, rather than the box surface, provides correct occlusion.
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	vec4 view = INV_PROJECTION_MATRIX * ndc;
	view.xyz /= view.w;
	if (debug_mode != 3) {
		t_exit = min(t_exit, length(view.xyz) / max(v_model_scale, 1e-4));
	}
	if (t_exit <= t_enter) {
		discard;
	}

	float jitter = hash12(FRAGCOORD.xy + vec2(fract(TIME) * 61.0, 0.0));
	float dt = (t_exit - t_enter) / float(steps);
	float t = t_enter + dt * jitter;
	vec2 lean = normalize(wind_direction + vec2(1e-4)) * wind_strength * wind_lean;
	float glow = brightness * (0.86 + 0.14 * clamp(fire_intensity, 0.0, 2.0)) * (0.9 + 0.12 * flare);
	vec3 col = vec3(0.0);
	float transmittance = 1.0;
	float n_max = 0.0;
	float d_sum = 0.0;
	for (int i = 0; i < 64; i++) {
		if (i >= steps || transmittance < 0.02) {
			break;
		}
		vec3 p = ro + rd * t;
		float h = (p.y + box_half.y) / height;
		if (h > 0.0 && h < 1.15) {
			vec2 xz = p.xz - lean * h * h * 0.5;
			float r = radius * (1.0 - 0.72 * h) * (0.85 + 0.3 * flare);
			float angle = atan(xz.y, xz.x);
			float lobes = sin(angle * 3.0 + TIME * 1.3) * 0.12 + sin(angle * 5.0 - TIME * 0.9) * 0.08;
			r *= 1.0 + lobes * (1.0 - smoothstep(0.20, 0.55, h));
			float radial = length(xz) / max(r, 1e-3);
			vec3 np = vec3(xz * noise_scale, (p.y - TIME * rise_speed) * noise_scale * 0.7);
			float n1 = texture(noise_tex, np * 0.5).r;
			float n2 = texture(noise_tex, np * 1.3 + vec3(0.37, TIME * 0.05, 0.11)).r;
			float n = n1 * 0.65 + n2 * 0.35;
			if (debug_mode == 4) {
				n = 0.4;
			}
			n_max = max(n_max, n);
			// The noise texture averages about 0.4: centre it there, then let it
			// carve the column harder with height so the top tears into tongues.
			float shape = 1.0 - radial;
			// The noise carves harder, and harder still with height, so the
			// column separates into tongues with dark gaps between them.
			float d = shape + (n - 0.4) * (1.6 + 1.9 * h) - h * h * 0.7;
			// Keep internal noise instead of clamping most of the core to one
			// uniform density; that plateau made the fire read as luminous fog.
			// A steep threshold and high extinction give the tongues crisp edges
			// and dark gaps instead of one translucent glow.
			d = clamp(smoothstep(0.22, 0.80, d) * density / 3.0, 0.0, 1.0) * smoothstep(0.0, 0.06, h) * (1.0 - smoothstep(0.75, 1.15, h));
			d_sum += d;
			if (d > 0.001) {
				// Hottest low and central; the tips cool to orange and red.
				float heat = clamp(d * 0.95 * (1.2 - h * 1.15) * (1.1 - radial * 0.6), 0.0, 1.0);
				float a = 1.0 - exp(-d * dt * extinction);
				col += fire_ramp(heat) * glow * a * transmittance * (0.22 + 0.78 * heat);
				if (debug_mode != 7) {
					transmittance *= 1.0 - a;
				}
			}
		}
		t += dt;
	}
	ALBEDO = col;
	ALPHA = 1.0 - transmittance;
	if (debug_mode == 5) {
		ALBEDO = vec3(n_max, 0.0, 1.0 - n_max);
		ALPHA = 1.0;
	} else if (debug_mode == 6) {
		ALBEDO = vec3(d_sum * 0.15, d_sum * 0.05, 0.0);
		ALPHA = 1.0;
	}
	}
}
