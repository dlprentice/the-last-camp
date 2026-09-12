shader_type spatial;
render_mode cull_disabled, diffuse_burley;
#include "res://shaders/inc/noise.gdshaderinc"
global uniform float scene_wetness;
uniform sampler2D weave_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D weave_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
void fragment() {
	vec3 weave = texture(weave_tex, UV * 4.0).rgb;
	float edge = min(min(UV.x, 1.90 - UV.x), min(UV.y, 1.20 - UV.y));
	float border = smoothstep(0.022, 0.026, edge) * (1.0 - smoothstep(0.045, 0.049, edge));
	float second = smoothstep(0.063, 0.067, edge) * (1.0 - smoothstep(0.072, 0.076, edge));
	float variation = gnoise2(UV * 8.0) * 0.65 + gnoise2(UV * 31.0 + 0.4) * 0.35;
	vec3 linen = vec3(0.43, 0.40, 0.32) * (0.89 + dot(weave, vec3(0.333)) * 0.22) * (0.96 + variation * 0.075);
	float hem = 1.0 - smoothstep(0.005, 0.009, edge);
	ALBEDO = mix(linen, vec3(0.10, 0.16, 0.12), max(border, second) * (0.68 + variation * 0.08));
	ALBEDO *= 1.0 - hem * 0.065;
	// Linen absorbs water through its fibres instead of acquiring a polished
	// shell. Keep the woven normal relief and a broad, predominantly matte lobe.
	float soaked = clamp(scene_wetness, 0.0, 1.0) * (0.88 + variation * 0.12);
	ALBEDO *= 1.0 - soaked * 0.30;
	ROUGHNESS = mix(0.91 + variation * 0.07, 0.82, soaked);
	NORMAL_MAP = texture(weave_normal, UV * 4.0).rgb;
	NORMAL_MAP_DEPTH = 0.10;
	SPECULAR = mix(0.18, 0.20, soaked);
	BACKLIGHT = ALBEDO * 0.12 * (1.0 - soaked * 0.20);
	if (!FRONT_FACING) { NORMAL = -NORMAL; }
}
