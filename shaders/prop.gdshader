shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// World-space triplanar PBR for logs, rocks, tent canvas and dock planks.
// Each axis gets its own tangent frame so the normal map never flips into a
// dent when the projection changes. Optional moss and a cheap fire-warmth
// term keep props tied to the camp lighting.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 fire_position;
global uniform float fire_intensity;

uniform sampler2D albedo_tex : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_tex : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D orm_tex : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D noise_tex : hint_default_white, repeat_enable;

uniform vec3 tint : source_color = vec3(1.0, 1.0, 1.0);
uniform float tile = 0.55;
uniform float normal_strength : hint_range(0.0, 2.0) = 1.0;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;
uniform float moss_amount : hint_range(0.0, 1.0) = 0.0;
uniform vec3 moss_color : source_color = vec3(0.20, 0.32, 0.12);
uniform float wet_darkening : hint_range(0.0, 1.0) = 0.45;
uniform float wet_band = 0.9;
uniform float ao_strength : hint_range(0.0, 1.0) = 0.85;
uniform float fire_warmth : hint_range(0.0, 2.0) = 0.55;
// Charring: surfaces within char_radius of the fire blacken; ember_glow adds
// pulsing cracks of heat inside the charred zone.
uniform float char_amount : hint_range(0.0, 1.0) = 0.0;
uniform float char_radius = 0.9;
uniform float ember_glow : hint_range(0.0, 10.0) = 0.0;

varying vec3 world_pos;
varying vec3 world_nrm;
// World-space triplanar gave every instance of a rock or log the same texture
// orientation; a per-instance offset from the model origin breaks the twins.
varying vec3 tex_offset;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_nrm = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	vec3 origin = MODEL_MATRIX[3].xyz;
	float seed = fract(sin(dot(floor(origin * 4.0), vec3(12.9898, 78.233, 37.719))) * 43758.5453);
	tex_offset = vec3(seed * 7.3, fract(seed * 5.71) * 6.1, fract(seed * 3.37) * 8.7);
}

vec3 triplanar_weights(vec3 n) {
	vec3 w = pow(abs(n), vec3(4.0));
	return w / max(w.x + w.y + w.z, 1e-4);
}

vec3 sample_albedo(vec3 p, vec3 w) {
	return texture(albedo_tex, p.zy * tile).rgb * w.x
			+ texture(albedo_tex, p.xz * tile).rgb * w.y
			+ texture(albedo_tex, p.xy * tile).rgb * w.z;
}

vec3 sample_orm(vec3 p, vec3 w) {
	return texture(orm_tex, p.zy * tile).rgb * w.x
			+ texture(orm_tex, p.xz * tile).rgb * w.y
			+ texture(orm_tex, p.xy * tile).rgb * w.z;
}

// Whiteout-blend of three axis-oriented tangent-space normals.
vec3 sample_normal(vec3 p, vec3 n, vec3 w) {
	vec3 nx = texture(normal_tex, p.zy * tile).xyz * 2.0 - 1.0;
	vec3 ny = texture(normal_tex, p.xz * tile).xyz * 2.0 - 1.0;
	vec3 nz = texture(normal_tex, p.xy * tile).xyz * 2.0 - 1.0;
	nx = normalize(vec3(nx.z * sign(n.x), nx.y, nx.x));
	ny = normalize(vec3(ny.x, ny.z * sign(n.y), ny.y));
	nz = normalize(vec3(nz.x, nz.y, nz.z * sign(n.z)));
	vec3 blended = nx * w.x + ny * w.y + nz * w.z + n;
	return normalize(mix(n, normalize(blended), normal_strength));
}

void fragment() {
	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.05)) {
		discard;
	}
	vec3 n = normalize(world_nrm);
	vec3 w = triplanar_weights(n);
	vec3 tex_pos = world_pos + tex_offset;
	vec3 albedo = sample_albedo(tex_pos, w) * tint;
	vec3 orm = sample_orm(tex_pos, w);
	vec3 n_world = sample_normal(tex_pos, n, w);

	float patch = texture(noise_tex, world_pos.xz * 0.22 + world_pos.y * 0.08).g;
	float moss = moss_amount * smoothstep(0.28, 0.78, n_world.y * 0.55 + patch * 0.6);
	albedo = mix(albedo, moss_color * (0.75 + 0.4 * patch), moss);
	float rough = mix(orm.g, 0.92, moss);

	float wet = 1.0 - smoothstep(0.0, max(wet_band, 0.001), world_pos.y - water_level);
	wet = max(wet, scene_wetness * (0.55 + max(n.y, 0.0) * 0.45) * (1.0 - metallic));
	albedo *= 1.0 - wet * wet_darkening;
	float film = smoothstep(0.30, 0.95, wet) * (1.0 - moss * 0.65);
	// Below the surface the water itself carries the reflection: a glossy
	// submerged hull or post mirrored the sky through the water as a cyan sheet.
	float submerged = 1.0 - smoothstep(-0.06, 0.02, world_pos.y - water_level);
	film *= 1.0 - submerged;
	rough = mix(rough, 0.78, submerged);
	// Wet stone is glossy, not a mirror: the old 0.14 floor turned boulders chrome.
	rough = mix(rough, 0.42 + patch * 0.12, wet * (0.95 - moss * 0.3));
	n_world = normalize(mix(n_world, n, film * 0.35));

	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.42) * fire_intensity * fire_warmth;

	vec3 emission = albedo * vec3(1.0, 0.42, 0.12) * warmth * 0.12;
	if (char_amount > 0.0) {
		float flat_d = length((world_pos - fire_position).xz);
		float charring = char_amount * (1.0 - smoothstep(char_radius * 0.35, char_radius, flat_d));
		charring *= 0.6 + 0.4 * texture(noise_tex, world_pos.xz * 3.0 + world_pos.y * 2.0).a;
		albedo = mix(albedo, vec3(0.035, 0.03, 0.025), charring);
		rough = mix(rough, 0.9, charring * 0.6);
		float cracks = smoothstep(0.55, 0.85, texture(noise_tex, world_pos.xz * 5.0 + world_pos.yx * 3.0).b);
		float pulse = 0.7 + 0.3 * sin(TIME * 2.7 + world_pos.x * 9.0 + world_pos.z * 7.0);
		emission += vec3(1.0, 0.3, 0.04) * cracks * charring * charring * pulse * ember_glow * clamp(fire_intensity, 0.0, 2.0);
	}

	ALBEDO = albedo;
	ROUGHNESS = clamp(rough, 0.04, 1.0);
	METALLIC = metallic * (1.0 - moss);
	SPECULAR = 0.45;
	// A thin, rough coat: the old 0.65 mirror coat turned wet boulders chrome.
	CLEARCOAT = film * 0.12;
	CLEARCOAT_ROUGHNESS = 0.32 + moss * 0.30;
	AO = mix(1.0, orm.r, ao_strength);
	AO_LIGHT_AFFECT = 0.4;
	NORMAL = view_normal_from_world(VIEW_MATRIX, n_world);
	EMISSION = emission;
}
