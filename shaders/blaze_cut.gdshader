shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The faces of one of Tom's knife notches, carved into the host's mesh by
// BlazeNotch.carve. UV is the point's (across, up) on the notch in metres; UV2.x
// is how far the cut goes in there and UV2.y how deep the crease is at that
// point. The faces are wood a dozen years open: greyed and darkened, the fibres
// running up the stem and the growth rings showing as faint lines at constant
// depth, darker still where the crease shuts in. The top few millimetres are the
// bark's own section, dark and fibrous, where the bark tore or flaked away.
// It bends with the trunk exactly as the bark round it does (same tree_height,
// same wind at the tree base); on a felled log wind_response is 0.

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
// Old cut wood: the grey-brown of the weathered texture, a little warmer.
uniform vec3 wood_tint : source_color = vec3(0.86, 0.78, 0.68);
uniform vec3 inner_bark_color : source_color = vec3(0.13, 0.085, 0.06);
uniform float bark_thickness = 0.007;
uniform float ring_spacing = 0.0024;
uniform float normal_strength : hint_range(0.0, 2.0) = 0.7;
uniform float wind_response : hint_range(0.0, 1.0) = 1.0;
instance uniform float tree_height = 12.0;

varying vec3 world_pos;

void vertex() {
	vec3 base_world = MODEL_MATRIX[3].xyz;
	vec2 wind = wind_field(base_world, wind_direction, wind_strength, TIME);
	if (impostor_bake != 0) wind = vec2(0.0);
	VERTEX = mix(VERTEX, tree_bend(VERTEX, tree_height, wind), wind_response);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.05)) {
		discard;
	}
	vec2 p = UV;
	float depth = max(UV2.x, 0.0);
	float crease = max(UV2.y, 0.004);
	// The weathered texture with its grain running up the stem, at real scale.
	vec2 grain_uv = vec2(p.x * 3.2, p.y * 1.4 + depth * 2.0) + vec2(0.31, 0.17);
	vec3 tex = texture(albedo_tex, grain_uv).rgb;
	vec3 orm = texture(orm_tex, grain_uv).rgb;
	// Fine fibres along the stem, and the growth rings: thin darker lines where
	// the cut crosses each year's latewood at the same depth.
	float fibre = gnoise2(vec2(p.x * 900.0, p.y * 45.0)) * 0.5 + 0.5;
	float ring_at = depth / ring_spacing + gnoise2(p * vec2(40.0, 25.0)) * 0.35;
	float ring = smoothstep(0.72, 0.92, fract(ring_at)) * (1.0 - smoothstep(0.92, 1.0, fract(ring_at)));
	// Weather greys the wood unevenly; rain running out of the cut stains it in streaks.
	float stain = smoothstep(0.1, 0.7, gnoise2(vec2(p.x * 60.0, p.y * 8.0)) * 0.5 + 0.5);
	vec3 wood = tex * wood_tint * mix(0.82, 1.06, fibre) * mix(1.0, 0.78, ring) * mix(1.0, 0.8, stain);
	// The bark's own section along the rim.
	float bark_edge = bark_thickness * (0.75 + 0.5 * (gnoise2(p * vec2(220.0, 300.0)) * 0.5 + 0.5));
	float bark = 1.0 - smoothstep(bark_edge * 0.8, bark_edge, depth);
	vec3 inner = inner_bark_color * mix(0.7, 1.25, hash12(floor(p * vec2(500.0, 1400.0))));
	vec3 albedo = mix(wood, inner, bark);
	// The crease is shut in: darker, and the light reaches less of it.
	float closed = smoothstep(0.6, 1.0, depth / crease);
	albedo *= mix(1.0, 0.6, closed);
	float soaked = clamp(scene_wetness, 0.0, 1.0);
	ALBEDO = albedo * (1.0 - soaked * 0.35);
	ROUGHNESS = mix(clamp(orm.g * 1.05, 0.75, 1.0), 0.6, soaked);
	SPECULAR = 0.3;
	AO = mix(1.0, 0.5, closed) * mix(orm.r, 1.0, 0.4);
	NORMAL_MAP = texture(normal_tex, grain_uv).rgb;
	NORMAL_MAP_DEPTH = normal_strength * (1.0 - bark * 0.5);
}
