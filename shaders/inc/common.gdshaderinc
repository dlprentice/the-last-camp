// Helpers shared by the scene's spatial shaders.

// The planar-reflection camera renders with this extra cull layer enabled so
// shaders can clip everything below the water plane (mirrored geometry would
// otherwise occlude the reflected sky).
const uint REFLECTION_LAYER_BIT = 1u << 19u;
const uint UNDERWATER_REFLECTION_LAYER_BIT = 1u << 18u;

bool in_reflection_pass(uint camera_layers) {
	return (camera_layers & REFLECTION_LAYER_BIT) != 0u;
}

bool in_underwater_reflection_pass(uint camera_layers) {
	return in_reflection_pass(camera_layers) && (camera_layers & UNDERWATER_REFLECTION_LAYER_BIT) != 0u;
}

// The upper mirror keeps the air halfspace; the underside mirror keeps the
// submerged halfspace. Ordinary cameras never set the reflection marker.
bool reflection_clips(uint camera_layers, float height, float level, float margin) {
	if (!in_reflection_pass(camera_layers)) { return false; }
	return in_underwater_reflection_pass(camera_layers) ? height > level + margin : height < level - margin;
}

// Wind field shared by trunks, leaves and grass so canopies move as one.
// Returns a horizontal displacement direction*magnitude in [-1, 1] units.
vec2 wind_field(vec3 world_pos, vec2 wind_dir, float wind_strength, float time) {
	vec2 dir = normalize(wind_dir + vec2(1e-4));
	float along = dot(world_pos.xz, dir);
	float gust = 0.55 + 0.45 * sin(along * 0.055 - time * 1.1);
	gust *= 0.7 + 0.3 * sin(along * 0.021 - time * 0.37 + world_pos.x * 0.013);
	float wave = sin(along * 0.19 - time * 1.7) * 0.6 + sin(along * 0.43 - time * 2.9 + world_pos.z * 0.11) * 0.4;
	return dir * gust * wave * wind_strength;
}

vec3 world_normal_from_view(mat4 inv_view, vec3 view_normal) {
	return normalize((inv_view * vec4(view_normal, 0.0)).xyz);
}

vec3 view_normal_from_world(mat4 view, vec3 world_normal) {
	return normalize((view * vec4(world_normal, 0.0)).xyz);
}

float luminance(vec3 c) {
	return dot(c, vec3(0.2126, 0.7152, 0.0722));
}

// Texture-streaming level for STREAMING_LOD from the screen-space gradients of
// the coordinate the feedback should follow. Same footprint rule as
// BaseMaterial3D: minor axis, limited to 16x anisotropy, offset by the
// engine's 4096-texel reference (log2 = 12).
float streaming_lod(vec2 ddx, vec2 ddy) {
	float dx_sq = dot(ddx, ddx);
	float dy_sq = dot(ddy, ddy);
	float footprint_sq = max(min(dx_sq, dy_sq), max(dx_sq, dy_sq) / 256.0);
	return 0.5 * log2(max(footprint_sq, 1e-30)) + 12.0;
}

// Cut-out leaves far away: a leaf narrower than a pixel flips between kept and
// discarded as wind and the upscaler's jitter move it, which sparkles against
// the sky. Beyond `soft_start` the cut becomes coverage across the alpha's own
// screen gradient, dithered per frame so temporal resolve settles it into a
// soft edge. Near leaves and shadow maps keep the hard cut.
bool leaf_alpha_keep(float alpha, float cutoff, float dist, vec2 frag, float time, bool shadow_pass) {
	float soft = shadow_pass ? 0.0 : smoothstep(14.0, 38.0, dist);
	float width = max(fwidth(alpha), 1e-3) * mix(1.0, 1.6, soft);
	float coverage = clamp((alpha - cutoff) / width + 0.5, 0.0, 1.0);
	vec2 cell = frag + vec2(5.588238, 2.471139) * mod(floor(time * 60.0), 64.0);
	float noise = fract(52.9829189 * fract(dot(cell, vec2(0.06711056, 0.00583715))));
	return coverage > mix(0.5, noise, soft);
}
