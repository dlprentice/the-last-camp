shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;

// Forest floor: four PBR sets (grass, leaf litter, wet mud, trodden path)
// height-blended by a vertex mask, sampled at two scales to hide tiling, with
// parallax occlusion on the near ground, shoreline wetness and underwater
// caustics. Each set uses an ORM texture: R = AO, G = roughness, B = height.

#include "res://shaders/inc/noise.gdshaderinc"
#include "res://shaders/inc/common.gdshaderinc"

global uniform float water_level;
global uniform float scene_wetness;
global uniform vec3 sun_direction;
global uniform vec3 sun_color;
global uniform float daylight;
global uniform vec3 fire_position;
global uniform float fire_intensity;

group_uniforms Grass;
uniform sampler2D grass_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D grass_orm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
group_uniforms Litter;
uniform sampler2D litter_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D litter_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D litter_orm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
group_uniforms Mud;
uniform sampler2D mud_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D mud_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D mud_orm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
group_uniforms Path;
uniform sampler2D path_albedo : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D path_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D path_orm : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

group_uniforms Detail;
uniform sampler2D noise_tex : hint_default_white, repeat_enable;
uniform sampler2D detail_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D caustics_tex : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform float tile_near = 0.5;
uniform float tile_far = 0.09;
uniform float far_blend_start = 6.0;
uniform float far_blend_end = 45.0;
uniform float blend_depth : hint_range(0.02, 1.0) = 0.28;
uniform int parallax_steps : hint_range(0, 32) = 16;
// Relief of the full height range in metres: pebbles and clods, not boulders.
uniform float parallax_depth : hint_range(0.0, 0.2) = 0.03;
uniform float parallax_fade = 22.0;
uniform float wet_darkening : hint_range(0.0, 1.0) = 0.42;
uniform float caustic_strength : hint_range(0.0, 3.0) = 0.9;
uniform float ao_strength : hint_range(0.0, 1.0) = 0.85;

varying vec3 world_pos;
varying vec3 world_nrm;
varying vec4 mask;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_nrm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	mask = COLOR;
}

// Height weights: the material standing proudest wins locally, softened by
// blend_depth so the transition follows blade tips and pebbles.
vec4 height_weights(vec4 m, vec4 h) {
	vec4 w = m * (h * 0.8 + 0.2);
	float peak = max(max(w.x, w.y), max(w.z, w.w));
	w = max(w - (peak - blend_depth), vec4(0.0));
	return w / max(w.x + w.y + w.z + w.w, 1e-4);
}

// Grass blades are drawn as geometry above this surface, so the turf's own
// height field only needs a hint of relief; pebbles and litter keep theirs.
vec4 sample_heights(vec2 uv, vec2 dx, vec2 dy) {
	return vec4(
			textureGrad(grass_orm, uv, dx, dy).b * 0.35 + 0.3,
			textureGrad(litter_orm, uv, dx, dy).b,
			textureGrad(mud_orm, uv, dx, dy).b,
			textureGrad(path_orm, uv, dx, dy).b);
}

// Parallax occlusion over the mask-blended height field. The offset is
// limited at grazing angles (no division by a vanishing view.z) and the depth
// eases out there, so pebbles never smear into streaks toward the camera;
// the step count rises with the angle instead so the relief stays crisp.
vec2 parallax(vec2 uv, vec3 view_ts, vec4 m, vec2 dx, vec2 dy, float depth_scale) {
	if (parallax_steps <= 0 || depth_scale <= 1e-4) {
		return uv;
	}
	float grazing = 1.0 - clamp(view_ts.z, 0.0, 1.0);
	depth_scale *= smoothstep(0.1, 0.35, view_ts.z);
	int step_count = int(clamp(mix(float(parallax_steps) * 0.6, float(parallax_steps) * 2.0, grazing), 4.0, 32.0));
	float steps = float(step_count);
	float layer_depth = 1.0 / steps;
	vec2 delta = (view_ts.xy / max(view_ts.z, 0.5)) * depth_scale * tile_near / steps;
	vec2 cur = uv;
	float cur_layer = 0.0;
	float cur_h = 1.0 - dot(m, sample_heights(cur, dx, dy));
	float prev_h = cur_h;
	for (int i = 0; i < 32; i++) {
		if (i >= step_count || cur_layer >= cur_h) {
			break;
		}
		cur -= delta;
		cur_layer += layer_depth;
		prev_h = cur_h;
		cur_h = 1.0 - dot(m, sample_heights(cur, dx, dy));
	}
	// Linear interpolation between the last two samples.
	float after = cur_h - cur_layer;
	float before = prev_h - (cur_layer - layer_depth);
	float t = clamp(after / max(after - before, 1e-4), 0.0, 1.0);
	return cur + delta * t;
}

void fragment() {
	vec3 to_cam = CAMERA_POSITION_WORLD - world_pos;
	float dist = length(to_cam);
	vec3 view_dir = to_cam / max(dist, 1e-4);

	if (reflection_clips(CAMERA_VISIBLE_LAYERS, world_pos.y, water_level, 0.02)) {
		discard;
	}

	// Tangent frame for a top-down projection: u along +X, image top towards -Z.
	vec3 n = normalize(world_nrm);
	vec3 t = normalize(vec3(1.0, 0.0, 0.0) - n * n.x);
	vec3 b = normalize(cross(n, t));

	vec2 uv_near = world_pos.xz * tile_near;
	vec2 uv_far = world_pos.xz * tile_far;
	vec2 dx_n = dFdx(uv_near);
	vec2 dy_n = dFdy(uv_near);

	vec4 m = mask / max(mask.r + mask.g + mask.b + mask.a, 1e-4);

	float parallax_amount = parallax_depth * (1.0 - smoothstep(parallax_fade * 0.5, parallax_fade, dist));
	vec3 view_ts = vec3(dot(view_dir, t), dot(view_dir, b), dot(view_dir, n));
	uv_near = parallax(uv_near, view_ts, m, dx_n, dy_n, parallax_amount);

	vec4 orm_g = textureGrad(grass_orm, uv_near, dx_n, dy_n);
	vec4 orm_l = textureGrad(litter_orm, uv_near, dx_n, dy_n);
	vec4 orm_m = textureGrad(mud_orm, uv_near, dx_n, dy_n);
	vec4 orm_p = textureGrad(path_orm, uv_near, dx_n, dy_n);
	vec4 w = height_weights(m, vec4(orm_g.b, orm_l.b, orm_m.b, orm_p.b));

	// The large-scale tiling only matters at distance and the per-layer
	// normal maps only up close; the terrain covers a third of most frames,
	// so each region fetches only the maps it can show (measured 6-7 ms for
	// this shader at High before the split).
	float far_mix = smoothstep(far_blend_start, far_blend_end, dist);
	bool want_far = far_mix > 0.001;
	bool want_normal = far_mix < 0.999;

	vec3 albedo = vec3(0.0);
	vec3 albedo_far = vec3(0.0);
	vec3 nrm_ts = vec3(0.0);
	float rough = 0.0;
	float ao = 0.0;
	if (w.x > 0.002) {
		albedo += textureGrad(grass_albedo, uv_near, dx_n, dy_n).rgb * w.x;
		if (want_far) {
			albedo_far += texture(grass_albedo, uv_far).rgb * w.x;
		}
		if (want_normal) {
			nrm_ts += (textureGrad(grass_normal, uv_near, dx_n, dy_n).rgb * 2.0 - 1.0) * w.x;
		}
		rough += orm_g.g * w.x;
		ao += orm_g.r * w.x;
	}
	if (w.y > 0.002) {
		albedo += textureGrad(litter_albedo, uv_near, dx_n, dy_n).rgb * w.y;
		if (want_far) {
			albedo_far += texture(litter_albedo, uv_far).rgb * w.y;
		}
		if (want_normal) {
			nrm_ts += (textureGrad(litter_normal, uv_near, dx_n, dy_n).rgb * 2.0 - 1.0) * w.y;
		}
		rough += orm_l.g * w.y;
		ao += orm_l.r * w.y;
	}
	if (w.z > 0.002) {
		albedo += textureGrad(mud_albedo, uv_near, dx_n, dy_n).rgb * w.z;
		if (want_far) {
			albedo_far += texture(mud_albedo, uv_far).rgb * w.z;
		}
		if (want_normal) {
			nrm_ts += (textureGrad(mud_normal, uv_near, dx_n, dy_n).rgb * 2.0 - 1.0) * w.z;
		}
		rough += orm_m.g * w.z;
		ao += orm_m.r * w.z;
	}
	if (w.w > 0.002) {
		albedo += textureGrad(path_albedo, uv_near, dx_n, dy_n).rgb * w.w;
		if (want_far) {
			albedo_far += texture(path_albedo, uv_far).rgb * w.w;
		}
		if (want_normal) {
			nrm_ts += (textureGrad(path_normal, uv_near, dx_n, dy_n).rgb * 2.0 - 1.0) * w.w;
		}
		rough += orm_p.g * w.w;
		ao += orm_p.r * w.w;
	}
	if (!want_normal) {
		nrm_ts = vec3(0.0, 0.0, 1.0);
	}

	// Distance blend towards the large-scale tiling, plus macro variation.
	albedo = mix(albedo, albedo_far * (0.85 + 0.15 * luminance(albedo) / max(luminance(albedo_far), 1e-3)), far_mix * 0.7);
	vec4 macro = texture(noise_tex, world_pos.xz * 0.011);
	vec4 macro2 = texture(noise_tex, world_pos.xz * 0.037 + vec2(3.1, 7.7));
	albedo *= 0.80 + 0.40 * macro.r;
	// Damp turf below the blades shares their colour and never reads as a
	// pale carpet through gaps in the stand.
	albedo *= mix(vec3(1.0), vec3(0.50, 0.68, 0.46), w.x);
	albedo *= mix(vec3(1.0), vec3(1.06, 1.0, 0.9), (macro2.g - 0.5) * 0.6);
	// The same habitat-blended PBR turf/litter continues onto the hills.
	// Mip filtering and macro variation handle scale; a flat distant colour
	// field would erase the turf texture exactly where geometric detail thins.
	rough = clamp(rough + (macro.b - 0.5) * 0.08, 0.65, 1.0);
	rough = mix(rough, max(rough, 0.88), w.x);

	// Detail normal for close-up crispness.
	float detail_w = 1.0 - smoothstep(3.0, 14.0, dist);
	vec3 dn = texture(detail_normal, world_pos.xz * 3.1).rgb * 2.0 - 1.0;
	nrm_ts.xy += dn.xy * 0.20 * detail_w;
	nrm_ts.xy *= 1.0 - far_mix * 0.6;
	nrm_ts = normalize(vec3(nrm_ts.xy, max(nrm_ts.z, 0.2)));

	// Shoreline wetness and standing water darkening.
	float above = world_pos.y - water_level;
	float wet = 1.0 - smoothstep(0.0, 1.1, above);
	wet = max(wet, w.z * 0.5);
	wet = max(wet, scene_wetness * (0.60 + macro.g * 0.4) * clamp(n.y * 1.4, 0.0, 1.0));
	albedo *= 1.0 - wet * wet_darkening;
	// Only compacted, relatively level earth gathers a shallow film. Litter
	// and turf remain broken and rough instead of turning into a glass floor.
	float basin = smoothstep(0.46, 0.70, texture(noise_tex, world_pos.xz * 0.22).g);
	float pooled = smoothstep(0.35, 0.95, scene_wetness) * basin
		* smoothstep(0.92, 0.995, n.y) * clamp(w.z + w.w * 0.8, 0.0, 1.0);
	rough = mix(rough, 0.32, wet * 0.85);
	rough = mix(rough, 0.11, pooled);
	nrm_ts.xy *= 1.0 - pooled * 0.85;
	nrm_ts = normalize(nrm_ts);

	vec3 emit = vec3(0.0);
	// Underwater caustics, strongest in shallow water and full sun.
	if (above < 0.0) {
		float depth = -above;
		vec2 c1 = world_pos.xz * 0.33 + vec2(TIME * 0.021, TIME * 0.013);
		vec2 c2 = world_pos.xz * 0.27 - vec2(TIME * 0.017, -TIME * 0.011);
		float ca = pow(texture(caustics_tex, c1).r * texture(caustics_tex, c2).r, 1.35);
		float fall = exp(-depth * 0.70) * clamp(sun_direction.y * 4.0, 0.0, 1.0);
		emit = albedo * ca * caustic_strength * fall * sun_color * 1.5;
	}

	vec3 n_world = normalize(t * nrm_ts.x + b * nrm_ts.y + n * nrm_ts.z);

	float fire_d = length(world_pos - fire_position);
	float warmth = exp(-fire_d * 0.38) * fire_intensity;
	emit += albedo * vec3(1.0, 0.42, 0.1) * warmth * 0.08;
	EMISSION = emit;

	ALBEDO = albedo;
	ROUGHNESS = rough;
	SPECULAR = mix(0.25, 0.5, wet);
	CLEARCOAT = pooled * 0.7;
	CLEARCOAT_ROUGHNESS = 0.12;
	AO = mix(1.0, ao, ao_strength);
	AO_LIGHT_AFFECT = 0.35;
	NORMAL = view_normal_from_world(VIEW_MATRIX, n_world);
}
