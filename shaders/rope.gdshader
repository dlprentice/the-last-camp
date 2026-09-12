shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

// Natural fibre rope for the procedural tube meshes. MeshBuilder.add_tube()
// guarantees U around the circumference and V along arc length, allowing a
// real helical strand pattern instead of triplanar canvas wrapped around rope.
#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 fire_position;
global uniform float fire_intensity;

uniform vec3 fibre_color : source_color = vec3(0.50, 0.39, 0.24);
uniform vec3 highlight_color : source_color = vec3(0.68, 0.56, 0.36);
uniform float lay_turns_per_metre : hint_range(0.5, 12.0) = 5.4;
uniform float strand_count : hint_range(2.0, 8.0) = 3.0;
uniform float groove_depth : hint_range(0.0, 0.4) = 0.18;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.01)) { discard; }
	// Three/four broad strands twist around the rope while a finer counter-wave
	// breaks the perfectly machined helix. V is physical-ish arc length for all
	// of the project's tube/rope helpers.
	float footprint = fwidth(UV.x * strand_count + UV.y * lay_turns_per_metre);
	float resolved = 1.0 - smoothstep(0.2, 0.75, footprint);
	float helix = sin((UV.x * strand_count + UV.y * lay_turns_per_metre) * TAU);
	float secondary = sin((UV.x * strand_count * 2.0 - UV.y * lay_turns_per_metre * 0.47 + 0.31) * TAU);
	float fibres = (helix * 0.72 + secondary * 0.28) * resolved;
	float fuzz = vnoise3(vec3(UV * vec2(17.0, 31.0), UV.x * 9.0));
	float light_strand = smoothstep(-0.45, 0.86, fibres);
	vec3 albedo = mix(fibre_color * 0.74, highlight_color, light_strand * 0.72);
	albedo *= 0.88 + fuzz * 0.22;

	// Perturb across the tube tangent so the helix catches a moving highlight;
	// it remains small because silhouette geometry already carries the cord.
	float slope = cos((UV.x * strand_count + UV.y * lay_turns_per_metre) * TAU);
	NORMAL = normalize(NORMAL + TANGENT * slope * groove_depth * resolved);

	vec3 n_world = world_normal_from_view(INV_VIEW_MATRIX, NORMAL);
	float rain_breakup = vnoise3(world_pos * 7.0 + vec3(2.0, 13.0, 5.0));
	float wet = clamp(scene_wetness, 0.0, 1.0) * smoothstep(0.20, 0.82, rain_breakup);
	wet *= 0.70 + 0.30 * max(n_world.y, 0.0);
	albedo *= 1.0 - wet * 0.26;

	float rough = 0.88 - light_strand * 0.07 + (fuzz - 0.5) * 0.08;
	rough = mix(rough, 0.60, wet * 0.70);
	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.50) * fire_intensity;

	ALBEDO = albedo;
	ROUGHNESS = clamp(rough, 0.50, 1.0);
	SPECULAR = mix(0.18, 0.28, wet);
	BACKLIGHT = albedo * 0.12;
	EMISSION = albedo * vec3(1.0, 0.33, 0.06) * warmth * 0.018;
}
