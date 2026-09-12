class_name CampWeather
extends Node3D
## Deterministic weather cues shared by interactive preview, captures and film.
## Rain lives in the world, with terrain/roof rejection and normal depth tests.

var cloud := 0.0
var rain := 0.0
var wet := 0.0
var flash := 0.0
var _clock := 0.0
var _chapter := ""
var _previous_time := -1.0
var _rain_mesh: MultiMeshInstance3D
var _light: DirectionalLight3D
var _bolt: MeshInstance3D
var _bolt_material: StandardMaterial3D
var _impacts: RainImpacts
var _rain_audio: AudioStreamPlayer
var _thunder_voices: Array[AudioStreamPlayer] = []
var _thunder_voice := 0
var _rain_gain := 0.0
var _rain_shelter_mix := 0.0
var _rain_filter: AudioEffectLowPassFilter
var _thunder_due: Array[Vector2] = []
var _thunder_clock := 0.0
var _roofs_ready := false

# Cue position, intensity and thunder travel time describe the same event.
# Cloud-only pulses alternate with visible forks; quiet gaps remain between
# brief multi-stroke flashes. Distances are deliberately beyond the forest.
const STORM_STRIKES := [
	{time = 9.0, delay = 2.8, power = 0.55, at = Vector3(-900, 0, -160), fork = true},
	{time = 15.0, delay = 1.35, power = 0.72, at = Vector3(-470, 0, 40), fork = false},
	{time = 21.0, delay = 2.0, power = 0.62, at = Vector3(-680, 0, -150), fork = true},
	{time = 28.0, delay = 0.85, power = 0.82, at = Vector3(-300, 0, -70), fork = false},
	{time = 33.0, delay = 0.65, power = 1.0, at = Vector3(-240, 0, -50), fork = true},
	{time = 37.0, delay = 1.65, power = 0.68, at = Vector3(-570, 0, 65), fork = false},
]
const SHELTER_STRIKES := [
	{time = 5.0, delay = 1.2, power = 0.62, at = Vector3(-415, 0, -90), fork = false},
	{time = 14.0, delay = 2.0, power = 0.48, at = Vector3(-700, 0, 120), fork = false},
	{time = 24.0, delay = 0.9, power = 0.78, at = Vector3(-310, 0, -50), fork = false},
]
const SHORT_STORM_STRIKES := [
	{time = 4.5, delay = 2.8, power = 0.55, at = Vector3(-900, 0, -160), fork = true},
	{time = 8.0, delay = 1.35, power = 0.72, at = Vector3(-470, 0, 40), fork = false},
	{time = 12.0, delay = 2.0, power = 0.62, at = Vector3(-680, 0, -150), fork = true},
	{time = 16.5, delay = 0.65, power = 1.0, at = Vector3(-240, 0, -50), fork = true},
]


static func strikes(chapter: String) -> Array:
	if chapter == "storm":
		return STORM_STRIKES
	if chapter == "storm_short":
		return SHORT_STORM_STRIKES
	if chapter == "shelter_rain":
		return SHELTER_STRIKES
	return []


static func chapter_flash(chapter: String, seconds: float) -> float:
	var total := 0.0
	for cue in strikes(chapter):
		total += strike_flash(seconds - cue.time) * cue.power
	return total


func cloud_coverage(daylight: float) -> float:
	# The star chapter must actually clear, regardless of the advected noise
	# phase reached after several hours of accelerated cinematic time.
	var fair_cover := 0.0 if _chapter == "clearing" else lerpf(0.18, 0.36, daylight)
	return lerpf(fair_cover, 0.48, cloud)


func _ready() -> void:
	name = "Weather"
	process_priority = -5
	_build_rain()
	_build_lightning()
	apply_chapter("", 0, 1, false)


static func strike_flash(age: float) -> float:
	if age < 0.0 or age > 0.48:
		return 0.0
	return exp(-age * 28.0) + 0.5 * exp(-pow((age - 0.105) / 0.025, 2.0))


static func conditions(chapter: String, seconds: float, duration: float) -> Vector4:
	var t := clampf(seconds / maxf(duration, 0.001), 0.0, 1.0)
	match chapter:
		"gathering":
			return Vector4(smoothstep(0.0, 1.0, t) * 0.65, smoothstep(0.75, 1.0, t) * 0.08, t * 0.05, 0.0)
		"storm":
			return Vector4(lerpf(0.65, 1.0, smoothstep(0.0, 0.4, t)), smoothstep(8.0, 19.0, seconds),
				smoothstep(8.0, 28.0, seconds), chapter_flash(chapter, seconds))
		"storm_short":
			return Vector4(lerpf(0.65, 1.0, smoothstep(0.0, 0.4, t)), smoothstep(3.0, 10.0, seconds),
				smoothstep(3.0, 17.0, seconds), chapter_flash(chapter, seconds))
		"shelter_rain":
			return Vector4(1.0, 1.0, 1.0, chapter_flash(chapter, seconds))
		"rain_detail":
			return Vector4(1.0, 1.0, 1.0, 0.0)
		"clearing":
			return Vector4(1.0 - smoothstep(0.0, 6.0, seconds), 1.0 - smoothstep(0.0, 4.0, seconds), 1.0, 0.0)
		"dawn":
			return Vector4(0.0, 0.0, lerpf(1.0, 0.35, t), 0.0)
		"morning":
			return Vector4(0.0, 0.0, lerpf(0.35, 0.15, t), 0.0)
	return Vector4.ZERO


func apply_chapter(chapter: String, seconds: float, duration: float, sound := true) -> void:
	if chapter != _chapter:
		_chapter = chapter
		_previous_time = -1.0
	var state := conditions(chapter, seconds, duration)
	cloud = state.x
	rain = state.y
	wet = state.z
	flash = state.w
	var cues := strikes(chapter)
	if sound:
		for cue in cues:
			if cue.time + cue.delay < duration - 0.7 and _previous_time < cue.time and seconds >= cue.time:
				_thunder_due.append(Vector2(_thunder_clock + cue.delay, cue.power))
	_previous_time = seconds
	RenderingServer.global_shader_parameter_set("rainfall", rain)
	RenderingServer.global_shader_parameter_set("scene_wetness", wet)
	RenderingServer.global_shader_parameter_set("lightning_flash", flash)
	if _rain_mesh != null:
		_rain_mesh.visible = rain > 0.001
		_rain_mesh.material_override.set_shader_parameter("rain_strength", rain)
	if _light != null:
		_light.visible = flash > 0.005
		_light.light_energy = flash * 7.0
		_bolt.visible = false
		for cue in cues:
			if strike_flash(seconds - cue.time) <= 0.01:
				continue
			# Only the local bolt geometry scales. Its ground position never
			# jumps because a width/intensity adjustment changed world coordinates.
			_bolt.position = cue.at
			_bolt.rotation.y = float(cue.time) * 0.37
			_bolt.scale = Vector3.ONE * lerpf(0.70, 1.0, cue.power)
			_bolt.visible = cue.fork and flash > 0.08
			_light.rotation = Vector3(deg_to_rad(-48), atan2(cue.at.x, cue.at.z), 0)
			RenderingServer.global_shader_parameter_set("lightning_position", _bolt.to_global(Vector3(0, 270, 0)))
		_bolt_material.emission_energy_multiplier = flash * 28.0


func _process(delta: float) -> void:
	_clock += delta
	_thunder_clock += delta
	var camera := get_viewport().get_camera_3d()
	if Game.camp != null and Game.camp.campsite != null and not _roofs_ready:
		var site: Campsite = Game.camp.campsite
		if site.tent != null and site.dock != null:
			var mat := _rain_mesh.material_override as ShaderMaterial
			mat.set_shader_parameter("tent_inverse", site.tent.global_transform.affine_inverse())
			mat.set_shader_parameter("dock_inverse", site.dock.global_transform.affine_inverse())
			mat.set_shader_parameter("dock_length", site.dock.total_length())
			_impacts = RainImpacts.new()
			add_child(_impacts)
			_impacts.build(Game.camp)
			_roofs_ready = true
	if _impacts != null:
		_impacts.update(_clock, rain)
	if camera != null and _rain_mesh.visible:
		_rain_mesh.global_position = Vector3(camera.global_position.x, 0, camera.global_position.z)
		_rain_mesh.material_override.set_shader_parameter("rain_clock", _clock)
	if Game.audio != null and Game.audio.is_ready():
		if _rain_audio == null:
			_build_audio()
		_rain_gain = move_toward(_rain_gain, rain, delta * 0.5)
		var sheltered := _chapter == "shelter_rain"
		if camera != null and Game.camp != null and Game.camp.campsite != null:
			var tent := Game.camp.campsite.tent as Tent
			if tent != null:
				sheltered = tent_shelter(tent.to_local(camera.global_position))
		_rain_shelter_mix = move_toward(_rain_shelter_mix, 1.0 if sheltered else 0.0, delta * 1.4)
		_rain_audio.volume_db = linear_to_db(maxf(0.0001, _rain_gain)) + lerpf(-8.0, -5.0, _rain_shelter_mix)
		_rain_filter.cutoff_hz = move_toward(_rain_filter.cutoff_hz, 2800.0 if sheltered else 9000.0, delta * 9000.0)
		for i in range(_thunder_due.size() - 1, -1, -1):
			if _thunder_clock >= _thunder_due[i].x:
				var voice := _thunder_voices[_thunder_voice % _thunder_voices.size()]
				_thunder_voice += 1
				voice.volume_db = (-12.0 if _chapter == "shelter_rain" else -8.0) + linear_to_db(maxf(_thunder_due[i].y, 0.3))
				voice.pitch_scale = 0.92 + float(_thunder_voice % 3) * 0.06
				voice.play()
				if Game.has_flag("film-quality") or Game.has_flag("cinematic"):
					print("THUNDER_CUE chapter=", _chapter, " frames_drawn=", Engine.get_frames_drawn())
				_thunder_due.remove_at(i)


func _build_rain() -> void:
	_rain_mesh = MultiMeshInstance3D.new()
	_rain_mesh.name = "Rain"
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	var quad := QuadMesh.new()
	quad.size = Vector2(0.011, 0.30)
	mm.mesh = quad
	mm.instance_count = 24000
	var rng := RandomNumberGenerator.new()
	rng.seed = 63009
	for i in mm.instance_count:
		mm.set_instance_transform(i, Transform3D(Basis.IDENTITY, Vector3(rng.randf_range(-26, 26), 0, rng.randf_range(-26, 26))))
		mm.set_instance_custom_data(i, Color(rng.randf(), rng.randf(), rng.randf(), rng.randf()))
	_rain_mesh.multimesh = mm
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/rain.gdshader")
	var field := TerrainField.new()
	var heights := Image.create(512, 512, false, Image.FORMAT_RF)
	for y in 512:
		for x in 512:
			var pos := Vector2(x, y) / 511.0 * 256.0 - Vector2.ONE * 128.0
			heights.set_pixel(x, y, Color(field.height(pos.x, pos.y), 0, 0))
	mat.set_shader_parameter("ground_height", ImageTexture.create_from_image(heights))
	_rain_mesh.material_override = mat
	_rain_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_rain_mesh.custom_aabb = AABB(Vector3(-40, -4, -40), Vector3(80, 24, 80))
	_rain_mesh.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	add_child(_rain_mesh)


func _build_lightning() -> void:
	_light = DirectionalLight3D.new()
	_light.name = "LightningLight"
	# Cool blue-white: a whiter flash pushed the wet foliage to lime green.
	_light.light_color = Color(0.66, 0.78, 1.0)
	# Godot temporal volumetrics ghost brief lights. Clouds get their own
	# synchronous emission; this light supplies real surface lighting/shadows.
	_light.light_volumetric_fog_energy = 0.0
	# SDFGI also converges across frames and feeds the fog through GI Inject.
	# Keep this millisecond pulse out of that delayed indirect-light history.
	_light.light_bake_mode = Light3D.BAKE_DISABLED
	_light.light_indirect_energy = 0.0
	_light.shadow_enabled = true
	_light.directional_shadow_max_distance = 150.0
	_light.sky_mode = DirectionalLight3D.SKY_MODE_LIGHT_ONLY
	_light.rotation = Vector3(deg_to_rad(-48), deg_to_rad(72), 0)
	add_child(_light)
	_bolt = MeshInstance3D.new()
	_bolt.name = "LightningFork"
	var mb := MeshBuilder.new()
	var rng := RandomNumberGenerator.new()
	rng.seed = 9451
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	for i in 25:
		var t := float(i) / 24.0
		points.append(Vector3(t * 20.0 + rng.randf_range(-7, 7), 270 * (1.0 - t), rng.randf_range(-7, 7)))
		radii.append(0.42)
	mb.add_tube(points, radii, 4)
	for fork in [7, 13, 17]:
		var branch: Array[Vector3] = [points[fork]]
		var widths: Array[float] = [0.13]
		for i in range(1, 7):
			branch.append(points[fork] + Vector3(i * -3.5 + rng.randf_range(-2, 2), i * -3.7, i * 1.1 + rng.randf_range(-2, 2)))
			widths.append(lerpf(0.13, 0.025, i / 6.0))
		mb.add_tube(branch, widths, 4)
	_bolt.mesh = mb.commit()
	_bolt_material = StandardMaterial3D.new()
	_bolt_material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	_bolt_material.albedo_color = Color(0.75, 0.83, 1.0)
	_bolt_material.emission_enabled = true
	_bolt_material.emission = Color(0.60, 0.72, 1.0)
	_bolt.material_override = _bolt_material
	_bolt.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(_bolt)


func _build_audio() -> void:
	var bus := AudioServer.get_bus_index(&"CampRain")
	if bus < 0:
		bus = AudioServer.bus_count
		AudioServer.add_bus()
		AudioServer.set_bus_name(bus, &"CampRain")
		AudioServer.set_bus_send(bus, AudioDirector.BUS_AMBIENCE)
	_rain_filter = AudioEffectLowPassFilter.new()
	_rain_filter.cutoff_hz = 9000.0
	AudioServer.add_bus_effect(bus, _rain_filter)
	_rain_audio = AudioStreamPlayer.new()
	_rain_audio.name = "RainSound"
	var rain_stream := preload("res://audio/rain.ogg").duplicate() as AudioStreamOggVorbis
	rain_stream.loop = true
	_rain_audio.stream = rain_stream
	_rain_audio.bus = &"CampRain"
	_rain_audio.volume_db = -80
	add_child(_rain_audio)
	_rain_audio.play()
	for i in 4:
		var voice := AudioStreamPlayer.new()
		voice.name = "ThunderSound%d" % i
		voice.stream = preload("res://audio/thunder.wav")
		voice.bus = AudioDirector.BUS_SFX
		add_child(voice)
		_thunder_voices.append(voice)


## Same A-frame roof used by rain clipping, now also used by the live listener.
## Shelter no longer depends on a cinematic chapter name during exploration.
static func tent_shelter(local: Vector3) -> bool:
	if absf(local.x) >= Tent.WIDTH * 0.5 or absf(local.z) >= Tent.LENGTH * 0.5 or local.y < -0.15:
		return false
	return local.y < Tent.HEIGHT * (1.0 - absf(local.x) / (Tent.WIDTH * 0.5))
