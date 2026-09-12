shader_type spatial;
render_mode cull_disabled, diffuse_burley;
#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"
#include "res://shaders/inc/tree_wind.gdshaderinc"
global uniform float scene_wetness;
global uniform float water_level;
global uniform vec2 wind_direction;
global uniform float wind_strength;
uniform sampler2D needles : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_trans_tex : filter_linear_mipmap_anisotropic;
uniform float translucency = 0.28;
varying float variation;
varying vec3 canopy_normal;
varying float canopy_ao;
varying vec3 world_pos;
void vertex() {
	variation = hash12(MODEL_MATRIX[3].xz);
	canopy_normal = normalize(MODEL_NORMAL_MATRIX * (CUSTOM0.xyz + vec3(0.0, 1e-4, 0.0)));
	canopy_ao = CUSTOM0.a;
	vec2 wind = wind_field(MODEL_MATRIX[3].xyz, wind_direction, wind_strength, TIME);
	VERTEX = tree_bend(VERTEX, 18.0, wind * 0.55);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}
void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) { discard; }
	vec4 tex = texture(needles, UV);
	bool bark = UV.x < 0.0;
	float mip = textureQueryLod(needles, UV).x;
	ALPHA = bark ? 1.0 : tex.a * (1.0 + clamp(mip, 0.0, 6.0) * 0.22);
	ALPHA_SCISSOR_THRESHOLD = 0.34;
	vec3 albedo = COLOR.rgb * (bark ? vec3(1.0) : tex.rgb) * mix(vec3(0.91, 0.96, 1.0), vec3(1.02, 1.0, 0.92), variation);
	// Native NORMAL already faces the viewer for two-sided cards. Preserve
	// the outward crown gradient so low sunlight models separate leaf masses.
	if (!bark) { NORMAL = normalize(mix(NORMAL, view_normal_from_world(VIEW_MATRIX, canopy_normal), 0.58)); }
	float exposed = bark ? 0.20 : (0.45 + 0.55 * max(canopy_normal.y, 0.0)) * canopy_ao;
	float wet = clamp(scene_wetness, 0.0, 1.0) * exposed;
	ALBEDO = albedo * (1.0 - wet * 0.07);
	ROUGHNESS = bark ? 0.9 : mix(0.64, 0.40, wet);
	SPECULAR = bark ? 0.15 : mix(0.26, 0.36, wet);
	BACKLIGHT = bark ? vec3(0.0) : vec3(1.0, 0.98, 0.88) * texture(normal_trans_tex, UV).a * translucency;
	AO = bark ? 0.9 : canopy_ao;
	AO_LIGHT_AFFECT = 0.30;
}
