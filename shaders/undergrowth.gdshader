shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, diffuse_burley, specular_schlick_ggx;

// Ferns, shrubs and wildflowers: alpha cards instanced with MultiMesh.
// Vertex COLOR.r = height along the plant (0 root .. 1 top) for wind and AO,
// COLOR.g = per-card variation. Instance COLOR tints; INSTANCE_CUSTOM.x is a
// wind phase.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform vec3 player_position;
global uniform float water_level;
global uniform float scene_wetness;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D normal_trans_tex : filter_linear_mipmap_anisotropic;
uniform float alpha_cutoff : hint_range(0.05, 0.95) = 0.45;
uniform float translucency : hint_range(0.0, 2.0) = 0.8;
uniform float wind_amount : hint_range(0.0, 2.0) = 0.6;
uniform float spherical_normal_mix : hint_range(0.0, 1.0) = 0.6;
uniform float fade_start = 70.0;
uniform float fade_end = 110.0;
uniform float roughness : hint_range(0.0, 1.0) = 0.55;
uniform float trample_radius = 1.1;
// Atlases with several cells: the integer part of INSTANCE_CUSTOM.x picks
// the cell (row-major), the fractional part is the wind phase.
uniform int atlas_cells = 1;

varying vec2 v_uv;

varying float v_h;
varying float v_var;
varying vec3 v_tint;
varying vec3 v_sphere_normal;
varying vec3 world_pos;

void vertex() {
	vec3 base = MODEL_MATRIX[3].xyz;
	float phase = fract(INSTANCE_CUSTOM.x);
	float cell = floor(INSTANCE_CUSTOM.x);
	float cells = float(max(atlas_cells, 1));
	v_uv = UV / cells + vec2(mod(cell, cells), floor(cell / cells)) / cells;
	float h = COLOR.r;
	v_h = h;
	v_var = COLOR.g;
	v_tint = INSTANCE_CUSTOM.yzw;

	float cam_dist = length(CAMERA_POSITION_WORLD - base);
	float fade = smoothstep(fade_start, fade_end, cam_dist);
	if (phase < fade) {
		VERTEX = vec3(0.0);
	}

	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec2 wind = wind_field(base, wind_direction, wind_strength, TIME);
	float flutter = sin(TIME * 2.6 + phase * 12.57 + world.x * 1.3) * 0.08 * wind_strength;
	vec2 sway = (wind * 0.16 + vec2(flutter)) * wind_amount;
	world.xz += sway * h * h;

	vec2 to_player = base.xz - player_position.xz;
	float pd = length(to_player);
	float trample = 1.0 - smoothstep(0.2, trample_radius, pd);
	if (trample > 0.0 && abs(base.y - player_position.y) < 1.8) {
		world.xz += to_player / max(pd, 1e-3) * trample * 0.5 * h;
		world.y -= trample * 0.25 * h;
	}
	world_pos = world;

	vec3 centre = base + vec3(0.0, MODEL_MATRIX[1].y * 0.45, 0.0);
	v_sphere_normal = normalize(world - centre + vec3(0.0, 0.3, 0.0) + vec3(1e-4));
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world, 1.0)).xyz;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) {
		discard;
	}
	bool stem = v_uv.x < 0.0;
	vec4 tex = stem ? vec4(0.16, 0.26, 0.075, 1.0) : texture(albedo_tex, v_uv);
	float mip = textureQueryLod(albedo_tex, v_uv).x;
	ALPHA = tex.a * (1.0 + clamp(mip, 0.0, 6.0) * 0.2);
	ALPHA_SCISSOR_THRESHOLD = alpha_cutoff;

	vec4 nt = stem ? vec4(0.5, 0.5, 1.0, 0.15) : texture(normal_trans_tex, v_uv);
	vec3 n_ts = nt.rgb * 2.0 - 1.0;
	// Godot already orients NORMAL for the back face with cull_disabled.
	vec3 face_n = NORMAL;
	vec3 sphere_view = view_normal_from_world(VIEW_MATRIX, v_sphere_normal);
	vec3 n = normalize(mix(face_n, sphere_view, spherical_normal_mix));
	n = normalize(n + TANGENT * n_ts.x * 0.4 + BINORMAL * n_ts.y * 0.4);

	vec3 albedo = tex.rgb * v_tint * (0.9 + 0.2 * v_var);
	float wet = clamp(scene_wetness, 0.0, 1.0);
	// Wet leaves darken and pick up a sheen, but the sky reflection must stay
	// well below the leaf colour: at full wetness the pale bush cards were
	// reading as white plastic in the after-rain shot.
	// Kept low on leaves: a stronger film turned sunlit bush cards into a
	// white, speckled mass after rain (the pale foreground shrub in the
	// after-the-rain shot).
	float film = wet * (stem ? 0.35 : 0.28);
	ALBEDO = albedo * (1.0 - wet * 0.16);
	NORMAL = n;
	ROUGHNESS = mix(roughness, 0.50, film * 0.8);
	SPECULAR = 0.3 + film * 0.04;
	CLEARCOAT = film * 0.06;
	CLEARCOAT_ROUGHNESS = 0.28;
	AO = mix(0.45, 1.0, smoothstep(0.0, 0.6, v_h));
	AO_LIGHT_AFFECT = 0.5;
	BACKLIGHT = albedo * nt.a * translucency;
}
