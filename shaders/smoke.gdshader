shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, diffuse_lambert, specular_disabled;

// Lit smoke puff: a camera-facing card with a noisy soft disc that thins as the
// particle ages, shaded by the scene lights (the fire warms its underside,
// the low sun rims it) and faded where it cuts into geometry.

#include "res://shaders/inc/noise.gdshaderinc"

uniform sampler2D noise_tex : hint_default_white, repeat_enable;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;
uniform vec3 smoke_color : source_color = vec3(0.70, 0.73, 0.76);
uniform float density : hint_range(0.1, 2.0) = 0.6;
uniform float soft_distance = 0.6;
global uniform float fire_intensity;

varying float v_phase;
varying float v_seed;
varying float v_view_depth;

void vertex() {
	v_phase = INSTANCE_CUSTOM.y;
	v_seed = float(INSTANCE_ID) * 0.377;
	vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	float ang = INSTANCE_CUSTOM.x + v_seed * 6.0;
	float ca = cos(ang);
	float sa = sin(ang);
	vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
	vec3 up = normalize(INV_VIEW_MATRIX[1].xyz);
	vec3 fwd = normalize(INV_VIEW_MATRIX[2].xyz);
	vec3 r2 = right * ca + up * sa;
	vec3 u2 = -right * sa + up * ca;
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(r2 * sc.x, 0.0), vec4(u2 * sc.y, 0.0), vec4(fwd * sc.z, 0.0), MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	v_view_depth = -(MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).z;
}

void fragment() {
	vec2 c = UV - 0.5;
	float radial = 1.0 - clamp(length(c) * 2.0, 0.0, 1.0);
	vec2 nuv = UV * 1.4 + vec2(v_seed, -TIME * 0.05);
	float n = texture(noise_tex, nuv).r * 0.7 + texture(noise_tex, nuv * 2.3 + 0.37).b * 0.3;
	float body = pow(radial, 1.3) * (0.25 + 1.1 * n) - v_phase * 0.3;
	body = clamp(body * 1.6, 0.0, 1.0) * COLOR.a * density;

	// Soft particle fade against the depth buffer.
	if (soft_distance > 0.0) {
		float scene_d = texture(depth_texture, SCREEN_UV).r;
		vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, scene_d, 1.0);
		vec4 view = INV_PROJECTION_MATRIX * ndc;
		float scene_depth = -view.z / view.w;
		body *= clamp((scene_depth - v_view_depth) / soft_distance, 0.0, 1.0);
	}

	// Rounded normal so the puff shades like a lump, not a plate.
	vec3 n_view = normalize(vec3(c.x * 1.4, -c.y * 1.4, 0.8));
	NORMAL = FRONT_FACING ? n_view : normalize(vec3(n_view.xy, -n_view.z));
	// Fresh smoke just above the flames carries their glow.
	vec3 albedo = smoke_color * COLOR.rgb;
	float fresh = 1.0 - smoothstep(0.0, 0.18, v_phase);
	EMISSION = vec3(1.0, 0.42, 0.1) * fresh * fresh * 0.35 * body * smoothstep(0.04, 0.45, fire_intensity);
	ALBEDO = albedo;
	ROUGHNESS = 1.0;
	// Thin enough to see the pot and the far bank through it, dense enough to
	// read in daylight against the trees.
	ALPHA = clamp(body, 0.0, 0.40);
}
