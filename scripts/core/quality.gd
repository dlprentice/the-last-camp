extends Node

## Applies `QualityPreset`s to the engine at runtime.
##
## Viewport-level settings (scaling, TAA, anisotropy) are applied directly;
## renderer-wide settings go through the RenderingServer so they take effect
## without a restart. World systems listen to `preset_changed` for the parts
## only they can change (environment flags, foliage density, particle counts).

signal preset_changed(preset: QualityPreset)

const TIER_ORDER: Array[QualityPreset.Tier] = [
	QualityPreset.Tier.LOW, QualityPreset.Tier.MEDIUM,
	QualityPreset.Tier.HIGH, QualityPreset.Tier.ULTRA,
]

var current: QualityPreset = QualityPreset.ultra()


## Keep the cinematic scene at a minimum 30 fps during ordinary play.
const AUTO_TUNE_LIMIT_MS := 33.3
const AUTO_TUNE_SAMPLE_SECONDS := 3.0

var explicit := false


func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS
	explicit = Game.has_flag("quality") or Game.has_flag("cinematic") or Game.has_flag("capture")
	var requested := Game.arg_value("quality", "ultra" if Game.has_flag("cinematic") or Game.has_flag("capture") else "high").to_lower()
	apply(tier_from_name(requested))


## Measures the frame time for a few seconds and steps the preset down (at
## most twice) if the machine cannot hold it. Skipped when the tier was
## chosen on the command line.
func auto_tune() -> void:
	if explicit:
		return
	for step in 2:
		while Game.camp != null and Game.camp.understory != null and Game.camp.understory._replant_thread != null:
			await get_tree().process_frame
		await get_tree().create_timer(1.0).timeout
		var total := 0.0
		var frames := 0
		var elapsed := 0.0
		while elapsed < AUTO_TUNE_SAMPLE_SECONDS:
			await get_tree().process_frame
			var dt := get_process_delta_time()
			elapsed += dt
			total += dt * 1000.0
			frames += 1
		if explicit:
			return
		var mean := total / maxf(float(frames), 1.0)
		if mean <= AUTO_TUNE_LIMIT_MS or current.tier == QualityPreset.Tier.LOW:
			return
		var lower := next_tier(-1)
		print("Auto quality: %.1f ms/frame at %s, switching to %s" % [mean, current.display_name, QualityPreset.for_tier(lower).display_name])
		apply(lower)


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("quality_1") or event.is_action_pressed("quality_2") or event.is_action_pressed("quality_3") or event.is_action_pressed("quality_4"):
		explicit = true
	if event.is_action_pressed("quality_1"):
		apply(QualityPreset.Tier.LOW)
	elif event.is_action_pressed("quality_2"):
		apply(QualityPreset.Tier.MEDIUM)
	elif event.is_action_pressed("quality_3"):
		apply(QualityPreset.Tier.HIGH)
	elif event.is_action_pressed("quality_4"):
		apply(QualityPreset.Tier.ULTRA)


static func tier_from_name(name: String) -> QualityPreset.Tier:
	match name:
		"low":
			return QualityPreset.Tier.LOW
		"medium":
			return QualityPreset.Tier.MEDIUM
		"high":
			return QualityPreset.Tier.HIGH
		_:
			return QualityPreset.Tier.ULTRA


func apply(tier: QualityPreset.Tier) -> void:
	var movie := Game.has_flag("film-quality") or (Game.has_flag("cinematic") and not Game.has_flag("capture-quality"))
	current = QualityPreset.film() if movie and tier == QualityPreset.Tier.ULTRA else QualityPreset.for_tier(tier)
	_apply_viewport(get_tree().root, current)
	_apply_renderer(current)
	preset_changed.emit(current)


func _apply_viewport(viewport: Viewport, p: QualityPreset) -> void:
	viewport.use_taa = false
	viewport.scaling_3d_mode = p.upscaler
	viewport.scaling_3d_scale = p.render_scale
	viewport.fsr_sharpness = p.fsr_sharpness
	viewport.use_taa = p.taa and p.upscaler != Viewport.SCALING_3D_MODE_FSR2
	viewport.msaa_3d = Viewport.MSAA_DISABLED
	viewport.screen_space_aa = Viewport.SCREEN_SPACE_AA_DISABLED
	viewport.use_debanding = true
	viewport.anisotropic_filtering_level = p.anisotropy
	viewport.positional_shadow_atlas_size = p.positional_shadow_atlas
	viewport.mesh_lod_threshold = 1.0 / maxf(p.lod_bias, 0.1)


func _apply_renderer(p: QualityPreset) -> void:
	var rs := RenderingServer
	rs.directional_shadow_atlas_set_size(p.directional_shadow_size, true)
	rs.directional_soft_shadow_filter_set_quality(p.soft_shadow_quality)
	rs.positional_soft_shadow_filter_set_quality(p.soft_shadow_quality)
	rs.environment_set_ssao_quality(p.ssao_quality, p.screen_space_half_size, 0.5, 2, 50.0, 300.0)
	rs.environment_set_ssil_quality(p.ssil_quality, p.screen_space_half_size, 0.5, 4, 50.0, 300.0)
	rs.environment_set_sdfgi_ray_count(p.sdfgi_ray_count)
	rs.environment_set_sdfgi_frames_to_converge(RenderingServer.ENV_SDFGI_CONVERGE_IN_10_FRAMES)
	rs.environment_set_sdfgi_frames_to_update_light(RenderingServer.ENV_SDFGI_UPDATE_LIGHT_IN_2_FRAMES)
	rs.environment_set_volumetric_fog_volume_size(p.fog_volume_size, p.fog_volume_depth)
	rs.environment_set_volumetric_fog_filter_active(true)


func next_tier(step: int) -> QualityPreset.Tier:
	var index := TIER_ORDER.find(current.tier)
	return TIER_ORDER[clampi(index + step, 0, TIER_ORDER.size() - 1)]
