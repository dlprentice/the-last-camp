shader_type canvas_item;
render_mode blend_disabled;

// Only the underwater reflection viewport: preserve broad irradiance and
// nearby dark silhouettes, while removing the recognisable duplicate bed.
// This is a rough-surface artistic filter, not a model of bulk turbidity.
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform vec3 ray_forward = vec3(0.0, 0.0, -1.0);
uniform vec3 ray_right = vec3(1.0, 0.0, 0.0);
uniform vec3 ray_up = vec3(0.0, 1.0, 0.0);

void fragment() {
	vec3 ray = normalize(ray_forward + ray_right * (SCREEN_UV.x * 2.0 - 1.0)
		+ ray_up * (1.0 - SCREEN_UV.y * 2.0));
	float grazing = 1.0 - smoothstep(0.18, 0.42, abs(ray.y));
	vec3 blurred = textureLod(screen_tex, SCREEN_UV, mix(2.8, 0.65, grazing)).rgb;
	float light = dot(blurred, vec3(0.2126, 0.7152, 0.0722));
	COLOR = vec4(mix(blurred, light * vec3(0.70, 1.0, 0.82), 0.65 * (1.0 - grazing)), 1.0);
}
