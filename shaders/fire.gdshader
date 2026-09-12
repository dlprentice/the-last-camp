shader_type spatial;
render_mode blend_premul_alpha, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled;

// Flame card: a camera-facing quad carrying a candle-flame silhouette of
// scrolling turbulence that erodes as the particle ages, coloured along a
// black-body ramp with an HDR core for the bloom. Premultiplied so the hot
// core covers the background while the edges add light to it. Two layers use
// this shader: the dense core (default) and thin outer tongues (lick > 0).
//   INSTANCE_CUSTOM.y  lifetime phase (0 birth .. 1 death)
//   COLOR              particle colour / alpha from the process material

#include "res://shaders/inc/noise.gdshaderinc"

global uniform float fire_intensity;
global uniform vec2 wind_direction;
global uniform float wind_strength;

uniform float heat : hint_range(0.2, 6.0) = 2.2;
uniform float opacity : hint_range(0.0, 1.0) = 0.55;
uniform float lick : hint_range(0.0, 1.0) = 0.0;
uniform float wind_lean : hint_range(0.0, 1.0) = 0.25;

varying float v_phase;
varying float v_seed;
varying vec2 v_lean;

void vertex() {
	v_phase = INSTANCE_CUSTOM.y;
	v_seed = float(INSTANCE_ID) * 0.731;
	vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 fwd = normalize(cross(right, up));
	right = normalize(cross(up, fwd));
	// Flames lean downwind; the lean grows towards the tip of the card.
	vec2 wd = normalize(wind_direction + vec2(1e-4)) * wind_strength * wind_lean;
	v_lean = vec2(dot(right, vec3(wd.x, 0.0, wd.y)), 0.0);
	vec3 local = VERTEX;
	float tip = clamp(local.y / max(sc.y, 1e-3) + 0.5, 0.0, 1.0);
	local.x += v_lean.x * tip * tip * 0.6;
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(right * sc.x, 0.0), vec4(up * sc.y, 0.0), vec4(fwd * sc.z, 0.0), MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	VERTEX = local;
}

// Black-body-ish ramp: dark red -> orange -> yellow -> near white.
vec3 fire_ramp(float t) {
	vec3 a = vec3(0.55, 0.05, 0.0);
	vec3 b = vec3(1.0, 0.32, 0.03);
	vec3 c = vec3(1.0, 0.72, 0.18);
	vec3 d = vec3(1.0, 0.96, 0.78);
	if (t < 0.33) {
		return mix(a, b, t / 0.33);
	} else if (t < 0.66) {
		return mix(b, c, (t - 0.33) / 0.33);
	}
	return mix(c, d, (t - 0.66) / 0.34);
}

void fragment() {
	float t = v_phase;
	float y = 1.0 - UV.y;
	float x = UV.x - 0.5;

	// Rising turbulence at two scales, sheared so tongues lick sideways.
	float speed = 2.6 + lick * 1.4;
	vec2 p = vec2(x * 3.0 + y * 0.7, y * 2.2 - TIME * speed) + vec2(v_seed, v_seed * 1.7);
	float n = fbm2(p, 4);
	float n2 = fbm2(p * 2.3 + vec2(7.3, -TIME * (1.9 + lick)), 3);

	// Candle-flame silhouette: rounded base tapering to a point, its edge
	// pushed about by the turbulence so no two cards share an outline.
	x += (n - 0.5) * (0.3 + lick * 0.25) * (0.2 + y);
	float width = (0.44 - lick * 0.16) * pow(max(1.0 - y, 0.0), 0.62 + lick * 0.2) * (0.85 + 0.3 * n2);
	float shape = 1.0 - smoothstep(width * 0.25, width, abs(x));
	shape *= smoothstep(0.0, 0.1, y);
	float density = shape * (0.35 + 0.85 * n + 0.3 * n2) - y * (0.3 + lick * 0.2);
	density -= t * t * (0.75 + lick * 0.4);
	density = clamp(density * 1.6, 0.0, 1.0);

	float hot = clamp((density - 0.2) * 1.4, 0.0, 1.0) * (1.0 - y * 0.55 - lick * 0.3);
	vec3 col = fire_ramp(hot);
	float core = clamp((density - 0.55) * 2.6, 0.0, 1.0) * (1.0 - y * 0.6) * (1.0 - lick * 0.7);
	float alpha = smoothstep(0.02, 0.4, density) * COLOR.a;
	float radiance = heat * (0.6 + 0.4 * clamp(fire_intensity, 0.0, 2.0));
	ALBEDO = col * COLOR.rgb * radiance * (alpha + core * 1.6);
	ALPHA = alpha * opacity;
}
