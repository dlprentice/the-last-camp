shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled;
global uniform vec2 wind_direction;
global uniform float wind_strength;
#include "res://shaders/inc/pond_waves.gdshaderinc"
uniform float water_level = -0.9;
varying float eye_distance;
varying float surface_offset;
void vertex() {
	vec3 centre = MODEL_MATRIX[3].xyz;
	float scale = length(MODEL_MATRIX[0].xyz);
	vec3 p = centre + (INV_VIEW_MATRIX[0].xyz * VERTEX.x + INV_VIEW_MATRIX[1].xyz * VERTEX.y) * scale;
	POSITION = PROJECTION_MATRIX * VIEW_MATRIX * vec4(p, 1.0);
	eye_distance = length(centre - CAMERA_POSITION_WORLD);
	surface_offset = pond_surface_offset(centre, water_level, TIME);
}
void fragment() {
	if (surface_offset > 0.0) { discard; }
	vec2 q = UV * 2.0 - 1.0;
	float r2 = dot(q, q);
	if (r2 > 1.0) { discard; }
	float z = sqrt(1.0 - r2);
	float rim = pow(1.0 - z, 3.0);
	float highlight = pow(max(dot(vec3(q, z), normalize(vec3(-0.4, -0.6, 0.8))), 0.0), 28.0);
	// The screen copy excludes transparent water, so sampling it inside a
	// bubble pasted bright dry sky over the pond. Use a transparent centre
	// and small reflected crescent, fading before a bubble reaches the lens.
	ALBEDO = vec3(0.12, 0.25, 0.18) + vec3(0.30, 0.42, 0.36) * highlight;
	ALPHA = (0.025 + rim * 0.22 + highlight * 0.18) * (1.0 - smoothstep(0.90, 1.0, r2))
		* smoothstep(0.12, 0.32, eye_distance);
}
