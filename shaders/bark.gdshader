shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Trunks and branches: cylindrical-UV PBR bark with junction AO baked in the
// vertex colour, moss creeping up the shaded, upward-facing sides, and the
// shared trunk bend.

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
uniform sampler2D noise_tex : hint_default_white, repeat_enable;
uniform vec3 moss_color : source_color = vec3(0.22, 0.34, 0.12);
uniform float moss_amount : hint_range(0.0, 1.0) = 0.5;
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;
uniform float wind_response : hint_range(0.0, 1.0) = 1.0;

// MultiMesh instances carry (height/40, crown height/40, crown radius/20,
// tint variation) in INSTANCE_CUSTOM instead of per-instance uniforms.
uniform bool use_instance_custom = false;
instance uniform float tree_height = 12.0;
instance uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);

varying vec3 world_pos;
varying float junction_ao;
varying float rel_height;
varying vec3 v_tint;

void vertex() {
	float th = use_instance_custom ? INSTANCE_CUSTOM.x * 40.0 : tree_height;
	v_tint = use_instance_custom ? vec3(0.9 + 0.2 * INSTANCE_CUSTOM.w) : tint;
	vec3 base_world = MODEL_MATRIX[3].xyz;
	vec2 wind = wind_field(base_world, wind_direction, wind_strength, TIME);
	rel_height = clamp(VERTEX.y / max(th, 0.1), 0.0, 1.0);
	VERTEX = mix(VERTEX, tree_bend(VERTEX, th, wind), wind_response);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	junction_ao = COLOR.r;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.05)) {
		discard;
	}
	vec3 albedo = texture(albedo_tex, UV).rgb;
	vec3 orm = texture(orm_tex, UV).rgb;
	vec3 nrm = texture(normal_tex, UV).rgb;

	// Moss grows on the upper, sheltered side of low branches and the trunk.
	vec3 n_world = world_normal_from_view(INV_VIEW_MATRIX, NORMAL);
	float upness = clamp(n_world.y * 0.5 + 0.5, 0.0, 1.0);
	float patch = texture(noise_tex, world_pos.xz * 0.35 + world_pos.y * 0.12).g;
	float moss = smoothstep(0.35, 0.8, upness * 0.6 + patch * 0.6) * moss_amount;
	moss *= 1.0 - smoothstep(0.15, 0.7, rel_height);
	moss *= 1.0 - orm.b * 0.5;
	albedo = mix(albedo, moss_color * (0.8 + 0.4 * patch), moss);
	float rough = mix(orm.g, 0.92, moss);

	// Porous bark darkens as it absorbs rain. Runoff reaches vertical trunks,
	// while upward-facing branches catch more; moss stays rough when soaked.
	// Reuse the broad moss variation so moisture does not become fine glitter.
	float exposure = 0.78 + 0.22 * max(n_world.y, 0.0);
	float soaked = clamp(scene_wetness, 0.0, 1.0) * exposure * (0.85 + patch * 0.15);
	float damp_roughness = min(rough, mix(0.66, 0.82, moss));
	ALBEDO = albedo * v_tint * (1.0 - soaked * mix(0.32, 0.38, moss));
	ROUGHNESS = mix(rough, damp_roughness, soaked);
	SPECULAR = 0.35 + soaked * 0.06 * (1.0 - moss * 0.6);
	AO = orm.r * mix(0.5, 1.0, junction_ao);
	AO_LIGHT_AFFECT = 0.5;
	NORMAL_MAP = nrm;
	NORMAL_MAP_DEPTH = normal_strength * (1.0 - moss * 0.5);
	if (impostor_bake == 1) {
		EMISSION = ALBEDO * AO;
		ALBEDO = vec3(0.0);
	} else if (impostor_bake == 2) {
		EMISSION = pow(normalize(NORMAL) * 0.5 + 0.5, vec3(2.2));
		ALBEDO = vec3(0.0);
	}
}
