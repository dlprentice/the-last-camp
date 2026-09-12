shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
#include "res://shaders/inc/common.gdshaderinc"
global uniform float rainfall;
global uniform float water_level;
global uniform float lightning_flash;
global uniform vec2 wind_direction;
global uniform float wind_strength;
#include "res://shaders/inc/pond_waves.gdshaderinc"
uniform float impact_clock = 0.0;
varying float opacity;
varying vec3 world_pos;
void vertex() {
	vec3 origin = MODEL_MATRIX[3].xyz;
	if (INSTANCE_CUSTOM.z > 0.5) {
		vec3 wave_normal;
		origin += pond_wave(origin.xz, TIME, wave_normal);
	}
	float period = mix(0.75, 1.60, INSTANCE_CUSTOM.y);
	float age = mod(impact_clock + INSTANCE_CUSTOM.x * period, period);
	float speed = mix(0.75, 1.35, COLOR.g);
	float life = speed / (4.905 * max(MODEL_MATRIX[1].y, 0.3));
	float angle = COLOR.r * 6.2831853 + INSTANCE_CUSTOM.x * 6.2831853;
	vec3 flight = vec3(cos(angle) * age * 0.40, speed * age, sin(angle) * age * 0.40);
	vec3 centre = origin + mat3(MODEL_MATRIX) * flight - vec3(0.0, 4.905 * age * age, 0.0);
	vec3 right = INV_VIEW_MATRIX[0].xyz;
	vec3 up = INV_VIEW_MATRIX[1].xyz;
	float size = mix(0.0016, 0.0032, COLOR.b);
	float alive = step(age, life) * step(INSTANCE_CUSTOM.w, rainfall);
	world_pos = centre + (right * VERTEX.x + up * VERTEX.y * 1.7) * size * alive;
	opacity = alive * (1.0 - smoothstep(life * 0.55, life, age))
		* (1.0 - smoothstep(12.0, 28.0, distance(CAMERA_POSITION_WORLD, centre)));
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world_pos, 1.0)).xyz;
	NORMAL = normalize((inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD - centre, 0.0)).xyz);
}
void fragment() {
	if (opacity < 0.001 || reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.0)) { discard; }
	float edge = 1.0 - smoothstep(0.20, 0.50, length(UV - vec2(0.5)));
	ALBEDO = vec3(0.46, 0.54, 0.60);
	ROUGHNESS = 0.10;
	SPECULAR = 0.65;
	EMISSION = vec3(0.008, 0.012, 0.016) + vec3(0.12, 0.14, 0.17) * lightning_flash;
	// Half the old opacity: dense bright splashes read as white noise on the pond.
	ALPHA = opacity * edge * 0.25;
}
