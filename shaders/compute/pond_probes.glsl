#[compute]
#version 450

// GPU-side gather: only these few residual heights are copied to the CPU.
layout(local_size_x = 32, local_size_y = 1, local_size_z = 1) in;
layout(rgba32f, set = 0, binding = 0) uniform readonly image2D state_texture;
layout(std430, set = 0, binding = 1) readonly buffer Positions { vec2 positions[]; } input_data;
layout(std430, set = 0, binding = 2) writeonly buffer Heights { float heights[]; } output_data;
layout(push_constant, std430) uniform Parameters {
	vec4 domain;
	vec4 counts; // Resolution, probe count, reserved, reserved.
} p;

void main() {
	uint index = gl_GlobalInvocationID.x;
	if (index >= uint(p.counts.y)) return;
	vec2 uv = (input_data.positions[index] - p.domain.xy) / p.domain.zw;
	if (any(isnan(uv)) || any(isinf(uv)) || any(lessThan(uv, vec2(0.0))) || any(greaterThanEqual(uv, vec2(1.0)))) {
		output_data.heights[index] = 0.0;
		return;
	}
	int size = int(p.counts.x);
	vec2 pixel = uv * float(size) - 0.5;
	ivec2 base = ivec2(floor(pixel));
	vec2 f = fract(pixel);
	// Match the display texture's ordinary bilinear interpolation, including
	// zero-height solid cells. Outside-domain samples never wrap to another bank.
	float height = 0.0;
	for (int z = 0; z < 2; z++) {
		for (int x = 0; x < 2; x++) {
			ivec2 cell = clamp(base + ivec2(x, z), ivec2(0), ivec2(size - 1));
			float weight = (x == 0 ? 1.0 - f.x : f.x) * (z == 0 ? 1.0 - f.y : f.y);
			height += imageLoad(state_texture, cell).r * weight;
		}
	}
	output_data.heights[index] = height;
}
