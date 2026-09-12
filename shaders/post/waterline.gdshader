shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled;

// Half-submerged frames. While the eye is still above the pond, the pixels
// whose near-plane point lies under the surface get the water's murk here,
// from the scene depth, so a crossing reads as one continuous surface with
// the wavy waterline the post grade draws. Full-screen quad on the near plane.

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
global uniform vec2 wind_direction;
global uniform float wind_strength;
#include "res://shaders/inc/pond_waves.gdshaderinc"
uniform vec3 fog_color : source_color = vec3(0.05, 0.17, 0.15);
uniform float density = 0.16;
uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform float water_level = -0.9;

varying vec3 v_near_view;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
	vec4 near_view = INV_PROJECTION_MATRIX * vec4(VERTEX.xy, 1.0, 1.0);
	v_near_view = near_view.xyz / near_view.w;
}

void fragment() {
	vec3 near_world = (INV_VIEW_MATRIX * vec4(v_near_view, 1.0)).xyz;
	float surface_offset = pond_lens_offset(near_world, SCREEN_UV, water_level, TIME);
	float below = 1.0 - smoothstep(-0.002, 0.002, surface_offset);
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	vec4 view = INV_PROJECTION_MATRIX * ndc;
	view.xyz /= view.w;
	float dist = max(length(view.xyz) - length(v_near_view), 0.0);
	vec3 ray_world = normalize((INV_VIEW_MATRIX * vec4(view.xyz, 0.0)).xyz);
	// Integrate only the submerged ray segment. Looking upward through the
	// split must not apply pond extinction to kilometres of air beyond it.
	if (ray_world.y > 0.0001) {
		dist = min(dist, max(-surface_offset / ray_world.y, 0.0));
	}
	// Receive native volumetric scattering at the submerged endpoint, rather
	// than at this near-plane quad. Godot applies extinction before scattered
	// light; a fog-disabled overlay would paint murk over the light shafts.
	LIGHT_VERTEX = v_near_view + normalize(view.xyz) * dist;
	float murk = 1.0 - exp(-dist * density);
	ALBEDO = fog_color;
	ALPHA = murk * below * strength;
}
