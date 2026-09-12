// Shared hashing and noise helpers. All hashes are sin-free for stability
// across GPUs; noise functions return roughly [0, 1].
// Hash without Sine variants: Copyright (c) 2014 David Hoskins, MIT.
// Original: https://www.shadertoy.com/view/4djSRW
// Full notice: THIRD_PARTY_NOTICES.md at the project root.

float hash11(float p) {
	p = fract(p * 0.1031);
	p *= p + 33.33;
	p *= p + p;
	return fract(p);
}

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float hash13(vec3 p3) {
	p3 = fract(p3 * 0.1031);
	p3 += dot(p3, p3.zyx + 31.32);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

vec3 hash33(vec3 p3) {
	p3 = fract(p3 * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yxz + 33.33);
	return fract((p3.xxy + p3.yxx) * p3.zyx);
}

// Gradient noise (Perlin-style) in 2D, quintic interpolation.
float gnoise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	vec2 ga = hash22(i + vec2(0.0, 0.0)) * 2.0 - 1.0;
	vec2 gb = hash22(i + vec2(1.0, 0.0)) * 2.0 - 1.0;
	vec2 gc = hash22(i + vec2(0.0, 1.0)) * 2.0 - 1.0;
	vec2 gd = hash22(i + vec2(1.0, 1.0)) * 2.0 - 1.0;
	float va = dot(ga, f - vec2(0.0, 0.0));
	float vb = dot(gb, f - vec2(1.0, 0.0));
	float vc = dot(gc, f - vec2(0.0, 1.0));
	float vd = dot(gd, f - vec2(1.0, 1.0));
	float n = mix(mix(va, vb, u.x), mix(vc, vd, u.x), u.y);
	return n * 0.7 + 0.5;
}

float fbm2(vec2 p, int octaves) {
	float sum = 0.0;
	float amp = 0.5;
	float norm = 0.0;
	mat2 rot = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
	for (int i = 0; i < octaves; i++) {
		sum += gnoise2(p) * amp;
		norm += amp;
		p = rot * p * 2.03 + vec2(7.3, 1.9);
		amp *= 0.5;
	}
	return sum / max(norm, 1e-4);
}

// Value noise in 3D (cheap, used for fire and smoke turbulence).
float vnoise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float a = hash13(i + vec3(0.0, 0.0, 0.0));
	float b = hash13(i + vec3(1.0, 0.0, 0.0));
	float c = hash13(i + vec3(0.0, 1.0, 0.0));
	float d = hash13(i + vec3(1.0, 1.0, 0.0));
	float e = hash13(i + vec3(0.0, 0.0, 1.0));
	float f2 = hash13(i + vec3(1.0, 0.0, 1.0));
	float g = hash13(i + vec3(0.0, 1.0, 1.0));
	float h = hash13(i + vec3(1.0, 1.0, 1.0));
	return mix(mix(mix(a, b, u.x), mix(c, d, u.x), u.y),
			mix(mix(e, f2, u.x), mix(g, h, u.x), u.y), u.z);
}

float fbm3(vec3 p, int octaves) {
	float sum = 0.0;
	float amp = 0.5;
	float norm = 0.0;
	for (int i = 0; i < octaves; i++) {
		sum += vnoise3(p) * amp;
		norm += amp;
		p = p * 2.07 + vec3(3.1, 1.7, 5.3);
		amp *= 0.5;
	}
	return sum / max(norm, 1e-4);
}

// Interleaved gradient noise: a stable screen-space dither pattern.
float ign(vec2 pixel) {
	return fract(52.9829189 * fract(dot(pixel, vec2(0.06711056, 0.00583715))));
}
