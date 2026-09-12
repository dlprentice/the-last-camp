shader_type spatial;
render_mode blend_mix, depth_draw_always, specular_schlick_ggx, diffuse_burley;
#ifdef BELOW_SURFACE
render_mode cull_front;
#else
render_mode cull_back;
#endif

// Pond surface. Refraction and absorption come from the scene colour/depth
// behind the surface; reflection from a planar mirror viewport (or, when it is
// disabled, from Godot's own environment specular through the PBR path).
// A `//PLANAR` marker line is replaced at load time to switch modes.

//PLANAR_RENDER_MODE

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform vec3 sun_direction;
global uniform vec3 sun_color;
global uniform vec2 wind_direction;
global uniform float wind_strength;
global uniform vec3 player_position;
global uniform float water_level;
global uniform float rainfall;
global uniform float daylight;

group_uniforms Waves;
uniform sampler2D normal_a : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D normal_b : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float ripple_scale_a = 0.11;
uniform float ripple_scale_b = 0.23;
uniform float ripple_speed = 0.035;
uniform float normal_strength : hint_range(0.0, 2.0) = 0.38;
#include "res://shaders/inc/pond_waves.gdshaderinc"

group_uniforms Body;
uniform vec3 absorption = vec3(0.28, 0.22, 0.30);
uniform vec3 scatter_color : source_color = vec3(0.02, 0.05, 0.035);
uniform float refraction_strength = 0.045;
uniform float edge_fade = 0.12;

group_uniforms Reflection;
uniform bool use_planar = true;
uniform sampler2D reflection_tex : hint_default_black, filter_linear;
uniform sampler2D underwater_reflection_tex : hint_default_black, filter_linear;
uniform sampler2D transmission_tex : hint_default_black, filter_linear;
// Below-water rough facets carry a subdued, filtered echo. This controls only
// the underside; above-water planar reflections stay at 1.0.
uniform float underside_reflection_strength : hint_range(0.0, 1.0) = 0.18;
uniform float reflection_distortion = 0.021;
uniform float reflection_strength : hint_range(0.0, 1.5) = 1.0;
// The mirror image is linear HDR: the sun's aureole in it runs far past the
// tonemapper's white and clipped into a flat, textureless patch on the water.
// Radiance above the knee rolls off so the ripples stay visible in the
// highlight; the glitter term supplies the actual sparkle.
uniform float reflection_knee : hint_range(0.5, 8.0) = 2.0;
uniform float reflection_rolloff : hint_range(0.05, 2.0) = 0.5;

group_uniforms Foam;
uniform sampler2D foam_tex : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float foam_depth = 0.32;

group_uniforms Interaction;
uniform float wading_strength = 0.0;
uniform bool debug_reflection = false;
// 1 drops the PBR sun highlight, 2 drops the sparkle: isolates a clipped patch.
uniform int highlight_debug = 0;
uniform vec4 ripples[8]; // contact x/z, birth time, amplitude
uniform float ripple_clock = 0.0;

uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_texture : hint_depth_texture, filter_nearest;

varying vec3 world_pos;
varying vec3 wave_normal;
varying vec3 interaction_bend;

void vertex() {
	vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	vec3 n;
	vec3 disp = pond_wave(wp.xz, TIME, n);
	vec3 base_normal;
	pond_base_wave(wp.xz, TIME, base_normal);
	vec2 bend = n.xz / max(n.y, 0.1) - base_normal.xz / max(base_normal.y, 0.1);
	interaction_bend = vec3(bend.x, 0.0, bend.y);
	wp += disp;
	world_pos = wp;
	wave_normal = n;
	VERTEX = (inverse(MODEL_MATRIX) * vec4(wp, 1.0)).xyz;
	NORMAL = n;
}

float linear_depth(vec2 uv, mat4 inv_proj) {
	float d = texture(depth_texture, uv).r;
	vec4 ndc = vec4(uv * 2.0 - 1.0, d, 1.0);
	vec4 view = inv_proj * ndc;
	return -view.z / view.w;
}

// Unpolarized dielectric Fresnel. eta is n_incident / n_transmitted;
// Snell's law gives total internal reflection when sin(theta_t)^2 >= 1.
// Water/air: eta=1.333, critical angle=48.61 degrees. See PBRT 4e, 9.3.
float dielectric_reflectance(float cosine, float eta) {
	float sin2_t = eta * eta * max(0.0, 1.0 - cosine * cosine);
	if (sin2_t >= 1.0) { return 1.0; }
	float ct = sqrt(max(0.0, 1.0 - sin2_t));
	float rs = (eta * cosine - ct) / max(eta * cosine + ct, 1e-5);
	float rp = (cosine - eta * ct) / max(cosine + eta * ct, 1e-5);
	return 0.5 * (rs * rs + rp * rp);
}

vec2 rain_ripples(vec2 p, float t) {
	vec2 grid = p * 2.6;
	vec2 cell = floor(grid);
	vec2 normal = vec2(0.0);
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 id = cell + vec2(float(x), float(y));
			float clock = t * 0.85 + hash33(vec3(id, 16.2)).z;
			vec3 h = hash33(vec3(id, floor(clock) + 16.2));
			float phase = fract(clock);
			vec2 radial = grid - (id + 0.18 + h.xy * 0.64);
			float d = length(radial);
			float ring = d - phase * 1.15;
			float wave = sin(ring * 29.0) * exp(-ring * ring * 55.0);
			float strength = (1.0 - phase) * smoothstep(0.0, 0.10, phase) * step(h.z, rainfall);
			normal += radial / max(d, 0.02) * wave * strength * 0.15;
		}
	}
	return normal;
}

void fragment() {
	// Let the near plane clip the actual surface. A radial cutout around the
	// lens exposed a circular patch of dry-looking bed during water crossings.
	vec2 wind_dir = normalize(wind_direction + vec2(1e-4));
	float t = TIME * ripple_speed;
	vec2 uv_a = world_pos.xz * ripple_scale_a + wind_dir * t;
	vec2 uv_b = world_pos.xz * ripple_scale_b - vec2(wind_dir.y, -wind_dir.x) * t * 0.7 + vec2(0.37, 0.11);
	vec2 uv_c = world_pos.xz * 0.61 + wind_dir * t * 1.9;
	vec3 na = texture(normal_a, uv_a).rgb * 2.0 - 1.0;
	vec3 nb = texture(normal_b, uv_b).rgb * 2.0 - 1.0;
	vec3 nc = texture(normal_a, uv_c).rgb * 2.0 - 1.0;
	float wind_amount = 0.35 + 0.65 * clamp(wind_strength, 0.0, 1.5);
	vec2 detail = (na.xy * 0.6 + nb.xy * 0.45 + nc.xy * 0.2) * normal_strength * wind_amount;
	for (int i = 0; i < 8; i++) {
		if (pond_interaction_enabled) { break; }
		float age = ripple_clock - ripples[i].z;
		if (age < 0.0 || age > 5.0 || ripples[i].w <= 0.0) { continue; }
		vec2 radial = world_pos.xz - ripples[i].xy;
		float dist = length(radial);
		float front = dist - age * 1.45;
		float envelope = exp(-front * front * 5.0) * exp(-age * 0.75) * smoothstep(0.0, 0.12, age);
		detail += radial / max(dist, 0.001) * sin(front * 23.0) * envelope * ripples[i].w * 0.26;
	}

	// Expanding rings from the player wading through the shallows.
	vec2 to_player = world_pos.xz - player_position.xz;
	float pd = length(to_player);
	if (!pond_interaction_enabled && wading_strength > 0.001 && pd < 6.0) {
		float ring = sin(pd * 9.0 - TIME * 7.0) * exp(-pd * 0.9) * wading_strength;
		detail += (to_player / max(pd, 1e-3)) * ring * 0.25;
	}

	// Tangent frame for a horizontal surface: u along +X, image top towards -Z.
	vec3 n_world = normalize(vec3(wave_normal.x + detail.x, 1.0, wave_normal.z - detail.y));
	if (rainfall > 0.001) {
		float rain_detail = 1.0 - smoothstep(12.0, 36.0, distance(CAMERA_POSITION_WORLD, world_pos));
		n_world.xz += rain_ripples(world_pos.xz, TIME) * rainfall * rain_detail;
		n_world = normalize(n_world);
	}
	vec3 view_dir = normalize(CAMERA_POSITION_WORLD - world_pos);
	// Reflection/refraction imagery must bend with the same residual field,
	// not only the fine scrolling normals. Geometry normals already include
	// this slope, so use it only for the optical sample coordinates here.
	vec3 bend_view = mat3(VIEW_MATRIX) * interaction_bend;
	vec2 optical_detail = detail + vec2(bend_view.x, -bend_view.y);

	// Seen from below: the surface is a wobbling silver ceiling that lets the
	// bright sky through near straight up (Snell's window) and mirrors the
	// murk at grazing angles (total internal reflection).
	if (!FRONT_FACING) {
		vec3 n_below = -n_world;
		float up_cos = clamp(dot(view_dir, n_below), 0.0, 1.0);
		float reflected_fraction = dielectric_reflectance(up_cos, 1.333);
		// The world above is refracted hard through the moving surface.
		// Reproject a point along the Snell-refracted ray into the air view.
		// Screen-space depth estimates the length beyond the interface; far
		// sky rays use a long path and nearby deck/pile rays keep parallax.
		vec3 transmitted_ray = refract(-view_dir, n_below, 1.333);
		float surface_depth = -(VIEW_MATRIX * vec4(world_pos, 1.0)).z;
		float air_length = clamp(linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX) - surface_depth, 1.0, 100.0);
		vec3 air_point = world_pos + transmitted_ray * air_length;
		vec4 air_clip = PROJECTION_MATRIX * VIEW_MATRIX * vec4(air_point, 1.0);
		vec2 below_uv = air_clip.xy / max(air_clip.w, 0.001) * 0.5 + 0.5;
		below_uv = clamp(below_uv, vec2(0.001), vec2(0.999));
		vec3 through = texture(screen_texture, below_uv).rgb;
		vec3 ceiling = vec3(0.06, 0.18, 0.17) * (0.6 + 0.4 * clamp(sun_direction.y * 3.0, 0.0, 1.0));
		if (use_planar) {
			vec2 mirror_uv = clamp(vec2(SCREEN_UV.x, 1.0 - SCREEN_UV.y) + optical_detail * 0.15,
				vec2(0.001), vec2(0.999));
			ceiling = texture(underwater_reflection_tex, mirror_uv).rgb;
			// Render the air halfspace without underwater world fog, so Snell's
			// window contains the actual deck and sky instead of a teal disc.
			through = texture(transmission_tex, below_uv).rgb;
		}
		// Unresolved rough facets turn the ceiling into broad rippling light.
		// Native fog integrates eye-to-surface distance for this whole mixture.
		float sun_height = smoothstep(-0.10, 0.50, sun_direction.y);
		vec3 canopy = mix(vec3(0.006, 0.018, 0.016), vec3(0.18, 0.29, 0.20), sun_height);
		canopy *= clamp(0.9 + detail.x * 2.0 - detail.y * 1.5, 0.55, 1.4);
		// Close to horizontal, retain the post's dark reflected silhouette
		// through the split view. Steeper angles keep the accepted rough canopy.
		float grazing = 1.0 - smoothstep(0.18, 0.42, abs(view_dir.y));
		ceiling = mix(canopy, ceiling, mix(underside_reflection_strength, 1.0, grazing));
		float facet = clamp(dot(n_below, normalize(view_dir + normalize(sun_direction * vec3(1.0, -1.0, 1.0)))), 0.0, 1.0);
		vec3 glint_below = sun_color * (pow(facet, 90.0) * 0.25 + pow(facet, 350.0) * 0.7);
		ALBEDO = vec3(0.0);
		EMISSION = mix(through, ceiling, reflected_fraction) + glint_below;
		ALPHA = 1.0;
		ROUGHNESS = 0.08;
		SPECULAR = 0.0;
		NORMAL = view_normal_from_world(VIEW_MATRIX, n_below);
	} else {
		float cos_theta = clamp(dot(n_world, view_dir), 0.0, 1.0);
		float fresnel = dielectric_reflectance(cos_theta, 1.0 / 1.333);

		// Depth of the scene behind the surface, in view space.
		float surface_depth = -(VIEW_MATRIX * vec4(world_pos, 1.0)).z;
		float scene_depth = linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
		float thickness = max(scene_depth - surface_depth, 0.0);

		// Refraction, rejected where the distorted sample lands in front of the water.
		vec2 refr_uv = SCREEN_UV + optical_detail * refraction_strength * clamp(thickness, 0.0, 1.5);
		refr_uv = clamp(refr_uv, vec2(0.001), vec2(0.999));
		float refr_depth = linear_depth(refr_uv, INV_PROJECTION_MATRIX);
		if (refr_depth < surface_depth) {
			refr_uv = SCREEN_UV;
			refr_depth = scene_depth;
		}
		float refr_thickness = max(refr_depth - surface_depth, 0.0);
		vec3 behind = texture(screen_texture, refr_uv).rgb;

		// Beer-Lambert absorption along the (approximate) underwater path.
		float path = refr_thickness * (1.0 + 1.0 / max(cos_theta, 0.2)) * 0.6;
		vec3 transmittance = exp(-absorption * path);
		vec3 body = scatter_color * (1.0 - transmittance) * (0.6 + 0.4 * clamp(sun_direction.y * 3.0, 0.0, 1.0));
		vec3 refracted = behind * transmittance + body * 2.2;

		// Planar reflection (mirror camera renders with a flipped up axis).
		vec3 reflected = vec3(0.0);
		if (use_planar) {
			vec2 refl_uv = vec2(SCREEN_UV.x, 1.0 - SCREEN_UV.y) + optical_detail * reflection_distortion;
			refl_uv = clamp(refl_uv, vec2(0.001), vec2(0.999));
			reflected = texture(reflection_tex, refl_uv).rgb * reflection_strength;
			float refl_lum = dot(reflected, vec3(0.2126, 0.7152, 0.0722));
			if (refl_lum > reflection_knee) {
				float over = refl_lum - reflection_knee;
				reflected *= (reflection_knee + over / (1.0 + over * reflection_rolloff)) / refl_lum;
			}
		}

		// Foam on the shoreline, broken up by the ripple pattern.
		vec2 foam_uv = world_pos.xz * 0.35 + wind_dir * TIME * 0.02 + detail * 0.4;
		float foam_mask = 1.0 - smoothstep(0.0, foam_depth, thickness);
		float foam = texture(foam_tex, foam_uv).r * texture(foam_tex, foam_uv * 1.9 + 0.3).r * 2.5;
		foam = smoothstep(0.45, 0.9, foam) * foam_mask;
		// Residual foam is born at impacts/steep disturbed water, transported
		// and dissipated by the same persistent field that moves the surface.
		float contact_foam = pond_interaction(world_pos.xz).b;
		foam = max(foam, contact_foam * (0.55 + 0.45 * texture(foam_tex, world_pos.xz * 2.3).r));

		vec3 col = mix(refracted, reflected, fresnel * float(use_planar));
		// The composite is emitted radiance, so the foam must carry the daylight
		// itself: a constant grey glowed as white splotches on the night shore.
		col = mix(col, vec3(0.62, 0.66, 0.62) * mix(0.04, 1.0, daylight), foam * 0.5);

		// Sun glitter: tight highlight along the sun path, broken into drifting
		// points by a fine speckle so the path sparkles instead of filling in
		// as one clipped patch (the mip-filtered ripple normals alone are too
		// smooth to resolve individual facets at any distance).
		vec3 h = normalize(view_dir + normalize(sun_direction));
		float glint = pow(clamp(dot(n_world, h), 0.0, 1.0), 700.0) * (1.0 - foam);
		float speck = gnoise2(world_pos.xz * 14.0 + vec2(TIME * 0.9, -TIME * 0.6));
		speck *= gnoise2(world_pos.xz * 23.0 - vec2(TIME * 0.5, TIME * 0.8) + 4.7);
		speck = smoothstep(0.40, 0.60, speck) * 3.0;
		vec3 glitter = sun_color * glint * speck * 3.0 * clamp(sun_direction.y * 8.0 + 0.2, 0.0, 1.0);
		if (highlight_debug == 2) {
			glitter = vec3(0.0);
		}

		// The composite is already radiance, so it goes out as emission; the PBR
		// path only contributes the sun's specular highlight (and, without planar
		// reflections, the environment's own specular).
		ALBEDO = vec3(0.0);
		EMISSION = col + glitter;
		if (debug_reflection) {
			EMISSION = reflected;
		}
		ALPHA = smoothstep(0.0, edge_fade, thickness);
		// Art-directed direct-light highlight: Godot uses F0 = 0.16 * SPECULAR^2,
		// so 0.25 gives 1% here (0.5 gave 4%). This reduces the white sun patch.
		// The custom reflection/transmission mix above has its own water Fresnel;
		// this PBR lobe is not a claim of a complete physical water BRDF.
		ROUGHNESS = mix(0.08, 0.55, foam);
		SPECULAR = highlight_debug == 1 ? 0.0 : 0.25;
		METALLIC = 0.0;
		NORMAL = view_normal_from_world(VIEW_MATRIX, n_world);
	}
}
