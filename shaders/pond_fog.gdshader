shader_type fog;

uniform float density = 0.0;
uniform vec3 water_albedo : source_color = vec3(0.40, 0.72, 0.57);

void fog() {
	// Reading SDF replaces Godot's automatic 10 cm box fade. A single thin
	// boundary keeps the water just below the lens as dense as deeper water.
	DENSITY = density * (1.0 - smoothstep(-0.008, 0.0, SDF));
	ALBEDO = water_albedo;
}
