shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled;

// Heat shimmer above the fire: a camera-facing card that re-samples the
// screen through rising noise. Invisible on its own; only what is behind it
// wobbles.

#include "res://shaders/inc/noise.gdshaderinc"

global uniform float fire_intensity;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear;
uniform float strength : hint_range(0.0, 0.1) = 0.02;

void vertex() {
	vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	vec3 right = normalize(INV_VIEW_MATRIX[0].xyz);
	vec3 up = vec3(0.0, 1.0, 0.0);
	vec3 fwd = normalize(cross(right, up));
	right = normalize(cross(up, fwd));
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(right * sc.x, 0.0), vec4(up * sc.y, 0.0), vec4(fwd * sc.z, 0.0), MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec2 c = UV - 0.5;
	float mask = (1.0 - smoothstep(0.2, 0.5, abs(c.x))) * (1.0 - smoothstep(0.25, 0.5, abs(c.y)));
	mask *= smoothstep(0.0, 0.25, 1.0 - UV.y);
	float n1 = fbm2(vec2(UV.x * 5.0, UV.y * 7.0 - TIME * 3.1), 3) - 0.5;
	float n2 = fbm2(vec2(UV.x * 7.0 + 3.0, UV.y * 9.0 - TIME * 4.3), 3) - 0.5;
	vec2 offset = vec2(n1, n2) * strength * mask * clamp(fire_intensity, 0.0, 2.0);
	ALBEDO = texture(screen_texture, clamp(SCREEN_UV + offset, vec2(0.001), vec2(0.999))).rgb;
	ALPHA = mask;
}
