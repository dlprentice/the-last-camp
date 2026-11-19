shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// One of Tom's knife notches, traced into the wood. The patch lies over the
// cut's opening with UV = (across, up) on the notch in metres; each fragment
// follows the view ray down into the V (BlazeNotch.cut_depth, the same lens-shaped
// two-stroke cut) and shades the face it meets: the dark section of bark at the
// rim, then pale wood a dozen summers old, a little greyed, darker in the crease.
// Fragments outside the opening are discarded, so the host's own bark shows there.
// It bends with the trunk exactly as the bark round it does (same tree_height,
// same wind at the tree base), so the notch never separates from its tree. On a
// felled log wind_response is 0.

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
uniform vec3 wood_color : source_color = vec3(0.63, 0.53, 0.39);
uniform vec3 weathered_color : source_color = vec3(0.44, 0.42, 0.38);
uniform float weathering : hint_range(0.0, 1.0) = 0.42;
uniform vec3 inner_bark_color : source_color = vec3(0.17, 0.10, 0.065);
uniform float wind_response : hint_range(0.0, 1.0) = 1.0;
uniform float cut_half_width = 0.065;
uniform float cut_top = 0.022;
uniform float cut_bottom = -0.013;
uniform float cut_depth = 0.025;
uniform float crease_at = 0.36;
uniform float bark_thickness = 0.008;
instance uniform float tree_height = 12.0;

varying vec3 world_pos;

void vertex() {
	vec3 base_world = MODEL_MATRIX[3].xyz;
	vec2 wind = wind_field(base_world, wind_direction, wind_strength, TIME);
	if (impostor_bake != 0) wind = vec2(0.0);
	VERTEX = mix(VERTEX, tree_bend(VERTEX, tree_height, wind), wind_response);
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

// Depth of the cut below the opening at p = (across, up), metres; 0 outside.
float notch_depth(vec2 p) {
	float t = abs(p.x) / cut_half_width;
	float spread = 1.0 - pow(t, 1.4);
	if (spread <= 0.0) return 0.0;
	float crease = cut_bottom + (cut_top - cut_bottom) * crease_at;
	float top = crease + (cut_top - crease) * spread;
	float bottom = crease - (crease - cut_bottom) * spread;
	if (p.y >= top || p.y <= bottom) return 0.0;
	float d = p.y >= crease ? (top - p.y) / max(top - crease, 1e-5) : (p.y - bottom) / max(crease - bottom, 1e-5);
	return cut_depth * clamp(d, 0.0, 1.0) * sqrt(spread);
}

void fragment() {
	// The patch's frame from its own derivatives (taken before any fragment is
	// dropped): T and B follow UV (across, up) in view space, N is the surface's
	// outward normal.
	vec3 n0 = normalize(NORMAL);
	vec3 dp1 = dFdx(VERTEX), dp2 = dFdy(VERTEX);
	vec2 du1 = dFdx(UV), du2 = dFdy(UV);
	vec3 dp2perp = cross(dp2, n0);
	vec3 dp1perp = cross(n0, dp1);
	vec3 T = normalize(dp2perp * du1.x + dp1perp * du2.x + vec3(1e-7));
	vec3 B = normalize(dp2perp * du1.y + dp1perp * du2.y + vec3(1e-7));
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.05)) {
		discard;
	}
	// The bark tore a little along the knife's edge: the rim is ragged.
	float tear = gnoise2(UV * vec2(260.0, 520.0)) * 0.5 + 0.5;
	if (notch_depth(UV) <= 0.0018 * tear) {
		discard;
	}
	// The view ray into the surface, in (across, up, out) metres.
	vec3 ray = -normalize(VIEW);
	vec3 r = vec3(dot(ray, T), dot(ray, B), dot(ray, n0));
	r.z = min(r.z, -0.05);
	float t_max = cut_depth / -r.z;
	vec3 start = vec3(UV, 0.0);
	float lo = 0.0, hi = t_max;
	const int STEPS = 40;
	for (int i = 1; i <= STEPS; i++) {
		float t = t_max * float(i) / float(STEPS);
		vec3 q = start + r * t;
		if (q.z <= -notch_depth(q.xy)) {
			hi = t;
			break;
		}
		lo = t;
	}
	for (int i = 0; i < 6; i++) {
		float mid = 0.5 * (lo + hi);
		vec3 q = start + r * mid;
		if (q.z <= -notch_depth(q.xy)) hi = mid; else lo = mid;
	}
	vec3 hit = start + r * hi;
	float depth = notch_depth(hit.xy);
	// The face's normal from the depth's slope.
	float e = 0.0004;
	float dx = (notch_depth(hit.xy + vec2(e, 0.0)) - notch_depth(hit.xy - vec2(e, 0.0))) / (2.0 * e);
	float dy = (notch_depth(hit.xy + vec2(0.0, e)) - notch_depth(hit.xy - vec2(0.0, e))) / (2.0 * e);
	// Two knife strokes leave faint facets across each face.
	float facet = sin(hit.x * 260.0 + sin(hit.x * 41.0) * 2.0) * 0.1;
	vec3 nt = normalize(vec3(dx, dy + facet * sign(dy), 1.0));
	NORMAL = normalize(T * nt.x + B * nt.y + n0 * nt.z);

	// Wood grain: the texture at the scale of real grain, with fine fibres
	// and a dozen summers' grey settled unevenly into it.
	vec2 grain_uv = vec2(hit.x * 16.0, hit.y * 5.0 - depth * 6.0);
	vec2 g1 = du1 * vec2(16.0, 5.0), g2 = du2 * vec2(16.0, 5.0);
	vec3 grain_tex = textureGrad(albedo_tex, grain_uv, g1, g2).rgb;
	vec3 orm = textureGrad(orm_tex, grain_uv, g1, g2).rgb;
	float grain = dot(grain_tex, vec3(0.333));
	float fibre = gnoise2(vec2(hit.x * 900.0, hit.y * 60.0 + depth * 200.0)) * 0.5 + 0.5;
	float grey = clamp(weathering + (gnoise2(hit.xy * 120.0) * 0.5) * 0.35, 0.0, 1.0);
	vec3 wood = mix(wood_color, weathered_color, grey) * mix(0.62, 1.15, grain) * mix(0.8, 1.08, fibre);
	// Bark section at the rim: dark, with a thin pale cambium line where it meets the wood.
	float bark = 1.0 - smoothstep(bark_thickness * 0.8, bark_thickness * 1.1, depth);
	float cambium = smoothstep(bark_thickness * 0.75, bark_thickness, depth) * (1.0 - smoothstep(bark_thickness, bark_thickness * 1.25, depth));
	vec3 inner = inner_bark_color * mix(0.8, 1.15, hash12(floor(hit.xy * vec2(400.0, 900.0))));
	vec3 albedo = mix(wood, inner, bark);
	albedo = mix(albedo, wood_color * 0.62, cambium * 0.6);
	// The crease is shut in and stays darker, as does wood in the cut's shadow.
	float closed = smoothstep(0.55, 1.0, depth / cut_depth);
	albedo *= mix(1.0, 0.55, closed);
	float soaked = clamp(scene_wetness, 0.0, 1.0);
	ALBEDO = albedo * (1.0 - soaked * 0.35);
	ROUGHNESS = mix(clamp(orm.g * 1.1, 0.7, 1.0), 0.62, soaked);
	SPECULAR = 0.3;
	AO = mix(1.0, 0.45, closed) * orm.r;
}
