class_name WorldController
extends Node3D

## Owns the Environment, sky, sun/moon lights, camera attributes, the final
## post pass and the time-of-day simulation. Everything lighting-related that
## changes with the hour (or with the camera going under water) is applied
## here so the rest of the scene can stay declarative.

signal hour_changed(hour: float)

## Hero time: the sun sits a few degrees above the treeline across the pond.
const DEFAULT_HOUR := 19.05
const CYCLE_HOURS_PER_SECOND := 24.0 / 180.0
const SCRUB_HOURS_PER_SECOND := 1.6
const FIRE_ORIGIN := Vector3(0.0, 0.55, 0.0)
## Prevailing wind (also the `wind_direction` shader global).
const WIND_DIRECTION := Vector2(1.0, 0.3)

## Warm-up stages: heavy renderer features come online one at a time so the
## first frames after loading never pile SDFGI, SSIL, volumetrics and the
## planar reflection onto one GPU submission.
enum Warm { NONE, VOLUMETRICS, SCREEN_SPACE, GLOBAL_ILLUMINATION, ALL }

const UNDERWATER_FOG := Color(0.05, 0.17, 0.15)
const UNDERWATER_SKY := Color(0.10, 0.30, 0.28)
## Maximum displacement of the three shared pond waves at the wind limit.
## Clear-air haze. 0.0007 mixed a quarter of the bright horizon into anything
## 400 m away and turned the wooded ridges pale yellow-green; real morning air
## over a forest is about half that.
const AIR_FOG_DENSITY := 0.0004
const WATER_FOG_DENSITY := 0.16
const AIR_VOLUME_DENSITY := 0.00045
const WATER_VOLUME_DENSITY := 0.09
const WATER_VOLUME_ALBEDO := Color(0.40, 0.72, 0.57)
const WATER_WAVE_ENVELOPE := 0.012 * 2.1 * 1.3 + PondSurface.RESIDUAL_BOUND

var hour := DEFAULT_HOUR:
	set(value):
		hour = fposmod(value, 24.0)
		_apply_hour()
var haze := 0.85
var wind_scale := 1.0
var cycle_running := false
var warm_stage: Warm = Warm.NONE
var exposure_scale := 1.0:
	set(value):
		exposure_scale = value
		if environment != null:
			environment.tonemap_exposure = _exposure_for(daylight(), Atmosphere.sun_direction(hour)) * exposure_scale

var underwater := false
var lens_entry_age := 100.0
var lens_exit_age := 100.0
var _lens_in_water := false
var _lens_initialized := false
var underwater_blend := 0.0
var _fog_history_reset := 0
var _water_focus_saved: Dictionary = {}

var environment: Environment
var entry_bubbles: GPUParticles3D
var weather: CampWeather
var sky: Sky
var sky_material: ShaderMaterial
var sun: DirectionalLight3D
var moon: DirectionalLight3D
var attributes: CameraAttributesPractical
var world_env: WorldEnvironment
var post: PostStack
var waterline: MeshInstance3D
var waterline_material: ShaderMaterial
var water_volume: FogVolume
var water_volume_material: ShaderMaterial

var _wind_time := 0.0
var _cloud_time := 0.0
var _cloud_hour := DEFAULT_HOUR
var _gust := 1.0
var _fog_color := Color(0.5, 0.6, 0.8)
var _preset: QualityPreset
var _mist: Array[FogVolume] = []
var _applied_cloud := -1.0
var _applied_rain := -1.0


func _ready() -> void:
	Game.world = self
	_build_environment()
	_build_lights()
	_build_camera_attributes()
	_build_post()
	_build_water_volume()
	_build_entry_bubbles()
	_build_mist()
	weather = CampWeather.new()
	add_child(weather)
	RenderingServer.global_shader_parameter_set("wind_direction", WIND_DIRECTION)
	Quality.preset_changed.connect(_on_quality_changed)
	_on_quality_changed(Quality.current)
	_apply_hour()


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("toggle_time_cycle"):
		cycle_running = not cycle_running


func _process(delta: float) -> void:
	var scrub := Input.get_action_strength("time_forward") - Input.get_action_strength("time_back")
	if Game.mode == Game.Mode.PLAY or Game.mode == Game.Mode.PHOTO:
		if scrub != 0.0:
			hour = hour + scrub * SCRUB_HOURS_PER_SECOND * delta
		elif cycle_running:
			hour = hour + CYCLE_HOURS_PER_SECOND * delta
	_wind_time += delta
	_gust = (0.7 + 0.3 * sin(_wind_time * 0.23) * sin(_wind_time * 0.071 + 1.3)) * wind_scale
	RenderingServer.global_shader_parameter_set("wind_strength", _gust)
	var hour_step := absf(wrapf(hour - _cloud_hour, -12.0, 12.0))
	# Weather advances with a continuous time lapse; a cut to another hour
	# does not teleport the cloud texture. Vegetation keeps its real clock.
	_cloud_time += delta + (hour_step * 360.0 if hour_step < 0.08 else 0.0)
	_cloud_hour = hour
	sky_material.set_shader_parameter("cloud_time", _cloud_time)
	if Game.player != null:
		RenderingServer.global_shader_parameter_set("player_position", Game.player.global_position)
	_update_underwater(delta)
	_update_post()
	# Sun energy, sky overcast and fog density all follow the weather but were
	# only recomputed when the hour changed, so a chapter cut that clears the
	# sky without moving the hour kept the storm's lighting for the whole next
	# shot. Re-apply the hour whenever the weather state moves.
	if weather != null and (not is_equal_approx(weather.cloud, _applied_cloud) or not is_equal_approx(weather.rain, _applied_rain)):
		_apply_hour()


## Brings the expensive features online over a few frames. Awaited by the
## scene entry point while the loading screen is still up.
func warm_up() -> void:
	for stage: Warm in [Warm.VOLUMETRICS, Warm.SCREEN_SPACE, Warm.GLOBAL_ILLUMINATION, Warm.ALL]:
		warm_stage = stage
		_apply_features()
		for i in 3:
			await get_tree().process_frame


# ------------------------------------------------------------------ building

func _build_environment() -> void:
	world_env = WorldEnvironment.new()
	world_env.name = "Environment"
	add_child(world_env)

	sky_material = ShaderMaterial.new()
	sky_material.shader = load("res://shaders/sky.gdshader")
	sky = Sky.new()
	sky.sky_material = sky_material
	sky.process_mode = Sky.PROCESS_MODE_INCREMENTAL
	sky.radiance_size = Sky.RADIANCE_SIZE_256

	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_sky_contribution = 1.0
	env.ambient_light_energy = 1.0
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY

	env.tonemap_mode = Environment.TONE_MAPPER_AGX
	env.tonemap_exposure = 1.0
	env.tonemap_white = 6.0

	env.glow_enabled = true
	env.glow_normalized = false
	env.glow_intensity = 0.32
	env.glow_strength = 0.85
	env.glow_bloom = 0.006
	env.glow_blend_mode = Environment.GLOW_BLEND_MODE_SOFTLIGHT
	env.glow_hdr_threshold = 1.45
	env.glow_hdr_scale = 1.6
	env.glow_hdr_luminance_cap = 8.0
	for level in 7:
		env.set_glow_level(level, 0.0)
	env.set_glow_level(2, 0.35)
	env.set_glow_level(3, 0.75)
	env.set_glow_level(4, 0.9)
	env.set_glow_level(5, 0.6)
	env.set_glow_level(6, 0.35)

	env.ssao_enabled = false
	env.ssao_radius = 1.2
	env.ssao_intensity = 1.35
	env.ssao_power = 1.25
	env.ssao_detail = 0.6
	env.ssao_horizon = 0.06
	env.ssao_sharpness = 0.98
	env.ssao_light_affect = 0.05
	env.ssao_ao_channel_affect = 0.0

	env.ssil_enabled = false
	env.ssil_radius = 4.0
	env.ssil_intensity = 1.1
	env.ssil_sharpness = 0.98
	env.ssil_normal_rejection = 1.0

	env.sdfgi_enabled = false
	env.sdfgi_cascades = 6
	env.sdfgi_min_cell_size = 0.2
	env.sdfgi_use_occlusion = true
	env.sdfgi_read_sky_light = true
	env.sdfgi_bounce_feedback = 0.5
	env.sdfgi_energy = 1.0
	env.sdfgi_normal_bias = 1.1
	env.sdfgi_probe_bias = 1.1
	env.sdfgi_y_scale = Environment.SDFGI_Y_SCALE_75_PERCENT

	env.ssr_enabled = false
	env.ssr_max_steps = 64
	env.ssr_fade_in = 0.15
	env.ssr_fade_out = 2.0
	env.ssr_depth_tolerance = 0.2

	env.fog_enabled = true
	env.fog_mode = Environment.FOG_MODE_EXPONENTIAL
	env.fog_density = 0.0007
	env.fog_aerial_perspective = 0.6
	env.fog_sky_affect = 0.12
	env.fog_sun_scatter = 0.07
	env.fog_light_energy = 1.0
	env.fog_height = TerrainField.WATER_LEVEL + 1.0
	env.fog_height_density = 0.02

	env.volumetric_fog_enabled = false
	env.volumetric_fog_density = 0.00065
	env.volumetric_fog_albedo = Color(0.92, 0.92, 0.92)
	env.volumetric_fog_emission = Color(0, 0, 0)
	env.volumetric_fog_gi_inject = 0.9
	env.volumetric_fog_anisotropy = 0.72
	env.volumetric_fog_length = 128.0
	env.volumetric_fog_detail_spread = 2.2
	env.volumetric_fog_ambient_inject = 0.18
	env.volumetric_fog_sky_affect = 0.45
	env.volumetric_fog_temporal_reprojection_enabled = true
	env.volumetric_fog_temporal_reprojection_amount = 0.92

	env.adjustment_enabled = true
	env.adjustment_brightness = 1.0
	env.adjustment_contrast = 1.04
	env.adjustment_saturation = 1.06

	environment = env
	world_env.environment = env


func _build_lights() -> void:
	sun = DirectionalLight3D.new()
	sun.name = "Sun"
	sun.light_angular_distance = 0.53
	sun.shadow_enabled = true
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	sun.directional_shadow_split_1 = 0.05
	sun.directional_shadow_split_2 = 0.15
	sun.directional_shadow_split_3 = 0.4
	sun.directional_shadow_blend_splits = true
	sun.directional_shadow_fade_start = 0.85
	sun.directional_shadow_max_distance = 160.0
	sun.shadow_bias = 0.025
	sun.shadow_normal_bias = 1.6
	sun.shadow_blur = 1.0
	sun.shadow_opacity = 1.0
	sun.light_volumetric_fog_energy = 1.0
	sun.sky_mode = DirectionalLight3D.SKY_MODE_LIGHT_ONLY
	add_child(sun)

	moon = DirectionalLight3D.new()
	moon.name = "Moon"
	moon.light_angular_distance = 0.5
	moon.shadow_enabled = true
	moon.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS
	moon.directional_shadow_max_distance = 90.0
	moon.shadow_blur = 2.0
	moon.light_volumetric_fog_energy = 0.6
	moon.sky_mode = DirectionalLight3D.SKY_MODE_LIGHT_ONLY
	moon.light_color = Atmosphere.MOON_COLOR
	add_child(moon)


func _build_camera_attributes() -> void:
	attributes = CameraAttributesPractical.new()
	attributes.auto_exposure_enabled = false
	attributes.exposure_multiplier = 1.0
	attributes.exposure_sensitivity = 100.0
	attributes.dof_blur_far_enabled = false
	attributes.dof_blur_near_enabled = false
	attributes.dof_blur_amount = 0.06


func _build_post() -> void:
	post = PostStack.new()
	add_child(post)
	# Murk for half-submerged frames: a full-screen quad drawn after the water.
	waterline = MeshInstance3D.new()
	waterline.name = "Waterline"
	# This lens effect belongs only to the main view. Drawing it from the
	# submerged mirror camera bakes a dark fog band into the pond reflection.
	waterline.layers = Pond.WATER_LAYER
	var quad := QuadMesh.new()
	quad.size = Vector2(2.0, 2.0)
	waterline.mesh = quad
	waterline_material = ShaderMaterial.new()
	waterline_material.shader = load("res://shaders/post/waterline.gdshader")
	waterline_material.render_priority = 100
	waterline_material.set_shader_parameter("fog_color", UNDERWATER_FOG)
	waterline_material.set_shader_parameter("water_level", TerrainField.WATER_LEVEL)
	waterline.material_override = waterline_material
	waterline.custom_aabb = AABB(Vector3(-4000.0, -4000.0, -4000.0), Vector3(8000.0, 8000.0, 8000.0))
	waterline.extra_cull_margin = 16384.0
	waterline.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	waterline.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	waterline.visible = false
	add_child(waterline)


func _build_water_volume() -> void:
	# Only the crossing needs a bounded volume: global water fog owns fully
	# submerged views. Auxiliary reflection views exclude the water layer.
	water_volume = FogVolume.new()
	water_volume.name = "WaterCrossingVolume"
	water_volume.layers = Pond.WATER_LAYER
	water_volume.shape = RenderingServer.FOG_VOLUME_SHAPE_BOX
	water_volume.size = Vector3(60.0, 6.0, 60.0)
	water_volume.position = Vector3(TerrainField.POND_CENTRE.x, TerrainField.WATER_LEVEL - 3.005, TerrainField.POND_CENTRE.y)
	water_volume_material = ShaderMaterial.new()
	water_volume_material.shader = load("res://shaders/pond_fog.gdshader")
	water_volume_material.set_shader_parameter("water_albedo", WATER_VOLUME_ALBEDO)
	water_volume.material = water_volume_material
	water_volume.visible = false
	add_child(water_volume)


# ----------------------------------------------------------------- per hour

func _apply_hour() -> void:
	if weather != null:
		_applied_cloud = weather.cloud
		_applied_rain = weather.rain
	if environment == null:
		return
	var sun_dir := Atmosphere.sun_direction(hour)
	var moon_dir := Atmosphere.moon_direction(hour)
	var sun_light: Dictionary = Atmosphere.sun_light(sun_dir, haze)
	var moon_light: Dictionary = Atmosphere.moon_light(sun_dir, moon_dir)

	# Lights point along -Z, so aim them away from the celestial body.
	sun.global_transform = Transform3D(Basis.looking_at(-sun_dir, _stable_up(sun_dir)), Vector3.ZERO)
	sun.light_color = sun_light.color
	var overcast := weather.cloud if weather != null else 0.0
	# Preserve the readable blue-hour meadow; deep night keeps enough moon that
	# the treeline, the shore and the water still have form under the stars
	# instead of falling to black around the lanterns.
	var deep_night := smoothstep(-0.12, -0.35, sun_dir.y)
	sun.light_energy = sun_light.energy * lerpf(1.0, 0.20, overcast)
	sun.visible = sun_light.energy > 0.001
	moon.global_transform = Transform3D(Basis.looking_at(-moon_dir, _stable_up(moon_dir)), Vector3.ZERO)
	# Storm cloud still passes a little moon: the rain shots kept only the dock lantern at 0.18.
	moon.light_energy = moon_light.energy * lerpf(2.0, 1.1, deep_night) * lerpf(1.0, 0.32, overcast)
	moon.visible = moon_light.energy > 0.001

	sky_material.set_shader_parameter("sun_direction", sun_dir)
	sky_material.set_shader_parameter("moon_direction", moon_dir)
	sky_material.set_shader_parameter("haze", haze)
	sky_material.set_shader_parameter("star_rotation", hour / 24.0 * TAU)
	# Clouds break apart as the evening cools, revealing the star field.
	sky_material.set_shader_parameter("cloud_coverage", weather.cloud_coverage(Atmosphere.daylight(hour)) if weather != null else lerpf(0.18, 0.36, Atmosphere.daylight(hour)))

	sky_material.set_shader_parameter("storm_amount", overcast)
	var daylight := Atmosphere.daylight(hour)
	_fog_color = Atmosphere.fog_color(sun_dir, haze)
	_apply_fog()
	environment.volumetric_fog_ambient_inject = lerpf(0.04, 0.18, daylight)
	environment.tonemap_exposure = _exposure_for(daylight, sun_dir) * exposure_scale
	# Night vision is colour-poor: desaturate and cool the grade after dark,
	# further still once the sun is well down and only the moon is left.
	environment.adjustment_saturation = lerpf(lerpf(0.86, 0.72, deep_night), 0.98, daylight)
	environment.adjustment_contrast = lerpf(1.04, 1.02, daylight)

	var sun_col: Color = sun_light.color
	var sun_energy: float = sun.light_energy
	var sun_radiance := Vector3(sun_col.r, sun_col.g, sun_col.b) * sun_energy
	RenderingServer.global_shader_parameter_set("sun_direction", sun_dir)
	RenderingServer.global_shader_parameter_set("sun_color", sun_radiance)
	RenderingServer.global_shader_parameter_set("daylight", daylight)
	hour_changed.emit(hour)


## Manual exposure curve: bright days are pulled down, deep night is lifted so
## the fire-lit camp stays readable without ever looking like daylight.
func _exposure_for(daylight: float, sun_dir: Vector3) -> float:
	var low_sun := 1.0 - smoothstep(0.0, 0.45, sun_dir.y)
	var day_exposure := lerpf(0.9, 1.25, low_sun)
	var night_exposure := 2.7
	return lerpf(night_exposure, day_exposure, daylight)


static func _stable_up(dir: Vector3) -> Vector3:
	return Vector3.UP if absf(dir.y) < 0.98 else Vector3.FORWARD


func fog_color() -> Color:
	return _fog_color


func wind_strength() -> float:
	return _gust


func sun_direction() -> Vector3:
	return Atmosphere.sun_direction(hour)


func daylight() -> float:
	return Atmosphere.daylight(hour)


# --------------------------------------------------------------- underwater

## Fog and volumetrics take over from the per-pixel crossing pass once the
## active camera's entire near plane is under the pond surface.
func _update_underwater(delta: float) -> void:
	var camera := get_viewport().get_camera_3d()
	var blend := 0.0
	var lens_depth := -1.0
	if camera != null and Game.camp != null:
		var p := camera.global_position
		if Game.camp.field.water_depth(p.x, p.z) > 0.0:
			var level: float = Game.camp.pond.surface_height(Vector2(p.x, p.z)) if Game.camp.pond != null else TerrainField.WATER_LEVEL
			lens_depth = level - p.y
			var size := get_viewport().get_visible_rect().size
			var aspect := size.x / maxf(size.y, 1.0)
			var tan_half := tan(deg_to_rad(camera.fov) * 0.5)
			if camera.keep_aspect == Camera3D.KEEP_WIDTH:
				tan_half /= aspect
			blend = water_fog_blend(camera.global_transform, camera.near, tan_half, aspect)
	update_water_lens(lens_depth, delta)
	if not is_equal_approx(blend, underwater_blend):
		underwater_blend = blend
		_apply_fog()
		_fog_history_reset = 6
	_apply_water_focus(blend)
	var now := blend > 0.001
	if now != underwater:
		underwater = now
		_apply_features()
		# Keep history writes active while giving old samples zero weight.
		# Disabling reprojection can retain the old water history for reuse.
		_fog_history_reset = 6
	if _fog_history_reset > 0:
		_fog_history_reset -= 1
		environment.volumetric_fog_temporal_reprojection_enabled = true
		environment.volumetric_fog_temporal_reprojection_amount = 0.0
	else:
		environment.volumetric_fog_temporal_reprojection_amount = 0.92


## The dead band prevents waves from firing a new splash on every frame.
## The first pose (including a capture teleport) does not fabricate a crossing.
func update_water_lens(depth: float, delta: float) -> void:
	lens_entry_age = minf(lens_entry_age + delta, 100.0)
	lens_exit_age = minf(lens_exit_age + delta, 100.0)
	if not _lens_initialized:
		_lens_initialized = true
		_lens_in_water = depth > 0.04
		return
	if not _lens_in_water and depth > 0.005:
		_lens_in_water = true
		lens_entry_age = 0.0
		if is_inside_tree() and Game.audio != null:
			Game.audio.water_crossing(true)
		var camera := get_viewport().get_camera_3d() if is_inside_tree() else null
		if camera != null and Game.camp != null and Game.camp.pond != null:
			Game.camp.pond.ripple(camera.global_position, 1.1)
		if entry_bubbles != null and camera != null:
			entry_bubbles.global_position = camera.global_position - camera.global_basis.z * 0.30 - camera.global_basis.y * 0.16
			entry_bubbles.restart()
			entry_bubbles.emitting = true
	elif _lens_in_water and depth < -0.04:
		_lens_in_water = false
		lens_exit_age = 0.0
		if is_inside_tree() and Game.audio != null:
			Game.audio.water_crossing(false)


## Native depth-of-field lets nearby timber keep its silhouette while distant
## detail dissolves. The final lens pass adds only a small baseline defocus.
## Global DOF starts after the whole near plane is underwater, preserving the
## sharp above-water part of a split view and the user's previous photo focus.
func _apply_water_focus(blend: float) -> void:
	if blend > 0.0:
		if _water_focus_saved.is_empty():
			for key in [&"dof_blur_far_enabled", &"dof_blur_near_enabled", &"dof_blur_far_distance", &"dof_blur_far_transition", &"dof_blur_amount"]:
				_water_focus_saved[key] = attributes.get(key)
		attributes.dof_blur_near_enabled = false
		attributes.dof_blur_far_enabled = true
		attributes.dof_blur_far_distance = 2.2
		attributes.dof_blur_far_transition = 6.0
		attributes.dof_blur_amount = 0.010 * blend
	elif not _water_focus_saved.is_empty():
		for key in _water_focus_saved:
			attributes.set(key, _water_focus_saved[key])
		_water_focus_saved.clear()


func _build_entry_bubbles() -> void:
	entry_bubbles = GPUParticles3D.new()
	entry_bubbles.name = "ImmersionBubbles"
	entry_bubbles.layers = Pond.WATER_LAYER
	entry_bubbles.emitting = false
	entry_bubbles.one_shot = true
	entry_bubbles.amount = 36
	entry_bubbles.lifetime = 1.8
	entry_bubbles.explosiveness = 0.90
	entry_bubbles.local_coords = false
	entry_bubbles.visibility_aabb = AABB(Vector3(-2, -2, -2), Vector3(4, 6, 4))
	entry_bubbles.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var process := ParticleProcessMaterial.new()
	process.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	process.emission_box_extents = Vector3(0.22, 0.025, 0.08)
	process.direction = Vector3.UP
	process.spread = 22.0
	process.initial_velocity_min = 0.35
	process.initial_velocity_max = 0.75
	process.gravity = Vector3(0, 0.18, 0)
	process.scale_min = 0.4
	process.scale_max = 1.3
	entry_bubbles.process_material = process
	var quad := QuadMesh.new()
	quad.size = Vector2(0.010, 0.013)
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/bubble.gdshader")
	mat.set_shader_parameter("water_level", TerrainField.WATER_LEVEL)
	quad.material = mat
	entry_bubbles.draw_pass_1 = quad
	add_child(entry_bubbles)


func reset_water_lens() -> void:
	_lens_initialized = false
	lens_entry_age = 100.0
	lens_exit_age = 100.0


## Global fog cannot split the image. Begin its handoff only after the highest
## near-plane corner is below even the lowest possible wave; the per-pixel
## pass supplies the remaining water density until the handoff is complete.
static func water_fog_blend(frame: Transform3D, near: float, tan_half: float, aspect: float) -> float:
	var highest_y := frame.origin.y + near * (-frame.basis.z.y + tan_half *
		(absf(frame.basis.y.y) + aspect * absf(frame.basis.x.y)))
	return smoothstep(0.0, 0.05, TerrainField.WATER_LEVEL - WATER_WAVE_ENVELOPE - highest_y)


func _apply_fog() -> void:
	var u := underwater_blend
	environment.background_color = Color.BLACK.lerp(UNDERWATER_SKY, u)
	# Aerial perspective is the horizon sky, which sits a little cooler than
	# the averaged scattering sample: bias the haze toward blue so distant
	# canopy fades grey-blue instead of yellow.
	var haze_colour := Color(_fog_color.r * 0.94, _fog_color.g * 0.99, minf(_fog_color.b * 1.06, 1.0)).lightened(0.03)
	environment.fog_light_color = haze_colour.lerp(UNDERWATER_FOG, u)
	var rain := weather.rain if weather != null else 0.0
	environment.fog_density = lerpf(AIR_FOG_DENSITY + rain * 0.006, WATER_FOG_DENSITY, u)
	environment.fog_aerial_perspective = lerpf(0.6, 0.0, u)
	environment.fog_sky_affect = lerpf(0.12, 1.0, u)
	environment.fog_sun_scatter = lerpf(0.07, 0.0, u)
	environment.fog_height_density = lerpf(0.005, 0.0, u)
	environment.volumetric_fog_density = lerpf(AIR_VOLUME_DENSITY + rain * 0.0012, WATER_VOLUME_DENSITY, u)
	environment.volumetric_fog_albedo = Color(0.92, 0.92, 0.92).lerp(WATER_VOLUME_ALBEDO, u)
	environment.volumetric_fog_anisotropy = lerpf(0.72, 0.75, u)
	environment.volumetric_fog_sky_affect = lerpf(0.45, 1.0, u)
	var deep_night := smoothstep(-0.12, -0.35, Atmosphere.sun_direction(hour).y)
	environment.ambient_light_energy = lerpf(lerpf(1.6, 1.0, daylight()) * lerpf(1.0, 0.75, deep_night) * lerpf(1.0, 0.65, rain), 0.6, u)
	environment.fog_light_energy = 1.0
	if waterline_material != null:
		waterline_material.set_shader_parameter("density", (WATER_FOG_DENSITY - AIR_FOG_DENSITY) * (1.0 - u))


func _update_post() -> void:
	if post == null:
		return
	var day := daylight()
	var photo := Game.mode == Game.Mode.PHOTO
	post.set_param(&"grain", lerpf(0.005, 0.007, day))
	post.set_param(&"vignette", (0.16 if photo else 0.12))
	post.set_param(&"chromatic", 0.025)
	post.set_param(&"sharpen", 0.13)
	post.set_param(&"entry_age", lens_entry_age)
	post.set_param(&"exit_age", lens_exit_age)
	_update_waterline()


## Feeds the camera frame to the waterline passes so each pixel can decide
## whether its near-plane point is under the pond surface.
func _update_waterline() -> void:
	var camera := get_viewport().get_camera_3d()
	if camera == null or Game.camp == null:
		post.set_param(&"underwater", 0.0)
		if waterline != null:
			waterline.visible = false
		if water_volume != null:
			water_volume.visible = false
		return
	var xf := camera.global_transform
	var size := get_viewport().get_visible_rect().size
	var aspect := size.x / maxf(size.y, 1.0)
	var tan_half := tan(deg_to_rad(camera.fov) * 0.5)
	if camera.keep_aspect == Camera3D.KEEP_WIDTH:
		tan_half /= aspect
	var over_water: bool = Game.camp.field.water_depth(xf.origin.x, xf.origin.z) > 0.0
	if water_volume != null:
		var near_surface := 1.0 - smoothstep(TerrainField.WATER_LEVEL + 0.015, TerrainField.WATER_LEVEL + 0.15, xf.origin.y)
		var volume_density := WATER_VOLUME_DENSITY * (1.0 - underwater_blend) * near_surface
		water_volume_material.set_shader_parameter("density", volume_density)
		water_volume.visible = over_water and environment.volumetric_fog_enabled and volume_density > 0.00001
	post.set_param(&"underwater", 1.0 if over_water else 0.0)
	post.set_param(&"cam_pos", xf.origin)
	post.set_param(&"cam_right", xf.basis.x)
	post.set_param(&"cam_up", xf.basis.y)
	post.set_param(&"cam_fwd", -xf.basis.z)
	post.set_param(&"cam_tan_half", tan_half)
	post.set_param(&"cam_aspect", aspect)
	post.set_param(&"cam_near", camera.near)
	post.set_param(&"water_level", TerrainField.WATER_LEVEL)
	if waterline == null:
		return
	var lowest_y := xf.origin.y + camera.near * (-xf.basis.z.y - tan_half *
		(absf(xf.basis.y.y) + aspect * absf(xf.basis.x.y)))
	waterline.visible = over_water and lowest_y < TerrainField.WATER_LEVEL + WATER_WAVE_ENVELOPE + 0.002 and underwater_blend < 1.0
	if waterline.visible:
		waterline_material.set_shader_parameter("strength", 1.0)


# ------------------------------------------------------------------ quality

func _on_quality_changed(p: QualityPreset) -> void:
	_preset = p
	_apply_features()
	sun.directional_shadow_max_distance = p.directional_shadow_distance
	match p.directional_shadow_splits:
		2:
			sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS
		3:
			sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS
			sun.directional_shadow_split_1 = 0.12
		_:
			sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_4_SPLITS
	sky.radiance_size = p.sky_radiance
	# Film and Ultra march the clouds with 40 steps and a finer erosion octave.
	sky_material.set_shader_parameter("cloud_quality", 2 if p.tier == QualityPreset.Tier.ULTRA else (1 if p.tier == QualityPreset.Tier.HIGH else 0))
	if post != null:
		post.set_enabled(p.lens_effects)


## Combines the preset with the warm-up gate.
func _apply_features() -> void:
	if _preset == null or environment == null:
		return
	var p := _preset
	environment.volumetric_fog_enabled = p.volumetric_fog and warm_stage >= Warm.VOLUMETRICS
	environment.ssao_enabled = p.ssao and warm_stage >= Warm.SCREEN_SPACE
	environment.ssil_enabled = p.ssil and warm_stage >= Warm.SCREEN_SPACE
	environment.sdfgi_enabled = p.sdfgi and warm_stage >= Warm.GLOBAL_ILLUMINATION
	environment.sdfgi_cascades = p.sdfgi_cascades
	environment.ssr_enabled = p.ssr and warm_stage >= Warm.ALL
	environment.ssr_max_steps = p.ssr_steps
	environment.glow_enabled = p.glow
	for volume in _mist:
		volume.visible = environment.volumetric_fog_enabled and not underwater


## Local banks leave the foreground clear while separating the shoreline and
## the forest into depth planes. They share the renderer's warm-up gate.
func _build_mist() -> void:
	var shader: Shader = load("res://shaders/ground_mist.gdshader")
	var banks := [
		# Thinner over the pond: with the sun low across the water the bank
		# turned the left half of the jetty shots into a white haze.
		[Vector3(-34.0, -0.15, 4.0), Vector3(48.0, 2.6, 38.0), 0.040],
		[Vector3(0.0, 4.0, -43.0), Vector3(90.0, 7.0, 24.0), 0.025],
		[Vector3(42.0, 4.5, 5.0), Vector3(28.0, 7.0, 75.0), 0.022],
		# After-rain ground mist over the camp: thin and low. At 0.38 it lit up as
		# a white bank around the tent in the sunlit after-the-rain shot.
		[Vector3(0.0, 0.9, -4.0), Vector3(20.0, 1.6, 12.0), 0.11],
	]
	for bank in banks:
		var volume := FogVolume.new()
		volume.name = "LowMist%d" % _mist.size()
		volume.layers = Pond.AIR_FOG_LAYER
		volume.shape = RenderingServer.FOG_VOLUME_SHAPE_BOX
		volume.size = bank[1]
		volume.position = bank[0]
		var mat := ShaderMaterial.new()
		mat.shader = shader
		mat.set_shader_parameter("density", bank[2])
		mat.set_shader_parameter("after_rain", _mist.size() == 3)
		volume.material = mat
		volume.visible = false
		add_child(volume)
		_mist.append(volume)
