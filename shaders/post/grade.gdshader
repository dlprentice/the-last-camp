shader_type canvas_item;
render_mode blend_disabled;

// Final grade, applied to the tonemapped image between the 3D scene and the
// HUD: a light contrast-adaptive sharpen (TAA softens), chromatic aberration
// that only exists in the far corners, a vignette, film grain in perceptual
// space, and the underwater treatment (tint, wobble, murk). Underwater is
// decided per pixel against the waterline: a pixel whose near-plane point is
// under the pond surface is submerged, so a lens at the surface shows a wavy
// split with a bright meniscus instead of a band of edge-on water.

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
global uniform vec2 wind_direction;
global uniform float wind_strength;
#include "res://shaders/inc/pond_waves.gdshaderinc"
#include "res://shaders/inc/lens_water.gdshaderinc"
#include "res://shaders/inc/noise.gdshaderinc"
uniform float grain : hint_range(0.0, 0.2) = 0.03;
uniform float vignette : hint_range(0.0, 1.0) = 0.22;
uniform float chromatic : hint_range(0.0, 2.0) = 0.35;
uniform float sharpen : hint_range(0.0, 1.0) = 0.25;
// 1 while the camera is over the pond; the waterline test does the rest.
uniform float underwater : hint_range(0.0, 1.0) = 0.0;
uniform vec3 underwater_tint : source_color = vec3(0.16, 0.42, 0.40);
uniform float exposure_fade : hint_range(0.0, 1.0) = 0.0;
uniform float entry_age = 100.0;
uniform float exit_age = 100.0;

group_uniforms Waterline;
uniform vec3 cam_pos = vec3(0.0);
uniform vec3 cam_right = vec3(1.0, 0.0, 0.0);
uniform vec3 cam_up = vec3(0.0, 1.0, 0.0);
uniform vec3 cam_fwd = vec3(0.0, 0.0, -1.0);
uniform float cam_tan_half = 0.41;
uniform float cam_aspect = 1.78;
uniform float cam_near = 0.05;
uniform float water_level = -0.9;
group_uniforms;

// Height of this pixel's near-plane point above the (wavy) waterline, in
// metres. Negative means the pixel is under water.
float waterline_offset(vec2 uv) {
	vec2 ndc = vec2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
	vec3 p = cam_pos + cam_near * (cam_fwd + cam_right * (ndc.x * cam_tan_half * cam_aspect) + cam_up * (ndc.y * cam_tan_half));
	return pond_lens_offset(p, uv, water_level, TIME);
}

// pcg3d: a well distributed integer hash, free of the streaks that sin-based
// hashes produce on some GPUs.
uvec3 pcg3d(uvec3 v) {
	v = v * 1664525u + 1013904223u;
	v.x += v.y * v.z;
	v.y += v.z * v.x;
	v.z += v.x * v.y;
	v ^= v >> 16u;
	v.x += v.y * v.z;
	v.y += v.z * v.x;
	v.z += v.x * v.y;
	return v;
}

float grain_noise(vec2 pixel, float frame) {
	uvec3 h = pcg3d(uvec3(uint(pixel.x), uint(pixel.y), uint(frame)));
	// Triangular distribution reads as film rather than digital noise.
	float a = float(h.x) * (1.0 / 4294967295.0);
	float b = float(h.y) * (1.0 / 4294967295.0);
	return a + b - 1.0;
}

void fragment() {
	vec2 px = SCREEN_PIXEL_SIZE;
	vec2 uv = SCREEN_UV;
	vec2 centred = uv * 2.0 - 1.0;
	float aspect = px.y / max(px.x, 1e-6);
	centred.x *= aspect;
	float r2 = dot(centred, centred) / (aspect * aspect + 1.0);

	float span = cam_near * cam_tan_half;
	float line_offset = underwater > 0.0 ? waterline_offset(uv) : 1000.0;
	// Spread the optical change across the wet meniscus, not a binary lens
	// state. Geometry/fog still use the actual water surface in the 3D pass.
	float below = underwater * (1.0 - smoothstep(-span * 0.20, span * 0.20, line_offset));
	float wet_edge = underwater * exp(-abs(line_offset) / max(span * 0.14, 0.001));
	// A curved wet meniscus magnifies and bends the image across the boundary.
	uv.y += tanh(line_offset / max(span * 0.04, 0.0005)) * wet_edge * 0.008;
	uv.x += sin(uv.x * 29.0 + TIME * 2.0) * wet_edge * 0.0015;
	float runoff = exp(-exit_age * 2.8) * (1.0 - below);
	float drop_coverage = 0.0;
	vec2 drop_normal = vec2(0.0);
	if (exit_age < 8.0 && below < 0.99) {
		vec2 film = lens_water_height(SCREEN_UV, exit_age, aspect);
		drop_coverage = film.y * (1.0 - below);
		drop_normal = vec2(dFdx(film.x) / px.x / aspect, dFdy(film.x) / px.y);
		// A bead is a tiny lens: it compresses and bends the image, with a
		// restrained crescent reflection rather than an opaque bubble ring.
		uv += clamp(drop_normal * vec2(0.024 / aspect, 0.024), vec2(-0.035), vec2(0.035)) * (1.0 - below);
		uv += vec2(sin(uv.x * 47.0 + uv.y * 9.0), cos(uv.x * 31.0 - exit_age * 2.0)) * runoff * 0.004;
	}
	uv = clamp(uv, px, 1.0 - px);

	// Underwater: the image wobbles as light refracts through the moving surface.
	if (below > 0.001) {
		vec2 wobble = vec2(
				sin(uv.y * 23.0 + TIME * 1.9) * cos(uv.x * 17.0 - TIME * 1.3),
				cos(uv.x * 19.0 + TIME * 1.6) * sin(uv.y * 13.0 + TIME * 1.1));
		// Strong bending lives at the air/water boundary. Once submerged,
		// solid posts retain their shape in the otherwise homogeneous water.
		uv += wobble * (0.0007 + wet_edge * 0.0038) * below;
		uv = clamp(uv, px, 1.0 - px);
	}

	// Chromatic aberration grows with the cube of the radius: none in the
	// centre, a couple of pixels in the corners.
	vec2 ca = centred * chromatic * r2 * r2 * 0.0035 * (1.0 + below * 2.0);
	vec3 col;
	col.r = texture(screen_tex, clamp(uv + ca, px, 1.0 - px)).r;
	col.g = texture(screen_tex, uv).g;
	col.b = texture(screen_tex, clamp(uv - ca, px, 1.0 - px)).b;
	// Gaussian screen mips are much softer than ordinary texture mips. Keep
	// a small optical softness; distance fog and far DOF provide visibility
	// falloff without erasing the nearby bed and its moving light pools.
	vec3 submerged_blur = textureLod(screen_tex, uv, 0.65).rgb;
	col = mix(col, submerged_blur, below * 0.40);
	col = mix(col, textureLod(screen_tex, uv, 2.0).rgb, runoff * 0.65 + wet_edge * 0.25);

	// Contrast-adaptive sharpen: strengthen the pixel against its cross
	// neighbours, damped on strong edges so it never rings.
	if (sharpen > 0.001) {
		vec3 l = texture(screen_tex, uv - vec2(px.x, 0.0)).rgb;
		vec3 r = texture(screen_tex, uv + vec2(px.x, 0.0)).rgb;
		vec3 u = texture(screen_tex, uv - vec2(0.0, px.y)).rgb;
		vec3 d = texture(screen_tex, uv + vec2(0.0, px.y)).rgb;
		vec3 blur = (l + r + u + d) * 0.25;
		vec3 mn = min(min(l, r), min(u, d));
		vec3 mx = max(max(l, r), max(u, d));
		float contrast = max(max(mx.r - mn.r, mx.g - mn.g), mx.b - mn.b);
		float amount = sharpen * (1.0 - smoothstep(0.15, 0.6, contrast)) * (1.0 - below);
		col += (col - blur) * amount;
	}

	// Absorption and in-scattering are already integrated over view distance
	// by the 3D fog passes. A second flat teal grade destroyed that depth and
	// made nearby timber change colour abruptly as the lens crossed the water.
	// Meniscus: a bright wet line where the surface meets the lens.
	float meniscus = underwater * exp(-abs(line_offset) / max(span * 0.05, 1e-5));
	col *= 1.0 - wet_edge * 0.12;
	col += vec3(0.55, 0.75, 0.75) * meniscus * 0.22;
	// Broken microfoam flecks ride the moving wet edge, never a solid stripe.
	float flecks = smoothstep(0.55, 0.72, gnoise2(vec2(SCREEN_UV.x * 190.0, TIME * 0.8)));
	col += vec3(0.62, 0.73, 0.68) * meniscus * flecks * 0.17;
	// Air entrained at the crossing forms an irregular frothy wash. It tears
	// into patches and clears upward quickly; the remaining bubbles live in 3D.
	if (entry_age < 0.9) {
		vec2 flow = SCREEN_UV * vec2(aspect, 1.0) + vec2(0.0, entry_age * 0.65);
		vec2 curl = vec2(fbm2(flow * 6.0 + 13.2, 3), fbm2(flow * 6.0 - 3.7, 3));
		float patches = fbm2(flow * 13.0 + curl * 2.3, 3);
		float lace = gnoise2(flow * 165.0 + curl * 8.0);
		float foam = smoothstep(0.45, 0.62, patches) * smoothstep(0.39, 0.59, lace);
		float wash = (1.0 - smoothstep(0.08, 0.85, entry_age)) * smoothstep(-0.1, 0.5, SCREEN_UV.y);
		col = mix(col, vec3(0.50, 0.64, 0.59), foam * wash * 0.62 * below);
	}
	float bead_glint = pow(max(0.0, dot(normalize(vec3(drop_normal, 0.7)), normalize(vec3(-0.35, -0.6, 0.8)))), 32.0);
	col += vec3(0.55, 0.65, 0.68) * bead_glint * drop_coverage * 0.13;

	// Vignette.
	float vig = 1.0 - smoothstep(0.25, 1.65, r2) * (vignette + below * 0.08);
	col *= vig;

	// Grain, stronger in the shadows like a negative.
	float lum = dot(col, vec3(0.2126, 0.7152, 0.0722));
	float n = grain_noise(FRAGCOORD.xy, floor(TIME * 48.0));
	col += n * grain * (1.1 - lum * 0.8);

	col *= 1.0 - exposure_fade;
	COLOR = vec4(max(col, vec3(0.0)), 1.0);
}
