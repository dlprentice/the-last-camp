shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled;

// Suspended silt in the pond: tiny soft grains lit from above, brighter
// where the sun still reaches, fading with distance through the water.
// Drawn only while the lens is under the surface.

global uniform vec3 sun_direction;
global uniform vec3 sun_color;
global uniform float water_level;

uniform float visibility : hint_range(0.0, 1.0) = 0.0;
uniform float glow : hint_range(0.0, 6.0) = 0.8;
uniform float extinction : hint_range(0.0, 1.0) = 0.16;
varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	if (visibility <= 0.001 || world_pos.y > water_level - 0.05) {
		discard;
	}
	float facing = clamp(abs(dot(normalize(NORMAL), normalize(VIEW))), 0.0, 1.0);
	float core = pow(facing, 4.0);
	float depth = water_level - world_pos.y;
	float sun_up = smoothstep(0.0, 0.3, sun_direction.y);
	vec3 light = sun_color * (0.30 * sun_up * exp(-depth * 0.5)) + vec3(0.05, 0.11, 0.09);
	float dist = length(world_pos - CAMERA_POSITION_WORLD);
	// Grains right at the lens would read as snow: ease them in over the first metre.
	float atten = exp(-dist * extinction) * smoothstep(0.25, 1.3, dist);
	ALBEDO = COLOR.rgb * light * (0.3 + 1.7 * core) * COLOR.a * glow * visibility * atten;
	ALPHA = 1.0;
}
