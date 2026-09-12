shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled;
global uniform float lightning_flash;
global uniform vec2 wind_direction;
global uniform float wind_strength;
uniform sampler2D ground_height : filter_linear, repeat_disable;
uniform mat4 tent_inverse;
uniform mat4 dock_inverse;
uniform float dock_length = 8.9;
uniform float rain_clock = 0.0;
uniform float rain_strength = 0.0;
varying vec3 world_pos;
// A drop passing centimetres from the lens would draw as a sharp streak the
// height of the frame; real lenses cannot resolve it, so it fades out.
varying float near_fade;
void vertex() {
	vec3 centre = MODEL_MATRIX[3].xyz;
	float speed = 12.0 + INSTANCE_CUSTOM.y * 8.0;
	float fall = mod(rain_clock * speed + INSTANCE_CUSTOM.w * 22.0, 22.0);
	centre.y = 18.0 - fall;
	centre.xz += wind_direction * fall * 0.12 * wind_strength;
	vec3 axis = normalize(vec3(-wind_direction.x * 0.12 * wind_strength, 1.0, -wind_direction.y * 0.12 * wind_strength));
	vec3 right = normalize(cross(axis, CAMERA_POSITION_WORLD - centre));
	world_pos = centre + right * VERTEX.x + axis * VERTEX.y * (0.75 + INSTANCE_CUSTOM.z);
	near_fade = smoothstep(0.25, 0.85, length(centre - CAMERA_POSITION_WORLD));
	VERTEX = (inverse(MODEL_MATRIX) * vec4(world_pos, 1.0)).xyz;
	NORMAL = normalize((inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD - centre, 0.0)).xyz);
}
void fragment() {
	float ground = texture(ground_height, (world_pos.xz + vec2(128.0)) / 256.0).r;
	if (world_pos.y < max(ground, -0.90)) { discard; }
	vec3 tent = (tent_inverse * vec4(world_pos, 1.0)).xyz;
	float roof = 1.6 * (1.0 - abs(tent.x) / 1.2);
	if (abs(tent.x) < 1.2 && abs(tent.z) < 1.5 && tent.y < roof + 0.025) { discard; }
	vec3 dock = (dock_inverse * vec4(world_pos, 1.0)).xyz;
	if (abs(dock.x) < 0.8 && dock.z < 0.0 && dock.z > -dock_length && dock.y < 0.12) { discard; }
	float edge = 1.0 - smoothstep(0.1, 0.5, abs(UV.x - 0.5));
	float tip = sin(UV.y * 3.14159265);
	ALBEDO = vec3(0.53, 0.62, 0.72);
	ROUGHNESS = 0.25;
	EMISSION = vec3(0.005, 0.007, 0.009) + lightning_flash * vec3(0.06, 0.08, 0.12);
	ALPHA = edge * tip * rain_strength * 0.40 * smoothstep(-1.0, -0.7, world_pos.y) * near_fade;
}
