class_name QualityPreset
extends RefCounted

## One complete, immutable rendering configuration. Presets are data; the
## `Quality` autoload applies them. Budgets were set from the built-in
## profiler (`--profile`) on an RTX 4060 Laptop at 1080p: shadow-casting
## point lights and the planar mirror dominate, so those scale first.

enum Tier { LOW, MEDIUM, HIGH, ULTRA }

var tier: Tier
var display_name: String

# Resolution / anti-aliasing
var render_scale := 1.0
var upscaler: Viewport.Scaling3DMode = Viewport.SCALING_3D_MODE_BILINEAR
var taa := true
var fsr_sharpness := 0.2

# Shadows
var directional_shadow_size := 4096
var directional_shadow_distance := 120.0
var directional_shadow_splits := 4
var soft_shadow_quality := RenderingServer.SHADOW_QUALITY_SOFT_HIGH
var positional_shadow_atlas := 4096
var fire_shadows := true
## Layers the fire's omni shadow rasterises; presets drop the small-plant layer.
var fire_shadow_casters := 0xFFFFF
var lantern_shadows := false

# Screen-space and GI
var ssao := true
var ssao_quality := RenderingServer.ENV_SSAO_QUALITY_HIGH
var ssil := true
var ssil_quality := RenderingServer.ENV_SSIL_QUALITY_HIGH
var screen_space_half_size := true
var sdfgi := true
var sdfgi_cascades := 6
var sdfgi_ray_count := RenderingServer.ENV_SDFGI_RAY_COUNT_16
var ssr := true
var ssr_steps := 32

# Volumetrics and post
var volumetric_fog := true
var fog_volume_size := 128
var fog_volume_depth := 128
var glow := true
var dof := true
var motion_blur := true
var lens_effects := true
var sky_radiance := Sky.RADIANCE_SIZE_256

# World density
var planar_reflections := true
var reflection_scale := 0.45
var reflection_half_rate := false
var grass_density := 0.9
var grass_distance := 72.0
var foliage_distance := 1.0
var particle_scale := 1.0
var lod_bias := 1.0
## Wooded ridges: generator detail multiplier for the hill trees and whether
## the far band (300-655 m, only visible from elevated views or through gaps)
## is planted at all. Film and Ultra keep everything; the play presets trim
## what no ground-level shot can see. --ridge=full|light|near overrides.
var ridge_detail := 1.0
var ridge_far_band := true
var anisotropy := Viewport.ANISOTROPY_16X
var parallax_steps := 16


static func ultra() -> QualityPreset:
	var p := QualityPreset.new()
	p.tier = Tier.ULTRA
	p.display_name = "Ultra"
	# Sun shadows reach the wooded ridges, so the distant canopy shades
	# itself instead of turning into a pale, flat mass past the near forest.
	p.directional_shadow_distance = 480.0
	p.ssao_quality = RenderingServer.ENV_SSAO_QUALITY_ULTRA
	p.ssil_quality = RenderingServer.ENV_SSIL_QUALITY_ULTRA
	p.sdfgi_ray_count = RenderingServer.ENV_SDFGI_RAY_COUNT_32
	p.sky_radiance = Sky.RADIANCE_SIZE_512
	p.lantern_shadows = true
	p.grass_density = 1.0
	p.grass_distance = 300.0
	p.foliage_distance = 1.25
	p.lod_bias = 2.0
	p.parallax_steps = 24
	p.ssr_steps = 48
	return p


## Offline capture spends extra GPU time on contact, shadow and fog detail.
## Playable Ultra remains available independently of these movie settings.
static func film() -> QualityPreset:
	var p := ultra()
	p.display_name = "Film"
	p.render_scale = 1.5
	p.directional_shadow_size = 8192
	p.soft_shadow_quality = RenderingServer.SHADOW_QUALITY_SOFT_ULTRA
	p.screen_space_half_size = false
	p.sdfgi_ray_count = RenderingServer.ENV_SDFGI_RAY_COUNT_64
	p.fog_volume_size = 192
	p.fog_volume_depth = 192
	p.reflection_scale = 1.0
	return p


static func high() -> QualityPreset:
	var p := QualityPreset.new()
	p.tier = Tier.HIGH
	p.display_name = "High"
	# Measured on the RTX 4060 Laptop (2026-09-10): the frame is fragment-bound
	# in vegetation, so the atlas and internal resolution are the levers that
	# pay without changing the layout. Ultra keeps the full-size settings.
	p.render_scale = 0.77
	p.upscaler = Viewport.SCALING_3D_MODE_FSR2
	p.directional_shadow_size = 2048
	# Three cascades to 80 m: the near cascade gets half the atlas instead of a
	# quarter, and the fourth cascade only ever covered the last ten metres.
	p.directional_shadow_distance = 80.0
	p.directional_shadow_splits = 3
	p.fire_shadow_casters = 0xFFFFF & ~Pond.GRASS_LAYER
	p.soft_shadow_quality = RenderingServer.SHADOW_QUALITY_SOFT_MEDIUM
	p.sdfgi_cascades = 5
	p.ssr_steps = 24
	p.fog_volume_size = 96
	p.fog_volume_depth = 96
	p.reflection_scale = 0.4
	p.reflection_half_rate = true
	p.grass_density = 0.7
	p.grass_distance = 285.0
	p.parallax_steps = 10
	p.ridge_detail = 0.75
	return p


static func medium() -> QualityPreset:
	var p := QualityPreset.new()
	p.tier = Tier.MEDIUM
	p.display_name = "Medium"
	p.render_scale = 0.67
	p.upscaler = Viewport.SCALING_3D_MODE_FSR2
	p.directional_shadow_size = 2048
	p.directional_shadow_distance = 80.0
	p.directional_shadow_splits = 3
	p.fire_shadow_casters = 0xFFFFF & ~Pond.GRASS_LAYER
	p.soft_shadow_quality = RenderingServer.SHADOW_QUALITY_SOFT_LOW
	p.positional_shadow_atlas = 2048
	p.ssao_quality = RenderingServer.ENV_SSAO_QUALITY_MEDIUM
	p.ssil = false
	p.sdfgi_cascades = 4
	p.sdfgi_ray_count = RenderingServer.ENV_SDFGI_RAY_COUNT_8
	p.ssr = false
	p.fog_volume_size = 80
	p.fog_volume_depth = 80
	p.dof = false
	p.sky_radiance = Sky.RADIANCE_SIZE_128
	p.reflection_scale = 0.3
	p.reflection_half_rate = true
	p.grass_density = 0.5
	p.grass_distance = 260.0
	p.foliage_distance = 0.8
	p.ridge_detail = 0.5
	p.ridge_far_band = false
	p.particle_scale = 0.7
	p.anisotropy = Viewport.ANISOTROPY_8X
	p.parallax_steps = 8
	return p


static func low() -> QualityPreset:
	var p := QualityPreset.new()
	p.tier = Tier.LOW
	p.display_name = "Low"
	p.render_scale = 0.5
	p.upscaler = Viewport.SCALING_3D_MODE_FSR2
	p.directional_shadow_size = 2048
	p.directional_shadow_distance = 55.0
	p.directional_shadow_splits = 2
	p.soft_shadow_quality = RenderingServer.SHADOW_QUALITY_SOFT_VERY_LOW
	p.positional_shadow_atlas = 1024
	p.fire_shadows = false
	p.ssao = true
	p.ssao_quality = RenderingServer.ENV_SSAO_QUALITY_VERY_LOW
	p.ssil = false
	p.sdfgi = false
	p.ssr = false
	p.volumetric_fog = true
	p.fog_volume_size = 64
	p.fog_volume_depth = 64
	p.dof = false
	p.motion_blur = false
	p.sky_radiance = Sky.RADIANCE_SIZE_128
	p.planar_reflections = false
	p.grass_density = 0.3
	p.grass_distance = 230.0
	p.foliage_distance = 0.65
	p.particle_scale = 0.5
	p.lod_bias = 0.75
	p.anisotropy = Viewport.ANISOTROPY_4X
	p.parallax_steps = 0
	p.ridge_detail = 0.4
	p.ridge_far_band = false
	return p


static func for_tier(tier: Tier) -> QualityPreset:
	match tier:
		Tier.LOW:
			return low()
		Tier.MEDIUM:
			return medium()
		Tier.HIGH:
			return high()
		Tier.ULTRA:
			return ultra()
		_:
			assert(false, "Unhandled quality tier %s" % tier)
			return ultra()
