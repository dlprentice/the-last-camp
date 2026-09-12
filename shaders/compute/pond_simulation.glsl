#[compute]
#version 450

// Original damped finite-difference wave/foam solver. Native RD plumbing follows
// https://github.com/godotengine/godot-demo-projects/tree/master/compute/texture
// No FFT, pressure projection, volume conservation or overturning fluid is implied.
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;
layout(rgba32f, set = 0, binding = 0) uniform readonly image2D previous_state;
layout(rgba32f, set = 0, binding = 1) uniform writeonly image2D next_state;
layout(std430, set = 0, binding = 2) readonly buffer Contacts {
	vec4 impulses[32]; // World XZ, radius, signed velocity kick.
	vec4 wake_starts[8];
	vec4 wake_ends[8];
} contacts;
layout(push_constant, std430) uniform Parameters {
	vec4 domain; // Origin XZ, size XZ.
	vec4 physics; // Fixed dt, wave speed squared, velocity/height damping rates.
	vec4 limits; // Foam decay, height limit, velocity limit, reserved.
	vec4 flow_cell; // Surface drift XZ, cell dimensions XZ (metres).
	vec4 counts; // Resolution, impulses, wakes, reserved; encoded as floats.
} p;

bool in_grid(ivec2 cell) {
	int size = int(p.counts.x);
	return all(greaterThanEqual(cell, ivec2(0))) && all(lessThan(cell, ivec2(size)));
}

vec4 state_at(ivec2 cell) {
	return in_grid(cell) ? imageLoad(previous_state, cell) : vec4(0.0);
}

// Zero normal derivative across a solid face: a reflecting Neumann boundary.
float neighbour_height(ivec2 cell, float centre) {
	vec4 value = state_at(cell);
	return value.a > 0.5 ? value.r : centre;
}

float advected_foam(vec2 backtrace, ivec2 origin, float fallback) {
	ivec2 base = ivec2(floor(backtrace));
	vec2 f = fract(backtrace);
	float weighted = 0.0;
	float weight_sum = 0.0;
	for (int z = 0; z < 2; z++) {
		for (int x = 0; x < 2; x++) {
			ivec2 tap = base + ivec2(x, z);
			vec4 value = state_at(tap);
			// A diagonal bilinear tap must not cross the corner of two solids.
			if (tap.x != origin.x && tap.y != origin.y &&
				(state_at(ivec2(tap.x, origin.y)).a < 0.5 || state_at(ivec2(origin.x, tap.y)).a < 0.5)) value.a = 0.0;
			float weight = (x == 0 ? 1.0 - f.x : f.x) * (z == 0 ? 1.0 - f.y : f.y) * value.a;
			weighted += value.b * weight;
			weight_sum += weight;
		}
	}
	return weight_sum > 0.0001 ? weighted / weight_sum : fallback;
}

float gaussian(vec2 offset, float radius) {
	float q = dot(offset, offset) / (radius * radius);
	return q < 16.0 ? exp(-0.5 * q) : 0.0;
}

void main() {
	ivec2 cell = ivec2(gl_GlobalInvocationID.xy);
	if (!in_grid(cell)) return;
	vec4 old = imageLoad(previous_state, cell);
	if (old.a < 0.5) {
		imageStore(next_state, cell, vec4(0.0));
		return;
	}
	float dt = p.physics.x;
	vec2 spacing = p.flow_cell.zw;
	float left = neighbour_height(cell + ivec2(-1, 0), old.r);
	float right = neighbour_height(cell + ivec2(1, 0), old.r);
	float down = neighbour_height(cell + ivec2(0, -1), old.r);
	float up = neighbour_height(cell + ivec2(0, 1), old.r);
	float laplacian = (left + right - 2.0 * old.r) / (spacing.x * spacing.x)
		+ (down + up - 2.0 * old.r) / (spacing.y * spacing.y);
	vec2 slope = vec2(right - left, up - down) / (2.0 * spacing);
	vec2 world = p.domain.xy + (vec2(cell) + 0.5) * spacing;
	float kick = 0.0;
	for (int i = 0; i < int(p.counts.y); i++) {
		vec4 impulse = contacts.impulses[i];
		vec2 offset = world - impulse.xy;
		float q = dot(offset, offset) / (impulse.z * impulse.z);
		// Mexican-hat profile integrates to zero on an unbounded surface.
		if (q < 16.0) kick += impulse.w * (1.0 - 0.5 * q) * exp(-0.5 * q);
	}
	for (int i = 0; i < int(p.counts.z); i++) {
		vec4 start = contacts.wake_starts[i];
		vec2 end = contacts.wake_ends[i].xy;
		kick += start.w * (gaussian(world - end, start.z) - gaussian(world - start.xy, start.z));
	}
	float velocity = (old.g + dt * p.physics.y * laplacian) * exp(-p.physics.z * dt) + kick;
	velocity = clamp(velocity, -p.limits.z, p.limits.z);
	float height = (old.r + dt * velocity) * exp(-p.physics.w * dt);
	// Saturation is a safety bound for violent contacts, not the normal integrator.
	if (abs(height) > p.limits.y) {
		height = clamp(height, -p.limits.y, p.limits.y);
		if (height * velocity > 0.0) velocity = 0.0;
	}
	vec2 drift = (p.flow_cell.xy - slope * 0.18) * dt / spacing;
	// Less than half a cell per step prevents transport through a thin obstacle.
	float drift_length = length(drift);
	if (drift_length > 0.45) drift *= 0.45 / drift_length;
	vec2 backtrace = vec2(cell) - drift;
	if (state_at(ivec2(floor(backtrace + 0.5))).a < 0.5) backtrace = vec2(cell);
	float foam = advected_foam(backtrace, cell, old.b) * exp(-p.limits.x * dt);
	float agitation = max(abs(velocity) - 0.10, 0.0) * 2.2 + max(abs(laplacian) - 0.18, 0.0) * 0.08;
	foam = clamp(foam + agitation * dt + min(abs(kick), 1.0) * 0.22, 0.0, 1.0);
	imageStore(next_state, cell, vec4(height, velocity, foam, 1.0));
}
