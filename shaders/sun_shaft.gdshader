shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled, world_vertex_coords;

// Underwater sun shafts. Each instance is a camera-facing streak hung from
// the mean surface and laid along the refracted sun direction, so the beams
// lean the way real light enters the pond and swing with the sun through
// the day. Brightness ripples down the beam like drifting caustic focus and
// dies with depth; the streak fades where it meets the bed or a post so no
// hard card edges show. Only drawn while the lens is under the surface.

#include "res://shaders/inc/noise.gdshaderinc"

global uniform vec3 sun_direction;
global uniform vec3 sun_color;
global uniform float water_level;

uniform float visibility : hint_range(0.0, 1.0) = 0.0;
uniform float strength : hint_range(0.0, 4.0) = 0.55;
uniform float extinction : hint_range(0.0, 1.0) = 0.18;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

varying vec2 v_uv;
varying vec3 v_world;
varying float v_phase;
varying float v_gain;
varying float v_view_fade;

float linear_depth(vec2 uv, mat4 inv_proj) {
	float d = texture(depth_texture, uv).r;
	vec4 ndc = vec4(uv * 2.0 - 1.0, d, 1.0);
	vec4 view = inv_proj * ndc;
	return -view.z / view.w;
}

void vertex() {
	// Instance basis is identity: the anchor is the instance origin on the
	// mean surface and the mesh is a unit quad, x across, y from 0 down to -1.
	vec3 origin = MODEL_MATRIX[3].xyz;
	vec3 local = VERTEX - origin;
	vec4 data = INSTANCE_CUSTOM; // phase, width, length, gain
	vec3 sun = normalize(sun_direction);
	vec3 axis = refract(-sun, vec3(0.0, 1.0, 0.0), 1.0 / 1.333);
	if (dot(axis, axis) < 1e-4 || sun.y < 0.02) {
		axis = vec3(0.0, -1.0, 0.0);
	}
	axis = normalize(axis);
	// The surface waves walk the focus around slowly.
	vec3 anchor = origin + vec3(sin(TIME * 0.55 + data.x * 6.283) * 0.14, 0.0,
		cos(TIME * 0.42 + data.x * 5.1) * 0.14);
	vec3 to_camera = CAMERA_POSITION_WORLD - anchor;
	vec3 side = cross(axis, normalize(to_camera));
	float side_len = length(side);
	// A streak seen along its own axis collapses into a line: let it go.
	v_view_fade = smoothstep(0.15, 0.6, side_len);
	side = side_len > 1e-4 ? side / side_len : vec3(1.0, 0.0, 0.0);
	float along = clamp(-local.y, 0.0, 1.0);
	float width = data.y * (1.0 + along * 0.7);
	vec3 world = anchor + axis * (along * data.z) + side * (local.x * width);
	VERTEX = world;
	v_world = world;
	v_uv = vec2(local.x, along);
	v_phase = data.x;
	v_gain = data.w;
}

void fragment() {
	if (visibility <= 0.001) {
		discard;
	}
	vec3 sun = normalize(sun_direction);
	float sun_up = smoothstep(0.02, 0.35, sun.y);
	// Diffuse sheet, not a pillar: the lobe is soft all the way across and
	// its edge wanders with noise so no straight card edge survives.
	float edge_wander = (fbm2(vec2(v_uv.y * 3.0 + v_phase * 11.0, TIME * 0.2), 2) - 0.5) * 0.35;
	float across = 1.0 - clamp(abs(v_uv.x + edge_wander) * 2.0, 0.0, 1.0);
	float profile = pow(across * across * (3.0 - 2.0 * across), 2.4);
	float depth = max(water_level - v_world.y, 0.0);
	float top = smoothstep(0.0, 0.16, v_uv.y);
	// Beams thin out gradually instead of ending in a rounded cap.
	float fall = exp(-depth * 0.55) * pow(1.0 - smoothstep(0.35, 1.0, v_uv.y), 1.5);
	// Bright bands drift down the beam as the focusing ripples pass overhead.
	float t = TIME * 0.32;
	float n = fbm2(vec2(v_uv.y * 2.4 - t, v_phase * 19.0 + v_uv.x * 1.2 + t * 0.15), 3);
	float flicker = 0.25 + 1.2 * smoothstep(0.30, 0.80, n);
	// Slower, broader breaks so beams thin out and return rather than glow evenly.
	float breaks = fbm2(vec2(v_phase * 7.3 + v_uv.x * 0.6, v_uv.y * 0.9 - t * 0.45), 2);
	flicker *= 0.35 + 1.3 * smoothstep(0.28, 0.72, breaks);
	float scene = linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
	float here = -(VIEW_MATRIX * vec4(v_world, 1.0)).z;
	float soft = clamp((scene - here) / 0.7, 0.0, 1.0);
	float dist = length(v_world - CAMERA_POSITION_WORLD);
	float atten = exp(-dist * extinction) * smoothstep(0.3, 1.2, dist);
	vec3 tint = vec3(0.60, 0.92, 0.84);
	ALBEDO = sun_color * tint * (profile * top * fall * flicker * soft * atten
		* v_view_fade * sun_up * strength * visibility * v_gain);
	ALPHA = 1.0;
}
