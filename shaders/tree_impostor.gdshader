shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

// A baked tree seen from six directions: one quad that turns towards the
// camera around Y, picks the two nearest baked views and blends their
// albedo and view-space normals, then lets the renderer light it like the
// hero tree. With face_sun the quad turns towards the sun instead and only
// casts shadows, so the tree's shadow keeps its shape from the light's side.

#include "res://shaders/inc/common.gdshaderinc"

global uniform vec3 sun_direction;
global uniform float scene_wetness;
global uniform float water_level;

uniform sampler2D albedo_atlas : source_color, filter_linear_mipmap;
uniform sampler2D normal_atlas : filter_linear_mipmap;
uniform int tiles = 6;
uniform bool face_sun = false;
uniform float alpha_cutoff : hint_range(0.05, 0.95) = 0.42;
uniform float roughness : hint_range(0.0, 1.0) = 0.62;
uniform float translucency : hint_range(0.0, 1.0) = 0.22;
uniform vec3 tint : source_color = vec3(1.0);
uniform float ao : hint_range(0.0, 1.0) = 0.8;
instance uniform float yaw = 0.0;

varying float v_azimuth;
varying vec3 world_pos;

void vertex() {
	vec3 base = MODEL_MATRIX[3].xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	vec3 towards = face_sun ? sun_direction : (CAMERA_POSITION_WORLD - base);
	vec3 dir = normalize(vec3(towards.x, 0.0, towards.z) + vec3(1e-5, 0.0, 0.0));
	vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), dir));
	vec3 world = base + right * (VERTEX.x * s) + vec3(0.0, 1.0, 0.0) * (VERTEX.y * s);
	world_pos = world;
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
	NORMAL = normalize((inverse(MODEL_MATRIX) * vec4(dir, 0.0)).xyz);
	// The baked view i looked at the tree from azimuth i * 360 / tiles.
	v_azimuth = atan(dir.x, dir.z) - yaw;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) {
		discard;
	}
	float t = fract(v_azimuth / (2.0 * PI)) * float(tiles);
	float ia = floor(t);
	float f = t - ia;
	float ib = mod(ia + 1.0, float(tiles));
	vec2 uv_a = vec2((UV.x + ia) / float(tiles), UV.y);
	vec2 uv_b = vec2((UV.x + ib) / float(tiles), UV.y);
	vec4 ca = texture(albedo_atlas, uv_a);
	vec4 cb = texture(albedo_atlas, uv_b);
	vec4 na = texture(normal_atlas, uv_a);
	vec4 nb = texture(normal_atlas, uv_b);
	// Weight each view by its coverage so a thin view does not dilute a full one.
	float wa = (1.0 - f) * ca.a;
	float wb = f * cb.a;
	float wsum = max(wa + wb, 1e-4);
	vec3 albedo = (ca.rgb * wa + cb.rgb * wb) / wsum;
	float alpha = mix(ca.a, cb.a, f);
	ALPHA = alpha;
	ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;
	// Baked normals are relative to the baking camera: x right, y up, z towards it.
	vec3 nva = na.xyz * 2.0 - 1.0;
	vec3 nvb = nb.xyz * 2.0 - 1.0;
	float aza = ia / float(tiles) * 2.0 * PI + yaw;
	float azb = ib / float(tiles) * 2.0 * PI + yaw;
	vec3 wa_n = vec3(cos(aza) * nva.x + sin(aza) * nva.z, nva.y, -sin(aza) * nva.x + cos(aza) * nva.z);
	vec3 wb_n = vec3(cos(azb) * nvb.x + sin(azb) * nvb.z, nvb.y, -sin(azb) * nvb.x + cos(azb) * nvb.z);
	vec3 n_world = normalize(wa_n * wa + wb_n * wb + vec3(1e-4));
	NORMAL = view_normal_from_world(VIEW_MATRIX, n_world);
	float wet = clamp(scene_wetness, 0.0, 1.0);
	ALBEDO = albedo * tint * (1.0 - wet * 0.08);
	ROUGHNESS = mix(roughness, 0.3, wet * 0.6);
	SPECULAR = 0.3;
	AO = ao;
	AO_LIGHT_AFFECT = 0.45;
	BACKLIGHT = vec3(1.0, 0.98, 0.88) * translucency;
}
