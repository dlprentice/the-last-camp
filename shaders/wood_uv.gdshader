shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// UV-mapped PBR wood for built things (dock planks, piles, the canoe) where
// the grain must follow the piece. Vertex colour tints each board, the
// waterline grows a dark algae band and wets the wood below it, and the
// campfire warms nearby surfaces.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 fire_position;
global uniform float fire_intensity;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : hint_default_white, repeat_enable;

uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);
uniform float roughness_scale : hint_range(0.2, 1.5) = 1.0;
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;
uniform float ao_strength : hint_range(0.0, 1.0) = 0.8;
uniform float wet_band = 0.5;
// Closed vessels can mark protected inner wood with vertex alpha = 0.
// Rain still reaches that wood; pond water does not soak through the hull.
uniform bool use_shore_mask = false;
uniform float algae_amount : hint_range(0.0, 1.0) = 0.6;
uniform float weathering : hint_range(0.0, 1.0) = 0.3;
uniform float fire_warmth : hint_range(0.0, 2.0) = 0.4;

varying vec3 world_pos;
varying vec3 v_tint;
varying float v_shore_exposure;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_tint = COLOR.rgb;
	v_shore_exposure = use_shore_mask ? COLOR.a : 1.0;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.05)) {
		discard;
	}
	vec3 albedo = texture(albedo_tex, UV).rgb * tint * v_tint;
	vec3 orm = texture(orm_tex, UV).rgb;
	vec3 nrm = texture(normal_tex, UV).rgb;

	// Silvered weathering on faces that see the sky.
	vec3 n_world = world_normal_from_view(INV_VIEW_MATRIX, NORMAL);
	float upness = clamp(n_world.y, 0.0, 1.0);
	float patch = texture(noise_tex, world_pos.xz * 0.7 + world_pos.y * 0.3).r;
	float silver = weathering * smoothstep(0.2, 0.9, upness * 0.7 + patch * 0.5);
	albedo = mix(albedo, vec3(0.52, 0.5, 0.46) * (0.8 + 0.4 * patch), silver * 0.6);

	// Waterline band and wet darkening.
	float above = world_pos.y - water_level;
	float wet = (1.0 - smoothstep(0.0, wet_band, above)) * v_shore_exposure;
	wet = max(wet, scene_wetness * (0.55 + upness * 0.45));
	float band = 1.0 - smoothstep(0.0, 0.32, abs(above - 0.06));
	float algae = algae_amount * band * v_shore_exposure * (0.4 + 0.6 * texture(noise_tex, world_pos.xz * 1.3 + world_pos.y * 2.0).g);
	albedo = mix(albedo, vec3(0.07, 0.10, 0.05), algae);
	albedo *= 1.0 - wet * 0.4;
	float film = smoothstep(0.25, 0.95, wet) * (0.45 + upness * 0.55);
	float rough = mix(orm.g * roughness_scale, 0.16 + patch * 0.08, wet * 0.95);

	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.42) * fire_intensity * fire_warmth;

	ALBEDO = albedo;
	ROUGHNESS = clamp(rough, 0.05, 1.0);
	SPECULAR = mix(0.4, 0.5, wet);
	CLEARCOAT = film * 0.65;
	CLEARCOAT_ROUGHNESS = 0.10 + patch * 0.10;
	AO = mix(1.0, orm.r, ao_strength);
	AO_LIGHT_AFFECT = 0.4;
	NORMAL_MAP = nrm;
	NORMAL_MAP_DEPTH = normal_strength * (1.0 - film * 0.48);
	EMISSION = albedo * vec3(1.0, 0.42, 0.12) * warmth * 0.12;
}
