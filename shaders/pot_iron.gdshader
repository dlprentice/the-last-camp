shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
#include "res://shaders/inc/noise.gdshaderinc"
global uniform float fire_intensity;
varying vec3 local_pos;
void vertex() { local_pos = VERTEX; }
void fragment() {
	float soot = fbm3(local_pos * 34.0, 3);
	float bottom = 1.0 - smoothstep(1.021, 1.085, local_pos.y);
	float heat = bottom * smoothstep(0.12, 1.0, fire_intensity);
	ALBEDO = mix(vec3(0.060, 0.057, 0.052), vec3(0.11, 0.035, 0.015), heat * 0.65) * (0.85 + soot * 0.25);
	METALLIC = mix(0.72, 0.40, bottom);
	ROUGHNESS = 0.66 + soot * 0.15;
	SPECULAR = 0.4;
	// A dull heat tint in the bottom quarter, never a luminous orange band.
	EMISSION = vec3(0.25, 0.017, 0.002) * heat * (0.72 + 0.28 * soot);
}
