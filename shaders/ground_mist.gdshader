shader_type fog;

#include "res://shaders/inc/noise.gdshaderinc"

global uniform vec2 wind_direction;
global uniform float daylight;
global uniform float scene_wetness;
uniform float density = 0.035;
uniform bool after_rain = false;

void fog() {
	vec3 edge = smoothstep(vec3(0.0), vec3(0.22), UVW) * (1.0 - smoothstep(vec3(0.72), vec3(1.0), UVW));
	vec3 drift = vec3(wind_direction.x, 0.0, wind_direction.y) * TIME * 0.14;
	float billows = fbm3((WORLD_POSITION - drift) * vec3(0.14, 0.65, 0.14), 3);
	float wisps = smoothstep(0.28, 0.70, billows);
	float layer = exp(-max(UVW.y - 0.22, 0.0) * 3.5);
	DENSITY = density * edge.x * edge.y * edge.z * layer * wisps * mix(1.15, 0.75, daylight);
	if (after_rain) { DENSITY *= scene_wetness * smoothstep(0.05, 0.75, daylight); }
	ALBEDO = vec3(0.74, 0.84, 0.88);
}
