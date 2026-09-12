shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
#include "res://shaders/inc/common.gdshaderinc"
global uniform float water_level;
varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) { discard; }
	// Low vertex alpha marks the thin insect wing. This remains an opaque,
	// lit surface: small glints come from light, never a glowing screen dot.
	float membrane = 1.0 - smoothstep(0.25, 0.65, COLOR.a);
	ALBEDO = COLOR.rgb;
	ROUGHNESS = mix(0.82, 0.32, membrane);
	SPECULAR = mix(0.28, 0.48, membrane);
	BACKLIGHT = COLOR.rgb * mix(0.08, 0.26, membrane);
}
