shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_disabled, shadows_disabled;

// The virtual camera sits above the water. Only the reflected object's
// submerged leg is integrated here; the real camera's native volumetric fog
// supplies the eye-to-surface leg when the mirror is drawn on the water.
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform vec3 fog_color : source_color = vec3(0.05, 0.17, 0.15);
uniform float density = 0.16;
global uniform float water_level;

void vertex() {
	POSITION = vec4(VERTEX.xy, 1.0, 1.0);
}

void fragment() {
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, texture(depth_tex, SCREEN_UV).r, 1.0);
	view.xyz /= view.w;
	vec3 point = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
	float submerged_fraction = clamp((water_level - point.y) / max(CAMERA_POSITION_WORLD.y - point.y, 0.001), 0.0, 1.0);
	float water_distance = length(view.xyz) * submerged_fraction;
	LIGHT_VERTEX = view.xyz;
	ALBEDO = fog_color;
	ALPHA = 1.0 - exp(-density * water_distance);
}
