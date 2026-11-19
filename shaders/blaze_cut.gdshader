shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The cut face of one of Tom's knife notches: wood a dozen summers old, greyed
// and dulled, darker in the crease. It bends with the trunk exactly as the bark
// around it does (same tree_height, same wind at the tree base), so the notch
// never separates from its tree. On a felled log wind_response is 0.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"
#include "res://shaders/inc/tree_wind.gdshaderinc"

global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform float water_level;
global uniform float scene_wetness;
global uniform int impostor_bake;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform vec3 age_tint : source_color = vec3(0.60, 0.57, 0.52);
uniform float wind_response : hint_range(0.0, 1.0) = 1.0;
instance uniform float tree_height = 12.0;

varying vec3 world_pos;
varying vec3 v_tint;

void vertex() {
	vec3 base_world = MODEL_MATRIX[3].xyz;
	vec2 wind = wind_field(base_world, wind_direction, wind_strength, TIME);
	if (impostor_bake != 0) wind = vec2(0.0);
	VERTEX = mix(VERTEX, tree_bend(VERTEX, tree_height, wind), wind_response);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_tint = COLOR.rgb;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.05)) {
		discard;
	}
	vec3 wood = texture(albedo_tex, UV * 4.0).rgb;
	vec3 orm = texture(orm_tex, UV * 4.0).rgb;
	// Weathered: most of the grain's colour gone to grey, none of it bright.
	float grain = dot(wood, vec3(0.333));
	vec3 albedo = mix(vec3(grain), wood, 0.35) * age_tint * v_tint;
	float soaked = clamp(scene_wetness, 0.0, 1.0);
	ALBEDO = albedo * (1.0 - soaked * 0.35);
	ROUGHNESS = mix(clamp(orm.g * 1.1, 0.6, 1.0), 0.62, soaked);
	SPECULAR = 0.3;
	AO = orm.r;
	NORMAL_MAP = texture(normal_tex, UV * 4.0).rgb;
	NORMAL_MAP_DEPTH = 0.6;
}
