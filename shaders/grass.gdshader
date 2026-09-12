shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// Geometry grass: clumps of thin blades instanced with MultiMesh.
//   INSTANCE_CUSTOM: (phase, lean.x, lean.y, bend)   COLOR (instance): tint, a = dryness
//   UV.y: 0 at the root, 1 at the tip.   UV2: (per-blade random, d local.y / dv)
// Blades bend along their lean, ride the shared wind field, part around the
// player and dissolve with distance. Close shading follows curved blades;
// distant grass retains its ground-normal blend, with translucent tips.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform vec3 player_position;
global uniform float scene_wetness;

uniform vec3 root_color : source_color = vec3(0.065, 0.14, 0.045);
uniform vec3 tip_color : source_color = vec3(0.22, 0.39, 0.16);
uniform vec3 dry_tip_color : source_color = vec3(0.49, 0.47, 0.32);
uniform float fade_start = 55.0;
uniform float fade_end = 80.0;
uniform float trample_radius = 0.9;
uniform float sheen : hint_range(0.0, 1.0) = 0.18;
uniform float distance_widen : hint_range(0.0, 2.0) = 1.15;

varying float v_height;
varying vec3 v_tint;
varying float v_dry;
varying vec3 v_world_normal;
varying float v_ground_blend;
varying float v_side;
varying float v_blade;

void vertex() {
	vec3 base = MODEL_MATRIX[3].xyz;
	float phase = INSTANCE_CUSTOM.x;
	vec2 lean = INSTANCE_CUSTOM.yz * 2.0 - 1.0;
	float bend = INSTANCE_CUSTOM.w;
	float v = UV.y;
	float blade_random = UV2.x;
	v_height = v;
	v_tint = COLOR.rgb;
	v_dry = COLOR.a;
	v_side = UV.x;
	v_blade = blade_random;

	// Far dissolve only: the stand stays at full density everywhere it is
	// planted, and clumps at the planted edge shrink away in random order
	// over a band of thresholds rather than popping. Survivors widen so the
	// cover holds to the last metre.
	float cam_dist = length(CAMERA_POSITION_WORLD - base);
	float fade = smoothstep(fade_start, fade_end, cam_dist);
	float keep = smoothstep(fade - 0.06, fade + 0.06, phase);
	float widen = 1.0 + smoothstep(20.0, 110.0, cam_dist) * distance_widen;
	vec3 local = VERTEX;
	if (keep <= 0.001) {
		local = vec3(0.0);
	} else {
		vec3 centre_line = vec3(0.0, local.y, 0.0);
		local.xz = (local.xz - centre_line.xz) * widen * keep + centre_line.xz;
		local.y *= keep;
	}

	float clump_h = MODEL_MATRIX[1].y;
	float vv = v * v;

	// Static lean, shared wind, per-blade flutter.
	vec2 wind = wind_field(base, wind_direction, wind_strength, TIME);
	float flutter = sin(TIME * (2.6 + blade_random * 1.4) + phase * 12.57 + blade_random * 9.0 + base.x * 0.7) * 0.07 * wind_strength;
	vec2 sway = lean * bend * 0.3 + wind * 0.2 + lean * flutter;
	vec3 offset = vec3(sway.x, 0.0, sway.y) * vv * clump_h;
	// Analytic derivative of this same displacement with respect to UV.y.
	vec3 offset_dv = vec3(sway.x, 0.0, sway.y) * (2.0 * v * clump_h);

	// Trample: push away from the player and flatten.
	vec2 to_player = base.xz - player_position.xz;
	float pd = length(to_player);
	float trample = 1.0 - smoothstep(0.0, trample_radius, pd);
	if (trample > 0.0 && abs(base.y - player_position.y) < 1.6) {
		vec2 dir = to_player / max(pd, 1e-3);
		offset.xz += dir * trample * 0.5 * v * clump_h;
		offset.y -= trample * 0.4 * v * clump_h;
		offset_dv.xz += dir * trample * 0.5 * clump_h;
		offset_dv.y -= trample * 0.4 * clump_h;
	}
	float stretch = length(offset.xz);
	offset_dv.y -= dot(offset.xz, offset_dv.xz) / max(clump_h, 0.05);
	offset.y -= stretch * stretch / max(clump_h, 0.05) * 0.5;
	// A disappearing clump shrinks its deformation with its geometry; otherwise
	// the wind alone leaves floating slivers after the base mesh reaches zero.
	offset *= keep;
	offset_dv *= keep;

	// MODEL_NORMAL_MATRIX handles the independent instance scales. Undo the
	// extra x/z widening on the normal; uniform keep cancels on normalization.
	vec3 blade_n = normalize(MODEL_NORMAL_MATRIX * (NORMAL / vec3(widen, 1.0, widen)));
	if (blade_random >= 0.0 && keep > 0.001) {
		// J = I + (d offset/dv) outer (UP / vertical_derivative). Its cofactor
		// transforms the normal without a matrix inverse or singular divide.
		// See GPU Gems 42, "Deforming Normals"; no extra wind samples needed.
		float vertical_derivative = max(clump_h * UV2.y * keep, 0.001);
		vec3 slope = offset_dv / vertical_derivative;
		vec3 bent_n = vec3(blade_n.x * (1.0 + slope.y),
			blade_n.y - dot(blade_n.xz, slope.xz), blade_n.z * (1.0 + slope.y));
		if (dot(bent_n, bent_n) > 1e-8) {
			blade_n = normalize(bent_n);
		}
	}
	// A small cross-blade fold gives the ribbon a soft ridge without changing
	// its silhouette. Ground bias returns smoothly to the established far value.
	vec3 side_t = normalize(cross(vec3(0.0, 1.0, 0.0), blade_n) + vec3(1e-4));
	v_world_normal = normalize(blade_n + side_t * (UV.x - 0.5) * 0.4);
	v_ground_blend = mix(0.28, 0.6, smoothstep(3.0, 18.0, cam_dist));

	// Grass instances contain rotation and independent axis scales, without
	// shear. Project the wind displacement onto those axes instead of taking
	// two full matrix inverses for every vertex of every blade.
	vec3 ax = MODEL_MATRIX[0].xyz;
	vec3 ay = MODEL_MATRIX[1].xyz;
	vec3 az = MODEL_MATRIX[2].xyz;
	vec3 local_offset = vec3(dot(offset, ax) / dot(ax, ax),
		dot(offset, ay) / dot(ay, ay), dot(offset, az) / dot(az, az));
	VERTEX = local + local_offset;
}

void fragment() {
	// Custom NORMAL replaces Godot's earlier double-sided normal flip. Orient
	// the physical blade first, then add upward bias so back faces do not point
	// into the ground. Fragment NORMAL is converted to view space below.
	vec3 blade_n = normalize(v_world_normal) * (FRONT_FACING ? 1.0 : -1.0);
	vec3 n = normalize(mix(blade_n, vec3(0.0, 1.0, 0.0), v_ground_blend));
	// A stand contains leaves of several ages. Its old leaves are muted straw
	// among living blades, never a uniformly gold replacement for the clump.
	float stand_age = smoothstep(0.18, 0.88, clamp(v_dry, 0.0, 1.0));
	float leaf_age = (v_blade - 0.5) * 0.34 * smoothstep(0.0, 0.28, v_dry);
	float old_leaf = clamp(stand_age * 0.58 + leaf_age, 0.0, 0.76);
	vec3 tip = mix(tip_color, dry_tip_color, old_leaf);
	vec3 root = mix(root_color, dry_tip_color * 0.28, old_leaf * 0.65);
	vec3 albedo = mix(root, tip, smoothstep(0.0, 0.85, v_height)) * v_tint;
	albedo *= 0.88 + 0.24 * v_blade;
	albedo *= 0.94 + 0.12 * abs(v_side - 0.5) * 2.0;

	// Cattail seed heads are matte brown, independent of the green blade tint.
	if (v_blade < -0.5) {
		albedo = vec3(0.12, 0.052, 0.018) * (0.83 + 0.17 * hash12(UV * vec2(60.0, 180.0)));
	}
	// A thin rain film catches the light on blades; absorbent seed heads stay
	// mostly matte. Dry grass keeps its established response.
	float wet = clamp(scene_wetness, 0.0, 1.0);
	// Limit the wet specular film so blade tips do not resemble frost.
	float film = wet * (v_blade < -0.5 ? 0.08 : 0.22 + 0.28 * v_height);
	ALBEDO = albedo * (1.0 - wet * 0.12);
	float dry_roughness = v_blade < -0.5 ? 0.96 : mix(0.75, 0.5, v_height) - sheen * 0.12 + old_leaf * 0.12;
	ROUGHNESS = mix(dry_roughness, 0.40, film * 0.75);
	SPECULAR = 0.18 + sheen * 0.15 + film * 0.08;
	CLEARCOAT = film * 0.10;
	CLEARCOAT_ROUGHNESS = 0.22;
	AO = mix(0.48, 1.0, smoothstep(0.0, 0.75, v_height));
	AO_LIGHT_AFFECT = 0.28;
	NORMAL = view_normal_from_world(VIEW_MATRIX, n);
	// Forward+ applies ALBEDO to BACKLIGHT itself. Keep transmission restrained
	// while allowing low sunlight to pass through the finer green blade tips.
	BACKLIGHT = vec3(1.0, 0.98, 0.88) * (v_blade < -0.5 ? 0.008 : 0.07 + 0.15 * v_height);
}
