shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"
global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform float water_level;
#include "res://shaders/inc/pond_waves.gdshaderinc"

uniform bool blossom = false;
uniform bool stalk = false;
uniform bool floating = true;

varying float variation;
varying vec3 world_pos;

void vertex() {
	if (stalk) {
		// Petioles lean toward their rhizome via the instance transform.
		MODELVIEW_NORMAL_MATRIX = transpose(inverse(mat3(MODELVIEW_MATRIX)));
	}
	vec3 origin = MODEL_MATRIX[3].xyz;
	variation = hash12(origin.xz);
	if (floating) {
		vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
		vec3 n;
		// Leaves conform to the small wave across their surface; blossoms move
		// as a whole on their flexible stalk, without stretching the petals.
		vec3 disp = pond_wave((blossom || stalk) ? origin.xz : wp.xz, TIME, n);
		if (stalk) {
			// Unit stalk y=-1 is rooted in the bed; y=0 meets the leaf.
			disp *= smoothstep(0.0, 0.75, VERTEX.y + 1.0);
		}
		wp += disp;
		if (blossom) {
			wp.y -= dot((wp.xz - origin.xz - disp.xz), n.xz) / max(n.y, 0.1);
		}
		VERTEX = (inverse(MODEL_MATRIX) * vec4(wp, 1.0)).xyz;
	}
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	// A mirror camera sits below the pond. Submerged stalks must not enter
	// that image: otherwise their reflection rises like rods above the pads.
	// Keep the leaf rims through the full wave envelope. Only rooted geometry
	// can form the erroneous long reflection below that small moving margin.
	float local_level = water_level + pond_interaction(world_pos.xz).r;
	if ((in_underwater_reflection_pass(CAMERA_VISIBLE_LAYERS) && world_pos.y > local_level + 0.04) ||
		(in_reflection_pass(CAMERA_VISIBLE_LAYERS) && !in_underwater_reflection_pass(CAMERA_VISIBLE_LAYERS) && (stalk || blossom) && world_pos.y < local_level - 0.04)) {
		discard;
	}
	vec2 p = UV * 2.0 - 1.0;
	float radial = length(p);
	float a = atan(p.y, p.x);
	// Radiating veins, lighter than the blade, plus a fine cross-vein net.
	float veins = pow(abs(cos(a * 15.0 + sin(radial * 22.0) * 0.14)), 18.0);
	float net = pow(abs(sin(radial * 60.0 + a * 3.0)), 30.0) * 0.5;
	float edge = smoothstep(0.68, 1.0, radial);
	// A waxy mid green, paler toward the centre, browning at the rim.
	vec3 green = mix(vec3(0.055, 0.145, 0.045), vec3(0.11, 0.23, 0.07), variation);
	green = mix(green, green * vec3(1.25, 1.2, 0.9), (1.0 - radial) * 0.35);
	vec3 leaf = mix(green, vec3(0.17, 0.15, 0.05), edge * 0.5);
	leaf = mix(leaf, leaf * vec3(1.35, 1.3, 1.0), (veins + net) * radial * 0.35);
	ALBEDO = blossom ? COLOR.rgb : leaf;
	if (stalk) { ALBEDO = vec3(0.065, 0.13, 0.035); }
	// Waxy cuticle: the pad catches the sky like the water around it.
	ROUGHNESS = stalk ? 0.78 : (blossom ? 0.55 : 0.30 + variation * 0.10);
	SPECULAR = blossom ? 0.35 : 0.5;
	NORMAL_MAP = normalize(vec3(p * ((blossom || stalk) ? 0.0 : 0.05), 1.0)) * 0.5 + 0.5;
	BACKLIGHT = ALBEDO * 0.2;
}
