shader_type spatial;
render_mode blend_add, depth_draw_never, cull_disabled, unshaded, shadows_disabled, fog_disabled;

// Ember streak: a thin quad whose local Y follows the particle's velocity
// (align_y flag), rotated about that axis to face the camera.

uniform float glow : hint_range(0.2, 8.0) = 3.0;

void vertex() {
	vec3 sc = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz));
	vec3 up = normalize(MODEL_MATRIX[1].xyz);
	vec3 to_cam = normalize(CAMERA_POSITION_WORLD - MODEL_MATRIX[3].xyz);
	vec3 right = cross(up, to_cam);
	if (length(right) < 1e-4) {
		right = vec3(1.0, 0.0, 0.0);
	}
	right = normalize(right);
	vec3 fwd = cross(right, up);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(vec4(right * sc.x, 0.0), vec4(up * sc.y, 0.0), vec4(fwd * sc.z, 0.0), MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	float ax = 1.0 - abs(UV.x - 0.5) * 2.0;
	float ay = 1.0 - abs(UV.y - 0.5) * 2.0;
	float body = pow(clamp(ax, 0.0, 1.0), 2.2) * pow(clamp(ay, 0.0, 1.0), 0.7);
	vec3 col = mix(COLOR.rgb, vec3(1.0, 0.9, 0.6), body * body * 0.6);
	ALBEDO = col * body * COLOR.a * glow;
	ALPHA = 1.0;
}
