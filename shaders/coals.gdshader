shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// The ash bed: grey-black ash with a network of glowing cracks that breathe
// with the fire's intensity, brightest at the centre.

#include "res://shaders/inc/noise.gdshaderinc"

global uniform float fire_intensity;

uniform sampler2D noise_tex : hint_default_white, repeat_enable;
uniform vec3 ash_color : source_color = vec3(0.09, 0.085, 0.08);
uniform vec3 ember_color : source_color = vec3(1.0, 0.32, 0.04);
uniform float ember_strength : hint_range(0.0, 10.0) = 2.0;
uniform float bed_radius = 0.5;

varying vec3 local_pos;

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	vec2 p = local_pos.xz;
	float r = length(p) / bed_radius;
	vec4 n = texture(noise_tex, p * 2.6 + vec2(0.13, 0.71));
	vec4 n2 = texture(noise_tex, p * 6.5 + vec2(0.5, 0.2));
	float cracks = smoothstep(0.52, 0.8, n.b * 0.6 + n2.r * 0.5);
	float centre = 1.0 - smoothstep(0.15, 1.0, r);
	float pulse = 0.72 + 0.28 * sin(TIME * 2.3 + n.g * 9.0) * sin(TIME * 0.9 + n2.b * 5.0);
	float live = smoothstep(0.02, 0.5, fire_intensity) * clamp(fire_intensity, 0.0, 2.0);
	float glow = cracks * centre * pulse * live;
	vec3 albedo = mix(ash_color * (0.7 + 0.6 * n.r), ash_color * 0.35, glow);
	ALBEDO = albedo;
	ROUGHNESS = 0.95;
	SPECULAR = 0.1;
	EMISSION = ember_color * glow * ember_strength;
}
